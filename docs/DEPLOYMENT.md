# Syxx Server — Deployment & Rollback Guide

> **Target**: Production server B6354A1 (IP `198.71.60.193`), path `/opt/SyxxServer`
> **Domain**: `syxx.app` (primary), `leak.syxx.app` (DNS collector)
> **Last updated**: 2026-08-11

---

## 1. Prerequisites

### Required

| Tool | Minimum Version | Notes |
|---|---|---|
| **Python** | 3.12+ | Server runtime |
| **uv** | latest | Package manager (`curl -LsSf https://astral.sh/uv/install.sh \| sh`) |
| **SSH** | any | Access to production server (`ssh root@198.71.60.193`) |
| **Git** | 2.30+ | Source checkout |

### Optional

| Tool | Purpose |
|---|---|
| **Docker** | 24+ | Container-based deployment |
| **Docker Compose** | 2.20+ | Orchestrated container deployment |
| **nginx** | any | Reverse proxy (auto-configured by `deploy.sh`) |
| **certbot** | any | Let's Encrypt TLS (auto-configured by `deploy.sh`) |

### Production server requirements

- Linux (any distro with `systemd`)
- 1 GB+ RAM, 10 GB+ disk
- Ports 53 (DNS), 80 (HTTP), 443 (HTTPS), 8080 (internal HTTP)
- Root or sudo access for `deploy.sh`

---

## 2. Local Development Setup

```bash
# Clone the repository
git clone https://github.com/milkandgin/Syxx.git
cd Syxx

# Install dependencies (Server lives in Server/)
cd Server
uv sync

# Run the server locally
uv run python server.py
# → HTTP on http://127.0.0.1:8080
# → DNS on 127.0.0.1:53 (may need sudo for privileged port)

# Run tests
cd Server
uv run pytest tests/ -v

# Run with specific config
SYXX_ENV=development SYXX_HTTP_PORT=9090 uv run python server.py
```

### Local dev environment variables

Set these in a `.env` file or export directly:

```bash
export SYXX_ENV=development
export SYXX_HTTP_PORT=9090       # avoid port conflict
export SYXX_DNS_PORT=5353        # avoid needing sudo
export SYXX_DEV_MODE=1           # enables dev-only fallbacks
export SYXX_FETCH_ALLOW_PRIVATE=1  # allows localhost fetches for testing
```

---

## 3. Production Deployment (Traditional — deploy.sh)

The primary deployment method is `scripts/deploy.sh`, which:

1. Creates a pre-deploy backup (for rollback)
2. Ships the entire `Server/` tree via tar-over-SSH
3. Installs Python runtime dependencies
4. Creates/updates the systemd service unit
5. Configures nginx + Let's Encrypt reverse proxy (optional)
6. Runs post-deploy health checks
7. **Auto-rolls back** if health checks fail

### 3.1 Quick deploy

```bash
# Minimum: set the server IP
SERVER_IP=198.71.60.193 ./scripts/deploy.sh

# Full: with all options
SERVER_IP=198.71.60.193 \
  DOMAIN=syxx.app \
  LETSENCRYPT_EMAIL=you@example.com \
  SETUP_TLS=1 \
  ./scripts/deploy.sh
```

### 3.2 Pre-deploy checklist

Before running `deploy.sh`, verify:

```bash
# 1. SSH access works
ssh root@198.71.60.193 'echo connected'

# 2. Local files exist
ls Server/index.html Server/server.py Server/syxx_server/

# 3. Tests pass locally
cd Server && uv run pytest tests/ -x -q

# 4. Git status is clean (or at least committed)
git status
```

### 3.3 Deployment configuration variables

Set these before running `deploy.sh`:

| Variable | Required | Default | Description |
|---|---|---|---|
| `SERVER_IP` | **Yes** | *(none)* | Target server IP address |
| `REMOTE_USER` | No | `root` | SSH user |
| `REMOTE_DIR` | No | `/opt/SyxxServer` | Deployment directory on server |
| `DOMAIN` | No | `syxx.app` | Domain for nginx/certbot |
| `LETSENCRYPT_EMAIL` | No | `jjwarne@icloud.com` | Email for Let's Encrypt |
| `SETUP_TLS` | No | `1` | Set `0` to skip nginx/TLS setup |

