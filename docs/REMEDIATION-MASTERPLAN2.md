# Syxx Remediation MASTER PLAN 2 — remaining work (Tiers 1-6)

**Author:** Claude (Syxx neural lead) · **Date:** 2026-07-14 · Branch `feat/beta-signup-and-server-hardening`
**Supersedes:** `REMEDIATION-MASTERPLAN-complete.md` (the original WS1-WS5 plan), which an adversarial
6-agent code audit (workflow `wf_fcee90f0-c89`, 2026-07-14) found **partially implemented, not done**.
This document carries every item the audit found still unimplemented, in dependency order, with verified
`file:line` seams and per-item acceptance bars.

**What actually landed in the predecessor plan** (committed, byte-parity both platforms unless noted):

- **WS1 engine verdict-shape:** Ph3.1 cautious rule + lineage table; Ph4.1 class-set `Bel` confidence;
  Ph4.2 mechanism-class partition + concrete headline; Ph4.3 `classMargin` + within-class; Ph5.1
  cross-vantage discrepancy as scored DST mass (CDN = zero); Stage-5 headline / provenance
  (direct + unavailable) / within-class / answer-matrix; B098 TTL/type canonical coverage.
- **WS2:** WS2-A.1/A.2 (didFail truncated/servfail; ANCOUNT mismatch); WS2-A.4 **B072 only** (extended
  RCODE combine); WS2-B.1/B.2 union/degraded (partial).
- **WS3:** WS3-A.1 D057; WS3-A.3 D003/D004/D011/A006 **Kit-level** (pins never passed by the GUIs =
  inert in the shipped app); E011/E019/E024 identity.
- **WS5:** E015 Windows `ConclusionsView` (fail-closed Stage-5); Stage-5 reasoning views; advisories
  in `OverallVerdictView`; macOS B097 `ProphetView` tri-state; `RemoteContentView` host allowlist.

---

## §0 — Binding conventions (every item)

1. **GUI is first** (`AGENTS.md` line 95-97: GUI > CLI). The build/verify bar for every tier is the GUI,
   not just the Kit: macOS `Syxx.app` via `macOS/bundle-app.sh debug` AND `release`; Windows `SyxxUI`
   built on the VM (`build.ps1` / `run-tests.ps1` at `~/Desktop/Shared/Syxx-Windows/`). Self-contained
   GUI honesty defects are prioritized, not deferred.
2. **Real run beats green tests** (Protocol §2/§9). The sandbox NOW has outbound **UDP:53 open** and the
   **`cryptography`** lib installed, so DNS/verdict/signing fixes MUST be verified against real input
   (live `syxx nslook`/`mirror`/`leak`, real Ed25519 sign+verify, real server + curl), not only unit tests.
3. **Both platforms, byte-parity.** A `SyxxKit` change needs its `Syxx.Kit` twin; trust-material touches
   `Server/` too. Windows WinUI (`SyxxUI`) cannot build on the macOS host — its build is a VM bar.
4. **Tri-state, never boolean** (Protocol §1/§10). Missing/timeout/invalid/unauthenticated → inconclusive,
   never a silent clean/secure/good/positive.
5. **Every fix ships a mutation-proven FAILING-CASE test** (Protocol §3/§4). Do not soften/delete a failing
   test — it exposes a real gap.
6. **Auto re-stage** after each tier: `scripts/stage-windows.sh` pushes the coherent Windows tree to the VM.
7. **Checkpoint** `CHANGELOG.md` + `MEMORY.md` (+ `ROADMAP.md`/`DELTAS.md`) between tiers, then continue.
8. **GUI-visible copy** → propose wording for approval (`AGENTS.md` line 42) before shipping the string.

Full per-item evidence lives in the audit transcript (`subagents/workflows/wf_fcee90f0-c89/`).

---

## Tier 1 — Honesty-critical false verdicts + evidence integrity (IN PROGRESS)

These produce actively wrong verdicts today. (Currently being implemented — workflow `wf_59fd4dbf-d55`.)

