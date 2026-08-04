# Encrypted Transports — Sub-projects E & F: Engineer-Ready Plan

Bundled Unbound (**E**) and systemwide routing + menu-bar/tray (**F**). Derived from
`ENCRYPTED-TRANSPORTS-PLAN.md` §§1, 3, 4, 5.E, 5.F, 6 and `DESIGN.md:309-341`. Every
`file:line` below was re-verified against the live tree on the planning date; drift
corrections are called out inline (e.g. `HasConfiguredDoHTemplate` is `:347-361`, not
the `:347-363` the program plan cited).

> **This is a plan. No feature code is written here.** Implementers: `syxx-engineer`
> for all core/service/native code; `syxx-gui-architect` for the menu-bar/tray surface
> and every user-visible string (all copy below is **PROPOSED, pending approval** and
> must land in `outputs-catalog.csv`).

---

## 0. Scope, locked decisions, and the honesty frame

**Locked (do not re-litigate):**

- Unbound is **PAID**. Gate on the server-validated entitlement: macOS
  `LicenseManager.isFeatureUnlocked()` (`LicenseManager.swift:210`, returns
  `(unlocked, message)`), Windows `LicenseManager.IsFeatureUnlockedAsync()`
  (`LicenseManager.cs:483`). There is **one** Pro unlock, not a per-feature enum — gate
  on `.unlocked` / `.Unlocked`, never a local bool (D013/KTRU-01).
- Two user-selectable modes: **(a)** full DNSSEC-validating recursion (root hints +
  `unbound-anchor` trust anchor); **(b)** forward-over-DoT (`forward-tls-upstream`) to a
  resolver profile produced by sub-project C.
- Bind: macOS `127.0.0.1:5335` (avoid a root helper); Windows `127.0.0.1:53` via a
  standing elevated Windows service.
- Windows privilege model: **a standing elevated Windows service** (decision 5).
- Full macOS/Windows parity via **one shared tri-state contract** (F7).
- Ubuntu is a follow-on NOTE only (§7 here).
- Unbound is **BSD-3-Clause** — no licensing pause on bundling.

**This is Syxx's first WRITE path into OS DNS state.** The engaged/disengaged/health
state is now governed by the honesty spine exactly like a verdict. The seven fail-closed
rules from §5.F are binding on every F step and reproduced in §2. The load-bearing
anti-pattern: `HasConfiguredDoHTemplate()` (`NetworkKeyState.cs:347-361`) reads
`HKLM\...\Dnscache\Parameters\DohInterfaceSettings\Doh\<ip>\DohFlags` — it proves a
template is *configured*, not that resolution is *flowing through us*. It is
**detection-only and MUST NEVER be the engaged-state oracle.** Engaged state is proven
only by a live resolution through the intended path this session (F1).

**Ledger convention (Protocol §2, binding).** Each step is tagged:
- `[real-runnable now]` — has a concrete real run that can be executed today on the named
  platform. `[done]` only after that run produces honest output.
- `[code-complete, real-run pending entitlement]` — macOS NetworkExtension pieces that
  compile but cannot `saveToPreferences`/activate without the Apple Developer ID +
  `com.apple.developer.networking.networkextension` entitlement. **Never** recorded
  `[done]` on a green build alone.
- Notarized **distribution** of bundled Unbound is Dev-ID-gated and is a separate axis
  from local run — do not conflate. Local build/run/lifecycle of Unbound is
  `[real-runnable now]` on both platforms.

---

## 1. Dependency graph

```
  C (resolver profiles)  ──────────────┐  (forward-over-DoT template source)
                                        v
  E1 bundle binaries ─> E2 anchor/hints ─> E3 config templates ─> E4 lifecycle mgr
        (mac + win)                                                    │
                                                                       v
  E5 start/wait-for-listen ─> E6 loopback health tri-state (ResolverHealth)
                                                                       │
  ┌────────────────────────────────────────────────────────────────────┘
  v
  F0 SHARED ROUTING CONTRACT (RoutingState/ResolverHealth/EngageProof) ── must land first in F
        │                                   │
        v                                   v
  F-mac                                 F-win
  Fm1 NSStatusItem host (now) ──┐       Fw1 elevated service host + IPC (now)
  Fm2 NEDNSSettingsManager      │       Fw2 NRPT + netsh + per-iface DNS + restore (now)
      toggle (entitlement-gated)│       Fw3 Win11/Server-2022 DoH-floor detect (now)
  Fm3 NEDNSProxyProvider PID    │
      fix (SEPARATE target,     │
      entitlement-gated)        │
        │                       │
        └────────► FP captive-portal failsafe (both platforms, reuses captivePortalStatus) ◄──┘
```

**Hard ordering constraints (each with its reason):**
- **E1 → E2 → E3 → E4 → E5 → E6.** You cannot health-check a resolver you have not
  started; you cannot start one without a config; the config's validating mode is
  meaningless without the `unbound-anchor` trust anchor (E2), so E2 must precede E3's
  recursion template.
- **C → E3(b).** Forward-over-DoT templates the upstream `forward-addr` / `#hostname`
  from a selected C profile. If C has not landed, ship **E3(a) recursion mode only** and
  stub forward mode as `.untested`; do **not** hardcode a forwarder (that would ship an
  unverified upstream claiming to be user-chosen).
- **F0 → everything in F.** Both platforms must consume the *same* `RoutingState` /
  `ResolverHealth` enums or they diverge exactly like E014. F0 lands before Fm2/Fw2.
- **E6 → Fm2 / Fw2.** F1 (engaged-is-proven-by-live-resolution) reuses E6's loopback
  health probe as the proof-of-path when routing to local Unbound. Routing to a remote
  DoT profile reuses the A-transport probe instead; either way the proof lands before the
  toggle can report "engaged."
