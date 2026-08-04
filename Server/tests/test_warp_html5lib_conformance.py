"""WARP_MANIFEST_V1 §10 conformance — server HTML asset enumerator vs. browser ground truth.

Because the client no longer parses (the server is the single parser), server-vs-client parser
divergence is structurally impossible. The only residual is server-parser-vs-BROWSER *coverage*:
does warp's stdlib `html.parser`-based enumerator list what a browser actually loads on initial
page load? This suite MEASURES that against a vendored html5lib-tests-derived corpus and prints an
honest xfail ledger. 100% green is NOT the goal — `html.parser` cannot be fully spec-conformant
(no <template>/<noscript> content models, no entity decoding in attributes); the ledger is the
honest statement of that coverage gap.

Contract enforced:
  * every case whose xfail flag is false MUST match the browser ground truth (a regression guard);
  * every case whose xfail flag is true is EXPECTED to diverge — if the stdlib parser happens to
    match anyway that is recorded as an "unexpected pass" (never a failure);
  * a case that diverges WITHOUT being marked xfail fails the suite — an undocumented gap is not
    allowed to slip in silently.
"""
import json
import os
import pathlib
import unittest

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
from syxx_server import warp  # noqa: E402

_DATA = pathlib.Path(__file__).resolve().parent / "conformance" / "data" / "html5lib_asset_cases.json"


def _load():
    with open(_DATA, encoding="utf-8") as f:
        return json.load(f)


def _actual_assets(html, base_url):
    """What warp's enumerator would list for this HTML — [[url, class], ...] in document order."""
    ordered = warp._enumerate_manifest_assets(html.encode("utf-8"), base_url)
    return [[url, cls] for (url, cls) in ordered]


class Html5libConformanceTests(unittest.TestCase):
    def test_measured_conformance_and_xfail_ledger(self):
        data = _load()
        base = data["base_url"]
        cases = data["cases"]

        passed, xfailed, xpassed = [], [], []
        undocumented = []   # diverged but NOT marked xfail → a regression / new gap
        for case in cases:
            expected = [list(a) for a in case["expected_assets"]]
            actual = _actual_assets(case["html"], base)
            matches = actual == expected
            if case.get("xfail"):
                (xpassed if matches else xfailed).append(case["name"])
            else:
                if matches:
                    passed.append(case["name"])
                else:
                    undocumented.append((case["name"], expected, actual))

        total = len(cases)
        print("\n=== WARP_MANIFEST_V1 html5lib conformance ledger ===")
        print(f"  total cases          : {total}")
        print(f"  conformant (pass)    : {len(passed)}")
        print(f"  known-divergent xfail: {len(xfailed)}  {sorted(xfailed)}")
        print(f"  unexpected xpass     : {len(xpassed)}  {sorted(xpassed)}")
        print(f"  undocumented gaps    : {len(undocumented)}")
        for name, exp, act in undocumented:
            print(f"    - {name}: expected {exp} got {act}")

        # Regression guard: a non-xfail case that diverges is an undocumented gap — fail.
        self.assertEqual(undocumented, [], "undocumented parser divergence — update the ledger")
        # Ledger completeness: every case is accounted for exactly once.
        self.assertEqual(len(passed) + len(xfailed) + len(xpassed) + len(undocumented), total)
        # The corpus must actually exercise both conformant and divergent behavior, or it is not
        # measuring the gap it claims to.
        self.assertGreater(len(passed), 0, "corpus records no conformant cases")
        self.assertGreater(len(xfailed) + len(xpassed), 0, "corpus records no divergence cases")


if __name__ == "__main__":
    unittest.main()
