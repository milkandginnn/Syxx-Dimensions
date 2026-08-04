# DIAG — Glutton / leak test "CollectorError error 0" (2026-07-29)

Read-only diagnosis. No source files edited.

## GUI symptom (from owner screenshot)
```
Conclusion: ERROR
Observed Resolvers: none
Note: Token request failed: The operation couldn't be completed.
      (SyxxKit.LeakDetector.(unknown context at $100fbc78c).CollectorError error 0.)
```

## STEP 1 — Decode "CollectorError error 0"

Enum: `macOS/Sources/SyxxKit/LeakDetector.swift:381-384`
```swift
private enum CollectorError: Error {
    case badStatus(Int)   // <- declaration index 0  == "error 0"
    case badJSON(Error)   // <- declaration index 1
}
```
- `CollectorError` is a bare `Error`, NOT `LocalizedError`. So `error.localizedDescription`
  is the Foundation NSError bridge string: `"The operation couldn't be completed.
  (<Type> error <caseIndex>.)"`. Case index 0 == the FIRST declared case == `badStatus(Int)`.
- **The case thrown is `badStatus`.** The associated `Int` (the HTTP status code) is
  DISCARDED by `localizedDescription` — the message never shows which code it was.

`badStatus` is thrown in exactly two places, both on a non-200 HTTP response:
- `requestToken` — `LeakDetector.swift:410-412` (token step, the one in the message)
- `verifyToken` — `LeakDetector.swift:434` (verify step)

The message says "Token request failed" → it is the **token step**, so the throw site is
`LeakDetector.swift:412`, reached from the guard at `410-411`:
```swift
guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
    let code = (response as? HTTPURLResponse)?.statusCode ?? -1
    throw CollectorError.badStatus(code)
}
```
Conclusion of Step 1: **the collector returned a non-200 HTTP status on
`POST /api/leak/token`.** This is not a crash, not a decode error, not (by itself) a
transport failure.

## STEP 2 — Client token request, end to end

- Endpoint: `collectorBaseURL` + `api/leak/token`, method **POST**, query `zone=<baseDomain>`,
  `Content-Length: 0`. Built at `LeakDetector.swift:402-408`.
- Auth applied at `applyAuth` (`388-395`): `authToken` → `Authorization: Bearer`,
  `licenseKey` → `X-Syxx-License`. In an unlicensed build both are nil/absent.
- On non-200 → `throw CollectorError.badStatus(code)` (`412`).
- Caught by the do/catch at `LeakDetector.swift:183-194`:
  - `isTransportIdentityFailure(error)` (`282-284`) is TRUE only for `URLError.cancelled`.
    A `CollectorError` is not a `URLError`, so this is FALSE.
  - Falls through to `191-193`:
    `return .error, errorReason: "Token request failed: \(error.localizedDescription)"`.
  - `error.localizedDescription` → "The operation couldn't be completed. (…CollectorError
    error 0.)". **Exactly the screenshot string.** Reproduced by inspection.

## STEP 3 — Server side

Route table: `app.py:700-701` → `_route_leak_token(...)`.
Handler: `app.py:1582-1591`:
```python
def _route_leak_token(self, query):
    ent, ok = self._require(query, config.ENFORCE_WEB)   # 1583
    if not ok or not self._rate_ok(ent):                 # 1584
        return
    ...
    result = leak.issue_token(ent.subject, zone)          # 1589
    self._send(200, result)                               # 1591
```
Gate: `_require` (`app.py:573-588`). For an anonymous/unlicensed caller with
`enforce=True`: no entitlement, no transient error → `app.py:585-588` emits
**HTTP 402** `"payment required: activate a Syxx Pro license"` and returns `ok=False`,
so the handler returns at line 1585 WITHOUT issuing a token.

`config.ENFORCE_WEB = True` — hardcoded, `config.py:314` (not env-overridable, unlike
ENFORCE_NATIVE/ENFORCE_DOH). So on a production collector, an unlicensed
`/api/leak/token` ALWAYS gets 402.

`leak.issue_token()` (`leak.py:145`) is never reached in the unlicensed case.

