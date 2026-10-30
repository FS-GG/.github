/// The Wizard-owned log of coordination retrofits (FS-GG/.github#4964).
///
/// Until 0.23.0 `retrofit` appended a `retrofits` member to the SDD-owned
/// `.fsgg/scaffold-provenance.json`. SDD CLI 2.5.3's strict provenance schema rejects unknown members,
/// so the log now lives in `.fsgg/coordination-retrofits.json`. A legacy `retrofits` member is moved
/// the next time the Wizard runs `retrofit` on the workspace.
module NewSddWorkspace.RetrofitLog

open System.Text.Json.Nodes

/// Workspace-relative path of the Wizard-owned log.
val relativePath: string

/// Value of the `schema` field the Wizard writes and requires.
val schemaId: string

/// The member the Wizard 0.23.0 and earlier wrote into the SDD-owned provenance.
val legacyProvenanceKey: string

/// Move a legacy `retrofits` member out of provenance into the Wizard-owned log, preserving every
/// other provenance member. Returns the number of entries moved (0 when there was nothing to move).
/// The log is durable before provenance changes; a repeat after an interruption deduplicates by
/// the entry's exact JSON.
val migrateLegacy: target: string -> Result<int, string>

/// Append one retrofit entry (after a tolerant legacy migration). Provenance is never created or
/// written except to remove the legacy member.
val append: target: string -> entry: JsonObject -> Result<unit, string>

/// Current entries (after a tolerant legacy migration); empty when no log exists.
val entries: target: string -> Result<JsonObject list, string>
