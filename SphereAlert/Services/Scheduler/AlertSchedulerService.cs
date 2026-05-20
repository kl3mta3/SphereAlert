using SphereAlert.Data.Repositories;
using SphereAlert.Services.Alerts;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.Scheduler
{
    /// <summary>
    /// Background worker that expires timed alerts. Every 60 seconds it finds active
    /// alerts whose EndAt has passed, pushes ::none:: to their domains, and marks them
    /// expired. Alerts with EndAt = NULL are never touched — they persist until cleared.
    /// </summary>
    public class AlertSchedulerService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly Logger _logger;

        public AlertSchedulerService(IServiceScopeFactory scopeFactory, Logger logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _logger.Info("Alert scheduler started (60s interval).");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var alertRepository = scope.ServiceProvider.GetRequiredService<AlertRepository>();
                    var alertService = scope.ServiceProvider.GetRequiredService<AlertService>();

                    var dueAlerts = await alertRepository.GetAlertsToExpireAsync();
                    foreach (var alert in dueAlerts)
                    {
                        await alertService.ExpireAlertAsync(alert.AlertId);
                        await _logger.Info($"Alert {alert.AlertId} reached its end time and was cleared.");
                    }
                }
                catch (Exception ex)
                {
                    await _logger.Error($"Scheduler tick failed: {ex.Message}");
                }

                // Always delay, even after an error, so a failure can't spin the loop.
                try
                {
                    await Task.Delay(Interval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
