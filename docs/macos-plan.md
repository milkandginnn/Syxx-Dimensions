# macOS-Side MUST FIX Deltas: Implementation Plan

**Pipeline:** Research → Plan → Build → Verify
**Scope:** 13 open B/C/D/E domain macOS deltas
**Generated:** 2026-07-27

---

## Stale Deltas (DO NOT FIX)

| Delta | Reason |
|-------|--------|
| B130-B134 | Dempster-Shafer engine retired (2026-07-21), replaced by MatrixDeterminativeEngine |
| C050 | WeightedMass/fusionRule not in codebase |
| C052 | leakLabel doesn't exist in current DashboardView |
| B135 | Already fixed (B157 guard at SignedIncidentReport.swift:63) |
| C051 | Windows-only (three-bool pattern in VantageComparer.cs) |

---

## Cluster 1: Signature Trust (B066, B067, B081, C071)

**Priority:** Critical — false secure verdicts on untrusted signatures
**Files:** GluttonView.swift, WitnessSigner.swift

### B066: GluttonView Color Ignores signatureKeyTrusted

**Location:** GluttonView.swift:197
**Current:** `color: leak.verdict == .notDetected ? .green : .red`
**Fix:** Check both `signatureValid` AND `signatureKeyTrusted`:

```swift
// B066: color must reflect BOTH signature validity AND key trust
let sigColor: Color
if leak.verdict == .notDetected && leak.signatureKeyTrusted == true {
    sigColor = .green
} else if leak.verdict == .notDetected && leak.signatureKeyTrusted == false {
    sigColor = .orange  // valid signature but untrusted key
} else {
    sigColor = .red
}
```

### B067: Green When Pinning Not Configured

**Location:** GluttonView.swift:197
**Current:** Shows green when leakPinningConfigured == false and leakVerdict == .notDetected
**Fix:** Show .secondary when pinning is off:

```swift
// B067: pinning not configured = inconclusive, not clean
if !leakPinningConfigured {
    sigColor = .secondary
}
```

### B081: verifySnapshot() Skips Trust Validation

**Location:** WitnessSigner.swift:117, 148
**Current:** `public static func verifySnapshot(_ snap: WitnessSnapshot, trustedPublicKeys: [String]? = nil) throws -> Bool`
Line 148: `guard let expectedB64 = trustedPublicKeys?.first else { return true }`
**Fix:** Return false when no trusted keys provided:

```swift
// B081: must not return true when no trusted keys are provided
guard let expectedB64 = trustedPublicKeys?.first else {
    return false  // was: return true
}
```

### C071: signatureKeyTrusted Not Surfaced in UI

**Location:** GluttonView.swift (no UI element for this field)
**Fix:** Add a label showing trust status:

```swift
// C071: surface signatureKeyTrusted to the user
if !leak.signatureKeyTrusted {
    Text("Signature key not trusted")
        .font(.caption)
        .foregroundColor(.orange)
}
```

---

## Cluster 2: Comparison Tri-State (B093)

**Priority:** Critical — inconclusive comparisons surface as clean
**Files:** WitnessTestifyPlanner.swift, Synthesizer.swift

### B093: Inconclusive Comparison Surfaces as Clean

**Location:** WitnessTestifyPlanner.swift:68, Synthesizer.swift:596
**Current:** `guard comparison.isRedirectionDetected else { return nil }` only checks redirection
When empty drifts → Synthesizer returns `.negative` (clean, 1.0 completeness)

**Fix in WitnessTestifyPlanner.swift:**

```swift
// B093: check BOTH redirection AND inconclusive
guard comparison.isRedirectionDetected || comparison.isInconclusive else { return nil }
if comparison.isInconclusive {
    // Return an inconclusive drift signal, not nil
    return AnswerDrift(inconclusive: true, reason: comparison.inconclusiveReason)
}
```

**Fix in Synthesizer.swift:596:**

```swift
// B093: empty drifts from inconclusive comparison must NOT be .negative
if subs.isEmpty {
    if comparisonWasInconclusive {
        return SectionEvidence(status: .inconclusive, completeness: 0.5)
    }
    return SectionEvidence(status: .negative, confidence: 1.0, completeness: 1.0)
}
```

---

## Cluster 3: Fallback Resolver (C025, C074)

**Priority:** High — baselines established on unencrypted connections
**Files:** VantageComparer.swift, AppModel.swift

### C025: BaselineLocal Built Over Plaintext UDP

**Location:** VantageComparer.swift:170-173
**Current:** Baseline established over unencrypted DNS
**Fix:** Use encrypted DNS for baseline when available:

```swift
// C025: baseline must use encrypted DNS when available
let baselineLocal: [String: [Answer]]
if let dohServer = encryptedDNSServer {
    baselineLocal = await queryViaDoH(domain: domain, server: dohServer)
} else {
    baselineLocal = await queryViaUDP(domain: domain)  // fallback only
}
```

### C074: Fallback to 8.8.8.8 Without User Knowledge

