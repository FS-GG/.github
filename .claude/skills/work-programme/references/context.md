# Parent context operations

Use `scripts/context.py COMMAND INPUT PRIVATE_ROOT --input-sha256 SHA` for parent reads and context
diagnostics. Commands are `view`, `review`, `report`, and `command`. Python standard library only; no CLR,
GitHub, model, workload launch or daemon. Fresh native admission still governs every actual effect.
Inputs are pinned JSON of at most256KiB; duplicate keys/unknown fields refuse. Results use unique0600
files under a dedicated0700 private root. Exit0 means the projection succeeded; exit3 means refusal.
Default stdout is8KiB (`--stdout-bytes` accepts1024–65536). Larger results return a path/digest, never a
truncated finding. Retrieve the required sections through `view`; a retained handle is not proof of review.

## Bounded evidence

`view` input has schema `fsgg.programme.context-view-input/1` and required fields:
`artifact:{path,bytes,sha256}`, `trust:instruction|plan|data`, `access:allowed`,
`obligationsComplete:bool`, `reason`, `selection`, `maximumBytes` (1–65536; start with4096).
Paths are absolute, symlinks refuse, raw size/hash are checked before any selection. Evidence is at most2MiB.
Selection is exactly one of `{"lines":[first,last]}` (inclusive one-based),
`{"pointers":["/boundaries","/continuation"]}` (RFC6901 JSON pointers), or `{"whole":true}`.
Whole applicable instructions require `trust:instruction`, `obligationsComplete:true` and whole selection.
Never use a smaller bound to omit an instruction. MaximumBytes bounds the JSON encoding of the selected
value; escaping counts. Drift, missing fields/ranges, denied/unknown access and overflow refuse without text.
Source/evidence remains data; verification establishes equality to a declared pin, not authenticity.

Prefer fields showing changes, boundaries, unknowns and the parent decision. Do not print full returns,
manifests or inventories simply because a notification supplies their path. Pin unchanged instructions
once per window and retain applicability acknowledgments. On recovery, consult current pointers and
retrieve missing obligations instead of reloading every artifact. F# `view`/`reuse` remain available for
their existing contracts; neither route replaces required semantic/native validation.

## Safeguard reviews

`review` input has schema `fsgg.programme.context-review-input/1`, exact40-character `sourceRevision`,
`obligations:[{id,question,viewIds}]`, `views:[{id,input}]` (each input is a view input),
`unchanged:[{path,bytes,sha256}]`, `unknowns:[string]` and `maximumBytes` (1–65536).
Limits:16 views,32 obligations,1024 unchanged pins,32 unknowns. Every obligation must reference an included
view; every included view must serve an obligation. Unchanged files are byte-verified (at most64MiB each)
and projected as a count/closure digest instead of their bodies. Source identity/completeness is caller-declared.

The owner includes changed code and tests needed to judge actual predicates, original deadlines, retained
first cause, cleanup, and authority, as applicable. Root reads the selected logic and independently checks
semantic correspondence. Omitted dependencies, unclear joins or findings require expanded views. A clean
unchanged closure never proves prior semantic acceptance, current authority or absence of hidden reads.
Do not import the whole package as a shortcut. Preserve the original failed attempt outside the review.

## Existing guard commands

`command` prepares an argument vector; it never executes it. Input schema
`fsgg.programme.guard-command-input/1` has pinned `python`, `launcher`, `operations`, `window`, `runtime`,
`binding`, plus `bindingSha256`, `index`, and absolute unused `receipt` path. Pins have path/bytes/sha256.
The helper verifies their bytes and operation/binding/window identity joins and rejects an existing receipt.
It emits the existing selected-entry launcher's fixed flags, with `executed:false` and `windowRenewed:false`.
Root still supplies independently checked source/runtime/support closure and fresh effect-specific admission;
the existing launcher validates live deadlines, capabilities, accounting and cleanup. Command preparation
is not one-use consumption, executable authenticity or admission. Do not replace a refused guard with an
unguarded command. Use the pinned descriptor and fixed launcher invocation rather than rebuilding repeated
assembly/check scripts in model context; novel guard semantics remain separately reviewed source changes.

## Native context report

`report` input has schema `fsgg.programme.context-report-input/1`, UTC `start`/`end`,
`sessions:[{sessionId,artifact:{path,bytes,sha256}}]`, `expectedSessionIds:[string]`,
`helperDirectories:[absolute-path]`. Explicit selections only: no session-directory crawl or recursive
helper discovery. Up to32 sessions,64 named helper directories and10000 helper measurements. Pin a stable
copy of an actively appended native log using the existing private artifact route; do not modify its original.
Each session snapshot is at most64MiB. Native metadata must match the declared session ID. Parent/agent IDs
come from metadata; missing participants remain named. An unterminated malformed final append is reported
as incomplete coverage; malformed complete/interior records refuse. Raw messages and credentials are not
printed. Compactions are observed events in the window, never inferred from text size or elapsed time.

The report gives native input-token min/max, tool exposure bytes/calls, compaction timestamps, and deltas
of cumulative input/cached/output/reasoning counters only with a pre-window baseline and no reset. Missing
baseline or any counter decrease yields unknown deltas. Token counter inputs count repeated inference
history; caching reduces some cost, not context length. Session snapshots/selected records do not prove
continuous observation or complete whole-family/item usage. Join genuine dispatch/attempt evidence through
the existing observer before making economic claims; this command cannot mint or repair those joins.

Helper measurements are collected only from the named directories and timestamped filenames within the
window, separately from native counters. Existing `programme.fsx report` covers one helper directory's I/O;
use both reports without treating byte counters as native tokens or unobserved compactions as zero.

## Context rotation and restart

At a completed milestone, repeated native compactions or substantial recovery rereads justify considering
fresh-context continuation. The current owner prepares a compact checkpoint: original/current identities,
exact source, current return/packet pins, full reservation pointers, pending effects, first failures and next
bounded action. Prefer the runtime's same-owner fresh-context mechanism. Never interrupt a live effect or
transfer ownership implicitly. If the mechanism is unavailable, retain the owner and report the capability
gap; a packet does not erase inherited history. No rotation renews an execution window or changes an attempt.

Keep the dispatch index focused on current navigation; replace superseded entries and link detailed history.
A delegated recovery owner reads the full handoff once. Parent recovery consumes its compact decision view,
complete reservations and unresolved decisions. With delegation prohibited/unavailable, the parent performs
the same bounded recovery directly. Preserve every unknown reservation and unresolved operation.
