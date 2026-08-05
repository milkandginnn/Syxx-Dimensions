# `resolvectl status` golden corpus

Drives `Syxx.Tests.ResolvectlCorpusTests` (in `Windows/dotNET/Syxx.Tests/ConfigAndPolicyTests.cs`),
which exercises `NetworkKeyState.ParseResolvectlStatus` / `ReadLinuxResolvers` — the Linux resolver
discovery seam.

## Why this corpus exists (P-22)

The two tests that previously covered this parser hand-wrote `resolvectl` formats that
systemd-resolved has never emitted — `DNSOverHTTPS: yes` and `DNS Over TLS: yes`. They were green.
They were worse than no tests, because they licensed the belief that Linux resolver discovery was
covered while the parser could not read a single real machine.

**The rule that follows from that:** every fixture here states where its shape came from. A fixture
whose format nobody can point to a source for is the same defect one layer up.

## Provenance markers — every fixture carries exactly one

| Marker | Meaning |
| --- | --- |
| `DERIVED-FROM-DOCUMENTATION` | Shape reconstructed from systemd's own printing code, its manpages, or a verbatim paste in an upstream bug report. The cited URL is in the fixture header. **Not** captured from a running host. |
| `CAPTURED-FROM-REAL-HOST` | Byte-for-byte output of `resolvectl status --no-pager` on a real machine, with the host and systemd version in the header. |

At the time of writing, **every fixture in `fixtures/` and `resolvconf/` is
`DERIVED-FROM-DOCUMENTATION`**, because the authoring host is macOS and has no systemd-resolved.
See `PENDING-OWNER-VERIFICATION.md` for exactly what still needs a real box.

## Fixture file format

A leading block of `#`-prefixed lines is the provenance header. The test loader strips it and hands
the remainder to the parser verbatim, so the body is exactly what the tool prints. Expectations live
in the test, not in the fixture.

## Dropping in a real capture

Run this on the Linux box (§10.2 of the master plan):

```
resolvectl --version; echo "--- STATUS"; resolvectl status --no-pager
echo "--- RESOLVCONF"; cat /etc/resolv.conf; echo "--- MODE"; readlink -f /etc/resolv.conf
```

Save the **entire** output, unedited, to:

```
corpus/resolvectl-status/captured/<label>.pack.txt
```

`<label>` is anything descriptive (`ubuntu-2404-desktop`, `mint-22-laptop`). Nothing else is needed —
`ResolvectlCorpusTests.CapturedPacks_ParseAndAgreeWithResolvConf` discovers every `*.pack.txt` in
that directory, splits it on the `--- STATUS` / `--- RESOLVCONF` / `--- MODE` markers, and asserts
the §10.2 expectations against it. There is no manifest to update and no fixture to hand-write.

A captured pack is the only thing that can promote these fixtures from
`DERIVED-FROM-DOCUMENTATION` to verified.
