# Linux Parity Ledger

The durable artifact answering **"is Linux at parity?"** — with evidence, and with an explicit
statement of what each answer is worth.

---

## The five states, and why "fixed" is not one of them

Every row below carries exactly one of these. **"Fixed" is deliberately not available**, because it
is the word that let this codebase's packaging defects survive: `build_deb.sh` was "fixed" to ship
both binaries months before anything ever built a `.deb`, and the fix was real but nobody could
have known.

| State | Means | What it is worth |
|---|---|---|
| `open` | The defect stands. Nothing has landed. | — |
| `fixed-unverified` | A change is in the tree. **Nothing has executed it.** | A claim. It is the author's reading of what the code will do. |
| `verified-by-CI` | A CI job has run this exact assertion and gone green. | Evidence, and it is re-checked on every push. |
| `verified-by-real-run` | Executed on a machine, output inspected, by a named party. | Evidence for one machine at one moment. |
| `deliberately-out` | Decided not to do. The reason is recorded. | A decision, not a gap. |

> **★ Nothing in the Linux packaging section is `verified-by-CI` yet.** The four jobs that would
> produce that evidence — `package-deb`, `install-smoke-deb`, `package-snap`, `linux-secret-store`
> — are committed in `.github/workflows/ci.yml` and **have never executed**. They were added in the
> same wave as the fixes they check. The first CI run on this branch is what converts a large block
> of `fixed-unverified` into `verified-by-CI` in one step. Until then, read `fixed-unverified` as
> exactly what it says.

---

## §P — Packaging and delivery

### ★ Blocking: HEAD cannot build a `.deb`

| ID | Item | State | Evidence |
|---|---|---|---|
| P-00 | `Linux/Ubuntu/debian/postinst`, `debian/postrm` and `polkit/app.syxx.policy` are **untracked**, while the committed `build_deb.sh` fails closed on all three (`:209-213`, `:225-236`). A clean checkout cannot build a package at all. | **`open`** | `git ls-files Linux/Ubuntu/debian Linux/Ubuntu/polkit` → empty. `git diff HEAD -- Linux/Ubuntu/build_deb.sh` → empty, so the script *is* HEAD. |

Closing P-00 needs a `git add` of those three paths — an owner action. Until it happens,
`package-deb` fails at its first step and every row below it in this section is untestable.
The gate at `ci.yml` *"Assert every packaging input is committed, not just present locally"* exists
to make that failure legible instead of a bare `cp: No such file or directory`.

*(Note: a clean-checkout CI job also currently fails for an unrelated reason — an illegal
`var (masterKey, int actualMode)` deconstruction in `LinuxSecretStore.cs` at `413ab23`, repaired
only in the uncommitted working tree. Two independent reasons, neither caused by the other.)*

### Both packages ship the whole product

| ID | Item | State | Evidence / what closes it |
|---|---|---|---|
| P-09/P-17 | The `.deb` shipped **only the GUI**; the snap shipped **only the CLI**. Both assemblies are named `Syxx`, so neither omission was visible. | `fixed-unverified` | Both now ship both, CLI as `syxx`, GUI as `syxx-gui` (`build_deb.sh:177-191`; `snapcraft.yaml:52-99`). Closed by `package-deb` + `package-snap`. |
| P-09a | Nothing made a half-empty package fail the build. | `fixed-unverified` | `assert_shipped_binary()` (`build_deb.sh:118-149`) — existence, non-empty, ELF magic, exec bit — plus a non-identical-`Syxx.dll` check and positive/negative `Avalonia.dll` identification (`:159-172`). Twin of `macOS/bundle-app.sh:29-33`. Snap twin at `snapcraft.yaml:153-158`, `:200-204`. |
| P-09b | The archive a user receives was never inspected, only the staging tree. | `fixed-unverified` | `package-deb` asserts against `dpkg-deb -c` / `-I` output; `install-smoke-deb` asserts against the installed filesystem. |

### Privilege

