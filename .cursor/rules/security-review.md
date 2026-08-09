# Security Review Rule

When performing a security review of code changes, follow this protocol:

## Review Categories (in priority order)

1. **Injection** — SQL, command, LDAP, XPath — any place user input reaches an interpreter
2. **Authentication** — Flawed auth logic, missing MFA, session fixation, JWT misconfiguration
3. **Authorization** — Missing access controls, IDOR, privilege escalation paths
4. **Cryptography** — Weak algorithms (MD5, SHA1, RC4), hardcoded keys/secrets, missing SPKI pinning, improper IV/nonce handling
5. **Data exposure** — Secrets in logs/config/URLs, verbose errors, sensitive data in responses
6. **Input validation** — Path traversal, SSRF, open redirect, XML external entities
7. **Configuration** — Debug mode, permissive CORS, missing security headers, default credentials
8. **Dependencies** — Known CVEs, outdated packages, supply chain risks

## Output Format

For each finding:
- **[severity] `file:line` — one-sentence defect statement**
- **Failure scenario:** concrete inputs/state → wrong outcome
- **Evidence:** the code that shows it
- **Suggested fix:** minimal change to resolve it

Severity: critical | high | medium | low

End with counts by severity and overall recommendation.