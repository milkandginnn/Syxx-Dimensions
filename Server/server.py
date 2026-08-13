#!/usr/bin/env python3
"""Syxx Server daemon entrypoint.

Starts the authoritative DNS server (leak collector) on its own thread and the
threaded HTTP API/website server in the foreground. All behaviour lives in the
``syxx_server`` package; this file just wires the two listeners together.

Run:  python3 server.py
Configure via environment (see syxx_server/config.py and README.md).
"""
import os
import sys
import threading

# A177: belt-and-braces — every file this process creates (DB, backups, logs,
# temp files) inherits (mode & ~umask). Setting 0o077 means nothing lands
# group- or world-readable by accident, even if a call site forgets to chmod.
os.umask(0o077)

# Ensure the package is importable when launched as ``python3 server.py`` from
# any working directory (systemd sets WorkingDirectory, but be resilient).
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from syxx_server import (app, config, db, dns_authoritative, dot_listener,  # noqa: E402
                         logging_util, preflight)


def main():
    db.init()
    logging_util.info("startup", version=__import__("syxx_server").__version__,
                      http_port=config.HTTP_PORT, dns_port=config.DNS_PORT,
                      enforce_native=config.ENFORCE_NATIVE,
                      leak_zones=config.LEAK_ZONES)

    # Say at BOOT what will not work, rather than at the customer. Licensing fails closed by
    # design, so an unprovisioned host denies every valid key with nothing in the log explaining
    # why. Never blocks startup: DNS/analysis works fine with zero commerce config.
    preflight.log_commerce_readiness(logging_util)

    dns_thread = threading.Thread(target=dns_authoritative.run, daemon=True,
                                  name="dns-server")
    dns_thread.start()

    # D-2: DoT listener, additive and off by default. On any setup failure it marks itself `down`
    # in the registry and returns — it never blocks the HTTP server or the UDP collector.
    if config.ENABLE_DOT:
        threading.Thread(target=dot_listener.run, daemon=True, name="dot-server").start()

    try:
        app.serve()
    except KeyboardInterrupt:
        logging_util.info("shutdown")


if __name__ == "__main__":
    main()
