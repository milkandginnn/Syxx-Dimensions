# Syxx Observability Plan: Sentry + OpenTelemetry

## Current State Audit

| Component | Bootstrap | Traces | Spans | Logs | Metrics | Crash | Status |
|---|---|---|---|---|---|---|---|
| macOS (SyxxApp) | ✅ OTelBootstrap.swift → bugsmasher | ✅ OTLP HTTP exporter | ❌ 0 spans created | ❌ | ❌ | ❌ | **Wired but idle** |
| Windows (SyxxUI) | ✅ App.xaml.cs → bugsmasher | ✅ OTLP HTTP/Protobuf | ❌ 0 spans created | ❌ | ❌ | ❌ | **Wired but idle** |
| Linux (Avalonia) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | **Not instrumented** |
| Python server | ✅ app.py → bugsmasher | ✅ OTLP HTTP exporter | ❌ 0 spans created | ✅ error-level via logging_util | ⚠️ Local counters only, no export | ❌ | **Most complete** |

**The core problem:** TracerProviders are registered on 3 of 4 platforms, but zero custom spans are created anywhere. The exporters have nothing to export. No crash capture exists on any platform.

## Architecture

```
                    ┌──────────────────────────────────────┐
                    │          Sentry Self-Hosted           │
                    │  (Docker on bugsmasher.syxx.app)      │
                    │                                       │
                    │  ┌─────────┐  ┌───────┐  ┌────────┐ │
                    │  │  Issues  │  │ Traces │  │ Alerts │ │
                    │  │ (errors) │  │(spans) │  │(rules) │ │
                    │  └────┬─────┘  └───┬───┘  └────┬───┘ │
                    └───────┼────────────┼────────────┼─────┘
                            │            │            │
              ┌─────────────┼────────────┼────────────┼─────────────┐
              │              Sentry SDK (errors/crashes)            │
              │              OTLP Exporter (traces)                 │
              └──────────┬──────────┬──────────┬────────────────────┘
                         │          │          │
        ┌────────────────┤          │          ├────────────────┐
        │                │          │          │                │
   ┌────┴─────┐    ┌────┴─────┐   ┌┴────────┐  ┌─────────────┐
   │  macOS   │    │ Windows  │   │  Linux  │  │   Server    │
   │ sentry-  │    │ sentry-  │   │ sentry- │  │  sentry-    │
   │ cocoa    │    │ dotnet   │   │ dotnet  │  │  python     │
   │ +OTel    │    │ +OTel    │   │ +OTel   │  │  +OTel      │
   └──────────┘    └──────────┘   └─────────┘  └─────────────┘
```

**Dual-channel approach:**
- **Sentry SDK** → crash reports, unhandled exceptions, breadcrumbs, release tracking (what Sentry does best)
- **OTel exporter** → structured traces with custom spans for DNS operations, probe pipelines, server requests (what OTel does best)
- Sentry accepts OTLP natively — both channels flow into one dashboard

---

## Phase 1: Deploy Sentry Self-Hosted (S)

**Goal:** Get the Sentry server running on your infrastructure.

### Steps

1. **Deploy via Docker Compose** on `bugsmasher.syxx.app` (or a subdomain like `sentry.syxx.app`):
   ```bash
   git clone https://github.com/getsentry/self-hosted.git
   cd self-hosted
   ./install.sh    # creates admin user, configures PostgreSQL/ClickHouse/Kafka
   docker compose up -d
   ```

2. **Create Sentry projects** (one per component):
   - `syxx-macos` (platform: Cocoa)
   - `syxx-windows` (platform: .NET)
   - `syxx-linux` (platform: .NET)
   - `syxx-server` (platform: Python)

3. **Collect DSNs** — each project gets a DSN like `https://<key>@sentry.syxx.app/<project-id>`. These go into build config or environment variables (never hardcoded in source).

4. **Enable OTLP ingest** in Sentry settings → Performance → enable "OpenTelemetry" as a trace source.

