// Autonomous feature-factory (Workflow version, budget-driven, in-model).
//
// Pipes feature ideas through: spec -> implement -> debug-verify(loop), one feature
// at a time, and KEEPS GOING until the token budget is nearly spent — the in-model
// analogue of "run until usage runs out."
//
// Workflow scripts cannot read files, so pass the CSV LINES in via `args` (one per element):
//     Workflow({ scriptPath: ".../feature-loop.workflow.js",
//                args: ["1. Feature A", "a. detail x", "b. detail y", "2. Feature B", ...] })
// The script groups them: a line starting with a NUMBER ("1.", "2)") starts a feature;
// following lines (typically "a.", "b.", ...) are folded in as its details until the next
// number line. (You may also pass already-grouped whole-feature strings; each becomes one feature.)
//
// Give the turn a token target (e.g. a "+2m tokens" directive) so budget.total is set;
// without a target the loop runs until the CSV is exhausted or the agent cap is hit.

export const meta = {
  name: 'feature-factory',
  description: 'Pipe CSV feature ideas through spec -> implement -> debug-verify until the token budget runs low',
  phases: [
    { title: 'Spec' },
    { title: 'Implement' },
    { title: 'Debug-verify' },
  ],
}

// Group raw CSV lines: a NUMBER-prefixed line ("1.", "2)") starts a feature; every
// following line is folded in as a detail until the next number line. Already-grouped
// whole-feature strings pass through as one feature each.
function groupFeatures(lines) {
  const feats = []
  let cur = null
  for (const raw of lines) {
    const t = String(raw).replace(/\r$/, '').trim()
    if (!t) continue
    if (/^\d+[.)]/.test(t)) { if (cur) feats.push(cur); cur = t }
    else if (cur) { cur += '\n' + t }
    else { cur = t }                 // detail before any number -> its own feature
  }
  if (cur) feats.push(cur)
  return feats
}

const features = groupFeatures(Array.isArray(args) ? args : (args ? [String(args)] : []))
if (!features.length) {
  log('No features in args. Pass CSV lines as args: ["1. Feature A","a. detail","2. Feature B", ...]')
  return { built: [], note: 'no-input' }
}

const RESERVE = 120_000        // don't START a new feature under this many tokens left
const MAX_DEBUG_ROUNDS = 4     // cap the fix loop per feature

const SPEC_SCHEMA = {
  type: 'object', required: ['spec', 'acceptance'],
  properties: { spec: { type: 'string' }, acceptance: { type: 'array', items: { type: 'string' } } },
}
const VERDICT_SCHEMA = {
  type: 'object', required: ['green', 'summary'],
  properties: { green: { type: 'boolean' }, summary: { type: 'string' },
                remaining: { type: 'array', items: { type: 'string' } } },
}

const built = []
let idx = 0

while (idx < features.length) {
  if (budget.total && budget.remaining() < RESERVE) {
    log(`budget low (~${Math.round(budget.remaining() / 1000)}k left) — stopping after ${built.length} feature(s)`)
    break
  }
  const feature = features[idx]
  log(`feature ${idx + 1}/${features.length}: ${feature}`)

  // 1) SPEC — stands in for /team-feature
  const spec = await agent(
    `Develop a crisp implementation spec and a short acceptance-criteria list for this feature: "${feature}". Return the spec text and the acceptance criteria.`,
    { phase: 'Spec', label: `spec:${idx + 1}`, schema: SPEC_SCHEMA },
  )
  if (!spec) { log(`spec failed for feature ${idx + 1} — skipping`); idx++; continue }

  // 2) IMPLEMENT — stands in for /team-composition + build. Worktree-isolated so each
  //    feature's edits don't collide. general-purpose agent has write/edit/bash.
  await agent(
    `Implement this feature in the repository, WITH tests, leaving no TODOs.\nSPEC:\n${spec.spec}\nACCEPTANCE:\n- ${spec.acceptance.join('\n- ')}\nMake sensible decisions and proceed.`,
    { phase: 'Implement', label: `impl:${idx + 1}`, agentType: 'general-purpose', isolation: 'worktree' },
  )

  // 3) DEBUG-VERIFY loop — stands in for /team-debug, looping until green or the cap.
  let green = false, lastSummary = ''
  for (let round = 1; round <= MAX_DEBUG_ROUNDS; round++) {
    if (budget.total && budget.remaining() < RESERVE / 2) { log('budget nearly gone mid-debug — stopping'); break }
    const v = await agent(
      `Run the test suite and adversarially hunt for bugs in the feature "${feature}" (acceptance: ${spec.acceptance.join('; ')}). Fix the CODE — never weaken, skip, or delete tests — until the suite is green. Report whether it is green and what (if anything) still fails.`,
      { phase: 'Debug-verify', label: `debug:${idx + 1}.r${round}`, agentType: 'general-purpose', schema: VERDICT_SCHEMA },
    )
    lastSummary = v?.summary ?? 'no verdict'
    if (v?.green) { green = true; break }
    log(`feature ${idx + 1} still red after round ${round}: ${lastSummary}`)
  }

  built.push({ feature, green, summary: lastSummary })
  const left = budget.total ? `${Math.round(budget.remaining() / 1000)}k left` : 'no budget cap'
  log(`feature ${idx + 1} ${green ? 'DONE (green)' : 'BLOCKED'} — ${built.length} built, ${left}`)
  idx++
}

return { built, processed: idx, remaining: features.slice(idx) }
