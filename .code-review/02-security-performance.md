# Phase 2C — Security & Performance

## Security (`security-scanning__security-auditor`)
**0 Critical, 0 High, 1 Medium, 4 Low, 3 Informational**

### Medium
- `Server/syxx_server/app.py:654` — HSTS via `X-Forwarded-Proto` without peer validation. If server exposed beyond loopback, attacker could trigger HSTS emission.

### Low
- `Server/syxx_server/app.py:755` — SEC-020 "extendable via config" not implemented (no TRUSTED_PROXIES setting)
- `Server/syxx_server/db.py:1077-1088` — API key prefix exposed in list endpoint, leaks 7 of 43 random chars
- `Server/syxx_server/app.py:1648-1650` — Activation lockout state is per-process, lost on restart
- `Server/syxx_server/db.py:484-491` — Offline license HMAC shares root secret with session tokens

### Verified Secure (No Issues)
- CSRF protection: Origin-based check on all POST routes ✅
- Session token delivery: HttpOnly, Secure, SameSite=Strict ✅
- CSP nonce generation: os.urandom(16), base64, per-request ✅
- Request smuggling: Transfer-Encoding rejected, duplicate CL rejected ✅
- Stripe webhook: HMAC-SHA256, constant-time comparison ✅
- Keygen verification: Ed25519, Date-header skew check ✅
- SSRF protection: DNS resolve + private/loopback IP block ✅

## Performance (`application-performance__performance-engineer`)
**2 Critical, 2 High, 3 Medium, 3 Low**

### Critical
- `Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs:170-210` — Triple enumeration of same windowed records (O(3n) vs O(n))
- `Windows/dotNET/Syxx.Kit/Synthesis/ParityVectorLoader.cs:1650-1660` — O(n log n) key-sort on every `Walker.Done()` call (~500+ calls per corpus load)

### High
- `macOS/Sources/SyxxKit/CageRedundancyCorrelator.swift:130-225` — Four dictionary builds + four set copies + four sorts
- `Windows/dotNET/Syxx.Kit/Synthesis/ParityVectorLoader.cs:380-410` — `RejectDuplicateMembers` allocates HashSet per JSON object (~450 allocations per corpus load)

### Medium
- `CaptureEvents.cs` — Event Data dictionaries allocate on hot path (0-9 tiny dicts/sec)
- `app.py` — `_client_ip` validates ALL XFF entries when only rightmost matters
- `resolver.py` — Repeated `_in_bailiwick` calls in glue loop, repeated `rstrip` calls