### 3.4 What deploy.sh does (step by step)

1. **SSH connection**: Opens a single shared SSH connection (ControlMaster) — you enter the password once.

2. **Pre-deploy backup** (F079): Creates a timestamped tarball of the current deployment at `/opt/SyxxServer.backups/syxx-deploy-YYYYMMDD-HHMMSS.tar.gz`. Keeps the 3 most recent backups.

3. **Upload**: Ships the full `Server/` tree via `tar | ssh tar` (not rsync — rsync may not be installed). Excludes `__pycache__`, `.venv`, `*.db`, and macOS metadata.

4. **Asset verification**: Confirms `index.html` and image assets landed on the server.

5. **Python dependencies**: Installs `cryptography` (for Ed25519 signing) using pip with PEP 668 fallback. Verifies by importing, not just exit code.

6. **Service user**: Creates an unprivileged `syxx` user (the daemon must not run as root).

7. **Environment file**: Creates `/etc/syxx/syxx.env` (mode 0600) on first deploy. This file persists across redeployments — your secrets are preserved.

8. **Systemd unit**: Installs `/etc/systemd/system/syxx-server.service` with extensive hardening (ProtectSystem=strict, CAP_NET_BIND_SERVICE, etc.).

9. **Health check**: Verifies the daemon is active AND `/api/pubkey` responds. If either fails, **automatic rollback** triggers.

10. **TLS** (optional): Installs nginx + certbot, obtains Let's Encrypt certificate, configures reverse proxy to `127.0.0.1:8080`.

### 3.5 Post-deploy verification

```bash
# Check service status
ssh root@198.71.60.193 'systemctl status syxx-server'

# Check the health endpoint
ssh root@198.71.60.193 'curl -fsS http://127.0.0.1:8080/healthz'

# Check the public key endpoint
curl -fsS https://syxx.app/api/pubkey

# Check DNS is serving
dig @198.71.60.193 leak.syxx.app A +short

# Check recent logs
ssh root@198.71.60.193 'journalctl -u syxx-server -n 50 --no-pager'
```

### 3.6 First-time secret configuration

After the first deploy, edit the environment file to add payment/licensing secrets:

```bash
ssh root@198.71.60.193 'nano /etc/syxx/syxx.env'
# Fill in:
#   STRIPE_SECRET_KEY=sk_live_...
#   STRIPE_WEBHOOK_SECRET=whsec_...
#   SYXX_KEYGEN_PUBLIC_KEY=<hex-encoded Ed25519 key>
#   SYXX_KEYGEN_ADMIN_TOKEN=prod-...v3   (PRODUCT token, not admin)
#   SYXX_KEYGEN_POLICY_MAC_MONTHLY=pol_...
#   ... (see Section 5 for full list)

# Restart after editing
ssh root@198.71.60.193 'systemctl restart syxx-server'
```

---

## 4. Production Deployment (Docker)

### 4.1 Build and run

```bash
cd Server

# Build the image
docker build -t syxx-server .

# Run with docker compose
docker compose up -d

# View logs
docker compose logs -f syxx-server

# Check health
docker compose ps
docker compose exec syxx-server python -c "import urllib.request; print(urllib.request.urlopen('http://localhost:8080/healthz').read())"
```

### 4.2 Docker Compose configuration

The `Server/docker-compose.yml` provides a production-ready setup with:

- **Port mapping**: `8080:8080` (HTTP), `5353:5353/udp` (DNS, non-privileged for rootless)
- **Persistent volume**: `syxx-data` at `/app/data` (SQLite DB + backups)
- **Health check**: `/healthz` every 30 seconds
- **Resource limits**: 512 MB memory, 2 CPU cores
- **Log rotation**: 10 MB per file, 5 files max
- **Non-root runtime**: Runs as `syxx` user inside the container

### 4.3 Docker environment variables

Edit `Server/docker-compose.yml` to uncomment and set production secrets:

```yaml
environment:
  SYXX_ENV: production
  # Uncomment for production:
  # STRIPE_SECRET_KEY: sk_live_...
  # STRIPE_WEBHOOK_SECRET: whsec_...
  # SYXX_KEYGEN_ADMIN_TOKEN: prod-...v3
  # SYXX_KEYGEN_WEBHOOK_SECRET: ...
  # SENTRY_DSN: https://...@o...ingest.sentry.io/...
```

