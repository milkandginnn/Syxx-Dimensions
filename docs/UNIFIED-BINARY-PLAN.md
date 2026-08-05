# Unified Binary: Consolidate 12 CLI Tools into `syxx`

**Date:** 2026-07-13
**Author:** syxx-neural-researcher
**Status:** PLANNING — awaiting user approval before implementation

---

## What

Turn 12 separate CLI executables (`syxx-nslook`, `syxx-mirror`, `syxx-leak`, etc.) into a single `syxx` binary with subcommands. GUI stays separate. Remove `SyxxDstProbe`.

```
syxx                          # Interactive guided mode (no args)
syxx nslook <domain> [...]    # DNS lookup
syxx mirror <domain> [...]    # Vantage comparison
syxx leak [...]               # VPN leak test
syxx witness [...]            # Signed evidence
syxx key                      # Network state
syxx route <target>           # Traceroute
syxx lan                      # ARP/LAN map
syxx forensics [...]          # DNS forensics
syxx sentinel [...]           # Config drift
syxx cage [...]               # Packet capture (sudo inline)
syxx warp <url>               # Content integrity
syxx --help / --version
```

---

## Resources

| Resource | Purpose | Status |
|----------|---------|--------|
| `@syxx-neural-executor` | Implements code changes on macOS and Windows | Available |
| `@syxx-parity-check` skill | Proves parity across platforms after Phase 2 | Available |
| `@syxx-honesty-audit` skill | Audits the new CLI output for honest verdicts | Available |
| Swift toolchain (6.3.3) | macOS build | Confirmed installed |
| .NET SDK (10.0.301) | Windows build | Confirmed installed |
| `swift build` / `swift test` | macOS build/test | Working |
| `dotnet build Windows/Syxx.sln` / `dotnet test` | Windows build/test | Available |

---

## Proposed User-Facing Output (requires approval before implementation)

### `syxx --help`

```
syxx — DNS integrity toolkit

Usage:
  syxx <command> [options]

Commands:
  nslook     DNS lookup (A, AAAA, MX, TXT, NS, DNSKEY, etc.)
  mirror     Compare local DNS resolution against public vantage points
  leak       Test whether DNS queries leak outside a VPN/tunnel
  witness    Create and verify signed evidence snapshots
  key        Report network state (resolvers, interfaces, VPN, encrypted DNS)
  route      Traceroute path analysis
  lan        Map on-link devices from the ARP table
  forensics  DNS log analysis, beaconing, exfiltration detection
  sentinel   Detect changes to DNS configuration
  cage       Capture port-53 DNS packets (requires sudo)
  warp       Web content integrity check via the syxx.app proxy

Options:
  --version, -V   Show version
  --help, -h      Show this help

Run 'syxx' with no arguments for the interactive guided mode.
```

### `syxx --version`

```
syxx 2.0.0
```

### Error: unknown subcommand

```
Unknown command: foo
Run 'syxx --help' for a list of commands.
```

### Error: no args to subcommand

```
Usage: syxx nslook <domain> [record_type] [@server] [--dnssec] [--doh [url]]
```

---

## Pre-flight Check

- [x] Swift toolchain available: `swift-driver version: 1.148.6 Apple Swift version 6.3.3`
- [x] .NET SDK available: `10.0.301`
- [x] macOS builds: `swift build` completes (0.34s)
- [x] Write access to `macOS/Sources/SyxxCLI/`, `macOS/Sources/SyxxKit/`, `macOS/Package.swift`
- [x] Write access to `Windows/Syxx/`, `Windows/Syxx.Kit/`, `Windows/Syxx.sln`
- [x] Write access to `DELTAS.md`, `ROADMAP.md`, `DESIGN.md`, `CHANGELOG.md`, `MEMORY.md`

---

## Pitfalls and Risks

