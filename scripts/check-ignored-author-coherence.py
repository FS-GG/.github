#!/usr/bin/env python3
"""Assert every push identity this repo's workflows configure is named in the preset's `gitIgnoredAuthors`.

.github#1538, epic #266 (coherence gates that fail open). Filed from #1533, which added the
`gitIgnoredAuthors` entry and observed that nothing in CI compares it to anything.

THE DEFECT THIS CLOSES
  One fact — the email the org's App-token pushes are authored with — is spelled in N+1 places:
  once in `default.json`'s `gitIgnoredAuthors`, and once per workflow that runs
  `git config user.email`. Nothing compared them. #1538 named two workflows; there are FOUR
  (`kit-materialize`, `lockfile-sync`, `skill-registry-autofix`, `feed-autofix`), which is the
  "copy-paste into a third pushing workflow" hazard already realised twice before the gate existed.

  A drift is INVISIBLE. The workflows keep pushing. Renovate keeps parking. The only symptom is a
  bump PR that never opens, and a non-proposal is not an error — it shows up nowhere. That is what
  froze FS.GG.Kit 0.3.0-0.7.0 across the org (FS.GG.Governance#315/#331/#332, FS.GG.Game#512).

WHY THE WRONG VALUE LOOKS EXACTLY LIKE THE RIGHT ONE — MEASURED, NOT INFERRED
  Renovate's `isBranchModified` (renovate 43.281.1, `dist/util/git/index.js`) builds a Set of `%ae`
  AND `%ce` over `origin/<base>..origin/<branch>`, then `Set.delete`s `gitAuthorEmail` and each
  `gitIgnoredAuthors` entry. `Set.delete` is an EXACT string match: not a login, not a regex, not
  case-insensitive.

  Driven end-to-end through that real function against a fixture branch carrying one
  App-authored commit (#1538, renovate 43.281.1, `repositoryCache: disabled`):

      gitIgnoredAuthors = ["297630107+fs-gg-cross-repo-dispatch[bot]@users.noreply.github.com"]
                                                              -> isBranchModified = false  RELEASED
      gitIgnoredAuthors = ["fs-gg-cross-repo-dispatch[bot]"]   -> true   PARKED   (the login)
      gitIgnoredAuthors = ["297630107+FS-GG-Cross-Repo-Dispatch[bot]@users..."]
                                                              -> true   PARKED   (re-cased)
      gitIgnoredAuthors = []                                  -> true   PARKED

  Three of those four wrong answers are configurations `renovate-config-validator` calls VALID.

WHY A VALIDATOR RUN IS NOT THIS GATE, AND WHY THE WORKFLOW RUNS BOTH
  `renovate-config-validator --no-global default.json` is worth having and this gate's workflow runs
  it (#1538 criterion 3) — this org-shared preset had never been schema-checked, and a config error
  here breaks dependency management in every FS-GG repo at once. But it is nowhere near sufficient.
  Measured against 43.281.1 on this very file:

      RED   an invented top-level key (`gitIgnoredAuthorz`)
      RED   `gitIgnoredAuthors` as a string instead of a list
      RED   an invented `packageRules[0]` field
      GREEN the bot LOGIN in place of the email          <- parks every branch
      GREEN `gitIgnoredAuthors` deleted entirely         <- parks every branch
      GREEN a re-cased email                             <- parks every branch
      GREEN `extends: ["github>FS-GG/this-preset-does-not-exist-9f3a"]`

  The last line is the general warning, and it is not this gate's discovery — FS.GG.Net#33's worker
  established it: the validator does NOT resolve presets, so a validator pass says nothing about
  what a repo extending this file actually receives. A green validator is evidence about SHAPE only.

WHAT THIS GATE ASSERTS
  For every workflow under `<root>/.github/workflows/`:

      every commit-identity email the workflow CONFIGURES resolves to a member of
      `<root>/default.json`'s `gitIgnoredAuthors`.

  "Configures" is the set of spellings that decide `%ae`/`%ce` — the two fields `isBranchModified`
  reads — and covers the env-var route as well as `git config`, so the rule cannot be sidestepped by
  changing how the identity is set:

      git config [--global|--local|--system|--file X] user.email <value>
      git -c user.email=<value> …
      [export] GIT_AUTHOR_EMAIL=<value>      /  GIT_AUTHOR_EMAIL: <value>      (an `env:` key)
      [export] GIT_COMMITTER_EMAIL=<value>   /  GIT_COMMITTER_EMAIL: <value>

NOTHING HERE IS A HAND-MAINTAINED LIST — that would be the same disease one level up
  Both halves are DERIVED:

    * WHICH WORKFLOWS ARE GRADED: any workflow containing one of the spellings above. Not a list of
      workflow names. #1538 was filed naming two workflows and this gate found four the day it was
      written; a fifth is graded the day it is written, with no edit here.
    * THE APP SLUG: read OUT OF the preset entry, never spelled here. The workflows write the address
      as a TEMPLATE — `297630107+${{ steps.app-token.outputs.app-slug }}[bot]@users.noreply.github.com`
      — because the slug arrives from `actions/create-github-app-token` at run time. So the gate asks
      the only question it can answer offline and the only one that matters: IS THERE A MEMBER OF
      `gitIgnoredAuthors` THIS TEMPLATE COULD PRODUCE? Every character the workflow spells literally
      is compared literally; each `${{ … }}` / `${VAR}` / `$VAR` stands for exactly one run of
      slug-legal characters.

  That comparison is not weakened by taking the slug from the preset, and this is the point worth
  being sure about: the `297630107+` prefix and the `[bot]@users.noreply.github.com` suffix are
  LITERAL in all four workflows, so they are compared literally against the preset's literal. Change
  the id in the preset and the workflows no longer match it. Change it in a workflow and that
  workflow no longer matches the preset. Only the slug — the one part no side spells — is inferred.

WHAT A PLACEHOLDER MAY STAND FOR, AND WHY THE CLASS IS NARROW
  `[A-Za-z0-9._-]+`: what a GitHub App slug may contain. It excludes `@`, `+`, `[` and `]` on
  purpose — those are the characters that carry the STRUCTURE of the address, and a placeholder
  allowed to swallow them would let `${{ … }}@users.noreply.github.com` match by accident.

  A value whose literal part contains no `@` at all — `git config user.email "$EMAIL"` — is REFUSED
  (exit 3), not graded. The gate cannot read what address that produces, and "I could not look" is
  not "I looked, and it is fine" (#266). It is also not a finding: the workflow may well be correct.
  The repair is to spell the address literally except for the slug, which is what all four do today.

SHAPES THIS GATE REFUSES RATHER THAN SKIPS (exit 3 — a skip is how a coherence gate fails open)
  * `default.json` missing, unparsable, or not a JSON object.
  * `gitIgnoredAuthors` ABSENT, not a list, empty, or holding a non-string / blank entry. An absent
    key is the exact vacuous pass #1538 criterion 2 demands red: with no entries, "every configured
    identity is a member" is vacuously true and every branch in the org is parked.
  * NO IDENTITY ASSIGNMENT ANYWHERE in the workflow tree. A gate about push identities that found
    none has been pointed at the wrong tree; reporting green would hide that.
  * A value the gate cannot read (see above).
  * A `run:` line that names `user.email` or a `GIT_*_EMAIL` and is not any spelling above — an
    `eval`, a `$( … )` substitution, an `echo` that interpolates the key. It MIGHT be a write, and a
    coherence gate that skips what it cannot read is the fail-open this gate exists to end. The one
    exemption is the DECIDABLE read: `git config [flags] user.email` with no value after it prints
    the value and cannot set one. That exemption is drawn where the tokens settle it, not where the
    line looks harmless.
  * `git commit --author=…`. It sets `%ae` and therefore belongs to the subject, but its argument is
    a `Name <email>` display form with its own quoting, and a half-read identity is worse than an
    unread one. Not live in this repo; refused anyway, so the gate cannot quietly start being
    unsound the day one appears.

  Refusals are COLLECTED, not raised on sight: one unreadable line must not suppress every real
  finding in the rest of the tree. Findings print first, then the refusals, then exit 3.

A MENTION IS NOT A USE (#683), AND THE PARSER IS WHAT DECIDES
  These spellings live in `run:` shell, so this gate does read shell — there is nowhere else an
  identity assignment can be. #683's lesson is that a regex over a whole file cannot tell a MENTION
  from a USE, and that matters here rather than hypothetically: all four subject workflows carry
  long comment blocks about this exact address, and `default.json` quotes the `git config
  user.email` spelling in its own description. Three layers keep the gate off them:

    * IT NEVER SCANS THE FILE AS TEXT. `yaml.compose` supplies the node tree, and the gate reads
      only `run:` scalars and `env:` mappings. A YAML comment is not in the tree at all, and neither
      is a `name:` — so `- name: Configure git user.email` can never be mistaken for one, which a
      whole-file regex could not manage without a fourth special case.
    * INSIDE A `run:` BLOCK, a line whose first non-blank character is `#` is a shell comment.
    * THE VALUE IS TOKENISED BY `shlex`, not cut out by a regex. That is what makes a trailing
      comment (`git config user.email x  # why`) read as the address `x` rather than as `x  # why`
      — a regex would have reported a confident FINDING about a correct line. It also keeps the
      `${{ … }}` expression whole, which whitespace-splitting cannot: the expression contains
      spaces and only its quotes hold it together.

  A line that survives the screen and that `shlex` cannot tokenise, or that tokenises into no
  spelling this gate knows, is REFUSED — never skipped.

WHAT THIS GATE DELIBERATELY DOES NOT DO — say it out loud, because a green here is narrow
  * IT DOES NOT GRADE UNUSED PRESET ENTRIES. An entry that no workflow in THIS repo pushes under is
    reported as UNUSED and is never a finding. `default.json` is an ORG-SHARED preset: an entry may
    legitimately name an identity that pushes from a workflow in a repository this gate cannot read.
    Redding it would be this repo asserting something about trees it has not looked at.
  * IT DOES NOT KNOW WHICH BRANCH A WORKFLOW PUSHES TO. #1538 scopes the rule to workflows that push
    to `renovate/*` "or other bot" branches; deciding that statically means reading `run:` text for
    intent, which #683 rules out. So the rule is the WIDER one — every configured identity must be
    declared — which is sound because a `gitIgnoredAuthors` entry only ever affects branches Renovate
    itself owns, so declaring an identity that pushes elsewhere costs nothing. All four of this
    repo's pushing workflows push to bot branches (`renovate/*`, `build-config/*`,
    `bot/feed-autofix`, `bot/skill-registry-autofix`) and share one identity, so the wider rule and
    the narrower one have the same subject today.
  * IT SAYS NOTHING ABOUT THE OTHER FS-GG REPOSITORIES. It reads the workflow tree of whichever
    `--root` names. A receiver that hand-rolled its own pushing workflow would not be seen. That is
    a real limit: the summary names the tree it audited rather than claiming the org is clean.
  * IT READS `.github/workflows/*.{yml,yaml}` AND NOTHING ELSE. A composite action under
    `.github/actions/**` could configure an identity and would not be graded. There is one such
    action today (`setup-policy-python`) and it configures none; the reach is stated rather than
    quietly assumed.
  * IT KNOWS FOUR SPELLINGS, not every conceivable one. A third-party action that sets the identity
    through its own `with:` inputs is invisible to it. The four it knows are the ones this org
    uses and the ones a copy-paste would reach for; a fifth would be a real gap, and is a gap this
    docstring names rather than one a green quietly covers.
  * IT IS NOT A RENOVATE SEMANTICS CHECK. That `gitIgnoredAuthors` matching is exact is established
    by driving the real function (see above) and is asserted in the fixture as a recorded fact, not
    re-derived at gate time.

EXIT CODES — THE CONTRACT (scripts/lib/gate.py)
  0  OK — every configured identity resolves to a `gitIgnoredAuthors` member.
  1  FINDING — at least one configured identity does not. The message names the file, the line and
     the value.
  3  NO VERDICT (permanent) — a refused shape above.

  There is deliberately no exit 2: this gate is pure and offline. It reads two files and makes no
  network call, so it has no condition a re-run could resolve.

usage: check-ignored-author-coherence.py [--root DIR]
"""

