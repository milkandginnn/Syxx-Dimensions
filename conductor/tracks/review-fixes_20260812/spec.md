# Specification: Recovered xhigh Review — Confirmed Defect Repairs

**Track ID:** review-fixes_20260812
**Type:** Bug
**Created:** 2026-08-12
**Status:** Draft
**Tier:** 3 (`IMPLEMENTATION-RULES.md` §3 — cross-platform, security-sensitive, UI work included)

## Summary

Repair the 36 unique CONFIRMED defects recovered from three `/code-review` xhigh runs (2026-08-11/12), spanning Windows/.NET `Syxx.Kit`, macOS/Swift `SyxxKit`/`SyxxApp`/CLI, and the Linux/Avalonia and Server/ siblings named by cross-runtime parity findings.

## Context

Three xhigh review runs reported 45 findings, all CONFIRMED. Runs 1 and 2 covered Windows/.NET and overlap on 9 defects; Run 3 covered macOS/Swift. After dedup: **36 unique defects — 21 Windows/.NET, 15 macOS/Swift**.

Authoritative input: [`findings.md`](./findings.md), preserved in this track directory (recovered verbatim from session transcript `f93531fc`; the original scratchpad copy is ephemeral).

The dominant theme is **honesty-contract failure in both directions**: measurements that never happened rendering as affirmative clean results (W02, W04, W05, W20, M04, M09, M10), and benign network behavior rendering as confirmed tampering (W06, W11, M03, M14). Both classes attack the product's core claim. A second cluster is **cross-runtime divergence** — the same capture yielding different conclusions on different platforms (W04, W16, M12, W20/M02).

## Problem Description

### Windows/.NET — `Windows/dotNET/Syxx.Kit/` unless noted

| ID | Location | Defect |
|----|----------|--------|
| W01 | `Synthesis/SynthesisEngine.cs:326` | `DnssecCanarySecure → TrustedWitnessOK (0.75)` hard-excludes `dnsSpoofing` + `dnsInterception`; canary probes the control domain, not the target |
| W02 | `Synthesis/ReportCard.cs:88` (+`:83`) | `Inconclusive` graded B/Clean with "looks clean" headline; `hasThreatOverhang` skips `HardExcluded` suppressions — contradicts the file's own honesty contract header |
| W03 | `Synthesis/SynthesisEngine.cs:296` (+`:305`) | `ResolverStatus → ResolverAgreement (0.80)` turns NXDOMAIN/REFUSED/SERVFAIL into Major clean-support |
| W04 | `VantageComparer.cs:453`/`:460` | Empty or <2-endpoint DoH set returns `Clean`; Swift twin returns `.tampered` for the same input |
| W05 | `VantageComparer.cs:332` | `UnanimousTamperingResult.Undetermined` declared and handled but never returned; `inconclusiveFromUnanimous` is dead |
| W06 | `VantageComparer.cs:451` | Any RCODE disagreement → `Tampered`; `DidFail` excludes NXDomain/Refused/ServFail/NoData |
| W07 | `CollectorPinning.cs:41` | Whitespace-only pin file shadows `SYXX_COLLECTOR_PINS`; empty pin set means accept-anything |
| W08 | `WarpManifestPinning.cs:41` | Persisted key file read before env var, inverting the documented order; stale file permanently shadows a rotated pin |
| W09 | `DpapiSecretStore.cs:51` | Keystore root moved Roaming → Local with no legacy-path read fallback; `Exists` returns `Absent` not `Unknown`, licensing a second Witness identity |
| W10 | `DNSCapture.cs:323` | IPv6 Fragment header (44) walked, then payload parsed as UDP/DNS; the sibling IPv4 guard rejects exactly this |
| W11 | `AuthoritativeVantage.cs:216`/`:225` | Same-provider disjoint → `Diverges` instead of `Inconclusive`; ordinary CDN geo-routing emits a tampering finding |
| W12 | `WarpEngine.cs:577` | `WarpCommand.cs:48` and `Program.cs:701` pass no replay nonce; `EvaluateFreshness` skips the check when null |
| W13 | `LinuxSecretStore.SecretTool.cs:125` | `ReadToEnd` before `WaitForExit(5000)`; bound never applies and sequential pipe reads can deadlock at startup |
| W14 | `IPClassifier.cs:118` | Teredo/6to4 moved out of `PublicRoutable`; every SSRF allow-list is `== PublicRoutable`, so they are silently rejected |
| W15 | `IPClassifier.cs:73` | `198.18/15` → `Benchmarking`; rogue-DHCP off-link test still matches `PrivateUse` only |
| W16 | `DNSSECVerifier.cs:192` | `MaxPlausibleValiditySpanSeconds` = 3y vs Swift 5y vs Python 2y — three values for one honesty gate |
| W17 | `LicenseManager.cs:296` | Trial HMAC key changed; both candidates use the new key, so no prior-build signature can validate |
| W18 | `LicenseManager.cs:338` (+`:295`) | `GetInstallationSalt()` failure path returns a per-call `ephemeral:{Ticks}`, making signatures unreproducible |
| W19 | `Synthesis/FirstRunScan.cs:60` + `SyxxUI/Views/ConclusionsView.xaml.cs:251` | `fallbackToDefaultDomain` guard wired into Avalonia only; WinUI keeps the `true` default |
| W20 | `Synthesis/MatrixDeterminativeEngine.cs:1054` | Non-usable section's rule weight added to `observedSupportWeight`, inflating coverage and deflating contradiction |
| W21 | `SyxxConfig.cs:44` | `--verbose` early-returns, short-circuiting `--help`/`--version`; the command runs live probes instead |

