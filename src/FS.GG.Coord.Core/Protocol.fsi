namespace FS.GG.Coord

/// THE PROTOCOL, AS DATA — the source every projection is emitted FROM (ADR-0034 §4.5).
///
/// A coordination rule was stated in up to six places — the ADR, the canonical doc, the tool, and four
/// `SKILL.md` bodies across two skill roots — then content-addressed into `repos.lock` and byte-copied
/// into six receivers: 54 vendored copies. The rules live HERE now, once, in the typed core that already
/// enforces them, and the prose is GENERATED. A rule cannot land in one tier and not the others, because
/// there are no tiers.
module Protocol =

    open Types

    /// One rule. `Id` is the anchor a projection references, so a doc can cite a rule without restating
    /// it and a reader can grep the id back to the code that enforces it.
    type Rule =
        { Id: string
          Title: string

          /// The rule itself, in one paragraph. This is the text that lands in every projection.
          Statement: string

          /// Why it is this way — the incident that bought it. Emitted into the canonical doc, and
          /// omitted from the terse skill projections.
          Because: string }

    /// A schedulability verdict, as the worker meets it.
    type VerdictDoc = { Kind: string; Meaning: string }

    /// The verdict union, as prose. Emitted from the same cases the scheduler returns, so the list a
    /// worker reads cannot omit one — which is what fourteen of the scheduler family's issues were.
    val verdicts: VerdictDoc list

    val touchSetGrammar: Rule
    val touchSetDeclaration: Rule
    val blockerResolution: Rule
    val checkOrder: Rule
    val claimLock: Rule
    val leaseRule: Rule
    val failClosed: Rule

    /// Every rule, in the order a projection presents them.
    val rules: Rule list
