# Syxx Document Deltas

A **DOCDELTA** is a defect in a principle document (`DESIGN.md`, `ROADMAP.md`, `MEMORY.md`,
`AGENTS.md`) rather than in shipped code. It qualifies when the document misdescribes the code,
contradicts another document or itself, cites a location that does not exist, states superseded
guidance as current, or admits a live defect that never reached `DELTAS.md`.

DOCDELTAS matter for the same reason a wrongly-green roadmap row matters: agents act on what the
documents say. A false statement in `DESIGN.md` produces wrong code as reliably as a false verdict
produces a wrong decision. Every finding below was verified against the live tree at the commit
recorded in the header, not inherited from another document's claim.

**Audit date:** 2026-07-30
**Tree state:** branch `matrix-cutover-phase3`, HEAD `788c5de`
**Post-audit note (2026-07-31):** `macOS/Sources/SyxxApp/AppModel.swift` and `SyxxApp.swift` were
edited (Jul 30 20:49, uncommitted) after this audit, resolving the 13 macOS GUI compile errors the
tail of this file used to name. J006 is filed against the resulting working tree and supersedes
that stale note.
**Verification standard:** every finding carries `file:line` on BOTH sides, the document and the
code, and was confirmed by direct read or command output in this session. Findings that could not be
confirmed are marked `UNVERIFIED` and say what would settle them.

## Domains

| Code | Domain | Meaning |
|---|---|---|
| **G** | Doc-vs-Code Drift | The document describes code that does not exist, or behaves differently. |
| **H** | Cross-Document Contradiction | Two documents, or one document with itself, assert incompatible things. |
| **I** | Terminology | Violations of the verdict / result / conclusion rule. |
| **J** | Admitted Live Defect, Unfiled | A document admits a defect that is still true in code and absent from `DELTAS.md`. |
| **K** | Structural / Navigational | Ordering, dangling references, and anything that makes a reader absorb stale content as current. |

Severity order for multi-domain findings: **J** (a live defect nobody is tracking) > **G** (agents
will write wrong code from it) > **I** > **H** > **K**.

## The terminology rule (owner directive, 2026-07-30)

`AGENTS.md:54` currently reads only: *"STOP USING THE WORD 'VERDICT'. Erase it from any apps and then
erase it from your contexts."* That states the prohibition without stating the replacements, which is
why the rename has been applied inconsistently. The owner's full rule is:

1. The word **verdict** is not to be used.
2. In **code**, the replacement is **result**.
3. In **user-facing interfaces and documentation**, the replacement is **conclusion**.
4. **Exception:** code may use *conclusion* when it names a user-facing surface. `ConclusionsView` is
   correct in code precisely because it implements the user-facing Conclusion pane.

This document uses the word *verdict* throughout, unavoidably, because it is the ledger that tracks
the word's removal. That is the one legitimate use.

## Two conventions in this file

**Verbatim quotes are byte-accurate and must stay that way.** Every `Code Sample:` block quotes the
source exactly, which means six of them contain em-dashes. Those are evidence of the violation
recorded in G004, not authored text, and correcting them would make the ledger misrepresent the
source. One of them, at I003, is a shipped Linux user-facing string that contains an em-dash, so it is
simultaneously evidence for two findings. Do not sanitize the quotes.

**Alignment.** Fields align to value column 24, matching the `DELTAS.md` house template (162 labelled
fields, 0 misaligned, verified). Note that `scripts/reflow-deltas.py` cannot be used on this file: it
searches for a `## **A. ...` domain header and exits with "nothing to do" when it finds domains G
through K instead. Align by hand, or generalize the script's domain pattern first.

---

## **G. Doc-vs-Code Drift**

---
- **[G001]**            DESIGN.md declares the Windows engine retired when Windows now runs the matrix engine
- **Criticality:**      Critical. An agent reading this will refuse to touch working Windows code, or
                        will "port" an engine that is already ported. User made aware in this report.
- **Location:**         DESIGN.md:141-142; Windows/dotNET/Syxx.Kit/Synthesis/Conclusion.cs:21, 29, 51-64
- **Issue:**            DESIGN.md:141-142 states "`Windows/Syxx.Kit/Synthesis/` still runs the retired
                        DST engine until the Phase 3 port (MATRIX-MIGRATION-PLAN.md) do not extend
                        it." That is false twice over. First, the path is wrong: the Windows tree
                        moved to `Windows/dotNET/Syxx.Kit/`. Second, the engine claim is wrong:
                        `Conclusion.cs:21` reads "Replaced by `Determination: DeterminativeResult?`
                        the matrix engine's tri-state result", `:29` declares
                        `DeterminativeResult? Determination = null`, and `:51-64` expose
                        `Determination?.State == DeterminativeState.Clean` and `HasNoDetermination`
                        with the comment "These are thin wrappers over Determination, never DST
                        fields." Windows migrated. The "do not extend it" instruction now actively
                        misdirects work away from the live engine.
- **Cross-ref. Delta:** None in DELTAS.md (empty, post-cycle). Related H001, same doc.
- **Fix(es):**          [Fix 1] Rewrite DESIGN.md:141-142 to state Windows runs
                        MatrixDeterminativeEngine behind the `UseMatrixEngine` flag, that the DST
                        engine is retained but unreferenced, and that deletion is the remaining step.
                        [Fix 2] Correct every `Windows/Syxx.Kit/` path in DESIGN.md to
                        `Windows/dotNET/Syxx.Kit/`.
                        [Fix 3] Name the Linux client, which DESIGN.md's synthesis section omits
                        entirely despite three-platform parity being binding per AGENTS.md:49.
- **Code Sample:**      [

                        // Windows/dotNET/Syxx.Kit/Synthesis/Conclusion.cs:21
                        /// Replaced by `Determination: DeterminativeResult?` — the matrix engine's tri-state result.

                        // :51-53
                        /// These are thin wrappers over Determination, never DST fields.
                            Determination?.State == DeterminativeState.Clean;

                        ]
---

---
- **[G002]**            DESIGN.md's score-design invariants govern three scorers that were deleted
- **Criticality:**      Non-critical for runtime, critical for planning. 76 lines of binding contract
                        describe code that cannot be found. Any agent implementing B005 from this
                        section builds against a vanished architecture.
- **Location:**         DESIGN.md:159-234; macOS/Sources/SyxxKit/Synthesis/ (no Scorers.swift)
- **Issue:**            DESIGN.md:159-234 specifies no-data, partial-data, and full-data contracts for
                        "Resolver Provenance Confidence", "Tamper Mechanism Fingerprint", and
                        "Evidence Confidence Score", and states they are "binding on the B001/B002
                        sub-scorer implementations". Those scorers no longer exist. `Scorers.swift`
                        was deleted from macOS on 2026-07-21 with the DST engine, and grep finds zero
                        occurrences of `resolverProvenanceConfidence` or
                        `tamperMechanismFingerprint` anywhere in macOS/Sources. The section also
                        rests on DST belief mass ("weighted by its DST belief mass", DESIGN.md:188)
                        and on a numeric confidence percentage, both of which DESIGN.md:147 itself
                        says no longer exist: "No confidence percentage exists." The document
                        contradicts itself 40 lines apart.
- **Cross-ref. Delta:** None. Related H002, the same self-contradiction seen from the other side.
- **Fix(es):**          [Fix 1] Mark DESIGN.md:159-234 as RETIRED with the 2026-07-21 owner directive
                        cited, rather than deleting it, so the reasoning survives for whoever
                        calibrates the matrix engine in Phase 6.
                        [Fix 2] Replace it with the matrix engine's actual honesty contract, which
                        DESIGN.md:148-157 already states correctly and could be promoted.
                        [Fix 3] Note that ROADMAP.md's B003 independence decision, which blocked the
                        B005 composition math, is now moot because the scorers it governed are gone.
