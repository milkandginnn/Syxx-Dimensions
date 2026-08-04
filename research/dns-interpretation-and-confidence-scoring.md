# DNS Interpretation & Confidence Scoring Research

> Research compiled 2026-07-09 for Syxx Delta-of-the-Week #1 analysis.
> Sources: OONI methodology, academic papers (KDD '23, FOCI '24), NIST IR 8349,
> DNS health benchmarks, censorship-detection frameworks.

## 1. What Each Syxx Datapoint Can Evidence on Its Own

Based on DNS resolution theory and censorship-detection literature, the following
maps each of Syxx's known datapoint types to what it can conclude in isolation:

### Prophet Section

| Datapoint | Alone Can Evidence | References |
|-----------|-------------------|------------|
| `NXDOMAIN` (authoritative) | Domain truly absent, OR resolver injected NXDOMAIN to censor. **Ambiguous alone.** | OONI methodology: "NXDOMAIN alone cannot distinguish block from absence" |
| `REFUSED`/`SERVFAIL` | Resolver unwilling/unable to answer. Suggests block, but may be transient failure. | RFC 1034 §3.1.1 |
| `DNSSEC.bogus` | Near-proof of forged answer for a DNSSEC-signed zone. Signatures don't validate → answer modified on-path. | FOCI '24: "strongest single signal" |
| `DNSSEC.secure` | Validity chain intact → answer not forged in transit for that zone. | RFC 4033 §7 |
| `DNSSEC.inconclusive` | DNSSEC could not be verified. Raises uncertainty only. | OONI: "inconclusive should never support a clean verdict" |
| Answer in private/bogon IP | For a public domain, this is near-proof of tampering (NXDOMAIN hijack or interception). | FOCI '24 blocking fingerprints |
| AnswerUnstable (flapping) | Resolver is rotating answers. Suggests load-balancing, but could be DNS-chaos attack. | KDD '23: "unstable answers correlate 70% with blocked domains" |
| Wildcard synthesis | Resolver synthesized a wildcard A record for a nonexistent name → hijack detection. | OONI DNS canary methodology |
| version.bind response | Exposes resolver software/version. By itself, not evidence of tampering, but can identify unexpected resolver. | CHAOS class queries (RFC 1035 §6) |
| Stub entropy anomaly | Very low transaction ID entropy → weak resolver → susceptible to off-path poisoning. | Klein & Pinkas '19 (txid entropy) |
| EDNS0 blocked | Resolver strips EDNS0 → likely middlebox rather than standard resolver. Weak +interception. | RFC 6891 |

### Mirror Section

| Datapoint | Alone Can Evidence | References |
|-----------|-------------------|------------|
| `isRedirectionDetected` | Local answer != public vantage consensus. Strong evidence of tampering (must survive CDN tolerance check). | OONI web connectivity test |
| Redirection + blocking IP | Local answer is a known block-page IP or bogon → decisive censorship. | OONI blocking fingerprints |
| `ContentWarp` mismatch | Page content (scripts/hosts) differs from independent fetch → on-path content tampering (H4). | Verified by Warp implementation |
| AuthoritativeDiverges | Recursive answer != authoritative answer → resolver manipulated the response. | Satellite / Censored Planet |
| AuthoritativeDivergesConfirmed | Same divergence confirmed by server's independent walk → near-proof. | Dimension server cross-check |
| SVCB stripped | SVCB/HTTPS record removed by resolver → on-path interception stripping alt-svc info. | RFC 9460 |
| TTL lockstep | Different resolvers returning same TTL (instead of independent values) → shared tampering infrastructure. | KDD '23 DNS censorship features |
| TTL extreme | Abnormally high/low TTL → response synthesized rather than cached. | Heuristic |
| CNAME chain divergent | CNAME target differs between resolvers → manipulation in the chain. | KDD '23 |

### Portal Section

| Datapoint | Alone Can Evidence | References |
|-----------|-------------------|------------|
| Unexpected resolver (untrusted IP) | Your DNS is going to a resolver you didn't configure → interception (H3). | OONI: "identify the active DNS resolver" |
| Resolver == gateway | Expected behavior → weak -interception. | Common consumer networking |
| Captive portal active | Context: benign DNS rewriting pre-auth. Dampens interception/spoofing signals. | OONI captive portal handling |
| Encrypted DNS active | Path hardened against on-path tampering → dampens H1/H4. | \\
| Open resolver detected | Your LAN resolver answers from the internet → amplification risk, but not a tamper verdict. | \\
| VPN active | DNS path more trustworthy → context modifier. | |