from __future__ import annotations

import json
import os
import re
import shlex
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib.gate import (  # noqa: E402  (path shim above must run first)
    ExitCode,
    GateError,
    base_parser,
    read_text,
    report_findings,
    report_ok,
    run,
    workflow_files,
)

NAME = "check-ignored-author-coherence"

# The tree this gate walks, and the file it compares it against.
WORKFLOW_ROOT = ".github/workflows"

# Renovate's OWN convention, not a choice this gate makes: `github>FS-GG/.github` with no path
# suffix resolves to `default.json` at the repository root. Spelling any other name here would
# grade a file no consumer reads.
PRESET = "default.json"

# The key. Renovate's spelling, from its config schema.
KEY = "gitIgnoredAuthors"

# WHAT THIS GATE READS, FOR THE WORKFLOW THAT RUNS IT (#996, epic #266). `check-paths-coherence.py`
# reads this BY AST and reds `ignored-author-coherence.yml` if its `paths:` does not select every
# entry. It is COMPOSED from the constants the walk uses, never retyped beside them — a retyped copy
# is free to drift the day one changes, which is the disease this gate is an instance of closing.
PATHS_SUBJECT = (WORKFLOW_ROOT, PRESET)

# A `${{ … }}` expression, a `${VAR}`, or a bare `$VAR`. What a workflow writes where the app slug
# will be at run time.
PLACEHOLDER = re.compile(r"\$\{\{.*?\}\}|\$\{[A-Za-z_][A-Za-z0-9_]*\}|\$[A-Za-z_][A-Za-z0-9_]*")

