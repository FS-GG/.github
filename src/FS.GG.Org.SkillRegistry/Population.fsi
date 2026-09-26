namespace FS.GG.Org.SkillRegistry

/// A catalog fact with its producer identity and declared source digest.
type RegistryRow = { Id: string; Owner: string; Source: string; Sha256: string }

/// A skill declaration from a producer manifest. SuppliedBy binds its source directory.
type ManifestEntry = { Id: string; SuppliedBy: string option; Sha256: string }

/// Whether a producer manifest was read, could not be read, or was absent.
type ManifestState = Parsed of ManifestEntry list | Unreadable of string | Absent

/// A reachable producer checkout; a rostered repository may have no manifest.
type Checkout = { Repo: string; Manifest: ManifestState }

/// Independently enumerated roster, checkouts, and catalog rows.
/// Keep an unreadable roster as Error so it cannot appear to be an empty roster.
type PopulationInput = { Rostered: Result<string list, string>; Checkouts: Checkout list; Rows: RegistryRow list }

/// A stable refusal naming the violated population or identity rule.
type PopulationFinding = { Code: string; Subject: string; Detail: string }

/// Pure closure check across the roster, producer manifests, and catalog rows.
module Population =
    /// Returns sorted, distinct findings. Refuses missing or unreadable inputs, unsafe
    /// identities and paths, ambiguous declarations, and mismatched owners or digests.
    /// Reads no files and never changes the registry.
    val inspect: input: PopulationInput -> PopulationFinding list
