# Sub-project D — Server encrypted listeners (DoH / DoT / DoQ): engineer-ready plan

Derived from `ENCRYPTED-TRANSPORTS-PLAN.md` sections 1, 3, 4, 5.D. This is the
implementation blueprint for the *server* leg only (`Server/`). Client legs (A/B/C/E/F)
are out of scope here. Executor: `syxx-engineer` (server lane / Cline). All line
numbers below were re-verified against the live tree on the planning date; any that
drifted from the program plan are corrected inline and flagged **[corrected]**.

Owning delta / roadmap anchor: `ROADMAP.md:954-967` (Deferred — Encrypted transports),
which already slates "server DoH (≈ free RAM) then DoT (evaluate DoQ separately on
measured RAM)". This plan implements that line.

---

## 0. Locked decisions (do not relitigate)

- **DoH = paywalled forwarder.** GET/POST `/dns-query` reuses the `_require` entitlement
  middleware (`app.py:220`) and forwards to `config.PUBLIC_RESOLVERS` (`config.py:177`),
  **plus** a per-request IP allowlist and the existing rate limiter (`ratelimit.allow`,
  `ratelimit.py:28`, via `_rate_ok` `app.py:257`).
- **DoT / DoQ = scope-limited.** They cannot carry an HTTP license header, so they are
  **not** general forwarders. Default scope = authoritative-leak-only (reuse
  `dns_authoritative._build_response`, `dns_authoritative.py:40-99`), with an optional
  source-IP allowlist that upgrades a configured peer to forwarding. No paywall bypass.
- **`aioquic` approved** as a new dependency, but the server DoQ **listener stays gated
  behind the D-4 RAM measurement**. Numbers on the real VPS first; listener second.
- **App TLS cert.** The service gets **group read** on the Let's Encrypt privkey
  (`/etc/letsencrypt/live/<domain>/privkey.pem`), provisioned in `scripts/deploy.sh`
  (the nginx/certbot block is `deploy.sh:224-259`), because DoQ cannot be nginx-proxied
  and must terminate TLS itself. See D-2 §"Cert provisioning" for the today-vs-SD-53
  nuance (today the unit runs `User=root`, `deploy.sh:182`, and can already read the
  privkey; the group grant is what keeps it working once SD-53 drops root).
- **Honesty:** the app is reachable **plaintext on :8080** (`config.py:107-108`,
  `HTTP_HOST=0.0.0.0`, `HTTP_PORT=8080`; SD-59). No DoH response may assert the channel
  was "encrypted" — TLS is an nginx property, not an app property, on that path.

---

## 1. Architecture reality (what the implementer must know before touching code)

- **Entry point:** `Server/server.py`. `main()` (`server.py:21-36`) calls `db.init()`,
  starts the DNS leak collector on a **daemon thread** (`server.py:28-30`,
  `dns_authoritative.run`), then runs the HTTP server in the foreground via
  `app.serve()`.
- **HTTP server:** `app.serve()` (`app.py:1024-1038`) builds a `ThreadingHTTPServer`
  (`app.py:1028`), optionally TLS-wraps it if `config.TLS_CERT`/`TLS_KEY` are set
  (`app.py:1030-1035`), and `serve_forever()`s. Thread-per-connection, **no connection
  cap** (SD-51, `app.py:925` in the delta ledger → live line `app.py:1028`
  **[corrected]**).
- **Request handler:** `Handler(BaseHTTPRequestHandler)` (`app.py:166`), HTTP/1.1
  (`app.py:167`). GET dispatch: `_dispatch_get` (`app.py:344-394`). POST dispatch:
  `do_POST` (`app.py:322-341`). Response helper: `_send` (`app.py:191-208`) — accepts a
  `content_type` and `extra_headers`, so wireformat bodies are already supported.
  Body reader: `_body` (`app.py:263-283`) with `_BODY_TOO_LARGE`/`_BODY_INCOMPLETE`
  sentinels and `_body_error` (`app.py:285-292`).
- **Auth / rate / IP:** `_require(query, enforce)` returns `(ent, ok)` and emits the
  402/503 itself (`app.py:220-232`). `entitlement_for(headers, query)` reads a
  `Authorization: Bearer <session>` token then an `X-Syxx-License` header
  (`licensing.py:283-298`) — **never** a query string. `_rate_ok(ent)` (`app.py:257`),
  `_client_ip()` (`app.py:234-250`, honors `X-Real-IP` only from a loopback peer).
- **DNS primitives available for reuse:**
  - Authoritative wire builder: `dns_authoritative._build_response(data)` returns
    `(response_bytes, leak_hit)` and already does bounds-checked parsing + REFUSED for
    out-of-zone names (`dns_authoritative.py:40-99`, REFUSE at `:62-66`).
  - Name parse with compression/loop guards: `resolver._read_name` (`resolver.py:63-95`).
  - Response parse + question validation (A020): `resolver._parse_response`
    (`resolver.py:98-136`).
  - Recursive single-resolver query (connected UDP, TCP-on-truncation):
    `resolver.query(name, qtype, server, timeout)` (`resolver.py:156-173`), TCP framing
    in `resolver._query_tcp` (`resolver.py:176-189`, 2-byte length prefix at `:178`).
  - Authoritative walker (ground truth, heavy): `resolver.resolve_authoritative`
    (`resolver.py:384`). **Not used by these listeners** (per decision 1, forward not walk).
- **Health today:** `/healthz` returns `{ok:true}` unconditionally (`app.py:356-357`,
  SD-62-adjacent). `/readyz` (`_route_readyz`, `app.py:540-545`) only checks a DB
  connect. Neither reports listener state. The DNS collector already degrades silently to
  `:5053` (`dns_authoritative.py:148-150`, SD-62).
