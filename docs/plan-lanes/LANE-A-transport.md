# LANE A — Transport engine (seat E1)

- **Date:** 2026-08-04
- **Stage:** PLANNING ONLY. Nothing in this lane has been built. No file outside this one was
  created, edited or deleted by seat E1.
- **Parent plan:** `docs/PLAN-linux-parity-team-2026-08-04.md` — this document fills the E1 half of
  its empty §7 gap register and its empty Phase-2 task list.
- **Goal:** every macOS encrypted-DNS-transport capability present and honest on Linux.
- **Keep-out inherited verbatim:** all GUI, all Cage, all Mirror, and everything in
  `docs/HANDOFF-mirror-warp-gui-2026-08-04.md` §1. This lane touches no `.axaml`, no `.xaml`, no
  view, no `WarpEngine.*`, no `ProphetPresentation.cs`.

---

## 0. Reading key

Every Linux mechanism below carries exactly one of two labels:

- **CONFIRMED-from-repo-code** — the claim is provable by reading a file in this repository, and the
  `file:line` is given. It is a fact about the tree, not about a running Ubuntu box.
- **INFERENCE-needs-real-Ubuntu-run** — the claim comes from the kernel/userland ABI or from
  distribution packaging, NOT from this repo. It may be high confidence and still be wrong on the
  owner's specific release. **No implementer may treat one of these as settled.** Each is attached to
  a §7 verification command whose expectations are written down before the run.

There is no third label. If something is neither provable from the tree nor verifiable by a command
in §7, it is in §8 ("what I could not determine") instead of in a task.

---

## 1. The two decisions this lane owes the team

### Decision D-A1 — the flow-observer mechanism (the `nettop` twin)

**The problem, stated exactly.** `FlowTransportObserver.activeFlows` (FlowTransportObserver.swift:195-204)
decides that a flow carried traffic during the interval if it is new since the previous sample, *or*
if `bytesIn`/`bytesOut` grew (`:200-202`). The comment at `:191-194` says why the growth test exists:

> "A flow that merely persists unchanged is NOT activity — counting the historical rows `nettop` keeps
> for closed sockets would turn old traffic into a current claim, which is exactly the staleness
> failure this design forbids."

Linux has no equivalent counter. `/proc/net/tcp`, `/proc/net/tcp6`, `/proc/net/udp` and
`/proc/net/udp6` expose `tx_queue:rx_queue`, which are **instantaneous queue depths, not cumulative
byte counters**, plus a UDP `drops` counter which is not traffic.
(INFERENCE-needs-real-Ubuntu-run — `proc(5)` / `net/ipv4/*_diag`; verified by §7 V-A9-1.)
So the anti-staleness test at `:200-202` has no direct Linux substitute for UDP, and a naive port
would either delete it (fabricating a current claim from a socket that has existed for hours) or
invert it (treating every persisting socket as idle, which hides live cleartext).

**Options considered.**

| Option | What it gives | Why it is not the whole answer |
|---|---|---|
| `/proc/net/{tcp,tcp6,udp,udp6}` alone | full system-wide flow table, world-readable, peer for connected sockets, `uid`, `inode` | no cumulative byte counters at all |
| `ss -tunap` | same table plus process attribution | `-p` needs privilege for other users' sockets; still no UDP counters |
| `ss -ti` (tcp_info) | **cumulative** `bytes_sent` / `bytes_received` per TCP socket | TCP only — DoT/853 and DoH/443 yes, Do53-over-UDP no |
| `/proc/net/snmp` `Udp: OutDatagrams` | cumulative, but **system-wide**, not per socket | cannot attribute; only usable as a one-sided proof |
| `conntrack -L` | reveals the peer of unconnected UDP/53 — would solve contract 9 outright | needs root, needs `nf_conntrack_acct=1`, and has **no process attribution**, so Syxx's own audit Do53 would be indistinguishable from the user's — a direct contract-6 violation (see §2) |
| eBPF on `udp_sendmsg` | per-socket peer + pid + payload-free: the real answer | needs `CAP_BPF`/root and a kernel-version-sensitive build; a tier-2 project, not this lane |

**RECOMMENDATION.** Build the tier-1 observer on `/proc/net/{tcp,tcp6,udp,udp6}` +
`/proc/<pid>/fd` inode mapping, and replace the boolean "did it grow" with an explicit **tri-state
activity result** — the same move this codebase already makes everywhere else it cannot measure:

```csharp
public enum FlowActivity { Grew, ProvenIdle, Unknown }
```

- **new key since last sample ⇒ `Grew`** — identical to FlowTransportObserver.swift:199.
- **TCP ⇒ `Grew` / `ProvenIdle` from `ss -ti` counters** — a real growth test with the same semantics
  as `nettop`. (INFERENCE-needs-real-Ubuntu-run: exact iproute2 field spelling; §7 V-A9-2.)
- **UDP ⇒ one-sided idleness proof.** Read `Udp: OutDatagrams` from `/proc/net/snmp` (and
  `Udp6OutDatagrams` from `/proc/net/snmp6`) at both ends of the interval. If the **global** counter
  did not move, then no UDP socket on the host sent anything, so this socket is `ProvenIdle` — a
  *sound* inference in that direction only. If it moved, we cannot attribute the datagrams to a
  socket, so the answer is `Unknown`. Never `Grew`.

**The one-sided activity rule** (this is the load-bearing part):

> `Unknown` activity may **never** contribute to `CleartextOffHost` and may **never** contribute to
> `EncryptedDns`. It contributes only to `TransportUnobservable`, and only for a DNS-relevant flow.

That single rule keeps every affected contract:
- contract 7 (unchanged flow ≠ activity) — `ProvenIdle` is dropped exactly as macOS drops it;
- contract 9 (unconnected UDP ⇒ undetermined) — `Unknown` pushes to `TransportUnobservable`, and
  `DNSTransportState.cs:228-229` turns that into `Undetermined`/`DnsActivityTransportUnobservable`;
- and it cannot fabricate **either** definite: not `ConfirmedFalse` from a stale `:53` socket, not
  `ConfirmedTrue` from a stale `:853` socket.

**The honest cost, stated up front so nobody "fixes" it later.** On a desktop running any QUIC/HTTP3
traffic, `Udp: OutDatagrams` grows every interval. The system resolver's persistent UDP socket will
therefore be `Unknown` on almost every tick, which means `TransportUnobservable > 0`, which means the
Linux tier-1 DNS signal will frequently sit at **Undetermined even when DoT is genuinely in use**.
That is the correct answer for what this tier can see, and it is *why* the privileged tier matters
more on Linux than on macOS. **Do not weaken the rule to make the graph greener.** Any future change
that lets `Unknown` reach `ConfirmedTrue` is the exact defect this product exists to refuse.

`TransportEvidence.Detail` must say so in words on every Linux sample — e.g. `"per-socket byte
counters do not exist for UDP on Linux; interval activity for N UDP socket(s) could not be
established"` — so the string that reaches a surface carries the limitation with it.

### Decision D-A2 — the dnscrypt-proxy service model

**RECOMMENDATION: app-managed child process, mirroring macOS exactly. Not a systemd unit.**

macOS's model is stated at DNSCryptProxyManager.swift:7-10: the proxy is "an app-managed background
child process tied to the enable/disable toggle — not an installed launchd daemon," listening only on
a private loopback port that Unbound forwards to.

Reasons to keep that model on Linux, in order of weight:

1. **A systemd unit lets someone else's configuration produce Syxx's success.** If Syxx adopted
   `systemctl start dnscrypt-proxy`, then `resolverReached == true` would be true of a proxy running
   the *distribution's* `/etc/dnscrypt-proxy/dnscrypt-proxy.toml` — possibly with no relay, possibly
   with `require_nolog = false`, possibly with remote `[sources]`. `classifyStart`
   (DNSCryptProxyConfig.swift:72-96) would then return `readyAnonymized`/`readyDirect` for a
   transport Syxx never configured and cannot describe. That is a fabricated measurement.
2. **`stop()` must actually stop it.** The macOS toggle owns the lifetime
   (DNSCryptProxyManager.swift:113-119). A shared system unit cannot be stopped without disrupting a
   configuration the user may have set up themselves.
3. **`verify-dnscrypt` must be admin-free and reproducible.** macOS runs the whole chain on high
   loopback ports with no system switch (SyxxUnbound.swift:39-73, UnboundManager.swift:601-607).
   A systemd unit needs root and mutates system state; the verification would stop being
   reproducible-by-a-normal-user.
4. Parity: `UnboundManager.cs:17-21` already documents the identical app-managed-child decision for
   Unbound on Windows/Linux. A different model for the proxy would be an unjustified divergence.

**The conflict that must be verified before this is built (INFERENCE-needs-real-Ubuntu-run).**
Ubuntu's `dnscrypt-proxy` package is believed to ship BOTH `dnscrypt-proxy.service` AND a
socket-activated `dnscrypt-proxy.socket`, enabled on install, binding a loopback address on port 53
(reportedly `127.0.2.1:53`), with `listen_addresses = []` in the shipped TOML because systemd hands
the proxy its listening fd. If that is true, three things follow and all three are hazards:

- **H1 — port theft.** A packaged instance already listening could occupy the port Syxx picks.
  Syxx's own `Process.Start` would fail to bind, but a probe query to `127.0.0.1:<port>` could still
  succeed *through the other instance*, producing `readyDirect`/`readyAnonymized` from a proxy Syxx
  did not configure. **Contract 3 catches part of this, and contract 1 catches the rest**: Syxx
  truncates and reads *its own* query-log path (DNSCryptProxyManager.swift:50-51, `:141-151`), and a
  foreign proxy will not write to it, so the log is empty despite a successful query ⇒
  `undetermined(reason: "query log is empty despite a successful query")` ⇒ `.inconclusive`. That is
  the correct outcome and it must not be "optimised away". A9/A3 additionally add an explicit
  pre-bind conflict check so the user gets a better message than `inconclusive`.
- **H2 — AppArmor.** If Debian/Ubuntu ships a confinement profile for the binary, a Syxx-generated
  TOML in a Syxx state directory may be unreadable to it, and the query-log path unwritable. The
  failure would look like "started but unreachable" — honest, but wrong-causally. §7 V-A3-2 checks it.
- **H3 — binary path and package name.** `locateBinary` on macOS searches Homebrew paths
  (DNSCryptProxyConfig.swift:195-203). Linux needs `/usr/sbin`, `/usr/bin`, `/usr/local/sbin`,
  `/usr/local/bin`, plus a `which`/`command -v` fallback, and the install hint must become
  `sudo apt install dnscrypt-proxy` rather than `brew install dnscrypt-proxy`
  (DNSCryptProxyConfig.swift:98).

**Explicitly rejected:** writing a Syxx-owned systemd unit (`syxx-dnscrypt.service`). It buys
persistence across reboot, which is a layer-4/tray concern that neither platform ships today, at the
cost of root, of a second lifetime that the toggle does not own, and of a state Syxx cannot honestly
describe after a reboot it did not observe.

---

## 2. The `dotnet syxx.dll` self-exclusion hazard (flagged; mitigation is mandatory, not optional)

**The hazard.** Contract 6 is enforced on macOS purely by process-name prefix:
`selfProcessPrefixes = ["syxx", "Syxx", "DeepSyxx", "SyxxGUI"]` (FlowTransportObserver.swift:96),
applied at `:224`. On Linux, `/proc/<pid>/comm` is the **thread name**, capped at 15 bytes and set
from the executable. If `syxx` is launched framework-dependent as `dotnet /opt/syxx/syxx.dll`, `comm`
reads `dotnet`. A name-prefix self-exclusion then silently fails, and Syxx's own deliberately
cleartext audit queries — `DNSResolver.QueryUDPDetailedAsync`, used by every scan, and by
`UnboundManager.cs:346-353` — become `CleartextOffHost` evidence. `DNSTransportState.cs:220-221`
turns one such flow into `ConfirmedFalse`/`CleartextQueryObserved`. **Syxx would tell the user their
DNS is unencrypted on the strength of Syxx's own traffic.** It fails *silently and in the alarming
direction*, so it will not be noticed as a bug — it will be believed.

The same failure occurs for a self-contained publish renamed by packaging, for a snap wrapper
(`/snap/syxx/current/...`), and for `dotnet test` running the observer inside the test host, where
`comm` is `testhost` or `dotnet`.

**Mitigation (four layers; layer 1 is the actual fix).**

1. **Self-exclusion by socket inode, never by name.** `/proc/self/fd` is always fully readable by the
   process itself at any privilege level. Enumerate it on **every** sample (sockets are ephemeral),
   collect every `socket:[N]` inode into `selfInodes`, and drop any `/proc/net/*` row whose `inode`
   is in that set — before classification, exactly where macOS drops by name
   (FlowTransportObserver.swift:224). This is strictly stronger than the macOS test: it is an
   identity, not a heuristic, and it cannot be defeated by a rename, a wrapper, a symlink or a
   framework-dependent launch. (INFERENCE-needs-real-Ubuntu-run: `/proc/self/fd` readability under
   snap confinement — §7 V-A9-3.)
2. **Also exclude by pid** — `Environment.ProcessId` and any pid whose `/proc/<pid>/exe` resolves to
   the same inode as `/proc/self/exe`. Covers a Syxx helper process we spawned.
3. **Keep a name net, but read the right file.** Retain `SelfProcessPrefixes` as parity with
   FlowTransportObserver.swift:96, and match it against `/proc/<pid>/cmdline` (full argv, so
   `dotnet /opt/syxx/syxx.dll` contains `syxx`) and against the basename of the `/proc/<pid>/exe`
   symlink target — **never against `/proc/<pid>/comm`**. A comment must say why.
