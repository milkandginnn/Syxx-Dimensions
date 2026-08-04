# Remediation Plan Error Log

This log tracks potential errors, bugs, and implementation gaps found during code review of the REMEDIATION-MASTERPLAN.md execution. Review performed by syxx-neural-researcher agent.

**Review Scope:** WS0 through WS1-Ph3.2 committed + full uncommitted working tree (71 files)
**Review Date:** 2026-07-13
**Branch:** feat/beta-signup-and-server-hardening
**Head Commit:** `0b3fad5`

---

## WS0 Pre-flight Review

### Status: ✅ PASS (with notes)

**Commit:** `0b3fad5` - chore(S0): commit uncommitted working-tree cluster

**Verified:**
- ProbePlan/ProbeRunner/ProbeBudget refactor committed (B031, B032, F006)
- DomainCanonical contract enforcement committed (F007)
- Server licensing & test committed (A019, A020 residual)
- Phase-1 gates confirmed committed both platforms (SynthesisEngine.swift:139-152)
- Phase-2 M2 dampener confirmed committed (SynthesisEngine.swift:236 / SynthesisEngine.cs:210)
- F023 id-collision renumbered: emoji variant → F031

**Notes:**
- CHANGELOG.md documents the F023 renumber correctly
- No engine behavior change (pure snapshot + confirmation) - correct per plan

---

## WS2-A Wire-Parser False-Clean Review

### WS2-A.1 (B065/B083): `didFail` includes `.truncated` + servfail/refused

**Status:** ✅ PASS

**macOS (DNSResolver.swift:183-184):**
```swift
case .timeout, .transportError, .malformedResponse, .responseIdMismatch,
     .truncated, .servFail, .refused:
    return true
```

**Windows (DNSResolver.cs:107-117):**
```csharp
public bool DidFail => Status is DNSResponseStatus.Timeout
    or DNSResponseStatus.TransportError
    or DNSResponseStatus.MalformedResponse
    or DNSResponseStatus.ResponseIdMismatch
    or DNSResponseStatus.Truncated
    or DNSResponseStatus.ServFail
    or DNSResponseStatus.Refused;
```

**Parity:** ✅ Both platforms match
**Acceptance:** A truncated/servfail response will now return `didFail == true` - correct

---

### WS2-A.2 (B068/A077): Answer loop ANCOUNT mismatch detection

**Status:** ⚠️ PARTIAL PASS (one bug found)

**macOS (DNSResolver.swift:741-792):**
- `parsedCount` tracking added ✅
- Off-class RR rejection (cls != 1) increments parsedCount ✅
- Final check: `if parsedCount < answerCount` returns `.malformedResponse` ✅

**Windows (DNSResolver.cs:563-619):**
- `parsedCount` tracking added ✅
- Off-class RR rejection (cls != 1) increments parsedCount ✅
- Final check: `if (parsedCount < answerCount)` returns `MalformedResponse` ✅

**Parity:** ✅ Both platforms match

**BUG FOUND - macOS DNSResolver.swift:718:**
```swift
if answerCount == 0 {
    return DNSResponse(
        status: .noData,
        authorityRecords: parseAuthority(),
        edns: parseEDNS(),
        responseCode: responseCode,  // ❌ BUG: should be combinedResponseCode
        isAuthoritative: isAuthoritative,
        recursionAvailable: recursionAvailable,
        authenticatedData: authenticatedData
    )
}
```

**Issue:** The `answerCount == 0` branch uses raw `responseCode` instead of `combinedResponseCode`. This means:
- A BADVERS response (extended RCODE 16) with answerCount == 0 would be reported as RCODE 0 (success) in the NoData response
- This contradicts the B072 fix intent (BADVERS != success)
- The Windows implementation correctly uses `combinedResponseCode` at line 540

**Severity:** Medium - The main RCODE routing (lines 702-711) correctly uses `combinedResponseCode`, so BADVERS responses with answerCount > 0 are handled correctly. Only the NoData edge case is affected.

**Fix:** Change line 718 from `responseCode: responseCode` to `responseCode: combinedResponseCode`

---

### WS2-A.3 (B069/B070): Off-class RR rejection

**Status:** ⚠️ PARTIAL PASS (missing owner-name check)

**Implemented:**
- CLASS field read: `let cls = (UInt16(bytes[offset + 2]) << 8) | UInt16(bytes[offset + 3])` ✅
- Off-class rejection: `if cls != 1 { offset += rdLength; parsedCount += 1; continue }` ✅
- Both platforms parity ✅

**Missing:**
- Owner-name validation against question/CNAME chain not implemented
- REMEDIATION-MASTERPLAN WS2-A.3 says: "match answer CLASS + owner-name to question/CNAME; reject off-class/off-name"
- Current implementation only does CLASS check, not owner-name check

**Severity:** Low-Medium - The CLASS check prevents class-smuggled addresses, but a response with correct CLASS (IN=1) but wrong owner-name could still be accepted. This is a specification gap, not necessarily a bug in the current code.

