#!/usr/bin/env python3
"""Is EVERY touch of a loaded decision program inside the fail-closed boundary? (.github#2571/#2652)

The obligation arm imports another program to ask it a question. Every line that then reaches into
that program — an import, an attribute lookup, a call, or handing the module to something that will
do one of those — is a place the program's code runs, and `SystemExit` is a `BaseException`, so an
unguarded one exits this gate ZERO in silence.

That happened twice in two rounds, both times by adding an ordinary-looking line, and neither time did
the behaviour fixture red: the round-1 repair put `patch_tuple` outside the guard, and an audit then
found a PEP 562 `__getattr__` hole at the `decide` lookup that had been there since the arm was
written. Behaviour legs can only cover the holes someone thought of. This covers the shape.

THE RULE, checked over the AST rather than by grep or by eye: inside the functions that hold a loaded
module, every call that could execute that program's code must be DEFERRED INTO a `_guarded(...)`
thunk.

WHAT COUNTS AS GUARDED, and why the distinction is the whole check (.github#2652). `_guarded(what,
call)` takes a THUNK, so guardedness is a property of being *deferred*, never of sitting lexically
inside the call's parentheses. The first version of this file marked the whole subtree of a
`_guarded(...)` call guarded, which blessed the single mistake the API most invites —

    completions = _guarded("enum", automation.completions(module, candidate, frontier))

— where the inner call is evaluated BEFORE `_guarded` runs and therefore outside its `try`. Measured
at the time: that edit made the arm exit 0 in silence on a program whose `patch_tuple` calls
`sys.exit(0)`, and this checker said `ok`. Only the lambda's BODY (or a bare name passed as the thunk)
is guarded now; the message argument, the lambda's own defaults, and a `_guarded` call spelled with
keywords or the wrong arity defer nothing and are reported as NOT A THUNK.

WHAT COUNTS AS A TOUCH. A call whose func reaches a tracked reference (`module.decide(...)`,
`m.decide(...)`, `module.__dict__["decide"](...)`, `decide(...)`, `decide.__call__(...)`,
`(decide if c else other)(...)`), or one that
HANDS a tracked reference over as a positional argument, a keyword argument, a starred element, or an
entry of a literal container (`getattr(module, ...)`, `f(mod=module)`, `f(*[module])`,
`f(**{"mod": module})`, `sorted(xs, key=decide)`, `map(decide, xs)`), or any `exec_module(...)`.

WHAT COUNTS AS A TRACKED REFERENCE. The name `module`, whatever a call to a LOADERS function is bound
to, every local name aliased from one of those, and — since .github#2667 — whatever a READ out of one
is bound to, however that read is spelled: `module.decide`, `module.__dict__[k]`,
`getattr(module, "decide", None)`, or any of those DEFERRED into a guard's thunk. The alias pass
deliberately OVER-approximates — `a, b = module, other` marks both — because a false flag costs an
author one line of explanation while a miss costs a silent exit 0.

WHAT IS DELIBERATELY NOT TRACKED: an INVOCATION RESULT. `decide(facts)` returns plain data, and the
arm reads it unguarded and correctly — `decision.get("action")`, `isinstance(decision, dict)`,
`declined.append((completion, decision))`. Tracking a result would red all three, and the only way to
keep the checker green over its own subject would then be to weaken the rule for every lifted value
too, which is the trade .github#2667's first repair made and this one does not. A READ yields the
program's own state; a CALL yields whatever that state computed. Only the first is the program.

The one narrowing is NON_INVOKING below: three strictly unary builtins that provably cannot run what
they are handed, exempted from the HANDOVER rule alone. It is a short list with a stated reason per
entry, not a general escape hatch, and it never applies in func position.

WHAT THIS CANNOT SEE, stated because a checker that overstates its reach is worse than one that does
not exist — and stated as a RULE a reader can apply to a spelling nobody has written yet, because
this is the third generation of one cause (#2571 → .github#2652 → .github#2667) and an enumeration of
exemplars is what invites a fourth.

  FIRST, there must BE a call: the unit of this whole check is `ast.Call` (see `is_touch`). THEN walk
  backwards from that call to the tracked name. In FUNC position the steps this checker follows are
  attribute, subscript, conditional and boolean-operator. In ARGUMENT position it will first peel any
  nesting of starred elements and literal list/tuple/set/dict, then follow that same chain. If there
  is no call, or the path from the name to it needs ANY other step, this checker does not see it.

Three steps are outside it. The list is not claimed to be exhaustive — that claim is what a reader
would rely on to stop looking, and it is the one thing this docstring must not get wrong.

**No call at all**, which is the widest of the three and the easiest to miss because nothing about it
looks like a boundary question. The unit is the CALL, so a program-defined dunder reached by SYNTAX is
invisible: interpolation (`f"{decide!r}"`), `%`-formatting (`"%s" % decide`), comparison
(`decide == x`), bare truth-testing (`if decide:`), iteration (`[x for x in decide]`), hashing. Note
the asymmetry this produces, because it is the practical tell — the SAME hazard is graded in its call
spelling and unseen in its syntactic one: `repr(decide)` and `bool(decide)` are touches, while
`f"{decide!r}"` and `if decide:` are not. `NON_INVOKING` below excludes `repr` precisely because it
runs `__repr__`; the f-string is that operation without the call, and this checker cannot reach it.

  ONE MEMBER OF THAT FAMILY DIFFERS IN KIND, and it is the one worth knowing. Every exemplar above is
  a CONSUMPTION of a value whose own lift was checked. The LIFT ITSELF can be spelled without a call —
  `decide = module.decide`, `decide = module.__dict__[k]` — and then it is not merely unseen: it
  leaves the touch accounting altogether. Substituting either spelling for the arm's guarded lift
  drops `decision_function` from two touches to one and still reports `ok:`, where M7's `getattr`
  spelling of the very same removal reports UNGUARDED. So the guard `.github#2571`'s round-2 repair
  put there — the one whose absence let a PEP 562 `__getattr__` green EVERY subject in silence — can
  be deleted with this checker still naming `decision_function` as a holder it checked. DECLARED_HOLDERS
  and MAP ROT refuse a subject that rots to zero HOLDERS; nothing here refuses a holder that rots to
  zero TOUCHES, so read `ok:` as "no touch I can see is unguarded", never as "the guards are present".

**Indexing into or iterating a container** — `[decide][0](x)`, `{"d": decide}["d"](x)`,
`sorted(xs, key=[*[decide]][0])`, `f([d for d in [decide]])` — because knowing which element comes out
means evaluating the container.

**An unbound call result** — `type(module)(...)`, `_guarded(w, lambda: getattr(module, "d"))(x)` —
because a result is tracked when it is BOUND to a name (see `lifts_from`) and there is no name here.

The rule also decides the cases that ARE seen: `functools.partial(decide, x)()` and
`getattr(module, "d")(x)` both reach a tracked name in argument position with no such step, so both
are touches, and a reader can determine that from the paragraph without either being listed.

Outside that class it remains a NAME-based AST check over one file, so a program stashed into a
container or an attribute (`sys.modules[spec.name] = module`, which the arm really does) and later
reached back through an expression naming none of the tracked names is outside it; so is a program
reached through a dynamic name, and so is a touch in another module. It also assumes the boundary is
still spelled `_guarded`: rename that function and every touch reports UNGUARDED, which is the right
direction to be wrong in. What it does cover, it covers by shape, so the next ordinary-looking line to
reach into a loaded program reds here at authoring time rather than in a later review round.

Usage:  guarded-boundary.py <gate.py>       # exits 3 and names each finding
"""
import ast
import sys

