# Syxx Docs Deltas

Document-level defect audit — a companion ledger to `DELTAS.md`. Where `DELTAS.md` tracks
defects in **code**, this ledger tracks defects in the **design-related documents and
artifacts**: descriptions of features (proposed or implemented), programmatics, and code
shown in `DESIGN.md`, `MEMORY.md`, `ROADMAP.md`, `PROPOSALS.md`, `PROJECT-STATE.md`, the
engine / Stage-5 / CL specs, GUI plans, handoffs, skills, and the server design docs.

A **docs-delta** is a passage in a document that is **risky, harmful, ineffective,
inefficient, or unnecessarily overcomplicated** (relative to an equally or more effective
simpler alternative). This includes: a proposed feature that is structurally unable to catch
the tampering/censorship it targets; a described scheme that is insecure but presented as
safe; a programmatic that yields false confidence; a design contradicted by source or by
other docs; stale claims that mislead implementers; or an over-engineered approach with a
simpler, equally effective alternative.

## Document Delta Analysis Instructions

When asked to perform `document delta analysis`, systematically audit the design-related
documents to identify descriptions that prevent Syxx from meeting its mission:
**investigating and detecting DNS tampering, hijacking, spoofing, and censorship**, and
producing evidence an end user can actually trust.

### Scope & Approach

**Read the design-related documents** — `DESIGN.md`, `MEMORY.md`, `ROADMAP.md`,
`PROPOSALS.md`, `PROJECT-STATE.md`, the engine / Stage-5 / CL specs, GUI plans, handoffs,
skills, and server design docs. Focus on document passages that:

- Describe verdict logic (secure/tampered/inconclusive) or how confidence is computed
- Describe evidence generation, signing, or attestation
- Prescribe credential, key, or authentication handling
- Describe platform-specific behavior (Darwin/Win32) or programmatics
- Present proposed features, architectures, or code snippets

Do not try to read every document line-by-line — instead, **trace the claims**:

1. Start from `DESIGN.md` (the functional map) and follow each feature to its spec
2. Read the engine/synthesis/aggregation design (`REASONING-SYSTEM-DESIGN.md`, Stage-5 / CL specs)
3. Audit security-critical descriptions (LicenseManager, DNSSECVerifier, WitnessSigner, key handling)
4. Compare macOS vs Windows *described* behavior for divergence
5. Review server endpoint / collector / proxy descriptions for security, efficacy, efficiency
6. Cross-check described detection methods against `research/reference-library/` and
   authoritative sources (RFC/IETF, published research) — a claimed method that is
   known-broken or ineffective is a delta. Use web search and the reference library before
   asserting a method is unsound.

### Technical Domains

All docs-deltas must be filed under exactly one of the following six technical domains:

A. **Server Components:** Docs-deltas concerning the Python server (`Server/`) — risky,
harmful, ineffective, or inefficient *descriptions* of the DNS-leak collector,
content-integrity proxy, collector-attested evidence endpoints, or server programmatics.

B. **Detection Efficacy:** Docs-deltas describing missed detections, false negatives/
positives, or structural inability to catch the tampering/censorship targeted. Includes
proposed features that fail to run, probes described as returning wrong results, or
comparisons described as using attacker-controlled baselines.

C. **Verdict & Evidence Integrity:** Docs-deltas that describe false "secure/clean" or
false "tampered" verdicts, forgeable/untrustworthy evidence presented as sound, suppressed
referrals, or violations of the Honesty Protocol (§1 no-verdict-before-evidence, §3
zero-data confidence, §4 mocked output). This is the highest-priority category — any doc
passage that can cause false confidence belongs here.

D. **Platform Integration & Resource Safety:** Docs-deltas involving OS-specific APIs
(Darwin/Win32), interface/privilege handling, resource/footprint claims, crashes-on-hostile-
input, or macOS↔Windows *described* divergence.

E. **Security & Hardening:** Docs-deltas involving credential management, HMAC secrets, key
handling, authentication, input-validation, or exploitable attack surface *as described*.

F. **Miscellaneous:** Documentation errors, typos, non-efficacy perf, or deltas that
clearly don't fit 1–5. Use sparingly; if more than 10% of your docs-deltas are filed here,
you're likely miscategorizing and should ask the user for guidance.

### Severity Ordering (for multi-domain docs-deltas)

If a docs-delta spans multiple domains, file it under the **most severe** applicable domain.
The order of severity is:

1. **Verdict & Evidence Integrity** (highest priority)
2. **Server Components**
3. **Detection Efficacy**
4. **Security & Hardening**
5. **Platform Integration**
6. **Miscellaneous**

### Delta Numbers

When recording identified docs-deltas, take its sequence order (3-digit format) and append it
to its domain code. For instance, a docs-delta that would be the fourth listed in the Server
Components domain would be numbered A004. Each Monday, renumber docs-deltas sequentially
within each domain to fill gaps left by fixed or archived entries. Mark any docs-delta that
should not be renumbered by appending `HOLD -` followed by your tool name inside brackets
above the number — e.g., `[HOLD - OPENCODE]`. Brackets cause XCode to set the text off in
red for the user.