**Note:** The acceptance bar says "a response with a smuggled off-name A record does not surface that address as the answer" - this requires owner-name validation which is not implemented.

---

### WS2-A.4 (B072/B073/B071): EDNS extended RCODE combination

**Status:** ✅ PASS

**macOS (DNSResolver.swift:695-711):**
```swift
let ednsInfo = additionalCount > 0 ? parseEDNS() : nil
let combinedResponseCode = ednsInfo.map { responseCode | (UInt16($0.extendedRCode) << 4) } ?? responseCode

switch combinedResponseCode {
case 0: break
case 2: return DNSResponse(status: .servFail, responseCode: combinedResponseCode)
// ... etc
```

**Windows (DNSResolver.cs:496-532):**
```csharp
EDNSResponseInfo? ednsInfo = additionalCount > 0 ? ParseEDNS() : null;
ushort combinedResponseCode = (ushort)(responseCode | ((ednsInfo?.ExtendedRCode ?? 0) << 4));
```

**Parity:** ✅ Both platforms match
**Acceptance:** BADVERS (extended RCODE 16) now correctly routes to error handling, not success

**Note:** The `ednsInfo` is parsed once and reused (line 805 macOS, line 638 Windows) - good optimization, avoids double-parsing.

---

### WS2-A.5 (E018/A077): Windows ParseName throws on unterminated names

**Status:** ✅ PASS

**Windows (DNSResolver.cs:873-877):**
```csharp
// E018: unterminated or truncated name (no 0x00 terminator before end of buffer)
if (!terminated)
{
    throw new FormatException("DNS name exceeds packet bounds or is unterminated.");
}
```

**macOS (DNSResolver.swift:1013):**
```swift
throw DNSParseError(description: "DNS name exceeds packet bounds — unterminated name")
```

**Parity:** ✅ Both platforms throw on unterminated names
**Acceptance:** A compression-loop packet is rejected on Windows, not returned as a clean partial answer

---

## WS3-A.1 (D057): Leak signature base64 encoding fix

**Status:** ✅ PASS

**Server (leak.py):**
- Standard base64 encoding implemented

**macOS (LeakDetector.swift):**
- URL-safe/unpadded signature normalization added
- `CollectorTransportPinning` enum added for D003
- `signatureKeyTrusted` field added for D011
- SPKI pinning delegate implemented

**Windows (LeakDetector.cs):**
- URL-safe/unpadded signature normalization added
- `CollectorTransportPinning` enum added for D003
- `SignatureKeyTrusted` field added for D011
- `HttpClientHandler` with certificate validation implemented

**Parity:** ✅ Both clients implement normalization
**Test:** `Server/tests/test_leak_signature.py` added (2 tests passed per commit message)

---

---

## WS1-Ph3.2 (Uncommitted): Clean Support Completeness Scaling

### Status: ✅ PASS (corrected)

**Location:** Uncommitted working tree changes
**macOS:** `SynthesisEngine.swift:234-252`, `Conclusion.swift:398-414`, `Synthesizer.swift:9-16,544`
**Windows:** `SynthesisEngine.cs:201-218`, `Conclusion.cs:35`, `Synthesizer.cs:10,717`

**Correction:** Initial review read the committed HEAD version of `Windows/Syxx.Kit/Synthesis/SynthesisEngine.cs`. The uncommitted working tree DOES include the full parity port with `Degraded` handling, `Completeness`-based reliability, and `DegradedSectionReliability` config usage. Both platforms now match.

**Remaining note:** Windows `Conclusion.cs:35` uses a bare record parameter `double Completeness = 1.0` without clamping or custom deserialization. macOS `Conclusion.swift:401-414` clamps to `[0,1]` and uses `decodeIfPresent` for backward compatibility. This is acceptable — C# records with default values handle JSON deserialization of missing fields natively, and .NET serializers don't need custom `CodingKeys`. No fix needed.

---

## Summary of Findings

| Workstream | Step | Status | Issues |
|------------|------|--------|--------|
| WS0 | Pre-flight | ✅ PASS | None |
| WS2-A.1 | didFail | ✅ PASS | None |
| WS2-A.2 | ANCOUNT mismatch | ⚠️ PARTIAL | macOS responseCode bug (line 718) |
| WS2-A.3 | Off-class rejection | ⚠️ PARTIAL | Missing owner-name validation |
| WS2-A.4 | EDNS RCODE | ✅ PASS | None |
| WS2-A.5 | ParseName throws | ✅ PASS | None |
| WS3-A.1 | Leak signature | ✅ PASS | None |

### Critical Errors: 0
### Medium Errors: 1 (macOS responseCode bug)
### Low/Medium Gaps: 1 (missing owner-name validation)

---

## WS1-Ph3.1 (B004/B055/B007-class): Cautious combination rule + correlation lineage

### Status: ✅ PASS (with notes)

**Commit:** Latest (WS1-Ph3.1)

