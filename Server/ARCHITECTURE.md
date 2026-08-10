# Syxx Server Architecture

A Python threaded HTTP server and DNS infrastructure that powers the Syxx DNS
tampering-detection product. Serves a marketing website, paid investigation
APIs, a leak-detection collector, commerce (Stripe/Keygen), and background
monitoring — all from a single process behind an nginx reverse proxy.

## High-Level Topology

```
Client (app / website)
       │
       ▼
   nginx (TLS termination, IP filtering)
       │
       ▼
┌──────────────────────────────────────────────────┐
│              BoundedThreadingHTTPServer           │
│  (semaphore-bounded worker pool, max 64 threads)  │
│                                                    │
│  Handler.do_GET / do_POST / do_DELETE              │
│       │                                            │
│       ▼                                            │
│  _dispatch_get / do_POST (route table)             │
│       │                                            │
│       ├── Public routes (free, rate-limited)       │
│       ├── Paid routes (entitlement gate → 402)     │
│       └── Commerce/Admin routes                    │
│       │                                            │
│       ▼                                            │
│  Business logic modules                            │
│       │                                            │
│       ▼                                            │
│  Repository layer (SQLite WAL)                     │
└──────────────────────────────────────────────────┘

   dns_authoritative (UDP :53, own thread)
       │
       ▼
   leak.observe() → SQLite
```

## Module Boundaries

### Core — HTTP server and configuration

| Module | Responsibility |
|---|---|
| **app** | HTTP server (`BoundedThreadingHTTPServer`), `Handler` class with route dispatch, CORS, CSRF, security headers, OTel spans, response helpers (`_send`, `_json_error`). Single monolithic router — `_dispatch_get` maps ~40 GET paths, `do_POST` maps ~15 POST paths, `do_DELETE` maps 2. |
| **config** | Environment-driven configuration. All secrets from env vars, never hardcoded. Product catalog (Stripe SKU → product/price ID mapping). Helper functions `_b()`, `_i()`, `_s()`, `_list()` for typed env reading with validation and logging. |
| **db** | SQLite connection management (WAL mode, thread-local pool, max 4 connections per thread). Schema DDL, migration orchestration, row-cap/retention enforcement. Re-exports repository functions for backward compatibility. |

### Repository Layer — Data access

Extracted from `db.py` to isolate CRUD by domain. Each repo operates on one table family.

| Repository | Table(s) | Purpose |
|---|---|---|
| **kv_repo** | `kv` | Key-value store for session secrets, Stripe event idempotency, singleton state |
| **license_repo** | `licenses`, `keygen_licenses` | License key CRUD, Keygen license ID sync |
| **leak_repo** | `leak_tokens`, `leak_observations` | Leak token issuance, resolver observation recording |
| **monitor_repo** | `monitors`, `monitor_results` | Scheduled monitor definitions and check results |
| **beta_repo** | `beta_signups` | Beta waitlist email collection |

### Business Logic — Feature modules

| Module | Responsibility |
|---|---|
| **licensing** | Paywall gate. Validates Keygen license keys (Ed25519 signature verification), offline/admin keys, and HMAC-signed session tokens. `entitlement_for()` returns an Entitlement or an error. Session tokens are stateless — no round-trip to Keygen. |
| **warp** | SSRF-safe web page structural analysis. Fetches a URL via `fetcher`, parses HTML with `HTMLParser` (not regex), extracts scripts/stylesheets/iframes, hashes inline script bodies, builds a comparison manifest. Cached baselines in SQLite. |
| **investigate** | Multi-vantage DNS investigation. Fans out a domain query across system resolver, UDP public resolvers, and DoH. Reports consensus as a tri-state result. Records findings into passive DNS. Bounded by a wall-clock deadline (default 15 s). |
| **leak** | Signed one-time leak-token collector. Issues tokens (`/api/leak/token`), records DNS observations from the authoritative server, and returns Ed25519-signed attestations (`/api/leak/verify`). Dedicated collector keypair, independent from licensing. |
| **dns_authoritative** | Authoritative UDP DNS server for configured leak zones. Answers A/AAAA/SOA/NS queries, records token lookups via `leak.observe()`. Per-source-IP rate limit (50 q/60 s). Runs on its own thread. |
| **fulfillment** | Stripe webhook handler. On `checkout.session.completed`, mints a Keygen license under the SKU's policy and emails the key. On `customer.subscription.deleted`, notes the lapse. Idempotent by Stripe event ID with a processing-lease lock. |
| **stripe_checkout** | Creates Stripe Checkout Sessions for the six SKUs (3 platforms × 2 billing periods). Reads price IDs from the product catalog in `config`. |
| **fetcher** | SSRF-safe outbound HTTP gateway. Scheme allowlist (http/https only), DNS-resolve-then-block-private-IPs, manual redirect following with per-hop re-validation, response size cap, hard timeout. |
| **resolver** | Self-contained DNS client. Three query modes: `query()` (recursive, formatted), `raw_query()` (non-recursive, raw bytes for DNSSEC), `forward_wire()` (passthrough for DoH/DoT). UDP with TCP fallback on truncation. |
| **scheduler** | Background daemon thread. Loops every 60 s, runs due monitors (investigate/baseline/warp checks), persists results, emails alerts on changes. Graceful shutdown via `threading.Event`. |
| **diff** | Content comparison helpers. Diffs current vs. previous investigation/baseline/warp results, returns structured change lists. |
| **passivedns** | Passive DNS index. Records every DNS observation seen by the server for later querying via `/api/passive-dns`. |

