# Delta-Fix Implementation Plan

**Date:** 2026-07-28
**Companion document:** `DELTA_FIX_SUBAGENT_DEPLOYMENT_PLAN.md` (who is deployed, when)
**Authority:** `DELTAS_FIXES_PLANNING_INSTRUCTIONS.md.rtf`
**Scope:** all 168 open deltas — every ID in `DELTAS.md` absent from both `DELTAS-fixed.md` and `DELTAS-rejected.md`

---

## Executive summary

168 deltas remain open; **112 are Critical**. They are not 168 independent bugs. Clustered by root cause they collapse into **17 groups**, and the groups collapse further into four recurring failure modes:

| Failure mode | Groups | Deltas | What it looks like in the field |
|---|---|---|---|
| **A measurement that did not happen is reported as one that did** | G01, G05, G06, G18 | 67 | A never-run scan graded **C**; a capture that dropped packets rendered conclusive; a leak test that observed nothing reporting "no leak" |
| **Evidence is trusted without establishing its provenance** | G02, G03, G16 | 39 | A snapshot signed by any attacker-chosen key reads VALID; a proxy response accepted as baseline; NS glue queried without a public-IP gate |
| **A value is parsed, compared, or classified incorrectly** | G04, G16, G18 | 38 | Hex garbage surfaced as a typed A record; identical TTLs reported as varying; CDN rotation classified as instability |
| **A platform twin diverged** | G11, G12, G13, G14 | 19 | Same evidence, two verdicts; a state that exists on one platform and not the other |

**The unifying rule for every fix in this document:** a verdict, colour, grade, number, or reassuring word may never precede the evidence that justifies it, and evidence that is missing, degraded, or unmeasured must never render as clean. `inconclusive` (ran, valid data, unresolved) is a decision state in its own right — never a synonym for `untested` (never ran) or `invalidated` (ran, data corrupt).

---

## Blocking precondition — read before scheduling any group

**140 of the 373 delta blocks in `DELTAS.md` also appear in `DELTAS-fixed.md`.** The two ledgers disagree about 140 items, so no group's scope is trustworthy until they are reconciled. Two confirmed instances, each failing in the opposite direction:

- **A036** is filed in `DELTAS-fixed.md` while `Server/syxx_server/leak.py:227-232` still excludes `observations` from the signed payload — a **false fixed entry**.
- **A118** is still open in `DELTAS.md` while `Server/tests/test_routes.py:337-425` fully satisfies it across 16 paid routes — a **missed graduation**.

Reconciliation is assigned to the standing knowledge-synthesizer and must complete before Wave 1 exits. Until then, treat 168 as an upper bound on open work and 112 as an upper bound on Critical.

---

## Per-delta planning contract

Every delta in every group below gets, at planning time, all six fields. A delta missing any field is not planned:

1. **Root cause** — the mechanism, not the symptom.
2. **Targets** — exact `file:line` on **every** affected platform. Twin-bound deltas name both.
3. **Fix** — the change, expressed against the live API.
4. **Failure-case test** — the test that fails before the fix.
5. **Mutation** — the specific edit that must turn that test red afterwards. Per `AGENTS.md` §5, a test that still passes when its subject is deleted does not count as coverage.
6. **Real-run bar** — what must be observed on real data or a real host, not merely compiled. Green tests are not the bar.

**Telemetry is part of the fix, not a follow-up.** Any delta whose fix changes what the product can conclude must also emit the span/attribute that makes the new state observable — `syxx.outcome` ∈ `success|failure|inconclusive|untested|unclassified`, with `syxx.attempted` distinguishing untested from inconclusive. A fix that corrects a verdict but leaves the field blind to it is half-done.

---

## Wave sequence

| Wave | Groups | Deltas | Gate to exit |
|---|---|---|---|
| **W1 Foundations** | G03, G08, G10 | 13 | Gating predicates correct on all surfaces; secrets fail closed; ledger reconciled |
| **W2 Evidence truth** | G04, G06, G16 | 44 | Every parser round-trips hostile input without fabricating a typed value |
| **W3 Determinative core** | G01, G02, G18 | 53 | Golden vectors pass identically on both twins; no intermediate state renders clean |
| **W4 Capture & platform** | G05, G13, G14 | 19 | Partial capture reports degraded, never conclusive; telemetry observable from every surface |
| **W5 Surfaces** | G11, G12 | 16 | No surface renders green for a state the engine did not establish |
| **W6 Commerce & operations** | G07, G09, G17 | 23 | Purchase→activate→use works end to end; server survives hostile load |

W6 may run in parallel with W2–W4. W2 must not begin until W1 exits; W3 not until W2; W5 not until W3.

---

## Groups, in sequence order

Each table below is the complete delta membership for that group. `**C**` marks Critical.


---

## Wave W1

### G03 SSRF & public-IP gating — 4 deltas (4 Critical)

**Root cause.** One public-IP predicate exists in four places and they disagree. `netutil.ip_is_public` (server) normalises IPv4-mapped IPv6 and rejects NAT64/CGNAT; the two client `IPClassifier`s do not implement NAT64; `app.py:1388` hand-rolls a fifth check missing link-local, multicast and CGNAT.
**Targets.** `Server/syxx_server/netutil.py:19-44` · `Server/syxx_server/app.py:1388` · `macOS/Sources/SyxxKit/IPClassifier.swift:80-92` · `Windows/dotNET/Syxx.Kit/IPClassifier.cs:86-95` · `macOS/Sources/SyxxKit/AuthoritativeVantage.swift:92-111` · `Windows/dotNET/Syxx.Kit/AuthoritativeVantage.cs:57-58`
**Fix.** One predicate per tier, NAT64 `64:ff9b::/96` included on all three; delete the hand-rolled copy; gate NS glue IPs before querying them.
**Mutation bar.** Delete the NAT64 branch → a DNS64 network's v6-wrapped internal address classifies `publicRoutable` → red. Remove the glue gate → an NS record pointing at 127.0.0.1 is queried → red.

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| B084 | **C** | SSRF Validation in ContentWarp Blocks CNAME Resolving Hosts on Both Platforms | `macOS/Sources/SyxxKit/ContentWarp.swift:325-347;` |
| D019 | **C** | ContentWarp SSRF Guard TOCTOU/DNS-Rebind + Fails Open on No-Resolver | `macOS/Sources/SyxxKit/ContentWarp.swift:312-347 (fetches :87, :118, :140,` |
| D025 | **C** | Public IPs Classify .other → isProbeable → Unannounced TCP-Connect Scan | `macOS/Sources/SyxxKit/NetworkAddressClassifier.swift:52, 13-20` |
| D075 | **C** | Windows SSRF Guard Fails Open on No-Resolver (macOS Parity Gap, cf. D019) | `Windows/Syxx.Kit/ContentWarp.cs:308-349 (especially 327-329)` |
### G08 Budgets, timeouts & concurrency — 5 deltas (3 Critical)

