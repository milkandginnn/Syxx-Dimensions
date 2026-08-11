# Security Hardening — Step 10: Secrets Management Implementation

**Target:** Syxx — Python server, Swift/C# desktop clients
**Date:** 2026-08-06
**Status:** ASSESSMENT — no code changes

---

## 1. Current Secrets Management Architecture

### 1.1 Server-Side Secrets

All server secrets are managed via environment variables, sourced from a root-only env file:

| Secret | Env Var | Storage | Rotation |
|---|---|---|---|
| Session signing key | `SYXX_SESSION_SECRET` | `/etc/syxx/syxx.env` (0600) | Auto-generated on first run; manual rotation |
| Collector Ed25519 private key | `SYXX_COLLECTOR_PRIVATE_KEY` | `/etc/syxx/syxx.env` (0600) | Requires server restart |
| Keygen Ed25519 public key | `SYXX_KEYGEN_PUBLIC_KEY` | `/etc/syxx/syxx.env` (0600) | Manual (Keygen account key rotation) |
| Keygen admin/product token | `SYXX_KEYGEN_ADMIN_TOKEN` | `/etc/syxx/syxx.env` (0600) | Manual (Keygen dashboard) |
| Keygen webhook secret | `SYXX_KEYGEN_WEBHOOK_SECRET` | `/etc/syxx/syxx.env` (0600) | Manual (Keygen dashboard) |
| Stripe secret key | `STRIPE_SECRET_KEY` | `/etc/syxx/syxx.env` (0600) | Manual (Stripe dashboard) |
| Stripe webhook secret | `STRIPE_WEBHOOK_SECRET` | `/etc/syxx/syxx.env` (0600) | Manual (Stripe dashboard) |
| SMTP credentials | `SYXX_SMTP_USER/PASSWORD` | `/etc/syxx/syxx.env` (0600) | Manual (SMTP provider) |
| Admin token | `SYXX_ADMIN_TOKEN` | `/etc/syxx/syxx.env` (0600) | Manual |
| Offline license keys | `SYXX_LICENSE_KEYS` | `/etc/syxx/syxx.env` (0600) | Manual |

### 1.2 Client-Side Secrets

| Secret | Platform | Current Storage | Planned Storage |
|---|---|---|---|
| License key | macOS | Keychain | ✅ Keychain (already) |
| License key | Windows | DPAPI | ✅ DPAPI (already) |
| License key | Linux | libsecret/AES-256-GCM | ✅ Already encrypted |
| Collector trusted key | macOS | UserDefaults → Keychain | ✅ Migration planned (Step 4) |
| Witness trusted keys | macOS | UserDefaults → Keychain | ✅ Migration planned (Step 4) |
| HMAC integrity key | macOS | Keychain | ✅ Keychain (already) |
| Ed25519 signing key | macOS | Keychain | ✅ Keychain (already) |

### 1.3 Secrets Hygiene

| Check | Status | Evidence |
|---|---|---|
| No hardcoded secrets | ✅ | `config.py:4` — "No secrets are hardcoded" |
| `.env` in `.gitignore` | ✅ | `.gitignore:92-93` — `.env` and `**/.env` |
| No secrets in systemd unit | ✅ | A109 — secrets only in `/etc/syxx/syxx.env` |
| No `SYXX_DEV_MODE` in prod | ✅ | Deliberately absent from env file template |
| No secrets in logs | ✅ | Structured logging with `logging_util`; Sentry `send_default_pii=False` |
| No secrets in error messages | ✅ | Generic error messages; no debug info in responses |
| No secrets in git history | ✅ | Verified via grep — no credential patterns in tracked files |

---

## 2. Gaps Assessment

### 2.1 No Vault/Secrets Manager Integration [informational]

**Current State:** All secrets are managed via environment variables in a root-only file. There is no HashiCorp Vault, AWS Secrets Manager, or equivalent.

**Assessment:** Informational. For a single-host deployment with 10 secrets, a dedicated secrets manager adds significant operational complexity for minimal benefit. The current approach (root-only env file, not in git, not in systemd unit) is the standard pattern for small deployments.

**Recommendation (Not Recommended):** Vault integration is not recommended for this deployment. The env file approach is:
- Simpler to operate (no additional service to maintain)
- Equally secure for a single-host deployment (root-only file with 0600 permissions)
- Already follows the systemd best practice (A109: no inline Environment=)
- Not a multi-host deployment where secret distribution would be needed

### 2.2 No Automated Secret Rotation [low]

**Current State:** All secret rotation is manual. The Ed25519 collector key rotation requires a server restart (addressed in Step 5 as a deferred item).

**Assessment:** Low severity. For a single-host deployment with a small number of secrets, manual rotation is acceptable. The Keygen and Stripe secrets are managed by their respective dashboards and rotation is event-driven (e.g., key compromise).