| ID | Item | State | Evidence / what closes it |
|---|---|---|---|
| P-02 | `pkexec` was already load-bearing (`UpdateService.cs:409` shells `/usr/bin/pkexec`) but **no polkit action was ever shipped**, so every escalation fell back to the generic *"run `/usr/bin/syxx' as the super user"* dialog. | `fixed-unverified` | `Linux/Ubuntu/polkit/app.syxx.policy`, installed to `/usr/share/polkit-1/actions/`. `install-smoke-deb` asserts it landed, parses, annotates an executable path, is byte-identical to the reviewed source, and that all three `allow_*` arms are `auth_admin`. |
| P-02a | `setcap cap_net_raw+ep` as the alternative. | `deliberately-out` | Owner decision 2. It grants raw sockets to **every local user, permanently**, from install. For a forensics tool that is a worse trade than one prompt per capture. |
| P-02b | `auth_admin_keep` (caches ~5 min). | `deliberately-out` | The decision is a prompt **per capture**. CI asserts the negative — matched inside `<allow_*>` only, so the policy's own comment explaining the choice does not trip it. |
| P-02c | The action covers `/usr/bin/syxx`, i.e. **any** subcommand as root, not just `cage`. | `open` | Not a regression — `sudo syxx cage` already had that surface — but not least-privilege either. Narrowing needs a capture-only verb on the CLI, a C#-side change. |

### Licensing node-lock

| ID | Item | State | Evidence / what closes it |
|---|---|---|---|
| P-03 | `scripts/syxx-capture-hardware-id.sh` exists, is pinned by five tests, reasons its way to install-time capture — and was **never invoked**. Every Linux licence fell back to an InstallScoped fingerprint, so **each reinstall burned another activation**. | `fixed-unverified` | `DEBIAN/postinst` now calls it. Blocked behind P-00. |
| P-03a | The helper's own behaviour: hashes a real DMI UUID, refuses the all-zero and all-`f` placeholders, is idempotent, creates its own parent directory. | **`verified-by-real-run`** | Executed on the macOS host, 2026-08-05, seat R2-DELIVERY. Real UUID → `878da9f7…bbcde`, matches `^[0-9a-f]{64}$`; all-zero → refused, no file; all-`f` → refused; two runs byte-identical; `mkdir -p` at `:86` creates `/var/lib/syxx` (which `build_deb.sh` never ships). This is the exact assertion pair at `ci.yml:833-846`, replayed. |
| P-03b | The snap **cannot** node-lock: no `postinst`, no install-time root hook, and under strict confinement it cannot read `/sys/class/dmi/id/product_uuid` (mode 0400) at runtime either. | `deliberately-out` | Documented in a 17-line block at `snapcraft.yaml:34-49`. Every fix is worse: a setuid helper, a root daemon, or a per-launch prompt to answer "which machine is this". Snap installs must be presented honestly as install-scoped. |
| P-03c | A `postinst` failure must not fail the install. | `fixed-unverified` | `postinst` is `set -e`-safe: every optional step guarded by `command -v` or `\|\| true`, every function ends `return 0`. `install-smoke-deb` proves it end-to-end by asserting `dpkg-query -W -f='${Status}'` is exactly `install ok installed` — an exit code, not script text. |
| P-03d | `postrm purge` must remove `/var/lib/syxx/hardware-id` (dpkg does not know it exists — Policy §6.8). | `fixed-unverified` | `postrm` removes on `purge`, `rmdir`s the state dir only if empty, and deliberately **keeps** the id on plain `remove` so a remove/reinstall cycle costs no activation. |
| P-03e | No `prerm`. | `deliberately-out` | A `prerm` stops something before dpkg unlinks it: a daemon, an init script, an alternatives registration. This package has none. Its only out-of-file-list state is handled by `postrm` on purge. A no-op `prerm` is one more script whose `set -e` can wedge an uninstall. |

### Dependencies

