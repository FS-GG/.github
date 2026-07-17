#!/usr/bin/env python3
"""Assert every parity fixture holds the HTTP framing properties #939 established.

.github#940, epic #266 (gates that fail open). Found while working .github#761 (PR #939).

WHY A GATE, AND NOT JUST THE REPAIR

PR #939 repaired the parity corpus's long-running random red. The repair is CONVENTION-ONLY:
nothing makes it fail loudly if it is undone. Two properties are load-bearing, and both were
established by hand across 41 fixtures:

  1. `protocol_version = "HTTP/1.1"` over ThreadingHTTPServer. The stdlib default is HTTP/1.0,
     which closes the connection after every response (1435 closes in one run) and races the
     engine's pooling HttpClient — an RST discards an already-written response, and the corpus
     reds at random on a contended runner.
  2. NO BODY ON A 204. `done_server`/`restore_server` answered a 204 with `{}`; a conforming
     client does not read a 204 body, so those two bytes became the NEXT response's status line
     (`Received an invalid status line: '{}HTTP/1.1 200 OK'`). HTTP/1.0's close-per-response hid
     this for the life of the suite.

A worker who adds fixture #42 by copying a pre-#939 file out of git history, or who drops
`protocol_version` while editing, reintroduces either defect SILENTLY. The symptom is the one that
already cost three workers a session each: a red on a DIFFERENT assertion each time, on `main` as
readily as on a PR, not reproducible on a fast box.

NOTHING TYPE-CHECKS A TEST FIXTURE'S HTTP FRAMING. That is the whole disease, and it is the same
reasoning `check-recipe-pagination.py` and `check-recipe-landable.py` are built on: the rule gets a
gate, so it cannot rot. What is at stake is the credibility of the cut-over gate (ADR-0038) — a
corpus that reds at random teaches exactly one lesson, "parity red is noise, re-run it", and the
next red will be real.

WHY THE PARSER, AND NOT grep

The corpus's own idiom defeats a textual check in BOTH directions:

  * `return self._send(204, {})` is SAFE — `_send` guards the code internally and writes no body.
    A grep for `_send(204,` flags 2 correct call sites and teaches people to ignore the gate.
  * `self.send_response(204)` inside `_send`'s early-return guard is followed, LATER IN THE SAME
    FUNCTION, by `self.wfile.write(b)` — the 200 path. A grep for "a write after a 204" flags all
    41 fixtures.

So the 204 rule is answered structurally: find the block where `send_response(204)` is a direct
statement, and scan its FOLLOWING SIBLINGS ONLY, stopping at the `return` that ends the guard.

WHAT IS CHECKED, AND WHAT IS DELIBERATELY NOT

  R1 threading-server  every fixture constructs ThreadingHTTPServer, never a bare HTTPServer.
  R2 protocol-version  the handler class declares protocol_version = "HTTP/1.1", exactly.
  R3 no-204-body       no response body is written on a 204.
  R4 drain-body        do_POST / do_PATCH / do_PUT read the request body.

R4 covers the methods that carry a body TODAY (37 do_POST, 6 do_PATCH — all draining) and do_PUT,
which no fixture defines yet but which would carry one the moment a fixture answers a PUT. It does
NOT cover:

  * do_GET  — a GET has no body to drain. Requiring it would be a rule about nothing.
  * do_DELETE — #940 asks for this and the gate does not enforce it, deliberately. All 7 do_DELETE
    handlers currently drain NOTHING, and the defect is latent-but-unreachable: the engine's only
    DELETE sets `Body = NoBody`, so no body is ever sent to un-drain. Enforcing it here would red
    `main` on 7 fixtures, and the fix belongs in those fixtures — which live under
    `tests/coord-engine-parity/`, a touch-set held by .github#1028 at the time this was written.
    A gate that reds `main` on work its author may not do is worse than the gap it closes. This is
    recorded rather than dropped: see #940 and the PR that added this file.

The exemption is NOT "DELETE is safe forever". It is "DELETE is unreachable today, and the rule
that would make it reachable is the engine's, not the fixture's". If the engine ever sends a
body-bearing DELETE, R4 is the line to widen — and it is one string in BODY_BEARING.
"""

import ast
import sys
from pathlib import Path

FIXTURES = "tests/coord-engine-parity"
GLOB = "*_server.py"

REQUIRED_PROTOCOL = "HTTP/1.1"
HANDLER_BASE = "BaseHTTPRequestHandler"

# The methods a fixture must drain. See the module docstring for why do_GET and do_DELETE are not
# here — the first has no body by construction, the second has none REACHABLE today.
BODY_BEARING = ("do_POST", "do_PATCH", "do_PUT")


