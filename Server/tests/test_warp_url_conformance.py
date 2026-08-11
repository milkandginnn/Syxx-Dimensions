"""WARP_MANIFEST_V1 §10 conformance — server URL resolver vs. WHATWG URL Standard.

warp resolves every asset URL with urllib.parse.urljoin (via warp._resolve). A browser resolves
with the WHATWG URL Standard. This suite MEASURES where the two disagree against a vendored
web-platform-tests url/ -derived corpus and prints an honest xfail ledger. stdlib urljoin is a
pre-WHATWG resolver (no backslash-as-slash for special schemes, no tab/newline stripping, no
leading-control trim, no host lowercasing), so 100% green is NOT the goal — the ledger is the
honest statement of the coverage gap.

Contract enforced (same discipline as the html5lib suite):
  * xfail=false cases MUST match the WHATWG ground truth (regression guard);
  * xfail=true cases are EXPECTED to diverge; matching anyway is an "unexpected pass", not a failure;
  * a divergence WITHOUT an xfail marker fails the suite (no silent, undocumented gap).
"""

import json
import os
import pathlib

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
from syxx_server import warp  # noqa: E402

_DATA = pathlib.Path(__file__).resolve().parent / "conformance" / "data" / "wpt_url_cases.json"


def _load():
    with open(_DATA, encoding="utf-8") as f:
        return json.load(f)


def test_measured_conformance_and_xfail_ledger():
    cases = _load()["cases"]

    passed, xfailed, xpassed = [], [], []
    undocumented = []
    for case in cases:
        expected = case.get("expected_href")
        actual = warp._resolve(case["base"], case["input"])
        matches = actual == expected
        if case.get("xfail"):
            (xpassed if matches else xfailed).append(case["name"])
        else:
            if matches:
                passed.append(case["name"])
            else:
                undocumented.append((case["name"], expected, actual))

    total = len(cases)
    print("\n=== WARP_MANIFEST_V1 WPT url conformance ledger ===")
    print(f"  total cases          : {total}")
    print(f"  conformant (pass)    : {len(passed)}")
    print(f"  known-divergent xfail: {len(xfailed)}  {sorted(xfailed)}")
    print(f"  unexpected xpass     : {len(xpassed)}  {sorted(xpassed)}")
    print(f"  undocumented gaps    : {len(undocumented)}")
    for name, exp, act in undocumented:
        print(f"    - {name}: expected {exp!r} got {act!r}")

    assert undocumented == [], "undocumented URL-resolution divergence — update the ledger"
    assert len(passed) + len(xfailed) + len(xpassed) + len(undocumented) == total
    assert len(passed) > 0, "corpus records no conformant cases"
    assert len(xfailed) + len(xpassed) > 0, "corpus records no divergence cases"
