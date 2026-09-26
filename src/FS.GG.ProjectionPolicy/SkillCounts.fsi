namespace FS.GG.ProjectionPolicy

/// One parsed catalog row used for the generated scope and owner counts.
type SkillRow = { Id: string; Scope: string; Owner: string }

/// Pure renderer for the skill count projection body.
module SkillCounts =
    /// Produces the pure dynamic body for generate-projections'
    /// skill-registry-counts region. Returns deterministic Markdown counts
    /// for a nonempty catalog with unique,
    /// safe IDs and owners and supported scopes; otherwise returns an error.
    /// Parsing, catalog completeness, and publication remain with their owners.
    val render: rows: SkillRow list -> Result<string, string>
