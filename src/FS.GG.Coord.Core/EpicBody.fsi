namespace FS.GG.Coord

/// An epic's OTHER record of its children: the refs its BODY declares, as a task list.
///
/// `lint` (EPIC-UNLINKED-CHILD) and the `done --flip` rollup both need to ask "which children does this
/// epic's body CLAIM?" and compare that to the sub-issue graph. Two hand-copied regexes in two engines is
/// exactly the drift #485 names ("computed in N places, agrees in N-1 at best"), so it is ONE definition
/// here, consumed by both.
module EpicBody =

    /// The child refs an epic body declares, canonicalized to `owner/repo#n` and sorted.
    ///
    /// A child is declared by a markdown TASK-LIST line — `- [ ]` / `- [x]`, and the `*` and `+` bullets
    /// GitHub renders a task list for too. The FIRST ref on the line is the child: `- [x] (b) #268 — see
    /// #100` declares #268, not #100. Prose, tables, and links elsewhere in the body declare nothing — a
    /// mention is not a declaration. A task line inside a fenced code block (``` or ~~~) is one of those
    /// mentions: an issue that QUOTES a task list to talk about one declares nothing by doing so, which is
    /// what a body's reader already assumes, because it is what GitHub renders (#965). A ref may be `#n`,
    /// `owner/repo#n`, or a full issue URL; a bare `#n`
    /// resolves against the epic's OWN repo, so all three land in the `owner/repo#n` form the sub-issue
    /// graph is compared in. Deduplicated and sorted, so the set is stable and directly diffable.
    val childRefs: selfOwner: string -> selfRepo: string -> body: string -> string list

    /// The acceptance lines that delegate to NOBODY — task lines carrying no issue ref, in body order.
    ///
    /// **AN EPIC'S ACCEPTANCE IS ITS CHILDREN.** Every acceptance line must be a child ref; a line that is
    /// not a child is not acceptance. That rule is what makes the roll-up sound BY CONSTRUCTION rather than
    /// by checking — #609's "an import cannot drift by construction", applied to the epic graph — and this
    /// function is the half that finds the lines breaking it.
    ///
    /// It exists because closure is transitive and verification is not (#965). `Done.rollUp` refuses a
    /// parent for four good reasons, and had no way to notice that a closed child was closed FALSELY:
    /// `AllResolved` is computed from child `state`, and a state is a record, not reality. So a parent
    /// inherited the truth of its children's closures without ever testing one. #561 is what that cost — its
    /// step 3 was the PARENT'S OWN work, delegated to no child, so no child could ever have covered it, and
    /// it was closed on the strength of its children being closed. The alternative fix — re-verify a
    /// parent's predicates at close — is unimplementable as stated: nothing can mechanically decide "the
    /// tripwire is intact" from prose. Deleting the un-delegated prose is what makes the question answerable.
    ///
    /// A ref-less task line is KEPT and named, never dropped. `childRefs` used to drop it silently, which is
    /// the opposite of the #266 answer `Done.bodyUnlinkedChildren`'s own `prune` gives eight lines away — it
    /// KEEPS a ref it cannot resolve. One question ("what do I do with something I cannot verify?"), two
    /// opposite answers, in one module. This is the other one, corrected.
    ///
    /// Fences apply here exactly as they do to `childRefs`: a quoted task list declares nothing and is
    /// therefore un-delegated acceptance NOWHERE. Lines come back trimmed and in body order — a human has to
    /// find each one to fix it, and sorting them would scramble the order they wrote them in.
    val undelegatedAcceptance: body: string -> string list
