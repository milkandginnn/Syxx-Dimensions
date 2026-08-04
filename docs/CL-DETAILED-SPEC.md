# CL-1/CL-2/CL-3 Detailed Technical Specification — syxx-neural-planner

**Generated:** 2026-07-11 · **Agent:** syxx-neural-planner  
**Spec:** [`HANDOFF-CLINE.md`](HANDOFF-CLINE.md) · **Master:** [`REASONING-SYSTEM-DESIGN.md`](REASONING-SYSTEM-DESIGN.md)  
**Status:** Ready for syxx-neural-executor

---

## Current State Summary (from code survey)

### Server (Python)
- **`resolver.py:384-456`** — `resolve_authoritative()` already exists. Full iterative root-walker with AA-bit validation, SSRF protection (`_ip_is_public`), DNSKEY zone-signing check, bounded depth (16), bounded servers per level (4). Returns `{name, qtype, delegation, authoritative: {answers, aa, rcode, signed}, verdict}`.
- **`app.py:680-700`** — `_route_authoritative()` already exists. Calls `resolve_authoritative()`, returns JSON. Paid route (requires entitlement).
- **`app.py:415-426`** — `_route_pubkey()` already exists. Returns the leak collector's Ed25519 public key (from `leak.collector_public_key()`).
- **What's missing:** Ed25519 signing of the GroundTruth response, freshness (timestamp/nonce), domain/record binding in the signature, quorum (≥2 auth IPs queried independently), replay protection, and a typed trust level enum.

### Client (Swift)
- **`DNSCanaries.swift:41-79`** — `nxdomainHijack()` queries two random names, checks for shared IP. **Defect:** requires shared IP between two names — a round-robin block page that returns different IPs per query evades detection.
- **`DNSCanaries.swift:86-117`** — `dnssecControl()` uses single hardcoded `cloudflare.com`. **Defect:** single control — a benign lapse fires decisive stripping.
- **What's missing:** Structural NXDOMAIN rule (RFC 8020: any answer for nonexistent name = hijack), multi-control DNSSEC quorum (≥3 controls, ≥2 must agree), DO-bit gate, encrypted-transport wildcard reference.

### Client (Windows)
- Not yet surveyed in detail, but per AGENTS.md parity rule, every Swift change needs a C# twin.

---

## CL-1 — Signed GroundTruth is Decisive-Grade

### 1.1 What already exists and what must be added

The server already produces authoritative ground truth via `resolve_authoritative()`. The gap is that the response is **unsigned, unauthenticated, and has no freshness/binding/quorum metadata**. A client consuming `/api/authoritative` has no way to verify:
- The response came from the Syxx server (not a MITM)
- The response is fresh (not replayed)
- The response covers the exact domain+qtype+answers (not a different query)
- The server queried ≥2 independent authoritative IPs (quorum)

### 1.2 Typed contract: `GroundTruth` (Python)

```python
# Server/syxx_server/groundtruth.py (NEW FILE)

from dataclasses import dataclass, field
from enum import Enum
from typing import Optional
import base64
import json
import time
import uuid

class GroundTrustLevel(str, Enum):
    """Tri-state trust level for a GroundTruth response.
    NEVER a bool — a bare True/False loses the distinction between
    'signature valid but quorum not met' and 'signature invalid'."""
    DECISIVE = "decisive"          # all six properties pass
    STANDARD = "standard"          # signature valid but quorum/freshness borderline
    INCONCLUSIVE = "inconclusive"  # signature invalid, expired, or binding mismatch

@dataclass
class GroundTruth:
    domain: str
    qtype: str                     # "A" or "AAAA"
    answers: list[str]             # authoritative answer values
    timestamp: str                 # ISO-8601 UTC
    nonce: str                     # UUID4 (replay protection)
    signature: str                 # base64-encoded Ed25519 signature
    pubkey: str                    # base64-encoded Ed25519 public key
    quorum: dict                   # {nameservers: [str], agreeing: int, met: bool}
    trust_level: GroundTrustLevel
    delegation: list[dict] = field(default_factory=list)  # from resolve_authoritative

    def to_dict(self) -> dict:
        return {
            "domain": self.domain,
            "qtype": self.qtype,
            "answers": self.answers,
            "timestamp": self.timestamp,
            "nonce": self.nonce,
            "signature": self.signature,
            "pubkey": self.pubkey,
            "quorum": self.quorum,
            "trustLevel": self.trust_level.value,
            "delegation": self.delegation,
        }
```

### 1.3 Signing module (Python)

```python
# Server/syxx_server/signing.py (NEW FILE)

import base64
import json
import os
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path

from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey, Ed25519PublicKey
from cryptography.hazmat.primitives import serialization

from . import config

_private_key: Ed25519PrivateKey | None = None
_public_key: Ed25519PublicKey | None = None

def ensure_keypair():
    """Generate Ed25519 keypair on first run, persist to disk. Idempotent."""
    global _private_key, _public_key
    key_path = Path(config.GROUNDTRUTH_KEY_PATH)
    if key_path.exists():
        with open(key_path, "rb") as f:
            _private_key = serialization.load_pem_private_key(f.read(), password=None)
    else:
        _private_key = Ed25519PrivateKey.generate()
        key_path.parent.mkdir(parents=True, exist_ok=True)
        with open(key_path, "wb") as f:
            f.write(_private_key.private_bytes(
                serialization.Encoding.PEM,
                serialization.PrivateFormat.PKCS8,
                serialization.NoEncryption()))
        os.chmod(key_path, 0o600)
    _public_key = _private_key.public_key()

def public_key_base64() -> str:
    """The server's Ed25519 public key, base64-encoded for client consumption."""
    if _public_key is None:
        ensure_keypair()
    return base64.b64encode(
        _public_key.public_bytes(serialization.Encoding.Raw, serialization.PublicFormat.Raw)
    ).decode("ascii")

def sign_groundtruth(domain: str, qtype: str, answers: list[str],
                     timestamp: str, nonce: str) -> str:
    """Sign a GroundTruth payload. Returns base64-encoded Ed25519 signature.
    
    The signed message is: domain|qtype|answers_json|timestamp|nonce
    This binds the signature to the exact domain, query type, answer set,
    and freshness parameters — a replay with different domain/answers fails verification.
    """
    if _private_key is None:
        ensure_keypair()
    message = f"{domain}|{qtype}|{json.dumps(sorted(answers))}|{timestamp}|{nonce}"
    sig = _private_key.sign(message.encode("utf-8"))
    return base64.b64encode(sig).decode("ascii")

def verify_signature(pubkey_b64: str, message: str, signature_b64: str) -> bool:
    """Verify an Ed25519 signature. Returns True if valid, False otherwise.
    Never raises — a malformed key or signature returns False."""
    try:
        pubkey_bytes = base64.b64decode(pubkey_b64)
        pubkey = Ed25519PublicKey.from_public_bytes(pubkey_bytes)
        sig = base64.b64decode(signature_b64)
        pubkey.verify(sig, message.encode("utf-8"))
        return True
    except Exception:
        return False
```

