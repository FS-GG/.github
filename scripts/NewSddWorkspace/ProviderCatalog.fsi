/// The consumer side of the ref-pinned, schema-2 provider catalog (FS-GG/.github#3010, ADR-0092).
///
/// Everything here is pure: parsing, typed-parameter validation, platform and archive selection,
/// preview rows and the exact `fsgg-sdd catalog` argument lists. Fetching, hashing files on disk and
/// running processes stay in `Program.fs`, so each decision can be tested without a network or a CLI.
/// No provider, language or parameter name appears in this module: a provider that the catalog
/// declares validly is selectable, prompted, previewed and forwarded without a source edit.
/// The authoritative catalog, policy, platform and tool checks remain `fsgg-sdd`'s (SDD#928); this
/// reader refuses early, before any target exists, and never widens what SDD would accept.
module NewSddWorkspace.ProviderCatalog

/// The catalog schema and descriptor protocol this consumer understands.
val supportedSchemaVersion: int
val supportedContractVersion: string

/// The ref-relative path of the catalog inside FS.GG.Templates.
val catalogRelativePath: string

/// Closed set of parameter kinds the catalog may declare.
type ParameterKind =
    | Text
    | Choice
    | ExactVersion

type Validation =
    { NonEmpty: bool
      MinLength: int
      MaxLength: int
      AllowedValues: string list }

type Parameter =
    { Key: string
      Kind: ParameterKind
      Required: bool
      Prompt: string
      Help: string
      Default: string option
      Values: string list
      Validation: Validation }

/// Which declared parameter carries each product identity. The roles are the descriptor's own.
type Identities =
    { RawName: string
      PackageIdentity: string
      CodeIdentifier: string }

type Tool =
    { Id: string
      Version: string
      Platforms: string list }

type Provider =
    { Id: string
      DisplayName: string
      Help: string
      Language: string
      ProductShape: string
      ContractVersion: string
      TemplateSource: string
      TemplateId: string
      Platforms: string list
      Parameters: Parameter list
      Identities: Identities
      Tools: Tool list
      CapabilityIds: string list
      Skills: string list }

type Catalog =
    { Id: string
      Revision: string
      /// The catalog's own semantic digest declaration (`sha256:` + 64 hex); SDD re-verifies it.
      Digest: string
      Providers: Provider list }

/// A parsed catalog together with the exact bytes and where they came from.
type Loaded =
    { Catalog: Catalog
      Bytes: byte[]
      /// `sha256:` + 64 lowercase hex of the exact catalog bytes (the raw digest SDD verifies).
      RawDigest: string
      Source: string }

/// The catalog-route inputs a caller can supply. All are optional at parse time.
type Inputs =
    { CatalogFile: string option
      CatalogSha256: string option
      TemplateArchive: string option
      TemplateSha256: string option
      AdmissionPolicy: string option
      AdmissionPolicySha256: string option
      Params: (string * string) list
      DryRun: bool }

val emptyInputs: Inputs

/// `sha256:` + lowercase hex of the bytes.
val sha256Digest: bytes: byte[] -> string

/// Accept `sha256:<64 hex>` or bare 64 hex, in either case, and return the canonical lowercase form.
val normalizeDigest: value: string -> Result<string, string>

/// A closed lexical version literal: no range, floating selector or wildcard (SDD's own rule).
val isExactVersion: value: string -> bool

/// Parse and validate a catalog. The whole catalog is refused on any malformed, unknown,
/// duplicate or unsupported entry; there is no partial listing. Bounded to 1 MiB and strict UTF-8.
val parse: bytes: byte[] -> Result<Catalog, string>

/// Providers a caller may select, excluding ids a built-in template already owns (built-ins win, so
/// existing command-line names keep their meaning). The second list names the shadowed ids.
val selectable: builtIn: string list -> catalog: Catalog -> Provider list * string list

val tryFind: id: string -> providers: Provider list -> Provider option

/// Distinct languages, in catalog order.
val languages: providers: Provider list -> string list

/// Providers of one language (case-sensitive, as declared), in catalog order.
val shapesOf: language: string -> providers: Provider list -> Provider list

/// Why one value is unacceptable for one parameter, or None. Mirrors the declared validation.
val validateValue: parameter: Parameter -> value: string -> string option

/// The parameter bound to the raw product name role, if the descriptor declares it.
val rawNameKey: provider: Provider -> string

/// Resolve the caller's supplied values for one provider.
/// - an unknown, duplicate or invalid key refuses; so does a missing required value;
/// - the parameter bound to the raw-name role defaults to the product name, nothing else is derived;
/// - the result lists only the explicit values to forward (supplied plus that one default), in
///   declared order; SDD applies the remaining declared defaults itself.
val resolveParameters:
    provider: Provider -> product: string -> supplied: (string * string) list -> Result<(string * string) list, string list>

/// `FS.GG.Workspace.Template::0.20.0` -> package id and exact version.
val parseTemplateSource: source: string -> Result<string * string, string>

/// The NuGet flat-container URL of an exact package.
val flatContainerUrl: baseUrl: string -> packageId: string -> version: string -> string

/// `linux-x64`, `osx-arm64`, ... from the OS family and architecture names; None when unknown.
val platformId: os: string -> architecture: string -> string option

/// The provider's refusal when the host platform is not one of its declared platforms.
val checkPlatform: provider: Provider -> platform: string -> Result<unit, string>

/// The preflight and scaffold budgets, in seconds, that an admission policy declares, after checking that it
/// also declares an archive association for this provider's template source.
val policyBudgets: policyBytes: byte[] -> provider: Provider -> Result<int * int, string>

/// Label/value rows describing a selection, built only from the catalog and the resolved values.
val previewRows: provider: Provider -> effective: (string * string) list -> (string * string) list

/// One-line description of a provider for menus and help.
val describe: provider: Provider -> string

/// Everything `fsgg-sdd catalog inspect|scaffold` is given for one creation.
type Invocation =
    { CatalogPath: string
      CatalogDigest: string
      ProviderId: string
      TargetRoot: string
      ArchivePath: string
      ArchiveDigest: string
      PolicyPath: string
      PolicyDigest: string
      Platform: string
      TransportExecutable: string
      PreflightSeconds: int
      ScaffoldSeconds: int
      Parameters: (string * string) list
      DryRun: bool }

/// `catalog inspect` arguments: read-only, selects the provider and resolves its parameters.
val inspectArguments: invocation: Invocation -> string list

/// `scaffold` arguments for the catalog route. Parameters are forwarded as literal `--param key=value`.
val scaffoldArguments: invocation: Invocation -> string list
