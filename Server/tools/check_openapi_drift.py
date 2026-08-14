#!/usr/bin/env python3
"""OpenAPI <-> live-route drift check for the Syxx collector server.

Derives the live route set MECHANICALLY from the route dispatch in
``syxx_server/app.py`` (via AST — never a hand-maintained list), loads the
``paths:`` keys and their methods from ``docs/openapi.yaml``, and fails
(exit 1) on ANY divergence in either direction. The comparison is over
(METHOD, path) pairs, not path alone — a path documented for one HTTP
method but live under a different one is drift a path-only comparison
cannot see:

    missing = live (method, path) pairs not documented in the spec
    extra   = spec (method, path) pairs with no live handler

Usage:
    uv run python Server/tools/check_openapi_drift.py     # from repo root
    python tools/check_openapi_drift.py                    # from Server/
    python tools/check_openapi_drift.py APP.py SPEC.yaml   # explicit paths

Importable from tests as ``check(app_path, spec_path) -> (live, missing, extra)``,
where all three are sets of (METHOD, path) pairs.
"""

from __future__ import annotations

import ast
import sys
from pathlib import Path

import yaml

_SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_APP_PATH = _SCRIPT_DIR / ".." / "syxx_server" / "app.py"
DEFAULT_SPEC_PATH = _SCRIPT_DIR / ".." / "docs" / "openapi.yaml"

# Handler dispatch methods -> HTTP verb.
_DISPATCH_METHODS = {
    "_dispatch_get": "get",
    "do_POST": "post",
    "do_DELETE": "delete",
}

# startswith() dispatches that are parametrized API routes. The live code
# matches any /api/keys/<id> prefix; the spec documents the canonical {id} path.
_STARTSWITH_ROUTE_MAP = {
    "/api/keys/": "/api/keys/{id}",
    "/api/monitors/": "/api/monitors/{id}",
}

# Non-API surfaces intentionally not documented in the OpenAPI spec:
# static/doc/marketing pages and the /assets/ image tree.
_STATIC_DOC_EXCLUDED = frozenset({
    "/", "/index.html", "/docs/api", "/docs/api/", "/docs/help", "/docs/help/",
    "/sitemap.xml", "/robots.txt",
})
_ASSETS_PREFIX = "/assets/"

_HTTP_METHODS = frozenset({"get", "post", "put", "patch", "delete", "head", "options"})


def _string_literal(node):
    """Return the str value of an ast constant, else None."""
    if isinstance(node, ast.Constant) and isinstance(node.value, str):
        return node.value
    return None


def _path_tests(node):
    """Extract ("exact", route) / ("prefix", prefix) entries matched against `path`.

    Recognised shapes inside a dispatch method:
      if path == "X":            exact route X
      if path in ("A", "B"):     exact routes A and B
      if path.startswith("P"):   prefix P

    Non-literal compares (``if path in _STATIC_FILES:``) and the /api/v1/ alias
    strip (``startswith("/api/v1/")``, rewritten in do_GET/do_POST/do_DELETE
    before dispatch) are ignored — the spec documents canonical /api/* only.
    """
    test = node.test
    if isinstance(test, ast.Compare):
        left = test.left
        if not isinstance(left, ast.Name) or left.id != "path":
            return []
        op = test.ops[0]
        if isinstance(op, ast.Eq):
            value = _string_literal(test.comparators[0])
            return [("exact", value)] if value else []
        if isinstance(op, ast.In):
            comp = test.comparators[0]
            if isinstance(comp, ast.Tuple):
                return [("exact", v) for v in (_string_literal(e) for e in comp.elts) if v]
        return []
    if isinstance(test, ast.Call):
        func = test.func
        if (
            isinstance(func, ast.Attribute)
            and isinstance(func.value, ast.Name)
            and func.value.id == "path"
            and func.attr == "startswith"
        ):
            prefix = _string_literal(test.args[0]) if test.args else None
            return [("prefix", prefix)] if prefix else []
    return []


def _collect_live(app_path):
    """Parse app.py and return (route_methods, excluded) for the Handler dispatch."""
    source = Path(app_path).read_text(encoding="utf-8")
    tree = ast.parse(source, filename=str(app_path))
    routes: dict[str, set[str]] = {}
    excluded: set[str] = set()
    for node in ast.walk(tree):
        if not isinstance(node, ast.ClassDef) or node.name != "Handler":
            continue
        for stmt in node.body:
            if not isinstance(stmt, ast.FunctionDef):
                continue
            method = _DISPATCH_METHODS.get(stmt.name)
            if method is None:
                continue
            for sub in ast.walk(stmt):
                if not isinstance(sub, ast.If):
                    continue
                for kind, value in _path_tests(sub):
                    if kind == "prefix":
                        if value in _STARTSWITH_ROUTE_MAP:
                            routes.setdefault(_STARTSWITH_ROUTE_MAP[value], set()).add(method)
                        elif value == _ASSETS_PREFIX:
                            excluded.add(value)
                        # any other prefix (e.g. "/api/v1/") is intentionally ignored
                        continue
                    if value in _STATIC_DOC_EXCLUDED:
                        excluded.add(value)
                        continue
                    routes.setdefault(value, set()).add(method)
    return routes, excluded


