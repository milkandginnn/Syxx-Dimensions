# Syxx Feature Proposals

Generated 2026-07-04. Companion to `DELTAS.md`. Where `DELTAS.md` catalogs defects that
impair the mission, this document proposes *capability-level* advances that extend it.

Mission: investigate and detect DNS tampering, hijacking, spoofing, and censorship, and
produce trustworthy evidence of it. Syxx today ships the original CLI family (`syxx` launcher,
`syxx-nslook`, `syxx-leak`, `syxx-mirror`, `syxx-witness`, `syxx-key`, `syxx-route`,
`syxx-cages`, `syxx-warp`) plus the new `syxx-lan` surface and a thin macOS `syxx-gui` target,
over a shared `SyxxKit` / `Syxx.Kit` core. The proposals below assume correctness deltas are being
remediated in parallel; they describe new capability, not bug fixes.

---

## Current Feature Analysis — 2026-07-04

The current tree has moved beyond the original proposal list: LAN cartography exists on both
platforms (`syxx-lan`), macOS has an initial SwiftUI GUI wrapper, and the server already exposes
paid leak-token verification, passive-DNS query, and investigation endpoints. The feature gap is no
longer "can Syxx run probes?" but "can Syxx synthesize all those probes into derivative conclusions
that another DNS utility does not provide?"

- **F1. Resolver Provenance Confidence:** combine active-route resolver discovery, `syxx-lan`
  ARP/MAC mapping, mDNS/service probes, and DNS capture destination IPs into a confidence model for
  "who is answering my DNS?" Output should classify resolver provenance as expected router,
  unexpected on-link device, loopback proxy, VPN/upstream, or unknown, with evidence. This is the
  most store-ready differentiator because it turns many ordinary datapoints into one answer a user
  can act on.
*Vetting:* Agree. Resolver provenance classification is highly valuable for detecting local routing shifts, rogue resolvers, and on-link configuration modifications.

- **F2. Censorship Mechanism Timeline:** combine VantageComparer outcomes, DNS RCODEs, Warp
  HTTP/block-page status, route last-hop/timeouts, and passive-DNS drift into a timeline that
  explains *how* access changed: DNS poisoning, NXDOMAIN/REFUSED policy block, transparent HTTP
  proxy, SNI/TLS failure, route blackhole, or inconclusive.
*Vetting:* Agree with revision. Revisions: Differentiating persistent policy configurations from transient active hijacking require local long-term caching of prior vantage state results.

- **F3. Evidence Confidence & Signing:** combine witness snapshots, baseline degradation,
  collector-signed leak results, capture attribution, and passive-DNS history into a signed
  confidence score. The UI should never flatten this to "secure"; it should show what evidence was
  direct, what was inferred, and what was unavailable.
*Vetting:* Agree. Cryptographic evidence confidence scores allow users to verify the security verification integrity of witness-attested results.

- **F4. GUI/Product Readiness:** the macOS GUI is a useful thin wrapper, but the source comments
  explicitly describe it as not a designed product surface. There is no native Windows GUI project
  yet, only mockups in `Design/`. A store-ready app should prioritize the F1/F2/F3 synthesis views
  over merely recreating CLI output in windows.
*Vetting:* Agree. Product UI parity across macOS and Windows is necessary to prepare the app for public release.

- **F5. LyxTools Parity Still Open:** the current LAN port covers command running, address
  classification, ARP parsing, service-probe planning, macOS active TCP probing, macOS mDNS, and LAN
  cartography. Still missing or incomplete: Windows active service probing, Windows mDNS, beacon and
  blocklist analyzers, Sieve blocklist loading, process network monitoring, AnswerLedger,
  ConfigurationWatcher, and macOS LaunchdScanner.
*Vetting:* Agree. Re-implementing parity features (mDNS checks, blocklist parsers, config monitors) ensures matching diagnostic coverage across platform targets.

- **F6. End-of-Test Meaning Decoder:** every Prophet/Mirror/Portal/Glutton/Cage/Witness run should
  end with a deterministic synthesis pass that distinguishes `untested`, `invalidated`, `degraded`,
  `inconclusive`, positive findings, and clean negative findings. This should reuse the
  `DNSLogAnalyzer` pattern and be driven by typed evidence points plus a small ruleset or bundled
  SQLite/JSON predicate table, not by ad hoc CLI strings or raw booleans.

*Vetting:* Agree. A typed predicate analyzer replaces unmaintainable string-matching filters in downstream modules.

---

## Part C — CLI/GUI-Agnostic Service Architecture

The `SyxxKit` / `Syxx.Kit` core shall be refactored into a stateless, event-driven service layer (JSON-RPC or structured IPC) to ensure CLI tools and GUI dashboards provide a unified view of the system's state.

*Vetting:* Agree. A clean stateless backend model with structured JSON IPC allows multi-platform wrappers to share state and prevent parsing inconsistencies.

## Part D — Enhanced Audit Capabilities (10 Enhancements)

- **A1. Multi-transport corroboration:** Compare UDP, TCP, DoH, DoT per vantage.
*Vetting:* Agree. Comparing results across UDP, TCP, DoH, and DoT is essential to reveal transport-specific DNS hijacking (e.g. transparent proxying on UDP/53 but clean responses on DoH).

- **A2. DNSSEC chain validation:** Full DS/DNSKEY/RRSIG verification.
*Vetting:* Agree. Validating DNSSEC cryptographic keys locally is the only mechanism to detect active on-path signature stripping.

- **A3. Collector-correlated leak detection:** Closed-loop egress attestation.
*Vetting:* Agree. Resolves the VPN DNS leak false positive warning issue by verifying token responses against signed collector payloads.

- **A4. Structured traceroute:** Typed hops, ASN mapping, and path diffing.
*Vetting:* Agree with revision. Revisions: Enrich traceroute path hop IPs with Autonomous System (AS) ownership numbers and BGP routing prefix matching to detect BGP hijacking anomalies.

- **A5. Warp SRI/TLS inspection:** DOM parsing + SRI/HSTS checks.
*Vetting:* Agree. Checking local script hashes against the original remote server baseline detects inline script injections.

- **A6. Mirror historical store:** Signed longitudinal drift detection.
*Vetting:* Agree. Preserving longitudinal history allows detection of silent, slow record IP modifications.

- **A7. Witness publication:** Timestamped logs and transparency ledger entries.
*Vetting:* Agree with revision. Revisions: Never publish raw user network IP and resolver names to public transparency ledgers. Enforce local encryption for witness logs and anonymize metadata prior to consent-driven sharing.

- **A8. Modern nslook:** DoT/DoQ support with provenance tagging.
*Vetting:* Agree. Adding DoH/DoT/DoQ diagnostic lookup capability matches modern system setups.

- **A9. Network monitoring:** Continuous `NetworkKeyState` watchers.
*Vetting:* Agree. Active network observers capture ephemeral connection changes.

- **A10. Transactional capture:** EDNS-aware, low-memory, streamed DNS capture.
*Vetting:* Agree. Low-memory streaming DNS capture prevents process crashes under high traffic volumes.

## Part E — Architectural Capabilities (10 New Broad Features)

- **B1. SyxxSentry:** Continuous background integrity-monitoring daemon (launchd/Windows Service).
*Vetting:* Agree. A continuous background monitor is necessary for active alert protection.

- **B2. Censorship Fingerprinting:** Active probes to classify blocking mechanisms (e.g., DNS, TCP-reset, SNI-filtering).
*Vetting:* Agree. Fingerprinting active censorship devices (e.g. matching connection resets vs silent packet drop) aids attribution.

- **B3. SyxxBridge:** Local DoH proxy for filtering/verification.
*Vetting:* Disagree. Building a custom local DoH proxy server expands the attack surface, introduces complex routing loops, and is out-of-scope for an auditing utility whose core value is passive/active investigation.