Or use a `.env` file (create `Server/.env`):

```bash
STRIPE_SECRET_KEY=sk_live_...
STRIPE_WEBHOOK_SECRET=whsec_...
SYXX_KEYGEN_ADMIN_TOKEN=prod-...v3
SENTRY_DSN=https://...@o...ingest.sentry.io/...
```

### 4.4 Docker networking notes

- The container binds to `0.0.0.0:8080` internally (required for container networking)
- DNS uses port `5353` (non-privileged) instead of `53`
- For production, put nginx/caddy in front on the host mapping `80→8080` and `53→5353`
- TLS termination should happen at the reverse proxy level

### 4.5 Docker volume management

```bash
# List volumes
docker volume ls | grep syxx

# Backup the database
docker compose exec syxx-server cp /app/data/syxx-server.db /app/data/backups/manual-$(date +%Y%m%d).db

# Restore from backup
docker compose exec syxx-server cp /app/data/backups/manual-20260811.db /app/data/syxx-server.db
docker compose restart

# Inspect data directory
docker compose exec syxx-server ls -la /app/data/
```

---

## 5. Environment Variables

### Core

| Variable | Default | Description |
|---|---|---|
| `SYXX_ENV` | `development` | `production`, `staging`, or `development` |
| `SYXX_HTTP_HOST` | `127.0.0.1` | HTTP bind address (production: `0.0.0.0`) |
| `SYXX_HTTP_PORT` | `8080` | HTTP listen port |
| `SYXX_DNS_HOST` | `0.0.0.0` | DNS bind address |
| `SYXX_DNS_PORT` | `53` | DNS listen port (requires CAP_NET_BIND_SERVICE or root) |
| `SYXX_DNS_FALLBACK_PORT` | `5053` | Fallback DNS port when :53 is unavailable |
| `SYXX_STATIC_DIR` | *(auto)* | Directory containing `index.html` and static assets |
| `SYXX_DB_PATH` | `./syxx-server.db` | SQLite database path |

### Secrets (set in `/etc/syxx/syxx.env`, mode 0600)

| Variable | Required | Description |
|---|---|---|
| `SYXX_SESSION_SECRET` | Auto-generated | 64-char hex session signing secret |
| `SYXX_COLLECTOR_PRIVATE_KEY` | Yes (production) | Ed25519 private key for DNS leak attestation |
| `STRIPE_SECRET_KEY` | Yes (selling) | Stripe secret key (`sk_live_...`) |
| `STRIPE_WEBHOOK_SECRET` | Yes (selling) | Stripe webhook signing secret (`whsec_...`) |
| `SYXX_KEYGEN_PUBLIC_KEY` | **Yes** | Ed25519 verify key (hex) from Keygen Dashboard. Without this, ALL license validation is denied (fail-closed) |
| `SYXX_KEYGEN_ADMIN_TOKEN` | Yes (selling) | Keygen **product** token (`prod-...v3`) for minting licenses |
| `SYXX_KEYGEN_WEBHOOK_SECRET` | Optional | HMAC secret for Keygen license webhooks |
| `SYXX_ADMIN_TOKEN` | Optional | Bearer token for admin API endpoints |
| `SYXX_DB_ENCRYPTION_KEY` | Optional | AES-GCM key for at-rest encryption of sensitive DB entries |

### Keygen Policy IDs (in `/etc/syxx/syxx.env`)

| Variable | Description |
|---|---|
| `SYXX_KEYGEN_POLICY_MAC_MONTHLY` | macOS monthly subscription policy |
| `SYXX_KEYGEN_POLICY_MAC_YEARLY` | macOS yearly subscription policy |
| `SYXX_KEYGEN_POLICY_WIN_MONTHLY` | Windows monthly subscription policy |
| `SYXX_KEYGEN_POLICY_WIN_YEARLY` | Windows yearly subscription policy |
| `SYXX_KEYGEN_POLICY_DEB_MONTHLY` | Debian/Mint monthly subscription policy |
| `SYXX_KEYGEN_POLICY_DEB_YEARLY` | Debian/Mint yearly subscription policy |
| `SYXX_KEYGEN_POLICY_MAC_TRIAL` | macOS trial policy |
| `SYXX_KEYGEN_POLICY_WIN_TRIAL` | Windows trial policy |
| `SYXX_KEYGEN_POLICY_DEB_TRIAL` | Debian/Mint trial policy |
| `SYXX_KEYGEN_POLICY_MAC_PERM` | macOS perpetual license policy |
| `SYXX_KEYGEN_POLICY_WIN_PERM` | Windows perpetual license policy |
| `SYXX_KEYGEN_POLICY_DEB_PERM` | Debian/Mint perpetual license policy |