| Risk | What could go wrong | Mitigation |
|------|---------------------|------------|
| Guided flow breaks during consolidation | The interactive menu (SyxxCLI:30-84) calls `runProphet`, `runMirror`, etc. If any function signature changes during extraction, the guided flow silently breaks. | Extract each command body verbatim first. Only refactor signatures after the guided flow is verified working. |
| Ccage linkage adds libpcap to all commands | The unified binary links libpcap even for non-cage commands. | libpcap is a system framework on macOS, negligible size. Acceptable. On Windows, cage uses raw sockets (no extra dependency). |
| GUI cage launch breaks | `AppModel.swift:614` constructs an osascript command referencing `bin.path` (the `syxx-cage` binary). After consolidation, this binary no longer exists. | GUI must be updated to re-exec self with a hidden cage subcommand, or use the unified binary path with `cage` argument. This is the highest-risk change. |
| Shared helpers stuck in SyxxCLI | `triLabel()`, `hostOnly()`, `isSafeTraceTarget()`, `isOwnNetworkAddress()`, `promptDomain()`, `promptCaptureDuration()` are defined in SyxxCLI.swift (:705-735) but needed by commands now in SyxxKit. | Move these helpers into SyxxKit as `public` functions before extracting commands. |
| SectionInputs wiring | The guided flow accumulates results in `SectionInputs` (:28) across multiple checks. The standalone commands don't use SectionInputs. | Standalone commands remain stateless (they print output). Only the guided flow uses SectionInputs. No change needed. |
| Windows `SyxxCage.exe` → `Syxx.exe cage` | Windows UAC elevation (`psi.Verb = "runas"` at Program.cs:596) works differently from macOS sudo. The unified binary must handle both. | On Windows, re-exec self with `cage` subcommand using `UseShellExecute = true, Verb = "runas"`. Test UAC prompt appears correctly. |

---

## Code Infrastructure

### What the implementer needs to know before starting

1. **SyxxKit is the shared core.** All 12 CLI tools depend on it. The GUI also depends on it. Moving command logic into SyxxKit means it's callable from both the CLI and the GUI.

2. **Ccage is a C module** (`macOS/Sources/Ccage/`) that wraps libpcap. It's only used by `SyxxCage.swift`. The unified binary must depend on `Ccage` to keep the cage command working.

3. **The guided flow is the most complex code.** SyxxCLI.swift is 736 lines. It has 6 guided check functions (`runProphet`, `runMirror`, `runPortal`, `runGlutton`, `runCage`, `runWitness`), a conclusions synthesizer, a license manager, and shared helpers. The guided flow accumulates state in `SectionInputs` (defined at `SyxxKit/Synthesis/Synthesizer.swift:23`).

4. **Standalone commands are simple.** Each is 50-120 lines: parse args, call a SyxxKit function, print results. They don't use `SectionInputs`.

5. **The GUI's cage launch uses osascript** (`AppModel.swift:614`). It constructs a shell command string with interpolated paths. After consolidation, it must reference the unified binary with a `cage` subcommand instead of a separate `syxx-cage` binary.

6. **Windows uses `tracert` not `traceroute`** and raw sockets not libpcap for cage. The TracerouteRunner must be platform-abstracted.

7. **Helpers split between CLI and SyxxKit:**
   - **Stay in SyxxCLI** (formatting/interactive, not library concerns):
     - `triLabel(_ s: TriState) -> String` (:705)
     - `hostOnly(_ target: String) -> String` (:713)
     - `isSafeTraceTarget(_ target: String) -> Bool` (:720)
     - `isOwnNetworkAddress(_ ip: String) -> Bool` (:730)
     - `promptDomain(why:prompt:) -> String` (:125)
     - `promptCaptureDuration() -> Int` (:532)
   - **Move to SyxxKit** (used by both CLI and GUI):
     - `cageCapturesDir() -> URL` (CLI:603, GUI AppModel:603)
     - `newestCaptureFile(in:after:) -> String?` (CLI:616, GUI AppModel)
     - `analyzeMonitorReport(path:inputs:)` (CLI:635, GUI AppModel)

