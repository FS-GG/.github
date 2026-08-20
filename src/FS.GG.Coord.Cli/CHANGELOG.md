# FS.GG.Coord.Cli historical release rationale

This file preserves the release-classification rationale that predates the bounded
`PackageReleaseNotes` history. Current and future consumer-facing release notes remain in
`FS.GG.Coord.Cli.fsproj` and on the package registry.

## 0.23.0

0.23.0 is a MINOR on the 0.x line: the `driver` events/cursor flags (.github#2135) are new
caller-visible CLI vocabulary — a flag pair, a help entry, a render mode, and a durable cursor
file format that 0.22.1 did not have. Diffing src/FS.GG.Coord.Cli/Options.fs between
coord-engine/v0.22.1 and HEAD is non-empty (Events/CursorFile flags, spellings, applicability),
so PATCH is not defensible even though Protocol.fs (the exit-code wire surface) is untouched;
this repository's 0.x policy treats new CLI vocabulary as the caller-observable-change signal
whenever there is no Major line to take. The 0.22.1 PATCH rationale follows as historical context.

## 0.22.1

0.22.1 is a PATCH on the 0.x line: #2308 bounds empty claim-marker reads so an
empty response is refused rather than treated as a known-unclaimed item. No command,
flag, result, or exit-code vocabulary changes; the fix is internal fail-closed behavior.
The 0.22.0 MINOR rationale follows as historical context.

## 0.22.0

0.22.0 is a MINOR on the 0.x line: three wire-surface commits since the 0.21.1 release
add the explicit delivery-route receipt boundary and typed intake transaction surface, while
#2300 and #2313 repair scheduler/reconcile read amplification. Receivers gain new command and
result vocabulary, so a patch would understate the caller-observable change. The remainder of
this historical note records the earlier 0.16.0 rationale.

## 0.16.0

0.15.0 to 0.16.0 is a MINOR, and under the repository's 0.x SemVer policy that is also the
caller-observable-change signal: there is no 1.x major available to take. Eight merged engine
commits ship (.github#1951); #1900's squash commit consolidates four reviewed input commits.
The cut includes a Protocol.fs wire-surface change and several independent changes
to scheduler, claim, lint, follow-up, and done-boundary behavior:

* #1915 (0b181f2) makes `lint` name consolidation candidates.
* #1921 (2065040) makes BLOCKER-CLEARED respect an in-flight implementation.
* #1901 (9d7c1a2) adds the ordered Severity ranking axis.
* #1942 (338e9aa) corrects KIT DIGEST claim advice for skill files.
* #1946 (5a93a3b) excludes decision items from the worker queue.
* #1948 (4f3ffa6) derives skill views from receiver declarations.
* #1949 (91645e2) fails closed when a marker scan is incomplete.
* #1900/#1952 (de03135, squashing f9b067c, 7d9927e, a8466c1, and 0b722c9) adds follow-up
  audit/reconciliation, makes `done` audit residual promises, and adds the durable disposition
  boundary.

Patch is not defensible: receivers gain command vocabulary, result vocabulary, refusal paths,
rank behavior, and terminal behavior. #1892 (secondary-rate-limit exit contract) and #1857
(mid-item session rotation) remain explicitly unresolved residue and are not part of this cut.
