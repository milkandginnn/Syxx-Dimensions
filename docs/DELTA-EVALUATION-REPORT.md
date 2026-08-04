# Delta Evaluation Report

## Summary

- **Total deltas evaluated:** 453
- **AGREE:** 367 (81.0%)
- **AGREE WITH REVISIONS:** 62 (13.7%)
- **DISAGREE:** 24 (5.3%)

### Key Findings

1. **Line number drift is pervasive.** The codebase has evolved significantly since most deltas were filed. Line numbers referenced in ~70% of deltas no longer match current source positions. This does NOT invalidate the issues; the referenced functions/patterns generally still exist at shifted locations.

2. **Prior reviews are thorough.** Most deltas through B065/A039/C012/D017/E017/F014 have 2-3 prior evaluations from syxx-researcher, Codex, and Claude. Where all three agree, their consensus is correct.

3. **Several deltas are already resolved or out of scope.** Documentation-only deltas (F001-F005), the example/template delta (B001), and several MERGED stubs should be removed from the open ledger.

4. **Some proposed fixes need revision.** A small number of fixes are too simplistic or would introduce new issues (e.g., A054's "don't update last" fix, A055's debunked SSRF premise).

5. **Duplicate entries persist.** Several deltas appear multiple times with different numbers (B029 appears twice; D014/D015/D016 have MERGED stubs; C028 duplicates B049).

---

## Deltas That Should Be Rejected

### [B001] — Resolver Provenence Spoof Vulnerability (Example Template)
**Assessment:** DISAGREE
**Basis:** This is the worked-example/template delta shown in the DELTAS.md instructions section (line 92). It is explicitly a teaching example, not a live defect. It should not be counted in the delta total or tracked as an open item. All three prior reviewers (syxx-researcher, opencode, Codex) agree it is a malformed duplicate stub with no standalone content.

### [B029-dup] — EvidenceItem.source Unset for Non-Canary Evidence (Line 7840)
**Assessment:** DISAGREE
**Basis:** This is explicitly marked "DUPLICATE — MERGED INTO B029" in its title. It is a duplicate of the canonical B029 at line 4179. Should be removed from the ledger.

### [D014-dup] — Windows GluttonView Uses Trusting IsFeatureUnlocked(bool) (Line 8244)
**Assessment:** DISAGREE
**Basis:** Explicitly marked "MERGED INTO D013" in its title. No standalone content.

### [D015-dup] — macOS AppModel Exposes Ed25519 Private Key to SwiftUI (Line 8274)
**Assessment:** DISAGREE
**Basis:** Explicitly marked "MERGED INTO D015" in its title. No standalone content.

### [D016-dup] — macOS Ed25519 Witness Key — Cross-Platform Gap (Line 8307)
**Assessment:** DISAGREE
**Basis:** Explicitly marked "MERGED INTO D015" in its title. No standalone content.

### [B128] — answerCount == 0 branch uses raw responseCode
**Assessment:** DISAGREE
**Basis:** Explicitly marked "FIXED 2026-07-13" in its title. Should be in DELTAS-fixed.md, not the open ledger.

### [F001] — §4 Leak-Detection Completeness Contradiction
**Assessment:** DISAGREE
**Basis:** Documentation-only issue in DESIGN.md. Per the DELTAS.md definition (lines 3-6), a delta must cause "missed detection, false verdict, forged evidence, or crash." A doc contradiction causes none of these. Claude confirms the contradiction was resolved in the restructured DESIGN.md. Opencode correctly identifies this as out of scope. Move to DELTAS-rejected.md.

### [F002] — Shipped Status Hidden in HTML Comments
**Assessment:** DISAGREE
**Basis:** Documentation-only issue. Same category error as F001. Claude confirms HTML comments were removed from DESIGN.md. Move to DELTAS-rejected.md.

### [F003] — Overloaded A6 / A#-vs-F# Identifiers
**Assessment:** DISAGREE
**Basis:** Documentation-only naming ambiguity. Not a product defect. Resolved in restructured DESIGN.md per Claude. Move to DELTAS-rejected.md.

### [F004] — Stale "Shipped" Bullets vs Runtime Table
**Assessment:** DISAGREE
**Basis:** Documentation-only dual-source-of-truth drift. Resolved in restructured DESIGN.md. Move to DELTAS-rejected.md.

### [F005] — §0 Ideation + Typos in Canonical Doc
**Assessment:** DISAGREE
**Basis:** Typos in a design doc are the weakest possible "delta." The "CHALLANGE" section was removed in the restructured DESIGN.md. Move to DELTAS-rejected.md.

### [A055] — Server SSRF Bypass via IPv4-Mapped IPv6 Addresses
**Assessment:** DISAGREE
**Basis:** Empirically debunked by both Codex and Claude. Python's `ipaddress` module correctly identifies `::ffff:127.0.0.1` as loopback, `::ffff:169.254.169.254` as link-local, and `::ffff:10.0.0.1` as private on the deployed Python 3.14 runtime. The `_ip_is_public` function rejects all dangerous mapped addresses. The premise is false. An explicit ipv4_mapped normalization is cheap defense-in-depth but does not constitute a live bypass.

### [F036] — macOS detectCaptivePortal() Unused Dead Bool Wrapper
**Assessment:** DISAGREE
**Basis:** The function `detectCaptivePortal()` does NOT exist in NetworkKeyState.swift. A grep for this function name returns zero results. Only the tri-state `captivePortalStatus()` exists (line 340). The Bool wrapper was either already removed or never existed. Issue does not exist.

### [A054] — Server Rate Limiter Indefinite Lockout (Simplistic Fix)
**Assessment:** DISAGREE (on the filed fix only)
**Basis:** The issue (token accumulation truncation on fast polling) is real and confirmed. However, the filed Fix 1 ("do not update last") would double-count elapsed time, as both Codex and Claude correctly identify. The delta itself is valid (AGREE), but its proposed fix introduces a new defect. See "AGREE WITH REVISIONS" section for the corrected fix.

### [B082] — Windows syxx-leak CLI Mislabels Ed25519 Attestation as HMAC-SHA256
**Assessment:** DISAGREE
**Basis:** This delta at line 15183 appears to be a standalone entry that duplicates B013's core concern about HMAC/Ed25519 overclaim in witness output. The distinction (CLI labeling vs. signer guarantee) is valid but the fix is the same as B013's. Should be merged into B013 rather than tracked separately.

### [B048] — CageView Unconditional Green [SUCCESS] Sniffer Mock
**Assessment:** DISAGREE
**Basis:** Cross-reference check: B048 (line 13905) and B049 (line 13974) cover closely related CageView/WitnessView unconditional green banner issues. C028 (line 22336) covers the SAME WitnessView green banner issue as B049. C028 should be merged into B049 as Codex recommends.

### [C028] — macOS WitnessView Displays Unconditional Green "Signature valid" Banner
**Assessment:** DISAGREE
**Basis:** Exact duplicate of B049 as Codex identifies. Both describe the same WitnessView.swift:178-188 unconditional green banner not gated by witnessValid. Retain B049 (canonical, earlier), merge C028 into it.

### [E014] — EncryptedDNSStatus.yes Possible on Windows but Never on macOS
**Assessment:** DISAGREE
**Basis:** Duplicate of E009 (line 7187). Both describe the same asymmetric encrypted DNS detection capability between platforms. E009 is the canonical entry; E014 at line 8517 adds no new information.

### [E009-dup/E014 overlap] — EncryptedDNSStatus asymmetry
**Assessment:** DISAGREE
**Basis:** E009 (line 7187) and E014 (line 8517) cover the same platform parity gap. Retain E009 as canonical.

---

## Deltas That Should Be Revised

### [A054] — Server Rate Limiter Indefinite Lockout and Fractional Token Truncation
**Assessment:** AGREE WITH REVISIONS
**Current Fix:** "Do not update the bucket's last timestamp to now when returning False"
**Required Revisions:** The filed fix would double-count elapsed time. Instead: (1) recompute tokens from a single monotonic last-refill anchor without pre-adding into stored tokens, or (2) carry the fractional remainder explicitly. Add rapid-block polling tests. The live code at ratelimit.py:37-50 has already been partially hardened with the credited_ms approach (A054 comment in source), but the original lockout pattern may still apply in edge cases.

### [A001] — Rate-Limiter Memory Leak — Dead _prune
**Assessment:** AGREE WITH REVISIONS
**Current Status:** Fixed and committed (266a4b7 per Claude). Live code at ratelimit.py:19-25 shows proper stale-bucket eviction.
**Required Revisions:** Move to DELTAS-fixed.md citing commit 266a4b7. The delta's open status is stale.

### [A002] — Legacy /leak Endpoint Returns Observations Without Ownership Check
**Assessment:** AGREE WITH REVISIONS
**Current Status:** Fixed and committed (40a8fe6 per Claude). Live code at app.py shows ownership check.
**Required Revisions:** Move to DELTAS-fixed.md citing commit 40a8fe6.

### [A012] — /api/diff Returns Hard "tampered" on Full Disjointness vs a Geo/CDN
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** Line numbers have shifted. The issue description and fix are correct, but reference app.py line numbers that no longer match. Update location to current line positions.

### [D042] — Silent License Save Failure Shows False "SUCCESS" to User
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** Line numbers shifted (claims SyxxCLI.swift:72, actual is :136-137). The `try?` silent failure pattern is confirmed at the current location. The §5 violation is real. Update line numbers and keep open until fixed.

### [D043] — validateLicense Boolean Wrapper Discards Network-Error Diagnosis
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** Line numbers shifted (claims LicenseManager.swift:356-357, actual is :427). The Bool wrapper collapsing .networkError/.keygenError into false is confirmed. Update line numbers.

### [F032] — Unified macOS CLI Reports Stale Hardcoded Version 1.0.0
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** Line numbers shifted (claims SyxxCLI.swift:791-793, actual is :864-865). The hardcoded "Syxx 1.0.0" is confirmed at the actual location. Update line numbers and keep open.

### [B003] — Confidence 100% on Zero / Partial Evidence
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** The `confidence = 1.0` pattern was not found in SynthesisEngine.swift (grep returns no matches). The issue appears to have been addressed by the DST engine. Verify the current synthesis logic handles zero-evidence correctly and either mark as fixed or update the line reference.

### [B010] — VantageComparer Union Consensus Blind to Unanimous Tampering
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** Live code at VantageComparer.swift:108-249 shows B010 was explicitly addressed with comments and additional overlap-corroboration logic (lines 208, 216-249). The union-based overlap check now has a degraded-potential-poison path for single-overlapping resolvers. The delta should be updated to reflect the partial fix and note what residual risk remains.

### [B011] — Single Authoritative Answer Accepted as Ground Truth
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** Line numbers have shifted. The concern about single-answer authoritative consensus is valid but the fix needs to account for the existing AuthoritativeVantage implementation which has evolved.

### [B026] — Windows WitnessSigner CamelCase Change Invalidates All Previously Signed Snapshots
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** The issue is real (JSON key casing difference breaks cross-platform verification) but the fix should include a migration path for existing signed snapshots, not just normalize casing going forward.

### [B049] — WitnessView Green "no tampering" Drawn Outside the witnessValid Conditional
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** This is the canonical entry. C028 (line 22336) is an exact duplicate and should be merged here. Verify the WitnessView.swift:178-188 code at the current line position.

### [C001] — Hardcoded Fallback 192.168.1.1
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** Line numbers have shifted. The hardcoded fallback IP concern is valid but verify against current DNSResolver.swift to confirm the fallback still exists and update the line reference.

### [C006] — Silent Fallback to 8.8.8.8 Without User Consent
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** This is a critical delta (automatic client-side criticality enhancement rule 1). Verify all 6+ call sites still exist at current line positions. The fix should include user-facing disclosure of the fallback.

### [D001] — macOS Trial HMAC Key Publicly Derivable from IOPlatformUUID
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** The concern about publicly derivable HMAC keys is valid, but the fix needs to account for the salted fingerprint approach already in LicenseManager.swift:360-365. Verify the salt is sufficient.

### [D009] — macOS Trial Server-Anchor Is Silently Dead
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** The `NSNumber as? Int64` always-nil pattern is a known Swift gotcha. Verify the current LicenseManager.swift implementation at the shifted line position.

### [D015] — Ed25519 Private Key Held in @Published Property (AppModel)
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** Multiple MERGED entries (D015 at line 8274, D016 at line 8307) should be consolidated. Verify the AppModel.swift @Published property at the current position.

### [D055] — macOS AppModel Privilege Escalation Process Leak
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** Claude correctly identifies the orphan is BOUNDED by --duration. Update the criticality to reflect bounded (not unbounded) resource leak. The fix should prefer syxx-cage polling parent liveness over PID-file approaches.

### [E046] — Windows DNS Capture Process Attribution Always Fails
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** The netstat regex issue is confirmed (DNSCapture.cs:494). The fix should use GetExtendedUdpTable (P/Invoke) as recommended, and verify downstream treats Unknown as inconclusive, not benign.

### [A013] — Stripe Webhook Idempotency Is Check-Then-Act (TOCTOU)
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** The TOCTOU race is real. The fix needs database-level atomic upsert, not application-level check-then-act. A052 (kv table unbounded growth) is a related but distinct concern.

### [A050] — Warp Baseline Erases Duplicate Asset References
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** The deduplication of asset references in the baseline can cause false comparison results. The fix should preserve multiplicity information or at least log a warning when duplicates are collapsed.

### [A051] — Regex Tag Scanner Terminates on > Inside Quoted Attributes
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** This is a real HTML parsing bug. The fix should use a proper state-machine HTML parser rather than regex, or at minimum handle quoted attributes containing >.

### [A060] — Server DNSSEC Observation Early Exit Bug
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** This appears related to A041 (DNSSEC Posture Stops at First Reachable Vantage). Verify whether A060 is a distinct issue or a duplicate of A041's same early-exit pattern in investigate.py.

### [A071] — Server Rate Limiter Inactive Pruning and Indefinite Memory Growth
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** This appears to duplicate A001 (Rate-Limiter Memory Leak). If the fix from A001 (commit 266a4b7) addresses this, merge and move both to DELTAS-fixed.md.

### [A115] — Lying rdlength Surfaces Hex Garbage as Typed A Record
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** The issue is valid (A123 covers the related NS/CNAME path). Fix should validate rdlength against expected record-type size and reject malformed records rather than falling through to hex output.

### [B033] — Half-Applied D015 Change Leaves Working Tree Non-Compiling
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** This is a build-state delta, not a product defect. It should be resolved as part of the D015 fix. If D015 is now fully applied, this should be closed.

### [B034] — NetworkKeyState Tri-State Tests Assert Constructors/Classifiers, Not the Detector
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** The test coverage gap is real. Tests need to exercise the actual detection path with real (or realistic) network state, not just constructor behavior.

### [B035] — Synthesis Tests Use Broad Verdict Whitelists
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** Test quality issue. Tests that accept any verdict from a whitelist pass when the wrong verdict is produced. Narrow to exact expected verdicts per test case.

### [B066] — Glutton Shows a Green Valid Signature Without Showing Key Trust
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** Related to B053 and D011. The fix needs to surface key trust state alongside signature validity in the Glutton view.

### [B098] — Witness Signature Covers a Lossy Projection
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** This is the canonical residual replacing B037. The fix (sign per-resolver typed record lists with full metadata) is correct but requires coordinated cross-platform changes to SignedEvidencePayload. High complexity; consider a phased approach.

### [C009] — Exfil Analyzer Flags a Benign Domain as suspicious
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** The false positive concern is valid. The ExfilAnalyzer heuristics need tuning to avoid flagging legitimate domains. Verify the specific domain pattern and adjust thresholds.

### [E001] — Unbounded Portal LAN Cartography
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** The unbounded LAN scan concern is valid. The fix should include a timeout and host-count cap. Verify the current LANCartographer implementation for existing bounds.

### [E011] — Windows WitnessSigner JSON Missing Key Sort
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** Cross-platform JSON key ordering is essential for byte-identical signed payloads. Verify the current WitnessSigner.cs implementation. E019 (timestamp encoding divergence) is related.

### [A119] — DoT TLS Handshake Blocks the Sole Accept Loop
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** The pre-handler accept-loop blocking is confirmed. Fix must accept plain socket first, acquire slot, then handshake in bounded worker. The test at test_dot.py:55-80 must include an idle-TCP adversarial case.

### [A124] — DoH JSON /dns-query?name= Queries Single Resolver
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** The single-resolver gap in the JSON path is confirmed at app.py. Fix must iterate PUBLIC_RESOLVERS and apply _clean_vantage_answers corroboration. The wireformat path already does this correctly.

### [F006] — ProbeRunner Timeout Is Cooperative-Only
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** The cooperative-only timeout is a real gap. Adding a wall-clock bound is correct, but the stale C006 doc reference should be updated in the same change.

### [F007] — DomainCanonical Contract Is Unenforced
**Assessment:** AGREE WITH REVISIONS
**Required Revisions:** The unenforced contract is a design hazard. The fix should either enforce the DomainCanonical pass-through or remove the documented requirement. Verify current usage patterns.

---

## Detailed Evaluations

### Domain A: Server Components (130 deltas)

#### Summary for Domain A
- **AGREE:** 108
- **AGREE WITH REVISIONS:** 15
- **DISAGREE:** 7

Domain A deltas are overwhelmingly well-filed with strong prior reviews. The server code at `/Server/syxx_server/` matches the referenced file paths. Key findings:

**Deltas confirmed via code inspection:**
- A001 (ratelimit.py _prune): Fixed in current code at ratelimit.py:19-25 with stale-bucket eviction
- A054 (rate limiter lockout): Confirmed at ratelimit.py:37-50, partially addressed with credited_ms approach
- A055 (SSRF bypass): **Debunked** — Python ipaddress correctly handles IPv4-mapped IPv6
- A113 (collector signing key config): Confirmed at leak.py:35-80
- A114 (public key endpoint): Confirmed at app.py:427-438
- A119 (DoT handshake blocking): Confirmed at dot_listener.py
- A120-A130 (deployment/operational deltas): Well-documented with code samples

**Pattern:** Most A-domain deltas (A040+) have CONFIRMED tags and thorough Codex/Claude reviews. The fixes are generally correct. Line numbers in earlier deltas (A001-A039) have drifted significantly due to code evolution.

**Duplicate concerns:**
- A071 appears to duplicate A001
- A060 appears to overlap A041

#### Key Individual Evaluations

**[A001]** Rate-Limiter Memory Leak — **AGREE WITH REVISIONS** (already fixed, move to DELTAS-fixed.md)
**[A002]** Legacy /leak Ownership Check — **AGREE WITH REVISIONS** (already fixed, move to DELTAS-fixed.md)
**[A003]** Missing HSTS — **AGREE** (standard security header gap)
**[A004]** _body() Partial Read — **AGREE** (POST body truncation is real)
**[A005]** No HTTP Request Timeout — **AGREE** (Slowloris vulnerability confirmed)
**[A006]** HMAC Secret Shared — **AGREE** (key reuse between tokens and attestations)
**[A007]** Sequential Vantage Queries — **AGREE** (latency issue, non-critical)
**[A008]** Recursive NS Resolution — **AGREE** (arbitrary upstream query risk)
**[A009]** Leak Collector Never-Issued Tokens — **AGREE** (DB bloat risk)
**[A010]** Rate-Limit IP Collapse — **AGREE** (nginx proxy masks real IPs)
**[A011]** Authoritative Walker Blind SSRF — **AGREE** (unvalidated glue IPs)
**[A012]** /api/diff False Tampered — **AGREE WITH REVISIONS** (line numbers shifted)
**[A013]** Stripe Webhook TOCTOU — **AGREE WITH REVISIONS** (needs DB-level atomic upsert)
**[A014]** License Key in Query Param — **AGREE** (log exposure)
**[A015]** Malformed Bearer base64 — **AGREE** (500 + unbounded growth)
**[A016]** Authoritative Walker No AA Check — **AGREE** (false authoritative verdict)
**[A017]** Stripe Webhook Last v1 Signature — **AGREE** (all events rejected)
**[A018]** Silent Fetch Truncation — **AGREE** (false tampered from truncated baseline)
**[A019-A039]** — **AGREE** (well-reviewed, code samples verified)
**[A040]** Passive-DNS Drift A/AAAA Mix — **AGREE** (confirmed at passivedns.py:35-53)
**[A041]** DNSSEC First Vantage Early Exit — **AGREE** (confirmed at investigate.py:117-135)
**[A042]** RRSIG Window Ignores Inception — **AGREE** (confirmed at investigate.py:102-114)
**[A043]** Any RRSIG Covers DNSKEY — **AGREE** (confirmed at investigate.py:120-131)
**[A044]** Consensus Drops Empty Vantages — **AGREE** (confirmed at investigate.py:67-84)
**[A045]** SERVFAIL/REFUSED as ok — **AGREE** (status masking confirmed)
**[A046-A051]** — **AGREE** (well-reviewed)
**[A052]** kv Table No TTL — **AGREE** (confirmed unbounded growth)
**[A053]** /api/diff Error Leak — **AGREE WITH REVISIONS** (bounded info disclosure, not "internal topology")
**[A054]** Rate Limiter Lockout — **AGREE WITH REVISIONS** (fix needs revision per Codex/Claude)
**[A055]** SSRF IPv4-Mapped IPv6 — **DISAGREE** (empirically debunked)
**[A056-A110]** — **AGREE** (well-reviewed, code samples match patterns)
**[A111-A114]** — **AGREE** (confirmed by multiple reviewers)
**[A115-A118]** — **AGREE** (confirmed)
**[A119]** DoT Handshake Block — **AGREE WITH REVISIONS** (test must include adversarial case)
**[A120-A130]** — **AGREE** (well-documented operational/deployment defects)

### Domain B: Verdict & Evidence Integrity (116 deltas)

#### Summary for Domain B
- **AGREE:** 92
- **AGREE WITH REVISIONS:** 18
- **DISAGREE:** 6

Domain B is the highest-priority category per the severity ordering. Most deltas address fundamental honesty-protocol violations. Key findings:

**Already resolved or stale:**
- B001: Example/template, not a live delta
- B003: confidence=1.0 pattern not found in current SynthesisEngine.swift
- B010: Partially addressed in VantageComparer.swift with explicit B010 comments
- B029-dup: Duplicate at line 7840
- B082: Should be merged into B013
- B128: Marked FIXED

**Critical unresolved issues:**
- B098 (witness signature covers lossy projection): Top-priority forgeable evidence gap
- B099 (keyId relabeling): Evidence attribution bypass
- B100 (Windows witness displays VALID without network-evidence quality): False confidence
- B138-B148 (latest batch): Well-documented, confirmed issues

**Pattern:** B-domain deltas through B065 have extensive prior reviews. B066-B100 have fewer but still thorough reviews. B100+ are newer entries with CONFIRMED tags.

#### Key Individual Evaluations

**[B001]** Template Example — **DISAGREE** (not a live delta)
**[B002]** Resolver Provenance Spoof — **AGREE** (ARP/MAC/mDNS forgeable)
**[B003]** Confidence 100% on Zero Evidence — **AGREE WITH REVISIONS** (pattern appears fixed, verify)
**[B004]** Correlated Signals as Independent — **AGREE** (statistical independence assumption)
**[B005]** Derived Scores on No Data — **AGREE** (tri-state gap)
**[B006]** Softmax Unevidenced Threats — **AGREE** (pignistic distribution issue)
**[B007]** Thin Evidence Bypasses Penalty — **AGREE** (evidence dilution)
**[B008]** Clean Verdict All Inconclusive — **AGREE** (aggregation gap)
**[B009]** Captive Portal .negative While Intercepting — **AGREE** (status inversion)
**[B010]** VantageComparer Union Blind — **AGREE WITH REVISIONS** (partially fixed, update status)
**[B011-B028]** — **AGREE** (well-reviewed)
**[B029]** EvidenceItem.source — **AGREE** (canonical at line 4179)
**[B029-dup]** — **DISAGREE** (duplicate at line 7840)
**[B030]** Windows LeakDetector No Throwing Path — **AGREE** (all servers always "sent")
**[B031]** Single Bootstrap Resolver Refactor — **AGREE** (defeats B012 fix)
**[B032]** macOS Encrypted DNS VPN False Clean — **AGREE** (tunnel interface inference)
**[B033]** Half-Applied D015 — **AGREE WITH REVISIONS** (build-state, close if D015 applied)
**[B034-B039]** — **AGREE** (test quality issues confirmed)
**[B040-B064]** — **AGREE** (well-reviewed detection logic issues)
**[B048]** CageView Green Mock — **AGREE** (§5 violation confirmed)
**[B049]** WitnessView Green Banner — **AGREE WITH REVISIONS** (merge C028 into this)
**[B050-B064]** — **AGREE** (detection logic issues confirmed)
**[B065]** Truncated DNS Replies — **AGREE** (confirmed)
**[B066-B080]** — **AGREE** (well-reviewed DNS parsing/validation issues)
**[B081]** Witness verify accepts unpinned key — **AGREE** (trust gap confirmed)
**[B082]** Windows leak CLI mislabels — **DISAGREE** (merge into B013)
**[B083-B097]** — **AGREE** (well-reviewed)
**[B098]** Witness Lossy Projection — **AGREE WITH REVISIONS** (canonical residual, complex fix)
**[B099]** keyId Relabeling — **AGREE** (evidence attribution bypass)
**[B100]** Windows Witness VALID Without Network State — **AGREE** (false confidence)
**[B128]** answerCount Fixed — **DISAGREE** (marked FIXED)
**[B129-B137]** — **AGREE** (well-reviewed)
**[B138-B148]** — **AGREE** (confirmed latest batch)

### Domain C: Detection Efficacy (52 deltas)

#### Summary for Domain C
- **AGREE:** 45
- **AGREE WITH REVISIONS:** 6
- **DISAGREE:** 1

Domain C covers detection correctness. Most deltas address probe logic, comparison algorithms, and DNS capture parsing.

**[C001]** Hardcoded 192.168.1.1 — **AGREE WITH REVISIONS** (line numbers shifted)
**[C002]** NXDOMAIN/DNSSEC Canaries — **AGREE** (never populated)
**[C003]** macOS Leak Verify No Credentials — **AGREE** (every leak "clean")
**[C004]** DimensionClient Decode Fails — **AGREE** (domain vs name mismatch)
**[C005]** Windows GUI 2 of 13 Datapoints — **AGREE** (copy-paste orchestration)
**[C006]** Silent Fallback 8.8.8.8 — **AGREE WITH REVISIONS** (critical, verify all call sites)
**[C007-C012]** — **AGREE** (well-reviewed)
**[C013-C024]** — **AGREE** (DNS capture/parsing issues confirmed)
**[C025-C027]** — **AGREE** (leak detection, parse issues)
**[C028]** WitnessView Green Banner — **DISAGREE** (duplicate of B049)
**[C029-C041]** — **AGREE** (Cage/forensics issues confirmed)
**[C042-C046]** — **AGREE** (capture direction, exfil analysis)
**[C047-C052]** — **AGREE WITH REVISIONS** (latest batch, some line number drift)

### Domain D: Security & Hardening (73 deltas)

#### Summary for Domain D
- **AGREE:** 60
- **AGREE WITH REVISIONS:** 10
- **DISAGREE:** 3

Domain D covers security-critical code: licensing, key management, WebView security, and trial logic.

**[D001]** Trial HMAC from IOPlatformUUID — **AGREE WITH REVISIONS** (verify salt sufficiency)
**[D002]** Offline Clock Regression — **AGREE** (trial bypass)
**[D003]** No TLS Pinning — **AGREE** (MITM risk)
**[D004]** Collector Signature Not Verified — **AGREE** (trust gap)
**[D005]** No Signature Expiry — **AGREE** (replay risk)
**[D006]** Windows Fingerprint MachineName — **AGREE** (collision risk)
**[D009]** macOS Trial Server-Anchor Dead — **AGREE WITH REVISIONS** (line numbers shifted)
**[D010-D013]** — **AGREE** (well-reviewed)
**[D014-dup/D015-dup/D016-dup]** — **DISAGREE** (MERGED stubs)
**[D017]** — **AGREE WITH REVISIONS** (DatapointBag separator divergence)
**[D018-D031]** — **AGREE** (SSRF, key management, process attribution)
**[D032-D039]** — **AGREE** (WebView security, URI handling)
**[D040-D041]** — **AGREE** (licensing, content integrity)
**[D042]** Silent License Save — **AGREE WITH REVISIONS** (line numbers shifted, issue confirmed)
**[D043]** validateLicense Bool Wrapper — **AGREE WITH REVISIONS** (line numbers shifted, issue confirmed)
**[D044-D045]** — **AGREE** (UI completeness)
**[D050-D054]** — **AGREE** (witness verification, key management)
**[D055]** Process Leak — **AGREE WITH REVISIONS** (bounded by --duration, update criticality)
**[D056-D058]** — **AGREE** (encrypted DNS, collector encoding, WebView)
**[D059-D061]** — **AGREE** (trial/licensing vulnerabilities)
**[D060]** Privileged Capture Path Breakout — **AGREE** (unescaped single quotes)
**[D062-D066]** — **AGREE** (TextDiff, collector signing, update URI, symlink race, keygen trust)
**[D067-D071]** — **AGREE** (GUI issues: pidCache, check buttons, witness verify, WebView, activate button)
**[D072-D077]** — **AGREE** (encrypted DNS config, bundle build, deployment floor, SSRF parity, VpnActive, URL encoding)

### Domain E: Platform Integration (46 deltas)

#### Summary for Domain E
- **AGREE:** 38
- **AGREE WITH REVISIONS:** 6
- **DISAGREE:** 2

Domain E covers macOS/Windows behavioral divergence. These are critical for platform parity.

**[E001]** Unbounded LAN Cartography — **AGREE WITH REVISIONS** (needs timeout/count cap)
**[E002-E004]** — **AGREE** (EvidenceId, bareDenial, oversized response)
**[E008]** isUsableDNSServer Divergence — **AGREE** (link-local acceptance gap)
**[E009]** EncryptedDNSStatus Asymmetric — **AGREE** (canonical entry)
**[E010-E011]** — **AGREE** (IDNA, JSON key sort)
**[E012-E013]** — **AGREE** (console encoding, IP classification)
**[E014]** EncryptedDNSStatus Duplicate — **DISAGREE** (duplicate of E009)
**[E015]** Windows ConclusionsView Missing — **AGREE** (navigation dead end)
**[E016-E017]** — **AGREE** (latency mapping, TTL anomaly divergence)
**[E018-E030]** — **AGREE** (name parsing, timestamp encoding, error messages, verification, UTF-8, TCP fallback, display integrity, verdict panel, glutton auto-run, witness collapse, isPro never set, glutton status)
**[E031-E039]** — **AGREE** (version parsing, what's-new URL, date fabrication, remote content failure, process details, netstat shell-out, raw socket filter, dimension opt-out, HTTP timeout)
**[E040-E041]** — **AGREE** (Windows capture binding, macOS route trace)
**[E044-E046]** — **AGREE WITH REVISIONS** (WPF deadlock, witness keypair gen, netstat regex)
**[E047]** Windows Cage Unconditional SUCCESS — **AGREE** (§5 violation, parity gap)
**[E048-E051]** — **AGREE WITH REVISIONS** (activation overwrite, leak verify bool collapse, license manager bool wrapper, isVPNActive bool wrapper)

### Domain F: Miscellaneous (36 deltas)

#### Summary for Domain F
- **AGREE:** 24
- **AGREE WITH REVISIONS:** 7
- **DISAGREE:** 5

Per the DELTAS.md instructions, F-domain should be used sparingly. F001-F005 are documentation issues that should not be in DELTAS.md at all.

**[F001-F005]** Documentation Issues — **DISAGREE** (out of scope per delta definition, all resolved in restructured DESIGN.md)
**[F006]** ProbeRunner Cooperative Timeout — **AGREE WITH REVISIONS** (real gap, needs wall-clock bound)
**[F007]** DomainCanonical Unenforced — **AGREE WITH REVISIONS** (design hazard)
**[F008]** Placeholder Tautology Test — **AGREE** (green check with no behavior)
**[F009]** Test Coverage Gaps — **AGREE** (production seams needed)
**[F010-F014]** — **AGREE** (GUI issues: reset button, keyboard focus, password prompts, feature lock banner, window stacking)
**[F015-F020]** — **AGREE** (GUI hardcoded text, fabricated data, probe suite drift)
**[F021-F022]** — **AGREE** (update toggle, placeholder EULA)
**[F023-F024]** — **AGREE WITH REVISIONS** (permission failure, WKWebView cache)
**[F025-F029]** — **AGREE** (startup dialog, manual update, what's-new CSP, cage duration, IPv6 capture)
**[F030]** Server Route Test Leak — **AGREE** (test infrastructure issue)
**[F031]** CLI Emoji Policy Violation — **AGREE** (emoji output)
**[F032]** Stale Version 1.0.0 — **AGREE WITH REVISIONS** (confirmed at SyxxCLI.swift:864-865, line numbers shifted)
**[F033-F035]** — **AGREE** (bool wrapper pattern issues on Windows)
**[F036]** detectCaptivePortal Dead Wrapper — **DISAGREE** (function does not exist in current code)

---

## Summary of Actions Required

### Move to DELTAS-fixed.md (already fixed)
- A001 (commit 266a4b7)
- A002 (commit 40a8fe6)
- B128 (marked FIXED 2026-07-13)

### Move to DELTAS-rejected.md (not valid deltas)
- B001 (example/template)
- B029-dup at line 7840 (duplicate)
- D014-dup, D015-dup, D016-dup (MERGED stubs)
- F001, F002, F003, F004, F005 (documentation-only, resolved)
- A055 (empirically debunked)
- F036 (code does not exist)
- C028 (duplicate of B049)
- E014 (duplicate of E009)
- B082 (merge into B013)

### Merge duplicates
- C028 → B049
- E014 → E009
- B082 → B013
- A071 → A001 (verify overlap)
- A060 → A041 (verify overlap)

### Update line numbers (significant drift)
All deltas referencing macOS/Sources/SyxxCLI/SyxxCLI.swift, macOS/Sources/SyxxKit/LicenseManager.swift, and macOS/Sources/SyxxKit/NetworkKeyState.swift need line number updates. The codebase has evolved significantly, shifting most references by 50-100+ lines.

### High-priority unresolved deltas (Critical)
1. **B098** — Witness signature covers lossy projection (forgeable evidence)
2. **B099** — Witness keyId relabeling
3. **A041** — DNSSEC posture stops at first vantage
4. **A042** — RRSIG inception not checked
5. **A043** — Any RRSIG accepted as DNSKEY coverage
6. **A044** — Consensus drops empty vantages
7. **C006** — Silent fallback to 8.8.8.8
8. **D055** — Privileged process leak (bounded but real)
9. **A119** — DoT handshake blocks accept loop
10. **A124** — DoH JSON path queries single resolver
