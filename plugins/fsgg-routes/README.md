# fsgg-routes

The routed subagent definitions the FS-GG board-driver skills dispatch. This plugin is the **one home**
for them: they are not also loose files under `.claude/agents/`.

Standing up this plugin is `.github#2230`, the operator half of the decision recorded on `.github#2203`
(Option 2 — an FS-GG plugin distributed by marketplace, payload bounded to route agent definitions
only). The consumer half — a materialized workspace receiving the same routes through the kit
`kind: config` lane — is a separate slice and deliberately not here.

## What it carries, and why all four

| definition | model | effort | dispatched by |
|---|---|---|---|
| `fsgg-worker-normal` | `sonnet` | `high` | `drive-board-normal`, `work-board-normal` — ordinary route |
| `fsgg-critic-normal` | `sonnet` | `high` | `drive-board-normal`, `work-board-normal` — ordinary route |
| `fsgg-worker-best` | `opus` | `high` | `drive-board-best`, `work-board-best` — ordinary route; **and every variant's repair phase** |
| `fsgg-critic-best` | `opus` | `high` | `drive-board-best`, `work-board-best` — ordinary route; **and every variant's repair phase** |

`.github#2230` required this enumeration to be argued rather than picked silently, because item 5's
one-home rule exists to stop two copies disagreeing — the class this repo files most (`#485`, `#865`) —
and a half-move would manufacture exactly that defect inside the change meant to prevent it.

**All four move, and the split is empty.** The reasons, in order of weight:

1. **All four are route definitions, so `#2203`'s AC2 bound admits all four and excludes nothing.** Each
   carries a `<runtime, model, effort>` triple in its frontmatter and exists only to be dispatched at
   that route. There is no principled line between a worker definition and a critic definition here;
   the board variants' own tables say "pass this route explicitly to **every** subagent spawn", which
   is one rule covering both roles.
2. **Leaving the critics loose would break the thing this slice exists to fix.** A repair phase
   dispatches a fresh implementer *and* a fresh critic. A consumer that enabled a workers-only plugin
   would be able to start a review chain it could not staff — a worse failure than the one on
   `.github#2144`, because it surfaces mid-chain rather than before dispatch.
3. **A partial move is the duplication defect, not a smaller version of it.** Two of four in the plugin
   and two at the root means the route surface has two homes, and the next editor has to know which
   half they are holding.

Nothing legitimately remains loose. `.claude/agents/` is empty of route definitions after this slice,
and `scripts/check-plugin-route-parity.py` fails if a definition reappears there.

## `fsgg-worker-repair` is superseded, not merely unbuilt

`#2203` and `.github#2230` were both written when the escalated repair-phase route was a fifth
definition named `fsgg-worker-repair`, at `opus`/`high`. **That definition does not exist and should not
be created.** The role it named is filled by `fsgg-worker-best`, and the same is true of
`fsgg-critic-repair` / `fsgg-critic-best`.

The evidence is in the route tables themselves, not in a preference:

- `drive-board-best` and `work-board-best` state that their repair-phase route is *identical* to their
  ordinary route — "the escalation is the fresh attempt and the higher round ceiling, not a stronger
  model".
- `drive-board-normal` and `work-board-normal` name `-best`'s route (`opus`/`high`) as their
  repair-phase route — the exact pair `fsgg-worker-best` and `fsgg-critic-best` already carry.
- `fsgg-worker-best.md` says so in its own body: "There is no separate escalated tier below the human
  park."

So `#2203`'s decision is satisfied by a different pair of definitions than it named. This is recorded
here because it changes what a reader should conclude from `#2203`'s text, and
`check-plugin-route-parity.py` makes it mechanical: it asserts that each `-normal` variant's
**repair-phase** table equals the route the `-best` definitions declare. If someone reintroduces a
separate escalated tier, that assertion is where it surfaces.

## Marketplace source vs plugin source — the binding constraint from `#2203`

These are two different `source` fields and the constraint lands on only one of them. Getting this
backwards is the specific mistake `#2203`'s decision comment was written to prevent, so it is recorded
here and **enforced** by `scripts/check-plugin-route-parity.py` rather than left as prose.

**The marketplace source — in `.claude/settings.json` under `extraKnownMarketplaces` — must be
`github`.** A `directory` source installs *in place*: `claude plugin marketplace add <path>` records an
`installLocation` equal to the path it was given, so the marketplace **is** the checkout it points at.
Every session in every git worktree of this repo would resolve that one path back to the single main
checkout, making the route definitions one shared mutable resource under a tree whose repairs the host
has to serialise — the engine-currency shape of `#1549` and `#1663`. A `github` source instead fetches
a private per-machine copy under `~/.claude/plugins/marketplaces/`, which is what makes concurrent
worktrees safe.

**The plugin source — in `.claude-plugin/marketplace.json` — may be relative, and is: `./plugins/fsgg-routes`.**
A relative plugin source resolves against *the fetched marketplace copy*, never against anyone's
checkout, so it does not reintroduce the hazard above. It is therefore not a violation of the
constraint. Anthropic's own `claude-plugins-official` marketplace uses this form for 53 of its plugins.

### Why this is not written as a comment in `.claude/settings.json`

`.github#2230` AC4 asks for this to be recorded where the marketplace source is configured. It is
recorded here, and enforced by the gate, rather than annotated into `settings.json`, because that file
cannot safely carry the annotation:

- `claude --help` states that settings files which fail validation are **silently ignored** in
  non-interactive mode (`-p`, or any non-TTY stdout). `.claude/settings.json` also carries this repo's
  `block-merge-and-main-push.sh` PreToolUse push guard, so a rejected settings file would drop that
  guard with no error shown.
- A `"//"` annotation key is reported as `Unrecognized field` by the settings validator, and
  `claude plugin validate --strict` rejects the same idiom in `marketplace.json`
  (`Unknown field '//'. Claude Code ignores it at load time.`).

A gate that fails red with the reason printed is a stronger guarantee than a comment anyway: a later
reader who changes the marketplace source to `directory` does not merely miss a note, they get a
failing check that explains the constraint.

## Editing a definition

These files are consumed from the **fetched marketplace copy**, not from your checkout. After a change
lands on `main`, a machine picks it up with:

```bash
claude plugin marketplace update fsgg
```

and a restart. A change on a feature branch is not live in your own session; review it as a diff.
