# Captured packs — drop real output here

Save the **whole, unedited** output of the §10.2 pack as `<label>.pack.txt` in this directory:

```
resolvectl --version; echo "--- STATUS"; resolvectl status --no-pager
echo "--- RESOLVCONF"; cat /etc/resolv.conf; echo "--- MODE"; readlink -f /etc/resolv.conf
```

Example: `ubuntu-2404-desktop.pack.txt`.

Nothing else to do. `ResolvectlCorpusTests.CapturedPacks_ParseAndAgreeWithResolvConf` discovers every
`*.pack.txt` here, splits it on the `--- STATUS` / `--- RESOLVCONF` / `--- MODE` markers and asserts
the expectations recorded in `../PENDING-OWNER-VERIFICATION.md`. No manifest, no hand-written fixture.

Do not trim, reformat or redact the output. Alignment and wrapping are part of what is being pinned.
If the DNS servers on the box are sensitive, say so rather than editing the file — an edited capture
is a derived fixture, not a capture, and would defeat the point of this directory.

This directory being empty is itself a finding: it means no claim in this corpus has been witnessed
on a real machine yet.
