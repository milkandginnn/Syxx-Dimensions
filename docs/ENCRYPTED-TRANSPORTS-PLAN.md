# Encrypted Transports and Trusted Resolution: Program Plan

Master design and decomposition for adding systemwide DoH3, DoT, and DNS-over-QUIC
transports, resolver profiles, a bundled Unbound local validating resolver, and
server-side encrypted listeners to Syxx. This document is the program-level blueprint.
Each sub-project below gets its own engineer-ready plan (produced next by the planner
agents) with pre-queued commands and step-level acceptance bars.

Anchors this plan builds on: `DESIGN.md:309-341` (the four-layer arc), `ROADMAP.md:954-967`
(the deferred track and suggested order), `PROPOSALS.md` A1 (`:72`, multi-transport
corroboration) and A8 (`:93`, modern nslook with provenance).

Status: DESIGN, pending user sign-off on the open decisions in section 4. No code is
written until this design and its open decisions are approved.

---

## 1. Scope (locked by the user)

- **Both platforms, full parity.** macOS `SyxxKit` and Windows `Syxx.Kit`, line-for-line.
- **Full server DoH / DoT / DoQ**, with DoQ gated behind a measured RAM budget (not a guess).
- **Unbound in both modes, user-selectable:** full DNSSEC-validating recursion on loopback,
  and forward-over-DoT to a chosen resolver profile.
- **Ubuntu client:** follow-on note only (via the cross-platform .NET `Syxx.Kit`), not in this
  effort's build or real-run scope.

### Monetization (locked by the user)

| Feature | Tier |
|---|---|
| **DoH3** (DoH over HTTP/3) client transport | **Free** |
| **DoT** client transport | **Paid** |
| **Bundled Unbound** local resolver | **Paid** |
| **DoQ** client transport | **Open decision** (recommend Paid, grouped with DoT) |
| Resolver profiles / routing | Follows the transport it selects (free if DoH3, paid if DoT) |
| Systemwide routing toggle | Paid in practice (routes over DoT / Unbound, both paid) |

Gating mechanism is the existing entitlement path: macOS `LicenseManager.isFeatureUnlocked`,
Windows `IsFeatureUnlockedAsync` (server-validated, per D013). Paid transports must gate on the
server-validated unlock, never a local bool.

### Dev-ID posture (locked by the user)

The Apple Developer ID and NetworkExtension entitlement are **in progress**. Their absence
blocks **runtime testing** of the macOS systemwide-routing pieces and notarized distribution of
bundled Unbound. It does **not** block implementation. Consequence for this plan, binding under
Protocol §2: macOS NE code is marked `[code-complete, real-run pending entitlement]` and is
**never** recorded `[done]` on a green build alone.

---

## 2. Program decomposition

| # | Sub-project | Gated? | Real-run now? | Platforms | Tier |
|---|---|---|---|---|---|
| **A** | Client encrypted transports (DoT, DoQ, DoH3) + transport provenance | No | Yes | mac, Win | DoH3 free; DoT/DoQ paid |
| **B** | Multi-transport corroboration (feeds Mirror / synthesis) | No | Yes | mac, Win | n/a (engine) |
| **C** | Resolver profiles / routing | No | Yes | mac, Win | per-transport |
| **D** | Server encrypted listeners (DoH, DoT, DoQ) | DoQ dep-gated | DoH/DoT yes; DoQ after RAM gate | Server | n/a |
| **E** | Bundled Unbound (both modes) | Distribution/notarization (macOS) | Build+run yes; notarized ship later | mac, Win, (Ubuntu note) | Paid |
| **F** | Systemwide routing + menu-bar/tray | NE entitlement (macOS runtime only) | Win yes; macOS code-complete/gated | mac, Win | Paid |

**Dependency order:** A first (foundation). Then B and C depend on A. D runs in parallel
(independent of the client). E depends on C (its forward-over-DoT mode targets a resolver
profile). F depends on A, C, and ideally E. This matches `ROADMAP.md:966`.

