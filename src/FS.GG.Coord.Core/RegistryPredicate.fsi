namespace FS.GG.Coord

/// The registry-predicate oracle `P(id, field, value)` (ADR-0050, call-site A / .github#1202).
///
/// One question, asked at a state transition, that today routes through a human every time: for a
/// registry `id`/`field`/`value`, **does the row exist, and does the OWNING producer's manifest
/// agree?** #1194 asked `.github` to set `fs-gg-playtest` `mirrored: true`; FS.GG.Game's manifest —
/// the owner (`owner:` decides whose word is law) — declares `false`. The request was refutable from
/// data at filing time, yet nothing caught it until hand-triage. This module is the refutation, made
/// mechanical.
///
/// PURE, like every sibling in this project. The reader is split the way `Kit` splits its digest
/// obligation (#485): the impure edge (reading `registry/skills.yml` off disk, locating and parsing
/// the owning manifest under a `--repos-root`) lives in the Cli; what it hands in is already-resolved
/// data, and the verdict logic here is unit-tested with no filesystem. This is also why the oracle is
/// safe in a receiver (ADR-0042's carve-out): the Cli reports `registry/` absent as `Unreadable`, and
/// `classify` turns that into `Unknown` — never a refutation it could not prove.
///
/// FAITHFUL-OR-ABSTAIN. The oracle compares a field only when it can do so exactly as the Python
/// reconcile (`scripts/fsgg-skill-registry-check`) does — the ADR's "reuse the reconcile rules, do
/// not fork a second predicate" (#1202). `mirrored` is that field: a bool whose ABSENCE is `Unknown`,
/// never `false` (`mirror_of` / .github#658). Every other field is `Unknown` here rather than compared
/// by a rule that could silently drift from the reconcile — see `supportsField`.
module RegistryPredicate =

    /// One `- { id: ..., owner: ..., source: ..., <field>: ... }` row of `registry/skills.yml`, parsed
    /// off its inline flow-mapping. `Fields` carries every `key: value` pair (including `owner`/`source`
    /// again, for uniformity); `Owner`/`Source` are lifted out because the manifest lookup keys on them.
    type Row =
        { Id: string
          Owner: string option
          Source: string option
          Fields: Map<string, string> }

    /// What the OWNING producer's manifest says about a given `(id, field)` — resolved by the Cli's
    /// impure edge and handed in. The three cases are the three the ADR's verdict distinguishes.
    type OwnerDeclaration =
        /// The owning manifest was read and declares this exact value for the field.
        | Declares of value: string
        /// The owning manifest was read, but declares NO value for the field. An absent value is
        /// UNKNOWN, never `false` (`mirror_of` / .github#658) — this becomes `Unknown`, not a match.
        | Silent
        /// The owning manifest, or the owner checkout, or `registry/` itself could not be read. In a
        /// receiver this is the normal answer (ADR-0042). Becomes `Unknown` — fail closed (#266).
        | Unreadable of reason: string

    /// A machine-readable assertion: the `id`/`field`/`value` triple a `cross-repo:request` carries in
    /// the issue form. Structured by construction so the claim is not parsed out of prose (#683).
    type Assertion =
        { Id: string
          Field: string
          Value: string }

    /// The oracle's three-valued verdict (ADR-0050). There is no `bool` here: "I could not tell" is a
    /// first-class answer that never advances and never refutes.
    type Verdict =
        /// The row exists and the owning manifest declares `field = value`.
        | Agrees
        /// The owning manifest declares a DIFFERENT value. Carries the owner-declared value and the
        /// governing note, which is what the filing-time check auto-comments before triage.
        | Contradicts of ownerValue: string * note: string
        /// The row, the manifest, the owner checkout, or the field's comparison rule was not available.
        | Unknown of reason: string

    /// Parse the data rows of `registry/skills.yml`. Every data row is a single-line YAML flow-mapping
    /// (`  - { id: x, owner: y, ... }`); a line that is not one — a comment, the `skills:` header,
    /// blank — is skipped. This is the same "parse the one shape the file actually uses, not YAML in
    /// general" tack `Kit.parseLock` takes with `repos.lock`; there is no YAML reader in this engine
    /// and ADR-0042 says there must not be one that a receiver would carry.
    val parseRows: yaml: string -> Row list

    /// The row with this `id`, or `None`.
    val findRow: rows: Row list -> id: string -> Row option

    /// Extract the `id`/`field`/`value` assertion from a filed `cross-repo-request` issue body, or
    /// `None` when the filer made none (the fields left blank / `_No response_`, or only some filled).
    /// Reads the fixed `### <label>` sections a GitHub issue FORM renders — the structured source,
    /// not the free-text prose (#683). A partial triple is `None`: an un-checkable assertion is no
    /// assertion. The labels it anchors on are `assertionLabels`.
    val parseAssertion: issueBody: string -> Assertion option

    /// The three form labels `parseAssertion` anchors on, in `(id, field, value)` order. Exposed so a
    /// test can pin them against `.github/ISSUE_TEMPLATE/cross-repo-request.yml` — if the form and the
    /// parser drift, the assertion silently stops being read (#266's shape), so they are gated equal.
    val assertionLabels: string * string * string

    /// Whether the oracle can compare this field EXACTLY as the reconcile does. Only `mirrored` today
    /// (a bool, absent ⇒ Unknown). A field it does not support is `Unknown`, never guessed — comparing
    /// `materializes-when` needs the canonical `normalize_when` seam (.github#398) this oracle does not
    /// yet share, and a second normalizer here is the divergence ADR-0050 forbids.
    val supportsField: field: string -> bool

    /// The oracle. `rows` is `registry/skills.yml` parsed; `owner` is what the owning manifest declares
    /// for `(assertion.Id, assertion.Field)`, already resolved by the Cli. See the module doc for the
    /// fail-closed shape: a missing row, an unsupported field, an `Unreadable` or `Silent` owner all
    /// yield `Unknown`.
    val classify: rows: Row list -> owner: OwnerDeclaration -> assertion: Assertion -> Verdict

    /// The verdict word — `agrees` / `contradicts` / `unknown` — for stdout and the exit-code contract.
    val name: verdict: Verdict -> string