- **B4. SyxxRegistry:** Append-only ledger for resolver health/performance.
*Vetting:* Agree with revision. Revisions: Keep the resolver registry strictly local to preserve client resolution privacy, avoiding central tracking.

- **B5. SyxxDeepVerify:** Authoritative zone auditing.
*Vetting:* Agree. Direct authority zone audits verify delegation path health.

- **B6. SyxxFlow:** Traceroute-correlated global network heat maps.
*Vetting:* Agree with revision. Revisions: Global heat maps must anonymize routing paths and client IP details to protect user geo-privacy.

- **B7. SyxxAutomation:** Lua-based scripting engine for custom audits.
*Vetting:* Agree. Lua scripting is standard for custom enterprise security rules.

- **B8. SyxxIdentity:** Process-level DNS-request attribution.
*Vetting:* Agree. Process identity correlation is a major requirement for endpoint threat detection.

- **B9. SyxxPortal:** Real-time web-based dashboard UI.
*Vetting:* Agree. Dynamic portal dashboards provide premium visual feedback.

- **B10. SyxxAudit:** Automated end-to-end security compliance reports.
*Vetting:* Agree. Standard compliance reports are highly useful for network admin compliance.

---

## Front-End Feature Analysis — 2026-07-06

To make DNS investigation complete, intuitive, and store-ready, the front-end GUI is missing several critical interactive visualizers and diagnostics that bridge raw CLI output with human analysis:

### 1. Interactive DNS Packet Inspector (Wire-Level Visualizer)

- **What is missing:** The GUI currently displays flat lists of query names and IP answers. It lacks the ability to inspect the internal DNS packet structure (flags like RD, RA, AA, TC, DNSSEC RRSIG payload fields, and EDNS0 options).
- **Proposal:** A collapsible hierarchical protocol tree view (similar to Wireshark but streamlined) for captured queries, highlighting anomalous fields (e.g., mismatched transaction IDs or invalid signatures) in bright warn/error colors.
- *Vetting:* Agree. Wire-level field validation is essential for detailed analysis.

### 2. Interactive Network Path Map (Visual Traceroute)

- **What is missing:** Path diagnostics are shown as raw text lists of hops.
- **Proposal:** A dynamic node-link graph showing the packet path from the client to the target. Hops are styled by round-trip latency, with geographic geolocation and Autonomous System Number (ASN) details mapped directly onto the nodes, highlighting where private/internal IPs unexpectedly appear in public routes (indicating transparent proxy interception).
- *Vetting:* Agree. Visual hop-by-hop latency tracing is much clearer than text logs.

### 3. Resolver Consensus & Drift Matrix

- **What is missing:** The Mirror tab lists results but does not show side-by-side comparisons of resolved records.
- **Proposal:** A comparative grid displaying record values (A, AAAA, CNAME, NS) resolved across multiple vantages (System, Cloudflare, Google, Quad9, and local LAN gateway). Divergent answers are highlighted in a heat-map style, making regional DNS blocking or cache poisoning instantly visible.
- *Vetting:* Agree. Makes cross-resolver differences immediately visible.

### 4. Interactive LAN Cartography Node Graph

- **What is missing:** `syxx-lan` results are rendered in static grids.
- **Proposal:** A topological map representing the local network. Nodes show the gateway, the client, configured DNS servers, and discovered network hosts. Rogue resolvers (e.g. unrecognized MAC addresses offering DNS services) are colored red with warning badges.
- *Vetting:* Agree. Simplifies local network rogue device detection.

### 5. Live Stream & Process-Correlated Audit Cockpit

- **What is missing:** DNS capture (`syxx-cages`) requires stopping the probe before viewing logs.
- **Proposal:** A real-time scrolling cockpit showing DNS lookups as they are sent, grouped by their originating system process (e.g., Slack, Chrome, or unsigned binaries). High-entropy queries (potential DNS tunnels) or plaintext UDP/53 requests are flagged in real-time with action alerts.
- *Vetting:* Agree. Real-time process attribution allows active threat response.

### 6. Longitudinal Latency & Packet Loss Charts

- **What is missing:** Network performance metrics are static snapshots.
- **Proposal:** Interactive time-series line charts tracking DNS latency and packet loss rates across all interfaces over 24-hour, 7-day, and 30-day windows to help users identify periodic or time-of-day censorship.
- *Vetting:* Agree. Tracks intermittent ISP network degradation over time.

---

## Part F — Missing DNS Diagnostic Datapoints (10 Collection Proposals)

The app already collects Rdata values, response statuses, transport metadata, and system resolver configuration. But several DNS datapoints that provide strong diagnostic signal for tampering, hijacking, and censorship detection are not collected at all. Each proposal below is a specific datapoint we should collect — not a UI feature or a synthesis algorithm, but raw data that feeds the evidence engine.

### F1. EDNS0 Capability Probe

- **What is missing:** Every DNS query today is sent without an EDNS0 OPT pseudo-record. We have no idea whether the resolver supports EDNS0, what its maximum UDP buffer size is, or what EDNS options (ECS, NSID, DNS Cookies, TCP Keepalive, Padding) it understands. After the 2020 DNS flag day, resolvers that reject EDNS0 produce timeout/failure patterns that today render as uninformative transport errors — indistinguishable from network blocks.
- **Datapoint to collect:** For each resolver, send an initial query with an EDNS0 OPT record advertising a 4096-byte UDP buffer (standard recommendation) and check whether the response includes an OPT record in the additional section. If the resolver echoes our options or adds its own (NSID, ECS), record those. Track: EDNS version supported, max UDP buffer size, supported option codes, and whether OPT is present at all.
- **Diagnostic value:** Absent OPT in response from a resolver we sent EDNS0 to = resolver may be pre-2020 flag day. Small max buffer (≤512) = legacy resolver. Echoed ECS = CDN-aware routing expected. NSID present = resolver identity available. This datapoint alone disambiguates "resolver is blocking me" from "resolver is too old to talk to."
- **Implementation:** Add a 12-byte OPT pseudo-record (name=0, type=41, payload=4096, rcode=0, version=0, DO=0) to every query's additional section. Parse any OPT record in the response. macOS `DNSPacket` struct needs extension; Windows `BuildQuery` needs OPT injection.
- **Status:** Not collected. No code touches OPT records.
- *Vetting:* Agree. EDNS0 options analysis prevents misclassifying legacy buffer failures as active blocking.

### F2. Resolver Fingerprint via CHAOS Queries

- **What is missing:** We know *what* each resolver answers but not *who* it is. An intercepting transparent proxy running Unbound, a home router running dnsmasq, a corporate resolver running BIND, and a public resolver like Cloudflare all produce fingerprintable DNS responses via CHAOS-class queries — but we never send them.
- **Datapoint to collect:** For each resolver, send CHAOS-class queries for `version.bind`, `version.server`, `id.server`, and `hostname.bind`. Record the TXT rdata of any response.
- **Diagnostic value:** `version.bind TXT "dnsmasq-2.85"` → home router or local forwarder. `version.bind TXT "unbound 1.17.0"` → possible recursive resolver. No response → hardened resolver or transparent proxy. The specific version string can be checked against known-vulnerable versions. The server identity (`id.server`, `hostname.bind`) tells us the resolver's own hostname, which may match or contradict the network path we observe — a mismatch is strong evidence of interception.
- **Implementation:** Add `queryChaos(name: String)` to `DNSResolver` on both platforms. Same wire format as standard queries but with QCLASS=3 (CHAOS) instead of QCLASS=1 (IN). Short timeout (2s) since CHAOS queries are often blocked.
- **Status:** Not collected. No CHAOS-class code exists.
- *Vetting:* Agree. CHAOS txt queries identify DNS proxy implementations (like Unbound or dnsmasq) running on-link.

