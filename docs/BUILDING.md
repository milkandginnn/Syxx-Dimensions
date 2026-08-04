# Building Syxx

Per-platform build, test, and packaging instructions.

## Evidence marking on every command in this document

**No command in this document was executed by the agent that wrote it.** This session had no shell
available, so nothing here is backed by an observed exit code. Saying otherwise would be the
documentation equivalent of reporting a conclusion before its evidence, which is the one thing this
project forbids. Instead, every command carries an explicit marker for what it *is* backed by:

| Marker | Means |
|--------|-------|
| **[CI]** | This exact command line, or one differing only in an obvious parameter, runs as a step in `.github/workflows/ci.yml` on the named runner. It is executed on every push and pull request, so a green `main` is real evidence that it works on that runner. It has still **not** been run on your machine or in this session. |
| **[PATH-VERIFIED]** | The solution, project, script, or file the command names has been confirmed to exist in this tree, at the cited path. The command was **not** executed and its exit code is unknown. |
| **[UNVERIFIED]** | Cannot be checked or executed from this environment at all. Treat as a claim to be confirmed, not as an instruction known to work. |

If you run something here and it fails, that is a defect in this file. Please correct it rather than
working around it.

### What genuinely cannot be verified from a macOS host

These are marked **[UNVERIFIED]** throughout, and the reason is structural, not laziness:

- **`snapcraft`** does not run on macOS. Snap packaging can only be confirmed on Ubuntu.
  `Linux/snap/snapcraft.yaml:20-24` carries its own in-file UNVERIFIED note on the `command:` path,
  because the stage path the core24 dotnet plugin publishes to depends on the plugin and has never
  been observed.
- **Windows MSIX packaging and WinUI 3 builds.** `SyxxUI` targets `net10.0-windows` and cannot
  restore off Windows. This is why the CI matrix builds it only on the `windows-latest` leg
  (`.github/workflows/ci.yml:82-90`).
- **Apple code signing and notarization.** Blocked on an Apple Developer ID that has not been
  obtained. Nobody can run these commands yet, on any host. See [`../ROADMAP.md`](../ROADMAP.md).
- **Anything requiring live packet capture or privileged sockets.** Sandboxes generally have no
  UDP:53 and no `CAP_NET_RAW`.

## Repository layout, because it is not obvious

The directory names describe origin, not exclusivity. Read this before running anything.

| What you want | Where it actually lives |
|---------------|-------------------------|
| macOS core, CLI, GUI | `macOS/` (one SwiftPM package) |
| Shared C# core (Windows **and** Linux) | `Windows/dotNET/Syxx.Kit/` |
| `syxx` CLI for Windows **and** Linux | `Windows/dotNET/Syxx/` |
| Windows GUI (WinUI 3, Windows-only) | `Windows/dotNET/SyxxUI/` |
| Linux GUI (Avalonia) | `Linux/Ubuntu/SyxxUI.Avalonia/` |
| Snap packaging | `Linux/snap/snapcraft.yaml` |
| Server | `Server/` |

The solution files are `Windows/Syxx.sln` (everything, needs Windows) and
`Windows/Syxx.Linux.slnf` (a filter excluding WinUI, so it restores anywhere). Verified against
`Windows/Syxx.sln:6-15` and `Windows/Syxx.Linux.slnf:4-9`.

**Project names.** The real projects are `Syxx.Kit`, `Syxx`, `Syxx.Tests`, `SyxxUI`, `SyxxUI.Tests`
(under `Windows/dotNET/`), plus `SyxxUI.Avalonia` and `SyxxUI.Avalonia.Tests` (under `Ubuntu/`).
Earlier revisions of this document referenced `SyxxCLI`, `Syxx.LinuxCLI`, and `Syxx.Linux.Tests`.
**No such projects exist**; those commands could never have worked. Corrected 2026-07-29.

## Prerequisites

| Platform | Required tools |
|----------|---------------|
| macOS | Xcode 16+ or a Swift 6 toolchain; `libpcap` is a system library, no install needed |
| Windows | .NET SDK per `Windows/global.json`; Visual Studio 2022+ or the `dotnet` CLI; Windows App SDK / WinUI 3 runtime; Npcap for capture |
| Linux | .NET SDK per `Windows/global.json`; `libpcap-dev`; X11/GTK runtime libraries for Avalonia |
| Server | Python 3.12 (the version CI uses), `pip` |

**The .NET SDK pin is `Windows/global.json`.** It reads `10.0.100`, `rollForward: latestMinor`,
`allowPrerelease: false` (verified, `Windows/global.json:1-7`). CI passes this file to
`actions/setup-dotnet` via `global-json-file` rather than hardcoding a version.