- **Deploy:** `scripts/deploy.sh`. VPS IP `74.208.237.162`, domain `syxx.app`
  (`deploy.sh:19,22`). systemd unit generated at `deploy.sh:175-216`:
  `User=root` (`:182`), `ExecStart=… server.py` (`:184`), `Restart=always`/`RestartSec=5`
  (`:185-186`), **`AmbientCapabilities=CAP_NET_BIND_SERVICE` (`:209`)** — already covers
  binding :853 for both TCP and UDP, so **no new capability is needed**. nginx/certbot at
  `deploy.sh:224-259`; **no firewall provisioned** (SD-56).
- **Dependencies today:** stdlib + `cryptography` only (`requirements.txt:7`). `aioquic`
  is **not** installed.
- **Tests:** `Server/tests/`. Two proven patterns to reuse:
  1. **Handler unit without a socket** — `object.__new__(app.Handler)` then set
     `.headers`/`.rfile` and call the method directly (`tests/test_server_hardening.py:20-29`).
  2. **Live-socket route harness** — `app.ThreadingHTTPServer(("127.0.0.1", 0), app.Handler)`
     on an ephemeral port in a daemon thread (`tests/test_routes.py:50-67`), driven with
     `urllib.request`.

**Platform parity:** sub-project D is **server-only**. There is no `SyxxKit`/`Syxx.Kit`
twin. The parity obligation is *contract* parity: the server DoH answer must be the same
wire the client A-transports (sub-project A) will parse and prove — do not invent a
server-specific wire dialect. No macOS/Windows code changes in this plan.

---

## 2. Global dependency graph (hard ordering)

```
D-0 (listener health registry + /readyz) ──┐
                                            ├─> D-1 (DoH route)          [ships first, ~free RAM]
D-0 ────────────────────────────────────────┼─> D-2 (DoT listener + cert + conn cap)
                                            │        │
                                            │        └─> firewall/systemd :853 open (D-5)
                                            │
D-4 (RAM measurement harness + VPS run) ────┴─> D-3 (DoQ listener)      [gated on D-4 numbers + aioquic]
```

- **D-0 must precede D-1/D-2/D-3** — every listener reports into the shared health
  registry, and shipping a listener that can't surface `down`/`degraded` re-creates the
  `/healthz {ok:true}` lie (SD-62). Small, do it first.
- **D-1 before D-2 before D-3** (effort + risk order; matches `ROADMAP.md:961`). DoH reuses
  the running HTTP server and nginx TLS → lowest risk. DoT adds a new listener + cert +
  connection cap. DoQ adds a dependency + a new concurrency model.
- **D-4 strictly before D-3.** Do not write the DoQ listener until the measurement harness
  has returned numbers that fit the VPS with margin. If they don't fit, D-3 is deferred and
  that is a valid, honest outcome.
- **D-5 (firewall/systemd) lands with D-2** (opens :853/tcp) and is extended by D-3
  (:853/udp). Opening the port before the listener is up would expose a closed port; opening
  it after means the first real-run `kdig +tls` fails on a firewall, not the app — so
  coordinate, don't skip.

---

## 3. Steps

### D-0 — Listener health registry + honest `/readyz` (do first)

**Action.** Add a thread-safe per-listener health registry and surface it as a tri-state in
`/readyz`; never let a listener degrade silently.

**Files / targets.**
- **New:** `Server/syxx_server/listeners.py` — a module holding
  `_state: dict[str, str]` under a `threading.Lock`, states drawn from a fixed set
  `{"listening", "degraded-fallback", "down", "unknown"}` (tri-state+unknown, per the §3
  honesty contract — *listener health: listening / degraded-fallback / down*). API:
  `set_state(name, state, detail=None)`, `snapshot() -> dict`. Default every known listener
  to `"unknown"` at import so a never-started listener reads `unknown`, never `listening`.
- **Edit:** `app.py:540-545` `_route_readyz`. Keep the DB check, then fold in
  `listeners.snapshot()`. Return `200 {"ready": true, "listeners": {...}}` **only** if the
  DB is reachable **and** no known listener is `"down"`; return `503` with the offending
  listener names otherwise. A `"degraded-fallback"` listener is reported verbatim (it is
  degraded, not down) but does **not** by itself flip the top-level `ready` to false —
  document that choice inline so a reviewer sees it is deliberate.
- **Edit:** wire `dns_authoritative` into the registry too (retires SD-62's silent
  degrade for the existing collector): in `_bind` set `listening` on the primary port
  (`dns_authoritative.py:143-144`), `degraded-fallback` on the `:5053` fallback (`:145-146`),
  `down` when no port binds (`:148-150`). This is a small, in-scope honesty fix that the new
  registry makes trivial; note it in the commit as also touching SD-62.

**Effort.** S.

**Real-data acceptance bar.** Run `python3 server.py` locally with `SYXX_DNS_PORT` set to a
privileged/occupied port so the collector falls back, then `curl -s localhost:8080/readyz`.
Honest output: `readyz` shows `"dns": "degraded-fallback"` (not a blanket ok). Kill the DB
file mid-run → `readyz` returns `503`. Start clean → `"dns": "listening"`. The regression
floor: a unit test asserting `snapshot()` transitions and that `/readyz` returns 503 when any
listener is `down` (below, shared across D-1/2/3 test files or in `test_server_hardening.py`).

**Approval gate.** `/readyz` JSON adds machine-readable keys, not prose — low copy risk, but
log the new keys to `outputs-catalog.csv`. If any human-readable `detail` string is added,
propose it first.

