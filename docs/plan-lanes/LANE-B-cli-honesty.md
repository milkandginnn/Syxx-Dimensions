# LANE B — CLI Contract & Platform Honesty (seat E2)

**Scope.** The shared C# CLI (`Windows/dotNET/Syxx/`) and the parts of the shared engine
(`Windows/dotNET/Syxx.Kit/`) that feed it. One binary serves Windows **and** Linux; the macOS
Swift CLI (`macOS/Sources/SyxxCLI/`, `macOS/Sources/SyxxKit/`) is the parity reference.
`Linux/dotNET/Syxx.Linux/` is a dead stub and is ignored throughout.

**Goal.** Full functional parity — every macOS feature present on Linux — without importing any
macOS *dishonesty*, and without regressing the places where C# is already better.

**The rule every task below serves.** A measurement that could not be taken renders
unknown / inconclusive — never clean, secure, or definite. Collapsing a tri-state into a bool is
the defect class this product exists to refuse.

**House copy rule.** The word "verdict" is banned. `result` is the identifier used in code;
`conclusion` is the word used in module names, UI, and docs. Where the two platforms disagree on
a user-facing label, the user-facing string becomes `conclusion` (tasks B15, B16).

---

## §1 — Ranked task list

Ranked by *how badly a user could be misled*, worst first. "New" marks findings produced by the
Syxx.Kit sweep in §3 that were not in the brief.

| # | Task | Misleads by | Effort | Depends on | New |
|---|------|-------------|--------|------------|-----|
| B0 | Make command results returnable + testable | (enabler — no user impact) | M | — | |
| B1 | `firstrun` never maps its determination to an exit code | Flagship scan detects a THREAT, exits 2 = "nothing measured" | S | B0 | |
| B2 | `key` / `filter` / `forensics` / `sentinel` drop THREAT signals | Captive portal, unenforced filter, beaconing, config drift all exit 2 | M | B0 | |
| B3 | `sentinel` loses unattended detections; no save-error handling; missing empty-resolver guard; no quarantine | A change detected overnight is erased by the baseline roll; a failed write crashes after printing success | M | B0, B4 | |
| B4 | `DnsEncrypted` tri-state collapsed to `bool` | Unreadable transport prints "no" = plaintext; fires phantom `encrypted -> plaintext` drift; monitor warns "lookups now visible" from an unmeasured state | M | — | partly |
| B5 | `DhcpPushedDNS` returns empty (not null) off Windows | "Router pushed no DNS" and "we could not check" are the same output — a primary hijack vector made invisible | M | — | |
| B6 | Gateway `AdminReachable` collapses Unknown → No | A firewalled/timed-out probe prints as a definite "no admin panel on the router" | S | — | **New** |
| B7 | `leak` hides the LEAK headline, hardcodes the resolver count, drops the 4-state signature result and transport pinning | "1 resolver observed" is printed even when zero were; an unverifiable collector receipt reads like a verified one | M | B0 | |
| B8 | `lan` collapses the snapshot tri-state, asserts off-LAN, omits the ARP caveat, never persists | A failed ARP read and a partial read render identically; "they are reached through the gateway" is asserted from a cache miss | M | B0 | |
| B9 | `witness create` has no `--key-file` and no argv-exposure guard | The Ed25519 signing key travels through `/proc/<pid>/cmdline`; on Linux there is no file-based alternative at all | M | — | |
| B10 | `witness --gen-keypair` silently overwrites an existing key | One re-run destroys the signing identity; every previously signed snapshot is orphaned | S | — | **New** |
| B11 | `forensics --analyze` conflates I/O error with "not an endpoint log", and does not expand `~` | An unreadable endpoint log is reported as a capture log with no records; `~/log.json` "does not exist" | S | B0 | partly |
| B12 | `firstrun` never passes `isPro` | A paying Linux user silently gets the Lite discipline subset and a LOWER grade | S | — | |
| B13 | `-v` means `--version` in C#, `--verbose` on macOS | `syxx leak -v` prints a banner and exits — the user believes the check ran | S | — | |
| B14 | `unbound`: no `verify-dnscrypt`, no "Gated (not built)" line, Windows elevation wording on Linux | A whole macOS subcommand is absent; Linux is told to use an "elevated prompt" | L | B0 | partly |
| B15 | `forensics` says `result:` where macOS says `conclusion:`; `unbound` says `RESULT:` | Cross-platform transcripts and docs disagree; violates the house copy rule | S | — | |
| B16 | `key` omits the resolver-empty reason | "None detected" without cause reads like "you have no DNS", not "we could not read it" | S | — | |
| B17 | `nslook` / `route` / `speed` still never set an exit code | Every invocation exits 2 regardless of what happened | S | B0 | |
| B18 | LAN snapshot persistence absent from the C# Kit | `LANDiff` exists but can never run — a whole macOS capability is inert on Linux | M | B8 | **New** |
| B19 | `ContinuousMonitor` has no "VPN state is now unknown" alert | A VPN that became *unmeasurable* is reported as "Your VPN is no longer active." | S | B4 | **New** |
| B20 | Witness trust anchors read different env vars per platform | macOS docs (`SYXX_COLLECTOR_TRUSTED_KEYS`) configure nothing on Linux → every signer stays unanchored | S | — | **New** |
| B21 | `firstrun` section header / suppressed-signal wording diverges | Transcript diffing and support scripts break across platforms | S | B1 | **New** |

---

## §2 — Tasks

### B0 — Make command results returnable and unit-testable *(enabler)*

1. **What changes.** Every `internal static class *Command` in
   `Windows/dotNET/Syxx/Commands/` changes `RunAsync(string[] args)` from `Task` / `void` to
   `Task<int>` returning a `CLIExitStatus`-shaped value; `Program.cs:75-93` assigns
   `Environment.ExitCode = await XCommand.RunAsync(args[1..])`. Add
   `Windows/dotNET/Syxx.Kit/CLIExitStatus.cs` with `Completed = 0, Threat = 1, Inconclusive = 2,
   Failure = 3` and XML docs quoting the contract. `Program.cs:25` keeps its
   `Environment.ExitCode = 2` seed (it is the correct default for the interactive path), but it
   stops being load-bearing for subcommands. `WitnessCommand.ExitStatus`
   (`WitnessCommand.cs:30-44`) stays as-is and its `Exit()` helper (`:67`) becomes
   `return (int)status`.
2. **macOS reference.** `macOS/Sources/SyxxCLI/SyxxCLI.swift:17-21` (`CLIExitStatus`) and
   `:46-100` (dispatcher assigns `exitCode = await XCLI.run(...)` for every subcommand).
3. **Wrong today / required.** Today `syxx key` on a hijacked network prints
   `⚠️  DETECTED` and exits `2`. Required: the printed text is unchanged and the process exits
   `1`. This task changes no printed byte; it only makes B1/B2/B7/B8/B11/B14/B17 expressible
   and testable without launching a process.
4. **False-clean it removes.** None directly — it removes the *structural* possibility that a
   command "falls out without deciding", which is what produced every defect in B1/B2/B17.
5. **Failure-case test.** `Syxx.Tests/CliExitContractTests.cs`:
   `EveryCommandReturnsAnInt()` — reflect over every type in `Syxx.Commands` whose name ends in
   `Command`, assert the public `RunAsync` returns `Task<int>`. A newly-added command that
   returns `Task` fails the build's test run. Second test:
   `NoCommandWritesEnvironmentExitCodeDirectly()` — grep-style source assertion over
   `Windows/dotNET/Syxx/Commands/*.cs` (embedded as a test resource, or an analyzer rule) that
   `Environment.ExitCode` appears zero times outside `Program.cs`.
6. **Effort.** M. **Order.** First. Everything in the exit-code family depends on it.
7. **Owner verification.**
   ```
   dotnet build Windows/dotNET/Syxx/Syxx.csproj -c Release
   dotnet test  Windows/dotNET/Syxx.Tests --filter CliExitContractTests
   ```
   - **pass** — build succeeds, both tests green.
   - **fail** — a command still returns `Task`, or `Environment.ExitCode` is still assigned
     inside `Commands/`.
   - **inconclusive** — build fails for an unrelated reason (missing SDK, restore failure). The
     test result is unknown; **do not** record this task as done.

---

### B1 — `firstrun` must exit on its own determination

1. **What changes.** `Windows/dotNET/Syxx/Commands/FirstRunCommand.cs:13-147`. Add, after the
   suppressed-signals block at `:144`, the determination → exit mapping. The `catch` at `:77-83`
   returns `Inconclusive` (2), not a bare `return`. The `Error: Invalid domain` path at `:46-47`
   returns `Failure` (3).
2. **macOS reference.** `macOS/Sources/SyxxCLI/Synthesis/FirstRunScan.swift:152-163`.
3. **Wrong today / required.**
   - Today: a scan whose `result.Conclusion.Determination.State == ThreatDetected` prints
     `🎓 Grade: 🔴 F …` and the process exits **2** (`Program.cs:25`'s seed is never overwritten
     — `FirstRunCommand.cs` assigns `Environment.ExitCode` exactly once, at `:36`, for an
     unknown option).
   - Required: `Clean → 0`, `ThreatDetected → 1`, `Inconclusive → 2`, `Untested → 2`,
     `Determination == null → 2`, scan threw → 2, bad domain / unknown option → 3.
4. **False-clean it removes.** Inverted rather than false-clean, and worse for automation: a
   confirmed threat currently reports as *"we could not measure"*. A cron job or CI gate written
   as `syxx firstrun || alert` never fires, and one written as `if [ $? -eq 2 ]` reports "network
   unmeasurable" while the grade on screen says F. The flagship command's machine-readable
   channel carries **no** information today: it is the constant 2.
5. **Failure-case test.** `Syxx.Tests/FirstRunExitContractTests.cs`:
   `ThreatDetectedExitsOne()` — construct a `FirstRunResult` whose `Conclusion.Determination.State`
   is `ThreatDetected` (the existing `FirstRunScanTests.cs` helpers already build these), pass it
   to the extracted `FirstRunCommand.ExitFor(result)`, assert `== 1`. Companion cases for
   `Clean → 0`, `Untested → 2`, `null → 2`. The test must be written against the *state*, not the
   grade, so a future change that grades an F but returns `Clean` still trips.
6. **Effort.** S. **Order.** Immediately after B0.
7. **Owner verification.** On the Linux box, with a resolver known to hijack NXDOMAIN (or with
   `SYXX_DIMENSION_URL` pointed at a stub that returns divergence):
   ```
   syxx firstrun --domain example.com; echo "exit=$?"
   ```
   - **pass** — printed grade is D/F with a threat headline **and** `exit=1`; or grade A/B with a
     clean headline **and** `exit=0`.
   - **fail** — any run where the printed conclusion says THREAT and `exit` is not 1, or says
     CLEAN and `exit` is not 0.
   - **inconclusive** — the scan prints `Disciplines skipped:` for the discipline that would have
     produced the threat (no network, resolver unreachable). `exit=2` is then *correct* and the
     run proves nothing; re-run on a network where the discipline can execute.

---

### B2 — Restore the four dropped THREAT signals

1. **What changes.** Four files, four mappings:
   - `Commands/KeyCommand.cs:11-68` → return `Threat` (1) when
     `report.CaptivePortal.State == ConditionState.Yes`, else `Completed` (0).
   - `Commands/FilterCommand.cs:13-67` → after all evidence is printed:
     `Enforcement.Inconclusive → 2`; `Inactive` or `Partial → 1`; `Active` with
     `DirectBypassOpen == Yes` **or** `doh.Status == Working → 1`; otherwise `0`. The
     no-resolver early return at `:20-24` → 2.
   - `Commands/ForensicsCommand.cs:22-46` and its five handlers → `--classify` returns 1 when any
     domain is flagged; `--beacon` returns 1 for beacon/regular/likely, 2 for
     inconclusive/unknown, 0 otherwise; `--exfil` returns 1 for likely/high/suspicious, 2 for
     inconclusive, 0 otherwise; missing/short arguments and the `default:` usage branch return 3.
   - `Commands/SentinelCommand.cs:19-124` → see B3, which subsumes the exit mapping.
2. **macOS reference.** `SyxxKey.swift:59-63`; `SyxxFilter.swift:76-88`;
   `SyxxForensics.swift:39-58` (classify), `:59-78` (beacon), `:79-103` (exfil);
   `SyxxSentinel.swift:100-133`.
3. **Wrong today / required.** Example, `filter`:
   - Today: `  [NOT FILTERING] Every readable probe resolved publicly. …` then
     `  [OPEN] A direct query to 8.8.8.8 over plain UDP:53 resolved a blocked domain …` and the
     process exits **2**.
   - Required: identical text, `exit=1`.
   Example, `key`: today `  ⚠️  DETECTED` + `exit=2`; required `  ⚠️  DETECTED` + `exit=1`.
4. **False-clean it removes.** Not clean-vs-threat but *threat-vs-unmeasured*, which in an
   unattended context is the same harm: the Pi-hole household that runs
   `syxx filter && echo ok || page-me` is never paged when the blocklist stops enforcing,
   because 2 is neither. And on the `key` path, `--exfil` / captive-portal detections that a
   support engineer collects by exit code all come back identical to "the probe did not run".
