namespace FS.GG.Coord.Cli

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Runtime.InteropServices
open Microsoft.Data.Sqlite
open FS.GG.Coord

module TelemetryStoreApplication =
    type DrainHooks = { BeforeCommit: unit -> unit; AfterCommitBeforeDelete: unit -> unit }
    let databaseFileName = "telemetry.sqlite3"
    let private minimumEngine = Version(3, 51, 3)
    let private busyMilliseconds = 750
    let private maxDrainBatches = 128
    let private maxDrainBytes = 8L * 1024L * 1024L
    let private maxPendingPerProducer = 128

    module private Native =
        [<Literal>]
        let LockExclusive = 2
        [<Literal>]
        let LockNonBlocking = 4
        [<Literal>]
        let LockUnlock = 8
        [<DllImport("libc", SetLastError = true)>]
        extern int flock(int fd, int operation)
        [<DllImport("libc", SetLastError = true)>]
        extern int fsync(int fd)
        [<DllImport("libc", EntryPoint = "open", SetLastError = true)>]
        extern int openDirectory(string path, int flags)
        [<DllImport("libc", SetLastError = true)>]
        extern int close(int fd)
    let private migrationSql = """
CREATE TABLE store_metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL) STRICT;
CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY, digest TEXT NOT NULL, applied_utc TEXT NOT NULL) STRICT;
CREATE TABLE items(identity TEXT PRIMARY KEY, item_id TEXT, feature_id TEXT) STRICT;
CREATE TABLE features(identity TEXT PRIMARY KEY, item_id TEXT, name TEXT NOT NULL) STRICT;
CREATE TABLE attempts(identity TEXT PRIMARY KEY, item_id TEXT, parent_item_id TEXT NOT NULL, status TEXT NOT NULL) STRICT;
CREATE TABLE parent_child(identity TEXT PRIMARY KEY, item_id TEXT, parent_id TEXT NOT NULL, child_id TEXT NOT NULL) STRICT;
CREATE TABLE pr_heads(identity TEXT PRIMARY KEY, item_id TEXT, repository TEXT NOT NULL, pr_number INTEGER NOT NULL CHECK(pr_number >= 0), head TEXT NOT NULL) STRICT;
CREATE TABLE source_cursors(source_identity TEXT NOT NULL, generation TEXT NOT NULL, cursor TEXT NOT NULL, batch_digest TEXT NOT NULL, PRIMARY KEY(source_identity,generation)) STRICT;
CREATE TABLE usage_observations(identity TEXT PRIMARY KEY, item_id TEXT, provider TEXT NOT NULL, model TEXT NOT NULL, effort TEXT NOT NULL, input_count INTEGER NOT NULL CHECK(input_count >= 0), cached_input INTEGER NOT NULL CHECK(cached_input >= 0), cache_write_input INTEGER NOT NULL CHECK(cache_write_input >= 0), output_count INTEGER NOT NULL CHECK(output_count >= 0), reasoning INTEGER, total INTEGER NOT NULL CHECK(total >= 0), responses INTEGER NOT NULL CHECK(responses >= 0), sessions INTEGER NOT NULL CHECK(sessions >= 0), turns INTEGER NOT NULL CHECK(turns >= 0)) STRICT;
CREATE TABLE delivery_observations(identity TEXT PRIMARY KEY, item_id TEXT, state TEXT NOT NULL, expected_head TEXT, observed_head TEXT, pr_number INTEGER) STRICT;
CREATE TABLE evidence_observations(identity TEXT PRIMARY KEY, item_id TEXT, digest TEXT NOT NULL, availability TEXT NOT NULL) STRICT;
CREATE TABLE coverage_observations(identity TEXT PRIMARY KEY, item_id TEXT, record_validity TEXT NOT NULL, join_integrity TEXT NOT NULL, population_coverage TEXT NOT NULL, qualification TEXT NOT NULL, eligible INTEGER, observed INTEGER) STRICT;
CREATE TABLE health_diagnostics(identity TEXT PRIMARY KEY, item_id TEXT, code TEXT NOT NULL, severity TEXT NOT NULL) STRICT;
CREATE TABLE ingest_batches(ingest_id TEXT PRIMARY KEY, content_digest TEXT NOT NULL, source_identity TEXT NOT NULL, generation TEXT NOT NULL, cursor TEXT NOT NULL, accepted_count INTEGER NOT NULL, replay_count INTEGER NOT NULL) STRICT;
CREATE TABLE ingest_facts(identity TEXT PRIMARY KEY, kind TEXT NOT NULL, item_id TEXT, revision INTEGER NOT NULL CHECK(revision >= 0), content_digest TEXT NOT NULL, canonical TEXT NOT NULL) STRICT;
CREATE TABLE corrections(sequence INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, identity TEXT NOT NULL, old_revision INTEGER NOT NULL, new_revision INTEGER NOT NULL, old_digest TEXT NOT NULL, new_digest TEXT NOT NULL) STRICT;
PRAGMA user_version=1;
"""
    let private migrationDigest = CanonicalJson.sha256(Encoding.UTF8.GetBytes migrationSql)

    let private scalarText (connection: SqliteConnection) sql =
        use command = connection.CreateCommand()
        command.CommandText <- sql
        string (command.ExecuteScalar())
    let private execute (connection: SqliteConnection) sql =
        use command = connection.CreateCommand()
        command.CommandText <- sql
        command.ExecuteNonQuery() |> ignore
    let private configure writer (connection: SqliteConnection) =
        execute connection $"PRAGMA busy_timeout=%d{busyMilliseconds}; PRAGMA foreign_keys=ON;"
        if writer then execute connection "PRAGMA synchronous=FULL;"
        let engine = scalarText connection "SELECT sqlite_version();"
        match Version.TryParse engine with
        | true, version when version >= minimumEngine -> Ok engine
        | _ -> Error [ $"unsupported native SQLite engine %s{engine}; require >= %O{minimumEngine}" ]
    let private connect path mode =
        let builder = SqliteConnectionStringBuilder()
        builder.DataSource <- Path.Combine(path, databaseFileName)
        builder.Mode <- mode
        builder.Pooling <- false
        let connection = new SqliteConnection(builder.ConnectionString)
        connection.Open()
        match configure (mode <> SqliteOpenMode.ReadOnly) connection with
        | Ok engine -> Ok(connection, engine)
        | Error errors -> connection.Dispose(); Error errors
    let private parameter (command: SqliteCommand) name (value: obj) = command.Parameters.AddWithValue(name, value) |> ignore
    let private beginImmediate (connection: SqliteConnection) = execute connection "BEGIN IMMEDIATE;"
    let private rollback (connection: SqliteConnection) = try execute connection "ROLLBACK;" with _ -> ()
    let private failBusy (error: SqliteException) =
        if error.SqliteErrorCode = 5 || error.SqliteExtendedErrorCode = 5 then [ $"store-busy after bounded %d{busyMilliseconds}ms" ] else [ error.Message ]

    type private WriterLock(stream: FileStream) =
        member _.Stream = stream
        interface IDisposable with
            member _.Dispose() =
                if OperatingSystem.IsLinux() then Native.flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), Native.LockUnlock) |> ignore
                stream.Dispose()
    let private tryWriterLock root =
        try
            let stream = new FileStream(Path.Combine(root, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite, 1, FileOptions.WriteThrough)
            if not (OperatingSystem.IsLinux()) then stream.Dispose(); Error [ "writer lock is supported only on Linux" ]
            elif Native.flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), Native.LockExclusive ||| Native.LockNonBlocking) <> 0 then stream.Dispose(); Error [ "writer-busy" ]
            else Ok(new WriterLock(stream))
        with
        | :? IOException -> Error [ "writer-busy" ]
        | error -> Error [ error.Message ]
    let private fsyncDirectory path =
        if OperatingSystem.IsLinux() then
            let descriptor = Native.openDirectory(path, 0x10000 ||| 0x80000)
            if descriptor < 0 then raise (IOException $"cannot open directory for durability sync: %s{path}")
            try if Native.fsync descriptor <> 0 then raise (IOException $"cannot sync directory: %s{path}")
            finally Native.close descriptor |> ignore

    let private existingAncestors path =
        let rec loop (directory: DirectoryInfo) acc =
            if isNull directory then acc else loop directory.Parent (directory :: acc)
        loop (DirectoryInfo path) [] |> List.filter _.Exists
    let private commandOutput executable arguments =
        try
            let info = ProcessStartInfo(executable)
            info.UseShellExecute <- false; info.RedirectStandardOutput <- true; info.RedirectStandardError <- true
            arguments |> List.iter info.ArgumentList.Add
            use childProcess = Process.Start info
            let output = childProcess.StandardOutput.ReadToEnd().Trim()
            childProcess.WaitForExit(1000) |> ignore
            if childProcess.ExitCode = 0 then Some output else None
        with _ -> None
    let assessProductionRoot path =
        try
            if String.IsNullOrWhiteSpace path || not (Path.IsPathFullyQualified path) then TelemetryStore.Unsafe "path is not absolute"
            else
                let full = Path.GetFullPath path
                let temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + string Path.DirectorySeparatorChar
                if full.StartsWith(temp, StringComparison.Ordinal) || full = temp.TrimEnd(Path.DirectorySeparatorChar) then TelemetryStore.Unsafe "temporary storage is not durable"
                elif existingAncestors full |> List.exists (fun entry -> not (isNull entry.LinkTarget)) then TelemetryStore.Unsafe "symlinked root or ancestor"
                elif existingAncestors full |> List.exists (fun entry -> Directory.Exists(Path.Combine(entry.FullName, ".git")) || File.Exists(Path.Combine(entry.FullName, ".git"))) then TelemetryStore.Unsafe "repository roots and worktrees are not telemetry stores"
                elif OperatingSystem.IsLinux() then
                    let mountTarget =
                        if Directory.Exists full then full
                        else existingAncestors full |> List.tryLast |> Option.map _.FullName |> Option.defaultValue full
                    match commandOutput "findmnt" [ "-n"; "-o"; "FSTYPE"; "--target"; mountTarget ] with
                    | Some fs when [ "nfs"; "nfs4"; "cifs"; "smb3"; "9p"; "tmpfs"; "overlay"; "fuse" ] |> List.exists (fun value -> fs.StartsWith(value, StringComparison.OrdinalIgnoreCase)) -> TelemetryStore.Unsafe $"filesystem '%s{fs}' is network, memory, or an unqualified container layer"
                    | Some _ -> TelemetryStore.ApprovedLocalDurable
                    | None -> TelemetryStore.DurabilityUnverified "filesystem placement could not be classified"
                else TelemetryStore.DurabilityUnverified "platform durability classification is unavailable"
        with error -> TelemetryStore.DurabilityUnverified error.Message

    let private validateExistingPermissions path =
        try
            if Directory.Exists path && not (OperatingSystem.IsWindows()) then
                let mode = File.GetUnixFileMode path
                if mode.HasFlag UnixFileMode.OtherWrite || mode.HasFlag UnixFileMode.GroupWrite then Error [ "store root permissions permit group/other writes" ]
                elif OperatingSystem.IsLinux() then
                    match commandOutput "stat" [ "-c"; "%u"; path ], commandOutput "id" [ "-u" ] with
                    | Some owner, Some current when owner = current -> Ok ()
                    | Some _, Some _ -> Error [ "store root is not owned by the current user" ]
                    | _ -> Error [ "store root ownership could not be verified" ]
                else Ok ()
            else Ok ()
        with error -> Error [ "cannot validate store root permissions: " + error.Message ]

    let private validateRoot path assessment =
        TelemetryStore.validateStoreRoot path assessment
        |> Result.bind (fun root -> validateExistingPermissions root |> Result.map (fun () -> root))

    let initialize path assessment =
        match validateRoot path assessment with
        | Error errors -> Error errors
        | Ok root ->
            try
                Directory.CreateDirectory root |> ignore
                if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(root, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
                let probe = Path.Combine(root, ".fsgg-write-probe-" + Guid.NewGuid().ToString("N"))
                use _probe = File.Create(probe, 1, FileOptions.DeleteOnClose)
                match tryWriterLock root with
                | Error errors -> Error errors
                | Ok writerLock ->
                    use writerLock = writerLock
                    match connect root SqliteOpenMode.ReadWriteCreate with
                    | Error errors -> Error errors
                    | Ok(connection, engine) ->
                      use connection = connection
                      try
                        let version = Int32.Parse(scalarText connection "PRAGMA user_version;")
                        if version > 1 then Error [ $"store schema version %d{version} is newer than supported version 1" ]
                        else
                            if version = 0 then
                                execute connection "PRAGMA journal_mode=WAL;"
                                beginImmediate connection
                                try
                                    execute connection migrationSql
                                    use migration = connection.CreateCommand()
                                    migration.CommandText <- "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(1,$digest,$utc); INSERT INTO store_metadata(key,value) VALUES('schema','fsgg.telemetry.sqlite-store/1'),('nativeEngine',$engine);"
                                    parameter migration "$digest" migrationDigest; parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O")); parameter migration "$engine" engine
                                    migration.ExecuteNonQuery() |> ignore
                                    execute connection "COMMIT;"
                                with error -> rollback connection; raise error
                            let storedDigest = scalarText connection "SELECT digest FROM schema_migrations WHERE version=1;"
                            if storedDigest <> migrationDigest then Error [ "migration checksum mismatch" ]
                            else Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.store-status/1"; status = "ready"; root = root; database = databaseFileName; schemaVersion = 1; nativeEngine = engine; journalMode = scalarText connection "PRAGMA journal_mode;"; synchronous = scalarText connection "PRAGMA synchronous;" |} + "\n")
                      with :? SqliteException as error -> Error(failBusy error)
            with error -> Error [ error.Message ]

    let status path assessment =
        match validateRoot path assessment with
        | Error errors -> Error errors
        | Ok root when not (File.Exists(Path.Combine(root, databaseFileName))) ->
            Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.store-status/1"; status = "not-initialized"; root = root |} + "\n")
        | Ok root ->
            match connect root SqliteOpenMode.ReadOnly with
            | Error errors -> Error errors
            | Ok(connection, engine) ->
                use connection = connection
                try
                    let version = Int32.Parse(scalarText connection "PRAGMA user_version;")
                    if version > 1 then Error [ $"store schema version %d{version} is newer than supported version 1" ]
                    elif version <> 1 then Error [ "telemetry store schema is not initialized" ]
                    elif scalarText connection "SELECT digest FROM schema_migrations WHERE version=1;" <> migrationDigest then Error [ "migration checksum mismatch" ]
                    else
                        let inbox = Path.Combine(root, "inbox")
                        let pending = if Directory.Exists inbox then Directory.EnumerateFiles(inbox, "*.ready", SearchOption.AllDirectories) |> Seq.truncate 129 |> Seq.length else 0
                        Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.store-status/1"; status = "ready"; root = root; database = databaseFileName; schemaVersion = version; nativeEngine = engine; journalMode = scalarText connection "PRAGMA journal_mode;"; pendingBatches = pending |} + "\n")
                with error -> Error [ error.Message ]

    let private deleteTyped (connection: SqliteConnection) identity =
        for table in [ "items"; "features"; "attempts"; "parent_child"; "pr_heads"; "usage_observations"; "delivery_observations"; "evidence_observations"; "coverage_observations"; "health_diagnostics" ] do
            use command = connection.CreateCommand()
            command.CommandText <- $"DELETE FROM %s{table} WHERE identity=$identity;"
            parameter command "$identity" identity
            command.ExecuteNonQuery() |> ignore
    let private insertTyped (connection: SqliteConnection) (fact: TelemetryStore.Fact) =
        let optional value = value |> Option.map box |> Option.defaultValue DBNull.Value
        let run sql (values: (string * obj) list) =
            use command = connection.CreateCommand()
            command.CommandText <- sql
            parameter command "$identity" fact.Identity
            parameter command "$item" (optional fact.ItemId)
            values |> List.iter (fun (name, value) -> parameter command name value)
            command.ExecuteNonQuery() |> ignore
        match fact.Payload with
        | TelemetryStore.Item feature -> run "INSERT INTO items VALUES($identity,$item,$feature);" [ "$feature", optional feature ]
        | TelemetryStore.Feature name -> run "INSERT INTO features VALUES($identity,$item,$name);" [ "$name", box name ]
        | TelemetryStore.Attempt(parent,status) -> run "INSERT INTO attempts VALUES($identity,$item,$parent,$status);" [ "$parent", box parent; "$status", box status ]
        | TelemetryStore.ParentChild(parent,child) -> run "INSERT INTO parent_child VALUES($identity,$item,$parent,$child);" [ "$parent", box parent; "$child", box child ]
        | TelemetryStore.PullRequestHead(repo,pr,head) -> run "INSERT INTO pr_heads VALUES($identity,$item,$repo,$pr,$head);" [ "$repo", box repo; "$pr", box pr; "$head", box head ]
        | TelemetryStore.Source(source,generation,cursor) -> run "INSERT INTO health_diagnostics VALUES($identity,$item,'source-fact','info');" []
        | TelemetryStore.Usage(provider,model,effort,input,cached,write,output,reasoning,total,responses,sessions,turns) -> run "INSERT INTO usage_observations VALUES($identity,$item,$provider,$model,$effort,$input,$cached,$write,$output,$reasoning,$total,$responses,$sessions,$turns);" [ "$provider",box provider; "$model",box model; "$effort",box effort; "$input",box input; "$cached",box cached; "$write",box write; "$output",box output; "$reasoning", optional reasoning; "$total",box total; "$responses",box responses; "$sessions",box sessions; "$turns",box turns ]
        | TelemetryStore.Delivery(state,expected,observed,pr) -> run "INSERT INTO delivery_observations VALUES($identity,$item,$state,$expected,$observed,$pr);" [ "$state",box state; "$expected",optional expected; "$observed",optional observed; "$pr",optional pr ]
        | TelemetryStore.Evidence(digest,availability) -> run "INSERT INTO evidence_observations VALUES($identity,$item,$digest,$availability);" [ "$digest",box digest; "$availability",box availability ]
        | TelemetryStore.Coverage coverage -> run "INSERT INTO coverage_observations VALUES($identity,$item,$validity,$join,$coverage,$qualification,$eligible,$observed);" [ "$validity",box coverage.RecordValidity; "$join",box coverage.JoinIntegrity; "$coverage",box coverage.PopulationCoverage; "$qualification",box coverage.Qualification; "$eligible",optional coverage.Eligible; "$observed",optional coverage.Observed ]
        | TelemetryStore.Diagnostic(code,severity) -> run "INSERT INTO health_diagnostics VALUES($identity,$item,$code,$severity);" [ "$code",box code; "$severity",box severity ]
        | TelemetryStore.Correction(target,reason) -> run "INSERT INTO health_diagnostics VALUES($identity,$item,$code,'correction');" [ "$code", box $"target=%s{target}; %s{reason}" ]

    let private ingestBatchLocked root beforeCommit (batch: TelemetryStore.Batch) =
            if not (File.Exists(Path.Combine(root, databaseFileName))) then Error [ "telemetry store is not initialized" ] else
            match connect root SqliteOpenMode.ReadWrite with
            | Error errors -> Error errors
            | Ok(connection, engine) ->
                use connection = connection
                try
                    let version = Int32.Parse(scalarText connection "PRAGMA user_version;")
                    if version > 1 then Error [ $"store schema version %d{version} is newer than supported version 1" ]
                    elif version <> 1 then Error [ "telemetry store schema is not initialized" ]
                    else
                        beginImmediate connection
                        try
                            use priorBatch = connection.CreateCommand()
                            priorBatch.CommandText <- "SELECT content_digest FROM ingest_batches WHERE ingest_id=$id;"
                            parameter priorBatch "$id" batch.IngestId
                            let prior = priorBatch.ExecuteScalar()
                            if not (isNull prior) && string prior <> batch.ContentDigest then invalidOp "ingest identity conflicts with different batch content"
                            let mutable accepted = 0L
                            let mutable replayed = 0L
                            for fact in batch.Facts do
                                use existing = connection.CreateCommand()
                                existing.CommandText <- "SELECT content_digest,revision FROM ingest_facts WHERE identity=$identity;"
                                parameter existing "$kind" fact.Kind; parameter existing "$identity" fact.Identity
                                use reader = existing.ExecuteReader()
                                let state = if reader.Read() then Some(reader.GetString(0), reader.GetInt64(1)) else None
                                reader.Close()
                                match state with
                                | Some(digest, _) when digest = fact.ContentDigest -> replayed <- replayed + 1L
                                | Some(_, revision) when fact.Revision <= revision -> invalidOp $"native fact identity conflict: %s{fact.Kind}/%s{fact.Identity}"
                                | Some(oldDigest, revision) ->
                                    use correction = connection.CreateCommand()
                                    correction.CommandText <- "INSERT INTO corrections(kind,identity,old_revision,new_revision,old_digest,new_digest) VALUES($kind,$identity,$old,$new,$oldDigest,$newDigest); UPDATE ingest_facts SET kind=$kind,item_id=$item,revision=$new,content_digest=$newDigest,canonical=$canonical WHERE identity=$identity;"
                                    [ "$kind",box fact.Kind; "$identity",fact.Identity; "$old",revision; "$new",fact.Revision; "$oldDigest",oldDigest; "$newDigest",fact.ContentDigest; "$item",fact.ItemId |> Option.map box |> Option.defaultValue DBNull.Value; "$canonical",fact.Canonical ] |> List.iter (fun (name,value) -> parameter correction name value)
                                    correction.ExecuteNonQuery() |> ignore; deleteTyped connection fact.Identity; insertTyped connection fact; accepted <- accepted + 1L
                                | None ->
                                    use insert = connection.CreateCommand()
                                    insert.CommandText <- "INSERT INTO ingest_facts(kind,identity,item_id,revision,content_digest,canonical) VALUES($kind,$identity,$item,$revision,$digest,$canonical);"
                                    [ "$kind",box fact.Kind; "$identity",fact.Identity; "$item",fact.ItemId |> Option.map box |> Option.defaultValue DBNull.Value; "$revision",fact.Revision; "$digest",fact.ContentDigest; "$canonical",fact.Canonical ] |> List.iter (fun (name,value) -> parameter insert name value)
                                    insert.ExecuteNonQuery() |> ignore; insertTyped connection fact; accepted <- accepted + 1L
                            use source = connection.CreateCommand()
                            source.CommandText <- "INSERT INTO source_cursors(source_identity,generation,cursor,batch_digest) VALUES($source,$generation,$cursor,$digest) ON CONFLICT(source_identity,generation) DO UPDATE SET cursor=excluded.cursor,batch_digest=excluded.batch_digest;"
                            [ "$source",box batch.SourceIdentity; "$generation",batch.Generation; "$cursor",batch.Cursor; "$digest",batch.ContentDigest ] |> List.iter (fun (name,value) -> parameter source name value)
                            source.ExecuteNonQuery() |> ignore
                            if isNull prior then
                                use insertBatch = connection.CreateCommand()
                                insertBatch.CommandText <- "INSERT INTO ingest_batches VALUES($id,$digest,$source,$generation,$cursor,$accepted,$replayed);"
                                [ "$id",box batch.IngestId; "$digest",batch.ContentDigest; "$source",batch.SourceIdentity; "$generation",batch.Generation; "$cursor",batch.Cursor; "$accepted",accepted; "$replayed",replayed ] |> List.iter (fun (name,value) -> parameter insertBatch name value)
                                insertBatch.ExecuteNonQuery() |> ignore
                            beforeCommit ()
                            execute connection "COMMIT;"
                            Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.ingest-result/1"; ingestId = batch.IngestId; digest = batch.ContentDigest; accepted = accepted; replayed = replayed; cursor = batch.Cursor; nativeEngine = engine |} + "\n")
                        with error -> rollback connection; raise error
                with
                | :? SqliteException as error -> Error(failBusy error)
                | error -> Error [ error.Message ]

    let publish path assessment bytes =
        match validateRoot path assessment, TelemetryStore.parseBatch bytes with
        | Error errors, _ | _, Error errors -> Error errors
        | Ok root, Ok batch ->
            try
                if not (File.Exists(Path.Combine(root, databaseFileName))) then Error [ "telemetry store is not initialized" ] else
                let inbox = Path.Combine(root, "inbox")
                let producer = Path.Combine(inbox, batch.SourceIdentity)
                Directory.CreateDirectory producer |> ignore
                if not (OperatingSystem.IsWindows()) then
                    File.SetUnixFileMode(inbox, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
                    File.SetUnixFileMode(producer, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
                let pending = Directory.EnumerateFiles(producer, "*.ready", SearchOption.TopDirectoryOnly) |> Seq.truncate (maxPendingPerProducer + 1) |> Seq.length
                if pending >= maxPendingPerProducer then Error [ "producer inbox is full" ] else
                let ready = Path.Combine(producer, $"%s{batch.IngestId}.%s{batch.ContentDigest}.ready")
                if File.Exists ready then
                    let existing = File.ReadAllBytes ready
                    match TelemetryStore.parseBatch existing with
                    | Ok current when current.ContentDigest = batch.ContentDigest -> Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.publish-result/1"; status = "already-queued"; producer = batch.SourceIdentity; ingestId = batch.IngestId; digest = batch.ContentDigest |} + "\n")
                    | _ -> Error [ "ready publication identity collision" ]
                else
                    let nonce = Guid.NewGuid().ToString("N")
                    let temporary = Path.Combine(producer, $".%s{batch.IngestId}.%s{nonce}.tmp")
                    try
                        use stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)
                        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(temporary, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
                        stream.Write bytes; stream.Flush(true); stream.Close()
                        File.Move(temporary, ready, false)
                        fsyncDirectory producer
                        Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.publish-result/1"; status = "queued"; producer = batch.SourceIdentity; ingestId = batch.IngestId; digest = batch.ContentDigest |} + "\n")
                    finally
                        if File.Exists temporary then File.Delete temporary
            with error -> Error [ error.Message ]

    let private fairReadyFiles root =
        let inbox = Path.Combine(root, "inbox")
        if not (Directory.Exists inbox) then [] else
        let cursorPath = Path.Combine(root, "drain.cursor")
        let prior = if File.Exists cursorPath then File.ReadAllText(cursorPath).Trim() else ""
        let directories = Directory.EnumerateDirectories inbox |> Seq.sort |> Seq.toArray
        let start = directories |> Array.tryFindIndex (fun directory -> String.CompareOrdinal(DirectoryInfo(directory).Name, prior) > 0) |> Option.defaultValue 0
        let ordered = if start = 0 then directories else Array.append directories[start..] directories[..start-1]
        let queues =
            ordered
            |> Seq.map (fun directory -> Collections.Generic.Queue<string>(Directory.EnumerateFiles(directory, "*.ready") |> Seq.sort))
            |> Seq.toArray
        let selected = ResizeArray<string>()
        let mutable totalBytes = 0L
        let mutable progressed = true
        while selected.Count < maxDrainBatches && totalBytes < maxDrainBytes && progressed do
            progressed <- false
            for queue in queues do
                if selected.Count < maxDrainBatches && queue.Count > 0 then
                    let candidate = queue.Peek()
                    let size = FileInfo(candidate).Length
                    if totalBytes + size <= maxDrainBytes then
                        selected.Add(queue.Dequeue()); totalBytes <- totalBytes + size; progressed <- true
        List.ofSeq selected

    let private pendingCount root =
        let inbox = Path.Combine(root, "inbox")
        if Directory.Exists inbox then Directory.EnumerateFiles(inbox, "*.ready", SearchOption.AllDirectories) |> Seq.truncate 129 |> Seq.length else 0

    let private quarantine root (path: string) reasons =
        let producer = DirectoryInfo(Path.GetDirectoryName path).Name
        let targetDirectory = Path.Combine(root, "quarantine", producer)
        Directory.CreateDirectory targetDirectory |> ignore
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(targetDirectory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
        let initial = Path.Combine(targetDirectory, Path.GetFileName(path) + ".rejected")
        let target = if File.Exists initial then initial + "." + Guid.NewGuid().ToString("N") else initial
        File.Move(path, target, false)
        let bounded = String.concat "; " reasons
        let reason = if bounded.Length <= 2048 then bounded else bounded.Substring(0,2048)
        File.WriteAllText(target + ".reason", reason, UTF8Encoding(false))
        if not (OperatingSystem.IsWindows()) then
            File.SetUnixFileMode(target, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            File.SetUnixFileMode(target + ".reason", UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        fsyncDirectory targetDirectory; fsyncDirectory(Path.GetDirectoryName path)

    let drainWithHooks path assessment hooks =
        match validateRoot path assessment with
        | Error errors -> Error errors
        | Ok root ->
            match tryWriterLock root with
            | Error errors -> Error errors
            | Ok writerLock ->
                use writerLock = writerLock
                let selected = fairReadyFiles root
                let mutable accepted = 0
                let mutable replayed = 0
                let mutable quarantined = 0
                let mutable failures: string list = []
                for ready in selected do
                    let bytes = try File.ReadAllBytes ready with error -> failures <- error.Message :: failures; Array.empty
                    let parsed = TelemetryStore.parseBatch bytes
                    let nameValid (batch: TelemetryStore.Batch) = Path.GetFileName ready = $"%s{batch.IngestId}.%s{batch.ContentDigest}.ready"
                    match parsed with
                    | Error errors -> quarantine root ready errors; quarantined <- quarantined + 1
                    | Ok batch when not (nameValid batch) -> quarantine root ready [ "ready filename does not match batch identity and digest" ]; quarantined <- quarantined + 1
                    | Ok batch ->
                        match ingestBatchLocked root hooks.BeforeCommit batch with
                        | Error errors when errors |> List.exists (fun error -> error.Contains("identity conflict", StringComparison.Ordinal)) ->
                            quarantine root ready errors; quarantined <- quarantined + 1
                        | Error errors -> failures <- (String.concat "; " errors) :: failures
                        | Ok result ->
                            use doc = JsonDocument.Parse result
                            accepted <- accepted + doc.RootElement.GetProperty("accepted").GetInt32()
                            replayed <- replayed + doc.RootElement.GetProperty("replayed").GetInt32()
                            try hooks.AfterCommitBeforeDelete(); File.Delete ready; fsyncDirectory(Path.GetDirectoryName ready) with error -> failures <- ("committed but ready removal failed: " + error.Message) :: failures
                match List.tryLast selected with
                | Some last ->
                    let cursorPath = Path.Combine(root, "drain.cursor")
                    let temporary = cursorPath + ".tmp"
                    File.WriteAllText(temporary, DirectoryInfo(Path.GetDirectoryName last).Name, UTF8Encoding(false))
                    File.Move(temporary, cursorPath, true); fsyncDirectory root
                | None -> ()
                if not failures.IsEmpty then Error(List.rev failures)
                else Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.drain-result/1"; accepted = accepted; replayed = replayed; quarantined = quarantined; remaining = pendingCount root |} + "\n")

    let drain path assessment =
        drainWithHooks path assessment { BeforeCommit = ignore; AfterCommitBeforeDelete = ignore }

    let ingest path assessment bytes =
        match publish path assessment bytes with
        | Error errors -> Error errors
        | Ok queued ->
            match drain path assessment with
            | Ok drained -> Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.ingest-disposition/1"; status = "drained"; publish = JsonDocument.Parse(queued).RootElement.Clone(); drain = JsonDocument.Parse(drained).RootElement.Clone() |} + "\n")
            | Error [ "writer-busy" ] -> Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.ingest-disposition/1"; status = "queued"; reason = "writer-busy"; publish = JsonDocument.Parse(queued).RootElement.Clone() |} + "\n")
            | Error errors -> Error errors

    let private readSummary (connection: SqliteConnection) itemId : TelemetryStore.Aggregate =
        let count table =
            use command = connection.CreateCommand()
            command.CommandText <- $"SELECT count(*) FROM %s{table} WHERE item_id=$item;"
            parameter command "$item" itemId
            Convert.ToInt64(command.ExecuteScalar())
        let sum column =
            use command = connection.CreateCommand()
            command.CommandText <- $"SELECT coalesce(sum(%s{column}),0) FROM usage_observations WHERE item_id=$item;"
            parameter command "$item" itemId
            Convert.ToInt64(command.ExecuteScalar())
        let latestCoverage column fallback =
            use command = connection.CreateCommand()
            command.CommandText <- $"SELECT %s{column} FROM coverage_observations WHERE item_id=$item ORDER BY rowid DESC LIMIT 1;"
            parameter command "$item" itemId
            let value = command.ExecuteScalar()
            if isNull value || value = box DBNull.Value then fallback else string value
        let usageCount = count "usage_observations"
        let reasoning =
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT CASE WHEN count(*)=count(reasoning) THEN coalesce(sum(reasoning),0) ELSE NULL END FROM usage_observations WHERE item_id=$item;"
            parameter command "$item" itemId
            let value = command.ExecuteScalar()
            if isNull value || value = box DBNull.Value then None else Some(Convert.ToInt64 value)
        { ItemId = itemId; FactCount = count "ingest_facts"; UsageObservations = usageCount; DeliveryObservations = count "delivery_observations"
          Input = sum "input_count"; CachedInput = sum "cached_input"; CacheWriteInput = sum "cache_write_input"; Output = sum "output_count"; Reasoning = reasoning; Total = sum "total"
          RecordValidity = latestCoverage "record_validity" "unknown"; JoinIntegrity = latestCoverage "join_integrity" "unknown"; PopulationCoverage = latestCoverage "population_coverage" "unknown"; Qualification = latestCoverage "qualification" "not-evaluated" }

    let summary path assessment itemId =
        match validateRoot path assessment |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly) with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection
            Ok(TelemetryStore.publicJson (readSummary connection itemId))

    let exportPublic path assessment itemId outputPath =
        try
            let content =
                match itemId with
                | Some item -> summary path assessment item
                | None ->
                    match validateRoot path assessment |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly) with
                    | Error errors -> Error errors
                    | Ok(connection, _) ->
                        use connection = connection
                        use command = connection.CreateCommand()
                        command.CommandText <- "SELECT DISTINCT item_id FROM ingest_facts WHERE item_id IS NOT NULL ORDER BY item_id;"
                        use reader = command.ExecuteReader()
                        let items = ResizeArray<string>()
                        while reader.Read() do items.Add(reader.GetString 0)
                        reader.Close()
                        let summaries =
                            items
                            |> Seq.map (fun item ->
                                use document = JsonDocument.Parse(TelemetryStore.publicJson (readSummary connection item))
                                document.RootElement.Clone())
                            |> Seq.toArray
                        Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.public-export/1"; items = summaries |} + "\n")
            match content with
            | Error errors -> Error errors
            | Ok content when Encoding.UTF8.GetByteCount content > 64 * 1024 -> Error [ "public export exceeds 65536 bytes" ]
            | Ok content ->
                let target = Path.GetFullPath outputPath
                let rootPrefix = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + string Path.DirectorySeparatorChar
                if target.StartsWith(rootPrefix, StringComparison.Ordinal) then Error [ "public export must be outside the private store root" ]
                elif existingAncestors target |> List.exists (fun entry -> not (isNull entry.LinkTarget)) then Error [ "public export path has a symlinked ancestor" ]
                else
                    let parent = Path.GetDirectoryName target
                    Directory.CreateDirectory parent |> ignore
                    let temporary = target + ".tmp-" + Guid.NewGuid().ToString("N")
                    File.WriteAllText(temporary, content, UTF8Encoding(false)); File.Move(temporary, target, true)
                    fsyncDirectory parent
                    Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.export-result/1"; output = target; bytes = Encoding.UTF8.GetByteCount content |} + "\n")
        with error -> Error [ error.Message ]

    let private option name args = args |> List.indexed |> List.tryPick (fun (index,value) -> if value = name then args |> List.tryItem(index+1) else None)
    let private root args = option "--store-root" args |> Option.orElseWith (fun () -> Environment.GetEnvironmentVariable("FSGG_TELEMETRY_STORE") |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not))
    let private output (result: Result<string, string list>) = match result with Ok json -> Console.Out.Write json; 0 | Error errors -> errors |> List.iter (fun error -> Console.Error.WriteLine("fsgg-coord-engine: telemetry store: " + error)); 1
    let private readBounded path =
        use stream = if path = "-" then Console.OpenStandardInput() else File.OpenRead path
        use memory = new MemoryStream()
        let buffer = Array.zeroCreate<byte> 8192
        let mutable doneReading = false
        while not doneReading && memory.Length <= int64 TelemetryStore.MaxBatchBytes do
            let count = stream.Read(buffer,0,buffer.Length)
            if count = 0 then doneReading <- true else memory.Write(buffer,0,count)
        memory.ToArray()
    let run action args =
        match root args with
        | None ->
            if action = "status" then Console.Out.WriteLine("{\"schema\":\"fsgg.telemetry.store-status/1\",\"status\":\"unconfigured\"}"); 0
            else output(Error [ "store root is not configured; use --store-root or FSGG_TELEMETRY_STORE" ])
        | Some path ->
            let assessment = assessProductionRoot path
            match action with
            | "status" -> status path assessment |> output
            | "init" -> initialize path assessment |> output
            | "publish" -> match option "--input" args with Some input -> publish path assessment (readBounded input) |> output | None -> output(Error [ "--input is required" ])
            | "drain" -> drain path assessment |> output
            | "ingest" -> match option "--input" args with Some input -> ingest path assessment (readBounded input) |> output | None -> output(Error [ "--input is required" ])
            | "summary" -> match option "--item" args with Some item -> summary path assessment item |> output | None -> output(Error [ "--item is required" ])
            | "export" when List.contains "--public" args -> match option "--output" args with Some target -> exportPublic path assessment (option "--item" args) target |> output | _ -> output(Error [ "--output is required" ])
            | "export" -> output(Error [ "only --public export is supported" ])
            | _ -> output(Error [ "action must be status, init, ingest, summary, or export" ])
