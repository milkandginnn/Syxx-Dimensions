# Code Review Report — Syxx Warp Content Integrity

**Target**: `Software/Syxx/` (warp content integrity feature, branch `feature/warp-content-integrity`)
**Reviewers**: transfer-encoding, unicode-equality-and-idn, time-precision-and-rounding, filesystem-case-sensitivity, trust-stores, collection-ordering
**Date**: 2026-08-09
**Files Reviewed**: 24 (Python server, Swift macOS, C# Windows, test vectors, specs)

---

## Critical Findings (1)

### [CR-001] IDN/Punycode bypass of per-run unique-host cap

**Location**: `Server/syxx_server/warp.py:339`
**Dimension**: unicode-equality-and-idn
**Severity**: Critical

**Description**: `urlsplit` does not perform IDN-to-punycode conversion. `café.com` and `xn--caf-dma.com` produce different hostnames, each consuming a separate budget slot. An attacker can double the effective host limit by submitting the same asset host in both Unicode and punycode forms.

**Impact**: The per-run unique-host cap (`FETCH_MAX_UNIQUE_HOSTS`) is the SSRF fan-out bound. This bypass allows resource exhaustion attacks.

**Fix**: Convert hostnames to punycode canonical form before adding to the host budget:

```python
import encodings.idna

def _canonical_host(url):
    host = (urllib.parse.urlsplit(url).hostname or "").lower()
    try:
        ascii_host = encodings.idna.ToASCII(host)
        return encodings.idna.ToUnicode(ascii_host).lower()
    except UnicodeError:
        return host
```

---

## High Findings (5)

### [HI-001] Swift `origin_url` comparison diverges from C# — Unicode normalization

**Location**: `macOS/Sources/SyxxKit/WarpEngine.swift:307` vs `Windows/dotNET/Syxx.Kit/WarpEngine.cs:455`
**Dimension**: unicode-equality-and-idn
**Severity**: High

**Description**: Swift `String.==` uses Unicode canonical equivalence (NFC = NFD). C# `StringComparison.Ordinal` compares UTF-16 code units byte-by-byte (NFC ≠ NFD). A manifest with Unicode in `origin_url` produces `fresh` on Swift and `originMismatch` on C#.

**Fix**: Normalize both strings to NFC before comparison on both platforms:

Swift:
```swift
let normalizedOrigin = manifestOrigin.precomposedStringWithCanonicalMapping
let normalizedRequested = requestedOrigin.precomposedStringWithCanonicalMapping
if normalizedOrigin != normalizedRequested { return .originMismatch }
```

C#:
```csharp
string normalizedOrigin = manifestOrigin.Normalize(NormalizationForm.FormC);
string normalizedRequested = requestedOrigin.Normalize(NormalizationForm.FormC);
if (!string.Equals(normalizedOrigin, normalizedRequested, StringComparison.Ordinal))
```

### [HI-002] No Unicode normalization of hostnames in host budget

**Location**: `Server/syxx_server/warp.py:339,442`
**Dimension**: unicode-equality-and-idn
**Severity**: High

**Description**: `.lower()` alone does not normalize Unicode. Two visually identical hostnames in different normalization forms consume two budget slots.

**Fix**: Apply `unicodedata.normalize('NFC', host.lower())`:

```python
import unicodedata
host = unicodedata.normalize('NFC', (urllib.parse.urlsplit(url).hostname or "").lower())
```

### [HI-003] Swift same-origin host comparison is case-sensitive

**Location**: `macOS/Sources/SyxxKit/WarpEngine.swift:608`
**Dimension**: filesystem-case-sensitivity
**Severity**: High

**Description**: `URL.host` preserves original case. `example.com` ≠ `Example.COM` in Swift, but DNS is case-insensitive. A legitimate same-origin redirect with case variation is rejected as a policy violation. C# and Python are immune.

**Fix**: Add `.lowercased()` to host comparisons:

```swift
case .sameOrigin:
    return from.scheme == to.scheme
        && from.host?.lowercased() == to.host?.lowercased()
        && (from.port ?? -1) == (to.port ?? -1)
```

### [HI-004] Swift `redirectReconciles` uses case-sensitive URL comparison

**Location**: `macOS/Sources/SyxxKit/WarpEngine.swift:444-445`
**Dimension**: filesystem-case-sensitivity
**Severity**: High

**Description**: `URL.absoluteString` preserves hostname case from HTTP response. The server's `final_url` (via `urlsplit`) is always lowercase. Case mismatch causes `fetchError` → `INCONCLUSIVE` for every asset.

**Fix**: Normalize URLs before comparison:

```swift
private static func normalizeURL(_ url: String) -> String {
    guard let u = URL(string: url),
          let scheme = u.scheme?.lowercased(),
          let host = u.host?.lowercased() else { return url }
    let port = u.port.map { ":\($0)" } ?? ""
    return "\(scheme)://\(host)\(port)\(u.path)\(u.query.map { "?\($0)" } ?? "")"
}
```

### [HI-005] TOCTOU DNS rebinding in Swift HTTP fetch

**Location**: `macOS/Sources/SyxxKit/WarpEngine.swift:520-525`
**Dimension**: trust-stores
**Severity**: High

**Description**: `validatePublicHost` resolves DNS → verifies public IPs, then `URLSession.data(for:)` resolves DNS again for the actual connection. A DNS rebinding attack between the two resolutions can redirect HTTP connections to private IPs, bypassing the SSRF gate. C# is immune (atomic resolution in `ConnectCallback`).

**Fix**: Resolve DNS once and pin the connection to that IP, or forbid HTTP in vantage fetch (HTTPS-only).

---

## Medium Findings (8)

| ID | Finding | Location | Dimension |
|----|---------|----------|-----------|
| ME-001 | `_envelope_decode` function name and docs conflate Content-Encoding with Transfer-Encoding | `Server/syxx_server/warp.py:218` | transfer-encoding |
| ME-002 | No key rotation, expiration, or revocation support in trust store | `WarpManifestPinning.swift:19` / `WarpManifestPinning.cs:22` | trust-stores |
| ME-003 | No explicit CRL/OCSP checking in TLS connections | `WarpEngine.swift:508` / `WarpEngine.cs:574` | trust-stores |
| ME-004 | Server exposes two different `key_id` schemes from same key (`/api/pubkey` vs warp manifest) | `app.py:1933` vs `leak.py:158` | trust-stores |
| ME-005 | No zero-width character handling in URL parsing | `warp.py:339` / `fetcher.py:90` | unicode-equality-and-idn |
| ME-006 | No homoglyph/confusable character detection in domain names | `warp.py:339` + both clients | unicode-equality-and-idn |
| ME-007 | `same-origin` redirect host comparison is platform-dependent (NFC vs Ordinal) | `WarpEngine.swift:608` / `WarpEngine.cs:635` | unicode-equality-and-idn |
| ME-008 | Python `str.lower()` is locale-sensitive for hostname normalization | `warp.py:339,442` | filesystem-case-sensitivity |

---

## Low Findings (13)

| ID | Finding | Location | Dimension |
|----|---------|----------|-----------|
| LO-001 | `_envelope_decode` doesn't handle multiple Content-Encoding values | `warp.py:226` | transfer-encoding |
| LO-002 | Swift comment says "transfer-encoding" but describes Content-Encoding | `WarpEngine.swift:540` | transfer-encoding |
| LO-003 | C# comment says "transfer-encoding" but describes Content-Encoding | `WarpEngine.cs:600` | transfer-encoding |
| LO-004 | Swift `UInt64` crash on negative system clock | `WarpEngine.swift:501` | time-precision |
| LO-005 | Cross-platform `nowMs` generation inconsistency (float vs int paths) | 3 platforms | time-precision |
| LO-006 | Server uses `time.time()` without clock-source documentation | `warp.py:309` | time-precision |
| LO-007 | Reference evaluator freshness check diverges structurally from client engines | `_build_vectors.py:228` | time-precision |
| LO-008 | No upper bound on `freshness_seconds` at server | `warp.py:310` | time-precision |
| LO-009 | `IntegrityTag.constantTimeEqual` leaks HMAC length via early return | `IntegrityTag.swift:117` | trust-stores |
| LO-010 | Test Ed25519 private keys committed to repo | `warp-manifest-vectors/keys/` | trust-stores |
| LO-011 | Deprecated `build_baseline` serializes without `sort_keys=True` | `warp.py:192` | collection-ordering |
| LO-012 | Test vector builder serializes `envelope.json` without `sort_keys=True` | `_build_vectors.py:521` | collection-ordering |
| LO-013 | `json.dumps` with `ensure_ascii=False` produces non-normalized UTF-8 signed bytes | `app.py:1925` | unicode-equality-and-idn |

---

## Summary by Dimension

| Dimension | Critical | High | Medium | Low | Total |
|-----------|----------|------|--------|-----|-------|
| unicode-equality-and-idn | 1 | 2 | 3 | 1 | **7** |
| filesystem-case-sensitivity | 0 | 2 | 1 | 0 | **3** |
| trust-stores | 0 | 1 | 3 | 2 | **6** |
| transfer-encoding | 0 | 0 | 1 | 3 | **4** |
| time-precision-and-rounding | 0 | 0 | 0 | 5 | **5** |
| collection-ordering | 0 | 0 | 0 | 3 | **3** |
| **Total** | **1** | **5** | **8** | **14** | **28** |

---

## Recommendation

**Swift is the outlier platform.** Of the 6 High/Critical findings, 4 are Swift-only bugs (HI-001, HI-003, HI-004, HI-005). The C# and Python implementations are more robust against these edge cases due to platform defaults (`Uri` normalization, `urlsplit` lowercasing). The Swift client needs the most attention.

**Top 3 priorities:**

1. **CR-001** — IDN host budget bypass (server-side, SSRF fan-out bound)
2. **HI-003 + HI-004** — Swift case-sensitivity in URL/hostname comparisons (breaks same-origin redirect enforcement for real-world CDNs)
3. **HI-005** — Swift TOCTOU DNS rebinding (SSRF vector)

**Positive findings:** The cryptographic core (Ed25519 signing, SHA-256 hashing, manifest serialization with `sort_keys=True`) is fundamentally sound. The collection-ordering reviewer confirmed zero issues in the production protocol path — the design of server-only parsing with signature over exact bytes eliminates entire classes of ordering bugs. The protocol's freshness enforcement is robust with the 600-second ceiling preventing overflow and the 300,000 ms skew tolerance preventing clock-drift false positives.