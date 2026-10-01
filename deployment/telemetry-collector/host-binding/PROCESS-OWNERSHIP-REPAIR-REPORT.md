# OPS-TYPED-01.5 exited-parent process ownership repair

**Status:** source repair tested on 2026-10-01. Protected recipe adoption,
Sandbox rendering, wrapper adoption, and a native attempt remain pending root
work.

This successor closes the remaining process ownership finding in
`/tmp/ops-typed-01-5-host-binding-bounded-schema-independent-review-20261001.md`,
SHA-256 `e3e190fd2cacc947db4428370139ae57571128857eecaa47567506267710f7a2`.
The reviewed predecessor `4982d2b5a77a3fccba3b97c18bb254d737b345eb`
and all earlier reports and build outputs remain unchanged.

Each bounded child now starts through the fixed Linux `/usr/bin/setsid` adapter
inside its own process group. The F# process becomes a child subreaper before
launch. Output overflow, timeout, drain failure, or a direct parent that exits
while descendants retain its pipes causes TERM then KILL within fixed cleanup
budgets, reaps adopted members, and verifies that the owned group no longer
exists. Signalling is limited to the newly created group ID; no process-name or
host-wide census is used.

The actual prior reproduction is now a test: a Python parent forks a 30-second
sleeping child that retains stdout/stderr, records its PID, and exits. The
production `runBoundedProcess` refuses, terminates and reaps that descendant, and
returns within five seconds with no `/proc/<pid>` survivor. Existing exact-bound,
stdout-flood, stderr-flood, and live-parent deadline tests remain green.

## Verification

- cold locked .NET 10.0.401 restore and Release build;
- 13 F# tests passed, including the real exited-parent/held-pipe process test;
- 36 private qualification tests passed, retaining admission framing, complete
  profile validation, and seven compiled pre-materialization probes;
- exact-head inspect/render/synthetic derive/stdin verify;
- repeat Release build digest equality;
- four immutable native input files remain byte-identical;
- Python compilation and `git diff --check`.

The CLI and workflow template interface are unchanged. No actual nonce,
admission, authentication, wrapper, runtime, container, native operation, API,
publication, push, or PR effect is used or claimed.
