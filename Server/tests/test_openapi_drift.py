"""OpenAPI spec <-> live-route drift gate (warp-hardening_20260811, Tasks 2.1-2.3).

The live route set is derived mechanically from the route dispatch in
``syxx_server/app.py`` (AST-based, never a hand-maintained list). The
comparison is over (METHOD, path) pairs, not path alone — a path-only
comparison cannot see a path that is documented for one HTTP method but live
under another. That gap was real: ``/api/leaktest/start`` is dispatched from
both ``do_POST`` (app.py:896-897) and ``_dispatch_get`` (app.py:1027-1028),
both calling the same handler, ``_route_leaktest_start`` (app.py:2121), but
``docs/openapi.yaml`` documented only ``post:``. These tests make drift fail
the test suite, not only CI:

  - the spec must document every live (method, path) pair and nothing more,
  - the negative tests prove the gate binds (the checker does not vacuously
    pass): dropping one live (method, path) pair from a COPY of the spec is
    reported as missing, and an invented (method, path) pair is reported as
    extra.

The real spec file is never mutated.
"""

from __future__ import annotations

import os
import sys

import yaml

# The drift checker is a standalone script under Server/tools/; insert that
# directory on sys.path the same way other server tests insert Server/.
_TOOLS_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "tools")
if _TOOLS_DIR not in sys.path:
    sys.path.insert(0, _TOOLS_DIR)

from check_openapi_drift import check  # noqa: E402

_SERVER_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
_APP_PATH = os.path.join(_SERVER_ROOT, "syxx_server", "app.py")
_SPEC_PATH = os.path.join(_SERVER_ROOT, "docs", "openapi.yaml")


def _read_spec():
    """Load the real spec in memory (never written back to disk)."""
    with open(_SPEC_PATH, encoding="utf-8") as f:
        return yaml.safe_load(f)


def _write_spec(tmp_path, spec):
    spec_file = tmp_path / "openapi.yaml"
    with open(spec_file, "w", encoding="utf-8") as f:
        yaml.safe_dump(spec, f, sort_keys=False)
    return str(spec_file)


class TestOpenAPIDrift:
    def test_spec_matches_live_methods(self):
        """The widened gate: comparison is over (METHOD, path) pairs."""
        live, missing, extra = check(_APP_PATH, _SPEC_PATH)
        assert live, "live route set is empty — checker cannot run"
        problems = []
        if missing:
            problems.append(f"live (method, path) pairs missing from spec: {sorted(missing)}")
        if extra:
            problems.append(f"spec (method, path) pairs with no live route: {sorted(extra)}")
        assert not missing and not extra, "; ".join(problems)

    def test_get_leaktest_start_is_live_and_documented(self):
        """Regression pin for the gap this widened gate exists to catch.

        Before this change, ``check()`` returned bare paths, so a path-only
        comparison could not tell that ``/api/leaktest/start`` is live under
        BOTH GET (app.py _dispatch_get:1027-1028) and POST (app.py
        do_POST:896-897) while openapi.yaml documented only POST. Asserting
        membership of the (METHOD, path) pair itself — not just the path —
        is what makes that gap visible.
        """
        live, missing, _ = check(_APP_PATH, _SPEC_PATH)
        assert ("GET", "/api/leaktest/start") in live, "expected GET /api/leaktest/start to be live"
        assert ("POST", "/api/leaktest/start") in live, "expected POST /api/leaktest/start to be live"
        assert ("GET", "/api/leaktest/start") not in missing

    def test_checker_detects_missing_method_on_documented_path(self, tmp_path):
        """Deleting one live HTTP method from an otherwise-documented path
        (not the whole path) must be reported as missing. This is the exact
        shape of the historical /api/leaktest/start gap: the path was
        present in the spec, just missing one of its live methods.
        """
        live, _, _ = check(_APP_PATH, _SPEC_PATH)
        multi_method: dict[str, set[str]] = {}
        for method, route in live:
            multi_method.setdefault(route, set()).add(method)
        route, methods = next((r, m) for r, m in multi_method.items() if len(m) > 1)
        victim_method = sorted(methods)[0]
        spec = _read_spec()
        del spec["paths"][route][victim_method.lower()]
        spec_file = _write_spec(tmp_path, spec)
        _, missing, _ = check(_APP_PATH, spec_file)
        assert (victim_method, route) in missing, (
            f"deleting {victim_method} {route} from the spec copy must be reported as missing"
        )

    def test_checker_detects_missing_route(self, tmp_path):
        live, _, _ = check(_APP_PATH, _SPEC_PATH)
        victim_method, victim_route = sorted(live)[0]
        spec = _read_spec()
        del spec["paths"][victim_route]
        spec_file = _write_spec(tmp_path, spec)
        _, missing, _ = check(_APP_PATH, spec_file)
        assert (victim_method, victim_route) in missing, (
            f"deleting {victim_route} from the spec copy must be reported as missing"
        )

    def test_checker_detects_extra_route(self, tmp_path):
        live, _, _ = check(_APP_PATH, _SPEC_PATH)
        live_routes = {route for _, route in live}
        fake = "/api/__drift_probe_extra__"
        while fake in live_routes:
            fake += "_"
        spec = _read_spec()
        spec["paths"][fake] = {
            "get": {"summary": "drift gate probe", "responses": {"200": {"description": "probe"}}}
        }
        spec_file = _write_spec(tmp_path, spec)
        _, _, extra = check(_APP_PATH, spec_file)
        assert ("GET", fake) in extra, f"adding {fake} to the spec copy must be reported as extra"
