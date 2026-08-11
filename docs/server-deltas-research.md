# Server-Side MUST FIX Deltas: Consolidated Research Output

## Delta A025: Stripe Customer→License Single-Slot Overwrite — Lifetime Buyer Lockout

**Criticality:** Critical (payments/funds)
**Location:** `Server/syxx_server/fulfillment.py:122,160-164`
**Current Behavior:**

- Line 122: `db.kv_set("stripe_cust:" + str(obj["customer"]), license_id)` — single slot overwritten on every purchase
- Lines 160-164: `customer.subscription.deleted` reads that single slot and suspends whatever it currently points at
**Minimal Fix:**

1. Key entitlements on `stripe_sub:<subscription_id>→license` (per-subscription, not per-customer)
2. Suspend by subscription ID, not by customer's current slot
3. Handle one-time/lifetime charges with a sentinel key that is never suspended
**Ripple Effects:**

- Affects all Stripe webhook handlers in fulfillment.py
- Requires database schema change (new key pattern)
- Must handle both subscription-based and one-time purchase flows
- A013's atomic idempotency fix prevents double-mint but doesn't solve cross-purchase slot collision

## Delta A026: /api/diff Flags Legit Cross-Host Third-Party Scripts as "injected" — False Tampered

**Criticality:** Critical (false tampered verdict)
**Location:** `Server/syxx_server/app.py:189-196,228-229`
**Current Behavior:**

- Line 189: `injected = sorted(local_script_set - clean_scripts)`
- Lines 195-196: `if injected_hosts: assessment = "injected"`
- Line 228-229: `if dns_assessment == "divergent" or c == "injected": return "tampered"`
- Baseline from `warp.build_baseline(url)` (warp.py:90-134) uses cookieless datacenter fetch
**Minimal Fix:**

1. Require corroboration before "tampered" (e.g., DNS divergence + content divergence)
2. Treat cross-host third-party divergence as `divergent`/inconclusive, not tampered
**Ripple Effects:**

- Affects `_diff_verdict()` function and all callers
- May need to adjust confidence scoring in synthesis engine
- Cross-ref A018, A012 (baseline-divergence handling)

## Delta A028: Server Authoritative Walks Lack Per-Request Deadline — Global Query Budget

**Criticality:** High (resource exhaustion)
**Location:** `Server/syxx_server/resolver.py:289-290,384-456; Server/syxx_server/app.py:598`
**Current Behavior:**

- `resolve_authoritative()` runs synchronously in request handler threads
- Per-query 3s timeouts with recursive NS resolution
- No global work/deadline budget
- Can accumulate many seconds worst-case per request
**Minimal Fix:**

1. Impose a global work/deadline budget for authoritative walks
2. Use bounded worker pool instead of unbounded background threads
**Ripple Effects:**

- Merged with A101 (thread exhaustion vector)
- Affects `resolve_authoritative()` and all routes that call it
- Must maintain DNS resolution accuracy while adding timeouts

## Delta A031: Server beta_signups Stores PII (Email, IP) Without Retention Window

**Criticality:** High (PII retention)
**Location:** `Server/syxx_server/db.py:345-361`
**Current Behavior:**

- Beta signups store email, IP, timestamps without expiration
- No purge mechanism exists
**Minimal Fix:**

1. Add retention window (e.g., 30 days)
2. Implement automatic purge job
3. Consider anonymizing after signup confirmation
**Ripple Effects:**

- Affects database schema and maintenance jobs
- May need migration to add expiration timestamps to existing records

## Delta A032: Server trial_anchors Over-Cap Eviction Deletes Oldest `first_launch`

**Criticality:** Medium (resource management)
**Location:** `Server/syxx_server/app.py:269-282`
**Current Behavior:**

- When trial anchors exceed cap, oldest `first_launch` is deleted
- May delete active trial data
**Minimal Fix:**

1. Evict least-recently-used anchors instead of oldest first_launch
2. Consider preserving anchors with active trials
**Ripple Effects:**

- Affects trial anchor storage and retrieval logic
- May need to track access patterns for LRU implementation

