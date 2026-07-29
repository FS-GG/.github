"""The ONE reading of an ``actions/checkout`` step and its sparse-checkout block (.github#1530, #1553).

WHY THIS EXISTS. Two files read this block and answer questions about it, and until this module
neither could see the other:

  * ``scripts/check-sparse-checkout-closure.py`` (#1522) grades each pattern as anchored / literal /
    directory / selecting-something.
  * ``tests/lock-range-coherence/sparse_set.py`` (#1518) resolves the patterns through real
    ``git sparse-checkout`` to materialise exactly what the runner would get.

The overlap was not incidental. Both had to get the SAME THREE non-obvious readings right, and each
got them right independently — which is #520 / #587 / #599 / #710 / #724's shape, and the shape
#1522 itself exists to close one level down: a hand-maintained duplicate with nothing comparing the
two copies. ``check-paths-coherence.py`` states the principle this module serves: *"not 'somebody
wrote it down accurately', but 'the thing that does the work is the thing being read'."*

AND THE TWO COPIES HAD ALREADY DRIFTED. Measured 2026-07-27, before the hoist, on the two files as
they stood:

  * ``sparse-checkout:`` — a bare key with NO VALUE. The gate refused it. ``sparse_set`` returned
    ``([], False)``, which its own ``select()`` reads as A FULL CLONE, so a workflow that fetches an
    empty tree materialised the entire repository and every assertion downstream passed over it.
    That is the exact fail-open ``sparse_set``'s docstring claims it does not have ("A
    declared-but-unreadable one is an error, never that"): PyYAML resolves both an ABSENT key and a
    VALUELESS one to ``None``, and ``params.get(...)`` cannot tell them apart. Only ``in`` can.
  * ``sparse-checkout-cone-mode: "false"`` — the QUOTED spelling. The gate accepted it; ``sparse_set``
    refused it outright. Every ``with:`` value reaches an action as a string and the action reads
    this one with ``core.getBooleanInput``, so the quoted form is a workflow that WORKS.

Neither drift was reachable from either file's own fixture. That is the argument for one copy.

THE THREE READINGS, EACH A PLACE WHERE THE PLAUSIBLE READING IS THE WRONG ONE
  1. ``actions/checkout`` SPLITS THE INPUT ON NEWLINES AND DROPS BLANKS, so a literal block scalar
     and a plain string reach git identically — and a FOLDED scalar (``>``) joins its lines with a
     SPACE, yielding one pattern that matches nothing and fetches an empty tree. Mirroring the
     action's own splitting, rather than special-casing the YAML shape, is what keeps that honest;
     ``sparse_set``'s first draft scanned by indentation and failed OPEN on exactly this.
  2. AN OMITTED ``sparse-checkout-cone-mode`` IS ``true`` — the action's documented default, not
     "unknown". It decides whether the patterns are gitignore expressions or rooted directory
     prefixes, i.e. what they MEAN, so it is READ rather than assumed and an unreadable one is a
     no-verdict.
  3. A DECLARED-BUT-EMPTY ``sparse-checkout`` IS AN EMPTY TREE, NOT A FULL CLONE. ``""``, ``"   "``,
     ``[]`` and a bare key are refused ALIKE, because the runner cannot tell them apart either;
     giving them different verdicts would be inventing a distinction the subject does not make.

AND THEN THE SELECTOR BESIDE IT WAS STILL TWO COPIES (.github#1553, #1530's follow-up). #1530 left
"find the step whose ``with:`` block you are about to read" in each caller, on the argument that the
two ask different questions of the document. They do — the gate wants EVERY cross-repo checkout with
its job id, ``sparse_set`` wants the single FS-GG/.github one and hard-fails on any other count — but
that is the FILTER, not the qualification, and underneath it both had to answer one identical
question: *is this step an ``actions/checkout`` aimed at repository R?* They answered it differently,
in opposite directions, and neither divergence was reachable from either fixture:

  * ``sparse_set`` NEVER READ ``uses:``. Any step whose ``with:`` carried ``repository:
    FS-GG/.github`` qualified — a ``docker/build-push-action``, a composite action, anything — so the
    workflow's real checkout could be crowded out by a step that fetches nothing.

    Those are the TWO divergences, and there is a third difference that is NOT one: ``sparse_set``
    spelled the field read ``str(params.get("repository", "")).strip()`` where the gate spells it
    ``str(params.get(...) or "")``. On a bare ``repository:`` the first yields the string ``"None"``
    — which is why the gate's spelling is the one kept here — but ``sparse_set`` then compared
    ``== "FS-GG/.github"``, which ``"None"`` fails, so it changed no verdict. Recorded because an
    overstated divergence is its own kind of wrong comment.
  * ``sparse_set`` COMPARED ``repository:`` CASE-SENSITIVELY, where GitHub resolves it without regard
    to case and the gate's own fixture asserts as much (``tests/sparse-checkout-closure/run.sh``'s
    ``repo-casing`` leg writes ``repository: fs-gg/.GitHub`` and requires rule (4) to still run).
    ``sparse_set`` saw ZERO authority steps in that same workflow and hard-failed on the count.

So the QUALIFICATION is here, once, in :func:`checkout_steps` and :func:`repository_matches`. What
each caller then WANTS out of that list stays the caller's: the gate takes all of them, ``sparse_set``
filters to the authority repo and enforces its own count. Those are genuinely different questions and
are deliberately NOT folded into one shape.

The failure direction of ``sparse_set``'s half was fail-CLOSED — a hard error on the wrong count,
never a wrong verdict — which is why #1553 was a normal finding rather than an incident. It is fixed
anyway, because "the second copy disagreed" is this repo's most-filed bug class (#520, #587/#599,
#710, #724) and a shared reading is only shared if nothing beside it re-answers the same question.

WHAT IS NOT HERE, AND WHY
  * WHICH of the qualifying steps a caller wants, and what it does about the count. See above: the
    gate grades every cross-repo checkout; ``sparse_set`` refuses any count but one. Forcing those
    into a single shape would be inventing a rule neither caller has.
  * GRADING A PATTERN. Anchored / literal / directory / selects-something is #1522's rule, not this
    module's reading. It lives in the gate.
  * RESOLVING PATTERNS THROUGH GIT. ``sparse_set`` alone does that, on purpose (#1522 does not need a
    subprocess to know a literal anchored directory when it sees one).

REFUSAL SEMANTICS. Everything unreadable raises :class:`SparseRefusal` and NOTHING here returns a
default in its place, because "I could not tell" is not "it is fine" (epic #266). The exception is
deliberately plain — this module has no dependencies, not even on ``lib.gate`` — so a caller maps it
to whatever no-verdict its own contract spells: the gate collects it as a refusal (exit 3),
``sparse_set`` re-raises it as its own ``SparseError`` (exit 2).
"""

