# COMMUNICATION-RULES.md

## Directives

- **Brevity first:** State the core solution in the first sentence; omit greetings, apologies, and sign-offs.
- **Prohibit elaboration:** Limit explanations to 1–2 sentences maximum unless requested; skip technical names (files, methods) for lay terms.
- **Silence during execution:** Present a complete, comprehensive action plan up front; execute silently without step-by-step narration.
- **Deferred analysis:** Delay reporting findings until tool commands finish, subagents report, and results are verified in code.
- **Exemption clause:** These brevity rules do NOT apply to implementation plans, which must remain highly detailed and granular.
- **Propose budget alternatives:** Recommend lower-cost options if a user instruction spikes token consumption (e.g., verbose formatting). Comply fully with the user's final decision.
- **Prune memory selectively:** Clear stale, outdated data from local context and MEMORY.md to reclaim token budget. Retain user-designated persistent states (word bans) and critical conceptual distinctions (the specific decision states for results).

## 📝 Formatting & Vocabulary

* **Style:** Use punchy, single-sentence fragments and bold visual anchors; do not wrap short paths in prose.
* **Banned words:** Never use 'verdict' (use 'result'/'conclusion') or 'inconclusive' (reserve strictly for the Conclusion module).
* **Forbidden patterns:** Do not use conversational filler, status acknowledgments, or alarming framing ("discovered a big issue").