**Dependency.** None. Blocks D-1/D-2/D-3.

---

### D-1 — DoH route: GET/POST `/dns-query` (paywalled forwarder)

**Action.** Add RFC 8484 `application/dns-message` (GET `?dns=` + POST body) and the
`application/dns-json` convenience format, gated on `_require`, IP-allowlisted, rate-limited,
forwarding to `config.PUBLIC_RESOLVERS`. Near-zero new RAM (reuses the running HTTP server,
nginx TLS, rate limiter, `_body`).

**Files / targets.**
- **Edit `app.py:322-341` (`do_POST`):** add, before the `_json_error(404)` at `app.py:338`,
  `if path == "/dns-query": return self._route_doh_post(urllib.parse.parse_qs(parsed.query))`.
- **Edit `app.py:344-394` (`_dispatch_get`):** add, before the final
  `return self._json_error(404, "not found")` at `app.py:394`,
  `if path == "/dns-query": return self._route_doh_get(query)`.
- **New handler methods on `Handler`** (place near the DNS-ish routes, e.g. after
  `_route_authoritative`):
  - `_route_doh_get(self, query)` — content-negotiate: if `?dns=<base64url>` present →
    wireformat path; elif `?name=` present → JSON path; else `400`.
  - `_route_doh_post(self, query)` — require `Content-Type: application/dns-message`
    (else `415`), read `self._body()` (respect `_body_error`), wireformat path.
  - `_doh_common_gate(self, query) -> (ent, ok)` — the shared gate:
    1. `ent, ok = self._require(query, config.ENFORCE_DOH)` — **enforce=True** (paywalled;
       do not reuse the anonymous grace window). Emits 402/503 itself.
    2. IP allowlist: if `config.DOH_ALLOWLIST` is non-empty and `self._client_ip()` is not in
       it → `self._json_error(403, "forwarder not enabled for this address")`, return not-ok.
    3. `if not self._rate_ok(ent): return None, False`.
  - `_doh_forward_wire(self, wire) -> (response_wire | None, http_error_status | None)` —
    the forwarder core (see below).
- **New `resolver.forward_wire(query_wire, server, timeout)` in `resolver.py`** (recommended
  over the re-encoder — see §"Design note"): parse just enough to extract `txid` + question
  (reuse `_read_name`, `resolver.py:63`), send the **exact client bytes** over connected UDP
  to `server:53` (connected socket filters off-path spoof, matching `resolver.query`'s A020
  posture, `resolver.py:163`), receive raw, **validate** the response `txid` and question match
  the query (reject mismatches as `DNSError`), retry over TCP with the 2-byte length prefix if
  the TC bit is set (mirror `_query_tcp`, `resolver.py:176-189`), and return the **raw response
  wire unchanged**. Add `config` knobs `DOH_UPSTREAM_TIMEOUT` (default 5).
- **JSON path** (`application/dns-json`): parse `?name=&type=`, call
  `resolver.query(name, qtype, config.PUBLIC_RESOLVERS[0])`, emit the Google JSON schema
  (`{Status, TC, RD, RA, AD, CD, Question:[...], Answer:[...]}`) from the parsed dict. On
  parse/upstream failure emit `Status:2` (SERVFAIL) or HTTP 502 — never a fabricated `Status:0`.
- **New config** (`config.py`, near the investigation block `:175-196`):
  `ENFORCE_DOH = _b("SYXX_ENFORCE_DOH", True)`,
  `DOH_ALLOWLIST = _list("SYXX_DOH_ALLOWLIST", [])`,
  `DOH_UPSTREAM_TIMEOUT = _i("SYXX_DOH_UPSTREAM_TIMEOUT", 5)`.

**Design note (honesty-critical) — passthrough, not re-encode.** The program plan cites the
`_build_response` / `_parse_response` primitives as if the DoH route should *re-encode* answers
from a parsed dict. **Recommend against that for `application/dns-message`.** Re-encoding from
`resolver._parse_response` (`resolver.py:98-136`) only surfaces the record types
`_format_rdata` knows (`resolver.py:139-153`) and drops EDNS/DNSSEC/unknown RRs — that would
ship a *lossy, partly-fabricated* answer, which is exactly the "fabricated clean answer"
failure the §3 contract rule 5 forbids. Raw passthrough (forward exact query bytes, return
exact response bytes, validate txid+question) is both simpler and strictly more honest. Keep
the JSON format as the only parsed representation, because JSON is inherently a parse and its
failure mode is an explicit `Status:2`, not a silent drop. If a reviewer insists on the
re-encoder, it must be flagged as lossy and the DoH answer labeled accordingly — do not ship a
re-encoded wire answer that looks authoritative.

**Never assert encryption.** Do not add any response header or body field claiming the channel
is encrypted/secure (SD-59: plaintext on :8080). The response carries the DNS answer and
nothing about transport security.

**Failure mapping (honest, never a clean lie).**
- Malformed/empty client query, wrong content-type, oversized body → `400`/`415`/`413`.
- Not entitled → `402` (from `_require`). Not allowlisted → `403`. Rate limited → `429`.
- Upstream unreachable / all `PUBLIC_RESOLVERS` fail → **HTTP 502** (never HTTP 200 + empty).
- Upstream returns SERVFAIL/NXDOMAIN → pass the wire through with its real rcode (HTTP 200).
  Never rewrite an rcode to NOERROR.

**Effort.** S-M.