- **Code Sample:**      [

                        # DESIGN.md:188 describes DST belief mass weighting
                        # DESIGN.md:147 says the opposite about the current engine:
                        #   "Abstention is `.inconclusive` with an EMPTY diagnoses list — never a
                        #    scored class, never a distribution. No confidence percentage exists."

                        $ grep -rc 'resolverProvenanceConfidence' macOS/Sources
                        0

                        ]
---

---
- **[G003]**            MEMORY.md reports every suite green and artifacts shipped while two GUIs do not compile
- **Criticality:**      Critical. This is the newest entry in the file, so it is the first thing an
                        agent reads, and it reports a green tree that does not build.
- **Location:**         MEMORY.md:100-102; macOS/Sources/SyxxApp/ (13 errors); Linux/Ubuntu/SyxxUI.Avalonia/ (2 errors)
- **Issue:**            MEMORY.md:100-102 states "Swift 661 with 5 expected skips and 0 failures;
                        .NET core 286/286; Windows UI lifecycle 10/10; Linux Avalonia 52/52; macOS
                        Debug and Release builds plus both app-bundle modes passed. Final artifacts
                        are `macOS/Syxx.app` and `macOS/Syxx-2.0.0.dmg`." A real `swift build` from
                        `macOS/` in this session failed at `emit-module` with 13 distinct errors, all
                        in `Sources/SyxxApp`, the GUI. `Linux/Ubuntu/SyxxUI.Avalonia` fails with 2
                        errors because `IntegrityTag` is internal but consumed cross-module.
                        `Windows/Syxx.Linux.slnf` cannot load at all (MSB5028). The Kit and CLI do
                        compile clean, so the entry is not wholly wrong, but per AGENTS.md the GUI is
                        "several fold the importance of the CLI", and the entry reports the failing
                        half as passing.
- **Cross-ref. Delta:** None. Related H003.
- **Fix(es):**          [Fix 1] Amend MEMORY.md:100-102 to scope the green claim to SyxxKit, SyxxCLI,
                        and DeepSyxx, and to record the GUI failures with their error counts.
                        [Fix 2] State plainly that a `.app` and `.dmg` produced while SyxxApp does
                        not compile were built from an earlier tree, so the artifact claim does not
                        describe HEAD.
- **Code Sample:**      [

                        $ cd macOS && swift build
                        Sources/SyxxApp/AppModel.swift:970:13: error: invalid redeclaration of 'result'
                        Sources/SyxxApp/DimensionService.swift:175:27: error: cannot find 'IntegrityTag' in scope
                        Sources/SyxxApp/ConclusionsView.swift:219:37: error: type '()' cannot conform to 'View'
                        ... 13 total
                        error: emit-module command failed with exit code 1

                        ]
---

---
- **[G004]**            MEMORY.md's em-dash admission cites a dead location and understates scope by three orders of magnitude
- **Criticality:**      Critical. This is a standing directive the owner has raised six or seven
                        times. The document that is supposed to prevent regression measures the
                        problem at 4 and the real figure is 2,978.
- **Location:**         MEMORY.md:16-17; macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift (291 lines total)
- **Issue:**            MEMORY.md:16-17 states "Four shipped strings in `SynthesisEngine.swift:728-743`
                        violate it today." That file is 291 lines long, so lines 728 to 743 do not
                        exist; the citation died when the matrix migration shortened the file. The
                        file does contain 28 em-dashes. Tree-wide the count is 1,564 lines in macOS
                        Swift sources, 1,322 in Windows C# and XAML, and 92 in Linux C# and AXAML:
                        2,978 lines total. Not all are user-facing strings, many are comments and
                        prose, but AGENTS.md:66 and MEMORY.md:11-13 both extend the prohibition to
                        "code comments" explicitly. An agent reading MEMORY.md believes four strings
                        remain and will close the item after fixing four.
- **Cross-ref. Delta:** None. This is the measurement defect, not the em-dashes themselves.
- **Fix(es):**          [Fix 1] Replace the dead citation with the real tree-wide counts and a
                        reproducible command, so the number cannot silently rot again.
                        [Fix 2] Separate the count into user-facing strings versus comments, since
                        only the former is shipped copy and the two need different remediation paths.
                        [Fix 3] Add a CI grep gate. A directive that regresses seven times is not
                        going to be held by a prose reminder.
- **Code Sample:**      [

                        $ wc -l < macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift
                        291                          # MEMORY.md cites lines 728-743

                        $ grep -rn $'\u2014' macOS/Sources --include='*.swift' | wc -l
                        1564
                        # Windows 1322, Linux 92. MEMORY.md:16-17 says "Four".

                        ]
---

---
- **[G005]**            MEMORY.md claims zero third-party tool names remain; one survives on Windows
- **Criticality:**      Non-critical, but it is a firm owner directive stated twice and reported
                        complete. The macOS purge landed; the Windows twin kept an identifier.
- **Location:**         MEMORY.md:18-22, 135-138; Windows/dotNET/SyxxUI/ViewModels/AppViewModel.cs:120, 124, 132
- **Issue:**            MEMORY.md:135-138 records "Third-party names purged to zero occurrences" and
                        the standing directive at :18-22 instructs "Grep both platforms and
                        `Server/` for all four before calling any related work done." `ProcessMonitor`
                        still appears as a method name, `ProcessMonitorTick`, at three sites in the
                        Windows GUI view-model. `DNSMonitor`, `FileMonitor`, and `Objective-See` are
                        genuinely at zero across macOS, Windows, and Server sources. So the sweep was
                        real but incomplete, and it was reported as complete.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Rename `ProcessMonitorTick` to `MonitorTickForProcesses` or similar,
                        Windows-only, no macOS twin affected.
                        [Fix 2] Correct MEMORY.md:135-138 to say macOS and Server reached zero and
                        Windows retained one identifier, rather than claiming a clean sweep.
- **Code Sample:**      [

                        // Windows/dotNET/SyxxUI/ViewModels/AppViewModel.cs:132
                        private void ProcessMonitorTick(MonitorTick tick)

                        ]
---

---
- **[G006]**            macOS deployment target is macOS 14 against a rule MEMORY.md marks still binding
- **Criticality:**      Non-critical for detection, relevant to notarization and to any macOS 26 API
                        the GUI redesign assumes is available.
- **Location:**         MEMORY.md:425-427; macOS/Package.swift:7
- **Issue:**            MEMORY.md:425-427 states the macOS deployment target "must be macOS 26
                        (Tahoe), NOT `.macOS(.v14)` / `.v15`" and explicitly marks that rule "Still
                        binding, not superseded", in contrast to the .NET parts of the same entry
                        which were superseded on 2026-07-18. `macOS/Package.swift:7` reads
                        `.macOS(.v14)`. The file was edited recently for the DeepSyxx `-sectcreate`
                        work, so the platform line was passed over rather than never revisited.
                        AGENTS.md:70 describes the product as macOS 26.
- **Cross-ref. Delta:** None. Related J003, the same manifest.
- **Fix(es):**          [Fix 1] Decide and record whether the target is 26 or 14. If 26, change
                        `macOS/Package.swift:7` and confirm the SwiftUI APIs in use still compile.
                        [Fix 2] If 14 is deliberate, supersede MEMORY.md:425-427 explicitly instead
                        of leaving a binding rule the tree violates.