- **B050** — expired/not-yet-valid target RRSIG must read `.degraded`, not `.positive`
  (`Synthesizer.swift:101-106` / `Synthesizer.cs:109-114`). Preserve the B047 control-canary escalation
  (known-signed control returning unsigned = on-path stripping stays `.positive`).
  *Bar:* a real benign-lapse RRSIG reads degraded/inconclusive; a real DNSSEC-strip still reads tampered.
- **B051** — encrypted-transport `.blocked` only when a Do53 cleartext control SUCCEEDS while DoH FAILS
  (`EncryptedTransportProbe.swift:57-58` / `.cs`; `Synthesizer.swift:388-393` / `.cs:511-515`). Offline /
  down provider → inconclusive.
  *Bar:* real run offline → inconclusive; DoH-blocked-while-Do53-ok → blocked.
- **B070** — match answer owner-NAME to the question / CNAME chain; reject off-name in-class RRs
  (`DNSResolver.swift:770-797` / `.cs:589-595`). Fix the false "B069/B070" comment.
  *Bar:* a crafted response with a smuggled off-name A record does not surface that address (real query).
- **B073/B071** — distinguish no-OPT vs malformed-OPT (EDNS-inconclusive marker); strict authority parse
  (malformed != absent-denial) (`DNSResolver` both platforms).
- **A013** — restore ATOMIC compare-and-set mint while keeping A019 retry-after-outage
  (`fulfillment.py:81-88`; working-tree regression to non-atomic `kv_get`+`kv_set`).
  *Bar:* two concurrent deliveries mint exactly one; a `failed` event redelivered re-enters.
- **B049/B053/C028/B063** — remove the HARDCODED green "Signature valid...no tampering"
  (`WitnessView.swift:178-188`); tri-state gated on `signatureValid` AND `signatureKeyTrusted`; not-run /
  expired / untrusted / threw must be visually distinct from both green and a tamper verdict; add the
  missing Windows witness trust surface (parity, VM-build). GUI copy → approval.

**Also owed (confirmed on the VM, engine-lane):** the Windows cautious-combination collapses
correlated-vs-independent evidence to EQUAL confidence where macOS distinguishes them
(`SourceCorrelationTests` fails Windows 256/257 at `SyxxKitTests.cs:2261`, passes macOS). The merge code is
byte-identical (`SynthesisEngine.swift:177-205` ≡ `.cs:133-165`); the divergence is downstream in
`combine()`/`Bel`. Fix Windows to parity (do NOT touch the test — Protocol §4). Reconcile once Tier-1
releases `SynthesisEngine.cs`.

---

## Tier 2 — WS2-B/C evidence acquisition (precede WS1-Ph5 real-run; close macOS DoH parity)

- **WS2-C.1 / B010 — macOS DoH vantage parity (HIGH, GUI-relevant).** `VantageComparer.swift` is UDP-only;
  `evaluate()` (:121) has no `dohOutcomes`. Windows already runs DoH cross-vantage. Bring macOS to parity.
  *Bar:* macOS runs a real DoH cross-vantage query and a transport-selective tamper is caught.
- **WS2-C.3 / C014 — macOS `DetectUnanimousTampering`.** Absent on macOS (`VantageComparer` union-only);
  Windows has it (`VantageComparer.cs:200`). Add the macOS twin. Plus C015 sharedProvider softening; **D056**
  `encryptedDNSStatus` ANY-encrypted → `.yes` while a plaintext resolver still leaks (`NetworkKeyState`).
- **WS2-B.2 / B058/B059** — upgrade LIKELY→CONFIRMED redirection on union so a poisoned baseline that echoes
  the local tampered IP does not downgrade to merely DEGRADED (`VantageComparer.swift:146-188` / `.cs`);
  B057 wildcard reference `!=resolver`; B056 SVCB caveat.
- **WS2-B.4 / DNSSEC** — **A030** DoH EDNS DO-bit strip; **A042** RRSIG inception + RFC1982 serial-wrap
  (`DNSSECVerifier.swift:114-124` / `.cs:128-130` treat inception as absolute epoch, wrap NOT done);
  **B046** no-OPT/DO weighting; **B047** RRSIG-vs-local-clock; **A036** verify-unsigned. Client
  `DNSSECVerifier` + server `resolver.py` `_zone_is_signed`.
