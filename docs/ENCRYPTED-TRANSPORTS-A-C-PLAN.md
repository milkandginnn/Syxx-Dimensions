# Encrypted Transports — Sub-projects A (client transports) + C (resolver profiles): Engineer-Ready Plan

Source of truth: `ENCRYPTED-TRANSPORTS-PLAN.md` §1–5 (A, C) and its §3 honesty contract; `DESIGN.md:309-341`.
Every file:line below was re-verified against the live tree on 2026-07-14 (branch `feat/beta-signup-and-server-hardening`). Anchors corrected from the program plan are flagged **[corrected]**.

**Locked decisions honored:** DoH3 = FREE. DoT + DoQ = PAID, gated on macOS `LicenseManager.isFeatureUnlocked()` / Windows server-validated `LicenseManager.IsFeatureUnlockedAsync()` — never a local bool. Strict macOS↔Windows parity.

---

## 0. VERIFY-FIRST SPIKE RESULT — macOS `NWProtocolQUIC` client bidirectional streams

**Verdict: VIABLE. DoQ-macOS is NOT blocked.** The research flagged this SPECULATIVE / PAUSE-worthy; on this toolchain it is real and usable.

- Toolchain observed: macOS **26.6** (build 25G5043d), Xcode **26.6**, Apple Swift **6.3.3**, target `arm64-apple-macos26.0`. (`macOS/Package.swift:7` still declares `.macOS(.v14)`; the QUIC path must be `@available(macOS 15.0, *)`-guarded so the package keeps building for its declared floor.)
- Throwaway snippets compiled clean with `swiftc -sdk "$(xcrun --show-sdk-path)" -target arm64-apple-macos26.0` (exit 0; not committed, deleted after). Confirmed-usable API surface for a DoQ client:
  - `NWProtocolQUIC.Options(alpn: ["doq"])`; `.idleTimeout`, `.maxUDPPayloadSize`, and **`.direction = .bidirectional`** (`NWProtocolQUIC.Options.Direction.bidirectional`) — the client-initiated bidirectional stream the research doubted exists.
  - `options.securityProtocolOptions` (`sec_protocol_options_t`) — the hook for a **fail-closed** `sec_protocol_verify` block (net-new DoT/DoQ pinning; the D003 URLSession pin does NOT apply here).
  - `NWMultiplexGroup(to: .hostPort(...))` + `NWConnectionGroup(with:using:)`; **`NWConnection(from: group)`** opens a client-initiated outbound stream; `send`/`receiveMessage` work on that stream.
  - Honest transport proof on the received context: `context.protocolMetadata(definition: NWProtocolQUIC.definition) as? NWProtocolQUIC.Metadata`, then **`metadata.negotiatedALPN`** (`String?`, e.g. `"doq"`) and `metadata.streamIdentifier` (`UInt64`).
- **Caveat (binding):** compile-viability is not runtime-verification. DoQ-macOS is planned as `[code-complete]` until step A3's real-run bar (AdGuard `94.140.14.14:853`, ALPN "doq", ID 0, cross-checked with `kdig +tls +quic`) passes on hardware. If the handshake proves unusable at runtime, mark `[untested]` and report `.untested`/inconclusive — never fail-silent.
- Bonus verified for A4: macOS h3 opt-in is **`URLRequest.assumesHTTP3Capable = true`** (NOT `URLSessionConfiguration.assumesHTTP3Capable`, which does not exist), proven via `URLSessionTaskMetrics.transactionMetrics.last?.networkProtocolName == "h3"`. Both compiled clean.

---

## 1. Overview, workstreams, dependency graph

Goal: give both client resolvers DoT, DoQ, and DoH3 with **per-answer transport provenance**, so an encrypted reference lookup cannot be silently rewritten by an on-path Do53 proxy and any downgrade is always visible; then let a user pick a trusted resolver profile that routes probes over the proven transport, gated correctly by tier.

Hard dependency order (each depends only on earlier):

```
A0 (tri-state result, = E030 extended)  ─┐
                                          ├─► A1 (provenance model on DNSResponse) ─┬─► A2 DoT (paid)
                                          │                                          ├─► A3 DoQ (paid)
                                          │                                          └─► A4 DoH3 (free)
                                          │                                                    │
                                          └────────────────────────────────────────────────────┘
                                                                                               ▼
                                                                            C (profiles persist + select + route,
                                                                               free→DoH3 / DoT·DoQ→paid gate,
                                                                               Unbound forward-over-DoT template → E)
```

