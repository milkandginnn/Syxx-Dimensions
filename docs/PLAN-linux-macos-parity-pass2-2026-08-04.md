# Linux → macOS parity: second-pass survey and build plan

- **Date:** 2026-08-04
- **Stage:** planning only. **No code was changed by this pass.** Three read-only reviewer seats
  (no Write, no Edit tool) surveyed; this document is the whole output.
- **Supersedes nothing.** `docs/PLAN-linux-parity-team-2026-08-04.md` (pass 1) remains valid; its §7
  register is referenced here as `G-nn` and is not repeated.

---

## Read this first — in plain terms

**What pass 1 was:** a debug run on the in-flight Witness/Warp changes. Five hypotheses, narrow scope.
It did **not** compare the two apps. Its own status said Mirror UI parity was "not started".

**What this pass is:** the first actual feature-by-feature comparison of the Linux client against the
macOS client. It is a survey plus an implementation plan. Nothing here has been built.

**The seven things that matter most, in ordinary language:**

1. **Five buttons on the Linux main screen do nothing.** "Conduct All Scans", "Flush DNS Cache",
   "Enable Unbound DNS", "Enable Encrypted DNS", "Begin the trial" are styled rectangles with no
   command attached. Clicking them produces no action, no error, no log line.
2. **The Linux app tells every user their traffic is already encrypted, before checking.** The string
   "Disabled — traffic already encrypted (VPN)" is hardcoded and shows on first paint.
3. **Neither Linux package is the whole product.** The `.deb` installs only the GUI; the snap installs
   only the CLI. Both binaries are named `Syxx`. The GUI instructs users to run `sudo syxx cage`,
   which the `.deb` never installed.
4. **On Linux the app measures the wrong DNS server.** It cannot parse real `resolvectl` output, falls
   back to `/etc/resolv.conf`, and accepts the systemd stub `127.0.0.53` as "the system resolver". On
   Ubuntu — the declared target — that means every comparison and leak probe is aimed at a local
   placeholder rather than the real upstream. This is the product's central function.
5. **Linux gives up on slow networks before an answer can arrive.** The probe deadline is 2.5s; a DNS
   query needing one retry takes 4s. macOS allows 5s for exactly this reason. Linux records
   "inconclusive" where macOS records evidence, on precisely the degraded networks that matter.
6. **The Witness signing key is weakly protected on Linux.** No user secret in the derivation, a
   constant salt over world-readable inputs, and a window at every write where the file is readable.
   Another local user could plausibly sign testimony as you. macOS uses the login-protected Keychain.
7. **No CI job has ever built the Linux packages.** That is why 1–4 survived.

**And the direction is not one-way.** Several things are *better* on Linux and macOS should adopt them —
including one **high-severity, destructive macOS defect** (see M-01) where a locked Keychain makes the
Mac claim you have no signing key and then instruct you into an action that overwrites the one you have.

---

## Coverage — what was and was not surveyed

Three dimensions were commissioned. Two were interrupted by a session usage limit and resumed; their
final areas were still arriving when this document was written.

| Dimension | Scope | Status |
|---|---|---|
| **D3** platform, packaging, privilege, CI | 5 areas | **complete** — 22 findings |
| **D1** GUI surface + behavioural honesty | 5 areas | **partial** — feature surface + Conclusions/Witness done; shell/menu, remaining-pane honesty & copy, Mirror/Cage inventory **pending** |
| **D2** Kit twins + CLI | 4 areas | **partial** — transport subsystem + behavioural divergence done; CLI subcommand parity, Kit tri-state **pending** |

**Not surveyed at all, and worth commissioning separately:** Prophet and Mirror pane internals (both
reserved), Cage beyond inventory, the Python server, and Windows-vs-macOS parity (this pass compared
Linux against macOS only).

Every claim below carries `file:line`. Items marked ★ I verified independently against source rather
than accepting the reviewer's reading.

---

## 1. Findings — Linux behind macOS

### 1.1 The keystone: Linux has no shared application state (D1-00) — HIGH

Every Linux pane constructs its own view model in its own code-behind (`ConclusionsView.axaml.cs:12`,
`DashboardView.axaml.cs:12`, `GluttonView.axaml.cs:12`, `MirrorView.axaml.cs:12`,
`PortalView.axaml.cs:12`, `ProphetView.axaml.cs:12`, `SettingsView.axaml.cs:12`,
`WitnessView.axaml.cs:12`; `CageView.axaml.cs` sets no DataContext at all). macOS has one `AppModel`
injected as `@EnvironmentObject` (`ContentView.swift:107`); Windows has one `AppViewModel`.

**Nothing can see anything else.** There is one `Domain` field *per pane*, inverting the exact defect
macOS fixed as E108. `isPro` has nowhere to live, so it is hardcoded. There is no reset. And most
expensively:

**Linux's Conclusions pane cannot see any warden's output (D1-13).** `ConclusionsViewModel.cs:53-124`
calls `FirstRunScan.RunAsync` and nothing else — it never reads `SectionInputs`. So a user who runs all
six wardens and opens Conclusions gets a grade computed from a *separate, freshly-run scan*, not from
the evidence they just watched being collected. The result surface and the evidence surfaces are two
disconnected products sharing a window. This is corroborated from the other side inside the tree:
`WitnessViewModel.cs:88-93` computes drift and documents that it is deliberately not rendered because
"the Linux client has no drift-consuming Conclusions surface yet".

**Fix:** a `SessionModel` carrying `SectionInputs`+`Merge`, one `Domain`/`Server`, `IsPro` +
`RefreshEntitlementAsync`, `Conclusion?`, `DashboardLog`, and `ResetAll()`. Exposed as
`SessionModel.Current` so reserved panes need not be touched (see §4).

### 1.2 Inert affordances — HIGH

A gap class, not a single bug: **controls styled as buttons with no command attached.** On a product
whose value is that its output can be trusted, a control that silently does nothing is the same failure
mode as a fabricated value — the user believes an action occurred.

- ★ `DashboardView.axaml` — `grep -c 'Command|Click'` returns **0**. All five primary actions inert.
- ★ `MainWindow.axaml` — returns **0**. The "Start your story anew" reset pill (`:92-100`) is a bare
  `<Border>` with a `⟲` glyph. This is the shell-level face of the missing `ResetAll()`.
- Dashboard warden tiles do not navigate (macOS tiles set `model.selection`,
  `DashboardView.swift:277-278`).

