namespace FS.GG.Coord.Cli

/// THE KIT-DIGEST OBLIGATION'S IO EDGE (#469/#563/#588/#509) — the file/tree reads under the pure
/// comparisons in `Core.Kit`, extracted out of `Client` as the first Client.fs decomposition seam
/// (ADR-0047). Its whole surface is the two advisories `Client` composes into `claim` and the
/// verify/PR verbs; the digest, root-walk, and lock IO under them stay private behind this `.fsi`.
///
/// BOTH ARE ADVISORY AND NEITHER IS A GATE — `repos-registry-selftest` is the authority, and CI is
/// where the obligation is enforced. Each `warn` is silent when there is no kit root or no
/// `registry/repos.lock` to read, and on any read it could not make (#266): an advisory that spoke on a
/// failed read would be asserting a fact it never observed.
module KitDigest =

    open FS.GG.Coord
    open FS.GG.Coord.GitHub

    /// The kit root: `FSGG_KIT_ROOT` (the fixture's throwaway tree), else the git toplevel — FREE and
    /// offline, `None` outside a checkout. Exposed because `verify-paths` resolves the same root for its
    /// generated-paths subtraction; the two advisories below use it internally.
    val kitRoot: unit -> string option

    /// *Is the lock stale?* — OBSERVE the kit-digest obligation off the TREE and warn on stderr. Names any
    /// content-addressed kit source whose `registry/repos.lock` digest no longer matches the file, and any
    /// KIT skill whose two roots have diverged. Never inferred from what was declared (#563): a declaration
    /// cannot prove a digest matches, so it is never asked to. Silent with no root or no lock.
    val digestWarn: unit -> unit

    /// *Will this worker owe a relock?* — read the touch-set the worker just claimed off the issue body and
    /// name any content-addressed kit source it reserves, BEFORE anything is edited (#509). Purely
    /// additive to `digestWarn`: it can only ADD advice, never suppress it, so it cannot fail open the way
    /// its inference-based ancestor did (#563). Takes the transport directly — `ctx.Transport` at the call
    /// site — because this module is compiled before `Client` and that field is all it reads. Advisory;
    /// silent on a read it could not make.
    val declaredWarn: transport: Transport.IGitHubTransport -> ref: Types.Ref -> unit
