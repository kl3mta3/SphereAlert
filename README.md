# SphereAlert

SphereAlert is a self-hosted, Dockerized web app that pushes site-wide alert banners to
your websites by updating a single DNS TXT record. Drop one small script onto each site,
and from then on you control every banner from one dashboard: pick the affected domains,
choose a level, type a message, optionally set an end time, and hit send — SphereAlert
fans the change out to each domain's DNS provider in parallel. Visitors see the banner on
their next page load. It is the sibling product to [SphereSSL](https://github.com/kl3mta3/SphereSSL):
same stack, same look, same trust model — your DNS API keys never leave your box, and
SphereAlert never phones home.

---

## How it works

1. SphereAlert writes a TXT record at `alert.<domain>` with a value like
   `::low:: Scheduled maintenance until 9am CST`.
2. The `sphere-alert.js` script on each site reads that TXT record via DNS-over-HTTPS on
   every page load and renders (or hides) a banner accordingly.
3. When an alert has an end time, a background scheduler rewrites the TXT record to
   `::none::` when that time arrives. The website itself never tracks time or expiry —
   it just shows whatever the record currently says.

```
                +---------------------------+
   operator --> |  SphereAlert  (:7227)     |
                |  - web UI                 |
                |  - SQLite (data volume)   |
                |  - 60s expiry scheduler   |
                +-------------+-------------+
                              |  upsert TXT  alert.<domain>
                              v
                +---------------------------+
                |  DNS provider API         |   (Cloudflare, Route53, …)
                +-------------+-------------+
                              |  TXT record
                              v
   visitor's browser  <--  sphere-alert.js  <--  DNS-over-HTTPS lookup
        renders banner
```

---

## Quick start

SphereAlert listens on port **7227** inside the container. Map it to whatever you like
on the host.

### Docker Compose (recommended)

```bash
git clone https://github.com/kl3mta3/SphereAlert.git
cd SphereAlert
docker compose up -d --build
```

```yaml
# docker-compose.yml
services:
  spherealert:
    image: spherealert:latest
    build: .
    ports:
      - "7227:7227"
    volumes:
      - spherealert-data:/data
    environment:
      SPHEREALERT_DATA_DIR: /data
      SPHEREALERT_LOG_LEVEL: Info
    restart: unless-stopped
    container_name: spherealert

volumes:
  spherealert-data:
```

### docker run

```bash
docker build -t spherealert:latest .
docker run -d --name spherealert \
  -p 7227:7227 \
  -v spherealert-data:/data \
  -e SPHEREALERT_DATA_DIR=/data \
  --restart unless-stopped \
  spherealert:latest
```

### Data persistence

The database, the encryption keyfile, and logs live in the **named Docker volume**
`spherealert-data`. Docker creates it automatically on first `up` and keeps it in its
own storage — **outside the project folder**. It survives image rebuilds, container
recreation, redeploys, fresh `git clone`s, and `git clean`. Operators just run the
container and sign in; nothing else to set up.

Only an explicit `docker compose down -v` (or `docker volume rm spherealert-data`)
deletes it. To back it up:

```bash
docker run --rm -v spherealert-data:/data -v "$(pwd):/backup" \
  busybox tar czf /backup/spherealert-backup.tgz -C /data .
```

> A bind mount like `./data:/data` is **not** recommended: it puts the database inside
> the project folder, where a redeploy that re-clones or cleans the repo will erase it.

| Environment variable   | Default  | Purpose                                  |
|------------------------|----------|------------------------------------------|
| `SPHEREALERT_DATA_DIR` | `data`   | Directory for the database, keyfile, log |
| `SPHEREALERT_LOG_LEVEL`| `Info`   | `Info` or `Debug`                        |

### Running without Docker

```bash
dotnet run --project SphereAlert/SphereAlert.csproj -c Release
```

---

## First login

1. Open `http://<host>:7227/`.
2. Sign in with the default credentials:
   - **Username:** `admin`
   - **Password:** `pass123`
3. You will be required to set a new password before you can do anything else. The new
   password must be 8–64 characters and include an uppercase letter, a lowercase letter,
   and a number.

That's it — there is one admin account, no public sign-up, no other roles.

---

## Adding a DNS provider

**Providers** (in the sidebar) → fill in the *Add a Provider* form:

1. Pick the **provider type** (Cloudflare, AWS Route 53, DigitalOcean, and 11 more).
2. Give it a **display name** (e.g. "Production Cloudflare").
3. Paste the **API credentials**. The form shows the expected format for the selected
   type — most providers use a single token; some use a `key:secret` pair.
4. Save. Credentials are encrypted with AES-256-GCM before they touch the database.
5. Use **Test** on the provider row to verify the credentials reach the provider's API.

Then go to **Domains** → **Refresh domains** next to the provider to import its zones,
or add a domain manually.

---

## Pushing an alert

**New Alert** (in the sidebar):

1. Choose a **level**: `info`, `low`, `medium`, `high`, or `critical`.
2. Type a **message** (up to 240 characters). A live preview shows exactly what the
   banner — and the JSON TXT record value — will look like.
3. Set **dismissable** and **force scroll-on-hover** as needed.
4. Optionally set an **end time**. Leave it blank and the alert stays up until you clear
   it manually.
5. Select one or more **domains**, and for each domain pick which **slot** (1, 2, or 3)
   the banner goes to.
6. **Send Alert.** SphereAlert writes `alert[N].<domain>` TXT records to every selected
   domain's provider in parallel and shows you a per-domain result screen — which
   succeeded, which failed, and why. Failed domains can be retried from that screen.

Manage live alerts under **Active**: edit the message/level/end time, or **Clear Now**
to remove the banner immediately. Every push, clear, and expiry is recorded under
**History**.

---

## Installing the script on a site

The banners only appear once `sphere-alert.js` is on the site. Two ways:

### Manual

1. Download the script from your running container: `http://<host>:7227/js/sphere-alert.js`
2. Place it in a `js/` folder at the root of your site (`js/sphere-alert.js`).
3. Add this tag inside `<head>` (or `<body>`):

   ```html
   <script src="/js/sphere-alert.js" defer></script>
   ```

### Zip injection (Tools → Install)

Upload your site's build output as a `.zip`. SphereAlert adds the script tag to every
HTML file that doesn't already have it, drops `sphere-alert.js` into a `js/` folder
(creating it if absent), and hands back a repackaged `.zip` for you to re-upload to your
host. There is no FTP/SSH integration by design — you stay in control of the deploy.

---

## Architecture

```
SphereAlert/
  Program.cs / Services/Config/StartUp.cs   bootstrap + DI + Kestrel (:7227)
  Controllers/ScriptController.cs           GET /js/sphere-alert.js, GET /healthz
  Pages/                                    Razor Pages UI (login, dashboard, compose,
                                            active, history, domains, providers, install)
  Data/Database/DatabaseManager.cs          SQLite schema + versioned migration
  Data/Repositories/                        Users, Providers, Domains, Alerts, History
  Models/                                   plain DTO/record types
  Services/Security/                        PasswordService (PBKDF2), CryptoService (AES-GCM)
  Services/APISupportedProviders/           IAlertDnsProvider + 14 provider adapters
  Services/Alerts/AlertService.cs           push / re-push / clear / expire orchestration
  Services/Scheduler/AlertSchedulerService  60s background worker that expires alerts
  Services/Scripts/                         serve script, zip injection, install detection
  Services/Domains/DomainImportService.cs   import zones from a provider
```

- **Stack:** ASP.NET Core 8 / C#, Razor Pages, embedded SQLite, Bootstrap-based UI.
- **Auth:** single admin role, session-based, forced password change on first login.
- **Storage:** one SQLite file in the data volume; schema is versioned with a startup
  migration step.
- **DNS providers:** Cloudflare, AWS Route 53, DigitalOcean, Hetzner, Namecheap, GoDaddy,
  DNS Made Easy, Porkbun, Gandi, ClouDNS, DreamHost, Vultr, Linode, DuckDNS — all adapted
  from SphereSSL's DNS-01 challenge writers to upsert arbitrary TXT values at
  `alert.<domain>`.
- **Health:** `GET /healthz` returns `ok`; the container `HEALTHCHECK` polls it.

### Slots and the TXT record format

Each domain has **three alert slots**, read from `alert.<domain>`, `alert2.<domain>`,
and `alert3.<domain>` — they render stacked, top to bottom. When you push an alert you
pick a slot per domain.

`sphere-alert.js` expects each slot's TXT record to be a JSON object:

```json
{"l":2,"m":"Maintenance Sat 7am-9am","d":1,"s":0}
```

| Field | Required | Meaning                                                            |
|-------|----------|--------------------------------------------------------------------|
| `l`   | yes      | level — `0` info, `1` low, `2` medium, `3` high, `4` critical      |
| `m`   | yes      | message text (≤ 240 chars)                                         |
| `d`   | no       | dismissable — `1` yes (default), `0` no                            |
| `s`   | no       | force scroll-on-hover — `1` yes, `0` auto (default)                |

Any value that is not valid JSON renders no banner. Clearing or expiring an alert
replaces the record with a human-readable note — `Cleared <timestamp> UTC — previous:
<message>` — so the record doubles as an audit trail. SphereAlert tracks the current
value of every slot; open a domain to see what is live in each one.

---

Built by Kenneth Lasyone ([kl3mta3](https://github.com/kl3mta3)). Part of the Sphere
product family. Self-hosted, privacy-first, operator-owned.