# What ONE placeholder may stand for: a GitHub App slug. Two exclusions, each load-bearing.
#
#   `@ + [ ]` — the characters that carry the address's STRUCTURE. A placeholder allowed to swallow
#   them would let `${{ … }}@users.noreply.github.com` match a real entry by accident, and the gate
#   would pass anything ending in the right domain.
#
#   UPPERCASE — a GitHub App slug is lowercase by construction; GitHub derives it from the App name
#   and lower-cases it. Measured for the App this org actually pushes under, not assumed:
#   `gh api orgs/FS-GG/installations` reports `app_slug: fs-gg-cross-repo-dispatch` (app_id 4166418
#   — note that is NOT the 297630107 in the address, which is the `[bot]` USER's id: `gh api
#   users/fs-gg-cross-repo-dispatch%5Bbot%5D` -> id 297630107, the #514 trap). Admitting uppercase
#   here would make the gate GREEN on a re-cased preset entry, and a re-cased entry is MEASURED to
#   park every branch — `Set.delete` is case-sensitive (renovate 43.281.1, driven; see the fixture).
SLUG = r"[a-z0-9._-]+"

# The git config key, and the two environment variables. Renovate reads BOTH `%ae` and `%ce`, so
# both variables are subjects — a gate that knew only `git config` could be sidestepped by one line.
CONFIG_KEY = "user.email"
ENV_KEYS = ("GIT_AUTHOR_EMAIL", "GIT_COMMITTER_EMAIL")

