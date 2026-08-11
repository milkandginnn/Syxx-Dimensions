# Tech Stack

Detected from the tree on 2026-08-05. Versions are what the manifests pin, not what is installed.

## Languages

| Language | Version | Where |
|---|---|---|
| Swift | tools 6.0, targets macOS 14+ | `macOS/Sources/{SyxxKit,SyxxApp,SyxxCLI}` |
| C# | `net10.0`, `<Nullable>enable</Nullable>` | `Windows/dotNET/*`, `Linux/Ubuntu/SyxxUI.Avalonia` |
| Python | 3.12 (`target-version = "py312"`) | `Server/syxx_server`, `scripts/`, corpus builders |

Note the asymmetry: **two shared-logic twins, four runtimes.** `Syxx.Kit` (C#) serves both Windows
and Linux; `SyxxKit` (Swift) serves macOS alone. Parity work is therefore mostly Swift ↔ C#, plus a
runtime matrix for the single C# assembly.

## UI frameworks

| Platform | Framework |
|---|---|
| macOS | SwiftUI |
| Windows | WinUI (`net10.0-windows10.0.26100.0`) |
| Linux | Avalonia 12.1.0 (`Desktop`, `Themes.Fluent`, `Fonts.Inter`) |

## Key dependencies

- **Crypto** — `NSec.Cryptography` 26.4.0 (Ed25519, C# side); CryptoKit (Swift side)
- **Telemetry** — `opentelemetry-swift` 2.5.0 / `opentelemetry-swift-core` 2.5.1;
  `System.Diagnostics.DiagnosticSource` 9.0.0 (C#)
- **Crash reporting** — `sentry-cocoa` 9.x (Swift); `Sentry` 6.8.0 (NuGet, 3 projects);
  `sentry-sdk[opentelemetry]` ≥2.67.0 (Python)
- **Server** — Python 3.12, `Server/.venv` (uv canonical; note: the corpus builder needs
  `Server/.venv/bin/python`; system `python3` lacks `brotli`)

## Tooling

| Concern | Tool | Config |
|---|---|---|
| Python package management | **uv** | — |
| Python lint/format | **ruff** | `Server/pyproject.toml`, line-length 120, selects `E,W,F,I,B,C4,UP,S,T20` |
| Python type check | **ty** | — |
| Swift lint | **SwiftLint** | `.swiftlint.yml` |
| .NET | `dotnet` SDK 10.0.302 | `Windows/Syxx.sln` |

Do not use pip, mypy, or black.

## Build and test invocation

These are load-bearing; the wrong working directory produces misleading failures.

```bash
# Swift — from the package root, or with an explicit --package-path
swift build --package-path macOS
swift test  --package-path macOS
# Running from elsewhere produces a spurious DeepSyxx-Info.plist link failure that is
# purely a working-directory artifact.

# .NET — from the test project, not the repo root
dotnet test Windows/dotNET/Syxx.Tests

# Cross-language parity check, local, ~90s
dotnet run --project Windows/dotNET/Syxx/Syxx.csproj -c Release -- parity-transcript --out=/tmp/t.net.json
swift run --package-path macOS -c release syxx parity-transcript --out=/tmp/t.swift.json
diff <(jq --sort-keys . /tmp/t.net.json) <(jq --sort-keys . /tmp/t.swift.json)
```

## Platform constraints

- **`Windows/dotNET/SyxxUI` cannot build on macOS.** The WinUI XamlCompiler crashes
  ("output.json was not created"), and it fails *before* `csc`, so it hides ordinary C# syntax
  errors in that project. Never claim Windows GUI verification from a Mac.
- **Everything else in `Windows/dotNET/` does build on macOS.** Five of the seven projects target
  plain `net10.0` — including `Syxx.csproj`, which hosts `parity-transcript`. Only `SyxxUI` and
  `SyxxUI.Tests` carry the `-windows` TFM.
- **Tests write `receipts/` and `review-receipts/` relative to cwd**, so each `cd` spawns a stray
  pair. They are test residue, not anyone's work product.

## Infrastructure

Not yet decided — deliberately left open. Record it here when it is.
