#!/usr/bin/env python3
"""Verify the canonical Syxx EULA resource and its three platform copies."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path


MANIFEST_PATH = Path("Legal/EULA/manifest.json")
CANONICAL_PATH = Path("Legal/EULA/Syxx-EULA.txt")
PLATFORM_PATHS = (
    Path("macOS/Sources/SyxxApp/Resources/Legal/Syxx-EULA.txt"),
    Path("Windows/dotNET/SyxxUI/Assets/Legal/Syxx-EULA.txt"),
    Path("Linux/Ubuntu/SyxxUI.Avalonia/Assets/Legal/Syxx-EULA.txt"),
)
SECTION_PATTERN = re.compile(r"(?m)^(\d{1,2})\.\s")


class ParityError(RuntimeError):
    """A canonical-resource or platform-copy contract violation."""


def read_bytes(root: Path, relative_path: Path) -> bytes:
    path = root / relative_path
    try:
        return path.read_bytes()
    except FileNotFoundError as error:
        raise ParityError(f"{relative_path.as_posix()}: missing") from error


def validate_canonical(data: bytes) -> str:
    if data.startswith(b"\xef\xbb\xbf"):
        raise ParityError(f"{CANONICAL_PATH.as_posix()}: UTF-8 BOM is not allowed")
    if b"\r" in data:
        raise ParityError(f"{CANONICAL_PATH.as_posix()}: must use LF line endings")
    if not data.endswith(b"\n"):
        raise ParityError(f"{CANONICAL_PATH.as_posix()}: must end with one LF")

    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError as error:
        raise ParityError(
            f"{CANONICAL_PATH.as_posix()}: must be valid UTF-8"
        ) from error

    section_numbers = [int(match.group(1)) for match in SECTION_PATTERN.finditer(text)]
    if section_numbers != list(range(1, 17)):
        raise ParityError(
            f"{CANONICAL_PATH.as_posix()}: expected ordered sections 1 through 16, "
            f"found {section_numbers}"
        )
    return text


def load_manifest(root: Path) -> dict[str, object]:
    raw = read_bytes(root, MANIFEST_PATH)
    if raw.startswith(b"\xef\xbb\xbf") or b"\r" in raw:
        raise ParityError(f"{MANIFEST_PATH.as_posix()}: must be LF UTF-8 without BOM")
    try:
        manifest = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ParityError(f"{MANIFEST_PATH.as_posix()}: invalid UTF-8 JSON") from error
    if not isinstance(manifest, dict):
        raise ParityError(f"{MANIFEST_PATH.as_posix()}: root must be an object")
    return manifest


def validate_manifest(manifest: dict[str, object], digest: str) -> None:
    if manifest.get("schemaVersion") != 1:
        raise ParityError("manifest schemaVersion must be 1")
    if manifest.get("documentId") != "syxx-eula":
        raise ParityError("manifest documentId must be syxx-eula")

    version_id = manifest.get("versionId")
    if not isinstance(version_id, str) or not version_id.strip():
        raise ParityError("manifest versionId must be a stable, non-empty string")
    if manifest.get("status") != "internal-draft-presentation":
        raise ParityError("manifest status must be internal-draft-presentation")
    if manifest.get("canonicalPath") != CANONICAL_PATH.as_posix():
        raise ParityError("manifest canonicalPath does not name the canonical resource")
    if manifest.get("sha256") != digest:
        raise ParityError("manifest sha256 does not match canonical bytes")

    expected_copies = [path.as_posix() for path in PLATFORM_PATHS]
    if manifest.get("copies") != expected_copies:
        raise ParityError("manifest copies must name all three platform resources in order")


def check(root: Path) -> tuple[str, int]:
    canonical = read_bytes(root, CANONICAL_PATH)
    validate_canonical(canonical)
    digest = hashlib.sha256(canonical).hexdigest()
    validate_manifest(load_manifest(root), digest)

    for relative_path in PLATFORM_PATHS:
        platform_copy = read_bytes(root, relative_path)
        if platform_copy != canonical:
            raise ParityError(f"{relative_path.as_posix()}: byte mismatch")

    return digest, len(PLATFORM_PATHS)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
        help="repository root (defaults to the parent of scripts/)",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        digest, copy_count = check(args.root.resolve())
    except ParityError as error:
        print(f"EULA parity FAIL: {error}", file=sys.stderr)
        return 1
    print(f"EULA parity PASS: sha256={digest}; {copy_count} platform copies")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