### F3. Source Port & TXID Entropy Audit

- **What is missing:** On-path DNS poisoning attacks work by guessing the transaction ID and source port of a query. Resolvers that use predictable source ports or weak transaction ID generation are susceptible. DNSCapture already captures source ports of observed traffic, but the resolver's *own queries* (upstream) are never audited for entropy quality.
- **Datapoint to collect:** For each resolver, send 20-30 queries (same domain) and record for each response: transaction ID, source port (from UDP datagram), and whether the TXID matches our query. For the resolver's own upstream queries (passively observable), record source port and TXID distribution.
- **Diagnostic value:** Low source port entropy (same port or predictable pattern) → resolver is vulnerable to poisoning. Low TXID entropy (sequential, limited range) → same. Combined with DNSSEC status, this tells us whether the resolver's answers can be trusted at all, even if they pass comparison.
- **Implementation:** Modify `DNSResolver` to return source port from the UDP response. Add an `EntropyProbe` utility that sends N queries, collects port+TXID, computes Shannon entropy and collision rate. Passive: DNSCapture already has source port; add TXID extraction and an entropy aggregation pass over captured data.
- **Status:** Not collected. TXID is generated and validated but never audited for entropy; source port from DNSResolver responses is discarded.
- *Vetting:* Agree. Source port and transaction ID entropy checking detects weak implementations vulnerable to cache poisoning.

### F4. TTL-Based Anomaly Detection

- **What is missing:** TTL values are parsed in `DNSRecord` on both platforms but are never compared, analyzed for anomalies, or fed into any detection algorithm. They are collected and then unused. TTL signs are one of the most informative — and most overlooked — DNS datapoints for tampering detection.
- **Datapoint to collect:** For every DNS response, record the TTL alongside the Rdata. When comparing across vantages, flag: (a) identical TTLs across unrelated resolvers (suggests cache-sharing or upstream hijack — unlikely for Cloudflare and Quad9 to agree on a TTL to the millisecond), (b) extreme TTLs (>86400s = >1 day, suspicious for non-authoritative data; <1s = dynamic DNS or manipulation), (c) TTL changes exceeding 10x between queries seconds apart (cache flush or answer replaced).
- **Diagnostic value:** TTL cross-resolver agreement on the exact second value (not just same order of magnitude) is a strong indicator that both resolvers received the answer from the same upstream — which is expected for the *same* provider but suspicious across different providers. A TTL of 2147483647 (max int32) or 0 from one vantage but normal TTL from another is nearly diagnostic of cache poisoning.
- **Implementation:** Extend `ComparisonResult` to include per-record TTL. Add `TtlComparison` field: `identicalAcrossVantages`, `divergent`, `extremeValue`, `changedRecently`. Feed into Synthesis as evidence.
- **Status:** TTL parsed but never compared or analyzed. Current VantageComparer only checks Rdata strings.
- *Vetting:* Agree. TTL comparisons detect cache spoofing and forced records.

### F5. Wildcard Synthesis Detection

- **What is missing:** When we query a domain and get a non-NXDOMAIN response, we cannot distinguish "the zone uses wildcard records legitimately" from "the resolver is hijacking NXDOMAIN." The NXDOMAIN canary (already built in `DNSCanaries`) tests completely random names, but a *targeted* wildcard test under the specific queried domain tells us whether the domain itself uses wildcards — a critical piece of context for interpreting the Prophet section's NXDOMAIN findings.
- **Datapoint to collect:** After resolving the target domain, generate 2-3 random subdomain labels (e.g., `a83kfq.example.com`, `j2nspd.example.com`). If all return the same IP as each other *and* the IP matches the target domain's answer, the zone likely uses a wildcard (`*.example.com → IP`). If they return the same IP but different from the target, the resolver is likely hijacking NXDOMAIN to a block page.
- **Diagnostic value:** A confirmed wildcard changes the interpretation of NXDOMAIN from "either absent or blocked" to "definitely absent (wildcard would have caught it otherwise)." A confirmed NXDOMAIN hijack (random names → block page IP) is decisive evidence of tampering (H2/H3) independent of any vantage comparison.
- **Implementation:** Already partially exists in `DNSCanaries.swift` which tests two random names against the system resolver. Extend to: (a) test the *target domain's* subdomains specifically, (b) compare the wildcard IP against the target domain's answer, (c) report `wildcardPresent`, `wildcardSuspicious`, or `noWildcard` per resolver. Needs implementation on Windows.
- **Status:** NXDOMAIN canary exists (macOS only, `DNSCanaries`). Domain-specific wildcard detection does not.
- *Vetting:* Agree. Subdomain wildcard queries isolate domain-level features from active resolver hijacking.

### F6. Re-query Consistency Score

- **What is missing:** All probes query each domain once per resolver and assume that single answer is representative. But many DNS manipulations are intermittent: a transparent proxy may forward every third query to a legitimate resolver while answering the other two with a poisoned response. A single sample misses this entirely.
- **Datapoint to collect:** For the target domain, send 3-5 queries to the same resolver in rapid succession (200ms apart). Record the answer set for each. Compute a consistency score: 1.0 = all identical, 0.0 = all different. Record the answer variants — if 3 out of 5 return one IP and 2 out of 5 return another, log both.
- **Diagnostic value:** Low consistency (< 0.8) from a single resolver indicates either: (a) round-robin DNS (legitimate, common for CDNs — but should also see varying TTLs), (b) load balancing (same), (c) intermittent poisoning (adversarial — flagged), or (d) a resolver behind multiple upstreams with divergent views. High consistency from a resolver that should be round-robin (known CDN domain) suggests a proxy or cache conflating results — itself suspicious.
- **Implementation:** Add a `QueryConsistency` struct. `DNSResolver` gets a `queryRepeatedly(domain:count:interval:)` method that re-queries and returns all results with a consistency score. Feed into Synthesis as evidence weight.
- **Status:** Not collected. No re-query logic exists.
- *Vetting:* Agree. Consistency checks detect intermittent DNS interception.

### F7. Full CNAME Chain Collection & Cross-Vantage Comparison

- **What is missing:** CNAME records are collected alongside A/AAAA records in the current resolver (line 461 on macOS keeps CNAME for type 5), but (a) the chain is not followed to its terminal A/AAAA record, (b) intermediate CNAME targets are not compared across vantages, and (c) Windows drops CNAME records entirely (WDN-001). A CNAME chain targeting different IPs from different vantages is strong evidence of redirection, but we never collect the full chain.
- **Datapoint to collect:** For each domain and resolver, follow the full CNAME chain: query the original domain, extract CNAME target, query that target, repeat until an A/AAAA record is reached or a loop is detected (max 10 hops). Record every hop name and type. Compare chains across vantages: same length and same intermediate targets suggest honest routing; different terminals suggest poisoning.
- **Diagnostic value:** A CNAME chain that terminates at `evil.com` from the local resolver but at `cdn.example.com` from public vantages is near-definitive evidence of a redirection attack — stronger than IP comparison alone, because CNAMEs are semantically meaningful. Chain length differences (local sees 3 hops, vantages see 2) suggest a proxy injecting an extra hop.
- **Implementation:** Add `followCNAMEChain(domain:)` to `DNSResolver` on both platforms. Add `CNAMEChain` struct with hops, terminal, length. Extend VantageComparer to compare chains. Fix Windows CNAME retention (WDN-001 fix prerequisite).
- **Status:** macOS preserves CNAME records but doesn't follow chains. Windows drops CNAME records entirely. No cross-vantage CNAME comparison exists.
- *Vetting:* Agree. CNAME path audits detect target redirections to rogue locations.

### F8. IPv6 vs IPv4 Resolution Audit (Includes DNS64 Detection)

