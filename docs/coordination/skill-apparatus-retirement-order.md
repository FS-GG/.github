# The skill-apparatus retirement order (ADR-0067 §9, phase 4)

ADR-0067 §9, verbatim: *"build the view and the absence check; run it alongside the existing gates
unchanged; **port** the fixtures rather than re-derive them; **retire the old apparatus per repo, with
the freshness sweep last**."*

This is that order, **written before the first retirement** rather than derived per repo as it goes
(`.github#1676` AC 1). Phases 1–3 are landed and closed: `#1621` (measurement), `#1635` (the view and
§8's loud absence check, `scripts/skill-view`), `#1674` (the alongside comparison,
`scripts/skill-view-parity.sh`, 8/8 repos AGREE). §5 (three roots → two) landed separately as `#1636`.

Everything below was **measured on 2026-07-28** against `0ea5396`. Where a number appears, the command
that produced it appears with it.

---

## 1. What actually retires — and what does not

ADR-0067's Consequences name seven things "for eventual replacement": `coordination-sync`, kit
materialization, `kit-published-coherence`, `coordination-coherence`, `skill-union-assert`, the kit-pin
freshness sweep, and the kit Renovate bump loop. **Phase 4 measured that list against the mechanism and
found it conflates two different things**, and the distinction decides the whole order:

- **Duplication *within* a repo** — one repo committing the same skill under two roots. This is what
  resolve-don't-copy removes. One source, one generated view.
- **Distribution *between* repos** — `.github` shipping skills to seven receivers through a versioned
  package. §4 settled that the multi-repo split is **not** revisited, so distribution survives the
  rewrite entirely. The kit still ships; pins still go stale; bumps still have to land.

Sorting the seven by that distinction:

| piece | verdict under resolve-don't-copy | why |
|---|---|---|
| `skill-union-assert` | **RETIRES** | Its subject is *"every skill … present in every root and byte-identical across them"* (`registry/repos.yml:226`). With a generated view there is **one object**, so divergence is not detected-and-absent, it is **structurally impossible**. `scripts/skill-view-parity.sh` checks that claim rather than assuming it, and reported it on every resolved tree measured below. |
| the second committed root, per repo | **RETIRES** | This *is* the copy. |
| kit materialization | **NARROWS** | Two roots → one. The package still ships and still materializes. |
| `coordination-coherence` (+ `coordination-sync --check`) | **NARROWS** | Its skill paths halve. It also covers `scripts/fsgg-coord` and `.config/dotnet-tools.json`, which are **not** skills and are untouched by this rewrite. It does not dissolve. |
| `kit-published-coherence` | **UNCHANGED** | Compares the published nupkg's manifest to the staged one. The kit still publishes. |
| the kit Renovate bump loop | **UNCHANGED** | The kit still versions and still needs bumping. |
| **the kit-pin freshness sweep** | **UNCHANGED — and this is why it goes last** | *"Is receiver R's pin current?"* is a question about **distribution**. Resolve-don't-copy does not answer it and does not remove it. §9 orders it retired last; phase 4's measurement is that it has **no retirement trigger at all** under the decided end state. It goes last, and on today's evidence it does not go. |

**Consequence for the board.** Roughly a quarter of live rows (18 of 71, measured 2026-07-28) have this
apparatus as their subject, but most of them are about **distribution** — bump loops, pin staleness,
receiver fan-out — not about duplication. They do not dissolve when the copy goes. `#1676` expected a
bulk `SUPERSEDED` closure; the measured answer is that **the closure is real but it is downstream of a
retirement that has not happened yet**, and closing those rows now would close live subjects.

---

## 2. The layout every retirement retires INTO, and why it is a HALF-view

Two shapes are available. Only one of them is usable while `.github#1685` is open.

- **Half-view (the order uses this).** One root is the tracked source; the other is a generated view of
  it. In `.github` that is `.claude/skills` (tracked — it is the kit source, see §3) and
  `.agents/skills` (generated, git-ignored).
- **All-view.** Both roots are views of a third tracked directory. This is the cleaner reading of §1,
  and it is **refused today**: `scripts/skill-union-assert.sh:456` enumerates with `find` and no `-L`,
  so a root that *is* a symlink contributes zero ids and the gate exits 2.

Measured, on `.github`'s own 13 skills:

```
half-view, source=.claude/skills  →  skill-union-assert exit 0 ; skill-view-parity AGREE
half-view, source=.agents/skills  →  skill-union-assert exit 0 ; skill-view-parity AGREE
all-view,  source=skills/         →  skill-union-assert exit 2 ("no skills found under any root")
                                     skill-view-parity  exit 2 (NOT COMPARABLE), naming #1685
```

So **`#1685` is not a blocker for this order** — it binds only the all-view shape, which the order does
not use. It fails closed in both directions, and phase 3 pinned it. It becomes a blocker only if a later
change wants the all-view end state.

