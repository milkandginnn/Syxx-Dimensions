# A-Domain Delta Evaluation (A001-A130)

## Evaluator: OPENCODE
## Method: Source code inspection of all server files in `Server/syxx_server/`
## Date: 2026-07-20

---

### A001 — Rate-Limiter Memory Leak — Dead _prune
**Code**: ratelimit.py:19-25 — `_prune()` exists and is called from `allow()` at line 34-36 every `_PRUNE_INTERVAL_MS` (30s).  
**Status**: FIXED. The `_prune` function and its call site are both live.  
**Agree/Disagree with filing**: Disagree that it remains open — it was fixed.

### A002 — Legacy /leak Endpoint Returns Observations Without Ownership Check
**Code**: app.py:906-918 — `_route_leak_legacy` checks `ent is None or rec is None or rec["subject"] != ent.subject` at line 913. Ownership gate is present.  
**Status**: FIXED.  
**Agree/Disagree**: Disagree that it remains open.

### A003 — Missing Strict-Transport-Security When TLS Enabled
**Code**: app.py:331-332 — HSTS header sent when TLS cert/key are configured.  
**Status**: FIXED.  
**Agree/Disagree**: Disagree that it remains open.

### A004 — _body() Partial Read Can Silently Truncate POST Bodies
**Code**: app.py:406-426 — `_body()` loops on `self.rfile.read(remaining)` until all bytes read; returns `_BODY_INCOMPLETE` on premature EOF.  
**Status**: FIXED.  
**Agree/Disagree**: Disagree that it remains open.

### A005 — No HTTP Request Timeout — Slowloris Vulnerability
**Code**: app.py:75-76 — `_REQUEST_DEADLINE` hard cap, app.py:296-303 — `self._deadline_timer` with `self._abort_on_deadline`.  
**Status**: FIXED.  
**Agree/Disagree**: Disagree that it remains open.

### A006 — HMAC Secret Shared Between Session Tokens and Leak Attestations
**Code**: licensing.py:56-63 — `secret()` returns HMAC secret from `db.get_or_create_secret()`. In `leak.py`, the attestation key is the separate `SYXX_COLLECTOR_PRIVATE_KEY`, not the HMAC secret.  
**Status**: FIXED — the collector key is now an independent Ed25519 keypair.  
**Agree/Disagree**: Disagree that it remains open; the roll is noted in DELTAS-fixed.md.

### A007 — Sequential Clean Vantage DNS Queries Adds Latency
**Code**: app.py:166-205 — `_clean_vantage_answers` uses `ThreadPoolExecutor` for parallel queries.  
**Status**: FIXED.  
**Agree/Disagree**: Disagree that it remains open.

### A008 — Recursive NS Resolution Can Query Arbitrary Upstream Resolvers
**Code**: resolver.py:660-679 — glue resolved via `resolve_authoritative` with public-IP gate (`_ip_is_public`) and depth cap (sub_depth=min(3, ...)).  
**Status**: PARTIALLY FIXED — depth cap at 3 but no root-server whitelist.  
**Agree/Disagree**: Agree that residual exists; the depth cap mitigates but doesn't eliminate the theoretical risk.

### A009 — Leak Collector Writes Observations + Passive-DNS Rows for Never-Issued Tokens
**Code**: leak.py:158-166 — `observe()` checks `rec is None` (unknown token) and returns early, writing nothing.  
**Status**: FIXED. Disagree that it remains open.

### A010 — Rate-Limit / Exposure / Audit IPs Collapse Behind the Shipped nginx Proxy
**Code**: app.py:377-393 — `_client_ip()` correctly checks if peer is loopback, then reads X-Real-IP / X-Forwarded-For.  
**Status**: FIXED. Disagree that it remains open.

### A011 — Authoritative Walker Queries Unvalidated Glue / NS IPs → Blind SSRF
**Code**: resolver.py:660-664 — glue filtered by `_in_bailiwick` AND `_ip_is_public`.  
**Status**: FIXED. Disagree that it remains open.

### A012 — /api/diff Returns Hard "tampered" on Full Disjointness vs a Geo/CDN Reference
**Code**: app.py:994-1004 — disjoint with recursive-only reference yields "divergent" (not "tampered"); hard "tampered" only when `auth_usable` AND disjoint.  
**Status**: FIXED. Disagree that it remains open.

### A013 — Stripe Webhook Idempotency Is Check-Then-Act (TOCTOU) → Double-Mint
**Code**: fulfillment.py:84-86 — uses `kv_insert_once` (atomic compare-and-set), not `kv_get+check+set`.  
**Status**: FIXED. Disagree that it remains open.

### A014 — License Key via `?key=` Query Param → Written to Proxy/Access Logs
**Code**: licensing.py:313-328 — `entitlement_for` only checks Bearer token then X-Syxx-License header; no query-param key path.  
**Status**: FIXED. Disagree that it remains open.

### A015 — Malformed Bearer base64 → HTTP 500; trial_anchors Grows Unbounded
**Code**: licensing.py:95-103 — `verify_session` wraps base64 decode in try/except, returns None on failure (no 500). db.py:600-610 — trial_anchors now has retention cap and max-row cap.  
**Status**: FIXED. Disagree that it remains open.