---

## 3. Cross-cutting honesty contract (binding on every sub-project)

This program adds Syxx's **first write path into OS DNS state** and its first set of
self-hosted encrypted listeners. The honesty spine now governs engaged/disengaged state and
listener health, not just verdicts. Five rules, enforced everywhere below:

1. **Proven, not requested; proven, not configured.** A transport label reflects the transport
   that *actually carried* the answer (proven by ALPN, `URLSessionTaskMetrics.networkProtocolName`,
   `HttpResponseMessage.Version`), never what was requested. An "engaged over DoT" or "encrypted"
   state reflects a *live resolution through the intended path this session*, never a successful
   config write. This is the same false-clean class as the filed D056, E014, and B032.
2. **No silent downgrade, no encrypted-to-plaintext fallback.** h3 to h2, DoT to Do53, DoQ
   handshake to nothing: each is tagged and demotes the result. The existing UDP-to-TCP fallback is
   safe (both plaintext) and stays; an encrypted transport must **not** copy it and fall back to
   Do53. If it does, tag `servedTransport = .udp` and mark inconclusive-as-reference.
3. **Tri-state end to end.** Transports: served/unknown. Routing: engaged/disengaged/unknown.
   DNSSEC: validating/bogus/insecure. Listener health: listening/degraded-fallback/down. No bare
   booleans (retires the pre-existing two-bool `SecureResolutionResponse`, section A0).
4. **Fail-closed on TLS.** No permissive certificate-validation callback on any stack, ever.
   Handshake, cert, SNI, or timeout failure maps to `.transportError`/inconclusive, never success.
5. **Honest failure is SERVFAIL/refused/blocked, never a fabricated clean answer.** A listener or
   probe that cannot resolve returns an explicit error, never an empty NOERROR or "nothing wrong."

**User-visible copy:** every new string (transport labels, engage/disengage notices, health
lines, resolver-profile names, portal-disengage notice) is user-facing. Per AGENTS.md it is
proposed and approved before implementation and logged to `outputs-catalog.csv`. No wording is
finalized in code without sign-off.

---

## 4. Open decisions requiring your sign-off (resolve before the gated steps run)

These are genuine blockers or forks, not priorities. Each has my recommendation.

1. **[BLOCKER] Server answer-engine scope (sub-project D).** The server has no general resolver;
   its DNS side is authoritative-for-leak-zones-only, all else REFUSED (`dns_authoritative.py:62-66`).
   What do the encrypted listeners answer?
   - (a) Authoritative-leak-only. Safe, but near-useless as a "clean reference resolver."
   - (b) Forward to `config.PUBLIC_RESOLVERS` (`config.py:177`). Simple, but an internet-open
     forwarding resolver is the DDoS-reflector open resolver Syxx's own `/api/exposure` warns
     against (`app.py:851-854`).
   - (c) Walker-backed via `resolve_authoritative` (`resolver.py:384`). Ground-truth, heavy per query.
   - **Recommendation:** DoH gated behind the existing paywall (`_require`, `app.py:220`) as a
     forwarder (b) with an IP-allowlist and rate limit; DoT/DoQ authoritative-leak-only or
     paywalled-by-scope, since they cannot carry an HTTP license header. Confirm the intent: is the
     server meant to be a resolver users point at, or only an encrypted channel for Syxx's own
     probes and the leak collector?
2. **[BLOCKER, dependency] `aioquic` for server DoQ.** Not installed; the server is deliberately
   stdlib-only plus `cryptography` (`requirements.txt:7`). Adding `aioquic` (pulls `pylsqpack`,
   `certifi`) and an asyncio loop the process does not have today is required for server DoQ.
   Approve adding it, or defer server DoQ. **Recommendation:** approve, but only after the RAM
   measurement gate (D-4) returns numbers that fit the VPS.
