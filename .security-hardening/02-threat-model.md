# Security Hardening — Step 2: Threat Model & Risk Analysis

**Target:** Syxx — cross-platform DNS tampering detection toolkit  
**Methodology:** STRIDE + OWASP threat categories  
**Date:** 2026-08-05

---

## 1. System Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        CLIENTS (4 runtimes)                        │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────────────┐   │
│  │  macOS   │  │ Windows  │  │  Linux   │  │  Web (static)    │   │
│  │  Swift   │  │  C# WPF  │  │ C# Aval. │  │  HTML mockup     │   │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────────┬─────────┘   │
│       │              │              │                  │             │
│  Keychain       DPAPI/       libsecret/          Browser           │
│  (secrets)      %AppData     AES-256-GCM         (no auth)         │
└───────┼──────────────┼──────────────┼──────────────────┼───────────┘
        │              │              │                  │
        └──────────────┴──────────────┴──────────────────┘
                              │
                    HTTPS (TLS 1.2+)
                    SPKI pinning (D003)
                              │
┌─────────────────────────────┼───────────────────────────────────────┐
│                    SERVER (Python stdlib)                           │
│  ┌─────────────────────────┴────────────────────────────────────┐  │
│  │  HTTP Router (app.py)                                        │  │
│  │  ├─ Auth middleware (HMAC session tokens, license gating)     │  │
│  │  ├─ Rate limiting (token bucket per IP/subject)              │  │
│  │  ├─ Request deadline (wall-clock timeout)                    │  │
│  │  └─ Security headers (CSP, HSTS, XFO, X-Content-Type)       │  │
│  └──────────────────────────────────────────────────────────────┘  │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────────────┐  │
│  │  Leak    │  │Licensing │  │  Stripe  │  │  DNS Resolver    │  │
│  │ Collector│  │ (Keygen) │  │Checkout  │  │  (resolver.py)   │  │
│  │ Ed25519  │  │ Ed25519  │  │HMAC-SHA256│ │  UDP/TCP/DoH     │  │
│  └──────────┘  └──────────┘  └──────────┘  └──────────────────┘  │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐                        │
│  │  SQLite  │  │  SSRF    │  │  Config  │                        │
│  │ (WAL)    │  │  Guard   │  │  (env)   │                        │
│  └──────────┘  └──────────┘  └──────────┘                        │
└────────────────────────────────────────────────────────────────────┘
        │
        ├── DNS (UDP/TCP) → Public resolvers (1.1.1.1, 8.8.8.8)
        ├── HTTPS → Keygen.sh (license validation)
        ├── HTTPS → Stripe API (checkout)
        └── HTTPS → Authoritative DNS (resolver.py)