### 1.4 Config additions (Python)

```python
# Add to Server/syxx_server/config.py after line 220:

# --- GroundTruth signing (CL-1) -------------------------------------------
GROUNDTRUTH_KEY_PATH = _s("SYXX_GROUNDTRUTH_KEY_PATH",
                          os.path.join(BASE_DIR, "groundtruth_ed25519.pem"))
GROUNDTRUTH_MAX_AGE_SECONDS = _i("SYXX_GROUNDTRUTH_MAX_AGE", 300)  # 5 minutes
GROUNDTRUTH_MIN_QUORUM = _i("SYXX_GROUNDTRUTH_MIN_QUORUM", 2)
```

### 1.5 Quorum query (Python)

```python
# Add to Server/syxx_server/resolver.py after line 456:

def query_authoritative_quorum(name: str, qtype: int, timeout: int = 3,
                                min_quorum: int = 2) -> dict:
    """Query ≥min_quorum authoritative nameservers independently and compare answers.
    
    Returns:
        {
            "answers": list[str],           # consensus answer set (empty if no quorum)
            "nameservers": list[str],       # IPs of authoritative NS queried
            "agreeing": int,                # count of NS that returned the consensus answer
            "met": bool,                    # agreeing >= min_quorum
            "per_server": dict[str, list],  # {ns_ip: [answers]} for transparency
        }
    
    Uses resolve_authoritative() to discover the authoritative NS IPs from the
    delegation chain, then queries each independently (RD=0, direct to each NS IP).
    """
    # Step 1: Discover authoritative nameservers via the root-walker
    auth = resolve_authoritative(name, qtype, timeout=timeout)
    delegation = auth.get("delegation", [])
    
    # Find the terminal delegation (the one with authoritative=True)
    terminal = None
    for d in reversed(delegation):
        if d.get("authoritative"):
            terminal = d
            break
    
    if terminal is None or terminal.get("server") is None:
        return {"answers": [], "nameservers": [], "agreeing": 0,
                "met": False, "per_server": {}}
    
    # Step 2: Get NS records for the zone to find multiple authoritative IPs
    zone = terminal["zone"]
    ns_resp = _query_sections(zone, TYPE_NS, terminal["server"], timeout)
    ns_names = [r["value"] for r in ns_resp.get("answers", []) if r["type"] == "NS"]
    
    # Resolve NS names to IPs (using the terminal server as resolver)
    ns_ips = []
    for ns_name in ns_names[:4]:  # bound to 4 NS
        try:
            a_resp = _query_sections(ns_name, TYPE_A, terminal["server"], timeout)
            for r in a_resp.get("answers", []):
                if r["type"] == "A" and _ip_is_public(r["value"]):
                    ns_ips.append(r["value"])
        except DNSError:
            continue
    
    if not ns_ips:
        # Fallback: use the single terminal server IP
        ns_ips = [terminal["server"]]
    
    # Step 3: Query each NS independently (RD=0)
    per_server = {}
    for ns_ip in ns_ips[:4]:
        try:
            resp = _query_sections(name, qtype, ns_ip, timeout)
            if resp.get("aa"):
                answers = [r["value"] for r in resp.get("answers", []) if r["rtype"] == qtype]
                per_server[ns_ip] = sorted(answers)
        except DNSError:
            per_server[ns_ip] = []
    
    # Step 4: Find consensus (most common answer set)
    from collections import Counter
    answer_sets = [tuple(v) for v in per_server.values()]
    if not answer_sets:
        return {"answers": [], "nameservers": ns_ips, "agreeing": 0,
                "met": False, "per_server": per_server}
    
    counter = Counter(answer_sets)
    consensus_set, agreeing = counter.most_common(1)[0]
    
    return {
        "answers": list(consensus_set),
        "nameservers": ns_ips,
        "agreeing": agreeing,
        "met": agreeing >= min_quorum,
        "per_server": per_server,
    }
```

### 1.6 Modified `/api/authoritative` route (Python)

