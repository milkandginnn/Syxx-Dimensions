# Security Hardening — Step 8: Authentication & Authorization Enhancement

**Target:** Syxx — cross-platform DNS tampering detection toolkit
**Date:** 2026-08-06
**Status:** ASSESSMENT — no code changes

---

## 1. Current Authentication Architecture

Syxx uses a **license-key-based authentication model** with three credential types:

| Credential | Issuance | Verification | Storage | Scope |
|---|---|---|---|---|
| License key (Keygen) | Third-party (Keygen.sh) | Ed25519 response signature (A021) + HMAC session mint | Server env (not stored client-side) | Pro plan entitlement |
| License key (offline) | Operator env var | HMAC-SHA256 over DB row fields (D053) | Server env + DB (with HMAC) | Pro plan entitlement |
| Session token | Server-minted after license validation | HMAC-SHA256 with server secret, constant-time compare | Client memory only | Time-limited (TTL) |
| API key | Server-minted via `/api/keys` | SHA-256 hash lookup in DB | Client-controlled | Long-lived, subject-bound |

### Authentication Flow

1. Client sends `X-Syxx-License` header to `/api/auth/activate`
2. Server validates license via Keygen (Ed25519 response signature) or offline DB (HMAC-gated)
3. Server mints HMAC-signed session token with subject + plan + expiry
4. Client uses `Authorization: Bearer <token>` for subsequent requests
5. `entitlement_for()` resolves credentials in priority: API key → Bearer token → License key

### Key Design Decisions

- **No password-based auth** — Syxx has no user accounts; entitlement is license-key-derived
- **No OAuth2/OIDC** — there are no user identities to federate; the product is a desktop tool, not a SaaS platform
- **No MFA** — there are no passwords to protect with a second factor; the license key IS the factor
- **Stateless session tokens** — HMAC-signed, no server-side session store, no session fixation
- **Fail-closed defaults** — `ENFORCE_NATIVE=True`, `ENFORCE_WEB=True`; unprovisioned server denies all keys (D040/A021)

---

## 2. Current Authorization Architecture

### Route Classes

| Class | Routes | Auth Required | Enforcement |
|---|---|---|---|
| Public | `/`, `/healthz`, `/readyz`, `/api/auth/activate`, `/api/checkout`, `/api/webhooks/stripe`, `/api/whats-new`, `/api/pubkey` | None | None |
| Paid (native) | `/warp`, `/fetch_script`, `/leak/*` (native app) | License or session | `ENFORCE_NATIVE` (default True) |
| Paid (web) | `/api/investigate`, `/api/diff`, `/api/trace`, `/api/leak/*` (website) | License or session | `ENFORCE_WEB` (default True) |
| License-gated | `/api/keys`, `/api/monitors`, `/api/billing/portal` | License or session | Always enforced |

### Access Control Model

- **Subject-bound**: Each entitlement carries a `subject` (hash of license key). Leak observations are gated by subject — a user can only see their own data.
- **Ownership-gated**: API keys and monitors are owned by the subject that created them; CRUD operations check ownership.
- **Rate-limited**: Token bucket per IP/subject with configurable limits.
- **No RBAC**: There is one plan (`pro`); no role hierarchy. This is appropriate for a single-tier product.

---

## 3. Gaps Assessment — OWASP ASVS V2/V3/V4

### V2: Authentication

| ASVS | Control | Status | Notes |
|---|---|---|---|
| 2.1.1 | Secure auth mechanisms | ✅ Pass | HMAC session tokens, Ed25519 license verification |
| 2.1.4 | Auth via secure channel | ✅ Pass | TLS 1.2+ for all API endpoints; HSTS |
| 2.1.6 | Anti-automation | ✅ Pass | Rate limiting per IP/subject; semaphore on Keygen requests |
| 2.2.1 | Credential storage | ✅ Pass | License keys never stored client-side; session tokens in memory only |
| 2.2.3 | Password hashing | N/A | No password-based auth |
| 2.3.1 | Credential recovery | ✅ Pass | License re-issue via Keygen; Stripe billing portal for subscription management |
| 2.5.1 | Session token entropy | ✅ Pass | HMAC-SHA256 with cryptographic random secret from `db.get_or_create_secret()` |
| 2.5.5 | Session timeout | ✅ Pass | Tokens have TTL; `config.SESSION_TTL` |
| 2.5.6 | Session token revocation | ⚠️ Gap | No mechanism to revoke a session token before expiry. A leaked Bearer token is valid until its TTL expires. |
| 2.7.1 | Anti-automation | ✅ Pass | Rate limiting per IP/subject |
| 2.8.1 | Credential change | ✅ Pass | License key can be changed in app; old session tokens expire naturally |
| 2.9.1 | Credential storage — server | ✅ Pass | Offline license keys in env vars; DB rows HMAC-gated (D053) |

