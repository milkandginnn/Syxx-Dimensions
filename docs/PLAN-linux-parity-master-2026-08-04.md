# Linux ↔ macOS parity: consolidated register and build plan

- **Date:** 2026-08-04. **Single source of truth** — supersedes and replaces
  `PLAN-linux-parity-team-2026-08-04.md` (pass 1, narrow debug run) and
  `PLAN-linux-macos-parity-pass2-2026-08-04.md` (pass 2, the parity survey). Both remain in git history.
- **Stage:** planning, except where §2 marks work as LANDED. Pass 2 changed no code at all — its three
  reviewer seats had no Write or Edit tool, by design.
- **ID namespaces are preserved as issued**, because commits and code comments already cite them:
  `G-nn` pass 1, `Q-nn` questions, `D1-nn` GUI survey, `K-nn`/`C-nn`/`T-nn` Kit and CLI survey,
  `P-nn` platform survey, `M-nn` macOS-should-adopt.
- ★ marks a claim I verified against source myself rather than accepting a reviewer's reading.

---

## Read this first — in plain terms

**Two separate exercises produced this.** Pass 1 was a debug run on the in-flight Witness/Warp changes —
narrow, five hypotheses, and it did **not** compare the two apps. Pass 2 was the first actual
feature-by-feature comparison of the Linux client against macOS. Pass 1's summary once implied the
opposite and has been corrected; the distinction matters because pass 1's clean bill on a few code paths
was read as reassurance about the product.

**The seven things that matter most:**

1. **Five buttons on the Linux main screen do nothing.** Styled rectangles with no command attached.
2. **The Linux app tells every user their traffic is already VPN-encrypted, before checking.** Hardcoded.
3. **Neither Linux package is the whole product.** The `.deb` ships only the GUI, the snap only the CLI,
   both binaries are named `Syxx`, and the GUI tells users to run a command the `.deb` never installs.
4. **On Linux the app measures the wrong DNS server** — it can't parse real `resolvectl` output, falls
   back to `/etc/resolv.conf`, and accepts the systemd stub as "the system resolver." That is the
   product's central function, on its declared target platform.
5. **Linux gives up before answers arrive.** Probe deadline 2.5s; a DNS retry needs 4s. macOS allows 5s
   for exactly this reason. Linux records "inconclusive" where macOS records evidence.
6. **The shipped CLI destroys signing keys and leaks them.** `--gen-keypair` overwrites an existing
   identity with no warning; `--key` puts a private key where other local accounts can read it, and the
   help text advertises that form.
7. **No CI job has ever built the Linux packages.** That is why 1–4 survived.

**The direction is not one-way.** Several things are better on Linux and macOS should adopt them,
including a **destructive macOS defect** (M-01) where a locked Keychain makes the Mac claim you have no
signing key and then instruct you into overwriting the one you have.

**Do these before the build wave** — small, live today, each destroys or exposes an identity: **C-10**,
**C-11**, **M-01**. And **test C-09** before planning any Cage work.

---

## 1. Status and coverage

### 1.1 Landed — pass 1's build pass (four seats, one read-only auditor). Not pushed.

