#!/usr/bin/env python3
"""Parse the Project collaborator GraphQL request and materialize its typed variables.

This is deliberately a request-contract test seam, not a GitHub response stub. It rejects malformed
GraphQL before the fixture chooses a response and reconstructs the nested `gh api -F` array into
objects, so query substrings cannot make a syntactically invalid or string-valued request pass.
"""

from __future__ import annotations

import re
import sys


TOKEN = re.compile(r"\s+|,|\.\.\.|\$|[!$():@\[\]{}]|[_A-Za-z][_0-9A-Za-z]*|-?[0-9]+|\"(?:\\.|[^\"])*\"")


def lex(source: str) -> list[str]:
    out: list[str] = []
    position = 0
    while position < len(source):
        match = TOKEN.match(source, position)
        if not match:
            raise ValueError(f"invalid GraphQL token at byte {position}")
        token = match.group(0)
        position = match.end()
        if not token.isspace() and token != ",":
            out.append(token)
    return out


class Parser:
    def __init__(self, tokens: list[str]):
        self.tokens = tokens
        self.at = 0

    def take(self, expected: str | None = None) -> str:
        if self.at >= len(self.tokens):
            raise ValueError("unexpected end of GraphQL document")
        value = self.tokens[self.at]
        if expected is not None and value != expected:
            raise ValueError(f"expected {expected!r}, got {value!r}")
        self.at += 1
        return value

    def variable_definitions(self) -> dict[str, str]:
        definitions: dict[str, str] = {}
        self.take("(")
        while self.tokens[self.at] != ")":
            self.take("$")
            name = self.take()
            self.take(":")
            pieces: list[str] = []
            depth = 0
            while True:
                token = self.tokens[self.at]
                if token == "[":
                    depth += 1
                elif token == "]":
                    depth -= 1
                elif depth == 0 and token in {"$", ")"}:
                    break
                pieces.append(self.take())
            definitions[name] = "".join(pieces)
        self.take(")")
        return definitions

    def value(self):
        token = self.take()
        if token == "$":
            return ("variable", self.take())
        if token == "{":
            result = {}
            while self.tokens[self.at] != "}":
                name = self.take()
                self.take(":")
                result[name] = self.value()
            self.take("}")
            return result
        if token == "[":
            values = []
            while self.tokens[self.at] != "]":
                values.append(self.value())
            self.take("]")
            return values
        return token

    def selection_set(self) -> list[dict]:
        selections: list[dict] = []
        self.take("{")
        while self.tokens[self.at] != "}":
            if self.tokens[self.at] == "...":
                self.take("...")
                self.take("on")
                selections.append({"fragment": self.take(), "selection": self.selection_set()})
                continue
            name = self.take()
            arguments = {}
            if self.tokens[self.at] == "(":
                self.take("(")
                while self.tokens[self.at] != ")":
                    argument = self.take()
                    self.take(":")
                    arguments[argument] = self.value()
                self.take(")")
            nested = self.selection_set() if self.at < len(self.tokens) and self.tokens[self.at] == "{" else []
            selections.append({"field": name, "arguments": arguments, "selection": nested})
        self.take("}")
        return selections

    def document(self):
        self.take("mutation")
        definitions = self.variable_definitions()
        selections = self.selection_set()
        if self.at != len(self.tokens):
            raise ValueError("trailing GraphQL tokens")
        return definitions, selections


def field(selections: list[dict], name: str) -> dict:
    matches = [item for item in selections if item.get("field") == name]
    if len(matches) != 1:
        raise ValueError(f"expected exactly one {name} field")
    return matches[0]


def validate_query(query: str) -> None:
    definitions, root = Parser(lex(query)).document()
    if definitions != {"id": "ID!", "collaborators": "[ProjectV2Collaborator!]!"}:
        raise ValueError(f"wrong variable definitions: {definitions}")
    mutation = field(root, "updateProjectV2Collaborators")
    if mutation["arguments"] != {
        "input": {
            "projectId": ("variable", "id"),
            "collaborators": ("variable", "collaborators"),
        }
    }:
        raise ValueError("mutation input is not bound to the typed variables")
    collaborators = field(mutation["selection"], "collaborators")
    field(collaborators["selection"], "totalCount")
    nodes = field(collaborators["selection"], "nodes")
    fragments = {item.get("fragment"): item["selection"] for item in nodes["selection"] if "fragment" in item}
    if set(fragments) != {"User", "Team"}:
        raise ValueError("mutation payload must select User and Team actor identities")
    if {item.get("field") for item in fragments["User"]} != {"id", "login"}:
        raise ValueError("User payload identity is incomplete")
    if {item.get("field") for item in fragments["Team"]} != {"id", "slug"}:
        raise ValueError("Team payload identity is incomplete")


def validate_variables(arguments: list[str]) -> None:
    project_ids = [arg.split("=", 1)[1] for arg in arguments if arg.startswith("id=")]
    if len(project_ids) != 1 or not project_ids[0]:
        raise ValueError("exactly one nonempty Project id variable is required")
    rows: list[dict[str, str]] = []
    for arg in arguments:
        match = re.fullmatch(r"collaborators\[\]\[(userId|teamId|role)\]=(.*)", arg)
        if not match:
            continue
        key, value = match.groups()
        if key in {"userId", "teamId"}:
            rows.append({key: value})
        elif not rows or "role" in rows[-1]:
            raise ValueError("collaborator role is not paired with one actor object")
        else:
            rows[-1]["role"] = value
    if not rows or any(set(row) not in ({"userId", "role"}, {"teamId", "role"}) for row in rows):
        raise ValueError(f"collaborator variables are not object rows: {rows}")
    if any(row["role"] != "WRITER" for row in rows):
        raise ValueError("every requested actor must carry WRITER role")


def main(arguments: list[str]) -> int:
    queries = [arg.removeprefix("query=") for arg in arguments if arg.startswith("query=")]
    if len(queries) != 1:
        raise ValueError("exactly one GraphQL query field is required")
    validate_query(queries[0])
    validate_variables(arguments)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except ValueError as error:
        print(f"project GraphQL contract rejected: {error}", file=sys.stderr)
        raise SystemExit(1)