- **Code Sample:**      [

                        // macOS/Package.swift:7
                            .macOS(.v14)

                        ]
---

---
- **[G007]**            DESIGN.md's shared-probe-orchestration section is written as a proposal for work that landed
- **Criticality:**      Non-critical. Wastes planning effort and invites duplicate implementation.
- **Location:**         DESIGN.md:237-291
- **Issue:**            DESIGN.md:237-291 describes C005 in present-tense-problem and future-tense-fix
                        form: "Today each of the four frontends runs probes in a private
                        `runProphet`/`runMirror` method", then proposes `ProbeRunner` and
                        `ProbeBudget` as things to add, with a three-step "Incremental adoption"
                        plan. MEMORY.md:1178-1333 records C005 as delivered with all phases done and
                        per-section budget defaults, and both types exist. The section is a shipped
                        feature still presented as a design proposal, so its adoption checklist reads
                        as outstanding work.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Rewrite in past tense as an architecture description, and move the
                        per-section budget defaults from MEMORY.md into it so DESIGN.md holds the
                        contract and MEMORY.md holds only the session history.
                        [Fix 2] Retain the documented limitation R1 (probes wrapping
                        `queryUDPDetailed` can overrun their timeout by roughly 4s because
                        cancellation is not enforced at the transport layer), which belongs in
                        DESIGN.md rather than buried in a session note.
- **Code Sample:**      [

                        DESIGN.md:241  "Today each of the four frontends ... runs probes in a
                        DESIGN.md:277  "### Incremental adoption
                        DESIGN.md:278   1. Add `ProbeBudget` + `ProbeRunner` to `SyxxKit`/`Syxx.Kit`

                        ]
---

---
- **[G008]**            DESIGN.md's GUI redesign section depends on an external Downloads folder
- **Criticality:**      Non-critical, but it makes the design unreproducible for any other machine or
                        agent, and AGENTS.md:377 asks agents to record concerns with that design.
- **Location:**         DESIGN.md:379-385
- **Issue:**            DESIGN.md:379-385 points the canonical GUI mockups and implementation guide at
                        four paths under `/Users/masterrabbit/Downloads/Dartboard GUI redesign
                        fantasy theme-5/`. A principle document that sources its design authority
                        from a user's Downloads directory cannot be honored by CI, by a fresh clone,
                        or by any agent on another machine. DESIGN.md:391 separately references
                        `Mockups-Web-Promos/` in-repo, so two different mockup locations are cited as
                        authoritative in one document.
- **Cross-ref. Delta:** None. Related H004.
- **Fix(es):**          [Fix 1] Move the four mockups and IMPLEMENTATION_GUIDE.md into the repo, and
                        point DESIGN.md at the in-repo path.
                        [Fix 2] Reconcile against `Mockups-Web-Promos/` and state which set is
                        canonical, since F038's mockup-translation guard at DESIGN.md:391 cites the
                        other one.
- **Code Sample:**      [

                        DESIGN.md:381
                        '/Users/masterrabbit/Downloads/Dartboard GUI redesign fantasy theme-5/Syxx macOS GUI Mxups.html'

                        DESIGN.md:391
                        Mockup data regions are watermarked ... (`Mockups-Web-Promos/`)

                        ]
---

## **H. Cross-Document Contradiction**

---
- **[H001]**            A5 continuous monitoring is [done] and its live path has never executed
- **Criticality:**      Critical. ROADMAP.md calls monitoring "THE subscription item", so the
                        contradiction sits on the feature the business model depends on.
- **Location:**         ROADMAP.md:434 versus ROADMAP.md:145, 216, 221-222, 487-491, 1641
- **Issue:**            ROADMAP.md:434 marks "[done] A5 Continuous monitoring mode (THE subscription
                        item)" with alerts, state persistence, and OS notification delivery wired on
                        both platforms. ROADMAP.md:145 marks row D-2 Continuous monitoring absent,
                        with "the live path has never executed" on either platform. ROADMAP.md:216
                        makes D-2 a v1 requirement, "or there is no subscription to sell".
                        ROADMAP.md:487-491 records that A5 persists only `LastTick` and `LastChange`,
                        not a multi-entry history, which is why C8 is blocked. ROADMAP.md:1641 calls
                        the A5 scheduler PARTIAL. Kit-level logic being complete while the production
                        path has never run is exactly the state AGENTS.md §2 says does not qualify as
                        done.
- **Cross-ref. Delta:** None. Same class as H002 and H003: Kit-complete read as product-complete.
- **Fix(es):**          [Fix 1] Downgrade ROADMAP.md:434 from [done] to partial, with the bar stated
                        as one real scheduler tick delivering one real notification.
                        [Fix 2] Reconcile the A-series and letter-dash row schemes, or drop one. Two
                        ID schemes tracking the same features in one file is the root cause here.
- **Code Sample:**      [

                        ROADMAP.md:434   [done] A5 — Continuous monitoring mode (THE subscription item)
                        ROADMAP.md:145   D-2  Continuous monitoring  ⬜  "the live path has never executed"

                        ]
---

---
- **[H002]**            DESIGN.md asserts both that no confidence percentage exists and that three confidence scores are binding
- **Criticality:**      Non-critical to runtime, high for anyone implementing scoring.
- **Location:**         DESIGN.md:147 versus DESIGN.md:159-234
- **Issue:**            DESIGN.md:147 states of the current engine: "Abstention is `.inconclusive`
                        with an EMPTY diagnoses list, never a scored class, never a distribution. No
                        confidence percentage exists." DESIGN.md:159-234 then specifies three
                        confidence scores in numeric ranges, including "Evidence Confidence Score
                        ... This is the flagship number shown to the end user" at :215-217, with
                        `Float` outputs in [0,1] and a 0.40 cap rule. Both cannot be true. The first
                        describes the shipped matrix engine; the second describes the retired DST
                        scorers. See G002.
- **Cross-ref. Delta:** G002, the same defect from the code side.
- **Fix(es):**          [Fix 1] Resolve by retiring :159-234 per G002 Fix 1, which removes the
                        contradiction rather than papering over it.
- **Code Sample:**      [

                        DESIGN.md:147  "No confidence percentage exists."
                        DESIGN.md:217  "This is the flagship number shown to the end user."

                        ]
---

---
- **[H003]**            ROADMAP.md asserts nothing was marked done that is not done, then downgrades five rows
- **Criticality:**      Non-critical alone, but it teaches agents to trust the checkmarks, which is
                        the failure mode MEMORY.md:228-236 warns about by name.
- **Location:**         ROADMAP.md:97 versus ROADMAP.md:99, 1901-1910
- **Issue:**            ROADMAP.md:97 states "Nothing was marked done that is not done." Two lines
                        later, :99 concedes items marked done were "spot-checked, not re-proven
                        end-to-end" and that several "cannot be verified from this host at all". The
                        same-dated audit at :1901-1910 then downgrades five previously-green rows,
                        including B-7, whose Windows twin is recorded as holding 17 references to
                        archived types and not compiling. ROADMAP.md:97 also states the correct
                        principle immediately before contradicting it: "a wrongly-green roadmap row
                        is the same defect class as a wrongly-green verdict."
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Delete the "nothing was marked done that is not done" sentence. The
                        surrounding caveats are honest; the sentence is the only false part.
                        [Fix 2] Add a verification column to the 47-row matrix distinguishing
                        proven-on-this-host, spot-checked, and unverifiable-without-hardware.