On the half-view layout `skill-view-parity.sh` reports the byte-identity fact as *"OLD-ONLY, and
STRUCTURALLY IMPOSSIBLE to violate here — every configured root resolves to the same object … Checked,
not assumed."* That sentence is the evidence for retiring `skill-union-assert` on that repo, and it is
re-derived per repo rather than inherited.

---

## 3. Which root is the source

**`.claude/skills`.** Not a preference — two independent consumers already require it:

- `registry/repos.yml:328-334` — all four `kit:` skill rows source `.claude/skills/<id>`, and
  `registry/repos.lock` content-addresses those exact paths.
- `scripts/generate-driver-manifest:38-41` — *"The digest is read from the canonical `.claude` root (the
  `supplied-by` path)"*.

Making `.agents/skills` the source instead would rewrite both, relock, and touch a `kit:` source — which
obliges a kit republish and a seven-receiver fan-out **for a path rename**. That is precisely the churn
ADR-0067 exists to end, so the order does not spend it.

This does not contradict ADR-0067 §5's *"`.agents/skills` (canonical, because it needs no pointing)"*.
§5's claim is about **runtime discovery** — Codex finds `.agents/skills` with no configuration — and a
generated view at that path is discovered identically. §5 did not decide which root **git tracks**;
phase 4 does, and the two consumers above decide it.

---

## 4. Per-repo eligibility — the precondition, and today's verdict

ADR-0067 §9: *nothing is retired before its replacement is proven.* For repo R the replacement is proven
when **all** of these hold. Re-run them for R immediately before retiring R; do not inherit a fleet-wide
verdict.

1. `scripts/skill-view` is **present and executable in R**. It is the replacement; a repo without it has
   no replacement.
