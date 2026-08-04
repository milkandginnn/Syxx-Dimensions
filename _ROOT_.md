# Syxx Skill Tree

The root engineering skill for building **Syxx**, a DNS-integrity tool that investigates and detects DNS tampering, hijacking, spoofing, and censorship. Syxx ships a **macOS** client (SwiftUI), a **Windows** client (WinUI 3 / C#), a **Linux** client (Avalonia / C#), and a **Python** server. Load this skill first to orient, then follow the routing table to the specific skill for your task.

**Use when:** coding the macOS, Windows, or Linux client, designing UI/UX, or doing networking / packet-capture work for Syxx.

**Not for:** designing the DNS-integrity detection logic itself (that is the app's problem domain), or any malware / offensive-security work (intentionally excluded).

---

## Syxx Workflow Skills

Project-specific skills for Syxx's development workflow. Load these when performing the corresponding task regardless of platform.

| Skill | Location | Load when |
|---|---|---|
| `syxx-delta-file` | `.claude/skills/syxx-delta-file/` | Filing a defect in DELTAS.md (false verdict, missed detection, forgeable evidence, crash, boolean-where-tri-state) |
| `syxx-honesty-audit` | `.claude/skills/syxx-honesty-audit/` | Auditing a verdict surface (CLI report, GUI view, synthesis output) against the Verification & Honesty Protocol |
| `syxx-parity-check` | `.claude/skills/syxx-parity-check/` | Proving a reasoning/synthesis/scoring change is behavior-identical across macOS, Windows, and Linux twins |
| `syxx-reference-lookup` | `.claude/skills/syxx-reference-lookup/` | Grounding a claim in the project's reference papers (DNS tampering, DNSSEC, FMEA, scoring) before reaching for web search |
| `syxx-skill-development` | `.claude/skills/syxx-skill-development/` | Creating, maintaining, selecting, and using Syxx skills (format, quality standards, routing, inventory) |
| `syxx-agent-development` | `.claude/skills/syxx-agent-development/` | Creating, selecting, coordinating, and using Syxx agents/subagents (roster, dispatch, definitions, lifecycle) |

## Syxx Domain Skills

Cross-cutting domain knowledge skills covering what Syxx does and how it works. Load these when working on the corresponding subsystem.

| Skill | Location | Load when |
|---|---|---|
| `syxx-synthesis-engine` | `.claude/skills/syxx-synthesis-engine/` | Understanding or modifying the synthesis/reasoning engine (SynthesisEngine, Synthesizer, Conclusion, ReportCard, MatrixDeterminativeEngine) |
| `syxx-scoring-mathematics` | `.claude/skills/syxx-scoring-mathematics/` | Working with scoring, confidence calculation, FMEA/RPN, weight rubric, probe budgets, or threshold tuning |
| `syxx-dns-detection-logic` | `.claude/skills/syxx-dns-detection-logic/` | Understanding or modifying any of the 20+ DNS probes (AnswerStability, CNAMEChain, DNSSEC, Filtering, Leak, etc.) |
| `syxx-server-architecture` | `.claude/skills/syxx-server-architecture/` | Working on the Python server (DNS authoritative, leak collector, content-integrity proxy, licensing, database) |
| `syxx-evidence-cryptography` | `.claude/skills/syxx-evidence-cryptography/` | Working on Witness signing, key management, signed evidence payloads, Ed25519 attestation, chain of trust |
| `syxx-gui-architecture` | `.claude/skills/syxx-gui-architecture/` | Understanding the GUI state machine (AppModel/AppViewModel), navigation, verdict rendering pipeline, cross-platform view parity |
| `syxx-ui-design-system` | `.claude/skills/syxx-ui-design-system/` | Working with IoDesignSystem, color palette, typography, severity indicators, verdict visualization, brand guidelines |
| `syxx-data-persistence` | `.claude/skills/syxx-data-persistence/` | Working with configuration storage, settings, answer ledger, license state, server database, cross-platform settings parity |
| `syxx-cross-platform-parity` | `.claude/skills/syxx-cross-platform-parity/` | Ensuring changes maintain parity across macOS (Swift), Windows (C#), and Linux (C#); CI parity gate; platform-conditional code |
| `macos-tahoe-ui-implementation` | `.claude/skills/macos-tahoe-ui-implementation/` | Implementing SwiftUI views for the macOS GUI (AppModel, ContentView, toolset views, panels, Liquid Glass) |
| `windows-ui-implementation` | `.claude/skills/windows-ui-implementation/` | Implementing WinUI 3/XAML views for the Windows GUI (AppViewModel, MainWindow, toolset views, MVVM, Fluent Design) |

---

## macOS (SwiftUI client)

Each skill lives in `skills/` and carries its own "When to Use" section and full technical depth.

| Dir | Skill | Load when |
|---|---|---|
| `07-` | `macos-tahoe-crypto-signing` | Signing/notarizing the macOS build; Gatekeeper/notarization failures; verifying a macOS binary's signature |
| `10-` | `macos-tahoe-native-runtimes` | Leaks/retain cycles/data races in Swift/Obj-C/C; dispatch, swizzling, dyld, Swift concurrency |
| `11-` | `macos-tahoe-managed-runtimes` | Embedding Python/Ruby/JS/JVM; interpreter or subprocess isolation |
| `12-` | `macos-tahoe-scripting-automation` | Shell/AppleScript, `launchd` agents/daemons, scheduled jobs |
| `13-` | `macos-tahoe-web-scripting` | `WKWebView`, JS↔Swift bridges, CSP/origin isolation |
| `14-` | `macos-tahoe-network-forensics` | BPF/libpcap DNS/packet capture for Cage; per-process attribution |
| `19-` | `macos-tahoe-xcode-tooling` | Xcode/SwiftPM/CocoaPods builds; `otool`/`nm`/`lipo` Mach-O inspection |
| `20-` | `macos-tahoe-ecosystem-tools` | Homebrew, CLI developer tooling, coexistence with third-party security software |
| `23-` | `macos-tahoe-ui-ux` | SwiftUI/Liquid Glass UI, menu-bar/status-item, alerts, graphic design |
| `28-` | `macos-tahoe-network-protocols` | Sockets/TCP/UDP/DNS client, Network.framework, `URLSession` |
| `29-` | `macos-tahoe-swift-sdk-conventions` | Idiomatic Swift, API design, `async`/`await` conventions |

> All macOS skills live under `skills/` (e.g. `skills/07-macos-tahoe-crypto-signing/SKILL.md`).

## Windows (WinUI 3 / C# client)

| Skill | Load when |
|---|---|
| `windows-crypto-signing` | Authenticode signing; SmartScreen; verifying a Windows binary's signature |
| `windows-native-runtimes` | PE/Win32/C++, P/Invoke & COM interop, NativeAOT |
| `windows-managed-runtimes` | .NET CLR/GC/JIT, assembly loading, runtime hosting |
| `windows-scripting-automation` | PowerShell, Task Scheduler, WMI/CIM |
| `windows-web-scripting` | WebView2, host↔JS interop, CSP |
| `windows-network-forensics` | Npcap/WinDivert/pktmon/ETW DNS capture for Cage |
| `windows-toolchain-tooling` | Visual Studio/MSBuild/`dotnet` CLI, NuGet supply-chain integrity |
| `windows-ecosystem-tools` | winget/NuGet/Sysinternals/Windows Terminal |
| `windows-ui-ux` | WinUI 3/XAML/Fluent Design, tray, toasts, alert dialogs |
| `windows-network-protocols` | Winsock, IP Helper API, `DnsQuery`, WFP |
| `windows-dotnet-sdk-conventions` | C#/.NET SDK conventions, nullable, `async`/`await` |

> **Status:** Windows skills are being finalized. SKILL.md files are not yet written. Until then, reference the macOS equivalent and adapt for Win32/.NET.

## Linux (Avalonia / C# client)

Linux skills have not yet been created. The Linux client shares the C# codebase with Windows (`Syxx.Kit`). Reference Windows skills and adapt for Linux-specific APIs: Netlink, `resolv.conf`, `iptables`/`nftables`, `systemd-resolved`, AppArmor, `libpcap` (Linux variant).

---

## Notes

- macOS skill directories use numeric prefixes (07, 10, 11, etc.) for sort ordering. The numbers are local to `skills/` and not global IDs.
- `.claude/skills/` is the canonical location for Syxx workflow skills.
- When creating a new skill, follow the format of existing macOS skills: YAML frontmatter (`name`, `description`), a "When to Use" section, and numbered technical sections with code examples.
- Syxx workflow skills (delta-file, honesty-audit, parity-check, reference-lookup) are registered with the active IDE's skill system and do not need manual loading.