- **Code Sample:**      [

                        ROADMAP.md:97  Nothing was marked done that is not done.
                        ROADMAP.md:99  Items marked ✅ were spot-checked, not re-proven end-to-end ...
                                       several cannot be verified from this host at all.

                        ]
---

---
- **[H004]**            Two typography specifications are both stated as current
- **Criticality:**      Non-critical, but the GUI is the stated top priority and the two sets differ.
- **Location:**         MEMORY.md:44-45 versus MEMORY.md:113-117; DESIGN.md:35-38
- **Issue:**            The 2026-07-21 Warden theme entry names Cinzel for display and EB Garamond for
                        lore italic, with system-ui body and ui-monospace code. The 2026-07-30 entry
                        names five roles: Cinzel display, Archivo UI and body, Cormorant SC deck, EB
                        Garamond Italic emphasis, JetBrains Mono technical. Archivo, Cormorant SC,
                        and JetBrains Mono are absent from the earlier set; system-ui and
                        ui-monospace are absent from the later one. DESIGN.md:35-38 states the later
                        set. The earlier entry is not marked superseded, and it additionally records
                        that Cinzel and EB Garamond were never bundled into macOS Resources, so text
                        fell back to Georgia. Whether the newly named fonts are bundled is a separate
                        open question.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Mark MEMORY.md:44-45 superseded by the 2026-07-30 role list.
                        [Fix 2] Verify all five font families are bundled with licenses on all three
                        platforms and record the hashes, since DESIGN.md:37 requires the bytes match.
- **Code Sample:**      [

                        MEMORY.md:113  Fonts: Cinzel display, EB Garamond lore italic, system-ui body
                        MEMORY.md:44   Cinzel display, Archivo UI/body, Cormorant SC deck,
                                       EB Garamond Italic emphasis, JetBrains Mono technical

                        ]
---

---
- **[H005]**            MEMORY.md states two opposite commit policies
- **Criticality:**      Non-critical but directly operational. An agent following the wrong one
                        either loses work or commits another session's staged files.
- **Location:**         MEMORY.md:776 versus MEMORY.md:1115
- **Issue:**            MEMORY.md:776 records the user saying "Forget about commits. No one cares."
                        MEMORY.md:1115 records "Do NOT commit/push unless the user asks." Neither is
                        marked superseded and they are roughly three days apart. The stakes are now
                        higher than when either was written: the working tree currently carries 443
                        staged files from prior sessions, so an agent acting on the permissive
                        reading commits other sessions' in-flight work.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Have the owner state one policy and mark the other superseded.
                        [Fix 2] Record the `git commit --only <path>` technique regardless of which
                        policy wins, because the 443-file staged state makes a bare `git commit`
                        destructive under either reading.
- **Code Sample:**      [

                        MEMORY.md:776   "Forget about commits. No one cares."
                        MEMORY.md:1115  "Do NOT commit/push unless the user asks."

                        ]
---

## **I. Terminology (verdict → result / conclusion)**

---
- **[I001]**            Windows kept VerdictAction after macOS renamed it to ResultAction, and its header cites the dead path
- **Criticality:**      Non-critical to runtime, high for parity. macOS is the gold standard per
                        AGENTS.md:7 and it already performed this rename, so the direction is settled
                        and Windows simply did not follow.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/VerdictAction.cs:7-8, 24, 26;
                        macOS/Sources/SyxxKit/Synthesis/ResultAction.swift:19, 33
- **Issue:**            macOS renamed the A1 "so what" layer to `ResultAction.swift`, declaring
                        `public struct ResultAction` at :19 and `public enum ResultActions` at :33.
                        The Windows twin is still `VerdictAction.cs`, declaring
                        `public sealed record VerdictAction` at :24 and `public static class
                        VerdictActions` at :26. Its own header comment at :7-8 says it is "the Windows
                        twin of `macOS/Sources/SyxxKit/Synthesis/VerdictAction.swift`", a file that
                        does not exist. This is the ONLY remaining `Verdict` symbol in the entire
                        Windows source tree (10 occurrences, all in this one file), and Linux source
                        has zero. So the rename is one file away from complete on code identifiers.
                        Grep also finds no consumer of `VerdictAction` anywhere in
                        Syxx.Kit, Syxx, SyxxUI, or Syxx.Tests, so it is currently dead code, which
                        makes the rename low-risk but also raises whether the A1 layer is wired on
                        Windows at all.
- **Cross-ref. Delta:** Related I002: do NOT extend this rename to the signed wire field.
- **Fix(es):**          [Fix 1] Rename the file to `ResultAction.cs`, the record to `ResultAction`,
                        and the static class to `ResultActions`, matching macOS exactly.
                        [Fix 2] Correct the header comment to cite `ResultAction.swift`.
                        [Fix 3] Separately determine why nothing consumes it. A "so what" layer with
                        no caller means the Windows Conclusions surface has no plain-language line,
                        which is a functional parity gap, not a naming one.
- **Code Sample:**      [

                        // Windows/dotNET/Syxx.Kit/Synthesis/VerdictAction.cs:7-8
                        // MATRIX-MIGRATION-PLAN.md Phase 3.6 — the Windows twin of
                        // macOS/Sources/SyxxKit/Synthesis/VerdictAction.swift.        <-- does not exist

                        // :24
                        public sealed record VerdictAction(string Meaning, string PrimaryAction, Severity Severity, bool Qualified);

                        ]
---

---
- **[I002]**            "verdict" is a field name inside the SIGNED witness payload; renaming it breaks issued evidence
- **Criticality:**      Critical. A naive completion of the terminology rename silently invalidates
                        every previously issued signed snapshot and breaks cross-platform witness
                        verification. This is the single most dangerous item in this ledger.
- **Location:**         corpus/witness-crossplatform/witness-macos-answers.json (`evidence.comparisonResult.verdict`);
                        macOS/Sources/SyxxKit/WitnessSigner.swift:6-10, 19-20, 201, 260
- **Issue:**            `WitnessSigner.swift:19-20` documents `evidence: SignedEvidencePayload` as "The
                        canonical, cross-platform byte-stable content the signature actually covers
                        (E011)", and :6-10 confirms the display mirrors are re-checked against it so
                        "tampering a display field is rejected". The cross-platform golden vector
                        contains the key `verdict` at path `evidence.comparisonResult.verdict` with
                        value `"inconclusive"`. Because that key is inside the signed payload, its
                        name is part of the signed bytes. Renaming `verdict` to `result` changes the
                        canonical encoding, so every snapshot signed under the old name fails
                        verification, and macOS and Windows would disagree unless both change in the
                        same commit. `formatVersion` is pinned at 2 (`:201`) and the verifier accepts
                        only 2 or the error-message-free version (`:260`). MEMORY.md:265-268 records
                        that the flip to v3 is PARKED and must never be flipped one-sided. So the
                        terminology rename is coupled to a parked cross-platform flip.
                        `corpus/stage0/seeds.json` additionally carries `expectedVerdict`, a fixture
                        key, which is safe to rename because nothing signs it.