### macOS/Swift — `macOS/Sources/`

| ID | Location | Defect |
|----|----------|--------|
| M01 | `DeepSyxx/main.swift:427` | IPv6 loopback test `allSatisfy({$0==0}) && last==1` is unsatisfiable; `::1` passes the root helper's restricted-DNS gate |
| M02 | `SyxxKit/Synthesis/MatrixDeterminativeEngine.swift:864` (+`:852`) | Support/decisive recording moved outside the non-contradictory branch; contradicting indicators count as support |
| M03 | `SyxxKit/VantageComparer.swift:336`/`:344` | DoH unavailable or <2 endpoints returns `.tampered` instead of `.undetermined` |
| M04 | `SyxxApp/MirrorView.swift:456` (+`:468`) | Fabricated green "0% / Identical" badge on the only production-reachable branches **[UI]** |
| M05 | `SyxxApp/AppModel+WitnessRun.swift:150` | `.displayMismatch` and `.untrustedSigner` fall to `default:` → `.signed` **[UI]** |
| M06 | `SyxxKit/Trust/IntegrityTag.swift:129` | `constantTimeEqual` truncates the length delta through `UInt8`; deltas that are multiples of 256 contribute 0 |
| M07 | `DeepSyxx/main.swift:47` (+`:43`) | Helper allowlist derived from a bare daemon's `Bundle.main.bundlePath` yields `/Resources/`; all Cage capture rejected |
| M08 | `SyxxKit/WitnessKeyManager.swift:120` (+`:136`) | Wrapped-key prefix detected by UTF-8-decoding the whole blob; every wrapped key reads back nil after restart |
| M09 | `SyxxKit/Synthesis/MatrixDeterminativeEngine.swift:846` | B157 quality floor deleted; thin-but-conclusive sections never reach tier |
| M10 | `SyxxKit/Synthesis/SyxxMatrixIntegration.swift:227` | `mappableIndicators: nil` returns unmappable rules to the coverage denominator, capping coverage below threshold |
| M11 | `SyxxKit/EncryptedTransportProbe.swift:74` | Silent fallback to three hardcoded third-party DoH endpoints reports a blocked path as working |
| M12 | `SyxxKit/SignedEvidencePayload.swift:277`/`:283` | `ttl: 0` hardcoded in signed canonical records; C# twin writes the real TTL **[signed contract]** |
| M13 | `SyxxKit/ECSProbe.swift:71` | Undeterminable ECS state reported as `.notForwarded` |
| M14 | `SyxxKit/AnswerStabilityProbe.swift:104` | Disjoint CDN edge rotation classified `.unstable` |
| M15 | `SyxxCLI/Commands/SyxxMirror.swift:25` | Flag loop starts at index 0; `argv[0]` becomes the domain |

## Authoritative Decisions

Recorded from the planning interview. These bind implementation.

