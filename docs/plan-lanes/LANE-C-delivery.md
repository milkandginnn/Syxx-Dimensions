# LANE C — Delivery: packaging, CI, tests

Seat E3. Planning only: nothing in this document has been applied. Every file:line below was
re-verified against the working tree on 2026-08-04.

**Goal for this lane:** a Linux user who installs Syxx gets the *same two executables* macOS and
Windows users get, the package refuses to build if either is missing, and CI proves it by
installing the artifact and reading exit codes — not by reading the script.

---

## 0. The question, answered first

> **Should the deb and the snap both ship GUI+CLI, or should the snap be renamed?**

**Both ship GUI+CLI. Do not rename the snap. If the snap cannot be verified on a real Ubuntu box
this cycle, delete `Linux/snap/snapcraft.yaml` rather than ship it or rename it.**

Reasoning, in the order the reasons matter:

1. **Parity is defined by the other two platforms, and both ship both.** `macOS/bundle-app.sh:18`
   copies the GUI to `Contents/MacOS/Syxx`; `:23` copies the CLI to `Contents/Resources/syxx`; `:29-33`
   fails the bundle if the CLI is absent. `build-installer.ps1:58-63` publishes the GUI,
   `:74-78` publishes the CLI into the same directory, `:86-89` fails if `Syxx.exe` is missing. One
   install, two binaries, on both shipped platforms. A Linux package that ships one of them is not
   at parity regardless of which one it ships.

2. **Renaming the snap does not fix the defect; it renames it.** A `syxx-cli` snap and a `syxx` deb
   are still two products. `UpdateService.cs:58-59` maps *every* Linux host to platform string
   `"deb"` unconditionally, and `:399-402` shells out to `dpkg -i`. A snap-installed user would be
   offered, downloaded, and handed a `.deb` — cross-package-manager corruption. Renaming makes the
   two identities explicit but leaves the snap with no update path at all. That is worse, not
   better, because it looks deliberate.

3. **The snap's honest problem is confinement, not naming.** `snapcraft.yaml:35-38` already states
   plainly that no plug grants `CAP_NET_RAW`, so `syxx cage` under strict confinement must report
   *untested*, never clean. That is an argument for the snap being a **secondary** channel with a
   documented capability ceiling, not for it being a different product. The deb can grant the
   capability via `postinst` + `setcap` (task C5); the snap cannot. Ship both, document the delta.

4. **The snap has never been built.** `snapcraft.yaml:20-24` admits the `command:` stage path is
   UNVERIFIED and that snapcraft has never run on a real Ubuntu box. Nothing in
   `.github/workflows/ci.yml` invokes `snapcraft` (zero grep hits). An unbuilt package definition
   carrying a hardcoded version (`:3`, `'2.0.0'`) held to `SyxxConfig.cs:13` by a *comment* is the
   same defect class as `Linux/dotNET/Syxx.Linux/`: a plausible-looking artifact that no automation
   touches. **If C20's one real snapcraft run does not happen this cycle, delete the file.** Deleting
   is honest; shipping unverified is not; renaming is unverified plus a second name to maintain.

**Decision rule for the owner:** commit to C20 (one `snapcraft` run on a real Ubuntu box) or commit
to `git rm Linux/snap/snapcraft.yaml`. Do not pick a third option.

---

## 1. Corrections to the brief

Two items in the seat brief need adjusting before anyone acts on them.

- **`Makefile` has no deb/snap/linux target — but that Makefile is the wrong Makefile.**
  `/Users/masterrabbit/Software/Syxx/Makefile` is the *claude-agents marketplace* Makefile
  (targets at `:166 generate`, `:186 validate`, `:193 garden`, `:197 test`, `:204 smoke-test`,
  per `CLAUDE.md`). Adding `deb:` / `snap:` targets there would put Syxx build verbs into the
  plugin-marketplace surface, which is a category error. **Do not touch the root Makefile.**
  Document the Linux build commands in `docs/BUILDING.md` (which already carries the CI path
  annotations, e.g. `docs/BUILDING.md:329`) and, if a make-like entry point is wanted, create
  `Linux/Makefile`. Tracked as C22 (optional).

- **`AssemblyName` collision makes "just add the CLI to the deb" impossible as written.**
  `SyxxUI.Avalonia.csproj:20` and `Syxx.csproj:8` **both** declare `<AssemblyName>Syxx</AssemblyName>`.
  Publishing both into `${PKG_DIR}/opt/syxx/` (`build_deb.sh:59,65`) would have the second `cp -R`
  overwrite the first. Renaming the Avalonia assembly is *not* an option: `SyxxUI.Avalonia.csproj:25-28`
  documents that the avares root **is** the AssemblyName, so `avares://Syxx/Assets/...` URIs break.
  The fix is a **directory split**, which is exactly what macOS does (`MacOS/Syxx` vs
  `Resources/syxx`, `bundle-app.sh:18` vs `:23`). Windows avoids the collision only because
  `SyxxUI.csproj:23` is `<AssemblyName>SyxxUI</AssemblyName>`. Baked into C2.

---

## 2. The invariant this lane is enforcing

On both shipped platforms, **`syxx` on a command line is the CLI**:

| platform | GUI | CLI on PATH | fail-closed assert |
|---|---|---|---|
| macOS | `Syxx.app/Contents/MacOS/Syxx` | `Syxx.app/Contents/Resources/syxx` | `bundle-app.sh:29-33` (D073) |
| Windows | `SyxxUI.exe` | `Syxx.exe` | `build-installer.ps1:86-89` (D114) |
| Linux (today) | `/opt/syxx/Syxx` → **also `/usr/bin/syxx`** | **absent** | **none** |
| Linux (target) | `/opt/syxx/gui/Syxx` → `/usr/bin/syxx-gui` | `/opt/syxx/cli/Syxx` → `/usr/bin/syxx` | C3 |

Today `build_deb.sh:68-72` writes `exec /opt/syxx/Syxx "$@"` pointing at an
`<OutputType>WinExe</OutputType>` Avalonia app (`SyxxUI.Avalonia.csproj:13`). `dpkg -i` succeeds,
`which syxx` resolves, and `syxx witness verify report.json` opens a window instead of returning an
exit code. Every task below exists to make that state unreachable.

---

## 3. Dependency order

```
C1  decoy removal ─────────────┐
C1b decoy guard                │
                               ▼
C2  deb payload split ──► C3 fail-closed assert ──► C7 package-linux job ──► C8 install+exit-code
                     └──► C4 Depends            ──────────────────────────► C9 minimal-container
                     └──► C5 maintainer scripts ─────────────────────────► C8
                     └──► C6 EULA/copyright     ─────────────────────────► C11
C10 version gate (independent) ────────────────────────────────────────────► C16
C11 eula wiring (independent)
C12..C18 tests (independent of packaging; C12 depends on C1 only for tidiness)
C19 honesty gate extension  ──► needs C13/C14/C15 landed first (it registers their guards)
C20 snap decision           ──► after C2 (reuses the two-app layout)
C21 owner runbook           ──► after C7/C8 produce an artifact to install
```

Recommended sequencing: **C1, C1b → C2, C3 → C7, C8 → C4, C9 → C5, C6 → C10, C11 → C12–C18 →
C19 → C20 → C21.** C12–C18 can run fully in parallel with the packaging chain; they share no files
with it.

---

## 4. Tasks

### Tier 0 — remove the trap

---

#### C1 — Delete the `Syxx.Linux` decoy
**Effort: S · verifiable-in-CI · no dependencies**

**What changes.** `git rm -r Linux/dotNET/Syxx.Linux/` (and the stale `Linux/dotNET/.DS_Store`).

**Why, with evidence.** `Linux/dotNET/Syxx.Linux/Program.cs` dispatches 8 verbs and every handler
prints a placeholder: `:111` Prophet, `:119` Mirror, `:126` Portal, `:132` Glutton, `:141` Cage,
`:148` Witness, `:156` Warp, `:164` Route — all `"(… implementation pending)"`. The real CLI
`Windows/dotNET/Syxx/Program.cs:75-101` dispatches 16 verbs (`nslook`, `mirror`, `leak`, `witness`,
`key`, `route`, `lan`, `forensics`, `sentinel`, `cage`, `warp`, `speed`, `unbound`, `filter`,
`firstrun`, `parity-transcript`) — and note the decoy's verb *names* (`prophet`, `portal`,
`glutton`) do not even exist in the real CLI, so it is not a subset, it is a different vocabulary.

Grep confirms zero build references: searching `Syxx\.Linux` across `*.csproj|*.sln|*.slnf|*.yml|*.sh|*.ps1|*.yaml`
returns only `Syxx.Linux.csproj:6` (its own `RootNamespace`), plus three hits on the *unrelated
file* `Windows/Syxx.Linux.slnf` (`ci.yml:88,92,314`, `SyxxUI.Tests.csproj:11`). And
`Windows/Syxx.Linux.slnf` lists `dotNET\Syxx\Syxx.csproj` — the real CLI — never the decoy.