- **WS2-B.3 / B080** amp-timeout must not fabricate `recursiveLowAmp~0`; **B061** notADenial; **C010**
  version.bind gating parity.
- **WS2-B.5 / B084** ContentWarp CNAME-target not blocking all CDN; B074-B079 SVCB/CNAME/TTL edges.
- **WS2-C.2 / B025/C013** — Windows `ClassifyIPv6` `::ffff:0:0/96` mapped-private branch
  (`IPClassifier.cs:73-95` falls through to PublicRoutable → mapped sinkhole not blocked); C016/C017/C018.
- **WS2-C.4 / server** — **A044** walker sets `status='ok'` regardless of RCODE (`investigate.py:55`);
  A039/A040/A045/A058/A059; E001/E008/E010/E014; commit the uncommitted A020 `resolver.py` change.
- **WS1-Ph3.2 completeness** — populate `SectionEvidence.completeness` with a real per-section N/M (today
  always default 1.0, so the count-based clean-support scaling is inert) and build the bake-off runner over
  the Stage0 corpus. *Bar:* a real 4/5-clean + 1-timeout run shows visibly lower completeness than 5/5.

---

## Tier 3 — WS1 later phases (Ph5.2, Ph6, Ph7, Stage-5 remainder)

- **Ph5.2 / B005** — wire the two DEAD sub-scorers into engine output: `resolverProvenanceConfidence`
  (`Scorers.swift:53` / `.cs:55`) and `tamperMechanismFingerprint` (`Scorers.swift:87` / `.cs:92`) are
  defined to the no/partial/full-data contracts but never called (only `evidenceConfidence` is, at
  `SynthesisEngine.swift:395` / `.cs:386`). Ship-gate both. *Bar:* thin/empty evidence reads inconclusive.
- **Ph6.1 / B098** — actually WitnessSigner-SIGN the `DecisiveActCatalog` membership + version (add a
  signature field to `ValidatedDecisiveActs` and verify it). Today the catalog enum exists but is unsigned.
- **Ph6.2 / B062/B064** — POPULATE `ValidatedDecisiveActs` at runtime (thread
  `DecisiveActCatalog.currentValidatedActs` into `synthesize`; Windows discards it at
  `SynthesisEngine.cs:91` "Phase-2 scaffold"), wire M4 gate inputs, add the RRSIG-validated-only DNSSEC
  quorum and the structural NXDOMAIN decisive rule. *Bar:* a real round-robin block page trips `.positive`
  hijack; a `recordsPresentUnverified` quorum does not reach the decisive tier.
- **Ph7.1** — make `WeightRubric.getWeight` a LOADER over a defaults table (today a hardcoded switch,
  `Conclusion.swift:233+` / `.cs`), make `runWithWeights` actually apply overrides (macOS no-op; **Windows
  `Calibration.cs` has no `runWithWeights` at all = parity break**), add the corpus reliability-report
  runner. *Bar:* loading a different weight file changes verdicts deterministically.
- **Ph7.2** — author `Synthesis/RPNOccurrence.swift`/`.cs` (does not exist) with the static expert-elicited
  Occurrence default; populate Stage-5 `PriorityFactors.occurrence` (schema v2).
- **Stage-5 S5.1** — datapoint-level completeness that RETIRES `profile.sections.v0`
  (`SynthesisEngine.swift:434` / `.cs:422`); populate the still-empty `inferred` provenance channel.

---

## Tier 4 — WS3-B/C trust infrastructure

- **WS3-A.2** — COMMIT the licensing fail-closed change (HEAD still fail-open at `licensing.py:130`;
  the fix is in the working tree only).
- **WS3-A.3** — wire `trustedPublicKeys` + `pinSet` from BOTH GUIs into `runCollectorTest`
  (`AppModel.swift:381-385`, `AppViewModel.cs:218-220`) so D003/D011 actually engage in the shipped app.
- **WS3-B.1a / A024/C026/B030** — an empty collector must not mint a signed `leaked:false`; return
  inconclusive / sign over a liveness proof (`leak.py:140-164`). **WS3-B.1c / A034** — bound
  `leak_observations` (dedup/cap) against a replayed valid token (`db.py:272-282`).