1. **Scope** — one track, all platforms. Cross-runtime parity siblings in `Linux/Ubuntu/` and `Server/` are in scope where a finding names them (W16 → `Server/syxx_server/resolver.py:118`; W04/W05 → Swift twin; W20 → `MatrixDeterminativeEngine.swift:785`; M12 → `SignedEvidencePayload.cs:348`).
2. **Fix strategy** — redesign each defect properly on its merits. A revert is chosen only where it is independently the correct design, not as a default.
3. **WinUI gap** — W19 is repaired by relocating the default-domain guard out of XAML code-behind into shared `net10.0` code (`Syxx.Kit` / `SyxxUI.Logic`) so both Conclusions surfaces call one testable path. `SyxxUI` and `SyxxUI.Tests` are `net10.0-windows10.0.26100.0` and cannot compile here; every other project is `net10.0` and validates locally.
4. **Repaired-item bar** — read the finding's specification, check whether the defect is live in current code. If not live, verify the existing repair is sound (not merely that the old code changed), record the evidence, move on. Live defects take the full TDD motion per `workflow.md`.
5. **Team** — one orchestrator plus a reused core: C# implementer, Swift implementer, security specialist, test specialist, code reviewer. Sequential themed waves, each validated and committed before the next. Every assignment carries declared read/write scope and spend ceilings per `SUBAGENT-RULES.md` §16.
6. **Signed-evidence contract (M12)** — see below.

### M12 — signed-evidence canonicalization contract

A `formatVersion` bump alone cannot repair this: historical real-TTL snapshots and regression-era `ttl: 0` snapshots share prior `formatVersion` values, so a verifier keying only on the version cannot separate them.

**Required design:**

1. Introduce format **v5**, whose canonical signed representation always serializes `r.Ttl`.
2. All new sealers emit v5 only. Zero is emitted only when the observed TTL is genuinely zero.
3. Verification runs through explicitly named canonicalization profiles: `legacyRealTTL`, `legacyZeroTTL`, `v5RealTTL`.
4. For affected historical versions only, attempt the legitimate historical encodings in a tightly bounded order. A `legacyZeroTTL` match returns that fact to the caller as a semantic/provenance warning — not a bare valid result.

**Rationale.** A snapshot sealed during the regression carries a valid Ed25519 signature over the bytes its sealer emitted. Making it unverifiable destroys evidence continuity without changing the fact that the TTL assertion was wrong. The distinction the contract must express: *cryptographic validity* (the signature validly attests the historical byte sequence) versus *semantic reliability* (that TTL was fabricated by the implementation and must never be treated as observed).

**Guardrails:**

- No open-ended serializer retry. Fallback is scoped to the exact affected legacy versions and build window.
- Verification output returns the matched profile, e.g. `verifiedCanonicalization: .legacyZeroTTL`.
- `legacyZeroTTL` results carry: "Signature is valid, but TTL was canonicalized as zero due to a historical producer defect."
- v5 verification accepts the real-TTL serializer **only** — no fallback.
- Fixed cross-runtime golden vectors in Swift and C# covering legacy real TTL, legacy zero TTL, v5 real TTL, and nonzero / zero / boundary / absent-normalized TTL cases.
- The byte-level schema and canonicalization profile are documented alongside `formatVersion`. A version must mean "these exact bytes are signed", not "roughly this evidence shape".
- If v2/v3/v4 each underwent multiple silent byte-layout shifts, each layout is treated as a distinct historical verification profile even where the recorded `formatVersion` is identical.

## Acceptance Criteria

- [ ] All 36 defects are resolved or formally retired as already-repaired with recorded verification evidence.
- [ ] Every live defect has a regression test encoding its failure scenario, written before the fix and observed failing for the right reason.
- [ ] No section reports an affirmative clean or affirmative tampering result from evidence it did not measure (W02, W04, W05, W20, M04, M09, M10 and the false-positive cluster W06, W11, M03, M14).
- [ ] Cross-runtime parity holds for every arbitrated value: one DNSSEC plausibility span across C#/Swift/Python, one comparator tri-state result across C#/Swift, one coverage accumulation rule across C#/Swift, one signed-payload canonicalization across C#/Swift.
- [ ] The M12 contract ships with named profiles, bounded legacy fallback, provenance warning, and cross-runtime golden vectors.
- [ ] `dotnet test` (Syxx.Tests, SyxxUI.Logic.Tests, Avalonia tests), `swift test`, and `pytest` pass locally.
- [ ] `ruff`, `ty`, `swiftlint --strict`, and `dotnet build` clean for all `net10.0` projects.
- [ ] `MEMORY.md`, `CHANGELOG.md`, and the track TODO are current per `IMPLEMENTATION-RULES.md` §9.
- [ ] Final diff reviewed for scope, regressions, and sensitive data; no secrets committed.

## Dependencies

- `findings.md` (this directory) — authoritative defect list.

### Baseline established 2026-08-12 (pre-plan inspection)