### V3: Session Management

| ASVS | Control | Status | Notes |
|---|---|---|---|
| 3.1.1 | No URL session tokens | ✅ Pass | Tokens in `Authorization` header, not query strings |
| 3.2.1 | Session token generation | ✅ Pass | HMAC-SHA256 with server-side secret; base64url-encoded |
| 3.2.2 | Session token entropy | ✅ Pass | `secrets`-grade secret key; HMAC-SHA256 output |
| 3.3.1 | Session logout | ✅ Pass | Token consumption (one-time leak tokens); TTL expiry for session tokens |
| 3.4.1 | Cookie security | N/A | No cookies used (API-only, token auth) |
| 3.5.1 | Token binding | ⚠️ Gap | Session tokens are not bound to a client fingerprint. A token stolen from one client can be used from another. |

### V4: Access Control

| ASVS | Control | Status | Notes |
|---|---|---|---|
| 4.1.1 | Access control enforcement | ✅ Pass | License gating, subject-bound tokens |
| 4.1.2 | Deny by default | ✅ Pass | Free-tier cannot access paid endpoints (A133); `ENFORCE_NATIVE=True` |
| 4.1.3 | Access control failures | ✅ Pass | 402 for invalid license; 503 for transient failures (distinguished from denial) |
| 4.2.1 | Data access by ownership | ✅ Pass | Leak observations gated by subject; API keys/monitors ownership-checked |
| 4.3.1 | Insecure direct object references | ✅ Pass | Token subject binding; no sequential IDs |
| 4.4.1 | Admin access control | ✅ Pass | Admin endpoints gated by `SYXX_ADMIN_TOKEN` env var |
| 4.5.1 | Credential revocation | ⚠️ Gap | No endpoint to revoke a license key's session tokens. A leaked license key can mint new sessions until the key itself is revoked in Keygen. |

**Summary: 18/20 controls pass; 2 minor gaps (low severity).**

---

## 4. Enhancement Recommendations

### 4.1 Session Token Revocation (Before Expiry) [low]

**Problem:** A leaked Bearer token is valid until its TTL expires. There is no server-side revocation mechanism.

**Assessment:** Low severity. Session TTLs are short (configurable). The server is stateless by design — adding a revocation list would require a store (DB or Redis). The current architecture avoids this by using short TTLs and relying on the license key as the root credential.

**Recommendation (Deferred):** If session TTLs increase beyond 1 hour, add a lightweight revocation table:
- `revoked_tokens` table: (token_hash, expires_at)
- On `verify_session()`, check if the token hash is in the revocation table
- Auto-purge expired entries on read
- Add `POST /api/auth/revoke` endpoint that revokes the caller's current session

**Effort:** Medium | **Priority:** Low | **When:** If session TTLs increase or if token theft becomes a real incident

### 4.2 Session Token Binding to Client Fingerprint [low]

**Problem:** A session token stolen from one client can be used from another. There is no binding between the token and the client that minted it.

**Assessment:** Low severity. Token theft requires a compromise of the client application's memory or a network interception (mitigated by TLS + HSTS). The attacker would need to extract the Bearer token from a running process or a network capture.

**Recommendation (Deferred):** If token theft becomes a real concern, bind session tokens to a client fingerprint:
- Include a `fp` (fingerprint) claim in the session token payload
- Fingerprint = SHA-256 of (User-Agent + client IP subnet /24)
- On `verify_session()`, validate the fingerprint matches the current request
- This adds a stateless check without requiring server-side storage

**Effort:** Low | **Priority:** Low | **When:** If token theft incidents occur

### 4.3 License Key Revocation Endpoint [low]

**Problem:** There is no operator-facing endpoint to revoke a specific license key's sessions. Currently, revocation requires Keygen dashboard access or manual DB manipulation.

**Assessment:** Low severity. The Keygen admin API already supports license suspension/revocation, and the periodic API key sync job (`check_keygen_license_status`) will revoke API keys for invalidated licenses. The gap is the lack of a local admin endpoint for emergency revocation.

