# Verification evidence — `.github#2380`

`run-checks.sh` makes VO-001 runnable: it re-executes the measurements `spec.md` records, through the
**real** evaluator (`scripts/skill-union-assert.sh`) against two committed fixtures. No check looks for
a string in a file, and none re-implements the predicate grammar — a leg asserting a predicate
evaluates a certain way *runs* it.

It is hermetic: no network, no board, no scaffold, no `dotnet`. Two committed fixtures plus this
repository's own gate script.

## Clean run

```
$ work/2380-feedback-report-materialization/verification/run-checks.sh
github2380 verification: 8 passed, 0 failed
$ echo $?
0
```

## Gate-inversion evidence

Every check ships with proof it can fail. `FSGG_2380_INVERT=<check>` flips exactly that check's
expected value; the run must go red. All eight, measured:

| Check | Inverted result | Observed failure |
|---|---|---|
| `always_holds` | red, exit 1 | `predicate 'always' evaluated 'true', expected 'false'` |
| `profile_five_is_false` | red, exit 1 | `predicate 'profile in [app, headless-scene, governed, sample-pack, game]' evaluated 'false', expected 'true'` |
| `profile_game_samplepack_false` | red, exit 1 | `predicate 'profile in [game, sample-pack]' evaluated 'false', expected 'true'` |
| `profile_eq_samplepack_false` | red, exit 1 | `predicate 'profile == sample-pack' evaluated 'false', expected 'true'` |
| `template_vocabulary_false` | red, exit 1 | `predicate 'template in [fable-game, fable-bindings]' evaluated 'false', expected 'true'` |
| `negated_unset_param_true` | red, exit 1 | `predicate 'lifecycle != spec-kit' evaluated 'true', expected 'false'` |
| `array_provenance_refused` | red, exit 1 | `expected exit 0 against array-shaped effectiveParameters, got 2: ::error::skill-union-assert: params has no .effectiveParameters object` |
| `fixture_shapes_differ` | red, exit 1 | `expected list/dict effectiveParameters in the two fixtures, got dict/list` |

Each inverted run reports `7 passed, 1 failed` and exits 1; the clean run reports `8 passed, 0 failed`
and exits 0.

### One inversion initially survived, and that is recorded rather than quietly fixed

On the first pass `fixture_shapes_differ` **passed under its own inversion** — it read
`FSGG_2380_INVERT` nowhere, so the variable had no effect on it and the run stayed green at
`8 passed, 0 failed`, exit 0. A check that cannot fail is not a gate, and by the authoring rule that is
a material finding at review by definition. It was repaired at authoring time by making the inversion
swap the expected shapes, and the table above is the post-repair measurement. The prior state is noted
here because the *reason* the rule exists is that a surviving inversion is invisible unless someone
runs it.

## What each check establishes

- The six predicate checks are `spec.md` F5's table. Together they show why
  `fs-gg-feedback-report` is the only **product** row whose absence is detectable on a scaffold that
  carries no `profile` parameter: `always` is the only product predicate that holds there, so it is the
  only one reaching `skill-union-assert.sh`'s `[missing]` class. Every `profile`-gated row evaluates
  false and is classified as a *justified* off-profile omission.
- `template_vocabulary_false` is `.github#2547`'s cause made executable: `FS.GG.Templates`' own manifest
  predicates use a `template` parameter this repository does not declare, so they answer a constant
  `false` here.
- `negated_unset_param_true` records the latent hazard — an unset parameter resolves to the empty
  string, so a **negated** clause fires. No current product row uses that form; it is recorded, not
  claimed as active.
- `array_provenance_refused` is `.github#2546`'s defect held in place: it asserts the specific exit
  code **2** and the documented refusal text, not merely "nonzero", so the check would notice the
  defect being fixed rather than silently keep passing. When `#2546` lands, this check is expected to
  go red and must be updated with it — that is intended, and it is why the assertion is specific.
- `fixture_shapes_differ` keeps `array_provenance_refused` from becoming vacuous: if both fixtures ever
  drifted to the same shape, the refusal check would be testing nothing.

## Scope limit, stated plainly

These checks verify the **record's measurements**, not the org's materialization behaviour. They do not
scaffold a product, do not contact any repository, and cannot detect a change in what
`FS.GG.Workspace.Template` or `FS.GG.Rendering` actually emit. The claims about those producers rest on
the reads cited in `spec.md` F1-F4, which are pinned to `main` at read time and are re-runnable from the
commands given there. Extending coverage to a real scaffold run is `.github#2545` acceptance 3's
regression fixture, which is deliberately owned there.
