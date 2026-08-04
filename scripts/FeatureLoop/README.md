# feature-factory

Two ways to run an autonomous loop that turns a CSV of feature ideas into built,
tested code — one idea at a time, over and over, until the CSV is exhausted, usage
runs out, or you stop it.

## CSV format (feature grouping)

A line starting with a **number** (`1.`, `2)`) **starts a new feature**. Every line after
it — typically lettered `a.`, `b.`, `c.` — is a **detail** of that feature and is folded in,
until the next number line begins the next feature. So one feature = its title line + all its
detail lines, handed to the build as a single unit:

```
1. Dark mode toggle
a. Detect the system color-scheme preference on first load
b. Persist the user's explicit choice across sessions
2. CSV import with column mapping
a. Let the user map source columns to target fields
b. Show a dry-run preview before committing
```

→ Feature 1 (toggle + its 2 details), then Feature 2 (import + its 2 details). The resume
cursor and `MAX_FEATURES` count **whole features**, not raw lines. See `features.csv`.

---

## 1. `run-loop.sh` — bash driver (recommended for unattended, run-until-limits)

Each iteration launches a fresh **headless Claude** (`claude --print`) that runs
`/team-feature` → `/team-composition` → `/team-debug` for one feature, then advances.

```bash
cd feature-factory
# optional but recommended: gate "done" on your real test suite
TEST_CMD="npm test" ./run-loop.sh
```

- **Resume:** remembers its place in `.feature-cursor`; rerun to continue.
- **Stop:** `touch .stop` (halts gracefully after the current feature).
- **Isolation:** each feature lands on its own git branch `feat/<n>` (set `BRANCH_PER_FEATURE=0` to disable).
- **Guards:** `MAX_DEBUG_ITERS` (fix-loop cap), `MAX_FEATURES`, `TIME_BUDGET_SECS`.
- **Permissions:** defaults to `--permission-mode acceptEdits`. `PERMISSION_MODE=dangerously-skip`
  auto-approves *everything* (incl. arbitrary Bash) — powerful and risky; only on a repo you can reset.
- Stops automatically when `claude` exits non-zero (a usage/rate limit), saving the cursor.

If `TEST_CMD` is empty it trusts the model's own debug loop; **set it** so "loop until no bugs"
is gated on a real green suite, not the model's say-so.

## 2. `feature-loop.workflow.js` — Workflow version (in-model, budget-aware)

Runs as one long in-model job that pipelines each feature through
spec → implement → debug-verify and keeps going **until the token budget is nearly spent**.
Workflow scripts can't read files, so pass the CSV rows via `args`:

```
Workflow({ scriptPath: "<abs path>/feature-loop.workflow.js",
           args: ["idea 1", "idea 2", "idea 3"] })
```

Give the turn a token target so `budget.total` is set (that's what makes it stop at the
right point). Each feature is worktree-isolated; the debug loop caps at `MAX_DEBUG_ROUNDS`.

---

## Reality check (read before letting it run unattended)

- **Slash commands aren't CLI verbs** — they only exist inside a Claude session. The bash driver
  passes them as prompts to `claude --print`; the Workflow version re-encodes their behavior as `agent()` calls.
- **"Until no bugs" is undecidable from outside** — always cap iterations and gate on a real test command.
- **Cost is unbounded by design.** Use `MAX_FEATURES` / `TIME_BUDGET_SECS` (bash) or the token budget (workflow).
- **No human between features** → quality drifts. Branch-per-feature + review later beats auto-merge.
