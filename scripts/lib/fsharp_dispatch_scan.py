"""Structural reader for the dispatch that runs in Coord.Cli `main` before the typed handler table.

Shared by the v1 writer census (scripts/check-v1-writer-census.py), which pins this file's hash. The scan is
deliberately structural rather than a single regex: arms are read at the column of the first arm after the
outermost `match`; same-line bodies, `when` guards, trailing comments and stacked or-patterns are accepted;
and every dispatch-shaped call is cross-checked, so an unfamiliar spelling cannot slip past an arm-only
reader. Outside a listed arm the scan is an allow-list: every token must be a known-safe construct below or a
census-listed dispatcher, so an unfamiliar call, a bare or let-bound call, a lambda or a nested arm is an error,
not a pass.
"""

from __future__ import annotations

import re


class DispatchScanError(RuntimeError):
    pass


MAIN_START = "let main argv"
HANDLER_DISPATCH = "commandHandlers[opts.Command]"
# The end marker is located in the code view of the file (comments and string contents blanked), so a comment or
# a string that merely spells it cannot truncate the scanned segment before an evil call.
HANDLER_DISPATCH_CODE = re.compile(r"commandHandlers\s*\[\s*opts\s*\.\s*Command\s*\]")
# A call into another module that runs or dispatches something: X.tryRun, X.run, X.tryExecute, X.executeWithContext ...
DISPATCH_REFERENCE = re.compile(r"\b(?P<module>[^\W\d]\w*(?:\.[^\W\d]\w*)*)\.(?P<method>(?:try)?(?:run|execute|dispatch|handle|invoke|exec)\w*)", re.I)
MATCH_LINE = re.compile(r"^(?P<indent> *)(?:.*\b)?match\b.*\bwith\s*$")


# Tokens that may appear in `main` outside a listed arm (before the first arm and in the wildcard arm), each with
# the reason it is safe. Anything else must be a census-listed dispatcher or the scan refuses. Adding a token
# here is a review decision: it is the only way new code can run in this region without a census row.
SAFE_TOKENS = {
    "let": "binding keyword for the two bindings below",
    "mutable": "the invoked-name cell",
    "main": "the entry point's own name",
    "argv": "the process arguments",
    "arguments": "the argument list bound once from argv",
    "invoked": "the telemetry name cell assigned by each arm",
    "List.ofArray": "converts argv; no effect",
    "try": "the enclosing try/with",
    "with": "the enclosing match/try/with and record-with",
    "match": "dispatch on the argument list and on tryRun/parse results",
    "Some": "option pattern of a tryRun result",
    "None": "option pattern of a tryRun result",
    "Ok": "result pattern of Options.parse",
    "Error": "result pattern of Options.parse",
    "exitCode": "the exit code returned by a tryRun dispatcher",
    "message": "the parse error text printed to stderr",
    "opts": "the parsed options",
    "opts.Command": "the parsed command used for the invoked name and the handler lookup",
    "Options.parse": "argument parser with no remote effect: it reads one environment variable and checks whether file options are present",
    "Options.usage": "constant usage text",
    "Options.commandName": "pure command-name lookup",
    "eprint": "writes to stderr",
    "ExitError": "exit-code constant",
    "_": "the wildcard pattern of the outer match",
}
# Patterns of the arms that may be nested under the wildcard arm: results of the dispatchers and of the parse.
ALLOWED_NESTED_PATTERNS = {"Some exitCode", "None", "Error message", "Ok opts"}
# Identifiers are Unicode-aware: F# accepts non-ASCII letters, so `Δ.Σ arguments` is a call, not nothing.
IDENTIFIER = r"\.?[^\W\d][\w']*(?:\.[^\W\d][\w']*)*"
# Operators are tokens too: a custom operator such as `|>!` can run any code defined elsewhere.
OPERATOR = r"[!%&*+\-/<=>?@^|~:]+"
TOKEN = re.compile(IDENTIFIER + "|" + OPERATOR)
# Operators that may appear outside a listed arm, each with the reason it is safe.
SAFE_OPERATORS = {
    "=": "binding and comparison; no call",
    "<-": "assignment to the invoked-name cell",
    "->": "match arm arrow",
    "::": "list cons in patterns",
    "|": "match arm bar",
}


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