Its `Syxx.Linux.csproj:5` declares `<AssemblyName>syxx</AssemblyName>` (lowercase), differing in
case from the shipping `Syxx.csproj:8` `<AssemblyName>Syxx</AssemblyName>`. It also has a live
`ProjectReference` to `Syxx.Kit` (`:10`), so it *compiles* — there are stale build outputs at
`Linux/dotNET/Syxx.Linux/obj/Release/net10.0/syxx.dll` proving someone has built it.

**Reference mechanism.** None needed — this is deletion. The parallel is `ci.yml:9-15`, which
documents the previous no-op `parity` job: a thing that looks like a deliverable and measures
nothing. Same class.

**What makes it FAIL.** N/A (deletion). Its guard is C1b.

**Note for the lane lead.** Anyone assigned "implement the Linux CLI" must be redirected to
`Windows/dotNET/Syxx/` before C1 lands, or they will implement into a directory nothing builds.
This is the highest-urgency item in the lane despite being the smallest.

---

#### C1b — Orphan-project guard
**Effort: S · verifiable-in-CI · depends on C1**

**What changes.** New `scripts/check-orphan-projects.py`, wired into the existing `parity` job
(`ci.yml:413-435`) as a third step alongside `cross-platform-parity-gate.py` (`:427-428`) and
`package-pin-parity.py` (`:434-435`).

**Behavior.** Enumerate every `*.csproj` under the repo (excluding `.worktrees/`, `obj/`, `bin/`).
For each, assert it is reachable from at least one of: `Windows/Syxx.sln`, `Windows/Syxx.Linux.slnf`,
a `ProjectReference` from a reachable project, or an explicit allow-list in the script. Exit 1
listing any project that is in the tree but in no build graph.

**What makes it FAIL.** Add a new `.csproj` under `Linux/` that no solution references → red.
Re-add `Syxx.Linux/` → red. **Anti-vacuity:** if the enumeration finds **zero** csproj files, or
cannot parse `Windows/Syxx.sln`, exit 1 — never exit 0 without having enumerated something. Model
this on the anti-vacuous guard at `ci.yml:458-466`, which fails when the transcript glob is empty
rather than passing on nothing found.

**CI shape.**
```yaml
      # parity job, after ci.yml:435
      - name: Orphan-project gate (no .csproj outside the build graph)
        run: python3 scripts/check-orphan-projects.py --github
```

---

### Tier 1 — deb payload correctness

---

#### C2 — Publish the CLI into the .deb and split the payload
**Effort: M · verifiable-in-CI · depends on C1**

**What changes.** `Linux/Ubuntu/build_deb.sh`.

1. **`:47-52`** — currently one publish, the Avalonia GUI only:
   ```
   cd "${ROOT}/Linux/Ubuntu/SyxxUI.Avalonia"
   dotnet publish -c "$CONFIG" -r "$RID" --self-contained true -o "${ROOT}/dist/publish"
   ```
   Becomes two publishes into two output dirs (`dist/publish-gui`, `dist/publish-cli`). The CLI
   publish must use **exactly the flags `ci.yml:126-131` already uses**:
   `-r $RID --self-contained true -p:PublishSingleFile=true`. Using the identical invocation is the
   point: today the binary CI publishes and the binary the .deb ships are produced by different
   commands, so CI's green publish proves nothing about the package. Single-file also keeps
   `/opt/syxx/cli/` to one artifact, so the C3 assertion has an unambiguous target.

2. **`:59`** — `mkdir -p "${PKG_DIR}/opt/syxx"` becomes `mkdir -p "${PKG_DIR}/opt/syxx/gui"
   "${PKG_DIR}/opt/syxx/cli"`. **This split is mandatory, not stylistic**: both projects emit
   `Syxx` (`SyxxUI.Avalonia.csproj:20`, `Syxx.csproj:8`), so a shared directory silently
   overwrites one with the other. See §1.

3. **`:65`** — one `cp -R` becomes two, GUI → `/opt/syxx/gui/`, CLI → `/opt/syxx/cli/`.

4. **`:68-72`** — the launcher currently reads `exec /opt/syxx/Syxx "$@"` and points at the GUI.
   Split into two launchers:
   - `/usr/bin/syxx` → `exec /opt/syxx/cli/Syxx "$@"` (**the CLI takes the bare name** — see §2)
   - `/usr/bin/syxx-gui` → `exec /opt/syxx/gui/Syxx "$@"`

   Both `chmod 755` (mirroring `:72`).

5. **`:80`** — `.desktop` `Exec=/usr/bin/syxx` becomes `Exec=/usr/bin/syxx-gui`. `Terminal=false`
   at `:82` stays correct for the GUI entry. Leave `Icon=syxx` and the `:89-93` fail-closed icon
   check untouched — that check is already correct and is the local precedent for C3's shape.

6. **`:143`** — `rm -rf "${PKG_DIR}" "${DIST}/publish"` must clean both new publish dirs, and must
   run **after** the C3 assertions, not before.

**Reference mechanism.** `build-installer.ps1:53-83` — publish GUI, then publish CLI into the
distributable, in that order, each with its own `$LASTEXITCODE` check (`:65-68`, `:80-83`).
`bundle-app.sh:10-11,18,22-23` — same two-binary shape on macOS, with the CLI built by an explicit
third `swift build --product syxx` so it can never be assumed present.

