namespace FS.GG.Org.Manifest

module DriverManifest =
    /// A validated schema-v2 driver-skill manifest. Its shape remains private until
    /// the SDD artifact producer contract and installed consumer are qualified.
    type Manifest

    /// Parse a driver manifest against its authoritative producer skill root.
    /// The root is a contained repository-relative path such as `.claude/skills`.
    val parse: expectedRoot: string -> json: string -> Result<Manifest, string list>

    /// Emit stable, path-sorted UTF-8 JSON with a final newline.
    val renderCanonical: manifest: Manifest -> byte array