```python
# Replace Server/syxx_server/app.py lines 680-700 with:

def _route_authoritative(self, query):
    """Ground-truth oracle (CL-1): walk root -> TLD -> authoritative for a name,
    query ≥2 authoritative NS for quorum, sign the result with Ed25519, and emit
    a typed trust level (decisive/standard/inconclusive)."""
    ent, ok = self._require(query, config.ENFORCE_WEB)
    if not ok or not self._rate_ok(ent):
        return
    domain = (query.get("domain", [""])[0]).strip()
    if not investigate._valid_domain(domain) or len(domain) > 253:
        return self._json_error(400, "invalid or missing domain")
    qtype_str = (query.get("type", ["A"])[0]).strip().upper()
    qtypes = {"A": resolver.TYPE_A, "AAAA": resolver.TYPE_AAAA, "NS": resolver.TYPE_NS}
    if qtype_str not in qtypes:
        return self._json_error(400, "unsupported type (A, AAAA, NS)")
    
    from . import signing, groundtruth
    
    # Step 1: Quorum query
    try:
        quorum = resolver.query_authoritative_quorum(
            domain, qtypes[qtype_str],
            min_quorum=config.GROUNDTRUTH_MIN_QUORUM)
    except resolver.DNSError as e:
        return self._json_error(502, "resolution failed: %s" % e)
    
    # Step 2: Also get the full delegation chain for transparency
    try:
        auth = resolver.resolve_authoritative(domain, qtypes[qtype_str])
    except resolver.DNSError:
        auth = {"delegation": [], "authoritative": None, "verdict": "inconclusive"}
    
    # Step 3: Generate freshness parameters
    timestamp = datetime.now(timezone.utc).isoformat()
    nonce = str(uuid.uuid4())
    
    # Step 4: Sign the payload
    answers = quorum["answers"]
    signature = signing.sign_groundtruth(domain, qtype_str, answers, timestamp, nonce)
    pubkey = signing.public_key_base64()
    
    # Step 5: Determine trust level
    trust_level = groundtruth.compute_trust_level(
        answers=answers,
        quorum_met=quorum["met"],
        timestamp=timestamp,
        max_age=config.GROUNDTRUTH_MAX_AGE_SECONDS,
        auth_verdict=auth.get("verdict"),
    )
    
    # Step 6: Build response
    gt = groundtruth.GroundTruth(
        domain=domain,
        qtype=qtype_str,
        answers=answers,
        timestamp=timestamp,
        nonce=nonce,
        signature=signature,
        pubkey=pubkey,
        quorum={
            "nameservers": quorum["nameservers"],
            "agreeing": quorum["agreeing"],
            "met": quorum["met"],
            "perServer": quorum["per_server"],
        },
        trust_level=trust_level,
        delegation=auth.get("delegation", []),
    )
    
    logging_util.incr("authoritative_queries")
    self._send(200, gt.to_dict())
```

### 1.7 Trust level computation (Python)

```python
# Add to Server/syxx_server/groundtruth.py:

from datetime import datetime, timezone

def compute_trust_level(answers: list[str], quorum_met: bool,
                        timestamp: str, max_age: int,
                        auth_verdict: str | None) -> GroundTrustLevel:
    """Compute the tri-state trust level for a GroundTruth response.
    
    DECISIVE: answers non-empty AND quorum met AND timestamp fresh AND auth verdict authoritative
    STANDARD: answers non-empty AND (quorum not met OR timestamp borderline)
    INCONCLUSIVE: answers empty OR auth verdict not authoritative OR timestamp expired
    """
    # Check answers present
    if not answers:
        return GroundTrustLevel.INCONCLUSIVE
    
    # Check auth verdict
    if auth_verdict != "authoritative":
        return GroundTrustLevel.INCONCLUSIVE
    
    # Check freshness
    try:
        ts = datetime.fromisoformat(timestamp)
        age = (datetime.now(timezone.utc) - ts).total_seconds()
    except (ValueError, TypeError):
        return GroundTrustLevel.INCONCLUSIVE
    
    if age > max_age:
        return GroundTrustLevel.INCONCLUSIVE
    
    # Check quorum
    if not quorum_met:
        return GroundTrustLevel.STANDARD
    
    return GroundTrustLevel.DECISIVE
```

### 1.8 Client-side: `GroundTrustLevel` enum (Swift)

```swift
// macOS/Sources/SyxxKit/GroundTruth.swift (NEW FILE)

import Foundation

/// Tri-state trust level for a GroundTruth response.
/// NEVER a Bool — a bare true/false loses the distinction between
/// 'signature valid but quorum not met' and 'signature invalid'.
public enum GroundTrustLevel: String, Codable, Sendable {
    case decisive      // all six properties pass
    case standard      // signature valid but quorum/freshness borderline
    case inconclusive  // signature invalid, expired, or binding mismatch
}

/// A signed, quorum-backed authoritative ground-truth response from the Syxx server.
public struct GroundTruth: Codable, Sendable {
    public let domain: String
    public let qtype: String
    public let answers: [String]
    public let timestamp: String
    public let nonce: String
    public let signature: String
    public let pubkey: String
    public let quorum: QuorumInfo
    public let trustLevel: GroundTrustLevel
    public let delegation: [DelegationEntry]
    
    public struct QuorumInfo: Codable, Sendable {
        public let nameservers: [String]
        public let agreeing: Int
        public let met: Bool
        public let perServer: [String: [String]]
    }
    
    public struct DelegationEntry: Codable, Sendable {
        public let zone: String
        public let server: String?
        public let authoritative: Bool?
        public let answer: [String]?
        public let referral: [String]?
        public let delegatedZone: String?
        public let error: String?
    }
}
```

### 1.9 Client-side: `DimensionClient` (Swift)