- **What is missing:** AAAA (IPv6) lookups are performed on both platforms but the results are evaluated independently from A (IPv4) results. We never ask: does this resolver prefer IPv6? Does it synthesize AAAA records from A records (NAT64/DNS64)? Are the AAAA records genuine or fake? DNS64 synthesis is increasingly common on mobile/cellular networks and VPNs, and can appear as tampering when the synthetic address fails to resolve from an IPv4-only vantage.
- **Datapoint to collect:** For each resolver, query for AAAA and compare the result to the A result. If the AAAA address follows the well-known IPv6 prefix `64:ff9b::/96` → DNS64 synthesis confirmed (legitimate). If the resolver returns NXDOMAIN for AAAA but a valid A record → resolver has no IPv6 route (expected). If both return the same CNAME chain → consistent. If AAAA returns a different CNAME than A → suspicious. If AAAA succeeds where A fails → unusual, possibly IPv6-preferential routing.
- **Diagnostic value:** DNS64 synthesis detected → the network is NAT64, which means IPv4-only vantages will naturally disagree with local results — this must be dampened in VantageComparer to avoid false tampering verdicts. AAAA-only failure with A success on a dual-stacked domain → the resolver has IPv6 connectivity issues, a common censorship vector. AAAA and A returning completely different answer sets → two different answers for the same name, a strong tampering signal.
- **Implementation:** Compare AAAA vs A results in a post-resolution `compareTransports()` step. Add DNS64 prefix detection (`64:ff9b::/96`, `64:ff9b:1::/48`, and well-known NAT64 prefixes). Output a `Dns64Detected` flag for use in VantageComparer dampening.
- **Status:** Both AAAA and A are queried on both platforms, but the two are never compared, no DNS64 detection exists, and no transport-preference analysis is performed.
- *Vetting:* Agree. DNS64 detection avoids false-positives under dual-stack configurations.

### F9. Response Size Amplification Tracking

- **What is missing:** DNS response size is never measured, logged, or analyzed. Large responses (especially those exceeding the EDNS0 buffer size) indicate either legitimate DNSSEC records, DNS amplification attempts, or poisoned responses with bloated additional sections. The EDNS0 buffer capability (F1) and response size together tell us whether the resolver is producing legitimate-sized responses.
- **Datapoint to collect:** For every DNS response, record the wire-format size in bytes (including IP/UDP headers when available). Compare against: the EDNS0 buffer size we advertised (F1), the 512-byte legacy limit, and the typical response size for the query type. Flag responses where: (a) size > advertised EDNS0 buffer (indicates resolver ignoring EDNS0), (b) size < 512 but TC bit set (contradiction, suspicion), (c) size > 1500 (exceeds typical MTU, IP fragmentation likely).
- **Diagnostic value:** A response that exceeds the advertised EDNS0 buffer but is accepted anyway means the resolver or a middlebox is ignoring EDNS0 — an important input for the EDNS0 compliance profile. A response with TC=0 but size > 512 on a resolver that didn't receive an EDNS0 query means the resolver is responding to non-EDNS0 queries with data that should have been truncated — non-standard behavior suggesting a proxy that strips EDNS0. Large answer counts (>50 records) in the additional section suggest cache poisoning attempting to inject many forged records.
- **Implementation:** Record response byte count in `DNSResponse` or as a new property. Add `ResponseSize` analysis pass that flags anomalous sizes. Integrate into VantageComparer's evidence output.
- **Status:** Not collected. No code tracks response size.
- *Vetting:* Agree. DNS response size checks detect amplification and injection exploits.

### F10. Resolver EDNS Client Subnet (ECS) Support & Echo

- **What is missing:** ECS is an EDNS0 option that allows recursive resolvers to forward a prefix of the client's IP address to authoritative nameservers, enabling CDN geo-routing. Whether a resolver supports ECS, and whether it echoes back our subnet, directly affects VantageComparer: resolvers with ECS will get different (geo-appropriate) answers than resolvers without. If we don't know each resolver's ECS behavior, we can't distinguish "legitimate geo-diversity" from "tampering divergence."
- **Datapoint to collect:** Send two queries to each resolver: one with an ECS option containing a /24 prefix of a known IP in a different geographic region (e.g., a European IP prefix if we're in the US), and one without ECS. If the ECS-tagged query returns different answers than the plain query, the resolver supports and respects ECS. Record the echoed subnet (if any) and whether the answers differ.
- **Diagnostic value:** A resolver that does not support ECS will return the same answers regardless of the ECS option — any cross-resolver answer differences are likely due to other factors (caching, upstream selection). A resolver that does support ECS and echoes our subnet is doing CDN geo-routing properly — answer diversity from this resolver is expected and should be weighted lower for tampering detection. A resolver that echoes a different subnet than we sent suggests a proxy stripping and replacing ECS — strong tampering evidence.
- **Implementation:** Add an ECS option (option code 8) to the EDNS0 OPT record in the initial probe (F1). The ECS value should be a /24 source prefix from the local resolver's own IP (via NetworkKeyState). Compare responses with and without ECS. Output `ecsSupported: Bool`, `ecsEchoesClientSubnet: Bool`, `ecsAnswersDiffer: Bool`.
- **Status:** Not collected. No ECS option code exists anywhere in the codebase, and there is no probe that varies query parameters to detect ECS behavior.
- *Vetting:* Agree. Checking ECS support helps identify geo-diversity versus tampering.

---

## Syxx — Features Analysis (night pass)

**Date:** 2026-07-06. Companion to DELTAS.md. Where deltas fix what exists, this proposes what
to *add*. Grounded in the current architecture (six sections → Synthesis engine → signed verdict).

---

### Part A — 10 datapoints we should collect to diagnose DNS issues, but currently don't

Each is verified absent in the current code (query builder emits a bare A/AAAA question with no OPT
record; only `AD`/`TC` flags are surfaced; VantageComparer compares resolvers to each other but never to
authoritative; `encryptedDNSStatus` is a loopback heuristic, not an active probe). Ordered by
diagnostic value.

#### 1. Per-resolver query latency (RTT) + retransmission/timeout counts

**Gap:** resolution is recorded as success / no-data / timeout — a boolean. No timing.
**Why it matters:** "DNS is slow" is one of the most common real complaints, and it's invisible today.
Latency per vantage separates a degraded forwarder from a healthy one, exposes a resolver that's up but
sluggish, and gives the retransmit count that signals packet loss on the path.
**Collect where:** Prophet — stamp `monotonic` before/after each UDP/DoH attempt; record RTT, attempt
count, and which transport won the race. Feeds a new `degraded` signal when RTT ≫ peers.

#### 2. Authoritative-vs-resolver ground truth

**Gap:** the tool establishes "truth" only by *consensus* across recursive resolvers. If an answer is
hijacked upstream of all of them (or is a legitimate CDN/geo difference), consensus can't tell which.
**Why it matters:** this is the missing anchor. Resolve the domain's delegation (NS + glue), query an
authoritative nameserver **directly**, and diff the recursive answer against it. A resolver answer that
disagrees with authoritative is real evidence of resolver-level manipulation; agreement rules it out.
**Collect where:** Prophet/Mirror — new "authoritative vantage." Powerful new Synthesis evidence item
distinguishing `spoofing`/`interception` from benign CDN variance.

#### 3. HTTPS/SVCB records (type 65) — and CNAME-chain / MX / TXT / NS / CAA breadth

**Gap:** only A/AAAA are queried. Modern connectivity increasingly rides SVCB/HTTPS RRs (ECH keys,
HTTP/3 alt-svc, port/ALPN hints).
**Why it matters:** a stripped or tampered HTTPS RR silently breaks ECH / downgrades transport — a real,
growing failure class the tool is blind to. CAA tampering enables mis-issuance; TXT/MX exposure catches
broader hijacks. The full CNAME chain (not just the final address) reveals redirection paths.
**Collect where:** Prophet — add qtypes 65/5/15/16/2/257. Mirror can compare SVCB/ECH across vantages.

#### 4. EDNS0 capabilities + large-response / fragmentation behavior

**Gap:** queries carry no OPT record, so nothing about EDNS0 is observed.
**Why it matters:** EDNS0/large-answer handling is among the most common actual breakages — DNSSEC
responses exceed 512 bytes, UDP fragments get dropped, buffer sizes mismatch, and resolvers fall back to
TCP (or fail). Record the negotiated UDP buffer size, whether the resolver honors the DO bit, EDNS
version/flags, and whether a deliberately large answer survives UDP or forces TCP.
**Collect where:** Prophet — send an EDNS0 OPT (DO set, 1232-byte buffer) and a known-large probe.

#### 5. Full response-flag set + resolver identity (NSID / version.bind)

**Gap:** only `AD` and `TC` are read. `RA`, `AA`, `CD`, `RD`-echo, and RCODE-extended are ignored.
**Why it matters:** `RA=0` means the resolver won't recurse (misconfig/forwarder); `AA=1` on a recursive
answer is suspicious; `CD` state affects DNSSEC meaning. NSID (RFC 5001) and a `version.bind` CH TXT
probe identify the anycast node / resolver software — invaluable for "which box actually answered me"
and for spotting an unexpected middlebox.
**Collect where:** Prophet — parse all header flags; add an NSID EDNS option and a `version.bind` probe.

#### 6. Authenticated denial-of-existence for negatives (SOA + NSEC/NSEC3)

**Gap:** an NXDOMAIN/NODATA is opaque — just the rcode.
**Why it matters:** a genuine NXDOMAIN carries an SOA (with the minimum/negative-cache TTL) and, in a
signed zone, NSEC/NSEC3 records proving the name really doesn't exist. An *injected* NXDOMAIN (blocking /
censorship) typically lacks these. This turns "domain didn't resolve" into "domain was blocked vs
genuinely absent" — a core censorship-detection distinction.
**Collect where:** Prophet/Mirror — capture the authority section on negatives; feed `censorship` vs a
benign-negative status.