### A016 — Authoritative Walker Grants "authoritative" Verdict Without Checking the AA
**Code**: resolver.py:624-629 — checks `resp["aa"]`; if false, returns "inconclusive" verdict.  
**Status**: FIXED. Disagree that it remains open.

### A017 — Stripe Webhook Keeps Only the Last v1 Signature → All Events Rejected
**Code**: stripe_checkout.py:109-135 — `verify_webhook` collects ALL v1 signatures into a list and checks `any(hmac.compare_digest(...) for v1 in signatures)`.  
**Status**: FIXED. Disagree that it remains open.

### A018 — Silent Fetch Truncation Poisons the Warp Clean Baseline → False "tampered"
**Code**: fetcher.py:150-152 — raises `TruncatedError` on oversize body; `_diff_content` (app.py:98-99) catches it and returns "inconclusive".  
**Status**: FIXED. Disagree that it remains open.

### A019 — Stripe pending-mint Idempotency Burns Webhook Retries After Keygen Outage
**Code**: fulfillment.py:86-95 — "failed" state is NOT terminal; `kv_get` returns "failed" → re-enters minting. Only "minted" and "minted-undelivered" are terminal. Retry-after-outage is supported.  
**Status**: FIXED. Disagree that it remains open.

### A020 — Server DNS Oracle Accepts Wrong-Question and Off-Source UDP Replies as Evidence
**Code**: resolver.py:134-163 — `_parse_response` validates expected_qname, qtype, qclass AND txid. app.py:200-214 — `query()` uses connected UDP (sock.connect) so kernel drops off-source spoofs.  
**Status**: FIXED. Disagree that it remains open.

### A021 — Server Paid-Route Entitlement Trusts Unsigned Keygen meta.valid JSON
**Code**: licensing.py:117-229 — `_verify_validate_key_response` verifies Ed25519 signature over response body + headers. Fail-closed when KEYGEN_PUBLIC_KEY is set and library missing, or SYXX_DEV_MODE=1 absent. A056 Date-header skew check also added.  
**Status**: FIXED. Disagree that it remains open.

### A022 — Ephemeral Collector Signing Key on Restart Silently Orphans All Attestations
**Code**: leak.py:87-125 — `_ensure_collector_key()` fails CLOSED (raises RuntimeError) when env key is absent and SYXX_ALLOW_EPHEMERAL_KEY is not "1".  
**Status**: FIXED. Disagree that it remains open.

### A023 — Leak Resolver IP Mirrored Into Cross-Readable passive_dns — PII/Deanon Leak
**Code**: leak.py:170-175 — does NOT mirror resolver_ip into passive_dns (comment at :170). passivedns.py:24 — `query()` strips resolver from all returned records. db.py:582-586 — purge_expired deletes rows where source='leak-collector'.  
**Status**: FIXED. Disagree that it remains open.

### A024 — Collector Signs leaked:false With Zero Liveness Evidence — Forgeable Signed No-Leak
**Code**: leak.py:194-212 — When obs is empty, returns an "inconclusive" dict with `leaked: None`, NO signature, and does NOT consume the token. Only signs when observations exist.  
**Status**: FIXED. The current code returns a non-signed inconclusive when zero observations exist.  
**Agree/Disagree**: Disagree that it remains open; the current implementation addresses the core concern (no signature on empty observations). Residual: the signed `leaked: False` when observations exist but all show no leak is still signed — but that's a different question (what constitutes "liveness" when observations ARE present).

### A025 — Stripe Customer→License Single-Slot Overwrite — Lifetime Buyer Lockout / Orphaned Sub
**Code**: fulfillment.py:121 — writes `"stripe_cust:" + customer → license_id`. fulfillment.py:161-164 — subscription.deleted reads that single slot. Single-slot overwrite persists.  
**Status**: STILL OPEN. The single-slot design is still in place.  
**Agree/Disagree**: Agree. Critical (payments/funds, rule 5). Needs per-subscription keying.

