# Implementation Plan: Notarization-Gated Feature Audit & Developer ID Readiness

**Track ID:** notarization-audit_20260830
**Spec:** [spec.md](./spec.md)
**Created:** 2026-08-30
**Status:** [ ] Not Started

## Overview

Phase 1 closes the audit (no Dev ID needed). Phase 2 lands every hardening change that is codeable **before** the credential arrives. Phase 3 is the Day-0 runbook executed the day membership activates. Phase 4 verifies the previously blocked features on a real Developer ID build. Phases 2-4 are owner-gated: planning is complete when this plan is approved; coding seats spend only on an explicit build instruction.

## Phase 1: Close the audit (no Apple credential required)

Resolve the two unresolved survey items and make every gap durable in repo tracking.

### Tasks

- [x] Task 1.1: Read `macOS/Sources/SyxxKit/UpdateService.swift:250+` (installDMG/installZIP) and record whether the replacement bundle's Apple code signature / quarantine state is checked before swap; file a delta if not. (Specialist: swift-expert, read-only.) → **No check of any kind; TOCTOU; swallowed rollback. Filed E207; details in [findings.md](./findings.md).**
- [x] Task 1.2: Determine whether the LAN/route path (`SyxxKit/LAN/*`) can trigger the macOS 14+ local-network TCC prompt; record the source-level determination and mark final runtime confirmation as blocked on a signed build (Phase 4). → **Reachable and unconditional (gateway HTTP GET on every Portal run). `NSLocalNetworkUsageDescription` required; `NSBonjourServices` not warranted. Recorded in D306 + findings.md.**
- [x] Task 1.3: Register DELTAS.md entries for every spec-table gap. → **Filed C218, D302-D307, E205-E207, F074 (11 entries). DELTAS.md edits are in the working tree but deliberately NOT committed: the file carries ~1,161 lines of prior-session unstaged audit content that must not be swept into this track's commits — owner to commit DELTAS.md when the prior content is reviewed.**
- [x] Task 1.4: Add the Day-0 runbook to this track as `runbook.md`. → **[runbook.md](./runbook.md): entitlement requests first (longest lead time), then certs, first fail-closed signed build, notarize+staple, NE-grant follow-on, ES as future track.**

**Open decision for the phase gate:** E207 (update install path: no Apple signature check, TOCTOU, non-atomic swap) exceeded the audit's expected scope. Options: (a) add Fixes 2-4 to Phase 2 (Dev-ID-independent) and Fix 1 to Phase 4; (b) spin off a dedicated track. Recommendation: (a) — it is squarely update-channel hardening this track already owns.

### Verification

