# Swift Style Guide

Authored for this repo — the Conductor template set ships C#, Python, Go, TypeScript, JavaScript,
Dart, HTML/CSS, and general, but no Swift, and Swift is one of the two primary languages here.

Derived from `.swiftlint.yml` and from conventions observed in `macOS/Sources/SyxxKit`.

## Baseline

Swift tools 6.0, targeting macOS 14+. Strict concurrency is in effect — `@Sendable` conformance
problems are compile errors, not warnings, and a fix landed for exactly this in the Warp test suite.

Enforced by `.swiftlint.yml`:

| Rule | Level | Why it is set that way |
|---|---|---|
| `force_unwrapping` | **error** | — |
| `force_cast` | **error** | — |
| `force_try` | **error** | — |
| `implicitly_unwrapped_optional` | warning | — |
| `line_length` | warn 200 / error 300 | Deliberately loose; long descriptive lines are fine |
| `function_body_length` | warn 100 / error 200 | — |
| `cyclomatic_complexity` | warn 20 / error 35 | — |

The config's own header states the reason for the first three: *"Enforces safety rules critical to
the tri-state honesty protocol."* That is the whole rationale. A force-unwrap turns a missing
measurement into a crash; the product's contract is that a missing measurement becomes `untested`
and says so. The lint rule is not hygiene here — it is the thesis, enforced mechanically.

## `try?` is the sharpest edge in this codebase

`SyxxKit` currently uses `try?` 85 times against 57 `throws`. `try?` converts a *specific failure*
into `nil`, and downstream `nil` is usually read as **absent** rather than **failed to measure**.
Those two must never collapse into each other — that collapse is precisely the false-clean class
already recorded as an open finding, where enumeration caps stopped and recorded *nothing*, so a
250-asset page produced a full match over the 200 that were checked.

```swift
// Loses the reason. The caller cannot distinguish "no record exists"
// from "the query failed", and will usually assume the former.
let record = try? resolver.lookup(name)

// Preserves it. The caller must decide, and the conclusion can degrade honestly.
do {
    let record = try resolver.lookup(name)
    ...
} catch {
    return .inconclusive(reason: .lookupFailed(error))
}
```

Use `try?` only where the failure genuinely carries no information the conclusion depends on, and
say so in a comment when it is not obvious. Anywhere a conclusion is derived, prefer `throws` or an
explicit `Result`.

## Access control

`SyxxKit` is a library, so `public` is expected — but 196 `public` against 7 `private` means access
control is not currently narrowing the surface. For new code, default to `internal` and promote to
`public` only when a target outside the module needs it. A smaller public surface is what lets the
twin-parity contract stay stable while internals change.

## Twin-parity rules

These apply to any type with a `Syxx.Kit` counterpart.

- **Wire strings are the contract; symbol names are not.** Rename Swift types freely. Never change
  an emitted string without changing its C# twin in the same commit — `cross-platform-parity-gate.py`
  matches on emitted string sets precisely so renames stay cheap and wire drift stays impossible.
- **Sort before emitting.** Dictionary and Set iteration order differs between the runtimes. Any
  collection that reaches output gets an explicit, stable sort. The C# twin uses
  `StringComparer.Ordinal`; match ordinal semantics, not locale-aware ones.
- **Beware Swift's canonical string equality.** Swift `==` on `String` treats canonically equivalent
  grapheme clusters as equal; C# `==` is ordinal code-unit comparison. For anything derived from a
  domain name or wire bytes, compare explicitly rather than relying on `==` matching the twin.
- **Hash raw bytes, never decoded ones.** `URLSession` decompresses transparently; the C# twin
  historically did not. Any hash taken for comparison must be over the bytes as received.
- **Time is 100ns ticks on the C# side and a `Double` of seconds on this one.** Pin the clock in
  anything comparable, as `ParityTranscript.swift` does.

## Naming

- **Observe the banned word** — see `conductor/product-guidelines.md`. Use `result` for identifiers,
  `conclusion` for user-facing and module names; `Conclusion.swift` is correct. One pre-existing
  signed wire key under `evidence.comparisonResult.*` retains the banned segment and must not be
  renamed; do not introduce it anywhere else.
- Match the C# twin's file name where a twin exists — `DNSSECVerifier.swift` ↔ `DNSSECVerifier.cs`
  makes the parity surface greppable.

## Comments

Comment the *mechanism*, not the intent. A comment claiming a property the code does not have is
worse than no comment: `ContentWarp`'s comment asserted "never a silent drop" while the enumeration
cap did exactly that, and the comment is part of why it survived. If a comment states a guarantee,
something must enforce it.
