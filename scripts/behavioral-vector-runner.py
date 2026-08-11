#!/usr/bin/env python3
"""Behavioral vector runner — executes CLI commands defined in corpus/behavioral-vectors/*.json
and captures one canonical record per case per platform.

Part of track cross-platform-parity_20260805, Task 4.3.
Follows the shape of scripts/parity-local-gate.sh (accumulate rc, never exit early).
"""

import json
import os
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from typing import Any

REPO = Path(__file__).resolve().parent.parent
VECTORS = REPO / "corpus" / "behavioral-vectors"

# C# Program.cs seeds Environment.ExitCode=2 (the "no conclusion" sentinel).
# When a command never sets ExitCode, the process inherits 2 on exit.
CS_SEEDED_EXIT = 2


def find_binaries() -> tuple[Path, str]:
    """Return (swift_bin, cs_cmd) or exit."""
    swift_bin = REPO / ".build" / "debug" / "syxx"
    if not swift_bin.is_file():
        print(f"FAIL: Swift binary not found at {swift_bin}", file=sys.stderr)
        print("Build with: (cd macOS && swift build)", file=sys.stderr)
        sys.exit(1)

    cs_dir = REPO / "Windows" / "dotNET" / "Syxx" / "bin" / "Debug"
    cs_candidates = sorted(cs_dir.glob("net*/Syxx"), reverse=True)
    if not cs_candidates:
        print("FAIL: C# binary not found under Windows/dotNET/Syxx/bin/Debug/", file=sys.stderr)
        print("Build with: dotnet build Windows/dotNET/Syxx/Syxx.csproj", file=sys.stderr)
        sys.exit(1)

    cs_bin = cs_candidates[0]
    return swift_bin, str(cs_bin)


def run_vector(vec: dict, label: str, binary: str, work: Path) -> dict[str, Any]:
    """Run one vector against one binary. Returns a canonical record dict."""
    vec_id = vec["vectorId"]
    group = vec["group"]
    args = vec["args"]
    command = vec["command"]
    expected_exit = vec["expectedExitCode"]
    timeout_sec = vec["timeoutSeconds"]
    expected_phrases = vec.get("expectedOutputPhrases", [])
    expected_absent = vec.get("expectedOutputAbsent", [])
    divergences = vec.get("knownParityDivergences", [])
    has_exitcode_divergence = any("never sets Environment.ExitCode" in d for d in divergences)

    full_args = [binary, command] + args

    out_dir = work / label
    out_dir.mkdir(parents=True, exist_ok=True)

    try:
        proc = subprocess.run(
            full_args, capture_output=True, text=True, timeout=timeout_sec
        )
        exit_code = proc.returncode
        timed_out = False
    except subprocess.TimeoutExpired:
        exit_code = -1
        timed_out = True

    record: dict[str, Any] = {
        "vectorId": vec_id,
        "label": label,
        "group": group,
        "exitCode": exit_code,
        "expectedExitCode": expected_exit,
        "timedOut": timed_out,
    }

    if timed_out:
        record["status"] = "TIMEOUT"
        return record

    combined = (proc.stdout or "") + (proc.stderr or "")

    # Check exit code
    exit_match = exit_code == expected_exit
    if not exit_match and label == "cs" and has_exitcode_divergence:
        # C# seeded-2 exception: if the command doesn't set ExitCode,
        # it inherits the seeded 2 regardless of what the vector expects.
        if exit_code == CS_SEEDED_EXIT:
            exit_match = True
            record["exitCodeNote"] = "C# seeded-2 treated as known divergence"

    record["exitMatch"] = exit_match

    # Check expected output phrases
    missing_phrases = [p for p in expected_phrases if p not in combined]
    record["phrasesMatch"] = len(missing_phrases) == 0
    if missing_phrases:
        record["missingPhrases"] = missing_phrases

    # Check absent phrases
    present_absent = [p for p in expected_absent if p in combined]
    record["absentMatch"] = len(present_absent) == 0
    if present_absent:
        record["presentAbsent"] = present_absent

    # Overall status
    if exit_match and not missing_phrases and not present_absent:
        record["status"] = "PASS"
    else:
        record["status"] = "FAIL"

    # Write per-vector stdout/stderr for debugging
    (out_dir / f"{vec_id}.stdout").write_text(proc.stdout or "")
    (out_dir / f"{vec_id}.stderr").write_text(proc.stderr or "")

    return record