5. **Configure alerting rules:**
   - New issue type → notify (email/Slack)
   - Crash rate > threshold → notify
   - Regression (previously resolved issue reappears) → notify

**Acceptance bar:** Sentry UI loads, all 4 projects created, test event sent from each DSN.

---

## Phase 2: Sentry SDK Integration (M)

**Goal:** Automatic crash/exception capture on all 4 platforms.

### 2a. macOS — `sentry-cocoa`

**File:** `macOS/Package.swift`
```swift
.package(url: "https://github.com/getsentry/sentry-cocoa", from: "8.40.0")
```

**File:** `macOS/Sources/SyxxApp/SentryBootstrap.swift` (new)
```swift
import Sentry

enum SentryBootstrap {
    static func setup() {
        SentrySDK.start { options in
            options.dsn = SyxxConfig.sentryDSN  // from env/config, never hardcoded
            options.environment = SyxxConfig.buildEnvironment
            options.releaseName = "syxx-macos@\(SyxxConfig.version)"
            options.tracesSampleRate = 0.1  // 10% of traces alongside Sentry errors
            options.enableAutoPerformanceTracing = true
            options.enableAppHangTracking = true
            options.attachStacktrace = true
            options.sendDefaultPii = false  // privacy-first
        }
    }
}
```

**Call site:** `SyxxApp.swift` `init()` — call `SentryBootstrap.setup()` before `OTelBootstrap.setup()`.

**What you get automatically:**
- Mach exception capture (SIGSEGV, SIGABRT, SIGBUS)
- Swift error capture via `SentrySDK.capture(error:)`
- App hang detection (>2s main thread block)
- Breadcrumbs (navigation, HTTP, UI events)
- dSYM symbolication (upload in CI)

### 2b. Windows — `Sentry.NET`

**File:** `Windows/SyxxUI/SyxxUI.csproj`
```xml
<PackageReference Include="Sentry" Version="4.13.0" />
```

**File:** `Windows/SyxxUI/App.xaml.cs` (modify)
```csharp
using Sentry;

public partial class App : Application
{
    private IDisposable? _sentry;

    public App()
    {
        _sentry = SentrySdk.Init(o =>
        {
            o.Dsn = SyxxConfig.SentryDsn;
            o.Environment = SyxxConfig.BuildEnvironment;
            o.Release = $"syxx-windows@{SyxxConfig.Version}";
            o.TracesSampleRate = 0.1;
            o.AutoSessionTracking = true;
            o.AttachStacktrace = true;
            o.SendDefaultPii = false;
            o.CaptureFailedRequests = true;
        });
        // ... existing OTel init
    }
}
```

**File:** `Windows/Syxx/Program.cs` (CLI entry — add same init)

**What you get automatically:**
- Unhandled .NET exceptions
- Native crashes via `Sentry.Native` (included in package)
- Win32 SEH exceptions
- Breadcrumbs, session tracking

### 2c. Linux — `Sentry.NET`

Same as Windows — `Sentry.NET` works on Linux. Add to `Ubuntu/SyxxUI.Avalonia/` project.

### 2d. Python Server — `sentry-sdk`

**File:** `Server/requirements.txt`
```
sentry-sdk[opentelemetry]>=2.0
```

**File:** `Server/syxx_server/app.py` (add before OTel init)
```python
import sentry_sdk
sentry_sdk.init(
    dsn=config.get("sentry_dsn", ""),
    environment=config.get("environment", "production"),
    release=f"syxx-server@{__version__}",
    traces_sample_rate=0.1,
    send_default_pii=False,
)
```

**What you get automatically:**
- Unhandled Python exceptions
- `http.server` request instrumentation
- Database query spans (if using sqlite3)

---

## Phase 3: OTel Custom Spans (M)

**Goal:** Actually instrument the probe pipelines and server operations with real spans.

### 3a. macOS — DNS probe spans