`flushDNSCache` has **no C# twin at all** — macOS shells `dscacheutil -flushcache`
(`AppModel.swift:651-663`); Linux needs `resolvectl flush-caches` with an honest elevation-required
result. `witnessFree` likewise exists on macOS and Windows and nowhere on Linux.

### 1.3 Fabricated and stale claims on the Linux Dashboard — HIGH

- ★ **D1-07:** `DashboardView.axaml:109` hardcodes **"Disabled — traffic already encrypted (VPN)"**,
  and `:98` hardcodes the dimming. macOS drives both from `model.isEncrypted` with a four-way clause
  (`:499-506`) whose `nil` arm is **"not yet checked."** — the arm Linux most needs and entirely lacks.
- ★ **D1-02:** every warden status pill is a literal (`:138,:158,:178,:198,:217,:237`). They read
  "NOT RUN" after a run, and "LOCKED · PRO" to a paying subscriber, forever. ★ No C# twin of
  `DashboardWardenState` exists in either client (macOS has it at 77 lines) — a spine #6 gap.
- **D1-03:** the trust ring and "N of 6 checks have run" can never change — four of five
  `DashboardViewModel` properties are declared and never assigned.
- ★ **D1-08:** the Dashboard advertises **"$2 / moon"** (`:276`) against `LicenseView.axaml:29`'s
  actual **"$3.95 / month"**.
- **D1-06:** Linux still carries "THE COUNCIL'S CONCLUSION" and "THE CHRONICLE", which macOS removed
  by owner directive 2026-07-23 as result-shaped cards that the Dashboard should not carry.

### 1.4 Conclusions drops honesty signals the Kit already computes — MEDIUM-HIGH

- **D1-14:** `ResultAction.Qualified` — documented as "true when confidence is limited or a required
  check did not run", set on four of five arms — is rendered by macOS as a **QUALIFIED** pill whose
  comment reads *"the difference between 'you are clean' and 'what we could see looked clean'."*
  ★ `grep Qualified` over the Linux GUI returns **0**. The flag is already in the record the VM holds.
- **D1-15:** `ConfidenceText` is assigned `det.State.ToString()` — a field labelled *confidence*
  displaying the *state*. `CompletenessText` is assigned `""` — a labelled field that always renders
  blank.
- **D1-16:** active and suppressed diagnoses go into one orange list, distinguished only by
  parentheses. A suppressed finding reads as an active one.

### 1.5 Portal advertises a capability it does not have — HIGH

★ **D1-09:** `grep` for traceroute/`TraceState` over the Linux GUI returns **0**, while the pane's own
eyebrow reads "PORTAL • LOCAL NETWORK **& ROUTE**" and its lore promises "tracing the path of a hidden
interceptor". macOS has a five-state machine (`PortalView.swift:206-242`) whose `.failed` arm is amber
and says **"This is not evidence of a clean path."** — that sentence is the honesty content of the
whole panel. `TracerouteRunner.cs` already exists with a working Linux branch.

Also: no Portal domain input (resolved by the shared `Domain`), and no LAN-devices panel — macOS at
least ships an honest placeholder disclosing that discovery is CLI-only.

### 1.6 The engine gives up too early on Linux — HIGH

**K-15:** every `ProbeBudget` default diverges, and the C# per-probe deadline fires *below its own
transport worst case*:

| Section | C# total / per-probe | Swift total / per-probe |
|---|---|---|
| Prophet | 30s / **2.5s** | 45s / 5.0s |
| Mirror | 30s / **2.5s** | 50s / 6.0s |
| Portal | 15s / **2.5s** | 30s / 5.0s |
| Glutton | 20s / **2.5s** | 25s / 5.0s |

UDP worst case is identical on both platforms: 2 attempts × 2000 ms = **4.0s**. Swift's 5.0s fits it
with slack; C#'s 2.5s fires 1.5s *before the retry could complete*, so a UDP probe needing a retry can
never return an answer on Linux. The Swift comment (C058) explicitly identifies the flat 2.5s as the
broken value it replaced, on the grounds that an early deadline "discards arriving evidence". DoH is
worse: C# allows the transport 8s under a 2.5s probe deadline — a 3.2× shortfall.

**K-09(a):** the same upstream fix added a 1.5s grace drain that returns a late-but-valid result rather
than "a fabricated absence" (`ProbeRunner.swift:117-128`). C# has none. **These two are halves of one
fix and must land together** — a longer deadline without the recovery window changes timing without
fixing the evidence loss.

### 1.7 Security-relevant false negatives in shared logic — HIGH

Three cases where both platforms have the code and the C# side is deficient:

- **K-16 — CDN softening dismisses tampering.** With resolver and authoritative sets disjoint but
  inside one provider's ranges, C# concludes `Inconclusive` ("CDN/geo routing, not a rewrite") where
  Swift concludes `.diverges` and retains the divergence because "an attacker may share the provider's
  ASN". Traced downstream: `Diverges` emits an evidence item and a Positive Mirror subsection;
  `Inconclusive` falls to `default:` and **emits no evidence item at all**. Same network, silence on
  Linux. *Note:* the adjacent C# evidence text is only true *because* of the softening, so this fix
  requires a coordinated edit in `Synthesizer.cs`.
- **K-18 — typosquat detection misses transpositions.** C#'s edit distance is plain Levenshtein with
  two rolling rows, which structurally cannot carry the transposition term; its own doc hedges
  "Damerau-**ish**". Swift keeps a full matrix and applies the term (B140). Caps and call sites are
  identical, so it bites exactly at the cap: `payapl.com` vs `paypal` is distance 2 in C# (no finding)
  and 1 in Swift (finding). Confusables tables are identical — the divergence is six lines.
- **K-17 — underscore names silently dropped.** Swift's normalizer allows `_` (RFC 8552; SRV, DKIM,
  DMARC, mDNS) and its comment *claims Windows parity that does not exist*. C# rejects `_`, so
  `_dmarc.example.com`, `_sip._udp.…`, `_services._dns-sd._udp.local` return null and the caller drops
  the record entirely — vanishing from every count including exfil detection. Aggravating: Swift
  *discloses* excluded names via a counter; C# has neither counter nor disclosure. Fix both together.

### 1.8 Encrypted DNS on Linux — the path exists and is defective — HIGH

