# Mirror Module Refined Implementation Specification

**Version:** 2.0.0
**Created:** 2026-08-06
**Status:** Refined (supersedes v1.0.0)
**Platforms:** macOS (SwiftUI), Linux (Avalonia), Windows (WinUI 3)

---

## 1. Gap Analysis: CSV Requirements vs Existing State

### 1.1 What Already Exists (Reuse)

| Component | Location | Status |
|-----------|----------|--------|
| DNS resolver comparison | All 3 platforms | ✅ Complete |
| WARP_MANIFEST_V1 signed manifest | Server warp.py + app.py | ✅ Complete |
| Raw content endpoint | Server /api/warp/raw | ✅ Complete |
| Ed25519 signing | Server WarpManifestPinning | ✅ Complete |
| Domain input + capture button | All 3 MirrorViews | ✅ Complete |
| Static content integrity display | macOS MirrorView | ⚠️ Partial |
| AppModel wiring | AppModel.run() → ProbePlan | ✅ Complete |
| Color palette & typography | Design system | ✅ Complete |

### 1.2 Identified Gaps (14 total)

| Gap # | What's Missing |
|-------|----------------|
| G-01 | Terminal-style panes mocking host OS terminal |
| G-02 | Fallback for missing pre-JS resources |
| G-03 | Server encrypted bundle endpoint (AES-256-GCM) |
| G-04 | Client AES-256-GCM decryption pipeline |
| G-05 | Git-diff-style code comparison view |
| G-06 | Degradation score calculation (line-diff-based) |
| G-07 | Interactive file explorer with diff markup |
| G-08 | Mockup vs CSV conflict (attribute table vs terminal) |
| G-09 | Linux terminal-style MirrorView |
| G-10 | Windows terminal-style MirrorView |
| G-11 | features-implemented.CSV |
| G-12 | Diff engine (Myers diff) for Swift and C# |
| G-13 | Local static content fetcher on all client platforms |
| G-14 | Terminal chrome components per platform |

### 1.3 Conflict Resolution: Dual-View Design

1. **Primary view** (mockup): Attribute comparison table — default, high-level summary
2. **Expanded view** (CSV): Terminal-style side-by-side code comparison with diff markup — revealed on "View Details"

---

## 2. Architecture

### 2.1 Server: REUSE existing endpoints
- `GET /api/warp/manifest?url=<domain>&nonce=<nonce>` — signed manifest
- `GET /api/warp/raw?url=<asset_url>&raw=1` — raw bytes + SHA-256
- Phase 1: Skip AES-256-GCM wrapping; signed manifest over TLS 1.3 is sufficient
- Add `fallback_mode` + `fallback_reason` to manifest output

### 2.2 Data Models

```swift
struct MirrorDiffSession {
    let domain: String
    let remoteManifest: WarpManifest
    let localManifest: LocalManifest
    let diffSections: [DiffSection]
    let degradationScore: DegradationScore
    let fallbackMode: Bool
}

struct DiffSection {
    let name: String           // "HTML", "CSS", "JavaScript"
    let remoteContent: String
    let localContent: String
    let lines: [DiffLine]
    let additions: Int
    let deletions: Int
}

enum DiffLine {
    case context(String, lineNumber: Int)
    case addition(String, lineNumber: Int)
    case deletion(String, lineNumber: Int)
}

struct DegradationScore {
    let score: Double          // 0.0 to 1.0
    let classification: DegradationClassification
    let fallbackAdjusted: Bool
}

enum DegradationClassification: String {
    case identical = "IDENTICAL"
    case minor = "MINOR DIFFERENCES"
    case moderate = "MODERATE DIFFERENCES"
    case significant = "SIGNIFICANT DIFFERENCES"
    case critical = "CRITICAL DIFFERENCES"
}
```

### 2.3 Terminal Chrome Per Platform

| Platform | Style | Font | Prompt |
|----------|-------|------|--------|
| macOS | Terminal.app (traffic lights) | Menlo 12pt | `user@hostname ~ %` green |
| Linux | GNOME Terminal (flat header) | Ubuntu Mono 12pt | `user@hostname:~$` green |
| Windows | PowerShell (tab bar) | Cascadia Code 12pt | `PS C:\Users\user>` yellow |

---

## 3. Acceptance Criteria (36 total)

### Functional (AC-01 to AC-22)
- AC-01: Domain input on all platforms
- AC-02: Capture button on all platforms
- AC-03: Fetches signed manifest from /api/warp/manifest
- AC-04: Simultaneously fetches local static content
- AC-05: Ed25519 signature verified before display
- AC-06: Attribute comparison table with match/mismatch indicators
- AC-07: Unified diff per section (HTML, CSS, JS)
- AC-08: Diff markup: teal (#2dd4bf) additions, red (#f87171) deletions
- AC-09: Degradation score 0.0–1.0 calculated
- AC-10: Classification displayed (5 tiers)
- AC-11: Attribute table green ✓ / red ✗ per mockup
- AC-12: Terminal panes independently scrollable with OS chrome
- AC-13: macOS Terminal.app chrome
- AC-14: Linux GNOME Terminal chrome
- AC-15: Windows PowerShell chrome
- AC-16: Expandable/collapsible detailed comparison
- AC-17: Section tabs (HTML/CSS/JS)
- AC-18: Fallback mode detection and warning
- AC-19: Fallback floors degradation at 0.1
- AC-20: Network error handling
- AC-21: Signature failure handling
- AC-22: Export button (disabled Phase 1)

### Non-Functional (NFR-01 to NFR-08)
- NFR-01: Capture < 10 seconds
- NFR-02: Diff < 2 seconds for 5MB
- NFR-03: Smooth rendering up to 10,000 lines
- NFR-04: Memory < 200MB
- NFR-05: TLS 1.3 + cert pinning
- NFR-06: No sensitive data logged
- NFR-07: Design system compliance
- NFR-08: Platform-native terminal chrome

### Cross-Platform (XP-01 to XP-05)
- XP-01: macOS feature-complete
- XP-02: Linux feature-complete
- XP-03: Windows feature-complete
- XP-04: Identical degradation scores across platforms
- XP-05: Identical diff markup across platforms

### Deliverables
- DEL-01: features-implemented.CSV created
