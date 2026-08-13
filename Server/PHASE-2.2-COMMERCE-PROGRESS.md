# Phase 2.2 commerce gaps — progress

Scope: `Server/syxx_server/**`, `Server/tests/**` only. No `.html` edits. No client edits.
Nothing committed.

## Suite

| | Result |
|---|---|
| Before | **332 passed, 1 failed**, 38 subtests |
| After | **366 passed, 0 failed**, 38 subtests |

The brief said 333 passing. It was 332 passed + 1 failed. The failure was
`tests/test_doh.py::JsonHonestyTests::test_upstream_failure_is_status_2_not_a_fake_clean`,
deterministic and reproducible in isolation, pre-existing, out of scope. I never touched it.
It is green in the final run because a CONCURRENT agent changed that code mid-session (see
hazard below), not because of anything I did.

The count does not reconcile exactly (332 + 35 of mine = 367, observed 366) because another
agent was editing `Server/syxx_server/**` and `Server/tests/**` throughout the session.

## State sweep vs the brief

| # | Brief said | Actual |
|---|---|---|
| 1 | `/api/billing/portal` absent | **PRESENT** already. Route `app.py:720`, handler `app.py:975`, Stripe call `stripe_checkout.py:109`. Absent: any test; a body drain; and it used a SUBSTRING match to pick the customer. |
| 2 | refund/dispute unhandled | **PARTLY present.** Logged only, no revocation, no idempotency. Brief's impact claim correct. |
| 3 | `trial_period_days` absent | CONFIRMED ABSENT. |
| 4 | machine binding absent | CONFIRMED ABSENT. |

## Changes

1. **Billing portal** — `app.py` `_route_billing_portal`: drain+discard the POST body (was never
   read, so a body desynced keep-alive); documented that the customer id is derived from the
   session only. `db.kv_find_by_value_prefix` made an EXACT match (was substring: decides whose
   billing portal opens).
2. **Refund/dispute** — `fulfillment.py`: `_revoke_for_chargeback`, idempotent via the EXISTING
   `db.kv_insert_once("stripe_evt:"+id)` scheme. Three-valued outcome
   `revoked | revoke-failed | revoke-unresolved`. Local API keys revoked even when Keygen cannot
   confirm (fail closed). New `db.keygen_subject_for_license_id`. New `stripe_pi:` map so a
   dispute (which carries no `customer`) can resolve. `charge.dispute.closed/updated` stay
   log-only: a dispute can be WON.
3. **trial_period_days** — `config.STRIPE_TRIAL_DAYS` (`SYXX_STRIPE_TRIAL_DAYS`, default **0**),
   applied only when the price-derived mode is `subscription`, and stamped into
   `metadata[trial_days]`.
4. **Machine binding** — `config.KEYGEN_MAX_MACHINES` (`SYXX_KEYGEN_MAX_MACHINES`, default **5**
   to match `Server/index.html:513`), sent as `maxMachines` on every mint.

## Mutation proofs (all confirmed RED, then restored)

M1 idempotency claim removed; M2 suspend never called; M3 unresolved returned as "revoked";
M4 local key revoke skipped; M5/M5b body-supplied customer id honoured; M6 session check dropped;
M7 exact match reverted to substring; M8 trial param dropped; M9 subscription_data on a one-time
session; M10 maxMachines dropped; M11 limit set to 3 (contradicts the EULA); M12 signature compare
disabled; M13 route processes an unverified event.

## Open, NOT done by me

- No automatic reinstatement after a WON dispute (suspended until an operator acts).
- Machine binding is DECLARED, not ENFORCED (no client activates a machine; Keygen policy
  override not verifiable from here). Two-sided change; client half not in scope.
- Concurrent agent destroyed uncommitted A025/A085 work in `fulfillment.py`. See report.