| ID | Item | State | Evidence / what closes it |
|---|---|---|---|
| P-11 | `Depends:` omitted `libsecret-1-0` and `libsecret-tools`, which **guarantees** the degraded file-based secret path, and omitted `pkexec`. | `fixed-unverified` | `build_deb.sh:328`. `pkexec \| policykit-1` is alternated on purpose: `policykit-1` is transitional on Debian 12 / Ubuntu 24.04 and **gone in Debian 13**, while Ubuntu 22.04 has no `pkexec` package. Closed by `install-smoke-deb`, which installs into a bare `ubuntu:24.04` with `--no-install-recommends` so `Recommends` cannot rescue a missing `Depends`. |
| P-11a | `iproute2` — `LAN/LanCartography.cs:715` runs `ip neigh show`, falling back to `arp -a`; net-tools is not installed by default on modern Ubuntu, so with neither present the neighbour table is unreadable every run. | `fixed-unverified` | Added to `.deb` `Depends` and to the snap's `stage-packages`, 2026-08-05. |
| P-11b | `systemd-resolved` — `resolvectl` at `NetworkKeyState.cs:340` and `EncryptedDNSConfigurator.cs:105`. | `fixed-unverified` | Added to `Recommends`, not `Depends`: `NetworkKeyState` falls back to `/etc/resolv.conf` and `EncryptedDNSConfigurator` reports the failure rather than claiming a configuration it did not make. Honest degradation. |
| P-11c | `bind9-host` was considered and **rejected**. | `deliberately-out` | A grep for a shelled `host` matches only `IndependenceGroup: "host"` in `Synthesis/SyxxMatrixIntegration.cs:224,331` and a `<paramref name="host"/>` in `DimensionClient.cs:187`. The product does not shell `host`. Recorded so it is not re-added. |
| P-11d | `libglib2.0-0t64` / `libgobject-2.0` are dlopened (`LinuxSecretStore.cs:1162,1175`) but not declared. | `deliberately-out` | `libsecret-1-0` depends on both. A second declaration is only a name to keep in step. |
| P-11e | Every shipped native object must resolve using **only** the declared `Depends`. | `fixed-unverified` | `install-smoke-deb` runs `ldd` over both apphosts and every staged `.so`, greps `not found`, and refuses to pass vacuously (`checked -lt 3` is fatal). |

### Version derivation

| ID | Item | State | Evidence / what closes it |
|---|---|---|---|
| P-12 | The snap version was hardcoded under a comment asking a human to keep it in step with `SyxxConfig.Version`, while `build_deb.sh` greps the constant and fails closed. The two packagers disagreed about whether the product version is a fact or a note. | `fixed-unverified` | `adopt-info: syxx-cli` + an `override-pull` that greps the same constant and `exit 1`s when it cannot read it (`snapcraft.yaml:120-130`). Asserted three ways: control field, snap filename, and the **running** CLI's `--version`. |

### CI — the mechanism, not a coincidence

| ID | Item | State | Evidence / what closes it |
|---|---|---|---|
| P-18 | **No CI job had ever built either Linux package.** `grep -rE 'build_deb\|snapcraft\|dpkg' .github/` returned zero hits. That is why every defect above survived: the build was always green because nothing built. | `fixed-unverified` | `package-deb`, `install-smoke-deb`, `package-snap`, `linux-secret-store` in `ci.yml`. **Never executed.** |
| P-18a | Building an archive proves little; it can be well-formed and uninstallable. | `fixed-unverified` | `install-smoke-deb` installs into a bare `ubuntu:24.04` container — deliberately not the runner image, which has hundreds of preinstalled libraries and would mask the missing-`Depends` class entirely. Step order is load-bearing: `actions/checkout` runs **after** the dependency assertions, because it needs `git`, and apt-installing `git` first would drag in libraries that could satisfy an undeclared dependency. |
| P-18b | Nothing had ever launched the shipped GUI. | `fixed-unverified` | `install-smoke-deb` runs `/usr/bin/syxx-gui` under `xvfb-run` and requires exit code **124** (still alive after 15s). Kept fatal on purpose. A bare container is harsher than a real desktop, so a failure here is not automatically a user-facing bug — but it is always a real finding about what the package declares. |
| P-18c | The GUI leg would have died on `xauth: command not found` and reported it as *"GUI cannot start"* — `xvfb-run` pipes a magic cookie to `xauth`, `xauth` is a `Recommends` of `xvfb`, and every apt line in that job uses `--no-install-recommends`. | `fixed-unverified` | `xauth` named explicitly, 2026-08-05. |
| P-18d | `package-snap` assumed `unsquashfs` was on the runner (it arrives only transitively via snapd). | `fixed-unverified` | Guarded with `command -v unsquashfs \|\| apt-get install squashfs-tools`, 2026-08-05. |
| P-19 | Neither package was ever published as an artifact. | `fixed-unverified` | `upload-artifact` with `if-no-files-found: error` on both. |

