module RecipeControls

open System
open System.Text
open System.Text.Json
open System.IO
open System.Diagnostics
open FSGG.Telemetry.PersistentV3.ImageClosure

let private require condition reason=if not condition then failwith reason
let private refused action=
    let mutable rejected=false
    try action() with :? InvalidDataException | :? IOException | :? JsonException -> rejected<-true
    require rejected "recipe accepted changed input/command bytes"

let private cliControls (selection:Selection) (trusted:TrustedNativeSelection) (program:CommandProgram)=
    let root=Directory.CreateTempSubdirectory("recipe-cli-").FullName
    try
        let write name (bytes:byte array)=let path=Path.Combine(root,name) in File.WriteAllBytes(path,bytes);path
        let selectedPath=write "selection.json" (JsonSerializer.SerializeToUtf8Bytes selection)
        let trustedPath=write "trusted.json" (JsonSerializer.SerializeToUtf8Bytes trusted)
        let recipePath=Path.Combine(root,"recipe.json")
        let entry=Reflection.Assembly.GetEntryAssembly().Location
        let invoke arguments=
            let start=ProcessStartInfo(Environment.ProcessPath,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true)
            for value in [|"exec";"--runtimeconfig";Path.ChangeExtension(entry,"runtimeconfig.json");"--depsfile";Path.ChangeExtension(entry,"deps.json");typeof<CommandProgram>.Assembly.Location|] do start.ArgumentList.Add value
            for value in arguments do start.ArgumentList.Add value
            use child=Process.Start start
            let output=child.StandardOutput.ReadToEndAsync()
            let error=child.StandardError.ReadToEndAsync()
            if not(child.WaitForExit(10000)) then
                child.Kill(true)
                child.WaitForExit()
                failwith "recipe CLI exceeded source-control deadline"
            child.ExitCode,output.GetAwaiter().GetResult(),error.GetAwaiter().GetResult()
        let args=[|"prepare-command-recipe";"--selection";selectedPath;"--trusted-native-selection";trustedPath;"--recipe-selection";recipePath|]
        let run (raw:byte array) expected=
            File.WriteAllBytes(recipePath,raw)
            let code,output,error=invoke args
            require(code=expected) (sprintf "recipe CLI exit %d expected %d: %s" code expected error)
            if expected=0 then require(Encoding.UTF8.GetBytes output=CommandRecipe.bytes program&&error="") "recipe CLI differs from maintained producer bytes"
            else require(output=""&&error.StartsWith("persistent-v3-image-closure-refused: ")) "recipe CLI leaked output or omitted refusal"
        run (JsonSerializer.SerializeToUtf8Bytes program.Selection) 0
        for changed in [
            {program.Selection with Schema="foreign"}
            {program.Selection with IdentityClass="production"}
            {program.Selection with SelectionSha256=String('f',64)}
            {program.Selection with TrustedSha256=String('f',64)}
            {program.Selection with PreparedSha256=String('f',64)}
            {program.Selection with CustodyIdentity="invalid"}
            {program.Selection with Target="execute"}
            {program.Selection with ParentOci="relative"}
            {program.Selection with WorkRoot=program.Selection.EvidenceRoot}
            {program.Selection with NonceA=program.Selection.NonceB}
            {program.Selection with NonceB="invalid"}
            {program.Selection with CleanupRemovalLimit=33}] do run (JsonSerializer.SerializeToUtf8Bytes changed) 3
        let raw=JsonSerializer.Serialize program.Selection
        for text in [raw.Replace("\"Schema\":","\"extra\":1,\"Schema\":");raw.Replace("\"Schema\":","\"Target\":\"run-c4\",\"Schema\":");raw.Replace("\"Target\":\"run-inactive\"","\"Target\":null").Replace("\"Target\":\"run-c4\"","\"Target\":null");"{"] do run (Encoding.UTF8.GetBytes text) 3
        let code,output,_=invoke [|"prepare-command-recipe";"--selection";selectedPath;"--selection";selectedPath;"--recipe-selection";recipePath|]
        require(code=3&&output="") "recipe CLI accepted duplicate flags"
        let placeholder=write "placeholder.json" (File.ReadAllBytes(Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__,"../../deployment/telemetry-collector/persistent/v3/image-closure/production-selection.json"))))
        let code,output,error=invoke [|"prepare-command-recipe";"--selection";placeholder;"--trusted-native-selection";Path.Combine(root,"absent-trusted");"--recipe-selection";Path.Combine(root,"absent-recipe")|]
        require(code=2&&output=""&&error.Contains("native-image-closure-inventory-acquisition-required")) "recipe CLI bypassed acquisition-required selection"
        require(not(Directory.Exists program.Selection.WorkRoot)&&not(Directory.Exists program.Selection.EvidenceRoot)) "recipe preparation created execution roots"
        printfn "PASS real prepare-command-recipe CLI: exact producer bytes, closed inputs, stale joins, custody/target/path/nonce/bounds refusals and unavailable production placeholder"
    finally Directory.Delete(root,true)