- **FP is a gate on Fm2/Fw2 engage, not a successor.** Portal check (F6) runs *before*
  every engage and on every network change; it dominates. It can land in parallel but the
  engage path must call it before F0's contract is allowed to report engaged.

**Priority ordering by mission risk (false-confidence first):** F0 (shared contract, so
neither platform can silently report a false "engaged") → E6 (health tri-state, so a dead
Unbound cannot read "protected") → FP (portal failsafe, so we never strand DNS) → then the
mechanical bundling/toggle steps. A step that makes routing *work* but can report engaged
while resolution is actually falling open to plaintext is a false-secure regression and is
sequenced behind the honesty scaffolding that prevents it.

---

## 2. The seven fail-closed rules (F1–F7) — binding on every F step

| # | Rule | Enforcement in this plan |
|---|---|---|
| **F1** | Engaged is proven by **live resolution through the intended path this session**, never a config write | `EngageProof` (F0) is populated only by E6's loopback probe or the A-transport probe returning a real answer over the intended transport. No proof → state is `.unknown`, never `.engaged`. |
| **F2** | Re-read system DNS and confirm it points at us | Post-write verification: macOS re-read via `NEDNSSettingsManager.loadFromPreferences` + `scutil --dns`; Windows re-read `Get-DnsClientNrptRule` + `Get-DnsClientServerAddress`. Mismatch → `.unknown` + disengage. |
| **F3** | No plaintext fallback in fail-closed mode | Windows `netsh ... udpfallback=no`; Unbound forward mode ships `forward-tls-upstream: yes` with **no** Do53 fallback line; macOS `NEDNSSettings` with no fallback resolvers. |
| **F4** | Tri-state health: validating / forwarding-insecure / down / unknown | `ResolverHealth` enum (F0), populated by E6. Forward mode is **never** labeled "DNSSEC validating." |
| **F5** | Disengage is **proven and safe** with exact state restore | Capture prior state before write (Windows), restore exactly on disengage; re-read to confirm restore; never leave broken DNS. |
| **F6** | Captive portal **dominates** | FP: before engage and on every network change, `captivePortalStatus() == .yes` forces auto-disengage; portal state overrides any engaged claim. |
| **F7** | One shared tri-state contract across platforms | F0 defines `RoutingState`/`ResolverHealth`/`EngageProof` mirrored line-for-line on both platforms. |

---

# Sub-project E — Bundled Unbound (paid, both modes)

New files (proposed paths):
- macOS: `macOS/Sources/SyxxKit/UnboundManager.swift`, config template assets under
  `macOS/Sources/SyxxApp/Resources/unbound/`.
- Windows: `Windows/Syxx.Kit/UnboundManager.cs`, service host `Windows/Syxx.Service/`
  (see F-win), template assets under `Windows/Syxx.Kit/Assets/unbound/`.

## E1 — Bundle `unbound` + `unbound-anchor` binaries `[real-runnable now]`

**Action.** Vendor the `unbound` and `unbound-anchor` executables (BSD-3) into each app's
resource tree and make them locatable by the same discovery pattern the app already uses
for the `syxx` CLI.

**macOS targets.**
- `macOS/bundle-app.sh:17-27` — the resource-copy block. Add copies of
  `Resources/unbound/unbound` and `Resources/unbound/unbound-anchor` into
  `"$APP/Contents/Resources/unbound/"`, mirroring the existing `cp "$BINDIR/syxx"` line
  (`bundle-app.sh:22`).
- Discovery: add `UnboundManager.locateBinary(_ name:)` modeled exactly on
  `AppModel.locateCageBinary()` (`AppModel.swift:777-784`) — search
  `Bundle.main.resourceURL/unbound/<name>`, then next to the executable.

**Windows targets.**
- `Windows/SyxxUI/SyxxUI.csproj` — add the two binaries as `<Content>` with
  `CopyToOutputDirectory=PreserveNewest` under `Assets/unbound/`.
- Discovery: `UnboundManager.LocateBinary(string name)` off `AppContext.BaseDirectory`.

**Classes/methods to add.** `UnboundManager` (both platforms) with `locateBinary`,
`configDirectory` (writable app-support path, since Resources is read-only and Unbound
writes its anchor + PID there).

**Pre-queued commands.**
```
# macOS — obtain a BSD-3 unbound build (Homebrew binary is a starting reference; a
# notarizable build for distribution is E-dist, out of local-run scope):
brew install unbound && cp "$(brew --prefix unbound)/sbin/unbound" macOS/Sources/SyxxApp/Resources/unbound/ && cp "$(brew --prefix unbound)/sbin/unbound-anchor" macOS/Sources/SyxxApp/Resources/unbound/
codesign -dv --entitlements - macOS/Sources/SyxxApp/Resources/unbound/unbound  # inspect existing sig
otool -L macOS/Sources/SyxxApp/Resources/unbound/unbound  # list dylib deps to co-bundle or verify present
# Windows — NLnet Labs ships an official signed unbound MSI/zip; extract unbound.exe + unbound-anchor.exe:
#   download from https://nlnetlabs.nl/projects/unbound/download/ , verify PGP sig, place under Assets\unbound\
```

**Real-run acceptance bar.**
```
# macOS
./macOS/bundle-app.sh release && ./Syxx.app/Contents/Resources/unbound/unbound -V
# HONEST PASS: prints "Version <x> ... linked libs: ..." and exits 0 from inside the bundle.
# Windows
Assets\unbound\unbound.exe -V   # prints version, exits 0
```
The bar is **the vendored binary runs from inside the app tree**, proving it is present and
its dylib/DLL deps resolve — not merely that a file was copied.