### Secret store against a real keyring

| ID | Item | State | Evidence / what closes it |
|---|---|---|---|
| P-13 | Every `LinuxSecretStore` test runs where there is no libsecret, no `secret-tool` and no keyring, so **the only backend ever exercised is the encrypted file fallback** (`WitnessKeyStoreLoadResultTests.cs:402` says so). The keyring paths — what a real Ubuntu desktop user gets — have never been executed anywhere. | `fixed-unverified` | Job **`linux-secret-store`**: installs `libsecret-1-0 libsecret-tools gnome-keyring dbus-x11`, runs under `dbus-run-session`. **Opt in with `[Trait("Category", "SecretService")]`**; `SYXX_CI_SECRET_SERVICE=1` is exported for tests that want to assert a keyring is present rather than skip. |
| P-13a | The leg must not pass while silently measuring the file fallback. | `fixed-unverified` | Two independent anti-vacuity gates: a strict `secret-tool store` → `lookup` round-trip **before** the tests, and a `.trx` counter that fails if the `--filter` matched zero tests. |
| G-16 | `libsecret` P/Invoke passes the keyType **value** in an attribute-**name** slot, so `secret_password_store_sync` returns FALSE without even setting a GError and every write through it fails silently. | `open` | Fix coded; needs `linux-secret-store` to run. This is why `libsecret-tools` is a `Depends` and not a `Recommends`: `secret-tool` is currently the only backend that can genuinely write to the keyring. |
| G-02b | Store-before-mint residual on the libsecret P/Invoke path. | `open` | Must co-land with G-16. |

### Snap: ship, rename, or delete

| ID | Item | State | Evidence |
|---|---|---|---|
| P-20 | **`UpdateService` breaks every snap user's in-app update by construction.** | `open` — **owner decision needed** | `UpdateService.cs:59-60` maps *every* Linux host to platform `"deb"` unconditionally; `InstallDebAsync` at `:397-410` shells `/usr/bin/pkexec dpkg -i`. A snap user therefore fetches the `.deb` appcast, downloads a `.deb`, and either fails (no `pkexec` inside strict confinement) or installs a **second, parallel** copy of Syxx outside the snap. Renaming the snap does not fix this; it renames it. Line numbers corrected from the plan's `:58-59`/`:399-402`. |

**Recommendation (R2-DELIVERY, not executed — deleting is an owner-visible act):** *keep*
`Linux/snap/snapcraft.yaml` **for now, and let `package-snap` answer the question.** The lane's
fallback was framed around the snap being unverifiable this cycle; it is not — `package-snap` builds
it on a real Ubuntu runner and is blocking. What genuinely must be settled before the snap reaches
a user is P-20: either `UpdateService` learns it is running under snap (`SNAP` / `SNAP_NAME` in the
environment) and refuses to `dpkg -i`, or the snap is deleted. Shipping it as-is is the one option
that is not defensible.

### Witness path coupling — checked, not tripped

