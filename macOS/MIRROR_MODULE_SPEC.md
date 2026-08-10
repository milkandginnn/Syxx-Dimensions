# Mirror Module Implementation Specification

**Version:** 1.0.0  
**Created:** 2026-08-06  
**Status:** Draft  
**Platform:** macOS

---

## 1. Overview

The Mirror Module provides a side-by-side terminal-style comparison between static web content captured remotely (via the Dimension server) and locally (from the user's device). This allows users to detect DNS hijacking, content injection, or man-in-the-middle attacks by comparing what the server sees versus what the local device receives.

### 1.1 Core Concept

```
┌─────────────────┐                    ┌─────────────────┐
│  Remote Server  │                    │  Local Device   │
│  (Dimension)    │                    │  (User's Mac)   │
└────────┬────────┘                    └────────┬────────┘
         │                                      │
         │ 1. Fetch static content              │ 1. Fetch static content
         │    (HTML, JS, CSS)                   │    (HTML, JS, CSS)
         │                                      │
         │ 2. Concatenate bundle                │ 2. Concatenate bundle
         │                                      │
         │ 3. Encrypt + Sign                    │ 3. Store locally
         │    (Ed25519 + AES-256-GCM)           │
         │                                      │
         │ 4. Client retrieves                  │ 4. Client retrieves
         │    signed bundle                     │    local bundle
         │                                      │
         │ 5. Decrypt + Verify                  │ 5. Prepare for diff
         │                                      │
         └──────────────┬───────────────────────┘
                        │
                        ▼
              ┌─────────────────────┐
              │  Side-by-Side Diff  │
              │  (Terminal Style)   │
              └─────────────────────┘
```

---

## 2. Architecture

### 2.1 Component Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                        MirrorView                            │
│  (Main UI container - two terminal panes + input field)     │
└────────────────┬────────────────────────────────────────────┘
                 │
    ┌────────────┴────────────┐
    │                         │
┌───▼──────────┐      ┌──────▼──────────┐
│MirrorTerminal│      │MirrorTerminal   │
│   View       │      │    View         │
│ (Remote)     │      │  (Local)        │
└───┬──────────┘      └──────┬──────────┘
    │                         │
    └────────────┬────────────┘
                 │
         ┌───────▼────────┐
         │MirrorDiffView  │
         │(Unified diff)  │
         └───────┬────────┘
                 │
    ┌────────────▼────────────┐
    │   MirrorCaptureService  │
    │  (Orchestration layer)  │
    └────┬───────────────┬────┘
         │               │
    ┌────▼────┐    ┌─────▼─────┐
    │Dimension│    │LocalFetch │
    │ Client  │    │  Service  │
    └─────────┘    └───────────┘
```

### 2.2 Data Models

#### MirrorBundle
```swift
struct MirrorBundle {
    let source: BundleSource          // .remote or .local
    let domain: String
    let timestamp: Date
    let htmlContent: String           // Raw HTML
    let cssContent: String            // Concatenated CSS
    let jsContent: String             // Concatenated JavaScript
    let metadata: BundleMetadata
    
    var concatenated: String {
        // HTML + CSS + JS in canonical order
        return """
        <!-- HTML -->
        \(htmlContent)
        
        <!-- CSS -->
        \(cssContent)
        
        <!-- JavaScript -->
        \(jsContent)
        """
    }
}

enum BundleSource {
    case remote
    case local
}

struct BundleMetadata {
    let fetchDuration: TimeInterval
    let byteSize: Int
    let resourceCount: Int
    let signature: String?           // Ed25519 signature (remote only)
    let serverHash: String           // SHA-256 of concatenated content
}
```

#### MirrorResult
```swift
struct MirrorResult {
    let remoteBundle: MirrorBundle
    let localBundle: MirrorBundle
    let diffResult: DiffResult
    let degradationScore: DegradationScore
    let comparisonTimestamp: Date
}

struct DiffResult {
    let htmlDiff: [DiffLine]
    let cssDiff: [DiffLine]
    let jsDiff: [DiffLine]
    let totalDifferences: Int
    let hasChanges: Bool
}

struct DegradationScore {
    let score: Double                // 0.0 (identical) to 1.0 (completely different)
    let classification: Classification
    
    enum Classification {
        case identical               // score == 0.0
        case minorDifferences        // 0.0 < score <= 0.1
        case moderateDifferences     // 0.1 < score <= 0.3
        case significantDifferences  // 0.3 < score <= 0.6
        case criticalDifferences     // score > 0.6
    }
}
```

### 2.3 Service Layer

#### MirrorCaptureService

Orchestrates the entire capture-and-compare workflow:

```swift
protocol MirrorCaptureServicing {
    func capture(domain: String) async throws -> MirrorResult
}

final class MirrorCaptureService: MirrorCaptureServicing {
    private let dimensionClient: DimensionClient
    private let localFetcher: LocalFetchService
    private let diffEngine: DiffEngine
    private let degradationCalculator: DegradationCalculator
    
    func capture(domain: String) async throws -> MirrorResult {
        // 1. Initiate remote capture
        async let remoteBundle = captureRemote(domain: domain)
        
        // 2. Simultaneously capture locally
        async let localBundle = captureLocal(domain: domain)
        
        // 3. Wait for both
        let (remote, local) = try await (remoteBundle, localBundle)
        
        // 4. Generate diffs
        let diffResult = diffEngine.compare(remote: remote, local: local)
        
        // 5. Calculate degradation
        let degradation = degradationCalculator.calculate(diff: diffResult)
        
        return MirrorResult(
            remoteBundle: remote,
            localBundle: local,
            diffResult: diffResult,
            degradationScore: degradation,
            comparisonTimestamp: Date()
        )
    }
}
```

#### LocalFetchService

Handles local web content retrieval with proper isolation:

```swift
final class LocalFetchService {
    private let session: URLSession
    
    func fetch(domain: String) async throws -> MirrorBundle {
        // 1. Fetch HTML
        let html = try await fetchHTML(url: "https://\(domain)")
        
        // 2. Parse and fetch CSS
        let cssURLs = extractCSSURLs(from: html)
        let cssContents = try await fetchResources(urls: cssURLs)
        
        // 3. Parse and fetch JavaScript
        let jsURLs = extractJSURLs(from: html)
        let jsContents = try await fetchResources(urls: jsURLs)
        
        // 4. Concatenate
        return MirrorBundle(
            source: .local,
            domain: domain,
            timestamp: Date(),
            htmlContent: html,
            cssContent: cssContents.joined(separator: "\n\n"),
            jsContent: jsContents.joined(separator: "\n\n"),
            metadata: BundleMetadata(
                fetchDuration: 0,
                byteSize: 0,
                resourceCount: cssURLs.count + jsURLs.count,
                signature: nil,
                serverHash: ""
            )
        )
    }
}
```

---

## 3. Security Considerations

### 3.1 Encryption Protocol

**Remote Bundle Encryption:**
- **Transport Layer:** HTTPS (TLS 1.3) with certificate pinning to Dimension server
- **Application Layer:** 
  - Bundle encrypted with AES-256-GCM
  - Key derived from session-specific nonce + server secret
  - Bundle signed with Ed25519 (existing WarpManifestPinning keys)
  - Replay protection via timestamp + nonce

**Signature Verification:**
```swift
func verifyRemoteBundle(_ bundle: MirrorBundle) throws {
    guard let signature = bundle.metadata.signature else {
        throw MirrorError.missingSignature
    }
    
    let publicKey = WarpManifestPinning.publicKey
    let bundleHash = bundle.metadata.serverHash
    
    guard Ed25519.verify(signature: signature, 
                         message: bundleHash.data(using: .utf8)!,
                         publicKey: publicKey) else {
        throw MirrorError.invalidSignature
    }
}
```

### 3.2 Isolation Strategy

**Memory Isolation:**
- Remote and local bundles stored in separate `MirrorBundle` instances
- No shared mutable state between bundles
- Diff engine receives immutable copies

**Network Isolation:**
- Remote fetch uses Dimension server's network path
- Local fetch uses device's network stack
- Separate URLSession instances with different configurations

### 3.3 Threat Mitigations

| Threat | Mitigation |
|--------|-----------|
| MITM on remote fetch | TLS 1.3 + certificate pinning + Ed25519 signature |
| DNS hijacking (local) | Comparison with remote reveals divergence |
| Replay attacks | Nonce + timestamp validation |
| Content injection | SHA-256 hash verification |
| Bundle contamination | Memory isolation + immutable data structures |

---

## 4. UI Layout

### 4.1 Visual Design

**Color Palette (from mockups):**
- Background: `#0b0518` (deep purple-black)
- Surface: `#1a0c33` (dark purple)
- Border: `rgba(148, 120, 246, 0.14)` (violet, 14% opacity)
- Primary text: `#efeafe` (light lavender)
- Secondary text: `#9a8fc0` (muted purple)
- Accent (added lines): `#2dd4bf` (teal/green)
- Accent (removed lines): `#f87171` (red)
- Accent (context): `#8f83b8` (gray-purple)

**Typography:**
- Headings: `Cinzel` (serif, elegant)
- Body: `EB Garamond` (serif, readable)
- Code/Diff: `ui-monospace` (system monospace)

### 4.2 Layout Structure

```
┌──────────────────────────────────────────────────────────────┐
│  Mirror Module                                               │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Domain: [____________________] [Capture]               │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌──────────────────────┐    ┌──────────────────────┐       │
│  │  Remote (Server)     │    │  Local (Your Mac)    │       │
│  │  ─────────────────── │    │  ─────────────────── │       │
│  │  $ curl example.com  │    │  $ curl example.com  │       │
│  │                      │    │                      │       │
│  │  <!DOCTYPE html>     │    │  <!DOCTYPE html>     │       │
│  │  <html>              │    │  <html>              │       │
│  │  <head>              │    │  <head>              │       │
│  │+   <script src="x">  │    │+   <script src="y">  │       │
│  │  ...                 │    │  ...                 │       │
│  │                      │    │                      │       │
│  │  [Scrollable]        │    │  [Scrollable]        │       │
│  └──────────────────────┘    └──────────────────────┘       │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  Unified Diff View                                      │ │
│  │  ─────────────────────────────────────────────────────  │ │
│  │  <  <script src="https://cdn.example.com/analytics.js"> │ │
│  │  >  <script src="https://evil.example.com/track.js">    │ │
│  │                                                         │ │
│  │  [Expandable sections: HTML | CSS | JavaScript]         │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  Degradation: Moderate (23% differences)                     │
│  [Export Report] [View Details]                              │
└──────────────────────────────────────────────────────────────┘
```

### 4.3 Terminal Styling

Each terminal pane should mimic macOS Terminal.app:

```swift
struct MirrorTerminalStyle {
    static let backgroundColor = Color(red: 0.04, green: 0.02, blue: 0.09)  // #0b0518
    static let textColor = Color(red: 0.94, green: 0.96, blue: 0.99)        // #efeafe
    static let promptColor = Color(red: 0.18, green: 0.83, blue: 0.75)      // #2dd4bf
    static let font = Font.custom("Menlo", size: 12)
    static let lineSpacing: CGFloat = 1.4
    static let padding: CGFloat = 16
}
```

---

## 5. Acceptance Criteria

### 5.1 Functional Requirements

- [ ] **AC-01:** User can enter a domain name in an input field
- [ ] **AC-02:** User can trigger capture via a "Capture" button
- [ ] **AC-03:** System fetches static content (HTML, CSS, JS) from remote server via Dimension API
- [ ] **AC-04:** System simultaneously fetches static content locally from user's device
- [ ] **AC-05:** Remote bundle is encrypted (AES-256-GCM) and signed (Ed25519)
- [ ] **AC-06:** System verifies remote bundle signature before display
- [ ] **AC-07:** System generates unified diff showing line-by-line differences
- [ ] **AC-08:** Diff view uses color coding (green for additions, red for deletions)
- [ ] **AC-09:** System calculates degradation score (0.0 to 1.0) based on differences
- [ ] **AC-10:** Degradation classification displayed (identical/minor/moderate/significant/critical)
- [ ] **AC-11:** Both terminal panes are independently scrollable
- [ ] **AC-12:** Unified diff view is expandable/collapsible by section (HTML/CSS/JS)
- [ ] **AC-13:** User can export comparison report to file
- [ ] **AC-14:** System handles network errors gracefully with user-friendly messages
- [ ] **AC-15:** System handles signature verification failures (display error, do not show content)

### 5.2 Non-Functional Requirements

- [ ] **NFR-01:** Remote + local capture completes within 10 seconds for typical domains
- [ ] **NFR-02:** Diff generation completes within 2 seconds for bundles up to 5MB
- [ ] **NFR-03:** UI renders smoothly with bundles up to 10,000 lines
- [ ] **NFR-04:** Memory usage stays below 200MB during comparison
- [ ] **NFR-05:** All network requests use HTTPS (TLS 1.3)
- [ ] **NFR-06:** No sensitive data logged to console or crash reports
- [ ] **NFR-07:** UI matches Syxx design system (colors, typography, spacing)

### 5.3 Security Requirements

- [ ] **SEC-01:** Remote bundle signature verified before any content display
- [ ] **SEC-02:** TLS certificate pinning enforced for Dimension server
- [ ] **SEC-03:** Nonce validation prevents replay attacks
- [ ] **SEC-04:** Local and remote bundles isolated in memory (no shared references)
- [ ] **SEC-05:** Failed signature verification displays error, never shows unverified content
- [ ] **SEC-06:** Bundle hashes (SHA-256) displayed for user verification

### 5.4 Error Handling

- [ ] **ERR-01:** Network timeout: "Connection timed out. Please check your network and try again."
- [ ] **ERR-02:** Invalid domain: "Invalid domain name. Please enter a valid domain (e.g., example.com)."
- [ ] **ERR-03:** Signature failure: "Remote bundle signature verification failed. Content may be tampered."
- [ ] **ERR-04:** Fetch failure: "Failed to fetch content from [source]. Please try again."
- [ ] **ERR-05:** Diff failure: "Failed to generate comparison. Bundles may be too large or corrupted."

---

## 6. File Structure

### 6.1 New Files

```
SyxxApp/
├── Mirror/
│   ├── Views/
│   │   ├── MirrorView.swift                    # Main container view
│   │   ├── MirrorTerminalView.swift            # Terminal-style pane component
│   │   ├── MirrorDiffView.swift                # Unified diff display
│   │   ├── MirrorInputField.swift              # Domain input field
│   │   └── MirrorDegradationBadge.swift        # Degradation score badge
│   ├── Models/
│   │   ├── MirrorBundle.swift                  # Bundle data model
│   │   ├── MirrorResult.swift                  # Comparison result
│   │   ├── DiffResult.swift                    # Diff data structures
│   │   └── DegradationScore.swift              # Scoring model
│   └── Services/
│       ├── MirrorCaptureService.swift          # Orchestration layer
│       ├── LocalFetchService.swift             # Local content fetcher
│       └── DegradationCalculator.swift         # Score calculation

SyxxKit/
├── Mirror/
│   ├── MirrorBundle+Encryption.swift           # Encryption/decryption
│   ├── MirrorBundle+Signature.swift            # Ed25519 signing/verification
│   ├── StaticContentParser.swift               # HTML/CSS/JS extraction
│   └── DiffEngine.swift                        # Line-based diff algorithm
```

### 6.2 Modified Files

```
SyxxKit/
├── ProbePlan.swift                             # Add Mirror capture orchestration
├── SectionInputs.swift                         # Add mirrorBundle field
└── Synthesizer.swift                           # Integrate Mirror results

SyxxApp/
├── AppModel.swift                              # Add mirror capture state
└── ContentView.swift                           # Wire Mirror view into navigation
```

---

## 7. Data Flow

### 7.1 Capture Sequence

```
User Action: [Enter domain] [Click Capture]
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│ 1. MirrorCaptureService.capture(domain:)                    │
└─────────────────────────────────────────────────────────────┘
    │
    ├──────────────────────────────┬──────────────────────────────┐
    │                              │                              │
    ▼                              ▼                              ▼
┌─────────────────┐    ┌──────────────────────┐    ┌──────────────────────┐
│ 2a. Remote      │    │ 2b. Local            │    │ 2c. Generate         │
│ Fetch           │    │ Fetch                │    │ Nonce                │
│                 │    │                      │    │                      │
│ - HTTPS request │    │ - NSURLSession       │    │ - UUID v4            │
│ - Certificate   │    │ - Parse HTML         │    │ - Timestamp          │
│   pinning       │    │ - Extract CSS/JS     │    │                      │
│ - Receive       │    │ - Fetch resources    │    │                      │
│   encrypted     │    │ - Concatenate        │    │                      │
│   bundle        │    │                      │    │                      │
└────────┬────────┘    └──────────┬───────────┘    └──────────┬───────────┘
         │                        │                            │
         │                        │                            │
         ▼                        ▼                            │
┌─────────────────────────────────────────────────────────┐    │
│ 3. Server-side processing (Dimension server)            │    │
│                                                         │    │
│ - Fetch static content from clean vantage point        │◄───┘
│ - Concatenate HTML + CSS + JS
│ - Encrypt with AES-256-GCM (key = nonce + secret)
│ - Sign with Ed25519
│ - Return: { encryptedBundle, signature, metadata }
└─────────────────────────────────────────────────────────┘
         │
         │
         ▼
┌─────────────────────────────────────────────────────────┐
│ 4. Client verification                                  │
│                                                         │
│ - Verify Ed25519 signature (WarpManifestPinning keys)  │
│ - Decrypt AES-256-GCM bundle                           │
│ - Validate nonce matches                               │
│ - Validate timestamp within window                     │
│ - Compute SHA-256 hash                                 │
└─────────────────────────────────────────────────────────┘
         │
         │
         ▼
┌─────────────────────────────────────────────────────────┐
│ 5. Diff generation                                      │
│                                                         │
│ - TextDiff.unified(remote, local) for HTML             │
│ - TextDiff.unified(remote, local) for CSS              │
│ - TextDiff.unified(remote, local) for JS               │
│ - Count additions, deletions, context lines            │
└─────────────────────────────────────────────────────────┘
         │
         │
         ▼
┌─────────────────────────────────────────────────────────┐
│ 6. Degradation calculation                              │
│                                                         │
│ - totalDifferences = additions + deletions             │
│ - totalLines = remote.lines + local.lines              │
│ - score = totalDifferences / totalLines                │
│ - Classify: identical / minor / moderate / significant │
└─────────────────────────────────────────────────────────┘
         │
         │
         ▼
┌─────────────────────────────────────────────────────────┐
│ 7. UI rendering                                         │
│                                                         │
│ - Display remote bundle in left terminal pane          │
│ - Display local bundle in right terminal pane          │
│ - Display unified diff in bottom pane                  │
│ - Show degradation badge                               │
│ - Enable export button                                 │
└─────────────────────────────────────────────────────────┘
```

### 7.2 State Management

```swift
@MainActor
final class MirrorViewModel: ObservableObject {
    @Published var domain: String = ""
    @Published var isCapturing: Bool = false
    @Published var remoteBundle: MirrorBundle?
    @Published var localBundle: MirrorBundle?
    @Published var diffResult: DiffResult?
    @Published var degradationScore: DegradationScore?
    @Published var errorMessage: String?
    
    private let captureService: MirrorCaptureServicing
    
    func capture() async {
        guard !domain.isEmpty else {
            errorMessage = "Please enter a domain name."
            return
        }
        
        isCapturing = true
        errorMessage = nil
        
        do {
            let result = try await captureService.capture(domain: domain)
            
            await MainActor.run {
                self.remoteBundle = result.remoteBundle
                self.localBundle = result.localBundle
                self.diffResult = result.diffResult
                self.degradationScore = result.degradationScore
                self.isCapturing = false
            }
        } catch {
            await MainActor.run {
                self.errorMessage = error.localizedDescription
                self.isCapturing = false
            }
        }
    }
}
```

---

## 8. API Integration

### 8.1 Dimension Server Endpoint

**Endpoint:** `POST /api/v1/mirror/capture`

**Request:**
```json
{
  "domain": "example.com",
  "nonce": "550e8400-e29b-41d4-a716-446655440000",
  "timestamp": "2026-08-06T12:34:56Z"
}
```

**Response:**
```json
{
  "encryptedBundle": "base64-encoded-aes-256-gcm-bundle",
  "signature": "base64-encoded-ed25519-signature",
  "metadata": {
    "domain": "example.com",
    "timestamp": "2026-08-06T12:34:57Z",
    "byteSize": 245760,
    "resourceCount": 12,
    "serverHash": "sha256:abc123..."
  }
}
```

**Error Responses:**
- `400 Bad Request`: Invalid domain or missing nonce
- `401 Unauthorized`: Invalid license key
- `500 Internal Server Error`: Server-side fetch failure
- `504 Gateway Timeout`: Upstream timeout

### 8.2 DimensionClient Extension

```swift
extension DimensionClient {
    func captureMirrorBundle(domain: String, nonce: String) async throws -> MirrorEncryptedBundle {
        let endpoint = baseURL.appendingPathComponent("api/v1/mirror/capture")
        
        var request = URLRequest(url: endpoint)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        
        let body: [String: Any] = [
            "domain": domain,
            "nonce": nonce,
            "timestamp": ISO8601DateFormatter().string(from: Date())
        ]
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        
        // Certificate pinning enforced
        let (data, response) = try await session.data(for: request)
        
        guard let httpResponse = response as? HTTPURLResponse,
              httpResponse.statusCode == 200 else {
            throw MirrorError.serverError
        }
        
        return try JSONDecoder().decode(MirrorEncryptedBundle.self, from: data)
    }
}
```

---

## 9. Testing Strategy

### 9.1 Unit Tests

- **MirrorBundleTests:** Encryption/decryption round-trip, signature verification
- **DiffEngineTests:** Edge cases (empty, identical, completely different)
- **DegradationCalculatorTests:** Score calculation accuracy, classification boundaries
- **StaticContentParserTests:** HTML/CSS/JS extraction from various page structures

### 9.2 Integration Tests

- **MirrorCaptureServiceTests:** End-to-end capture with mocked Dimension server
- **LocalFetchServiceTests:** Real HTTP requests to test domains
- **SignatureVerificationTests:** Valid/invalid/expired signatures

### 9.3 UI Tests

- **MirrorViewTests:** Input validation, button states, error display
- **MirrorTerminalViewTests:** Scrolling, syntax highlighting, line wrapping
- **MirrorDiffViewTests:** Color coding, expand/collapse, export functionality

---

## 10. Future Enhancements

### 10.1 Phase 2 Features

- **Historical comparisons:** Store previous captures for trend analysis
- **Scheduled monitoring:** Automatic periodic captures with change notifications
- **Multi-domain batch capture:** Compare multiple domains in one session
- **Advanced diff algorithms:** Myers diff for better alignment
- **Syntax-aware diffing:** Understand HTML structure, not just text

### 10.2 Phase 3 Features

- **Visual diff:** Screenshot comparison of rendered pages
- **Performance metrics:** Load time comparison between remote and local
- **Certificate inspection:** Compare TLS certificate chains
- **Header analysis:** Compare HTTP response headers

---

## 11. Dependencies

### 11.1 Existing SyxxKit Components

- `DimensionClient`: Network communication with Dimension server
- `WarpManifestPinning`: Ed25519 key management
- `TextDiff`: Line-based diff algorithm (extend if needed)

### 11.2 External Libraries

- **CryptoKit:** AES-256-GCM encryption, Ed25519 signatures (Apple native)
- **SwiftSoup:** HTML parsing and resource extraction (if needed)
- **KeychainAccess:** Secure storage of session keys (optional)

---

## 12. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|-----------|
| Dimension server unavailable | High | Medium | Retry logic, offline mode with local-only capture |
| Large bundles (>10MB) | Medium | Low | Streaming diff, pagination, memory limits |
| TLS certificate pinning failures | High | Low | Fallback to system trust store with warning |
| Signature key rotation | Medium | Low | Support multiple keys in WarpManifestPinning |
| Network timeout on slow connections | Medium | Medium | Configurable timeout, progress indicator |

---

## 13. Rollout Plan

### 13.1 Development Phases

1. **Phase 1 (Weeks 1-2):** Core capture and diff functionality
2. **Phase 2 (Week 3):** UI implementation and styling
3. **Phase 3 (Week 4):** Security hardening and testing
4. **Phase 4 (Week 5):** Integration with Syxx app and beta testing

### 13.2 Feature Flags

```swift
enum FeatureFlags {
    static let mirrorModuleEnabled = true
    static let mirrorExportEnabled = false  // Phase 2
    static let mirrorHistoricalEnabled = false  // Phase 3
}
```

---

## 14. Glossary

- **Bundle:** Concatenated HTML + CSS + JavaScript content from a single domain
- **Degradation Score:** Metric (0.0 to 1.0) indicating how different remote and local content are
- **Dimension Server:** Syxx's backend server that provides clean vantage point captures
- **Nonce:** Cryptographic random value used once to prevent replay attacks
- **Static Content:** HTML, CSS, and JavaScript before any client-side execution
- **Unified Diff:** Standard diff format showing context, additions (+), and deletions (-)

---

## 15. References

- **Mockups:** `/Users/masterrabbit/Software/Syxx/Syxx GUI Mockups/Syxx macOS GUI Mxups.html`
- **Typography:** `/Users/masterrabbit/Downloads/Syxx Type Specimen.html`
- **Existing Code:** `/Users/masterrabbit/Software/Syxx/macOS/Sources/`
- **WARP_MANIFEST_V1 Spec:** Existing signed content integrity protocol
- **DimensionClient:** Existing network client for Dimension server

---

**Document Version:** 1.0.0  
**Last Updated:** 2026-08-06  
**Author:** AI Assistant  
**Reviewers:** (Pending)  
**Approval:** (Pending)
