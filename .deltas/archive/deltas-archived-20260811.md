# Archived Deltas — 2026-08-11

Automatically archived on 2026-08-11. These 185 deltas were verified by CI
during the delta remediation cycle and have been removed from the active DELTAS.md.

---

## **A. Server Components**

- **[A001]**            Passive DNS drift boolean fallback collapses tri-state
- **Status:**           **fixed-unverified** — Verified in source. passivedns.py:123 still emits
                        `"driftSuspected": drift == "suspected"`. The comment says "backward-compat
                        bool alongside tri-state" (A117), but legacy clients reading only the
                        boolean cannot distinguish "insufficient-data" from "none" — both collapse
                        to false, violating the no-result-before-evidence rule the comment itself
                        cites.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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
- **Status:**           **fixed-unverified** — Found during warp content-integrity multi-reviewer
                        audit
                        2026-08-09. urlsplit does not perform IDN-to-punycode conversion.
                        caf\xc3\xa9.com and xn--caf-dma.com produce different hostnames, each consuming
                        a separate budget slot.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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
- **Status:**           **fixed-unverified** — Found during warp content-integrity multi-reviewer
                        audit
                        2026-08-09. /api/pubkey returns a base64-of-first-8-bytes key_id; warp
                        manifest uses lowercase hex of full 32-byte key.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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


## **F. Miscellaneous**

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


- **[A013]**            IDN domain corruption in DNSSEC validator — `encode("ascii","replace")`
- **Status:**           **fixed-unverified** — Found during codebase-cleanup tech-debt scan
                        2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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
- **Status:**           **fixed-unverified** — Found during codebase-cleanup tech-debt scan
                        2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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
- **Status:**           **fixed-unverified** — Found during codebase-cleanup tech-debt scan
                        2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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


- **[A032]**            cryptography is 9 major versions behind — exposed to CVE-2026-69249
                        (CVSS 8.7) and multiple OpenSSL CVEs
- **Status:**           **fixed-unverified** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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
- **Status:**           **fixed-unverified** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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
- **Status:**           **fixed-unverified** — Found during codebase-cleanup deps-audit 2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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
- **Status:**           **fixed-unverified** — Found during deep delta-analysis performance audit
                        2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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
- **Status:**           **fixed-unverified** — Found during deep delta-analysis observability audit
                        2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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
- **Status:**           **fixed-unverified** — Found during the completed five-agent audit
                        2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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


- **[A069]**            RRL pruning is dead code — _rrl_buckets grows unbounded (memory leak)
- **Status:**           **fixed-unverified** — Found during fast delta-analysis 2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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


- **[A084]**            `ResolverConsensusEngine` force-unwraps `dohEndpoints[name]!` — crash
                        when resolver name has no DoH endpoint entry
- **Status:**           **fixed-unverified** — Found during delta-analysis (deep) 2026-08-10.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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


## **C. Verdict & Evidence Integrity**

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


## **F. Miscellaneous**

- **[C002]**            WitnessWireJson.cs — IEEE-754 ULP-nudging date math is fragile
                        precision-critical code
- **Status:**           **fixed-unverified** — Found during codebase-cleanup tech-debt scan
                        2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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
- **Status:**           **fixed-unverified** — Found during codebase-cleanup tech-debt scan
                        2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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


- **[C006]**            C# Assemble drops hard-excluded diagnoses from the clean-blocking
                        overhang — false `.clean` verdict on Windows (B163 parity gap)
- **Status:**           **fixed-unverified** — Found during delta analysis (-deep) of feat/1 branch
                        2026-08-09. macOS preserves B163; the C# port's Assemble still strips
                        HardExcluded from the overhang used to block a clean verdict.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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
- **Status:**           **fixed-unverified** — Found during delta analysis (-deep) of feat/1 branch
                        2026-08-09. Parity agent P2.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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
- **Status:**           **fixed-unverified** — Found during the completed five-agent audit
                        2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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
- **Status:**           **fixed-unverified** — Found during the completed five-agent audit
                        2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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


- **[C010]**            Windows CaptureEngine BothFailed produces Completed outcome via empty
                        Task.WhenAll — capture never started but reported as success
- **Status:**           **fixed-unverified** — Found during deepest delta-analysis 2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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
- **Status:**           **fixed-unverified** — Found during deepest delta-analysis 2026-08-09.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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
- **Status:**           **fixed-unverified** — Found during delta-analysis (deep) 2026-08-10.
- **Fixed by:**         opencode
- **Fixed at:**         2026-08-10T13:12:56.189168+00:00
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


## **E. Security & Hardening**

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