# The functions this file DECLARES as holding a loaded decision program. It is a floor, not the
# subject: the subject is derived below, so a NEW holder is checked without editing this file. The
# declared names are still asserted to exist, because a set that can rot into naming nothing would let
# this checker report `ok` over an empty subject — the exact shape it exists to refuse.
DECLARED_HOLDERS = {"decision_function", "merge_performs_act"}

# A call to one of these yields a loaded decision program. `module_from_spec` is where one enters this
# gate at all; `decision_function` is the arm's own loader, so its callers hold one too. Any function
# containing such a call is a holder, and whatever the call is bound to is tracked under any name.
LOADERS = {"module_from_spec", "decision_function"}

# The name the arm gives a loaded program. Seeded as well as derived: belt and braces, since a holder
# that obtained its module some other way still spells it this way today.
MODULE = "module"

GUARD = "_guarded"

# Calls whose RESULT is part of the loaded program rather than merely computed from it (.github#2667).
# `getattr` is the spelling the arm itself uses to lift `decide`; `vars` is `module.__dict__` wearing a
# call. A value read out through one of these IS the program's own state, so it stays tracked under
# whatever name it lands in — the dataflow step a name-and-handover walk cannot take by itself.
READERS = {"getattr", "vars"}

# Builtins that CANNOT run the object they are handed, exempted from the HANDOVER rule ONLY — never
# from the func-position rule, where `callable(...)` would mean calling a tracked value.
#
# Each earns its place by a property of CPython rather than by convenience: `callable(x)` is
# `tp_call != NULL`, `type(x)` is `Py_TYPE(x)`, `id(x)` is an address. None of the three reaches a
# `__getattr__`, a `__call__`, or any other program-defined slot. The exemption applies ONLY to the
# strictly unary form — `type(name, bases, ns)` is the class constructor and runs a metaclass, which
# is a different function wearing one name.
#
# THE TEST FOR A CANDIDATE, in the order that settles it soonest:
#
#   1. Could it ever qualify? `cannot_invoke` requires exactly one positional argument, so an
#      always-binary builtin is DEAD CODE in this set. `isinstance` is the example — listing it would
#      read as a considered exemption while never once applying. Structural ineligibility cannot rot,
#      which is why it is asked first.
#   2. Is it needed? The arm's `isinstance(decision, dict)` is already green because an INVOCATION
#      RESULT is never tracked. An allowlist entry that exists to excuse a value that should not have
#      been tracked is the weaker rule creeping back in.
#   3. Is the hook on the tracked argument, or somewhere else? This is the one that needs care.
#      `repr(x)` runs `x.__repr__` — the tracked object's own slot — so it is NOT inert and is
#      excluded. `isinstance(x, C)` runs `type(C).__instancecheck__`, which lives on the OTHER
#      argument's metaclass and never reaches `x`'s slots at all, so "not provably inert" would be the
#      wrong reason to exclude it. Ask whose slot the builtin reaches, not whether a hook exists.
NON_INVOKING = {"callable", "type", "id"}