```

---

## 2. Trust Boundaries

| Boundary | Description | Crosses |
|---|---|---|
| **TB-1** | Internet ↔ Server | Client HTTP requests, DNS queries |
| **TB-2** | Server ↔ Third-party APIs | Keygen.sh, Stripe, public resolvers |
| **TB-3** | Server ↔ SQLite | Local DB queries (no network exposure) |
| **TB-4** | Client ↔ OS Secret Store | Keychain/DPAPI/libsecret access |
| **TB-5** | Client ↔ Local Storage | UserDefaults, %AppData%, ~/.config |
| **TB-6** | Server ↔ DNS Infrastructure | UDP/TCP queries, authoritative walks |
| **TB-7** | Client ↔ User | UI input, license key entry, settings |

---

## 3. STRIDE Threat Analysis

### S — Spoofing

| ID | Threat | Trust Boundary | Risk | Mitigation | Gap |
|---|---|---|---|---|---|
| S-01 | Attacker spoofs Stripe webhook | TB-1 | Medium | HMAC-SHA256 verification with constant-time compare | ✅ Mitigated |
| S-02 | Attacker spoofs Keygen license response | TB-2 | Medium | Ed25519 signature verification | ✅ Mitigated |
| S-03 | Attacker spoofs collector API | TB-1 | Medium | SPKI certificate pinning (D003) | ✅ Mitigated |
| S-04 | Attacker spoofs DNS response to resolver | TB-6 | Low | Random TXID, connected UDP, bailiwick checking | ✅ Mitigated |
| S-05 | Attacker replaces collector public key in UserDefaults | TB-5 | Medium | SYXX-VULN-006 — key in UserDefaults, not Keychain | ⚠️ Partially mitigated |
| S-06 | Attacker forges leak attestation | TB-1 | Low | Ed25519 signature; fail-closed without key | ✅ Mitigated |

### T — Tampering

| ID | Threat | Trust Boundary | Risk | Mitigation | Gap |
|---|---|---|---|---|---|
| T-01 | SQL injection via API input | TB-1→TB-3 | Low | Parameterized queries; f-string SQL uses constants only | ✅ Mitigated |
| T-02 | SSRF via user-provided URL | TB-1→TB-2 | Medium | IP pinning, private IP blocking, DNS rebind prevention, port allowlist | ✅ Mitigated |
| T-03 | DNS cache poisoning | TB-6 | Low | Random TXID, connected UDP, bailiwick checking | ✅ Mitigated |
| T-04 | Baseline/sentinel file tampering (local) | TB-5 | Low | HMAC-SHA256 integrity tags (D030) | ✅ Mitigated |
| T-05 | CSP bypass via XSS injection | TB-1 | Medium | CSP with `unsafe-inline` for Stripe | ⚠️ SYXX-VULN-001 |
| T-06 | HTTP request smuggling | TB-1 | Low | Transfer-Encoding rejection (A144), duplicate Content-Length rejection | ✅ Mitigated |
| T-07 | Warp manifest tampering | TB-1 | Medium | SHA-256 comparison (declared vs fetched) | ✅ Mitigated |

### R — Repudiation

| ID | Threat | Trust Boundary | Risk | Mitigation | Gap |
|---|---|---|---|---|---|
| R-01 | User denies performing leak test | TB-1 | Low | Ed25519-signed attestation with subject binding | ✅ Mitigated |
| R-02 | User denies license activation | TB-1 | Low | Keygen license audit trail + Ed25519 response | ✅ Mitigated |
| R-03 | Server denies receiving webhook | TB-2 | Low | Stripe webhook signature verification + timestamp | ✅ Mitigated |

### I — Information Disclosure

| ID | Threat | Trust Boundary | Risk | Mitigation | Gap |
|---|---|---|---|---|---|
| I-01 | Leak resolver IPs exposed to other users | TB-3 | Medium | A023: passive_dns mirror removed; observations gated by ownership | ✅ Mitigated |
| I-02 | License key intercepted in transit | TB-1 | Low | TLS 1.2+ required; SPKI pinning | ✅ Mitigated |
| I-03 | SQLite database file read by local attacker | TB-3 | Low | File permissions; WAL mode | ⚠️ Verify file permissions |
| I-04 | Session token brute-force | TB-1 | Low | HMAC-SHA256 with cryptographic random secret; rate limiting | ✅ Mitigated |
| I-05 | Sentry DSN exfiltration | TB-1 | Low | DSN from env var; read-only error reporting | ✅ Mitigated |

### D — Denial of Service

| ID | Threat | Trust Boundary | Risk | Mitigation | Gap |
|---|---|---|---|---|---|
| D-01 | API rate-limit bypass | TB-1 | Medium | Token bucket per IP/subject | ✅ Mitigated |
| D-02 | Slow-drip request attack | TB-1 | Medium | Request deadline (wall-clock timeout) | ✅ Mitigated |
| D-03 | Unbounded DB writes via leak observation | TB-1 | Medium | A009: unissued tokens rejected; A034: observation dedup | ✅ Mitigated |
| D-04 | Concurrent Keygen requests | TB-2 | Low | Concurrent request cap | ✅ Mitigated |
| D-05 | DNS amplification via resolver | TB-6 | Low | Bounded thread pool; query timeouts | ✅ Mitigated |

### E — Elevation of Privilege

| ID | Threat | Trust Boundary | Risk | Mitigation | Gap |
|---|---|---|---|---|---|
| E-01 | Free-tier user accesses paid endpoints | TB-1 | Medium | License gating; A133: origin stored on token | ✅ Mitigated |
| E-02 | Local attacker elevates via UserDefaults write | TB-5 | Medium | HMAC integrity tags (D030); D037: cache HMAC | ⚠️ SYXX-VULN-006/007/008 |
| E-03 | Attacker forges update binary | TB-1 | Medium | Ed25519 signature on appcast; URL origin allowlist | ✅ Mitigated |
| E-04 | DPAPI/Keychain bypass on shared machine | TB-4 | Low | User-scoped DPAPI; `kSecAttrAccessibleWhenUnlockedThisDeviceOnly` | ✅ Mitigated |

---

## 4. Attack Tree — Highest-Risk Scenario

**Goal:** Forge a leak attestation that a client accepts as authentic

```
Forge attestation
├── [1] Compromise Ed25519 private key
│   ├── [1.1] Extract from server process memory
│   │   └── Requires: server RCE or memory dump
│   └── [1.2] Extract from SYXX_COLLECTOR_PRIVATE_KEY env var
│       └── Requires: server file read or process env access
├── [2] Replace collector public key in client
│   ├── [2.1] Write UserDefaults (SYXX-VULN-006)
│   │   └── Requires: local access to macOS user session
│   └── [2.2] MitM the /api/pubkey response
│       └── Requires: TLS compromise or SPKI pin bypass
└── [3] Replay a previously valid attestation
    └── [3.1] Submit old token/attestation pair
        └── Requires: token not yet consumed (A111: atomic claim)
