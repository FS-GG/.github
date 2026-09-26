namespace FS.GG.Org.SkillRegistry

/// One organization catalog row after syntax parsing by a future adapter,
/// with its producer identity and declared source digest.
type RegistryRow = { Id: string; Owner: string; Source: string; Sha256: string }

/// One declared skill from a producer's already parsed manifest.
/// SuppliedBy binds its source directory.
type ManifestEntry = { Id: string; SuppliedBy: string option; Sha256: string }

/// Whether a manifest is present and parsed, unreadable, or absent at
/// the producer's known roots.
type ManifestState = Parsed of ManifestEntry list | Unreadable of string | Absent

/// One reachable producer checkout. A rostered repository may legitimately
/// have no skill manifest.
type Checkout = { Repo: string; Manifest: ManifestState }

/// Inputs whose roster and reachable population were independently enumerated
/// by the caller. A missing or unreadable roster must be carried as Error,
/// never converted to an empty set.
type PopulationInput = { Rostered: Result<string list, string>; Checkouts: Checkout list; Rows: RegistryRow list }

/// A deterministic refusal naming the violated population or identity rule.
/// No input is silently dropped from the population check.
type PopulationFinding = { Code: string; Subject: string; Detail: string }

/// Pure population, identity, and source-path closure across the roster,
/// producer manifests, and catalog rows. No filesystem or registry write occurs here.
module Population =
    /// Inspects one enumerated population and returns sorted, distinct findings
    /// for stable test and command output. Refuses missing or unreadable inputs, unsafe
    /// identities and paths, ambiguous declarations, and mismatched owners or digests.
    /// Reads no files and never changes the registry.
    val inspect: input: PopulationInput -> PopulationFinding list