**Real-data acceptance bar (run on the VPS, real network).**
- **Honest refusal, unauthenticated:** `kdig +https=/dns-query @syxx.app example.com`
  (kdig sends no `Authorization` header) → the client sees the paywall: **HTTP 402**
  surfaced as a failed DoH exchange, **not** an answer. Confirm with
  `curl -s -o /dev/null -w '%{http_code}\n' 'https://syxx.app/dns-query?name=example.com&type=A' -H 'accept: application/dns-json'` → `402`.
- **Honest success, authenticated + allowlisted:** mint a session token
  (`curl -s -XPOST https://syxx.app/api/auth/activate -H 'content-type: application/json' -d '{"key":"<a real license key>"}'`
  → `.token`), add the test host IP to `SYXX_DOH_ALLOWLIST`, then
  `curl -s 'https://syxx.app/dns-query?name=example.com&type=A' -H 'accept: application/dns-json' -H "authorization: Bearer $TOK"`
  → real `Answer` records for example.com, `Status:0`. Wireformat check:
  `curl -s 'https://syxx.app/dns-query?dns=<base64url query>' -H 'accept: application/dns-message' -H "authorization: Bearer $TOK" --output - | kdig -x` (or decode with a scratch script) → the A record for example.com, byte-faithful to what `1.1.1.1` returned.
- **Honest backend failure:** point `SYXX_PUBLIC_RESOLVERS` at an unreachable IP
  (`192.0.2.1`), authenticated request → **HTTP 502**, never an empty NOERROR.
- **Regression floor (tests, not the bar):** `tests/test_doh.py`, below.

**Approval gate.** Any human-readable error string (`"forwarder not enabled for this address"`,
`"payment required…"` is reused verbatim from `app.py:230`) must be **proposed for approval and
logged to `outputs-catalog.csv`** before it ships. Propose the 403 wording; do not finalize it
in code.

**Dependency.** D-0. No new pip dependency.

---

### D-2 — DoT listener: ssl-wrapped bounded `ThreadingTCPServer` on :853

**Action.** Add a TLS DNS-over-TCP listener on :853, scope-limited (authoritative-leak-only by
default, forward only for allowlisted source IPs), with a **hard connection cap** (retires
SD-51 for this path), terminating TLS with the Let's Encrypt cert.

**Files / targets.**
- **New:** `Server/syxx_server/dot_listener.py`.
  - `class _BoundedThreadingTCPServer(socketserver.ThreadingTCPServer)` with
    `daemon_threads = True`, `allow_reuse_address = True`, and a
    `threading.BoundedSemaphore(config.DOT_MAX_CONN)` acquired (non-blocking) in
    `get_request`/handler entry; on failure, close the socket immediately and increment a
    `dot_conn_rejected` metric (`logging_util.incr`). **This cap is the SD-51 retirement for
    the DoT path** — DoT keep-alive makes unbounded thread-per-conn worse, so the cap is a
    prerequisite, not a nicety.
  - `class _Handler(socketserver.BaseRequestHandler)`: read the 2-byte length prefix then the
    message with a bounded `recv` loop (mirror `resolver._recv_exact`, `resolver.py:192-198`,
    and the framing in `_query_tcp`, `resolver.py:176-189`); apply a per-connection read
    timeout (`config.DOT_READ_TIMEOUT`) and a max in-flight query count per connection.
  - **Answer scope:** call `dns_authoritative._build_response(msg)` (`dns_authoritative.py:40-99`)
    — authoritative-leak-only, REFUSED for everything else. If `self.client_address[0]` is in
    `config.DOT_FORWARD_ALLOWLIST`, instead forward via `resolver.forward_wire` (from D-1). This
    keeps the listener from being an open forwarder while still letting Syxx's own infra use it.
    Record leak hits exactly as the UDP collector does (`dns_authoritative.py:185-186`,
    `leak.observe`).
  - `run(stop_event=None)`: build an `ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)`, `minimum_version
    = ssl.TLSVersion.TLSv1_2`, `load_cert_chain(config.DOT_TLS_CERT, config.DOT_TLS_KEY)`,
    **no** custom verify callback (fail-closed, §3 rule 4), wrap the server socket
    `server_side=True`. On bind/cert failure set `listeners.set_state("dot", "down", detail=…)`
    and return (HTTP/DoH keep running); on success set `"listening"`. Serve on its own daemon
    thread.
- **Edit `server.py:28-30`:** after the existing `dns_thread`, add a `dot_thread`
  (`threading.Thread(target=dot_listener.run, daemon=True, name="dot-server")`) started the same
  way, guarded by `if config.ENABLE_DOT`. Import `dot_listener` at `server.py:18`.
- **New config** (`config.py`, DNS listener block `:106-116`):
  `ENABLE_DOT = _b("SYXX_ENABLE_DOT", False)` (default off until cert + firewall are staged),
  `DOT_PORT = _i("SYXX_DOT_PORT", 853)`, `DOT_HOST = _s("SYXX_DOT_HOST", "0.0.0.0")`,
  `DOT_MAX_CONN = _i("SYXX_DOT_MAX_CONN", 128)`,
  `DOT_READ_TIMEOUT = _i("SYXX_DOT_READ_TIMEOUT", 10)`,
  `DOT_FORWARD_ALLOWLIST = _list("SYXX_DOT_FORWARD_ALLOWLIST", [])`,
  `DOT_TLS_CERT = _s("SYXX_DOT_TLS_CERT", "")`, `DOT_TLS_KEY = _s("SYXX_DOT_TLS_KEY", "")`.

**Cert provisioning (decision 4).**
- The LE cert lives at `/etc/letsencrypt/live/<domain>/{fullchain.pem,privkey.pem}` (issued by
  the certbot block, `deploy.sh:253-255`). Set `SYXX_DOT_TLS_CERT`/`SYXX_DOT_TLS_KEY` to those
  paths in the systemd unit env (`deploy.sh:187-206`).
