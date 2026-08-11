"""DNS-over-HTTPS route methods — DohRoutes mixin."""

import base64

from .. import config, logging_util, resolver


class DohRoutes:
    """DNS-over-HTTPS routes: doh_get, doh_post, and internal helpers."""

    # Name→number for the JSON convenience format's ?type=. Small on purpose: the wireformat path
    # carries any type; JSON is a convenience for the common ones. A numeric ?type= is also accepted.
    _DOH_JSON_TYPES = {
        "A": 1,
        "NS": 2,
        "CNAME": 5,
        "SOA": 6,
        "PTR": 12,
        "MX": 15,
        "TXT": 16,
        "AAAA": 28,
        "SRV": 33,
        "HTTPS": 65,
    }

    def _doh_gate(self, query):
        """Shared gate: entitlement (paywalled), optional IP allowlist, rate limit.
        Returns (entitlement, ok). Sends the 402/403/429 itself when not ok."""
        ent, ok = self._require(query, config.ENFORCE_DOH)
        if not ok:
            return None, False
        if config.DOH_ALLOWLIST and self._client_ip() not in config.DOH_ALLOWLIST:
            # Wording pending approval (D-1 approval gate) — logged to outputs-catalog.csv.
            self._json_error(403, "forwarder not enabled for this address")
            return None, False
        if not self._rate_ok(ent):
            return None, False
        return ent, True

    def _doh_forward(self, query_wire):
        """Forward raw query bytes to each PUBLIC_RESOLVER in turn; return raw response bytes, or
        None if all upstreams failed. Passthrough — never re-encoded (resolver.forward_wire)."""
        for server in config.PUBLIC_RESOLVERS:
            try:
                return resolver.forward_wire(query_wire, server, config.DOH_UPSTREAM_TIMEOUT)
            except resolver.DNSError as e:
                logging_util.warn("doh_upstream_failed", server=server, err=str(e))
                continue
        return None

    def _route_doh_get(self, query):
        """SEC-041: Enforce query size limit on GET DoH requests. The dns= parameter
        is base64url-encoded wire format; limit it to prevent abuse via oversized queries."""
        ent, ok = self._doh_gate(query)
        if not ok:
            return
        dns_param = query.get("dns", [""])[0]
        if dns_param:
            # SEC-041: Limit base64url-encoded DNS query size (512 bytes wire = ~684 base64)
            # A standard DNS/UDP query is 512 bytes max; EDNS0 extends to ~1232 typical.
            # Cap at 2048 base64 chars (~1536 wire bytes) for generous EDNS0 headroom.
            if len(dns_param) > 2048:
                return self._json_error(413, "DNS query too large")
            # base64url, no padding (RFC 8484). Restore padding before decode.
            try:
                pad = "=" * (-len(dns_param) % 4)
                wire = base64.urlsafe_b64decode(dns_param + pad)
            except Exception:  # noqa: S110
                return self._json_error(400, "invalid dns= parameter (want base64url)")
            return self._doh_respond_wire(wire)
        if query.get("name", [""])[0]:
            return self._doh_respond_json(query)
        return self._json_error(400, "provide ?dns= (wireformat) or ?name= (json)")

    def _route_doh_post(self, query):
        """SEC-042: Enforce body size limit on POST DoH requests. DNS wire format
        over TCP can be up to 65535 bytes, but practical queries are much smaller."""
        ent, ok = self._doh_gate(query)
        if not ok:
            return
        ctype = (self.headers.get("Content-Type", "").split(";")[0]).strip().lower()
        if ctype != "application/dns-message":
            return self._json_error(415, "POST requires Content-Type: application/dns-message")
        # SEC-042: Check Content-Length before reading body
        content_length = self.headers.get("Content-Length")
        if content_length:
            try:
                cl = int(content_length)
                if cl > 4096:  # 4KB max — generous for any practical DNS query
                    return self._json_error(413, "DNS query body too large")
            except ValueError:
                return self._json_error(400, "invalid Content-Length")
        raw = self._body()
        if self._body_error(raw):
            return
        if not raw:
            return self._json_error(400, "empty DNS query body")
        # SEC-042: Double-check actual size after reading
        if len(raw) > 4096:
            return self._json_error(413, "DNS query body too large")
        return self._doh_respond_wire(raw)

    def _doh_respond_wire(self, wire):
        if len(wire) < 12:
            return self._json_error(400, "DNS query too short")
        resp = self._doh_forward(wire)
        if resp is None:
            # Every upstream failed. 502, never 200-with-empty: "we could not resolve" must not read
            # as "no such record".
            return self._json_error(502, "all upstream resolvers failed")
        self._send(200, resp, "application/dns-message")

    def _doh_respond_json(self, query):
        name = (query.get("name", [""])[0]).strip()
        type_param = (query.get("type", ["A"])[0]).strip().upper()
        qtype = self._DOH_JSON_TYPES.get(type_param)
        if qtype is None:
            try:
                qtype = int(type_param)
            except ValueError:
                return self._json_error(400, f"unknown type {type_param!r}")
        # A124: iterate ALL configured resolvers (not just [0]) so a single poisoned
        # resolver cannot dictate the answer. Collect answers from each and apply
        # 2-of-3 corroboration before returning, matching the wireformat path's
        # standard (_clean_vantage_answers / A027).
        servers = list(config.PUBLIC_RESOLVERS[:3])
        if not servers:
            return self._json_error(502, "no upstream resolvers configured")
        parsed_results = []
        last_error = None
        for server in servers:
            try:
                parsed = resolver.query(name, qtype, server, timeout=config.DOH_UPSTREAM_TIMEOUT)
                parsed_results.append(parsed)
            except resolver.DNSError as e:
                last_error = e
                logging_util.warn("doh_json_upstream_failed", name=name, server=server, err=str(e))
                continue
        if not parsed_results:
            # All upstreams failed. Emit Google-JSON Status:2 (SERVFAIL) under HTTP 200 —
            # never Status:0 with empty answers (a fake clean). Wireformat keeps HTTP 502
            # because an empty dns-message body would look like a truncated success.
            logging_util.warn(
                "doh_json_all_upstreams_failed", name=name, err=str(last_error) if last_error else "unknown"
            )
            return self._send(
                200,
                {
                    "Status": 2,
                    "TC": False,
                    "RD": True,
                    "AD": False,
                    "Question": [{"name": name, "type": qtype}],
                    "Comment": "upstream query failed",
                },
            )
        # Use the first successful result; with corroboration, if multiple resolvers
        # returned results, prefer the answer set confirmed by >=2 vantages.
        chosen = parsed_results[0]
        if len(parsed_results) >= 2:
            # _TYPE_NAMES maps numeric types to strings; reverse-map the type_param
            # to match the record's "type" field (which is the formatted string name).
            answer_sets = []
            for p in parsed_results:
                aset = frozenset(str(r["value"]).lower() for r in p.get("records", []) if r.get("type") == type_param)
                answer_sets.append(aset)
            # Pick the answer set that appears most often (>=2 agreement preferred).
            from collections import Counter

            counts = Counter(answer_sets)
            best_set, best_count = counts.most_common(1)[0]
            if best_count >= 2:
                # Find the parsed result matching the corroborated set.
                for p in parsed_results:
                    pset = frozenset(
                        str(r["value"]).lower() for r in p.get("records", []) if r.get("type") == type_param
                    )
                    if pset == best_set:
                        chosen = p
                        break
        # The parser returns {rcode, truncated, ad, records:[{type,value}]} — no TTL is captured, so
        # the JSON omits it rather than fabricate one. `ad` is the only DNSSEC signal we actually
        # observed; RA/CD are not measured, so they are not claimed.
        answers = [{"name": name, "type": qtype, "data": r["value"]} for r in chosen.get("records", [])]
        self._send(
            200,
            {
                "Status": chosen.get("rcode", 0),
                "TC": chosen.get("truncated", False),
                "RD": True,
                "AD": chosen.get("ad", False),
                "Question": [{"name": name, "type": qtype}],
                "Answer": answers,
            },
        )