#### 7. EDNS Client Subnet (ECS) behavior + ASN/geo of resolver and answers

**Gap:** ECS is never sent, and resolved IPs / the resolver IP are never enriched.
**Why it matters:** whether the resolver forwards your client subnet explains "why do I get a different
IP than my friend," and reveals a privacy leak (your subnet handed to authoritatives). Annotating each
answer IP and the resolver IP with ASN + geo gives Mirror a *reference* for redirection — an answer that
jumps to an unexpected ASN/country is a strong tamper signal that resolver-consensus alone misses.
**Collect where:** Portal/Glutton (ECS + resolver enrichment), Mirror (answer-IP enrichment) — offline
ASN/geo table shipped with the app to stay dependency-free.

#### 8. Encrypted-transport health (active DoH/DoT probe + TLS/cert validation)

**Gap:** `encryptedDNSStatus` infers encryption from a loopback-resolver heuristic (also flagged in
KDNS-23) — it never *proves* the encrypted path works.
**Why it matters:** users think they're on DoH/DoT while it's silently failing open to Do53. Actively
connect to the configured (or well-known) DoH/DoT endpoint, record TLS version, cert chain, pin match,
and whether a query actually completes encrypted. This is the difference between "configured for
encryption" and "encrypted right now."
**Collect where:** Prophet/Glutton — active encrypted-transport probe with cert introspection.

#### 9. Answer stability over repeated queries (+ TTL honoring / cache behavior)

**Gap:** each vantage is queried once.
**Why it matters:** repeating a query a few times exposes round-robin rotation vs genuine instability vs
a resolver that ignores TTLs (a cache-poisoning or broken-cache signal). A resolver returning a
decreasing-then-resetting TTL is normal; a fixed TTL or flapping answers is not. Cheap to collect, high
signal for "intermittent DNS."
**Collect where:** Prophet — N repeats with TTL-decay tracking; new `unstable`/`ttl-anomaly` status.

#### 10. System resolver configuration depth (search/match domains, split-horizon, 0x20 / QNAME-min)

**Gap:** the system-resolver read gets server IPs (and scoped detection is itself buggy — KDNS-28), but
not search domains, per-domain (split-horizon) resolvers, resolver ordering, or hardening posture.
**Why it matters:** split-horizon and search-domain misconfig are extremely common real-world DNS
problems ("works at home, not on VPN"). Per-domain resolvers reveal VPN/enterprise interception scope.
Whether the resolver preserves 0x20 mixed-case and does QNAME-minimization reveals its anti-spoofing
hardening — directly relevant to how much to trust its answers.
**Collect where:** Portal — full `scutil --dns`/`SystemConfiguration` match-domain map; Prophet — a
0x20 case-preservation probe and a QNAME-minimization inference.

**Cross-cutting:** all ten become first-class `EvidenceItem`s / `SectionStatus` inputs in the Synthesis
engine, most with signed weights — several (2, 6, 7) give the engine the *authoritative reference* it
currently lacks, letting it distinguish real tampering from benign CDN/geo variance instead of leaning on
resolver consensus alone.

---

### Part B — Feature opportunities (normal features directive)

#### B1. Turn the ten datapoints into three shipped capabilities

- **"Ground Truth" mode (datapoints 2, 6, 7):** authoritative + denial-of-existence + ASN/geo give a
  real reference, upgrading Mirror from "resolvers disagree" to "resolver disagrees with authoritative,
  and the answer moved to an unexpected network."
- **"Transport Doctor" (datapoints 1, 4, 8):** latency + EDNS0 + encrypted-path health — the
  performance/what's-actually-encrypted story users ask for most.
- **"Resolver Fingerprint" (datapoints 5, 9, 10):** identity + stability + hardening posture — a
  trust dossier on the box answering you.

#### B2. Synthesis / product features

- **Historical trend & drift dashboard.** AnswerLedger/Sentinel already persist snapshots; surface a
  per-datapoint timeline ("your resolver's answer for X changed 3 days ago") — high-value, mostly a
  read-model over data already captured.
- **Shareable signed diagnostic bundle.** WitnessSigner already signs snapshots; export a portable,
  verifiable report a user can hand to their ISP/IT ("here's proof my DNS is being rewritten"). Strong
  differentiator; leans on existing crypto.
- **"Explain this verdict" drill-down.** The engine already produces per-evidence weights and
  probabilities; render the reasoning chain (why it referred, what tipped it) — makes the offline,
  no-ML engine legible and defensible.
- **Continuous/background sentinel mode.** Periodically re-run the canaries + datapoints and alert on
  change, rather than one-shot — moves Syxx from a scanner to a monitor (aligns with Zzyn/Nnyx family).

#### B3. Coverage gaps worth a feature

- **DoT (853) alongside DoH**, and **DNSCrypt** detection — the encrypted-transport story is DoH-only today.
- **IPv6 parity** across LAN probing and service checks (several KTRU/KDNS deltas note v4-only paths).
- **Public-suffix-aware** registrable-domain logic (KTRU-24) unlocks correct blocklist + grouping.

---

### Part C — Prioritization (impact × effort)

| Feature | Impact | Effort | Note |
|---|---|---|---|
| #1 Latency/RTT | High | Low | Timing wrapper around existing probes |
| #9 Answer stability | High | Low | N-repeat loop + TTL tracking |
| #3 SVCB/HTTPS + qtype breadth | High | Med | New qtypes in the existing parser |
| #5 Full flags + resolver identity | High | Low | Parse flags already in the packet |
| #6 Negative-answer proof (SOA/NSEC) | High | Med | Parse authority section |
| #2 Authoritative ground truth | Very High | Med-High | Delegation walk + direct query |
| #4 EDNS0 + fragmentation | High | Med | OPT record + large-probe |
| #8 Encrypted-transport health | High | Med | Active DoH/DoT + cert introspection |
| #7 ECS + ASN/geo | Med-High | Med | Ship an offline ASN/geo table |
| #10 Resolver config depth + 0x20 | Med | Med | SystemConfiguration + case probe |