- **Today (`User=root`, `deploy.sh:182`) the app already reads the privkey directly** — no perms
  change is strictly required for the first real run. The group-read grant is the
  **SD-53-forward-compatible** design: once the unit drops from root to a dedicated/`DynamicUser`
  service, it must keep read access. Add to `deploy.sh` (idempotent, after certbot succeeds):
  create group `syxx-cert`, `setfacl -R -m g:syxx-cert:rX /etc/letsencrypt/{live,archive}` (ACL
  survives certbot renewals better than chmod on `archive/`), and add the service user to
  `syxx-cert` (or, under `DynamicUser`, `SupplementaryGroups=syxx-cert` in the unit). Document
  that certbot's deploy-hook may reset perms → add a certbot `--deploy-hook` that re-applies the
  ACL. Flag the interaction with SD-53 explicitly so whoever lands SD-53 doesn't break DoT/DoQ.
- **nginx `stream` caveat:** if a future deploy terminates DoT via an nginx `stream {}` block
  instead of app-terminated TLS, the app receives **plaintext** and must **not** attest the
  client channel was encrypted (same class as SD-59). This plan uses **app-terminated** TLS, so
  the app holds the TLS session and the attestation is honest — keep it that way.

**Effort.** M.

**Real-data acceptance bar (VPS).**
- **Honest success:** `kdig +tls @syxx.app leak-canary.leak.syxx.app A` (an in-zone leak name)
  → a real authoritative answer over TLS 1.3, and the query is recorded by the collector
  (`leak.observe`). Pre-flight the handshake:
  `openssl s_client -connect syxx.app:853 -servername syxx.app </dev/null` → LE chain, TLS 1.3.
- **Honest scope refusal:** `kdig +tls @syxx.app example.com A` from a non-allowlisted host →
  **REFUSED** (rcode 5), never a fabricated answer. From an allowlisted host it forwards and
  answers.
- **Honest connection-cap behavior:** open `DOT_MAX_CONN + 50` concurrent idle TLS connections
  (a small script) → excess connections are closed/refused and `dot_conn_rejected` increments;
  the HTTP server and collector stay up (prove SD-51 is retired for this path).
- **Honest failure:** stop the listener / block :853 → `kdig +tls @syxx.app` reports a
  refused/closed :853 (connection error), **never** a plaintext Do53 answer. `/readyz` shows
  `"dot": "down"`.
- **Regression floor:** `tests/test_dot.py`, below.

**Approval gate.** No new user-facing prose beyond DNS rcodes (REFUSED is protocol, not copy).
Log the new `/readyz` `"dot"` key and any log-line strings to `outputs-catalog.csv`.

**Dependency.** D-0. Lands with D-5 (firewall opens :853/tcp).

---

### D-4 — DoQ RAM measurement harness (BEFORE D-3)

**Action.** Measure aioquic's real memory/CPU footprint on the production VPS and record the raw
numbers here, before a single line of the DoQ *listener* is written. This is the gate; a bad
number defers D-3.

