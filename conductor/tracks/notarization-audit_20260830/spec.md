# Specification: Notarization-Gated Feature Audit & Developer ID Readiness

**Track ID:** notarization-audit_20260830
**Type:** Chore (audit + readiness plan; implementation tasks owner-gated)
**Created:** 2026-08-30
**Status:** Draft

## Summary

Inventory every macOS feature that requires (or will require) Apple Developer Program signing, notarization, restricted entitlements, or provisioning to function; rate each feature's code completion with file:line evidence; and produce a Day-0 readiness TODO so that when the Apple Developer status arrives this week, every blocked feature can be brought online in a known order with no rediscovery.

## Context

macOS distribution is the only platform lane blocked on an external credential (ROADMAP.md:29, :107, :231-243). The privileged helper (DeepSyxx, SMJobBless + XPC), Cage capture, keychain persistence, and the update channel are all code-complete or near-complete but cannot function or ship on ad-hoc/dev signatures. Two NetworkExtension features and any future Endpoint Security client additionally need restricted entitlements granted by Apple. The team OU `994XAF8MYH` is hard-coded in four requirement strings and may not match the new membership.

## Audit Findings (baseline, 2026-08-30)

Completion ratings are code-completion, evidenced by the read-only survey recorded in this track. "Complete (code)" means runtime success still depends on a Developer ID.