- **Cross-ref. Delta:** Related I001. Blocks on the E020 formatVersion flip recorded in MEMORY.md:265-268.
- **Fix(es):**          [Fix 1] DO NOT rename the signed field as part of a terminology sweep. Gate it
                        behind the E020 v2 to v3 flip so the name change and the version bump land
                        together, in ONE commit, on both platforms.
                        [Fix 2] Until then, add a comment at the field declaration recording that the
                        name is frozen by the wire format and is a known, accepted exception to the
                        terminology rule.
                        [Fix 3] Have the verifier accept both spellings across the v2 to v3 boundary
                        so existing snapshots keep verifying, and prove it with a golden vector in
                        each version.
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/WitnessSigner.swift:19-20
                        /// The canonical, cross-platform byte-stable content the signature actually covers (E011).
                        public let evidence: SignedEvidencePayload

                        // corpus/witness-crossplatform/witness-macos-answers.json
                        //   evidence.comparisonResult.verdict = "inconclusive"     <-- inside the signature

                        ]
---

---
- **[I003]**            Linux is the only client with "verdict" in user-facing UI text, including two honesty strings
- **Criticality:**      Non-critical to correctness, but it is shipped user-visible copy, it exists on
                        exactly one of three clients, and changing it needs owner approval per
                        AGENTS.md:56-60.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/Views/DashboardView.axaml:10, 249, 252;
                        Linux/Ubuntu/SyxxUI.Avalonia/Views/CageView.axaml:22;
                        Linux/Ubuntu/SyxxUI.Avalonia/Views/ConclusionsView.axaml:25, 53
- **Issue:**            Linux ships four user-visible strings and two comments containing "verdict".
                        `DashboardView.axaml:249` renders the label "No verdict yet" and :252 renders
                        "No testimony has been gathered. A verdict is withheld until the wardens run,
                        silence is not safety." `CageView.axaml:22` ends with "A capture on its own is
                        not a security verdict." These are good honesty strings doing exactly the
                        right job, which is why they must be reworded rather than deleted. macOS and
                        Windows have no equivalent occurrences, so this is also a three-way copy
                        divergence on the same panes. `ConclusionsView.axaml:25` and :53 are comments.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] PROPOSED wording, pending owner approval, not to be written into code
                        from this ledger: "No conclusion yet"; "No testimony has been gathered. A
                        conclusion is withheld until the wardens run. Silence is not safety."; "A
                        capture on its own is not a security conclusion."
                        [Fix 2] Rewrite the two comments to say "conclusion", no approval needed.
                        [Fix 3] Log all three approved strings to outputs-catalog.csv with file and
                        line, per AGENTS.md:64.
- **Code Sample:**      [

                        <!-- Linux/Ubuntu/SyxxUI.Avalonia/Views/DashboardView.axaml:249 -->
                        <TextBlock Text="No verdict yet" ...
                        <!-- :252 -->
                        Text="No testimony has been gathered. A verdict is withheld until the wardens run — silence is not safety."

                        ]
---

---
- **[I004]**            AGENTS.md states the prohibition without the replacements, which is why the rename drifted
- **Criticality:**      Non-critical, but it is the root cause of the inconsistent application
                        recorded in I001, I003, and I005.
- **Location:**         AGENTS.md:54; MEMORY.md (no occurrence of the rule)
- **Issue:**            AGENTS.md:54 reads in full: "STOP USING THE WORD 'VERDICT'. Erase it from any
                        apps and then erase it from your contexts." It names no replacement, draws no
                        distinction between code and user-facing text, and states no exception for
                        types that name user-facing surfaces. An agent told only to erase a word has
                        no basis to choose between "result" and "conclusion", and no basis to know
                        that `ConclusionsView` should be left alone. MEMORY.md, which exists to
                        restate directives that regress, does not mention the rule at all despite
                        containing 28 uses of the word. AGENTS.md is owner-only, so I have not
                        touched it.
- **Cross-ref. Delta:** Root cause of I001, I003, I005, I006.
- **Fix(es):**          [Fix 1] Owner to expand AGENTS.md:54 to state: code uses "result",
                        user-facing interfaces and documentation use "conclusion", and code naming a
                        user-facing surface may use "conclusion" (`ConclusionsView` is correct).
                        [Fix 2] Add the same rule to MEMORY.md's standing-directives block, which I
                        have done in this session.
                        [Fix 3] Record the signed-wire-field exception from I002 in the rule itself,
                        so the next agent to sweep does not break issued evidence.
- **Code Sample:**      [

                        AGENTS.md:54
                        - **STOP USING THE WORD "VERDICT"** Erase it from any apps and then erase it
                          from your contexts.

                        ]
---

---
- **[I005]**            The principle documents carry 116 uses of the word they prohibit
- **Criticality:**      Non-critical. Documents are the "documentation" case, so the target word is
                        "conclusion".
- **Location:**         DESIGN.md:139, 155, 222, 229, 233, 332 (6); ROADMAP.md (68); MEMORY.md (28); AGENTS.md (14)
- **Issue:**            Under the owner rule, documentation uses "conclusion". DESIGN.md carries 6
                        occurrences, ROADMAP.md 68, MEMORY.md 28, AGENTS.md 14. DESIGN.md's are the
                        highest-value to fix because it is the architecture document agents build
                        from, and two of them (:222, :229, :233) sit inside the section G002 retires
                        anyway. AGENTS.md is owner-only. This ledger deliberately retains the word,
                        as stated in its header.
- **Cross-ref. Delta:** I004, the missing rule statement.
- **Fix(es):**          [Fix 1] Sweep DESIGN.md's 6 to "conclusion", excluding those inside the
                        retired section, which should be marked retired rather than reworded.
                        [Fix 2] Leave ROADMAP.md and MEMORY.md historical entries alone. Rewriting a
                        dated session note falsifies the record. Apply the rule to new entries only,
                        and note that convention at the top of each file.
                        [Fix 3] Owner to sweep AGENTS.md's 14.
- **Code Sample:**      [

                        DESIGN.md:139  nil-on-throw = no manufactured verdict);
                        DESIGN.md:155  they gate verdicts
                        DESIGN.md:332  its output is a per-domain hostile-traffic verdict

                        ]
---

---
- **[I006]**            Correct exceptions: do not rename these
- **Criticality:**      Informational. Recorded so a future sweep does not "fix" correct code.
- **Location:**         macOS/Sources/SyxxApp/ConclusionsView.swift; macOS/Sources/SyxxKit/Synthesis/Conclusion.swift;
                        Windows/dotNET/SyxxUI/Views/ConclusionsView.xaml(.cs); Windows/dotNET/Syxx.Kit/Synthesis/Conclusion.cs;
                        Linux/Ubuntu/SyxxUI.Avalonia/Views/ConclusionsView.axaml(.cs); Linux/.../ViewModels/ConclusionsViewModel.cs
- **Issue:**            Per the owner's stated exception, code may use "conclusion" where it names a
                        user-facing surface. `ConclusionsView` on all three clients is correct because
                        it implements the user-facing Conclusion pane. `Conclusion.swift` and
                        `Conclusion.cs` are the model behind that pane and are also correct. Two
                        remaining macOS occurrences of "verdict" are in comments only
                        (`WitnessSigner.swift:266`, `SyxxCLI.swift:94`) and both use it descriptively
                        while stating an honesty rule, so they are low-priority comment edits, not
                        identifier renames. `Windows/dotNET/Syxx/Program.cs:93, 98` use it in
                        comments explaining that `--version` and `--help` are not results (E073),
                        which should become "result" per the code rule.
- **Cross-ref. Delta:** I001, I002.
- **Fix(es):**          [Fix 1] No action on the ConclusionsView and Conclusion families.
                        [Fix 2] Comment-only edits at WitnessSigner.swift:266, SyxxCLI.swift:94,
                        Program.cs:93, Program.cs:98.