NOTE: prior agent cited the gate at ~app.py:1497-1502; the live line is **app.py:1583**
(gate) / **585-588** (402 emit). Line refs had drifted; the mechanism is unchanged.

### 402 → screenshot mapping (verified by code path)
Server 402  →  client guard `410` sees `statusCode != 200`  →  `throw badStatus(402)`
(`412`)  →  catch `183` , not transport-identity  →  `.error` +
"Token request failed: …CollectorError error 0." (`191-193`). The `402` value is thrown
away at the boundary; the UI shows an opaque enum description. **Matches the screenshot.**

## STEP 4 — Does the client distinguish 402 from transport failure? NO.

- No `402` / `payment` / `requiresPro` / paywall handling exists anywhere in
  `macOS/Sources/` (grep: zero hits). `badStatus(Int)` carries the code at
  `382`, `412`, `434`, but **nothing ever inspects the associated Int.**
- Every non-200 (402 paywall, 403, 429 rate-limit, 500 server error) collapses into the
  SAME `.error` verdict with the SAME opaque string. The only status the client separates
  out is `URLError.cancelled` (pin failure), and only because it is a different Swift type.
- This violates honesty-spine #3 (tri-state, never collapse distinct states): "you are not
  licensed" and "the network/server failed" require different user outcomes and are rendered
  identically. It is a real CODE DEFECT independent of the licensing decision.

## OTLP / OTEL cert confound — UNRELATED to this response

Server OTLP exporter fails every export with
`CERTIFICATE_VERIFY_FAILED … 'bugsmasher.syxx.app'`. That is span EXPORT (background batch),
not the request path. Two independent reasons it cannot affect the token response:
1. In the unlicensed case, `_require` returns at `app.py:1584-1585` BEFORE any span is
   created (span creation is `1587`). The OTEL code is never even entered for a 402.
2. Even on the 200 path, spans are created/ended locally (`1587-1590`) and the response is
   sent by `self._send(200, result)` (`1591`) independent of whether the batch exporter can
   later reach the collector. A failed export cannot change an HTTP status code.
→ The cert failure is **unrelated noise** to this failure. Do not chase it here.

## VERDICT

**PAYWALL WORKING AS DESIGNED BUT SURFACED DISHONESTLY**, layered over a real
**CODE DEFECT** in the client.

- Server: correct. 402 on unlicensed `/api/leak/token` is the intended paywall
  (`app.py:1583`, `585-588`; `config.py:314`). Not a server defect. Not a crash.
- Client CODE DEFECT: `macOS/Sources/SyxxKit/LeakDetector.swift`
  - `410-412`: captures the status `code` into `badStatus(code)` but the value never
    reaches the user.
  - `191-193`: collapses ALL non-200 statuses into one `.error` with a raw Swift enum
    description; a 402 paywall is indistinguishable from a network/server failure.
  - Net user-visible harm: an unlicensed user is told "operation couldn't be completed /
    CollectorError error 0" instead of "this feature requires Syxx Pro." Distinct states
    (unlicensed vs. failed) are collapsed — an honesty-spine #3 violation.

This is NOT a false-secure/false-success (it shows ERROR, not clean), so it does not trip
honesty-spine #2/#4. But it is a dishonest/uninformative surfacing of a distinct state.

## Blast radius (to verify, not yet confirmed)
- Windows twin `Syxx.Kit` LeakDetector: likely the same collapse — NOT YET CHECKED.
- Verify step `verifyToken` (`434`) has the identical `badStatus` collapse; a 402/404/500
  on verify would surface the same opaque string.
- Any other Kit HTTP client that only checks `statusCode == 200` inherits the pattern.

## Disposition
Read-only per instructions — NOT fixed here. Hand to `syxx-neural-executor` /
`syxx-neural-surfacer`: map `badStatus(402)` to a distinct "requires Pro" outcome (client
CODE + Stage-5/UI surfacing), and audit `verifyToken` + the Windows twin for the same
collapse. Do NOT bypass the paywall — the 402 is correct; only its surfacing is wrong.
What would settle the Windows blast radius: read `Windows/**/Syxx.Kit/**LeakDetector*`.
