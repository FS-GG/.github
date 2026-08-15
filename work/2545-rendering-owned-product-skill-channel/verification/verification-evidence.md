# Gate-inversion evidence — `.github#2545`

`pnext-item` §3: *"Every gate this change adds or modifies ships with evidence it can fail. Invert it,
run the suite, and record the mutation and the observed red."*

This change adds one gate — the `delivery-channel` arm of `scripts/fsgg-skill-registry-check` — and
two suites that measure it: `tests/skill-registry/run.sh` cases 69-77, and this package's
`verification/run-checks.sh`. Both were inverted, in both directions, and every inversion was
observed red.

Recorded here in three parts, because they answer three different questions:

1. **Can the GATE fail?** Five source mutations to the arm itself, each run against the whole fixture
   suite. This is the question a green suite cannot answer on its own.
2. **Can each OBLIGATION fail?** Twelve expectation inversions through `FSGG_2545_INVERT`, one per
   check `verification/run-checks.sh` performs.
3. **Does the shipped tree pass?** The unmutated runs, with the JUnit report's exact bytes.

Every command below was run from a clean worktree at
`item/2545-rendering-owned-product-skill-channel`, offline, with no producer checkouts.

---

## 1. Source mutations to the arm — does `tests/skill-registry/run.sh` have teeth?

Each mutation was applied to `scripts/fsgg-skill-registry-check`, the full suite was run
(`bash tests/skill-registry/run.sh`), and the file was restored from a pre-mutation copy before the
next one. The unmutated suite exits 0 with `skill-registry fixture: all checks passed`.

| # | Mutation (exact) | Suite exit | First failure observed |
|---|---|---|---|
| M1 | in `delivery_channels`, the catalog-closure loop's `if key in declared:` → `if True:` — the closure direction never fires | **1** | `FAIL: an undeclared class was not reported` (case 70) |
| M2 | the dead-entry loop's `if key in catalog:` → `if True:` — the converse direction never fires | **1** | `FAIL: a dead declaration entry was not reported` (case 72) |
| M3 | `TRACKED_BY_RE = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]*/[A-Za-z0-9][A-Za-z0-9._-]*#[1-9][0-9]*")` → `re.compile(r".*")` — any string is an acceptable reference | **1** | `FAIL: expected 'full owner/repo#number' for entry: … tracked-by: .github#1240 }` (case 75) |
| M4 | the unreadable-declaration arm `return [finding(…)]` → `return []` — a declaration the gate cannot read means every class is fine | **1** | `FAIL: a MISSING declaration was not a finding` (case 76) |
| M5 | `check()`'s `findings.extend(delivery_channels(registry_path, registry["skills"]))` deleted — the arm is never wired in at all | **1** | `FAIL: an undeclared class was not reported` (case 70) |
| M6 | `DELIVERY_DISPOSITIONS["gap"]` required fields `("tracked-by",)` → `("tracked-by", "evidence")` — a `gap` must evidence a negative | **1** | `FAIL: a gap carrying tracked-by and no evidence was reported` (case 74) |
| M7 | `DELIVERY_DISPOSITIONS["provider-scoped"]` required fields lose `"evidence"` | **1** | `FAIL: expected 'requires a non-empty .evidence.' for entry: … provider-scoped …` (case 74) |

M6 and M7 were added in **repair 1**. An independent reviewer read this package's prose, which said
`evidence:` was required on *every* disposition, probed a `gap` entry carrying none, and found it
green — the schema was right and three separate paragraphs were wrong. The prose is corrected; these
two mutants are what stop the two drifting apart again, in **both** directions: M6 pins that `gap`
must NOT demand evidence of a negative, M7 pins that `provider-scoped` must.

M4 and M5 are the two that matter most and are the easiest to ship by accident: M4 is the fail-open
shape (`#266`) one level up from the classes themselves — a gate answering confidently about a file it
never read — and M5 is a complete, correct, thoroughly commented arm that no caller invokes, which is
the ornament this organisation keeps building (epic `#416`).

Restoration was verified, not assumed: after M5 the file was restored and the suite re-run to
`SUITE_EXIT=0`, `skill-registry fixture: all checks passed`, and `git diff --stat` on
`scripts/fsgg-skill-registry-check` reported only the intended additions.

## 2. Expectation inversions — can each verification obligation fail?

`verification/run-checks.sh` accepts `FSGG_2545_INVERT=<obligation>`, which flips that one check's
expected verdict. An obligation whose inversion still passes is not measuring anything.

```
FSGG_2545_INVERT=VO-00N bash work/2545-rendering-owned-product-skill-channel/verification/run-checks.sh
```