**Files / targets.**
- **New:** `Server/scripts/doq_ram_probe.py` — a self-contained harness that runs on the VPS
  (`deploy.sh:19`, `74.208.237.162`). It stands up a **minimal** aioquic echo/DoQ-stub server
  (ALPN "doq", the LE cert) in one process, and a companion client driver that opens N
  connections. It measures the **server process** RSS via `/proc/<pid>/status:VmRSS` (not the
  client's), and CPU via `/proc/<pid>/stat` utime+stime deltas.
- **Protocol (record every raw number in §5 below):**
  1. **Baseline:** server up, **0 connections**, sample RSS 3× at 5 s spacing → `RSS_0`
     (report min/median/max, not just one).
  2. **Idle-scale:** hold **N ∈ {100, 500, 1000}** connections open (no queries), sample RSS →
     `RSS_N`. Compute `per_conn = (RSS_N − RSS_0) / N` for each N and **check linearity**
     (per-conn should be roughly constant; a super-linear curve means buffer/stream bloat and is
     itself a defer signal).
  3. **Active:** at each N, drive each connection issuing back-to-back DoQ queries for ~30 s;
     record peak RSS and CPU%.
  4. **Headroom gate:** on the VPS run `free -m`; record `MemAvailable`. Account for the
     **co-resident** HTTP server, the DoT listener, the leak collector, nginx, and the SQLite
     **WAL** (check `*.db-wal` size under load). Compute
     `max_safe_conns = (MemAvailable − reserved_for_coresidents) / per_conn`, apply a **50 %
     margin**. **Ship D-3 only if** the realistic peak concurrent DoQ connection count is well
     below `max_safe_conns` **and** per-conn scaling is linear. Otherwise **defer D-3** and
     record that decision here.
- **Pre-queued VPS commands** (run over the existing deploy SSH):
  `ssh root@74.208.237.162 'free -m; python3 --version'` (confirm headroom + interpreter),
  then install aioquic in a venv (§4), then `python3 scripts/doq_ram_probe.py --n 100 500 1000`.

**Effort.** M (mostly running + recording, but the harness must be real — no synthetic numbers).

**Real-data acceptance bar.** The harness prints a table of `RSS_0`, `RSS_N`, `per_conn`,
active RSS/CPU, and the `free -m` headroom, **captured from the real VPS**, and this document's
§5 is filled in with those numbers and an explicit **ship / defer** verdict. A harness that
prints estimates or hardcoded figures fails this bar (Protocol §2/§5 — no fabricated measured
values).

**Dependency.** aioquic installed (§4). Blocks D-3.

---

### D-3 — DoQ listener: aioquic on :853/udp, ALPN "doq" (gated on D-4)

**Action.** Add a DNS-over-QUIC listener, app-terminated TLS, on its own asyncio loop on a
dedicated thread (the process has no event loop today). **Do not start this step until D-4's §5
numbers say ship.**

**Files / targets.**
- **New:** `Server/syxx_server/doq_listener.py`.
  - `run(stop_event=None)`: create a fresh `asyncio` event loop, `loop.run_until_complete`ing
    `aioquic.asyncio.serve(...)` bound to `config.DOQ_HOST:config.DOQ_PORT` (udp) with a
    `QuicConfiguration(is_client=False, alpn_protocols=["doq"])`,
    `configuration.load_cert_chain(config.DOQ_TLS_CERT, config.DOQ_TLS_KEY)`. On bind/cert
    failure → `listeners.set_state("doq", "down", …)` and return; success → `"listening"`.
  - A `QuicConnectionProtocol` subclass whose stream handler implements RFC 9250: each DNS
    query arrives on its **own** client-initiated bidirectional stream; **the 2-byte length
    prefix is present** (RFC 9250 §4.2 frames the DNS message with the 2-octet length, same as
    DoT). **RFC 9250 requires the DNS message ID be 0** — accept ID 0, and bind the answer to
    the stream (do not rely on txid matching). Answer via the same scope logic as DoT
    (`dns_authoritative._build_response`, or forward for `config.DOQ_FORWARD_ALLOWLIST`).
    Write the response framed with its 2-byte length back on the same stream and end the stream.
  - Malformed/oversized/short frames → close the stream with the RFC 9250 error, never crash the
    loop (mirror the collector's "one bad packet never kills the loop" posture,
    `dns_authoritative.py:189-190`).
- **Edit `server.py`:** add a `doq_thread` guarded by `if config.ENABLE_DOQ` (default False),
  import `doq_listener` at `server.py:18`.
- **New config:** `ENABLE_DOQ = _b("SYXX_ENABLE_DOQ", False)`,
  `DOQ_PORT = _i("SYXX_DOQ_PORT", 853)`, `DOQ_HOST`, `DOQ_TLS_CERT`, `DOQ_TLS_KEY`,
  `DOQ_FORWARD_ALLOWLIST`.
- **Edit `requirements.txt`:** add `aioquic>=1.0.0` **with a comment** that it is optional and
  only pulled when `SYXX_ENABLE_DOQ=true` (it drags `pylsqpack` + `certifi`, breaking the
  stdlib-only posture — the comment must say so, per SD-59-style honest config).
- **Import guard:** `doq_listener` must `try: import aioquic` and, if absent, set
  `listeners.set_state("doq", "down", detail="aioquic not installed")` and no-op — so a deploy
  without the dep degrades honestly, never fail-silent (matches the program plan's
  "`[untested]` where the QUIC stack is unavailable, never fail-silent").

**Effort.** L (new dependency + new concurrency model + measurement gate).

**Real-data acceptance bar (VPS, only after D-4 says ship).**
- **Honest success:** `kdig +quic @syxx.app leak-canary.leak.syxx.app A` → real authoritative
  answer over QUIC, ALPN negotiated "doq", collector records the hit. Cross-check the handshake
  with `kdig +tls +quic` semantics per the client A3 bar.
- **RFC 9250 ID-0 correctness:** a compliant client sending message ID 0 gets a valid answer
  (prove the listener does not reject ID 0 as malformed).
- **Honest scope refusal:** `kdig +quic @syxx.app example.com` from a non-allowlisted host →
  REFUSED, never a fabricated answer.
- **Honest failure:** aioquic absent, or listener down, or :853/udp blocked →
  `kdig +quic @syxx.app` reports a refused/closed listener; `/readyz` shows `"doq":"down"` with
  the reason. **Never** a plaintext fallback answer.
- **Regression floor:** `tests/test_doq.py`, below.

**Dependency.** D-4 numbers (ship verdict) + aioquic installed + D-5 opens :853/udp.

---

### D-5 — systemd / firewall for :853 (lands with D-2, extended by D-3)

**Action.** Open :853/tcp (D-2) and :853/udp (D-3) on the VPS firewall, coordinate with SD-56's
proposed UFW allowlist, and validate any SD-53 `SystemCallFilter` tightening against aioquic's
QUIC/UDP syscalls.

**Files / targets.**
- **Edit `scripts/deploy.sh`:** the unit already grants `CAP_NET_BIND_SERVICE`
  (`deploy.sh:209`) — **no capability change needed**. If SD-56's UFW block lands (it proposes
  UFW for 80/443/53), it **must** also `ufw allow 853/tcp` and `ufw allow 853/udp`, or DoT/DoQ
  silently break behind the firewall. Add those two rules alongside the SD-56 allowlist; if
  SD-56 hasn't landed, add a minimal `ufw allow 853/tcp,udp` guarded so an existing UFW config
  isn't clobbered.
- **SD-53 interaction (tighten-then-test):** if/when `SystemCallFilter=@system-service` (and
  `RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX`, `MemoryDenyWriteExecute`) are added,
  aioquic's UDP path uses `sendmmsg`/`recvmmsg` and may use `UDP_SEGMENT` (GSO) setsockopt;
  `@system-service` includes these, but **`MemoryDenyWriteExecute` can break** C-extension JITs
  — `pylsqpack` is a compiled extension, so verify it loads under the sandbox. Procedure:
  tighten one directive at a time, restart, and run `kdig +quic @syxx.app` after each; the first
  restart that yields a refused/closed :853 identifies the offending directive. Do **not** ship
  a tightened unit without a passing real `kdig +quic`.
