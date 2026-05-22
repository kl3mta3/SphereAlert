# SphereAlert

**Push site-wide alert banners to any number of websites by changing one DNS record.**

SphereAlert is a self-hosted, Dockerized web app for operators who need to put a banner
on their sites: maintenance windows, incidents, notices; without redeploying anything.
You manage alerts from one dashboard; SphereAlert writes a TXT record to each domain's
DNS provider, and a tiny client script on each site reads that record and renders the
banner. Your DNS API keys never leave your box, and SphereAlert never phones home.

It is the sibling product to [SphereSSL](https://github.com/kl3mta3/SphereSSL) — same
stack, same conventions, same self-hosted philosophy.

---

## How it works

1. You run SphereAlert as a container and add your DNS provider credentials.
2. Just add `<script src="https://cdn.jsdelivr.net/gh/kl3mta3/SphereAlert@main/js/sphere-alert.js"></script>` to the `<head></head>` of pages you want to show alerts.

1. To raise an alert: pick the domains, a level, and a slot, type a message, hit **Send**.
2. SphereAlert writes a TXT record at `alert.<domain>` (or `alert2`/`alert3`) — a small
   JSON payload — to every selected domain's provider, in parallel.
3. Visitors' browsers read that record over DNS-over-HTTPS on each page load and render
   the banner. A background scheduler clears alerts that have an end time.

```
  operator ──> SphereAlert ──> DNS provider API ──> TXT record at alert.<domain>
   (web UI)    (:7227)                                      │
                                                            ▼
                          visitor's browser  <──  sphere-alert.js reads it
                                renders banner
```

---

## Quick start

SphereAlert listens on port **7227** in the container.

```bash
git clone https://github.com/kl3mta3/SphereAlert.git
cd SphereAlert
docker compose up -d --build
```

Then open `http://<host>:7227/`.

`docker-compose.yaml`:

```yaml
services:
  spherealert:
    build: .
    ports:
      - "7227:7227"
    environment:
      - SPHEREALERT_DATA_DIR=/data
      - SPHEREALERT_LOG_LEVEL=Info
    volumes:
      - spherealert-data:/data
    restart: unless-stopped

volumes:
  spherealert-data:
```

### Data persistence

The database, the encryption keyfile, and logs live in the **named volume
`spherealert-data`**, mounted at `/data`. It is created automatically and is kept across
rebuilds and redeploys — Docker (and PaaS platforms like Coolify) preserve named volumes
declared in the compose file. Don't hardcode `container_name`, and don't use a bind mount
to a path inside the repo — a redeploy that re-clones the repo would wipe it.

> Deploying with **Coolify**: use the *Docker Compose* build pack and point it at
> `docker-compose.yaml`. The `spherealert-data` volume appears under the app's
> **Persistent Storage** tab and survives every redeploy.

### Configuration

| Environment variable    | Default | Purpose                                     |
|-------------------------|---------|---------------------------------------------|
| `SPHEREALERT_DATA_DIR`  | `/data` | Directory for the database, keyfile, logs   |
| `SPHEREALERT_LOG_LEVEL` | `Info`  | `Info` or `Debug`                           |

---

## First login

Open the app and sign in with the seed credentials:

- **Username:** `admin`
- **Password:** `pass123`

You'll be required to set a **new username and password** before you can do anything
else. There is one admin account — no sign-up, no other roles.

---

## Using SphereAlert

### Add a DNS provider

**Providers** → fill in the *Add a Provider* form: pick the type, give it a name, paste
the API credentials (the form shows the expected format per provider). Credentials are
encrypted with **AES-256-GCM** before they touch the database. Use **Test** to verify
they reach the provider's API.

Supported: Cloudflare, AWS Route 53, DigitalOcean, Hetzner, Namecheap, GoDaddy,
DNS Made Easy, Porkbun, Gandi, ClouDNS, DreamHost, Vultr, Linode, DuckDNS.

### Add domains

**Domains** → **Refresh** next to a provider to import its zones, or add one by hand.
The list paginates, with checkboxes for bulk delete and bulk script-install checks.

### Push an alert

**New Alert** → choose a **level** and a **slot**, write a **message** (≤ 240 chars),
set **dismissable** / **scroll** options, optionally set an **end time**, pick the
**domains**, and **Send**. A live preview shows the exact banner and TXT value. The
result screen reports success/failure per domain, with retry for failures.

Manage live alerts under **Active** (edit, or clear immediately). Every push, clear, and
expiry is recorded in **History**.

### Install the client script

The banners only render once `sphere-alert.js` is on the site:

- **Auto:** On your site, add `<script src="https://cdn.jsdelivr.net/gh/kl3mta3/SphereAlert@main/js/sphere-alert.js"></script>` inside `<head>`.

- **Manual:** download it from `http://<host>:7227/js/sphere-alert.js` or the git, place it at
  `js/sphere-alert.js` on your site, and add
  `<script src="/js/sphere-alert.js" defer></script>` inside `<head>`.
  
- **Zip injection** (*Install* page): upload your site's build `.zip`; SphereAlert adds
  the tag to every HTML file and drops the script into a `js/` folder, then hands back a
  repackaged `.zip`.

---

## The TXT record format

Each domain has **three slots** — `alert`, `alert2`, `alert3` — rendered stacked. Each
slot's TXT record is a JSON object:

```json
{"l":2,"m":"Maintenance Sat 7am-9am","d":1,"s":0}
```

| Field | Required | Meaning                                                       |
|-------|----------|---------------------------------------------------------------|
| `l`   | yes      | level — `0` info, `1` low, `2` medium, `3` high, `4` critical |
| `m`   | yes      | message text (≤ 240 chars)                                    |
| `d`   | no       | dismissable — `1` yes (default), `0` no                       |
| `s`   | no       | force scroll-on-hover — `1` yes, `0` auto (default)           |

Anything that isn't valid JSON renders no banner. Clearing or expiring an alert replaces
the record with a `Cleared <timestamp> — previous: <message>` note, so the record itself
is an audit trail.

---

## Architecture

- **Stack:** ASP.NET Core 8 / C#, Razor Pages, embedded SQLite, Docker.
- **Auth:** single admin, session-based, forced username/password change on first login.
- **Security:** passwords hashed with PBKDF2-SHA256; provider credentials encrypted at
  rest with AES-256-GCM (the key lives in a keyfile beside the database).
- **Scheduler:** a background worker expires timed alerts every 60 seconds.
- **Endpoints:** `GET /js/sphere-alert.js` (the client script), `GET /healthz`.

```
SphereAlert/
  Controllers/        script + health endpoints
  Pages/              Razor Pages UI
  Data/               SQLite schema + repositories
  Models/             record/DTO types
  Services/
    APISupportedProviders/   IAlertDnsProvider + 14 provider adapters
    Alerts/                  push / clear / expire orchestration
    Scheduler/               60s expiry worker
    Scripts/                 serve script, zip injection, install detection
    Security/                password hashing, credential encryption
```

---

## License

[MIT](LICENSE) © 2026 Kenneth Lasyone

Part of the Sphere family of self-hosted, operator-owned infrastructure tools.