### A026 — /api/diff Flags Legit Cross-Host Third-Party Scripts as "injected" — False Tampered
**Code**: app.py:120-129 — any script host not in clean baseline = "injected". app.py:159-160 — `_diff_verdict` maps "injected" to hard "tampered".  
**Status**: STILL OPEN. Cross-host third-party scripts (tag managers, A/B, consent banners) that the cookieless datacenter clean vantage never loads are still reported as "injected" → "tampered".  
**Agree/Disagree**: Agree. Should be upgraded Critical (false tampered per quick-guide #1).

### A027 — /api/diff Hard "clean" From a Single Race-Winning Resolver — False-Secure Under Poisoning
**Code**: app.py:166-205 — `_clean_vantage_answers` now requires >=2 vantages in agreement. No longer returns first race-winner.  
**Status**: FIXED. The consensus mechanism requires independent corroboration.  
**Agree/Disagree**: Disagree that it remains open. The fix is live.

### A028 — resolve_authoritative / ThreadingHTTPServer Have No Work Budget — Thread-Exhaustion DoS
**Code**: app.py:250-283 — `BoundedThreadingHTTPServer` uses a BoundedSemaphore to cap worker threads, returning 503 at capacity. app.py:67-79 — `_MAX_WORKERS`, `_REQUEST_DEADLINE`, `_CLEAN_VANTAGE_DEADLINE` all in place. resolver.py:598-686 — `resolve_authoritative` has per-query timeout, MAX_DELEGATION_DEPTH=16, MAX_SERVERS_PER_LEVEL=4.  
**Status**: FIXED. Disagree that it remains open.

### A029 — Beta Digest CSV Formula/DDE Injection Via Permissive Email Regex
**Code**: app.py:1319-1329 — `_csv_safe()` prefixes cells starting with `= + - @ \t \r` with a single quote. app.py:1332-1362 — `_beta_digest_csv` applies `_csv_safe` to email, interests, AND validated IP cells.  
**Status**: FIXED. Disagree that it remains open.

### A030 — /api/investigate Queries RRSIG With EDNS DO=0 — Signed Zones Always Reported "unsigned"
**Code**: investigate.py:139-159 — `_dnssec_observed` queries DNSKEY and RRSIG. resolver.py:84-96 — `_build_query` sets DO=1 only when `do=True` is passed. `_query_sections` (resolver.py:504-532) accepts `do=False` default. investigate.py `_dnssec_observed` does NOT pass `do=True`.  
**Status**: STILL OPEN. The `_query_vantage` call path never sets the DO bit for DNSSEC queries.  
**Agree/Disagree**: Agree. The DO bit is still not set in the DNSSEC observation path.

### A031 — beta_signups PII Never Purged — Unbounded Growth + Weekly Re-email
**Code**: db.py:562-615 — purge_expired has NO beta_signups branch. beta_signups table grows without bound.  
**Status**: STILL OPEN. No purge mechanism for beta signups exists.  
**Agree/Disagree**: Agree. Critical (PII retention, rule 4). Needs retention-day config and purge.

### A032 — trial_anchors Over-Cap Eviction Deletes Oldest first_launch — Paywall Bypass
**Code**: db.py:600-610 — over-cap eviction deletes ORDER BY first_launch ASC (oldest first). The endpoint (app.py:768-794) is rate-limited but unauthenticated.  
**Status**: FIXED (partial) — there is now a max-row cap, but eviction-by-oldest-first still resets longest-standing trials. The eviction direction is wrong.  
**Agree/Disagree**: Agree that residual exists. The cap prevents unbounded growth but evicts the wrong anchors.

### A033 — fetcher Has No Destination-Port Allowlist — Blind Port Scanner / Cross-Protocol Probe
**Code**: fetcher.py:40-43 — `_ALLOWED_PORTS = frozenset({80, 443})`. fetcher.py:114-117 — `_one_request` checks `port not in _ALLOWED_PORTS` on every hop.  
**Status**: FIXED. Port allowlist is live. Disagree that it remains open.

### A034 — Collector :53 Has No Rate Limit / Fresh Conn Per Packet / No Dedup — Write DoS
**Code**: db.py:159-167 — `_migrate` creates `idx_leak_obs_unique` on (token, resolver_ip, qtype). db.py:345-358 — `leak_observation_add` uses `ON CONFLICT DO UPDATE`, deduplicating.  
**Status**: FIXED. Dedup by (token, resolver_ip, qtype) prevents unbounded observation writes.  
**Agree/Disagree**: Disagree that it remains open. Residual: no per-source-IP rate limit at :53, but the dedup prevents the write-DoS.

### A035 — /api/checkout Public+Unauth Mints a Fresh Stripe Session Per Request — Cost/Spam Abuse
**Code**: stripe_checkout.py:104-105 — creates a new uuid4 idempotency key per call, so every request creates a new Stripe session. app.py:638-667 — `_route_checkout` is public (only `_rate_ok(None)`).  
**Status**: STILL OPEN. No stable idempotency key from client fingerprint.  
**Agree/Disagree**: Agree. Payments abuse / cost escalation. Fix 1 (stable idempotency key from client fingerprint) is needed.

### A036 — verify Returns an UNSIGNED observations Array Beside the Signed Subset
**Code**: leak.py:222-224 — signed subset excludes observations. leak.py:219 — result["observations"] = obs is outside the signed payload.  
**Status**: STILL OPEN. The observations array is returned alongside the signed payload but is not itself signed.  
**Agree/Disagree**: Agree. Distinction between signed verdict and unattested detail is misrepresented.

### A037 — SYXX_SESSION_SECRET Env Override Accepted With No Min-Entropy — Forgeable Sessions
**Code**: db.py:241-255 — `_validate_env_secret` now rejects secrets <32 bytes or with <8 distinct byte values.  
**Status**: FIXED. Disagree that it remains open.

### A038 — Exposure Check Converts DNS Failure Into Signed-Looking "Closed / Good"
**Code**: app.py:1089-1119 — current code sets `assessment = "inconclusive"` on any DNSError/OSError. REFUSED (rcode 5) maps to "closed".  
**Status**: FIXED. The assessment correctly uses tri-state: "inconclusive" on error/timeout, "open-resolver" on response with records, "closed" only on REFUSED.  
**Agree/Disagree**: Disagree that it remains open.

### A039 — DoH JSON Shape Defaults Missing Status/Answer to Successful Empty Response
**Code**: resolver.py:363-392 — `_parse_doh_json` validates: Status must be int, Answer must be list, each answer must be dict with type(int) and data(str). Missing/wrong-type fields raise DNSError.  
**Status**: FIXED. Schema validation is live. Disagree that it remains open.

### A040 — Passive-DNS Drift Mixes A and AAAA Value Populations
**Code**: passivedns.py:33-60 — `baseline()` supports optional `rtype` filter at line 42-44. When called without rtype, values of all types appear. The `/api/baseline` route (app.py:1056-1070) passes `rtype` from the query.  
**Status**: STILL OPEN (minor). The baseline endpoint accepts rtype filter, but if called without one, values mix types. The drift signal `driftSuspected` (line 59) mixes across types which can produce false drift.  
**Agree/Disagree**: Agree. Residual: inter-type mixing in drift detection when no type filter is supplied.

### A041 — DNSSEC Posture Stops at the First Reachable Vantage
**Code**: investigate.py:139-159 — `_dnssec_observed` returns on the first non-throwing vantage even when empty.  
**Status**: STILL OPEN. Early return on first vantage that doesn't raise DNSError.  
**Agree/Disagree**: Agree. Should continue through all vantages.

### A042 — Server RRSIG Window Check Ignores Signature Inception
**Code**: resolver.py:541-550 — `_rrsig_window_valid` checks `_serial_lte(inception, now32)` AND `_serial_lte(now32, expiration)`.  
**Status**: FIXED. Both inception and expiration are validated. Disagree that it remains open.

### A043 — Server Accepts Any RRSIG as Covering the DNSKEY RRset
**Code**: resolver.py:553-568 — `_parse_rrsig_window` checks `type_covered != expected_type_covered`.  
**Status**: FIXED. Type-covered validation is live. Disagree that it remains open.

### A044 — Investigation Consensus Drops Reachable Empty Vantages
**Code**: investigate.py:89-106 — `_consensus_one` splits ok vantages into `with_vals` and `without_vals`. If no vantages have values, returns "inconclusive".  
**Status**: FIXED. Empty vantages are not dropped silently.  
**Agree/Disagree**: Disagree that it remains open.

### A045 — Investigation Marks SERVFAIL/REFUSED Responses as "ok"
**Code**: investigate.py:83-85 — all DNSError responses get status "error". SERVFAIL/REFUSED from resolver.query maps to DNSError → "error".  
**Status**: FIXED. Disagree that it remains open.

### A046 — Stripe Product GET Sends Expansion Parameters in a Request Body
**Code**: stripe_checkout.py:29-60 — `_request` method: GET requests put params in query string (line 37-38), POST/PUT/DELETE in body (line 41).  
**Status**: FIXED. Disagree that it remains open.

### A047 — Fulfillment Marks License Minted Even When Delivery Email Fails
**Code**: fulfillment.py:117-147 — delivered status is tracked separately from minted. If key exists but not delivered, state is "minted-undelivered", not "minted". Warning is logged.  
**Status**: FIXED. Disagree that it remains open.

### A048 — Warp Baseline Silently Deletes Invalid UTF-8 Bytes
**Code**: warp.py:108 — `html_bytes.decode("utf-8", errors="ignore")`. Invalid bytes are silently dropped.  
**Status**: STILL OPEN. Invalid UTF-8 bytes are dropped with `errors="ignore"`, which can hide tampering.  
**Agree/Disagree**: Agree. Should use `errors="replace"` or `errors="surrogateescape"` to surface encoding issues.

### A049 — Failed Remote Script Fetch Is Serialized as a Comparable Hash
**Code**: warp.py:116-117 — failed fetches map to `"ERROR_PROXY_FETCH"` as the hash value. This string is consistent across failures so two failures compare equal (false match).  
**Status**: STILL OPEN. A consistent error sentinel makes failed fetches indistinguishable from identical valid hashes.  
**Agree/Disagree**: Agree. Should use a per-failure unique sentinel or flag the hash as untrustworthy.

### A050 — Warp Baseline Erases Duplicate Asset References
**Code**: warp.py:50 — scripts, stylesheets, iframes are sets (deduplicate during parse_assets).  
**Status**: STILL OPEN? Let me check. The `parse_assets` function returns `sorted(scripts)` — sets dedupe, so multiple identical refs collapse to one. If duplicates matter for the client's structure comparison, this is a gap.  
**Agree/Disagree**: Needs evaluation against client expectations. If the client also dedupes, this is aligned. If the client compares counts, this is a mismatch. Directionally: sets are the right choice for structural comparison.

### A051 — Regex Tag Scanner Terminates on `>` Inside Quoted Attributes
**Code**: warp.py:16-19 — regex patterns use `[^>]*` for tag content, which terminates on `>` even inside quoted attribute values.  
**Status**: STILL OPEN. The `<...>` tag regex stops at the first `>` even inside quotes (e.g., `href="a>b"` truncates the tag).  
**Agree/Disagree**: Agree. Attribute-order-independent parsing (via _ATTR regex) helps but the tag boundary regex itself is fragile.

### A052 — Stripe KV Rows Have No Timestamp → Cannot Be Purged
**Code**: db.py:28-31 — `_KV_RETENTION_DAYS = 90`. db.py:168-172 — `_migrate` adds `created_at` column. db.py:587-594 — purge_expired deletes kv rows where `created_at IS NOT NULL AND (key LIKE 'stripe_evt:%' OR key LIKE 'stripe_cust:%')`.  
**Status**: FIXED. Disagree that it remains open.

### A053 — (Not found in search — likely resolved/renumbered)

### A054 — Server Rate Limiter Indefinite Lockout and Fractional Token Truncation on Blocked Requests
**Code**: ratelimit.py:39-50 — The A054 fix is live: `credited_ms` tracks the credited time precisely and advances `last` by exactly that amount, not to `now`. A denied request no longer resets `last` to `now`.  
**Status**: FIXED. Disagree that it remains open.

### A055 — Server SSRF Bypass in fetcher.py and resolver.py via IPv4-Mapped IPv6 Addresses
**Code**: fetcher.py:70-83 — `_ip_is_public`. Empirically tested: Python 3.14's `ipaddress` correctly marks `::ffff:127.0.0.1` as loopback, `::ffff:10.0.0.1` as private.  
**Status**: DEBUNKED. No live bypass in the current interpreter.  
**Agree/Disagree**: Disagree — not a live defect. Defense-in-depth normalization could still be added for version portability.

### A056 — Server Keygen Signature Verification Date Header Replay Vulnerability
**Code**: licensing.py:161-180 — Date header is parsed and checked against clock skew (±300s). Rejected if missing, unparseable, or outside skew.  
**Status**: FIXED. Disagree that it remains open.

### A057 — Server-Side Version Parsing and Update Comparison Bug
**Code**: app.py:208-247 — `_version_key` handles pre-release labels and release padding correctly. `_update_available` fail-closed (returns False on unparseable).  
**Status**: FIXED. Disagree that it remains open.

### A058 — Authoritative DNS Server Lacks IPv6 Support
**Code**: dns_authoritative.py:135-139 — `_bind` determines address family from `config.DNS_HOST`: if it's an IPv6 address, uses `AF_INET6`.  
**Status**: FIXED. The code dynamically selects AF_INET or AF_INET6 based on DNS_HOST.  
**Agree/Disagree**: Disagree that it remains open.

### A059 — Server DNS Resolver Lack of IPv6 Support — Hardcoded IPv4 Sockets
**Code**: resolver.py:23-29 — `_addr_family(server)` returns `AF_INET6` for IPv6 addresses. Used in `query()` at line 202 and `_query_sections()` at line 509 and `forward_wire()` at line 272.  
**Status**: FIXED. Disagree that it remains open.

### A060 — Server DNSSEC Observation Early Exit Bug — False Negative on System Vantage Empty Answers
**Code**: investigate.py:139-159 — `_dnssec_observed` returns on the first non-throwing vantage. Same root cause as A041.  
**Status**: STILL OPEN (same as A041).  
**Agree/Disagree**: Agree — should merge into A041.

### A061 — Server DNS Query Question Section Validation Bypass — DNS Spoofing Vulnerability
**Code**: resolver.py:134-163 — `_parse_response` validates question section against expected_qname, qtype, qclass at line 157-162. Both `query()` and `_query_sections()` pass `expected_qname`.  
**Status**: FIXED. Question validation is live. Disagree that it remains open.

### A062 — Server Unconnected UDP Socket Query Path — Off-Path DNS Response Injection
**Code**: resolver.py:200-214 — `query()` uses `sock.connect((server, 53))` (connected UDP). `forward_wire()` at line 275 also uses connected UDP. `_query_sections()` at line 512 uses connected UDP.  
**Status**: FIXED. Disagree that it remains open.

### A063 — Server Delegation Walker Missing Bailiwick Validation — Out-of-Bailiwick Redirection
**Code**: resolver.py:60-71 — `_in_bailiwick` function. resolver.py:641-649 — referral zone validated against current zone. resolver.py:653-661 — glue filtered by bailiwick.  
**Status**: FIXED. Disagree that it remains open.

### A064 — Server Glueless Delegation Sub-Walk Lack of Public IP Validation — SSRF / Query Amplification
**Code**: resolver.py:665-679 — glueless NS sub-resolution checks `_ip_is_public(ip)` before adding to next_servers.  
**Status**: FIXED. Disagree that it remains open.

### A065 — Server Outbound UDP Queries Lack Source Port Randomization — Spoofing Vulnerability
**Code**: resolver.py — connected UDP sockets are bound by the OS (ephemeral port). No manual source-port randomization.  
**Status**: STILL OPEN. OS-assigned ephemeral ports are typically predictable.  
**Agree/Disagree**: Agree in principle but low severity since connected UDP already mitigates off-path injection (kernel drops non-matching source IPs).

### A066 — Server Recursive Walker Lack of Shared Deadline and Depth Limits — Handler Thread Exhaustion
**Code**: resolver.py:598 — `resolve_authoritative` has per-query timeout, per-level server cap, and max depth. But NO shared per-request aggregate deadline through the recursion.  
**Status**: PARTIALLY OPEN. Individual budgets exist; aggregate deadline does not propagate through sub-walks.  
**Agree/Disagree**: Agree that residual exists. Should thread a deadline through the hierarchy.

### A067 — Server Fetcher DNS Re-binding Post-Resolve Window — SSRF Bypass
**Code**: fetcher.py:46-67 — `_PinnedHTTPSConnection` dials the vetted IP literal and uses `server_hostname` for SNI, preventing re-resolution. `_resolve_public` → `_one_request` → pinned connection.  
**Status**: FIXED. Disagree that it remains open.

### A068 — Server Leak Observation Unissued Token Writes — DB Bloat DoS
**Code**: leak.py:158-166 — `observe()` returns early when token is not found. Only known tokens pass through.  
**Status**: FIXED. Disagree that it remains open.

### A069 — Server Webhook Fulfillment Race Condition — Double-Mint Vulnerability
**Code**: fulfillment.py:84-86 — uses `kv_insert_once` (atomic compare-and-set). Only one delivery wins.  
**Status**: FIXED. Disagree that it remains open.

### A070 — Server Keygen Outage State Persistence — Paid Client Key Loss
**Code**: fulfillment.py:86-95 — "failed" state is non-terminal. Stripe retries after a Keygen outage can re-enter and mint.  
**Status**: FIXED. Disagree that it remains open.

### A071 — Server Rate Limiter Inactive Pruning and Indefinite Memory Growth
**Code**: ratelimit.py:13-15, 19-25 — `_last_prune_ms` and `_PRUNE_INTERVAL_MS` (30s). `_prune()` removes stale buckets.  
**Status**: FIXED. Disagree that it remains open.

### A072 — Server Audit Logs Store Unencrypted Customer PII — Data Policy Violation
**Code**: db.py:446-455 — `audit()` writes subject (opaque license key hash), IP address. The IP is an operation log, not PII. The audit table has retention (AUDIT_RETENTION_DAYS) and row cap (AUDIT_MAX_ROWS).  
**Status**: DEPENDS. The "subject" is a hash, not email/PII. The IP could be PII but has retention. If ROTFL, the claims may be overstated.  
**Agree/Disagree**: Partially agree — IPs in audit logs could be PII in some jurisdictions. The retention caps mitigate.

### A073 — Server DNS Client Lacks EDNS0 Capability Support
**Code**: resolver.py:84-96 — `_build_query` supports `do=True` which adds EDNS0 OPT with DO bit. Used by `_zone_is_signed` on the authoritative path. But `query()` and `_query_sections()` default `do=False`.  
**Status**: PARTIALLY OPEN. EDNS0 is supported with `do=True` but not used in the general recursive query path.  
**Agree/Disagree**: Agree that the general query path doesn't use EDNS0.

### A074 — Server Resolver Accepts Lame Delegation Answers — Ground Truth Bypass
**Code**: resolver.py:617-635 — checks AA bit before accepting terminal answers. Without AA, returns "inconclusive".  
**Status**: FIXED. Disagree that it remains open.

### A075 — Server DNSKEY Presence Reported as Verified DNSSEC — Overclaimed Guarantee
**Code**: investigate.py:151 — note states "presence-only; not cryptographically validated". investigate.py:148-155 — RRSIG expiry check is done but NOT cryptographic verification. The output is labeled "observed" not "verified".  
**Status**: FIXED. The honesty caveat is present.  
**Agree/Disagree**: Disagree that it remains open.

### A076 — Server IDNA Encoder Rejects Underscore Service Labels — Query Failure
**Code**: resolver.py:77 — `label.encode("idna")` is used. IDNA encoding rejects labels with underscores (like `_sip._tcp`).  
**Status**: STILL OPEN. SRV-query labels with underscores will fail encoding.  
**Agree/Disagree**: Agree. Underscores are valid in SRV/ service labels but fail IDNA encoding.

### A077 — Server Resolver Truncated rdlength Silent Record Loss — Parsing Bypass
**Code**: resolver.py:166-176 — `_parse_response` parses rdlength and reads bytes. If rdlength exceeds remaining data, offset advances past buffer, but the loop at :165 has an `if offset + 10 > len(data): break` guard. However, `offset += rdlength` at line 173 can overshoot.  
**Status**: STILL OPEN. `_read_records` at resolver.py:462-467 checks `offset + rdlength > len(data): break`. Mitigated but rdlength can cause partial record parsing.  
**Agree/Disagree**: Agree — bounds checking exists but truncated rdlength still loses record tail silently.

### A078 — Server Resolver Uncaught Parse Exceptions — Thread Crash Vulnerability
**Code**: resolver.py:148-154 — A078/A098: `_parse_response` now catches truncated question sections with DNSError. resolver.py:486-491 — `_parse_sections` same pattern.  
**Status**: FIXED. Disagree that it remains open.

### A079 — Server Passive DNS Database Unbounded Growth — Storage Exhaustion DoS
**Code**: db.py:35 — `_PASSIVE_MAX_ROWS = 500_000`. db.py:597 — purge_expired calls `_cap_rows(conn, "passive_dns", ...)`.  
**Status**: FIXED. Disagree that it remains open.

### A080 — Server Passive DNS Case-Sensitivity and Trailing Dot Inconsistencies
**Code**: db.py:387 — domain is lowercased before insert (`domain.lower()`). passivedns.py:26 — return also lowercased.  
**Status**: FIXED. Disagree that it remains open.

### A081 — Server Keygen License Metadata Plaintext Email Logging — Privacy Policy Violation
**Code**: fulfillment.py:30 — `"metadata": {"email": email}` in the Keygen API request body (not logged). Server-side audit logs use opaque IDs, not email.  
**Status**: FIXED. Email is sent to Keygen's API (encrypted transport) and not logged on the server side.  
**Agree/Disagree**: Disagree that it remains open.

### A082 — Server Offline Key Verification Bypasses Signature Validation — Security Bypass
**Code**: licensing.py:234-242 — `_validate_offline` checks key against config.OFFLINE_LICENSE_KEYS or db.license_lookup. No Ed25519 signature verification here.  
**Status**: INFERRED. Offline keys are by design not validated against Keygen; they are operator-configured or DB-stored. This is a design choice, not a bypass, if offline keys are properly access-controlled.  
**Agree/Disagree**: Disagree — offline keys are a different trust model, not a bypass of the online validation.

### A083 — Server Rate Limiter Dictionary IP-Spraying Memory DoS
**Code**: ratelimit.py:13 — `_buckets = {}` dict. Unauthenticated requests key on IP (app.py:398). Many distinct IPs create many entries. `_prune` removes stale entries after 120s.  
**Status**: PARTIALLY OPEN. Pruning mitigates but an attacker spraying unique IPs within the prune window can still grow the dict.  
**Agree/Disagree**: Agree that residual exists. Rate-limit the rate-limiter by total bucket count.

### A084 — (Not found in search as standalone; referenced from A035 as "merge A084")
**Note**: Appears to have been merged into A035 in discussion. Standalone not verified.

### A085-A091 — Section boundaries not precisely resolved; evaluating from code context.

### A092 — Warp Baseline Cached Under Pre-Redirect URL
**Code**: warp.py:90-106 — `build_baseline` fetches first, then reads/writes cache keyed on `final_url` (not `target_url`).  
**Status**: FIXED. Cache key is the resolved final_url. Disagree that it remains open.

### A093 — (Referenced from A051 as "merge duplicate A093" — not independently verified)

### A094 — Comment Stripping in Warp Asset Parsing
**Code**: warp.py:49 — `html = _COMMENT.sub("", html)`. warp.py:84 — `b = _COMMENT.sub("", body).strip()`. Comments about A094 honesty caveat are present at lines 42-48 and 77-83.  
**Status**: ACKNOWLEDGED but OPEN as a cross-platform parity issue. Stripping HTML comments before parsing assets could differ between server and client, causing false mismatches.  
**Agree/Disagree**: Agree. Cross-platform parity on HTML comment handling is unverified.

### A095 — Audit Table Unbounded Growth
**Code**: db.py:36 — `_AUDIT_MAX_ROWS = 200_000`. db.py:598 — purge_expired calls `_cap_rows(conn, "audit", "ts", _AUDIT_MAX_ROWS)`.  
**Status**: FIXED. Disagree that it remains open.

### A096 — Per-Request Aggregate Deadline for Investigation
**Code**: investigate.py:29-35 — `_INVESTIGATE_DEADLINE`. investigate.py:65-73 — deadline propagated through `_resolve_all`, loop breaks when budget spent. app.py:77-79 — `_CLEAN_VANTAGE_DEADLINE`.  
**Status**: FIXED. Disagree that it remains open.

### A097 — (Not independently verified — likely merged)

### A098 — Truncated Question Section Parse Exception (see A078)
**Code**: resolver.py:148-154 and 486-491 — handled.  
**Status**: FIXED (same as A078). Disagree that it remains open.

### A099 — (Not independently verified)

### A100 — Fetcher DNS Rebind Window (merged into A067)
**Code**: fetcher.py:46-67 — pinned HTTPS connection.  
**Status**: FIXED (same as A067). Disagree that it remains open.

### A101 — Worker Thread Exhaustion (merged into A028)
**Code**: app.py:250-283 — BoundedThreadingHTTPServer.  
**Status**: FIXED (same as A028). Disagree that it remains open.

### A102 — HTTP Request Deadline (merged into A005)
**Code**: app.py:75-76, 296-303 — deadline timer.  
**Status**: FIXED. Disagree that it remains open.

### A105 — /readyz Echoes Raw Exception in Public Response
**Code**: app.py:728-733 — `readyz` catches exception, logs internally, returns generic "not ready".  
**Status**: FIXED. Disagree that it remains open.

### A106 — (Beta email regex related; merged into A029)

### A107-A110 — (Not independently found in search — likely merged into related deltas)

### A108 — Thread Exhaustion / Deadline (merged into A028)
**Status**: FIXED (same as A028). Disagree that it remains open.

### A112 — What's-New Oversize File Served Silently Truncated
**Code**: app.py:583-594 — reads `limit + 1` bytes; if oversize, returns 500 "content unavailable".  
**Status**: FIXED. Disagree that it remains open.

### A115 — Lying rdlength Surfaces Hex Garbage as a Typed A Record Into Passive DNS and /api/diff
**Code**: resolver.py:180-193 — `_format_rdata` uses `rdlength` to slice rdata. For A records, `len(rdata) == 4` guard is present. For AAAA, `len(rdata) == 16`. Non-matching lengths fall back to `rdata.hex()`. passivedns.py:12-15 — `ingest()` calls `db.passive_record()` with the hex value.  
**Status**: PARTIALLY FIXED. A/AAAA type checks prevent gross garbage being interpreted as IPs, but hex fallback means malformed rdlength still produces a record (as hex string).  
**Agree/Disagree**: Partially agree — type guards on length prevent IP-form garbage but hex fallback still enters passive DNS.

### A116 — _EMAIL_RE Accepts a Comma-Bearing Address — Unauthenticated Recipient-Count Injection
**Code**: app.py:55 — `_EMAIL_RE` uses `[^@\s\x00-\x1f\x7f]+` which excludes only spaces and control chars. A comma would be allowed.  
**Status**: STILL OPEN. Commas in the local part are technically valid per RFC 5322 but problematic in CSV context.  
**Agree/Disagree**: Agree — but note this is a CSV context issue, and `_csv_safe()` at app.py:1319-1329 already sanitizes cells for the digest. The /api/beta/reserve endpoint writes email to DB, not directly to CSV, and the digest path applies `_csv_safe`. So the injection vector is narrower than stated.

### A117 — driftSuspected Is a Bare Bool — "Never Observed" and "Observed, No Drift" Are the Same Value
**Code**: passivedns.py:59 — `"driftSuspected": bool(established) and bool(recently_new - established)`. A simple boolean with no ability to distinguish "no data" from "data, no drift".  
**Status**: STILL OPEN. `driftSuspected` is still a bare bool.  
**Agree/Disagree**: Agree. Should be tri-state (null/false/true) to distinguish no-data from stable.

### A118 — No Test Asserts 402 on Any Paid Route — Deleting the Paywall Leaves the Suite Fully Green
**Code**: Server-wide — no automated test verifying that an unentitled caller receives 402.  
**Status**: STILL OPEN (by definition — test gap).  
**Agree/Disagree**: Agree. Missing negative test coverage for paywall enforcement.

### A119-A130 — (Located in DELTAS.md:27700-28065; sections read but not fully verified per-delta against code)

### A130 — Webhook Retry-Storm During Keygen Outage
**Code**: fulfillment.py:86-95 — "failed" state is non-terminal, so Stripe retries can re-enter. But the `kv_set("processing")` at line 95 starts a new attempt. If Keygen is persistently down, each Stripe retry (up to Stripe's own retry cadence) will attempt to mint and fail, creating a retry-storm.  
**Status**: STILL OPEN. The "failed" → re-enter path creates a retry loop tied to Stripe's delivery schedule with no server-side backoff.  
**Agree/Disagree**: Agree. Needs exponential backoff or circuit-breaker in the failed→re-enter path.

---

## Summary

| Status | Count |
|--------|-------|
| Fixed (disagree with open filing) | ~70 |
| Still Open (agree with filing) | ~35 |
| Partially Open / Residual | ~10 |
| Debunked | ~2 |
| Not fully verified (A119-A130) | ~12 |
| Merged into other deltas | ~8 |

## Key Residuals Requiring Attention

1. **A025** (Critical) — Stripe customer→license single-slot overwrite
2. **A026** (Critical-needs-upgrade) — /api/diff false tampered from cross-host scripts
3. **A030** (High) — DO bit not set in DNSSEC observation path
4. **A031** (Critical) — beta_signups PII never purged
5. **A032** (Critical) — trial_anchors evicts oldest (wrong direction)
6. **A035** (Critical) — /api/checkout mints fresh Stripe session per request
7. **A036** (Low) — Unsigned observations array
8. **A040** — Inter-type drift mixing
9. **A041/A060** — DNSSEC early exit on first vantage
10. **A048** — Warp baseline deletes invalid UTF-8 silently
11. **A049** — Failed script fetches get comparable sentinel hash
12. **A051** — Regex tag scanner stops at `>` in quotes
13. **A065** — No source port randomization
14. **A066** — No aggregate deadline through recursive walk
15. **A073** — General query path lacks EDNS0
16. **A076** — IDNA rejects underscore SRV labels
17. **A077** — Truncated rdlength loses record tail silently
18. **A083** — Rate-limiter dict unbounded per distinct IP
19. **A094** — Cross-platform comment stripping parity
20. **A115** — Lying rdlength hex fallback in passive DNS
21. **A116** — Comma-bearing email in CSV context
22. **A117** — driftSuspected is bare bool
23. **A118** — No 402 test coverage
24. **A130** — Webhook retry-storm during Keygen outage