★ A premise correction worth stating: `EncryptedDNSConfigurator.cs` **does** exist with a Linux
`resolvectl` branch (`:101-113`) wired to the Linux GUI (`SettingsViewModel.cs:69`). The work is *fix a
shipped path*, not *port a subsystem*.

- **K-06 / P-05b — a DoT configuration is confirmed by a DoH probe.** `:75` probes `resolver.DohUrl`
  via a DoH-only path. On a network permitting 443 but blocking 853 — exactly the filtering posture
  Syxx exists to detect — `resolvectl` reports DoT configured and the DoH probe succeeds, so Syxx
  reports **Enabled** over failing DNS. Immediate honest fix is one line: pass
  `encryptedProbeCompleted: false` on the Linux path so the result is `Inconclusive`.
- **K-05 / P-05d — Linux can enable encrypted DNS and cannot disable it.** No `DisableAsync`, no
  `Disabled` member in the result enum. Windows handles this honestly and says so in a comment
  ("do not fabricate a result that claims encryption was turned off"); macOS actually reverts. Linux
  constructs a `Failed` result with reason `"disabled"` and renders **"Encryption is off."** while the
  system DNS stays pointed at the encrypted resolver. The adjacent comment claims character-for-
  character parity with Windows — true of the helper, false of the branch feeding it, so the comment
  conceals the divergence. **Two lines, reusing Windows' existing string.**
- **K-04** — resolver catalog is 3 entries vs 11, and C#'s non-nullable `DohUrl`/`DotHost` cannot
  represent the 4 entries that legitimately have neither. A macOS-documented resolver id is a hard
  error on Linux.
- **K-03** — DNSCrypt / Anonymized-DNSCrypt entirely absent from C#. The one transport that hides the
  client IP from the resolver is unavailable on the platform where `dnscrypt-proxy` is a first-class
  package.
- **P-05a** — `resolvectl dns ""` has no documented global form; needs a real box to settle.
- **P-05e** — `resolvectl` state is runtime-only: lost on reboot, on link change, overwritten by
  NetworkManager. The right Linux mechanism is a persistent
  `/etc/systemd/resolved.conf.d/50-syxx.conf` drop-in, which also *supplies the missing revert*.

### 1.9 The resolver-truth defect — HIGH, first-order

★ **P-06.** `NetworkKeyState.cs` routes all Linux resolver discovery through a `resolvectl status`
parser with four independent defects:

1. ★ `:277` `if (currentInterface == null) continue;` — the **Global** block is unreachable, which is
   exactly where `/etc/systemd/resolved.conf` DNS lives.
2. The `DNSOverHTTPS:` branch is dead by construction — systemd-resolved has no DoH.
3. The DoT branch matches `"DNS Over TLS:"`; real output is `DNSOverTLS setting:` or an inline
   `Protocols: … +DNSOverTLS` token list.
4. ★ Multi-server lines add the whole remainder as one entry, so `DNS Servers: 192.168.1.1 8.8.8.8`
   yields one invalid address and the link contributes **zero** resolvers.

When the parser yields nothing it falls back to `/etc/resolv.conf`, which under systemd-resolved is the
stub. ★ `IsUsableDNSServer` filters Documentation/Unspecified/Invalid/Multicast/Reserved/LinkLocal —
**not Loopback** — and ★ `grep 127.0.0.53` across the Kit, CLI and Linux tree returns **0 hits**. So the
stub is accepted as the system resolver and every comparison, leak and attribution probe is aimed at it.
macOS deliberately avoids this: `NetworkKeyState.swift:202-204` documents that `/etc/resolv.conf` is
not authoritative and prefers `scutil --dns`.

**P-22:** both tests covering this parser hand-write output formats that do not exist
(`ConfigAndPolicyTests.cs:356-371`, `:373-388`). They are green and prove nothing — worse than no test,
because they license the belief that this is covered.

### 1.10 Transport observation — the question users actually ask — HIGH

**K-01.** The "OBSERVED" tier has no producer and no consumer in C#; `DNSTransportState.cs` is dead
code (zero hits for its types outside the defining file). Linux can answer *configured* and *reachable*
and never *"are my queries actually leaving this machine encrypted?"* Per the C# file's own comment,
those cannot substitute: a machine configured for DoT that silently falls back to cleartext reads as
configured-and-reachable.

**K-02 must land first.** C#'s `MonotonicClock` uses `Stopwatch.GetTimestamp()` = `CLOCK_MONOTONIC`,
which does not advance across suspend, where Swift uses `CLOCK_MONOTONIC_RAW` and documents that it
must. Close a Linux laptop lid, reopen hours later, and the pre-suspend transport state is served as
current — the exact staleness-into-a-claim failure the type exists to prevent. Fix without P/Invoke:
require both monotonic **and** wall-clock age within the validity window.

### 1.11 Privilege: Linux has no privileged-execution layer — HIGH

**P-01.** macOS has `PrivilegeManager` (tri-state `granted/denied/unavailable`, never caches a refusal)
plus an `SMJobBless` XPC helper, version-compared and liveness-pinged. Linux has `geteuid()==0` plus
printed instructions to re-run under sudo. The Linux Cage pane is a 10-line code-behind with no view
model and a static instruction card whose own comment says graphical elevation is "still to be wired".

**P-02 reframes it:** ★ `pkexec` is *already load-bearing* in the tree — `UpdateService.cs:409` shells
`pkexec dpkg -i` to install updates. So graphical escalation is not a missing capability, just unused
for capture. Two gaps around it: no polkit action file is shipped, and `policykit-1` is not in the
`.deb` `Depends:`. **Recommend polkit over `setcap`** — `setcap cap_net_raw+ep` grants raw sockets to
every local user permanently, which for a forensics tool is a worse trade than a per-capture prompt.

### 1.12 Secret storage — HIGH, and worse than pass 1 recorded

Pass 1's G-16 established that the libsecret backend silently fails every write, so Linux secrets land
in secret-tool (only if installed) or an AES-GCM file. This pass adds three things G-16 did not have:

- ★ **The salt is a constant** — `SHA256("Syxx-AES-256-GCM-Key-v1")`, not per-install. So the 350k
  PBKDF2 iterations buy nothing: the entire KDF input is reconstructible from world-readable material
  (`/etc/machine-id` at 0444, plus the hostname).
- ★ **There is a write race.** `File.Create` writes the ciphertext at default mode and *then* chmods it
  to 0600. The containing directory is created with no mode, so `~/.syxx/keystore` is world-listable
  permanently.
