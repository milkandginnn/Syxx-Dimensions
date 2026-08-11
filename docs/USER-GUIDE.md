# Syxx user guide

## What is Syxx?

Syxx is a DNS security tool that checks whether your internet connection is being watched, redirected, or tampered with. When you visit a website, your computer asks a DNS server (think of it as the internet's phone book) to translate the name you typed into the IP address your computer actually connects to. If someone interferes with that translation, you can end up on a fake version of the site you meant to visit without ever knowing it. Syxx runs a battery of tests across six different angles to detect that kind of interference and gives you a plain-language answer you can trust — including an honest "I don't know" when it could not measure enough to answer.

---

## How Syxx reports results

Every Syxx check ends in one of four verdicts. Understanding them — especially "inconclusive" — is the most important part of using Syxx.

### Clean

No signs of tampering, hijacking, or leaking were found in what Syxx actually measured. **This does not mean nothing is wrong** — it means Syxx looked and did not detect anything wrong. No security tool can guarantee 100% safety.

### Threat detected

Syxx found evidence of interference. The report tells you specifically what was detected (for example, DNS redirection, a content mismatch, or blocking) and which test found it. A threat verdict always comes with the evidence that supports it, so you can see exactly what Syxx saw.

### Inconclusive

Syxx could not reach a clear conclusion. This happens when a test did not complete (a server timed out, a probe failed to send), when the evidence is contradictory, or when not enough data was collected to make a confident call.

**Inconclusive is not a pass.** It is not a "clean" verdict in disguise, and Syxx will never quietly upgrade it to one. Missing data is never reported as clean data. An inconclusive verdict means exactly this: Syxx did not get the evidence it needed, so it is refusing to grade what it did not measure. If you see it, the right response is to fix whatever blocked the test (reconnect, retry, check your connection) and run it again — not to assume you are safe.

### Untested

A specific area was not tested during this run. This is normal when you run a single check: if you only run `syxx leak`, the other areas show as untested. Untested is also not a pass — it is simply "we didn't look here."

---

## Quick start: scan everything

### In the app

1. Open Syxx. On first launch, the welcome screen shows a **Scan my network** button — click it. (Later, you can find the same button in the **Conclusion** section of the sidebar.)
2. Syxx runs every check that needs no setup and shows a progress readout.
3. When the scan finishes, you get a report card with a grade and a plain-language explanation. See "How to read your report card" below.

### From the command line

```
syxx firstrun
```

This runs all disciplines that need no configuration and grades the result. It also lists which disciplines ran and which were skipped, so you always know what the grade does and does not cover. You can point it at a specific domain with `syxx firstrun --domain example.com`.

Running `syxx` with no arguments opens an interactive menu instead: choose a check by number (1–6), press **C** for Conclusions (the combined verdict and report card), **L** to manage your license, or **Q** to quit.

---

## The six modules

Syxx organizes its tests into six areas. Each one asks a specific question about your connection. In the app, each has its own section in the sidebar; from the command line, each maps to one or more `syxx` subcommands.

### Prophet — is my DNS resolver telling the truth?

**What it checks:** whether the DNS server your computer uses is giving you accurate, unmodified answers — including DNSSEC (cryptographic signing of DNS answers), response consistency, and "canary" tests for common tricks like rewriting not-found answers.

**How to run it:**

- App: select **Prophet** in the sidebar, enter a domain, and start the check.
- Command line: choose option 1 in the `syxx` menu for the full probe battery, or run a single lookup:

  ```
  syxx nslook example.com              # basic lookup via your system resolver
  syxx nslook example.com MX @8.8.8.8  # specific record type via a specific server
  syxx nslook example.com --dnssec     # include DNSSEC validation
  ```

**How to read the result:** Prophet lists your resolver's answers (A and AAAA records), its DNSSEC status, and a series of probe results (canaries, latency, answer stability, and more). Each probe reports its own outcome before any conclusion is drawn. A probe that fails or gets no answer is labeled as failed or unmeasured — Syxx explicitly reminds you that "no answer" is not evidence of safety.

**When it says inconclusive:** one or more probes timed out or your resolver did not respond. Nothing was proven either way. Re-run when your connection is stable; if it stays inconclusive, that itself is worth investigating.

**Common false alarm:** if you run your own DNS filter (like a Pi-hole) or your ISP applies a family/content filter you signed up for, Prophet can flag intentionally blocked domains as blocking. That is Prophet doing its job — it detects the filtering; only you know whether you asked for it.

### Mirror — is someone changing what I see?

**What it checks:** whether the answers and web content you receive match what independent vantage points receive — comparing your resolver's answers against public resolvers, and (with Pro) comparing page and script hashes against a clean copy fetched through Syxx's server.

**How to run it:**

- App: select **Mirror** in the sidebar (a Pro section), enter a domain, and click **Check**.
- Command line:

  ```
  syxx mirror example.com              # compare your resolver against public vantage points
  syxx warp https://example.com        # content integrity check (Pro; server-anchored)
  syxx warp https://example.com --diff # also show the local-vs-remote HTML diff
  ```

**How to read the result:** the resolver comparison ends in one of four outcomes: secure (answers agree), censored/blocked (your network returned an internal or invalid address while public servers resolved the domain), maliciously redirected (your resolver returned a real but wrong address — do not enter credentials on that site), or inconclusive. The content check reports match, mismatch (content or script injection on your connection), or inconclusive.

**When it says inconclusive:** no public vantage point returned a usable baseline, so there was nothing trustworthy to compare against. Syxx says so plainly: this is not evidence of safety. Re-run on a stable connection.

**Common false alarm:** large sites behind content delivery networks (CDNs) can legitimately hand different addresses to different resolvers, and dynamic pages can differ per visitor. Mirror compares multiple vantage points to reduce this, but a divergence on a big CDN-hosted site deserves a re-check before you conclude anything.

### Portal — what does my network actually look like?

**What it checks:** your local network state — which resolvers and interfaces are active, whether a VPN is up, which devices are on your network, and the path your traffic takes to reach a site.

**How to run it:**

- App: select **Portal** in the sidebar, optionally enter a domain to trace, and run it.
- Command line:

  ```
  syxx key                # resolver / interface / VPN / captive-portal / encrypted-DNS state
  syxx lan                # map on-link devices and flag which one is your DNS resolver
  syxx route example.com  # trace the path your traffic takes to a target
  ```

**How to read the result:** Portal is mostly a map, not a verdict. It lists your resolvers, active interfaces, and VPN status (active / inactive / unknown — Syxx keeps the honest third state rather than guessing), then the devices on your network with your DNS resolver flagged, then the route trace hop by hop. If the trace stops before reaching the destination, the last responding hop is where interception or blocking may begin.

**When it says unknown or unmeasured:** a VPN status of "unknown," an unavailable LAN scan, or a trace that returned no hops means Syxx could not observe that part of your network. It reports the gap instead of guessing.

**Common false alarm:** an "unknown device" on your network is very often your own printer, smart TV, or thermostat. Likewise, many routers and firewalls silently drop trace probes, so a route that ends early does not by itself prove blocking — it marks where to look, not a conviction.

### Glutton — is my VPN leaking?

**What it checks:** whether your DNS queries actually travel through your VPN, or escape through your regular connection where your ISP (or anyone on the path) can see them.

**How to run it:**

- App: select **Glutton** in the sidebar (a Pro section) and start the leak check. For a confirmed result, enter the collector server URL and run collector verification.
- Command line:

  ```
  syxx leak                                        # dispatch local probes
  syxx leak --collector <url> --key <license-key>  # collector-confirmed result (paid route)
  ```

**How to read the result:** Glutton first shows each probe's real outcome — sent or failed — before drawing any conclusion. A local run by itself can only ever end in **inconclusive**: it proves your probes were dispatched, but confirming which resolver actually forwarded them requires Syxx's collector server, which records the source address and signs that record so you can verify it offline. If every probe dispatch fails, Syxx tells you loudly that the test did not run and nothing was measured — that is not a clean result; it usually means your resolvers are unreachable (no connectivity, a VPN or firewall blocking DNS, or a captive portal).

**When it says inconclusive:** either you ran without the collector (expected — add `--collector` and your license key for a confirmed verdict) or the probes could not be sent at all. In both cases the leak question is unanswered, not answered "no."

**Common false alarm:** running the test while your VPN is disconnected, or with split tunneling deliberately configured, will show your real IP handling DNS — which is exactly what you configured, not a leak. For a meaningful result, run the test while your VPN is connected and actively passing traffic.

### Cage — what DNS traffic is flowing on my network?

**What it checks:** the actual DNS packets leaving your machine — what is being queried, by what, and whether any of it looks hostile (beaconing on a regular schedule, data smuggled out through DNS, queries to known-bad domains).

**How to run it:**

- App: select **Cage** in the sidebar and start a capture. Packet capture requires administrator rights.
- Command line (requires sudo):

  ```
  sudo syxx cage --duration 60   # capture port-53 DNS packets for 60 seconds
  ```

  In the guided `syxx` menu, Cage asks how long to keep the capture running (1 to 9000 seconds; just press Enter for 60). While it runs, use the web and your apps normally — you have to generate DNS queries for there to be anything to analyze.

**How to read the result:** Cage reports how many queries and responses it ingested, per-signal findings, and a capture-only verdict. One honest limitation to know: a passive capture cannot prove your network is clean. Watching traffic can reveal threats, but it cannot run the active cross-checks (like comparing answers against independent vantage points) that a clean verdict requires — so those sections stay untested, and Cage will never hand out a clean bill of health from a capture alone.

**When it says untested:** the capture could not start (for example, administrator rights were declined) or produced no readable records. Syxx says so explicitly: nothing was captured, nothing was measured, and that is not a clean result.

**Common false alarm:** plenty of legitimate software phones home on a fixed schedule — operating system update checks, antivirus, app telemetry. That regular rhythm looks a lot like beaconing. Before worrying, check which program sent the queries and whether the domain belongs to a vendor you actually use.

### Witness — can I trust this evidence?

**What it checks:** whether your environment has changed since Syxx last looked (resolvers, interfaces, VPN, DNS encryption), and it lets you cryptographically sign test results so you can later prove they were not altered.

**How to run it:**

- App: select **Witness** in the sidebar (a Pro section). Provide your Ed25519 private key (it stays on this machine and is never sent anywhere; guard it — whoever holds it could forge testimony in your name), then use **Witness Testimony** to sign or **Verify Testimony** to check a signature.
- Command line:

  ```
  syxx witness --gen-keypair                    # create a signing keypair
  syxx witness create example.com --key <key>   # sign evidence for a domain comparison
  syxx witness verify <file>                    # verify a signed evidence file
  syxx sentinel                                 # detect changes to your network configuration
  syxx sentinel --set-home                      # mark the current network as your home baseline
  ```

**How to read the result:** Witness shows a snapshot of your environment and compares it against the last one. "No change" means your environment is stable since the previous run. If this is your first run, there is nothing to compare against — Syxx tells you the current snapshot has now become the baseline. That is a starting point, not a verdict: a first snapshot has verified nothing yet.

**When it can't answer:** verifying a signature without the matching public key, or comparing with no prior baseline, produces no conclusion — Witness reports what it could and could not establish rather than rounding up to "trusted."

**Common false alarm:** taking your laptop from home to a coffee shop legitimately changes your resolvers and interfaces, and Witness will flag it as a change — because it is one. Use `syxx sentinel --set-home` on your trusted network, then `syxx sentinel --home` to compare against that baseline instead of whatever network you used last.

---

## How to read your report card

The report card is the combined answer across everything you ran. In the app it lives in the **Conclusion** section; on the command line, press **C** in the `syxx` menu or read the end of `syxx firstrun`.

It contains:

- **Grade:** A through F — or **N/A** when the run could not be measured. N/A is not a passing grade. If probes failed or nothing was collected, Syxx refuses to grade rather than invent a score.
- **Headline and top reasons:** one sentence summing up the finding, and the top reasons behind the grade.
- **Verdict:** clean, a named threat, inconclusive, or untested — with a severity level, and where applicable a strength percentage for the leading diagnosis.
- **What this means / Do this:** a plain-language explanation of the verdict and the single most useful next action.
- **Why we think so:** the reasoning, in ordinary words.
- **Key observations:** the individual pieces of evidence, labeled by which module produced them. If no evidence was collected, the card says so instead of showing a verdict conjured from nothing.
- **Unverified signals:** things Syxx saw that looked suspicious but could not confirm. These are shown separately so you know about them without them being dressed up as proven findings.

Two honesty rules govern every card: the verdict is only drawn from checks that actually completed, and anything that did not complete is shown as untested or inconclusive — never silently counted as clean.

---

## What to do if a threat is detected

1. **Read the report.** Syxx tells you exactly what it found, which test detected it, and what it could indicate.

2. **Check the recommended action.** Each detection includes a concrete next step. For example:
   - **Maliciously redirected:** do not enter credentials on the affected site. Change your DNS resolver to a trusted provider like Cloudflare (1.1.1.1) or Quad9 (9.9.9.9) and re-run the scan.
   - **Content mismatch:** try the same site over a different network (for example, switch from Wi-Fi to a cellular hotspot) and re-run the check.
   - **DNS leak (collector-confirmed):** switch to a different VPN server or protocol, or contact your VPN provider.

3. **Save the evidence.** Sign your results so you can prove later that they were not altered: in the app, use **Witness Testimony**; on the command line, run `syxx witness --gen-keypair` once, then `syxx witness create <domain> --key <your-key>`.

4. **Run a second scan.** After taking the recommended action, scan again to confirm the threat is gone.

5. **Still seeing threats?** Contact support at feedback@milkandgin.com. Include your signed evidence file if possible.

---

## Command line reference

Run `syxx --help` for the full list. Verified subcommands:

| Command | What it does |
|---|---|
| `syxx` | Interactive guided mode (menu of all six checks plus Conclusions) |
| `syxx firstrun [--domain <d>]` | CLI-only guided probe set (not the GUI). The GUI never auto-scans on first launch; modules run only when you invoke them. |
| `syxx nslook <domain> [type] [@server] [--dnssec]` | DNS lookup with type and resolver control |
| `syxx mirror <domain> [--local <resolver>]` | Compare your resolver's answers against public vantage points |
| `syxx warp <url> [--diff]` | Web content integrity check via the syxx.app proxy (Pro) |
| `syxx key` | Resolver / interface / VPN / captive-portal / encrypted-DNS state |
| `syxx route <target>` | Traceroute path analysis |
| `syxx lan` | Map on-link devices from the ARP table; flags your DNS resolver |
| `syxx leak [--collector <url>] [--key <license>]` | VPN DNS leak test (collector confirms the result) |
| `syxx cage [--duration <seconds>] [--keep]` | Capture port-53 DNS packets (requires sudo) |
| `syxx witness create\|verify\|report\|--gen-keypair` | Sign and verify tamper-evident evidence |
| `syxx sentinel [--set-home] [--home] [--reset] [--forget]` | Detect changes to your DNS-relevant configuration |
| `syxx speed [domain]` | Rank your configured resolvers by latency |
| `syxx filter [@resolver]` | Verify DNS blocklist enforcement and bypass paths |
| `syxx forensics` | DNS forensics analysis (domain classification, exfil, beaconing) |
| `syxx unbound [verify\|status\|enable\|disable]` | Local DNSSEC-validating resolver |

Exit codes for scripting: `0` = clean, `1` = threat detected, `2` = inconclusive or untested, `3` = error. Note that `2` is the default until a run proves otherwise — Syxx never exits "clean" without evidence.

---

## Pro features

Syxx's free tier includes local scans and grades. Pro ($3.95 per month or $19.95 per year) unlocks the server-anchored features — the checks that need Syxx's own infrastructure to give you a confirmable answer:

- **Mirror content checks:** page and script hash comparison against a clean copy fetched through the syxx.app proxy (`syxx warp`, and the Mirror section in the app).
- **Glutton leak testing:** the Glutton section in the app, and collector-confirmed leak verdicts with a signed attestation you can verify offline.
- **Witness:** the Witness section in the app for signing and verifying evidence.

In the app, Pro sections show a lock in the sidebar until a license is activated. Activate via the license screen in the app, or press **L** in the `syxx` command line menu. Your license key is validated with the licensing service and stored on your machine; it never travels alongside your DNS data, so your network findings stay unlinked from your identity.

---

## Support

If you have questions, run into problems, or need help understanding your results, contact us at:

**feedback@milkandgin.com**

When reporting an issue, please include:

- Your platform (Linux, Windows, or macOS) and version.
- The Syxx version (found in **Settings > About** or by running `syxx --version`).
- A description of what happened and what you expected.
- If possible, a signed Witness evidence file of your results.