| Item | State | Note |
|---|---|---|
| **G-01** `SaveKey` verify-after-store | **LANDED** | read-back + ordinal compare + throw before the token write; orphan discarded |
| **G-02a** positive proof of absence | **LANDED** | `File.Exists` → guarded open; `HasKey`'s error-as-absence removed |
| **G-02b** store-before-mint | **PARTIAL — see §3.4** | closed for file-store + secret-tool; **open** for libsecret |
| **G-03** first-run manifest copy | **LANDED (C#)** / Swift **unverified** | both twins edited; Swift cannot run, see G-16b |
| **G-04** two broken CI gates | **LANDED** | both repairs; assertion proven non-vacuous |
| **G-05** test seam | **LANDED** | live hazard removed (key path **and** file-store dir) |

**Anti-vacuity evidence**, because this codebase's characteristic failure is tests that assert nothing:
the key-store guards were mutation-tested — neutering all **five** flips 9 tests, restoring returns 22/22,
and the auditor reproduced it independently with its own mutations, getting 8 and then chasing the
discrepancy to a fifth guard site the seat's own label had undercounted. The pin-parity self-test was
mutation-tested in a scratchpad copy. The CI assertion was cleared three ways, including ruling out
`actions/cache` as a staleness vector. G-03's end-to-end test was traced to the only two `ScriptMismatch`
emit sites. Catalog appends verified at **0 deletions**.

### 1.2 Surveyed

| Dimension | Scope | Status |
|---|---|---|
| **D3** platform, packaging, privilege, CI | 5 areas | **complete** — P-01…P-22 |
| **D1** GUI surface + behavioural honesty | 5 areas | **complete** — D1-00…D1-39 |
| **D2** Kit twins + CLI | 4 areas | **complete** — K-01…K-20, C-09…C-16, T-01…T-04 |

### 1.3 Not surveyed — commission separately

Prophet and Mirror pane internals (reserved), Cage beyond inventory, the Python server, and
**Windows-vs-macOS parity** — both passes compared Linux against macOS only, yet §3.2's C-10/C-11 and
several K-items are C#-side and therefore hit **Windows too**.

---

## 2. Ground truths — verified first-hand, and they reshape assumptions

**2.1 `Linux/dotNET/Syxx.Linux/` is a decoy.** 166 lines in which every subcommand prints
"(implementation pending)". **Recommendation: delete, not repair** — a stub sharing the product name is a
live mis-signal about shipped capability. Owner decision, it is a `git rm`.

**2.2 The shared C# CLI *is* the Linux CLI.** `Windows/dotNET/Syxx/` is what Linux runs, so a C#-side CLI
gap is a Linux gap even though the file lives under `Windows/`. Same for `Syxx.Kit`, which Linux takes by
`ProjectReference`.

**2.3 `Syxx.Kit` already carries Linux awareness** — including `TrayIndicator.cs` and
`EncryptedDNSConfigurator.cs`'s `resolvectl` branch. Several "missing subsystem" assumptions are wrong;
the work is often *fix a shipped path*, not *port one*.

**2.4 ★ The keep-out is narrower than assumed.** `docs/HANDOFF-mirror-warp-gui-2026-08-04.md` §1 reserves
exactly **eight** Linux files: `MirrorView` ×3, `ProphetView` ×3, `CageView` ×2. §1.3 lists no Linux
Witness file, and §4 explicitly grants "license/settings/about surfaces, shell/navigation chrome." **So
the GUI work needs no keep-out lift** — an earlier assumption to the contrary removed two false owner
decisions. **But §1.3 is stale:** the Linux Witness twin now carries live uncommitted modifications, the
precise condition §1.3 cites for reserving the other two. Recommend adding it.

**2.5 A rule for every seat: a cross-platform parity claim in a code comment is not evidence.** Three
Swift-side "Windows parity" assertions were tested; **two were false** — `DNSLogAnalyzer.swift:308` claims
underscore support exists on the Windows twin (K-17: it does not), and `Stage5Copy.swift:9` points at a
C# file that **does not exist**. Only the `cage --duration` ceiling held. Checking costs one file open and
it changed a reviewer's conclusion twice.

---

## 3. The register — open, Linux behind macOS

### 3.1 CRITICAL — packaging ships disjoint halves (P-09…P-12, P-17…P-19)

★ The `.deb` publishes only the Avalonia GUI (`exec /opt/syxx/Syxx "$@"`); the snap has
`source-subdir: Windows/dotNET/Syxx` — the CLI. ★ **Both assemblies are named `Syxx`**, which is why this
was invisible. The GUI's Cage pane prints **`sudo syxx cage --duration 30`**, which on a `.deb` install
resolves to the *GUI*, launched as root with an argument it ignores.

★ macOS solved this and guards it: `bundle-app.sh` builds `--product syxx`, copies it into
`Contents/Resources/`, and **fails the build** if it is missing, because otherwise you ship "a
distributable .app where Cage cannot start a real capture."

★ **And no CI job has ever built either Linux package** — `grep build_deb|snapcraft|dpkg` across all
workflows: **0 hits**. `snapcraft.yaml:20-24` still carries a committed `UNVERIFIED` marker on its own
stage path. macOS has a `package-macos` job whose comment says why it exists: the bundle layout and helper
embedding "were never exercised, which is how D079's missing `__launchd_plist` went unnoticed."

Supporting: **P-10** no maintainer scripts at all, so no hardware-id capture, no polkit install, no icon
cache; **P-11** `Depends:` omits `libsecret-1-0` (guaranteeing the degraded file path) and `policykit-1`;
**P-12** snap version hardcoded with only a comment asking someone to keep it in step, where
`build_deb.sh` greps the constant and fails closed; **P-03** the licence node-lock script exists, is
test-pinned in five places, explicitly reasons its way to install-time capture, and is **never invoked**,
so Linux licences silently fall back to non-node-lockable and reinstalls burn activations; **P-18** the
self-contained publish is configured but never executed, and both csprojs warn the DNS paths are
trim-hostile — "the green build that lies" class; **P-19** no Linux artifact is uploaded from any run, on
the platform that ships first.

### 3.2 HIGH — the shipped CLI destroys keys and leaks keys (C-10, C-11)

★ **C-10 — `syxx witness --gen-keypair` silently destroys an existing signing identity.** macOS refuses
without `--force`: *"Re-running --gen-keypair would destroy your current signing identity"*
(`SyxxWitness.swift:69-74`, B249), because it "makes previously signed snapshots orphaned from their
signer." ★ `grep force|Exists|fileExists` over `WitnessCommand.cs`: **0**. No existence check, and
`--force` is not in the C# flag surface.

**The precedent matters more than the bug.** `WitnessCommand.cs:87-91` cites D086 — a *prior* port of a
witness-key safety fix from the same macOS file. That pairing has already been partially ported once and
B249 was missed. **Action: a full file-level re-diff of `SyxxWitness.swift` against `WitnessCommand.cs`.**

★ **C-11 — the private key is accepted on argv with no gate, and the help advertises it.** macOS gates
`--key` behind `--insecure-key-arg` and warns that argv is exposed via `ps -ww`, shell history and CI logs
(B189). ★ `grep -ci 'insecure|ps -ww|shell history'`: **0**. ★ The C# usage string leads with
`create <domain> --key <b64-private-key>` — the insecure form is the *documented* one.

**Worse on Linux, inverting the usual direction:** stock Ubuntu mounts `/proc` without `hidepid`, so
`/proc/<pid>/cmdline` is world-readable and any local account can read the key of a running
`syxx witness create`. *(hidepid default inferred; the code asymmetry is verified.)* C# already supports
`--key-file`, so this is a gate plus a help correction.

### 3.3 HIGH — the resolver-truth defect (P-06, P-22), first-order

★ `NetworkKeyState.cs` routes all Linux resolver discovery through a `resolvectl status` parser with four
defects: ★ `:277` `if (currentInterface == null) continue;` makes the **Global** block unreachable — where
`resolved.conf` DNS lives; the `DNSOverHTTPS:` branch is dead by construction (systemd-resolved has no
DoH); the DoT branch matches `"DNS Over TLS:"` where real output is `DNSOverTLS setting:` or an inline
`Protocols:` token list; and ★ multi-server lines add the whole remainder as one entry, so
`DNS Servers: 192.168.1.1 8.8.8.8` yields one invalid address and the link contributes **zero** resolvers.

When the parser yields nothing it falls back to `/etc/resolv.conf` — the systemd stub. ★
`IsUsableDNSServer` filters Documentation/Unspecified/Invalid/Multicast/Reserved/LinkLocal — **not
Loopback** — and ★ `grep 127.0.0.53` across the Kit, CLI and Linux tree returns **0 hits**. So the stub is
accepted as the system resolver and every comparison, leak and attribution probe aims at it. macOS
deliberately avoids this, documenting that `/etc/resolv.conf` is not authoritative and preferring
`scutil --dns`.

**P-22:** both tests covering this parser hand-write formats that do not exist. Green, and worse than no
test, because they license the belief that this is covered.

### 3.4 HIGH — secret storage (G-02b, G-16, P-04)

**G-16 — the libsecret backend cannot store, so nothing ever reaches the keyring through the primary
path.** An undeclared attribute name makes `secret_attributes_buildv` return NULL and
`secret_password_store_sync` return FALSE **without setting the GError**; ★ `LinuxSecretStore.Store:57-61`
treats that as "backend declined" and falls through. The call passes the keyType *value* into the
attribute-*NAME* varargs slot, while the schema declares `syxx-key-type`/`syxx-key-id`.
*Evidence boundary: established against master-branch libsecret; not verified against 0.18/0.20 or
distro-patched builds. "Never, on every machine" was stronger than the evidence.*

**Security posture, which outranks the honesty gap.** Every Linux secret therefore lands in secret-tool
(only if installed) or `~/.syxx/keystore` under AES-256-GCM with ★ **no user secret in the derivation**,
★ **a constant salt** (`SHA256("Syxx-AES-256-GCM-Key-v1")`, so 350k PBKDF2 iterations buy nothing against
a reconstructible input), ★ **a write race** (`File.Create` writes then chmods; the directory is created
with no mode, so `~/.syxx/keystore` is world-listable permanently), and **silent degradation** — all
three derivation reads are `try{}catch{}` with no breadcrumb, `product_uuid` is mode 0400 so it *always*
fails unprivileged and is absent on most arm64, and if machine-id also fails the key is PBKDF2 over a bare
hostname with nothing reporting it.

**Honest synthesis of the exposure:** steady-state, a 0600 file needs same-user, root, or a file leak
(backup, synced home, stolen disk). But at **every write** there is a window where the ciphertext is
readable, and once captured it needs **no secret** to decrypt. Weak-KDF-plus-file-access with a race —
narrower than "any local process can decrypt at any time," wider than pass 1's first wording.
★ `PlatformSecretStore.cs:65-70` wires the same store into **both** `LicenseManager` and
`WitnessKeyStore`, so the licence rides it too.

**G-02b boundary, verbatim:** *mint-guard closed wherever the secret is enumerable **without the keyring's
cooperation** — the file store (including every case where the keyring was locked, missing or unusable
**at mint time**) and a keyring secret-tool can still query **at probe time**. **OPEN** where the secret
is in the keyring but unanswerable **at probe time** — `libsecret-tools` removed after minting, or the
Secret Service locked/absent when we probe. The libsecret P/Invoke backend is not a route because it
cannot store at all; **if G-16 is repaired that route opens and the fail-open arm becomes load-bearing, so
the two must land together.*** The boundary is **temporal, not per-backend.**

**The fail-open arm is deliberate and accepted:** mapping `Unknown` → `Undetermined` would make first-run
minting impossible on every GNOME/KDE desktop, leaving no route to a first identity — a worse failure. A
Warning breadcrumb fires rather than the fallback being silent, and the characterization test is labelled
"tighten the behaviour, do not soften the assertion."

**G-16 needs NO migration path.** An earlier draft recorded one; the P/Invoke secrets it would strand do
not exist, because those writes all returned FALSE. Keep a legacy read path only as cheap insurance
against an unverified older libsecret.

**Also open, self-reported:** `SecretTool.Exists` reads "exit 0 + empty stdout" as proof of `Absent` — the
one place that probe could manufacture a **false Absent**. Unverified without a keyring; costs nothing
while the caller's `Unknown` arm also mints; the assumption and its exact test are written into the code.
**First item on any real-keyring leg.** **P-04b:** `chmod` is invoked as a PATH-resolved subprocess, the
hazard D102 already fixed elsewhere in this Kit via a `SystemToolPath()` helper;
`File.SetUnixFileMode` exists since .NET 7 and this is net10.0.

### 3.5 HIGH — the engine gives up too early (K-15, K-09)

**K-15** — every `ProbeBudget` default diverges, and the C# per-probe deadline fires **below its own
transport worst case**:

| Section | C# total / per-probe | Swift total / per-probe |
|---|---|---|
| Prophet | 30s / **2.5s** | 45s / 5.0s |
| Mirror | 30s / **2.5s** | 50s / 6.0s |
| Portal | 15s / **2.5s** | 30s / 5.0s |
| Glutton | 20s / **2.5s** | 25s / 5.0s |

UDP worst case is identical on both: 2 attempts × 2000 ms = **4.0s**. Swift's 5.0s fits with slack; C#'s
2.5s fires **1.5s before the retry could complete**, so a UDP probe needing a retry can never return an
answer on Linux. The Swift C058 comment explicitly identifies the flat 2.5s as the broken value it
replaced, because an early deadline "discards arriving evidence." DoH is worse: C# allows the transport 8s
under a 2.5s probe deadline.

**K-09(a)** — the same upstream fix added a 1.5s grace drain returning a late-but-valid result rather than
"a fabricated absence." C# has none. **Two halves of one fix; they must land together** — a longer
deadline without the recovery window changes timing without fixing the evidence loss.

**K-09 also carries:** a documented invariant violated (C# tags `probe.result = "no_result"` while
reporting reason `"operation_returned_null"`, the exact drift Swift's comment guards); different outcome
for the same condition (`.unclassified` vs `Inconclusive`); token spelling divergence
(`operation_returned_nil` vs `_null`, splitting telemetry into two buckets, and no C# counterpart for
`late_but_valid`); and orphan teardown on the non-cancellable overload, which the C# comment acknowledges
as intended — flagged as a divergence for ruling, not a defect.

### 3.6 HIGH — security-relevant false negatives in shared logic (K-16, K-17, K-18)

Three cases where both platforms have the code and C# is deficient:

- **K-16 — CDN softening dismisses tampering.** With resolver and authoritative sets disjoint but inside
  one provider's ranges, C# concludes `Inconclusive` ("CDN/geo routing, not a rewrite") where Swift
  concludes `.diverges` and retains it because "an attacker may share the provider's ASN." Traced:
  `Diverges` emits an evidence item and a Positive Mirror subsection; `Inconclusive` falls to `default:`
  and **emits no evidence item at all**. *Note:* the adjacent C# evidence text is only true *because* of
  the softening, so the fix requires a coordinated `Synthesizer.cs` edit.
- **K-18 — typosquat detection misses transpositions.** C#'s edit distance is plain Levenshtein with two
  rolling rows, which structurally cannot carry the transposition term; its own doc hedges
  "Damerau-**ish**." Swift keeps a full matrix (B140). Caps and call sites identical, so it bites at the
  cap: `payapl.com` vs `paypal` is distance 2 in C# (no finding), 1 in Swift. Confusables tables are
  identical — the divergence is six lines.
- **K-17 — underscore names silently dropped.** Swift allows `_` (RFC 8552; SRV, DKIM, DMARC, mDNS) and
  ★ *claims Windows parity that does not exist*. C# rejects `_`, so `_dmarc.example.com`,
  `_sip._udp.…`, `_services._dns-sd._udp.local` return null and the caller **drops the record entirely** —
  vanishing from every count including exfil detection. Aggravating: Swift *discloses* excluded names via
  a counter; C# has neither counter nor disclosure.

*Consolidation note:* this is a **fourth** hand-written domain normalizer alongside `DomainCanonical`,
`DNSResolver.ValidateDomainName` and `DNSSECVerifier.normalizeDomainName` — four character-set rules over
one concept, already drifted. Consolidate as a follow-on, not inside these seats.

### 3.7 HIGH — encrypted DNS: the path exists and is defective (K-03…K-06, P-05)

★ `EncryptedDNSConfigurator.cs` **does** exist with a Linux `resolvectl` branch wired to the Linux GUI.

- **K-06 / P-05b — a DoT configuration is confirmed by a DoH probe.** On a network permitting 443 but
  blocking 853 — exactly the filtering posture Syxx exists to detect — `resolvectl` reports DoT configured
  and the DoH probe succeeds, so Syxx reports **Enabled** over failing DNS. Immediate honest fix is one
  line: pass `encryptedProbeCompleted: false` on the Linux path so the result is `Inconclusive`.
  *Shared defect:* macOS synthesises a DoH URL for its DoT case too — but it bites harder on Linux because
  the `active` half is fed by the broken parser of §3.3.
- **K-05 / P-05d — Linux can enable and cannot disable.** No `DisableAsync`, no `Disabled` result member,
  so the captive-portal failsafe the file itself calls non-negotiable is unimplementable. See §3.10 for
  the fabrication this produces.
- **K-04** — catalog is 3 entries vs 11, and C#'s non-nullable `DohUrl`/`DotHost` cannot represent the 4
  entries that legitimately have neither. A macOS-documented resolver id is a hard error on Linux.
- **K-03** — DNSCrypt / Anonymized-DNSCrypt entirely absent from C#. The one transport that hides the
  client IP from the resolver is unavailable on the platform where `dnscrypt-proxy` is a first-class
  package. `classifyStart` and `generateToml` are pure and are the whole honesty surface.
- **P-05a** — `resolvectl dns ""` has no documented global form; needs a real box.
- **P-05e** — `resolvectl` state is runtime-only: lost on reboot, on link change, overwritten by
  NetworkManager. The right mechanism is a persistent `/etc/systemd/resolved.conf.d/50-syxx.conf`
  drop-in, which also **supplies the missing revert**.

### 3.8 HIGH — privilege: Linux has no privileged-execution layer (P-01, P-02)

macOS has `PrivilegeManager` (tri-state `granted/denied/unavailable`, never caches a refusal) plus an
`SMJobBless` XPC helper, version-compared and liveness-pinged. Linux has `geteuid()==0` and printed
instructions to re-run under sudo. The Cage pane is a 10-line code-behind with no view model and a static
instruction card whose own comment says graphical elevation is "still to be wired."

★ **P-02 reframes it:** `pkexec` is *already load-bearing* — `UpdateService.cs:409` shells
`pkexec dpkg -i` to install updates. Graphical escalation is not missing, just unused for capture. Two
gaps: no polkit action file is shipped, and `policykit-1` is not in `Depends:`. **Recommend polkit over
`setcap`** — `setcap cap_net_raw+ep` grants raw sockets to every local user permanently, a worse trade for
a forensics tool than a per-capture prompt. Precedent: `syxx-capture-hardware-id.sh` already rejected
setuid and root-daemon for the same class of reasoning.

**And §3.1 makes P-01 fatal rather than incomplete:** the GUI cannot capture, tells the user to run a CLI
command, and that command is not installed.

### 3.9 HIGH — the GUI keystone and what depends on it (D1-00 and family)

**D1-00 — Linux has no shared application state.** Every pane constructs its own view model in its own
code-behind (eight files at `:12`; `CageView.axaml.cs` sets no DataContext at all). macOS has one
`AppModel` as `@EnvironmentObject`; Windows one `AppViewModel`. **Nothing can see anything else.** One
`Domain` *per pane*, inverting macOS's E108 fix. `isPro` hardcoded. No reset.

**Its most expensive consequence — D1-13: Linux's Conclusions pane cannot see any warden's output.**
`ConclusionsViewModel.ScanAsync` calls `FirstRunScan.RunAsync` and nothing else; it never reads
`SectionInputs`. So a user who runs all six wardens and opens Conclusions gets a grade computed from a
**separate, freshly-run scan** — the result surface and the evidence surfaces are two disconnected
products sharing a window. Corroborated from the other side in-tree: `WitnessViewModel.cs:88-93` computes
drift and documents that it is deliberately unrendered because "the Linux client has no drift-consuming
Conclusions surface yet."

**★ D1-12 — and WINDOWS, not macOS, is the reference for that fix.**
`Windows/dotNET/SyxxUI/Views/ConclusionsView.xaml` already carries all three halves: per-warden evidence
cards (`ProphetCard:159`, `MirrorCard:175`, `WarpCard:191`, `PortalCard:207`, `GluttonCard:223`), the
one-shot scan (`ScanBtn:132`), and a reset (`ResetBtn:110`). ★ macOS has **no one-shot scan at all**
(0 hits for "scan" in `ConclusionsView.swift`). Linux kept only the one-shot half. **The half Linux is
missing already exists in C#, in the sibling project Linux's own csproj references.**

> **Seat brief line, verbatim:** port the evidence half from
> `Windows/dotNET/SyxxUI/Views/ConclusionsView.xaml`; consult macOS **only** for the module-status gate
> (`ConclusionsView.swift:133-140,:198`) and the QUALIFIED pill (`:309-316`).

**Fix for D1-00:** a `SessionModel` carrying `SectionInputs`+`Merge`, one `Domain`/`Server`, `IsPro` +
`RefreshEntitlementAsync`, `Conclusion?`, `DashboardLog`, `ResetAll()` — exposed as `SessionModel.Current`
so reserved panes need not be touched. **Cost stated plainly: per-pane domain closes for 4 of 6 wardens,
not 6.** Prophet and Mirror are being rewritten wholesale, so a contract note folds into that rewrite. The
narrow two-file lift that would close all six is declined by default, overridable.

**D1-37 — no pane invalidates its result when the domain changes.** No `OnDomainChanged` in Mirror,
Witness or Portal. Run Mirror on `github.com`, get green MATCHES, type `example.com` — the green line stays
beside a domain it was never measured against. macOS shares the shape but has **one** domain; on Linux,
**six independently-stale results can coexist against six different domains.** Fix belongs in
`SessionModel.Domain`'s setter — only possible once the keystone lands.

### 3.10 HIGH — fabricated and inert UI (D1-01…D1-08, D1-26, D1-28, D1-34)

**Inert affordances — a gap class, not a bug.** On a product whose value is that its output can be
trusted, a control that silently does nothing is the same failure mode as a fabricated value: the user
believes an action occurred.
- ★ `DashboardView.axaml` — `grep -c 'Command|Click'` returns **0**. All five primary actions inert.
- ★ `MainWindow.axaml` — **0**. The "Start your story anew" reset pill is a bare `<Border>`. macOS's is a
  real button behind a confirmation dialog offering "Erase session results," because destructive actions
  must confirm. Linux has the pill, no command, no confirmation, no `ResetAll()`.
- Dashboard warden tiles do not navigate.
- ★ **And this class is not Linux-only — D1-33:** macOS declares `@State localDarkMode`, passes it into
  `WardenSidebar`, declares the `@Binding` — and never reads it in the sidebar body (verified: the only
  consumer is `.preferredColorScheme` inside `ContentView`). A binding threaded in for a toggle never
  built. **A parity pass that only moves fixes one direction will not find it.**

`flushDNSCache` has **no C# twin at all**; `witnessFree` exists on macOS and Windows and nowhere on Linux.

**Fabricated and stale claims:**
- ★ **D1-07:** the Dashboard hardcodes **"Disabled — traffic already encrypted (VPN)"** and the dimming
  with it. macOS drives both from `model.isEncrypted` with a four-way clause whose `nil` arm is **"not yet
  checked."** — the arm Linux most needs and entirely lacks.
- ★ **D1-02:** every warden pill is a literal. "NOT RUN" after a run, "LOCKED · PRO" to a paying
  subscriber, forever. ★ No C# twin of `DashboardWardenState` exists in **either** client (macOS has 77
  lines) — a spine #6 gap.
- **D1-26:** the sidebar PRO lock pills are hardcoded for Mirror, Glutton and Witness, so a Pro subscriber
  sees locks forever in the most persistent chrome in the app.
- **D1-03:** the trust ring and "N of 6 checks have run" can never change — four of five
  `DashboardViewModel` properties are declared and never assigned.
- ★ **D1-08:** the Dashboard advertises **"$2 / moon"** against `LicenseView.axaml:29`'s actual
  **"$3.95 / month"**.
- **D1-06:** Linux still carries "THE COUNCIL'S CONCLUSION" and "THE CHRONICLE," removed on macOS by owner
  directive 2026-07-23 as result-shaped cards the Dashboard should not carry.

**D1-34 — Settings fabricates a system-state claim, and Linux is the platform that added it.** When the
toggle goes false, `OnEncryptEnabledChanged` constructs `new EncryptedDNSApplyResult(Failed, "disabled")`
**locally — no configurator call, no probe, no read-back** — and renders **"Encryption is off."** Worse,
`Persist()` runs *before* the apply, so the stored preference and the live resolver config disagree
**persistently**. macOS is correct (real `disable()`, persist-after-confirmation, revert on failure, D097).
Windows is incomplete but does not lie (no disable API, no `turningOn` branch at all). Its adjacent
comment claims *"character-for-character parity with Windows"* — true for two arms, **false for the arm
where the fabrication lives**. **D097 is fixed only on macOS**; Linux and Windows both persist before
confirming and neither reverts. *Do not over-correct: the Linux **enable** path is honest and good.*

### 3.11 HIGH — Portal advertises a capability it does not have (D1-09…D1-11)

★ `grep` for traceroute/`TraceState` over the Linux GUI returns **0**, while the pane's eyebrow reads
"PORTAL • LOCAL NETWORK **& ROUTE**" and its lore promises "tracing the path of a hidden interceptor."
macOS has a five-state machine whose `.failed` arm is **amber** and says **"This is not evidence of a
clean path."** — that sentence is the honesty content of the whole panel. `TracerouteRunner.cs` already
exists with a working Linux branch. Also: no Portal domain input, and no LAN-devices panel — macOS at
least ships an honest placeholder disclosing that discovery is CLI-only.

### 3.12 HIGH — transport observation, the question users actually ask (K-01, K-02)

**K-01** — the OBSERVED tier has no producer or consumer in C#; `DNSTransportState.cs` is dead code (zero
hits for its types outside the defining file). Linux can answer *configured* and *reachable* and never
*"are my queries actually leaving encrypted?"* Per the C# file's own comment those cannot substitute: a
machine configured for DoT that silently falls back to cleartext reads as configured-and-reachable.