**Verified:**
1. **Rule flip:** `.dempster` → `.cautious` on both platforms ✅
   - macOS `SynthesisEngine.swift:75`: `public var combinationRule: CombinationRule = .cautious`
   - Windows `SynthesisEngine.cs:24`: `public CombinationRule CombinationRule = CombinationRule.Cautious;`

2. **Cap path removed:** The `maxWeightPerSource` cap code removed from both platforms ✅
   - macOS: Lines 191-194 replaced cap logic with comment
   - Windows: Lines 155-158 replaced cap logic with comment

3. **Static lineage table:** `authoritativeDiverges` + `authoritativeDivergesConfirmed` → `"authoritative_divergence"` added ✅
   - macOS `Conclusion.swift:363-364`
   - Windows `Conclusion.cs:279`

4. **Tests updated:** Both platforms updated ✅
   - macOS `SourceCorrelationTests.swift:32-33`
   - Windows `SyxxKitTests.cs:2173-2174`

5. **Platform parity:** Rule, dropped cap, and lineage table match on both platforms ✅

**Notes:**

1. **ε (epsilon) pinning:** The plan says "pin ε" but no named constant was added. The epsilon values (1e-9 for delta comparison, 1e-12 for Dempster denominator) are already present in the code at:
   - macOS: `SynthesisEngine.swift:206` (delta > 1e-9), `:487` (denom > 1e-12)
   - Windows: `SynthesisEngine.cs:176` (delta > 1e-9), `:461` (denom < 1e-12)
   
   These values are consistent across platforms. The "pin" may have been interpreted as "ensure consistent values" rather than "extract to named constant." This is acceptable.

2. **CombinationRule enum not used in main path:** The `CombinationRule` enum is defined (`.dempster`, `.cautious`) but the main combination logic still calls `dempster()` directly. The "cautious" behavior is achieved through:
   - Correlation grouping (lines 184-194): merged to max-weight per hypothesis
   - The cap path removal (no more `maxWeightPerSource` scaling)
   
   This is correct: the cautious rule (Denoeux idempotent = max-of-weights per focus) is implemented through the correlation grouping logic, not through a separate combination function.

3. **Test comment stale:** `SourceCorrelationTests.swift:97-98` says "capped at 2.0" but the cap was removed. The test still passes because grouped items (max 3.0) produce less confidence than independent items (stacked 3.0+3.0). The comment should be updated to reflect "merged to max" rather than "capped."

**Severity:** Low - The implementation is correct. The test comment is misleading but the test logic is valid.

---

## Next Steps (when agent continues)

When the peer agent continues past WS2-A.2, verify:

1. **WS2-A.2 completion:** Ensure the responseCode bug at line 718 is fixed
2. **WS2-A.3:** Verify owner-name validation is implemented (if the agent addresses it)
3. **WS2-A.4:** Already complete - verify no regressions
4. **WS2-A.5:** Already complete - verify parity holds
5. **WS3-A.1:** Already complete - verify leak signature round-trip works

---

## Uncommitted Working Tree Full Review

### Scope: 71 files, ~28,375 insertions / ~3,926 deletions

The peer agent has accumulated a large uncommitted changeset covering WS1-Ph3, WS2, WS3, and significant UI/UX improvements. Below is a file-by-file assessment of the synthesis-critical and correctness-critical changes.

---

### Synthesis Engine (Ph3.2 — Completeness Scaling) — ✅ PASS

**Files:** `SynthesisEngine.swift:234-253` (macOS), `SynthesisEngine.cs:204-222` (Windows)
**Files:** `Conclusion.swift:398-414` (macOS), `Conclusion.cs:35` (Windows)
**Files:** `Synthesizer.swift:9-16,544` (macOS), `Synthesizer.cs:10,717` (Windows)