def func_name(node):
    """The bare name a call's func ends in: `f` for `f(...)`, `g` for `a.b.g(...)`."""
    if isinstance(node, ast.Attribute):
        return node.attr
    if isinstance(node, ast.Name):
        return node.id
    return None


def is_loader_call(node):
    return isinstance(node, ast.Call) and func_name(node.func) in LOADERS


def reaches(node, tracked):
    """Does this expression evaluate to a tracked reference, or to an attribute/item of one?

    The subscript step is not decoration: `module.__dict__["decide"]` is the same reach as
    `module.decide` spelled so an attribute-chain walk cannot see it.

    A conditional and a boolean operator BRANCH to one of their operands rather than transforming it,
    so an expression that could evaluate to a tracked reference is one (.github#2667). Either branch
    suffices: `(decide if c else other)(x)` calls the program on one of two paths, and a checker that
    needed both would be asking for a guarantee no static walk can give. This is the same
    over-approximation the alias pass makes, for the same reason.
    """
    while True:
        if isinstance(node, ast.Name) and node.id in tracked:
            return True
        if isinstance(node, (ast.Attribute, ast.Subscript)):
            node = node.value
            continue
        if isinstance(node, ast.IfExp):
            return reaches(node.body, tracked) or reaches(node.orelse, tracked)
        if isinstance(node, ast.BoolOp):
            return any(reaches(value, tracked) for value in node.values)
        return False


def handed_over(node, tracked):
    """Is this ARGUMENT a tracked reference being handed to a call?

    Peels the shapes an argument can wear on the way in — `*[module]`, `**{"mod": module}`, a literal
    container — because every one of them hands the same object to the same callee. It deliberately
    does NOT recurse into a nested call or a lambda: a nested call is judged as a touch in its own
    right, and a lambda body is exactly what a guard defers.
    """
    if isinstance(node, ast.Starred):
        return handed_over(node.value, tracked)
    if isinstance(node, (ast.List, ast.Tuple, ast.Set)):
        return any(handed_over(element, tracked) for element in node.elts)
    if isinstance(node, ast.Dict):
        return any(value is not None and handed_over(value, tracked) for value in node.values)
    return reaches(node, tracked)


def cannot_invoke(node):
    """Is this call one of the few that provably cannot run what it is handed? (.github#2667)

    See NON_INVOKING. Strictly unary and positional: a second argument, a keyword, or a starred
    element means this is not the form whose inertness was established, so the exemption lapses and
    the ordinary handover rule applies.
    """
    return (
        func_name(node.func) in NON_INVOKING
        and len(node.args) == 1
        and not node.keywords
        and not isinstance(node.args[0], ast.Starred)
    )


