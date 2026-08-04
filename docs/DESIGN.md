# Syxx Functional Design

A classification of Syxx's features into six functional disciplines. Each discipline lists what
has shipped, what's still open in `ROADMAP.md`, and what's proposed in `PROPOSALS.md`, so the
three documents can be read together: `DELTAS.md` catalogs defects, `ROADMAP.md` tracks their
remediation, `PROPOSALS.md` proposes new capability, and this document shows how it all fits
together by mission function rather than by file or tool.

> **Packaging (2026-07) — unified CLI binary.** The former 12 standalone command-line tools
> (`syxx nslook`, `syxx mirror`, and the rest) are now **subcommands of a single `syxx` binary**:
> `syxx nslook`, `syxx mirror`, `syxx leak`, `syxx witness`, `syxx key`, `syxx route`, `syxx lan`,
> `syxx forensics`, `syxx sentinel`, `syxx cage`, `syxx warp`, plus an interactive guided mode when
> run with no arguments. The graphical app ships separately as `syxx-gui`; its Cage capture re-execs
> the unified `syxx` binary with the `cage` subcommand under an admin prompt (macOS: osascript /
> sudo; Windows: UAC `runas`). Tool names below name the subcommand that surfaces each capability.

## Desktop startup and presentation contract

All three graphical clients share the same startup state machine:

1. Load and validate the bundled agreement.
2. Require acceptance of the exact agreement version and SHA-256 digest.
3. Persist the acceptance timestamp, then read the saved state back exactly.
4. Run guided setup without starting a network scan.
5. Persist and verify the setup choices.
6. Enter the application shell at What’s New.

Absent, corrupt, mismatched, or unreadable state cannot enter the shell. Telemetry, crash reporting,
update checks, notifications, license checks, tray services, and encrypted-DNS application are
created only after the ready state, and only when the verified settings permit them. The canonical
agreement is `Legal/EULA/Syxx-EULA.txt`; platform copies are packaging inputs whose bytes must match
that source.

The desktop typography roles are also cross-platform. Cinzel is used for display headings, Archivo
for UI and body copy, Cormorant SC for deck copy, EB Garamond Italic for emphasis, and JetBrains
Mono for technical text. These roles come from the supplied type specimen. They do not authorize
layout, color, control, or general visual redesign.


## 1. DNS Resolution & Protocol Integrity

*Is a single query/response exchange itself trustworthy?*

- **Shipped:** `DNSResolver` (UDP/TCP/DoH engine), `DNSSECVerifier`, `syxx nslook`, F1
  EDNS0 Capability Probe (`EDNS0Probe`), F2 CHAOS/version fingerprinting (`VersionBindProbe`),
  F3 TXID & Source Port Entropy Audit (`StubEntropyProbe`), `ResolverLatencyProbe`,
  `AnswerStabilityProbe`, `WildcardSynthesisProbe`, `DenialOfExistenceProbe`, `DualStackProbe`
- **Open in ROADMAP:** None — CUR-001 (QR=0 rejection), CUR-002 (`resolveSecurely` returns
  `inconclusive`, not throws), and CUR-005 (typed DNS statuses in macOS launcher) are all
  **Closed** (CF-04, CF-05).
- **Proposed:** A2 (real DS→DNSKEY→RRSIG chain validation, replacing presence-only checks —
  CF-08), A8 (DoT/DoQ + per-answer transport provenance in nslook), B5 (`SyxxDeepVerify` —
  authoritative zone auditing)



## 2. Tampering & Censorship Detection

*Is something actively interfering with what I asked for?*

- **Shipped:** `VantageComparer`, `ContentWarp`, `syxx mirror`, `syxx warp`, `SyxxCLI` Option A,
  F4 TTL Anomaly Detection (`TTLAnomalyProbe`), F7 Full CNAME Chain comparison (`CNAMEChainProbe`),
  `AuthoritativeVantage`, `SVCBProbe`