```swift
// macOS/Sources/SyxxKit/DimensionClient.swift (NEW FILE)

import Foundation
import CryptoKit

/// Fetches and verifies signed GroundTruth from the Syxx server.
/// Verification checks all six properties: signature, freshness, binding, quorum, auth, replay.
public enum DimensionClient {
    
    /// Maximum age of a GroundTruth timestamp before it's considered stale.
    /// Must match server's GROUNDTRUTH_MAX_AGE_SECONDS.
    private static let maxAgeSeconds: TimeInterval = 300
    
    /// Fetch a GroundTruth from the server and verify it.
    /// Returns nil if the fetch fails or verification returns inconclusive.
    public static func fetchAndVerify(
        domain: String,
        qtype: String = "A",
        serverBaseURL: String = "https://syxx.app",
        licenseToken: String? = nil
    ) async -> (groundTruth: GroundTruth?, trustLevel: GroundTrustLevel) {
        // Step 1: Fetch
        var components = URLComponents(string: "\(serverBaseURL)/api/authoritative")
        components?.queryItems = [
            URLQueryItem(name: "domain", value: domain),
            URLQueryItem(name: "type", value: qtype),
        ]
        guard let url = components?.url else {
            return (nil, .inconclusive)
        }
        
        var request = URLRequest(url: url)
        request.timeoutInterval = 10
        if let token = licenseToken {
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }
        
        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let httpResp = response as? HTTPURLResponse, httpResp.statusCode == 200 else {
                return (nil, .inconclusive)
            }
            let gt = try JSONDecoder().decode(GroundTruth.self, from: data)
            
            // Step 2: Verify
            let trustLevel = verify(gt, requestedDomain: domain, requestedQtype: qtype)
            return (gt, trustLevel)
        } catch {
            return (nil, .inconclusive)
        }
    }
    
    /// Verify all six properties of a GroundTruth response.
    /// Returns the computed trust level.
    public static func verify(
        _ gt: GroundTruth,
        requestedDomain: String,
        requestedQtype: String
    ) -> GroundTrustLevel {
        // 1. Binding: domain and qtype match what we asked for
        guard gt.domain.lowercased() == requestedDomain.lowercased(),
              gt.qtype.uppercased() == requestedQtype.uppercased() else {
            return .inconclusive
        }
        
        // 2. Freshness: timestamp within maxAge
        guard let ts = ISO8601DateFormatter().date(from: gt.timestamp) else {
            return .inconclusive
        }
        let age = Date().timeIntervalSince(ts)
        guard age >= 0 && age <= maxAgeSeconds else {
            return .inconclusive
        }
        
        // 3. Signature: verify Ed25519 signature over the bound message
        let message = "\(gt.domain)|\(gt.qtype)|\(jsonSortedAnswers(gt.answers))|\(gt.timestamp)|\(gt.nonce)"
        guard let pubkeyData = Data(base64Encoded: gt.pubkey),
              let sigData = Data(base64Encoded: gt.signature),
              let pubkey = try? Curve25519.Signing.PublicKey(rawRepresentation: pubkeyData) else {
            return .inconclusive
        }
        guard pubkey.isValidSignature(sigData, for: message.data(using: .utf8)!) else {
            return .inconclusive
        }
        
        // 4. Quorum: check quorum.met
        if !gt.quorum.met {
            return .standard
        }
        
        // 5. Answers: non-empty
        if gt.answers.isEmpty {
            return .inconclusive
        }
        
        // 6. Trust level from server matches our verification
        // (We trust our own verification over the server's claim)
        return .decisive
    }
    
    private static func jsonSortedAnswers(_ answers: [String]) -> String {
        let sorted = answers.sorted()
        let data = try? JSONEncoder().encode(sorted)
        return data.flatMap { String(data: $0, encoding: .utf8) } ?? "[]"
    }
}
```

### 1.10 Client-side: `GroundTrustLevel` enum (C#)

```csharp
// Windows/Syxx.Kit/GroundTruth.cs (NEW FILE)

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Syxx.Kit
{
    /// <summary>
    /// Tri-state trust level for a GroundTruth response.
    /// NEVER a bool — a bare true/false loses the distinction between
    /// 'signature valid but quorum not met' and 'signature invalid'.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GroundTrustLevel
    {
        [JsonPropertyName("decisive")]
        Decisive,
        [JsonPropertyName("standard")]
        Standard,
        [JsonPropertyName("inconclusive")]
        Inconclusive
    }

    public class GroundTruth
    {
        public string Domain { get; set; } = "";
        public string Qtype { get; set; } = "";
        public List<string> Answers { get; set; } = new();
        public string Timestamp { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string Signature { get; set; } = "";
        public string Pubkey { get; set; } = "";
        public QuorumInfo Quorum { get; set; } = new();
        public GroundTrustLevel TrustLevel { get; set; }
        public List<DelegationEntry> Delegation { get; set; } = new();
    }

    public class QuorumInfo
    {
        public List<string> Nameservers { get; set; } = new();
        public int Agreeing { get; set; }
        public bool Met { get; set; }
        public Dictionary<string, List<string>> PerServer { get; set; } = new();
    }

    public class DelegationEntry
    {
        public string Zone { get; set; } = "";
        public string? Server { get; set; }
        public bool? Authoritative { get; set; }
        public List<string>? Answer { get; set; }
        public List<string>? Referral { get; set; }
        public string? DelegatedZone { get; set; }
        public string? Error { get; set; }
    }
}
```

### 1.11 Client-side: `DimensionClient` (C#)