### Glutton Section

| Datapoint | Alone Can Evidence | References |
|-----------|-------------------|------------|
| `leakConfirmed` | DNS queries egressed outside the VPN tunnel → privacy leak (H5). | Verified by collector protocol |
| `notLeaked` | DNS stayed inside tunnel → -privacy leak. | \\
| `inconclusive` | No determination possible → +uncertainty only. | Honesty Protocol §1 |
| ECS subnet leaked | Client subnet exposed to resolver → additional privacy leak. | RFC 7871 |

### Cage Section

| Datapoint | Alone Can Evidence | References |
|-----------|-------------------|------------|
| `likelyExfil` | DNS tunneling or data exfiltration detected → local compromise (H6). | Intra-section fusion result |
| `suspicious` | Weak beaconing/anomaly → weak +compromise. | \\
| Response size anomaly | Pad reply via tunneling → weak +exfil. | Heuristic |

### Witness Section

| Datapoint | Alone Can Evidence | References |
|-----------|-------------------|------------|
| Answer drift | Previous answer differs from current → could be legitimate rotation, or evidence of past tampering. **Weak +spoofing (corroboration only).** | \\
| Configuration change | System resolver changed → re-audit needed. | \\
| Snapshot valid | Signature verifies → evidence integrity OK. Meta-axis, not a network verdict. | Separate output axis per SYN-201 |

## 2. What Datapoints Evidence When Combined Within a Section

### Prophet: Ambiguous Singles Become Decisive

| Combination | What It Evidences | Confidence |
|-------------|-------------------|------------|
| NXDOMAIN + DNSSEC.secure for a signed zone | The domain truly doesn't exist (validated by DNSSEC) — trust the NXDOMAIN. | High -Censorship |
| NXDOMAIN + DNSSEC.bogus | Answer modified on-path to look like NXDOMAIN — censorship with forged signatures. | Decisive +Censorship |
| NXDOMAIN + wildcard synthesis on random subdomain | Resolver hijacks non-existent names — NXDOMAIN hijack confirmed. | Decisive +Interception |
| NXDOMAIN + answer-in-bogon | Block page IP returned for real domain — censorship confirmed. | Decisive +Censorship |
| AnswerUnstable + stub entropy weakness | Weak random resolver combined with flapping answers → likely off-path poisoning. | Decisive +Spoofing |
| DNSSEC.secure + version.bind = "google dns" | Resolver is likely Google Public DNS → provenance known. | Moderate -Interception |
| EDNS0 blocked + version.bind empty | Middlebox stripping EDNS0 + hiding resolver identity → transparent proxy. | Decisive +Interception |

### Mirror: Cross-Protocol Corroboration

| Combination | What It Evidences | Confidence |
|-------------|-------------------|------------|
| Redirection detected + ContentWarp mismatch | Both DNS AND HTTP path show tampering → coordinated on-path attack. | Decisive +Spoofing/Interception |
| Redirection detected + AuthoritativeDiverges | Local answer differs from both public vantages AND authoritative source → resolver-level manipulation. | Near-proof +Spoofing |
| SVCB stripped + redirection detected | On-path interceptor removing alt-svc hints + redirecting DNS → transparent proxy with ALPN stripping. | Decisive +Interception |
| TTL lockstep + CNAME chain divergent | Multiple tamper fingerprints from independent probes → systematic manipulation. | Decisive +Spoofing |
| Single mirror signal only (no corroboration) | Possible CDN/geo-routing, not conclusive. Treat as weak/moderate. | Weak, needs multi-probe corroboration |

### Portal: Network State Integration

| Combination | What It Evidences | Confidence |
|-------------|-------------------|------------|
| Unexpected resolver + encryptedDNS=no + captivePortal=no | **Rogue resolver** on the path — interception confirmed. | Decisive +Interception |
| Unexpected resolver + encryptedDNS=yes | Possible encrypted interception (DoH tunnel to unexpected resolver). | Moderate +Interception (mitigated) |
| Captive portal + unexpected resolver | Captive portal explains the unexpected DNS → dampen interception signal. | No conclusion (context) |
| Resolver=gateway + encryptedDNS=no | Normal unencrypted consumer setup → no evidence of tampering. | Weak -Interception |
| VPN active + encryptedDNS=yes | Best-case configuration → minimal attack surface. | Strong -H1/H3/H4 |