### Supporting Modules

| Module | Responsibility |
|---|---|
| **ratelimit** | Per-subject token-bucket rate limiter. Keys by entitlement subject (authenticated) or client IP (anonymous). In-memory only (resets on restart with a 50 % startup-grace window). Hard cap of 10 000 buckets. |
| **listeners** | Per-listener health registry. Tracks state of HTTP, DNS, and DoH listeners as `listening` / `degraded-fallback` / `down` / `unknown`. `/readyz` folds the registry into its response. |
| **preflight** | Boot-time validation. Reports missing Stripe keys, unconfigured Keygen public key, SMTP misconfig, and collector key parse errors. Never blocks startup — reports only. |
| **logging_util** | Structured JSON logging with OTel bridge. Counters (`incr`), warnings, errors. Wired to OTLP log exporter at startup. |
| **mailer** | SMTP email delivery for license keys, monitor alerts, and beta digests. |
| **broadcast** | Reads operator-edited `broadcast.json` for message-of-the-day. |
| **appcast** | Ed25519-signed auto-update appcast (Sparkle-inspired format) for macOS/Windows/Debian clients. |
| **email_auth** | Email authentication (SPF/DMARC) checking. |
| **whois_lookup** | WHOIS/RDAP lookups for domain registration data. |
| **subdomain_enum** | Subdomain enumeration via DNS brute-force and certificate transparency. |
| **lookalike** | Domain lookalike/homograph detection. |
| **firewall_test** | Firewall port-reachability testing. |
| **dnssec_validator** | Cryptographic DNSSEC validation (RRSIG/DNSKEY verification). Optional — requires `cryptography` library. |
| **seo** | SEO page generation with pricing from `config`. |
| **netutil** | Network utilities including `PinnedHTTPSConnection` for certificate-pinned TLS. |

## Data Flow

### Typical paid API request

```
1.  Client sends GET /api/investigate?domain=example.com
        with Authorization: Bearer <session-token> or X-Syxx-License: <key>

2.  nginx terminates TLS, forwards to :8080 with X-Real-IP

3.  BoundedThreadingHTTPServer acquires worker semaphore
        (503 if pool exhausted)

4.  Handler.do_GET → _dispatch_get routes to _route_investigate

5.  _require(query, enforce=True) calls licensing.entitlement_for()
        ├── Session token: HMAC verify (stateless, fast)
        ├── License key: Keygen API call + Ed25519 signature check
        └── None → 402 Payment Required

6.  ratelimit.allow(subject) checks token bucket
        └── Over limit → 429 Too Many Requests

7.  investigate.investigate(domain) fans out DNS queries
        ├── resolver.query() via system resolver
        ├── resolver.query() via public UDP resolvers
        ├── resolver.query() via DoH
        └── passivedns.record() stores observations

8.  Handler._send(200, result_json)
        ├── ETag from SHA-256 of body (304 if match)
        ├── gzip if Accept-Encoding permits
        ├── CORS + security headers
        └── OTel span ends with status code
```

### Leak detection flow

```
1.  Client: POST /api/leak/token  →  leak.issue_token()
        Generates random token, persists to leak_tokens, returns token

2.  Client configures DNS to use the token as a subdomain query
        e.g., A query for <token>.leak.syxx.app

3.  dns_authoritative (UDP thread) receives the query
        → leak.observe(token, resolver_ip, qtype)
        → Records into leak_observations table

4.  Client: GET /api/leak/verify?token=<token>
        → leak.verify(token) returns observed resolver IPs
        → Signs attestation with collector Ed25519 private key
        → Returns {resolvers, signature, public_key}
```

### Commerce flow (purchase)