### Stripe Product/Price Overrides

| Variable | Description |
|---|---|
| `SYXX_STRIPE_PRODID_MAC_MONTHLY` | Override macOS monthly product ID |
| `SYXX_STRIPE_PRICEID_MAC_MONTHLY` | Override macOS monthly price ID |
| *(same pattern for `_MAC_YEARLY`, `_WIN_*`, `_DEB_*`)* | |

### SMTP (optional — leave `SYXX_SMTP_HOST` empty to disable)

| Variable | Default | Description |
|---|---|---|
| `SYXX_SMTP_HOST` | *(empty)* | SMTP server hostname |
| `SYXX_SMTP_PORT` | `587` | SMTP port |
| `SYXX_SMTP_STARTTLS` | `true` | `true` = STARTTLS on 587; `false` = implicit TLS on 465 |
| `SYXX_SMTP_USER` | *(empty)* | SMTP username |
| `SYXX_SMTP_PASSWORD` | *(empty)* | SMTP password |
| `SYXX_SMTP_TIMEOUT` | `15` | Connection timeout in seconds |
| `SYXX_SMTP_INSECURE_CERTS` | `false` | Allow self-signed certificates |
| `SYXX_MAIL_FROM` | `Syxx <noreply@syxx.app>` | From address |
| `SYXX_BETA_DIGEST_TO` | *(empty)* | Email for weekly signup digest |

### DNS Leak Collector

| Variable | Default | Description |
|---|---|---|
| `SYXX_LEAK_ZONES` | `leak.syxx.app,leak.zzyn.net` | Authoritative zones to monitor |
| `SYXX_LEAK_SINK_A` | `198.51.100.1` | IPv4 sink for leak detection (RFC 5737 TEST-NET-2) |
| `SYXX_LEAK_SINK_AAAA` | `::1` | IPv6 sink for leak detection |
| `SYXX_LEAK_TOKEN_TTL` | `900` | Leak token TTL in seconds |
| `SYXX_DNS_SOA_MNAME` | `ns1.syxx.app` | SOA primary nameserver |
| `SYXX_DNS_SOA_RNAME` | `hostmaster.syxx.app` | SOA responsible email |

### Observability

| Variable | Default | Description |
|---|---|---|
| `SENTRY_DSN` | *(empty)* | Sentry DSN for error tracking |
| `SYXX_OTLP_ENDPOINT` | `https://bugsmasher.syxx.app` | OpenTelemetry OTLP collector endpoint |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | *(empty)* | Alternative OTLP endpoint variable |

### TLS / DoT

| Variable | Default | Description |
|---|---|---|
| `SYXX_TLS_CERT` | *(empty)* | Path to TLS certificate for built-in HTTPS |
| `SYXX_TLS_KEY` | *(empty)* | Path to TLS private key |
| `SYXX_ENABLE_DOT` | `false` | Enable DNS-over-TLS listener |
| `SYXX_DOT_TLS_CERT` | *(empty)* | DoT certificate path (auto-set by deploy.sh) |
| `SYXX_DOT_TLS_KEY` | *(empty)* | DoT private key path (auto-set by deploy.sh) |
| `SYXX_DOT_PORT` | `853` | DoT listen port |

### Security & Rate Limiting

