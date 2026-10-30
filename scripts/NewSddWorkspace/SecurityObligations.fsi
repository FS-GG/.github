/// The Wizard-owned store for security obligations and verified security receipts
/// (FS-GG/.github#4964, under the #3010 ruling).
///
/// `.fsgg/scaffold-provenance.json` is owned by the SDD CLI and validated by a strict schema: SDD CLI
/// 2.5.3 reports `scaffold.provenanceMalformed` ("Unknown property") for any key it does not define,
/// which made `fsgg-sdd doctor` incoherent for every default-coordination creation. The Wizard
/// therefore never writes into that file. Its security state lives in `.fsgg/security-obligations.json`
/// with the same row content the provenance key used to carry.
///
/// Everything here is local file work with no network and no process, so each decision is testable
/// against a temporary directory. Writes are whole-file replacements through a sibling temporary file.
module NewSddWorkspace.SecurityObligations

open System.Text.Json.Nodes

/// Workspace-relative path of the Wizard-owned file.
val relativePath: string

/// Value of the `schema` field the Wizard writes and requires.
val schemaId: string

/// The two keys the Wizard 0.23.0 wrote into the SDD-owned provenance (and nothing else).
val legacyProvenanceKeys: string list

/// Absolute path of the Wizard-owned file inside a workspace.
val filePath: target: string -> string

/// What `migrateLegacy` found and did.
type Migration =
    /// Provenance is absent, unreadable as an object, or carries none of the legacy keys.
    | NothingToMigrate
    /// Legacy rows were merged into the Wizard-owned file and the keys were removed from provenance.
    | Migrated of obligations: int * receipts: int
    /// A migration step could not complete; provenance and the Wizard-owned file stay consistent
    /// (each is written whole or not at all), so a later run retries and converges.
    | Incomplete of reason: string

/// Merge the legacy provenance keys into the Wizard-owned file, then remove exactly those keys from
/// the provenance, preserving every other member. Idempotent and crash-safe: the Wizard-owned file is
/// written first, so an interruption leaves rows in both places and the next run deduplicates. A row
/// already present in the Wizard-owned file (same kind and target) wins over its legacy copy. A
/// provenance file that is not a JSON object is left untouched.
val migrateLegacy: target: string -> Migration

/// Read the Wizard-owned state after a tolerant legacy migration. An absent file with nothing to
/// migrate is `Ok None`; a file that is not a JSON object of the expected schema is an `Error`.
val load: target: string -> Result<JsonObject option, string>

/// Replace the Wizard-owned file with `state`, stamping the schema. Never touches provenance.
val save: target: string -> state: JsonObject -> Result<unit, string>

/// An empty state document (schema stamp only) for first writes.
val emptyState: unit -> JsonObject