- **WS3-B.2a / A056** — validate the Keygen `Date` header freshness before trusting the signed response
  (`licensing.py:176-199`). **WS3-B.2b / B085** — reject future-dated snapshots in `verifySnapshot`
  (`WitnessSigner.swift:130` / `.cs:127`).
- **WS3-B.3 / A110** run the systemd service as a dedicated unprivileged user (`deploy.sh:182` `User=root`);
  **A109** stop emitting inline `Environment=` secrets into the 0644 unit (`deploy.sh:190-199`); A113 fix the
  DER-vs-32-seed docstring contradiction; A114 rotation contract. **A023** scope `resolver_ip` PII out of the
  cross-readable `passive_dns` index (`passivedns.py:18-20`, `db.py:317-325`, `leak.py:120`).
- **WS3-C.1 / B091** derive the leak verdict from the SIGNED payload, not the outer JSON
  (`LeakDetector.swift:208-233` / `.cs:224`); **B092** add SPKI pin + response signature to
  `DimensionClient` ground-truth calls.
- **WS3-C.3 / C031** — add an expected-resolver baseline to `reconcileVerdict`
  (`LeakDetector.swift:292` / `.cs:331`) so the user's own resolver does not read as a leak.
- **WS3-C.4 / E020** remove free-text `errorMessage` from the signed canonical bytes; **E021** preserve
  expired-vs-untrusted-vs-invalid tri-state instead of collapsing to `Bool false`.
- **WS3-C.5** — licensing/key-hygiene grab-bag (D001/D021/D002/D006/D027/D030/D051/D052/D053/D066/B099/B100),
  starting with the unsigned offline-key path `licensing.py:204-212`.

---

## Tier 5 — WS4 server hardening (IN PROGRESS — most of B/C/D/E/F under way, payments pending Tier-1)