---

## Phase 1: macOS

### Step 1 — Move shared capture helpers into SyxxKit

Only the three helpers shared between CLI and GUI move. Formatting helpers stay in SyxxCLI.

**NEW:** `macOS/Sources/SyxxKit/CageHelpers.swift`

- Move `cageCapturesDir()` from SyxxCLI.swift:603-612
- Move `newestCaptureFile(in:after:)` from SyxxCLI.swift:616-629
- Move `analyzeMonitorReport(path:inputs:)` from SyxxCLI.swift:635-667

**Files touched:**

- NEW: `macOS/Sources/SyxxKit/CageHelpers.swift`
- EDIT: `macOS/Sources/SyxxCLI/SyxxCLI.swift` (delete moved helpers, add `import SyxxKit` if not present)

**Verification:** `swift build` succeeds. Guided flow still works (run interactively, select each option).

---

### Step 2 — Extract TracerouteRunner into SyxxKit

Three duplicate traceroute wrappers exist:

- SyxxCLI.swift:423-445 (guided Portal flow, maxHops=15)
- SyxxRoute.swift:24-66 (standalone tool, maxHops=30)
- AppModel.swift:455-484 (GUI, maxHops=30)

**NEW:** `macOS/Sources/SyxxKit/TracerouteRunner.swift`

```swift
public enum TracerouteRunner {
    /// Runs /usr/sbin/traceroute and returns trimmed output lines.
    /// - Parameters:
    ///   - target: hostname or IP to trace
    ///   - maxHops: maximum TTL (default 30)
    ///   - timeout: seconds before forced termination (default 45)
    /// - Returns: array of trimmed output lines, empty on failure
    public static func trace(target: String, maxHops: Int = 30, timeout: TimeInterval = 45) async -> [String]
}
```

Implementation: extract from SyxxRoute.swift:24-66 (the most complete version with 30-hop max and 45s timeout).

**Files touched:**

- NEW: `macOS/Sources/SyxxKit/TracerouteRunner.swift`
- EDIT: `macOS/Sources/SyxxCLI/SyxxCLI.swift:423-445` → replace with `let lines = await TracerouteRunner.trace(target: domain, maxHops: 15)`
- EDIT: `macOS/Sources/SyxxRoute/SyxxRoute.swift:24-66` → replace with `let lines = await TracerouteRunner.trace(target: target)`
- EDIT: `macOS/Sources/SyxxApp/AppModel.swift:455-484` → replace with `let lines = await TracerouteRunner.trace(target: target)`

**Verification:** `swift build` succeeds. Run `syxx route 8.8.8.8` and verify output matches pre-change. Run guided Portal flow and verify traceroute output matches.

---

### Step 3 — Move command logic into SyxxKit/Commands/

For each of the 11 standalone tools, create a command type in SyxxKit.

**NEW files (11):**

| File | Source | Lines | Key dependencies |
|------|--------|-------|------------------|
| `SyxxKit/Commands/NSLookCommand.swift` | SyxxNSLook.swift | 116 | DNSResolver, SyxxConfig |
| `SyxxKit/Commands/MirrorCommand.swift` | SyxxMirror.swift | 84 | VantageComparer, NetworkKeyState |
| `SyxxKit/Commands/LeakCommand.swift` | SyxxLeak.swift | 136 | LeakDetector, NetworkKeyState, LicenseManager |
| `SyxxKit/Commands/WitnessCommand.swift` | SyxxWitness.swift | 123 | WitnessSigner, WitnessKeyManager, NetworkKeyState |
| `SyxxKit/Commands/KeyCommand.swift` | SyxxKey.swift | 65 | NetworkKeyState |
| `SyxxKit/Commands/RouteCommand.swift` | SyxxRoute.swift | 76 | TracerouteRunner, isSafeTraceTarget |
| `SyxxKit/Commands/LanCommand.swift` | SyxxLan.swift | 57 | LANCartographer, NetworkKeyState |
| `SyxxKit/Commands/ForensicsCommand.swift` | SyxxForensics.swift | 124 | SieveBlocklistLoader, BlocklistAnalyzer, BeaconAnalyzer, ExfilAnalyzer |
| `SyxxKit/Commands/SentinelCommand.swift` | SyxxSentinel.swift | 51 | Sentinel, NetworkConfigSnapshot, AnswerLedger |
| `SyxxKit/Commands/CageCommand.swift` | SyxxCage.swift | 316 | Ccage, DNSCapture, MonitorReportAnalyzer |
| `SyxxKit/Commands/WarpCommand.swift` | SyxxWarp.swift | 129 | ContentWarp, LicenseManager |

