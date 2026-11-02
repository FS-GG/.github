namespace FS.GG.Coord.GitHub

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json.Nodes
open Transport

module V2TendFieldRecovery =
    let private project = "PVT_kwDOEYAWY84Bldpa"
    let private text (node: JsonNode) (name: string) = node[name].GetValue<string>()

    let internal reconstruct (record: JsonObject) =
        let item = text record "itemId"
        let key = SHA256.HashData(Encoding.UTF8.GetBytes(item.ToLowerInvariant())) |> Convert.ToHexString |> fun s -> s.Substring(0, 32).ToLowerInvariant()
        let fields = record["fields"].AsArray()
        if fields.Count <> 1 || text (fields[0]) "field" <> "Status" then invalidOp "not this incident field shape"
        let change = V2TendWrite.SetFieldValue(project, item, [ text (fields[0]) "fieldId", V2TendWrite.OptionValue(text (fields[0]) "value") ])
        let request = V2TendWrite.render ("tend-" + text record "run" + "-" + key) change |> List.head
        let legacy =
            match request.Body with
            | Query(document, variables) ->
                { request with Body = Query(document.Replace("$v0: String!", "$v0: ID!"),
                                             variables |> List.map (function "v0", VString s -> "v0", VId s | pair -> pair)) }
            | _ -> invalidOp "not GraphQL"
        // Must equal the independently authenticated installed-assembly reconstruction hash in the incident policy.
        canonicalMutationBytes legacy

    let internal readCurrent reads (record: JsonObject) =
        match V2TendWrite.readItem reads (text record "itemId") [] with
        | Error error -> Error error
        | Ok item ->
            let matches =
                item.ItemId = text record "itemId" && item.ProjectId = project && item.Ref = text record "ref" && not item.Archived && item.ContentId <> ""
                && (record["fields"].AsArray() |> Seq.forall (fun field ->
                    item.Named.TryFind(text field "field") = Some(text field "preimage")
                    && (item.Fields |> List.exists (fun schema ->
                        schema.Id = text field "fieldId" && schema.Name = text field "field" && schema.DataType = "SINGLE_SELECT"
                        && (schema.Options |> List.contains (text field "value", text field "target"))
                        && (match item.Values.TryFind(text field "fieldId") with
                            | Some raw ->
                                let value = JsonNode.Parse raw
                                text value "name" = text field "preimage"
                                && (schema.Options |> List.contains (text value "optionId", text field "preimage"))
                            | None -> false)))))
            if not matches then Error "current identity, field schema or preimage differs"
            else
                let current = JsonObject()
                current["projectId"] <- item.ProjectId
                current["itemId"] <- item.ItemId
                current["contentId"] <- item.ContentId
                current["ref"] <- item.Ref
                current["archived"] <- item.Archived
                current["fields"] <- record["fields"].DeepClone()
                let values = JsonObject()
                for field in record["fields"].AsArray() do
                    let id = text field "fieldId"
                    values[id] <- JsonNode.Parse(item.Values[id])
                current["fieldValues"] <- values
                Ok(current.ToJsonString())

    let admit reads syncDirectory input =
        let dependencies : V2TendNoEffect.Admission =
            { Acquire = fun () ->
                  V2WriterLock.production project V2WriterLock.migrationLegacyName
                  |> Result.defaultWith invalidOp |> V2WriterLock.acquire
              ReadCurrent = readCurrent reads
              ReadSchema = fun () ->
                  let request =
                      { Method = "POST"; Path = "graphql"; Query = []; Budget = GraphQl; IfNoneMatch = None; Subject = "tend option schema"
                        Body = Query("query { __type(name: \"ProjectV2FieldValue\") { name kind inputFields { name type { kind name } } } }", []) }
                  match reads.Send request with
                  | Ok response when response.Status = 200 -> Ok response.Body
                  | _ -> Error "provider schema unreadable"
              Reconstruct = reconstruct
              SyncDirectory = syncDirectory
              Clock = TimeProvider.System }
        V2TendNoEffect.admitWith V2TendNoEffect.policy dependencies input