**File:** `macOS/Sources/SyxxKit/DNSResolver.swift` (and each probe)

```swift
import OpenTelemetryApi

extension DNSResolver {
    func queryWithSpan(hostname: String, type: DNSType) async -> ProbeResult<DNSResponse> {
        let tracer = OpenTelemetry.instance.tracerProvider.get(instrumentationName: "SyxxKit", instrumentationVersion: nil)
        let span = tracer.spanBuilder(spanName: "dns.query")
            .setSpanKind(spanKind: .client)
            .startSpan()
        span.setAttribute(key: "dns.hostname", value: hostname)
        span.setAttribute(key: "dns.type", value: String(type))
        defer { span.end() }

        let result = await query(hostname: hostname, type: type)
        switch result {
        case .success(let response):
            span.setAttribute(key: "dns.answers", value: response.answers.count)
            span.status = .ok
        case .failure(let error):
            span.status = .error(description: "\(error)")
        case .inconclusive(let reason):
            span.setAttribute(key: "dns.inconclusive", value: "\(reason)")
        }
        return result
    }
}
```

**Span hierarchy to create:**

```
syxx.prophet.run
├── dns.query (per resolver)
│   ├── dns.response.parse
│   └── dns.dnssec.verify
├── answer-stability.compare
├── cname-chain.analyze
└── ... (one span per probe)

syxx.mirror.run
├── vantage.compare
│   ├── dns.query (vantage A)
│   └── dns.query (vantage B)
└── content-warp.compare

syxx.portal.run
├── traceroute.execute
└── network.path.monitor

syxx.glutton.run
├── leak.collector.query
└── leak.attestation.verify

syxx.cage.run
├── capture.start
├── capture.packet.process
└── capture.report.generate

syxx.witness.run
├── evidence.sign
└── key.verify
```

### 3b. Windows — same span hierarchy via `System.Diagnostics.Activity`

```csharp
using System.Diagnostics;

private static readonly ActivitySource _source = new("SyxxKit", "1.0.0");

public async Task<ProbeResult<DnsResponse>> QueryAsync(string hostname, DnsType type)
{
    using var activity = _source.StartActivity("dns.query", ActivityKind.Client);
    activity?.SetTag("dns.hostname", hostname);
    activity?.SetTag("dns.type", type.ToString());

    var result = await QueryInternalAsync(hostname, type);
    // ... set status/tags based on result
    return result;
}
```

### 3c. Linux — same as Windows (shared `Syxx.Kit`)

### 3d. Python Server — operation spans

```python
from opentelemetry import trace

tracer = trace.get_tracer("syxx-server")

def handle_leak_query(self, token: str) -> dict:
    with tracer.start_as_current_span("leak.collector.query") as span:
        span.set_attribute("leak.token_prefix", token[:8])
        # ... existing logic
        span.set_attribute("leak.observations", len(observations))
        return result
```

**Server spans to create:**

```
server.request
├── leak.collector.query
├── dns.authoritative.lookup
├── warp.content.compare
├── fetcher.fetch (with SSRF check sub-span)
├── resolver.iterate
├── investigate.consensus
├── licensing.validate
├── stripe.webhook.verify
└── db.query
```

---

## Phase 4: Linux OTel Bootstrap (S)

**Goal:** Instrument the Linux/Avalonia client to match Windows.

**File:** `Ubuntu/SyxxUI.Avalonia/SyxxUI.Avalonia.csproj`
```xml
<PackageReference Include="OpenTelemetry" Version="1.16.0" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.16.0" />
<PackageReference Include="Sentry" Version="4.13.0" />
```

**File:** `Ubuntu/SyxxUI.Avalonia/App.axaml.cs` (add to `OnFrameworkInitializationCompleted`)
```csharp
_tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("SyxxKit")
    .AddOtlpExporter(o =>
    {
        o.Endpoint = new Uri("https://sentry.syxx.app/api/PROJECT_ID/envelope/");
        o.Protocol = OtlpExportProtocol.HttpProtobuf;
    })
    .Build();
```