- **A0 must land first.** It is the shared verdict type every transport hangs its secure/inconclusive/unverifiable result off. Landing a transport before A0 would crush a proven downgrade into a bool (the E030 defect) — a false-secure.
- **A1 must land before A2/A3/A4.** Every transport writes `servedTransport` / `negotiatedProtocol`; without the model they cannot report provenance and a downgrade is invisible.
- **A2/A3/A4 are mutually independent** once A1 lands; sequence by mission risk: **A4 DoH3 first** (free, ships to everyone, exercises the provenance path end to end with the lowest new-surface), then **A2 DoT**, then **A3 DoQ** (highest risk: new stream model both platforms, ID-0 trap, availability gates).
- **C depends on all of A** (it routes through the transports and reports provenance from them). The Unbound forward-over-DoT template (C's last step) is consumed by sub-project E and can land last.

Effort: A0 S · A1 S–M · A2 M · A3 L · A4 M · C M. Total M–L.

Every user-visible string in this plan is **proposed, not finalized** (§7); nothing lands in code without sign-off, and each goes to `outputs-catalog.csv`.

---

## Step A0 — Shared tri-state secure-resolution verdict (implements delta E030, extended)

**Anchor:** delta **E030** (`DELTAS.md:15888`, CONFIRMED, vetted; do NOT file a new delta). E030 Fix 1 (port the Windows enum as the sole verdict field) + Fix 2 (reject unknown decoded enum values) ARE the A0 work — **extended**: E030 only anticipated the existing `{Secure, Inconclusive}` shape and did not foresee the proven-downgrade case A1–A4 introduce. A0 therefore adds a third member for "an answer arrived but the encrypted-transport guarantee failed."

**Files (verified):**
- macOS `macOS/Sources/SyxxKit/DNSResolver.swift:192-198` — `struct SecureResolutionResponse` with two independent Bools `isSecure` / `isInconclusive` (the defect). Constructors at `:219-262` (`resolveSecurely`). **[corrected]** E030 cites `:191-197` / `:218-260`; live lines are `:192-198` / `:219-262`.
- Windows `Windows/Syxx.Kit/DNSResolver.cs:65-69` — `enum SecureResolutionResult { Secure, Inconclusive }`; `:71-77` `class SecureResolutionResponse`; constructors `:223-282` (`ResolveSecurelyAsync`). **[corrected]** E030 cites `:213-268`; live enum is `:65-69`, consumers `:223-282`.
- Consumers to migrate (small blast radius — no production caller today; only tests + the resolver itself): macOS test `macOS/Tests/SyxxKitTests/SyxxKitTests.swift:609-613` (`response.isSecure`); Windows test `Windows/Syxx.Tests/SyxxKitTests.cs:526-531` (`SecureResolutionResult.Inconclusive`).

**Method signatures / types to add:**
- macOS — replace the two Bools with one enum, the sole verdict field:
  ```swift
  public enum SecureResolutionResult: String, Codable, Sendable {
      case secure          // answer carried by a proven encrypted transport
      case inconclusive    // could not verify (timeout / transport error / loopback proxy / no answer)
      case unverifiable    // an answer arrived but over a proven-downgraded / unproven transport — untrustworthy as a reference
      // Fix 2: reject unknown/invalid serialized states rather than defaulting secure/inconclusive.
      public init(from decoder: Decoder) throws {
          let raw = try decoder.singleValueContainer().decode(String.self)
          guard let v = SecureResolutionResult(rawValue: raw) else {
              throw DecodingError.dataCorrupted(.init(codingPath: decoder.codingPath,
                  debugDescription: "Unknown SecureResolutionResult '\(raw)'"))
          }
          self = v
      }
  }
  public struct SecureResolutionResponse: Codable, Sendable {
      public let result: SecureResolutionResult    // sole verdict field
      public let records: [DNSRecord]
      public let provenance: String
      public let reason: String?
  }
  ```
- Windows — extend the existing enum and reject unknown values on deserialize:
  ```csharp
  public enum SecureResolutionResult { Secure, Inconclusive, Unverifiable }
  // SecureResolutionResponse keeps Result/Records/Provenance/Reason; add a guarded
  // deserialization path (custom JsonConverter or Enum.IsDefined check) so an out-of-range
  // integer/string cannot decode to Secure or Inconclusive by default.
  ```
- Rewrite `resolveSecurely` (`:242-248` success path) to `result: .secure`; loopback (`:223-229`), invalid-URL (`:232-238`), and catch (`:254-260`) paths to `.inconclusive`. **A0 introduces no `.unverifiable` producer yet** — that state is wired by A2/A3/A4 when a proven downgrade is detected. Windows `ResolveSecurelyAsync` mirrors: `:261-266`→`.Secure`, `:229-235`/`:253-259`/`:274-280`→`.Inconclusive`.

**Honesty / fail-closed rules enforced:** retires the boolean-where-tri-state defect (both-true / both-false impossible states can no longer be decoded — honesty spine rule 3). Fix 2 makes a hostile/stale serialized verdict fail closed to a decode error, never a silent `.secure`. Cancellation still propagates (never becomes a verdict). No verdict path changes meaning for existing callers (loopback + transport-failure still read inconclusive — rule 2).

**Pre-queued build/test:**
```
swift build --package-path macOS && swift test --package-path macOS --filter SecureResolution
dotnet build Windows/Syxx.sln && dotnet test Windows/Syxx.Tests/Syxx.Tests.csproj --filter FullyQualifiedName~ResolveSecurely
```
Carry forward E030's **mandatory tests** on both platforms: (happy) DoH success → `.secure`; loopback + transport failure → `.inconclusive`; (failure) a malformed/unknown serialized state fails to decode into `.secure`/`.inconclusive`; cancellation propagates rather than becoming a verdict.

**Real-data acceptance bar (E030's bar, carried forward):** run the production unified binary `swift build --package-path macOS --product syxx` then `./macOS/.build/debug/syxx nslook cloudflare.com A` against (1) a reachable DoH endpoint and (2) an intentionally unreachable endpoint (`--doh https://10.255.255.1/dns-query`). Honest output: reachable → verdict maps to `.secure` with real records; unreachable → `.inconclusive` with a reason, **never** an empty-but-secure answer. Windows: `dotnet run --project Windows/Syxx -- nslook cloudflare.com A` same two cases. Tests are the floor; this run is the bar.

**Parity:** both enums must carry all three members with identical semantics before any transport lands, or the platforms diverge on the downgrade case (the E014-class split the honesty contract §3.7 warns against).

---

## Step A1 — Transport-provenance model on `DNSResponse` (both platforms)

**Files (verified):** macOS `DNSResolver.swift:111-190` (`struct DNSResponse`: stored fields `112-131`, memberwise `init 133-159`, decode-tolerant `init(from:) 163-177`, `didFail 181-189`). Windows `DNSResolver.cs:88-120` (`class DNSResponse`, `DidFail 113-119`).

**Types / fields to add (additive + defaulted, so every current call site and every persisted `DNSResponse` still decodes — mirror the existing `authorityRecords`/`edns` decode-tolerance at macOS `:167-168`, Windows `:96-99`):**
```swift
public enum DNSTransport: String, Codable, Sendable {
    case udp, tcp, doh, dot, doq, doh3, unknown
}
// on DNSResponse:
public let requestedTransport: DNSTransport            // default .udp for legacy call sites
public let servedTransport: DNSTransport?              // proven from evidence; nil / .unknown when undeterminable
public let negotiatedProtocol: String?                 // observed token: "h3","h2","http/1.1","doq", TLS ver
public var wasDowngraded: Bool {                        // derived, not stored
    guard let served = servedTransport else { return false }
    return served != .unknown && served != requestedTransport
}
```
Windows mirror: `public enum DNSTransport { Udp, Tcp, Doh, Dot, Doq, Doh3, Unknown }`; `RequestedTransport`/`ServedTransport?`/`NegotiatedProtocol?` auto-properties defaulted; `public bool WasDowngraded => ServedTransport is { } s && s != DNSTransport.Unknown && s != RequestedTransport;`.

Add both to the decode-tolerant initializers (macOS `init(from:) :163-177` via `decodeIfPresent … ?? .udp` / `?? nil`; Windows defaulted auto-props already decode-tolerant).

**Wire existing transports to stamp provenance (no behavior change, honest labels only):** `queryUDPDetailed` (`:272`) → `requestedTransport = .udp`, on success `servedTransport = .udp`; when it TCP-falls-back on truncation (`:302-309`) → `servedTransport = .tcp` (both plaintext, the safe fallback the honesty contract §3.2 explicitly keeps). `queryDoHDetailed` (`:334`) → `requestedTransport = .doh`, `servedTransport = .doh` on success. Windows `QueryUDPDetailedAsync`/`QueryDoHDetailedAsync` (`:289`) identical.

**Honesty / fail-closed rules enforced (contract §3.1, §3.3):** the label reflects the transport that *actually carried* the answer, never what was requested. `servedTransport == .unknown` on any encrypted/DoH3 path forces the caller to treat the claim as inconclusive (feeds A0 `.inconclusive`). A proven downgrade to plaintext feeds A0 `.unverifiable`. No bare-bool transport flags anywhere.

**Pre-queued build/test:**
```
swift build --package-path macOS && swift test --package-path macOS --filter DNSResolver
dotnet build Windows/Syxx.sln && dotnet test Windows/Syxx.Tests/Syxx.Tests.csproj --filter FullyQualifiedName~DNSResolver
```
Add unit tests: a legacy-serialized `DNSResponse` (no transport fields) decodes with `servedTransport == nil`, `wasDowngraded == false`; a `.doh` requested / `.udp` served response has `wasDowngraded == true`.

**Real-data acceptance bar:** `syxx nslook cloudflare.com A --doh` (existing flag) now prints `served: doh` for the real Cloudflare answer; a plain `syxx nslook cloudflare.com A @1.1.1.1` prints `served: udp`. A truncated answer that falls back to TCP prints `served: tcp` with `wasDowngraded=false` (both plaintext). No fabricated transport label appears when the field is genuinely unknown — it prints `served: unknown`, not a guess.

**Parity:** enum members, defaults, and `wasDowngraded` derivation identical on both platforms; add a golden byte-parity assertion if `E011ByteParityTests` covers `DNSResponse` serialization.

---

## Step A2 — DoT client transport (PAID)

**Files (verified):** macOS clone `sendTCP` (`:1098-1150`) + `readTCPResponse` (`:1152-1182`) into `sendDoT`; new public `queryDoTDetailed`. Windows clone `SendTCPAsync` (`:955-977`) reusing `ReadExactAsync` (`:979-998`) into `SendDoTAsync`; new `QueryDoTDetailedAsync`. Pinning-posture pattern to mirror (NOT reuse): `CollectorTransportPinning` enum `LeakDetector.swift:10-17` (`enforced`/`notConfigured`/`failed`) and `CollectorPinning.decide` `:447-454`.

**Method signatures:**
```swift
// macOS
public static func queryDoTDetailed(name: String, type: UInt16, server: String,
    port: UInt16 = 853, sni: String, pins: Set<String> = []) async -> DNSResponse
private static func sendDoT(_ framed: Data, to server: String, port: UInt16,
    sni: String, pins: Set<String>) async throws -> (data: Data, pinning: TransportPinning, tlsVersion: String?)
```
```csharp
// Windows
public static Task<DNSResponse> QueryDoTDetailedAsync(string name, ushort type, string server,
    ushort port = 853, string sni = null, ISet<string> pins = null);
```
- macOS transport: `NWParameters(tls: tlsOpts, tcp: .init())` to `server:853`; `tlsOpts = NWProtocolTLS.Options()`; `sec_protocol_options_set_tls_server_name(sec, sni)`; ALPN via `sec_protocol_options_add_tls_application_protocol(sec, "dot")`; **fail-closed** `sec_protocol_options_set_verify_block` that runs system trust evaluation and, when `pins` is non-empty, additionally requires the leaf SPKI pin (reuse `CollectorPinning.spkiPin(forCertificateDER:)` `LeakDetector.swift:435` and `.decide` logic `:447-454`, but a **new** `sec_protocol` verify block — the D003 URLSession delegate does not apply to NWConnection, per contract note and program plan `:181`). Same 2-byte length-prefix framing as `sendTCP` (`:1100-1106`).
- Windows transport: `SslStream` over `TcpClient` to `:853`; `SslClientAuthenticationOptions { TargetHost = sni, ApplicationProtocols = [ new("dot") ], RemoteCertificateValidationCallback = <fail-closed> }`; **never** a permissive callback that returns `true` — validate `sslPolicyErrors == None` first, then optional pin check; reuse `ReadExactAsync` + length prefix (`:968-974`).

Add a transport-pinning posture field to the returned `DNSResponse` (or a sibling struct) `{ enforced / notConfigured / failed }` mirroring D003 — an encrypted transport whose identity is unverified must surface that posture.

**Gating (PAID):** callers gate before invoking DoT — `let lic = await LicenseManager.isFeatureUnlocked(); guard lic.unlocked else { /* return .inconclusive "requires subscription" */ }` (macOS `LicenseManager.swift:210`); Windows `var lic = await LicenseManager.IsFeatureUnlockedAsync(); if (!lic.Unlocked) …` (`LicenseManager.cs:483`). The gate is on the server-validated unlock tuple, **never a local bool** (D013). Gate lives at the call site (nslook flag / profile selection / probe), same pattern as `ProbePlan.cs:146-147` content-warp gate — not inside `sendDoT` (keep transport pure/testable).

**Honesty / fail-closed rules (contract §3.2, §3.4):** handshake/cert/SNI/timeout → `.transportError`/`.inconclusive`, never success. No permissive TLS callback on either stack. **No encrypted-to-plaintext fallback** — DoT must NOT copy the UDP→TCP fallback; if the TLS path fails it returns inconclusive, it does not retry over Do53. On success stamp `requestedTransport=.dot`, `servedTransport=.dot`, `negotiatedProtocol="TLS1.3"` (read from `sec_protocol_metadata` / `SslStream.NegotiatedCipherSuite`+`SslProtocol`). If ALPN negotiated something other than "dot" or the peer offered no TLS, stamp `servedTransport=.unknown` → inconclusive.

**Pre-queued build/test:**
```
swift build --package-path macOS && swift test --package-path macOS --filter DoT
dotnet build Windows/Syxx.sln && dotnet test Windows/Syxx.Tests/Syxx.Tests.csproj --filter FullyQualifiedName~DoT
# pre-flight the endpoint before the real run:
openssl s_client -connect 1.1.1.1:853 -servername cloudflare-dns.com -alpn dot </dev/null 2>/dev/null | head
```

**Real-data acceptance bar:** `queryDoTDetailed("cloudflare.com", A, server: "1.1.1.1", sni: "cloudflare-dns.com")` (exercised via a paid-unlocked `syxx nslook cloudflare.com A --dot @1.1.1.1`). Honest success: non-empty records, `servedTransport=.dot`, `negotiatedProtocol` shows TLS 1.3, pinning posture `notConfigured` (no pins) or `enforced` (pins supplied). **Failure case:** point at a blocked/closed :853 (e.g. `--dot @192.0.2.1`) → `.inconclusive`/`transportError`, **never** a clean answer and never a silent drop to plaintext. Second failure case: a locked (non-subscriber) run reports the paid-gate message and does not resolve.

**Parity:** identical ALPN token "dot", identical port default 853, identical fail-closed semantics, identical pinning-posture enum values on both platforms.

---

## Step A3 — DoQ client transport (PAID) — macOS viable per spike; Windows availability-gated

**Files (verified):** macOS new `sendDoQ` + `queryDoQDetailed` in `DNSResolver.swift` (new NWConnectionGroup path, `@available(macOS 15.0, *)`). Windows new `SendDoQAsync` + `QueryDoQDetailedAsync` using `System.Net.Quic`. Parser already accepts a fixed expected ID: macOS `parseResponsePacketDetailed(…, expectedTransactionID:)` `:646`, check `:653-656`; Windows `ParseResponsePacketDetailed(…, expectedTransactionId)` check `:459-462`. Builder to add a zero-ID variant next to macOS `makeQueryPacketWithID` `:381` / Windows `MakeQueryPacket(… out transactionId …)` `:343-361` (both currently randomize the ID at macOS `:387` / Windows `:348`).

**The RFC 9250 ID-0 trap (load-bearing).** RFC 9250 §4.2.1: *"the Message ID field of the DNS message SHOULD be set to 0. The stream mapping ... allows for unambiguous correlation."* Today the builder randomizes the ID and the parser rejects mismatches (macOS `:654`, Windows `:459`) — a compliant DoQ resolver echoes ID 0, so a randomized-ID query would make the honest answer look spoofed (`.responseIdMismatch`). Fix: a DoQ builder that forces ID 0, and parse with `expectedTransactionID: 0` (macOS) / `expectedTransactionId: 0` (Windows). Correlation is guaranteed by QUIC stream binding (one query per client-initiated bidirectional stream) + the existing `responseQuestionMatches` check (macOS `~:1020-1035`, Windows `~:426`), not by the DNS ID.

**Method signatures:**
```swift
// macOS — @available(macOS 15.0, *)
public static func queryDoQDetailed(name: String, type: UInt16, server: String,
    port: UInt16 = 853, sni: String, pins: Set<String> = []) async -> DNSResponse
```
macOS transport (spike-proven): `let q = NWProtocolQUIC.Options(alpn: ["doq"]); q.direction = .bidirectional; q.idleTimeout = 5000;` fail-closed verify block on `q.securityProtocolOptions` (system trust + optional SPKI pin, same `CollectorPinning.decide` logic, new `sec_protocol` verify block — NOT the URLSession pin); `NWConnectionGroup(with: NWMultiplexGroup(to: .hostPort(host, 853)), using: NWParameters(quic: q))`; `NWConnection(from: group)` for the client-initiated stream; send the length-prefixed DoQ message (RFC 9250 uses the 2-byte length prefix, same framing as DoT/TCP); `receiveMessage`; read `context.protocolMetadata(definition: NWProtocolQUIC.definition) as? NWProtocolQUIC.Metadata` and require `metadata.negotiatedALPN == "doq"` as the honest proof.

```csharp
// Windows — gated on QuicConnection.IsSupported
public static Task<DNSResponse> QueryDoQDetailedAsync(string name, ushort type, string server,
    ushort port = 853, string sni = null, ISet<string> pins = null);
```
Windows transport: `if (!QuicConnection.IsSupported) return DNSResponse{ Status=TransportError, ServedTransport=Unknown, ErrorMessage="DoQ unsupported on this host" }` → **`.untested`/inconclusive, never fail-silent** (requires msquic: Win11 22H2+ or app-local). `QuicClientConnectionOptions { DefaultStreamErrorCode=…, DefaultCloseErrorCode=…, RemoteEndPoint=new(IPAddress.Parse(server),853), ClientAuthenticationOptions = new(){ ApplicationProtocols=[ new SslApplicationProtocol("doq") ], TargetHost=sni, RemoteCertificateValidationCallback=<fail-closed> } }`; `await QuicConnection.ConnectAsync(...)`; `await conn.OpenOutboundStreamAsync(QuicStreamType.Bidirectional)`; write length-prefixed message; read; prove `stream`/`conn.NegotiatedApplicationProtocol == "doq"`.

**Gating (PAID):** identical to A2 (`isFeatureUnlocked` / `IsFeatureUnlockedAsync`, server-validated, at the call site).

**Honesty / fail-closed rules:** ID 0 built and expected (proves the fix). No permissive TLS callback. No fallback to plaintext or to DoT. Handshake/idle-timeout/ALPN-mismatch → `.inconclusive`. Where the QUIC stack is unavailable → `.untested`/inconclusive, never a fabricated result. On success stamp `requestedTransport=.doq`, `servedTransport=.doq`, `negotiatedProtocol="doq"` (from `negotiatedALPN`); if `negotiatedALPN != "doq"` stamp `.unknown` → inconclusive.

**Pre-queued build/test:**
```
swift build --package-path macOS && swift test --package-path macOS --filter DoQ
dotnet build Windows/Syxx.sln && dotnet test Windows/Syxx.Tests/Syxx.Tests.csproj --filter FullyQualifiedName~DoQ
# cross-check the live endpoint independently:
kdig +tls +quic @94.140.14.14 cloudflare.com A
```
Tests (floor): a DoQ builder emits header bytes `00 00` for the ID; the parser with `expectedTransactionID: 0` accepts an ID-0 answer and does NOT return `.responseIdMismatch`; on Windows where `QuicConnection.IsSupported == false` the path returns the untested/inconclusive result, not an exception.

**Real-data acceptance bar:** `queryDoQDetailed("cloudflare.com", A, server:"94.140.14.14", sni:"unfiltered.adguard-dns.com")` via paid-unlocked `syxx nslook cloudflare.com A --doq @94.140.14.14`. Honest success: non-empty records, `servedTransport=.doq`, `negotiatedProtocol="doq"`. **Failure case #1 (proves the ID-0 fix shipped):** a compliant resolver must NOT report `.responseIdMismatch`. **Failure case #2:** blocked :853/udp → inconclusive, never clean. **Windows-specific:** on a host without msquic, output is the untested/inconclusive state, not a crash or a false clean. Record the DoQ-macOS ledger status as `[code-complete]` until this run passes on hardware, then `[done]`; if the runtime handshake fails, `[untested]`.

**Parity:** same ALPN "doq", port 853, ID-0 contract, and untested-not-silent policy both platforms.

---

## Step A4 — DoH3 client transport (FREE)

**Files (verified):** macOS `dohSession` (`:204-210`) + `queryDoHDetailed` (`:334-371`) — add an h3 variant `queryDoH3Detailed`. Windows `_httpClient` (`:124-127`) + `QueryDoHDetailedAsync` (`:289-331`) — add `QueryDoH3DetailedAsync`. Reuse the **D003 URLSession/HttpClient pin here** (it DOES apply to DoH3, unlike DoT/DoQ): macOS `LeakDetector.makeCollectorSession(pins:)` `:101-108` + `CollectorPinningDelegate` `:512`.

**Method signatures:**
```swift
// macOS
public static func queryDoH3Detailed(name: String, type: UInt16, url: URL,
    pins: Set<String> = []) async -> DNSResponse
```
macOS: build the request as in `queryDoHDetailed` (`:345-350`) but set **`request.assumesHTTP3Capable = true`** (verified API — NOT on the config). Use a session with a `URLSessionTaskDelegate` capturing `urlSession(_:task:didFinishCollecting:)` → `metrics.transactionMetrics.last?.networkProtocolName`. **Prove h3:** label `servedTransport=.doh3` / `negotiatedProtocol="h3"` **only** when that token equals `"h3"`; if it is `"h2"`/`"http/1.1"` → `servedTransport=.doh` + `wasDowngraded=true` (requested `.doh3`, served `.doh`), which A0 maps to `.unverifiable` for a DoH3 claim.
```csharp
// Windows
public static Task<DNSResponse> QueryDoH3DetailedAsync(string name, ushort type, string dohUrl, ISet<string> pins = null);
```
Windows: `request.Version = HttpVersion.Version30; request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;` (forces h3 — the honest choice; RequestVersionOrLower would silently downgrade). **Prove h3:** `response.Version == HttpVersion.Version30` (i.e. `3.0`); otherwise stamp `.doh` + downgraded.

**Gating: FREE.** No license check — DoH3 is available to everyone.

**Honesty / fail-closed rules (contract §3.1, honesty crux):** "we asked for h3" ≠ "the answer came over h3." An on-path attacker can strip Alt-Svc to hold the client on h2; DoH3 is labeled ONLY when the metric/version proves h3, else it is honest DoH-over-h2 (still TLS, not h3) and marked downgraded. TLS failure → `.transportError`/inconclusive; reuse the fail-closed pin delegate; no permissive callback.

**Pre-queued build/test:**
```
swift build --package-path macOS && swift test --package-path macOS --filter DoH3
dotnet build Windows/Syxx.sln && dotnet test Windows/Syxx.Tests/Syxx.Tests.csproj --filter FullyQualifiedName~DoH3
# confirm the endpoint really offers h3:
curl -sI --http3-only https://cloudflare-dns.com/dns-query | head -1
```
Tests (floor): given a stubbed metrics token `"h2"`, the classifier returns `servedTransport=.doh` + `wasDowngraded=true`, NOT `.doh3`; given `"h3"`, returns `.doh3`.

**Real-data acceptance bar:** `queryDoH3Detailed("cloudflare.com", A, url: https://cloudflare-dns.com/dns-query)` via `syxx nslook cloudflare.com A --doh3`. Honest success: non-empty records AND `networkProtocolName == "h3"` (macOS) / `response.Version == 3.0` (Windows) → `served: doh3`. **Failure case:** an Alt-Svc-stripped / h2-only host (or a proxy that blocks UDP/443) → `served: doh`, `wasDowngraded=true`, and the DoH3 claim reads inconclusive — **never** a green "DoH3" label without the metric.

**Parity:** both platforms force-then-prove h3, both demote to `.doh`+downgraded when the proof fails, identical tokens.

---

## Step C — Resolver profiles: struct, persistence, selection, routing, Unbound template

**Files (verified — note the persistence gap):**
- macOS `SyxxConfig.swift:3-37` is an **enum with no persistence** (static consts + a `dimensionBaseURL` computed off `UserDefaults` key `"dimension_base_url"` at `:12-15`). Windows `SyxxConfig.cs:7-46` is a **static class with no persistence** (env-var-backed `DimensionBaseURL` at `:44-45`). **There is no existing profile/selection store on either platform.** The program plan's "selection persisted in `SyxxConfig`" therefore requires *adding* a persistence mechanism, not just a field. **[design decision — see Risks]**
- License gate: macOS `LicenseManager.isFeatureUnlocked()` `:210`; Windows `LicenseManager.IsFeatureUnlockedAsync()` `:483`.
- Routing consumers: the A transports (A2/A3/A4) + `EncryptedTransportProbe` (macOS `.swift:42-48`, Windows `.cs:21-30`) and its callers (`ProbePlan.swift:167-169`, `ProbePlan.cs:171-172`, `SyxxCLI.swift:412`, `Program.cs:453`).

**Types to add (both platforms, mirrored):**
```swift
public struct ResolverProfile: Codable, Sendable, Equatable {
    public let id: String                 // stable key, e.g. "cloudflare", "nextdns"
    public let name: String               // display name (user-visible → approval)
    public let transport: DNSTransport    // .doh3 (free) | .dot | .doq (paid)
    public let endpoint: String           // IP or hostname:port
    public let sni: String?               // TLS server name for DoT/DoQ/DoH3
    public let configId: String?          // NextDNS per-user config id; nil otherwise
    public var isPaid: Bool { transport == .dot || transport == .doq }  // DoH3 free
}
```
Built-in presets (endpoints are factual, not user copy): Cloudflare (`1.1.1.1` / `cloudflare-dns.com`), Quad9 (`9.9.9.9`), Google (`8.8.8.8` / `dns.google`), AdGuard (`94.140.14.14`), Mullvad (`dns.mullvad.net`), NextDNS (per-user config id), OpenDNS (`208.67.222.222`). Display **names** are user-visible → §7 approval.

**Persistence + selection (recommended shape — see Risks for the open decision):**
```swift
// macOS: persist the selected profile id via UserDefaults, same pattern as dimension_base_url
public static var selectedResolverProfileID: String? {
    get { UserDefaults.standard.string(forKey: "selected_resolver_profile") }
    set { UserDefaults.standard.set(newValue, forKey: "selected_resolver_profile") }
}
```
```csharp
// Windows: persist to %APPDATA%\Syxx\config.json (JsonSerializer), since SyxxConfig has no store today.
public static string? SelectedResolverProfileID { get; set; }   // loaded/saved from config.json
```
Custom profiles (NextDNS config id, arbitrary endpoint) serialize into the same store.

**Routing + gating:** a resolver `resolve(name, type, via profile)` dispatcher selects the A transport by `profile.transport`; if `profile.isPaid`, gate on `isFeatureUnlocked`/`IsFeatureUnlockedAsync` **before** dispatch and return `.inconclusive` with the paid-gate reason when locked. Free (DoH3) profiles route with no gate. The selected profile feeds `EncryptedTransportProbe.probe(endpoint:)` so Portal reports the encrypted path of the *chosen* resolver.

**Unbound forward-over-DoT template (consumed by sub-project E):**
```swift
public static func unboundForwardOverDoTConfig(for profile: ResolverProfile) -> String
```
emits (only valid for a DoT profile; guard `profile.transport == .dot`):
```
forward-zone:
    name: "."
    forward-tls-upstream: yes
    forward-addr: <endpoint>@853#<sni>
```
A DoH3/DoQ profile has no Unbound forward-over-DoT representation → return nil / throw, never emit a plaintext `forward-addr` (that would silently drop the encryption E is supposed to provide).

**Honesty / fail-closed rules:** a paid profile selected without an unlock must NOT silently fall back to a free transport or to Do53 — it returns inconclusive with the gate reason. Selecting a profile does not itself claim "engaged/encrypted"; only a live resolution through it, proven by A's `servedTransport`, does (contract §3.1). The UI frames profiles as *trusted third parties*, not Syxx-verified recursion (`DESIGN.md:325`). The generated Unbound template is inert config text — E owns proving the resolver actually came up (its own tri-state health).

**Pre-queued build/test:**
```
swift build --package-path macOS && swift test --package-path macOS --filter ResolverProfile
dotnet build Windows/Syxx.sln && dotnet test Windows/Syxx.Tests/Syxx.Tests.csproj --filter FullyQualifiedName~ResolverProfile
```
Tests (floor): a DoH3 profile has `isPaid==false`; a DoT/DoQ profile `isPaid==true`; the Unbound template for Cloudflare-DoT contains `forward-tls-upstream: yes` and `1.1.1.1@853#cloudflare-dns.com`; the template generator refuses a DoH3 profile.

**Real-data acceptance bar:** select the Cloudflare-DoH3 (free) profile → `syxx nslook cloudflare.com A --profile cloudflare` resolves and prints `served: doh3` (proven by A4). Select a DoT profile while **locked** → the probe reports the paid-gate message and does NOT resolve over Do53. Select AdGuard-DoQ while **unlocked** → resolves with `served: doq` against `94.140.14.14`. Generate the Unbound forward-over-DoT config for a DoT profile and confirm it is textually valid (spot-check with `unbound-checkconf` in sub-project E's scope). The bar is the real routed resolution reporting the *proven* transport, not a config write.

**Parity:** identical preset ids/transports, identical `isPaid` derivation, identical gate behavior, identical template output both platforms. (Persistence backend differs by OS — UserDefaults vs config.json — but the profile model and selection semantics are identical.)

---

## 7. Proposed user-visible strings (REQUIRE APPROVAL — log to `outputs-catalog.csv`)

None finalized in code. Proposed, plain-language (no jargon per house style), for sign-off:

| # | Where | Trigger | Proposed string |
|---|---|---|---|
| S1 | nslook / provenance line | answer served over the requested encrypted transport | `Answer carried over: DNS-over-TLS (verified).` / `…over: DNS-over-QUIC (verified).` / `…over: DNS-over-HTTP/3 (verified).` |
| S2 | nslook / provenance line | proven downgrade | `Warning: this answer arrived over plain DNS, not the encrypted transport you asked for. It cannot be trusted as a private reference.` |
| S3 | nslook / provenance line | transport unknown | `The transport that carried this answer could not be confirmed; treating this result as inconclusive.` |
| S4 | paid gate (DoT/DoQ) | locked user selects paid transport | `Encrypted DNS over TLS and QUIC is part of the subscription. This lookup was not sent over the encrypted path.` |
| S5 | DoH3 downgrade | asked h3, got h2 | `The server did not answer over HTTP/3; it fell back to HTTPS. Not labeled as HTTP/3.` |
| S6 | DoQ unavailable (Windows) | no msquic | `DNS-over-QUIC is not available on this system, so it could not be tested here.` |
| S7 | profile picker | display names | `Cloudflare`, `Quad9`, `Google`, `AdGuard`, `Mullvad`, `NextDNS`, `OpenDNS` (factual brand names; confirm framing copy: "trusted third-party resolvers, not verified by Syxx"). |
| S8 | nslook help | new flags | `--dot`, `--doq`, `--doh3`, `--profile <name>` usage lines added to `SyxxNSLook.swift:94-109` / Windows `NslookCommand.cs`. |

CLI report layout / GUI surfacing of these is **`syxx-gui-architect`'s** lane; wording sign-off precedes implementation.

---

## 8. Risks & open questions

1. **[Design decision — C persistence]** `SyxxConfig` has no persistence on either platform (verified: macOS enum `:3-37`, Windows static class `:7-46`). C requires adding one. Recommendation: macOS `UserDefaults` (reuses the `dimension_base_url`/`collector_spki_pins` pattern); Windows `%APPDATA%\Syxx\config.json` via `JsonSerializer` (no existing store to extend). Alternative: a new shared `SyxxSettings` type on both. This is a genuine fork — flag for the primary agent / product owner before C's persistence step, do not silently pick the JSON-file path on Windows without confirmation.
2. **[Runtime, not compile]** DoQ-macOS is compile-viable (spike) but NOT runtime-verified. The `[code-complete]`→`[done]` transition is gated on A3's real AdGuard run. If the `NWConnectionGroup`/`NWConnection(from:)` handshake fails at runtime (historically thin), fall back to `[untested]` and report inconclusive — do not force a design against an API that doesn't work at runtime.
3. **[Windows msquic floor]** DoQ + DoH3 on Windows need msquic (Win11 22H2+ / Server 2022+ or app-local). On older builds both must report untested/inconclusive, never fail-silent. Confirm the shipping Windows floor; if pre-22H2 is supported, app-local msquic packaging is added scope (out of A's core).
4. **[Transient-honesty ordering]** A2/A3/A4 landed *before* A1 would have no `servedTransport` to write and would emit encrypted claims with no downgrade visibility — a false-secure window. A1-before-transports is a hard constraint, stated in the dependency graph. A0-before-A1 likewise (the verdict type must exist before provenance feeds it).
5. **[Pinning scope]** D003's URLSession/HttpClient pin applies to DoH3 (A4) but NOT to NWConnection DoT/DoQ (A2/A3), which need a net-new `sec_protocol` verify block. The plan reuses `CollectorPinning.decide`/`.spkiPin` *logic* but a separate transport hook — do not wire the URLSession delegate into NWConnection (it will not fire).
6. **[Unverified externally-quoted]** RFC 9250 §4.2.1 (ID-0) is quoted from memory of the standard; the engineer should confirm the exact SHOULD-clause against the published RFC before shipping the ID-0 builder. The behavioral fix (build 0, expect 0, correlate by stream + question) is safe regardless.
7. **[Not a new delta]** A0 is E030; no new ledger entry is filed by this plan (per coordinator correction). If the `.unverifiable` extension is judged to exceed E030's vetted scope, `syxx-researcher` should note the extension on E030 — this planner does not edit the ledger.

---

## 9. Handoff

- **A0, A1, A2, A3, A4, C core** → `syxx-engineer` (core/transport lane). Start at **A0** (E030 extended), then A1, then A4 (lowest-risk transport, ships free), A2, A3, then C.
- **User-visible strings (§7), nslook flag help, CLI report layout, any GUI profile picker** → `syxx-gui-architect` after wording sign-off; log all to `outputs-catalog.csv`.
- **Sub-project B (multi-transport corroboration)** consumes A1's provenance fields → `syxx-neural-planner` (verdict/engine lane), out of this plan's scope but unblocked once A1 lands.
- **Sub-project E** consumes C's `unboundForwardOverDoTConfig` template.

Suggested first commit: A0 on both platforms with E030's mandatory tests + the two real nslook runs, recorded `[done]` only after those runs show honest secure/inconclusive output.