- **Silent degradation to hostname-only.** All three derivation reads are `try { } catch { }` with no
  breadcrumb. `product_uuid` is mode 0400 so it *always* fails unprivileged, and is absent entirely on
  most arm64 — a declared target. If machine-id also fails, the key is PBKDF2 over a bare hostname,
  and nothing reports it.

**Honest synthesis of the exposure**, since two seats framed it differently: steady-state, a 0600 file
needs same-user, root, or a file leak (backup, synced home, stolen disk). But at *every write* there is
a window where the ciphertext is readable, and once captured it needs **no secret** to decrypt. So:
weak-KDF-plus-file-access with a race window — narrower than "any local process can decrypt at any
time", wider than pass 1's committed wording. ★ `PlatformSecretStore.cs:65-70` wires the same store into
**both** `LicenseManager` and `WitnessKeyStore`, so the licence rides it too.

**P-04b:** `chmod` is invoked as a PATH-resolved subprocess (`FileName = "chmod"`, unqualified) — the
exact hazard D102 already fixed elsewhere in this Kit via a `SystemToolPath()` helper.
`File.SetUnixFileMode` has been available since .NET 7 and this is net10.0.

### 1.13 Packaging: neither Linux artifact is the product — CRITICAL

★ **P-09.** The `.deb` publishes only the Avalonia GUI; its launcher is `exec /opt/syxx/Syxx "$@"`. The
snap has `source-subdir: Windows/dotNET/Syxx` — the CLI — with `command: Syxx`. ★ Both assemblies are
named `Syxx`, which is why this has been invisible. The GUI's Cage pane prints
**`sudo syxx cage --duration 30`**, which on a `.deb` install resolves to the *GUI*, launched as root
with an argument it ignores.

★ macOS solved exactly this and guards it at build time: `bundle-app.sh` builds `--product syxx`, copies
it into `Contents/Resources/`, and **fails the build** if it is missing, because otherwise you ship "a
distributable .app where Cage cannot start a real capture".

So all 16 CLI commands are unreachable from the `.deb`, the GUI is unreachable from the snap, and P-01
is *fatal rather than merely incomplete* — the GUI cannot run Cage, tells the user to run a CLI command,
and that command is not installed.

Supporting gaps: **P-10** no maintainer scripts at all (so no hardware-id capture, no polkit install,
no icon cache update); **P-11** `Depends:` omits `libsecret-1-0` (guaranteeing the degraded file path)
and `policykit-1`; **P-12** snap version hardcoded with only a comment asking someone to keep it in
step, where `build_deb.sh` greps the constant and fails closed; **P-03** the licence node-lock script
exists, is test-pinned in five places, explicitly reasons its way to install-time capture — and is
**never invoked**, because there is no `postinst`, so Linux licences silently fall back to
non-node-lockable and reinstalls burn activations.

### 1.14 CI: nothing builds or executes the Linux artifacts — HIGH

★ **P-17.** `grep build_deb|snapcraft|dpkg` across all workflows: **0 hits**. macOS has a
`package-macos` job that asserts bundle internals, and its own comment says why it exists — the layout
and helper embedding "were never exercised, which is how D079's missing `__launchd_plist` … went
unnoticed". `snapcraft.yaml:20-24` still carries a committed `UNVERIFIED` marker on its own stage path.

**P-18:** the Linux self-contained publish is configured but **never executed** — and both csprojs carry
comments warning that NSec and the DNS reflection paths are trim-hostile, "the green build that lies"
class. **P-19:** no Linux artifact is uploaded from any CI run, on the platform that ships first.
**P-20:** eight macOS suites have no Linux equivalent, notably `CLIExitCodeProcessTests.swift` — the
shared CLI *is* the Linux CLI, so nothing pins its exit codes anywhere.

### 1.15 The shipped CLI destroys keys and leaks keys — HIGH, worst findings in the survey

`Windows/dotNET/Syxx/` is the binary Linux actually ships. Two of these are irreversible-user-harm class.

★ **C-10 — `syxx witness --gen-keypair` silently destroys an existing signing identity.** macOS refuses
without `--force`: *"Re-running --gen-keypair would destroy your current signing identity"*
(`SyxxWitness.swift:69-74`, B249), because silently destroying it "makes previously signed snapshots
orphaned from their signer". ★ `grep force|Exists|fileExists` over `WitnessCommand.cs` returns **0** —
no existence check anywhere, and `--force` is not in the C# flag surface at all. Run it twice on Linux
and every previously signed testimony becomes unverifiable, with the old key unrecoverable.

**The aggravating context matters more than the bug.** `WitnessCommand.cs:87-91` cites *"D086 (Windows
regression of the same defect already fixed on macOS SyxxWitness.swift:9-40)"*. This exact file has
already been through **one round of porting a witness-key safety fix from macOS**, and B249 sits a few
lines from the ported fix and was not taken. **Action: a full file-level re-diff of `SyxxWitness.swift`
against `WitnessCommand.cs`, not a two-item patch.**

★ **C-11 — the private key is accepted on argv with no gate, and the help text advertises it.** macOS
gates `--key` behind `--insecure-key-arg` (`:131`) and warns (`:161`, B189) that argv is exposed via
`ps -ww`, shell history and CI logs. ★ `grep -ci 'insecure|ps -ww|shell history'` over
`WitnessCommand.cs`: **0**. And ★ the C# usage string (`:79`) leads with
`create <domain> --key <b64-private-key>` — the insecure form is the *documented* one, where macOS's
usage leads with `--key-file` and annotates `--key` as requiring the opt-in.

**This is worse on Linux than it would be on macOS**, inverting the usual assumption: stock Ubuntu
mounts `/proc` without `hidepid`, so `/proc/<pid>/cmdline` is world-readable and any local user can read
the Ed25519 private key of a running `syxx witness create`. Recent macOS restricts argv visibility
across users. *(`hidepid` default is inferred — distro-dependent, unverified on a host. The code
asymmetry is verified.)* C# already supports `--key-file`, so this is a gate plus a help correction, not
new capability.