## Delta A035: Server /api/checkout Public Without Auth — Mints Fresh Stripe Sessions

**Criticality:** High (abuse vector)
**Location:** `Server/syxx_server/app.py:1368-1386`
**Current Behavior:**

- `/api/checkout` is public/unauthenticated
- Mints fresh Stripe session per request
- Can be abused for session flooding
**Minimal Fix:**

1. Add rate limiting
2. Consider requiring authentication or CAPTCHA
3. Add session reuse logic
**Ripple Effects:**

- Affects Stripe integration and user experience
- Must balance security with legitimate checkout flow

## Delta A036: Server Leak Attestation Excludes Per-Observation Detail from Signed Payload

**Criticality:** High (evidence integrity)
**Location:** `Server/syxx_server/leak.py:173-232`
**Current Behavior:**

- Per-observation detail excluded from signed payload
- Signed payload lacks granular evidence
**Minimal Fix:**

1. Include per-observation detail in signed payload
2. Maintain backward compatibility with existing verifiers
**Ripple Effects:**

- Affects evidence signing and verification
- May need versioned payload format

## Delta A041: Server DNSSEC Validation Stops at First Reachable Vantage

**Criticality:** High (detection efficacy)
**Location:** `Server/syxx_server/resolver.py:613-630`
**Current Behavior:**

- `_dnssec_observed()` returns after first reachable vantage
- May miss DNSSEC validation failures on other vantages
**Minimal Fix:**

1. Query multiple vantages for DNSSEC validation
2. Require consensus or majority for DNSSEC verdict
**Ripple Effects:**

- Affects DNSSEC validation logic and performance
- Cross-ref A044 (consensus logic)

## Delta A042: Server RRSIG Window Check Ignores Signature Inception

**Criticality:** High (DNSSEC validation)
**Location:** `Server/syxx_server/resolver.py:691-707`
**Current Behavior:**

- `_check_rrsig_expiry()` ignores signature inception
- May accept expired signatures or reject valid ones
**Minimal Fix:**

1. Check both inception and expiration times
2. Validate current time is within signature validity window
**Ripple Effects:**

- Affects all DNSSEC signature validation
- Must handle clock skew and timezone issues

## Delta A043: Server Accepts Any RRSIG as Covering DNSKEY RRset

**Criticality:** High (DNSSEC validation)
**Location:** `Server/syxx_server/resolver.py:727-735`
**Current Behavior:**

- RRSIG counted as DNSKEY evidence without `typeCovered` check
- May accept signatures for wrong record types
**Minimal Fix:**

1. Verify `typeCovered` field matches DNSKEY
2. Validate signature covers the correct RRset
**Ripple Effects:**

- Affects DNSSEC signature validation
- Must check all RRSIG fields properly

## Delta A044: Server Consensus Drops Reachable Empty Vantages from Count

**Criticality:** Medium (detection accuracy)
**Location:** `Server/syxx_server/app.py:196-201`
**Current Behavior:**

- Consensus logic drops reachable empty vantages from count
- May produce false consensus with incomplete data
**Minimal Fix:**

1. Include empty vantages in consensus calculation
2. Treat empty responses as evidence of censorship/tampering
**Ripple Effects:**

- Affects consensus logic across multiple routes
- Cross-ref A041 (DNSSEC validation)

## Delta A045: Server Maps SERVFAIL/REFUSED to status:ok

**Criticality:** High (false clean verdict)
**Location:** `Server/syxx_server/resolver.py:1054-1057,1083-1093`
**Current Behavior:**

- SERVFAIL/REFUSED mapped to `status:"ok"`
- Hides DNS errors as successful responses
**Minimal Fix:**

1. Map SERVFAIL/REFUSED to appropriate error status
2. Propagate error status through synthesis engine
**Ripple Effects:**

- Affects all DNS resolution paths
- May need to adjust error handling in clients

## Delta A048: Server Warp Baseline Decode Silently Strips Replacement Characters

**Criticality:** Medium (data corruption)
**Location:** `Server/syxx_server/warp.py:84-86`
**Current Behavior:**

