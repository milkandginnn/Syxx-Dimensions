# Syxx Functional Design

A classification of Syxx's features into six functional disciplines. Each discipline lists what
has shipped, what's still open in `ROADMAP.md`, and what's proposed in `PROPOSALS.md`, so the
three documents can be read together: `DELTAS.md` catalogs defects, `ROADMAP.md` tracks their
remediation, `PROPOSALS.md` proposes new capability, and this document shows how it all fits
together by mission function rather than by file or tool.

## 0. CHALLANGE OF THE DAY/WEEK!

**QUESTION** I want all agents to consider something while going through this code. I am approved to sell apps on the Windows Store (signing privileges from Apple come this week), so this may be the first app out the door. I want it to do something other apps do not. As you go through this code and implement, and bring over tools from LyxTools, etc., think about the vast number of datapoints these tools give us--all of which can come form any other number of tools. I want you to, instead, think about what derivative datapoints this information gives us. For instance, 'this group of tools gives us datapoints A, B, and C, but when you consider the, together, you can infer datapoints X, Y, and Z.' In other words, how can we use this vast amount of information to derive something new and highly useful?

- **Register your answers below:**

- **Resolver Provenance Confidence:** combine `NetworkKeyState` resolver discovery, `syxx-lan`
  ARP/MAC cartography, mDNS/service probes, and DNS capture destination IPs to infer whether the
  active resolver is an expected router, an off-LAN upstream, a loopback proxy, or an unexpected
  on-link device. This is more useful than the raw datapoints because it gives a user a confidence
  score for "who is answering my DNS?"
- **Tamper Mechanism Fingerprint:** combine DNS RCODEs, local-vs-public answer divergence, Warp
  fetch/block status, and route timeout/last-hop patterns to infer likely DNS poisoning,
  NXDOMAIN/REFUSED censorship, transparent proxying, or path-level blocking.
- **Evidence Confidence Score:** combine witness signing, baseline degradation, collector leak
  attestation, capture attribution, and historical/passive-DNS drift into a single "how much can
  I trust this conclusion?" signal. This keeps uncertainty visible instead of hiding it in per-tool
  details.

> **Datapoint code note.** In the per-discipline lists below, `F#` codes are the Part F datapoint
> proposals (PROPOSALS.md); the older `A#`/`B#` codes are a separate enhancement taxonomy and do
> **not** line up with the `F#` numbers (e.g. §2's `A6` = "Mirror historical baseline/drift", which
> is unrelated to the Workstream-H A6 denial-of-existence datapoint). Where a proposal has since
> shipped, it is reflected in the shipped-component table (§ Runtime options), not under "Proposed":
> **F5** (Wildcard Synthesis) shipped as `WildcardSynthesisProbe` in **Prophet**, and **F6** (Re-query
> Consistency) is subsumed by the shipped `AnswerStabilityProbe` in **Prophet**; **F8** (IPv4/IPv6
> audit & DNS64) shipped as `DualStackProbe` in **Prophet** — *reclassified from Mirror*, because it
> audits one resolver's own dual-stack behaviour rather than comparing against an independent vantage.

