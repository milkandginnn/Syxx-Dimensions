# Security Hardening — Step 9: Infrastructure Security Controls

**Target:** Syxx — single-tenant VPS deployment (Vultr, 198.71.60.193)
**Date:** 2026-08-06
**Status:** ASSESSMENT — current infrastructure is adequate for the threat model

---

## 1. Current Infrastructure Security Posture

Syxx runs on a single Vultr VPS with this topology:

```
Internet → Vultr Cloud Firewall → nginx (TLS termination, port 80/443)
    → Python stdlib HTTP server (127.0.0.1:8080)
    → Authoritative DNS server (port 53 UDP/TCP)
```

### 1.1 What Vultr Provides

| Control | Status | Notes |
|---|---|---|
| Network firewall | ✅ | Vultr cloud firewall rules (inbound: 80, 443, 53; outbound: all) |
| DDoS protection | ✅ | Vultr basic DDoS mitigation (network-layer) |
| Physical security | ✅ | Data center physical access controls |
| Hypervisor isolation | ✅ | KVM virtualization |
| Network isolation | ✅ | VPC/private networking available |

### 1.2 What the Application Provides

| Control | Status | Notes |
|---|---|---|
| TLS termination | ✅ | nginx handles TLS; app runs on localhost |
| Security headers | ✅ | HSTS, CSP, XFO, X-Content-Type-Options, Referrer-Policy, Permissions-Policy |
| CORS | ✅ | Explicit origin allowlist (not wildcard) |
| Rate limiting | ✅ | Token bucket per IP/subject (configurable) |
| SSRF protection | ✅ | Multi-layer: scheme allowlist, IP pinning, private IP blocking, DNS rebind prevention |
| Port allowlist | ✅ | Outbound fetch limited to ports 80/443 (A033) |
| Request deadline | ✅ | Wall-clock timeout prevents slow-drip attacks |
| Input validation | ✅ | Bounded inputs, type checking, parseaddr for email |
| No subprocess/exec/eval | ✅ | Eliminates command injection |
| Fail-closed defaults | ✅ | All enforcement flags default to True |

### 1.3 What the Deployment Provides

| Control | Status | Notes |
|---|---|---|
| Server runs as non-root | ✅ | systemd unit runs as `syxx` user |
| File permissions | ✅ | DB file `0600`, owned by `syxx` user |
| Environment-based secrets | ✅ | All secrets from env vars (systemd env file) |
| No shell-sourcing of env | ✅ | E070: deploy.sh parses with grep/cut, not shell `.` |
| Database backup | ✅ | `syxx-backup.sh` with SQLite online backup API |

---

## 2. WAF Assessment [informational]

### Current State

No WAF is deployed. The application implements its own input validation and security headers.

### Assessment

**A WAF is not needed for Syxx's current deployment.** Reasons:

1. **No HTML rendering** — the server emits JSON API responses and one locked-down HTML page (`/api/whats-new`). XSS is not a viable attack vector.
2. **No user-generated content** — no forms, no comments, no file uploads. WAF rule sets target these patterns.
3. **Comprehensive input validation** — the application already validates all inputs at the route level (bounded inputs, type checking, parseaddr, parameterized queries).
4. **No framework** — stdlib-only server eliminates framework-level vulnerabilities (Django/Flask CVEs) that WAFs typically protect against.
5. **Single-tenant, low-traffic** — a WAF adds latency and complexity for minimal benefit on a single-tenant VPS.

### When a WAF Would Be Needed

- If the web frontend is de-mocked and serves dynamic user-facing pages
- If the traffic volume increases significantly
- If a compliance requirement (PCI-DSS) mandates WAF deployment

### Recommendation

**No action required.** If a WAF is needed in the future, use Cloudflare's free tier (proxy mode) or Vultr's managed WAF.

---

## 3. Network Segmentation Assessment [informational]

### Current State

Single VPS — no segmentation. All services (HTTP, DNS, SQLite) run on the same host.

### Assessment

**Network segmentation is not applicable for a single-VPS deployment.** Micro-segmentation is an enterprise pattern for multi-service architectures. Syxx has:

- 1 HTTP server (localhost:8080, behind nginx)
- 1 DNS server (port 53)
- 1 SQLite database (file on disk)

The attack surface is already minimal:
- nginx only exposes ports 80/443 to the internet
- The Python server binds to 127.0.0.1 (localhost only)
- The DNS server is the only other internet-facing service

### Recommendation

**No action required.** If the deployment scales to multiple servers, consider:
- Separate DNS server from HTTP server
- Private network between services
- Database on a separate host with no public IP