```

**Most feasible path:** 2.1 (SYXX-VULN-006) — local attacker replaces UserDefaults key. Requires local user session access. Impact: attacker can forge attestations for their own device, but cannot forge attestations for other users' devices.

---

## 5. Risk Matrix

| Threat | Likelihood | Impact | Risk Level | Mitigation Status |
|---|---|---|---|---|
| CSP bypass (SYXX-VULN-001) | Low | Medium | **Medium** | Partial (Stripe requires unsafe-inline) |
| UserDefaults key replacement (SYXX-VULN-006) | Low | Medium | **Medium** | Partial (local access required) |
| SSRF via API input | Very Low | High | **Low** | Strong (multi-layer) |
| SQL injection | Very Low | High | **Low** | Strong (parameterized) |
| DNS cache poisoning | Very Low | Medium | **Low** | Strong (random TXID, bailiwick) |
| HTTP request smuggling | Very Low | High | **Low** | Strong (TE/CL rejection) |
| Session token brute-force | Very Low | Medium | **Low** | Strong (HMAC + rate limit) |
| DPAPI/Keychain bypass | Very Low | Medium | **Low** | Strong (OS primitives) |
| Update binary forgery | Very Low | High | **Low** | Strong (Ed25519 + origin check) |

---

## 6. Data Flow Classification

| Data | Classification | In Transit | At Rest | Notes |
|---|---|---|---|---|
| Ed25519 private key | Secret | N/A | Env var (server) | Fail-closed if not set |
| Ed25519 public key | Public | HTTPS | UserDefaults (client) ⚠️ | SYXX-VULN-006 |
| HMAC session secret | Secret | N/A | Env var (server) | Validated strength |
| Stripe webhook secret | Secret | N/A | Env var (server) | — |
| License key | Confidential | HTTPS (TLS) | Keychain/DPAPI | — |
| Leak tokens | Confidential | HTTPS (TLS) | SQLite (server) | One-time, auto-consumed |
| Leak observations | Confidential | — | SQLite (server) | Ownership-gated |
| DNS queries | Public | UDP/TCP | Not persisted | — |
| Warp manifests | Public | HTTPS | Not persisted | SHA-256 verified |
| Witness signing keys | Secret | N/A | Keychain/DPAPI | — |
| User email | PII | HTTPS (TLS) | SQLite (server) | parseaddr guard |
| Resolver IPs | PII | HTTPS (TLS) | SQLite (server) | A023: not in passive_dns |

---

## 7. Threat Actors

| Actor | Capability | Motivation | Top Targets |
|---|---|---|---|
| **Network-level attacker** | MitM, DNS spoofing, TLS interception | Surveillance, censorship | DNS queries, leak attestations |
| **ISP/Rogue resolver** | DNS response injection, logging | Surveillance, censorship | DNS answers, leak tokens |
| **Malicious local user** | File read/write, UserDefaults, process memory | License bypass, data theft | License keys, signing keys |
| **Remote attacker** | HTTP API abuse, SSRF, injection | Service disruption, data exfiltration | API endpoints, SSRF surfaces |
| **Compromised third-party** | Stripe/Keygen API abuse | Financial fraud | Webhook endpoints, license grants |

---

## 8. Key Findings Summary

1. **No Critical/High risk threats** — the codebase has strong defensive posture
2. **Two Medium risks** — CSP `unsafe-inline` (Stripe trade-off) and UserDefaults public key storage
3. **Strongest defenses** — Ed25519 signing, SSRF protection, fail-closed defaults, constant-time comparisons
4. **Weakest link** — UserDefaults storage of trust anchors (SYXX-VULN-006/007) on macOS
5. **Supply chain** — Minimal dependency surface (6 Python, 2 Swift, 0 .NET) reduces attack surface

---

*Report generated by Security Hardening Orchestrator — Step 2 of 13*  
*Next step: Architecture Security Review (Step 3)*