**Protocol (in CLIHelpers.swift):**

```swift
public protocol SyxxCommand {
    static func run(args: [String]) async
}
```

**Extraction method:** For each tool, take the `main()` body (after the `handleGlobalFlags` guard and argument parsing), wrap it in `static func run(args: [String]) async`, and move it to the command type. The argument parsing stays inline (each command parses its own args from the `args` parameter).

**Order of extraction (dependency-aware):**

1. KeyCommand — simplest, no sub-dependencies
2. LanCommand — uses LANCartographer only
3. SentinelCommand — uses Sentinel only
4. NSLookCommand — uses DNSResolver
5. MirrorCommand — uses VantageComparer
6. RouteCommand — uses TracerouteRunner (created in Step 2)
7. WitnessCommand — uses WitnessSigner
8. LeakCommand — uses LeakDetector, LicenseManager
9. ForensicsCommand — uses multiple analyzers
10. WarpCommand — uses ContentWarp, LicenseManager
11. CageCommand — most complex, uses Ccage, needs privilege detection

**Files deleted (11):**

- `macOS/Sources/SyxxNSLook/SyxxNSLook.swift`
- `macOS/Sources/SyxxMirror/SyxxMirror.swift`
- `macOS/Sources/SyxxLeak/SyxxLeak.swift`
- `macOS/Sources/SyxxWitness/SyxxWitness.swift`
- `macOS/Sources/SyxxKey/SyxxKey.swift`
- `macOS/Sources/SyxxRoute/SyxxRoute.swift`
- `macOS/Sources/SyxxLan/SyxxLan.swift`
- `macOS/Sources/SyxxForensics/SyxxForensics.swift`
- `macOS/Sources/SyxxSentinel/SyxxSentinel.swift`
- `macOS/Sources/SyxxCage/SyxxCage.swift`
- `macOS/Sources/SyxxWarp/SyxxWarp.swift`

**Directories deleted:**

- `macOS/Sources/SyxxNSLook/`
- `macOS/Sources/SyxxMirror/`
- `macOS/Sources/SyxxLeak/`
- `macOS/Sources/SyxxWitness/`
- `macOS/Sources/SyxxKey/`
- `macOS/Sources/SyxxRoute/`
- `macOS/Sources/SyxxLan/`
- `macOS/Sources/SyxxForensics/`
- `macOS/Sources/SyxxSentinel/`
- `macOS/Sources/SyxxCage/`
- `macOS/Sources/SyxxWarp/`

**Verification:** `swift build` succeeds. Each command works via `swift run syxx <subcommand>`. Run all 11 subcommands with real input and verify output matches pre-change.

---

### Step 4 — Rewrite SyxxCLI.swift entry point

Replace the current `main()` with subcommand dispatch. Keep the interactive mode as `runInteractiveMode()`.

**REWRITE:** `macOS/Sources/SyxxCLI/SyxxCLI.swift`