**Parity.** Both platforms bundle both binaries with identical directory layout.

**License note.** Add Unbound's BSD-3 `LICENSE` to the app's third-party notices
(`outputs-catalog.csv` is for user-facing strings; the notice file is a separate
`gui-architect` task — flag it).

**Ledger.** `[real-runnable now]` local. **Notarized distribution of the macOS binary is
Dev-ID-gated** — record that axis as `[code-complete, distribution pending Dev-ID]`, never
folded into the local-run `[done]`.

---

## E2 — Trust-anchor bootstrap via `unbound-anchor` + root hints `[real-runnable now]`

**Action.** On first engage of **recursion mode**, run `unbound-anchor` to fetch/validate
the DNSSEC root trust anchor (`root.key`) into the writable config dir, and ship the IANA
root hints (`root.hints`). Treat a failed anchor bootstrap as **health `.unknown` / mode
unavailable**, never a silent fall-through to unvalidated recursion.

**Targets.** `UnboundManager.bootstrapTrustAnchor()` (both platforms) → runs
`locateBinary("unbound-anchor") -a <configDir>/root.key`. Ship `root.hints` as a bundled
asset (IANA `named.root`) alongside the templates (E1 dirs).

**Honesty crux.** `unbound-anchor` exit code 1 means "anchor updated" (success), 0 means
"unchanged" (also success); any other code, or a missing/zero-byte `root.key`, means the
anchor is **not trustworthy** → recursion mode must refuse to start and report
`ResolverHealth.unknown` with evidence, not start unvalidated. Do not map "no network" to
"anchor OK."

**Pre-queued commands.**
```
# macOS (writable dir, e.g. ~/Library/Application Support/Syxx/unbound)
D="$HOME/Library/Application Support/Syxx/unbound"; mkdir -p "$D"
./Syxx.app/Contents/Resources/unbound/unbound-anchor -a "$D/root.key"; echo "exit=$? (0 unchanged, 1 updated = OK; else bad)"
test -s "$D/root.key" && head -1 "$D/root.key"   # non-empty, contains a DNSKEY/DS line
```

**Real-run acceptance bar.** `root.key` exists, is non-empty, and contains a `. IN DS`/
`DNSKEY` record; a run with the network unplugged and no prior `root.key` reports
**mode-unavailable / health unknown**, not "validating." (Verify by disabling networking
and re-running: the manager must refuse recursion mode, not start it insecure.)

**Parity.** Identical bootstrap + refusal semantics both platforms.

**Ledger.** `[real-runnable now]`.

---

## E3 — Both config-mode templates `[real-runnable now]` (forward mode depends on C)

**Action.** Generate the Unbound config for the selected mode into the writable config dir.

**E3(a) Recursion template** (verified DNSSEC-validating):
```
server:
  interface: 127.0.0.1@<PORT>          # 5335 macOS, 53 Windows
  access-control: 127.0.0.0/8 allow
  do-not-query-localhost: no
  auto-trust-anchor-file: "<configDir>/root.key"   # from E2
  root-hints: "<assetsDir>/root.hints"
  harden-dnssec-stripped: yes
  val-permissive-mode: no              # fail-closed: bogus -> SERVFAIL, never served
  hide-identity: yes
  hide-version: yes
  # loopback-only; no remote-control unless bound to 127.0.0.1 with a generated key
```

**E3(b) Forward-over-DoT template** (guarantee is only as good as upstream — **never**
labeled validating):
```
server:
  interface: 127.0.0.1@<PORT>
  access-control: 127.0.0.0/8 allow
forward-zone:
  name: "."
  forward-tls-upstream: yes
  forward-addr: <ip>@853#<hostname>    # TEMPLATED FROM THE SELECTED C PROFILE
```
`<ip>`, `<hostname>` come from the sub-project C resolver profile struct. **F3 fail-closed:**
no Do53 `forward-addr` fallback line is ever emitted; if the DoT upstream is unreachable
Unbound returns SERVFAIL, which E6 must surface as `down`/`forwarding-insecure`, not clean.

**Targets.** `UnboundManager.renderConfig(mode:profile:)` (both platforms). Mode persisted
in `SyxxConfig` (macOS `SyxxConfig.swift`, Windows `SyxxConfig.cs`) as a new key
`unboundMode` = `recursion` | `forwardDoT`.

**Dependency.** E3(b) requires C's profile struct. **If C is not yet landed, ship E3(a)
only and have `renderConfig` throw/return unavailable for `.forwardDoT` (surfaced as
`.untested`), never substitute a hardcoded upstream.**

**Pre-queued commands.**
```
# validate whichever template rendered:
./Syxx.app/Contents/Resources/unbound/unbound-checkconf "$D/unbound.conf"   # macOS
Assets\unbound\unbound-checkconf.exe %CFG%\unbound.conf                     # Windows
```

**Real-run acceptance bar.** `unbound-checkconf` reports "no errors" for both rendered
modes; the forward template contains the **exact** `forward-addr` from the selected C
profile and **no** plaintext fallback line (grep-assert absence of a bare `forward-addr`
without `#hostname`).

**Parity.** Same two templates, same field set, differing only in bind port.

**Copy/approval.** The mode picker labels ("Full validating recursion" vs
"Forward encrypted to <profile>") are user-visible — **PROPOSED, pending approval**, log to
`outputs-catalog.csv`. Forward mode's label must **not** imply DNSSEC validation.

**Ledger.** E3(a) `[real-runnable now]`; E3(b) `[real-runnable now once C lands]`, else
`[blocked on C]`.