- **Open in ROADMAP:** None — CUR-004 (degraded comparison wording) is **Closed** (CF-05).
- **Proposed:** A1 (multi-transport corroboration — diverging encrypted vs. plaintext answers as
  tamper evidence), A5 (Warp SRI/TLS/HSTS inspection), A6 (Mirror historical baseline/drift), B2
  (Censorship Fingerprinting Engine — classify *how* something is blocked)

## 3. Network & Path Diagnostics

*What does my local network and the path to the destination actually look like?*

- **Shipped:** `NetworkKeyState`, `IPClassifier`, LAN cartography (`LANCartographer`, `syxx lan`),
  `syxx key`, `syxx route`, `EncryptedTransportProbe`, `OpenResolverProbe`
- **Open in ROADMAP:** CUR-006 (`syxx route` still emits raw traceroute while claiming route-shift
  and path-anomaly analysis — CF-07 pending).  CUR-003 (baseline unusable on truncation) is
  **Closed** (CF-06).
- **Proposed:** A4 (structured traceroute with baseline diffing — CF-07), A9 (continuous
  `NetworkKeyState` watchers), B3 (`SyxxBridge` — local DoH proxy sitting in the path), B6
  (`SyxxFlow` — traceroute-correlated network heat maps)

## 4. Privacy Leak Detection

*Is my VPN/tunnel actually keeping my DNS queries private?*

- **Shipped:** `LeakDetector`, `syxx leak`, `SyxxCLI` Option B, F10 EDNS Client Subnet (ECS)
  echo/leak detection (`ECSProbe`)
- **Open in ROADMAP:** None — the collector-confirmed leak protocol is shipped (CF-01).  The
  basic attestation (observed resolver IPs, Ed25519-signed for third-party verifiability) is
  complete on both platforms and the server.  `LeakDetector` now includes a `verify()` method
  that checks the collector's Ed25519 signature offline.
- **Proposed:** A3 (enriched collector egress attestation — bind observations to source IP
  + interface, protocol/port, first-seen timing, canary-label↔token linkage; CF-09)

## 5. Capture, Forensics & Attribution

*What DNS traffic is flowing, and which process/service is responsible?*

- **Shipped:** `DNSCapture`, `DNSCaptureReportGenerator`, `syxx cage`, `syxx forensics`,
  `ResponseSizeProbe` (F9), `MonitorReportAnalyzer` (capture → determinative-engine bridge)
- **Open in ROADMAP:** None — all three P2 capture/performance items are done.  macOS capture
  rewritten with **libpcap** (CF-02), replacing the non-functional raw-socket approach.
  Honesty guards: `[SUCCESS]` prints after first packet; zero records → `INCONCLUSIVE`.
- **Proposed:** A10 (EDNS-aware, low-memory streamed capture), B8 (`SyxxIdentity` — real-time
  process-level DNS attribution)


## 6. Trust Infrastructure: Evidence, Identity & Platform

*Can the evidence, credentials, and the app itself be trusted?*

- **Shipped:** `WitnessSigner` (Ed25519, CF-01), `syxx witness`, `LicenseManager` (Keychain-backed
  license key), `Sentinel`, `AnswerLedger`
- **Open in ROADMAP:** None — all three P1 items are done (credential storage, trial-state
  hardening, witness-signing hardening).  HMAC→Ed25519 upgrade (CF-01) makes all signed evidence
  third-party-verifiable.
- **SD-25 (LicenseManager CCHmac):** Retained — the `hmacSHA256` call at `LicenseManager.swift:304`
  signs the trial-start timestamp with the device fingerprint for **local Keychain anti-tamper**
  only.  No third party ever verifies this signature; symmetric MAC is the correct tool.
- **Proposed:** Part C (service-layer refactor of `SyxxKit` for CLI+GUI parity), A7 (witness
  transparency-ledger publication), B1 (`SyxxSentry` continuous daemon), B4 (`SyxxRegistry`
  resolver-health ledger), B7 (`SyxxAutomation` scripting), B9 (`SyxxPortal` dashboard), B10
  (`SyxxAudit` compliance reports)