```swift
@main
struct SyxxCLI {
    static func main() async {
        let args = CommandLine.arguments
        if args.count < 2 || args[1] == "guided" {
            await runInteractiveMode()
            return
        }
        switch args[1] {
        case "nslook":    await NSLookCommand.run(args: Array(args.dropFirst(2)))
        case "mirror":    await MirrorCommand.run(args: Array(args.dropFirst(2)))
        case "leak":      await LeakCommand.run(args: Array(args.dropFirst(2)))
        case "witness":   await WitnessCommand.run(args: Array(args.dropFirst(2)))
        case "key":       await KeyCommand.run(args: Array(args.dropFirst(2)))
        case "route":     await RouteCommand.run(args: Array(args.dropFirst(2)))
        case "lan":       await LanCommand.run(args: Array(args.dropFirst(2)))
        case "forensics": await ForensicsCommand.run(args: Array(args.dropFirst(2)))
        case "sentinel":  await SentinelCommand.run(args: Array(args.dropFirst(2)))
        case "cage":      await CageCommand.run(args: Array(args.dropFirst(2)))
        case "warp":      await WarpCommand.run(args: Array(args.dropFirst(2)))
        case "--version", "-V": print("syxx \(SyxxConfig.version)")
        case "--help", "-h":    printUsage()
        default:
            print("Unknown command: \(args[1]). Run 'syxx --help'.")
            exit(1)
        }
    }
}
```

**Cage privilege escalation in guided flow:**
The `runCageCapture()` function (SyxxCLI.swift:550-581) currently calls `locateSiblingTool("syxx-cage")` and shells out with sudo. Replace with:

```swift
static func runCageCapture(durationSeconds: Int) async -> String? {
    let startTime = Date()
    let process = Process()
    if geteuid() == 0 {
        // Already root — run cage capture directly
        return await CageCommand.runCaptureOnly(durationSeconds: durationSeconds)
    } else {
        // Need sudo — re-exec self with "cage" subcommand
        print("• Live packet capture needs administrator rights — you may be prompted for your password.")
        process.executableURL = URL(fileURLWithPath: "/usr/bin/sudo")
        process.arguments = [CommandLine.arguments[0], "cage", "--duration", String(durationSeconds), "--keep"]
        process.standardOutput = FileHandle.standardOutput
        process.standardError = FileHandle.standardError
        do { try process.run(); process.waitUntilExit() }
        catch {
            print("⚠️  Live capture could not start: \(error.localizedDescription).")
            return nil
        }
    }
    return newestCaptureFile(in: cageCapturesDir(), after: startTime)
}
```

Delete `locateSiblingTool()` (:586-598).

**Files touched:**

- REWRITE: `macOS/Sources/SyxxCLI/SyxxCLI.swift` (entry point + delete helpers moved in Step 1)

**Verification:** `swift build` succeeds. `syxx` (no args) enters interactive mode. `syxx nslook example.com` works. `syxx cage --duration 3 --keep` prompts for sudo and captures.

---

### Step 5 — Update Package.swift

**EDIT:** `macOS/Package.swift`

Remove 12 executable targets. Keep:

- `.executable(name: "syxx", dependencies: ["SyxxKit", "Ccage"], path: "Sources/SyxxCLI")`
- `.executableTarget(name: "SyxxApp", dependencies: ["SyxxKit"], path: "Sources/SyxxApp", resources: [.copy("Resources")])`

Also delete `SyxxDstProbe` target (temporary diagnostic harness).

**Files touched:**

- EDIT: `macOS/Package.swift`

**Verification:** `swift build` succeeds. `swift build --target syxx` succeeds. `swift build --target SyxxApp` succeeds.

---

### Step 6 — Update GUI cage launch

**EDIT:** `macOS/Sources/SyxxApp/AppModel.swift:614`

Current: constructs osascript command referencing `bin.path` (the `syxx-cage` binary).
New: references the unified binary with `cage` subcommand.

```swift
// BEFORE (line 614):
let script = "do shell script \"'\(bin.path)' --duration \(duration) --keep --output '\(outputURL.path)' --owner-uid \(getuid()) --owner-gid \(getgid())\" with administrator privileges"

// AFTER:
let script = "do shell script \"'\(bin.path)' cage --duration \(duration) --keep --output '\(outputURL.path)' --owner-uid \(getuid()) --owner-gid \(getgid())\" with administrator privileges"
```