def main():
    swift_bin, cs_cmd = find_binaries()
    print(f"Swift binary: {swift_bin}")
    print(f"C# binary:    {cs_cmd}")
    print(f"Vectors:      {VECTORS}")
    print()

    # Discover vectors
    vector_files = sorted(VECTORS.glob("[0-9][0-9][0-9]_*.json"))
    if not vector_files:
        print("No behavioral vectors found.", file=sys.stderr)
        sys.exit(1)

    # Load all vectors
    vectors = []
    for vf in vector_files:
        with open(vf) as f:
            vectors.append(json.load(f))

    with tempfile.TemporaryDirectory() as work_str:
        work = Path(work_str)
        rc = 0
        swift_pass = swift_fail = cs_pass = cs_fail = 0
        all_records: dict[str, list[dict]] = {"swift": [], "cs": []}

        for group_name in ("offline", "fixture"):
            group_vectors = [v for v in vectors if v["group"] == group_name]
            if not group_vectors:
                continue

            print(f"=== Running {group_name} vectors ({len(group_vectors)}) ===")

            for vec in group_vectors:
                vec_id = vec["vectorId"]
                print(f"  {vec_id}")

                # For fixture vectors, check if the fixture artifact exists
                if group_name == "fixture":
                    fixture_req = vec.get("fixtureRequirements", {})
                    artifact_path = fixture_req.get("artifactPath", "")
                    if artifact_path:
                        # Check both corpus-relative and repo-relative
                        if not (REPO / "corpus" / artifact_path).exists() and \
                           not (REPO / artifact_path).exists():
                            skip_rec = {
                                "vectorId": vec_id,
                                "group": group_name,
                                "status": "SKIP",
                                "reason": f"fixture missing: {artifact_path}",
                            }
                            all_records["swift"].append(dict(skip_rec, label="swift"))
                            all_records["cs"].append(dict(skip_rec, label="cs"))
                            print(f"    SKIP — fixture missing: {artifact_path}")
                            continue

                for label, binary in [("swift", str(swift_bin)), ("cs", cs_cmd)]:
                    rec = run_vector(vec, label, binary, work)
                    all_records[label].append(rec)
                    status = rec["status"]
                    if status == "PASS":
                        print(f"    ok   {label}/{vec_id} (exit={rec['exitCode']})")
                        if label == "swift":
                            swift_pass += 1
                        else:
                            cs_pass += 1
                    elif status == "SKIP":
                        print(f"    skip {label}/{vec_id}")
                    else:
                        print(f"    FAIL {label}/{vec_id} (exit={rec['exitCode']} "
                              f"expected={rec['expectedExitCode']})", file=sys.stderr)
                        if rec.get("missingPhrases"):
                            print(f"         missing: {rec['missingPhrases']}", file=sys.stderr)
                        if rec.get("presentAbsent"):
                            print(f"         should-be-absent: {rec['presentAbsent']}", file=sys.stderr)
                        if label == "swift":
                            swift_fail += 1
                        else:
                            cs_fail += 1
                        rc = 1

        # Write envelopes
        for label in ("swift", "cs"):
            envelope = {
                "label": label,
                "totalVectors": len(vectors),
                "records": all_records[label],
            }
            envelope_path = work / f"behavioral-{label}.json"
            with open(envelope_path, "w") as f:
                json.dump(envelope, f, indent=2)
            print(f"Wrote {len(all_records[label])} records to {envelope_path}")

        # Copy envelopes to repo for inspection
        results_dir = REPO / "corpus" / "behavioral-vectors" / "results"
        results_dir.mkdir(exist_ok=True)
        for label in ("swift", "cs"):
            src = work / f"behavioral-{label}.json"
            dst = results_dir / f"behavioral-{label}.json"
            dst.write_text(src.read_text())
            print(f"Persisted: {dst}")

        print()
        print("=== Summary ===")
        print(f"Vectors:      {len(vectors)}")
        print(f"Swift:        {swift_pass} pass / {swift_fail} fail")
        print(f"C#:           {cs_pass} pass / {cs_fail} fail")

        if rc:
            print("FAIL behavioral-vector-runner", file=sys.stderr)
        else:
            print("PASS behavioral-vector-runner")
        sys.exit(rc)


if __name__ == "__main__":
    main()