3. **DoQ client tier.** Free (like DoH3, also QUIC) or paid (like DoT)? **Recommendation:** paid,
   grouped with DoT as "premium encrypted transports."
4. **App TLS cert for server DoT/DoQ.** The Let's Encrypt cert lives only in nginx
   (`deploy.sh:224-259`); the app has none. DoQ cannot be nginx-proxied, so the app must terminate
   TLS itself for DoQ (and for app-terminated DoT). **Recommendation:** grant the service read
   access to the LE privkey via group perms, or provision a dedicated cert. Confirm which.
5. **Windows privilege model (sub-projects E, F).** A standing elevated Windows service, or a
   UAC-per-action helper? **Recommendation:** a service, since NRPT and DoH registration are HKLM
   writes and Unbound binds a privileged port.
6. **Unbound bind port (sub-project E).** Privileged `127.0.0.1:53` (needs a root/admin helper) or
   high port `127.0.0.1:5335` plus redirect. **Recommendation:** 5335 on macOS to avoid a root
   helper for local run; `:53` via the Windows service.

### Reconciliation after planning (2026-07-14)

The four sub-plans landed. Status of the decisions above and new ones the planners surfaced:

- **Decisions 2-6 proceeding on the recommended defaults** unless the user says otherwise: aioquic
  approved but gated behind the D-4 RAM measurement; DoQ client paid; app TLS via the LE key (the
  unit already runs as root so the first real run needs no perms change, with a `setfacl` group-read
  grant staged for the eventual SD-53 root-drop); Windows elevated service; Unbound 5335 (macOS) /
  53 (Windows). The macOS lifecycle is a **LaunchAgent**, not a LaunchDaemon (corrected in 5.E).
- **Decision 1 is the one genuine product fork still open.** The D plan sharpened it: the paywalled
  DoH real-run needs `curl` with an `Authorization: Bearer` header because `kdig +https` cannot carry
  a license. So the question is concrete: **is the server a resolver users authenticate to and point
  their systems at, or only an encrypted channel for Syxx's own probes plus the leak collector?**
  This changes the DoH product, its abuse controls, and its copy. Needs a user answer.
- **New (recommended defaults, correct if wrong):**
  - **DoH raw-wire passthrough, not re-encode** (D plan). Re-encoding from the server parser drops
    record types and yields a fabricated-looking answer (honesty rule 5). Recommend passthrough.
  - **C profile persistence** (A+C plan). `SyxxConfig` has **no** persistence mechanism on either
    platform today. C needs one added: recommend `UserDefaults` (macOS) and
    `%APPDATA%\Syxx\config.json` (Windows).
- **New cross-plan couplings to hold during implementation:**
  - Unbound forward-over-DoT mode consumes a **DoT** resolver profile only; a DoQ-only C profile
    cannot back forward mode (E+F plan). C must carry the transport type and E must reject non-DoT.
  - Sub-project A and step E6a both touch the `DNSResolver` OPT builder (E6a adds the DNSSEC-OK/DO
    bit the health check needs, `DNSResolver.swift:415-420` / `.cs:379-386`); sequence so they do
    not collide.
  - B consumes A1's provenance fields verbatim (`requestedTransport`, `servedTransport?`,
    `negotiatedProtocol?`, derived `wasDowngraded`); A1 must land before B3 wires the emitter.
- **DoQ-macOS unblocked:** the `NWProtocolQUIC` client bidirectional-stream spike compiled on the
  live toolchain; A3-macOS is `[code-complete]` pending the real `94.140.14.14:853` runtime run.

---

## 5. Sub-project plans

### A. Client encrypted transports (DoT, DoQ, DoH3) + provenance

**Goal.** Add DoT, DoQ, and DoH3 to both client resolvers so an encrypted reference lookup cannot
be transparently rewritten by an on-path Do53 proxy, with per-answer transport provenance so a
downgrade is always visible. Delivers A8 and the data path for A1/B.