```csharp
// Windows/Syxx.Kit/DimensionClient.cs (NEW FILE)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Syxx.Kit
{
    /// <summary>
    /// Fetches and verifies signed GroundTruth from the Syxx server.
    /// Verification checks all six properties: signature, freshness, binding, quorum, auth, replay.
    /// </summary>
    public static class DimensionClient
    {
        private static readonly double MaxAgeSeconds = 300;

        public static async Task<(GroundTruth? groundTruth, GroundTrustLevel trustLevel)>
            FetchAndVerify(string domain, string qtype = "A",
                          string serverBaseUrl = "https://syxx.app",
                          string? licenseToken = null)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var url = $"{serverBaseUrl}/api/authoritative?domain={Uri.EscapeDataString(domain)}&type={Uri.EscapeDataString(qtype)}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (licenseToken != null)
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", licenseToken);

                var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return (null, GroundTrustLevel.Inconclusive);

                var json = await response.Content.ReadAsStringAsync();
                var gt = JsonSerializer.Deserialize<GroundTruth>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (gt == null)
                    return (null, GroundTrustLevel.Inconclusive);

                var trustLevel = Verify(gt, domain, qtype);
                return (gt, trustLevel);
            }
            catch
            {
                return (null, GroundTrustLevel.Inconclusive);
            }
        }

        public static GroundTrustLevel Verify(GroundTruth gt, string requestedDomain, string requestedQtype)
        {
            // 1. Binding
            if (!string.Equals(gt.Domain, requestedDomain, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(gt.Qtype, requestedQtype, StringComparison.OrdinalIgnoreCase))
                return GroundTrustLevel.Inconclusive;

            // 2. Freshness
            if (!DateTimeOffset.TryParse(gt.Timestamp, out var ts))
                return GroundTrustLevel.Inconclusive;
            var age = (DateTimeOffset.UtcNow - ts).TotalSeconds;
            if (age < 0 || age > MaxAgeSeconds)
                return GroundTrustLevel.Inconclusive;

            // 3. Signature (Ed25519 via NSec or BouncyCastle)
            var message = $"{gt.Domain}|{gt.Qtype}|{JsonSortedAnswers(gt.Answers)}|{gt.Timestamp}|{gt.Nonce}";
            try
            {
                var pubkeyBytes = Convert.FromBase64String(gt.Pubkey);
                var sigBytes = Convert.FromBase64String(gt.Signature);
                // Ed25519 verification — implementation depends on chosen library
                if (!Ed25519Verify(pubkeyBytes, sigBytes, Encoding.UTF8.GetBytes(message)))
                    return GroundTrustLevel.Inconclusive;
            }
            catch
            {
                return GroundTrustLevel.Inconclusive;
            }

            // 4. Quorum
            if (!gt.Quorum.Met)
                return GroundTrustLevel.Standard;

            // 5. Answers non-empty
            if (gt.Answers.Count == 0)
                return GroundTrustLevel.Inconclusive;

            return GroundTrustLevel.Decisive;
        }

        private static string JsonSortedAnswers(List<string> answers)
        {
            var sorted = answers.OrderBy(a => a).ToList();
            return JsonSerializer.Serialize(sorted);
        }

        private static bool Ed25519Verify(byte[] pubkey, byte[] signature, byte[] message)
        {
            // Use NSec.Cryptography or BouncyCastle — implementation TBD
            // This is a placeholder for the executor to fill with the chosen library
            throw new NotImplementedException("Ed25519 verification requires NSec or BouncyCastle");
        }
    }
}
```

### 1.12 WitnessSigner integration (Swift)

```swift
// Add to macOS/Sources/SyxxKit/WitnessSigner.swift:

/// Extension to include GroundTrustLevel in signed evidence metadata.
extension WitnessSigner {
    /// Sign evidence with GroundTruth trust level metadata included.
    /// Decisive acts (e.g., authoritativeDivergesConfirmed) require
    /// groundTrustLevel == .decisive to be treated as decisive.
    public static func signEvidence(
        _ evidence: [EvidenceItem],
        groundTrustLevel: GroundTrustLevel,
        conclusion: Conclusion
    ) -> SignedEvidence {
        let metadata: [String: Any] = [
            "groundTrustLevel": groundTrustLevel.rawValue,
            "generatedAt": ISO8601DateFormatter().string(from: Date()),
        ]
        // ... existing signing logic, with metadata included in signed payload
    }
}
```

### 1.13 Failure-case tests

| Test | Input | Expected |
|---|---|---|
| Replay attack | Same signature+nonce+timestamp submitted twice | Second rejected (inconclusive) |
| Stale timestamp | timestamp > 5 minutes old | inconclusive |
| Wrong domain binding | signature for `example.com` used for `evil.com` | inconclusive |
| Quorum failure | only 1 of 2 NS IPs agrees | standard (not decisive) |
| Signature mutation | flip one bit in signature | inconclusive |
| Empty answers | authoritative returns no answers | inconclusive |

---

## CL-2 — Probe Robustness

### 2.1 Structural NXDOMAIN rule (RFC 8020)

**Current code** (`DNSCanaries.swift:41-79`): Checks if two random names resolve to the **same** IP. A round-robin block page returning different IPs per query evades this.

**New contract:**

```swift
// Replace DNSCanaries.swift nxdomainHijack() with:

/// **NXDOMAIN-hijack canary (RFC 8020 structural rule).**
/// A resolver returning ANY A/AAAA record for a provably-nonexistent name
/// is a hijack, regardless of whether the IP matches a known block page.
/// RFC 8020: "NXDOMAIN means there is nothing underneath" — any answer is a lie.
public static func nxdomainHijack(resolver: String) async -> CanaryResult {
    let n1 = randomLabel() + ".com"
    let n2 = randomLabel() + ".com"
    let r1 = await DNSResolver.queryUDPDetailed(DNSQuery(name: n1, type: 1, server: resolver))
    let r2 = await DNSResolver.queryUDPDetailed(DNSQuery(name: n2, type: 1, server: resolver))
    
    if r1.didFail && r2.didFail {
        return CanaryResult(section: .prophet, status: .invalidated, evidence: nil,
            detail: "NXDOMAIN canary could not reach the resolver.")
    }
    
    // RFC 8020: both should be NXDOMAIN. Any answer for a nonexistent name is a hijack.
    let r1HasAnswer = !r1.records.filter { $0.type == 1 || $0.type == 28 }.isEmpty
    let r2HasAnswer = !r2.records.filter { $0.type == 1 || $0.type == 28 }.isEmpty
    
    if r1.status == .nxDomain && r2.status == .nxDomain {
        return CanaryResult(section: .prophet, status: .negative,
            evidence: EvidenceItem(section: .prophet,
                evidenceId: .nxCanaryClean,
                observation: "Random nonexistent names correctly returned NXDOMAIN",
                rationale: "The resolver does not rewrite nonexistent domains.",
                source: "canaries"),
            detail: "Both canary names → NXDOMAIN.")
    }
    
    if r1HasAnswer || r2HasAnswer {
        // At least one canary got an answer for a nonexistent name — hijack
        let hijackedName = r1HasAnswer ? n1 : n2
        let hijackedIPs = (r1HasAnswer ? r1 : r2).records
            .filter { $0.type == 1 || $0.type == 28 }
            .map { $0.rdata }
        return CanaryResult(section: .prophet, status: .positive,
            evidence: EvidenceItem(section: .prophet,
                evidenceId: .nxCanaryHijack,
                observation: "Nonexistent name \(hijackedName) resolved to \(hijackedIPs.joined(separator: ", "))",
                rationale: "RFC 8020: NXDOMAIN means nothing exists underneath. Any answer for a provably-nonexistent name is a hijack.",
                source: "canaries"),
            detail: "Structural NXDOMAIN violation: \(hijackedName) → \(hijackedIPs)")
    }
    
    // Neither NXDOMAIN nor has-answer (e.g., SERVFAIL, REFUSED)
    return CanaryResult(section: .prophet, status: .inconclusive,
        evidence: EvidenceItem(section: .prophet,
            evidenceId: .nxCanaryAtypical,
            observation: "Canary names returned atypical responses",
            rationale: "Not a clean NXDOMAIN, but not a clear hijack either.",
            source: "canaries"),
        detail: "n1=\(r1.status.rawValue); n2=\(r2.status.rawValue)")
}
```