| Obligation | What it asserts | Inverted result |
|---|---|---|
| VO-001 | a class in the catalog with no declaration entry is a finding naming its rows | **red** — `FAIL VO-001: inverted: expected quiet, got a finding` |
| VO-002 | a declaration entry matching no catalog row is a finding | **red** — `FAIL VO-002: inverted: expected quiet, got a finding` |
| VO-003 | a `tracked-by` that is not `owner/repo#number` is a finding | **red** — `FAIL VO-003: inverted: expected quiet, got a finding` |
| VO-004 | a `provider-scoped` entry with neither `tracked-by` nor `accepted` is a finding | **red** — `FAIL VO-004: inverted: expected quiet, got a finding` |
| VO-005 | the arm reaches a verdict with an empty `--repos-root` | **red** — `FAIL VO-005: inverted: expected quiet, got a finding` |
| VO-006 | removing the shipped `fs-gg-rendering`/`product` entry reds the gate | **red** — `FAIL VO-006: inverted: expected quiet, got a finding` |
| VO-007 | `skill-registry-coherence.yml` selects the declaration on both triggers | **red** — `FAIL VO-007: inverted: expected the filter to be missing the path` |
| VO-008 | the shipped registry + declaration pair is coherent | **red** — `FAIL VO-008: inverted: expected a finding and the pair was quiet` |
| FR-006 | `tests/skill-registry/run.sh` passes and reaches cases 69-77 | **red** — `FAIL FR-006: tests/skill-registry/run.sh did not pass: …` |
| FR-008 | every Rendering-owned product row is named in `spec.md`'s disposition section | **red** — `FAIL FR-008: spec.md's disposition section does not name: INVERTED` |
| PC-001 | `registry/skills.yml` is byte-unchanged versus `origin/main` | **red** — `FAIL PC-001: registry/skills.yml differs from origin/main — this item declares it untouched` |
| PM-001 | an unknown declaration `schemaVersion` is refused, not parsed | **red** — `FAIL PM-001: inverted: expected quiet, got a finding` |

VO-005 deserves a note, because "it ran offline" is the kind of claim a suite can assert by not
looking. The check first asserts the scratch `--repos-root` is genuinely empty
(`find "$EMPTY" -mindepth 1 -print -quit` returns nothing) and records a FAILURE if it is not, so the
obligation cannot be satisfied by a run that quietly had producer trees available.

FR-008 deserves one too, because it is the check that separates a real record obligation from a
vacuous one. It does **not** grep `spec.md` for its own section heading. It derives the 18 row ids from
`registry/skills.yml` — a different artefact, which moves independently — and requires each to be named
in the disposition section, so adding a 19th Rendering-owned product row reds this until the record
answers for it too. (It also anchors the heading search at line start: `AC-008`'s own prose names that
heading, and an unanchored `find()` landed there instead. The first run of this check failed for
exactly that reason, which is itself evidence the check reads the section it claims to.)

VO-007 deserves one too. It reads `.github/workflows/skill-registry-coherence.yml` through a YAML
parser and checks the path is under **both** `pull_request.paths` and `push.paths`, rather than
grepping the repository for the filename. A filter entry under the wrong trigger is the `.github#1606`
shape exactly, and a grep cannot see the difference between the two.

## 3. The unmutated runs

```
$ bash tests/skill-registry/run.sh
…
== 69. the SHIPPED registry and its SHIPPED declaration are coherent ==
   ok
== 70. a class the registry carries and the declaration ignores is a finding, naming its rows ==
   ok
== 71. GATE-INVERSION on 70: declaring the class clears it, and nothing else did ==
   ok
== 72. a declaration entry the registry no longer carries is a dead-entry finding ==
   ok
== 73. a class declared TWICE is a finding — two answers to one question ==
   ok
== 74. every disposition's required fields are enforced, and the vocabulary is CLOSED ==
   ok
== 75. a provider-scoped class must name who owes universal reach, or why it does not need it ==
   ok
== 76. the arm FAILS CLOSED — a missing, mis-shaped, or unknown-schema declaration is a finding ==
   ok
== 77. GATE-INVERSION ON THE SHIPPED PAIR: drop the fs-gg-rendering entry and the gate reds ==
   ok
skill-registry fixture: all checks passed
```

```
$ bash work/2545-rendering-owned-product-skill-channel/verification/run-checks.sh
2545 verification: 12 passed, 0 failed -> …/verification/junit.xml
```

`verification/junit.xml`, exact bytes:
`sha256:07d6d83eb2cc32e59dc36f2b1abb181352519e067a88f411877cc4139a4713a3`
(12 tests, 0 failures, testsuite `github2545-delivery-channel`). This is the report whose bytes each
`observedRun` receipt in `evidence.yml` is digested against; 19 of the package's 24 obligations carry
one. The five that do not, and why no honest receipt exists for them, are in `lifecycle-status.md`.

## 4. The rest of the change's blast radius

`python3 scripts/test --list` selects **28** suites for this diff, deriving the mapping from the
workflows' own `pull_request.paths` filters rather than from a table. It reports the new file
reaching its gate directly:

```
bash tests/skill-registry/run.sh
    skill-registry-coherence.yml: registry/skills.delivery-channels.yml  ←  registry/skills.delivery-channels.yml
```

All 28 pass locally. `shell-lint` needed the pinned `shellcheck 0.11.0`
(`bash scripts/install-shellcheck.sh 0.11.0 8c3be12b… <cache>`), which this environment does not carry
by default; with it, `scripts/lint-shell.sh` reports
`OK — 133 file(s) and 397 workflow-embedded step(s) clean at severity 'warning'` and the fixture
reports `48 passed, 0 failed`. Local green is not the gate; the PR's checks are.