- `decode("utf-8", errors="ignore")` silently strips replacement chars from baseline
- May corrupt HTML content
**Minimal Fix:**

1. Use `errors="replace"` to preserve data integrity
2. Or validate encoding before decoding
**Ripple Effects:**

- Affects baseline building and diff accuracy
- May need to adjust client-side decoding for parity

## Delta A055: Server SSRF Guard Misses IPv4-Mapped IPv6 Addresses

**Criticality:** Critical (SSRF bypass)
**Location:** `Server/syxx_server/fetcher.py:40-53; Server/syxx_server/resolver.py:30-42`
**Current Behavior:**

- `_ip_is_public` misses IPv4-mapped IPv6 (`::ffff:`)
- Allows SSRF via mapped addresses
**Minimal Fix:**

1. Unwrap IPv4-mapped IPv6 addresses before checking
2. Test for both IPv4 and IPv6 private ranges
**Ripple Effects:**

- Affects all SSRF protection in fetcher and resolver
- Critical security fix

## Delta A072: Server Audit Logs Store Raw IP and Email in Plaintext

**Criticality:** High (PII exposure)
**Location:** `Server/syxx_server/db.py:557-565`
**Current Behavior:**

- Audit logs store raw IP and email in plaintext
- PII exposed in logs
**Minimal Fix:**

1. Hash or anonymize IP and email in audit logs
2. Consider separate PII storage with access controls
**Ripple Effects:**

- Affects logging infrastructure and compliance
- May need log migration to anonymize existing data

## Delta A077: Server DNS Packet Parsing: rdlength Beyond Data Boundaries

**Criticality:** High (crash/security)
**Location:** `Server/syxx_server/resolver.py:162-177`
**Current Behavior:**

- `rdlength` beyond data boundaries causes silent truncation
- May lead to incorrect parsing or crashes
**Minimal Fix:**

1. Validate rdlength against remaining data
2. Handle malformed packets gracefully
**Ripple Effects:**

- Affects all DNS packet parsing
- Critical for handling hostile input

## Delta A093: Server Warp Script Tag Regex Allows Bypass

**Criticality:** High (detection evasion)
**Location:** `Server/syxx_server/warp.py:17-18`
**Current Behavior:**

- `_SCRIPT_TAG` regex allows `><script` tag bypass
- Attackers can hide script tags
**Minimal Fix:**

1. Improve regex to catch all script tag variations
2. Consider using HTML parser instead of regex
**Ripple Effects:**

- Affects script extraction and diff accuracy
- May need client-side parity updates

## Delta A097: Server Returns Egress Resolver IPs in Plaintext

**Criticality:** Medium (information disclosure)
**Location:** `Server/syxx_server/app.py:1597-1604,1733`
**Current Behavior:**

- Egress resolver IPs returned in plaintext
- Reveals infrastructure details
**Minimal Fix:**

1. Anonymize or hash resolver IPs in responses
2. Consider not returning resolver details to clients
**Ripple Effects:**

- Affects API responses and client behavior
- May need client updates to handle anonymized data

## Delta A099: Server Paid API Endpoints Accept Wildcard CORS

**Criticality:** Medium (security)
**Location:** `Server/syxx_server/app.py:118,418-420`
**Current Behavior:**

- Paid API endpoints accept wildcard CORS
- May allow unauthorized cross-origin access
**Minimal Fix:**

1. Restrict CORS to specific allowed origins
2. Implement proper origin validation
**Ripple Effects:**

- Affects all paid API endpoints
- Must balance security with legitimate cross-origin use

## Delta A106: Server Mailer Recipient Field Lack of Sanitization — SMTP Header Injection

**Criticality:** Critical (injection)
**Location:** `Server/syxx_server/app.py:1654-1655; Server/syxx_server/mailer.py:40-43`
**Current Behavior:**

- Email passed directly to SMTP header fields
- Python EmailMessage rejects CR/LF, but unvalidated control chars can raise ValueError
**Minimal Fix:**

1. Move header construction inside try/except
2. Validate email format before SMTP
**Ripple Effects:**

- Affects all email sending functionality
- Debunked as injection, but robustness note remains

