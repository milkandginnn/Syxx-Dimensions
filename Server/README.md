# Syxx Server

Backend for the paid, server-reliant Syxx features (**Warp**, **Leak**) and the
paywalled **web query** API behind `syxx.app`. Stdlib-only Python 3.8+ — no pip
dependencies — so it runs anywhere `python3` does.

```
python3 server.py     # DNS collector (thread) + HTTP API/website (foreground)
```

## The paywall

Every server-reliant capability is a **paid** route. A caller is entitled only
if it presents one of:

* a **session token** (`Authorization: Bearer …`) minted by `/api/auth/activate`
  after a license validated — this is the website's path, **or**
* a **license key** (`X-Syxx-License:` header or `?key=`) validated live against
  the same Keygen account the app uses, **or**
* an **offline/admin license** in `SYXX_LICENSE_KEYS` or the `licenses` table.

Because `/api/investigate`, `/api/passive-dns`, and `/api/leak/*` are all paid,
**the website cannot be used to run the server-reliant search for free** — an
unactivated visitor gets `402 Payment Required` and only the activation field.
Public routes are limited to the marketing page, `/api/stats`, health checks,
and `/api/auth/activate` itself.

## Routes

| Route | Class | Purpose |
|-------|-------|---------|
| `GET /`, `/syxx-favicon.svg` | public | website + favicon |
| `GET /assets/*` | public | website images (logos, screenshots) — allowlisted dir, traversal-safe |
| `GET /healthz`, `/readyz`, `/metrics` | public | liveness / readiness / counters |
| `GET /api/stats` | public | passive-DNS record count (marketing) |
| `POST /api/auth/activate` | public | license key → session token |
| `POST /api/beta/reserve` | public | Nnyx/Zzyn beta signup (persists; emails a confirmation) |
| `GET /warp?url=` | **paid** | **retired (410)** — unsigned baseline; use `GET /api/warp/manifest` (signed, WARP_MANIFEST_V1) |
| `GET /fetch_script?url=`, `GET /warp/fetch_script?url=` | **paid** | single script fetch (app; both paths routed) |
| `GET /leak?token=` | **paid** | resolver readout for a raw token (app) |
| `GET /api/investigate?domain=` | **paid** | multi-vantage web investigation |
| `GET /api/passive-dns?domain=` | **paid** | passive-DNS history |
| `POST /api/leak/token` | **paid** | issue a signed one-time leak token |
| `GET /api/leak/verify?token=` | **paid** | signed attestation of observed resolvers |

## Configuration (environment)

See `syxx_server/config.py` for the full list. Key ones:

| Var | Default | Meaning |
|-----|---------|---------|
| `SYXX_SESSION_SECRET` | persisted random | HMAC secret for sessions/attestations |
| `SYXX_KEYGEN_ACCOUNT_ID` | app's account | Keygen account for live validation (empty = offline only) |
| `SYXX_LICENSE_KEYS` | — | comma-separated offline license allowlist |
| `SYXX_ENFORCE_NATIVE` | `true` | enforce the paywall on `/warp`,`/leak`,`/fetch_script` |
| `SYXX_LEAK_ZONES` | `leak.syxx.app,leak.zzyn.net` | zones the DNS collector is authoritative for |
| `SYXX_TLS_CERT`/`SYXX_TLS_KEY` | — | optional built-in TLS (a reverse proxy is preferred) |
| `SYXX_FETCH_ALLOW_PRIVATE` | `false` | **testing only** — allow private IPs in the fetcher |
| `SYXX_SMTP_HOST` | — | SMTP server for outbound mail. **Empty = all mail disabled** (no-op, never fatal) |
| `SYXX_SMTP_PORT` | `587` | SMTP port |
| `SYXX_SMTP_USER`/`SYXX_SMTP_PASSWORD` | — | SMTP auth (omit for unauthenticated relay) |
| `SYXX_SMTP_STARTTLS` | `true` | `true`=STARTTLS (587); `false`=implicit TLS (465) |
| `SYXX_MAIL_FROM` | `Syxx <noreply@syxx.app>` | From header for confirmations + digest |
| `SYXX_PUBLIC_BASE_URL` | `https://syxx.app` | absolute base for image URLs in emails |
| `SYXX_BETA_CONFIRM` | `true` | email each beta subscriber a confirmation |
| `SYXX_BETA_DIGEST_TO` | — | operator address for the weekly signup CSV. **Empty = digest off** |
| `SYXX_BETA_DIGEST_INTERVAL_DAYS` | `7` | digest cadence in days |

**Beta signups & mail.** A `POST /api/beta/reserve` persists `{email, interests}` to the
`beta_signups` table (`interests` ⊆ {nnyx, zzyn}; at least one required). The subscriber receives the
designed acknowledgment email (`syxx_server/emails/beta_ack.html`, `{{BASE}}`→`SYXX_PUBLIC_BASE_URL`). If SMTP is configured, the subscriber gets a confirmation email and the
operator (`SYXX_BETA_DIGEST_TO`) receives a CSV of all signups every `…_INTERVAL_DAYS` (opens as
a spreadsheet in Excel/Numbers/Sheets). Mail is best-effort: with no `SYXX_SMTP_HOST` the endpoint
still records the signup and simply returns `confirmation: false`. The digest cadence survives
restarts (a `kv` timestamp) and is seeded on first boot so a fresh deploy never fires immediately.

## Deployment

`scripts/deploy.sh` rsyncs the whole `Server/` tree, installs a hardened systemd
unit (DNS on :53 via `CAP_NET_BIND_SERVICE`, HTTP on :8080), and puts nginx +
Let's Encrypt in front so the app's `https://syxx.app/…` calls reach the daemon:

```
SERVER_IP=1.2.3.4 DOMAIN=syxx.app LETSENCRYPT_EMAIL=you@example.com ./scripts/deploy.sh
```

The leak zones must have NS records delegating to the server IP for the DNS
collector to see resolver lookups.

## App-side Warp wiring

The shipped app calls `https://syxx.app/api/warp/manifest` and `/api/warp/raw` (both paid routes,
so an entitlement is required when `SYXX_ENFORCE_NATIVE=true`). The legacy `GET /warp?url=…`
route is **retired (410 Gone)** — clients no longer call it; its in-process `warp.build_baseline`
consumer survives only in the scheduler baseline job and the `/api/diff` content half. If a native
call needs to run without a stored Keygen key you can use a grace window with
`SYXX_ENFORCE_NATIVE=false`; the website paywall stays fully enforced regardless.