from __future__ import annotations

from typing import NamedTuple

__all__ = [
    "SparseRefusal",
    "CHECKOUT_ACTION",
    "CheckoutStep",
    "checkout_steps",
    "repository_matches",
    "patterns_of",
    "cone_mode_of",
]

# THERE IS NO `declaration_of(params)` CONVENIENCE, AND THAT IS A DECISION. A first draft of this
# module had one, reading the patterns and then the cone flag and handing back both. It is exactly the
# wrong thing for this module to own: the gate deliberately does NOT read the cone flag for a step
# with no `sparse-checkout:` at all — a full clone is not a subject, and an unevaluated `${{ }}` cone
# flag beside one must not become a refusal — while `sparse_set.py` reads it unconditionally. Shipping
# a third composition, used by neither, would have put a SECOND reading back inside the module whose
# whole purpose is that there is only one. Callers compose the two readings and own the order.

CONE_KEY = "sparse-checkout-cone-mode"
PATTERNS_KEY = "sparse-checkout"

# The action whose `with:` block this module reads. Steps name it as `actions/checkout@v7`,
# `actions/checkout@<sha>`, or unversioned; the qualification is on the owner/name, casefolded, with
# the ref ignored. This is the ONLY action literal in the qualification, and it is the action's own
# published name rather than a policy choice.
CHECKOUT_ACTION = "actions/checkout"

