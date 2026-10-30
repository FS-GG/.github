/// The pinned coordination kit (FS-GG/.github#4964).
///
/// The legacy coordination wiring used to fetch the kit from the moving `main` of FS-GG/.github, so the
/// materialized skills, shim and tool manifest were not reproducible. The Wizard now names one reviewed
/// commit and sha256 anchors (`coordination-kit-pin.json`, embedded in the tool; recorded and checked
/// against git by `scripts/new-sdd-workspace-kit-pin.py`). The generated skill manifest is verified
/// against its anchor, and its per-file digests then pin every skill file transitively.
///
/// This module is pure: the caller supplies the byte fetcher, so every decision is testable without a
/// network. Two outcomes are distinct on purpose.
///  * A file that cannot be fetched (network, HTTP status, size) is `Unavailable`: the kit step warns and
///    the workspace stays usable, because the kit can be added later by `retrofit`.
///  * A file that is fetched but whose sha256 differs from the pin, a malformed manifest, or a manifest
///    path outside the closed layout is a refusal: nothing from the kit is written.
module NewSddWorkspace.CoordinationKit

/// The reviewed revision, the three anchors and the skills the Wizard embeds. An embedded skill is
/// authoritative in this tool: its pinned-manifest row is never fetched, vendored or reconciled from the kit
/// (the pin cannot name a post-merge commit that contains the embedded copy), and the Wizard installs and
/// reconciles it from its own resource instead.
type Pin =
    { Repository: string
      Revision: string
      EmbeddedSkills: string list
      ManifestSha256: string
      ShimSha256: string
      ToolManifestSha256: string }

/// One skill file the verified manifest lists.
type SkillFile =
    { Skill: string
      Path: string
      Executable: bool
      Content: string }

/// Verified kit bytes. Every member present was digest-checked; `Unavailable` names what could not
/// be fetched. `HasSkillManifest` is true only when the manifest itself was fetched and verified, so
/// callers may treat the skill file set as closed.
type Kit =
    { HasSkillManifest: bool
      /// Every (skill id, relative path) the verified manifest lists, fetched or not.
      Listed: (string * string) list
      Skills: SkillFile list
      Shim: string option
      ToolManifest: string option
      Unavailable: string list }

val sha256Hex: bytes: byte[] -> string

/// Parse the closed pin document. Unknown members, a short revision or a malformed digest are errors.
val parsePin: json: string -> Result<Pin, string>

/// The pin embedded in this tool.
val embeddedPin: unit -> Result<Pin, string>

/// The raw URL of a kit path at the pinned revision, under `rawBase`
/// (default `https://raw.githubusercontent.com`).
val rawUrl: rawBase: string -> pin: Pin -> path: string -> string

/// Fetch and verify the kit. Skills named in `pin.EmbeddedSkills` are skipped entirely (not fetched, not
/// listed). `fetch` takes a repository-relative path and returns its exact bytes.
/// `Error` carries the refusal reasons (digest mismatch, malformed or out-of-layout manifest).
val fetchVerified: pin: Pin -> fetch: (string -> Result<byte[], string>) -> Result<Kit, string list>

/// Accept the revision requested for the kit (`--ref` of `retrofit`) only when it is the pinned one.
val selectRevision: pin: Pin -> requested: string -> Result<string, string>
