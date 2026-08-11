# Syxx macOS Architecture

## Module Overview

The macOS codebase is organized as a Swift Package with seven targets. Dependencies flow strictly downward — no circular imports.

```
┌─────────────────────────────────────────────────────┐
│  SyxxGUI (executable)    SyxxCLI (executable)       │
│         │                         │                  │
│         └────────┬────────────────┘                  │
│                  ▼                                   │
│           ┌─────────────┐                            │
│           │  SyxxApp    │  ← SwiftUI presentation    │
│           │  (library)  │    + app lifecycle          │
│           └──────┬──────┘                            │
│                  │                                   │
│         ┌────────┼────────┐                          │
│         ▼        ▼        ▼                          │
│   ┌──────────┐ ┌──────┐ ┌─────┐                     │
│   │ SyxxKit  │ │Syxx  │ │Cage │                     │
│   │ (library)│ │Telem │ │     │                     │
│   └──────────┘ └──────┘ └─────┘                     │
│                  │                                   │
│         ┌────────┴────────┐                          │
│         ▼                 ▼                          │
│   DeepSyxx (privileged   OpenTelemetry + Sentry     │
│   helper executable)     (external deps)            │
└─────────────────────────────────────────────────────┘
```

### Target Responsibilities

| Target | Type | Purpose |
|--------|------|---------|
| **SyxxKit** | Library | Core reasoning engine — DNS probing, synthesis, trust, forensics, WARP verification |
| **SyxxApp** | Library | SwiftUI views, app lifecycle, settings, onboarding |
| **SyxxTelemetry** | Library | OTel + Sentry bootstrap, diagnostic sinks |
| **SyxxGUI** | Executable | Thin entry point that hosts SyxxApp |
| **SyxxCLI** | Executable | Command-line interface (parity transcript, diagnostics) |
| **DeepSyxx** | Executable | Privileged helper (SMJobBless) for packet capture |
| **Ccage** | Library | Low-level libpcap wrapper for DNS capture |

## SyxxKit Module Boundaries

SyxxKit is the reasoning core. It has no UI dependency and is testable in isolation.

### DNS & Capture

Files at `SyxxKit/` root level handle DNS query capture, resolution, and transport monitoring:

- **DNSCapture** / **DNSCaptureReportGenerator** — pktap/libpcap DNS interception
- **DNSResolver** / **UnboundResolver** / **UnboundManager** — authoritative DNS lookups
- **DNSSECVerifier** — DNSSEC chain validation
- **DNSTransportMonitor** / **DNSTransportState** — DoH/DoT/plain transport observation
- **CageHelpers** / **PktapHeader** — packet capture plumbing (consumes Ccage)
- **DNSCryptProxyConfig** / **DNSCryptProxyManager** — encrypted DNS proxy management
- **EncryptedDNSConfig** — DoH/DoT template configuration

### Probes (root-level `*Probe.swift` files)

Individual evidence-gathering probes, orchestrated by **ProbeRunner** / **ProbePlan** / **ProbeBudget**:

- **CNAMEChainProbe** — CNAME chain resolution and analysis
- **DenialOfExistenceProbe** — NXDOMAIN / NODATA verification
- **DualStackProbe** — IPv4/IPv6 resolution parity
- **ECSProbe** — EDNS Client Subnet behavior
- **EDNS0Probe** — EDNS0 option support detection
- **EncryptedTransportProbe** — DoH/DoT availability
- **FilteringProbe** — DNS filtering detection
- **OpenResolverProbe** — open resolver identification
- **ResolverLatencyProbe** — resolver response timing
- **ResponseSizeProbe** — DNS response size analysis
- **StubEntropyProbe** — query randomization quality
- **SVCBProbe** — SVCB/HTTPS record probing
- **TTLAnomalyProbe** — TTL-based tampering detection
- **VersionBindProbe** — resolver software identification
- **WildcardSynthesisProbe** — wildcard DNS synthesis detection

### Synthesis (`Synthesis/`)

The reasoning pipeline that turns raw probe evidence into conclusions:

- **Synthesizer** / **SynthesisEngine** — orchestrates the full pipeline
- **MatrixDeterminativeEngine** / **SyxxMatrixIntegration** — matrix-based evidence weighting
- **Conclusion** — tri-state output (VERIFIED_MATCH / TAMPERING_DETECTED / INCONCLUSIVE)
- **Report** / **ReportCard** — structured output for presentation
- **CrossVantageConsensus** / **VantageComparer** — multi-vantage agreement
- **DNSCanaries** / **FirstRunScan** — baseline establishment
- **PlainExplanation** — human-readable result summaries
- **Stage5Presentation** / **TrayIndicator** — result surface state

### Trust (`Trust/`)

