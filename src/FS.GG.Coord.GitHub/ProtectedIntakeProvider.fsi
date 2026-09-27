namespace FS.GG.Coord.GitHub

open FS.GG.Coord.GitHub.ProtectedIntakeJournal

/// Read-only qualification of GitHub's public mutation surfaces for the retained intake journal.
/// No callable write port is exposed until one provider operation can both compare the exact old head
/// and publish the exact proposal commit and objects.
module ProtectedIntakeProvider =
    type UnsupportedAuthority =
        { ProtectedGenesisVerified: bool
          RestUpdateRefHasExpectedHead: bool
          GraphQlPreservesProposalCommit: bool
          ResponseUnknownReconciliationDefined: bool
          Reasons: string list }

    type Feasibility =
        | Unsupported of UnsupportedAuthority

    type ResponseUnknownReconciliation =
        /// The exact proposal is the current protected head. This proves provider application but is
        /// deliberately not a CAS grant for the interrupted caller.
        | AppliedWithoutGrant of Snapshot
        /// The exact expected parent remains current after a complete authoritative reread.
        | ProvenAbsent of Snapshot
        /// A different complete protected head won the race.
        | ForeignHead of Snapshot
        /// The reread or exact proposal correspondence could not be established.
        | Indeterminate of string

    /// Refuses an absent, moving, malformed, or unprotected genesis. A valid installed genesis still
    /// reports Unsupported because GitHub's public APIs do not satisfy both halves of the port contract.
    val assess: Read -> Result<Feasibility, string>

    /// Classifies a lost mutation response from a fresh complete protected-ref reread. The result never
    /// manufactures a successful CAS grant.
    val reconcileResponseUnknown: Proposal -> Read -> ResponseUnknownReconciliation