---

## E4 — Per-OS lifecycle manager (LaunchAgent/Daemon vs Windows service) `[real-runnable now]`

**Action.** Install and manage the Unbound process under the OS supervisor.

**macOS.** Ship a launchd job that launches the bundled `unbound -d -c <configDir>/unbound.conf`
bound to `127.0.0.1:5335`. **Design fork to resolve (see Risks):** decision 6 locks 5335
*to avoid a root helper*, but a true **LaunchDaemon** lives in `/Library/LaunchDaemons` and
requires root to install — which reintroduces the helper the decision avoids. **Recommended:
a per-user LaunchAgent** in `~/Library/LaunchAgents/app.syxx.unbound.plist` (no privilege,
5335 is unprivileged so `unbound` binds it as the user). This honors "avoid a root helper"
exactly. Use a LaunchDaemon only if a system-wide (all-users, pre-login) resolver is
required — that needs a one-time authorized install and should be an explicit product
decision. `UnboundManager` writes the plist and calls `launchctl bootstrap gui/$(id -u)`.

**Windows.** Register `unbound.exe` as a Windows service bound to `127.0.0.1:53`, with
service recovery = auto-restart. Registration is an HKLM/service-DB write requiring
elevation → performed by the **standing elevated Syxx service** (Fw1), not the GUI.
`UnboundManager.installService()` / `startService()` / `stopService()` issue control
requests to that service.

**Classes/methods.** `UnboundManager`: `install()`, `start()`, `stop()`, `uninstall()`,
`isRunning()`. macOS via `launchctl` (`Process`, model on `AppModel.swift:599-610`);
Windows via the elevated service + `ServiceController`.

**Pre-queued commands.**
```
# macOS LaunchAgent
launchctl bootstrap gui/$(id -u) ~/Library/LaunchAgents/app.syxx.unbound.plist
launchctl print gui/$(id -u)/app.syxx.unbound | grep -i state   # -> running
# Windows (from the elevated service context)
sc.exe query SyxxUnbound   # STATE : 4 RUNNING
```

**Real-run acceptance bar.** After `start()`, the OS supervisor reports the job running AND
a listener is bound (proven in E5, not here). After `stop()`, the listener is gone and the
supervisor shows the job stopped. Crash test: `kill -9` the unbound PID → macOS LaunchAgent
`KeepAlive` restarts it / Windows recovery restarts it; **E6's health must read `down`
during the gap, never "protected."**

**Parity.** Same install/start/stop/uninstall surface; platform-specific supervisor beneath.

**Ledger.** `[real-runnable now]` both platforms.

---

## E5 — Start + wait-for-listen `[real-runnable now]`

**Action.** After `start()`, poll `127.0.0.1:<port>` until it accepts a DNS query or a
bounded timeout elapses; a timeout is `down`, never "assume it came up."

**Targets.** `UnboundManager.waitForListen(timeout:)` — issue a minimal UDP query
(`. NS` or the E6 control name) to loopback using the existing sender:
macOS `DNSResolver.queryUDPDetailed(_:)` (`DNSResolver.swift:272`) with a `DNSQuery`
(`DNSResolver.swift:5-32`) `server: "127.0.0.1", port: 5335`; Windows
`DNSResolver.SendUDPAsync` (`DNSResolver.cs:927`) to `127.0.0.1:53`. Poll ~10× at 250ms.

**Real-run acceptance bar.** `waitForListen` returns true within the timeout when Unbound
is healthy; returns false (→ `down`) when the config is broken or the port is contended
(test by binding the port first with `nc -l`). No false "listening."

**Parity.** Same poll loop, same timeout, platform-specific UDP sender.

**Ledger.** `[real-runnable now]`.

---

## E6 — Loopback health tri-state (`ResolverHealth`) `[real-runnable now]` — PRIORITY

**Action.** Health-check by resolving control names **through** Unbound on loopback and
classifying into `validating / forwardingInsecure / down / unknown`, reusing the
`EncryptedTransportProbe` injectable-classifier pattern
(`EncryptedTransportProbe.swift:35-62`, `EncryptedTransportProbe.cs:16-44`) but with a
**loopback-UDP query fn** instead of the DoH fn.

**How the tri-state is derived (the honest distinguisher):**
1. **down** — control query to loopback times out / transport error (E5 sender returns
   `.timeout`/`.transportError`).
2. **validating** — query a known DNSSEC-**signed** name (e.g. `cloudflare.com`) *with the
   DO bit set* and observe `authenticatedData == true` in the response, AND a query for a
   known DNSSEC-**bogus** name (e.g. `dnssec-failed.org` / `sigfail.verteiltesysteme.net`)
   returns **SERVFAIL** (rcode 2). Both conditions ⇒ the resolver is actually validating.
3. **forwardingInsecure** — control names resolve (records returned) but `authenticatedData`
   is false on the signed name and/or the bogus name resolves to an answer instead of
   SERVFAIL. This is forward mode, or recursion with validation off — **must not** be
   labeled validating.
4. **unknown** — reached but ambiguous (odd rcode, partial), or anchor bootstrap (E2) failed.

