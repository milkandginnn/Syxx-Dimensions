# Syxx Dimension — Server Delta Analysis

Vetting doc (per the delta workflow: this file = candidates only; agreed items graduate to ROADMAP.md).
Produced 2026-07-07 by five parallel reviewers over `Server/syxx_server/*` + `scripts/deploy.sh`.
Line numbers are approximate anchors. **Not yet vetted — review before acting.**

Legend: **[H]** high · **[M]** medium · **[L]** low · categories: Sec / Correct / Robust / Privacy / Perf.

---

## Tier 1 — High (security holes, money/availability, guaranteed breakage)

**SD-01** · [H] Sec · `resolver.py:83` — The oracle verifies only the DNS TXID + QR bit, never that the reply's question section echoes the queried QNAME/qtype, so a spoofed reply with the right guessed ID but wrong question is accepted as ground truth. **Fix:** parse the question section and reject any response whose QNAME (case-insensitive) / qtype don't match the query.

**SD-02** · [H] Sec · `resolver.py:137` — `recvfrom`'s peer address is discarded (`data, _ =`), so an off-path packet reaching the unconnected UDP socket is accepted. **Fix:** `connect()` the UDP socket (or verify the peer is `(server,53)`) so the kernel drops foreign packets.

**SD-03** · [H] Sec · `resolver.py:377` — The delegation walker follows NS names and additional-section glue without a bailiwick check, so a malicious parent/on-path answer can redirect the walk to attacker IPs. **Fix:** only accept NS at/under the current zone and glue at/under the delegated zone; discard the rest.

**SD-04** · [H] Sec · `resolver.py:385` — Glueless delegation recurses into `resolve_authoritative(nsname,…)`, which can resolve to and query *any* IP incl. private/loopback/link-local — SSRF + query amplification. **Fix:** reject NS targets resolving to private/reserved ranges (reuse the imported `ipaddress`) and cap total queries per walk.

**SD-05** · [H] Sec · `resolver.py:134` — UDP sockets aren't bound to a randomized source port, so only 16-bit TXID entropy defends against off-path poisoning of the ground-truth oracle. **Fix:** randomize the source port (or document reliance on OS randomization) as defense-in-depth with the TXID.

**SD-06** · [H] Robust · `resolver.py:354` — Bounds are per-level (depth 16 × 4 servers × 3s) and the glueless branch recurses, so worst-case wall-clock/queries per single API call multiply out. **Fix:** thread a shared deadline + total-query counter through the recursion; abort when either trips.

**SD-07** · [H] Sec · `fetcher.py:36` — `_ip_is_public("::ffff:169.254.169.254")` / `::ffff:127.0.0.1` returns True — IPv4-mapped IPv6 (and `64:ff9b::/96`) bypass the private check, reaching cloud metadata / loopback. **Fix:** when `addr.version==6` and `addr.ipv4_mapped`, re-check the embedded IPv4 with the same public rules.

**SD-08** · [H] Sec · `fetcher.py:80` — The vetted IP is resolved once, but on some Python versions the connect can re-resolve the hostname (DNS-rebinding window) rather than using the pinned IP. **Fix:** connect to the pinned IP only (SNI via `server_hostname`) and assert `sock.getpeername()[0]==ip` post-connect.

**SD-09** · [H] Sec · `leak.py:46` — `observe()` takes an attacker-chosen DNS label (any subdomain, ≤63B, unvalidated) and writes a new `passive_dns` + `leak_observations` row *even for tokens never issued* — unauthenticated DB-bloat DoS. **Fix:** look up the token first and return early if unknown; validate the label charset/length before any write.

**SD-10** · [H] Correct · `fulfillment.py:72` — Webhook idempotency reads `stripe_evt:<id>` and only writes the marker at the end via a *separate* connection, so two concurrent re-deliveries both pass the guard and mint two licenses. **Fix:** atomically claim the event id up front (`INSERT … ON CONFLICT DO NOTHING` returning whether newly claimed) before any Keygen work.

**SD-11** · [H] Correct · `fulfillment.py:94` — On a Keygen outage, mint returns `(None,None)`, the event is recorded `pending-mint`, and because the id is now stored the idempotency guard treats Stripe's retry as a duplicate → **the paid customer never gets a key.** **Fix:** don't persist the marker on mint failure (or store a distinct retryable state and raise so Stripe retries / a reconciliation job re-attempts).

