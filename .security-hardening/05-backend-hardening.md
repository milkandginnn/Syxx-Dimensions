# Security Hardening — Step 5: Backend Security Hardening Plan

**Target:** Syxx — Python server (`Server/syxx_server/`)
**Date:** 2026-08-05
**Status:** PLAN — awaiting owner review before implementation

---

## 1. Current Security Posture

The server is stdlib-only (no framework) with 6 dependencies. It already has:
- Parameterized SQL queries, no injection vectors
- Multi-layer SSRF protection (IP pinning, private IP blocking, DNS rebind prevention)
- HMAC-SHA256 session tokens with constant-time comparison
- Rate limiting (token bucket per IP/subject)
- Request deadline (wall-clock timeout)
- HSTS, CSP, XFO, CORS with explicit allowlist
- No subprocess/exec/eval calls
- Ed25519 signing (fail-closed), Stripe webhook HMAC verification

**Overall: Strong.** This plan addresses the remaining gaps identified in the architecture review.

---

## 2. Gap: No Online Ed25519 Key Rotation

**Severity:** Low | **Priority:** Low | **Effort:** Medium

### Current State

The collector's Ed25519 private key is read from `SYXX_COLLECTOR_PRIVATE_KEY` env var at startup. Rotation requires a server restart, which causes a brief attestation gap.

### Plan

Add a key rotation API that supports dual-key overlap:

1. **New env var:** `SYXX_COLLECTOR_PRIVATE_KEY_NEXT` — the incoming key
2. **New endpoint:** `POST /api/admin/rotate-collector-key` (admin-only, not in the public API surface)
3. **Overlap period:** During rotation, both keys are valid for verification. The old key signs with a `key-transitions-at` timestamp; after that time, only the new key signs.
4. **Atomic swap:** The new key becomes the signing key; the old key remains in the verification set until a configurable TTL expires.
5. **Public key update:** `/api/pubkey` returns both keys during the overlap period with `notBefore`/`notAfter` timestamps.

### Implementation

- `config.py`: Add `COLLECTOR_PRIVATE_KEY_NEXT` env var
- `leak.py`: Check both keys for verification; sign with the newer key
- `app.py`: Add admin rotation endpoint (gated by `SYXX_ADMIN_TOKEN` env var, not license)

### Regression Tests

1. Verify attestations signed with old key are still accepted during overlap
2. Verify attestations signed with new key are accepted immediately
3. Verify after TTL expires, old key is no longer accepted
4. Verify `/api/pubkey` returns both keys during overlap

---

## 3. Gap: No Automated Dependency Scanning in CI

**Severity:** Low | **Priority:** Medium | **Effort:** Low

### Plan

Add `pip-audit` to the CI pipeline:

1. **CI step:** Add to `.github/workflows/ci.yml`:
   ```yaml
   - name: Python dependency audit
     run: |
       pip install pip-audit
       pip-audit -r Server/requirements.txt --desc
   ```
2. **Pre-commit hook (optional):** Add `pip-audit` to the pre-commit config for local checks
3. **Alerting:** CI failure on any High/Critical CVE in dependencies

### Regression Tests

1. CI fails when a known-vulnerable dependency is present
2. CI passes with the current dependency set (all clean)

---

## 4. Gap: SQLite PII Not Encrypted at Rest

**Severity:** Low | **Priority:** Low | **Effort:** High

### Current State

The SQLite database at `syxx-server.db` stores resolver IPs and email addresses in plaintext. File permissions (owner-only) are the current protection. The server runs as user `syxx` via systemd.

### Assessment

The risk is low because:
- The server runs on a dedicated VPS with no other users
- File permissions are `0600` (owner-only)
- The database is not exposed to the network
- Physical access to the server would be required to read the file

### Plan (Deferred)

SQLCipher integration is a significant change requiring:
1. Replace `sqlite3` module with `pysqlcipher3` dependency
2. Derive encryption key from `SYXX_DB_ENCRYPTION_KEY` env var
3. Migrate existing plaintext database on first startup
4. Add key management (rotation, backup)

**Recommendation:** Defer unless compliance requirements (SOC2, HIPAA) mandate encryption at rest. The current file-permission protection is adequate for a single-tenant VPS.

