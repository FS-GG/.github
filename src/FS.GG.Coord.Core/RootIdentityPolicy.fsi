namespace FS.GG.Coord

/// Shared Root principals, embedded from the same canonical policy consumed by Python.
/// This identifies principals only; it does not grant any operation or trust caller assertions.
module RootIdentityPolicy =
    type Policy = { RootLogins: Set<string>; Sha256: string }

    /// Parse canonical policy bytes for internal regression checks, never caller authorization.
    val internal parse: raw: byte array -> Result<Policy, string>

    /// Immutable embedded policy, or an explicit error when unavailable or malformed.
    val policy: Result<Policy, string>

    /// The principals from the embedded policy, retaining any policy error.
    val rootLogins: Result<Set<string>, string>

    /// SHA-256 of the exact embedded canonical policy bytes, retaining any policy error.
    val digest: Result<string, string>

    /// Apply only to a principal obtained from a protected native identity read.
    val isRoot: login: string -> Result<bool, string>