5. **Failure-case test.** `Syxx.Tests/ThreatExitParityTests.cs`, one theory per command with a
   table of (typed engine result → required exit). E.g.
   `FilterActiveWithOpenBypassIsThreat()` builds a `FilteringProbe.Result` with
   `Enforcement.Active` + `DirectBypassOpen = Yes` and asserts the mapper returns 1 — the case a
   naive "active means clean" refactor would break. `KeyCaptivePortalDetectedIsThreat()` asserts
   1 for `ConditionState.Yes` **and 0 for `ConditionState.Unknown`** (an unknown portal must not
   be reported as a threat either).
6. **Effort.** M. **Order.** After B0; `sentinel`'s half lands with B3.
7. **Owner verification.**
   ```
   syxx filter @127.0.0.1 ; echo "exit=$?"      # against a Pi-hole
   syxx forensics --classify doubleclick.net ; echo "exit=$?"
   syxx key ; echo "exit=$?"
   ```
   - **pass** — `filter` prints `[ACTIVE]` + `[COVERED]` twice ⇒ `exit=0`; prints
     `[NOT FILTERING]` or `[LEAKY]` or any `[OPEN]` ⇒ `exit=1`. `forensics --classify` on a
     known-blocked domain ⇒ `exit=1`; on `example.com` ⇒ `exit=0`. `key` on a clear network ⇒
     `exit=0`.
   - **fail** — any `exit=2` accompanying a line that begins `[NOT FILTERING]`, `[LEAKY]`,
     `[OPEN]`, `flagged by`, or `⚠️  DETECTED`.
   - **inconclusive** — `filter` prints `[INCONCLUSIVE] No probe produced a readable answer`
     (correctly `exit=2`), or `key` prints `❓ UNKNOWN` for the captive portal. Neither run
     exercises the threat path; retry on a network where the probes complete.

---

### B3 — `sentinel`: stop losing unattended detections

Five defects in one command; they must land together because they share the control flow.

1. **What changes.** `Windows/dotNET/Syxx/Commands/SentinelCommand.cs`.
   - **(a) Change log.** After the change block at `:117-121`, before the roll-forward at `:123`,
     append every `ConfigChange` to `~/.syxx/sentinel-changes.jsonl` — append-only, created
     `0600` via `Syxx.Kit.SyxxPaths.EnsurePrivateDir` + `RestrictToOwner`, never truncated by
     Syxx. New helper `SentinelCommand.AppendChangeLog(IReadOnlyList<ConfigChange>)`, or better,
     `Syxx.Kit.Sentinel.AppendChangeLog(...)` so the GUI shares it.
   - **(b) Save-error handling.** `Sentinel.SaveSnapshot` (`Syxx.Kit/Trust/Trust.cs:302-317`)
     **throws** — there is no `try`/`catch` in it. All four call sites are unguarded:
     `:56` (`HomeBaseline.Save`), `:89` (`--reset`), `:103` (first baseline), `:123`
     (roll-forward). Wrap each; on failure print the honest message and return 3.
   - **(c) Corrupt-baseline quarantine.** `:94-100` prints "run `syxx sentinel --reset`" and
     stops. Add `Syxx.Kit.Sentinel.QuarantineCorruptBaseline(path)` (rename to
     `sentinel-snapshot.corrupt.<utc>.json`, preserve the `IntegrityTag` sidecar), then write a
     fresh baseline and return 2.
   - **(d) Empty-resolver guard.** `:109-114`. When `changes.Count == 0` **and**
     `current.Resolvers.Count == 0`, the run measured nothing.
   - **(e) Exit mapping.** `--forget`/`--set-home`/`--reset` success → 0; `--home` absent
     baseline → 2, corrupt → 2, matches → 0, differs → 1; no prior baseline → 0; corrupt → 2;
     no change with resolvers → 0; no change without resolvers → 2; changes → 1; any persist
     failure → 3.
   - **(f) Tri-state display.** `:50` prints `current.VpnActive == ConditionState.Yes ? "yes" : "no"`
     — `VpnActive` is *already* `ConditionState` (`Trust.cs:215`), so `Unknown` prints as "no".
     `:51` prints the `DnsEncrypted` bool; that one needs B4 first.
2. **macOS reference.** `SyxxSentinel.swift:121` + `:139-158` (change log);
   `:29-39`, `:65-75`, `:80-87`, `:125-131` (guarded saves); `:88-98` (quarantine);
   `:102-111` (empty-resolver guard); `:24` and `:26` (tri-state display);
   `:100-133` (exit mapping).
3. **Wrong today / required.**
   - Today (change detected while nobody is watching):
     ```
     🚨 Configuration change detected — you should re-run tampering/leak checks:
       • Resolvers changed: 192.168.1.1 -> 45.90.28.0
     ```
     …then `Sentinel.SaveSnapshot(current)` at `:123` rolls the baseline forward. The **next**
     run prints `No change to the DNS configuration Syxx can read, since the last run.` and there
     is no surviving artifact of the hijack. Exit code both times: 2.
   - Required: identical stdout, **plus** a line appended to `~/.syxx/sentinel-changes.jsonl`
     (`{"timestamp":"…","kind":"Resolvers changed","detail":"192.168.1.1 -> 45.90.28.0"}`), and
     `exit=1`.
   - Today (unwritable `~/.syxx`): `--reset` throws out of `Main` — an unhandled
     `UnauthorizedAccessException` stack trace and a shell exit status outside {0,1,2,3}; the
     roll-forward path at `:123` throws *after* the user has already read a normal-looking
     report. Required: `⚠️ Could not update baseline: <reason>. Next run may show stale
     comparisons.` and `exit=3`.
   - Today (`VPN active : no` when the adapter list could not be enumerated). Required:
     `VPN active : unknown`.
4. **False-clean it removes.** Two, both severe. (i) The second run's
   `No change to the DNS configuration Syxx can read` after a real, printed, then-discarded
   hijack — the archetypal "nothing to see here" produced by a detection that was successfully
   made and then thrown away. (ii) `VPN active : no` and (after B4)
   `DNS encrypted : no` printed from an *unreadable* configuration: "no" is a definite negative
   claim about the user's protection, sourced from a measurement that did not happen.
5. **Failure-case test.** `Syxx.Tests/SentinelHonestyTests.cs`:
   - `ChangeIsLoggedBeforeBaselineRolls()` — point `Sentinel.DefaultPath` at a temp dir, write
     baseline A, capture snapshot B with different resolvers, run the command body, assert the
     JSONL file exists with mode `0600` and contains one entry, **and** that the on-disk baseline
     now equals B. Deleting the `AppendChangeLog` call must fail this test.
   - `SaveFailureIsReportedNotSwallowed()` — make the target directory read-only
     (`File.SetUnixFileMode(dir, UserRead | UserExecute)`), assert the command returns 3 and that
     no exception escapes.
   - `NoChangeWithNoResolversIsInconclusive()` — baseline and current both empty-resolver;
     assert exit 2 and that stdout does **not** contain the string `unchanged` or `✅`.
   - `UnknownVpnDoesNotPrintNo()` — snapshot with `VpnActive = ConditionState.Unknown`; assert
     rendered line contains `unknown` and does not match `: no$`.
6. **Effort.** M. **Order.** After B0 and B4 (B4 supplies the tri-state `DnsEncrypted` this
   command must render).
7. **Owner verification.**
   ```
   rm -f ~/.syxx/sentinel-changes.jsonl
   syxx sentinel --reset ; echo "exit=$?"
   # now change DNS: e.g. `resolvectl dns <iface> 1.1.1.1` or edit the router's DHCP DNS
   syxx sentinel ; echo "exit=$?"
   syxx sentinel ; echo "exit=$?"
   ls -l ~/.syxx/sentinel-changes.jsonl && cat ~/.syxx/sentinel-changes.jsonl
   chmod a-w ~/.syxx && syxx sentinel --reset ; echo "exit=$?" ; chmod u+w ~/.syxx
   ```
   - **pass** — first `--reset` `exit=0`; second run prints the 🚨 block with `exit=1`; third run
     prints the no-change line with `exit=0`; the JSONL exists at `-rw-------` with exactly one
     entry that survived the third run; the read-only `--reset` prints
     `⚠️ Could not reset baseline: …` with `exit=3` and **no stack trace**.
   - **fail** — the JSONL is missing or empty after the third run; the read-only run prints a
     .NET stack trace; any `exit=2` on the change-detected run.
   - **inconclusive** — the DNS change did not actually take effect (both runs print the
     no-change line, `exit=0`). Nothing about the change-log path was exercised; the run is not
     evidence that B3 works. Force a change with `--servers`-style manual edit and repeat.

---

### B4 — `DnsEncrypted` is a tri-state, not a bool

1. **What changes.**
   - `Syxx.Kit/Trust/Trust.cs:216` — `public bool DnsEncrypted` → `public ConditionState DnsEncrypted`.
   - `Trust.cs:233` — `DnsEncrypted = NetworkKeyState.IsDNSEncrypted()` →
     `DnsEncrypted = NetworkKeyState.EncryptedDNSStatus().State`.
   - `Trust.cs:284-286` — the diff detail becomes a `ConditionState` label
     (reuse/rename the existing `VpnLabel` at `:291-297` to `StateLabel`, yielding
     `encrypted` / `plaintext` / `unknown`).
   - `Syxx.Kit/NetworkKeyState.cs:699` — `IsDNSEncrypted()` keeps its signature (external
     callers rely on it) but gains an XML remark that it is lossy and must never be used to
     populate a stored snapshot or a rendered field; add `[Obsolete]`-style guidance or delete it
     if `Trust.cs:233` was the only caller (verify with a solution-wide search first).
   - `Syxx.Kit/Trust/ContinuousMonitor.cs:100` — `previous.DnsEncrypted && !current.DnsEncrypted`
     becomes the tri-state branch: confirmed loss (`Yes → No`) = Warning; `→ Unknown` = Info with
     neutral wording; otherwise Info.
   - Renderers: `Commands/SentinelCommand.cs:51` and `Program.cs:1083` print the state label.
   - Persisted-format note: `NetworkConfigSnapshot` is serialised to
     `~/.syxx/sentinel-snapshot.json` with an HMAC sidecar (`Trust.cs:310-316`). A `bool` → enum
     change makes existing baselines undeserialisable, i.e. `Corrupted`. That is *safe* under
     B3(c) (quarantine + fresh baseline) but must not land before it. Preferred: accept both
     shapes in a custom `JsonConverter` for one release, mapping legacy `true → Yes`,
     `false → No` — **not** `false → Unknown`, which would fabricate a state the old code never
     recorded.
2. **macOS reference.** `macOS/Sources/SyxxKit/Trust/Sentinel.swift:17` (`dnsEncrypted:
   ConditionState`), `:34` (capture from `encryptedDNSStatus().state`), `:73-78` (diff detail);
   `macOS/Sources/SyxxKit/Trust/ContinuousMonitor.swift:131-148`;
   `macOS/Sources/SyxxCLI/Commands/SyxxSentinel.swift:26`.
3. **Wrong today / required.**
   - Today, on a host whose resolver transport cannot be read (`EncryptedDNSStatus().State ==
     Unknown` — which `NetworkKeyState.cs:642-649` returns for *every* Linux host without
     `resolvectl`, and `:579-586` for every host using a loopback proxy such as `dnsmasq`,
     `stubby`, `systemd-resolved` at `127.0.0.53`, or Unbound-on-localhost):
     ```
       DNS encrypted      : no
     ```
   - Required:
     ```
       DNS encrypted      : unknown
     ```
   - Today, run-to-run: an actually-DoT-configured host whose transport Syxx cannot read is
     recorded as `false` on every run — so the diff is silent — but the moment `resolvectl`
     becomes readable and reports DoT, Sentinel prints
     `• Encrypted-DNS state changed: plaintext -> encrypted` for a network that never changed.
     The mirror-image case is worse: a host recorded `true` that becomes unreadable prints
     `encrypted -> plaintext` and `ContinuousMonitor` raises **Warning: "Encrypted DNS is no
     longer in use. Your lookups are now visible to the network."** — a definite claim about the
     user's exposure derived from a measurement that did not happen.
   - Required diff detail: `unknown -> encrypted` / `encrypted -> unknown`, and the monitor alert
     for the latter is Info: "Encrypted DNS state is now unknown. … This is not a confirmed loss
     of protection."
4. **False-clean it removes.** Strictly, a false-*dirty* on one side and a false-clean on the
   other, and the false-clean is the dangerous one: with `false` meaning both "plaintext" and
   "unknown", a transition **from** unknown **to** confirmed-plaintext produces *no diff at all*.
   A user whose DoH configuration is stripped by a network they just joined — while Syxx could
   not read the previous state — is told `No change to the DNS configuration Syxx can read`. The
   entire purpose of Sentinel is defeated for exactly the population most at risk (loopback-proxy
   and non-systemd Linux hosts).
5. **Failure-case test.** `Syxx.Tests/TriStateSnapshotTests.cs`:
   - `UnknownEncryptionIsNotRenderedAsNo()` — snapshot with `DnsEncrypted = Unknown`; assert the
     rendered line is `unknown`, and assert `!line.EndsWith("no")`.
   - `UnknownToNoIsADetectedChange()` — `Diff(prev: Unknown, curr: No)` returns exactly one
     `EncryptedDNSChanged` with detail `unknown -> plaintext`. **This is the test that trips if
     the bool comes back**: under a bool both sides are `false` and the diff is empty.
   - `YesToUnknownIsInfoNotWarning()` — `ContinuousMonitor.ToAlert` for `Yes → Unknown` has
     `Severity.Info` and its headline does not contain `no longer in use`.
   - `LegacyBooleanBaselineDeserialises()` — feed the converter a legacy
     `{"DnsEncrypted":true}` document, assert `Yes`; `false` → `No`; missing → `Unknown`.