4. **Fail closed.** If `/proc/self/fd` cannot be enumerated, the sampler returns
   `SampleResult` with `Flows == null` ⇒ `ObserverUnavailable` (contract 10). Classifying without a
   complete self-exclusion set is forbidden. There is no partial-credit path.

**Do NOT do this:** the tempting alternative — "exclude flows whose local port matches a port we
opened" — is not sound, because the audit socket is closed by the time the next sample is taken and
the port is reusable.

The regression test for this is **A13-T1**, spelled out in §5, and it is the single highest-value
test in this lane.

---

## 3. Interface contracts with other seats

`NetworkKeyState.cs` is **E2-owned** (parent plan §3). E1 does not edit it. E1 needs only what
already exists — all four are CONFIRMED-from-repo-code and require **no signature change**:

| Member | Where | E1 uses it for |
|---|---|---|
| `NetworkKeyState.GetScopedResolvers()` | NetworkKeyState.cs:148 | `EncryptedResolverAddresses` (A11) — twin of DNSTransportMonitor.swift:66-72 |
| `NetworkKeyState.GetSystemDNSServers()` | NetworkKeyState.cs:132 | network fingerprint (A11) — twin of DNSTransportMonitor.swift:53-61 |
| `NetworkKeyState.GetActiveInterfaces()` | NetworkKeyState.cs:375 | network fingerprint (A11) |
| `NetworkKeyState.VpnStatus()` | NetworkKeyState.cs:417 | the VPN signal (A11) — twin of DNSTransportMonitor.swift:89-93 |

**Two requests to E2, to be agreed before A11 is written** (both are E2's call; E1 builds against
whatever E2 lands):

- **C-1.** `DnsProtocol` (NetworkKeyState.cs:55-59) has `{ Doh, Dot }`. If A1 adds
  `EncryptedDNSTransport.Dnscrypt`, a resolver configured through the DNSCrypt path has no
  `DnsProtocol` value to report, and `EncryptedDNSStatus.ConfiguredTransport`
  (NetworkKeyState.cs:51) would have to stay null — which is honest but under-informative. E1
  proposes E2 adds `DnsProtocol.Dnscrypt`. **E1 will not depend on it**: A11 keys
  `EncryptedResolverAddresses` off `Protocol != null`, exactly as DNSTransportMonitor.swift:68 does,
  so a null protocol simply omits the address and the port-443 branch stays conservative.
