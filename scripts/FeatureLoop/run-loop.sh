#!/usr/bin/env bash
#
# Autonomous feature-factory loop (bash driver).
# For each FEATURE GROUP in a CSV, drive Claude Code (headless) through:
#   1) develop the feature spec        -> /team-feature
#   2) compose a team + implement       -> /team-composition
#   3) debug until the tests pass       -> /team-debug (externally gated on TEST_CMD)
# then advance to the next feature. Runs until the CSV is exhausted, a usage/rate
# limit is hit (claude exits non-zero), or you `touch .stop`.
#
# CSV FORMAT (grouping):
#   A line beginning with a NUMBER (e.g. "1." or "2)") STARTS a new feature.
#   Every following line — typically lettered "a.", "b.", "c." — is a DETAIL of that
#   feature and is folded into it, until the next number line begins the next feature.
#     1. Feature A
#     a. Feature A should do X
#     b. Feature A should behave like Y
#     c. Feature A should interact using input Z
#     2. Feature B
#     ...
#
# Usage:   ./run-loop.sh          Resume: rerun (remembers in .feature-cursor)
# Stop:    touch .stop            (halts gracefully after the current feature)
#
set -uo pipefail

# ----------------------------- config (override via env) -----------------------------
CSV="${CSV:-features.csv}"
STATE="${STATE:-.feature-cursor}"          # next FEATURE index to process (resume point)
LOG="${LOG:-build-loop.log}"
STOP="${STOP:-.stop}"
TEST_CMD="${TEST_CMD:-}"                   # e.g. "npm test" / "pytest -q". EMPTY = trust the model's debug loop
PERMISSION_MODE="${PERMISSION_MODE:-acceptEdits}"   # acceptEdits | plan | default | dangerously-skip
MAX_DEBUG_ITERS="${MAX_DEBUG_ITERS:-5}"    # cap the fix loop per feature
MAX_FEATURES="${MAX_FEATURES:-0}"          # 0 = no cap; else stop after N features this run
TIME_BUDGET_SECS="${TIME_BUDGET_SECS:-0}"  # 0 = no cap
BRANCH_PER_FEATURE="${BRANCH_PER_FEATURE:-1}"
export CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS=1
# -------------------------------------------------------------------------------------

log(){ printf '%s  %s\n' "$(date +%H:%M:%S)" "$*" | tee -a "$LOG"; }

claude_run(){  # $1 = prompt ; returns claude's exit code
  local flags=(--print)
  if [ "$PERMISSION_MODE" = "dangerously-skip" ]; then flags+=(--dangerously-skip-permissions)
  else flags+=(--permission-mode "$PERMISSION_MODE"); fi
  claude "${flags[@]}" "$1" >>"$LOG" 2>&1
}

trim(){ printf '%s' "$1" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//'; }
is_feature_start(){ printf '%s' "$1" | grep -qE '^[0-9]+[.)]'; }   # "1." or "2)" starts a feature

command -v claude >/dev/null || { echo "claude CLI not found in PATH"; exit 2; }
[ -f "$CSV" ] || { echo "no CSV at $CSV"; exit 2; }

# ---- parse CSV into feature groups (number line + its detail lines) ----
declare -a FEATURES=()
declare -a TITLES=()
current=""; title=""
while IFS= read -r raw || [ -n "$raw" ]; do
  line="$(trim "${raw%$'\r'}")"          # strip CR (CRLF) + surrounding space
  [ -z "$line" ] && continue
  if is_feature_start "$line"; then
    [ -n "$current" ] && { FEATURES+=("$current"); TITLES+=("$title"); }
    current="$line"; title="$line"
  else
    if [ -n "$current" ]; then current="$current"$'\n'"$line"
    else current="$line"; title="$line"; fi   # detail before any number -> its own feature
  fi
done < "$CSV"
[ -n "$current" ] && { FEATURES+=("$current"); TITLES+=("$title"); }

total=${#FEATURES[@]}
[ "$total" -gt 0 ] || { echo "no features parsed from $CSV"; exit 2; }
start=$(cat "$STATE" 2>/dev/null || echo 1)
done_count=0
log "parsed $total feature(s); starting at #$start (perm=$PERMISSION_MODE, test='${TEST_CMD:-<model-gated>}')"

for (( n=start; n<=total; n++ )); do
  [ -f "$STOP" ] && { log "stop file present — halting at #$n"; echo "$n" >"$STATE"; rm -f "$STOP"; exit 0; }
  [ "$MAX_FEATURES" -gt 0 ] && [ "$done_count" -ge "$MAX_FEATURES" ] && { log "MAX_FEATURES reached"; echo "$n" >"$STATE"; exit 0; }
  [ "$TIME_BUDGET_SECS" -gt 0 ] && [ "$SECONDS" -ge "$TIME_BUDGET_SECS" ] && { log "time budget reached"; echo "$n" >"$STATE"; exit 0; }

  feature="${FEATURES[$((n-1))]}"       # full group: title line + detail lines (multiline)
  title="${TITLES[$((n-1))]}"
  log "=== [#$n/$total] $title ==="

  if [ "$BRANCH_PER_FEATURE" = 1 ] && git rev-parse --git-dir >/dev/null 2>&1; then
    git switch -c "feat/$n" 2>/dev/null || git switch "feat/$n" 2>>"$LOG" || log "  (branch switch skipped)"
  fi

  # ---- phases 1 + 2: develop spec, compose team, implement ----
  if ! claude_run "Build ONE feature end-to-end, non-interactively. Do not ask questions; make sensible decisions and proceed.
The feature and ALL of its detail lines (each detail is a requirement that is part of this same feature):
---
$feature
---
Run in order:
1) /team-feature  — turn the above (title + every detail line) into a clear spec + acceptance criteria.
2) /team-composition — organize an implementation team, plan, and implement the build WITH tests, satisfying every detail line.
Finish by printing 'BUILT: <one-line summary>'."; then
    log "usage/limit or error at #$n (exit $?) — cursor saved, stopping"; echo "$n" >"$STATE"; exit 1
  fi

  # ---- phase 3: debug loop, externally gated on TEST_CMD when provided ----
  round=0
  while : ; do
    if [ -z "$TEST_CMD" ]; then
      claude_run "/team-debug Identify and fix any issues in the feature just built (title + details):
---
$feature
---
Loop until the tests are green (fix code, never weaken tests), then print 'GREEN'." || {
        log "usage/limit during debug at #$n — cursor saved, stopping"; echo "$n" >"$STATE"; exit 1; }
      break
    fi
    if eval "$TEST_CMD" >>"$LOG" 2>&1; then
      log "  tests GREEN for #$n after $round debug round(s)"; break
    fi
    round=$((round+1))
    if [ "$round" -gt "$MAX_DEBUG_ITERS" ]; then
      log "  BLOCKED: #$n still failing after $MAX_DEBUG_ITERS rounds — left on branch feat/$n, moving on"; break
    fi
    log "  tests RED for #$n — debug round $round"
    claude_run "/team-debug The test command '$TEST_CMD' is failing for the feature (title + details):
---
$feature
---
Diagnose with competing hypotheses and fix the CODE until '$TEST_CMD' passes. Do NOT weaken, skip, or delete tests." || {
      log "usage/limit during debug at #$n — cursor saved, stopping"; echo "$n" >"$STATE"; exit 1; }
  done

  done_count=$((done_count+1))
  echo "$((n+1))" >"$STATE"   # advance + persist resume point
  log "  feature #$n done ($done_count this run)"
done

log "all $total features processed"