| # | Feature | Discipline | Apple requirement | Completion |
|---|---------|-----------|-------------------|------------|
| 1 | DeepSyxx privileged helper (SMJobBless + XPC) | infra/Cage enabler | Developer ID signing; notarization to ship | **Partial** |
| 2 | Cage packet capture (libpcap/BPF/PKTAP) | Cage | Developer ID (indirect, via helper's pinned requirement strings) | **Complete (code)** |
| 3 | Endpoint Security client | Cage-adjacent/forensics | Restricted entitlement `com.apple.developer.endpoint-security.client` (Apple-granted) + profile | **Absent** (offline ES-log analyzer exists and is Apple-independent) |
| 4 | System-wide encrypted DNS (`NEDNSSettingsManager`) | Prophet-adjacent | Restricted entitlement `com.apple.developer.networking.networkextension` (dns-settings) + profile + Developer ID | **Partial** |
| 5 | `NEDNSProxyProvider` always-on toggle + in-flow PID attribution ("layer 4") | Prophet/Cage | Same NE entitlement (dns-proxy) + extension target + profile | **Stub** |
| 6 | Local Unbound resolver + system resolver switch | Prophet | None (admin elevation via helper) | **Complete** |
| 7 | Signed update channel (Ed25519 appcast) | infra | Notarization + Developer ID for the delivered artifact | **Partial** |
| 8 | Keychain-backed secrets (Witness key, license, integrity HMAC) | Witness/infra | Developer ID (stable signature → persistent keychain ACL) | **Complete (code)** |
| 9 | Hardened runtime + notarization in build pipeline | infra | Notarization | **Partial** (runtime flag yes; no notarytool/stapler/`--timestamp`) |
| 10 | launchd registration | infra | Developer ID (via SMJobBless) | **Complete** (daemon); **Absent** (user agent/login item) |
| 11 | TCC usage-description declarations | Portal/Cage | TCC prompts | **Absent** |
| 12 | Provisioning profile plumbing | infra | Profile embedding for NE/ES entitlements | **Absent** |

### Itemized: exists vs. not yet coded

**1. DeepSyxx helper — exists:** SMJobBless `-sectcreate` linker flags (macOS/Package.swift:93-110); embedded Info/launchd plists (macOS/Sources/DeepSyxx/DeepSyxx-Info.plist:17-20, com.milkandgin.deepsyxx.plist); build-time Mach-O/plist assertions (macOS/bundle-app.sh:42-121, :236-243); bless + XPC connect + liveness ping (macOS/Sources/SyxxApp/PrivilegeManager.swift:213-236, :297-398); audit-token client signature validation (macOS/Sources/DeepSyxx/main.swift:285-337). **Not yet coded:** fail-closed signing (bundle-app.sh:198-210 silently degrades to ad-hoc `-`, which cannot be blessed); no `SMAppService` migration (deprecated SMJobBless reached via `@_silgen_name`, PrivilegeManager.swift:6-24); no notarization step; entitlements file carries only `app-sandbox=false`.

**2. Cage capture — exists:** Ccage pcap/PKTAP open (macOS/Sources/Ccage/include/Ccage.h:20-33), CLI capture with announced PKTAP fallback (SyxxCLI/Commands/SyxxCage.swift:186-220, :468-485), PKTAP parse + per-DLT decode (SyxxKit/PktapHeader.swift, DNSCapture.swift:266-345), helper-side CLI signature gate (DeepSyxx/main.swift:41-68). **Not yet coded:** nothing — but the GUI capture path is non-functional on ad-hoc builds because the pinned requirement string at DeepSyxx/main.swift:58 cannot pass; no code comment marks that consequence.

**3. Endpoint Security — exists:** offline ES-shaped log analyzer only (SyxxKit/Forensics/EndpointLogAnalyzer.swift). **Not yet coded:** everything live — no `es_new_client`/`import EndpointSecurity`, no framework linkage (Package.swift:67-71), no ES entitlement key in either .entitlements file. The entitlement itself requires an application to Apple with lead time.

**4. Encrypted DNS — exists:** full enable/disable/currentState over `NEDNSSettingsManager`, DoH+DoT (SyxxApp/EncryptedDNSConfigurator.swift:19-112); entitlement-error classification (:122-144; SyxxKit/EncryptedDNSConfig.swift:82, :176); honest Settings copy (SettingsView.swift:229-230). **Not yet coded:** NE entitlement key absent from Syxx.entitlements (:5-10); no provisioning profile and no `embedded.provisionprofile` copy step in bundle-app.sh; classifier tests quarantined (Tests/_quarantine/EncryptedDNSConfigTests.swift:40-43 — not in any target).

**5. Layer 4 dns-proxy — exists:** seam only — `SystemResolverSwitching` protocol + `UnboundEntitlementSeam.deferredReason` (SyxxKit/UnboundResolver.swift:290-311); `TODO(dev-ID)` markers (UnboundResolver.swift:28-32, AppModel.swift:357-358). **Not yet coded:** the `NEDNSProxyProvider` subclass, the extension target, the `.appex`/PlugIns bundling step, the auto-toggle wiring.

**7. Update channel — exists:** verify-before-decode Ed25519 manifest, SHA-256 + size + type + host policy gates, pinned vectors (SyxxKit/UpdateService.swift:56-190, :378-460, :504-511). **Not yet coded:** notarization of the shipped DMG (macOS/scripts/build_dmg.sh:61-63 only echoes `notarytool`/`stapler` as instructions); unresolved: whether installDMG/installZIP checks the replacement bundle's Apple signature/quarantine (UpdateService.swift:250+ — not yet read; resolve in Phase 1).

**8. Keychain — exists:** data-protection keychain, `WhenUnlockedThisDeviceOnly`, atomic update-or-add (SyxxKit/WitnessKeyManager.swift:50-206; LicenseManager.swift:80-150; Trust/IntegrityTag.swift:85-112). **Not yet coded:** nothing — repeated password prompts are the documented consequence of ad-hoc signing (LicenseManager.swift:93-100, WitnessKeyManager.swift:64-68, PrivilegeManager.swift:51-54); D119 is blocked on D116.

**9. Pipeline — exists:** `--options runtime` on all three codesign calls (bundle-app.sh:215, :222, :230); Developer-ID-first identity resolution (:198-210); Info.plist-bound-to-signature gate (:236-243). **Not yet coded:** any executable `notarytool submit` / `stapler staple` / `spctl --assess` step; `--timestamp` on all three codesign calls (a notarization prerequisite); DMG signing (build_dmg.sh:41-52 is hdiutil + shasum only).

**11. TCC — not yet coded:** the generated Info.plist (bundle-app.sh:170-194) declares zero usage-description keys (`NSLocalNetworkUsageDescription` etc.) despite LAN/route features (SyxxKit/LAN/*, PortalView.swift:35). Unresolved: whether the LAN path triggers the local-network prompt on macOS 14+ — needs a runtime observation (Phase 1 carries it; final resolution needs a signed build).

**12. Cross-cutting risks:** team OU `994XAF8MYH` hard-coded in four requirement strings (DeepSyxx-Info.plist:19, bundle-app.sh:190, DeepSyxx/main.swift:58, :291) — if the new membership gets a different Team ID every one must change in lockstep; no test asserts entitlement-file content; RootPackageParityTests does not compare `linkerSettings` (the SMJobBless `-sectcreate` flags can drift between the two Package.swift manifests undetected); no DELTAS.md entries exist for any signing/notarization gap (tracked only in ROADMAP/CHANGELOG); source comments cite DESIGN.md:328-334 line numbers that do not exist in the current 75-line docs/DESIGN.md.

## Acceptance Criteria

- [ ] The two unresolved audit items are resolved with evidence (UpdateService install-path signature/quarantine handling; LAN TCC prompt behavior) or explicitly carried as tracked deltas.
- [ ] Every gap above is registered as a DELTAS.md entry (currently zero exist) with affected paths and evidence needed to close.
- [ ] A Day-0 Developer ID runbook exists in this track: ordered owner tasks (enroll, certs, restricted-entitlement requests, profiles) and agent tasks (team-ID parameterization, `--timestamp`, notarization step, entitlement embedding), each with its blocking dependency named.
- [ ] Dev-ID-independent hardening tasks are enumerated in plan.md so they can be implemented before the credential arrives (fail-closed signing option, entitlement-content tests, linkerSettings parity test, un-quarantined encrypted-DNS classifier tests, TCC usage strings).
- [ ] Restricted-entitlement requests (NetworkExtension dns-settings/dns-proxy; Endpoint Security client) are documented with their Apple lead-time risk so the owner can file them the day membership activates.

## Dependencies

- Apple Developer Program membership (owner; expected this week) — gates Phases 3-4 only.
- DeepSyxx helper and Cage code (complete; this track does not modify their logic).
- D116 security decision (blocks D119 keychain-prompt closure).

## Out of Scope

- Implementing the `NEDNSProxyProvider` extension or a live Endpoint Security client (each is a future feature track; this track only plans and files the entitlement requests).
- Windows Authenticode / Linux packaging trust work.
- Any change to signed payload bytes, the Ed25519 update manifest format, licensing, or Witness signing logic.
- Server-side changes.

## Technical Notes

- Owner memory: teams plan by default — Phases 2-4 implementation tasks require an explicit build instruction before any coding seat is spent.
- All signing-pipeline edits touch bundle-app.sh / build_dmg.sh only; helper logic, canonical bytes, and requirement-string *semantics* are untouched (the team-ID literal is parameterized, not weakened).
- The helper + XPC + licensing surfaces are automatically high-risk per AGENTS.md; code review is mandatory on every Phase 2+ change.

---

_Generated by Conductor. Review and edit as needed._