def is_touch(node, tracked):
    if not isinstance(node, ast.Call):
        return False
    if reaches(node.func, tracked):
        # module.decide(...), m.decide(...), module.__dict__["decide"](...), and — since .github#2667
        # — decide(...) and decide.__call__(...), because a lifted value is tracked under its name.
        return True
    if cannot_invoke(node):
        return False  # callable(decide): the arm's own validation of the lift, and provably inert
    if any(handed_over(argument, tracked) for argument in node.args):
        return True  # getattr(module, ...), automation.completions(module, ...), map(decide, xs)
    if any(handed_over(keyword.value, tracked) for keyword in node.keywords):
        return True  # f(mod=module), f(**{"mod": module}), sorted(xs, key=decide)
    # `spec.loader.exec_module(module)` is caught by the argument rule above, but name it too so a
    # future `exec_module()` spelled without the argument cannot slip past.
    return func_name(node.func) == "exec_module"


def is_guard_call(node):
    return (
        isinstance(node, ast.Call)
        and isinstance(node.func, ast.Name)
        and node.func.id == GUARD
    )


def guard_thunk(node):
    """The ONE subtree a `_guarded(...)` call DEFERS, or None when it defers nothing.

    None is the answer for every shape that runs its second argument eagerly — a call, a comprehension,
    an f-string, a keyword spelling of either parameter, the wrong arity. Nothing about those is
    guarded, and saying so is what stops this checker blessing them.
    """
    if not is_guard_call(node):
        return None
    if len(node.args) != 2 or node.keywords:
        return None
    thunk = node.args[1]
    if isinstance(thunk, ast.Lambda):
        # The BODY, not the lambda: a default argument is evaluated where the lambda is built, which
        # is outside the guard.
        return thunk.body
    if isinstance(thunk, ast.Name):
        return thunk
    return None


def lifts_from(node, tracked):
    """Does this expression READ a value OUT of a loaded decision program? (.github#2667)

    This is the dataflow step `handed_over` deliberately does not take. It refuses to recurse into a
    nested call — correctly, because a nested call is judged as a touch in its own right — but that
    left the call's RESULT untracked, so a callable lifted out under the guard and invoked later was
    certified as safe. `LOADERS` already carries `decision_function` for exactly this reason: that
    entry is a hand-placed patch for the one instance of this gap somebody remembered, and the gap it
    patches is general.

    A READ, in any of its spellings: `module.decide` and `module.__dict__[k]`, which `reaches` already
    walks; `getattr(module, "decide", None)`; and any of those DEFERRED into a guard's thunk — the
    arm's own lift is the last of these, and the guard is why the value escaped tracking at all.

    NOT an invocation result. `decide(facts)` is a call THROUGH the program, not a read OUT of it, and
    what it returns is data the arm may handle freely; see WHAT IS DELIBERATELY NOT TRACKED. Reading
    the guard's THUNK rather than the `_guarded(...)` call is what keeps the two apart: `handed_over`
    does not peel a lambda, which is precisely what makes deferral mean something, and `guard_thunk`
    already computes the one subtree that is deferred.
    """
    if isinstance(node, ast.Call):
        thunk = guard_thunk(node)
        if thunk is not None:
            return lifts_from(thunk, tracked)
        return (
            func_name(node.func) in READERS
            and bool(node.args)
            and reaches(node.args[0], tracked)
        )
    return reaches(node, tracked)


def bindings(node):
    """(target, value) for every shape that can bind a name to an existing object."""
    if isinstance(node, ast.Assign):
        for target in node.targets:
            yield target, node.value
    elif isinstance(node, ast.AnnAssign) and node.value is not None:
        yield node.target, node.value
    elif isinstance(node, ast.NamedExpr):
        yield node.target, node.value
    elif isinstance(node, ast.For):
        yield node.target, node.iter
    elif isinstance(node, ast.withitem) and node.optional_vars is not None:
        yield node.optional_vars, node.context_expr


def bound_names(target):
    """The plain local names a binding target introduces.

    An attribute or subscript target introduces none — `sys.modules[spec.name] = module` stores the
    program somewhere this file cannot follow, which the module docstring states as a limit rather
    than pretends away.
    """
    return {
        node.id
        for node in ast.walk(target)
        if isinstance(node, ast.Name) and isinstance(node.ctx, ast.Store)
    }