- [x] Every DELTAS entry names affected paths + evidence to close (11 entries: C218, D302-D307, E205-E207, F074). `make garden` run 2026-08-31: **FAIL with 16 dead-link errors, all pre-existing in docs/* files this track never touched — zero errors originate from files changed by this phase** (criterion recorded as "no new garden errors", openly amended from "passes"; the 16-error baseline is prior drift, itself partly covered by F-domain docs deltas). Remaining "could not determine" items (quarantine propagation through mounted DMG; traceroute child-process TCC attribution; macOS 14-vs-15 prompt floor) are all tracked in E207/D306/findings.md and carried to Phase 4.

## Phase 2: Dev-ID-independent hardening (codeable now; owner build instruction required)

All tasks are TDD; none require the credential. Helper/signing surfaces are high-risk → code-review subagent mandatory per change.

### Tasks

- [ ] Task 2.1: Parameterize the team ID: single source of truth (env/config) feeding `DeepSyxx-Info.plist`, `bundle-app.sh:190`, and the two requirement strings in `DeepSyxx/main.swift:58,:291`; test asserts all four sites agree. Counterpart note: macOS-only, no C# twin.
- [ ] Task 2.2: Fail-closed signing mode: `bundle-app.sh` gains `REQUIRE_DEVELOPER_ID=1` that errors instead of degrading to ad-hoc `-` (default behavior unchanged for dev builds); assertion test in the bundle script's self-checks.
- [ ] Task 2.3: Add `--timestamp` to all three codesign calls (bundle-app.sh:215,:222,:230) — notarization prerequisite, harmless on dev builds.
- [ ] Task 2.4: Script the notarization step: `notarytool submit --wait` + `stapler staple` + `spctl --assess` in `build_dmg.sh`, gated behind credentials being present (skip with loud NOT RUN otherwise; never silently pass).
- [ ] Task 2.5: Entitlement-content tests: active test target reads both `.entitlements` files and asserts expected keys, so a silently emptied file fails the suite.
- [ ] Task 2.6: Extend `RootPackageParityTests` to compare `linkerSettings` (sectcreate flags, linkedFramework/linkedLibrary) between the two Package.swift manifests.
- [ ] Task 2.7: Restore quarantined `EncryptedDNSConfigTests` (classifier + `.entitlementRequired` state) into the active SyxxKitTests target; fix for the right reason if stale.
- [ ] Task 2.8: Add TCC usage-description keys to the generated Info.plist in `bundle-app.sh` (at minimum `NSLocalNetworkUsageDescription`), copy registered in `outputs-catalog.csv`.
- [ ] Task 2.9: Add the missing code comment at `DeepSyxx/main.swift:58` noting the ad-hoc-build consequence for the GUI capture path.

### Verification

- [ ] `cd macOS && swift build && swift test` — compare failing NAMES against the 29-name baseline; bundle-app.sh self-assertions pass in both default and fail-closed modes; code review complete on every task.

## Phase 3: Day-0 runbook (executes when Apple Developer status lands)

Owner tasks (O) cannot be performed by agents; agent tasks (A) depend on the owner artifacts.

### Tasks

- [ ] Task 3.1 (O): Complete enrollment; record the real Team ID.
- [ ] Task 3.2 (O): Create **Developer ID Application** certificate (and Developer ID Installer if a pkg is ever wanted); install into the login keychain of the build host.
- [ ] Task 3.3 (O): File both restricted-entitlement requests immediately (longest lead time): NetworkExtension (`dns-settings` + `dns-proxy`) and Endpoint Security client — the ES grant requires an Apple application and can take weeks; filing is Day-0 even though the ES feature itself is a future track.
- [ ] Task 3.4 (A): Set the parameterized team ID (Task 2.1) to the real Team ID; if it differs from `994XAF8MYH`, verify all four requirement sites updated in lockstep.
- [ ] Task 3.5 (A): Produce the first fail-closed (`REQUIRE_DEVELOPER_ID=1`) signed build; verify SMJobBless bless succeeds and XPC ping returns.
- [ ] Task 3.6 (A): Run the scripted notarization (Task 2.4); staple; `spctl --assess --type execute` passes on app, CLI, helper, and DMG.
- [ ] Task 3.7 (O+A): Once the NE dns-settings entitlement is granted: create the provisioning profile, add the entitlement key to `Syxx.entitlements`, add the `embedded.provisionprofile` copy step to `bundle-app.sh`.

### Verification

- [ ] Notarized, stapled DMG passes Gatekeeper on a clean machine/VM; bless + capture path work end-to-end without ad-hoc warnings.

## Phase 4: Post-credential feature verification

### Tasks

- [ ] Task 4.1: Cage GUI capture path — end-to-end on the signed build (helper requirement strings now satisfiable); record real-run evidence.
- [ ] Task 4.2: Keychain prompt behavior — confirm the repeated-password-prompt symptom disappears with a stable signature; close/annotate D119 (respecting the D116 gate).
- [ ] Task 4.3: Encrypted DNS — with entitlement + profile embedded, verify `NEDNSSettingsManager` save succeeds and the honest error states still classify correctly.
- [ ] Task 4.4: LAN TCC prompt — runtime confirmation of Task 1.2's determination; adjust usage strings if the prompt copy renders.
- [ ] Task 4.5: Update channel — one full signed-appcast update cycle installing a notarized artifact; resolve the Task 1.1 finding if a signature/quarantine check was added.
- [ ] Task 4.6: File follow-up feature tracks: `NEDNSProxyProvider` layer-4 extension; live Endpoint Security client (both explicitly out of scope here).

### Verification

- [ ] Real-run evidence recorded per feature; ROADMAP.md blocked rows updated; DELTAS entries from Task 1.3 closed or re-scoped.

## Final Verification

- [ ] All acceptance criteria in spec.md met
- [ ] Test suites pass (failing NAMES compared to baseline, never counts)
- [ ] DELTAS.md / ROADMAP.md / CHANGELOG.md current
- [ ] Ready for review

---

_Generated by Conductor. Tasks will be marked [~] in progress and [x] complete._