**Key files.**

- macOS `macOS/Sources/SyxxKit/DNSResolver.swift`: `DNSQuery` (5-32, no transport selector today),
  `DNSResponse` (111-190), `DNSResponseStatus` (89-100), `didFail` (181-189),
  `SecureResolutionResponse` (192-198), `queryUDPDetailed` (272), `queryDoHDetailed` (334-371),
  `dohSession` (204-210), `sendUDP` (1039-1096, NWConnection.udp 1041), `sendTCP` (1098-1150, tcp
  1109, length-prefix 1100-1106), `readTCPResponse` (1152-1182), `makeQueryPacketWithID` (381),
  `parseResponsePacketDetailed` (646/654), `responseQuestionMatches` (1020).
- Windows `Windows/Syxx.Kit/DNSResolver.cs`: `DNSQuery` (13-41), `DNSResponse` (88-120), `DidFail`
  (113-119), `DNSResponseStatus` (51-63), `SecureResolutionResult` (65-69), `SecureResolutionResponse`
  (71-77), `SendUDPAsync` (927-953), `SendTCPAsync` (955-977, length-prefix 968), `ReadExactAsync`
  (979-998), `QueryDoHDetailedAsync` (289-331), static `HttpClient` (124-127), builder (343),
  parser (459), `responseQuestionMatches` (426).
- Probe to extend, not replace: `EncryptedTransportProbe` (macOS `.swift:35-62`, Windows `.cs:16-44`),
  callers `ProbePlan.swift:168`, `SyxxCLI.swift:412`, `ProbePlan.cs:172`.
- Pinning to selectively reuse: `LeakDetector.swift:95-108` (URLSession only; unusable for NWConnection).

**Step A0 (do first): fix the tri-state result type.** macOS `SecureResolutionResponse` (192-198)
uses two independent Bools (`isSecure`, `isInconclusive`) that can encode both-true and both-false,
the exact boolean-where-tri-state defect. Windows has an enum but only `{Secure, Inconclusive}`.
Replace with a shared tri-state (`secure / inconclusive / unverifiable`) on both platforms before
any transport hangs off it, or a downgrade is crushed into a bool. **Already tracked as delta E030**
(`DELTAS.md:15888`, fully vetted, non-critical today because constructors are consistent). A0 is
E030's Fix 1 (port the Windows enum as the sole verdict field) + Fix 2 (reject unknown decoded
values), **extended** with a `tampered`/`unverifiable` member that E030 did not anticipate, needed
for the proven-downgrade case. Do not file a new delta; anchor to E030.

**Step A1: transport provenance model.** Add to `DNSResponse` on both platforms:
`requestedTransport: DNSTransport`, `servedTransport: DNSTransport?` (inferred from evidence, nil or
`.unknown` when undeterminable), `negotiatedProtocol: String?` (the observed token: "h3", "h2",
"http/1.1", "quic", TLS version). `DNSTransport` enum: `{ udp, tcp, doh, dot, doq, doh3, unknown }`.
Derived `wasDowngraded = servedTransport != nil && servedTransport != requestedTransport`. Rule:
`servedTransport == .unknown` makes any encrypted/DoH3 claim inconclusive; a downgrade to plaintext
makes the answer inconclusive-as-reference.

**Step A2: DoT (paid).** macOS: `sendDoT` clones `sendTCP` but uses `NWParameters(tls:tcp:)` to
:853, SNI = resolver hostname, ALPN "dot". Windows: `SslStream` over `TcpClient` to :853,
`AuthenticateAsClientAsync(host)`, reuse `ReadExactAsync` and length-prefix framing. Same 2-byte
length-prefix wire format as DNS-over-TCP. Fail-closed cert validation (system trust store default,
no permissive callback). Add an explicit pinning posture field (`enforced / notConfigured / failed`)
mirroring D003; DoT/DoQ pinning on macOS is net-new (needs a `sec_protocol_options` verify block,
the URLSession D003 pin does not apply).