| ID | Item | State | Evidence |
|---|---|---|---|
| P-21 | Two witness identities coexist on one machine: the CLI writes a raw base64 key to `UserProfile/.syxx/witness.key`, `WitnessKeyStore` writes a reference token to `LocalApplicationData/Syxx/witness.key`. If they are ever unified the GUI goes permanently `Undetermined` with no in-app recovery. Any packaging change to `HOME` / `XDG_DATA_HOME` / `XDG_CONFIG_HOME` is a named trigger. | `fixed-unverified` (no change made; checked) | **The `.deb` cannot trip it:** both launchers are `exec /opt/syxx/{cli,gui}/Syxx "$@"` with no environment manipulation whatsoever, and the package writes only under `/opt`, `/usr` and `/var/lib`. **The snap moves both paths but does not unify them:** snapd sets `HOME=$SNAP_USER_DATA` *and* `XDG_DATA_HOME=$SNAP_USER_DATA/.local/share`, so `UserProfile/.syxx/witness.key` and `LocalApplicationData/Syxx/witness.key` remain two distinct paths under one root. **Reasoned from snapd semantics, not executed** — a real snap install is the only thing that can confirm it. |

---

## §S — Security review findings (2026-08-05, 4-seat review team)

| # | Severity | Finding | State |
|---|---|---|---|
| S-1 | Medium | `PassphraseAvailable()` TOCTOU — double `TryGetPassphrase()` call | `open` |
| S-2 | Medium | Cached derived key returned by reference across a lock boundary | `open` |
| S-3 | Medium | `SYXX_WITNESS_PASSPHRASE` readable via `/proc/<pid>/environ` | `open` (doc fix queued) |
| S-4 | Low | `StringBuilder` material never zeroed | `open` |

---

## §G — Build pass results (committed 2026-08-05, sha `39b95ad`)

| ID | Item | State |
|---|---|---|
| G-01 | `SaveKey` verify-after-store: read-back + ordinal compare + throw before token write; orphan discarded | `fixed-unverified` |
| G-02a | Positive proof of absence: `File.Exists` → guarded `FileStream` open; HasKey error-as-absence gone | `fixed-unverified` |
| G-03 | First-run manifest: `WarpManifest` folded in both the C# and Swift twins | `fixed-unverified` |
| G-04 | CI gates: `package-pin-parity.py` path fixed + self-test; sln config entries; CI DLL assertion | `fixed-unverified` |
| G-05 | Test seam: `KeyPath` + store dir injectable; 22 tests; mutation-tested (5 guards, 9 flip) | `fixed-unverified` |
| G-16b | `DNSPathConfirmation.swift` Equatable — blocks all Swift compilation | `open` |
| G-17 | macOS `hasKey` collapse | `open` (depends on G-16b) |

### Reserved — routed to other streams, not this ledger's to move

| Item | Owner |
|---|---|
| G-09 | Mirror stream |
| G-10, G-11, G-12, G-14 | 2nd Avalonia team |
| G-13 | Stage-5 wording |
| G-15 | All platforms reserved |

---

## §4.6 — Parity gate baseline

| Command | State |
|---|---|
| `python3 scripts/cross-platform-parity-gate.py` | 🔴 RED (`tracer_outcome` enum drift) |
| `python3 scripts/package-pin-parity.py` | 🟢 GREEN (after G-04) |
| `dotnet build Windows/Syxx.Linux.slnf -c Release` | ⚪ NOT RUN (not available on the macOS host) |
| `dotnet test Windows/dotNET/Syxx.Tests/Syxx.Tests.csproj -c Release` | ⚪ NOT RUN |

---

## §0 — Findings from the original plan survey

| ID | Finding | Date |
|---|---|---|
| 0.1 | `Linux/dotNET/Syxx.Linux/` was a decoy (166 lines, all stubs, never referenced) — **REMOVED**. Verified 2026-08-05: only gitignored `bin/`+`obj/` build output remains on the authoring host; `git ls-files Linux/dotNET/` lists `README.md` alone. The removal claim in `Linux/dotNET/README.md` is honest. | 2026-08-05 |
| 0.2 | The real Linux CLI is `Windows/dotNET/Syxx/Syxx.csproj` (`win-x64;linux-x64;linux-arm64`), built via `Windows/Syxx.Linux.slnf` | 2026-08-05 |
| 0.3 | `Syxx.Kit` is already substantially Linux-aware (~40 `IsLinux`/`IsMacOS` hits) | 2026-08-05 |

---

## What still needs a real Linux box, and cannot be closed by CI

