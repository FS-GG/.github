#!/usr/bin/env python3
"""Is EVERY touch of a loaded decision program inside the fail-closed boundary? (.github#2571 round 2)

The obligation arm imports another program to ask it a question. Every line that then reaches into
that program — an import, an attribute lookup, a call, or handing the module to something that will
do one of those — is a place the program's code runs, and `SystemExit` is a `BaseException`, so an
unguarded one exits this gate ZERO in silence.

That happened twice in two rounds, both times by adding an ordinary-looking line, and neither time did
the behaviour fixture red: the round-1 repair put `patch_tuple` outside the guard, and an audit then
found a PEP 562 `__getattr__` hole at the `decide` lookup that had been there since the arm was
written. Behaviour legs can only cover the holes someone thought of. This covers the shape.

THE RULE, checked over the AST rather than by grep or by eye: inside the functions that hold a loaded
module, every call that could execute that program's code must have a `_guarded(...)` call among its
ancestors. Concretely a node is a touch when it is a call whose func reaches the name `module`, a call
passing `module` as an argument, a `getattr(module, ...)`, or `spec.loader.exec_module(...)`.

Usage:  guarded-boundary.py <gate.py>       # exits 3 and names each unguarded touch
"""
import ast
import sys

HOLDERS = {"decision_function", "merge_performs_act"}
MODULE = "module"


def reaches_module(node):
    """Does this expression bottom out at the local name `module`?"""
    while isinstance(node, ast.Attribute):
        node = node.value
    return isinstance(node, ast.Name) and node.id == MODULE


def is_touch(node):
    if not isinstance(node, ast.Call):
        return False
    if reaches_module(node.func):
        return True  # module.decide(...), module.thing.other(...)
    if any(reaches_module(arg) for arg in node.args):
        return True  # getattr(module, ...), automation.completions(module, ...)
    func = node.func
    # `spec.loader.exec_module(module)` is caught by the argument rule above, but name it too so a
    # future `exec_module()` spelled without the argument cannot slip past.
    return isinstance(func, ast.Attribute) and func.attr == "exec_module"


def is_guard(node):
    return (
        isinstance(node, ast.Call)
        and isinstance(node.func, ast.Name)
        and node.func.id == "_guarded"
    )


def main(argv):
    if len(argv) != 1:
        sys.exit("usage: guarded-boundary.py <gate.py>")
    tree = ast.parse(open(argv[0], encoding="utf-8").read(), filename=argv[0])

    unguarded = []
    for function in ast.walk(tree):
        if not isinstance(function, ast.FunctionDef) or function.name not in HOLDERS:
            continue
        # Walk with an explicit ancestor stack; `ast.walk` alone cannot answer "is this inside a
        # `_guarded` call", which is the entire question.
        stack = [(function, False)]
        while stack:
            node, guarded = stack.pop()
            guarded = guarded or is_guard(node)
            if is_touch(node) and not guarded:
                unguarded.append((function.name, node.lineno, ast.unparse(node)))
            for child in ast.iter_child_nodes(node):
                stack.append((child, guarded))

    if unguarded:
        print(
            f"UNGUARDED: {len(unguarded)} touch(es) of a loaded decision program sit outside "
            f"`_guarded`. A `sys.exit(0)` reached through any of them exits this gate ZERO, silently.",
            file=sys.stderr,
        )
        for where, line, source in sorted(unguarded):
            print(f"  {argv[0]}:{line} in {where}(): {source}", file=sys.stderr)
        return 3
    print(
        f"ok: every touch of a loaded decision program in {', '.join(sorted(HOLDERS))} is inside "
        f"`_guarded`."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