```
1.  Client: GET /api/checkout?sku=syxx_mac_monthly_sub
        → stripe_checkout.create_checkout_session()
        → Returns Stripe Checkout URL

2.  Customer completes payment on Stripe

3.  Stripe: POST /api/webhooks/stripe (HMAC-signed event)
        → fulfillment.handle_checkout_completed()
        → _keygen_create_license(policy_id, email)
        → mailer.send_key(email, license_key)
        → Records event ID for idempotency

4.  Client: POST /api/auth/activate with license key
        → licensing validates key
        → Mints session token (HMAC-signed)
        → Returns session token for subsequent API calls
```

## External Dependencies

| Service | Integration | Failure mode |
|---|---|---|
| **Stripe** | Checkout Sessions, Webhooks (HMAC-verified). Product catalog in `config`. | Missing key → checkout disabled, webhook rejected. Fail closed. |
| **Keygen.sh** | License minting (admin API), key validation (Ed25519-signed responses). TLS-pinned. | Missing public key → all keys denied. Fail closed. |
| **Public DNS resolvers** | UDP + DoH queries for investigation. Multiple vantage points. | Timeout → vantage absent from result (not counted as agreeing). |
| **Sentry** | Crash reporting, exception capture. Best-effort. | Failure → logged, never crashes the server. |
| **OTLP/Grafana** | Traces + logs exported via HTTP to `bugsmasher.syxx.app`. | Failure → recorded in `_OTEL_STATE`, surfaced by `/readyz`. |
| **SMTP** | License key emails, monitor alerts, beta digests. | Missing config → emails silently no-op (preflight warns at boot). |

## Security Model

### Authentication and authorization

- **Entitlement gate**: Paid routes require a valid Keygen license key, offline/admin key, or HMAC-signed session token. No entitlement → 402.
- **Session tokens**: Stateless HMAC-signed blobs minted after license validation. Include subject, expiry, and signing timestamp. Verified without a Keygen round-trip.
- **Admin token**: Bearer token for support tooling (`SYXX_ADMIN_TOKEN`). Blank → admin endpoints disabled.

### Transport and request security

- **CSRF**: State-changing POST/DELETE requests require an `Origin` header matching the CORS allowlist. Webhook endpoints excluded (HMAC-verified instead).
- **Sec-Fetch-Site**: Cross-site browser requests rejected (`cross-site` value → 403).
- **CORS**: Origin echo only for allowlisted origins. Never `*`.
- **CSP**: Per-request cryptographic nonce for inline scripts. No `unsafe-inline` for `script-src`.
- **HSTS**: Emitted when TLS is terminated (directly or via reverse proxy `X-Forwarded-Proto`).
- **Security headers**: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, `Permissions-Policy` restricting camera/microphone/geolocation.

### Rate limiting

- **HTTP**: Per-subject token-bucket (by entitlement subject or client IP). In-memory with 10 000 bucket cap. 50 % capacity during 60 s startup grace.
- **DNS**: Per-source-IP sliding window (50 queries per 60 s). 10 000 IP cap with stale-entry pruning.
- **Workers**: Semaphore-bounded thread pool (max 64). Excess connections → 503.
- **Request deadline**: Absolute wall-clock force-close (default 60 s) prevents slow-drip clients from pinning workers.

### SSRF protection

- **fetcher**: Scheme allowlist (http/https only), DNS resolve → block private/loopback/link-local/reserved IPs, manual redirect following with per-hop re-validation, response size cap, connection pinned to vetted IP.

### Webhook verification

- **Stripe**: `Stripe-Signature` HMAC verification with configurable timestamp tolerance (default 300 s).
- **Keygen**: HMAC-SHA256 signature verification on license webhooks.

### Cryptographic attestation

- **Leak collector**: Dedicated Ed25519 keypair signs leak attestations. Public key exposed at `/api/pubkey` for third-party verification.
- **License validation**: Keygen response signatures verified via Ed25519 (server) or P-256 ECDSA (clients). TLS-pinned connections to Keygen API.
- **Appcast**: Ed25519-signed update manifests for auto-update clients. Dedicated signing key, never the collector key.

### Domain privacy

- **Telemetry redaction**: Domain names in OTel spans are one-way hashed (SHA-256, 8 hex chars) with only the TLD preserved in cleartext (`_redact_domain()`).

## Process Model

The server runs as a single Python process with multiple threads:

| Thread | Module | Lifetime |
|---|---|---|
| Main | `BoundedThreadingHTTPServer.serve_forever()` | Process lifetime |
| Worker pool | Handler threads (up to 64 concurrent) | Per-request |
| DNS | `dns_authoritative` UDP server | Process lifetime |
| DoH | DNS-over-HTTPS listener | Process lifetime |
| Scheduler | `scheduler` monitoring loop (60 s interval) | Daemon, graceful shutdown |
| Deadline timer | Per-connection force-close timer | Per-connection |

All threads share a single SQLite database file in WAL mode with thread-local connection pooling.
