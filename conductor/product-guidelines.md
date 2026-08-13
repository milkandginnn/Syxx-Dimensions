# Product Guidelines

## Voice and Tone

**Voice:** Friendly and approachable.

**Guidelines:**

- Explain security findings in plain, reassuring language; never alarmist.
- Prefer active voice and short sentences.
- Use consistent verdict terminology (e.g., "Tampered", "Clean", "Inconclusive") across all three clients.

## Design Principles

Security, efficacy, efficiency, simplicity.

### 1. Security First

- Fail closed on uncertainty (dev signing key gated behind `SYXX_ALLOW_DEV_UPDATE_KEY=1`).
- Verify signatures and sizes before install (Ed25519 manifest → SHA-256).
- Never expose secrets in logs or UI.

### 2. Efficacy

- Multi-vantage DNSSEC validation and tamper consensus are the product's core claims — they must be correct and covered by tests.

### 3. Efficiency

- Bound resource use (rate-limit buckets, caches) with real pruning and memory-bound tests.

### 4. Simplicity

- Clear verdicts, minimal configuration, one obvious path through the UI on every platform.

## Cross-Platform Parity

- The three desktop clients (SwiftUI / WinUI 3 / Avalonia) must behave identically.
- Contracts live in one place; duplicated constants are a known drift mechanism and must be deduplicated.
- Parity is enforced by CI parity gates.

## Standards

- Conventional Commits for all changes.
- Tests are required; never soften a test to advance faulty code.
- All changes are reviewed before merge.
- Use subagents for implementation tasks.
