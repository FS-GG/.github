// allocation-input-v1.schema.json sha256:ee40eefafe234e9484be804bb9e7ec50a3cbbfdbb8353373935b43b46859f12b
// episode-input-v1.schema.json sha256:5d287174cf3ea3438a295212bcbff9f3a9c49581472b0ba89f4d89b28c6b4324
// assessment-input-v1.schema.json sha256:f0a60f750cb7b830939e7b8bf1c15424b36ab00b4c44cb1776a9da40aeb2c33f
// analysis-request-input-v1.schema.json sha256:d298747316bf30150c46614e97f849d67acb10d13d5189ff55db0a974f5b4cd5
// analysis-claim-input-v1.schema.json sha256:5b6071d1b0caab904d370b22132f3d9d8b519b91e99d7d71646d347b52068b4d
// analysis-attach-invocation-input-v1.schema.json sha256:d52a2ea875cd86e566783d52a1ff2e2e2758142a975520f955aee3601925774a
// analysis-settle-input-v1.schema.json sha256:bbfce06f9f23118ef566dd6c4b2819948d46e0aa6a65977f2b9145904c7c9527
namespace FS.GG.Coord

open System
open System.Globalization
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

module EfficiencyInput =
    type Record = private Record of JsonElement

    let private fail label reason = raise (FormatException(label + ": " + reason))
    let private require label condition reason = if not condition then fail label reason
    let private integer label (node: JsonElement) =
        match node.TryGetInt64() with
        | true, value when node.ValueKind = JsonValueKind.Number -> value
        | _ -> fail label "expected int64"
    let private objectFields label allowed required (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Object) "expected object"
        let names = node.EnumerateObject() |> Seq.map _.Name |> Seq.toList
        require label (names.Length = (List.distinct names).Length) "duplicate property"
        require label (names |> List.forall (fun name -> List.contains name allowed)) "unknown property"
        require label (required |> List.forall (fun name -> List.contains name names)) "missing property"
    let private codePointLength (text: string) =
        let mutable runes = text.EnumerateRunes()
        let mutable count = 0
        while runes.MoveNext() do count <- count + 1
        count
    let private timestamp label (text: string) =
        require label (Regex.IsMatch(text, "^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(?:\\.\\d+)?(?:Z|[+-]\\d{2}:\\d{2})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds 50.)) "expected RFC3339 timestamp"
        match DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None) with
        | true, _ -> ()
        | _ -> fail label "invalid timestamp"

    let private check0 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (text = "efficiency-resource-allocation/1") "unsupported version"

    let private check1 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (codePointLength text >= 1) "string bound exceeded"
        require label (codePointLength text <= 256) "string bound exceeded"

    let private check2 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Null) "expected null"

    let private check3 (label: string) (node: JsonElement) =
        let number = integer label node
        require label (number >= 0L) "integer bound exceeded"
        require label (number <= 9223372036854775807L) "integer bound exceeded"

    let private check4 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "runtime-turn-usage"; "usage"; "ci-run"; "ci-job"; "ci-step"; "activity-span"; "complication"; "process-review"; "native-item-outcome"; "expected-dispatch"; "invocation-lineage"; "runtime-admission"; "operational-activation"; "efficiency-resource-allocation/1"; "efficiency-problem-episode/1"; "efficiency-assessment/1"; "evidence"; "pull-request-head"; "source"; "runtime-start"; "runtime-terminal"; "runtime-gap"; "runtime-native-inventory/1"; "runtime-native-inventory-source/1"; "learn-installed-origin/1"; "runtime-provider-observation/1"; "runtime-response-usage/1" ])) "unsupported value"

    let private check5 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Regex.IsMatch(text, "^sha256:[a-f0-9]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds 50.)) "invalid identifier or digest"

    let private check6 (label: string) (node: JsonElement) =
        objectFields label [ "id"; "kind"; "revision"; "contentDigest" ] [ "id"; "kind"; "revision"; "contentDigest" ] node
        check1 (label + ".id") (node.GetProperty "id")
        check4 (label + ".kind") (node.GetProperty "kind")
        check3 (label + ".revision") (node.GetProperty "revision")
        check5 (label + ".contentDigest") (node.GetProperty "contentDigest")

    let private check7 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "tokens-input"; "tokens-output"; "tokens-total"; "tokens-cached-input"; "tokens-cache-write-input"; "tokens-reasoning"; "runner-seconds"; "observed-span-seconds"; "human-seconds"; "estimated-currency"; "billed-currency" ])) "unsupported value"

    let private check8 (label: string) (node: JsonElement) =
        objectFields label [ "sourceRef"; "dimension"; "provider"; "accountingScope"; "unit"; "amount" ] [ "sourceRef"; "dimension"; "provider"; "accountingScope"; "unit"; "amount" ] node
        check6 (label + ".sourceRef") (node.GetProperty "sourceRef")
        check1 (label + ".dimension") (node.GetProperty "dimension")
        check1 (label + ".provider") (node.GetProperty "provider")
        check1 (label + ".accountingScope") (node.GetProperty "accountingScope")
        check7 (label + ".unit") (node.GetProperty "unit")
        check3 (label + ".amount") (node.GetProperty "amount")

    let private check9 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "direct-product"; "useful-assurance"; "necessary-coordination"; "process-improvement"; "avoidable-process"; "unknown" ])) "unsupported value"

    let private check10 (label: string) (node: JsonElement) =
        let number = integer label node
        require label (number >= 1L) "integer bound exceeded"
        require label (number <= 9223372036854775807L) "integer bound exceeded"

    let private check11 (label: string) (node: JsonElement) =
        objectFields label [ "numerator"; "denominator" ] [ "numerator"; "denominator" ] node
        check3 (label + ".numerator") (node.GetProperty "numerator")
        check10 (label + ".denominator") (node.GetProperty "denominator")

    let private check12 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "observed"; "supported-inference"; "hypothesis"; "unknown" ])) "unsupported value"

    let private check13 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "required"; "avoidable"; "uncertain" ])) "unsupported value"

    let private check14 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Array) "expected array"
        require label (node.GetArrayLength() <= 16) "array bound exceeded"
        node.EnumerateArray() |> Seq.iteri (fun index child -> check6 (label + "[" + string index + "]") child)

    let private check15 (label: string) (node: JsonElement) =
        if node.ValueKind = JsonValueKind.Null then ()
        else
            require label (node.ValueKind = JsonValueKind.String) "expected string"
            let text = node.GetString()
            require label (codePointLength text <= 1024) "string bound exceeded"

    let private check16 (label: string) (node: JsonElement) =
        objectFields label [ "itemId"; "purpose"; "fraction"; "epistemicStatus"; "necessity"; "evidenceRefs"; "alternative" ] [ "itemId"; "purpose"; "fraction"; "epistemicStatus"; "necessity"; "evidenceRefs"; "alternative" ] node
        check1 (label + ".itemId") (node.GetProperty "itemId")
        check9 (label + ".purpose") (node.GetProperty "purpose")
        check11 (label + ".fraction") (node.GetProperty "fraction")
        check12 (label + ".epistemicStatus") (node.GetProperty "epistemicStatus")
        check13 (label + ".necessity") (node.GetProperty "necessity")
        check14 (label + ".evidenceRefs") (node.GetProperty "evidenceRefs")
        check15 (label + ".alternative") (node.GetProperty "alternative")

    let private check17 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Array) "expected array"
        require label (node.GetArrayLength() <= 64) "array bound exceeded"
        node.EnumerateArray() |> Seq.iteri (fun index child -> check16 (label + "[" + string index + "]") child)

    let private check18 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "complete"; "partial"; "unknown"; "not-applicable" ])) "unsupported value"

    let private check19 (label: string) (node: JsonElement) =
        objectFields label [ "allocation"; "evidence" ] [ "allocation"; "evidence" ] node
        check18 (label + ".allocation") (node.GetProperty "allocation")
        check18 (label + ".evidence") (node.GetProperty "evidence")

    let private check20 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "runtime-observer"; "ci-observer"; "root-reviewer"; "completion-analyst" ])) "unsupported value"

    let private check21 (label: string) (node: JsonElement) =
        if node.ValueKind = JsonValueKind.Null then ()
        else
            require label (node.ValueKind = JsonValueKind.String) "expected string"
            let text = node.GetString()
            require label (codePointLength text <= 256) "string bound exceeded"

    let private check22 (label: string) (node: JsonElement) =
        objectFields label [ "sourceIdentity"; "authorityRole"; "authorityRef"; "rootDispatchRef"; "invocationRef" ] [ "sourceIdentity"; "authorityRole"; "authorityRef"; "rootDispatchRef"; "invocationRef" ] node
        check1 (label + ".sourceIdentity") (node.GetProperty "sourceIdentity")
        check20 (label + ".authorityRole") (node.GetProperty "authorityRole")
        check6 (label + ".authorityRef") (node.GetProperty "authorityRef")
        check21 (label + ".rootDispatchRef") (node.GetProperty "rootDispatchRef")
        check21 (label + ".invocationRef") (node.GetProperty "invocationRef")

    let private check23 (label: string) (node: JsonElement) =
        if node.ValueKind = JsonValueKind.Null then ()
        else
            require label (node.ValueKind = JsonValueKind.String) "expected string"
            let text = node.GetString()
            timestamp label text

    let private check24 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        timestamp label text

    let private check25 (label: string) (node: JsonElement) =
        objectFields label [ "kind"; "identity"; "itemId"; "revision"; "resource"; "shares"; "coverage"; "provenance"; "occurredAt"; "observedAt" ] [ "kind"; "identity"; "itemId"; "revision"; "resource"; "shares"; "coverage"; "provenance"; "occurredAt"; "observedAt" ] node
        check0 (label + ".kind") (node.GetProperty "kind")
        check1 (label + ".identity") (node.GetProperty "identity")
        check2 (label + ".itemId") (node.GetProperty "itemId")
        check3 (label + ".revision") (node.GetProperty "revision")
        check8 (label + ".resource") (node.GetProperty "resource")
        check17 (label + ".shares") (node.GetProperty "shares")
        check19 (label + ".coverage") (node.GetProperty "coverage")
        check22 (label + ".provenance") (node.GetProperty "provenance")
        check23 (label + ".occurredAt") (node.GetProperty "occurredAt")
        check24 (label + ".observedAt") (node.GetProperty "observedAt")

    let private check26 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (text = "efficiency-problem-episode/1") "unsupported version"

    let private check27 (label: string) (node: JsonElement) =
        if node.ValueKind = JsonValueKind.Null then ()
        else
            let number = integer label node
            require label (number >= 1L) "integer bound exceeded"

    let private check28 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "attempt"; "native-item"; "provisional-delivery" ])) "unsupported value"

    let private check29 (label: string) (node: JsonElement) =
        objectFields label [ "outcomeRef"; "outcomeEpoch"; "attemptRef"; "activityRef"; "scope" ] [ "outcomeRef"; "outcomeEpoch"; "attemptRef"; "activityRef"; "scope" ] node
        check21 (label + ".outcomeRef") (node.GetProperty "outcomeRef")
        check27 (label + ".outcomeEpoch") (node.GetProperty "outcomeEpoch")
        check21 (label + ".attemptRef") (node.GetProperty "attemptRef")
        check21 (label + ".activityRef") (node.GetProperty "activityRef")
        check28 (label + ".scope") (node.GetProperty "scope")

    let private check30 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "planning"; "implementation"; "review"; "validation"; "delivery"; "repair"; "operations"; "coordination"; "context-recovery"; "other"; "unknown" ])) "unsupported value"

    let private check31 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "check-failure"; "retry"; "timeout"; "cancellation"; "refusal"; "handoff-return"; "reopen"; "data-conflict"; "missing-observation"; "other"; "unknown" ])) "unsupported value"

    let private check32 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "product-defect"; "requirements"; "dependency-drift"; "test-nondeterminism"; "infrastructure"; "authorization"; "capacity-custody"; "orchestration-handoff"; "context-loss"; "duplicate-process"; "bookkeeping-lineage"; "telemetry-loss"; "other"; "unknown" ])) "unsupported value"

    let private check33 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Array) "expected array"
        require label (node.GetArrayLength() <= 6) "array bound exceeded"
        node.EnumerateArray() |> Seq.iteri (fun index child -> check32 (label + "[" + string index + "]") child)

    let private check34 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "info"; "minor"; "major"; "critical"; "unknown" ])) "unsupported value"

    let private check35 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (codePointLength text >= 1) "string bound exceeded"
        require label (codePointLength text <= 1024) "string bound exceeded"

    let private check36 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Array) "expected array"
        require label (node.GetArrayLength() <= 16) "array bound exceeded"
        node.EnumerateArray() |> Seq.iteri (fun index child -> check1 (label + "[" + string index + "]") child)

    let private check37 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (codePointLength text <= 1024) "string bound exceeded"

    let private check38 (label: string) (node: JsonElement) =
        objectFields label [ "id"; "activity"; "purpose"; "trigger"; "primaryCause"; "contributingCauses"; "necessity"; "epistemicStatus"; "severity"; "summary"; "evidenceRefs"; "affectedObjects"; "alternative"; "recoveryRefs"; "uncertainty" ] [ "id"; "activity"; "purpose"; "trigger"; "primaryCause"; "contributingCauses"; "necessity"; "epistemicStatus"; "severity"; "summary"; "evidenceRefs"; "affectedObjects"; "alternative"; "recoveryRefs"; "uncertainty" ] node
        check1 (label + ".id") (node.GetProperty "id")
        check30 (label + ".activity") (node.GetProperty "activity")
        check9 (label + ".purpose") (node.GetProperty "purpose")
        check31 (label + ".trigger") (node.GetProperty "trigger")
        check32 (label + ".primaryCause") (node.GetProperty "primaryCause")
        check33 (label + ".contributingCauses") (node.GetProperty "contributingCauses")
        check13 (label + ".necessity") (node.GetProperty "necessity")
        check12 (label + ".epistemicStatus") (node.GetProperty "epistemicStatus")
        check34 (label + ".severity") (node.GetProperty "severity")
        check35 (label + ".summary") (node.GetProperty "summary")
        check36 (label + ".evidenceRefs") (node.GetProperty "evidenceRefs")
        check14 (label + ".affectedObjects") (node.GetProperty "affectedObjects")
        check15 (label + ".alternative") (node.GetProperty "alternative")
        check36 (label + ".recoveryRefs") (node.GetProperty "recoveryRefs")
        check37 (label + ".uncertainty") (node.GetProperty "uncertainty")

    let private check39 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Array) "expected array"
        require label (node.GetArrayLength() <= 32) "array bound exceeded"
        node.EnumerateArray() |> Seq.iteri (fun index child -> check6 (label + "[" + string index + "]") child)

    let private check40 (label: string) (node: JsonElement) =
        objectFields label [ "kind"; "identity"; "itemId"; "revision"; "subject"; "finding"; "evidenceRefs"; "allocationRefs"; "provenance"; "occurredAt"; "observedAt" ] [ "kind"; "identity"; "itemId"; "revision"; "subject"; "finding"; "evidenceRefs"; "allocationRefs"; "provenance"; "occurredAt"; "observedAt" ] node
        check26 (label + ".kind") (node.GetProperty "kind")
        check1 (label + ".identity") (node.GetProperty "identity")
        check1 (label + ".itemId") (node.GetProperty "itemId")
        check3 (label + ".revision") (node.GetProperty "revision")
        check29 (label + ".subject") (node.GetProperty "subject")
        check38 (label + ".finding") (node.GetProperty "finding")
        check39 (label + ".evidenceRefs") (node.GetProperty "evidenceRefs")
        check14 (label + ".allocationRefs") (node.GetProperty "allocationRefs")
        check22 (label + ".provenance") (node.GetProperty "provenance")
        check23 (label + ".occurredAt") (node.GetProperty "occurredAt")
        check24 (label + ".observedAt") (node.GetProperty "observedAt")

    let private check41 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (text = "efficiency-assessment/1") "unsupported version"

    let private check42 (label: string) (node: JsonElement) =
        let number = integer label node
        require label (number >= 1L) "integer bound exceeded"

    let private check43 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (text = "fsgg.telemetry.efficiency-assessment/1") "unsupported version"

    let private check44 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "native-item"; "provisional-delivery" ])) "unsupported value"

    let private check45 (label: string) (node: JsonElement) =
        objectFields label [ "itemId"; "outcomeId"; "outcomeEpoch"; "scope" ] [ "itemId"; "outcomeId"; "outcomeEpoch"; "scope" ] node
        check1 (label + ".itemId") (node.GetProperty "itemId")
        check1 (label + ".outcomeId") (node.GetProperty "outcomeId")
        check27 (label + ".outcomeEpoch") (node.GetProperty "outcomeEpoch")
        check44 (label + ".scope") (node.GetProperty "scope")

    let private check46 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "outcome"; "attempt"; "invocation"; "operation"; "pr"; "ci-run"; "ci-job"; "release"; "adoption"; "usage"; "activity"; "complication"; "process-review"; "correction"; "assessment" ])) "unsupported value"

    let private check47 (label: string) (node: JsonElement) =
        let number = integer label node
        require label (number >= 0L) "integer bound exceeded"

    let private check48 (label: string) (node: JsonElement) =
        objectFields label [ "id"; "kind"; "revision" ] [ "id"; "kind"; "revision" ] node
        check1 (label + ".id") (node.GetProperty "id")
        check46 (label + ".kind") (node.GetProperty "kind")
        check47 (label + ".revision") (node.GetProperty "revision")

    let private check49 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Array) "expected array"
        require label (node.GetArrayLength() <= 128) "array bound exceeded"
        node.EnumerateArray() |> Seq.iteri (fun index child -> check48 (label + "[" + string index + "]") child)

    let private check50 (label: string) (node: JsonElement) =
        objectFields label [ "population"; "usage"; "classification"; "lineage"; "dependency" ] [ "population"; "usage"; "classification"; "lineage"; "dependency" ] node
        check18 (label + ".population") (node.GetProperty "population")
        check18 (label + ".usage") (node.GetProperty "usage")
        check18 (label + ".classification") (node.GetProperty "classification")
        check18 (label + ".lineage") (node.GetProperty "lineage")
        check18 (label + ".dependency") (node.GetProperty "dependency")

    let private check51 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Array) "expected array"
        require label (node.GetArrayLength() <= 32) "array bound exceeded"
        node.EnumerateArray() |> Seq.iteri (fun index child -> check35 (label + "[" + string index + "]") child)

    let private check52 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Array) "expected array"
        require label (node.GetArrayLength() <= 8) "array bound exceeded"
        node.EnumerateArray() |> Seq.iteri (fun index child -> check35 (label + "[" + string index + "]") child)

    let private check53 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Array) "expected array"
        require label (node.GetArrayLength() <= 16) "array bound exceeded"
        node.EnumerateArray() |> Seq.iteri (fun index child -> check48 (label + "[" + string index + "]") child)

    let private check54 (label: string) (node: JsonElement) =
        objectFields label [ "id"; "activity"; "purpose"; "trigger"; "primaryCause"; "contributingCauses"; "necessity"; "epistemicStatus"; "severity"; "summary"; "evidenceRefs"; "affectedObjects"; "alternative"; "recoveryRefs"; "uncertainty" ] [ "id"; "activity"; "purpose"; "trigger"; "primaryCause"; "contributingCauses"; "necessity"; "epistemicStatus"; "severity"; "summary"; "evidenceRefs"; "affectedObjects"; "alternative"; "recoveryRefs"; "uncertainty" ] node
        check1 (label + ".id") (node.GetProperty "id")
        check30 (label + ".activity") (node.GetProperty "activity")
        check9 (label + ".purpose") (node.GetProperty "purpose")
        check31 (label + ".trigger") (node.GetProperty "trigger")
        check32 (label + ".primaryCause") (node.GetProperty "primaryCause")
        check33 (label + ".contributingCauses") (node.GetProperty "contributingCauses")
        check13 (label + ".necessity") (node.GetProperty "necessity")
        check12 (label + ".epistemicStatus") (node.GetProperty "epistemicStatus")
        check34 (label + ".severity") (node.GetProperty "severity")
        check35 (label + ".summary") (node.GetProperty "summary")
        check36 (label + ".evidenceRefs") (node.GetProperty "evidenceRefs")
        check53 (label + ".affectedObjects") (node.GetProperty "affectedObjects")
        check15 (label + ".alternative") (node.GetProperty "alternative")
        check36 (label + ".recoveryRefs") (node.GetProperty "recoveryRefs")
        check37 (label + ".uncertainty") (node.GetProperty "uncertainty")

    let private check55 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Array) "expected array"
        require label (node.GetArrayLength() <= 16) "array bound exceeded"
        node.EnumerateArray() |> Seq.iteri (fun index child -> check54 (label + "[" + string index + "]") child)

    let private check56 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Array) "expected array"
        require label (node.GetArrayLength() <= 32) "array bound exceeded"
        node.EnumerateArray() |> Seq.iteri (fun index child -> check1 (label + "[" + string index + "]") child)

    let private check57 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (text = "hypothesis") "unsupported version"

    let private check58 (label: string) (node: JsonElement) =
        objectFields label [ "ownerRole"; "mechanism"; "validationMethod"; "status" ] [ "ownerRole"; "mechanism"; "validationMethod"; "status" ] node
        check1 (label + ".ownerRole") (node.GetProperty "ownerRole")
        check35 (label + ".mechanism") (node.GetProperty "mechanism")
        check35 (label + ".validationMethod") (node.GetProperty "validationMethod")
        check57 (label + ".status") (node.GetProperty "status")

    let private check59 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Array) "expected array"
        require label (node.GetArrayLength() <= 3) "array bound exceeded"
        node.EnumerateArray() |> Seq.iteri (fun index child -> check58 (label + "[" + string index + "]") child)

    let private check60 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (text = "efficiency-rubric/1") "unsupported version"

    let private check61 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (text = "efficiency-taxonomy/1") "unsupported version"

    let private check62 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (text = "efficiency-analysis-policy/1") "unsupported version"

    let private check63 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "pending"; "accepted"; "rejected" ])) "unsupported value"

    let private check64 (label: string) (node: JsonElement) =
        objectFields label [ "producer"; "modelAlias"; "promptVersion"; "rubricVersion"; "taxonomyVersion"; "analysisPolicyVersion"; "usageRefs"; "startedAt"; "finishedAt"; "validationResult" ] [ "producer"; "modelAlias"; "promptVersion"; "rubricVersion"; "taxonomyVersion"; "analysisPolicyVersion"; "usageRefs"; "startedAt"; "finishedAt"; "validationResult" ] node
        check1 (label + ".producer") (node.GetProperty "producer")
        check1 (label + ".modelAlias") (node.GetProperty "modelAlias")
        check1 (label + ".promptVersion") (node.GetProperty "promptVersion")
        check60 (label + ".rubricVersion") (node.GetProperty "rubricVersion")
        check61 (label + ".taxonomyVersion") (node.GetProperty "taxonomyVersion")
        check62 (label + ".analysisPolicyVersion") (node.GetProperty "analysisPolicyVersion")
        check36 (label + ".usageRefs") (node.GetProperty "usageRefs")
        check23 (label + ".startedAt") (node.GetProperty "startedAt")
        check23 (label + ".finishedAt") (node.GetProperty "finishedAt")
        check63 (label + ".validationResult") (node.GetProperty "validationResult")

    let private check65 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "pending"; "running"; "partial"; "ready"; "failed"; "unavailable" ])) "unsupported value"

    let private check66 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Regex.IsMatch(text, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds 50.)) "invalid identifier or digest"

    let private check67 (label: string) (node: JsonElement) =
        objectFields label [ "state"; "idempotencyKey"; "failureReason"; "itemReviewRef"; "generatedAt" ] [ "state"; "idempotencyKey"; "failureReason"; "itemReviewRef"; "generatedAt" ] node
        check65 (label + ".state") (node.GetProperty "state")
        check66 (label + ".idempotencyKey") (node.GetProperty "idempotencyKey")
        check15 (label + ".failureReason") (node.GetProperty "failureReason")
        check21 (label + ".itemReviewRef") (node.GetProperty "itemReviewRef")
        check24 (label + ".generatedAt") (node.GetProperty "generatedAt")

    let private check68 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (text = "private") "unsupported version"

    let private check69 (label: string) (node: JsonElement) =
        objectFields label [ "visibility"; "policyVersion" ] [ "visibility"; "policyVersion" ] node
        check68 (label + ".visibility") (node.GetProperty "visibility")
        check21 (label + ".policyVersion") (node.GetProperty "policyVersion")

    let private check70 (label: string) (node: JsonElement) =
        objectFields label [ "schema"; "assessmentId"; "revision"; "supersedes"; "subject"; "evidenceDigest"; "evidenceRefs"; "coverage"; "omissions"; "outcomeSynopsis"; "wentWell"; "findings"; "metricRefs"; "improvements"; "provenance"; "lifecycle"; "publication" ] [ "schema"; "assessmentId"; "revision"; "supersedes"; "subject"; "evidenceDigest"; "evidenceRefs"; "coverage"; "omissions"; "outcomeSynopsis"; "wentWell"; "findings"; "metricRefs"; "improvements"; "provenance"; "lifecycle"; "publication" ] node
        check43 (label + ".schema") (node.GetProperty "schema")
        check1 (label + ".assessmentId") (node.GetProperty "assessmentId")
        check42 (label + ".revision") (node.GetProperty "revision")
        check21 (label + ".supersedes") (node.GetProperty "supersedes")
        check45 (label + ".subject") (node.GetProperty "subject")
        check5 (label + ".evidenceDigest") (node.GetProperty "evidenceDigest")
        check49 (label + ".evidenceRefs") (node.GetProperty "evidenceRefs")
        check50 (label + ".coverage") (node.GetProperty "coverage")
        check51 (label + ".omissions") (node.GetProperty "omissions")
        check35 (label + ".outcomeSynopsis") (node.GetProperty "outcomeSynopsis")
        check52 (label + ".wentWell") (node.GetProperty "wentWell")
        check55 (label + ".findings") (node.GetProperty "findings")
        check56 (label + ".metricRefs") (node.GetProperty "metricRefs")
        check59 (label + ".improvements") (node.GetProperty "improvements")
        check64 (label + ".provenance") (node.GetProperty "provenance")
        check67 (label + ".lifecycle") (node.GetProperty "lifecycle")
        check69 (label + ".publication") (node.GetProperty "publication")

    let private check71 (label: string) (node: JsonElement) =
        objectFields label [ "kind"; "identity"; "itemId"; "revision"; "assessment"; "provenance"; "observedAt" ] [ "kind"; "identity"; "itemId"; "revision"; "assessment"; "provenance"; "observedAt" ] node
        check41 (label + ".kind") (node.GetProperty "kind")
        check1 (label + ".identity") (node.GetProperty "identity")
        check1 (label + ".itemId") (node.GetProperty "itemId")
        check42 (label + ".revision") (node.GetProperty "revision")
        check70 (label + ".assessment") (node.GetProperty "assessment")
        check22 (label + ".provenance") (node.GetProperty "provenance")
        check24 (label + ".observedAt") (node.GetProperty "observedAt")

    let private check72 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (text = "fsgg.telemetry.efficiency-analysis-request-input/1") "unsupported version"

    let private check73 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (text = "efficiency-analysis-request/1") "unsupported version"

    let private check74 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Array) "expected array"
        require label (node.GetArrayLength() <= 128) "array bound exceeded"
        node.EnumerateArray() |> Seq.iteri (fun index child -> check6 (label + "[" + string index + "]") child)

    let private check75 (label: string) (node: JsonElement) =
        objectFields label [ "schema"; "kind"; "requestId"; "subject"; "evidenceDigest"; "analysisPolicyVersion"; "evidenceRefs"; "authority"; "nativeReviewRef"; "populationWitnessRefs"; "requestedAt"; "modelAlias"; "claimant"; "invocationRef" ] [ "schema"; "kind"; "requestId"; "subject"; "evidenceDigest"; "analysisPolicyVersion"; "evidenceRefs"; "authority"; "nativeReviewRef"; "populationWitnessRefs"; "requestedAt"; "modelAlias"; "claimant"; "invocationRef" ] node
        check72 (label + ".schema") (node.GetProperty "schema")
        check73 (label + ".kind") (node.GetProperty "kind")
        check66 (label + ".requestId") (node.GetProperty "requestId")
        check45 (label + ".subject") (node.GetProperty "subject")
        check5 (label + ".evidenceDigest") (node.GetProperty "evidenceDigest")
        check62 (label + ".analysisPolicyVersion") (node.GetProperty "analysisPolicyVersion")
        check74 (label + ".evidenceRefs") (node.GetProperty "evidenceRefs")
        check22 (label + ".authority") (node.GetProperty "authority")
        check21 (label + ".nativeReviewRef") (node.GetProperty "nativeReviewRef")
        check39 (label + ".populationWitnessRefs") (node.GetProperty "populationWitnessRefs")
        check24 (label + ".requestedAt") (node.GetProperty "requestedAt")
        check2 (label + ".modelAlias") (node.GetProperty "modelAlias")
        check2 (label + ".claimant") (node.GetProperty "claimant")
        check2 (label + ".invocationRef") (node.GetProperty "invocationRef")

    let private check76 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (text = "fsgg.telemetry.efficiency-analysis-claim-input/1") "unsupported version"

    let private check77 (label: string) (node: JsonElement) =
        objectFields label [ "requestId"; "expectedRevision"; "expectedContentDigest" ] [ "requestId"; "expectedRevision"; "expectedContentDigest" ] node
        check66 (label + ".requestId") (node.GetProperty "requestId")
        check47 (label + ".expectedRevision") (node.GetProperty "expectedRevision")
        check5 (label + ".expectedContentDigest") (node.GetProperty "expectedContentDigest")

    let private check78 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "enforced"; "unavailable"; "observed-only" ])) "unsupported value"

    let private check79 (label: string) (node: JsonElement) =
        objectFields label [ "inputTokens"; "outputTokens"; "seconds" ] [ "inputTokens"; "outputTokens"; "seconds" ] node
        check78 (label + ".inputTokens") (node.GetProperty "inputTokens")
        check78 (label + ".outputTokens") (node.GetProperty "outputTokens")
        check78 (label + ".seconds") (node.GetProperty "seconds")

    let private check80 (label: string) (node: JsonElement) =
        objectFields label [ "schema"; "cas"; "claimId"; "modelAlias"; "invocationRef"; "authority"; "claimedAt"; "dispatchRef"; "limitSupport" ] [ "schema"; "cas"; "claimId"; "modelAlias"; "invocationRef"; "authority"; "claimedAt"; "dispatchRef"; "limitSupport" ] node
        check76 (label + ".schema") (node.GetProperty "schema")
        check77 (label + ".cas") (node.GetProperty "cas")
        check1 (label + ".claimId") (node.GetProperty "claimId")
        check1 (label + ".modelAlias") (node.GetProperty "modelAlias")
        check21 (label + ".invocationRef") (node.GetProperty "invocationRef")
        check22 (label + ".authority") (node.GetProperty "authority")
        check24 (label + ".claimedAt") (node.GetProperty "claimedAt")
        check6 (label + ".dispatchRef") (node.GetProperty "dispatchRef")
        check79 (label + ".limitSupport") (node.GetProperty "limitSupport")

    let private check81 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (text = "fsgg.telemetry.efficiency-analysis-attach-invocation-input/1") "unsupported version"

    let private check82 (label: string) (node: JsonElement) =
        objectFields label [ "schema"; "cas"; "claimId"; "dispatchRef"; "invocationRef"; "lineageRefs"; "authority"; "attachedAt" ] [ "schema"; "cas"; "claimId"; "dispatchRef"; "invocationRef"; "lineageRefs"; "authority"; "attachedAt" ] node
        check81 (label + ".schema") (node.GetProperty "schema")
        check77 (label + ".cas") (node.GetProperty "cas")
        check1 (label + ".claimId") (node.GetProperty "claimId")
        check6 (label + ".dispatchRef") (node.GetProperty "dispatchRef")
        check1 (label + ".invocationRef") (node.GetProperty "invocationRef")
        check14 (label + ".lineageRefs") (node.GetProperty "lineageRefs")
        check22 (label + ".authority") (node.GetProperty "authority")
        check24 (label + ".attachedAt") (node.GetProperty "attachedAt")

    let private check83 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (text = "fsgg.telemetry.efficiency-analysis-settle-input/1") "unsupported version"

    let private check84 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "settled"; "failed"; "unavailable" ])) "unsupported value"

    let private check85 (label: string) (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.String) "expected string"
        let text = node.GetString()
        require label (Set.contains text (set [ "completed"; "failed"; "unknown"; "proven-no-effect" ])) "unsupported value"

    let private check86 (label: string) (node: JsonElement) =
        objectFields label [ "schema"; "cas"; "claimId"; "state"; "resultAssessmentRef"; "reason"; "usageRefs"; "invocationOutcome"; "reconciliationRefs"; "authority"; "settledAt"; "invocationRef" ] [ "schema"; "cas"; "claimId"; "state"; "resultAssessmentRef"; "reason"; "usageRefs"; "invocationOutcome"; "reconciliationRefs"; "authority"; "settledAt"; "invocationRef" ] node
        check83 (label + ".schema") (node.GetProperty "schema")
        check77 (label + ".cas") (node.GetProperty "cas")
        check1 (label + ".claimId") (node.GetProperty "claimId")
        check84 (label + ".state") (node.GetProperty "state")
        check21 (label + ".resultAssessmentRef") (node.GetProperty "resultAssessmentRef")
        check15 (label + ".reason") (node.GetProperty "reason")
        check39 (label + ".usageRefs") (node.GetProperty "usageRefs")
        check85 (label + ".invocationOutcome") (node.GetProperty "invocationOutcome")
        check39 (label + ".reconciliationRefs") (node.GetProperty "reconciliationRefs")
        check22 (label + ".authority") (node.GetProperty "authority")
        check24 (label + ".settledAt") (node.GetProperty "settledAt")
        check21 (label + ".invocationRef") (node.GetProperty "invocationRef")

    let private validate check node =
        try check "efficiency" node; Ok()
        with
        | :? FormatException as error -> Error error.Message
        | :? InvalidOperationException as error -> Error error.Message
        | :? RegexMatchTimeoutException -> Error "efficiency pattern deadline"

    let parseEvent (node: JsonElement) =
        if node.ValueKind <> JsonValueKind.Object then Error "efficiency event must be object"
        else
            match node.TryGetProperty "kind" with
            | true, kind when kind.ValueKind = JsonValueKind.String ->
                let check =
                    match kind.GetString() with
                    | "efficiency-resource-allocation/1" -> Some check25
                    | "efficiency-problem-episode/1" -> Some check40
                    | "efficiency-assessment/1" -> Some check71
                    | _ -> None
                match check with
                | Some check -> validate check node |> Result.map (fun () -> Record(node.Clone()))
                | None -> Error "unsupported efficiency event"
            | _ -> Error "missing efficiency kind"

    let validateCommand action node =
        match action with
        | "enqueue" -> validate check75 node
        | "claim" -> validate check80 node
        | "attach-invocation" -> validate check82 node
        | "settle" -> validate check86 node
        | _ -> Error "unsupported efficiency action"

    let validateReference node = validate check6 node

    let body (Record node) = node