---

## 4. IDS/IPS Assessment [informational]

### Current State

No IDS/IPS is deployed. The application has:
- Structured logging (OTel, Sentry)
- Rate limiting with per-IP tracking
- Audit logging for all API requests

### Assessment

**A dedicated IDS/IPS is not needed for a single-tenant VPS.** Reasons:

1. **Low traffic** — the server handles a small number of requests per day
2. **Application-level detection** — rate limiting, audit logging, and Sentry already detect anomalous behavior
3. **Vultr's network-level protection** — basic DDoS mitigation and network filtering
4. **No compliance requirement** — no regulatory mandate for IDS/IPS

### What Would Be Useful (Low Priority)

- **Fail2ban** — parse nginx logs for repeated 401/403 responses and auto-block IPs at the firewall level. This is the most practical IDS-like control for a single VPS.
- **OTel alerting** — set up alerts on elevated error rates or unusual request patterns in the existing OTel spans.

### Recommendation

**Optional:** Consider adding fail2ban with a custom filter for repeated 402/403 responses from the Syxx API. This is a low-effort, high-value addition for a single VPS.

| Priority | Effort | Action |
|---|---|---|
| Low | Low | Add fail2ban filter for nginx 402/403 responses |
| Low | Low | Add OTel alert on error rate spike |

---

## 5. Cloud Security Groups Assessment [informational]

### Current State

Vultr cloud firewall rules control inbound traffic. Based on the deployment topology:

| Direction | Port | Protocol | Source | Purpose |
|---|---|---|---|---|
| Inbound | 80 | TCP | Any | HTTP → nginx redirect to HTTPS |
| Inbound | 443 | TCP | Any | HTTPS → nginx → app |
| Inbound | 53 | UDP | Any | DNS queries (leak test) |
| Inbound | 53 | TCP | Any | DNS queries (fallback) |
| Outbound | All | All | Any | Updates, API calls, OTel, Sentry |

### Assessment

**The firewall rules are appropriate.** The only exposed services are:
- HTTP/HTTPS (nginx) — required for API
- DNS (port 53) — required for leak test functionality

The Python server itself is not exposed — it binds to 127.0.0.1:8080 and is only reachable through nginx.

### Potential Hardening

| Rule | Priority | Notes |
|---|---|---|
| Restrict outbound to specific ports | [informational] | Not needed — no compliance requirement; outbound is only for API calls, OTel, Sentry |
| Restrict SSH to specific IPs | [low] | Add a Vultr firewall rule for SSH (port 22) limited to operator IPs |
| Add ICMP rate limiting | [informational] | Not needed — Vultr handles this at the network level |

### Recommendation

**One action:** Add a Vultr firewall rule restricting SSH (port 22) to known operator IPs. This is the single most impactful infrastructure hardening step.

---

## 6. DDoS Protection Assessment [informational]

### Current State

| Layer | Protection | Status |
|---|---|---|
| Network | Vultr basic DDoS mitigation | ✅ Provided |
| Application | Rate limiting (token bucket per IP/subject) | ✅ Implemented |
| DNS | No rate limiting on authoritative DNS server | ⚠️ Gap |
| HTTP | Request deadline (wall-clock timeout) | ✅ Implemented |

### Assessment

**DDoS protection is adequate for the current deployment scale.** The server is single-tenant, low-traffic. The main risk vectors:

1. **HTTP flood** — mitigated by rate limiting + Vultr network-level protection
2. **DNS amplification** — the authoritative DNS server on port 53 has no rate limiting. A DNS amplification attack could consume bandwidth. However, the DNS server only responds to queries for `syxx.app` subdomains (not an open resolver), limiting amplification factor.
3. **Slow-drip** — mitigated by request deadline

### DNS Rate Limiting

The authoritative DNS server (port 53) does not implement rate limiting. For a DNS server that only serves `syxx.app` zones:
- The amplification factor is low (responses are small, only for authoritative zones)
- An open resolver would be a high-risk target; this is not one
- The Vultr cloud firewall already limits traffic to reasonable volumes

### Recommendation

**No action required today.** If DNS traffic increases significantly, add:
- Per-IP rate limiting on the DNS server (e.g., 100 queries/second per IP)
- Response Rate Limiting (RRL) for identical queries

---

## 7. Enhancement Recommendations

### 7.1 Existing — Already Implemented