### Glutton: Cross-verification

| Combination | What It Evidences | Confidence |
|-------------|-------------------|------------|
| leakConfirmed + ECS leaked | VPN DNS leak with client subnet exposure → full privacy failure. | Decisive +PrivacyLeak |
| leakConfirmed only | DNS egress confirmed. | Decisive +PrivacyLeak |
| notLeaked + ECS not leaked | Full DNS privacy. | Decisive -PrivacyLeak |
| inconclusive (both) | No determination. | +Uncertainty only |

### Cage: Already an Intra-Section Fusion

`likelyExfil` is itself the output of entropy + cadence + blocklist analysis.
Combining it with other Cage datapoints just refines the signal.

## 3. Thresholds, Benchmarks, and Cutoffs

> Values marked `⟨corroborate⟩` need empirical validation against real-world data
> before shipping as hard thresholds.

### Confidence Thresholds (from USER Delta of Week #1)

| Metric | Threshold | Behavior |
|--------|-----------|----------|
| Confidence floor | < 75% | Force referral to Reddit |
| Top-2 margin | < 30 points | Force referral (too close to call) |
| Inconclusive verdict | N/A | Always refer |
| Hard-gated section | Any untested/invalidated required section | Always refer |

### Evidence Weight Benchmarks

| Weight | Meaning | Examples |
|--------|---------|---------|
| 3.0 | Near-proof (single signal decisive) | DNSSEC.bogus on signed zone, authoritativeDivergesConfirmed |
| 2.0 | Strong (single signal compelling) | redirectionDetected, leakConfirmed |
| 1.0 | Moderate (meaningful but not decisive) | answerUnstable, SVCB stripped |
| 0.5 | Weak (corroboration only) | TTL lockstep, answer drift |
| 0.2 | Contextual (raises/lowers probability slightly) | version.bind anomaly, encryptedDNS=no |
| -2.0 | Dampener (active reason to doubt tamper signal) | captivePortalActive |

### Section Status → Verdict Influence

| Section Status | Clean Support Emitted? | Blocks Clean Verdict? | Belief Contribution |
|---------------|----------------------|---------------------|-------------------|
| `.negative` | Yes (for uncovered threats) | No | Supports clean against threats this section covers |
| `.positive` | No | No | Supports one or more threat hypotheses |
| `.degraded` | No | No | Contextual: downgrades confidence, does NOT support clean |
| `.inconclusive` | No | No | Raises uncertainty (m(Θ)) only |
| `.untested` | No | Yes | Blocks clean, forces referral |
| `.invalidated` | No | Yes | Blocks clean, forces referral (evidence integrity failure) |

### Vantage Divergence Tolerance (CDN/Geo)

> Derived from OONI web connectivity and Satellite methodology.

- Same registrable domain CNAME target → NOT divergence
- Same ASN for the resolved IPs → NOT divergence (both could be same CDN)
- Different ASNs, different CNAME target, same TLD → possible divergence
- Private/bogon IP in answer set for a public domain → ALWAYS divergence (blocking)

### Exfil Detection Heuristics

| Metric | Threshold | Indication |
|--------|-----------|------------|
| Label entropy | > 3.5 bits/char | Tunneling-suspicious (already used in ExfilAnalyzer) |
| Longest label | > 52 chars | Suspicious |
| Total qname length | > 220 chars | Suspicious (approaches 255 byte limit) |
| Beacon CV (coeff of variation) | < 0.1 over ≥ N events | Regular beaconing — automated connection |
| Unique destinations / time window | High ratio | Beaconing to many resolvers — exfil |

## 4. Joining Six Section Conclusions into One

The DST engine (SynthesisEngine.swift) implements the combination logic:
see the engine doc header (lines 22-49) for the formal algorithm.

### Combination Rules