| Variable | Default | Description |
|---|---|---|
| `SYXX_ENFORCE_NATIVE` | `true` | Enforce licensing on native endpoints |
| `SYXX_ENFORCE_DOH` | `true` | Enforce licensing on DoH forwarder |
| `SYXX_DEV_MODE` | `false` | **Never enable in production** — disables fail-closed licensing |
| `SYXX_RATE_CAPACITY` | `30` | Token bucket capacity (requests) |
| `SYXX_RATE_REFILL_MILLIS` | `1000` | Token bucket refill rate (millitokens/second) |
| `SYXX_CORS_ALLOWED_ORIGINS` | `https://syxx.app,...` | Comma-separated CORS allowlist |

### Data Retention

| Variable | Default | Description |
|---|---|---|
| `SYXX_AUDIT_RETENTION_DAYS` | `7` | Purge audit rows older than N days (0 = disable) |
| `SYXX_PASSIVE_RETENTION_DAYS` | `30` | Purge passive observations older than N days |
| `SYXX_DB_BACKUP_KEEP` | `14` | Number of rotating DB backups to keep |
| `SYXX_DB_BACKUP_DIR` | *(auto)* | Backup directory (defaults to `<db-dir>/backups`) |

---

## 6. Rollback Procedures

### 6.1 Identify the current version

```bash
# Traditional deployment — check when the deployment directory was last updated
ssh root@198.71.60.193 'ls -la /opt/SyxxServer/server.py && stat /opt/SyxxServer/server.py'

# Check running process start time
ssh root@198.71.60.193 'systemctl status syxx-server | grep Active'

# Check recent git commits deployed
ssh root@198.71.60.193 'cat /opt/SyxxServer/.deploy-info 2>/dev/null || echo "no deploy info file"'

# Docker deployment — check running image
docker compose ps
docker inspect syxx-server --format '{{.Created}}'
```

### 6.2 List available backups

```bash
# Traditional deployment backups
ssh root@198.71.60.193 'ls -lah /opt/SyxxServer.backups/'

# Expected output:
# -rw-r--r-- 1 root root  15M Aug 10 14:32 syxx-deploy-20260810-143200.tar.gz
# -rw-r--r-- 1 root root  14M Aug 09 09:15 syxx-deploy-20260809-091500.tar.gz
# -rw-r--r-- 1 root root  14M Aug 08 16:45 syxx-deploy-20260808-164500.tar.gz
```

### 6.3 Rollback — Traditional Deployment

**Automatic rollback** is built into `deploy.sh`: if the post-deploy health check fails (service won't start or `/api/pubkey` doesn't respond), the script automatically restores from the backup created at the start of that deployment.

**Manual rollback** when automatic rollback didn't trigger (e.g., you discover a bug hours later):

```bash
# Step 1: SSH to the server
ssh root@198.71.60.193

# Step 2: Stop the service
systemctl stop syxx-server

# Step 3: List available backups
ls -lah /opt/SyxxServer.backups/

# Step 4: Pick the backup to restore (e.g., the one before the broken deploy)
BACKUP=/opt/SyxxServer.backups/syxx-deploy-20260810-143200.tar.gz

# Step 5: Clear the deployment directory
find /opt/SyxxServer -mindepth 1 -delete

# Step 6: Extract the backup
tar xzf "$BACKUP" -C /opt/SyxxServer

# Step 7: Fix ownership
chown -R syxx /opt/SyxxServer

# Step 8: Restart the service
systemctl restart syxx-server

# Step 9: Verify
sleep 3
systemctl is-active --quiet syxx-server && echo "✅ Service active" || echo "❌ Service failed"
curl -fsS http://127.0.0.1:8080/api/pubkey && echo "✅ API responding" || echo "❌ API not responding"
```

**One-liner rollback** (copy-paste ready):

```bash
ssh root@198.71.60.193 'bash -s' <<'EOF'
set -e
BACKUP=$(ls -1t /opt/SyxxServer.backups/syxx-deploy-*.tar.gz 2>/dev/null | head -1)
[ -z "$BACKUP" ] && echo "❌ No backups found" && exit 1
echo "Rolling back to: $BACKUP"
systemctl stop syxx-server
find /opt/SyxxServer -mindepth 1 -delete
tar xzf "$BACKUP" -C /opt/SyxxServer
chown -R syxx /opt/SyxxServer
systemctl restart syxx-server
sleep 3
systemctl is-active syxx-server && echo "✅ Rollback complete"
EOF
```

### 6.4 Rollback — Docker Deployment