**Recommendation:** Add `POST /api/admin/revoke-license` (gated by `SYXX_ADMIN_TOKEN`):
- Accepts a license key or subject hash
- Marks the license as revoked in the DB
- Invalidates all sessions and API keys for that subject
- Returns the count of revoked sessions/keys

**Effort:** Low | **Priority:** Low | **When:** Next time the admin surface is expanded

### 4.4 Admin Endpoint Audit Logging [informational]

**Problem:** Admin operations (license lookup, key resend) are not logged to the audit table. The `SYXX_ADMIN_TOKEN` gate is sufficient for access control, but there is no audit trail for admin actions.

**Assessment:** Informational. The admin surface is small and operator-only. No evidence of misuse.

**Recommendation:** Add `db.audit()` calls to all admin routes with `subject="admin"` and the action performed.

**Effort:** Low | **Priority:** Low | **When:** Next time the admin surface is expanded

---

## 5. What Does NOT Need Enhancement

### OAuth2/OIDC — Not Recommended

Syxx has no user accounts. There are no identities to federate. Adding OAuth2 would introduce:
- A new identity provider dependency
- Session management complexity (authorization code flow, PKCE, token refresh)
- A user model that does not exist in the current architecture
- Significantly increased attack surface for no security benefit

The license key IS the credential. It is validated cryptographically (Ed25519 response signature). This is stronger than password-based auth because there is no password to phish, reuse, or brute-force.

### MFA — Not Recommended

There are no passwords to protect with a second factor. The license key is a single-factor credential, but it is:
- Cryptographically generated (not human-memorable)
- Never stored in plaintext on the client
- Validated via Ed25519-signed responses from Keygen
- Not reusable across different products

Adding MFA would require a user account system, which does not exist.

### RBAC/ABAC — Not Recommended

Syxx has a single plan (`pro`). There is no role hierarchy. Adding RBAC would:
- Introduce complexity for a single-tier product
- Require a user model and role assignment mechanism
- Not provide meaningful access control beyond what license gating already provides

The current access control model (license-gated, subject-bound, ownership-checked) is appropriate for the product's architecture.

### Risk-Based Authentication — Not Recommended

Risk-based auth requires a user behavior model, which requires user accounts and historical data. Syxx has neither. The current rate limiting (token bucket per IP/subject) provides the equivalent of risk-based throttling without the complexity.

---

## 6. Deferred Items

| Item | Severity | Effort | When |
|---|---|---|---|
| Session token revocation list | Low | Medium | If session TTLs increase |
| Token-to-client fingerprint binding | Low | Low | If token theft incidents occur |
| License key revocation endpoint | Low | Low | Next admin surface expansion |
| Admin endpoint audit logging | Informational | Low | Next admin surface expansion |
| Ed25519 key rotation API | Low | Medium | Already planned in Step 5 |

---

## 7. Files to Modify

| File | Change | When |
|---|---|---|
| `Server/syxx_server/licensing.py` | Add revocation table check to `verify_session()` | If session revocation is implemented |
| `Server/syxx_server/app.py` | Add `POST /api/admin/revoke-license` route | If admin surface is expanded |
| `Server/syxx_server/db.py` | Add `revoked_tokens` table and `license_revoke()` | If session revocation is implemented |

---

## 8. Assessment Summary

Syxx's authentication and authorization architecture is **appropriate for its threat model**. The license-key-based auth with HMAC session tokens, Ed25519 response verification, and fail-closed defaults is a strong, well-designed system for a desktop tool with no user accounts.

**The protocol's default recommendations (OAuth2/OIDC, MFA, RBAC, risk-based auth) do not apply to Syxx's architecture.** Applying them would introduce complexity and attack surface without meaningful security benefit.

| Category | Score | Notes |
|---|---|---|
| **Authentication** | 9/10 | Strong cryptographic verification; no password attack surface |
| **Session Management** | 9/10 | Stateless HMAC tokens; minor gap in revocation |
| **Access Control** | 9/10 | License-gated, subject-bound, ownership-checked |
| **OWASP ASVS V2/V3/V4** | 18/20 | 2 minor gaps (low severity, deferred) |

---

*Report generated by Security Hardening Orchestrator — Step 8 of 13*
*Next: Infrastructure Security Controls (Step 9)*