**C-09 — `syxx cage` on Linux probably captures nothing while reporting success. TEST FIRST, do not fix
first.** `CaptureEngine.cs:323-324` asserts "on Linux/macOS, SOCK_RAW + **IPPROTO_UDP** already receives
all UDP packets". The call sites pass `ProtocolType.IP` (0) and `ProtocolType.IPv6` (41) — ★ `grep
ProtocolType.Udp` across the Kit returns **0**, so the comment's precondition is never met. No socket
option is applied on non-Windows (both `IP_HDRINCL` and `SIO_RCVALL` are Windows-gated), the socket is
bound to a single unicast address, and no `AF_PACKET`/`libpcap` path exists anywhere in the tree.
Expected outcome: little or nothing captured, reported as a successful session — the exact defect class
this repo keeps legislating against. Confirming it needs a root run on Linux. If confirmed, the remedy
is an `AF_PACKET` or libpcap path, not a flag tweak.

**C-12 — `sudo syxx cage` leaves root-owned files.** macOS hands ownership back via fd-based `fchown`
(never path-based, to avoid symlink following, C076) with a uid-validation guard. ★ `grep
SUDO_UID|SUDO_GID|chown` over `CageCommand.cs` and `Capture/*.cs`: **0**. The user cannot read or delete
their own capture without sudo. **Port the capability, not the flags** — macOS's `--owner-uid/--owner-gid`
exist only because its GUI escalates via XPC where `SUDO_UID` is unset; on Linux those variables are set.

**C-13/C-14 — `cage` flag divergence.** C# uses `--out` where Swift uses `--output`/`-o`, so any
documented macOS invocation fails on Linux — and C#'s own `witness` uses `--output`, so it is internally
inconsistent. C# also has no `--interface`/`-i` override, so on a multi-homed host, a container bridge,
or with a VPN up there is no way to choose the capture interface; it auto-picks the default-gateway
adapter with no override. That is precisely the split-tunnel case Syxx cares about.

**Copy nit, user-facing:** C#'s `cage` usage says "requires **administrator**" where Swift says "requires
sudo". Linux users are told to become an administrator.

**Verified non-gaps:** all 15 subcommands dispatch 1:1 with identical tokens on both sides; flag surfaces
are identical for 9 of 14 commands; the `--duration` 86400s ceiling and 10.0s default match exactly
(checked specifically because the last such cross-platform parity *claim* — K-17's underscore comment —
proved false); the exit-code contract is shared, and its undocumented-in-`--help` TODO is unresolved on
**both** platforms, so it is a shared gap not a parity gap.

### 1.16 Shell, tray and chrome — HIGH in aggregate

★ **D1-24 — no menu bar, no tray icon, no keyboard shortcut anywhere on Linux.** `grep
TrayIcon|NativeMenu|KeyGesture|HotKey|KeyBinding` over the Linux GUI: **0 hits**. Avalonia supports both
`NativeMenu` and `TrayIcon`, so this is a build gap, not a framework limit. macOS has About/Settings ⌘,/
License/Check-for-Updates ⌘U/Help ⌘?, plus number-key accelerators 1–8 on every sidebar row.

**D1-25 — Linux is the only client with no trust indicator outside the window, and the logic is already
compiled into its binary.** macOS has a `MenuBarExtra` whose severity dot comes from
`TrayIndicators.For(model.conclusion)` — *"the SAME mapping the grade and result badge use, so the three
can never disagree — and `.unknown` is grey, never the green of a measured-clean network."* Its dropdown
carries a **latched** monitor change with Acknowledge (*"so the user cannot miss 'your DNS changed' by
not having the window open when it happened"*), a watch toggle, and B247's *"⚠︎ Notifications denied —
monitoring cannot alert you"* under the rule *"never present monitoring as armed when it cannot alert."*
`Syxx.Kit/Synthesis/TrayIndicator.cs` exists and Linux takes `Syxx.Kit` by `ProjectReference`, so **it
already compiles into the Linux binary and is simply never called.** The honesty cost: Linux has no
surface that could ever tell the user monitoring is armed but cannot alert, and no latched channel for a
change detected while the window was closed.

**D1-26 — the sidebar PRO lock pills are hardcoded.** `NavAspectConverter.cs:26-39` sets `Locked: true`
for Mirror, Glutton and Witness as literals. A paying Pro subscriber sees a 🔒 PRO badge on three wardens
**forever**, in the most persistent chrome in the app. macOS drives the same pills from
`isLocked: !model.isPro`.

★ **D1-28 — the reset pill is the eleventh inert affordance, and the first in shell chrome.** macOS's is
a real `Button` → confirmation dialog → *"Erase session results"* / Cancel, with the message "This clears
every result and collected piece of evidence from this session," because §9.5 requires destructive
actions to confirm. Linux has the pill, no command, no confirmation, and no `ResetAll()`.

**D1-27 — nav divergence, one item provably unfinished:** the gold "Dimensions" external row exists on
macOS and is absent on Linux — and `NavAspectConverter.cs:41` defines a `GoldGlyph` that **nothing ever
returns**, with a header comment describing "gold for Dimensions". The converter was written to serve a
row that was never added. Also: result pane is "Conclusion"/`✒` on macOS vs "Conclusions"/`⟐` on Linux,
and Linux has no per-warden selected-row accent (one violet gradient for every row).

**D1-29 — Linux panes render flush to the window edge.** macOS defines one shell-owned origin with the
invariant *"the shell owns the pane origin; child panes must not add another outer inset."* Linux's shell
`ScrollViewer` has no padding and none of the eleven pane roots carry a margin. One-line fix on the
shell, which preserves the invariant. Related: Linux scrolls once at the shell rather than per pane, so
scroll position is the one piece of state the held-view design does not preserve.

**D1-30 — a broadcast arriving mid-session is never surfaced on Linux.** macOS shows it as a floating
panel from any pane; Linux references broadcasts *only* in the What's New pane, which is the launch
destination — so one present at launch is seen and one arriving later is not.

**D1-32 — startup typography: macOS fails loud, Linux fails silent.** macOS escalates a tri-state at
launch and traps a debug build if no face registered, with the rationale that a font failing to register
"renders as the system font with no error anywhere … a silent-fallback defect that survived a full
release cycle." Linux has a *test-time* guard but **no runtime check**, while addressing bundled faces by
`avares://` URI whose failure mode is a silent fallback. Given §1.13 already found the `.deb` doesn't
install the CLI, packaging regressions are a live risk on this platform specifically.

**D1-31 — the EULA cannot be re-read after acceptance on Linux** (macOS keeps it under Help). And
**About** is absent from *both* C# clients, not just Linux.

**Corrections to my own earlier framing, verified:** Linux's **update surface is real and complete**
(`SettingsView.axaml:32-56` → `UpdateViewModel` with live check/download over the same `UpdateService`);
macOS reaches it by ⌘U menu, Linux embeds it in Settings — justified idiom, **not a gap**. Likewise
`MotdLoadController` exists on all three platforms, so What's New load-state policy is genuine parity
despite different renderers.

---

## 2. Findings — macOS behind Linux (adopt from Linux)

**A parity plan that only moves one direction will import defects.** These are the reverse cases.

### M-01 — macOS claims "no signing key" when the Keychain is merely locked, then instructs the user into overwriting it. HIGH, destructive, GUI-layer only.

The chain: `WitnessKeyManager.swift:38-44` maps `.undetermined → false` in its `hasKey` Bool, and its
own doc at `:35-37` says *"callers that need to distinguish 'no key' from 'could not ask' should use
`checkKeyPresence()` instead"* — the honest tri-state was built (D084) and the UI was told to use it.
`AppModel.witnessVerify()` calls `hasKey` at `:2007` anyway and sets `.noKey`, which
`WitnessView.swift:46-48` renders as *"no signing key on this machine. **Enter your key above, then
verify again.**"* If the user complies, `saveWitnessKey` (`:1888-1906`) calls `saveKey` with **no
presence check** — it overwrites.

So a locked Keychain produces a false claim of absence plus an instruction whose completion rotates an
identity that still exists, and every previously sealed testimony stops verifying. This is verbatim the
failure Linux closed as RG-2. **Two lines in `AppModel.witnessVerify`:** switch on `checkKeyPresence()`
and add an `.undetermined` state. The Kit needs no change. **Single most valuable Linux → macOS
adoption found.**

### M-02 — macOS shows a result with no coverage statement. MEDIUM-HIGH.

macOS's module-status grid lives only on the *pre-result* branch (`ConclusionsView.swift:126-140`). Once
a conclusion exists, nothing says which wardens did not run — the `QUALIFIED` pill is the only residue,
and it is one bit. Linux is correct here: a persistent COVERAGE card with "Checks that ran" **and
"Checks that did not run"** in orange, fed from `result.Ran`/`result.Skipped`. Linux also has a
null-determination gate with honest copy macOS lacks: *"That is a gap in the evidence, not a clean
result."*

### M-03 — Linux is the reference on Witness lifecycle and error handling.

Explicit `WitnessTestimonyState` with derived `IsRunning`/`HasResult`, where `HasResult` is true for
`Failed` so a failure never falls back to the never-ran appearance — and the file records that Linux was
the *origin* shape and the other two were retrofitted (B195). `ErrorPresenter` on all four failure
branches. A resolver-absence guard that refuses to fabricate. Import validation before persistence.
Never-ran correctly amber, not red. **Nobody should "fix" Linux toward macOS here.**

### M-04 — foreign-snapshot contamination (pass 1, G-15). HIGH.

Windows and macOS let an external paste overwrite the field self-verify reads, then render a composite
that never existed — macOS shows your domain with a foreign signature, Windows your signature with a
foreign resolver, against its own comment saying "never fabricate a domain or resolver". Linux is immune.

### M-05 — Linux is ahead on encrypted DNS. Do not frame this axis as catching up.

★ macOS `EncryptedDNSConfigurator.swift:10-14` states that `NEDNSSettingsManager` requires an
Apple-granted entitlement the current SwiftPM build lacks, so **macOS cannot complete a save today**
and returns `.entitlementRequired`. The Linux `resolvectl` path needs no vendor grant.

### M-06 — smaller adoptions

Linux's error path on synthesis (`ErrorPresenter` + *"A crashed scan is an unmeasured network, not a
clean one"*) where macOS calls `Synthesizer.conclude` bare; Linux's `LinuxSecretStoreTests` and
`TypographyAssetsTests` have no macOS twins; and Linux is the only platform testing the Witness
severity-tier mapping at all.

### Not gaps — recorded so nobody chases them

No autostart/login-item/service on **any** platform — do not invent a systemd unit for parity. Linux
fonts are complete (nine families, OFL, embedded, with a compensating test). `CageRedundancyCorrelator`
and `ProbeRunner.runTyped` are dead on the *Swift* side. `PktapHeader` and `PrivilegedHelperProtocol`
are justified Darwin idioms. C# multi-type file consolidation under `Trust/`, `Forensics/`, `LAN/` is
layout only — do **not** "fix". `ProbeBudget` being a class in C# and a struct in Swift is a justified
language idiom.

---

## 3. The build plan — seats, ownership, sequencing

**Every file below has exactly one writer.** Two writers in one file is the failure this design exists
to prevent. A seat that needs another's file sends a request; it does not edit.

### Wave 0 — prerequisites (must precede everything)

| # | Task | Why first |
|---|---|---|
| **W0-a** | Unblock the Swift build — claim or fix the untracked `macOS/Sources/SyxxKit/DNSPathConfirmation.swift:90` (`DNSPathSnapshot` does not conform to `Equatable`) | ★ `SyxxKit` does not compile in-tree, so **no Swift test can run at any scope**. Every twin claim below would be *asserted, not verified*. This is another stream's file — it needs claiming, not seizing. |
| **W0-b** | Interface stubs: `SessionModel` (D1) and the `NetworkKeyState` seam (D3) land alone, as their own commits | Dependents cannot start against a moving contract. |
| **W0-c** | Request the resolver verification pack from the owner (§5) | Unblocks the highest-severity finding at a cost of under a minute. |
| **W0-d** | **Fix C-10 and C-11 immediately, ahead of the plan.** Both are in one file, both are irreversible-harm class, and neither depends on anything else in this document | A user running `syxx witness --gen-keypair` twice today destroys their signing identity with no warning; a user following the documented `--key` form today exposes their private key to every local account. These should not wait for a wave. |
| **W0-e** | **Test C-09 on a real Linux host with root** before any Cage work is planned | If `syxx cage` captures nothing while reporting success, the remedy is a new capture mechanism, not a fix — and that changes the plan's shape. Test first. |

### Wave 1 — parallel, no shared files

| Seat | Owns | Carries |
|---|---|---|
| **E1 resolver-truth** | `NetworkKeyState.cs`, `ConfigAndPolicyTests.cs`, `corpus/resolvectl-status/**` (new) | P-06, P-22. **Sole writer** — nominally contended, so nobody else touches it. Highest priority: nothing downstream of resolver discovery is trustworthy until it is fixed. |
| **E3 delivery+privilege** | `build_deb.sh`, `Linux/Ubuntu/debian/**` (new), `Linux/Ubuntu/polkit/app.syxx.policy` (new), `snapcraft.yaml`, `.github/workflows/ci.yml` | P-02(ship), P-03(wire), P-09, P-10, P-11, P-12, P-13, P-17, P-18, P-19. **Largest single win: P-09 + P-17.** Start immediately — zero dependencies. |
| **E4 secret-store** | `LinuxSecretStore.cs`, `LinuxSecretStoreTests.cs` | P-04, P-04b. **Merge with** any surviving pass-1 seat holding this file. |
| **E5 transport-observed** | `DNSTransportState.cs`, `FlowTransportObserver.cs` (new), `DNSTransportMonitor.cs` (new), + 3 new test files | K-02 **then** K-01, hard order. Needs a *lease* on one field in `Synthesizer.cs`, not the file. |
| **E6 twin-classification** | `AuthoritativeVantage.{cs,swift}`, `LookalikeDetector.{cs,swift}`, `Forensics/DNSLogAnalyzer.{cs,swift}`, `ForensicsAnalyzerTests.cs` | K-16, K-17, K-18, K-07. Three of four high-severity twin items. Needs the `Synthesizer.cs` lease for K-16. |
| **E7 twin-timing** | `ProbeBudget.{cs,swift}`, `ProbeRunner.{cs,swift}`, `DNSResolver.{cs,swift}` (surgical), `LAN/DomainCanonical.cs` + `DomainCanonical.swift` | K-15 + K-09 **together** (two halves of C058), K-19, K-20. |
| **E8 tri-state** | `TriState.{cs,swift}`, `TriStateAndDnssecTests.cs`, `MatrixUntestedStateTests.cs`, `MatrixAllNegativeParityTests.cs` | Kit spine #3. **Blocked on the Stage5 decision** before touching `Conclusion.cs`. |
| **E9 macOS-adopts** | `macOS/Sources/SyxxApp/AppModel.swift`, `WitnessView.swift`, `ConclusionsView.swift` | M-01 (two lines, destructive bug), M-02, M-06. Small, high value, independent. |

### Wave 2 — after Wave 0-b and Wave 1's contended seats

| Seat | Owns | Carries |
|---|---|---|
| **E2 encrypted-DNS** | `EncryptedDNSConfigurator.cs`, `EncryptedDNSConfig.cs`, `EncryptedTransportProbe.cs`, `SyxxSettings.cs`, `DNSCryptProxyConfig.cs` (new), `DNSCryptProxyManager.cs` (new), + 2 new test files | **Merged seat** (D2-B into D3-S2 — do not run both). Blocked by E1. Internal order **K-06 → K-04 → K-05 → K-03**: K-06 first because it is the only one that makes *shipped* behaviour lie; K-04 before K-03 because the nullable-catalog reshape is what makes DNSCrypt entries representable. |
| **E10 dashboard** | `DashboardView.axaml{,.cs}`, `DashboardViewModel.cs`, `Windows/dotNET/SyxxUI/Presentation/DashboardWardenState.cs` (new), `DashboardViewModelTests.cs` (new) | D1-01…D1-08. Blocked by `SessionModel`. Warden-state helper goes in the Windows presentation folder and is **linked** into Linux — authoring it Linux-side means writing it twice and re-opening spine #6. |
| **E11 portal** | `PortalView.axaml{,.cs}`, `PortalViewModel.cs`, `PortalViewModelTests.cs` (new) | D1-09/10/11. Reads `TracerouteRunner.cs`, does not own it. |
| **E12 conclusions** | `ConclusionsView.axaml{,.cs}`, `ConclusionsViewModel.cs`, `ConclusionsViewModelTests.cs` (new) | D1-13…D1-18. |
| **E13 panels+copy** | Onboarding/WhatsNew/License/Settings views + their VMs, `Services/*`, new panel files, **and both output catalogs as sole writer** | Includes the two-line P-05d fix. **Catalog rule:** each seat writes rows to its own scratchpad file; E13 performs one append per catalog at the end. Append-only prevents reordering but not a git conflict at a shared tail, and the Mirror stream is appending concurrently. |
| **E14 CLI parity** | `Windows/dotNET/Syxx/Program.cs`, all 16 `Commands/*.cs`, **and `Syxx.Kit/Capture/CaptureEngine.cs`** (C-09/C-12 land there, not in `Commands/`) | C-10…C-16. **Sequence C-10 and C-11 first** — both are in `WitnessCommand.cs` and both are security/data-loss class, ahead of any flag-naming work. The seat's brief must require a **full re-diff of `SyxxWitness.swift` against `WitnessCommand.cs`**, not a two-item patch: the D086 comment proves a partial port of that exact pairing already happened once and missed B249. |
| **E15 shell+tray** | `MainWindow.axaml{,.cs}` and `Converters/NavAspectConverter.cs` — **coordinate with E-keystone, which also owns `MainWindow`**; give the reset pill (D1-28) to the keystone seat since it owns `ResetAll()`, and give tray/menu/nav to E15 | D1-24…D1-30, D1-32. `TrayIndicator.cs` already compiles into the Linux binary — no new reasoning code, only a caller. |
| **E-audit** | nothing — read-only | Continuous honesty audit. Non-negotiable: this codebase's characteristic failure is **tests that assert nothing** (three were found in pass 1, two more here). Mutation-test every guard. |

### Not seats — routed out

- **Cage capture wiring** touches `CageCommand.cs`, `Program.cs`, `CageView.axaml` — all inside the
  hard Cage keep-out, mid-transition under a paused agent. E3 ships the polkit action and the
  dependency; the **call site is a requirement handed to the Cage stream**.
- **Linux Prophet and Mirror panes** stay unmigrated. `SessionModel.Current` is a static accessor
  precisely so their reserved files need not be touched; both are slated for wholesale rewrite, so the
  contract note folds into that rewrite. **Cost, stated plainly: per-pane domain closes for 4 of 6
  wardens, not 6.** Closing all six needs a two-file, one-line-each lift — declined, overridable.
- **Linux Witness** is contended by live uncommitted work. No seat owns it; D1's Witness findings
  (M-01, M-03, D1-21, D1-22, D1-23) route to that stream as advisory notes.

### Keep-out correction

★ The reserved list (`docs/HANDOFF-mirror-warp-gui-2026-08-04.md` §1) covers exactly **eight** Linux
files: `MirrorView` ×3, `ProphetView` ×3, `CageView` ×2. §1.3 lists no Linux Witness file, and §4
explicitly grants "license/settings/about surfaces, shell/navigation chrome". **So D1 needs no
keep-out lift** — an earlier assumption to the contrary was wrong and removed two owner decisions.
**But §1.3 is stale:** the Linux Witness twin now *does* carry live uncommitted modifications, which is
the precise condition §1.3 cites for reserving the other two. Recommend adding it.

---

## 4. Owner decisions — none of these are an implementer's call

1. **Packaging and command surface (blocks E3, gates P-09).** Recommended: both packages ship both
   binaries, CLI as `syxx`, GUI as `syxx-gui`. That is what macOS does and it makes every existing
   "run `sudo syxx cage`" string true. Alternatives: per-channel instruction strings, or rename an
   assembly. This changes the shipped command surface.
2. **Linux privilege model (blocks E3's policy file and the Cage routing).** Recommended: pkexec-per-
   capture with a shipped polkit action — the mechanism is already in the tree and leaves no standing
   privilege. `setcap` is rejected on the grounds above. Note the hardware-id script already rejected
   setuid and root-daemon for the same class of reasoning.
3. **Linux at-rest posture (E4's ceiling).** E4 can harden (per-store salt, honest degradation,
   0600/0700) but **cannot close** this. Closing it needs a user secret — passphrase-wrapping the
   witness key, prompted on first sign per session. User-visible behaviour change. Recommend deciding
   *now* rather than shipping the current posture on the first-ship platform.
4. **Encrypted-DNS mechanism + captive-portal failsafe (blocks E2's persistence work).** Confirm the
   persistent `resolved.conf.d` drop-in over runtime `resolvectl` — the case where a Linux-native
   mechanism beats a literal port, and it supplies the missing revert. The portal auto-revert policy is
   explicitly yours per the file's own comment and is still absent.
5. **Node-lock on Linux.** Wire the hardware-id `postinst`, or accept non-node-lockable installs
   permanently? Snap cannot do the `postinst` either way, so this also decides whether snap and `.deb`
   have different licensing behaviour.
6. **Stage5 (blocks E8 touching `Conclusion.cs`).** `Stage5Presentation.swift` is 533 lines with live
   tests and consumers, while `Conclusion.cs:20` records `Stage5` as **deliberately removed** in the
   MATRIX-MIGRATION rewrite. Either Swift is stale or the C# removal was premature. Nothing in the tree
   settles it.
7. **The decoy CLI.** `Linux/dotNET/Syxx.Linux/Program.cs` — every subcommand prints "(implementation
   pending)". Recommended: **delete, not repair** — a stub sharing the product name is a live
   mis-signal about shipped capability. This is a `git rm`.
8. **Dashboard price.** Correct "$2 / moon" to $3.95, or delete the banner. macOS has no such banner.
9. **The narrow Prophet/Mirror lift** (§3) — declined by default, yours to overrule.
10. **Conclusions eyebrow copy.** macOS says "THE COUNCIL · SYNTHESIS"; Linux and Windows say
    "DIAGNOSTICS • SYNTHESIS". macOS is the outlier. A copy question, not a defect.

---

## 5. Verification

**Request this first.** One command block, under a minute, and it unblocks the highest-severity finding:

```
resolvectl --version; echo "--- STATUS"; resolvectl status --no-pager
echo "--- RESOLVCONF"; cat /etc/resolv.conf; echo "--- MODE"; readlink -f /etc/resolv.conf
```

**Expectation declared in advance**, so the result cannot be rationalised after the fact:
- **PASS** — output shows a `Global` section and ≥1 `Link` section, with DoT state appearing either as
  a `Protocols:` token (`+DNSOverTLS`/`-DNSOverTLS`) or a `DNSOverTLS setting:` line.
- **FAIL for the current parser** — neither the literal `DNS Over TLS:` nor `DNSOverHTTPS:` appears
  anywhere, confirming both branches and both tests are dead.
- **INCONCLUSIVE** — systemd-resolved is not running, in which case the fallback path needs a different
  pack.
- **False clean to refuse:** if `/etc/resolv.conf` does *not* contain `127.0.0.53` on your specific
  machine, that does **not** clear P-06 — it means your box is not in stub configuration while the
  majority of Ubuntu Desktop installs are. Do not let one clean paste close it.

That paste becomes a golden corpus, and CI holds it permanently thereafter.

### Verification split

- **Closable by CI alone:** P-04b, P-05c, P-09, P-10, P-11, P-12, P-13, P-17, P-18, P-19, and
  P-06/P-22 permanently once the corpus exists.
- **Closable by neither — provable today by reading, so fixable before asking you for anything:**
  P-05b, P-05d, P-06's four structural defects, P-07, P-08, P-20, P-21, all of P-04, and every K-item
  in §1.6/§1.7, plus M-01…M-03.
- **Require a real Linux box:** P-05a, P-06's true output formats, P-01/P-02 (pkexec with and without
  an agent), P-03 (`postinst` as root; the dual-boot claim needs two installs), P-04 (libsecret against
  a live keyring), P-05e (drop-in persistence across reboot), P-09/P-17's install leg, P-12's snap
  stage path, P-16's snap update channel.

### Standing bars

- `cd Linux/Ubuntu/SyxxUI.Avalonia.Tests && dotnet test` — **165/165** today. Any drop is a regression.
- `cd Windows/dotNET/Syxx.Tests && dotnet test` — **12 failed / 907 passed**, all 12 the known non-Warp
  baseline. Check failure **names**, not the count.
- `swift build --package-path macOS --target SyxxKit` — **currently fails** (W0-a). Until then, no Swift
  claim is verified.
- `Windows/dotNET/SyxxUI` **cannot build on macOS** (WinUI XamlCompiler crashes before `csc`). Never
  report Windows-GUI verification from this host.

---

## House rules inherited

The word **"verdict"** is banned — `result` in code, `conclusion` in UI and docs; the sole exception is
the pre-existing signed wire field `evidence.comparisonResult.verdict`, never renamed and never
introduced anywhere new. Every new user-facing string is **appended** to both output catalogs, never
sorted or reordered. Honesty protocol: `Idle / Running / Completed / Failed` — Failed ≠ never-ran ≠
match. Never soften, disable or delete a test to go green; a failing honesty test is data. Explicit-path
commits only; never `git add -A`.