# `core.getBooleanInput`'s vocabulary.  It is a CASE-SENSITIVE six-spelling set: accepting `1`/`0`,
# or case-folding a spelling beyond these values, would make this reader report a mode that the real
# checkout action rejects before it can materialise anything.  Keep the directions as data so they
# cannot drift apart.
TRUE_SPELLINGS = {"true", "True", "TRUE"}
FALSE_SPELLINGS = {"false", "False", "FALSE"}


class SparseRefusal(Exception):
    """A sparse-checkout block this module will not guess at.

    NEVER raised for a block that is merely absent, and never carrying a usable value alongside: a
    caller that catches this has NO reading, which is the point. Map it to a no-verdict exit code —
    never to a finding, and never to green.
    """


class CheckoutStep(NamedTuple):
    """One qualifying `actions/checkout` step: where it is, what it fetches, and its `with:` block.

    `repository` is the spelling the WORKFLOW USES, stripped but never casefolded, because callers
    print it back to an operator ("fetches 'fs-gg/.GitHub', which is not the audited repository") and
    a message that silently rewrites the file's own text is a message that sends someone hunting for
    a line that does not exist. Compare it with :func:`repository_matches`, never with `==`.
    """

    job_id: str
    repository: str
    params: dict


def repository_matches(declared: object, wanted: object) -> bool:
    """Does a step's `repository:` name `wanted`? CASEFOLDED, because GitHub resolves it that way.

    `repository: fs-gg/.GitHub` and `repository: FS-GG/.github` fetch the same repository on a real
    runner, so a reader that tells them apart is answering a question GitHub does not ask. This was
    the second of #1553's two divergences: the gate compared casefolded (and its fixture asserts the
    case-variant spelling still resolves), `sparse_set` compared exactly and would have found zero
    authority steps in that same workflow.

    An EMPTY side never matches, including empty against empty. A step with no `repository:` is the
    caller's own checkout, not a checkout of a repository named by the empty string, and `wanted`
    being unreadable (`origin_repository()` returns None for a tree with no usable remote) is a
    question that cannot be answered rather than one that answers yes.
    """
    left = str(declared or "").strip()
    right = str(wanted or "").strip()
    return bool(left) and bool(right) and left.casefold() == right.casefold()


def checkout_steps(document: object) -> list[CheckoutStep]:
    """Every `actions/checkout` step in a parsed workflow that NAMES a `repository:`.

    THE ONE ANSWER to "is this step an `actions/checkout` aimed at repository R" (#1553). Both
    qualifying facts are read the way GitHub resolves them:

      * `uses:` is matched on owner/name, CASEFOLDED, with the `@ref` stripped — `actions/Checkout@v7`
        runs the real action, and a subject dropped for its spelling is a subject that left the set
        without anyone deciding to remove it.
      * `repository:` must be non-empty. A checkout with none is the caller's OWN repository: there is
        no second tree to under-fetch, and no cross-repo dependency to keep closed.

    Steps are returned in document order, and the FILTERING is left to the caller — the gate grades
    all of them, `sparse_set` wants exactly one named repository. See this module's header for why
    that half is deliberately not shared.

    Anything that is not the shape a workflow has — a document that is not a mapping, a `jobs:` that
    is not one, a job whose `steps:` is absent (a `uses:`-a-reusable-workflow job) or is not a list, a
    step that is not a mapping, a `with:` that is not a mapping — contributes NOTHING and raises
    nothing. That is not a fail-open: this function's answer is "which steps are subjects", and a
    malformed region contains no `actions/checkout` step anyone can read. The refusals in this module
    are about a step that IS a subject and whose block cannot be read.
    """
    found: list[CheckoutStep] = []
    jobs = document.get("jobs") if isinstance(document, dict) else None
    if not isinstance(jobs, dict):
        return found
    for job_id, job in jobs.items():
        if not isinstance(job, dict):
            continue
        steps = job.get("steps")
        if not isinstance(steps, list):
            continue
        for step in steps:
            if not isinstance(step, dict):
                continue
            uses = str(step.get("uses") or "").strip()
            if uses.split("@", 1)[0].strip().casefold() != CHECKOUT_ACTION:
                continue
            params = step.get("with")
            if not isinstance(params, dict):
                continue
            # `or ""`, not `get("repository", "")`: PyYAML resolves a bare `repository:` to None, and
            # `str(None)` is the four-character string "None" — a repository name that matches
            # nothing but is NOT empty, so a non-emptiness test written the other way qualifies the
            # step. `sparse_set`'s pre-#1553 copy did spell it `get("repository", "")`, and that was
            # HARMLESS there — it compared `== "FS-GG/.github"`, which "None" fails — so this is the
            # gate's spelling kept because it is the correct one, not a divergence being repaired.
            repository = str(params.get("repository") or "").strip()
            if not repository:
                continue
            found.append(CheckoutStep(str(job_id), repository, params))
    return found


