namespace FS.GG.Coord.GitHub

/// Fixed-query read acquisition for one complete initial or final inspection phase.
/// This internal seam supplies no caller-selected acquisition mode or effect route.
module internal V2InspectionAcquisition =
    open Transport

    /// Evaluate a read-only projection after resolving all its discovered requests.
    /// Exact requests share only this phase's bounded native responses; a new call
    /// reacquires every determining read, including original failures and final barriers.
    /// Batches contain at most ten compatible fixed GraphQL requests. Variables and root
    /// aliases retain exact identities; unknown/partial/malformed replies remain failures.
    /// Unsupported query forms use independent native reads. Pagination is still drained
    /// and checked by the original decoders, rather than inferred from batch success.
    /// The evaluator recreates mutable counters and policy gates on every discovery round;
    /// it must perform no side effects or retain a provisional authority decision.
    /// Mutation and durable retry are unavailable. Sixty-four discovery rounds bound
    /// acquisition; missing reads at that bound remain explicit failures.
    val run: transport: IGitHubTransport -> evaluate: (IGitHubTransport -> 'value) -> 'value
