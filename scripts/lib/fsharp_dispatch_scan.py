"""Structural reader for the dispatch that runs in Coord.Cli `main` before the typed handler table.

Shared by the v1 writer census (scripts/check-v1-writer-census.py), which pins this file's hash. The scan is
deliberately structural rather than a single regex: arms are read at the column of the first arm after the
outermost `match`; same-line bodies, `when` guards, trailing comments and stacked or-patterns are accepted;
and every dispatch-shaped call is cross-checked, so an unfamiliar spelling cannot slip past an arm-only
reader. Anything dispatch-shaped that it cannot place is an error, not a pass.
"""

from __future__ import annotations

import re


class DispatchScanError(RuntimeError):
    pass


MAIN_START = "let main argv"
HANDLER_DISPATCH = "commandHandlers[opts.Command]"
# A call into another module that runs or dispatches something: X.tryRun, X.run, X.tryExecute, X.executeWithContext ...
DISPATCH_REFERENCE = re.compile(r"\b(?P<module>[A-Za-z_][\w]*(?:\.[A-Za-z_]\w*)*)\.(?P<method>(?:try)?(?:run|execute|dispatch|handle|invoke|exec)\w*)", re.I)
MATCH_LINE = re.compile(r"^(?P<indent> *)(?:.*\b)?match\b.*\bwith\s*$")


def _strip_fsharp_comments(text: str) -> str:
    """Blank `//` and nested `(* *)` comments, keeping string literals, newlines and columns."""
    out: list[str] = []
    index, depth, length = 0, 0, len(text)
    while index < length:
        char = text[index]
        pair = text[index:index + 2]
        if depth:
            if pair == "(*":
                depth += 1
                out.append("  ")
                index += 2
            elif pair == "*)":
                depth -= 1
                out.append("  ")
                index += 2
            else:
                out.append("\n" if char == "\n" else " ")
                index += 1
        elif pair == "(*" and text[index:index + 3] != "(*)":
            depth = 1
            out.append("  ")
            index += 2
        elif pair == "//":
            while index < length and text[index] != "\n":
                out.append(" ")
                index += 1
        elif char == '"':
            triple = text[index:index + 3] == '"""'
            end = index + 3 if triple else index + 1
            while end < length:
                if triple and text[end:end + 3] == '"""':
                    end += 3
                    break
                if not triple and text[end] == "\\":
                    end += 2
                    continue
                if not triple and text[end] == '"':
                    end += 1
                    break
                end += 1
            out.append(text[index:end])
            index = end
        else:
            out.append(char)
            index += 1
    return "".join(out)


def _arrow_index(line: str) -> int:
    """Index of the first `->` outside a string literal, or -1."""
    inside = False
    index = 0
    while index < len(line):
        if line[index] == "\\" and inside:
            index += 2
            continue
        if line[index] == '"':
            inside = not inside
        elif not inside and line.startswith("->", index):
            return index
        index += 1
    return -1


def pre_handler_dispatch(text: str) -> list[str]:
    """Every dispatch arm and dispatcher of `main` that precedes the typed handler table."""
    start = text.find(MAIN_START)
    end = text.find(HANDLER_DISPATCH, start)
    if start < 0 or end < 0:
        raise DispatchScanError(f"main no longer has the main/{HANDLER_DISPATCH} shape this guard reads")
    lines = _strip_fsharp_comments(text[start:end]).splitlines()
    if any(re.search(r"\bfunction\b", line) for line in lines):
        raise DispatchScanError("main uses a `function` dispatch this guard does not read; extend the guard")

    def indent(line: str) -> int:
        return len(line) - len(line.lstrip(" "))

    # The outermost match: the least-indented `match ... with` line. Its arms define the arm column.
    matches = [(i, indent(line)) for i, line in enumerate(lines) if MATCH_LINE.match(line)]
    if not matches:
        raise DispatchScanError("main shows no `match ... with` dispatch; the guard cannot read it")
    outer = min(column for _, column in matches)
    first_match = next(i for i, column in matches if column == outer)
    arm_lines = [i for i in range(first_match + 1, len(lines)) if lines[i].lstrip().startswith("|")]
    if not arm_lines:
        raise DispatchScanError("main shows no dispatch arm after its outer match")
    arm_column = indent(lines[arm_lines[0]])
    if arm_column < outer:
        raise DispatchScanError("main has a dispatch arm left of its match; the guard cannot read it")
    stray = [lines[i].strip() for i in arm_lines if indent(lines[i]) < arm_column]
    if stray:
        raise DispatchScanError(f"main has dispatch arm(s) at an unexpected indent: " + "; ".join(stray))
    top = [i for i in arm_lines if indent(lines[i]) == arm_column]

    keys: list[str] = []
    spans: list[tuple[int, int, str]] = []
    pending: list[str] = []
    pending_start = 0
    for position, i in enumerate(top):
        stop = top[position + 1] if position + 1 < len(top) else len(lines)
        # The pattern may continue over lines, or be stacked as `| A` / `| B ->` (an or-pattern).
        joined, j = lines[i].strip()[1:].strip(), i
        while _arrow_index(joined) < 0 and j + 1 < stop:
            j += 1
            joined += " " + lines[j].strip()
        arrow = _arrow_index(joined)
        if arrow < 0:
            if not pending:
                pending_start = i
            pending.append(joined)
            continue
        key = re.sub(r"\s+", " ", " | ".join(pending + [joined[:arrow]])).strip()
        spans.append((pending_start if pending else i, stop, key))
        pending = []
        if key != "_":
            if key in keys:
                raise DispatchScanError(f"main repeats a pre-handler dispatch arm: {key}")
            keys.append(key)
    if pending:
        raise DispatchScanError(f"main ends with an arm that has no `->`: {pending[-1]}")

    # Cross-check every dispatch-shaped call. Inside a named arm it belongs to that arm's row; anywhere else
    # (the wildcard arm, the lines before the first arm, a later `if ... then X.run`) it is a dispatcher.
    named = {(low, high) for low, high, key in spans if key != "_"}
    dispatchers: list[str] = []
    for i, line in enumerate(lines):
        if any(low <= i < high for low, high in named):
            continue
        for match in DISPATCH_REFERENCE.finditer(line):
            reference = f"{match.group('module')}.{match.group('method')}"
            if reference not in dispatchers:
                dispatchers.append(reference)
    arms = keys + dispatchers
    if not arms:
        raise DispatchScanError("main shows no pre-handler dispatch; the guard cannot read it")
    return arms
