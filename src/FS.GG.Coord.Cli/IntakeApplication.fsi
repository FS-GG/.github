namespace FS.GG.Coord.Cli

/// The LOCAL half of `.github#2134`'s public `intake` command contract, and the peer of
/// `DeliveryApplication` and `ReviewApplication` in this family.
///
/// Every result this module prints is one `fsgg.coord.intake-result/v1` object on stdout, and every
/// refusal is a typed `kind: "refusal"` carrying a reason — never a bare non-zero exit.
///
/// `apply` IS DELIBERATELY A REFUSAL, and stating so is the point of this signature. Until `Client`
/// owns the live duplicate/ownership/board transaction, there is no create path here at all; a
/// green validation is NEVER a create, and `writes` is reported as `0` on every path that can
/// succeed. A reader who cannot tell those apart from the implementation is exactly who this file
/// is for.
module IntakeApplication =

    /// Decode one filing draft from a JSON file, once, before either the local validation
    /// projection or (eventually) the live transaction.
    ///
    /// THIS IS PUBLIC ON PURPOSE. It is the single decoder: keeping it exported prevents `intake
    /// apply` growing a second, subtly different reading of the same document, which is the failure
    /// this module is shaped to avoid rather than a surface anyone needs for its own sake.
    ///
    /// STRICT IN BOTH DIRECTIONS. The root must be a JSON object; an UNKNOWN field is an error
    /// naming that field, so a misspelt key can never be silently dropped and read as absent. The
    /// eleven required strings, the `paths` string array, and `disposition` (exactly `create` or
    /// `reuse`) must all be present and well typed. The six optional fields — `phase`, `severity`,
    /// `blockedBy`, `blockedOn`, `backlogReason`, `judgementQuestion` — may be absent, but a
    /// present one must be a NON-EMPTY, non-whitespace string: `""` is refused rather than
    /// normalised to absent, because an empty declaration and no declaration mean different things
    /// on the board.
    ///
    /// REPORTS EVERY FAILURE IT FOUND, not the first: the error is the accumulated list joined with
    /// `"; "`, so one round trip tells a filer everything wrong with the draft. An unreadable file
    /// and unparseable JSON are distinguished from field-level findings by their own messages.
    ///
    /// Performs NO board or network IO and validates no semantics — `Intake.validate` owns those.
    val readDraft: path: string -> Result<FS.GG.Coord.Intake.Draft, string>

    /// Check that every declared path in the draft actually EXISTS in the target repository's
    /// working tree — the check that catches a `Paths:` declaration naming a file that was never
    /// created, which no amount of schema validation can see.
    ///
    /// RESOLVES THE TARGET CHECKOUT BY IDENTITY, NEVER BY NAME ALONE. Candidates are, in order: the
    /// git root containing the current directory, `$FSGG_REPOS_ROOT/<repository>`, and
    /// `<ancestor>/<repository>` for each ancestor of that git root. A candidate is accepted only
    /// if it exists AND its `origin` remote URL ends with `/<owner>/<repository>` or
    /// `:<owner>/<repository>` (case-insensitively, with any `.git` suffix and trailing slash
    /// stripped). A directory that merely has the right NAME is not the right repository, and is
    /// not used.
    ///
    /// REFUSES RATHER THAN PASSES WHEN IT CANNOT LOOK. `Error` — not `Ok` — is returned when no git
    /// root can be located at all, and when no candidate matches the target repository's identity.
    /// A path check that cannot reach its subject reports that it could not, which is the whole
    /// difference between this and a gate that manufactures confidence (epic .github#266).
    ///
    /// PATH SUBJECTS ARE THE DECLARED GLOB'S DIRECTORY. A trailing `/**`, `/*`, or `/` is stripped
    /// and the remaining prefix must exist as a file OR a directory, so a directory declaration is
    /// satisfied by the directory rather than requiring a matching entry inside it.
    ///
    /// A path that exists as either a file or a directory passes; all missing subjects are reported
    /// together in one comma-separated error. `git` failures while probing a candidate are treated
    /// as "not this candidate", never as success.
    val validateLivePaths: draft: FS.GG.Coord.Intake.Draft -> Result<unit, string>

    /// Run `intake <validate|apply> <draft.json>`, printing one `fsgg.coord.intake-result/v1`
    /// object and returning the matching exit code.
    ///
    /// `validate` decodes the draft, applies `Intake.validate`'s semantic rules, and THEN checks
    /// the declared paths against the live target checkout. All three must pass; the result is
    /// `kind: "validated"` with the draft id and an explicit `writes: 0`.
    ///
    /// `apply` REFUSES even on a draft that validates cleanly — `"live intake apply is not wired;
    /// validation performed zero writes"`. It is a typed refusal rather than an unimplemented
    /// branch, so a caller cannot mistake it for a create that happened to produce no output.
    ///
    /// An unknown action, a wrong argument count, an unreadable draft, and every validation finding
    /// are all `kind: "refusal"` with the reason on stdout. Exit codes are `ExitCode.Green` and
    /// `ExitCode.Error`; this command takes no claim and performs no board write on any path.
    val run: opts: Options.Options -> int