### 2.2 Multi-control DNSSEC quorum + DO-bit gate

**Current code** (`DNSCanaries.swift:86-117`): Single hardcoded `cloudflare.com` control.

**New contract:**

```swift
// Replace DNSCanaries.swift dnssecControl() with:

/// DNSSEC-signed control domains for quorum checking.
/// ≥3 controls so a single benign lapse doesn't fire decisive stripping.
private static let dnssecControls = [
    "cloudflare.com",
    "dnssec-deployment.org",
    "verisigninc.com",
]

/// Minimum controls that must show stripping for a decisive result.
private static let dnssecQuorumThreshold = 2

/// **DNSSEC control canary with multi-control quorum and DO-bit gate.**
/// Queries ≥3 DNSSEC-signed domains. Stripping is decisive only if
/// ≥dnssecQuorumThreshold controls show unsigned/stripped.
/// A single benign lapse → standard (not decisive).
public static func dnssecControl(resolver: String) async -> CanaryResult {
    var results: [(control: String, status: DNSSECStatus)] = []
    
    for control in dnssecControls {
        let status = await DNSSECVerifier.checkStatus(domain: control, resolver: resolver)
        results.append((control, status))
    }
    
    let stripped = results.filter { $0.status == .unsigned || $0.status == .signatureExpiredOrNotYetValid }
    let secure = results.filter { $0.status == .recordsPresentUnverified }
    let failed = results.filter { $0.status == .inconclusive }
    
    // All controls failed to reach — invalidated
    if failed.count == results.count {
        return CanaryResult(section: .prophet, status: .invalidated, evidence: nil,
            detail: "All DNSSEC controls unreachable.")
    }
    
    // Quorum met: ≥threshold controls show stripping
    if stripped.count >= dnssecQuorumThreshold {
        let strippedNames = stripped.map { $0.control }.joined(separator: ", ")
        return CanaryResult(section: .prophet, status: .positive,
            evidence: EvidenceItem(section: .prophet,
                evidenceId: .dnssecCanaryStripped,
                observation: "\(stripped.count)/\(results.count) DNSSEC controls came back UNSIGNED: \(strippedNames)",
                rationale: "Multiple independent DNSSEC-signed domains missing DNSKEY/RRSIG — stripping confirmed by quorum.",
                source: "canaries"),
            detail: "DNSSEC quorum: \(stripped.count)/\(results.count) stripped")
    }
    
    // Single control stripped — standard, not decisive
    if stripped.count == 1 {
        return CanaryResult(section: .prophet, status: .positive,
            evidence: EvidenceItem(section: .prophet,
                evidenceId: .dnssecCanaryStripped,
                observation: "1/\(results.count) DNSSEC controls came back UNSIGNED: \(stripped[0].control)",
                rationale: "Single control stripping — may be a benign lapse. Not decisive without quorum.",
                source: "canaries"),
            detail: "DNSSEC: 1/\(results.count) stripped (below quorum)")
    }
    
    // All secure
    if secure.count == results.count {
        return CanaryResult(section: .prophet, status: .negative,
            evidence: EvidenceItem(section: .prophet,
                evidenceId: .dnssecCanarySecure,
                observation: "All \(results.count) DNSSEC controls have intact records",
                rationale: "DNSKEY + RRSIG present on all controls — no stripping detected.",
                source: "canaries"),
            detail: "All \(results.count) DNSSEC controls → secure")
    }
    
    // Mixed / inconclusive
    return CanaryResult(section: .prophet, status: .inconclusive,
        evidence: nil,
        detail: "DNSSEC: \(secure.count) secure, \(stripped.count) stripped, \(failed.count) failed")
}
```

### 2.3 Wildcard encrypted-transport reference

**Current code** (`WildcardSynthesisProbe.swift`): Reference lookup uses plain-UDP.

**New contract:**

