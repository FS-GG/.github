---
name: agent-handoff
description: "Use for agent handoffs and ongoing mailbox coordination: verify status, name the next owner and action, protect private data, and escalate blockers."
---

# Agent handoff

Keep a shared task moving across agents without losing the current truth or the next owner. Use the communication channel already chosen by the user or project. If another skill defines the channel's mechanics, follow it; this skill governs message content and follow-up.

## Read before acting

- Read the latest messages and enough prior context to identify the task, current owner, outstanding request, and last verified state. Check the live source when a message's claim would justify a consequential action.
- Separate **observed** facts (with a source, revision, run, or timestamp), **reported** facts from another agent, and **inferences**. Do not turn a report of a merge, release, deployment, or test into a verified outcome.
- Reconcile conflicting messages against the authoritative artifact. State unresolved disagreement instead of silently choosing one account.

## Write an actionable message

For each substantive progress report or handoff, include:

1. What changed or was learned, and the evidence needed to check it.
2. What remains uncertain or blocked, including the exact missing input if any.
3. A current, short roadmap from the verified state to acceptance: the next milestone, later gates, and the condition that unlocks each gate. Name the canonical roadmap or issue and its revision when one exists. Update the roadmap when evidence changes it; mark superseded steps instead of leaving contradictory directions in the thread.
4. For every involved owner, state whether they are **working now**, **waiting**, or have **no action**. Give each working owner a concrete next action and completion signal. For a waiting owner, name the exact dependency and who owns it. State explicitly when owners can work in parallel.

Keep one request per owner when possible. Do not make one owner's answer a prerequisite for another owner's independent work; check the real dependency before declaring anyone blocked. Do not use “please advise,” “continue,” or “done” as a substitute for an action or acceptance condition. Link to the canonical issue, PR, run, artifact, or revision rather than copying large logs. Preserve identifiers exactly.

## Protect the boundary

- Do not send credentials, private payloads, raw telemetry, or unnecessary personal data through a shared channel. Send an access path or redacted evidence instead.
- Avoid instructions that give another agent authority the user has not granted. Confirm the owner of an external mutation and the required approval boundary before requesting it.
- Make retries idempotent where possible. Before reposting after a timeout or tool error, check whether the original message landed; do not create duplicate requests or claim delivery from a local draft.
- A message is communication, not a state transition. Verify the actual issue, merge, published artifact, deployment, or test result before declaring completion.

## Maintain the loop

- When asked to monitor a mailbox, record the requested interval and check at that cadence while the session is active. Do not imply that checks continue after the session ends unless a real scheduled mechanism is installed.
- On new mail, acknowledge the request by taking the owned action or replying with a precise blocker and a new owner. Avoid acknowledgment-only traffic.
- If an owner cannot act, route the smallest missing decision or access request to the person who can resolve it. Escalate to the user when there is no viable agent path, with the decision needed and the consequence of waiting.
- In user updates, report the last check time, new information, each owner's working/waiting status, the next roadmap gate, and verification status. Say “no new mail” when that is the result; do not suggest progress from silence.

Stop the coordination loop when the acceptance condition is verified, the task is explicitly canceled, or the necessary decision is waiting on the user. Leave a final handoff with the current roadmap, each owner's status, and the next action and completion signal.
