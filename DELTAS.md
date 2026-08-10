# Syxx Deltas

Comprehensive source-grounded defect audit. A **delta** is a bug that impairs the app's mission
to **investigate and detect DNS tampering, hijacking, spoofing, and censorship** — i.e. anything
that causes a missed detection, a false "secure/clear" verdict, a false positive, a crash/hang on
hostile input, or unreliable/forgeable evidence.

## Delta Analysis Instructions

When asked to perform `delta analysis`, systematically audit the macOS, Windows, and Server codebases to identify defects or omissions that prevent Syxx from meeting its mission: **investigating and detecting DNS tampering, hijacking, spoofing, and censorship**, and producing evidence an end user can actually trust.

### Scope & Approach

**Read the entire codebase** — macOS client (`macOS/Sources`), Windows client (`Windows/`), and server (`Server/`). Focus on code paths that:

- Produce verdicts (secure/tampered/inconclusive)
- Generate or sign evidence
- Handle credentials, keys, or authentication
- Make platform-specific API calls (Darwin/Win32)
- Process untrusted input (DNS packets, HTML, subprocess arguments)

Do not try to read every file line-by-line — instead, **trace critical paths**:

1. Start at CLI entry points (`SyxxCLI.swift`, `Program.cs`) and follow to verdict synthesis
2. Read all synthesis/aggregation logic (`SynthesisEngine.swift`, `Synthesizer.cs`, `Synthesizer.swift`)
3. Audit all security-critical modules (LicenseManager, DNSSECVerifier, WitnessSigner, NetworkKeyState)
4. Compare macOS vs Windows implementations for behavioral divergence
5. Review server endpoints for security, efficacy, and efficiency

### Technical Domains

All deltas must be filed under exactly one of the following six technical domains:

A. **Server Components:** Deltas arising from the Python server (`Server/`) — risky, harmful, ineffective, or inefficient server code, including the DNS-leak collector, content-integrity proxy, or collector-attested evidence endpoints.

B. **Detection Efficacy:** Deltas that cause missed detections, false negatives, false positives, or structural inability to catch the tampering/censorship they target. Includes tools that fail to run at all, probes that return wrong results, or comparisons that use attacker-controlled baselines.

C. **Verdict & Evidence Integrity:** Deltas that produce false "secure/clean" or false "tampered/malicious" verdicts, forgeable evidence, suppress warranted Reddit referrals, or violate the Honesty Protocol (§1 no-verdict-before-evidence, §3 zero-data confidence, §4 mocked output). This is the highest-priority category — any delta that can cause false confidence belongs here.

D. **Platform Integration & Resource Safety:** Deltas involving OS-specific APIs (Darwin/Win32), interface detection, privilege handling, resource leaks, crashes on hostile input, or failures to ensure platform parity (macOS vs Windows behavioral divergence).

E. **Security & Hardening:** Deltas involving credential management, HMAC secrets, key handling, authentication bypass, input validation, fuzzing failures, or exploitable attack surface.

F. **Miscellaneous:** Documentation errors, typos, performance optimizations that don't affect efficacy, or deltas that clearly don't fit categories 1–5. This category should be used sparingly. **If more than 10% of your deltas are filed here, you're likely miscategorizing and need to ask the user for guidance.**

### Severity Ordering (for multi-domain deltas)

If a delta spans multiple domains, file it under the **most severe** applicable domain. The order of severity is:

1. **Verdict & Evidence Integrity** (highest priority for client code)
2. **Server Components** (separate pipeline, always severe)
3. **Detection Efficacy**
4. **Security & Hardening**
5. **Platform Integration**
6. **Miscellaneous**

### Delta Numbers

When recording identified delties, take its sequence order (3-digit format) and then append it to its domain code. For instance, a delta that would be the fourth listed in the Server Components domain would be numbered A004. A delta involving Verdict and Evidence Integrity that would be the 127th recorded delta in its domain would be number B127. Each Monday, renumber deltas sequentially within each domain to fill gaps left by fixed or archived deltas. For example, if A003 and A007 are fixed, the former A008 becomes the new A003, A009 becomes A004, etc. Update all cross-references accordingly. Mark any deltas that should not be renumbered by appending `HOLD -` followed by your tool name inside of brackets above the delta number--e.g., `[HOLD - OPENCODE]` or `[HOLD - CLAUDE]`. Brackets cause XCode to set text    off in red so it is easily readable to the user.

Priority can be inferred from delta numbering, whereas A- and B-numbered deltas are the highest priority and E- and F-numbered deltas are the lowest priority.

### Template

Report findings in `DELTAS.md` format:

---

- **[E035]**            Telemetry consent toggles persist settings but never stop or start
                        the live exporters
- **Status:**           **OPEN** — Found during the completed five-agent audit
                        2026-08-09.
- **Criticality:**      Critical. Users can turn telemetry on or off in settings and the
                        preference is saved, but the running exporter state keeps whatever
                        it had at startup.
- **Location:**         Windows/dotNET/SyxxUI/Views/SettingsView.xaml.cs:108-117;
                        Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/SettingsViewModel.cs:70-90;
                        Linux/Ubuntu/SyxxUI.Avalonia/Services/LinuxReadyServices.cs:16-53
- **Issue:**            The Windows and Linux settings handlers only mutate settings and
                        call `Persist()`. `LinuxReadyServices.Start()` applies telemetry only
                        once at launch, and there is no corresponding stop/start path when
                        the user changes the consent toggle later. The UI therefore records
                        the new preference while the process keeps emitting according to the
                        old one.
- **Cross-ref. Delta:** D097 (Windows GUI bootstrap path) and D096 (Linux sink path),
                        but this issue is the settings-to-runtime disconnect itself.
- **Fix(es):**          [Fix 1] Restart or reconfigure the live exporter when telemetry
                        consent changes. [Fix 2] Make the settings UI call into a runtime
                        apply hook, not just `Persist()`.
- **Code Sample:**      [

                        // Windows/dotNET/SyxxUI/Views/SettingsView.xaml.cs:108-117
                        _settings.TelemetryEnabled = TelemetryToggle.IsOn; Persist();

                        // Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/SettingsViewModel.cs:70-90
                        _settings.TelemetryEnabled = TelemetryEnabled;
                        SaveNote = SyxxSettingsStore.Save(_settings) ? "Saved." : "Could not save settings.";

                        // Linux/Ubuntu/SyxxUI.Avalonia/Services/LinuxReadyServices.cs:16-53
                        if (settings.TelemetryEnabled) { _tracerProvider = ...Register(...); }

                        ]

- **[Delta Number]**    [Delta Short Description/Name]
- **Criticality:**      Indicate `Critical / Non-critical`; whether the delta is critical and user was made aware.
- **Location:**         Filename and line numbers (e.g., `SynthesisEngine.swift:78-84`)
- **Issue:**            Clear description of what's wrong and why it matters
- **Cross-ref. Delta:** Note if this is already tracked (e.g., "Partially covered by DSN-002 but not the specific math gap")
- **Fix(es):**          Brief suggestion for how to fix (not full implementation)

- **Code Sample:**      [

                        Paste an example of the defective code.
                        For more complex involvement, choose the most severe code example(s) to paste.
                        For more simple code examples, just paste the whole thing.

                        ]

---
---

In the location field,separate multiple lines within a file with a comma (e.g., `SynthesisEngine.swift:78-84, 123-127`); separate multiple lines in multiple files with a semicolon (e.g., `SynthesisEngine.swift: 78-84, 123-127; Conclusion.swift: 46-52`).

Example:

---
---

[HOLD - IBM BOB]
Here's the **fully rewritten, production-ready version** with all fixes applied:

### Indentation & Alignment (Required)

Every delta field aligns to a fixed value column at character 24 (columns 0–23 hold the label + padding; the value and all wrapped lines begin at column 24).

- **Alignment Rules**

Pad each label so its value starts at column 24. The longest label, `- **Cross-ref. Delta:**` (23 chars), gets one trailing space; shorter labels get more. Never butt a value against its label.

Hard-wrap long values at ~column 100; every continuation line is indented exactly 24 spaces. Do not rely on the editor's soft-wrap (it wraps to column 0 and looks ragged).

- **Fix(es):** — each `[Fix N]` on its own line starting at column 24; wrapped continuations also at column 24.
- **Code Sample:** — opening `[` on the label line at column 24, then a blank line, then each code line indented 24 spaces (preserve the code's own relative indentation on top of that), then a blank line, then `]` at column 24. Never word-wrap code — only indent it.

Wrap each delta in `---` delimiters.

If alignment is wrong (or you inherited un-aligned entries), don't hand-fix — run:

```bash
python3 scripts/reflow-deltas.py DELTAS.md
```

It re-indents every delta field to column 24 and touches only the A–F ledger (instructions, template, worked example, and the Delta-of-the-Week section are left untouched). Column and wrap width are the `VALUE_COL = 24` / `WIDTH = 100` constants at the top of the script.

## Delta Evaluation Instructions

When asked to `evaluate` deltas, you are asked to visit this document and critique the deltas submitted by your peers, i.e., peer vetting. Read each and decide:

- whether you **agree** with the delta or **disagree** with the delta.

- if you **disagree** with the delta, you are asked to explain the basis of your disagreement.

Disagreement can be based on any of three grounds:

1. You believe that a delta should be overruled as a false positive or false detection (an alleged delta is, in fact, not a delta at all),

2. You agree that the delta is a legitimate concern but disagree on its stated basis (the issue is a legitimate delta but for other reasons), or

3. You agree that the delta is a legitimate concern but disagree on the proposed fix(es).

### Commenting Template

When evaluating deltas, you are asked to make comments inline to the comment you're referencing. When doing so, follow the instructions for recording new deltas, except the row labels should be indented to column (character) 24 and each line of text, other than the row label, should be indented to column (character) 48. Delta comments are written immediately after the referenced delta but within the `---` wrap delimiters, using the following row labels and matching this format:

                       - **Tool Name**            [Opencode]
                       - **Date**                 7-8-2026
                       - **Agree / Disagree**     Disagree
                       - **Disagree Basis**       I disagree on the basis that the proposed fixes would introduce new deltas by...
                       - **Proposed Revisions**   I proposed the following fixes, which would not introduce new deltas.
                       - **Proposed Code**        [
                                                    // SynthesisEngine.swift:78-84
                                                    if degradedCount == 0 && inconclusiveCount == 0 {
                                                        confidence = 1.0 // 100% with zero evidence
                                                        // different code here.
                                                    }
                                                  ]
                                                  
    Likewise, you can respond to a comment by indenting your response further so that the row labels begin on column (or character) no. 48, with each line being left justified to column (or character) no. 72. The row labels for comments to comments are: `- **Tool Name**`, `- **Date**`, `- **Agree/Disagree**`, `- **Disagree Basis**`, `- **Proposed Revision**`, and `- **Proposed Coded**`.

---
---

### Criticality

**Automatic client-side criticality enhancement:**

    1. Any delta involving silent fallback to a public resolver when system resolver undetectable is automatically enhanced to critical status.
    2. Any delta involving a reliance on forgeable evidence is automatically enhanced to critical status.
    
**Automatic server-side criticality enhancement:**

    1. Any delta that downs or exposes the server to malicious attack is automatically enhanced to critical status.
    2. Any delta involving a DNS-leak collector that doesn't validate input (RCE risk via injection) is automatically enhanced to critical status.
    3. Any delta involving a content-integrity proxy that accepts forged collector-attested evidence is automatically enhanced to critical status.
    4. Any delta that involves PII retention and release is automatically enhanced to critical status. 
    5. Any delta involving payments or transfers of funds is automatically enhanced to critical status.
    6. Any delta involving server endpoints that don't authenticate requests (anyone can submit fake evidence) is automatically enhanced to critical status.

These are not static, nor are they inflexible. These are illustrative. **If a delta ought to be considered critical but does not fit these categories, it too is automatically enhanced to critical status.**

#### Quick decision guide

If you're unsure whether a delta is Critical, ask:

1. Can this cause a false "secure" or false "tampered" verdict? → Critical

2. Can this cause real tampering/censorship to go undetected? → Critical

3. Can this evidence be forged or altered by an attacker? → Critical

4. Does this violate the Honesty Protocol (no verdict before evidence, no mocked output, no zero-data confidence)? → Critical

5. Does this silently hide a failure from the user? → Critical

If you ask yourself, "Can this wait?" and the answer is, "No," it's critical. It is automatically enhanced to critical status.

If none of the above apply, it's probably High or Medium, not Critical.

#### Reporting Requirement

Stop and report to the user immediately if:

- You find a **Critical-severity defect** (false confidence risk, false verdict, forgeable evidence) — report immediately, don't wait for the full audit. This is critical for all, as many of you do not have persistent context and user has limited usage for all of you.
- You hit a network/dependency failure (per AGENTS.md "Pause and Report" rules)
- You're unsure whether a pattern is a bug or intentional design — ask before logging

Do not call the analysis "complete" until you've covered all critical paths above, even if you haven't read every file in the codebase.


## **A. Server Components**

---

- **[A001]**            Passive DNS drift boolean fallback collapses tri-state
- **Status:**           **OPEN** — Verified in source. passivedns.py:123 still emits
                        `"driftSuspected": drift == "suspected"`. The comment says "backward-compat
                        bool alongside tri-state" (A117), but legacy clients reading only the
                        boolean cannot distinguish "insufficient-data" from "none" — both collapse
                        to false, violating the no-result-before-evidence rule the comment itself
                        cites.
- **Criticality:**      Non-critical; API response JSON includes legacy driftSuspected boolean
                        alongside tri-state drift string.
- **Location:**         Server/syxx_server/passivedns.py:121
- **Issue:**            baseline() computes a tri-state drift string ("insufficient-data",
                        "suspected", "none"), but then collapses it into a boolean driftSuspected
                        field in response JSON, turning "insufficient-data" and "none" into false
                        for legacy clients.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Remove driftSuspected boolean from API response JSON forcing clients
                        to handle tri-state drift string.
- **Code Sample:**      [

                        // Server/syxx_server/passivedns.py:121
                        "driftSuspected": drift == "suspected",

                        ]

---

- **[A002]**            IDN/Punycode bypass of per-run unique-host cap enables SSRF fan-out
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. urlsplit does not perform IDN-to-punycode conversion.
                        caf\xc3\xa9.com and xn--caf-dma.com produce different hostnames, each consuming
                        a separate budget slot.
- **Criticality:**      Critical — server-side input validation gap allowing host budget bypass.
                        The FETCH_MAX_UNIQUE_HOSTS cap is the SSRF fan-out bound; an attacker can
                        double the effective host limit by submitting the same asset host in both
                        Unicode and punycode forms.
- **Location:**         Server/syxx_server/warp.py:339
- **Issue:**            urllib.parse.urlsplit does not perform IDN-to-punycode conversion. An IDN
                        domain like https://caf\xc3\xa9.com/a.js and its punycode equivalent
                        https://xn--caf-dma.com/a.js produce different hostnames for the host_budget
                        set, allowing bypass of the unique-host cap. socket.getaddrinfo internally
                        encodes both to the same punycode before DNS resolution, so the actual
                        fetch goes to the same server but the cap doesn't see it.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Convert hostnames to punycode canonical form before adding to the
                        host budget. Use encodings.idna.ToASCII + ToUnicode for round-trip
                        normalization, or enforce ASCII-only hostnames in the manifest.
- **Code Sample:**      [

                        # Server/syxx_server/warp.py:339
                        host = (urllib.parse.urlsplit(url).hostname or "").lower()

                        ]

---

- **[A003]**            No Unicode normalization of hostnames in host budget comparison
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. .lower() alone does not normalize Unicode; two visually
                        identical hostnames in different normalization forms consume two budget
                        slots.
- **Criticality:**      Non-critical; host budget is the SSRF fan-out gate. A hostname arriving
                        in two different normalization forms consumes two budget slots instead of
                        one, potentially enabling resource exhaustion. urlsplit normalizes to NFC
                        in most cases, but explicit normalization is defense-in-depth.
- **Location:**         Server/syxx_server/warp.py:339, 442
- **Issue:**            .lower() provides Unicode-aware case-folding but does not normalize to a
                        canonical Unicode form. If a hostname arrives in a different normalization
                        form that urlsplit doesn't catch (e.g., via percent-encoding edge cases),
                        two visually identical hostnames could be treated as different hosts,
                        defeating the host budget.
- **Cross-ref. Delta:** Related to A002 (IDN host budget bypass).
- **Fix(es):**          [Fix 1] Apply unicodedata.normalize('NFC', host.lower()) to all hostnames
                        before adding to host budget set.
- **Code Sample:**      [

                        # Server/syxx_server/warp.py:339
                        host = (urllib.parse.urlsplit(url).hostname or "").lower()

                        ]

---

- **[A004]**            _envelope_decode function name conflates Content-Encoding with Transfer-
                        Encoding
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. The function processes HTTP Content-Encoding (gzip, br, deflate)
                        but is named and documented as handling the "transfer envelope."
- **Criticality:**      Non-critical; no byte-processing bug, but the naming could mislead future
                        security audits. Transfer-Encoding (chunked) and Content-Encoding (gzip/br)
                        are distinct HTTP concepts per RFC 9110/9112.
- **Location:**         Server/syxx_server/warp.py:218, 233, 269, 277
- **Issue:**            The function _envelope_decode is documented as "decode the transfer envelope
                        ONLY" but actually processes the Content-Encoding response header. The
                        error message at line 233 correctly says "content-encoding" while the
                        function name says "envelope," creating internal inconsistency. The spec
                        (warp-manifest-v1-spec.md \xc2\xa78) also uses "envelope-decoded" propagating
                        the same conflation.
- **Cross-ref. Delta:** Related to F002, F003 (same terminology conflation in Swift/C# comments).
- **Fix(es):**          [Fix 1] Rename _envelope_decode to _content_encoding_decode and update all
                        call sites. [Fix 2] Update spec \xc2\xa78 to use "Content-Encoding" instead of
                        "transfer envelope" / "envelope-decoded."
- **Code Sample:**      [

                        # Server/syxx_server/warp.py:218-219
                        def _envelope_decode(body, encoding, max_bytes):
                            """Spec \xc2\xa78: decode the transfer envelope ONLY (gzip / br / deflate),
                            to recover the resource's actual bytes."""

                        ]

---

- **[A005]**            Server exposes two different key_id schemes from the same Ed25519 key
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. /api/pubkey returns a base64-of-first-8-bytes key_id; warp
                        manifest uses lowercase hex of full 32-byte key.
- **Criticality:**      Non-critical; operator confusion risk. An operator who copies keyId from
                        /api/pubkey into their pinned key set would pin a value that never matches
                        the warp manifest key_id, producing silent untrustedSigner -> INCONCLUSIVE.
- **Location:**         Server/syxx_server/app.py:1933; Server/syxx_server/leak.py:158-161
- **Issue:**            The warp manifest route derives key_id as lowercase hex of the full 32-byte
                        raw Ed25519 public key. leak.collector_key_id() returns base64 of the first
                        8 bytes. Both schemes coexist for the same key with no documentation of the
                        difference. The /api/pubkey endpoint returns the leak format, which is
                        incompatible with warp manifest pinning.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Unify on one scheme: update leak.collector_key_id() to return hex
                        form (breaking change for leak-attestation clients). [Fix 2] Add a separate
                        /api/pubkey field "warpKeyId" for the warp format. [Fix 3] Document the
                        difference prominently in the /api/pubkey response.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:1933
                        key_id = base64.b64decode(leak.collector_public_key()).hex()

                        # Server/syxx_server/leak.py:160-161
                        _COLLECTOR_KEY_ID = base64.b64encode(
                            _COLLECTOR_PUB_KEY_RAW[:8]).decode("ascii")

                        ]

---

- **[A006]**            Python str.lower() is locale-sensitive for hostname normalization
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. In Turkish locale, I lowercases to \xc4\xb1 (dotless i), not i.
- **Criticality:**      Non-critical; currently mitigated because urlsplit already lowercases ASCII
                        hostnames. The redundant .lower() call is fragile for future changes.
- **Location:**         Server/syxx_server/warp.py:339, 442
- **Issue:**            Hostnames are lowercased using Python's str.lower() which is locale-aware.
                        In the Turkish locale, "I" lowercases to "\xc4\xb1" not "i". While urlsplit
                        already normalizes hostnames to lowercase ASCII, the explicit .lower() is
                        redundant and fragile. A future change that applies .lower() to URL path
                        components could behave differently by locale.
- **Cross-ref. Delta:** Related to A003 (Unicode normalization of hostnames).
- **Fix(es):**          [Fix 1] Replace .lower() with .casefold() which is locale-insensitive.
                        [Fix 2] Remove the redundant call since urlsplit already lowercases.
- **Code Sample:**      [

                        # Server/syxx_server/warp.py:339
                        host = (urllib.parse.urlsplit(url).hostname or "").lower()

                        ]

---

- **[A007]**            _envelope_decode does not handle multiple Content-Encoding values
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. Per RFC 9110 \xc2\xa78.4, Content-Encoding can carry multiple values.
- **Criticality:**      Non-critical; multi-encoding is extremely rare in practice and the fail-safe
                        behavior (FetchError -> fetch_failed -> INCONCLUSIVE) is correct.
- **Location:**         Server/syxx_server/warp.py:226-233
- **Issue:**            The function does exact string match on Content-Encoding (gzip, deflate,
                        br).
                        A header like "Content-Encoding: gzip, br" would match none of the single-
                        value checks and raise FetchError, recording the asset as fetch_failed.
                        While this is a fail-safe outcome (never a false match), it is a
                        completeness gap.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] If multi-encoding support is desired, split on comma and decode in
                        reverse order. Given the rarity and correct fail-safe behavior, this may be
                        deferred.
- **Code Sample:**      [

                        # Server/syxx_server/warp.py:226-233
                        enc = (encoding or "").strip().lower()
                        if enc in ("", "identity"):
                            return body
                        if enc == "gzip":
                            return _bounded_decompress(body, 16 + zlib.MAX_WBITS, max_bytes)
                        if enc == "deflate":
                            ...
                        if enc == "br":
                            return _brotli_decompress(body, max_bytes)
                        raise fetcher.FetchError(f"unsupported content-encoding: {enc!r}")

                        ]

---

- **[A008]**            Server uses time.time() (wall clock) for manifest timestamps without clock-
                        source documentation
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. Wall-clock time is subject to NTP adjustments, leap seconds, and
                        manual clock changes.
- **Criticality:**      Non-critical; the 300,000 ms skew tolerance is the only defense against
                        clock drift. Operators should be aware of the clock-source dependency.
- **Location:**         Server/syxx_server/warp.py:309
- **Issue:**            time.time() returns wall-clock time, subject to NTP adjustments and clock
                        jumps. The freshness check compares this server-generated timestamp against
                        the client's local now_ms. If the server clock is behind the client, the
                        effective freshness window is shortened. If the server clock jumps backward
                        (NTP correction), a manifest could be stamped with a future timestamp.
                        The deprecated build_baseline correctly uses time.monotonic() for its
                        deadline check, showing awareness of the distinction.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Document the clock-source dependency in the spec and code comments.
                        [Fix 2] Consider using a monotonic clock source anchored to a known epoch
                        for the timestamp, though this would require a protocol change.
- **Code Sample:**      [

                        # Server/syxx_server/warp.py:309
                        "timestamp": int(time.time() * 1000),

                        ]

---

- **[A009]**            No upper bound on freshness_seconds emitted by the server
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. Server emits freshness_seconds directly from config without
                        enforcing the 600-second verifier-side ceiling (Amendment A4).
- **Criticality:**      Non-critical; clients independently clamp, but server may emit values larger
                        than the verifier ceiling, which is meaningless.
- **Location:**         Server/syxx_server/warp.py:310
- **Issue:**            The server emits freshness_seconds directly from configuration without
                        enforcing the 600-second verifier-side ceiling. Clients apply their own
                        min(freshness_seconds, 600) clamp, so the protocol is safe, but a
                        misconfigured server operator may think they've set a 1-hour window when
                        clients silently cap at 10 minutes. This is a configuration footgun.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Clamp at the server: min(config.WARP_MANIFEST_FRESHNESS, 600).
                        [Fix 2] Document the ceiling in the config.
- **Code Sample:**      [

                        # Server/syxx_server/warp.py:310
                        "freshness_seconds": config.WARP_MANIFEST_FRESHNESS,

                        ]

---

- **[A010]**            Deprecated build_baseline serializes without sort_keys=True
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. The replacement build_manifest path correctly uses sort_keys=True.
- **Criticality:**      Non-critical; build_baseline is deprecated and superseded by build_manifest.
                        Python 3.7+ dict insertion order provides implicit determinism.
- **Location:**         Server/syxx_server/warp.py:192
- **Issue:**            build_baseline caches result dict as JSON via json.dumps(result) without
                        sort_keys=True. The result dict contains sub-dicts keyed by URL. JSON key
                        ordering relies on Python 3.7+ dict insertion-order guarantees, which is
                        implicit rather than explicit. The replacement build_manifest path uses
                        sort_keys=True at app.py:1925.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Since this is deprecated and scheduled for deletion per spec
                        \xc2\xa711,
                        no code change is needed. If the deprecation window extends, add
                        sort_keys=True.
- **Code Sample:**      [

                        # Server/syxx_server/warp.py:192
                        db.warp_cache_put(final_url, json.dumps(result))

                        ]

---

- **[A011]**            json.dumps with ensure_ascii=False produces non-normalized UTF-8 signed
                        bytes
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. The manifest is signed over these exact bytes; NFC and NFD
                        produce different byte sequences.
- **Criticality:**      Non-critical; currently mitigated because asset URLs are resolved by urljoin
                        (NFC), origin_url comes from client query (ASCII-safe), and nonce is
                        restricted to [A-Za-z0-9_-]. A future Unicode field would expose this.
- **Location:**         Server/syxx_server/app.py:1925
- **Issue:**            manifest_bytes = json.dumps(manifest, ...,
                        ensure_ascii=False).encode("utf-8")
                        If the manifest contains Unicode characters (e.g., non-ASCII URL paths),
                        the serialized bytes depend on the normalization form of the input strings.
                        NFC and NFD produce different byte sequences, producing different signatures
                        for semantically identical manifests.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Normalize string values in the manifest dict before serialization
                        using unicodedata.normalize('NFC', ...). Apply to origin_url and final_url
                        fields.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:1925
                        manifest_bytes = json.dumps(manifest, separators=(",", ":"),
                            sort_keys=True, ensure_ascii=False).encode("utf-8")

                        ]

---

## **B. Detection Efficacy**

---

- **[B001]**            Passive DNS drift boolean fallback collapses tri-state
- **Status:**           **OPEN** — Verified in source. passivedns.py:123 still emits
                        `"driftSuspected": drift == "suspected"`. The comment says "backward-compat
                        bool alongside tri-state" (A117), but legacy clients reading only the
                        boolean cannot distinguish "insufficient-data" from "none" — both collapse
                        to false, violating the no-result-before-evidence rule the comment itself
                        cites.
- **Criticality:**      Non-critical; API response JSON includes legacy driftSuspected boolean
                        alongside tri-state drift string.
- **Location:**         Server/syxx_server/passivedns.py:121
- **Issue:**            baseline() computes a tri-state drift string ("insufficient-data",
                        "suspected", "none"), but then collapses it into a boolean driftSuspected
                        field in response JSON, turning "insufficient-data" and "none" into false
                        for legacy clients.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Remove driftSuspected boolean from API response JSON forcing clients
                        to handle tri-state drift string.
- **Code Sample:**      [

                        // Server/syxx_server/passivedns.py:121
                        "driftSuspected": drift == "suspected",

                        ]

---

## **C. Verdict & Evidence Integrity**

---

- **[C001]**            Passive DNS drift boolean fallback collapses tri-state
- **Status:**           **OPEN** — Verified in source. passivedns.py:123 still emits
                        `"driftSuspected": drift == "suspected"`. The comment says "backward-compat
                        bool alongside tri-state" (A117), but legacy clients reading only the
                        boolean cannot distinguish "insufficient-data" from "none" — both collapse
                        to false, violating the no-result-before-evidence rule the comment itself
                        cites.
- **Criticality:**      Non-critical; API response JSON includes legacy driftSuspected boolean
                        alongside tri-state drift string.
- **Location:**         Server/syxx_server/passivedns.py:121
- **Issue:**            baseline() computes a tri-state drift string ("insufficient-data",
                        "suspected", "none"), but then collapses it into a boolean driftSuspected
                        field in response JSON, turning "insufficient-data" and "none" into false
                        for legacy clients.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Remove driftSuspected boolean from API response JSON forcing clients
                        to handle tri-state drift string.
- **Code Sample:**      [

                        // Server/syxx_server/passivedns.py:121
                        "driftSuspected": drift == "suspected",

                        ]

---

## **D. Platform Integration & Resource Safety**

---

- **[D001]**            Passive DNS drift boolean fallback collapses tri-state
- **Status:**           **OPEN** — Verified in source. passivedns.py:123 still emits
                        `"driftSuspected": drift == "suspected"`. The comment says "backward-compat
                        bool alongside tri-state" (A117), but legacy clients reading only the
                        boolean cannot distinguish "insufficient-data" from "none" — both collapse
                        to false, violating the no-result-before-evidence rule the comment itself
                        cites.
- **Criticality:**      Non-critical; API response JSON includes legacy driftSuspected boolean
                        alongside tri-state drift string.
- **Location:**         Server/syxx_server/passivedns.py:121
- **Issue:**            baseline() computes a tri-state drift string ("insufficient-data",
                        "suspected", "none"), but then collapses it into a boolean driftSuspected
                        field in response JSON, turning "insufficient-data" and "none" into false
                        for legacy clients.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Remove driftSuspected boolean from API response JSON forcing clients
                        to handle tri-state drift string.
- **Code Sample:**      [

                        // Server/syxx_server/passivedns.py:121
                        "driftSuspected": drift == "suspected",

                        ]

---

- **[D002]**            Swift origin_url comparison diverges from C# due to Unicode normalization
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. Swift String.== uses canonical equivalence (NFC = NFD);
                        C# StringComparison.Ordinal compares UTF-16 code units (NFC != NFD).
- **Criticality:**      Non-critical; structural cross-platform parity violation. If a manifest's
                        origin_url contains Unicode in a different normalization form than the
                        client's requestedOrigin, Swift proceeds to content verification while C#
                        returns originMismatch -> INCONCLUSIVE.
- **Location:**         macOS/Sources/SyxxKit/WarpEngine.swift:307;
                        Windows/dotNET/Syxx.Kit/WarpEngine.cs:455
- **Issue:**            Swift's String.== uses Unicode canonical equivalence — NFC and NFD forms of
                        the same string compare equal. C#'s StringComparison.Ordinal compares UTF-16
                        code units byte-by-byte — NFC and NFD compare as different. This is a
                        structural parity violation between the two client engines. The shared
                        corpus doesn't exercise URLs with non-ASCII characters, so this divergence
                        is not caught by the oracle.
- **Cross-ref. Delta:** Related to D005 (same-origin redirect host comparison divergence).
- **Fix(es):**          [Fix 1] Normalize both strings to NFC before comparison on both platforms.
                        Swift: .precomposedStringWithCanonicalMapping. C#: .Normalize(NormalizationForm.FormC).
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/WarpEngine.swift:307
                        if manifestOrigin != requestedOrigin { return .originMismatch }

                        // Windows/dotNET/Syxx.Kit/WarpEngine.cs:455
                        if (!string.Equals(manifestOrigin, requestedOrigin,
                            StringComparison.Ordinal))

                        ]

---

- **[D003]**            Swift same-origin host comparison is case-sensitive, violating DNS case-
                        insensitivity
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. URL.host preserves original case; "example.com" != "Example.COM"
                        in Swift but DNS is case-insensitive.
- **Criticality:**      Non-critical; a legitimate same-origin redirect with case variation is
                        rejected as a policy violation, producing false fetchError -> INCONCLUSIVE.
                        C# (Uri.Host normalizes to lowercase) and Python (urlsplit lowercases) are
                        immune. This is a cross-platform behavioral divergence.
- **Location:**         macOS/Sources/SyxxKit/WarpEngine.swift:608
- **Issue:**            hopPermittedByPolicy uses from.host == to.host for same-origin enforcement.
                        URL.host preserves the original case from the URL string. A redirect from
                        https://example.com/ to https://Example.COM/path would be rejected as a
                        policy violation. Any real-world CDN or load-balancer that redirects with
                        varying hostname case would cause the Swift client to fail-closed into
                        INCONCLUSIVE for every asset.
- **Cross-ref. Delta:** Related to D004 (redirectReconciles case sensitivity).
- **Fix(es):**          [Fix 1] Add .lowercased() to both host comparisons in hopPermittedByPolicy.
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/WarpEngine.swift:608
                        case .sameOrigin:
                            return from.scheme == to.scheme
                                && from.host == to.host
                                && (from.port ?? -1) == (to.port ?? -1)

                        ]

---

- **[D004]**            Swift redirectReconciles uses case-sensitive URL comparison against server's
                        lowercase final_url
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. URL.absoluteString preserves hostname case from HTTP response;
                        the server's final_url (via urlsplit) is always lowercase.
- **Criticality:**      Non-critical; case mismatch causes fetchError -> INCONCLUSIVE for every
                        asset under same-origin or list redirect policy. C# Uri.AbsoluteUri
                        normalizes to lowercase and is immune.
- **Location:**         macOS/Sources/SyxxKit/WarpEngine.swift:444-445
- **Issue:**            redirectReconciles compares terminalURL (from
                        URLResponse.url?.absoluteString,
                        preserving hostname case from HTTP response) against the manifest's finalURL
                        (server-generated, always lowercase). If the HTTP response Location header
                        has "https://Example.COM/path" and the manifest declares
                        "https://example.com/path", the == comparison fails.
- **Cross-ref. Delta:** Related to D003 (same-origin host case sensitivity).
- **Fix(es):**          [Fix 1] Normalize URLs before comparison: lowercase scheme and host,
                        normalize port. Apply to terminalURL, requestURL, and finalURL.
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/WarpEngine.swift:441-446
                        static func redirectReconciles(policy: WarpRedirectPolicy,
                            requestURL: String, finalURL: String?,
                            terminalURL: String) -> Bool {
                            switch policy {
                            case .none:
                                return terminalURL == requestURL
                            case .sameOrigin, .list:
                                if let finalURL { return terminalURL == finalURL }
                                return terminalURL == requestURL
                            }
                        }

                        ]

---

- **[D005]**            same-origin redirect host comparison is platform-dependent (NFC vs Ordinal)
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. Swift String.== uses canonical equivalence; C# string == uses
                        StringComparison.Ordinal. Redirect enforcement diverges for Unicode hosts.
- **Criticality:**      Non-critical; structural parity violation. A redirect permitted by one
                        engine and rejected by the other would cause divergent conclusions.
- **Location:**         macOS/Sources/SyxxKit/WarpEngine.swift:608;
                        Windows/dotNET/Syxx.Kit/WarpEngine.cs:635
- **Issue:**            Swift URL.host comparison uses String.== (canonical equivalence). C#
                        Uri.Host comparison uses string == (Ordinal, byte-by-byte). The same
                        redirect hop could be permitted by one engine and rejected by the other,
                        causing a divergence in same-origin redirect policy enforcement. The spec
                        \xc2\xa76 redirect policy must be applied identically.
- **Cross-ref. Delta:** Related to D002 (origin_url comparison divergence), D003 (case sensitivity).
- **Fix(es):**          [Fix 1] Normalize hostnames to lowercase NFC before comparison in both
                        engines. Swift: .lowercased().precomposedStringWithCanonicalMapping.
                        C#: .ToLowerInvariant().Normalize(NormalizationForm.FormC).
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/WarpEngine.swift:608
                        return from.scheme == to.scheme
                            && from.host == to.host
                            && (from.port ?? -1) == (to.port ?? -1)

                        // Windows/dotNET/Syxx.Kit/WarpEngine.cs:635
                        target.Scheme == u.Scheme && target.Host == u.Host
                            && target.Port == u.Port

                        ]

---

- **[D006]**            Swift UInt64 crash on negative system clock value
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. UInt64() traps at runtime on negative values; a misconfigured
                        system clock causes a hard crash instead of INCONCLUSIVE.
- **Criticality:**      Non-critical; turns a recoverable state (bad clock -> futureDated or
                        expired) into a denial-of-service for the content-integrity check. C# twin
                        is immune (unchecked ulong cast wraps).
- **Location:**         macOS/Sources/SyxxKit/WarpEngine.swift:501
- **Issue:**            Date().timeIntervalSince1970 returns a TimeInterval (Double). If the system
                        clock is set to before January 1, 1970, this returns a negative Double.
                        UInt64() traps at runtime (fatal error) on negative values. The production
                        code path (SyxxWarp.swift:41 calls runVantageCheck without passing nowMs)
                        always hits this fallback.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Guard against negative values before UInt64 conversion; use 0 as a
                        safe floor. Freshness will almost certainly fail (future-dated or expired),
                        which is correct.
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/WarpEngine.swift:501
                        let now = nowMs ?? UInt64(Date().timeIntervalSince1970 * 1000)

                        ]

---

- **[D007]**            Cross-platform nowMs generation uses inconsistent mechanisms (float vs
                        integer)
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. Python and Swift use float-based paths; C# uses native integer
                        milliseconds.
- **Criticality:**      Non-critical; Double ULP at current epoch is ~0.000238 ms, well below the
                        1 ms target. Latent inconsistency for future precision changes.
- **Location:**         Server/syxx_server/warp.py:309;
                        macOS/Sources/SyxxKit/WarpEngine.swift:501;
                        Windows/dotNET/Syxx.Kit/WarpEngine.cs:517
- **Issue:**            Three platforms generate nowMs using three different mechanisms: Python
                        int(time.time() * 1000) (float truncation), Swift UInt64(Date()... * 1000)
                        (float truncation), C# (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        (native integer). The float-based paths are needlessly indirect. Any future
                        change to the multiplication factor (e.g., microseconds) would silently lose
                        precision.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Python: use time.time_ns() // 1_000_000 (Python 3.7+). Swift:
                        document the float precision assumption. C#: already correct.
- **Code Sample:**      [

                        # Server/syxx_server/warp.py:309
                        int(time.time() * 1000)

                        # macOS/Sources/SyxxKit/WarpEngine.swift:501
                        UInt64(Date().timeIntervalSince1970 * 1000)

                        # Windows/dotNET/Syxx.Kit/WarpEngine.cs:517
                        (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

                        ]

---

## **E. Security & Hardening**

---

- **[E001]**            Passive DNS drift boolean fallback collapses tri-state
- **Status:**           **OPEN** — Verified in source. passivedns.py:123 still emits
                        `"driftSuspected": drift == "suspected"`. The comment says "backward-compat
                        bool alongside tri-state" (A117), but legacy clients reading only the
                        boolean cannot distinguish "insufficient-data" from "none" — both collapse
                        to false, violating the no-result-before-evidence rule the comment itself
                        cites.
- **Criticality:**      Non-critical; API response JSON includes legacy driftSuspected boolean
                        alongside tri-state drift string.
- **Location:**         Server/syxx_server/passivedns.py:121
- **Issue:**            baseline() computes a tri-state drift string ("insufficient-data",
                        "suspected", "none"), but then collapses it into a boolean driftSuspected
                        field in response JSON, turning "insufficient-data" and "none" into false
                        for legacy clients.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Remove driftSuspected boolean from API response JSON forcing clients
                        to handle tri-state drift string.
- **Code Sample:**      [

                        // Server/syxx_server/passivedns.py:121
                        "driftSuspected": drift == "suspected",

                        ]

---

- **[E002]**            TOCTOU DNS rebinding in Swift client-vantage HTTP fetch bypasses SSRF gate
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. validatePublicHost resolves DNS once, then URLSession resolves
                        again for the actual connection — a DNS rebinding attack between the two
                        resolutions can redirect to private IPs.
- **Criticality:**      Non-critical; for HTTPS, TLS certificate validation against the original
                        hostname provides defense-in-depth. For HTTP, this is a working SSRF vector.
                        C# is immune (atomic resolution in ConnectCallback).
- **Location:**         macOS/Sources/SyxxKit/WarpEngine.swift:520-525, 601-605
- **Issue:**            validatePublicHost resolves the target hostname to IPs and verifies all are
                        public-routable. Then URLSession.data(for:) performs its own DNS resolution
                        for the actual TCP+TLS connection. A DNS rebinding attack between the two
                        resolutions can redirect the connection to a private (RFC1918/loopback) IP.
                        The same gap exists for every redirect hop in WarpRedirectController.
                        The C# twin's ConnectCallback resolves DNS and connects atomically.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Resolve DNS once and pin the connection to that IP, mirroring the
                        C# approach. Use NWConnection or a custom URLSession delegate. [Fix 2] For
                        HTTP specifically, forbid HTTP entirely in vantage fetch (HTTPS-only).
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/WarpEngine.swift:520-525
                        guard await validatePublicHost(url) else { return .failed }
                        ...
                        guard let (data, resp) = try? await session.data(for: request) ...

                        ]

---

- **[E003]**            No key rotation, expiration, or revocation support in trust store
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. Both WarpManifestPinning implementations load a static flat
                        dictionary with no per-key metadata.
- **Criticality:**      Non-critical; multi-key pinning is supported but the operator must manually
                        replace the entire key file to rotate. No transition mode or remote
                        revocation.
- **Location:**         macOS/Sources/SyxxKit/WarpManifestPinning.swift:19-48;
                        Windows/dotNET/Syxx.Kit/WarpManifestPinning.cs:22-66
- **Issue:**            Both implementations parse a flat [key_id: raw_public_key_bytes] dictionary
                        from environment variables or a persisted key file. There is no mechanism
                        for key expiration (valid_until), graceful rotation (transition period with
                        deprecation warning), or key revocation (revoked list). If a signing key is
                        compromised, the operator must race to update the key file on every client.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Extend the trust-set format to carry per-key metadata: expires,
                        revoked, transition. [Fix 2] Consider a structured JSON format for the key
                        file with per-key objects.
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/WarpManifestPinning.swift:19-48
                        public static func parse(_ raw: String) -> [String: Data] {
                            var out: [String: Data] = [:]
                            // ... flat key_id -> pubkey mapping, no metadata
                            return out
                        }

                        ]

---

- **[E004]**            No explicit certificate revocation checking (CRL/OCSP) in TLS connections
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. Both client-vantage fetchers rely on system TLS stack defaults
                        without enabling revocation checking.
- **Criticality:**      Non-critical; Ed25519 manifest signature provides end-to-end integrity.
                        A MITM with a revoked certificate could cause DoS (invalidSignature) but
                        not forge a valid manifest. For /api/warp/raw overlay fetch (unsigned),
                        a MITM with a revoked-but-trusted certificate could substitute clean asset
                        bytes.
- **Location:**         macOS/Sources/SyxxKit/WarpEngine.swift:508-512;
                        Windows/dotNET/Syxx.Kit/WarpEngine.cs:574-578
- **Issue:**            Swift URLSessionConfiguration.ephemeral does not set TLS revocation flags.
                        C# SocketsHttpHandler does not set SslOptions.CertificateRevocationCheckMode
                        or attach a ServerCertificateCustomValidationCallback. If a CA certificate
                        used by a target origin is revoked, the client will still trust it if the
                        system trust store hasn't been updated.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] C#: set SslOptions.CertificateRevocationCheckMode =
                        X509RevocationMode.Online. [Fix 2] Swift: use a custom URLSessionDelegate
                        with urlSession(_:didReceive:completionHandler:) for revocation checking.
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/WarpEngine.swift:508-512
                        private static let vantageSession: URLSession = {
                            let config = URLSessionConfiguration.ephemeral
                            // no revocation flags
                            return URLSession(configuration: config, delegate: nil,
                                delegateQueue: nil)
                        }()

                        ]

---

- **[E005]**            No zero-width character handling in URL parsing enables invisible host
                        differentiation
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. urlsplit preserves zero-width characters, bidi markers, and
                        other invisible Unicode in hostnames and paths.
- **Criticality:**      Non-critical; invisible characters in hostnames consume host budget slots,
                        cause spurious fetch_failed entries, and could hide malicious origins from
                        human review.
- **Location:**         Server/syxx_server/warp.py:339; Server/syxx_server/fetcher.py:90-98
- **Issue:**            urllib.parse.urlsplit preserves zero-width characters (U+200B ZWSP,
                        U+200C ZWNJ, U+200D ZWJ, U+FEFF BOM), bidi markers (U+202E RLO, U+202D LRO,
                        U+202C PDF), and other invisible Unicode. These characters are invisible to
                        the human eye but differentiate the hostname for the host budget and cause
                        DNS resolution to fail.
- **Cross-ref. Delta:** Related to A002 (IDN host budget bypass), A003 (Unicode normalization).
- **Fix(es):**          [Fix 1] Strip or reject zero-width and bidi control characters in hostnames
                        before processing. Apply to hostname at warp.py:339, 442 and fetcher.py.
- **Code Sample:**      [

                        # Server/syxx_server/warp.py:339
                        host = (urllib.parse.urlsplit(url).hostname or "").lower()

                        ]

---

- **[E006]**            No homoglyph / confusable character detection in domain names
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. Cyrillic 'а' (U+0430) vs Latin 'a' (U+0061) are treated as
                        completely different domains with no warning.
- **Criticality:**      Non-critical; a homoglyph-based phishing domain could pass Warp verification
                        with no warning. The user sees VERIFIED_MATCH for a domain they think is
                        legitimate. The SSRF gate validates the resolved IP, but the content-
                        integrity system doesn't flag this as suspicious.
- **Location:**         Server/syxx_server/warp.py:339; macOS/Sources/SyxxKit/WarpEngine.swift;
                        Windows/dotNET/Syxx.Kit/WarpEngine.cs
- **Issue:**            The code treats all hostnames as opaque strings. A domain using Cyrillic 'а'
                        instead of Latin 'a' — e.g., exаmple.com vs example.com — is treated as a
                        completely different domain. The content-integrity system doesn't flag this
                        as suspicious. The SyxxDiagnostics.swift already has a dns.name.is_idn
                        check; this could be extended to the Warp CLI.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add a confusable-character warning in the CLI output and UI when the
                        requested URL contains characters from multiple scripts. Extend
                        SyxxDiagnostics.swift dns.name.is_idn check to the Warp CLI.
- **Code Sample:**      [

                        # Server/syxx_server/warp.py:339
                        host = (urllib.parse.urlsplit(url).hostname or "").lower()
                        # Treats exаmple.com (Cyrillic 'а') and example.com (Latin 'a')
                        # as completely different domains with no warning.

                        ]

---

- **[E007]**            IntegrityTag.constantTimeEqual leaks HMAC length via early return
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. The guard aBytes.count == bBytes.count else { return false }
                        is a timing side-channel.
- **Criticality:**      Non-critical; the HMAC key is stored in the macOS Keychain and is not
                        attacker-controlled. The HMAC is always 64 hex characters (SHA-256), so the
                        length leak reveals nothing unknown. Practical risk is negligible.
- **Location:**         macOS/Sources/SyxxKit/Trust/IntegrityTag.swift:117-124
- **Issue:**            constantTimeEqual early-returns on length mismatch, creating a timing
                        side-channel. An attacker who can submit candidate HMACs and measure
                        response time can determine the correct HMAC length. The remaining byte-by-
                        byte comparison is constant-time, but the length guard is not.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Use constant-time comparison that does not branch on length: pad the
                        shorter input to the longer length before comparing, or use CryptoKit's
                        built-in HMAC validation.
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/Trust/IntegrityTag.swift:117-124
                        private static func constantTimeEqual(_ a: String, _ b: String) -> Bool {
                            let aBytes = Array(a.utf8)
                            let bBytes = Array(b.utf8)
                            guard aBytes.count == bBytes.count else { return false }
                            var result: UInt8 = 0
                            for i in 0..<aBytes.count {
                                result |= aBytes[i] ^ bBytes[i]
                            }
                            return result == 0
                        }

                        ]

---

- **[E008]**            Test Ed25519 private keys committed to the repository
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. warp-manifest-vectors/keys/ contains test_signing_key.pem and
                        untrusted_key.pem derived from fixed seeds.
- **Criticality:**      Non-critical; these are test keys labeled as such in keys/README.md and
                        never used in production. The production collector key is loaded from the
                        SYXX_COLLECTOR_PRIVATE_KEY environment variable. The presence of private key
                        material could trigger security scanners.
- **Location:**         warp-manifest-vectors/keys/test_signing_key.pem;
                        warp-manifest-vectors/keys/untrusted_key.pem
- **Issue:**            Two Ed25519 private keys in PKCS#8 PEM format are committed to version
                        control. They are derived from fixed 32-byte seeds clearly labeled as test-
                        only. The private keys are only needed by _build_vectors.py to sign test
                        fixtures; the public keys and pinned_keys.json are sufficient for all other
                        purposes.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Replace PEM files with a build step that generates keypairs from
                        seeds at test time. [Fix 2] Add a .gitignore rule note and pre-commit hook
                        warning about private key material.
- **Code Sample:**      [

                        # warp-manifest-vectors/_build_vectors.py:68-69
                        TEST_SEED = b"syxx-warp-manifest-TEST-KEY-seed"
                        UNTRUSTED_SEED = b"syxx-warp-manifest-UNTRUSTED-key"

                        ]

---

## **F. Miscellaneous**

---

- **[F001]**            Sample F-domain Deltas
- **Status:**           **OPEN** — Verified in source. passivedns.py:123 still emits
                        [...]
                        cites.
- **Criticality:**      Non-critical; API response JSON includes [...]
                        alongside tri-state drift string.
- **Location:**         Server/syxx_server/passivedns.py:121
- **Issue:**            baseline() computes a tri-state [...]
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Remove driftSuspected [...]
- **Code Sample:**      [

                        // Server/syxx_server/passivedns.py:121
                        "driftSuspected": drift == "suspected",

                        ]

---

- **[F002]**            Swift client comment says "transfer-encoding" but describes Content-Encoding
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. The comment header says "§8 transfer-encoding note" but the body
                        correctly describes Content-Encoding handling by URLSession.
- **Criticality:**      Non-critical; documentation inconsistency. The comment body is correct and
                        the warning about double-decoding is important.
- **Location:**         macOS/Sources/SyxxKit/WarpEngine.swift:540
- **Issue:**            The comment header line says "transfer-encoding note" but the body correctly
                        references Content-Encoding (URLSession transparently decodes gzip/br).
                        This perpetuates the same terminology conflation as A004.
- **Cross-ref. Delta:** Related to A004 (_envelope_decode naming), F003 (C# comment).
- **Fix(es):**          [Fix 1] Change header to "§8 Content-Encoding note."
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/WarpEngine.swift:540
                        // §8 transfer-encoding note: URLSession/CFNetwork transparently
                        // decodes Content-Encoding gzip/br, so `data` below is ALWAYS
                        // the decoded plaintext...

                        ]

---

- **[F003]**            C# client comment says "transfer-encoding" but describes Content-Encoding
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. The comment header says "§8 transfer-encoding decode" but
                        describes AutomaticDecompression which handles Content-Encoding.
- **Criticality:**      Non-critical; documentation inconsistency. The comment body is correct and
                        the warning about manual Accept-Encoding headers is important.
- **Location:**         Windows/dotNET/Syxx.Kit/WarpEngine.cs:600
- **Issue:**            The comment references "transfer-encoding" but describes
                        AutomaticDecompression which handles Content-Encoding (the Accept-Encoding /
                        Content-Encoding negotiation). The warning about manual Accept-Encoding
                        headers suppressing automatic decompression is correct and important.
- **Cross-ref. Delta:** Related to A004 (_envelope_decode naming), F002 (Swift comment).
- **Fix(es):**          [Fix 1] Change to "§8 Content-Encoding decode." Update "transport" to
                        "HTTP client" for clarity.
- **Code Sample:**      [

                        // Windows/dotNET/Syxx.Kit/WarpEngine.cs:600
                        // §8 transfer-encoding decode: hashing is over envelope-DECODED
                        // raw bytes, so the transport must transparently un-gzip/deflate/
                        // brotli. Setting AutomaticDecompression makes SocketsHttpHandler
                        // send the Accept-Encoding header itself and decode the response...

                        ]

---

- **[F004]**            Reference evaluator freshness check diverges structurally from client
                        engines
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. The reference evaluator (_build_vectors.py) omits the now_ms > ts
                        guard and overflow check that the client engines carry.
- **Criticality:**      Non-critical; all 31 corpus cases pass identically on both paths. Structural
                        alignment between the oracle and engines reduces risk of future divergence.
- **Location:**         warp-manifest-vectors/_build_vectors.py:228-230
- **Issue:**            The reference evaluator computes freshness without the now_ms > ts guard
                        before computing now_ms - ts. In Python this is functionally harmless
                        (arbitrary-precision integers), but the two code paths are semantically
                        different. The Swift client also carries an overflowed guard (belt-and-
                        braces against unclamped values) that the oracle lacks.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Align the reference evaluator to match the client engines' guard
                        structure. Low-priority cleanup — current behavior is correct.
- **Code Sample:**      [

                        # warp-manifest-vectors/_build_vectors.py:228-230
                        freshness_seconds = min(freshness_seconds,
                            MAX_FRESHNESS_WINDOW_SECONDS)
                        if not isinstance(ts, int) or now_ms - ts > freshness_seconds * 1000:
                            result["freshnessState"] = "expired"

                        ]

---

- **[F005]**            Test vector builder serializes envelope.json without sort_keys=True
- **Status:**           **OPEN** — Found during warp content-integrity multi-reviewer audit
                        2026-08-09. The inner manifest (signed payload) correctly uses sort_keys=True
                        via canonical_manifest_bytes(); the envelope itself is not signed.
- **Criticality:**      Non-critical; JSON object key order is semantically insignificant, and the
                        envelope is not part of the signed bytes. Consistency concern only.
- **Location:**         warp-manifest-vectors/_build_vectors.py:521
- **Issue:**            The envelope dict (manifest_b64, signature_ed25519, key_id) is serialized
                        without sort_keys=True. The inner manifest is correctly serialized with
                        sort_keys=True. The envelope is not signed — it's just the transport
                        container. The client parses it by key name, not by key order.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] For consistency with the rest of the corpus builder, add
                        sort_keys=True to the json.dumps call for envelope.json.
- **Code Sample:**      [

                        # warp-manifest-vectors/_build_vectors.py:521
                        (case_dir / "envelope.json").write_text(
                            json.dumps(envelope, indent=2) + "\n")

                        ]

---

- **[A012]**            In-memory lockout counters reset on server restart
- **Status:**           **OPEN** — Found during security-scanning audit of feat/1 branch
                        2026-08-09. Both activation lockout (SEC-021) and trial anchor per-IP rate
                        limiting (SEC-006) use process-local dictionaries that vanish on restart.
- **Criticality:**      Non-critical; the in-memory approach is a significant improvement over the
                        previous unbounded path. A restart-reset attack requires the attacker to
                        trigger a crash or deployment, which is a high bar but not impossible.
- **Location:**         Server/syxx_server/app.py:1586 (_ACTIVATE_FAILURES dict),
                        Server/syxx_server/app.py:1667 (_TRIAL_ANCHOR_IP_COUNTS dict)
- **Issue:**            Both the activation lockout and trial anchor per-IP rate limiting use
                        process-local dictionaries. A server restart, deployment, or crash resets
                        all counters to zero. An attacker who can trigger a restart (e.g., via DoS)
                        can reset their lockout count and continue brute-forcing indefinitely.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Persist failure counts to the SQLite kv table with a TTL equal to
                        the lockout window. The kv table already supports timestamped entries and
                        purge_expired already cleans them. This is a follow-up, not a blocker.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:1586
                        _ACTIVATE_FAILURES = {}  # ip -> (count, first_failure_time)

                        # Server/syxx_server/app.py:1667
                        _TRIAL_ANCHOR_IP_COUNTS = {}  # ip -> (count, day_start_time)

                        ]

---

- **[E009]**            Keygen API calls over configurable base URL lack HTTPS enforcement
- **Status:**           **OPEN** — Found during security-scanning audit of feat/1 branch
                        2026-08-09. The KEYGEN_BASE_URL env var is operator-settable; if set to
                        http://, the KEYGEN_ADMIN_TOKEN travels in cleartext.
- **Criticality:**      Non-critical; the operator already controls the token, so risk is limited to
                        accidental misconfiguration. Defense-in-depth hardening.
- **Location:**         Server/syxx_server/fulfillment.py:51,85,109;
                        Server/syxx_server/licensing.py:341,448
- **Issue:**            All urllib.request.Request calls to KEYGEN_BASE_URL carry the
                        KEYGEN_ADMIN_TOKEN as a Bearer header. If the operator sets
                        SYXX_KEYGEN_BASE_URL to an http:// URL (e.g., an internal mock server),
                        the admin token is transmitted in cleartext on the local network. The
                        # noqa: S310 comments correctly note the URL is "operator config, never
                        user input," but there is no runtime guard.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add a startup validation in config.py: if KEYGEN_BASE_URL is set
                        and does not start with https://, log a warning and refuse to start unless
                        SYXX_INSECURE_KEYGEN_URL=1 is set.
- **Code Sample:**      [

                        # Server/syxx_server/fulfillment.py:51
                        base = (config.KEYGEN_BASE_URL
                                or f"https://api.keygen.sh/v1/accounts/{config.KEYGEN_ACCOUNT_ID}")\
                            .rstrip("/")
                        req = urllib.request.Request(base + "/licenses", data=body, method="POST")
                        req.add_header("Authorization", "Bearer " + config.KEYGEN_ADMIN_TOKEN)

                        ]

---

- **[E010]**            _cap_rows f-string SQL construction gates on caller discipline only
- **Status:**           **OPEN** — Found during security-scanning audit of feat/1 branch
                        2026-08-09. The function accepts arbitrary table/column strings with no
                        runtime validation, relying entirely on caller discipline.
- **Criticality:**      Non-critical; currently only called from _purge_old with hardcoded
                        constants. A one-line assertion makes the invariant machine-checkable.
- **Location:**         Server/syxx_server/db.py:885 (_cap_rows function signature)
- **Issue:**            _cap_rows uses f"DELETE FROM {table} WHERE..." with # noqa: S608. The
                        function is currently only called from _purge_old with hardcoded
                        constants (passive_dns, audit, warp_cache). However, the function signature
                        accepts arbitrary strings and has no runtime validation that the arguments
                        are safe. A future developer adding _cap_rows(conn, user_input_table,
                        "col", 100) would introduce SQL injection.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add an assertion at the top of _cap_rows:
                        assert table in ("passive_dns", "audit", "warp_cache"),
                        f"unexpected table: {table}". One-line guard that makes the invariant
                        machine-checkable.
- **Code Sample:**      [

                        # Server/syxx_server/db.py:885-897
                        def _cap_rows(conn, table, order_col, max_rows):
                            """Delete the oldest rows ..."""
                            if max_rows <= 0:
                                return
                            row = conn.execute(
                                f"SELECT COUNT(*) c FROM {table}"  # noqa: S608
                            ).fetchone()
                            excess = (row["c"] if row else 0) - max_rows
                            if excess > 0:
                                conn.execute(
                                    f"DELETE FROM {table} WHERE rowid IN "
                                    f"(SELECT rowid FROM {table} ORDER BY {order_col} ASC LIMIT ?)",
                                    (excess,))

                        ]

---

- **[E011]**            logging_util event category uses f-string interpolation for caller data
- **Status:**           **OPEN** — Found during security-scanning audit of feat/1 branch
                        2026-08-09. The category parameter is constructed with f-strings from
                        caller-controlled data; acceptable as-is but fragile.
- **Criticality:**      Non-critical; reason is currently drawn from a controlled set
                        ("refund", "dispute", etc.). Risk is limited to future maintainers
                        adding new callers with attacker-controlled data.
- **Location:**         Server/syxx_server/fulfillment.py:140,227,260
- **Issue:**            The category parameter to logging_util.warn()/error() is constructed with
                        f-strings like f"stripe_{reason}". While reason is currently from a
                        controlled set, this pattern is fragile — a future developer adding a
                        caller passing attacker-controlled data into the category string could
                        enable log injection or metric cardinality explosion.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Document the constraint on logging_util functions that the
                        category parameter must be a static string literal. No code change
                        needed at this time.
- **Code Sample:**      [

                        # Server/syxx_server/fulfillment.py:140 (representative)
                        logging_util.warn(
                            f"stripe_{reason}",
                            event_id=event_id,
                            detail=f"Unhandled Stripe event type: {event_type}",
                        )

                        ]

---

- **[A013]**            IDN domain corruption in DNSSEC validator — `encode("ascii","replace")`
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Critical. Silently breaks DNSSEC validation for all internationalized
                        domain names. The failure mode is indistinguishable from "this zone
                        is not signed."
- **Location:**         Server/syxx_server/dnssec_validator.py:84
- **Issue:**            The `_nw` function (canonical DNS wire-format name encoder) uses
                        `domain.encode("ascii", "replace")` as a fallback when IDNA encoding
                        fails. The "replace" error handler substitutes every non-ASCII
                        character with `?` (0x3F). An IDN like `münchen.de` becomes
                        `m?nchen.de` on the wire — a completely different name that will
                        never match any DS/DNSKEY/RRSIG record. This silently breaks DNSSEC
                        validation for the entire non-ASCII namespace.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Raise `DNSError` instead of falling back to
                        `encode("ascii", "replace")`. The caller already handles `DNSError`
                        gracefully. The IDNA path should be the only path; if it fails, the
                        domain is unresolvable for DNSSEC purposes.
- **Code Sample:**      [

                        # Server/syxx_server/dnssec_validator.py:73-84
                        def _nw(domain):
                            try:
                                encoded = domain.encode("idna").decode("ascii")
                            except (UnicodeError, UnicodeDecodeError):
                                encoded = domain.encode("ascii", "replace")
                            return b"".join(
                                _lenprefixed(l) for l in encoded.split(".")
                            )

                        ]

---

- **[A014]**            backup() leaves stale .partial tmp files on error — no finally cleanup
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. Failed backups accumulate .partial files that could be mistaken
                        for restore points during disaster recovery. The test
                        `test_no_partial_file_is_left_behind` only covers the happy path.
- **Location:**         Server/syxx_server/db.py:986-998
- **Issue:**            The `backup()` function writes a snapshot to a `.partial` tmp file,
                        then atomically renames it via `os.replace()`. On error paths
                        (`src.backup(dst)` fails or `os.replace()` fails), the tmp file is
                        never cleaned up. There is no `finally` block that removes the tmp
                        file. Over time, failed backups accumulate `.partial` files.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add a `finally` block that removes the tmp file on error.
                        [Fix 2] Add error-path tests for both failure modes.
- **Code Sample:**      [

                        # Server/syxx_server/db.py:986-998 (simplified)
                        def backup(src, dst_dir, keep=10):
                            tmp = os.path.join(dst_dir, "backup.db.partial")
                            out = os.path.join(dst_dir, "backup.db")
                            src.backup(tmp)       # may raise
                            os.replace(tmp, out)  # may raise; tmp left behind

                        ]

---

- **[A015]**            app.py is a 3,425-line God module mixing 5 distinct concerns
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Critical. High merge-conflict surface; diff subsystem untestable in
                        isolation; impossible to reason about one concern without navigating
                        the entire HTTP handler.
- **Location:**         Server/syxx_server/app.py
- **Issue:**            The `Handler` class and module-level helpers mix HTTP routing, DNS
                        diff/comparison orchestration, Stripe checkout/purchase flow, leak
                        token verification, SEO meta tag injection, and static file serving.
                        `_diff_content` and `_diff_result` (165 lines) are defined at module
                        level in app.py but belong in a dedicated diff module. `_route_diff`
                        alone is ~120 lines.
- **Cross-ref. Delta:** Related to A020, A024, A028.
- **Fix(es):**          [Fix 1] Extract `_diff_content` and `_diff_result` into a dedicated
                        `diff.py` module. [Fix 2] Extract Stripe checkout into a `billing.py`
                        module. [Fix 3] Extract SEO injection into `seo.py` (consolidating
                        with existing seo.py). [Fix 4] Keep app.py as the HTTP router only,
                        delegating to extracted modules.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:265-354 (representative)
                        async def _diff_content(client_ip, domain, clean_scripts):
                            """Compare client-submitted scripts vs clean vantage."""
                            # ~90 lines of diff logic in the HTTP routing module
                            ...
                        async def _diff_result(client_ip, domain, answers, author):
                            """Compare client DNS answers vs authoritative."""
                            # ~75 lines of diff logic in the HTTP routing module
                            ...

                        ]

---

- **[A016]**            dnssec_validator.py has zero test coverage — 785-line crypto module
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Critical. A 785-line cryptographic validation module with no tests.
                        Any regression in DNSSEC validation is undetectable.
- **Location:**         Server/syxx_server/dnssec_validator.py (entire file, 785 lines)
- **Issue:**            Not a single test file exists for the DNSSEC chain-of-trust validator.
                        `_parse`, `_q`, `_rr_raw`, `_dsm`, `_vsig`, `_cv`, `_chk_ans`,
                        `validate_chain`, and `verify_denial_of_existence` are all untested.
                        The `_nw` IDN corruption defect (A013) would have been caught by a
                        basic unit test.
- **Cross-ref. Delta:** A013 (IDN corruption in same module).
- **Fix(es):**          [Fix 1] Create `test_dnssec_validator.py` with tests for `_nw` IDN
                        encoding, `_parse` response parsing, `_dsm` DS matching, `_vsig`
                        signature validation, `validate_chain` happy path, and
                        `verify_denial_of_existence`. [Fix 2] Use known test vectors
                        (RFC-compliant DNSSEC-signed zones with published RRSIG/DS records).
- **Code Sample:**      [

                        # No test file exists for this module
                        # Server/syxx_server/dnssec_validator.py:1-785
                        # Contains: _nw, _parse, _q, _rr_raw, _dsm, _vsig, _cv,
                        #           _chk_ans, _sq, _resolve_ns, _ds_at_parent,
                        #           validate_chain, verify_denial_of_existence

                        ]

---

- **[A017]**            DNS wire protocol duplicated between dnssec_validator.py and resolver.py
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. Two code paths to audit for DNS security; fixes to one may not
                        be applied to the other. ~200 lines of duplicated logic.
- **Location:**         Server/syxx_server/dnssec_validator.py:89-200;
                        Server/syxx_server/resolver.py:638-750
- **Issue:**            `dnssec_validator.py` has its own DNS query and response parsing
                        infrastructure that parallels `resolver.py`. Both implement query
                        construction, TCP fallback on truncation, section parsing, and record
                        reading. `dnssec_validator` already imports `_read_name`, `_build_query`,
                        `_addr_family`, `_in_bailiwick`, `_recv_exact`, and
                        `_rrsig_window_valid` from `resolver` — but reimplements the entire
                        query/parse layer. These two implementations will inevitably diverge
                        on security fixes.
- **Cross-ref. Delta:** A030 (no unified DNS query interface).
- **Fix(es):**          [Fix 1] Extract shared DNS query/parse into a single implementation
                        in `resolver.py`. [Fix 2] Have `dnssec_validator` use `resolver` for
                        all transport-layer operations, keeping only DNSSEC-specific
                        validation logic.
- **Code Sample:**      [

                        # dnssec_validator.py:110 (own parser)
                        def _parse(b):
                            """Parse DNS response into sections."""
                            ...
                        # resolver.py:681 (independent parser)
                        def _parse_sections(b):
                            """Parse DNS response into sections."""
                            ...

                        ]

---

- **[A018]**            db.py is a 1,351-line data access monolith with no repository classes
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. Any schema change to one domain risks breaking unrelated
                        domains. The module is too large to review in a single pass.
- **Location:**         Server/syxx_server/db.py (entire file, 1,351 lines)
- **Issue:**            A single module handles: connection pooling, schema definition,
                        migrations, CRUD for 10+ tables (licenses, leak tokens, leak
                        observations, passive DNS, warp cache, audit, beta signups, trial
                        anchors, API keys, monitors, monitor results), HMAC-based license
                        integrity, env secret validation, backup/restore, and data retention
                        purging. There are no repository classes or domain-specific data
                        access objects.
- **Cross-ref. Delta:** A014 (backup tmp file leak), A021 (circular dependency).
- **Fix(es):**          [Fix 1] Split into domain-specific repository modules:
                        `license_repo.py`, `leak_repo.py`, `monitor_repo.py`, etc.
                        [Fix 2] Each repository owns its schema, migrations, and CRUD.
                        [Fix 3] Keep `db.py` as the connection pool and migration runner.
- **Code Sample:**      [

                        # Server/syxx_server/db.py — single module handles:
                        # - _init_db()            # schema for all tables
                        # - _migrate()            # migrations for all tables
                        # - license_*()           # 6 license CRUD functions
                        # - leak_token_*()        # 4 leak token functions
                        # - leak_observation_*()  # 5 leak observation functions
                        # - passive_dns_*()       # 3 passive DNS functions
                        # - warp_cache_*()        # 3 warp cache functions
                        # - audit_*()             # 3 audit functions
                        # - monitor_*()           # 5 monitor functions
                        # - backup() / restore()  # 2 backup functions
                        # - _cap_rows()           # 1 retention function

                        ]

---

- **[A019]**            No architecture documentation for Server — no ADRs, C4 diagrams, or
                        module dependency graphs
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. New contributors cannot understand the system without reading
                        all 9,355 lines of source code. The DNS diff pipeline is particularly
                        opaque.
- **Location:**         Server/ (no architecture docs exist)
- **Issue:**            `Server/docs/` contains only `api.html` and `help.html` (user-facing
                        docs). There are no Architecture Decision Records, C4 diagrams,
                        module dependency graphs, or data flow diagrams for the
                        diff/investigation pipeline. `SERVER_IMPLEMENTATION_PLAN.md` and
                        `SERVER_DELTAS.md` are planning documents, not architecture docs.
- **Cross-ref. Delta:** F006 (macOS no architecture docs).
- **Fix(es):**          [Fix 1] Create `Server/ARCHITECTURE.md` with: module dependency graph,
                        data flow from DNS query → investigate → diff → response, C4
                        Container diagram, and ADRs for key decisions (SQLite choice, HMAC
                        license scheme, collector-attested leak model).
- **Code Sample:**      [

                        # No architecture documentation exists for the Server.
                        # The DNS diff pipeline is:
                        #   app.py Handler → investigate.py → resolver.py +
                        #   dnssec_validator.py → app.py _diff_result → client
                        # This flow is discoverable only by reading all source files.

                        ]

---

- **[A020]**            _diff_content has no direct unit tests — tested only indirectly via
                        /api/diff route handler
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. Refactoring _diff_content out of app.py (A015) is risky without
                        direct test coverage.
- **Location:**         Server/syxx_server/app.py:265-354
- **Issue:**            The diff content comparison function (~90 lines) that compares
                        client-submitted scripts/inline hashes against the clean vantage is
                        tested only indirectly through the `/api/diff` HTTP route handler.
                        No direct unit tests exist for edge cases: empty clean scripts,
                        injected host detection, inline hash comparison, truncated baseline
                        handling, or the `FetchError` path.
- **Cross-ref. Delta:** A015 (app.py God module), A024 (different comparison logic).
- **Fix(es):**          [Fix 1] Extract _diff_content into a dedicated module first.
                        [Fix 2] Add direct unit tests for: empty scripts, injected host
                        detection, hash comparison, truncated baseline, FetchError.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:265-354
                        # _diff_content() is tested only through HTTP requests
                        # No direct unit test exists for this function

                        ]

---

- **[A021]**            Circular dependency workarounds in config.py, db.py, licensing.py
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. Refactoring any of these three modules risks import-time
                        failures that only surface at runtime.
- **Location:**         Server/syxx_server/config.py:39;
                        Server/syxx_server/db.py:485,497;
                        Server/syxx_server/licensing.py:521
- **Issue:**            Three modules use lazy imports to avoid circular imports:
                        `config.py` imports `logging_util` inside `_b()`, `db.py` imports
                        `licensing` inside HMAC key functions, and `licensing.py` imports
                        `db` inside a function. The dependency graph has a cycle:
                        config → logging_util → config and db → licensing → db.
- **Cross-ref. Delta:** A018 (db.py monolith).
- **Fix(es):**          [Fix 1] Extract shared dependencies into a `core.py` or `types.py`
                        that both modules can import without cycles. [Fix 2] Use dependency
                        injection — pass the HMAC secret and db handle as parameters rather
                        than importing them.
- **Code Sample:**      [

                        # config.py:39
                        def _b(key):
                            from . import logging_util  # lazy import to avoid cycle
                            ...
                        # db.py:485
                        def _license_hmac_key_v1():
                            from . import licensing  # lazy import to avoid cycle
                            ...
                        # licensing.py:521
                        def _validate_license(conn, key):
                            from . import db  # lazy import to avoid cycle
                            ...

                        ]

---

- **[A022]**            12 type safety suppression overrides in pyproject.toml mask real errors
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. Real bugs that a type checker would catch are being silenced.
                        The dnssec_validator.py has 5 distinct rule categories suppressed.
- **Location:**         Server/pyproject.toml (12 [[tool.ty.overrides]] blocks)
- **Issue:**            Six modules have type errors suppressed via pyproject.toml overrides
                        rather than fixed: `app.py`, `dnssec_validator.py`, `leak.py`,
                        `resolver.py`, `netutil.py`, `whois_lookup.py`. The
                        `dnssec_validator.py` overrides are most concerning — 5 distinct rule
                        categories, indicating the module was never brought to clean type
                        checking. The `leak.py` `missing-argument` override is suspicious.
- **Cross-ref. Delta:** A013, A016 (dnssec_validator issues).
- **Fix(es):**          [Fix 1] Fix the underlying type errors rather than suppressing them.
                        [Fix 2] Remove overrides one at a time, fixing the revealed errors.
                        [Fix 3] Add `ty` (type checker) to CI to prevent new suppressions.
- **Code Sample:**      [

                        # Server/pyproject.toml (representative)
                        [[tool.ty.overrides]]
                        module = "syxx_server.dnssec_validator"
                        suppress = [
                            "not-subscriptable",
                            "invalid-argument-type",
                            "too-many-positional-arguments",
                            "call-non-callable",
                            "unresolved-attribute",
                        ]

                        ]

---

- **[A023]**            _env_int helper duplicated across 3 modules — copy-paste pattern
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Low. Signals that modules don't trust the shared config interface.
- **Location:**         Server/syxx_server/app.py:233;
                        Server/syxx_server/investigate.py:32;
                        Server/syxx_server/db.py:31
- **Issue:**            Three modules define an identical 2-line wrapper around `config._i()`:
                        `def _env_int(name, default): return config._i(name, default)`.
                        `passivedns.py:14` defines its own `_i()` wrapper. This is textbook
                        copy-paste — it indicates modules are drifting toward independent
                        config reading.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Export `_env_int` from `config.py` and have all modules
                        import it. [Fix 2] Standardize on `config._i()` directly.
- **Code Sample:**      [

                        # app.py:233
                        def _env_int(name, default):
                            return config._i(name, default)
                        # investigate.py:32
                        def _env_int(name, default):
                            return config._i(name, default)
                        # db.py:31
                        def _env_int(name, default):
                            return config._i(name, default)

                        ]

---

- **[A024]**            _diff_content and _route_diff use different comparison logic for the
                        same tri-state assessment
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. Two comparison paths that produce the same tri-state result
                        use different criteria and data structures.
- **Location:**         Server/syxx_server/app.py:265-354 vs app.py:2062-2180
- **Issue:**            `_diff_content` compares scripts/inline hashes from the client vs the
                        clean vantage. `_route_diff` compares DNS answers from the client vs
                        authoritative/clean vantage. Both compute an `assessment` tri-state
                        result, but they use different criteria and different data structures.
                        `_diff_result` folds them together, but the two comparison paths are
                        not obviously consistent.
- **Cross-ref. Delta:** A015, A020.
- **Fix(es):**          [Fix 1] Extract both into a shared `diff.py` module.
                        [Fix 2] Define a
                        common tri-state comparison interface. [Fix 3] Add tests that verify
                        both paths produce consistent results for the same input.
- **Code Sample:**      [

                        # app.py:265 — content diff
                        async def _diff_content(client_ip, domain, clean_scripts):
                            # compares scripts/inline hashes
                            ...
                        # app.py:2062 — DNS answer diff
                        async def _route_diff(self, client_ip, domain, answers, author):
                            # compares DNS answers
                            ...

                        ]

---

- **[A025]**            seo.py duplicates pricing constants from app.py — not shared via config
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Low. Changing pricing requires updating two files.
- **Location:**         Server/syxx_server/seo.py:33-34;
                        Server/syxx_server/app.py:178-179
- **Issue:**            Both `seo.py` and `app.py` read `SYXX_SEO_PRICE_MONTHLY` and
                        `SYXX_SEO_PRICE_YEARLY` from the environment at module load time with
                        the same defaults. The constants are duplicated rather than shared
                        through `config.py`.
- **Cross-ref. Delta:** A029 (config.py mixes product catalog).
- **Fix(es):**          [Fix 1] Move pricing constants to `config.py` as the single source of
                        truth. [Fix 2] Have both `app.py` and `seo.py` import from config.
- **Code Sample:**      [

                        # seo.py:33-34
                        SYXX_SEO_PRICE_MONTHLY = os.environ.get("SYXX_SEO_PRICE_MONTHLY", "3.95")
                        SYXX_SEO_PRICE_YEARLY = os.environ.get("SYXX_SEO_PRICE_YEARLY", "19.95")
                        # app.py:178-179
                        SYXX_SEO_PRICE_MONTHLY = os.environ.get("SYXX_SEO_PRICE_MONTHLY", "3.95")
                        SYXX_SEO_PRICE_YEARLY = os.environ.get("SYXX_SEO_PRICE_YEARLY", "19.95")

                        ]

---

- **[A026]**            _dnssec_observed deeply nested — ~120 lines, 5 levels of nesting,
                        3 transport branches
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. Difficult to test in isolation; high cognitive load.
- **Location:**         Server/syxx_server/investigate.py:230-350
- **Issue:**            The `_dnssec_observed` function has 5 levels of nesting and 3 distinct
                        transport branches (DoH, UDP, system), each with its own result
                        shaping. The function is ~120 lines and would benefit from extraction
                        into a dedicated DNSSEC observation module.
- **Cross-ref. Delta:** A017 (DNS wire protocol duplication).
- **Fix(es):**          [Fix 1] Extract into a dedicated `dnssec_observation.py` module.
                        [Fix 2] Each transport branch becomes a separate, testable function.
- **Code Sample:**      [

                        # Server/syxx_server/investigate.py:230-350 (simplified)
                        async def _dnssec_observed(domain, nameservers, ...):
                            for ns in nameservers:
                                try:
                                    # DoH branch
                                    ...
                                except:
                                    try:
                                        # UDP branch
                                        ...
                                    except:
                                        # System resolver branch
                                        ...

                        ]

---

- **[A027]**            resolve_authoritative — 6 levels of nesting, ~160 lines, 8 return sites
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. The most complex function in the codebase; difficult to
                        maintain or debug.
- **Location:**         Server/syxx_server/resolver.py:830-990
- **Issue:**            The authoritative walk function is a delegation-following loop with
                        glue record resolution, bailiwick validation, deadline checking,
                        sub-walk recursion, and parse-completeness gating. It has at least
                        6 levels of nesting and 8 return sites. The glue-resolution sub-logic
                        should be extracted into a separate function.
- **Cross-ref. Delta:** A017 (DNS wire protocol duplication).
- **Fix(es):**          [Fix 1] Extract glue record resolution into a dedicated function.
                        [Fix 2] Extract bailiwick validation into a dedicated function.
                        [Fix 3] Reduce nesting by using early returns for error conditions.
- **Code Sample:**      [

                        # Server/syxx_server/resolver.py:830-990 (simplified)
                        async def resolve_authoritative(domain, rtype, ...):
                            while True:
                                for ns in nameservers:
                                    # resolve glue
                                    # validate bailiwick
                                    # check deadline
                                    # recurse into sub-walk
                                    if complete:
                                        return
                                    # next iteration

                        ]

---

- **[A028]**            _diff_content lives in app.py but depends on warp.build_baseline
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. Creates a dependency from HTTP routing to the Warp/baseline
                        module. The diff logic should be in a dedicated module.
- **Location:**         Server/syxx_server/app.py:265-354
- **Issue:**            The diff content function is defined in `app.py` but depends on
                        `warp.build_baseline()` and `warp.parse_assets()`. This creates a
                        dependency from the HTTP routing module to the Warp/baseline module.
                        The diff logic should be in a dedicated `diff.py` module that both
                        `app.py` and `warp.py` can import.
- **Cross-ref. Delta:** A015, A020, A024.
- **Fix(es):**          [Fix 1] Extract into `diff.py` along with `_diff_result` and
                        `_route_diff`. [Fix 2] `app.py` delegates to `diff.py` for all
                        comparison logic.
- **Code Sample:**      [

                        # app.py:265 — diff logic in HTTP module
                        async def _diff_content(client_ip, domain, clean_scripts):
                            baseline = await warp.build_baseline(domain)
                            ...

                        ]

---

- **[A029]**            config.py mixes product catalog (Stripe SKUs) with infrastructure config
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. Changing a Stripe price requires touching the config module
                        that also controls server bind addresses and timeouts.
- **Location:**         Server/syxx_server/config.py (624 lines)
- **Issue:**            The `config.py` module mixes infrastructure config (HTTP host, DNS
                        port, TLS certs), Stripe product catalog (SKU table, price IDs,
                        product IDs), Keygen license policy mappings, data retention windows,
                        and SEO pricing constants. `STRIPE_SKUS` embeds live Stripe product
                        IDs and a `_sku()` factory function. This is a product catalog, not
                        configuration.
- **Cross-ref. Delta:** A025 (seo.py pricing duplication).
- **Fix(es):**          [Fix 1] Extract `STRIPE_SKUS` and `_sku()` into a dedicated
                        `catalog.py` module. [Fix 2] Keep `config.py` for infrastructure
                        config only (hosts, ports, TLS, timeouts, retention).
- **Code Sample:**      [

                        # Server/syxx_server/config.py:280-300 (representative)
                        STRIPE_SKUS = {
                            "monthly": {
                                "price_id": "price_xxx",
                                "product_id": "prod_xxx",
                            },
                            "yearly": {
                                "price_id": "price_yyy",
                                "product_id": "prod_yyy",
                            },
                        }

                        ]

---

- **[A030]**            No unified DNS query interface — 5 different query paths for 3 transports
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. Adding a new DNS transport requires understanding all 5
                        existing paths to avoid duplicating yet another variant.
- **Location:**         Server/syxx_server/resolver.py;
                        Server/syxx_server/dnssec_validator.py;
                        Server/syxx_server/dot_listener.py
- **Issue:**            The codebase has at least 5 different query paths: `resolver.query()`
                        (UDP+TCP), `resolver.query_doh()` (DoH JSON), `resolver.query_system()`
                        (getaddrinfo), `resolver._query_sections()` (section-aware), and
                        `dnssec_validator._q()` (DNSSEC-specific with RD=0, DO=1). There is
                        no common interface or abstract base class for DNS queries. Each path
                        has its own timeout handling, error wrapping, and response parsing.
- **Cross-ref. Delta:** A017 (DNS wire protocol duplication).
- **Fix(es):**          [Fix 1] Define a `DNSQuery` protocol/ABC with `query(domain, rtype,
                        flags)` → `DNSResponse`. [Fix 2] Implement concrete transports
                        (UDP, TCP, DoH, System) conforming to the protocol. [Fix 3] Have
                        `dnssec_validator` use the shared transport layer.
- **Code Sample:**      [

                        # 5 different query paths:
                        # resolver.query()          — UDP with TCP fallback
                        # resolver.query_doh()      — DNS-over-HTTPS JSON API
                        # resolver.query_system()   — getaddrinfo
                        # resolver._query_sections() — section-aware authoritative
                        # dnssec_validator._q()     — DNSSEC-specific (RD=0, DO=1)

                        ]

---

- **[A031]**            Tests use unittest.TestCase alongside pytest inconsistently
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Low. Inconsistent patterns make tests harder to write and maintain.
- **Location:**         Server/tests/ (~15 files)
- **Issue:**            The test suite is split between `unittest.TestCase` (with manual
                        `setUp`/`tearDown`) and pytest-native tests. `conftest.py` sets up a
                        session-level shared DB using `tempfile.mkstemp` and
                        `pytest_unconfigure` rather than pytest fixtures. Many tests subclass
                        `unittest.TestCase`, which prevents use of pytest fixtures,
                        parametrization, and markers.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Migrate `unittest.TestCase` tests to pytest-native using
                        fixtures. [Fix 2] Use `pytest.fixture` for DB setup instead of manual
                        `conftest.py` session management. [Fix 3] Standardize on pytest
                        throughout the test suite.
- **Code Sample:**      [

                        # Server/tests/test_something.py (representative)
                        class TestSomething(unittest.TestCase):
                            def setUp(self):
                                self.db = setup_test_db()
                            def test_foo(self):
                                result = foo(self.db)
                                self.assertEqual(result, expected)

                        # Should be:
                        # def test_foo(db_fixture):
                        #     result = foo(db_fixture)
                        #     assert result == expected

                        ]

---

- **[B002]**            208 quarantined test methods in macOS _quarantine/ — synthesis engine
                        has zero regression coverage
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Critical. The synthesis engine, matrix engine, and scoring logic are
                        the core of Syxx's DNS integrity analysis. Having 208 test methods
                        quarantined means the most algorithmic complexity in the codebase has
                        zero regression coverage.
- **Location:**         macOS/Tests/_quarantine/ (19 active test files, 208 test methods)
- **Issue:**            The `_quarantine/` directory contains 19 test files that are not
                        compiled or executed. Includes `SynthesisEngineTests.swift` (574
                        lines), `SynthesizerDatapointWiringTests.swift` (649 lines),
                        `FusionRuleBakeoffTests.swift` (12,446 lines),
                        `Stage0GoldenCorpusTests.swift` (10,919 lines), and
                        `ScorersContractTests.swift` (13,410 lines). Additionally, 23 files
                        are duplicates (`.phase32-active-dup`) or compile-broken
                        (`.phase3-compile-break`).
- **Cross-ref. Delta:** D008 (duplicate test files).
- **Fix(es):**          [Fix 1] Re-enable the 19 active quarantined test files — fix
                        compilation issues, run them, fix failures. [Fix 2] Delete the 23
                        duplicate/compile-broken files. [Fix 3] Add CI gate to prevent
                        future test quarantines.
- **Code Sample:**      [

                        # macOS/Tests/_quarantine/ — 19 test files not running:
                        # SynthesisEngineTests.swift (574 lines)
                        # SynthesizerDatapointWiringTests.swift (649 lines)
                        # FusionRuleBakeoffTests.swift (12,446 lines)
                        # Stage0GoldenCorpusTests.swift (10,919 lines)
                        # ScorersContractTests.swift (13,410 lines)
                        # ... 14 more files with 208 total test methods

                        ]

---

- **[B003]**            Synthesizer.cs — 15 duplicated switch (probe.Status) blocks across 6
                        EvidenceAdapter methods
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Critical. Adding a new probe or changing status precedence requires
                        touching every adapter method. The Prophet method alone has 13 switch
                        blocks.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs (1,073 lines)
- **Issue:**            Six methods (`Prophet`, `Mirror`, `Portal`, `Glutton`, `Cage`,
                        `Witness`) spanning ~780 lines each follow the identical pattern:
                        `List<EvidenceItem>` + `List<SectionStatus>` → 3–13 switch blocks on
                        probe statuses → `CombineWithEvidence(...)`. The pattern is
                        mechanically identical — every probe type could implement a
                        `(SectionStatus, EvidenceItem?) Evaluate()` interface.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Extract a `ProbeEvaluator` interface with a single
                        `Evaluate()` method. [Fix 2] Each probe type implements the interface.
                        [Fix 3] The adapter reduces to a single loop over evaluators.
- **Code Sample:**      [

                        // Synthesizer.cs — 15 instances of this pattern:
                        switch (probe.Status) {
                            case XStatus.Y: subs.Add(SectionStatus.Z); break;
                            case XStatus.W:
                                ev.Add(EvidenceItem.Create(...));
                                subs.Add(SectionStatus.V);
                                break;
                            // ... 3-12 more cases per switch
                        }

                        ]

---

- **[B004]**            MatrixDeterminativeEngine.cs — 1,319-line port of Swift engine with
                        "Swift is the SPECIFICATION" comment
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Critical. Any change to diagnosis logic requires keeping two 1,300+
                        line files in lockstep. Type definitions mixed with domain logic.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs
                        (1,319 lines)
- **Issue:**            Single class preceded by ~800 lines of type definitions (records,
                        enums, JSON converters, exceptions). The `Evaluate` method is heavily
                        nested with multi-level scoring, coverage/fit/margin calculations,
                        contradiction handling, and tiered diagnosis selection. The comment
                        "The Swift file is the SPECIFICATION" means any change requires
                        lockstep porting to the Swift version (B009).
- **Cross-ref. Delta:** B009 (Swift counterpart).
- **Fix(es):**          [Fix 1] Extract type definitions into a separate file.
                        [Fix 2] Extract
                        scoring phases (coverage, fit, exclusion, threshold) into composable
                        methods. [Fix 3] Externalize the diagnosis spec (see B007).
- **Code Sample:**      [

                        // MatrixDeterminativeEngine.cs:1-808 — type definitions
                        // MatrixDeterminativeEngine.cs:808-1319 — engine logic
                        // Comment: "The Swift file is the SPECIFICATION."
                        // Both files must be kept in lockstep.

                        ]

---

- **[B005]**            DNSResolver.cs — 1,289-line static class mixing transport, parsing,
                        validation, and encoding
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Critical. Cannot test any concern in isolation. UDP and DoH paths
                        share ~80% identical response validation but are copy-pasted.
- **Location:**         Windows/dotNET/Syxx.Kit/DNSResolver.cs (1,289 lines)
- **Issue:**            Single static class contains: UDP query with retry, TCP fallback,
                        DoH query with per-call timeout, secure resolution, packet building
                        (`MakeQueryPacket`), packet parsing (`ParseResponsePacketDetailed`),
                        response question validation, IDNA encoding, domain name
                        canonicalization, registrable domain extraction, and EDNS0/ECS option
                        building. `QueryUDPDetailedAsync` alone is ~150 lines.
- **Cross-ref. Delta:** B008 (Swift counterpart).
- **Fix(es):**          [Fix 1] Split into `DNSWireFormat` (parsing), `DNSTransport` (UDP/TCP/
                        DoH), and `DNSResolver` (orchestration). [Fix 2] Extract common
                        response validation into a shared method used by both UDP and DoH.
- **Code Sample:**      [

                        // DNSResolver.cs — single static class containing:
                        // QueryUDPDetailedAsync       (~150 lines)
                        // QueryDoHDetailedAsync        (~80 lines)
                        // MakeQueryPacket              (~50 lines)
                        // ParseResponsePacketDetailed  (~300 lines)
                        // + IDNA, canonicalization, EDNS0, ECS, retry, fallback

                        ]

---

- **[B006]**            LeakDetector.cs — 1,092-line monolith mixing HTTP, TLS, Ed25519,
                        error formatting, and 6 enums
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Critical. Business logic is inseparable from the HTTP pipeline.
                        Changing user-facing error messages requires touching the same class
                        as the Ed25519 signature verification.
- **Location:**         Windows/dotNET/Syxx.Kit/LeakDetector.cs (1,092 lines)
- **Issue:**            Single file contains: `LeakDetector` static class (~600 lines),
                        `CollectorFailure` class (~200 lines with Reason/Cause/Consequence
                        string formatting), `LeakTestResult` class (~80 lines), and 6 enum
                        definitions. `RunCollectorTestAsync` is ~200 lines. The file mixes
                        HTTP client construction, TLS pinning, token request/verify API calls,
                        Ed25519 signature verification, result derivation, and user-facing
                        error message strings.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Extract enums into their own files.
                        [Fix 2] Extract HTTP
                        pipeline into a separate class. [Fix 3] Extract `CollectorFailure`
                        into its own file with user-facing strings separated from
                        classification logic.
- **Code Sample:**      [

                        // LeakDetector.cs — single file containing:
                        // LeakDetector static class      (~600 lines)
                        // CollectorFailure                (~200 lines)
                        // LeakTestResult                  (~80 lines)
                        // 6 enums: LeakResult, LeakSignatureVerification,
                        //   CollectorStep, CollectorFailureKind,
                        //   CollectorTransportPinning, CollectorPinning

                        ]

---

- **[B007]**            SyxxMatrixIntegration.cs — 200-line inline BuildDefaultSpec() with ~100
                        uncalibrated constants
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. These ~100 constants are "UNCALIBRATED expert constants" (per
                        the code comment) and will need tuning. Changing one requires
                        recompiling the entire engine.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/SyxxMatrixIntegration.cs (485 lines)
- **Issue:**            `SyxxMatrixBuilder.BuildDefaultSpec()` is ~200 lines of inline object
                        construction for 8 diagnosis specs, each with 3–8 indicator rules
                        containing `ExpectedStrength`, `Weight`, `EvidenceClass`,
                        `IndependenceGroup`, `DecayExponent`. The `DefaultSpecJson()` method
                        already produces the canonical JSON form — loading from that file
                        would make tuning a configuration change rather than a code change.
- **Cross-ref. Delta:** B004 (MatrixDeterminativeEngine).
- **Fix(es):**          [Fix 1] Load the diagnosis spec from the JSON produced by
                        `DefaultSpecJson()` rather than building it inline. [Fix 2] Make
                        the JSON file path configurable for tuning experiments.
- **Code Sample:**      [

                        // SyxxMatrixIntegration.cs:200-400
                        public static SyxxMatrixSpec BuildDefaultSpec()
                        {
                            // ~200 lines of inline object construction
                            // ~100 uncalibrated constants
                            // Comment: "UNCALIBRATED expert constants"
                            ...
                        }
                        // DefaultSpecJson() already exists — produces the same spec

                        ]

---

- **[B008]**            DNSResolver.swift — 1,698-line monolith mixing transport and wire format
                        parsing in one file
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. The wire-format parser is independently testable but is coupled
                        to the transport layer. Changing DoH URL format requires touching the
                        same file as the name decompressor.
- **Location:**         macOS/Sources/SyxxKit/DNSResolver.swift (1,698 lines)
- **Issue:**            The `DNSResolver` enum packs UDP query construction, TCP fallback,
                        DoH resolution, raw DNS wire-format parsing (name decompression, EDNS0
                        OPT parsing, record extraction), response validation, and telemetry
                        redaction into a single file. `parseResponsePacketDetailed` alone is
                        ~300 lines with deeply nested name-decompression logic and jump-budget
                        tracking.
- **Cross-ref. Delta:** B005 (C# counterpart).
- **Fix(es):**          [Fix 1] Split into `DNSWireFormat` (parsing), `DNSTransport` (UDP/TCP/
                        DoH), and `DNSResolver` (orchestration). [Fix 2] The wire-format
                        parser should be a standalone module with its own test suite.
- **Code Sample:**      [

                        // DNSResolver.swift — single enum containing:
                        // parseResponsePacketDetailed  (~300 lines)
                        // parseName                    (~100 lines)
                        // sendUDP / sendTCP            (~200 lines)
                        // queryDoH / queryDoHDetailed  (~200 lines)
                        // resolveSecurely              (~100 lines)
                        // + EDNS0, ECS, telemetry, validation

                        ]

---

- **[B009]**            MatrixDeterminativeEngine.swift — 1,068 lines, deeply nested scoring
                        logic, 14-parameter ThresholdProfile init
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. The scoring algorithm is difficult to reason about without
                        reading the entire ~400-line `evaluate()` method. The 14-parameter
                        init is easy to misorder.
- **Location:**         macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift
                        (1,068 lines)
- **Issue:**            The `evaluate()` method is ~400 lines with deeply nested loops over
                        diagnosis rules, indicator mappings, exclusion checks, and threshold
                        comparisons. `ThresholdProfile` has 14 parameters. While well-tested
                        (884 lines of active tests), the code is difficult to extend.
- **Cross-ref. Delta:** B004 (C# counterpart).
- **Fix(es):**          [Fix 1] Extract the scoring pipeline into smaller composable phases
                        (coverage calculation, fit scoring, exclusion gating, threshold
                        application). [Fix 2] Use a builder pattern for `ThresholdProfile`.
- **Code Sample:**      [

                        // MatrixDeterminativeEngine.swift: evaluate() ~400 lines
                        // ThresholdProfile(14 parameters)
                        // Deeply nested loops over:
                        //   diagnosis rules → indicator mappings →
                        //   exclusion checks → threshold comparisons

                        ]

---

- **[B010]**            SynthesisEngine incomplete indicator mapping — 4 clean-support
                        indicators intentionally not wired
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
                        Note: this was partially addressed by the mappableIndicators fix
                        (commit 2649e25 on 2026-08-09), but the underlying gap remains.
- **Criticality:**      Medium. The engine cannot affirm cleanliness for benign runs because
                        the affirmative-clean signals are unwired. Deliberate incompleteness
                        that makes the product less useful.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs:180-230
- **Issue:**            The `IndicatorMapping` dictionary maps only 15 `EvidenceId` →
                        `SyxxIndicator` pairs. Four clean-support indicators
                        (`resolverAgreement`, `routeClean`, `trustedWitnessOK`,
                        `localEvidenceSealValid`) are explicitly documented as "intentionally
                        NOT wired" — meaning a benign run can never reach `.clean`, only
                        `.inconclusive`. The gap is documented but unresolved.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Wire the 4 clean-support indicators.
                        [Fix 2] Add tests that
                        verify clean runs can reach `.clean` when all indicators are positive.
                        [Fix 3] Gate on the indicators being properly calibrated before
                        enabling.
- **Code Sample:**      [

                        // SynthesisEngine.cs:180-230
                        // IndicatorMapping: 15 EvidenceId → SyxxIndicator pairs
                        // 4 indicators intentionally NOT wired:
                        //   resolverAgreement, routeClean, trustedWitnessOK,
                        //   localEvidenceSealValid
                        // Comment: "the honest posture (inconclusive over unearned
                        //   secure) until those affirmative-clean signals are wired"

                        ]

---

- **[B011]**            No integration tests on macOS — all 889 active tests are unit tests
                        with mocks
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. The core value proposition — detecting DNS tampering — cannot
                        be verified by the test suite. A regression in the DNS wire-format
                        parser or synthesis engine would pass all unit tests but break the
                        product.
- **Location:**         macOS/Tests/ (889 test methods, all unit tests)
- **Issue:**            All 889 active test methods are unit tests. There are no tests that
                        resolve DNS queries against a real resolver, no tests that verify the
                        Cage capture pipeline end-to-end, and no tests that exercise the full
                        synthesis pipeline from raw DNS responses to a `Conclusion`.
- **Cross-ref. Delta:** B002 (quarantined tests), B012 (no integration tests on any platform).
- **Fix(es):**          [Fix 1] Add integration tests against a known-good public resolver
                        (8.8.8.8) for basic DNS resolution. [Fix 2] Add integration tests
                        against a test matrix of known probe inputs for the synthesis engine.
                        [Fix 3] Gate integration tests behind an environment flag.
- **Code Sample:**      [

                        // No integration tests exist.
                        // All 889 tests use mocks for DNS resolution.
                        // The full pipeline (DNS query → probe → synthesis →
                        //   conclusion) is never exercised by tests.

                        ]

---

- **[B012]**            No integration tests on any platform — cannot verify DNS tampering
                        detection end-to-end
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. This is the single most important testing gap. The product's
                        core mission cannot be verified by automated tests.
- **Location:**         All platforms (macOS, Windows, Linux, Server)
- **Issue:**            No platform has integration tests that exercise the full pipeline:
                        DNS query → probe evaluation → synthesis engine → conclusion.
                        Unit tests verify individual components, but the end-to-end detection
                        of DNS tampering is never verified programmatically.
- **Cross-ref. Delta:** B011 (macOS integration tests), B002 (quarantined tests).
- **Fix(es):**          [Fix 1] Create a test matrix of known DNS inputs (clean, tampered,
                        censored, spoofed). [Fix 2] Build integration tests that run the full
                        pipeline against these inputs. [Fix 3] Gate on an environment flag and
                        run in CI on a schedule.
- **Code Sample:**      [

                        # No integration tests exist on any platform.
                        # The full pipeline is:
                        #   DNS query → parse response → probe evaluation →
                        #   evidence collection → synthesis engine → conclusion
                        # This is never verified end-to-end by automated tests.

                        ]

---

- **[B013]**            _diff_content lives in app.py but belongs in a dedicated diff module
                        alongside warp.py dependencies
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. The diff content function is in the HTTP routing module but
                        depends on `warp.build_baseline()` — a cross-cutting dependency.
- **Location:**         Server/syxx_server/app.py:265-354
- **Issue:**            The diff content function is defined in `app.py` but depends on
                        `warp.build_baseline()` and `warp.parse_assets()`. This creates a
                        dependency from the HTTP routing module to the Warp/baseline module.
                        The diff logic should be in a dedicated `diff.py` module that both
                        `app.py` and `warp.py` can import.
- **Cross-ref. Delta:** A015, A020, A024, A028.
- **Fix(es):**          [Fix 1] Extract into `diff.py` along with `_diff_result`.
                        [Fix 2]
                        `app.py` delegates to `diff.py` for all comparison logic.
- **Code Sample:**      [

                        # app.py:265 — diff logic in HTTP module depending on warp
                        async def _diff_content(client_ip, domain, clean_scripts):
                            baseline = await warp.build_baseline(domain)
                            ...

                        ]

---

- **[C002]**            WitnessWireJson.cs — IEEE-754 ULP-nudging date math is fragile
                        precision-critical code
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. This code guarantees millisecond agreement with macOS's
                        Foundation TimeInterval encoding. A change to the macOS date encoding
                        would silently break the agreement.
- **Location:**         Windows/dotNET/Syxx.Kit/WitnessWireJson.cs:200-240
- **Issue:**            `AppleReferenceDateJsonConverter.ToAppleReferenceSeconds()` contains a
                        ULP-nudging loop that manipulates IEEE-754 `double` bit patterns
                        (`BitConverter.DoubleToInt64Bits` / `Int64BitsToDouble`) to guarantee
                        millisecond agreement with macOS's `UInt64(date.timeIntervalSince1970
                        * 1000)` truncation. The loop is bounded at 64 iterations. The
                        comment says it's "unreachable in practice" but the nudging loop
                        exists as a fallback. This is inherently fragile precision-critical
                        code working around a fundamental impedance mismatch.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add comprehensive tests for the ULP-nudging code with edge
                        cases (dates near epoch, dates far in future, leap seconds).
                        [Fix 2] Document the underlying Foundation/.NET date encoding
                        mismatch in the module header. [Fix 3] Consider a protocol-level
                        fix — use an integer milliseconds-since-epoch encoding instead of
                        double-precision TimeInterval.
- **Code Sample:**      [

                        // WitnessWireJson.cs:200-240
                        // ULP-nudging loop:
                        while (attempts < 64) {
                            double candidate = nextRepresentable(seconds);
                            if (matchesMilliseconds(candidate, target)) break;
                            // manipulate IEEE-754 bit patterns
                            long bits = BitConverter.DoubleToInt64Bits(candidate);
                            bits += (direction > 0) ? 1 : -1;
                            candidate = BitConverter.Int64BitsToDouble(bits);
                        }

                        ]

---

- **[C003]**            HasChanges API divergence — Linux uses computed property, Windows uses
                        constructor-set field
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. If someone mutates DiffResult after construction, HasChanges
                        would lie on Windows but not on Linux.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/Mirror/DiffResult.cs:62;
                        Windows/dotNET/SyxxUI/Mirror/DiffResult.cs:69,80
- **Issue:**            Linux: `public bool HasChanges => TotalDifferences > 0;` (computed
                        property — always correct). Windows: `public bool HasChanges { get; }`
                        set in constructor — can diverge from `TotalDifferences` if the
                        object is mutated after construction. This is a ticking time bomb for
                        any future code that modifies `DiffResult` after construction.
- **Cross-ref. Delta:** D009 (Mirror/ engine duplication).
- **Fix(es):**          [Fix 1] Use the computed property pattern on both platforms.
                        [Fix 2] Extract the Mirror/ engine to Syxx.Kit (D009) so there is
                        only one implementation.
- **Code Sample:**      [

                        // Linux (correct — always consistent)
                        public bool HasChanges => TotalDifferences > 0;

                        // Windows (fragile — can diverge)
                        public bool HasChanges { get; }
                        // set in constructor: HasChanges = changes > 0;

                        ]

---

- **[C004]**            SynthesisEngine incomplete indicator mapping — 4 clean-support
                        indicators intentionally not wired (see B010)
- **Status:**           **DUPLICATE** — See B010.
- **Criticality:**      See B010.
- **Location:**         See B010.
- **Issue:**            See B010.
- **Cross-ref. Delta:** B010 (primary tracking).
- **Fix(es):**          See B010.
- **Code Sample:**      [

                        # See B010 for full details.

                        ]

---

- **[D008]**            23 duplicate test files in macOS _quarantine/ — .phase32-active-dup and
                        .phase3-compile-break suffixes
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. Dead weight that inflates grep results, confuses code search,
                        and could be accidentally re-enabled.
- **Location:**         macOS/Tests/_quarantine/
- **Issue:**            The `_quarantine/` directory contains 23 files that are exact
                        duplicates of active test files with `.phase32-active-dup` suffix
                        (e.g., `SynthesisEngineTests.swift.phase32-active-dup`). 4 files
                        have `.phase3-compile-break` suffixes and fail to compile. These are
                        artifacts of a prior migration and provide no value.
- **Cross-ref. Delta:** B002 (quarantined tests).
- **Fix(es):**          [Fix 1] Delete all `.phase32-active-dup` and `.phase3-compile-break`
                        files. The active originals exist in `Tests/SyxxKitTests/` and
                        `Tests/SyxxAppTests/`.
- **Code Sample:**      [

                        # macOS/Tests/_quarantine/ — 23 dead files:
                        # SynthesisEngineTests.swift.phase32-active-dup
                        # SynthesizerDatapointWiringTests.swift.phase32-active-dup
                        # FusionRuleBakeoffTests.swift.phase32-active-dup
                        # ... 20 more .phase32-active-dup files
                        # SyxxKitTests.swift.phase32-compile-break
                        # ... 3 more .phase3-compile-break files

                        ]

---

- **[D009]**            Mirror/ engine duplicated between Linux and Windows — 7 files, ~850
                        lines each, with divergent fixes
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Critical. The Linux versions are objectively better (ReDoS protection,
                        async support, D333/D334 fixes). The Windows versions are stale.
                        Any bugfix must be manually ported.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/Mirror/ (7 files);
                        Windows/dotNET/SyxxUI/Mirror/ (7 files)
- **Issue:**            The entire Mirror/ static-content comparison engine is duplicated
                        between Linux and Windows with only namespace differences. The Linux
                        version has D333 (source-line-count normalization), D334 (empty-string
                        CountLines fix), ReDoS protection (5-second regex timeout), async
                        support (DiffAsync with CancellationToken), and `IReadOnlyList`
                        return types. The Windows version has none of these improvements.
                        The DiffEngine's own XML doc says it "delegates LCS computation to
                        TextDiff.Unified (shared Syxx.Kit)" — the entire engine should be
                        in Syxx.Kit, shared once.
- **Cross-ref. Delta:** C003 (HasChanges divergence), D010 (SummaryLine drift), D011 (ReDoS
                        on Windows), D012 (DiffAsync Linux-only), D013 (IReadOnlyList vs List).
- **Fix(es):**          [Fix 1] Extract the Mirror/ engine to `Syxx.Kit` (or a new
                        `Syxx.Kit.Mirror` package). [Fix 2] Use the Linux implementation as
                        the canonical baseline. [Fix 3] Windows consumes it from Syxx.Kit.
- **Code Sample:**      [

                        // Linux/.../Mirror/DiffEngine.cs     — 102 lines (async, CT)
                        // Windows/.../Mirror/DiffEngine.cs   — 94 lines (sync only)
                        // Linux/.../Mirror/MirrorBundle.cs   — 122 lines (D334 fix)
                        // Windows/.../Mirror/MirrorBundle.cs — 94 lines (pre-fix)
                        // Linux/.../Mirror/DiffResult.cs     — 112 lines (computed)
                        // Windows/.../Mirror/DiffResult.cs   — 99 lines (constructor)
                        // Linux/.../Mirror/MirrorResult.cs   — 69 lines (IsPartial)
                        // Windows/.../Mirror/MirrorResult.cs — 46 lines (no IsPartial)
                        // Linux/.../Mirror/DegradationCalc.cs — 103 lines (D333)
                        // Windows/.../Mirror/DegradationCalc.cs — 72 lines (no D333)
                        // Linux/.../Mirror/StaticContentParser.cs — 262 lines (ReDoS)
                        // Windows/.../Mirror/StaticContentParser.cs — 190 lines (no ReDoS)
                        // Linux/.../Mirror/DegradationScore.cs — 52 lines
                        // Windows/.../Mirror/DegradationScore.cs — 60 lines

                        ]

---

- **[D010]**            MirrorBundle.SummaryLine implementation drift — Linux has D334 fix,
                        Windows does not
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. The Linux version has a documented fix (D334: empty content
                        returns 0 lines, not 1) and avoids the string.Split allocation.
                        The Windows version has the old behavior.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/Mirror/MirrorBundle.cs:107-122;
                        Windows/dotNET/SyxxUI/Mirror/MirrorBundle.cs:83-93
- **Issue:**            Linux `CountLines` uses a manual loop that correctly returns 0 for
                        empty strings and avoids allocation. Windows uses `Split('\n').Length`
                        which allocates a string array and returns 1 for empty strings.
- **Cross-ref. Delta:** D009 (Mirror/ engine duplication).
- **Fix(es):**          [Fix 1] Backport the D334 fix to Windows.
                        [Fix 2] Extract the Mirror/
                        engine to Syxx.Kit (D009) so there is only one implementation.
- **Code Sample:**      [

                        // Linux (D334 fix — manual loop, correct for empty)
                        private static int CountLines(string text) {
                            if (string.IsNullOrEmpty(text)) return 0;
                            int count = 1;
                            for (int i = 0; i < text.Length; i++)
                                if (text[i] == '\n') count++;
                            return count;
                        }
                        // Windows (pre-fix — allocates, wrong for empty)
                        int htmlLines = string.IsNullOrEmpty(HtmlContent)
                            ? 0 : HtmlContent.Split('\n').Length;

                        ]

---

- **[D011]**            Windows StaticContentParser lacks ReDoS protection — Linux has 5-second
                        regex timeout
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. Adversarial HTML input with pathological backtracking
                        (e.g., deeply nested unclosed <style> tags) could hang the Windows
                        client indefinitely.
- **Location:**         Windows/dotNET/SyxxUI/Mirror/StaticContentParser.cs (entire file);
                        Linux/Ubuntu/SyxxUI.Avalonia/Mirror/StaticContentParser.cs:118
- **Issue:**            The Linux version adds a 5-second regex timeout on all 6
                        `Regex.Matches` calls. The Windows version has no timeout at all.
                        The Linux version is hardened; the fix must be ported to Windows.
- **Cross-ref. Delta:** D009 (Mirror/ engine duplication).
- **Fix(es):**          [Fix 1] Backport the 5-second regex timeout to Windows.
                        [Fix 2] Extract the Mirror/ engine to Syxx.Kit (D009) so there is
                        only one implementation.
- **Code Sample:**      [

                        // Linux (hardened)
                        private static readonly TimeSpan RegexTimeout =
                            TimeSpan.FromSeconds(5);
                        var matches = Regex.Matches(html, pattern,
                            RegexOptions.IgnoreCase, RegexTimeout);

                        // Windows (unprotected)
                        var matches = Regex.Matches(html, pattern,
                            RegexOptions.IgnoreCase);

                        ]

---

- **[D012]**            DiffEngine.DiffAsync only exists on Linux — no async variant on Windows
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Low. Quality-of-life improvement that should be available on both
                        platforms.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/Mirror/DiffEngine.cs:15-19
- **Issue:**            Linux has `DiffAsync` with `CancellationToken` support and
                        `IReadOnlyList<DiffLine>` return type. Windows has no async variant.
- **Cross-ref. Delta:** D009 (Mirror/ engine duplication).
- **Fix(es):**          [Fix 1] Backport `DiffAsync` to Windows.
                        [Fix 2] Extract the Mirror/
                        engine to Syxx.Kit (D009) so there is only one implementation.
- **Code Sample:**      [

                        // Linux-only
                        public static Task<IReadOnlyList<DiffLine>> DiffAsync(
                            string remote, string local, int context = 2,
                            CancellationToken cancellationToken = default)
                        {
                            return Task.Run(() =>
                                (IReadOnlyList<DiffLine>)Diff(remote, local, context),
                                cancellationToken);
                        }

                        ]

---

- **[D013]**            DiffResult uses IReadOnlyList on Linux, List on Windows — type divergence
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Low. The Linux version is more correct (immutable API surface), but
                        the type difference prevents sharing a single DiffResult type.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/Mirror/DiffResult.cs;
                        Windows/dotNET/SyxxUI/Mirror/DiffResult.cs
- **Issue:**            Linux exposes `IReadOnlyList<DiffLine>` for `HtmlDiff`, `CssDiff`,
                        `JsDiff`, and `LinesFor()`. Windows exposes `List<DiffLine>`. The
                        Linux version is more correct but the type difference means the two
                        platforms can't share a single `DiffResult` type without a breaking
                        change on Windows.
- **Cross-ref. Delta:** D009 (Mirror/ engine duplication).
- **Fix(es):**          [Fix 1] Standardize on `IReadOnlyList<DiffLine>` on both platforms.
                        [Fix 2] Extract the Mirror/ engine to Syxx.Kit (D009).
- **Code Sample:**      [

                        // Linux (correct — immutable)
                        public IReadOnlyList<DiffLine> HtmlDiff { get; }
                        // Windows (mutable)
                        public List<DiffLine> HtmlDiff { get; }

                        ]

---

- **[D014]**            AppModel.swift is a 2,354-line God class with 63 @Published vars and
                        60 methods
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Critical. Every tool change, every new feature, every state variable
                        gets added here. Testing individual tools requires instantiating the
                        entire 2,354-line class. Sentry breadcrumb calls are scattered inline
                        (7 locations) rather than through a decorator.
- **Location:**         macOS/Sources/SyxxApp/AppModel.swift (2,354 lines)
- **Issue:**            `AppModel` is the single `@MainActor ObservableObject` that owns
                        every piece of state for the entire GUI. It directly orchestrates DNS
                        lookups, Mirror comparisons, DNS leak tests, traceroute, Cage live
                        capture (100+ lines of NDJSON parsing), Witness signing/verification,
                        Unbound resolver toggling, license validation, continuous monitoring,
                        Dimension startup checks, and notification delivery. It also owns the
                        `DashboardLog` ring buffer.
- **Cross-ref. Delta:** D015 (runAllWardens duplicates run), D016 (no ViewModel layer).
- **Fix(es):**          [Fix 1] Extract per-tool ViewModels (ProphetViewModel, CageViewModel,
                        WitnessViewModel, etc.) that own their own @Published state.
                        [Fix 2] AppModel becomes a navigation coordinator only.
                        [Fix 3] Replace inline Sentry calls with a decorator pattern.
- **Code Sample:**      [

                        // AppModel.swift — single class containing:
                        // 63 @Published vars
                        // 60 methods
                        // runLookup(), runCompare(), runLeak(), startTraceroute(),
                        // startRealCapture() (100+ lines), runWitness(),
                        // runAllWardens(), run(), execute(), applyLicense(),
                        // monitor, notifications, dimension checks, DashboardLog

                        ]

---

- **[D015]**            runAllWardens() duplicates run() logic — timeout, Sentry, state management
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Low. Adding a seventh warden requires updating both methods.
- **Location:**         macOS/Sources/SyxxApp/AppModel.swift:632-745, 746-866
- **Issue:**            `runAllWardens()` manually sequences all six wardens with individual
                        `withTimeout` calls, `selection` switching, and `isPro` gating.
                        `run()` dispatches through `execute()`. Both methods duplicate the
                        same timeout pattern, Sentry breadcrumbs, and
                        `runningTool`/`scanCorrelationId` management.
- **Cross-ref. Delta:** D014 (AppModel God class).
- **Fix(es):**          [Fix 1] `runAllWardens()` should call `run()` for each tool in
                        sequence. [Fix 2] Both should delegate to a shared `runTool(_ tool:
                        Tool)` method.
- **Code Sample:**      [

                        // AppModel.swift:632-745 — runAllWardens()
                        // AppModel.swift:746-866 — run()
                        // Both duplicate: withTimeout, selection switching,
                        //   SentrySDK.addBreadcrumb, runningTool/scanCorrelationId

                        ]

---

- **[D016]**            No ViewModel layer on macOS — views directly import SyxxKit types
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. Changing a SyxxKit type signature forces changes in multiple
                        view files. Views cannot be tested independently of SyxxKit.
- **Location:**         macOS/Sources/SyxxApp/ (34 files import SyxxKit directly)
- **Issue:**            Every SyxxApp view imports `SyxxKit` directly. Views reference
                        `DNSRecord`, `ComparisonResult`, `WitnessSnapshot`, `NetworkStateReport`
                        directly in their body and subview builders. There is no intermediate
                        ViewModel layer that transforms SyxxKit domain models into
                        view-specific display models.
- **Cross-ref. Delta:** D014 (AppModel God class).
- **Fix(es):**          [Fix 1] Introduce per-tool ViewModels that own @Published display
                        state. [Fix 2] Views should only depend on their ViewModel, not on
                        SyxxKit directly. [Fix 3] ViewModels map SyxxKit domain types to
                        view-specific types.
- **Code Sample:**      [

                        // Every SyxxApp view:
                        import SyxxKit
                        // Direct references to SyxxKit types in view bodies:
                        // DNSRecord, ComparisonResult, WitnessSnapshot,
                        // NetworkStateReport, etc.

                        ]

---

- **[D017]**            CLI commands have no shared protocol — 15 files with identical
                        `static func run(_ argv: [String]) async -> Int32` signatures
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. Adding a new CLI command requires copy-pasting the arg-parsing
                        boilerplate. Changing the exit-code convention requires touching 15
                        files. Cage and Witness commands duplicate business logic from AppModel.
- **Location:**         macOS/Sources/SyxxCLI/Commands/ (15 files)
- **Issue:**            Every CLI command duplicates the same structural pattern: a `static
                        func run(_ argv: [String]) async -> Int32` that manually parses argv,
                        validates arguments, and orchestrates the operation. No shared
                        protocol, no `ParsableCommand` conformance, no common argument parsing.
                        `SyxxCage.swift` (623 lines) and `SyxxWitness.swift` (582 lines)
                        contain substantial business logic that should live in SyxxKit.
- **Cross-ref. Delta:** D018 (SyxxCLI monolithic parser).
- **Fix(es):**          [Fix 1] Define a `SyxxCommand` protocol with a shared
                        `run(arguments:)` method. [Fix 2] Move Cage/Witness business logic
                        into SyxxKit services. [Fix 3] Adopt `ArgumentParser` for declarative
                        argument parsing.
- **Code Sample:**      [

                        // 15 files, identical pattern:
                        // Commands/SyxxProphet.swift
                        static func run(_ argv: [String]) async -> Int32 { ... }
                        // Commands/SyxxMirror.swift
                        static func run(_ argv: [String]) async -> Int32 { ... }
                        // ... 13 more
                        // SyxxCage.swift  — 623 lines, business logic in CLI
                        // SyxxWitness.swift — 582 lines, business logic in CLI

                        ]

---

- **[D018]**            SyxxCLI.swift — 1,133-line monolithic CLI parser, no ArgumentParser
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. Adding a new subcommand requires touching the monolithic
                        router. Help text is manually maintained.
- **Location:**         macOS/Sources/SyxxCLI/SyxxCLI.swift (1,133 lines)
- **Issue:**            The main CLI entry point is a single file that manually routes
                        subcommands, parses flags, and handles help text via a large switch
                        statement. It does not use `ArgumentParser` (Apple's Swift argument
                        parser). Each subcommand is dispatched via a large switch.
- **Cross-ref. Delta:** D017 (CLI no shared protocol).
- **Fix(es):**          [Fix 1] Adopt `ArgumentParser` for declarative command structure.
                        [Fix 2] Each command becomes a self-contained `ParsableCommand` struct.
- **Code Sample:**      [

                        // SyxxCLI.swift — 1,133 lines
                        // Manual subcommand routing via switch statement
                        // Manual flag parsing per subcommand
                        // Manual help text

                        ]

---

- **[D019]**            Dual telemetry — Sentry + OTel side-by-side with SyxxDiagnostics layer
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. Three telemetry APIs with different semantics. A developer
                        adding a new feature must decide which system to use and often
                        duplicates — Sentry breadcrumbs and OTel spans for the same event.
- **Location:**         macOS/Sources/SyxxTelemetry/ (3 files);
                        macOS/Sources/SyxxKit/ and macOS/Sources/SyxxApp/ (scattered calls)
- **Issue:**            59 Sentry references, 29 OTel references, 32 SyxxDiagnostics
                        references. AppModel has 7 inline `SentrySDK.addBreadcrumb` calls.
                        The `SyxxDiagnostics` layer is a fourth abstraction that doesn't
                        fully replace either.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Consolidate on one telemetry abstraction — `SyxxTracer` as
                        the single API surface. [Fix 2] Sentry and OTel as backends.
                        [Fix 3] Remove inline `SentrySDK` calls from AppModel.
- **Code Sample:**      [

                        // Three telemetry systems in use:
                        // SentrySDK.addBreadcrumb(...) — 7 calls in AppModel alone
                        // SyxxTracer (OTel) — 29 references
                        // SyxxDiagnostics — 32 references
                        // All three track the same events with different APIs

                        ]

---

- **[D020]**            Syxx.Kit is a kitchen-sink module — 80+ files in flat namespace, no
                        internal boundaries
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. Every consumer gets the entire surface area. Internal types
                        that should be `internal` are `public` because everything lives in
                        one project. No way to take a dependency on "just DNS resolution."
- **Location:**         Windows/dotNET/Syxx.Kit/ (80+ .cs files)
- **Issue:**            A single project contains: DNS resolution/transport, packet capture,
                        synthesis engine, matrix engine, leak detection, lookalike detection,
                        forensics/log analysis, encryption (NSec), witness wire format, LAN
                        cartography, configuration, licensing, platform abstraction, secret
                        storage, probe running, telemetry, and content warp. `InternalsVisibleTo`
                        grants expose internals that should be proper public API boundaries.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Split into bounded packages: `Syxx.Kit.Dns`,
                        `Syxx.Kit.Synthesis`, `Syxx.Kit.Capture`, `Syxx.Kit.Leak`, etc.
                        [Fix 2] Each package has a clear public API surface.
- **Code Sample:**      [

                        // Syxx.Kit — single project containing:
                        // DNSResolver, CaptureEngine, SynthesisEngine,
                        // MatrixDeterminativeEngine, LeakDetector, WitnessWireJson,
                        // LookalikeDetector, Forensics, LAN cartography,
                        // Configuration, Licensing, Platform, SecretStore,
                        // ProbeRunner, Telemetry, ContentWarp
                        // All 80+ files in flat namespace Syxx.Kit

                        ]

---

- **[D021]**            No mocking library in C# tests — 15,903 lines of test code with ad-hoc
                        inline fakes
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. Every test file reinvents its own fake infrastructure. Changing
                        DNSResponse shape requires touching every hand-rolled fake.
- **Location:**         Windows/dotNET/Syxx.Tests/ (63 test files, 15,903 lines)
- **Issue:**            Zero mocking libraries (no Moq, NSubstitute, or FakeItEasy). Zero
                        assertion libraries (no FluentAssertions or Shouldly). Tests use
                        `Func<DNSQuery, Task<DNSResponse>>` lambdas as inline fakes. The
                        `Mock()` lambda in `SynthesisEngineTests.cs` is 35 lines of
                        hand-rolled test doubles. No shared `TestDNSResolver` or
                        `TestLeakCollector` — each test file builds its own.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add Moq + FluentAssertions to test projects.
                        [Fix 2] Build
                        shared test fakes (`TestDNSResolver`, `TestLeakCollector`).
                        [Fix 3] Reduce test code duplication by sharing test infrastructure.
- **Code Sample:**      [

                        // SynthesisEngineTests.cs:24-58 — 35 lines of hand-rolled fake
                        Func<DNSQuery, Task<DNSResponse>> mock = async (query) => {
                            if (query.Domain == "clean.example.com")
                                return new DNSResponse { ... };
                            if (query.Domain == "tampered.example.com")
                                return new DNSResponse { ... };
                            // ... more hand-rolled fake logic
                        };
                        // No Moq, no NSubstitute, no shared test infrastructure

                        ]

---

- **[D022]**            SyxxUI.Tests is a dead "UNVERIFIED SCAFFOLDING" test project — cannot
                        build or run
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. A test project that cannot be built or run on any CI system is
                        dead code. It consumes maintenance attention and creates a false sense
                        of coverage.
- **Location:**         Windows/dotNET/SyxxUI.Tests/SyxxUI.Tests.csproj
- **Issue:**            The project's own .csproj header declares itself "UNVERIFIED
                        SCAFFOLDING — no Windows/.NET WinUI3 toolchain was available to
                        restore, build, or run this project." It is explicitly not wired into
                        CI. It targets `net10.0-windows10.0.26100.0` with `UseWinUI=true`.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Either wire it into CI (requires a Windows runner) or remove
                        it. [Fix 2] If kept, add a CI job on a Windows runner.
- **Code Sample:**      [

                        // SyxxUI.Tests.csproj header:
                        // "UNVERIFIED SCAFFOLDING — no Windows/.NET WinUI3
                        //  toolchain was available to restore, build, or run
                        //  this project."
                        // "NOT done here (flagged, not fixed): this project is
                        //  not wired into .github/workflows/ci.yml"

                        ]

---

- **[D023]**            No central package version management — versions repeated across 7
                        .csproj files
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. Version drift between projects (e.g., OTel at 1.9.0 vs 1.16.0)
                        carried known CVEs. No `Directory.Packages.props` or `global.json`
                        SDK pin.
- **Location:**         Windows/dotNET/ (all 7 .csproj files)
- **Issue:**            Package versions are repeated across projects: `Sentry` 4.13.0 in 3
                        projects, `OpenTelemetry` 1.16.0 in 2 projects, `xunit` 2.9.2 in 3
                        projects, `xunit.runner.visualstudio` 2.8.2 in 3 projects. No
                        `Directory.Packages.props` for centralized version management. No
                        `Directory.Build.props` for shared MSBuild properties.
- **Cross-ref. Delta:** D024 (no global.json).
- **Fix(es):**          [Fix 1] Add `Directory.Packages.props` for centralized package
                        versioning. [Fix 2] Add `Directory.Build.props` for shared MSBuild
                        properties (`Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`).
- **Code Sample:**      [

                        // Sentry 4.13.0: in Syxx.csproj, SyxxUI.csproj,
                        //   Syxx.Telemetry.csproj
                        // OpenTelemetry 1.16.0: in SyxxUI.csproj,
                        //   Syxx.Telemetry.csproj
                        // xunit 2.9.2: in 3 test projects
                        // Comment in Syxx.Telemetry.csproj:
                        //   "OTel was at 1.9.0 while SyxxUI was at 1.16.0"
                        // No Directory.Packages.props exists

                        ]

---

- **[D024]**            No global.json or Directory.Build.props — no SDK pin, no shared
                        MSBuild properties
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Low. Different developer machines may use different SDK versions,
                        producing different builds or warnings.
- **Location:**         Windows/dotNET/ (missing files)
- **Issue:**            No `global.json` to pin the .NET SDK version. No `Directory.Build.props`
                        for shared properties (`Nullable`, `ImplicitUsings`,
                        `TreatWarningsAsErrors`, `AnalysisLevel`). Each of the 7 `.csproj`
                        files independently declares `<Nullable>enable</Nullable>` and
                        `<ImplicitUsings>enable</ImplicitUsings>`.
- **Cross-ref. Delta:** D023 (no central package versioning).
- **Fix(es):**          [Fix 1] Add `global.json` pinning the .NET 10.0 SDK.
                        [Fix 2] Add
                        `Directory.Build.props` with shared MSBuild properties.
- **Code Sample:**      [

                        // Missing from Windows/dotNET/:
                        // global.json — SDK version pin
                        // Directory.Build.props — shared properties:
                        //   <Nullable>enable</Nullable>
                        //   <ImplicitUsings>enable</ImplicitUsings>
                        //   <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                        //   <AnalysisLevel>latest</AnalysisLevel>

                        ]

---

- **[D025]**            _retired-fonts directory left in source tree — dead assets still tracked
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Low. Dead assets increase checkout size, confuse new developers, and
                        risk accidental re-reference.
- **Location:**         Windows/dotNET/_retired-fonts/
- **Issue:**            A directory of retired font assets still present in the repository.
                        Not referenced by any `.csproj` but still tracked in git.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Delete the directory or move to an archival location outside
                        the active source tree.
- **Code Sample:**      [

                        # Windows/dotNET/_retired-fonts/
                        # Dead assets, not referenced by any .csproj
                        # Still tracked in git

                        ]

---

- **[D026]**            SyxxUI.Logic.Tests is an empty/near-empty test project
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Low. Adds CI overhead with no benefit. Compiles production code but
                        has no tests to exercise it.
- **Location:**         Windows/dotNET/SyxxUI.Logic.Tests/SyxxUI.Logic.Tests.csproj
- **Issue:**            The project file references `Syxx.Kit` and compiles `FirstRunViewModel.cs`
                        from the production code, but no test `.cs` files were found.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Either add tests or remove the project.
                        [Fix 2] If kept, add
                        at least one test to justify the CI overhead.
- **Code Sample:**      [

                        // SyxxUI.Logic.Tests.csproj
                        // References Syxx.Kit
                        // Compiles FirstRunViewModel.cs from production
                        // No test .cs files found

                        ]

---

- **[D027]**            No BenchmarkDotNet for performance-critical paths — undetectable
                        regressions
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Low. Performance regressions in the capture loop or matrix engine
                        evaluation are undetectable until a user reports slowness.
- **Location:**         Windows/dotNET/ (no benchmark project)
- **Issue:**            The codebase has no performance benchmarks despite containing: packet
                        capture loop, DNS packet parsing, matrix engine evaluation (O(n²)
                        diagnosis × indicator scoring), and JSON serialization of witness
                        snapshots.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add a `BenchmarkDotNet` project with benchmarks for
                        `ParseIPUDPHeader`, `ParseDNSPacket`, `MatrixDeterminativeEngine.
                        Evaluate`, and `WitnessWireJson` serialization. [Fix 2] Run benchmarks
                        in CI to catch regressions.
- **Code Sample:**      [

                        # No benchmark project exists.
                        # Performance-critical paths without benchmarks:
                        # - Packet capture loop
                        # - DNS packet parsing
                        # - Matrix engine evaluation (O(n²))
                        # - Witness JSON serialization

                        ]

---

- **[D028]**            Hardcoded color values across 34 locations on Linux — no shared design
                        tokens
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. Changing a theme color requires touching 4-6 files.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/ (8 files, 34 Color.Parse() calls)
- **Issue:**            The same hex colors appear in multiple files: `#F2B33D` (amber) in 6
                        locations, `#FF5C7A` (red) in 4 locations, `#3FD08A` (green) in 3
                        locations, `#FF4D6D` (critical) in 3 locations. `SeverityColor.cs`
                        centralizes severity-to-color mapping, but all other files define
                        their own `static readonly` brushes. ViewModels can't access
                        `App.axaml` resource dictionary tokens without `Application.Current`.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Create a centralized `SyxxBrushes` or `DesignTokens` class.
                        [Fix 2] Have ViewModels reference the shared tokens.
- **Code Sample:**      [

                        // #F2B33D (amber) — 6 locations:
                        // WitnessViewModel, ProphetViewModel, MirrorViewModel,
                        // DegradationBadge, SeverityColor, WhatsNewViewModel
                        // #FF5C7A (red) — 4 locations:
                        // SeverityColor, MirrorViewModel, ProphetViewModel,
                        // WhatsNewViewModel
                        // #3FD08A (green) — 3 locations
                        // #FF4D6D (critical) — 3 locations

                        ]

---

- **[D029]**            6 Linux ViewModels have zero test coverage — ~700 lines untested
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. The D278 "no resolver" gate and Defect-2 error-surface branches
                        are present in every ViewModel but only tested in 3 of 9. A regression
                        in the untested ViewModels would silently break the honesty contract.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/ (6 files)
- **Issue:**            The test project has 14 test files covering Mirror/ engine, Witness,
                        Glutton, and Prophet — but six ViewModels have zero tests:
                        `DashboardViewModel` (81 lines), `MirrorViewModel` (119 lines),
                        `PortalViewModel` (87 lines), `ShellViewModel` (92 lines),
                        `ConclusionsViewModel` (125 lines), `UpdateViewModel` (121 lines).
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add ViewModel tests for the 6 untested ViewModels.
                        [Fix 2] Verify the D278 gate and Defect-2 error branches in each.
- **Code Sample:**      [

                        // 6 ViewModels with zero tests:
                        // DashboardViewModel.cs    — 81 lines
                        // MirrorViewModel.cs       — 119 lines
                        // PortalViewModel.cs       — 87 lines
                        // ShellViewModel.cs        — 92 lines
                        // ConclusionsViewModel.cs  — 125 lines
                        // UpdateViewModel.cs       — 121 lines
                        // Total: ~700 lines untested

                        ]

---

- **[D030]**            GluttonViewModel (643 lines) and WitnessViewModel (522 lines) are
                        God classes — 5-6 distinct responsibilities each
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      High. `SignAsync` in WitnessViewModel has 5 nested try/catch blocks.
                        Extraction would make each component testable in isolation.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/GluttonViewModel.cs (643 lines);
                        Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/WitnessViewModel.cs (522 lines)
- **Issue:**            GluttonViewModel handles 5 responsibilities: feature/license status,
                        VPN/encryption card, leak probe dispatch, collector-attested
                        verification, and `RenderLeakResult` honesty gate (120 lines).
                        WitnessViewModel handles 6 responsibilities: signing workflow
                        (`SignAsync`, 150 lines, 5 nested try/catch), self-verify, external
                        verify, external-verify rendering, key import, and state management.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Extract `LeakHonestyGate` from GluttonViewModel.
                        [Fix 2]
                        Extract `WitnessVerifyPresenter` from WitnessViewModel. [Fix 3]
                        Reduce each ViewModel to a single responsibility.
- **Code Sample:**      [

                        // GluttonViewModel.cs — 5 responsibilities:
                        // 1. Feature/license status (E117, ~50 lines)
                        // 2. VPN/encryption status card (~40 lines)
                        // 3. Local leak probe dispatch (~80 lines)
                        // 4. Collector-attested verification (~80 lines)
                        // 5. RenderLeakResult honesty gate (~120 lines)
                        // WitnessViewModel.cs — 6 responsibilities:
                        // 1. SignAsync (150 lines, 5 nested try/catch)
                        // 2. ApplyVerification 3. VerifyExternal
                        // 4. ApplyExternalVerification 5. ImportKey
                        // 6. State management

                        ]

---

- **[D031]**            lazy var monitor in AppModel is not thread-safe under Swift 6
                        strict concurrency
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. Latent data race. Swift 6's strict concurrency checking may
                        flag this. The `lazy` pattern is incompatible with `@MainActor`
                        isolation guarantees.
- **Location:**         macOS/Sources/SyxxApp/AppModel.swift:277
- **Issue:**            `lazy var monitor: MonitorScheduler = MonitorScheduler(...)` is
                        declared on a `@MainActor` class. Under Swift 6, `lazy var`
                        initialization is not actor-isolated — the synthesized getter can be
                        called from any context. The `MonitorScheduler` initializer captures
                        `[weak self]` and calls `UNUserNotificationCenter` APIs.
- **Cross-ref. Delta:** D014 (AppModel God class).
- **Fix(es):**          [Fix 1] Replace `lazy var` with an explicitly initialized stored
                        property or a computed property backed by an optional. [Fix 2]
                        Initialize `monitor` in an `init()` or via a factory method.
- **Code Sample:**      [

                        // AppModel.swift:277
                        lazy var monitor: MonitorScheduler = MonitorScheduler(
                            interval: 300,
                            onEvent: { [weak self] event in
                                // UNUserNotificationCenter calls
                            }
                        )

                        ]

---

- **[D032]**            Legacy DispatchQueue and @escaping patterns remain despite Swift 6
                        migration target
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. Mixing DispatchQueue with Swift 6 actors creates potential
                        actor isolation violations.
- **Location:**         macOS/Sources/SyxxKit/NetworkKeyState.swift:233;
                        macOS/Sources/SyxxKit/UnboundManager.swift:390;
                        macOS/Sources/SyxxKit/DNSCryptProxyManager.swift:16;
                        macOS/Sources/SyxxKit/CommandRunner.swift:172-181;
                        macOS/Sources/SyxxApp/RemoteContentView.swift:283-392;
                        macOS/Sources/SyxxApp/WelcomeView.swift:247-258
- **Issue:**            Despite the project targeting Swift 6.0 and having 408 async/await
                        usage points, several files still use `DispatchQueue.global().async`,
                        `DispatchQueue.main.async`, and `@escaping` completion handlers.
                        `UnboundManager` and `DNSCryptProxyManager` maintain their own
                        `DispatchQueue` serial queues.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Replace `DispatchQueue.global().async` with `Task.detached`.
                        [Fix 2] Replace serial `DispatchQueue` instances with Swift actors.
                        [Fix 3] Replace `DispatchQueue.main.asyncAfter` with `Task.sleep` +
                        `@MainActor`.
- **Code Sample:**      [

                        // NetworkKeyState.swift:233
                        DispatchQueue.global(qos: .userInitiated).async { ... }
                        // CommandRunner.swift:172-181
                        DispatchQueue.global().async { ... }
                        // RemoteContentView.swift:283-392
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) { ... }
                        // UnboundManager.swift:390
                        private let queue = DispatchQueue(label: "syxx.unbound")

                        ]

---

- **[D033]**            URLSession delegate completion handlers remain — last @escaping
                        network patterns
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Low. These are the only places where the old URLSession delegate
                        pattern is used instead of async URLSession methods.
- **Location:**         macOS/Sources/SyxxKit/LeakDetector.swift:1012-1029;
                        macOS/Sources/SyxxKit/WarpEngine.swift:625-664
- **Issue:**            Both `LeakDetector` and `WarpEngine` implement `URLSessionDelegate`
                        methods inline with `completionHandler` callbacks for SPKI pinning
                        and redirect blocking. These are the last remaining `@escaping`
                        completion-handler patterns in the network layer.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Replace with `URLSessionDelegate`-based `AsyncSequence`
                        patterns. [Fix 2] Use `URLSession.data(for:delegate:)` with an async
                        delegate.
- **Code Sample:**      [

                        // LeakDetector.swift:1012-1029
                        func urlSession(_ session: URLSession,
                            didReceive challenge: URLAuthenticationChallenge,
                            completionHandler: @escaping (...) -> Void) {
                            // SPKI pinning
                            completionHandler(...)
                        }
                        // WarpEngine.swift:625-664
                        func urlSession(_ session: URLSession,
                            task: URLSessionTask,
                            willPerformHTTPRedirection: ...,
                            completionHandler: @escaping (...) -> Void) {
                            // Redirect blocking
                            completionHandler(...)
                        }

                        ]

---

- **[E012]**            No architecture documentation on macOS — no ADRs, C4 diagrams, or
                        module dependency graphs
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Low. A new developer must read 50,000 lines of code to understand the
                        data flow from DNS query → probe → synthesis engine → conclusion.
- **Location:**         macOS/ (no architecture docs exist)
- **Issue:**            The codebase has excellent inline comments (design invariants, fix
                        references like C062, D278, M-01) but no standalone architecture
                        documentation. No diagram showing how SyxxKit modules relate, no
                        documentation of the synthesis pipeline, and no onboarding guide.
                        `ARCHITECTURE.md` at the project root covers cross-platform structure
                        but not the macOS-specific module design.
- **Cross-ref. Delta:** A019 (Server architecture docs).
- **Fix(es):**          [Fix 1] Create `macOS/ARCHITECTURE.md` documenting: SyxxKit module
                        boundaries, synthesis pipeline (DNS query → probe → evidence →
                        matrix engine → conclusion), and data flow diagrams.
- **Code Sample:**      [

                        # No architecture documentation for macOS.
                        # Inline comments reference fix tags (C062, D278, M-01)
                        # but there is no module-level architecture doc.
                        # The synthesis pipeline is:
                        #   DNSResolver → Probe → SynthesisEngine →
                        #   MatrixDeterminativeEngine → Synthesizer → Conclusion

                        ]

---

- **[F006]**            Regex documentation only on Linux StaticContentParser — Windows lacks
                        limitation docs
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Low. The Linux version explicitly documents 4 regex limitations;
                        Windows has none.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/Mirror/StaticContentParser.cs (L2-012);
                        Windows/dotNET/SyxxUI/Mirror/StaticContentParser.cs
- **Issue:**            The Linux version documents 4 regex limitations (nested <style>,
                        CDATA, attribute ordering, unquoted src) and the ReDoS protection.
                        The Windows version has none of this documentation.
- **Cross-ref. Delta:** D009, D011.
- **Fix(es):**          [Fix 1] Copy documentation from Linux to Windows.
                        [Fix 2] Extract the
                        Mirror/ engine to Syxx.Kit (D009) so there is only one copy.
- **Code Sample:**      [

                        // Linux StaticContentParser.cs:1-12
                        // Documents 4 regex limitations:
                        // - nested <style> tags
                        // - CDATA sections
                        // - attribute ordering
                        // - unquoted src attributes
                        // + ReDoS protection (5-second timeout)
                        // Windows has none of this documentation

                        ]

---

- **[F007]**            Cross-platform architecture documentation missing — no unified
                        module map across all 4 platforms
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Medium. No single document shows how SyxxKit (macOS), Syxx.Kit
                        (Windows), SyxxUI.Avalonia (Linux), and syxx_server (Server) relate
                        to each other or share concepts.
- **Location:**         Project root (no cross-platform architecture doc)
- **Issue:**            The `ARCHITECTURE.md` at the project root covers the plugin
                        marketplace structure, not the Syxx product architecture. There is no
                        document showing the synthesis pipeline across platforms, the shared
                        concepts (DNS resolution, probe evaluation, matrix engine), or the
                        cross-platform parity requirements.
- **Cross-ref. Delta:** A019, E012.
- **Fix(es):**          [Fix 1] Create a cross-platform architecture document showing: shared
                        concepts (DNS, synthesis, matrix), platform-specific implementations,
                        data flow from query → conclusion, and parity requirements.
- **Code Sample:**      [

                        # No cross-platform product architecture doc exists.
                        # ARCHITECTURE.md covers plugin marketplace, not Syxx.
                        # Shared concepts across platforms:
                        # - DNS resolution (Swift, C#, Python)
                        # - Probe evaluation (Swift, C#)
                        # - Synthesis/Matrix engine (Swift, C#)
                        # - Collector-attested leak detection (C#, Python)
                        # - Witness wire format (Swift, C#)

                        ]

---

- **[F008]**            StaticContentParser regex-based HTML parsing is inherently fragile
                        across both platforms
- **Status:**           **OPEN** — Found during codebase-cleanup tech-debt scan 2026-08-09.
- **Criticality:**      Low. The Linux version documents the limitations, but the fact that
                        limitations exist at all is a smell that regex-based HTML parsing is
                        inherently fragile.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/Mirror/StaticContentParser.cs;
                        Windows/dotNET/SyxxUI/Mirror/StaticContentParser.cs
- **Issue:**            Both platforms use regex to parse HTML for scripts, stylesheets, and
                        inline content. The Linux version documents 4 known limitations
                        (nested <style>, CDATA, attribute ordering, unquoted src). Regex is
                        not a robust HTML parser — it will fail on valid but unexpected HTML
                        structures. A proper HTML parser (e.g., HtmlAgilityPack or AngleSharp)
                        would be more robust.
- **Cross-ref. Delta:** D009, D011, F006.
- **Fix(es):**          [Fix 1] Evaluate replacing regex-based parsing with HtmlAgilityPack
                        or AngleSharp. [Fix 2] If keeping regex, add comprehensive tests for
                        the documented limitations.
- **Code Sample:**      [

                        // Both platforms use regex for HTML parsing:
                        // Regex.Matches(html, @"<script[^>]*src=[""']([^""']+)[""']")
                        // Regex.Matches(html, @"<link[^>]*href=[""']([^""']+)[""']")
                        // Regex.Matches(html, @"<style[^>]*>(.*?)</style>")
                        // Known limitations:
                        // - nested <style> tags not handled
                        // - CDATA sections not parsed
                        // - attribute ordering assumed
                        // - unquoted src/href not supported

                        ]

---

- **[A032]**            cryptography is 9 major versions behind — exposed to CVE-2026-69249
                        (CVSS 8.7) and multiple OpenSSL CVEs
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      Critical. CVE-2026-69249 (CVSS 8.7 HIGH): Duplicate self-signed
                        intermediates cause exponential path-building DoS. Fixed in 49.0.0.
                        Also exposed to GHSA-h4gh-qq45-vh27 (Moderate, fixed in 43.x),
                        GHSA-6vqw-3v5j-54x4 (Moderate, fixed in 42.x), and multiple OpenSSL
                        advisories (HIGH, 2026). The server uses cryptography for Ed25519
                        leak attestation signing (CF-01) — an attacker exploiting a
                        Bleichenbacher oracle or path-building DoS could compromise
                        attestation integrity or cause denial of service.
- **Location:**         Server/requirements.txt (constraint: `cryptography>=41.0.0`)
- **Issue:**            The `>=41.0.0` constraint allows installation of versions 41.0.0
                        through 50.0.0. Without a lockfile, any version in that range could
                        be resolved, including versions with known CVEs. The latest is 50.0.0
                        (released Jul 31, 2026).
- **Cross-ref. Delta:** C005 (attestation signing exposure).
- **Fix(es):**          [Fix 1] Pin to `cryptography>=49.0.0` (first version with
                        CVE-2026-69249 fix). [Fix 2] Add `pip-audit` to CI for automated
                        vulnerability scanning. [Fix 3] Migrate Server to uv with lockfile
                        for reproducible builds.
- **Code Sample:**      [

                        # Server/requirements.txt
                        cryptography>=41.0.0  # ← 9 major versions behind
                        # Latest: 50.0.0 (Jul 2026)
                        # CVE-2026-69249 fixed in 49.0.0

                        ]

---

- **[A033]**            sentry-sdk[opentelemetry] is 47 minor versions behind — missing OTel
                        interop fixes and potential silent error data loss
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      High. Sentry SDK is the error telemetry pipeline. Dozens of bug fixes
                        and OTel interop improvements between 2.19 and 2.66.1. Missing error
                        context could mean silent data loss in production monitoring.
- **Location:**         Server/requirements.txt (constraint: `sentry-sdk[opentelemetry]>=2.19`)
- **Issue:**            The constraint allows any version from 2.19 to 2.66.1. Without a
                        lockfile, the resolved version is non-deterministic. The latest is
                        2.66.1 — 47 minor versions ahead. No known CVEs in the gap, but the
                        OTel interop improvements are critical for correct telemetry.
- **Cross-ref. Delta:** D034 (Swift sentry-cocoa), D036 (NuGet Sentry).
- **Fix(es):**          [Fix 1] Pin to `sentry-sdk[opentelemetry]>=2.66.0`.
- **Code Sample:**      [

                        # Server/requirements.txt
                        sentry-sdk[opentelemetry]>=2.19  # ← 47 minor versions behind
                        # Latest: 2.66.1

                        ]

---

- **[A034]**            Server uses pip not uv — no lockfile, no reproducible builds, no
                        transitive dependency pinning
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      High. Without a lockfile, every deployment resolves dependencies
                        non-deterministically. A new deployment could pull in a vulnerable
                        version of a transitive dependency (e.g., idna, cryptography) without
                        any warning.
- **Location:**         Server/ (no uv.lock, no Pipfile.lock, no poetry.lock)
- **Issue:**            `Server/pyproject.toml` has no `[project]` section — it is purely
                        tool configuration (ruff, bandit, pytest, ty overrides).
                        `Server/requirements.txt` is the canonical dependency declaration
                        with no lockfile. There is no `[tool.uv]` section. The `idna` package
                        (critical for DNSSEC IDN validation) is a transitive dependency of
                        cryptography with no version pin.
- **Cross-ref. Delta:** A035 (idna explicit dependency).
- **Fix(es):**          [Fix 1] Add a proper `[project]` section to `Server/pyproject.toml`.
                        [Fix 2] Add `[tool.uv]` section and generate `uv.lock`. [Fix 3] Pin
                        all transitive dependencies via the lockfile.
- **Code Sample:**      [

                        # Server/pyproject.toml — no [project] section
                        # Server/requirements.txt — only source of truth
                        # No lockfile exists
                        # Transitive deps (idna, etc.) are unpinned

                        ]

---

- **[A035]**            idna package is a transitive dependency of cryptography — should be
                        explicit direct dependency for DNSSEC IDN validation
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      Medium. The known dnssec_validator.py IDN defect (A013) is directly
                        related to the resolved idna version. Without an explicit pin, the
                        resolved version is non-deterministic. idna 2.x and 3.x have
                        different IDNA 2003 vs 2008 behavior.
- **Location:**         Server/requirements.txt (idna not listed)
- **Issue:**            `idna` is a transitive dependency of `cryptography` but is not listed
                        in `requirements.txt`. With `cryptography>=41.0.0`, the resolved
                        idna version depends on the solver and could be anything from 2.1 to
                        3.18. idna 3.x is a complete rewrite vs 2.x with different IDNA 2008
                        compliance. Adding it as an explicit dependency makes it visible and
                        auditable.
- **Cross-ref. Delta:** A013 (IDN domain corruption in dnssec_validator).
- **Fix(es):**          [Fix 1] Add `idna>=3.7` as an explicit direct dependency in
                        `requirements.txt`.
- **Code Sample:**      [

                        # Server/requirements.txt — missing:
                        # idna>=3.7
                        # Currently resolved transitively via cryptography
                        # Version depends on solver — could be 2.1 to 3.18

                        ]

---

- **[A036]**            brotli is 1 minor version behind — missing performance improvements
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      Low. No known CVEs. Performance improvements in 1.2.0.
- **Location:**         Server/requirements.txt (constraint: `brotli>=1.1.0`)
- **Issue:**            Constraint allows 1.1.0 through 1.2.0. Latest is 1.2.0.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Bump to `brotli>=1.2.0`.
- **Code Sample:**      [

                        # Server/requirements.txt
                        brotli>=1.1.0  # Latest: 1.2.0

                        ]

---

- **[B014]**            sentry-sdk 47 minor versions behind — missing OTel interop fixes could
                        cause silent error data loss in production monitoring
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      High. Sentry is the error telemetry pipeline for the Server. Missing
                        47 versions of bug fixes and OTel interop improvements means
                        production errors may be silently dropped or misattributed.
- **Location:**         Server/requirements.txt (constraint: `sentry-sdk[opentelemetry]>=2.19`)
- **Issue:**            Every version between 2.19 and 2.66.1 contains bug fixes for OTel
                        span export, error context capture, and performance improvements.
                        Without these fixes, production monitoring is degraded.
- **Cross-ref. Delta:** A033 (same issue, Server domain).
- **Fix(es):**          [Fix 1] Pin to `sentry-sdk[opentelemetry]>=2.66.0`.
- **Code Sample:**      [

                        # Server/requirements.txt
                        sentry-sdk[opentelemetry]>=2.19  # ← 47 versions behind
                        # Latest: 2.66.1

                        ]

---

- **[C005]**            cryptography CVE-2026-69249 exposes Ed25519 leak attestation signing
                        to DoS and potential compromise
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      Critical. The Server uses cryptography for Ed25519 leak attestation
                        signing (CF-01). CVE-2026-69249 (CVSS 8.7) allows DoS via exponential
                        path-building. An attacker could cause attestation failures or,
                        combined with other vulnerabilities, compromise attestation integrity.
- **Location:**         Server/requirements.txt (constraint: `cryptography>=41.0.0`);
                        Server/syxx_server/leak.py (CF-01 attestation signing)
- **Issue:**            The cryptography library is used for Ed25519 signing in the
                        collector-attested leak verification pipeline. The current version
                        constraint allows versions vulnerable to CVE-2026-69249 and multiple
                        OpenSSL advisories. This is the highest-severity finding in the audit.
- **Cross-ref. Delta:** A032 (same issue, Server domain).
- **Fix(es):**          [Fix 1] Pin to `cryptography>=49.0.0`.
                        [Fix 2] Verify attestation
                        signing still works after upgrade. [Fix 3] Add regression test for
                        Ed25519 attestation signing.
- **Code Sample:**      [

                        # Server/requirements.txt
                        cryptography>=41.0.0  # CVE-2026-69249 (CVSS 8.7)
                        # Used in leak.py for Ed25519 attestation signing

                        ]

---

- **[D034]**            sentry-cocoa pinned to 8.x — 1 major version behind, 8.x end-of-life
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      High. 8.x is end-of-life — no more bug fixes or security patches.
                        9.0.0 shipped July 2025 (over a year ago). 9.x requires
                        swift-tools-version 6.0 and macOS 14 minimum, which the project
                        already meets.
- **Location:**         Package.swift; macOS/Package.swift
                        (constraint: `.package(url: "...sentry-cocoa.git", from: "8.43.0")`)
- **Issue:**            The `from: "8.43.0"` constraint prevents SPM from resolving to 9.x.
                        Resolved version is 8.58.4 (latest 8.x). Latest overall is 9.25.0.
                        9.0.0 breaking changes: `SentryIntegrationProtocol` removed,
                        `SentryLevel` type change, performance V2 mandatory, user feedback
                        API replaced. Migration requires API surface changes.
- **Cross-ref. Delta:** A033 (Python sentry-sdk), D036 (NuGet Sentry).
- **Fix(es):**          [Fix 1] Update constraint to `from: "9.0.0"` in both Package.swift
                        files. [Fix 2] Audit SyxxTelemetry for SentryIntegrationProtocol
                        usage and SentryLevel references. [Fix 3] Test sentry-cocoa
                        integration after migration.
- **Code Sample:**      [

                        // Package.swift
                        .package(url: "https://github.com/getsentry/sentry-cocoa.git",
                                 from: "8.43.0")  // ← 1 major behind
                        // Resolves to 8.58.4. Latest: 9.25.0

                        ]

---

- **[D035]**            opentracing-objc and thrift-swift are transitive deps from unmaintained
                        undefinedlabs repos — no security patching
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      High. Both packages are from `undefinedlabs` (acquired by Datadog in
                        2021). 0 stars, 0 watchers, no activity since acquisition. If a
                        vulnerability is discovered, there is no maintainer to ship a fix.
- **Location:**         Package.resolved (transitive via opentelemetry-swift → grpc-swift)
- **Issue:**            `opentracing-objc` 0.5.2 and `thrift-swift` 1.1.2 are pulled in
                        transitively by `opentelemetry-swift` (the OTLP gRPC exporter). They
                        use outdated Swift patterns and have no maintainer. If the project
                        only uses the HTTP exporter (not gRPC), these may be dead transitive
                        dependencies.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Verify whether gRPC export is actually used.
                        [Fix 2] If only
                        HTTP export is used, drop the gRPC exporter product dependency to
                        eliminate these transitives. [Fix 3] File an issue with
                        opentelemetry-swift asking them to replace these dependencies.
- **Code Sample:**      [

                        # Package.resolved — transitive deps:
                        # opentelemetry-swift → grpc-swift →
                        #   thrift-swift 1.1.2 (undefinedlabs, 0 stars)
                        #   opentracing-objc 0.5.2 (undefinedlabs, 0 stars)
                        # Both unmaintained since Datadog acquisition (2021)

                        ]

---

- **[D036]**            Sentry NuGet is 2 major versions behind — 4.13.0 → 6.8.0 across 3
                        projects
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      High. Three projects (`Syxx`, `Syxx.Telemetry`, `SyxxUI`) are on
                        Sentry 4.13.0. Latest is 6.8.0 — a gap of two major versions (5.x
                        and 6.x). Missing bug fixes, performance improvements, and new
                        platform support.
- **Location:**         Windows/dotNET/Syxx/Syxx.csproj;
                        Windows/dotNET/Syxx.Telemetry/Syxx.Telemetry.csproj;
                        Windows/dotNET/SyxxUI/SyxxUI.csproj
                        (all: `<PackageReference Include="Sentry" Version="4.13.0" />`)
- **Issue:**            All three projects pin Sentry 4.13.0. The `Syxx.Telemetry.csproj`
                        has an exemplary comment documenting the OpenTelemetry version pin
                        rationale — but no equivalent warning exists for the Sentry lag.
- **Cross-ref. Delta:** A033 (Python sentry-sdk), D034 (Swift sentry-cocoa).
- **Fix(es):**          [Fix 1] Review Sentry 5.0 and 6.0 changelogs for breaking changes.
                        [Fix 2] Plan staged upgrade: 4.13 → 5.x → 6.x with test validation
                        at each step. [Fix 3] Add version-pin documentation similar to the
                        OTel comment in Syxx.Telemetry.csproj.
- **Code Sample:**      [

                        <!-- Syxx.csproj, Syxx.Telemetry.csproj, SyxxUI.csproj -->
                        <PackageReference Include="Sentry" Version="4.13.0" />
                        <!-- Latest: 6.8.0 — 2 major versions behind -->

                        ]

---

- **[D037]**            Missing Directory.Packages.props for centralized NuGet version
                        management
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      Medium. While there is currently no version drift across 9 .csproj
                        files, this is fragile. A future developer adding a package could
                        introduce a different version. Centralized management would also make
                        upgrading Sentry (used in 3 projects) a single-line change.
- **Location:**         Windows/dotNET/ (no Directory.Packages.props exists)
- **Issue:**            Package versions are repeated across 7 .csproj files. No
                        `Directory.Packages.props` for centralized versioning. No
                        `Directory.Build.props` for shared MSBuild properties (`Nullable`,
                        `ImplicitUsings`, `TreatWarningsAsErrors`).
- **Cross-ref. Delta:** D023 (SDK pin), D024 (Directory.Build.props).
- **Fix(es):**          [Fix 1] Add `Directory.Packages.props` with all package versions.
                        [Fix 2] Add `Directory.Build.props` for shared MSBuild properties.
- **Code Sample:**      [

                        # Missing from Windows/dotNET/:
                        # Directory.Packages.props — centralized NuGet versions
                        # Directory.Build.props — shared MSBuild properties

                        ]

---

- **[D038]**            Microsoft.NET.Test.Sdk is 1 major version behind — 17.11.1 → 18.8.1
                        across 4 test projects
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      Medium. 17.x → 18.x is a major bump. May bring new test platform
                        features. No known CVEs.
- **Location:**         4 test .csproj files (all: `Version="17.11.1"`)
- **Issue:**            All 4 test projects pin 17.11.1. Latest is 18.8.1.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Evaluate 18.x compatibility.
                        [Fix 2] Upgrade if no breaking
                        changes.
- **Code Sample:**      [

                        <!-- 4 test projects -->
                        <PackageReference Include="Microsoft.NET.Test.Sdk"
                                          Version="17.11.1" />
                        <!-- Latest: 18.8.1 -->

                        ]

---

- **[D039]**            xunit.runner.visualstudio is 1 major version behind — 2.8.2 → 3.1.5
                        across 4 test projects
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      Medium. 2.x → 3.x is a major bump. Since the project uses xunit
                        2.9.2 (not xunit v3), runner 3.x may have compatibility constraints.
                        Verify the migration path before upgrading.
- **Location:**         4 test .csproj files (all: `Version="2.8.2"`)
- **Issue:**            All 4 test projects pin 2.8.2. Latest is 3.1.5. Must verify xunit
                        v2 → v3 runner compatibility.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Verify xunit v2 compatibility with runner 3.x.
                        [Fix 2]
                        Upgrade if compatible, or stay on 2.x if not.
- **Code Sample:**      [

                        <!-- 4 test projects -->
                        <PackageReference Include="xunit.runner.visualstudio"
                                          Version="2.8.2" />
                        <!-- Latest: 3.1.5 — check xunit v2 compat -->

                        ]

---

- **[D040]**            Microsoft.Windows.SDK.BuildTools is 1 major version behind
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      Medium. Tied to Windows SDK tooling. Upgrade requires testing WinUI
                        packaging.
- **Location:**         SyxxUI.csproj; SyxxUI.Tests.csproj
                        (all: `Version="10.0.26100.4654"`)
- **Issue:**            Current is 10.0.26100.4654. Latest is 10.0.28000.2526.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Evaluate SDK compatibility.
                        [Fix 2] Upgrade if no breaking
                        changes.
- **Code Sample:**      [

                        <!-- SyxxUI.csproj, SyxxUI.Tests.csproj -->
                        <PackageReference Include="Microsoft.Windows.SDK.BuildTools"
                                          Version="10.0.26100.4654" />
                        <!-- Latest: 10.0.28000.2526 -->

                        ]

---

- **[D041]**            OpenTelemetry NuGet is 1 minor version behind — 1.16.0 → 1.17.0
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      Low. Minor version bump, likely safe. No known CVEs. The project
                        already documents the prior CVE fix (GHSA-g94r-2vxg-569j +
                        GHSA-4625-4j76-fww9) in Syxx.Telemetry.csproj.
- **Location:**         Syxx.Telemetry.csproj; SyxxUI.csproj (all: `Version="1.16.0"`)
- **Issue:**            Both projects pin 1.16.0. Latest is 1.17.0.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Bump to 1.17.0.
                        [Fix 2] Update the version-pin documentation
                        comment.
- **Code Sample:**      [

                        <!-- Syxx.Telemetry.csproj, SyxxUI.csproj -->
                        <PackageReference Include="OpenTelemetry" Version="1.16.0" />
                        <!-- Latest: 1.17.0 -->

                        ]

---

- **[D042]**            Microsoft.WindowsAppSDK is 1 minor version behind — 2.2.0 → 2.3.1
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      Low. Minor version bump. Test WinUI packaging before deploying.
- **Location:**         SyxxUI.csproj; SyxxUI.Tests.csproj (all: `Version="2.2.0"`)
- **Issue:**            Both projects pin 2.2.0. Latest is 2.3.1.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Bump to 2.3.1.
                        [Fix 2] Test WinUI packaging.
- **Code Sample:**      [

                        <!-- SyxxUI.csproj, SyxxUI.Tests.csproj -->
                        <PackageReference Include="Microsoft.WindowsAppSDK"
                                          Version="2.2.0" />
                        <!-- Latest: 2.3.1 -->

                        ]

---

- **[D043]**            Package.resolved drift between root and macOS/ Swift build environments
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      Low. Two build environments produce slightly different dependency
                        graphs. Not a functional issue but means builds are not reproducible.
- **Location:**         Package.resolved (root); macOS/Package.resolved
- **Issue:**            `macOS/Package.resolved` and root `Package.resolved` diverge on
                        `swift-certificates` (1.19.3 vs 1.19.4) and `swift-nio-http2`
                        (1.44.0 vs 1.45.0). The root resolved file is slightly newer.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Delete both Package.resolved files and re-resolve from a
                        single canonical environment. [Fix 2] Or commit only one resolved
                        file and add the other to .gitignore.
- **Code Sample:**      [

                        # Package.resolved (root):
                        #   swift-certificates: 1.19.4
                        #   swift-nio-http2: 1.45.0
                        # macOS/Package.resolved:
                        #   swift-certificates: 1.19.3  ← older
                        #   swift-nio-http2: 1.44.0     ← older

                        ]

---

- **[E013]**            colorthief is unmaintained since Feb 2017 — Python 2 only, will not
                        install on Python 3.12+
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      Critical. Last release February 2017 (9.5 years ago). PyPI
                        classifiers: Python 2.6, 2.7 only. "Development Status :: 4 - Beta".
                        The `requires-python` metadata may block installation, or the package
                        may use syntax incompatible with Python 3.
- **Location:**         tools/yt-design-extractor/pyproject.toml
                        (constraint: `"colorthief>=0.2.1"`)
- **Issue:**            This package is unmaintained and will not work on modern Python.
                        It is used in the yt-design-extractor tool for color palette
                        extraction from YouTube thumbnails.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Replace with Pillow's built-in color analysis
                        (`Image.getcolors()`, `Image.quantize()`) or `colorgram.py`.
                        [Fix 2] Remove `colorthief` from pyproject.toml.
- **Code Sample:**      [

                        # tools/yt-design-extractor/pyproject.toml
                        "colorthief>=0.2.1",  # UNMAINTAINED since Feb 2017
                        # Python 2 only per PyPI classifiers

                        ]

---

- **[E014]**            Pillow constraint >=10.0.0 allows vulnerable versions — 11 HIGH CVEs
                        in 10.4.0, zero in 12.3.0
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      High. The `>=10.0.0` constraint means pip could resolve to any 10.x,
                        11.x, or 12.x version. Pillow 10.4.0 alone has 11 High, 3 Medium,
                        1 Low severity vulnerabilities. Version 12.3.0 has zero known
                        vulnerabilities.
- **Location:**         tools/yt-design-extractor/pyproject.toml
                        (constraint: `"Pillow>=10.0.0"`)
- **Issue:**            Without a lockfile, the resolved version is non-deterministic. If a
                        deployment resolves to an old 10.x version, every deployment is
                        exposed to known vulnerabilities.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Change constraint to `"Pillow>=12.3.0"`.
- **Code Sample:**      [

                        # tools/yt-design-extractor/pyproject.toml
                        "Pillow>=10.0.0",  # ← allows vulnerable versions
                        # 11 HIGH CVEs in 10.4.0. Zero in 12.3.0.

                        ]

---

- **[E015]**            No automated dependency vulnerability scanning in CI for any ecosystem
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      High. The project has no automated scanning for dependency
                        vulnerabilities. Python, NuGet, and Swift all have available tools
                        (pip-audit, dotnet list package --vulnerable, SPM audit) but none
                        are configured in CI.
- **Location:**         .github/workflows/ (no dependency audit workflow)
- **Issue:**            The existing CI workflows (ci.yml, validate.yml, code-quality.yml)
                        do not include dependency vulnerability scanning. A new vulnerable
                        dependency could be introduced without detection.
- **Cross-ref. Delta:** A032, C005, E014.
- **Fix(es):**          [Fix 1] Add `pip-audit` step to CI for Python dependencies.
                        [Fix 2] Add `dotnet list package --vulnerable` step for NuGet.
                        [Fix 3] Add `npm audit` step (future-proofing). [Fix 4] Schedule
                        weekly scans on cron.
- **Code Sample:**      [

                        # No dependency audit workflow exists.
                        # Recommended additions to CI:
                        # - pip-audit -r Server/requirements.txt
                        # - dotnet list package --vulnerable
                        # - npm audit (future-proofing)

                        ]

---

- **[F009]**            Multiple Python packages behind latest — pyyaml, typer, ruff, ty,
                        pytesseract, youtube-transcript-api
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      Low. No known CVEs in the gaps. Minor version and patch bumps.
- **Location:**         plugins/plugin-eval/pyproject.toml; tools/yt-design-extractor/pyproject.toml
- **Issue:**            `pyyaml` 6.0 → 6.0.3 (3 patch), `typer` 0.26.8 → 0.27.1 (1 minor),
                        `ruff` 0.15.21 → 0.16.2 (1 minor), `ty` 0.0.59 → 0.0.69 (10 patch),
                        `pytesseract` 0.3.10 → 0.3.13 (3 patch), `youtube-transcript-api`
                        0.6.0 → 1.2.4 (1 major).
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Bump all to latest.
                        [Fix 2] For youtube-transcript-api,
                        verify 1.x API compatibility (may have breaking changes).
- **Code Sample:**      [

                        # plugin-eval/pyproject.toml
                        "pyyaml>=6.0",     # Latest: 6.0.3
                        "typer>=0.26.8",   # Latest: 0.27.1
                        "ruff>=0.15.21",   # Latest: 0.16.2
                        "ty>=0.0.59",      # Latest: 0.0.69
                        # yt-design-extractor/pyproject.toml
                        "pytesseract>=0.3.10",           # Latest: 0.3.13
                        "youtube-transcript-api>=0.6.0", # Latest: 1.2.4

                        ]

---

- **[F010]**            Node.js package.json is empty — no metadata, no private flag, no
                        engine requirements
- **Status:**           **OPEN** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Criticality:**      Low. The file is an empty object `{}`. At minimum, it should declare
                        `"name": "Syxx"`, `"private": true`, and a `"version"` field to
                        prevent accidental publication and provide metadata for tooling.
- **Location:**         package.json (root)
- **Issue:**            The root `package.json` contains only `{}`. It has no name, no
                        private flag, no version, and no engine requirements. While no npm
                        dependencies exist today, this file should be properly initialized
                        for future use.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Populate with `"name": "Syxx"`, `"private": true`,
                        `"version": "1.0.0"`, `"engines": { "node": ">=20.0.0" }`.
- **Code Sample:**      [

                        // package.json (root)
                        {}

                        ]

---

- **[A037]**            do_DELETE has no CSRF protection while do_POST does — attacker can
                        delete monitors via fetch() with DELETE method
- **Status:**           **OPEN** — Found during comprehensive-review code-review
                        (ultra effort) 2026-08-09.
- **Criticality:**      Critical. An attacker hosting a page on evil.com can use
                        `fetch()` with `method: "DELETE"` to hit `do_DELETE`, which has no
                        CSRF check, and delete the user's monitors. `do_POST` has CSRF
                        protection via `_check_csrf()` but `do_DELETE` does not.
- **Location:**         Server/syxx_server/app.py:996
- **Issue:**            `do_POST` (line ~863) calls `_check_csrf()` for all routes except
                        webhooks. `do_DELETE` (line 996) handles `_route_revoke_api_key`
                        and `_route_delete_monitor` — both state-changing operations — with
                        no CSRF check. The `_check_csrf()` method was added in this diff
                        but only wired into `do_POST`.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add the same CSRF check at the top of `do_DELETE`:
                        `if path not in ("/api/webhooks/stripe", "/api/webhooks/keygen"):
                        if not self._check_csrf(): return`
- **Code Sample:**      [

                        # app.py:996 — do_DELETE handles state-changing operations
                        def do_DELETE(self):
                            # NO _check_csrf() call here
                            path = urlparse(self.path).path
                            if path == "/api/keys":
                                self._route_revoke_api_key()
                            elif path.startswith("/api/monitors/"):
                                self._route_delete_monitor()

                        ]

---

- **[A038]**            _client_ip returns loopback (127.0.0.1) on malformed XFF, enabling
                        rate-limit evasion via shared IP bucket
- **Status:**           **OPEN** — Found during comprehensive-review code-review
                        (ultra effort) 2026-08-09.
- **Criticality:**      High. An attacker behind a reverse proxy sends a request with
                        `X-Forwarded-For: garbage`. The SEC-035 validation detects the
                        malformed entry and returns `peer` (loopback address 127.0.0.1).
                        All such requests share the same rate-limit key, so the attacker
                        can flood the server without triggering per-IP rate limits.
- **Location:**         Server/syxx_server/app.py (SEC-035 XFF validation)
- **Issue:**            The fallback to `peer` is the same loopback address for every
                        request, making it a shared rate-limit bucket. When XFF is
                        malformed, a unique-per-request identifier or dedicated sentinel
                        should be used.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Return a unique-per-request identifier (e.g., hash of the
                        raw XFF + peer). [Fix 2] At minimum, include the malformed XFF
                        value in the rate key.
- **Code Sample:**      [

                        # app.py — SEC-035 XFF validation
                        for part in parts:
                            try:
                                ipaddress.ip_address(part[:45])
                            except ValueError:
                                logging_util.warn("client_ip_malformed_xff", value=part[:32])
                                return peer  # ← 127.0.0.1 for ALL malformed requests

                        ]

---

- **[A039]**            HSTS emitted via X-Forwarded-Proto without peer validation — attacker
                        could trigger HSTS if server exposed beyond loopback
- **Status:**           **OPEN** — Found during comprehensive-review security audit
                        (ultra effort) 2026-08-09.
- **Criticality:**      Medium. If the Python server were ever exposed to a non-loopback
                        network, an attacker could send `X-Forwarded-Proto: https` and
                        trigger HSTS emission. The shipped topology binds to loopback
                        (nginx handles TLS), but there is no guard correlating the
                        X-Forwarded-Proto trust with the loopback check.
- **Location:**         Server/syxx_server/app.py:654
- **Issue:**            The `X-Forwarded-Proto` branch does not verify the peer is on
                        loopback before trusting the header. This is low-risk in the
                        shipped topology but would be exploitable in alternative
                        deployments.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Gate the `X-Forwarded-Proto` branch on
                        `peer_addr.is_loopback`: `elif peer_addr.is_loopback and
                        self.headers.get("X-Forwarded-Proto") == "https":`
- **Code Sample:**      [

                        # app.py:654
                        elif self.headers.get("X-Forwarded-Proto") == "https":
                            self.send_header("Strict-Transport-Security",
                                "max-age=31536000; includeSubDomains")

                        ]

---

- **[B015]**            Swift Forensics module (1,173 lines) has zero tests — C# twin has
                        comprehensive ForensicsAnalyzerTests
- **Status:**           **OPEN** — Found during comprehensive-review code-review
                        (ultra effort) 2026-08-09.
- **Criticality:**      Critical. Forensics is a security-critical analysis path. Behavioral
                        vectors exist for forensics scenarios, but without Swift tests, only
                        the C# side validates them. A Swift regression in beacon detection,
                        exfiltration classification, or list-based analysis would ship
                        silently.
- **Location:**         macOS/Sources/SyxxKit/Forensics/ (1,173 lines, no test file);
                        Windows/dotNET/Syxx.Tests/ForensicsAnalyzerTests.cs (451 lines)
- **Issue:**            The C# twin has `ForensicsAnalyzerTests.cs` (451 lines). The Swift
                        side has no corresponding test file. No `ForensicsAnalyzerTests.swift`
                        exists in `macOS/Tests/`.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Create `macOS/Tests/SyxxKitTests/ForensicsAnalyzerTests.swift`
                        consuming the same behavioral vectors as the C# tests.
                        [Fix 2] Use C# `ForensicsAnalyzerTests.cs` as a template.
- **Code Sample:**      [

                        # macOS/Tests/ — no ForensicsAnalyzerTests.swift exists
                        # Windows/dotNET/Syxx.Tests/ForensicsAnalyzerTests.cs — 451 lines
                        # Behavioral vectors: corpus/behavioral-vectors/001-020

                        ]

---

- **[B016]**            CaptureEvents.ClassifyWindow triple-enumerates windowed records —
                        O(3n) vs O(n) on daemon hot path
- **Status:**           **OPEN** — Found during comprehensive-review performance review
                        (ultra effort) 2026-08-09.
- **Criticality:**      Critical. Called every second by the daemon poller. Three
                        independent LINQ enumerations over the same `inWindow` collection
                        where a single pass would suffice. For 500-1000 records per window,
                        this is 1500-3000 needless string comparisons per tick.
- **Location:**         Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs:170-210
- **Issue:**            `ClassifyWindow` performs three scans: `inWindow.Any(...)` for
                        PlainTextLeak, `inWindow.Where(...).ToList()` for rcode tallies,
                        and `inWindow.Count(...)` for ResponseLossHigh. Each scan does
                        string matching on `Network.Direction`.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Single-pass enumeration with accumulators for outbound
                        count, inbound count, nxdomain count, and servfail count.
- **Code Sample:**      [

                        // CaptureEvents.cs:170-210 — three scans:
                        inWindow.Any(r => r.Network.Direction.Contains("Outbound", ...))
                        inWindow.Where(r => r.Network.Direction.Contains("Inbound", ...)).ToList()
                        inWindow.Count(r => r.Network.Direction.Contains("Outbound", ...))

                        ]

---

- **[B017]**            ParityVectorLoader.Walker.Done() does O(n log n) key-sort on every
                        call — ~500+ calls per corpus load
- **Status:**           **OPEN** — Found during comprehensive-review performance review
                        (ultra effort) 2026-08-09.
- **Criticality:**      Critical. `Walker.Done()` sorts ALL remaining keys just to find the
                        first unknown one. With `DecodeSectionInputs` calling Walker for
                        32+ field decoders, each with nested objects, this fires hundreds of
                        times per corpus load. Corpus loading latency scales poorly.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/ParityVectorLoader.cs:1650-1660
- **Issue:**            `var stray = _o.Select(kv => kv.Key).Where(k => !_read.Contains(k))
                        .OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();` — sorts
                        ALL remaining keys with O(n log n) just to find the first unknown.
                        The sort is unnecessary; the first unknown key found is the one that
                        fails the load.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Replace with single-pass scan: `foreach (var kv in _o) {
                        if (!_read.Contains(kv.Key)) throw Bad(...); }` Determinism is not
                        needed here — any key-order dependency in tests is a smell.
- **Code Sample:**      [

                        // ParityVectorLoader.cs:1650-1660
                        var stray = _o.Select(kv => kv.Key)
                            .Where(k => !_read.Contains(k))
                            .OrderBy(k => k, StringComparer.Ordinal) // O(n log n)
                            .FirstOrDefault();

                        ]

---

- **[B018]**            CageRedundancyCorrelator.swift (235 lines) has zero tests — pure logic
                        documented as "exhaustively testable"
- **Status:**           **OPEN** — Found during comprehensive-review code-review
                        (ultra effort) 2026-08-09.
- **Criticality:**      High. The correlator is the honesty-spine mechanism for dual-capture
                        agreement/disagreement. Its own documentation says it's "PURE logic
                        — no I/O, no root, no BPF" and "exhaustively testable." Zero tests
                        exist on either platform.
- **Location:**         macOS/Sources/SyxxKit/CageRedundancyCorrelator.swift (235 lines)
- **Issue:**            A bug in matching logic (CageQueryKey hashing, agreement scoring,
                        or disagreement classification) would produce false "clean" or false
                        "flagged" results. Pure logic with no I/O means there's no excuse for
                        not testing.
- **Cross-ref. Delta:** D047 (no C# twin), D050 (performance: four set copies).
- **Fix(es):**          [Fix 1] Create
                        `macOS/Tests/SyxxKitTests/CageRedundancyCorrelatorTests.swift`
                        with synthetic fixture data for perfect agreement, partial overlap,
                        complete disagreement, XID collision, and empty captures.
- **Code Sample:**      [

                        // CageRedundancyCorrelator.swift — header comment:
                        // "PURE logic — no I/O, no root, no BPF — so it lives in
                        //  SyxxKit and is exhaustively testable."
                        // No test file exists.

                        ]

---

- **[B019]**            DNSPathConfirmation.swift (318 lines) has zero tests — accuracy-critical
                        DNS encryption claim
- **Status:**           **OPEN** — Found during comprehensive-review code-review
                        (ultra effort) 2026-08-09.
- **Criticality:**      High. This is the definitive-tier DNS path confirmation engine that
                        answers "is DNS actually encrypted right now?" by combining
                        daemon-side observations. A bug in tri-state merge logic would
                        produce misleading security claims.
- **Location:**         macOS/Sources/SyxxKit/DNSPathConfirmation.swift (318 lines)
- **Issue:**            318 lines of transport state logic, tri-state merging, and flavor
                        disambiguation. Zero tests. The honesty-spine constraints (lines
                        29-44) are only enforced by documentation, not by tests.
- **Cross-ref. Delta:** D048 (macOS-only, no C# equivalent).
- **Fix(es):**          [Fix 1] Create `macOS/Tests/SyxxKitTests/DNSPathConfirmationTests.swift`
                        testing all tri-state combinations, flavor disambiguation
                        (DoH vs DoT vs DNSCrypt), and honesty-spine invariants.
- **Code Sample:**      [

                        // DNSPathConfirmation.swift — 318 lines, zero tests
                        // Honesty-spine constraints (lines 29-44):
                        // - VPN ≠ encrypted DNS
                        // - unknown ≠ plaintext
                        // - tri-state merge logic
                        // No test file exists.

                        ]

---

- **[B020]**            19 unique test files quarantined in macOS _quarantine/ — SynthesisEngine
                        fully quarantined with zero active tests
- **Status:**           **OPEN** — Found during comprehensive-review code-review
                        (ultra effort) 2026-08-09. Previously noted in B002 but severity
                        escalated after confirming SynthesisEngine has NO active tests.
- **Criticality:**      High. The SynthesisEngineTests are fully quarantined — there is no
                        active `SynthesisEngineTests.swift` in the test directory. The
                        synthesis engine, the core of Syxx's DNS integrity analysis, has
                        zero regression coverage.
- **Location:**         macOS/Tests/_quarantine/ (44 files: 19 unique, 14 .phase32-active-dup,
                        9 .compile-break, 2 .phase1-compile-break)
- **Issue:**            `SyxxKitTests.swift.phase32-compile-break` (2,119 lines) is the
                        largest single test file and cannot compile under Swift 6.
                        `SynthesisEngineTests.swift.phase1-compile-break` (574 lines) has
                        no active counterpart. The `.phase32-active-dup` files are exact
                        duplicates of active tests.
- **Cross-ref. Delta:** B002 (208 quarantined test methods), D008 (23 duplicate files).
- **Fix(es):**          [Fix 1] Delete 14 `.phase32-active-dup` files (exact duplicates).
                        [Fix 2] Fix `SyxxKitTests.swift.phase32-compile-break` (2,119 lines).
                        [Fix 3] Fix `SynthesisEngineTests.swift.phase1-compile-break`.
                        [Fix 4] Fix remaining 7 `.compile-break` files.
- **Code Sample:**      [

                        # macOS/Tests/_quarantine/ — 44 files:
                        # 19 unique test files (not running)
                        # 14 .phase32-active-dup (exact duplicates — delete)
                        # 9 .compile-break (cannot compile under Swift 6)
                        # 2 .phase1-compile-break (SynthesisEngineTests fully gone)

                        ]

---

- **[B021]**            CaptureEvents.cs (407 lines) has zero tests — new Windows capture
                        events module
- **Status:**           **OPEN** — Found during comprehensive-review code-review
                        (ultra effort) 2026-08-09.
- **Criticality:**      High. Capture events are the Windows equivalent of the macOS BPF
                        capture path. Untested event parsing, filtering, or classification
                        would produce false negatives in DNS tampering detection.
- **Location:**         Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs (407 lines)
- **Issue:**            No test file references `CaptureEvents` anywhere in
                        `Windows/dotNET/Syxx.Tests/`. Event parsing, filtering, and
                        classification logic is entirely untested.
- **Cross-ref. Delta:** B016 (triple enumeration performance), D046 (CaptureEventGate
                        not thread-safe).
- **Fix(es):**          [Fix 1] Create `Windows/dotNET/Syxx.Tests/CaptureEventsTests.cs`
                        with tests for event parsing, filtering, classification, and edge
                        cases.
- **Code Sample:**      [

                        # Windows/dotNET/Syxx.Tests/ — no CaptureEventsTests.cs exists
                        # CaptureEvents.cs — 407 lines, new in this diff

                        ]

---

- **[B022]**            ParityVectorLoader.RejectDuplicateMembers allocates HashSet per JSON
                        object — ~450 allocations per corpus load
- **Status:**           **OPEN** — Found during comprehensive-review performance review
                        (ultra effort) 2026-08-09.
- **Criticality:**      High. For a 30-case corpus with ~15 nested objects per case, this
                        is ~450 HashSet allocations. Each is small (2-10 elements), but the
                        allocation pressure and GC cost on corpus load is non-trivial.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/ParityVectorLoader.cs:380-410
- **Issue:**            `RejectDuplicateMembers` pushes a `new HashSet<string>` onto a
                        stack for every `StartObject` token. Not a hot path (corpus loading
                        is startup-only), but the allocation pattern is wasteful.
- **Cross-ref. Delta:** B017 (Walker.Done O(n log n) sort).
- **Fix(es):**          [Fix 1] Reuse a single `HashSet<string>` per scope, cleared on
                        `EndObject`, tracked by a count stack. [Fix 2] Or use a ref struct
                        approach with `Utf8JsonReader` to detect duplicates without
                        allocating per-object.
- **Code Sample:**      [

                        // ParityVectorLoader.cs:380-410
                        // new HashSet<string>(StringComparer.Ordinal) per StartObject
                        // For a 30-case corpus: ~450 HashSet allocations

                        ]

---

- **[B023]**            CaptureEvents rcode string matching fragile to producer changes —
                        classifier silently stops detecting NXDOMAIN floods
- **Status:**           **OPEN** — Found during comprehensive-review code-review
                        (ultra effort) 2026-08-09.
- **Criticality:**      Medium. If the cage CLI changes rcode formatting from "nxdomain" to
                        "NXDOMAIN" or "NXD", the classifier silently stops detecting
                        NXDOMAIN floods — no error, no warning.
- **Location:**         Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs
- **Issue:**            The classifier matches rcode via string literals:
                        `if (rc == "nxdomain" || rc == "nxdomain-records" || rc == "3")`.
                        The numeric rcode values (3 = NXDOMAIN, 2 = SERVFAIL) are used as
                        fallbacks, but the string names are not from a shared constant.
                        Any format change by the producer silently breaks classification.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Use numeric DNS rcode values as primary match (3 = NXDOMAIN,
                        2 = SERVFAIL). [Fix 2] Define a shared `Rcode` enum or constants in
                        `DNSCaptureRecord` so producer and classifier use same vocabulary.
- **Code Sample:**      [

                        // CaptureEvents.cs — string matching on rcode:
                        if (rc == "nxdomain" || rc == "nxdomain-records" || rc == "3")
                            nxdomain++;
                        else if (rc == "servfail" || rc == "2")
                            servfail++;

                        ]

---

- **[D044]**            cli-surface-gate.py (1,295 lines) has zero tests — only enforcement
                        mechanism for CLI surface parity
- **Status:**           **OPEN** — Found during comprehensive-review code-review
                        (ultra effort) 2026-08-09.
- **Criticality:**      Critical. The gate is the only thing preventing silent CLI-surface
                        divergence between platforms. If it has a bug (e.g., a false negative
                        in flag extraction), parity regressions ship unnoticed. The script
                        itself refactored flag comparison logic (commit f433760) without
                        tests.
- **Location:**         scripts/cli-surface-gate.py (1,295 lines, no test file)
- **Issue:**            A production-quality validation script that extracts CLI argument
                        surfaces from both Swift and C# source, compares them, and enforces
                        parity. No test file exists anywhere in the repo.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Create `scripts/tests/test_cli_surface_gate.py` with
                        fixture-based tests for the three extraction levels (TOP, GLOBAL,
                        SUBCOMMAND), including edge cases like `--verbose` vs `--version`
                        disambiguation.
- **Code Sample:**      [

                        # scripts/cli-surface-gate.py — 1,295 lines
                        # No test file exists anywhere in the repo.
                        # CLI surface extraction from Swift and C# source.
                        # Flag comparison logic refactored without tests.

                        ]

---

- **[D045]**            DicePassphrase generator missing from Swift — C# has full generator,
                        Swift only has wordlist data
- **Status:**           **OPEN** — Found during comprehensive-review architecture review
                        (ultra effort) 2026-08-09.
- **Criticality:**      Critical. macOS cannot generate witness-key passphrases using the
                        DicePassphrase method. C# doc states: "On Linux it is load-bearing…
                        On macOS it is additive." The macOS side of this additive feature
                        is missing. Swift wordlist doc comments reference types that don't
                        exist in the Swift codebase.
- **Location:**         macOS/Sources/SyxxKit/DicePassphraseWordlist.swift (102 lines);
                        Windows/dotNET/Syxx.Kit/DicePassphrase.cs (316 lines);
                        Linux/Ubuntu/SyxxUI.Avalonia.Tests/DicePassphraseTests.cs (337 lines)
- **Issue:**            C# has `DicePassphrase.cs` (316 lines, full generator + verifier)
                        AND `DicePassphraseWordlist.cs` (108 lines). Swift has ONLY
                        `DicePassphraseWordlist.swift` (102 lines). The wordlist doc comments
                        reference `DicePassphrase.wordlistSHA256` and
                        `DicePassphrase.canonicalWordlistText` — types that don't exist.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Port `DicePassphrase.cs` to Swift as
                        `macOS/Sources/SyxxKit/DicePassphrase.swift`. [Fix 2] Or update
                        wordlist doc comments to state the generator is C#-only.
- **Code Sample:**      [

                        // DicePassphraseWordlist.swift:4,10,19 — doc comments:
                        // References DicePassphrase.wordlistSHA256 (doesn't exist)
                        // References DicePassphrase.canonicalWordlistText (doesn't exist)
                        // C# has DicePassphrase.cs (316 lines) — Swift has no equivalent

                        ]

---

- **[D046]**            CaptureEventGate is not thread-safe — Dictionary accessed without
                        synchronization
- **Status:**           **OPEN** — Found during comprehensive-review code-review
                        (ultra effort) 2026-08-09.
- **Criticality:**      High. The daemon's poller thread calls `Filter()` every second. If
                        a diagnostic/HTTP endpoint also calls `Filter()`, concurrent access
                        to `_lastEmitted` Dictionary races, causing
                        `InvalidOperationException` or silent corruption.
- **Location:**         Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs:407
- **Issue:**            `private readonly Dictionary<CaptureEventKind, DateTime> _lastEmitted`
                        is accessed in `Filter()` without a lock or `ConcurrentDictionary`.
                        The comment says "The daemon calls ... once a second," implying
                        single-thread usage, but the class is `public sealed` with no
                        threading documentation.
- **Cross-ref. Delta:** B021 (CaptureEvents zero tests).
- **Fix(es):**          [Fix 1] Document "single-threaded only" on the class.
                        [Fix 2] Or
                        use `ConcurrentDictionary<CaptureEventKind, DateTime>`. [Fix 3] Or
                        add a `lock` around `_lastEmitted` access.
- **Code Sample:**      [

                        // CaptureEvents.cs:407
                        public sealed class CaptureEventGate
                        {
                            private readonly Dictionary<CaptureEventKind, DateTime>
                                _lastEmitted = new();
                            // Filter() accesses _lastEmitted without lock
                            if (_lastEmitted.TryGetValue(ev.Kind, out var last) ...)
                            _lastEmitted[ev.Kind] = now;
                        }

                        ]

---

- **[D047]**            CageRedundancyCorrelator — macOS-only pure logic, no C# twin exists
- **Status:**           **OPEN** — Found during comprehensive-review architecture review
                        (ultra effort) 2026-08-09.
- **Criticality:**      High. The correlator is pure logic (no I/O, no root, no BPF) and
                        explicitly documented as reusable by "a future Windows Cage twin."
                        No C# twin exists in Windows/dotNET/Syxx.Kit/. The Cage capture
                        feature exists on Windows, so the correlator is useful cross-platform.
- **Location:**         macOS/Sources/SyxxKit/CageRedundancyCorrelator.swift (235 lines)
- **Issue:**            The file states: "A future Windows Cage twin can reuse it
                        unchanged." Yet no C# twin exists. This is a cross-platform parity
                        gap — macOS has two-capture-stream correlation; Windows does not.
- **Cross-ref. Delta:** B018 (zero tests), D050 (performance: four set copies).
- **Fix(es):**          [Fix 1] Port to C# as `Windows/dotNET/Syxx.Kit/CageRedundancyCorrelator.cs`.
                        The port should be mechanical since it's pure logic with declared
                        Codable/Sendable types. [Fix 2] Or document as deliberate gap.
- **Code Sample:**      [

                        // CageRedundancyCorrelator.swift — header:
                        // "PLATFORM SCOPE. The correlator is PURE logic — no I/O,
                        //  no root, no BPF — so it lives in SyxxKit and is
                        //  exhaustively testable. A future Windows Cage twin
                        //  can reuse it unchanged;"
                        // No C# twin exists in Windows/dotNET/Syxx.Kit/

                        ]

---

- **[D048]**            DNSPathConfirmation — macOS-only daemon communication, no C# equivalent
                        for encrypted-DNS verification
- **Status:**           **OPEN** — Found during comprehensive-review architecture review
                        (ultra effort) 2026-08-09.
- **Criticality:**      High. The definitive-tier encrypted-DNS question (Q1+Q3 confirmed
                        from the privileged daemon) cannot be answered on Windows. The
                        file's comments say the daemon sees "both the resolver configuration
                        AND the DNSCrypt proxy process" — Windows has no equivalent
                        verification path.
- **Location:**         macOS/Sources/SyxxKit/DNSPathConfirmation.swift (318 lines)
- **Issue:**            This file communicates with the DeepSyxx launchd daemon over XPC.
                        It's inherently macOS-specific at the transport layer, but the
                        `EncryptedDNSFlavor` enum and tri-state logic are reusable. No
                        equivalent exists for Windows.
- **Cross-ref. Delta:** B019 (zero tests).
- **Fix(es):**          [Fix 1] Document this as a deliberate platform gap (like Cage's BPF
                        dependency). [Fix 2] If Windows has an equivalent privileged
                        observation path, port the logic layer. [Fix 3] Explicitly note in
                        file header that this is macOS-only by design.
- **Code Sample:**      [

                        // DNSPathConfirmation.swift — communicates with DeepSyxx daemon
                        // over XPC. Sees both resolver config AND DNSCrypt proxy process.
                        // No Windows equivalent exists.

                        ]

---

- **[D049]**            CI Linux packaging jobs never executed — all `fixed-unverified`, P-00
                        will fail because untracked debian/postinst files
- **Status:**           **OPEN** — Found during comprehensive-review architecture review
                        (ultra effort) 2026-08-09.
- **Criticality:**      High. The PARITY-LEDGER.md explicitly states: "Nothing in the Linux
                        packaging section is `verified-by-CI` yet. The four jobs… are
                        committed… and have never executed." P-00 requires untracked
                        `debian/postinst`, `postrm`, and `polkit/app.syxx.policy` files
                        that are not in the repo. The CI job `package-deb` will fail at
                        its first step.
- **Location:**         .github/workflows/ci.yml (jobs: package-deb, install-smoke-deb,
                        package-snap, linux-secret-store)
- **Issue:**            The Linux packaging pipeline (P-00 through P-18) is entirely in
                        `fixed-unverified` state. A clean checkout cannot build a `.deb`
                        because required files are untracked. Do not merge until at least
                        one CI run completes.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] `git add` the untracked debian/postinst, postrm, and polkit
                        files. [Fix 2] Run CI on this branch to convert `fixed-unverified`
                        to `verified-by-CI`. [Fix 3] Verify build_deb.sh and snapcraft.yaml
                        reference correct project paths.
- **Code Sample:**      [

                        # PARITY-LEDGER.md:
                        # "Nothing in the Linux packaging section is
                        #  `verified-by-CI` yet. The four jobs… are committed…
                        #  and have never executed."
                        # P-00: untracked debian/postinst, postrm, polkit files

                        ]

---

- **[D050]**            CageRedundancyCorrelator builds four dictionary + four set copies +
                        four sorts — unnecessary intermediate allocations
- **Status:**           **OPEN** — Found during comprehensive-review performance review
                        (ultra effort) 2026-08-09.
- **Criticality:**      High. For 500 queries per stream, this is ~4,000 dictionary
                        insertions + 4 Set copies (~2,000 elements) + 4 set operations.
                        The four Set copies are unnecessary — set operations already create
                        new Sets internally.
- **Location:**         macOS/Sources/SyxxKit/CageRedundancyCorrelator.swift:130-225
- **Issue:**            The `correlate` method builds eight data structures from two input
                        arrays. The four Set copies (daemonKeys, cageKeys, daemonXids,
                        cageXids) are intermediate variables that can be eliminated by
                        calling `.intersection()` and `.subtracting()` directly on
                        `Set(daemonByKey.keys)`.
- **Cross-ref. Delta:** B018 (zero tests), D047 (no C# twin).
- **Fix(es):**          [Fix 1] Eliminate intermediate Set copies: `let matchedKeys =
                        Set(daemonByKey.keys).intersection(cageByKey.keys)`.
- **Code Sample:**      [

                        // CageRedundancyCorrelator.swift:130-225
                        let daemonKeys = Set(daemonByKey.keys)  // unnecessary copy
                        let cageKeys = Set(cageByKey.keys)      // unnecessary copy
                        let matchedKeys = daemonKeys.intersection(cageKeys)
                        // These copies are redundant — set operations create new sets

                        ]

---

- **[D051]**            WarpEngine.ResolveOverrideForTests is mutable internal static — not
                        thread-safe for parallel tests
- **Status:**           **OPEN** — Found during comprehensive-review code-review
                        (ultra effort) 2026-08-09.
- **Criticality:**      Medium. Two test classes running in parallel will clobber each
                        other's override. This is the same "shared mutable static"
                        anti-pattern that `LinuxSecretStore` explicitly fixed with
                        `_instanceDir`.
- **Location:**         Windows/dotNET/Syxx.Kit/WarpEngine.cs
- **Issue:**            `internal static Func<string, Task<string?>>? ResolveOverrideForTests;`
                        is a single global that two test classes running in parallel will
                        clobber for each other. The `LinuxSecretStore` diff acknowledges
                        this exact problem and fixed it.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Follow the same pattern `LinuxSecretStore` used — add an
                        instance-level override on `WarpEngine`. [Fix 2] Or pass the
                        resolver as a parameter rather than a static hook.
- **Code Sample:**      [

                        // WarpEngine.cs
                        internal static Func<string, Task<string?>>?
                            ResolveOverrideForTests;
                        // Shared mutable static — two parallel test classes clobber

                        ]

---

- **[D052]**            Mirror module is Linux-only — 3 separate implementations across
                        platforms, D286 says changes MUST be manually ported
- **Status:**           **OPEN** — Found during comprehensive-review architecture review
                        (ultra effort) 2026-08-09. Previously noted in D009 but severity
                        escalated after confirming 3 independent implementations exist.
- **Criticality:**      Medium. The D286 architectural note says "macOS and Windows clients
                        carry their own equivalent implementations" and "changes here MUST
                        be manually ported." Three separate implementations of the same
                        diff/content-comparison logic will inevitably diverge.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/Mirror/ (7 files, ~1,050 lines)
- **Issue:**            The Mirror module (DiffEngine, MirrorBundle, DegradationCalculator,
                        StaticContentParser) is purely logical — it should be extractable
                        to a shared library (Syxx.Kit). The D286 note explicitly recommends
                        this but it hasn't been done.
- **Cross-ref. Delta:** D009 (Mirror/ engine duplication), C003 (HasChanges divergence),
                        D010 (SummaryLine drift), D011 (ReDoS on Windows).
- **Fix(es):**          [Fix 1] Extract Mirror into Syxx.Kit so all three platforms consume
                        a single source of truth. [Fix 2] Prioritize before implementations
                        diverge further.
- **Code Sample:**      [

                        // D286 architectural note:
                        // "macOS and Windows clients carry their own equivalent
                        //  implementations. Changes here MUST be manually ported
                        //  to the macOS and Windows mirror implementations."
                        // Three separate implementations of same logic.

                        ]

---

- **[D053]**            No observability additions in macOS SwiftUI witness key changes —
                        violates observability-as-code rule
- **Status:**           **OPEN** — Found during comprehensive-review best practices review
                        (ultra effort) 2026-08-09.
- **Criticality:**      Medium. The `WitnessKeyWriteGate` guards against an irreversible
                        operation (overwriting the signing key). If reached in production,
                        there will be no telemetry to diagnose what happened. The project's
                        observability-as-code rule requires OTel/Sentry instrumentation
                        alongside new features.
- **Location:**         macOS/Sources/SyxxApp/AppModel.swift (WitnessKeyAbsence,
                        WitnessKeyWriteGate, witnessKeyWriteGate(), externalSnapshotJson,
                        noKey paths)
- **Issue:**            The diff adds significant new code paths for witness key management
                        but contains zero Sentry breadcrumbs, OTel spans, or os_log calls
                        alongside these additions. The Avalonia side added Sentry/OTel
                        runtime toggling; the macOS side added no instrumentation.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add Sentry breadcrumbs at each `WitnessKeyWriteGate` return
                        path. [Fix 2] Add Sentry breadcrumb at `saveWitnessKey` call site.
                        [Fix 3] Instrument the `noKey` path with an OTel span.
- **Code Sample:**      [

                        // AppModel.swift — new witness key code paths:
                        // WitnessKeyAbsence enum, WitnessKeyWriteGate enum,
                        // witnessKeyWriteGate(), externalSnapshotJson,
                        // noKey(WitnessKeyAbsence) case
                        // Zero Sentry breadcrumbs, zero OTel spans

                        ]

---

- **[E016]**            Breadcrumb() called from catch blocks can throw, replacing the
                        original exception with a telemetry exception
- **Status:**           **OPEN** — Found during comprehensive-review code-review
                        (ultra effort) 2026-08-09.
- **Criticality:**      High. If `SyxxDiagnostics.Breadcrumb` throws (e.g., telemetry sink
                        unavailable), the original exception is replaced. The
                        `Console.Error.WriteLine` never executes, and the caller sees a
                        telemetry exception instead of the actual failure.
- **Location:**         Windows/dotNET/Syxx.Kit/LinuxSecretStore.cs
- **Issue:**            `StoreWithFile` catches `Exception ex`, calls `Breadcrumb("keystore
                        write failed", ex)`, then writes to `Console.Error`. If `Breadcrumb`
                        throws, the original exception is lost. `Breadcrumb` is also called
                        from non-catch code paths where an unhandled telemetry exception
                        would propagate to the caller.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Wrap the `Breadcrumb` body in its own try/catch:
                        `try { SyxxDiagnostics.Breadcrumb(...); } catch { /* best-effort */ }`
- **Code Sample:**      [

                        // LinuxSecretStore.cs
                        catch (Exception ex)
                        {
                            Breadcrumb("keystore write failed", ex); // can throw
                            Console.Error.WriteLine(...); // never reached if above throws
                        }

                        ]

---

- **[F011]**            CHANGELOG.md has zero substantive additions for 803-file / 55K-line
                        diff — only whitespace changes
- **Status:**           **OPEN** — Found during comprehensive-review best practices review
                        (ultra effort) 2026-08-09.
- **Criticality:**      Critical. This diff spans 803 files, 55K insertions, 16K deletions
                        including a new 7-command delta remediation suite, behavioral vector
                        runner, parity fingerprint system, CLI surface gate, live theme
                        switching, and dozens of cross-platform parity fixes. None appears
                        in the CHANGELOG. A release manager would see only formatting tweaks.
- **Location:**         CHANGELOG.md (174 lines added, all whitespace-only)
- **Issue:**            Every non-blank line in the CHANGELOG diff is a whitespace-only
                        change (blank-line insertions, `+` → `-` list-marker changes).
                        Zero new content was added for a massive feature branch.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add a dated CHANGELOG section covering major workstreams:
                        delta remediation suite, parity fingerprint, behavioral vectors,
                        CLI surface gate, theme switching, witness key gates, build error
                        fixes, and plan-lane docs.
- **Code Sample:**      [

                        # CHANGELOG.md diff — all whitespace changes:
                        # + (blank line additions)
                        # + → - (list marker changes)
                        # No new content for 803-file / 55K-line diff

                        ]

---

- **[F012]**            DELTAS.md text corruption in A031 delta entry — missing spaces:
                        "while_validate_offline" and "However,_validate_offline()"
- **Status:**           **OPEN** — Found during comprehensive-review architecture review
                        (ultra effort) 2026-08-09.
- **Criticality:**      High. These corruptions are in the Criticality and Issue fields of
                        a delta entry. They make the text harder to parse and suggest a
                        tool or manual edit removed spaces during a prior formatting pass.
- **Location:**         DELTAS.md:245,249 (A031 delta entry)
- **Issue:**            `while_validate_offline` should be `while _validate_offline`.
                        `However,_validate_offline()` should be `However, _validate_offline()`.
                        Missing spaces in two locations in the A031 entry.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Restore missing spaces.
                        [Fix 2] Run
                        `python3 scripts/reflow-deltas.py DELTAS.md` to reindent.
                        [Fix 3] Manually verify A031 reads correctly after fix.
- **Code Sample:**      [

                        # DELTAS.md:245,249 — A031 delta entry:
                        # "while_validate_offline" → should be "while _validate_offline"
                        # "However,_validate_offline()" → should be "However, _validate_offline()"

                        ]

---

- **[F013]**            Generated artifacts incomplete — `make generate` likely not run before
                        commit; some harnesses missing delta remediation and security review
- **Status:**           **OPEN** — Found during comprehensive-review best practices review
                        (ultra effort) 2026-08-09.
- **Criticality:**      Medium. The delta remediation and security review plugin additions
                        were hand-committed to select generated directories. Some harnesses
                        (Gemini skills, Copilot security rules) are missing the new plugins.
                        `make garden` drift detection would likely fail.
- **Location:**         .opencode/commands/, .codex/agents/, .cursor/rules/, .copilot/rules/,
                        skills/, agents/, commands/
- **Issue:**            `.opencode/commands/` has delta remediation, `.codex/agents/` has
                        partial coverage, `.cursor/rules/` has partial coverage.
                        `.copilot/rules/` is missing security review. `skills/` and
                        `agents/` (Gemini) are missing delta remediation entirely.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Run `make generate-all` from the project root.
                        [Fix 2]
                        Verify all harness directories receive the new plugins. [Fix 3]
                        Commit the full set.
- **Code Sample:**      [

                        # Generated artifact coverage:
                        # .opencode/commands/ — ✅ complete
                        # .codex/agents/      — ⚠️ partial
                        # .cursor/rules/      — ⚠️ partial
                        # .copilot/rules/     — ❌ missing security review
                        # skills/ (Gemini)    — ❌ missing delta remediation
                        # agents/ (Gemini)    — ❌ missing delta remediation

                        ]

---

- **[C006]**            C# Assemble drops hard-excluded diagnoses from the clean-blocking
                        overhang — false `.clean` verdict on Windows (B163 parity gap)
- **Status:**           **OPEN** — Found during delta analysis (-deep) of feat/1 branch
                        2026-08-09. macOS preserves B163; the C# port's Assemble still strips
                        HardExcluded from the overhang used to block a clean verdict.
- **Criticality:**      Critical. A hard-excluded diagnosis that carried real raw support
                        licenses `.clean` on Windows where macOS returns `.inconclusive` —
                        a false "clean/secure" verdict on the Windows build.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:1193-1201,
                        1207; macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift:982-999
- **Issue:**            The C# `Assemble` overhang computation filters out
                        `SuppressionReason.HardExcluded` (:1194) and gates clean only on
                        `overhang.Count == 0` (:1207). The macOS twin keeps hardExcluded
                        entries in the overhang (:984) so a degraded exclusion forces
                        `.inconclusive`. Same input therefore yields a false `.clean` on
                        Windows. The `surfaceSuppressed` filter at :1199 also hides
                        hard-excluded entries from the "Unverified Signals" overhang list.
- **Cross-ref. Delta:** None in live ledger. macOS-side fix tracked as B163 in
                        docs/DELTAS-fixed.md; C# twin fix never ported.
- **Fix(es):**          [Fix 1] Remove the `s.Reason != HardExcluded` predicate from the
                        overhang computation in Assemble (:1194) so hard-excluded diagnoses
                        block clean exactly as on macOS. [Fix 2] Keep hardExcluded out of
                        `surfaceSuppressed` (:1199) for display, but never out of the
                        clean-blocking overhang. [Fix 3] Add a Windows regression test where
                        a hard-excluded diagnosis with raw support forces Inconclusive.
- **Code Sample:**      [

                        // MatrixDeterminativeEngine.cs:1193-1207 (C# — defective)
                        var overhang = suppressed
                            .Where(s => s.Reason != SuppressedDiagnosis.SuppressionReason.HardExcluded
                                        && s.RawSupport >= OverhangFloor)
                            .ToList();
                        // ...
                        if (emitted.TryGetValue(CleanId, out var clean) && overhang.Count == 0)
                            return DeterminativeResult.Clean;

                        // MatrixDeterminativeEngine.swift:984 (macOS — correct, B163)
                        let overhang = suppressed.filter { $0.rawSupport >= Self.overhangFloor }

                        ]

---

- **[C007]**            C# DNS spoofed-then-timeout collapses to `.timeout` where macOS
                        records `.responseIdMismatch` — recorded evidence diverges for the
                        same attack
- **Status:**           **OPEN** — Found during delta analysis (-deep) of feat/1 branch
                        2026-08-09. Parity agent P2.
- **Criticality:**      Medium. Both platforms end in a didFail verdict, so no false
                        verdict results; but the Windows evidence bundle records `.timeout`
                        where macOS records `.responseIdMismatch` (injection observed) for
                        the identical attack, under-reporting the spoofing signal.
- **Location:**         Windows/dotNET/Syxx.Kit/DNSResolver.cs:244-257, 284-313;
                        macOS/Sources/SyxxKit/DNSResolver.swift:423-425, 452-462, 497-535
- **Issue:**            When a response matching a spoofed transaction ID arrives and the
                        query then times out, the C# path collapses to `.timeout`; macOS
                        prefers the earlier `.responseIdMismatch` observation. The reason
                        code recorded per-platform diverges, so Windows evidence hides that
                        an ID-mismatched (injected) answer was observed.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Match macOS ordering — surface `.responseIdMismatch` when an
                        ID mismatch is observed before the deadline instead of overwriting
                        with `.timeout`. [Fix 2] Add a parity replay vector covering the
                        spoofed-then-timeout case in corpus/probe-parity.
- **Code Sample:**      [

                        // DNSResolver.cs — spoofed ID observed, then deadline fires:
                        //   C#   → DNSResponseStatus.Timeout
                        //   Swift→ DNSResponseStatus.responseIdMismatch

                        ]

---

- **[C008]**            Collector-less Glutton runs promote a never-executed probe to
                        `Invalidated` instead of `Untested`
- **Status:**           **OPEN** — Found during the completed five-agent audit
                        2026-08-09.
- **Criticality:**      Non-critical. The run still reports absence of collector data, but
                        the status code overstates the failure mode and makes a never-run
                        probe look like a corrupt one.
- **Location:**         Windows/dotNET/Syxx.Kit/ProbePlan.cs:224-226;
                        Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:761-763;
                        Windows/dotNET/Syxx.Kit/Synthesis/SyxxMatrixIntegration.cs:370-371;
                        macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:576-579
- **Issue:**            The collector-less path always manufactures an inconclusive leak
                        result, but the synthesis layer maps `LeakResult.Inconclusive` with
                        `Signature == null` to `Invalidated` on Windows while the macOS twin
                        marks the same situation as `Untested`. A missing collector is a
                        probe-not-run condition, not a corrupted probe.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Map `Signature == null` to `Untested` on both platforms.
                        [Fix 2] Reserve `Invalidated` for probes that actually ran and were
                        structurally unusable.
- **Code Sample:**      [

                        // Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:761-763
                        case LeakResult.Inconclusive:
                            subs.Add(leak.Signature is null ? SectionStatus.Invalidated
                                : SectionStatus.Inconclusive);

                        // macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:576-579
                        case .inconclusive:  subs.append(l.signature == nil ? .untested
                            : .inconclusive)

                        ]

---

- **[C009]**            Conclusions scan drops accumulated session evidence and falls back to
                        the hard-coded `github.com` first-run target
- **Status:**           **OPEN** — Found during the completed five-agent audit
                        2026-08-09.
- **Criticality:**      Critical. The conclusions surface is supposed to summarize the
                        current run, but this path clears its collections and launches a
                        fresh first-run scan against a default target instead.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/ConclusionsViewModel.cs:62-66;
                        Windows/dotNET/Syxx.Kit/Synthesis/FirstRunScan.cs:41-47
- **Issue:**            `ScanAsync()` clears `TopReasons`, `HonestyLines`, `Ran`, and
                        `Skipped`, then calls `FirstRunScan.RunAsync(progress: progress)`
                        with no session input. `FirstRunScan` defaults `domain` to
                        `github.com`, so the conclusions page can end up summarizing a new
                        first-run scan instead of the evidence the user already collected.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Pass the current session/domain into the conclusions scan
                        path. [Fix 2] Do not default to `github.com` when the user is on the
                        conclusions surface. [Fix 3] Preserve prior session evidence unless
                        the user explicitly requests a fresh scan.
- **Code Sample:**      [

                        // Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/ConclusionsViewModel.cs:62-66
                        TopReasons.Clear(); HonestyLines.Clear(); Ran.Clear(); Skipped.Clear();
                        result = await FirstRunScan.RunAsync(progress: progress);

                        // Windows/dotNET/Syxx.Kit/Synthesis/FirstRunScan.cs:41-43
                        public const string DefaultDomain = "github.com";

                        ]

---

- **[A040]**            Trial-anchor per-IP limiter `_TRIAL_ANCHOR_IP_COUNTS` grows unbounded
                        (no eviction within a day)
- **Status:**           **OPEN** — Found during delta analysis (-deep) of feat/1 branch
                        2026-08-09. Security agent finding #5.
- **Criticality:**      Non-critical. A spoofed-source flood can grow the dict with an
                        unbounded number of IP keys within a single day, a slow memory leak;
                        rollover only rewrites the map at day boundaries.
- **Location:**         Server/syxx_server/app.py:1667-1702 (_TRIAL_ANCHOR_IP_COUNTS)
- **Issue:**            The per-IP trial-anchor counter dict adds a key for every distinct
                        source IP and never deletes them; the day rollover re-creates the
                        dict but cannot bound growth inside a day. Under attacker-spoofed
                        source IPs the map grows without limit.
- **Cross-ref. Delta:** A012 (restart-reset of the same dict). A012 covers persistence;
                        A040 covers unbounded in-process growth.
- **Fix(es):**          [Fix 1] Cap the dict with an LRU or expiry sweep (e.g., evict keys
                        idle beyond the trial window). [Fix 2] Move per-IP accounting into
                        the existing ratelimit module, which already evicts expired entries.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:1667
                        _TRIAL_ANCHOR_IP_COUNTS = {}  # ip -> (count, day_start_time)
                        # Keys are only ever added or rewritten at day rollover — never
                        # evicted mid-day, so a spoofed-source flood grows this unbounded.

                        ]

---

- **[A041]**            `/api/diff` content half runs `warp.build_baseline` with no aggregate
                        deadline — web-tier worker pinned 150–750 s per request
- **Status:**           **OPEN** — Found during delta analysis (-deep) of feat/1 branch
                        2026-08-09. Performance agent finding A.
- **Criticality:**      High. The URL is attacker-supplied (app.py:2119) and the web tier
                        can be driven to pin workers for ~12 minutes per request, an
                        amplification-style DoS on server compute.
- **Location:**         Server/syxx_server/app.py:265-273; Server/syxx_server/warp.py:123-193,
                        166-170; Server/syxx_server/scheduler.py:78-79
- **Issue:**            The content half of `/api/diff` calls `warp.build_baseline` without
                        an aggregate deadline. `build_baseline`'s per-hop deadline checks are
                        gated on `deadline is not None` (:166-170), so the web path gets none.
                        Worst case ≈ 30 s initial fetch + 40 × 18 s hops ≈ 750 s of pinned
                        worker time per request. The deprecated `build_baseline` still runs
                        on the SM-016-scheduler path with a deadline; the web route does not.
- **Cross-ref. Delta:** None. Distinct from A028 (which tracks the app.py→warp dependency
                        location, not the missing deadline).
- **Fix(es):**          [Fix 1] Derive and pass the SM-016 aggregate deadline into
                        `build_baseline` from the `/api/diff` route. [Fix 2] Add a wall-clock
                        cap on the web content path that bounds total elapsed time regardless
                        of hop count.
- **Code Sample:**      [

                        # Server/syxx_server/warp.py:166-170
                        if deadline is not None:
                            if time.monotonic() > deadline:
                                break   # deadline only enforced when a caller passes one
                        # /api/diff (app.py:265-273) calls build_baseline with deadline=None

                        ]

---

- **[D054]**            C# DNSResponse drops the spoof/attempt telemetry fields present on
                        macOS
- **Status:**           **OPEN** — Found during delta analysis (-deep) of feat/1 branch
                        2026-08-09. Parity agent P1.
- **Criticality:**      Medium. Windows telemetry and diagnostics that rely on attempt and
                        spoofed-packet counts lose the data macOS records for the same
                        transport — cross-platform evidence divergence.
- **Location:**         Windows/dotNET/Syxx.Kit/DNSResolver.cs:130-176;
                        macOS/Sources/SyxxKit/DNSResolver.swift:186-194, 259-260
- **Issue:**            The C# `DNSResponse` does not expose the attempt/spoofed-packet
                        telemetry macOS carries. Metrics, span tags, and diagnostic reports
                        keyed on those fields silently degrade on Windows.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Port the macOS `AttemptCount`/`SpoofedPacketCount` fields
                        onto the C# `DNSResponse` and populate them in the UDP/DoH paths.
                        [Fix 2] Emit them as OTel span tags on both platforms.
- **Code Sample:**      [

                        // DNSResolver.cs:130-176 (C#) — DNSResponse lacks
                        // AttemptCount / SpoofedPacketCount present in the Swift twin
                        // (DNSResolver.swift:186-194, 259-260)

                        ]

---

- **[D055]**            SyxxDiagnostics run-attribution ledger is never populated in
                        production on either platform — every run reports
                        `unattributed_degradation`
- **Status:**           **OPEN** — Found during delta analysis (-deep) of feat/1 branch
                        2026-08-09. Observability agent finding.
- **Criticality:**      High. Run-context attribution (run_id, attribution_complete) is only
                        exercised by tests; production runs always emit
                        `result.unattributed_degradation` with `syxx.run_id` = "none",
                        so run-level telemetry attribution is dead on delivery.
- **Location:**         macOS/Sources/SyxxKit/SyxxDiagnostics.swift:111, 148;
                        macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:827, 844;
                        macOS/Sources/SyxxTelemetry/SentryDiagnosticSink.swift:65;
                        Windows/dotNET/Syxx.Kit/SyxxDiagnostics.cs:111-112, 134
- **Issue:**            `SyxxDiagnostics.run` (Swift TaskLocal :111) and `RunContext.Run`
                        (C# AsyncLocal :111-112) are only ever set by test helpers
                        (ResultProvenanceTests.swift:74, 91). No production entry point
                        (AppModel runAllWardens / CLI run) establishes a RunContext, so
                        Synthesizer always sees an unattributed run: it emits
                        `unattributed_degradation` (Synthesizer.swift:844) and records
                        `syxx.attribution_complete = false`, and the Sentry sink always
                        tags `syxx.run_id = "none"`.
- **Cross-ref. Delta:** D019 (dual telemetry architecture) — distinct; D055 is the
                        missing run attribution wiring, not the sink count.
- **Fix(es):**          [Fix 1] Establish a RunContext at the real run entry points
                        (AppModel runAllWardens, CLI run) on both platforms. [Fix 2] Assert
                        in a production-path test that run_id is non-"none" after a real run.
- **Code Sample:**      [

                        // SyxxDiagnostics.swift:111 / SyxxDiagnostics.cs:111-112
                        //   Swift:  @TaskLocal public static var run: RunContext?
                        //   C#:     public static RunContext? Run { get => _run.Value; ... }
                        // Set only by tests — no production entry point assigns it.

                        ]

---

- **[A042]**            Server Sentry SDK initialized but never used — zero capture_exception
                        calls in the entire server codebase
- **Status:**           **OPEN** — Found during delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Critical. The SDK is a dead dependency shipping an initialized-but-
                        idle SDK. Production exceptions are invisible to Sentry.
- **Location:**         Server/syxx_server/app.py:74 (sentry_sdk.init)
- **Issue:**            `sentry_sdk.init()` configures the SDK at startup, but there are
                        zero `sentry_sdk.capture_exception()` or `sentry_sdk.capture_message()`
                        calls anywhere in the server codebase. All errors route through
                        `logging_util.error()` → stdout + OTLP only. Sentry is a dead
                        dependency.
- **Cross-ref. Delta:** A043 (handler_exception no Sentry event).
- **Fix(es):**          [Fix 1] Add `sentry_sdk.capture_exception(e)` to the
                        `handler_exception` catch-all in app.py. [Fix 2] Add
                        `sentry_sdk.capture_exception()` to all `logging_util.error()` call
                        sites that represent operational failures. [Fix 3] Or remove the
                        `sentry_sdk.init()` call and the dependency if Sentry is not needed
                        on the server.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:74
                        sentry_sdk.init(
                            dsn=config._s("SENTRY_DSN"),
                            environment=config._s("SYXX_ENV", "production"),
                            traces_sample_rate=0.1,
                        )
                        # Zero sentry_sdk.capture_exception() calls in entire codebase

                        ]

---

- **[A043]**            handler_exception catch-all creates no Sentry event — production
                        exceptions invisible to error tracking
- **Status:**           **OPEN** — Found during delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Critical. The Sentry SDK is initialized 50 lines above this catch
                        block but never invoked. Un-spanned endpoint exceptions are invisible.
- **Location:**         Server/syxx_server/app.py:919
- **Issue:**            `handler_exception` catches all unhandled GET exceptions and calls
                        `logging_util.error("handler_exception", route=path, err=str(e))`,
                        but never `sentry_sdk.capture_exception(e)`. The Sentry SDK is
                        initialized at line 74 but never invoked. Production exceptions in
                        un-spanned endpoints (lookup, whois, propagation, etc.) are invisible
                        to Sentry.
- **Cross-ref. Delta:** A042 (Sentry never used).
- **Fix(es):**          [Fix 1] Add `sentry_sdk.capture_exception(e)` to the
                        `handler_exception` block. [Fix 2] Include route path and request
                        context as Sentry tags.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:919-925
                        async def handler_exception(self, request, e):
                            path = request.path
                            logging_util.error(
                                "handler_exception",
                                route=path,
                                err=str(e),
                            )
                            # No sentry_sdk.capture_exception(e) here

                        ]

---

- **[A044]**            Server OTel spans cover only 16 of ~30+ endpoint paths — most-called
                        product endpoints produce no distributed traces
- **Status:**           **OPEN** — Found during delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      High. The most-called product endpoints (lookup, whois, propagation,
                        subdomains, firewall, dnssec, reverse DNS) produce no distributed
                        traces, making latency debugging and error correlation impossible.
- **Location:**         Server/syxx_server/app.py (span creation sites)
- **Issue:**            `app.py` creates OTel spans for warp, investigate, authoritative,
                        diff, trace, baseline, exposure, and leak endpoints. But no spans
                        exist for `_route_lookup`, `_route_whois`, `_route_propagation`,
                        `_route_subdomains`, `_route_firewall_test`, `_route_dnssec`,
                        `_route_reverse_dns`, `_route_broadcast`, `_route_checkout`,
                        `_route_appcast`, `_route_activate`, `_route_passive`,
                        `_route_metrics`, `_route_readyz`, or `_route_beta_reserve`.
- **Cross-ref. Delta:** A045 (healthz no spans).
- **Fix(es):**          [Fix 1] Add OTel spans to all un-instrumented endpoint handlers.
                        [Fix 2] Use a decorator or middleware pattern to auto-instrument
                        all routes rather than adding spans per-route.
- **Code Sample:**      [

                        # app.py — spans exist for these 16 endpoints:
                        # warp, investigate, authoritative, diff, trace, baseline,
                        # exposure, leak_*, fetch_script, leak_legacy
                        # No spans for these ~14 endpoints:
                        # lookup, whois, propagation, subdomains, firewall_test,
                        # dnssec, reverse_dns, broadcast, checkout, appcast,
                        # activate, passive, metrics, readyz, beta_reserve

                        ]

---

- **[A045]**            Server /readyz and /healthz endpoints do not create OTel spans
- **Status:**           **OPEN** — Found during delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Medium. Load balancers and monitors hit these constantly. Without
                        spans, there is no way to trace whether a readiness probe failure
                        cascaded from a DB timeout or a listener down event.
- **Location:**         Server/syxx_server/app.py:1044 (/healthz), 1551 (/readyz)
- **Issue:**            The 16 endpoints that DO have spans prove the pattern exists; the
                        health endpoints were simply never instrumented. These are the
                        endpoints that orchestration systems depend on for availability
                        decisions.
- **Cross-ref. Delta:** A044 (missing spans on product endpoints).
- **Fix(es):**          [Fix 1] Add OTel spans to /healthz and /readyz handlers.
                        [Fix 2]
                        Include dependency health (DB, DNS listener) as span attributes.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:1044
                        async def _route_healthz(self, request):
                            # No OTel span
                            return web.Response(text="OK")
                        # Server/syxx_server/app.py:1551
                        async def _route_readyz(self, request):
                            # No OTel span
                            ...

                        ]

---

- **[A046]**            _route_diff performs two sequential DNS resolutions that could run
                        concurrently — doubles handler thread occupancy
- **Status:**           **OPEN** — Found during delta-analysis performance audit 2026-08-09.
- **Criticality:**      Medium. With a 5s authoritative deadline, worst-case thread
                        occupancy is ~10s per diff request instead of ~5s.
- **Location:**         Server/syxx_server/app.py:2097-2108
- **Issue:**            `_route_diff` calls `resolver.resolve_authoritative()` then
                        `_clean_vantage_answers()` sequentially on the same handler thread.
                        The authoritative walk and clean-vantage queries are independent —
                        they could run concurrently via `asyncio.gather()`. Currently
                        serialized, doubling the wall-clock time the handler thread is pinned.
- **Cross-ref. Delta:** A033 (SD-44 — sync DNS walk blocks handler).
- **Fix(es):**          [Fix 1] Use `asyncio.gather()` to run the authoritative walk and
                        clean-vantage query concurrently. [Fix 2] Ensure both tasks respect
                        the same deadline.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:2097-2108
                        auth = resolver.resolve_authoritative(domain, qtypes[...], ...)
                        # ... then sequentially ...
                        clean_answers = _clean_vantage_answers(domain, qtypes[...], ...)
                        # These are independent — could be:
                        # auth, clean = await asyncio.gather(
                        #     resolver.resolve_authoritative(...),
                        #     _clean_vantage_answers(...),
                        # )

                        ]

---

- **[B024]**            ExfilSuspicious EvidenceId unmapped in C# SynthesisEngine — macOS
                        maps it to LocalEnvironmentCompromised; C# silently drops it
- **Status:**           **OPEN** — Found during delta-analysis parity audit 2026-08-09.
- **Criticality:**      Critical. A detection gap: C# silently drops exfiltration evidence
                        that macOS feeds into the matrix engine, potentially missing
                        host-compromise diagnoses on Windows.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs:283-310;
                        macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift:290-292
- **Issue:**            macOS maps `EvidenceId.ExfilSuspicious` to
                        `SyxxIndicator.LocalEnvironmentCompromised` at strength 0.35 (C201).
                        The C# `IndicatorMapping` dictionary omits `ExfilSuspicious` entirely.
                        The C# `EvidenceId` enum defines it, and the wire-name tables include
                        it, but the mapping never references it. A run where Witness detects
                        suspicious exfiltration patterns produces `EvidenceId.ExfilSuspicious`
                        on both platforms — macOS feeds it into the matrix; C# silently drops
                        it.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add `[EvidenceId.ExfilSuspicious] = (SyxxIndicator.
                        LocalEnvironmentCompromised, 0.35)` to the C# `IndicatorMapping`
                        dictionary at line 310. [Fix 2] Add a parity test that verifies
                        both platforms handle ExfilSuspicious identically.
- **Code Sample:**      [

                        // macOS SynthesisEngine.swift:290-292
                        EvidenceId.ExfilSuspicious:
                            (SyxxIndicator.LocalEnvironmentCompromised, 0.35),
                        // C# SynthesisEngine.cs:283-310 — ExfilSuspicious is absent
                        // from the IndicatorMapping dictionary entirely

                        ]

---

- **[B025]**            MatrixDeterminativeEngine.evaluate() O(n²) competitor strength
                        computation — recomputed per diagnosis instead of pre-computed once
- **Status:**           **OPEN** — Found during delta-analysis performance audit 2026-08-09.
- **Criticality:**      Medium. Negligible at current scale (6 diagnoses = 36 iterations)
                        but grows quadratically if the diagnosis catalog expands beyond ~20.
- **Location:**         macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift:640-646
- **Issue:**            `competitorBestStrength(for:)` is called once per diagnosis in Pass 2,
                        and each invocation iterates all diagnoses to find the max competitor
                        strength. The results could be pre-computed in a single O(d²) pass
                        rather than recomputed per diagnosis. Currently 6 diagnoses, but the
                        architecture is O(d²) and will grow if the catalog expands.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Pre-compute `competitorBestStrength` for all diagnoses in a
                        single O(d²) pass before Pass 2. [Fix 2] Cache results in a
                        dictionary keyed by diagnosis ID.
- **Code Sample:**      [

                        // MatrixDeterminativeEngine.swift:640-646
                        func competitorBestStrength(for d: DiagnosisSpec) -> Double {
                            var best = 0.0
                            for other in spec.diagnoses where other.id != d.id {
                                best = max(best, raws[other.id]?.strength ?? 0)
                            }
                            return best
                        }
                        // Called once per diagnosis — O(d²) total

                        ]

---

- **[D056]**            runAllWardens() timeout path produces no error capture — operational
                        failure invisible to Sentry
- **Status:**           **OPEN** — Found during delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Critical. When a scan times out after 60-120s, the user sees a
                        message but the failure is invisible to Sentry as an event. This is
                        a genuine operational failure that should be captured for trend
                        analysis.
- **Location:**         macOS/Sources/SyxxApp/AppModel.swift:789
- **Issue:**            `runAllWardens()` adds only a breadcrumb on timeout at line 789.
                        When a scan times out, no `SyxxDiagnostics.capture()` or
                        `SentrySDK.capture()` is called. The same pattern at lines 660-668
                        where individual warden timeouts set only `self.output` with zero
                        telemetry.
- **Cross-ref. Delta:** D015 (runAllWardens duplicates run), D058 (no error capture in UI).
- **Fix(es):**          [Fix 1] Add `SyxxDiagnostics.capture()` on scan timeout with the
                        scanCorrelationId, elapsed time, and timed-out warden names.
                        [Fix 2] Add individual warden timeout captures in the runAllWardens
                        loop.
- **Code Sample:**      [

                        // AppModel.swift:789 — timeout breadcrumb only
                        SentrySDK.addBreadcrumb(Breadcrumb(
                            level: .info, category: "gui.state",
                            message: "Wardens scan timed out"
                        ))
                        // No SyxxDiagnostics.capture() or SentrySDK.capture()

                        ]

---

- **[D057]**            flushDNSCache() has zero telemetry — no breadcrumb, span, or error
                        capture
- **Status:**           **OPEN** — Found during delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      High. A DNS cache flush failure is a meaningful diagnostic signal
                        but produces no telemetry. If the command fails (exit code ≠ 0 or
                        throws), the error is only shown in the UI.
- **Location:**         macOS/Sources/SyxxApp/AppModel.swift:718-743
- **Issue:**            `flushDNSCache()` spawns a `Task.detached` that runs `dscacheutil
                        -flushcache`. No breadcrumb, no span, no error capture. If the
                        command fails, the error is only shown in the UI — no Sentry event,
                        no OTel span, no diagnostic record.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add a breadcrumb before and after the flush.
                        [Fix 2] Add
                        `SyxxDiagnostics.capture()` on non-zero exit code. [Fix 3] Add an
                        OTel span for the flush operation.
- **Code Sample:**      [

                        // AppModel.swift:718-743
                        Task.detached {
                            let task = Process()
                            task.launchPath = "/usr/sbin/dscacheutil"
                            task.arguments = ["-flushcache"]
                            task.launch()
                            task.waitUntilExit()
                            // No telemetry on success or failure
                        }

                        ]

---

- **[D058]**            Neither macOS App nor Windows UI capture errors to Sentry as events
                        — breadcrumbs only, no issue creation
- **Status:**           **OPEN** — Found during delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      High. The `SentryDiagnosticSink` bridge is installed and working,
                        but only `SyxxKit` (the core) calls `SyxxDiagnostics.capture()`. The
                        UI layer produces breadcrumbs only, meaning UI-level errors never
                        create Sentry issues.
- **Location:**         macOS/Sources/SyxxApp/ (0 SentrySDK.capture calls);
                        Windows/dotNET/SyxxUI/ (0 SentrySdk.CaptureException calls)
- **Issue:**            `SentrySDK.capture()` returns 0 results in `SyxxApp/`;
                        `SentrySdk.CaptureException()` returns 0 results in `SyxxUI/`. The
                        `AppModel` scan methods add breadcrumbs on success/failure but never
                        capture errors as Sentry events. The `SentryDiagnosticSink` bridge
                        is installed and working, but only `SyxxKit` calls `capture()`.
- **Cross-ref. Delta:** D059 (macOS ErrorPresenter), D060 (Windows ErrorPresenter), D019
                        (dual telemetry).
- **Fix(es):**          [Fix 1] Add `SyxxDiagnostics.capture()` calls at UI error sites.
                        [Fix 2] Ensure the SentryDiagnosticSink bridge is called from UI
                        error paths, not just from SyxxKit.
- **Code Sample:**      [

                        // macOS/Sources/SyxxApp/ — 0 SentrySDK.capture() calls
                        // Windows/dotNET/SyxxUI/ — 0 SentrySdk.CaptureException() calls
                        // All error reporting is breadcrumb-only at the UI layer

                        ]

---

- **[D059]**            macOS ErrorPresenter only uses breadcrumb, never captures errors as
                        Sentry events
- **Status:**           **OPEN** — Found during delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      High. When a user-facing error is presented, the full diagnostic
                        event (with fingerprint, impact, error class) is not captured. The
                        breadcrumb is ephemeral; the event is what creates a Sentry issue.
- **Location:**         macOS/Sources/SyxxApp/ErrorPresenter.swift:23
- **Issue:**            `ErrorPresenter` calls `SyxxDiagnostics.breadcrumb(category:
                        "gui.error", ...)` but never `SyxxDiagnostics.capture()`. The
                        breadcrumb is ephemeral (scrolls off in 100 events); the capture
                        is what creates a durable Sentry issue with fingerprint and impact.
- **Cross-ref. Delta:** D058 (no error capture in UI), D060 (Windows counterpart).
- **Fix(es):**          [Fix 1] Add `SyxxDiagnostics.capture()` alongside the existing
                        breadcrumb call. [Fix 2] Include error class, impact, and user-
                        visible message in the capture.
- **Code Sample:**      [

                        // ErrorPresenter.swift:23
                        SyxxDiagnostics.breadcrumb(
                            category: "gui.error",
                            message: message
                        )
                        // No SyxxDiagnostics.capture() call

                        ]

---

- **[D060]**            Windows ErrorPresenter only uses breadcrumb, never captures errors
                        as Sentry events
- **Status:**           **OPEN** — Found during delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      High. Mirrors the macOS gap exactly. Compounded by the fact that
                        ErrorPresenter.cs is linked into both WinUI and Avalonia (Linux)
                        builds.
- **Location:**         Windows/dotNET/SyxxUI/ErrorPresenter.cs:47
- **Issue:**            `ErrorPresenter` calls `SyxxDiagnostics.Breadcrumb()` but never
                        `SyxxDiagnostics.Capture()`. Same pattern as macOS ErrorPresenter
                        (D059). This file is linked into both WinUI and Avalonia builds,
                        so the gap affects both Windows and Linux.
- **Cross-ref. Delta:** D058 (no error capture in UI), D059 (macOS counterpart).
- **Fix(es):**          [Fix 1] Add `SyxxDiagnostics.Capture()` alongside the existing
                        breadcrumb call. [Fix 2] Ensure it works in both WinUI and Avalonia
                        build configurations.
- **Code Sample:**      [

                        // ErrorPresenter.cs:47
                        SyxxDiagnostics.Breadcrumb(
                            category: "gui.error",
                            message: message
                        );
                        // No SyxxDiagnostics.Capture() call

                        ]

---

- **[D061]**            ConclusionsView.runSynthesis() uses inline SentrySDK.addBreadcrumb
                        bypassing SyxxDiagnostics bridge
- **Status:**           **OPEN** — Found during delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Medium. Inconsistent with SynthesisEngine.swift:62 which uses
                        SyxxDiagnostics.breadcrumb() for the same conceptual event. The
                        inline path lacks redaction and surface tagging.
- **Location:**         macOS/Sources/SyxxApp/ConclusionsView.swift:492
- **Issue:**            `runSynthesis()` adds a synthesis breadcrumb directly via
                        `SentrySDK.addBreadcrumb()` rather than through
                        `SyxxDiagnostics.breadcrumb()`. Inconsistent with
                        `SynthesisEngine.swift:62` which uses the structured bridge for the
                        same event. The inline path lacks redaction, surface tagging, and
                        run_id correlation.
- **Cross-ref. Delta:** D019 (dual telemetry), D056 (missing captures).
- **Fix(es):**          [Fix 1] Replace `SentrySDK.addBreadcrumb()` with
                        `SyxxDiagnostics.breadcrumb()` for consistency.
- **Code Sample:**      [

                        // ConclusionsView.swift:492
                        SentrySDK.addBreadcrumb(Breadcrumb(
                            level: .info, category: "synthesis",
                            message: "Synthesis complete"
                        ))
                        // Should use SyxxDiagnostics.breadcrumb() instead

                        ]

---

- **[D062]**            Cross-platform SentryDiagnosticSink platform tag inconsistency —
                        macOS hardcodes "macos", Windows receives no tag, Linux passes "linux"
- **Status:**           **OPEN** — Found during delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Medium. Sentry queries filtering by platform are unreliable because
                        the tag is set differently (or not at all) on each platform.
- **Location:**         macOS/Sources/SyxxKit/SentryDiagnosticSink.swift:65;
                        Windows/dotNET/Syxx.Kit/SentryDiagnosticSink.cs;
                        Linux/Ubuntu/SyxxUI.Avalonia/LinuxReadyServices.cs:44
- **Issue:**            macOS hardcodes `tags["syxx.platform"] = "macos"`. Linux passes
                        `platform: "linux"` as a parameter. The Windows
                        `SentryDiagnosticSink` API accepts a `platform` parameter but the
                        call sites (Program.cs:39, App.xaml.cs:65) do not pass it — Windows
                        events are tagged with the default. Sentry queries filtering by
                        `syxx.platform` are unreliable.
- **Cross-ref. Delta:** D019 (dual telemetry).
- **Fix(es):**          [Fix 1] Ensure all three platforms consistently set
                        `syxx.platform` at Sentry init time. [Fix 2] Use a build-time
                        constant rather than a runtime parameter.
- **Code Sample:**      [

                        // macOS SentryDiagnosticSink.swift:65
                        tags["syxx.platform"] = "macos"  // hardcoded
                        // Linux LinuxReadyServices.cs:44
                        platform: "linux"  // passed as parameter
                        // Windows Program.cs:39 — platform parameter not passed
                        // => default tag value

                        ]

---

- **[D063]**            DiffEngine helper methods accept narrower types on Windows than Linux
                        — List<DiffLine> vs IReadOnlyList<DiffLine>
- **Status:**           **OPEN** — Found during delta-analysis parity audit 2026-08-09.
- **Criticality:**      Low. Callers on Windows that receive IReadOnlyList from DiffResult
                        need to cast or convert to call the helper methods.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/Mirror/DiffEngine.cs:68-92
                        (IReadOnlyList); Windows/dotNET/SyxxUI/Mirror/DiffEngine.cs:78-96
                        (List)
- **Issue:**            Linux `ChangeCount`/`AdditionCount`/`RemovalCount` accept
                        `IReadOnlyList<DiffLine>`. Windows accepts only `List<DiffLine>`.
                        Callers receiving `IReadOnlyList<DiffLine>` from `DiffResult` (D013
                        fix) need to cast or convert.
- **Cross-ref. Delta:** D013 (DiffResult IReadOnlyList vs List).
- **Fix(es):**          [Fix 1] Change Windows helper methods to accept
                        `IReadOnlyList<DiffLine>`. [Fix 2] Extract the Mirror/ engine to
                        Syxx.Kit (D009).
- **Code Sample:**      [

                        // Linux (correct — accepts broader interface)
                        public static int ChangeCount(IReadOnlyList<DiffLine> diffs)
                        // Windows (narrower — accepts only List)
                        public static int ChangeCount(List<DiffLine> diffs)

                        ]

---

- **[D064]**            StaticContentParser.ParsedContent — CssUrls/JsUrls type divergence
                        between Linux and Windows
- **Status:**           **OPEN** — Found during delta-analysis parity audit 2026-08-09.
- **Criticality:**      Low. Same pattern as D013 and D063 — mutable List on Windows vs
                        immutable IReadOnlyList on Linux.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/Mirror/StaticContentParser.cs:38-39
                        (IReadOnlyList); Windows/dotNET/SyxxUI/Mirror/StaticContentParser.cs:
                        24-25 (List)
- **Issue:**            Linux returns immutable `IReadOnlyList<string>` for `CssUrls` and
                        `JsUrls`. Windows returns mutable `List<string>`. Consumers can
                        mutate the returned lists on Windows.
- **Cross-ref. Delta:** D013 (DiffResult type divergence), D009 (Mirror engine duplication).
- **Fix(es):**          [Fix 1] Change Windows `ParsedContent.CssUrls` and `JsUrls` to
                        `IReadOnlyList<string>`. [Fix 2] Extract the Mirror/ engine to
                        Syxx.Kit (D009).
- **Code Sample:**      [

                        // Linux (immutable)
                        public IReadOnlyList<string> CssUrls { get; init; }
                        // Windows (mutable)
                        public List<string> CssUrls { get; set; }

                        ]

---

- **[D065]**            macOS SynthesisEngine uses preconditionFailure for unmapped sections;
                        C# uses graceful fallback — crash vs silent degradation asymmetry
- **Status:**           **OPEN** — Found during delta-analysis parity audit 2026-08-09.
- **Criticality:**      Medium. A new section added on one platform behaves differently —
                        macOS crashes (preconditionFailure), C# silently maps to Prophet.
                        The worst of both worlds: neither crashes consistently nor degrades
                        consistently.
- **Location:**         macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift:88-91;
                        Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs:330-335
- **Issue:**            When a `Section` value cannot be mapped to `SyxxSection`, macOS calls
                        `preconditionFailure` (crashes in debug, undefined in release). C#
                        maps it to `SyxxSection.Prophet` as a sentinel and logs a warning.
                        A future section added to the shared model would crash macOS but
                        silently degrade on C# — the section is effectively invisible on C#
                        without any indication of the defect.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Align both platforms — either both crash (preferable for
                        invariants) or both log-and-sentinel (preferable for production
                        resilience). [Fix 2] Add a parity test that new sections behave
                        identically.
- **Code Sample:**      [

                        // macOS (crashes)
                        guard let section = SyxxSection(rawValue: section.rawValue) else {
                            preconditionFailure("unmapped section")
                        }
                        // C# (silently degrades)
                        var section = SynthesisSectionExtensions.FromSection(s);
                        // Falls back to Prophet sentinel

                        ]

---

- **[D066]**            LeakDetector subdomain format divergence — macOS prepends "id-";
                        C# does not
- **Status:**           **OPEN** — Found during delta-analysis parity audit 2026-08-09.
- **Criticality:**      Medium. If the collector's authoritative DNS server expects "id-"
                        prefixed subdomains and C# sends `{uuid}.leak.syxx.app`, the
                        collector may not find the token in its database.
- **Location:**         macOS/Sources/SyxxKit/LeakDetector.swift:229-231
                        (`id-{uuid}.{baseDomain}`); Windows/dotNET/Syxx.Kit/LeakDetector.cs:
                        242-245 (`{uuid}.{baseDomain}`)
- **Issue:**            macOS generates `id-{uuid}.leak.syxx.app`. C# generates
                        `{uuid}.leak.syxx.app`. If the collector server expects the `id-`
                        prefix, C# leak tests will fail to correlate, returning false
                        negatives or errors.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Align the formats — either add `id-` prefix to C# or remove
                        it from macOS. [Fix 2] Verify the collector server's expected format
                        and match it on both platforms.
- **Code Sample:**      [

                        // macOS LeakDetector.swift:229
                        let subdomain = "id-\(uuid).\(baseDomain)"
                        // C# LeakDetector.cs:242
                        var subdomain = $"{uuid}.{baseDomain}";
                        // Collector server may expect one format or the other

                        ]

---

- **[D067]**            All 6 section adapters run eagerly in Synthesizer.conclude() regardless
                        of which sections executed — ~30% of adapter work is redundant
- **Status:**           **OPEN** — Found during delta-analysis performance audit 2026-08-09.
- **Criticality:**      Medium. On a 6-section scan, adapters for sections that never ran
                        still process nil inputs, building empty EvidenceItem arrays and
                        computing DatapointCompleteness.
- **Location:**         macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:744-752
- **Issue:**            `conclude()` builds a tuple array of all 6 adapters and executes
                        them all every time, even when a section's inputs are entirely nil
                        (`.untested`). Each adapter builds `[EvidenceItem]` arrays, runs
                        `combine()` on sub-statuses, and computes `DatapointCompleteness`.
                        The `prophet` adapter alone is ~200 lines of `if let` chains.
                        Sections that never ran should be skipped.
- **Cross-ref. Delta:** B003 (Synthesizer duplicated switch blocks).
- **Fix(es):**          [Fix 1] Guard each adapter invocation with a check for `.untested`
                        status. [Fix 2] Return an empty `SectionEvidence` immediately for
                        untested sections without processing nil inputs.
- **Code Sample:**      [

                        // Synthesizer.swift:744-752
                        let sections: [(Section, SectionEvidence)] = [
                            (.prophet, EvidenceAdapters.prophet(...)),
                            (.mirror, EvidenceAdapters.mirror(...)),
                            (.portal, EvidenceAdapters.portal(...)),
                            (.glutton, EvidenceAdapters.glutton(...)),
                            (.cage, EvidenceAdapters.cage(...)),
                            (.witness, EvidenceAdapters.witness(...)),
                        ]
                        // All 6 run regardless of whether the section was tested

                        ]

---

- **[B026]**            C# MatrixDeterminativeEngine missing B162 section-usability gate for
                        support rules — non-usable section indicators inflate scores
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09. Cross-file trace: MatrixDeterminativeEngine.cs:920-950.
- **Criticality:**      Critical. The C# engine can emit diagnoses the Swift engine would
                        suppress. A non-usable section's indicators inflate alignedSupport,
                        coverage, fit, and strength on C# unconditionally.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:920-950
- **Issue:**            Swift's `rawScore` at MatrixDeterminativeEngine.swift:800 gates on
                        `!producing.status.isUsable` — non-usable sections contribute only
                        to overhang, not scoring. C# has no equivalent check. The `IsUsable()`
                        extension method exists at line 205 but is never called in the
                        per-indicator support/exclude/hard-exclusion loops.
- **Cross-ref. Delta:** B027, B028, B029 (same file, same class of defect).
- **Fix(es):**          [Fix 1] Add `sectionById.TryGetValue(obs.Section, out var producing)
                        && !producing.Status.IsUsable()` guard in the support-rules loop,
                        counting only toward overhang then `continue`.
- **Code Sample:**      [

                        // Swift (correct — gated)
                        if let producing = sectionByID[obs.section],
                           !producing.status.isUsable {
                            observedSupportWeight += rule.weight
                            r.observedSupport += rule.weight * aligned
                            continue
                        }
                        // C# (missing gate — always scores)
                        observedSupportWeight += rule.Weight;
                        r.ObservedSupport += rule.Weight * aligned;

                        ]

---

- **[B027]**            C# MatrixDeterminativeEngine missing B162 section-usability gate for
                        exclude rules — non-usable sections add contradictionMass
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09.
- **Criticality:**      Critical. Same defect as B026 but for exclusion-polarity rules.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:958-966
- **Issue:**            Exclusionary indicator from a non-usable section adds
                        `contradictionMass` on C#, which can artificially inflate
                        `Contradiction` and suppress a real diagnosis. Swift's rawScore at
                        line 820 skips non-usable sections entirely for exclude rules.
- **Cross-ref. Delta:** B026 (support rules), B028 (hard exclusion).
- **Fix(es):**          [Fix 1] Add section-usability guard before processing exclude rules.
- **Code Sample:**      [

                        // Swift (correct — skips non-usable)
                        if let producing = sectionByID[obs.section],
                           !producing.status.isUsable { continue }
                        // C# (missing gate — processes non-usable)

                        ]

---

- **[B028]**            C# MatrixDeterminativeEngine missing B163 ExclusionQualityFloor — no
                        quality floor for hard exclusions
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09.
- **Criticality:**      Critical. A low-quality observation can hard-exclude a diagnosis on
                        C# that Swift would reject. The `ExclusionQualityFloor` constant
                        (0.5) exists in Swift but is absent from C#.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:969-976
- **Issue:**            Swift requires `clamp01(obs.quality) >= Self.exclusionQualityFloor`
                        (0.5). C# requires only `s >= ExclusionFiringThreshold` (0.5) with
                        no quality floor. A degraded section (quality=0.2) producing a
                        "firing" exclusionary indicator at strength 0.6 can hard-exclude
                        incorrectly on C#.
- **Cross-ref. Delta:** B026, B027, B029.
- **Fix(es):**          [Fix 1] Add `private const double ExclusionQualityFloor = 0.5;` and
                        gate hard exclusion on `Clamp01(obs.Quality) >= ExclusionQualityFloor`.
- **Code Sample:**      [

                        // Swift: private static let exclusionQualityFloor = 0.5
                        // Swift: clamp01(obs.quality) >= Self.exclusionQualityFloor
                        // C#: only checks s >= ExclusionFiringThreshold — no quality gate

                        ]

---

- **[B029]**            C# MatrixDeterminativeEngine hard exclusion missing section-usability
                        check — untrusted sections can hard-exclude
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09.
- **Criticality:**      Critical. A hard exclusionary indicator from an `.untested` or
                        `.invalidated` section can hard-exclude a diagnosis on C#. Swift
                        requires `producing.status.isUsable`.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:969-976
- **Issue:**            Swift at line 832 requires `producing.status.isUsable` for hard
                        exclusion. C# only checks `obs.ObservedStrength is { } s && !obs.
                        Contradictory && s >= ExclusionFiringThreshold` — no section-
                        usability check.
- **Cross-ref. Delta:** B026, B027, B028.
- **Fix(es):**          [Fix 1] Add `sectionById.TryGetValue(obs.Section, out var producing)
                        && producing.Status.IsUsable()` to the hard-exclusion guard.
- **Code Sample:**      [

                        // Swift: let producing = sectionByID[obs.section],
                        //        producing.status.isUsable
                        // C#: only checks strength and contradictory flag

                        ]

---

- **[B030]**            C# MatrixDeterminativeEngine missing B159 emittableIDs filter in
                        CompetitorBestStrength — gated diagnoses inflate competitor margins
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09.
- **Criticality:**      High. A gated diagnosis with residual strength inflates the best
                        competitor's margin, suppressing emittable diagnoses below their tier.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:850-860
- **Issue:**            Swift's `competitorBestStrength` filters to `emittableIDs.contains(
                        other.id)` — a gated diagnosis is not a rival hypothesis. C# includes
                        ALL diagnoses. The `emittableIDs` variable doesn't exist in C#.
- **Cross-ref. Delta:** B026-B029.
- **Fix(es):**          [Fix 1] Compute `emittableIDs` (gated/hard-excluded → excluded)
                        before CompetitorBestStrength, filter competitors accordingly.
- **Code Sample:**      [

                        // Swift: emittableIDs.contains(other.id) in competitor filter
                        // C#: foreach (var other in Spec.Diagnoses) — no filter

                        ]

---

- **[B031]**            C# MatrixDeterminativeEngine missing B168 cleanID guard — clean
                        diagnosis can enter overhang suppression
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09.
- **Criticality:**      High. The clean diagnosis can be gated/hard-excluded into the
                        suppressed list, potentially blocking a clean result.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:880-895
- **Issue:**            Swift guards gate/hard-exclusion admission with `d.id != Self.cleanID`.
                        C# has no such guard. The clean diagnosis can enter the `suppressed`
                        list via the gate path.
- **Cross-ref. Delta:** B026-B030.
- **Fix(es):**          [Fix 1] Insert `d.Id != CleanId` guard at both admission sites.
- **Code Sample:**      [

                        // Swift: if d.id != Self.cleanID && raw.observedSupport >= ...
                        // C#: if (raw.ObservedSupport >= OverhangFloor) — no CleanId check

                        ]

---

- **[B032]**            DNSResolver spoof-detection uses fragile string-matching on
                        human-readable error messages
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09.
- **Criticality:**      Medium. If error messages are reworded, the spoof guard silently
                        fails and a genuine spoof observation is overwritten by a timeout.
- **Location:**         macOS/Sources/SyxxKit/DNSResolver.swift:513, 525
- **Issue:**            The spoof-retention logic checks `lastError?.contains("spoofed")`.
                        If `DNSResolverError` messages are reworded (e.g., "injected packet"
                        instead of "spoofed"), the guard fails silently.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Track spoof detection as a separate `Bool` flag rather than
                        pattern-matching on human-readable error strings.
- **Code Sample:**      [

                        // DNSResolver.swift:513
                        if !(lastError?.contains("spoofed") ?? false) {
                            lastError = "UDP timeout"
                        }

                        ]

---

- **[B033]**            DNSResponse.didFail includes .servFail and .refused as "failures" —
                        conflates transport errors with protocol-level negatives
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09.
- **Criticality:**      Low. Callers that use `didFail` as a "no answer" gate get false
                        positives for valid protocol-level responses.
- **Location:**         macOS/Sources/SyxxKit/DNSResolver.swift:330
- **Issue:**            `.servFail` and `.refused` mean the resolver REPLIED with a valid
                        error. The property name implies transport failure but includes
                        protocol-level negatives. EvidenceAdapters.prophet handles these
                        separately as `.inconclusive`, but `didFail` would return true.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Rename to `didNotSucceed` or split into `didFail` (transport
                        only) and `didNotResolve` (includes RCODE errors).
- **Code Sample:**      [

                        // DNSResolver.swift:330
                        case .timeout, .transportError, .malformedResponse,
                             .responseIdMismatch, .truncated, .servFail,
                             .refused, .rcodeOther: return true

                        ]

---

- **[B034]**            ScriptMismatchInconclusive EvidenceId missing from C# — inconclusive
                        warp conflated with confirmed tampering
- **Status:**           **OPEN** — Found during deep delta-analysis parity audit 2026-08-09.
- **Criticality:**      Critical. Same input produces different evidence IDs across platforms.
                        C# emits `ScriptMismatch` (confirmed tampering) where Swift emits
                        `ScriptMismatchInconclusive` (abstention). The matrix engine scores
                        them differently.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:463;
                        macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:354
- **Issue:**            C# code at line 463 emits `EvidenceId.ScriptMismatch` for the
                        `WarpConclusion.INCONCLUSIVE` branch. Swift uses the distinct
                        `.scriptMismatchInconclusive`. The C# EvidenceId enum (37 members)
                        has no `ScriptMismatchInconclusive` case (Swift has 38). C# JSON
                        converter throws on macOS-produced reports containing this ID.
- **Cross-ref. Delta:** B024 (ExfilSuspicious missing — same pattern).
- **Fix(es):**          [Fix 1] Add `ScriptMismatchInconclusive` to C# EvidenceId enum,
                        RawValue(), JSON converter, DisplayName(). [Fix 2] Change
                        Synthesizer.cs:463 to use EvidenceId.ScriptMismatchInconclusive.
- **Code Sample:**      [

                        // C# Synthesizer.cs:463 — INCONCLUSIVE branch
                        ev.Add(EvidenceItem.Create(Section.Mirror,
                            EvidenceId.ScriptMismatch, ...))  // WRONG
                        // Swift Synthesizer.swift:354 — INCONCLUSIVE branch
                        evidenceId: .scriptMismatchInconclusive  // CORRECT

                        ]

---

- **[B035]**            C# authoritative negative treats CNAME-only answers as divergence
                        instead of deferring to the CNAME delegation path
- **Status:**           **OPEN** — Found during the completed five-agent audit
                        2026-08-09.
- **Criticality:**      Critical. A zone that delegates through CNAMEs can be flagged as
                        injected tampering on Windows even though the authoritative answer
                        is just non-address data.
- **Location:**         Windows/dotNET/Syxx.Kit/AuthoritativeVantage.cs:148-190;
                        macOS/Sources/SyxxKit/AuthoritativeVantage.swift:131-193
- **Issue:**            The C# path returns `Diverges` as soon as `authSet.Count == 0`,
                        before it knows whether the authoritative response was merely a
                        CNAME-only delegation. The Swift twin tracks `sawCNAMEOnly` and
                        returns `Inconclusive` instead, so the same zone can become a false
                        negative on Windows.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Port the Swift CNAME delegation guard to C#.
                        [Fix 2] Only
                        classify a zero-address authoritative response as divergence when
                        no CNAME delegation was observed.
- **Code Sample:**      [

                        // Windows/dotNET/Syxx.Kit/AuthoritativeVantage.cs:189-190
                        if (authSet.Count == 0)
                            return Make(AuthComparisonStatus.Diverges, ...);

                        // macOS/Sources/SyxxKit/AuthoritativeVantage.swift:187-193
                        if authSet.isEmpty && sawCNAMEOnly {
                            return AuthoritativeComparison(status: .inconclusive, ...)
                        }

                        ]

---

- **[D068]**            SectionInputs.warp field marked LEGACY but still public and Codable —
                        dead code or misleading comment
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09.
- **Criticality:**      Low. If never populated and never read, it is dead code. If it IS
                        read, the comment is misleading.
- **Location:**         macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:60
- **Issue:**            `SectionInputs.warp` is annotated "LEGACY warp result... No longer
                        populated." It is a `WarpResult?` public property on a `Sendable`
                        struct. Either remove or clarify.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Remove if dead.
                        [Fix 2] Update comment if still used.
- **Code Sample:**      [

                        // Synthesizer.swift:60
                        /// LEGACY warp result (WARP_ASSEMBLY / proxy path, retired)
                        /// — kept as a compile shim... No longer populated.

                        ]

---

- **[D069]**            SynthesisEngine.mapEvidenceToIndicators uses #if DEBUG for C063
                        missing-completeness guard — invisible in production
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09.
- **Criticality:**      Medium. A real defect in production is invisible because the stderr
                        write and assert are compiled out in release builds.
- **Location:**         macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift:140-152
- **Issue:**            `#if DEBUG` block writes to stderr; `assert` on next line also
                        compiled out. Missing completeness silently defaults to quality=0.0
                        with no observability signal.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Replace `#if DEBUG` stderr write with `SyxxDiagnostics.
                        capture` call (available in release).
- **Code Sample:**      [

                        // SynthesisEngine.swift:147-152
                        #if DEBUG
                        FileHandle.standardError.write(...)
                        #endif
                        assert(rawCompleteness == nil || rawCompleteness! > 0, ...)

                        ]

---

- **[D070]**            C# Assemble overhang filter lacks documentation for HardExcluded
                        asymmetry with BelowTier
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09.
- **Criticality:**      Low. The Swift version documents the rationale; C# does not.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:1198
- **Issue:**            `Assemble` filters overhang to `Reason != HardExcluded`. Swift has
                        the same filter documented as "hard exclusion is not an overhang —
                        it was affirmatively ruled out." C# lacks this rationale.
- **Cross-ref. Delta:** B026-B031.
- **Fix(es):**          [Fix 1] Add comment explaining why HardExcluded is excluded from
                        the overhang.
- **Code Sample:**      [

                        // C#: s.Reason != SuppressedDiagnosis.SuppressionReason.HardExcluded
                        // No doc comment explaining the asymmetry

                        ]

---

- **[D071]**            DNSResolver UDP retry count differs — Swift 2, C# 3
- **Status:**           **OPEN** — Found during deep delta-analysis parity audit 2026-08-09.
- **Criticality:**      High. Same network conditions produce different outcomes. Swift
                        returns .timeout where C# succeeds on the third attempt.
- **Location:**         macOS/Sources/SyxxKit/DNSResolver.swift:305;
                        Windows/dotNET/Syxx.Kit/DNSResolver.cs:200
- **Issue:**            Swift: `maxUDPAttempts = 2`. C#: `MaxUDPAttempts = 3`. Both use
                        2000ms receive timeout per attempt. Affects every probe.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Align both to 3.
                        [Fix 2] Make configurable via ProbeBudget.
- **Code Sample:**      [

                        // Swift: private static let maxUDPAttempts = 2
                        // C#:   private const int MaxUDPAttempts = 3;

                        ]

---

- **[D072]**            DNSResponse missing 4 diagnostic fields in C# — OffClassAnswerCount,
                        ConflictingCNAMEOwnerCount, SpoofedPacketCount, AttemptCount
- **Status:**           **OPEN** — Found during deep delta-analysis parity audit 2026-08-09.
- **Criticality:**      High. Injection detection and latency diagnostics are thinner on
                        Windows/Linux because these observations are silently dropped.
- **Location:**         Windows/dotNET/Syxx.Kit/DNSResolver.cs:130-170
- **Issue:**            Swift tracks offClassAnswerCount, conflictingCNAMEOwnerCount,
                        spoofedPacketCount, and attemptCount. C# has no equivalent fields.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add the four fields to C# DNSResponse.
                        [Fix 2] Populate in
                        UDP response parser. [Fix 3] Wire into Synthesizer evidence path.
- **Code Sample:**      [

                        // Swift DNSResolver.swift:174-194
                        public let offClassAnswerCount: Int
                        public let conflictingCNAMEOwnerCount: Int
                        public let spoofedPacketCount: Int
                        public let attemptCount: Int
                        // C# — none of these exist

                        ]

---

- **[D073]**            ProbeBudget defaults not overridable via environment variables in C#
- **Status:**           **OPEN** — Found during deep delta-analysis parity audit 2026-08-09.
- **Criticality:**      High. CI/calibration runs cannot extend probe budgets on Windows/
                        Linux. Swift reads SYXX_BUDGET_* env vars; C# hardcodes values.
- **Location:**         Windows/dotNET/Syxx.Kit/ProbeBudget.cs:75-93
- **Issue:**            Swift reads `SYXX_BUDGET_PROPHET_TOTAL` etc. C# has hardcoded
                        `DefaultProphet => new(TimeSpan.FromSeconds(45), ...)` with no env
                        var lookup. Same for DefaultMirror, DefaultPortal, DefaultGlutton.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add env var lookups in C# factory properties.
- **Code Sample:**      [

                        // Swift: ProcessInfo...environment["SYXX_BUDGET_PROPHET_TOTAL"]
                        // C#:   public static ProbeBudget DefaultProphet =>
                        //         new(TimeSpan.FromSeconds(45), ...)  // hardcoded

                        ]

---

- **[D074]**            Config buildEnvironment differs — no DEBUG detection in C#
- **Status:**           **OPEN** — Found during deep delta-analysis parity audit 2026-08-09.
- **Criticality:**      Medium. DEBUG builds of C# clients report "production" to Sentry,
                        polluting the production project.
- **Location:**         Windows/dotNET/Syxx.Kit/SyxxConfig.cs:56-57
- **Issue:**            Swift: `#if DEBUG return "development"`. C#: reads
                        `SYXX_ENVIRONMENT` env var only, no `#if DEBUG` check.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add `#if DEBUG` preprocessor check returning "development".
- **Code Sample:**      [

                        // Swift: #if DEBUG return "development"
                        // C#:   Environment.GetEnvironmentVariable("SYXX_ENVIRONMENT")
                        //         ?? "production"  // no DEBUG check

                        ]

---

- **[D075]**            Config dimensionBaseURL source differs — UserDefaults vs env var
- **Status:**           **OPEN** — Found during deep delta-analysis parity audit 2026-08-09.
- **Criticality:**      Medium. macOS GUI configures dimension URL in UserDefaults; C# client
                        reads SYXX_DIMENSION_URL env var — different sources, different values.
- **Location:**         macOS/Sources/SyxxKit/SyxxConfig.swift:43-46;
                        Windows/dotNET/Syxx.Kit/SyxxConfig.cs:60-61
- **Issue:**            Swift reads `UserDefaults.standard.string(forKey: "dimension_base_url")`.
                        C# reads `Environment.GetEnvironmentVariable("SYXX_DIMENSION_URL")`.
- **Cross-ref. Delta:** D076 (sentryDSN same pattern).
- **Fix(es):**          [Fix 1] Both platforms should read from same source.
                        [Fix 2] C# should
                        check config file/registry; Swift should also support env var.
- **Code Sample:**      [

                        // Swift: UserDefaults.standard.string(forKey: "dimension_base_url")
                        // C#:   Environment.GetEnvironmentVariable("SYXX_DIMENSION_URL")

                        ]

---

- **[D076]**            Config sentryDSN source differs — UserDefaults+env var vs env var only
- **Status:**           **OPEN** — Found during deep delta-analysis parity audit 2026-08-09.
- **Criticality:**      Medium. macOS GUI writes sentry_dsn to UserDefaults; C# client only
                        checks SENTRY_DSN env var. Crash reporting silently absent on C#.
- **Location:**         macOS/Sources/SyxxKit/SyxxConfig.swift:33-37;
                        Windows/dotNET/Syxx.Kit/SyxxConfig.cs:63-68
- **Issue:**            Swift: checks SENTRY_DSN env var, falls back to UserDefaults. C#:
                        checks only SENTRY_DSN env var.
- **Cross-ref. Delta:** D075 (dimensionBaseURL same pattern).
- **Fix(es):**          [Fix 1] Add config-file or registry fallback for C# SentryDsn.
- **Code Sample:**      [

                        // Swift: ProcessInfo...["SENTRY_DSN"] ?? UserDefaults...["sentry_dsn"]
                        // C#:   Environment.GetEnvironmentVariable("SENTRY_DSN")  // only

                        ]

---

- **[D077]**            SectionStatusEntry has extra nullable fields in C# not in Swift —
                        schema divergence
- **Status:**           **OPEN** — Found during deep delta-analysis parity audit 2026-08-09.
- **Criticality:**      Low. C# has datapointsObserved/datapointsExpected (nullable,
                        JsonIgnore WhenWritingNull). Swift has 3 fields; C# has 5. Latent
                        asymmetry.
- **Location:**         macOS/Sources/SyxxKit/Synthesis/Conclusion.swift:157-165;
                        Windows/dotNET/Syxx.Kit/Synthesis/Conclusion.cs:303-318
- **Issue:**            Different JSON shapes for the same logical object. Swift's Codable
                        ignores unknown keys by default, but strict decoding would reject
                        C# output.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add fields to Swift or remove from C#.
                        [Fix 2] Align schema.
- **Code Sample:**      [

                        // Swift: 3 fields (section, status, completeness)
                        // C#:    5 fields (+ datapointsObserved, datapointsExpected)

                        ]

---

- **[D078]**            Redundant DNS queries across sections — same domain queried 3+ times
                        per full scan
- **Status:**           **OPEN** — Found during deep delta-analysis performance audit
                        2026-08-09.
- **Criticality:**      Critical. 200–600ms redundant latency per full scan. ResolverConsensus
                        Store exists but is never populated by Prophet/Mirror UI paths.
- **Location:**         macOS/Sources/SyxxApp/AppModel.swift:639,1138,1160;
                        macOS/Sources/SyxxKit/ResolverConsensusEngine.swift:130,173
- **Issue:**            `runAllWardens` → `runLookup` (12-oracle fan-out) → `runCompare`
                        → `runLocalDeltaFanOut` (3 UDP + 3 DoH). Cloudflare/Google/Quad9
                        queried 3+ times for same domain. `ResolverConsensusStore` exists at
                        ResolverConsensusStore.swift:43 but only `runUnified()` populates it.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Populate ResolverConsensusStore from Prophet/Mirror paths.
                        [Fix 2] Reuse cached results across sections in the same scan.
- **Code Sample:**      [

                        // ResolverConsensusStore.swift:43 — exists but underutilized
                        // runUnified() populates it; Prophet/Mirror UI paths do not

                        ]

---

- **[D079]**            Sync socket I/O in Capture hot loop — async facade over sync Poll/Receive
- **Status:**           **OPEN** — Found during deep delta-analysis performance audit
                        2026-08-09.
- **Criticality:**      Critical. At 10,000 DNS packets/sec: 10,000 thread-pool thread-
                        seconds wasted per second of capture. 100ms Poll timeout means 10
                        sync polls/second even when idle.
- **Location:**         Windows/dotNET/Syxx.Kit/Capture/CaptureEngine.cs:163-176, 181-198
- **Issue:**            `socket.Poll(100000, SelectRead)` + `socket.Receive(buffer)` block
                        thread-pool threads. The `async Task` shell is a facade over entirely
                        synchronous I/O.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Use `Socket.ReceiveAsync` with `SocketAsyncEventArgs` pooling.
                        [Fix 2] Replace 100ms Poll with async wait via `Socket.ReceiveAsync`.
- **Code Sample:**      [

                        // CaptureEngine.cs:163-169
                        socket.Poll(100000, SelectMode.SelectRead);  // SYNC 100ms
                        int bytesRead = socket.Receive(buffer);       // SYNC blocking

                        ]

---

- **[D080]**            Sync-over-async deadlock risk in MonitorScheduler — GetResult() inside
                        lock
- **Status:**           **OPEN** — Found during deep delta-analysis performance audit
                        2026-08-09.
- **Criticality:**      Critical. Production crash risk. `.GetAwaiter().GetResult()` inside
                        `lock(_runLock)` blocks calling thread on async DNS + file I/O. If
                        TickOnceAsync needs to resume on a thread holding _runLock → deadlock.
- **Location:**         Windows/dotNET/Syxx.Kit/MonitorScheduler.cs:70, 79-88
- **Issue:**            `RunOnce()`:79 acquires `lock(_runLock)` then calls `_tick()`:88 →
                        `ContinuousMonitor.TickOnceAsync().GetAwaiter().GetResult()`:70.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Move the async call outside the lock.
                        [Fix 2] Use
                        `Task.Run(() => TickOnceAsync()).GetAwaiter().GetResult()` or
                        restructure to avoid sync-over-async.
- **Code Sample:**      [

                        // MonitorScheduler.cs:79-88
                        lock (_runLock) {
                            _tick();  // → TickOnceAsync().GetAwaiter().GetResult()
                        }

                        ]

---

- **[D081]**            6–9 heap allocations per captured packet — 60K–90K allocs/s at 10K pps
- **Status:**           **OPEN** — Found during deep delta-analysis performance audit
                        2026-08-09.
- **Criticality:**      High. 2.4–3.6 MB/s GC pressure at 10,000 packets/sec. byte[] copies
                        are the largest allocations. Lists allocated even for zero-result
                        packets.
- **Location:**         Windows/dotNET/Syxx.Kit/Capture/CaptureEngine.cs:181-198
- **Issue:**            Per-packet: `new byte[bytesRead]`, `new byte[payloadLen]`,
                        `new List<DNSQuestionReport>()`, `new List<DNSAnswerReport>()`,
                        `new StringBuilder()`, `new DNSCaptureRecord`. Lists allocated even
                        for packets with zero questions/answers.
- **Cross-ref. Delta:** D079 (sync I/O in same loop).
- **Fix(es):**          [Fix 1] Pool byte[] buffers with ArrayPool.
                        [Fix 2] Lazily allocate
                        lists only when questions/answers exist. [Fix 3] Use
                        ObjectPool<DNSCaptureRecord>.
- **Code Sample:**      [

                        // CaptureEngine.cs:181-198 — per-packet allocations
                        byte[] buffer = new byte[bytesRead];       // pool
                        byte[] payload = new byte[payloadLen];     // pool
                        var questions = new List<DNSQuestionReport>(); // lazy
                        var answers = new List<DNSAnswerReport>();    // lazy

                        ]

---

- **[D082]**            Live capture polls file every 400ms — 150 wakes/min, 400ms display
                        latency
- **Status:**           **OPEN** — Found during deep delta-analysis performance audit
                        2026-08-09.
- **Criticality:**      High. No FSEvents/DispatchSource.FileSystemEvent — pure polling adds
                        up to 400ms latency between packet capture and GUI display.
- **Location:**         macOS/Sources/SyxxApp/AppModel.swift:1640-1655, 1750-1814
- **Issue:**            `startRealCapture()` → `while !Task.isCancelled` → `readNewRows()` →
                        `FileManager.attributesOfItem` (stat) → `FileHandle.readDataToEndOfFile`
                        → `Task.sleep(.milliseconds(400))`. 150 iterations per 60-second
                        capture.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Replace polling with `DispatchSource.makeFileSystemObjectSource`
                        or `FSEvents` for event-driven tail.
- **Code Sample:**      [

                        // AppModel.swift:1655
                        try await Task.sleep(for: .milliseconds(400))  // polling

                        ]

---

- **[D083]**            JSONEncoder created per-packet in capture — 100+ encoder instances/sec
- **Status:**           **OPEN** — Found during deep delta-analysis performance audit
                        2026-08-09.
- **Criticality:**      High. `JSONEncoder` initialization is ~10–20µs. At 100+ queries/sec:
                        100+ encoder instances plus temporary Data buffer and String per encode.
- **Location:**         macOS/Sources/SyxxCLI/SyxxCage.swift:570-576
- **Issue:**            `let encoder = JSONEncoder()` inside per-packet function. Should be
                        `static let` for reuse.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Move encoder to `static let` at type level.
- **Code Sample:**      [

                        // SyxxCage.swift:570 — per-packet allocation
                        let encoder = JSONEncoder()  // should be static let
                        encoder.encode(record)

                        ]

---

- **[D084]**            5-pass over inWindow records in CaptureEventClassifier — 5× predicate
                        evaluations
- **Status:**           **OPEN** — Found during deep delta-analysis performance audit
                        2026-08-09.
- **Criticality:**      Medium. 5,000 records → 25,000 predicate evaluations instead of
                        5,000. Where().ToList() also materializes a copy.
- **Location:**         Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs:207-252
- **Issue:**            PASS 1: time filter. PASS 2: Any(Outbound). PASS 3: Where(Inbound).
                        ToList(). PASS 4: foreach rcode tally. PASS 5: Count(Outbound).
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Single-pass tally with counters for inbound/outbound/rcode.
- **Code Sample:**      [

                        // CaptureEvents.cs:207-252 — 5 separate iterations
                        foreach (r in records) { ... }  // PASS 1
                        inWindow.Any(r => "Outbound")   // PASS 2
                        inWindow.Where(...).ToList()    // PASS 3
                        foreach (r in inbound) { ... }  // PASS 4
                        inWindow.Count(r => "Outbound") // PASS 5

                        ]

---

- **[D085]**            Missing ConfigureAwait(false) in 90% of library async calls — UI thread
                        contention
- **Status:**           **OPEN** — Found during deep delta-analysis performance audit
                        2026-08-09.
- **Criticality:**      Medium. Only 27 ConfigureAwait(false) across 224 .cs files. Every
                        uncaptured await schedules continuation back to UI thread, causing
                        thread-pool starvation and UI jank.
- **Location:**         Windows/dotNET/Syxx.Kit/ (LeakDetector.cs, DNSResolver.cs,
                        DimensionClient.cs, etc.)
- **Issue:**            LeakDetector.RunCollectorTestAsync alone has 4+ uncaptured awaits.
                        DimensionClient.WarpRawAssetAsync, DNSResolver async chain all affected.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add ConfigureAwait(false) to all library async calls that
                        don't need to resume on the UI thread.
- **Code Sample:**      [

                        // LeakDetector.cs:261 — uncaptured await
                        var response = await httpClient.SendAsync(request);
                        // Should be:
                        var response = await httpClient.SendAsync(request)
                            .ConfigureAwait(false);

                        ]

---

- **[D086]**            Full O(m×n) matrix in Damerau-Levenshtein per brand comparison — 57MB
                        GC for 1000 domains
- **Status:**           **OPEN** — Found during deep delta-analysis performance audit
                        2026-08-09.
- **Criticality:**      Medium. The OSA recurrence only needs rows i-2, i-1, and i. A 3-row
                        rolling array would reduce allocation from O(m×n) to O(3n).
- **Location:**         Windows/dotNET/Syxx.Kit/LookalikeDetector.cs:79
- **Issue:**            `var d = new int[m + 1, n + 1]` allocates full matrix. For 30-char
                        domain × 15 brands: 15 × 31 × 31 = 14,415 ints per call. 1000+
                        domains in forensics: 57MB of GC pressure.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Use 3-row rolling array instead of full matrix.
- **Code Sample:**      [

                        // LookalikeDetector.cs:79
                        var d = new int[m + 1, n + 1];  // O(m×n) — use 3-row rolling

                        ]

---

- **[D087]**            run_id is always "none" — scanCorrelationId never propagated to
                        SyxxDiagnostics.run
- **Status:**           **OPEN** — Found during deep delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Critical. Every Sentry event across all platforms has run_id="none".
                        Zero correlation between events, spans, and breadcrumbs from the same
                        scan. Synthesizer always sees unattributed_degradation.
- **Location:**         macOS/Sources/SyxxKit/SyxxDiagnostics.swift:111;
                        macOS/Sources/SyxxApp/AppModel.swift:635, 750;
                        Windows/dotNET/Syxx.Kit/SyxxDiagnostics.cs:138
- **Issue:**            `scanCorrelationId` is set on ViewModel but never propagated to
                        `SyxxDiagnostics.run`. `SentryDiagnosticSink` always tags
                        `syxx.run_id = SyxxDiagnostics.run?.id ?? "none"`.
- **Cross-ref. Delta:** D055 (missing run attribution wiring), D089 (correlation on spans).
- **Fix(es):**          [Fix 1] Set SyxxDiagnostics.run at scan entry points (AppModel.
                        runAllWardens, CLI run). [Fix 2] Wire scanCorrelationId into run.id.
- **Code Sample:**      [

                        // AppModel.swift:635 — scanCorrelationId set, never propagated
                        scanCorrelationId = corrId
                        // SentryDiagnosticSink.swift:65 — always "none"
                        tags["syxx.run_id"] = SyxxDiagnostics.run?.id ?? "none"

                        ]

---

- **[D088]**            Breadcrumbs in AppModel/AppViewModel bypass SyxxDiagnostics → skip
                        redaction — PII leak vector
- **Status:**           **OPEN** — Found during deep delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      High. Any breadcrumb containing a domain name or IP is exported
                        verbatim to Sentry instead of being redacted through the
                        SentryDiagnosticSink bridge.
- **Location:**         macOS/Sources/SyxxApp/AppModel.swift:638,647,753,776,780,789,864;
                        Windows/dotNET/SyxxUI/ViewModels/AppViewModel.cs:368,386,423,490,507,
                        545,579,593,628,639
- **Issue:**            GUI layers call SentrySDK.addBreadcrumb() directly instead of
                        SyxxDiagnostics.breadcrumb(). The SyxxDiagnostics path applies
                        redaction (domain/IP/home-path stripping); the direct path does not.
- **Cross-ref. Delta:** D019 (dual telemetry).
- **Fix(es):**          [Fix 1] Replace all direct SentrySDK breadcrumb calls with
                        SyxxDiagnostics.breadcrumb().
- **Code Sample:**      [

                        // AppModel.swift:638 — direct Sentry, no redaction
                        SentrySDK.addBreadcrumb(Breadcrumb(...))
                        // Should be:
                        SyxxDiagnostics.breadcrumb(category: "gui.state", ...)

                        ]

---

- **[D089]**            scanCorrelationId not attached to OTel spans — cannot join breadcrumbs
                        to spans in collector
- **Status:**           **OPEN** — Found during deep delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Medium. A query in the collector cannot join a scan's breadcrumbs
                        to its spans.
- **Location:**         macOS/Sources/SyxxKit/ProbeRunner.swift:70-88;
                        Windows/dotNET/Syxx.Kit/ProbeRunner.cs:110
- **Issue:**            scanCorrelationId is set on ViewModel but never passed as a tag to
                        probe.* or dns.query.* spans.
- **Cross-ref. Delta:** D087 (run_id always none), D055 (missing run attribution).
- **Fix(es):**          [Fix 1] Pass scanCorrelationId as a span attribute at probe entry.
- **Code Sample:**      [

                        // ProbeRunner.swift:70 — no correlation tag
                        SyxxTracer.span("probe.\(label)")
                        // Should include: attributes["syxx.scan_id"] = scanCorrelationId

                        ]

---

- **[D090]**            Cross-platform span naming convention inconsistent — server uses
                        "server.*", clients use "dns.*", "probe.*", "syxx.*"
- **Status:**           **OPEN** — Found during deep delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Low. No shared prefix for cross-service span grouping in collector.
- **Location:**         Server/syxx_server/app.py (span creation sites);
                        macOS/Sources/SyxxKit/ (ProbeRunner, DNSResolver, Synthesizer)
- **Issue:**            Server: "server.warp", "server.diff", "server.authoritative".
                        Clients: "dns.query.udp", "probe.Prophet", "syxx.synthesis.synthesize".
                        No shared "syxx." prefix across all spans.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Standardize on "syxx.*" prefix for all spans.
                        [Fix 2] Add
                        "syxx.platform" attribute to all spans.
- **Code Sample:**      [

                        // Server: "server.warp"
                        // macOS: "dns.query.udp", "probe.Prophet"
                        // C#:    "syxx.synthesis.synthesize"
                        // No shared prefix for cross-service grouping

                        ]

---

- **[D091]**            DNS resolution error path has span outcome but no SyxxDiagnostics.
                        capture() — invisible to Sentry
- **Status:**           **OPEN** — Found during deep delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Medium. DNS errors recorded in OTel spans but not as Sentry events.
- **Location:**         macOS/Sources/SyxxKit/DNSResolver.swift:366-388;
                        Windows/dotNET/Syxx.Kit/DNSResolver.cs:207-255
- **Issue:**            `resolveSecurely` catch block sets span.setOutcome(.inconclusive) but
                        never calls SyxxDiagnostics.capture().
- **Cross-ref. Delta:** D058 (no error capture in UI).
- **Fix(es):**          [Fix 1] Add SyxxDiagnostics.capture() in the DNS error path.
- **Code Sample:**      [

                        // DNSResolver.swift:383 — span outcome set, no capture
                        classify: { .inconclusive }  // span only
                        // No SyxxDiagnostics.capture() call

                        ]

---

- **[D092]**            SyxxDiagnostics.isReporting and isActive are duplicate APIs — both
                        check sink != nil
- **Status:**           **OPEN** — Found during deep delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Low. Two names for the same condition creates confusion.
- **Location:**         macOS/Sources/SyxxKit/SyxxDiagnostics.swift:128, 139
- **Issue:**            Both `isReporting` and `isActive` check the same condition
                        (`sink != nil`). `isReporting` is documented but `isActive` is the
                        one checked by the settings UI.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Deprecate one, document the canonical name.
- **Code Sample:**      [

                        // SyxxDiagnostics.swift:128 — isReporting
                        // SyxxDiagnostics.swift:139 — isActive (same condition)
                        public static var isReporting: Bool { sink != nil }
                        public static var isActive: Bool { sink != nil }

                        ]

---

- **[D093]**            No SyxxDiagnostics.capture() in probe timeout/error path — probe
                        failures generate no Sentry events
- **Status:**           **OPEN** — Found during deep delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Low. A probe that times out or throws is recorded in the span but
                        generates no Sentry event.
- **Location:**         macOS/Sources/SyxxKit/ProbeRunner.swift:88-115;
                        Windows/dotNET/Syxx.Kit/ProbeRunner.cs:110-185
- **Issue:**            Probe runner creates OTel spans and sets syxx.outcome tags but does
                        not call SyxxDiagnostics.capture() on failure.
- **Cross-ref. Delta:** D091 (DNS error no capture).
- **Fix(es):**          [Fix 1] Add SyxxDiagnostics.capture() on probe failure.
- **Code Sample:**      [

                        // ProbeRunner.swift:88-115 — span outcome set, no capture

                        ]

---

- **[D094]**            C# SyxxDiagnostics.Install uses Interlocked.Exchange but reads use
                        Volatile.Read — memory barrier asymmetry
- **Status:**           **OPEN** — Found during deep delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Low. Technically correct but fragile — future reader may not realize
                        write side needs stronger barrier.
- **Location:**         Windows/dotNET/Syxx.Kit/SyxxDiagnostics.cs:128
- **Issue:**            `Install()` uses `Interlocked.Exchange(ref _sink, sink)` (full
                        barrier) but `Capture()` and `Breadcrumb()` use `Volatile.Read(ref
                        _sink)` (acquire-only).
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add comment documenting the asymmetry.
                        [Fix 2] Or use
                        Interlocked.CompareExchange for reads too.
- **Code Sample:**      [

                        // Install: Interlocked.Exchange(ref _sink, sink)  // full barrier
                        // Read:   Volatile.Read(ref _sink)                // acquire-only

                        ]

---

- **[D095]**            Linux Avalonia client has no ViewModel-level breadcrumbs — parity gap
                        with macOS and Windows
- **Status:**           **OPEN** — Found during deep delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Low. Linux scans produce no lifecycle breadcrumbs (scan-start, scan-
                        complete). macOS AppModel and Windows AppViewModel both have them.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/ (no SentrySdk.AddBreadcrumb or
                        SyxxDiagnostics.Breadcrumb in ViewModel files)
- **Issue:**            Linux client delegates to shared Syxx.Kit/Syxx.Telemetry, but its
                        ViewModel layer has no scan lifecycle breadcrumbs.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add scan-start and scan-complete breadcrumbs to Linux
                        ViewModels.
- **Code Sample:**      [

                        // No breadcrumb calls in Linux ViewModel layer
                        // macOS: 7 inline SentrySDK.addBreadcrumb in AppModel
                        // Windows: 11 inline SentrySdk.AddBreadcrumb in AppViewModel
                        // Linux: 0

                        ]

---

- **[D096]**            Linux diagnostic sink installs Sentry capture without initializing
                        the Sentry SDK first
- **Status:**           **OPEN** — Found during the completed five-agent audit
                        2026-08-09.
- **Criticality:**      Critical. The sink path can record capture calls, but without a live
                        Sentry SDK initialization those events never reach the backend.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/Services/LinuxReadyServices.cs:24-53;
                        Windows/dotNET/Syxx.Telemetry/SentryDiagnosticSink.cs:80-96
- **Issue:**            Linux turns Sentry on by calling `SentryDiagnosticSink.Install()`
                        at runtime, but the surrounding startup path never initializes the
                        Sentry SDK the way the Windows client does. The sink can therefore
                        exist without a live capture pipeline, dropping diagnostic events.
- **Cross-ref. Delta:** D058 (UI capture gap) and D062 (platform tagging), but this is
                        the Linux initialization path specifically.
- **Fix(es):**          [Fix 1] Initialize Sentry before installing the diagnostic sink.
                        [Fix 2] Make the install helper fail closed or log an explicit
                        warning when Sentry is unavailable.
- **Code Sample:**      [

                        // Linux/Ubuntu/SyxxUI.Avalonia/Services/LinuxReadyServices.cs:24-53
                        ApplySentryState(settings.CrashReportingEnabled);
                        global::Syxx.Telemetry.SentryDiagnosticSink.Install(
                            surface: "gui",
                            platform: "linux",
                            ifAllowed: true);

                        // Windows/dotNET/Syxx.Telemetry/SentryDiagnosticSink.cs:96
                        SentrySdk.CaptureEvent(e);

                        ]

---

- **[D097]**            Windows GUI bypasses shared OTel bootstrap and swallows exporter
                        startup failures
- **Status:**           **OPEN** — Found during the completed five-agent audit
                        2026-08-09.
- **Criticality:**      Critical. The GUI can enter a state where telemetry is requested but
                        the exporter never starts, and the failure is hidden behind a silent
                        catch.
- **Location:**         Windows/dotNET/SyxxUI/App.xaml.cs:88-103;
                        Windows/dotNET/Syxx.Telemetry/OTelBootstrap.cs:39-87
- **Issue:**            The Windows app constructs its own tracer provider with
                        `Sdk.CreateTracerProviderBuilder()` and catches all exceptions
                        without surfacing them, instead of using the shared
                        `OTelBootstrap.Register()` path that already reports init failures
                        through `SyxxDiagnostics.Capture()`. If exporter startup fails, the
                        GUI continues as if telemetry is live.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Route the GUI through `OTelBootstrap.Register()`.
                        [Fix 2]
                        Surface exporter-startup failures through diagnostics instead of
                        swallowing them.
- **Code Sample:**      [

                        // Windows/dotNET/SyxxUI/App.xaml.cs:88-103
                        try {
                            _tracerProvider = Sdk.CreateTracerProviderBuilder()...Build();
                        } catch {
                            // Telemetry is best-effort after explicit setup consent.
                        }

                        // Windows/dotNET/Syxx.Telemetry/OTelBootstrap.cs:39-87
                        return Sdk.CreateTracerProviderBuilder()...Build();
                        SyxxDiagnostics.Capture(...);

                        ]

---

- **[A047]**            investigate.py _resolve_all uses type(e).__name__ in broad except —
                        loses exception chain detail
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09.
- **Criticality:**      High. Only the leaf exception type is recorded, not the full chain.
                        A URLError wrapping a gaierror records only "URLError" — root cause
                        is lost.
- **Location:**         Server/syxx_server/investigate.py:122-127
- **Issue:**            `except Exception: "error": f"{type(e).__name__}: {e}"`. `type(e).
                        __name__` is only the leaf type. Should use `traceback.
                        format_exception_only()` or include `e.__cause__`.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Use traceback.format_exception_only(type(e), e).
                        [Fix 2]
                        Include e.__cause__ recursively.
- **Code Sample:**      [

                        # investigate.py:127
                        "error": f"{type(e).__name__}: {e}"
                        # Only records leaf type, not the full chain

                        ]

---

- **[A048]**            _deadline_timer comment claims "per-CONNECTION" but setup() runs per-
                        request via handle_one_request()
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09.
- **Criticality:**      Medium. Misleading comment could cause a future maintainer to adjust
                        logic incorrectly.
- **Location:**         Server/syxx_server/app.py:300-308
- **Issue:**            Comment says timer is "per-CONNECTION... not per-request as the name
                        suggests." But CPython's BaseHTTPRequestHandler.handle_one_request()
                        calls setup() at the start of each request cycle. The timer IS per-
                        request.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Correct comment to "per-REQUEST absolute wall-clock deadline."
- **Code Sample:**      [

                        # app.py:300 — incorrect comment
                        # A137: this is actually a per-CONNECTION deadline
                        # (setup runs once per connection, not per request)

                        ]

---

- **[A049]**            investigate.py hasattr(resolver, "TYPE_DNSKEY") fallback to 48 is
                        dead code — constant always exists
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09.
- **Criticality:**      Low. TYPE_DNSKEY=48 is defined at resolver.py:27. The hasattr always
                        returns True. The else branch is unreachable.
- **Location:**         Server/syxx_server/investigate.py:200
- **Issue:**            `resolver.TYPE_DNSKEY if hasattr(resolver, "TYPE_DNSKEY") else 48`.
                        The constant is statically known to exist.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Remove hasattr guard, reference resolver.TYPE_DNSKEY directly.
- **Code Sample:**      [

                        # investigate.py:200
                        resolver.TYPE_DNSKEY if hasattr(resolver, "TYPE_DNSKEY") else 48
                        # TYPE_DNSKEY = 48 is defined at resolver.py:27 — always exists

                        ]

---

- **[A050]**            _route_investigate records domain in OTel span BEFORE validation —
                        attacker-controlled garbage in span attribute
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09.
- **Criticality:**      Low. Minor telemetry pollution — 10KB of garbage domain is recorded
                        in span attribute before validation rejects it.
- **Location:**         Server/syxx_server/app.py:2017-2022
- **Issue:**            `span.set_attribute("investigate.domain", domain)` before
                        `investigate.investigate(domain)` validates the domain. Span attribute
                        survives even if investigate() returns an error.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Move span attribute set after successful validation.
                        [Fix 2]
                        Redact long/malformed values.
- **Code Sample:**      [

                        # app.py:2017-2022
                        span.set_attribute("investigate.domain", domain)  # before validation
                        result = investigate.investigate(domain)

                        ]

---

- **[A051]**            leak.py _ensure_collector_key raises RuntimeError at import time —
                        crashes /api/pubkey handler with HTTP 500
- **Status:**           **OPEN** — Found during deep delta-analysis correctness audit
                        2026-08-09.
- **Criticality:**      Low. On a misconfigured server, /api/pubkey returns 500 instead of
                        structured error. preflight.py has collector_key_error() that can
                        diagnose without raising.
- **Location:**         Server/syxx_server/leak.py:89-97
- **Issue:**            `_ensure_collector_key()` raises RuntimeError if key is unset. The
                        /api/pubkey handler calls this and the RuntimeError propagates to
                        HTTP 500.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add try/except RuntimeError in /api/pubkey handler to return
                        structured error instead of 500.
- **Code Sample:**      [

                        # leak.py:93
                        raise RuntimeError("SYXX_COLLECTOR_PRIVATE_KEY is not set...")
                        # preflight.py has collector_key_error() that returns string, not raise

                        ]

---

- **[A052]**            No ETag/If-None-Match support on server — full body retransmit every
                        request
- **Status:**           **OPEN** — Found during deep delta-analysis performance audit
                        2026-08-09.
- **Criticality:**      High. Every GET /api/startup, /api/broadcast, /healthz, and static
                        asset request returns full 200. Client polling /api/startup every 60s
                        receives full body 1,440 times/day.
- **Location:**         Server/syxx_server/app.py (all _send(), _serve_static(),
                        _serve_whats_new(), route handlers)
- **Issue:**            No ETag header set on any response. No If-None-Match check on any
                        request. Static assets have Cache-Control: max-age=86400 but no ETag
                        for 304 Not Modified.
- **Cross-ref. Delta:** A055 (no Cache-Control on API responses).
- **Fix(es):**          [Fix 1] Compute ETag from content hash for static assets and API
                        responses. [Fix 2] Check If-None-Match and return 304.
- **Code Sample:**      [

                        # app.py — no ETag anywhere
                        # Every request returns full 200 body

                        ]

---

- **[A053]**            N+1 sequential DNS queries in firewall_test — 120 DNS queries, all
                        sequential, handler pinned up to 60s
- **Status:**           **OPEN** — Found during deep delta-analysis performance audit
                        2026-08-09.
- **Criticality:**      High. 10 domains × (1 user resolver × 2 qtypes + 5 clean vantages
                        × 2 qtypes) = up to 120 DNS queries, sequential. _TEST_DEADLINE_
                        SECONDS=60 truncates but handler is pinned for up to 60s.
- **Location:**         Server/syxx_server/firewall_test.py:120, 170, 210, 220
- **Issue:**            No ThreadPoolExecutor parallelism. Every query blocks the handler
                        thread sequentially.
- **Cross-ref. Delta:** A033 (SD-44 — sync DNS walk blocks handler).
- **Fix(es):**          [Fix 1] Use ThreadPoolExecutor for parallel DNS queries.
                        [Fix 2]
                        Batch queries by resolver to reduce round-trips.
- **Code Sample:**      [

                        # firewall_test.py:210-220
                        for entry in test_domains:  # sequential
                            for vantage in _CLEAN_VANTAGES:  # sequential
                                _query_resolver()  # blocks

                        ]

---

- **[A054]**            N+1 sequential script fetches in warp.build_baseline — up to 50 scripts
                        fetched sequentially, 10–250s blocked handler
- **Status:**           **OPEN** — Found during deep delta-analysis performance audit
                        2026-08-09.
- **Criticality:**      High. FETCH_MAX_SCRIPTS=50. Each fetch: DNS + TCP/TLS + HTTP GET +
                        response decompression. 20 scripts at 500ms each = 10s blocked.
- **Location:**         Server/syxx_server/warp.py:120-150; Server/syxx_server/fetcher.py
- **Issue:**            All script fetches are sequential. DNS resolution + TCP/TLS handshake
                        + HTTP GET for each script, one at a time.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Use asyncio.gather() for parallel script fetches.
                        [Fix 2]
                        Add a concurrency limit (e.g., Semaphore(5)).
- **Code Sample:**      [

                        # warp.py:145-150
                        for script_url in scripts[:FETCH_MAX_SCRIPTS]:  # sequential
                            content = fetcher.fetch(script_url)  # blocks

                        ]

---

- **[A055]**            No Cache-Control on any API response — zero browser/CDN caching
- **Status:**           **OPEN** — Found during deep delta-analysis performance audit
                        2026-08-09.
- **Criticality:**      Medium. Only _serve_whats_new (max-age=300) and static assets
                        (max-age=86400) set cache headers. All JSON API responses are sent
                        without Cache-Control.
- **Location:**         Server/syxx_server/app.py:660, 2000, 2050, 2180, 2450, 2660
- **Issue:**            Every JSON API response lacks Cache-Control. Idempotent GET routes
                        (investigate, authoritative, baseline, lookup, whois) should have
                        Cache-Control: public, max-age=60.
- **Cross-ref. Delta:** A052 (no ETag support).
- **Fix(es):**          [Fix 1] Add Cache-Control: public, max-age=60 to idempotent GET
                        routes. [Fix 2] Add Cache-Control: no-store to mutation endpoints.
- **Code Sample:**      [

                        # app.py:660 — _send() for JSON responses, no Cache-Control

                        ]

---

- **[A056]**            Server OTel spans have no error status on exception paths — span ends
                        with OK/default status
- **Status:**           **OPEN** — Found during deep delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      High. The collector sees error-path spans as successful. Affects
                        server.authoritative and server.trace spans.
- **Location:**         Server/syxx_server/app.py:2051-2053, 2189-2191
- **Issue:**            `except resolver.DNSError: span.set_attribute("error", str(e));
                        span.end()` — no `span.set_status(StatusCode.ERROR)`. Span ends
                        with OK status.
- **Cross-ref. Delta:** A044 (missing spans on endpoints).
- **Fix(es):**          [Fix 1] Add span.set_status(StatusCode.ERROR) in exception handlers.
- **Code Sample:**      [

                        # app.py:2051-2053
                        except resolver.DNSError:
                            span.set_attribute("error", str(e))
                            span.end()  # NO set_status(ERROR)

                        ]

---

- **[A057]**            No health check for Sentry/OTel connectivity — server can be "healthy"
                        while exporting zero telemetry
- **Status:**           **OPEN** — Found during deep delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Medium. /healthz returns {"ok": true} without checking Sentry or
                        OTel exporter health. _OTEL_STATE variable exists but is unreachable
                        from monitoring.
- **Location:**         Server/syxx_server/app.py:1044-1045, 1546-1565
- **Issue:**            If OTel exporter crashes at import, _OTEL_STATE is set to "failed:
                        ..." but this is never surfaced in any health endpoint.
- **Cross-ref. Delta:** A045 (healthz no span).
- **Fix(es):**          [Fix 1] Include OTel and Sentry health in /readyz response.
- **Code Sample:**      [

                        # app.py:1044-1045
                        {"ok": true, "scope": "process-liveness-only"}
                        # No telemetry health check

                        ]

---

- **[A058]**            Server /metrics returns raw JSON dict, not Prometheus exposition format
- **Status:**           **OPEN** — Found during deep delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Low. Counters exist but are incompatible with standard Prometheus
                        scraping.
- **Location:**         Server/syxx_server/app.py:1580-1586
- **Issue:**            `/metrics` returns `{"metrics": logging_util.snapshot()}` — raw JSON
                        dict. Not Prometheus exposition format.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add Prometheus text format output.
                        [Fix 2] Or use OTel
                        metrics exporter.
- **Code Sample:**      [

                        # app.py:1580-1586
                        return web.Response(text=json.dumps(
                            {"metrics": logging_util.snapshot()}))

                        ]

---

- **[A059]**            Server OTLP log records lack trace context — log records disconnected
                        from active spans
- **Status:**           **OPEN** — Found during deep delta-analysis observability audit
                        2026-08-09.
- **Criticality:**      Low. A collector query cannot join a server error log to its request
                        span.
- **Location:**         Server/syxx_server/logging_util.py:77-85
- **Issue:**            `logging_util.error()` creates LogRecord with body and severity but
                        no trace_id or span_id. The log record is disconnected from any
                        active span.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Include trace_id and span_id from the current OTel context
                        in LogRecord.
- **Code Sample:**      [

                        # logging_util.py:77-85
                        _otel_logger.emit(rec_obj)  # no trace_id, no span_id

                        ]

---

- **[A060]**            Stripe SPKI pin list is loaded but never enforced on checkout
                        requests
- **Status:**           **OPEN** — Found during the completed five-agent audit
                        2026-08-09.
- **Criticality:**      Non-critical. The client still verifies CA trust and hostname
                        checks, so this is defense-in-depth that fails open to standard
                        TLS rather than a direct break in checkout.
- **Location:**         Server/syxx_server/stripe_checkout.py:45-50, 175
- **Issue:**            `_STRIPE_SPKI_PINS` is populated from `SYXX_STRIPE_PIN_SPKI`,
                        and `_verify_spki_pin()` exists, but the request path only passes
                        `_STRIPE_SSL_CONTEXT` into `urlopen()`. The pin check is never
                        called, so configured pins do nothing.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Call `_verify_spki_pin()` on the peer certificate after
                        the TLS handshake succeeds. [Fix 2] Fail closed when pins are
                        configured but no certificate matches.
- **Code Sample:**      [

                        # Server/syxx_server/stripe_checkout.py:45-50, 175
                        _STRIPE_SPKI_PINS = [
                            p.strip() for p in os.environ.get("SYXX_STRIPE_PIN_SPKI", "").split(",")
                            if p.strip()
                        ]
                        with urllib.request.urlopen(req, timeout=15, context=_STRIPE_SSL_CONTEXT) as r:
                            return json.loads(r.read())

                        ]

---

- **[A061]**            Keygen revocation webhook is replayable because it checks HMAC only
                        and stores no nonce / timestamp fence
- **Status:**           **OPEN** — Found during the completed five-agent audit
                        2026-08-09.
- **Criticality:**      Non-critical. A replay can re-run the same revocation action, but
                        the handler is already idempotent on the target rows; the defect is
                        replay hygiene, not immediate privilege escalation.
- **Location:**         Server/syxx_server/app.py:1407-1452
- **Issue:**            The webhook validates `Keygen-Signature` with HMAC-SHA256 and then
                        immediately processes the JSON body. There is no timestamp, nonce, or
                        event-id replay fence, so a captured signed payload can be resent
                        later and processed again.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Require a signed timestamp and reject stale deliveries.
                        [Fix 2] Store and deduplicate webhook event IDs. [Fix 3] Treat
                        duplicates as no-ops before revocation logic runs.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:1419-1452
                        sig_header = self.headers.get("Keygen-Signature", "")
                        expected = hmac.new(config.KEYGEN_WEBHOOK_SECRET.encode("utf-8"),
                            raw or b"", hashlib.sha256).hexdigest()
                        sig_valid = hmac.compare_digest(expected, sig_header)
                        event = json.loads(raw or b"{}")
                        if event_type in ("license.revoked", "license.suspended", "license.expired"):
                            local_subject = db.keygen_subject_for_license_id(lic_id)

                        ]

---

- **[E017]**            Hardcoded P-256 Keygen public key in macOS and Windows clients — no
                        key rotation path; server uses env var
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09. Cross-file trace: LicenseManager.swift:74 →
                        LicenseManager.cs:508.
- **Criticality:**      Critical. If Keygen rotates their signing key, ALL clients are
                        permanently locked out of Pro features. Server can rotate via env var;
                        clients cannot. Key embedded in binary at fixed offset.
- **Location:**         macOS/Sources/SyxxKit/LicenseManager.swift:74;
                        Windows/dotNET/Syxx.Kit/LicenseManager.cs:508
- **Issue:**            Keygen response verification public key is compiled into both client
                        binaries as a hardcoded P-256 DER hex string. The server reads
                        KEYGEN_PUBLIC_KEY from environment — rotatable. An attacker who
                        extracts the key from one binary can forge valid responses for any
                        client of that build.
- **Cross-ref. Delta:** E018 (algorithm mismatch).
- **Fix(es):**          [Fix 1] Deliver public key via authenticated /api/startup response.
                        [Fix 2] Cache locally with TTL. [Fix 3] Fall back to compiled-in key
                        only when server is unreachable.
- **Code Sample:**      [

                        // LicenseManager.swift:74 — hardcoded in binary
                        private static let keygenPublicKeyDERHex =
                            "3059301306072a8648ce3d020106082a8648ce3d03010703420004..."

                        ]

---

- **[E018]**            Key algorithm mismatch — server docstring says Ed25519, clients use
                        P-256
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      Critical. Server and clients verify against DIFFERENT algorithms.
                        Deployment-time footgun: operator must configure same algorithm on
                        both or verification fails silently.
- **Location:**         Server/syxx_server/licensing.py:61-63 (Ed25519);
                        macOS/Sources/SyxxKit/LicenseManager.swift:74 (P-256);
                        Windows/dotNET/Syxx.Kit/LicenseManager.cs:508 (P-256)
- **Issue:**            Server imports Ed25519PublicKey. Clients use P256/P-256. Keygen.sh
                        supports both but the operator must configure the SAME algorithm.
                        Currently documented inconsistently.
- **Cross-ref. Delta:** E017 (hardcoded key).
- **Fix(es):**          [Fix 1] Pick one algorithm and use consistently.
                        [Fix 2] Document
                        in deployment docs.
- **Code Sample:**      [

                        # Server licensing.py:61-63 — Ed25519
                        # macOS LicenseManager.swift:684 — P-256
                        # Windows LicenseManager.cs:556 — P-256 (ECDsa)

                        ]

---

- **[E019]**            CSP on main site leaks user IPs to Google Fonts — privacy violation
                        for DNS investigation product
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      Critical. Every page load sends user IP, Referer, and User-Agent to
                        Google. The whats-new pages already use tighter CSP — main site was
                        never updated.
- **Location:**         Server/syxx_server/app.py:628-638
- **Issue:**            CSP allows font-src fonts.gstatic.com and style-src fonts.googleapis.
                        com. This is the exact surveillance vector Syxx is designed to detect.
- **Cross-ref. Delta:** E020 (script-src 'self').
- **Fix(es):**          [Fix 1] Self-host fonts.
                        [Fix 2] Use font-src data: matching the
                        whats-new page CSP.
- **Code Sample:**      [

                        # app.py:628-638 — main site CSP (leaks to Google)
                        "font-src 'self' https://fonts.gstatic.com data:;"
                        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com;"
                        # app.py:1209 — whats-new CSP (correct, no Google)
                        "font-src data:; style-src 'unsafe-inline';"

                        ]

---

- **[E020]**            CSP script-src 'self' allows any same-origin script — bypasses nonce
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      Critical. If attacker places .js file in static directory (path
                        traversal, upload, compromised deploy), script executes with full
                        origin authority. Nonce only protects inline scripts.
- **Location:**         Server/syxx_server/app.py:628-638
- **Issue:**            CSP script-src includes 'self' which allows any same-origin script.
                        The nonce is redundant because 'self' already permits everything.
- **Cross-ref. Delta:** E019 (Google Fonts CSP).
- **Fix(es):**          [Fix 1] Replace with script-src 'nonce-{nonce}' (no 'self').
                        [Fix 2]
                        Use strict-dynamic.
- **Code Sample:**      [

                        "script-src 'self' 'nonce-{nonce}' https://js.stripe.com;"
                        // 'self' allows ANY same-origin script — nonce is bypassed

                        ]

---

- **[E021]**            Session cookie syxx_session lacks Secure flag
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      High. HSTS mitigates for returning visitors, but first visit or
                        misconfiguration exposes session token.
- **Location:**         Server/syxx_server/app.py (cookie setting)
- **Issue:**            syxx_session cookie documented as httpOnly but no Secure flag observed.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add Secure flag.
                        [Fix 2] Add __Host- prefix.
- **Code Sample:**      [

                        // No Secure flag on syxx_session cookie

                        ]

---

- **[E022]**            No rate limiting on /api/auth/activate — session token brute-force
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      High. Attacker can submit thousands of invalid keys per second.
- **Location:**         Server/syxx_server/app.py (activation endpoint)
- **Issue:**            Keygen API semaphore-limited (4 concurrent) but endpoint has no per-IP
                        rate limiting. ratelimit.py may not cover activation.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add per-IP rate limiting.
                        [Fix 2] Add exponential backoff.
- **Code Sample:**      [

                        // No rate limiting on /api/auth/activate

                        ]

---

- **[E023]**            Trial fingerprint bypass on VMs/containers — documented, enforcement
                        needs verification
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      High. InstallScoped fingerprints allow unlimited trial resets. Server
                        MUST refuse them per config.py docs — enforcement needs verification.
- **Location:**         Server/syxx_server/config.py:370-375
- **Issue:**            "The issuing endpoint must therefore treat a non-node-lockable
                        fingerprint as a REFUSAL, not a warning." Actual enforcement unverified.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Verify trial anchor endpoint refuses InstallScoped fingerprints.
                        [Fix 2] Add test.
- **Code Sample:**      [

                        # config.py:370-375 — policy documented, enforcement TBD

                        ]

---

- **[E024]**            Leak tokens stored as plaintext in SQLite — no hashing
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      High. DB compromise exposes all tokens. Tokens also appear in DNS
                        qnames at every recursive resolver.
- **Location:**         Server/syxx_server/db.py:715-760; leak.py:185
- **Issue:**            Tokens are 16-byte random, base32-encoded, stored as plaintext.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Hash tokens with SHA-256 before storage.
- **Code Sample:**      [

                        # leak.py:185 — token = base64.b32encode(secrets.token_bytes(16))
                        # db.py:715 — stored as plaintext TEXT PRIMARY KEY

                        ]

---

- **[E025]**            SQLite WAL/SHM sidecar permissions may drift from 0600 to umask default
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      High. chmod only at init. SQLite recreates WAL/SHM after checkpoint;
                        new files inherit umask (typically 0644).
- **Location:**         Server/syxx_server/db.py:311-330
- **Issue:**            No periodic re-chmod or umask enforcement.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Set process umask to 0077.
                        [Fix 2] Periodic chmod.
                        [Fix 3]
                        Verify in /readyz.
- **Code Sample:**      [

                        # db.py:311-330 — chmod at init only
                        os.chmod(config.DB_PATH + "-wal", 0o600)

                        ]

---

- **[E026]**            DNS authoritative server binds to 0.0.0.0:53 by default
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      High. Rate limiting mitigates amplification but broader exposure
                        increases attack surface. HTTP server defaults to 127.0.0.1 (safer).
- **Location:**         Server/syxx_server/config.py:393
- **Issue:**            DNS_HOST defaults to "0.0.0.0". Production should use specific IP.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Change default to specific IP or require explicit config.
- **Code Sample:**      [

                        DNS_HOST = _s("SYXX_DNS_HOST", "0.0.0.0")

                        ]

---

- **[E027]**            Missing Sec-Fetch-* header validation for cross-origin request
                        protection
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      High. CSRF attacks against activation/checkout endpoints possible
                        without Sec-Fetch-Site/Mode/Dest validation.
- **Location:**         Server/syxx_server/app.py (request handling)
- **Issue:**            No Sec-Fetch-* header validation anywhere.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Validate Sec-Fetch-Site for mutation endpoints.
                        [Fix 2]
                        Reject cross-site requests to /api/auth/activate and /api/checkout.
- **Code Sample:**      [

                        // No Sec-Fetch-* header validation

                        ]

---

- **[E028]**            random.shuffle in firewall_test uses non-cryptographic Mersenne Twister
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      Medium. Domain order could be predicted, enabling traffic
                        fingerprinting. Use secrets.SystemRandom().shuffle().
- **Location:**         Server/syxx_server/firewall_test.py:233
- **Issue:**            `random.shuffle(test_domains)` — Mersenne Twister, not CSPRNG.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Replace with secrets.SystemRandom().shuffle().
- **Code Sample:**      [

                        random.shuffle(test_domains)  # Mersenne Twister

                        ]

---

- **[E029]**            Witness key stored as plain base64 in Keychain — no additional wrapping
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      Medium. Keychain encrypts at rest but key material not additionally
                        wrapped. Defense-in-depth concern.
- **Location:**         macOS/Sources/SyxxKit/WitnessKeyManager.swift:100-120
- **Issue:**            Ed25519 private key stored as base64 string without additional
                        wrapping.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Wrap key with device-specific key before Keychain storage.
- **Code Sample:**      [

                        // Key stored as base64-encoded string in Keychain

                        ]

---

- **[E030]**            License key Keychain fallback to password-only when biometric
                        unavailable
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      Medium. Falls back from .biometryAny to .whenUnlockedThisDeviceOnly.
                        Reasonable tradeoff but worth noting.
- **Location:**         macOS/Sources/SyxxKit/LicenseManager.swift:130-180
- **Issue:**            When biometric unavailable, any process can read license key while
                        device is unlocked.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Document tradeoff.
                        [Fix 2] Consider requiring biometric for
                        Pro.
- **Code Sample:**      [

                        // if biometricContext() != nil: .biometryAny
                        // else: .whenUnlockedThisDeviceOnly

                        ]

---

- **[E031]**            SQLite database has no encryption at rest beyond filesystem permissions
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      Medium. Session secrets, license keys, PII, leak tokens all plaintext.
                        Attacker with disk access can read all data.
- **Location:**         Server/syxx_server/db.py:311-330
- **Issue:**            Only protection is 0600 filesystem permissions. SQLite has no built-in
                        encryption.
- **Cross-ref. Delta:** E024, E025.
- **Fix(es):**          [Fix 1] Use SQLCipher or SEE.
                        [Fix 2] Encrypt sensitive columns at
                        application level.
- **Code Sample:**      [

                        # 0600 permissions only — no encryption at rest

                        ]

---

- **[E032]**            Server error messages may leak internal paths in debug scenarios
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      Medium. A105 policy documented but enforcement may be incomplete.
- **Location:**         Server/syxx_server/app.py:1556
- **Issue:**            "A105: never echo the raw exception." Policy may not be uniform
                        across all endpoints.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Audit all endpoints.
                        [Fix 2] Centralize error sanitization.
- **Code Sample:**      [

                        # A105: never echo the raw exception
                        # (it carries the sqlite path / driver detail)

                        ]

---

- **[E033]**            No Subresource Integrity hashes for whats-new HTML files
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      Low. Tight CSP limits damage but modified files rendered without
                        integrity verification.
- **Location:**         Server/syxx_server/app.py:1166-1210
- **Issue:**            Whats-new HTML served from filesystem without integrity hashes.
- **Cross-ref. Delta:** A052 (no ETag).
- **Fix(es):**          [Fix 1] Add Content-Digest header based on file hash.
- **Code Sample:**      [

                        // _serve_whats_new() — no integrity hash

                        ]

---

- **[E034]**            security.txt may lack Expires field — RFC 9116 compliance
- **Status:**           **OPEN** — Found during deep delta-analysis security audit
                        2026-08-09.
- **Criticality:**      Low. RFC 9116 recommends expiration to ensure contact info is current.
- **Location:**         Server/syxx_server/app.py:163
- **Issue:**            .well-known/security.txt served but no Expires field observed.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add Expires field.
- **Code Sample:**      [

                        // .well-known/security.txt — no Expires field

                        ]

---

- **[E035]**            Session signing secret stored as plaintext in SQLite kv table — DB
                        compromise = full paywall bypass
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      High. Attacker with DB file access can forge valid session tokens
                        for any subject, bypassing the entire paywall.
- **Location:**         Server/syxx_server/licensing.py:90-99; db.py:get_or_create_secret()
- **Issue:**            HMAC-SHA256 session signing secret stored as plaintext row in the
                        `kv` table. DB compromise via backup leakage, misconfigured
                        permissions, or server compromise → all paid API endpoints accessible
                        without license.
- **Cross-ref. Delta:** E031 (SQLite encryption at rest).
- **Fix(es):**          [Fix 1] Encrypt session secret at rest with key derived from server-
                        local secret. [Fix 2] Rotate on restart if encryption key unavailable.
- **Code Sample:**      [

                        # licensing.py:90-99 — session secret generation
                        # Stored as plaintext in kv table

                        ]

---

- **[E036]**            Custom ASN.1 DER parser in SPKI pin extraction is error-prone —
                        potential outage or verification bypass
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Medium. Multi-byte length encoding, constructed bit strings, or
                        padding edge cases could cause mismatch or bypass.
- **Location:**         Server/syxx_server/stripe_checkout.py:60-96
- **Issue:**            `_extract_spki_sha256_b64` implements minimal ASN.1 DER parser for
                        X.509 SPKI extraction. Custom DER parsing is error-prone. A parser
                        bug could produce a false match for attacker certificate or reject
                        legitimate Stripe connections.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Use cryptography.x509 instead of custom DER parsing. The
                        `cryptography` library is already a dependency via leak.py.
- **Code Sample:**      [

                        # stripe_checkout.py:60-96 — custom DER parser
                        def _extract_spki_sha256_b64(der):
                            # Manual ASN.1 DER parsing — error-prone

                        ]

---

- **[E037]**            Unauthenticated trial anchor endpoint accepts arbitrary POST requests
                        — no rate limiting, fingerprint validation, or auth
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Medium. Attacker can flood endpoint with arbitrary fingerprints,
                        poison trial DB, or probe for valid fingerprints via timing.
- **Location:**         Server/syxx_server/app.py (/api/trial/anchor route)
- **Issue:**            Endpoint accepts unauthenticated POSTs with arbitrary fingerprint
                        values. No rate limiting, no fingerprint format validation, no auth.
                        Client correctly handles unauthenticated response (B190), but server
                        endpoint itself is unprotected.
- **Cross-ref. Delta:** E023 (trial fingerprint bypass).
- **Fix(es):**          [Fix 1] Add rate limiting specific to trial anchor.
                        [Fix 2] Validate
                        fingerprint format. [Fix 3] Require minimal proof-of-work.
- **Code Sample:**      [

                        # /api/trial/anchor — unauthenticated, no rate limit

                        ]

---

- **[E038]**            Email auth DNS queries (SPF/DMARC/DKIM) not DNSSEC-validated — MITM
                        can forge email security posture
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Medium. MITM on DNS path could strip SPF/DMARC records or inject
                        permissive records, misrepresenting domain's email security.
- **Location:**         Server/syxx_server/email_auth.py:30-90
- **Issue:**            _check_spf, _check_dmarc, _check_dkim query DNS TXT records without
                        DNSSEC validation. Results displayed to users as email security
                        assessments.
- **Cross-ref. Delta:** A013 (DNSSEC IDN corruption).
- **Fix(es):**          [Fix 1] Set DO bit on DNS queries.
                        [Fix 2] Validate DNSSEC signatures
                        for TXT responses. [Fix 3] Mark as "unverified" when DNSSEC unavailable.
- **Code Sample:**      [

                        # email_auth.py:30-90 — TXT queries without DNSSEC

                        ]

---

- **[E039]**            ThreadingHTTPServer lacks production security features — no request
                        size limits, header limits, keep-alive limits
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Medium. Python's http.server documented as "not recommended for
                        production." No request size limits, header count limits, or keep-alive
                        limits. Relies on reverse proxy for TLS termination.
- **Location:**         Server/syxx_server/app.py (ThreadingHTTPServer usage)
- **Issue:**            _MAX_WORKERS=64 and _REQUEST_DEADLINE=60 help but underlying server
                        not designed for adversarial traffic.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Consider gunicorn/uvicorn behind reverse proxy.
                        [Fix 2]
                        Document reverse proxy as hard requirement in production.
- **Code Sample:**      [

                        # app.py — ThreadingHTTPServer in production

                        ]

---

- **[E040]**            Witness signing key exposed via SYXX_WITNESS_KEY env var — visible to
                        all processes from same shell
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Medium. If set in shell profile, every spawned process can read the
                        Ed25519 private key. Attacker can forge witness attestations.
- **Location:**         macOS/Sources/SyxxCLI/Commands/SyxxWitness.swift:176
- **Issue:**            `ProcessInfo.processInfo.environment["SYXX_WITNESS_KEY"]` reads key
                        from environment. proc_pidinfo and sysctl can expose env vars. If set
                        in .zshrc/.bashrc, visible to all processes.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Prefer --passphrase-file (0600) or interactive prompt.
                        [Fix 2] Clear env var after reading: setenv("SYXX_WITNESS_KEY", "", 1).
                        [Fix 3] Document exposure in help text.
- **Code Sample:**      [

                        // SyxxWitness.swift:176 — reads key from env
                        ProcessInfo.processInfo.environment["SYXX_WITNESS_KEY"]

                        ]

---

- **[E041]**            Hardcoded OTLP telemetry endpoint in macOS binary — redirect risk if
                        domain expires or compromised
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Low. bugsmasher.syxx.app hardcoded. If domain expires or compromised,
                        telemetry redirects silently.
- **Location:**         macOS/Sources/SyxxTelemetry/OTelBootstrap.swift:14
- **Issue:**            `https://bugsmasher.syxx.app/v1/traces` hardcoded. Attacker who
                        controls domain could receive OTel traces (hostnames/IPs redacted by
                        SentryDiagnosticSink).
- **Cross-ref. Delta:** E055 (server OTel endpoint same pattern).
- **Fix(es):**          [Fix 1] Make configurable via env var with current value as default.
- **Code Sample:**      [

                        // OTelBootstrap.swift:14 — hardcoded endpoint

                        ]

---

- **[E042]**            Unicode homograph attacks possible in domain validation regex — mixed-
                        script confusable characters accepted
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Low. Cyrillic 'а' in "раypal.com" passes regex. Punycode conversion
                        produces different domain but visual similarity misleads users.
- **Location:**         Server/syxx_server/investigate.py:25-28
- **Issue:**            _HOSTNAME regex accepts Unicode labels (\u00C0-\uFFFF). IDN support
                        intentional (SM-001) but homograph attacks possible.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add confusable character detection using Unicode confusables.
                        txt. [Fix 2] Warn on mixed-script characters.
- **Code Sample:**      [

                        # investigate.py:25-28 — _HOSTNAME regex
                        # Accepts \u00C0-\uFFFF — homograph risk

                        ]

---

- **[E043]**            DPAPI legacy entropy constants are static across all installations —
                        no machine-specific binding
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Low. DPAPI blob from one machine could be decrypted on another if
                        user credential matches (domain-joined machines).
- **Location:**         Windows/dotNET/Syxx.Kit/DpapiSecretStore.cs:220-225
- **Issue:**            LegacyLicenseEntropy and LegacyWitnessEntropy are hardcoded
                        "com.milkandgin.syxx"u8.ToArray(). Static entropy provides no
                        additional protection beyond DPAPI.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Derive from machine-specific secret.
                        [Fix 2] Remove legacy
                        path after migration window.
- **Code Sample:**      [

                        // DpapiSecretStore.cs:220-225 — static entropy
                        "com.milkandgin.syxx"u8.ToArray()

                        ]

---

- **[E044]**            In-memory rate limit buckets lost on server restart — burst attack
                        window
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Low. All buckets cleared on restart. Coordinated restart+burst could
                        exhaust per-subject capacity.
- **Location:**         Server/syxx_server/ratelimit.py:22-23
- **Issue:**            _buckets dict is module-level, in-memory only. _MAX_BUCKETS=10,000 cap.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Persist state to DB with short TTL.
                        [Fix 2] Add startup grace
                        period with stricter limits.
- **Code Sample:**      [

                        # ratelimit.py:22-23 — in-memory only
                        _buckets: dict = {}

                        ]

---

- **[E045]**            Session token TTL bound to server wall clock — no clock-skew tolerance
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Low. NTP failure or VM migration could extend or shorten token
                        validity unexpectedly.
- **Location:**         Server/syxx_server/licensing.py:94
- **Issue:**            mint_session uses int(time.time()) for expiration. KEYGEN_DATE_SKEW_
                        SECONDS=300 shows awareness of clock skew, but no protection for
                        session tokens.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Use time.monotonic() for relative TTL.
                        [Fix 2] Document skew
                        tolerance.
- **Code Sample:**      [

                        # licensing.py:94 — wall clock for expiration
                        exp = int(time.time()) + SESSION_TTL

                        ]

---

- **[E046]**            Settings file permissions not verified on load — overly permissive
                        files silently accepted
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Low. save() sets 0o600 but load() doesn't verify. Misconfigured
                        .syxx/ directory could expose settings to other local users.
- **Location:**         macOS/Sources/SyxxKit/SyxxSettings.swift:176-177, 143-152
- **Issue:**            save() chmods to 0o600; load() reads without permission check.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Verify file permissions in load().
                        [Fix 2] Reject files with
                        overly permissive modes; log warning and fall back to defaults.
- **Code Sample:**      [

                        // SyxxSettings.swift:176-177 — save sets 0o600
                        // SyxxSettings.swift:143-152 — load doesn't verify

                        ]

---

- **[E047]**            Keystore passphrase-derived key held in memory for 15-minute idle
                        window — memory dump exposure
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Low. Argon2id-derived 32-byte key in memory for 15 min after last
                        use. Memory dump or swap would expose key.
- **Location:**         Windows/dotNET/Syxx.Kit/LinuxSecretStore.cs (KeystorePassphraseSession)
- **Issue:**            IdleTimeout=15min. Key zeroed on expiry but accessible during window.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Reduce idle timeout to 5 minutes.
                        [Fix 2] Use ProtectedMemory
                        (Windows) or mlock (Linux) to prevent swap.
- **Code Sample:**      [

                        // KeystorePassphraseSession — IdleTimeout = 15 min

                        ]

---

- **[E048]**            WKContentRuleList compilation races with first navigation — brief
                        window without content blocking
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Low. HTML may load before rule list compiled, allowing external
                        resource fetch on first load.
- **Location:**         macOS/Sources/SyxxApp/RemoteContentView.swift:207-224
- **Issue:**            compileContentRuleList is async; loadContent called immediately
                        without waiting for callback.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Defer loadContent until rule list compilation callback fires.
- **Code Sample:**      [

                        // RemoteContentView.swift:207-224 — async compilation
                        // loadContent called before compilation completes

                        ]

---

- **[E049]**            Captive portal detection uses unencrypted HTTP to gateway IP — MITM
                        on local network possible
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Low. ARP spoofing attacker could impersonate gateway. Response
                        rendered as raw text (not HTML), limiting XSS risk.
- **Location:**         macOS/Sources/SyxxKit/NetworkKeyState.swift:804
- **Issue:**            `http://\(gwIP)/` constructs unencrypted HTTP URL to gateway. Gateway
                        IP from system routing table, not user input.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add comment documenting deliberate HTTP for reachability.
                        [Fix 2] Add timeout and response size limit.
- **Code Sample:**      [

                        // NetworkKeyState.swift:804 — HTTP to gateway
                        "http://\(gwIP)/"

                        ]

---

- **[E050]**            Hardcoded Stripe product IDs and price IDs in server config — reveals
                        product catalog structure
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Low. Public identifiers but reveal exact catalog and pricing tiers.
                        Env var overrides exist but defaults are hardcoded.
- **Location:**         Server/syxx_server/config.py (STRIPE_SKUS table)
- **Issue:**            prod_Uq652gww87MGYT etc. hardcoded. Env var overrides (SYXX_STRIPE_
                        PRODID_*, SYXX_STRIPE_PRICEID_*) exist but defaults are in source.
- **Cross-ref. Delta:** A029 (config.py mixes product catalog).
- **Fix(es):**          [Fix 1] Make env vars mandatory for production.
                        [Fix 2] Remove
                        hardcoded defaults.
- **Code Sample:**      [

                        # config.py — STRIPE_SKUS with hardcoded product IDs

                        ]

---

- **[E051]**            Server OTel export endpoint hardcoded — same as E041 but for server
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Low. bugsmasher.syxx.app hardcoded in app.py:109.
- **Location:**         Server/syxx_server/app.py:109
- **Issue:**            _endpoint = "https://bugsmasher.syxx.app" hardcoded. Same risk as
                        E041 (macOS).
- **Cross-ref. Delta:** E041 (macOS OTel endpoint).
- **Fix(es):**          [Fix 1] Make configurable via env var.
- **Code Sample:**      [

                        # app.py:109 — hardcoded OTel endpoint

                        ]

---

- **[E052]**            CLI re-spawns self under sudo using CommandLine.arguments[0] — symlink
                        attack risk
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Low. Attacker controlling argv[0] via symlink or exec -a could execute
                        different binary with sudo. Sudo requires authentication.
- **Location:**         macOS/Sources/SyxxCLI/SyxxCLI.swift:882-886
- **Issue:**            CommandLine.arguments[0] used to re-spawn under sudo. Not resolved
                        through realpath().
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Use Bundle.main.executablePath or resolve through realpath().
- **Code Sample:**      [

                        // SyxxCLI.swift:882-886 — re-spawn under sudo
                        CommandLine.arguments[0]  // not resolved

                        ]

---

- **[E053]**            Server query parameters logged verbatim — domain names in telemetry
                        reveal user browsing history
- **Status:**           **OPEN** — Found during deepest delta-analysis security scan
                        2026-08-09.
- **Criticality:**      Low. Domains submitted to /api/investigate, /api/diff, /warp logged
                        without redaction in Sentry/OTel pipeline.
- **Location:**         Server/syxx_server/app.py (route handler logging calls)
- **Issue:**            logging_util.error/warn pass domain names verbatim. Hostnames/IPs
                        redacted in some paths but domain names are not.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Redact query domain names in telemetry logs.
                        [Fix 2] Hash or
                        truncate domain names. [Fix 3] Add redact_domain() helper.
- **Code Sample:**      [

                        # app.py — logging domain names verbatim
                        logging_util.error("handler_exception", route=path, ...)

                        ]

---

- **[A062]**            HTML injection via _SEO_PRICE_* env vars — operator-controlled values
                        injected unescaped into served HTML
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Medium. CSP blocks inline script execution but arbitrary HTML
                        injection (event handlers, iframes, form phishing) remains possible.
- **Location:**         Server/syxx_server/app.py:1505-1506
- **Issue:**            `_SEO_PRICE_MONTHLY` and `_SEO_PRICE_YEARLY` read from env, injected
                        into every served HTML page via str.replace() with no HTML escaping.
                        A value like `3.95<script>...</script>` is injected verbatim.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] HTML-escape env var values before injection.
- **Code Sample:**      [

                        # app.py:1505-1506
                        html = html.replace("$3.95", f"${_SEO_PRICE_MONTHLY}")
                        html = html.replace("$19.95", f"${_SEO_PRICE_YEARLY}")

                        ]

---

- **[A063]**            Keygen webhook has no rate limiting — resource exhaustion vector
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Medium. Attacker can flood webhook with forged payloads, consuming
                        CPU for HMAC verification and JSON parsing on every request.
- **Location:**         Server/syxx_server/app.py:1407
- **Issue:**            `_route_keygen_webhook` has no `ratelimit.allow()` call. Stripe webhook
                        at app.py:1385 applies rate limiting; Keygen webhook skips it.
- **Cross-ref. Delta:** E022 (activate rate limiting).
- **Fix(es):**          [Fix 1] Add ratelimit.allow() before HMAC verification.
- **Code Sample:**      [

                        # app.py:1407 — no ratelimit.allow()

                        ]

---

- **[A064]**            _route_warp and _route_warp_raw missing URL length validation
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. A very long URL (10KB query string) passes unchecked.
- **Location:**         Server/syxx_server/app.py:1781, 1809
- **Issue:**            `?url=` parameter accepted with no length validation. Only .strip()
                        applied before passing to fetcher.fetch().
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add URL length bound (e.g., 2048 chars).
- **Code Sample:**      [

                        # app.py:1781
                        target = (query.get("url", [""])[0]).strip()
                        # No length check

                        ]

---

- **[A065]**            _route_benchmark missing domain length validation (253 char check)
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. Inconsistent with every other domain-accepting route.
- **Location:**         Server/syxx_server/app.py:2521-2522
- **Issue:**            Checks `investigate.valid_domain(domain)` but omits `len(domain) >
                        253` check present in all other domain routes.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add len(domain) > 253 check.
- **Code Sample:**      [

                        # app.py:2521-2522 — no len check

                        ]

---

- **[A066]**            _route_investigate missing domain validation at route level
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. Downstream investigate.investigate() validates internally but
                        error message differs. Defense-in-depth and consistency gap.
- **Location:**         Server/syxx_server/app.py:2016-2017
- **Issue:**            Paid /api/investigate strips domain but performs no valid_domain() or
                        length check at handler level.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add valid_domain() and length check at route level.
- **Code Sample:**      [

                        # app.py:2016-2017 — no validation at route level

                        ]

---

- **[A067]**            _route_leak_verify and _route_leaktest_check missing token length
                        validation
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. Very long token strings traverse handler, string ops, and DB
                        query path without upper bound.
- **Location:**         Server/syxx_server/app.py:2361, 2405
- **Issue:**            `?token=` parameter accepted with no length validation. All other
                        parameterized routes enforce length limits.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add token length bound (e.g., 128 chars).
- **Code Sample:**      [

                        # app.py:2361, 2405 — no token length check

                        ]

---

- **[A068]**            _route_activate missing license key upper length bound
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. Very long key (up to 1MB body cap) is constructed into HTTP POST
                        to Keygen API. _body() enforces 1MB cap but no domain-specific bound.
- **Location:**         Server/syxx_server/app.py:1633
- **Issue:**            Only checks `len(key) < 8` (rejecting short keys). No upper bound.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add upper length bound (e.g., 256 chars).
- **Code Sample:**      [

                        # app.py:1633 — only checks lower bound
                        if not key: ...  # len(key) < 8 check only

                        ]

---

- **[B036]**            Denial NotADenial maps to Negative in C# but skipped in Swift —
                        fabricated clean datapoint
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      High. C# adds a Negative sub-status (clean datapoint) when the denial
                        check doesn't apply. Swift skips it entirely ("absence of applicability
                        is not cleanliness"). The C# B195 comment incorrectly claims macOS
                        maps to .negative.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:304-306;
                        macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:230-232
- **Issue:**            C# `case DenialStatus.NotADenial: subs.Add(SectionStatus.Negative)`.
                        Swift `case .notADenial: break` — emits no sub-status.
- **Cross-ref. Delta:** B024 (ExfilSuspicious same class of defect).
- **Fix(es):**          [Fix 1] Change C# to `break` (skip), matching Swift B061.
- **Code Sample:**      [

                        // C#: subs.Add(SectionStatus.Negative) — fabricates clean datapoint
                        // Swift: break — no sub-status emitted

                        ]

---

- **[B037]**            Latency Unreachable maps to Invalidated in C# but Degraded in Swift —
                        blocks clean on C# incorrectly
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      High. C# produces Invalidated (blocks clean, "premise failed").
                        Swift produces Degraded (quality caveat, does not block clean). Same
                        input, different verdict-blocking behavior.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:213-214;
                        macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:165
- **Issue:**            C# `case ResolverLatencyStatus.Unreachable: subs.Add(SectionStatus.
                        Invalidated)`. Swift `case .slow, .degraded, .unreachable: subs.
                        append(.degraded)`.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Move Unreachable into Degraded group, matching Swift.
- **Code Sample:**      [

                        // C#: Unreachable → Invalidated (blocks clean)
                        // Swift: unreachable → Degraded (quality caveat only)

                        ]

---

- **[B038]**            AnswerStabilityStatus.Rotating defined in C# enum but not handled in
                        Synthesizer switch — silent datapoint drop
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Medium. Rotating answers (benign round-robin) silently dropped on C#.
                        Swift maps to .negative (clean). No default arm in C# switch.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:229-255
- **Issue:**            `AnswerStabilityStatus.Rotating` exists in enum but has no case in
                        Synthesizer switch. No `default:` arm. Swift maps `.rotating` →
                        `.negative`.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add Rotating case alongside Stable → Negative.
                        [Fix 2] Add
                        default arm that logs unhandled values.
- **Code Sample:**      [

                        // C# switch has no Rotating case, no default arm
                        // Swift: case .stable, .rotating: subs.append(.negative)

                        ]

---

- **[B039]**            Glutton Inconclusive without signature maps to Invalidated in C# but
                        Untested in Swift
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Medium. C# says "ran but premise failed" (Invalidated). Swift says
                        "probe never executed" (Untested). Both block clean but semantics
                        differ and matrix engine may apply different rules.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:763;
                        macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:596
- **Issue:**            C# `subs.Add(leak.Signature is null ? SectionStatus.Invalidated :
                        SectionStatus.Inconclusive)`. Swift B164: `.untested` for no-signature.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Change C# to SectionStatus.Untested for null-signature path.
- **Code Sample:**      [

                        // C#: Invalidated (wrong — implies test ran and found corrupt data)
                        // Swift B164: Untested (correct — probe never had collector)

                        ]

---

- **[B040]**            ECS leak text in C# overclaims — B138 correction not ported
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. C# claims "ECS exposes a /24 of your address" but the probe
                        sends a synthetic test subnet, not the user's real prefix. Swift
                        B138 correctly reports as capability check.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:781-783
- **Issue:**            C# ECS evidence item text overclaims. Swift B138 honestly reports
                        synthetic test subnet.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Replace C# text with B138-corrected Swift text.
- **Code Sample:**      [

                        // C#: "ECS exposes a /24 of your address" — overclaims
                        // Swift B138: "synthetic test subnet" — honest

                        ]

---

- **[B041]**            Server-confirmed authoritative divergence text overclaims in C# —
                        B092 correction not ported
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. C# says "near-proof of upstream rewrite" but Dimension response
                        arrives over unsigned TLS. Swift B092 correctly says "corroborated
                        but unsigned, should be independently verified."
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:488-493
- **Issue:**            C# overclaims cryptographic certainty. Swift B092 honestly reports
                        unsigned TLS transport.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Replace C# text with B092-corrected Swift text.
- **Code Sample:**      [

                        // C#: "near-proof of an upstream rewrite" — overclaims
                        // Swift B092: "corroborates ... but unsigned" — honest

                        ]

---

- **[B042]**            Portal B176(c) assertion guard missing from C# — silent evidence drop
                        undetectable
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. Swift has debug assertion to catch evidence-emitting branches
                        without corresponding sub-status. C# has no equivalent guard.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:600-730
- **Issue:**            Swift B176(c) assertion catches bug where evidence is emitted but no
                        sub-status added. C# silently drops evidence.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add Debug.Assert matching Swift B176(c).
- **Code Sample:**      [

                        // Swift: assert(ev.isEmpty, "B176(c): ...")
                        // C#: no equivalent guard

                        ]

---

- **[B043]**            ResponseSizeStatus and StubEntropyStatus InsufficientData not
                        explicitly handled in C# — rely on default arm
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. Both enums define InsufficientData but C# Synthesizer relies on
                        default arm → Inconclusive. Swift has explicit cases. New enum members
                        would silently fall into Inconclusive.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:853-868, 396-397
- **Issue:**            `default: subs.Add(SectionStatus.Inconclusive)` catches
                        InsufficientData. No compile error on unhandled future values.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add explicit InsufficientData cases.
                        [Fix 2] Remove default
                        arms so unhandled values become compile errors.
- **Code Sample:**      [

                        // C#: default → Inconclusive (silent catch-all)
                        // Swift: explicit case .insufficientData → .inconclusive

                        ]

---

- **[B044]**            ResponseSizeStatus enum missing Inconclusive member in C#
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. C# enum has 4 members (Normal, Oversized, InsufficientData,
                        Untested). Swift has 5 (adds .inconclusive). Default arm papers over
                        the gap.
- **Location:**         Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:853-868
- **Issue:**            C# ResponseSizeStatus enum lacks Inconclusive. Swift has it.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add Inconclusive to C# enum or confirm intentional omission.
- **Code Sample:**      [

                        // C# enum: Normal, Oversized, InsufficientData, Untested (4)
                        // Swift enum: + .inconclusive (5)

                        ]

---

- **[D098]**            C# DNS parser drops all header evidence on truncated responses — EDNS,
                        flags, wire-size silently lost
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      High. Truncated response with EDNS0 OPT, AA=1, RA=1, AD=1 → C#
                        returns only Status, ResponseCode, IsTruncated, AuthorityRecords. All
                        other fields default to null/false/0. Swift preserves everything.
- **Location:**         Windows/dotNET/Syxx.Kit/DNSResolver.cs:689-703
- **Issue:**            Truncated return path omits Edns, EdnsMalformed, IsAuthoritative,
                        RecursionAvailable, AuthenticatedData, ResponseByteLength.
- **Cross-ref. Delta:** D072 (DNSResponse missing 4 fields).
- **Fix(es):**          [Fix 1] Populate all parsed fields in the truncated return.
- **Code Sample:**      [

                        // DNSResolver.cs:689-703 — truncated path
                        return new DNSResponse {
                            Status = Truncated, ResponseCode = ...,
                            IsTruncated = true, AuthorityRecords = ...
                            // Missing: Edns, IsAuthoritative, RecursionAvailable,
                            //   AuthenticatedData, ResponseByteLength
                        };

                        ]

---

- **[D099]**            C# DNS parser drops all header evidence on ServFail responses
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      High. SERVFAIL with EDNS0, AA=1, authority records → C# returns only
                        Status and ResponseCode. Indistinguishable from ServFail with no data.
- **Location:**         Windows/dotNET/Syxx.Kit/DNSResolver.cs:706-707
- **Issue:**            ServFail return path omits all parsed header fields.
- **Cross-ref. Delta:** D098 (truncated same pattern), D100 (Refused same pattern).
- **Fix(es):**          [Fix 1] Populate same fields as NXDomain branch.
- **Code Sample:**      [

                        // DNSResolver.cs:706-707
                        return new DNSResponse {
                            Status = ServFail, ResponseCode = ...
                            // Missing: all header fields
                        };

                        ]

---

- **[D100]**            C# DNS parser drops all header evidence on Refused responses
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      High. Same defect as D099 but for RCODE=5 (REFUSED).
- **Location:**         Windows/dotNET/Syxx.Kit/DNSResolver.cs:724-725
- **Issue:**            Refused return path omits all parsed header fields.
- **Cross-ref. Delta:** D098, D099.
- **Fix(es):**          [Fix 1] Populate same fields as NXDomain branch.
- **Code Sample:**      [

                        // DNSResolver.cs:724-725
                        return new DNSResponse {
                            Status = Refused, ResponseCode = ...
                            // Missing: all header fields
                        };

                        ]

---

- **[D101]**            C# DNS parser misclassifies non-standard RCODEs as TransportError —
                        FORMERR/NOTIMP/BADVERS indistinguishable from unreachable
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      High. RCODE 1 (FORMERR), 4 (NOTIMP), 16 (BADVERS) return
                        TransportError on C# — same status as "server unreachable." Swift
                        uses .rcodeOther (distinct status).
- **Location:**         Windows/dotNET/Syxx.Kit/DNSResolver.cs:726-727
- **Issue:**            `combinedResponseCode != 0` → `TransportError`. Swift uses `.rcodeOther`
                        for non-standard RCODEs.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Return RcodeOther for non-standard RCODEs.
- **Code Sample:**      [

                        // C#: non-zero RCODE → TransportError (wrong)
                        // Swift: non-standard RCODE → .rcodeOther (correct)

                        ]

---

- **[D102]**            C# LicenseManager silently returns Invalid on Keygen HTTP 200 with
                        unexpected JSON shape — paying subscriber locked out
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      High. MITM/captive portal returning structurally different JSON causes
                        silent Invalid result. Signature never verified (gated behind
                        meta.Valid). Offline grace never activates (result is Invalid, not
                        NetworkError). Swift verifies signature regardless and returns
                        .networkError on verification failure.
- **Location:**         Windows/dotNET/Syxx.Kit/LicenseManager.cs:611-614
- **Issue:**            `JsonSerializer.Deserialize` succeeds on wrong-shape JSON. Meta gets
                        default (Valid=false). Signature check skipped. Returns Invalid.
- **Cross-ref. Delta:** E017 (hardcoded key), E018 (algorithm mismatch).
- **Fix(es):**          [Fix 1] Verify signature before checking meta.Valid.
                        [Fix 2] Require
                        Keygen-Signature header; return NetworkError if absent.
- **Code Sample:**      [

                        // LicenseManager.cs:611-614
                        var validation = JsonSerializer.Deserialize<...>(resJson);
                        if (validation?.Meta.Valid == true) { // only verified when Valid
                            if (!VerifyKeygenSignature(...)) return NetworkError;
                        }
                        return (validation?.Meta.Valid ?? false) ? Valid : Invalid;

                        ]

---

- **[D103]**            C# DNS parser answer-section class filter hardcoded to IN(1) —
                        CHAOS-class answers silently dropped
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Medium. CHAOS-class DNS queries (version.bind TXT CHAOS for resolver
                        fingerprinting) have answers silently dropped. Swift uses questionClass.
- **Location:**         Windows/dotNET/Syxx.Kit/DNSResolver.cs:844
- **Issue:**            `if (r.Cls != 1) continue` — hardcoded IN(1). CHAOS answers (class 3)
                        dropped. Swift extracts QCLASS from question section.
- **Cross-ref. Delta:** D072 (DNSResponse missing fields).
- **Fix(es):**          [Fix 1] Extract questionClass from question section.
                        [Fix 2] Use it
                        instead of hardcoded 1.
- **Code Sample:**      [

                        // DNSResolver.cs:844
                        if (r.Cls != 1) continue;  // hardcoded IN(1)
                        // Swift: if r.cls != questionClass { offClassCount += 1; continue }

                        ]

---

- **[D104]**            C# DNS parser ParseName lacks global pointer-jump budget — CPU DoS
                        vector via deeply chained pointers
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Medium. Hostile DNS response with many records, each containing deeply
                        pointer-chained names, causes O(n²) CPU. Per-call visited set is reset
                        each invocation. Swift uses shared JumpBudget (1024 cap).
- **Location:**         Windows/dotNET/Syxx.Kit/DNSResolver.cs:1083
- **Issue:**            `ParseName` uses local `HashSet<int>()` visited set, fresh per call.
                        No global budget across calls in a single response parse.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add global jump budget (1024) passed through all ParseName
                        calls. [Fix 2] Decrement on each pointer jump; throw on exhaustion.
- **Code Sample:**      [

                        // DNSResolver.cs:1083
                        var visited = new HashSet<int>();  // fresh per call, no global budget
                        // Swift: JumpBudget class shared across all parseName calls

                        ]

---

- **[D105]**            SynthesisEngine all-untested with indicators returns .inconclusive
                        instead of .untested — dishonest state classification
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Medium. When all sections are .untested but indicators are present
                        (internal inconsistency), engine proceeds to scoring instead of
                        returning .untested. Same on both platforms.
- **Location:**         macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift:649-653;
                        Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:848-852
- **Issue:**            `allUntested && noIndicators` guard skips .untested early return when
                        indicators present. All-untested + indicators = internal inconsistency
                        that should return .untested, not proceed to scoring.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Tighten guard to `if allUntested` (drop noIndicators conjunct).
- **Code Sample:**      [

                        // SynthesisEngine.swift:649-653
                        if allUntested && noIndicators { return .untested }
                        // Should be: if allUntested { return .untested }

                        ]

---

- **[D106]**            WitnessWireJson outer response path has no required-field validation
                        after deserialization
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. Unknown JSON keys silently ignored (forward-compatible by design).
                        Typo in field name silently accepted with default value. The signed
                        attestation path guards against this but the outer unsigned path does not.
- **Location:**         Windows/dotNET/Syxx.Kit/LeakDetector.cs:284-286
- **Issue:**            JsonSerializerOptions has PropertyNameCaseInsensitive=true but no
                        UnmappedMemberHandling. ParseSignedAttestation guards required fields;
                        outer VerifyTokenAsync does not.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Verify required fields non-null after deserialization in
                        VerifyTokenAsync and RunCollectorTestAsync.
- **Code Sample:**      [

                        // LeakDetector.cs:284-286
                        PropertyNameCaseInsensitive = true
                        // No UnmappedMemberHandling or required-field validation

                        ]

---

- **[A069]**            RRL pruning is dead code — _rrl_buckets grows unbounded (memory leak)
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Critical. Every code path through the `with _rrl_lock:` block
                        explicitly returns, making the pruning block at lines 85-90
                        unreachable. Under sustained DNS traffic, dict grows until OOM.
- **Location:**         Server/syxx_server/dns_authoritative.py:85-90
- **Issue:**            `_rrl_ok()` returns before reaching the pruning code. The
                        `_rrl_buckets` dict keyed on `(source_ip, qname, qtype)` never shrinks.
- **Cross-ref. Delta:** A070, A071 (same pattern).
- **Fix(es):**          [Fix 1] Move pruning into the `with` block before the returns, or
                        use a separate periodic pruning task.
- **Code Sample:**      [

                        # dns_authoritative.py:70-90
                        with _rrl_lock:
                            ...
                            return True   # ← returns before pruning
                            return False  # ← returns before pruning
                            return True   # ← returns before pruning
                        # DEAD CODE below — never reached:
                        if len(_rrl_buckets) > 1000: ...

                        ]

---

- **[A070]**            _dns_rate_buckets has no pruning — unbounded memory growth
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      High. Each unique source IP creates permanent entry. DNS resolvers
                        rotate source IPs; dict grows over days/weeks until restart.
- **Location:**         Server/syxx_server/dns_authoritative.py:30-51
- **Issue:**            `_dns_rate_ok()` has no pruning mechanism. Window-reset is per-entry
                        but entries are never removed.
- **Cross-ref. Delta:** A069 (RRL same pattern), A071.
- **Fix(es):**          [Fix 1] Add periodic pruning or max-size eviction.
- **Code Sample:**      [

                        # dns_authoritative.py:30-51 — no pruning at all

                        ]

---

- **[A071]**            _token_obs_counts has no pruning — permanent counter per leak token
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      High. Every distinct leak token leaves permanent counter. Tokens
                        expire from DB but in-memory counter persists until restart.
- **Location:**         Server/syxx_server/dns_authoritative.py:97-108
- **Issue:**            `_token_obs_counts` keyed on leak token, never pruned.
- **Cross-ref. Delta:** A069, A070.
- **Fix(es):**          [Fix 1] Add periodic pruning or TTL-based eviction.
- **Code Sample:**      [

                        # dns_authoritative.py:97-108 — no pruning

                        ]

---

- **[A072]**            _sync_api_keys() ThreadPoolExecutor can hang scheduler loop beyond
                        deadline — pool.__exit__ waits for all threads regardless of timeout
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      High. If _check_one() hangs (Keygen API stall, DNS timeout), the
                        `with` block blocks until that thread completes — potentially far
                        beyond the 2-minute budget.
- **Location:**         Server/syxx_server/scheduler.py:163-173
- **Issue:**            `as_completed(futures, timeout=...)` fires timeout but
                        `ThreadPoolExecutor.__exit__` calls `shutdown(wait=True)` which waits
                        for ALL submitted futures.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Cancel pending futures on timeout.
                        [Fix 2] Use
                        `shutdown(wait=False, cancel_futures=True)` (Python 3.9+).
- **Code Sample:**      [

                        # scheduler.py:163-173
                        with ThreadPoolExecutor(...) as pool:
                            futures = {pool.submit(_check_one, s): s for s in subjects}
                            for fut in as_completed(futures, timeout=...):
                                ...  # timeout fires, but pool.__exit__ waits for ALL

                        ]

---

- **[A073]**            DoT listener has no restart mechanism — silent daemon death on
                        unhandled exception
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      High. If serve_forever() raises, daemon thread silently dies.
                        Unlike UDP collector which has _rebind_loop, DoT has no restart.
- **Location:**         Server/syxx_server/dot_listener.py:147-151
- **Issue:**            `server.serve_forever()` in daemon=True thread. finally sets health
                        to "down" but no rebind loop or restart mechanism.
- **Cross-ref. Delta:** A074 (_serve_dns same pattern).
- **Fix(es):**          [Fix 1] Add _rebind_loop pattern matching dns_authoritative.py.
- **Code Sample:**      [

                        # dot_listener.py:147-151
                        try:
                            server.serve_forever()  # if crashes, daemon thread dies
                        finally:
                            listeners.set_state("dot", "down", ...)
                        # No restart — unlike dns_authoritative.py _rebind_loop

                        ]

---

- **[A074]**            _serve_dns() has no top-level exception handler — unexpected exceptions
                        silently kill DNS collector thread
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Medium. Only catches (OSError, ValueError). MemoryError, RuntimeError,
                        or future regressions silently kill the dns-server daemon thread.
- **Location:**         Server/syxx_server/dns_authoritative.py:294-319
- **Issue:**            No catch-all for unexpected exceptions. _rebind_loop only guards
                        startup bind failures, not mid-flight crashes.
- **Cross-ref. Delta:** A073 (DoT same pattern).
- **Fix(es):**          [Fix 1] Add catch-all exception handler with logging and restart.
- **Code Sample:**      [

                        # dns_authoritative.py:294-298
                        try:
                            readable, _, _ = select.select(socks, [], [], 1.0)
                        except (OSError, ValueError):  # only catches these two
                            ...
                        # Any other exception kills the thread silently

                        ]

---

- **[A075]**            db.backup() blocks scheduler shutdown for unbounded time — large DB
                        backup holds thread beyond 10-second join timeout
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Medium. For large DBs (hundreds of MB), backup takes minutes.
                        Scheduler shutdown joins with 10s timeout but thread is blocked in
                        src.backup(dst). daemon=True kills thread mid-backup on exit,
                        potentially leaving corrupt .partial file.
- **Location:**         Server/syxx_server/scheduler.py:276-279; db.py:956-1002
- **Issue:**            `_backup_db()` calls `db.backup()` without checking _shutdown during
                        operation. Thread cannot respond to shutdown until backup completes.
- **Cross-ref. Delta:** A014 (backup tmp file leak).
- **Fix(es):**          [Fix 1] Check _shutdown.is_set() periodically during backup.
                        [Fix 2] Use backup API's step callback to check for cancellation.
- **Code Sample:**      [

                        # scheduler.py:276-279 — blocks for unbounded time
                        db.backup()  # pages through entire DB
                        # _shutdown not checked during operation

                        ]

---

- **[D107]**            GluttonView "No probes dispatched" label visible when probes ARE
                        dispatched — visibility gated on HasSubdomain instead of probe count
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Medium. Once a subdomain exists, both the label and the probe results
                        ItemsControl render simultaneously.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/Views/GluttonView.axaml:142
- **Issue:**            TextBlock "No probes dispatched" has IsVisible bound to HasSubdomain
                        (true once subdomain exists) instead of LeakProbeResults.Count == 0.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Gate visibility on LeakProbeResults.Count == 0 or add
                        HasNoProbeResults computed property.
- **Code Sample:**      [

                        <!-- GluttonView.axaml:142-143 -->
                        <TextBlock Text="No probes dispatched."
                                   IsVisible="{Binding HasSubdomain}" />
                        <!-- Should be: IsVisible="{Binding !HasProbeResults}" -->

                        ]

---

- **[D108]**            Settings pane missing crash reporting toggle — user cannot disable
                        Sentry after onboarding
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Medium. CrashReporting toggle exists only in FirstRunViewModel.
                        SettingsViewModel has TelemetryEnabled, NotificationsEnabled, etc.
                        but no CrashReportingEnabled. LinuxReadyServices.ApplySentryState()
                        is callable but never invoked from Settings after onboarding.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/SettingsViewModel.cs
- **Issue:**            No CrashReportingEnabled property. No ToggleSwitch in SettingsView.
                        ApplySentryState() only called at startup.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add CrashReportingEnabled to SettingsViewModel.
                        [Fix 2] Wire
                        OnChanged to LinuxReadyServices.ApplySentryState(). [Fix 3] Add
                        ToggleSwitch in SettingsView.axaml.
- **Code Sample:**      [

                        // SettingsViewModel — has TelemetryEnabled, NotificationsEnabled,
                        // EncryptEnabled, UseDot — but no CrashReportingEnabled

                        ]

---

- **[D109]**            GluttonViewModel constructor fire-and-forget async calls silently
                        swallow exceptions — loading cards stuck forever
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Medium. LoadFeatureStatusAsync() and CheckStatusAsync() called as
                        `_ = ...` with no try/catch. Exceptions silently swallowed; Feature
                        Status card stays at "Loading..." forever.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/GluttonViewModel.cs:170-171
- **Issue:**            Discarded Task means exception never surfaces. No ContinueWith or
                        try/catch wrapping.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Wrap in try/catch that sets error status text.
                        [Fix 2] Use
                        ContinueWith to surface errors.
- **Code Sample:**      [

                        // GluttonViewModel.cs:170-171
                        _ = LoadFeatureStatusAsync();  // fire-and-forget, no error handling
                        _ = CheckStatusAsync();        // fire-and-forget, no error handling

                        ]

---

- **[D110]**            Dashboard action buttons all hardcoded "Not yet available" with no
                        VM-driven state — feature gating requires XAML rewrite
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. Buttons statically disabled in XAML with hardcoded text.
                        DashboardViewModel has no properties to control states.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/DashboardViewModel.cs
- **Issue:**            "Conduct All Scans", "Flush DNS Cache", "Enable Unbound DNS",
                        "Enable Encrypted DNS" all hardcoded IsEnabled=False.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add bool properties to DashboardViewModel and bind XAML.
- **Code Sample:**      [

                        // DashboardView.axaml
                        <Button IsEnabled="False">
                            <TextBlock Text="Not yet available" />
                        </Button>

                        ]

---

- **[D111]**            PortalViewModel resolver line has no severity brush on empty-resolver
                        state — warning renders as normal text
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. "No DNS servers discovered" renders in default text color
                        instead of warning amber. MirrorViewModel sets amber for same case.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/PortalViewModel.cs:49-51
- **Issue:**            No brush set for empty-resolver case. MirrorViewModel.cs:79-83 sets
                        Amber for same condition.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Set ResolverBrush to amber when servers.Count == 0.
- **Code Sample:**      [

                        // PortalViewModel.cs:49-51
                        ResolversLine = servers.Count == 0
                            ? "No DNS servers discovered"
                            : string.Join(", ", servers);
                        // No brush set for empty case

                        ]

---

- **[D112]**            WitnessViewModel SelfVerifyCommand relies on CommunityToolkit source
                        generator for private method — fragile binding
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. [RelayCommand] on private method generates public property.
                        Works at runtime but fragile if toolkit changes or method visibility
                        changes.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/WitnessViewModel.cs:474
- **Issue:**            `private void SelfVerify()` with [RelayCommand] generates public
                        SelfVerifyCommand property. XAML binds to it. Currently works but
                        fragile.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Document the intentional pattern. No code change needed.
- **Code Sample:**      [

                        // WitnessViewModel.cs:474
                        [RelayCommand]
                        private void SelfVerify() { ... }

                        ]

---

- **[D113]**            CageView hardcoded CLI command "sudo syxx cage --duration 30" in XAML
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. Binary name and duration hardcoded. If binary renamed or
                        duration adjusted, UI text becomes stale.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/Views/CageView.axaml:30
- **Issue:**            TextBlock text is hardcoded CLI command string.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Move to ViewModel property or shared constant.
- **Code Sample:**      [

                        <!-- CageView.axaml:30 -->
                        <TextBlock Text="sudo syxx cage --duration 30" />

                        ]

---

- **[D114]**            WhatsNewViewModel IsFromCache and ProvenanceText only set in fallback
                        path — inert in normal path
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. Normal (non-fallback) code path never sets IsFromCache or
                        ProvenanceText. XAML bindings remain at defaults.
- **Location:**         Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/WhatsNewViewModel.cs
- **Issue:**            LoadAsync() normal path sets HasBroadcast, StatusText, etc. but never
                        IsFromCache or ProvenanceText.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Set IsFromCache/ProvenanceText in normal path or remove
                        unused bindings.
- **Code Sample:**      [

                        // LoadAsync() normal path — never sets IsFromCache
                        // or ProvenanceText

                        ]

---

- **[D115]**            C# LeakDetector signed payload field name mismatch — expects "leaked"
                        but server sends "queriesObserved" — all Windows collector-verified
                        leak tests broken
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      High. Server signs payload with "queriesObserved" field. C#
                        SignedLeakAttestation expects [JsonPropertyName("leaked")]. dto.Leaked
                        is null → parse returns null → DeriveResult returns Inconclusive.
                        Every collector-verified leak test on Windows is broken.
- **Location:**         Windows/dotNET/Syxx.Kit/LeakDetector.cs:608-615, 803
- **Issue:**            C# has `[JsonPropertyName("leaked")] public bool? Leaked`. Swift
                        extracts "leaked" then falls back to "queriesObserved". C# has no
                        fallback.
- **Cross-ref. Delta:** D116 (replay protection missing from Swift).
- **Fix(es):**          [Fix 1] Add `[JsonPropertyName("queriesObserved")]` property to
                        SignedLeakAttestation. [Fix 2] Derive Leaked from it in
                        ParseSignedAttestation.
- **Code Sample:**      [

                        // C# LeakDetector.cs:803
                        [JsonPropertyName("leaked")] public bool? Leaked
                        // Server sends: {"queriesObserved": true, ...}
                        // Swift fallback: if let queriesObserved = json["queriesObserved"]

                        ]

---

- **[D116]**            Swift LeakDetector missing B216 replay protection — accepts replayed
                        attestations that C# correctly rejects
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      High. Relay can capture valid attestation for token T1/zone
                        leak.syxx.app and replay against T2/syxx.app. C# detects mismatch
                        and returns Inconclusive. Swift accepts replayed attestation and may
                        return .notDetected (false clean).
- **Location:**         macOS/Sources/SyxxKit/LeakDetector.swift:346-410
- **Issue:**            Swift runCollectorTest does not check token, zone, or resolverCount
                        consistency. C# DeriveResult (lines 648-668) checks all three.
- **Cross-ref. Delta:** D115 (field name mismatch).
- **Fix(es):**          [Fix 1] Add extractSignedToken, extractSignedZone, and
                        resolverCount-consistency check to Swift, mirroring C# B216 gates.
- **Code Sample:**      [

                        // Swift LeakDetector.swift:346-410 — no token/zone/resolverCount check
                        // C# DeriveResult lines 648-668:
                        // if (tokenLabel != signed.Token) return Inconclusive
                        // if (baseDomain != signed.Zone) return Inconclusive

                        ]

---

- **[D117]**            LeakDetector keyId computation length divergence — C# uses 4 bytes
                        (8 hex), Swift uses 8 bytes (16 hex) — trusted key sync broken
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Medium. Server sends keyId as 8 bytes SHA-256 (16 hex). C# computes
                        4 bytes (8 hex) and rejects with "keyId mismatch." Trusted signing
                        key never persisted on Windows.
- **Location:**         macOS/Sources/SyxxKit/LeakDetector.swift:887;
                        Windows/dotNET/Syxx.Kit/LeakDetector.cs:1002
- **Issue:**            Swift: SHA256.hash(data:).prefix(8) → 16 hex chars. C#:
                        Convert.ToHexString(keyHash, 0, 4) → 8 hex chars. Doc comments say
                        "first 8 bytes" but implementations diverge.
- **Cross-ref. Delta:** D080 (WitnessKeyManager same divergence pattern).
- **Fix(es):**          [Fix 1] Align on 8 bytes (16 hex chars).
                        [Fix 2] Verify against
                        server's actual keyId computation.
- **Code Sample:**      [

                        // Swift: SHA256.hash(data:).prefix(8).hex → 16 hex chars
                        // C#: Convert.ToHexString(keyHash, 0, 4) → 8 hex chars

                        ]

---

- **[D118]**            VerifyResponse.leaked type divergence — Swift Bool? vs C# bool
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Medium. Server omits "leaked" key → Swift decodes nil, C# decodes
                        false. Same JSON produces different in-memory values. Legacy
                        unauthenticated path can fork results.
- **Location:**         macOS/Sources/SyxxKit/LeakDetector.swift:575;
                        Windows/dotNET/Syxx.Kit/LeakDetector.cs:794
- **Issue:**            Swift: `let leaked: Bool?`. C#: `public bool Leaked { get; set; }`
                        (non-nullable, defaults to false).
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Change C# to `bool? Leaked`.
                        [Fix 2] Treat null as "no claim."
- **Code Sample:**      [

                        // Swift: let leaked: Bool?  (nil when absent)
                        // C#:    public bool Leaked { get; set; }  (false when absent)

                        ]

---

- **[D119]**            Swift VerifyResponse has "inconclusive" field; C# doesn't — schema
                        asymmetry
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Medium. Server sends {"inconclusive": true}. Swift decodes it; C#
                        silently ignores. Latent fork if future code consumes this field.
- **Location:**         macOS/Sources/SyxxKit/LeakDetector.swift:581
- **Issue:**            Swift has `let inconclusive: Bool?`. C# VerifyResponse has no
                        Inconclusive property.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add `bool? Inconclusive` to C# or remove from Swift.
- **Code Sample:**      [

                        // Swift: let inconclusive: Bool?
                        // C#: no Inconclusive property

                        ]

---

- **[D120]**            LeakDetector empty-servers result divergence — Swift .error vs C#
                        .Inconclusive
- **Status:**           **OPEN** — Found during fast delta-analysis 2026-08-09.
- **Criticality:**      Low. No system DNS resolvers → macOS returns .error, Windows returns
                        .Inconclusive. Same for all-dispatch-failed guard.
- **Location:**         macOS/Sources/SyxxKit/LeakDetector.swift:298;
                        Windows/dotNET/Syxx.Kit/LeakDetector.cs:338
- **Issue:**            Same input produces different LeakResult values across platforms.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Align on Inconclusive (C# choice — "no evidence gathered"
                        is more honest than .error which implies product defect).
- **Code Sample:**      [

                        // Swift: return LeakTestResult(result: .error, ...)
                        // C#:    Result = LeakResult.Inconclusive

                        ]

---

- **[A076]**            DoH DNSSEC probe reports signed zones as "unsigned" when DO bit
                        can't be carried via JSON API
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      High. DoH JSON API doesn't set DO bit → no RRSIG returned → code
                        reports signed="no" instead of "unknown." Contradicts the function's
                        own documented tri-state contract.
- **Location:**         Server/syxx_server/investigate.py:296-299
- **Issue:**            `elif dnskey_records: signed = "no"` — should be "unknown" per the
                        docstring at line 268.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Change to signed="unknown" with note about DO bit.
- **Code Sample:**      [

                        # investigate.py:296-299
                        elif dnskey_records:
                            signed = "no"  # WRONG — DO bit not carried via DoH JSON
                            # Should be: signed = "unknown"

                        ]

---

- **[A077]**            zone_signing_status ignores parseComplete flag — false "unsigned" on
                        incomplete DNSKEY parse
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. Malformed record drops subsequent DNSKEY from answer section.
                        zone_signing_status reports "unsigned" instead of "inconclusive."
- **Location:**         Server/syxx_server/resolver.py:873-880
- **Issue:**            No check of resp["parseComplete"] before checking for DNSKEY records.
                        Walker at line 943 correctly checks parseComplete.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add `if not resp.get("parseComplete", True): return None, []`
                        before DNSKEY check.
- **Code Sample:**      [

                        # resolver.py:873-878 — no parseComplete check
                        resp = _query_sections(zone, TYPE_DNSKEY, ...)
                        if not any(r["type"] == "DNSKEY" for r in resp["answers"]):
                            return False, []  # "unsigned" — but parse may be incomplete

                        ]

---

- **[A078]**            Warp route error messages leak internal IP addresses and network
                        topology to API clients
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. BlockedError/FetchError messages contain resolved IPs and
                        internal hostnames. Attacker can enumerate server's DNS topology.
- **Location:**         Server/syxx_server/app.py:1791-1793, 1823-1825, 1920-1926
- **Issue:**            Raw str(e) passed to API client. _diff_content at line 275 already
                        redacts — Warp routes should follow same pattern.
- **Cross-ref. Delta:** E053 (domain names in telemetry).
- **Fix(es):**          [Fix 1] Redact error details; log full error server-side only.
- **Code Sample:**      [

                        # app.py:1791
                        except fetcher.BlockedError as e:
                            return self._json_error(400, f"target blocked: {e}")
                        # e contains resolved IP + hostname — leaked to client

                        ]

---

- **[A079]**            SMTP password and all server secrets stored as immutable Python
                        strings — no memory zeroing possible
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Low. Fundamental Python limitation. Secrets persist in process
                        memory for server lifetime. Core dump or /proc read extracts all.
- **Location:**         Server/syxx_server/config.py:574 (SMTP), 149 (Stripe), 173 (Keygen)
- **Issue:**            Python strings are immutable — cannot be zeroed. All secrets exposed
                        via memory forensics.
- **Cross-ref. Delta:** E031 (SQLite no encryption).
- **Fix(es):**          [Fix 1] Restrict core dumps.
                        [Fix 2] Use secrets manager with
                        short-lived tokens. [Fix 3] Subprocess isolation for most sensitive.
- **Code Sample:**      [

                        # config.py:574 — immutable Python string, cannot be zeroed
                        SMTP_PASSWORD = _s("SYXX_SMTP_PASSWORD", "")

                        ]

---

- **[A080]**            Missing index on passive_dns.source — full-table scan on every purge
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. DELETE WHERE source='leak-collector' scans all 500K rows.
- **Location:**         Server/syxx_server/db.py:1280
- **Issue:**            Low cardinality column (~3-5 values) but no index. B-tree index would
                        be highly effective.
- **Cross-ref. Delta:** A032 (passive_dns.last_seen missing index).
- **Fix(es):**          [Fix 1] CREATE INDEX idx_passive_source ON passive_dns(source).
- **Code Sample:**      [

                        # db.py:1280 — full-table scan
                        DELETE FROM passive_dns WHERE source = 'leak-collector'

                        ]

---

- **[A081]**            N+1 query in passivedns.ingest() — SELECT LIMIT 101 before every INSERT
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. Highest-frequency write path does extra SELECT per ingest.
- **Location:**         Server/syxx_server/passivedns.py:24-30
- **Issue:**            Queries up to 101 rows to check cardinality before every insert.
                        Materializes 101 dicts, builds set, discards.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Use COUNT(DISTINCT rtype, value) with LIMIT 101 shortcut.
- **Code Sample:**      [

                        # passivedns.py:24-30
                        existing = db.passive_query(domain, _PASSIVE_PER_DOMAIN_MAX + 1)
                        known_values = {(r["rtype"], r["value"]) for r in existing}

                        ]

---

- **[A082]**            _cap_rows double full-table scan — COUNT(*) + DELETE subquery per purge
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. O(n log n) on 500K-row passive_dns at every purge.
- **Location:**         Server/syxx_server/db.py:942-953
- **Issue:**            Two full-table operations: COUNT(*) scan + DELETE subquery sort.
- **Cross-ref. Delta:** A032 (missing index).
- **Fix(es):**          [Fix 1] Single query combining count + delete.
                        [Fix 2] Index on
                        last_seen makes sort O(excess).
- **Code Sample:**      [

                        # db.py:942-953 — two full-table operations
                        row = conn.execute(f"SELECT COUNT(*) c FROM {table}").fetchone()
                        conn.execute(f"DELETE FROM {table} WHERE rowid IN "
                                     f"(SELECT rowid FROM {table} ORDER BY ... LIMIT ?)")

                        ]

---

- **[A083]**            Missing index on warp_cache.created_at — full-table scan during purge
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Low. 10K-row table, modest scan but runs every purge.
- **Location:**         Server/syxx_server/db.py (schema + lines 948, 1301)
- **Issue:**            warp_cache has no indexes at all.
- **Cross-ref. Delta:** A032, A080.
- **Fix(es):**          [Fix 1] CREATE INDEX idx_warp_created ON warp_cache(created_at).
- **Code Sample:**      [

                        # warp_cache — no indexes, sorted by created_at in purge

                        ]

---

- **[B045]**            macOS DNSCapture parser lacks monotonic compression-pointer defense —
                        forward pointers can fabricate domain names
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      High. Server resolver.py:189 enforces monotonic rule; macOS capture
                        parser does not. Attacker can inject fabricated domain names into
                        capture reports.
- **Location:**         macOS/Sources/SyxxKit/DNSCapture.swift:690-695
- **Issue:**            `guard pointer < bytes.count` allows forward jumps. Server uses
                        `if pointer >= offset: raise DNSError("non-monotonic")`.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add `guard pointer < current` after bounds check.
- **Code Sample:**      [

                        // DNSCapture.swift:690
                        guard pointer < bytes.count else { throw DNSParseError() }
                        // Missing: guard pointer < current  // monotonic defense

                        ]

---

- **[B046]**            ProbeRunner.runTyped classifies deliberate nil return as transportError
                        — conflates "not applicable" with "network failure"
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. Downstream consumers that check .transportError for retry
                        logic would incorrectly retry a deliberate nil.
- **Location:**         macOS/Sources/SyxxKit/ProbeRunner.swift:193-201
- **Issue:**            `operation_returned_nil` forced into .transportError case. No
                        `.noResult` case exists in ProbeOutcome enum.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add .noResult case to ProbeOutcome.
                        [Fix 2] Or change
                        produced to carry Output?.
- **Code Sample:**      [

                        // ProbeRunner.swift:197
                        return .transportError("operation_returned_nil")
                        // Should be a distinct case, not transportError

                        ]

---

- **[B047]**            macOS AAAA answer rdata uses uncompressed IPv6 format — C077 fix
                        applied only to IP header addresses
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Low. Cosmetic divergence in capture exports. Same address appears
                        different when comparing AAAA rdata with resolver-reported addresses.
- **Location:**         macOS/Sources/SyxxKit/DNSCapture.swift:639-642
- **Issue:**            Raw group-hex join produces "2001:db8:0:0:0:0:0:1" instead of
                        RFC 5952 "2001:db8::1".
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Use inet_ntop(AF_INET6, ...) for AAAA rdata.
- **Code Sample:**      [

                        // DNSCapture.swift:639-642 — uncompressed format
                        groups.append(String(format: "%x", val))
                        return groups.joined(separator: ":")
                        // Should use inet_ntop for canonical form

                        ]

---

- **[B048]**            Windows CaptureEvents totalResponses>0 guard silently excludes 100%
                        response loss from detection
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. 15 outbound queries + 0 responses = total DNS failure, but
                        ResponseLossHigh event never fires because guard prevents it.
- **Location:**         Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs:259
- **Issue:**            `totalResponses > 0` guard evaluates false for zero responses. Method
                        returns empty events list. No fallthrough.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Remove the guard; (double)0/outbound = 0.0 <= 0.5 satisfies
                        ratio check.
- **Code Sample:**      [

                        // CaptureEvents.cs:259
                        && totalResponses > 0  // guard excludes the worst case
                        // (double)0 / outbound = 0.0 <= 0.5 — would pass without guard

                        ]

---

- **[C010]**            Windows CaptureEngine BothFailed produces Completed outcome via empty
                        Task.WhenAll — capture never started but reported as success
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. Both sockets fail → empty tasks list → WhenAll completes
                        immediately → Outcome returns Completed. Caller renders empty
                        capture as normal result.
- **Location:**         Windows/dotNET/Syxx.Kit/Capture/CaptureEngine.cs:125-128
- **Issue:**            When sockets.V4 and V6 are both null, no CaptureLoop starts.
                        Task.WhenAll(emptyList) is already completed.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add synthetic error to _loopErrors.
                        [Fix 2] Return Aborted
                        in OutcomeAsync when no capture loops ran.
- **Code Sample:**      [

                        // CaptureEngine.cs:125-128
                        var tasks = new List<Task>(2);
                        if (sockets.V4 is { } s4) tasks.Add(CaptureLoop(...));
                        if (sockets.V6 is { } s6) tasks.Add(CaptureLoop(...));
                        // When both null: tasks is empty, WhenAll returns completed

                        ]

---

- **[C011]**            macOS PrivilegeManager failed helper install permanently suppresses
                        retry — hasAttemptedInstall set before installHelper()
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. User cancels password prompt → hasAttemptedInstall = true →
                        all subsequent privileged operations silently skip install → fail
                        with connectionFailed. No retry until app restart.
- **Location:**         macOS/Sources/SyxxApp/PrivilegeManager.swift:145-155
- **Issue:**            Flag set BEFORE installHelper() call. If installHelper throws,
                        flag remains true permanently.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Reset hasAttemptedInstall = false in catch block.
                        [Fix 2]
                        Add "Retry Installation" button in UI.
- **Code Sample:**      [

                        // PrivilegeManager.swift:149
                        hasAttemptedInstall = true  // set BEFORE attempt
                        try await installHelper()   // can throw — flag stays true

                        ]

---

- **[D121]**            LAN: Windows NetworkAddressClassifier missing publicRoutable — public
                        IPs silently classified as Other and probed
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      High. macOS classifies public IPs as .publicRoutable (not probeable).
                        Windows classifies as Other (probeable). TCP-connect scan of public
                        IP proceeds without user awareness.
- **Location:**         macOS/Sources/SyxxKit/LAN/NetworkAddressClassifier.swift:64-67;
                        Windows/dotNET/Syxx.Kit/LAN/LanCartography.cs:48-75
- **Issue:**            No PublicRoutable enum member in C#. Falls through to Other which
                        IsProbeable returns true for.
- **Cross-ref. Delta:** D122-D125 (other LAN parity gaps).
- **Fix(es):**          [Fix 1] Add PublicRoutable to C# LANAddressKind enum.
                        [Fix 2]
                        Implement public-IP detection check.
- **Code Sample:**      [

                        // macOS: .publicRoutable → NOT probeable
                        // Windows: no PublicRoutable → falls to Other → IS probeable

                        ]

---

- **[D122]**            LAN: Windows missing directed-broadcast detection — subnet broadcast
                        addresses classified as probeable devices
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      High. 192.168.1.255 classified as Other (probeable) on Windows;
                        .broadcast on macOS (not probeable). Counted as real device.
- **Location:**         macOS/Sources/SyxxKit/LAN/NetworkAddressClassifier.swift:34-45;
                        Windows/dotNET/Syxx.Kit/LAN/LanCartography.cs:48-75
- **Issue:**            C# checks only global broadcast 255.255.255.255. No directed-broadcast
                        detection for last-octet 255/0 on private ranges.
- **Cross-ref. Delta:** D121.
- **Fix(es):**          [Fix 1] Add directed-broadcast detection mirroring Swift C114 block.
- **Code Sample:**      [

                        // macOS: detects 192.168.1.255 as .broadcast
                        // Windows: 192.168.1.255 → Other (probeable device)

                        ]

---

- **[D123]**            LAN: Windows missing snapshot persistence — cross-run diff impossible
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      High. macOS persists to ~/.syxx/lan-snapshot.json enabling device-
                        change detection. Windows has no equivalent. Every run starts from
                        nothing. Rogue device between runs never flagged.
- **Location:**         macOS/Sources/SyxxKit/LAN/LANCartographer.swift:557-656;
                        Windows — no equivalent code
- **Issue:**            grep confirms no saveSnapshot/loadSnapshot in C# codebase.
- **Cross-ref. Delta:** D121, D122.
- **Fix(es):**          [Fix 1] Port SaveSnapshot/LoadSnapshot to C#.
                        [Fix 2] Call from
                        LanCommand.Run after rendering.
- **Code Sample:**      [

                        // macOS: saveSnapshot() → ~/.syxx/lan-snapshot.json (0600)
                        // Windows: no equivalent — every run starts from nothing

                        ]

---

- **[D124]**            LAN CLI: Windows missing ARP-unauthenticated warning (C106)
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      High. macOS prints trust caveat about ARP spoofing. Windows renders
                        device grid with no disclaimer.
- **Location:**         macOS/Sources/SyxxCLI/Commands/SyxxLan.swift:66-68;
                        Windows/dotNET/Syxx/Commands/LanCommand.cs:70-107
- **Issue:**            No equivalent warning in Windows CLI output.
- **Cross-ref. Delta:** D121-D123.
- **Fix(es):**          [Fix 1] Add C106 warning block after device grid.
- **Code Sample:**      [

                        // macOS: "⚠️ ARP bindings are unauthenticated..."
                        // Windows: no equivalent warning

                        ]

---

- **[D125]**            Leak CLI: Windows Confirmed output suppresses 🚨 DNS LEAK CONFIRMED
                        headline — primary signal invisible
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      High. macOS prints unmistakable 🚨 headline. Windows prints only
                        "N resolver(s) observed" — reads as informational, not alarming.
- **Location:**         macOS/Sources/SyxxCLI/Commands/SyxxLeak.swift:88;
                        Windows/dotNET/Syxx/Commands/LeakCommand.cs:128-129
- **Issue:**            Exit code correctly 1 (threat) but stdout has no clear leak signal.
- **Cross-ref. Delta:** D126 (NotDetected hardcoded count).
- **Fix(es):**          [Fix 1] Mirror macOS headline text.
- **Code Sample:**      [

                        // macOS: "🚨 DNS LEAK CONFIRMED — N resolver(s) observed..."
                        // Windows: "N resolver(s) observed" — no headline

                        ]

---

- **[D126]**            Leak CLI: Windows NotDetected hardcodes "1 resolver" instead of real
                        count
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. Collector observes 3 resolvers but Windows prints "1 resolver
                        observed." User comparing to dashboard sees mismatch.
- **Location:**         Windows/dotNET/Syxx/Commands/LeakCommand.cs:132
- **Issue:**            Hardcoded "1 resolver" regardless of ObservedResolvers.Count.
- **Cross-ref. Delta:** D125.
- **Fix(es):**          [Fix 1] Use ObservedResolvers.Count dynamically.
- **Code Sample:**      [

                        // LeakCommand.cs:132 — hardcoded "1 resolver"
                        // Should use ObservedResolvers.Count

                        ]

---

- **[D127]**            Sentinel CLI: Windows collapses VPN and DNS-encrypted tri-states to
                        binary — "unknown" rendered as "no"
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. macOS honestly reports "unknown." Windows reports "no,"
                        implying confirmed off when status was undetermined.
- **Location:**         macOS/Sources/SyxxCLI/Commands/SyxxSentinel.swift:24,26;
                        Windows/dotNET/Syxx/Commands/SentinelCommand.cs:50-51
- **Issue:**            C# DnsEncrypted is bool (not ConditionState). VPN ternary collapses
                        Unknown to "no."
- **Cross-ref. Delta:** D128, D129.
- **Fix(es):**          [Fix 1] Change DnsEncrypted to ConditionState.
                        [Fix 2] Render all
                        three states.
- **Code Sample:**      [

                        // macOS: "yes", "no", or "unknown"
                        // Windows VPN: current.VpnActive == Yes ? "yes" : "no"
                        // Windows DNS: current.DnsEncrypted ? "yes" : "no" (bool!)

                        ]

---

- **[D128]**            Sentinel CLI: Windows missing corrupt-baseline quarantine and
                        auto-recovery
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. macOS quarantines corrupt file, establishes fresh baseline,
                        self-heals. Windows prints error and exits — corrupt file persists
                        indefinitely.
- **Location:**         macOS/Sources/SyxxCLI/Commands/SyxxSentinel.swift:89-99;
                        Windows/dotNET/Syxx/Commands/SentinelCommand.cs:86-90
- **Issue:**            No quarantine or re-establish logic on Windows.
- **Cross-ref. Delta:** D129 (missing change log).
- **Fix(es):**          [Fix 1] Port quarantine-and-reestablish logic.
- **Code Sample:**      [

                        // macOS: quarantine corrupt → establish fresh → self-heal
                        // Windows: print error → exit → corrupt file persists

                        ]

---

- **[D129]**            Sentinel CLI: Windows missing persistent change log (C109)
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. macOS logs changes to ~/.syxx/sentinel-changes.jsonl. Windows
                        prints to stdout only — changes lost when baseline rolls forward.
- **Location:**         macOS/Sources/SyxxCLI/Commands/SyxxSentinel.swift:115-142;
                        Windows/dotNET/Syxx/Commands/SentinelCommand.cs
- **Issue:**            grep confirms no sentinel-changes or changeLog in C# codebase.
- **Cross-ref. Delta:** D128.
- **Fix(es):**          [Fix 1] Port appendChangeLog to C#.
- **Code Sample:**      [

                        // macOS: appendChangeLog → ~/.syxx/sentinel-changes.jsonl
                        // Windows: stdout only — changes lost on baseline roll

                        ]

---

- **[D130]**            LAN CLI: Windows off-LAN resolver message is categorical; macOS is
                        conditional — honesty divergence
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. macOS: "not conclusive" (ARP cache is not a probe). Windows:
                        "reached through the gateway" (unestablished claim).
- **Location:**         macOS/Sources/SyxxCLI/Commands/SyxxLan.swift:75-79;
                        Windows/dotNET/Syxx/Commands/LanCommand.cs:106
- **Issue:**            Windows overclaims certainty about resolver reachability.
- **Cross-ref. Delta:** D121-D124.
- **Fix(es):**          [Fix 1] Mirror macOS conditional wording.
- **Code Sample:**      [

                        // macOS: "...not conclusive — resolver may be off-LAN or dead"
                        // Windows: "...reached through the gateway (off-LAN)"

                        ]

---

- **[D131]**            Forensics CLI: Windows never sets non-zero exit code — all threats
                        return 0
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. macOS returns 1 for flagged/beacon/exfil. Windows Run method
                        is void, never sets Environment.ExitCode. CI pipelines reading
                        $LASTEXITCODE treat flagged domain as clean.
- **Location:**         macOS/Sources/SyxxCLI/Commands/SyxxForensics.swift:39-82;
                        Windows/dotNET/Syxx/Commands/ForensicsCommand.cs:10-119
- **Issue:**            Run method is void. All subcommands return via `return;` with no
                        exit code.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Set Environment.ExitCode = 1 in threat branches.
- **Code Sample:**      [

                        // macOS: exit(1) for flagged/beacon/exfil
                        // Windows: void Run() — always exits 0

                        ]

---

- **[D132]**            macOS LANCartographer has zero telemetry instrumentation
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      High. Entire LAN discovery subsystem (~380 lines) has zero spans,
                        zero breadcrumbs, zero OSLog entries. Security-critical feature
                        completely invisible to observability stack.
- **Location:**         macOS/Sources/SyxxKit/LAN/LANCartographer.swift (entire file)
- **Issue:**            snapshot(), diff(), saveSnapshot(), loadSnapshot() — no telemetry.
- **Cross-ref. Delta:** D133-D135 (DimensionClient same pattern).
- **Fix(es):**          [Fix 1] Add SyxxTracer spans to all LAN operations.
- **Code Sample:**      [

                        // LANCartographer.swift — zero SyxxTracer, SyxxDiagnostics,
                        // or OSLog calls in entire file

                        ]

---

- **[D133]**            macOS and Windows DimensionClient — zero spans on all API calls
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      High. Four network operations (authoritative, diff, warpManifest,
                        warpRawAsset) have zero spans on both platforms. SyxxTracer exists
                        in same module but Dimension layer was never instrumented.
- **Location:**         macOS/Sources/SyxxKit/DimensionClient.swift (entire file);
                        Windows/dotNET/Syxx.Kit/DimensionClient.cs (entire file)
- **Issue:**            Server-confirmation subsystem is a telemetry black hole.
- **Cross-ref. Delta:** D132 (LAN same pattern), D134 (Windows bare catches).
- **Fix(es):**          [Fix 1] Add SyxxTracer spans to all Dimension API calls.
- **Code Sample:**      [

                        // DimensionClient — zero SyxxTracer.StartSpan() calls
                        // despite SyxxTracer defined in same assembly

                        ]

---

- **[D134]**            Windows DimensionClient — six bare catch blocks swallow all errors
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      High. TLS failure, DNS error, JSON parse error, socket timeout all
                        produce same output: null. All failure modes indistinguishable.
- **Location:**         Windows/dotNET/Syxx.Kit/DimensionClient.cs:104,145,183,214,239,266
- **Issue:**            Six `catch { return null; }` blocks with no logging, metric, or
                        breadcrumb.
- **Cross-ref. Delta:** D133.
- **Fix(es):**          [Fix 1] Log exception type and message.
                        [Fix 2] Return typed
                        DimensionOutcome instead of null.
- **Code Sample:**      [

                        // DimensionClient.cs — six instances of:
                        catch { return null; }  // no logging, no metric

                        ]

---

- **[D135]**            macOS DimensionService — silent fallback on all failure paths
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. fetchStartup, fetchBroadcast, fetchWhatsNewURL all catch
                        errors and silently fall back to cache/hardcoded default. No
                        breadcrumb or diagnostic signal.
- **Location:**         macOS/Sources/SyxxApp/DimensionService.swift:82-95, 108-117
- **Issue:**            Dimension server unreachable → app appears healthy serving stale data
                        with no telemetry to prove it.
- **Cross-ref. Delta:** D133.
- **Fix(es):**          [Fix 1] Add SyxxDiagnostics.breadcrumb() on all fallback paths.
- **Code Sample:**      [

                        // DimensionService.swift — catch all errors, fallback silently
                        // No breadcrumb, no OSLog, no diagnostic signal

                        ]

---

- **[D136]**            No end-to-end scan trace correlation — no trace_id propagates between
                        client and server
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      High. Server OTel spans have no W3C trace context extraction. Clients
                        never inject trace context into outgoing requests. A user's complete
                        diagnostic flow crosses 6+ network round-trips but cannot be
                        reconstructed as a single trace.
- **Location:**         Cross-cutting (all client and server telemetry)
- **Issue:**            "Why did this user's scan show inconclusive" is operationally
                        impossible to answer from telemetry alone.
- **Cross-ref. Delta:** D087 (run_id always none).
- **Fix(es):**          [Fix 1] Generate trace_id at scan start.
                        [Fix 2] Inject into all
                        outgoing HTTP headers. [Fix 3] Extract on server side.
- **Code Sample:**      [

                        // No trace_id, scan_id, or correlation_id propagates
                        // between client and server anywhere in the codebase

                        ]

---

- **[D137]**            macOS RemoteContentView — server-side Ed25519 signature on What's New
                        HTML is never verified by any client
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. Server signs HTML with collector's Ed25519 key (D041) but
                        neither macOS nor Windows client fetches or verifies the signature.
                        Defense is inert.
- **Location:**         macOS/Sources/SyxxApp/RemoteContentView.swift:369-416;
                        Server/syxx_server/app.py:1195-1198
- **Issue:**            loadContent fetches HTML via URLSession, injects CSS reset, loads
                        into WKWebView — no signature fetched, parsed, or verified.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Implement client-side Ed25519 verification.
                        [Fix 2] Or
                        remove server-side signing and misleading comment.
- **Code Sample:**      [

                        // app.py:1197 — "sign the What's-New HTML with Ed25519"
                        // RemoteContentView.swift:369-416 — no verification anywhere

                        ]

---

- **[D138]**            macOS WelcomeView — unbounded recursive polling timer in scan
                        completion check
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. Recursive asyncAfter with no maximum retry count or timeout.
                        Hung warden → eternal "Scanning..." spinner with no cancel option.
- **Location:**         macOS/Sources/SyxxApp/WelcomeView.swift:196-206
- **Issue:**            checkScanComplete() reschedules every 1s indefinitely.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add max iteration count or monotonic deadline (120s).
                        [Fix 2] Offer retry/skip on timeout.
- **Code Sample:**      [

                        // WelcomeView.swift:196-206
                        DispatchQueue.main.asyncAfter(deadline: .now() + 1.0) {
                            checkScanComplete()  // recursive — no upper bound
                        }

                        ]

---

- **[E054]**            macOS What's New HTML server signature never verified — defense is
                        inert (see D137)
- **Status:**           **DUPLICATE** — See D137.
- **Cross-ref. Delta:** D137.

---

- **[E055]**            Server Warp routes leak internal network topology via error messages
- **Status:**           **DUPLICATE** — See A078.
- **Cross-ref. Delta:** A078.

---

- **[F014]**            DNS collector has no query volume metric — capacity planning impossible
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      High. _serve_dns processes every UDP datagram but never increments
                        a counter. Only dns_rate_limited (rejected) is measured. Cannot
                        determine total queries, responses sent, or leak-zone vs REFUSED ratio.
- **Location:**         Server/syxx_server/dns_authoritative.py:371-438
- **Issue:**            No logging_util.incr("dns_queries_received") or equivalent.
- **Cross-ref. Delta:** F015 (observation metric also missing).
- **Fix(es):**          [Fix 1] Add query/response counters at top of serving loop.
- **Code Sample:**      [

                        # dns_authoritative.py:371-438 — main serving loop
                        # No counter for total queries received

                        ]

---

- **[F015]**            DNS collector has no metric for recorded observations — core business
                        metric unmeasured
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. leak.observe() succeeds but no counter incremented. Cannot
                        determine legitimate observations per unit time.
- **Location:**         Server/syxx_server/dns_authoritative.py:428-433
- **Issue:**            Only rejected queries are counted. Successful observations are not.
- **Cross-ref. Delta:** F014.
- **Fix(es):**          [Fix 1] Add counter after successful leak.observe().
- **Code Sample:**      [

                        # dns_authoritative.py:428-433
                        leak.observe(...)  # success — no counter incremented

                        ]

---

- **[F016]**            macOS @Published SwiftUI invalidation cascade — 63 properties, ~15
                        writes per scan, each triggers full view-tree invalidation
- **Status:**           **OPEN** — Found during deepest delta-analysis 2026-08-09.
- **Criticality:**      Medium. Each @Published write fires objectWillChange.send(),
                        invalidating every observing view. Each await boundary is a new
                        run-loop tick → separate invalidation cycle. ~15-75ms wasted per scan.
- **Location:**         macOS/Sources/SyxxApp/AppModel.swift (all 63 @Published properties)
- **Issue:**            runAllWardens writes ~15 distinct @Published properties across
                        multiple await suspension points.
- **Cross-ref. Delta:** D014 (AppModel God class).
- **Fix(es):**          [Fix 1] Extract per-tool ViewModels.
                        [Fix 2] Use Observable macro
                        (iOS 17+) for fine-grained invalidation.
- **Code Sample:**      [

                        // AppModel.swift — 63 @Published properties
                        // Each write invalidates ALL observing views
                        // Each await boundary = new tick = new invalidation

                        ]

---

---

- **[A084]**            `ResolverConsensusEngine` force-unwraps `dohEndpoints[name]!` — crash
                        when resolver name has no DoH endpoint entry
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      Critical. A missing entry in `dohEndpoints` crashes the entire
                        scan at runtime via Swift trap, producing no verdict and no evidence.
                        Any operator adding a resolver name to `mirrorPrimaryResolvers` without
                        a matching `dohEndpoints` entry triggers this unconditionally.
- **Location:**         macOS/Sources/SyxxKit/ResolverConsensusEngine.swift:179, 181, 258
- **Issue:**            `runLocalDeltaFanOut` and `runUnified` subscript `dohEndpoints` with
                        `!`. The dict has 7 entries; `publicResolvers` has 11 names.
                        If any resolver name in `mirrorPrimaryResolvers` lacks a matching
                        `dohEndpoints` entry the force-unwrap traps at runtime, terminating
                        the scan.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Replace `!` subscript with a nil-guard that returns a
                        `.transportError` outcome when no endpoint is configured for the name.
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/ResolverConsensusEngine.swift:179
                        async let cloudflareDoH = dohOutcome(domain: domain, type: type,
                            endpoint: dohEndpoints["Cloudflare"]!)   // traps if missing

                        // line 258
                        endpoint: dohEndpoints[name]!)               // traps on any unknown name

                        ]

---

- **[C012]**            `MatrixDeterminativeEngine` gates `.untested`/`.locked` sections as
                        `.missingRequiredSection` regardless of `hardGateOnMissingRequiredSections`
                        flag — flag semantics violated for present-but-untested sections
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      Critical. When `hardGateOnMissingRequiredSections` is `false`, a
                        section present in the bundle but with status `.untested` still fires
                        the hard gate — the flag only guards the absent-from-bundle path.
                        A required section that ran but produced no data hard-gates a diagnosis
                        even when the operator has explicitly disabled that gate, silently
                        suppressing valid detections.
- **Location:**         macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift:884-895
- **Issue:**            The `hardGateOnMissingRequiredSections` flag at line 884 guards only
                        the case where the section is absent from the bundle entirely (line 876).
                        A section present in the bundle but `!isUsable` (e.g. `.untested`,
                        `.locked`) bypasses the flag check and always fires the gate with
                        reason `.missingRequiredSection`.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Check `hardGateOnMissingRequiredSections` (or a dedicated
                        `hardGateOnUntestedRequiredSections`) before gating on present-but-
                        untested sections, matching the intent of the flag for absent sections.
- **Code Sample:**      [

                        // MatrixDeterminativeEngine.swift:891-895
                        if !section.status.isUsable {
                            r.gate = GateInfo(reason: .missingRequiredSection,
                                detail: "Required section '...' produced no usable output.")
                            break   // ← fires even when hardGateOnMissingRequiredSections == false
                        }

                        ]

---

- **[C013]**            `AnswerLedger.trimEntriesPreservingLastPerKey` anchor key omits
                        `networkKey` — eviction can delete the only prior anchor for a
                        network-scoped key, masking real DNS drift as first-seen
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      Critical. After eviction, the next `detectDrift` call for that
                        domain/network pair returns `.firstSeen` instead of `.drifted`,
                        directly suppressing a DNS-tampering signal.
- **Location:**         macOS/Sources/SyxxKit/Trust/AnswerLedger.swift:188
- **Issue:**            The C103 anchor key is `"\(e.domain)|\(e.queryType)"` — `networkKey`
                        is excluded. Drift detection at line 162 IS network-scoped:
                        `$0.networkKey == entry.networkKey`. When the global cap fires and the
                        eviction loop runs, entries for one networkKey may be evicted even
                        though they are the only anchor for that `(networkKey, domain, queryType)`
                        triple. On the next `record()` call the ledger sees no prior entry and
                        returns `.firstSeen`.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Include `networkKey` in the anchor key:
                        `"\(e.domain)|\(e.queryType)|\(e.networkKey ?? "")"`.
- **Code Sample:**      [

                        // AnswerLedger.swift:188
                        let key = "\(e.domain)|\(e.queryType)"   // ← networkKey omitted

                        // drift detection at line 162 IS network-scoped:
                        $0.networkKey == entry.networkKey        // ← inconsistency

                        ]

---

- **[D139]**            `Synthesizer.Prophet` maps `ResolverLatencyStatus.Unreachable` to
                        `.degraded` on macOS but `.invalidated` on Windows — cross-platform
                        Prophet section status divergence
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      Critical. `.invalidated` blocks threat diagnoses that require a usable
                        Prophet section (Windows); `.degraded` does not (macOS). An identical
                        run on the two platforms can produce different verdicts when the
                        resolver latency probe is unreachable.
- **Location:**         macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:163-168;
                        Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:207-224
- **Issue:**            macOS `case .slow, .degraded, .unreachable: subs.append(.degraded)`.
                        Windows `case Unreachable: subs.Add(SectionStatus.Invalidated)`.
                        The Windows comment (B193) says macOS should be changed to
                        `.invalidated`, but neither platform has been updated to agree.
- **Cross-ref. Delta:** D012 (this filing supersedes D012 from the parity sub-agent).
- **Fix(es):**          [Fix 1] Decide the canonical mapping. `.invalidated` is semantically
                        sounder (an unreachable probe produced no data). Update macOS
                        `Synthesizer.swift` to map `.unreachable` → `.invalidated`, matching
                        Windows B193 intent.
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:165
                        case .slow, .degraded, .unreachable: subs.append(.degraded)   // macOS

                        // Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:220-221
                        case ResolverLatencyStatus.Unreachable:
                            subs.Add(SectionStatus.Invalidated);                      // Windows

                        ]

---

- **[D140]**            `Synthesizer.Mirror` WARP inconclusive emits `EvidenceId.ScriptMismatch`
                        on Windows (0.75-strength threat indicator) but `scriptMismatchInconclusive`
                        on macOS (unmapped, zero strength) — false threat signal on Windows
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      Critical. An inconclusive WARP check (e.g. CDN 429, upstream timeout)
                        on Windows injects a 0.75-strength `LiveQueryContentRewrite` indicator
                        into the matrix engine, potentially producing a false `ThreatDetected`
                        verdict. macOS correctly contributes zero strength for an inconclusive
                        result.
- **Location:**         macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:352-357;
                        Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:462-468
- **Issue:**            Windows `case WarpConclusion.INCONCLUSIVE` emits
                        `EvidenceId.ScriptMismatch` — the same ID used for a confirmed
                        tampering detection. macOS uses `EvidenceId.scriptMismatchInconclusive`
                        (C199), which is absent from `indicatorMapping` and contributes nothing.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Change Windows `EvidenceId.ScriptMismatch` in the INCONCLUSIVE
                        arm to `EvidenceId.ScriptMismatchInconclusive`, exactly as macOS C199.
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:354-356 (correct)
                        evidenceId: .scriptMismatchInconclusive   // unmapped — zero strength

                        // Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:463 (defective)
                        EvidenceId.ScriptMismatch                 // mapped at 0.75 strength

                        ]

---

- **[D141]**            `FilteringProbe` missing B139 control-resolver cross-check on Windows —
                        false-positive filtering detection for domains that have gone offline
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      Critical. A probe domain that stops resolving globally (de-registered,
                        registry hold) reads as `Active` filtering on Windows — a false
                        positive telling the user their filter is blocking requests that the
                        domain no longer services. macOS demotes these to `Unknown` via B139.
- **Location:**         macOS/Sources/SyxxKit/FilteringProbe.swift:82-101;
                        Windows/dotNET/Syxx.Kit/FilteringProbe.cs:66-74
- **Issue:**            macOS verifies through an independent control resolver that a
                        NXDOMAIN/NODATA result means the domain should resolve before
                        classifying it as sinkholed. Windows `ProbeAsync` has no control-
                        resolver confirmation step at all.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add a `controlResolver` parameter and the B139 cross-check
                        loop to `FilteringProbe.ProbeAsync` in Windows.
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/FilteringProbe.swift:92-99 (correct)
                        if initial == .sinkholed {
                            let controlResp = await DNSResolver.queryUDPDetailed(
                                DNSQuery(name: domain, type: 1, server: controlResolver))
                            if controlResp.status == .nxDomain || controlResp.status == .noData {
                                perDomain[domain] = .unknown; continue
                            }
                        }

                        // Windows/dotNET/Syxx.Kit/FilteringProbe.cs:66-74 (missing cross-check)
                        perDomain[domain] = Classify(resp);   // ← no control-resolver step

                        ]

---

- **[D142]**            `SynthesisEngine.indicatorMapping` missing `exfilSuspicious` on Windows —
                        suspicious DNS exfiltration patterns produce zero indicator strength
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      High. A capture returning suspicious-but-unconfirmed exfil patterns
                        contributes 0.35-strength `LocalEnvironmentCompromised` on macOS but
                        zero on Windows. A Windows user with suspicious DNS activity sees a
                        weaker matrix determination than a macOS user in identical conditions.
- **Location:**         macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift:291-292;
                        Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs:310
- **Issue:**            macOS `indicatorMapping` maps `.exfilSuspicious → (.localEnvironmentCompromised, 0.35)`.
                        Windows `IndicatorMapping` contains only `ExfilLikely` (0.60) with no
                        `ExfilSuspicious` entry.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add `[EvidenceId.ExfilSuspicious] = (SyxxIndicator.LocalEnvironmentCompromised, 0.35)`
                        to the Windows `IndicatorMapping` dictionary.
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift:291-292
                        .exfilSuspicious: (.localEnvironmentCompromised, 0.35),   // present

                        // Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs:310
                        // ExfilSuspicious entry is absent                         // missing

                        ]

---

- **[D143]**            `DNSResponse.DidFail` excludes `RcodeOther` on Windows — unhandled
                        RCODEs (FORMERR, NOTIMP, BADVERS) reported as non-failure
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      High. A resolver returning RCODE 4 (NOTIMP) is treated as a
                        non-failure on Windows, allowing a probe to proceed on a response
                        that signifies the resolver cannot answer. macOS correctly maps
                        unhandled RCODEs to `rcodeOther` with `didFail = true`.
- **Location:**         macOS/Sources/SyxxKit/DNSResolver.swift:267-271;
                        Windows/dotNET/Syxx.Kit/DNSResolver.cs:169-175
- **Issue:**            Windows `DidFail` property lists `Timeout, TransportError,
                        MalformedResponse, ResponseIdMismatch, Truncated, ServFail, Refused`
                        but omits `RcodeOther`. Additionally, Windows parser emits
                        `TransportError` for unknown RCODEs while macOS emits `RcodeOther`,
                        so the two platforms use different status values for the same wire
                        condition.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add `or DNSResponseStatus.RcodeOther` to Windows `DidFail`.
                        [Fix 2] Update the Windows parser to emit `RcodeOther` (not
                        `TransportError`) for non-zero, non-handled RCODEs.
- **Code Sample:**      [

                        // Windows/dotNET/Syxx.Kit/DNSResolver.cs:169-175
                        public bool DidFail => Status is DNSResponseStatus.Timeout
                            or DNSResponseStatus.TransportError
                            ...
                            or DNSResponseStatus.Refused;
                            // ← RcodeOther absent

                        ]

---

- **[D144]**            `LeakDetector` empty-servers returns `.inconclusive` on Windows but
                        `.error` on macOS — divergent Glutton section status
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      High. macOS maps `LeakResult.error` → Glutton `.invalidated`.
                        Windows maps `.inconclusive` differently. Identical physical condition
                        (no DNS resolvers configured) produces different section status and
                        matrix attenuation across platforms.
- **Location:**         macOS/Sources/SyxxKit/LeakDetector.swift:291-295;
                        Windows/dotNET/Syxx.Kit/LeakDetector.cs:334-342
- **Issue:**            No resolvers configured: macOS returns `LeakResult.error`;
                        Windows returns `LeakResult.Inconclusive`. The macOS approach is
                        semantically correct — nothing was dispatched, so this is an
                        execution failure, not an ambiguous measurement.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Change Windows empty-servers early-return to
                        `LeakResult.Error` to match macOS and align Glutton section status.
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/LeakDetector.swift:291-295
                        return LeakTestResult(result: .error, ...)   // macOS

                        // Windows/dotNET/Syxx.Kit/LeakDetector.cs:337-341
                        return new LeakTestResult { Result = LeakResult.Inconclusive, ... }  // Windows

                        ]

---

- **[D145]**            `Synthesizer.Prophet` `DenialStatus.NotADenial` adds clean support on
                        Windows (`.negative`) but is skipped on macOS (B061) — inflates
                        Prophet completeness on Windows
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      High. Adding `.negative` for `NotADenial` (the domain resolved —
                        the check doesn't apply) inflates the Prophet section's sub-count,
                        potentially pushing a borderline `inconclusive` Prophet section to
                        `negative` on Windows with no macOS counterpart.
- **Location:**         macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:231-232;
                        Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:306-308
- **Issue:**            macOS B061: `case .notADenial: break` — contributes nothing to
                        subs. Windows B195 comment contradicts B061 and adds
                        `subs.Add(SectionStatus.Negative)` for `NotADenial`.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Remove `subs.Add(SectionStatus.Negative)` from the Windows
                        `NotADenial` arm and correct the B195 comment to align with macOS B061.
- **Code Sample:**      [

                        // macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:231-232
                        case .notADenial: break   // contributes nothing — B061

                        // Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:306-308
                        case DenialStatus.NotADenial:
                            subs.Add(SectionStatus.Negative);   // ← inflates completeness

                        ]

---

- **[E056]**            `_route_checkout` uses raw `self.client_address[0]` (socket peer, always
                        `127.0.0.1` behind nginx) for Stripe idempotency key — all users in
                        the same hour window share one Stripe checkout session
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      Critical. Two users requesting the same SKU in the same 1-hour window
                        both receive the same Stripe session URL. User B is directed to User A's
                        payment page. This is a session-fixation variant exposing payment and
                        billing details.
- **Location:**         Server/syxx_server/app.py:1308-1310
- **Issue:**            `client_ip = self.client_address[0]` is the raw TCP socket peer —
                        always `127.0.0.1` when behind nginx. Every checkout request in the
                        same window for the same SKU produces the identical idempotency key.
                        Stripe deduplicates on this key and returns the first session to all
                        subsequent callers.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Replace `self.client_address[0]` with `self._client_ip()`
                        (the existing helper that reads X-Forwarded-For), matching all other
                        routes.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:1308-1310
                        window = int(time.time()) // 3600
                        client_ip = self.client_address[0]    # ← ALWAYS 127.0.0.1 behind nginx
                        idem_key = f"checkout-{sku}-{hashlib.sha256(client_ip.encode()).hexdigest()[:16]}-{window}"

                        ]

---

- **[E057]**            Free-tier leak-test verify path uses `allow_any_subject=True` — DNS
                        eavesdropper can claim another user's leak token, read their resolver
                        PII, and consume (DoS) the token
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      Critical. DNS is cleartext. A passive eavesdropper observing
                        `<label>.leak.syxx.app` queries can call `/api/leaktest/check?token=<label>`
                        to read User A's resolver IPs (PII) and atomically consume the token,
                        preventing User A from ever receiving their result. The paid-tier path
                        is correctly subject-gated; the free path is not.
- **Location:**         Server/syxx_server/app.py:2396-2416
- **Issue:**            `_route_leaktest_check` passes `allow_any_subject=True` to
                        `leak.verify`, bypassing token ownership entirely. Any caller who
                        knows the label (readable from DNS) can drain the result.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Bind the free-tier token to the issuing client IP at creation
                        time and verify it at check time (without `allow_any_subject=True`).
                        [Fix 2] Alternatively, require a short-lived HMAC challenge proving
                        knowledge of the token before returning results.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:2407-2410
                        result, label = leak.verify(token, client_ip, allow_any_subject=True)
                        # ↑ ownership check bypassed — any caller with the label wins

                        ]

---

- **[E058]**            `_ACTIVATE_FAILURES` reset path sets `count = 0` but retains the
                        original `first_time` anchor — lockout threshold can never fire;
                        brute-force proceeds indefinitely
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      Critical. An attacker submitting `threshold - 1` failed activation
                        attempts, waiting for `_ACTIVATE_LOCKOUT_WINDOW` to expire, then
                        repeating gets the counter reset to 0 on every cycle. The lockout
                        threshold is never reached. License key brute-force proceeds at
                        `(threshold - 1) / window` = 9 guesses per 15 minutes indefinitely.
- **Location:**         Server/syxx_server/app.py:1641-1648
- **Issue:**            When `elapsed >= _ACTIVATE_LOCKOUT_WINDOW`, the increment path
                        sets `count = 0` but writes `(count + 1, first_time)` — the
                        `first_time` anchor is the original failure time, now in the past.
                        The next call computes `elapsed >= window` again (still true),
                        resets again, and the lockout never fires.
- **Cross-ref. Delta:** A012 (same dict — restart resets; this is the within-session bypass).
- **Fix(es):**          [Fix 1] On window expiry, write `(1, time.time())` — a fresh
                        anchor and a fresh count of 1 for the current attempt, not 0.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:1641-1648
                        count, first_time = self._ACTIVATE_FAILURES[client_ip]
                        elapsed = time.time() - first_time
                        if elapsed >= self._ACTIVATE_LOCKOUT_WINDOW:
                            count = 0   # ← count reset but first_time stays in the past
                        self._ACTIVATE_FAILURES[client_ip] = (count + 1, first_time)
                        # next call: elapsed is still >= window → resets again indefinitely

                        ]

---

- **[E059]**            Server OTel spans emit verbatim queried domain names to the OTLP
                        collector — DNS investigation requests are browsing-history PII
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      Critical. Per SyxxDiagnostics.swift:42-44: "A DNS query stream IS
                        a browsing history." The server emits the exact domain to a
                        third-party OTLP endpoint on every `/api/investigate`,
                        `/api/authoritative`, `/api/diff`, `/api/trace`, and `/api/baseline`
                        request with no redaction path.
- **Location:**         Server/syxx_server/app.py:2019, 2044, 2091, 2176, 2237
- **Issue:**            `span.set_attribute("investigate.domain", domain)` and similarly
                        named attributes on other routes emit the verbatim user-supplied
                        hostname. The server has no equivalent of `TelemetryRedaction` from
                        SyxxKit. The OTLP export bypasses `_emit`/`_redact_emails`.
- **Cross-ref. Delta:** E060 (client IP also in spans).
- **Fix(es):**          [Fix 1] Apply server-side domain redaction: emit label-count,
                        name-length, and `is_idn` flag instead of the raw hostname. Add an
                        allowlist of Syxx canary domains safe to emit verbatim.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:2019
                        span.set_attribute("investigate.domain", domain)  # ← verbatim user hostname

                        ]

---

- **[E060]**            Server OTel spans emit verbatim client IP address on `/api/exposure`
                        and `/api/leaktest/start` — real public IP is PII
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      Critical. The resolved real client IP is written to `exposure.ip`
                        and `leak.client_ip` span attributes and exported to the OTLP
                        endpoint unconditionally. This correlates a specific IP address with
                        the security queries a user is making.
- **Location:**         Server/syxx_server/app.py:2280, 2390
- **Issue:**            `span.set_attribute("exposure.ip", ip)` and
                        `span.set_attribute("leak.client_ip", client_ip)` emit the full
                        IPv4/IPv6 address. The OTLP pipeline has no redaction step.
- **Cross-ref. Delta:** E059 (domain PII in spans).
- **Fix(es):**          [Fix 1] Replace with a one-way hash or emit only the IP class
                        (`"public_ipv4"`, `"private"`, `"loopback"`).
- **Code Sample:**      [

                        # Server/syxx_server/app.py:2280
                        span.set_attribute("exposure.ip", ip)          # ← verbatim public IPv4

                        # Server/syxx_server/app.py:2390
                        span.set_attribute("leak.client_ip", client_ip)  # ← verbatim IP

                        ]

---

- **[E061]**            `HomeBaseline.save` writes without HMAC protection; `loadOutcome`
                        reads without `IntegrityTag.verify` — home baseline is forgeable by
                        a local attacker
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      High. A local attacker who can write `~/.syxx/home-baseline.json`
                        can substitute any chosen baseline, causing `HomeBaseline.compare`
                        to report `matchesHome: true` for an attacker-controlled config —
                        suppressing all legitimate diff signals with no detection.
- **Location:**         macOS/Sources/SyxxKit/Trust/Sentinel.swift:209-213, 223-240
- **Issue:**            `HomeBaseline.save` writes raw JSON without `IntegrityTag.signAndWrite`.
                        `HomeBaseline.loadOutcome` reads without `IntegrityTag.verify`.
                        Contrast with `Sentinel.saveSnapshot`/`loadSnapshotOutcome` which
                        correctly apply HMAC on both save and load.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Apply `IntegrityTag.signAndWrite` in `HomeBaseline.save`.
                        [Fix 2] Apply `IntegrityTag.verify` in `HomeBaseline.loadOutcome`,
                        treating a verification failure as absent (not trusted).
- **Code Sample:**      [

                        // Sentinel.swift:209-213 — HomeBaseline.save (missing HMAC)
                        try JSONEncoder().encode(snapshot).write(to: url, options: .atomic)
                        try? FileManager.default.setAttributes([.posixPermissions: 0o600], ...)
                        // ← no IntegrityTag.signAndWrite

                        // Sentinel.swift:90 — Sentinel.saveSnapshot (correct)
                        try IntegrityTag.signAndWrite(data, for: url)   // ← HMAC protected

                        ]

---

- **[B049]**            `server.investigate` OTel span always ends with `StatusCode.UNSET` —
                        investigation failures are indistinguishable from successes in APM
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      High. A systematic failure in `investigate.investigate()` (resolver
                        bug, dependency outage) produces zero APM signal. `span.end()` is
                        called before the `if err:` branch, so the span is already closed
                        before the failure is tested.
- **Location:**         Server/syxx_server/app.py:2017-2026
- **Issue:**            The `server.investigate` span is started and ended unconditionally
                        with no status, outcome attribute, or error classification. Error
                        paths are unreachable from the span because `span.end()` fires first.
- **Cross-ref. Delta:** E059 (same span also leaks domain PII).
- **Fix(es):**          [Fix 1] Set `span.set_status(StatusCode.ERROR, err)` before ending
                        the span on the error path. Move `span.end()` to a `finally` block
                        or use a context manager.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:2021-2026
                        result, err = investigate.investigate(domain)
                        if span:
                            span.end()                   # ← ends before error is tested
                        if err:
                            return self._json_error(400, err)

                        ]

---

- **[B050]**            `server.warp` span is never ended on `BlockedError` or `FetchError` —
                        leaked-open spans accumulate unboundedly in the `BatchSpanProcessor`
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      High. Repeated fetch failures (persistent block-list mis-classification,
                        CDN outages) cause unbounded span accumulation in the OTel
                        `BatchSpanProcessor`, a slow memory leak with zero error rate signal
                        visible in the collector.
- **Location:**         Server/syxx_server/app.py:1784-1793
- **Issue:**            The `server.warp` span is started inside the `try` block. On
                        `BlockedError` and `FetchError` exception paths, neither `span.end()`
                        nor `span.set_status` is called. Spans are also never ended on
                        the deprecated `build_baseline` path when it raises.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Wrap the span lifetime in a `finally` block. Set
                        `span.set_status(StatusCode.ERROR, ...)` in each except clause.
- **Code Sample:**      [

                        # Server/syxx_server/app.py:1784-1793
                        try:
                            span = _OTEL_TRACER.start_span("server.warp") if _OTEL_TRACER else None
                            result = warp.build_baseline(target)
                            if span: span.end()          # ← only on success
                        except fetcher.BlockedError as e:
                            return self._json_error(400, …)   # span leaks
                        except fetcher.FetchError as e:
                            return self._json_error(502, …)   # span leaks

                        ]

---

- **[B051]**            `ContinuousMonitor.sampleDNSAndRecord` silently swallows all DNS
                        transport errors — probe failure mode invisible, no span, no diagnostic
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      High. A transport bug causing `queryUDPDetailed` to throw (not just
                        timeout-status) silently produces `0/3 succeeded` with no per-domain
                        error class, no OTel event, and no `SyxxDiagnostics` breadcrumb.
                        A systematic probe regression is invisible at the individual-failure
                        level. The monitoring tick path has no OTel span at all.
- **Location:**         macOS/Sources/SyxxKit/Trust/ContinuousMonitor.swift:359-361
- **Issue:**            `catch { // DNS failure for a sample domain must not crash the loop }`
                        — the entire catch body is a comment. No diagnostic signal is emitted.
                        No span wraps `sampleDNSAndRecord` or `tickOnce`.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Add a `SyxxDiagnostics.breadcrumb` call in the catch block
                        with domain and `type(of: error)`. [Fix 2] Wrap `sampleDNSAndRecord`
                        in a `SyxxTracer.span("monitor.dns_sample_round")` span with
                        `attempted` and `succeeded` attributes.
- **Code Sample:**      [

                        // ContinuousMonitor.swift:359-361
                        } catch {
                            // DNS failure for a sample domain must not crash the loop
                            // ← no breadcrumb, no span, no counter
                        }

                        ]

---

- **[F017]**            `_recv_exact` in `resolver.py` accumulates TCP response bytes with
                        `buf += chunk` — O(n²) allocation on fragmented reads
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      Non-critical. For a 16 KB DNS-over-TCP response received in
                        256-byte chunks (64 iterations), ~512 KB of byte copying occurs
                        instead of 16 KB. Under concurrent TCP fallback load this degrades
                        latency and increases peak RSS.
- **Location:**         Server/syxx_server/resolver.py:416-423
- **Issue:**            `buf += chunk` allocates a new `bytes` object of
                        `len(buf) + len(chunk)` on every iteration. `bytes` is immutable
                        in Python so every `+=` copies all previously accumulated bytes.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Replace with a `bytearray`/`memoryview` accumulator using
                        `recv_into` for zero-copy accumulation and a single final `bytes(buf)`
                        conversion.
- **Code Sample:**      [

                        # Server/syxx_server/resolver.py:416-423
                        buf = b""
                        while len(buf) < n:
                            chunk = sock.recv(n - len(buf))
                            if not chunk: raise DNSError("connection closed")
                            buf += chunk   # ← O(n²): new allocation + copy per iteration

                        ]

---

- **[F018]**            `ContinuousMonitor.sampleDNSAndRecord` resolves three sample domains
                        serially — worst-case 3× the per-domain timeout per monitoring tick
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      Non-critical. On a captive portal or throttled network each domain
                        can timeout independently (~2 s each), pinning the monitoring tick
                        for up to 6 s. Concurrent queries collapse this to ~2 s.
- **Location:**         macOS/Sources/SyxxKit/Trust/ContinuousMonitor.swift:322-362
- **Issue:**            `for domain in sampleDomains` iterates three domains sequentially
                        inside a single `async` function. There is no `withTaskGroup` or
                        `async let` fan-out. The tick budget is `3 × query_timeout` in the
                        worst case.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Replace the `for` loop with a `withTaskGroup` fan-out,
                        running all three queries concurrently and collecting results.
- **Code Sample:**      [

                        // ContinuousMonitor.swift:322-330
                        for domain in sampleDomains {   // ← sequential, not concurrent
                            attempted += 1
                            do {
                                ...
                                response = await DNSResolver.queryUDPDetailed(
                                    DNSQuery(name: domain, type: 1, server: firstResolver))
                            }
                        }

                        ]

---

- **[F019]**            `resolver.py` `resolve_authoritative` fires synchronous DNSKEY round-
                        trip inside answer path — adds up to 3 s to every authoritative lookup
- **Status:**           **OPEN** — Found during delta-analysis (deep) 2026-08-10.
- **Criticality:**      High. `_zone_is_signed` is called unconditionally in the return-dict
                        construction after the authoritative answer is already found. On a
                        slow or unresponsive nameserver this adds the full `timeout=3 s` to
                        the caller's authoritative API response. The server handler thread is
                        pinned for the full duration.
- **Location:**         Server/syxx_server/resolver.py:1002
- **Issue:**            `"dnskeyObserved": _zone_is_signed(zone, used_ip, timeout)` is a
                        synchronous socket call embedded in a dict literal. It is issued
                        after the walk has already completed. The remaining deadline budget
                        (`deadline` parameter) is not shared with this call — it uses a
                        fresh `timeout` argument.
- **Cross-ref. Delta:** None.
- **Fix(es):**          [Fix 1] Return `dnskeyObserved: None` and let the caller issue
                        the DNSKEY query concurrently. [Fix 2] At minimum, cap the
                        `_zone_is_signed` call with the remaining walk deadline rather
                        than a fresh 3 s budget.
- **Code Sample:**      [

                        # Server/syxx_server/resolver.py:1002
                        "dnskeyObserved": _zone_is_signed(zone, used_ip, timeout),
                        # ↑ synchronous round-trip after answer found — adds up to 3 s

                        ]

---
