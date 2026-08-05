# PENDING OWNER VERIFICATION

**Every fixture in this corpus is `DERIVED-FROM-DOCUMENTATION`. None was captured from a running
Linux host.** The authoring host is macOS and has no systemd-resolved, so `resolvectl` output could
not be produced there. The shapes come from systemd's own printing code, its manpages, and a
verbatim paste in an upstream bug report — cited per fixture — but a documented shape is not a
witnessed one.

This file exists because the defect being fixed was caused by someone hand-writing a `resolvectl`
format that does not exist. Recording exactly what is still unwitnessed is the only thing that stops
that from recurring one layer up.

## How to clear this file

Run the §10.2 pack on the target box and save the whole output to
`corpus/resolvectl-status/captured/<label>.pack.txt`:

```
resolvectl --version; echo "--- STATUS"; resolvectl status --no-pager
echo "--- RESOLVCONF"; cat /etc/resolv.conf; echo "--- MODE"; readlink -f /etc/resolv.conf
```

`ResolvectlCorpusTests.CapturedPacks_ParseAndAgreeWithResolvConf` picks the file up automatically.

## Declared expectations, recorded before the pack is run

Written down in advance so the result cannot be rationalised afterwards.

- **PASS** — a `Global` section and at least one `Link` section are present, and the DoT state
  appears either as a `Protocols:` token (`+DNSOverTLS` / `-DNSOverTLS` / `DNSOverTLS=opportunistic`)
  or as a `DNSOverTLS setting:` line.
- **FAIL for the replaced parser** — neither the literal `DNS Over TLS:` nor `DNSOverHTTPS:` appears
  anywhere in the capture. That confirms both branches, and both tests that covered them, were dead.
- **INCONCLUSIVE** — systemd-resolved is not running on the box. The fallback path needs a
  different pack; this one proves nothing either way.
- **A false clean to refuse:** if `/etc/resolv.conf` does **not** contain `127.0.0.53` on the
  owner's machine, that does **not** clear the stub defect. It means that box is not in stub
  configuration, while the majority of Ubuntu Desktop installs are. One clean paste does not
  close it.

## Specific claims that need a real box

| # | Claim encoded in the corpus / parser | Source relied on | What would falsify it |
| --- | --- | --- | --- |
| 1 | v249+ prints `Protocols: +DefaultRoute +LLMNR -mDNS -DNSOverTLS DNSSEC=no/unsupported` | systemd `resolvectl.c`, upstream issue 21123 paste | different token spelling or ordering |
| 2 | Multiple DNS servers render space-separated on one line (`TABLE_STRV_WRAPPED`) | systemd `resolvectl.c` `dump_list`, issue 21123 paste | one-per-line on a wide terminal |
| 3 | Narrow terminals wrap that row onto unlabelled continuation lines | systemd table formatter semantics | no wrapping, or a different continuation indent |
| 4 | `opportunistic` renders as `DNSOverTLS=opportunistic`, not `+DNSOverTLS` | `strv_extend_extended_bool` in `resolvectl.c` | `+DNSOverTLS` shown for an opportunistic box |
| 5 | v245-era prints `DNSOverTLS setting: no` and one server per line | systemd v245 `resolvectl.c` | different labels on Ubuntu 20.04 |
| 6 | v245-era prints no DefaultRoute field, so scope cannot be derived there | absence in v245 `resolvectl.c` | a `DefaultRoute setting:` line appearing |
| 7 | `127.0.0.54` is a systemd-resolved stub listener (the proxy stub) | systemd-resolved(8) | it is something else, or absent in supported versions |
| 8 | `resolv.conf mode:` takes values `stub` / `static` / `uplink` / `foreign` | systemd-resolved(8) | an unlisted value appearing |
| 9 | `Delegate <name>` blocks exist in newer systemd and are skipped by the parser | `resolvectl.c` header string only | delegates carrying resolvers that matter — currently dropped |
| 10 | A link with an mDNS-only `Current Scopes` can still list DNS Servers | **constructed edge case, no source** | likely never occurs; the tokenisation rule it pins is still correct |
| 11 | ANSI escapes never appear in piped `--no-pager` output | systemd `ansi_highlight()` is tty-gated | escapes in a captured pack (the parser strips them defensively either way) |

Item 9 is the one that can currently lose data: if `Delegate` blocks carry resolvers, the parser
drops them silently. It skips rather than guesses because no documented example was found.

Item 10 is the only fixture content not backed by a source, and it is labelled as constructed inside
the fixture itself.
