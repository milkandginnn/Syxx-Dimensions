# Security Hardening — Step 13: Security Monitoring & SIEM Integration

**Target:** Syxx — Python server on dedicated VPS, macOS/Windows/Linux desktop clients
**Date:** 2026-08-06
**Status:** ASSESSMENT — no code changes

---

## 1. Current Monitoring Architecture

### 1.1 Server-Side Monitoring

| Component | Tool | Status | Notes |
|---|---|---|---|
| Structured logging | `logging_util.py` | ✅ Active | Per-event structured fields; no PII |
| Error tracking | Sentry | ✅ Active | `send_default_pii=False`; `before_send` callback scrubs sensitive fields |
| Distributed tracing | OpenTelemetry | ✅ Active | `syxx.server.*` span naming; spans per request |
| Metrics | `/metrics` endpoint | ✅ Active | Prometheus-compatible; Bearer token gated (S4) |
| Health checks | `/healthz`, `/readyz` | ✅ Active | Used by load balancer / monitoring |
| Audit logging | SQLite `audit` table | ✅ Active | Per-request audit entries with subject, route, status, IP |
| Preflight checks | `preflight.py` | ✅ Active | Startup validation of all secrets and config |

### 1.2 Client-Side Monitoring

| Component | Tool | Status | Notes |
|---|---|---|---|
| Error tracking | Sentry | ✅ Active | macOS client; `send_default_pii=False` |
| Crash reporting | Sentry | ✅ Active | Symbolicated crash reports |
| OTel spans | OpenTelemetry | ✅ Active | Client-side spans for investigation operations |

### 1.3 Alerting

| Alert | Trigger | Status |
|---|---|---|
| Keygen public key unset | Preflight | ✅ Active — S11: throttled warning during denials |
| Collector key missing | Preflight + RuntimeError | ✅ Active — server refuses to start |
| Stripe webhook failure | Webhook signature mismatch | ✅ Active — logged and rejected |
| Rate limit exceeded | Token bucket exhaustion | ✅ Active — `logging_util.incr("ratelimit_exceeded")` |
| License validation failure | Invalid key | ✅ Active — `db.audit()` entry |
| Session minted | Successful activation | ✅ Active — `logging_util.incr("sessions_minted")` |
| API key created | POST /api/keys | ✅ Active — `logging_util.incr("api_keys_created")` |

---

## 2. Gap Assessment

### 2.1 No Centralized SIEM [informational]

**Current State:** No SIEM (Splunk, ELK, Sentinel) is deployed. Monitoring is distributed across:
- Sentry (errors, crashes)
- OTel (traces)
- SQLite audit table (request logs)
- systemd journal (service logs)

**Assessment:** Informational. For a single-host deployment with low traffic, a centralized SIEM adds operational complexity without proportional benefit. The current monitoring stack (Sentry + OTel + structured logging) provides:
- Real-time error alerting (Sentry)
- Distributed tracing (OTel)
- Request-level audit trail (SQLite audit table)
- Service health monitoring (systemd + /healthz)

**Recommendation (Not Recommended):** A SIEM becomes valuable when:
- Logs need to be correlated across multiple hosts
- Compliance requirements mandate centralized log retention
- Alerting rules need to span multiple data sources
- The deployment scales beyond a single host

None of these apply to Syxx's current deployment.

### 2.2 No Automated Incident Response Playbooks [informational]

**Current State:** No automated incident response playbooks are deployed. The operator manually investigates and responds to incidents.

**Assessment:** Informational. For a single-host deployment with a small number of failure modes, automated playbooks would add complexity without meaningful benefit. The most common incidents (service down, license validation failure, webhook failure) are already handled by:
- systemd automatic restart (`Restart=always`)
- Preflight checks with clear error messages
- Webhook signature verification with logging

**Recommendation (Deferred):** If the deployment scales to multiple hosts or adds on-call rotation, create playbooks for:
1. Service outage recovery
2. Keygen/Stripe integration failure
3. DNS resolver failure
4. Security incident response

### 2.3 No Behavioral Analytics / Anomaly Detection [informational]

**Current State:** No behavioral analytics or anomaly detection is deployed. The rate limiter provides basic anti-automation but does not detect sophisticated attacks that stay within rate limits.

**Assessment:** Informational. For a low-traffic single-host deployment, behavioral analytics would generate more false positives than genuine alerts. The current rate limiting handles the most common abuse patterns (credential stuffing, API abuse).