```bash
# Option A: Revert to previous image tag (if you tagged before deploying)
docker compose down
docker compose up -d --build   # rebuilds from current source

# Option B: Use docker compose rollback pattern
# 1. Stop the container
docker compose stop syxx-server

# 2. The syxx-data volume persists — database is unaffected

# 3. Rebuild from a known-good git commit
git checkout <known-good-commit>
docker compose build --no-cache
docker compose up -d

# 4. Verify
docker compose ps
docker compose logs --tail 20 syxx-server
curl -fsS http://localhost:8080/healthz
```

### 6.5 Database Migration Rollback Considerations

The Syxx server uses SQLite with a single file (`syxx-server.db`). There are no formal migration scripts — the schema is managed by `db.py` which creates/alters tables on startup.

**Key points:**

- **Backups are automatic**: The server creates rotating online backups in `SYXX_DB_BACKUP_DIR` (default: `<db-dir>/backups`), keeping `SYXX_DB_BACKUP_KEEP` (default: 14) copies.
- **Pre-deploy backups**: `deploy.sh` backs up the entire deployment directory (including the DB file) before each deploy.
- **Rollback restores the DB**: When you roll back to a previous deployment backup, the SQLite DB is also restored to its state at that point.

**To restore just the database** (without rolling back code):

```bash
# Traditional: restore DB from pre-deploy backup
ssh root@198.71.60.193 'systemctl stop syxx-server && \
  tar xzf /opt/SyxxServer.backups/syxx-deploy-20260810-143200.tar.gz -C /tmp/restore ./syxx-server.db && \
  cp /tmp/restore/syxx-server.db /opt/SyxxServer/syxx-server.db && \
  chown syxx /opt/SyxxServer/syxx-server.db && \
  systemctl restart syxx-server'

# Docker: restore DB from volume backup
docker compose exec syxx-server cp /app/data/backups/syxx-server.db.bak /app/data/syxx-server.db
docker compose restart syxx-server
```

---

## 7. Monitoring

### Health checks

| Endpoint | URL | Purpose |
|---|---|---|
| Process liveness | `https://syxx.app/healthz` | Returns 200 if process is alive (no DB/DNS dependency) |
| Public key | `https://syxx.app/api/pubkey` | Returns Ed25519 public key — proves crypto stack works |
| DNS health | `dig @198.71.60.193 leak.syxx.app A` | Verifies DNS listener is serving |

### Sentry

Sentry is self-hosted (deployed via `scripts/deploy-sentry.sh`). Four projects:

| Project | Platform | DSN env var |
|---|---|---|
| `syxx-macos` | Cocoa (Swift) | `SENTRY_DSN` in macOS app |
| `syxx-windows` | C# (.NET) | `SENTRY_DSN` in Windows app |
| `syxx-linux` | C# (.NET) | `SENTRY_DSN` in Linux app |
| `syxx-server` | Python | `SENTRY_DSN` in server env |

Set the server DSN in `/etc/syxx/syxx.env`:

```bash
SENTRY_DSN=https://examplePublicKey@o0.ingest.sentry.io/0
```

### OpenTelemetry

The server exports OTLP traces and logs to the configured endpoint:

```bash
# In /etc/syxx/syxx.env or docker-compose.yml
SYXX_OTLP_ENDPOINT=https://bugsmasher.syxx.app
```

### Systemd journal

```bash
# Live log tailing
ssh root@198.71.60.193 'journalctl -u syxx-server -f'

# Recent errors
ssh root@198.71.60.193 'journalctl -u syxx-server --since "1 hour ago" -p err'

# Last 100 lines
ssh root@198.71.60.193 'journalctl -u syxx-server -n 100 --no-pager'
```

### Docker logs

```bash
docker compose logs -f syxx-server
docker compose logs --since 1h syxx-server
```

---

## 8. Troubleshooting

### Service won't start

