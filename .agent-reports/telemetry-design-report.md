# Syxx Telemetry Design Report — GUI Element Stability Monitor & Observability Audit

**Date:** 2026-07-27
**Author:** performance-monitor subagent
**Scope:** Full observability audit + GUI jiggle/drift monitor design + span placement recommendations
**Status:** Complete, awaiting review

---

## Table of Contents

1. [Current State of Observability](#1-current-state-of-observability)
2. [GUI Element Stability Monitor Design](#2-gui-element-stability-monitor-design)
3. [Span Design for Meaningful, Traceable Telemetry](#3-span-design-for-meaningful-traceable-telemetry)
4. [Specific File:Line Locations for New Spans/Breadcrumbs](#4-specific-fileline-locations)
5. [Conflicts with UNIFIED_IMPLEMENTATION_PLAN.md Phase 4.2](#5-conflicts-with-unified_implementation_planmd-phase-42)
6. [Implementation Sequencing](#6-implementation-sequencing)

---

## 1. Current State of Observability

### 1.1 What is Wired

| Platform | Bootstrap | Actual Spans | Sentry SDK | Crash Capture | Hangs |
|----------|-----------|-------------|------------|---------------|-------|
| macOS (SyxxApp) | `OTelBootstrap.swift` + `SentryBootstrap.swift` | **1 span** — `syxx.synthesis.synthesize` in `SynthesisEngine.swift:34` | `sentry-cocoa` 8.43.0 | ✅ Mach exceptions | ✅ >2s hang detection |
| Windows (SyxxUI) | `App.xaml.cs:46-55` | **0 spans** — TracerProvider listens for source "SyxxUI" but no ActivitySource exists | `Sentry.NET` 4.13.0 | ✅ .NET exceptions | ❌ |
| Windows CLI (Syxx) | `Program.cs:17-20` — Sentry only | **0 spans** — no OTel bootstrap | `Sentry.NET` 4.13.0 | ✅ | ❌ |
| Linux (Avalonia) | ❌ None | **0 spans** | ❌ None | ❌ | ❌ |
| Python Server | `app.py:57-77` — OTel + `app.py:43-46` — Sentry | **0 custom spans** | `sentry-sdk` 2.19+ | ✅ Python exceptions | ❌ |

### 1.2 What is Dead Code

| Item | File:Line | Problem |
|------|-----------|---------|
| Windows TracerProvider | `App.xaml.cs:49` | `.AddSource("SyxxUI")` — no `ActivitySource` named "SyxxUI" exists anywhere in the codebase. The TracerProvider is registered but will never export a span. |
| macOS SyxxTracer variants | `SyxxTracer.swift:28-67` (throwing), `:69-87` (non-throwing) | Only `spanSync` (line 89) is used. The async variants are dead code. |
| macOS NoOpSpan | `SyxxTracer.swift:104-131` | Full implementation of a no-op span for when OTel is not initialized. Works correctly, but the only code path that hits it is test runs. |
| Server tracer provider | `app.py:74-77` | `TracerProvider` is registered but zero custom spans are created anywhere in `resolver.py`, `leak.py`, `warp.py`, `investigate.py`, or `licensing.py`. |

### 1.3 What is Missing (Critical Gaps)

| Gap | Severity | Description |
|-----|----------|-------------|
| **No spans in probe pipelines** | CRITICAL | DNSResolver, ContentWarp, VantageComparer, LeakDetector, DNSCapture, WitnessSigner — none of these create spans. The exporter has nothing to export. |
| **No Windows ActivitySource** | CRITICAL | `Syxx.Kit` has no `ActivitySource` definition. OBS-1 in the plan is accurate. |
| **No span correlation client↔server** | HIGH | No W3C `traceparent` header propagation. Server spans are islands. |
| **No exporter health check** | HIGH | If the OTLP exporter fails to connect to `bugsmasher.syxx.app`, no alert fires. |
| **No shutdown flush** | HIGH | On app termination, in-flight spans may be lost. Neither `SentryBootstrap` nor `OTelBootstrap` call `OpenTelemetry.instance.tracerProvider.forceFlush()`. |
| **No sampling strategy** | MEDIUM | `tracesSampleRate = 0.1` is set on Sentry but OTel has no sampling configured — every span is exported. At scale, this could overwhelm the collector. |
| **No Linux telemetry** | MEDIUM | Avalonia client has zero instrumentation. |
| **No macOS CLI telemetry** | MEDIUM | CLI tools (`syxx`, `syxx-nslook`, etc.) have no OTel bootstrap. |
| **No GUI element stability monitoring** | MEDIUM | No CADisplayLink, CompositionTarget.Rendering, or layout-change tracking. The user's concern about "jiggling" has no telemetry backing. |

### 1.4 Telemetry Meaningfulness Assessment

The user's emphasis on **meaningful, not just voluminous, telemetry** is well-founded. The current state is:

- **Volume without meaning:** If we simply add spans to every function, we produce noise. The exporter would be flooded with 50+ spans per run, most of which are trivial (e.g., a 2ms string format).
- **No causal chain:** A span for `dns.query` exists in isolation. There is no parent span that ties it to the user action ("clicked Scan in Prophet"), the synthesis run, and the final verdict displayed.
- **No root-cause path:** If a user sees "Inconclusive" on the dashboard, there is no telemetry that traces that verdict back through the engine to the specific missing/invalidated section.

**The fix:** Spans must be organized into a **causal hierarchy** where each span answers "what happened, why, and what was the outcome?" — not just "this function was called."

---

## 2. GUI Element Stability Monitor Design

### 2.1 Problem Statement

The user wants telemetry to detect when GUI elements are "jiggling" or "drifting" — visually unstable layout. The Syxx window is hard-locked at 1050×613 (macOS) and 1050×700 (Windows), so the window itself cannot resize. However, internal content can:

1. **Conditional view swaps** (the 17 findings in OBSERVABILITY-PLAN.md Phase 8) — `if let result { BigCard } else { SmallPlaceholder }` causes content to jump.
2. **Async data arrival** — when a DNS probe completes, the result card replaces the empty state, potentially shifting sibling elements.
3. **ScrollView content changes** — CageView's terminal, activity log in DashboardView — new lines push existing content.
4. **Animation glitches** — Spring animations that overshoot, layout during transitions.

### 2.2 macOS Design — CADisplayLink-Based Stability Monitor

#### Architecture

```
GUILayoutStabilityMonitor (new class in SyxxKit)
├── CADisplayLink (per-window, 60fps)
├── Per-element frame tracking (origin + size)
├── Drift detection (5-frame sliding window)
├── Sentry event on threshold breach
└── OTel span on layout instability
```

#### Key Design Decisions

1. **Track specific elements, not all views.** We track the origin of key "anchor" views that should remain stable: sidebar frame, content area top-left, verdict badge position, and the overall window content rect. Tracking every view would be noisy and expensive.

2. **Drift = cumulative delta across 5 consecutive frames.** A single-frame 1px shift is normal (rounding, subpixel layout). A 5-frame monotonic drift of >2px in any direction is a layout instability signal.

3. **Ignore scroll-driven changes.** ScrollView content offsets change intentionally. We track only the *frame origin* of the ScrollView itself (its position in the window), not its content offset.

4. **Fire a Sentry event, not a crash.** A layout instability is not a crash — it is a quality signal. Sentry's `captureMessage` with level `warning` is the right channel.

5. **Telemetry is opt-in, gated behind `SYXX_TELEMETRY_GUI_LAYOUT=1`.** This is not a privacy concern (we track view frame origins, not user content), but it adds a 60fps callback overhead. Default off except in developer builds.

#### File: `macOS/Sources/SyxxKit/GUILayoutStabilityMonitor.swift` (new)

```swift
import Foundation
import QuartzCore  // CADisplayLink
import CoreGraphics

/// Monitors key view frame origins for layout instability ("jiggle" / "drift").
/// Uses CADisplayLink at 60fps to sample per-frame origin deltas.
/// Fires a Sentry breadcrumb on drift >2px across 5 consecutive frames.
/// Telemetry is opt-in via SYXX_TELEMETRY_GUI_LAYOUT=1 env var.
public final class GUILayoutStabilityMonitor: @unchecked Sendable {
    public struct AnchoredElement: Sendable {
        public let label: String        // e.g., "sidebar", "content-area", "verdict-badge"
        public let initialFrame: CGRect
    }

    public struct DriftEvent: Sendable {
        public let elementLabel: String
        public let dx: CGFloat
        public let dy: CGFloat
        public let frameCount: Int
        public let timestamp: Date
    }

    /// Elements to track. Each is keyed by a stable label.
    private let elements: [String: CGRect]
    /// 5-frame sliding window of origins per element.
    private var history: [String: [CGPoint]] = [:]
    private let historySize = 5
    private let driftThreshold: CGFloat = 2.0  // pixels

    private var displayLink: CADisplayLink?
    private let onDrift: (DriftEvent) -> Void

    /// Initialize with the elements to monitor. `onDrift` is called on the main thread
    /// when a drift threshold is breached.
    public init(elements: [AnchoredElement], onDrift: @escaping (DriftEvent) -> Void) {
        self.elements = Dictionary(uniqueKeysWithValues: elements.map { ($0.label, $0.initialFrame) })
        self.onDrift = onDrift
        for label in self.elements.keys {
            history[label] = []
        }
    }

    /// Start the display link. Must be called from the main thread.
    public func start() {
        guard displayLink == nil else { return }
        let link = CADisplayLink(target: self, selector: #selector(tick))
        link.preferredFrameRateRange = CAFrameRateRange(minimum: 30, maximum: 60, preferred: 60)
        link.add(to: .main, forMode: .common)
        displayLink = link
    }

    /// Stop the display link. Must be called from the main thread.
    public func stop() {
        displayLink?.invalidate()
        displayLink = nil
    }

    /// Called by the display link on each frame. Reads the current frame origin of
    /// each anchored element and compares against the sliding window.
    @objc private func tick(_ link: CADisplayLink) {
        // Re-read element frames from the view hierarchy.
        // In practice, this requires the caller to update element frames
        // via `updateFrame(for:frame:)` before each tick, since CADisplayLink
        // cannot directly query SwiftUI view frames.
        // Alternative: use GeometryReader + PreferenceKey to push frame updates
        // to the monitor, then compare on tick.
        checkDrift()
    }

    /// Update the current frame for a tracked element. Called from SwiftUI
    /// GeometryReader callbacks.
    public func updateFrame(for label: String, frame: CGRect) {
        guard var points = history[label] else { return }
        points.append(frame.origin)
        if points.count > historySize {
            points.removeFirst()
        }
        history[label] = points
    }

    private func checkDrift() {
        for (label, points) in history {
            guard points.count == historySize else { continue }
            let first = points.first!
            let last = points.last!
            let dx = abs(last.x - first.x)
            let dy = abs(last.y - first.y)
            if dx > driftThreshold || dy > driftThreshold {
                onDrift(DriftEvent(
                    elementLabel: label,
                    dx: last.x - first.x,
                    dy: last.y - first.y,
                    frameCount: historySize,
                    timestamp: Date()
                ))
            }
        }
    }
}
```

#### Integration Points in SwiftUI (macOS)

The monitor needs to read actual frame origins. SwiftUI's `GeometryReader` + `PreferenceKey` is the standard pattern:

1. **`ContentView.swift`** — wrap the sidebar and content area in `GeometryReader` + `.background(GeometryPreferenceKey.self)` to push frame origins to the monitor.
2. **`OverallVerdictView.swift`** — track the verdict badge position.
3. **`DashboardView.swift`** — track the trust ring position.

The monitor is created in `AppModel` and started/stopped in `SyxxAppDelegate.applicationDidFinishLaunching`.

#### Breadcrumb Format (Sentry)

```swift
SentrySDK.addBreadcrumb(Breadcrumb(
    level: .warning,
    category: "gui.layout",
    message: "Layout drift: \(event.elementLabel) moved \(event.dx)×\(event.dy)pt over \(event.frameCount) frames",
    data: [
        "element": event.elementLabel,
        "dx": event.dx,
        "dy": event.dy,
        "frame_count": event.frameCount,
        "timestamp": event.timestamp.timeIntervalSince1970
    ]
))
```

### 2.3 Windows Design — CompositionTarget.Rendering-Based Stability Monitor

#### Architecture

```
GUILayoutStabilityMonitor (new class in Syxx.Kit)
├── CompositionTarget.Rendering event (per-frame)
├── Per-element transform tracking (GeneralTransform)
├── Drift detection (5-frame sliding window)
└── Sentry breadcrumb on threshold breach
```

#### Key Differences from macOS

1. **WinUI 3 uses `CompositionTarget.Rendering`** instead of `CADisplayLink`. It fires on every composition frame.
2. **Frame positions are read via `UIElement.TransformToVisual(Window.Current.Content)`** — this gives the element's position relative to the window root.
3. **The monitor must be on the UI thread** (CompositionTarget.Rendering fires on the UI thread).
4. **No GeometryReader equivalent** — we use `UIElement.ActualOffset` (or `TransformToVisual`) to read positions.

#### File: `Windows/dotNET/Syxx.Kit/GUILayoutStabilityMonitor.cs` (new)

```csharp
using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Syxx.Kit;

/// <summary>
/// Windows-parity twin of macOS GUILayoutStabilityMonitor.
/// Monitors key UIElement positions for layout instability using CompositionTarget.Rendering.
/// Fires a Sentry breadcrumb on drift >2px across 5 consecutive frames.
/// </summary>
public sealed class GUILayoutStabilityMonitor : IDisposable
{
    public sealed record AnchoredElement(string Label, UIElement Element);
    public sealed record DriftEvent(string Label, double Dx, double Dy, int FrameCount, DateTime Timestamp);

    private readonly Dictionary<string, UIElement> _elements = new();
    private readonly Dictionary<string, Queue<Windows.Foundation.Point>> _history = new();
    private readonly int _historySize = 5;
    private readonly double _driftThreshold = 2.0;
    private readonly Action<DriftEvent> _onDrift;
    private bool _isRunning;

    public GUILayoutStabilityMonitor(IEnumerable<AnchoredElement> elements, Action<DriftEvent> onDrift)
    {
        _onDrift = onDrift;
        foreach (var e in elements)
        {
            _elements[e.Label] = e.Element;
            _history[e.Label] = new Queue<Windows.Foundation.Point>(_historySize);
        }
    }

    public void Start()
    {
        if (_isRunning) return;
        CompositionTarget.Rendering += OnRendering;
        _isRunning = true;
    }

    public void Stop()
    {
        CompositionTarget.Rendering -= OnRendering;
        _isRunning = false;
    }

    private void OnRendering(object? sender, object e)
    {
        foreach (var (label, element) in _elements)
        {
            try
            {
                // TransformToVisual gives position relative to the window root
                var transform = element.TransformToVisual(Window.Current.Content);
                var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                var queue = _history[label];
                queue.Enqueue(point);
                if (queue.Count > _historySize)
                    queue.Dequeue();
                if (queue.Count == _historySize)
                {
                    var first = queue.Peek();
                    var last = point;
                    var dx = Math.Abs(last.X - first.X);
                    var dy = Math.Abs(last.Y - first.Y);
                    if (dx > _driftThreshold || dy > _driftThreshold)
                    {
                        _onDrift(new DriftEvent(label, last.X - first.X, last.Y - first.Y, _historySize, DateTime.UtcNow));
                    }
                }
            }
            catch
            {
                // Element may not be in the visual tree yet — skip
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _elements.Clear();
        _history.Clear();
    }
}
```

#### Integration Points in WinUI 3 (Windows)

1. **`MainWindow.xaml`** — give `x:Name` to the sidebar `Grid`, the content `Frame`, and the verdict panel.
2. **`MainWindow.xaml.cs`** — create the monitor in `OnLaunched`, pass named elements, start.
3. **`AppViewModel`** — wire the `onDrift` callback to `SentrySdk.AddBreadcrumb`.

### 2.4 Threshold Calibration

The default `driftThreshold = 2.0px` and `historySize = 5` frames are conservative starting points. These should be calibrated with real data:

| Parameter | Default | Tuning Guidance |
|-----------|---------|-----------------|
| `driftThreshold` | 2.0px | Increase if false positives on subpixel antialiasing. Decrease if jiggle is visible but under 2px. |
| `historySize` | 5 frames | Larger windows are less sensitive to transient bumps. Smaller windows catch faster. |
| `frameRate` | 60fps | Lower to 30fps on battery. The monitor is cheap (point comparisons) but 60fps is a continuous callback. |

### 2.5 Opt-In Gating

The monitor must be **opt-in** for three reasons:

1. **Performance overhead** — 60fps callback per frame, even though cheap, is continuous work.
2. **Privacy** — while we track only frame origins (not user data), users deserve control.
3. **Battery** — on laptops, 60fps callbacks prevent the display from entering low-power modes.

Gate mechanism:

- **macOS:** `UserDefaults.standard.bool(forKey: "SYXX_TELEMETRY_GUI_LAYOUT")` — defaults to `false` outside `DEBUG` builds.
- **Windows:** `SyxxSettings.GuiLayoutTelemetryEnabled` — defaults to `false` outside `DEBUG` builds.
- **CLI env var:** `SYXX_TELEMETRY_GUI_LAYOUT=1` for both platforms.

---

## 3. Span Design for Meaningful, Traceable Telemetry

### 3.1 The Causal Hierarchy Principle

Every span must answer three questions:

1. **What happened?** — the operation name (e.g., `dns.query.udp`)
2. **Why was it needed?** — the parent span (e.g., `prophet.resolver.probe` → `dns.query.udp`)
3. **What was the outcome?** — span status + attributes (e.g., `.ok` with `dns.answers=4`, or `.error` with `dns.timeout=5000ms`)

A span that just says "this function was called" is noise. A span that says "Prophet queried 8.8.8.8 for AAAA github.com, got 2 answers in 47ms" is actionable.

### 3.2 Span Hierarchy by Toolset

#### Prophet (DNS Resolution)

```
prophet.run                          [user clicked "Scan" in Prophet]
├── prophet.resolver.probe           [per resolver: 8.8.8.8]
│   ├── dns.query.udp               [actual UDP query]
│   │   ├── dns.packet.encode       [encode DNS wire format]
│   │   └── dns.packet.decode       [decode response]
│   ├── dns.query.doh               [DoH fallback query]
│   └── dns.response.validate       [validate RCODE, TC bit, truncation]
├── prophet.consensus.compare        [compare answers across resolvers]
├── prophet.dnssec.check             [DNSSEC presence check]
└── prophet.synthesis.evaluate       [matrix engine evaluation of prophet evidence]
```

**Meaningful attributes:**
- `dns.hostname` — the domain being queried
- `dns.type` — A, AAAA, MX, etc.
- `dns.resolver` — the resolver IP (not the user's DNS server; that's `dns.resolver.local`)
- `dns.answers` — count of answer RRs
- `dns.latency_ms` — round-trip time
- `dns.rcode` — NOERROR, NXDOMAIN, SERVFAIL, etc.
- `dns.transport` — udp, tcp, doh, dot
- `dns.encrypted` — true/false (was transport encrypted?)

#### Mirror (Tampering Detection)

```
mirror.run                           [user clicked "Scan" in Mirror]
├── mirror.vantage.local             [local DNS query]
│   └── dns.query.udp (parent)
├── mirror.vantage.remote            [server-side query via Dimension API]
│   └── http.fetch (parent)
├── mirror.content.compare           [byte-identity comparison of fetched content]
│   ├── mirror.content.fetch.local   [local fetch of JS/CSS via proxy]
│   └── mirror.content.fetch.remote  [remote fetch via server proxy]
└── mirror.synthesis.evaluate        [matrix engine evaluation of mirror evidence]
```

**Meaningful attributes:**
- `mirror.domain` — the domain being compared
- `mirror.local_answers` — count of local DNS answers
- `mirror.remote_answers` — count of remote DNS answers
- `mirror.content_hash_match` — true/false/inconclusive
- `mirror.content_size_delta` — byte difference between local and remote fetch

#### Portal (Network Diagnostics)

```
portal.run
├── portal.traceroute.execute        [system traceroute]
├── portal.lan.discover              [LAN cartography]
├── portal.dns.resolver.detect       [detect current DNS resolver]
│   ├── dns.reverse.ptr              [reverse DNS for resolver IP]
│   └── dns.query.txt                [resolver identification query]
├── portal.ip.classify               [IPClassifier: public/private/CGNAT]
└── portal.synthesis.evaluate
```

#### Glutton (Leak Detection)

```
glutton.run
├── glutton.leak.collector.query     [query leak.syxx.app]
├── glutton.leak.attestation.verify  [verify signed attestation payload]
│   └── witness.signature.verify     [Ed25519 signature verification]
├── glutton.resolver.expected        [compare observed resolver to expected]
└── glutton.synthesis.evaluate
```

#### Cage (Capture/Forensics)

```
cage.run
├── cage.capture.start               [start packet capture]
├── cage.capture.packet.process      [per-packet processing loop]
│   ├── cage.packet.decode           [DNS wire format decode]
│   └── cage.packet.classify        [process attribution, severity]
├── cage.capture.stop                [stop capture, flush buffers]
├── cage.report.generate             [forensic report generation]
└── cage.synthesis.evaluate
```

#### Witness (Evidence/Trust)

```
witness.run
├── witness.snapshot.collect         [collect environment snapshot]
├── witness.signature.create         [Ed25519 sign the snapshot]
├── witness.signature.verify         [verify the signature]
├── witness.key.check                [key presence in Keychain/DPAPI]
└── witness.synthesis.evaluate
```

#### Cross-Cutting (GUI Session)

```
gui.session                          [app launch → app quit]
├── gui.view.appear                  [onAppear, per toolset]
│   ├── gui.view.prophet
│   ├── gui.view.mirror
│   ├── gui.view.portal
│   ├── gui.view.glutton
│   ├── gui.view.cage
│   ├── gui.view.witness
│   ├── gui.view.dashboard
│   ├── gui.view.conclusions
│   └── gui.view.settings
├── gui.scan.start                   [user clicks "Scan" button]
│   └── (toolset span as child)
└── gui.verdict.display              [verdict rendered to screen]
```

### 3.3 The Correlation ID Pattern

Every user-initiated scan gets a **correlation ID** (UUID v4) that flows through:

1. GUI: `gui.scan.start` → sets `scan.correlation_id` attribute
2. Each toolset: `prophet.run` → inherits `scan.correlation_id`
3. Each probe: `dns.query.udp` → inherits `scan.correlation_id`
4. Server: `server.request` → receives `scan.correlation_id` as HTTP header `X-Syxx-Correlation-Id`
5. Synthesis: `syxx.synthesis.synthesize` → reads `scan.correlation_id` from evidence

This means a user report of "my scan said inconclusive" can be traced from the GUI click through every DNS query to the final synthesis verdict in a single trace view in Sentry.

### 3.4 Breadcrumbs vs. Spans — When to Use Each

| Signal | Use Span | Use Breadcrumb |
|--------|----------|----------------|
| DNS query latency | ✅ Span with `dns.latency_ms` attribute | ❌ |
| User clicked "Scan" | ✅ Span (root of trace) | ✅ Breadcrumb (navigation context) |
| View appeared/disappeared | ✅ Span (session duration) | ✅ Breadcrumb (navigation path) |
| Layout drift >2px | ❌ | ✅ Breadcrumb (quality signal, not operation) |
| Scan completed with verdict | ✅ Span with `verdict` attribute | ✅ Breadcrumb (state transition) |
| Crash/exception | ❌ | ✅ Sentry event (automatic) |
| App hang (>2s) | ❌ | ✅ Sentry event (automatic) |
| License validation | ✅ Span with `license.valid` attribute | ✅ Breadcrumb (auth state) |

**Rule of thumb:** If it measures a discrete operation with a start and end, use a span. If it is a point-in-time signal that provides context for debugging, use a breadcrumb. Both are valuable; they serve different purposes.

---

## 4. Specific File:Line Locations for New Spans/Breadcrumbs

### 4.1 macOS — SyxxKit (Shared Library)

| File | Line | What to Add | Span Name |
|------|------|-------------|-----------|
| `DNSResolver.swift:45` (approx — `query()` entry) | Wrap `query()` in span | `dns.query` |
| `DNSResolver.swift:120` (approx — `queryOverDoH()`) | Wrap DoH path | `dns.query.doh` |
| `ContentWarp.swift:85` (approx — `fetchContent()`) | Wrap fetch in span | `content.fetch` |
| `ContentWarp.swift:180` (approx — `compare()`) | Wrap comparison | `content.compare` |
| `VantageComparer.swift:30` (approx — `compareVantagePoints()`) | Wrap comparison | `vantage.compare` |
| `LeakDetector.swift:25` (approx — `detectLeak()`) | Wrap detection | `leak.detect` |
| `WitnessSigner.swift:40` (approx — `sign()`) | Wrap signing | `witness.sign` |
| `WitnessSigner.swift:90` (approx — `verify()`) | Wrap verification | `witness.verify` |
| `DNSCapture.swift:55` (approx — `startCapture()`) | Wrap capture | `capture.start` |
| `DNSCapture.swift:120` (approx — `stopCapture()`) | Wrap stop | `capture.stop` |
| `IPClassifier.swift:15` (approx — `classify()`) | Wrap classification | `ip.classify` |
| `SynthesisEngine.swift:34` | **Already has span** `syxx.synthesis.synthesize` | ✅ Done |
| `MatrixDeterminativeEngine.swift:30` (approx — `evaluate()`) | Add child span under synthesis | `synthesis.matrix.evaluate` |

### 4.2 macOS — SyxxApp (GUI Layer)

| File | Line | What to Add | Span/Breadcrumb |
|------|------|-------------|-----------------|
| `SyxxApp.swift:12` | Add `gui.session` span start | `gui.session` (root span) |
| `SyxxApp.swift:82` (app terminate) | End `gui.session` span | — |
| `ContentView.swift:44` (onAppear) | Add `gui.view.appear` breadcrumb | Breadcrumb |
| `ProphetView.swift:30` (onAppear) | Add `gui.view.prophet` span | `gui.view.prophet` |
| `ProphetView.swift:80` (scan button) | Add `gui.scan.start` span | `gui.scan.start` |
| `ProphetView.swift:107` (result display) | Add `gui.verdict.display` breadcrumb | Breadcrumb |
| `MirrorView.swift:30` (onAppear) | Add `gui.view.mirror` span | `gui.view.mirror` |
| `PortalView.swift:30` (onAppear) | Add `gui.view.portal` span | `gui.view.portal` |
| `GluttonView.swift:30` (onAppear) | Add `gui.view.glutton` span | `gui.view.glutton` |
| `CageView.swift:30` (onAppear) | Add `gui.view.cage` span | `gui.view.cage` |
| `WitnessView.swift:30` (onAppear) | Add `gui.view.witness` span | `gui.view.witness` |
| `DashboardView.swift:50` (onAppear) | Add `gui.view.dashboard` span | `gui.view.dashboard` |
| `ConclusionsView.swift:35` (onAppear) | Add `gui.view.conclusions` span | `gui.view.conclusions` |
| `OverallVerdictView.swift:84` (verdict render) | Add `gui.verdict.display` breadcrumb | Breadcrumb |
| `AppModel.swift:200` (approx — `runScan()`) | Generate correlation ID, set attribute | `scan.correlation_id` attribute |
| `AppModel.swift:500` (approx — `setMonitoring()`) | Add `gui.monitor.toggle` breadcrumb | Breadcrumb |

### 4.3 Windows — Syxx.Kit (Shared Library)

| File | Line | What to Add | Activity Name |
|------|------|-------------|---------------|
| `Syxx.Kit/SyxxTracer.cs` | **NEW FILE** | Create `ActivitySource` "SyxxKit" | `ActivitySource` definition |
| `DNSResolver.cs:50` (approx — `QueryAsync()`) | Wrap in `Activity` | `dns.query` |
| `DNSResolver.cs:160` (approx — DoH path) | Wrap in `Activity` | `dns.query.doh` |
| `ContentWarp.cs:45` (approx — `FetchContentAsync()`) | Wrap in `Activity` | `content.fetch` |
| `VantageComparer.cs:30` (approx) | Wrap in `Activity` | `vantage.compare` |
| `LeakDetector.cs:25` (approx) | Wrap in `Activity` | `leak.detect` |
| `WitnessSigner.cs:40` (approx) | Wrap in `Activity` | `witness.sign` |
| `SynthesisEngine.cs:85` (approx — `Conclude()`) | Wrap in `Activity` | `syxx.synthesis.synthesize` |
| `MatrixDeterminativeEngine.cs:30` (approx) | Wrap in `Activity` | `synthesis.matrix.evaluate` |

### 4.4 Windows — SyxxUI (GUI Layer)

| File | Line | What to Add | Activity/Breadcrumb |
|------|------|-------------|---------------------|
| `App.xaml.cs:49` | **FIX:** Change `.AddSource("SyxxUI")` to `.AddSource("SyxxKit")` | OBS-2 fix |
| `App.xaml.cs:21` (after Sentry init) | Add `gui.session` Activity start | `gui.session` |
| `MainWindow.xaml.cs:OnLaunched` | Add `gui.view.appear` breadcrumb | Breadcrumb |
| `AppViewModel.cs:100` (approx — `RunProphetAsync()`) | Generate correlation ID, wrap in Activity | `gui.scan.start` |
| `DashboardView.xaml.cs:OnNavigatedTo` | Add `gui.view.dashboard` Activity | `gui.view.dashboard` |
| `ProphetView.xaml.cs:OnNavigatedTo` | Add `gui.view.prophet` Activity | `gui.view.prophet` |
| `MirrorView.xaml.cs:OnNavigatedTo` | Add `gui.view.mirror` Activity | `gui.view.mirror` |
| `ConclusionsView.xaml.cs:OnNavigatedTo` | Add `gui.view.conclusions` Activity | `gui.view.conclusions` |

### 4.5 Server — Python

| File | Line | What to Add | Span Name |
|------|------|-------------|-----------|
| `leak.py:30` (approx — `handle_leak_query()`) | Wrap in span | `leak.collector.query` |
| `warp.py:25` (approx — `handle_warp_compare()`) | Wrap in span | `warp.content.compare` |
| `resolver.py:100` (approx — `resolve()`) | Wrap in span | `dns.authoritative.lookup` |
| `investigate.py:20` (approx — `handle_investigate()`) | Wrap in span | `investigate.consensus` |
| `licensing.py:30` (approx — `validate_key()`) | Wrap in span | `licensing.validate` |
| `app.py:130` (approx — before_request) | Extract `X-Syxx-Correlation-Id` header, set as span attribute | `server.request` correlation |

### 4.6 GUI Stability Monitor — New Files

| Platform | File | Purpose |
|----------|------|---------|
| macOS | `macOS/Sources/SyxxKit/GUILayoutStabilityMonitor.swift` | CADisplayLink-based stability monitor |
| macOS | `macOS/Sources/SyxxApp/Views/Components/FrameTrackingModifier.swift` | SwiftUI ViewModifier that pushes frame origins to the monitor via PreferenceKey |
| Windows | `Windows/dotNET/Syxx.Kit/GUILayoutStabilityMonitor.cs` | CompositionTarget.Rendering-based stability monitor |
| Windows | `Windows/dotNET/Syxx.Kit/SyxxTracer.cs` | ActivitySource "SyxxKit" definition (OBS-1) |

---

## 5. Conflicts with UNIFIED_IMPLEMENTATION_PLAN.md Phase 4.2

### 5.1 Confirmed Conflicts

| Plan Item | Conflict | Resolution |
|-----------|----------|------------|
| **OBS-1:** "Create `Windows/Syxx.Kit/SyxxTracer.cs` with ActivitySource 'SyxxKit'" | **No conflict.** This report agrees and specifies the exact file. | Implement as specified in §4.3. |
| **OBS-2:** "Fix `App.xaml.cs` from AddSource('SyxxUI') to AddSource('SyxxKit')" | **No conflict.** Confirmed as blocking — the TracerProvider currently listens for a nonexistent source. | Fix as specified in §4.4. |
| **OBS-3:** "Mirror 4 macOS OTel spans to Windows" | **Underspecified.** The plan says "mirror 4 spans" but doesn't say which 4. This report specifies 12+ spans per platform. | Use the span hierarchy in §3.2 as the canonical list. |
| **OBS-4:** "Add Sentry breadcrumbs to verdict paths on both platforms" | **No conflict.** This report specifies exact breadcrumb locations in §4.2 and §4.4. | Implement as specified. |
| **OBS-5:** "Add explicit `capture*()` in all catch blocks" | **No conflict.** This report adds `span.status = .error` in every catch, which is the OTel equivalent. | Implement alongside span instrumentation. |
| **OBS-9:** "Trace context propagation (client↔server W3C traceparent)" | **Underspecified.** The plan lists this as LOW priority but it is critical for end-to-end traceability. Without it, client spans and server spans are disconnected islands. | Elevate to HIGH. Add `X-Syxx-Correlation-Id` header as a lightweight alternative to full W3C traceparent (which requires OTel propagator configuration on both sides). |

### 5.2 New Recommendations Not in the Plan

| Recommendation | Priority | Reason |
|----------------|----------|--------|
| **GUI layout stability monitor** | MEDIUM | New user request. Not in the plan. Design in §2. |
| **Correlation ID pattern** | HIGH | Not in the plan. Without it, traces are disconnected. Design in §3.3. |
| **Shutdown flush** | HIGH | Not in the plan. In-flight spans are lost on app termination. Add `forceFlush()` in `SyxxAppDelegate.applicationWillTerminate` and `App.OnSuspending`. |
| **Exporter health check** | HIGH | Not in the plan. If the OTLP exporter fails, no alert fires. Add a startup health-check span that verifies connectivity to `bugsmasher.syxx.app`. |
| **Sampling strategy** | MEDIUM | Not in the plan. OTel has no sampling configured. Add `TraceIdRatioBased` sampler at 0.1 for production, 1.0 for DEBUG. |
| **Breadcrumb categories** | LOW | Not in the plan. Standardize breadcrumb categories: `gui.state`, `gui.layout`, `auth`, `network`, `dns`, `synthesis`. |

### 5.3 Plan Items This Report Defers (No Conflicts)

| Plan Item | Reason for Deferral |
|-----------|---------------------|
| OBS-6 (macOS CLI telemetry) | Out of scope for this report (GUI-focused). Defer to CLI subagent. |
| OBS-7 (Linux/Avalonia telemetry) | Out of scope. Linux bootstrap is Phase 4. |
| OBS-8 (Server operation spans) | Server spans are specified in §4.5 but server implementation is deferred to backend subagent. |
| OBS-10 (Shutdown flush + exporter health check) | This report elevates these to HIGH priority (see §5.2). The plan lists them as LOW; this is a conflict in priority, not content. |

---

## 6. Implementation Sequencing

### Phase A — Foundation (must come first, unblocks everything else)

| Step | What | Platform | Files |
|------|------|----------|-------|
| A1 | Create `SyxxTracer.cs` with `ActivitySource` "SyxxKit" | Windows | `Windows/dotNET/Syxx.Kit/SyxxTracer.cs` (new) |
| A2 | Fix `App.xaml.cs` AddSource("SyxxUI") → AddSource("SyxxKit") | Windows | `App.xaml.cs:49` |
| A3 | Add correlation ID generation to `AppModel.runScan()` | macOS | `AppModel.swift:200` (approx) |
| A4 | Add correlation ID generation to `AppViewModel.RunProphetAsync()` | Windows | `AppViewModel.cs:100` (approx) |
| A5 | Add shutdown flush to both platforms | Both | `SyxxApp.swift` + `App.xaml.cs` |

### Phase B — Probe Pipeline Spans (one toolset at a time, validates the pattern)

| Step | What | Platform | Files |
|------|------|----------|-------|
| B1 | Instrument Prophet probe pipeline | macOS + Windows | `DNSResolver.swift`, `DNSResolver.cs` |
| B2 | Instrument Mirror probe pipeline | macOS + Windows | `ContentWarp.swift`, `VantageComparer.cs` |
| B3 | Instrument Glutton probe pipeline | Both | `LeakDetector.swift/cs` |
| B4 | Instrument Witness pipeline | Both | `WitnessSigner.swift/cs` |
| B5 | Instrument Cage pipeline | Both | `DNSCapture.swift/cs` |
| B6 | Instrument Portal pipeline | Both | `LANCartographer.swift`, `IPClassifier.cs` |
| B7 | Instrument Synthesis engine | Both | `SynthesisEngine.swift:34` (already done macOS), `SynthesisEngine.cs:85` (new for Windows) |

### Phase C — GUI Telemetry

| Step | What | Platform | Files |
|------|------|----------|-------|
| C1 | Add view lifecycle spans (onAppear/onDisappear) | macOS | All `*View.swift` files |
| C2 | Add view lifecycle Activities (OnNavigatedTo) | Windows | All `*View.xaml.cs` files |
| C3 | Add verdict display breadcrumbs | Both | `OverallVerdictView.swift`, `ConclusionsView.xaml.cs` |
| C4 | Add Sentry breadcrumbs on state transitions | Both | `AppModel.swift`, `AppViewModel.cs` |

### Phase D — GUI Stability Monitor (the new request)

| Step | What | Platform | Files |
|------|------|----------|-------|
| D1 | Create `GUILayoutStabilityMonitor.swift` | macOS | `macOS/Sources/SyxxKit/GUILayoutStabilityMonitor.swift` (new) |
| D2 | Create `FrameTrackingModifier.swift` (SwiftUI PreferenceKey) | macOS | `macOS/Sources/SyxxApp/Views/Components/FrameTrackingModifier.swift` (new) |
| D3 | Integrate monitor into `SyxxAppDelegate` | macOS | `SyxxApp.swift:12` |
| D4 | Add GeometryReader to key anchor views | macOS | `ContentView.swift`, `OverallVerdictView.swift`, `DashboardView.swift` |
| D5 | Create `GUILayoutStabilityMonitor.cs` | Windows | `Windows/dotNET/Syxx.Kit/GUILayoutStabilityMonitor.cs` (new) |
| D6 | Integrate into `MainWindow.xaml.cs` | Windows | `MainWindow.xaml.cs` |
| D7 | Name key elements in XAML | Windows | `MainWindow.xaml`, `DashboardView.xaml` |
| D8 | Wire drift callback to Sentry breadcrumb | Both | `SentryBootstrap.swift`, `App.xaml.cs` |

### Phase E — Server Traces (deferred to backend subagent)

| Step | What | Files |
|------|------|-------|
| E1 | Add operation spans to leak, warp, investigate, licensing | `leak.py`, `warp.py`, `investigate.py`, `licensing.py` |
| E2 | Extract correlation ID from `X-Syxx-Correlation-Id` header | `app.py` before_request |
| E3 | Add server health-check span | `app.py` healthz endpoint |

---

## Appendix A: Key Files Referenced

| File | Lines | Purpose |
|------|-------|---------|
| `macOS/Sources/SyxxKit/SyxxTracer.swift` | 1-131 | OTel span helper — only `spanSync` used (line 89) |
| `macOS/Sources/SyxxApp/OTelBootstrap.swift` | 1-19 | OTel TracerProvider registration |
| `macOS/Sources/SyxxApp/SentryBootstrap.swift` | 1-35 | Sentry SDK initialization |
| `macOS/Sources/SyxxApp/SyxxApp.swift` | 1-236 | App entry point, menu bar, window lock |
| `macOS/Sources/SyxxApp/ContentView.swift` | 1-296 | Main view: sidebar + content area |
| `macOS/Sources/SyxxApp/DashboardView.swift` | 1-490 | Dashboard with trust ring, health meter, warden tiles |
| `macOS/Sources/SyxxApp/ConclusionsView.swift` | 1-423 | Synthesis conclusion display |
| `macOS/Sources/SyxxApp/Views/Components/OverallVerdictView.swift` | 1-383 | Verdict/evidence display |
| `macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift` | 1-201 | Synthesis engine, matrix bridge, indicator mapping |
| `macOS/Sources/SyxxApp/AppModel.swift` | 1-1629 | Main app state, license, monitoring, scan orchestration |
| `Windows/dotNET/SyxxUI/App.xaml.cs` | 1-72 | Windows app entry, Sentry + OTel bootstrap |
| `Windows/dotNET/Syxx.Kit/SyxxConfig.cs` | 1-63 | Platform config, Sentry DSN, version |
| `Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs` | 1-746 | Windows synthesis engine (old DST) |
| `Windows/dotNET/Syxx.Kit/Synthesis/Conclusion.cs` | 1-655 | Conclusion types, SectionStatus, Hypothesis |
| `Windows/dotNET/SyxxUI/ViewModels/AppViewModel.cs` | 1-546 | Windows main view-model |
| `Server/syxx_server/app.py` | 43-91 | Server Sentry + OTel bootstrap |
| `OBSERVABILITY-PLAN.md` | Full | Observability strategy, Phase 8 GUI audit |
| `UNIFIED_IMPLEMENTATION_PLAN.md` | Phase 4.2 | OBS-1 through OBS-10 observability parity items |