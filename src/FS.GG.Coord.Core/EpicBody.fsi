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
    /// mention is not a declaration. A ref may be `#n`, `owner/repo#n`, or a full issue URL; a bare `#n`
    /// resolves against the epic's OWN repo, so all three land in the `owner/repo#n` form the sub-issue
    /// graph is compared in. Deduplicated and sorted, so the set is stable and directly diffable.
    val childRefs: selfOwner: string -> selfRepo: string -> body: string -> string list
