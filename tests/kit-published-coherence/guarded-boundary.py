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
`m.decide(...)`, `module.__dict__["decide"](...)`), or one that HANDS a tracked reference over as a
positional argument, a keyword argument, a starred element, or an entry of a literal container
(`getattr(module, ...)`, `f(mod=module)`, `f(*[module])`, `f(**{"mod": module})`), or any
`exec_module(...)`.

WHAT COUNTS AS A TRACKED REFERENCE. The name `module`, whatever a call to a LOADERS function is bound
to, and every local name aliased from one of those. The alias pass deliberately OVER-approximates —
`a, b = module, other` marks both — because a false flag costs an author one line of explanation while
a miss costs a silent exit 0.

WHAT THIS CANNOT SEE, stated because a checker that overstates its reach is worse than one that does
not exist. It is a NAME-based AST check over one file, so a program stashed into a container or an
attribute (`sys.modules[spec.name] = module`, which the arm really does) and later reached back
through an expression that names none of the tracked names is outside it; so is a program reached
through a dynamic name, and so is a touch in another module. It also assumes the boundary is still
spelled `_guarded`: rename that function and every touch reports UNGUARDED, which is the right
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
    """
    while True:
        if isinstance(node, ast.Name) and node.id in tracked:
            return True
        if isinstance(node, (ast.Attribute, ast.Subscript)):
            node = node.value
            continue
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


def is_touch(node, tracked):
    if not isinstance(node, ast.Call):
        return False
    if reaches(node.func, tracked):
        return True  # module.decide(...), m.decide(...), module.__dict__["decide"](...)
    if any(handed_over(argument, tracked) for argument in node.args):
        return True  # getattr(module, ...), automation.completions(module, ...), f(*[module])
    if any(handed_over(keyword.value, tracked) for keyword in node.keywords):
        return True  # f(mod=module), f(**{"mod": module}), f(**kwargs)
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
    """Every local name in this function that holds a loaded decision program.

    Run to a fixpoint, so `a = module` followed by `b = a` tracks both. Over-approximating on purpose:
    the direction of error is a false flag, never a miss.
    """
    names = {MODULE}
    changed = True
    while changed:
        changed = False
        for node in ast.walk(function):
            for target, value in bindings(node):
                if not (is_loader_call(value) or handed_over(value, names)):
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