**Step A3: DoQ (tier per decision 3).** macOS: `NWConnection` with `NWParameters(quic:)`, ALPN
"doq", :853. **Verify first:** confirm macOS 26 `NWProtocolQUIC` exposes usable client-initiated
bidirectional streams before committing (historically thin; PAUSE if not). Windows: `System.Net.Quic`
`QuicConnection`/`QuicStream`, ALPN "doq", gated by `QuicConnection.IsSupported`; requires msquic
(Win11 22H2+ or app-local). Where unsupported, report `.untested`/inconclusive, never fail-silent.
**Trap:** RFC 9250 requires the DNS message ID be **0** on DoQ; the current builder randomizes it and
the parser rejects mismatches, so a compliant resolver would look spoofed. Add a DoQ packet builder
with ID 0 and pass `expectedTransactionID: 0` (rely on stream binding + `responseQuestionMatches`).

**Step A4: DoH3 (free).** macOS: opt into HTTP/3 on `dohSession` and **prove** it by reading
`URLSessionTaskMetrics` `transactionMetrics.last.networkProtocolName`; label DoH3 only when it
returns "h3", otherwise it is DoH-over-h2/h1 (still TLS, not h3). Windows: `request.Version =
HttpVersion.Version30` with `HttpVersionPolicy.RequestVersionExact` (forces h3, the honest choice);
prove via `response.Version == 3.0`. Reuse the D003 URLSession/HttpClient pin here (it does apply).
Honesty crux: "we asked for h3" is not "the answer came over h3"; an on-path attacker can strip
Alt-Svc to hold you on h2. Never label DoH3 without the metric.

**Fail-closed rules (all A steps).** Handshake/cert/SNI/timeout to inconclusive, never success. No
permissive TLS callback. No encrypted-to-plaintext fallback. Claimed transport proven by evidence or
`.unknown`. DoT/DoQ carry a pinning posture.

**Real-run bar (per transport, plus a failure case each).**

- DoT: `queryDoTDetailed(cloudflare.com, A, 1.1.1.1:853)`; pre-flight `openssl s_client -connect
  1.1.1.1:853 -servername cloudflare-dns.com`. Honest: success, records non-empty,
  `servedTransport=.dot`, TLS 1.3. Failure case: blocked :853 reports inconclusive, not clean.
- DoQ: AdGuard `94.140.14.14:853` ALPN "doq", ID 0; cross-check `kdig +tls +quic`. Failure case: a
  compliant resolver must not report `.responseIdMismatch` (proves the ID-0 fix shipped).
- DoH3: Cloudflare `https://cloudflare-dns.com/dns-query`, forced h3. Honest:
  `networkProtocolName=="h3"` / `Version==3.0`. Failure case: Alt-Svc-stripped host reports
  `servedTransport=.doh` + `wasDowngraded=true`, not DoH3.

**Dependencies:** msquic (Windows DoQ/DoH3); macOS `NWProtocolQUIC` stream-API verification.
**Effort:** M-L. **Dedup anchors:** PROPOSALS A1 (`:72`), A8 (`:93`); DELTA `:2501`.

### B. Multi-transport corroboration (engine)