**Verified dependency — the AD bit is available but DO is not yet.** Both `DNSResponse`
types already expose `authenticatedData` (`DNSResolver.swift:127`; `DNSResolver.cs:474`,
set at `:519/:543/:644`) — **no parser change needed.** BUT the current EDNS0/OPT builder
sets flags `0x00000000` with **no DO bit** (`DNSResolver.swift:415-420` comment "no DO
bit"; `DNSResolver.cs:379-386`), so `authenticatedData` will not be meaningfully set by a
validating resolver. **Sub-task E6a:** add a `dnssecOK: Bool` (DO-bit) option to
`DNSQuery`/the OPT builder on both platforms (set the high bit of the OPT TTL flags field,
`0x00008000`), additive + defaulted false so no existing call site changes. This is the one
small resolver change E requires; it belongs to E6, not a separate delta, and is
`[real-runnable now]`.

**Classes/methods.** `enum ResolverHealth { validating, forwardingInsecure, down, unknown }`
(defined in F0 so F reuses it). `UnboundManager.checkHealth() -> ResolverHealth`.

**Pre-queued commands.**
```
# macOS — prove the four states by hand before trusting the classifier:
dig @127.0.0.1 -p 5335 +dnssec cloudflare.com A      | grep -E 'flags:|status:'  # want 'ad' flag, NOERROR
dig @127.0.0.1 -p 5335 dnssec-failed.org A           | grep 'status:'            # want SERVFAIL (validating)
# Windows
Resolve-DnsName -Server 127.0.0.1 -DnssecOk cloudflare.com -Type A
Resolve-DnsName -Server 127.0.0.1 dnssec-failed.org -Type A   # -> query refused/SERVFAIL when validating
```

**Real-run acceptance bar (this is the load-bearing E bar).**
- Recursion mode: signed name returns records with the **AD flag set**, bogus name returns
  **SERVFAIL** ⇒ classifier reports `validating`. Observed via the `dig`/`Resolve-DnsName`
  lines above matching the classifier output.
- Forward-over-DoT mode: same names resolve but AD is **absent** and/or bogus name resolves
  ⇒ classifier reports `forwardingInsecure` and the UI label does **not** say "validating."
- Kill Unbound mid-check ⇒ `down`. Anchor bootstrap failed ⇒ `unknown`. **At no point does
  a dead or non-validating resolver read as "protected/validating."**

**Parity.** Identical four-state classifier and control names on both platforms; the shared
`EncryptedTransportProbe`-style `classify()` keeps the logic line-for-line.

**Copy/approval.** Health-line strings ("Validating locally", "Forwarding encrypted (not
locally validated)", "Local resolver down", "Health unknown") are user-visible —
**PROPOSED, pending approval**, `outputs-catalog.csv`.

**Ledger.** `[real-runnable now]` both platforms.

---

# Sub-project F — Systemwide routing + menu-bar/tray (paid)

## F0 — Shared routing tri-state contract (`RoutingState` / `ResolverHealth` / `EngageProof`) `[real-runnable now]` — LANDS FIRST IN F (F7)

**Action.** Define one contract on both platforms so they cannot diverge (F7). New files:
`macOS/Sources/SyxxKit/RoutingContract.swift`, `Windows/Syxx.Kit/RoutingContract.cs`.

```
enum RoutingState  { engaged, disengaged, unknown }          // never a bool
enum ResolverHealth { validating, forwardingInsecure, down, unknown }   // F4, reused by E6
struct EngageProof  { let resolvedName: String; let servedVia: String;  // F1 live-proof
                      let observedAt: Date }                              // nil proof => cannot be engaged
struct SystemDNSCheck { let pointsAtUs: Bool; let observedServers: [String] }  // F2
```
Rule baked in: a `RoutingState.engaged` value is **constructible only** with a non-nil
`EngageProof` AND a `SystemDNSCheck.pointsAtUs == true`. Absent either ⇒ `.unknown`. This
makes F1+F2 unrepresentable-to-violate at the type level, mirroring the A0 tri-state fix.

**Real-run acceptance bar.** Unit + a real construction test: attempting to build
`.engaged` without proof yields `.unknown`. (Type-level test is the floor; the real bar is
that Fm2/Fw2 below can only report engaged after a live resolution.)

**Parity.** Byte-for-byte equivalent enums/structs both platforms; add a parity fixture.

**Ledger.** `[real-runnable now]`.

---

## F macOS

### Fm1 — `NSStatusItem` menu-bar host (`LSUIElement`) `[real-runnable now]`

**Action.** Add a Dock-less menu-bar agent presenting engage/disengage, mode, health, and
portal state. This is the structural fix for "window lost in the stack" (`DESIGN.md:331`)
and is fully real-runnable **without** any entitlement — only the routing *action* (Fm2) is
gated.

**Targets.**
- `macOS/bundle-app.sh:29-48` — add `<key>LSUIElement</key><true/>` to the generated
  `Info.plist` (currently ends at `ATSApplicationFontsPath`, `bundle-app.sh:44-45`). **Note:
  this makes the whole app agent-style/Dock-less** — confirm the product wants the menu-bar
  form to replace the windowed app, or gate `LSUIElement` behind a build flag if both forms
  must coexist (Risks).
- New `macOS/Sources/SyxxApp/MenuBarController.swift`: `NSStatusItem` +
  `NSMenu`, bound to an `@MainActor` observable that reads `RoutingState`/`ResolverHealth`.
- Wire into `SyxxApp.swift` app lifecycle.

**Real-run acceptance bar.** `./bundle-app.sh release && open Syxx.app` → a status item
appears in the menu bar, no Dock icon, the menu shows live health from E6 (start Unbound,
watch it flip validating→down when killed). The menu **reflects real E6 health**, not a
static label.

**Parity.** Windows equivalent is the tray in Fw1.

**Copy/approval.** All menu titles/state lines — **PROPOSED, pending approval**,
`outputs-catalog.csv`.

**Ledger.** `[real-runnable now]`.

### Fm2 — `NEDNSSettingsManager` engage/disengage toggle `[code-complete, real-run pending entitlement]`

**Action.** Route system DNS over local Unbound (`127.0.0.1:5335`) or a DoT profile via
`NEDNSSettingsManager` — the lighter of the two NE APIs. **Do NOT couple this to the proxy
(Fm3).**

**Targets.** New `macOS/Sources/SyxxApp/DNSRoutingManager.swift`:
- `NEDNSSettingsManager.shared()`, build `NEDNSOverTLSSettings` (DoT profile) or, for local
  Unbound, an `NEDNSSettings` pointing at `127.0.0.1:5335` (loopback via the resolver's
  `servers`; validate NE accepts a loopback server — see Risks). No permissive fallback
  (F3): do not populate `matchDomainsNoSearch` in a way that allows plaintext leak.
- Entitlement: add a `Syxx.entitlements` file (none exists today — verified) with
  `com.apple.developer.networking.networkextension` = `["dns-settings"]`, referenced from
  the build/sign step. **Compiles now; `saveToPreferences()`/`loadFromPreferences()` fail
  at runtime without the provisioning-profile-backed entitlement.**

**F-rule enforcement.** Engage sequence: FP portal check (F6) → `saveToPreferences()` →
**re-read** via `loadFromPreferences()` + `scutil --dns` (F2) → run E6 loopback health /
A-transport probe to obtain `EngageProof` (F1) → only then construct `RoutingState.engaged`.
Any failure → `.unknown` + auto-disengage. Disengage: `removeFromPreferences()` → re-read to
confirm system DNS restored (F5).

**Pre-queued commands (runnable only once entitled).**
```
scutil --dns | grep -A2 'resolver #1'   # after engage: nameserver should be us / NE-managed
```

**Real-run acceptance bar (gated).** With the entitlement present: engage → `scutil --dns`
shows the NE-managed resolver, a real lookup resolves through it, `EngageProof` is
populated, state reads `engaged`; disengage → prior resolver restored, state `disengaged`.
**Until entitled, this is `[code-complete, real-run pending entitlement]` — build-green is
NOT `[done]`.** The honest interim output must be a UI state of `unknown`/"routing
unavailable on this build," never a fake "engaged."

**Parity.** Windows counterpart is Fw2 (real-runnable now).

**Copy/approval.** Engage/disengage notices — **PROPOSED, pending approval**,
`outputs-catalog.csv`.

**Ledger.** `[code-complete, real-run pending entitlement]`.

### Fm3 — `NEDNSProxyProvider` for in-flow PID attribution ONLY `[code-complete, real-run pending entitlement]`

**Action.** A **separate** Network Extension target providing `NEDNSProxyProvider`, used
**only** to retire the post-hoc `lsof` race by reading
`NEFlowMetaData.sourceAppAuditToken` in `handleNewFlow` (`DESIGN.md:330, 337-338`). **This
is NOT the routing toggle and must not be coupled to Fm2** — Fm2 works without it.

**Targets.** New extension target `macOS/NEDNSProxy/` with a `NEDNSProxyProvider` subclass;
entitlement value `dns-proxy` (distinct from Fm2's `dns-settings`); wire the extracted PID
into the existing process-attribution consumer (the Cage/`lsof` path — cross-reference the
attribution consumer during implementation).

**Real-run acceptance bar (gated).** With entitlement: a flow's originating PID is read from
`sourceAppAuditToken` in-flow and matches the true source process (verify against a known
test process), with no `lsof` call. Gated → `[code-complete, real-run pending entitlement]`.

**Parity.** Windows in-flow-PID equivalent is a WFP callout kernel driver — **explicitly out
of initial scope** (`DESIGN.md:339`).

**Ledger.** `[code-complete, real-run pending entitlement]`. Never `[done]` on green build.

---

## F Windows

### Fw1 — Standing elevated service + tray host + IPC `[real-runnable now]`

**Action.** Create the standing elevated Windows service (decision 5) that performs all
privileged DNS writes and hosts/controls Unbound (E4), plus a tray UI in `SyxxUI` that sends
engage/disengage/mode commands to it over a local IPC channel (named pipe). The service holds
elevation; the GUI does not.

**Targets.**
- New project `Windows/Syxx.Service/` (`ServiceBase`/`BackgroundService` worker), registered
  auto-start, ACL'd named-pipe control endpoint (authenticate the caller to the interactive
  user; reject remote/unauthorized callers).
- `Windows/SyxxUI/app.manifest` — currently has **no** `requestedExecutionLevel` (verified
  absent). Keep the GUI `asInvoker`; do **not** mark the GUI `requireAdministrator`. Only the
  one-time service *installer* elevates (UAC once) to register the service.
- Tray: `NotifyIcon`/WinUI tray surface in `Windows/SyxxUI/Services/` mirroring Fm1.

**Real-run acceptance bar.** Install the service (one UAC prompt), `sc.exe query SyxxDnsHelper`
→ RUNNING; the non-elevated GUI sends an engage command over the pipe and the service
executes it; an unauthorized pipe client is rejected. Tray reflects live E6 health.

**Parity.** macOS counterpart: Fm1 host (privilege model differs — macOS uses the
per-user LaunchAgent for Unbound and NE for routing, so no standing root service).

**Ledger.** `[real-runnable now]`.

### Fw2 — NRPT + netsh encryption + per-interface DNS, with capture/restore `[real-runnable now]`

**Action.** Engage systemwide routing via, in order: capture prior state → set DNS →
register encryption → add NRPT rule; disengage restores exactly (F5). All writes run inside
the Fw1 service.

**Engage (fail-closed, F3):**
```
# capture first (for exact restore, F5):
Get-DnsClientServerAddress -AddressFamily IPv4 | Export-Clixml $state\dns.xml
Get-DnsClientNrptRule | Export-Clixml $state\nrpt.xml
netsh dnsclient show encryption > $state\enc.txt
# then, for local Unbound (127.0.0.1:53):
Set-DnsClientServerAddress -InterfaceIndex <idx> -ServerAddresses 127.0.0.1
# for a DoH/DoT profile:
netsh dnsclient add encryption server=<ip> dohtemplate=<url> autoupgrade=yes udpfallback=no   # udpfallback=no = the fail-closed knob (F3)
Add-DnsClientNrptRule -Namespace "." -NameServers <ip> -DohTemplate <url>
```

**Disengage (F5, exact restore):**
```
Remove-DnsClientNrptRule -Name <captured> -Force
netsh dnsclient delete encryption server=<ip>
Set-DnsClientServerAddress -InterfaceIndex <idx> -ResetServerAddresses   # or re-apply captured static set
```

**Targets.** `DNSRoutingManager.cs` in `Windows/Syxx.Service/` (writes) with
`Engage(mode, profile)`, `Disengage()`, `CaptureState()`, `RestoreState()`. **F2 re-read:**
after engage, `Get-DnsClientNrptRule` + `Get-DnsClientServerAddress` must confirm the system
points at us before constructing `RoutingState.engaged`; **must NOT** use
`HasConfiguredDoHTemplate()` (`NetworkKeyState.cs:347-361`) as the engaged oracle — that
proves configuration, not flow. **F1 proof:** run E6 loopback health (local Unbound) or the
A-transport probe (remote profile) for a live `EngageProof`.

**Real-run acceptance bar (real-runnable on Win11/Server-2022+ today).**
```
# engage, then prove routing is live and fail-closed:
Resolve-DnsName cloudflare.com   # resolves via us
Get-DnsClientNrptRule            # shows our rule
netsh dnsclient show encryption  # udpfallback : no
# disengage, then prove exact restore:
Get-DnsClientServerAddress       # back to captured values; Get-DnsClientNrptRule shows our rule gone
```
Honest bar: while engaged, resolution flows through us with `udpfallback=no` (blocked
:853/DoH does **not** fall open to Do53 — verify by blocking the upstream and confirming
resolution **fails**, not silently plaintexts); after disengage, state is byte-for-byte the
captured prior state. A partial engage (e.g. NRPT set but DNS write failed) must roll back to
captured state and report `unknown`, never a half-engaged "engaged."

**Parity.** macOS counterpart Fm2 (gated).

**Copy/approval.** Engage/disengage/restore notices — **PROPOSED, pending approval**,
`outputs-catalog.csv`.

**Ledger.** `[real-runnable now]` on Win11/Server-2022+.

### Fw3 — DoH-floor detection; report unsupported, never silent no-op `[real-runnable now]`

**Action.** Detect the Windows build floor for the DoH client stack (Win11 / Server 2022+).
On older builds, `netsh dnsclient add encryption` / NRPT-DoH are unavailable — the only path
is **local Unbound** (which needs no OS DoH support). Report **unsupported** explicitly;
never silently no-op into a false "engaged."

**Targets.** `DNSRoutingManager.SupportsSystemDoH()` — check `Environment.OSVersion` /
`RtlGetVersion` build number (>= 22000 for Win11) and/or probe for the netsh encryption verb.
Feed the result into F0's `RoutingState` (unsupported → `.unknown` with evidence, plus
offer local-Unbound-only mode).

