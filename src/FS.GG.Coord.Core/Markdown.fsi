namespace FS.GG.Coord

/// FENCED CODE BLOCKS: where the code starts and stops, decided ONCE.
///
/// Every body this engine reads is markdown a human wrote, and every rule it applies to one — "is this a
/// `Paths:` declaration?", "is this a child ref?", "may I rewrite this line?" — has the same precondition:
/// the line must not be inside a fenced code block. A quoted declaration is a QUOTATION, and the protocol
/// docs quote themselves constantly (#277).
///
/// THIS MODULE EXISTS BECAUSE THAT PRECONDITION WAS LEARNED THREE TIMES AND SHARED ZERO (#972). `TouchSet`
/// learned it from #277 — a quoted `Paths:` became a fabricated touch-set. `Writes` learned it separately,
/// and its own comment says so: "the bash client learned this the hard way". `EpicBody` was written later,
/// never learned it at all, and shipped fence-blind until a body quoting a task list parsed as declaring a
/// child (#965). That is not three oversights. There was no shared concept to inherit, so fence-awareness
/// was taught by production incident — which means the module nobody had hurt yet was fence-blind BY
/// CONSTRUCTION, and the next body-parsing module added to this engine would have been too.
///
/// The three private trackers disagreed in every particular: what indent opens a fence (`^ {0,3}` vs
/// `^\s*`), whether a ``` block ends on a `~~~` line (both said yes; CommonMark says no), whether a ````
/// block ends on a ``` (both said yes), and what to do with an unterminated one. That is #485's shape —
/// one question, N implementations, agreeing in none — reproduced inside the typed core ADR-0034 built to
/// end it, and it is #864's shape exactly one module over: two callers, opposite verdicts, a green test
/// pinning each. #864's cure is this module's design: put the rule in one place and have every caller ASK
/// it, so there is no threshold left for a caller to get wrong.
///
/// THE RULE IS COMMONMARK, AND THAT CHOICE IS LOAD-BEARING. GitHub renders these bodies with a CommonMark
/// parser, so CommonMark is what the human who wrote the body SAW. Any other rule puts the engine and the
/// author in disagreement about which of their own words are code — and the author is not available to
/// ask. A parser that is merely self-consistent can still be confidently wrong about somebody else's
/// issue.
module Markdown =

    /// What a line of a body IS, once fences are accounted for.
    type LineKind =
        /// Outside every fence. The ONLY lines that declare, mention, or mean anything.
        | Text
        /// An opening or closing fence marker. Structure, never content.
        | FenceMarker
        /// Inside a fence. A quotation — of a task list, a `Paths:` line, somebody else's issue.
        | Code

    /// Every line of `body`, paired with what it is. Line endings are normalised (CRLF → LF) and a null
    /// body is an empty one, so no caller repeats either.
    ///
    /// Returns EVERY line, in order, because a caller that REWRITES a body must emit the lines it does not
    /// mean (`Writes.rewrite`) as faithfully as the ones it does. A caller that only READS wants `unfenced`.
    val classify: body: string -> (string * LineKind) list

    /// The lines of `body` that are outside every fenced code block — `classify`, keeping the `Text`.
    ///
    /// This is the read every rule in this engine actually wants: `TouchSet.parse` looks for `Paths:` here,
    /// `EpicBody.childRefs` looks for task lines here.
    val unfenced: body: string -> string list

    /// The marker that would CLOSE the fence `body` ends inside, or `None` when it ends outside one.
    ///
    /// An unterminated fence runs to the end of the body — CommonMark's rule, and GitHub's rendering — so
    /// anything APPENDED to such a body lands inside the code block and means nothing. That is not
    /// hypothetical: `Writes.rewrite` appended a `Paths:` declaration below an unterminated fence and then
    /// closed the fence underneath it, so `widen` reported success and `TouchSet.parse` read `Undeclared`
    /// (#972). A caller that appends must ask this FIRST and close the fence BEFORE what it appends.
    ///
    /// The marker is the OPENER's: a `~~~~` fence is closed by `~~~~`, never by "```". Length and spelling
    /// both matter, and a caller cannot know either without asking.
    val unterminatedFenceCloser: body: string -> string option
