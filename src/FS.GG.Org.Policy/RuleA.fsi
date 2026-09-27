namespace FS.GG.Org.Policy

/// The result for one workflow and whether a comparable trigger pair was audited.
/// A whole-tree audit with zero audited pairs cannot be accepted as clean.
type RuleAResult =
    { AuditedPair: bool
      Verdict: RuleAVerdict }

/// Aggregate path-agreement evidence for an already enumerated workflow set.
/// Zero pairs cannot masquerade as a clean fleet scan.
type RuleAAudit =
    { AuditedPairs: int
      Verdict: RuleAVerdict }

/// Pure source-only pull-request and push path agreement policy. This is neither an installed
/// gate nor a parity receipt.
module RuleA =
    /// Inspect one supplied workflow without reading or changing repository state.
    val inspect: path: string -> text: string -> RuleAResult

    /// Audit supplied workflow identities and bytes, refusing a zero-pair clean verdict.
    /// Filesystem enumeration belongs to a later adapter.
    val audit: workflows: seq<string * string> -> RuleAAudit