**Real-run acceptance bar.** On a Win11 host: `SupportsSystemDoH() == true`, routing engages.
On a pre-22000 host (or a VM pinned to Server 2019): returns false, the UI says the systemwide
DoH path is unsupported and offers local Unbound; it does **not** claim engaged. Verify by
running on a down-level VM and confirming the honest "unsupported" surface.

**Parity.** macOS has its own floor (NE entitlement + OS version) surfaced via Fm2's gating.

**Copy/approval.** "Systemwide encrypted DNS needs Windows 11 or Server 2022; local resolver
available" — **PROPOSED, pending approval**, `outputs-catalog.csv`.

**Ledger.** `[real-runnable now]`.

> **WFP callout (in-flow PID on Windows) is explicitly OUT OF INITIAL SCOPE** — it is a
> signed kernel driver (`DESIGN.md:339`). Not planned here.

---

## FP — Captive-portal failsafe (both platforms) `[real-runnable now]` — F6, PRIORITY

**Action.** Before every engage and on every network change, if a captive portal is
detected, auto-disengage so the portal's resolver can complete login; portal state dominates
any engaged claim. Fail safe on disengage (never strand DNS), and **never** report
"encrypted/secure" while disengaged.

**Targets.** Reuse the existing tri-state, **verified** anchors (no new detection logic):
- macOS `NetworkKeyState.captivePortalStatus()` — `NetworkKeyState.swift:324-347`.
- Windows `NetworkKeyState.CaptivePortalStatusAsync()` — `NetworkKeyState.cs:256-300`.