def patterns_of(params: dict, where: str) -> list[str] | None:
    """The sparse patterns the runner would receive, or ``None`` when the key is ABSENT entirely.

    ``None`` and ``[]`` are not interchangeable here and the distinction is the whole of reading 3:
    ``None`` means no ``sparse-checkout:`` was declared — a full clone, which under-fetches nothing
    and is not a subject. A key that is PRESENT and supplies no pattern is refused instead, because
    whether the action then falls back to a full clone or fetches an empty tree is not readable off
    the workflow file and the two differ enormously.

    The membership test is ``in``, NOT ``params.get(...) is None``. PyYAML resolves a bare
    ``sparse-checkout:`` to ``None``, identically to an absent key, so ``get`` conflates a fetch of
    NOTHING with a fetch of EVERYTHING — the pre-hoist fail-open recorded in this module's header.
    """
    if PATTERNS_KEY not in params:
        return None
    raw = params.get(PATTERNS_KEY)
    # Mirror the action: split on newlines, drop blanks. A list is already split. Note that this is
    # what makes a FOLDED scalar visible — `>` has already joined its lines with spaces by the time
    # PyYAML hands it over, so it arrives here as ONE string with a space in it, and stays one.
    entries = raw if isinstance(raw, list) else str(raw if raw is not None else "").split("\n")
    patterns = [str(entry).strip() for entry in entries if str(entry).strip()]
    if not patterns:
        raise SparseRefusal(
            f"{where}: `sparse-checkout` is present but supplies no pattern. Whether the runner then "
            f"falls back to a full clone or fetches an empty tree is not readable off this file, and "
            f"the two differ enormously — remove the key, or give it a directory."
        )
    return patterns


def cone_mode_of(params: dict, where: str) -> bool:
    """``sparse-checkout-cone-mode``, defaulted the way ``actions/checkout`` documents it.

    An omitted flag IS ``true`` — the action's default — not "unknown". It changes what the patterns
    MEAN, so an unreadable one is a no-verdict rather than a guess.

    A QUOTED boolean is not unreadable. Every ``with:`` value reaches an action as a string and the
    action reads this one with ``core.getBooleanInput``, so its six textual YAML 1.2 core-schema
    spellings are accepted exactly; refusing the quoted spelling would red a workflow that works,
    which is what ``sparse_set`` did before the hoist. ``1`` and ``0`` are NOT boolean spellings for
    that action, whether PyYAML handed us strings or numbers, and are refused. An unevaluated
    ``${{ }}`` expression IS refused: its value is decided at run time, and nothing here can grade a
    mode it cannot know.
    """
    if CONE_KEY not in params:
        return True
    raw = params.get(CONE_KEY)
    if isinstance(raw, bool):
        return raw
    spelling = str(raw).strip()
    if spelling in TRUE_SPELLINGS:
        return True
    if spelling in FALSE_SPELLINGS:
        return False
    raise SparseRefusal(
        f"{where}: unreadable `{CONE_KEY}: {raw!r}`. It decides whether the patterns "
        f"are gitignore expressions or rooted directory prefixes, so the gate will not guess it."
    )