6. **Effort.** M. **Order.** Before B3 (B3 renders it) and before B19.
7. **Owner verification.** On a Linux host using `systemd-resolved` at `127.0.0.53` (the default
   on Ubuntu — `EncryptedDNSStatusCore` short-circuits to Unknown at
   `NetworkKeyState.cs:578-586`):
   ```
   syxx sentinel | grep 'DNS encrypted'
   syxx key | grep -A1 'Encrypted DNS Status'
   ```
   - **pass** — `sentinel` prints `DNS encrypted      : unknown` and `key` prints
     `❓ UNKNOWN` with an `Evidence:` line naming the loopback proxy. The two commands agree.
   - **fail** — `sentinel` prints `no` while `key` prints `❓ UNKNOWN`. That disagreement between
     two views of the same call is the defect, and it is visible without any network setup.
   - **inconclusive** — the host is configured with a direct upstream resolver and `resolvectl`
     reports `DNSOverTLS: yes`, so both print encrypted/`🔒`. The Unknown path was not
     exercised; re-run on a loopback-proxy host or with `resolvectl` removed from `PATH`.

---

### B5 — `DhcpPushedDNS` must return "unknown", and must be implemented for Linux

1. **What changes.** `Syxx.Kit/NetworkKeyState.cs:794-814`.
   - `:796` — `if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(interfaceId)) return new
     List<string>();` This contradicts the method's own doc comment two lines above
     (`:792-793`: "returns null when the registry is unreadable (distinct from empty — 'no
     servers pushed' vs 'couldn't check')"). Split the two conditions: an empty `interfaceId` is
     still `null` (we could not identify the interface, so we could not check), and the
     non-Windows branch dispatches to a new Linux reader instead of returning empty.
   - **New** `DhcpPushedDNSLinux(string interfaceName)`: try, in order,
     `nmcli -g IP4.DNS device show <iface>`, then `resolvectl status <iface>` (the
     `DNS Servers:` line under a link whose `DHCP` origin is reported), then the dhclient lease
     files (`/var/lib/dhcp/dhclient.<iface>.leases`, `/var/lib/NetworkManager/*.lease`) parsing
     the last `option domain-name-servers`. Each source that is absent or unreadable falls
     through; if **all** fall through, return `null`.
   - `DetectGatewayAsync` (`:729-787`) currently takes `iface.Id` (`:743`). On Linux `.NET`'s
     `NetworkInterface.Id` is the interface *name*, so the same value serves both readers; make
     that explicit with a comment rather than relying on it silently.
   - `:784` — `DhcpPushedDNS = dhcpDns ?? new List<string>()` flattens the null back to empty in
     the structured field even when `Detail` says "could not be determined". Change
     `GatewayInfo.DhcpPushedDNS` (`NetworkKeyState.cs:101`) to `List<string>?` so structured
     consumers (Synthesis, GUI, JSON transcript) see the same tri-state the string does.
2. **macOS reference.** `macOS/Sources/SyxxKit/NetworkKeyState.swift:659-666`
   (`dhcpPushedDNS()` via `ipconfig getoption <iface> domain_name_server`), `:693`, `:729`.
   *Note the macOS twin has the same flaw in a milder form*: it returns `[]` for both "no
   options" and "`ipconfig` failed" (`:660`, `:661`) and omits the clause entirely at `:729`.
   Do **not** copy that. Linux gets the honest version; a follow-up in the macOS lane should
   bring Swift up to it.
3. **Wrong today / required.**
   - Today, `syxx key` on Linux, on a network whose router pushes `45.90.28.0` by DHCP:
     ```
     Gateway / Router:
       Gateway: 192.168.1.1; acts as DNS server; admin panel reachable on port 80
     ```
     — byte-identical to the output on a network that pushes nothing.
   - Required, when the list is readable:
     `…; DHCP pushed DNS: 45.90.28.0`
     and when no source could be read:
     `…; DHCP pushed DNS: could not be determined`
     (the `:774-777` clause already exists and is simply never reached on Linux).
4. **False-clean it removes.** The absence of the `DHCP pushed DNS:` clause is read as "the
   router pushed nothing unusual". Router-pushed DNS is *the* primary consumer-grade hijack
   vector — it is how a compromised CPE, a hostile hotel network, or a malicious DHCP server on
   the LAN redirects every device without touching any of them. On Linux, Syxx currently renders
   "we never looked" and "we looked and it was clean" as the same sentence, in the one command
   (`key`) a user runs to answer "what is my network doing to my DNS?".
5. **Failure-case test.** `Syxx.Tests/GatewayHonestyTests.cs`:
   - `NonWindowsWithNoReadableSourceReturnsNull()` — invoke `DhcpPushedDNS` with a
     command-runner seam whose `nmcli`/`resolvectl`/lease-file probes all fail; assert `null`,
     **not** an empty list. This is the exact assertion that trips if `:796`'s
     `return new List<string>()` is restored.
   - `NullDhcpRendersTheCouldNotDetermineClause()` — build a `GatewayInfo` detail from a null
     list; assert the string contains `could not be determined` and does **not** simply omit the
     clause.
   - `EmptyListRendersNoClause()` — the genuine "DHCP supplied no DNS option" case renders
     without the clause, so the two remain distinguishable.
   - `ParsesNmcliOutput()` / `ParsesDhclientLease()` — golden-vector parse tests so the Linux
     reader itself is pinned.
6. **Effort.** M (the Linux reader is the bulk). **Order.** Independent; schedule with B6 since
   both live in `DetectGatewayAsync`.
7. **Owner verification.** On the Linux box, on a normal DHCP LAN:
   ```
   syxx key | sed -n '/Gateway \/ Router/,+2p'
   nmcli -g IP4.DNS device show "$(ip route show default | awk '{print $5; exit}')"
   ```
   - **pass** — the `Gateway:` line ends with `; DHCP pushed DNS: <ips>` and those IPs match the
     `nmcli` output exactly.
   - **fail** — the line has no `DHCP pushed DNS:` clause at all while `nmcli` prints addresses.
   - **inconclusive** — `nmcli` is absent and the host has no dhclient leases (a statically
     configured or container host). The correct output is then
     `; DHCP pushed DNS: could not be determined`; seeing that is *not* proof the reader works,
     only that the honest fallback works. Re-run on a DHCP-configured host to exercise the
     reader.

---

### B6 — Gateway `AdminReachable` collapses Unknown into No *(sweep — new)*