```bash
# Check systemd status and recent logs
ssh root@198.71.60.193 'systemctl status syxx-server && journalctl -u syxx-server -n 50 --no-pager'

# Common causes:
# 1. Python import error — missing dependency
ssh root@198.71.60.193 'cd /opt/SyxxServer && python3 -c "import syxx_server"'

# 2. Port already in use
ssh root@198.71.60.193 'ss -tlnp | grep :8080 && ss -ulnp | grep :53'

# 3. Permission denied (service user can't read files)
ssh root@198.71.60.193 'ls -la /opt/SyxxServer/ | head -5'
ssh root@198.71.60.193 'chown -R syxx /opt/SyxxServer'

# 4. Environment file syntax error
ssh root@198.71.60.193 'cat /etc/syxx/syxx.env | grep -v "^#" | grep -v "^$"'
# Each line must be NAME=value (no export, no spaces around =)
```

### SSL / TLS certificate issues

```bash
# Check certificate expiry
ssh root@198.71.60.193 'openssl x509 -in /etc/letsencrypt/live/syxx.app/fullchain.pem -noout -dates'

# Renew manually
ssh root@198.71.60.193 'certbot renew --force-renewal && systemctl reload nginx'

# Check nginx config
ssh root@198.71.60.193 'nginx -t'

# If DoT fails to load TLS cert, check syxx-cert group membership
ssh root@198.71.60.193 'id syxx | grep syxx-cert'
# If missing: usermod -aG syxx-cert syxx && systemctl restart syxx-server
```

### Database locked (SQLite busy)

```bash
# Check for stale lock files
ssh root@198.71.60.193 'ls -la /opt/SyxxServer/syxx-server.db*'

# Remove WAL/SHM files (safe if service is stopped)
ssh root@198.71.60.193 'systemctl stop syxx-server && \
  rm -f /opt/SyxxServer/syxx-server.db-wal /opt/SyxxServer/syxx-server.db-shm && \
  systemctl restart syxx-server'
```

### All license keys rejected (402)

```bash
# Most likely cause: SYXX_KEYGEN_PUBLIC_KEY is blank in /etc/syxx/syxx.env
# The server fails CLOSED — without the public key, every validation is denied.
ssh root@198.71.60.193 'grep KEYGEN_PUBLIC /etc/syxx/syxx.env'

# Fix: add your Keygen account's Ed25519 verify key (hex-encoded)
# Find it at: Keygen Dashboard → Settings → Public Key
# Or: curl https://api.keygen.sh/v1/accounts/4b50cc83-1664-4626-99b1-fbd4e76e35c3/public-key

# After editing:
ssh root@198.71.60.193 'systemctl restart syxx-server'
```

### /api/pubkey returns 500

```bash
# The cryptography package is not importable
ssh root@198.71.60.193 'python3 -c "import cryptography; print(cryptography.__version__)"'

# Fix:
ssh root@198.71.60.193 'python3 -m pip install --break-system-packages -r /opt/SyxxServer/requirements.txt'
ssh root@198.71.60.193 'systemctl restart syxx-server'
```

### Port conflicts

```bash
# Check what's using ports 53 and 8080
ssh root@198.71.60.193 'ss -tlnp | grep -E ":53 |:8080"'
ssh root@198.71.60.193 'ss -ulnp | grep ":53"'

# Common culprit: systemd-resolved on port 53
ssh root@198.71.60.193 'systemctl status systemd-resolved'
# Fix: disable it or change Syxx DNS to fallback port 5053

# Common culprit: another web server on 8080
ssh root@198.71.60.193 'lsof -i :8080'
```

### DNS not resolving from outside

```bash
# Verify DNS is listening on all interfaces
ssh root@198.71.60.193 'ss -ulnp | grep python'

# Test from the server itself (loopback)
ssh root@198.71.60.193 'dig @127.0.0.1 -p 53 leak.syxx.app A +short'

# Test from outside
dig @198.71.60.193 leak.syxx.app A +short

# Check firewall
ssh root@198.71.60.193 'iptables -L -n | grep 53'
# Or: ufw status
```

### Sentry deploy-sentry.sh troubleshooting

```bash
# Sentry not reachable after deploy
ssh root@<sentry-server> 'cd /opt/sentry && docker compose ps'
ssh root@<sentry-server> 'cd /opt/sentry && docker compose logs --tail 30'

# Health check
ssh root@<sentry-server> 'curl -sf http://localhost:9000/_health/'

# Admin password lost — retrieve from file
ssh root@<sentry-server> 'cat /root/.syxx-sentry-admin'
```