---

## Phase 5: CI Symbol Upload (S)

**Goal:** Symbolicated stack traces in Sentry for crash reports.

### macOS dSYM upload

**File:** `.github/workflows/ci.yml` (add after `swift build`)
```yaml
- name: Upload dSYMs to Sentry
  if: matrix.os == 'macos-latest'
  run: |
    curl -sL https://sentry.io/get-cli/ | bash
    sentry-cli dif upload \
      --org syxx \
      --project syxx-macos \
      --auth-token ${{ secrets.SENTRY_AUTH_TOKEN }} \
      macOS/.build/arm64-apple-macosx/debug/
```

### Windows PDB upload

```yaml
- name: Upload PDBs to Sentry
  if: matrix.os == 'windows-latest'
  run: |
    sentry-cli dif upload \
      --org syxx \
      --project syxx-windows \
      --auth-token ${{ secrets.SENTRY_AUTH_TOKEN }} \
      Windows/SyxxUI/bin/
```

### Source map for Python

Python stack traces are already readable (no compilation), but Sentry can use source context:
```yaml
- name: Create Sentry release
  run: |
    sentry-cli releases new "syxx-server@${{ github.sha }}"
    sentry-cli releases set-commits "syxx-server@${{ github.sha }}" --auto
    sentry-cli releases finalize "syxx-server@${{ github.sha }}"
```

---

## Phase 6: Alerting & Dashboard (S)

**Goal:** Know when something breaks, before users report it.

### Alert rules

| Rule | Trigger | Action |
|---|---|---|
| New crash type | First occurrence of a new issue | Email + Slack notification |
| Crash rate spike | >5 crashes/hour for any platform | Slack notification |
| Regression | Previously resolved issue reappears | Email notification |
| Server 5xx rate | >1% of requests returning 5xx over 5 min | Slack notification |
| Probe timeout spike | >20% of DNS probes timing out over 5 min | Slack notification |

### Dashboard panels

| Panel | Data source | Shows |
|---|---|---|
| Crash-free sessions | Sentry releases | % of sessions without crashes per release |
| Top crashes | Sentry issues | 10 most frequent unresolved issues |
| Probe latency P50/P95/P99 | OTel spans | DNS query latency percentiles per resolver |
| Server request rate | OTel spans | Requests/second by endpoint |
| Error rate by toolset | OTel spans | Failure rate per toolset (Prophet/Mirror/etc.) |
| Leak collector health | OTel spans | Attestation success/failure/inconclusive ratio |

---

## Phase 7: MCP Integration (S)

**Goal:** Let agents query Sentry directly during debugging workflows.

Install the Sentry MCP extension:
```bash
qwen extensions install https://github.com/getsentry/sentry-mcp
```

This gives `syxx-neural-debugger` and `syxx-researcher` direct access to:
- Search issues by platform, release, or error signature
- Read full stack traces with symbolicated frames
- Check crash trends across releases
- Read breadcrumbs leading to a crash

---

## Sequencing & Dependencies

```
Phase 1 (Deploy)          ── no dependencies
Phase 2 (Sentry SDKs)     ── depends on Phase 1 (DSNs available)
Phase 3 (OTel spans)      ── independent of Phases 1-2 (can parallel)
Phase 4 (Linux bootstrap) ── depends on Phase 2b/2c (SDK pattern established)
Phase 5 (CI symbols)      ── depends on Phase 1 (Sentry running)
Phase 6 (Alerting)        ── depends on Phase 1 + Phase 2 (data flowing)
Phase 7 (MCP)             ── depends on Phase 1 + Phase 2 (issues exist to query)
```

**Recommended order:** 1 → 2 (all platforms) → 5 → 3 → 4 → 6 → 7

## Files to Create or Modify