**Root cause.** Budgets are not derived from the transports beneath them. `perProbeTimeout` is 2.5 s while UDP is 2×2.0 s and DoH is 5–8 s, so the retry that provides KDNS-02 spoofed-retry immunity is structurally unreachable — an attacker who injects one bogus response converts a detectable spoof into a "timeout", i.e. chooses the verdict.
**Targets.** `macOS/Sources/SyxxKit/ProbeBudget.swift:32-41` · `Windows/dotNET/Syxx.Kit/ProbeBudget.cs:63-73` · `DNSResolver.swift:234-241,408` · `DNSResolver.cs:167,1094-1095` · `LAN/CommandRunner.swift:43`
**Fix.** Assert the invariant in code, not prose: `perProbeTimeout >= maxUDPAttempts * udpTimeout` and `>= dohRequestTimeout`. Then either raise the cap or lower the transports — decided by the field data the telemetry plan specifies, not by preference.
**Mutation bar.** A behavioural test asserting the invariant fails **today** (2.5 < 4.0). It must not be resolved by editing the assertion.

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| A028 | **C** | resolve_authoritative / ThreadingHTTPServer Have No Work Budget — Thread- | `Server/syxx_server/resolver.py:384-456; Server/syxx_server/app.py:730-762,` |
| A122 |  | Fatal Authoritative-Socket Errors Spin Forever While Health Stays Listening | `Server/syxx_server/dns_authoritative.py:167-196;` |
| C035 |  | Endpoint Forensics Consumes Its 400-Line Budget on Invalid Lines | `Windows/Syxx.Kit/Forensics/EndpointLogAnalyzer.cs:47-65` |
| D020 | **C** | Name-Decompression O(n^2) on a Hostile TCP Response — CPU DoS | `macOS/Sources/SyxxKit/DNSResolver.swift:737-772, 949-994 (via sendTCP:1078)` |
| D024 | **C** | MDNSScanner Uncapped Sequential Resolves in a Thread That Outlives scan() | `macOS/Sources/SyxxKit/MDNSScanner.swift:125-156, 98, 105` |
### G10 Persistence & secrets at rest — 4 deltas (2 Critical)

**Root cause.** Protection applied at the wrong moment or to the wrong artefacts. `db.py` chmods before `connect()` creates the file, so a fresh deploy is world-readable; WAL/SHM sidecars — which hold the most recent rows including session tokens — are never chmod'd at all.
**Targets.** `Server/syxx_server/db.py:233-244` · `macOS/Sources/SyxxKit/SyxxSettings.swift:59-72` · `Windows/dotNET/Syxx.Kit/SyxxSettings.cs:31,46` · keystores on all three clients
**Fix.** Create the file, then chmod it and both sidecars; `os.umask(0o077)` at start; consent stays fail-closed on undecodable state (landed, guarded by `ConsentFailClosedTests`).
**Mutation bar.** Move the chmod back above `connect()` → a fresh-deploy test observes mode 0644 → red.

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| A066 |  | Server Recursive Walker Lack of Shared Deadline and Depth Limits — Handler Thread Exhaustion | `Server/syxx_server/resolver.py:384-391` |
| A072 | **C** | Server Audit Logs Store Unencrypted Customer PII — Data Policy Violation | `Server/syxx_server/app.py:460,520` |
| A101 |  | Server Authoritative Walk Blocks Connection Handler Threads — Resource Exhaustion | `Server/syxx_server/app.py:598` |
| B141 | **C** | Ubuntu Mirror Lite Invokes Windows DPAPI and Discards a Completed Comparison | `Ubuntu/SyxxUI.Avalonia/ViewModels/MirrorViewModel.cs:37-48;` |

---

## Wave W2

### G04 DNS wire parsing & DNSSEC — 11 deltas (7 Critical)

**Root cause.** Attacker-controlled length and name fields are trusted. A lying `rdlength` surfaces hex garbage as a typed A record; NSEC denial is unverified; TCP length is server-declared.
**Targets.** `Server/syxx_server/resolver.py:210-218,311,376,617` · `dnssec_validator.py:89-92,170-186,231-253,270-329` · `DNSResolver.swift:1045-1051` · `DNSResolver.cs:806-813` · `DNSCapture.swift:343-388`
**Fix.** Validate before typing; disclose dropped records as a `malformed` count rather than silently shrinking the set; implement NSEC window checking; cap TCP length at 16384; thread remaining budget into chain validation.
**Mutation bar.** Feed a record whose `rdlength` exceeds the packet → must yield `malformed`, never a typed value → red if the guard is removed.

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| A041 | **C** | DNSSEC Posture Stops at the First Reachable Vantage | `Server/syxx_server/investigate.py:117-135` |
| A043 | **C** | Server Accepts Any RRSIG as Covering the DNSKEY RRset | `Server/syxx_server/investigate.py:120-131` |
| A060 | **C** | Server DNSSEC Observation Early Exit Bug — False Negative on System Vantage Empty Answers | `Server/syxx_server/investigate.py:117-136` |
| A073 |  | Server DNS Client Lacks EDNS0 Capability Support | `Server/syxx_server/resolver.py:55` |
| A107 |  | Server Insecure TLS Settings Allowed in Production SMTP Services | `Server/syxx_server/mailer.py:54` |
| A123 |  | NS/CNAME `except Exception` Swallows Name Decode Errors Into Hex | `Server/syxx_server/resolver.py:185-193` |
| A127 |  | Passive DNS Drift Thresholds Hardcoded — No Configuration Path | `Server/syxx_server/passivedns.py:53-54` |
| B047 | **C** | RRSIG Validity Judged vs Local Wall Clock — Attacker-Controllable "spoofing" | `macOS/Sources/SyxxKit/DNSSECVerifier.swift:77, 100` |
| B075 | **C** | CNAME Loops and Hop-Cap Exhaustion Are Returned as Ordinary Chains | `macOS/Sources/SyxxKit/CNAMEChainProbe.swift:65-80;` |
| C017 | **C** | CNAME Comparison Uses Raw Wire Hex Instead of Decoded Names | `macOS/Sources/SyxxKit/DNSResolver.swift:762-767;` |
| F018 | **C** | ProphetView DNSSEC .inconclusive Rendered Critical-Red — Unmeasured State | `macOS/Sources/SyxxApp/ProphetView.swift:246-250` |
### G06 Leak detection & collector attestation — 19 deltas (12 Critical)