**K-02 must land first.** C#'s `MonotonicClock` uses `Stopwatch.GetTimestamp()` = `CLOCK_MONOTONIC`, which
does not advance across suspend, where Swift uses `CLOCK_MONOTONIC_RAW` and documents that it must. Close
a laptop lid, reopen hours later, and pre-suspend transport state is served as current — the exact
staleness-into-a-claim failure the type exists to prevent. Fix without P/Invoke: require **both**
monotonic and wall-clock age within the validity window, treating a negative wall delta as expired.

*Consolidation:* `parse`/`sample` are a third hand-written text parser and will not hold parity with
nettop parsing. Keep `activeFlows`/`classify`/port constants shared and fixture-tested; make
`parse`/`sample` explicitly per-platform. **Assert parity on `classify`, never on `parse`.**

### 3.13 HIGH in aggregate — shell, tray and chrome (D1-24…D1-32)

★ **D1-24 — no menu bar, no tray icon, no keyboard shortcut anywhere.** `grep
TrayIcon|NativeMenu|KeyGesture|HotKey|KeyBinding` over the Linux GUI: **0 hits**. Avalonia supports both
`NativeMenu` and `TrayIcon` — a build gap, not a framework limit. macOS has About/Settings ⌘,/License/
Check-for-Updates ⌘U/Help ⌘?, plus number accelerators 1–8 on every sidebar row.