# THE CHEAP SCREEN, and it is a screen rather than the parse. A line matching this is TOKENISED and
# then graded or refused; a line that does not is not shell this gate has anything to say about.
# Widening it is safe (a false hit becomes a refusal, never a green); narrowing it is not.
TOUCHES_IDENTITY = re.compile("|".join([re.escape(CONFIG_KEY), *ENV_KEYS]))

# `git commit --author=…`. A real way to set `%ae`, and one this gate refuses to grade rather than
# half-read. Recognised from the TOKENS, so `git log --author=` — a FILTER, not an assignment — is
# not mistaken for one, and neither is the word `--author` inside a message string.
COMMIT_AUTHOR_FLAG = "--author"


def preset_authors(root: str) -> list[str]:
    """`gitIgnoredAuthors` from ``<root>/default.json``, or a no-verdict naming what is wrong.

    EVERY DEGENERATE SHAPE IS A NO-VERDICT, NOT A GREEN, and that is #1538 criterion 2 rather than
    defensiveness: with no entries, "every configured identity is a member of the list" is vacuously
    true — the gate would pass its loudest, most complete-looking pass in exactly the state where
    every Renovate branch in the org is parked. An empty list and an absent key are the SAME state
    to Renovate (`config.ignoredAuthors = gitIgnoredAuthors ?? []`), so they are the same state here.
    """
    path = os.path.join(root, PRESET)
    text = read_text(path, f"the org-shared Renovate preset ({PRESET})")
    try:
        document = json.loads(text)
    except json.JSONDecodeError as error:
        raise GateError(
            f"{PRESET}: not parsable as JSON — {error}. This is the preset every FS-GG repo extends; "
            f"an unreadable one is a no-verdict, never a pass."
        ) from error
    if not isinstance(document, dict):
        raise GateError(f"{PRESET}: top level is {type(document).__name__}, not a JSON object.")

    if KEY not in document:
        raise GateError(
            f"{PRESET}: has no `{KEY}` key. Renovate reads a missing key as the EMPTY LIST, so every "
            f"commit this org's workflows push onto a `renovate/*` branch is read as a user edit and "
            f"every branch in every receiver parks — silently, because a bump PR that never opens is "
            f"not an error (#1533). Refusing to report green over that (#266)."
        )
    authors = document[KEY]
    if not isinstance(authors, list) or not authors:
        raise GateError(
            f"{PRESET}: `{KEY}` is {authors!r}. An empty or non-list value is the same state as an "
            f"absent key — every push identity would be vacuously 'declared' and every branch parks."
        )
    for entry in authors:
        if not isinstance(entry, str) or not entry.strip():
            raise GateError(f"{PRESET}: `{KEY}` contains {entry!r}, which is not a non-empty string.")
    return [str(entry) for entry in authors]


