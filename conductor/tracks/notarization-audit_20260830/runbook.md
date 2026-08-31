# Day-0 Runbook — Apple Developer Status Arrival

**Track:** notarization-audit_20260830 · **Written:** 2026-08-31 · **Owner tasks are marked (O); agent tasks (A).**

Execute top to bottom. File the two restricted-entitlement requests FIRST — they have the longest lead times and nothing else depends on them being granted.

## Step 0 — Immediately on membership activation

| # | Who | Action | Depends on | Notes |
|---|-----|--------|-----------|-------|
| 0.1 | O | Record the real **Team ID** from the membership page | membership | If it differs from `994XAF8MYH`, flag it loudly — E205: four requirement sites must change in lockstep (DeepSyxx-Info.plist:19, bundle-app.sh:190, DeepSyxx/main.swift:58, :291) |
| 0.2 | O | File the **NetworkExtension** entitlement request: `com.apple.developer.networking.networkextension` with `dns-settings` AND `dns-proxy` values | membership | dns-settings unblocks encrypted DNS (D305); dns-proxy unblocks the future layer-4 track. Request both in one filing |
| 0.3 | O | File the **Endpoint Security** client entitlement application: `com.apple.developer.endpoint-security.client` | membership | Apple application with business justification; historically weeks of lead time. The ES feature itself is a future track — filing now is free optionality |

## Step 1 — Certificates (same day)

| # | Who | Action | Depends on | Notes |
|---|-----|--------|-----------|-------|
| 1.1 | O | Create **Developer ID Application** certificate; install in the build host's login keychain (private key backed up offline) | 0.1 | This is the one certificate everything below needs |
| 1.2 | O | (Optional) **Developer ID Installer** certificate | 0.1 | Only if a `.pkg` is ever wanted; DMG flow does not need it |
| 1.3 | O | Create an **App Store Connect API key or app-specific password** for `notarytool` | membership | Store via `xcrun notarytool store-credentials` profile; never in the repo (repo rule: no secrets) |

## Step 2 — First real signed build (agent, after 1.1)

| # | Who | Action | Depends on | Notes |
|---|-----|--------|-----------|-------|
| 2.1 | A | Set the parameterized team ID (plan Task 2.1 must have landed; else do the 4-site lockstep edit by hand) to the real Team ID | 0.1, 1.1 | Verify all four sites agree — E205 |
| 2.2 | A | Build fail-closed: `REQUIRE_DEVELOPER_ID=1 ./bundle-app.sh` (plan Task 2.2) with `--timestamp` active (Task 2.3) | 1.1 | Build must ERROR, not warn, if the cert isn't found — D302/D303 |
| 2.3 | A | Verify **SMJobBless** end-to-end: bless prompt → helper installed to /Library/PrivilegedHelperTools → XPC `ping` returns | 2.2 | First-ever real test of PrivilegeManager.swift:297-398 with a satisfiable requirement chain |
| 2.4 | A | Verify keychain prompts collapse to one-time (D119 symptom) | 2.2 | LicenseManager/WitnessKeyManager per-launch caches were mitigations; confirm and annotate, respecting the D116 gate |

## Step 3 — Notarize + staple (agent, after 1.3 + 2.2)

| # | Who | Action | Depends on | Notes |
|---|-----|--------|-----------|-------|
| 3.1 | A | Run scripted notarization (plan Task 2.4): `notarytool submit --wait` on the DMG → `stapler staple` → `spctl --assess --type execute` on app, CLI, helper, DMG | 1.3, 2.2 | D304. First submission often surfaces signing mistakes — read the notarytool log on failure |
| 3.2 | A | Gatekeeper test on a clean machine/VM (download over HTTP so quarantine applies) | 3.1 | This also feeds the E207 quarantine-propagation question |
| 3.3 | A | One full update-channel cycle installing the notarized artifact | 3.1 | Phase 4 Task 4.5; note E207 remains open until its fixes land |

## Step 4 — When the NE dns-settings grant arrives (days-weeks later)

| # | Who | Action | Depends on | Notes |
|---|-----|--------|-----------|-------|
| 4.1 | O | Create the provisioning profile carrying the NE entitlement for the app's bundle ID | 0.2 grant | |
| 4.2 | A | Add the entitlement key to `Syxx.entitlements`, add `embedded.provisionprofile` copy step to bundle-app.sh (plan Task 3.7) | 4.1 | D305; entitlement-content tests (Task 2.5) must be updated in the same change |
| 4.3 | A | Verify `NEDNSSettingsManager` save succeeds; verify the honest error states still classify (C218 tests must be active by then) | 4.2 | Phase 4 Task 4.3 |

## Step 5 — When the ES grant arrives (weeks; future track)

| # | Who | Action | Depends on | Notes |
|---|-----|--------|-----------|-------|
| 5.1 | O+A | Open a new feature track for the live Endpoint Security client (design → TDD) | 0.3 grant | Nothing exists in code today (audit conclusion: absent); do not bolt onto this track |

## Standing cautions

- **Never commit** certificates, API keys, notarytool profiles, or provisioning profiles. `.gitignore` check before any new artifact paths.
- Requirement-string changes (E205 sites) are **high-risk** per AGENTS.md (privileged helper) — code review mandatory, and semantics must stay pinned (parameterize the value, never weaken the requirement).
- Every gate that cannot run (e.g., no clean VM) is reported **NOT RUN**, never passed.