- **C-2.** `NetworkKeyState.RunProcess` (NetworkKeyState.cs:313-340) calls
  `process.StandardOutput.ReadToEnd()` **after** `WaitForExit` while both stdout and stderr are
  redirected. That is the D104 pipe-buffer deadlock that `UnboundManager.cs:113-117` and
  `EncryptedDNSConfigurator.cs:142-145` were both explicitly fixed for. E1 raises it and does **not**
  fix it (not E1's file); E1's own new files use the drain-concurrently pattern. Route to E2/L.

To **E3** (owns `Windows/dotNET/Syxx.Tests/**`): every test in §5 is specified here but must be
written into E3-owned files, or ownership of one new test file must be transferred to E1. **This
needs an L decision before A13 starts** — see §8.

To **L**: two new user-facing string sets (A6's `verify-dnscrypt` output, A3's DNSCrypt result
copy) must be **appended** to `outputs-catalog-general-mode.csv` and
`outputs-catalog-story-mode.csv` (house rule 2 — append only). Those files are unowned in parent
plan §3.

---

## 4. Tasks

Effort key: **S** ≤ half a day, **M** ≈ 1–2 days, **L** ≈ 3+ days, each assuming the real-Ubuntu
verification in §7 is available on request.

---

### A1 — DNSCrypt resolver profiles in the catalog

**1. What.** `Windows/dotNET/Syxx.Kit/EncryptedDNSConfig.cs` (owned). Add:

```csharp
public sealed record DNSCryptRelay(string Name, string Stamp);

public sealed record EncryptedResolver(
    string Id, string DisplayName, string DohUrl, string DotHost, IReadOnlyList<string> Addresses,
    string? DnscryptServerName = null,
    string? DnscryptStamp = null,
    IReadOnlyList<DNSCryptRelay>? DnscryptRelays = null)
{
    public bool SupportsDnscrypt => DnscryptStamp is not null && DnscryptServerName is not null;
    public IReadOnlyList<DNSCryptRelay> Relays => DnscryptRelays ?? Array.Empty<DNSCryptRelay>();
}

public enum EncryptedDNSTransport { Doh, Dot, Dnscrypt }   // was { Doh, Dot } at :20

public static class EncryptedDNSConfig
{
    // Catalog grows 3 -> 11 entries.
    public static IReadOnlyList<EncryptedResolver> DnscryptProfiles { get; }
    public static EncryptedResolver? DefaultDnscryptResolver { get; }
}
```

**2. macOS reference.** `EncryptedDNSConfig.swift:20-24` (`DNSCryptRelay`), `:34-62`
(`EncryptedResolver` DNSCrypt fields + `supportsDNSCrypt`), `:64-73` (three-case transport enum),
`:100-148` (the eleven catalog entries, with the pinned Quad9 DNSCrypt stamp at `:115` and three
relay stamps at `:117-119`), `:156-160` (`dnscryptProfiles` / `defaultDNSCryptResolver`).
Current C# state: `EncryptedDNSConfig.cs:20` (two-case enum), `:37-48` (three catalog entries), no
relay type anywhere in the tree.

**3. Linux mechanism.** None — this file is pure data and pure functions, no OS calls.
**CONFIRMED-from-repo-code**: `EncryptedDNSConfig.cs` has no `using System.Diagnostics`, no process
launch, no I/O. Copy the stamps **byte-for-byte** from `EncryptedDNSConfig.swift:115-119`; do not
re-derive, do not re-fetch, do not reformat. The provenance comment at
`EncryptedDNSConfig.swift:104-110` (minisign-signed `v3/public-resolvers.md` and `v3/relays.md`,
fetched 2026-07-21) must be carried across verbatim, because a stamp with no provenance is a pinned
trust decision with no audit trail.

**4. Honesty contract.** Contract 2, in its data form:

> "readyDirect … Still encrypted, but the resolver sees the client IP — a materially weaker state
> that must be surfaced, never dressed up as anonymized." (DNSCryptProxyConfig.swift:31-33)

The wrong output that would look like success here is a **relay list silently dropped in
translation**: `Relays` defaulting to empty for Quad9 would make `relaysConfigured == false` at
DNSCryptProxyConfig.swift:79/85-89, so a proxy that failed every relay would report the *softer*
unreachable string and — worse — a proxy that succeeded without a relay would be `readyDirect` with
`relaysConfigured == false`, i.e. "no relay was asked for" rather than "the relay we asked for did
not happen". Same enum case, different truth.

**5. Failure-case test (A13-T2).** Assert `EncryptedDNSConfig.Resolver("quad9")!.Relays.Count == 3`
and that the three `Stamp` values are string-equal to the three literals at
`EncryptedDNSConfig.swift:117-119`. Assert `Catalog.Count == 11` and that the `Id` sequence equals
the macOS `id` sequence in order. The test trips on a dropped relay, a reordered catalog, or a
transcription typo in a stamp.

**6. Dependency order / effort.** First task in the lane; A2, A3, A5, A8 all consume it. **M.**

**7. Verification.** Data-only; no real run needed. Covered by A13-T2 in CI. Cross-check with:
```bash
python3 scripts/cross-platform-parity-gate.py; echo "exit=$?"
```
- **PASS** — `exit=0`.
- **FAIL** — `exit=1` (a wire vocabulary diverged; read the annotation).
- **INCONCLUSIVE** — `exit=2` (the gate could not run: bad paths / unreadable source). `exit=2` is
  **not** a pass; re-run from the repo root and report the stderr.

---

### A2 — `DNSCryptProxyConfig.cs` (new): the pure DNSCrypt core

**1. What.** New file `Windows/dotNET/Syxx.Kit/DNSCryptProxyConfig.cs`. Public surface:

```csharp
public enum DNSCryptResultKind { ReadyAnonymized, ReadyDirect, NotInstalled, Unreachable, Failed, Inconclusive }

public sealed record DNSCryptResult(
    DNSCryptResultKind Kind, ushort? Port = null, string? Resolver = null,
    string? Relay = null, string? Reason = null)
{
    public bool IsReady => Kind is DNSCryptResultKind.ReadyAnonymized or DNSCryptResultKind.ReadyDirect;
    public bool IsAnonymized => Kind == DNSCryptResultKind.ReadyAnonymized;
    public ushort? ReadyPort => IsReady ? Port : null;
}

public sealed record ProxyConfig(
    ushort Port, string ResolverName, string ResolverStamp,
    IReadOnlyList<DNSCryptRelay> Relays, string QueryLogPath,
    string ListenAddress = "127.0.0.1",
    IReadOnlyList<string>? BootstrapResolvers = null);

public static class DNSCryptProxyConfig
{
    public static DNSCryptResult ClassifyStart(ushort port, string resolver, string? relay,
        bool binaryFound, bool processStarted, bool resolverReached, bool viaRelay,
        bool relaysConfigured, string? failureReason = null);
    public static string GenerateToml(ProxyConfig c);
    public static string? LocateBinary();
    public static string InstallHint { get; }
}
```

`ClassifyStart` and `GenerateToml` must be **pure** — no process, no network, no file I/O — for the
same reason as DNSCryptProxyConfig.swift:63-64 ("Kept pure so every state is unit-testable without
spawning a process or touching the network").

**2. macOS reference.** `DNSCryptProxyConfig.swift` in full: `:28-59` (the six-case result and its
three accessors), `:72-96` (`classifyStart`, the honesty gate), `:98` (`installHint`), `:105-131`
(`ProxyConfig`), `:136-187` (`generateToml`), `:193-213` (`locateBinary`).

**3. Linux mechanism.**
- `LocateBinary` — candidate list becomes `/usr/sbin/dnscrypt-proxy`, `/usr/bin/dnscrypt-proxy`,
  `/usr/local/sbin/dnscrypt-proxy`, `/usr/local/bin/dnscrypt-proxy`, then a
  `/usr/bin/which dnscrypt-proxy` fallback. **INFERENCE-needs-real-Ubuntu-run** — the Debian package
  layout is not derivable from this repo; §7 V-A2-1 settles it. Keep the macOS Homebrew paths in the
  non-Linux branch so the same file still works on a macOS build host, matching the existing
  per-OS-candidate pattern at `UnboundResolver.cs:176-197` (CONFIRMED-from-repo-code).
- `InstallHint` — must be OS-dependent: `"dnscrypt-proxy is not installed. Install it with: sudo apt
  install dnscrypt-proxy"` on Linux, `choco` on Windows, `brew` on macOS. Precedent for a
  multi-OS hint string is `UnboundResolver.cs:68-69` (CONFIRMED-from-repo-code).
- The generated TOML is byte-identical to macOS. `listen_addresses = ['127.0.0.1:<port>']` is
  correct for an app-managed child (D-A2) and is **exactly what must not be blanked**: a
  socket-activated packaged instance uses `listen_addresses = []`, and copying that pattern would
  make Syxx's child inherit nothing and listen nowhere.
  **INFERENCE-needs-real-Ubuntu-run** on the packaged unit's shape; §7 V-A3-1.
- **PATH-hijack note (D102 family).** `LocateBinary`'s `which` fallback must resolve to an absolute
  path and be re-checked with `File.Exists` before use, mirroring DNSCryptProxyConfig.swift:207-211.
  The absolute-path discipline established at `EncryptedDNSConfigurator.cs:124-131` and
  `UnboundManager.cs:89-102` applies (CONFIRMED-from-repo-code), and see A8 for the `resolvectl`
  half of that gap.

**4. Honesty contract.** Contracts 2, 3 and 4 all live in `ClassifyStart`:

> "the app must NEVER claim DNSCrypt is protecting the user on the strength of a process having
> spawned. A proxy being *launched* is not proof it *reached a resolver*, and 'reached a resolver
> directly' is NOT the same as 'reached it through a relay.'" (DNSCryptProxyConfig.swift:16-20)
> "the feature must NEVER silently fall back to plain recursion (or to non-anonymized direct
> DNSCrypt) without saying so." (`:19-20`)

Wrong output that looks like success: **`ReadyDirect` returned when `resolverReached == false`.** The
C# translation risk is real and specific — a `switch` expression or an early `IsReady` shortcut that
checks `processStarted` before `resolverReached` inverts the guard at `:83-90`. The user then sees
"DNSCrypt active, direct" for a proxy that has never spoken to a resolver, while Unbound forwards to
a dead port and DNS breaks — or worse, while something else answers on that port.
Second wrong output: `ReadyAnonymized` when `relay == null`. macOS guards this with
`if viaRelay, let relay = relay` (`:92`); a C# `if (viaRelay)` without the null check would produce
`ReadyAnonymized(Relay: null)` and a surface would render "anonymized via (null)".

**5. Failure-case test (A13-T3).** A truth-table test over all 2^5 combinations of
(`binaryFound`, `processStarted`, `resolverReached`, `viaRelay`, `relaysConfigured`) × relay
null/non-null, asserting the exact `DNSCryptResultKind` for each. It must contain these three
explicit assertions, each of which trips on the failure named above:
- `ClassifyStart(processStarted: true, resolverReached: false, viaRelay: true, relay: "anon-cs-nyc", relaysConfigured: true).Kind == Unreachable` — **not** `ReadyAnonymized`;
- `ClassifyStart(resolverReached: true, viaRelay: true, relay: null, ...).Kind == ReadyDirect` — **not** `ReadyAnonymized`;
- `ClassifyStart(binaryFound: false, processStarted: true, resolverReached: true, ...).Kind == NotInstalled` — the binary check dominates (`:81`).
Plus a golden-vector test: `GenerateToml` output string-equals a fixture captured from
`DNSCryptProxyConfig.generateToml` on macOS for the same `ProxyConfig`, so `require_nolog`,
`skip_incompatible`, the empty `[sources]` and the `[anonymized_dns] routes` block cannot silently
drift. Note that `[sources]` being empty is load-bearing (DNSCryptProxyConfig.swift:184-185): a
non-empty `[sources]` would make the proxy fetch a remote resolver list, so the pinned stamps would
stop being the whole world and the config would stop being reproducible.

**6. Dependency order / effort.** After A1 (needs `DNSCryptRelay`). Blocks A3. **M.**

**7. Verification.**
```bash
# V-A2-1 — where the binary lives and whether the package exists at all
apt-cache policy dnscrypt-proxy 2>/dev/null | head -5
command -v dnscrypt-proxy; echo "which-exit=$?"
dpkg -L dnscrypt-proxy 2>/dev/null | grep -E '/(s?bin)/' || echo "NOT-INSTALLED"
dnscrypt-proxy -version 2>&1 | head -2; echo "version-exit=$?"
```
- **PASS** — a path under `/usr/sbin` or `/usr/bin` is printed and `-version` prints a version.
  Record the exact path; it goes into the `LocateBinary` candidate list verbatim.
- **FAIL** — `apt-cache policy` shows the package exists but `dpkg -L` prints a path that is in
  **none** of the planned candidates (then the candidate list is wrong and must be corrected).
- **INCONCLUSIVE** — `NOT-INSTALLED` and `which-exit=1`. This is a legitimate outcome: it means the
  A2 `NotInstalled` path is the one under test, and V-A3-* cannot run until the owner installs the
  package. It is **not** a pass for `LocateBinary`; the bar stays open.

---

### A3 — `DNSCryptProxyManager.cs` (new): the child-process glue

**1. What.** New file `Windows/dotNET/Syxx.Kit/DNSCryptProxyManager.cs`.

```csharp
public sealed class DNSCryptProxyManager : IDisposable
{
    public const ushort LoopbackPort = 5354;
    public string StateDir { get; }
    public DNSCryptProxyManager(string? stateDir = null);
    public bool IsRunning { get; }
    public Task<DNSCryptResult> StartAsync(EncryptedResolver resolver, ushort port = LoopbackPort);
    public void Stop();

    internal enum RelayLogKind { Relay, Direct, Undetermined }
    internal sealed record RelayLogResult(RelayLogKind Kind, string? Name = null, string? Reason = null);
    internal static RelayLogResult RelayFromQueryLog(string path, IReadOnlyList<string> knownRelays);
}
```

`RelayFromQueryLog` must be `internal static` and pure over a file path so it is testable from a
fixture without spawning anything — the C# equivalent of macOS keeping it a private method on a
type whose logic is exercised by the pure core.

**2. macOS reference.** `DNSCryptProxyManager.swift` in full: `:11-27` (type, port constant,
state dir, `isRunning`), `:35-111` (`start`, including the 400 ms liveness check at `:73-78`, the
20-second readiness poll at `:80-93`, and the D093 branch at `:106-110`), `:113-119` (`stop`, with
`terminate` → 0.3 s → `SIGKILL`), `:124-128` (`queryReaches`), `:130-158` (`RelayLogResult` and
`relayFromQueryLog`, with `:143` no-relays-is-genuinely-direct, `:145-147` unreadable-is-undetermined,
`:149-151` empty-is-undetermined).

**3. Linux mechanism.**
- **Child process** — `System.Diagnostics.Process` with `RedirectStandardError = true`,
  `UseShellExecute = false`, args `["-config", tomlPath]`. Must drain stderr with
  `ErrorDataReceived` + `BeginErrorReadLine`, **not** `ReadToEnd`, exactly as
  `UnboundManager.cs:296-306` does (CONFIRMED-from-repo-code, D104): a healthy dnscrypt-proxy logs
  to stderr continuously, so a blocking read deadlocks the readiness poll and the whole feature
  would hang, not fail. macOS gets away with `availableData` at DNSCryptProxyManager.swift:75
  because it only reads once, immediately after a 400 ms window.
- **Stop** — `Process.Kill(entireProcessTree: false)` then `WaitForExit(1500)`. .NET's `Kill()` sends
  `SIGKILL` on Unix, so the macOS `terminate` (SIGTERM) → grace → `SIGKILL` escalation at
  DNSCryptProxyManager.swift:116-118 has **no direct .NET API**. Options: (a) accept SIGKILL and
  document the divergence — dnscrypt-proxy holds no persistent state Syxx cares about and the query
  log is truncated on every start anyway (`:50-51`); (b) P/Invoke `kill(pid, SIGTERM)` first.
  **Recommend (a)** with an explicit comment naming the divergence and its reason, so the parity
  auditor classifies it JUSTIFIED rather than UNNECESSARY.
  **INFERENCE-needs-real-Ubuntu-run**: whether a SIGKILLed dnscrypt-proxy leaves anything behind
  (it should not) — §7 V-A3-3.
- **State dir** — `/var/lib/syxx/dnscrypt` when running as root, else
  `$XDG_STATE_HOME/syxx/dnscrypt` (fallback `~/.local/state/syxx/dnscrypt`). Must be created via
  `SyxxPaths.EnsurePrivateDir` (SyxxPaths.cs:17-32, CONFIRMED-from-repo-code) and the written TOML
  passed to `SyxxPaths.RestrictToOwner` (`:38-52`) — the TOML pins which resolver the user trusts,
  and `UnboundManager.cs:148-150` sets the precedent of a per-user state dir. Note macOS uses a
  machine-wide `/Library/Application Support/Syxx/dnscrypt` (DNSCryptProxyManager.swift:24) with the
  "never NSHomeDirectory under sudo" rationale at `:23`; the same rationale gives the root/non-root
  split above rather than a blind `LocalApplicationData`, because under `sudo` .NET's
  `LocalApplicationData` resolves to root's home and the file becomes invisible to the user.
- **Pre-existing-listener guard (H1 from D-A2).** Before `Process.Start`, attempt a
  `UdpClient`/`Socket` bind to `127.0.0.1:<port>`; if it fails with `AddressAlreadyInUse`, return
  `Failed(reason: "port <n> on loopback is already in use — refusing to start; another
  dnscrypt-proxy (for example the packaged systemd unit) may be listening. Syxx will not report on a
  proxy it did not configure.")`. **INFERENCE-needs-real-Ubuntu-run** — §7 V-A3-1.
- **Readiness** — reuse `DNSResolver.QueryUDPDetailedAsync` against `127.0.0.1:<port>` with the same
  20 × 1 s poll as `:84-87`. The existing pattern is `UnboundManager.cs:335-353`
  (CONFIRMED-from-repo-code).

**4. Honesty contract.** Contract 1 (D093), quoted from the code that implements it:

> "D093: an unreadable query log is NOT 'direct' — it is 'we don't know.' Report inconclusive rather
> than fabricating a definite 'NOT anonymized' conclusion." (DNSCryptProxyManager.swift:107-108)
> "The old nil-means-both collapsed an unreadable log into 'direct, NOT anonymized' — a fabricated
> definite." (`:131-132`)

Wrong output that looks like success: **`ReadyDirect` from an unreadable or empty query log.** In C#
this is one `catch { return Direct; }` away, and it is doubly attractive to write because
`ReadyDirect` reads like a *cautious* answer — it is the weaker of the two success states, so it
does not feel like an overclaim. It is one anyway: the user is told "the resolver can see your IP"
as a fact, when the truth is "we could not tell whether the relay was used". Under H1 it is also the
precise output that would launder a *foreign* proxy into a Syxx result. The correct output is
`Inconclusive(reason: "reached resolver <name> but could not read the query log to determine relay
use: <why>")` (`:109`).

Also contract 3, in `StartAsync`: `processStarted == true` must mean **our** child is alive
(`p.HasExited == false` after the 400 ms window, `:74`), never "the port answers".

**5. Failure-case test (A13-T4), three arms, all from fixtures — no process:**
- **unreadable** — `RelayFromQueryLog("/nonexistent/query.log", ["anon-cs-nyc"])` ⇒
  `Kind == Undetermined`. Assert **explicitly** `Kind != Direct`. This is the arm that trips on the
  `catch { return Direct; }` regression.
- **empty** — write a zero-byte file, `knownRelays` non-empty ⇒ `Kind == Undetermined` with a reason
  mentioning the log was empty (`:149-151`).
- **no relays configured** — `knownRelays` empty ⇒ `Kind == Direct` (`:143`). This arm must pass, or
  the fix for the first two has over-rotated into "everything is undetermined", which is its own
  dishonesty (an unaskable question reported as an unanswered one).
Plus a fourth: a fixture log whose last PASS line's trailing tab-column is `anon-cs-nyc` ⇒
`Kind == Relay, Name == "anon-cs-nyc"`, parsed from the **last** matching line scanning in reverse
(`:153-155`).

**6. Dependency order / effort.** After A2. Blocks A5. **L** — the process lifecycle, the readiness
poll, the state-dir privilege split and the bind guard are each small but each has a failure mode.

**7. Verification.**
```bash
# V-A3-1 — is a packaged dnscrypt-proxy already running / socket-activated? (hazard H1)
systemctl list-unit-files 'dnscrypt-proxy*' --no-pager 2>/dev/null
systemctl is-active dnscrypt-proxy.service dnscrypt-proxy.socket 2>/dev/null
ss -lunp 2>/dev/null | grep -E ':(53|5354|5355)\b' || echo "NO-LISTENER-ON-53/5354/5355"
grep -n '^listen_addresses' /etc/dnscrypt-proxy/dnscrypt-proxy.toml 2>/dev/null || echo "NO-PACKAGED-TOML"
```
- **PASS (for the plan as written)** — `NO-LISTENER-ON-53/5354/5355`, units `inactive`/`disabled`.
  The app-managed child model works unmodified.
- **FAIL** — a listener is bound on **5354** or **5355**. The default ports must move, and the
  pre-existing-listener guard becomes mandatory rather than defensive. Report the exact ports.
- **INCONCLUSIVE** — `systemctl` unavailable or the commands produce no output because the package
  is not installed (see V-A2-1). Nothing about H1 is settled; the guard is built anyway and the bar
  stays open.

```bash
# V-A3-2 — AppArmor confinement (hazard H2)
aa-status 2>/dev/null | grep -i dnscrypt || echo "NO-APPARMOR-PROFILE-FOR-DNSCRYPT"
ls /etc/apparmor.d/ 2>/dev/null | grep -i dnscrypt || echo "NO-PROFILE-FILE"
```
- **PASS** — both lines print the "NO-" sentinel: unconfined, so a Syxx-owned TOML and query log in
  a Syxx state dir are readable/writable.
- **FAIL** — a profile is listed in **enforce** mode. The state-dir choice must be re-planned
  (likely `/etc/dnscrypt-proxy/` sub-path or a profile local override), and A3's design changes.
- **INCONCLUSIVE** — `aa-status` needs root and was refused. Re-run with `sudo aa-status`.

```bash
# V-A3-3 — a hand-run of exactly what A3 will do, before any code exists
sudo -n true 2>/dev/null && echo "have-sudo" || echo "no-sudo"
D=$(mktemp -d); printf "%s\n" \
 "listen_addresses = ['127.0.0.1:5354']" "server_names = ['quad9-dnscrypt-ip4-filter-pri']" \
 "ipv6_servers = false" "dnscrypt_servers = true" "doh_servers = false" \
 "require_nolog = true" "cache = false" "netprobe_timeout = 60" \
 "bootstrap_resolvers = ['9.9.9.9:53', '1.1.1.1:53']" "ignore_system_dns = true" \
 "[static]" "  [static.'quad9-dnscrypt-ip4-filter-pri']" \
 "    stamp = 'sdns://AQMAAAAAAAAADDkuOS45Ljk6ODQ0MyBnyEe4yHWM0SAkVUO-dWdG3zTfHYTAC4xHA2jfgh2GPhkyLmRuc2NyeXB0LWNlcnQucXVhZDkubmV0'" \
 "  [static.'anon-cs-nyc']" "    stamp = 'sdns://gRExNDYuNzAuMTU0LjY3OjQ0Mw'" \
 "[anonymized_dns]" "  routes = [" \
 "    { server_name = 'quad9-dnscrypt-ip4-filter-pri', via = ['anon-cs-nyc'] }," "  ]" \
 "  skip_incompatible = true" "[query_log]" "  file = '$D/query.log'" "[sources]" > "$D/p.toml"
timeout 45 dnscrypt-proxy -config "$D/p.toml" & PID=$!
sleep 20; dig +time=5 +tries=1 @127.0.0.1 -p 5354 example.com A +short; echo "dig-exit=$?"
echo "--- query log ($(wc -c < "$D/query.log" 2>/dev/null || echo MISSING) bytes) ---"
cat "$D/query.log" 2>/dev/null | tail -3
kill $PID 2>/dev/null; wait $PID 2>/dev/null; echo "state-dir=$D"
```
- **PASS** — `dig` prints an A record, `dig-exit=0`, and the query log's last line's trailing
  tab-column is `anon-cs-nyc`. That closes contracts 1, 3 and 4 in one run and confirms
  `RelayFromQueryLog`'s column position.
- **FAIL** — `dig` succeeds but the query log is **missing or zero bytes**. That is exactly the
  D093 case; A3 must report `Inconclusive` for it, and this run proves the case is reachable on
  Linux. (It is a FAIL for "the relay column is where we think it is", and simultaneously a PASS for
  "the D093 branch is not dead code". Record both.)
- **INCONCLUSIVE** — `dig-exit` non-zero. Could be no network, a blocked port 443 to the relay, or a
  relay that cannot reach Quad9 (a known real case — EncryptedDNSConfig.swift:108-110 records
  `anon-scaleway` failing to Quad9 live). Re-run once with `via = ['anon-scaleway']` before drawing
  any conclusion. **Do not** read a failed dig as "DNSCrypt does not work on Linux".

---

### A4 — Unbound forward-to-DNSCrypt seam

**1. What.** `Windows/dotNET/Syxx.Kit/UnboundResolver.cs` (owned). Extend `UnboundConfig`
(currently `:44-53`) with `ushort? ForwardToDNSCryptPort = null`, and extend `GenerateConfig`
(`:73-117`) to emit a plain-loopback forward zone when it is set — **mutually exclusive** with the
existing `ForwardOverDoT` branch at `:108-115`.

**2. macOS reference.** `UnboundResolver.swift:82-88` (the field and its "NOT TLS" comment),
`:150-163` (the emission, `forward-addr: 127.0.0.1@<port>`).

**3. Linux mechanism.** None — pure string generation. **CONFIRMED-from-repo-code**:
`UnboundResolver.GenerateConfig` is a `StringBuilder` with no I/O (`UnboundResolver.cs:73-117`).
The one Linux-relevant detail is already handled: `username: ""` and `chroot: ""` at
`UnboundResolver.cs:103-104` with the rationale at `:101-102` ("do not drop to a (possibly
nonexistent) 'unbound' user or chroot") — on Debian/Ubuntu the `unbound` user *does* exist, but
dropping to it would make the Syxx-owned state dir unreadable, so the existing choice is correct on
Linux for a different reason than it is on macOS. Add that to the comment; do not change the code.

**4. Honesty contract.** Contract 4:

> "the feature must NEVER silently fall back to plain recursion" (DNSCryptProxyConfig.swift:19-20)

Wrong output that looks like success: **an Unbound config that emits *no* forward zone when
`ForwardToDNSCryptPort` is set but the emission branch is missed.** Unbound would then recurse
directly to the root over cleartext UDP/53 and answer every query correctly. Everything looks
healthy — `chainResolves` true, AD bit set, bogus zone rejected — and the DNSCrypt transport is
simply not in the path. This is the single most convincing false-clean in the whole lane, because
every downstream check still passes. It must be caught by A5's transport assertion, not by hope.
Second wrong output: emitting `forward-tls-upstream: yes` on the DNSCrypt forward zone. dnscrypt-proxy
listens plain on loopback (UnboundResolver.swift:83-84); TLS there would fail, and a lenient
implementation might then fall through to recursion.

**5. Failure-case test (A13-T5).** `GenerateConfig(cfg with ForwardToDNSCryptPort = 5354)` must
contain `forward-addr: 127.0.0.1@5354` and must **not** contain `forward-tls-upstream`. A second
assertion: `GenerateConfig(cfg with both ForwardOverDoT and ForwardToDNSCryptPort set)` must produce
exactly one `forward-zone:` block — pick DNSCrypt and say so, or throw; never emit two forward zones
for `.`, which Unbound resolves in an order Syxx does not control.

**6. Dependency order / effort.** Independent of A1–A3; can start immediately. Blocks A5. **S.**

**7. Verification.** Unit-testable; no real run needed for the string. The config's *acceptance* by
the Linux unbound build is checked as part of V-A5-1.

---

### A5 — `VerifyDNSCryptChainAsync` + `DNSCryptChainReport`

**1. What.** `Windows/dotNET/Syxx.Kit/UnboundManager.cs` (owned). Add:

```csharp
public sealed record DNSCryptChainReport(
    DNSCryptResult Proxy, bool ChainResolves, bool DnssecValidatedOverChain, string Detail)
{
    public bool Ok => Proxy.IsReady && ChainResolves && DnssecValidatedOverChain;
}

public Task<DNSCryptChainReport> VerifyDNSCryptChainAsync(
    EncryptedResolver resolver, ushort proxyPort = 5355, ushort unboundPort = 5356);
```

and change `StartProcessAsync` (`UnboundManager.cs:264`) to
`StartProcessAsync(string unbound, ushort port, ushort? forwardToDNSCryptPort = null)`, threading the
port into the `UnboundConfig` construction at `:289`.

**2. macOS reference.** `UnboundManager.swift:588-599` (`DNSCryptChainReport` and its strict `ok`),
`:601-607` (signature and the "reproducible without administrator rights" contract), `:608-641`
(binary check, proxy start, the honest `why` switch, both `defer`red stops), `:643-654` (the AD-bit
poll and the `dnssec-failed.org` negative control), `:656-665` (the detail string).

**3. Linux mechanism.**
- **High ports, no privilege** — 5355/5356 are unprivileged on Linux exactly as on macOS.
  **CONFIRMED-from-repo-code** that this is the intended shape: `UnboundManager.cs:236` already
  runs `VerifyAsync(5354)` with no elevation, and `UnboundCommand.cs:50` invokes it.
- **Verify state dir** — macOS uses a user-scoped `verifyStateDir` (UnboundManager.swift:613-614,
  D090: "so a non-root process can create the state directory"). C# must do the same rather than
  reuse `StateDir`; `UnboundManager.cs:148-150` already resolves `StateDir` under
  `LocalApplicationData`, which is user-scoped, so the C# side needs only a distinct `.../verify`
  subdirectory. **CONFIRMED-from-repo-code.**
- **`unbound-anchor`** — `StartProcessAsync` hard-fails without the root trust anchor
  (`UnboundManager.cs:286-287`). On Debian/Ubuntu the helper ships in the `unbound-anchor` binary
  inside the `unbound` package, and `/var/lib/unbound/root.key` may already exist and be
  root-owned. `LocateHelper` (`UnboundResolver.cs:173-174`) searches `/usr/sbin` first, which is
  where Debian puts it. **INFERENCE-needs-real-Ubuntu-run** — §7 V-A5-1. If `unbound-anchor` cannot
  write into the Syxx state dir as a normal user, the whole `verify` verb is root-only on Linux,
  which is a real parity loss and must be reported, not papered over.
- **Both children always stopped** — macOS uses `defer` twice (`:630`, `:641`). C# must use
  `try/finally`, and `DNSCryptProxyManager` implements `IDisposable` so `await using`/`using` is
  available. A leaked dnscrypt-proxy holding port 5355 makes the *next* verify hit the H1 path.

**4. Honesty contract.** Contract 3, at chain scope, plus the `Ok` conjunction:

> "Strict pass: proxy ready AND chain resolves AND DNSSEC validation proven over the chain."
> (UnboundManager.swift:597-598)

Wrong output that looks like success: **`Ok == true` computed without `Proxy.IsReady`.** If `Ok` were
`ChainResolves && DnssecValidatedOverChain`, then the A4 failure mode (no forward zone emitted) would
report a full PASS for a chain that ran entirely over cleartext recursion — DNSSEC proves the
*answer* was not tampered with, and says nothing whatever about the *transport*. The CLI would print
"the chain resolves and DNSSEC validates over encrypted DNSCrypt transport"
(SyxxUnbound.swift:67) about an unencrypted path. Conflating "validated" with "encrypted" is the
exact confusion `DNSTransportState.cs:14-18` exists to prevent (Q1/Q2/Q3 are different questions).

**5. Failure-case test (A13-T6).** Construct `DNSCryptChainReport` with
`Proxy = new DNSCryptResult(Unreachable)`, `ChainResolves = true`, `DnssecValidatedOverChain = true`
and assert `Ok == false`. Then `Proxy = ReadyDirect`, `ChainResolves = true`,
`DnssecValidatedOverChain = false` ⇒ `Ok == false`. Then `Proxy = Inconclusive` (the D093 case) with
both booleans true ⇒ `Ok == false` — an inconclusive transport can never carry a strict pass.

**6. Dependency order / effort.** After A3 and A4. Blocks A6. **M.**

**7. Verification.**
```bash
# V-A5-1 — can an unprivileged user obtain the DNSSEC root anchor and run unbound on a high port?
command -v unbound unbound-anchor; echo "locate-exit=$?"
D=$(mktemp -d); unbound-anchor -a "$D/root.key"; echo "anchor-exit=$?"
ls -l "$D/root.key" 2>/dev/null || echo "NO-ANCHOR-FILE"
head -c 120 "$D/root.key" 2>/dev/null; echo
```
- **PASS** — `root.key` exists and is non-empty. (`unbound-anchor` exits 1 on first fetch by design;
  `UnboundManager.cs:284` already comments that the file's presence is what matters, so
  `anchor-exit=1` **with** a file present is a PASS.)
- **FAIL** — `NO-ANCHOR-FILE`. `syxx unbound verify` and `verify-dnscrypt` are then unavailable to a
  non-root user on Linux, which is a parity loss versus macOS and must be ledgered as such, not
  hidden behind a generic failure message.
- **INCONCLUSIVE** — `locate-exit` non-zero (unbound not installed). Install `unbound` and re-run;
  nothing is decided.

---

### A6 — `syxx unbound verify-dnscrypt`

**1. What.** `Windows/dotNET/Syxx/Commands/UnboundCommand.cs` (owned). Add the sub-verb to the
`switch` at `:21-28`, to the usage string at `:18`, and to the unknown-action hint at `:27`. Add a
`RunVerifyDNSCrypt()` mirroring `RunVerify()` (`:45-56`). Adopt the macOS exit-code contract.

**2. macOS reference.** `SyxxUnbound.swift:15` (usage), `:23-24` (dispatch), `:32` (hint),
`:39-73` (`runVerifyDNSCrypt`, including the `readyDirect` warning at `:58-60` and the
`unreachable` "Not falling back to plain recursion" line at `:61-62`), `:11` (the exit-code
contract: 0 = pass/status, 2 = inconclusive, 3 = fail).

**3. Linux mechanism.** None — CLI text and dispatch. Two Linux-relevant notes:
- **Exit codes.** macOS has `CLIExitStatus` with a distinct **2 = inconclusive**
  (SyxxUnbound.swift:11, `:151`). `UnboundCommand.cs:74-99` returns nothing at all — `PrintResult`
  is `void` and `RunAsync` is `async Task`. **CONFIRMED-from-repo-code.** A shell caller on Linux
  (a systemd timer, a CI assertion, the owner's own script) therefore cannot distinguish
  inconclusive from pass. Fixing this is in-scope for parity and is the difference between an
  automatable honest result and a human-readable one. Requires setting `Environment.ExitCode`;
  check with L whether `Program.cs` already routes a return value before changing the signature.
- **Copy gate.** Both `UnboundCommand.cs:12` and `SyxxUnbound.swift:9` carry
  "the human-readable strings below are PROPOSED wording pending owner approval". New strings inherit
  that status and must be **appended** to both output catalogs (house rule 2).

**4. Honesty contract.** Contract 2, at the surface:

> "readyDirect … Still encrypted, but the resolver sees the client IP — a materially weaker state
> that must be surfaced, never dressed up as anonymized." (DNSCryptProxyConfig.swift:31-33)

Wrong output that looks like success: **printing only `CONCLUSION: PASS` for a `ReadyDirect` run.**
`report.Ok` is true for `ReadyDirect` (`IsReady` covers both ready states,
DNSCryptProxyConfig.swift:47-49), so a straight `if (Ok) print PASS` is a *correct* pass that omits
the anonymization downgrade. macOS prints the warning **before** the conclusion, unconditionally, at
SyxxUnbound.swift:57-65. Dropping it means a user who chose DNSCrypt *for* anonymity is told PASS
while the resolver sees their IP. Same for `Unreachable`: the "Not falling back to plain recursion"
line (`:62`) is the sentence that tells the user their DNS is unchanged rather than silently
degraded — contract 4 at the surface.

**5. Failure-case test (A13-T7).** A CLI-surface test that renders a `DNSCryptChainReport` with
`Proxy = ReadyDirect` and asserts the emitted text contains the "WITHOUT a relay" downgrade sentence
**and** that it appears before the conclusion line; and a second with `Proxy = Unreachable` asserting
the text contains "Not falling back to plain recursion" and that the exit code is 3, not 0.
This requires `RunVerifyDNSCrypt` to be factored so the rendering is testable without `Console` —
follow whatever pattern the existing CLI tests use, or return the composed string.

**6. Dependency order / effort.** After A5. Terminal for the DNSCrypt group. **S.**

**7. Verification.**
```bash
# V-A6-1 — the verb exists, is discoverable, and its exit codes discriminate
./syxx unbound --help 2>&1 | head -5
./syxx unbound bogus-verb 2>&1 | head -3
./syxx unbound verify-dnscrypt; echo "exit=$?"
```
- **PASS** — the usage line names `verify-dnscrypt`; the unknown-verb hint names it; the run prints
  either a PASS with a named relay, or a FAIL/INCONCLUSIVE with a reason, and `exit` is 0 / 3 / 2
  respectively.
- **FAIL** — `exit=0` alongside text containing `FAIL`, or `exit=0` for a `ReadyDirect` run that did
  not print the downgrade sentence.
- **INCONCLUSIVE** — the run reports `NOT INSTALLED` (V-A2-1 was inconclusive) or `INCONCLUSIVE`
  with a query-log reason. Both are *correct behaviour* and neither closes the bar; re-run after
  installing `dnscrypt-proxy` and confirming V-A3-3.

---

### A7 — `LinuxSystemResolverSwitcher` (so `unbound enable`/`disable` can succeed at all)

**1. What.** New file `Windows/dotNET/Syxx.Kit/LinuxSystemResolverSwitcher.cs` implementing
`ISystemResolverSwitching` (declared `UnboundResolver.cs:57-64`), plus a one-line change to the
switcher selection in the `UnboundManager` constructor (`UnboundManager.cs:145-147`):

```csharp
_switcher = switcher ?? (OperatingSystem.IsWindows() ? new WindowsSystemResolverSwitcher()
          : OperatingSystem.IsLinux()   ? new LinuxSystemResolverSwitcher()
          : new NullResolverSwitcher());
```

**2. macOS reference.** The macOS twin is the `networksetup`-based switcher inside
`macOS/Sources/SyxxKit/UnboundManager.swift`; the *contract* it must satisfy is
`UnboundResolver.cs:57-64` and the D099 snapshot rules at `UnboundManager.cs:177-184` and
`:212-218`.

**3. Linux mechanism.**
- **`resolvectl dns <link> <servers...>`** to set, and `resolvectl revert <link>` to restore. The
  repo already uses `resolvectl` for reads (`NetworkKeyState.cs:216`, parser at `:224-303`) and for
  DoT configuration (`EncryptedDNSConfigurator.cs:105-111`) — **CONFIRMED-from-repo-code** that
  `resolvectl` is this project's chosen Linux resolver interface.
- **Snapshot** must return `null` when the read failed and `Array.Empty<string>()` only when the
  link genuinely has no servers — the D099 distinction, stated at `UnboundResolver.cs:59-61` and
  implemented for Windows at `WindowsSystemResolverSwitcher.Snapshot` (`:40-49`).
  `NetworkKeyState.ParseResolvectlStatus` (`:224`) already produces per-link resolvers and is
  `internal`, so it is reachable from `Syxx.Kit`. **CONFIRMED-from-repo-code.**
- **Alternatives considered.** (a) Direct D-Bus to `org.freedesktop.resolve1` (`SetLinkDNS`,
  `RevertLink` on `/org/freedesktop/resolve1/link/_N`) — avoids a subprocess and gives typed errors,
  but adds a D-Bus client dependency to `Syxx.Kit` and a second code path to audit.
  **Recommend `resolvectl`** for consistency with the two existing call sites; note D-Bus as the
  upgrade if polkit interaction becomes necessary. (b) Rewriting `/etc/resolv.conf` — **rejected**:
  under systemd-resolved it is a symlink to a stub file that resolved rewrites, so the change is
  silently reverted and Syxx would report a switch that did not stick. That is a fabricated success
  and is exactly what `NullResolverSwitcher` was written to avoid (`UnboundManager.cs:363-366`, E082).
- **Which link?** `resolvectl dns "" <servers>` sets the *global* configuration
  (`EncryptedDNSConfigurator.cs:105` already does this). Per-link is more precise but needs the
  default-route interface. Recommend global for v1 to match the existing configurator, and note the
  divergence from `WindowsSystemResolverSwitcher`'s per-interface model.
  **INFERENCE-needs-real-Ubuntu-run** — whether `resolvectl dns ""` actually takes effect for the
  stub resolver at `127.0.0.53`, and whether `revert` restores DHCP-pushed servers. §7 V-A7-1.
- **The 127.0.0.53 problem.** With systemd-resolved active, `/etc/resolv.conf` points at the stub
  `127.0.0.53:53`, so pointing "system DNS" at `127.0.0.1` requires either stopping resolved
  (destructive, and not something a toggle should do silently) or setting resolved's *upstream* to
  `127.0.0.1` — which is a different topology from macOS/Windows: applications keep talking to the
  stub, and the stub talks to Unbound. Functionally equivalent, observably different (the flow
  observer will see `systemd-resolve → 127.0.0.1:53`, i.e. `CleartextLoopback`, which
  `DNSTransportState.cs:228` correctly treats as *undetermined*, not cleartext-off-host).
  **INFERENCE-needs-real-Ubuntu-run** — §7 V-A7-1. **This topology decision must be settled by a
  real run before A7 is written**; it is the difference between a working feature and a broken one.
- **Elevation.** `IsElevationError` at `EncryptedDNSConfigurator.cs:118-122` already covers
  "must be root" / "Interactive authentication required" / "Operation not permitted"
  (CONFIRMED-from-repo-code). Reuse that logic; do not write a second detector.
- **PATH hardening.** `resolvectl` must be resolved to an absolute path — see A8.

**4. Honesty contract.** Not one of the ten transport contracts, but the same spine, and the
existing code states it:

> "E082: Restore must return failure, not fake success. The old `(true, false, null)` caused the CLI
> to print 'DISABLED — restored: (DHCP-provided)' when nothing was ever touched."
> (`UnboundManager.cs:363-365`)
> "D099: a null snapshot means the original DNS servers were never readable — restoring them would
> silently reset static DNS to DHCP." (`:212-213`)

Wrong output that looks like success: **`Snapshot()` returning `Array.Empty<string>()` when
`resolvectl status` could not be read.** That is precisely today's `NullResolverSwitcher.Snapshot`
(`UnboundManager.cs:360`) — it returns empty, not null, so the D099 guard at `:180-184` does **not**
trip, and only the later `SetServers` failure stops the flow. If A7's `Snapshot` copies that shape,
`Disable()` would print `DISABLED — restored: (DHCP-provided)` (`UnboundCommand.cs:83`) after
resetting a user's static DNS to DHCP. Losing a user's DNS configuration while telling them it was
restored is a data-loss bug wearing a success message.

**5. Failure-case test (A13-T8).** Inject a `resolvectl` runner stub that fails (non-zero exit /
empty output) and assert `Snapshot() is null` — **explicitly** `Assert.Null`, not
`Assert.Empty`. Second: with a null snapshot, `UnboundManager.Disable()` returns
`UnboundResultKind.Failed` with the "no safe restore point" message (`:216-217`), never `Disabled`.
Third: a stub whose status output contains a link with genuinely zero `DNS Servers` returns
`Array.Empty<string>()`, not null — the two "nothing" cases must stay distinguishable.

**6. Dependency order / effort.** Independent of A1–A6; parallelisable. **L** — the topology
question (127.0.0.53) is genuinely open and may change the design after V-A7-1.

**7. Verification.**
```bash
# V-A7-1 — resolvectl set/revert semantics and the stub topology. RUN ON A BOX YOU CAN REBOOT.
resolvectl status | head -30
echo "--- resolv.conf ---"; ls -l /etc/resolv.conf; head -5 /etc/resolv.conf
echo "--- set global to 127.0.0.1 ---"
sudo resolvectl dns "" 127.0.0.1; echo "set-exit=$?"
resolvectl status | grep -A3 -i "global" | head -10
dig +time=3 +tries=1 example.com A +short; echo "dig-after-set-exit=$?"
echo "--- revert ---"
sudo resolvectl revert ""; echo "revert-exit=$?"; resolvectl status | grep -A3 -i "global" | head -10
dig +time=3 +tries=1 example.com A +short; echo "dig-after-revert-exit=$?"
```
- **PASS** — `set-exit=0`, the status block shows `127.0.0.1` as the global DNS server,
  `revert-exit=0`, and the status returns to its original servers with `dig-after-revert-exit=0`.
  Record the *exact* `resolvectl status` global block, both before and after: `A7.Snapshot` parses it.
- **FAIL** — `revert` does not restore the original servers (the restore path has no working
  mechanism, and `Disable()` cannot be honest), **or** `/etc/resolv.conf` does not point at
  `127.0.0.53` while `resolvectl status` reports links (an unexpected topology; re-plan).
- **INCONCLUSIVE** — `resolvectl` is absent or systemd-resolved is inactive. That is a *supported
  configuration*, not a broken box: A7 must then detect it and return the honest
  "not implemented for this resolver stack" failure rather than guessing at
  `/etc/resolv.conf`. The bar for the resolved path stays open.

---

### A8 — `EncryptedDNSConfigurator` honesty repairs

**1. What.** `Windows/dotNET/Syxx.Kit/EncryptedDNSConfigurator.cs` (owned). Three changes:
- **(a)** The Linux transport gate at `:43-47` currently rejects everything except `Dot`. Once A1
  adds `Dnscrypt`, that rejection message becomes factually wrong for `Dnscrypt`. Change the gate to
  accept `Dot` (systemd-resolved) and `Dnscrypt` (routed to the local dnscrypt-proxy + Unbound path),
  and keep rejecting `Doh` with the existing, *accurate* reason.
- **(b)** `SystemToolPath` (`:126-131`) returns the bare string `"resolvectl"` for every non-Windows
  tool. Resolve it to an absolute path from a fixed candidate list (`/usr/bin/resolvectl`,
  `/bin/resolvectl`), and refuse to run if none exists.
- **(c)** The `Dnscrypt` branch must route to `DNSCryptProxyManager` + `UnboundManager` rather than
  to `resolvectl`, and its confirmation must be `DNSCryptResult.IsReady`, not
  `NetworkKeyState.IsDNSEncrypted()` — the DNSCrypt path does not change what `resolvectl` reports.

**2. macOS reference.** `EncryptedDNSConfig.swift:64-73` (three transports, with `:69-71` stating
explicitly that DNSCrypt "is NOT an `NEDNSSettingsManager` transport … it is realised by the
local-resolver path — `dnscrypt-proxy` fronted by Unbound"). That comment is the specification for
change (c).

**3. Linux mechanism.**
- **(a)** `resolvectl dnsovertls "" yes` is the DoT mechanism and has no DoH verb —
  **CONFIRMED-from-repo-code** at `EncryptedDNSConfigurator.cs:43-47` and `:109`. The existing
  rejection of DoH on Linux is *honest and correct*; keep it and keep its wording.
- **(b)** `resolvectl` lives in `/usr/bin/resolvectl` on Debian/Ubuntu.
  **INFERENCE-needs-real-Ubuntu-run** — §7 V-A8-1.
- **(c)** No new OS mechanism; composition of A3 + A5.

**4. Honesty contract.** Contract 4, plus the file's own spine at `:21-23`:

> "every path routes through `EncryptedDNSConfig.ClassifyApply`. Applying a setting is never reported
> as 'encrypted' without the system's active flag AND a real encrypted query completing."

Wrong output that looks like success for **(a)**: leaving the gate as-is after `Dnscrypt` exists
means `syxx` tells a Linux user *"Linux only supports DoT (DNS-over-TLS) via systemd-resolved. DoH
configuration is not implemented on this platform"* when they asked for **DNSCrypt** — a message that
names the wrong transport and the wrong reason. It is a `Failed`, so it is not a false *clean*, but
it is a fabricated *explanation*, and D072's whole point (`:34-37`) was that silently mishandling a
transport pair is the defect.
Wrong output for **(c)**: reusing `NetworkKeyState.IsDNSEncrypted()` (`:74`) as the active-check for
the DNSCrypt path. `IsDNSEncrypted` reads `resolvectl`/registry state (`NetworkKeyState.cs:699`,
`:567`), which the DNSCrypt path never touches — so it would return `No`/`Unknown` for a *working*
DNSCrypt chain, and `ClassifyApply` (`EncryptedDNSConfig.cs:67-69`) would return `Inconclusive`
forever. That is a false *negative* rather than a false clean — still wrong, and the kind of wrong
that gets "fixed" later by someone hardcoding `active = true`.
Wrong output for **(b)**: a PATH-hijacked `resolvectl` executed under the user's `sudo`. `netsh` and
`powershell` got this hardening (D102, `EncryptedDNSConfigurator.cs:124-131`,
`UnboundManager.cs:89-102`); `resolvectl` did not, and it is the one invoked under elevation on
Linux.

**5. Failure-case test (A13-T9).**
- `EnableAsync("quad9", EncryptedDNSTransport.Dnscrypt)` on Linux must **not** return a `Failed`
  whose reason mentions DoH. Assert on the absence of the substring `"DoH configuration is not
  implemented"`.
- `EnableAsync(..., Doh)` on Linux must still return `Failed` with the existing reason — the
  relaxation must not become a blanket accept.
- `SystemToolPath("resolvectl")` returns a rooted path (`Path.IsPathRooted(...) == true`) on Linux.
  A unit test that asserts `!= "resolvectl"` trips on the current code today.

**6. Dependency order / effort.** (b) is independent and can land immediately. (a) needs A1.
(c) needs A3 and A5. **M** overall; (b) alone is **S**.

**7. Verification.**
```bash
# V-A8-1 — absolute path of resolvectl, and whether DoH has any resolvectl verb at all
command -v resolvectl; readlink -f "$(command -v resolvectl)"
resolvectl --help 2>&1 | grep -iE 'dnsovert|dnsoverh|doh' || echo "NO-DOH-VERB"
resolvectl --version 2>&1 | head -2
```
- **PASS** — an absolute path under `/usr/bin` (record it verbatim for the candidate list) **and**
  `NO-DOH-VERB` / only a `dnsovertls` line. `NO-DOH-VERB` confirms that the D072 Linux DoH rejection
  at `:43-47` is honest and must stay.
- **FAIL** — a DoH verb **is** listed. Then `:43-47`'s stated reason is false on this release, the
  comment is wrong, and DoH-on-Linux becomes a new gap to file rather than a closed one.
- **INCONCLUSIVE** — `resolvectl` absent. Nothing is settled; see V-A7-1's inconclusive branch.

---

### A9 — `FlowTransportObserver.cs` (new): the pure classifier

**1. What.** New file `Windows/dotNET/Syxx.Kit/FlowTransportObserver.cs`.

```csharp
public sealed record ObservedFlow(
    string ProcessName, int Pid, string NetworkProtocol,
    ushort? LocalPort, string? RemoteAddress, ushort? RemotePort,
    string? InterfaceName, string? State, ulong? BytesIn, ulong? BytesOut,
    ulong Inode = 0, uint Uid = 0)          // Linux-only identity fields
{
    public string Key => $"{Pid}|{NetworkProtocol}|{LocalPort?.ToString() ?? "*"}|{RemoteAddress ?? "*"}|{RemotePort?.ToString() ?? "*"}";
    public bool IsUdp => NetworkProtocol.StartsWith("udp", StringComparison.Ordinal);
}

public enum FlowActivity { Grew, ProvenIdle, Unknown }          // see Decision D-A1

public sealed record FlowSample(
    IReadOnlyList<ObservedFlow>? Flows, string? FailureDetail,
    bool AttributionComplete = true, ulong GlobalUdpOut = 0, ulong GlobalUdpIn = 0)
{ public bool Available => Flows is not null; }

public interface IFlowSampler { FlowSample Sample(); }

public static class FlowTransportObserver
{
    public static readonly IReadOnlyList<string> ResolverProcessPrefixes;   // + "systemd-resolved"
    public static readonly IReadOnlyList<string> SelfProcessPrefixes;       // secondary net only
    public const ushort DotPort = 853, DoqPort = 8853, DohPort = 443, Do53Port = 53;
    public static readonly IReadOnlySet<ushort> NonUnicastResolverPorts;    // 5353, 5350, 5351

    public static IReadOnlyList<(ObservedFlow Flow, FlowActivity Activity)> ActiveFlows(
        FlowSample previous, FlowSample current);
    public static TransportEvidence Classify(
        IReadOnlyList<(ObservedFlow, FlowActivity)> activeFlows,
        ISet<string> encryptedResolverAddresses);
}
```

Note `ActiveFlows` returns pairs, not flows — the activity tri-state must survive to the classifier
or the one-sided rule cannot be applied.

**2. macOS reference.** `FlowTransportObserver.swift` in full: `:39-76` (`ObservedFlow`, `key`,
`isUDP`), `:82-91` (resolver prefixes — note `"systemd-resolve"` is already listed at `:90` as
"Linux twin"), `:96` (self prefixes), `:99-105` (ports), `:195-204` (`activeFlows`),
`:214-271` (`classify`, including the port switch at `:228-248` and the unconnected-UDP branch at
`:252-259`), `:277-281` (`SampleResult`), `:304-312` (fail-closed sampling).

The file-header commentary at `:3-36` is the *specification* and must be carried across in full,
adjusted only where the Linux mechanism differs — in particular `:14-19` (why unconnected UDP is
unobservable at socket level) and `:21-30` (scope and retention: transport metadata only, never
payload, nothing written to disk).

**3. Linux mechanism.** Pure classification — **no OS calls in this file at all**; the sampler is
A10. That separation is the C# twin of macOS's split between `classify` (`:214`) and `sample`
(`:299`) and is what makes the honesty tests runnable from fixtures.

The one substantive translation is the activity rule from Decision D-A1, applied inside `Classify`:

```
Grew        -> classify exactly as macOS FlowTransportObserver.swift:227-259
ProvenIdle  -> drop (contract 7)
Unknown     -> may contribute ONLY to TransportUnobservable, and only when the flow is
               DNS-relevant (resolver-attributed, or a DNS port); never to CleartextOffHost,
               never to EncryptedDns
```

**4. Honesty contracts.** This file carries five of the ten. Quoted, each with its false-clean:

- **Contract 6 (self-exclusion).**
  > "Syxx sends cleartext Do53 on purpose during every audit … Counting either would let Syxx confirm
  > a state out of its own traffic — a fabricated measurement." (`:32-36`, enforced at `:224`)

  False output: `ConfirmedFalse`/`CleartextQueryObserved` produced entirely by Syxx's own audit
  query. See §2 for why this fails silently on Linux and for the inode-based fix.
- **Contract 7 (unchanged flow ≠ activity).**
  > "A flow that merely persists unchanged is NOT activity — counting the historical rows `nettop`
  > keeps for closed sockets would turn old traffic into a current claim." (`:191-194`)

  False output: `ConfirmedFalse` from a `:53` socket that a browser opened an hour ago.
- **Contract 8 (port 443 is not automatically DNS).**
  > "A port-443 flow counts as encrypted DNS ONLY to one of these; counting any HTTPS flow would make
  > every web request look like encrypted DNS." (`:210-213`, enforced by the `where` clause at `:242`)

  False output: `ConfirmedTrue`/`EncryptedTransportObserved` on a machine doing plain UDP/53 DNS and
  ordinary web browsing. The most seductive bug in the file, because it makes the product look like
  it works.
- **Contract 9 (unconnected UDP ⇒ undetermined).**
  > "An unconnected UDP socket has no peer … so NO socket-level tool on any OS can report it … That
  > case resolves to `transportUnobservable` ⇒ UNDETERMINED." (`:14-19`, enforced at `:252-259`)

  False output: dropping such a socket entirely, producing zero evidence, which
  `DNSTransportState.cs:231` renders as `NoDnsActivityObserved` — "no DNS happened" instead of "DNS
  happened and we could not see how". Combined with one DoT flow that is `ConfirmedTrue`.
- **Contract 10 (observer absent ⇒ observerUnavailable).**
  > "`flows == nil` means the sampler could not run at all — which is `observerUnavailable`, never an
  > answer about encryption." (`:275-276`, enforced at `:305-310`)

  False output: returning an **empty list** instead of null. `DNSTransportState.cs:210-215` treats a
  non-null empty evidence as a successful observation of nothing, so "we could not look" becomes
  "we looked and saw no DNS". On Linux this is a live risk because `/proc/net/udp` on a container
  with a private netns legitimately returns a header row and nothing else.

**5. Failure-case tests (A13-T1, A13-T10..T14).** All from fixtures; none touch the network.
- **A13-T1 (the §2 regression, highest value).** Fixture flow list containing a Do53 flow whose
  `Inode` is in the sampler's self-inode set and whose `ProcessName` is `"dotnet"`. Assert
  `Classify(...).CleartextOffHost == 0`. A second fixture with the same flow and a **different**
  inode asserts `CleartextOffHost == 1`, so the test proves the exclusion is doing work rather than
  the classifier being inert. If self-exclusion is implemented by name, arm one fails.
- **A13-T10.** A `:443` flow to an address **not** in `encryptedResolverAddresses` ⇒
  `EncryptedDns == 0` (contract 8).
- **A13-T11.** Identical flow present in both samples with identical counters and
  `Activity = ProvenIdle` ⇒ evidence is empty (contract 7). Same flow with `Activity = Unknown` ⇒
  `CleartextOffHost == 0` **and** `TransportUnobservable == 1` — this is the one-sided rule, and it
  is the test that trips if a later change lets `Unknown` reach a definite.
- **A13-T12.** Peerless UDP socket, resolver-attributed, local port 5353 ⇒ not counted
  (`:255`, mDNS is not unicast DNS); local port 34567 with `Unknown` activity ⇒
  `TransportUnobservable == 1` (contract 9).
- **A13-T13.** `FlowSample(Flows: null)` through `DNSTransportDetermination.ClassifyDns(evidence:
  null, observerAvailable: false, observationSucceeded: false)` ⇒
  `(Undetermined, ObserverUnavailable)`; and `FlowSample(Flows: [])` must **not** be reachable as a
  success — assert the monitor path treats an empty-with-`AttributionComplete: false` sample as
  `ObservationFailed` (contract 10).
- **A13-T14.** A fixture with one DoT `:853` flow (`Grew`) **and** one peerless resolver UDP socket
  (`Unknown`) ⇒ `EncryptedDns == 1` **and** `TransportUnobservable == 1`, and
  `ClassifyDns` returns `ConfirmedTrue` — because `DNSTransportState.cs:226` checks `EncryptedDns`
  before `TransportUnobservable`. **This is a finding, not a test of correct behaviour:** the
  existing consumer prefers affirmative encryption over unobservable activity, which on Linux (where
  `Unknown` will be the common case, per D-A1) means one DoT flow can outvote a resolver socket that
  may have carried cleartext all interval. The test pins the current behaviour so the divergence is
  visible; **escalate to L and to the parity auditor** — changing it would change macOS too, and that
  is not E1's call.

**6. Dependency order / effort.** Needs nothing from A1–A8; parallelisable with the DNSCrypt group.
Blocks A10 and A11. **L.**

**7. Verification.** Fixture-driven; no real run. The real-run bar is A14 (§6).

---

### A10 — `LinuxFlowSampler.cs` (new): the `/proc` sampler

> **Ownership note:** this file must live at `Windows/dotNET/Syxx.Kit/LinuxFlowSampler.cs`, **not**
> under `Windows/dotNET/Syxx.Kit/Platform/`, because `Platform/**` is E2-owned (parent plan §3).

**1. What.** New file implementing `IFlowSampler` for Linux, plus a `NullFlowSampler` that returns
`FlowSample(Flows: null, FailureDetail: "no flow observer is implemented for this OS")` on every
other platform.

**2. macOS reference.** `FlowTransportObserver.swift:285-287` (the constant, unsteerable command
line), `:289-302` (`sample`, and the note at `:294-298` that the runner is injected for tests and
that the subprocess path is deliberately not public), `:304-312` (fail-closed: not-executable ⇒
`flows: nil`, no output ⇒ `flows: nil`).

**3. Linux mechanism.** All **INFERENCE-needs-real-Ubuntu-run** unless marked otherwise; §7 V-A9-1
through V-A9-4 settle them.

- **Flow table** — read `/proc/net/tcp`, `/proc/net/tcp6`, `/proc/net/udp`, `/proc/net/udp6`.
  Columns: `sl local_address:port rem_address:port st tx_queue:rx_queue tr:tm->when retrnsmt uid
  timeout inode …`. Addresses are hex; IPv4 is a little-endian 32-bit word, IPv6 is four
  little-endian words; ports are plain hex. `rem_address` of `00000000:0000` means no peer
  (the contract-9 case). No cumulative byte counters exist — that is Decision D-A1's premise and
  V-A9-1 is the command that proves or disproves it on the target release.
- **Process attribution** — build `inode → pid` by scanning `/proc/[0-9]+/fd/*` for symlinks whose
  target matches `socket:[N]`. Readable for the caller's own processes always; for other users'
  processes only as root. Name from `/proc/<pid>/cmdline` and `/proc/<pid>/exe`, **never** `comm`
  (§2, layer 3).
- **Self-exclusion** — `/proc/self/fd`, every sample (§2, layer 1). If it cannot be enumerated,
  return `Flows: null` (§2, layer 4).
- **Secondary attribution by uid** — `/proc/net/*` carries the socket owner's uid unconditionally.
  Map it via `/etc/passwd` (or `getpwuid`) and treat `systemd-resolve`, `unbound`, `dnscrypt-proxy`,
  `stubby`, `bind`, `dnsmasq` as resolver processes. This recovers the contract-9 branch for an
  unprivileged run that cannot read `/proc/<other-pid>/fd`.
- **`AttributionComplete`** — false when any DNS-relevant socket's inode could not be mapped to a pid
  **and** could not be attributed by uid. The monitor (A11) turns that into `ObservationFailed`
  rather than a classification (see A11's contract).
- **TCP counters** — `ss -tin` (no `-p`, so no privilege needed), parsing `bytes_sent` /
  `bytes_received` from `tcp_info`. Older iproute2 prints `bytes_acked` instead of `bytes_sent`;
  the parser must accept both and, if it finds neither, yield `FlowActivity.Unknown` rather than
  assuming idle. Like macOS's sampler command (`:286-287`), the argument vector is a **constant** —
  no part of it is client-supplied.
- **Global UDP counters** — `Udp: OutDatagrams` / `InDatagrams` from `/proc/net/snmp`, and
  `Udp6OutDatagrams` / `Udp6InDatagrams` from `/proc/net/snmp6`, captured into `FlowSample` at each
  sample so `ActiveFlows` can compute the one-sided idleness proof.
- **Interface name** — `/proc/net/*` does **not** carry it (macOS gets it from `nettop -J
  interface`). It must be derived from the local address by matching against
  `NetworkKeyState.GetActiveInterfaces()` / `NetworkInterface.GetAllNetworkInterfaces()`, or left
  `null`. **`null` is acceptable**: `TransportEvidence.Interfaces` is presentational
  (`DNSTransportState.cs:76`) and nothing in `DNSTransportDetermination` reads it. Do not invent one.
- **Cost** — macOS measured its sampler at 0.04–0.18 s wall per sample (`:289-292`). Four `/proc`
  reads plus a `/proc/*/fd` walk plus one `ss` exec should be comparable or cheaper, but it is a
  claim, not a measurement: V-A9-4 measures it. If the `/proc/*/fd` walk dominates on a box with
  thousands of processes, restrict the walk to pids whose `/proc/<pid>/comm` is in the resolver set
  plus `/proc/self` — but only after measuring, and note that the restriction weakens uid-based
  attribution.
- **Retention** — carry `FlowTransportObserver.swift:21-30` across verbatim: transport metadata only,
  never payload, nothing written to disk. This type performs **no** file writes.

**4. Honesty contract.** Contract 10 first:

> "`flows == nil` means the sampler could not run at all — which is `observerUnavailable`, never an
> answer about encryption." (`:275-276`)

Wrong output that looks like success: **returning an empty flow list when `/proc/net/udp` was
unreadable, or when the process runs in a netns whose tables are genuinely empty, or when
`/proc/self/fd` could not be walked.** Every one of those is "we could not look" and every one of
them, rendered as `Flows: []`, becomes "we looked and DNS is quiet" — and a quiet interval alongside
one DoT flow is `ConfirmedTrue`. Rule: **any** read failure ⇒ `Flows: null` + a `FailureDetail`
naming the file that failed.
Also contract 6: a sampler that returns flows without a complete self-inode set is forbidden
(§2 layer 4).

**5. Failure-case test (A13-T15).** Point the sampler at a fixture directory (the file paths must be
injectable — a `procRoot` constructor parameter, the C# twin of macOS's injected `runner` at
`:299-301`) containing: (a) a `/proc/net/udp` fixture with a real Ubuntu header and rows, asserting
the hex address/port decode against known values including an IPv6 row; (b) a directory with
`net/udp` **absent**, asserting `Flows is null` and `FailureDetail` names the path; (c) a fixture
where `self/fd` is unreadable, asserting `Flows is null`. Arm (b) is the one that trips on an
"empty on error" regression.

**6. Dependency order / effort.** After A9 (needs the record shapes). Blocks A11. **L.**

**7. Verification.**
```bash
# V-A9-1 — the flow table's real shape, and the absence of byte counters (D-A1's premise)
echo "--- /proc/net/udp (head) ---"; head -3 /proc/net/udp
echo "--- /proc/net/udp6 (head) ---"; head -2 /proc/net/udp6
echo "--- /proc/net/tcp (head) ---"; head -3 /proc/net/tcp
echo "--- who owns the resolver socket ---"
ss -lunp 2>/dev/null | grep -E '127.0.0.5[03]|:53\b' | head -5
echo "--- global udp counters ---"; grep -A1 '^Udp:' /proc/net/snmp
```
- **PASS** — the header lines are printed and contain `tx_queue rx_queue` and `inode`, and **no**
  cumulative byte column. That confirms D-A1's premise and the exact column order the parser needs.
  Paste the header lines verbatim into the ledger; they are the parser's specification.
- **FAIL** — a cumulative per-socket byte column exists that this plan did not anticipate. Then
  D-A1's UDP branch should be **replaced** by a real growth test, and the plan is better than
  expected — re-plan A9/A10 accordingly.
- **INCONCLUSIVE** — `/proc/net/udp` is empty apart from the header (possible inside a container or
  a private netns). Nothing about the format is settled; re-run on the desktop host, not in a
  container.

```bash
# V-A9-2 — do TCP byte counters exist and grow, unprivileged?
(ss -tin 'dport = :443' | head -20) ; echo "--- generate traffic ---"
curl -s -o /dev/null https://example.com
ss -tin | grep -oE 'bytes_(sent|acked|received):[0-9]+' | head -6 || echo "NO-BYTE-FIELDS"
```
- **PASS** — `bytes_sent`/`bytes_acked` and `bytes_received` appear with non-zero values.
  Record which spelling this iproute2 uses.
- **FAIL** — `NO-BYTE-FIELDS`. Then TCP has no growth test either, every TCP flow becomes `Unknown`,
  and the observer can essentially never reach `ConfirmedTrue` on Linux. That is a large honest
  degradation and must be ledgered, not worked around.
- **INCONCLUSIVE** — `ss` not installed (`iproute2` absent). Install and re-run.

```bash
# V-A9-3 — self-exclusion by inode, and attribution reach at the caller's privilege (§2)
ls -l /proc/self/fd | head -10
echo "--- can we read another user's fd table unprivileged? ---"
RPID=$(pgrep -x systemd-resolved | head -1); echo "resolved-pid=${RPID:-NONE}"
ls -l /proc/${RPID:-1}/fd 2>&1 | head -3
echo "--- as root ---"; sudo ls -l /proc/${RPID:-1}/fd 2>&1 | head -3
echo "--- what comm does a dotnet-hosted syxx report? ---"
cat /proc/self/comm
```
- **PASS** — `/proc/self/fd` lists `socket:[N]` entries (self-exclusion is implementable at any
  privilege), and the unprivileged read of resolved's fd table is **denied** while the `sudo` read
  succeeds. That denial is the expected result and is what makes `AttributionComplete: false` a real
  state that must be handled.
- **FAIL** — `/proc/self/fd` is not readable (self-exclusion has no mechanism; the whole observer
  must return `ObserverUnavailable` on this platform and the lane re-plans).
- **INCONCLUSIVE** — `resolved-pid=NONE` (systemd-resolved not running). Attribution reach is
  untested; re-run on a box with the target resolver stack.

```bash
# V-A9-4 — sampler cost, against the macOS 0.04-0.18 s baseline
time ( cat /proc/net/tcp /proc/net/tcp6 /proc/net/udp /proc/net/udp6 > /dev/null; \
       ls -l /proc/[0-9]*/fd > /dev/null 2>&1; ss -tin > /dev/null )
```
- **PASS** — real time under ~0.5 s. At the 5 s cadence (`DNSTransportState.cs:318`) that is an
  acceptable duty cycle.
- **FAIL** — over 2 s. The `/proc/*/fd` walk must be narrowed (see A10 §3) before the monitor ships.
- **INCONCLUSIVE** — wildly variable across three runs. Run it five times and record all five;
  a single sample is not a measurement.

---

### A11 — `DNSTransportMonitor.cs` (new): the producer that ends the orphan

**1. What.** New file `Windows/dotNET/Syxx.Kit/DNSTransportMonitor.cs`.

```csharp
public enum TransportObserverHealth { Healthy, Unavailable, Failed }

public sealed class DNSTransportMonitor
{
    public DNSTransportStateStore Store { get; }
    public DNSTransportMonitor(DNSTransportStateStore? store = null, IFlowSampler? sampler = null);

    public static string NetworkFingerprint(IReadOnlyList<string> interfaces, IReadOnlyList<string> resolvers);
    public static string CurrentNetworkFingerprint();
    public static ISet<string> EncryptedResolverAddresses(IReadOnlyList<ScopedResolver> scoped);

    public DNSTransportSample Tick(
        Func<FlowSample>? sampler = null, Func<VPNStatus>? vpn = null,
        Func<IReadOnlyList<ScopedResolver>>? scopedResolvers = null,
        Func<string>? fingerprint = null, Func<DateTime>? now = null,
        Func<ulong>? monotonicNow = null);
}
```

**Why this task matters most for the orphan.** `DNSTransportState.cs` (419 lines) is a complete,
faithful twin of the determination core, and **nothing in the tree constructs a `DNSTransportSample`
or calls `DNSTransportStateStore.Record`** — verified by grep across all `.cs`: every hit is inside
`DNSTransportState.cs` itself (`:107`, `:250`, `:315-418`). A11 is the producer that makes those 419
lines reachable. Note the same is true on macOS at Kit level: `DNSTransportMonitor` is constructed
only in `macOS/Tests/SyxxKitTests/DNSTransportDeterminationTests.swift:335,360` and
`DNSTransportRealRunTests.swift:114`. **So parity does not require a CLI or GUI surface** — adding
one would put Linux *ahead* of macOS, which is out of scope for this team. The parity deliverable is
the producer plus its test twins (A13, A14).

**2. macOS reference.** `DNSTransportMonitor.swift` in full: `:27-34` (`TransportObserverHealth`),
`:38-48` (the class and its store), `:53-61` (fingerprint), `:66-72` (`encryptedResolverAddresses`),
`:74-132` (`tick`, with the baseline rationale at `:74-80`, the sampler-unavailable branch at
`:96-104`, the baseline branch at `:113-121`, and the real branch at `:123-131`),
`:134-148` (`publish`). The observation-order commentary at `:3-23` (Phase A then Phase B, and why
Phase A alone cannot confirm encryption) must be carried across.

**3. Linux mechanism.** No direct OS calls; composition of A10 plus four existing E2-owned members
(§3 table) — all **CONFIRMED-from-repo-code**. Two Linux-specific additions:
- The sampler is selected by OS: `LinuxFlowSampler` on Linux, `NullFlowSampler` elsewhere. On Windows
  this is **not a regression** — Windows has no flow observer today and `NullFlowSampler` returns
  `Flows: null` ⇒ `ObserverUnavailable`, which is the honest answer. It is specifically *not* an
  empty list (see A12).
- `AttributionComplete == false` from A10 ⇒ call `ClassifyDns(evidence: null, observerAvailable:
  true, observationSucceeded: false)` ⇒ `(Undetermined, ObservationFailed)`. This is a Linux-only
  branch with no macOS twin, because macOS's `nettop` always attributes. It must be labelled as a
  deliberate addition for the parity auditor, with this justification: without it, an unprivileged
  Linux run would classify only the *fraction* of flows it could attribute, and a partial view
  rendered as a whole view is a fabricated measurement.

**4. Honesty contract.** Contract 5:

> "The FIRST tick establishes the diff baseline and therefore cannot report activity: with no
> previous sample, every flow in the table looks 'new', including sockets that have been idle for
> hours. Reporting from that first sample would turn history into a current claim, so the baseline
> tick is `observationFailed`/undetermined by construction." (DNSTransportMonitor.swift:74-80)

Wrong output that looks like success: **a first tick that classifies.** On Linux this is worse than
on macOS, because `ActiveFlows` treats an unseen key as `Grew` (D-A1, matching
FlowTransportObserver.swift:199) — so on the baseline tick *every* socket in `/proc/net/*` is "new",
including a `:853` connection a mail client opened at boot. The first thing the user would ever see
is `ConfirmedTrue`, derived entirely from history. The `hadBaseline` guard
(DNSTransportMonitor.swift:113-121) is the only thing standing between the product and that output,
and it is one `if` that a C# translation can easily drop because it looks redundant.
Also contract 10, again: the sampler-unavailable branch (`:96-104`) must publish a sample with
`TransportObservationMethod.None` and the sampler's own `FailureDetail`, not silently skip the tick —
a skipped tick becomes a gap, and `DNSTransportStateStore.Timeline` (`:361-399`) renders gaps as
`NotYetObserved`, which loses the reason.

**5. Failure-case test (A13-T16).** Drive `Tick` with an injected sampler returning a fixture
containing one `:853` flow and one `:53` flow. Assert the **first** tick returns
`DnsEncryption == Undetermined` — assert `!= ConfirmedTrue` **and** `!= ConfirmedFalse`, both, so
neither direction can leak — and that `DnsEvidence.Method == FlowObservation` with a detail
mentioning the baseline. Assert the **second** tick (same fixture, unchanged counters, all
`ProvenIdle`) yields `Undetermined`/`NoDnsActivityObserved` — proving the baseline is a baseline and
not merely a one-tick delay. Third: an injected sampler returning `Flows: null` ⇒
`Undetermined`/`ObserverUnavailable` with the failure detail preserved in the evidence.

**6. Dependency order / effort.** After A9 and A10. Terminal for the observer group. **M.**

**7. Verification.** Covered by A14's real-run pack (§6). No separate command.

---

### A12 — Non-Linux sampler must be unavailable, not silent

**1. What.** `NullFlowSampler` in the A10 file, plus the OS selection in A11's constructor. Broken
out as its own task because it is a distinct honesty risk with a distinct test.

**2. macOS reference.** `FlowTransportObserver.swift:305-307` — the "sampler not present" branch
returns `flows: nil` with a detail naming the missing path, not an empty list.

**3. Linux mechanism.** N/A. **CONFIRMED-from-repo-code** that this matters today: Windows has no
flow observer of any kind in `Syxx.Kit` (grep for `TransportEvidence` finds only
`DNSTransportState.cs`), so whatever A12 does becomes Windows's behaviour by default.

**4. Honesty contract.** Contract 10. Wrong output that looks like success: a `LinuxFlowSampler`
whose `/proc` reads all fail on Windows and which returns `Flows: []` with no failure detail. Every
Windows tick then reports "no DNS activity observed" — a measurement of a machine that was never
observed, published into a store whose readings feed a glyph
(`DNSTransportState.cs:159-160`). Silence manufactured on a platform with no observer is the purest
form of the defect this product exists to refuse.

**5. Failure-case test (A13-T17).** `new DNSTransportMonitor().Tick()` on a non-Linux host returns
`Undetermined`/`ObserverUnavailable` with a non-empty failure detail. Assert
`DnsEvidence.Method == TransportObservationMethod.None`. Gate on `OperatingSystem.IsLinux()` so the
assertion inverts rather than skips when the suite runs on Linux — a skipped test is not a passed one.

**6. Dependency order / effort.** With A10/A11. **S.**

**7. Verification.** CI-enforced (the .NET suite runs on `ubuntu-latest` and could run on Windows);
no owner run needed.

---

### A13 — The failure-case test surface (ownership escalation required)

**1. What.** Seventeen tests, T1–T17, each specified inline above. Proposed file layout, mirroring
`macOS/Tests/SyxxKitTests/DNSTransportDeterminationTests.swift`:

```
Windows/dotNET/Syxx.Tests/DNSCryptProxyConfigTests.cs      T2, T3, T4, T5, T6, T7
Windows/dotNET/Syxx.Tests/LinuxResolverSwitchTests.cs      T8, T9
Windows/dotNET/Syxx.Tests/FlowTransportObserverTests.cs    T1, T10..T15
Windows/dotNET/Syxx.Tests/DNSTransportMonitorTests.cs      T16, T17
```

**2. macOS reference.** `macOS/Tests/SyxxKitTests/DNSTransportDeterminationTests.swift` — the
existing C# suite has **no** transport test file at all (`ls Windows/dotNET/Syxx.Tests/` shows none;
CONFIRMED-from-repo-code), which is itself a parity gap the auditor should record under
"a missing failure-case test where macOS has one" (parent plan §4.3).

**3. Linux mechanism.** N/A.

**4. Honesty contract.** House rule 5 — "Never edit/disable/soften/delete a test or golden vector to
go green; a failing honesty test is data." Note that **A13-T14 is expected to pin behaviour E1
considers questionable**, not to assert behaviour E1 endorses; it must be commented as such so a
future reader does not "fix" the code to match a test that was documenting a finding.

**5. Failure-case test.** Recursive; N/A.

**6. Dependency order / effort.** Each test lands with its task, not in a batch at the end. **M** in
aggregate.

**7. Verification.**
```bash
cd Windows/dotNET/Syxx.Tests && dotnet test -c Release --filter "FullyQualifiedName~Transport|FullyQualifiedName~DNSCrypt"; echo "exit=$?"
```
(House rule 4: run from the test project directory, not the repo root.)
- **PASS** — `exit=0` with a non-zero test count. **A zero-test run is not a pass** — record the count.
- **FAIL** — any assertion fails. Report the assertion text; do not adjust it.
- **INCONCLUSIVE** — the build fails for reasons unrelated to this lane (a pre-existing red from the
  Phase-0 baseline in parent plan §6). Compare against that baseline before attributing blame.

> **ESCALATION TO L (blocking A13).** Parent plan §3 assigns `Windows/dotNET/Syxx.Tests/**` to **E3**
> ("Linux-path tests only"). All seventeen tests above are E1's honesty contracts. Either (a) E1 is
> granted ownership of the four new files listed above, or (b) E1 hands each test as a written
> specification to E3 and E3 writes them. **(a) is strongly preferred** — a test written by someone
> other than the author of the guard tends to assert the implementation rather than the contract, and
> these seventeen exist specifically to trip on the implementation being wrong.

---

### A14 — Real-run bar for the observer

**1. What.** `Windows/dotNET/Syxx.Tests/DNSTransportRealRunTests.cs` — the C# twin of the macOS
real-run suite, gated on `SYXX_REAL_RUN=1` so it never runs unattended in CI.

**2. macOS reference.** `macOS/Tests/SyxxKitTests/DNSTransportRealRunTests.swift` in full. Its four
arms map one-to-one:
- `:52-66` cleartext arm — `dig +tcp @1.1.1.1` ⇒ `ConfirmedFalse`/`CleartextQueryObserved`;
- `:74-95` encrypted arm — a real DoT exchange on TCP/853 ⇒ `ConfirmedTrue`/`EncryptedTransportObserved`;
- `:99-108` quiet arm — no DNS at all ⇒ **never** `ConfirmedTrue`;
- `:112-152` live-monitor arm — baseline tick is `Undetermined` (`:120-121`), `ConfirmedTrue`
  requires `window.encryptedDNS > 0` (`:138-142`), `ConfirmedFalse` requires
  `window.cleartextOffHost > 0` (`:143-146`), and a far-future clock expires the reading (`:147-151`).

**3. Linux mechanism.** `dig +tcp` for the cleartext arm (`dnsutils` package) — the `+tcp` matters:
UDP/53 is exactly the case Linux cannot observe (contract 9), so the cleartext arm must use TCP or it
will legitimately fail. `kdig +tls` (knot-dnsutils) is the cleanest DoT generator on Linux and
replaces the `SYXX_DOT_SCRIPT` indirection at `:76-79`; fall back to the same
env-var-supplied-script pattern if `kdig` is unavailable, and **skip rather than pass** when neither
is present (`:78`). **INFERENCE-needs-real-Ubuntu-run** — §7 V-A14-1.

**4. Honesty contract.** All ten, end to end, on real traffic. The quiet arm (`:99-108`) is the one
that matters most on Linux, because per D-A1 the expected Linux answer in a quiet interval is
`Undetermined` via `TransportUnobservable` rather than via `NoDnsActivityObserved` — a *different
reason* for the same signal. The assertion must therefore be `!= ConfirmedTrue` (as macOS writes it
at `:106-107`), **not** an equality on the reason, or the Linux arm will fail for the right behaviour.

**5. Failure-case test.** Recursive; the suite *is* the failure case.

**6. Dependency order / effort.** After A11. **M.**

**7. Verification.**
```bash
# V-A14-1 — the traffic generators exist and produce the traffic the arms need
command -v dig kdig; echo "tools-exit=$?"
dig +tcp +time=5 +tries=1 @1.1.1.1 example.com A +short; echo "do53-tcp-exit=$?"
kdig +tls @1.1.1.1 example.com A +short 2>&1 | tail -3; echo "dot-exit=$?"
```
- **PASS** — both tools present, `do53-tcp-exit=0` with an A record, `dot-exit=0` with an A record.
  Both arms of A14 are then runnable.
- **FAIL** — `kdig +tls` returns an answer but the observer's encrypted arm still sees
  `EncryptedDns == 0` when A14 runs. That is an observer bug (a `:853` TCP flow that the parser
  missed), not a tooling problem.
- **INCONCLUSIVE** — `kdig` absent (`knot-dnsutils` not installed). The DoT arm **skips**, and a
  skipped arm does **not** close the "can Linux confirm encrypted?" bar. Install and re-run.

```bash
# V-A14-2 — the actual real-run bar
cd Windows/dotNET/Syxx.Tests && SYXX_REAL_RUN=1 dotnet test -c Release \
  --filter "FullyQualifiedName~DNSTransportRealRun" --logger "console;verbosity=detailed"; echo "exit=$?"
```
- **PASS** — all four arms pass, **and** the printed `[REAL RUN · …]` evidence lines show non-zero
  counts for the arms that assert them. Paste the raw output into the ledger verbatim (parent plan
  §8.1 rule 3).
- **FAIL** — the quiet arm reaches `ConfirmedTrue`, or the cleartext arm reports `EncryptedDns > 0`,
  or the monitor arm's baseline tick is not `Undetermined`.
- **INCONCLUSIVE** — arms skip for missing tools, or the encrypted arm reports
  `TransportUnobservable > 0` alongside `EncryptedDns > 0` (the D-A1 expected-cost case, and the
  A13-T14 finding). **Record it as inconclusive, not as a pass** — and attach the A13-T14 escalation.

---

## 5. Ordered task list

Two chains that do not touch each other's files, plus one independent repair. Run the chains in
parallel if the seat is split; otherwise run in the order below.

| # | Task | Effort | Depends on | Files |
|---|---|---|---|---|
| 1 | **A1** DNSCrypt catalog + `Dnscrypt` transport | M | — | `EncryptedDNSConfig.cs` |
| 2 | **A4** Unbound forward-to-DNSCrypt seam | S | — | `UnboundResolver.cs` |
| 3 | **A8(b)** absolute-path `resolvectl` (D102) | S | — | `EncryptedDNSConfigurator.cs` |
| 4 | **A9** `FlowTransportObserver.cs` (pure) | L | — | new |
| 5 | **A2** `DNSCryptProxyConfig.cs` (pure) | M | A1 | new |
| 6 | **A10** `LinuxFlowSampler.cs` | L | A9 | new |
| 7 | **A12** non-Linux sampler unavailable | S | A10 | new (same file) |
| 8 | **A3** `DNSCryptProxyManager.cs` | L | A2 | new |
| 9 | **A11** `DNSTransportMonitor.cs` | M | A9, A10 | new |
| 10 | **A7** `LinuxSystemResolverSwitcher.cs` | L | V-A7-1 result | new + `UnboundManager.cs:145-147` |
| 11 | **A5** `VerifyDNSCryptChainAsync` | M | A3, A4 | `UnboundManager.cs` |
| 12 | **A8(a)(c)** transport-gate relaxation | M | A1, A3, A5 | `EncryptedDNSConfigurator.cs` |
| 13 | **A6** `verify-dnscrypt` CLI verb | S | A5 | `UnboundCommand.cs` |
| 14 | **A13** test surface | M | per-task | new test files (**ownership escalation**) |
| 15 | **A14** real-run bar | M | A11 | new test file |

**Run before task 10:** V-A7-1. Its result can change A7's design (the 127.0.0.53 topology question),
so writing A7 first risks writing the wrong thing.
**Run before task 8:** V-A2-1 and V-A3-1. They determine the binary path and whether the
pre-existing-listener guard is defensive or mandatory.
**Run before task 6:** V-A9-1, V-A9-2, V-A9-3. They are D-A1's entire evidentiary basis. If V-A9-1
comes back FAIL (a byte counter exists after all), A9 and A10 both get simpler and better — but only
if the run happens first.

Per parent plan §4.1, each finished file goes to seat A with: the C# `file:line` range changed, the
macOS twin path, and every deliberate deviation with a one-line reason. This lane's deliberate
deviations, declared in advance so they are not re-litigated per file:

1. **`FlowActivity` tri-state** where macOS has a boolean growth test — Decision D-A1. JUSTIFIED:
   the platform has no per-socket UDP byte counter.
2. **Self-exclusion by socket inode** where macOS matches process-name prefixes — §2. JUSTIFIED:
   `comm` is unreliable under `dotnet <dll>`, and the inode test is strictly stronger.
3. **`AttributionComplete` ⇒ `ObservationFailed`** — a Linux-only branch with no macOS twin (A11).
   JUSTIFIED: macOS `nettop` always attributes; `/proc` unprivileged does not.
4. **`SIGKILL` without a `SIGTERM` grace period** on proxy stop (A3). JUSTIFIED: .NET has no
   portable `SIGTERM` API and the proxy holds no state Syxx reads across a restart.
5. **Global (not per-link) `resolvectl dns`** in A7 — matches the existing call site at
   `EncryptedDNSConfigurator.cs:105`, diverges from `WindowsSystemResolverSwitcher`'s per-interface
   model. ESCALATE if the auditor disagrees; it is a real design choice, not an idiom.

---

## 6. What this lane does NOT close

Stated so nobody reads a completed Lane A as "Linux transport is at parity".

- **The privileged/definitive tier.** `TransportObservationMethod.PacketObservation`
  (`DNSTransportState.cs:59-60`) has no implementation on any platform in scope here. On Linux the
  natural mechanism (eBPF on `udp_sendmsg`) would make unicast UDP/53 *observable*, i.e. would put
  Linux **ahead** of macOS on the one question tier 1 provably cannot answer. That is a separate
  project and it is where the D-A1 cost gets bought back.
- **A GUI surface.** Out of scope per parent plan §1, and macOS has no Kit-level consumer either.
- **Captive-portal failsafe** for encrypted DNS — flagged as an owner decision at
  `EncryptedDNSConfigurator.cs:17-19` and still open. Not E1's to decide.
- **`syxx unbound enable` on a non-systemd-resolved Linux** (resolvconf, NetworkManager-only,
  Alpine). A7 handles systemd-resolved and must *honestly refuse* elsewhere.

---

## 7. What I could not determine from the repo

Every item here is a real gap in my knowledge, not a formality. None of it is guessed at above; each
is attached to a verification command or is explicitly deferred.

1. **Whether Ubuntu's `dnscrypt-proxy` package uses socket activation, and on which address/port.**
   Nothing in this repo mentions the Debian packaging. This is hazard H1 in D-A2 and the single
   biggest risk to the DNSCrypt group. → V-A3-1.
2. **Whether an AppArmor profile confines `dnscrypt-proxy` on the target release**, and therefore
   whether a Syxx-owned TOML and query log outside `/etc/dnscrypt-proxy/` are readable/writable by
   it. → V-A3-2. If confined, A3's state-dir design changes.
3. **The exact `/proc/net/{tcp,udp}` column layout on the owner's kernel**, and definitively that no
   cumulative per-socket byte counter exists. D-A1 rests entirely on this. → V-A9-1.
4. **Which byte-counter field names this iproute2 emits** (`bytes_sent` vs `bytes_acked`), and
   whether they appear without `-p`/root. → V-A9-2.
5. **Whether `resolvectl dns ""` + `resolvectl revert ""` is a working set/restore pair**, and the
   exact `resolvectl status` text A7's `Snapshot` must parse. → V-A7-1.
6. **The 127.0.0.53 stub topology decision** — whether "point system DNS at Unbound" on Linux means
   setting resolved's upstream to `127.0.0.1` or displacing resolved entirely. This changes what the
   flow observer sees (`CleartextLoopback` vs `CleartextOffHost`) and therefore what the product
   reports. → V-A7-1, but it may need an owner decision on top of the run.
7. **Whether an unprivileged user can obtain the DNSSEC root anchor** via `unbound-anchor` into a
   Syxx-owned directory on Ubuntu. If not, `syxx unbound verify` and `verify-dnscrypt` are root-only
   on Linux — a real parity loss versus macOS. → V-A5-1.
8. **Whether `Program.cs` already threads a return value into `Environment.ExitCode`.** I read
   `UnboundCommand.cs` (which returns `void`/`Task`) but not `Program.cs`. A6's exit-code parity
   depends on it; check before changing signatures.
9. **Snap confinement's effect on `/proc/self/fd`, `/proc/<pid>/fd` and `/proc/net/*`.**
   `Linux/snap/snapcraft.yaml` builds the CLI (parent plan §0.2), and strict confinement could break
   the entire observer. Partially covered by V-A9-3 if the owner runs it from an installed snap; if
   they run the bare binary, the snap case stays open. **Flag to E3**, who owns the snap.
10. **Whether the parity auditor's `syxx-parity-check` skill has landed** (parent plan §6 Phase 0
    step 1). It was unreachable from the main tree at authoring time. The deviation declarations in
    §5 assume the classification scheme in parent plan §4.3.
11. **Real DNSCrypt relay reachability from the owner's network.** `EncryptedDNSConfig.swift:108-110`
    records `anon-scaleway` failing to Quad9 live from a macOS host; whether any of the three pinned
    relays reaches Quad9 from the owner's Linux box is unknown, and it determines whether
    `verify-dnscrypt` can reach `ReadyAnonymized` at all there. → V-A3-3.
12. **The pre-existing baseline state of the four gates** in parent plan §4.6. If any is already red,
    this lane must not inherit blame for it (parent plan §6 Phase 0 step 2). I did not run them —
    building is blocked at authoring time and running them is not a planning action.