**Size note (declare it, don't discover it).** Two self-contained .NET publishes roughly double the
payload. Expect the .deb to go from ~55–70 MB to ~110–140 MB. That is the correct trade: macOS
ships 25.8 MB GUI + 20.9 MB CLI in one bundle for the same reason. Do **not** "fix" this by
trimming — `Syxx.csproj:12-14` and `SyxxUI.Avalonia.csproj:21-23` both document that NSec and the
DNS reflection paths are trim-hostile and that a trimming drop is the green-build-that-lies class.

**What makes it FAIL.** Nothing yet — C2 is the change, C3 is its gate. Land them together.

---

#### C3 — Fail-closed payload assertion in `build_deb.sh`
**Effort: S · verifiable-in-CI · depends on C2**

**What changes.** New block in `Linux/Ubuntu/build_deb.sh`, inserted after the copies (post-`:65`)
and **before** `dpkg-deb --build` at `:140`.

**Shape — modeled directly on `bundle-app.sh:29-33` and `build-installer.ps1:86-89`:**

```bash
# LANE-C / C3: post-payload assertion. The package must contain BOTH executables, and
# /usr/bin/syxx must resolve to the CLI. Modeled on macOS bundle-app.sh:29-33 (D073) and
# Windows build-installer.ps1:86-89 (D114). Ubuntu had neither, which is how a .deb that
# ships only a GUI reached "installs cleanly, `which syxx` resolves" and was called done.
if [ ! -x "${PKG_DIR}/opt/syxx/cli/Syxx" ]; then
    echo "❌ CLI binary missing or not executable: ${PKG_DIR}/opt/syxx/cli/Syxx" >&2
    echo "   Every non-GUI Syxx feature (witness verify, nslook, key, cage) is unreachable" >&2
    echo "   without it. Refusing to build a package where 'syxx --version' opens a window." >&2
    exit 1
fi
if [ ! -x "${PKG_DIR}/opt/syxx/gui/Syxx" ]; then
    echo "❌ GUI binary missing or not executable: ${PKG_DIR}/opt/syxx/gui/Syxx" >&2
    exit 1
fi
# The bare name must be the CLI on every platform (macOS Resources/syxx, Windows Syxx.exe).
if ! grep -q '^exec /opt/syxx/cli/Syxx "\$@"$' "${PKG_DIR}/usr/bin/syxx"; then
    echo "❌ /usr/bin/syxx does not dispatch to the CLI." >&2
    exit 1
fi
# Fail closed if the CLI cannot even report its own version (native-host builds only;
# a cross-build to a foreign RID legitimately cannot exec, so this is arch-gated).
if [ "${ARCH}" = "$(dpkg --print-architecture 2>/dev/null || echo none)" ]; then
    if ! "${PKG_DIR}/opt/syxx/cli/Syxx" --version >/dev/null 2>&1; then
        echo "❌ ${PKG_DIR}/opt/syxx/cli/Syxx --version did not exit 0." >&2
        exit 1
    fi
fi
```

**What makes it FAIL.** Revert C2's second `dotnet publish` → red. Point the `/usr/bin/syxx`
launcher back at the GUI → red. Break the CLI so `--version` throws → red on native builds.
The arch gate is deliberate: on a cross-build (`SYXX_DEB_ARCH=arm64` from an x64 host, or the
macOS-host path `build_deb.sh:24-27` documents) the exec check reports *inconclusive by omission*
rather than failing — but the three structural checks still run unconditionally.

**Note.** The `:89-93` icon check is the existing local precedent and phrases it exactly right:
"refusing to build a package with a broken launcher entry." Use the same voice.

---

#### C4 — Expand `Depends` to the real runtime surface
**Effort: M · verifiable-in-CI (via C9) · depends on C2**

**What changes.** `Linux/Ubuntu/build_deb.sh:97-116` (`resolve_icu_depends`) and `:125`
(`Depends: ${ICU_DEPENDS}`).

Today `Depends:` resolves to at most `libc6, libicu74|76|72`. That is insufficient in two layers:

- **Any self-contained .NET app** needs `libgcc-s1`, `libstdc++6`, `zlib1g`, `libssl3`
  (`libssl3t64` on 24.04+ — the rename must be handled the same distro-aware way ICU already is).
- **Avalonia** additionally needs `libx11-6`, `libice6`, `libsm6`, `libfontconfig1`, and a GL
  provider (`libgl1 | libglx-mesa0`, plus `libegl1` for the EGL path).

A minimal Ubuntu image or container installs the current package cleanly and then fails at first
exec with a loader error — which reads to a user as "Syxx is broken", not "a dependency is missing".

**Shape.** Keep the existing fail-closed structure at `:97-115` and generalise it: a
`resolve_depends()` that emits a base set plus the distro-probed ICU and OpenSSL sonames, and
`exit 1` with the `SYXX_DEB_DEPENDS` override hint when it cannot probe (mirroring `:108-114`).
Do **not** replace the probe with a hardcoded list — `:95-96` documents that an unconditional
`libicu74` pin produced uninstallable packages, and `snapcraft.yaml:57-58` still carries exactly
that pin (fixed in C20).

**Reference mechanism.** No macOS/Windows twin exists (both ship self-contained runtimes with no
package manager in the loop), so the reference here is `build_deb.sh`'s own `:95-115` — the one
place in the repo that already treats a dependency pin as something to *probe and fail closed on*.

**What makes it FAIL.** C9 (minimal-container install) is this task's gate. Without C9 this change
is unfalsifiable — the fat CI runner has every one of these libraries already, so a wrong list
still passes. **Land C4 and C9 in the same PR.**

---

#### C5 — Maintainer scripts: capabilities, hardware-id, desktop integration
**Effort: M · partly verifiable-in-CI, `setcap` effect is needs-real-Ubuntu-box · depends on C2**

**What changes.** `build_deb.sh:58` currently creates only `${PKG_DIR}/DEBIAN` and the `control`
file at `:119-131`. There is no `postinst`, `prerm`, `postrm`, or `preinst` anywhere in the repo.
Add `postinst` and `prerm` (mode 0755, written into `DEBIAN/` alongside `control`).

**`postinst` must do three things:**

1. **Grant `CAP_NET_RAW` to the CLI.** `setcap cap_net_raw+ep /opt/syxx/cli/Syxx`. This *cannot*
   live in the payload — file capabilities are not preserved through a `.deb` archive, so
   `postinst` is the only correct place. Must be non-fatal: if `setcap` is absent or the filesystem
   does not support xattrs, log it and `exit 0`, because Cage's contract is to report
   **untested**, not to fail the install.

2. **Capture the hardware id.** Invoke the existing, already-written
   `scripts/syxx-capture-hardware-id.sh` (shipped into the payload as `/opt/syxx/bin/syxx-capture-hardware-id.sh`).
   Its own header says so explicitly: *"install-time — the packager (.deb postinst / snap / rpm %post)
   ALREADY runs as root, once. Capture there and the app needs no privilege at all, ever. <- this"*.
   That script writes `/var/lib/syxx/hardware-id`, which is precisely the path
   `MachineFingerprint.LinuxHelperCaptured` reads at `MachineFingerprint.cs:131` (default parameter).
   **Right now that code path can never succeed on any shipped Linux install, because nothing writes
   the file.** The script already handles VM/container failure by writing nothing and exiting 0
   ("A licensing helper must never fail a package install"), so wiring it in is safe.

3. **Refresh desktop integration.** `update-desktop-database` and `gtk-update-icon-cache` (both
   guarded with `command -v`), so the `.desktop` at `:75-85` and the icon at `:93` take effect
   without a re-login.

**`prerm` must** remove the file capability and leave `/var/lib/syxx/hardware-id` alone on upgrade
(remove it only on `purge`, via `postrm`) — otherwise every upgrade re-rolls the machine identity
and burns an activation.

**The Cage capability admission.** `snapcraft.yaml:35-38` is honest about the capability ceiling in
a comment. The `.deb` currently makes no such admission anywhere. Add the equivalent statement in
two places: the `postinst` output when `setcap` fails, and the `control` long description at
`:127-130`. The user-facing contract: *with* `cap_net_raw+ep`, Cage performs a real capture;
*without* it, Cage reports **untested** — never clean, never empty-normal.

**Reference mechanism.** macOS's equivalent privilege story is the SMJobBless helper embedded at
`bundle-app.sh:40` with the `__launchd_plist`/`__info_plist` sections asserted at `:42+` and
re-asserted in CI at `ci.yml:257-281`. The structural lesson to copy is *the assertion*, not the
mechanism: macOS refuses to ship a bundle whose privilege path is structurally broken. Linux should
refuse to ship a package whose `postinst` is missing (assert its presence in C7).

**What makes it FAIL.** C7 asserts `dpkg-deb -e` extracts a `postinst` that is present and
executable and contains `setcap` — remove the maintainer script and the job goes red. The *effect*
of `setcap` (does Cage actually capture?) is **needs-real-Ubuntu-box**, C21 item 3.

---

#### C6 — EULA and copyright in the package
**Effort: S · verifiable-in-CI · depends on C2**

**What changes.** `build_deb.sh` — install `Legal/EULA/Syxx-EULA.txt` to
`/usr/share/doc/syxx/copyright` and `/opt/syxx/Legal/Syxx-EULA.txt`.

**Why.** `scripts/check-eula-parity.py:16-20` names three platform copies, including
`Linux/Ubuntu/SyxxUI.Avalonia/Assets/Legal/Syxx-EULA.txt` (verified present, 6037 bytes, byte-equal
to the canonical). That copy is an `AvaloniaResource` compiled into the GUI assembly — so it reaches
GUI users and no one else. A CLI-only user (the whole point of C2) never sees it, and `dpkg` has no
`copyright` file at all, which is a lintian error and a licensing gap.

**What makes it FAIL.** C7 asserts `dpkg-deb -c` lists `./usr/share/doc/syxx/copyright` and that
its sha256 equals `Legal/EULA/Syxx-EULA.txt`'s. Drift either file → red. This is the packaging-side
complement to C11 (source-side parity).

---

### Tier 2 — CI

---

#### C7 — `package-linux` job mirroring `package-macos`
**Effort: M · verifiable-in-CI · depends on C2, C3**

**What changes.** New job in `.github/workflows/ci.yml`, placed immediately after `package-macos`
(which occupies `:242-309`).

**Why this job is the root cause fix.** `package-macos` asserts the macOS bundle structure on every
PR (`:257-281`). Nothing in any workflow invokes `build_deb.sh` or `snapcraft` — zero grep hits
across `.github/workflows/`. That asymmetry is the mechanical reason the missing-CLI defect survived:
macOS packaging has a gate, Linux packaging has none. `ci.yml:234-241` already writes the rationale
for `package-macos` in exactly these terms ("the .app bundle layout … NEVER exercised by any
automation — which is how D079's missing `__launchd_plist` … went unnoticed"). The same sentence is
true of the .deb today.

**Job shape:**

```yaml
  # LANE-C / C7: Linux packaging structural gate. Mirrors package-macos (ci.yml:242-309).
  # Nothing has ever invoked build_deb.sh from automation, which is why the .deb shipped
  # a GUI at /usr/bin/syxx and no CLI at all.
  package-linux:
    name: Ubuntu .deb build + structural assertions (${{ matrix.arch }})
    strategy:
      fail-fast: false
      matrix:
        include:
          - arch: amd64
            runs-on: ubuntu-latest
          - arch: arm64
            runs-on: ubuntu-24.04-arm
    runs-on: ${{ matrix.runs-on }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: Windows/global.json

      - name: Build the .deb
        run: |
          chmod +x Linux/Ubuntu/build_deb.sh
          Linux/Ubuntu/build_deb.sh release

      - name: Assert exactly one .deb was produced
        run: |
          shopt -s nullglob
          debs=(dist/syxx_*_${{ matrix.arch }}.deb)
          if [ ${#debs[@]} -ne 1 ]; then
            echo "::error title=C7::expected exactly one .deb, found ${#debs[@]}: ${debs[*]}"
            exit 1
          fi
          echo "DEB=${debs[0]}" >> "$GITHUB_ENV"

      - name: Assert payload contains BOTH executables
        run: |
          listing=$(dpkg-deb -c "$DEB")
          if [ -z "$listing" ]; then
            echo "::error title=C7 vacuous::dpkg-deb -c produced no listing"; exit 1
          fi
          fail=0
          for p in ./opt/syxx/cli/Syxx ./opt/syxx/gui/Syxx ./usr/bin/syxx ./usr/bin/syxx-gui \
                   ./usr/share/applications/syxx.desktop \
                   ./usr/share/icons/hicolor/512x512/apps/syxx.png \
                   ./usr/share/doc/syxx/copyright; do
            grep -q " $p\$" <<<"$listing" || { echo "::error title=C7::missing $p"; fail=1; }
          done
          exit $fail

      - name: Assert /usr/bin/syxx dispatches to the CLI, not the GUI
        run: |
          tmp=$(mktemp -d); dpkg-deb -x "$DEB" "$tmp"
          if ! grep -q '/opt/syxx/cli/Syxx' "$tmp/usr/bin/syxx"; then
            echo "::error title=C7::/usr/bin/syxx does not point at the CLI — a user typing"
            echo "::error::'syxx witness verify report.json' would get a GUI window, not an exit code."
            exit 1
          fi
          grep -q '/opt/syxx/gui/Syxx' "$tmp/usr/share/applications/syxx.desktop" \
            || grep -q 'Exec=/usr/bin/syxx-gui' "$tmp/usr/share/applications/syxx.desktop" \
            || { echo "::error title=C7::.desktop Exec does not launch the GUI"; exit 1; }

      - name: Assert maintainer scripts present (C5)
        run: |
          tmp=$(mktemp -d); dpkg-deb -e "$DEB" "$tmp"
          [ -x "$tmp/postinst" ] || { echo "::error title=C7::DEBIAN/postinst missing or not executable"; exit 1; }
          grep -q setcap "$tmp/postinst" || { echo "::error title=C7::postinst does not grant cap_net_raw — Cage can never capture"; exit 1; }
          grep -q syxx-capture-hardware-id "$tmp/postinst" || { echo "::error title=C7::postinst does not capture the hardware id — MachineFingerprint.LinuxHelperCaptured (MachineFingerprint.cs:131) can never succeed"; exit 1; }

      - name: Assert control Version == SyxxConfig.Version (C10)
        run: |
          want=$(grep 'public const string Version' Windows/dotNET/Syxx.Kit/SyxxConfig.cs | sed 's/.*"\(.*\)".*/\1/')
          [ -n "$want" ] || { echo "::error title=C7 vacuous::could not read canonical version"; exit 1; }
          got=$(dpkg-deb -f "$DEB" Version | cut -d- -f1)
          [ "$want" = "$got" ] || { echo "::error title=C7::control Version '$got' != SyxxConfig '$want'"; exit 1; }

      - name: Upload .deb artifact
        uses: actions/upload-artifact@v4
        with:
          name: syxx-${{ matrix.arch }}.deb
          path: dist/syxx_*.deb
          if-no-files-found: error
```

**What makes it FAIL.** Remove the CLI publish → "missing ./opt/syxx/cli/Syxx". Point the launcher
at the GUI → the dispatch assertion. Drop `postinst` → the maintainer-script assertion. Bump
`SyxxConfig.cs:13` without rebuilding → the version assertion. **Anti-vacuity:** empty `dpkg-deb -c`
output, zero or multiple `.deb` files, or an unreadable canonical version each exit 1 rather than
passing — same discipline as `ci.yml:458-466` and `:148-152`.

---

#### C8 — Real install + CLI exit-code smoke on the shipped artifact
**Effort: M · verifiable-in-CI · depends on C7**

**What changes.** Additional steps at the end of `package-linux`.

**Why.** `ci.yml:124-131` calls itself a "Real-run bar" but only proves the CLI *publishes*;
nothing executes it. `ci.yml:222-225` runs exactly one subcommand (`parity-transcript`) on an empty
vector. **Nothing anywhere runs `syxx nslook` / `witness` / `key` on Linux, and nothing asserts a
single exit code.** macOS has `macOS/Tests/SyxxKitTests/CLIExitCodeProcessTests.swift`, which
spawns the real binary and asserts 0/1/2/3 across sixteen `--help` paths (`:44-60`) and eight misuse
paths (`:30-39`). And critically: the binary CI publishes at `:126-131` is not the binary the .deb
ships. This step closes both gaps at once by exercising the *installed* package.

GitHub-hosted `ubuntu-latest` and `ubuntu-24.04-arm` runners have passwordless sudo, so this is
**verifiable-in-CI** — no real box needed.

```yaml
      - name: Install the .deb and exercise the installed CLI
        run: |
          sudo apt-get update
          sudo apt-get install -y "./$DEB"        # apt (not dpkg -i) so Depends are resolved
          command -v syxx || { echo "::error title=C8::syxx not on PATH after install"; exit 1; }

          # Contract (Program.cs:75-101 dispatch; 0=clean 1=threat 2=inconclusive 3=error):
          run() { set +e; syxx "$@" >/dev/null 2>&1; echo $?; set -e; }

          fail=0
          # 0 — help/version are not measurements. Mirrors CLIExitCodeProcessTests.swift:44-60.
          for v in --help --version; do
            [ "$(run $v)" = "0" ] || { echo "::error title=C8::syxx $v != 0"; fail=1; }
          done
          for v in nslook mirror leak witness key route lan forensics sentinel cage warp speed unbound filter; do
            [ "$(run $v --help)" = "0" ] || { echo "::error title=C8::syxx $v --help != 0"; fail=1; }
          done
          # 3 — misuse is an operational failure, never a threat.
          # Mirrors CLIExitCodeProcessTests.swift:30-39.
          for v in nslook mirror warp forensics witness definitely-not-a-subcommand; do
            c=$(run $v)
            [ "$c" = "3" ] || { echo "::error title=C8::syxx $v exited $c, expected 3"; fail=1; }
            [ "$c" != "1" ] || { echo "::error title=C8::misuse reported as THREAT"; fail=1; }
          done
          # 1 — a tampered witness snapshot must be a threat, not a pass.
          # Fixture reused from Syxx.Tests (see C12).
          c=$(run witness verify Windows/dotNET/Syxx.Tests/fixtures/witness-tampered.json)
          [ "$c" = "1" ] || { echo "::error title=C8::tampered witness exited $c, expected 1"; fail=1; }
          # 3 — missing evidence file. Mirrors CLIExitCodeProcessTests.swift:78-83.
          c=$(run witness report /nonexistent-$RANDOM.json)
          [ "$c" = "3" ] || { echo "::error title=C8::missing report exited $c, expected 3"; fail=1; }

          # GUI binary must exist and be a distinct file from the CLI.
          [ -x /opt/syxx/gui/Syxx ] || { echo "::error title=C8::GUI binary missing after install"; fail=1; }
          [ "$(readlink -f /opt/syxx/gui/Syxx)" != "$(readlink -f /opt/syxx/cli/Syxx)" ] \
            || { echo "::error title=C8::GUI and CLI resolve to the same file"; fail=1; }
          exit $fail
```

**What makes it FAIL.** Any exit-code regression on any of ~22 assertions. Revert the .deb to
GUI-only → `syxx --help` opens no window on a headless runner but exits non-zero (Avalonia has no
display) → red. Force a misuse path back to `exit 0` → red. **Anti-vacuity:** `command -v syxx`
gates everything; if the install did not produce a CLI on PATH the job fails before any assertion
can be skipped. Do **not** add `|| true` anywhere, and do **not** add `continue-on-error` to this
job.

**Note on the GUI.** Do not attempt to *launch* the Avalonia GUI here — headless runners have no
display and the honest answer is inconclusive. Presence + distinctness only. Actually launching it
is C21 item 2 (**needs-real-Ubuntu-box**).

---

#### C9 — Minimal-container dependency gate
**Effort: S · verifiable-in-CI · depends on C4, C7**

**What changes.** One more step in `package-linux` (amd64 leg only is sufficient; arm64 optional).

**Why.** C4's `Depends:` list is unfalsifiable on a GitHub runner — `ubuntu-latest` already has
`libstdc++6`, `libssl3`, `libx11-6`, and a GL stack installed, so a wrong dependency list still
passes. The only way to make C4 a real gate is to install into a bare image.

```yaml
      - name: Depends gate — install into a bare ubuntu:24.04 container
        if: matrix.arch == 'amd64'
        run: |
          docker run --rm -v "$PWD:/w" -w /w ubuntu:24.04 bash -euo pipefail -c '
            apt-get update -qq
            apt-get install -y -qq ./'"$DEB"'
            # Loader errors surface here, not on a users desktop.
            syxx --version
            syxx nslook --help
            # The GUI binary must at least LOAD its shared libraries. ldd -r reports
            # unresolved symbols without needing a display.
            ldd /opt/syxx/gui/Syxx | grep -q "not found" && { echo "GUI has unresolved libs"; exit 1; } || true
          '
```

**What makes it FAIL.** Remove `libstdc++6` (or any real dependency) from C4's list → the container
install succeeds but `syxx --version` dies with a loader error → red. This is the step that turns
`Depends:` from documentation into a contract.

**Caveat to declare up front.** `ldd | grep "not found"` covers the *GUI's* library closure but not
the runtime X11/GL behavior, which genuinely needs a display. That remains **needs-real-Ubuntu-box**
(C21 item 2). Do not let the container step's green imply the GUI runs.

---

#### C10 — Version parity gate (kills the hardcoded snap version)
**Effort: S · verifiable-in-CI · no dependencies**

**What changes.** New `scripts/check-version-parity.py`, wired into the `parity` job
(`ci.yml:413-435`).

**Why.** Three of four version derivations already fail closed:
- `build_deb.sh:17` greps `SyxxConfig.cs`, and `:19-22` exits 1 if the grep misses.
- `build-installer.ps1:41-46` greps the same, and `:42-46` refuses to stamp a placeholder — the
  comment at `:34-39` records that this exact bug once shipped `Syxx-0.0.0-*.zip`, "a fabricated
  version presented as the measured one, on the file users would download."
- `macOS/scripts/build_dmg.sh:13` greps the canonical version, fail-closed.

**`snapcraft.yaml:3` hardcodes `version: '2.0.0'`**, held to `SyxxConfig.cs:13` by nothing but the
comment at `:2` (*"Keep in step with SyxxConfig.Version"*). Bump the canonical version and the snap
silently ships the old number.

**Behavior.** Read the canonical from `Windows/dotNET/Syxx.Kit/SyxxConfig.cs:13`; assert equality
against `Linux/snap/snapcraft.yaml:3`, the Swift `SyxxConfig.version`, and any `Info.plist`
`CFBundleShortVersionString` produced by `bundle-app.sh`. Exit 1 on any mismatch; exit 1 (not 0) if
the canonical cannot be read or fewer than N sites were compared.

**Better long-term shape (note, do not block on it).** Have `snapcraft.yaml` use
`adopt-info:`/`craftctl set version` from a `parts:` override that greps `SyxxConfig.cs`, matching
the other three scripts' derive-don't-declare pattern. C10's gate should stay either way — a derived
version can still drift if the derivation breaks.

**CI shape.**
```yaml
      - name: Version parity gate (canonical vs snap vs Swift vs Info.plist)
        run: python3 scripts/check-version-parity.py --github
```

**What makes it FAIL.** Change `SyxxConfig.cs:13` to `2.1.0` without touching `snapcraft.yaml:3` →
red. **Anti-vacuity:** if the script compares fewer than the expected number of declaration sites,
it exits 1 with "gate compared N sites, expected M" rather than passing.

**Test-side twin.** `macOS/Tests/SyxxKitTests/VersionConsistencyTests.swift` has no C# equivalent —
that is C16.

---

#### C11 — Wire `check-eula-parity.py` into CI
**Effort: S · verifiable-in-CI · no dependencies**

**What changes.** One step in the `parity` job (`ci.yml:413-435`).

**Why.** `scripts/check-eula-parity.py` exists, is complete, fails closed
(`read_bytes` raises `ParityError` on a missing file; `main()` returns 1 on any `ParityError`), and
is **invoked from nowhere** — grep across `.github/workflows/` returns zero hits. It already covers
the Linux copy (`:16-20`: `Linux/Ubuntu/SyxxUI.Avalonia/Assets/Legal/Syxx-EULA.txt`), so wiring it
in is pure gain with no code change to the script.

```yaml
      - name: EULA parity gate (canonical + 3 platform copies)
        run: python3 scripts/check-eula-parity.py
```

Note the script takes `--root` (default = parent of `scripts/`), not `--github`, so the invocation
above is correct as written. Its output on success is
`EULA parity PASS: sha256=…; 3 platform copies`.

**What makes it FAIL.** Edit any one of the four EULA files → byte mismatch → exit 1. Delete the
Linux copy → `missing` → exit 1. Add a BOM or CRLF to the canonical → exit 1 (`validate_canonical`).
Add a fourth platform without updating `manifest.json` → exit 1.

**Placement rationale.** The `parity` job takes no `needs:` on purpose (`ci.yml:405-408`): it must
report even when a build leg is red. Legal-text divergence is exactly that class of question.

---

### Tier 3 — the untested Linux code paths

All of Tier 3 is **verifiable-in-CI**: `ci.yml:120-122` already runs
`dotnet test Windows/dotNET/Syxx.Tests/Syxx.Tests.csproj` gated only on `matrix.kind == 'dotnet'`,
so it executes on `ubuntu-x64` *and* `ubuntu-arm64`. That is the strongest existing piece of Linux
CI and every test below rides it for free. No new job is needed for C12–C18.

---

#### C12 — C# CLI exit-code process test (the macOS twin)
**Effort: M · verifiable-in-CI · depends on C1 (tidiness only)**

**What changes.** New `Windows/dotNET/Syxx.Tests/CLIExitCodeProcessTests.cs`.

**The seam already exists — this is cheaper than it looks.**
`Windows/dotNET/Syxx.Tests/WitnessVerifyExitCodeTests.cs:214-250` already contains a complete
process harness: a `RunSyxx(params string[])` that spawns `dotnet <CliAssemblyPath()>`, captures
stdout+stderr, and returns `p.ExitCode`; and a `CliAssemblyPath()` that locates
`Syxx/bin/<Config>/<Tfm>/Syxx.dll` and — critically — **throws rather than skipping** when it is
missing, with the comment "a silently-skipped exit-code test is indistinguishable from a passing
one, which is how B253 survived." `Syxx.Tests.csproj:17-21` already carries the
`ProjectReference … ReferenceOutputAssembly="false"` that guarantees the CLI is built first.

So the work is: extract that harness to a shared `CliProcessHarness` helper and write the full verb
table against it.

**What to assert**, mirroring `CLIExitCodeProcessTests.swift`:
- **0** for `--help`, `--version`, and `<verb> --help` across all 15 user verbs from
  `Program.cs:75-89` (`nslook mirror leak witness key route lan forensics sentinel cage warp speed
  unbound filter firstrun`). The Swift twin covers 16 such paths at `:44-60`; today
  `WitnessVerifyExitCodeTests.cs:158-160` covers only three (`witness --help`, `--help`, `--version`).
- **3** for bare-verb misuse. Swift `:30-39` covers `nslook`, `mirror`, `warp`, `forensics`,
  `route --not-a-host`, an unknown subcommand, `unbound not-a-real-action`,
  `firstrun --local 1.1.1.1`. C# today covers three (`:168-170`).
- **`AssertNotEqual(1, …)`** on every misuse path. Swift `:38` states the rule: "Threat code is
  reserved for findings, never misuse." That single assertion is what caught the original defect.
- **Timeout discipline.** The Swift harness bounds every run at 20s and *throws on hang*
  (`:105-116`) rather than letting the test wedge. The existing C# harness at `:230-233` calls
  `WaitForExit()` with **no timeout** — port the bound over. This matters more on Linux: several
  paths route through `NetworkKeyState` process invocation, and the Swift test's own header
  documents a hang there.

**Offline-safety.** Restrict to the same offline-safe set the Swift twin restricts to
(`CLIExitCodeProcessTests.swift:17-21`): help/usage/misuse plus file-backed witness paths. Do not
add a test that needs DNS — CI runners' network egress is not a contract.

**What makes it FAIL.** Force any misuse path back to `exit 0` → red. Have a subcommand return a
threat code for a usage error → red. Break the CLI build → `CliAssemblyPath()` throws → red (never
skipped).

---

#### C13 — `LinuxArpParser` tests
**Effort: S · verifiable-in-CI · no dependencies**

**The cheapest high-value gap in the repo.** `Windows/dotNET/Syxx.Kit/LAN/LanCartography.cs:243`
declares `public static class LinuxArpParser` with `public static List<ARPEntry> Parse(string)` at
`:245` and `ParseWithDiagnostics` at `:250` — pure, public, static, `string → List<ARPEntry>`, no
process invocation, no seam work needed. **Zero tests.** Meanwhile `WindowsArpParser` has 10+
assertions in `Windows/dotNET/Syxx.Tests/LANAndTrustTests.cs`. The Linux LAN map is the untested
half of a feature whose Windows half is well covered.

**What to test**, in a new `LinuxArpParserTests.cs` (or appended to `LANAndTrustTests.cs`):
- `TryParseIpNeigh` (`:278`) against the documented format at `:277`:
  `192.168.1.1 dev eth0 lladdr aa:bb:cc:dd:ee:ff REACHABLE`. Assert ip, mac, iface, state.
- Incomplete neighbours: `192.168.1.9 dev eth0  FAILED` / `INCOMPLETE` (no `lladdr`) — assert these
  are *rejected*, not emitted as an entry with a null MAC. A phantom host in the LAN map is a
  fabricated observation.
- `TryParseLinuxArp` (`:333`) against both documented forms at `:331-332`:
  `? (192.168.1.1) at aa:bb:cc:dd:ee:ff [ether] on eth0` and the named-host variant. Assert the `?`
  becomes a null host, not the literal string `"?"`.
- IPv6 neighbour lines — assert they are rejected by the dotted-quad check
  (`WindowsArpParser.IsDottedQuadInternal` at `:284`) rather than silently mangled.
- `ParseWithDiagnostics` accounting: `candidateCount` / `rejectedCandidateCount` (`:274`) must add
  up. A parser that rejects everything and reports zero candidates is indistinguishable from an
  empty ARP table — the diagnostics exist precisely to tell those apart, so pin them.
- Empty input, whitespace-only input, and the `ip neigh` header-less-but-garbage case → empty list,
  no throw.

**Reference mechanism.** `LANAndTrustTests.cs` `WindowsArpParser` cases — same shape, same
assertions, different fixture strings. Copy the structure verbatim so the two parsers' test suites
stay comparable at a glance.

**Effort: S.** No `internal`, no `InternalsVisibleTo`, no seam. Table-driven fixture strings only.
Do this first in Tier 3.

---

#### C14 — `TracerouteRunner` Linux argument tests
**Effort: S · verifiable-in-CI · no dependencies**

**What changes.** `Windows/dotNET/Syxx.Kit/TracerouteRunner.cs:114` —
`private static (string fileName, string args) GetTracerouteCommand(string, int, int)` must become
`internal static`, plus `[assembly: InternalsVisibleTo("Syxx.Tests")]` on `Syxx.Kit`
(`SyxxUI.Avalonia.csproj:36-38` already uses `<InternalsVisibleTo>` for exactly this reason, so the
pattern is established in-repo). Then a new test file.

**Why this one specifically.** The method's own doc comment at `:107-111` describes the defect it
guards: *"`tracert -w` takes MILLISECONDS, `traceroute -w` takes SECONDS. Passing the raw
millisecond value to traceroute would set a per-probe wait of 1000 SECONDS and hang the trace far
past the caller's deadline."* The conversion is at `:125` (`Math.Max(1, waitMs / 1000)`), and the
comment at `:104-106` states the method exists as a method *rather than an inline ternary* so it can
be tested. **It has zero tests.** A doc comment describing a 1000-second hang, with no test, is a
comment — not a guard.

**What to assert:**
- Windows branch (`:120-123`): `("tracert", "-d -h 30 -w 2000 example.com")` — `-w` in ms, unconverted.
- Non-Windows branch (`:124-126`): `("traceroute", "-n -m 30 -w 2 example.com")` — `-w` in seconds.
- **The floor:** `waitMs = 500` → `-w 1`, never `-w 0` (`:125`'s `Math.Max(1, …)`; traceroute
  rejects 0).
- **The regression itself:** `waitMs = 2000` must produce `-w 2`, and the test must assert the
  string does **not** contain `-w 2000`. That single assertion is the whole point of the task.
- `-n` present on the Linux branch (the `-d` equivalent, per `:124`).

Gate the Windows-branch assertion on `OperatingSystem.IsWindows()` or refactor to take the platform
as a parameter — otherwise the test asserts different things on different matrix legs, which is its
own honesty problem. Prefer parameterising.

---

#### C15 — `UpdateService` Linux platform/extension tests
**Effort: S · verifiable-in-CI (pure logic) / needs-real-Ubuntu-box (install path)**

**What changes.** New tests in `Syxx.Tests` against `Windows/dotNET/Syxx.Kit/UpdateService.cs`.

**Verifiable-in-CI (pure logic):**
- `:58-59` — `_platform` is `"deb"` on Linux, `"win"` otherwise. On the Linux CI legs this asserts
  `"deb"` directly; on Windows it asserts `"win"`. Needs a constructor seam or an internal accessor.
- `:243-248` — `DownloadWithProgressAsync` derives the temp-file extension from the *authenticated*
  package type and validates it against `AllowedPackageTypes`. The doc comment at `:240-242` records
  the defect: *"Forcing every package into a .zip path made Linux .deb installs impossible."* Assert
  `.deb` in → `.deb` out; `.zip` in → `.zip` out; an unknown/attacker-supplied type → rejected, not
  coerced. That last case is a security assertion, not a convenience one.
- `:141` — the extension is passed through from `package.Type`, not hardcoded.

**needs-real-Ubuntu-box:** `InstallDebAsync` at `:397-402` shells out to `dpkg -i` and requires
root. Do not fake it. C21 item 4.

**Note the interaction with §0.** `:58-59` maps every Linux host to `"deb"` unconditionally. If the
snap ships (C20), a snap-installed user is on a Linux host and will be offered a `.deb`. Add a
regression test now that pins the current behavior, and file the snap-detection question
(`SNAP` env var present → refuse to self-update, defer to `snapd`) as a follow-up for whichever
lane owns `UpdateService`. **This lane does not own that file's logic**; C15 only adds tests.

---

#### C16 — C# `VersionConsistencyTests` (the macOS twin)
**Effort: S · verifiable-in-CI · pairs with C10**

**What changes.** New `Windows/dotNET/Syxx.Tests/VersionConsistencyTests.cs`, porting
`macOS/Tests/SyxxKitTests/VersionConsistencyTests.swift`.

**Why.** The Swift file's header records the defect: `printVersion()` hardcoded `"Syxx 1.0.0"` while
`SyxxConfig.version` was `"2.0.0"`, so `syxx --version` and `syxx nslook --version` printed
different versions *from the same executable*. It notes this was "Caught by a real run, not by the
suite." The C# CLI has the identical two-path structure (`Program.cs:94-96` handles `--version`/
`-v`/`-V` at top level, with per-subcommand global-flag handling below), and has **no equivalent
test**.

**Three tests, ported directly:**
1. `SyxxConfig.Version` is well-formed MAJOR.MINOR.PATCH, all-numeric, not `0.x`
   (Swift `:17-25`).
2. The global-flag handler recognises `--version` / `-V` and does **not** swallow an ordinary
   argument (Swift `:31-37`). `Program.cs:94-96` is the C# site; note E088's comment at `:96` about
   accepting both `-v` and `-V` — pin that too.
3. **The regression itself:** walk `Windows/dotNET/**/*.cs`, regex `Syxx (\d+\.\d+\.\d+)`, and fail
   on any literal that contradicts `SyxxConfig.Version` (Swift `:43-60`). Exclude `obj/`, `bin/`,
   and `.worktrees/`.

**Relationship to C10.** C10 gates version parity *across packaging manifests* (snap, deb, plist).
C16 gates it *within the C# source tree*. Neither subsumes the other; land both.

---

#### C17 — `EncryptedDNSConfigurator` and `DimensionService` C# tests
**Effort: M · verifiable-in-CI (pure branches) / needs-real-Ubuntu-box (systemd-resolved)**

**`EncryptedDNSConfigurator`** — `Windows/dotNET/Syxx.Kit/EncryptedDNSConfigurator.cs:25` is the
Linux systemd-resolved DoT configurator, with **zero tests in either project**.
Verifiable-in-CI: the config-text generation, the resolver-address validation, and the
platform-dispatch branch (the wrong platform must refuse, not no-op silently). needs-real-Ubuntu-box:
actually writing `/etc/systemd/resolved.conf.d/`, restarting `systemd-resolved`, and confirming DoT
is live — C21 item 5. Note the *read* half of this story is already covered:
`ParseResolvectlStatus` has tests at `ConfigAndPolicyTests.cs:356,374`. The write half is the gap.

**`DimensionService`** — source-linked into the Avalonia client at
`SyxxUI.Avalonia.csproj:66-67` (`<Compile Include="..\..\..\Windows\dotNET\SyxxUI\Services\DimensionService.cs">`),
with **zero C# tests**, while macOS has `DimensionClientTests.swift`. Because it is source-linked
rather than project-referenced, it compiles into *both* GUIs from one file — a change breaks Windows
and Ubuntu simultaneously with no test on either. Add tests to `SyxxUI.Avalonia.Tests` (which
`ci.yml:94-96` and `:335-336` already run) rather than `Syxx.Tests`, since that is the project the
file is linked into on Linux. Note `ErrorPresenter.cs` is source-linked the same way at
`SyxxUI.Avalonia.csproj:74` — same exposure, worth a glance while in there.

---

#### C18 — `MachineFingerprint.LinuxHelperCaptured` tests
**Effort: S · verifiable-in-CI · pairs with C5**

**What changes.** New cases in `Windows/dotNET/Syxx.Tests/MachineFingerprintTests.cs`.

`MachineFingerprint.cs:131` — `public static MachineFingerprint LinuxHelperCaptured(string path = "/var/lib/syxx/hardware-id")`
**takes the path as a parameter with a default**, so it is fully testable today with a temp file.
No seam work.

**What to assert:**
- Missing file → `MachineFingerprint.Unavailable` (`:134`).
- 64-char lowercase hex → `FingerprintQuality.Hardware`, source `"helper:hardware-id"` (`:145`).
- Wrong length, uppercase hex, or non-hex → `Unavailable` (`:140`). The comment at `:138-139` states
  the rule: "Anything else means a corrupt or hand-edited file: treat it as absent rather than
  trusting a value we cannot vouch for." Pin all three rejection cases.
- Leading/trailing whitespace is trimmed (`:135`).
- **Do not re-hash.** `:143-144`: "Already salted+hashed by the helper — do NOT hash again, or the
  value would differ from a root-run direct DMI read of the same machine." Assert the returned
  identifier equals the file contents byte-for-byte. That is the assertion that would catch a
  well-meaning "add a hash for consistency" edit.
- Unreadable file (permissions) → `Unavailable` via the `catch` at `:146-149`, no throw.

**The other half of this task is C5.** These tests will pass today and still describe a code path
that can never fire on a real install, because no `postinst` writes `/var/lib/syxx/hardware-id` and
no systemd unit exists. `scripts/syxx-capture-hardware-id.sh` — the script that writes it — is
present, complete, and invoked by nothing. **State that in the test file's header comment** so the
next reader does not mistake green tests for a working feature.

---

### Tier 4 — the honesty gate

---

#### C19 — Extend `honesty-coverage-gate.py` beyond Python
**Effort: L · verifiable-in-CI · depends on C13, C14, C15, C18 landing first**

**What changes.** `scripts/honesty-coverage-gate.py`, plus a new CI step.

**Current state.** The gate's `GUARDS` registry (`:44-80`) holds 7 entries, every one of them a
Python module under `syxx_server/`: `leak.py` ×2, `licensing.py` ×2, `passivedns.py`,
`dns_authoritative.py` ×2. It runs the pytest suite under coverage and asserts each anchor line was
executed. It is invoked at `ci.yml:380-382` — **inside the `server` job**, so it is structurally
Python-only. There is no C# or Swift equivalent anywhere. The reasoning core that ships to three
platforms has no orphaned-guard gate at all.

**Design.** Add two parallel registries with the same `(module, delta-id, anchor, rationale)` shape:

- `CSHARP_GUARDS` — driven by `dotnet test Windows/dotNET/Syxx.Tests/Syxx.Tests.csproj
  --collect:"XPlat Code Coverage"` (coverlet, cobertura XML). Map each anchor to a source line via
  the same `resolve_anchor` logic (`:88+`) and check the line's `hits` attribute.
- `SWIFT_GUARDS` — driven by `swift test --package-path macOS --enable-code-coverage` plus
  `llvm-cov export`.

Preserve the two properties that make the existing gate worth having:
1. **Anchor rot is a failure.** If an anchor no longer matches exactly one line, exit 1 — do not
   silently drop the guard. This is what catches deletion.
2. **The two-element anchor form** (`:56-63`) for cases where the outer `if` takes both arcs but the
   fail-closed `return` does not. That comment records a real false PASS. The C# port needs the same
   escape hatch (e.g. a guard whose `if` is reached but whose `throw` is not).

**Linux guards worth registering first** (all become reachable once Tier 3 lands):
- `MachineFingerprint.cs:140` — the `hash.Length != 64 || !IsLowerHex(hash)` rejection. Deleting it
  makes Syxx trust a hand-edited hardware id.
- `UpdateService.cs:247` — the `AllowedPackageTypes.Contains(pkgType)` check. Deleting it lets an
  attacker-chosen package type steer the download path.
- `TracerouteRunner.cs:125` — the `Math.Max(1, waitMs / 1000)` conversion. Deleting it produces a
  1000-second hang the caller reads as a completed trace.
- `LanCartography.cs:284` — the `IsDottedQuadInternal` rejection in `TryParseIpNeigh`. Deleting it
  admits malformed lines into the LAN map as real hosts.
- `Program.cs` — the misuse→exit-3 mapping (pairs with C12).

**CI shape.** Do **not** bolt this onto the `server` job — that job is Python-scoped by design and
its `if:`-free steps assume a venv. Add a step to `build-test` gated on
`matrix.kind == 'dotnet' && runner.os == 'Linux' && matrix.rid == 'linux-x64'` (one leg only; the
gate is source-level, not RID-level), plus one on the swift leg:

```yaml
      - name: Honesty-coverage gate (C#)
        if: matrix.kind == 'dotnet' && runner.os == 'Linux' && matrix.rid == 'linux-x64'
        run: python3 scripts/honesty-coverage-gate.py --lang csharp

      - name: Honesty-coverage gate (Swift)
        if: matrix.kind == 'swift'
        run: python3 scripts/honesty-coverage-gate.py --lang swift
```

**What makes it FAIL.** Delete any registered guard line → anchor rot → exit 1. Keep the guard but
delete the only test that reaches it → hits = 0 → exit 1. **Anti-vacuity:** if the coverage report
is empty, unparseable, or contains zero of the registered modules, exit 1 — never 0. Note
`MEMORY.md:565` records that this gate "is RED today" for the Python registry; confirm the Python
side is green before extending, or the new legs will inherit an ambiguous signal.

**Effort is L** because it means three coverage formats and a real refactor of a script that
currently hardcodes `pytest` + `coverage.json`. Sequence it last in the test tier.

---

### Tier 5 — snap

---

#### C20 — Snap: ship GUI+CLI under one identity, or delete the file
**Effort: M · needs-real-Ubuntu-box · depends on C2**

**Decision gate first.** See §0. If no one will run `snapcraft` on a real Ubuntu box this cycle,
the correct action is `git rm Linux/snap/snapcraft.yaml` and this task is done. What follows assumes
the owner commits to the verification run.

**What changes in `Linux/snap/snapcraft.yaml`:**

1. **`:47-58`** — one part, `source-subdir: Windows/dotNET/Syxx` (CLI only). `SyxxUI.Avalonia`
   appears **zero times** in the file. Add a second part building
   `Linux/Ubuntu/SyxxUI.Avalonia`, with organize rules keeping the two `Syxx` assemblies in
   separate stage paths — the same `AssemblyName` collision as C2 (`SyxxUI.Avalonia.csproj:20` vs
   `Syxx.csproj:8`) applies here, and snapcraft will report a stage conflict rather than silently
   overwriting, which is at least honest.

2. **`:14-25`** — one app entry, `command: Syxx`. Becomes two: `syxx` (the CLI, so
   `snap run syxx …` matches `/usr/bin/syxx` from the deb) and `syxx.gui` (the GUI). Same invariant
   as §2: the bare name is the CLI.

3. **`:26-38`** — no `desktop:` key and no desktop-related plugs. A snap GUI needs at minimum
   `desktop`, `desktop-legacy`, `wayland`, `x11`, `opengl`, and a `desktop:` entry pointing at a
   staged `.desktop` file. Without these the GUI app entry installs and cannot draw.

4. **`:3`** — hardcoded `version: '2.0.0'`. Replace with `adopt-info:` + a `craftctl set version`
   override that greps `SyxxConfig.cs`, matching `build_deb.sh:17`, `build-installer.ps1:41`, and
   `build_dmg.sh:13`. C10's gate stays regardless.

5. **`:57-58`** — `stage-packages: [libicu74]`. This is **the exact unconditional pin that
   `build_deb.sh:95-96` documents as having produced uninstallable packages.** Snap staging is less
   fragile than deb `Depends` (the library is bundled, not resolved), but the pin still breaks the
   moment the `core24` base moves. At minimum add the C4 library set; better, let the dotnet plugin
   resolve it.

6. **`:35-38`** — the `CAP_NET_RAW` admission. **Keep it verbatim.** It is the most honest sentence
   in the Linux packaging tree and it is the model for C5's deb-side admission. Add the corresponding
   user-facing note: under the snap, `syxx cage` reports **untested**; for real capture, use the deb.

**Reference mechanism.** `package-macos` (`ci.yml:242-309`) asserts a bundle it also *builds*.
C20 has no CI equivalent — snapcraft cannot run on a GitHub-hosted runner without LXD/multipass
setup that is out of scope for this lane. **This task is therefore `needs-real-Ubuntu-box` and its
result must be recorded as measured, inconclusive, or failed** — not assumed. `snapcraft.yaml:20-24`
already sets that precedent by writing "UNVERIFIED ON THIS HOST" into the file. Preserve that
discipline: if C20's run does not happen, the comment stays and the file does not ship.

---

### Tier 6 — owner-run

---

#### C21 — Real-Ubuntu-box runbook
**Effort: M (owner time) · needs-real-Ubuntu-box · depends on C7/C8 producing an artifact**

Each item below declares pass / fail / **inconclusive** *before* the run. Record all three
possibilities; an item that has no inconclusive branch is a badly-specified test.

---

**Item 1 — the .deb installs and `syxx` is the CLI**

```bash
# Download the package-linux artifact for your arch first.
sudo apt-get update
sudo apt-get install -y ./syxx_*_$(dpkg --print-architecture).deb
which syxx && syxx --version; echo "exit=$?"
syxx nslook example.com; echo "exit=$?"
```
- **PASS** — `which syxx` → `/usr/bin/syxx`; `--version` prints the `SyxxConfig.cs:13` version and
  exits 0; `nslook` exits 0, 1, or 2 with a printed result.
- **FAIL** — a GUI window opens (the C2 regression), or `--version` exits non-zero, or `nslook`
  exits 3.
- **INCONCLUSIVE** — `nslook` exits 2 *and* prints an inconclusive-reason (no network, blocked
  resolver). That is the tool working correctly on a bad network; re-run on a known-good link
  before recording anything.

---

**Item 2 — the GUI actually launches (the thing CI structurally cannot answer)**

```bash
syxx-gui &
sleep 10
xdotool search --name Syxx || wmctrl -l | grep -i syxx
echo "gui-exit=$?"
```
- **PASS** — a Syxx window appears and the process stays alive past 10s.
- **FAIL** — immediate exit, or a loader error naming a missing library (that means C4's `Depends`
  is still short — go add the named library).
- **INCONCLUSIVE** — headless session, no `$DISPLAY`/`$WAYLAND_DISPLAY`, or no `xdotool`/`wmctrl`
  installed. Record inconclusive; do **not** record pass because "no error appeared."

Run this on **both** GNOME and one of MATE/Cinnamon. `SyxxUI.Avalonia.csproj:3-6` claims Avalonia
"renders identically on GNOME / MATE / Cinnamon rather than adopting one DE's styling" — that claim
is currently untested by anything.

---

**Item 3 — Cage capability (the C5 payoff)**

```bash
getcap /opt/syxx/cli/Syxx           # expect: cap_net_raw=ep
syxx cage --duration 10 --json | tee /tmp/cage.json
jq -r '.status // .result // .conclusion' /tmp/cage.json
```
- **PASS** — `getcap` reports `cap_net_raw=ep` AND the capture reports a real result (clean or
  threat) with a non-zero packet/observation count.
- **FAIL** — `getcap` reports the capability, the capture runs, and it reports **clean on zero
  observations**. That is the single worst outcome in this product's failure taxonomy and it is
  what `snapcraft.yaml:36-37` exists to prevent.
- **INCONCLUSIVE** — `getcap` is empty (setcap did not apply; filesystem may lack xattrs) and Cage
  reports **untested**. This is the *correct* degraded behavior. Record inconclusive and note
  whether `postinst` logged the setcap failure.

Repeat under the snap if C20 ships: the expected snap result is **always inconclusive/untested**,
per `snapcraft.yaml:35-38`. A snap Cage that reports *clean* is a FAIL.

---

**Item 4 — the self-update path (`UpdateService`)**

```bash
sudo /opt/syxx/cli/Syxx --check-update   # or whichever verb drives UpdateService
# Observe: platform string, downloaded file extension, dpkg invocation.
```
- **PASS** — platform resolves to `"deb"` (`UpdateService.cs:58-59`), the downloaded temp file keeps
  its `.deb` extension (`:243-248`), and `dpkg -i` (`:399-402`) upgrades in place; `syxx --version`
  afterwards reports the new version.
- **FAIL** — the download lands as `.zip` (the defect `:240-242` documents), or `dpkg` errors, or
  the version does not change after a successful install.
- **INCONCLUSIVE** — no newer package is published upstream, so nothing to download. Record
  inconclusive; do not treat "no update available" as a verified update path.

**Also check:** does `/var/lib/syxx/hardware-id` survive the upgrade? If it changes,
`MachineFingerprint.LinuxHelperCaptured` (`:131`) returns a new identity and the user burns an
activation on every update. That is C5's `prerm`/`postrm` split; verify it here.

---

**Item 5 — systemd-resolved DoT (`EncryptedDNSConfigurator`)**

```bash
resolvectl status | head -30                      # capture BEFORE state
sudo syxx unbound enable-dot                      # or the equivalent verb
resolvectl status | grep -iE 'DNS over TLS|DNSOverTLS'
sudo cp -a /etc/systemd/resolved.conf.d /tmp/syxx-resolved-backup   # keep a rollback
```
- **PASS** — `resolvectl status` reports DoT enabled with the intended resolver, and a subsequent
  `syxx nslook` still resolves.
- **FAIL** — DNS resolution breaks after the change, or the config is written but `resolvectl`
  shows no change (a write with no effect is worse than a refusal).
- **INCONCLUSIVE** — the host does not use `systemd-resolved` (check `resolvectl status` exits
  non-zero, or `/etc/resolv.conf` is not the stub). Record inconclusive; the configurator
  `EncryptedDNSConfigurator.cs:25` targets systemd-resolved specifically.

**Back up `/etc/systemd/resolved.conf.d/` before running.** This is the one item in the runbook that
can leave the box without working DNS.

---

**Item 6 — secret store on a real desktop session**

```bash
# From a GRAPHICAL session with an unlocked keyring — not SSH.
syxx key generate && syxx key list
secret-tool search --all service syxx 2>&1 | head
```
- **PASS** — the key round-trips via libsecret (`LinuxSecretStore.cs:45-56`), and `secret-tool`
  shows it.
- **FAIL** — the key is silently written to the file fallback while a working keyring is present
  (that is a security downgrade presented as success), or the round-trip loses the key.
- **INCONCLUSIVE** — no D-Bus session or locked keyring → the file fallback engages. That path is
  **already covered** by `LinuxSecretStoreTests.cs`; this run exists to exercise the P/Invoke
  backend, which CI cannot reach. Record inconclusive if the keyring never unlocked.

---

**Item 7 — snapcraft (only if C20 proceeds)**

```bash
cd Linux/snap && snapcraft --verbose 2>&1 | tee /tmp/snapcraft.log
sudo snap install --dangerous ./syxx_*.snap
snap run syxx --version; echo "exit=$?"
snap run syxx.gui &
snap connect syxx:network-observe
```
- **PASS** — the build succeeds, `snap run syxx --version` exits 0 and prints the canonical version,
  and the GUI draws.
- **FAIL** — the build fails, or `snap run syxx` cannot find the command (the `:20-24` UNVERIFIED
  stage-path risk materialising), or the version printed differs from `SyxxConfig.cs:13`.
- **INCONCLUSIVE** — the `dotnet-sdk` snap or the `core24` base is unavailable in the environment.
  Record inconclusive and **do not remove the `:20-24` UNVERIFIED comment** — it stays until a build
  actually succeeds.

**Whatever the outcome, replace the `:20-24` comment with the measured result and the date.** The
comment is currently correct; leaving it stale after a successful build would be its own small
dishonesty.

---

#### C22 — Document the Linux build commands (optional)
**Effort: S · verifiable-in-CI (link check only)**

Add a Linux packaging section to `docs/BUILDING.md` alongside the existing CI-path annotations
(e.g. `:329`, `:335`). Cover `build_deb.sh`, `snapcraft`, the `package-linux` job, and the two
launcher names. **Do not add targets to the root `Makefile`** — see §1.

---

## 5. Summary table

| # | Task | Effort | Where verified | Blocks |
|---|---|---|---|---|
| C1 | Delete `Syxx.Linux` decoy | S | CI (C1b) | C1b |
| C1b | Orphan-project gate | S | CI | — |
| C2 | Deb payload: GUI+CLI, dir split | M | CI (C3, C7) | C3–C9 |
| C3 | Fail-closed payload assert | S | CI | C7 |
| C4 | Expand `Depends` | M | CI (C9) | C9 |
| C5 | postinst/prerm: setcap, hw-id, desktop | M | CI (presence) / box (effect) | C8, C21#3 |
| C6 | EULA + copyright in package | S | CI | — |
| C7 | `package-linux` job | M | CI | C8, C9 |
| C8 | Install + exit-code smoke | M | CI | — |
| C9 | Minimal-container Depends gate | S | CI | — |
| C10 | Version parity gate | S | CI | — |
| C11 | Wire `check-eula-parity.py` | S | CI | — |
| C12 | C# CLI exit-code process test | M | CI | C19 |
| C13 | `LinuxArpParser` tests | S | CI | C19 |
| C14 | `TracerouteRunner` Linux args | S | CI | C19 |
| C15 | `UpdateService` Linux logic | S | CI / box (install) | C19, C21#4 |
| C16 | C# `VersionConsistencyTests` | S | CI | — |
| C17 | `EncryptedDNSConfigurator` + `DimensionService` | M | CI / box | C21#5 |
| C18 | `LinuxHelperCaptured` tests | S | CI | C19 |
| C19 | Honesty gate beyond Python | L | CI | — |
| C20 | Snap: GUI+CLI or delete | M | **box only** | — |
| C21 | Owner runbook | M | **box only** | — |
| C22 | `docs/BUILDING.md` Linux section | S | CI (links) | — |

**Minimum viable slice** if the lane must ship something small: **C1, C2, C3, C7, C8, C13.** That
removes the trap, puts the CLI in the package, makes its absence a build failure, makes CI install
the real artifact and read exit codes, and closes the cheapest test gap. Everything else is
strengthening.