def _code_without_strings(text: str) -> str:
    """The text with string literals emptied; the `{expr}` holes of interpolated strings stay as code."""
    out: list[str] = []
    index, length = 0, len(text)
    while index < length:
        char = text[index]
        if char != '"':
            out.append(char)
            index += 1
            continue
        interpolated = bool(out) and out[-1] == "$"
        triple = text[index:index + 3] == '"""'
        end = index + (3 if triple else 1)
        out.append('""')
        while end < length:
            if triple and text.startswith('"""', end):
                end += 3
                break
            if not triple and text[end] == "\\":
                end += 2
                continue
            if not triple and text[end] == '"':
                end += 1
                break
            if interpolated and text[end] == "{":
                if text.startswith("{{", end):
                    end += 2
                    continue
                close = text.find("}", end)
                close = length if close < 0 else close
                out.append(" " + text[end + 1:close] + " ")
                end = close + 1
                continue
            end += 1
        index = end
    return "".join(out)


def _blank_strings(text: str) -> str:
    """Length-preserving: string literal contents (and their interpolation holes) become spaces."""
    out: list[str] = []
    index, length = 0, len(text)
    while index < length:
        if text[index] != '"':
            out.append(text[index])
            index += 1
            continue
        triple = text.startswith('"""', index)
        end = index + (3 if triple else 1)
        while end < length:
            if triple and text.startswith('"""', end):
                end += 3
                break
            if not triple and text[end] == "\\":
                end += 2
                continue
            if not triple and text[end] == '"':
                end += 1
                break
            end += 1
        end = min(end, length)
        out.append("".join("\n" if c == "\n" else " " for c in text[index:end]))
        index = end
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


def pre_handler_dispatch(text: str, listed: set[str] | frozenset[str] = frozenset()) -> list[str]:
    """Every dispatch arm and dispatcher of `main` that precedes the typed handler table.

    `listed` is the set of census rows; a token that equals one is an allowed dispatcher."""
    # Locate `main` and its end marker in the code view (comments and strings blanked, length preserved), and take
    # the LAST real code occurrence of the marker: a comment or string that spells it, or an earlier dead use, must
    # not truncate the segment ahead of a call.
    without_comments = _strip_fsharp_comments(text)
    code_view = _blank_strings(without_comments)
    start = code_view.find(MAIN_START)
    ends = [m.start() for m in HANDLER_DISPATCH_CODE.finditer(code_view, max(start, 0))]
    if start < 0 or not ends:
        raise DispatchScanError(f"main no longer has the main/{HANDLER_DISPATCH} shape this guard reads")
    end = ends[-1]
    lines = without_comments[start:end].splitlines()
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

    # Outside a named arm (before the first arm, the wildcard arm and anything nested in it) only an allow-list is
    # accepted: a known-safe token, a census-listed dispatcher, or a dispatcher-shaped call that the caller then
    # reports as unlisted. Any other call, bare call, let-bound call, lambda or nested arm is refused.
    named = {(low, high) for low, high, key in spans if key != "_"}
    dispatchers: list[str] = []
    region = [i for i in range(len(lines)) if not any(low <= i < high for low, high in named)]
    for i in region:
        stripped = lines[i].strip()
        if stripped.startswith("|") and indent(lines[i]) != arm_column:
            arrow = _arrow_index(stripped)
            pattern = re.sub(r"\s+", " ", stripped[1:arrow if arrow >= 0 else None]).strip()
            if pattern not in ALLOWED_NESTED_PATTERNS:
                raise DispatchScanError(f"main has a nested arm under the wildcard that is not an allowed result pattern: {pattern}")
    code = _code_without_strings("\n".join(lines[i] for i in region))
    unknown: list[str] = []
    for token in TOKEN.findall(code):
        if token in SAFE_OPERATORS:
            continue
        if token in SAFE_TOKENS or token in listed:
            if token in listed and token not in dispatchers:
                dispatchers.append(token)
        elif DISPATCH_REFERENCE.fullmatch(token):
            if token not in dispatchers:
                dispatchers.append(token)
        elif token not in unknown:
            unknown.append(token)
    # An unlisted dispatcher-shaped call is reported by name as a missing census row; otherwise unknown is fatal.
    if unknown and all(d in listed for d in dispatchers):
        raise DispatchScanError(
            "main has " + ", ".join(f"`{token}`" for token in unknown) + " outside every listed arm; only the known-safe "
            "tokens in SAFE_TOKENS or a census-listed dispatcher may run there. List it as an arm or dispatcher, or add it "
            "to SAFE_TOKENS with a reason")
    arms = keys + dispatchers
    if not arms:
        raise DispatchScanError("main shows no pre-handler dispatch; the guard cannot read it")
    return arms
