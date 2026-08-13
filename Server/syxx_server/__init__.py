"""Syxx server backend package.

A dependency-free (stdlib-only) implementation of the Syxx server that backs the
paid, server-reliant app features (Warp, Leak) and the paywalled web query API.

Modules:
    config              env-driven configuration
    logging_util        structured JSON logging + metrics counters
    db                  sqlite persistence (WAL, thread-safe helpers)
    licensing           Keygen validation + HMAC session tokens + entitlement
    ratelimit           per-subject token-bucket limiter
    fetcher             SSRF-safe outbound HTTP fetch gateway
    resolver            self-contained DNS client (UDP + TCP fallback)
    dns_authoritative   authoritative UDP DNS server + leak collector
    warp                Warp structural/hash comparison logic
    leak                signed one-time leak-token issuance + verification
    investigate         multi-vantage web investigation query
    passivedns          passive-DNS ingestion + query index
    app                 threaded HTTP server, router, auth middleware
"""

__version__ = "2.0.0"
