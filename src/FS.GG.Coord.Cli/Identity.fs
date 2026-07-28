namespace FS.GG.Coord.Cli

module Identity =

    open System
    open System.Security.Cryptography
    open System.Text

    type Provenance =
        | FromFlag
        | FromEnv of var: string
        | FromSession of harness: string * sessionId: string
        | FromSharedSession of harness: string * sessionId: string * why: string

    type Worker =
        { Id: string
          Session: string option
          Provenance: Provenance
          /// WHO THIS PROCESS IS WHEN NOBODY TELLS IT — rules 2-4 with `--worker` taken away (#1646).
          ///
          /// `Id` is what the caller asked to BE; this is what the caller IS. They differ on exactly one
          /// shape — `--worker <somebody else>` — and that shape is what turns a shared harness session into
          /// a working impersonation, because both of the older checks (the id, and `session=`) match on
          /// facts a sibling of the fan-out legitimately holds.
          ///
          /// `None` is "this process resolves nothing of its own": a human operator, a harness exporting no
          /// session. It is UNASKABLE, never a clean answer — see `Writes.SelfIdentity`.
          Derived: string option }

    /// The word list, VERBATIM from the bash client — a derived id must be the same id on both engines, or a
    /// worker that switched engines mid-loop would appear to be two workers and lose its own lock.
    let private words =
        [| "finch"; "heron"; "wren"; "swift"; "kite"; "tern"; "rook"; "crake"; "snipe"; "plover"
           "merlin"; "godwit"; "curlew"; "dunlin"; "teal"; "smew"; "osprey"; "shrike"; "avocet"; "brant" |]

    let private env name =
        match Environment.GetEnvironmentVariable(name: string) with
        | null
        | "" -> None
        | v -> Some v

    /// A filename/id-safe slug, matching the bash client's `slug` so an id round-trips identically.
    let slug (s: string) =
        let cleaned =
            s
            |> Seq.map (fun c -> if Char.IsLetterOrDigit c || c = '-' || c = '_' then Char.ToLowerInvariant c else '-')
            |> Seq.toArray
            |> String

        cleaned.Trim('-')

    /// `name_from_seed`, ported EXACTLY: sha256 the seed, take the first 8 hex chars, the word from the
    /// first byte mod 20, the suffix from the next 3 hex chars. Same seed → same id on both engines.
    let private nameFromSeed (seed: string) =
        let hex =
            use sha = SHA256.Create()

            sha.ComputeHash(Encoding.UTF8.GetBytes seed)
            |> Array.map (fun b -> b.ToString("x2"))
            |> String.concat ""

        let head = hex.Substring(0, 8)
        let idx = Convert.ToInt32(head.Substring(0, 2), 16) % words.Length
        $"%s{words.[idx]}-%s{head.Substring(2, 3)}"

    /// Detect the agent harness and its session id — the same ladder as the bash client, including the
    /// `FSGG_AGENT_SESSION_ID` escape hatch for a harness we do not know about.
    let private harnessSession () : (string * string) option =
        match env "CLAUDE_CODE_SESSION_ID" with
        | Some s -> Some("claude-code", s)
        | None ->
            match env "OPENCODE_SESSION_ID" with
            | Some s -> Some("opencode", s)
            | None ->
                match env "FSGG_AGENT_SESSION_ID" with
                | Some s -> Some(env "FSGG_AGENT_HARNESS" |> Option.defaultValue "unknown", s)
                | None -> None

    /// Does one session id name one worker, on this harness? Only where subagents get their own session.
    /// Claude Code shares one id across subagents; an unknown harness is assumed to share (the safe reading).
    let private sessionIsPerWorker (harness: string) =
        match harness with
        | "opencode" -> true
        | _ -> false

    /// WHO THIS PROCESS IS WITH `--worker` TAKEN AWAY (#1646) — rules 2-4, and nothing else.
    ///
    /// ONE DEFINITION, and `resolve` below is a CALLER of it rather than a second copy. The flag branch and
    /// the flagless branch must not be able to disagree about who this process is — that is #485's shape (one
    /// question, two implementations) sitting inside the function whose whole job is to answer it once — and
    /// it is also where the `FromSharedSession` warning text lives, so `whoami`'s warning and the refusal that
    /// quotes it cannot drift.
    ///
    /// It returns the PROVENANCE with the id, because a refusal that sends a worker back to this id has to be
    /// able to say whether the id it is recommending is itself a shared one (#419).
    ///
    /// A `$FSGG_WORKER` that slugs to NOTHING is not an identity (#1070) — it is the id every annihilating
    /// input shares — so it derives `None` rather than an empty string that would then "disagree" with every
    /// `--worker` and read as an impersonation. Refusing a real worker over a malformed variable would be a
    /// lockout, and `resolved` already refuses that input on its own terms when it is the id being USED.
    ///
    /// A SHARED session id still counts. It names the SESSION rather than the worker — `whoami` warns exactly
    /// that — so it is a poor identity; but "poor identity" and "no identity" are different, and the shape
    /// being closed is *naming a different worker*, which a shared-session caller does just as deliberately as
    /// any other. Excluding it here would leave the hole open to any caller who simply never minted an id.
    let private derivedIdentity () : (string * Provenance) option =
        match env "FSGG_WORKER" with
        | Some w ->
            match slug w with
            | "" -> None
            | id -> Some(id, FromEnv "FSGG_WORKER")
        | None ->
            match harnessSession () with
            | Some(harness, sid) ->
                let id = nameFromSeed sid

                if sessionIsPerWorker harness then
                    Some(id, FromSession(harness, sid))
                else
                    // DERIVED, BUT UNSAFE. Every subagent of this session shares the id. It is still an
                    // identity (a single-worker session is the common case and it IS that worker), but the
                    // provenance says so, so a fan-out caller — and any refusal that points back at this id —
                    // can see the hazard.
                    Some(id, FromSharedSession(harness, sid, $"every subagent of this %s{harness} session shares this id"))
            | None -> None

    let private derivedId () : string option = derivedIdentity () |> Option.map fst

    let derivedProvenance () : Provenance option = derivedIdentity () |> Option.map snd

    /// THE ID IS THE SLUG, SO THE SLUG IS WHAT MUST BE CHECKED (#1070).
    ///
    /// `slug` maps every character that is not a letter, digit, `-` or `_` to `-` and then trims `-`, so it
    /// ANNIHILATES an all-punctuation input: `.`, `///`, `!!!`, `..`, `.-.` all slug to `""`. The guard used
    /// to sit on the INPUT (`not (String.IsNullOrWhiteSpace w)`) and return the OUTPUT of a transformation
    /// nothing re-read — and `.` is not whitespace, so it passed, and `resolve` answered `Ok` with an EMPTY
    /// id.
    ///
    /// **An empty id is an id several workers SHARE** — every caller whose input annihilates lands on the
    /// same one — which is #419's collapse reached from inside the resolver rather than from an agent
    /// inventing an id. It is also this function contradicting its own documented rule 4 (*refuse rather
    /// than invent an id that several workers would share*) on precisely the inputs that rule exists for.
    ///
    /// So the post-condition is checked where it is PRODUCED, once, for both branches: a `Worker` this
    /// module returns has a non-empty id, by construction rather than by the caller remembering to look.
    ///
    /// REFUSE — never fall through to the session-derived id. On Claude Code every subagent shares one
    /// session id, so a fallback would swap one shared id for another and `whoami` still would not warn.
    /// And the message names the OFFENDING INPUT (#611): "could not derive a worker id" alone would send a
    /// worker to check a variable they did set.
    let private resolved (raw: string) (source: string) (session: string option) (provenance: Provenance) =
        match slug raw with
        | "" ->
            Error
                $"the worker id from %s{source} ('%s{raw}') has no letter or digit in it, so it slugs to NOTHING — and an EMPTY id is one that EVERY worker whose id does this would share, which is the double-claim ADR-0027 exists to prevent (#419). Give this worker a real id (do NOT invent one): eval \"$(scripts/fsgg-coord whoami --mint)\""
        | id ->
            Ok
                { Id = id
                  Session = session
                  Provenance = provenance
                  Derived = derivedId () }

    let resolve (worker: string option) : Result<Worker, string> =
        let session = harnessSession () |> Option.map snd

        match worker with
        | Some w when not (String.IsNullOrWhiteSpace w) -> resolved w "--worker" session FromFlag
        | _ ->
            match env "FSGG_WORKER" with
            | Some w -> resolved w "$FSGG_WORKER" session (FromEnv "FSGG_WORKER")
            | None ->
                // RULE 3, AND IT IS `derivedIdentity`'S ANSWER RATHER THAN A SECOND ONE. With no flag and no
                // `$FSGG_WORKER`, "who did this process resolve for itself" and "who is this process acting
                // as" are the same question — so they must be the same code, or `Derived` could disagree with
                // `Id` on the one path where they are identical by definition (#1646).
                match derivedIdentity () with
                | Some(id, provenance) ->
                    Ok
                        { Id = id
                          Session = session
                          Provenance = provenance
                          Derived = Some id }
                | None ->
                    // REFUSE rather than invent a shared id. The bash client persists a per-checkout id
                    // here; the engine does not, because a persisted-per-checkout id is itself a shared id
                    // under a fan-out sharing one checkout — the exact thing ADR-0027 forbids. Make the
                    // caller say who they are.
                    Error
                        "could not derive a worker id: no --worker, no $FSGG_WORKER, and no agent session to derive one from. Pass --worker <id>, or run: eval \"$(scripts/fsgg-coord whoami --mint)\" in EACH worker's shell (do NOT invent one — N workers sharing an id is the double-claim ADR-0027 exists to prevent)."

    let mint () =
        // Genuinely random, INCLUDING the word — a pid+time seed draws the same word for every agent a
        // harness fans out within one second (#419), so both halves come from the CSPRNG.
        let bytes = RandomNumberGenerator.GetBytes 3
        let word = words.[int bytes.[0] % words.Length]
        let suffix = ((int bytes.[1] <<< 8) ||| int bytes.[2]).ToString("x4")
        $"%s{word}-%s{suffix}"

    let explain (worker: Worker) =
        let rule =
            match worker.Provenance with
            | FromFlag -> "--worker flag"
            | FromEnv var -> $"$%s{var}"
            | FromSession(harness, sid) -> $"%s{harness} session id (%s{sid}) — per-worker on this harness"
            | FromSharedSession(harness, sid, why) -> $"%s{harness} session id (%s{sid}) — WARNING: %s{why}"

        [ $"worker: %s{worker.Id}"
          $"derived: %s{rule}"
          match worker.Session with
          | Some s -> $"session: %s{s}"
          | None -> "session: (none)"
          // WHO THIS PROCESS IS WHEN NOBODY TELLS IT (#1646) — and it is printed ONLY when it disagrees with
          // the id in use, because that is the only time it says anything the two lines above do not. A
          // disagreement is the impersonation shape, and `whoami` is where a worker checks its identity
          // BEFORE a lock command refuses it — a refusal a worker cannot reproduce from `whoami` is one they
          // have to guess at.
          match worker.Derived with
          | Some d when d <> worker.Id ->
              // NAME WHAT IS ACTUALLY REFUSED. An earlier wording said "over their live marker", which was
              // true of `release`/`heartbeat`/`widen` and NOT of `claim` — `claim` refuses the argv itself,
              // live marker or not. A worker who cannot predict from `whoami` which of its verbs will work
              // has been given a report that costs a failed command to interpret.
              $"self: %s{d} — this process's OWN id. `--worker %s{worker.Id}` names a DIFFERENT worker, so claim/release/heartbeat/widen on %s{worker.Id}'s items REFUSE (#1646)."
          | _ -> () ]