- **Code Sample:**      [

                        // CORRECT, do not rename: implements the user-facing Conclusion pane
                        macOS/Sources/SyxxApp/ConclusionsView.swift
                        Windows/dotNET/SyxxUI/Views/ConclusionsView.xaml
                        Linux/Ubuntu/SyxxUI.Avalonia/Views/ConclusionsView.axaml

                        ]
---

## **J. Admitted Live Defect, Unfiled**

`DELTAS.md` is currently empty by design (post-cycle, per AGENTS.md:48). Everything in this domain is
a defect the principle documents admit or imply, which is still true in code, and which is therefore
tracked nowhere in the open ledger. These are candidates for the next cycle, not go-signals.

---
- **[J001]**            Dashboard Mirror chip renders PASSED on confirmed redirection
- **Criticality:**      CRITICAL. A false clean on the first surface the user sees, on the flagship
                        paid module. Two shipped surfaces read the same flag and disagree.
- **Location:**         macOS/Sources/SyxxApp/DashboardView.swift:357-362; contrast macOS/Sources/SyxxApp/MirrorView.swift:309
- **Issue:**            `DashboardView.wardenState` for the Mirror case checks `r.isInconclusive` then
                        `r.isDegraded` then returns `"PASSED"`. It never reads
                        `comparison.isRedirectionDetected`. `MirrorView.swift:309`, the detail pane,
                        returns `"TAMPERING DETECTED"` from that same flag, and
                        `Synthesizer.swift:288`, `SignedEvidencePayload.swift:214`, and
                        `SignedIncidentReport.swift:66` all consume it. So for a run where redirection
                        is confirmed but the result is neither degraded nor inconclusive, the Dashboard
                        says PASSED while the Mirror pane says TAMPERING DETECTED. The user is most
                        likely to see the Dashboard. This is a Verdict-and-Evidence-Integrity defect
                        (domain C) and would be auto-enhanced to Critical under DELTAS.md:179.
                        MEMORY.md:1473 and ROADMAP.md:1863 both admit a related pattern
                        (`wardenState` returning PASSED for any non-nil result) and ROADMAP.md:119
                        claims a 2026-07-29 fix for the Prophet branch, but the Mirror branch was not
                        fixed.
- **Cross-ref. Delta:** Untracked. Related to the retired PR-01 finding in ROADMAP.md:1863, whose
                        cited delta ID is dangling (see K003).
- **Fix(es):**          [Fix 1] Add `if r.comparison.isRedirectionDetected { return "TAMPERING
                        DETECTED" }` before the degraded check, matching MirrorView's precedence.
                        [Fix 2] Fold both surfaces onto one shared mapping so they cannot diverge
                        again. ROADMAP.md:1635 records the same class of problem for
                        `trustStatColor`, which is a second independent severity mapping.
                        [Fix 3] Add a test asserting Dashboard and MirrorView agree for every
                        ComparisonResult shape, and mutation-prove it by removing the new branch.
- **Code Sample:**      [

                        // macOS/Sources/SyxxApp/DashboardView.swift:357-362
                        case "Mirror":
                            guard model.isPro else { return "LOCKED · PRO" }
                            guard let r = model.mirrorResult else { return "NOT RUN" }
                            if r.isInconclusive { return "INCONCLUSIVE" }
                            if r.isDegraded { return "DEGRADED" }
                            return "PASSED"                    // <-- redirection never checked

                        // macOS/Sources/SyxxApp/MirrorView.swift:309, same data, opposite answer
                        if c.isRedirectionDetected { return "TAMPERING DETECTED" }

                        ]
---

---
- **[J002]**            The root Package.swift builds a privileged helper SMJobBless rejects, and the parity test cannot see it
- **Criticality:**      CRITICAL. Reintroduces D079. The guard written to prevent exactly this passes
                        while the defect is live, which is an AGENTS.md §3 litmus failure.
- **Location:**         Package.swift (DeepSyxx target, no `exclude:`, one `-sectcreate` pair);
                        macOS/Package.swift:89-94; macOS/Tests/SyxxKitTests/RootPackageParityTests.swift:80, 91, 103, 117
- **Issue:**            `macOS/Package.swift` embeds TWO sections into the DeepSyxx helper,
                        `__TEXT,__info_plist` and `__TEXT,__launchd_plist`, and its comment at :89-94
                        states that "omitting the embedded launchd plist makes the bless fail with
                        kSMErrorJobPlistNotFound (CFErrorDomainLaunchd 8). See D079." The new root
                        manifest omits the second `-sectcreate` pair and the `exclude:` block.
                        Verified by `otool`: the root-built binary has only `__info_plist`; the
                        macOS-built binary has both. The root manifest's own header describes itself
                        as a superset view and warns that a setting added to one and not the other
                        produces this exact failure, then commits it. `RootPackageParityTests.swift`
                        has zero occurrences of `linkerSettings`, `exclude`, or `sectcreate`; its four
                        tests compare only targets, dependencies, products, and path prefixes. So the
                        divergence exists now and the suite is green.
- **Cross-ref. Delta:** Untracked. D079 is recorded as fixed in the ledgers; this is a regression by a
                        new file rather than a reopening of the original.
- **Fix(es):**          [Fix 1] Copy the `exclude:` list and the second `-sectcreate` pair into the
                        root manifest, with the paths rebased to `macOS/Sources/DeepSyxx/`.
                        [Fix 2] Extend RootPackageParityTests to compare `linkerSettings` and
                        `exclude` verbatim, and prove it goes RED on the current tree BEFORE fixing
                        the manifest, so the test is shown to bite.
                        [Fix 3] Add a build-time assertion that the shipped DeepSyxx binary contains
                        `__launchd_plist`, since a manifest comparison is a proxy for the property
                        that actually matters.
- **Code Sample:**      [

                        $ otool -l .build/debug/DeepSyxx | grep sectname | grep plist
                          sectname __info_plist                      # root manifest: ONE section

                        $ otool -l macOS/.build/debug/DeepSyxx | grep sectname | grep plist
                          sectname __info_plist
                          sectname __launchd_plist                   # macOS manifest: both

                        $ grep -c 'linkerSettings\|exclude\|sectcreate' macOS/Tests/SyxxKitTests/RootPackageParityTests.swift
                        0

                        ]
---

---
- **[J003]**            Collector pinning and the witness trust set are inert in every shipped call path
- **Criticality:**      Critical, auto-enhanced per DELTAS.md:162 (reliance on forgeable evidence).
- **Location:**         ROADMAP.md:171, 124, 1575-1577, 1000-1003, 1906, 1909; macOS/Sources/SyxxApp/AppModel.swift (runCollectorTest call)
- **Issue:**            ROADMAP.md records D003 and D011 as done while stating in three separate
                        places that no production caller supplies `trustedPublicKeys`, that
                        `.untrustedSigner` is therefore unreachable, and that "a snapshot signed by
                        any attacker-chosen key renders VALID (delta B081)". ROADMAP.md:1001 adds that
                        until the owner supplies pin values, "transport is transparently unpinned
                        (posture `notConfigured`)". So the signature proves integrity but not
                        provenance: an attacker who substitutes their own keypair produces a snapshot
                        the shipped app calls valid. `WitnessSigner.swift:106` defines the
                        `.untrustedSigner` state, so the Kit is correct and only the wiring is
                        missing. This is the gap between a done ledger row and a working product.
- **Cross-ref. Delta:** Untracked as an open delta. B081 is referenced by ROADMAP.md but DELTAS.md is
                        empty, so nothing tracks it.
