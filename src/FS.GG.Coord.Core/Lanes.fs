namespace FS.GG.Coord

module Lanes =

    open System.Collections.Generic
    open FS.GG.Coord.Types

    type Unlanable =
        | NoTouchSet of Item
        | DeliberatelyNone of Item
        | UnusableTokens of item: Item * tokens: string list
        /// The body was never read, so the touch-set is UNKNOWN. It cannot be laned — and it must not be
        /// silently dropped either, or a lane partition would look complete over an item nobody saw.
        | Unread of item: Item * reason: string

        member this.Item =
            match this with
            | Unread(i, _) -> i
            | NoTouchSet i -> i
            | DeliberatelyNone i -> i
            | UnusableTokens(i, _) -> i

    type Lane =
        { Id: Ref
          Owner: string
          Repo: string
          Items: Item list
          Tokens: string list
          HeldBy: WorkerId list }

    type Partition =
        { Lanes: Lane list
          Unlanable: Unlanable list }

    let private matchable (ts: TouchSet) =
        match ts with
        | Declared tokens ->
            tokens
            |> List.choose (
                function
                | Matchable t -> Some t
                | Unmatchable _ -> None
            )
        | Undeclared
        | DeclaredNone
        // A chore reserves no files, so it names no matchable token — it lanes as a degenerate singleton
        // that conflicts with nothing (#1103 leg 8).
        | DeclaredChore
        | Unreadable _ -> []

    /// A live claim on the item, if the caller observed one. A lane holding one is OCCUPIED.
    let private holder (item: Item) =
        match item.Claim with
        | Some(claim, _) -> Some claim.Worker
        | None -> None

    let partition (items: Item list) : Partition =

        // THE THREE WAYS AN ITEM CANNOT BE LANED, KEPT APART (#496).
        //
        // A `Declared` touch-set is lanable only if `TouchSet.usability` says so — and ASKING is the
        // whole point (#864). This used to test `matchable <> []` and lane anything with one live token,
        // while `Schedulability` refused anything with one DEAD token. Same item, same question, opposite
        // answers, a green test pinning each. ADR-0034's promise is one schedulability function; `free`
        // and `explain` keep it by taking `startable` as a parameter, and then this fold quietly
        // re-decided half of it before that parameter was ever consulted.
        //
        // The scheduler's rule wins because it is the safe one, and because a lane is a claim about work
        // that CAN BE STARTED. An item the scheduler refuses forever cannot contend with anybody, so
        // laning it is a lie twice over: its dead tokens can glue two real lanes into one (understating
        // the ceiling on tokens that reserve NOTHING), and the chore list — the one surface whose job is
        // board health — never names it. The worker then sees a full board and nothing to do, and reads a
        // BROKEN DECLARATION as blocked work. That is #496's shape one layer up.
        let lanable, unlanable =
            items
            |> List.fold
                (fun (lanable, unlanable) item ->
                    match item.TouchSet with
                    | Unreadable reason -> lanable, Unread(item, reason) :: unlanable
                    | Undeclared -> lanable, NoTouchSet item :: unlanable
                    | DeclaredNone -> lanable, DeliberatelyNone item :: unlanable
                    // A chore IS startable (#1103 leg 8) — it lanes, as a degenerate singleton that
                    // reserves nothing and so conflicts with nobody. It is real absorbable work, unlike
                    // the three unlanable shapes above, so it belongs on the lanable side.
                    | DeclaredChore -> item :: lanable, unlanable
                    | Declared _ ->
                        // Collapsed, as in `Schedulability` and for its reason (#945): a lane is about
                        // what a declaration RESERVES, and both shapes reserve less than they name.
                        match TouchSet.usability item.TouchSet with
                        | TouchSet.AllUnmatchable bad
                        | TouchSet.SomeUnmatchable bad -> lanable, UnusableTokens(item, bad) :: unlanable
                        | TouchSet.Usable -> item :: lanable, unlanable)
                ([], [])

        let lanable = lanable |> List.rev
        let unlanable = unlanable |> List.rev

        let arr = List.toArray lanable
        let n = arr.Length

        // Union-find. The board is hundreds of items, so the O(n²) pairwise pass is far cheaper than
        // the machinery to avoid it — and it uses the SAME `TouchSet.conflicts` the scheduler reserves
        // against, so a lane can never disagree with the batch about what collides (#485).
        let parent = Array.init n id

        let rec find i =
            if parent[i] = i then
                i
            else
                parent[i] <- find parent[i] // path compression
                parent[i]

        let union i j =
            let ri, rj = find i, find j

            if ri <> rj then
                parent[max ri rj] <- min ri rj // lowest index wins, so lane ids are deterministic

        for i in 0 .. n - 1 do
            for j in i + 1 .. n - 1 do
                let a, b = arr[i], arr[j]

                // SAME REPO, OR NO EDGE. `Paths:` tokens are repo-relative — `scripts/foo` in one repo
                // and `scripts/foo` in another name two different files in two different worktrees — so
                // comparing them bare invents a phantom collision (#312, #353) and would glue two
                // independent lanes into one, halving the parallelism the board actually has.
                if a.Ref.Owner = b.Ref.Owner && a.Ref.Repo = b.Ref.Repo then
                    if not (List.isEmpty (TouchSet.conflicts a.TouchSet b.TouchSet)) then
                        union i j

        let lanes =
            [ 0 .. n - 1 ]
            |> List.groupBy find
            |> List.map (fun (_, idxs) ->
                let members = idxs |> List.map (fun i -> arr[i]) |> List.sortBy (fun it -> it.Ref.Number)

                let head = List.head members

                { Id = head.Ref
                  Owner = head.Ref.Owner
                  Repo = head.Ref.Repo
                  Items = members
                  Tokens = members |> List.collect (fun it -> matchable it.TouchSet) |> List.distinct |> List.sort
                  HeldBy = members |> List.choose holder |> List.distinct })
            |> List.sortBy (fun lane -> (lane.Owner, lane.Repo, lane.Id.Number))

        { Lanes = lanes; Unlanable = unlanable }

    type Glue =
        { Token: string
          DeclaredBy: Ref list
          SplitsInto: int }

    /// The number of connected components among `items`, comparing touch-sets with `tokens` of each item
    /// projected out. Same repo throughout (a lane never spans repos), so no repo test is needed here.
    let private componentsWithout (drop: string) (items: Item list) =
        let project (it: Item) =
            let kept =
                match it.TouchSet with
                | Declared tokens ->
                    tokens
                    |> List.filter (
                        function
                        | Matchable t -> t <> drop
                        | Unmatchable _ -> true
                    )
                | other -> []

            { it with TouchSet = Declared kept }

        let arr = items |> List.map project |> List.toArray
        let n = arr.Length
        let parent = Array.init n id

        let rec find i =
            if parent[i] = i then
                i
            else
                parent[i] <- find parent[i]
                parent[i]

        for i in 0 .. n - 1 do
            for j in i + 1 .. n - 1 do
                if not (List.isEmpty (TouchSet.conflicts arr[i].TouchSet arr[j].TouchSet)) then
                    let ri, rj = find i, find j

                    if ri <> rj then
                        parent[max ri rj] <- min ri rj

        // An item left with NO matchable token reserves nothing and joins nothing — it becomes its own
        // singleton. That is the honest count: without the dropped token it genuinely contends with
        // nobody, which is exactly the parallelism the chore would be buying.
        [ 0 .. n - 1 ] |> List.map find |> List.distinct |> List.length

    let glue (lane: Lane) : Glue list =
        // A lane of one cannot be glued to anything.
        if List.length lane.Items < 2 then
            []
        else

        lane.Tokens
        |> List.map (fun token ->
            let declaredBy =
                lane.Items
                |> List.filter (fun it ->
                    match it.TouchSet with
                    | Declared tokens ->
                        tokens
                        |> List.exists (
                            function
                            | Matchable t -> t = token
                            | Unmatchable _ -> false
                        )
                    | _ -> false)
                |> List.map (fun it -> it.Ref)

            { Token = token
              DeclaredBy = declaredBy
              SplitsInto = componentsWithout token lane.Items })
        // Highest payoff first, then by the number of issues that would have to be edited (cheapest
        // first), then by name so the ranking is deterministic and two workers agree on the target.
        |> List.sortBy (fun g -> (-g.SplitsInto, List.length g.DeclaredBy, g.Token))

    let free (startable: Item -> bool) (partition: Partition) : Lane list =
        // A lane is FREE only if nobody is standing in it AND there is something in it to start. Both
        // halves matter: a lane with no startable item is not capacity, it is a lane whose work is all
        // blocked or done — and counting it would overstate the ceiling and send a worker to an empty
        // lane, which is #440 ("no schedulable item" over a board full of work) wearing a lane's hat.
        partition.Lanes
        |> List.filter (fun lane -> List.isEmpty lane.HeldBy && List.exists startable lane.Items)

    let explain (startable: Item -> bool) (partition: Partition) : string list =
        let freeLanes = free startable partition

        [ let total = List.length partition.Lanes

          yield $"%d{total} lane(s) — %d{List.length freeLanes} free, %d{total - List.length freeLanes} occupied or with no startable work."

          yield ""

          for lane in partition.Lanes do
            let held =
                match lane.HeldBy with
                | [] -> ""
                | ws -> "  HELD by " + (ws |> List.map (fun w -> w.Value) |> String.concat ", ")

            let starts = lane.Items |> List.filter startable |> List.length

            yield $"lane %s{lane.Id.Short}  (%d{List.length lane.Items} item(s), %d{starts} startable)%s{held}"

            for item in lane.Items do
                let mark = if startable item then "→" else " "
                yield $"  %s{mark} %s{item.Ref.Short}"

            // THE GLUE, and it is the actionable half. A lane of forty items is not forty items of
            // naturally-coupled work — it is a handful of over-broad declarations holding unrelated
            // things together. Only tokens whose removal actually BUYS something are worth printing;
            // one that splits the lane into 1 is load-bearing, and narrowing it would be a lie.
            let worthwhile = glue lane |> List.filter (fun g -> g.SplitsInto > 1)

            if not (List.isEmpty worthwhile) then
                yield "  the declarations gluing this lane together:"

                for g in worthwhile |> List.truncate 5 do
                    let refs = g.DeclaredBy |> List.map (fun r -> r.Short) |> String.concat ", "

                    yield
                        $"    %s{g.Token}  — declared by %d{List.length g.DeclaredBy}; drop it and this lane becomes %d{g.SplitsInto} lanes"

                    yield $"        ({refs})"

            yield ""

          // THE CEILING. Fan out wider than this and the extra workers are handed NOTHING — and `take`
          // reports an empty queue over a board that is full of work.
          yield $"CEILING: %d{List.length freeLanes} worker(s) can start right now, provably without colliding."

          // The chore, and it is the actionable half of this whole report.
          let chores =
              partition.Unlanable
              |> List.filter (
                  function
                  | DeliberatelyNone _ -> false // correct, and never a chore
                  | _ -> true
              )

          if not (List.isEmpty chores) then
              yield ""

              yield
                  $"%d{List.length chores} item(s) CANNOT be laned — they are invisible to every worker who asks for work:"

              for chore in chores do
                  match chore with
                  | NoTouchSet item -> yield $"  %s{item.Ref.Short}  no `Paths:` at all — somebody forgot. It is real work, and nobody can pick it up."
                  | Unread(item, reason) ->
                      // NOT a chore anybody can fix by declaring a touch-set — we never READ the item.
                      // Saying "somebody forgot" here would send an agent to add a `Paths:` line to a body
                      // that may already have one.
                      yield
                          $"  %s{item.Ref.Short}  its body could not be READ, so its touch-set is unknown — not absent (%s{reason}). Retry; do not declare one for it."
                  | UnusableTokens(item, tokens) ->
                      let named = String.concat ", " tokens

                      // "declares N of M" — because SOME of them being dead is the case that reads as
                      // fine and is not (#864). Saying "it reserves nothing" here was true only when
                      // every token was dead; a partly-dead declaration reserves its live tokens and is
                      // still refused by the scheduler, so the flat claim was a lie on exactly the item
                      // this list exists to surface.
                      let declared =
                          match item.TouchSet with
                          | Declared ts -> List.length ts
                          | _ -> List.length tokens

                      yield
                          $"  %s{item.Ref.Short}  declares %d{List.length tokens} of %d{declared} `Paths:` token(s) that match NOTHING (%s{named})"

                      yield
                          "      Those tokens reserve nothing, so the files they name are invisible to every other worker's"

                      yield
                          "      overlap check — and the scheduler refuses the item outright, so nobody can start it. Worse"

                      yield "      than undeclared: it LOOKS declared."
                  | DeliberatelyNone _ -> ()

          let deliberate =
              partition.Unlanable
              |> List.filter (
                  function
                  | DeliberatelyNone _ -> true
                  | _ -> false
              )

          if not (List.isEmpty deliberate) then
              yield ""

              yield
                  $"%d{List.length deliberate} item(s) declare `Paths: none` — epics and decisions. Unschedulable BY DESIGN; not a chore." ]