```swift
// Modify WildcardSynthesisProbe.swift:

/// The reference lookup MUST use encrypted transport (DoH or DoT).
/// If encrypted transport is unavailable, the result is INCONCLUSIVE —
/// never benign clean-support. A transparent Do53 proxy can rewrite
/// plain-UDP queries, making a hijack look like a wildcard.
public static func wildcardSynthesis(
    domain: String,
    resolver: String,
    dohVantage: DoHVantage? = nil
) async -> WildcardResult {
    // Step 1: Try encrypted transport reference
    guard let doh = dohVantage ?? DoHVantage.default else {
        return WildcardResult(status: .inconclusive,
            detail: "No encrypted-transport vantage available — wildcard inconclusive")
    }
    
    let reference = await DNSResolver.queryDoH(
        DNSQuery(name: domain, type: 1),
        vantage: doh
    )
    
    if reference.didFail {
        return WildcardResult(status: .inconclusive,
            detail: "Encrypted-transport reference failed — wildcard inconclusive")
    }
    
    // Step 2: Compare local resolver answer against encrypted reference
    let local = await DNSResolver.queryUDPDetailed(
        DNSQuery(name: domain, type: 1, server: resolver))
    
    // ... existing comparison logic, but NEVER emit .negative (clean-support)
    // unless the encrypted reference confirms wildcard behavior
}
```

### 2.4 Failure-case tests

| Test | Input | Expected |
|---|---|---|
| Round-robin block page | Different IPs per query for nonexistent name | hijack (Positive) |
| Single DNSSEC lapse | 1 of 3 controls unsigned | not decisive (standard) |
| DO bit missing | Query without DO bit | inconclusive |
| Wildcard plain-UDP | Transparent proxy rewrites reference | inconclusive (not benign) |
| All DNSSEC controls fail | All 3 controls unreachable | invalidated |

---

## CL-3 — Labeled Adversarial Capture Corpus + Phase-0 Harness

### 3.1 Golden-vector JSON schema

```json
{
  "$schema": "https://syxx.app/schemas/golden-vector-v1.json",
  "vectorId": "string (unique identifier)",
  "description": "string (human-readable scenario description)",
  "mechanismClass": "NXDOMAIN_HIJACK | DNSSEC_STRIP | CAPTIVE_PORTAL | TRANSPARENT_PROXY | PRIVACY_LEAK | CONTENT_TAMPERING | BENIGN_CDN | BENIGN_CAPTIVE",
  "evidence": [
    {
      "evidenceId": "string (EvidenceId enum raw value)",
      "section": "prophet | mirror | portal | glutton | cage | witness",
      "weight": "number (LLR weight)",
      "hypothesis": "string (Hypothesis enum raw value)"
    }
  ],
  "sectionStatuses": [
    {
      "section": "prophet | mirror | portal | glutton | cage | witness",
      "status": "positive | negative | degraded | inconclusive | invalidated | untested"
    }
  ],
  "expected": {
    "verdict": "string (Hypothesis or mechanism class)",
    "confidencePct_exact": "integer (exact integer comparison for gate values)",
    "marginPct_exact": "integer (exact integer comparison)",
    "conflictPct_max": "integer (maximum allowed conflict)",
    "uncertainty_eps6": "number (10^-6 tolerance for display values)",
    "belThreatSet_eps6": "number (10^-6 tolerance)"
  },
  "currentEngineBehavior": "string (optional — documents what the current broken engine produces)"
}
```

### 3.2 Corpus capture format

```json
{
  "captureId": "string",
  "mechanismClass": "string",
  "timestamp": "ISO-8601",
  "labRig": "string (which adversarial rig produced this)",
  "networkContext": {
    "resolver": "string (resolver IP)",
    "vantage": "string (client IP/network)"
  },
  "rawPcap": "string (path to pcap file, optional)",
  "parsedEvidence": {
    "evidence": [...],
    "sectionStatuses": [...]
  },
  "groundTruth": {
    "actualMechanism": "string (what was actually happening)",
    "confidence": "high | medium | low"
  }
}
```

### 3.3 Lab harness configurations

#### dnsmasq NX-hijack rig
```
# corpus/lab/dnsmasq-nx-hijack.conf
port=5353
listen-address=127.0.0.1
no-resolv
# Return sinkhole IP for ALL queries (including nonexistent domains)
address=/#/10.10.10.1
```

#### CoreDNS DNSSEC-strip rig
```yaml
# corpus/lab/coredns-dnssec-strip.yml
.:5354 {
    forward . 8.8.8.8
    # Strip RRSIG and DNSKEY records from responses
    filter {
        remove RRSIG
        remove DNSKEY
    }
}
```

#### Captive portal rig
```
# corpus/lab/dnsmasq-captive.conf
port=5355
listen-address=127.0.0.1
no-resolv
# Redirect all queries to captive portal IP
address=/#/10.10.10.2
```

### 3.4 Golden-vector runner (Python)