## Determinative synthesis (cross-cutting)

*Given the six disciplines' outputs, what is the one honest conclusion — and how sure are we?*

Each of the six checks answers its own question; the **synthesis engine** joins those answers into a
single conclusion for the end user (the goal of Delta-of-the-Week #1). Like the GUI, it is
cross-cutting — not a seventh launcher option.

- **Engine (macOS, current ground truth — owner directive 2026-07-21; the prior DST/weight-rubric
  engine with its confidence/probability distribution and referral thresholds is RETIRED and
  deleted on macOS, never to be rebuilt):** `MatrixDeterminativeEngine`
  (`macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift`) evaluates an `EvidenceBundle`
  (section usability + observed indicators) against a `MatrixSpec` of `DiagnosisSpec` rules and a
  fail-closed `ThresholdProfile`. The thin bridge is `SynthesisEngine.swift`
  (`mapEvidenceToIndicators`, max-strength collapse, nil-on-throw = no manufactured conclusion);
  `MonitorReportAnalyzer` remains the `SyxxCage` capture → section-inputs bridge.
- **Engine (Windows, migrated 2026-07-29; corrected 2026-07-30, DOCDELTAS G001):**
  `Windows/dotNET/Syxx.Kit/Synthesis/` now runs the matrix engine in production via
  `SyxxMatrixIntegration.cs`, behind a `UseMatrixEngine` flag defaulting to true.
  `Conclusion.cs:29` carries `DeterminativeResult? Determination`, and `:51-64` expose
  `Determination?.State` wrappers documented as "thin wrappers over Determination, never DST fields."
  The DST engine is retained but unreferenced; deleting it is the remaining migration step. Note the
  path: the Windows tree lives under `Windows/dotNET/`, not `Windows/`.
  **This entry previously read "still runs the retired DST engine ... do not extend it," which was
  false after 2026-07-29 and was steering work away from the live engine.**
- **Engine (Linux):** the Avalonia client links `Syxx.Kit` rather than forking it, so it inherits the
  Windows engine state. Three-platform parity is binding (`AGENTS.md:49`), so a determinative change
  owes a verified twin on all three, not two.
- **Output:** a tri-state `DeterminativeResult` — `.clean` / `.threatDetected` / `.inconclusive` —
  plus only the diagnoses the evidence affirmatively supports (tiers `.probable`/`.conclusive`),
  each with fit/coverage/margin/contradiction and its own `supportingIndicators` (C064: surfaces
  justify a named diagnosis only from these). Abstention is `.inconclusive` with an EMPTY
  diagnoses list — never a scored class, never a distribution. No confidence percentage exists.
- **Honesty posture (Phase 1, 2026-07-23):** missing/invalidated required sections hard-gate their
  diagnoses (fail-closed) and the gate is checked BEFORE hard exclusion (an unevaluable diagnosis
  cannot be affirmatively disproved); `SectionStatusEntry.completeness` threads into
  `ObservedIndicator.quality` (C063 — attenuates scoring, defaults to 0.0 never 1.0); the honesty
  overhang (`SuppressedDiagnosis`: gated, hard-excluded, or C062 `belowTier`) is admitted and
  clean-checked on quality-INDEPENDENT observed support (C065 — a thin section or a crushing gate
  may attenuate a score but never hide a signal that fired), and any non-excluded overhang entry
  forbids `.clean`. All weights/thresholds are uncalibrated expert constants: they gate conclusions
  and are NEVER shown as measurements (calibration is Phase 6 of MATRIX-MIGRATION-PLAN.md, blocked
  on a labeled corpus).

## Score-design invariants: no/partial/full-data contracts (B005) — RETIRED

> **RETIRED 2026-07-30 (DOCDELTAS G002). Do not implement from this section.**
>
> The three scores below, and the B001/B002 sub-scorers they bind, no longer exist. `Scorers.swift`
> was deleted from macOS on 2026-07-21 with the DST engine (owner directive), and
> `resolverProvenanceConfidence` and `tamperMechanismFingerprint` return zero grep hits anywhere in
> `macOS/Sources`. The section also rests on two things the current engine does not have: DST belief
> mass, and a numeric confidence percentage. That directly contradicts the shipped contract stated
> above at "Honesty posture", which reads "No confidence percentage exists."
>
> **What replaced it:** `MatrixDeterminativeEngine` carries its own honesty gates, described above.
> Abstention is `.inconclusive` with an empty diagnoses list, never a scored class and never a
> distribution. There is no flagship number.
>
> **Consequence for the backlog:** the B003 DST-versus-independence decision, which `ROADMAP.md`
> records as blocking the B005 composition math, is moot. The scorers it governed are gone.
>
> **Why this is kept rather than deleted:** the no-data, partial-data, and full-data reasoning is
> sound and is the right starting point for calibrating the matrix engine in Phase 6 of
> MATRIX-MIGRATION-PLAN.md, which is itself blocked on a labeled corpus. Read it as design rationale,
> not as a contract on live code.

*What does each score output when evidence is missing, thin, or complete?*

This section specifies the **zero-data, partial-data, and full-data** behavior for the three
flagship derivation scores. These contracts were binding on the B001/B002 sub-scorer
implementations: no score may emit a confident number from absent, timed-out, or invalid data.

### Invariant (all scores, all data regimes)

1. **No score may default to confident.** Missing/invalid data must produce `inconclusive` (no
   score), not a confident number with a caveat.
2. **Missing data is not evidence.** A null field or a timed-out probe counts as zero
   information — it cannot tilt the score toward clean or tampered.
3. **Partial data produces attenuated confidence.** When only *N* of *M* signals are available,
   the score is multiplied by `N/M` so thin evidence yields proportionally reduced certainty.
4. **The composition gate is per-score.** A missing sub-score for one component does not
   collapse the aggregated score — that component drops out of the denominator (see each
   score's spec below).
5. **All thresholds above 75% or within 30-point margin require referral** (per USER Delta #1).

### 1. Resolver Provenance Confidence

*How sure are we that the response came from the intended resolver?*

| Data regime | Behavior | Output |
|---|---|---|
| **No data** (all probe fields nil/empty; no resolver response) | No score emitted. Section reads `inconclusive`. | `nil` / `null` — never a number. |
| **Partial data** (k of N provenance signals available, remainder timed out or invalid) | Attenuated: `score * (k/N)`. If `k < N/2` (majority missing), score is capped at 0.40 (40%), and the constituent deficits are surfaced in evidence. | `Float?` ∈ [0, 1] or `nil`. |
| **Full data** (all N signals collected and valid) | Normal B001 sub-scorer composition. Each signal weighted by its DST belief mass (not equally). | `Float` ∈ [0, 1]. |

**Signals used:** EDNS0 presence, source-port entropy, TXID entropy, transport encryption
(DoH/DoT/DoQ vs plain UDP), resolver-IP match with expected collector vantage.

**Edge case — resolver unreachable:** When `resolverDidFail` is set, all provenance signals
are missing → no data regime applies (inconclusive, never clean).

### 2. Tamper Mechanism Fingerprint

*What kind of interference is occurring (DNS poisoning, transparent proxy, forged cert, SRI mismatch)?*

| Data regime | Behavior | Output |
|---|---|---|
| **No tamper signals** (all comparisons: matched, no discrepancies) | No fingerprint emitted. Section reads `secure`. | `nil` — no fingerprint (correct operation). |
| **Partial/conflicting signals** (some anomalies detected but inconsistent mechanism surface) | Tentative fingerprint with confidence ≤ 0.35. Surfaced as a hypothesis, not a conclusion — the referral gate (§5) catches this. | `Fingerprint?` with confidence ∈ [0, 0.35] or `nil`. |
| **Full consistent signal set** (≥3 distinct tamper indicators converge on same mechanism class) | Normal fingerprint computed. Confidence reflects signal strength and cross-mechanism corroboration. | `Fingerprint` with confidence ∈ [0.35, 1]. |

**Signal classes used:** HTTP→HTTPS downgrade, SRI mismatch, cert-public-key mismatch,
content-body divergence, TTL anomaly, CNAME-chain discontinuity, transport-protocol
downgrade, authoritatively-sourced A/AAAA comparison.

**Minimum corroboration rule:** A single signal can never trigger a fingerprint — ≥2
signals from independent sources (e.g., content body + TLS cert, not two TTL values)
are required for even a tentative fingerprint. This mirrors the C009 exfil minimum-signal
rule (WS2-02).

### 3. Evidence Confidence Score

*Overall confidence that the collected evidence supports the conclusion.* This is the
flagship number shown to the end user.

| Data regime | Behavior | Output |
|---|---|---|
| **No evidence** (all six sections: inconclusive or empty) | No score. Overall verdict: `inconclusive`. The referral gate always fires. | `nil` — never a number. |
| **Partial evidence** (K of 6 sections conclusive, remainder inconclusive/skipped) | Gated: `score = baseScore * (K/6)`. If any **critical** section (Prophet, Mirror, Glutton) is inconclusive, the score is further halved. Inconclusive sections are surfaced in the evidence breakdown. | `Float?` ∈ [0, 1] or `nil`. |
| **Full evidence** (all 6 sections: secure or tampered) | Normal composition: weighted mean of per-section DST beliefs, with referral-gate check. | `Float` ∈ [0, 1]. |

**Critical sections:** Prophet (DNS integrity), Mirror (tamper detection), Glutton (privacy
leak). These are the three sections most directly tied to the tamper/censorship/leak
mission. If any of these three is inconclusive, the score is halved to prevent a confident
verdict built on non-critical sections alone.

**Non-critical sections:** Portal (network path), Cage (forensics), Witness (trust
infrastructure). These provide supporting evidence but cannot independently sustain a
confident verdict.

---

## Shared probe orchestration (C005) — SHIPPED

> **Status corrected 2026-07-30 (DOCDELTAS G007).** This section was written as a proposal and left
> in that tense after the work landed, so its "Incremental adoption" checklist read as outstanding.
> `ProbeRunner` and `ProbeBudget` exist on both platforms and all phases are complete. Read the
> "Problem" statement below as history, not as the current state.
>
> **Per-section budget defaults, as shipped** (total / per-probe / max-probes): Prophet 30s / 5s / 15,
> Mirror 30s / 5s / 10, Portal 15s / 5s / 5, Glutton 20s / 5s / 5.
>
> **Documented limitation R1, still live:** probes wrapping `DNSResolver.queryUDPDetailed` may run up
> to roughly 4s past their stated timeout, because cancellation is not enforced at the transport layer
> and an internal retry loop keeps going. A budget is therefore an approximate ceiling, not a hard
> one. Any surface that presents a timeout as a measured bound is overstating it.
>
> **Platform note:** Swift `ProbeBudget` is a struct passed `inout`; the C# twin is a class. Windows
> `ProbeRunner.RunAsync<T>` carries a `T : class` constraint, so value types stay unwrapped.

*Execution, budgeting, and consensus, extracted so four frontends do not drift.*

### Problem (as of the C005 design, now resolved)

Each of the four frontends (macOS CLI, macOS App, Windows CLI, Windows UI) ran probes in a
private `runProphet`/`runMirror` method. The probe lists were similar but not identical; the timing,
error-handling, and partial-result logic was duplicated; and a new probe had to be wired into all four.

### Design: three separable concerns

**1. Execution** — a uniform `ProbeRunner` that takes a probe + target + budget and returns a typed
result. Every probe in the system already conforms to a `static func probe(...) async -> T?` pattern;
the runner is a thin wrapper that adds timing capture and error classification.

```
protocol Probe { associatedtype Output }
struct ProbeRun { let name: String; let duration: TimeInterval; let result: Success/Failure/Timeout }
```

**2. Budgeting** — a central `ProbeBudget` struct consumed by the runner.

```
struct ProbeBudget {
    var totalTimeout: TimeInterval    // hard wall-clock cap for the entire section
    var perProbeTimeout: TimeInterval // individual probe timeout
    var maxProbes: Int                // maximum probes to run before early termination
}
```

**3. Consensus** — already implemented and in use:
- `VantageComparer.evaluate()` + `detectUnanimousTampering()` (B010) — divergence classification
- `AuthoritativeVantage.majorityConsensus()` (B012) — quorum-based agreement
- `SectionStatus` / `SectionEvidence` — per-section aggregation

These three are already in `SyxxKit`/`Syxx.Kit`. C005 only needs a lightweight container that
calls them in order with shared budget.

### Incremental adoption

1. Add `ProbeBudget` + `ProbeRunner` to `SyxxKit`/`Syxx.Kit` (zero behavioral change — all
   existing Probe static methods remain).
2. One by one, migrate `runProphet`/`runMirror` in each frontend to call `ProbeRunner.run()`.
3. Remove duplicated probe lists; each frontend only specifies which probes to run and passes a
   budget.

### What stays where it is

- **Probe implementations** (`ResolverLatencyProbe`, `WildcardSynthesisProbe`, etc.) — each remains
  a standalone `static func probe(...)` in its own file. C005 does not change how a probe works.
- **VantageComparer / AuthoritativeVantage** — remain the interpretation layer. C005 calls them in
  sequence; it doesn't replace them.
- **SectionInputs / EvidenceAdapters** — remain the output contract. `ProbeRunner` writes into
  `SectionInputs`; `EvidenceAdapters` reads from it.

---

**Notes on the shape of this split:**

- P3 (test coverage) in `ROADMAP.md` isn't its own discipline — it's cross-cutting QA that lands
  inside whichever of the six a given test targets (DNS-packet tests → #1, vantage/inconclusive
  tests → #2, IP classification/network-state tests → #3, witness signing tamper tests → #6,
  capture parser fixtures → #5). All five P3 sub-items are now done on both platforms.
- The third remediation sweep closed the original frozen-baseline ROADMAP, but the current
  working-tree re-audit has reopened a small active backlog (`CUR-001`…`CUR-006` in
  `DELTAS.md` / `ROADMAP.md`). The remaining broader product surface is still proposals in
  `PROPOSALS.md`.

---

## Runtime options: the six checks the launcher presents

The interactive `syxx` launcher (both platforms) exposes exactly one named check per discipline.
Every tool/component is assigned to the group **its output is most relevant to** — the group a user
would consult that output *for*, not merely the code it happens to touch.

| # | Runtime option | Discipline | One-line purpose | Components (output lands here) | CLIs |
|---|---|---|---|---|---|
| 1 | **Prophet** | DNS Resolution & Protocol Integrity | Tests your DNS resolver for accuracy. | `DNSResolver`, `DNSSECVerifier`, `ResolverLatencyProbe`, `AnswerStabilityProbe`, `WildcardSynthesisProbe`, `DenialOfExistenceProbe`, `DualStackProbe`, `EDNS0Probe`, `VersionBindProbe`, `StubEntropyProbe` | `syxx nslook` |
| 2 | **Mirror** | Tampering & Censorship Detection | Tests downloaded web content against a remote copy to ensure it matches. | `VantageComparer`, `ContentWarp`, `AuthoritativeVantage`, `SVCBProbe`, `TTLAnomalyProbe`, `CNAMEChainProbe` | `syxx warp`, `syxx mirror` |
| 3 | **Portal** | Network & Path Diagnostics | Tests your local LAN state and the route to a chosen domain. | `NetworkKeyState`, `IPClassifier`, LAN cartography (`LANCartographer`, `ARPTableParser`, `NetworkAddressClassifier`, `ServiceProbePlan`, `MDNSScanner`), `EncryptedTransportProbe`, `OpenResolverProbe` | `syxx key`, `syxx route`, `syxx lan` |
| 4 | **Glutton** | Privacy Leak Detection | A DNS leak test — diagnoses vulnerable VPN connections. | `LeakDetector`, `ECSProbe` | `syxx leak` |
| 5 | **Cage** | Capture, Forensics & Attribution | DNS capture & evaluation: which processes initiate queries, plus beaconing/attack tests. | `DNSCapture`, `DNSCaptureReportGenerator`, `ResponseSizeProbe`, `MonitorReportAnalyzer`, forensics (`BeaconAnalyzer`, `BlocklistAnalyzer`, `SieveBlocklistLoader`, `ExfilAnalyzer`) | `syxx cage`, `syxx forensics` |
| 6 | **Witness** | Trust Infrastructure: Evidence, Identity & Platform | Trust analysis for your home or office computer environment. | `WitnessSigner` (Ed25519), `LicenseManager`, `Sentinel`, `AnswerLedger`, `SyxxConfig` | `syxx witness`, `syxx sentinel` |

**Why a few assignments go where they do (output-relevance, not code-touch):**
- **`AnswerLedger` and `Sentinel` → Witness (6), not Tampering (2)**, even though answer-drift and
  config-change are tamper *signals*. Their output is a persistent trust/evidence baseline of your
  environment over time — you consult it to answer "is my environment still the one I trusted?",
  which is a #6 question. The point-in-time "is this answer being tampered *right now*" lives in
  Mirror (2).
- **LAN cartography → Portal (3), not Capture (5)**: its output describes the *shape* of your local
  network (who is on-link, which device is your resolver), a #3 question — even though it feeds the
  X1 rogue-resolver derivation.
- **`ExfilAnalyzer` → Cage (5)**: its output is a per-domain hostile-traffic conclusion (tunneling /
  beaconing), which is what you open the forensics discipline to get.
- The GUI (`syxx-gui`, macOS) is cross-cutting and surfaces all six; it is not itself a group.
- The **synthesis engine** consumes all six disciplines' outputs to produce one conclusion; it is
  cross-cutting, not a group (see "Determinative synthesis" above).

Each launcher option runs its discipline's primary check and reuses the shared `SyxxKit`/`Syxx.Kit`
modules, so CLI and menu stay in lockstep.


## Encrypted transports & trusted resolution — forward-looking (PARTIALLY GATED)

**Status:** design only; deferred in `ROADMAP.md`. Layers 1–2 are **not gated**; layers 3–4 are **gated on the
Apple Developer ID** (NetworkExtension entitlement). The arc: move Syxx from *detecting* DNS tampering to also
*routing around* it — a trustworthy encrypted, user-chosen (or self-hosted) resolution path Syxx verifies end
to end. Four layers, each usable alone:

1. **Encrypted transports (DoH/DoT/DoQ), client + server.** *Not gated.* The client already does **DoH**
   (`DNSResolver.queryDoH`); add **DoT** (NWConnection+TLS, :853) and **DoQ** (QUIC, :853). Integrity win: an
   encrypted *reference* lookup can't be transparently rewritten by an on-path Do53 proxy, so it's a trustworthy
   baseline (closes the "reference rides plain UDP" gap). **Server RAM tiers:** DoH ≈ free (another HTTPS route
   on the existing server); DoT modest (TLS listener); **DoQ is the real RAM cost** (QUIC per-conn state +
   `aioquic`) — evaluate separately.
2. **Resolver profiles / routing.** *Not gated; land early.* `{name, transport, endpoint, configId?}` presets:
   Cloudflare, Quad9, Google, AdGuard, Mullvad, **NextDNS** (per-user config ID), **OpenDNS/Umbrella**. The user
   picks a trusted resolver; Syxx routes probes (and later system resolution) through it and verifies it isn't
   downgraded/tampered. UI must frame these as *trusted third parties*, not Syxx-verified recursion.
3. **Native local validating resolver — GATED.** Bundle **Unbound** (BSD) as a local recursive, DNSSEC-
   validating, caching resolver on `127.0.0.1` — the differentiator ("be your own root of trust"). Spectrum:
   full recursion ⇄ forward-over-DoT to a §2 resolver. Cost is packaging/notarization + lifecycle, not RAM.
4. **Menu-bar client + one-click DoT engage/disengage — GATED.** `NSStatusItem` menu-bar app (macOS) with a
   toggle routing system DNS over the chosen transport/resolver via **`NEDNSSettingsManager`** (lighter) or
   **`NEDNSProxyProvider`** (in-flow). A menu-bar app **lives outside the window stack** — it structurally fixes
   the "window lost in the stack / no Dock focus" problem (correctly Dock-less for that form factor). A
   **captive-portal failsafe is non-negotiable** (auto-disengage on a portal via the Portal discipline), and
   disengage must never leave the user with broken DNS.

**Dev-ID gate — two wins from one entitlement:** the NetworkExtension entitlement unlocks the §4 system-resolver
toggle AND the real fix for the process-attribution race — an `NEDNSProxyProvider` receives the originating PID
*in* the flow (no post-hoc `lsof`), resolving the "only the persistent Objective-See DNSMonitor socket
attributes" limitation. Layers 1–2 need no entitlement and can start before the ID arrives. **Windows:** the
NE/menu-bar mechanisms are macOS-first; the Windows equivalents (tray + WFP/service) are a separate later scope.

## GUI Re-design

As of july 20, 2026, a new and (hopefully) final mockup of the GUI has been created. If you identify foreseeable issues with the design, please record them in DELTAS.md and let user know of your concerns directly. This time, there's a mockup for macOS, Windows, and Linux separately.

> **Fragility warning (DOCDELTAS G008, 2026-07-30).** The four paths below live in a user's
> `~/Downloads` directory, so this design cannot be honored by CI, by a fresh clone, or by any agent
> on another machine. They should be moved into the repo and re-pointed. Note also that F038 below
> cites a *different* in-repo mockup location (`Mockups-Web-Promos/`), so two mockup sets are
> currently presented as authoritative in one document. Resolve which is canonical.

The mockup location: 

'/Users/masterrabbit/Downloads/Dartboard GUI redesign fantasy theme-5/Syxx Linux GUI Mxups.html' '/Users/masterrabbit/Downloads/Dartboard GUI redesign fantasy theme-5/Syxx Windows GUI Mxups.html' '/Users/masterrabbit/Downloads/Dartboard GUI redesign fantasy theme-5/Syxx macOS GUI Mxups.html'  '/Users/masterrabbit/Downloads/Dartboard GUI redesign fantasy theme-5/Syxx Artboard (standalone).html'

The implementation guide:

'/Users/masterrabbit/Downloads/Dartboard GUI redesign fantasy theme-5/IMPLEMENTATION_GUIDE.md'

For macOS (and to a lesser extent Windows), this is not a huge change. It's a shift of colors and a slight reorganization of content. It's a significantly larger undertaking for linux. 

Also adds clarity for the content of reports, how the reports should be organized, etc. 

### Mockup-translation guard (F038)

Mockup data regions are watermarked "SAMPLE DATA — ILLUSTRATIVE, NOT A LIVE CAPTURE" (`Mockups-Web-Promos/`). When translating a mockup into a production view, no literal data value from the mockup may survive into view code — no IPs, hashes, hostnames, timings, notes, or severity labels. Views bind to live `AppModel`/`AppViewModel` state or render an explicit empty/untested state; a mockup value appearing in production view code is a §4 honesty violation, not placeholder convenience.
