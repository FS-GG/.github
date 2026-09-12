# O2 source-composition qualification receipts

Historical source receipts retained from the [standalone telemetry roadmap](../roadmaps/2026-09-09-190726-standalone-telemetry-host-and-orchestration.md).
These do not establish artifact publication, installed acceptance or live/reboot qualification.

  Production inspection on September 11 established that an adapter factory did not compose the Host:
  durable execution persistence, runnable executor transport, input/candidate implementations and the
  production seven-effect driver were required. This same S2 source window completed in dependency order:

  - S2a — Source delivered in [Coordination PR #370](https://github.com/FS-GG/FS.GG.Coordination/pull/370),
    exact head `9c36f7cba396b4ba5cbf26547ea0fce3fd06cd1e`, protected-main merge
    `7e2501f1a6b416e33cc471527e7752e0b5da4f19` at September 11 18:46 UTC. Native readback
    confirmed identical candidate and merged trees. Required
    [qualification run 34633843218](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/34633843218)
    passed, including real PostgreSQL concurrency, restart, backup/restore and original-runtime expiry.
    Routine delivery reported `current` with coherent validation `not-required`; optional
    [optimistic run 34633843371](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/34633843371)
    subsequently passed. No artifact publication or installation
    is implied. Coordination implements the transactional PostgreSQL execution journal, explicit bounded
    execution command/receipt/content codecs and subscription admission/reservation/settlement.
    Preserve existing runner `/1` and execution-launch `/2` meanings. Record commands before exposing
    them; fence identity, generation, revision and original limits. Unknown tokens/cost are not zero.
    Real PostgreSQL restart, concurrent append, lost-response and backup/restore tests must preserve
    the original attempt and pending effects. The selected subscription policy is one nonrenewing
    attempt with a 30-minute deadline/runtime bound, not a rewrite of an existing permit.
  - S2b — Source delivered in [Coordination PR #371](https://github.com/FS-GG/FS.GG.Coordination/pull/371),
    exact head `e815659121a0cda91058e8ebac3b95a0c5f8b7df`, protected-main merge
    `c28c0777a6d5c68c6d118d4b5f16d722636b9fcf` at September 11 20:06 UTC. Native merge readback,
    [bootstrap qualification](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/34640178301)
    and [coherent validation](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/34640178299)
    passed; routine delivery reported `current` and coherent validation `passed`. Twenty-six focused
    tests passed on the final head. Four actual locally published-executable fixtures cover long-session
    readiness, crash/restart before and after process creation, duplicate-launch refusal and responsive
    cancellation. Earlier local Release qualification passed 607 architecture and 372 unit tests;
    final-head hosted qualification passed independently. Postmerge runs
    [34642480216](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/34642480216) and
    [34642480141](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/34642480141) both passed.
    Local package qualification is not external publication or installed acceptance.
    Coordination adds an executor mode to the existing runner artifact, preserving its `post`
    mode. A fixed workspace, digest-addressed input reader and real Git candidate inspector verify
    baseline, allowed changes, head/tree and reproducible candidate objects. Bounded framed transport
    remains responsive to cancellation. Executable fixtures prove duplicate-process exclusion,
    crash/reconnect ambiguity, malformed/oversized traffic, candidate tampering and stale refusal;
    missing container state never proves the previous process did not run.
  - S2c — Source delivered in [Coordination PR #372](https://github.com/FS-GG/FS.GG.Coordination/pull/372),
    exact head `f00305b592e44e817a55146f4d0aacdfba3fd2c1`, protected-main merge
    `59e7521d324fe2c7af2aa10a77c2861238f7bedd` at September 11 22:55 UTC. Native merge readback,
    [bootstrap qualification](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/34653800639)
    and [coherent validation](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/34653800717)
    passed; routine delivery waited for the `current` coherent result. Focused qualification passed
    Host 30, Execution 15, packaged-executor 26, Core 372 and real PostgreSQL execution 21 tests;
    the final compatibility repair also passed nine PostgreSQL pilot tests. The composed journey
    enters bearer-authenticated admission, restarts a fresh Host actor graph after durable claim
    settlement before continuation, recovers from PostgreSQL without duplicate claim, and completes
    all seven effects using a Release packaged runner and deterministic external GitHub transport.
    This proves source composition and actor-graph restart recovery, not a live GitHub/model run or
    the later operational host-reboot proof. Coordination composes Main's actor, durable journal, remote execution provider and actual
    seven-effect Host driver, including separately authorized GitHub callbacks and durable candidate
    readback. An executable Host/PostgreSQL/runner test with deterministic external GitHub responses
    must enter through supported admission and complete/recover the route without preloaded success.
    Preserve startup pause, fresh reconciliation, exact protected checks and native delivery identity.