CI closes far more than was assumed — everything in §P marked *closable* above runs on a GitHub
Ubuntu runner. These do not:

1. **A real desktop session.** The GUI leg proves the process survives 15s under Xvfb on a bare
   container. It does not prove theming, cursors, portal access, or that `extensions: [gnome]` is
   or is not needed for the snap (`snapcraft.yaml:93-99`).
2. **A real polkit prompt.** CI proves the action file lands, parses, annotates a real executable,
   and requires `auth_admin` on every arm. Only a human at a desktop sees the dialog text.
3. **A real DMI UUID.** Containers have none, so `install-smoke-deb` honestly asserts the helper
   *ships intact and works when given one* rather than asserting the captured file exists. Whether
   `postinst` node-locks a physical machine needs a physical machine.
4. **A snap install.** `package-snap` builds; nothing installs. Confinement behaviour, the P-21 path
   question, and P-20's update path are all real-box items.
5. **`dpkg` / `snapcraft` / `pkexec` on the authoring host.** None can run on macOS. No install on
   this branch has been verified by a human; every packaging row above is reading plus static
   validation plus, where the tool was portable, a real local execution (P-03a).

---

## §CP — Cross-platform parity (track `cross-platform-parity_20260805`, 2026-08-05)

Swift-vs-C# engine parity. Full evidence in
`conductor/tracks/cross-platform-parity_20260805/findings.md`.

| ID | State | What |
|---|---|---|
| F-01 | `verified-by-real-run` | `resolverSubstituted` (B174) and the Mirror degraded-cap existed only on Swift, so substituted evidence could read clean on Windows and Linux. Ported to C#; twin tests both platforms; C# test proven to fail with the cap neutralized |
| F-02 | `verified-by-real-run` | `transportObservation` was absent from the C# `SectionInputs`. Added for structural parity. **Neither platform reads the field**, so no conclusion depended on it — my first report of this overstated the consequence |
| F-03 | `open` | The matrix golden corpus was loaded by C# only; the Swift twin sat in `_quarantine`. Restored 2026-08-05 and it now runs — but see F-05 for what it found, and F-07 for the half still missing on C# |
| F-04 | `verified-by-real-run` | Spec serialization was nondeterministic on BOTH platforms — `Set`-derived arrays emitted in per-process hash order. Three runs, three orders. Both sides now sort; twin tests assert *sorted*, not merely stable. Recorded during the port at `MatrixDeterminativeEngine.cs:365` and deferred to a rule that never landed |
| F-05 | `open` | Neither platform matches `default-spec.frozen.json`, and they fail differently — Swift has an extra `mappableIndicators` key, C# unescapes the `/` the oracle escapes. The oracle is stale; regenerating it is an explicit spec-version bump per `MATRIX-MIGRATION-PLAN.md:358` |
| F-06 | `open` | F042 was recorded FIXED on a file that never compiled. `docs/DELTAS-fixed.md` corrected 2026-08-05 |
| F-07 | `open` | `MatrixGoldenCorpusTests.cs` documents two assertions and implements one; no vector-evaluation test on the C# side |
| F-08 | `open` | `-v` means verbose on macOS and version on Windows/Linux. B240 and E088 are both documented and contradict each other. Owner decision — it changes a shipped CLI flag |

**Not covered by this track so far:** the six matrix tests failing before this work began (four in
`MatrixDeterminativeEngineTests`, one each in `MatrixAllNegativeParityTests` and
`MatrixUntestedStateTests`); the 21 files / 351 test functions in `macOS/Tests/_quarantine/` beyond
the one restored; GUI parity, which is a separate track.

---

## Appendix — ledger entries, newest first

| Date | Entry |
|---|---|
| 2026-08-05 | R2-DELIVERY: restructured to the five-state vocabulary; added §P; recorded P-00 (packaging inputs uncommitted), P-11a/b/c/d, P-18c/d, P-20, P-21; P-03a moved to `verified-by-real-run`. |
| 2026-08-05 | Ledger created from the §7.0 build pass and the security review results. |