- **Fix(es):**          [Fix 1] Thread `trustedPublicKeys` and `pinSet` from the macOS GUI into
                        `runCollectorTest`, which ROADMAP.md:1575-1577 already identifies as the owed
                        change (WS7-11).
                        [Fix 2] Make the absence loud: with no trust set configured, the surface must
                        read inconclusive or "provenance unverified", never VALID.
                        [Fix 3] Owner to supply the collector SPKI pin values, which ROADMAP.md:1000
                        marks an owner deliverable.
- **Code Sample:**      [

                        ROADMAP.md:171
                        F-1  Signed evidence  — OVERCLAIMED. No caller supplies `trustedPublicKeys`;
                        `.untrustedSigner` UNREACHABLE; a snapshot signed by any attacker-chosen key
                        renders VALID (delta B081). Signature proves integrity, not provenance.

                        ]
---

---
- **[J004]**            19 quarantined test files are staged as added but deleted on disk
- **Criticality:**      Non-critical, but a commit lands 19 invisible test files, several of which
                        duplicate the names of live tests.
- **Location:**         macOS/Tests/_quarantine/ (19 entries, `AD` in git status, 0 files present)
- **Issue:**            `git status` reports 19 files in `macOS/Tests/_quarantine/` as `AD`: present in
                        the index, absent from the working tree. Committing the current index lands
                        19 test files nobody can see locally. Several are name-duplicates of live
                        tests, including `SynthesisEngineTests.swift`, `SynthesizerTests.swift`,
                        `Stage5PresentationTests.swift`, and `RoundingParityTests.swift`. They sit
                        outside the `SyxxKitTests` target path so they do not compile today, but they
                        become redeclaration errors the moment a target root or path changes. Note
                        several tests MEMORY.md still describes as live defects may be among these,
                        which would make those MEMORY.md entries stale rather than actionable.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Decide restore or drop. If dropped, `git rm --cached` the 19.
                        [Fix 2] If restored, move them under a path the test target excludes
                        deliberately, and record why each was quarantined.
                        [Fix 3] Re-check MEMORY.md's cannot-fail-test claims against this list, since
                        some may already be gone.
- **Code Sample:**      [

                        $ git status --porcelain macOS/Tests/_quarantine | head -3
                        AD macOS/Tests/_quarantine/EncryptedDNSConfigTests.swift
                        AD macOS/Tests/_quarantine/FirstRunScanTests.swift
                        AD macOS/Tests/_quarantine/FusionRuleBakeoffTests.swift

                        $ ls macOS/Tests/_quarantine | wc -l
                        0

                        ]
---

---
- **[J005]**            The Linux build entry point cannot load
- **Criticality:**      Non-critical to detection, blocking for the first-ship platform. ROADMAP.md:20
                        makes Linux ship first.
- **Location:**         Windows/Syxx.Linux.slnf; Windows/Syxx.sln
- **Issue:**            `Syxx.Linux.slnf` lists five projects, two of which
                        (`..\Linux\Ubuntu\SyxxUI.Avalonia\SyxxUI.Avalonia.csproj` and its
                        `.Tests` sibling) are not members of `Syxx.sln`, which contains only
                        `Syxx.Kit`, `Syxx`, `Syxx.Tests`, `SyxxUI`, and `SyxxUI.Tests`. A solution
                        filter requires membership in its parent solution, so the filter fails with
                        MSB5028 and the whole Linux build entry point is unusable. ROADMAP.md:37-44
                        states Linux ships first and carries the largest verification debt; an
                        unloadable solution filter means the CI leg added on 2026-07-27 cannot be
                        driven from it.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add both Avalonia projects to `Windows/Syxx.sln`.
                        [Fix 2] Reconsider the layout. A Linux solution filter living under
                        `Windows/` and reaching up into `Linux/` is why this broke; a
                        `Linux/Syxx.Linux.sln` of its own would not have this failure mode.
- **Code Sample:**      [

                        MSB5028: Solution filter ... includes project
                        "../Linux/Ubuntu/SyxxUI.Avalonia/SyxxUI.Avalonia.csproj"
                        that is not in the solution file at ".../Windows/Syxx.sln"

                        ]
---

---
- **[J006]**            Alert delivery failure is evictable-only on macOS and swallowed on Windows
- **Criticality:**      High for honesty, zero for runtime. The user is silently never alerted while
                        monitoring appears armed, the same false-assurance class as J001.
- **Location:**         macOS/Sources/SyxxApp/AppModel.swift:148-162, 226-235, 199, 220;
                        macOS/Sources/SyxxApp/SyxxApp.swift:284-288;
                        Windows/dotNET/SyxxUI/ViewModels/AppViewModel.cs:146-157, 151
- **Issue:**            B247 (the monitor alert path) records a delivery failure only as an evictable
                        dashboard-log line, contradicting the documented invariant at
                        AppModel.swift:148-152. That comment defines the log as "fed by `didSet` on
                        those properties" (the output and Unbound channels) and "a display bound, not
                        a data source." The failure call at AppModel.swift:231 is the only feeder not
                        driven by `didSet`, so the log is now both mirror and origin. It is also the
                        only record: nothing persists it, nothing else surfaces it, and 200 subsequent
                        lines evict it. Authorization failures get a durable tri-state field
                        (`notificationAuthorized`, :199) and a banner (SyxxApp.swift:284); delivery
                        failures get neither. Windows is strictly worse: AppViewModel.cs:156 catches
                        and discards the toast error outright, so the user is never told. Secondary:
                        both notification titles carry a unicode-escape em-dash (AppModel.swift:220
                        `\u{2014}`, AppViewModel.cs:151 `\u2014`), invisible to the plain U+2014 grep
                        in G004, violating AGENTS.md:52 in shipped user-visible output.
- **Cross-ref. Delta:** None. Related J001 (false-assurance class), G004 (em-dash count missed the
                        unicode escapes), and the B247 monitor work. Promote to DELTAS.md when the
                        evaluation cycle opens.
- **Fix(es):**          [Fix 1] Add a durable tri-state `NotificationDeliveryState` (.undetermined /
                        .delivered / .failed(String)) as @Published with a didSet that feeds the log
                        only on real transitions (into .failed, and .failed to .delivered recovery),
                        restoring the invariant's mirror mechanism. [Fix 2] Set it from the add()
                        completion at AppModel.swift:228-234. [Fix 3] Surface a banner beside
                        SyxxApp.swift:284 when monitoring is on and delivery is .failed (copy-gated).
                        [Fix 4] Windows twin: replace the bare catch at AppViewModel.cs:156 with the
                        same tri-state and a tray surface, preserving parity. [Fix 5] Replace both
                        em-dash titles (copy-gated).
- **Code Sample:**      [

                        // macOS, AppModel.swift:231 - the only feeder not driven by didSet,
                        // contradicting the invariant at :148-152:
                        self?.appendDashboardFeedback("[monitor] notification delivery failed: ...")

                        // Windows, AppViewModel.cs:156 - delivery failure swallowed outright:
                        catch { /* notification failure must not crash the monitor loop */ }

                        ]
---

## **K. Structural / Navigational**

---
- **[K001]**            MEMORY.md's tail is chronologically scrambled, hiding the governing plan at the bottom
- **Criticality:**      High for agent behavior, zero for runtime. An agent reading top-down absorbs
                        stale content and misses the entry that controls current work.