def tracked_names(function):
    """Every local name in this function that holds a loaded decision program, or a value read out
    of one.

    ONE set, graded by one rule. .github#2667 adds a third way in — `lifts_from`, a READ out of a
    program — rather than a second, weaker tier, because a tier that had to be graded more leniently
    would have licensed `sorted(xs, key=decide)` and `map(decide, xs)` to escape.

    Run to a fixpoint, so `a = module` followed by `b = a` tracks both, and so does
    `d = _guarded(..., lambda: getattr(module, "decide", None))` followed by `e = d`.
    Over-approximating on purpose: the direction of error is a false flag, never a miss.
    """
    names = {MODULE}
    changed = True
    while changed:
        changed = False
        for node in ast.walk(function):
            for target, value in bindings(node):
                if not (
                    is_loader_call(value)
                    or handed_over(value, names)
                    or lifts_from(value, names)
                ):
                    continue
                for name in bound_names(target) - names:
                    names.add(name)
                    changed = True
    return names


def holders(tree):
    """The functions that hold a loaded decision program, DERIVED from the loader calls they make."""
    return {
        node.name: node
        for node in ast.walk(tree)
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))
        and any(is_loader_call(inner) for inner in ast.walk(node))
    }


def main(argv):
    if len(argv) != 1:
        sys.exit("usage: guarded-boundary.py <gate.py>")
    tree = ast.parse(open(argv[0], encoding="utf-8").read(), filename=argv[0])

    found = holders(tree)
    missing = sorted(DECLARED_HOLDERS - set(found))
    if missing:
        print(
            f"MAP ROT: {argv[0]} no longer contains a loaded-program holder named "
            f"{', '.join(missing)}. This checker's subject is derived from the functions that call "
            f"{', '.join(sorted(LOADERS))}, and DECLARED_HOLDERS is the floor asserting that "
            f"derivation still finds the holders it was written against. Renamed? Deleted? Update "
            f"DECLARED_HOLDERS deliberately — a checker that silently narrows its own subject is the "
            f"shape this file exists to refuse.",
            file=sys.stderr,
        )
        return 2

    unguarded = []
    malformed = []
    for name in sorted(found):
        function = found[name]
        tracked = tracked_names(function)
        # The subtrees a guard actually defers, collected first: a lambda's body is a GRANDCHILD of
        # the `_guarded(...)` call, so guardedness cannot be decided from the parent link alone.
        deferred = {
            id(thunk)
            for node in ast.walk(function)
            if (thunk := guard_thunk(node)) is not None
        }
        # Walk with an explicit ancestor stack; `ast.walk` alone cannot answer "is this inside a
        # deferred thunk", which is the entire question.
        stack = [(function, False)]
        while stack:
            node, guarded = stack.pop()
            guarded = guarded or id(node) in deferred
            if is_guard_call(node) and guard_thunk(node) is None:
                malformed.append((name, node.lineno, ast.unparse(node)))
            if is_touch(node, tracked) and not guarded:
                unguarded.append((name, node.lineno, ast.unparse(node)))
            for child in ast.iter_child_nodes(node):
                stack.append((child, guarded))

    if malformed:
        print(
            f"NOT A THUNK: {len(malformed)} `{GUARD}(...)` call(s) evaluate their second argument "
            f"BEFORE the guard runs, so whatever it touches happens outside the boundary. "
            f"`{GUARD}(what, call)` takes a THUNK — pass `lambda: <the call>`, never the call itself.",
            file=sys.stderr,
        )
        for where, line, source in sorted(malformed):
            print(f"  {argv[0]}:{line} in {where}(): {source}", file=sys.stderr)

    if unguarded:
        print(
            f"UNGUARDED: {len(unguarded)} touch(es) of a loaded decision program sit outside "
            f"`{GUARD}`. A `sys.exit(0)` reached through any of them exits this gate ZERO, silently.",
            file=sys.stderr,
        )
        for where, line, source in sorted(unguarded):
            print(f"  {argv[0]}:{line} in {where}(): {source}", file=sys.stderr)

    if malformed or unguarded:
        return 3
    print(
        f"ok: every touch of a loaded decision program in {', '.join(sorted(found))} is inside "
        f"`{GUARD}`."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