> **Contradiction, not yet resolved.** The CI step that consumes this file is *named* "Install .NET
> 11 preview SDK (from Windows/global.json)" (`.github/workflows/ci.yml:66`), but `global.json`
> pins .NET **10.0.100** and explicitly disallows prerelease. The step name and the file disagree.
> The file wins, because it is what the tool actually reads, so **.NET 10 is the real requirement**.
> The step name appears to be stale from an earlier .NET 11 experiment; `CHANGELOG.md` records a
> deliberate revert to .NET 10 release on 2026-07-18. Flagged rather than silently corrected,
> because `.github/` is outside this pass's scope.

## macOS

### Core, CLI, and tests

```bash
# From the repo root. --package-path avoids any dependence on your cwd.
swift build --package-path macOS                       # [PATH-VERIFIED] debug
swift build --package-path macOS -c release            # [CI] macos-14, ci.yml:124
swift test  --package-path macOS                       # [CI] macos-14, ci.yml:116
```

Products declared in `macOS/Package.swift:9-14`: the `SyxxKit` library, and executables `syxx`
(target `SyxxCLI`), `syxx-gui` (target `SyxxApp`), and `DeepSyxx` (the privileged helper).

```bash
# Run the CLI                                          # [PATH-VERIFIED]
swift run --package-path macOS syxx                    # interactive guided mode
swift run --package-path macOS syxx nslook example.com
swift run --package-path macOS syxx leak
```

### The release symbol-absence check

CI does not merely build release; it then asserts that a specific fabricated fixture is **absent**
from the shipped binaries:

```bash
# [CI] macos-14, ci.yml:121-132
swift build --package-path macOS -c release
for bin in macOS/.build/release/syxx macOS/.build/release/syxx-gui; do
  nm "$bin" 2>/dev/null | grep -q mockInconclusive && echo "C061 regression in $bin"
done
```

`Conclusion.mockInconclusive` is a preview fixture carrying invented section statuses. It is
`#if DEBUG`-gated at `macOS/Sources/SyxxKit/Synthesis/Conclusion.swift:204-227` and must be
structurally unreachable from a release product. This check has a real, documented failure case:
remove the `#if DEBUG` guard and the symbol reappears and the step fails. That is what a test with a
proven failure case looks like.

### Lint

```bash
swiftlint lint --config .swiftlint.yml --strict         # [CI] macos-14, ci.yml:137-141
```

Not cosmetic. It bans force-unwrap, force-cast, and force-try, which are the idioms that turn a
missing measurement into a crash or a defaulted value.

### GUI app bundle

```bash
bash macOS/bundle-app.sh              # [PATH-VERIFIED] release, the default
bash macOS/bundle-app.sh debug        # [PATH-VERIFIED]
open macOS/Syxx.app                   # [UNVERIFIED]
```

The script (`macOS/bundle-app.sh`, read and confirmed) `cd`s to its own directory, then:

1. Builds `syxx-gui` and `DeepSyxx` in the chosen configuration (`:10-11`).
2. Assembles `Syxx.app` and copies `syxx-gui` in as `Contents/MacOS/Syxx` (`:14-18`).
3. Builds and bundles the `syxx` CLI into `Contents/Resources/` so the GUI's Cage can re-exec it for
   admin-authorized packet capture (`:20-23`).
4. Installs the `DeepSyxx` privileged helper for SMJobBless (`:25-27`).
5. Copies the icon, fonts, and the SwiftPM resource bundle, the last so `Bundle.module` still
   resolves inside the wrapped app (`:28-32`).
6. Derives the version from `SyxxConfig.swift` so `Info.plist` stays in sync (`:34-35`).

### DMG and notarization

```bash
bash macOS/scripts/build_dmg.sh       # [PATH-VERIFIED] script exists; not executed
```

Code signing and notarization are **[UNVERIFIED] and currently impossible**: they are blocked on an
Apple Developer ID that has not been obtained. An unsigned DMG will be refused by Gatekeeper on a
customer machine. macOS ships third for exactly this reason.

### Requirements

- macOS 14.0 (Sonoma) or later (`macOS/Package.swift:7`).
- `libpcap` is a system library.

## Windows

Build **from the repository root**; the paths below are repo-relative.

### Core, CLI, and tests