1. **What changes.** `Syxx.Kit/NetworkKeyState.cs:757-767`.
   - `:762` — `adminReachable = (int)resp.StatusCode < 500 ? ConditionState.Yes : ConditionState.No;`
     A 5xx from an intermediate proxy is not evidence that the router has no admin panel.
     Required: `200-399 → Yes`, `400-499 → Yes` (it answered and refused — the panel exists),
     `>= 500 → Unknown`, non-HTTP response → `Unknown`.
   - `:764-767` — `catch (HttpRequestException or TaskCanceledException) { adminReachable =
     ConditionState.No; }`. A 3-second timeout is exactly what a filtering firewall, a dropped
     SYN, or a slow CPE produces. Required: `Unknown` for timeout / TLS / DNS / general network
     failure; reserve `No` for a definite connection **refusal** (`SocketError.ConnectionRefused`
     inside the `HttpRequestException`'s inner `SocketException`).
   - `:748-749` — the undeterminable-gateway detail is `"Could not determine default gateway"`;
     the returned `GatewayInfo` leaves `IsDNS` at its `bool` default `false`. macOS appends the
     disclaimer. Required: `"Could not determine default gateway — isDNS is unknown, not false"`.
     (`GatewayInfo.IsDNS` being a `bool` at `NetworkKeyState.cs:96` is itself a tri-state
     collapse; the string is the minimum fix, the typed fix is a stretch goal.)
2. **macOS reference.** `macOS/Sources/SyxxKit/NetworkKeyState.swift:696-724` (three-way status
   mapping, `.unknown` on catch, comments C108 at `:704-705` and `:718-719`), `:686-690`
   (undeterminable-gateway detail).
3. **Wrong today / required.**
   - Today, on a network where port 80 to the gateway is filtered (guest Wi-Fi, enterprise
     segmentation, or a CPE that only listens on 443):
     `Gateway: 192.168.1.1; acts as DNS server` — the `admin panel reachable` clause is silently
     absent, and the structured `AdminReachable` field is the definite value `No`.
   - Required: `AdminReachable = Unknown`, and the detail carries
     `; admin panel: could not be determined` so the omission is not read as a negative finding.
4. **False-clean it removes.** `AdminReachable == No` is a *reassuring* claim: it says the
   router's web interface is not exposed. It is consumed by the synthesis layer and by anyone
   reading `syxx key`. Today a **blocked probe** — the single most likely thing to happen on
   precisely the hostile networks Syxx is pointed at — produces that reassurance. Worse, the
   `catch` also swallows the case where the *gateway itself* is dropping our traffic, which is a
   finding, not an absence of one.
5. **Failure-case test.** `Syxx.Tests/GatewayHonestyTests.cs`:
   - `TimeoutYieldsUnknownNotNo()` — inject an `HttpMessageHandler` that throws
     `TaskCanceledException`; assert `ConditionState.Unknown`. Restoring the old `catch` fails it.
   - `ServerErrorYieldsUnknown()` — handler returns 502; assert `Unknown`.
   - `ConnectionRefusedYieldsNo()` — handler throws `HttpRequestException` wrapping a
     `SocketException(SocketError.ConnectionRefused)`; assert `No`. This is the case that keeps
     the fix from degenerating into "everything is Unknown".
   - `ForbiddenYieldsYes()` — handler returns 403; assert `Yes` (it answered).
6. **Effort.** S. **Order.** Independent; bundle with B5.
7. **Owner verification.**
   ```
   syxx key | sed -n '/Gateway \/ Router/,+2p'
   curl -s -o /dev/null -w '%{http_code}\n' --max-time 3 "http://$(ip route show default | awk '{print $3; exit}')/"
   ```
   - **pass** — `curl` prints a 2xx/3xx/4xx code **and** Syxx says `admin panel reachable on
     port 80`; or `curl` times out (prints `000`) **and** Syxx says
     `admin panel: could not be determined`.
   - **fail** — `curl` times out and Syxx omits the clause entirely (the current behaviour, which
     is indistinguishable from a clean negative).
   - **inconclusive** — the gateway answers instantly with 200 on every attempt. Only the `Yes`
     branch was exercised. Re-run with `iptables -A OUTPUT -d <gw> -p tcp --dport 80 -j DROP` to
     force the timeout path, then flush.

---

### B7 — `leak`: restore the headline, the real count, the 4-state signature result, and pinning

1. **What changes.** `Windows/dotNET/Syxx/Commands/LeakCommand.cs`. All the data already exists
   on `LeakTestResult` (`Syxx.Kit/LeakDetector.cs:201-240`: `SignatureValid` `:218`,
   `SignatureVerification` `:222`, `TransportPinning` `:230`, `SignatureKeyTrusted` `:239`) — this
   is purely a rendering and exit-mapping fix, no Kit change.
   - `:127-130` — add the headline.
   - `:131-134` — replace the hardcoded `"\n1 resolver observed — verify it matches your
     provider"` with the real count, and branch on zero.
   - `:135-138` — split the `default:` arm. `LeakResult.Invalid` and `LeakResult.Error` are
     currently both folded into one `INCONCLUSIVE` print with exit 2.
   - `:147-155` — after the receipt line, render the four signature states and the transport
     pinning line.
2. **macOS reference.** `SyxxLeak.swift:88` (headline), `:91-97` (count incl. the zero case),
   `:98-106` (four typed arms), `:114-135` (signature four-state + `Transport:` line).
3. **Wrong today / required.**
   - Today, leak **confirmed**:
     ```

     3 resolver(s) observed
     Observed resolvers (collector-attested):
       …
     ```
     Required:
     ```

     🚨 DNS LEAK CONFIRMED — 3 resolver(s) observed outside your expected path
       Detail: <errorReason, when present>
     ```
   - Today, **no leak**, zero resolvers observed: `1 resolver observed — verify it matches your
     provider` — a fabricated count. Required, for zero:
     `No resolver was observed by the collector — verify your network path`; for n≥1:
     `<n> resolver(s) observed — verify they match your provider`.
   - Today, receipt present:
     ```
     Collector receipt (Ed25519-signed; verifiable with public key — keep for disputes): <sig>
       Note: this is a chain-of-custody receipt, not a client-verifiable signature.
     ```
     Required, matching the four states already computed by the Kit:
     ```
     Collector receipt (Ed25519-signed attestation): <sig>
       [VALID] Ed25519 signature valid · signer trusted
       Transport: Enforced
     ```
     with the alternates `[WARNING] Ed25519 signature valid · signer NOT trusted`,
     `Ed25519 signature valid · signer trust not configured`,
     `[BLOCKED] Ed25519 signature INVALID — collector attestation cannot be verified`,
     `Signature not verified — no key supplied`, and `Transport: NotConfigured|Enforced|Failed`.
   - Exit mapping: `Confirmed → 1`, `NotDetected → 0`, `Inconclusive → 2`, `Invalid → 2`,
     `Error → 3` (today `Error` reaches the `default:` arm and exits 2).
4. **False-clean it removes.** Two.
   (i) `1 resolver observed` on a `NotDetected` result where `ObservedResolvers.Count == 0`
   asserts a measurement that did not occur — the user reads "one resolver saw my query, and it
   is presumably mine" when in fact **nothing** was observed and the no-leak conclusion rests on
   an absence of data.
   (ii) The current receipt block prints the same reassuring two lines whether the Ed25519
   signature verified, failed, or was never checked, and whether transport pinning was
   `Enforced` or `Failed`. A collector attestation whose signature is **invalid** — i.e. exactly
   the man-in-the-middle scenario the receipt exists to detect — is rendered as
   "Ed25519-signed … keep for disputes".
5. **Failure-case test.** `Syxx.Tests/LeakRenderingTests.cs` (extract the renderer into
   `LeakCommand.RenderCollectorResult(LeakTestResult) : (string text, int exit)` so it is pure):
   - `NotDetectedWithZeroObservedDoesNotClaimOne()` — assert the text does **not** contain
     `1 resolver` and does contain `No resolver was observed`. Re-introducing the hardcoded
     literal fails here.
   - `InvalidSignatureIsBlocked()` — `SignatureValid = false`; assert the text contains
     `[BLOCKED]` and does not contain `[VALID]`.
   - `ValidButUntrustedSignerIsWarning()` — `SignatureValid = true, SignatureKeyTrusted = false`;
     assert `[WARNING]` and **not** `[VALID]`.
   - `ConfirmedLeakExitsOneAndPrintsHeadline()` — assert exit 1 and `DNS LEAK CONFIRMED`.
   - `TransportPinningIsAlwaysPrintedWhenAReceiptExists()` — assert a `Transport:` line for each
     of the three enum values.
6. **Effort.** M. **Order.** After B0.
7. **Owner verification.** Requires a collector. Against the staging collector with a license:
   ```
   SYXX_LICENSE_KEY=... syxx leak --collector https://<staging>/ ; echo "exit=$?"
   ```
   - **pass** — a no-leak run prints a count that matches the number of lines under
     `Observed resolvers (collector-attested):` (or the zero-case sentence when that block is
     absent), prints exactly one `Transport:` line, and `exit=0`. A forced-leak run (query
     through a second uplink) prints `🚨 DNS LEAK CONFIRMED` with `exit=1`.
   - **fail** — the printed count and the enumerated resolver lines disagree; or a receipt is
     printed with no `Transport:` line; or `exit=2` on a run whose headline says CONFIRMED.
   - **inconclusive** — the collector is unreachable and the command prints
     `INCONCLUSIVE`/`EXECUTION ERROR`. Nothing about the rendering was exercised. Run the
     `LeakRenderingTests` unit tests instead and treat the live run as not performed.

---

### B8 — `lan`: honour the snapshot tri-state, stop asserting off-LAN, restore the caveat

1. **What changes.** `Windows/dotNET/Syxx/Commands/LanCommand.cs`.
   - `:60` — `if (snapshot.IsPartial)`. `IsPartial` is a compatibility shim
     (`Syxx.Kit/LAN/LanCartography.cs:453`: `Status != LANSnapshotStatus.Complete`) that folds
     `Partial` and `Unreadable` together. The real tri-state enum already exists at
     `LanCartography.cs:434-438`. Switch on `snapshot.Status`.
   - `:104-107` — the categorical `"…they are reached through the gateway (off-LAN)."`
   - After `:93` — add the unauthenticated-ARP caveat.
   - After the render — persist the snapshot (see B18) and return the mapped exit.
   - `:40-44` — the `catch` around `Snapshot()` returns without an exit code; make it 3.
2. **macOS reference.** `SyxxLan.swift:27-47` (status switch), `:64-66` (ARP caveat),
   `:73-79` (conditional off-LAN wording), `:81-87` (persistence), `:88-92` (exit mapping),
   `:15-18` (throw → 3).
3. **Wrong today / required.**
   - Today, ARP table entirely unreadable: `Syxx could not fully read the ARP table — this is not
     evidence that your LAN is empty. (…)` then `return` with exit 2 — the *same* prefix a
     genuinely partial read produces, so a reader cannot tell "we got nothing" from "we got some".
     Required, per macOS: unreadable →
     `The LAN map could not be built: <reason>.` / `This is not a measurement of your network — it
     is an incomplete result.` + exit 2; partial with devices →
     `⚠️ The LAN map is incomplete — this listing is not a complete picture of your network.` +
     `   Reason: <reason>` then the device table + exit 2; complete + empty →
     `No devices found in the ARP cache. …` + exit 0; complete with devices → table + exit 0.
   - Today, resolvers not in the cache:
     `🔎 Derived: none of your configured resolvers appear as on-link devices — they are reached
     through the gateway (off-LAN).`
     Required:
     ```
     🔎 Derived: none of your configured resolvers are currently in this host's ARP cache.
        That usually means they are off-LAN (reached through the gateway), but a resolver
        this host has not contacted recently will also be absent — this is not conclusive.
     ```
     (macOS says "this Mac"; the C# string must say "this host" — it ships on Windows too.)
   - Today, no caveat after the device table. Required, immediately after the closing separator:
     ```
     ⚠️  ARP bindings are unauthenticated — a device on this network can claim any address.
        Treat this map as a lead, not as proof of device identity.
     ```
4. **False-clean it removes.** The off-LAN assertion. A rogue DNS box on the segment that this
   host has simply not ARPed for in the last few minutes produces
   `they are reached through the gateway (off-LAN)` — an affirmative statement that the resolver
   is **not** a local device, which is precisely the finding `syxx lan` exists to surface
   (datapoint X1, per the comment at `LanCommand.cs:46-47`). The command answers its own core
   question with a guess phrased as a fact. Secondarily, the missing ARP caveat lets a user treat
   MAC-to-IP bindings as identity, which they are not.
5. **Failure-case test.** `Syxx.Tests/LanRenderingTests.cs` (extract
   `LanCommand.Render(LANSnapshot, ISet<string> resolverIPs) : (string, int)`):
   - `UnreadableAndPartialRenderDifferently()` — build one `LANSnapshot.Unreadable(...)` and one
     `LANSnapshot.Partial(devices, reason, 0)`; assert the two rendered strings are not equal and
     that only the partial one contains the device table. Collapsing back to `IsPartial` makes
     them equal and fails.
   - `OffLanIsNotAsserted()` — resolvers present, none in the device list; assert the text
     contains `not conclusive` and does **not** contain the substring `(off-LAN).` as a
     sentence-final assertion.
   - `ArpCaveatAlwaysPrintedWhenDevicesShown()` — assert `unauthenticated` appears whenever the
     device table does.
   - `PartialExitsTwo()` / `CompleteExitsZero()` / `UnreadableExitsTwo()`.
6. **Effort.** M. **Order.** After B0; B18 lands on top.
7. **Owner verification.**
   ```
   syxx lan ; echo "exit=$?"
   ip neigh flush all && syxx lan ; echo "exit=$?"
   ```
   - **pass** — the populated run lists devices, ends with the `⚠️  ARP bindings are
     unauthenticated` caveat, and `exit=0`. The flushed run prints
     `No devices found in the ARP cache.` with `exit=0` (an empty-but-readable cache is a
     complete measurement).
   - **fail** — either run prints `they are reached through the gateway (off-LAN).`; or the
     caveat is absent; or the flushed run reports the same message as a permission failure.
   - **inconclusive** — `ip neigh show` is unavailable and the fallback `arp -a` is also absent
     (minimal container), so every run is `Unreadable` with `exit=2`. Only the unreadable branch
     was exercised; install `iproute2` and repeat.

---

### B9 — `witness create` must read the signing key from a file, and must warn on argv

1. **What changes.** `Windows/dotNET/Syxx/Commands/WitnessCommand.cs:173-246`.
   - `:179-200` — the argument loop accepts only `--key`/`-k` (`:182`) and `--output`/`-o`
     (`:190`). Add `--key-file <path>` and `--insecure-key-arg`.
   - New, before the env fallback at `:202`: when `--key-file` is given, stat the file, warn if
     `mode & 0o077 != 0`, read and trim it; on any read failure print
     `[ERROR] Could not read key file <path>: <reason>` and return 3. `--key-file` takes
     precedence over `--key`.
   - New: when `--key` was supplied on argv **and** `--key-file` was not **and**
     `--insecure-key-arg` was not, print the two-line warning (the command still proceeds — the
     macOS behaviour — so this is not a breaking change for existing scripts).
   - `:203-209` — the "key required" error message must name `--key-file` first.
   - `:654-668` — update the usage block to lead with `--key-file`, matching macOS.
2. **macOS reference.** `SyxxWitness.swift:126-131` (flag parsing incl. `--insecure-key-arg`),
   `:148-160` (permission check + file read + error path), `:162-165` (argv warning),
   `:166-171` (env fallback + required error), `:480-496` (usage text).
3. **Wrong today / required.**
   - Today, the only two ways to supply the key on Linux are `--key <b64>` (argv) and
     `SYXX_WITNESS_KEY` (environment). Both are readable by any local process:
     `/proc/<pid>/cmdline` is world-readable by default on stock Ubuntu (no `hidepid`), and so is
     `/proc/<pid>/environ` to the same UID. `syxx witness --gen-keypair` writes a `0600` key file
     at `~/.syxx/witness.key` (`:101-131`) that **`witness create` then has no way to read**.
   - Required: `syxx witness create example.com --key-file ~/.syxx/witness.key` works; passing
     `--key` without `--insecure-key-arg` prints
     ```
     [WARNING] Passing the private key on argv exposes it via `ps -ww`, shell history, and CI logs.
       Use --key-file <path> or set SYXX_WITNESS_KEY instead. To suppress this warning, pass --insecure-key-arg.
     ```
     and a key file with loose permissions prints
     `[WARNING] Key file <path> has permissions 644 — should be 0600 (owner-only).`
4. **False-clean it removes.** Not an output false-clean — an *evidentiary* one, which for this
   command is the same failure. A Witness snapshot's whole value is that only the holder of the
   private key could have produced it. On Linux today the key is exposed to every local process
   for the lifetime of the signing command, so any snapshot's `[VALID] Tamper-evident` line is a
   claim the platform cannot support. The tool ships a secure key store and then offers no route
   from it to the signing operation.
5. **Failure-case test.** `Syxx.Tests/WitnessKeyInputTests.cs`:
   - `KeyFileIsAccepted()` — write a `0600` temp file containing a valid base64 key; assert the
     parser returns that key and no error.
   - `KeyFileTakesPrecedenceOverArgv()` — supply both; assert the file's key is used. A future
     refactor that lets argv win fails here.
   - `LooseKeyFilePermissionsWarn()` — chmod `0644`; assert the warning text contains
     `should be 0600`.
   - `ArgvKeyWithoutOptInWarns()` — assert the two-line warning is emitted; and
     `ArgvKeyWithOptInIsSilent()`.
   - `UnreadableKeyFileReturnsThreeAndDoesNotSign()` — point at a nonexistent path; assert exit 3
     **and** that no output file was written. The "and does not sign" half is what trips if a
     future change makes the read failure non-fatal.
6. **Effort.** M. **Order.** Independent. Land with B10 (same command, same file).
7. **Owner verification.**
   ```
   syxx witness --gen-keypair --key-file /tmp/w.key
   ls -l /tmp/w.key
   syxx witness create example.com --key-file /tmp/w.key --output /tmp/w.json ; echo "exit=$?"
   syxx witness verify /tmp/w.json --detailed ; echo "exit=$?"
   syxx witness create example.com --key "$(cat /tmp/w.key)" --output /tmp/w2.json 2>&1 | head -3
   ```
   - **pass** — the key file is `-rw-------`; `create --key-file` prints `[SUCCESS] Witness
     snapshot successfully created and signed!` with `exit=0`; `verify --detailed` prints
     `[VALID]` with `exit=0`; the argv form prints the `[WARNING] Passing the private key on
     argv…` block before proceeding.
   - **fail** — `create --key-file` prints `[ERROR] --key/-k is required` (the flag was never
     added); or the argv form prints no warning.
   - **inconclusive** — `create` prints `INCONCLUSIVE: No system DNS resolvers were detected.`
     (`:216`) because the host has no readable resolvers. The key-input path was exercised but
     the signing path was not; fix resolver discovery (or run on a normal host) and repeat before
     recording the task done.

---

### B10 — `witness --gen-keypair` must not destroy an existing signing identity *(sweep — new)*

1. **What changes.** `Windows/dotNET/Syxx/Commands/WitnessCommand.cs:85-160`. Between resolving
   `keyFile` (`:101-102`) and generating the pair (`:104`), add: if `File.Exists(keyFile)` and
   `--force` was not passed, print the three `[ERROR]` lines and return 3. Add `--force` to the
   argument scan at `:92-100` and to the usage block at `:666-667`. The write itself
   (`:118-131`) uses `FileMode.Create`, which truncates unconditionally.
2. **macOS reference.** `SyxxWitness.swift:69-77` (the B249 guard) and `:71` (`--force`).
3. **Wrong today / required.**
   - Today: `syxx witness --gen-keypair` run a second time silently replaces
     `~/.syxx/witness.key` and prints
     `Private key written to: /home/u/.syxx/witness.key  (owner-only permissions — keep it secret)`
     followed by a **different** public key. Exit 0.
   - Required:
     ```
     [ERROR] Private key already exists at: /home/u/.syxx/witness.key
     [ERROR] Re-running --gen-keypair would destroy your current signing identity.
     [ERROR] Use --force to overwrite, or --key-file <path> to write to a different location.
     ```
     with exit 3, and the file untouched.
4. **False-clean it removes.** `Private key written to: … (owner-only permissions — keep it
   secret)` + exit 0 is an unqualified success message printed at the moment the user's entire
   evidentiary history is destroyed. Every previously signed snapshot becomes unverifiable
   against the key the tool will now hand out, and any trust anchor a third party recorded is
   silently orphaned. The user is told the operation succeeded, and it did — it just also
   irreversibly destroyed the thing the command exists to protect. On macOS this is guarded; on
   Linux it is not.
5. **Failure-case test.** `Syxx.Tests/WitnessKeypairGuardTests.cs`:
   - `SecondGenKeypairRefusesAndPreservesFile()` — run the generator twice into a temp path;
     assert the second returns 3, that the file's bytes are byte-identical to after the first
     run, and that stdout contains `would destroy your current signing identity`. This is the
     precise assertion that trips if the guard is removed or if `--force` becomes the default.
   - `ForceOverwritesAndStillWrites0600()` — with `--force`, assert the bytes changed **and**
     the mode is still `0600` (a naive `File.WriteAllBytes` under `--force` would re-create at
     the umask default).
6. **Effort.** S. **Order.** Independent; land with B9.
7. **Owner verification.**
   ```
   syxx witness --gen-keypair --key-file /tmp/g.key | tee /tmp/g1.txt
   sha256sum /tmp/g.key
   syxx witness --gen-keypair --key-file /tmp/g.key ; echo "exit=$?"
   sha256sum /tmp/g.key
   syxx witness --gen-keypair --key-file /tmp/g.key --force ; echo "exit=$?"
   sha256sum /tmp/g.key ; ls -l /tmp/g.key
   ```
   - **pass** — the second invocation prints the refusal, `exit=3`, and the two `sha256sum`
     values are identical. The `--force` invocation `exit=0`, the digest changes, and the mode is
     still `-rw-------`.
   - **fail** — the digest changes between the first and second `sha256sum` (the key was
     destroyed), or the second run exits 0.
   - **inconclusive** — `--gen-keypair` fails on the first run (e.g. `/tmp` mounted `noexec`/full
     and the write errors). Nothing was tested; use a writable path and repeat.

---

### B11 — `forensics --analyze`: distinguish I/O error from "not an endpoint log", and expand `~`

1. **What changes.**
   - `Syxx.Kit/Forensics/EndpointLogAnalyzer.cs:65-70` — `Analyze` returns `string?` where
     `null` means *both* "not an endpoint log" and "the file could not be opened", because
     `Detect` (`:72-96`) swallows every exception at `:94` (`catch { }`) and returns `null`.
     Introduce `EndpointLogAnalysisResult` mirroring the Swift enum: `Success(string)`,
     `Error(string)`, `NotApplicable`. Check existence and readability before `Detect`, and let
     `Detect` surface its own I/O exception as `Error`.
   - `Windows/dotNET/Syxx/Commands/ForensicsCommand.cs:97-119` — `:104` takes `parts[0]` with no
     tilde expansion; add it (`path.StartsWith("~/") → Path.Combine(userProfile, path[2..])`).
     `:106-107` switches on the new result. `:110-111` read failure → 3. `:113-117` zero records
     → 2. Success → 0.
   - **Shared hazard, note only:** when `Detect` returns "not an endpoint log", both platforms
     then read the whole file into memory (`ForensicsCommand.cs:110`,
     `SyxxForensics.swift:121`). On a multi-GB endpoint log whose first 4000 lines contain no
     marker, that is an OOM on both. Not a parity gap; raise separately.
2. **macOS reference.** `macOS/Sources/SyxxKit/Forensics/EndpointLogAnalyzer.swift:11-15` (the
   enum), `:24-38` (existence/openability checks before `detect`);
   `macOS/Sources/SyxxCLI/Commands/SyxxForensics.swift:109` (`expandingTildeInPath`),
   `:112-131` (the three-arm switch and its exit codes).
3. **Wrong today / required.**
   - Today, `syxx forensics --analyze ~/es.json` (unquoted `~` already expanded by the shell
     works; the failing case is a quoted or programmatically-passed `"~/es.json"`):
     `Could not read file: Could not find file '~/es.json'.` and exit 2.
     Required: the path is expanded and the file is analysed.
   - Today, an endpoint log that exists but cannot be read past the first chunk (EACCES on a
     bind-mounted log, an I/O error on a failing disk): `Detect` catches, returns `null`,
     `File.ReadAllText` may then succeed on the buffered prefix, `CaptureLogParser.Parse` returns
     zero records, and the user reads
     `No records parsed from /var/log/es.json. (Expected a capture query log or endpoint activity
     log.)` with exit 2.
     Required: `Endpoint log error: <reason>` with exit 3.
4. **False-clean it removes.** "No records parsed" is read as "I looked at your endpoint log and
   found nothing" — an absence-of-findings statement. It is emitted today for a file that was
   never successfully read. In a forensics command, whose entire output is a claim about what was
   and was not observed, reporting an unreadable input as an empty result is the defect class
   this product refuses.
5. **Failure-case test.** `Syxx.Tests/ForensicsAnalyzerTests.cs` (extend the existing file):
   - `UnreadableFileIsErrorNotNotApplicable()` — create a file, `chmod 000`, call
     `EndpointLogAnalyzer.Analyze`; assert `Error`, not `NotApplicable`. Restoring the bare
     `catch { }` in `Detect` fails it. (Skip when running as root — assert the skip is explicit,
     not silent.)
   - `MissingFileIsError()` — assert `Error` with a message containing the path.
   - `PlainTextFileIsNotApplicable()` — a readable non-endpoint file must still be
     `NotApplicable`, so the fix does not turn every capture log into an error.
   - `TildePathIsExpanded()` — assert the command resolves `~/x` against the user profile.
6. **Effort.** S. **Order.** After B0.
7. **Owner verification.**
   ```
   printf '{"event":"ES_EVENT_TYPE_NOTIFY_EXEC"}\n' > /tmp/es.json
   syxx forensics --analyze /tmp/es.json ; echo "exit=$?"
   chmod 000 /tmp/es.json && syxx forensics --analyze /tmp/es.json ; echo "exit=$?" ; chmod 644 /tmp/es.json
   cp /tmp/es.json ~/es.json && syxx forensics --analyze '~/es.json' ; echo "exit=$?"
   ```
   - **pass** — the readable run prints a report with `exit=0`; the `chmod 000` run prints
     `Endpoint log error:` with `exit=3`; the tilde run prints the same report as the first,
     `exit=0`.
   - **fail** — the `chmod 000` run prints `No records parsed from …` or
     `Could not read file:` with `exit=2`; or the tilde run reports a missing file.
   - **inconclusive** — running as root, so `chmod 000` does not block the read and the second
     run succeeds. That branch is untested; re-run as an unprivileged user.

---

### B12 — `firstrun` must thread `isPro`

1. **What changes.** `Windows/dotNET/Syxx/Commands/FirstRunCommand.cs:75` —
   `FirstRunScan.RunAsync(domain: domain, progress: progress)` omits `isPro`, whose default is
   `false` (`Syxx.Kit/Synthesis/FirstRunScan.cs:51-53`). Add a
   `await LicenseManager.IsFeatureUnlockedAsync()` before the call and pass
   `isPro: lic.Unlocked`. Same defect in the guided path: `Program.cs:357` calls
   `FirstRunScan.RunAsync(progress: progress)` even though `Program.cs:189-213` has already
   computed `isPro` — pass it.
2. **macOS reference.** `macOS/Sources/SyxxCLI/Synthesis/FirstRunScan.swift:82-86`.
3. **Wrong today / required.**
   - Today, a licensed Linux user runs `syxx firstrun`. `ProbePlan.RunMirrorAsync(..., isPro:
     false)` (`FirstRunScan.cs:95`) skips the Dimension-server corroboration, so the Mirror
     discipline contributes weaker evidence; the grade line reads e.g.
     `Grade: 🟡 C` where the same network on macOS reads `Grade: 🟢 B`, and
     `Disciplines skipped:` omits any mention that a paid capability was not used.
   - Required: identical inputs produce identical grades on both platforms for the same license
     state, and a Pro user's run performs the Pro corroboration.
4. **False-clean it removes.** Inverted — it produces a *worse* grade than the evidence supports,
   which is the safe direction but still a correctness defect and a paid-feature failure. The
   honesty issue is the silence: nothing in the output tells the user that a discipline ran in
   reduced form. If the fix cannot be made immediately, the interim requirement is a line in
   `Disciplines skipped:` naming the Pro corroboration.
5. **Failure-case test.** `Syxx.Tests/FirstRunProTests.cs`:
   `ProFlagReachesProbePlan()` — inject a fake `ILicenseGate` (or stub
   `LicenseManager.SetSecretStore` + a canned validator) reporting unlocked, run the command
   body against a `ProbePlan` seam that records its `isPro` argument, assert `true`. Companion
   `LiteFlagReachesProbePlan()` asserts `false`. A regression that drops the argument silently
   re-defaults to `false` and fails the first test.
6. **Effort.** S. **Order.** Independent; land alongside B1 (same file).
7. **Owner verification.** With a valid Pro key activated on the Linux box:
   ```
   syxx firstrun --domain example.com 2>&1 | tee /tmp/pro.txt
   grep -E 'Dimension|ground truth|Disciplines skipped' /tmp/pro.txt
   ```
   - **pass** — the transcript shows the Pro corroboration step (the Dimension ground-truth line)
     or, on a clean network where corroboration is not triggered, `Disciplines skipped:` does not
     name Mirror.
   - **fail** — the run is byte-identical to an unlicensed run.
   - **inconclusive** — the license service is unreachable, so `IsFeatureUnlockedAsync` returns
     locked and the run is legitimately Lite. The plumbing was not exercised; re-run with
     connectivity, or rely on the unit test.

---

### B13 — `-v` must not mean `--version` under a subcommand

1. **What changes.** `Syxx.Kit/SyxxConfig.cs:33` —
   `if (flags.Contains("--version") || flags.Contains("-V") || flags.Contains("-v"))`. Drop
   `"-v"` from this test. `Program.cs:94-99` (the **top-level** dispatcher) may keep `-v` as a
   version alias if the owner wants `syxx -v` to work — macOS deliberately does not
   (`SyxxCLI.swift:92-93`, comment B240) — but `HandleGlobalFlags`, which every subcommand calls
   first, must not. Add `SyxxConfig.IsVerbose(string[])` returning
   `args.Contains("--verbose") || args.Contains("-v")` as the twin of
   `SyxxConfig.swift:66-68`, so `-v` has a destination rather than becoming an unknown flag.
2. **macOS reference.** `macOS/Sources/SyxxKit/SyxxConfig.swift:50-52` (only `-V`),
   `:66-68` (`isVerbose`); `macOS/Sources/SyxxCLI/SyxxCLI.swift:91-93` (the reservation comment).
3. **Wrong today / required.**
   - Today: `syxx leak -v` prints `syxx leak 2.0.0` and returns immediately — no leak test runs,
     and the exit code is the seeded 2. A user who typed `-v` expecting verbosity (the meaning it
     has on the macOS build, and the near-universal Unix convention) reads a version banner and a
     "could not measure" exit code, and may well conclude the check ran and found nothing.
   - Required: `syxx leak -v` runs the leak test. `syxx leak -V` and `syxx leak --version` print
     the banner.
4. **False-clean it removes.** A command that never executed, exiting with the same code as a
   command that executed and could not conclude. In a scripted context
   (`syxx filter -v; case $? in ...`) the two are indistinguishable.
5. **Failure-case test.** `Syxx.Tests/GlobalFlagTests.cs`:
   - `DashVIsNotVersion()` — `SyxxConfig.HandleGlobalFlags(new[]{"-v"}, "syxx leak")` returns
     `false`. Re-adding `-v` to the version test fails it.
   - `DashCapitalVIsVersion()` and `LongVersionIsVersion()` return `true`.
   - `IsVerboseRecognisesBothForms()`.
   - `TopLevelDashVStillPrintsVersion()` — pins whichever behaviour the owner chooses for
     `Program.cs`, so the two layers cannot silently drift apart again.
6. **Effort.** S. **Order.** Independent.
7. **Owner verification.**
   ```
   syxx leak -v ; echo "exit=$?"
   syxx leak -V ; echo "exit=$?"
   ```
   - **pass** — `-v` produces leak-test output (`Generated leak test subdomain: …`) and a
     conclusion-bearing exit code; `-V` produces `syxx leak 2.0.0` and `exit=0`.
   - **fail** — `-v` produces `syxx leak 2.0.0`.
   - **inconclusive** — `-v` produces `INCONCLUSIVE: No system DNS resolvers were detected.`
     That *is* the leak command running, so the flag fix is confirmed; the leak test itself was
     not exercised, which is a separate matter.

---

### B14 — `unbound`: the missing subcommand, the missing gate line, and the Windows wording

1. **What changes.** `Windows/dotNET/Syxx/Commands/UnboundCommand.cs`.
   - **(a)** `:31-43` `RunStatus` — append the gate line. C# has no `UnboundEntitlementSeam`;
     add `Syxx.Kit/UnboundResolver.cs :: UnboundEntitlementSeam.DeferredReason` as the twin of
     the Swift enum, with the *platform's own* reason (on Linux/Windows the gate is not the Apple
     NetworkExtension entitlement — the C# string must state the real reason for this platform,
     not copy the macOS sentence).
   - **(b)** `:89` — `"  ADMIN REQUIRED — {r.Message}. Re-run from an elevated prompt."` The
     same file already platform-switches this wording correctly at `:62`. Use the same switch.
   - **(c)** `:23` / `:18` — no `verify-dnscrypt`. macOS has the whole
     Unbound → dnscrypt-proxy → Anonymized-DNSCrypt chain (`SyxxUnbound.swift:39-73`), backed by
     `macOS/Sources/SyxxKit/DNSCryptProxyConfig.swift` and `DNSCryptProxyManager.swift`. Neither
     file has a C# counterpart. Full parity requires porting both plus
     `UnboundManager.VerifyDNSCryptChain`. This is the single largest parity gap in the lane.
   - **(d)** `:27` — the unknown-action branch prints and falls through with no exit code.
   - **(e)** `:53-55` — `RESULT: PASS/FAIL` → `CONCLUSION: PASS/FAIL` (see B15).
   - Exit mapping: `verify` pass → 0, fail → 3; `status` → 0; `enable`/`disable` per
     `PrintResult` (Enabled/Disabled → 0, NotInstalled/ElevationRequired/Failed → 3,
     Inconclusive → 2); unknown action → 3.
2. **macOS reference.** `SyxxUnbound.swift:89` (gate line), `:141` (sudo wording),
   `:14-15` (usage listing `verify-dnscrypt`), `:23`+`:39-73` (the subcommand),
   `:101`/`:104` (CONCLUSION), `:31-34` (unknown action → 3), `:124-153` (result → exit).
3. **Wrong today / required.**
   - Today, `syxx unbound status` on Linux ends at
     `────────────────────────────────────────────────────` with no statement that the always-on
     layer is not built. A user reads a complete-looking status panel and concludes the feature
     set shown is the feature set that exists.
     Required, as the last line before the blank:
     `  Gated (not built): <platform-specific reason>`
   - Today, `syxx unbound enable` without root on Linux:
     `  ADMIN REQUIRED — binding the local resolver to port 53 requires administrator rights.
     Re-run from an elevated prompt.` — advice that does not exist on Linux, in a binary that
     already knows better six lines earlier.
     Required: `… Re-run with sudo.`
   - Today, `syxx unbound verify-dnscrypt`:
     `Unknown 'unbound' action 'verify-dnscrypt'. Use: verify | status | enable | disable` and
     exit 2. Required: the chain verification runs, or (as a staged deliverable) the command is
     recognised and returns 2 with an explicit
     `NOT IMPLEMENTED on this platform — the DNSCrypt forward chain is not yet available here.`
     Silence is not acceptable; "unknown action" tells the user the feature does not exist when
     the product ships it on another platform.
4. **False-clean it removes.** (i) The status panel that presents itself as complete while a
   named layer is unbuilt. (ii) `verify` printing `RESULT: PASS — the local resolver recurses and
   validates DNSSEC.` with exit 2 — a positive security assertion whose machine-readable
   companion says "unmeasured".
5. **Failure-case test.** `Syxx.Tests/UnboundCliTests.cs`:
   - `StatusAlwaysPrintsTheGateLine()` — assert `Gated (not built):` appears in `RunStatus`
     output regardless of whether the binary is installed. Deleting the line fails it.
   - `ElevationWordingIsPlatformCorrect()` — assert the rendered `ElevationRequired` string
     contains `sudo` when `OperatingSystem.IsWindows()` is false, and `elevated` when true (drive
     via an injected platform predicate so it is testable on either host).
   - `UnknownActionExitsThree()`.
   - `VerifyFailExitsThree()` / `VerifyPassExitsZero()`.
   - `VerifyDnscryptIsRecognised()` — asserts the dispatcher does **not** report it as an unknown
     action. This test is the parity ratchet: it fails today and stays failing until (c) lands,
     so the gap cannot be quietly forgotten.
6. **Effort.** L (driven entirely by (c); (a),(b),(d),(e) together are S). **Order.** After B0.
   Split: land (a),(b),(d),(e) immediately; schedule (c) as its own deliverable.
7. **Owner verification.**
   ```
   syxx unbound status ; echo "exit=$?"
   syxx unbound enable ; echo "exit=$?"        # as a non-root user
   syxx unbound bogus  ; echo "exit=$?"
   syxx unbound verify-dnscrypt ; echo "exit=$?"
   ```
   - **pass** — `status` ends with a `Gated (not built):` line and `exit=0`; `enable` says
     `Re-run with sudo.` with `exit=3`; `bogus` prints the usage line with `exit=3`;
     `verify-dnscrypt` either runs the chain (`exit` 0 or 3 with a `CONCLUSION:` line) or prints
     the explicit not-implemented message with `exit=2`.
   - **fail** — any occurrence of `elevated prompt` on Linux; `status` with no gate line;
     `verify-dnscrypt` reported as an unknown action.
   - **inconclusive** — `unbound` is not installed, so `enable` returns `NOT INSTALLED` and the
     elevation branch is unreachable. `apt install unbound` first; until then (b) is untested.

---

### B15 — Reconcile `result:` / `RESULT:` with `conclusion:` / `CONCLUSION:`

1. **What changes.** Per the house copy rule (`result` in code, `conclusion` in module names, UI,
   and docs), the *user-facing* strings become "conclusion":
   - `Commands/ForensicsCommand.cs:90` — `$"  result: {a.Suspicion.Label()}"` →
     `$"  conclusion: {a.Suspicion.Label()}"`.
   - `Commands/UnboundCommand.cs:53-55` — `"  RESULT: PASS …"` / `"  RESULT: FAIL …"` →
     `"  CONCLUSION: PASS …"` / `"  CONCLUSION: FAIL …"`.
   - `Commands/FirstRunCommand.cs:87` — the section header `  RESULTS` → `  CONCLUSIONS` (see
     B21).
   - Identifiers (`LeakResult`, `SnapshotVerification`, `ExitStatus`, `result` locals) are **not**
     renamed — the rule keeps `result` in code.
2. **macOS reference.** `SyxxForensics.swift:90` (`conclusion:`), `SyxxUnbound.swift:101`/`:104`
   (`CONCLUSION: PASS`/`FAIL`), `FirstRunScan.swift:91` (`CONCLUSIONS`).
3. **Wrong today / required.** Exactly as above: `  result: Likely tunneling` →
   `  conclusion: Likely tunneling`; `  RESULT: PASS — the local resolver recurses and validates
   DNSSEC.` → `  CONCLUSION: PASS — …`.
4. **False-clean it removes.** None directly. It removes a *documentation* hazard: support
   scripts, the parity transcript corpus, and the docs all key on these labels, and a
   platform-dependent label means a Linux transcript silently fails a macOS-authored check —
   which then gets "fixed" by loosening the check, which is how honesty assertions rot.
5. **Failure-case test.** Extend the existing parity-transcript corpus
   (`Syxx.Tests/MatrixGoldenCorpusTests.cs` pattern) with
   `Syxx.Tests/LabelParityTests.cs :: UserFacingLabelsMatchMacOS()` — a table of
   (C# emitter, expected literal) pairs sourced from the Swift files, asserted against the
   rendered output. Adding a new user-facing `RESULT:`/`result:` label anywhere in
   `Commands/` fails a companion source-scan assertion.
6. **Effort.** S. **Order.** Independent, but land *after* B14(e) and B21 so the three edits do
   not collide.
7. **Owner verification.**
   ```
   syxx forensics --exfil aGVsbG8gd29ybGQ.tunnel.evil.net --times 1000,1060,1120 | grep -i 'conclusion\|result'
   syxx unbound verify | grep -i 'conclusion\|result'
   ```
   - **pass** — both print `conclusion:` / `CONCLUSION:` and neither prints `result:` / `RESULT:`.
   - **fail** — either still prints the `result` form.
   - **inconclusive** — `unbound verify` cannot run (binary absent) so no label is emitted; the
     forensics half still stands on its own.

---

### B16 — `key` must say *why* no resolvers were detected

1. **What changes.** `Windows/dotNET/Syxx/Commands/KeyCommand.cs:23` —
   `Console.WriteLine("  None detected");`. The reason must name the sources that were consulted,
   which on this binary are platform-dependent: `resolvectl` / `/etc/resolv.conf` on Linux
   (`NetworkKeyState.cs:150-152`, `:214-221`), the adapter enumeration on Windows
   (`:156-211`). Emit the platform-appropriate parenthetical, and — important — distinguish
   *"enumeration failed"* from *"enumeration succeeded and found none"*:
   `NetworkKeyState.SafeGetInterfaces()` (`:361-371`) already knows the difference and
   `GetActiveInterfaces` (`:377-379`) already discards it. Surface it.
2. **macOS reference.** `SyxxKey.swift:16` —
   `"  None detected (scutil/resolv.conf returned no usable nameservers)"`.
3. **Wrong today / required.**
   - Today: `  None detected`
   - Required (Linux, sources readable but empty):
     `  None detected (resolvectl/resolv.conf returned no usable nameservers)`
   - Required (Linux, adapter enumeration failed):
     `  Could not be determined (network adapter enumeration failed — this is not evidence that
     you have no resolvers)`
4. **False-clean it removes.** `None detected` reads as a finding about the machine ("you have no
   DNS servers configured"). It is emitted equally when Syxx could not read the configuration at
   all. Every downstream command keys off this list — `leak`, `filter`, `mirror`, `witness
   create` all refuse to run with "No system DNS resolvers were detected" — so a *read failure*
   cascades into a fleet of refusals that all describe the network rather than the tool.
5. **Failure-case test.** `Syxx.Tests/KeyRenderingTests.cs`:
   - `EmptyButEnumeratedSaysReturnedNone()` — assert the string contains
     `returned no usable nameservers`.
   - `EnumerationFailureDoesNotSayNoneDetected()` — with the enumeration seam throwing, assert
     the rendered line does **not** contain `None detected` and does contain
     `not evidence`. This is the assertion that trips if the two states are re-merged.
6. **Effort.** S. **Order.** Independent.
7. **Owner verification.**
   ```
   syxx key | sed -n '/System DNS Resolvers/,+2p'
   sudo mv /etc/resolv.conf /etc/resolv.conf.bak ; syxx key | sed -n '/System DNS Resolvers/,+2p' ; sudo mv /etc/resolv.conf.bak /etc/resolv.conf
   ```
   - **pass** — the normal run lists resolvers; the second run prints a `None detected (…)` line
     naming the sources consulted.
   - **fail** — the second run prints a bare `  None detected`.
   - **inconclusive** — `resolvectl` is running and still answers after `resolv.conf` is moved,
     so resolvers are still listed. The empty branch was not reached; stop `systemd-resolved`
     first, or rely on the unit tests.

---

### B17 — Exit codes for the remaining commands

1. **What changes.** `nslook`, `route`, `speed` (`cage` is out of scope; `filter`, `forensics`,
   `key`, `sentinel` are covered by B2/B3; `lan` by B8; `unbound` by B14).
   - `Commands/NslookCommand.cs` — `:29-30` usage → 3; `:91-92` unsupported record type → 3;
     `:112-122` records or no records → 0 (an empty answer is a completed query, not a threat);
     `:131-134` exception → 3.
   - `Commands/RouteCommand.cs` — `:26-29` unsafe target → 3; `:34-47` no hops → 3;
     otherwise → 0.
   - `Commands/SpeedCommand.cs` — `:21-25` no system resolvers → 2; otherwise → 0.
2. **macOS reference.** `SyxxNSLook.swift:12-15`, `:58-59`, `:92`, `:96`;
   `SyxxRoute.swift:19-22`, `:26-29`, `:34`; `SyxxSpeed.swift:19-21`, `:100`.
3. **Wrong today / required.** `syxx nslook example.com` succeeds, prints records, exits **2**.
   `syxx route 8.8.8.8` completes a 15-hop trace, exits **2**. `syxx speed` ranks every resolver,
   exits **2**. Required: 0 in all three cases; the failure codes above otherwise.
4. **False-clean it removes.** The inverse: a successful measurement reported as unmeasured.
   Every wrapper script on Linux that gates on `$?` sees "inconclusive" forever, which trains
   users and scripts to ignore the exit code entirely — and an ignored exit code is how the
   genuinely-inconclusive results in B1/B2 get missed.
5. **Failure-case test.** `Syxx.Tests/CliExitContractTests.cs` (same file as B0), one case per
   command driven through the extracted mappers:
   `NslookEmptyAnswerIsZeroNotOne()` (an NXDOMAIN is not a threat),
   `NslookBadTypeIsThree()`, `RouteNoHopsIsThree()`, `RouteUnsafeTargetIsThree()`,
   `SpeedNoResolversIsTwo()`. The first is the important one: a well-meaning future change that
   treats "no records" as a finding would break the contract in the threat direction.
6. **Effort.** S. **Order.** After B0.
7. **Owner verification.**
   ```
   syxx nslook example.com          ; echo "exit=$?"
   syxx nslook example.com BOGUSTYPE; echo "exit=$?"
   syxx route 8.8.8.8               ; echo "exit=$?"
   syxx route -- --evil             ; echo "exit=$?"
   syxx speed                       ; echo "exit=$?"
   ```
   - **pass** — 0, 3, 0, 3, 0 respectively.
   - **fail** — any 2 on the successful runs; any 1 anywhere (none of these is a threat channel).
   - **inconclusive** — no network, so `nslook` throws and `route` returns no hops: 3 and 3 are
     then *correct* but the success paths are untested. Re-run with connectivity.

---

### B18 — LAN snapshot persistence is missing from the C# Kit *(sweep — new)*

1. **What changes.** `Syxx.Kit/LAN/LanCartography.cs` has `LANDiff` (`:569-600`) and
   `Diff(previous, current)` (`:803-870`) but **no** `SaveSnapshot` / `LoadSnapshot`. Nothing in
   the C# tree writes a `LANSnapshot`, so `Diff` can never be called with a real `previous` and
   the entire LAN-change capability is dead code on Windows and Linux. Add
   `LANCartographer.SaveSnapshot(LANSnapshot, string? path = null)` and
   `LoadSnapshot(string? path = null)` writing `~/.syxx/lan-snapshot.json` through
   `SyxxPaths.EnsurePrivateDir` + `RestrictToOwner`, atomically (temp + move, as
   `Trust.cs:308-313` does). Call `SaveSnapshot` from `LanCommand` after the render (B8), and
   treat a persistence failure as non-fatal for the one-shot listing — matching macOS — but
   **say so** rather than swallowing it silently.
2. **macOS reference.** `macOS/Sources/SyxxKit/LAN/LANCartographer.swift:453` (`saveSnapshot`),
   `:463` (`loadSnapshot`); `macOS/Sources/SyxxCLI/Commands/SyxxLan.swift:81-87` (the call site).
3. **Wrong today / required.**
   - Today: `syxx lan` on Linux prints a listing and forgets it. A new device joining the segment
     between two runs — a rogue DNS box, an ARP-spoofing host — is invisible, because there is no
     "before" to compare against. The comparison code exists and is unreachable.
   - Required: after each successful listing, `~/.syxx/lan-snapshot.json` exists at `0600` and
     contains the snapshot; `LANCartographer.Diff` becomes reachable from a future
     `syxx lan --diff`.
4. **False-clean it removes.** Not a printed false-clean — a *capability* false-clean. The C#
   codebase carries a fully-implemented change detector (`Diff` computes new devices, missing
   devices, and MAC changes) whose presence in the source implies the product detects LAN
   changes. On Linux it detects nothing, ever, and says nothing about that. Also: macOS calls
   this out explicitly (`SyxxLan.swift:81`, comment C100) so the parity claim is falsifiable.
5. **Failure-case test.** `Syxx.Tests/LanLifecycleTests.cs` (extend the existing file):
   - `SnapshotRoundTrips()` — save then load; assert device list, status, and reason survive.
   - `SavedSnapshotIsOwnerOnly()` — assert mode `0600` on non-Windows.
   - `DiffAgainstPersistedSnapshotDetectsNewDevice()` — save A, build B with one extra device,
     `Diff(Load(), B)` reports exactly one new device. **This is the test that fails today** and
     that would fail again if persistence were removed, because `Diff` would have no input.
   - `SaveFailureIsReportedNotSilent()` — read-only directory; assert the caller receives the
     failure rather than a swallowed exception.
6. **Effort.** M. **Order.** After B8.
7. **Owner verification.**
   ```
   rm -f ~/.syxx/lan-snapshot.json
   syxx lan >/dev/null ; ls -l ~/.syxx/lan-snapshot.json
   ```
   - **pass** — the file exists at `-rw-------` and its JSON contains the same device count the
     listing printed.
   - **fail** — the file is absent after a successful listing, or is created world-readable.
   - **inconclusive** — the listing itself was `Unreadable` (no `ip`/`arp`), in which case no
     snapshot should be written and its absence proves nothing.

---

### B19 — `ContinuousMonitor` has no "VPN state is now unknown" alert *(sweep — new)*

1. **What changes.** `Syxx.Kit/Trust/ContinuousMonitor.cs:111-119`. `lostVpn` is computed
   correctly as a tri-state comparison (`previous.VpnActive == Yes && current.VpnActive != Yes`)
   but the headline has only two forms. Add the third: when `current.VpnActive == Unknown`, the
   headline is `"VPN state is now unknown."`, not `"Your VPN is no longer active."`. Same shape
   as the `EncryptedDNSChanged` fix in B4.
2. **macOS reference.** `macOS/Sources/SyxxKit/Trust/ContinuousMonitor.swift:154-160`.
3. **Wrong today / required.**
   - Today, when adapter enumeration fails between two monitor ticks (`NetworkKeyState.cs:420-428`
     returns `Unknown`), the alert is
     `Your VPN is no longer active. — Traffic that you may expect to be tunnelled is not. Re-check
     for leaks before trusting this connection.`
   - Required: `VPN state is now unknown.` with the same Warning severity (a VPN that can no
     longer be confirmed **is** a reason to re-check) but wording that does not assert the tunnel
     dropped.
4. **False-clean it removes.** Inverted — a false *positive* claim. It matters because it is the
   same class: a definite statement about the user's protection derived from a measurement that
   did not happen. A user who acts on "your VPN is no longer active" by reconnecting, or by
   assuming they were exposed, has been misinformed by a tool whose entire pitch is that it
   never does this.
5. **Failure-case test.** `Syxx.Tests/ContinuousMonitorTests.cs` (extend):
   `YesToUnknownVpnDoesNotClaimTheTunnelDropped()` — assert the headline contains `unknown` and
   does **not** contain `no longer active`; and `YesToNoVpnStillSaysNoLongerActive()` so the fix
   does not blur the genuine case.
6. **Effort.** S. **Order.** After B4 (same review, same file).
7. **Owner verification.** Not directly reachable from the CLI — `ContinuousMonitor` runs from
   the scheduler/GUI. Verify by unit test, then:
   ```
   dotnet test Windows/dotNET/Syxx.Tests --filter ContinuousMonitorTests
   ```
   - **pass** — both new cases green.
   - **fail** — the unknown case still produces the "no longer active" headline.
   - **inconclusive** — the test project does not build. Not evidence either way.

---

### B20 — Witness trust anchors read different environment variables per platform *(sweep — new)*

1. **What changes.** `Syxx.Kit/WitnessSigner.cs:400-412` resolves the witness trust set from
   `SYXX_WITNESS_TRUSTED_KEYS`, falling back to
   `%LOCALAPPDATA%/Syxx/witness_trusted_keys.txt` (`:417-424`). The macOS witness CLI passes
   `CollectorPinning.configuredTrustedSigningKeys()` (`SyxxWitness.swift:291-292`, `:317-319`),
   which reads the `collector_trusted_keys` UserDefaults key then
   `SYXX_COLLECTOR_TRUSTED_KEYS` (`LeakDetector.swift:828-841`). The two platforms therefore
   consult **different, non-overlapping** configuration. Decide one contract and implement both
   halves: recommended is that witness anchors are their own namespace
   (`SYXX_WITNESS_TRUSTED_KEYS`) on **both** platforms — the C# side is the more principled one —
   with the Swift CLI switched to a new `WitnessPinning.configuredTrustedWitnessKeys()`. Whatever
   is chosen, add a file path on Linux (`~/.syxx/witness_trusted_keys.txt`) since
   `%LOCALAPPDATA%` maps to `~/.local/share` there and is not where any doc points.
2. **macOS reference.** `SyxxWitness.swift:291`, `:317`; `LeakDetector.swift:828-841`.
3. **Wrong today / required.**
   - Today: a user follows the macOS instructions, exports `SYXX_COLLECTOR_TRUSTED_KEYS`, runs
     `syxx witness verify snap.json` on Linux and gets
     `  Verification: [VALID] Tamper-evident — Ed25519 signed`
     `  Trust: signer unverified (no trust set configured)`.
     The trust set they configured was never read.
   - Required: the same environment variable configures the same thing on both platforms, and
     `Trust:` reports `signer trusted` when it is set correctly.
   - Note the C# `Trust:` disclosure line (`WitnessCommand.cs:402-405`, `:440-442`) is *good* and
     macOS's non-detailed branch has an equivalent (`SyxxWitness.swift:337-343`); neither is the
     defect. The defect is purely which key the two read.
4. **False-clean it removes.** `[VALID] Tamper-evident — Ed25519 signed` combined with a trust
   set the user believes is configured. The `Trust:` line prevents this from being a silent
   false-clean *on screen* — but the exit code does not: an unanchored signer maps to
   `SnapshotVerification.Valid → ExitStatus.Clean = 0` (`WitnessCommand.cs:48-63`), whereas with
   the trust set actually loaded it would be `UntrustedSigner → Inconclusive = 2`. So automation
   reads 0 for a snapshot signed by an unknown key, on a machine the operator configured to
   reject exactly that.
5. **Failure-case test.** `Syxx.Tests/WitnessTrustAnchorTests.cs`:
   - `ConfiguredAnchorIsHonoured()` — set the agreed env var to a key that is *not* the
     snapshot's signer; assert `VerifySnapshotDetailed` returns `UntrustedSigner` and the mapped
     exit is 2. Renaming the variable on either platform fails this.
   - `MatchingAnchorIsValid()` — set it to the actual signer; assert `Valid`/0.
   - `NoAnchorIsValidButDisclosed()` — unset; assert `Valid`/0 **and** that the rendered text
     contains `signer unverified` so the 0 is never emitted bare.
   - A cross-platform assertion belongs in the parity corpus: the variable name is a shared
     constant, asserted equal in both test suites.
6. **Effort.** S (C# side) + coordination with the macOS lane. **Order.** Independent.
7. **Owner verification.**
   ```
   syxx witness --gen-keypair --key-file /tmp/t.key
   PUB=$(syxx witness --gen-keypair --key-file /tmp/t2.key | awk '/Public key/{print $NF}')
   syxx witness create example.com --key-file /tmp/t.key --output /tmp/t.json
   SYXX_WITNESS_TRUSTED_KEYS="$PUB" syxx witness verify /tmp/t.json --detailed ; echo "exit=$?"
   ```
   - **pass** — with a trust set that does **not** contain the signer, the output shows
     `[INCONCLUSIVE] UNTRUSTED SIGNER` and `exit=2`.
   - **fail** — `[VALID]` with `exit=0` while a non-matching trust set is exported.
   - **inconclusive** — `create` fails for an unrelated reason (no resolvers, B9). The trust
     path was not exercised.

---

### B21 — `firstrun` transcript wording diverges from macOS *(sweep — new)*

1. **What changes.** `Windows/dotNET/Syxx/Commands/FirstRunCommand.cs`.
   - `:87` — `  RESULTS` → `  CONCLUSIONS`.
   - `:67` — progress prefix `$"  {msg}"` → `$"  ⏳ {msg}"`.
   - `:141-143` — `  ⚠️ UNVERIFIED SIGNALS:` and `     - {DisplayName} [{Reason}]: {Detail}` →
     `  ⚠️  Unverified signals (seen but not confirmable):` and `     - {DisplayName}: {Detail}`.
     **Judgement call for the owner:** the C# form carries `[{s.Reason}]`, which macOS drops.
     That is *more* honest — it names why the signal was suppressed. Recommendation: change
     macOS to include the reason rather than removing it from C#; if the owner prefers strict
     transcript parity now, drop it from C# and file the macOS improvement.
   - `:115` — C# guards the empty case (`result.Ran.Count == 0 ? "(none)" : …`); macOS
     (`FirstRunScan.swift:120`) joins an empty array to an empty string, printing
     `Disciplines ran: ` with nothing after it. **Keep the C# form**; this is an inverted gap.
2. **macOS reference.** `FirstRunScan.swift:91` (header), `:74` (progress prefix), `:144-147`
   (suppressed block), `:120` (the ran line — the inverted one).
3. **Wrong today / required.** As enumerated above. The functional half is B1; this is the
   textual half, and it must not be done by degrading C#.
4. **False-clean it removes.** None on its own. It removes the *pretext* for weakening a parity
   assertion: a transcript diff that fails only on cosmetics gets muted, and a muted diff stops
   catching the substantive divergences (B1, B12) it was built for.
5. **Failure-case test.** `Syxx.Tests/FirstRunTranscriptParityTests.cs`:
   `HeaderAndSuppressedBlockMatchMacOS()` — assert against literals extracted from the Swift
   source and checked into the corpus. Plus `RanLineHandlesEmpty()` — assert
   `Disciplines ran: (none)` for an empty list, so the inverted gap is never "fixed" toward
   macOS by accident.
6. **Effort.** S. **Order.** After B1; land with B15.
7. **Owner verification.**
   ```
   syxx firstrun --domain example.com | sed -n '1,12p;/CONCLUSION/,+4p'
   ```
   - **pass** — the section header reads `  CONCLUSIONS`, progress lines carry the ⏳ prefix,
     and any suppressed-signal block matches the agreed form.
   - **fail** — the header still reads `  RESULTS`.
   - **inconclusive** — the scan aborts into the `[INCONCLUSIVE] The scan could not complete`
     path, so the results section is never printed.

---

## §3 — SWEEP: `Syxx.Kit` re-run for Linux tri-state degradations

The brief noted a prior sweep died before finishing, with only two findings confirmed
(`DhcpPushedDNS`, `IsDNSEncrypted`). This is the completed sweep across every requested file. For
each `OperatingSystem.IsWindows()` / `IsLinux()` / `RuntimeInformation.IsOSPlatform` / Registry /
WMI / netsh / Win32 path, the question asked was: **does the non-Windows branch return an honest
unknown, or a dishonest definite?**

### 3.1 Every platform branch found

| File:line | Branch | Non-Windows behaviour | Honest? |
|---|---|---|---|
| `NetworkKeyState.cs:150` | `IsLinux()` → `GetScopedResolversFromResolvectl` | Falls back to adapter enumeration when `resolvectl` is absent (`:214-221`) | ✅ |
| `NetworkKeyState.cs:188` | `IsWindows()` gate on DoH template read | Linux leaves `Protocol = null`; `EncryptedDNSStatusCore:642-649` then returns Unknown for the all-null case | ✅ (C138 already generalised this) |
| `NetworkKeyState.cs:445` | `IsLinux()` → tunnel-name matching | Correct — `.NET` reports Linux tunnels as Ethernet/Unknown; prefix set pinned by test | ✅ |
| `NetworkKeyState.cs:647` | platform name in the Unknown evidence string | Cosmetic | ✅ |
| `NetworkKeyState.cs:682` | `!IsWindows() → Unknown` for DoH template | Textbook honest unknown | ✅ |
| **`NetworkKeyState.cs:796`** | `!IsWindows() → new List<string>()` | **Empty ≠ null; contradicts its own doc at `:792-793`** | ❌ **B5** |
| **`NetworkKeyState.cs:762`** | 5xx → `ConditionState.No` | 5xx from a proxy asserted as "no admin panel" | ❌ **B6 (new)** |
| **`NetworkKeyState.cs:764-767`** | timeout/HTTP failure → `ConditionState.No` | A blocked probe asserted as a negative finding | ❌ **B6 (new)** |
| `NetworkKeyState.cs:748-749` | gateway undeterminable | Detail omits macOS's "isDNS is unknown, not false"; `GatewayInfo.IsDNS` is a `bool` | ⚠️ **B6 (new)** |
| **`NetworkKeyState.cs:699`** | `IsDNSEncrypted() => State == Yes` | Unknown → false | ❌ **B4** |
| `NetworkKeyState.cs:712-713` | `NetworkStateReport.VpnActive` / `CaptivePortalDetected` are `bool` | Unknown → false in the structured report; mitigated because the typed `Vpn`/`CaptivePortal` fields sit beside them and `KeyCommand` uses those | ⚠️ note, low |
| `TracerouteRunner.cs:116` | `IsWindows()` → `tracert` vs `traceroute` | Correct binary + correct `-w` unit conversion; `TraceStatus` is a real tri-state and `RouteCommand` renders it | ✅ (better than macOS) |
| `LAN/LanCartography.cs:707` | `IsLinux()` → `ip neigh show` with `arp -a` fallback | Failure → `LANSnapshot.Unreadable(reason)`; genuine tri-state at `:434-438` | ✅ (the collapse is in the CLI — B8) |
| `Forensics/EndpointLogAnalyzer.cs:48-54` | Linux vs Windows persistence/exec markers | Real Linux marker sets, not a Windows list applied blindly | ✅ |
| **`Forensics/EndpointLogAnalyzer.cs:94`** | `catch { }` in `Detect` | I/O error indistinguishable from "not an endpoint log" | ❌ **B11** |
| `LicenseManager.cs:173` | `IsWindows()` legacy DPAPI blob | Non-Windows returns `null`, not a fabricated key | ✅ |
| `LicenseManager.cs:395-410` | fingerprint dispatch | `Unavailable` when unidentifiable; the hostname fallback was deliberately removed (`Platform/MachineFingerprint.cs:9-17`) | ✅ exemplary |
| `Platform/MachineFingerprint.cs` | Linux hardware id | `FingerprintQuality.Hardware / InstallScoped / Unavailable`, never a silent degrade | ✅ exemplary |
| `SyxxPaths.cs:20`, `:40` | `!IsWindows()` chmod 0700 / 0600 | Best-effort with an explicit comment on exFAT/overlay; failure is not claimed as success by the helper | ⚠️ see 3.3 |
| `UnboundResolver.cs:172-199` | binary name / search path / `where` vs `which` | Correct Linux paths; `null` means not installed and is surfaced as `NotInstalled` | ✅ |
| `PlatformSecretStore.cs:31-41` | store selection | libsecret → `secret-tool` → AES-256-GCM file; each fallback explicit | ✅ |
| `ProbeRunner.cs`, `ProbePlan.cs`, `ResolverContext.cs`, `DNSResolver.cs` | — | **No platform branches at all.** Swept clean. | ✅ |
| `Platform/LinuxDistro.cs` | Linux-only by construction | No cross-platform claim | ✅ |

### 3.2 New findings promoted to tasks

- **B6** — gateway `AdminReachable` collapses Unknown → No on 5xx and on timeout
  (`NetworkKeyState.cs:762`, `:764-767` vs `NetworkKeyState.swift:704-721`), and the
  undeterminable-gateway detail omits the `isDNS is unknown, not false` disclaimer
  (`:748-749` vs `:686-690`).
- **B18** — `LANCartographer` has `Diff` (`LanCartography.cs:803`) but no
  `SaveSnapshot`/`LoadSnapshot`; macOS has both (`LANCartographer.swift:453`, `:463`). The change
  detector is unreachable on Windows and Linux.
- **B19** — `ContinuousMonitor.cs:111-119` has no `Unknown` headline for the VPN transition;
  macOS does (`ContinuousMonitor.swift:154-160`).
- **B20** — witness trust anchors read `SYXX_WITNESS_TRUSTED_KEYS` in C#
  (`WitnessSigner.cs:402`) and the collector namespace on macOS (`SyxxWitness.swift:291`,
  `LeakDetector.swift:828-841`).
- **B10** — `witness --gen-keypair` has no overwrite guard in C# (`WitnessCommand.cs:85-160`);
  macOS refuses without `--force` (`SyxxWitness.swift:69-77`).
- **B21** — `firstrun` transcript wording (`FirstRunCommand.cs:87`, `:67`, `:141-143` vs
  `FirstRunScan.swift:91`, `:74`, `:144-147`).
- Extension to **B4** — `ContinuousMonitor.cs:100`'s `previous.DnsEncrypted && !current.DnsEncrypted`
  is a second consumer of the collapsed bool, producing a **Warning** ("Your lookups are now
  visible to the network") from a `Yes → Unknown` transition. macOS distinguishes
  (`ContinuousMonitor.swift:131-148`). The brief covered the display and the diff; this is the
  third blast-radius site.
- Extension to **B3** — `SentinelCommand.cs:50` collapses `VpnActive`, which is **already** a
  `ConditionState` in the Kit (`Trust.cs:215`, comment D076). This is a pure display bug with no
  Kit change required, and the same collapse appears in the guided path at `Program.cs:1082`.

### 3.3 Findings deliberately **not** promoted to tasks

- `SyxxPaths.EnsurePrivateDir` / `RestrictToOwner` (`SyxxPaths.cs:26-30`, `:44-48`) swallow chmod
  failures. The comment says "best-effort" and names exFAT/overlay, which is a real constraint —
  but a `witness.key` that could not be narrowed to `0600` is a *secret in a known-insecure
  state*, and the caller is not told. `WitnessCommand.cs:143-154` shows the right pattern (it
  reports the failure and returns 3). Recommendation: give both helpers a `bool` return and audit
  the ~8 call sites. Not a Linux/macOS parity gap, so it is out of this lane's mandate — file it.
- `NetworkStateReport.VpnActive` / `CaptivePortalDetected` as `bool` (`NetworkKeyState.cs:712-713`).
  The typed tri-states sit beside them and every CLI consumer uses the typed field. Flagged as a
  trap for future consumers rather than a live defect.
- The shared multi-GB `ReadAllText` hazard after a failed endpoint-log detect
  (`ForensicsCommand.cs:110`, `SyxxForensics.swift:121`) — present on both platforms, so not a
  parity gap. Availability, not honesty. File separately.

---

## §4 — Inverted gaps: C# is better. Do **not** "fix" these toward macOS.

Each of these must be protected by an assertion so a future parity pass cannot degrade it.

1. **`route` failure diagnostics.** `RouteCommand.cs:36-46` labels the specific failure
   (`failed to launch` / `timed out before any hop responded` / `produced no output`), prints
   `Diagnostic:`, and shows the truncation notice at `:54-58`. macOS
   (`SyxxRoute.swift:26-29`) prints one undifferentiated
   `Traceroute failed, timed out, or produced no output.` **Keep C#.** B17 adds exit codes only.
   Protect with `RouteRenderingTests :: EachTraceStatusHasItsOwnMessage()`.
2. **`firstrun --help` guard.** `FirstRunCommand.cs:17-18` calls `HandleGlobalFlags` before doing
   anything; macOS `FirstRunScan.swift:29-46` has no such guard, so `syxx firstrun -h` starts a
   multi-minute scan. **Keep C#.** Protect with
   `FirstRunExitContractTests :: HelpDoesNotStartAScan()`.
3. **`witness verify` evidence-quality disclosure.** `WitnessCommand.cs:397-398` and `:433-434`
   print both `Inconclusive:` and `Degraded:` (comment B100); macOS's detailed branch
   (`SyxxWitness.swift:304`) prints neither and its summary branch (`:333`) prints only
   `Inconclusive:`. **Keep C#** — a valid signature over degraded evidence must not read as a
   clean bill of health.
4. **`witness` trust posture in the detailed branch.** `WitnessCommand.cs:402-405` prints
   `Trust: signer trusted | signer NOT in your trust set | signer unverified (no trust set
   configured)`. macOS's detailed branch has no equivalent line. **Keep C#.**
5. **`firstrun` empty-discipline guard.** `FirstRunCommand.cs:115` renders `(none)`; macOS
   (`FirstRunScan.swift:120`) prints a trailing empty string. **Keep C#** (see B21).
6. **`nslook` numeric-type acceptance error text.** `NslookCommand.cs:89-91` sorts the supported
   list with an explicit `StringComparer.Ordinal` for a stable message. Cosmetic but keep.
7. **Guided-mode honesty copy.** `Program.cs:816-822`, `:884`, `:919`, `:927`, `:939`, `:946`,
   `:962`, `:1005-1007` all carry explicit "this is NOT a clean result" language with a cause and
   a remedy. macOS's guided path is thinner. **Keep C#.**

---

## §5 — Deferred / out of scope for this lane

Reserved by other agents; noted so the plan is complete, not so they are worked.

- **`cage` / `CageCommand.cs` / `DNSCapture.cs` / `Capture/**`** — mid-change. `cage` is one of
  the ten commands that never sets an exit code; B0 makes the return type available, and the
  cage owner should map it. *Not touched here.*
- **`mirror` / `MirrorCommand.cs` / `ProphetPresentation.cs` / `ProphetConsensus.cs`** — being
  de-duplicated under the Mirror/Prophet reimagining. `MirrorCommand` already sets exit codes.
  *Not touched here.*
- **`WarpEngine.cs` / `WarpManifestPinning.cs` / `TextDiff.cs`** — Warp is being made functional
  in a separate lane. `WarpCommand` already sets exit codes. *Not touched here.*
- **All GUI** (`Windows/dotNET/SyxxUI/**`, `macOS/Sources/SyxxApp/**`) — out of lane. Note that
  B4's `NetworkConfigSnapshot.DnsEncrypted` type change is a **cross-lane signal**: any GUI
  binding to that property must be updated in the same commit. Flag to the GUI owner before
  landing B4.
- **`Program.cs` interactive guided mode** — in lane and in scope, but its defects are the same
  ones the subcommands have (`:1082-1083` tri-state collapses, `:1109` unguarded
  `SaveSnapshot`, `:357` missing `isPro`). Fold those edits into B3, B4, and B12 rather than
  giving them their own tasks.
- **macOS-side reciprocal fixes** identified while establishing the reference and belonging to
  the macOS lane: `dhcpPushedDNS()` returning `[]` for both "none" and "failed"
  (`NetworkKeyState.swift:659-661`) and omitting the clause at `:729`; `firstrun` starting a scan
  on `-h`; `firstrun` printing an empty `Disciplines ran:`; `witness verify --detailed` not
  printing `Degraded:`; `route` collapsing every failure into one sentence. Hand off, do not fix
  here.

---

## §6 — Suggested landing order

```
B0                                  (enabler — must be first)
├─ B1, B12, B21                     firstrun (one file, one review)
├─ B2                               key / filter / forensics threat exits
├─ B17                              nslook / route / speed exits
├─ B11                              forensics analyze
├─ B7                               leak rendering
├─ B8  →  B18                       lan rendering, then persistence
└─ B14 (a,b,d,e)                    unbound quick wins;  B14(c) scheduled separately

B4  →  B3, B19                      tri-state DnsEncrypted, then its consumers
B5  +  B6                           gateway honesty (same method, one review)
B9  +  B10                          witness key handling (same file, one review)
B13                                 -v
B15                                 label reconciliation (after B14e, B21)
B16                                 key resolver-empty reason
B20                                 trust-anchor namespace (needs macOS-lane agreement)
```

**Full-lane acceptance gate.** Before this lane is called done, on the Linux target:

```
for c in nslook key route lan filter forensics sentinel speed unbound firstrun leak witness; do
  syxx "$c" --help >/dev/null 2>&1; echo "$c help=$?"
done
```

- **pass** — every command reports `help=0`. (`--help` is not a security conclusion; today ten of
  them report 2.)
- **fail** — any `help=2`.
- **inconclusive** — the binary is not on `PATH` or fails to start; nothing was measured.
