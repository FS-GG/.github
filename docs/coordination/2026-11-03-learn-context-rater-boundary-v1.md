# Context-rater boundary v1

This source contribution prepares and validates frozen inputs for Delivery #5276. It does
**not** qualify a model runtime, execute ratings or establish a hardness predictor. The
[reconstruction protocol](2026-11-03-learn-context-replay-protocol-v1.md) and its held-out
selection remain unchanged. The earlier [audit](../../reports/learn-04-context-reconstruction-audit-2026-11-03.md)
established reconstruction feasibility: 13 bundles from 16 original cases, with all three
unavailable cases retained. No private case data is included here.

## Implemented boundary

[learn-context-rater.py](../../scripts/learn-context-rater.py) has five commands:

| Command | Actual behavior |
|---|---|
| `prepare` | Verify the externally frozen manifest digest, task digest, every tracked source blob's Git identity, size and SHA-256; flatten only a predeclared file plan and the canonical rubric into a bounded payload. |
| `probe` | Read local Codex version/help only; report observed flags and explicit unqualified capabilities. |
| `protocol` | Start a fresh Python validator, verify the payload seal, accept one closed final-output frame, and quarantine the validated synthetic response. This process is not a model session. |
| `inspect-wire` | Audit a captured outbound JSON request against the exact frozen payload and a closed, tools-empty candidate Responses contract; send nothing. |
| `launch` | Refuse unconditionally because no canonical context-only inference transport has been qualified. A supplied approval boolean cannot override this. |

A file plan is a closed object with `schema: "fsgg.context-file-plan/1"`,
`manifestSha256`, `treatment` and `files`. Treatments are `issue-only` (empty files)
and `frozen-context` (one to 64 unique manifest paths). Freeze each plan before viewing
outcomes, with a common selection procedure and equal input/output budgets across paired
treatments. The implementation cannot establish whether a human selected files blindly.
Missing planned paths, binary selections and excess budgets refuse rather than truncate.
Both treatments retain the same issue packet and rubric.

Each path component opens without following symlinks. Shared hardlinked inputs, traversal,
`.git` paths and changed files refuse. Validation covers even unselected manifest files,
up to 20,000 files, 4 MiB per blob and 64 MiB total source. The flattened model-visible
payload is at most 256 KiB. Controller files and unlisted files never enter the payload;
paired causal fixtures change those files and require identical allowed payload bytes.
This constrains the builder's input. It does not prevent a future inference process from
reading other files.

`fsgg.context-rater-request/1` is an **internal contract**, not a provider request or an
implemented API adapter. It declares empty tools, `toolChoice: "none"`, no previous
conversation and one payload. The broker rejects every tool/control frame without a
dispatch path. A future adapter must bind these properties to the actual provider protocol;
simply serializing this object does not enforce them.

Final judgments use the existing closed
[semantic-hardness schema](../../contracts/learn/semantic-hardness-v1.schema.json) and
[validator](../../scripts/learn-classifier-pilot.py), including exact task quotation checks.
A model's claimed effective model/effort is rejected as an extra field. The quarantine
wrapper records the requested normal profile (`gpt-6.1-sol`, `medium`) separately from
effective model, effort and evidence, which remain null/Unknown. All outputs retain
`isolationQualified: false` and `evaluationEligible: false`; no scoring or fitting follows.

## Runtime qualification gap

Local Codex 0.166.0 exposes `--ignore-user-config`, `--ignore-rules`, `--ephemeral`,
`--output-schema` and sandbox options. These reduce inherited state, but version/help
and generated app-server schemas do not prove the actual inference tool inventory or
absence of automatic context. Official [configuration documentation](https://learn.chatgpt.com/docs/config-file/config-reference)
describes tool-specific switches, including command execution; the
[security documentation](https://learn.chatgpt.com/docs/security) does not turn a read-only
filesystem sandbox into a prohibition on reading future/controller data.

A bounded local fake-provider control observed the actual Codex request using a dummy key,
private empty home/workspace, no daemon, the flags above and all discovered tool feature
switches disabled. Its top-level `tools` property was absent, yet its first input item was
an `additional_tools` developer block containing runtime tool definitions. The request used
`tool_choice: "auto"` and seven input items, including injected developer/environment context.
Thus absence of top-level tools does not establish tool absence in this runtime. Exactly one
49,698-byte local request reached a terminal synthetic HTTP 400 response; the child exited
and the local receiver retired. No real provider or model was invoked. This is a control of
that configuration, not proof that every possible Codex configuration has the same behavior.
The wire inspector rejects embedded tool definitions even when top-level tools are empty,
and rejects injected context, resume IDs, automatic tool choice and changed output schemas.

Presence-only checks found no existing OpenAI/Azure API key or configured base URL in the
worker environment; no credential values were read or reused. The located existing Responses
artifact helper is explicitly static and does not launch inference. A supported data-only
Responses adapter could avoid the Codex tool layer, but none was qualified here.

No model smoke is needed to establish this missing evidence. No synthetic provider call,
real-case call, new credential, runtime installation or experiment is authorized by this
prototype's commands. Current collaboration tools share filesystem and network access;
a fresh subagent alone does not qualify isolation.

The next bounded step is to qualify an existing supported transport that exposes the
actual outbound tools-empty request and all context inputs, with no workspace attachment,
retrieval, session reuse or hidden skill/project loading. Inspect actual request/protocol
records and enforce refusal of tool-call frames; test a synthetic forbidden-read canary.
Distinguish unavailable tools from a model voluntarily declining to use them. Establish
requested-versus-effective metadata from trusted runtime evidence or retain Unknown.
Only then consider one bounded synthetic inference and separately admit real blinded cases.
That would qualify a context mechanism, not predictor validity, cross-model invariance,
judge agreement or actual execution demand. Remaining-work estimates conditioned on supplied
state must remain distinct from total demand before implementation began.

Synthetic controls run with `python3 -m unittest discover -s tests/learn-context-rater -v`.
They cover source/path/protocol boundaries and launch refusal; they do not test provider
isolation, model accuracy or OS confinement of an inference process.
