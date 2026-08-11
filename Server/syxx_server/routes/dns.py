"""DNS investigation route methods — DnsRoutes mixin."""

import concurrent.futures
import json
import time

from .. import config, investigate, logging_util, passivedns, resolver

try:
    from .. import dnssec_validator
except ImportError:
    dnssec_validator = None

from .base import (
    _OTEL_STATUS_ERROR,
    _OTEL_TRACER,
    _clean_vantage_answers,
    _diff_content,
    _diff_result,
    _redact_domain,
)


class DnsRoutes:
    """DNS investigation routes: investigate, authoritative, diff, trace, baseline, passive, dnssec."""

    def _route_investigate(self, query):
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        domain = (query.get("domain", [""])[0]).strip()
        # A066: validate at route level (mirrors _route_authoritative) so the error
        # message is consistent and garbage never reaches investigate().
        if not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        span = _OTEL_TRACER.start_span("server.investigate") if _OTEL_TRACER else None
        if span:
            # A050: only record the domain on the span AFTER validation passes, so
            # attacker-controlled garbage never lands in the span attribute.
            span.set_attribute("investigate.domain", _redact_domain(domain))
        result, err = investigate.investigate(domain)
        if span:
            span.end()
        if err:
            return self._json_error(400, err)
        logging_util.incr("investigations")
        self._send(200, result, extra_headers={"Cache-Control": "public, max-age=60"})

    def _route_authoritative(self, query):
        """Ground-truth oracle (Workstream S #1): walk root -> TLD -> authoritative for a name and
        return the zone's own answer + the delegation chain, so the app can diff its local answer
        against data an interfering resolver cannot fake."""
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        qtype_str = (query.get("type", ["A"])[0]).strip().upper()
        qtypes = {"A": resolver.TYPE_A, "AAAA": resolver.TYPE_AAAA, "NS": resolver.TYPE_NS}
        if qtype_str not in qtypes:
            return self._json_error(400, "unsupported type (A, AAAA, NS)")
        span = _OTEL_TRACER.start_span("server.authoritative") if _OTEL_TRACER else None
        if span:
            span.set_attribute("dns.domain", _redact_domain(domain))
            span.set_attribute("dns.type", qtype_str)
        try:
            # A066: bound the walk with a shared wall-clock deadline.
            deadline = time.monotonic() + config.AUTH_WALK_DEADLINE_SECONDS
            result = resolver.resolve_authoritative(domain, qtypes[qtype_str], deadline=deadline)
        except resolver.DNSError as e:
            if span:
                span.set_attribute("error", str(e))
                # A056: mark the span as errored so the collector does not see
                # error-path spans as successful.
                if _OTEL_STATUS_ERROR is not None:
                    span.set_status(_OTEL_STATUS_ERROR)
                span.end()
            return self._json_error(502, f"resolution failed: {e}")
        if span:
            span.end()
        result["type"] = qtype_str
        logging_util.incr("authoritative_queries")
        self._send(200, result, extra_headers={"Cache-Control": "public, max-age=60"})

    def _route_diff(self, query):
        """Inside/outside diff (Workstream S #2): the app POSTs what IT saw for a name; the server
        returns the authoritative ground truth plus its own clean-vantage recursive answer and an
        assessment. A divergence the authority does not serve is on-path interception / tampering.

        A024 NOTE: The DNS-answer comparison below (subset → match, disjoint → divergent,
        intersection → partial) operates on IP address sets.  The content half
        (_diff_content, called further down) compares script URLs and inline hashes
        with foreign-host detection.  Different data types, different semantics —
        intentionally not unified."""
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        if not self._require_json():
            return
        raw = self._body()
        if self._body_error(raw):
            return
        try:
            payload = json.loads(raw or b"{}")
        except ValueError:
            return self._json_error(400, "invalid JSON body")
        domain = (payload.get("domain") or "").strip()
        if not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        qtype_str = (payload.get("type") or "A").strip().upper()
        qtypes = {"A": resolver.TYPE_A, "AAAA": resolver.TYPE_AAAA}
        if qtype_str not in qtypes:
            return self._json_error(400, "unsupported type (A, AAAA)")
        local = payload.get("localAnswers")
        if not isinstance(local, list) or len(local) > 64 or not all(isinstance(x, str) for x in local):
            return self._json_error(400, "localAnswers must be a list of up to 64 strings")
        local_set = {a.strip().lower() for a in local if a.strip()}

        span = _OTEL_TRACER.start_span("server.diff") if _OTEL_TRACER else None
        if span:
            span.set_attribute("dns.domain", _redact_domain(domain))
            span.set_attribute("dns.type", qtype_str)
        # A038: a resolution failure must surface as inconclusive, not propagate as a crash/500.
        # A046: run the authoritative walk and clean-vantage query concurrently
        # instead of sequentially — they are independent DNS resolutions.
        deadline = time.monotonic() + config.AUTH_WALK_DEADLINE_SECONDS

        def _auth_walk():
            return resolver.resolve_authoritative(domain, qtypes[qtype_str], deadline=deadline)

        def _clean_query():
            return _clean_vantage_answers(domain, qtypes[qtype_str], qtype_str)

        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            auth_future = pool.submit(_auth_walk)
            clean_future = pool.submit(_clean_query)
            deadline_remaining = max(deadline - time.monotonic(), 0.1)
            try:
                auth = auth_future.result(timeout=deadline_remaining)
            except resolver.DNSError:
                auth = {"conclusion": "unreachable"}
            except (concurrent.futures.TimeoutError, Exception):
                auth = {"conclusion": "unreachable"}
            try:
                clean_answers = clean_future.result(timeout=deadline_remaining)
            except resolver.DNSError:
                # A142 Fix 3: type must match every other path (set, not dict).
                clean_answers = set()
            except (concurrent.futures.TimeoutError, Exception):
                clean_answers = set()
        auth_answers = {a.lower() for a in (auth.get("authoritative") or {}).get("answers", [])}
        auth_usable = auth.get("conclusion") == "authoritative" and bool(auth_answers)

        reference = auth_answers if auth_usable else clean_answers
        if not reference or not local_set:
            match, assessment = None, "inconclusive"
        elif local_set <= reference:
            match, assessment = True, "match"
        elif local_set.isdisjoint(reference):
            match, assessment = False, "divergent" if auth_usable else "partial"
        else:
            match, assessment = False, "partial"

        content = None
        url = (payload.get("url") or "").strip()
        if url:
            if not (url.startswith("http://") or url.startswith("https://")):
                return self._json_error(400, "url must be http(s)")
            local_html = payload.get("localHTML")
            if local_html is not None and not isinstance(local_html, str):
                return self._json_error(400, "localHTML must be a string")
            if isinstance(local_html, str) and len(local_html) > 500_000:
                return self._json_error(413, "localHTML too large")
            local_scripts = payload.get("localScripts") if isinstance(payload.get("localScripts"), list) else None
            local_inline = (
                payload.get("localInlineScriptHashes")
                if isinstance(payload.get("localInlineScriptHashes"), list)
                else None
            )
            content = _diff_content(url, local_html, local_scripts, local_inline)

        result = _diff_result(assessment, content, clean_answers, local_set)
        if span:
            span.set_attribute("diff.assessment", assessment)
            span.set_attribute("diff.conclusion", result)
            span.end()
        logging_util.incr("diffs")
        response = {
            "domain": domain,
            "type": qtype_str,
            "localAnswers": sorted(local_set),
            "authoritative": {"answers": sorted(auth_answers), "conclusion": auth.get("conclusion")},
            "cleanVantage": {"answers": sorted(clean_answers)},
            # A142 Fix 1: derive the label from the same predicate as the selection —
            # auth_usable, not auth_answers alone. When the authority answered but the walk
            # did not reach an authoritative conclusion, the evidence used was cleanVantage
            # and the label must say so.
            "reference": "authoritative" if auth_usable else ("cleanVantage" if clean_answers else "none"),
            "match": match,
            "assessment": assessment,
            "conclusion": result,
        }
        if content is not None:
            response["content"] = content
        self._send(200, response, extra_headers={"Cache-Control": "no-store"})

    def _route_trace(self, query):
        """DNS trace / hierarchy diagnostics (Workstream S #3): surfaces the root->TLD->authoritative
        delegation walk as a diagnostic, so a user can see where resolution breaks or mismatches."""
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        qtype_str = (query.get("type", ["A"])[0]).strip().upper()
        qtypes = {"A": resolver.TYPE_A, "AAAA": resolver.TYPE_AAAA, "NS": resolver.TYPE_NS}
        if qtype_str not in qtypes:
            return self._json_error(400, "unsupported type (A, AAAA, NS)")
        span = _OTEL_TRACER.start_span("server.trace") if _OTEL_TRACER else None
        if span:
            span.set_attribute("dns.domain", _redact_domain(domain))
            span.set_attribute("dns.type", qtype_str)
        # A066: bound the walk with a shared wall-clock deadline.
        deadline = time.monotonic() + config.AUTH_WALK_DEADLINE_SECONDS
        # A152: wrap resolve_authoritative in try/except like _route_authoritative
        # does (SD-41 fix). A DNSError propagating to the catch-all produced a
        # dishonest 500 "internal error" when the truth is "the delegation chain
        # did not answer." For this route, a partial trace is the diagnostic
        # itself — return 200 with result "unreachable" and brokenAt naming the
        # zone that failed, matching what the docstring promises ("where resolution breaks").
        try:
            result = resolver.resolve_authoritative(domain, qtypes[qtype_str], deadline=deadline)
        except resolver.DNSError as e:
            if span:
                span.set_attribute("error", str(e))
                # A056: mark the span as errored so the collector does not see
                # error-path spans as successful.
                if _OTEL_STATUS_ERROR is not None:
                    span.set_status(_OTEL_STATUS_ERROR)
                span.end()
            logging_util.incr("traces")
            return self._send(
                200,
                {
                    "domain": domain,
                    "type": qtype_str,
                    "delegation": [],
                    "conclusion": "unreachable",
                    "brokenAt": None,
                    "error": str(e),
                },
            )
        # A152: use .get for "delegation" to handle partial results gracefully.
        delegation = result.get("delegation", [])
        broken = next((d.get("zone") for d in delegation if d.get("error")), None)
        if span:
            span.set_attribute("trace.conclusion", result.get("conclusion", "unknown"))
            span.end()
        logging_util.incr("traces")
        self._send(
            200,
            {
                "domain": domain,
                "type": qtype_str,
                "delegation": delegation,
                "conclusion": result.get("conclusion", "unknown"),
                "brokenAt": broken,
            },
        )

    def _route_baseline(self, query):
        """Historical baseline / drift (Workstream S #4): the server's memory layer — what a domain
        has resolved to over time, so the app can spot a value that changed after months of stability.
        Reads the passive-DNS store the server already populates; supporting context, never a result."""
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        rtype = (query.get("type", [""])[0]).strip().upper() or None
        if rtype and rtype not in ("A", "AAAA", "CNAME", "NS", "MX", "TXT"):
            return self._json_error(400, "unsupported type")
        span = _OTEL_TRACER.start_span("server.baseline") if _OTEL_TRACER else None
        if span:
            span.set_attribute("dns.domain", _redact_domain(domain))
            span.set_attribute("dns.type", rtype or "any")
        result = passivedns.baseline(domain, rtype)
        if span:
            span.end()
        logging_util.incr("baselines")
        self._send(200, result, extra_headers={"Cache-Control": "public, max-age=60"})

    def _route_passive(self, query):
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain:
            return self._json_error(400, "missing domain")
        # Pagination support: parse page/limit from query string.
        from .base import _paginate

        result = passivedns.query(domain)
        records = result.get("records", [])
        paged, meta = _paginate(query, records)
        result["records"] = paged
        result["pagination"] = meta
        self._send(200, result)

    def _route_dnssec(self, query):
        """Free-tier DNSSEC chain validator. Walks root→TLD→domain verifying DS/DNSKEY/RRSIG."""
        if not self._rate_ok(None):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain or not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        if dnssec_validator is None:
            return self._json_error(501, "DNSSEC validation unavailable (cryptography library not installed)")
        try:
            result = dnssec_validator.validate_chain(domain)
        except Exception as e:
            logging_util.error("dnssec_validation_failed", domain=_redact_domain(domain), err=str(e))
            return self._json_error(502, f"DNSSEC validation failed: {e}")
        logging_util.incr("dnssec_validations")
        self._send(200, result)