The `bin.path` must now resolve to the unified `syxx` binary, not `syxx-cage`. Verify that `Bundle.main.executablePath` or the equivalent resolves correctly.

**Files touched:**

- EDIT: `macOS/Sources/SyxxApp/AppModel.swift:614`

**Verification:** GUI launches, cage capture works from GUI, live-tail updates during capture.

---

### Step 7 — Update GUI text references to standalone binary names

13 references to standalone binary names in the GUI must be updated to reflect the unified syntax.

**Files and lines to edit:**

| File | Line | Current text | New text |
|------|------|-------------|----------|
| `AppModel.swift` | 360 | `syxx-leak --collector` | `syxx leak --collector` |
| `AppModel.swift` | 500 | `syxx-witness --gen-keypair` | `syxx witness --gen-keypair` |
| `AppModel.swift` | 584 | `syxx-cage` (doc comment) | `syxx cage` |
| `AppModel.swift` | 598 | `syxx-cage capture binary` | `syxx cage` |
| `AppModel.swift` | 810 | `syxx-cage` (doc comment) | `syxx cage` |
| `WitnessView.swift` | 21 | `syxx-witnesssigner, syxx-sentinel, syxx-answerledger` | `syxx witness, syxx sentinel, syxx answerledger` |
| `GluttonView.swift` | 39 | `syxx-leakdetector` | `syxx leak` |
| `CageView.swift` | 54 | `syxx-cage / DNSCapture` | `syxx cage` |
| `CageView.swift` | 113 | `sudo syxx-cage --duration` | `sudo syxx cage --duration` |
| `PortalView.swift` | 18 | `syxx-route` | `syxx route` |
| `PortalView.swift` | 148 | `syxx-key --scan` | `syxx key --scan` |

**Files touched:**

- EDIT: `macOS/Sources/SyxxApp/AppModel.swift` (5 lines)
- EDIT: `macOS/Sources/SyxxApp/WitnessView.swift` (1 line)
- EDIT: `macOS/Sources/SyxxApp/GluttonView.swift` (1 line)
- EDIT: `macOS/Sources/SyxxApp/CageView.swift` (2 lines)
- EDIT: `macOS/Sources/SyxxApp/PortalView.swift` (2 lines)

**Verification:** Grep for `syxx-` across the GUI sources returns zero matches.

---

### Step 8 — Build app bundle and verify

**Commands:**

```bash
cd macOS && swift build -c release && swift build --target SyxxApp -c release
```

Verify the app bundle is created and `syxx-gui.app` launches.

**Log outputs:** Capture all CLI and GUI output to `outputs-catalog.csv`.

---

## Phase 2: Windows Parity

### Step 1 — Move shared helpers into Syxx.Kit

Create `Windows/Syxx.Kit/CLIHelpers.cs` with the Windows equivalents of `triLabel()`, `hostOnly()`, `isSafeTraceTarget()`, `isOwnNetworkAddress()`, `cageCapturesDir()`, `newestCaptureFile()`.

**Files touched:**

- NEW: `Windows/Syxx.Kit/CLIHelpers.cs`

---

### Step 2 — Extract TracerouteRunner into Syxx.Kit

Create `Windows/Syxx.Kit/TracerouteRunner.cs` using `tracert` instead of `traceroute`.

**Files touched:**

- NEW: `Windows/Syxx.Kit/TracerouteRunner.cs`
- EDIT: `Windows/Syxx/Program.cs` (replace inline traceroute)
- EDIT: `Windows/SyxxUI/ViewModels/AppViewModel.cs` (replace inline traceroute)

---

### Step 3 — Move command logic into Syxx.Kit/Commands/

Create 11 command files in `Windows/Syxx.Kit/Commands/` mirroring the macOS extraction.

**Files touched:**

- NEW: `Windows/Syxx.Kit/Commands/*.cs` (11 files)

---