Priority can be inferred from numbering: A- and B-numbered docs-deltas are highest priority;
E- and F-numbered are lowest.

### Template

Report findings in `DOCSDELTAS.md` format:

---

- **[Delta Number]**    [Delta Short Description/Name]
- **Criticality:**      Indicate `Critical / Non-critical`; whether the docs-delta is critical and user was made aware.
- **Location:**         Document and line numbers (e.g., `DESIGN.md:78-84`)
- **Issue:**            Clear description of what's wrong and why it matters
- **Cross-ref. Delta:** Note if this is already tracked (e.g., "Partially covered by DSN-002 but not the specific gap"), or cross-ref a `DELTAS.md` code delta this document describes.
- **Fix(es):**          Brief suggestion for how to fix (not full implementation)

- **Document Sample:**  [

                        Paste the defective passage verbatim from the document.

                        ]

---

In the location field, separate multiple lines within one document with a comma
(e.g., `DESIGN.md:78-84, 123-127`); separate multiple documents with a semicolon
(e.g., `DESIGN.md:78-84; PROPOSALS.md:46-52`). No location, no docs-delta.

Each `[Fix N]` gets its own line. **Document Sample** opens with `[` on the label line,
then a blank line, then the verbatim passage indented 24 spaces, then a blank line, then `]`
at column 24. Never word-wrap the sample — only indent it. Wrap each docs-delta in `---`
delimiters.

If alignment is wrong, run `python3 scripts/reflow-deltas.py DOCSDELTAS.md` (it re-indents
every field to column 24 and touches only the A–F ledger).

### Commenting Template

When evaluating docs-deltas, comment inline immediately after the referenced docs-delta but
within the `---` wrap delimiters, using these row labels (labels at column 24, text at
column 48):

                       - **Tool Name**            [syxx-neural-researcher]
                       - **Date**                 2026-07-13
                       - **Agree / Disagree**     Disagree
                       - **Disagree Basis**       ...
                       - **Proposed Revisions**   ...
                       - **Proposed Document**    [

                                                  <revised passage>

                                                  ]

### Criticality

**Automatic criticality enhancement (document framing):**

1. Any docs-delta describing silent fallback to a public resolver when the system resolver is
   undetectable is automatically enhanced to Critical.
2. Any docs-delta relying on / presenting forgeable evidence as trustworthy is automatically
   enhanced to Critical.
3. Any docs-delta describing a server endpoint that doesn't authenticate requests (anyone can
   submit fake evidence) is automatically enhanced to Critical.
4. Any docs-delta describing a scheme that yields a false "secure/clean" or false
   "tampered" verdict, or violates the Honesty Protocol, is automatically enhanced to Critical.
5. Any docs-delta that silently hides a failure or prescribes a false-confidence programmatic
   is automatically enhanced to Critical.

If a docs-delta ought to be Critical but does not fit these categories, it too is enhanced to
Critical. If unsure whether a passage is a defect or intentional design, ask before logging.

## **A. Server Components**

---
---

## **B. Detection Efficacy**

---
---

## **C. Verdict & Evidence Integrity**

---
---

---

- **[C001]**            ROADMAP C006 remediation ships a false-clean verdict on missing data
- **Criticality:**      Critical; user made aware? No.
- **Location:**         ROADMAP.md:110-111, 305-307
- **Issue:**            The C006 fix (eliminate the silent 8.8.8.8 fallback) concludes by having
                        `ContentWarp` return `true` ("assume public/unmodified") **when no resolver
                        is available**. This replaces a banned fallback with a new silent false-clean
                        branch: missing/unmeasurable data reported as "secure." Violates the Honesty
                        Protocol ("missing data is not clean data"; no verdict before evidence). This is
                        documented *planned* behavior — evidence someone specified bad (over-confident) code.
- **Cross-ref. Delta:** Same false-"secure" class as DELTAS.md C; this is the design instruction that produces it.
- **Fix(es):**          [Fix 1] When no resolver is available, return inconclusive — never `true`.
                        [Fix 2] Specify the resolver-availability failure path as an inconclusive trigger, not a clean verdict.
- **Document Sample:**  [

                        ROADMAP.md:305-307 — "...when no resolver available, ContentWarp returns
                        true (assume public/unmodified)..."

                        ]

---

- **[C002]**            GUI plan specifies a binary "Suspicious / Clean" verdict tab
- **Criticality:**      Critical; user made aware? No.
- **Location:**         GUI_PLAN.md:408
- **Issue:**            The Conclusion screen is specified with a two-position tab switcher
                        "Suspicious / Clean" — a boolean verdict UI that violates the project's own
                        "tri-state, never boolean" rule and lets the app present a "Clean" affordance as a
                        verdict. A user can read the highlighted "Clean" tab as "you are safe." Evidence of
                        planned over-confident UI.
