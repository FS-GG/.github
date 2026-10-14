namespace FSGG.Telemetry.PersistentV3.ImageClosure

open System
open System.IO
open System.Text.Json

type RecipeSelection={Schema:string;IdentityClass:string;CustodyIdentity:string;Target:string;SelectionSha256:string;TrustedSha256:string;PreparedSha256:string;ParentOci:string;WorkRoot:string;EvidenceRoot:string;NonceA:string;NonceB:string;CleanupRemovalLimit:int}
type RecipeStep={Step:string;Command:ProcessCommand}
type CommandProgram={Selection:RecipeSelection;Steps:RecipeStep array;NormalCommands:int;CleanupCommands:int;MaximumCommands:int}

/// Pure exact command production. Recipe bytes bind data; they never authenticate a root operation.
module CommandRecipe=
    [<Literal>]
    let Builder="/usr/sbin/podman"
    let private require condition reason=if not condition then raise(InvalidDataException reason)
    let private digest value=OciEvidence.shaBytes(JsonSerializer.SerializeToUtf8Bytes value)
    let bytes (program:CommandProgram)=JsonSerializer.SerializeToUtf8Bytes program
    let identity (program:CommandProgram)=OciEvidence.shaBytes(bytes program)
    let parse (raw:byte array)=
        require(raw.Length>0&&raw.Length<=2*1024*1024) "recipe-byte-bound"
        use document=JsonDocument.Parse raw
        let exact (kind:Type) node=CustodyProtocol.exact (kind.GetProperties()|>Array.map _.Name|>Array.toList) node
        let rec closed (node:JsonElement)=
            match node.ValueKind with
            | JsonValueKind.Object->
                let names=Collections.Generic.HashSet<string>()
                for item in node.EnumerateObject() do
                    require(names.Add item.Name) "recipe-duplicate-field"
                    closed item.Value
            | JsonValueKind.Array->
                require(node.GetArrayLength()<=8192) "recipe-array-bound"
                for item in node.EnumerateArray() do closed item
            | JsonValueKind.Null|JsonValueKind.Undefined->raise(InvalidDataException "recipe-null")
            | _->()
        let root=document.RootElement
        closed root
        exact typeof<CommandProgram> root
        exact typeof<RecipeSelection> (root.GetProperty("Selection"))
        let steps=root.GetProperty("Steps")
        require(steps.ValueKind=JsonValueKind.Array&&steps.GetArrayLength()>0&&steps.GetArrayLength()<=256) "recipe-step-bound"
        for step in steps.EnumerateArray() do
            exact typeof<RecipeStep> step
            exact typeof<ProcessCommand> (step.GetProperty("Command"))
        let program=JsonSerializer.Deserialize<CommandProgram> raw
        require(bytes program=raw) "recipe-noncanonical-bytes"
        program
    let private path value=
        require(not(String.IsNullOrWhiteSpace value)&&Path.IsPathFullyQualified value&&Path.GetFullPath value=value&&
                System.Text.RegularExpressions.Regex.IsMatch(value,"^/[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*$")) "recipe-path"
    let bundleRoot (program:CommandProgram) logical=
        require(logical="a"||logical="b") "recipe-bundle"
        Path.Combine(program.Selection.WorkRoot,"bundle-"+logical)
    let nonce (program:CommandProgram) logical=match logical with "a"->program.Selection.NonceA|"b"->program.Selection.NonceB|_->raise(InvalidDataException "recipe-bundle")
    let environment root=Map.ofList[
        "PATH","/usr/sbin:/usr/bin:/bin";"HOME",root;"XDG_RUNTIME_DIR",Path.Combine(root,"runtime");"TMPDIR",Path.Combine(root,"tmp");
        "REGISTRY_AUTH_FILE",Path.Combine(root,"auth.json");"CONTAINERS_CONF",Path.Combine(root,"containers.conf");"CONTAINERS_STORAGE_CONF",Path.Combine(root,"storage.conf")]
    let private command (root:string) reload (arguments:string array)=
        let prefix=if reload then "reload-" else "build-"
        {Executable=Builder;Arguments=Array.append [|"--remote=false";"--events-backend=none";"--storage-driver=vfs";"--root";Path.Combine(root,prefix+"graph");"--runroot";Path.Combine(root,prefix+"run");"--tmpdir";Path.Combine(root,prefix+"tmp")|] arguments;
         Environment=environment root;WorkingDirectory=root;OutputLimit=1024*1024}
    let prepare (selection:Selection) (trusted:TrustedNativeSelection) prepared custody target parent work evidence nonceA nonceB removalLimit=
        require(target="run-c4"||target="run-inactive") "recipe-target"
        CustodyProtocol.sha custody
        for value in [parent;work;evidence] do path value
        require(work<>evidence&&not(work.StartsWith(evidence+"/",StringComparison.Ordinal))&&not(evidence.StartsWith(work+"/",StringComparison.Ordinal))) "recipe-roots-overlap"
        for value in [nonceA;nonceB] do require(not(isNull value)&&System.Text.RegularExpressions.Regex.IsMatch(value,"^[a-f0-9]{32}$")) "recipe-nonce"
        require(nonceA<>nonceB) "recipe-nonce-reuse"
        require(removalLimit>=0&&removalLimit<=32) "recipe-cleanup-bound"
        let selected={Schema="fsgg.learn.rootless-command-program/1";IdentityClass=selection.IdentityClass;CustodyIdentity=custody;Target=target;
                      SelectionSha256=digest selection;TrustedSha256=digest trusted;PreparedSha256=OciEvidence.shaBytes prepared;
                      ParentOci=parent;WorkRoot=work;EvidenceRoot=evidence;NonceA=nonceA;NonceB=nonceB;CleanupRemovalLimit=removalLimit}
        let rows=ResizeArray<RecipeStep>()
        let add logical step reload argv=
            rows.Add({Step=logical+"/"+step;Command=command (Path.Combine(work,"bundle-"+logical)) reload argv})
        for logical in ["a";"b"] do
            let root=Path.Combine(work,"bundle-"+logical)
            let context=Path.Combine(root,"context")
            add logical "parent-load" false [|"load";"--input";parent|]
            add logical "parent-inspect" false [|"image";"inspect";selection.RuntimeImageDigest|]
            add logical "build" false [|"build";"--platform";"linux/amd64";"--format";"oci";"--no-cache";"--pull";"never";"--network";"none";"--isolation";"rootless";"--timestamp";"1790899200";"--compression-format";"gzip";"--compression-level";"6";"--force-compression";"--file";Path.Combine(context,"Containerfile");"--tag";"localhost/learn-p2c4:inert";context|]
            add logical "build-save" false [|"save";"--format";"oci-archive";"--output";Path.Combine(evidence,logical+"-build.oci.tar");"localhost/learn-p2c4:inert"|]
        for logical,selectedNonce in ["a",nonceA;"b",nonceB] do
            add logical "reload-load" true [|"load";"--input";Path.Combine(evidence,logical+"-build.oci.tar")|]
            add logical "reload-inspect" true [|"image";"inspect";"localhost/learn-p2c4:inert"|]
            add logical "reload-save" true [|"save";"--format";"oci-archive";"--output";Path.Combine(evidence,logical+"-reload.oci.tar");"localhost/learn-p2c4:inert"|]
            let probe step executable argv=
                add logical step true (Array.concat[[|"run";"--name";"learn-p2c4-"+selectedNonce;"--rm";"--network";"none";"--read-only";"--cap-drop";"ALL";"--security-opt";"no-new-privileges";"--cpus";"1";"--memory";"512m";"--pids-limit";"64";"--user";"32768:32768";"--entrypoint";executable;"localhost/learn-p2c4:inert"|];argv])
            probe "probe-dotnet" "/usr/share/dotnet/dotnet" [|"--list-runtimes"|]
            probe "probe-python" trusted.PythonExecutable [|"-I";"-S";"-B";"--version"|]
            probe "probe-native" "/opt/fsgg/codex/codex" [|"--version"|]
        if target="run-inactive" then
            for logical in ["a";"b"] do
                let manager,host=InactiveQualification.commands selection trusted (Path.Combine(work,"bundle-"+logical))
                add logical "inactive-inspect" true [|"image";"inspect";"localhost/learn-p2c4:inert"|]
                add logical "inactive-manager" true manager
                add logical "inactive-host" true host
        let normal=rows.Count
        for logical in ["a";"b"] do
            for reload in [false;true] do
                let prefix=if reload then "cleanup-reload-" else "cleanup-build-"
                add logical (prefix+"census") reload [|"ps";"--all";"--quiet";"--no-trunc"|]
                add logical (prefix+"empty") reload [|"ps";"--all";"--quiet"|]
                add logical (prefix+"mount") reload [|"mount";"--notruncate"|]
        let program={Selection=selected;Steps=rows.ToArray();NormalCommands=normal;CleanupCommands=rows.Count-normal;MaximumCommands=rows.Count+removalLimit}
        require(bytes program|>Array.length|>fun count->count<=2*1024*1024) "recipe-byte-bound"
        program
    let validate selection trusted prepared custody target parent work evidence (program:CommandProgram)=
        let expected=prepare selection trusted prepared custody target parent work evidence program.Selection.NonceA program.Selection.NonceB program.Selection.CleanupRemovalLimit
        require(bytes expected=bytes program) "recipe-command-or-input-drift"
    let step program logical name=
        let rows=program.Steps|>Array.filter(fun row->row.Step=logical+"/"+name)
        require(rows.Length=1) "recipe-step-not-selected"
        let selected=rows[0].Command
        {selected with Arguments=Array.copy selected.Arguments}
    /// Only the container ID operand is dynamic. Its captured-result custody is a native caller obligation.
    let removal program logical reload container=
        require(not(isNull container)&&System.Text.RegularExpressions.Regex.IsMatch(container,"^[a-f0-9]{64}$")) "cleanup-container-identity"
        command (bundleRoot program logical) reload [|"rm";"--force";container|]