```powershell
# Shared core                          # [CI] windows-latest, ci.yml:80
dotnet build Windows/dotNET/Syxx.Kit/Syxx.Kit.csproj -c Release

# Everything except WinUI              # [CI] ubuntu, ci.yml:76 (also works on Windows)
dotnet build Windows/Syxx.Linux.slnf -c Release

# Shared reasoning-core tests          # [CI] all dotnet legs, ci.yml:102
dotnet test Windows/dotNET/Syxx.Tests/Syxx.Tests.csproj -c Release --nologo

# Run the CLI                          # [PATH-VERIFIED]
dotnet run --project Windows/dotNET/Syxx -- nslook example.com
```

### GUI

```powershell
# [CI] windows-latest ONLY, ci.yml:90
dotnet build Windows/dotNET/SyxxUI/SyxxUI.csproj -c Release -p:Platform=x64

# [CI] windows-latest ONLY, ci.yml:98
dotnet test Windows/dotNET/SyxxUI.Tests/SyxxUI.Tests.csproj -c Release -p:Platform=x64
```

**`-p:Platform` is mandatory.** WinUI 3 has no "Any CPU" platform, so a bare
`dotnet build Windows/Syxx.sln` can silently skip `SyxxUI` or error on it. `build.ps1:7-8` documents
this, and `build.ps1:20` selects `arm64` or `x64` from `PROCESSOR_ARCHITECTURE`. A build that
"succeeded" without compiling the GUI is a false green.

The `windows-latest` leg is the **only** CI job that ever compiles the Windows GUI. Without it a
Windows GUI regression can ship having been verified by static reading alone
(`.github/workflows/ci.yml:82-87`).

### Convenience scripts

```powershell
.\build.ps1              # [PATH-VERIFIED] repo root, NOT Windows/
.\build-installer.ps1    # [PATH-VERIFIED] repo root
```

`build.ps1` builds GUI first, then core, CLI, and tests, in **both** Debug and Release
(`build.ps1:37-40`). Note both scripts live at the **repository root**, not under `Windows/`.

### MSIX packaging

**[UNVERIFIED].** Cannot be produced or validated off Windows. Windows ships second via the
Microsoft Store, which is approved and needs no EV certificate.

### Requirements

