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
    let private currentSchemaVersion = 6

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
    let private migration2Sql = """
CREATE TABLE runtime_admissions(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, invocation_id TEXT NOT NULL UNIQUE, feature_id TEXT NOT NULL, attempt_id TEXT NOT NULL, parent_attempt_id TEXT, producer_stream TEXT NOT NULL, requested_model TEXT, requested_effort TEXT, backend TEXT) STRICT;
CREATE TABLE runtime_starts(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, invocation_id TEXT NOT NULL, thread_id TEXT, turn_id TEXT, turn_sequence INTEGER CHECK(turn_sequence >= 0), process_id INTEGER NOT NULL CHECK(process_id >= 0), phase TEXT NOT NULL) STRICT;
CREATE UNIQUE INDEX runtime_thread_start_identity ON runtime_starts(invocation_id,thread_id) WHERE phase='thread' AND thread_id IS NOT NULL;
CREATE UNIQUE INDEX runtime_turn_start_identity ON runtime_starts(invocation_id,thread_id,turn_sequence) WHERE phase='turn';
CREATE TABLE runtime_turn_usage(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, invocation_id TEXT NOT NULL, thread_id TEXT NOT NULL, turn_id TEXT, turn_sequence INTEGER NOT NULL CHECK(turn_sequence >= 0), provider TEXT, requested_model TEXT, observed_model TEXT, requested_effort TEXT, observed_effort TEXT, backend TEXT, accounting_scope TEXT NOT NULL, provenance TEXT NOT NULL, input_count INTEGER NOT NULL CHECK(input_count >= 0), cached_input INTEGER NOT NULL CHECK(cached_input >= 0), output_count INTEGER NOT NULL CHECK(output_count >= 0), reasoning INTEGER, total INTEGER NOT NULL CHECK(total >= 0), UNIQUE(invocation_id,thread_id,turn_sequence)) STRICT;
CREATE UNIQUE INDEX runtime_turn_native_identity ON runtime_turn_usage(invocation_id,thread_id,turn_id) WHERE turn_id IS NOT NULL;
CREATE TABLE runtime_terminals(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, invocation_id TEXT NOT NULL UNIQUE, thread_id TEXT, outcome TEXT NOT NULL, exit_code INTEGER NOT NULL CHECK(exit_code >= 0)) STRICT;
CREATE TABLE runtime_gaps(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, invocation_id TEXT NOT NULL, code TEXT NOT NULL) STRICT;
PRAGMA user_version=2;
"""
    let private migration2Digest = CanonicalJson.sha256(Encoding.UTF8.GetBytes migration2Sql)
    let private migration3Sql = """
CREATE TABLE ci_bindings(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, collection_id TEXT NOT NULL UNIQUE, repository TEXT NOT NULL, head TEXT NOT NULL, pr_number INTEGER NOT NULL CHECK(pr_number > 0), workflow TEXT NOT NULL, feature_id TEXT NOT NULL, attempt_id TEXT NOT NULL, parent_attempt_id TEXT, producer_stream TEXT NOT NULL, binding TEXT NOT NULL) STRICT;
CREATE TABLE ci_pages(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, collection_id TEXT NOT NULL REFERENCES ci_bindings(collection_id), resource TEXT NOT NULL, page INTEGER NOT NULL CHECK(page > 0), count INTEGER NOT NULL CHECK(count BETWEEN 0 AND 100), total INTEGER NOT NULL CHECK(total BETWEEN 0 AND 1000), UNIQUE(collection_id,resource,page)) STRICT;
CREATE TABLE ci_runs(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, run_id INTEGER NOT NULL, attempt INTEGER NOT NULL CHECK(attempt > 0), workflow TEXT NOT NULL, event TEXT NOT NULL, head TEXT NOT NULL, status TEXT NOT NULL, conclusion TEXT, created_at TEXT, started_at TEXT, updated_at TEXT, UNIQUE(repository,run_id,attempt)) STRICT;
CREATE TABLE ci_jobs(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, run_id INTEGER NOT NULL, attempt INTEGER NOT NULL CHECK(attempt > 0), job_id INTEGER NOT NULL, name TEXT NOT NULL, status TEXT NOT NULL, conclusion TEXT, created_at TEXT, started_at TEXT, completed_at TEXT, UNIQUE(repository,run_id,attempt,job_id)) STRICT;
CREATE TABLE ci_steps(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, run_id INTEGER NOT NULL, attempt INTEGER NOT NULL CHECK(attempt > 0), job_id INTEGER NOT NULL, number INTEGER NOT NULL CHECK(number >= 0), name TEXT NOT NULL, status TEXT NOT NULL, conclusion TEXT, started_at TEXT, completed_at TEXT, classification TEXT NOT NULL, rationale TEXT NOT NULL, UNIQUE(repository,run_id,attempt,job_id,number)) STRICT;
CREATE TABLE ci_coverage(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, collection_id TEXT NOT NULL REFERENCES ci_bindings(collection_id), inventory TEXT NOT NULL, attempts TEXT NOT NULL, job_pages TEXT NOT NULL, terminal TEXT NOT NULL, timestamps TEXT NOT NULL, lineage TEXT NOT NULL, classification TEXT NOT NULL, critical_path TEXT NOT NULL) STRICT;
PRAGMA user_version=3;
"""
    let private migration3Digest = CanonicalJson.sha256(Encoding.UTF8.GetBytes migration3Sql)
    let private migration4Sql = """
CREATE TABLE budget_population_facts(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, original_item_id TEXT NOT NULL, state TEXT NOT NULL CHECK(state IN ('open','completed')), source_kind TEXT NOT NULL, source_ref TEXT NOT NULL UNIQUE, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE TABLE budget_attribution_facts(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, dimension TEXT NOT NULL, provider TEXT NOT NULL, accounting_scope TEXT NOT NULL, numerator INTEGER CHECK(numerator >= 0), denominator INTEGER CHECK(denominator >= 0), coverage TEXT NOT NULL, attribution TEXT NOT NULL, source_kind TEXT NOT NULL, source_ref TEXT NOT NULL UNIQUE, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE TABLE budget_interval_facts(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, dimension TEXT NOT NULL, classification TEXT NOT NULL CHECK(classification IN ('administrative','useful','productive')), start_ns INTEGER NOT NULL CHECK(start_ns >= 0), end_ns INTEGER NOT NULL CHECK(end_ns >= start_ns), witnessed INTEGER NOT NULL CHECK(witnessed IN (0,1)), source_kind TEXT NOT NULL, source_ref TEXT NOT NULL UNIQUE, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE TABLE budget_intervention_facts(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, intervention_id TEXT NOT NULL, transition TEXT NOT NULL CHECK(transition IN ('deployed','verified')), sequence INTEGER NOT NULL CHECK(sequence >= 0), result TEXT NOT NULL, coverage TEXT NOT NULL, source_ref TEXT NOT NULL UNIQUE, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE TABLE budget_shared_cost_refs(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, source_ref TEXT NOT NULL UNIQUE, dimension TEXT NOT NULL, provider TEXT NOT NULL, accounting_scope TEXT NOT NULL) STRICT;
CREATE TABLE budget_dirty_items(item_id TEXT PRIMARY KEY) STRICT;
CREATE TABLE budget_epochs(epoch_id TEXT PRIMARY KEY, ordinal INTEGER NOT NULL UNIQUE CHECK(ordinal > 0), state TEXT NOT NULL CHECK(state IN ('open','verified'))) STRICT;
CREATE UNIQUE INDEX budget_one_open_epoch ON budget_epochs(state) WHERE state='open';
CREATE TABLE budget_epoch_membership(epoch_id TEXT NOT NULL REFERENCES budget_epochs(epoch_id), item_id TEXT NOT NULL, original_item_id TEXT NOT NULL, PRIMARY KEY(epoch_id,item_id), UNIQUE(item_id)) STRICT;
CREATE TABLE budget_assessment_revisions(item_id TEXT NOT NULL, dimension TEXT NOT NULL, provider TEXT NOT NULL, accounting_scope TEXT NOT NULL, assessment_revision INTEGER NOT NULL CHECK(assessment_revision > 0), epoch_id TEXT NOT NULL REFERENCES budget_epochs(epoch_id), verdict TEXT NOT NULL CHECK(verdict IN ('unknown','not-applicable','pass','breach')), numerator INTEGER, denominator INTEGER, severe INTEGER NOT NULL CHECK(severe IN (0,1)), reason TEXT NOT NULL, source_digest TEXT NOT NULL, PRIMARY KEY(item_id,dimension,provider,accounting_scope,assessment_revision)) STRICT;
CREATE TABLE budget_breaches(epoch_id TEXT NOT NULL REFERENCES budget_epochs(epoch_id), item_id TEXT NOT NULL, dimension TEXT NOT NULL, provider TEXT NOT NULL, accounting_scope TEXT NOT NULL, assessment_revision INTEGER NOT NULL, severe INTEGER NOT NULL CHECK(severe IN (0,1)), PRIMARY KEY(epoch_id,item_id,dimension,provider,accounting_scope)) STRICT;
CREATE TABLE budget_interventions(intervention_id TEXT PRIMARY KEY, epoch_id TEXT NOT NULL UNIQUE REFERENCES budget_epochs(epoch_id), state TEXT NOT NULL CHECK(state IN ('open','verified')), trigger_item_id TEXT NOT NULL, trigger_kind TEXT NOT NULL CHECK(trigger_kind IN ('fifteenth-distinct','severe')), deployed_ref TEXT, verified_ref TEXT) STRICT;
INSERT INTO budget_epochs(epoch_id,ordinal,state) VALUES('epoch-1',1,'open');
PRAGMA user_version=4;
"""
    let private migration4Digest = CanonicalJson.sha256(Encoding.UTF8.GetBytes migration4Sql)
    let private migration5Sql = """
CREATE TABLE operational_activations(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, activation_id TEXT NOT NULL, scope TEXT NOT NULL CHECK(scope='explicit-future-dispatches'), runtime TEXT NOT NULL, activated_at TEXT NOT NULL, clock_provenance TEXT NOT NULL, late_after_seconds INTEGER NOT NULL CHECK(late_after_seconds >= 0), fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(item_id,activation_id)) STRICT;
CREATE TABLE expected_dispatches(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, dispatch_id TEXT NOT NULL, activation_id TEXT NOT NULL, relation TEXT NOT NULL CHECK(relation IN ('root','child','follow-up')), parent_dispatch_id TEXT, runtime TEXT NOT NULL, expected_at TEXT NOT NULL, clock_provenance TEXT NOT NULL, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(item_id,dispatch_id)) STRICT;
CREATE TABLE invocation_lineage(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, dispatch_id TEXT NOT NULL, invocation_id TEXT NOT NULL, relation TEXT NOT NULL CHECK(relation IN ('root','child','follow-up')), parent_invocation_id TEXT, root_invocation_id TEXT NOT NULL, runtime TEXT NOT NULL, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE INDEX invocation_lineage_dispatch ON invocation_lineage(dispatch_id);
CREATE INDEX invocation_lineage_invocation ON invocation_lineage(invocation_id);
CREATE TABLE operational_event_times(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, invocation_id TEXT NOT NULL, event TEXT NOT NULL CHECK(event IN ('admission','start','terminal')), occurred_at TEXT, occurred_clock_provenance TEXT, observed_at TEXT, observed_clock_provenance TEXT, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(item_id,invocation_id,event)) STRICT;
CREATE INDEX operational_event_times_invocation ON operational_event_times(invocation_id);
PRAGMA user_version=5;
"""
    let private migration5Digest = CanonicalJson.sha256(Encoding.UTF8.GetBytes migration5Sql)
    let private migration6Sql = """
CREATE TABLE ci_population_admissions(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, collection_id TEXT NOT NULL UNIQUE, repository TEXT NOT NULL, pr_number INTEGER NOT NULL CHECK(pr_number > 0), base_ref TEXT NOT NULL, base_sha TEXT NOT NULL, head TEXT NOT NULL, witness TEXT NOT NULL CHECK(witness='native-pr-head'), fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(item_id,repository,pr_number,base_ref,base_sha,head)) STRICT;
CREATE TABLE ci_check_runs(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, check_id INTEGER NOT NULL, name TEXT NOT NULL, app_slug TEXT, status TEXT NOT NULL, conclusion TEXT, started_at TEXT, completed_at TEXT, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(repository,check_id)) STRICT;
CREATE TABLE ci_population_coverage(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, collection_id TEXT NOT NULL REFERENCES ci_population_admissions(collection_id), actions TEXT NOT NULL CHECK(actions IN ('complete','partial','unknown')), checks TEXT NOT NULL CHECK(checks IN ('complete','partial','unknown')), attempts TEXT NOT NULL CHECK(attempts IN ('complete','partial','unknown')), jobs TEXT NOT NULL CHECK(jobs IN ('complete','partial','unknown')), terminal TEXT NOT NULL CHECK(terminal IN ('complete','partial','unknown')), timestamps TEXT NOT NULL CHECK(timestamps IN ('complete','partial','unknown')), continuation TEXT NOT NULL CHECK(continuation IN ('none','pending')), external_checks INTEGER NOT NULL CHECK(external_checks >= 0), gaps TEXT NOT NULL, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(collection_id)) STRICT;
PRAGMA user_version=6;
"""
    let private migration6Digest = CanonicalJson.sha256(Encoding.UTF8.GetBytes migration6Sql)
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
                        if version > currentSchemaVersion then Error [ $"store schema version %d{version} is newer than supported version %d{currentSchemaVersion}" ]
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
                            let afterV1 = Int32.Parse(scalarText connection "PRAGMA user_version;")
                            let storedDigest = scalarText connection "SELECT digest FROM schema_migrations WHERE version=1;"
                            if storedDigest <> migrationDigest then Error [ "migration checksum mismatch" ]
                            else
                                if afterV1 = 1 then
                                    beginImmediate connection
                                    try
                                        execute connection migration2Sql
                                        use migration = connection.CreateCommand()
                                        migration.CommandText <- "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(2,$digest,$utc);"
                                        parameter migration "$digest" migration2Digest
                                        parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                        migration.ExecuteNonQuery() |> ignore
                                        execute connection "COMMIT;"
                                    with error -> rollback connection; raise error
                                if scalarText connection "SELECT digest FROM schema_migrations WHERE version=2;" <> migration2Digest then Error [ "migration checksum mismatch" ]
                                else
                                    let afterV2 = Int32.Parse(scalarText connection "PRAGMA user_version;")
                                    if afterV2 = 2 then
                                        beginImmediate connection
                                        try
                                            execute connection migration3Sql
                                            use migration = connection.CreateCommand()
                                            migration.CommandText <- "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(3,$digest,$utc);"
                                            parameter migration "$digest" migration3Digest
                                            parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                            migration.ExecuteNonQuery() |> ignore
                                            execute connection "COMMIT;"
                                        with error -> rollback connection; raise error
                                    if scalarText connection "SELECT digest FROM schema_migrations WHERE version=3;" <> migration3Digest then Error [ "migration checksum mismatch" ]
                                    else
                                        let afterV3 = Int32.Parse(scalarText connection "PRAGMA user_version;")
                                        if afterV3 = 3 then
                                            beginImmediate connection
                                            try
                                                execute connection migration4Sql
                                                use migration = connection.CreateCommand()
                                                migration.CommandText <- "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(4,$digest,$utc);"
                                                parameter migration "$digest" migration4Digest
                                                parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                                migration.ExecuteNonQuery() |> ignore
                                                execute connection "COMMIT;"
                                            with error -> rollback connection; raise error
                                        if scalarText connection "SELECT digest FROM schema_migrations WHERE version=4;" <> migration4Digest then Error [ "migration checksum mismatch" ]
                                        else
                                            let afterV4 = Int32.Parse(scalarText connection "PRAGMA user_version;")
                                            if afterV4 = 4 then
                                                beginImmediate connection
                                                try
                                                    execute connection migration5Sql
                                                    use migration = connection.CreateCommand()
                                                    migration.CommandText <- "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(5,$digest,$utc);"
                                                    parameter migration "$digest" migration5Digest
                                                    parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                                    migration.ExecuteNonQuery() |> ignore
                                                    execute connection "COMMIT;"
                                                with error -> rollback connection; raise error
                                            if scalarText connection "SELECT digest FROM schema_migrations WHERE version=5;" <> migration5Digest then Error [ "migration checksum mismatch" ]
                                            else
                                                let afterV5 = Int32.Parse(scalarText connection "PRAGMA user_version;")
                                                if afterV5 = 5 then
                                                    beginImmediate connection
                                                    try
                                                        execute connection migration6Sql
                                                        use migration = connection.CreateCommand()
                                                        migration.CommandText <- "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(6,$digest,$utc);"
                                                        parameter migration "$digest" migration6Digest
                                                        parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                                        migration.ExecuteNonQuery() |> ignore
                                                        execute connection "COMMIT;"
                                                    with error -> rollback connection; raise error
                                                if scalarText connection "SELECT digest FROM schema_migrations WHERE version=6;" <> migration6Digest then Error [ "migration checksum mismatch" ]
                                                else Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.store-status/1"; status = "ready"; root = root; database = databaseFileName; schemaVersion = currentSchemaVersion; nativeEngine = engine; journalMode = scalarText connection "PRAGMA journal_mode;"; synchronous = scalarText connection "PRAGMA synchronous;" |} + "\n")
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
                    if version > currentSchemaVersion then Error [ $"store schema version %d{version} is newer than supported version %d{currentSchemaVersion}" ]
                    elif version <> currentSchemaVersion then Error [ "telemetry store schema requires migration; run telemetry store init" ]
                    elif scalarText connection "SELECT digest FROM schema_migrations WHERE version=1;" <> migrationDigest then Error [ "migration checksum mismatch" ]
                    elif scalarText connection "SELECT digest FROM schema_migrations WHERE version=2;" <> migration2Digest then Error [ "migration checksum mismatch" ]
                    elif scalarText connection "SELECT digest FROM schema_migrations WHERE version=3;" <> migration3Digest then Error [ "migration checksum mismatch" ]
                    elif scalarText connection "SELECT digest FROM schema_migrations WHERE version=4;" <> migration4Digest then Error [ "migration checksum mismatch" ]
                    elif scalarText connection "SELECT digest FROM schema_migrations WHERE version=5;" <> migration5Digest then Error [ "migration checksum mismatch" ]
                    elif scalarText connection "SELECT digest FROM schema_migrations WHERE version=6;" <> migration6Digest then Error [ "migration checksum mismatch" ]
                    else
                        let inbox = Path.Combine(root, "inbox")
                        let pending = if Directory.Exists inbox then Directory.EnumerateFiles(inbox, "*.ready", SearchOption.AllDirectories) |> Seq.truncate 129 |> Seq.length else 0
                        Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.store-status/1"; status = "ready"; root = root; database = databaseFileName; schemaVersion = version; nativeEngine = engine; journalMode = scalarText connection "PRAGMA journal_mode;"; pendingBatches = pending |} + "\n")
                with error -> Error [ error.Message ]

    let private deleteTyped (connection: SqliteConnection) identity =
        for table in [ "items"; "features"; "attempts"; "parent_child"; "pr_heads"; "usage_observations"; "delivery_observations"; "evidence_observations"; "coverage_observations"; "health_diagnostics"; "runtime_admissions"; "runtime_starts"; "runtime_turn_usage"; "runtime_terminals"; "runtime_gaps"; "ci_bindings"; "ci_pages"; "ci_runs"; "ci_jobs"; "ci_steps"; "ci_coverage"; "ci_population_coverage"; "ci_check_runs"; "ci_population_admissions"; "budget_population_facts"; "budget_attribution_facts"; "budget_interval_facts"; "budget_intervention_facts"; "budget_shared_cost_refs"; "operational_activations"; "expected_dispatches"; "invocation_lineage"; "operational_event_times" ] do
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
        | TelemetryStore.RuntimeAdmission(invocation,feature,attempt,parent,producer,model,effort,backend) -> run "INSERT INTO runtime_admissions VALUES($identity,$item,$invocation,$feature,$attempt,$parent,$producer,$model,$effort,$backend);" [ "$invocation",box invocation; "$feature",box feature; "$attempt",box attempt; "$parent",optional parent; "$producer",box producer; "$model",optional model; "$effort",optional effort; "$backend",optional backend ]
        | TelemetryStore.RuntimeStart(invocation,threadId,turnId,turnSequence,processId,phase) -> run "INSERT INTO runtime_starts VALUES($identity,$item,$invocation,$thread,$turn,$sequence,$pid,$phase);" [ "$invocation",box invocation; "$thread",optional threadId; "$turn",optional turnId; "$sequence",optional turnSequence; "$pid",box processId; "$phase",box phase ]
        | TelemetryStore.RuntimeTurnUsage(invocation,threadId,turnId,sequence,provider,requestedModel,observedModel,requestedEffort,observedEffort,backend,scope,provenance,input,cached,output,reasoning,total) -> run "INSERT INTO runtime_turn_usage VALUES($identity,$item,$invocation,$thread,$turn,$sequence,$provider,$requestedModel,$observedModel,$requestedEffort,$observedEffort,$backend,$scope,$provenance,$input,$cached,$output,$reasoning,$total);" [ "$invocation",box invocation; "$thread",box threadId; "$turn",optional turnId; "$sequence",box sequence; "$provider",optional provider; "$requestedModel",optional requestedModel; "$observedModel",optional observedModel; "$requestedEffort",optional requestedEffort; "$observedEffort",optional observedEffort; "$backend",optional backend; "$scope",box scope; "$provenance",box provenance; "$input",box input; "$cached",box cached; "$output",box output; "$reasoning",optional reasoning; "$total",box total ]
        | TelemetryStore.RuntimeTerminal(invocation,threadId,outcome,exitCode) -> run "INSERT INTO runtime_terminals VALUES($identity,$item,$invocation,$thread,$outcome,$exit);" [ "$invocation",box invocation; "$thread",optional threadId; "$outcome",box outcome; "$exit",box exitCode ]
        | TelemetryStore.RuntimeGap(invocation,code) -> run "INSERT INTO runtime_gaps VALUES($identity,$item,$invocation,$code);" [ "$invocation",box invocation; "$code",box code ]
        | TelemetryStore.CiBinding(collection,repository,head,pr,workflow,feature,attempt,parent,producer,binding) -> run "INSERT INTO ci_bindings VALUES($identity,$item,$collection,$repository,$head,$pr,$workflow,$feature,$attempt,$parent,$producer,$binding);" [ "$collection",box collection; "$repository",box repository; "$head",box head; "$pr",box pr; "$workflow",box workflow; "$feature",box feature; "$attempt",box attempt; "$parent",optional parent; "$producer",box producer; "$binding",box binding ]
        | TelemetryStore.CiPage(collection,resource,page,count,total) -> run "INSERT INTO ci_pages VALUES($identity,$item,$collection,$resource,$page,$count,$total);" [ "$collection",box collection; "$resource",box resource; "$page",box page; "$count",box count; "$total",box total ]
        | TelemetryStore.CiRun(repository,runId,attempt,workflow,event,head,status,conclusion,created,started,updated) -> run "INSERT INTO ci_runs VALUES($identity,$item,$repository,$run,$attempt,$workflow,$event,$head,$status,$conclusion,$created,$started,$updated);" [ "$repository",box repository; "$run",box runId; "$attempt",box attempt; "$workflow",box workflow; "$event",box event; "$head",box head; "$status",box status; "$conclusion",optional conclusion; "$created",optional created; "$started",optional started; "$updated",optional updated ]
        | TelemetryStore.CiJob(repository,runId,attempt,jobId,name,status,conclusion,created,started,completed) -> run "INSERT INTO ci_jobs VALUES($identity,$item,$repository,$run,$attempt,$job,$name,$status,$conclusion,$created,$started,$completed);" [ "$repository",box repository; "$run",box runId; "$attempt",box attempt; "$job",box jobId; "$name",box name; "$status",box status; "$conclusion",optional conclusion; "$created",optional created; "$started",optional started; "$completed",optional completed ]
        | TelemetryStore.CiStep(repository,runId,attempt,jobId,number,name,status,conclusion,started,completed,classification,rationale) -> run "INSERT INTO ci_steps VALUES($identity,$item,$repository,$run,$attempt,$job,$number,$name,$status,$conclusion,$started,$completed,$classification,$rationale);" [ "$repository",box repository; "$run",box runId; "$attempt",box attempt; "$job",box jobId; "$number",box number; "$name",box name; "$status",box status; "$conclusion",optional conclusion; "$started",optional started; "$completed",optional completed; "$classification",box classification; "$rationale",box rationale ]
        | TelemetryStore.CiCoverage(collection,inventory,attempts,jobPages,terminal,timestamps,lineage,classification,criticalPath) -> run "INSERT INTO ci_coverage VALUES($identity,$item,$collection,$inventory,$attempts,$jobPages,$terminal,$timestamps,$lineage,$classification,$criticalPath);" [ "$collection",box collection; "$inventory",box inventory; "$attempts",box attempts; "$jobPages",box jobPages; "$terminal",box terminal; "$timestamps",box timestamps; "$lineage",box lineage; "$classification",box classification; "$criticalPath",box criticalPath ]
        | TelemetryStore.CiPopulationAdmission(collection,repository,pr,baseRef,baseSha,head,witness) ->
            run "INSERT INTO ci_population_admissions VALUES($identity,$item,$collection,$repository,$pr,$baseRef,$baseSha,$head,$witness,$revision);" [ "$collection",box collection; "$repository",box repository; "$pr",box pr; "$baseRef",box baseRef; "$baseSha",box baseSha; "$head",box head; "$witness",box witness; "$revision",box fact.Revision ]
        | TelemetryStore.CiCheck(repository,checkId,name,app,status,conclusion,started,completed) ->
            run "INSERT INTO ci_check_runs VALUES($identity,$item,$repository,$check,$name,$app,$status,$conclusion,$started,$completed,$revision);" [ "$repository",box repository; "$check",box checkId; "$name",box name; "$app",optional app; "$status",box status; "$conclusion",optional conclusion; "$started",optional started; "$completed",optional completed; "$revision",box fact.Revision ]
        | TelemetryStore.CiPopulationCoverage(collection,actions,checks,attempts,jobs,terminal,timestamps,continuation,externalChecks,gaps) ->
            run "INSERT INTO ci_population_coverage VALUES($identity,$item,$collection,$actions,$checks,$attempts,$jobs,$terminal,$timestamps,$continuation,$external,$gaps,$revision);" [ "$collection",box collection; "$actions",box actions; "$checks",box checks; "$attempts",box attempts; "$jobs",box jobs; "$terminal",box terminal; "$timestamps",box timestamps; "$continuation",box continuation; "$external",box externalChecks; "$gaps",box gaps; "$revision",box fact.Revision ]
        | TelemetryStore.BudgetPopulation(original,state,sourceKind,sourceRef) ->
            run "INSERT INTO budget_population_facts VALUES($identity,$item,$original,$state,$sourceKind,$sourceRef,$revision);" [ "$original",box original; "$state",box state; "$sourceKind",box sourceKind; "$sourceRef",box sourceRef; "$revision",box fact.Revision ]
        | TelemetryStore.BudgetAttribution(dimension,provider,scope,numerator,denominator,coverage,attribution,sourceKind,sourceRef) ->
            run "INSERT INTO budget_attribution_facts VALUES($identity,$item,$dimension,$provider,$scope,$numerator,$denominator,$coverage,$attribution,$sourceKind,$sourceRef,$revision); INSERT INTO budget_shared_cost_refs VALUES($identity,$item,$sourceRef,$dimension,$provider,$scope);" [ "$dimension",box dimension; "$provider",box provider; "$scope",box scope; "$numerator",optional numerator; "$denominator",optional denominator; "$coverage",box coverage; "$attribution",box attribution; "$sourceKind",box sourceKind; "$sourceRef",box sourceRef; "$revision",box fact.Revision ]
        | TelemetryStore.BudgetInterval(dimension,classification,startAt,endAt,witnessed,sourceKind,sourceRef) ->
            run "INSERT INTO budget_interval_facts VALUES($identity,$item,$dimension,$classification,$start,$end,$witnessed,$sourceKind,$sourceRef,$revision); INSERT INTO budget_shared_cost_refs VALUES($identity,$item,$sourceRef,$dimension,'interval','interval');" [ "$dimension",box dimension; "$classification",box classification; "$start",box startAt; "$end",box endAt; "$witnessed",box (if witnessed then 1 else 0); "$sourceKind",box sourceKind; "$sourceRef",box sourceRef; "$revision",box fact.Revision ]
        | TelemetryStore.BudgetIntervention(intervention,transition,sequence,result,coverage,sourceRef) ->
            run "INSERT INTO budget_intervention_facts VALUES($identity,$item,$intervention,$transition,$sequence,$result,$coverage,$sourceRef,$revision);" [ "$intervention",box intervention; "$transition",box transition; "$sequence",box sequence; "$result",box result; "$coverage",box coverage; "$sourceRef",box sourceRef; "$revision",box fact.Revision ]
        | TelemetryStore.OperationalActivation(activation,scope,runtime,activatedAt,clock,lateAfter) ->
            run "INSERT INTO operational_activations VALUES($identity,$item,$activation,$scope,$runtime,$activatedAt,$clock,$lateAfter,$revision);" [ "$activation",box activation; "$scope",box scope; "$runtime",box runtime; "$activatedAt",box activatedAt; "$clock",box clock; "$lateAfter",box lateAfter; "$revision",box fact.Revision ]
        | TelemetryStore.ExpectedDispatch(dispatch,activation,relation,parent,runtime,expectedAt,clock) ->
            run "INSERT INTO expected_dispatches VALUES($identity,$item,$dispatch,$activation,$relation,$parent,$runtime,$expectedAt,$clock,$revision);" [ "$dispatch",box dispatch; "$activation",box activation; "$relation",box relation; "$parent",optional parent; "$runtime",box runtime; "$expectedAt",box expectedAt; "$clock",box clock; "$revision",box fact.Revision ]
        | TelemetryStore.InvocationLineage(dispatch,invocation,relation,parent,root,runtime) ->
            run "INSERT INTO invocation_lineage VALUES($identity,$item,$dispatch,$invocation,$relation,$parent,$root,$runtime,$revision);" [ "$dispatch",box dispatch; "$invocation",box invocation; "$relation",box relation; "$parent",optional parent; "$root",box root; "$runtime",box runtime; "$revision",box fact.Revision ]
        | TelemetryStore.EventTime(invocation,event,occurred,occurredClock,observed,observedClock) ->
            run "INSERT INTO operational_event_times VALUES($identity,$item,$invocation,$event,$occurred,$occurredClock,$observed,$observedClock,$revision);" [ "$invocation",box invocation; "$event",box event; "$occurred",optional occurred; "$occurredClock",optional occurredClock; "$observed",optional observed; "$observedClock",optional observedClock; "$revision",box fact.Revision ]

        match fact.ItemId, fact.Payload with
        | Some item, (TelemetryStore.BudgetPopulation _ | TelemetryStore.BudgetAttribution _ | TelemetryStore.BudgetInterval _ | TelemetryStore.BudgetIntervention _ | TelemetryStore.RuntimeGap _ | TelemetryStore.CiCoverage _) ->
            use dirty = connection.CreateCommand()
            dirty.CommandText <- "INSERT INTO budget_dirty_items(item_id) VALUES($item) ON CONFLICT(item_id) DO NOTHING;"
            parameter dirty "$item" item
            dirty.ExecuteNonQuery() |> ignore
        | _ -> ()

    let private budgetReevaluate (connection: SqliteConnection) =
        let scalarInt sql parameters =
            use command = connection.CreateCommand()
            command.CommandText <- sql
            parameters |> List.iter (fun (name,value) -> parameter command name value)
            Convert.ToInt64(command.ExecuteScalar())
        let scalarOptionalText sql parameters =
            use command = connection.CreateCommand()
            command.CommandText <- sql
            parameters |> List.iter (fun (name,value) -> parameter command name value)
            let value = command.ExecuteScalar()
            if isNull value || value = box DBNull.Value then None else Some(string value)
        let currentEpoch () = scalarText connection "SELECT epoch_id FROM budget_epochs WHERE state='open';"
        let dirtyItems =
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT item_id FROM budget_dirty_items ORDER BY item_id LIMIT 32;"
            use reader = command.ExecuteReader()
            let values = ResizeArray<string>()
            while reader.Read() do values.Add(reader.GetString 0)
            List.ofSeq values
        for item in dirtyItems do
            let itemParameter = [ "$item", box item ]
            let population =
                use command = connection.CreateCommand()
                command.CommandText <- "SELECT original_item_id,state,source_ref FROM budget_population_facts WHERE item_id=$item ORDER BY fact_revision DESC,identity LIMIT 2;"
                parameter command "$item" item
                use reader = command.ExecuteReader()
                let values = ResizeArray<string * string * string>()
                while reader.Read() do values.Add(reader.GetString 0,reader.GetString 1,reader.GetString 2)
                List.ofSeq values
            let completed = population |> List.filter (fun (_,state,_) -> state = "completed")
            let original = completed |> List.tryHead |> Option.map (fun (value,_,_) -> value)
            let membership = scalarOptionalText "SELECT epoch_id FROM budget_epoch_membership WHERE item_id=$item;" itemParameter
            let epoch =
                match membership, original with
                | Some value, _ -> Some value
                | None, Some originalItem ->
                    let value = currentEpoch ()
                    use insert = connection.CreateCommand()
                    insert.CommandText <- "INSERT INTO budget_epoch_membership(epoch_id,item_id,original_item_id) VALUES($epoch,$item,$original);"
                    parameter insert "$epoch" value; parameter insert "$item" item; parameter insert "$original" originalItem
                    insert.ExecuteNonQuery() |> ignore
                    Some value
                | None, None -> None
            let references = scalarInt "SELECT count(*) FROM budget_shared_cost_refs WHERE item_id=$item;" itemParameter
            let runtimeGap = scalarInt "SELECT count(*) FROM runtime_gaps WHERE item_id=$item;" itemParameter > 0L
            let ciIncomplete =
                use command = connection.CreateCommand()
                command.CommandText <- "SELECT inventory,attempts,job_pages,terminal,timestamps,lineage,classification FROM ci_coverage WHERE item_id=$item ORDER BY rowid DESC LIMIT 1;"
                parameter command "$item" item
                use reader = command.ExecuteReader()
                reader.Read() && [0..6] |> List.exists (fun index -> reader.GetString index <> "complete")
            let attributions =
                use command = connection.CreateCommand()
                command.CommandText <- "SELECT dimension,provider,accounting_scope,numerator,denominator,coverage,attribution,source_kind,source_ref,fact_revision FROM budget_attribution_facts WHERE item_id=$item ORDER BY dimension,provider,accounting_scope,identity LIMIT 4097;"
                parameter command "$item" item
                use reader = command.ExecuteReader()
                let values = ResizeArray<_>()
                while reader.Read() do
                    values.Add(reader.GetString 0,reader.GetString 1,reader.GetString 2,(if reader.IsDBNull 3 then None else Some(reader.GetInt64 3)),(if reader.IsDBNull 4 then None else Some(reader.GetInt64 4)),reader.GetString 5,reader.GetString 6,reader.GetString 7,reader.GetString 8,reader.GetInt64 9)
                List.ofSeq values
            for dimension,provider,scope,suppliedNumerator,denominator,coverage,attribution,sourceKind,sourceRef,factRevision in attributions |> List.truncate 4096 do
                let intervals =
                    use command = connection.CreateCommand()
                    command.CommandText <- "SELECT classification,start_ns,end_ns,witnessed,source_ref FROM budget_interval_facts WHERE item_id=$item AND dimension=$dimension ORDER BY start_ns,end_ns LIMIT 4097;"
                    parameter command "$item" item; parameter command "$dimension" dimension
                    use reader = command.ExecuteReader()
                    let values = ResizeArray<_>()
                    while reader.Read() do values.Add(reader.GetString 0,reader.GetInt64 1,reader.GetInt64 2,reader.GetInt64 3 = 1L,reader.GetString 4)
                    List.ofSeq values
                let intervalOverflow = intervals.Length > 4096
                let intervalNumerator =
                    let administrative = intervals |> List.choose (fun (kind,startAt,endAt,witnessed,_) -> if kind = "administrative" && witnessed then Some({ StartNanoseconds = startAt; EndNanoseconds = endAt } : TelemetryBudget.Interval) else None)
                    let exclusions = intervals |> List.choose (fun (kind,startAt,endAt,witnessed,_) -> if kind <> "administrative" && witnessed then Some({ StartNanoseconds = startAt; EndNanoseconds = endAt } : TelemetryBudget.Interval) else None)
                    if administrative.IsEmpty then suppliedNumerator else TelemetryBudget.subtractNanoseconds administrative exclusions
                let unwitnessed = intervals |> List.exists (fun (_,_,_,witnessed,_) -> not witnessed)
                let usability =
                    if completed.IsEmpty then TelemetryBudget.Unknown "whole-item-incomplete"
                    elif completed |> List.map (fun (value,_,_) -> value) |> List.distinct |> List.length <> 1 then TelemetryBudget.Unknown "population-conflict"
                    elif references > 4096L || intervalOverflow then TelemetryBudget.Unknown "reference-limit"
                    elif coverage = "not-applicable" then TelemetryBudget.NotApplicable "source-not-applicable"
                    elif coverage <> "complete" then TelemetryBudget.Unknown "source-coverage"
                    elif attribution <> "classified" then TelemetryBudget.Unknown "attribution-unknown"
                    elif sourceKind = "runtime" && runtimeGap then TelemetryBudget.Unknown "runtime-gap"
                    elif sourceKind = "ci" && ciIncomplete then TelemetryBudget.Unknown "ci-coverage"
                    elif unwitnessed && not intervals.IsEmpty then TelemetryBudget.Unknown "unwitnessed-critical-path"
                    else
                        match intervalNumerator, denominator with
                        | Some numerator, Some value -> TelemetryBudget.Usable(numerator,value)
                        | _ -> TelemetryBudget.Unknown "missing-measurement"
                let verdict = TelemetryBudget.assess usability
                let verdictText,numeratorValue,denominatorValue,severe,reason =
                    match verdict with
                    | TelemetryBudget.UnknownVerdict why -> "unknown",None,None,false,why
                    | TelemetryBudget.NotApplicableVerdict why -> "not-applicable",intervalNumerator,denominator,false,why
                    | TelemetryBudget.Pass(numerator,value) -> "pass",Some numerator,Some value,false,"within-ceiling"
                    | TelemetryBudget.Breach(numerator,value,isSevere) -> "breach",Some numerator,Some value,isSevere,(if isSevere then "above-severe-threshold" else "above-ceiling")
                match epoch with
                | None -> ()
                | Some epochId ->
                    let sourceDigest = CanonicalJson.sha256(Encoding.UTF8.GetBytes(String.concat "|" [ string factRevision; sourceRef; coverage; attribution; string intervalNumerator; string denominator; string runtimeGap; string ciIncomplete; reason ]))
                    let latestDigest = scalarOptionalText "SELECT source_digest FROM budget_assessment_revisions WHERE item_id=$item AND dimension=$dimension AND provider=$provider AND accounting_scope=$scope ORDER BY assessment_revision DESC LIMIT 1;" [ "$item",box item; "$dimension",box dimension; "$provider",box provider; "$scope",box scope ]
                    let assessmentRevision = scalarInt "SELECT coalesce(max(assessment_revision),0)+1 FROM budget_assessment_revisions WHERE item_id=$item AND dimension=$dimension AND provider=$provider AND accounting_scope=$scope;" [ "$item",box item; "$dimension",box dimension; "$provider",box provider; "$scope",box scope ]
                    let effectiveRevision = if latestDigest = Some sourceDigest then assessmentRevision - 1L else assessmentRevision
                    if latestDigest <> Some sourceDigest then
                        use insert = connection.CreateCommand()
                        insert.CommandText <- "INSERT INTO budget_assessment_revisions VALUES($item,$dimension,$provider,$scope,$revision,$epoch,$verdict,$numerator,$denominator,$severe,$reason,$digest);"
                        [ "$item",box item; "$dimension",box dimension; "$provider",box provider; "$scope",box scope; "$revision",box assessmentRevision; "$epoch",box epochId; "$verdict",box verdictText; "$numerator",numeratorValue |> Option.map box |> Option.defaultValue DBNull.Value; "$denominator",denominatorValue |> Option.map box |> Option.defaultValue DBNull.Value; "$severe",box (if severe then 1 else 0); "$reason",box reason; "$digest",box sourceDigest ] |> List.iter (fun (name,value) -> parameter insert name value)
                        insert.ExecuteNonQuery() |> ignore
                    use breach = connection.CreateCommand()
                    if verdictText = "breach" then
                        breach.CommandText <- "INSERT INTO budget_breaches VALUES($epoch,$item,$dimension,$provider,$scope,$revision,$severe) ON CONFLICT(epoch_id,item_id,dimension,provider,accounting_scope) DO UPDATE SET assessment_revision=excluded.assessment_revision,severe=excluded.severe;"
                        parameter breach "$revision" effectiveRevision; parameter breach "$severe" (if severe then 1 else 0)
                    else
                        breach.CommandText <- "DELETE FROM budget_breaches WHERE epoch_id=$epoch AND item_id=$item AND dimension=$dimension AND provider=$provider AND accounting_scope=$scope;"
                    parameter breach "$epoch" epochId; parameter breach "$item" item; parameter breach "$dimension" dimension; parameter breach "$provider" provider; parameter breach "$scope" scope
                    breach.ExecuteNonQuery() |> ignore
            use clean = connection.CreateCommand()
            clean.CommandText <- "DELETE FROM budget_dirty_items WHERE item_id=$item;"
            parameter clean "$item" item
            clean.ExecuteNonQuery() |> ignore

        let epochId = currentEpoch ()
        let breachCount = scalarInt "SELECT count(DISTINCT item_id) FROM budget_breaches WHERE epoch_id=$epoch;" [ "$epoch",box epochId ]
        let hasSevere = scalarInt "SELECT count(*) FROM budget_breaches WHERE epoch_id=$epoch AND severe=1;" [ "$epoch",box epochId ] > 0L
        let intervention = $"%s{epochId}-intervention"
        if TelemetryBudget.interventionDue (int breachCount) hasSevere then
            let trigger = scalarText connection (if hasSevere then $"SELECT item_id FROM budget_breaches WHERE epoch_id='%s{epochId}' AND severe=1 ORDER BY item_id LIMIT 1;" else $"SELECT item_id FROM budget_breaches WHERE epoch_id='%s{epochId}' ORDER BY item_id LIMIT 1;")
            use insert = connection.CreateCommand()
            insert.CommandText <- "INSERT INTO budget_interventions(intervention_id,epoch_id,state,trigger_item_id,trigger_kind) VALUES($intervention,$epoch,'open',$item,$kind) ON CONFLICT(epoch_id) DO NOTHING;"
            parameter insert "$intervention" intervention; parameter insert "$epoch" epochId; parameter insert "$item" trigger; parameter insert "$kind" (if hasSevere then "severe" else "fifteenth-distinct")
            insert.ExecuteNonQuery() |> ignore

        match scalarOptionalText "SELECT intervention_id FROM budget_interventions WHERE epoch_id=$epoch AND state='open';" [ "$epoch",box epochId ] with
        | None -> ()
        | Some openIntervention ->
            use evidence = connection.CreateCommand()
            evidence.CommandText <- "SELECT transition,sequence,result,coverage,source_ref FROM budget_intervention_facts WHERE intervention_id=$intervention ORDER BY sequence,identity;"
            parameter evidence "$intervention" openIntervention
            use reader = evidence.ExecuteReader()
            let values = ResizeArray<_>()
            while reader.Read() do values.Add(reader.GetString 0,reader.GetInt64 1,reader.GetString 2,reader.GetString 3,reader.GetString 4)
            reader.Close()
            let deployed = values |> Seq.filter (fun (transition,_,_,coverage,_) -> transition = "deployed" && coverage = "complete") |> Seq.tryHead
            let verified =
                match deployed with
                | None -> None
                | Some(_,deployedSequence,_,_,_) -> values |> Seq.tryFind (fun (transition,sequence,result,coverage,_) -> transition = "verified" && sequence > deployedSequence && result = "improved" && coverage = "complete")
            match deployed, verified with
            | Some(_,_,_,_,deployedRef), Some(_,_,_,_,verifiedRef) ->
                use close = connection.CreateCommand()
                close.CommandText <- "UPDATE budget_interventions SET state='verified',deployed_ref=$deployed,verified_ref=$verified WHERE intervention_id=$intervention AND state='open'; UPDATE budget_epochs SET state='verified' WHERE epoch_id=$epoch; INSERT INTO budget_epochs(epoch_id,ordinal,state) SELECT 'epoch-' || (ordinal+1),ordinal+1,'open' FROM budget_epochs WHERE epoch_id=$epoch;"
                parameter close "$deployed" deployedRef; parameter close "$verified" verifiedRef; parameter close "$intervention" openIntervention; parameter close "$epoch" epochId
                close.ExecuteNonQuery() |> ignore
            | _ -> ()

    let private ingestBatchLocked root beforeCommit reevaluateBudget (batch: TelemetryStore.Batch) =
            if not (File.Exists(Path.Combine(root, databaseFileName))) then Error [ "telemetry store is not initialized" ] else
            match connect root SqliteOpenMode.ReadWrite with
            | Error errors -> Error errors
            | Ok(connection, engine) ->
                use connection = connection
                try
                    let version = Int32.Parse(scalarText connection "PRAGMA user_version;")
                    if version > currentSchemaVersion then Error [ $"store schema version %d{version} is newer than supported version %d{currentSchemaVersion}" ]
                    elif version <> currentSchemaVersion then Error [ "telemetry store schema requires migration; run telemetry store init" ]
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
                            if reevaluateBudget then budgetReevaluate connection
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
                let mutable reevaluated = false
                for ready in selected do
                    let bytes = try File.ReadAllBytes ready with error -> failures <- error.Message :: failures; Array.empty
                    let parsed = TelemetryStore.parseBatch bytes
                    let nameValid (batch: TelemetryStore.Batch) = Path.GetFileName ready = $"%s{batch.IngestId}.%s{batch.ContentDigest}.ready"
                    match parsed with
                    | Error errors -> quarantine root ready errors; quarantined <- quarantined + 1
                    | Ok batch when not (nameValid batch) -> quarantine root ready [ "ready filename does not match batch identity and digest" ]; quarantined <- quarantined + 1
                    | Ok batch ->
                        match ingestBatchLocked root hooks.BeforeCommit (not reevaluated) batch with
                        | Error errors when errors |> List.exists (fun error -> error.Contains("identity conflict", StringComparison.Ordinal) || error.Contains("UNIQUE constraint failed: budget_", StringComparison.Ordinal) || error.Contains("UNIQUE constraint failed: operational_event_times", StringComparison.Ordinal) || error.Contains("UNIQUE constraint failed: ci_population_", StringComparison.Ordinal) || error.Contains("UNIQUE constraint failed: ci_check_runs", StringComparison.Ordinal)) ->
                            quarantine root ready errors; quarantined <- quarantined + 1
                        | Error errors -> failures <- (String.concat "; " errors) :: failures
                        | Ok result ->
                            reevaluated <- true
                            use doc = JsonDocument.Parse result
                            accepted <- accepted + doc.RootElement.GetProperty("accepted").GetInt32()
                            replayed <- replayed + doc.RootElement.GetProperty("replayed").GetInt32()
                            try hooks.AfterCommitBeforeDelete(); File.Delete ready; fsyncDirectory(Path.GetDirectoryName ready) with error -> failures <- ("committed but ready removal failed: " + error.Message) :: failures
                if not reevaluated then
                    match connect root SqliteOpenMode.ReadWrite with
                    | Error errors -> failures <- String.concat "; " errors :: failures
                    | Ok(connection, _) ->
                        use connection = connection
                        try
                            beginImmediate connection
                            budgetReevaluate connection
                            execute connection "COMMIT;"
                        with error -> rollback connection; failures <- error.Message :: failures
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
        let runtimeScalar sql =
            use command = connection.CreateCommand()
            command.CommandText <- sql
            parameter command "$item" itemId
            Convert.ToInt64(command.ExecuteScalar())
        let runtimeSum column = runtimeScalar $"SELECT coalesce(sum(%s{column}),0) FROM runtime_turn_usage WHERE item_id=$item;"
        let latestCoverage column fallback =
            use command = connection.CreateCommand()
            command.CommandText <- $"SELECT %s{column} FROM coverage_observations WHERE item_id=$item ORDER BY rowid DESC LIMIT 1;"
            parameter command "$item" itemId
            let value = command.ExecuteScalar()
            if isNull value || value = box DBNull.Value then fallback else string value
        let runtimeTurns = runtimeScalar "SELECT count(*) FROM runtime_turn_usage WHERE item_id=$item;"
        let usageCount = count "usage_observations" + runtimeTurns
        let reasoning =
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT CASE WHEN count(*)=count(reasoning) THEN coalesce(sum(reasoning),0) ELSE NULL END FROM (SELECT reasoning FROM usage_observations WHERE item_id=$item UNION ALL SELECT reasoning FROM runtime_turn_usage WHERE item_id=$item);"
            parameter command "$item" itemId
            let value = command.ExecuteScalar()
            if isNull value || value = box DBNull.Value then None else Some(Convert.ToInt64 value)
        { ItemId = itemId; FactCount = count "ingest_facts"; UsageObservations = usageCount; DeliveryObservations = count "delivery_observations"
          Input = sum "input_count" + runtimeSum "input_count"; CachedInput = sum "cached_input" + runtimeSum "cached_input"; CacheWriteInput = sum "cache_write_input"; Output = sum "output_count" + runtimeSum "output_count"; Reasoning = reasoning; Total = sum "total" + runtimeSum "total"
          Admitted = runtimeScalar "SELECT count(*) FROM runtime_admissions WHERE item_id=$item;"
          Started = runtimeScalar "SELECT count(DISTINCT invocation_id) FROM runtime_starts WHERE item_id=$item AND phase='process';"
          Terminal = runtimeScalar "SELECT count(*) FROM runtime_terminals WHERE item_id=$item;"
          RuntimeUsage = runtimeScalar "SELECT count(DISTINCT invocation_id) FROM runtime_turn_usage WHERE item_id=$item;"
          MissingAdmission = runtimeScalar "SELECT count(*) FROM (SELECT invocation_id FROM runtime_starts WHERE item_id=$item UNION SELECT invocation_id FROM runtime_terminals WHERE item_id=$item UNION SELECT invocation_id FROM runtime_turn_usage WHERE item_id=$item) x WHERE NOT EXISTS (SELECT 1 FROM runtime_admissions a WHERE a.invocation_id=x.invocation_id);"
          MissingStart = runtimeScalar "SELECT count(*) FROM runtime_admissions a WHERE item_id=$item AND NOT EXISTS (SELECT 1 FROM runtime_starts s WHERE s.invocation_id=a.invocation_id AND s.phase='process');"
          MissingTerminal = runtimeScalar "SELECT count(*) FROM runtime_admissions a WHERE item_id=$item AND NOT EXISTS (SELECT 1 FROM runtime_terminals t WHERE t.invocation_id=a.invocation_id);"
          MissingUsage = runtimeScalar "SELECT count(*) FROM runtime_admissions a WHERE item_id=$item AND NOT EXISTS (SELECT 1 FROM runtime_turn_usage u WHERE u.invocation_id=a.invocation_id);"
          RecordValidity = latestCoverage "record_validity" "unknown"; JoinIntegrity = latestCoverage "join_integrity" "unknown"; PopulationCoverage = latestCoverage "population_coverage" "unknown"; Qualification = latestCoverage "qualification" "not-evaluated" }

    let summary path assessment itemId =
        match validateRoot path assessment |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly) with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection
            Ok(TelemetryStore.publicJson (readSummary connection itemId))

    type private ActivationRow =
        { Id: string; Runtime: string; ActivatedAt: DateTimeOffset; Clock: string; LateAfterSeconds: int64 }
    type private DispatchRow =
        { Id: string; ActivationId: string; Relation: string; ParentId: string option; Runtime: string; ExpectedAt: DateTimeOffset; Clock: string }
    type private LineageRow =
        { DispatchId: string; InvocationId: string; Relation: string; ParentInvocationId: string option; RootInvocationId: string; Runtime: string }
    type private TimeRow =
        { InvocationId: string
          Event: string
          OccurredAt: DateTimeOffset option
          OccurredClock: string option
          ObservedAt: DateTimeOffset option
          ObservedClock: string option }
    type private ReconciliationRow =
        { DispatchId: string
          InvocationId: string option
          LineageStatus: string
          LineageCode: string
          TimingStatus: string
          TimingCode: string }

    let reconcile path assessment (itemId: string) =
        match validateRoot path assessment |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly) with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection
            try
                let version = Int32.Parse(scalarText connection "PRAGMA user_version;")
                if version <> currentSchemaVersion then Error [ "telemetry store schema requires migration; run telemetry store init" ] else
                let readStringOption (reader: SqliteDataReader) index = if reader.IsDBNull index then None else Some(reader.GetString index)
                let activations =
                    use command = connection.CreateCommand()
                    command.CommandText <- "SELECT activation_id,runtime,activated_at,clock_provenance,late_after_seconds FROM operational_activations WHERE item_id=$item;"
                    parameter command "$item" itemId
                    use reader = command.ExecuteReader()
                    [ while reader.Read() do
                        yield { Id = reader.GetString 0; Runtime = reader.GetString 1; ActivatedAt = DateTimeOffset.Parse(reader.GetString 2); Clock = reader.GetString 3; LateAfterSeconds = reader.GetInt64 4 } ]
                    |> List.map (fun row -> row.Id, row) |> Map.ofList
                let dispatches =
                    use command = connection.CreateCommand()
                    command.CommandText <- "SELECT dispatch_id,activation_id,relation,parent_dispatch_id,runtime,expected_at,clock_provenance FROM expected_dispatches WHERE item_id=$item ORDER BY dispatch_id;"
                    parameter command "$item" itemId
                    use reader = command.ExecuteReader()
                    [ while reader.Read() do
                        yield { Id = reader.GetString 0; ActivationId = reader.GetString 1; Relation = reader.GetString 2; ParentId = readStringOption reader 3; Runtime = reader.GetString 4; ExpectedAt = DateTimeOffset.Parse(reader.GetString 5); Clock = reader.GetString 6 } ]
                let byDispatch = dispatches |> List.map (fun row -> row.Id, row) |> Map.ofList
                let lineages =
                    use command = connection.CreateCommand()
                    command.CommandText <- "SELECT dispatch_id,invocation_id,relation,parent_invocation_id,root_invocation_id,runtime FROM invocation_lineage WHERE item_id=$item ORDER BY rowid;"
                    parameter command "$item" itemId
                    use reader = command.ExecuteReader()
                    [ while reader.Read() do
                        yield { DispatchId = reader.GetString 0; InvocationId = reader.GetString 1; Relation = reader.GetString 2; ParentInvocationId = readStringOption reader 3; RootInvocationId = reader.GetString 4; Runtime = reader.GetString 5 } ]
                let byLineage = lineages |> List.groupBy _.DispatchId |> Map.ofList
                let invocationMultiplicity = lineages |> List.countBy _.InvocationId |> Map.ofList
                let times =
                    use command = connection.CreateCommand()
                    command.CommandText <- "SELECT invocation_id,event,occurred_at,occurred_clock_provenance,observed_at,observed_clock_provenance FROM operational_event_times WHERE item_id=$item ORDER BY rowid;"
                    parameter command "$item" itemId
                    use reader = command.ExecuteReader()
                    [ while reader.Read() do
                        yield
                            { InvocationId = reader.GetString 0
                              Event = reader.GetString 1
                              OccurredAt = readStringOption reader 2 |> Option.map DateTimeOffset.Parse
                              OccurredClock = readStringOption reader 3
                              ObservedAt = readStringOption reader 4 |> Option.map DateTimeOffset.Parse
                              ObservedClock = readStringOption reader 5 } ]
                    |> List.groupBy _.InvocationId |> Map.ofList
                let cyclic dispatch =
                    let rec loop seen current =
                        if Set.contains current seen then true else
                        match Map.tryFind current byDispatch |> Option.bind _.ParentId with
                        | None -> false
                        | Some parent -> loop (Set.add current seen) parent
                    loop Set.empty dispatch.Id
                let oneLineage id = Map.tryFind id byLineage |> Option.bind (function [ value ] -> Some value | _ -> None)
                let reconcileOne dispatch =
                    let rows = Map.tryFind dispatch.Id byLineage |> Option.defaultValue []
                    let invocation = rows |> List.tryHead |> Option.map _.InvocationId
                    let result lineageStatus lineageCode timingStatus timingCode =
                        { DispatchId = dispatch.Id; InvocationId = invocation; LineageStatus = lineageStatus; LineageCode = lineageCode; TimingStatus = timingStatus; TimingCode = timingCode }
                    let timing (activation: ActivationRow) (lineage: LineageRow) =
                        let eventRows = Map.tryFind lineage.InvocationId times |> Option.defaultValue []
                        let grouped = eventRows |> List.groupBy _.Event |> Map.ofList
                        let required = [ "admission"; "start"; "terminal" ]
                        if required |> List.exists (fun event -> Map.tryFind event grouped |> Option.exists (fun rows -> List.length rows > 1)) then "invalid", "event-time-conflict"
                        elif required |> List.exists (fun event -> not (Map.containsKey event grouped)) then "missing", "required-event-missing"
                        else
                            let witnesses = required |> List.map (fun event -> Map.find event grouped |> List.exactlyOne)
                            if witnesses |> List.exists (fun row -> row.OccurredAt.IsNone || row.ObservedAt.IsNone) then "missing", "timestamps-missing"
                            elif witnesses |> List.exists (fun row -> row.OccurredClock.IsNone || row.ObservedClock.IsNone) then "missing", "clock-provenance-missing"
                            elif witnesses |> List.exists (fun row -> row.OccurredClock <> row.ObservedClock) then "invalid", "clock-domain-mismatch"
                            elif witnesses |> List.choose _.OccurredClock |> Set.ofList |> Set.count <> 1 then "invalid", "lifecycle-clock-domain-mismatch"
                            else
                                let complete = witnesses |> List.map (fun row -> row.OccurredAt.Value, row.ObservedAt.Value)
                                if complete |> List.exists (fun (occurred, observed) -> observed < occurred) then "invalid", "event-time-reversed"
                                elif complete |> List.map fst |> List.pairwise |> List.exists (fun (earlier, later) -> later < earlier) then "invalid", "lifecycle-occurrence-order-invalid"
                                elif complete |> List.map snd |> List.pairwise |> List.exists (fun (earlier, later) -> later < earlier) then "invalid", "lifecycle-observation-order-invalid"
                                elif complete |> List.exists (fun (occurred, observed) -> (observed - occurred).TotalSeconds > float activation.LateAfterSeconds) then "late", "observation-late"
                                else "complete", "required-events-complete"
                    match Map.tryFind dispatch.ActivationId activations with
                    | None -> result "unknown" "activation-missing" "not-evaluated" "lineage-unavailable"
                    | Some activation when dispatch.Runtime <> "codex-exec" || activation.Runtime <> "codex-exec" -> result "unsupported" "runtime-unsupported" "not-evaluated" "runtime-unsupported"
                    | Some activation when dispatch.Clock <> activation.Clock -> result "unknown" "activation-clock-domain-mismatch" "not-evaluated" "dispatch-scope-unknown"
                    | Some activation when dispatch.ExpectedAt < activation.ActivatedAt -> result "out-of-scope" "dispatch-predates-activation" "not-evaluated" "dispatch-out-of-scope"
                    | Some _ when cyclic dispatch -> result "invalid" "cyclic-lineage" "not-evaluated" "lineage-invalid"
                    | Some _ when List.length rows > 1 -> result "invalid" "conflicting-identity" "not-evaluated" "lineage-invalid"
                    | Some _ when rows.IsEmpty -> result "unknown" "invocation-missing" "not-evaluated" "lineage-unavailable"
                    | Some activation ->
                        let lineage = List.head rows
                        let duplicateInvocation = Map.tryFind lineage.InvocationId invocationMultiplicity |> Option.defaultValue 0 > 1
                        if duplicateInvocation || lineage.Runtime <> dispatch.Runtime || lineage.Relation <> dispatch.Relation then result "invalid" "conflicting-identity" "not-evaluated" "lineage-invalid"
                        else
                            let parentProblem =
                                match dispatch.Relation, dispatch.ParentId, lineage.ParentInvocationId with
                                | "root", None, None when lineage.RootInvocationId = lineage.InvocationId -> None
                                | "root", _, _ -> Some "conflicting-identity"
                                | ("child" | "follow-up"), Some parentDispatch, Some parentInvocation ->
                                    match oneLineage parentDispatch with
                                    | None -> Some "missing-parent"
                                    | Some parent when parent.InvocationId <> parentInvocation || parent.RootInvocationId <> lineage.RootInvocationId -> Some "conflicting-identity"
                                    | Some _ -> None
                                | _ -> Some "missing-parent"
                            match parentProblem with
                            | Some code -> result (if code = "missing-parent" then "unknown" else "invalid") code "not-evaluated" "lineage-unavailable"
                            | None ->
                                let timingStatus, timingCode = timing activation lineage
                                result "matched" "expected-invocation-match" timingStatus timingCode
                let rows = dispatches |> List.map reconcileOne
                let lineageCount status = rows |> List.filter (fun row -> row.LineageStatus = status) |> List.length
                let timingCount status = rows |> List.filter (fun row -> row.TimingStatus = status) |> List.length
                Ok(JsonSerializer.Serialize
                    {| schema = "fsgg.telemetry.operational-reconciliation/1"; item = itemId
                       scope = "explicit-future-dispatches"; historicalSessionDiscovery = false; supportedRuntimes = [| "codex-exec" |]
                       expected = rows.Length
                       lineageCoverage = {| matched = lineageCount "matched"; unknown = lineageCount "unknown"; invalid = lineageCount "invalid"; unsupported = lineageCount "unsupported"; outOfScope = lineageCount "out-of-scope" |}
                       timingCoverage = {| complete = timingCount "complete"; late = timingCount "late"; missing = timingCount "missing"; invalid = timingCount "invalid"; notEvaluated = timingCount "not-evaluated" |}
                       usageCoverage = "not-evaluated"; terminalOutcomeCoverage = "not-evaluated"
                       reconciliations = rows |> List.map (fun row -> {| dispatchId = row.DispatchId; invocationId = row.InvocationId; lineage = {| status = row.LineageStatus; code = row.LineageCode |}; timing = {| status = row.TimingStatus; code = row.TimingCode |} |}) |> List.toArray |} + "\n")
            with error -> Error [ error.Message ]

    let ciSummary path assessment (itemId: string) =
        match validateRoot path assessment |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly) with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection
            let scalar sql =
                use command = connection.CreateCommand()
                command.CommandText <- sql
                parameter command "$item" itemId
                Convert.ToInt64(command.ExecuteScalar())
            let intervals sql =
                use command = connection.CreateCommand()
                command.CommandText <- sql
                parameter command "$item" itemId
                use reader = command.ExecuteReader()
                let values = ResizeArray<TelemetryCi.Interval>()
                while reader.Read() do
                    let startAt = if reader.IsDBNull 0 then None else Some(reader.GetString 0)
                    let endAt = if reader.IsDBNull 1 then None else Some(reader.GetString 1)
                    TelemetryCi.interval startAt endAt |> Option.iter values.Add
                values |> Seq.toList
            let coverage column =
                use command = connection.CreateCommand()
                command.CommandText <- $"SELECT %s{column} FROM ci_coverage WHERE item_id=$item ORDER BY rowid DESC LIMIT 1;"
                parameter command "$item" itemId
                let value = command.ExecuteScalar()
                if isNull value || value = box DBNull.Value then "unknown" else string value
            let population column fallback =
                use command = connection.CreateCommand()
                command.CommandText <- $"SELECT %s{column} FROM ci_population_coverage WHERE item_id=$item ORDER BY fact_revision DESC LIMIT 1;"
                parameter command "$item" itemId
                let value = command.ExecuteScalar()
                if isNull value || value = box DBNull.Value then fallback else string value
            let jobs = intervals "SELECT started_at,completed_at FROM ci_jobs WHERE item_id=$item;"
            let queues = intervals "SELECT created_at,started_at FROM ci_jobs WHERE item_id=$item;"
            let runner = if jobs.IsEmpty then None else jobs |> List.sumBy (fun value -> int64 (value.EndUtc - value.StartUtc).TotalSeconds) |> Some
            let wall = TelemetryCi.unionSeconds jobs
            let classified classification =
                use command = connection.CreateCommand()
                command.CommandText <- "SELECT started_at,completed_at FROM ci_steps WHERE item_id=$item AND classification=$classification;"
                parameter command "$item" itemId; parameter command "$classification" classification
                use reader = command.ExecuteReader()
                let values = ResizeArray<TelemetryCi.Interval>()
                while reader.Read() do TelemetryCi.interval (if reader.IsDBNull 0 then None else Some(reader.GetString 0)) (if reader.IsDBNull 1 then None else Some(reader.GetString 1)) |> Option.iter values.Add
                values |> Seq.toList |> TelemetryCi.unionSeconds
            Ok(JsonSerializer.Serialize
                {| schema = "fsgg.telemetry.ci-summary/1"; item = itemId
                   runs = scalar "SELECT count(*) FROM (SELECT repository,run_id FROM ci_runs WHERE item_id=$item UNION SELECT repository,run_id FROM ci_jobs WHERE item_id=$item);"
                   attempts = scalar "SELECT count(*) FROM (SELECT repository,run_id,attempt FROM ci_runs WHERE item_id=$item UNION SELECT repository,run_id,attempt FROM ci_jobs WHERE item_id=$item);"
                   jobs = scalar "SELECT count(*) FROM ci_jobs WHERE item_id=$item;"
                   steps = scalar "SELECT count(*) FROM ci_steps WHERE item_id=$item;"
                   runnerSeconds = runner; wallSeconds = wall; queueSeconds = TelemetryCi.unionSeconds queues
                   usefulValidationSeconds = classified "useful-validation"; administrativeSeconds = classified "admin"; necessarySetupSeconds = classified "necessary-setup"; mixedSeconds = classified "mixed"; unclassifiedSeconds = classified "unclassified"
                   monetary = "unknown"; avoidableRerun = "unknown"
                   inventoryCoverage = population "actions" (coverage "inventory"); checkCoverage = population "checks" "unknown"; attemptCoverage = population "attempts" (coverage "attempts"); jobPageCoverage = population "jobs" (coverage "job_pages"); terminalCoverage = population "terminal" (coverage "terminal"); timestampCoverage = population "timestamps" (coverage "timestamps"); continuation = population "continuation" "none"; externalChecks = Int64.Parse(population "external_checks" "0"); populationGaps = population "gaps" "[]"; lineageCoverage = coverage "lineage"; classificationCoverage = coverage "classification"; criticalPathCoverage = coverage "critical_path" |} + "\n")

    let ciPopulationAdmissionExists path assessment (itemId: string) (repository: string) (pullRequest: int) (baseRef: string) (baseSha: string) (head: string) =
        match validateRoot path assessment |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly) with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection
            try
                if Int32.Parse(scalarText connection "PRAGMA user_version;") <> currentSchemaVersion then Error [ "telemetry store schema requires migration; run telemetry store init" ]
                else
                    use command = connection.CreateCommand()
                    command.CommandText <- "SELECT count(*) FROM ci_population_admissions WHERE item_id=$item AND repository=$repo AND pr_number=$pr AND base_ref=$baseRef AND base_sha=$baseSha AND head=$head AND witness='native-pr-head';"
                    parameter command "$item" itemId; parameter command "$repo" repository; parameter command "$pr" pullRequest; parameter command "$baseRef" baseRef; parameter command "$baseSha" baseSha; parameter command "$head" head
                    Ok(Convert.ToInt64(command.ExecuteScalar()) = 1L)
            with error -> Error [ error.Message ]

    let budgetSummary path assessment (itemId: string) =
        match validateRoot path assessment |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly) with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection
            try
                let version = Int32.Parse(scalarText connection "PRAGMA user_version;")
                if version <> currentSchemaVersion then Error [ "telemetry store schema requires migration; run telemetry store init" ] else
                use command = connection.CreateCommand()
                command.CommandText <- "SELECT dimension,provider,accounting_scope,verdict,numerator,denominator,severe,reason,epoch_id FROM budget_assessment_revisions a WHERE item_id=$item AND assessment_revision=(SELECT max(assessment_revision) FROM budget_assessment_revisions b WHERE b.item_id=a.item_id AND b.dimension=a.dimension AND b.provider=a.provider AND b.accounting_scope=a.accounting_scope) ORDER BY dimension,provider,accounting_scope;"
                parameter command "$item" itemId
                use reader = command.ExecuteReader()
                let dimensions = ResizeArray<_>()
                while reader.Read() do
                    dimensions.Add(
                        {| dimension = reader.GetString 0; provider = reader.GetString 1; accountingScope = reader.GetString 2
                           verdict = reader.GetString 3; numerator = if reader.IsDBNull 4 then None else Some(reader.GetInt64 4)
                           denominator = (if reader.IsDBNull 5 then None else Some(reader.GetInt64 5)); severe = (reader.GetInt64 6 = 1L)
                           reason = reader.GetString 7; epoch = reader.GetString 8 |})
                reader.Close()
                use membership = connection.CreateCommand()
                membership.CommandText <- "SELECT epoch_id FROM budget_epoch_membership WHERE item_id=$item;"
                parameter membership "$item" itemId
                let epochValue = membership.ExecuteScalar()
                let epoch = if isNull epochValue || epochValue = box DBNull.Value then None else Some(string epochValue)
                Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.budget-summary/1"; item = itemId; epoch = epoch; dimensions = dimensions.ToArray() |} + "\n")
            with error -> Error [ error.Message ]

    let budgetStatus path assessment =
        match validateRoot path assessment |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly) with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection
            try
                let version = Int32.Parse(scalarText connection "PRAGMA user_version;")
                if version <> currentSchemaVersion then Error [ "telemetry store schema requires migration; run telemetry store init" ] else
                let epoch = scalarText connection "SELECT epoch_id FROM budget_epochs WHERE state='open';"
                let scalar sql =
                    use command = connection.CreateCommand()
                    command.CommandText <- sql
                    Convert.ToInt64(command.ExecuteScalar())
                let intervention =
                    use command = connection.CreateCommand()
                    command.CommandText <- "SELECT state FROM budget_interventions WHERE epoch_id=$epoch;"
                    parameter command "$epoch" epoch
                    let value = command.ExecuteScalar()
                    if isNull value || value = box DBNull.Value then "none" else string value
                Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.budget-status/1"; epoch = epoch; distinctBreaches = scalar $"SELECT count(DISTINCT item_id) FROM budget_breaches WHERE epoch_id='%s{epoch}';"; intervention = intervention; dirtyItems = scalar "SELECT count(*) FROM budget_dirty_items;" |} + "\n")
            with error -> Error [ error.Message ]

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
            | "reconcile" -> match option "--item" args with Some item -> reconcile path assessment item |> output | None -> output(Error [ "--item is required" ])
            | "export" when List.contains "--public" args -> match option "--output" args with Some target -> exportPublic path assessment (option "--item" args) target |> output | _ -> output(Error [ "--output is required" ])
            | "export" -> output(Error [ "only --public export is supported" ])
            | _ -> output(Error [ "action must be status, init, ingest, summary, reconcile, or export" ])

    let runBudget action args =
        match root args with
        | None ->
            if action = "status" then Console.Out.WriteLine("{\"schema\":\"fsgg.telemetry.budget-status/1\",\"status\":\"unconfigured\"}"); 0
            else output(Error [ "store root is not configured; use --store-root or FSGG_TELEMETRY_STORE" ])
        | Some path ->
            let assessment = assessProductionRoot path
            match action with
            | "status" -> budgetStatus path assessment |> output
            | "summary" -> match option "--item" args with Some item -> budgetSummary path assessment item |> output | None -> output(Error [ "--item is required" ])
            | _ -> output(Error [ "action must be status or summary" ])