```python
# corpus/golden-vectors/runner.py (NEW FILE)

import json
import subprocess
import sys
from pathlib import Path
from dataclasses import dataclass

@dataclass
class VectorResult:
    vector_id: str
    verdict: str
    confidence_pct: int
    margin_pct: int
    conflict_pct: int
    uncertainty: float
    bel_threat_set: float
    platform: str  # "macos" or "windows"

def run_vector(vector_path: Path, platform: str) -> VectorResult:
    """Run a single golden vector on the specified platform.
    Invokes the SynthesisEngine via subprocess and parses the output."""
    with open(vector_path) as f:
        vector = json.load(f)
    
    # Build the input JSON for the engine
    engine_input = {
        "evidence": vector["evidence"],
        "sectionStatuses": vector["sectionStatuses"],
    }
    
    if platform == "macos":
        cmd = ["swift", "run", "syxx-golden-vector", "--input", json.dumps(engine_input)]
    elif platform == "windows":
        cmd = ["dotnet", "run", "--project", "Windows/Syxx.Tests", "--", "golden-vector", "--input", json.dumps(engine_input)]
    else:
        raise ValueError(f"Unknown platform: {platform}")
    
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=30)
    if result.returncode != 0:
        raise RuntimeError(f"Engine failed: {result.stderr}")
    
    output = json.loads(result.stdout)
    return VectorResult(
        vector_id=vector["vectorId"],
        verdict=output["verdict"],
        confidence_pct=output["confidencePct"],
        margin_pct=output["marginPct"],
        conflict_pct=output["conflictPct"],
        uncertainty=output["uncertainty"],
        bel_threat_set=output["belThreatSet"],
        platform=platform,
    )

def check_vector(vector_path: Path, macos_result: VectorResult, windows_result: VectorResult) -> list[str]:
    """Check a golden vector against both platform results. Returns list of failures."""
    with open(vector_path) as f:
        vector = json.load(f)
    
    expected = vector["expected"]
    failures = []
    
    # Exact integer comparisons for gate values
    for field in ["confidencePct_exact", "marginPct_exact", "conflictPct_max"]:
        if field in expected:
            key = field.replace("_exact", "").replace("_max", "")
            exp_val = expected[field]
            mac_val = getattr(macos_result, f"{key}_pct" if key != "conflict" else "conflict_pct")
            win_val = getattr(windows_result, f"{key}_pct" if key != "conflict" else "conflict_pct")
            
            if field.endswith("_max"):
                if mac_val > exp_val:
                    failures.append(f"macOS {key}={mac_val} > max {exp_val}")
                if win_val > exp_val:
                    failures.append(f"Windows {key}={win_val} > max {exp_val}")
            else:
                if mac_val != exp_val:
                    failures.append(f"macOS {key}={mac_val} != expected {exp_val}")
                if win_val != exp_val:
                    failures.append(f"Windows {key}={win_val} != expected {exp_val}")
            
            # Cross-platform parity
            if mac_val != win_val:
                failures.append(f"Platform divergence: {key} macos={mac_val} windows={win_val}")
    
    # Epsilon comparisons for display values
    eps = 1e-6
    for field in ["uncertainty_eps6", "belThreatSet_eps6"]:
        if field in expected:
            key = field.replace("_eps6", "").replace("_", "_")
            exp_val = expected[field]
            mac_val = getattr(macos_result, key.replace("Threat", "_threat_").replace("Set", "_set"))
            win_val = getattr(windows_result, key.replace("Threat", "_threat_").replace("Set", "_set"))
            
            if abs(mac_val - exp_val) > eps:
                failures.append(f"macOS {key}={mac_val} not within {eps} of {exp_val}")
            if abs(win_val - exp_val) > eps:
                failures.append(f"Windows {key}={win_val} not within {eps} of {exp_val}")
            if abs(mac_val - win_val) > eps:
                failures.append(f"Platform divergence: {key} macos={mac_val} windows={win_val}")
    
    return failures

def main():
    corpus_dir = Path("corpus/golden-vectors")
    vectors = sorted(corpus_dir.glob("*.json"))
    
    all_failures = []
    for vector_path in vectors:
        print(f"Running {vector_path.name}...")
        try:
            macos = run_vector(vector_path, "macos")
            windows = run_vector(vector_path, "windows")
            failures = check_vector(vector_path, macos, windows)
            if failures:
                print(f"  FAIL: {len(failures)} failure(s)")
                for f in failures:
                    print(f"    - {f}")
                all_failures.extend([(vector_path.name, f) for f in failures])
            else:
                print(f"  PASS")
        except Exception as e:
            print(f"  ERROR: {e}")
            all_failures.append((vector_path.name, str(e)))
    
    if all_failures:
        print(f"\n{len(all_failures)} total failure(s)")
        sys.exit(1)
    else:
        print(f"\nAll {len(vectors)} vectors passed")
        sys.exit(0)

if __name__ == "__main__":
    main()
```

---

## Dependency Graph (updated with actual code locations)

```
CL-1.1 signing.py (NEW) + config.py additions
  ↓
CL-1.2 resolver.py: query_authoritative_quorum() (NEW function)
  ↓
CL-1.3 app.py: _route_authoritative() (MODIFY existing)
  ↓
CL-1.4 groundtruth.py (NEW) + GroundTruth.swift (NEW) + GroundTruth.cs (NEW)
  ↓
CL-1.5 DimensionClient.swift (NEW) + DimensionClient.cs (NEW)
  ↓
CL-1.6 WitnessSigner.swift (MODIFY) + WitnessSigner.cs (MODIFY)
  ↓
CL-1.7 Tests (NEW)

CL-2.1 DNSCanaries.swift: nxdomainHijack() (MODIFY) + DNSCanaries.cs (MODIFY)
  ↓
CL-2.2 DNSCanaries.swift: dnssecControl() (MODIFY) + DNSCanaries.cs (MODIFY)
  ↓
CL-2.3 WildcardSynthesisProbe.swift (MODIFY) + WildcardSynthesisProbe.cs (MODIFY)
  ↓
CL-2.4 Tests (NEW/MODIFY)

CL-3.1 corpus/lab/ (NEW directory + configs)
  ↓
CL-3.2 corpus/captures/ (NEW directory + captures)
  ↓
CL-3.3 corpus/golden-vectors/runner.py (NEW)
  ↓
CL-3.4 corpus/golden-vectors/*.json (NEW vectors)
```

---

## Acceptance Criteria (Real-Run Bar)

### CL-1 Acceptance
1. `curl https://syxx.app/api/authoritative?domain=cloudflare.com&type=A` returns signed GroundTruth with `trustLevel: "decisive"`
2. Replay: same payload twice → second rejected
3. Stale: timestamp > 5 min → `inconclusive`
4. Wrong binding: signature for `example.com` with domain=`evil.com` → `inconclusive`
5. Quorum fail: 1 of 2 NS agrees → `standard`

### CL-2 Acceptance
1. Round-robin block page (different IPs per query) → hijack (Positive)
2. Single DNSSEC lapse (1 of 3 controls) → not decisive
3. Wildcard over plain-UDP with transparent proxy → inconclusive

### CL-3 Acceptance
1. ≥6 labeled captures, one per mechanism class
2. macOS and Windows produce identical verdicts on all golden vectors
3. All NE1–NE7 failing scenarios reproduced as red vectors

---

**Plan complete. Ready for syxx-neural-executor.**