**SD-12** · [H] Correct · `ratelimit.py:16` — `_prune` does `_buckets.clear()` (wipes *all* clients' budgets) and is never actually called, so there's no pruning at all. **Fix:** delete only entries older than `stale_before`, and invoke prune periodically under the lock.

**SD-13** · [H] Correct · `app.py:483` — `_route_trial_anchor` calls `int(time.time())` but `time` is never imported at module scope, so **every `/api/trial/anchor` request 500s** with NameError. **Fix:** add `import time` to the stdlib imports.

**SD-14** · [H] Privacy · `app.py:460,520` — `db.audit(...)` and `db.beta_reserve(email,…,self.client_address[0])` persist the raw source IP (and email), which the CSV digest then exports — directly violating the zero-PII-server-side goal. **Fix:** pass `None` for the IP (or a salted-truncated hash only if a coarse abuse signal is truly needed).

---

## Tier 2 — Medium (hardening, correctness, DoS surface)

**SD-15** · [M] Correct · `resolver.py:40` — No EDNS0 OPT, so queries advertise 512B and large DNSKEY/referral responses truncate → extra TCP or silent record loss. **Fix:** add an EDNS0 OPT (~1232B UDP) and keep TC handling.

**SD-16** · [M] Correct · `resolver.py:366` — Any response with an empty authority section is treated as authoritative even when `aa` is false, so a lame/forwarding server's empty answer is accepted as ground truth. **Fix:** require `aa` (or SOA-in-authority for NODATA) before declaring authoritative; else inconclusive/lame.

**SD-17** · [M] Correct(honesty) · `resolver.py:336` — `_zone_is_signed` returns True on mere DNSKEY presence and the field is `"signed"`, easily misread as DNSSEC-validated; no DS-from-parent linkage. **Fix:** label it `dnskeyPresent`, and state that no DS chain / RRSIG validation was performed.

**SD-18** · [M] Robust · `resolver.py:33` — `label.encode("idna")` rejects underscore/service labels (`_dmarc`, `_acme-challenge`), aborting queries for legitimate names. **Fix:** IDNA-encode only non-ASCII labels; fall back to raw ASCII for underscore/service labels.

**SD-19** · [M] Robust · `resolver.py:101` — A record whose `rdlength` exceeds remaining data is `break`-ed, but earlier records were already appended → a partial answer set returned as if complete. **Fix:** signal a parse error / mark the response `malformed` instead of silently truncating.

**SD-20** · [M] Robust · `resolver.py:378` — Section access like `resp["authority"][0]["owner"]` assumes well-formed sections; a crafted response raises IndexError/KeyError not caught by the `DNSError`-only handler → 500. **Fix:** map unexpected parse exceptions to DNSError/inconclusive.

**SD-21** · [M] Perf · `passivedns.py:15` — `ingest` appends indefinitely with no cap/prune, so the store grows unbounded and baseline/query scans degrade. **Fix:** retention policy (max rows/domain and/or max age) + index on `(domain,last_seen)`.

**SD-22** · [M] Correct · `passivedns.py:12` — `ingest` stores the domain as-given while query lowercases only on output, so `Example.com`, `example.com.`, `example.com` fragment into distinct keys and undercount history. **Fix:** normalize (lowercase, strip trailing dot, IDNA) on ingest and query.

**SD-23** · [M] Correct · `stripe_checkout.py:97` — During webhook-secret rotation Stripe sends multiple `v1=` sigs, but the parser keeps only the last, rejecting events signed with the other active secret. **Fix:** collect all `v1` values and accept if any matches via `compare_digest`.

**SD-24** · [M] Privacy · `fulfillment.py:31` — The buyer email is written into Keygen license metadata (`attributes.metadata.email`), stored indefinitely there — the "no PII" claim is only half-true. **Fix:** drop email from Keygen metadata (or hash it); rely on the opaque customer id already mapped.

**SD-25** · [M] Sec · `licensing.py:114` — `validate-key` reads the `"valid"` boolean straight from JSON with no response-signature check, so a MITM/DNS-hijack returning `{"meta":{"valid":true}}` grants Pro. **Fix:** use Keygen's cryptographic (Ed25519-signed) license/verification and verify the signature, not just the flag.

**SD-26** · [M] Perf · `ratelimit.py:13` — `allow()` inserts a bucket per distinct key and prune never runs, so spoofed-IP/key spraying grows the dict without bound (memory DoS). **Fix:** run the fixed prune on a schedule and/or LRU-cap the dict.

**SD-27** · [M] Robust · `stripe_checkout.py:80` — A transient timeout on session-create raises with no retry, and a fresh `uuid4` idempotency key per call means client retries create duplicate sessions. **Fix:** derive the idempotency key from a stable client request id + bounded retry on 5xx/OSError.

**SD-28** · [M] Correct · `fulfillment.py:103` — Only `subscription.deleted` suspends; there's no reactivation handler, and the `stripe_cust` key is overwritten on a second purchase — so a returning customer stays suspended and the first license is orphaned. **Fix:** handle reactivation to un-suspend; store license ids as a set per customer.

**SD-29** · [M] Sec · `licensing.py:53` — `_sign` uses the shared secret with no context tag, and the same secret signs "leak attestations," so a blob from one context could be replayed as a session token. **Fix:** domain-separate: prefix signed bytes with a fixed per-purpose label (e.g. `b"session:v1"`).

**SD-30** · [M] Robust · `fulfillment.py:42` — Keygen mint/suspend swallow all errors → success-shaped `None/False`, so a bad admin token (auth error/422) is indistinguishable from transient downtime, and audit records only 200/500. **Fix:** log the HTTP status/body and record a structured failure reason for reconciliation.

**SD-31** · [M] Sec · `fetcher.py:73` — Numeric-literal hosts (`http://0x7f000001/`, `http://2130706433/`) are handed to `getaddrinfo`, which accepts decimal/hex/octal IPv4 forms, potentially skipping the `ipaddress` vet. **Fix:** if the host parses as an `ip_address`, validate that literal directly before/instead of `getaddrinfo`.

**SD-32** · [M] Perf · `warp.py:89` — `max_bytes` is per-request, so one Warp run can pull HTML (5MB) + up to 40 scripts × 5MB ≈ 205MB, all buffered. **Fix:** per-run cumulative byte budget + smaller per-script cap.

**SD-33** · [M] Sec · `warp.py:89` — Warp re-fetches attacker-controlled script/iframe URLs (each a fresh DNS+connect), turning the endpoint into a fan-out SSRF/port-scan amplifier. **Fix:** dedupe + cap distinct hosts per run; prefer hashing only same-origin scripts.

**SD-34** · [M] Perf · `warp.py:109` — Every distinct `target_url` string caches a multi-MB HTML blob with no per-entry cap/eviction shown — cache inflation/poisoning. **Fix:** cap payload size, canonicalize/bound the key, add size/row eviction.

**SD-35** · [M] Correct · `warp.py:79` — Cache keys on the pre-redirect `target_url`, so two inputs redirecting to the same `final_url` cache separately and a changed redirect serves stale content. **Fix:** key on (or secondary-index by) `final_url`.

**SD-36** · [M] Correct · `warp.py:16` — Regex tag parsing (`<script\b[^>]*>`) fails on `>` inside quoted attrs and split/malformed tags, so a page can hide a malicious script from the clean-vantage diff. **Fix:** use stdlib `html.parser.HTMLParser`.

**SD-37** · [M] Correct · `warp.py:71` — `inline_scripts` strips "HTML comments" from JS bodies, but `<!--`/`-->` are valid JS substrings, mangling code so server/client inline hashes won't match. **Fix:** hash the raw inline body; don't comment-strip JS.

**SD-38** · [M] Robust · `investigate.py:58` — Every A/AAAA value from resolvers/DoH is ingested to passive DNS unbounded; a malicious authoritative can return many garbage answers, letting a paid caller inflate the index via crafted domains. **Fix:** cap values ingested per query, validate each parses as an IP, rate-limit passive writes.

**SD-39** · [M] Perf · `investigate.py:142` — A single `investigate()` fans A+AAAA across every vantage, then DNSKEY+RRSIG again, with only per-query timeouts → slow-loris resource holding. **Fix:** overall wall-clock budget, concurrent vantage queries / capped vantage count.

**SD-40** · [M] Privacy · `leak.py:51` — `observe` stores `resolver_ip` (potentially the user's real egress IP) into `leak_observations` *and* the long-lived passive-DNS index, and `verify` echoes every raw IP with no retention bound. **Fix:** bound retention, don't duplicate the IP into passive DNS, truncate/aggregate IPs in the response.

**SD-41** · [M] Robust · `app.py:633` — `/api/diff` calls `resolve_authoritative` without the try/except every sibling route uses, so a `DNSError` becomes a generic 500. **Fix:** wrap it and return `502 "resolution failed"` like the others.

**SD-42** · [M] Sec · `app.py:143` — `_cors()` sends `Access-Control-Allow-Origin: *` on all routes incl. paid ones reading `Authorization`/`X-Syxx-License`, letting any site drive a user's licensed endpoints from their browser. **Fix:** echo an allowlisted origin, or drop CORS on paid/native routes.

**SD-43** · [M] Sec · `app.py:658` — `/api/diff` accepts `payload["url"]` and fetches it with only a scheme check and no length/host/userinfo/port validation (unlike domains). **Fix:** validate + bound the url (max length, reject `@`/userinfo/odd ports) before fetching.

**SD-44** · [M] Perf · `app.py:598` — The authoritative walk (multi-round-trip UDP, up to max depth × 3s) runs synchronously on the per-connection handler thread, so a few slow/hostile domains exhaust threads. **Fix:** hard per-request wall-clock cap and/or run behind a bounded executor.

**SD-45** · [M] Robust · `app.py:207` — `_body()` reads exactly `Content-Length` bytes with no socket read timeout, so a client sending a big length then trickling bytes pins a thread (body slow-loris). **Fix:** set a per-request socket/read timeout; read the body in a bounded, timed loop.

**SD-46** · [M] Correct · `app.py:188` — `_require` returns `(None, True)` in the grace window; `_route_leak_token`/`_route_leak_verify` use `ent.subject` unconditionally → AttributeError 500 if those run with `ENFORCE_WEB` false. **Fix:** guard `ent` for None in the leak routes.

---

## Tier 3 — Low (polish / lower-likelihood, still worth doing)

**SD-47** · [L] Sec · `broadcast.py:42` — `url` is length-bounded but any scheme is accepted (`javascript:`/`data:`/`file:`), and `minVersion`/`maxVersion` are arbitrary strings reflected into the app. **Fix:** require `https://`; constrain versions to a semver regex.

**SD-48** · [L] Sec · `app.py:439` — `_route_readyz` returns `str(e)` from a failed `db.connect()` in the public 503 body, leaking DB paths/driver internals. **Fix:** log server-side; return a generic `{"error":"not ready"}`.

**SD-49** · [L] Sec · `mailer.py:42` — `msg["To"]=to` takes the Stripe email verbatim; `EmailMessage` blocks raw CRLF but an address-list value (`a@x, bcc@evil`) is accepted and fans out mail. **Fix:** `parseaddr` and reject anything whose parsed address ≠ input / contains commas.

**SD-50** · [L] Sec · `mailer.py:54` — `SMTP_INSECURE_CERTS` sets `CERT_NONE`, so a misconfig sends license-key emails over unauthenticated TLS (MITM reads the key). **Fix:** restrict to non-prod, log loudly on every insecure send, never allow with STARTTLS in prod.

---

## Tier 4 — Infrastructure (config / DB / entrypoint / deploy + systemd)

**SD-51** · [H] Robust · `app.py:925` — `ThreadingHTTPServer` spawns one unbounded thread per connection with no cap, so a keep-alive/slow-client flood exhausts memory/FDs and takes the single VPS down. **Fix:** bounded worker model (ThreadPoolExecutor-gated) + accept backlog/rlimit.

**SD-52** · [H] Sec · `scripts/deploy.sh:182` — The generated **session secret and SMTP password are still inline `Environment=` lines** in the unit (readable via `systemctl show syxx-server`), defeating the 0600 EnvironmentFile. **Fix:** move ALL secrets into the root-only EnvironmentFile; delete the inline `Environment=` secret lines.

**SD-53** · [H] Sec · `scripts/deploy.sh:174` — Daemon runs `User=root` with only `ProtectSystem=full`/`PrivateTmp`, so an RCE in the stdlib HTTP/DNS parser is instant root. **Fix:** dedicated user / `DynamicUser=`, add `ProtectHome`, `PrivateDevices`, `ProtectKernel*`, `RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX`, `MemoryDenyWriteExecute`, `SystemCallFilter=@system-service`.

**SD-54** · [H] Privacy · `db.py:82` — `beta_signups` stores plaintext email + client IP indefinitely and `purge_expired` skips the table — squarely violates zero-PII. **Fix:** retention window (or drop the `ip` column) and include it in `purge_expired`.

**SD-55** · [M] Robust · `server.py:32` — Only `KeyboardInterrupt` is caught; no SIGTERM handler / `httpd.shutdown()`, so `systemctl restart` hard-kills threads mid-transaction. **Fix:** SIGTERM/SIGINT handlers that drain + a stop event for the DNS thread.

**SD-56** · [M] Robust · `scripts/deploy.sh:209` — `Restart=always`+`RestartSec=5` with no `StartLimit` crash-loops forever on a config error; no firewall/fail2ban/backup provisioned. **Fix:** `StartLimitIntervalSec/Burst`, UFW (80/443/53), fail2ban, a SQLite `.backup` timer.

**SD-57** · [M] Robust · `scripts/deploy.sh:245` — `certbot` runs once but the renewal timer is never verified → cert silently expires in 90 days. **Fix:** enable + verify `certbot.timer` (or a renew timer) and log next-run.

**SD-58** · [M] Privacy · `db.py:72` — `audit` stores the client IP; behind nginx it's `127.0.0.1`, but if HTTP is bound to `0.0.0.0` directly it records real user IPs — PII the project claims not to collect. **Fix:** bind HTTP to `127.0.0.1` (force the proxy) + truncate/hash or drop the IP.

**SD-59** · [M] Robust · `config.py:98` — `HTTP_HOST` defaults to `0.0.0.0` (public bind even when a proxy is intended) and malformed int/JSON silently falls back to defaults (bad `SYXX_STRIPE_PRODUCTS_JSON` → silent). **Fix:** default `127.0.0.1`; add `validate()` that warns on parse-fallbacks and refuses contradictory config (e.g. `TLS_CERT` without `TLS_KEY`).

**SD-60** · [M] Privacy · `logging_util.py:16` — `_emit` serializes any `**fields` verbatim with `default=str`, so an accidental email/IP/token/key lands in stdout/journald. **Fix:** redact known-sensitive keys (email/ip/token/key/secret) in `_emit`.

**SD-61** · [M] Correct · `db.py:436` — `get_or_create_trial_anchor` is SELECT-then-INSERT, relying entirely on `BEGIN IMMEDIATE`; any plain `connect()` caller races. **Fix:** `INSERT … ON CONFLICT(fingerprint) DO NOTHING` then read back.

**SD-62** · [M] Robust · `dns_authoritative.py:136` — DNS falls back to `:5053` and rebinds every 60s, but nothing re-points delegation, so leak queries hit an unreachable port with no alert. **Fix:** error metric/log + fail health on the fallback port; gate the rebind loop on a stop event.

**SD-63** · [L] Perf · `db.py:424` — `warp_cache`/`passive_dns` purges full-scan (no index on `created_at`/`last_seen`), locking the writer longer as tables grow under WAL. **Fix:** add those indexes and/or batch deletes with `LIMIT`.

**SD-64** · [L] Robust · `config.py:63` — Blank `STRIPE_SECRET_KEY`/`_WEBHOOK_SECRET`/`KEYGEN_ADMIN_TOKEN` silently disable checkout/webhook/minting with no signal — a deploy that forgot the env looks healthy but sells nothing. **Fix:** warn at startup per unset secret that disables a route; expose their *presence* (not value) in readiness.

*DB non-finding (good):* every query is parameterized (no SQLi), and the fresh-connection-per-op + WAL + `BEGIN IMMEDIATE` pattern is a legitimately correct SQLite-under-threads approach. The gaps are missing bounds + deploy hardening, not the DB access pattern.

---

## Cross-cutting themes

- **Ground-truth integrity (SD-01–06):** the oracle an on-path attacker isn't supposed to be able to fabricate currently accepts unmatched questions, any source IP, out-of-bailiwick glue, and can be steered into internal IPs. Highest-value cluster.
- **SSRF surface (SD-04, 07, 08, 31, 33, 43):** several distinct bypasses/amplifiers across resolver, fetcher, Warp, and the diff route.
- **Unbounded/attacker-driven DB growth (SD-09, 21, 26, 34, 38, 40):** multiple paths let an unauthenticated or paid caller inflate storage.
- **Payment integrity (SD-10, 11, 12, 23, 27, 28, 30):** double-mint race, dropped-sale-on-outage, and the broken rate limiter touch real money/availability.
- **Zero-PII goal (SD-14, 24, 40):** client IPs and emails still land in the audit log, Keygen metadata, and leak/passive stores.

- **Deploy / systemd hardening (SD-51, 52, 53, 55, 56, 57):** unbounded thread pool, secrets readable via `systemctl show`, root with weak sandbox, no graceful shutdown, no crash-loop/firewall/backup guards, unverified TLS renewal.

**64 deltas total** (14 High · 40 Medium · 10 Low). All five reviewers in.

---

## Ledger (2026-07-27 triage — 44 new deltas)

Three parallel reviewers audited modules not covered by SD-01–SD-64: `bulk_investigate.py`,
`dnssec_validator.py`, `dot_listener.py`, `firewall_test.py`, `listeners.py`, `lookalike.py`,
`scheduler.py`, `subdomain_enum.py`, `whois_lookup.py`, `email_auth.py`, `scripts/`, plus
gaps in `app.py`, `seo.py`, `index.html`, `robots.txt`, `sitemap.xml`.

### Tier 1 — High (new)

**SD-65** · [H] Sec · `dnssec_validator.py:79-91` — Root DNSKEY query uses unconnected UDP with no response authentication; cache-poisoning the root key compromises the entire validation chain. Same class as SD-01/SD-02 but in the DNSSEC trust-anchor path. **Fix:** `sock.connect((srv, 53))` + question-section matching.

**SD-66** · [H] Privacy · `dot_listener.py:127-129` — DoT queries from non-allowlisted clients are written to the leak observation database; encrypted transport creates a privacy expectation that is silently violated. **Fix:** do not call `leak.observe()` for DoT connections, or gate behind an explicit opt-in defaulting to off.

**SD-67** · [H] Sec · `whois_lookup.py:~120` — RDAP redirect follower only checks `scheme=="https"` but never validates the redirect target host against private/reserved IP ranges — SSRF to cloud metadata. **Fix:** resolve redirect target hostname via `ipaddress` and reject private/reserved addresses.

**SD-68** · [H] Robust · `whois_lookup.py:~160` — Raw WHOIS TCP/43 read loop has no cumulative size cap; a malicious WHOIS server can exhaust server memory. **Fix:** enforce max response size (e.g., 64 KB).

### Tier 2 — Medium (new)

**SD-69** · [M] Correct · `bulk_investigate.py:53` — Uncaught `TimeoutError` from `as_completed` drops partial results and crashes with 500; cleanup loop is unreachable on that path. **Fix:** wrap in `try/except TimeoutError`, run cancellation in `finally`.

**SD-70** · [M] Robust · `bulk_investigate.py:50-63` — ThreadPoolExecutor threads stuck in `investigate()` are not killed on shutdown; repeated bulk calls accumulate phantom threads. **Fix:** add timeout parameter to `investigate()`.

**SD-71** · [M] Robust · `dnssec_validator.py:181-235` — No aggregate time budget; chain validation can run for minutes on deep domains (10 labels × 320s theoretical). **Fix:** accept `overall_timeout`, compute deadline, pass remaining time to each query.

**SD-72** · [M] Robust · `dnssec_validator.py:82-88` — TCP fallback trusts server-declared length prefix for allocation; malicious server can force 64KB allocation per connection. **Fix:** cap `ln` at configurable ceiling.

**SD-73** · [M] Sec · `dnssec_validator.py:188-220` — Validator accepts denial-of-existence without verifying NSEC/NSEC3 proof chains; forged "no DS" responses mask unsigned delegations. **Fix:** verify NSEC/NSEC3 denial-of-existence proofs before accepting.

**SD-74** · [M] Correct · `dnssec_validator.py:151` — KSK selection falls back to first DNSKEY when no SEP bit is set; no protocol field validation. **Fix:** filter by `protocol==3`; try all keys against DS records when no SEP key exists.

**SD-75** · [M] Sec · `dot_listener.py:35-42` — Connection semaphore acquired before TLS handshake; SYN flood without completing handshake exhausts 128 slots and DoSes the DoT listener. **Fix:** acquire semaphore after TLS handshake completes.

**SD-76** · [M] Correct · `dot_listener.py:80-83` — DNS error responses (SERVFAIL/FORMERR) never sent to DoT clients; all errors silently close the connection. **Fix:** construct and send minimal DNS error response on exception.

**SD-77** · [M] Sec · `dot_listener.py:116-121` — Forwarded queries for allowlisted IPs send raw client bytes to upstream resolvers with no query sanitization. **Fix:** parse and validate query before forwarding.

**SD-78** · [M] Perf · `firewall_test.py:128-154` — `test()` fires up to 30 sequential DNS queries with no parallelism; single API call can occupy a handler thread for ~2.5 minutes. **Fix:** parallelize with `ThreadPoolExecutor`.

**SD-79** · [M] Correct · `listeners.py:17,35-37` — "unknown" state does not trigger `/readyz` 503; a listener that never called `set_state` is silently treated as healthy. **Fix:** treat "unknown" same as "down", or add a startup-timeout mechanism.

**SD-80** · [M] Sec · `whois_lookup.py:~110` — `_rdap_query` opens plain HTTPConnection when bootstrap URL has scheme "http", sending the domain query in cleartext. **Fix:** refuse non-HTTPS bootstrap URLs or upgrade to HTTPS.

**SD-81** · [M] Perf · `whois_lookup.py:~25` — Thundering herd on IANA bootstrap fetch: lock released before network fetch, so N concurrent threads all issue the same request. **Fix:** use `threading.Event` for double-checked lock.

**SD-82** · [M] Robust · `lookalike.py:85` — `max_permutations` is caller-controlled with no server-side upper bound; can trigger 1M+ sequential DNS queries pinning a thread for hours. **Fix:** enforce hard cap + overall scan deadline.

**SD-83** · [M] Perf · `lookalike.py:95` — All DNS queries in `scan()` run sequentially; with 200 permutations at 10s timeout each, a single scan blocks a handler thread for ~200s. **Fix:** bounded `ThreadPoolExecutor` with shared deadline.

**SD-84** · [M] Correct · `scheduler.py:52-54` — "baseline" and "warp" scheduler keys map to identical `check_type` and identical lambdas; same expensive fetch runs twice every 60s tick. **Fix:** differentiate the checks or deduplicate before running.

**SD-85** · [M] Robust · `scheduler.py:90` — `_tick` iterates all due monitors synchronously; one slow monitor blocks all subsequent monitors in that tick. **Fix:** cap batch size per tick or dispatch to bounded thread pool.

**SD-86** · [M] Correct · `email_auth.py:57` — SPF "all" mechanism match uses `endswith("all")`, firing on any token ending in "all" (e.g., `include:_spf.sendgridall`). **Fix:** exact match against the 5 valid forms.

**SD-87** · [M] Sec · `app.py:294` — Metrics cardinality explosion: unbounded path keys in `http_endpoint_*` counters; attacker generates unlimited unique metric keys to OOM the process. **Fix:** whitelist known endpoint slugs, bucket unknown paths under "other".

**SD-88** · [M] Sec · `app.py:1702` — API key TTL accepts zero and negative values (`ttlDays:0` creates a key that expires immediately). **Fix:** `ttl = max(1, min(int(...), 365))`.

**SD-89** · [M] Correct · `app.py:1702,1739` — Unhandled `ValueError` on non-numeric integer fields returns 500 instead of 400. **Fix:** wrap in `try/except ValueError`, return 400.

**SD-90** · [M] Privacy · `index.html:26-27` — Google Fonts leaks visitor IP/UA to third party; for a DNS privacy product, this is a direct trust contradiction. **Fix:** self-host font files or use system fonts.

**SD-91** · [M] Sec · `app.py:1835` — DoH JSON endpoint lacks domain/name validation; user-supplied "name" passed directly to `resolver.query()` without `valid_domain()` check (every other DNS endpoint validates). **Fix:** validate name length (≤253) and format.

**SD-92** · [M] Sec · `app.py:1104` — `fetch_script` serves arbitrary upstream content as `application/javascript` regardless of actual content type. **Fix:** serve with actual upstream content type or validate it's JS.

**SD-93** · [M] Robust · `scripts/syxx-backup.sh:22` — Backup script captures Python stdout as path without verifying it's a valid file; a traceback line passes as "success" while no backup was created. **Fix:** verify output is an existing regular file before declaring success.

### Tier 3 — Low (new)

**SD-94** · [L] Correct · `dnssec_validator.py:107-118` — RRSIG signer name not verified against the zone being validated; cross-zone key-tag collisions could verify wrong signature. **Fix:** check `sig["sg"]` matches zone before crypto verification.

**SD-95** · [L] Sec · `dot_listener.py:147-149` — TLS context does not restrict cipher suites or set ALPN `["dot"]` per RFC 7858. **Fix:** set cipher string + `set_alpn_protocols(["dot"])`.

**SD-96** · [L] Privacy · `firewall_test.py:31-43` — Test domain names and monitoring categories reflected verbatim in API responses; leaks the product's filtering taxonomy. **Fix:** return aggregate results only, or gate detail behind Pro.

**SD-97** · [L] Correct · `firewall_test.py:77-91` — `_query_resolver` catches all exceptions identically; DNS protocol errors, network timeouts, and validation failures all reported as generic "error". **Fix:** catch `DNSError` separately from `socket.timeout`.

**SD-98** · [L] Correct · `listeners.py:25-30` — `set_state` accepts arbitrary unknown listener names without warning; typos create phantom entries. **Fix:** log warning when name not in `_KNOWN`.

**SD-99** · [L] Sec · `whois_lookup.py:~158` — Domain parameter sent verbatim to WHOIS server with no CR/LF sanitization; embedded `\r\n` could inject additional WHOIS commands. **Fix:** reject domains containing CR/LF.

**SD-100** · [L] Correct · `lookalike.py:79` — All `DNSError` exceptions silently mapped to `(False, [])`; network errors indistinguishable from "domain does not resolve". **Fix:** return tri-state (resolves/does-not-resolve/unknown).

**SD-101** · [L] Correct · `email_auth.py:112` — DKIM fallback queries 5 selectors sequentially; for domains without DKIM, this adds 5× timeout latency. **Fix:** parallelize fallback selector queries.

**SD-102** · [L] Robust · `scripts/doq_ram_probe.py:117` — `_mem_available_mb()` reads `/proc/meminfo` (Linux-only); on macOS raises `RuntimeError` with no catch. **Fix:** platform check at entry with clear error message.

**SD-103** · [L] Sec · `app.py:1370` — Trial anchor fingerprint has no length limit; attacker can send multi-MB fingerprints to bloat storage. **Fix:** cap fingerprint at 128 bytes.

**SD-104** · [L] Correct · `seo.py:183-188` — `PAGE_META` defines "title" for every page but `inject_meta_tags()` never writes or replaces the `<title>` element — dead code. **Fix:** inject `<title>` or remove from `PAGE_META`.

**SD-105** · [L] Correct · `seo.py:213-215` — `_TAG_RE` regex assumes fixed attribute order (`name` before `content`); HTML attributes can appear in any order, so dedup fails on non-canonical order. **Fix:** order-independent regex or HTML parser.

**SD-106** · [L] Sec · `seo.py:193` — JSON-LD block does not escape `</script>`; `json.dumps()` does not escape angle brackets. Current values are safe, but future metadata containing `</script>` would break out. **Fix:** replace `</` with `<\/` in serialized JSON.

**SD-107** · [L] Correct · `robots.txt:10-11` — Allow paths reference `.html` extensions that don't match actual server routes (`/leak-test.html` vs `/leak-test`). **Fix:** align with actual route paths.

**SD-108** · [L] Correct · `sitemap.xml` — `/leaktest` route missing from sitemap; only `/leak-test` listed. **Fix:** add entry or canonical redirect.

### New cross-cutting themes

- **DNSSEC trust-anchor fragility (SD-65, 71, 73, 94):** the DNSSEC validator's root key query, KSK selection, denial-of-existence handling, and signer-name verification are all independently exploitable. Combined with SD-01–06 (resolver oracle), the entire ground-truth chain has no authenticated segment.
- **Thread-exhaustion DoS (SD-70, 78, 82, 83):** bulk investigate, firewall test, lookalike scan, and scheduler all pin handler threads for minutes with sequential I/O. Combined with SD-51 (unbounded thread pool), a few concurrent API calls can exhaust the server.
- **Privacy self-contradiction (SD-66, 90, 96):** DoT query logging, Google Fonts third-party leakage, and firewall test taxonomy exposure all undermine the zero-PII/privacy-first positioning from different angles.

**108 deltas total** (18 High · 62 Medium · 28 Low). Three reviewers in.

---

## Corrections (2026-07-27 deep review)

Two findings from the initial triage are **incorrect** after source-level verification:

**SD-65 — REJECTED (false positive).** The agent claimed `dnssec_validator.py:79-91` uses unconnected UDP. The actual code at line 79 is `s.connect((srv, 53))` — the socket IS connected, and `_parse()` at line 55 DOES verify question-section matching (`if qn and (q.lower() != qn.lower() or qt2 != qt or qc != 1): raise DNSError("question mismatch")`). Both protections the agent said were missing are present. Downgrade the cross-cutting theme "DNSSEC trust-anchor fragility" accordingly — the root DNSKEY query is authenticated at the transport level; the remaining DNSSEC deltas (SD-71 KSK selection, SD-73 NSEC verification, SD-94 signer name) are valid but none are as severe as originally claimed.

**SD-79 — REJECTED (by design).** The agent claimed `listeners.py` should treat "unknown" state as "down" for `/readyz`. The module docstring explicitly documents this design: *"A never-started listener defaults to 'unknown', never 'listening': absence of a report is not evidence of health."* The `_KNOWN` tuple enumerates all expected listeners; a listener that is in `_KNOWN` but still "unknown" has genuinely never reported, which is a different diagnostic signal than an explicit "down". The agent confused a deliberate tri-state design (listening/degraded/down + unknown) with a missing check.

---

## Ledger addendum (2026-07-27 deep review — 3 new deltas)

**SD-109** · [H] Robust · `subdomain_enum.py:108-118` via `app.py:1645-1657` — Free-tier `/api/subdomains` route calls `enumerate()` which resolves up to 200 subdomains **sequentially** via `_resolve_subdomains()` (each at 3s timeout, no overall wall-clock deadline). A single request can pin an HTTP handler thread for ~10 minutes. This is **free-tier** (no entitlement required, only per-IP rate limit), making it a trivial thread-exhaustion DoS vector when combined with SD-51 (unbounded thread pool). An attacker with 6 IPs can exhaust the server in one batch. **Fix:** add overall deadline to `enumerate()`, parallelize DNS resolution with bounded `ThreadPoolExecutor`, or enforce a stricter per-IP rate limit on this specific route.

**SD-110** · [M] Robust · `scheduler.py:107-125` — `_sync_api_keys()` blocks the scheduler daemon thread while making sequential HTTPS calls to Keygen for every subject with active API keys. Each `check_keygen_license_status()` has a ~10s timeout. With 100 active subjects and Keygen degraded, this blocks the scheduler for ~17 minutes, during which NO monitors execute. The sweep is "daily" but its blocking behavior means monitors can miss multiple ticks. **Fix:** run `_sync_api_keys()` in a separate thread, or cap the batch size per sweep and resume on the next tick.

**SD-111** · [M] Correct · `email_auth.py:100-106` — `_dmarc_verdict()` returns "pass" when `p=reject` or `p=quarantine` is present with a `rua` tag, but never validates that the rua domain matches or is authorized by the policy domain (RFC 7489 §7.1 external authorization via `*.report._dmarc` TXT lookup). An attacker controlling a domain can set `rua=mailto:attacker@evil.com` and receive a "pass" verdict, even though DMARC aggregate reports would be sent to an unauthorized third party. **Fix:** either validate rua domain authorization or label the verdict "pass-unverified" to distinguish from fully validated DMARC.

### Updated cross-cutting themes

- **Thread-exhaustion DoS (SD-70, 78, 82, 83, 109):** add `subdomain_enum` to the thread-pinning cluster. This is the **worst offender** — it's free-tier (no entitlement gate), resolves 200 names sequentially, and has no overall deadline. Combined with SD-51, the entire server is trivially exhaustible.
- **DNSSEC trust-anchor fragility — REVISED (SD-71, 73, 94):** SD-65 was rejected (socket IS connected, question IS verified). The remaining DNSSEC deltas are valid correctness issues but none compromise transport-level authentication of the root key query. Severity of the cluster is lower than initially assessed.

**111 deltas total** (18 High · 64 Medium · 29 Low). Deep reviewer in.