def template_regex(value: str) -> re.Pattern[str]:
    """``value`` as a pattern: literals compared literally, each placeholder standing for one slug.

    This is the whole of how a TEMPLATE is compared to a LITERAL. Every character the workflow
    spells is escaped and must appear verbatim; only the `${{ … }}` runs are free, and only over
    slug-legal characters. So the `297630107+` prefix and the `[bot]@users.noreply.github.com`
    suffix are genuinely compared — the inference is confined to the one part no side spells.
    """
    pattern, position = [], 0
    for match in PLACEHOLDER.finditer(value):
        pattern.append(re.escape(value[position : match.start()]))
        pattern.append(SLUG)
        position = match.end()
    pattern.append(re.escape(value[position:]))
    return re.compile("".join(pattern))


def literal_parts(value: str) -> str:
    """Everything in ``value`` OUTSIDE a placeholder — what the workflow actually spells."""
    return PLACEHOLDER.sub("", value)


class Assignment:
    """One configured commit identity: where it was written, how, and what it says."""

    def __init__(self, where: str, line: int, spelling: str, value: str) -> None:
        self.where, self.line, self.spelling, self.value = where, line, spelling, value

    def __repr__(self) -> str:  # pragma: no cover - diagnostics only
        return f"Assignment({self.where}:{self.line} {self.spelling} {self.value!r})"