def _calls(node: ast.AST, name: str) -> list[ast.Call]:
    """Every Call to `name` in this subtree, spelled bare or as an attribute."""
    out = []
    for n in ast.walk(node):
        if not isinstance(n, ast.Call):
            continue
        f = n.func
        if isinstance(f, ast.Name) and f.id == name:
            out.append(n)
        elif isinstance(f, ast.Attribute) and f.attr == name:
            out.append(n)
    return out


def _is_204(call: ast.Call) -> bool:
    return bool(call.args) and isinstance(call.args[0], ast.Constant) and call.args[0].value == 204


def _writes_body(node: ast.AST) -> bool:
    """Does this subtree write to the response stream?"""
    for n in ast.walk(node):
        if isinstance(n, ast.Attribute) and n.attr == "write":
            v = n.value
            if isinstance(v, ast.Attribute) and v.attr == "wfile":
                return True
    return False


def check_threading_server(tree: ast.AST, rel: str) -> list[str]:
    if _calls(tree, "HTTPServer"):
        return [
            f"{rel}: constructs a bare HTTPServer. Use ThreadingHTTPServer — the single-threaded "
            f"server serialises the engine's pooled connections and reintroduces #761's race (R1)."
        ]
    if not _calls(tree, "ThreadingHTTPServer"):
        return [
            f"{rel}: constructs no ThreadingHTTPServer. Every parity fixture must serve over one "
            f"(R1) — see #939/#761."
        ]
    return []


def check_protocol_version(tree: ast.AST, rel: str) -> list[str]:
    handlers = [
        n
        for n in ast.walk(tree)
        if isinstance(n, ast.ClassDef)
        and any(
            (isinstance(b, ast.Name) and b.id == HANDLER_BASE)
            or (isinstance(b, ast.Attribute) and b.attr == HANDLER_BASE)
            for b in n.bases
        )
    ]
    if not handlers:
        # An empty subject is a finding, not a pass — this gate's own lesson, applied one level in (#266).
        return [f"{rel}: defines no {HANDLER_BASE} subclass, so R2 has no subject to check."]

    findings = []
    for h in handlers:
        found = None
        for stmt in h.body:
            targets = (
                stmt.targets
                if isinstance(stmt, ast.Assign)
                else [stmt.target] if isinstance(stmt, ast.AnnAssign) else []
            )
            for t in targets:
                if isinstance(t, ast.Name) and t.id == "protocol_version":
                    found = stmt.value
        if found is None:
            findings.append(
                f"{rel}:{h.lineno}: class {h.name} declares no protocol_version. The stdlib default "
                f'is HTTP/1.0, which closes per response and races the engine\'s pooling client — the '
                f'#761 random red. Add:  protocol_version = "{REQUIRED_PROTOCOL}"  (R2)'
            )
        elif not (isinstance(found, ast.Constant) and found.value == REQUIRED_PROTOCOL):
            got = ast.dump(found) if not isinstance(found, ast.Constant) else repr(found.value)
            findings.append(
                f"{rel}:{h.lineno}: class {h.name} sets protocol_version = {got}, not "
                f'"{REQUIRED_PROTOCOL}" (R2).'
            )
    return findings


def _guards_204(fn: ast.FunctionDef) -> bool:
    """Does this body-writing helper early-return on a 204 before writing?

    The corpus's guard is:

        if code == 204:
            self.send_response(204)
            self.end_headers()
            return

    So: an `if` whose test mentions 204, whose branch returns, and which writes no body on the way.
    """
    for n in ast.walk(fn):
        if not isinstance(n, ast.If):
            continue
        if not any(isinstance(c, ast.Constant) and c.value == 204 for c in ast.walk(n.test)):
            continue
        branch = n.body
        returns = any(isinstance(s, ast.Return) for s in branch)
        writes = any(_writes_body(s) for s in branch)
        if returns and not writes:
            return True
    return False


def check_helper_204(tree: ast.AST, rel: str) -> list[str]:
    """A body-writing helper called with a literal 204 must guard it.

    THIS is the rule that catches the actual #761 defect, and the sibling rule below does not: the
    pre-#939 `_send` wrote its body unconditionally and sent the code DYNAMICALLY
    (`self.send_response(code)`), so there is no literal 204 anywhere near the write. The defect
    lives entirely in the CALL — `self._send(204, {})` — and is invisible to any check that looks
    for a 204 next to a write.
    """
    findings = []
    helpers = {
        n.name: n
        for n in ast.walk(tree)
        if isinstance(n, ast.FunctionDef) and _writes_body(n) and not _guards_204(n)
    }
    if not helpers:
        return findings
    for n in ast.walk(tree):
        if not isinstance(n, ast.Call):
            continue
        f = n.func
        name = f.id if isinstance(f, ast.Name) else f.attr if isinstance(f, ast.Attribute) else None
        if name in helpers and _is_204(n):
            findings.append(
                f"{rel}:{n.lineno}: {name}(204, ...) — but {name} (line {helpers[name].lineno}) "
                f"writes a response body unconditionally and does not guard 204. A 204 carries NO "
                f"body (RFC 9110 6.4.1); a conforming client does not read one, so the bytes stay in "
                f"the stream and become the NEXT response's status line on a kept-alive connection "
                f"(`{{}}HTTP/1.1 200 OK`). This is #761 exactly. Guard it:  "
                f"if code == 204: self.send_response(204); self.end_headers(); return  (R3)"
            )
    return findings