**Root cause.** Count of unique resolver IPs is treated as evidence of encryption or leakage — a signal structurally incapable of establishing either. Compounded by a wire-format break: the server renamed `leaked`→`queriesObserved` and neither client was updated, so the collector-confirmed path is dead on both desktops.
**Targets.** `Server/syxx_server/leak.py:210-232` · `app.py:1471-1483` · `LeakDetector.swift:214,234-236,364` · `LeakDetector.cs:351,368,491` · `GluttonView.swift:201-218` · `leak-test.html:185-190`
**Fix.** Restore wire compatibility (or version the payload); derive the verdict exclusively from the signed payload; gate macOS on attestation as Windows already does; delete the mock fallback that renders a verdict on fabricated IPs.
**Mutation bar.** Serve `signatureValid: false` → every surface must render `[UNVERIFIED]` → red if the gate is absent.

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| A034 | **C** | Collector :53 Has No Rate Limit / Fresh Conn Per Packet / No Dedup — Write | `Server/syxx_server/dns_authoritative.py:175-190;` |
| A053 |  | `/api/diff` Fetch Error Message Leaked to Client — Internal Network Detail Disclosure | `Server/syxx_server/app.py:73-74 (_diff_content)` |
| A097 | **C** | Server Leak Observation Stores Plaintext Egress IPs — Privacy Violation | `Server/syxx_server/leak.py:51` |
| A111 |  | Leak Verification Is Not an Atomic One-Time Operation | `Server/syxx_server/leak.py:125-170;` |
| A114 |  | Collector Public-Key Endpoint Omits the Rotation Contract Marked Shipped | `Server/syxx_server/app.py:427-438; ROADMAP.md:368-440` |
| B067 | **C** | Glutton Colors `notDetected` Green When Collector Pinning Is Unconfigured | `macOS/Sources/SyxxApp/GluttonView.swift:173-203;` |
| B137 | **C** | macOS CLI Never Sets a Non-Zero Exit Code — a Confirmed Leak Exits 0 | `macOS/Sources/SyxxCLI/** (zero occurrences of `exit(`/`EXIT_FAILURE` in the en` |
| B138 | **C** | Synthetic ECS Option Is Promoted to a Claim That the User's Subnet Leaked | `macOS/Sources/SyxxKit/ECSProbe.swift:39-59;` |
| C026 |  | Leak verdict cannot tell "no leak" from "query never arrived" | `Server/syxx_server/leak.py:125-164 (verify: leaked = len(resolvers) > 0);` |
| C031 | **C** | Collector Protocol Calls Every Successful Recursive Lookup a DNS Leak | `Server/syxx_server/leak.py:138-151;` |
| C032 |  | Windows Glutton Displays Rows From a Different Test Than Its Collector Verdict | `Windows/SyxxUI/ViewModels/AppViewModel.cs:208-220;` |
| C048 | **C** | Ubuntu Glutton Fabricates DNS Encryption State From VPN State | `Ubuntu/SyxxUI.Avalonia/ViewModels/GluttonViewModel.cs:34-50;` |
| C052 | **C** | macOS DashboardView `leakLabel` Shows "No" (Green) When No Leak Data Collected — §1 | `macOS/Sources/SyxxApp/DashboardView.swift:274-281` |
| D068 | **C** | GUI Check buttons stuck spinning; cross-panel trigger leaks isRunning state | `macOS/Sources/SyxxApp/AppModel.swift:202-212 (run()); ProphetView.swift:58-68;` |
| E026 | **C** | Windows Glutton Auto-Runs Collector Against Hardcoded syxx.app/api/leak | `Windows/SyxxUI/AppViewModel.cs:216-219` |
| E049 |  | Windows `LeakDetector.Verify` Bool Collapses 4 Distinct Failure Modes Into `false` | `Windows/Syxx.Kit/LeakDetector.cs:295-314` |
| F013 | **C** | Feature Lock Banner Has Ugly Presentation — Emoji, Red Styling, Parsing Issues | `macOS/Sources/SyxxApp/ (GluttonView, MirrorView, or wherever feature` |
| F024 | **C** | RemoteContentView WKWebView Static Cache Never Evicts (Memory Leak) | `macOS/Sources/SyxxApp/RemoteContentView.swift:25, 63` |
| F030 |  | Server Route Test Setup Leaks a Global Auth Stub When Socket Bind Fails | `Server/tests/test_routes.py:45-67;` |
### G16 ContentWarp & HTML/script comparison — 14 deltas (10 Critical)

**Root cause.** HTML is parsed with regex against adversarial input, and failures are serialised as if they were measurements — a failed fetch becomes a comparable hash, so "different" and "could not fetch" are indistinguishable downstream.
**Targets.** `Server/syxx_server/warp.py` (regex scanner, comment stripping, UTF-8 handling) · `ContentWarp.swift:83,228-268,334-357` · `ContentWarp.cs:42,67,196-207,340-367`
**Fix.** Fail closed with a typed reason (`.noResolver` ≠ "resolves to a non-public IP" — the current message misattributes the cause); never hash a failed fetch; preserve invalid UTF-8 rather than deleting it.
**Mutation bar.** Point the fetch at an unreachable host → assessment must be `inconclusive` with the true reason, never `divergent` → red.

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| A026 | **C** | /api/diff Flags Legit Cross-Host Third-Party Scripts as "injected" — False | `Server/syxx_server/app.py:95-101, 132 (`_diff_verdict`)` |
| A048 | **C** | Warp Baseline Silently Deletes Invalid UTF-8 Bytes | `Server/syxx_server/warp.py:84-86` |
| A049 | **C** | Failed Remote Script Fetch Is Serialized as a Comparable Hash | `Server/syxx_server/warp.py:88-105;` |
| A050 | **C** | Warp Baseline Erases Duplicate Asset References | `Server/syxx_server/warp.py:40-57;` |
| A051 | **C** | Regex Tag Scanner Terminates on `>` Inside Quoted Attributes | `Server/syxx_server/warp.py:16-20, 40-57` |
| A085 |  | Server Webhook Fulfillment Lacks Subscription Reactivation Handlers | `Server/syxx_server/fulfillment.py:103` |
| A093 |  | Server Warp Regex HTML Script Parsing Bypass | `Server/syxx_server/warp.py:16` |
| A094 |  | Server Warp Comment Stripping JS Parsing Errors | `Server/syxx_server/warp.py:71` |
| A118 | **C** | No Test Asserts 402 on Any Paid Route — Deleting the Paywall Leaves the Suite Fully Green | `Server/tests/test_routes.py:53-54; Server/tests/test_warp_raw.py:23; gate at S` |
| A120 | **C** | Deployment Masks Critical Dependency and Backup Failures, Then Declares Success | `scripts/deploy.sh:122-155, 479-515` |
| A121 |  | Deployed DoT Service User Is Never Added to the Certificate ACL Group | `scripts/deploy.sh:189-204, 304-345, 452-468` |
| B148 | **C** | Windows `ContentWarp.IsInlineScriptIdentical` Defaults to `true` — False Secure on | `Windows/Syxx.Kit/ContentWarp.cs:41, 240, 249` |
| D023 | **C** | Client ContentWarp Fetch Has No Timeout + No Script-Count Cap — Slow- | `macOS/Sources/SyxxKit/ContentWarp.swift:87, 118, 140, 211, 111-124` |
| E028 | **C** | isPro Never Set True on Either GUI — Windows Mirror Never Runs, macOS Warp | `macOS/Sources/SyxxApp/AppModel.swift:73; Windows/SyxxUI/AppViewModel.cs:23,` |

---

## Wave W3

### G01 Tri-state & verdict integrity — 19 deltas (15 Critical)

**Root cause.** Boolean returns and non-exhaustive `if` chains where a four-state verdict is required. Fixed this session on both twins for `Untested`; the same shape remains across 19 deltas.
**Targets.** `Synthesis/` on both platforms — `Conclusion`, `VerdictAction`, `ReportCard`, `PlainExplanation`, `Report`, `Synthesizer`; `ProbeRunner` return type on both.
**Fix.** Replace bare `Bool` with the tri-state; make every verdict switch exhaustive so a new case is a compile error, not a silent default; `ProbeRunner` returns a classified result rather than `T?` (four causes currently collapse into one `nil`).
**Mutation bar.** Add a fifth verdict case → every renderer must fail to compile. Delete any `.untested` arm → the guard suite goes red (proven: `MatrixUntestedStateTests`, both platforms).

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| A044 | **C** | Investigation Consensus Drops Reachable Empty Vantages | `Server/syxx_server/investigate.py:67-84` |
| A045 | **C** | Investigation Marks SERVFAIL/REFUSED Responses as `ok` | `Server/syxx_server/investigate.py:48-64` |
| A124 | **C** | DoH JSON `/dns-query?name=` Queries Single Resolver — No 2-of-3 Corroboration | `Server/syxx_server/app.py:1237-1243` |
| B035 |  | Synthesis Tests Use Broad Verdict Whitelists That Pass the Wrong User-Facing | `macOS/Tests/SyxxKitTests/SynthesizerTests.swift:16-22` |
| B039 |  | `ReportTests` Covers Only `local_resolver`; the Cross-Discipline `target_answer` | `macOS/Tests/SyxxKitTests/ReportTests.swift:22-48 (both tests populate only` |
| B074 | **C** | One SVCB Responder Can Produce a Clean `present` or `absent` Result | `macOS/Sources/SyxxKit/SVCBProbe.swift:64-78;` |
| B086 | **C** | Stage-5 Completeness Counts Inconclusive Sections as Fully Observed | `macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift:371-387;` |
| B088 | **C** | Dashboard “Trust Score” Reassures on Threat Verdicts | `macOS/Sources/SyxxApp/DashboardView.swift:239-250;` |
| B090 | **C** | Invalid or Missing Stage-5 Data Does Not Suppress the Legacy Verdict Surface | `macOS/Sources/SyxxApp/ConclusionsView.swift:23-31;` |
| C015 | **C** | Same-CDN-Provider Softening Erases a Real Authoritative Divergence | `macOS/Sources/SyxxKit/AuthoritativeVantage.swift:166-172;` |
| C025 | **C** | Vantage baseline queried over plaintext UDP (attacker-controllable) | `macOS/Sources/SyxxKit/VantageComparer.swift:86-112, 170-173 (queryOutcome -> D` |
| C046 | **C** | Unanimous-Tampering Detector Bails Out On Exactly The Two Conditions That Prove Interception | `Windows/Syxx.Kit/VantageComparer.cs:229, 232-234, 244-247;` |
| C051 | **C** | Windows VantageComparer `Verdict` Uses Three Independent Bools Instead of Tri-State Enum | `Windows/Syxx.Kit/VantageComparer.cs:8-19, 269-274` |
| D029 | **C** | macOS Pro Dimension Path Fires on Caller isPro Bool Alone (Even Nil Key) | `macOS/Sources/SyxxKit/ProbePlan.swift:115-120;` |
| D076 |  | Windows `VpnActive` Bool Loses TriState Nuance in Sentinel.Diff | `Windows/Syxx.Kit/Trust.cs:158, 172, 220-222;` |
| E051 |  | macOS `isVPNActive()` Bool Wrapper Collapses Unknown — Used by Sentinel Snapshot | `macOS/Sources/SyxxKit/NetworkKeyState.swift:331-334;` |
| F033 | **C** | Windows `FirstRunScan.Track(bool)` Conflates "Inconclusive" With "Produced" | `Windows/Syxx.Kit/FirstRunScan.cs:126-129, 135-138` |
| F034 | **C** | Windows `SyxxSettingsStore.Save` Bool Cannot Distinguish Failure Modes | `Windows/Syxx.Kit/SyxxSettings.cs:51-61` |
| F035 | **C** | Windows `MonitorScheduler.IsRunning` Bool Hides Starting/Stopping/Crashed States | `Windows/Syxx.Kit/MonitorScheduler.cs:28, 104-129` |
### G02 Attestation, signing & evidence provenance — 21 deltas (16 Critical)

**Root cause.** Signature validity is conflated with trustworthiness, and the trust set is never supplied. `verifySnapshotDetailed(snap)` is called bare at `AppModel.swift:1580` and `SyxxWitness.swift:185`, so `.untrustedSigner` is unreachable and a snapshot signed by any attacker-chosen key renders VALID (B081).
**Targets.** `WitnessSigner.swift:152-240` · `WitnessSigner.cs:166-280` · `AppModel.swift:1580` · `SyxxWitness.swift:185` · `leak.py:227-232` (A036) · `Synthesizer.swift:299-310` / `.cs:406-413` (B092, unsigned Dimension treated as decisive)
**Fix.** Pass a trust set at every call site; render four typed states (valid+trusted / valid-untrusted / invalid / unknown); include observations inside the signed payload or return only the signed subset; cap unsigned responses at non-decisive.
**Mutation bar.** Sign with an untrusted key → must render `valid-untrusted`, never VALID → red today, because the state is currently unreachable.

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| A031 | **C** | beta_signups PII Never Purged — Unbounded Growth + Weekly Re-email | `Server/syxx_server/db.py:421-456; Server/syxx_server/config.py:130-137` |
| A042 | **C** | Server RRSIG Window Check Ignores Signature Inception | `Server/syxx_server/investigate.py:102-114` |
| B043 | **C** | Windows Glutton Drops the ECS Leak Signal — Early `return` in the Leak | `Windows/Syxx.Kit/Synthesis/Synthesizer.cs:566-604 (Glutton);` |
| B053 | **C** | WitnessView Conflates Ed25519 Signature-Validity With a Clean NETWORK | `macOS/Sources/SyxxApp/WitnessView.swift:131-145` |
| B064 | **C** | DNSSEC Back-Compat Bool Reports Expired / Not-Yet-Valid Signatures as `true` | `macOS/Sources/SyxxKit/DNSSECVerifier.swift:68-73;` |
| B066 | **C** | Glutton Shows a Green Valid Signature Without Showing Key Trust | `macOS/Sources/SyxxApp/GluttonView.swift:188-197;` |
| B081 | **C** | Witness verify accepts any unpinned self-signed key as "VALID" | `macOS/Sources/SyxxKit/WitnessSigner.swift:117-141; macOS/Sources/SyxxWitness/S` |
| B087 | **C** | Witness Launchers Convert Configuration Changes Into Fake Answer Drift and | `macOS/Sources/SyxxCLI/SyxxCLI.swift:671-699;` |
| B093 | **C** | macOS GUI Witness Converts Inconclusive Comparison Into Clean Witness Evidence | `macOS/Sources/SyxxApp/AppModel.swift:880-892;` |
| B100 |  | Windows Witness Displays VALID Without the Network-Evidence Quality State | `Windows/SyxxWitness/Program.cs:76-87,112-125;` |
| B135 | **C** | `SignedIncidentReport` Prints "MATCHES — Answers match reference" for a Comparison That | `macOS/Sources/SyxxKit/Synthesis/SignedIncidentReport.swift:59-67;` |
| B145 |  | `_zone_is_signed` Only Checks `answers` Section for RRSIGs — Misses Authority/Additional | `Server/syxx_server/resolver.py:589-594` |
| B147 | **C** | Windows Empty-Field AnswerDrift Fabricates Spurious Witness Evidence From Config Changes | `Windows/Syxx/Program.cs:842; Windows/Syxx.Kit/Synthesis/Synthesizer.cs:760-766` |
| C045 |  | Exfil Analyzer Turns a Real Single Signal Into False Benign/Clean Evidence | `macOS/Sources/SyxxKit/Forensics/ExfilAnalyzer.swift:61-89;` |
| D001 | **C** | macOS Trial HMAC Key Publicly Derivable from IOPlatformUUID | `macOS/Sources/SyxxKit/LicenseManager.swift:203-205` |
| D030 | **C** | Baseline/Sentinel Snapshot JSON Loaded UNSIGNED — Local Attacker Suppresses | `Windows/Syxx.Kit/Trust.cs:123-139, 239-250;` |
| D050 |  | Witness Verification Returns Bool Instead of Tri-State | `macOS/Sources/SyxxKit/WitnessSigner.swift:117-166 (verifySnapshot` |
| D063 | **C** | Authoritative Leak DNS Server Signs Observations From Non-Query/Malformed Packets | `Server/syxx_server/dns_authoritative.py:40-99,175-190;` |
| E012 |  | Console.OutputEncoding Failure Silently Swallowed Across All Windows CLIs | `Windows/Syxx/Program.cs:15; Windows/SyxxWitness/Program.cs:13` |
| E017 | **C** | Answer-Stability Sub-Signal Diverges — macOS Drops the TTL-Anomaly Evidence | `macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:118-131;` |
| E047 | **C** | Windows CLI Cage Prints Unconditional `[SUCCESS] Sniffer started` Before Any Packet — No `No pac | `Windows/Syxx/Commands/CageCommand.cs:72 (eager `[SUCCESS]` print), :98-149 (ca` |
### G18 Scoring heuristics & classification thresholds — 13 deltas (12 Critical)

**Root cause.** Heuristics that convert absence of measurement into a positive classification: identical TTLs reported as varying, CDN rotation as instability, a failed amplification measurement as `recursiveLowAmp`, NXDOMAIN/NODATA as active enforcement.
**Targets.** `Synthesizer.swift` / `.cs` dampener and referral logic (B130, B133, B134) · answer-stability and TTL classifiers (B077-B080) · filtering probe (B139) · lookalike detector (B140) · `version.bind` gating (C010) · latency mapping (E016)
**Fix.** Every classifier must distinguish "measured and negative" from "not measured". A dampener may not apply at reliability 1.0 to evidence it cannot see; `dataRegime: fullData` may not be emitted when a section is empty.
**Mutation bar.** Feed each classifier an empty/failed input → must yield inconclusive, never a positive class → red.

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| B040 | **C** | `target_answer` Is Set-Valued but Merged as Scalar — Every Multi-A/AAAA Domain | `macOS/Sources/SyxxKit/Synthesis/Report.swift:34 (`isConflicting =` |
| B057 | **C** | Wildcard Reference 1.1.1.1 Can Equal Resolver-Under-Test — nxHijack | `macOS/Sources/SyxxKit/Probes/WildcardSynthesisProbe.swift:74-78;` |
| B077 | **C** | Two Identical Nonzero TTLs Are Reported as “TTLs Vary” | `macOS/Sources/SyxxKit/TTLAnomalyProbe.swift:74-79;` |
| B078 | **C** | Answer Stability Ignores Intermediate TTL Increases | `macOS/Sources/SyxxKit/AnswerStabilityProbe.swift:81-87;` |
| B079 | **C** | Legitimate Disjoint CDN Rotations Are Classified Unstable | `macOS/Sources/SyxxKit/AnswerStabilityProbe.swift:89-96;` |
| B080 | **C** | Failed Amplification Measurement Becomes `recursiveLowAmp` | `macOS/Sources/SyxxKit/OpenResolverProbe.swift:74-92;` |
| B130 | **C** | A Decisive Act Suppresses the Referral Gate Unconditionally — Including Hard Gates and | `macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift:375; Windows/Syxx.Kit/Sy` |
| B133 | **C** | Dampeners Apply at Reliability 1.0 While the Evidence They Suppress Is Discounted to 0.5 | `macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift:247, compare :197-199;` |
| B134 | **C** | `dataRegime: fullData` and 6-of-6 Completeness Emitted When Every Section Decided Nothing | `macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift:444, :452-454; predicate` |
| B139 | **C** | Filtering Probe Treats Ordinary NXDOMAIN and NODATA as Active Enforcement | `macOS/Sources/SyxxKit/FilteringProbe.swift:45-71, 74-102;` |
| B140 |  | Lookalike Detector Claims Transposition Support but Misses Adjacent Swaps | `macOS/Sources/SyxxKit/LookalikeDetector.swift:3-7, 79-101, 149-158;` |
| C010 | **C** | version.bind Escalated From Tilt-Only to Section-Gating on Windows — | `macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:206-212;` |
| E016 | **C** | Resolver-Latency Status Mapping Diverges — Windows Reads a Slow Resolver as | `macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:109-115;` |

---

## Wave W4

### G05 Capture completeness (Cage) — 16 deltas (6 Critical)

**Root cause.** Partial capture is permitted to produce a conclusive report. One parsed record among N failures still prints a normal verdict; IPv6 extension headers and IPv4 fragments are invisible; the buffer tail is sliced instead of the UDP payload.
**Targets.** `DNSCapture.swift:343-388,462,606` · `DNSCapture.cs:196-210,278-279,642` · `DNSCaptureReportGenerator.swift:46,192,275` / `.cs:48,79,180` · `SyxxCage.swift:177-190,227-250,408` · `CageCommand.cs:151-170`
**Fix.** Define a completeness threshold and propagate a degraded state into the generator; disclose unparseable counts; walk bounded extension headers; reject fragments with a caveat; slice exactly the UDP payload; close the D065 symlink race by keeping the safe fd (`fchmod`/`fchown`) instead of re-opening by path.
**Mutation bar.** Capture with 90% malformed packets → report must read degraded/inconclusive → red if the threshold is removed.

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| C023 | **C** | Capture Parser Returns Partial Reports When DNS Counts Cannot Be Satisfied | `macOS/Sources/SyxxKit/DNSCapture.swift:255-297;` |
| C024 | **C** | Capture Uses Buffer Tail Instead of UDP Length as DNS Payload | `macOS/Sources/SyxxCage/SyxxCage.swift:221-227;` |
| C027 |  | Windows ParseDNSPacket Never Sets PayloadBytes → ResponseSizeProbe Dead | `Windows/Syxx.Kit/DNSCapture.cs:369-381 (ParseDNSPacket return);` |
| C029 |  | DNS Telemetry Reports Ethernet Padding as Part of DNS Payload Wire Size | `macOS/Sources/SyxxKit/DNSCapture.swift:253,355, Windows/Syxx.Kit/DNSCapture.cs` |
| C033 |  | Cage GUI and Raw-Query Export Reintroduce Unbounded Whole-Capture Memory | `macOS/Sources/SyxxApp/AppModel.swift:647-699;` |
| C040 |  | macOS Cage Drops JSON-Encoding Failures Without a Loss Counter | `macOS/Sources/SyxxCage/SyxxCage.swift:255-262` |
| C041 | **C** | Windows Cage Capture Exception Still Falls Through to Ordinary Reporting | `Windows/SyxxCage/Program.cs:89-167` |
| C042 |  | Cage Infers Direction and Resolver Identity From Attacker-Controlled DNS QR | `macOS/Sources/SyxxCage/SyxxCage.swift:239-260;` |
| C043 |  | Cage Is Blind to Encrypted DNS but Presents General DNS-Capture Coverage | `macOS/Sources/SyxxCage/SyxxCage.swift:117;` |
| C044 | **C** | macOS Cage Capture Error Also Falls Through to a Normal Partial Report | `macOS/Sources/SyxxCage/SyxxCage.swift:146-165;` |
| D022 | **C** | "Streaming" Log Readers OOM on Newline-Free Line; Aggregation Dicts | `macOS/Sources/SyxxKit/Cage/DNSCaptureReportGenerator.swift:289-309;` |
| D065 | **C** | Privileged macOS Cage Output Has a Symlink-Race Root File-Clobber Primitive | `macOS/Sources/SyxxApp/AppModel.swift:606-615,704-717;` |
| E035 |  | Windows GetProcessDetails Discards PID+Name When Path Is Null — Partial | `Windows/Syxx.Kit/DNSCapture.cs:525-553 (GetProcessDetails);` |
| E036 |  | Windows LookupProcessInfo Shells Out to cmd.exe Per Unique Port — | `Windows/Syxx.Kit/DNSCapture.cs:487-523 (LookupProcessInfo);` |
| E037 |  | Windows Raw Socket Captures All IP Traffic — No Kernel-Level Packet Filter | `Windows/SyxxCage/Program.cs:219-223 (IOControlCode.ReceiveAll);` |
| E046 |  | Windows DNS Capture Process Attribution Always Fails Due to Incorrect netstat Regex Search | `Windows/Syxx.Kit/DNSCapture.cs:494` |
### G13 Telemetry & observability — 1 deltas (1 Critical)

**Root cause.** Telemetry violated the doctrine it exists to enforce: unconditional `status = .ok`, skipped probes emitting no span, verdicts absent from spans. Partly fixed this session; the export path remains unbuilt.
**Targets.** `SyxxTracer.swift` (4 spans still record no outcome) · `SyxxTracer.cs:240,263,279,394` (still hard-codes Ok/Error) · `SynthesisEngine.swift:34-37` (discards its span) · bootstraps for macOS CLI, Windows CLI, Ubuntu, server · `DNSResolver.swift:250,312,386` (exports user DNS query names — PII)
**Fix.** Derive status from `syxx.outcome` everywhere; stamp `unrecorded` when a body sets none; bootstrap the four dark surfaces; replace `dns.hostname` with a class token; build the `SyxxDiagnostics` sink so the core can report at all.
**Mutation bar.** A span whose body records no outcome must not export Ok. Grep the collector for a private domain after a scan → zero hits.

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| E041 | **C** | macOS Route Trace Lacks Graceful Streaming and Timeout Handling — Deadlock Risk | `macOS/Sources/SyxxRoute/SyxxRoute.swift:37-63;` |
### G14 Cross-platform parity & packaging — 2 deltas (1 Critical)

**Root cause.** Twins drift because nothing fails when they do.
**Targets.** `Windows/dotNET/SyxxUI/Views/ConclusionsView.xaml.cs:252-295` and `DashboardView.xaml.cs:82-88` (dangling archived types — SyxxUI does **not** compile; this is not a toolchain gap) · `IsDNSEncrypted()` stub (B146) · CI legs
**Fix.** Re-derive the Windows GUI onto `DeterminativeResult`, matching macOS and the already-migrated Ubuntu view model; add a shared golden vector that fails CI on divergence.
**Mutation bar.** Change a verdict mapping on one platform only → the shared vector fails on the other.

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| B146 | **C** | Windows `IsDNSEncrypted() => false` Stub Kills Sentinel Change Detection and | `Windows/Syxx.Kit/NetworkKeyState.cs:383; Windows/Syxx.Kit/Trust.cs:159,173,223` |
| D073 |  | macOS Bundle Build Suppresses a Missing Cage Backend and Still Reports Success | `macOS/bundle-app.sh:6-22, 54-55;` |

---

## Wave W5

### G11 GUI verdict surfacing & accessibility — 14 deltas (10 Critical)

**Root cause.** Surfaces assert conclusions the model never established: `DashboardView` prints PASSED on non-nil, `ProphetView` claims "N/N agree" from a reachability count, `GluttonView` colours `.notDetected` green ungated by attestation.
**Targets.** `DashboardView.swift:388-401` · `ProphetView.swift:120-157` · `GluttonView.swift:201-218` · `ConclusionsView.xaml.cs` · Avalonia views · rune labels and colour-only indicators across all three
**Fix.** Render from the verdict, never from presence-of-object; text alternative for every colour-only signal; accessibility labels on every interactive element and rune.
**Mutation bar.** Feed an inconclusive conclusion → no surface may show green or a pass word → red.

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| A126 |  | DoH JSON Returns `Status: 2` for All Upstream Failures — Indistinguishable From Genuine | `Server/syxx_server/app.py:1242` |
| B041 | **C** | `renderText`/`RenderText` Silently Drops `conclusion.advisories` — the | `macOS/Sources/SyxxKit/Synthesis/Report.swift:111-155 (`renderText` renders` |
| B042 | **C** | `renderText`/`RenderText` Drops `conflict` and `uncertainty` — the Documented | `macOS/Sources/SyxxKit/Synthesis/Conclusion.swift:26-30 (doc: `conflict` is` |
| B076 | **C** | Identical Zero TTLs Are Labeled Independent Caching | `macOS/Sources/SyxxKit/TTLAnomalyProbe.swift:67-79;` |
| B096 |  | Windows Prophet Record-Type Picker Is Decorative and Always Audits A | `Windows/SyxxUI/Views/ProphetView.xaml:32-39;` |
| C036 |  | Exfil Analyzer Still Uses a Last-Two-Labels Registrable-Domain Heuristic | `macOS/Sources/SyxxKit/Forensics/ExfilAnalyzer.swift:43-88;` |
| C049 | **C** | macOS Prophet Discards the User-Selected Resolver From Displayed and Synthesized Evidence | `macOS/Sources/SyxxApp/ProphetView.swift:54-56, 125-163;` |
| D033 | **C** | macOS Remote Content Allows Every `.other` Navigation | `macOS/Sources/SyxxApp/RemoteContentView.swift:83-103` |
| D034 | **C** | Windows WebView Origin Check Uses Substring Matching | `Windows/SyxxUI/Views/RemoteContentPanel.xaml.cs:40-48` |
| D058 | **C** | macOS WebView Navigation Delegate Lifetime Security Bypass — Missing updateNSView Coordinator Sy | `macOS/Sources/SyxxApp/RemoteContentView.swift:67-69` |
| D070 |  | macOS GUI WebView (BetaView / What's New) renders blank white panel; no | `macOS/Sources/SyxxApp/RemoteContentView.swift:38-61 (WebView config +` |
| F019 | **C** | DashboardView "Queries seen" / Resolver IP Green Even With No Data — Absence | `macOS/Sources/SyxxApp/DashboardView.swift:81, 266-268, 70-72` |
| F021 | **C** | macOS “Early Access” Update Toggle Is Disconnected | `macOS/Sources/SyxxApp/UpdatePanel.swift:9, 46-61, 155-166` |
| F025 | **C** | Windows Startup Dialog Race Can Permanently Lose a Critical Broadcast | `Windows/SyxxUI/MainWindow.xaml.cs:30-41,44-57,65-101` |
### G12 CLI surfacing & exit codes — 2 deltas (2 Critical)

**Root cause.** Exit codes and machine-readable output are advertised but unreachable: every subcommand returns `Void`, so `syxx nslook` exits 2 whatever happens, and `--json`/`--verbose` are parsed then discarded.
**Targets.** `SyxxCLI.swift:7-10,37,175-190` and all 15 command entry points · `Windows/dotNET/Syxx/Program.cs:69`
**Fix.** Commands return an exit code; wire the flags through `handleGlobalFlags`; align the version string (`printVersion()` says 1.0.0, `SyxxConfig.version` says 2.0.0).
**Mutation bar.** Script asserting `syxx prophet` on a clean network exits 0 and on a tampered one exits 1 → red today.

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| B056 | **C** | CLI Mixes the Local Resolver Into the "known-modern" SVCB/TTL Set → False | `macOS/Sources/SyxxCLI/SyxxCLI.swift:290, 293 (contract` |
| F032 | **C** | Unified macOS CLI Reports Stale Hardcoded Version 1.0.0 Inside Version 2.0.0 Bundle | `macOS/Sources/SyxxCLI/SyxxCLI.swift:791-793;` |

---

## Wave W6

### G07 Licensing, entitlement & commerce — 15 deltas (7 Critical)

**Root cause.** The purchase loop is open at both ends: features are promised the server does not grant (a "3-DAY FREE TRIAL" with no `trial_period_days`), and licences are unenforceable (no machine binding, so one key works on unlimited machines while the UI claims "1 of 3 activations").
**Targets.** `stripe_checkout.py:94-103` · `fulfillment.py:29-33,67-170` · `licensing.py` · `LicenseManager.swift:395-397,409-418` / `.cs:447-449,469-476` · `LicenseView.swift:201` (hardcoded fake machine ID) · EULA surfaces on three platforms
**Fix.** Add the trial period or remove the promise; machine binding via Keygen `maxMachines`; billing portal; `charge.refunded`/`dispute.created` handling; port the server's Keygen signature verification to both clients; unify the three EULAs.
**Owner-gated:** device count, entity name, governing law, refund posture. Cannot be planned to completion without these.
**Mutation bar.** Activate the same key on a fourth machine → must be refused → red today.

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| A025 | **C** | Stripe Customer→License Single-Slot Overwrite — Lifetime Buyer Lockout / | `Server/syxx_server/fulfillment.py:97-98, 114-118` |
| A035 | **C** | /api/checkout Public+Unauth Mints a Fresh Stripe Session Per Request — | `Server/syxx_server/app.py:456-473;` |
| A081 | **C** | Server Keygen License Metadata Plaintext Email Logging — Privacy Policy Violation | `Server/syxx_server/fulfillment.py:31` |
| A084 |  | Server Stripe Checkout Idempotency Keys Bypass Session De-duplication | `Server/syxx_server/stripe_checkout.py:80` |
| A099 | **C** | Server Permissive CORS on Licensed Endpoints — Cross-Origin Security Vulnerability | `Server/syxx_server/app.py:143` |
| A125 |  | `data.get("meta")` After try/finally Crashes 500 on Non-Dict Keygen JSON | `Server/syxx_server/licensing.py:267, 289` |
| A128 |  | Stripe Webhook Timestamp Tolerance Lacks NTP-Skew Defense | `Server/syxx_server/stripe_checkout.py:130` |
| A130 | **C** | `_verify_validate_key_response` Collapses 7+ Distinct Failure Modes Into Bare `False` | `Server/syxx_server/licensing.py:119-229` |
| D042 |  | Silent License Save Failure Shows False "SUCCESS" to User | `macOS/Sources/SyxxCLI/SyxxCLI.swift:72` |
| D053 |  | Server Offline License Validation Trusts DB Records Without | `Server/syxx_server/licensing.py:194-202 (_validate_offline);` |
| D071 |  | macOS GUI "Activate" button is a dead end — opens syxx.app in browser | `macOS/Sources/SyxxApp/LicenseView.swift:125-131 (Activate button);` |
| E048 |  | macOS Activation Overwrites a Known-Good Credential Before Validation | `macOS/Sources/SyxxApp/AppModel.swift:326-369, 513-537` |
| E050 |  | Windows `LicenseManager.ValidateLicenseAsync` Bool Wrapper Hides Timeout/NetworkError at | `Windows/Syxx/Program.cs:105; Windows/Syxx.Kit/LicenseManager.cs:492-496` |
| F016 | **C** | LicenseView Fabricated Machine ID / Activation Count / "CURRENT" Pill | `macOS/Sources/SyxxApp/LicenseView.swift:129, 31` |
| F022 | **C** | Shipped EULA Is Explicitly a Placeholder | `macOS/Sources/SyxxApp/EULAPanel.swift:81-110` |
### G09 Server routing & API surface — 5 deltas (2 Critical)

**Root cause.** Endpoints report success they did not perform and expose more than intended. `_route_revoke_api_key` returns `{"revoked": true}` unconditionally — for a nonexistent key and for another tenant's key alike.
**Targets.** `Server/syxx_server/app.py` — revoke handler, pagination coverage (monitors and baseline unpaged), error taxonomy (no machine-readable `code`), passive-DNS ingestion inflation (A095)
**Fix.** Return real row counts; paginate the remaining collections; add a `code` field; bound ingestion.
**Mutation bar.** Delete a nonexistent key → response must say nothing was deleted → red today (the monitor twin was fixed this session; the key path was not).

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| A095 |  | Server Investigate Path Passive DNS Database Ingestion Inflation | `Server/syxx_server/investigate.py:58` |
| A116 | **C** | `_EMAIL_RE` Accepts a Comma-Bearing Address — Unauthenticated Recipient-Count Injection on `/api | `Server/syxx_server/app.py:55; Server/syxx_server/mailer.py:42` |
| B095 |  | Windows Endpoint Indicators Are Bypassed by Ordinary Case Variation | `Windows/Syxx.Kit/Forensics/EndpointLogAnalyzer.cs:18-38,153-154,220-224` |
| D038 | **C** | Dimension Base URL Accepts Plain HTTP and Arbitrary Origins | `macOS/Sources/SyxxApp/DimensionService.swift:36-38, 57-64;` |
| F027 |  | Shipped What’s-New Pages Contradict the Self-Contained CSP Contract | `Server/syxx_server/app.py:409-422;` |
### G17 Server deployment & service hardening — 3 deltas (2 Critical)

**Root cause.** Deployment reports success it has not achieved. The Sentry install swallowed a hard failure with `|| echo` and continued, burying the true cause under a misleading image-resolution error; `useradd` failure silently falls back to running as root.
**Targets.** `scripts/deploy.sh:199-204` (root fallback), systemd hardening directives, `:678-706` (Sentry install — fail-closed and flag detection landed this session) · DoT accept loop (A119) · service-user ACL (A121) · socket error spin (A122)
**Fix.** Fail closed on every install step; add the missing systemd directives (`RestrictAddressFamilies`, `SystemCallFilter`, `MemoryDenyWriteExecute`, `ProtectKernel*`); never silently degrade to root.
**Mutation bar.** Force `useradd` to fail → deploy must abort, not continue as root → red today.

| ID | Critical | Delta | Location |
|----|----------|-------|----------|
| A058 | **C** | Authoritative DNS Server Lacks IPv6 Support | `Server/syxx_server/dns_authoritative.py:127-151` |
| A119 | **C** | DoT TLS Handshake Blocks the Sole Accept Loop Before Connection Limits Apply | `Server/syxx_server/dot_listener.py:37-46, 59-65, 139-151;` |
| D074 |  | macOS Bundle Advertises an Unsupported Deployment Floor | `macOS/bundle-app.sh:29-46; macOS/Package.swift:4-8; MEMORY.md:283-284` |