### Step 4 — Rewrite Program.cs entry point

Add subcommand dispatch to `Windows/Syxx/Program.cs`. Keep the interactive menu as `RunInteractiveMode()`.

**Files touched:**

- REWRITE: `Windows/Syxx/Program.cs`

---

### Step 5 — Update Syxx.sln

Remove 11 standalone CLI project references. Keep `Syxx` (unified CLI), `Syxx.Kit`, `SyxxUI`, `Syxx.Tests`.

**Files touched:**

- EDIT: `Windows/Syxx.sln`

---

### Step 6 — Update GUI cage launch

**EDIT:** `Windows/SyxxUI/ViewModels/AppViewModel.cs` — update cage launch to use unified binary with `cage` subcommand.

---

### Step 7 — Build and verify

**Commands:**

```bash
dotnet build Windows/Syxx.sln
dotnet test Windows/Syxx.Tests/
```

Verify all tests pass. Log outputs to `outputs-catalog.csv`.

---

## Tests

### What to test and failure cases

**For each subcommand, test:**

1. Happy path: valid input → correct output
2. Failure path: invalid input → honest error message (not crash)
3. Edge case: empty args → usage text

**Specific failure cases:**

| Subcommand | Happy path input | Failure input | Expected failure output |
|------------|------------------|---------------|------------------------|
| `nslook` | `syxx nslook google.com` | `syxx nslook` (no args) | Usage text, not crash |
| `nslook` | `syxx nslook google.com MX @8.8.8.8` | `syxx nslook google.com ZZZ` | "Unsupported record type" error |
| `mirror` | `syxx mirror google.com` | `syxx mirror` (no args) | Usage text |
| `key` | `syxx key` | `syxx key --invalid` | Graceful handling |
| `route` | `syxx route 8.8.8.8` | `syxx route -evil` | "target must be a plain hostname" |
| `cage` | `sudo syxx cage --duration 3` | `syxx cage --duration 0` | Re-prompt or error |
| `warp` | `syxx warp https://example.com` | `syxx warp` (no args) | Usage text |
| `witness` | `syxx witness --gen-keypair` | `syxx witness` (no args) | Usage text |
| `forensics` | `syxx forensics --lists` | `syxx forensics` (no args) | Usage text |
| `leak` | `syxx leak` | `syxx leak --servers` (no values) | Graceful handling |
| `sentinel` | `syxx sentinel` | N/A (always works) | First run saves baseline |

**Guided flow test:**

- Run `syxx` interactively
- Select each option (1-6, C, L, Q)
- Verify each produces real output (not empty, not hardcoded)
- Verify Conclusions (C) shows evidence only after running checks

**Subprocess consolidation verification:**

- Verify `TracerouteRunner.trace()` produces the same output as the old inline code
- Verify no `Process()` spawns remain outside of TracerouteRunner and CageCommand

---

## Batch Permission Request

The following ~28 commands will be executed during implementation. Pre-approved scope:

```bash
# Phase 1, Step 1: Move shared helpers
cat > /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxKit/CLIHelpers.swift << 'EOF'
...
EOF
swift build --target SyxxKit 2>&1 | tail -3

# Phase 1, Step 2: Extract TracerouteRunner
cat > /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxKit/TracerouteRunner.swift << 'EOF'
...
EOF
swift build --target SyxxKit 2>&1 | tail -3

# Phase 1, Step 3: Create 11 command files
for cmd in NSLook Mirror Leak Witness Key Route Lan Forensics Sentinel Cage Warp; do
  cat > /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxKit/Commands/${cmd}Command.swift << EOF
  ...
  EOF
done
swift build --target SyxxKit 2>&1 | tail -3

# Phase 1, Step 4: Rewrite SyxxCLI entry point
cat > /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxCLI/SyxxCLI.swift << 'EOF'
...
EOF
swift build --target syxx 2>&1 | tail -3

# Phase 1, Step 5: Update Package.swift
swift build 2>&1 | tail -5

# Phase 1, Step 6: Update GUI cage launch
# Edit AppModel.swift:614
swift build --target SyxxApp 2>&1 | tail -3

# Phase 1, Step 7: Build release + app bundle
swift build -c release 2>&1 | tail -5
swift build --target SyxxApp -c release 2>&1 | tail -5

# Cleanup: delete old directories
rm -rf /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxNSLook/
rm -rf /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxMirror/
rm -rf /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxLeak/
rm -rf /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxWitness/
rm -rf /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxKey/
rm -rf /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxRoute/
rm -rf /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxLan/
rm -rf /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxForensics/
rm -rf /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxSentinel/
rm -rf /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxCage/
rm -rf /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxWarp/
rm -rf /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxDstProbe/

# Verify no standalone binary references remain in GUI
grep -rn "syxx-" /Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxApp/ --include="*.swift"

# Phase 2: Windows parity (similar batch)
dotnet build /Users/masterrabbit/Software/Syxx/Windows/Syxx.sln 2>&1 | tail -5
dotnet test /Users/masterrabbit/Software/Syxx/Windows/Syxx.Tests/ 2>&1 | tail -10
```

---

## DESIGN.md Update

After implementation, update `DESIGN.md` to reflect:

- Single binary architecture (`syxx` with subcommands)
- Updated CLI entry points (no more standalone binaries)
- Updated cage launch mechanism (unified binary re-exec)

---

## outputs-catalog.csv

Log all CLI and GUI output during verification to `outputs-catalog.csv`:

```bash
# Capture each subcommand's output
for cmd in "nslook google.com" "mirror google.com" "key" "route 8.8.8.8" "lan" "forensics --lists" "sentinel" "witness --gen-keypair" "warp https://example.com"; do
  echo "syxx $cmd" >> outputs-catalog.csv
  swift run syxx $cmd 2>&1 >> outputs-catalog.csv
  echo "---" >> outputs-catalog.csv
done
```

---

## Verification Checklist

### Phase 1 (macOS)

- [ ] `syxx --help` lists all subcommands (proposed text approved by user)
- [ ] `syxx --version` prints `syxx 2.0.0`
- [ ] `syxx` (no args) enters interactive guided mode
- [ ] `syxx nslook google.com` returns real A records
- [ ] `syxx nslook google.com MX @8.8.8.8` returns MX records
- [ ] `syxx nslook google.com ZZZ` prints "Unsupported record type"
- [ ] `syxx mirror google.com` shows local vs public vantage comparison
- [ ] `syxx leak` dispatches probes and reports results
- [ ] `syxx witness --gen-keypair` generates keys
- [ ] `syxx key` reports resolver/interface/VPN/captive-portal/encrypted-DNS state
- [ ] `syxx route 8.8.8.8` shows traceroute hops
- [ ] `syxx route -evil` prints "target must be a plain hostname"
- [ ] `syxx lan` shows ARP table devices
- [ ] `syxx forensics --lists` shows bundled blocklists
- [ ] `syxx sentinel` saves baseline on first run
- [ ] `sudo syxx cage --duration 3 --keep` prompts for password and captures
- [ ] `syxx warp https://example.com` runs content integrity check
- [ ] Interactive guided mode: all 6 checks produce real output
- [ ] Interactive Conclusions (C): shows evidence after running checks
- [ ] GUI `syxx-gui` launches and cage capture works
- [ ] `grep -rn "syxx-" macOS/Sources/SyxxApp/ --include="*.swift"` returns zero matches
- [ ] `swift test` passes
- [ ] `swift build -c release` succeeds
- [ ] App bundle created and launches

### Phase 2 (Windows)

- [ ] All same subcommands work via `dotnet run --project Syxx -- <subcommand>`
- [ ] `dotnet test` passes
- [ ] GUI cage capture works

### Cross-platform

- [ ] Output comparison: each subcommand produces equivalent output on both platforms
- [ ] No standalone binary names remain in PATH
