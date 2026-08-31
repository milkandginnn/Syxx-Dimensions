# Phase 1 Findings — notarization-audit_20260830

Condensed from two read-only specialist reports (swift-expert, 2026-08-31). Raw evidence lives in the cited files; delta IDs are the durable record.

## Task 1.1 — Update install path (UpdateService.swift:242-360)

```
domain: swift-code-analysis
status: complete
result: DELTA FILED — E207
```

- **No Apple code-signature check on the replacement bundle.** No `SecStaticCodeCheckValidity`, `spctl`, or `codesign` anywhere in SyxxKit's install path; installDMG (:260-317) / installZIP (:319-360) go from "first directory entry ending in `.app`" (:281, :337) straight to `fm.copyItem` over `Bundle.main.bundlePath` (:300, :351). No bundle-ID, team-ID, or signed-version cross-check either.
- **No quarantine handling.** `com.apple.quarantine` never set/cleared/checked; whether it propagates through the mounted DMG could not be determined from source (runtime question).
- **TOCTOU confirmed.** Hash/size computed by path (:128, :134-139); `hdiutil attach`/`unzip` re-open the same user-writable temp path (:267, :327). No fd pinning, no immutable staging, no re-hash before install; DMG not mounted `-readonly`.
- **Swap non-atomic; rollback swallowed.** move-aside + copy with `try?` restore (:293-305, :345-355) — failed restore leaves **no bundle at all**; installDMG has no `defer` (throw paths leak the mount); on success `state` stays `.installing` forever (:141-151).
- **Sole trust anchor is the pinned dev-only update key** (:417-426, self-documented as DEV-ONLY).
- **Zero install-path tests** — `UpdateSignatureVerificationTests.swift` (21 tests) stops at `validatedDownloadURL`; all install methods are `private` on a `@MainActor` class.

## Task 1.2 — LAN/Portal TCC reachability

```
domain: swift-code-analysis
status: complete
result: SETTLED (source level) — D306 updated; runtime confirmation → Phase 4 (Task 4.4)
```

- **The Local Network prompt is reachable from the GUI and the trigger is unconditional:** every Portal run (and the dashboard run-everything sweep, AppModel.swift:606) executes `detectGateway()` → HTTP GET to `http://<gatewayIP>/` (NetworkKeyState.swift:697-702) via PortalView → AppModel.run → ProbePlan.runPortal (:185).
- **Second trigger in the common case:** when the router is the system resolver, open-resolver DNS probes target the on-link private address (ProbePlan.swift:193-207, OpenResolverProbe.swift:62-80) — exactly the configuration Syxx most wants to inspect.
- **Non-triggers:** `arp -an` neighbour-table dump (LANCartographer.swift:262-266 — passive; and LANCartographer has **no GUI caller**, PortalView.swift:188), `route -n get`, `ipconfig getoption`, `scutil --dns`, `getifaddrs`, resolv.conf read, `dscacheutil -flushcache`.
- **Could not determine from source:** traceroute child-process TCC attribution (TracerouteRunner.swift:17-24); captive-portal probe destination class (resolver-determined).
- **Keys warranted:** `NSLocalNetworkUsageDescription` — required. `NSBonjourServices` — NOT warranted (no NWBrowser/NetServiceBrowser in macOS/Sources). No other usage keys indicated within read scope.
- **OS-floor note:** the user-facing prompt is believed to enforce from macOS 15 while the app floor is 14.0; the key is inert where unenforced. Flagged, not asserted; Phase 4 confirms at runtime.
- **Honesty follow-up (raised, not filed as its own delta — recorded in D306):** a denial degrades to `.unknown`/`.untested` (correct direction) but user copy does not distinguish "permission denied" from "network failure" (NetworkKeyState.swift:717-721).

## Deltas filed this phase (Task 1.3)

| ID | Domain | Summary |
|----|--------|---------|
| C218 | C | Encrypted-DNS entitlement-state classifier tests quarantined; zero active coverage |
| D302 | D | bundle-app.sh silently degrades to ad-hoc; helper unblessable while build reports success |
| D303 | D | No `--timestamp` on any codesign call (notarization prerequisite) |
| D304 | D | Notarization exists only as echoed instructions; no script notarizes/staples/assesses |
| D305 | D | Encrypted DNS: entitlement key + provisioning-profile plumbing absent |
| D306 | D | Zero TCC usage-description keys; Local Network prompt unconditionally reachable |
| D307 | D | Package.swift parity tests ignore linkerSettings (sectcreate/framework drift undetected) |
| E205 | E | Team OU 994XAF8MYH hard-coded in 4 requirement sites, no agreement test |
| E206 | E | No test asserts entitlement-file content |
| E207 | E | Update install: no Apple signature/quarantine/identity check, TOCTOU, swallowed rollback |
| F074 | F | Stale DESIGN.md:328-334 citations; layer-4 design text unlocatable |

Note: E207's remediation is not yet a plan task — it exceeded the audit's expected scope. Owner decision requested at the Phase 1 gate: add it to Phase 2 (Dev-ID-independent — Fixes 2-4) / Phase 4 (Fix 1 needs a real signature), or spin it into its own track.
