# Technology Stack

## Frontend (Desktop Clients)

### macOS Client

**Choice:** SwiftUI
**Version:** Swift 6 (macOS 14+)

**Rationale:** Native macOS UX with modern concurrency and SwiftPM packaging.

### Windows Client

**Choice:** WinUI 3
**Version:** .NET 10.0.100 (C#)

**Rationale:** Native Fluent Design on Windows via the Windows App SDK.

### Linux Client

**Choice:** Avalonia UI
**Version:** 12.1.0 (C#/.NET)

**Rationale:** Cross-platform XAML-based UI for Ubuntu/Mint via Snap and apt.

### Additional Frontend Libraries

| Library            | Purpose                        | Version              |
| ------------------ | ------------------------------ | -------------------- |
| opentelemetry-swift | Telemetry (macOS)              | 2.5.x                |
| sentry-cocoa       | Crash reporting (macOS)        | 8.43.0               |
| .NET Sentry        | Crash reporting (WinUI/Avalonia) | 4.13.0             |

## Backend

### Language

**Choice:** Python
**Version:** 3.12 (server v2.0.0)

**Rationale:** Stdlib core keeps the security-sensitive surface auditable and dependency-light.

### Framework

**Choice:** Python stdlib `http.server` (`BaseHTTPRequestHandler`, `ThreadingHTTPServer`)

**Version:** 3.12 stdlib

**Rationale:** No framework dependency; handler routes are explicit and testable.

### Database

#### Primary Database

**Choice:** SQLite (`sqlite3`)

**Version:** stdlib

**Rationale:** Zero-config, parameterized SQL everywhere, single-file deploy.

### Additional Backend Libraries

| Library            | Purpose                     | Version              |
| ------------------ | --------------------------- | -------------------- |
| cryptography       | Signing / hashing / AES-GCM | ≥ 41                 |
| brotli             | Compression                 | ≥ 1.1                |
| OpenTelemetry      | Traces                      | ≥ 1.44               |
| sentry-sdk         | Crash reporting             | ≥ 2.19               |

## Infrastructure

### Hosting

**Provider:** Self-hosted
**Environment:** Ubuntu Server 26.04 / 24.04

**Services Used:**

- nginx (reverse proxy)
- Let's Encrypt (TLS)
- systemd (service supervision)

### CI/CD

**Platform:** GitHub Actions (`.github/workflows/ci.yml`, 470-line 4-runtime matrix)

**Pipeline Stages:**

1. Python: pytest + honesty-coverage + ruff + bandit
2. macOS: Swift 6 test + SwiftLint `--strict`
3. Windows: dotnet test (WinUI)
4. Linux: Avalonia test + snap packaging + parity gates

### Monitoring

**APM:** OpenTelemetry
**Logging:** Structured redacted logs
**Alerting:** Sentry

### Additional Infrastructure

| Service   | Purpose                       | Provider          |
| --------- | ----------------------------- | ----------------- |
| pcap      | Packet capture (Ccage)        | libpcap           |
| DNS       | Authoritative/UDP-53 collector| self-hosted       |

## Development Tools

### Package Manager

**Choice:** uv (Python), SwiftPM (Swift), dotnet (C#)
**Version:** per-toolchain

### Testing

| Type        | Tool                 | Coverage Target             |
| ----------- | -------------------- | --------------------------- |
| Unit        | pytest / XCTest / xUnit | Security core ≥ 80%     |
| Integration | pytest (server)      | Guard anchors per module    |
| E2E         | CI parity gates      | Critical paths              |

### Linting & Formatting

**Linter:** ruff (Python) / SwiftLint (Swift) / dotnet analyzers (C#)
**Formatter:** ruff format
**Config:** `pyproject.toml` ([tool.ruff]), `.swiftlint.yml`

### Additional Dev Tools

| Tool          | Purpose                |
| ------------- | ---------------------- |
| make          | validate / garden / test / smoke-test |
| ty            | Python type checking   |
| Bandit        | Python security scanning |

## Decision Log

### Signed Update Channel

**Date:** 2026-08
**Status:** Accepted

**Context:**
Desktop clients auto-update; an unsigned update path would let a MITM ship tampered binaries.

**Decision:**
Ed25519-verified manifest → SHA-256/size verification before install; dev key gated behind `SYXX_ALLOW_DEV_UPDATE_KEY=1`.

**Consequences:**

- Update pipeline is verifiable end-to-end.
- Offline/license paths that bypass signing are suspect and must be tested or excised.

### Python stdlib server

**Date:** 2026-08
**Status:** Accepted

**Context:**
Server is the security-critical ground truth for DNS verdicts.

**Decision:**
Use stdlib `http.server` + SQLite with parameterized SQL; no framework dependency.

**Consequences:**

- Explicit routing and easy testing; larger `Handler` must be kept cohesive (god-class risk at `app.py` ~2949 lines).

## Version Compatibility Matrix

| Component        | Min Version | Max Version | Notes                          |
| ---------------- | ----------- | ----------- | ------------------------------ |
| Python           | 3.12        | 3.14        | Drift between 3.12 and 3.14 tracked |
| Swift            | 6 (macOS 14) | latest    | SwiftPM + opentelemetry-swift  |
| .NET             | 10.0.100    | latest      | WinUI 3 / Avalonia 12.1.0      |
| Ubuntu Server    | 24.04       | 26.04       | nginx + Let's Encrypt          |
