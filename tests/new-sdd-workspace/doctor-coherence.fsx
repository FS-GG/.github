// Exercise the actual compiled Creator decision, not a copy of its JSON parser.
open NewSddWorkspace.Program
let coherent = """{"schemaVersion":1,"command":{"name":"doctor"},"outcome":"succeeded","coherent":true,"doctor":{"isCoherent":true}}"""
let drift = """{"schemaVersion":1,"command":{"name":"doctor"},"outcome":"succeededWithWarnings","coherent":false,"doctor":{"isCoherent":false}}"""
if doctorCoherence 0 coherent <> Ok true then failwith "explicit coherent verdict was lost"
if doctorCoherence 0 drift <> Ok false then failwith "exit-zero drift was reported coherent"
for malformed in [ ""; "not JSON"; "null"; "[]"; "{}"; "{\"coherent\":true}";
                   coherent.Replace("\"coherent\":true", "\"coherent\":false");
                   coherent.Replace("\"isCoherent\":true", "\"isCoherent\":false");
                   coherent.Replace("\"coherent\":true", "\"coherent\":\"true\"");
                   coherent.Replace("\"doctor\"}", "\"upgrade\"}");
                   coherent.Replace("\"schemaVersion\":1", "\"schemaVersion\":2");
                   coherent.Replace("\"succeeded\"", "\"failed\"");
                   coherent.Replace("\"coherent\":true", "\"coherent\":true,\"coherent\":false") ] do
    match doctorCoherence 0 malformed with
    | Error _ -> ()
    | _ -> failwithf "missing, malformed or contradictory doctor verdict accepted: %s" malformed
for code in [ 1; 2; 127 ] do
    match doctorCoherence code coherent with
    | Error _ -> ()
    | _ -> failwithf "failed doctor exit%d claimed coherence" code
printfn "Doctor JSON coherence controls passed: true, exit-zero false, malformed/absent/inconsistent and failed command."