- **Location:**         MEMORY.md:1467-1547 (the 2026-07-24 entry, at the file's end)
- **Issue:**            MEMORY.md runs reverse-chronologically from the top (newest first) down to
                        roughly line 1170, then the tail runs FORWARD in time: 07-09, 07-10, 07-16,
                        07-17, and finally the 2026-07-24 entry at the very bottom. That last entry is
                        the second-newest substantive section in the file and contains the macOS
                        release-candidate plan plus the owner corrections that govern current work,
                        including "Do not restore the former MFAE engine" and the instruction that
                        "the next implementation agent should use the plan rather than reconstructing
                        tasks from the audit." An agent that reads the first 200 lines and stops, which
                        is the rational strategy for a 1,547-line file, misses it entirely.
                        Additionally 2026-07-11 appears twice, 07-13 twice, and 07-15 five times, at
                        non-adjacent positions.
- **Cross-ref. Delta:** K002, the same defect in CHANGELOG.md.
- **Fix(es):**          [Fix 1] Move the 2026-07-24 entry to its correct reverse-chronological
                        position near the top.
                        [Fix 2] Add a one-line reading order note under the title stating the file is
                        newest-first and flagging any remaining out-of-order block.
                        [Fix 3] Consolidate the duplicate dates so each date appears once.
- **Code Sample:**      [

                        MEMORY.md structure:
                          1-35     standing directives
                          37-...   reverse chronological, newest first
                          ~1170+   FORWARD chronological tail
                          1467+    2026-07-24 macOS RC plan  <-- second-newest, at the very bottom

                        ]
---

---
- **[K002]**            CHANGELOG.md hides three recent entries below much older content
- **Criticality:**      Medium. The hidden entries are the status of the largest unfinished work
                        program in the log.
- **Location:**         CHANGELOG.md:3559, 3569, 3587 (2026-07-23 and 07-24) versus CHANGELOG.md:5 (2026-07-30)
- **Issue:**            CHANGELOG.md is newest-first from line 5 (2026-07-30) down to roughly 3443,
                        then an appended tail runs forward and ends with three entries dated
                        2026-07-23 and 07-24 at lines 3559, 3569, and 3587. Those three record the
                        WS8 code-health and WS9 UI-parity sweeps, including that Phase 1 found 14
                        macOS findings and 11 Warden token mismatches across all three platforms, and
                        that "No Phase 2 work has begun yet". A newest-first reader misses the entire
                        status of both sweeps. The 07-24 entry additionally has blank agent names (", ,
                        and  have all reported"), which is text corruption.
- **Cross-ref. Delta:** K001.
- **Fix(es):**          [Fix 1] Move the three entries to their correct positions.
                        [Fix 2] Repair the corrupted agent-name line at CHANGELOG.md:3587.
                        [Fix 3] Record WS8 and WS9 as open in ROADMAP.md, since their status is
                        currently discoverable only from the bottom of a 3,595-line file.
- **Code Sample:**      [

                        CHANGELOG.md:5     ## ... (2026-07-30)      <-- newest, at top
                        CHANGELOG.md:3587  ## 2026-07-24 ...        <-- recent, at the very bottom
                                           ", , and  have all reported"   <-- corrupted names

                        ]
---

---
- **[K003]**            ROADMAP.md's 20-item publication-blocker table cites delta IDs that describe different defects or do not exist
- **Criticality:**      High. Twenty findings are marked open with no traceable backing, and the file
                        gives no way to tell whether they were never filed or already fixed.
- **Location:**         ROADMAP.md:1857-1891 (PR-01 through PR-20); DELTAS.md (empty); DELTAS-fixed.md
- **Issue:**            The PR table's legend at ROADMAP.md:1857 reads "already in DELTAS.md (see delta
                        number)" and its rows cite B192, B193, B194, B195, A136, A137, D113, and D114.
                        `DELTAS.md` currently contains no deltas at all, so every pointer dangles.
                        Worse, the IDs that do exist in `DELTAS-fixed.md` describe unrelated defects:
                        B192 there is a Windows ECS leak-signal defect, not PR-01's Dashboard
                        first-render issue; B193 is a resolver-latency mapping defect, not PR-02's
                        `runAllWardens()`. D113 and D114 do not appear as delta headers anywhere. The
                        likely cause is the Monday renumbering rule at DELTAS.md:57 reusing numbers
                        that ROADMAP.md had reserved as "(new)". The table also skips PR-12 entirely.
- **Cross-ref. Delta:** J001 is the live confirmation of the PR-01 defect class, filed here because
                        the PR row's own pointer is unusable.
- **Fix(es):**          [Fix 1] Re-verify all 20 rows against source and either file them fresh with
                        current numbers or mark them closed with evidence. They cannot stay as they
                        are, because they are neither trustworthy as open nor as closed.
                        [Fix 2] Stop citing not-yet-assigned delta numbers in ROADMAP.md. Reference by
                        `file:line` plus a short name until a number is actually allocated.
                        [Fix 3] Restore or explain the missing PR-12.
- **Code Sample:**      [

                        ROADMAP.md:1857  legend: "❌ already in DELTAS.md (see delta number)"
                        ROADMAP.md:1863  PR-01 ... B192 (new)

                        DELTAS.md         contains zero deltas
                        DELTAS-fixed.md   B192 = "Windows Glutton Still Drops the ECS Leak Signal"
                                                 (unrelated to PR-01)

                        ]
---

## Recommended order, should these be slated

Grouped so that each step makes the next one verifiable, per AGENTS.md's planning standard.

1. **J001** alone, first. It is a false clean on the highest-traffic GUI surface, the fix is four
   lines, and it needs no decision from anyone. Fold both severity mappings onto one source while
   there.
2. **J002**, and write the failing parity test BEFORE the manifest fix so the test is proven to bite.
   This is the one item where a green suite is currently actively misleading.
3. **G001, G002, G003, H002** as a single DESIGN.md and MEMORY.md accuracy pass. They are the same
   root cause (the matrix migration outran the documents) and fixing them separately would mean
   reading the same code three times.
4. **I004 and I005** next, because the terminology rule must be written down correctly before anyone
   sweeps anything. I004 is owner-only.
5. **I001** after I004. One file, no consumers, zero runtime risk. Also answer why the A1 layer has
   no Windows caller, which is the more serious finding hiding underneath the naming issue.
6. **I003** after owner copy approval. Three strings, one platform.
7. **I002 never on its own.** It rides the E020 formatVersion v2 to v3 flip, in one commit, on both
   platforms, with golden vectors in both versions. Renaming that field in a terminology sweep breaks
   every issued snapshot.
8. **K001, K002, K003** as a documents-hygiene pass. Cheap, and K003 will likely reveal that some of
   the 20 PR rows are already fixed, which shortens the real backlog.
9. **G004, G005, G006, G007, G008, H001, H003, H004, H005, J004, J005** as cleanup. J005 rises to the
   top of this group if Linux still ships first.
10. **J006** after owner copy approval of its wording (the delivery banner, the recovery log line,
    and the two em-dash notification titles). macOS is one property, one didSet, and one banner; the
    Windows twin replaces the bare catch at AppViewModel.cs:156 with the same tri-state and a tray
    surface in the same pass. Promote to DELTAS.md when the evaluation cycle opens.

**Not addressed here:** none of the audit's original compile-error items remain open. The 13 macOS
GUI and 2 Linux GUI compile errors were resolved by working-tree edits made after this audit
(AppModel.swift and SyxxApp.swift, Jul 30 20:49, uncommitted; `swift build` exits 0). The
"documented invariant to either honor or amend" item is now J006: the working-tree fix kept the
direct `appendDashboardFeedback` call, leaving the mirror claim at AppModel.swift:148-152 untrue,
and J006 tracks the durable fix and its copy-gated wording.