def check_no_204_body(tree: ast.AST, rel: str) -> list[str]:
    """No body may be written on a 204, where the 204 is sent inline.

    Scans FOLLOWING SIBLINGS of the statement that sends the 204, stopping at the return/raise that
    ends the guard. See the module docstring: the corpus's `_send` idiom makes both the naive
    textual checks wrong, in opposite directions. The dynamic-code case is `check_helper_204`.
    """
    findings = []
    for node in ast.walk(tree):
        for field in ("body", "orelse", "finalbody"):
            block = getattr(node, field, None)
            if not isinstance(block, list):
                continue
            for i, stmt in enumerate(block):
                if isinstance(stmt, (ast.If, ast.For, ast.While, ast.Try, ast.With, ast.FunctionDef)):
                    continue  # a compound stmt owns its own block; it is visited in its own right.
                sends = [c for c in _calls(stmt, "send_response") if _is_204(c)]
                if not sends:
                    continue
                for later in block[i + 1 :]:
                    if isinstance(later, (ast.Return, ast.Raise)):
                        break
                    if _writes_body(later):
                        findings.append(
                            f"{rel}:{later.lineno}: a response body is written after "
                            f"send_response(204) (line {sends[0].lineno}). A 204 carries NO body "
                            f"(RFC 9110 6.4.1) and a conforming client does not read one — the bytes "
                            f"stay in the stream and become the NEXT response's status line on a "
                            f"kept-alive connection (`{{}}HTTP/1.1 200 OK`). That is #761 (R3)."
                        )
                        break
    return findings


def check_drains_body(tree: ast.AST, rel: str) -> list[str]:
    findings = []
    for fn in ast.walk(tree):
        if not isinstance(fn, ast.FunctionDef) or fn.name not in BODY_BEARING:
            continue
        reads = any(
            isinstance(n, ast.Attribute) and n.attr == "rfile" for n in ast.walk(fn)
        )
        if not reads:
            findings.append(
                f"{rel}:{fn.lineno}: {fn.name} never reads self.rfile, so a request body it is sent "
                f"is left in the stream — where it becomes the next request's start line on a "
                f"kept-alive connection. Drain it:  "
                f'self.rfile.read(int(self.headers.get("Content-Length", 0)))  (R4)'
            )
    return findings


def main() -> int:
    repo = Path(__file__).resolve().parent.parent
    root = repo / FIXTURES
    fixtures = sorted(root.glob(GLOB))

    if not fixtures:
        # An empty subject is a finding, not a pass. A gate that reports OK because it found nothing
        # to check is the exact failure this gate's own epic is about (#266) — and it is reachable
        # here by a rename of the corpus directory, which is precisely when the rule matters.
        print(
            f"::error::check-parity-fixtures: found NO fixtures under {FIXTURES}/{GLOB}. "
            f"That is a broken gate, not a clean one — the corpus moved, or the glob is wrong."
        )
        return 1

    findings: list[str] = []
    for p in fixtures:
        rel = p.relative_to(repo).as_posix()
        try:
            tree = ast.parse(p.read_text(), filename=rel)
        except SyntaxError as e:
            findings.append(f"{rel}:{e.lineno}: does not parse as Python: {e.msg}")
            continue
        findings.extend(check_threading_server(tree, rel))
        findings.extend(check_protocol_version(tree, rel))
        findings.extend(check_no_204_body(tree, rel))
        findings.extend(check_helper_204(tree, rel))
        findings.extend(check_drains_body(tree, rel))

    if findings:
        for f in findings:
            print(f"::error::{f}")
        print(
            f"\ncheck-parity-fixtures: {len(findings)} finding(s) across {len(fixtures)} fixture(s) "
            f"— see above. These are the framing properties #939 established; a fixture that breaks "
            f"one reds the corpus AT RANDOM, on main as readily as on a PR (#761)."
        )
        print("::error::check-parity-fixtures FAILED")
        return 1

    print(
        f"check-parity-fixtures: OK — {len(fixtures)} fixture(s) scanned; all serve HTTP/1.1 over "
        f"ThreadingHTTPServer, none writes a body on a 204, and every body-bearing handler drains."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
