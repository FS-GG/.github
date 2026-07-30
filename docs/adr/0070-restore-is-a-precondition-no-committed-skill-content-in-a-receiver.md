# ADR-0070: Restore is a precondition of the repo being usable — a receiver commits no skill content, and both runtime roots are generated

- **Status:** Accepted
- **Date:** 2026-07-28
- **Affects:** FS-GG/.github, and the seven `coordination-kit` receivers — FS.GG.SDD, FS.GG.Rendering,
  FS.GG.Governance, FS.GG.Templates, FS.GG.Game, FS.GG.Audio, FS.GG.Net
- **Amends:** [ADR-0011](0011-agent-skill-roots-full-union-orchestrator-owned-mirror.md) Decision 1 and Decision 2 **as they apply to coordination-kit receivers**, [ADR-0065](0065-one-agent-skill-root-contract.md)'s *materialized* disposition, and [ADR-0067](0067-resolve-dont-copy-one-skill-source-two-runtime-roots-a-generated-view.md) §6 — see §5, which records what each of them arranged for and why the replacement is sufficient, rather than deleting the prose. This record **retires nothing** (§6), and it reaches only the receiver lane: the scaffolded-product-workspace half of ADR-0011 and ADR-0065 is untouched.
- **Applies:** [ADR-0058](0058-adopt-one-governing-principle-derive-dont-restate.md) — *derive, don't restate* — between the **package and the repo**, where the org so far applied it only *within* a repo.

## Context