- **Firewall failure is honest:** if :853 is closed, `/readyz` still reports the listener as
  `listening` (the app bound successfully) even though clients can't reach it — note this gap
  explicitly. Consider an external reachability check (out of scope here; flag as a follow-on) so
  a firewall-blocked-but-bound listener isn't invisibly unreachable.

**Effort.** S (deploy script) + the validation runs.

**Real-data acceptance bar.** After deploy, from an off-VPS host: `nc -vz syxx.app 853`
(tcp) succeeds, `kdig +tls @syxx.app` and (if shipped) `kdig +quic @syxx.app` succeed. Before
the firewall rule, the same commands fail with a connection error (proves the rule is what
opened it, not a coincidence). `journalctl -u syxx-server` shows the listener bound.

**Dependency.** D-2 (tcp), D-3 (udp).

---

## 4. Pre-queued commands

**Local dev (before real runs).**
```
# from Server/ — run the full server test suite (baseline, must pass before adding listeners)
cd /Users/masterrabbit/Software/Syxx/Server && python3 -m unittest discover -s tests -v
# generate a throwaway cert for local DoT/DoQ tests
openssl req -x509 -newkey rsa:2048 -keyout /tmp/syxx-test-key.pem -out /tmp/syxx-test-cert.pem -days 1 -nodes -subj "/CN=localhost"
# run the new listener tests
cd /Users/masterrabbit/Software/Syxx/Server && python3 -m unittest tests.test_doh tests.test_dot tests.test_doq -v
```

**Install kdig (knot-dnsutils) for the real-run bar.**
```
# macOS: brew install knot     |     Ubuntu/VPS: apt-get install -y knot-dnsutils
```

**aioquic (ONLY after user approval AND D-4 ship verdict — do not run preemptively).**
```
# on the VPS, in a venv to avoid touching the stdlib-only system python
ssh root@74.208.237.162 'python3 -m venv /opt/SyxxServer/.venv && /opt/SyxxServer/.venv/bin/pip install "aioquic>=1.0.0"'
```

**D-4 measurement (on the VPS).**
```
ssh root@74.208.237.162 'free -m'
ssh root@74.208.237.162 'cd /opt/SyxxServer && .venv/bin/python3 scripts/doq_ram_probe.py --n 100 500 1000'
```

**Real-run bar (from an off-VPS host after deploy).**
```
openssl s_client -connect syxx.app:853 -servername syxx.app </dev/null | head
kdig +https=/dns-query @syxx.app example.com        # expect 402-driven failure (unauth)
kdig +tls  @syxx.app leak-canary.leak.syxx.app A    # expect authoritative answer
kdig +tls  @syxx.app example.com A                  # expect REFUSED (scope)
kdig +quic @syxx.app leak-canary.leak.syxx.app A    # only after D-3 ships
curl -s -o /dev/null -w '%{http_code}\n' 'https://syxx.app/dns-query?name=example.com&type=A' -H 'accept: application/dns-json'
```

---

## 5. D-4 measurement results (FILL IN FROM THE REAL VPS RUN — do not pre-populate)

| Metric | N=0 | N=100 | N=500 | N=1000 |
|---|---|---|---|---|
| RSS idle (MB, min/med/max) | | | | |
| per-conn idle (KB) | — | | | |
| RSS active peak (MB) | | | | |
| CPU% active | | | | |

`free -m` MemAvailable at measurement: __ MB. Reserved for co-residents (HTTP+DoT+collector+nginx+WAL): __ MB.
`max_safe_conns` (50% margin): __. Realistic peak concurrent DoQ conns: __.
**Ship / Defer verdict:** ____ (with one sentence of justification).

---

## 6. Tests (`Server/tests/`) — each with a mandatory failure case (AGENTS.md §3)

**`tests/test_doh.py`** (unit, `object.__new__(app.Handler)` pattern per
`test_server_hardening.py:20-29`; live-socket for the route per `test_routes.py:50-67`).
- Happy: monkeypatch `resolver.forward_wire` to return a known A-record wire; authenticated +
  allowlisted GET `?dns=` → 200 + that exact wire. **Failure case:** flip the monkeypatch to
  return a txid-mismatched response → `forward_wire` raises `DNSError` → route returns **502**,
  not 200 (proves spoof rejection; the test fails if the code passes the bad wire through).
- Auth: no `Authorization` header → **402** (paywall). **Failure case:** if `_require` is
  wired with `enforce=False` the test must catch the anonymous grace leak (expect 402, get 200
  → fail).
- Allowlist: non-allowlisted `_client_ip()` with `DOH_ALLOWLIST` set → **403**. **Failure
  case:** empty allowlist must *not* 403 (regression guard both directions).
- Hostile packet: POST `application/dns-message` bodies that are truncated, a compression-loop
  name, oversized (>1 MB via `_body` → `_BODY_TOO_LARGE`) → 400/413, **never a 500 or a
  fabricated answer**. **Failure case:** a body that would `IndexError` in a naive parser must
  return 400, not crash.

**`tests/test_dot.py`** (net-new live-socket harness: start `dot_listener.run` on an ephemeral
port with `/tmp/syxx-test-cert.pem`, connect with `ssl.create_default_context(...)` +
`check_hostname=False` for the self-signed test cert).
- Happy: TLS connect, send a framed in-zone leak query → authoritative answer; `leak.observe`
  recorded. **Failure case:** send an out-of-zone name (`example.com`) → **REFUSED** (rcode 5);
  the test fails if any answer record is returned.
- Connection cap: open `DOT_MAX_CONN + 5` sockets → the excess are closed/refused and the
  `dot_conn_rejected` metric increments. **Failure case:** with the cap removed the test must
  observe unbounded acceptance (proves the cap is load-bearing, not decorative).