def shell_blocks(where: str, text: str) -> tuple[list[tuple[int, str]], list[tuple[int, str, str]]]:
    """``(run: blocks as (first content line, text), env: entries as (line, name, value))``.

    STRUCTURAL, NOT A TEXT SCAN, and that is the whole of #683 here. `yaml.compose` gives the node
    tree; a YAML COMMENT is not in it, and neither is a `name:` — so this gate physically cannot
    read `- name: Configure git user.email` as an assignment, and physically cannot read the long
    explanatory comment blocks all four subject workflows carry about this exact address. A
    whole-file regex would need a special case for each, and would still be wrong about the next
    shape nobody thought of.

    A block scalar's own `|` sits on ``start_mark.line``, so its first CONTENT line is the next one;
    a plain or quoted scalar's content begins on ``start_mark.line`` itself. Getting that offset
    right is what makes a finding say the line a reader can go and edit.
    """
    import yaml  # lazy, exactly as lib.gate does: only the YAML half pays for it.

    try:
        root = yaml.compose(text)
    except yaml.YAMLError as error:
        raise GateError(f"{where}: not parsable as YAML — {error}") from error
    if root is None:
        raise GateError(f"{where}: empty document — a workflow file with no content is not readable")

    runs: list[tuple[int, str]] = []
    envs: list[tuple[int, str, str]] = []

    def walk(node: object) -> None:
        if isinstance(node, yaml.SequenceNode):
            for child in node.value:
                walk(child)
            return
        if not isinstance(node, yaml.MappingNode):
            return
        for key, value in node.value:
            name = key.value if isinstance(key, yaml.ScalarNode) else None
            if name == "run" and isinstance(value, yaml.ScalarNode):
                first = value.start_mark.line + (2 if value.style in ("|", ">") else 1)
                runs.append((first, value.value))
            elif name == "env" and isinstance(value, yaml.MappingNode):
                for env_key, env_value in value.value:
                    if not isinstance(env_key, yaml.ScalarNode) or not isinstance(env_value, yaml.ScalarNode):
                        continue
                    if env_key.value in ENV_KEYS:
                        envs.append((env_key.start_mark.line + 1, str(env_key.value), str(env_value.value)))
            walk(value)

    walk(root)
    return runs, envs


def read_assignment(tokens: list[str]) -> tuple[str, str] | None:
    """``(spelling, value)`` if these shell tokens configure a commit identity, else None.

    Tokens, never a regex over the line. `shlex` has already stripped the quotes, dropped a trailing
    `# comment`, and — crucially — kept `${{ steps.app-token.outputs.app-slug }}` in ONE piece,
    which whitespace-splitting cannot do because the expression contains spaces and only its quotes
    hold it together.
    """
    # `git -c user.email=<value> …` — the per-invocation form. Anywhere in the argument list, and
    # it needs no `config` subcommand, so it is tested first and independently.
    for index, token in enumerate(tokens[:-1] if tokens else []):
        if token == "-c" and tokens[index + 1].startswith(CONFIG_KEY + "="):
            return "git -c user.email=", tokens[index + 1][len(CONFIG_KEY) + 1 :]

    # `[export] GIT_AUTHOR_EMAIL=<value>`
    for token in tokens:
        for env_key in ENV_KEYS:
            if token.startswith(env_key + "="):
                return env_key, token[len(env_key) + 1 :]

    if not tokens or tokens[0] != "git":
        return None

    # `git [-C dir] config [--flags] user.email <value>`
    if "config" in tokens and CONFIG_KEY in tokens:
        index = tokens.index(CONFIG_KEY)
        if index > tokens.index("config") and index + 1 < len(tokens):
            return "git config user.email", tokens[index + 1]
    return None