Decided on [`#1837`](https://github.com/FS-GG/.github/issues/1837) by the repository owner on
2026-07-28, on [`#1834`](https://github.com/FS-GG/.github/issues/1834)'s measurement
([`#1847`](https://github.com/FS-GG/.github/issues/1847), commit `4fadc88`). That thread carries the
candidate answers, the per-row attribution and the decision comment; this record is the decision, not
a second copy of the thread.

`.agents/skills` is already a generated, git-ignored view in all seven receivers (ADR-0067 §9 phase 4,
7 of 7). `.claude/skills` is the remaining committed root. This record decides what happens to it.

### The question the decision had to answer

ADR-0067 §1 says *"nothing that can be derived from that source is committed a second time."* Applied
between the package and the repo rather than within the repo, that means a receiver commits **no**
skill content at all. The obstacle, at full strength: **an agent runtime reads the checkout**, and a
fresh clone with no generated skills has no skills — silently, exit 0, in both runtimes, with no
diagnostic. That is ADR-0067 §8's failure at maximum blast radius. So:

> **What guarantees the generate has run before an agent reads the tree?**

### The ledger, as `#1834` corrected it

`#1837`'s own table claimed four error classes stop being representable. Two of its rows were wrong in
ways that matter, and both corrections favour the decision rather than softening it.

| error class | detected today by | with nothing committed |
|---|---|---|
| receiver bytes ≠ pin | `kit / coordination-kit`, required in 7 receivers — **live, and red 534 times in ~24 days** | **cannot occur** — no bytes to diverge |
| **orphan when a kit row is dropped** | **NOTHING.** `RetiredSkillRoots` completes a retired *root*, not a dropped *row*; `coordination-sync --check --against-pin` grades *declared* files, and an orphan is by construction no longer declared | **cannot occur** — nothing persists |
| a bump PR contaminated by materialized-path edits | `#1713` + `#1726` + `#1815` | **cannot occur** — no materialized paths in any diff |
| a hand-edit to a materialized `Directory.Build.props` | **NOTHING** — `include-build-config` defaults `false` and 0 of 7 receivers pass it, while 4 materialize those files ([`#1844`](https://github.com/FS-GG/.github/issues/1844)) | **cannot occur** |
| pin staleness | the kit-pin freshness sweep ([`#1540`](https://github.com/FS-GG/.github/issues/1540)) | **still representable — inherent; the sweep stays** |
| the generate did not run | ADR-0067 §8's alarm | **still representable — and now the ONLY failure mode** |

**The orphan row is the strongest single argument in the thread**, because it is the one class where
this decision removes a real, currently-unmitigated failure mode instead of trading a loud detector for
a silent one. It was reproduced twice, once with **no synthetic input at all**: `FS.GG.Kit`
`0.17.0 → 0.18.0` genuinely dropped `.config/dotnet-tools.json`
([`#1615`](https://github.com/FS-GG/.github/issues/1615) / ADR-0068), the graded set went **28 → 27**,
and the orphaned file was neither deleted, nor restored, nor graded thereafter — the gate stayed green
on it and stayed green when it was then corrupted. On that axis this is a **strict improvement, not a
like-for-like trade**, and that was not true when `#1837` was filed.

### "Self-healing at the next materialize" is up to a fortnight, and never happens on `main`

The two classes the byte-check *does* catch are exactly the two a materialize would have erased. That
made "the next materialize fixes it" sound like a reason not to care. It is not:

- the receivers' `kit-materialize.yml` callers trigger on `pull_request` **alone** — **no
  `workflow_dispatch` in any of the seven** — and the materialize job is further gated to `renovate/*`
  same-repo head refs;
- **across 842 workflow runs in seven receivers, ZERO ran on `main`.**

So a divergence committed to `main` is **never healed on `main`**. It is healed only when a Renovate
branch that happens to carry it merges. Measured interval between merged `renovate/*` pull requests —
the real heal window — is mean **27–84 h**, p90 70–265 h, **max 167–312 h (7 to 13 days)**, with
**FS.GG.Audio (n=2) and FS.GG.Net (n=1) NOT MEASURED** rather than fast
([`#266`](https://github.com/FS-GG/.github/issues/266)). The detector meanwhile runs **9 to 69 times a
day** in the same repos. The gate is not early warning of a state that clears itself; it is the thing
that stops the repo until a human fixes it by hand.

## Decision

**§1 — A receiver commits no kit-derived skill content, and every runtime root is generated.** Neither
`.claude/skills` nor `.agents/skills` is a tracked path in a `coordination-kit` receiver. Every root is
produced at the path its runtime already reads, from the restored `FS.GG.Kit` package — directly, or
from another root that was. ADR-0011's union rule is unchanged and, **for kit-derived content**, is now
*structural*: it all descends from one restored package, so there is no second copy for a first to
diverge from.

**Which ADR-0065 disposition each root lands in is NOT decided here.** *Materialized* (the kit writes
it) and *view* (generated from another root) both already exist, both already hold the root in the
runtime set, and the choice is a per-receiver one that step 3 makes on that repo's tree. What this
clause fixes is the property neither disposition names: **no root is tracked**. ADR-0067 §6.1's rule
follows for every root under this contract regardless of disposition — a root absent in every fresh
checkout must be generated **in a file the receiver owns**, never in a workflow step.

**§1.1 — This does NOT reach content the receiver itself put in a runtime root, and that is an
unresolved obstacle rather than an oversight.** *"Commits no skill content"* is scoped to **kit-derived**
content, because a runtime root today also holds content no package can regenerate. Both instances are
measured and both are in ADR-0067 §9: `FS.GG.SDD`'s producer-authoritative `.claude/skills/skill-manifest.json`
— rehomed *into* that root by `FS.GG.SDD#771`, and without which SDD's **required** `gate` dies at
*"producer manifest missing"* — and `FS.GG.Audio`'s **16 `fs-gg-sdd-*` skills it owns** inside both
audited roots. ADR-0065 §Retiring a root is explicit that only the materializer that wrote a file may
remove it and that a receiver's own content is *"left untouched"*; untracking a root that holds
receiver-owned content would delete it by another name, and ADR-0067 §9 stage 2 has already measured
exactly that failure — *"a view can lose a file that git reports nothing about"*.

So this contract is **not adoptable on a receiver until that receiver's runtime roots hold nothing but
kit-derived content**, either because they never did or because the rest was rehomed first. That is a
per-repo precondition, it belongs to step 3 alongside `diff -r` and the directory/glob-reference grep
(ADR-0067 §9, Rendering's finding), and it is stated here rather than discovered per receiver.
[`#1855`](https://github.com/FS-GG/.github/issues/1855) owns it.

**§1.1 decision and measurement (2026-07-30, #1855).** The seven receiver `main` tips were read
directly; an unreadable tree would have been recorded **NOT MEASURED**, never as an empty root. After
excluding the four kit directories (`check-board`, `cross-repo-coordination`,
`intra-repo-parallel-work`, `pnext-item`), Templates and Net have zero receiver-owned directories.
SDD has 28 receiver-owned directories plus `skill-manifest.json`; Rendering 45, Governance 11, Game
17, and Audio 16. The affected five take **shape 1**: rehome their receiver-owned producer/product
content before that receiver adopts §1. The content already has a receiver/producer authority and
must not be silently absorbed by the kit generator. **SDD takes shape 2** for its
producer-authoritative manifest: its required gate names that manifest at the runtime-root location,
so the SDD root must union the restored kit with SDD's declared producer set. Consequently the
structural claim is only about the kit-derived subset, as §1 says; the all-root union is not one
restored package and needs the receiver-owned source declaration to remain checkable. This decision
changes no receiver today and does not make any measured receiver eligible to untrack a root.

**§2 — The guarantee is that a receiver checkout without a restore is not a working tree.** This is
stated as the contract rather than mitigated by a mechanism. It was chosen over the three alternatives
because it is the only one whose guarantee does not depend on somebody having installed a hook or
remembered a step, and because **every consumer needs the package to run anything**, so one precondition
reaches all of them by the same path. The three it beat are recorded under *Alternatives considered*,
with the reason each loses.

**§3 — What this guarantee does NOT cover, named here rather than discovered later.** A consumer that
reads the tree **without ever invoking the toolchain** is unreached. Concretely:

- **Browsing the repository on GitHub shows no skills.** The web UI performs no restore.
- **A `grep` over a fresh clone finds none**, and so does any indexer, code-search tool or reviewer
  reading a diff. A PR that changes a receiver's skills shows no skill files, because there are none.
- **"What skills did this receiver have at SHA X?" stops being answerable from the receiver alone.**
  It remains *derivable* — the pin is committed — but deriving it needs the package feed, so an
  offline question becomes a networked one.
- **`git` stops being able to see a runtime root go missing.** With no root tracked, deleting one
  leaves `git status --porcelain` clean. Only a check that runs the toolchain can notice — which is
  the same unreached consumer, arriving from the other side.
- **ADR-0067 §8's alarm becomes the entire guarantee.** It was one failure mode among several; it is
  now the only one, so a receiver whose alarm cannot fire has lost everything rather than something.
  §8's requirement therefore binds harder under this contract than it did when it was written.

These are **costs of the contract, not gaps in it** — every candidate answer left some consumer
unreached, and this one's unreached consumer is the one that never runs the toolchain. Naming them is
`#1837` AC 1 and is the reason this section exists.

**§4 — ADR-0067 §7 does NOT reach this decision, and the reason is its subject, not its conclusion.**
ADR-0067 §7 is headed *"Do not build a resolver"*, and every mechanism it prices is a **resolver** — a
way to point a runtime somewhere other than the path it already reads:

| §7's subject | the cost §7 attaches to it |
|---|---|
| `--plugin-dir` | session-only scope |
| `CLAUDE_CONFIG_DIR` / `CODEX_HOME` | relocates an entire config home, including auth |
| the local directory marketplace | a per-machine bootstrap |
| Codex `[[skills.config]]` | a filter, not an adder — measured in both directions |

Generating content **at the runtime's own expected path** is a different act: it points nothing
anywhere, and no runtime is configured. §7 does not merely fail to forbid it — §7's own conclusion is
that it is the preferred thing:

> *"Producing the view at each runtime's expected path is strictly cheaper than all of them. The right
> use of "yes, it can be pointed" is to not need it."*

**The objection that has to be answered, rather than sidestepped, is that §7 prices a "per-machine
bootstrap" and a restore looks like one.** It is not the same object, on two counts. First, §7's
bootstrap is a *registration step performed outside the repo* whose effect is machine state the tree
cannot observe; a restore's effect is **in the tree, at the path the runtime reads**, so a check can
see whether it happened — which is exactly what ADR-0067 §8's alarm does and what a marketplace
registration could not be checked for. Second, §7's bootstrap is a step a consumer would otherwise not
perform, whereas a restore is already required to build, test or run anything in a receiver. This
decision adds a **precondition to an existing step**, not a new step.

That reading was taken against ADR-0067 §7's text, not inherited from `#1837`'s framing of it.

**§5 — What is overturned, what it was for, and why the replacement is sufficient.** Overturning a
decision with a diff that deletes its prose loses the argument, which is ADR-0068's precedent
([`#1615`](https://github.com/FS-GG/.github/issues/1615)). So each amended clause is recorded with the
reasoning it carried.

*ADR-0011 Decision 1 and Decision 2 (2026-07-01), as applied to receivers.* Every root MUST hold the
byte-identical union of all skills, the roots are **copies, not symlinks**, and one authority
materializes them and asserts the roots are equal. The reasoning was that three producers each wrote a
different subset of three roots, so Codex users and Claude users were handed materially different
instruction sets while every gate was green. *That defect is real and this record does not reopen it.*

*ADR-0065 (2026-07-22), the **materialized** disposition.* A declared root's content is written by the
kit as a content-addressed copy into a tracked path, graded against the receiver's pin by
`coordination-sync --check --against-pin`. The reasoning was transport parity: a receiver must be
unable to hide a duplicate by deleting a mirror, and the only way to see that it has not is to grade
the files it committed.

*ADR-0067 §6 (2026-07-27).* The mechanism for a non-committed root is a **view generated at checkout,
never a committed symlink** — because a committed symlink fails **silently** under
`core.symlinks=false` (the git-for-Windows default without Developer Mode): it checks out as a small
regular text file, and both runtimes then exit 0 with zero skills and no diagnostic. §6 applied that
to a *second* root resolved from a *committed* first one.

| what the amended clause arranged for | how it arranged for it | how this contract preserves it |
|---|---|---|
| every runtime root holds the same skills | N materialized copies, plus a gate asserting they are equal | **by construction** for kit-derived content — it all descends from one restored package; there is no second copy for a first to diverge from. Reported in exactly those words on the four half-view trees whose stage notes record it (Templates, SDD, Audio, Governance): *"STRUCTURALLY IMPOSSIBLE to violate … Checked, not assumed"*; the other three recorded parity **AGREE / AGREE** without that phrasing. **Content the receiver owns is outside this and is §1.1's obstacle** |
| a receiver's bytes match its pin | `coordination-sync --check --against-pin` over committed files | **by construction** — the bytes *are* the package's; the tree holds nothing to compare |
| a receiver cannot hide a duplicate by deleting a mirror | ADR-0065 §Retiring a root, and `FsggKitRetiredSkillRoots` | **preserved, and NOT widened** — a correction, because the tempting claim is false: `skill-view check --receiver-proj`'s roots-declaration lane already grades *"the union of `<FsggKitSkillRoots>` and `<FsggKitViewSkillRoots>` ceasing to be the runtime root set"* (ADR-0067 §8.1), i.e. every root, today. What genuinely changes is worse rather than better: with **no** root tracked, deleting one leaves `git status` clean, so that lane is the only thing that can see it. That is §3's cost, restated where it bites |
| real content at each runtime's expected path, never a committed symlink | ADR-0011 §Context, ADR-0014 D6, ADR-0067 §6 | the **committed**-symlink rejection is unchanged and re-affirmed — with nothing committed, that failure mode has no carrier. *"Copies, not symlinks"* is **not** re-affirmed beyond that, and saying so would be false: an **uncommitted** symlink is already how the view root is built (`.agents/skills` is a whole-directory symlink to `.claude/skills`), already sanctioned by ADR-0065's view disposition, and not new here |
| a dropped kit **row** leaves no orphan | *nothing did this* — see §Context | **the class stops existing.** This is the one row where the replacement is strictly stronger than what it replaces, rather than equal to it |

**What is NOT amended.** ADR-0011's union rule and its other invariants; ADR-0065's transport contract,
its §Retiring a root prohibition, and its *view* and *retired* dispositions; ADR-0067 §1, §2, §3, §4,
§5, §8 and §9 — §8 in particular binds harder here (§3). The **product-workspace** lane is out of
scope entirely: `fsgg-sdd` as mirror authority for scaffolded trees, and ADR-0065's rejection of a
runtime package dependency for Rendering's standalone/offline scaffolds, both stand untouched.

**§6 — This retires nothing, and it is not authority to remove a gate.** `#1837` AC 4: this decides a
contract. In particular it **does not retire `kit / coordination-kit`**, which has gone red **534 times
across the seven receivers in ~24 days** (SDD 193, Rendering 178, Game 71, Governance 34, Templates 30,
Audio 21, Net 7) out of 5139 runs, and runs 9–69 times a day per receiver. It is one of the busiest
signals in the fleet. Across the whole fleet adjudication
([`#1810`](https://github.com/FS-GG/.github/issues/1810) +
[`#1829`](https://github.com/FS-GG/.github/issues/1829) +
[`#1830`](https://github.com/FS-GG/.github/issues/1830)) **30 of 30 gates were adjudicated: 26
JUSTIFIED, 0 DECORATIVE, 4 NOT MEASURED.**

**The apparatus is being simplified because errors can be made unrepresentable, NOT because the checks
are idle**, and an ADR that implied otherwise would be cited to justify removals this decision does not
authorise. A gate is retired only if and when its subject becomes impossible, one at a time, each
argued on its own — and after the ADR-0067 §9 rule that nothing is retired before its replacement is
proven **in that repo**.

Two cautions on how the numbers above may be read, because both were paid for by workers who met them:

- **A never-red count is not a verdict.** `FS.GG.Game/governance.yml` sets `continue-on-error: true` at
  **both** job and step level, so its conclusion is `success` unconditionally — *"878 runs, 0 reds" is
  a tautology of its own configuration*, which is why its adjudicator recorded NOT MEASURED and
  refused to write DECORATIVE off two logs.
- **A never-red list decays while you read it.** `FS.GG.Net/gate.yml` went red for real *during its own
  adjudication* (run `30379948771`, `NU1004`, locked restore inconsistent with the project
  dependencies), which moved it off that list entirely.

**§7 — [`#1845`](https://github.com/FS-GG/.github/issues/1845) is a precondition of executing this
contract, and it is not yet met.** "Generate at checkout" cannot be adopted while **no CI path can run
a generate outside a Renovate pull request**, which is what `#1834` measured (0 of 842 runs on `main`;
no `workflow_dispatch` in any of the seven callers). `#1845` is being worked in parallel and this
record states the dependency rather than assuming it away.

The shape landing there, as its holder reported it while this record was being written: a
`workflow_dispatch` on each receiver's caller reaches the materialize job; **a dispatched run never
writes the repository's default branch** — the repair arrives as a branch and a pull request, so
`kit / coordination-kit` still grades it before it reaches `main` — and on any other branch it pushes
in place. Nothing in this record depends on a `main`-arm materialize, and it does not ask for one:
under this contract the skill roots are untracked, so *"a divergence on `main`"* stops being a state
they can be in at all. What `#1845` supplies is the ability to run a generate **on demand from CI**,
which is the property *"generate at checkout"* cannot be adopted without.

A second half of the same precondition was measured for this record and is stated because nobody had
asked: **the ordinary build does not reach the materialize either.** Read off each receiver's `main` on
2026-07-28, six of the seven carry a solution at the repository root and **none of the six lists
`.config/kit/FS.GG.Kit.receiver.proj`** — FS.GG.SDD, FS.GG.Rendering, FS.GG.Governance, FS.GG.Game,
FS.GG.Audio and FS.GG.Net; FS.GG.Templates has no repository-root solution and was **not measured**
further. So `dotnet restore <solution>`, the command a developer actually runs, restores everything
except the kit. *"Restore is a precondition of the repo being usable"* is therefore a statement about a
restore that does not happen on today's ordinary path, and making it true is work that belongs to the
per-receiver step, not an assumption this record may make.

**§8 — This contract invalidates a fleet gate verdict taken the same day, and the verdict must be
re-taken rather than inherited.** `FS.GG.Rendering/skill-view-check.yml` was adjudicated **JUSTIFIED**
on `#1830` on 2026-07-28, mutation-proven four ways — including the decisive leg, a partial view of
**49** per-skill symlinks where `--source` declares 50, detected as `rc 1, [missing-skill]` naming the
one omitted skill. Its adjudicator recorded an expiry condition with it, and the committed wording is
[`docs/reports/2026-07-28-gate-mutation-adjudication-fleet.md`](../reports/2026-07-28-gate-mutation-adjudication-fleet.md):

> *"`.agents/skills` is a whole-directory symlink to `.claude/skills`, so a view cannot diverge from
> what it is a view of, and the gate deliberately does **not** byte-compare the two roots. **If this
> repo ever returns to committing two independent copies, the per-skill lane at the view root becomes
> 50 tests of a file against itself and this verdict must be re-taken.**"*

(The `#1830` comment states the same condition in different words — *"the moment this repo's layout
stops being a view (a second committed copy returns) …"*. The committed report is quoted here because
it is the artifact, and because the two wordings are not interchangeable: the comment's opening clause
is direction-neutral and the report's is not.)

**That expiry names the opposite direction from this decision.** It anticipated a return to two
committed copies; this contract moves to **zero**, and under it `--source` cannot be a committed
`.claude/skills`, because there will not be one. **Neither wording covers that**, and the reason it
matters is the same reason the expiry was written: the gate's premise is that the *source* is the
authored thing and the roots are views of it. When the source is generated too, what the per-skill lane
compares changes, and whether it is still detecting anything is a question that has to be re-asked. The
gate's *subject* therefore changes materially, and a verdict taken on 2026-07-28 says nothing about the
tree this contract creates.
[`#1838`](https://github.com/FS-GG/.github/issues/1838) records two gates that already decayed after
being justified; this would be a third, and unlike those two it would be decayed **deliberately, by
us**. Re-taking it belongs to the receiver step that first makes the change, on that repo's own tree.

### Provenance of every number in this record

Stated so a later reader can tell a measurement from an inference
([`#266`](https://github.com/FS-GG/.github/issues/266)).

| claim | source |
|---|---|
| 0 of 842 materialize runs on `main`; no `workflow_dispatch` in any of the seven; `renovate/*` same-repo head gating | `#1834` → `#1847`, commit `4fadc88`, recorded in `src/FS.GG.Kit/build/FS.GG.Kit.targets`; the per-receiver table is on `#1845` |
| heal window mean 27–84 h, p90 70–265 h, max 167–312 h | same (`4fadc88` records *"two receivers had too few to measure at all"* and names neither) |
| the two unmeasured receivers are **FS.GG.Audio at n=2 and FS.GG.Net at n=1** | the per-receiver table on [`#1845`](https://github.com/FS-GG/.github/issues/1845), not `4fadc88` |
| orphan detected by nothing; `0.17.0 → 0.18.0` dropped `.config/dotnet-tools.json`; graded set 28 → 27 | same, reproduced twice, once with no synthetic input |
| detector runs 9–69×/day per receiver | same |
| `kit / coordination-kit` red 534 times in ~24 days, per-receiver split, of 5139 runs | `#1834`'s report on `#1837` |
| 30 of 30 adjudicated — 26 JUSTIFIED, 0 DECORATIVE, 4 NOT MEASURED | `#1810` + `#1829` + `#1830` |
| `governance.yml`'s `continue-on-error`; `FS.GG.Net/gate.yml` run `30379948771` | `#1830` |
| Rendering `skill-view-check.yml` JUSTIFIED, the 49-of-50 mutation, and the expiry condition quoted verbatim | `#1830` |
| `include-build-config` passed by 0 of 7 while 4 materialize the build-config files | [`#1844`](https://github.com/FS-GG/.github/issues/1844) |
| no repository-root solution lists the kit receiver project in 6 of 7 receivers; Templates not measured | measured first-hand for this record, 2026-07-28, over each receiver's `main` |

## Consequences

- **Every receiver's runtime skill roots become untracked and git-ignored.** A fresh clone has no
  skills until a restore has run, by design, and that state is what ADR-0067 §8's alarm exists to fail
  on rather than a state to tolerate.
- **ADR-0067 §8 is promoted from "one requirement of the rewrite" to "the whole of the guarantee."** A
  receiver whose alarm cannot fire has no other line of defence, which raises the bar on every
  can-fire demonstration and on the mutation-testing of those demonstrations.
- **Four error classes stop being representable, and two of the four were detected by nothing in the
  first place** — the orphaned row and the materialized build-config hand-edit. The gates whose
  subjects the other two are do **not** therefore retire; a gate retires only where its subject has
  become impossible *in that repo*, per ADR-0067 §9 and §6 above.
- **The kit-pin freshness sweep is untouched and still last.** *"Is receiver R's pin current?"* is a
  distribution question, inherent to a versioned package, and this record does not answer it.
- **`#1845` gates execution.** Until a materialize can be run on a receiver on demand, a receiver whose
  generate did not run — or ran wrong — has no way to be repaired from CI at all, and the only path
  back is a Renovate branch that merges, measured at up to 7–13 days away.
- **The ordinary restore path must reach the materialize before this is true anywhere** (§7). Today it
  does not, in six of seven receivers measured.
- **`FS.GG.Rendering/skill-view-check.yml`'s JUSTIFIED verdict must be re-taken** on the first receiver
  that adopts this contract (§8), on that receiver's own tree.
- Sequencing, the per-receiver order, and who-does-what live on the Coordination board (ADR-0001),
  which is where they will still be right next week.

## Alternatives considered

- **A `SessionStart` hook.** Proven — `.github` ships `.claude/hooks/skill-view-check.sh` — and the
  cheapest change of the four. **Rejected:** it reaches an interactive Claude Code agent and **nothing
  else**. Not CI, not a script, not a second runtime, not a developer who never opens an agent. Its
  gap is both the largest of the four and invisible from inside the tool that has it.
- **A mandated bootstrap step in the receiver contract** — *the repo is not usable until
  `dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitMaterialize` has run.* **Rejected:**
  enforceable in CI and unenforceable on a developer's first clone, which is exactly where a silent
  empty-skills tree does the most damage. It also differs from the adopted answer only in *who is
  obliged*: restore-as-precondition binds the toolchain, a mandated step binds a person.
- **Declining — keep `.claude/skills` committed and stop.** Legitimate, and recorded as genuinely
  considered rather than dismissed: `#1810`/`#1829`/`#1830` measured 26 of 30 gates JUSTIFIED and zero
  DECORATIVE, and `kit / coordination-kit` alone reds 534 times in ~24 days. The apparatus works.
  **Rejected** because the argument for change is not that the checks are idle: it is that four error
  classes become unrepresentable, and that one of them — the orphaned row — is today detected by
  nothing at all, so on that axis the change is a strict improvement rather than a trade.
- **A resolver: point each runtime at a shared directory.** Rejected by ADR-0067 §7 on cost, and not
  revisited here; §4 explains why that rejection does not reach this record either way.
- **Keeping `.claude/skills` committed and generating only `.agents/skills`** — the status quo since
  ADR-0067 §9 phase 4. **Rejected** as the arrangement that produces every error class in §Context's
  table: it is precisely one committed copy of content that is already published, and ADR-0067 §1 says
  a derivable thing is not committed a second time.