- Framing: a 2-byte length prefix larger than the bytes actually sent, then close → the handler
  times out/closes cleanly, **no crash, no partial-read hang**. **Failure case:** a naive
  `recv(length)` that returns short must be caught (assert the connection closed, not that a
  malformed answer was produced).

**`tests/test_doq.py`** (net-new live-socket harness using an aioquic client;
`@unittest.skipUnless(_HAS_AIOQUIC, "aioquic not installed")` so it is honestly **skipped**,
never silently passing, where the dep is absent).
- Happy: QUIC connect ALPN "doq", open a bidi stream, send an ID-0 framed in-zone query →
  authoritative answer. **Failure case:** send message ID != 0 or a non-"doq" ALPN → the
  handshake/stream is rejected per RFC 9250; the test fails if a compliant ID-0 query is *also*
  rejected (that would prove the ID-0 handling is wrong) — i.e. one assertion proves ID-0 is
  accepted, its mirror proves malformed is refused.
- Loop resilience: a truncated/garbage QUIC stream payload → the stream is closed with an
  error, the listener stays up and still answers a subsequent good query. **Failure case:** if a
  bad frame takes the loop down, the follow-up query times out → test fails.

---

## 7. Risks, open questions, and honesty gaps

1. **DoH auth is header-based; `kdig +https` cannot send it.** The paywall gate means the
   canonical `kdig +https` real-run *without* a license header will always 402. That is the
   honest, intended behavior, but it means the "success" real-run needs `curl` with an
   `Authorization: Bearer` header (or a licensed client), not bare kdig. Documented in the D-1
   bar. Confirm with the user this is acceptable UX for a *paywalled* forwarder (decision 1
   asked exactly this: "is the server a resolver users point at, or only an encrypted channel
   for Syxx's own probes?"). If the intended consumers are Syxx's own probes, an IP-allowlist +
   session token is the right model and the kdig-402 is fine.
2. **Re-encode vs passthrough (design fork).** §D-1 recommends **raw passthrough** over the
   program plan's implied re-encode, on honesty grounds (re-encode drops record types →
   fabricated-looking answer). This is a deliberate deviation from the source plan's wording;
   surfaced here for sign-off. If passthrough is rejected, the re-encoded answer must be labeled
   lossy and cannot be presented as authoritative.
3. **DoT/DoQ scope default.** Defaulting to authoritative-leak-only makes the encrypted
   listeners "near-useless as a clean reference resolver" (decision 1(a)'s tradeoff) but avoids
   the open-forwarder DDoS-reflector risk the server's own `/api/exposure` warns against
   (`app.py:851-854`). The allowlist-forward escape hatch covers Syxx's own infra. Confirm the
   intended DoT/DoQ consumers so the allowlist can be pre-seeded.
4. **D-4 numbers are unknown until run.** The ship/defer verdict for D-3 genuinely cannot be
   predicted here; QUIC per-connection state is heavier than TCP and the VPS is single and
   shared. Treat "defer D-3" as a fully acceptable outcome, not a failure. Do not write the D-3
   listener speculatively.
5. **`/readyz` reports bound-not-reachable.** A firewall-blocked but successfully-bound listener
   reads `listening` while being unreachable (D-5). An external reachability probe would close
   this gap but is out of scope; flagged as a follow-on so it isn't forgotten.
6. **SD-53 vs cert group-read vs aioquic sandbox.** Three infra items interact: dropping root
   (SD-53), granting privkey group-read, and `MemoryDenyWriteExecute` breaking `pylsqpack`. This
   plan stages the cert group-read to be SD-53-forward-compatible and prescribes tighten-then-test
   for the syscall filter, but whoever lands SD-53 must re-run the DoT/DoQ real-run bar. Called
   out so it isn't a silent regression.
7. **`aioquic` version pin.** `>=1.0.0` is a placeholder; confirm the exact version against the
   VPS python (`python3 --version` on `74.208.237.162`) before install, since aioquic wheels
   track the interpreter/OpenSSL. This is a **dependency install → pause-and-report** point per
   AGENTS.md: do not install until the user approves the exact version.

**References I could not fully verify against the live tree (flagged, not propagated):**
- The program plan cites SD-51 at `app.py:925`; the live `ThreadingHTTPServer` construction is
  at `app.py:1028` and `serve()` at `app.py:1024-1038`. **[corrected]** in §1.
- SD-53/56/62 line numbers in the delta ledger (`deploy.sh:174`, `:209`,
  `dns_authoritative.py:136`) are ledger-relative and drift from the live `deploy.sh:175-216`
  and `dns_authoritative.py:127-150`; live anchors used above.

---

## 8. Handoff

- **Executor:** `syxx-engineer` (server lane) for all of D-0..D-5. No `syxx-gui-architect`
  work except approving/logging the handful of user-visible strings (D-1 403 wording,
  `/readyz` keys) to `outputs-catalog.csv`.
- **Suggested start point:** D-0 (health registry), then D-1 (DoH) — both ship real value with
  near-zero RAM and no new dependency, and D-0 unblocks honest health for everything after.
- **Blocking approvals before the gated steps:** (a) confirm decision 1 consumer intent +
  passthrough-vs-reencode (risk 1/2); (b) approve the exact `aioquic` version for install
  (risk 7) — a pause-and-report dependency gate; (c) sign off the D-1 403 string.
- **Do not mark any step `[done]`** until its real-run bar is met on the VPS with honest output
  — a green `test_doh/dot/doq` suite is the floor, not the bar (Protocol §2).