def _load_spec(spec_path):
    """Return (path_set, path_methods) from the OpenAPI spec's `paths:` block."""
    with open(spec_path, encoding="utf-8") as f:
        data = yaml.safe_load(f)
    if not isinstance(data, dict) or not isinstance(data.get("paths"), dict):
        raise RuntimeError(f"{spec_path}: missing `paths:` mapping — cannot run the drift check")
    path_set = set(data["paths"].keys())
    path_methods = {
        p: {m for m in entry if isinstance(m, str) and m.lower() in _HTTP_METHODS}
        for p, entry in data["paths"].items()
        if isinstance(entry, dict)
    }
    return path_set, path_methods


def _pairs(route_methods: dict[str, set[str]]) -> set[tuple[str, str]]:
    """Flatten a {route: {method, ...}} map to a set of (METHOD, route) pairs."""
    return {(method.upper(), route) for route, methods in route_methods.items() for method in methods}


def _analyze(app_path, spec_path):
    app_path = Path(app_path)
    spec_path = Path(spec_path)
    if not app_path.is_file():
        raise RuntimeError(f"cannot read app source: {app_path}")
    if not spec_path.is_file():
        raise RuntimeError(f"cannot read OpenAPI spec: {spec_path}")
    live_methods, excluded = _collect_live(app_path)
    spec, spec_methods = _load_spec(spec_path)
    if not live_methods:
        raise RuntimeError(
            f"live route set is empty — cannot run vs. vacuously pass "
            f"(no routes extracted from {app_path})"
        )
    if not spec:
        raise RuntimeError(
            f"spec path set is empty — cannot run vs. vacuously pass "
            f"(`paths:` empty in {spec_path})"
        )
    live = set(live_methods)
    live_pairs = _pairs(live_methods)
    spec_pairs = _pairs(spec_methods)
    missing = live_pairs - spec_pairs
    extra = spec_pairs - live_pairs
    return {
        "app_path": app_path,
        "spec_path": spec_path,
        "live": live,
        "live_methods": live_methods,
        "excluded": excluded,
        "spec": spec,
        "spec_methods": spec_methods,
        "live_pairs": live_pairs,
        "spec_pairs": spec_pairs,
        "missing": missing,
        "extra": extra,
    }


def check(app_path, spec_path) -> tuple[set, set, set]:
    """Return (live_pairs, missing, extra) for the given app.py + openapi.yaml.

    All three are sets of (METHOD, path) pairs:
    - live_pairs: pairs served by Handler dispatch (exclusions applied)
    - missing:    live pairs absent from the spec
    - extra:      spec pairs with no live handler
    """
    data = _analyze(app_path, spec_path)
    return data["live_pairs"], data["missing"], data["extra"]


def _print_methods(title, route_map):
    lines = []
    for route in sorted(route_map):
        for method in sorted(route_map[route]):
            lines.append(f"{method.upper()} {route}")
    print(f"{title} ({len(lines)}):")
    for line in lines:
        print(f"  {line}")


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    app_path = Path(argv[0]) if len(argv) > 0 else DEFAULT_APP_PATH
    spec_path = Path(argv[1]) if len(argv) > 1 else DEFAULT_SPEC_PATH
    try:
        data = _analyze(app_path, spec_path)
    except RuntimeError as e:
        print(f"check_openapi_drift: cannot run: {e}")
        return 1

    print("OpenAPI drift check")
    print(f"  app:  {data['app_path']}")
    print(f"  spec: {data['spec_path']}")
    if data["excluded"]:
        print("excluded (static/doc):")
        for route in sorted(data["excluded"]):
            print(f"  {route}  excluded (static/doc)")
    _print_methods("live routes", data["live_methods"])
    _print_methods("spec paths", data["spec_methods"])

    if data["missing"]:
        print("MISSING from spec (live - spec):")
        for method, route in sorted(data["missing"]):
            print(f"  {method} {route}")
    if data["extra"]:
        print("EXTRA in spec (spec - live):")
        for method, route in sorted(data["extra"]):
            print(f"  {method} {route}")

    # Counts here are (METHOD, path) pairs — the same unit the comparison and
    # the "live routes"/"spec paths" listings above use — so they always agree.
    if not data["missing"] and not data["extra"]:
        print(
            f"RESULT: no drift — spec matches live routes "
            f"({len(data['live_pairs'])} live, {len(data['spec_pairs'])} spec)."
        )
        return 0
    print(
        f"RESULT: DRIFT DETECTED — {len(data['missing'])} missing, {len(data['extra'])} extra. "
        f"Update docs/openapi.yaml."
    )
    return 1


if __name__ == "__main__":
    sys.exit(main())