Being implemented in parallel (workflow `wf_39401ea1-95c`), EXCEPT WS4-A payments (fulfillment.py is owned
by Tier-1's A013).

- **WS4-A payments (after Tier-1):** A025 per-subscription slot (`fulfillment.py:97-98,116`); A035/A084
  stable idempotency key (`app.py:478` passes none); **A046** GET `expand[]` in query string not body
  (`stripe_checkout.py:32-33,64`); A047 don't mark minted on email-send failure; A032 trial over-cap eviction.
- **WS4-B SSRF:** A067/A088/A090/A100 pin the vetted sockaddr — stop `conn.host` re-resolve
  (`resolver.py:244-245`, `fetcher.py:94-95`); A063 out-of-bailiwick glue (`resolver.py:426,432-434`);
  A033 port allowlist (`fetcher.py:83`).
- **WS4-C crash-to-500:** A078/A098 guard question-section `struct.unpack` (`resolver.py:114,329`) → honest
  inconclusive not 500; A039 hostile DoH JSON; A038 DNSError → inconclusive not "good" (`app.py:846-854`);
  A075 `'signed'` → `dnskeyObserved` (`resolver.py:373,420`, copy-gated).
- **WS4-D diff:** A027 race-winner certifies clean without authority (`app.py:747-755,139-163`); A012 AA
  geo-DNS false tamper; A092 warp cache `final_url` key (`warp.py:79,109`); A094 caveat.
- **WS4-E DoS:** absolute request deadline + connection cap (`app.py:176` per-op only; `:1028`
  ThreadingHTTPServer unbounded); A054 ratelimit truncation; A079/A091/A095 row caps on passive_dns/audit;
  A052 kv timestamp + purge; A031 beta_signups purge; A085 reactivation handler; A057 version-compare ragged
  tuples (`app.py:447-461`).
- **WS4-F info-leak:** A105 readyz sqlite path (`app.py:544-545`); A112 silent truncation; A029 CSV
  formula-injection; A037 session-secret entropy (`db.py:191-193`); A104 broadcast scheme allowlist
  (`broadcast.py:42`); F030 test-harness stub leak.

### Keygen.sh + Stripe — production readiness (blockers)

- **Keygen:** a stock deploy DENIES every live validation — `deploy.sh` never provisions
  `SYXX_KEYGEN_PUBLIC_KEY` and never sets `SYXX_DEV_MODE`, so with D040 fail-closed + a hardcoded non-empty
  `KEYGEN_ACCOUNT_ID`, `/api/auth/activate` returns 402 for all real keys. A056 Date-replay unremediated.
- **Stripe:** mint idempotency reverted to non-atomic (A013 double-mint); license email silently no-ops
  without SMTP (paid buyer gets no key); A046 `expand[]` in GET body → `default_price` fetch fails; never
  exercised against a live Stripe endpoint.
- **To go live:** provision ~10 env artifacts (`SYXX_KEYGEN_PUBLIC_KEY`, KEYGEN account/product/policy +
  token, `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`, price/product IDs, SMTP host/creds) + land A013/A046.

---

## Tier 6 — WS5 client surfaces & platform safety (GUI — prioritized per GUI-first)

- **WS5-A GUI honesty:** **B090** gate the legacy verdict headline on Stage-5 `isValid`
  (`ConclusionsView.swift:26` / `.xaml.cs:34-41`); **B088** macOS `trust = 100-confidence` green-on-threat
  (`DashboardView.swift:239-251`) + Windows relabel Confidence (not TRUST SCORE) + populate Alerts;
  **B052** macOS `MirrorView` DEGRADED renders green (`MirrorView.swift:272-276`); **B097** Windows
  `ProphetView` still collapses `.No`+`.Unknown` to cleartext (`ProphetView.xaml.cs:28`); B067/F013/F018/F019
  macOS Dashboard green on "—"; B041/B042 CLI `showConclusions` drops advisories (`SyxxCLI.swift:118-148`).
- **WS5-B Cage honesty:** **C037/C039** CLI prints "Saved"/"erased" unconditionally though the IO is
  `try?`-swallowed (`SyxxCage.swift:183-186`); C038 unparsedCount must gate the verdict; **C042/D063**
  direction attribution from the forgeable QR bit (`SyxxCage.swift:244`) — key on port-53; C019-C024/C043
  capture UDP-only + no coverage disclosure (`DNSCapture.swift:249,265-266`); **D060** osascript apostrophe
  root injection (`AppModel.swift:589`); **D065** no `O_NOFOLLOW`; D067 unbounded `pidCache`
  (`DNSCapture.swift:421`).
- **WS5-C URL/SSRF/resource:** D045/D032-D036/D064 scheme+HTTPS-origin allowlist before opening
  server-controlled URLs (`BroadcastPanelView.swift:38-39`, `DashboardView.swift:39-40`); **F028** Windows
  cage NaN/Inf duration → AddSeconds crash (`CageCommand.cs:27,90`); **D026** `SieveBlocklistLoader`
  `://`-path LFI/remote fetch (`SieveBlocklistLoader.swift:24-32`); D018/D019/D025 client SSRF re-bind.
- **WS5-D licensing UX:** **D042** license save-failure prints SUCCESS / sets Pro; **D043** network outage
  vs invalid key; **D044** render the leak verdict in the Windows `GluttonView`; **E044** WinUI sync-over-async
  UI freeze; E012/E016.../F015-F029 hygiene cluster.

**Windows note:** every WinUI (`SyxxUI`) change is a VM-build bar — verify with `build.ps1` + `run-tests.ps1`
in `~/Desktop/Shared/Syxx-Windows/`. macOS SwiftUI + `Syxx.Kit`/console + Server build here.

---

## Handoff

- **`syxx-neural-executor`** — WS1 phases (Ph5.2/6/7 + S5.1), the Windows cautious-combination parity fix,
  answer-matrix feeds.
- **`syxx-engineer`** — WS2 probes/transports, WS4 server, WS5 Cage/URL/resource logic.
- **`syxx-neural-surfacer` / `syxx-gui-architect`** — WS5 surfaces + Stage-5 rendering; all copy-gated
  wording proposed for approval.

*Predecessor: `REMEDIATION-MASTERPLAN-complete.md`. Audit provenance: `wf_fcee90f0-c89`.*
