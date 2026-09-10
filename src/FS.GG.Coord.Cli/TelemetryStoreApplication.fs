namespace FS.GG.Coord.Cli

open System
open System.Diagnostics
open System.IO
open System.IO.Compression
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Runtime.InteropServices
open Microsoft.Data.Sqlite
open FS.GG.Coord

module TelemetryStoreApplication =
    type DrainHooks = { BeforeCommit: unit -> unit; AfterCommitBeforeDelete: unit -> unit }
    type DashboardSnapshotHooks = { AfterFirstRead: unit -> unit }
    type ScopedDashboardSnapshotHooks = { AfterFirstRead: unit -> unit; ReceiptKeyComputed: unit -> unit }
    let databaseFileName = "telemetry.sqlite3"
    let private minimumEngine = Version(3, 51, 3)
    let private busyMilliseconds = 750
    let private maxDrainBatches = 128
    let private maxDrainBytes = 8L * 1024L * 1024L
    let private maxPendingPerProducer = 128
    let private currentSchemaVersion = 9
    let private gzip (bytes: byte array) =
        use output = new MemoryStream()
        do
            use compressor = new GZipStream(output, CompressionLevel.SmallestSize, true)
            compressor.Write(bytes, 0, bytes.Length)
        output.ToArray()

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
    let private migration7Sql = """
CREATE TABLE native_item_outcomes(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, pr_number INTEGER NOT NULL CHECK(pr_number > 0), base_ref TEXT NOT NULL, base_sha TEXT NOT NULL, head TEXT NOT NULL, outcome TEXT NOT NULL, code_delivery TEXT NOT NULL, merge_commit TEXT, occurred_at TEXT, observed_at TEXT NOT NULL, source_kind TEXT NOT NULL CHECK(source_kind='routine-delivery'), source_ref TEXT NOT NULL UNIQUE, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE INDEX native_item_outcomes_item_observed ON native_item_outcomes(item_id,observed_at);
PRAGMA user_version=7;
"""
    let private migration7Digest = CanonicalJson.sha256(Encoding.UTF8.GetBytes migration7Sql)
    let private migration8Sql = """
CREATE TABLE process_reviews(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, scope TEXT NOT NULL CHECK(scope IN ('attempt','item')), attempt_id TEXT, outcome_synopsis TEXT NOT NULL, went_well TEXT NOT NULL, problems TEXT NOT NULL, avoidable_delay_rework TEXT NOT NULL, process_observations TEXT NOT NULL, remaining_risks TEXT NOT NULL, concrete_improvements TEXT NOT NULL, evidence TEXT NOT NULL, evidence_coverage TEXT NOT NULL CHECK(evidence_coverage IN ('complete','partial','unknown')), population_coverage TEXT NOT NULL CHECK(population_coverage IN ('complete','partial','unknown')), confidence TEXT NOT NULL CHECK(confidence IN ('low','medium','high')), reviewer_model TEXT NOT NULL, reviewer_effort TEXT NOT NULL, reviewed_at TEXT NOT NULL, duration_seconds INTEGER NOT NULL CHECK(duration_seconds BETWEEN 0 AND 86400), fact_revision INTEGER NOT NULL CHECK(fact_revision > 0), CHECK((scope='attempt' AND attempt_id IS NOT NULL) OR (scope='item' AND attempt_id IS NULL))) STRICT;
CREATE UNIQUE INDEX process_review_attempt_subject ON process_reviews(item_id,attempt_id) WHERE scope='attempt';
CREATE UNIQUE INDEX process_review_item_subject ON process_reviews(item_id) WHERE scope='item';
CREATE TABLE activity_spans(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, activity_id TEXT NOT NULL, invocation_id TEXT NOT NULL, attempt_id TEXT NOT NULL, category TEXT NOT NULL CHECK(category IN ('planning','implementation','review','validation','delivery','repair','operations','other','unclassified')), started_at TEXT NOT NULL, ended_at TEXT, clock_provenance TEXT NOT NULL, evidence TEXT NOT NULL, summary TEXT, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(item_id,activity_id)) STRICT;
CREATE INDEX activity_spans_item_attempt ON activity_spans(item_id,attempt_id);
CREATE TABLE activity_usage_attributions(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, usage_identity TEXT NOT NULL UNIQUE, activity_id TEXT, classification TEXT NOT NULL CHECK(classification IN ('direct','mixed','unclassified')), input_count INTEGER NOT NULL CHECK(input_count >= 0), cached_input INTEGER NOT NULL CHECK(cached_input >= 0), output_count INTEGER NOT NULL CHECK(output_count >= 0), reasoning INTEGER, total INTEGER NOT NULL CHECK(total >= 0), fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), CHECK((classification='direct' AND activity_id IS NOT NULL) OR (classification IN ('mixed','unclassified') AND activity_id IS NULL))) STRICT;
CREATE INDEX activity_usage_item ON activity_usage_attributions(item_id);
CREATE TABLE complication_events(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, attempt_id TEXT, activity_id TEXT, trigger TEXT NOT NULL, cause TEXT NOT NULL, occurred_at TEXT NOT NULL, synopsis TEXT NOT NULL, evidence TEXT NOT NULL, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE INDEX complication_events_item ON complication_events(item_id,occurred_at);
PRAGMA user_version=8;
"""
    let private migration8Digest = CanonicalJson.sha256(Encoding.UTF8.GetBytes migration8Sql)
    let private migration9Sql = """
CREATE TABLE receipt_producers(producer TEXT NOT NULL, stream TEXT NOT NULL, PRIMARY KEY(producer,stream)) STRICT;
CREATE TABLE transport_receipts(producer TEXT NOT NULL, batch TEXT NOT NULL, stream TEXT NOT NULL, digest TEXT NOT NULL, payload_bytes INTEGER NOT NULL, state TEXT NOT NULL CHECK(state IN ('durably-received','applied','rejected')), code TEXT, terminal_utc TEXT, PRIMARY KEY(producer,batch)) STRICT;
CREATE INDEX transport_pending ON transport_receipts(state,producer);
PRAGMA user_version=9;
"""
    let private migration9Digest = CanonicalJson.sha256(Encoding.UTF8.GetBytes migration9Sql)
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

    let private isReceiptScoped root =
        if not (File.Exists(Path.Combine(root, databaseFileName))) then Ok false else
        match connect root SqliteOpenMode.ReadOnly with
        | Error _ -> Error [ "storage-unavailable" ]
        | Ok(connection, _) ->
            use connection = connection
            try
                use command = connection.CreateCommand()
                command.CommandText <- "SELECT count(*) FROM store_metadata WHERE key='receiptWorkspace' AND value<>'';"
                Ok(Convert.ToInt64(command.ExecuteScalar()) = 1L)
            with _ -> Error [ "storage-unavailable" ]

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
                                                else
                                                    let afterV6 = Int32.Parse(scalarText connection "PRAGMA user_version;")
                                                    if afterV6 = 6 then
                                                        beginImmediate connection
                                                        try
                                                            execute connection migration7Sql
                                                            use migration = connection.CreateCommand()
                                                            migration.CommandText <- "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(7,$digest,$utc);"
                                                            parameter migration "$digest" migration7Digest
                                                            parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                                            migration.ExecuteNonQuery() |> ignore
                                                            execute connection "COMMIT;"
                                                        with error -> rollback connection; raise error
                                                    if scalarText connection "SELECT digest FROM schema_migrations WHERE version=7;" <> migration7Digest then Error [ "migration checksum mismatch" ]
                                                    else
                                                        let afterV7 = Int32.Parse(scalarText connection "PRAGMA user_version;")
                                                        if afterV7 = 7 then
                                                            beginImmediate connection
                                                            try
                                                                execute connection migration8Sql
                                                                use migration = connection.CreateCommand()
                                                                migration.CommandText <- "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(8,$digest,$utc);"
                                                                parameter migration "$digest" migration8Digest
                                                                parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                                                migration.ExecuteNonQuery() |> ignore
                                                                execute connection "COMMIT;"
                                                            with error -> rollback connection; raise error
                                                        if scalarText connection "SELECT digest FROM schema_migrations WHERE version=8;" <> migration8Digest then Error [ "migration checksum mismatch" ]
                                                        else
                                                            if Int32.Parse(scalarText connection "PRAGMA user_version;") = 8 then
                                                                beginImmediate connection
                                                                try
                                                                    execute connection migration9Sql
                                                                    use migration = connection.CreateCommand()
                                                                    migration.CommandText <- "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(9,$digest,$utc);"
                                                                    parameter migration "$digest" migration9Digest
                                                                    parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                                                    migration.ExecuteNonQuery() |> ignore
                                                                    execute connection "COMMIT;"
                                                                with error -> rollback connection; raise error
                                                            if scalarText connection "SELECT digest FROM schema_migrations WHERE version=9;" <> migration9Digest then Error [ "migration checksum mismatch" ]
                                                            else
                                                                fsyncDirectory root
                                                                fsyncDirectory(Path.GetDirectoryName root)
                                                                Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.store-status/1"; status = "ready"; root = root; database = databaseFileName; schemaVersion = currentSchemaVersion; nativeEngine = engine; journalMode = scalarText connection "PRAGMA journal_mode;"; synchronous = scalarText connection "PRAGMA synchronous;" |} + "\n")
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
                    elif scalarText connection "SELECT digest FROM schema_migrations WHERE version=7;" <> migration7Digest then Error [ "migration checksum mismatch" ]
                    elif scalarText connection "SELECT digest FROM schema_migrations WHERE version=8;" <> migration8Digest then Error [ "migration checksum mismatch" ]
                    elif scalarText connection "SELECT digest FROM schema_migrations WHERE version=9;" <> migration9Digest then Error [ "migration checksum mismatch" ]
                    else
                        let inbox = Path.Combine(root, "inbox")
                        let pending =
                            [inbox; Path.Combine(root,"receipt-inbox")]
                            |> List.sumBy (fun directory -> if Directory.Exists directory then Directory.EnumerateFiles(directory,"*.ready",SearchOption.AllDirectories) |> Seq.truncate 1025 |> Seq.length else 0)
                        Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.store-status/1"; status = "ready"; root = root; database = databaseFileName; schemaVersion = version; nativeEngine = engine; journalMode = scalarText connection "PRAGMA journal_mode;"; pendingBatches = pending |} + "\n")
                with error -> Error [ error.Message ]

    let private deleteTyped (connection: SqliteConnection) identity preservedTable =
        for table in [ "items"; "features"; "attempts"; "parent_child"; "pr_heads"; "usage_observations"; "delivery_observations"; "evidence_observations"; "coverage_observations"; "health_diagnostics"; "runtime_admissions"; "runtime_starts"; "runtime_turn_usage"; "runtime_terminals"; "runtime_gaps"; "ci_bindings"; "ci_pages"; "ci_runs"; "ci_jobs"; "ci_steps"; "ci_coverage"; "ci_population_coverage"; "ci_check_runs"; "ci_population_admissions"; "native_item_outcomes"; "budget_population_facts"; "budget_attribution_facts"; "budget_interval_facts"; "budget_intervention_facts"; "budget_shared_cost_refs"; "operational_activations"; "expected_dispatches"; "invocation_lineage"; "operational_event_times"; "process_reviews"; "activity_spans"; "activity_usage_attributions"; "complication_events" ] |> List.filter (fun table -> Some table <> preservedTable) do
            use command = connection.CreateCommand()
            command.CommandText <- $"DELETE FROM %s{table} WHERE identity=$identity;"
            parameter command "$identity" identity
            command.ExecuteNonQuery() |> ignore
    let private insertTyped (connection: SqliteConnection) (fact: TelemetryStore.Fact) =
        let optional value = value |> Option.map box |> Option.defaultValue DBNull.Value
        let scalarCount sql values =
            use command = connection.CreateCommand()
            command.CommandText <- sql
            values |> List.iter (fun (name, value) -> parameter command name value)
            Convert.ToInt64(command.ExecuteScalar())
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
        | TelemetryStore.CiBinding(collection,repository,head,pr,workflow,feature,attempt,parent,producer,binding) -> run "INSERT INTO ci_bindings VALUES($identity,$item,$collection,$repository,$head,$pr,$workflow,$feature,$attempt,$parent,$producer,$binding) ON CONFLICT(identity) DO UPDATE SET item_id=excluded.item_id,collection_id=excluded.collection_id,repository=excluded.repository,head=excluded.head,pr_number=excluded.pr_number,workflow=excluded.workflow,feature_id=excluded.feature_id,attempt_id=excluded.attempt_id,parent_attempt_id=excluded.parent_attempt_id,producer_stream=excluded.producer_stream,binding=excluded.binding;" [ "$collection",box collection; "$repository",box repository; "$head",box head; "$pr",box pr; "$workflow",box workflow; "$feature",box feature; "$attempt",box attempt; "$parent",optional parent; "$producer",box producer; "$binding",box binding ]
        | TelemetryStore.CiPage(collection,resource,page,count,total) -> run "INSERT INTO ci_pages VALUES($identity,$item,$collection,$resource,$page,$count,$total);" [ "$collection",box collection; "$resource",box resource; "$page",box page; "$count",box count; "$total",box total ]
        | TelemetryStore.CiRun(repository,runId,attempt,workflow,event,head,status,conclusion,created,started,updated) -> run "INSERT INTO ci_runs VALUES($identity,$item,$repository,$run,$attempt,$workflow,$event,$head,$status,$conclusion,$created,$started,$updated);" [ "$repository",box repository; "$run",box runId; "$attempt",box attempt; "$workflow",box workflow; "$event",box event; "$head",box head; "$status",box status; "$conclusion",optional conclusion; "$created",optional created; "$started",optional started; "$updated",optional updated ]
        | TelemetryStore.CiJob(repository,runId,attempt,jobId,name,status,conclusion,created,started,completed) -> run "INSERT INTO ci_jobs VALUES($identity,$item,$repository,$run,$attempt,$job,$name,$status,$conclusion,$created,$started,$completed);" [ "$repository",box repository; "$run",box runId; "$attempt",box attempt; "$job",box jobId; "$name",box name; "$status",box status; "$conclusion",optional conclusion; "$created",optional created; "$started",optional started; "$completed",optional completed ]
        | TelemetryStore.CiStep(repository,runId,attempt,jobId,number,name,status,conclusion,started,completed,classification,rationale) -> run "INSERT INTO ci_steps VALUES($identity,$item,$repository,$run,$attempt,$job,$number,$name,$status,$conclusion,$started,$completed,$classification,$rationale);" [ "$repository",box repository; "$run",box runId; "$attempt",box attempt; "$job",box jobId; "$number",box number; "$name",box name; "$status",box status; "$conclusion",optional conclusion; "$started",optional started; "$completed",optional completed; "$classification",box classification; "$rationale",box rationale ]
        | TelemetryStore.CiCoverage(collection,inventory,attempts,jobPages,terminal,timestamps,lineage,classification,criticalPath) -> run "INSERT INTO ci_coverage VALUES($identity,$item,$collection,$inventory,$attempts,$jobPages,$terminal,$timestamps,$lineage,$classification,$criticalPath);" [ "$collection",box collection; "$inventory",box inventory; "$attempts",box attempts; "$jobPages",box jobPages; "$terminal",box terminal; "$timestamps",box timestamps; "$lineage",box lineage; "$classification",box classification; "$criticalPath",box criticalPath ]
        | TelemetryStore.CiPopulationAdmission(collection,repository,pr,baseRef,baseSha,head,witness) ->
            run "INSERT INTO ci_population_admissions VALUES($identity,$item,$collection,$repository,$pr,$baseRef,$baseSha,$head,$witness,$revision) ON CONFLICT(identity) DO UPDATE SET item_id=excluded.item_id,collection_id=excluded.collection_id,repository=excluded.repository,pr_number=excluded.pr_number,base_ref=excluded.base_ref,base_sha=excluded.base_sha,head=excluded.head,witness=excluded.witness,fact_revision=excluded.fact_revision;" [ "$collection",box collection; "$repository",box repository; "$pr",box pr; "$baseRef",box baseRef; "$baseSha",box baseSha; "$head",box head; "$witness",box witness; "$revision",box fact.Revision ]
        | TelemetryStore.CiCheck(repository,checkId,name,app,status,conclusion,started,completed) ->
            run "INSERT INTO ci_check_runs VALUES($identity,$item,$repository,$check,$name,$app,$status,$conclusion,$started,$completed,$revision);" [ "$repository",box repository; "$check",box checkId; "$name",box name; "$app",optional app; "$status",box status; "$conclusion",optional conclusion; "$started",optional started; "$completed",optional completed; "$revision",box fact.Revision ]
        | TelemetryStore.CiPopulationCoverage(collection,actions,checks,attempts,jobs,terminal,timestamps,continuation,externalChecks,gaps) ->
            run "INSERT INTO ci_population_coverage VALUES($identity,$item,$collection,$actions,$checks,$attempts,$jobs,$terminal,$timestamps,$continuation,$external,$gaps,$revision);" [ "$collection",box collection; "$actions",box actions; "$checks",box checks; "$attempts",box attempts; "$jobs",box jobs; "$terminal",box terminal; "$timestamps",box timestamps; "$continuation",box continuation; "$external",box externalChecks; "$gaps",box gaps; "$revision",box fact.Revision ]
        | TelemetryStore.NativeItemOutcome(repository,pullRequest,baseRef,baseSha,head,outcome,codeDelivery,mergeCommit,occurredAt,observedAt,sourceKind,sourceRef) ->
            run "INSERT INTO native_item_outcomes VALUES($identity,$item,$repository,$pr,$baseRef,$baseSha,$head,$outcome,$codeDelivery,$mergeCommit,$occurred,$observed,$sourceKind,$sourceRef,$revision);" [ "$repository",box repository; "$pr",box pullRequest; "$baseRef",box baseRef; "$baseSha",box baseSha; "$head",box head; "$outcome",box outcome; "$codeDelivery",box codeDelivery; "$mergeCommit",optional mergeCommit; "$occurred",optional occurredAt; "$observed",box observedAt; "$sourceKind",box sourceKind; "$sourceRef",box sourceRef; "$revision",box fact.Revision ]
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
        | TelemetryStore.ProcessReview review ->
            let item = fact.ItemId |> Option.defaultWith (fun () -> invalidOp "process-review requires itemId")
            match review.Scope, review.AttemptId with
            | "attempt", Some attempt ->
                let admitted = scalarCount "SELECT count(*) FROM runtime_admissions WHERE item_id=$item AND attempt_id=$attempt;" [ "$item",box item; "$attempt",box attempt ]
                let settled = scalarCount "SELECT count(*) FROM runtime_admissions a WHERE a.item_id=$item AND a.attempt_id=$attempt AND EXISTS(SELECT 1 FROM runtime_terminals t WHERE t.item_id=a.item_id AND t.invocation_id=a.invocation_id);" [ "$item",box item; "$attempt",box attempt ]
                if admitted = 0L || admitted <> settled then invalidOp "attempt process review requires a fully terminal admitted attempt"
            | "item", None ->
                let expected = scalarCount "SELECT count(*) FROM expected_dispatches WHERE item_id=$item;" [ "$item",box item ]
                let settled = scalarCount "SELECT count(*) FROM expected_dispatches d WHERE d.item_id=$item AND (SELECT count(*) FROM invocation_lineage l WHERE l.item_id=d.item_id AND l.dispatch_id=d.dispatch_id)=1 AND EXISTS(SELECT 1 FROM invocation_lineage l JOIN runtime_terminals t ON t.item_id=l.item_id AND t.invocation_id=l.invocation_id WHERE l.item_id=d.item_id AND l.dispatch_id=d.dispatch_id);" [ "$item",box item ]
                if expected = 0L || expected <> settled then invalidOp "item process review requires the complete expected population to be terminal"
            | _ -> invalidOp "process-review scope is inconsistent"
            run "INSERT INTO process_reviews VALUES($identity,$item,$scope,$attempt,$synopsis,$well,$problems,$delay,$observations,$risks,$improvements,$evidence,$evidenceCoverage,$populationCoverage,$confidence,$model,$effort,$reviewed,$duration,$revision);"
                [ "$scope",box review.Scope; "$attempt",optional review.AttemptId; "$synopsis",box review.OutcomeSynopsis; "$well",box review.WentWell; "$problems",box review.Problems; "$delay",box review.AvoidableDelayOrRework; "$observations",box review.ProcessObservations; "$risks",box review.RemainingRisks; "$improvements",box review.ConcreteImprovements; "$evidence",box review.Evidence; "$evidenceCoverage",box review.EvidenceCoverage; "$populationCoverage",box review.PopulationCoverage; "$confidence",box review.Confidence; "$model",box review.ReviewerModel; "$effort",box review.ReviewerEffort; "$reviewed",box review.ReviewedAt; "$duration",box review.DurationSeconds; "$revision",box fact.Revision ]
        | TelemetryStore.ActivitySpan activity ->
            let item = fact.ItemId |> Option.defaultWith (fun () -> invalidOp "activity-span requires itemId")
            let bound = scalarCount "SELECT count(*) FROM runtime_admissions WHERE item_id=$item AND invocation_id=$invocation AND attempt_id=$attempt;" [ "$item",box item; "$invocation",box activity.InvocationId; "$attempt",box activity.AttemptId ]
            if bound <> 1L then invalidOp "activity span requires one matching admitted invocation and attempt"
            run "INSERT INTO activity_spans VALUES($identity,$item,$activity,$invocation,$attempt,$category,$started,$ended,$clock,$evidence,$summary,$revision);"
                [ "$activity",box activity.ActivityId; "$invocation",box activity.InvocationId; "$attempt",box activity.AttemptId; "$category",box activity.Category; "$started",box activity.StartedAt; "$ended",optional activity.EndedAt; "$clock",box activity.ClockProvenance; "$evidence",box activity.Evidence; "$summary",optional activity.Summary; "$revision",box fact.Revision ]
        | TelemetryStore.ActivityUsageAttribution attribution ->
            let item = fact.ItemId |> Option.defaultWith (fun () -> invalidOp "activity-usage-attribution requires itemId")
            use usage = connection.CreateCommand()
            usage.CommandText <- "SELECT input_count,cached_input,output_count,reasoning,total,invocation_id FROM runtime_turn_usage WHERE identity=$usage AND item_id=$item;"
            parameter usage "$usage" attribution.UsageIdentity; parameter usage "$item" item
            use reader = usage.ExecuteReader()
            if not (reader.Read()) then invalidOp "activity usage attribution requires matching native usage"
            let nativeReasoning = if reader.IsDBNull 3 then None else Some(reader.GetInt64 3)
            let invocation = reader.GetString 5
            if reader.GetInt64 0 <> attribution.Input || reader.GetInt64 1 <> attribution.CachedInput || reader.GetInt64 2 <> attribution.Output || nativeReasoning <> attribution.Reasoning || reader.GetInt64 4 <> attribution.Total then invalidOp "activity usage attribution counters must exactly match native usage"
            reader.Close()
            match attribution.Classification, attribution.ActivityId with
            | "direct", Some activity ->
                if scalarCount "SELECT count(*) FROM activity_spans WHERE item_id=$item AND activity_id=$activity AND invocation_id=$invocation;" [ "$item",box item; "$activity",box activity; "$invocation",box invocation ] <> 1L then invalidOp "direct usage attribution requires an activity on the same invocation"
            | ("mixed" | "unclassified"), None -> ()
            | _ -> invalidOp "activity usage attribution classification is inconsistent"
            run "INSERT INTO activity_usage_attributions VALUES($identity,$item,$usage,$activity,$classification,$input,$cached,$output,$reasoning,$total,$revision);"
                [ "$usage",box attribution.UsageIdentity; "$activity",optional attribution.ActivityId; "$classification",box attribution.Classification; "$input",box attribution.Input; "$cached",box attribution.CachedInput; "$output",box attribution.Output; "$reasoning",optional attribution.Reasoning; "$total",box attribution.Total; "$revision",box fact.Revision ]
        | TelemetryStore.Complication complication ->
            let item = fact.ItemId |> Option.defaultWith (fun () -> invalidOp "complication requires itemId")
            match complication.AttemptId with
            | Some attempt when scalarCount "SELECT count(*) FROM runtime_admissions WHERE item_id=$item AND attempt_id=$attempt;" [ "$item",box item; "$attempt",box attempt ] = 0L -> invalidOp "complication attempt is not admitted for the item"
            | _ -> ()
            match complication.ActivityId with
            | Some activity when scalarCount "SELECT count(*) FROM activity_spans WHERE item_id=$item AND activity_id=$activity;" [ "$item",box item; "$activity",box activity ] <> 1L -> invalidOp "complication activity is not recorded for the item"
            | _ -> ()
            run "INSERT INTO complication_events VALUES($identity,$item,$attempt,$activity,$trigger,$cause,$occurred,$synopsis,$evidence,$revision);"
                [ "$attempt",optional complication.AttemptId; "$activity",optional complication.ActivityId; "$trigger",box complication.Trigger; "$cause",box complication.Cause; "$occurred",box complication.OccurredAt; "$synopsis",box complication.Synopsis; "$evidence",box complication.Evidence; "$revision",box fact.Revision ]

        match fact.ItemId, fact.Payload with
        | Some item, (TelemetryStore.BudgetPopulation _ | TelemetryStore.BudgetAttribution _ | TelemetryStore.BudgetInterval _ | TelemetryStore.BudgetIntervention _
                    | TelemetryStore.RuntimeAdmission _ | TelemetryStore.RuntimeStart _ | TelemetryStore.RuntimeTurnUsage _ | TelemetryStore.RuntimeTerminal _ | TelemetryStore.RuntimeGap _
                    | TelemetryStore.CiBinding _ | TelemetryStore.CiPage _ | TelemetryStore.CiRun _ | TelemetryStore.CiJob _ | TelemetryStore.CiStep _ | TelemetryStore.CiCoverage _
                    | TelemetryStore.CiPopulationAdmission _ | TelemetryStore.CiCheck _ | TelemetryStore.CiPopulationCoverage _ | TelemetryStore.NativeItemOutcome _
                    | TelemetryStore.OperationalActivation _ | TelemetryStore.ExpectedDispatch _ | TelemetryStore.InvocationLineage _ | TelemetryStore.EventTime _
                    | TelemetryStore.ProcessReview _ | TelemetryStore.ActivitySpan _ | TelemetryStore.ActivityUsageAttribution _ | TelemetryStore.Complication _) ->
            use dirty = connection.CreateCommand()
            dirty.CommandText <- "INSERT INTO budget_dirty_items(item_id) VALUES($item) ON CONFLICT(item_id) DO NOTHING;"
            parameter dirty "$item" item
            dirty.ExecuteNonQuery() |> ignore
        | _ -> ()

    let private deriveBudgetInputs (connection: SqliteConnection) item =
        let parameterized sql =
            let command = connection.CreateCommand()
            command.CommandText <- sql
            parameter command "$item" item
            command
        let scalarInt64 sql = use command = parameterized sql in Convert.ToInt64(command.ExecuteScalar())
        let stable suffix = CanonicalJson.sha256(Encoding.UTF8.GetBytes($"%s{item}\u001f%s{suffix}")).Substring(0, 40)
        let optional value = value |> Option.map box |> Option.defaultValue DBNull.Value
        let revision = scalarInt64 "SELECT count(*) FROM ingest_facts WHERE item_id=$item;"
        let latestOutcome =
            use command = parameterized "SELECT outcome,code_delivery,occurred_at,observed_at FROM native_item_outcomes WHERE item_id=$item ORDER BY observed_at DESC,fact_revision DESC,identity DESC LIMIT 1;"
            use reader = command.ExecuteReader()
            if reader.Read() then Some(reader.GetString 0,reader.GetString 1,(if reader.IsDBNull 2 then None else Some(reader.GetString 2)),reader.GetString 3) else None
        match latestOutcome with
        | None -> ()
        | Some(outcome,codeDelivery,outcomeAt,observedAt) ->
            let expected = scalarInt64 "SELECT count(*) FROM expected_dispatches WHERE item_id=$item AND runtime='codex-exec';"
            let settled =
                scalarInt64 """SELECT count(*) FROM expected_dispatches d WHERE d.item_id=$item AND d.runtime='codex-exec'
AND (SELECT count(*) FROM invocation_lineage l WHERE l.item_id=d.item_id AND l.dispatch_id=d.dispatch_id)=1
AND EXISTS(SELECT 1 FROM invocation_lineage l JOIN runtime_terminals t ON t.item_id=l.item_id AND t.invocation_id=l.invocation_id WHERE l.item_id=d.item_id AND l.dispatch_id=d.dispatch_id);"""
            let activation = scalarInt64 "SELECT count(*) FROM operational_activations WHERE item_id=$item AND runtime='codex-exec';" > 0L
            let rootExpected = scalarInt64 "SELECT count(*) FROM expected_dispatches WHERE item_id=$item AND runtime='codex-exec' AND relation='root';" = 1L
            let deliveredComplete = codeDelivery = "delivered" && activation && rootExpected && expected > 0L && settled = expected
            let refusedComplete = outcome = "refused" && settled = expected
            let state = if deliveredComplete || refusedComplete then "completed" else "open"
            let prefix = "derived:" + stable "projection"
            use purge = parameterized """DELETE FROM budget_shared_cost_refs WHERE item_id=$item AND source_ref LIKE 'derived:%';
DELETE FROM budget_interval_facts WHERE item_id=$item AND source_ref LIKE 'derived:%';
DELETE FROM budget_attribution_facts WHERE item_id=$item AND source_ref LIKE 'derived:%';
DELETE FROM budget_population_facts WHERE item_id=$item AND source_ref LIKE 'derived:%';"""
            purge.ExecuteNonQuery() |> ignore
            use population = parameterized "INSERT INTO budget_population_facts VALUES($identity,$item,$item,$state,'native-item',$source,$revision);"
            parameter population "$identity" ("derived-population-" + stable "population")
            parameter population "$state" state; parameter population "$source" (prefix + ":population"); parameter population "$revision" revision
            population.ExecuteNonQuery() |> ignore

            let insertAttribution dimension provider scope numerator denominator coverage attribution sourceKind suffix =
                let sourceRef = prefix + ":" + suffix
                let identity = "derived-attribution-" + stable suffix
                use command = parameterized "INSERT INTO budget_attribution_facts VALUES($identity,$item,$dimension,$provider,$scope,$numerator,$denominator,$coverage,$attribution,$sourceKind,$source,$revision); INSERT INTO budget_shared_cost_refs VALUES($identity,$item,$source,$dimension,$provider,$scope);"
                [ "$identity",box identity; "$dimension",box dimension; "$provider",box provider; "$scope",box scope; "$numerator",optional numerator; "$denominator",optional denominator; "$coverage",box coverage; "$attribution",box attribution; "$sourceKind",box sourceKind; "$source",box sourceRef; "$revision",box revision ] |> List.iter (fun (name,value) -> parameter command name value)
                command.ExecuteNonQuery() |> ignore
            let runtimeIncomplete =
                scalarInt64 "SELECT count(*) FROM runtime_gaps WHERE item_id=$item;" > 0L
                || scalarInt64 "SELECT count(*) FROM runtime_admissions a WHERE a.item_id=$item AND NOT EXISTS(SELECT 1 FROM runtime_terminals t WHERE t.item_id=a.item_id AND t.invocation_id=a.invocation_id);" > 0L
                || scalarInt64 "SELECT count(*) FROM runtime_terminals t WHERE t.item_id=$item AND NOT EXISTS(SELECT 1 FROM runtime_turn_usage u WHERE u.item_id=t.item_id AND u.invocation_id=t.invocation_id);" > 0L
            let runtimeRows =
                use command = parameterized "SELECT coalesce(provider,'unknown'),sum(total) FROM runtime_turn_usage WHERE item_id=$item GROUP BY coalesce(provider,'unknown') ORDER BY 1;"
                use reader = command.ExecuteReader()
                let rows = ResizeArray<_>()
                while reader.Read() do rows.Add(reader.GetString 0,reader.GetInt64 1)
                List.ofSeq rows
            let runtimeRows = if runtimeRows.IsEmpty then [ "unknown",0L ] else runtimeRows
            for provider,total in runtimeRows do
                insertAttribution "model-usage" provider "whole-item" None (if total = 0L then None else Some total) (if runtimeIncomplete then "unknown" else "complete") "unclassified" "runtime" ("runtime:" + provider)
            insertAttribution "owner-effort" "human" "whole-item" None None "unknown" "unclassified" "native-item" "owner-effort"
            insertAttribution "priced-cost" "unknown" "whole-item" None None "unknown" "unclassified" "native-item" "priced-cost"

            let timestampNanoseconds (value: string) =
                match DateTimeOffset.TryParse value with
                | true, parsed -> Some(parsed.ToUnixTimeMilliseconds() * 1_000_000L)
                | _ -> None
            let activationAt =
                use command = parameterized "SELECT activated_at FROM operational_activations WHERE item_id=$item ORDER BY activated_at LIMIT 1;"
                let value = command.ExecuteScalar()
                if isNull value || value = box DBNull.Value then None else timestampNanoseconds (string value)
            let completedAt = outcomeAt |> Option.orElse (Some observedAt) |> Option.bind timestampNanoseconds
            let lead = match activationAt,completedAt with Some first,Some last when last >= first -> Some(last-first) | _ -> None
            insertAttribution "critical-path-delay" "github" "whole-item" None lead "unknown" "unclassified" "ci" "critical-path"
            let jobTotal =
                use command = parameterized "SELECT started_at,completed_at FROM ci_jobs WHERE item_id=$item;"
                use reader = command.ExecuteReader()
                let mutable total = 0L
                while reader.Read() do
                    if not (reader.IsDBNull 0 || reader.IsDBNull 1) then
                        match timestampNanoseconds(reader.GetString 0),timestampNanoseconds(reader.GetString 1) with Some first,Some last when last >= first -> total <- total + last-first | _ -> ()
                total
            let mutable adminTotal = 0L
            let stepValues =
                use steps = parameterized "SELECT identity,classification,started_at,completed_at FROM ci_steps WHERE item_id=$item ORDER BY identity;"
                use stepRows = steps.ExecuteReader()
                let values = ResizeArray<_>()
                while stepRows.Read() do values.Add(stepRows.GetString 0,stepRows.GetString 1,(if stepRows.IsDBNull 2 then None else Some(stepRows.GetString 2)),(if stepRows.IsDBNull 3 then None else Some(stepRows.GetString 3)))
                List.ofSeq values
            for stepIdentity,classification,startedAt,endedAt in stepValues do
                match startedAt |> Option.bind timestampNanoseconds,endedAt |> Option.bind timestampNanoseconds with
                | Some first,Some last when last >= first ->
                    if classification = "admin" then adminTotal <- adminTotal + last-first
                    let budgetClass,witnessed =
                        match classification with
                        | "admin" -> "administrative",false
                        | "useful-validation" -> "useful",false
                        | "necessary-setup" -> "productive",false
                        | _ -> "administrative",false
                    let suffix = "ci-interval:" + stepIdentity
                    let sourceRef = prefix + ":" + suffix
                    let identity = "derived-interval-" + stable suffix
                    use interval = parameterized "INSERT INTO budget_interval_facts VALUES($identity,$item,'critical-path-delay',$classification,$start,$end,$witnessed,'ci',$source,$revision); INSERT INTO budget_shared_cost_refs VALUES($identity,$item,$source,'critical-path-delay','interval','interval');"
                    [ "$identity",box identity; "$classification",box budgetClass; "$start",box first; "$end",box last; "$witnessed",box (if witnessed then 1 else 0); "$source",box sourceRef; "$revision",box revision ] |> List.iter (fun (name,value) -> parameter interval name value)
                    interval.ExecuteNonQuery() |> ignore
                | _ -> ()
            insertAttribution "ci-runner-administration" "github-actions" "diagnostic-only" (Some adminTotal) (if jobTotal = 0L then None else Some jobTotal) "not-applicable" "classified" "ci" "ci-runner-diagnostic"

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
            deriveBudgetInputs connection item
            let itemParameter = [ "$item", box item ]
            let population =
                use command = connection.CreateCommand()
                command.CommandText <- "SELECT original_item_id,state,source_ref FROM budget_population_facts WHERE item_id=$item ORDER BY CASE WHEN source_ref LIKE 'derived:%' THEN 0 ELSE 1 END,fact_revision DESC,identity DESC LIMIT 1;"
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
            let machineDerived = scalarInt "SELECT count(*) FROM native_item_outcomes WHERE item_id=$item;" itemParameter > 0L
            let references = scalarInt (if machineDerived then "SELECT count(*) FROM budget_shared_cost_refs WHERE item_id=$item AND source_ref LIKE 'derived:%';" else "SELECT count(*) FROM budget_shared_cost_refs WHERE item_id=$item;") itemParameter
            let runtimeGap = scalarInt "SELECT count(*) FROM runtime_gaps WHERE item_id=$item;" itemParameter > 0L
            let ciIncomplete =
                use command = connection.CreateCommand()
                command.CommandText <- "SELECT inventory,attempts,job_pages,terminal,timestamps,lineage,classification FROM ci_coverage WHERE item_id=$item ORDER BY rowid DESC LIMIT 1;"
                parameter command "$item" item
                use reader = command.ExecuteReader()
                reader.Read() && [0..6] |> List.exists (fun index -> reader.GetString index <> "complete")
            let attributions =
                use command = connection.CreateCommand()
                command.CommandText <- "SELECT dimension,provider,accounting_scope,numerator,denominator,coverage,attribution,source_kind,source_ref,fact_revision FROM budget_attribution_facts WHERE item_id=$item AND ($derived=0 OR source_ref LIKE 'derived:%') ORDER BY dimension,provider,accounting_scope,identity LIMIT 4097;"
                parameter command "$item" item
                parameter command "$derived" (if machineDerived then 1 else 0)
                use reader = command.ExecuteReader()
                let values = ResizeArray<_>()
                while reader.Read() do
                    values.Add(reader.GetString 0,reader.GetString 1,reader.GetString 2,(if reader.IsDBNull 3 then None else Some(reader.GetInt64 3)),(if reader.IsDBNull 4 then None else Some(reader.GetInt64 4)),reader.GetString 5,reader.GetString 6,reader.GetString 7,reader.GetString 8,reader.GetInt64 9)
                List.ofSeq values
            for dimension,provider,scope,suppliedNumerator,denominator,coverage,attribution,sourceKind,sourceRef,factRevision in attributions |> List.truncate 4096 do
                let intervals =
                    use command = connection.CreateCommand()
                    command.CommandText <- "SELECT classification,start_ns,end_ns,witnessed,source_ref FROM budget_interval_facts WHERE item_id=$item AND dimension=$dimension AND ($derived=0 OR source_ref LIKE 'derived:%') ORDER BY start_ns,end_ns LIMIT 4097;"
                    parameter command "$item" item; parameter command "$dimension" dimension; parameter command "$derived" (if machineDerived then 1 else 0)
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
                    | TelemetryBudget.UnknownVerdict why -> "unknown",intervalNumerator,denominator,false,why
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

    let private ingestBatchWithReceiptLocked root beforeCommit reevaluateBudget finishReceipt (batch: TelemetryStore.Batch) =
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
                                existing.CommandText <- "SELECT kind,content_digest,revision FROM ingest_facts WHERE identity=$identity;"
                                parameter existing "$kind" fact.Kind; parameter existing "$identity" fact.Identity
                                use reader = existing.ExecuteReader()
                                let state = if reader.Read() then Some(reader.GetString(0), reader.GetString(1), reader.GetInt64(2)) else None
                                reader.Close()
                                match state with
                                | Some(_, digest, _) when digest = fact.ContentDigest -> replayed <- replayed + 1L
                                | Some(_, _, revision) when fact.Revision <= revision -> invalidOp $"native fact identity conflict: %s{fact.Kind}/%s{fact.Identity}"
                                | Some(oldKind, oldDigest, revision) ->
                                    use correction = connection.CreateCommand()
                                    correction.CommandText <- "INSERT INTO corrections(kind,identity,old_revision,new_revision,old_digest,new_digest) VALUES($kind,$identity,$old,$new,$oldDigest,$newDigest); UPDATE ingest_facts SET kind=$kind,item_id=$item,revision=$new,content_digest=$newDigest,canonical=$canonical WHERE identity=$identity;"
                                    [ "$kind",box fact.Kind; "$identity",fact.Identity; "$old",revision; "$new",fact.Revision; "$oldDigest",oldDigest; "$newDigest",fact.ContentDigest; "$item",fact.ItemId |> Option.map box |> Option.defaultValue DBNull.Value; "$canonical",fact.Canonical ] |> List.iter (fun (name,value) -> parameter correction name value)
                                    correction.ExecuteNonQuery() |> ignore
                                    let preservedTable =
                                        if oldKind <> fact.Kind then None
                                        elif fact.Kind = "ci-population-admission" then Some "ci_population_admissions"
                                        elif fact.Kind = "ci-binding" then Some "ci_bindings"
                                        else None
                                    deleteTyped connection fact.Identity preservedTable
                                    insertTyped connection fact
                                    accepted <- accepted + 1L
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
                            finishReceipt connection
                            beforeCommit ()
                            execute connection "COMMIT;"
                            Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.ingest-result/1"; ingestId = batch.IngestId; digest = batch.ContentDigest; accepted = accepted; replayed = replayed; cursor = batch.Cursor; nativeEngine = engine |} + "\n")
                        with error -> rollback connection; raise error
                with
                | :? SqliteException as error -> Error(failBusy error)
                | error -> Error [ error.Message ]

    let private ingestBatchLocked root beforeCommit reevaluateBudget batch =
        ingestBatchWithReceiptLocked root beforeCommit reevaluateBudget ignore batch

    let publish path assessment bytes =
        match validateRoot path assessment, TelemetryStore.parseBatch bytes with
        | Error errors, _ | _, Error errors -> Error errors
        | Ok root, Ok batch ->
            try
                if not (File.Exists(Path.Combine(root, databaseFileName))) then Error [ "telemetry store is not initialized" ]
                else
                    match tryWriterLock root with
                    | Error errors -> Error errors
                    | Ok writer ->
                      use writer=writer
                      match isReceiptScoped root with
                      | Error errors -> Error errors
                      | Ok true -> Error [ "scoped-store-requires-receipt-ingestion" ]
                      | Ok false ->
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
        [inbox; Path.Combine(root,"receipt-inbox")]
        |> List.sumBy (fun directory -> if Directory.Exists directory then Directory.EnumerateFiles(directory,"*.ready",SearchOption.AllDirectories) |> Seq.truncate 1025 |> Seq.length else 0)

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
                match isReceiptScoped root with
                | Error errors -> Error errors
                | Ok true -> Error [ "scoped-store-requires-receipt-ingestion" ]
                | Ok false ->
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
                            | Error errors when errors |> List.exists (fun error -> error.Contains("identity conflict", StringComparison.Ordinal) || error.Contains("UNIQUE constraint failed: budget_", StringComparison.Ordinal) || error.Contains("UNIQUE constraint failed: operational_event_times", StringComparison.Ordinal) || error.Contains("UNIQUE constraint failed: ci_population_", StringComparison.Ordinal) || error.Contains("UNIQUE constraint failed: ci_check_runs", StringComparison.Ordinal) || error.Contains("UNIQUE constraint failed: process_reviews", StringComparison.Ordinal) || error.Contains("UNIQUE constraint failed: activity_spans", StringComparison.Ordinal) || error.Contains("UNIQUE constraint failed: activity_usage_attributions", StringComparison.Ordinal) || error.Contains("UNIQUE constraint failed: complication_events", StringComparison.Ordinal)) ->
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

    // A scoped store has exactly one immutable workspace association. Legacy roots remain
    // unassigned: enrollment never infers ownership of pre-existing observations.
    let private receiptCommand (connection: SqliteConnection) sql (values: (string * obj) list) =
        let command = connection.CreateCommand()
        command.CommandText <- sql
        values |> List.iter (fun (name,value) -> parameter command name value)
        command
    let private receiptScalar connection sql values =
        use command = receiptCommand connection sql values
        command.ExecuteScalar()
    let private receiptExecute connection sql values =
        use command = receiptCommand connection sql values
        command.ExecuteNonQuery() |> ignore
    let private receiptWorkspace connection =
        string (receiptScalar connection "SELECT value FROM store_metadata WHERE key='receiptWorkspace';" [])
    let private verifyScopedProvenance (connection:SqliteConnection) (transaction:SqliteTransaction) workspace receiptKeyComputed =
        use workspaceCommand = connection.CreateCommand()
        workspaceCommand.Transaction <- transaction
        workspaceCommand.CommandText <- "SELECT value FROM store_metadata WHERE key='receiptWorkspace';"
        let enrolled = workspaceCommand.ExecuteScalar()
        if isNull enrolled || enrolled = box DBNull.Value || string enrolled <> workspace then Error [ "projection-unavailable" ] else
        let count sql =
            use command=connection.CreateCommand()
            command.Transaction<-transaction;command.CommandText<-sql
            Convert.ToInt64(command.ExecuteScalar())
        let expected=count "SELECT count(*) FROM ingest_batches;"
        let mutable covered=0L
        let mutable cursor=0L
        let mutable complete=false
        let mutable valid=true
        while valid && not complete do
            use pageCommand=connection.CreateCommand()
            pageCommand.Transaction<-transaction
            // Force the rowid range scan. SQLite otherwise prefers transport_pending(state,producer)
            // and builds a temporary ordering tree again for every page, making this proof quadratic.
            pageCommand.CommandText<-"SELECT rowid,producer,batch,digest FROM transport_receipts NOT INDEXED WHERE rowid>$cursor AND state='applied' ORDER BY rowid LIMIT 256;"
            parameter pageCommand "$cursor" cursor
            use reader=pageCommand.ExecuteReader()
            let page=ResizeArray<int64*string*string*string>()
            while reader.Read() do page.Add(reader.GetInt64 0,reader.GetString 1,reader.GetString 2,reader.GetString 3)
            reader.Close()
            if page.Count=0 then complete<-true else
            for rowId,producer,batch,digest in page do
                cursor<-rowId;receiptKeyComputed()
                use lookup=connection.CreateCommand()
                lookup.Transaction<-transaction
                lookup.CommandText<-"SELECT count(*) FROM ingest_batches WHERE ingest_id=$id AND content_digest=$digest;"
                parameter lookup "$id" ("receipt-"+TelemetryReceipt.key producer batch);parameter lookup "$digest" digest
                if Convert.ToInt64(lookup.ExecuteScalar())=1L then covered<-covered+1L else valid<-false
        if valid && covered=expected then Ok () else Error [ "projection-unavailable" ]
    let private receiptAuthorized connection (scope: TelemetryReceipt.Scope) =
        receiptWorkspace connection = scope.Workspace
        && Convert.ToInt64(receiptScalar connection "SELECT count(*) FROM receipt_producers WHERE producer=$p AND stream=$s;" ["$p",box scope.Producer; "$s",box scope.Stream]) = 1L
    let private receiptParameters (envelope: TelemetryReceipt.Envelope) =
        [ "$p",box envelope.Scope.Producer; "$b",box envelope.BatchId ]
    let private receiptPath root producer batch = Path.Combine(root, "receipt-inbox", TelemetryReceipt.key producer batch + ".ready")
    let private receiptLocked path assessment action =
        match validateRoot path assessment with
        | Error errors -> Error errors
        | Ok root ->
            try
                match tryWriterLock root with
                | Error _ -> Error [ "overload" ]
                | Ok writer ->
                    use writer = writer
                    match connect root SqliteOpenMode.ReadWrite with
                    | Error _ -> Error [ "storage-unavailable" ]
                    | Ok(connection, _) ->
                        use connection = connection
                        if scalarText connection "PRAGMA user_version;" <> string currentSchemaVersion then Error [ "unsupported-version" ]
                        elif scalarText connection "PRAGMA journal_mode;" <> "wal" || scalarText connection "SELECT digest FROM schema_migrations WHERE version=9;" <> migration9Digest then Error [ "storage-unavailable" ]
                        else action root connection
            with _ -> Error [ "storage-unavailable" ]

    let provisionReceiptWorkspace path assessment workspaceId =
        if not (TelemetryReceipt.validId workspaceId) then Error [ "invalid-request" ] else
        receiptLocked path assessment (fun root connection ->
            let workspace = receiptWorkspace connection
            if workspace = workspaceId then Ok "{\"schema\":\"fsgg.telemetry.workspace-provision/1\",\"status\":\"already-provisioned\"}\n"
            elif workspace <> "" then Error [ "unauthorized-scope" ]
            elif Convert.ToInt64(receiptScalar connection "SELECT count(*) FROM ingest_batches;" []) > 0L || pendingCount root > 0 then Error [ "legacy-unassigned; select a new prospective store" ]
            else
                beginImmediate connection
                try
                    receiptExecute connection "INSERT INTO store_metadata(key,value) VALUES('receiptWorkspace',$w);" ["$w",box workspaceId]
                    execute connection "COMMIT;"
                    Ok "{\"schema\":\"fsgg.telemetry.workspace-provision/1\",\"status\":\"provisioned\"}\n"
                with error -> rollback connection; raise error)

    let enrollReceiptProducer path assessment (scope: TelemetryReceipt.Scope) =
        if [scope.Workspace; scope.Producer; scope.Stream] |> List.exists (TelemetryReceipt.validId >> not) then Error [ "invalid-request" ] else
        receiptLocked path assessment (fun root connection ->
            let workspace = receiptWorkspace connection
            if workspace <> "" && workspace <> scope.Workspace then Error [ "unauthorized-scope" ]
            elif workspace = "" && (Convert.ToInt64(receiptScalar connection "SELECT count(*) FROM ingest_batches;" []) > 0L || pendingCount root > 0) then Error [ "legacy-unassigned; select a new prospective store" ]
            else
                let existing = Convert.ToInt64(receiptScalar connection "SELECT count(*) FROM receipt_producers WHERE producer=$p;" ["$p",box scope.Producer])
                let producers = Convert.ToInt64(receiptScalar connection "SELECT count(DISTINCT producer) FROM receipt_producers;" [])
                let streams = Convert.ToInt64(receiptScalar connection "SELECT count(*) FROM receipt_producers;" [])
                if (existing = 0L && producers >= 128L) || (not (receiptAuthorized connection scope) && streams >= 1024L) then Error [ "overload" ] else
                beginImmediate connection
                try
                    receiptExecute connection "INSERT OR IGNORE INTO store_metadata(key,value) VALUES('receiptWorkspace',$w);" ["$w",box scope.Workspace]
                    receiptExecute connection "INSERT OR IGNORE INTO receipt_producers(producer,stream) VALUES($p,$s);" ["$p",box scope.Producer; "$s",box scope.Stream]
                    execute connection "COMMIT;"
                    Ok "{\"schema\":\"fsgg.telemetry.enrollment/1\",\"status\":\"enrolled\"}\n"
                with error -> rollback connection; raise error)

    let private receiptRead (root: string) connection (scope: TelemetryReceipt.Scope) batch now =
        use command = receiptCommand connection "SELECT stream,digest,state,code,terminal_utc FROM transport_receipts WHERE producer=$p AND batch=$b;" ["$p",box scope.Producer; "$b",box batch]
        use reader = command.ExecuteReader()
        if not (reader.Read()) then Error [ "receipt-unavailable" ]
        elif reader.GetString(0) <> scope.Stream then Error [ "unauthorized-scope" ]
        else
            let state = reader.GetString(2)
            let recoverable =
                if state <> "durably-received" then true else
                let file = FileInfo(receiptPath root scope.Producer batch)
                if not file.Exists || not (isNull file.LinkTarget) || file.Length > int64 TelemetryReceipt.MaxEnvelopeBytes then false else
                match TelemetryReceipt.parse(File.ReadAllBytes file.FullName) with
                | Ok envelope -> envelope.Scope = scope && envelope.BatchId = batch && envelope.Digest = reader.GetString(1)
                | Error _ -> false
            if not recoverable then Error [ "storage-unavailable" ] else
            let expired = not (reader.IsDBNull 4) && now - DateTimeOffset.Parse(reader.GetString(4), Globalization.CultureInfo.InvariantCulture) >= TimeSpan.FromDays 30.
            let code = if expired || reader.IsDBNull 3 then None else Some(reader.GetString 3)
            Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.receipt/1"; workspaceId = scope.Workspace; producerId = scope.Producer; streamId = scope.Stream; batchId = batch; digest = reader.GetString(1); status = (if expired then "expired" else state); code = code |} + "\n")

    // Recovery scans only bounded, server-owned receipt artifacts. The index can never
    // acknowledge a pending obligation without its recoverable immutable envelope.
    let private recoverReceiptIndex root connection hook =
        let inbox = Path.Combine(root, "receipt-inbox")
        if Directory.Exists inbox then
            for temporary in Directory.EnumerateFiles(inbox, ".*.tmp") do File.Delete temporary
            fsyncDirectory inbox
            let files = Directory.EnumerateFiles(inbox, "*.ready") |> Seq.truncate 1025 |> Seq.toArray
            if files.Length > 1024 then invalidOp "receipt capacity inconsistent"
            // The common path is an already indexed pending obligation. Load that bounded index once
            // instead of reparsing every retained 64 KiB envelope and issuing two queries per file on
            // every one-shot CLI admission. The content digest still verifies every indexed artifact;
            // only an orphan or an interrupted post-commit cleanup needs the full envelope parser.
            let pending = Collections.Generic.Dictionary<string, string * string * string * string>()
            use indexed = receiptCommand connection "SELECT r.producer,r.batch,r.stream,r.digest,p.producer FROM transport_receipts r LEFT JOIN receipt_producers p ON p.producer=r.producer AND p.stream=r.stream WHERE r.state='durably-received';" []
            use indexedReader = indexed.ExecuteReader()
            while indexedReader.Read() do
                if indexedReader.IsDBNull 4 then invalidOp "invalid receipt binding"
                let producer, batch, stream, digest = indexedReader.GetString(0), indexedReader.GetString(1), indexedReader.GetString(2), indexedReader.GetString(3)
                pending.Add(TelemetryReceipt.key producer batch, (producer, batch, stream, digest))
            indexedReader.Close()
            let workspace = receiptWorkspace connection
            if pending.Count > 0 && String.IsNullOrEmpty workspace then invalidOp "invalid receipt binding"
            let seen = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
            for file in files do
                let info = FileInfo file
                if not (isNull info.LinkTarget) || info.Length > int64 TelemetryReceipt.MaxEnvelopeBytes then invalidOp "invalid receipt artifact"
                let key = Path.GetFileNameWithoutExtension file
                match pending.TryGetValue key with
                | true, (producer, batch, stream, digest) ->
                    if file <> receiptPath root producer batch then invalidOp "invalid receipt binding"
                    let bytes = File.ReadAllBytes file
                    let actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData bytes).ToLowerInvariant()
                    if actual <> digest || not (seen.Add key) then invalidOp "invalid receipt artifact"
                    use document = JsonDocument.Parse bytes
                    let envelope = document.RootElement
                    let fields = envelope.EnumerateObject() |> Seq.map _.Name |> Seq.toArray
                    if envelope.ValueKind <> JsonValueKind.Object
                       || fields.Length <> 6 || fields |> Array.distinct |> Array.length <> 6
                       || envelope.GetProperty("schema").GetString() <> "fsgg.telemetry.envelope/1"
                       || envelope.GetProperty("workspaceId").GetString() <> workspace
                       || envelope.GetProperty("producerId").GetString() <> producer
                       || envelope.GetProperty("streamId").GetString() <> stream
                       || envelope.GetProperty("batchId").GetString() <> batch then invalidOp "invalid receipt binding"
                | false, _ ->
                    let envelope = TelemetryReceipt.parse(File.ReadAllBytes file) |> Result.defaultWith (fun _ -> invalidOp "invalid receipt artifact")
                    if file <> receiptPath root envelope.Scope.Producer envelope.BatchId || not (receiptAuthorized connection envelope.Scope) then invalidOp "invalid receipt binding"
                    let parameters = receiptParameters envelope
                    let prior = receiptScalar connection "SELECT digest FROM transport_receipts WHERE producer=$p AND batch=$b;" parameters
                    if not (isNull prior) && string prior <> envelope.Digest then invalidOp "receipt identity conflict"
                    if isNull prior then
                        receiptExecute connection "INSERT INTO transport_receipts(producer,batch,stream,digest,payload_bytes,state) VALUES($p,$b,$s,$d,$n,'durably-received');"
                            (parameters @ ["$s",box envelope.Scope.Stream; "$d",box envelope.Digest; "$n",box info.Length])
                        hook "index-committed"
                    let state = string (receiptScalar connection "SELECT state FROM transport_receipts WHERE producer=$p AND batch=$b;" parameters)
                    if state <> "durably-received" then File.Delete file; fsyncDirectory inbox
            if seen.Count <> pending.Count then invalidOp "missing accepted obligation"
        elif Convert.ToInt64(receiptScalar connection "SELECT count(*) FROM transport_receipts WHERE state='durably-received';" []) <> 0L then invalidOp "missing accepted inbox"

    let submitReceiptWithHook path assessment scope bytes hook =
        match TelemetryReceipt.parse bytes with
        | Error errors -> Error errors
        | Ok envelope ->
            match TelemetryReceipt.authorize scope envelope with
            | Error errors -> Error errors
            | Ok () ->
                receiptLocked path assessment (fun root connection ->
                    if not (receiptAuthorized connection scope) then Error [ "unauthorized-scope" ] else
                    recoverReceiptIndex root connection hook
                    let parameters = receiptParameters envelope
                    let prior = receiptScalar connection "SELECT digest FROM transport_receipts WHERE producer=$p AND batch=$b;" parameters
                    if not (isNull prior) then
                        if string prior <> envelope.Digest then Error [ "identity-conflict" ]
                        else receiptRead root connection scope envelope.BatchId DateTimeOffset.UtcNow
                    else
                        let count sql values = Convert.ToInt64(receiptScalar connection sql values)
                        let size = int64 (Encoding.UTF8.GetByteCount envelope.Canonical)
                        let p = ["$p",box scope.Producer]
                        if count "SELECT count(*) FROM transport_receipts;" [] >= 1000000L
                           || count "SELECT count(*) FROM transport_receipts WHERE state='durably-received';" [] >= 1024L
                           || count "SELECT coalesce(sum(payload_bytes),0) FROM transport_receipts WHERE state='durably-received';" [] + size > 64L*1024L*1024L
                           || count "SELECT count(*) FROM transport_receipts WHERE producer=$p AND state='durably-received';" p >= 128L
                           || count "SELECT coalesce(sum(payload_bytes),0) FROM transport_receipts WHERE producer=$p AND state='durably-received';" p + size > 8L*1024L*1024L then Error [ "overload" ]
                        else
                            let inbox = Path.Combine(root, "receipt-inbox")
                            Directory.CreateDirectory inbox |> ignore
                            File.SetUnixFileMode(inbox, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
                            fsyncDirectory root
                            let target = receiptPath root scope.Producer envelope.BatchId
                            let temporary = Path.Combine(inbox, "." + Guid.NewGuid().ToString("N") + ".tmp")
                            try
                                use stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)
                                File.SetUnixFileMode(temporary, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
                                stream.Write(Encoding.UTF8.GetBytes envelope.Canonical)
                                hook "before-file-sync"
                                stream.Flush true
                                hook "after-file-sync"
                                stream.Close()
                                File.Move(temporary,target,false)
                                hook "after-rename"
                                fsyncDirectory inbox
                                hook "after-directory-sync"
                                recoverReceiptIndex root connection hook
                                receiptRead root connection scope envelope.BatchId DateTimeOffset.UtcNow
                            finally
                                if File.Exists temporary then File.Delete temporary)
    let submitReceipt path assessment scope bytes = submitReceiptWithHook path assessment scope bytes ignore

    let lookupReceipt path assessment (scope: TelemetryReceipt.Scope) batch =
        if not (TelemetryReceipt.validId batch) then Error [ "invalid-request" ] else
        match validateRoot path assessment with
        | Error errors -> Error errors
        | Ok root ->
            try
                match connect root SqliteOpenMode.ReadOnly with
                | Error _ -> Error [ "storage-unavailable" ]
                | Ok(connection,_) ->
                    use connection = connection
                    if scalarText connection "PRAGMA user_version;" <> string currentSchemaVersion then Error [ "unsupported-version" ]
                    elif scalarText connection "SELECT digest FROM schema_migrations WHERE version=9;" <> migration9Digest then Error [ "storage-unavailable" ]
                    elif not (receiptAuthorized connection scope) then Error [ "unauthorized-scope" ]
                    else receiptRead root connection scope batch DateTimeOffset.UtcNow
            with _ -> Error [ "storage-unavailable" ]

    // Host-wide admission sums this read-only census across explicitly enrolled stores.
    // Tuple fields are lifetime identities, pending batches and pending canonical bytes.
    let receiptCapacity path assessment =
        match validateRoot path assessment with
        | Error errors -> Error errors
        | Ok root ->
            try
                match connect root SqliteOpenMode.ReadOnly with
                | Error _ -> Error [ "storage-unavailable" ]
                | Ok(connection,_) ->
                    use connection = connection
                    if scalarText connection "PRAGMA user_version;" <> string currentSchemaVersion then Error [ "unsupported-version" ]
                    elif scalarText connection "SELECT digest FROM schema_migrations WHERE version=9;" <> migration9Digest then Error [ "storage-unavailable" ]
                    else
                        let count sql = Convert.ToInt64(receiptScalar connection sql [])
                        Ok(count "SELECT count(*) FROM transport_receipts;", count "SELECT count(*) FROM transport_receipts WHERE state='durably-received';", count "SELECT coalesce(sum(payload_bytes),0) FROM transport_receipts WHERE state='durably-received';")
            with _ -> Error [ "storage-unavailable" ]

    let recoverReceiptCapacity path assessment =
        receiptLocked path assessment (fun _ connection ->
            recoverReceiptIndex path connection ignore
            let count sql = Convert.ToInt64(receiptScalar connection sql [])
            Ok(count "SELECT count(*) FROM transport_receipts;", count "SELECT count(*) FROM transport_receipts WHERE state='durably-received';", count "SELECT coalesce(sum(payload_bytes),0) FROM transport_receipts WHERE state='durably-received';"))

    let drainReceiptsWithHook path assessment (workspace: string) hook =
        receiptLocked path assessment (fun root connection ->
            if receiptWorkspace connection <> workspace then Error [ "unauthorized-scope" ] else
            recoverReceiptIndex root connection hook
            let cursor = string (receiptScalar connection "SELECT value FROM store_metadata WHERE key='receiptDrainCursor';" [])
            use command = receiptCommand connection "SELECT producer,batch FROM (SELECT producer,batch,row_number() OVER(PARTITION BY producer ORDER BY batch) AS ordinal FROM transport_receipts WHERE state='durably-received') ORDER BY ordinal,CASE WHEN producer>$cursor THEN 0 ELSE 1 END,producer LIMIT 128;" ["$cursor",box cursor]
            use reader = command.ExecuteReader()
            let selected = ResizeArray<string * string>()
            while reader.Read() do selected.Add(reader.GetString 0,reader.GetString 1)
            reader.Close()
            let mutable bytes = 0L
            let mutable applied = 0
            let mutable rejected = 0
            let mutable failure = false
            for producer,batchId in selected do
                let file = receiptPath root producer batchId
                let size = FileInfo(file).Length
                if bytes + size <= maxDrainBytes then
                    bytes <- bytes + size
                    let envelope = TelemetryReceipt.parse(File.ReadAllBytes file) |> Result.defaultWith (fun _ -> invalidOp "invalid receipt artifact")
                    // Only transport identities are adapted. Native fact identities/revisions remain unchanged.
                    let native =
                        { envelope.Batch with IngestId = "receipt-" + envelope.Key
                                              SourceIdentity = TelemetryReceipt.key producer (envelope.Scope.Stream + "\n" + envelope.Batch.SourceIdentity)
                                              ContentDigest = envelope.Digest }
                    let terminal (db: SqliteConnection) state code =
                        receiptExecute db "UPDATE transport_receipts SET state=$state,code=$code,terminal_utc=$utc WHERE producer=$p AND batch=$b;"
                            (["$p",box producer; "$b",box batchId; "$state",box state; "$code",code; "$utc",box (DateTimeOffset.UtcNow.ToString("O"))])
                    let result = ingestBatchWithReceiptLocked root (fun () -> hook "before-application-commit") true (fun db -> terminal db "applied" DBNull.Value) native
                    match result with
                    | Ok _ -> applied <- applied + 1
                    | Error errors when errors |> List.exists (fun error -> error.Contains("conflict",StringComparison.OrdinalIgnoreCase) || error.Contains("constraint",StringComparison.OrdinalIgnoreCase) || error = "invalid-request") ->
                        terminal connection "rejected" (box "semantic-conflict")
                        rejected <- rejected + 1
                    | Error _ -> failure <- true
                    if not failure then
                        hook "after-application-commit"
                        File.Delete file
                        hook "after-cleanup"
                        fsyncDirectory(Path.GetDirectoryName file)
                        receiptExecute connection "INSERT INTO store_metadata(key,value) VALUES('receiptDrainCursor',$p) ON CONFLICT(key) DO UPDATE SET value=excluded.value;" ["$p",box producer]
            if failure then Error [ "storage-unavailable" ]
            else Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.receipt-drain/1"; applied = applied; rejected = rejected |} + "\n"))
    let drainReceipts path assessment workspace = drainReceiptsWithHook path assessment workspace ignore

    let private fileDigest path =
        use stream = File.OpenRead path
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData stream).ToLowerInvariant()
    let private flushFile path =
        use stream = new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.Read,4096,FileOptions.WriteThrough)
        stream.Flush true

    let backupReceiptStore path assessment workspace (outputPath:string) =
        if not (Path.IsPathFullyQualified outputPath) then Error [ "backup output path must be absolute" ]
        elif File.Exists outputPath || Directory.Exists outputPath then Error [ "backup output already exists" ]
        else
            receiptLocked path assessment (fun root connection ->
                if receiptWorkspace connection <> workspace then Error [ "unauthorized-scope" ] else
                recoverReceiptIndex root connection ignore
                use projectionTransaction=connection.BeginTransaction()
                match verifyScopedProvenance connection projectionTransaction workspace ignore with
                | Error errors -> projectionTransaction.Rollback(); Error errors
                | Ok () ->
                projectionTransaction.Rollback()
                let target = Path.GetFullPath outputPath
                let rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + string Path.DirectorySeparatorChar
                if target.StartsWith(rootPrefix,StringComparison.Ordinal) then Error [ "backup must be outside the store root" ] else
                let parent=Path.GetDirectoryName target
                Directory.CreateDirectory parent |> ignore
                let temporary=Path.Combine(parent,"."+Path.GetFileName(target)+"."+Guid.NewGuid().ToString("N")+".tmp")
                try
                    Directory.CreateDirectory temporary |> ignore
                    if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(temporary,UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
                    let databaseTarget=Path.Combine(temporary,databaseFileName)
                    use destination=new SqliteConnection($"Data Source={databaseTarget};Mode=ReadWriteCreate;Pooling=False")
                    destination.Open(); connection.BackupDatabase destination; destination.Close()
                    flushFile databaseTarget
                    let files=ResizeArray<string*string*int64>()
                    files.Add(databaseFileName,fileDigest databaseTarget,FileInfo(databaseTarget).Length)
                    let inbox=Path.Combine(root,"receipt-inbox")
                    if Directory.Exists inbox then
                        let outputInbox=Path.Combine(temporary,"receipt-inbox")
                        Directory.CreateDirectory outputInbox |> ignore
                        for source in Directory.EnumerateFiles(inbox,"*.ready",SearchOption.TopDirectoryOnly) |> Seq.truncate 1025 do
                            let info=FileInfo source
                            if not(isNull info.LinkTarget) || info.Length>int64 TelemetryReceipt.MaxEnvelopeBytes then invalidOp "invalid receipt artifact"
                            let relative=Path.Combine("receipt-inbox",info.Name)
                            let copied=Path.Combine(temporary,relative)
                            File.Copy(source,copied,false)
                            flushFile copied
                            files.Add(relative.Replace(Path.DirectorySeparatorChar,'/'),fileDigest copied,info.Length)
                        fsyncDirectory outputInbox
                    if files.Count>1025 then invalidOp "receipt capacity inconsistent"
                    let manifest=JsonSerializer.Serialize {| schema="fsgg.telemetry.host-backup/1"; storeSchemaVersion=currentSchemaVersion; workspaceId=workspace; files=files |> Seq.map(fun (name,digest,size)->{|path=name;sha256=digest;bytes=size|}) |> Seq.toArray |}
                    let outputManifest=Path.Combine(temporary,"manifest.json")
                    File.WriteAllText(outputManifest,manifest+"\n",UTF8Encoding(false)); flushFile outputManifest
                    fsyncDirectory temporary
                    Directory.Move(temporary,target); fsyncDirectory parent
                    Ok(JsonSerializer.Serialize {| schema="fsgg.telemetry.host-backup-result/1"; workspaceId=workspace; output=target; files=files.Count |}+"\n")
                finally if Directory.Exists temporary then Directory.Delete(temporary,true))

    let restoreReceiptStore (inputPath:string) (path:string) assessment workspace =
        try
            if not(Path.IsPathFullyQualified inputPath) || not(Directory.Exists inputPath) || existingAncestors inputPath |> List.exists(fun entry->not(isNull entry.LinkTarget)) then Error [ "backup input is unavailable" ]
            elif not(Path.IsPathFullyQualified path) || Directory.Exists path || File.Exists path then Error [ "restore target must be a fresh path" ]
            else
                match validateRoot path assessment with
                | Error errors -> Error errors
                | Ok root ->
                    let manifestPath=Path.Combine(inputPath,"manifest.json")
                    if not(File.Exists manifestPath) || FileInfo(manifestPath).Length>1024L*1024L then Error [ "backup-integrity-failed" ] else
                    use document=JsonDocument.Parse(File.ReadAllBytes manifestPath)
                    let top=document.RootElement
                    let topNames=top.EnumerateObject() |> Seq.map _.Name |> Seq.toArray
                    if topNames.Length<>4 || Array.distinct topNames |> Array.length<>4 || Set.ofArray topNames<>set["schema";"storeSchemaVersion";"workspaceId";"files"] || top.GetProperty("schema").GetString()<>"fsgg.telemetry.host-backup/1" || top.GetProperty("storeSchemaVersion").GetInt32()<>currentSchemaVersion || top.GetProperty("workspaceId").GetString()<>workspace then Error [ "backup-incompatible" ] else
                    let files=top.GetProperty("files").EnumerateArray() |> Seq.toArray
                    if files.Length=0 || files.Length>1025 then Error [ "backup-integrity-failed" ] else
                    let validated=ResizeArray<string*string*string*int64>()
                    let paths=Collections.Generic.HashSet<string>(StringComparer.Ordinal)
                    let mutable valid=true
                    let mutable totalBytes=0L
                    for entry in files do
                        let names=entry.EnumerateObject() |> Seq.map _.Name |> Seq.toArray
                        let relative=entry.GetProperty("path").GetString()
                        let expected=entry.GetProperty("sha256").GetString()
                        let bytes=entry.GetProperty("bytes").GetInt64()
                        let source=if isNull relative then "" else Path.GetFullPath(Path.Combine(inputPath,relative))
                        let prefix=Path.GetFullPath(inputPath).TrimEnd(Path.DirectorySeparatorChar)+string Path.DirectorySeparatorChar
                        let canonicalReceipt = not(isNull relative) && relative.StartsWith("receipt-inbox/",StringComparison.Ordinal) && relative.EndsWith(".ready",StringComparison.Ordinal) && relative.Length=14+64+6 && relative.Substring(14,64) |> Seq.forall(fun c->Char.IsAsciiHexDigit c && not(Char.IsLetter(c) && Char.IsUpper(c)))
                        let allowed = relative=databaseFileName || canonicalReceipt
                        if names.Length<>3 || Array.distinct names |> Array.length<>3 || Set.ofArray names<>set["path";"sha256";"bytes"] || not allowed || not(paths.Add relative) || bytes<0L || bytes>4L*1024L*1024L*1024L || source="" || not(source.StartsWith(prefix,StringComparison.Ordinal)) || not(File.Exists source) || not(isNull(FileInfo(source).LinkTarget)) || FileInfo(source).Length<>bytes || fileDigest source<>expected then valid<-false
                        else totalBytes<-totalBytes+bytes; validated.Add(relative,source,expected,bytes)
                    let topEntries=Directory.EnumerateFileSystemEntries(inputPath,"*",SearchOption.TopDirectoryOnly) |> Seq.truncate 4 |> Seq.toArray
                    let linkTarget entry=if Directory.Exists entry then DirectoryInfo(entry).LinkTarget else FileInfo(entry).LinkTarget
                    if topEntries.Length>3 || topEntries |> Array.exists(fun entry->not(isNull(linkTarget entry)) || (Path.GetFileName entry<>"manifest.json" && Path.GetFileName entry<>databaseFileName && Path.GetFileName entry<>"receipt-inbox")) then valid<-false
                    let inboxPath=Path.Combine(inputPath,"receipt-inbox")
                    let inboxEntries=if Directory.Exists inboxPath then Directory.EnumerateFileSystemEntries(inboxPath,"*",SearchOption.TopDirectoryOnly) |> Seq.truncate 1026 |> Seq.toArray else [||]
                    if inboxEntries.Length>1025 || inboxEntries |> Array.exists(fun entry->Directory.Exists entry || not(isNull(FileInfo(entry).LinkTarget))) then valid<-false
                    let actualFiles=Seq.append (topEntries |> Seq.filter File.Exists) inboxEntries |> Seq.map(fun file->Path.GetRelativePath(inputPath,file).Replace(Path.DirectorySeparatorChar,'/')) |> Set.ofSeq
                    let expectedFiles=Set.add "manifest.json" (paths |> Set.ofSeq)
                    if not valid || totalBytes>4L*1024L*1024L*1024L || actualFiles<>expectedFiles || validated |> Seq.filter(fun (relative,_,_,_)->relative=databaseFileName) |> Seq.length <> 1 then Error [ "backup-integrity-failed" ] else
                    let parent=Path.GetDirectoryName root
                    Directory.CreateDirectory parent |> ignore
                    let temporary=Path.Combine(parent,"."+Path.GetFileName(root)+"."+Guid.NewGuid().ToString("N")+".restore")
                    Directory.CreateDirectory temporary |> ignore
                    if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(temporary,UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
                    try
                        for relative,source,expected,bytes in validated do
                            let target=Path.Combine(temporary,relative)
                            Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
                            File.Copy(source,target,false)
                            flushFile target
                            if FileInfo(target).Length<>bytes || fileDigest target<>expected then invalidOp "copied backup changed"
                        if Directory.Exists(Path.Combine(temporary,"receipt-inbox")) then fsyncDirectory(Path.Combine(temporary,"receipt-inbox"))
                        fsyncDirectory temporary
                        match status temporary assessment with
                        | Error errors -> Directory.Delete(temporary,true); Error errors
                        | Ok _ ->
                          match recoverReceiptCapacity temporary assessment with
                          | Error errors -> Directory.Delete(temporary,true); Error errors
                          | Ok _ ->
                            match connect temporary SqliteOpenMode.ReadOnly with
                            | Error errors -> Directory.Delete(temporary,true); Error errors
                            | Ok(restored,_) ->
                                use restored=restored
                                use transaction=restored.BeginTransaction()
                                match verifyScopedProvenance restored transaction workspace ignore with
                                | Error errors -> transaction.Rollback(); Directory.Delete(temporary,true); Error errors
                                | Ok () ->
                                    transaction.Rollback(); Directory.Move(temporary,root); fsyncDirectory parent
                                    Ok(JsonSerializer.Serialize {| schema="fsgg.telemetry.host-restore-result/1"; workspaceId=workspace; root=root; storeSchemaVersion=currentSchemaVersion |}+"\n")
                    with error ->
                        if Directory.Exists temporary then Directory.Delete(temporary,true)
                        raise error
        with _ -> Error [ "backup-integrity-failed" ]

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

    let private readReviews (connection: SqliteConnection) (itemId: string) limit =
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT scope,attempt_id,fact_revision,outcome_synopsis,went_well,problems,avoidable_delay_rework,process_observations,remaining_risks,concrete_improvements,evidence,evidence_coverage,population_coverage,confidence,reviewer_model,reviewer_effort,reviewed_at,duration_seconds FROM process_reviews WHERE item_id=$item ORDER BY CASE scope WHEN 'attempt' THEN 0 ELSE 1 END,coalesce(attempt_id,''),fact_revision DESC LIMIT $limit;"
        parameter command "$item" itemId; parameter command "$limit" limit
        use reader = command.ExecuteReader()
        let rows = ResizeArray<JsonElement>()
        let optional index = if reader.IsDBNull index then None else Some(reader.GetString index)
        let json index = use document = JsonDocument.Parse(reader.GetString index) in document.RootElement.Clone()
        while reader.Read() do
            rows.Add(JsonSerializer.SerializeToElement
                {| scope=reader.GetString 0; attemptId=optional 1; revision=reader.GetInt64 2; outcomeSynopsis=reader.GetString 3
                   wentWell=json 4; problems=json 5; avoidableDelayOrRework=json 6; processObservations=json 7
                   remainingRisks=json 8; concreteImprovements=json 9; evidence=json 10; evidenceCoverage=reader.GetString 11
                   populationCoverage=reader.GetString 12; confidence=reader.GetString 13; reviewerModel=reader.GetString 14
                   reviewerEffort=reader.GetString 15; reviewedAt=reader.GetString 16; durationSeconds=reader.GetInt64 17 |})
        rows.ToArray()

    let reviewSummary path assessment (itemId: string) =
        match validateRoot path assessment |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly) with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection
            try
                let reviews = readReviews connection itemId 129
                let truncated = reviews.Length > 128
                let bounded = if truncated then reviews[..127] else reviews
                Ok(JsonSerializer.Serialize {| schema="fsgg.telemetry.process-review-summary/1"; item=itemId; reviews=bounded; truncated=truncated |} + "\n")
            with error -> Error [ error.Message ]

    let itemDetail path assessment (itemId: string) =
        match validateRoot path assessment |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly) with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection
            try
                let optional (reader: SqliteDataReader) index = if reader.IsDBNull index then None else Some(reader.GetString index)
                let json (reader: SqliteDataReader) index = use document = JsonDocument.Parse(reader.GetString index) in document.RootElement.Clone()
                let activities = ResizeArray<JsonElement>()
                use activity = connection.CreateCommand()
                activity.CommandText <- "SELECT activity_id,invocation_id,attempt_id,category,started_at,ended_at,clock_provenance,evidence,summary,fact_revision FROM activity_spans WHERE item_id=$item ORDER BY started_at,activity_id LIMIT 257;"
                parameter activity "$item" itemId
                use activityReader = activity.ExecuteReader()
                while activityReader.Read() do
                    activities.Add(JsonSerializer.SerializeToElement {| activityId=activityReader.GetString 0; invocationId=activityReader.GetString 1; attemptId=activityReader.GetString 2; category=activityReader.GetString 3; startedAt=activityReader.GetString 4; endedAt=optional activityReader 5; clockProvenance=activityReader.GetString 6; evidence=json activityReader 7; summary=optional activityReader 8; revision=activityReader.GetInt64 9 |})
                activityReader.Close()
                let attributions = ResizeArray<JsonElement>()
                use attribution = connection.CreateCommand()
                attribution.CommandText <- "SELECT usage_identity,activity_id,classification,input_count,cached_input,output_count,reasoning,total,fact_revision FROM activity_usage_attributions WHERE item_id=$item ORDER BY usage_identity LIMIT 257;"
                parameter attribution "$item" itemId
                use attributionReader = attribution.ExecuteReader()
                while attributionReader.Read() do
                    attributions.Add(JsonSerializer.SerializeToElement {| usageIdentity=attributionReader.GetString 0; activityId=optional attributionReader 1; classification=attributionReader.GetString 2; input=attributionReader.GetInt64 3; cachedInput=attributionReader.GetInt64 4; output=attributionReader.GetInt64 5; reasoning=(if attributionReader.IsDBNull 6 then None else Some(attributionReader.GetInt64 6)); total=attributionReader.GetInt64 7; revision=attributionReader.GetInt64 8 |})
                attributionReader.Close()
                let complications = ResizeArray<JsonElement>()
                use complication = connection.CreateCommand()
                complication.CommandText <- "SELECT attempt_id,activity_id,trigger,cause,occurred_at,synopsis,evidence,fact_revision FROM complication_events WHERE item_id=$item ORDER BY occurred_at,identity LIMIT 257;"
                parameter complication "$item" itemId
                use complicationReader = complication.ExecuteReader()
                while complicationReader.Read() do
                    complications.Add(JsonSerializer.SerializeToElement {| attemptId=optional complicationReader 0; activityId=optional complicationReader 1; trigger=complicationReader.GetString 2; cause=complicationReader.GetString 3; occurredAt=complicationReader.GetString 4; synopsis=complicationReader.GetString 5; evidence=json complicationReader 6; revision=complicationReader.GetInt64 7 |})
                complicationReader.Close()
                let accounting classification =
                    use command = connection.CreateCommand()
                    command.CommandText <- "SELECT coalesce(sum(total),0) FROM activity_usage_attributions WHERE item_id=$item AND classification=$classification;"
                    parameter command "$item" itemId; parameter command "$classification" classification
                    Convert.ToInt64(command.ExecuteScalar())
                let missing =
                    use command = connection.CreateCommand()
                    command.CommandText <- "SELECT count(*) FROM runtime_turn_usage u WHERE item_id=$item AND NOT EXISTS(SELECT 1 FROM activity_usage_attributions a WHERE a.item_id=u.item_id AND a.usage_identity=u.identity);"
                    parameter command "$item" itemId
                    Convert.ToInt64(command.ExecuteScalar())
                let nativeTotal =
                    use command = connection.CreateCommand()
                    command.CommandText <- "SELECT coalesce(sum(total),0) FROM runtime_turn_usage WHERE item_id=$item;"
                    parameter command "$item" itemId
                    Convert.ToInt64(command.ExecuteScalar())
                let reviews = readReviews connection itemId 129
                let bounded (values: ResizeArray<JsonElement>) = if values.Count > 256 then (values.ToArray())[..255] else values.ToArray()
                let result = JsonSerializer.Serialize(
                    {| schema="fsgg.telemetry.item-detail/1"; item=itemId
                       activities=bounded activities; activityTruncated=activities.Count > 256
                       usageAttributions=bounded attributions; attributionTruncated=attributions.Count > 256
                       complications=bounded complications; complicationTruncated=complications.Count > 256
                       reviews=(if reviews.Length > 128 then reviews[..127] else reviews); reviewTruncated=reviews.Length > 128
                       accounting={| nativeTotal=nativeTotal; direct=accounting "direct"; mixed=accounting "mixed"; unclassified=accounting "unclassified"; missingAttribution=missing; allocation="native-exact-only" |} |}) + "\n"
                if Encoding.UTF8.GetByteCount result > 1024 * 1024 then Error [ "item detail exceeds 1048576 bytes" ] else Ok result
            with error -> Error [ error.Message ]

    // Dashboard snapshot /2 is intentionally a read model, not another persistence
    // schema.  Every database value below is selected on this one connection while
    // one explicit transaction is open.  Keeping the table vocabulary here makes
    // the engine the sole owner of SQLite and migration knowledge.
    let private dashboardSnapshotBound path assessment (workspaceId: (string*(unit->unit)) option) afterFirstRead (itemId: string option) =
        match validateRoot path assessment |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly |> Result.map (fun value -> root, value)) with
        | Error errors -> Error errors
        | Ok(root, (connection, _)) ->
            use connection = connection
            try
                use transaction = connection.BeginTransaction()
                let version = Int32.Parse(scalarText connection "PRAGMA user_version;")
                let journal = scalarText connection "PRAGMA journal_mode;" |> _.ToLowerInvariant()
                if version <> currentSchemaVersion then Error [ "telemetry store schema requires migration; run telemetry store init" ]
                elif journal <> "wal" then Error [ "telemetry store journal mode must be WAL" ]
                else
                    let projectionAuthorized =
                        match workspaceId with
                        | None -> Ok ()
                        | Some(workspace,_) when not (TelemetryReceipt.validId workspace) -> Error [ "unauthorized-scope" ]
                        | Some(workspace,computed) -> verifyScopedProvenance connection transaction workspace computed
                    match projectionAuthorized with
                    | Error errors -> Error errors
                    | Ok () ->
                    let selectedItems =
                        use command = connection.CreateCommand()
                        command.Transaction <- transaction
                        command.CommandText <-
                            match itemId with
                            | None -> "SELECT item_id FROM (SELECT item_id FROM ingest_facts WHERE item_id IS NOT NULL UNION SELECT item_id FROM budget_population_facts UNION SELECT item_id FROM native_item_outcomes) ORDER BY item_id LIMIT 202;"
                            | Some _ -> "SELECT item_id FROM (SELECT item_id,original_item_id FROM budget_population_facts WHERE item_id=$item OR original_item_id=$item UNION SELECT item_id,item_id AS original_item_id FROM ingest_facts WHERE item_id=$item) ORDER BY item_id LIMIT 202;"
                        itemId |> Option.iter (parameter command "$item")
                        use reader = command.ExecuteReader()
                        [| while reader.Read() do yield reader.GetString 0 |]
                    afterFirstRead()
                    if selectedItems.Length > 200 then Error [ "dashboard snapshot exceeds 200 selected items" ]
                    elif itemId.IsSome && selectedItems.Length = 0 then Error [ "unknown telemetry item" ]
                    else
                        let arrayOfStrings (values: seq<string>) = JsonArray(values |> Seq.map (fun value -> JsonValue.Create(value) :> JsonNode) |> Seq.toArray)
                        let rows (sql: string) =
                            use command = connection.CreateCommand()
                            command.Transaction <- transaction
                            command.CommandText <- sql
                            itemId |> Option.iter (parameter command "$selected")
                            use reader = command.ExecuteReader()
                            let result = JsonArray()
                            while reader.Read() do
                                let row = JsonObject()
                                for index in 0 .. reader.FieldCount - 1 do
                                    let value : JsonNode =
                                        if reader.IsDBNull index then null
                                        else
                                            match reader.GetValue index with
                                            | :? int64 as value -> JsonValue.Create(value)
                                            | :? int as value -> JsonValue.Create(value)
                                            | :? double as value -> JsonValue.Create(value)
                                            | :? (byte array) as value -> JsonValue.Create(Convert.ToBase64String value)
                                            | value -> JsonValue.Create(string value)
                                    row[reader.GetName index] <- value
                                result.Add row
                                if result.Count > 10000 then raise (InvalidOperationException("dashboard snapshot row bound exceeded"))
                            result
                        let whereItem column =
                            match itemId with
                            | None -> ""
                            | Some _ -> $" WHERE %s{column} IN (SELECT item_id FROM budget_population_facts WHERE item_id=$selected OR original_item_id=$selected UNION SELECT $selected)"
                        let itemFilter = whereItem "item_id"
                        let table name order = rows ($"SELECT * FROM %s{name}%s{itemFilter} ORDER BY %s{order} LIMIT 10001;")
                        let summaries = JsonArray()
                        for selected in selectedItems do
                            use document = JsonDocument.Parse(TelemetryStore.publicJson (readSummary connection selected))
                            summaries.Add(JsonNode.Parse(document.RootElement.GetRawText()))
                        let content = JsonObject()
                        let selectionMode = if itemId.IsSome then "item" else "all"
                        content["selection"] <- JsonSerializer.SerializeToNode {| mode = selectionMode; requestedItem = itemId; complete = true; maxItems = 200; maxRowsPerRelation = 10000 |}
                        content["store"] <- JsonSerializer.SerializeToNode {| schemaVersion = version; journalMode = journal |}
                        content["items"] <- arrayOfStrings selectedItems
                        content["summaries"] <- summaries
                        [ "populations", table "budget_population_facts" "item_id,fact_revision DESC,identity DESC"
                          "dirtyItems", table "budget_dirty_items" "item_id"
                          "outcomes", table "native_item_outcomes" "item_id,observed_at DESC,fact_revision DESC,identity DESC"
                          "admissions", table "runtime_admissions" "item_id,invocation_id"
                          "starts", table "runtime_starts" "item_id,invocation_id,phase"
                          "terminals", table "runtime_terminals" "item_id,invocation_id"
                          "expectedDispatches", table "expected_dispatches" "item_id,dispatch_id"
                          "lineage", table "invocation_lineage" "item_id,fact_revision DESC,identity DESC"
                          "times", table "operational_event_times" "item_id,invocation_id,event,fact_revision DESC,identity DESC"
                          "usage", table "runtime_turn_usage" "item_id,identity"
                          "runtimeGaps", table "runtime_gaps" "item_id,identity"
                          "ciRuns", table "ci_runs" "item_id,repository,run_id,attempt"
                          "ciJobs", table "ci_jobs" "item_id,repository,run_id,attempt,job_id"
                          "ciSteps", table "ci_steps" "item_id,repository,run_id,attempt,job_id,number"
                          "ciCoverage", table "ci_coverage" "item_id,rowid"
                          "ciPopulationCoverage", table "ci_population_coverage" "item_id,fact_revision"
                          "budgetAssessments", table "budget_assessment_revisions" "item_id,dimension,provider,accounting_scope,assessment_revision"
                          "budgetMembership", table "budget_epoch_membership" "item_id"
                          "budgetEpochs", rows "SELECT * FROM budget_epochs ORDER BY ordinal LIMIT 10001;"
                          "budgetBreaches", rows "SELECT * FROM budget_breaches ORDER BY epoch_id,item_id LIMIT 10001;"
                          "budgetInterventions", rows "SELECT * FROM budget_interventions ORDER BY epoch_id LIMIT 10001;"
                          "activities", table "activity_spans" "item_id,started_at,activity_id"
                          "activityUsageAttributions", table "activity_usage_attributions" "item_id,usage_identity"
                          "complications", table "complication_events" "item_id,occurred_at,identity"
                          "reviews", table "process_reviews" "item_id,scope,attempt_id,fact_revision" ]
                        |> List.iter (fun (name, value) -> content[name] <- value)
                        let canonical = CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(content.ToJsonString())) |> Result.defaultWith invalidOp
                        let canonicalBytes = Encoding.UTF8.GetBytes canonical
                        if canonicalBytes.Length > 4 * 1024 * 1024 then raise (InvalidOperationException("dashboard canonical snapshot exceeds 4194304 bytes"))
                        let revision = CanonicalJson.sha256 canonicalBytes
                        let envelope = JsonObject()
                        envelope["schema"] <- JsonValue.Create("fsgg.telemetry.item-detail/2")
                        envelope["observedAt"] <- JsonValue.Create(DateTimeOffset.UtcNow.ToString("O"))
                        envelope["revision"] <- JsonValue.Create(revision)
                        envelope["canonicalSnapshotGzip"] <- JsonValue.Create(Convert.ToBase64String(gzip canonicalBytes))
                        envelope["operational"] <-
                            match workspaceId with
                            | None -> JsonSerializer.SerializeToNode {| pendingBatches = pendingCount root; consistency = "observed-outside-database-transaction" |}
                            | Some _ ->
                                let count state =
                                    use command=connection.CreateCommand()
                                    command.Transaction<-transaction
                                    command.CommandText<-"SELECT count(*) FROM transport_receipts WHERE state=$state;"
                                    parameter command "$state" state
                                    Convert.ToInt64(command.ExecuteScalar())
                                JsonSerializer.SerializeToNode {| pendingBatches=count "durably-received";appliedReceipts=count "applied";rejectedReceipts=count "rejected";consistency="database-transaction" |}
                        let result = envelope.ToJsonString(JsonSerializerOptions(WriteIndented = false)) + "\n"
                        transaction.Rollback()
                        if Encoding.UTF8.GetByteCount result > 1024 * 1024 then Error [ "dashboard snapshot exceeds 1048576 bytes" ] else Ok result
            with error -> Error [ error.Message ]

    let dashboardSnapshotWithHooks path assessment (hooks: DashboardSnapshotHooks) itemId =
        dashboardSnapshotBound path assessment None hooks.AfterFirstRead itemId

    let dashboardSnapshot path assessment itemId =
        dashboardSnapshotWithHooks path assessment ({ AfterFirstRead = ignore }: DashboardSnapshotHooks) itemId

    let scopedDashboardSnapshotWithHooks path assessment workspaceId (hooks: ScopedDashboardSnapshotHooks) itemId =
        dashboardSnapshotBound path assessment (Some(workspaceId,hooks.ReceiptKeyComputed)) hooks.AfterFirstRead itemId

    let scopedDashboardSnapshot path assessment workspaceId itemId =
        scopedDashboardSnapshotWithHooks path assessment workspaceId { AfterFirstRead = ignore;ReceiptKeyComputed=ignore } itemId

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

    let budgetHealth path assessment (itemId: string) =
        match validateRoot path assessment |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly) with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection
            try
                if Int32.Parse(scalarText connection "PRAGMA user_version;") <> currentSchemaVersion then Error [ "telemetry store schema requires migration; run telemetry store init" ] else
                let scalar sql =
                    use command = connection.CreateCommand()
                    command.CommandText <- sql; parameter command "$item" itemId
                    Convert.ToInt64(command.ExecuteScalar())
                let text sql fallback =
                    use command = connection.CreateCommand()
                    command.CommandText <- sql; parameter command "$item" itemId
                    let value = command.ExecuteScalar()
                    if isNull value || value = box DBNull.Value then fallback else string value
                let population = text "SELECT state FROM budget_population_facts WHERE item_id=$item ORDER BY CASE WHEN source_ref LIKE 'derived:%' THEN 0 ELSE 1 END,fact_revision DESC,identity DESC LIMIT 1;" "missing"
                let pending = scalar "SELECT count(*) FROM budget_dirty_items WHERE item_id=$item;" > 0L
                let outcomes = scalar "SELECT count(*) FROM native_item_outcomes WHERE item_id=$item;"
                let unknown = scalar "SELECT count(*) FROM budget_assessment_revisions a WHERE item_id=$item AND verdict='unknown' AND assessment_revision=(SELECT max(assessment_revision) FROM budget_assessment_revisions b WHERE b.item_id=a.item_id AND b.dimension=a.dimension AND b.provider=a.provider AND b.accounting_scope=a.accounting_scope);"
                let status = if pending then "pending" elif outcomes = 0L then "missing-outcome" elif population = "completed" then "complete" else "open"
                Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.budget-health/1"; item = itemId; status = status; population = population; unknownDimensions = unknown; dirty = pending |} + "\n")
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

    let runReview action args =
        match root args with
        | None -> output(Error [ "store root is not configured; use --store-root or FSGG_TELEMETRY_STORE" ])
        | Some path ->
            let assessment = assessProductionRoot path
            match action, option "--item" args with
            | "summary", Some item -> reviewSummary path assessment item |> output
            | "summary", None -> output(Error [ "--item is required" ])
            | _ -> output(Error [ "action must be summary" ])

    let runItemDetail args =
        match root args with
        | None -> output(Error [ "store root is not configured; use --store-root or FSGG_TELEMETRY_STORE" ])
        | Some path ->
            match option "--format-version" args, List.contains "--all" args, option "--item" args with
            | None, false, Some item -> itemDetail path (assessProductionRoot path) item |> output
            | Some "1", false, Some item -> itemDetail path (assessProductionRoot path) item |> output
            | Some "2", false, Some item -> dashboardSnapshot path (assessProductionRoot path) (Some item) |> output
            | Some "2", true, None -> dashboardSnapshot path (assessProductionRoot path) None |> output
            | Some version, _, _ when version <> "1" && version <> "2" -> output(Error [ "unsupported item detail format version" ])
            | _, true, Some _ -> output(Error [ "--all and --item are mutually exclusive" ])
            | _ -> output(Error [ "--item is required (or use --format-version 2 --all)" ])