- **Cross-ref. Delta:** Analogous to the B/C false-"secure" class in DELTAS.md.
- **Fix(es):**          [Fix 1] Replace the binary tab with a tri-state surface (secure / tampered /
                        inconclusive); never render "Clean" as a verdict position.
- **Document Sample:**  [

                        GUI_PLAN.md:408 — "Add: tab switcher that toggles between Suspicious / Clean"

                        ]

---

- **[C003]**            GUI plan maps `.notDetected` to a green "✅ No Leak"
- **Criticality:**      Critical; user made aware? No.
- **Location:**         GUI_PLAN.md:322-323
- **Issue:**            The leak result `notDetected` (which can arise from a timeout or degraded run,
                        not only a verified-clean one) is specified to render as success-green "✅ No
                        Leak." Unverified/absent data is presented as a secure outcome — unearned "secure"
                        on unverified input.
- **Cross-ref. Delta:** Same false-"secure" class as DELTAS.md C / Glutton deltas.
- **Fix(es):**          [Fix 1] Map `notDetected` with no contradiction/timeout to inconclusive-green,
                        not success-green; reserve green only for verified-clean with full evidence.
- **Document Sample:**  [

                        GUI_PLAN.md:322-323 — "notDetected → ✅ No Leak (green)"

                        ]

---

- **[C004]**            GUI plan specifies a green "Bundle signed" lock with no key identity
- **Criticality:**      Critical; user made aware? No.
- **Location:**         GUI_PLAN.md (Bundle signed badge)
- **Issue:**            Prescribes a green trust lock indicating "Bundle signed" with no indication of
                        *which* key signed it. Self-attestation presented as authority — a green signal
                        with no trust anchor, the forgeable-evidence pattern the Honesty Protocol forbids.
- **Cross-ref. Delta:** Witness/keygen trust-anchor deltas in DELTAS.md (D011, E045, D066).
- **Fix(es):**          [Fix 1] Show the signing identity / key id, not just a green lock; treat an
                        unverifiable or self-signed bundle as inconclusive, never "trusted."
- **Document Sample:**  [

                        GUI_PLAN.md — "Bundle signed" green lock badge (no key identity shown)

                        ]

---

- **[C005]**            GUI plan renders unsigned server MOTD inside trusted app chrome
- **Criticality:**      Critical; user made aware? No.
- **Location:**         GUI_PLAN.md (MOTD / broadcast in-app WebView)
- **Issue:**            Specifies loading the server-supplied (unsigned, MITM-able) broadcast/MOTD into
                        the app's trusted WebView surface, presented as authoritative Syxx messaging. A
                        network attacker can substitute content; the design treats unauthenticated remote
                        content as trusted app output.
- **Cross-ref. Delta:** D041 / D037 / D033 / D034 (MOTD/WebView) in DELTAS.md.
- **Fix(es):**          [Fix 1] Render MOTD as clearly untrusted, operator-authored text; sign it and pin
                        the key, or move it out of the trusted verdict chrome.
- **Document Sample:**  [

                        GUI_PLAN.md — "load broadcast/MOTD into the in-app WebView (trusted surface)"

                        ]

---

- **[C006]**            Docs claim done/verdict-ready state while production is unimplemented
- **Criticality:**      Non-critical (documentation accuracy); user made aware? No.
- **Location:**         ROADMAP.md (C006/B024 marked done vs open in DELTAS.md); STAGE5-CONTRACT.md /
                        ENGINE-PHASE01-PLAN.md (stage5 = nil in production); GUI_PLAN.md (verdict surface)
- **Issue:**            Several docs assert completion / verdict-readiness the live code does not provide.
                        STAGE5-CONTRACT.md and ENGINE-PHASE01-PLAN.md state the Stage-5 synthesis object is
                        `nil` in the production build, yet GUI_PLAN.md builds a verdict surface against it
                        and ROADMAP.md marks related items "[done]." A reader concludes Syxx ships a working
                        verdict pipeline when none exists in production — misrepresented security posture.
- **Cross-ref. Delta:** Open C/B deltas in DELTAS.md contradict the "done" claims.
- **Fix(es):**          [Fix 1] Reconcile ROADMAP "[done]" items with open DELTAS.md state.
                        [Fix 2] Mark Stage-5 "implemented" only when the production object is populated; note
                        the GUI verdict surface is non-functional until then.
- **Document Sample:**  [

                        STAGE5-CONTRACT.md / ENGINE-PHASE01-PLAN.md — "stage5 = nil in production build"
                        vs GUI_PLAN.md verdict surface and ROADMAP.md "[done]" markers.

                        ]

---

## **D. Platform Integration & Resource Safety**

---
---

## **E. Security & Hardening**

---
---

## **F. Miscellaneous**

---
---