**D1-25 — Linux is the only client with no trust indicator outside the window, and the logic already
compiles into its binary.** macOS's `MenuBarExtra` severity dot comes from `TrayIndicators.For(...)` —
*"the SAME mapping the grade and result badge use, so the three can never disagree — and `.unknown` is
grey, never the green of a measured-clean network."* Its dropdown carries a **latched** monitor change with
Acknowledge (*"so the user cannot miss 'your DNS changed' by not having the window open when it
happened"*), a watch toggle, and B247's *"⚠︎ Notifications denied — monitoring cannot alert you"* under
the rule *"never present monitoring as armed when it cannot alert."* `Syxx.Kit/Synthesis/TrayIndicator.cs`
exists and Linux takes `Syxx.Kit` by `ProjectReference`, so **it already compiles into the Linux binary and
is simply never called.** Linux therefore has no surface that could ever say monitoring is armed but
cannot alert, and no latched channel for a change detected while the window was closed.

**D1-27** — nav divergence, one item provably unfinished: the gold "Dimensions" external row exists on
macOS and is absent on Linux, and `NavAspectConverter.cs:41` defines a `GoldGlyph` that **nothing ever
returns**, with a header comment describing "gold for Dimensions." Written to serve a row never added.
Also "Conclusion"/`✒` vs "Conclusions"/`⟐`, and no per-warden selected-row accent.

**D1-29** — Linux panes render flush to the window edge; macOS defines one shell-owned origin with the
invariant *"the shell owns the pane origin; child panes must not add another outer inset."* One-line fix on
the shell. Related: Linux scrolls once at the shell, so scroll position is the one piece of state the
held-view design does not preserve. **D1-30** — a broadcast arriving mid-session is never surfaced; macOS
floats it from any pane, Linux references broadcasts only in the launch-destination pane. **D1-32** —
startup typography: macOS escalates a tri-state and traps a debug build if no face registered, because a
font failing to register "renders as the system font with no error anywhere … a silent-fallback defect
that survived a full release cycle." Linux has a test-time guard but **no runtime check** while addressing
faces by `avares://` URI whose failure mode is silent fallback — and §3.1 makes packaging regressions a
live risk here specifically. **D1-31** — the EULA cannot be re-read after acceptance on Linux; **About** is
absent from *both* C# clients.

### 3.14 MEDIUM-HIGH and MEDIUM — dropped signals, attribution, and coverage

- **D1-14** — `ResultAction.Qualified`, documented as "true when confidence is limited or a required check
  did not run" and set on four of five arms, is rendered by macOS as a **QUALIFIED** pill whose comment
  reads *"the difference between 'you are clean' and 'what we could see looked clean'."* ★ `grep Qualified`
  over the Linux GUI: **0**. The flag is already in the record the VM holds.
- **D1-15** — `ConfidenceText` is assigned the *state* enum; `CompletenessText` is assigned `""` — a
  labelled field that always renders blank.
- **D1-16** — active and suppressed diagnoses share one unlabelled orange list, distinguished only by
  parentheses. A suppressed finding reads as an active one.
- **D1-38** — no Linux result names what produced it. macOS attributes six evidence rows by warden and
  renders *"…for `<domain>` via `<resolver>`"* with an honest "resolver not determined" fallback. Linux
  says "the local resolver" and names neither, despite having the IP in scope moments earlier.
- **D1-21** — Linux's Witness `Valid` copy regresses macOS's B053 split, which deliberately separated
  signature integrity from network result: *"'Signature matches' is an Ed25519 fact; 'no tampering' was a
  false-clean NETWORK claim."* Linux says "the seal is valid **and the testimony is intact**."
- **D1-22** — Linux verifies a pasted testimony without displaying its domain, timestamp or signer. A
  conclusion about evidence the user is not shown.
- **D1-23** — no `Generate Keypair` on Linux, so **there is no way to see your public identity before you
  sign**; and no `Free the Witness`.
- **D1-35** — `Presentation/SeverityColor.cs` is the **reference** severity mapping (five tones, `High`
  explicitly orange "(NOT red)", catch-all "neutral, never green," and a header naming "the
  green-on-inconclusive defect class this project has shipped before"). **But Witness and Mirror bypass
  it**, so the app ships **two different reds for the critical tier**. Cosmetic today; structurally it is
  the exact drift `SeverityColor` exists to prevent.
- **P-07** — `NetworkKeyState.cs:354` excludes LinkLocal; the Swift twin deliberately does not, citing
  RFC 8106 RDNSS and home routers advertising `fe80::` resolvers, because blanket exclusion "would drop
  the real system resolver and blind comparison/leak probes." The C# filter is Windows-shaped.
- **P-08** — genuinely missing transport files, of which `DNSPathConfirmation.swift` is the relevant one:
  it is precisely the "what the machine is SET UP to do vs what it ACTUALLY does" separation that would
  catch §3.3's stub problem.
- **C-12** — `sudo syxx cage` leaves root-owned files; macOS hands back via fd-based `fchown` (never
  path-based, to avoid symlink following) with a uid-validation guard. **Port the capability, not the
  flags** — macOS's `--owner-uid/--owner-gid` exist only because its GUI escalates via XPC where
  `SUDO_UID` is unset. **C-13/C-14** — `--out` vs `--output`, and no `--interface` override, so on a
  multi-homed host or with a VPN up there is no way to choose the capture interface: precisely the
  split-tunnel case Syxx cares about. **Copy nit:** C#'s `cage` says "requires **administrator**" where
  Swift says "requires sudo."
- **K-19/K-20** — `validateDomainName` accepts a leading dot on macOS and rejects it on C#;
  `DomainCanonical` loses the rejection reason on C# (bare null vs a typed error with descriptions).
- **T-01/T-02** — `ConditionState` is the one tri-state with **no JSON converter** on C# (latent: not on
  the signed wire, but `Trust.cs:215` holds one that `ContinuousMonitor.cs:112` compares across samples).
  And `SectionStatusEntry` emits `datapointsObserved`/`datapointsExpected` which ★ appear **nowhere in the
  entire macOS tree**; Swift silently drops them and has **no active test coverage** for that type — it
  appears only under `macOS/Tests/_quarantine/`. **K-07** — `AuthComparisonStatus` member order differs
  (`Matches`/`Diverges` swapped) with no wire converter. Latent; fix both before anything serializes them.
- **P-20** — eight macOS suites have no Linux equivalent, notably `CLIExitCodeProcessTests.swift`: the
  shared CLI *is* the Linux CLI, so nothing pins its exit codes anywhere.
- **D1-39** — house rule 2 is broadly unmet: **34 catalogued rows across 8 Linux files** against 800 and
  812 totals, with **17 files uncatalogued** — including the ones holding the real price, the fabricated
  encryption string, and the nav label drift. **So several defects in this document sit in files the owner
  cannot see from the catalogs.**

### 3.15 TEST FIRST, do not fix first — C-09

`CaptureEngine.cs:323-324` justifies the non-Windows path on "SOCK_RAW + **IPPROTO_UDP** already receives
all UDP packets." The call sites pass `ProtocolType.IP` (0) and `IPv6` (41); ★ `grep ProtocolType.Udp`
across the Kit: **0**, so the stated precondition is never met. No socket option is applied off Windows
(both `IP_HDRINCL` and `SIO_RCVALL` are Windows-gated), the socket binds a single unicast address, and no
`AF_PACKET`/`libpcap` path exists anywhere. **Expected: little or nothing captured, reported as a
successful session** — the exact defect class this repo keeps legislating against. Confirming needs root on
Linux. If confirmed, the remedy is a capture mechanism, not a flag, **which changes the plan's shape**.

---

## 4. The register — macOS behind Linux (adopt from Linux)

**A parity plan that only moves one direction will import defects.**

**M-01 — macOS claims "no signing key" when the Keychain is merely locked, then instructs the user into
overwriting it. HIGH, destructive, GUI-layer only, two lines.** `WitnessKeyManager.swift:38-44` maps
`.undetermined → false` in `hasKey`, and its own doc says *"callers that need to distinguish 'no key' from
'could not ask' should use `checkKeyPresence()` instead"* — the honest tri-state was built (D084) and the UI
was told to use it. `AppModel.witnessVerify()` calls `hasKey` anyway and sets `.noKey`, rendered as *"no
signing key on this machine. **Enter your key above, then verify again.**"* If the user complies,
`saveWitnessKey` calls `saveKey` with **no presence check** — it overwrites, and every previously sealed
testimony stops verifying. Verbatim the failure Linux closed as RG-2. **Switch on `checkKeyPresence()` and
add an `.undetermined` state. The Kit needs no change.** Single most valuable Linux → macOS adoption found.

**M-02 — macOS shows a result with no coverage statement.** Its module-status grid lives only on the
*pre-result* branch; once a conclusion exists nothing says which wardens did not run, and the QUALIFIED
pill is one bit. **Linux is correct:** a persistent COVERAGE card with "Checks that ran" **and "Checks
that did not run"** in orange. Linux also has a null-determination gate macOS lacks: *"That is a gap in the
evidence, not a clean result."*

**M-03 — Linux is the reference on Witness lifecycle and error handling.** Explicit
`WitnessTestimonyState` with derived `IsRunning`/`HasResult`, where `HasResult` is true for `Failed` so a
failure never falls back to the never-ran appearance — and the file records Linux as the **origin** shape,
with macOS and Windows retrofitted (B195). `ErrorPresenter` on all four failure branches. A resolver-absence
guard that refuses to fabricate. Validate-before-persist on import. Never-ran correctly amber, not red.
**Nobody should "fix" Linux toward macOS here.**

**M-04 (= G-15) — foreign-snapshot contamination. HIGH.** Windows and macOS let an external paste
overwrite the field self-verify reads, then render a composite that never existed: macOS shows *your*
domain with a **foreign** signature; Windows *your* signature with a **foreign** resolver, against its own
comment saying "never fabricate a domain or resolver." **Linux is immune** — `SelfVerify` reads a private
snapshot only a real seal writes. Fix: give the external path its own field on both platforms.

**M-05 — Linux is ahead on encrypted DNS.** ★ macOS states that `NEDNSSettingsManager` requires an
Apple-granted entitlement the current SwiftPM build lacks, so **macOS cannot complete a save today** and
returns `.entitlementRequired`. The Linux `resolvectl` path needs no vendor grant. **Do not frame this axis
as Linux catching up.**

**M-06 (= T-04) — Stage5: C# is ahead and Swift never finished the migration.** `ConclusionsView.xaml.cs:311`
still has a `RenderStage5`, **repointed** to take `DeterminativeResult?`, with a note saying it "previously
consumed `Stage5Presentation`." Swift still carries a live 533-line `Stage5Presentation` with a consumer and
two active tests. **Swift should finish MATRIX-MIGRATION Phase 3.3 and delete it.** Third case where C# is
the correct side, after **C-15** (`syxx -v` works on Linux, unrecognised on macOS) and **C-16** (the
`S`/`SCAN` menu key). Also **G-17**: macOS `hasKey` collapsing `.undetermined → false` is the same defect
the .NET side fixed in G-02a. And **M-06 smaller:** Linux's synthesis error path (*"A crashed scan is an
unmeasured network, not a clean one"*) where macOS calls `conclude` bare; Linux's `LinuxSecretStoreTests`
and `TypographyAssetsTests` have no macOS twins; Linux is the only platform testing the Witness severity
tier at all.

**Cross-dimension handoff:** `Stage5Copy.swift:9` declares a byte-for-byte copy-parity contract with
`Windows/SyxxUI/Presentation/Stage5Copy.cs`, ★ **which does not exist**.

### 4a. Windows behind both — G-09, still open and still the highest-impact single defect found

★ **G-09 — Windows discards a tampering conclusion behind "CONTENT WARP REQUIRES PRO."** Fires on **every
Pro Windows run**. `ProbePlan.cs:106,144` populate only `inputs.WarpManifest`; `inputs.Warp` is assigned by
nothing and `ContentWarp.cs:21-24` calls it a "compile shim." But
`SyxxUI/Views/MirrorView.xaml.cs:183` reads `SectionInputs.Warp`, so `:214-219` falls through to
`MirrorView.xaml:133-140` "CONTENT WARP REQUIRES PRO" — shown to a paying user whose run **completed**,
**discarding a `TAMPERING_DETECTED` conclusion without trace.** `ConclusionsView.xaml.cs:128-137` repeats
the stale read, so the Conclusions Warp card never appears either. The model layer is already correct
(`AppViewModel.cs:529`), so this is strictly the view surface. macOS was fixed in the same wave; Windows
was not.

**Fix:** in `RefreshWarp`, read `SectionInputs.WarpManifest` and switch on `WarpConclusion`, reusing the
mapping macOS ships at `MirrorView.swift:404-429` — VERIFIED_MATCH green, INCONCLUSIVE **amber never
green**, TAMPERING_DETECTED red. Reserve `WarpProGate` for `!IsPro` only, and add a distinct "did not
complete" state for Pro-with-null-manifest. Same migration at `ConclusionsView.xaml.cs:128`.

**Routing, and this is decision 14.** `MirrorView.xaml{,.cs}` is reserved by the Mirror stream, which is
about to rewrite them wholesale, so the cleanest path is handing G-09 to that stream as a **rewrite
requirement**. But `ConclusionsView.xaml.cs` is on **no** reserved list, so that half can be split out for
partial credit now. Recommended: both. **Until one is chosen, a completed tampering finding keeps being
suppressed on Windows** — and note this cannot be verified from a macOS host (§10.4).

*Corrects a memory claim:* "both GUIs' dead Warp panel wiring fixed" is true only at the model layer and
for the macOS view.

---

## 5. Verified clean, falsified, and non-gaps — do not re-litigate

**Falsified hypotheses, recorded because a falsification is a result:**
- **G-06** — nothing non-positive is ever rendered green, on any platform. All nine
  `SnapshotVerification` members map to distinct messages in each GUI switch; Windows and macOS wording is
  verbatim-identical to each other. **Caveat that does need work:** three collapses exist *inside* the
  non-green tiers — macOS's `runWitness()` carries a **second, divergent mapping** demoting five of six
  critical states to the caution glyph and calling `verifySnapshot` rather than `verifySnapshotDetailed`;
  Windows' default arm reuses the MalformedInput wording verbatim; and empty paste is rendered **red
  "malformed"** on Windows/macOS where Linux gives an amber prompt — **and both non-Linux suites pin that
  collapse as correct**, so fixing it means amending tests. Also a state-space divergence: **Linux has no
  no-local-key state at all**, a consequence of the auto-mint.
- **G-07** — Linux builds and is gated. `dotnet build` succeeds; the Avalonia suite is **165/165**
  including in CI's exact `-c Release` form; all Kit symbols resolve; AXAML compiles with CompiledBindings
  so a missing command member is an AVLN error, not runtime-silent; CI gates Linux twice.
- **G-08** — BYOK accept/reject sets are identical across platforms. Every save path routes through the
  platform's single validator, shared with the signer; the decoders' whitespace quirks agree and are
  test-pinned on both sides.
- **Q-01 resolved** — an auto-minted identity **cannot** fail its own trust set on a default machine.
  `WitnessSigner.cs:345` guards on `Count > 0` and is **fail-open** (*"Empty/unconfigured set = no
  restriction"*), and no production code on any platform writes the trust set — only macOS test setup.
  When a set **is** configured it fires exactly as feared: red `UntrustedSigner` directly under "Sealed."
  **Severity is inherited from G-01/G-02, so fixing those closes the common path in.** Standing design
  concern: "the signer is not in your trust set" is a **trust-posture statement, not tamper evidence**, yet
  it renders in the same critical-red tier as `InvalidSignature`. That reads as an accusation.
- **Kit tri-state core is in genuine parity** and is the best-maintained axis surveyed. `TriState`,
  `SectionStatus` and `EvidenceUsability` verified member by member including wire strings —
  `EvidenceUsability`'s doc comments are **verbatim identical line for line**, and C# adds a `RawValue()`
  helper annotated "the exact Swift rawValue string … parity-relevant." The `completeness` [0,1] clamp is
  on both sides. **Do not open these.**

**Non-gaps — recorded so nobody chases them:** no autostart/login-item/service on **any** platform (do not
invent a systemd unit for parity); Linux fonts are complete (nine families, OFL, embedded, with a
compensating test — the only asymmetry is that macOS *asserts* every declared face at bundle time);
Linux's **update surface is real and complete** inside Settings (macOS reaches it by ⌘U — justified idiom);
`MotdLoadController` exists on all three platforms, so What's New load-state policy is genuine parity
despite different renderers; `CageRedundancyCorrelator` and `ProbeRunner.runTyped`/`ProbeOutcome` are dead
on the **Swift** side; `runGlutton`'s `licenseKey:` is a symmetric dead parameter; `PktapHeader.swift` and
`PrivilegedHelperProtocol.swift` are justified Darwin idioms; C# multi-type file consolidation under
`Trust/`, `Forensics/`, `LAN/` is layout only — **do not "fix"**; `ProbeBudget` as class-vs-struct is a
justified language idiom; the C#/Swift `RunAsync` overload split likewise; all 15 CLI subcommands dispatch
1:1 with identical tokens and 9 of 14 flag surfaces are identical; the `cage --duration` 86400s ceiling and
10.0s default genuinely match; and the exit-code contract is shared, its undocumented-in-`--help` TODO
unresolved on **both** platforms — a shared gap, not a parity gap.

**Shared latent issue, equally wrong on both sides:** both `ToAsciiName`/`toASCIIName` take a pure-ASCII
fast path returning the input unvalidated and un-lowercased, so a malformed `xn--` A-label in an otherwise
ASCII name passes through on both platforms.

---

## 6. Landmines — read before touching these files

**★ T-03 — the tests are stale and the production code is right.** Verified by execution:
`dotnet test --filter "~TriState|~MatrixUntested|~MatrixAllNegative|~ResultProvenance"` → **21 passed, 2
failed**. Both failures are boolean-casing assertions in `ResultProvenanceTests` expecting `"False"` where
production emits `"false"`. `Synthesizer.cs:1001` emits lowercase and the **Swift twin test asserts
lowercase**. The wire casing was aligned to Swift in production; two C# assertions were left expecting
.NET `bool.ToString()` PascalCase. **Fix is two characters in each test.**

> **A seat that sees a red test and "fixes the code to match the test" would silently break wire parity
> with Swift on a degradation-provenance signal — spine #3's own telemetry. The brief covering
> `Synthesizer.cs` must state the direction explicitly.** These sit inside the ~12 known-baseline failures
> but are **not noise**: they are the residue of a completed parity fix, and treating them as baseline is
> how the landmine stays armed.

**The Mirror porting trap.** The obvious-looking legacy Warp result field is **retired and never
populated**; a port that reads it gets a permanently empty column. The live one is the manifest result.

**Two comment traps:** `CageView.axaml:15-16` claims the pane points at the CLI *"exactly as the macOS and
Windows clients do"* — ★ **macOS does not**, it has a working in-app capture button. And
`SettingsViewModel.cs:94` claims character-for-character parity with Windows, false for the arm that
fabricates. With §2.5's two false Swift-side claims, that is **five self-contradicting doc comments**
found across both passes.

---

## 7. Build plan — waves, seats, ownership

**Every file has exactly one writer.** A seat that needs another's file sends a request; it does not edit.
**Every seat brief carries three standing lines:** §2.5 (comment claims are not evidence), §6 (the T-03
direction), and this one —

> **D1-36 — Linux's result-rendering honesty is already correct.** `HasResult` gating is in place on Conclusions,
> Witness and Mirror; every view model sets it only after a real outcome and separates never-ran from
> failed; `MirrorViewModel` carries five states with only `MATCHES` green and refuses to fabricate a
> resolver; `SeverityColor.cs` is the reference mapping. **The implementer risk is REGRESSION, not
> absence.** You are bolting commands, navigation and shared state onto panes whose honesty logic is
> already right.

### Wave 0 — prerequisites

| # | Task | Why |
|---|---|---|
| **W0-a** | **Fix C-10 and C-11 now**, ahead of the plan | One file, both irreversible-harm, no dependencies. A user running `--gen-keypair` twice today destroys their identity; a user following the documented `--key` form exposes their key to every local account. |
| **W0-b** | **Fix M-01 now** | Two lines, destructive, macOS. |
| **W0-c** | **Unblock the Swift build** — claim or fix the untracked `macOS/Sources/SyxxKit/DNSPathConfirmation.swift:90` (`DNSPathSnapshot` does not conform to `Equatable`) | ★ `SyxxKit` does not compile in-tree, so **no Swift test runs at any scope** and every twin claim would be *asserted, not verified*. Another stream's file — claim, do not seize. |
| **W0-d** | Interface stubs land alone, as their own commits: `SessionModel` and the `NetworkKeyState` seam | Dependents cannot start against a moving contract. |
| **W0-e** | Request the resolver verification pack (§9) | Unblocks §3.3 for under a minute of owner time. |
| **W0-f** | **Test C-09** on a rooted Linux host | If capture returns nothing, the remedy is a new mechanism and the plan's shape changes. |

### Wave 1 — parallel, no shared files

| Seat | Owns | Carries |
|---|---|---|
| **E1 resolver-truth** | `NetworkKeyState.cs`, `ConfigAndPolicyTests.cs`, `corpus/resolvectl-status/**` (new) | P-06, P-22, P-07. **Sole writer.** Highest priority — nothing downstream of resolver discovery is trustworthy until it is fixed. |
| **E3 delivery+privilege** | `build_deb.sh`, `Linux/Ubuntu/debian/**` (new), `Linux/Ubuntu/polkit/app.syxx.policy` (new), `snapcraft.yaml`, `.github/workflows/ci.yml` | P-02(ship), P-03(wire), P-09…P-13, P-17…P-19. **Largest single win: P-09 + P-17.** Zero dependencies — start immediately. |
| **E4 secret-store** | `LinuxSecretStore.cs`, `LinuxSecretStoreTests.cs`, + `DicePassphrase.{cs,swift}` (new, §8) | P-04, P-04b, the G-02b residual, and the passphrase generator + Argon2id + per-store salt. |
| **E5 transport-observed** | `DNSTransportState.cs`, `FlowTransportObserver.cs` (new), `DNSTransportMonitor.cs` (new), + 3 new tests | **K-02 then K-01, hard order.** Needs a *lease* on one `Synthesizer.cs` field, not the file. |
| **E6 twin-classification** | `AuthoritativeVantage.{cs,swift}`, `LookalikeDetector.{cs,swift}`, `Forensics/DNSLogAnalyzer.{cs,swift}`, `ForensicsAnalyzerTests.cs` | K-16, K-17, K-18, K-07. Needs the `Synthesizer.cs` lease for K-16. |
| **E7 twin-timing** | `ProbeBudget.{cs,swift}`, `ProbeRunner.{cs,swift}`, `DNSResolver.{cs,swift}` (surgical), `DomainCanonical.{cs,swift}` | **K-15 + K-09 together** (two halves of C058), K-19, K-20. |
| **E8 tri-state** | `ResultProvenanceTests.cs` (T-03, two characters — **ship first**, it turns the suite greener at near-zero risk); `NetworkKeyState.cs` → **merge with E1** for T-01's one attribute | T-01, T-03. **Blocked on decision 9 before touching `Conclusion.cs`.** `TriState.*` and `MatrixDeterminativeEngine.*` **dropped — verified clean.** |
| **E9 macOS-adopts** | `macOS/Sources/SyxxApp/AppModel.swift`, `WitnessView.swift`, `ConclusionsView.swift` | M-01 (also W0-b), M-02, D1-17, D1-18, D1-33, M-06's smaller items. |

### Wave 2 — after W0-d and Wave 1's contended seats

| Seat | Owns | Carries |
|---|---|---|
| **E2 encrypted-DNS** | `EncryptedDNSConfigurator.cs`, `EncryptedDNSConfig.cs`, `EncryptedTransportProbe.cs`, `SyxxSettings.cs`, `DNSCryptProxyConfig.cs` (new), `DNSCryptProxyManager.cs` (new), + 2 new tests | **Merged seat — do not run a parallel D2 copy.** Blocked by E1. Internal order **K-06 → K-04 → K-05 → K-03**: K-06 first because it is the only one that makes *shipped* behaviour lie; K-04 before K-03 because the nullable-catalog reshape is what makes DNSCrypt entries representable. |
| **E-keystone** | `SessionModel.cs` (new), `ShellViewModel.cs`, `MainWindow.axaml{,.cs}`, `App.axaml.cs`, both csproj files (**sole writer**), `SessionModelTests.cs` (new) | D1-00, D1-28 (owns `ResetAll()`), D1-37's invalidation. **Stage A = interface stub alone.** |
| **E10 dashboard** | `DashboardView.axaml{,.cs}`, `DashboardViewModel.cs`, `Windows/dotNET/SyxxUI/Presentation/DashboardWardenState.cs` (new), `DashboardViewModelTests.cs` (new) | D1-01…D1-08. Warden-state helper goes in the **Windows** presentation folder and is **linked** into Linux — authoring it Linux-side means writing it twice and re-opening spine #6. |
| **E11 portal** | `PortalView.axaml{,.cs}`, `PortalViewModel.cs`, `PortalViewModelTests.cs` (new) | D1-09…D1-11. Reads `TracerouteRunner.cs`, does not own it. |
| **E12 conclusions** | `ConclusionsView.axaml{,.cs}`, `ConclusionsViewModel.cs`, `ConclusionsViewModelTests.cs` (new) | **D1-12…D1-16.** Reference is the **Windows XAML** — see §3.9. |
| **E13 panels+copy** | Onboarding/WhatsNew/License/Settings views + VMs, `Services/*`, new panel files, **both catalogs as sole writer** | D1-31, D1-34's GUI half, decision 10. **Catalog rule:** each seat stages rows in its own scratchpad file; E13 performs one append per catalog at the end. Append-only prevents reordering but not a git conflict at a shared tail, and the Mirror stream appends concurrently. |
| **E14 CLI** | `Syxx/Program.cs`, all 16 `Commands/*.cs`, **`Syxx.Kit/Capture/CaptureEngine.cs`** | C-10…C-16. **C-10 and C-11 first** (also W0-a), ahead of flag work, **with a full re-diff of `SyxxWitness.swift` against `WitnessCommand.cs`** — the D086 precedent proves a partial port already happened once. |
| **E15 shell+tray** | `Converters/NavAspectConverter.cs`; **coordinate with E-keystone on `MainWindow`** | D1-24…D1-27, D1-29, D1-30, D1-32. `TrayIndicator.cs` already compiles in — no new reasoning code, only a caller. |
| **E-audit** | nothing — read-only | Continuous honesty audit per §10.1. |

### Not seats — routed out

**Cage capture wiring** touches `CageCommand.cs`, `Program.cs`, `CageView.axaml` — inside the hard Cage
keep-out, mid-transition under a paused agent. E3 ships the polkit action and dependency; **the call site
is a requirement handed to the Cage stream.** Note Cage is the only pane needing a DataContext *added*
rather than redirected when `SessionModel` lands. **Linux Prophet and Mirror** stay unmigrated —
`SessionModel.Current` exists so their reserved files need not be touched; both are slated for wholesale
rewrite, so the contract note folds in. **Linux Witness** is contended by live uncommitted work; no seat
owns it, and M-01/M-03/D1-21/D1-22/D1-23 route to that stream as advisory notes. **Mirror and Cage pane
internals** (§3.13 inventory) route to their streams.

---

## 8. Owner-directed feature: EFF dice passphrase for the witness key at rest

**Directed 2026-08-04.** Both clients generate a three-word passphrase from EFF short wordlist #1. This
answers decision 3 and is the right shape: it introduces the **user secret** the current KDF entirely
lacks, and generating it removes the weak-user-chosen-password failure mode. ★ Verified from source: the
list is **1,296 lines**, `NNNN<TAB>word`, `1111 acid` … `6666 zoom`, four d6 digits per word.

### Decide the word count before building: three words is 31 bits

log2(1296) = 10.34 bits/word, so **three words = 31.0 bits** (2,176,782,336 combinations). Under the
existing 350k-iteration PBKDF2 that is roughly **11,000 guesses/sec on one modern GPU** — the whole
keyspace in **a day or two on a single card**, hours on a small rented cluster.

**Why disqualifying here specifically:** the threat this closes (§3.4) is *a local user who already holds
the ciphertext*. They attack **offline**, with no rate limit and no lockout. Three words is defensible for
a throttled network login; not as the sole protection on an offline file holding a signing identity whose
compromise means forged testimony in the owner's name.

**No KDF choice rescues it.** Argon2id is worth doing regardless, but memory-hardness buys two to four
orders of magnitude where closing a 31-bit gap needs about ten. The entropy floor binds.

**Recommendation, keeping the owner's wordlist: six words from the same short list = 62 bits.** Same list,
same ergonomics — the short list exists precisely to keep long passphrases typable, and EFF's own guidance
recommends six. Four words (41 bits) plus Argon2id is a middle position; three plus Argon2id is still weak.
**Parameterise on a named constant defaulting to the owner's choice**, so this is a one-line change either
way — and **put the computed entropy in the UI beside the passphrase**, so the number is visible rather
than implied. The app states what the passphrase is worth instead of asserting it is strong.

### Design — three surfaces, not two

"Both" is read as both GUI clients; the **shared CLI is a third surface**, because on Linux the CLI holds
the key and is where `--key` lives.

1. **Wordlist ships embedded, never fetched.** Embedded resource per platform, **SHA-256 pinned**, with a
   test asserting exactly 1,296 entries and well-formed `1111`–`6666` indices. A network fetch for key
   material adds supply-chain and availability failures to a security primitive; the pinned hash makes a
   truncated resource a build failure rather than a silently smaller keyspace.
2. **CSPRNG with rejection sampling, never modulo.** `RandomNumberGenerator.GetInt32`;
   `SecRandomCopyBytes`/`SystemRandomNumberGenerator`. Modulo over a raw byte biases toward low indices —
   entropy loss on the one operation whose entire job is entropy. Test uniformity over all 1,296 indices.
3. **Offer real dice, and be honest about which happened.** `eff.org/dice` describes a *physical*
   procedure. Provide both "Generate for me" (CSPRNG) and "I rolled dice — enter the numbers" (four digits,
   validated 1–6), and label which path produced the result. Claiming the user rolled dice when a PRNG
   chose is the fabrication class this codebase legislates against.
4. **Never on argv, never logged, never in a breadcrumb.** Same rule as **C-11**: prompt, or
   `SYXX_WITNESS_PASSPHRASE`, or `--passphrase-file` — never a positional or `--passphrase` argument, since
   `/proc/<pid>/cmdline` is world-readable on Linux. Test that it never reaches `SyxxDiagnostics`.
5. **KDF replacement lands with the per-store salt.** Argon2id with a **per-store random salt** persisted
   beside the ciphertext, replacing §3.4's constant-salt machine-derived KDF. **Together** — a user secret
   over a constant salt still permits one precomputation against every install.
6. **Prompt cadence is a UX decision with a security consequence.** Recommend per-session with an idle
   timeout, the derived key zeroed on timeout, and the passphrase itself never retained.
7. **There is no recovery, and the UI must say so before generating.** A lost passphrase makes the key
   unrecoverable and **every previously sealed testimony unverifiable** — the same outcome as C-10 by a
   different route. Require an explicit "I have written this down" confirmation. Any escrow path weakens
   the property this feature creates; that is decision 15.
8. **macOS is additive, Linux is load-bearing.** The Keychain already gates the key on login; on Linux this
   *replaces* machine-derived material and is the only thing between a local attacker and a forged signing
   identity. Say that in the copy rather than implying equivalence.

**Ownership: E4**, which already owns the salt and KDF work — splitting the KDF from the secret feeding it
would put two writers in one contract. GUI entry points go to the Witness stream; **E9** takes macOS.
**Sequence:** decide word count → generator + Argon2id + per-store salt together → the three surfaces.

---

## 9. Owner decisions — consolidated, deduplicated

**Resolved since pass 1, recorded so they are not re-asked:** the GUI keep-out lift (§2.4 — never needed);
`SettingsViewModel.cs` lift (never reserved); Q-01 (§5 — fail-open, severity inherited); Stage5 direction
(§4 M-06 — C# is ahead). Pass 1's `teammateMode`, its §3 file-ownership map, and its Phase 1–4 sequence are
superseded by §7.

1. **Packaging and command surface** (blocks E3, gates P-09). Recommended: both packages ship both
   binaries, CLI as `syxx`, GUI as `syxx-gui`. That is what macOS does and it makes every existing
   "run `sudo syxx cage`" string true. Alternatives: per-channel instruction strings, or rename an
   assembly. Changes the shipped command surface.
2. **Linux privilege model** (blocks E3's policy file and the Cage routing). Recommended: pkexec-per-capture
   with a shipped polkit action — already in the tree, leaves no standing privilege. `setcap` rejected per
   §3.8.
3. **Linux at-rest posture — answered by §8, but the word count is still open.** E4 can harden but cannot
   close §3.4 without a user secret. **Three words (31 bits) or six (62)?**
4. **Encrypted-DNS mechanism + captive-portal failsafe** (blocks E2's persistence work). Confirm the
   persistent `resolved.conf.d` drop-in over runtime `resolvectl` — a Linux-native mechanism beating a
   literal port, and it supplies the missing revert. The portal auto-revert policy is explicitly yours per
   the file's own comment and is still absent.
5. **Node-lock on Linux.** Wire the hardware-id `postinst`, or accept non-node-lockable installs
   permanently? Snap cannot do the `postinst` either way, so this also decides whether snap and `.deb`
   have different licensing behaviour.
6. **The decoy CLI** (§2.1). Recommended: delete, not repair. A `git rm`.
7. **Dashboard price** (D1-08). Correct "$2 / moon" to $3.95, or delete the banner. macOS has no such
   banner.
8. **G-16 route.** (a) A real-Linux CI leg with `libsecret-1-0`, `libsecret-tools`, `gnome-keyring` under
   `dbus-run-session` — the only route satisfying spine #5; or (b) an interim keyType presence record
   beside the file store, testable today, which closes the lock/uninstall path completely but carries a
   bricking mode (a stale record ⇒ permanent `Undetermined`, no in-app recovery).
9. **Are `datapointsObserved`/`datapointsExpected` wire contract or C#-local?** (T-02; gates the
   `Conclusion.cs` lease.) Contract → add to Swift. Local → `JsonIgnore` or document as non-contract. C#
   currently emits two keys that exist nowhere in the macOS tree.
10. **Catalog backfill** (D1-39). Does E13 backfill the 17 uncatalogued Linux files, or only append its own
    new strings? **Not bookkeeping** — the real price that contradicts the Dashboard, the fabricated
    encryption string and the nav drift all sit in files you cannot see from the catalogs. Backfill is the
    difference between being able to edit this copy and not, and is a real scope increase on one seat.
11. **The narrow Prophet/Mirror lift** (§3.9) — declined by default, yours to overrule.
12. **The Mirror/licence contradiction.** Mirror's deferral note says licence activation is unavailable on
    Linux; `LicenseView.axaml` is in the nav advertising a monthly price. Either the note is stale or the
    pane sells something unactivatable. Outside every seat's file set.
13. **Conclusions eyebrow copy.** macOS says "THE COUNCIL · SYNTHESIS"; Linux and Windows say "DIAGNOSTICS
    • SYNTHESIS". macOS is the outlier. Copy question, not a defect.
14. **G-09 routing** (§4a) — **the one that suppresses a live tampering finding.** Every Pro Windows run
    shows "CONTENT WARP REQUIRES PRO" and discards a `TAMPERING_DETECTED` conclusion. Three options:
    (a) hand it to the Mirror stream as a rewrite requirement, since that stream owns and is about to
    rewrite `MirrorView.xaml{,.cs}`; (b) split out `ConclusionsView.xaml.cs`, which is on **no** reserved
    list, for partial credit now; (c) lift the reservation for this one fix. **Recommended: (a) plus (b).**
    Not verifiable from a macOS host.
15. **Passphrase escrow** (§8.7). Any recovery path weakens the property the feature creates. Also: the
    empty-paste tier collapse (§5, G-06 caveat 3) needs approval to amend two tests that pin the wrong
    behaviour, and the `S`/`SCAN` menu key (C-16) needs a call on whether macOS adopts or C# drops.

---

## 10. Verification

### 10.1 The auditor loop — a seat, not a phase

One read-only seat runs continuously through every wave. Its dimensions: spine #2 (does any new path let a
failure read as success), spine #3 (did anyone collapse a tri-state to a bool, including in a fake), spine
#5 (**do the new tests assert, or merely execute** — hunt for non-emptiness-only assertions, tests that
pass because validation short-circuits before the code under test, and fakes so permissive the assertion is
vacuous), spine #6 (both twins changed; every `ISecretStore` implementation and fake updated), scope and
keep-out discipline, and house rules. It **runs the suites itself** and checks failure **names**, not
counts. It says plainly when a seat's work is correct — a review that only reports problems teaches nothing
about what can be trusted.

**Both passes justify the seat.** It found the G-02b residual that would otherwise have shipped as closed,
and it caught the corrected-mechanism error in §3.4 only because the finding was challenged rather than
accepted. Equally, it made one instructive mistake: it cleared the libsecret schemas by reading a
*declaration* and crediting it to a *call site* — the same "the artifact exists, so it must be in force"
reasoning as a vacuous test. **For varargs and P/Invoke, count arguments into their slots against the
signature.**

### 10.2 Request this pack first

One command block, under a minute, unblocking §3.3:

```
resolvectl --version; echo "--- STATUS"; resolvectl status --no-pager
echo "--- RESOLVCONF"; cat /etc/resolv.conf; echo "--- MODE"; readlink -f /etc/resolv.conf
```

**Expectation declared in advance, so the result cannot be rationalised afterwards:**
- **PASS** — a `Global` section and ≥1 `Link` section, with DoT state appearing as a `Protocols:` token
  (`+DNSOverTLS`/`-DNSOverTLS`) or a `DNSOverTLS setting:` line.
- **FAIL for the current parser** — neither the literal `DNS Over TLS:` nor `DNSOverHTTPS:` appears
  anywhere, confirming both branches and both tests are dead.
- **INCONCLUSIVE** — systemd-resolved is not running; the fallback path needs a different pack.
- **False clean to refuse:** if `/etc/resolv.conf` does *not* contain `127.0.0.53` on your machine, that
  does **not** clear §3.3 — it means your box is not in stub configuration while the majority of Ubuntu
  Desktop installs are. **Do not let one clean paste close it.**

That paste becomes a golden corpus and CI holds it permanently.

### 10.3 Verification split — the owner is the execution surface

Real-run Linux verification is **available on demand via the owner**; build execution on the authoring host
is not the same constraint. Every pack must be one copy-pasteable block with its pass/fail/inconclusive
expectation **declared before** it is run, and a named false-clean to refuse.

- **Closable by CI alone:** P-04b, P-05c, P-09…P-13, P-17…P-19, and P-06/P-22 permanently once the corpus
  exists.
- **Closable by neither — provable today by reading, so fixable before asking the owner for anything:**
  P-05b, P-05d, P-06's four structural defects, P-07, P-08, P-20, P-21, all of P-04, every K-item in
  §3.5–§3.7, C-10…C-14, M-01…M-03, and the whole of §3.9–§3.14.
- **Require a real Linux box:** P-05a, P-06's true output formats, P-01/P-02 (pkexec with and without an
  agent), P-03 (`postinst` as root; the dual-boot claim needs two installs), P-04/G-16 (libsecret against a
  live keyring — **and `SecretTool.Exists`'s false-Absent assumption is the first item on that leg**),
  P-05e (drop-in persistence across reboot), P-09/P-17's install leg, P-12's snap stage path, P-16's snap
  update channel, and **C-09** (§3.15).

### 10.4 Standing bars

- `cd Linux/Ubuntu/SyxxUI.Avalonia.Tests && dotnet test` — **165/165** today. Any drop is a regression.
- `cd Windows/dotNET/Syxx.Tests && dotnet test` — **12 failed / 907 passed**, all 12 the known non-Warp
  baseline (MatrixGoldenCorpus, ProbeRunner ×4, ReportTests, NetworkKeyState, D100 TTL,
  AuthoritativeVantage, ResultProvenance ×2, SectionCompleteness). **Check failure names, not the count** —
  and note §6: two of those 12 are stale assertions, not baseline.
- `swift build --package-path macOS --target SyxxKit` — **currently fails** (W0-c). Until then no Swift
  claim is verified.
- `Windows/dotNET/SyxxUI` **cannot build on macOS** (WinUI XamlCompiler crashes before `csc`, hiding plain
  C# syntax errors). Never report Windows-GUI verification from this host.
- `python3 scripts/package-pin-parity.py` must exit 0 **and** report ≥1 csproj per root.
- `dotnet build Windows/Syxx.Linux.slnf -c Release -t:Rebuild` must produce a **fresh** Avalonia Release
  DLL timestamp.
- Ledger states for every item: **open / fixed-unverified / verified-by-CI / verified-by-real-run /
  deliberately-out**. "Fixed" alone is not a state.

---

## 11. House rules — bind every seat

1. The word **"verdict" is banned** — `result` in code, `conclusion` in UI and docs. Sole exception: the
   pre-existing signed wire field `evidence.comparisonResult.verdict`, never renamed, never introduced
   anywhere new.
2. Every new user-facing string is **appended** to both output catalogs. Append only — never sort, dedupe,
   reformat or edit existing rows. Staging and single-writer rules per E13.
3. Honesty protocol: `Idle / Running / Completed / Failed`. Failed ≠ never-ran ≠ match. Never render a clean
   or positive state for something that did not run.
4. Never soften, disable or delete a test to go green. A failing honesty test is data. Where a test pins
   *wrong* behaviour, the behaviour is corrected and the test amended deliberately — never the bar lowered.
5. Run .NET tests from the test project directory, not the repo root.
6. Scope Swift builds: `swift build --package-path macOS --target <Target>`. Bare package-scope `swift
   test` hits a `DeepSyxx-Info.plist` link failure belonging to the paused Cage lane — expected, not yours.
7. Explicit-path commits only; **never `git add -A`**. Do not commit unless asked.
8. Tests write stray `receipts/`/`review-receipts/` directories relative to cwd. Leave them.
9. A cross-platform parity claim in a comment is not evidence (§2.5).

---

## 12. Provenance — how this was produced, and what to distrust

**Pass 1:** five ACH investigators (one hypothesis each, evidence-or-falsify) → four implementer seats plus
one read-only auditor. **Pass 2:** three read-only reviewer seats with no Write or Edit tool, so "no code
changes" was structural rather than promised.

**Three of five pass-1 hypotheses were falsified as stated**, and the corrections mattered more than the
confirmations: the reachable path in §3.4 was wrong until a seat traced it; the mechanism in §3.4 was wrong
until challenged; and pass 1's summary once implied overall parity from a narrow wave, which is corrected in
§1. Reviewers also corrected this document's briefs three times — the keep-out breadth (§2.4), the
encrypted-DNS premise (§3.7), and the Conclusions reference codebase (§3.9) — each time in the direction of
**overstating** the Linux gap.

**The task registry is unreliable.** It **dropped** records (two reports lost; sends returned success at the
sender and never surfaced, so a seat cannot detect it and will believe it reported) and **re-broadcast
completed assignments** as new, to four seats across both passes. Every seat correctly declined to redo
finished work, but a less careful one would have re-applied an applied edit. **Use it for human-visible
tracking only; have seats report by message.** Three seats were also killed mid-survey by a session usage
limit, which is why front-loading the at-risk deliverable — the seat proposal — before findings is now the
standing instruction.

**What to distrust in this document:** anything not marked ★ is a reviewer's reading that I did not
independently re-verify. Swift-side runtime claims are reading-based throughout, because `SyxxKit` does not
compile in-tree (W0-c). Windows-GUI runtime claims are absent by policy. `hidepid` behaviour in §3.2 and the
libsecret version boundary in §3.4 are explicitly inferred. And **no Linux runtime behaviour was executed at
all** — that is what §10.2 and §10.3 exist for.