**Goal.** Feed diverging encrypted-vs-plaintext answers into Mirror / synthesis as tamper evidence
(A1, `PROPOSALS.md:72`; PROPOSALS #4). This is the one sub-project that changes the verdict/evidence
engine, so it routes to `syxx-neural-planner` / `syxx-neural-executor`, not the transport lane.

**Shape.** After A lands transport provenance, add a comparison that runs the same query over Do53
and an encrypted transport, and treats a divergence in answers (where the encrypted path is the
trustworthy baseline) as a tamper signal, tri-state, with the minimum-corroboration rule from the
existing exfil work. Must not manufacture a verdict from a single transport, and a downgrade or
`.unknown` served transport yields inconclusive, not tampered. Detailed design is deferred to the
neural pipeline once A's provenance fields are fixed.

**Real-run bar.** A network where Do53 is poisoned but DoT/DoH3 is clean produces a tamper signal;
a clean network produces no false tamper. **Effort:** M (design-led).

### C. Resolver profiles / routing

**Goal.** `{name, transport, endpoint, configId?}` presets (Cloudflare, Quad9, Google, AdGuard,
Mullvad, NextDNS per-user config ID, OpenDNS). The user picks a trusted resolver; Syxx routes its
probes (and, via F, system resolution) through it. UI frames these as trusted third parties, not
Syxx-verified recursion (`DESIGN.md:325`).

**Shape.** A profile struct on both platforms feeding the A transports; selection persisted in
`SyxxConfig`. Free profiles use DoH3; DoT/DoQ profiles gate on the paid unlock. Templated config for
Unbound forward-over-DoT (E) is generated from the selected profile. **Real-run bar:** selecting a
profile routes a probe over the named transport to the named endpoint, proven by A's provenance.
**Effort:** S-M.

### D. Server encrypted listeners (DoH, DoT, DoQ)

**Goal.** Offer DoH, DoT, and DoQ on the server, honest about what they answer (decision 1) and what
is actually encrypted.

**Architecture reality.** Stdlib-only, synchronous, thread-per-connection:
`ThreadingHTTPServer` + hand-rolled `BaseHTTPRequestHandler` (`app.py:166`, `:1024-1038`), HTTP/1.1
only (`app.py:167`). Manual route dispatch: GET `_dispatch_get` (`app.py:344-394`), POST `do_POST`
(`app.py:322-341`), paywall `_require` (`app.py:220`). DNS is authoritative-leak-only
(`dns_authoritative.py`, REFUSE at `:62-66`) plus a client walker (`resolver.py:384`). Deployed
behind nginx + certbot; the app runs plaintext on `127.0.0.1:8080`, cert lives only in nginx
(`deploy.sh:224-259`). `CAP_NET_BIND_SERVICE` already covers :853 for both TCP and UDP; no firewall
is provisioned (SD-56).

**D-1: DoH route.** Add GET/POST `/dns-query` (RFC 8484 `application/dns-message` wireformat +
`application/dns-json`). Near-zero RAM (reuses server, nginx TLS, rate limiter, `_body` at `:263`).
Needs a wireformat encoder (primitives at `dns_authoritative._build_response:40-99`,
`resolver._parse_response:98`). Gate on `_require` per decision 1. Never assert "encrypted DNS" in
the response: the app is reachable plaintext on :8080 (SD-59), so encryption is an nginx property.

**D-2: DoT listener.** `socketserver.ThreadingTCPServer` (ssl-wrapped) on :853, own daemon thread
mirroring `server.py:28`, 2-byte length-prefix (reuse `resolver._query_tcp:176-189`). **Prerequisite:
a connection cap** (retires SD-51 for this path; DoT keep-alive makes the unbounded-thread flood
worse). Cert provisioning per decision 4. If nginx-`stream`-terminated, the app receives plaintext
and must not attest the client channel was encrypted.

**D-3: DoQ listener.** aioquic on :853/udp, ALPN "doq", own asyncio loop on a dedicated thread
(the process has none today). Blocked on decision 2 (aioquic). Cannot be nginx-proxied, so the app
terminates TLS (decision 4).

**D-4: DoQ RAM measurement gate (before D-3 ships).** On the real VPS (`deploy.sh:19`): baseline RSS
at 0 conns (3x); idle-scale at N in {100, 500, 1000} held-open conns, per-conn = (RSS_N - RSS_0)/N,
check linearity; active at N conns issuing queries, record RSS and CPU; gate against `free -m`
headroom accounting for the co-resident HTTP/DoT servers and SQLite WAL. Record raw numbers in the
plan; ship DoQ only if it fits with margin, else defer.

**Honesty (server).** `/healthz` returns `{ok:true}` unconditionally (`app.py:356-357`) and the
collector already degrades silently (`dns_authoritative.py:148-150`, SD-62). Each listener needs an
explicit tri-state (listening / degraded-fallback / down) surfaced in `/readyz` (`app.py:540`), never
a blanket ok. Backend error returns SERVFAIL / HTTP 502, never empty NOERROR. Do not run an
internet-open forwarding/recursing resolver without access control (decision 1).

**Deployment.** systemd unit (`deploy.sh:175-216`) needs :853/tcp + :853/udp opened; coordinate with
SD-56's proposed UFW allowlist so DoT/DoQ do not silently break. Validate any SD-53 `SystemCallFilter`
tightening against aioquic's QUIC/UDP syscall set (tighten-then-test with a real `kdig +quic`).

**Real-run bar.** `kdig +https=/dns-query @syxx.app example.com`; `kdig +tls @syxx.app`;
`kdig +quic @syxx.app`. Honest failure: SERVFAIL/502 or a refused/closed :853, never a plaintext
answer or fabricated NOERROR. Tests: `Server/tests/test_doh.py` (unit, hostile-packet parse via the
`object.__new__` handler pattern), `test_dot.py` / `test_doq.py` (net-new live-socket harness).
**Effort:** DoH S-M, DoT M, DoQ L (dep + new concurrency model + measurement).

### E. Bundled Unbound (paid, both modes)

**Goal.** Bundle Unbound (BSD-3, no licensing pause) on loopback, both modes user-selectable: full
DNSSEC-validating recursion (root hints + `unbound-anchor` trust anchor), and forward-over-DoT
(`forward-zone "." forward-tls-upstream: yes forward-addr: ip@853#hostname`) templated from a C
profile.

**Per-OS lifecycle.** macOS: ship `unbound` + `unbound-anchor` in the .app, per-user launchd
**LaunchAgent** (a LaunchDaemon would need root; 5335 is unprivileged so a LaunchAgent suffices),
bind `127.0.0.1:5335` (decision 6) to avoid a root helper. Windows: `unbound.exe` as a service
(native support), bind `127.0.0.1:53`, service recovery auto-restart. Ubuntu (note): systemd service,
mind the `systemd-resolved` stub-listener contention on `127.0.0.53:53`.

**Health (tri-state).** Start, wait-for-listen, health-check by resolving a control name *through*
Unbound on loopback (reuse the `EncryptedTransportProbe` pattern pointed at loopback). Distinguish:
validating recursion / forwarding-only-insecure / down / unknown. On crash the OS may point at a dead
listener (hard outage) or silently revert: the health check must catch this, never report "protected"
while the OS uses the old resolver. Forward mode must not be labeled "DNSSEC validating" (its
guarantee is only as good as the upstream).

**Real-run now vs gated.** Local build, bundle, launchd/service management, both config modes,
`unbound-anchor` bootstrap, health check, crash behavior: real-runnable today on both platforms.
macOS notarized **distribution** of the bundled binary is Dev-ID-gated (not local run). **Effort:** L.

### F. Systemwide routing + menu-bar/tray (paid)

**Goal.** One-click route system DNS over the chosen transport / local Unbound, with a menu-bar
(macOS) / tray (Windows) host and a non-negotiable captive-portal failsafe.

**macOS.** Two APIs, uncoupled: `NEDNSSettingsManager` (the toggle; `NEDNSOverHTTPSSettings` /
`NEDNSOverTLSSettings`; entitlement `com.apple.developer.networking.networkextension` value
`dns-settings`; compiles now, `saveToPreferences` runtime-gated) as the primary layer; and
`NEDNSProxyProvider` (`handleNewFlow`, value `dns-proxy`, separate extension target) only for the
in-flow PID attribution fix (its `NEFlowMetaData.sourceAppAuditToken` gives the originating PID,
retiring the post-hoc `lsof` race). `NSStatusItem` agent app (`LSUIElement=true`, no Dock) is
buildable and real-runnable now; only the routing action is gated.

**Windows (ships anytime, no gate).** NRPT (`Add-DnsClientNrptRule` / `DnsPolicyConfig`) plus
`netsh dnsclient add encryption server=<ip> dohtemplate=<url> autoupgrade=yes udpfallback=no` (the
`udpfallback=no` is the fail-closed knob) and per-interface DNS to `127.0.0.1` for local Unbound.
Capture prior state and restore exactly on disengage (`delete encryption`, `Remove-DnsClientNrptRule`,
`Set-DnsClientServerAddress -ResetServerAddresses`). Requires elevation (decision 5). WFP callout (the
in-flow-PID equivalent) is a signed kernel driver, out of initial scope. Detect the Windows build
floor (DoH client is Win11 / Server 2022+); on older builds the only path is local Unbound, report
unsupported, not silent no-op.

**Captive-portal failsafe.** Reuse `NetworkKeyState.captivePortalStatus()` (macOS `:324-347`,
Windows `:256-300`, already tri-state). Before engage and on every network change, if portal `.yes`,
auto-disengage so the portal resolver can complete login. Fail safe on disengage (never leave broken
DNS), but never report "encrypted/secure" while disengaged.

**Fail-closed rules (F).** Engaged requires a live proof-of-path this session (F1), re-read system
DNS and confirm it points at us (F2), no plaintext fallback in fail-closed mode (F3, `udpfallback=no`),
tri-state health (F4), disengage proven and safe with state restore (F5), portal dominates (F6), one
shared tri-state contract across platforms so they cannot diverge like E014 (F7). Never derive
"engaged/encrypted" from config presence: `HasConfiguredDoHTemplate()` (`NetworkKeyState.cs:347-363`)
is detection-only, never the engaged-state oracle.

**Real-run now vs gated.** Windows routing (NRPT + netsh + restore) real-runnable on Win11/Server
2022+ today. macOS `NSStatusItem` host + portal failsafe logic real-runnable now. macOS
`NEDNSSettingsManager` engage/disengage and `NEDNSProxyProvider` activation are code-complete,
real-run pending entitlement. **Effort:** L.

---

## 6. Sequencing and "done" ledger convention

Suggested order (borrowing D's parallelism):

1. **A0 + A** (foundation, real-runnable), file the tri-state delta.
2. **C** (profiles) and **B** (engine, neural pipeline) once A's provenance lands. **D** in parallel:
   D-1 DoH, then D-2 DoT, then D-4 measurement gate, then D-3 DoQ (dep-approved).
3. **E** (Unbound, real-runnable locally).
4. **F** (Windows real-run now; macOS code-complete, real-run pending entitlement).

**Ledger honesty.** A step is `[done]` only after one real run per platform with honest output.
macOS NE steps and notarized Unbound distribution are recorded `[code-complete, real-run pending
entitlement]` and never `[done]` on a green build. DoQ (client and server) is `[untested]` where the
QUIC stack is unavailable, never fail-silent. Keep `CHANGELOG.md`, `ROADMAP.md`, `DESIGN.md`, and
`outputs-catalog.csv` in sync per AGENTS.md.

---

## 7. Next actions

1. **You:** resolve the section-4 open decisions (especially the two blockers: server answer-engine
   scope, and `aioquic` approval).
2. **Me:** on your sign-off, dispatch `syxx-planner` to produce engineer-ready plans for A, C, D, E, F
   (per-step file:line, method signatures, pre-queued commands, step acceptance bars), and
   `syxx-neural-planner` for B. A0 is anchored to the existing delta **E030** (extended with a
   `tampered`/`unverifiable` state), not a new filing. Verify-first spike for macOS `NWProtocolQUIC`
   streams before DoQ is committed.
