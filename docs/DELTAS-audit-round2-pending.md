# Delta audit — loop-until-dry: paused at round 2 (session limit)

**Status:** ROUND 2 COMPLETE — all three lanes reported. Filed C174 (reasoning),
E201 (client), A268 (server). Round 2 is NOT dry (each lane still found one new
delta), so a round 3 is warranted to confirm dry. The AAAA/inet_ntop lead (LEAD-1)
was traced and DROPPED with evidence (the two IPv6 string forms never meet in a
comparison). LEAD-2 (db.py) was audited clean.

## Round 2 — FILED

- C174 (Non-critical) — MergedDatapoint.isConflicting false-fires DISCREPANCY on benign multi-A
- E201 (Non-critical/Medium) — Windows WitnessSigner never recomputes keyId at verify; B099 fix
  is macOS-only (parity regression on a delta archived as FIXED-both-platforms)
- A268 (Non-critical) — DoT leak-collector observation path bypasses the A034 per-source rate limit

## Round 2 — reasoning-core lane: DONE

- C174 (Non-critical) — FILED — MergedDatapoint.isConflicting false-fires DISCREPANCY on
  benign partially-overlapping multi-A answers; macOS pairwise logic contradicts B170
  intent; D142 would propagate the bug to Windows.
- Reasoning-core also reported: tier()/mutual-exclusion/ReportCard faithful across
  platforms; Stage-5 DTO removed (nothing to audit); one minor un-filed hardening note
  (Windows DatapointBag.Merge uses empty key/value separator vs macOS SOH  — latent,
  not currently reachable). Reasoning-core lane is effectively DRY except C174.

## Round 1 — FILED into DELTAS.md (done, verified, reflowed)

- A267 (Critical) — /api/monitors unbounded interval + no cap + unvalidated domain → self-DoS/amplification
- C170 (Critical) — invalidated sibling sub-probe silently erases a decisive threat indicator
- C171 (Critical) — Windows Prophet treats Truncated as clean Negative (macOS = Invalidated)
- C172 (Critical) — Windows combine() checks Positive before Invalidated (B062 never ported)
- C173 (Critical) — GUI Mirror/Witness/Testify silently substitute public resolver → false clean + signed evidence
- B270 (Non-critical) — dnsSpoofing/localCompromise require witness section, .untested on first run

## Round 2 — UNVERIFIED LEADS (do NOT file until confirmed source-grounded)

### LEAD-1 (client lane): AAAA rData formatting may diverge from canonical inet_ntop

- Observed: `formatRData` for AAAA records renders UNCOMPRESSED IPv6 at
  macOS/Sources/.../DNSCapture.swift:633-639, while the resolver path uses
  canonical `inet_ntop` (compressed) form.
- Hypothesis: if these two string forms are ever COMPARED (e.g. capture-vs-resolver,
  or baseline-vs-observed), the same IPv6 address in two different textual forms
  would register as a spurious MISMATCH → FALSE POSITIVE (a false "tampering"
  signal). Also check Windows twin after its B260 fix for parity.
- NOT YET CONFIRMED: the agent had not established whether the two forms actually
  meet in a comparison. Needs: trace consumers of formatRData(AAAA) and confirm a
  comparison site. Only file as a delta (domain B, likely Critical if it feeds a
  verdict) once the comparison path is proven in source.

### LEAD-2 (server lane): db.py transaction/write-path layer not yet audited

- The server agent was about to read db.py's connection/transaction/locking and
  write paths (injection surfaces, transaction correctness, locking) when it was
  cut off. No finding yet — this path is simply UNCOVERED, not clean.

## Paths still UNCOVERED at pause (resume targets)

- Reasoning core: overhang/attenuation math (overhangFloor, K/M completeness, degraded
  discounting), exclusionary-indicator logic, mutual-exclusion, confidence-tier
  boundaries, Report.* extractor, Stage-5 DTO population, remaining MDE parity.
- Client: Cage ExfilAnalyzer/BeaconAnalyzer, Glutton, Portal anomaly surfacing,
  Prophet NXDOMAIN/wildcard canaries, DoT transport+fallback, resolver-discovery
  edges, Win32/Darwin interface+privilege, hostile-packet resource leaks/crashes,
  WitnessSigner/NetworkKeyState remaining paths, + LEAD-1.
- Server: db.py (LEAD-2), authoritative-DNS edges, ratelimit.py bucket math,
  scheduler API-key revocation sweep + subscription lifecycle, remaining app.py
  routes, WHOIS/RDAP, config/secret handling.

## Next-free delta IDs at pause (RE-VERIFY against live ledger before filing)

A268+, B271+, C174+, D146+, E201+

## Resume procedure

1. Confirm session limit has reset.
2. Resume the 3 agents by their IDs with their context intact:
   - reasoning-core: a26798c9065320602
   - client non-core: aa2ba1ebcfd8df1c3
   - server: a5cc101d1d9a69b0c
3. Re-verify next-free IDs (concurrent writer may still be active on DELTAS.md).
4. File each round's findings, reflow, report out per round, loop until a full round
   is DRY across all three lanes.