**Location:** AppModel.swift:1518, 938
**Current:** `let local = NetworkKeyState.getSystemDNSServers().first ?? server`
**Fix:** Surface the fallback to the user:

```swift
// C074: when falling back, inform the user
let local: String
if let systemResolver = NetworkKeyState.getSystemDNSServers().first {
    local = systemResolver
} else {
    local = server ?? "8.8.8.8"
    // Surface warning to user
    warnings.append("System resolver undetectable; using \(local) as fallback")
}
```

---

## Cluster 4: Pro License Gating (B200, B203)

**Priority:** High — non-Pro users get inflated clean verdicts
**Files:** AppModel.swift

### B200: runAllWardens() Gates Critical Sections Behind isPro

**Location:** AppModel.swift:416, 428, 435
**Current:** `if isPro { selection = .mirror; _ = await runCompare() }`
**Fix:** Run sections regardless of Pro status, but restrict output:

```swift
// B200: always run detection, gate only the output
// Non-Pro users see limited results but synthesis is not inflated
let mirrorResult = await runCompare()
if isPro {
    sectionInputs.comparison = mirrorResult
} else {
    sectionInputs.comparison = nil  // .untested — honest
}
```

### B203: Mirror Section Entirely Gated Behind Pro

**Location:** AppModel.swift (Mirror section)
**Fix:** Same approach — run detection, gate output

---

## Cluster 5: Prophet Consensus (B197, B198)

**Priority:** High — "No tampering" rendered from resolver count, not agreement
**Files:** ProphetView.swift, AppModel.swift

### B198: ProphetResult.Consensus Missing Agreement Fields

**Location:** AppModel.swift:46-49
**Current:** `struct Consensus { resolversQueried: Int, medianLatency: Double }`
**Fix:** Add agreement/disagreement fields:

```swift
struct Consensus {
    let resolversQueried: Int
    let medianLatency: Double
    let agreement: Int       // B198: number of resolvers that agreed
    let disagreement: Int    // B198: number that disagreed
    let consensusLevel: Double  // B198: agreement / total
}
```

### B197: "No Tampering" Rendered From Resolver Count

**Location:** ProphetView.swift:116
**Current:** Shows "No tampering" based on resolver count
**Fix:** Show based on agreement analysis:

```swift
// B197: render based on consensus, not count
if consensus.consensusLevel >= 0.8 && consensus.disagreement == 0 {
    Text("No tampering detected")
} else if consensus.disagreement > 0 {
    Text("Potential tampering detected")
} else {
    Text("Inconclusive")
}
```

---

## D-domain Cluster: Security & Hardening

### D019: Captive Portal Probe Uses Plain HTTP

**Location:** NetworkKeyState.swift ~line 135
**Current:** Plain HTTP to captive.apple.com
**Fix:** Use HTTPS for captive portal probe

### D032: 8.8.8.8 Hardcoded Across 90 Locations

**Location:** Multiple files (VantageComparer.swift, FilteringProbe.swift, ResponseSizeProbe.swift, ResolverAttribution.swift, SyxxCLI.swift)
**Fix:** Centralize resolver configuration in SyxxConfig, replace hardcoded values

### D034: Sentinel Stores Resolver List in Plaintext JSON

**Location:** ~/.syxx/sentinel.json
**Fix:** Encrypt at rest or use Keychain

### D063: CollectorPinning Reads SPKI Pins from UserDefaults

**Location:** LeakDetector.swift:465
**Fix:** Move SPKI pins to Keychain

### D067: WitnessKeyManager Access Controls May Need Upgrading

**Location:** WitnessKeyManager.swift
**Current:** kSecAttrAccessibleWhenUnlockedThisDeviceOnly (exists but may need review)
**Fix:** Verify sufficiency; upgrade if needed

---

## E-domain Cluster: Platform Integration

### E028: System Resolver Fallback

**Location:** NetworkKeyState.swift, SyxxCLI.swift
**Current:** Falls back to 8.8.8.8 when system resolver undetectable
**Fix:** Surface fallback to user, use encrypted DNS

### E048: Windows Parity Gaps

**Location:** Needs Windows codebase investigation
**Fix:** After macOS fixes land, audit Windows Syxx.Kit for matching changes

---

## Build Order

1. **Cluster 1 (Signature Trust)** — Critical security, no dependencies
2. **Cluster 2 (Comparison Tri-State)** — Critical detection, no dependencies
3. **Cluster 5 (Prophet Consensus)** — High detection, no dependencies
4. **Cluster 3 (Fallback Resolver)** — High, may depend on Cluster 1
5. **Cluster 4 (Pro License Gating)** — High, may depend on Cluster 2
6. **D-domain** — Security hardening, sequential
7. **E-domain** — Platform parity, after D-domain

## Test Strategy

For each delta:

1. Verify test FAILS before fix
2. Apply fix
3. Verify test PASSES after fix
4. Run full test suite to check for regressions