let run (selection:Selection) (trusted:TrustedNativeSelection) (prepared:byte array)=
    let custody=String('c',64)
    let make target=CommandRecipe.prepare selection trusted prepared custody target "/recipe/parent.oci.tar" "/recipe/work" "/recipe/evidence" (String('a',32)) (String('b',32)) 32
    let c4=make "run-c4"
    let full=make "run-inactive"
    let validate target program=CommandRecipe.validate selection trusted prepared custody target "/recipe/parent.oci.tar" "/recipe/work" "/recipe/evidence" program
    validate "run-c4" c4
    validate "run-inactive" full
    require(c4.NormalCommands=20&&full.NormalCommands=26&&c4.CleanupCommands=12&&full.CleanupCommands=12&&c4.MaximumCommands=64&&full.MaximumCommands=70) "actual finite producer count drift"
    let raw=CommandRecipe.bytes full
    require(CommandRecipe.bytes(CommandRecipe.parse raw)=raw) "canonical recipe roundtrip differs"
    for text in [Encoding.UTF8.GetString raw+" ";Encoding.UTF8.GetString(raw).Replace("\"MaximumCommands\":70","\"MaximumCommands\":7e1");Encoding.UTF8.GetString(raw).Replace("\"MaximumCommands\":70","\"MaximumCommands\":70,\"MaximumCommands\":70");Encoding.UTF8.GetString(raw).Replace("\"Target\":\"run-inactive\"","\"Target\":null");Encoding.UTF8.GetString(raw).Replace("\"Target\":\"run-inactive\"","\"Target\":\"run-inactive\",\"unknown\":1")] do
        refused(fun()->CommandRecipe.parse(Encoding.UTF8.GetBytes text)|>ignore)
    let replace command=
        let rows=Array.copy c4.Steps
        rows[0]<-{rows[0] with Command=command}
        {c4 with Steps=rows}
    let original=c4.Steps[0].Command
    for changed in [
        {original with Executable="/bin/sh"}
        {original with Arguments=Array.append original.Arguments [|"--remote=true"|]}
        {original with Environment=original.Environment.Add("CONTAINER_HOST","foreign")}
        {original with WorkingDirectory="/recipe/foreign"}
        {original with OutputLimit=original.OutputLimit+1}] do refused(fun()->validate "run-c4" (replace changed))
    refused(fun()->validate "run-c4" {c4 with Selection={c4.Selection with NonceA=String('d',32)}})
    refused(fun()->validate "run-c4" {c4 with Selection={c4.Selection with Target="run-inactive"}})
    refused(fun()->validate "run-inactive" c4)
    refused(fun()->CommandRecipe.validate selection trusted prepared (String('d',64)) "run-c4" "/recipe/parent.oci.tar" "/recipe/work" "/recipe/evidence" c4)
    refused(fun()->CommandRecipe.validate selection trusted prepared custody "run-c4" "/recipe/other.oci.tar" "/recipe/work" "/recipe/evidence" c4)
    refused(fun()->CommandRecipe.validate selection trusted (Array.append prepared [|0uy|]) custody "run-c4" "/recipe/parent.oci.tar" "/recipe/work" "/recipe/evidence" c4)
    refused(fun()->CommandRecipe.validate {selection with HostSourceRevision=String('f',40)} trusted prepared custody "run-c4" "/recipe/parent.oci.tar" "/recipe/work" "/recipe/evidence" c4)
    refused(fun()->CommandRecipe.validate selection {trusted with PythonExecutable="/foreign/python"} prepared custody "run-c4" "/recipe/parent.oci.tar" "/recipe/work" "/recipe/evidence" c4)
    for limit in [-1;33] do refused(fun()->CommandRecipe.prepare selection trusted prepared custody "run-c4" "/recipe/parent.oci.tar" "/recipe/work" "/recipe/evidence" (String('a',32)) (String('b',32)) limit|>ignore)
    let returned=CommandRecipe.step c4 "a" "parent-load"
    returned.Arguments[0]<-"mutated-consumer-copy"
    validate "run-c4" c4
    require(CommandRecipe.identity c4<>CommandRecipe.identity full) "target not bound into recipe identity"
    printfn "PASS actual command producer/parser: C4/full counts, canonical closed bytes, argv/env/cwd/nonce/target/input joins, cleanup ceilings and copied command arrays"

// The linked-source composition executable has its own entrypoint. Only the
// real CLI test executable may select and launch the producer assembly here.
let runCli (selection:Selection) (trusted:TrustedNativeSelection) prepared=
    for target in ["run-c4";"run-inactive"] do
        let program=CommandRecipe.prepare selection trusted prepared (String('c',64)) target "/recipe/parent.oci.tar" "/recipe/work" "/recipe/evidence" (String('a',32)) (String('b',32)) 32
        cliControls selection trusted program
