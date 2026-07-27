"""The ONE reading of an ``actions/checkout`` sparse-checkout block (.github#1530).

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

WHAT IS NOT HERE, AND WHY
  * FINDING THE STEPS. The two callers ask different questions of the document — the gate wants every
    cross-repo checkout with its job id, ``sparse_set`` wants the single FS-GG/.github one and hard-
    fails on any other count — so each keeps its own selector and hands the ``with:`` mapping here.
    Their selectors do differ in a second way, and that is filed rather than smuggled into this
    refactor (see #1530's follow-up).
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

__all__ = [
    "SparseRefusal",
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

# `core.getBooleanInput`'s vocabulary, as the two callers between them already accepted it. Kept as
# data rather than an `in ("true", ...)` so the two directions cannot drift apart.
TRUE_SPELLINGS = {"true", "1"}
FALSE_SPELLINGS = {"false", "0"}


class SparseRefusal(Exception):
    """A sparse-checkout block this module will not guess at.

    NEVER raised for a block that is merely absent, and never carrying a usable value alongside: a
    caller that catches this has NO reading, which is the point. Map it to a no-verdict exit code —
    never to a finding, and never to green.
    """


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
    action reads this one with ``core.getBooleanInput``, so ``false`` and ``"false"`` are the same
    input; refusing the quoted spelling would red a workflow that works, which is what ``sparse_set``
    did before the hoist. An unevaluated ``${{ }}`` expression IS refused: its value is decided at
    run time, and nothing here can grade a mode it cannot know.
    """
    if CONE_KEY not in params:
        return True
    raw = params.get(CONE_KEY)
    if isinstance(raw, bool):
        return raw
    spelling = str(raw).strip().casefold()
    if spelling in TRUE_SPELLINGS:
        return True
    if spelling in FALSE_SPELLINGS:
        return False
    raise SparseRefusal(
        f"{where}: unreadable `{CONE_KEY}: {raw!r}`. It decides whether the patterns "
        f"are gitignore expressions or rooted directory prefixes, so the gate will not guess it."
    )