2. `scripts/skill-view-parity.sh --tree <R>` exits **0 (AGREE)** on R's current tree, and again on R's
   resolved equivalent (the recipe is in that script's header, lines 54–67).
3. R's post-retirement layout is a **half-view**, not all-view (§2).
4. R's second root is not left committed. `scripts/skill-view:331` **refuses to generate over a root git
   tracks, with no override** — so a repo whose second root is still committed cannot have a view
   generated there at all.

### Verdict as of 2026-07-28: **0 of 7 receivers are eligible.**

The roster is `registry/repos.yml:104-110` — `sdd`, `rendering`, `governance`, `templates`, `game`,
`audio`, `net`. All seven fail precondition 1, for a reason that is not on the board:

> **`scripts/skill-view` is not a `kit:` row.** The kit's six sources are four skills, `scripts/fsgg-coord`
> and `dist/dotnet/.config/dotnet-tools.json` (`registry/repos.yml:328-343`, `registry/repos.lock:9-14`).
> Nothing delivers the replacement to a receiver, and **no published version of `FS.GG.Kit` ever has** —
> including 0.14.0.

This is a stronger statement than "the receivers are stale". A current receiver would still have no
replacement.

Preconditions 2 and 3 are satisfiable — the parity harness measured 8/8 AGREE on `#1674`, and §2 above
re-measured the resolved half-view shape green. Precondition 4 is **not reachable by any mechanism that
exists**, which is blocker B2 below.

---

## 5. The blockers, in the order they must clear

| id | blocker | state |
|---|---|---|
| **B1** | The kit does not deliver `scripts/skill-view` (or the absence-check wiring) to receivers. Precondition 1 fails on all seven. | **filed by this item** |
| **B2** | The materializer has exactly two states for a root: `FsggKitSkillRoots` (materialize into it) and `FsggKitRetiredSkillRoots` (delete the kit's directories from it) — `src/FS.GG.Kit/build/FS.GG.Kit.props:19,25`. There is **no third state** for *"still a declared runtime root, but generated locally rather than materialized"*, which is exactly what a view root is. Without it a receiver's second root stays committed, and `scripts/skill-view:331` then refuses to generate the view there. ADR-0065 §Retiring a root forbids the receiver hand-deleting it — *"A receiver never hand-deletes a mirror; the materializer that created it is the thing that removes it."* | **filed by this item** |
| **B3** | [`#1693`](https://github.com/FS-GG/.github/issues/1693) — `#1587`'s diff-shape guard refuses the 0.14.0 bump on all seven receivers, so no kit change reaches any receiver today regardless. 0 of 7 are current (SDD 0.10.0; five at 0.8.0; Audio 0.6.0). | open |
| **B4** | `scripts/repos-audit.sh:1841` requires a receiver's gate to be armed on a change to a **committed** skill root. A generated root cannot be armed that way. Repairing this is **sanctioned** — ADR-0067 says the apparatus *"keeps running unchanged, and keeps being repaired"* until §9's order reaches it — but it must precede the first receiver retirement, and it **must not be confused with retiring the sweep**, which is last. | open, unfiled — raise with the first receiver |

B1 and B2 are both kit-content changes, so they land together, in one republish, and then ride B3.

---

## 6. The order

### Stage 0 — unblock (no retirement)

B1 + B2 in one kit change and one republish; B3; then the seven bumps land. Nothing is retired here.

### Stage 1 — per receiver, one at a time, each proven before the next

Roster order, cheapest blast radius first, so that the rollback path is exercised on the smallest tree
before the largest:

1. `FS.GG.Net` — no Renovate config (`default.json:12`), no `build-config`; the smallest surface.
2. `FS.GG.Audio`
3. `FS.GG.Templates`
4. `FS.GG.Governance`
5. `FS.GG.Game`
6. `FS.GG.SDD`
7. `FS.GG.Rendering` — largest skill tree (`.claude=50`, measured at `registry/repos.yml:234-236`), last.

For each: re-run §4's four preconditions; retire the second committed root; confirm the repo's gates
green **on `main`, by run id, not by merge**; retire that repo's `skill-union-assert` caller (**note: zero
receivers have wired one** — `registry/repos.yml:268-271` — so this step is vacuous today and costs
nothing); close only the board rows whose subject that repo's retirement actually dissolved.

### Stage 2 — fleet-wide, only after all seven

Narrow (do not delete) kit materialization to one root, and narrow `coordination-coherence`'s skill
paths. Both keep their non-skill subjects.

### Stage 3 — LAST

The kit-pin freshness sweep (`scripts/repos-audit.sh:1892`, `#1540`). §9 orders it last because it is the
alarm that would say a retirement went wrong. Phase 4's finding (§1) is that its subject — distribution
staleness — **survives the rewrite**, so on today's evidence stage 3 is reached and then declined.
Retiring it requires a separate decision that says what replaces *"is receiver R's pin current?"*.

### Not in this order

`.github`'s own duplicate roots. `.github` is the **authority, not a receiver** (`registry/repos.yml:103`
— it receives `labels` only), and its two committed roots are the **kit source**, not materialized
output. Retiring them is real resolve-don't-copy work and it is **separable and substantial**: measured
on 2026-07-28, the local gates come up green on the half-view layout (`skill-union-assert` 0,
`generate-driver-manifest --check` 0, `check-paths-coherence` 0, `repos.sh validate` 0, parity AGREE),
but a bare CI checkout has no generated root, so `skill-roots-selfcheck`, `skill-registry-coherence`,
`skill-view`, `skill-view-parity`, `projections`, `skill-quality` and `feed-autofix` each need a
generate step; `scripts/generate-projections`' twelve targets collapse to six of the same file; and
`scripts/check-skill-quality.py:166`'s mirror leg starts comparing a file to **itself** — a vacuous pass
(epic #266) created *by* the retirement. That is its own item, not a rider on this one.

---

## 7. The rollback path

**Stated.** A view retirement is rolled back by `git revert` alone, and this is a property of the
mechanism rather than a procedure to remember: the generated root is **untracked**, so the retirement
commit contains the entire change — the deletion of the committed copy, the `.gitignore` line, and the
generate step. Reverting it restores the committed copy; the stale generated root is then a symlink
sitting where a real directory belongs.

```
git revert --no-edit <retirement-commit>     # restores the committed second root
rm -rf <second-root>                          # discard the leftover generated view
scripts/skill-union-assert.sh                 # the old gate, over the restored copied tree
```

The `rm -rf` is required and is not optional tidying: `git revert` will not replace a symlink that the
working tree holds where the restored directory goes.

**Tested**, 2026-07-28, on `.github`'s own tree at `0ea5396` (13 skills, both roots committed):

| step | command | observed |
|---|---|---|
| retire | `git rm -r --cached .agents/skills` + `rm -rf` + `/.agents/skills` in `.gitignore` + `scripts/skill-view generate --source .claude/skills` | `13/13 declared skill(s) visible` in both roots; `git status --porcelain` shows **only** the 56 tracked deletions and the `.gitignore` edit — the generated root is invisible to git |
| prove | `scripts/skill-union-assert.sh` / `scripts/skill-view-parity.sh --tree .` | `0` / `0` (**AGREE**), byte-identity reported STRUCTURALLY IMPOSSIBLE |
| rollback | `rm -rf .agents/skills` + `git checkout -- .gitignore .agents` + `git reset HEAD .agents` | `git status --porcelain` → **0 changed paths**; `.agents/skills` back to 13 committed directories |

The rollback was exercised against the **producer**, because §4 measured that no receiver is eligible to
be the first repo. It must be re-run against the first receiver before the second is touched
(`#1676` AC 3).

---

## 8. What this order does NOT authorize

- Retiring anything on a repo that has not passed §4's four preconditions on its own tree, that day.
- Hand-deleting a mirror on a receiver. ADR-0065 §Retiring a root, unchanged and still governing.
- Amending ADR-0011 Decision 1, ADR-0014 Decision 5, or ADR-0065's root set. Those are amended **in
  direction only** and stay in force until a change actually lands a mechanism retirement — ADR-0067 §9,
  and `#1676` AC 5. **No such change has landed**, so this document amends none of them.
- Closing a board row whose subject has not dissolved. See §1.
