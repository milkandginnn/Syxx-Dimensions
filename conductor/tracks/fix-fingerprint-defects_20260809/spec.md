# spec.md — Fix Twin Parity Fingerprint Defects

**Track ID:** fix-fingerprint-defects_20260809
**Type:** bug
**Status:** new

## Overview

Fix four defects identified in the `twin-parity-fingerprint_20260809` review, plus commit one set of uncommitted improvements to the C# parity transcript emitter.

## Defects

### D-1: `UnsafeRelaxedJsonEscaping` non-ASCII divergence (Critical)

`ParityTranscriptCommand.cs:264` uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` which writes non-ASCII characters as raw UTF-8 bytes. Swift's `JSONSerialization` escapes them as `\uXXXX` sequences. Fingerprint hashes will diverge for any corpus case containing non-ASCII text.

**Fix:** Replace `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` with `JavaScriptEncoder.Default`.

### D-2: `EscapeForwardSlashes` false-negative (Critical)

`ParityTranscriptCommand.cs:324` — `IsAlreadyEscaped` uses a single-byte lookbehind that cannot distinguish `\/` (escaped slash) from `\\/` (literal slash after escaped backslash). The sequence `\\/` is incorrectly treated as already escaped, causing fingerprint divergence.

**Fix:** Walk backward to count consecutive backslashes. An odd count means the slash is escaped.

### D-3: `import subprocess` not at top of file (Medium)

`cross-platform-parity-gate.py:499` — `import subprocess` is inside `main()` rather than at the module level with the other imports. Violates PEP 8.

**Fix:** Move `import subprocess` to the top of the file.

### D-4: Shell scripts missing trailing newlines (Low)

`scripts/regenerate-parity-fingerprint.sh` and `scripts/verify-parity-fingerprint.sh` lack a terminating newline at EOF. POSIX requires it.

**Fix:** Append a trailing newline to both files.

### D-5: Uncommitted improvements to `ParityTranscriptCommand.cs`

The working tree has two uncommitted changes: (1) `G17` → `"R"` format specifier in `WriteValue` (explicit round-trip format), and (2) the `Write` method applies `EscapeForwardSlashes` to the final JSON output (fixes top-level slash escaping in the envelope).

**Fix:** Commit these changes as part of the track.

## Acceptance Criteria

- [ ] `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` replaced with `JavaScriptEncoder.Default`
- [ ] `IsAlreadyEscaped` correctly counts consecutive backslashes
- [ ] `import subprocess` is at the top of `cross-platform-parity-gate.py`
- [ ] Both shell scripts end with a trailing newline
- [ ] Uncommitted `G17`→`"R"` and top-level slash fix are committed
- [ ] `bash scripts/verify-parity-fingerprint.sh` exits 0
- [ ] `python3 scripts/cross-platform-parity-gate.py` exits 0
- [ ] Swift and C# transcript fingerprints match for the current corpus

## Out of Scope

- Adding non-ASCII test cases to the corpus (separate track)
- Signing fingerprints with Ed25519 (separate feature)
- Adding standalone unit tests for fingerprint scripts (separate track)