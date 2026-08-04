import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
CHECKER = REPOSITORY_ROOT / "scripts" / "check-eula-parity.py"
CANONICAL_PATH = Path("Legal/EULA/Syxx-EULA.txt")
PLATFORM_PATHS = (
    Path("macOS/Sources/SyxxApp/Resources/Legal/Syxx-EULA.txt"),
    Path("Windows/dotNET/SyxxUI/Assets/Legal/Syxx-EULA.txt"),
    Path("Linux/Ubuntu/SyxxUI.Avalonia/Assets/Legal/Syxx-EULA.txt"),
)


class EulaParityCheckerTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary_directory.name)
        body = "\n\n".join(
            f"{section}. Section {section}. Presentation text."
            for section in range(1, 17)
        ) + "\n"
        self.body = body.encode("utf-8")

        canonical = self.root / CANONICAL_PATH
        canonical.parent.mkdir(parents=True)
        canonical.write_bytes(self.body)

        for relative_path in PLATFORM_PATHS:
            platform_copy = self.root / relative_path
            platform_copy.parent.mkdir(parents=True)
            platform_copy.write_bytes(self.body)

        digest = hashlib.sha256(self.body).hexdigest()
        manifest = {
            "schemaVersion": 1,
            "documentId": "syxx-eula",
            "versionId": "syxx-eula-draft-2026-07-30",
            "status": "internal-draft-presentation",
            "canonicalPath": CANONICAL_PATH.as_posix(),
            "sha256": digest,
            "copies": [path.as_posix() for path in PLATFORM_PATHS],
        }
        manifest_path = self.root / "Legal/EULA/manifest.json"
        manifest_path.write_text(
            json.dumps(manifest, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )

    def tearDown(self) -> None:
        self._temporary_directory.cleanup()

    def run_checker(self) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(CHECKER), "--root", str(self.root)],
            capture_output=True,
            check=False,
            text=True,
        )

    def test_matching_canonical_and_platform_bytes_pass(self) -> None:
        result = self.run_checker()

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(hashlib.sha256(self.body).hexdigest(), result.stdout)
        self.assertIn("3 platform copies", result.stdout)

    def test_one_byte_platform_mutation_fails(self) -> None:
        windows_copy = self.root / PLATFORM_PATHS[1]
        windows_copy.write_bytes(self.body.replace(b"Section 9", b"Section IX", 1))

        result = self.run_checker()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn(PLATFORM_PATHS[1].as_posix(), result.stderr)
        self.assertIn("byte mismatch", result.stderr)

    def test_crlf_platform_copy_fails_exact_byte_contract(self) -> None:
        linux_copy = self.root / PLATFORM_PATHS[2]
        linux_copy.write_bytes(self.body.replace(b"\n", b"\r\n"))

        result = self.run_checker()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn(PLATFORM_PATHS[2].as_posix(), result.stderr)
        self.assertIn("byte mismatch", result.stderr)

    def test_manifest_hash_mutation_fails(self) -> None:
        manifest_path = self.root / "Legal/EULA/manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        manifest["sha256"] = "0" * 64
        manifest_path.write_text(
            json.dumps(manifest, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )

        result = self.run_checker()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("manifest sha256 does not match", result.stderr)


if __name__ == "__main__":
    unittest.main()