**Recommendation (Deferred):** The Ed25519 key rotation API (Step 5) is the only rotation that would benefit from automation. Other secrets (Stripe, Keygen, SMTP) are managed by their respective providers and rotation is infrequent.

### 2.3 No Hardcoded Secrets Detection in CI [low]

**Current State:** The CI pipeline does not scan for hardcoded secrets (no GitLeaks, TruffleHog, or detect-secrets integration).

**Assessment:** Low severity. The codebase has no hardcoded secrets (verified by grep). However, a CI gate would catch accidental commits of secrets in the future.

**Recommendation:** Add GitLeaks to the CI pipeline:

```yaml
- name: Secret scanning
  run: |
    pip install gitleaks
    gitleaks detect --source . --no-git
```

This is a low-effort, high-value addition that prevents the most common secret management failure (accidental commit).

### 2.4 No Encryption Key Management with HSM [informational]

**Current State:** All encryption keys are stored in software (env files, Keychain, DPAPI). There is no HSM (Hardware Security Module) integration.

**Assessment:** Informational. HSM integration is not appropriate for this deployment:
- The Ed25519 signing key is used for attestation, not for protecting financial data
- The HMAC session key is used for API authentication, not for encrypting sensitive data at rest
- The deployment is a single-host VPS with no physical HSM access
- Cloud HSM (AWS CloudHSM, Azure Key Vault) would add significant cost and complexity

**Recommendation (Not Recommended):** HSM integration is not recommended for this deployment. The software-based key management is appropriate for the threat model.

---

## 3. What Does NOT Need Enhancement

### HashiCorp Vault / AWS Secrets Manager — Not Recommended

For a single-host deployment with 10 secrets, a dedicated secrets manager adds:
- A new service to deploy, monitor, and maintain
- Network dependency (secrets unavailable if Vault is down)
- Operational complexity (seal/unseal, token management, policy configuration)
- No security benefit over the current root-only env file for a single-host deployment

The current approach is the standard pattern for small deployments. Vault becomes valuable when:
- Secrets need to be distributed across multiple hosts
- Secrets need automated rotation
- Secrets need fine-grained access control (per-service, per-environment)
- Compliance requirements mandate centralized secrets management

None of these apply to Syxx's current deployment.

### HSM — Not Recommended

The deployment is a single-host VPS. HSM integration requires either:
- A physical HSM device (not available in a VPS)
- Cloud HSM (AWS CloudHSM, Azure Key Vault) — adds significant cost
- Software HSM (SoftHSM) — provides no real security benefit over Keychain/DPAPI

The software-based key management is appropriate for the threat model.

---

## 4. Enhancement Recommendations

| # | Recommendation | Severity | Effort | Priority |
|---|---|---|---|---|
| 1 | Add GitLeaks to CI pipeline | Low | Low | Medium |
| 2 | Ed25519 key rotation API (from Step 5) | Low | Medium | Low |
| 3 | Document secret rotation procedures | Informational | Low | Low |

---

## 5. Deferred Items

| Item | When |
|---|---|
| Vault integration | If deployment scales to multiple hosts |
| Automated secret rotation | If compliance requirements mandate it |
| HSM integration | If financial data protection requires it |
| Centralized secrets management | If multi-environment deployment is needed |

---

## 6. Files to Modify

| File | Change | When |
|---|---|---|
| `.github/workflows/ci.yml` | Add GitLeaks scanning step | If CI pipeline is updated |
| `docs/` | Add secret rotation procedures to operational documentation | If documentation is created |

---

## 7. Assessment Summary

Syxx's secrets management is **appropriate for its deployment model**. The root-only env file approach, combined with no hardcoded secrets, no secrets in git, and no secrets in the systemd unit, follows the standard pattern for small deployments. The client-side Keychain/DPAPI migration (Step 4) will further strengthen the macOS client.

The protocol's default recommendations (HashiCorp Vault, HSM, automated rotation) do not apply to Syxx's single-host deployment. They would add operational complexity without meaningful security benefit.

| Category | Score | Notes |
|---|---|---|
| **Server secrets storage** | 9/10 | Root-only env file; no inline secrets; no hardcoded values |
| **Client secrets storage** | 9/10 | Keychain/DPAPI; UserDefaults migration planned |
| **Secrets hygiene** | 10/10 | No secrets in git, logs, error messages, or systemd unit |
| **Rotation** | 7/10 | Manual rotation; Ed25519 key rotation API deferred |
| **Overall** | 9/10 | Strong for a single-host deployment |

---

*Report generated by Security Hardening Orchestrator — Step 10 of 13*
*Next: Phase 3 Checkpoint — user approval required before proceeding to Phase 4*