| File | Action | Phase |
|---|---|---|
| `macOS/Package.swift` | Add `sentry-cocoa` dependency | 2 |
| `macOS/Sources/SyxxApp/SentryBootstrap.swift` | Create | 2 |
| `macOS/Sources/SyxxApp/SyxxApp.swift` | Add Sentry init call | 2 |
| `macOS/Sources/SyxxKit/DNSResolver.swift` | Add span instrumentation | 3 |
| `macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift` | Add span instrumentation | 3 |
| `Windows/SyxxUI/SyxxUI.csproj` | Add `Sentry` NuGet | 2 |
| `Windows/SyxxUI/App.xaml.cs` | Add Sentry init | 2 |
| `Windows/Syxx/Program.cs` | Add Sentry init (CLI) | 2 |
| `Windows/Syxx.Kit/DNSResolver.cs` | Add Activity spans | 3 |
| `Ubuntu/SyxxUI.Avalonia/*.csproj` | Add OTel + Sentry NuGets | 4 |
| `Ubuntu/SyxxUI.Avalonia/App.axaml.cs` | Add OTel + Sentry bootstrap | 4 |
| `Server/requirements.txt` | Add `sentry-sdk[opentelemetry]` | 2 |
| `Server/syxx_server/app.py` | Add `sentry_sdk.init()` | 2 |
| `Server/syxx_server/leak.py` | Add operation spans | 3 |
| `Server/syxx_server/warp.py` | Add operation spans | 3 |
| `Server/syxx_server/resolver.py` | Add operation spans | 3 |
| `.github/workflows/ci.yml` | Add dSYM/PDB upload, release creation | 5 |

## Cost

| Component | Cost |
|---|---|
| Sentry self-hosted | Your infrastructure (Docker host with 4GB+ RAM) |
| Sentry SDKs | Free (MIT licensed) |
| OTel SDKs | Free (Apache 2.0) |
| Sentry MCP | Free |
| **Total** | **Infrastructure only** |

---

## GUI Display Observability (Phase 8)

### GUI Audit Findings

A thorough audit of both macOS (SwiftUI) and Windows (WinUI 3/XAML) views identified **17 layout instability issues** across 5 high-severity, 7 medium-severity, and 5 low-severity findings.

**Root cause pattern:** Conditional view insertion via `if let` / `if` blocks that swap small placeholders with large content blocks when async data arrives. Since the window is size-locked (1050×613 macOS, 1050×700 Windows), these don't resize the window but cause content to appear/disappear abruptly and push elements below the fold.

#### High-severity layout instability

| ID | View | File:Line | Issue |
|---|---|---|---|
| H1 | ProphetView | `ProphetView.swift:85-107` | Result card (300+pt) replaces empty-state text (~34pt) |
| H2 | MirrorView | `MirrorView.swift:80-199` | Both columns independently swap empty→full, growing in steps |
| H3 | WitnessView | `WitnessView.swift:107-195` | Two large conditional sections appear sequentially |
| H4 | ConclusionsView | `ConclusionsView.swift:60-162` | Entire view hierarchy swaps (STAGE 1 → STAGE 2) |
| H5 | OverallVerdictView | `OverallVerdictView.swift:84,217,253` | Index-based `ForEach(0..<count)` risks row recycling |

#### Medium-severity layout instability

| ID | View | File:Line | Issue |
|---|---|---|---|
| M6 | PortalView | `PortalView.swift:144-177` | Traceroute has 5 different layout sizes across states |
| M7 | CageView | `CageView.swift:100-104` | Output message conditionally appears after capture |
| M8 | CageView | `CageView.swift:115-153` | Terminal swaps between 20pt placeholder and 180pt ScrollView |
| M9 | DashboardView | `DashboardView.swift:262-266` | Cage tile taller than other tiles in LazyVGrid |
| M10 | Windows MirrorView | `MirrorView.xaml` | Three visibility-collapsed panels swap simultaneously |
| M11 | Windows ConclusionsView | `ConclusionsView.xaml` | VerdictPanel vs EvidencePanel full-page swap |
| M12 | DashboardView | `DashboardView.swift:451-483` | Activity log swaps from static text to ScrollView |

