# WS8 Findings Independent Code Verification

Date: 2026-07-24  
Reference: `WS8-FIX-PLAN.md`  
Repository snapshot: commit `39579e9d077288ccf83054d86e43e65dfdaeafe6` plus the current uncommitted working tree  
Method: independent source trace of each stated finding, supplemented by focused local checks where possible  

## Scope and interpretation

This report does not modify `WS8-FIX-PLAN.md`, its findings, or any project ledger. It evaluates whether each finding is supported by the current code.

The working tree already contains uncommitted changes in several cited files. A result of "not present now" therefore does not establish that the pattern never existed. Where the repository baseline and working tree differ materially, that distinction is stated.

Status meanings:

- **Confirmed**: the current code demonstrates the stated defect and mechanism.
- **Partially confirmed**: part of the observation is present, but the stated cause, impact, or scope is not fully supported.
- **Not supported**: the named code contradicts the finding or does not contain the alleged mechanism.
- **Currently mitigated**: the current working tree contains a direct mitigation, so the defect is not present in the inspected snapshot.
- **Not independently assessable**: the plan does not identify enough concrete instances to test the claim without conducting a new audit.

## Executive result

| Item | Independent status | Short result |
|---|---|---|
| 1. `fetcher.py` IPv4-mapped IPv6 SSRF | **Currently mitigated** | Explicit mapped-address normalization is present and the exact private-address probes are rejected. |
| 2. `resolver.py` SQL injection | **Not supported** | `resolver.py` contains no database API, SQL statement, cursor execution, or interpolated SQL. |
| 3. `AppModel.swift` privilege loop | **Not supported as stated** | The app launches and waits for a separate `osascript` process; it owns no Authorization Services handle. The cited D060 concern is shell quoting, not a retained privilege handle. |
| 4. Witness DPAPI double-free | **Not supported** | Witness delegates to `DpapiNative`; DPAPI output is freed once with `LocalFree`, while caller-allocated input buffers are freed once with `FreeHGlobal`. |
| 5. Dashboard refactor clipping | **Partially confirmed** | The fixed-height, non-scrolling dashboard has a credible clipping risk, but the current code explicitly replaces the removed cards with a fixed-height activity terminal. The asserted stale right-column allocation is not present. |
| 6. Windows WFP return codes | **Not supported** | The named file and P/Invoke symbols do not exist. Current source identifies Windows WFP/service integration as later scope. |
| 7. macOS capture I/O storm | **Partially confirmed historically; currently mitigated** | The baseline had per-record writes, but not in `DNSCapture.swift` or on the GUI main queue. The working tree now batches writes in the external Cage CLI process. |
| 8. Hardcoded `KEYGEN_PUBLIC_KEY` | **Not supported** | The default is the empty string, not a shared public key. Missing configuration fails license verification closed and is reported by preflight. |
| 9. Windows synthesis O(n²) rule scan | **Not supported as stated** | The named engine has no connection-rule scan. The current matrix engine performs dictionary lookups while scanning each diagnosis's own rules; its separate diagnosis-margin pass is O(D²), not O(R²). |
| 10. World-writable root logs | **Not supported in the named code** | `logging_util.py` writes to stdout and creates no file or directory. Deployed redirection permissions are outside this module and are not established by the repository. |
| 11-17. Theme and layout totals | **Not independently assessable** | The plan supplies totals only, with no token names, view locations, expected values, or 28-deviation inventory. |

No item among 1 through 10 is confirmed exactly as written against the current working tree. Item 5 contains a current layout risk, and item 7 identifies a historical unbatched-write pattern, but each finding overstates or misidentifies its root cause.

## Detailed verification

### 1. Server SSRF: IPv4-mapped IPv6

**Status: Currently mitigated. Historical exploitability is not established by this snapshot.**

Current `Server/syxx_server/fetcher.py:70-84` detects `::ffff:0:0/96` and extracts the embedded IPv4 address. `_ip_is_public` applies that normalization before private, loopback, link-local, reserved, multicast, unspecified, and CGNAT checks at `fetcher.py:87-109`. `_resolve_public` rejects the request if any resolved address fails the gate at `fetcher.py:113-130`.

Focused local probes produced:

| Address | Current result |
|---|---|
| `::ffff:127.0.0.1` | rejected |
| `::ffff:10.0.0.1` | rejected |
| `::ffff:169.254.169.254` | rejected |
| `::ffff:8.8.8.8` | accepted |
| `::1` | rejected |
| `fc00::1` | rejected |
| `2606:4700:4700::1111` | accepted |

The current behavior matches the plan's desired allow/block distinction. The working-tree diff shows the explicit normalization was added after the current `HEAD`, so the report cannot describe the finding as an extant defect. The existing SSRF unit list at `Server/tests/test_server_hardening.py:140-148` does not include the mapped-address cases; the exact local probe above supplies current evidence but is not a committed regression test.

### 2. Server SQL injection in `resolver.py`

**Status: Not supported.**

`Server/syxx_server/resolver.py:1-16` identifies and imports the components of a self-contained DNS and HTTP client. An AST inspection of all 686 lines found:

- no `sqlite3`, database driver, ORM, or project database import;
- no `cursor`, `execute`, `executemany`, or `executescript` database call;
- no `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `CREATE TABLE`, or `DROP TABLE` token.

The `connect` method in this file belongs to its pinned HTTPS connection, not a database. There is therefore no SQL construction in the named file to parameterize and no direct SQL injection path matching the finding.

### 3. macOS `osascript` privilege loop

**Status: Not supported as stated.**

The cited line number is stale. The current escalation site is `macOS/Sources/SyxxApp/AppModel.swift:1032-1058`. It:

1. constructs an AppleScript command;
2. launches `/usr/bin/osascript` as a separate `Process`;
3. waits for that process to exit;
4. stores only the subprocess result.

On normal completion, the model clears its process reference at `AppModel.swift:1102-1113`. Stop and reset paths terminate a still-running subprocess through `CaptureProcessBox` at `AppModel.swift:979-980` and `AppModel.swift:1566-1569`.

The app does not create or retain an `AuthorizationRef`, privileged file descriptor, or reusable elevated service connection. Source inspection therefore does not support "elevated handles persist." A runtime process-tree audit would be required to prove whether an interrupted AppleScript ever leaves a child capture process alive, but that is a different claim.

The plan's D060 cross-reference is also mismatched. D060 concerns unsafe nested shell quoting in this command, not failure to drop a retained Authorization Services handle. Replacing the code with deprecated `AuthorizationExecuteWithPrivileges` does not follow from the alleged mechanism.

### 4. Windows DPAPI double-free

**Status: Not supported.**

`Windows/Syxx.Kit/WitnessKeyStore.cs:50-64` does not allocate or free native memory. It reads a non-empty file and delegates decryption to `DpapiNative.Unprotect`.

The actual native-memory ownership is in `Windows/Syxx.Kit/LicenseManager.cs:17-100`:

- `ToBlob` allocates the two input buffers with `Marshal.AllocHGlobal` at lines 46-50.
- The corresponding input buffers are each freed once in `finally` at lines 95-99.
- A successful `CryptUnprotectData` output is copied and freed once with the allocator-matched `LocalFree` at lines 53-61.
- If `CryptUnprotectData` returns failure, execution throws at lines 89-92 and never calls `FromBlob`, so no output buffer is freed by this code.

The finding conflates input and output ownership and names the wrong deallocator for the DPAPI output. No path in the inspected code calls `FreeHGlobal` twice on one allocation.

Platform limitation: DPAPI behavior cannot be executed on the macOS review host, so this conclusion is a source-level ownership trace rather than a Windows runtime test.

### 5. macOS Dashboard refactor and clipping

**Status: Partially confirmed.**

The component removal is real. `macOS/Sources/SyxxApp/DashboardView.swift:220-256` explicitly documents removal of the verdict card and chronicle, then lays out the warden grid beside an activity terminal. The right column is not left with stale invisible space: `terminalPane` is assigned a concrete 240-point content height plus padding at `DashboardView.swift:445-487`.

A separate clipping risk is source-verifiable:

- the application hard-locks its window to 1050 by 613 points at `macOS/Sources/SyxxApp/SyxxApp.swift:53-70` and `SyxxApp.swift:104-110`;
- the Dashboard root is a non-scrolling `VStack` at `DashboardView.swift:34-305`;
- the activity region alone has 240 points of fixed content height plus 28 points of padding at `DashboardView.swift:482-484`;
- the surrounding title, banner, actions, minimum spacers, warden section heading, and Pro banner must share the remaining height.

That combination can clip content at the fixed window height. However, the code does not support the stated cause that the right-column space was simply left unadjusted after removal. It was replaced with a fixed-height terminal, and restoring the removed verdict and chronicle would add content rather than resolve the height budget.

No current GUI screenshot was used. A live run at exactly 1050 by 613 remains necessary to establish the exact clipped controls and interaction impact.

### 6. Windows WFP return codes

**Status: Not supported.**

`Windows/Syxx.Kit/WfpFilterManager.cs` does not exist. Repository-wide C# search found zero references to `FwpmEngineOpen0` and `FwpmSubLayerAdd0`.

The current Windows resolver source explicitly says the Windows WFP/service equivalent is later scope at `Windows/Syxx.Kit/UnboundResolver.cs:24-30`. Because there are no WFP calls, there are no unchecked WFP return codes in the current implementation. The risk may become relevant when that future subsystem is implemented, but it is not a current code defect.

### 7. macOS DNSCapture I/O storm

**Status: Partially confirmed historically; currently mitigated.**

The plan identifies the wrong file and execution context. `macOS/Sources/SyxxKit/DNSCapture.swift` parses frames, packets, and process metadata; it does not perform the live per-packet file write and contains no main-queue dispatch.

The write path is in `macOS/Sources/SyxxCLI/Commands/SyxxCage.swift`. The repository baseline wrote each record immediately. The current working tree instead defines an actor-backed buffer at `SyxxCage.swift:4-40`, uses 100-record or 100-millisecond flushing at `SyxxCage.swift:19-34`, instantiates it at `SyxxCage.swift:184-186`, and performs a final flush at `SyxxCage.swift:227-228`.

The GUI launches the Cage CLI as a detached external process at `macOS/Sources/SyxxApp/AppModel.swift:1037-1058`. Therefore even the prior unbatched disk write was not a `DispatchQueue.main` operation in the GUI process.

No 10,000-packet performance benchmark exists in the cited plan or current tests, so the claimed threshold and UI-blocking impact remain unmeasured. The narrower historical observation, one output write per captured record, is supported by the baseline diff.

### 8. Hardcoded `KEYGEN_PUBLIC_KEY`

**Status: Not supported.**

`Server/syxx_server/config.py:272-277` reads `SYXX_KEYGEN_PUBLIC_KEY` with an empty-string default. There is no built-in public-key value shared by all builds.

When the value is absent outside explicit development mode, signature verification returns `False` at `Server/syxx_server/licensing.py:139-155`. The server preflight classifies the missing variable as a blocker at `Server/syxx_server/preflight.py:25-31`. Focused execution of all 12 preflight tests passed, including the stock-deploy missing-key blocker.

The daemon intentionally reports the blocker without taking down unrelated DNS and analysis services. That startup policy differs from the plan's proposed full startup failure, but it does not create the alleged shared trust anchor.

### 9. Windows synthesis O(n²) rule scan

**Status: Not supported as stated.**

`Windows/Syxx.Kit/Synthesis/SynthesisEngine.cs` contains no connection-rule collection, per-connection evaluation loop, or `RuleIndex` concept matching the finding.

The newer determinative engine builds dictionaries for sections and indicators once at `Windows/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:734-747`. It then scans only each diagnosis's own support and exclusion rules while performing O(1) indicator lookups at `MatrixDeterminativeEngine.cs:865-917`. This is linear in the total rule count for raw scoring.

There is a separate nested diagnosis comparison: each diagnosis asks for the strongest non-allowed competitor at `MatrixDeterminativeEngine.cs:749-769`. That pass is O(D²) in diagnosis count, not O(R²) in rules and not "all rules per connection." The default Windows specification currently declares 35 `DiagnosisIndicatorRule` instances, not 500 or more.

The legacy Dempster helper in `SynthesisEngine.cs:599-617` also performs pairwise focal-set multiplication, but that is evidence-mass combination rather than connection-rule lookup. No benchmark or production trace in the supplied material demonstrates the claimed 500-rule latency.

### 10. Server world-writable root logs

**Status: Not supported in the named code.**

`Server/syxx_server/logging_util.py:24-44` serializes JSON and writes it to `sys.stdout`. It opens no log file, creates no log directory, requests no `0666` mode, and implements no log rotation.

A filesystem scan found no world-writable regular file under `Server/` in the inspected workspace. This does not prove the permissions of files created by journald, a container runtime, shell redirection, or a production deployment. It does prove that `logging_util.py` is not creating root-owned `0666` files as alleged.

The proposed `ensure_log_permissions()` helper is therefore not connected to the current logging architecture. Any real production permission finding needs the concrete service unit, redirection configuration, deployed path, and `stat` output.

### 11-17. Theme token and GUI layout totals

**Status: Not independently assessable from the supplied findings.**

The plan states "11 Warden theme token mismatches" and "28 GUI layout deviations" but does not list:

- the affected token names;
- expected and actual values;
- platform and source file for each mismatch;
- the 28 affected views or controls;
- reference mockup selectors or coordinates;
- screenshots or reproduction dimensions.

Verifying those totals would require a new cross-platform visual and token audit, not verification of the supplied findings. This report therefore does not accept or reject the counts.

## Verification commands and outcomes

The following non-network checks were used:

1. Exact IPv4-mapped and native IPv6 calls to `fetcher._ip_is_public`: expected private cases rejected and public cases accepted.
2. `python3 -m unittest tests.test_server_hardening.SSRFBlockTests`: 4 tests passed.
3. `python3 -m unittest tests.test_preflight -v`: 12 tests passed.
4. Python AST inspection of `resolver.py`: no SQL imports, statements, or database execution calls.
5. Repository-wide symbol search: no WFP manager file and zero `FwpmEngineOpen0` or `FwpmSubLayerAdd0` hits.
6. Repository filesystem scan: zero world-writable regular files under `Server/`.
7. Static ownership trace of DPAPI input and output allocations.
8. Static layout constraint trace for the fixed-size macOS Dashboard.

One earlier preflight command guessed a nonexistent test class name and produced a `unittest` loader error. Running the actual `tests.test_preflight` module immediately afterward passed all 12 tests. The loader error was a command-selection mistake, not a code, test, or specification failure.

## Bottom line

The plan should not be used as implementation authority without another vetting step. Against the current tree:

- findings 2, 3, 4, 6, 8, 9, and 10 are unsupported by their named code paths;
- finding 1 is directly mitigated in the working tree;
- finding 7 has a narrower historical basis and is also mitigated in the working tree;
- finding 5 identifies a plausible current clipping problem but gives an unsupported root cause and a restoration direction that would increase the constrained content;
- findings 11-17 are too underspecified to verify.

No source code, supplied finding, or ledger was changed during this review.
