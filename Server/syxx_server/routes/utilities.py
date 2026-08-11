"""Free-tier public utility route methods — UtilityRoutes mixin."""

import concurrent.futures
import ipaddress
import time

from .. import (
    config,
    email_auth,
    firewall_test,
    investigate,
    logging_util,
    resolver,
    subdomain_enum,
    whois_lookup,
)
from .base import (
    _PROPAGATION_POOL,
    _redact_domain,
)


class UtilityRoutes:
    """Free-tier public utility routes: lookup, reverse_dns, benchmark, whois,
    propagation, email_auth, subdomains, firewall_test."""

    def _route_lookup(self, query):
        """Free-tier DNS lookup: query a resolver for any record type. Rate-limited per IP,
        no entitlement required. Uses the existing resolver.query() infrastructure."""
        if not self._rate_ok(None):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain or len(domain) > 253:
            return self._json_error(400, "missing or invalid domain")
        if not investigate.valid_domain(domain):
            return self._json_error(400, "invalid domain format")

        type_str = (query.get("type", ["A"])[0]).strip().upper()
        type_map = {
            "A": resolver.TYPE_A,
            "AAAA": resolver.TYPE_AAAA,
            "NS": resolver.TYPE_NS,
            "CNAME": resolver.TYPE_CNAME,
            "SOA": resolver.TYPE_SOA,
            "MX": resolver.TYPE_MX,
            "TXT": resolver.TYPE_TXT,
            "CAA": resolver.TYPE_CAA,
            "PTR": resolver.TYPE_PTR,
            "SRV": resolver.TYPE_SRV,
        }
        qtype = type_map.get(type_str)
        if qtype is None:
            return self._json_error(400, "unsupported record type")

        server = (query.get("server", [""])[0]).strip()
        if not server:
            server = config.PUBLIC_RESOLVERS[0] if config.PUBLIC_RESOLVERS else "1.1.1.1"
        # A141: restrict ?server= to the allowlist of known public resolvers to prevent
        # using this endpoint as an arbitrary DNS proxy/scanner.
        if server not in config.PUBLIC_RESOLVERS:
            return self._json_error(400, "server must be in the allowed resolver list")

        try:
            result = resolver.query(domain, qtype, server, timeout=config.INVESTIGATE_TIMEOUT)
        except resolver.DNSError as e:
            return self._json_error(502, f"DNS query failed: {e}")

        logging_util.incr("lookups")
        self._send(
            200,
            {
                "domain": domain,
                "type": type_str,
                "server": server,
                "rcode": result.get("rcode", -1),
                "rcodeName": resolver.rcode_name(result.get("rcode", -1)),
                "ad": result.get("ad", False),
                "records": result.get("records", []),
                "truncated": result.get("truncated", False),
            },
            extra_headers={"Cache-Control": "public, max-age=60"},
        )

    def _route_reverse_dns(self, query):
        """Free-tier reverse DNS lookup. Converts IP to arpa name and queries PTR."""
        if not self._rate_ok(None):
            return
        ip_str = (query.get("ip", [""])[0]).strip()
        if not ip_str:
            return self._json_error(400, "missing ip parameter")
        try:
            arpa = resolver.ip_to_arpa(ip_str)
        except resolver.DNSError as e:
            return self._json_error(400, str(e))

        server = config.PUBLIC_RESOLVERS[0] if config.PUBLIC_RESOLVERS else "1.1.1.1"
        try:
            result = resolver.query(arpa, resolver.TYPE_PTR, server, timeout=config.INVESTIGATE_TIMEOUT)
        except resolver.DNSError as e:
            return self._json_error(502, f"reverse DNS query failed: {e}")

        ptr_records = [r["value"] for r in result.get("records", []) if r["type"] == "PTR"]
        logging_util.incr("reverse_dns_lookups")
        self._send(
            200,
            {
                "ip": ip_str,
                "arpa": arpa,
                "ptr": ptr_records[0] if ptr_records else None,
                "allPtr": ptr_records,
                "rcode": result.get("rcode", -1),
            },
        )

    def _route_benchmark(self, query):
        """Free-tier DNS benchmark: time resolution across public resolvers."""
        if not self._rate_ok(None):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain or not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "missing or invalid domain")

        type_str = (query.get("type", ["A"])[0]).strip().upper()
        type_map = {
            "A": resolver.TYPE_A,
            "AAAA": resolver.TYPE_AAAA,
            "NS": resolver.TYPE_NS,
            "MX": resolver.TYPE_MX,
            "TXT": resolver.TYPE_TXT,
            "CNAME": resolver.TYPE_CNAME,
        }
        qtype = type_map.get(type_str, resolver.TYPE_A)

        try:
            rounds = min(10, max(1, int(query.get("rounds", ["3"])[0])))
        except (ValueError, TypeError):
            rounds = 3

        resolver_labels = {
            "1.1.1.1": "Cloudflare",
            "8.8.8.8": "Google",
            "9.9.9.9": "Quad9",
            "208.67.222.222": "OpenDNS",
            "64.6.64.6": "Verisign",
        }
        servers = config.PUBLIC_RESOLVERS[:6]
        results_by_server = {}
        deadline_start = time.monotonic()
        _BENCHMARK_DEADLINE = 15.0  # S3: aggregate wall-clock cap
        truncated = False  # A135: track whether we stopped early due to deadline

        for server in servers:
            elapsed_total = time.monotonic() - deadline_start
            if elapsed_total > _BENCHMARK_DEADLINE:
                truncated = True  # A135: mark that we stopped early
                break  # S3: stop probing when the aggregate deadline is exceeded
            entries = []
            for _ in range(rounds):
                # A135: check deadline INSIDE the inner loop so we don't run unbounded
                remaining_budget = _BENCHMARK_DEADLINE - (time.monotonic() - deadline_start)
                if remaining_budget <= 0:
                    truncated = True
                    break
                t0 = time.monotonic()
                # A135: derive each query's timeout from remaining budget (capped at 5s)
                query_timeout = min(5.0, remaining_budget)
                try:
                    result = resolver.query(domain, qtype, server, timeout=query_timeout)
                    elapsed = (time.monotonic() - t0) * 1000
                    entries.append(
                        {
                            "time_ms": round(elapsed, 2),
                            "rcode": result.get("rcode", -1),
                            "recordCount": len(result.get("records", [])),
                        }
                    )
                except resolver.DNSError as e:
                    elapsed = (time.monotonic() - t0) * 1000
                    entries.append(
                        {
                            "time_ms": round(elapsed, 2),
                            "rcode": -1,
                            "error": str(e),
                            "recordCount": 0,
                        }
                    )
            # A135: if we broke out of the inner loop due to deadline, mark truncation
            if len(entries) < rounds:
                truncated = True
            successes = [e for e in entries if int(e["rcode"]) >= 0]
            results_by_server[server] = {
                "server": server,
                "label": resolver_labels.get(server, server),
                "results": entries,
                "avg_ms": round(sum(float(e["time_ms"]) for e in successes) / len(successes), 2) if successes else None,
                "min_ms": round(min(float(e["time_ms"]) for e in successes), 2) if successes else None,
                "max_ms": round(max(float(e["time_ms"]) for e in successes), 2) if successes else None,
                "failures": len(entries) - len(successes),
            }

        resolver_list = list(results_by_server.values())
        ranked = sorted([r for r in resolver_list if r["avg_ms"] is not None], key=lambda r: r["avg_ms"])
        logging_util.incr("benchmarks")
        self._send(
            200,
            {
                "domain": domain,
                "type": type_str,
                "rounds": rounds,
                "resolvers": resolver_list,
                "fastest": ranked[0]["server"] if ranked else None,
                "slowest": ranked[-1]["server"] if ranked else None,
                # A135: report truncation honestly so clients know results are partial
                "truncated": truncated,
                "serversTested": len(results_by_server),
                "serversRequested": len(servers),
            },
        )

    def _route_whois(self, query):
        """Free-tier WHOIS/RDAP lookup. Rate-limited per IP, no entitlement required.
        Uses RDAP as primary (structured JSON), falls back to raw WHOIS on port 43.
        The IANA RDAP bootstrap is cached in memory for 24 hours (never fetched per-request)."""
        if not self._rate_ok(None):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain or not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        try:
            result = whois_lookup.lookup(domain)
        except Exception as e:
            logging_util.error("whois_failed", domain=_redact_domain(domain), err=str(e))
            return self._json_error(502, f"WHOIS lookup failed: {e}")
        logging_util.incr("whois_lookups")
        self._send(200, result, extra_headers={"Cache-Control": "public, max-age=60"})

    def _route_propagation(self, query):
        """Free-tier DNS propagation checker: query many global resolvers concurrently.
        Uses concurrent.futures with a 12-second aggregate deadline. The pool.shutdown
        cancel_futures parameter requires Python 3.9+; on older versions we skip it."""
        if not self._rate_ok(None):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain or not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")

        type_str = (query.get("type", ["A"])[0]).strip().upper()
        type_map = {
            "A": resolver.TYPE_A,
            "AAAA": resolver.TYPE_AAAA,
            "NS": resolver.TYPE_NS,
            "MX": resolver.TYPE_MX,
            "TXT": resolver.TYPE_TXT,
            "CNAME": resolver.TYPE_CNAME,
        }
        qtype = type_map.get(type_str)
        if qtype is None:
            return self._json_error(400, "unsupported record type")

        resolvers = config.PROPAGATION_RESOLVERS

        def query_one(entry):
            t0 = time.monotonic()
            try:
                result = resolver.query(domain, qtype, entry["ip"], timeout=4)
                elapsed = (time.monotonic() - t0) * 1000
                return {
                    "server": entry["ip"],
                    "label": entry["label"],
                    "region": entry["region"],
                    "status": "ok",
                    "records": result.get("records", []),
                    "time_ms": round(elapsed, 1),
                }
            except resolver.DNSError as e:
                elapsed = (time.monotonic() - t0) * 1000
                return {
                    "server": entry["ip"],
                    "label": entry["label"],
                    "region": entry["region"],
                    "status": "error",
                    "error": str(e),
                    "time_ms": round(elapsed, 1),
                }

        # A136: use the module-level shared pool instead of per-request creation.
        # Cap resolvers per request to stay within the shared pool budget.
        resolvers_to_use = resolvers[: _PROPAGATION_POOL._max_workers]
        futures = [_PROPAGATION_POOL.submit(query_one, r) for r in resolvers_to_use]
        results = []
        try:
            for fut in concurrent.futures.as_completed(futures, timeout=12):
                # A136 Fix 3: wrap fut.result() so one socket error doesn't discard
                # all already-collected results.
                try:
                    results.append(fut.result())
                except Exception:  # noqa: S110 — best-effort path; failing here must not take the request down
                    pass
        except concurrent.futures.TimeoutError:
            pass

        # Compute consensus
        all_values = set()
        for r in results:
            if r["status"] == "ok":
                for rec in r.get("records", []):
                    all_values.add(rec.get("value", ""))
        success_count = sum(1 for r in results if r["status"] == "ok")
        total = len(resolvers)
        propagation_pct = round(success_count / total * 100, 1) if total else 0

        if success_count == 0:
            # Zero resolvers answered, so `all_values` is empty and `len(all_values) <= 1` would
            # have declared "consistent" — agreement asserted over no reads at all, alongside
            # successCount: 0. Nothing was measured, so nothing is consistent.
            consensus = "inconclusive"
        elif len(all_values) <= 1:
            consensus = "consistent"
        elif len(all_values) <= 3:
            consensus = "mostly_consistent"
        else:
            consensus = "divergent"

        logging_util.incr("propagation_checks")
        self._send(
            200,
            {
                "domain": domain,
                "type": type_str,
                "resolvers": sorted(results, key=lambda r: r["label"]),
                "consensus": consensus,
                "uniqueValues": sorted(all_values),
                "totalResolvers": total,
                "successCount": success_count,
                "propagationPct": propagation_pct,
            },
        )

    def _route_email_auth(self, query):
        """Free-tier SPF/DMARC/DKIM email authentication checker. Rate-limited per IP."""
        if not self._rate_ok(None):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain or not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        dkim_selector = (query.get("selector", [""])[0]).strip() or None
        try:
            result = email_auth.check(domain, dkim_selector=dkim_selector)
        except Exception as e:
            logging_util.error("email_auth_failed", domain=_redact_domain(domain), err=str(e))
            return self._json_error(502, f"email auth check failed: {e}")
        logging_util.incr("email_auth_checks")
        self._send(200, result)

    def _route_subdomains(self, query):
        """Free-tier subdomain enumeration via Certificate Transparency logs. Rate-limited per IP."""
        if not self._rate_ok(None):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain or not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        try:
            result = subdomain_enum.enumerate(domain)
        except Exception as e:
            logging_util.error("subdomain_enum_failed", domain=_redact_domain(domain), err=str(e))
            return self._json_error(502, f"subdomain enumeration failed: {e}")
        logging_util.incr("subdomain_enumerations")
        self._send(200, result)

    def _route_firewall_test(self, query):
        """Free-tier DNS firewall/filter detection test. Rate-limited per IP.
        A141: self-scoped by construction — only tests the caller's own IP, never an
        arbitrary target, so this can never be used as a scanner."""
        if not self._rate_ok(None):
            return
        # A141: removed caller-supplied ?resolver= parameter to make this self-scoped.
        # Always test the connection's source IP, matching /api/exposure's design.
        resolver_ip = self._client_ip()
        try:
            addr = ipaddress.ip_address(resolver_ip)
            if not resolver._ip_is_public(str(addr)):
                return self._json_error(400, "client IP is not a public address")
        except ValueError:
            return self._json_error(400, "invalid client IP address")
        try:
            result = firewall_test.test(resolver_ip)
        except Exception as e:
            logging_util.error("firewall_test_failed", resolver=resolver_ip, err=str(e))
            return self._json_error(502, f"firewall test failed: {e}")
        logging_util.incr("firewall_tests")
        self._send(200, result)