**Recommendation (Deferred):** If anomalous usage patterns are observed, consider adding:
- Per-subject anomaly detection (sudden spike in API usage)
- Geographic anomaly detection (requests from unusual locations)
- Time-based anomaly detection (requests at unusual hours)

### 2.4 No Log Tamper Protection [low]

**Current State:** Logs are shipped to OTel and Sentry, but there is no local tamper protection. A local attacker with access to the SQLite audit table could modify or delete audit entries.

**Assessment:** Low severity. The audit table is protected by:
- File permissions (0600, owned by `syxx` user)
- Systemd sandboxing (ProtectSystem=strict)
- Remote shipping to OTel/Sentry (tamper-evident copies exist off-host)

**Recommendation (Deferred):** If compliance requirements mandate tamper-evident logging, add:
- Hash-chaining of audit entries (each entry includes the hash of the previous entry)
- Signed audit entries (Ed25519 signature over the entry content)
- Write-once storage (append-only file or WORM storage)

### 2.5 No Security Dashboard [informational]

**Current State:** No security dashboard is deployed. The operator monitors the service via:
- Sentry (errors, crashes)
- `/metrics` endpoint (Prometheus-compatible)
- systemd journal (service logs)
- SQLite audit table (request logs)

**Assessment:** Informational. For a single-operator deployment, a dedicated security dashboard would add complexity without proportional benefit. The existing monitoring stack provides sufficient visibility.

**Recommendation (Deferred):** If the deployment scales or adds on-call rotation, consider:
- Grafana dashboard for security metrics (rate limiting, auth failures, audit events)
- PagerDuty/Opsgenie integration for critical alerts
- Weekly security summary report

---

## 3. What Does NOT Need Enhancement

### Centralized SIEM — Not Recommended

For a single-host deployment, a SIEM adds:
- A new service to deploy, monitor, and maintain
- Log storage and retention management
- Query language learning curve
- Alert fatigue from false positives

The current monitoring stack (Sentry + OTel + structured logging + audit table) provides the same capabilities at a fraction of the operational cost.

### Automated Incident Response — Not Recommended

The most common incidents are already handled by automated recovery:
- Service crash → systemd automatic restart
- License validation failure → fail-closed with clear error messages
- Webhook failure → signature verification with logging
- Rate limit exceeded → 429 response with logging

### Behavioral Analytics — Not Recommended

For a low-traffic deployment, behavioral analytics would generate more false positives than genuine alerts. The rate limiter handles the most common abuse patterns.

---

## 4. Enhancement Recommendations

| # | Recommendation | Severity | Effort | Priority |
|---|---|---|---|---|
| 1 | Add audit log hash-chaining | Low | Medium | Low |
| 2 | Add Grafana security dashboard | Informational | Medium | Low |
| 3 | Add incident response playbooks | Informational | Low | Low |
| 4 | Add per-subject anomaly detection | Informational | Medium | Deferred |

---

## 5. Deferred Items

| Item | When |
|---|---|
| Centralized SIEM | If deployment scales to multiple hosts |
| Automated incident response | If on-call rotation is added |
| Behavioral analytics | If anomalous usage patterns are observed |
| Log tamper protection | If compliance requirements mandate it |
| Security dashboard | If operational visibility is insufficient |

---

## 6. Assessment Summary

Syxx's monitoring architecture is **appropriate for its deployment model**. The Sentry + OTel + structured logging + audit table stack provides real-time error alerting, distributed tracing, and request-level audit trail. The systemd service health monitoring and preflight checks provide operational visibility.

The protocol's default recommendations (SIEM, automated incident response, behavioral analytics, security dashboard) are designed for enterprise deployments and do not apply to Syxx's single-host, single-operator deployment.

| Category | Score | Notes |
|---|---|---|
| **Error tracking** | 9/10 | Sentry with PII scrubbing; structured logging |
| **Distributed tracing** | 9/10 | OTel spans; server + client |
| **Audit logging** | 8/10 | SQLite audit table; minor gap: no tamper protection |
| **Alerting** | 8/10 | Preflight checks; rate limit alerts; no SIEM integration |
| **Health monitoring** | 9/10 | /healthz, /readyz, /metrics; systemd restart |
| **Overall** | 9/10 | Strong for a single-host deployment |

---

*Report generated by Security Hardening Orchestrator — Step 13 of 13*
*Next: Security Hardening Complete — final summary*