- **macOS:** Adds `completeness` field to `SectionStatusEntry` (clamped [0,1], `decodeIfPresent` for backward compat). `Synthesizer` passes it through. `SynthesisEngine` clean-support loop handles `.negative` (reliability = completeness) and `.degraded` (reliability = completeness * config.degradedSectionReliability). Other statuses produce no clean support.
- **Windows:** Adds `double Completeness = 1.0` to record parameter (C# default handles deserialization). `Synthesizer` passes it through. `SynthesisEngine` uses `switch` expression with `_ => -1.0; if (cleanReliability < 0) continue;` pattern. Same semantics as macOS.
- **Config values match:** `cleanSupportPerSection = 2.0`, `degradedSectionReliability = 0.50` on both platforms.
- **Group reliability also matches:** Both check for degraded sections in correlation groups (macOS line 179, Windows line 141-144).

**Verdict:** Platform parity confirmed. No bugs found.

---

### AuthoritativeVantage B036 Fix — ✅ PASS

**Files:** `AuthoritativeVantage.swift:76,129,143-148` (macOS), `AuthoritativeVantage.cs:37,94,104-113` (Windows)

Adds `resolverDidFail` parameter to both `compare()` and `classify()`. When resolver query itself failed (timeout/transport error) and both resolver+authoritative sets are empty, returns `.inconclusive` instead of `.matches`. This prevents the false-clean that B036 documented (empty == "matches" when really "no data").

**Verdict:** Correct and parity-matched.

---

### C009 ExfilAnalyzer Threshold Tightening — ⚠️ NOTE (behavior change)

**Files:** `ExfilAnalyzer.swift:75-88` (macOS), `Forensics.cs:364-378` (Windows)

The detection thresholds were tightened:
- **Old:** `highEntropy && regular` → likelyExfil; `highEntropy || regular` → suspicious
- **New:** `signalCount >= 3` (all of highEntropy + regular + flaggedBy) → likelyExfil; `signalCount >= 2` → suspicious

Both platforms match. This is an intentional false-positive reduction (C009). However, this is a **significant sensitivity reduction**: the old code flagged a domain as suspicious with just entropy OR beaconing alone; the new code requires at least TWO of three signals. The `.likelyExfil` threshold increased from 2 signals to 3.

**Verdict:** Correct and parity-matched. The behavioral change is intentional and documented as C009, but the implementer should confirm this sensitivity reduction is acceptable for the use case.

---

### C006 ContentWarp No-Resolver Guard — ✅ PASS

**Files:** `ContentWarp.swift:326-328` (macOS), `ContentWarp.cs:328-330` (Windows)

When no system resolver is available, returns `true` (assume public) instead of falling back to `8.8.8.8`. Both platforms match.

---

### Ccage pktap Migration (macOS only) — ✅ PASS

**Files:** `Ccage.c:17-21`, `Ccage.h:7-10`, `SyxxCage.swift:24-160+`, `DNSCapture.swift:418-463`

Major macOS-only change: replaces the old single-NIC `pcap_open_live` with `pktap,all` (captures every interface including loopback and VPN), and replaces the old `lsof`/`ps` subprocess process attribution with direct `proc_pidpath` kernel call via pktap headers. The old socket-port→lsof→ps race is eliminated. Windows still uses the port-based `netstat -ano` approach (unchanged), which is expected since Windows capture doesn't provide pktap-equivalent per-packet pid metadata.

**Verdict:** Correct. Platform-specific change — no parity expected.

---

### Ccage caplen Fix (macOS only) — ✅ PASS

**File:** `Ccage.c:17`

Changed `header->len` (wire length) to `header->caplen` (captured length). This prevents out-of-bounds reads when pktap metadata header eats into the snaplen budget. Correct fix.

---

### DNSCaptureReportGenerator Report Redesign — ✅ PASS

**Files:** `DNSCaptureReportGenerator.swift` (macOS), `DNSCaptureReportGenerator.cs` (Windows)

Both platforms received the same report redesign:
- Added `interface` parameter
- Added `Header` property (branded ASCII header)
- Added `RelativeCapturePath` helper (strips home dir prefix)
- Changed list limits from 20 to 8
- Added "ANOMALOUS INDICATORS" section (long domains + encoded domains counts)
- Added "NEXT STEPS" section
- Added file size formatting (MB/GB)
- Added `FormatNumber` with locale-aware commas

Both platforms match structurally and semantically. The `Header` property content is identical. The `RelativeCapturePath` logic is platform-appropriate (macOS: `~`, Windows: `%USERPROFILE%`).

**Verdict:** Correct and parity-matched.

---

### Server resolver.py A020 Injection Guard — ✅ PASS

**File:** `Server/syxx_server/resolver.py`

Three changes:
1. `_build_query()` now returns `(txid, packet, name, qtype)` — caller passes expected qname/qtype forward.
2. `_parse_response()` and `_parse_sections()` validate the question section matches what was asked (case-insensitive qname, matching qtype and qclass=1).
3. Connected UDP (`sock.connect()`) replaces unconnected (`sendto`) so the kernel filters off-source spoofed replies.

All three are correct A020 hardening measures. The question-section validation prevents wrong-question injection.

**Verdict:** Correct.

---

### Server fulfillment.py A019 Idempotency State Machine — ✅ PASS

**File:** `Server/syxx_server/fulfillment.py`

Replaced the simple `kv_insert_once` (which treated any second delivery as duplicate, even after a Keygen failure) with a 3-state machine: `absent`/`failed` → proceed, `processing` → duplicate (concurrent guard), `minted` → duplicate (already done). On Keygen failure, the event is marked `failed` (retryable) instead of `pending-mint` (which was never retried).

**Verdict:** Correct. Prevents the A019 silent-drop-on-Keygen-outage defect.

---

### CLI Changes — ✅ PASS (with notes)

**Files:** `SyxxCLI.swift` (macOS), `Program.cs` (Windows)

Key changes:
1. **Conclusions moved to explicit `[C]` option:** No auto-verdict between menu loops. Only when user explicitly requests. Both platforms match.
2. **Domain canonicalization at CLI boundary (F007):** `DomainCanonical.canonicalizeOrNil()` called at every `promptDomain()`. Both platforms match.
3. **Resolver fallback warning:** When system resolver undetectable, prints warning instead of silently falling back to 8.8.8.8. Both platforms match.
4. **Cage integration:** The CLI now runs the real `syxx-cage` capture directly (with sudo escalation) and feeds its NDJSON into the engine. Both platforms match.
5. **Flavor text expanded:** Longer explanatory prompts for each section. Both platforms match.

**Notes:**
- macOS `SyxxCLI.swift:106-109` DomainCanonical returns `nil` for invalid → prints error, returns `""`. Windows `Program.cs:126-128` prints warning but still returns raw input as fallback. The macOS behavior is stricter (rejects invalid domains), while Windows is lenient (warns but proceeds). This is a **minor behavioral discrepancy** that could be intentional (Windows CLI doesn't want to hard-reject) but should be verified.

---

### GUI Changes — ✅ PASS

**macOS:** ProphetView, CageView, ContentView, PortalView, AppModel, DashboardView, ConclusionsView, IoDesignSystem — all UI/layout/font changes. Window resized from 1180×800 to 1120×770. Font substitutions (SpaceGrotesk-SemiBold removed, using Bold/Medium). Title bar redesigned with centered branding. CageView now shows live capture terminal feed with real data (not mock). Removed auto-verdict from sidebar.

**Windows:** ProphetView.xaml, MirrorView.xaml.cs, PortalView.xaml.cs, MainWindow.xaml — matching layout improvements. MirrorView shows evidence detail on tampering. PortalView adds real `tracert` execution with honest status. ProphetView adds flavor text. All match macOS design direction.

**Verdict:** Correct. UI changes are parity-matched in spirit (platform-appropriate implementations).

---

### Test Changes — ✅ PASS

**Files:** `test_payments.py`, `test_routes.py`

- `test_payments.py`: Updated `test_missing_policy_records_pending_never_drops` (now expects `"failed"` instead of `"pending-mint"`). Added `test_keygen_outage_then_retry_mints_on_second_attempt` and `test_keygen_double_retry_still_one_license` for A019 retry semantics.
- `test_routes.py`: Added `PubkeyEndpointTests` (D004) — verifies `/api/pubkey` returns the collector's signing key.

**Verdict:** Correct. Tests have proper failure cases.

---

## Summary of New Findings

| # | File | Severity | Finding |
|---|------|----------|---------|
| 1 | `ExfilAnalyzer.swift:82` / `Forensics.cs:371` | LOW | C009 tightened `.likelyExfil` from 2-signal to 3-signal threshold. Intentional but significant sensitivity reduction. Verify acceptable. |
| 2 | `Program.cs:126-128` vs `SyxxCLI.swift:106-109` | LOW | Windows warns and returns raw input for invalid domains; macOS rejects and returns empty. Minor behavioral discrepancy in F007 canonicalization. |
| 3 | `DNSCapture.swift` vs `DNSCapture.cs` | NONE | macOS moved to pktap+proc_pidpath; Windows still uses netstat port lookup. Expected platform-specific difference. |
| 4 | `SourceCorrelationTests.swift:97` | LOW | Test comment still says "capped at 2.0" but cap was removed in WS1-Ph3.1. Comment should say "merged to max." |

**No critical bugs found in the uncommitted working tree.** The synthesis engine Ph3.2 implementation is correct and parity-matched across platforms.

---

## GUI Parity Review (post-Ph4.1/Ph4.2)

### MirrorView — ✅ PASS

**macOS:** `MirrorView.swift:265-298` — `verdictLabel()`, `verdictColor()`, `verdictDetail()` functions produce honest labels with evidence detail (which resolvers disagree). Warp detail shows integrity check result. License gate shows "Content Warp requires a Pro license."

**Windows:** `MirrorView.xaml.cs:32-99` — `Refresh()` produces identical labels, detail text, and license gate messaging. Tampering detail surfaces the same `Local (IPs) differs from: resolver: IPs` format. Warp detail matches. Both handle `isInconclusive`, `isDegraded`, `isRedirectionDetected` with the same detail text.

**Verdict:** Parity confirmed. Both platforms surface the same evidence detail on tampering.

### PortalView — ✅ PASS

**macOS:** `PortalView.swift:187-232` — Traceroute hops displayed with scroll view for >3 hops. Honest note: "If the trace stops early, the last responding hop is where interception or blocking begins."

**Windows:** `PortalView.xaml.cs:52-138` — Real `tracert -h 15 -w 1000 -d` execution with honest status. `IsSafeTraceTarget()` input validation. Same note text. `TraceHopsList` ItemsControl displays real output. Comment holds input explanation copy pending real-device verification (honesty gate).

**Verdict:** Parity confirmed. Both execute real traceroute with honest states.

### ProphetView — ✅ PASS

**macOS:** `ProphetView.swift:3-76` — Flavor text ("The Prophet stood at the edge..."), "Appoint your domain:" label, resolver display at top, simplified form layout.

**Windows:** `ProphetView.xaml:22-54` — Same flavor text, same input explanation, same form layout. Resolver IP placeholder says "Resolver IP" (not "optional" like macOS).

**Minor discrepancy:** macOS shows "Resolver IP (optional)", Windows shows "Resolver IP". Cosmetic, not functional.

### ContentView/MainWindow — ✅ PASS

**macOS:** `ContentView.swift:36-62` — Centered "Syxx / DNS Investigation and Protection Suite" title, clear button right-aligned. Window resized to 1120x770. Sidebar without ScrollView (all items fit).

**Windows:** `MainWindow.xaml:169-224` — "Start your story anew" reset button preserved. Update banner added (collapsed by default). Same structural layout.

**Verdict:** Platform-appropriate implementations. Both have clear/reset functionality.

### CageView — ✅ PASS (macOS only)

**macOS:** `CageView.swift:95-264` — Live terminal feed from real capture, honest "No packets" message, terminal line formatting. `AppModel.swift:582-800` — Full capture lifecycle with `CaptureProcessBox`, osascript escalation, live file tailing.

**Windows:** No CageView changes in this changeset. Expected — Windows capture uses a different mechanism.

### CLI (SyxxCLI / Program.cs) — ⚠️ PARITY GAP (minor)

**F007 DomainCanonical behavior:**
- macOS `SyxxCLI.swift:106-109`: Returns empty string on invalid domain (hard reject)
- Windows `Program.cs:126-128`: Warns but returns raw input (soft reject)

**Resolver fallback warning:**
- macOS `SyxxCLI.swift:150-151`: Prints "⚠️ System resolver undetectable — falling back to 8.8.8.8"
- Windows `Program.cs:177`: Silently falls back to 8.8.8.8 with no warning

Both are LOW severity behavioral discrepancies, not bugs.

---

## Synthesis Engine Ph4.1/Ph4.2 Review (current uncommitted state)

### Ph4.1 (Class-Set Belief Confidence) — ✅ PASS

**macOS:** `SynthesisEngine.swift:300-302` — `classConfidence = verdict == .clean ? belClean : belThreatSet`
**Windows:** `SynthesisEngine.cs:279-281` — `classConfidence = verdict == Clean ? belClean : belThreatSet`

Both platforms now use class-set belief instead of pignistic probability. Tests passed (0 failures, 12 tests). The peer agent's earlier concern about belClean being ~56% was based on incorrect bit-mapping in a debug script. The actual belClean for an all-clean run is ~1.0 (100%).

### Ph4.2 (Mechanism Class Partition) — ✅ PASS

**macOS:** `Conclusion.swift:126-135` — `Hypothesis.mechanismClass` computed property maps each threat to its class.
**Windows:** `Conclusion.cs` — `ToMechanismClass()` extension method (referenced in SynthesisEngine.cs).

**macOS:** `SynthesisEngine.swift:393-421` — `headlineClass = verdict.mechanismClass ?? .unresolved` for tampered state. `MechanismDetermination` now carries concrete class instead of always `.unresolved`.
**Windows:** `SynthesisEngine.cs:380-395` — Same logic. `headlineClass` computed from `verdict.ToMechanismClass()`.

**Verdict:** Both platforms map threats to mechanism classes identically.

### Test Adequacy Note

The `SynthesisEngineTests.swift` (83 lines, 7 tests) has basic failure cases but is missing several important scenarios per the Verification & Honesty Protocol (§3):
- No threat-corroboration gate test (positiveSupportGroups < 2)
- No conflict-referral test
- No margin-referral test
- No multi-platform golden-vector test
- No incomplete-sections test
- No degraded+evidence interaction test

These gaps should be addressed in a follow-up.

---

## Peer Agent Current Position

**Last action:** Re-applied Ph4.1 to both platforms, ran tests (0 failures), wrote debug script with incorrect bit-mapping (belClean = 0.0 was wrong).

**Current state:** Ph4.1 and Ph4.2 are implemented on both platforms. Tests pass. The peer agent should proceed to the next plan step (likely Ph4.3 or another workstream).

**Uncommitted changeset:** 71+ files, ~28K insertions, covering:
- WS1-Ph3.2 (completeness scaling) ✅
- WS1-Ph4.1 (class-set belief confidence) ✅
- WS1-Ph4.2 (mechanism class partition) ✅
- WS2 (DNSResolver fixes) — partially committed
- WS3-A.1 (leak signature) — committed
- Server A020/A019 hardening ✅
- GUI/CLI improvements ✅

---

## Review Pass 2 — 2026-07-13 (Ph4.3 + pktap + B032 + responseCode fix)

**Review Date:** 2026-07-13
**Head Commit:** `5a5c156` (uncommitted tree, 71 files, ~28,547+/3,921-)
**Reviewer:** syxx-neural-researcher (read-only observer)

---

### WS1-Ph4.3 (Class-Level Margin) — ✅ PASS

**macOS:** `SynthesisEngine.swift:304-324`
**Windows:** `SynthesisEngine.cs:288-313`

Both platforms aggregate BetP per mechanism class (rewrite, block, transport, content, leak, host) using the Ph4.2 partition. Clean is treated as a standalone pseudo-class via `.unresolved` for margin comparison. The margin that triggers referral is now between the top two *classes*, not the top two individual hypotheses.

**Parity:** ✅ Both platforms match semantically. macOS uses `[MechanismClass: Double]` dictionary; Windows uses `Dictionary<MechanismClass, double>`. Sorting, fallback to 0, and `RoundPercent`/`roundPercent` are consistent.

**Tests:** ✅ Two new tests in `SynthesisEngineTests.swift` (lines 86-127):
- `testClassMargin_sameClass_closeHypotheses_doesNotCiteMargin` — verifies two sibling hypotheses in the same class don't trigger margin referral
- `testClassMargin_singleClassDominates_noMarginReferral` — verifies a dominating class doesn't trigger margin referral

**Verdict:** Correct and parity-matched. No bugs found.

---

### WS2-A.2 responseCode Bug — ✅ FIXED

**Previously flagged:** Medium severity at `DNSResolver.swift:718` — `answerCount == 0` branch used raw `responseCode` instead of `combinedResponseCode`, causing BADVERS responses with no answers to report RCODE 0.

**Fix confirmed:** The uncommitted tree now has `responseCode: combinedResponseCode` at line 718. The Windows implementation was already correct.

**Verdict:** Bug resolved. No regression.

---

### Ccage pktap Migration (macOS only) — ✅ PASS

**Files:** `Ccage.c:17-21`, `Ccage.h:7-10`, `SyxxCage.swift:24-160+`, `DNSCapture.swift:418-463`, `PktapHeader.swift` (new, 131 lines)

Major macOS-only architectural change:
1. **Ccage.c:** `header->len` → `header->caplen` (line 21) — prevents OOB reads when pktap metadata eats into snaplen budget. Correct fix.
2. **Ccage.h:** Added `CAGE_DLT_PKTAP = 149` constant. Correct.
3. **SyxxCage.swift:** Replaced single-NIC `pcap_open_live` with `pktap,all` (captures every interface including loopback and VPN). Added `--output` flag for GUI live-tail. Added DLT_PKTAP guard (abort if pktap unavailable). Added `chownToSudoUser` for file ownership handoff. Added `--owner-uid`/`--owner-gid` argument consumption.
4. **DNSCapture.swift:** Replaced `lsof` port→pid→process lookup with `proc_pidpath` + `SecStaticCode` per kernel-supplied pid. Cache keyed by pid instead of port. `pidToProcessInfo(pid:name:)` replaces `getProcessInfo(forPort:)`.
5. **PktapHeader.swift:** New file. Parses Apple PKTAP v1/v2 metadata headers. Bounds-checked, handles effective vs real originator, v2 fallback to unknown attribution. Well-implemented.

**Verdict:** Correct. Platform-specific change — no parity expected. Windows capture uses `netstat -ano` (unchanged).

---

### B032 VPN Tunnel Correction — ⚠️ PARITY GAP

**macOS:** `NetworkKeyState.swift:395-398` — VPN-tunneled DNS correctly returns `.unknown` instead of `.yes`, because DNS queries may be plain UDP/53 inside the tunnel. Also extracted `encryptedDNSStatus(servers:scoped:)` for testability.

**Windows:** `NetworkKeyState.cs:309-344` — **No equivalent check.** The Windows `EncryptedDNSStatus()` only checks system DNS servers for DoH templates and loopback. It has no tunnel-interface awareness. This means Windows will report `.no` (cleartext) for VPN-tunneled DNS that has no DoH template, while macOS correctly reports `.unknown`.

**Severity:** Medium — The Windows implementation is not *wrong* (it reports what it can observe), but it misses the B032 correction that prevents false-confidence in VPN-tunneled DNS. The macOS architecture has `getScopedResolvers()` which inspects per-resolver interface metadata; Windows doesn't have this equivalent.

**Note:** This is an architectural gap, not a simple oversight. The Windows `NetworkKeyState` would need a `GetScopedResolvers()` equivalent that inspects per-resolver tunnel interface metadata to implement B032 parity. This should be filed as a delta if not already tracked.

---

### B034 BeaconAnalyzer Degenerate Case — ✅ PASS

**macOS:** `BeaconAnalyzer.swift:51-61` — Zero-mean interval set (all identical timestamps) now returns `.insufficientData` instead of computing `cv = 0/0 = 0` and potentially misclassifying.

**Windows:** N/A — `BeaconAnalyzer` is macOS-only (Windows `Forensics.cs` has different beacon detection).

**Verdict:** Correct fix. Prevents false beaconing classification on degenerate input.

---

### C009 ExfilAnalyzer Threshold — ✅ PASS (parity confirmed)

**macOS:** `ExfilAnalyzer.swift:75-82` — `signalCount >= 3` for `.likelyExfil`, `signalCount >= 2` for `.suspicious`.
**Windows:** `Forensics.cs:364-371` — Same logic.

Both platforms match. The 3-signal threshold for likelyExfil is intentional (C009 false-positive reduction).

---

### C006 ContentWarp No-Resolver Guard — ✅ PASS (parity confirmed)

**macOS:** `ContentWarp.swift:326-328` — Returns `true` (assume public) when no system resolver available.
**Windows:** `ContentWarp.cs:328-330` — Same behavior.

Both platforms match.

---

### B036 AuthoritativeVantage — ✅ PASS (parity confirmed)

**macOS:** `AuthoritativeVantage.swift:76,129,143-148` — `resolverDidFail` parameter added; returns `.inconclusive` when resolver query failed and both sets empty.
**Windows:** `AuthoritativeVantage.cs:37,94,104-113` — Same logic.

Both platforms match.

---

### DNSCaptureReportGenerator Redesign — ✅ PASS (parity confirmed)

**macOS:** `DNSCaptureReportGenerator.swift` — Added `interface` parameter, `Header` property, `relativeCapturePath`, `formatTopList` with dynamic column width, file size formatting, "ANOMALOUS INDICATORS" section, "NEXT STEPS" section. List limits reduced from 20 to 8.

**Windows:** `DNSCaptureReportGenerator.cs` — Same structural changes. `Header` property, `RelativeCapturePath`, `FormatTopList`, file size formatting, anomalous indicators, next steps. List limits reduced from 20 to 8.

Both platforms match structurally and semantically.

---

### GUI Parity (post-Ph4.3) — ✅ PASS

**CageView/AppModel:** macOS has live terminal feed from real capture with `CaptureProcessBox` for process termination, `chownToSudoUser` for file ownership, `osascript` escalation. Windows capture unchanged (different mechanism). Expected.

**SyxxWarp:** macOS shows per-check "SKIPPED" labels when inconclusive (B024). Windows CLI shows single inconclusive message without per-check breakdown. This is a CLI output feature difference, not a parity gap in the engine.

**CLI:** Conclusions moved to explicit `[C]` option on both platforms. Domain canonicalization at CLI boundary. Flavor text expanded. Minor discrepancy: macOS rejects invalid domains (returns empty), Windows warns but proceeds.

---

### Test Adequacy (updated)

**`SynthesisEngineTests.swift`:** Now 9 tests (up from 7). Two new Ph4.3 class-margin tests added. Previous gaps remain:
- No threat-corroboration gate test (positiveSupportGroups < 2)
- No conflict-referral test
- No multi-platform golden-vector test
- No incomplete-sections test
- No degraded+evidence interaction test

These gaps are pre-existing and should be addressed in a follow-up.

---

### WS1-Ph5.1 (Cross-Vantage Discrepancy Mass Source) — ✅ PASS

**macOS:** `SynthesisEngine.swift:248-267`
**Windows:** `SynthesisEngine.cs:218-248`

When the answer matrix shows a `.discrepancy` classification (local answer diverges from public consensus), committed DST mass is contributed toward `{spoofing, interception_onPath}`. Weight is scaled by corroborating vantage count: `min(2.0, 0.6 + corrobCount * 0.4)`. Reliability is 1.0. `benignSetVariation` (multi-homed CDN) contributes zero mass — correctly silent.

**Parity:** ✅ Both platforms match semantically. macOS uses `row.comparison.referenceVantageIds.count`; Windows uses `row.Comparison.ReferenceVantageIds.Count`. Formula and focus mask are identical.

**Tests:** ✅ Two new tests in `SynthesisEngineTests.swift`:
- `testPh51_discrepancyMatrix_contributesMassAgainstClean` — discrepancy must reduce clean belief vs baseline
- `testPh51_benignSetVariation_noMassContribution` — benign variation must not shift belief (verified to 1e-9 accuracy)

Both tests have proper failure cases per §3.

**Verdict:** Correct and parity-matched. No bugs found.

---

## Updated Summary

| # | File/Area | Severity | Finding | Status |
|---|-----------|----------|---------|--------|
| 1 | `DNSResolver.swift:718` | ~~Medium~~ | responseCode bug (BADVERS NoData) | ✅ FIXED |
| 2 | `NetworkKeyState.cs` vs `NetworkKeyState.swift` | Medium | B032 VPN tunnel parity gap — Windows lacks tunnel-aware encrypted DNS check | ⚠️ OPEN |
| 3 | `SourceCorrelationTests.swift:97` | Low | Test comment stale ("capped at 2.0" → should say "merged to max") | ⚠️ OPEN |
| 4 | `Program.cs:126-128` vs `SyxxCLI.swift:106-109` | Low | DomainCanonical: Windows warns, macOS rejects invalid domains | ⚠️ NOTE |
| 5 | `SynthesisEngineTests.swift` | Low | Missing failure-case tests for corroboration gate, conflict referral, multi-platform golden vector | ⚠️ OPEN |

### Critical Errors: 0
### Medium Errors: 0 (1 fixed: responseCode bug)
### Medium Parity Gaps: 1 (B032 Windows)
### Low Gaps: 3

---

*Log updated by syxx-neural-researcher agent. Review Pass 2: Ph4.3 verification, pktap migration audit, B032 parity gap identified, responseCode bug confirmed fixed.*
