#!/usr/bin/env python3
"""Patch dns_authoritative.py to extract tokens from hyphenated subdomains."""
import sys

path = "/opt/SyxxServer/syxx_server/dns_authoritative.py"
with open(path) as f:
    code = f.read()

old1 = "leak_hit = (labels[0], TYPE_A)"
new1 = """first = labels[0]
            leak_hit = (first.split("-", 1)[0] if "-" in first else first, TYPE_A)"""

old2 = "leak_hit = (labels[0], TYPE_AAAA)"
new2 = """first = labels[0]
            leak_hit = (first.split("-", 1)[0] if "-" in first else first, TYPE_AAAA)"""

count = code.count(old1) + code.count(old2)
if count == 0:
    print("Already patched or patterns not found")  # noqa: T201
    sys.exit(0)

code = code.replace(old1, new1)
code = code.replace(old2, new2)

with open(path, "w") as f:
    f.write(code)

print("Patched %d replacement(s)" % count)  # noqa: T201, UP031