**Fast wins (low effort, high impact):** #1, #9, #5 — all are additions to data the current probes
already touch, and each immediately upgrades the Synthesis engine's inputs.
**Biggest capability unlock:** #2 (authoritative ground truth) — it gives the engine the reference it
structurally lacks today.

---

## Part D — Detection ROI Ranking (2026-07-06)

What follows ranks every proposal in this document by its contribution to the core mission —
**detecting DNS tampering, hijacking, spoofing, and censorship**, not by UX, platform parity,
or operational convenience. Each proposal is tagged with its origin (`Part A`, `Part D`, `Part E`,
`Part F`, or `Original F-`).

### Tier 1: Foundational Detection — ship first

| # | ID | Proposal | Detection gap closed |
|---|----|----------|---------------------|
| 1 | Part A #2 | **Authoritative ground truth** | VantageComparer has no true north. Querying authoritatives directly converts "resolvers disagree" (could be CDN geo) into "resolver disagrees with authoritative" (tampering). Without this every comparison verdict is structurally uncertain. |
| 2 | Part F #7 | **Full CNAME chain collection + cross-vantage comparison** | CNAME injection is a common hijack technique and we are blind to it — Windows drops CNAMEs entirely, macOS does not follow chains. Chain divergence is near-definitive redirection evidence, stronger than IP comparison. |
| 3 | Part D #A2 | **DNSSEC chain validation** | Current `verifyDNSSEC` checks DNSKEY+RRSIG *presence only* (MAC-042). Real DS→DNSKEY→RRSIG crypto validation is the only way DNSSEC contributes actual evidence instead of false secure verdicts. |
| 4 | Part D #A1 | **Multi-transport corroboration per vantage** | Comparing UDP vs TCP vs DoH vs DoT catches transparent proxies that intercept only one transport (e.g., UDP poisoned but TCP/DoH clean → censorship mechanism fingerprint). |
| 5 | Part A #6 | **Authenticated denial of existence (SOA + NSEC/NSEC3)** | NXDOMAIN is opaque today. Genuine NXDOMAIN carries SOA with negative-cache TTL; signed zones carry NSEC/NSEC3 proving absence. Injected blocking NXDOMAINs lack these. Core distinction we currently cannot make. |

### Tier 2: Strong Detection — build after Tier 1

| # | ID | Proposal | Detection gap closed |
|---|----|----------|---------------------|
| 6 | Part E #B2 | **Censorship mechanism fingerprinting** | Active probes that classify the blocking mechanism (DNS poisoning, TCP reset, SNI filter, HTTP redirect). Turns "something is wrong" into a typed mechanism. |
| 7 | Part F #5 | **Wildcard synthesis detection** | Tests random subdomains under the target domain, compares wildcard IP against the target answer. Decisive NXDOMAIN-hijack evidence when positive. Partially spec'd in DNSCanaries but not domain-specific. |
| 8 | Part F #6 | **Re-query consistency score** | Intermittent poisoning (every Nth query hijacked) is invisible to single-shot probes. 3-5 rapid re-queries with a consistency score catches this at near-zero collection cost. |
| 9 | Part F #4 | **TTL-based anomaly detection** | TTL already parsed but never analyzed. Cross-resolver TTL agreement on the exact millisecond across unrelated providers is a cache-poisoning signal sitting in collected data for free. |
| 10 | Original F1 | **Resolver provenance confidence** | Combines ARP/MAC mapping + DNS capture destination IPs + mDNS/service probes into a typed answer to "who is answering my DNS?" — expected router, unexpected on-link, VPN upstream, or unknown. |

### Tier 3: Useful Detection — additive context

| # | ID | Proposal | Detection gap closed |
|---|----|----------|---------------------|
| 11 | Part F #1 | **EDNS0 capability probe** | Disambiguates "resolver is blocking me" from "resolver is too old to talk to." Basic transport-layer diagnostic. |
| 12 | Part F #8 | **IPv6 vs IPv4 resolution audit + DNS64 detection** | Prevents false tampering alarms on NAT64 networks. Catches address-family-specific censorship. |
| 13 | Part A #1 | **Per-resolver query latency (RTT)** | Cheap signal that qualifies other findings (degraded link vs active block). Does not detect tampering alone. |
| 14 | Part F #3 | **Source port & TXID entropy audit** | Assesses whether the resolver path itself can be trusted. Data already available in DNSCapture passively. |
| 15 | Part A #5 | **Full response-flag set + resolver identity (NSID / version.bind)** | Fingerprints resolver software via CHAOS probes. Identifies unexpected middleboxes. |
| 16 | Part F #10 | **Resolver ECS support & echo** | Explains whether answer diversity is legitimate geo-routing or suspicious. Feeds VantageComparer dampening logic. |
| 17 | Part A #3 | **HTTPS/SVCB records + qtype breadth** | Catches ECH/HTTP3 downgrade and CAA tampering. Real but uncommon attack class today; growing importance. |

### Tier 4: Detection-Adjacent — improve trust, parity, or output, not detection

These proposals are valuable for evidence integrity, platform completeness, or user experience, but
they do not make the app *catch more tampering*:

- **Part A #8** — Encrypted-transport health (active DoH/DoT + TLS/cert introspection) — proves encryption is working, does not detect tampering
- **Part A #7** — ECS + ASN/geo enrichment — offline database for context
- **Part A #9** — Answer stability — overlaps with re-query consistency (Part F #6)
- **Part A #10** — System resolver configuration depth — diagnostic depth
- **Part F #9** — Response size amplification tracking — niche signal
- **Original F2** — Censorship mechanism timeline — synthesis/output improvement, not new detection
- **Original F3** — Evidence confidence & signing — trust infrastructure
- **Original F6** — End-of-test meaning decoder — synthesis/output improvement
- **Original F4 / F5** — GUI readiness / LyxTools parity — platform and feature parity
- **Part D A3** — Collector-correlated leak detection — trust infrastructure for leak tests
- **Part D A4–A10** — Structured traceroute, SRI/TLS inspection, historical store, witness publication, modern nslook, network monitoring, transactional capture — incremental improvements
- **Part E B1, B3–B10** — SyxxSentry, SyxxBridge, SyxxRegistry, SyxxDeepVerify, SyxxFlow, SyxxAutomation, SyxxIdentity, SyxxPortal, SyxxAudit — new product surfaces, not core detection
- **Part C** — CLI/GUI-agnostic service architecture — internal refactor
- **Front-End 1–6** — GUI visualizers (packet inspector, path map, drift matrix, cartography graph, live cockpit, latency charts) — UX only
- **Part B** — Ground Truth / Transport Doctor / Resolver Fingerprint bundles — packaging of Tier 1 items

---

## Part E — External Review: Perplexity's Validation (2026-07-06)

The ROI ranking above is directionally right: the biggest missing datapoints are **authoritative
ground truth, full CNAME chains, real DNSSEC validation, and multi-transport comparison**.
Those four most directly convert ambiguous "resolver disagreement" into evidence of tampering or
censorship, while the rest mostly add confidence, context, or UX.

### What's most valuable