def is_read(tokens: list[str]) -> bool:
    """Is this the unambiguous READ form — `git config [flags] user.email` with no value?

    `git config user.email` with nothing after it PRINTS the value; it cannot set one. It is the
    likeliest benign shape a future workflow will write, and refusing it would be the gate demanding
    a change to a line that does not configure anything. It is exempted because it is DECIDABLE from
    the tokens, not because it is harmless-looking — an `echo` or a `$( … )` mentioning the key is
    neither decidable nor exempt, and is refused. See SHAPES THIS GATE REFUSES.
    """
    return (
        tokens[:1] == ["git"]
        and "config" in tokens
        and tokens[-1] == CONFIG_KEY
        and tokens.index("config") < len(tokens) - 1
    )


def assignments(where: str, text: str) -> tuple[list[Assignment], list[str]]:
    """Every identity assignment in one workflow, plus the lines this gate refuses to read."""
    found: list[Assignment] = []
    refusals: list[str] = []

    runs, envs = shell_blocks(where, text)

    for line, name, value in envs:
        # An `env:` key IS the assignment — there is no shell to tokenise and nothing to misread.
        found.append(Assignment(where, line, f"env: {name}", value))

    for first, block in runs:
        for offset, raw in enumerate(block.splitlines()):
            line = raw.strip()
            # A SHELL comment. Inside a `run:` block a `#` really is one, and it cannot configure
            # anything — all four subject workflows explain this very address in such comments.
            if not line or line.startswith("#"):
                continue
            identity = bool(TOUCHES_IDENTITY.search(line))
            if not identity and COMMIT_AUTHOR_FLAG not in line:
                continue

            number = first + offset
            at = f"{where}:{number}"

            try:
                tokens = shlex.split(line, comments=True)
            except ValueError as error:
                refusals.append(
                    f"{at}: names a commit-identity setting on a line this gate cannot tokenise "
                    f"({error}):\n    {line}\n  A coherence gate that skips what it cannot read "
                    f"fails open (#266), so this is a NO VERDICT rather than a pass."
                )
                continue

            authored = any(
                token == COMMIT_AUTHOR_FLAG or token.startswith(COMMIT_AUTHOR_FLAG + "=")
                for token in tokens
            )
            if tokens[:1] == ["git"] and "commit" in tokens and authored:
                refusals.append(
                    f"{at}: `git commit --author` sets the commit's `%ae`, one of the two fields "
                    f"Renovate's isBranchModified reads — so it belongs to this gate's subject. Its "
                    f"argument is a `Name <email>` display form with its own quoting, and a "
                    f"half-read identity is worse than an unread one, so it is REFUSED rather than "
                    f"graded (#266). Set the identity with `git config user.email` instead."
                )
                continue

            read = read_assignment(tokens)
            if read is not None:
                found.append(Assignment(where, number, read[0], read[1]))
                continue

            if is_read(tokens):
                continue

            # Only `--author` matched, on something that is not a `git commit` — `git log
            # --author=<x>` is a FILTER, not an assignment, and redding it would be the gate
            # demanding a change to a question it has no part in.
            if not identity:
                continue

            refusals.append(
                f"{at}: names a commit-identity setting this gate cannot read as an assignment:\n"
                f"    {line}\n"
                f"  It is shell, not a comment, so it may well BE one — and a coherence gate that "
                f"skips what it cannot read fails open (#266). If it DOES set the identity, write "
                f"it as `git config user.email <address>` on its own line. If it only mentions the "
                f"key — an `echo`, a `$( … )` substitution — move the mention onto a comment line: "
                f"a gate cannot tell those apart from the outside, and guessing is how this class "
                f"of gate goes quietly green over a real one."
            )

    return found, refusals