- Windows 11.
- .NET SDK per `Windows/global.json`.
- Windows App SDK / WinUI 3 runtime.
- Npcap for `syxx cage` (https://npcap.com).

## Linux (Ubuntu / Debian / Mint)

The Linux client is built from the `Windows/` tree, which is cross-platform apart from `SyxxUI`.

### Core, CLI, and tests

```bash
# Core + CLI + tests, WinUI excluded   # [CI] ubuntu-latest and ubuntu-24.04-arm, ci.yml:76
dotnet build Windows/Syxx.Linux.slnf -c Release

# [CI] ci.yml:102
dotnet test Windows/dotNET/Syxx.Tests/Syxx.Tests.csproj -c Release --nologo
```

### Self-contained CLI publish

This is the real shipping artifact, and CI treats it as a hard gate:

```bash
# [CI] ubuntu-latest (linux-x64) and ubuntu-24.04-arm (linux-arm64), ci.yml:106-111
dotnet publish Windows/dotNET/Syxx/Syxx.csproj -c Release \
  -r linux-x64 --self-contained true -p:PublishSingleFile=true -o out/linux-x64
```

Deliberately **not** trimmed: NSec and the DNS reflection paths are trim-hostile
(`.github/workflows/ci.yml:104-105`). Each RID carries its own `libsodium.so`. Both `linux-x64` and
`linux-arm64` are built, the latter matching the Ubuntu Desktop arm64 ISO.

### GUI (Avalonia)

```bash
# [CI] ubuntu-latest, ci.yml:219
dotnet build Linux/Ubuntu/SyxxUI.Avalonia/SyxxUI.Avalonia.csproj -c Release --nologo

# [CI] ubuntu-latest, ci.yml:222
dotnet test Linux/Ubuntu/SyxxUI.Avalonia.Tests/SyxxUI.Avalonia.Tests.csproj -c Release --nologo
```

The Avalonia client is **excluded** from `Windows/Syxx.Linux.slnf` yet project-references the shared
`Syxx.Kit`. Until the `linux-gui` CI job was added on 2026-07-27, nothing in CI compiled it, so a
`Syxx.Kit` change could break the first-ship platform with every leg green. That is not
hypothetical: the client stopped compiling on retired DST types and nothing noticed
(`.github/workflows/ci.yml:200-205`).

### Packaging

```bash
bash Ubuntu/build_deb.sh              # [PATH-VERIFIED] script exists; not executed
snapcraft                             # [UNVERIFIED] cannot run on macOS
```

Snap notes, from `Linux/snap/snapcraft.yaml` (read and confirmed):

- The part builds `dotNET/Syxx/Syxx.csproj` with `source-subdir: Windows` (`:39-40`), confirming the
  shipping Linux CLI comes from the `Windows/` tree.
- `command: Syxx` must match `<AssemblyName>` in `Syxx.csproj`. It previously read `Syxx.CLI`, a
  name the build never produces, so the snap installed and then failed to launch (`:16-18`). The
  assembly name is now correct, but the **stage path remains [UNVERIFIED]** and must be confirmed by
  one real `snapcraft` run on Ubuntu before shipping (`:20-24`).
- Plugs are `network`, `network-bind`, `network-observe`. **None of these grants `CAP_NET_RAW`**, so
  under strict confinement `syxx cage` cannot capture. It must report `untested`, never a clean or
  empty-normal capture (`:30-32`). An empty capture is not a clean capture.

### Requirements

- Ubuntu 22.04+, Debian 12+, or Mint 21+.
- .NET SDK per `Windows/global.json`.
- `libpcap-dev`.
- Avalonia runtime libraries: `apt install libx11-6 libgtk-3-0 libice6 libsm6` **[UNVERIFIED]** on
  this host; CI's `ubuntu-latest` image supplies these already, so the job passing does not prove
  this exact apt line is sufficient on a clean machine.

## Server

```bash
cd Server

# [CI] ubuntu-latest, ci.yml:240-245 (CI uses Python 3.12)
python3 -m venv venv
./venv/bin/pip install --upgrade pip
./venv/bin/pip install -r requirements.txt -r requirements-dev.txt

# [CI] ci.yml:248-251
./venv/bin/python -m pytest tests/ -q

# [CI] ci.yml:265
./venv/bin/ruff check syxx_server/ tests/

# [CI] ci.yml:272
./venv/bin/bandit -r syxx_server/ -c pyproject.toml
```

`requirements.txt`, `requirements-dev.txt`, `pyproject.toml`, `server.py`, and `tests/` are all
**[PATH-VERIFIED]** present under `Server/`. Note that installing `requirements.txt` alone is not
enough to run the suite; `requirements-dev.txt` is also required, which the previous revision of
this document omitted.

```bash
python server.py          # [PATH-VERIFIED] development server; not executed
```

### The honesty-coverage gate

```bash
./scripts/honesty-coverage-gate.py     # [CI] ubuntu-latest, ci.yml:258 (run from the repo root)
```

This is not ordinary coverage. It runs the suite under coverage and asserts that **every registered
security guard is actually exercised by some test**. The threat it addresses is specific: a guard
that no test reaches can be deleted, and the suite stays green. Guard deletion would otherwise be an
invisible change. `scripts/honesty-coverage-gate.py` is **[PATH-VERIFIED]**.

### Production deployment

Deployed to `/opt/syxx/server/` on the production host. See `Server/README.md` and
`Server/SERVER_IMPLEMENTATION_PLAN.md`. **[UNVERIFIED]** from here.

## Cross-platform parity

`SyxxKit` (Swift) and `Syxx.Kit` (C#) are maintained in deliberate parity. A change to shared logic
in one requires the twin change in the other, **in the same pass**.

CI enforces this structurally with a four-runtime matrix: win-x64, linux-x64, linux-arm64, and the
Swift twin on macos-14 (`.github/workflows/ci.yml:41-60`).

**The transcript-parity `diff` job is currently a visible placeholder.** Its byte-identical and
epsilon assertions land with the transcript emitter in WS-B2. Until then it emits a **warning
annotation** rather than passing silently, so that an incomplete gate is never mistaken for a passed
one (`.github/workflows/ci.yml:290-298`). When transcripts do exist, divergence is a hard failure
(`:299-315`). Read the warning as "this gate is not yet armed", not as "parity is confirmed".

## CI summary

`.github/workflows/ci.yml` defines four jobs. Triggers: push to `main`, any pull request, and
`workflow_dispatch`.

| Job | Runner(s) | What it does |
|-----|-----------|--------------|
| `build-test` | macos-14, windows-latest, ubuntu-latest, ubuntu-24.04-arm | Builds and tests the engine on all four runtimes; the C061 symbol-absence check and SwiftLint on the Swift leg; the WinUI build and `SyxxUI.Tests` on the Windows leg; self-contained publish on the Linux legs |
| `linux-gui` | ubuntu-latest | Builds and tests the Avalonia client |
| `server` | ubuntu-latest | pytest, honesty-coverage gate, ruff, bandit |
| `diff` | ubuntu-latest | Transcript-parity gate; **placeholder assertions**, see above |

`fail-fast` is disabled on the matrix, so one platform failing does not hide the others' results.