## Delta A107: Server Insecure TLS Settings Allowed in Production SMTP Services

**Criticality:** Medium (MITM risk)
**Location:** `Server/syxx_server/mailer.py:52-66; Server/syxx_server/config.py:208`
**Current Behavior:**

- SMTP context drops cert verification when `SMTP_INSECURE_CERTS` is set
- Default is False, but unsafe escape hatch exists
**Minimal Fix:**

1. Permit only under explicit dev flag
2. Refuse startup when set in production
**Ripple Effects:**

- Affects SMTP configuration and deployment
- Lower severity from "unconditional production MITM"

## Delta A115: Server `_format_rdata` Returns `rdata.hex()` for Malformed A Records

**Criticality:** Critical (false typed answer)
**Location:** `Server/syxx_server/resolver.py:181-194`
**Current Behavior:**

- `_format_rdata` requires `len(rdata) == 4` for A record
- Returns `rdata.hex()` for malformed A records, labeled as type A
**Minimal Fix:**

1. Return error or inconclusive for malformed records
2. Don't label hex garbage as typed A record
**Ripple Effects:**

- Affects passive DNS and /api/diff
- Critical for data integrity

## Delta A117: Server `driftSuspected` Bool Conflates "Never Observed" with "No Drift"

**Criticality:** Medium (detection accuracy)
**Location:** `Server/syxx_server/app.py:297-303`
**Current Behavior:**

- `driftSuspected` bool conflates "never observed" with "no drift"
- May miss actual drift
**Minimal Fix:**

1. Use tri-state: no drift / drift detected / never observed
2. Adjust logic to distinguish between states
**Ripple Effects:**

- Affects drift detection and reporting
- Cross-ref A040 (passivedns baseline/drift logic)

## Delta A124: Server DoH JSON Path Queries Only `PUBLIC_RESOLVERS[0]`

**Criticality:** Medium (detection efficacy)
**Location:** `Server/syxx_server/app.py:1237-1243`
**Current Behavior:**

- DoH JSON path queries only first public resolver
- May miss resolver-specific issues
**Minimal Fix:**

1. Query multiple resolvers for DoH
2. Implement consensus or fallback logic
**Ripple Effects:**

- Affects DoH resolution and accuracy
- May need to adjust resolver selection logic

---

## Summary of Ripple Effects

### Critical Security Fixes (Immediate)

1. **A055**: SSRF bypass via IPv4-mapped IPv6 — affects all SSRF protection
2. **A115**: Malformed A records labeled as typed — affects data integrity
3. **A026**: False tampered verdicts — affects detection accuracy
4. **A045**: SERVFAIL/REFUSED mapped to ok — affects error handling

### High-Priority Detection Fixes

1. **A041**: DNSSEC stops at first vantage
2. **A042**: RRSIG inception ignored
3. **A043**: Any RRSIG accepted as DNSKEY
4. **A044**: Empty vantages dropped from consensus
5. **A093**: Script tag regex bypass

### Payment/Infrastructure Fixes

1. **A025**: Stripe single-slot overwrite
2. **A028**: No query budget
3. **A035**: Public checkout endpoint
4. **A107**: SMTP insecure TLS

### Evidence/Integrity Fixes

1. **A036**: Leak attestation excludes detail
2. **A072**: Audit logs store PII
3. **A097**: Resolver IPs in plaintext
4. **A117**: driftSuspected bool logic

## Implementation Considerations

### Cross-Platform Parity

- Many fixes affect both server and client (e.g., A048, A093)
- Client updates needed for decoding/encoding parity
- Test both platforms after changes

### Backward Compatibility

- Database schema changes need migration paths
- API changes need versioning or graceful degradation
- Client updates may lag server changes

### Testing Strategy

- Unit tests for each fix
- Integration tests for affected workflows
- Security tests for SSRF, injection, and validation fixes
- Performance tests for timeout/budget changes

### Deployment Order

1. Security fixes first (A055, A115, A026, A045)
2. Detection efficacy fixes (A041-A044, A093)
3. Infrastructure fixes (A025, A028, A035)
4. Evidence/integrity fixes (A036, A072, A097, A117)
