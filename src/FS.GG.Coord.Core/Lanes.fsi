namespace FS.GG.Coord

/// LANES: the partition of the board into sets of work that can NEVER collide.
///
/// `Batch.schedule` answers "what may run RIGHT NOW, given what is in flight?" — a question about this
/// instant, recomputed every call. A LANE is the structural fact underneath it: two items whose
/// touch-sets do not overlap, and which are not joined by any chain of items that do, can never
/// contend for a file no matter who claims what or in which order. So a lane is a **connected
/// component of the item-overlap graph**, and items in different lanes are **provably** safe to run
/// concurrently.
///
/// THIS IS DERIVED, NEVER ASSERTED. Nobody — no agent, no human, no label on an issue — declares that
/// two items are safe together. Safety is computed from the `Paths:` declarations by the same
/// `TouchSet.conflicts` the scheduler already trusts, and a wrong answer here puts two workers in one
/// file, which is the failure the whole protocol exists to prevent (#273, #461). An agent's job is to
/// improve the INPUT — to give an undeclared item an honest touch-set — never to pronounce on the
/// OUTPUT.
///
/// WHAT IT BUYS, over the batch the scheduler already computes:
///
///   * **The ceiling.** The number of lanes holding startable work is the number of workers this board
///     can actually absorb. Fan out wider than that and the extra workers do not queue — they are
///     handed nothing, and `take` reports an empty queue over a board full of work (#440's shape).
///   * **The chokepoint, named.** A lane that swallows most of the board is not a scheduling accident;
///     it is one over-broad touch-set gluing unrelated work together. `scripts/fsgg-coord` is exactly
///     this, and it serialises four open items (#428). A partition makes it visible as a structure
///     rather than as a series of surprises.
///   * **Affinity.** A worker's follow-up findings almost always land in the files it was just
///     standing in — the same lane. Keeping a worker in its lane means its own next item is the one
///     least likely to be locked out from under it (#533's shape, one level up).
module Lanes =

    open FS.GG.Coord.Types

    /// An item that cannot be placed in a lane — and the three reasons are NOT the same fact.
    ///
    /// Collapsing them is #496 exactly: before they were told apart, an epic and a forgotten touch-set
    /// rendered identically, so nine items of real work sat on the board invisible to every worker who
    /// asked for work, while the board-health surface reported `0 error(s)`. Two of these are a CHORE.
    /// One of them is correct and must never be "fixed".
    type Unlanable =
        /// No `Paths:` line at all. Somebody forgot. **This is the chore**: the item is real work, and
        /// it is invisible to every scheduler until it is declared.
        | NoTouchSet of Item

        /// `Paths: none` — a decision item, an epic, an investigation whose scope IS the question.
        /// Unschedulable BY DESIGN, and correct. An agent proposing a touch-set for one of these is
        /// making the board worse, not better.
        | DeliberatelyNone of Item

        /// Declared, but every token is unmatchable — a leading `**/`, a `*` in the middle. It reserves
        /// NOTHING, so it would read as disjoint from every other worker: a lock that succeeds under
        /// exactly the conditions it exists to prevent (#273). **Also the chore**, and a more urgent one
        /// than a missing declaration, because it looks like a declaration.
        | UnusableTokens of item: Item * tokens: string list

        member Item: Item

    /// A set of items that may contend with each other, and with nothing outside it.
    type Lane =
        { /// The lowest-numbered item in the lane. A deterministic, stable-ish name — two workers
          /// partitioning the same board compute the same lane ids, which is what makes a lane
          /// something you can talk about ("take lane .github#422") rather than a per-call artefact.
          Id: Ref

          /// Lanes NEVER span repos. `Paths:` tokens are repo-relative — `scripts/foo` in one repo and
          /// `scripts/foo` in another name two different files in two different worktrees — so joining
          /// them would invent a phantom collision and glue two independent lanes into one (#312, #353).
          Owner: string
          Repo: string

          /// In issue order.
          Items: Item list

          /// Every matchable token any item in the lane declares — what the lane RESERVES as a whole.
          Tokens: string list

          /// The workers holding a live claim inside this lane. Non-empty ⇒ the lane is OCCUPIED, and a
          /// second worker sent here would be scheduled against files somebody is standing in.
          HeldBy: WorkerId list }

    type Partition =
        { Lanes: Lane list

          /// Reported, never dropped. An item that cannot be laned is the single most actionable thing
          /// on the board: it is either work nobody can pick up, or a declaration that protects nothing.
          Unlanable: Unlanable list }

    /// A token that is holding a lane together, and what it COSTS.
    ///
    /// This is the chore's target list, and it is the reason the chore can be a computation rather than
    /// an opinion. A lane of forty items is not forty items of naturally-coupled work — it is a handful
    /// of over-broad declarations gluing unrelated things together, and until now the only way to find
    /// out WHICH ones was to read forty issue bodies and guess.
    ///
    /// `SplitsInto` is the whole point: it is what the board's parallelism would become if this one
    /// token stopped being declared. A token declared by five items whose removal splits the lane into
    /// four is worth an afternoon. A token whose removal splits it into one is load-bearing and must be
    /// left alone — the work really is coupled, and narrowing it would be a lie that puts two workers in
    /// one file.
    type Glue =
        { Token: string

          /// The items that declare it. These are the issues the chore would have to narrow.
          DeclaredBy: Ref list

          /// How many lanes this lane becomes if this token is removed from every item above.
          /// `1` means removing it buys NOTHING — the items are coupled by something else too.
          SplitsInto: int }

    /// Rank a lane's tokens by how much parallelism each one is costing. Highest `SplitsInto` first.
    ///
    /// Removing a token is HYPOTHETICAL — this computes what the partition WOULD be, and changes
    /// nothing. Acting on it means narrowing a real `Paths:` declaration on a real issue, and that is a
    /// judgement about what the work actually touches. The tool can say *"this token costs you three
    /// lanes"*; only a human or an agent reading the issue can say *"and it did not need to declare it"*.
    /// The tool must never do the second, because a touch-set narrowed wrongly reserves less than the
    /// work touches, and then `batch` hands those files to somebody else (#273).
    val glue: lane: Lane -> Glue list

    /// Partition the board. Pure, total, deterministic.
    ///
    /// Two items are joined iff they are in the SAME repo and `TouchSet.conflicts` finds an overlapping
    /// token pair — the same predicate the scheduler reserves against, so a lane cannot disagree with
    /// the batch about what collides. Lanes are the transitive closure of that: A—B and B—C put A and C
    /// in one lane even though they do not touch, because B serialises them.
    val partition: items: Item list -> Partition

    /// The lanes that hold startable work and nobody is standing in — i.e. how many MORE workers this
    /// board can absorb right now.
    ///
    /// `startable` is passed in rather than computed here, so that this module has exactly one job and
    /// the scheduler stays the only place that decides schedulability (#485: it was computed in five
    /// places and agreed in none).
    val free: startable: (Item -> bool) -> partition: Partition -> Lane list

    /// The human projection. A rendering of the same answer; nothing may parse it.
    val explain: startable: (Item -> bool) -> partition: Partition -> string list