### GUI Fixes (in priority order)

1. **Reserve vertical space** — give every conditional section `.frame(minHeight:)` matching the populated state's height. This turns layout jumps into internal content swaps.

2. **Always render container, swap internal content** — instead of `if let result { BigCard(result) } else { SmallPlaceholder() }`, render `BigCard { if let result { resultContent } else { placeholder } }`.

3. **Stable ForEach IDs** — give `EvidenceItem`, `Diagnosis`, `SuppressedSignal` stable `Identifiable` conformance instead of `ForEach(0..<count, id: \.self)`.

4. **Crossfade for ConclusionsView** — the most dramatic swap in the app. A 0.2s `.transition(.opacity)` eliminates the visual jolt.

### GUI Observability Instrumentation

#### View lifecycle spans

Add OTel spans when each toolset view appears and disappears, capturing how long users spend in each view:

```swift
// In each toolset view's onAppear/onDisappear
.onAppear {
    SyxxTracer.span("gui.view.appear", attributes: [
        "gui.view": .string("Prophet"),
        "gui.platform": .string("macos")
    ]) { _ in }
}
```

Span hierarchy:
```
gui.session
├── gui.view.prophet (onAppear → onDisappear)
├── gui.view.mirror
├── gui.view.portal
├── gui.view.glutton
├── gui.view.cage
├── gui.view.witness
├── gui.view.dashboard
├── gui.view.conclusions
└── gui.view.settings
```

#### Sentry breadcrumbs for state transitions

```swift
// When a toolset run completes and the view updates
SentrySDK.addBreadcrumb(crumb: Breadcrumb(
    level: .info,
    category: "gui.state",
    message: "Prophet completed: \(verdict)",
    data: ["toolset": "prophet", "verdict": "\(verdict)"]
))
```

#### App Hang detection (already enabled)

Sentry's `enableAppHangTracking = true` with `appHangTimeoutInterval = 2.0` automatically captures main-thread blocks >2s — the most common cause of GUI jank during async operations.

#### Layout shift metrics (future)

For each high-severity finding, track whether the fix has been applied:
- Before fix: conditional views cause layout shift → Sentry breadcrumb on state change
- After fix: reserved space prevents shift → no breadcrumb needed

### Files to create or modify (Phase 8)

| File | Action | Issue |
|---|---|---|
| `macOS/Sources/SyxxApp/ProphetView.swift` | Add `.frame(minHeight: 300)` to result area | H1 |
| `macOS/Sources/SyxxApp/MirrorView.swift` | Add `.frame(minHeight: 280)` to each column | H2 |
| `macOS/Sources/SyxxApp/WitnessView.swift` | Always render sections with placeholder content | H3 |
| `macOS/Sources/SyxxApp/ConclusionsView.swift` | Add `.transition(.opacity)` to stage swap | H4 |
| `macOS/Sources/SyxxApp/Views/Components/OverallVerdictView.swift` | Add `Identifiable` to model types | H5 |
| `macOS/Sources/SyxxApp/PortalView.swift` | Fix `.frame(minHeight: 200)` on traceroute | M6 |
| `macOS/Sources/SyxxApp/CageView.swift` | Always render ScrollView, placeholder inside | M7+M8 |
| `macOS/Sources/SyxxApp/DashboardView.swift` | Equalize warden tile heights | M9 |
| `Windows/SyxxUI/Views/MirrorView.xaml` | Add `MinHeight="280"` to content Borders | M10 |
| `Windows/SyxxUI/Views/ConclusionsView.xaml` | Smooth transition for panel swap | M11 |
| All toolset views (macOS) | Add view lifecycle spans | Observability |
| `macOS/Sources/SyxxApp/AppModel.swift` | Add Sentry breadcrumbs on state transitions | Observability |
