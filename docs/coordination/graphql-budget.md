# GraphQL budget & the `fsgg-coord` client

The [`Coordination` Projects v2 board](README.md) is the sequencing layer for all cross-repo work
(ADR-0001). **Projects v2 is GraphQL-only** — there is no REST surface for it — so every board read
and write spends from GitHub's GraphQL rate limit. Under sustained cross-repo coordination that
budget is the binding constraint, so this repo ships a thrifty client, [`scripts/fsgg-coord`](../../scripts/fsgg-coord),
and the skill routes board work through it.

## The one fact that dictates the fix

GitHub's GraphQL **primary** limit is **5,000 points/hour**, and a query's cost is computed from
**the number of nodes it requests — not the number of HTTP requests**. Two consequences:

- **Batching does nothing for the primary budget.** Aliasing five queries into one HTTP request
  pays the same total node cost. (Batching *does* help the *secondary* per-minute/request-count
  limits — but those are not what a read-heavy coordination workload hits.)
- **The only levers that lower primary consumption are:**
  1. **Don't re-fetch static data.** Field ids, single-select option ids, and the project
     number/node-id are stable for the life of the field. Re-introspecting them every session is
     pure waste.
  2. **Ask for fewer nodes.** Resolve one issue's board item via `issue -> projectItems` (a handful
     of nodes), never by scanning the whole board's `items x fields` (cost grows with the board).
  3. **Move non-Projects reads onto REST.** Issues, PRs, comments, and labels are all REST-available.
     REST is a **separate** 5,000-**requests**/hr budget (1 point each regardless of payload) and
     honors ETags, so an unchanged list returns **304 at zero cost** and never touches GraphQL.

`fsgg-coord` is those three levers made concrete. It is a thin `gh` wrapper — no daemon, no state
beyond a JSON cache of **ids only** (never field *values*), so the worst a stale cache can cause is
"board schema changed", fixed by `fsgg-coord bootstrap --refresh`.

## What the client does

| Command | Lever | Effect |
|---|---|---|
| `bootstrap [--refresh]` | (1) | Introspect project id + field/option ids **once** into a user-level cache (`~/.cache/fsgg-coord`). Refresh is only needed when the board's *schema* changes. |
| `board` / `field-id` / `option-id` | (1) | Serve ids from cache — **zero** GraphQL calls. |
| `item-id <issue>` | (2) | Resolve an issue's board item via `issue -> projectItems`, pick the matching board, cache it. One narrow call, then free. |
| `set-field <issue> <Field> <Value>` | (1)+(2) | Resolve project/field/item/option ids from cache and run **one** mutation, auto-routing by the field's `dataType` (single-select / date / number / text / iteration). No per-write introspection. |
| `issues <repo> [--label L] [--jq E]` | (3) | List issues over **REST** with a stored **ETag**; an unchanged repeat 304s to cache. `--jq` projects the payload to trim what you read back. |
| `ready [--repo R] [--status S] [--phase P] [--all]` | (4) | List the **actionable** board items (not `Done` by default). Projects v2 has no server-side item filter, so this is a full scan — but it selects only two fields per item via `fieldValueByName` (a **resolver** field, no node multiplication), not the `fieldValues(first:100)` nested inside `items(first:N)` that `gh project item-list` pays (**O(items × 100) ≈ 2,500 pts**). A 100-item page costs ~1 point. `--repo` matches the issue's **own** repo (e.g. `--repo .github`). |
| `next [--repo R]` | (4) | Print the one most-startable item — the first `Ready`, else the first `Backlog` — optionally scoped to a repo. The "what do I pick up next" query, made cheap. |
| `budget` | — | Print the GraphQL **and** REST meters (`gh api rate_limit` does not itself consume the core budget). |

Every GraphQL call also selects `rateLimit { cost remaining }`; run with `FSGG_COORD_DEBUG=1` to log
each call's cost, so you can **verify** the drop rather than guess. Start any investigation with
`fsgg-coord budget`.

## Example

```sh
fsgg-coord bootstrap                                  # once per ~day (or after a schema change)
fsgg-coord next --repo .github                         # what should I pick up next on the board?
fsgg-coord ready --status Ready                         # everything actionable, all repos
fsgg-coord set-field FS.GG.SDD#84 Phase  "P2 SDD"     # cache-resolved ids, one mutation
fsgg-coord set-field FS.GG.SDD#84 Status "In progress"
fsgg-coord set-field FS.GG.SDD#84 Target "2026-08-01"
fsgg-coord issues rendering --label cross-repo \
  --jq '.[] | "\(.number)\t\(.title)"'                # REST + ETag; 304 on repeat
```

> **Don't reach for raw `gh project item-list` to find the next item.** It nests
> `fieldValues(first:100)` inside `items(first:N)`, so its cost is **O(items × 100) ≈ 2,500 points**
> for a ~100-item board — a few calls exhaust the 5,000-pt/hr primary budget. `ready`/`next` answer
> the same question at ~1 point per 100-item page (a full board ~3) by reading Status/Phase through
> the `fieldValueByName` **resolver** field instead of the `fieldValues` connection.

## Guardrail

[`tests/fsgg-coord/run.sh`](../../tests/fsgg-coord/run.sh) drives the client against a `gh` stub that
**counts calls** and asserts the levers actually fire (bootstrap-then-cache adds zero GraphQL calls;
item lookup is one narrow call then cached; `set-field` routes by `dataType`; `issues` 304s to
cache; `ready`/`next` paginate a two-page board in exactly two calls and filter client-side). It
runs in CI via `.github/workflows/fsgg-coord-selftest.yml` — the same "a fixture proves
it" discipline as the [skill-union assertion](skill-union-assertion.md).