> **F2 / F3 / F9 — captured under three conditions (was deferred).** These close the last
> completeness gap: omitting a datapoint that could flip the verdict is a false-clean blind spot, so
> rather than drop them they are collected — but bounded by three rules that keep them safe:
> 1. **Captured from the right pipeline** — each is sourced where its raw data actually and reliably
>    lives, never synthesised from a pipeline that can't see it:
>    - **F2 (CHAOS `version.bind`)** — the **DNS query pipeline** (a CHAOS-class query; `DNSResolver`
>      QCLASS ≠ IN). Resolver identity / fingerprint.
>    - **F3 (TXID / source-port entropy)** and **F9 (response-size / amplification)** — the **SyxxCage
>      capture pipeline**, computed over a *population* of captured packets (entropy and size anomalies
>      are meaningless from a single query). The capture record already stores the transaction ID and
>      source port per packet (F3); F9 adds a payload-byte-length field. Never derived from the query API.
> 2. **Labelled to the layer each one actually measures** — these are layer-sensitive, not
>    fundamentally weak, signals; the only mistake would be to overstate the layer:
>    - **F3** on a *local host* capture measures the **local stub / OS randomization quality
>      (spoofability posture)** — weak local randomization makes the host easier to spoof — **not** direct
>      proof of upstream recursive cache poisoning. Labelled and worded as such.
>    - **F2** is **contextual** — it improves resolver attribution/triage (e.g. a public-resolver IP that
>      hands back a forwarder banner ⇒ something local is in the path); it does not by itself prove tampering.
>    - **F9** is **supporting telemetry** — it explains truncation, DNSSEC overhead, TCP fallback, and odd
>      amplification/tunnelling characteristics; not a standalone verdict driver.
>    - **F10 (ECS echo/leak)** — *already shipped* (`ECSProbe` → `ecsSubnetLeak`, Glutton). Beyond the
>      privacy-leak signal it also acts as a **dampener**: answer diversity explained by client-subnet
>      routing must not be mistaken for tampering.
> 3. **Supporting weight only, never decisive** — all sit in the Weak tier (~0.4–0.6 LLR), the same class
>    as the other Part F signals. None is ever assigned a Strong/Decisive weight, and none can
>    single-handedly move a hypothesis past the confidence/margin gates. Core/decisive verdicts stay tied
>    to the stronger evidence — authoritative divergence, authenticated denial-of-existence, redirection,
>    collector-confirmed leak; F2/F3/F9 only tilt an already-formed picture. A future *evidence-completeness
>    discount* (lower confidence when a known datapoint wasn't collected) would harden the false-clean
>    guarantee beyond the current section-level hard-gate.

## 1. DNS Resolution & Protocol Integrity

*Is a single query/response exchange itself trustworthy?*

- **Shipped:** `DNSResolver` (UDP/TCP/DoH engine), `DNSSECVerifier`, `syxx-nslook`
- **Open in ROADMAP:** CUR-001 (reject QR=0 non-response packets), CUR-002 (remove/re-type the
  records-only `resolveSecurely` secure-path wrapper), CUR-005 (macOS launcher Step 1 must preserve
  typed DNS statuses).
- **Proposed:** A2 (real DS→DNSKEY→RRSIG chain validation, replacing presence-only checks), A8
  (DoT/DoQ + per-answer transport provenance in nslook), B5 (`SyxxDeepVerify` — authoritative
  zone auditing), F1 (EDNS0 Capability Probe), F2 (CHAOS/version fingerprinting), F3 (TXID & Source Port Entropy Audit)
  <!-- F1 shipped (EDNS0Probe); F2 shipped in Prophet (VersionBindProbe — CHAOS version.bind, contextual/tilt-only, 0.4 interception hint); F3 shipped in Prophet (StubEntropyProbe — capture-sourced stub/OS randomization quality, degraded posture + 0.4 spoofing tilt), fed via MonitorReportAnalyzer. -->



## 2. Tampering & Censorship Detection

*Is something actively interfering with what I asked for?*

- **Shipped:** `VantageComparer`, `ContentWarp`, `syxx-mirror`, `syxx-warp`, `SyxxCLI` Option A
- **Open in ROADMAP:** CUR-004 (macOS CLIs must surface degraded comparison wording instead of
  printing fully secure/success output).
- **Proposed:** A1 (multi-transport corroboration — diverging encrypted vs. plaintext answers as
  tamper evidence), A5 (Warp SRI/TLS/HSTS inspection), A6 (Mirror historical baseline/drift), B2
  (Censorship Fingerprinting Engine — classify *how* something is blocked), F4 (TTL Anomaly Detection),
  F7 (Full CNAME Chain comparison)
  <!-- F5 shipped in Prophet (WildcardSynthesisProbe); F6 subsumed by AnswerStabilityProbe; F8 shipped in Prophet (DualStackProbe, reclassified from Mirror) — see the datapoint code note above. -->

## 3. Network & Path Diagnostics

*What does my local network and the path to the destination actually look like?*

- **Shipped:** `NetworkKeyState`, `IPClassifier`, LAN cartography (`LANCartographer`, `syxx-lan`),
  `syxx-key`, `syxx-route`
- **Open in ROADMAP:** CUR-006 (`syxx-route` still emits raw traceroute while claiming route-shift
  and path-anomaly analysis), plus CUR-003 on Windows comparison behavior when truncation makes a
  DNS baseline unusable.
- **Proposed:** A4 (structured traceroute with baseline diffing), A9 (continuous
  `NetworkKeyState` watchers), B3 (`SyxxBridge` — local DoH proxy sitting in the path), B6
  (`SyxxFlow` — traceroute-correlated network heat maps)

## 4. Privacy Leak Detection

*Is my VPN/tunnel actually keeping my DNS queries private?*

- **Shipped:** `LeakDetector`, `syxx-leak`, `SyxxCLI` Option B
- **Open in ROADMAP:** None — the collector-confirmed leak protocol is fully implemented on both
  platforms. `LeakDetector.runCollectorTest` / `RunCollectorTestAsync` issues a signed one-time
  token, dispatches DNS queries through system resolvers, then verifies with the collector to
  obtain an HMAC-signed `LeakTestResult` with verdict and observed-resolver evidence.
- **Proposed:** A3 (collector-correlated egress attestation — the feature that would close the
  above gap), F10 (EDNS Client Subnet ECS echo/leak detection)

## 5. Capture, Forensics & Attribution

*What DNS traffic is flowing, and which process/service is responsible?*

- **Shipped:** `DNSCapture`, `DNSCaptureReportGenerator`, `syxx-cages`
- **Open in ROADMAP:** None — all three P2 capture/performance items are done.
- **Proposed:** A10 (EDNS-aware, low-memory streamed capture), B8 (`SyxxIdentity` — real-time
  process-level DNS attribution), F9 (Response Size Amplification Tracking)
  <!-- F9 shipped in Cage (ResponseSizeProbe — capture-sourced, DNSPacketReport.payloadBytes; a sustained large-response run is a degraded caveat + 0.4 localCompromise tilt), fed via MonitorReportAnalyzer — the SyxxCage capture → determinative-engine bridge. -->


## 6. Trust Infrastructure: Evidence, Identity & Platform

*Can the evidence, credentials, and the app itself be trusted?*

- **Shipped:** `WitnessSigner`, `syxx-witness`, `LicenseManager` (now Keychain-backed for the
  license key)
- **Open in ROADMAP:** None — all three P1 items in this section are done (credential storage,
  trial-state hardening, witness-signing hardening).
- **Proposed:** Part C (service-layer refactor of `SyxxKit` for CLI+GUI parity), A7 (witness
  transparency-ledger publication), B1 (`SyxxSentry` continuous daemon), B4 (`SyxxRegistry`
  resolver-health ledger), B7 (`SyxxAutomation` scripting), B9 (`SyxxPortal` dashboard), B10
  (`SyxxAudit` compliance reports)

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
| 1 | **Prophet** | DNS Resolution & Protocol Integrity | Tests your DNS resolver for accuracy. | `DNSResolver`, `DNSSECVerifier`, `ResolverLatencyProbe`, `AnswerStabilityProbe`, `WildcardSynthesisProbe`, `DenialOfExistenceProbe`, `DualStackProbe` | `syxx-nslook` |
| 2 | **Mirror** | Tampering & Censorship Detection | Tests downloaded web content against a remote copy to ensure it matches. | `VantageComparer`, `ContentWarp`, `AuthoritativeVantage`, `SVCBProbe` | `syxx-warp`, `syxx-mirror` |
| 3 | **Portal** | Network & Path Diagnostics | Tests your local LAN state and the route to a chosen domain. | `NetworkKeyState`, `IPClassifier`, LAN cartography (`LANCartographer`, `ARPTableParser`, `NetworkAddressClassifier`, `ServiceProbePlan`, `MDNSScanner`), `EncryptedTransportProbe` | `syxx-key`, `syxx-route`, `syxx-lan` |
| 4 | **Glutton** | Privacy Leak Detection | A DNS leak test — diagnoses vulnerable VPN connections. | `LeakDetector` | `syxx-leak` |
| 5 | **Cage** | Capture, Forensics & Attribution | DNS capture & evaluation: which processes initiate queries, plus beaconing/attack tests. | `DNSCapture`, `DNSCaptureReportGenerator`, forensics (`BeaconAnalyzer`, `BlocklistAnalyzer`, `SieveBlocklistLoader`, `ExfilAnalyzer`) | `syxx-cages`, `syxx-forensics` |
| 6 | **Witness** | Trust Infrastructure: Evidence, Identity & Platform | Trust analysis for your home or office computer environment. | `WitnessSigner`, `LicenseManager`, `Sentinel`, `AnswerLedger`, `SyxxConfig` | `syxx-witness`, `syxx-sentinel` |

**Why a few assignments go where they do (output-relevance, not code-touch):**
- **`AnswerLedger` and `Sentinel` → Witness (6), not Tampering (2)**, even though answer-drift and
  config-change are tamper *signals*. Their output is a persistent trust/evidence baseline of your
  environment over time — you consult it to answer "is my environment still the one I trusted?",
  which is a #6 question. The point-in-time "is this answer being tampered *right now*" lives in
  Mirror (2).
- **LAN cartography → Portal (3), not Capture (5)**: its output describes the *shape* of your local
  network (who is on-link, which device is your resolver), a #3 question — even though it feeds the
  X1 rogue-resolver derivation.
- **`ExfilAnalyzer` → Cage (5)**: its output is a per-domain hostile-traffic verdict (tunneling /
  beaconing), which is what you open the forensics discipline to get.
- The GUI (`syxx-gui`, macOS) is cross-cutting and surfaces all six; it is not itself a group.

Each launcher option runs its discipline's primary check and reuses the shared `SyxxKit`/`Syxx.Kit`
modules, so CLI and menu stay in lockstep.
