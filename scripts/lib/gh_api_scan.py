"""Shared shell-text scan for `gh api` invocations that can send a non-GET request.

One tokeniser serves the producer fence (tests/producer-fence-attacks/run.py) and the v1 writer census
(scripts/check-v1-writer-census.py), so the two cannot disagree about what is a write.

`gh api` is a GET unless it is given a method other than GET or HEAD, a request body (the input flag),
or a field (the f, F, field and raw-field flags, which make it a POST). Flags may precede or follow the
endpoint, and an invocation may continue over a backslash-newline or, in a YAML folded or plain
multi-line scalar, over the physical line break. Each invocation is therefore tokenised up to the next
shell command boundary. A method whose value is not literally GET or HEAD (for example a variable) is
treated as a write: this scan fails closed on what it cannot resolve.

Known limits: flags held in a variable, gh global flags placed before the api word, and quoted
multi-line YAML scalars are not resolved.
"""

from __future__ import annotations

import re
import shlex

GH_API = re.compile(r"\bgh\s+api\b", re.I)
# `(` and `)` are deliberately absent: `gh api "$(echo -f x)"` and `gh api $(printf -- -f) x` carry the
# command's own flags inside a substitution, and a subshell's closing paren does not end an argument list
# that the scan can safely stop reading. Over-refusal there is the accepted failure direction.
SHELL_COMMAND_BOUNDARY = {";", ";;", "&&", "||", "|", "|&", "&"}
READ_ONLY_METHODS = {"GET", "HEAD"}

_BLOCK_HEADER = re.compile(r"^(?P<prefix>\s*(?:-\s+)*)(?P<key>[^\s#'\"\[{|>&*!-][^:#]*|\"[^\"]*\"|'[^']*'):\s*(?P<indicator>[|>])[-+0-9]*\s*$")
_PLAIN_HEADER = re.compile(r"^(?P<prefix>\s*(?:-\s+)*)(?P<key>[^\s#'\"\[{|>&*!-][^:#]*):\s+(?P<value>[^\s|>'\"\[{&*!#].*)$")


def _fold_yaml_scalars(text: str) -> str:
    """Join the physical lines of YAML folded (`>`) and plain multi-line scalars with a space.

    The shell sees a folded scalar as one line, so a flag on the next physical line belongs to the same
    command. Literal (`|`) bodies keep their newlines. Joining is only ever wider than the real folding,
    which can over-refuse and cannot under-refuse."""
    lines = text.splitlines()
    out: list[str] = []
    index = 0
    while index < len(lines):
        line = lines[index]
        block = _BLOCK_HEADER.match(line)
        plain = None if block else _PLAIN_HEADER.match(line)
        if block or plain:
            match = block or plain
            key_column = len(match.group("prefix"))
            folded = bool(plain) or match.group("indicator") == ">"
            body: list[str] = []
            cursor = index + 1
            while cursor < len(lines):
                candidate = lines[cursor]
                if candidate.strip() and len(candidate) - len(candidate.lstrip()) <= key_column:
                    break
                body.append(candidate)
                cursor += 1
            if folded:
                merged = line
                for item in body:
                    if item.strip():
                        merged += " " + item.strip()
                    else:
                        out.append(merged)
                        merged = ""
                out.append(merged)
            else:
                out.append(line)
                out.extend(body)
            index = cursor
            continue
        out.append(line)
        index += 1
    return "\n".join(out)


def _invocation_tokens(rest: str) -> list[str]:
    try:
        lexer = shlex.shlex(rest, posix=True, punctuation_chars=True)
        lexer.whitespace_split = True
        lexer.commenters = ""
        words = list(lexer)
    except ValueError:
        words = [word.strip("\"'") for word in rest.split()]
    tokens: list[str] = []
    for word in words:
        # shlex joins adjacent operator characters, so `);` and `)&&(` arrive as one word; parentheses alone
        # never end the argument list, the operator inside them does.
        if word.strip("()") in SHELL_COMMAND_BOUNDARY:
            break
        tokens.append(word)
    return tokens


def gh_api_writes(text: str, *, yaml: bool = False) -> bool:
    """True when any `gh api` invocation in `text` can send a non-GET request."""
    if yaml:
        text = _fold_yaml_scalars(text)
    joined = re.sub(r"\\[ \t]*\r?\n", " ", text)
    for line in joined.splitlines():
        for match in GH_API.finditer(line):
            tokens = _invocation_tokens(line[match.end():])
            for index, token in enumerate(tokens):
                value: str | None = None
                if token in ("-X", "--method"):
                    value = tokens[index + 1] if index + 1 < len(tokens) else ""
                elif token.startswith("--method="):
                    value = token[len("--method="):]
                elif re.fullmatch(r"-X=?.+", token):
                    value = token[2:].lstrip("=")
                if value is not None and value.upper() not in READ_ONLY_METHODS:
                    return True
                if token in ("--field", "--raw-field", "--input") or re.match(r"--(?:field|raw-field|input)=", token):
                    return True
                if re.match(r"-[A-Za-z]*[fF]", token) and not token.startswith("--"):
                    return True
    return False
