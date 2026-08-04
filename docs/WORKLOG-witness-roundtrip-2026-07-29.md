# Worklog — cross-platform witness round trip (B251 / E162)

Started 2026-07-29. Branch: matrix-cutover-phase3.

## Task
1. File two confirmed cross-platform defects in DELTAS.md (next free: B251, E162).
2. Fix the Windows READER so it accepts the macOS wire format (no change to what macOS writes).
3. Fix VantageComparer duplicate-key crash without discarding evidence.
4. Real round trip both directions + checked-in golden vector fixture verified by both suites.

## Status
- [ ] Baseline test counts
- [ ] Reproduce defect 1 (Windows verify of macOS witness.json)
- [ ] Reproduce defect 2 (Windows witness create)
- [ ] Enum boundary audit
- [ ] Fixes
- [ ] Golden vector
- [ ] Mutation testing
- [ ] Real round trip

## Baselines (2026-07-29)
- macOS: `Executed 565 tests, with 4 tests skipped and 0 failures`
- Windows: `Passed! - Failed: 0, Passed: 195, Skipped: 0, Total: 195`

## Reproductions (real CLI, real network)
- macOS sign: `syxx witness create example.com` -> VALID on macOS. File has
  `"comparison"."dohOutcomes"."Cloudflare"."status" : "transportError"`.
- Windows verify of that file:
  `Failed to verify witness snapshot: The JSON value could not be converted to
   Syxx.Kit.DNSResponseStatus. Path: $.comparison.dohOutcomes.Cloudflare.status |
   LineNumber: 8 | BytePositionInLine: 35.`   [DEFECT 1 CONFIRMED]
- Windows create: `Failed to create witness snapshot: An item with the same key has
  already been added. Key: Cloudflare`   [DEFECT 2 CONFIRMED]

### Defect 2 root cause (determined, not guessed)
Windows/dotNET/Syxx.Kit/VantageComparer.cs:88-96. `udpTasks` (keys Cloudflare/Google/
Quad9) and `dohTasks` (THE SAME three keys) are concatenated into one flat
`allResults`, then re-partitioned by NAME membership:
`allResults.Where(r => PublicResolvers.ContainsKey(r.Key)).ToDictionary(...)`.
Both the Do53 leg and the DoH leg of a resolver carry the same name, so the filter
admits both and ToDictionary throws. The resolver IS legitimately probed over two
transports; the KEY (bare resolver name) is what is wrong. Collapsing the duplicate
would discard the DoH observation, which is the entire evidentiary basis of the B010
unanimous-UDP-tampering check. Fix = partition by TRANSPORT (known at construction),
matching macOS VantageComparer.swift:117-134 which never flattens the two sets.

## Fixes landed (all inside Syxx.Kit — converters attached to TYPES, so CLI + GUI both get them)
- NEW  Windows/dotNET/Syxx.Kit/WitnessWireJson.cs
- EDIT Windows/dotNET/Syxx.Kit/DNSResolver.cs:79-103        [JsonConverter] on DNSResponseStatus
- EDIT Windows/dotNET/Syxx.Kit/VantageComparer.cs:22-73     errorDescription wire name + alias
- EDIT Windows/dotNET/Syxx.Kit/VantageComparer.cs:75-85,131-146,164-196  transport partition
- EDIT Windows/dotNET/Syxx.Kit/WitnessSigner.cs:13-36       date converters on the snapshot
- macOS/Sources/SyxxKit: UNCHANGED (gold standard, by design)

## Tests
- NEW macOS/Tests/SyxxKitTests/WitnessCrossPlatformGoldenVectorTests.swift   (8)
- NEW macOS/Tests/SyxxKitTests/DNSResponseStatusWireParityTests.swift        (4)
- NEW macOS/Tests/SyxxKitTests/_WitnessCrossPlatformFixtureGenerator.swift   (1, gated/skipped)
- NEW Windows/dotNET/Syxx.Tests/WitnessCrossPlatformWireTests.cs             (12)
- NEW Windows/dotNET/Syxx.Tests/WitnessCrossPlatformGoldenVectorTests.cs     (14)
- NEW corpus/witness-crossplatform/witness-macos-degraded.json  (verbatim real CLI capture)
- NEW corpus/witness-crossplatform/witness-macos-answers.json   (real signer, stated inputs)

## Mutation runs — every fix observed RED before restore
 M1 remove [JsonConverter] on DNSResponseStatus      -> 12 FAIL (orig STJ error text returns)
 M2 revert PartitionByTransport to name-keyed        -> 2 FAIL, "An item with the same key has
                                                       already been added. Key: Cloudflare"
 M3 revert ErrorMessage wire name to pre-fix state   -> 6 FAIL (both vectors DisplayMismatch)
 M4 remove the Apple-reference-date converters       -> 12 FAIL
 M5 drop the ULP nudge in ToAppleReferenceSeconds    -> 1 FAIL, Expected 1785342777002 /
                                                       Actual 1785342777001 (the D102 class)
 M6 rename Swift rawValue transportError             -> 10/12 FAIL on macOS
 M7 default an unknown status to Success             -> 2 FAIL
 All files restored and byte-compared against backups before the final suites.

## Results
- macOS  565 -> 578 executed, 4 -> 5 skipped, 0 failures
- Windows 195 -> 221 passed, 0 failures
- Real round trip, watched, both directions VALID (see final run in the report).

## Not fixed / reported
- B252 DoH evidence is unsigned and unchecked (filed, characterisation tests added).
- macOS SyxxKit UDP resolver times out where `dig` succeeds on this host — Prophet is dead here.
- Eight new user-reachable strings pending approval (outputs-catalog.csv).
