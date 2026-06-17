---
title: Routing, severity, and run modes
category: Governance design
categoryindex: 7
index: 5
description: Light-by-default routing, the Severity axis, the RunMode escape hatch, the unbypassable merge boundary, and the explainable Route output.
---

# Routing, severity, and run modes

Routing decides what proof a change needs. The previous implementation made this
oppressive: it was default-deny (anything unclassified fell through to the
heaviest tier), everything that matched a rule *blocked*, and the output was
opaque. This design fixes all three structurally.

## Light by default

The floor is the lightest tier, not the heaviest. An unclassified or low-stakes
change escalates to **nothing**. Heavy machinery requires a *positive* match
against a small, named, fenced high-stakes surface.

```fsharp
type Stakes =
    | Routine        // default. No gates.
    | Fenced of name: string   // an explicitly high-stakes surface

/// A change is Routine unless it positively matches a fenced surface.
/// There is no default-deny fall-through to a heavy tier.
let stakesOf (fences: Fence list) (change: ChangeSet) : Stakes =
    fences
    |> List.tryFind (fun f -> f.Matches change)
    |> function Some f -> Fenced f.Name | None -> Routine
```

The **zero-gate zone** is explicit and broad: notes, reports, drafts, scratch,
and experiments trigger no machinery, because thinking is not contract. Only an
explicitly fenced surface — a published API, a release artifact, an irreversible
contract — pulls in blocking checks.

`ChangeSet` is abstract on purpose. Software supplies a git diff over file paths;
a research or essay adapter supplies whichever notion of "what changed" fits.
Routing never assumes file paths.

## Severity — advisory by default

`Severity` is orthogonal to `CheckTier`. The tier says *who decides*; the
severity says *whether failure stops you*.

```fsharp
type Severity = Advisory | Blocking

let rule id tier spec check =
    { Id = id; Tier = tier; Spec = spec; Check = check
      Severity = Advisory; Question = None }     // <- reports, never blocks

let blocking r = { r with Severity = Blocking }  // explicit, rare, reviewable
let asking q  r = { r with Question = Some q }
```

Most of a catalog is advisory: it reports ("contrast is borderline here") and
moves on. Blocking is opt-in and deliberately rare. The entire blocking set must
be listable at a glance — a long blocking list is a design smell:

```fsharp
let blockingRules = rules |> List.filter (fun r -> r.Severity = Blocking)
```

## Run modes — the honest escape hatch

There is a real way to turn governance off for everyday work. It is loud,
local-only, and cannot be the basis of a merge.

```fsharp
type RunMode =
    | Sandbox   // governance OFF. Loud banner. Local only. Never a merge basis.
    | Inner     // advisory only: fast deterministic checks report; nothing blocks
    | Gate      // merge / CI: Blocking severity enforced, recomputed from base
```

The property that makes the hatch safe: **`Gate` recomputes from the diff against
the base branch and ignores any local mode.** So `Sandbox` gives a frictionless
inner loop for developing, debugging, and trying things, but you physically
cannot merge a sandboxed state without the gates running independently at the
boundary. You can develop *without* the machinery; you cannot *land* an
un-governed state.

This is the relief the prior art's disclosure-only flag never provided: it
policed honesty but never gave a way to just iterate.

## Explainable output

`Route` must always answer "why," and the light / no-gates case is the normal,
loud case — not silence.

```text
$ route
light — no gates (advisory only). 3 files changed, none on a fenced surface.

$ route        # change touches a fenced public surface
gate: PackageSurfaceCheck   (blocking)
  ← rule 'public-surface'   ← matched fence 'public-api'
  ← check: surface-matches(GeneratedTokenSurface, TokenDocument)

advisory: ContrastCheck
  ← rule 'contrast-policy'  ← matched fence 'theme-tokens'
  ← check: contrast-meets(Ant, ThemeSurface)
```

Each firing gate names its rule, the fence it matched, and the rendered check
(`Check.render`). A gate that cannot explain itself in one line is a defect.
"No reason" is unrepresentable, because the reason *is* the rule id plus the
rendered check.

## How a route is computed

```text
1. classify the change       -> ChangeSet (adapter supplies the notion of change)
2. match against fences       -> Routine | Fenced name      (light by default)
3. select rules whose Reads   intersect the change          (declared inputs)
4. evaluate the kernel        -> verdicts + review requests + blockers
5. partition by Severity      -> blocking gates vs advisory reports
6. in Gate mode, blocking failures stop; Inner reports; Sandbox is off
7. render the route with a reason for every entry
```

Steps 2 and 5 are the two levers that keep the system light: nothing is heavy
unless a fence was matched, and nothing blocks unless a rule opted into blocking.
