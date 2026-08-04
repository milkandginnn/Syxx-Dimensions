# Branch model

**Trunk-based, with per-platform release branches.** All day-to-day work happens on the
trunk; the `release/*` branches exist to cut and stabilise a shipment for one target OS.

## Why the platforms are NOT separate development branches

This was considered and deliberately rejected. The codebase is one tree with `macOS/`,
`Windows/`, `Server/`, and `Ubuntu/` directories, and three hard constraints bind them
together:

1. **Platform parity is a per-commit rule** (`AGENTS.md:36`). A change to `SyxxKit`
   almost always needs its `Syxx.Kit` twin *in the same commit*. On separate branches
   that change cannot be atomic, and the engine goes transiently divergent — exactly the
   state the honesty protocol forbids.
2. **The Stage-0 golden corpus is a single source of truth.** `corpus/stage0/seeds.json`
   is read by BOTH `Windows/Syxx.Tests/Stage0GoldenCorpusTests.cs` and
   `macOS/Tests/SyxxKitTests/Stage0GoldenCorpusTests.swift` so a fifth copy of the seed
   list cannot fork. Per-platform branches would fork it by construction.
3. **Linux builds FROM the Windows tree.** There is no separate Linux source tree by
   design: `Windows/Syxx.Linux.slnf` builds `Syxx.Kit` + the `syxx` CLI + tests and
   excludes the WinUI GUI. A "Linux branch" separated from Windows could not build.

## The branches

| Branch | Target | Notes |
|---|---|---|
| `feat/beta-signup-and-server-hardening` | — | **Trunk.** All development lands here. |
| `release/macos-26-tahoe` | macOS 26 (Tahoe) | Per `AGENTS.md:36,56`. `Package.swift` min is `.v14`; 26 is the ship target. |
| `release/windows-11` | Windows 11 | `SyxxUI.csproj` targets `net11.0-windows10.0.26100.0` (build 26100 = 11 24H2). |
| `release/mint-ubuntu-26.04` | Ubuntu 26.04 / Mint family | Per `Ubuntu/UBUNTU-PORT-PLAN.md` (target ISO `ubuntu-26.04-desktop-arm64`; snap base `core24` → `core26` when 26.04 lands). **No document pins a Mint point-version** — Mint tracks the Ubuntu base as a family member (`AGENTS.md:36` "Mint Linux/Ubuntu/Debian"). Rename once Mint's own version is decided. |
| `release/server` | *(intentionally unversioned)* | The server is **distro-agnostic by design**: `scripts/deploy.sh` supports apt/dnf/yum/apk/zypper/pacman and explicitly handles a non-Debian box. There is no target OS version to pin. |
| `main` | — | Pre-existing. |
| `fix/delta-remediation` | — | Pre-existing. |

## Rules

- **Work on trunk.** Parity changes (`SyxxKit` + `Syxx.Kit`, and the Linux slnf) stay atomic.
- **Cut a `release/*` at ship time** from trunk, then only stabilise on it.
- **Fixes go to trunk first**, then cherry-pick onto the release branch — never the reverse,
  or a platform silently diverges from its twin.
- A release branch must not be the only home of an engine change. If it is, the parity rule
  has already been broken.