| Control | Evidence |
|---|---|
| TLS termination via nginx | Deploy topology: nginx → app |
| Security headers (HSTS, CSP, XFO, X-Content-Type-Options) | `app.py:605-635` |
| CORS with explicit allowlist | `config.py:565` |
| Rate limiting (token bucket per IP/subject) | `config.py:586-591` |
| SSRF protection (multi-layer) | `fetcher.py`, `netutil.py` |
| Port allowlist for outbound fetch | `fetcher.py:33` (A033: ports 80/443 only) |
| Request deadline | `config.py:SYXX_HTTP_REQUEST_TIMEOUT` |
| Server binds to localhost only | `deploy.sh:SYXX_HTTP_HOST=127.0.0.1` |
| Non-root process | systemd unit runs as `syxx` user |
| File permissions (0600) | SQLite DB owner-only |
| Vultr cloud firewall | Inbound: 80, 443, 53 only |
| Vultr basic DDoS mitigation | Network-level protection |

### 7.2 Planned — In Prior Reports

| Control | Report | Status |
|---|---|---|
| HSTS preload | Step 5 | One-line fix, not yet implemented |
| Dependency scanning in CI | Step 5 | Add `pip-audit` to CI workflow |

### 7.3 New Recommendations

| # | Recommendation | Severity | Effort | Notes |
|---|---|---|---|---|
| 1 | Restrict SSH to operator IPs | [low] | Low | Add Vultr firewall rule for port 22 — single most impactful infra hardening step |
| 2 | Add fail2ban for nginx 402/403 | [low] | Low | Auto-block IPs with repeated failed auth attempts |
| 3 | OTel alert on error rate spike | [informational] | Low | Set up alerting on existing OTel spans |
| 4 | DNS server rate limiting | [informational] | Medium | Not needed today; add if DNS traffic increases |
| 5 | Outbound firewall restrictions | [informational] | Low | Not needed — no compliance requirement |

---

## 8. What Does NOT Need Enhancement

| Control | Why Not |
|---|---|
| **WAF** | No HTML rendering, no user-generated content, comprehensive input validation already in place |
| **Network segmentation** | Single VPS — no multi-service architecture to segment |
| **IDS/IPS** | Low traffic, application-level detection already present, Vultr network-level protection |
| **Micro-segmentation** | Enterprise pattern not applicable to a single-tenant VPS |
| **Service mesh** | Single service — no inter-service communication to secure |
| **Cloud WAF** | No web attack surface beyond the JSON API and one locked-down HTML page |
| **Geo-blocking** | DNS tampering detection is useful from any geography; no compliance requirement to restrict |

---

## 9. Deferred Items

| Item | Severity | Effort | When |
|---|---|---|---|
| SSH firewall rule (operator IPs only) | Low | Low | Next server maintenance |
| fail2ban for nginx | Low | Low | Next server maintenance |
| OTel error rate alerting | Informational | Low | When observability is expanded |
| DNS server rate limiting | Informational | Medium | If DNS traffic increases |
| Outbound firewall restrictions | Informational | Low | If compliance requires |
| WAF (Cloudflare) | Informational | Low | If web frontend is de-mocked |

---

## 10. Files to Modify

| File | Change | When |
|---|---|---|
| Vultr firewall config | Add SSH rule (port 22, operator IPs only) | Next server maintenance |
| `/etc/fail2ban/filter.d/syxx.conf` | New filter for nginx 402/403 responses | If fail2ban is installed |
| `/etc/fail2ban/jail.d/syxx.conf` | New jail using the filter | If fail2ban is installed |

No application code changes are required for infrastructure security.

---

## 11. Assessment Summary

Syxx's infrastructure security is **appropriate for a single-tenant VPS deployment**. The application already implements most of the security controls that a WAF or IDS would provide (input validation, rate limiting, SSRF protection, security headers). The Vultr cloud firewall and basic DDoS mitigation provide network-level protection.

| Category | Score | Notes |
|---|---|---|
| **Network Security** | 8/10 | Vultr firewall + nginx; SSH not restricted to operator IPs |
| **Application Security** | 9/10 | Comprehensive input validation, SSRF protection, rate limiting |
| **DDoS Protection** | 8/10 | Vultr basic + app-level rate limiting; DNS not rate-limited |
| **Monitoring** | 8/10 | OTel + Sentry; no automated alerting |
| **Deployment Security** | 9/10 | Non-root, localhost-only, file permissions, env-based secrets |

**Overall Infrastructure Security Score: 8.4/10**

The single most impactful improvement: **restrict SSH to operator IPs** via the Vultr firewall.

---

*Report generated by Security Hardening Orchestrator — Step 9 of 13*
*Next: Secrets Management Implementation (Step 10)*
