using Amazon;
using Amazon.Route53;
using Amazon.Route53.Model;
using Amazon.Runtime;
using SphereAlert.Models.DNSModels;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.APISupportedProviders
{
    /// <summary>
    /// AWS Route 53 DNS provider. Credential format: "AccessKeyId:SecretAccessKey".
    /// Uses the AWS SDK (AWSSDK.Route53). Adapted from SphereSSL's AWSRoute53Helper.
    /// </summary>
    public class AWSRoute53Provider : IAlertDnsProvider
    {
        private const int MinTtl = 60;

        private readonly string _accessKeyId;
        private readonly string _secretAccessKey;
        private readonly Logger _logger;

        public AWSRoute53Provider(string credentials, Logger logger)
        {
            var parts = (credentials ?? string.Empty).Split(':', 2);
            _accessKeyId = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            _secretAccessKey = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            _logger = logger;
        }

        private AmazonRoute53Client CreateClient()
        {
            var creds = new BasicAWSCredentials(_accessKeyId, _secretAccessKey);
            return new AmazonRoute53Client(creds, RegionEndpoint.USEast1);
        }

        public async Task<ConnectionTestResult> TestConnectionAsync()
        {
            try
            {
                using var client = CreateClient();
                await client.ListHostedZonesAsync(new ListHostedZonesRequest { MaxItems = "1" });
                return ConnectionTestResult.Ok();
            }
            catch (Exception ex)
            {
                return ConnectionTestResult.Fail(ex.Message);
            }
        }

        public async Task<List<ZoneInfo>> ListZonesAsync()
        {
            var zones = new List<ZoneInfo>();
            try
            {
                using var client = CreateClient();
                string? marker = null;
                while (true)
                {
                    var request = new ListHostedZonesRequest { Marker = marker };
                    var response = await client.ListHostedZonesAsync(request);
                    foreach (var zone in response.HostedZones)
                    {
                        zones.Add(new ZoneInfo
                        {
                            Name = zone.Name.TrimEnd('.'),
                            ZoneId = zone.Id.Replace("/hostedzone/", string.Empty)
                        });
                    }

                    if (response.IsTruncated && !string.IsNullOrEmpty(response.NextMarker))
                    {
                        marker = response.NextMarker;
                        continue;
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                _ = _logger.Debug($"Route53 ListZones exception: {ex.Message}");
            }
            return zones;
        }

        public async Task<UpsertResult> UpsertTxtRecordAsync(string zone, string name, string value, int? ttlSeconds)
        {
            try
            {
                using var client = CreateClient();

                string? zoneId = await GetZoneIdAsync(client, zone);
                if (string.IsNullOrEmpty(zoneId))
                    return UpsertResult.Fail($"No Route53 hosted zone found for '{zone}'.");

                long ttl = Math.Max(ttlSeconds ?? 120, MinTtl);
                string recordName = $"{name}.{zone}".TrimEnd('.') + ".";

                var request = new ChangeResourceRecordSetsRequest
                {
                    HostedZoneId = zoneId,
                    ChangeBatch = new ChangeBatch
                    {
                        Changes = new List<Change>
                        {
                            new Change
                            {
                                Action = ChangeAction.UPSERT,
                                ResourceRecordSet = new ResourceRecordSet
                                {
                                    Name = recordName,
                                    Type = RRType.TXT,
                                    TTL = ttl,
                                    ResourceRecords = new List<ResourceRecord>
                                    {
                                        // Route53 TXT values must be wrapped in double quotes.
                                        new ResourceRecord { Value = "\"" + value.Replace("\"", "\\\"") + "\"" }
                                    }
                                }
                            }
                        }
                    }
                };

                var response = await client.ChangeResourceRecordSetsAsync(request);
                if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
                {
                    _ = _logger.Info($"Route53 TXT upserted for {name}.{zone}.");
                    return UpsertResult.Ok();
                }

                _ = _logger.Debug($"Route53 upsert failed for {name}.{zone}: {response.HttpStatusCode}");
                return UpsertResult.Fail($"Route53 returned {(int)response.HttpStatusCode}.");
            }
            catch (Exception ex)
            {
                return UpsertResult.Fail(ex.Message);
            }
        }

        public async Task<DeleteResult> DeleteTxtRecordAsync(string zone, string name)
        {
            try
            {
                using var client = CreateClient();

                string? zoneId = await GetZoneIdAsync(client, zone);
                if (string.IsNullOrEmpty(zoneId))
                    return DeleteResult.Fail($"No Route53 hosted zone found for '{zone}'.");

                string recordName = $"{name}.{zone}".TrimEnd('.') + ".";

                // Route53 DELETE requires the exact current record set, so fetch it first.
                var existing = await GetRecordSetAsync(client, zoneId, recordName);
                if (existing is null)
                    return DeleteResult.Ok(); // Nothing to remove.

                var request = new ChangeResourceRecordSetsRequest
                {
                    HostedZoneId = zoneId,
                    ChangeBatch = new ChangeBatch
                    {
                        Changes = new List<Change>
                        {
                            new Change
                            {
                                Action = ChangeAction.DELETE,
                                ResourceRecordSet = existing
                            }
                        }
                    }
                };

                var response = await client.ChangeResourceRecordSetsAsync(request);
                if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
                    return DeleteResult.Ok();

                _ = _logger.Debug($"Route53 delete failed for {name}.{zone}: {response.HttpStatusCode}");
                return DeleteResult.Fail($"Route53 returned {(int)response.HttpStatusCode}.");
            }
            catch (Exception ex)
            {
                return DeleteResult.Fail(ex.Message);
            }
        }

        private static async Task<string?> GetZoneIdAsync(AmazonRoute53Client client, string zone)
        {
            string fqdn = zone.TrimEnd('.') + ".";
            string? bestMatchId = null;
            int bestMatchLength = -1;
            string? marker = null;

            while (true)
            {
                var response = await client.ListHostedZonesAsync(new ListHostedZonesRequest { Marker = marker });
                foreach (var z in response.HostedZones)
                {
                    if (fqdn.EndsWith(z.Name, StringComparison.OrdinalIgnoreCase) &&
                        z.Name.Length > bestMatchLength)
                    {
                        bestMatchId = z.Id.Replace("/hostedzone/", string.Empty);
                        bestMatchLength = z.Name.Length;
                    }
                }

                if (response.IsTruncated && !string.IsNullOrEmpty(response.NextMarker))
                {
                    marker = response.NextMarker;
                    continue;
                }
                break;
            }
            return bestMatchId;
        }

        private static async Task<ResourceRecordSet?> GetRecordSetAsync(
            AmazonRoute53Client client, string zoneId, string recordName)
        {
            var request = new ListResourceRecordSetsRequest
            {
                HostedZoneId = zoneId,
                StartRecordName = recordName,
                StartRecordType = RRType.TXT,
                MaxItems = "1"
            };
            var response = await client.ListResourceRecordSetsAsync(request);
            foreach (var rr in response.ResourceRecordSets)
            {
                if (rr.Type == RRType.TXT &&
                    string.Equals(rr.Name.TrimEnd('.'), recordName.TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
                {
                    return rr;
                }
            }
            return null;
        }
    }
}