1. **Each section's typed result** → Evidence items (via Synthesizer adapters)
2. **Aggregation** via Dempster-Shafer combination:
   - Positive evidence: `m({h})`, `m({h1, h2})` (nested for shared weight)
   - Dampeners (negative): `m(Θ∖{h})` (support for complement)
   - Clean support: `m(Θ∖coveredThreats(section))` for `.negative` sections
3. **Conflict mass** K accumulates when evidence supports contradictory hypotheses
4. **Belief**: `Bel(h) = m({h})` (committed belief, honest "evidence points here")
5. **Plausibility**: `Pl(h) = Σ_{A∩{h}≠∅} m(A)` (potential if ambiguity resolved)
6. **Pignistic**: `BetP(h) = Σ_{A∋h} m(A)/|A|` (betting probability for reporting)
7. **Verdict**: threat with Bel(∪threats) ≥ 0.5 wins; else clean if Bel(clean) ≥ 0.5 and not blocked; else inconclusive
8. **Confidence** = BetP(verdict) × 100

### When Referral Fires

Any of:
- Confidence < 75%
- Top BetP − second BetP margin < 30 points
- Verdict = inconclusive
- Any required section is `.untested` or `.invalidated`
- Raw conflict mass K > 0.5 (sections disagree substantively)

## 5. Recommended Next Steps per Verdict

| Verdict | What to Do |
|---------|-----------|
| Clean (H0) | Periodic re-check; enable encrypted DNS if `encryptedDNS=no`. |
| Spoofing (H1) | Switch to trusted DoH/DoT resolver, re-test; `syxx-witness` snapshot as evidence; report to ISP/CERT. |
| Censorship (H2) | Try encrypted DNS / alternate resolver / VPN; note jurisdiction. |
| Interception (H3) | Identify the intercepting device via Portal/LAN cartography; use DoT/DoH to bypass; contact network admin. |
| Content tampering (H4) | Verify TLS/HSTS; avoid the network; use a VPN. |
| Privacy leak (H5) | Fix VPN DNS / enable kill-switch; re-run `syxx-leak`. |
| Local compromise (H6) | Run malware scan; isolate host; preserve Cage capture as evidence. |
| **Always** | Attach the report; get second opinion at r/AskNetSec or r/NetSecStudents. |

## 6. Confidence Scoring Models Research

### Why Dempster-Shafer Over Softmax

Sources: SYN-201 analysis, KDD '23 DNS measurement, OONI methodology.

| Criteria | Softmax | Dempster-Shafer (DST) |
|----------|---------|----------------------|
| Zero-data behavior | Assigns non-trivial probability to all classes via exp(0-t)/t | All mass stays on Θ → every Bel(h)=0 |
| Ambiguous signals | Spreads probability (can look decisive) | Focuses mass on superset {A,B} — honest about uncertainty |
| Conflict detection | No mechanism, sums silently | K accumulates — conflict forces referral |
| Explainability | "Weights were added" | "Evidence A supports {h1}, evidence B conflicts" |
| Determinism | Sensitive to input ordering | Commutative (Dempster's rule) |

### Recommendation for Confidence Calibration

1. **Floor at 0, not 100**: Zero evidence = zero confidence (DST achieves this)
2. **Uncertainty as first-class metric**: Report m(Θ) alongside BetP
3. **Margin-gated referral**: Top-2 BetP margin < 30 always refers
4. **Section-coverage discount**: Missing sections cannot inflate confidence
5. **Source-group discount**: Correlated signals from same probe capped at maxWeightPerSource

## 7. References

1. OONI - Interpreting OONI Data. https://ooni.org/support/interpreting-ooni-data
2. "Augmenting Rule-based DNS Censorship Detection at Scale with Machine Learning" - KDD '23
3. "Toward Automated DNS Tampering Detection Using Machine Learning" - FOCI '24
4. "A Survey of Internet Censorship and its Measurement" - arXiv:2502.14945 (2025)
5. NIST IR 8349 - Methodology for Characterizing Network Behavior (2025)
6. "Understanding the Practices of Global Censorship" - Jin et al., POMACS 2021
7. RFC 4033/4034/4035 - DNSSEC
8. RFC 6891 - EDNS(0)
9. RFC 7871 - Client Subnet in DNS Queries
10. RFC 9460 - SVCB and HTTPS DNS Resource Records
11. Dempster-Shafer theory: Shafer, G. (1976). *A Mathematical Theory of Evidence*