**1) Authoritative ground truth.** Querying authoritative nameservers gives you the reference
answer, which is the cleanest way to distinguish normal CDN geo-DNS variation from actual
resolver manipulation. Without that anchor, resolver-vs-resolver comparisons remain uncertain
by design. [RFC 8198 — the "aggressive NSEC caching" spec — confirms that delegation-based
trust and denial-of-existence are well-established mechanisms the app can rely on.](https://www.rfc-editor.org/rfc/rfc8198.html)

**2) Full CNAME chain collection.** CNAMEs are part of the resolution path, and they matter
because the visible A/AAAA answer can be downstream of an injected or diverted alias chain.
Collecting the full chain lets you compare structure, not just final IPs, which is much stronger
evidence than simple answer-set matching.

**3) Real DNSSEC chain validation.** Validating DS → DNSKEY → RRSIG, plus denial-of-existence
proofs where relevant, is what turns DNSSEC from a "looks signed" check into actual cryptographic
evidence. RFCs on negative caching and NSEC/NSEC3 show that absence proofs and validation failures
are meaningful signals, not just metadata. [RFC 8198](https://www.rfc-editor.org/rfc/rfc8198.html)

**4) Multi-transport corroboration.** Comparing UDP, TCP, DoH, and DoT helps identify
path-specific interference, because a poisoned UDP answer with clean TCP or encrypted DNS
is a strong fingerprint of interception. That directly improves diagnosis of transparent proxies,
filtering, and selective tampering. [Cloudflare's DoT/DoH guidance confirms that encrypted
transport interop issues are a growing source of real-world failure modes the app should
surface explicitly.](https://www.cloudflare.com/learning/dns/dns-over-tls/)

### Why Tier 2 matters

Tier 2 items are strong because they turn a binary "bad/good" verdict into a **mechanism
diagnosis**. Wildcard detection, re-query consistency, and TTL anomalies are especially useful
because they catch attacks that only show up intermittently or via caching behavior.

### Two more high-value datapoints

If the goal is "what datapoints are we not collecting but should," two more merit consideration:

- **Negative-answer proof metadata:** SOA TTL, NSEC/NSEC3 records, and whether the response is
  an authentic NXDOMAIN/NODATA versus an injected block page or forged denial. This is part of
  making "blocked vs absent" distinguishable. [RFC 8198](https://www.rfc-editor.org/rfc/rfc8198.html)
- **Resolver provenance evidence:** the actual source of the answer, including on-link responder
  detection and proxy/loopback classification, so you can tell whether the answer came from the
  expected resolver or from something sitting in front of it. This reduces false attribution
  when multiple local components can answer. [Cloudflare's resolver architecture
  documentation](https://www.cloudflare.com/learning/dns/dns-over-tls/)

### Practical ship-first priority

| # | Datapoint | What question it answers | Attack class detected | Complexity |
|---|-----------|-------------------------|----------------------|------------|
| 1 | Authoritative ground truth | "Is the recursive resolver telling the truth?" | Resolver manipulation, cache poisoning, transparent proxy | Med-High |
| 2 | Full CNAME chain collection | "Was the resolution path altered?" | CNAME injection, alias hijacking, redirection | Med |
| 3 | Real DNSSEC validation + negative-answer proofs | "Is this NXDOMAIN real or injected?" | Forged denial, blocking via NXDOMAIN, DNSSEC stripping | Med |
| 4 | Multi-transport comparison | "Is only one transport tampered?" | Selective interception, transparent proxy, DoH downgrade | Med |
| 5 | Resolver provenance classification | "Who actually answered the query?" | On-path interceptor, rogue resolver, loopback malware | Med |

---

## Part G — v1 Ship Proposals (2026-07-16)

**What this part is.** Capability-level work that must be **coded** before v1 deploys and that
appears in **no existing list** — not in the 20-feature ROI ranking, not in Implementation Plan v2's
Phases A–E, not in Parts A–F above, and not in `DELTAS.md` (defects are that document's job; these
are things that do not exist at all).

**Why it exists.** Every list in this repo ranks *detection capability* — what Syxx can find. None
ranks the machinery a paid app needs to be installed, understood, kept current, supported, or
recovered. Six tree audits on 2026-07-16 found that machinery almost entirely absent. The items
below are the finite remainder: **18 proposals, ~19–26 engineer-days**, which is the number that
matters for scheduling a two-week window.

**Effort convention:** one engineer-day, one platform. "×2" means the macOS and Windows twins are
separate days (`AGENTS.md:36` parity). Estimates assume the existing Kit is reused, not rewritten.

---

### G-A — Client platform services (nothing in this group exists on either platform)

- **G1. Client logging framework.** Neither client logs anything: zero `os_log`/`OSLog` on macOS,
  zero `ILogger`/`EventLog`/`Serilog` on Windows. Structured, leveled, rotating, local-only by
  default, with the same JSON shape the server already emits (`logging_util.py`) so one format spans
  the product. Prerequisite for G3, and for G2's local-first persistence (a crash report must
  survive being offline; Bugsink is a destination, not a buffer).
  *Effort:* 1 ×2.

- **G2. Crash and unhandled-exception capture — via SELF-HOSTED Bugsink.** *(Owner decision,
  2026-07-16.)* No crash handler exists on either client, so a customer crash produces nothing an
  engineer can act on. Bugsink is Sentry-SDK compatible, so `sentry-cocoa` and `sentry-dotnet` (both
  MIT) give one integration pattern across both clients instead of two bespoke handlers, and the
  same DSN can also take the Python server's exceptions, which today go to journald and die there
  (nothing alerts on `handler_exception` at all). One tool, both halves of the product. Windows additionally
  needs its 11 `async void` event handlers wrapped; an exception in any of them kills the process.

  **Self-hosting is not a preference here, it is the only coherent option.** Syxx's pitch is "we
  tell you what leaves your machine." A crash reporter posting to a third party contradicts the
  product in a way a competitor would enjoy quoting. Bugsink runs on our host; the data stays ours.

  ***THE SCRUBBING CONTRACT — this is the acceptance bar, not a nice-to-have.*** Sentry SDKs are
  chatty by default: breadcrumbs, local variables, HTTP context, device info. For a tool whose
  working set is **the domains the user just looked up**, a default-configured crash report
  exfiltrates *someone's browsing history to our server*. For this product that is not a bug, it is
  a headline. Therefore:
  - `beforeSend` scrubbing, **denylist-by-default** — strip anything carrying a hostname, QNAME,
      resolver IP, licence key, or capture payload.
  - **Breadcrumbs off**, or filtered to a hardcoded allowlist of event types. The default
      breadcrumb trail is where DNS data will leak.
  - **No local-variable capture** (`includeLocalVariables = false` on .NET; the cocoa equivalent).
  - The `MachineFingerprint` **hash** may travel; the raw fingerprint never.
  - Gated behind G12 consent. Crash payloads are unbounded; the existing phone-home is two fields.
  *Bar:* crash the app **during a live Prophet run** and read what actually arrives at Bugsink. A
  scrubber's presence is not proof it scrubs — the same lesson the collector key taught (a non-empty
  variable holding a wrong-format key read as "configured").
  *Effort:* 0.5 ×2 SDK wiring + 1 scrubbing contract & its adversarial test + 0.5 Bugsink deploy.

- **G3. User-sendable diagnostics bundle.** Complements G2 rather than duplicating it: Bugsink
  answers "it crashed," this answers "it's behaving wrong but not crashing," which is most support
  tickets for a diagnostic tool. One button → a redacted archive (logs, versions, last verdict,
  machine identity **hash**, never the raw fingerprint or licence key) the customer attaches to a
  support mail — reusing G2's scrubber, not a second one. Works offline and needs no consent, since
  the user chooses to send it. Reuses the redaction discipline already in `fulfillment.py:123`
  (audit by opaque id, never PII).
  *Effort:* 1 ×2 (down from 1.5 — the scrubber comes from G2).

- **G4. Settings surface.** There is no settings UI of any kind on either platform. Minimum viable
  set: resolver selection, scan scope/frequency, notification preferences, telemetry opt-out
  (G12), diagnostics location, and data removal. Backed by a typed store, not scattered
  `UserDefaults`/registry reads.
  *Effort:* 2 ×2.

- **G5. Notification delivery.** Monitoring that cannot tell the user anything is a log file.
  `UNUserNotificationCenter` (macOS) / `AppNotificationManager` (Windows), permission requested at a
  moment the user understands why, respecting G4 preferences, with a click-through to the finding.
  **Hard prerequisite for the subscription value proposition** — without it, continuous monitoring
  and the trust indicator are decorative.
  *Effort:* 1.5 ×2.

- **G6. Network-change detection.** The product exists for the moment you join a café's wifi, and
  nothing detects that moment. `NWPathMonitor` (macOS) / `NetworkInformation.NetworkStatusChanged`
  (Windows) → debounce → re-evaluate → feed the indicator and monitoring. Also the natural trigger
  for the first-run scan on a *new* network.
  *Effort:* 1.5 ×2.

### G-B — Licence lifecycle (the parts Keygen does not give you)

- **G7. Offline entitlement grace.** Every entitlement check is a live 8-second HTTP round trip. A
  paying customer with no internet is locked out and told their trial expired. Cache the last
  successful validation with a signed timestamp, honour it for a bounded grace window, and surface
  the state honestly ("verified 2 days ago, offline since"). The tri-state
  (`LicenseValidationResult`) already exists to carry this and is discarded at the seam.
  **The failure mode is a filtered network — the exact condition this product diagnoses.**
  *Effort:* 1.5 ×2.

- **G8. Licence state machine.** Cancelled, suspended, expired-trial, bad-key, and offline currently
  collapse to one string. Model them as distinct states with distinct copy and distinct next
  actions, on both clients, driven by what the server actually returns.
  *Effort:* 1 ×2 (+0.5 server to return a reason code).

- **G9. Operator support tooling.** A ticket saying *"my key doesn't work, I'm jane@example.com"* has
  no queryable path — correctly, since we store no PII. A small admin CLI: look up by Stripe session
  or customer id, show mint/delivery state, resend the key email, reissue. `db.license_upsert` exists
  with zero callers; the resend path is unreachable outside the webhook handler.
  *Effort:* 1.5 (server only).

### G-C — Distribution machinery

- **G10. Build-time version stamping, single source of truth.** The version is three hand-synced
  literals (`SyxxConfig.swift:8`, `SyxxConfig.cs:13`, `Info.plist`) plus an `app.manifest` and a
  hardcoded `print("Syxx 1.0.0")`, already mutually inconsistent. Every macOS build ships as `1.0`
  because the plist is written by a heredoc. One version, injected at build, asserted by a test.
  **Any update mechanism is impossible without this** — an updater compares versions.
  *Effort:* 1 (both platforms; it is one pipeline change each).

- **G11. Update mechanism.** The check is disabled by default (`config.py:63-64` blank), so the app
  can never learn it is out of date; the "update" button opens a dead URL. Sparkle (macOS) /
  NetSparkle (Windows), EdDSA-signed appcast, hosted on the server that already serves the MOTD.
  Depends on G10 and on signing (owner-track).
  *Effort:* 2 ×2.

- **G12. Telemetry disclosure and consent.** The app phones home once per launch with no disclosure
  and no opt-out. The payload is already minimal and privacy-conscious (`platform` + `version`,
  no identifier) — which makes this cheap: a first-run disclosure, a settings toggle (G4), and a
  "what leaves your machine" page. Gate G2/G3 uploads behind it.
  *Effort:* 1 (+0.5 web page).

- **G13. In-app support surface.** No support address exists anywhere in either client; `/help` is a
  dead route. A Help menu item, a support address, and a failure path from the licence screen that
  tells a stuck customer what to do — wired to G3 so the diagnostics come attached.
  *Effort:* 0.5 ×2 (+0.5 server route).

### G-D — Purchase completion (server + web)

- **G14. Purchase return pages.** Stripe returns paying customers to `/purchase/success`, which does
  not exist — the public web surface is a three-entry allowlist. Serve success and cancel. **Success
  must render the licence key on-page**, not merely promise an email: the key exists at that moment,
  and email is a single point of failure that is currently unconfigured.
  *Effort:* 1 (server).

- **G15. Billing self-service.** No way to update a card, view invoices, or cancel from the app.
  Stripe's billing portal is a session-create call plus a button; without it every billing question
  becomes a support ticket you have no tooling to answer (G9).
  *Effort:* 0.5 (server) + 0.5 ×2 (button).

### G-E — Linux platform services (the client exists; the platform layer does not)

- **G16. Linux credential persistence.** `LicenseManager` stores via **DPAPI (`crypt32.dll`)**,
  Windows-only. On Linux the save throws and the CLI prints *"you will need to enter it again next
  launch."* **A paying Debian customer re-types their key on every launch**, and Debian SKUs are live
  in Stripe today. libsecret with an XDG-path fallback.
  *Effort:* 1.5.

- **G17. Linux privilege elevation.** All privilege logic is `WindowsPrincipal` + UAC `runas`; there
  is no `geteuid` check anywhere in the tree, so `cage` cannot run. Per the parity ruling
  (2026-07-16): one accomplishment, three mechanisms — `pkexec` graphical prompt, `sudo` re-exec
  fallback, gated on a real `geteuid()==0` check.
  *Effort:* 1.

- **G18. `.deb` packaging with hardware-id capture.** No packaging exists. The `postinst` must invoke
  `scripts/syxx-capture-hardware-id.sh` — the script is written, correct, and **orphaned**: its own
  header names `.deb postinst` as the intended caller and that caller does not exist. Without it every
  Linux install is `InstallScoped`, never node-lockable, and a dual-boot burns two activations.
  *Effort:* 1.5.

---

### Part G — schedule

| Group | Proposals | Days |
|---|---|---|
| G-A Client platform services | G1–G6 | **~11–14** (Bugsink trims G2/G3; the scrubbing contract adds it back) |
| G-B Licence lifecycle | G7–G9 | **~4** |
| G-C Distribution machinery | G10–G13 | **~5–7** |
| G-D Purchase completion | G14–G15 | **~2** |
| G-E Linux platform services | G16–G18 | **~4** |

**Total: ~19–26 engineer-days.** Against a two-week window that is the whole budget with nothing left
for the roadmap's own Phase A wiring — which is the finding, not a complaint. Two weeks buys **either**
the platform services **or** the product layer, not both.

**If forced to choose, the ship-critical subset is ~9 days:** G14 (customers can complete a purchase),
G7 + G8 (paying customers stay paying), G10 (nothing can ship twice without it), G16 + G17 + G18
(Linux is sellable or it is not), G9 (you can answer a support ticket), G13 (they can reach you).
Everything else — logging, crash capture, settings, notifications, network-change, updates, billing
portal, consent — is a v1.1 that starts the day v1 ships.

**The honest consequence of deferring G5 + G6:** continuous monitoring and the trust indicator have no
trigger and no way to speak. They are the subscription. Deferring them means v1 is a paid scanner, and
the pricing and copy should say scanner.

**Bugsink note (owner decision, 2026-07-16).** Self-hosted Sentry-compatible error tracking, chosen for
G2. It is worth more than its line item: the same DSN takes the Python server's exceptions, which today
reach journald and stop there — so it also closes half of "nothing alerts on anything" for free. Two
strings attached. First, it runs on the same box as everything else, which does not yet have a backup;
that makes the backup proposal more urgent, not less, because there is now a second thing on that disk
you cannot afford to lose. Second, and non-negotiable: **the scrubbing contract in G2 is the acceptance
bar.** A default Sentry SDK on a tool that inspects DNS will ship the user's browsing history to us in a
breadcrumb trail. Nothing else in this document can hurt a customer the way that would.