- **`warp-hardening_20260811` is not contending.** All tasks in all 4 phases are `[x]`; only its Final Verification block is open. Its `index.md` is stale (reports 0/4 phases against a plan.md showing 19/19 tasks) — not this track's file to correct, flagged for the owner.
- **Single file overlap: `LicenseManager.cs`.** That track's Task 4.3 deleted a dead sync wrapper there; it is the only cited defect file currently modified in the working tree. W17/W18 must be applied on top of that change, not against HEAD.
- **No contention on Warp files.** `WarpManifestPinning.cs`, `WarpEngine.cs`, `WarpCommand.cs`, and `Program.cs` are all clean; W08 and W12 are unblocked.
- **The 33k-line working-tree deletion is documentation, not code.** 62 files relocated to `docs/stale/`. Code-only diff is 53 files, +765 / −559.
- **Findings were written against a pre-revert tree.** `.full-review/archive/2026-08-12/state.json` records "All findings re-verified against the current snapshot after workspace revert". Two cited files do not exist at HEAD:
  - **W07** — `CollectorPinning.cs` is absent. `LeakDetector.ConfiguredPins()` (`LeakDetector.cs:906`) reads `SYXX_COLLECTOR_PINS` only, with no persisted-file source. The described defect cannot occur, because the persisted-pin-file feature itself is not present. Whether that feature should exist is a separate question, not this track's to answer.
  - **W13** — `LinuxSecretStore.SecretTool.cs` is absent; the code lives in `LinuxSecretStore.cs` (`WaitForExit` at `:541`, `:565`). The defect may still be live at the new location.
- **Consequence:** an unknown subset of the 36 describes code that was reverted or relocated. Triage against HEAD is mandatory before any repair work, and per-defect classification must include *describes-absent-code* and *moved-file* alongside live and already-repaired.

## Out of Scope

- The separate `/full-review` findings in `.full-review/` — including SEC-H1, the dead DB-provisioned offline-license path. Different review, owner decision pending, its own track.
- Hosted CI restoration (owner budget constraint, ~2026-08-27). Local validation only.
- Any new feature work, refactor, or cleanup not required by one of the 36 defects.
- Compiling or running `SyxxUI` / `SyxxUI.Tests` on this machine.

## Technical Notes

- Toolchain present: dotnet 10.0.302, Swift 6.3.3, uv/ruff/ty for `Server/`.
- `Syxx.Kit` is `net10.0` — 20 of 21 Windows defects are locally testable; W19's relocation removes the 21st from the Windows-only surface.
- W04/W05/W06 all sit in `DetectUnanimousTampering` and must be designed as one coherent tri-state, not three patches.
- W01/W02/W03/W20 and M02/M09/M10 are all synthesis-matrix honesty defects; they interact and share test fixtures.
- Several defects are second occurrences of a previously-fixed regression, so tests must bind the behavior, not the current line number.
- Rules files under `rules/` are read-only. No agent may edit, move, or restore them.

### `BUDGET-RULES.md` (added 2026-08-12, governs this track)

- **Decision gates** (§Decision Gates) — explicit owner approval before changing signed bytes, canonicalization, schemas, or wire protocols, and before selecting among options with materially different compatibility consequences. Binds M12 (design approved) and W16 (**ratified 2026-08-12** at `63_072_000` s / 730 days across all three runtimes; already unified at HEAD and verified sound, so the remaining work is enforcement binding — a shared cross-runtime fixture and live tests in C#, Swift, and Python — not a value change).
- **Test budget** (§Test Budget) — security, signature, parsing, and network code requires negative tests, boundary tests, regression vectors, and cross-platform validation. That table covers most of the 36 and sets the per-defect validation floor.
- **Compatibility rules** (§Compatibility and Evidence) — ratifies the M12 design: canonical bytes are an API contract, historical verification only through named bounded profiles, golden vectors for cross-runtime encodings, cryptographic validity separated from semantic trustworthiness.
- **Smallest-change tension** — §Implementation #1 prefers the smallest sufficient change; decision 2 above selects redesign. The §Exception Rule permits this as an explicit user request and to establish correctness of high-impact changes. Redesigns stay bounded to the defect: no cosmetic refactors bundled with behavioral fixes (§Implementation #3).
- **Compaction** (§Compaction Checkpoints) — each wave ends with a durable handoff using the §Durable Handoff Template, so the track survives context loss.
- **Honest validation** (§Test Budget) — where validation cannot run, state what ran, what did not, why, and the residual risk. No claimed validation that was not performed.

---

_Generated by Conductor. Review and edit as needed._