def grade(item: Assignment, authors: list[str]) -> tuple[str | None, str | None, str | None]:
    """``(matched author, finding, refusal)`` for one assignment — exactly one is not None."""
    if "@" not in literal_parts(item.value):
        return (
            None,
            None,
            f"{item.where}:{item.line}: `{item.spelling}` is set to {item.value!r}, whose literal "
            f"part contains no `@` — the address is hidden behind an expression this gate cannot "
            f"resolve offline, so it CANNOT SAY whether it matches `{KEY}`. Not a finding, a "
            f"no-verdict (#266). Spell the address literally except for the app slug, as "
            f"kit-materialize.yml does.",
        )

    pattern = template_regex(item.value)
    for author in authors:
        if pattern.fullmatch(author):
            return author, None, None

    return (
        None,
        f"{item.where}:{item.line}: `{item.spelling}` is set to {item.value!r}, and NO member of "
        f"`{KEY}` in {PRESET} can be that address. Members: {authors!r}. Renovate matches this by "
        f"`Set.delete` on the commit's exact `%ae`/`%ce` — not a login, not a regex, not "
        f"case-insensitively (driven through renovate 43.281.1's real isBranchModified, #1538) — so "
        f"a near-miss is a total miss. Every commit this workflow pushes onto a `renovate/*` branch "
        f"is then read as a user edit: Renovate parks the branch under PR Edited (Blocked), renames "
        f"the PR `… - abandoned`, and never updates it again. Nothing errors; the bump PR simply "
        f"never opens (#1533). Fix BOTH this line and {PRESET} in the SAME PR.",
        None,
    )


def main(argv: list[str]) -> int:
    parser = base_parser(__doc__.splitlines()[0])
    args = parser.parse_args(argv)
    root = args.root

    authors = preset_authors(root)

    findings: list[str] = []
    refusals: list[str] = []
    matched: dict[str, list[str]] = {author: [] for author in authors}
    graded = 0

    for path in workflow_files(root):
        where = os.path.relpath(path, root).replace(os.sep, "/")
        found, refused = assignments(where, read_text(path, where))
        refusals.extend(refused)
        for item in found:
            author, finding, refusal = grade(item, authors)
            if refusal is not None:
                refusals.append(refusal)
                continue
            if finding is not None:
                findings.append(finding)
                continue
            graded += 1
            matched[str(author)].append(f"{item.where}:{item.line}")

    # NO SUBJECT IS A BROKEN GATE, NOT A CLEAN TREE. A gate about push identities that found not one
    # has been pointed at a tree it cannot read — or at the wrong tree entirely. Green here would be
    # the loudest possible pass over the least possible work (#266). Note this fires on the total,
    # findings and refusals included: a tree whose every assignment was REFUSED is also a tree this
    # gate did not grade.
    if graded == 0 and not findings and not refusals:
        raise GateError(
            f"no commit-identity assignment found in any workflow under {WORKFLOW_ROOT}. This gate "
            f"grades `git config user.email`, `git -c user.email=`, GIT_AUTHOR_EMAIL and "
            f"GIT_COMMITTER_EMAIL; finding none means it is looking at the wrong tree, not that the "
            f"tree is clean."
        )

    if findings:
        for line in refusals:
            print(f"::error::{line}", file=sys.stderr)
        return report_findings(NAME, findings)

    if refusals:
        for line in refusals:
            print(f"::error::{line}", file=sys.stderr)
        print(
            f"::error::{NAME}: NO VERDICT — {len(refusals)} line(s) could not be read. That is not "
            f"a clean tree (#266).",
            file=sys.stderr,
        )
        return int(ExitCode.NO_VERDICT_PERMANENT)

    # UNUSED entries are REPORTED, never graded. `default.json` is org-shared: an entry may name an
    # identity that pushes from a workflow in a repository this gate cannot read, and redding it
    # would be this repo asserting something about a tree it has not looked at.
    unused = sorted(author for author, uses in matched.items() if not uses)
    summary = (
        f"{graded} configured push identit{'y' if graded == 1 else 'ies'} in "
        f"{os.path.abspath(root)}/{WORKFLOW_ROOT} — every one of them named in {PRESET}'s `{KEY}`"
    )
    if unused:
        summary += f"; {len(unused)} declared entr{'y' if len(unused) == 1 else 'ies'} unused here: {unused!r}"
    return report_ok(NAME, summary)


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name=NAME))