Add a `PortalWatcher` on each platform that (a) calls the above before engage and returns a
hard block if `state == .yes` / `TriState.Yes`; (b) subscribes to network-change
notifications (macOS `NWPathMonitor`; Windows `NetworkChange.NetworkAddressChanged`) and
auto-disengages on portal appearance. Route through the Fw1 service on Windows / the
DNSRoutingManager on macOS.

**Real-run acceptance bar.**
- Join a real captive-portal network (or simulate: point `captive.apple.com` / MS
  connect-test at an intercepting responder). Engage is **refused** with a portal notice; if
  a portal appears while engaged, routing **auto-disengages** and the state reads
  `disengaged` (not "engaged/secure"). Verify by watching the state flip on network change.
- On a clear network, `captivePortalStatus() == .no` → engage proceeds normally. `.unknown`
  (offline) must **not** be treated as "no portal" — engage is held, not forced.

**Parity.** Same pre-engage gate + network-change auto-disengage both platforms, reusing the
already-tri-state detectors.

**Copy/approval.** Portal-disengage notice ("Sign-in network detected — encrypted routing
paused so you can log in") — **PROPOSED, pending approval**, `outputs-catalog.csv`.

**Ledger.** `[real-runnable now]` both platforms.

---

## 3. Ubuntu — follow-on NOTE only (not in this effort's build/real-run scope)

Via the cross-platform .NET `Syxx.Kit`, an Ubuntu client would run Unbound as a **systemd
service** (`unbound.service`) on `127.0.0.1:5335` or `:53`. The load-bearing contention:
`systemd-resolved` owns the stub listener on `127.0.0.53:53`, so Unbound on `:53` collides.
Options: bind `:5335` and point `resolved` upstream at it (`/etc/systemd/resolved.conf`
`DNS=127.0.0.1:5335`, `DNSStubListener=no`), or replace `resolved` entirely. Routing engage
would rewrite `resolved` config / `/etc/resolv.conf` with capture-restore, reusing F0's
contract. **Not planned in detail here; carried as a NOTE for a later effort.**

---

## 4. Risks, open questions, and honesty-protocol hazards

1. **macOS supervisor: LaunchAgent vs LaunchDaemon (E4).** Decision 6 (bind 5335 *to avoid a
   root helper*) is in tension with the program plan's phrase "launchd **LaunchDaemon**"
   (`ENCRYPTED-TRANSPORTS-PLAN.md:305`) — a LaunchDaemon requires root to install,
   reintroducing the helper. **Recommendation: per-user LaunchAgent** (no privilege; 5335 is
   unprivileged). Use a LaunchDaemon only if a system-wide/pre-login resolver is an explicit
   product requirement (then a one-time authorized install is unavoidable). **Needs a
   product decision.**
2. **`LSUIElement` makes the whole app Dock-less (Fm1).** Confirm the menu-bar form
   *replaces* the windowed app, or gate `LSUIElement` behind a build flag so both can coexist.
3. **NE + loopback server (Fm2).** Whether `NEDNSSettings`/`NEDNSOverTLSSettings` will accept
   `127.0.0.1:5335` as the resolver (to route system DNS into local Unbound) needs a runtime
   check — and that check is itself entitlement-gated. Until the Dev-ID arrives this is an
   **unverifiable assumption**; the plan routes local-Unbound engage through it but flags that
   the NE-to-loopback path may require the proxy provider instead. Do not mark resolved.
4. **macOS notarized distribution of a third-party binary (E1).** Bundling Homebrew's
   `unbound` is fine for local run but its signature/hardened-runtime may not survive
   notarization; distribution should use an NLnet-Labs or self-built, hardened-runtime,
   Dev-ID-signed binary. Separate axis from local run — do not conflate.
5. **C dependency for forward mode (E3b).** If C's resolver-profile struct is not landed,
   forward-over-DoT ships as `.untested` (no hardcoded upstream). Confirm C's landing order
   relative to E.
6. **DoQ/DoT profile as Unbound upstream.** Unbound forwards over **DoT** (`forward-tls-
   upstream`); it does not speak DoQ upstream. A DoQ-only C profile cannot back forward mode —
   surface that as unsupported for local Unbound, not a silent DoT substitution.
7. **Standing elevated service = attack surface (Fw1).** The named-pipe control endpoint must
   authenticate the caller and reject remote/unauthorized clients; a permissive pipe would let
   any local process rewrite system DNS. Threat-model the IPC (candidate for `syxx-researcher`).
8. **`dnssec-failed.org` as the bogus control name (E6).** External dependency; if it stops
   being reliably bogus the validating-vs-insecure distinguisher weakens. Ship a second bogus
   control (e.g. `sigfail.verteiltesysteme.net`) and treat "both resolve" as the insecure
   signal. Corroborate the current bogus status at implementation time.
9. **Honesty hazard — half-engaged transient (Fw2).** A multi-command engage (DNS + netsh +
   NRPT) can partially apply. The plan requires roll-back-to-captured-state on any partial
   failure and a `unknown` report; landing Fw2 without the capture/restore of Fw2 itself would
   transiently strand DNS — capture MUST precede any write in the same step.

---

## 5. Handoff

- **`syxx-engineer`** — E1–E6 (both platforms), F0 contract, Fm2/Fm3 native code + entitlement
  file, Fw1 service + IPC, Fw2 routing writes, Fw3 floor detection, FP watcher logic. Suggested
  start: **F0** (shared contract, unblocks all of F and is real-runnable) in parallel with
  **E1→E2→E3(a)→E4→E5→E6** (the real-runnable Unbound spine; E6 is the priority honesty gate).
- **`syxx-gui-architect`** — Fm1 menu-bar surface, Fw1 tray surface, and **all** user-visible
  strings (mode labels, health lines, engage/disengage/restore/portal/unsupported notices),
  each proposed for approval and logged to `outputs-catalog.csv`. Also the Unbound BSD-3
  third-party-notice surface.
- **`syxx-researcher`** — threat-model the Windows service IPC endpoint (risk 7) and confirm
  the E6 bogus-control-name reliability (risk 8) before those steps are marked done.

**Do not** mark any macOS NE step (`Fm2`, `Fm3`) `[done]` on a green build — they are
`[code-complete, real-run pending entitlement]` until the Dev-ID arrives. **Do not** finalize
any user-visible string without approval.