---

## 5. Gap: HSTS Preload

**Severity:** Low | **Priority:** Low | **Effort:** Low

### Plan

1. Add `preload` directive to HSTS header:
   ```python
   self.send_header("Strict-Transport-Security", "max-age=31536000; includeSubDomains; preload")
   ```
2. Submit to https://hstspreload.org/ after the header is live for at least 18 weeks at `max-age=31536000`
3. Ensure all subdomains serve HTTPS (leak.syxx.app, leaktest.syxx.app, macmotd.syxx.app, etc.)

### File Change

`Server/syxx_server/app.py:636-639` — add `; preload` to both HSTS branches

### Regression Tests

1. Verify HSTS header includes `preload` in both TLS and proxy paths
2. Verify all subdomains resolve over HTTPS before submitting

---

## 6. Gap: Secure Logging — No PII/Secrets in Logs

**Severity:** Informational | **Priority:** Low | **Effort:** Low (verification only)

### Current State

The `logging_util.py` module uses structured logging with specific field names. Sentry integration is configured with `send_default_pii=False`. OTel spans use `syxx.server.*` naming.

### Verification Plan

1. Grep all `logging_util.*` calls for PII patterns (email, IP, token values)
2. Verify Sentry `before_send` callback scrubs sensitive fields
3. Verify OTel span attributes do not include PII

### Assessment

Based on the codebase patterns (audit annotations A001–A178 document every security decision), the logging is already well-structured. No action likely needed beyond verification.

---

## 7. API Endpoint Security Review

### Routes Reviewed

| Route | Auth | Validation | Notes |
|---|---|---|---|
| `/api/auth/activate` | Public | Email parseaddr, license key format | ✅ Properly validated |
| `/api/checkout` | Public | Stripe session creation | ✅ Stripe handles validation |
| `/api/webhooks/stripe` | Public (HMAC) | Constant-time HMAC verify | ✅ Properly verified |
| `/api/investigate` | Paid | Domain validation, bounded inputs | ✅ Properly validated |
| `/api/leak/start` | Paid | Token generation, rate limited | ✅ A111: atomic claim |
| `/api/leak/check` | Paid | Token ownership verification | ✅ Subject-bound |
| `/api/whats-new` | Public | Static HTML, strict CSP | ✅ Locked-down CSP |
| `/api/pubkey` | Public | Read-only, no input | ✅ No validation needed |
| `/api/warp/raw` | Paid | URL validation via SSRF guard | ✅ Multi-layer SSRF |
| `/api/keys` | License | CRUD with ownership | ✅ Owner-gated |
| `/api/monitors` | License | CRUD with ownership | ✅ Owner-gated |

**No gaps found.** All paid routes require entitlement; all public routes have appropriate validation or are read-only.

---

## 8. Rate Limiting Assessment

### Current Implementation

Token bucket per IP/subject with configurable limits. The `listeners.py` module tracks per-IP counters.

### Assessment

- **Sufficient for current scale.** The server is single-tenant, low-traffic.
- **Potential gap:** The rate limiter may not handle distributed attacks from multiple IPs targeting the same endpoint. Consider adding a global endpoint rate limit.
- **Potential gap:** No rate limiting on the DNS resolver (port 53). The authoritative DNS server is separate from the HTTP server and may be vulnerable to amplification.

### Plan (Deferred)

1. Add a global per-endpoint rate limit (e.g., 1000 req/min across all IPs for `/api/investigate`)
2. Add DNS query rate limiting to the authoritative DNS server
3. These are low priority given the current deployment scale

---

## 9. Implementation Priority

| Gap | Priority | Effort | Action |
|---|---|---|---|
| HSTS preload | Low | Low | Add `; preload` to header — one line |
| Dependency scanning in CI | Medium | Low | Add `pip-audit` to CI workflow |
| Secure logging verification | Low | Low | Grep audit — no code changes |
| Ed25519 key rotation | Low | Medium | Defer to separate track |
| SQLite PII encryption | Low | High | Defer unless compliance mandates |
| Global rate limiting | Low | Medium | Defer to separate track |

---

*Report generated by Security Hardening Orchestrator — Step 5 of 13*
*Next: Frontend Security Implementation (Step 6)*
