namespace FS.GG.Coord

open System
open System.Security.Cryptography
open System.Text

module Operation =

    type Op =
        | Merge
        | Dispatch of eventType: string
        | Publish of package: string

    type Part =
        | Item
        | Generation
        | Receiver
        | OperationPayload

    type Refusal =
        | Blank of part: Part
        | ControlCharacter of part: Part
        | ItemNotFullyQualified of item: string
        | ReceiverNotFullyQualified of receiver: string
        | GenerationNotServerAssigned of generation: string

    type OpKey =
        | OpKey of string

        member this.Value =
            let (OpKey value) = this
            value

    /// SHA-256 to lowercase hex. PRIVATE, and the eighth per-module copy of this primitive inside
    /// `FS.GG.Coord.Core` rather than a shared one — a deliberate, recorded decision, not an oversight.
    ///
    /// §3.3 of the design says "Reuse `Delivery.digest` rather than writing a second one", and that is
    /// not reachable: `Delivery.digest` is `let private` and appears nowhere in `Delivery.fsi`, so
    /// calling it means editing `Delivery.fs`/`Delivery.fsi` — files outside this slice's declared
    /// touch-set, and inside the engine's delivery decision path, which a pure first-in-order slice
    /// must not disturb. The primitive is already unshared seven ways here (`grep -rn 'SHA256.HashData'
    /// src/FS.GG.Coord.Core/` returns ten call sites across `IntakeReceipt`, `StructuredDecision`,
    /// `SemanticDiff`, `Driver`, `Delivery`, `Review` and `CycleLedger`, each module keeping its own
    /// private helper), so this slice cannot avoid being the eighth without a consolidation whose
    /// subject is those seven modules. That consolidation is recorded and routed, not dropped
    /// (`work/2311-operation-key-core/clarifications.md` DEC-001, DEC-007).
    ///
    /// What §3.3 is actually protecting — the COMPOSITION RULE for the operation key — does have
    /// exactly one home, and it is `preimage` below.
    let private hex (value: string) =
        value
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> fun digest -> digest.ToLowerInvariant()

    /// The pre-image field separator. Named once so that the refusal below and the rendering agree by
    /// construction: `separatorFree` is what keeps this character out of the domain, and
    /// `String.concat` is injective over 4-tuples exactly because of it.
    [<Literal>]
    let private Separator = "\n"

    let private isBlank (value: string) = String.IsNullOrWhiteSpace value

    /// Any control character, not merely the separator. Line feed and carriage return are the two that
    /// could forge an ambiguous pre-image; the rest are refused alongside them because a component
    /// carrying a tab, a NUL, or any other control character is a caller mistake in every case this key
    /// is composed for, and admitting them would leave the domain's boundary at "the two characters we
    /// happened to think of" rather than at a rule.
    ///
    /// This is deliberately DEFENCE IN DEPTH for three of the four components: `item`, `generation` and
    /// `receiver` each exclude a newline again through their own format rule below, so removing this
    /// check would not by itself admit an ambiguous pre-image for them — it would only change which
    /// refusal they get. The operation payload has no second rule, and `op` renders last, so a newline
    /// there cannot forge a different four-field pre-image today but would the moment a fifth field is
    /// appended. Stating the guarantee over the DOMAIN rather than over the current field order is what
    /// keeps that future change from being a silent one.
    let private separatorFree (value: string) = value |> Seq.exists Char.IsControl |> not

    /// A canonical, server-assigned GitHub id: a non-empty run of ASCII digits whose first digit is not
    /// zero. The leading-zero clause is not pedantry — `0012` and `12` are the same generation and would
    /// compose two different keys, which is exactly the "one thing keyed two ways" failure an
    /// idempotence key exists to prevent. It also excludes `0` itself, which is neither a comment id nor
    /// an issue number.
    let private serverAssignedId (value: string) =
        value.Length > 0
        && value[0] <> '0'
        && value |> Seq.forall Char.IsAsciiDigit

    /// `owner/repo`: exactly one `/`, both halves non-empty, no whitespace, and no `#` — the `#` clause
    /// is what stops `owner/repo#12` being accepted as a receiver.
    let private qualifiedRepo (value: string) =
        match value.Split('/') with
        | [| owner; repo |] ->
            owner.Length > 0
            && repo.Length > 0
            && [ owner; repo ]
               |> List.forall (fun half -> half |> Seq.exists (fun c -> Char.IsWhiteSpace c || c = '#') |> not)
        | _ -> false

    /// `owner/repo#N`: a qualified repo, exactly one `#`, and a canonical issue number. The board's
    /// `<repo>#N` shorthand fails the qualified-repo half, which is the point (`.github#2107`).
    let private qualifiedItem (value: string) =
        match value.Split('#') with
        | [| repo; number |] -> qualifiedRepo repo && serverAssignedId number
        | _ -> false

    /// The blank/control-character pair every component gets, in that order: a blank component has
    /// nothing to inspect for control characters, so reporting both would be one real problem stated
    /// twice.
    let private wellFormed part (value: string) =
        if isBlank value then [ Blank part ]
        elif not (separatorFree value) then [ ControlCharacter part ]
        else []

    let wire op =
        match op with
        | Merge -> "merge"
        | Dispatch eventType -> "dispatch:" + eventType
        | Publish package -> "publish:" + package

    /// The payload half of an operation's validation. `Merge` carries no payload, so it has nothing to
    /// refuse; the other two are ordinary components under `OperationPayload`.
    let private operationRefusals op =
        match op with
        | Merge -> []
        | Dispatch eventType -> wellFormed OperationPayload eventType
        | Publish package -> wellFormed OperationPayload package

    let preimage item generation receiver op =
        let itemRefusals =
            match wellFormed Item item with
            | [] -> if qualifiedItem item then [] else [ ItemNotFullyQualified item ]
            | refusals -> refusals

        let generationRefusals =
            match wellFormed Generation generation with
            | [] -> if serverAssignedId generation then [] else [ GenerationNotServerAssigned generation ]
            | refusals -> refusals

        let receiverRefusals =
            match wellFormed Receiver receiver with
            | [] -> if qualifiedRepo receiver then [] else [ ReceiverNotFullyQualified receiver ]
            | refusals -> refusals

        match itemRefusals @ generationRefusals @ receiverRefusals @ operationRefusals op with
        | [] -> Ok(String.concat Separator [ item; generation; receiver; wire op ])
        | refusals -> Error refusals

    let compose item generation receiver op =
        preimage item generation receiver op |> Result.map (hex >> OpKey)

    let private partName part =
        match part with
        | Item -> "item"
        | Generation -> "generation"
        | Receiver -> "receiver"
        | OperationPayload -> "operation payload"

    let describe refusal =
        match refusal with
        | Blank part -> $"the %s{partName part} component is empty"
        | ControlCharacter part ->
            $"the %s{partName part} component carries a control character, which the operation key's pre-image domain excludes"
        | ItemNotFullyQualified item ->
            $"the item '%s{item}' is not spelled owner/repo#N — the board's <repo>#N shorthand is not GitHub grammar"
        | ReceiverNotFullyQualified receiver -> $"the receiver '%s{receiver}' is not spelled owner/repo"
        | GenerationNotServerAssigned generation ->
            $"the generation '%s{generation}' is not a server-assigned claim-marker comment id"
