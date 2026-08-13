"""OpenAPI spec <-> live-route drift gate (warp-hardening_20260811, Tasks 2.1-2.3).

The live route set is derived mechanically from the route dispatch in
``syxx_server/app.py`` (AST-based, never a hand-maintained list). These tests
make drift fail the test suite, not only CI:

  - the spec must document every live route and nothing more,
  - the negative tests prove the gate binds (the checker does not vacuously
    pass): dropping one live route from a COPY of the spec is reported as
    missing, and an invented spec path is reported as extra.

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
    def test_spec_matches_live_routes(self):
        live, missing, extra = check(_APP_PATH, _SPEC_PATH)
        assert live, "live route set is empty — checker cannot run"
        problems = []
        if missing:
            problems.append(f"live routes missing from spec: {sorted(missing)}")
        if extra:
            problems.append(f"spec paths with no live route: {sorted(extra)}")
        assert not missing and not extra, "; ".join(problems)

    def test_checker_detects_missing_route(self, tmp_path):
        live, _, _ = check(_APP_PATH, _SPEC_PATH)
        victim = sorted(live)[0]
        spec = _read_spec()
        del spec["paths"][victim]
        spec_file = _write_spec(tmp_path, spec)
        _, missing, _ = check(_APP_PATH, spec_file)
        assert victim in missing, f"deleting {victim} from the spec copy must be reported as missing"

    def test_checker_detects_extra_route(self, tmp_path):
        live, _, _ = check(_APP_PATH, _SPEC_PATH)
        fake = "/api/__drift_probe_extra__"
        while fake in live:
            fake += "_"
        spec = _read_spec()
        spec["paths"][fake] = {
            "get": {"summary": "drift gate probe", "responses": {"200": {"description": "probe"}}}
        }
        spec_file = _write_spec(tmp_path, spec)
        _, _, extra = check(_APP_PATH, spec_file)
        assert fake in extra, f"adding {fake} to the spec copy must be reported as extra"
