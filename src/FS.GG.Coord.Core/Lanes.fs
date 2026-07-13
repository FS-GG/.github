namespace FS.GG.Coord

module Lanes =

    open System.Collections.Generic
    open FS.GG.Coord.Types

    type Unlanable =
        | NoTouchSet of Item
        | DeliberatelyNone of Item
        | UnusableTokens of item: Item * tokens: string list

        member this.Item =
            match this with
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
        | DeclaredNone -> []

    let private unmatchable (ts: TouchSet) =
        match ts with
        | Declared tokens ->
            tokens
            |> List.choose (
                function
                | Unmatchable t -> Some t
                | Matchable _ -> None
            )
        | Undeclared
        | DeclaredNone -> []

    /// A live claim on the item, if the caller observed one. A lane holding one is OCCUPIED.
    let private holder (item: Item) =
        match item.Claim with
        | Some(claim, _) -> Some claim.Worker
        | None -> None

    let partition (items: Item list) : Partition =

        // THE THREE WAYS AN ITEM CANNOT BE LANED, KEPT APART (#496).
        //
        // `Declared` with at least one MATCHABLE token is the only lanable shape. A declaration whose
        // every token matches nothing is NOT a lane of one — it reserves nothing, so it would read as
        // disjoint from every other worker, which is the lock succeeding under exactly the conditions
        // it exists to prevent (#273). It is a broken declaration, and it is reported as one.
        let lanable, unlanable =
            items
            |> List.fold
                (fun (lanable, unlanable) item ->
                    match item.TouchSet with
                    | Undeclared -> lanable, NoTouchSet item :: unlanable
                    | DeclaredNone -> lanable, DeliberatelyNone item :: unlanable
                    | Declared _ ->
                        match matchable item.TouchSet with
                        | [] -> lanable, UnusableTokens(item, unmatchable item.TouchSet) :: unlanable
                        | _ -> item :: lanable, unlanable)
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

            for token in lane.Tokens do
                yield $"    %s{token}"

            for item in lane.Items do
                let mark = if startable item then "→" else " "
                yield $"  %s{mark} %s{item.Ref.Short}"

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
                  | UnusableTokens(item, tokens) ->
                      let named = String.concat ", " tokens

                      yield
                          $"  %s{item.Ref.Short}  declares %d{List.length tokens} token(s) that match NOTHING (%s{named})"

                      yield "      It reserves nothing, so it reads as disjoint from everybody. Worse than undeclared."
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
