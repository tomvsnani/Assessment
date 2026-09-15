# Ambiguous — "make link analytics retention-compliant"

| | |
|---|---|
| Preset | [`scenarios/ambiguous.yaml`](../../scenarios/ambiguous.yaml) |
| Requirement | [`requirements/ambiguous.md`](../../requirements/ambiguous.md) — `REQ-AM-1` |
| Baseline | `git:v1-legacy` |
| Human gates | `approve-spec` — **the gate that matters here** — then `approve-design`, `approve-release` |
| Policies that matter here | `pii-in-logs` (what counts as PII is part of the ambiguity), `schema-change-needs-approval` (retention usually means a purge job and a column or table) |

## 1. The requirement

> From the product owner, forwarded from Compliance:
> *Legal has flagged that we keep link analytics forever. We need the shortener's analytics to be
> compliant with our data retention obligations before the next audit. Please make the necessary
> changes and confirm what is retained and for how long.*
>
> That is the whole request. Nobody has said which regulation applies, what "analytics" covers
> (per-click records, aggregate counts, the target URLs themselves), what the retention period
> is, whether deletion must be provable, or whether anything needs to be retained *longer* for
> audit. Work out what needs deciding, propose defensible options, and get a decision before
> building.

What makes it ambiguous: every reading is plausible and the choice depends on business intent,
not engineering practice. A pipeline that "helpfully" picks one and builds it has failed the
scenario even if the code is perfect. The correct output of the first stage is a set of questions
with options and a recommendation — and a stop.

## 2. What to watch for

- **The agent refuses to guess.** [`prompts/requirements.md`](../../prompts/requirements.md) draws
  the line: business ambiguities go to the human as `AMB-n` with 2–3 options and a trade-off
  each; engineering choices are settled by the agent and listed as assumptions so the human can
  veto them without being asked. Expected ambiguities here: regulation / retention window; what
  "analytics" includes (raw clicks vs aggregates vs target URLs); provable deletion vs best-effort;
  audit hold.
- **The run stops at `approve-spec`.** `ApprovalRequested` carries `openAmbiguities: N`. The
  dashboard shows each question with its options; the human picks. Nothing downstream starts.
- **Decisions are recorded and cited.** Each choice becomes a `Decision` (`D00n`,
  `OptionChosen`, `AMB-n → X`) in the event log and the audit chain, is written back into the spec
  as `resolvedOptionId`, and the architect prompt requires the design to cite the decision id
  ("per D002"). Lineage shows the chain from decision to design to code.
- **Assumptions are visible, not silent.** The engineering choices the agent made on its own
  appear in the spec's `assumptions` list, one line of rationale each.

## 3. Run evidence

**Status: no live ambiguous run has been completed yet.** Same reason as
[brownfield](brownfield.md#3-run-evidence): the engine fixes forced by the greenfield runs were
landed first. This section is filled from `runs/ambiguous-<timestamp>/` when it runs.

The mechanism itself is exercised and evidenced already:

- **Live, on the greenfield requirement:** `greenfield-20260915-163443` raised two business
  ambiguities (`AMB-1` duplicate-URL handling, `AMB-2` code length/alphabet); the human chose
  `B` and `A`; decisions `D002`/`D003` were recorded and written back into the spec. An earlier
  run (`greenfield-20260915-161352`) also raised the redirect status code as an ambiguity —
  something the prompt says to settle as an assumption. Recorded in the engineering summary as an
  instance of the model not following its instructions.
- **Live, on an out-of-scope requirement:** the React gym-app run (`adhoc-20260915-040442`)
  is the reason the requirements analyst now raises platform fit as a blocking `AMB-1 PLATFORM:`
  ambiguity with re-scope/stop options.
- **Tests:** `GovernanceFlowTests` — a human resolves an ambiguity at approval, the resolution is
  written into the spec and cited as a decision; rejection stops the run; "revise" re-runs the
  stage with the note and does not consume the retry budget.

## 4. Validation

- The spec gate is the validation: a human reads the ambiguity list and either resolves, revises
  (sends the analyst back with a note) or rejects (stops the run). All three paths are tested.
- Downstream, `pii-in-logs` and `schema-change-needs-approval` enforce two of the likely
  consequences of the decision (what may be logged; a purge needs an approved table/column).
- The release change record must state what is retained and for how long — the compliance
  question the requirement asked to have confirmed.