Cryptographic trust and continuous integrity monitoring:

- **Sentinel** — continuous DNS integrity monitor
- **ContinuousMonitor** / **MonitorScheduler** — scheduled re-verification
- **AnswerLedger** — tamper-evident record of DNS answers
- **IntegrityTag** — evidence integrity tagging
- **WitnessTestifyPlanner** — witness testimony planning

### Forensics (`Forensics/`)

Deep analysis of DNS and network logs for threat patterns:

- **DNSLogAnalyzer** — DNS log pattern analysis
- **BeaconAnalyzer** — DNS beaconing detection
- **ExfilAnalyzer** — data exfiltration via DNS detection
- **EndpointLogAnalyzer** — endpoint behavior analysis
- **Blocklist** / **SieveBlocklistLoader** — threat intelligence blocklists

### Mirror (`Mirror/`)

Content integrity comparison (before/after, cross-vantage):

- **DiffEngine** / **DiffResult** — content diffing
- **DegradationCalculator** / **DegradationScore** — content degradation scoring
- **MirrorBundle** / **MirrorResult** — mirror comparison packaging
- **StaticContentParser** — HTML/CSS/JS content parsing

### LAN (`LAN/`)

Local network topology and device discovery:

- **LANCartographer** — LAN device mapping
- **ARPTableParser** — ARP table analysis
- **NetworkAddressClassifier** — local/remote address classification
- **CommandRunner** — shell command execution for network tools

## Synthesis Pipeline Flow

```
DNS Query (captured via Ccage/pktap)
    │
    ▼
ProbeRunner executes ProbePlan
    │
    ├── CNAMEChainProbe → CNAME evidence
    ├── DenialOfExistenceProbe → existence evidence
    ├── DualStackProbe → A/AAAA parity evidence
    ├── DNSSECVerifier → DNSSEC chain evidence
    ├── TTLAnomalyProbe → TTL anomaly evidence
    ├── FilteringProbe → filtering evidence
    └── ... (15+ probes)
    │
    ▼
SynthesisEngine collects all probe evidence
    │
    ▼
MatrixDeterminativeEngine weights evidence
    │  (each probe contributes a signal with confidence)
    │
    ▼
Conclusion (tri-state):
    ├── VERIFIED_MATCH — DNS responses are consistent
    ├── TAMPERING_DETECTED — evidence of DNS manipulation
    └── INCONCLUSIVE — insufficient or conflicting evidence
    │
    ▼
Report / ReportCard → SyxxApp (UI presentation)
```

## Data Flow Between Targets

```
┌──────────┐     ┌───────────┐     ┌──────────────┐
│ SyxxApp  │────▶│ SyxxKit   │────▶│ SyxxTelemetry│
│ (UI)     │◀────│ (logic)   │◀────│ (observability)
└──────────┘     └───────────┘     └──────────────┘
     │                 │                   │
     │  user actions   │  OTel spans       │  Sentry events
     │  result display │  + metrics        │  + crash reports
     │                 │                   │
     ▼                 ▼                   ▼
  SwiftUI          Probes +            OTel exporter
  Views            Synthesis           + Sentry SDK
```

- **SyxxApp → SyxxKit**: SyxxApp calls SyxxKit APIs to trigger scans, read results, manage settings. SyxxKit exposes `AppModel`-compatible types.
- **SyxxKit → SyxxTelemetry**: SyxxKit emits OTel spans via `SyxxTracer`. SyxxTelemetry bootstraps the OTel SDK and Sentry, providing the export pipeline.
- **Ccage → SyxxKit**: Ccage provides raw packet capture (libpcap). SyxxKit's `DNSCapture` consumes captured DNS packets.
- **DeepSyxx → Ccage**: The privileged helper runs as root to open pktap interfaces, forwarding captured packets to SyxxKit via XPC.

## Key Types

| Type | Module | Role |
|------|--------|------|
| `TriState` | SyxxKit | Three-valued logic (clean / threat / inconclusive) |
| `Conclusion` | SyxxKit/Synthesis | Final synthesis output with state + explanation |
| `ReportCard` | SyxxKit/Synthesis | Structured probe results for UI display |
| `WarpEngine` | SyxxKit | WARP manifest signature verification |
| `SyxxConfig` | SyxxKit | Centralized configuration |
| `ProbeRunner` | SyxxKit | Orchestrates probe execution with budget constraints |
| `MatrixDeterminativeEngine` | SyxxKit/Synthesis | Evidence weighting and conclusion derivation |
| `Sentinel` | SyxxKit/Trust | Continuous DNS integrity monitoring |
| `AppModel` | SyxxApp | Observable state container for SwiftUI views |
| `OTelBootstrap` | SyxxTelemetry | OpenTelemetry SDK initialization |
