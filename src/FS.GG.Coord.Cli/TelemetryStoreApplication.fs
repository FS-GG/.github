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
    type DrainHooks =
        {
            BeforeCommit: unit -> unit
            AfterCommitBeforeDelete: unit -> unit
        }

    type DashboardSnapshotHooks = { AfterFirstRead: unit -> unit }

    type ScopedDashboardSnapshotHooks =
        {
            AfterFirstRead: unit -> unit
            ReceiptKeyComputed: unit -> unit
        }

    type NativeCollectorDispatch =
        {
            ItemId: string
            OriginalItemId: string
            InvocationId: string
            RootInvocationId: string
            RequestedModel: string
            RequestedEffort: string
        }

    type NativeDeliveryCandidate =
        {
            Identity: string
            ItemId: string
            Repository: string
            PullRequest: int64
            ExpectedHead: string
            SourceRef: string
            CanonicalFact: string
            FactDigest: string
            ReceiptRole: string option
            ReceiptGrantId: string option
            ReceiptGrantGeneration: int64 option
            ReceiptKey: string option
            ReceiptEnvelopeDigest: string option
            Binding: string
            BindingDigest: string
        }

    type InstalledOriginQuery =
        {
            WorkspaceId: string
            ProducerId: string
            StreamId: string
            Role: string
            GrantId: string
            GrantGeneration: int64
            ManagerReceiptSha256: string
            CapabilityProfileSha256: string
            CapabilityResultSha256: string
            NativeCaptureSha256: string
            NativeVerificationSha256: string
            InstallationSha256: string
        }

    type InstalledOrigin =
        {
            RecordId: string
            Revision: int64
            ObservedAt: string
            ExpiresAt: string
            InstallationSha256: string
            ReceiptKey: string
            EnvelopeDigest: string
        }

    let databaseFileName = "telemetry.sqlite3"
    let private minimumEngine = Version(3, 51, 3)
    let private busyMilliseconds = 750
    let private maxDrainBatches = 128
    let private maxDrainBytes = 8L * 1024L * 1024L
    let private maxPendingPerProducer = 128
    let private currentSchemaVersion = 14

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

    let private migrationSql =
        """
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

    let private migrationDigest =
        CanonicalJson.sha256 (Encoding.UTF8.GetBytes migrationSql)

    let private migration2Sql =
        """
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

    let private migration2Digest =
        CanonicalJson.sha256 (Encoding.UTF8.GetBytes migration2Sql)

    let private migration3Sql =
        """
CREATE TABLE ci_bindings(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, collection_id TEXT NOT NULL UNIQUE, repository TEXT NOT NULL, head TEXT NOT NULL, pr_number INTEGER NOT NULL CHECK(pr_number > 0), workflow TEXT NOT NULL, feature_id TEXT NOT NULL, attempt_id TEXT NOT NULL, parent_attempt_id TEXT, producer_stream TEXT NOT NULL, binding TEXT NOT NULL) STRICT;
CREATE TABLE ci_pages(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, collection_id TEXT NOT NULL REFERENCES ci_bindings(collection_id), resource TEXT NOT NULL, page INTEGER NOT NULL CHECK(page > 0), count INTEGER NOT NULL CHECK(count BETWEEN 0 AND 100), total INTEGER NOT NULL CHECK(total BETWEEN 0 AND 1000), UNIQUE(collection_id,resource,page)) STRICT;
CREATE TABLE ci_runs(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, run_id INTEGER NOT NULL, attempt INTEGER NOT NULL CHECK(attempt > 0), workflow TEXT NOT NULL, event TEXT NOT NULL, head TEXT NOT NULL, status TEXT NOT NULL, conclusion TEXT, created_at TEXT, started_at TEXT, updated_at TEXT, UNIQUE(repository,run_id,attempt)) STRICT;
CREATE TABLE ci_jobs(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, run_id INTEGER NOT NULL, attempt INTEGER NOT NULL CHECK(attempt > 0), job_id INTEGER NOT NULL, name TEXT NOT NULL, status TEXT NOT NULL, conclusion TEXT, created_at TEXT, started_at TEXT, completed_at TEXT, UNIQUE(repository,run_id,attempt,job_id)) STRICT;
CREATE TABLE ci_steps(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, run_id INTEGER NOT NULL, attempt INTEGER NOT NULL CHECK(attempt > 0), job_id INTEGER NOT NULL, number INTEGER NOT NULL CHECK(number >= 0), name TEXT NOT NULL, status TEXT NOT NULL, conclusion TEXT, started_at TEXT, completed_at TEXT, classification TEXT NOT NULL, rationale TEXT NOT NULL, UNIQUE(repository,run_id,attempt,job_id,number)) STRICT;
CREATE TABLE ci_coverage(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, collection_id TEXT NOT NULL REFERENCES ci_bindings(collection_id), inventory TEXT NOT NULL, attempts TEXT NOT NULL, job_pages TEXT NOT NULL, terminal TEXT NOT NULL, timestamps TEXT NOT NULL, lineage TEXT NOT NULL, classification TEXT NOT NULL, critical_path TEXT NOT NULL) STRICT;
PRAGMA user_version=3;
"""

    let private migration3Digest =
        CanonicalJson.sha256 (Encoding.UTF8.GetBytes migration3Sql)

    let private migration4Sql =
        """
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

    let private migration4Digest =
        CanonicalJson.sha256 (Encoding.UTF8.GetBytes migration4Sql)

    let private migration5Sql =
        """
CREATE TABLE operational_activations(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, activation_id TEXT NOT NULL, scope TEXT NOT NULL CHECK(scope='explicit-future-dispatches'), runtime TEXT NOT NULL, activated_at TEXT NOT NULL, clock_provenance TEXT NOT NULL, late_after_seconds INTEGER NOT NULL CHECK(late_after_seconds >= 0), fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(item_id,activation_id)) STRICT;
CREATE TABLE expected_dispatches(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, dispatch_id TEXT NOT NULL, activation_id TEXT NOT NULL, relation TEXT NOT NULL CHECK(relation IN ('root','child','follow-up')), parent_dispatch_id TEXT, runtime TEXT NOT NULL, expected_at TEXT NOT NULL, clock_provenance TEXT NOT NULL, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(item_id,dispatch_id)) STRICT;
CREATE TABLE invocation_lineage(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, dispatch_id TEXT NOT NULL, invocation_id TEXT NOT NULL, relation TEXT NOT NULL CHECK(relation IN ('root','child','follow-up')), parent_invocation_id TEXT, root_invocation_id TEXT NOT NULL, runtime TEXT NOT NULL, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE INDEX invocation_lineage_dispatch ON invocation_lineage(dispatch_id);
CREATE INDEX invocation_lineage_invocation ON invocation_lineage(invocation_id);
CREATE TABLE operational_event_times(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, invocation_id TEXT NOT NULL, event TEXT NOT NULL CHECK(event IN ('admission','start','terminal')), occurred_at TEXT, occurred_clock_provenance TEXT, observed_at TEXT, observed_clock_provenance TEXT, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(item_id,invocation_id,event)) STRICT;
CREATE INDEX operational_event_times_invocation ON operational_event_times(invocation_id);
PRAGMA user_version=5;
"""

    let private migration5Digest =
        CanonicalJson.sha256 (Encoding.UTF8.GetBytes migration5Sql)

    let private migration6Sql =
        """
CREATE TABLE ci_population_admissions(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, collection_id TEXT NOT NULL UNIQUE, repository TEXT NOT NULL, pr_number INTEGER NOT NULL CHECK(pr_number > 0), base_ref TEXT NOT NULL, base_sha TEXT NOT NULL, head TEXT NOT NULL, witness TEXT NOT NULL CHECK(witness='native-pr-head'), fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(item_id,repository,pr_number,base_ref,base_sha,head)) STRICT;
CREATE TABLE ci_check_runs(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, check_id INTEGER NOT NULL, name TEXT NOT NULL, app_slug TEXT, status TEXT NOT NULL, conclusion TEXT, started_at TEXT, completed_at TEXT, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(repository,check_id)) STRICT;
CREATE TABLE ci_population_coverage(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, collection_id TEXT NOT NULL REFERENCES ci_population_admissions(collection_id), actions TEXT NOT NULL CHECK(actions IN ('complete','partial','unknown')), checks TEXT NOT NULL CHECK(checks IN ('complete','partial','unknown')), attempts TEXT NOT NULL CHECK(attempts IN ('complete','partial','unknown')), jobs TEXT NOT NULL CHECK(jobs IN ('complete','partial','unknown')), terminal TEXT NOT NULL CHECK(terminal IN ('complete','partial','unknown')), timestamps TEXT NOT NULL CHECK(timestamps IN ('complete','partial','unknown')), continuation TEXT NOT NULL CHECK(continuation IN ('none','pending')), external_checks INTEGER NOT NULL CHECK(external_checks >= 0), gaps TEXT NOT NULL, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(collection_id)) STRICT;
PRAGMA user_version=6;
"""

    let private migration6Digest =
        CanonicalJson.sha256 (Encoding.UTF8.GetBytes migration6Sql)

    let private migration7Sql =
        """
CREATE TABLE native_item_outcomes(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, pr_number INTEGER NOT NULL CHECK(pr_number > 0), base_ref TEXT NOT NULL, base_sha TEXT NOT NULL, head TEXT NOT NULL, outcome TEXT NOT NULL, code_delivery TEXT NOT NULL, merge_commit TEXT, occurred_at TEXT, observed_at TEXT NOT NULL, source_kind TEXT NOT NULL CHECK(source_kind='routine-delivery'), source_ref TEXT NOT NULL UNIQUE, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE INDEX native_item_outcomes_item_observed ON native_item_outcomes(item_id,observed_at);
PRAGMA user_version=7;
"""

    let private migration7Digest =
        CanonicalJson.sha256 (Encoding.UTF8.GetBytes migration7Sql)

    let private migration8Sql =
        """
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

    let private migration8Digest =
        CanonicalJson.sha256 (Encoding.UTF8.GetBytes migration8Sql)

    let private migration9Sql =
        """
CREATE TABLE receipt_producers(producer TEXT NOT NULL, stream TEXT NOT NULL, PRIMARY KEY(producer,stream)) STRICT;
CREATE TABLE transport_receipts(producer TEXT NOT NULL, batch TEXT NOT NULL, stream TEXT NOT NULL, digest TEXT NOT NULL, payload_bytes INTEGER NOT NULL, state TEXT NOT NULL CHECK(state IN ('durably-received','applied','rejected')), code TEXT, terminal_utc TEXT, PRIMARY KEY(producer,batch)) STRICT;
CREATE INDEX transport_pending ON transport_receipts(state,producer);
PRAGMA user_version=9;
"""

    let private migration9Digest =
        CanonicalJson.sha256 (Encoding.UTF8.GetBytes migration9Sql)

    let private migration10Sql =
        """
CREATE TABLE native_item_outcomes_v10(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, pr_number INTEGER NOT NULL CHECK(pr_number > 0), base_ref TEXT NOT NULL, base_sha TEXT NOT NULL, head TEXT NOT NULL, outcome TEXT NOT NULL, code_delivery TEXT NOT NULL, merge_commit TEXT, occurred_at TEXT, observed_at TEXT NOT NULL, source_kind TEXT NOT NULL CHECK(source_kind IN ('routine-delivery','orchestration-delivery')), source_ref TEXT NOT NULL UNIQUE, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
INSERT INTO native_item_outcomes_v10 SELECT * FROM native_item_outcomes;
DROP TABLE native_item_outcomes;
ALTER TABLE native_item_outcomes_v10 RENAME TO native_item_outcomes;
CREATE INDEX native_item_outcomes_item_observed ON native_item_outcomes(item_id,observed_at);
PRAGMA user_version=10;
"""

    let private migration10Digest =
        CanonicalJson.sha256 (Encoding.UTF8.GetBytes migration10Sql)

    let private migration11Sql =
        """
CREATE TABLE learning_fact_order(sequence INTEGER PRIMARY KEY AUTOINCREMENT, identity TEXT NOT NULL UNIQUE REFERENCES ingest_facts(identity)) STRICT;
PRAGMA user_version=11;
"""

    let private migration11Digest =
        CanonicalJson.sha256 (Encoding.UTF8.GetBytes migration11Sql)

    let private migration12Sql =
        """
ALTER TABLE receipt_producers ADD COLUMN authority_role TEXT NOT NULL DEFAULT 'generic' CHECK(authority_role IN ('generic','native-collector'));
ALTER TABLE receipt_producers ADD COLUMN grant_id TEXT;
ALTER TABLE receipt_producers ADD COLUMN grant_generation INTEGER;
CREATE TABLE receipt_admissions(producer TEXT NOT NULL, batch TEXT NOT NULL, stream TEXT NOT NULL, authority_role TEXT NOT NULL CHECK(authority_role IN ('generic','native-collector')), grant_id TEXT, grant_generation INTEGER, receipt_key TEXT NOT NULL, envelope_digest TEXT NOT NULL, PRIMARY KEY(producer,batch), FOREIGN KEY(producer,batch) REFERENCES transport_receipts(producer,batch), CHECK((grant_id IS NULL AND grant_generation IS NULL) OR (grant_id IS NOT NULL AND grant_generation > 0))) STRICT;
CREATE TABLE fact_admissions(identity TEXT PRIMARY KEY REFERENCES ingest_facts(identity), producer TEXT NOT NULL, stream TEXT NOT NULL, authority_role TEXT NOT NULL CHECK(authority_role IN ('generic','native-collector')), grant_id TEXT, grant_generation INTEGER, receipt_key TEXT NOT NULL, envelope_digest TEXT NOT NULL, CHECK((grant_id IS NULL AND grant_generation IS NULL) OR (grant_id IS NOT NULL AND grant_generation > 0))) STRICT;
PRAGMA user_version=12;
"""

    let private migration12Digest = CanonicalJson.sha256 (Encoding.UTF8.GetBytes migration12Sql)

    let private migration13Sql =
        """
CREATE TABLE ci_attribution_corrections(correction_id TEXT PRIMARY KEY, plan_digest TEXT NOT NULL, plan TEXT NOT NULL, outcome_identity TEXT NOT NULL, predecessor TEXT REFERENCES ci_attribution_corrections(correction_id), repository TEXT NOT NULL, pr_number INTEGER NOT NULL, base_ref TEXT NOT NULL, base_sha TEXT NOT NULL, head TEXT NOT NULL, merge_commit TEXT NOT NULL, prior_item TEXT NOT NULL, effective_item TEXT NOT NULL, effective_feature TEXT NOT NULL, effective_attempt TEXT NOT NULL, observed_at TEXT NOT NULL, applied_at TEXT NOT NULL) STRICT;
CREATE TABLE ci_correction_evidence(correction_id TEXT NOT NULL REFERENCES ci_attribution_corrections(correction_id), identity TEXT NOT NULL, table_name TEXT NOT NULL, revision INTEGER NOT NULL, digest TEXT NOT NULL, canonical TEXT NOT NULL, PRIMARY KEY(correction_id,identity)) STRICT;
CREATE TABLE ci_effective_attribution(identity TEXT PRIMARY KEY REFERENCES ingest_facts(identity), correction_id TEXT NOT NULL REFERENCES ci_attribution_corrections(correction_id)) STRICT;
CREATE VIEW current_ingest_facts AS SELECT f.identity,f.kind,coalesce(c.effective_item,f.item_id) AS item_id,f.revision,f.content_digest,f.canonical FROM ingest_facts f LEFT JOIN ci_effective_attribution e ON e.identity=f.identity LEFT JOIN ci_attribution_corrections c ON c.correction_id=e.correction_id;
CREATE TRIGGER ci_correction_immutable_update BEFORE UPDATE ON ci_attribution_corrections BEGIN SELECT RAISE(ABORT,'correction ledger is immutable'); END;
CREATE TRIGGER ci_correction_immutable_delete BEFORE DELETE ON ci_attribution_corrections BEGIN SELECT RAISE(ABORT,'correction ledger is immutable'); END;
CREATE TRIGGER ci_correction_evidence_immutable_update BEFORE UPDATE ON ci_correction_evidence BEGIN SELECT RAISE(ABORT,'correction evidence is immutable'); END;
CREATE TRIGGER ci_correction_evidence_immutable_delete BEFORE DELETE ON ci_correction_evidence BEGIN SELECT RAISE(ABORT,'correction evidence is immutable'); END;
INSERT INTO store_metadata(key,value) VALUES('ciCorrectionStoreId',lower(hex(randomblob(16))));
PRAGMA user_version=13;
"""

    let private migration13Digest = CanonicalJson.sha256 (Encoding.UTF8.GetBytes migration13Sql)

    // Receiver clocks are actual new acceptance observations. Migration leaves every historical
    // fact without such a witness unknown; it never backdates history from occurred/observed time.
    let private migration14Sql =
        """
CREATE TABLE efficiency_records(identity TEXT PRIMARY KEY REFERENCES ingest_facts(identity), kind TEXT NOT NULL CHECK(kind IN ('efficiency-resource-allocation/1','efficiency-problem-episode/1','efficiency-assessment/1')), item_id TEXT, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), content_digest TEXT NOT NULL, canonical TEXT NOT NULL) STRICT;
CREATE INDEX efficiency_records_item ON efficiency_records(item_id,kind,identity);
CREATE TABLE efficiency_record_history(identity TEXT NOT NULL, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), content_digest TEXT NOT NULL, canonical TEXT NOT NULL, accepted_at TEXT NOT NULL, receipt_key TEXT NOT NULL, PRIMARY KEY(identity,fact_revision)) STRICT;
CREATE TABLE efficiency_allocation_context(identity TEXT PRIMARY KEY REFERENCES efficiency_records(identity), resource_identity TEXT NOT NULL, resource_revision INTEGER NOT NULL, resource_digest TEXT NOT NULL, effective_item_id TEXT, correction_identity TEXT, correction_digest TEXT, dimension TEXT NOT NULL, provider TEXT NOT NULL, accounting_scope TEXT NOT NULL, unit TEXT NOT NULL, UNIQUE(resource_identity,provider,accounting_scope,unit)) STRICT;
CREATE VIEW efficiency_current_allocations AS
SELECT a.identity,a.canonical,c.resource_identity,
CASE WHEN f.identity IS NOT NULL AND f.revision=c.resource_revision AND f.content_digest=c.resource_digest
 AND f.item_id IS c.effective_item_id AND r.correction_id IS c.correction_identity AND r.plan_digest IS c.correction_digest
 THEN 1 ELSE 0 END AS classification_current
FROM efficiency_records a JOIN efficiency_allocation_context c ON c.identity=a.identity
LEFT JOIN current_ingest_facts f ON f.identity=c.resource_identity
LEFT JOIN ci_effective_attribution e ON e.identity=f.identity
LEFT JOIN ci_attribution_corrections r ON r.correction_id=e.correction_id;
CREATE TABLE fact_acceptance_times(identity TEXT NOT NULL REFERENCES ingest_facts(identity), fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), content_digest TEXT NOT NULL, accepted_at TEXT NOT NULL, receipt_key TEXT, PRIMARY KEY(identity,fact_revision)) STRICT;
CREATE TABLE efficiency_analysis_requests(request_id TEXT PRIMARY KEY, stable_outcome_identity TEXT NOT NULL, outcome_epoch INTEGER, effective_item_id TEXT NOT NULL, scope TEXT NOT NULL CHECK(scope IN ('native-item','provisional-delivery')), evidence_digest TEXT NOT NULL, policy_version TEXT NOT NULL, state TEXT NOT NULL CHECK(state IN ('pending','claimed','settled','failed','unavailable')), revision INTEGER NOT NULL CHECK(revision >= 0), content_digest TEXT NOT NULL, canonical TEXT NOT NULL, evidence_packet BLOB NOT NULL, owner_producer TEXT NOT NULL, owner_stream TEXT NOT NULL, claim_id TEXT, claim_generation INTEGER, invocation_ref TEXT, claimed_at TEXT, requested_at TEXT NOT NULL, updated_at TEXT NOT NULL) STRICT;
CREATE INDEX efficiency_analysis_outcome ON efficiency_analysis_requests(stable_outcome_identity,outcome_epoch,policy_version);
CREATE TABLE efficiency_analysis_history(request_id TEXT NOT NULL, revision INTEGER NOT NULL CHECK(revision >= 0), content_digest TEXT NOT NULL, canonical TEXT NOT NULL, recorded_at TEXT NOT NULL, PRIMARY KEY(request_id,revision)) STRICT;
CREATE TABLE efficiency_analysis_reservations(stable_outcome_identity TEXT NOT NULL, epoch_key TEXT NOT NULL, policy_version TEXT NOT NULL, reservation INTEGER NOT NULL CHECK(reservation BETWEEN 1 AND 3), request_id TEXT NOT NULL REFERENCES efficiency_analysis_requests(request_id), claim_id TEXT NOT NULL UNIQUE, producer TEXT NOT NULL, stream TEXT NOT NULL, dispatch_ref TEXT NOT NULL, generation INTEGER NOT NULL CHECK(generation > 0), state TEXT NOT NULL CHECK(state IN ('reserved','started','settled','failed','unknown','proven-no-effect')), PRIMARY KEY(stable_outcome_identity,epoch_key,policy_version,reservation)) STRICT;
CREATE UNIQUE INDEX efficiency_analysis_dispatch_once ON efficiency_analysis_reservations(dispatch_ref);
CREATE UNIQUE INDEX efficiency_one_active_analysis ON efficiency_analysis_reservations(stable_outcome_identity,epoch_key,policy_version) WHERE state IN ('reserved','started');
CREATE TABLE efficiency_receiver_order(sequence INTEGER PRIMARY KEY AUTOINCREMENT,receipt_key TEXT NOT NULL UNIQUE,producer TEXT NOT NULL,stream TEXT NOT NULL,accepted_at TEXT NOT NULL) STRICT;
CREATE TABLE efficiency_outcome_epochs(original_item_id TEXT NOT NULL,epoch INTEGER NOT NULL CHECK(epoch>0),dispatch_identity TEXT NOT NULL UNIQUE,effective_item_id TEXT NOT NULL,begin_receipt_key TEXT NOT NULL REFERENCES efficiency_receiver_order(receipt_key),begin_sequence INTEGER NOT NULL REFERENCES efficiency_receiver_order(sequence),state TEXT NOT NULL CHECK(state IN ('open','closed')),begin_refs TEXT NOT NULL,outcome_identity TEXT,outcome_revision INTEGER CHECK(outcome_revision>=0),close_sequence INTEGER REFERENCES efficiency_receiver_order(sequence),close_refs TEXT,CHECK((state='open' AND outcome_identity IS NULL AND outcome_revision IS NULL AND close_sequence IS NULL AND close_refs IS NULL) OR (state='closed' AND outcome_identity IS NOT NULL AND outcome_revision IS NOT NULL AND close_sequence>=begin_sequence AND close_refs IS NOT NULL)),PRIMARY KEY(original_item_id,epoch),UNIQUE(outcome_identity,outcome_revision)) STRICT;
CREATE UNIQUE INDEX efficiency_one_open_epoch ON efficiency_outcome_epochs(original_item_id) WHERE state='open';
CREATE TABLE efficiency_epoch_gaps(receipt_key TEXT NOT NULL,original_item_id TEXT NOT NULL,reason TEXT NOT NULL,PRIMARY KEY(receipt_key,original_item_id,reason)) STRICT;

PRAGMA user_version=14;
"""

    let private migration14Digest = CanonicalJson.sha256 (Encoding.UTF8.GetBytes migration14Sql)

    let private roleName = function
        | TelemetryReceipt.Generic -> "generic"
        | TelemetryReceipt.NativeCollector -> "native-collector"

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

        if writer then
            execute connection "PRAGMA synchronous=FULL;"

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
        | Error errors ->
            connection.Dispose()
            Error errors

    let private parameter (command: SqliteCommand) name (value: obj) =
        command.Parameters.AddWithValue(name, value) |> ignore

    let private beginImmediate (connection: SqliteConnection) = execute connection "BEGIN IMMEDIATE;"

    let private rollback (connection: SqliteConnection) =
        try
            execute connection "ROLLBACK;"
        with _ ->
            ()

    let private failBusy (error: SqliteException) =
        if error.SqliteErrorCode = 5 || error.SqliteExtendedErrorCode = 5 then
            [ $"store-busy after bounded %d{busyMilliseconds}ms" ]
        else
            [ error.Message ]

    type private WriterLock(stream: FileStream) =
        member _.Stream = stream

        interface IDisposable with
            member _.Dispose() =
                if OperatingSystem.IsLinux() then
                    Native.flock (stream.SafeFileHandle.DangerousGetHandle().ToInt32(), Native.LockUnlock)
                    |> ignore

                stream.Dispose()

    let private tryWriterLock root =
        try
            let stream =
                new FileStream(
                    Path.Combine(root, "writer.lock"),
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite,
                    1,
                    FileOptions.WriteThrough
                )

            if not (OperatingSystem.IsLinux()) then
                stream.Dispose()
                Error [ "writer lock is supported only on Linux" ]
            elif
                Native.flock (
                    stream.SafeFileHandle.DangerousGetHandle().ToInt32(),
                    Native.LockExclusive ||| Native.LockNonBlocking
                )
                <> 0
            then
                stream.Dispose()
                Error [ "writer-busy" ]
            else
                Ok(new WriterLock(stream))
        with
        | :? IOException -> Error [ "writer-busy" ]
        | error -> Error [ error.Message ]

    let private fsyncDirectory path =
        if OperatingSystem.IsLinux() then
            let descriptor = Native.openDirectory (path, 0x10000 ||| 0x80000)

            if descriptor < 0 then
                raise (IOException $"cannot open directory for durability sync: %s{path}")

            try
                if Native.fsync descriptor <> 0 then
                    raise (IOException $"cannot sync directory: %s{path}")
            finally
                Native.close descriptor |> ignore

    let private existingAncestors path =
        let rec loop (directory: DirectoryInfo) acc =
            if isNull directory then
                acc
            else
                loop directory.Parent (directory :: acc)

        loop (DirectoryInfo path) [] |> List.filter _.Exists

    let private commandOutput executable arguments =
        try
            let info = ProcessStartInfo(executable)
            info.UseShellExecute <- false
            info.RedirectStandardOutput <- true
            info.RedirectStandardError <- true
            arguments |> List.iter info.ArgumentList.Add
            use childProcess = Process.Start info
            let output = childProcess.StandardOutput.ReadToEnd().Trim()
            childProcess.WaitForExit(1000) |> ignore
            if childProcess.ExitCode = 0 then Some output else None
        with _ ->
            None

    let assessProductionRoot path =
        try
            if String.IsNullOrWhiteSpace path || not (Path.IsPathFullyQualified path) then
                TelemetryStore.Unsafe "path is not absolute"
            else
                let full = Path.GetFullPath path

                let temp =
                    Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                    + string Path.DirectorySeparatorChar

                if
                    full.StartsWith(temp, StringComparison.Ordinal)
                    || full = temp.TrimEnd(Path.DirectorySeparatorChar)
                then
                    TelemetryStore.Unsafe "temporary storage is not durable"
                elif
                    existingAncestors full
                    |> List.exists (fun entry -> not (isNull entry.LinkTarget))
                then
                    TelemetryStore.Unsafe "symlinked root or ancestor"
                elif
                    existingAncestors full
                    |> List.exists (fun entry ->
                        Directory.Exists(Path.Combine(entry.FullName, ".git"))
                        || File.Exists(Path.Combine(entry.FullName, ".git")))
                then
                    TelemetryStore.Unsafe "repository roots and worktrees are not telemetry stores"
                elif OperatingSystem.IsLinux() then
                    let mountTarget =
                        if Directory.Exists full then
                            full
                        else
                            existingAncestors full
                            |> List.tryLast
                            |> Option.map _.FullName
                            |> Option.defaultValue full

                    match commandOutput "findmnt" [ "-n"; "-o"; "FSTYPE"; "--target"; mountTarget ] with
                    | Some fs when
                        [ "nfs"; "nfs4"; "cifs"; "smb3"; "9p"; "tmpfs"; "overlay"; "fuse" ]
                        |> List.exists (fun value -> fs.StartsWith(value, StringComparison.OrdinalIgnoreCase))
                        ->
                        TelemetryStore.Unsafe
                            $"filesystem '%s{fs}' is network, memory, or an unqualified container layer"
                    | Some _ -> TelemetryStore.ApprovedLocalDurable
                    | None -> TelemetryStore.DurabilityUnverified "filesystem placement could not be classified"
                else
                    TelemetryStore.DurabilityUnverified "platform durability classification is unavailable"
        with error ->
            TelemetryStore.DurabilityUnverified error.Message

    let private validateExistingPermissions path =
        try
            if Directory.Exists path && not (OperatingSystem.IsWindows()) then
                let mode = File.GetUnixFileMode path

                if mode.HasFlag UnixFileMode.OtherWrite || mode.HasFlag UnixFileMode.GroupWrite then
                    Error [ "store root permissions permit group/other writes" ]
                elif OperatingSystem.IsLinux() then
                    match commandOutput "stat" [ "-c"; "%u"; path ], commandOutput "id" [ "-u" ] with
                    | Some owner, Some current when owner = current -> Ok()
                    | Some _, Some _ -> Error [ "store root is not owned by the current user" ]
                    | _ -> Error [ "store root ownership could not be verified" ]
                else
                    Ok()
            else
                Ok()
        with error ->
            Error [ "cannot validate store root permissions: " + error.Message ]

    let private validateRoot path assessment =
        TelemetryStore.validateStoreRoot path assessment
        |> Result.bind (fun root -> validateExistingPermissions root |> Result.map (fun () -> root))

    let private isReceiptScoped root =
        if not (File.Exists(Path.Combine(root, databaseFileName))) then
            Ok false
        else
            match connect root SqliteOpenMode.ReadOnly with
            | Error _ -> Error [ "storage-unavailable" ]
            | Ok(connection, _) ->
                use connection = connection

                try
                    use command = connection.CreateCommand()

                    command.CommandText <-
                        "SELECT count(*) FROM store_metadata WHERE key='receiptWorkspace' AND value<>'';"

                    Ok(Convert.ToInt64(command.ExecuteScalar()) = 1L)
                with _ ->
                    Error [ "storage-unavailable" ]

    let initialize path assessment =
        match validateRoot path assessment with
        | Error errors -> Error errors
        | Ok root ->
            try
                Directory.CreateDirectory root |> ignore

                if not (OperatingSystem.IsWindows()) then
                    File.SetUnixFileMode(
                        root,
                        UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
                    )

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

                            if version > currentSchemaVersion then
                                Error
                                    [
                                        $"store schema version %d{version} is newer than supported version %d{currentSchemaVersion}"
                                    ]
                            else
                                if version = 0 then
                                    execute connection "PRAGMA journal_mode=WAL;"
                                    beginImmediate connection

                                    try
                                        execute connection migrationSql
                                        use migration = connection.CreateCommand()

                                        migration.CommandText <-
                                            "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(1,$digest,$utc); INSERT INTO store_metadata(key,value) VALUES('schema','fsgg.telemetry.sqlite-store/1'),('nativeEngine',$engine);"

                                        parameter migration "$digest" migrationDigest
                                        parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                        parameter migration "$engine" engine
                                        migration.ExecuteNonQuery() |> ignore
                                        execute connection "COMMIT;"
                                    with error ->
                                        rollback connection
                                        raise error

                                let afterV1 = Int32.Parse(scalarText connection "PRAGMA user_version;")

                                let storedDigest =
                                    scalarText connection "SELECT digest FROM schema_migrations WHERE version=1;"

                                if storedDigest <> migrationDigest then
                                    Error [ "migration checksum mismatch" ]
                                else
                                    if afterV1 = 1 then
                                        beginImmediate connection

                                        try
                                            execute connection migration2Sql
                                            use migration = connection.CreateCommand()

                                            migration.CommandText <-
                                                "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(2,$digest,$utc);"

                                            parameter migration "$digest" migration2Digest
                                            parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                            migration.ExecuteNonQuery() |> ignore
                                            execute connection "COMMIT;"
                                        with error ->
                                            rollback connection
                                            raise error

                                    if
                                        scalarText connection "SELECT digest FROM schema_migrations WHERE version=2;"
                                        <> migration2Digest
                                    then
                                        Error [ "migration checksum mismatch" ]
                                    else
                                        let afterV2 = Int32.Parse(scalarText connection "PRAGMA user_version;")

                                        if afterV2 = 2 then
                                            beginImmediate connection

                                            try
                                                execute connection migration3Sql
                                                use migration = connection.CreateCommand()

                                                migration.CommandText <-
                                                    "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(3,$digest,$utc);"

                                                parameter migration "$digest" migration3Digest
                                                parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                                migration.ExecuteNonQuery() |> ignore
                                                execute connection "COMMIT;"
                                            with error ->
                                                rollback connection
                                                raise error

                                        if
                                            scalarText
                                                connection
                                                "SELECT digest FROM schema_migrations WHERE version=3;"
                                            <> migration3Digest
                                        then
                                            Error [ "migration checksum mismatch" ]
                                        else
                                            let afterV3 = Int32.Parse(scalarText connection "PRAGMA user_version;")

                                            if afterV3 = 3 then
                                                beginImmediate connection

                                                try
                                                    execute connection migration4Sql
                                                    use migration = connection.CreateCommand()

                                                    migration.CommandText <-
                                                        "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(4,$digest,$utc);"

                                                    parameter migration "$digest" migration4Digest
                                                    parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                                    migration.ExecuteNonQuery() |> ignore
                                                    execute connection "COMMIT;"
                                                with error ->
                                                    rollback connection
                                                    raise error

                                            if
                                                scalarText
                                                    connection
                                                    "SELECT digest FROM schema_migrations WHERE version=4;"
                                                <> migration4Digest
                                            then
                                                Error [ "migration checksum mismatch" ]
                                            else
                                                let afterV4 = Int32.Parse(scalarText connection "PRAGMA user_version;")

                                                if afterV4 = 4 then
                                                    beginImmediate connection

                                                    try
                                                        execute connection migration5Sql
                                                        use migration = connection.CreateCommand()

                                                        migration.CommandText <-
                                                            "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(5,$digest,$utc);"

                                                        parameter migration "$digest" migration5Digest
                                                        parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                                        migration.ExecuteNonQuery() |> ignore
                                                        execute connection "COMMIT;"
                                                    with error ->
                                                        rollback connection
                                                        raise error

                                                if
                                                    scalarText
                                                        connection
                                                        "SELECT digest FROM schema_migrations WHERE version=5;"
                                                    <> migration5Digest
                                                then
                                                    Error [ "migration checksum mismatch" ]
                                                else
                                                    let afterV5 =
                                                        Int32.Parse(scalarText connection "PRAGMA user_version;")

                                                    if afterV5 = 5 then
                                                        beginImmediate connection

                                                        try
                                                            execute connection migration6Sql
                                                            use migration = connection.CreateCommand()

                                                            migration.CommandText <-
                                                                "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(6,$digest,$utc);"

                                                            parameter migration "$digest" migration6Digest

                                                            parameter
                                                                migration
                                                                "$utc"
                                                                (DateTimeOffset.UtcNow.ToString("O"))

                                                            migration.ExecuteNonQuery() |> ignore
                                                            execute connection "COMMIT;"
                                                        with error ->
                                                            rollback connection
                                                            raise error

                                                    if
                                                        scalarText
                                                            connection
                                                            "SELECT digest FROM schema_migrations WHERE version=6;"
                                                        <> migration6Digest
                                                    then
                                                        Error [ "migration checksum mismatch" ]
                                                    else
                                                        let afterV6 =
                                                            Int32.Parse(scalarText connection "PRAGMA user_version;")

                                                        if afterV6 = 6 then
                                                            beginImmediate connection

                                                            try
                                                                execute connection migration7Sql
                                                                use migration = connection.CreateCommand()

                                                                migration.CommandText <-
                                                                    "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(7,$digest,$utc);"

                                                                parameter migration "$digest" migration7Digest

                                                                parameter
                                                                    migration
                                                                    "$utc"
                                                                    (DateTimeOffset.UtcNow.ToString("O"))

                                                                migration.ExecuteNonQuery() |> ignore
                                                                execute connection "COMMIT;"
                                                            with error ->
                                                                rollback connection
                                                                raise error

                                                        if
                                                            scalarText
                                                                connection
                                                                "SELECT digest FROM schema_migrations WHERE version=7;"
                                                            <> migration7Digest
                                                        then
                                                            Error [ "migration checksum mismatch" ]
                                                        else
                                                            let afterV7 =
                                                                Int32.Parse(
                                                                    scalarText connection "PRAGMA user_version;"
                                                                )

                                                            if afterV7 = 7 then
                                                                beginImmediate connection

                                                                try
                                                                    execute connection migration8Sql
                                                                    use migration = connection.CreateCommand()

                                                                    migration.CommandText <-
                                                                        "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(8,$digest,$utc);"

                                                                    parameter migration "$digest" migration8Digest

                                                                    parameter
                                                                        migration
                                                                        "$utc"
                                                                        (DateTimeOffset.UtcNow.ToString("O"))

                                                                    migration.ExecuteNonQuery() |> ignore
                                                                    execute connection "COMMIT;"
                                                                with error ->
                                                                    rollback connection
                                                                    raise error

                                                            if
                                                                scalarText
                                                                    connection
                                                                    "SELECT digest FROM schema_migrations WHERE version=8;"
                                                                <> migration8Digest
                                                            then
                                                                Error [ "migration checksum mismatch" ]
                                                            else
                                                                if
                                                                    Int32.Parse(
                                                                        scalarText connection "PRAGMA user_version;"
                                                                    )
                                                                        =
                                                                        8
                                                                then
                                                                    beginImmediate connection

                                                                    try
                                                                        execute connection migration9Sql
                                                                        use migration = connection.CreateCommand()

                                                                        migration.CommandText <-
                                                                            "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(9,$digest,$utc);"

                                                                        parameter migration "$digest" migration9Digest

                                                                        parameter
                                                                            migration
                                                                            "$utc"
                                                                            (DateTimeOffset.UtcNow.ToString("O"))

                                                                        migration.ExecuteNonQuery() |> ignore
                                                                        execute connection "COMMIT;"
                                                                    with error ->
                                                                        rollback connection
                                                                        raise error

                                                                if
                                                                    scalarText
                                                                        connection
                                                                        "SELECT digest FROM schema_migrations WHERE version=9;"
                                                                    <> migration9Digest
                                                                then
                                                                    Error [ "migration checksum mismatch" ]
                                                                else
                                                                    if
                                                                        Int32.Parse(
                                                                            scalarText connection "PRAGMA user_version;"
                                                                        ) = 9
                                                                    then
                                                                        beginImmediate connection

                                                                        try
                                                                            execute connection migration10Sql
                                                                            use migration = connection.CreateCommand()
                                                                            migration.CommandText <-
                                                                                "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(10,$digest,$utc);"
                                                                            parameter migration "$digest" migration10Digest
                                                                            parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                                                            migration.ExecuteNonQuery() |> ignore
                                                                            execute connection "COMMIT;"
                                                                        with error ->
                                                                            rollback connection
                                                                            raise error

                                                                    if
                                                                        scalarText connection "SELECT digest FROM schema_migrations WHERE version=10;"
                                                                        <> migration10Digest
                                                                    then
                                                                        Error [ "migration checksum mismatch" ]
                                                                    else
                                                                        if Int32.Parse(scalarText connection "PRAGMA user_version;") = 10 then
                                                                            beginImmediate connection

                                                                            try
                                                                                execute connection migration11Sql
                                                                                use migration = connection.CreateCommand()
                                                                                migration.CommandText <-
                                                                                    "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(11,$digest,$utc);"
                                                                                parameter migration "$digest" migration11Digest
                                                                                parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                                                                migration.ExecuteNonQuery() |> ignore
                                                                                execute connection "COMMIT;"
                                                                            with error ->
                                                                                rollback connection
                                                                                raise error

                                                                        if scalarText connection "SELECT digest FROM schema_migrations WHERE version=11;" <> migration11Digest then
                                                                            Error [ "migration checksum mismatch" ]
                                                                        else
                                                                            if Int32.Parse(scalarText connection "PRAGMA user_version;") = 11 then
                                                                                beginImmediate connection

                                                                                try
                                                                                    execute connection migration12Sql
                                                                                    use migration = connection.CreateCommand()
                                                                                    migration.CommandText <-
                                                                                        "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(12,$digest,$utc);"
                                                                                    parameter migration "$digest" migration12Digest
                                                                                    parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                                                                    migration.ExecuteNonQuery() |> ignore
                                                                                    execute connection "COMMIT;"
                                                                                with error ->
                                                                                    rollback connection
                                                                                    raise error

                                                                            if scalarText connection "SELECT digest FROM schema_migrations WHERE version=12;" <> migration12Digest then
                                                                                Error [ "migration checksum mismatch" ]
                                                                            else
                                                                                if Int32.Parse(scalarText connection "PRAGMA user_version;") = 12 then
                                                                                    beginImmediate connection
                                                                                    try
                                                                                        execute connection migration13Sql
                                                                                        use migration = connection.CreateCommand()
                                                                                        migration.CommandText <- "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(13,$digest,$utc);"
                                                                                        parameter migration "$digest" migration13Digest
                                                                                        parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                                                                        migration.ExecuteNonQuery() |> ignore
                                                                                        execute connection "COMMIT;"
                                                                                    with error ->
                                                                                        rollback connection
                                                                                        raise error
                                                                                if scalarText connection "SELECT digest FROM schema_migrations WHERE version=13;" <> migration13Digest then
                                                                                    invalidOp "migration checksum mismatch"
                                                                                if Int32.Parse(scalarText connection "PRAGMA user_version;") = 13 then
                                                                                    beginImmediate connection
                                                                                    try
                                                                                        execute connection migration14Sql
                                                                                        use migration = connection.CreateCommand()
                                                                                        migration.CommandText <- "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES(14,$digest,$utc);"
                                                                                        parameter migration "$digest" migration14Digest
                                                                                        parameter migration "$utc" (DateTimeOffset.UtcNow.ToString("O"))
                                                                                        migration.ExecuteNonQuery() |> ignore
                                                                                        execute connection "COMMIT;"
                                                                                    with error ->
                                                                                        rollback connection
                                                                                        raise error
                                                                                if scalarText connection "SELECT digest FROM schema_migrations WHERE version=14;" <> migration14Digest then
                                                                                    invalidOp "migration checksum mismatch"
                                                                                fsyncDirectory root
                                                                                fsyncDirectory (Path.GetDirectoryName root)

                                                                                Ok(
                                                                                    JsonSerializer.Serialize
                                                                                        {|
                                                                                            schema = "fsgg.telemetry.store-status/1"
                                                                                            status = "ready"
                                                                                            root = root
                                                                                            database = databaseFileName
                                                                                            schemaVersion = currentSchemaVersion
                                                                                            nativeEngine = engine
                                                                                            journalMode =
                                                                                                scalarText
                                                                                                    connection
                                                                                                    "PRAGMA journal_mode;"
                                                                                            synchronous =
                                                                                                scalarText
                                                                                                    connection
                                                                                                    "PRAGMA synchronous;"
                                                                                        |}
                                                                                    + "\n"
                                                                                )
                        with :? SqliteException as error ->
                            Error(failBusy error)
            with error ->
                Error [ error.Message ]

    let status path assessment =
        match validateRoot path assessment with
        | Error errors -> Error errors
        | Ok root when not (File.Exists(Path.Combine(root, databaseFileName))) ->
            Ok(
                JsonSerializer.Serialize
                    {|
                        schema = "fsgg.telemetry.store-status/1"
                        status = "not-initialized"
                        root = root
                    |}
                + "\n"
            )
        | Ok root ->
            match connect root SqliteOpenMode.ReadOnly with
            | Error errors -> Error errors
            | Ok(connection, engine) ->
                use connection = connection

                try
                    let version = Int32.Parse(scalarText connection "PRAGMA user_version;")

                    if version > currentSchemaVersion then
                        Error
                            [
                                $"store schema version %d{version} is newer than supported version %d{currentSchemaVersion}"
                            ]
                    elif version <> currentSchemaVersion then
                        Error [ "telemetry store schema requires migration; run telemetry store init" ]
                    elif
                        scalarText connection "SELECT digest FROM schema_migrations WHERE version=1;"
                        <> migrationDigest
                    then
                        Error [ "migration checksum mismatch" ]
                    elif
                        scalarText connection "SELECT digest FROM schema_migrations WHERE version=2;"
                        <> migration2Digest
                    then
                        Error [ "migration checksum mismatch" ]
                    elif
                        scalarText connection "SELECT digest FROM schema_migrations WHERE version=3;"
                        <> migration3Digest
                    then
                        Error [ "migration checksum mismatch" ]
                    elif
                        scalarText connection "SELECT digest FROM schema_migrations WHERE version=4;"
                        <> migration4Digest
                    then
                        Error [ "migration checksum mismatch" ]
                    elif
                        scalarText connection "SELECT digest FROM schema_migrations WHERE version=5;"
                        <> migration5Digest
                    then
                        Error [ "migration checksum mismatch" ]
                    elif
                        scalarText connection "SELECT digest FROM schema_migrations WHERE version=6;"
                        <> migration6Digest
                    then
                        Error [ "migration checksum mismatch" ]
                    elif
                        scalarText connection "SELECT digest FROM schema_migrations WHERE version=7;"
                        <> migration7Digest
                    then
                        Error [ "migration checksum mismatch" ]
                    elif
                        scalarText connection "SELECT digest FROM schema_migrations WHERE version=8;"
                        <> migration8Digest
                    then
                        Error [ "migration checksum mismatch" ]
                    elif
                        scalarText connection "SELECT digest FROM schema_migrations WHERE version=10;"
                        <> migration10Digest
                    then
                        Error [ "migration checksum mismatch" ]
                    elif
                        scalarText connection "SELECT digest FROM schema_migrations WHERE version=12;"
                        <> migration12Digest
                    then
                        Error [ "migration checksum mismatch" ]
                    elif scalarText connection "SELECT digest FROM schema_migrations WHERE version=13;" <> migration13Digest then
                        Error [ "migration checksum mismatch" ]
                    elif scalarText connection "SELECT digest FROM schema_migrations WHERE version=14;" <> migration14Digest then
                        Error [ "migration checksum mismatch" ]
                    else
                        let inbox = Path.Combine(root, "inbox")

                        let pending =
                            [ inbox; Path.Combine(root, "receipt-inbox") ]
                            |> List.sumBy (fun directory ->
                                if Directory.Exists directory then
                                    Directory.EnumerateFiles(directory, "*.ready", SearchOption.AllDirectories)
                                    |> Seq.truncate 1025
                                    |> Seq.length
                                else
                                    0)

                        Ok(
                            JsonSerializer.Serialize
                                {|
                                    schema = "fsgg.telemetry.store-status/1"
                                    status = "ready"
                                    root = root
                                    database = databaseFileName
                                    schemaVersion = version
                                    nativeEngine = engine
                                    journalMode = scalarText connection "PRAGMA journal_mode;"
                                    pendingBatches = pending
                                |}
                            + "\n"
                        )
                with error ->
                    Error [ error.Message ]

    let private deleteTyped (connection: SqliteConnection) identity preservedTable =
        for table in
            [
                "items"
                "features"
                "attempts"
                "parent_child"
                "pr_heads"
                "usage_observations"
                "delivery_observations"
                "evidence_observations"
                "coverage_observations"
                "health_diagnostics"
                "runtime_admissions"
                "runtime_starts"
                "runtime_turn_usage"
                "runtime_terminals"
                "runtime_gaps"
                "ci_bindings"
                "ci_pages"
                "ci_runs"
                "ci_jobs"
                "ci_steps"
                "ci_coverage"
                "ci_population_coverage"
                "ci_check_runs"
                "ci_population_admissions"
                "native_item_outcomes"
                "budget_population_facts"
                "budget_attribution_facts"
                "budget_interval_facts"
                "budget_intervention_facts"
                "budget_shared_cost_refs"
                "operational_activations"
                "expected_dispatches"
                "invocation_lineage"
                "operational_event_times"
                "process_reviews"
                "activity_spans"
                "activity_usage_attributions"
                "complication_events"
                "efficiency_allocation_context"
                "efficiency_records"
            ]
            |> List.filter (fun table -> Some table <> preservedTable) do
            use command = connection.CreateCommand()
            command.CommandText <- $"DELETE FROM %s{table} WHERE identity=$identity;"
            parameter command "$identity" identity
            command.ExecuteNonQuery() |> ignore

    let private insertTyped (connection: SqliteConnection) (fact: TelemetryStore.Fact) =
        let optional value =
            value |> Option.map box |> Option.defaultValue DBNull.Value

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
        | TelemetryStore.EfficiencyRecord _ ->
            run
                "INSERT INTO efficiency_records VALUES($identity,$kind,$item,$revision,$digest,$canonical);"
                [ "$kind", box fact.Kind; "$revision", box fact.Revision
                  "$digest", box fact.ContentDigest; "$canonical", box fact.Canonical ]
            if fact.Kind = "efficiency-resource-allocation/1" then
                use document = JsonDocument.Parse fact.Canonical
                let resource = document.RootElement.GetProperty "resource"
                let reference = resource.GetProperty "sourceRef"
                run
                    """INSERT INTO efficiency_allocation_context
SELECT $identity,f.identity,f.revision,f.content_digest,f.item_id,c.correction_id,c.plan_digest,$dimension,$provider,$scope,$unit
FROM current_ingest_facts f
LEFT JOIN ci_effective_attribution e ON e.identity=f.identity
LEFT JOIN ci_attribution_corrections c ON c.correction_id=e.correction_id
WHERE f.identity=$resource AND f.revision=$sourceRevision AND f.content_digest=$sourceDigest;"""
                    [ "$resource", box (reference.GetProperty("id").GetString())
                      "$sourceRevision", box (reference.GetProperty("revision").GetInt64())
                      "$sourceDigest", box (reference.GetProperty("contentDigest").GetString().Substring(7))
                      "$dimension", box (resource.GetProperty("dimension").GetString())
                      "$provider", box (resource.GetProperty("provider").GetString())
                      "$scope", box (resource.GetProperty("accountingScope").GetString())
                      "$unit", box (resource.GetProperty("unit").GetString()) ]
        | TelemetryStore.Item feature ->
            run "INSERT INTO items VALUES($identity,$item,$feature);" [ "$feature", optional feature ]
        | TelemetryStore.Feature name -> run "INSERT INTO features VALUES($identity,$item,$name);" [ "$name", box name ]
        | TelemetryStore.Attempt(parent, status) ->
            run
                "INSERT INTO attempts VALUES($identity,$item,$parent,$status);"
                [ "$parent", box parent; "$status", box status ]
        | TelemetryStore.ParentChild(parent, child) ->
            run
                "INSERT INTO parent_child VALUES($identity,$item,$parent,$child);"
                [ "$parent", box parent; "$child", box child ]
        | TelemetryStore.PullRequestHead(repo, pr, head) ->
            run
                "INSERT INTO pr_heads VALUES($identity,$item,$repo,$pr,$head);"
                [ "$repo", box repo; "$pr", box pr; "$head", box head ]
        | TelemetryStore.Source(source, generation, cursor) ->
            run "INSERT INTO health_diagnostics VALUES($identity,$item,'source-fact','info');" []
        | TelemetryStore.Usage(provider,
                               model,
                               effort,
                               input,
                               cached,
                               write,
                               output,
                               reasoning,
                               total,
                               responses,
                               sessions,
                               turns) ->
            run
                "INSERT INTO usage_observations VALUES($identity,$item,$provider,$model,$effort,$input,$cached,$write,$output,$reasoning,$total,$responses,$sessions,$turns);"
                [
                    "$provider", box provider
                    "$model", box model
                    "$effort", box effort
                    "$input", box input
                    "$cached", box cached
                    "$write", box write
                    "$output", box output
                    "$reasoning", optional reasoning
                    "$total", box total
                    "$responses", box responses
                    "$sessions", box sessions
                    "$turns", box turns
                ]
        | TelemetryStore.Delivery(state, expected, observed, pr) ->
            run
                "INSERT INTO delivery_observations VALUES($identity,$item,$state,$expected,$observed,$pr);"
                [
                    "$state", box state
                    "$expected", optional expected
                    "$observed", optional observed
                    "$pr", optional pr
                ]
        | TelemetryStore.Evidence(digest, availability) ->
            run
                "INSERT INTO evidence_observations VALUES($identity,$item,$digest,$availability);"
                [ "$digest", box digest; "$availability", box availability ]
        | TelemetryStore.Coverage coverage ->
            run
                "INSERT INTO coverage_observations VALUES($identity,$item,$validity,$join,$coverage,$qualification,$eligible,$observed);"
                [
                    "$validity", box coverage.RecordValidity
                    "$join", box coverage.JoinIntegrity
                    "$coverage", box coverage.PopulationCoverage
                    "$qualification", box coverage.Qualification
                    "$eligible", optional coverage.Eligible
                    "$observed", optional coverage.Observed
                ]
        | TelemetryStore.Diagnostic(code, severity) ->
            run
                "INSERT INTO health_diagnostics VALUES($identity,$item,$code,$severity);"
                [ "$code", box code; "$severity", box severity ]
        | TelemetryStore.Correction(target, reason) ->
            run
                "INSERT INTO health_diagnostics VALUES($identity,$item,$code,'correction');"
                [ "$code", box $"target=%s{target}; %s{reason}" ]
        | TelemetryStore.RuntimeAdmission(invocation, feature, attempt, parent, producer, model, effort, backend) ->
            run
                "INSERT INTO runtime_admissions VALUES($identity,$item,$invocation,$feature,$attempt,$parent,$producer,$model,$effort,$backend);"
                [
                    "$invocation", box invocation
                    "$feature", box feature
                    "$attempt", box attempt
                    "$parent", optional parent
                    "$producer", box producer
                    "$model", optional model
                    "$effort", optional effort
                    "$backend", optional backend
                ]
        | TelemetryStore.RuntimeStart(invocation, threadId, turnId, turnSequence, processId, phase) ->
            run
                "INSERT INTO runtime_starts VALUES($identity,$item,$invocation,$thread,$turn,$sequence,$pid,$phase);"
                [
                    "$invocation", box invocation
                    "$thread", optional threadId
                    "$turn", optional turnId
                    "$sequence", optional turnSequence
                    "$pid", box processId
                    "$phase", box phase
                ]
        | TelemetryStore.RuntimeTurnUsage(invocation,
                                          threadId,
                                          turnId,
                                          sequence,
                                          provider,
                                          requestedModel,
                                          observedModel,
                                          requestedEffort,
                                          observedEffort,
                                          backend,
                                          scope,
                                          provenance,
                                          input,
                                          cached,
                                          output,
                                          reasoning,
                                          total) ->
            run
                "INSERT INTO runtime_turn_usage VALUES($identity,$item,$invocation,$thread,$turn,$sequence,$provider,$requestedModel,$observedModel,$requestedEffort,$observedEffort,$backend,$scope,$provenance,$input,$cached,$output,$reasoning,$total);"
                [
                    "$invocation", box invocation
                    "$thread", box threadId
                    "$turn", optional turnId
                    "$sequence", box sequence
                    "$provider", optional provider
                    "$requestedModel", optional requestedModel
                    "$observedModel", optional observedModel
                    "$requestedEffort", optional requestedEffort
                    "$observedEffort", optional observedEffort
                    "$backend", optional backend
                    "$scope", box scope
                    "$provenance", box provenance
                    "$input", box input
                    "$cached", box cached
                    "$output", box output
                    "$reasoning", optional reasoning
                    "$total", box total
                ]
        | TelemetryStore.RuntimeTerminal(invocation, threadId, outcome, exitCode) ->
            run
                "INSERT INTO runtime_terminals VALUES($identity,$item,$invocation,$thread,$outcome,$exit);"
                [
                    "$invocation", box invocation
                    "$thread", optional threadId
                    "$outcome", box outcome
                    "$exit", box exitCode
                ]
        | TelemetryStore.RuntimeGap(invocation, code) ->
            run
                "INSERT INTO runtime_gaps VALUES($identity,$item,$invocation,$code);"
                [ "$invocation", box invocation; "$code", box code ]
        | TelemetryStore.CiBinding(collection,
                                   repository,
                                   head,
                                   pr,
                                   workflow,
                                   feature,
                                   attempt,
                                   parent,
                                   producer,
                                   binding) ->
            run
                "INSERT INTO ci_bindings VALUES($identity,$item,$collection,$repository,$head,$pr,$workflow,$feature,$attempt,$parent,$producer,$binding) ON CONFLICT(identity) DO UPDATE SET item_id=excluded.item_id,collection_id=excluded.collection_id,repository=excluded.repository,head=excluded.head,pr_number=excluded.pr_number,workflow=excluded.workflow,feature_id=excluded.feature_id,attempt_id=excluded.attempt_id,parent_attempt_id=excluded.parent_attempt_id,producer_stream=excluded.producer_stream,binding=excluded.binding;"
                [
                    "$collection", box collection
                    "$repository", box repository
                    "$head", box head
                    "$pr", box pr
                    "$workflow", box workflow
                    "$feature", box feature
                    "$attempt", box attempt
                    "$parent", optional parent
                    "$producer", box producer
                    "$binding", box binding
                ]
        | TelemetryStore.CiPage(collection, resource, page, count, total) ->
            run
                "INSERT INTO ci_pages VALUES($identity,$item,$collection,$resource,$page,$count,$total);"
                [
                    "$collection", box collection
                    "$resource", box resource
                    "$page", box page
                    "$count", box count
                    "$total", box total
                ]
        | TelemetryStore.CiRun(repository,
                               runId,
                               attempt,
                               workflow,
                               event,
                               head,
                               status,
                               conclusion,
                               created,
                               started,
                               updated) ->
            run
                "INSERT INTO ci_runs VALUES($identity,$item,$repository,$run,$attempt,$workflow,$event,$head,$status,$conclusion,$created,$started,$updated);"
                [
                    "$repository", box repository
                    "$run", box runId
                    "$attempt", box attempt
                    "$workflow", box workflow
                    "$event", box event
                    "$head", box head
                    "$status", box status
                    "$conclusion", optional conclusion
                    "$created", optional created
                    "$started", optional started
                    "$updated", optional updated
                ]
        | TelemetryStore.CiJob(repository, runId, attempt, jobId, name, status, conclusion, created, started, completed) ->
            run
                "INSERT INTO ci_jobs VALUES($identity,$item,$repository,$run,$attempt,$job,$name,$status,$conclusion,$created,$started,$completed);"
                [
                    "$repository", box repository
                    "$run", box runId
                    "$attempt", box attempt
                    "$job", box jobId
                    "$name", box name
                    "$status", box status
                    "$conclusion", optional conclusion
                    "$created", optional created
                    "$started", optional started
                    "$completed", optional completed
                ]
        | TelemetryStore.CiStep(repository,
                                runId,
                                attempt,
                                jobId,
                                number,
                                name,
                                status,
                                conclusion,
                                started,
                                completed,
                                classification,
                                rationale) ->
            run
                "INSERT INTO ci_steps VALUES($identity,$item,$repository,$run,$attempt,$job,$number,$name,$status,$conclusion,$started,$completed,$classification,$rationale);"
                [
                    "$repository", box repository
                    "$run", box runId
                    "$attempt", box attempt
                    "$job", box jobId
                    "$number", box number
                    "$name", box name
                    "$status", box status
                    "$conclusion", optional conclusion
                    "$started", optional started
                    "$completed", optional completed
                    "$classification", box classification
                    "$rationale", box rationale
                ]
        | TelemetryStore.CiCoverage(collection,
                                    inventory,
                                    attempts,
                                    jobPages,
                                    terminal,
                                    timestamps,
                                    lineage,
                                    classification,
                                    criticalPath) ->
            run
                "INSERT INTO ci_coverage VALUES($identity,$item,$collection,$inventory,$attempts,$jobPages,$terminal,$timestamps,$lineage,$classification,$criticalPath);"
                [
                    "$collection", box collection
                    "$inventory", box inventory
                    "$attempts", box attempts
                    "$jobPages", box jobPages
                    "$terminal", box terminal
                    "$timestamps", box timestamps
                    "$lineage", box lineage
                    "$classification", box classification
                    "$criticalPath", box criticalPath
                ]
        | TelemetryStore.CiPopulationAdmission(collection, repository, pr, baseRef, baseSha, head, witness) ->
            run
                "INSERT INTO ci_population_admissions VALUES($identity,$item,$collection,$repository,$pr,$baseRef,$baseSha,$head,$witness,$revision) ON CONFLICT(identity) DO UPDATE SET item_id=excluded.item_id,collection_id=excluded.collection_id,repository=excluded.repository,pr_number=excluded.pr_number,base_ref=excluded.base_ref,base_sha=excluded.base_sha,head=excluded.head,witness=excluded.witness,fact_revision=excluded.fact_revision;"
                [
                    "$collection", box collection
                    "$repository", box repository
                    "$pr", box pr
                    "$baseRef", box baseRef
                    "$baseSha", box baseSha
                    "$head", box head
                    "$witness", box witness
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.CiCheck(repository, checkId, name, app, status, conclusion, started, completed) ->
            run
                "INSERT INTO ci_check_runs VALUES($identity,$item,$repository,$check,$name,$app,$status,$conclusion,$started,$completed,$revision);"
                [
                    "$repository", box repository
                    "$check", box checkId
                    "$name", box name
                    "$app", optional app
                    "$status", box status
                    "$conclusion", optional conclusion
                    "$started", optional started
                    "$completed", optional completed
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.CiPopulationCoverage(collection,
                                              actions,
                                              checks,
                                              attempts,
                                              jobs,
                                              terminal,
                                              timestamps,
                                              continuation,
                                              externalChecks,
                                              gaps) ->
            run
                "INSERT INTO ci_population_coverage VALUES($identity,$item,$collection,$actions,$checks,$attempts,$jobs,$terminal,$timestamps,$continuation,$external,$gaps,$revision);"
                [
                    "$collection", box collection
                    "$actions", box actions
                    "$checks", box checks
                    "$attempts", box attempts
                    "$jobs", box jobs
                    "$terminal", box terminal
                    "$timestamps", box timestamps
                    "$continuation", box continuation
                    "$external", box externalChecks
                    "$gaps", box gaps
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.NativeItemOutcome(repository,
                                           pullRequest,
                                           baseRef,
                                           baseSha,
                                           head,
                                           outcome,
                                           codeDelivery,
                                           mergeCommit,
                                           occurredAt,
                                           observedAt,
                                           sourceKind,
                                           sourceRef) ->
            run
                "INSERT INTO native_item_outcomes VALUES($identity,$item,$repository,$pr,$baseRef,$baseSha,$head,$outcome,$codeDelivery,$mergeCommit,$occurred,$observed,$sourceKind,$sourceRef,$revision);"
                [
                    "$repository", box repository
                    "$pr", box pullRequest
                    "$baseRef", box baseRef
                    "$baseSha", box baseSha
                    "$head", box head
                    "$outcome", box outcome
                    "$codeDelivery", box codeDelivery
                    "$mergeCommit", optional mergeCommit
                    "$occurred", optional occurredAt
                    "$observed", box observedAt
                    "$sourceKind", box sourceKind
                    "$sourceRef", box sourceRef
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.BudgetPopulation(original, state, sourceKind, sourceRef) ->
            run
                "INSERT INTO budget_population_facts VALUES($identity,$item,$original,$state,$sourceKind,$sourceRef,$revision);"
                [
                    "$original", box original
                    "$state", box state
                    "$sourceKind", box sourceKind
                    "$sourceRef", box sourceRef
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.BudgetAttribution(dimension,
                                           provider,
                                           scope,
                                           numerator,
                                           denominator,
                                           coverage,
                                           attribution,
                                           sourceKind,
                                           sourceRef) ->
            run
                "INSERT INTO budget_attribution_facts VALUES($identity,$item,$dimension,$provider,$scope,$numerator,$denominator,$coverage,$attribution,$sourceKind,$sourceRef,$revision); INSERT INTO budget_shared_cost_refs VALUES($identity,$item,$sourceRef,$dimension,$provider,$scope);"
                [
                    "$dimension", box dimension
                    "$provider", box provider
                    "$scope", box scope
                    "$numerator", optional numerator
                    "$denominator", optional denominator
                    "$coverage", box coverage
                    "$attribution", box attribution
                    "$sourceKind", box sourceKind
                    "$sourceRef", box sourceRef
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.BudgetInterval(dimension, classification, startAt, endAt, witnessed, sourceKind, sourceRef) ->
            run
                "INSERT INTO budget_interval_facts VALUES($identity,$item,$dimension,$classification,$start,$end,$witnessed,$sourceKind,$sourceRef,$revision); INSERT INTO budget_shared_cost_refs VALUES($identity,$item,$sourceRef,$dimension,'interval','interval');"
                [
                    "$dimension", box dimension
                    "$classification", box classification
                    "$start", box startAt
                    "$end", box endAt
                    "$witnessed", box (if witnessed then 1 else 0)
                    "$sourceKind", box sourceKind
                    "$sourceRef", box sourceRef
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.BudgetIntervention(intervention, transition, sequence, result, coverage, sourceRef) ->
            run
                "INSERT INTO budget_intervention_facts VALUES($identity,$item,$intervention,$transition,$sequence,$result,$coverage,$sourceRef,$revision);"
                [
                    "$intervention", box intervention
                    "$transition", box transition
                    "$sequence", box sequence
                    "$result", box result
                    "$coverage", box coverage
                    "$sourceRef", box sourceRef
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.OperationalActivation(activation, scope, runtime, activatedAt, clock, lateAfter) ->
            run
                "INSERT INTO operational_activations VALUES($identity,$item,$activation,$scope,$runtime,$activatedAt,$clock,$lateAfter,$revision);"
                [
                    "$activation", box activation
                    "$scope", box scope
                    "$runtime", box runtime
                    "$activatedAt", box activatedAt
                    "$clock", box clock
                    "$lateAfter", box lateAfter
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.ExpectedDispatch(dispatch, activation, relation, parent, runtime, expectedAt, clock) ->
            run
                "INSERT INTO expected_dispatches VALUES($identity,$item,$dispatch,$activation,$relation,$parent,$runtime,$expectedAt,$clock,$revision);"
                [
                    "$dispatch", box dispatch
                    "$activation", box activation
                    "$relation", box relation
                    "$parent", optional parent
                    "$runtime", box runtime
                    "$expectedAt", box expectedAt
                    "$clock", box clock
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.InvocationLineage(dispatch, invocation, relation, parent, root, runtime) ->
            run
                "INSERT INTO invocation_lineage VALUES($identity,$item,$dispatch,$invocation,$relation,$parent,$root,$runtime,$revision);"
                [
                    "$dispatch", box dispatch
                    "$invocation", box invocation
                    "$relation", box relation
                    "$parent", optional parent
                    "$root", box root
                    "$runtime", box runtime
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.EventTime(invocation, event, occurred, occurredClock, observed, observedClock) ->
            run
                "INSERT INTO operational_event_times VALUES($identity,$item,$invocation,$event,$occurred,$occurredClock,$observed,$observedClock,$revision);"
                [
                    "$invocation", box invocation
                    "$event", box event
                    "$occurred", optional occurred
                    "$occurredClock", optional occurredClock
                    "$observed", optional observed
                    "$observedClock", optional observedClock
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.ProcessReview review ->
            let item =
                fact.ItemId
                |> Option.defaultWith (fun () -> invalidOp "process-review requires itemId")

            match review.Scope, review.AttemptId with
            | "attempt", Some attempt ->
                let admitted =
                    scalarCount
                        "SELECT count(*) FROM runtime_admissions WHERE item_id=$item AND attempt_id=$attempt;"
                        [ "$item", box item; "$attempt", box attempt ]

                let settled =
                    scalarCount
                        "SELECT count(*) FROM runtime_admissions a WHERE a.item_id=$item AND a.attempt_id=$attempt AND EXISTS(SELECT 1 FROM runtime_terminals t WHERE t.item_id=a.item_id AND t.invocation_id=a.invocation_id);"
                        [ "$item", box item; "$attempt", box attempt ]

                if admitted = 0L || admitted <> settled then
                    invalidOp "attempt process review requires a fully terminal admitted attempt"
            | "item", None ->
                let expected =
                    scalarCount "SELECT count(*) FROM expected_dispatches WHERE item_id=$item;" [ "$item", box item ]

                let settled =
                    scalarCount
                        "SELECT count(*) FROM expected_dispatches d WHERE d.item_id=$item AND (SELECT count(*) FROM invocation_lineage l WHERE l.item_id=d.item_id AND l.dispatch_id=d.dispatch_id)=1 AND EXISTS(SELECT 1 FROM invocation_lineage l JOIN runtime_terminals t ON t.item_id=l.item_id AND t.invocation_id=l.invocation_id WHERE l.item_id=d.item_id AND l.dispatch_id=d.dispatch_id);"
                        [ "$item", box item ]

                if expected = 0L || expected <> settled then
                    invalidOp "item process review requires the complete expected population to be terminal"
            | _ -> invalidOp "process-review scope is inconsistent"

            run
                "INSERT INTO process_reviews VALUES($identity,$item,$scope,$attempt,$synopsis,$well,$problems,$delay,$observations,$risks,$improvements,$evidence,$evidenceCoverage,$populationCoverage,$confidence,$model,$effort,$reviewed,$duration,$revision);"
                [
                    "$scope", box review.Scope
                    "$attempt", optional review.AttemptId
                    "$synopsis", box review.OutcomeSynopsis
                    "$well", box review.WentWell
                    "$problems", box review.Problems
                    "$delay", box review.AvoidableDelayOrRework
                    "$observations", box review.ProcessObservations
                    "$risks", box review.RemainingRisks
                    "$improvements", box review.ConcreteImprovements
                    "$evidence", box review.Evidence
                    "$evidenceCoverage", box review.EvidenceCoverage
                    "$populationCoverage", box review.PopulationCoverage
                    "$confidence", box review.Confidence
                    "$model", box review.ReviewerModel
                    "$effort", box review.ReviewerEffort
                    "$reviewed", box review.ReviewedAt
                    "$duration", box review.DurationSeconds
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.ActivitySpan activity ->
            let item =
                fact.ItemId
                |> Option.defaultWith (fun () -> invalidOp "activity-span requires itemId")

            let bound =
                scalarCount
                    "SELECT count(*) FROM runtime_admissions WHERE item_id=$item AND invocation_id=$invocation AND attempt_id=$attempt;"
                    [
                        "$item", box item
                        "$invocation", box activity.InvocationId
                        "$attempt", box activity.AttemptId
                    ]

            if bound <> 1L then
                invalidOp "activity span requires one matching admitted invocation and attempt"

            run
                "INSERT INTO activity_spans VALUES($identity,$item,$activity,$invocation,$attempt,$category,$started,$ended,$clock,$evidence,$summary,$revision);"
                [
                    "$activity", box activity.ActivityId
                    "$invocation", box activity.InvocationId
                    "$attempt", box activity.AttemptId
                    "$category", box activity.Category
                    "$started", box activity.StartedAt
                    "$ended", optional activity.EndedAt
                    "$clock", box activity.ClockProvenance
                    "$evidence", box activity.Evidence
                    "$summary", optional activity.Summary
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.ActivityUsageAttribution attribution ->
            let item =
                fact.ItemId
                |> Option.defaultWith (fun () -> invalidOp "activity-usage-attribution requires itemId")

            use usage = connection.CreateCommand()

            usage.CommandText <-
                "SELECT input_count,cached_input,output_count,reasoning,total,invocation_id FROM runtime_turn_usage WHERE identity=$usage AND item_id=$item;"

            parameter usage "$usage" attribution.UsageIdentity
            parameter usage "$item" item
            use reader = usage.ExecuteReader()

            if not (reader.Read()) then
                invalidOp "activity usage attribution requires matching native usage"

            let nativeReasoning = if reader.IsDBNull 3 then None else Some(reader.GetInt64 3)
            let invocation = reader.GetString 5

            if
                reader.GetInt64 0 <> attribution.Input
                || reader.GetInt64 1 <> attribution.CachedInput
                || reader.GetInt64 2 <> attribution.Output
                || nativeReasoning <> attribution.Reasoning
                || reader.GetInt64 4 <> attribution.Total
            then
                invalidOp "activity usage attribution counters must exactly match native usage"

            reader.Close()

            match attribution.Classification, attribution.ActivityId with
            | "direct", Some activity ->
                if
                    scalarCount
                        "SELECT count(*) FROM activity_spans WHERE item_id=$item AND activity_id=$activity AND invocation_id=$invocation;"
                        [ "$item", box item; "$activity", box activity; "$invocation", box invocation ]
                    <> 1L
                then
                    invalidOp "direct usage attribution requires an activity on the same invocation"
            | ("mixed" | "unclassified"), None -> ()
            | _ -> invalidOp "activity usage attribution classification is inconsistent"

            run
                "INSERT INTO activity_usage_attributions VALUES($identity,$item,$usage,$activity,$classification,$input,$cached,$output,$reasoning,$total,$revision);"
                [
                    "$usage", box attribution.UsageIdentity
                    "$activity", optional attribution.ActivityId
                    "$classification", box attribution.Classification
                    "$input", box attribution.Input
                    "$cached", box attribution.CachedInput
                    "$output", box attribution.Output
                    "$reasoning", optional attribution.Reasoning
                    "$total", box attribution.Total
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.Complication complication ->
            let item =
                fact.ItemId
                |> Option.defaultWith (fun () -> invalidOp "complication requires itemId")

            match complication.AttemptId with
            | Some attempt when
                scalarCount
                    "SELECT count(*) FROM runtime_admissions WHERE item_id=$item AND attempt_id=$attempt;"
                    [ "$item", box item; "$attempt", box attempt ]
                    =
                    0L
                ->
                invalidOp "complication attempt is not admitted for the item"
            | _ -> ()

            match complication.ActivityId with
            | Some activity when
                scalarCount
                    "SELECT count(*) FROM activity_spans WHERE item_id=$item AND activity_id=$activity;"
                    [ "$item", box item; "$activity", box activity ]
                <> 1L
                ->
                invalidOp "complication activity is not recorded for the item"
            | _ -> ()

            run
                "INSERT INTO complication_events VALUES($identity,$item,$attempt,$activity,$trigger,$cause,$occurred,$synopsis,$evidence,$revision);"
                [
                    "$attempt", optional complication.AttemptId
                    "$activity", optional complication.ActivityId
                    "$trigger", box complication.Trigger
                    "$cause", box complication.Cause
                    "$occurred", box complication.OccurredAt
                    "$synopsis", box complication.Synopsis
                    "$evidence", box complication.Evidence
                    "$revision", box fact.Revision
                ]
        | TelemetryStore.LearnTaskSnapshot _
        | TelemetryStore.LearnContextManifest _
        | TelemetryStore.LearnExperimentAssignment _
        | TelemetryStore.LearnAccountingInventory _ ->
            // LEARN observations are immutable canonical ingest facts. Keeping them in
            // the existing append/replay ledger avoids a second execution-intent journal.
            let item = fact.ItemId |> Option.defaultWith (fun () -> invalidOp "LEARN observation requires itemId")
            let count =
                scalarCount
                    "SELECT count(*) FROM ingest_facts WHERE item_id=$item AND kind=$kind;"
                    [ "$item", box item; "$kind", box fact.Kind ]
            if count <> 1L then
                invalidOp $"%s{fact.Kind} must be unique per item"
        | TelemetryStore.LearnSharedCostAllocation(_, _, _, _, _, roster) ->
            use document = JsonDocument.Parse roster
            let members = document.RootElement.EnumerateArray() |> Seq.map _.GetString() |> Seq.toList
            let retainedBy = fact.ItemId |> Option.defaultWith (fun () -> invalidOp "shared allocation requires itemId")
            if not (members |> List.contains retainedBy) then
                invalidOp "shared allocation must be retained by a rostered item"
            for rosterItem in members do
                let assigned =
                    scalarCount
                        "SELECT count(*) FROM ingest_facts WHERE item_id=$item AND kind='learn-experiment-assignment';"
                        [ "$item", box rosterItem ]
                if assigned <> 0L then
                    invalidOp $"shared allocation for %s{rosterItem} must be persisted before assignment"
        | TelemetryStore.RuntimeNativeInventory _
        | TelemetryStore.RuntimeNativeInventorySource _
        | TelemetryStore.LearnSharedCost _
        | TelemetryStore.LearnSharedCostAuthority _
        | TelemetryStore.LearnNativeDeliverySource _ -> ()
        | TelemetryStore.LearnInstalledOrigin _ -> ()

        match fact.ItemId, fact.Payload with
        | Some item,
          (TelemetryStore.BudgetPopulation _ | TelemetryStore.BudgetAttribution _ | TelemetryStore.BudgetInterval _ | TelemetryStore.BudgetIntervention _ | TelemetryStore.RuntimeAdmission _ | TelemetryStore.RuntimeStart _ | TelemetryStore.RuntimeTurnUsage _ | TelemetryStore.RuntimeTerminal _ | TelemetryStore.RuntimeGap _ | TelemetryStore.CiBinding _ | TelemetryStore.CiPage _ | TelemetryStore.CiRun _ | TelemetryStore.CiJob _ | TelemetryStore.CiStep _ | TelemetryStore.CiCoverage _ | TelemetryStore.CiPopulationAdmission _ | TelemetryStore.CiCheck _ | TelemetryStore.CiPopulationCoverage _ | TelemetryStore.NativeItemOutcome _ | TelemetryStore.OperationalActivation _ | TelemetryStore.ExpectedDispatch _ | TelemetryStore.InvocationLineage _ | TelemetryStore.EventTime _ | TelemetryStore.ProcessReview _ | TelemetryStore.ActivitySpan _ | TelemetryStore.ActivityUsageAttribution _ | TelemetryStore.Complication _) ->
            use dirty = connection.CreateCommand()

            dirty.CommandText <-
                "INSERT INTO budget_dirty_items(item_id) VALUES($item) ON CONFLICT(item_id) DO NOTHING;"

            parameter dirty "$item" item
            dirty.ExecuteNonQuery() |> ignore
        | _ -> ()

    let private deriveBudgetInputs (connection: SqliteConnection) item =
        let parameterized sql =
            let command = connection.CreateCommand()
            command.CommandText <- sql
            parameter command "$item" item
            command

        let scalarInt64 sql =
            use command = parameterized sql in Convert.ToInt64(command.ExecuteScalar())

        let stable suffix =
            CanonicalJson.sha256(Encoding.UTF8.GetBytes($"%s{item}\u001f%s{suffix}")).Substring(0, 40)

        let optional value =
            value |> Option.map box |> Option.defaultValue DBNull.Value

        let revision = scalarInt64 "SELECT count(*) FROM current_ingest_facts WHERE item_id=$item;"

        let latestOutcome =
            use command =
                parameterized
                    "SELECT outcome,code_delivery,occurred_at,observed_at FROM native_item_outcomes WHERE item_id=$item ORDER BY observed_at DESC,fact_revision DESC,identity DESC LIMIT 1;"

            use reader = command.ExecuteReader()

            if reader.Read() then
                Some(
                    reader.GetString 0,
                    reader.GetString 1,
                    (if reader.IsDBNull 2 then None else Some(reader.GetString 2)),
                    reader.GetString 3
                )
            else
                None

        use purge =
            parameterized
                """DELETE FROM budget_shared_cost_refs WHERE item_id=$item AND source_ref LIKE 'derived:%';
DELETE FROM budget_interval_facts WHERE item_id=$item AND source_ref LIKE 'derived:%';
DELETE FROM budget_attribution_facts WHERE item_id=$item AND source_ref LIKE 'derived:%';
DELETE FROM budget_population_facts WHERE item_id=$item AND source_ref LIKE 'derived:%';"""
        purge.ExecuteNonQuery() |> ignore

        match latestOutcome with
        | None -> ()
        | Some(outcome, codeDelivery, outcomeAt, observedAt) ->
            let sourceClaims =
                use command =
                    parameterized
                        "SELECT identity,original_item_id,state,source_kind,source_ref FROM budget_population_facts WHERE item_id=$item AND source_ref NOT LIKE 'derived:%' ORDER BY original_item_id LIMIT 4097;"

                use reader = command.ExecuteReader()

                [
                    while reader.Read() do
                        yield
                            reader.GetString 0,
                            reader.GetString 1,
                            reader.GetString 2,
                            reader.GetString 3,
                            reader.GetString 4
                ]

            let trustedOriginal (identity, original, state, sourceKind, sourceRef) =
                let key =
                    System.Security.Cryptography.SHA256.HashData(
                        Encoding.UTF8.GetBytes(item + "\u001f" + original)
                    )
                    |> Convert.ToHexString
                    |> fun value -> value.ToLowerInvariant().Substring(0, 32)

                identity = "budget-population-" + key
                && state = "open"
                && sourceKind = "native-item"
                && sourceRef = "roadmap-dispatch:" + key

            let explicitOriginals =
                sourceClaims
                |> List.filter trustedOriginal
                |> List.map (fun (_, original, _, _, _) -> original)
                |> List.distinct

            let conflictingClaim =
                List.length sourceClaims > 4096
                || (sourceClaims
                    |> List.exists (fun claim ->
                        let (_, original, _, _, _) = claim
                        original <> item && not (trustedOriginal claim)))

            let originalItem =
                match explicitOriginals with
                | [ original ] -> original
                | _ -> item

            let expected = scalarInt64 "SELECT count(*) FROM expected_dispatches WHERE item_id=$item;"

            let supported =
                scalarInt64
                    "SELECT count(*) FROM expected_dispatches WHERE item_id=$item AND runtime IN ('codex-exec','collaboration-spawn-agent');"

            let settled =
                scalarInt64
                    """SELECT count(*) FROM expected_dispatches d
WHERE d.item_id=$item AND d.runtime IN ('codex-exec','collaboration-spawn-agent')
AND EXISTS(SELECT 1 FROM operational_activations a WHERE a.item_id=d.item_id AND a.activation_id=d.activation_id AND a.runtime=d.runtime)
AND (SELECT count(*) FROM invocation_lineage l WHERE l.item_id=d.item_id AND l.dispatch_id=d.dispatch_id)=1
AND EXISTS(
  SELECT 1 FROM invocation_lineage l
  JOIN runtime_terminals t ON t.item_id=l.item_id AND t.invocation_id=l.invocation_id
  WHERE l.item_id=d.item_id AND l.dispatch_id=d.dispatch_id AND l.relation=d.relation AND l.runtime=d.runtime
  AND (SELECT count(*) FROM invocation_lineage other WHERE other.item_id=l.item_id AND other.invocation_id=l.invocation_id)=1
  AND (d.relation='root' OR EXISTS(
    SELECT 1 FROM expected_dispatches parent
    JOIN invocation_lineage parent_lineage ON parent_lineage.item_id=parent.item_id AND parent_lineage.dispatch_id=parent.dispatch_id
    WHERE parent.item_id=d.item_id AND parent.dispatch_id=d.parent_dispatch_id
      AND parent_lineage.invocation_id=l.parent_invocation_id AND parent_lineage.root_invocation_id=l.root_invocation_id
      AND (SELECT count(*) FROM invocation_lineage parent_rows WHERE parent_rows.item_id=parent.item_id AND parent_rows.dispatch_id=parent.dispatch_id)=1
  )));"""

            let rootExpected =
                scalarInt64
                    "SELECT count(*) FROM expected_dispatches WHERE item_id=$item AND relation='root';"
                    =
                    1L

            let deliveredComplete =
                codeDelivery = "delivered"
                && rootExpected
                && expected > 0L
                && supported = expected
                && settled = expected

            // A final delivery refusal does not wait for unrelated, unsupported
            // expected populations (for example a queued CI inventory).
            let refusedComplete = outcome = "refused" && settled = supported

            let state =
                if
                    (deliveredComplete || refusedComplete)
                    && List.length explicitOriginals <= 1
                    && not conflictingClaim
                then
                    "completed"
                else
                    "open"

            let prefix = "derived:" + stable "projection"

            use population =
                parameterized
                    "INSERT INTO budget_population_facts VALUES($identity,$item,$original,$state,'native-item',$source,$revision);"

            parameter population "$identity" ("derived-population-" + stable "population")
            parameter population "$original" originalItem
            parameter population "$state" state
            parameter population "$source" (prefix + ":population")
            parameter population "$revision" revision
            population.ExecuteNonQuery() |> ignore

            let insertAttribution
                dimension
                provider
                scope
                numerator
                denominator
                coverage
                attribution
                sourceKind
                suffix
                =
                let sourceRef = prefix + ":" + suffix
                let identity = "derived-attribution-" + stable suffix

                use command =
                    parameterized
                        "INSERT INTO budget_attribution_facts VALUES($identity,$item,$dimension,$provider,$scope,$numerator,$denominator,$coverage,$attribution,$sourceKind,$source,$revision); INSERT INTO budget_shared_cost_refs VALUES($identity,$item,$source,$dimension,$provider,$scope);"

                [
                    "$identity", box identity
                    "$dimension", box dimension
                    "$provider", box provider
                    "$scope", box scope
                    "$numerator", optional numerator
                    "$denominator", optional denominator
                    "$coverage", box coverage
                    "$attribution", box attribution
                    "$sourceKind", box sourceKind
                    "$source", box sourceRef
                    "$revision", box revision
                ]
                |> List.iter (fun (name, value) -> parameter command name value)

                command.ExecuteNonQuery() |> ignore

            let runtimeIncomplete =
                scalarInt64 "SELECT count(*) FROM runtime_gaps WHERE item_id=$item;" > 0L
                || scalarInt64
                    "SELECT count(*) FROM runtime_admissions a WHERE a.item_id=$item AND NOT EXISTS(SELECT 1 FROM runtime_terminals t WHERE t.item_id=a.item_id AND t.invocation_id=a.invocation_id);"
                    >
                    0L
                || scalarInt64
                    "SELECT count(*) FROM runtime_terminals t WHERE t.item_id=$item AND NOT EXISTS(SELECT 1 FROM runtime_turn_usage u WHERE u.item_id=t.item_id AND u.invocation_id=t.invocation_id);"
                    >
                    0L

            let runtimeRows =
                use command =
                    parameterized
                        "SELECT coalesce(provider,'unknown'),sum(total) FROM runtime_turn_usage WHERE item_id=$item GROUP BY coalesce(provider,'unknown') ORDER BY 1;"

                use reader = command.ExecuteReader()
                let rows = ResizeArray<_>()

                while reader.Read() do
                    rows.Add(reader.GetString 0, reader.GetInt64 1)

                List.ofSeq rows

            let runtimeRows =
                if runtimeRows.IsEmpty then
                    [ "unknown", 0L ]
                else
                    runtimeRows

            for provider, total in runtimeRows do
                insertAttribution
                    "model-usage"
                    provider
                    "whole-item"
                    None
                    (if total = 0L then None else Some total)
                    (if runtimeIncomplete then "unknown" else "complete")
                    "unclassified"
                    "runtime"
                    ("runtime:" + provider)

            insertAttribution
                "owner-effort"
                "human"
                "whole-item"
                None
                None
                "unknown"
                "unclassified"
                "native-item"
                "owner-effort"

            insertAttribution
                "priced-cost"
                "unknown"
                "whole-item"
                None
                None
                "unknown"
                "unclassified"
                "native-item"
                "priced-cost"

            let timestampNanoseconds (value: string) =
                match DateTimeOffset.TryParse value with
                | true, parsed -> Some(parsed.ToUnixTimeMilliseconds() * 1_000_000L)
                | _ -> None

            let activationAt =
                use command =
                    parameterized
                        "SELECT activated_at FROM operational_activations WHERE item_id=$item ORDER BY activated_at LIMIT 1;"

                let value = command.ExecuteScalar()

                if isNull value || value = box DBNull.Value then
                    None
                else
                    timestampNanoseconds (string value)

            let completedAt =
                outcomeAt |> Option.orElse (Some observedAt) |> Option.bind timestampNanoseconds

            let lead =
                match activationAt, completedAt with
                | Some first, Some last when last >= first -> Some(last - first)
                | _ -> None

            insertAttribution
                "critical-path-delay"
                "github"
                "whole-item"
                None
                lead
                "unknown"
                "unclassified"
                "ci"
                "critical-path"

            let jobTotal =
                use command =
                    parameterized "SELECT started_at,completed_at FROM ci_jobs WHERE item_id=$item;"

                use reader = command.ExecuteReader()
                let mutable total = 0L

                while reader.Read() do
                    if not (reader.IsDBNull 0 || reader.IsDBNull 1) then
                        match timestampNanoseconds (reader.GetString 0), timestampNanoseconds (reader.GetString 1) with
                        | Some first, Some last when last >= first -> total <- total + last - first
                        | _ -> ()

                total

            let mutable adminTotal = 0L

            let stepValues =
                use steps =
                    parameterized
                        "SELECT identity,classification,started_at,completed_at FROM ci_steps WHERE item_id=$item ORDER BY identity;"

                use stepRows = steps.ExecuteReader()
                let values = ResizeArray<_>()

                while stepRows.Read() do
                    values.Add(
                        stepRows.GetString 0,
                        stepRows.GetString 1,
                        (if stepRows.IsDBNull 2 then
                             None
                         else
                             Some(stepRows.GetString 2)),
                        (if stepRows.IsDBNull 3 then
                             None
                         else
                             Some(stepRows.GetString 3))
                    )

                List.ofSeq values

            for stepIdentity, classification, startedAt, endedAt in stepValues do
                match startedAt |> Option.bind timestampNanoseconds, endedAt |> Option.bind timestampNanoseconds with
                | Some first, Some last when last >= first ->
                    if classification = "admin" then
                        adminTotal <- adminTotal + last - first

                    let budgetClass, witnessed =
                        match classification with
                        | "admin" -> "administrative", false
                        | "useful-validation" -> "useful", false
                        | "necessary-setup" -> "productive", false
                        | _ -> "administrative", false

                    let suffix = "ci-interval:" + stepIdentity
                    let sourceRef = prefix + ":" + suffix
                    let identity = "derived-interval-" + stable suffix

                    use interval =
                        parameterized
                            "INSERT INTO budget_interval_facts VALUES($identity,$item,'critical-path-delay',$classification,$start,$end,$witnessed,'ci',$source,$revision); INSERT INTO budget_shared_cost_refs VALUES($identity,$item,$source,'critical-path-delay','interval','interval');"

                    [
                        "$identity", box identity
                        "$classification", box budgetClass
                        "$start", box first
                        "$end", box last
                        "$witnessed", box (if witnessed then 1 else 0)
                        "$source", box sourceRef
                        "$revision", box revision
                    ]
                    |> List.iter (fun (name, value) -> parameter interval name value)

                    interval.ExecuteNonQuery() |> ignore
                | _ -> ()

            insertAttribution
                "ci-runner-administration"
                "github-actions"
                "diagnostic-only"
                (Some adminTotal)
                (if jobTotal = 0L then None else Some jobTotal)
                "not-applicable"
                "classified"
                "ci"
                "ci-runner-diagnostic"

    let private budgetReevaluateFor (connection: SqliteConnection) selectedItems allowIntervention =
        let scalarInt sql parameters =
            use command = connection.CreateCommand()
            command.CommandText <- sql
            parameters |> List.iter (fun (name, value) -> parameter command name value)
            Convert.ToInt64(command.ExecuteScalar())

        let scalarOptionalText sql parameters =
            use command = connection.CreateCommand()
            command.CommandText <- sql
            parameters |> List.iter (fun (name, value) -> parameter command name value)
            let value = command.ExecuteScalar()

            if isNull value || value = box DBNull.Value then
                None
            else
                Some(string value)

        let currentEpoch () =
            scalarText connection "SELECT epoch_id FROM budget_epochs WHERE state='open';"

        // Re-project retained native outcomes once after the runtime closure rule changes.
        // The marker and dirty census commit with the same budget transaction.
        if
            scalarOptionalText
                "SELECT value FROM store_metadata WHERE key='completedPopulationDerivation';"
                []
            <> Some "native-runtime-v2"
        then
            execute
                connection
                """INSERT OR IGNORE INTO budget_dirty_items(item_id) SELECT DISTINCT item_id FROM native_item_outcomes;
INSERT INTO store_metadata(key,value) VALUES('completedPopulationDerivation','native-runtime-v2')
ON CONFLICT(key) DO UPDATE SET value=excluded.value;"""

        let dirtyItems =
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT item_id FROM budget_dirty_items ORDER BY item_id LIMIT 32;"
            use reader = command.ExecuteReader()
            let values = ResizeArray<string>()

            while reader.Read() do
                values.Add(reader.GetString 0)

            List.ofSeq values

        for item in (selectedItems |> Option.defaultValue dirtyItems) do
            deriveBudgetInputs connection item
            let itemParameter = [ "$item", box item ]

            let population =
                use command = connection.CreateCommand()

                command.CommandText <-
                    "SELECT original_item_id,state,source_ref FROM budget_population_facts WHERE item_id=$item ORDER BY CASE WHEN source_ref LIKE 'derived:%' THEN 0 ELSE 1 END,fact_revision DESC,identity DESC LIMIT 1;"

                parameter command "$item" item
                use reader = command.ExecuteReader()
                let values = ResizeArray<string * string * string>()

                while reader.Read() do
                    values.Add(reader.GetString 0, reader.GetString 1, reader.GetString 2)

                List.ofSeq values

            let completed = population |> List.filter (fun (_, state, _) -> state = "completed")
            let original = completed |> List.tryHead |> Option.map (fun (value, _, _) -> value)

            let membership =
                scalarOptionalText "SELECT epoch_id FROM budget_epoch_membership WHERE item_id=$item;" itemParameter

            let epoch =
                match membership, original with
                | Some value, _ -> Some value
                | None, Some originalItem ->
                    let value = currentEpoch ()
                    use insert = connection.CreateCommand()

                    insert.CommandText <-
                        "INSERT INTO budget_epoch_membership(epoch_id,item_id,original_item_id) VALUES($epoch,$item,$original);"

                    parameter insert "$epoch" value
                    parameter insert "$item" item
                    parameter insert "$original" originalItem
                    insert.ExecuteNonQuery() |> ignore
                    Some value
                | None, None -> None

            let machineDerived =
                scalarInt "SELECT count(*) FROM native_item_outcomes WHERE item_id=$item;" itemParameter > 0L

            let references =
                scalarInt
                    (if machineDerived then
                         "SELECT count(*) FROM budget_shared_cost_refs WHERE item_id=$item AND source_ref LIKE 'derived:%';"
                     else
                         "SELECT count(*) FROM budget_shared_cost_refs WHERE item_id=$item;")
                    itemParameter

            let runtimeGap =
                scalarInt "SELECT count(*) FROM runtime_gaps WHERE item_id=$item;" itemParameter > 0L

            let ciIncomplete =
                use command = connection.CreateCommand()

                command.CommandText <-
                    "SELECT inventory,attempts,job_pages,terminal,timestamps,lineage,classification FROM ci_coverage WHERE item_id=$item ORDER BY rowid DESC LIMIT 1;"

                parameter command "$item" item
                use reader = command.ExecuteReader()

                reader.Read()
                && [ 0..6 ] |> List.exists (fun index -> reader.GetString index <> "complete")

            let attributions =
                use command = connection.CreateCommand()

                command.CommandText <-
                    "SELECT dimension,provider,accounting_scope,numerator,denominator,coverage,attribution,source_kind,source_ref,fact_revision FROM budget_attribution_facts WHERE item_id=$item AND ($derived=0 OR source_ref LIKE 'derived:%') ORDER BY dimension,provider,accounting_scope,identity LIMIT 4097;"

                parameter command "$item" item
                parameter command "$derived" (if machineDerived then 1 else 0)
                use reader = command.ExecuteReader()
                let values = ResizeArray<_>()

                while reader.Read() do
                    values.Add(
                        reader.GetString 0,
                        reader.GetString 1,
                        reader.GetString 2,
                        (if reader.IsDBNull 3 then None else Some(reader.GetInt64 3)),
                        (if reader.IsDBNull 4 then None else Some(reader.GetInt64 4)),
                        reader.GetString 5,
                        reader.GetString 6,
                        reader.GetString 7,
                        reader.GetString 8,
                        reader.GetInt64 9
                    )

                List.ofSeq values

            for dimension,
                provider,
                scope,
                suppliedNumerator,
                denominator,
                coverage,
                attribution,
                sourceKind,
                sourceRef,
                factRevision in attributions |> List.truncate 4096 do
                let intervals =
                    use command = connection.CreateCommand()

                    command.CommandText <-
                        "SELECT classification,start_ns,end_ns,witnessed,source_ref FROM budget_interval_facts WHERE item_id=$item AND dimension=$dimension AND ($derived=0 OR source_ref LIKE 'derived:%') ORDER BY start_ns,end_ns LIMIT 4097;"

                    parameter command "$item" item
                    parameter command "$dimension" dimension
                    parameter command "$derived" (if machineDerived then 1 else 0)
                    use reader = command.ExecuteReader()
                    let values = ResizeArray<_>()

                    while reader.Read() do
                        values.Add(
                            reader.GetString 0,
                            reader.GetInt64 1,
                            reader.GetInt64 2,
                            reader.GetInt64 3 = 1L,
                            reader.GetString 4
                        )

                    List.ofSeq values

                let intervalOverflow = intervals.Length > 4096

                let intervalNumerator =
                    let administrative =
                        intervals
                        |> List.choose (fun (kind, startAt, endAt, witnessed, _) ->
                            if kind = "administrative" && witnessed then
                                Some(
                                    {
                                        StartNanoseconds = startAt
                                        EndNanoseconds = endAt
                                    }
                                    : TelemetryBudget.Interval
                                )
                            else
                                None)

                    let exclusions =
                        intervals
                        |> List.choose (fun (kind, startAt, endAt, witnessed, _) ->
                            if kind <> "administrative" && witnessed then
                                Some(
                                    {
                                        StartNanoseconds = startAt
                                        EndNanoseconds = endAt
                                    }
                                    : TelemetryBudget.Interval
                                )
                            else
                                None)

                    if administrative.IsEmpty then
                        suppliedNumerator
                    else
                        TelemetryBudget.subtractNanoseconds administrative exclusions

                let unwitnessed =
                    intervals |> List.exists (fun (_, _, _, witnessed, _) -> not witnessed)

                let usability =
                    if completed.IsEmpty then
                        TelemetryBudget.Unknown "whole-item-incomplete"
                    elif
                        completed
                        |> List.map (fun (value, _, _) -> value)
                        |> List.distinct
                        |> List.length
                        <> 1
                    then
                        TelemetryBudget.Unknown "population-conflict"
                    elif references > 4096L || intervalOverflow then
                        TelemetryBudget.Unknown "reference-limit"
                    elif coverage = "not-applicable" then
                        TelemetryBudget.NotApplicable "source-not-applicable"
                    elif coverage <> "complete" then
                        TelemetryBudget.Unknown "source-coverage"
                    elif attribution <> "classified" then
                        TelemetryBudget.Unknown "attribution-unknown"
                    elif sourceKind = "runtime" && runtimeGap then
                        TelemetryBudget.Unknown "runtime-gap"
                    elif sourceKind = "ci" && ciIncomplete then
                        TelemetryBudget.Unknown "ci-coverage"
                    elif unwitnessed && not intervals.IsEmpty then
                        TelemetryBudget.Unknown "unwitnessed-critical-path"
                    else
                        match intervalNumerator, denominator with
                        | Some numerator, Some value -> TelemetryBudget.Usable(numerator, value)
                        | _ -> TelemetryBudget.Unknown "missing-measurement"

                let verdict = TelemetryBudget.assess usability

                let verdictText, numeratorValue, denominatorValue, severe, reason =
                    match verdict with
                    | TelemetryBudget.UnknownVerdict why -> "unknown", intervalNumerator, denominator, false, why
                    | TelemetryBudget.NotApplicableVerdict why ->
                        "not-applicable", intervalNumerator, denominator, false, why
                    | TelemetryBudget.Pass(numerator, value) ->
                        "pass", Some numerator, Some value, false, "within-ceiling"
                    | TelemetryBudget.Breach(numerator, value, isSevere) ->
                        "breach",
                        Some numerator,
                        Some value,
                        isSevere,
                        (if isSevere then
                             "above-severe-threshold"
                         else
                             "above-ceiling")

                match epoch with
                | None -> ()
                | Some epochId ->
                    let sourceDigest =
                        CanonicalJson.sha256 (
                            Encoding.UTF8.GetBytes(
                                String.concat
                                    "|"
                                    [
                                        string factRevision
                                        sourceRef
                                        coverage
                                        attribution
                                        string intervalNumerator
                                        string denominator
                                        string runtimeGap
                                        string ciIncomplete
                                        reason
                                    ]
                            )
                        )

                    let latestDigest =
                        scalarOptionalText
                            "SELECT source_digest FROM budget_assessment_revisions WHERE item_id=$item AND dimension=$dimension AND provider=$provider AND accounting_scope=$scope ORDER BY assessment_revision DESC LIMIT 1;"
                            [
                                "$item", box item
                                "$dimension", box dimension
                                "$provider", box provider
                                "$scope", box scope
                            ]

                    let assessmentRevision =
                        scalarInt
                            "SELECT coalesce(max(assessment_revision),0)+1 FROM budget_assessment_revisions WHERE item_id=$item AND dimension=$dimension AND provider=$provider AND accounting_scope=$scope;"
                            [
                                "$item", box item
                                "$dimension", box dimension
                                "$provider", box provider
                                "$scope", box scope
                            ]

                    let effectiveRevision =
                        if latestDigest = Some sourceDigest then
                            assessmentRevision - 1L
                        else
                            assessmentRevision

                    if latestDigest <> Some sourceDigest then
                        use insert = connection.CreateCommand()

                        insert.CommandText <-
                            "INSERT INTO budget_assessment_revisions VALUES($item,$dimension,$provider,$scope,$revision,$epoch,$verdict,$numerator,$denominator,$severe,$reason,$digest);"

                        [
                            "$item", box item
                            "$dimension", box dimension
                            "$provider", box provider
                            "$scope", box scope
                            "$revision", box assessmentRevision
                            "$epoch", box epochId
                            "$verdict", box verdictText
                            "$numerator", numeratorValue |> Option.map box |> Option.defaultValue DBNull.Value
                            "$denominator", denominatorValue |> Option.map box |> Option.defaultValue DBNull.Value
                            "$severe", box (if severe then 1 else 0)
                            "$reason", box reason
                            "$digest", box sourceDigest
                        ]
                        |> List.iter (fun (name, value) -> parameter insert name value)

                        insert.ExecuteNonQuery() |> ignore

                    use breach = connection.CreateCommand()

                    if verdictText = "breach" then
                        breach.CommandText <-
                            "INSERT INTO budget_breaches VALUES($epoch,$item,$dimension,$provider,$scope,$revision,$severe) ON CONFLICT(epoch_id,item_id,dimension,provider,accounting_scope) DO UPDATE SET assessment_revision=excluded.assessment_revision,severe=excluded.severe;"

                        parameter breach "$revision" effectiveRevision
                        parameter breach "$severe" (if severe then 1 else 0)
                    else
                        breach.CommandText <-
                            "DELETE FROM budget_breaches WHERE epoch_id=$epoch AND item_id=$item AND dimension=$dimension AND provider=$provider AND accounting_scope=$scope;"

                    parameter breach "$epoch" epochId
                    parameter breach "$item" item
                    parameter breach "$dimension" dimension
                    parameter breach "$provider" provider
                    parameter breach "$scope" scope
                    if allowIntervention || scalarInt "SELECT count(*) FROM budget_epochs WHERE epoch_id=$epoch AND state='open';" [ "$epoch", box epochId ] = 1L then
                        breach.ExecuteNonQuery() |> ignore

            if not allowIntervention then
                use invalidate = connection.CreateCommand()
                invalidate.CommandText <-
                    """INSERT INTO budget_assessment_revisions
SELECT a.item_id,a.dimension,a.provider,a.accounting_scope,a.assessment_revision+1,a.epoch_id,'unknown',NULL,NULL,0,'CI attribution superseded; current input unavailable',$digest
FROM budget_assessment_revisions a
WHERE a.item_id=$item AND a.verdict<>'unknown'
AND a.assessment_revision=(SELECT max(b.assessment_revision) FROM budget_assessment_revisions b WHERE b.item_id=a.item_id AND b.dimension=a.dimension AND b.provider=a.provider AND b.accounting_scope=a.accounting_scope)
AND NOT EXISTS(SELECT 1 FROM budget_attribution_facts f WHERE f.item_id=a.item_id AND f.dimension=a.dimension AND f.provider=a.provider AND f.accounting_scope=a.accounting_scope);
DELETE FROM budget_breaches WHERE item_id=$item AND epoch_id IN (SELECT epoch_id FROM budget_epochs WHERE state='open')
AND NOT EXISTS(SELECT 1 FROM budget_attribution_facts f WHERE f.item_id=budget_breaches.item_id AND f.dimension=budget_breaches.dimension AND f.provider=budget_breaches.provider AND f.accounting_scope=budget_breaches.accounting_scope);"""
                parameter invalidate "$item" item
                parameter invalidate "$digest" (CanonicalJson.sha256(Encoding.UTF8.GetBytes("ci-correction-unavailable:" + item)))
                invalidate.ExecuteNonQuery() |> ignore

            use clean = connection.CreateCommand()
            clean.CommandText <- "DELETE FROM budget_dirty_items WHERE item_id=$item;"
            parameter clean "$item" item
            clean.ExecuteNonQuery() |> ignore

        let epochId = currentEpoch ()

        let breachCount =
            scalarInt
                "SELECT count(DISTINCT item_id) FROM budget_breaches WHERE epoch_id=$epoch;"
                [ "$epoch", box epochId ]

        let hasSevere =
            scalarInt
                "SELECT count(*) FROM budget_breaches WHERE epoch_id=$epoch AND severe=1;"
                [ "$epoch", box epochId ]
                >
                0L

        let intervention = $"%s{epochId}-intervention"

        if allowIntervention && TelemetryBudget.interventionDue (int breachCount) hasSevere then
            let trigger =
                scalarText
                    connection
                    (if hasSevere then
                         $"SELECT item_id FROM budget_breaches WHERE epoch_id='%s{epochId}' AND severe=1 ORDER BY item_id LIMIT 1;"
                     else
                         $"SELECT item_id FROM budget_breaches WHERE epoch_id='%s{epochId}' ORDER BY item_id LIMIT 1;")

            use insert = connection.CreateCommand()

            insert.CommandText <-
                "INSERT INTO budget_interventions(intervention_id,epoch_id,state,trigger_item_id,trigger_kind) VALUES($intervention,$epoch,'open',$item,$kind) ON CONFLICT(epoch_id) DO NOTHING;"

            parameter insert "$intervention" intervention
            parameter insert "$epoch" epochId
            parameter insert "$item" trigger
            parameter insert "$kind" (if hasSevere then "severe" else "fifteenth-distinct")
            insert.ExecuteNonQuery() |> ignore

        match
            scalarOptionalText
                "SELECT intervention_id FROM budget_interventions WHERE epoch_id=$epoch AND state='open';"
                [ "$epoch", box epochId ]
        with
        | None -> ()
        | Some _ when not allowIntervention -> ()
        | Some openIntervention ->
            use evidence = connection.CreateCommand()

            evidence.CommandText <-
                "SELECT transition,sequence,result,coverage,source_ref FROM budget_intervention_facts WHERE intervention_id=$intervention ORDER BY sequence,identity;"

            parameter evidence "$intervention" openIntervention
            use reader = evidence.ExecuteReader()
            let values = ResizeArray<_>()

            while reader.Read() do
                values.Add(
                    reader.GetString 0,
                    reader.GetInt64 1,
                    reader.GetString 2,
                    reader.GetString 3,
                    reader.GetString 4
                )

            reader.Close()

            let deployed =
                values
                |> Seq.filter (fun (transition, _, _, coverage, _) -> transition = "deployed" && coverage = "complete")
                |> Seq.tryHead

            let verified =
                match deployed with
                | None -> None
                | Some(_, deployedSequence, _, _, _) ->
                    values
                    |> Seq.tryFind (fun (transition, sequence, result, coverage, _) ->
                        transition = "verified"
                        && sequence > deployedSequence
                        && result = "improved"
                        && coverage = "complete")

            match deployed, verified with
            | Some(_, _, _, _, deployedRef), Some(_, _, _, _, verifiedRef) ->
                use close = connection.CreateCommand()

                close.CommandText <-
                    "UPDATE budget_interventions SET state='verified',deployed_ref=$deployed,verified_ref=$verified WHERE intervention_id=$intervention AND state='open'; UPDATE budget_epochs SET state='verified' WHERE epoch_id=$epoch; INSERT INTO budget_epochs(epoch_id,ordinal,state) SELECT 'epoch-' || (ordinal+1),ordinal+1,'open' FROM budget_epochs WHERE epoch_id=$epoch;"

                parameter close "$deployed" deployedRef
                parameter close "$verified" verifiedRef
                parameter close "$intervention" openIntervention
                parameter close "$epoch" epochId
                close.ExecuteNonQuery() |> ignore
            | _ -> ()

    let private budgetReevaluate connection = budgetReevaluateFor connection None true

    // Efficiency facts use the existing authenticated receipt association. A declared
    // classifier role is not a grant; its witness must already be durably applied.
    let private efficiencyReference (connection: SqliteConnection) (reference: JsonElement) =
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT canonical,item_id FROM current_ingest_facts WHERE identity=$id AND kind=$kind AND revision=$revision AND content_digest=$digest;"
        [ "$id", box (reference.GetProperty("id").GetString())
          "$kind", box (reference.GetProperty("kind").GetString())
          "$revision", box (reference.GetProperty("revision").GetInt64())
          "$digest", box (reference.GetProperty("contentDigest").GetString().Substring(7)) ]
        |> List.iter (fun (name, value) -> parameter command name value)
        use reader = command.ExecuteReader()
        if not (reader.Read()) then invalidOp "efficiency-reference-unavailable-or-stale"
        let canonical = reader.GetString 0
        let item = if reader.IsDBNull 1 then None else Some(reader.GetString 1)
        use document = JsonDocument.Parse canonical
        document.RootElement.Clone(), item

    let private efficiencyReferences (node: JsonElement) =
        let rec collect (value: JsonElement) = seq {
            if value.ValueKind = JsonValueKind.Object then
                let properties = value.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
                if properties = set [ "id"; "kind"; "revision"; "contentDigest" ] then
                    yield value
                else
                    for property in value.EnumerateObject() do yield! collect property.Value
            elif value.ValueKind = JsonValueKind.Array then
                for child in value.EnumerateArray() do yield! collect child
        }
        collect node |> Seq.toList

    // Native authority is a collector grant and its applied inventory/origin receipts,
    // not the role text on an ordinary classifier or informational runtime row.
    let private efficiencyNativeWitnesses (connection: SqliteConnection) (item: string) (invocation: string) =
        use command = connection.CreateCommand()
        command.CommandText <- """
SELECT f.identity,f.kind,f.revision,f.content_digest,f.canonical,a.producer,a.stream,a.grant_id,a.grant_generation
FROM current_ingest_facts f
JOIN fact_admissions a ON a.identity=f.identity AND a.authority_role='native-collector'
JOIN receipt_producers p ON p.producer=a.producer AND p.stream=a.stream
 AND p.authority_role=a.authority_role AND p.grant_id=a.grant_id AND p.grant_generation=a.grant_generation
JOIN receipt_admissions ra ON ra.receipt_key=a.receipt_key AND ra.envelope_digest=a.envelope_digest
 AND ra.producer=a.producer AND ra.stream=a.stream
JOIN transport_receipts r ON r.producer=ra.producer AND r.batch=ra.batch AND r.state='applied'
WHERE a.grant_generation>0 AND f.kind IN ('runtime-native-inventory/1','runtime-native-inventory-source/1','learn-installed-origin/1')
 AND ((f.item_id=$item AND json_extract(f.canonical,'$.invocationId')=$invocation)
      OR (f.kind='learn-installed-origin/1' AND f.item_id IS NULL))
ORDER BY f.identity LIMIT 65;
"""
        parameter command "$item" item
        parameter command "$invocation" invocation
        let rows =
            use reader = command.ExecuteReader()
            [ while reader.Read() do
                yield reader.GetString 0,reader.GetString 1,reader.GetInt64 2,reader.GetString 3,
                      reader.GetString 4,reader.GetString 5,reader.GetString 6,reader.GetString 7,reader.GetInt64 8 ]
        if rows.Length > 64 then invalidOp "efficiency-native-witness-selection-bound"
        let byKind kind = rows |> List.filter (fun (_,actual,_,_,_,_,_,_,_) -> actual=kind)
        let valid = ResizeArray<_>()
        for source in byKind "runtime-native-inventory-source/1" do
            let _,_,_,_,sourceCanonical,producer,stream,grant,generation = source
            use sourceDocument = JsonDocument.Parse sourceCanonical
            let sourceNode = sourceDocument.RootElement
            let sourceBinding = sourceNode.GetProperty "sourceBinding"
            let bytes = Convert.FromBase64String(sourceBinding.GetProperty("bytesBase64").GetString())
            use bindingDocument = JsonDocument.Parse bytes
            let binding = bindingDocument.RootElement
            let rootInvocation = binding.GetProperty("rootInvocationId").GetString()
            use lineage = connection.CreateCommand()
            lineage.CommandText <- "SELECT count(*) FROM invocation_lineage l JOIN runtime_admissions a ON a.invocation_id=l.invocation_id AND a.item_id=l.item_id JOIN expected_dispatches d ON d.dispatch_id=l.dispatch_id AND d.item_id=l.item_id JOIN invocation_lineage rl ON rl.item_id=l.item_id AND rl.invocation_id=l.root_invocation_id AND rl.relation='root' AND rl.root_invocation_id=rl.invocation_id JOIN expected_dispatches rd ON rd.dispatch_id=rl.dispatch_id AND rd.item_id=rl.item_id AND rd.relation='root' WHERE l.item_id=$item AND l.invocation_id=$invocation AND l.root_invocation_id=$root;"
            parameter lineage "$item" item
            parameter lineage "$invocation" invocation
            parameter lineage "$root" rootInvocation
            let sameGrant (_,_,_,_,_,p,s,g,n) = p=producer && s=stream && g=grant && n=generation
            let inventories = byKind "runtime-native-inventory/1" |> List.filter (fun row ->
                let _,_,_,_,canonical,_,_,_,_ = row
                use document = JsonDocument.Parse canonical
                let node = document.RootElement
                sameGrant row && node.GetProperty("inventoryId").GetString()=sourceNode.GetProperty("inventoryId").GetString()
                && node.GetProperty("sourceDigest").GetString()=sourceNode.GetProperty("sourceDigest").GetString())
            let origins = byKind "learn-installed-origin/1" |> List.filter (fun row ->
                let _,_,_,_,canonical,_,_,_,_ = row
                use document = JsonDocument.Parse canonical
                let node = document.RootElement
                let captured = DateTimeOffset.Parse(binding.GetProperty("capturedAt").GetString(),Globalization.CultureInfo.InvariantCulture)
                let observed = DateTimeOffset.Parse(node.GetProperty("capabilityObservedAt").GetString(),Globalization.CultureInfo.InvariantCulture)
                let expires = DateTimeOffset.Parse(node.GetProperty("capabilityExpiresAt").GetString(),Globalization.CultureInfo.InvariantCulture)
                sameGrant row && captured>=observed && captured<=expires && node.GetProperty("workspaceId").GetString()=scalarText connection "SELECT value FROM store_metadata WHERE key='receiptWorkspace';"
                && node.GetProperty("producerId").GetString()=producer && node.GetProperty("streamId").GetString()=stream
                && node.GetProperty("role").GetString()="native-collector" && node.GetProperty("grantId").GetString()=grant
                && node.GetProperty("grantGeneration").GetInt64()=generation
                && DateTimeOffset.Parse(node.GetProperty("capabilityExpiresAt").GetString(),Globalization.CultureInfo.InvariantCulture)>DateTimeOffset.UtcNow)
            match inventories,origins with
            | [ inventory ],[ origin ] when Convert.ToInt64(lineage.ExecuteScalar())=1L ->
                valid.Add([ source;inventory;origin ])
            | _ -> ()
        match Seq.toList valid with
        | [ witnesses ] -> witnesses |> List.map (fun (id,kind,revision,digest,_,_,_,_,_) -> id,kind,revision,digest)
        | _ -> []

    let private efficiencyNativeUsageComplete (connection: SqliteConnection) item invocation =
        let witnesses = efficiencyNativeWitnesses connection item invocation
        match witnesses |> List.tryFind (fun (_,kind,_,_) -> kind="runtime-native-inventory/1") with
        | None -> false
        | Some(id,_,revision,digest) ->
            use inventory = connection.CreateCommand()
            inventory.CommandText <- "SELECT canonical FROM current_ingest_facts WHERE identity=$id AND revision=$revision AND content_digest=$digest;"
            parameter inventory "$id" id
            parameter inventory "$revision" revision
            parameter inventory "$digest" digest
            use document = JsonDocument.Parse(string(inventory.ExecuteScalar()))
            let node = document.RootElement
            let expected = node.GetProperty("expectedTurnIds").EnumerateArray() |> Seq.map _.GetString() |> Set.ofSeq
            use usage = connection.CreateCommand()
            usage.CommandText <- "SELECT u.turn_id,u.provider,u.requested_model,u.requested_effort,u.accounting_scope FROM runtime_turn_usage u JOIN fact_admissions a ON a.identity=u.identity WHERE u.item_id=$item AND u.invocation_id=$invocation ORDER BY u.turn_id;"
            parameter usage "$item" item
            parameter usage "$invocation" invocation
            let rows =
                use reader = usage.ExecuteReader()
                [ while reader.Read() do yield [ for column in 0..4 -> if reader.IsDBNull column then None else Some(reader.GetString column) ] ]
            // A partial inventory page never establishes a complete native population.
            node.GetProperty("page").GetInt64()=1L && node.GetProperty("pages").GetInt64()=1L
            && not expected.IsEmpty && rows.Length=expected.Count
            && (rows |> List.choose List.head |> Set.ofList)=expected
            && (rows |> List.forall (function
                | [ Some _;Some provider;Some model;Some effort;Some scope ] ->
                    provider=node.GetProperty("expectedProvider").GetString()
                    && model=node.GetProperty("requestedModel").GetString()
                    && effort=node.GetProperty("requestedEffort").GetString() && scope="completed-turn"
                | _ -> false))

    let private efficiencyAuthority connection (principal: TelemetryReceipt.Principal) (sourceIdentity: string) (provenance: JsonElement) =
        if provenance.GetProperty("sourceIdentity").GetString() <> sourceIdentity then
            invalidOp "efficiency-producer-source-mismatch"
        let reference = provenance.GetProperty "authorityRef"
        let _, effectiveItem = efficiencyReference connection reference
        let role = provenance.GetProperty("authorityRole").GetString()
        let kind = reference.GetProperty("kind").GetString()
        let validKind =
            match role with
            | "runtime-observer" -> kind = "runtime-native-inventory-source/1"
            | "ci-observer" -> kind = "native-item-outcome"
            | "root-reviewer" -> kind = "process-review"
            | "completion-analyst" -> kind = "runtime-native-inventory-source/1"
            | _ -> false
        if not validKind then invalidOp "efficiency-authority-witness-kind-mismatch"
        let nativeRole = role="runtime-observer" || role="completion-analyst"
        if nativeRole then
            let invocation = provenance.GetProperty "invocationRef"
            match effectiveItem,invocation.ValueKind with
            | Some item,JsonValueKind.String ->
                let witnesses = efficiencyNativeWitnesses connection item (invocation.GetString())
                if not (witnesses |> List.exists (fun (id,_,_,_) -> id=reference.GetProperty("id").GetString())) then
                    invalidOp "efficiency-native-authority-unavailable"
            | _ -> invalidOp "efficiency-native-invocation-unavailable"
        use command = connection.CreateCommand()
        command.CommandText <- """
SELECT count(*) FROM fact_admissions a
JOIN receipt_producers p ON p.producer=a.producer AND p.stream=a.stream
JOIN receipt_admissions ra ON ra.receipt_key=a.receipt_key AND ra.envelope_digest=a.envelope_digest
  AND ra.producer=a.producer AND ra.stream=a.stream
JOIN transport_receipts r ON r.producer=ra.producer AND r.batch=ra.batch AND r.state='applied'
WHERE a.identity=$identity AND (($native=1 AND a.authority_role='native-collector') OR (a.producer=$producer AND a.stream=$stream))
  AND p.authority_role=a.authority_role AND p.grant_id IS a.grant_id
  AND p.grant_generation IS a.grant_generation
  AND (SELECT value FROM store_metadata WHERE key='receiptWorkspace')=$workspace;
"""
        [ "$identity", box (reference.GetProperty("id").GetString())
          "$native", box (if nativeRole then 1 else 0)
          "$producer", box principal.Scope.Producer
          "$stream", box principal.Scope.Stream
          "$workspace", box principal.Scope.Workspace ]
        |> List.iter (fun (name, value) -> parameter command name value)
        if Convert.ToInt64(command.ExecuteScalar()) <> 1L then
            invalidOp "efficiency-authenticated-authority-unavailable"
        let requireOptional (name: string) (sql: string) =
            let value = provenance.GetProperty name
            if value.ValueKind = JsonValueKind.String then
                use lookup = connection.CreateCommand()
                lookup.CommandText <- sql
                parameter lookup "$reference" (value.GetString())
                parameter lookup "$item" (effectiveItem |> Option.map box |> Option.defaultValue DBNull.Value)
                if Convert.ToInt64(lookup.ExecuteScalar()) <> 1L then
                    invalidOp ("efficiency-authority-" + name + "-unavailable")
        requireOptional "rootDispatchRef" "SELECT count(*) FROM expected_dispatches WHERE (identity=$reference OR dispatch_id=$reference) AND item_id IS $item;"
        requireOptional "invocationRef" "SELECT count(*) FROM runtime_admissions WHERE (identity=$reference OR invocation_id=$reference) AND item_id IS $item;"
        effectiveItem

    let private efficiencyResourceCounter (resource: JsonElement) (source: JsonElement) =
        let text (name: string) = resource.GetProperty(name).GetString()
        let kind = source.GetProperty("kind").GetString()
        let unit = text "unit"
        let amount = resource.GetProperty("amount").GetInt64()
        let tokens =
            match unit with
            | "tokens-input" -> Some "input"
            | "tokens-output" -> Some "output"
            | "tokens-total" -> Some "total"
            | "tokens-cached-input" -> Some "cachedInput"
            | "tokens-cache-write-input" -> Some "cacheWriteInput"
            | "tokens-reasoning" -> Some "reasoning"
            | _ -> None
        match kind, tokens with
        | ("usage" | "runtime-turn-usage"), Some counter ->
            let mutable value = Unchecked.defaultof<JsonElement>
            if not (source.TryGetProperty(counter, &value)) || value.ValueKind <> JsonValueKind.Number
               || value.GetInt64() <> amount then invalidOp "efficiency-resource-amount-not-witnessed"
            let mutable provider = Unchecked.defaultof<JsonElement>
            if not (source.TryGetProperty("provider", &provider)) || provider.ValueKind <> JsonValueKind.String
               || provider.GetString() <> text "provider" then invalidOp "efficiency-resource-provider-mismatch"
            let sourceScope =
                if kind = "runtime-turn-usage" then source.GetProperty("scope").GetString()
                else "legacy-usage"
            if sourceScope <> text "accountingScope" then invalidOp "efficiency-resource-scope-mismatch"
        | ("ci-job" | "ci-step"), None when unit = "runner-seconds" ->
            let started = source.GetProperty "startedAt"
            let completed = source.GetProperty "completedAt"
            if started.ValueKind <> JsonValueKind.String || completed.ValueKind <> JsonValueKind.String then
                invalidOp "efficiency-resource-interval-unavailable"
            let first = DateTimeOffset.Parse(started.GetString(), Globalization.CultureInfo.InvariantCulture)
            let last = DateTimeOffset.Parse(completed.GetString(), Globalization.CultureInfo.InvariantCulture)
            let seconds = decimal (last.UtcTicks - first.UtcTicks) / decimal TimeSpan.TicksPerSecond
            if seconds < 0M || seconds <> decimal amount then invalidOp "efficiency-resource-amount-not-witnessed"
            if text "provider" <> "github-actions" || text "accountingScope" <> kind then
                invalidOp "efficiency-resource-scope-mismatch"
        | _ -> invalidOp "efficiency-resource-counter-unavailable"

    let private validateEfficiencyFact connection (admission: TelemetryReceipt.Admission option) sourceIdentity (fact: TelemetryStore.Fact) =
        match fact.Payload with
        | TelemetryStore.EfficiencyRecord record ->
            let admission = admission |> Option.defaultWith (fun () -> invalidOp "efficiency-authenticated-receipt-required")
            let body = EfficiencyInput.body record
            let authorityItem = efficiencyAuthority connection admission.Principal admission.Envelope.Batch.SourceIdentity (body.GetProperty "provenance")
            for reference in efficiencyReferences body do efficiencyReference connection reference |> ignore
            match fact.Kind with
            | "efficiency-resource-allocation/1" ->
                let resource = body.GetProperty "resource"
                let source, resourceItem = efficiencyReference connection (resource.GetProperty "sourceRef")
                efficiencyResourceCounter resource source
                use duplicate = connection.CreateCommand()
                duplicate.CommandText <- "SELECT count(*) FROM efficiency_allocation_context WHERE resource_identity=$resource AND unit=$unit AND provider=$provider AND accounting_scope=$scope AND identity<>$identity;"
                parameter duplicate "$resource" (resource.GetProperty("sourceRef").GetProperty("id").GetString())
                parameter duplicate "$unit" (resource.GetProperty("unit").GetString())
                parameter duplicate "$provider" (resource.GetProperty("provider").GetString())
                parameter duplicate "$scope" (resource.GetProperty("accountingScope").GetString())
                parameter duplicate "$identity" fact.Identity
                if Convert.ToInt64(duplicate.ExecuteScalar()) > 0L then invalidOp "efficiency-resource-already-allocated"
                let shares = body.GetProperty "shares" |> _.EnumerateArray() |> Seq.toList
                let mutable numerator = 0I
                let mutable denominator = 1I
                let seen = System.Collections.Generic.HashSet<string * string>()
                for share in shares do
                    let item = share.GetProperty("itemId").GetString()
                    let purpose = share.GetProperty("purpose").GetString()
                    if not (seen.Add(item, purpose)) then invalidOp "efficiency-allocation-share-duplicate"
                    let scopedResourceItem = resourceItem |> Option.orElse authorityItem
                    let supportedItem =
                        if scopedResourceItem = Some item then true
                        else
                            use relation = connection.CreateCommand()
                            relation.CommandText <- "SELECT count(*) FROM parent_child WHERE parent_id=$parent AND child_id=$child;"
                            parameter relation "$parent" (scopedResourceItem |> Option.defaultValue "")
                            parameter relation "$child" item
                            Convert.ToInt64(relation.ExecuteScalar()) > 0L
                    if not supportedItem then invalidOp "efficiency-allocation-effective-item-mismatch"
                    let fraction = share.GetProperty "fraction"
                    let n = bigint (fraction.GetProperty("numerator").GetInt64())
                    let d = bigint (fraction.GetProperty("denominator").GetInt64())
                    numerator <- numerator * d + n * denominator
                    denominator <- denominator * d
                    let gcd = System.Numerics.BigInteger.GreatestCommonDivisor(numerator, denominator)
                    numerator <- numerator / gcd
                    denominator <- denominator / gcd
                    if numerator > denominator then invalidOp "efficiency-allocation-overallocated"
                    if purpose <> "unknown" then
                        if share.GetProperty("epistemicStatus").GetString() <> "observed"
                           && share.GetProperty("epistemicStatus").GetString() <> "supported-inference" then
                            invalidOp "efficiency-purpose-unsupported-classification"
                        if share.GetProperty("evidenceRefs").GetArrayLength() = 0 then
                            invalidOp "efficiency-purpose-evidence-required"
                    if purpose = "avoidable-process" then
                        if share.GetProperty("necessity").GetString() <> "avoidable"
                           || share.GetProperty("alternative").ValueKind <> JsonValueKind.String
                           || String.IsNullOrWhiteSpace(share.GetProperty("alternative").GetString()) then
                            invalidOp "efficiency-avoidability-alternative-required"
            | "efficiency-problem-episode/1" ->
                if fact.ItemId.IsSome && authorityItem <> fact.ItemId then
                    invalidOp "efficiency-episode-authority-item-mismatch"
            | "efficiency-assessment/1" ->
                let assessment = body.GetProperty "assessment"
                let subject = assessment.GetProperty "subject"
                if assessment.GetProperty("assessmentId").GetString() <> fact.Identity
                   || assessment.GetProperty("revision").GetInt64() <> fact.Revision
                   || Some(subject.GetProperty("itemId").GetString()) <> fact.ItemId
                   || authorityItem <> fact.ItemId then invalidOp "efficiency-assessment-subject-mismatch"
                let outcome = subject.GetProperty("outcomeId").GetString()
                use outcomeCheck = connection.CreateCommand()
                outcomeCheck.CommandText <- "SELECT count(*) FROM native_item_outcomes WHERE identity=$identity AND item_id=$item;"
                parameter outcomeCheck "$identity" outcome
                parameter outcomeCheck "$item" (fact.ItemId |> Option.defaultValue "")
                if Convert.ToInt64(outcomeCheck.ExecuteScalar()) <> 1L then invalidOp "efficiency-assessment-outcome-unavailable"
                if assessment.GetProperty("lifecycle").GetProperty("state").GetString() = "ready" then
                    // A ready record may be received before named settle, but only against
                    // its already claimed request and the same witnessed runtime/population.
                    let lifecycle = assessment.GetProperty "lifecycle"
                    let epoch = subject.GetProperty "outcomeEpoch"
                    if subject.GetProperty("scope").GetString()<>"native-item" || epoch.ValueKind<>JsonValueKind.Number then
                        invalidOp "efficiency-ready-native-epoch-required"
                    for name in [ "population"; "usage"; "lineage" ] do
                        if assessment.GetProperty("coverage").GetProperty(name).GetString()<>"complete" then
                            invalidOp "efficiency-ready-population-incomplete"
                    use claimed = connection.CreateCommand()
                    claimed.CommandText <- "SELECT canonical,invocation_ref FROM efficiency_analysis_requests WHERE request_id=$id AND state='claimed' AND owner_producer=$producer AND owner_stream=$stream AND stable_outcome_identity=$outcome AND outcome_epoch=$epoch AND effective_item_id=$item;"
                    [ "$id", box (lifecycle.GetProperty("idempotencyKey").GetString())
                      "$producer", box admission.Principal.Scope.Producer; "$stream", box admission.Principal.Scope.Stream
                      "$outcome", box outcome; "$epoch", box (epoch.GetInt64()); "$item", box (fact.ItemId |> Option.defaultValue "") ]
                    |> List.iter (fun (name,value) -> parameter claimed name value)
                    let requestCanonical,invocation =
                        use reader = claimed.ExecuteReader()
                        if not (reader.Read()) || reader.IsDBNull 1 then invalidOp "efficiency-ready-claimed-runtime-unavailable"
                        reader.GetString 0,reader.GetString 1
                    use requestDocument = JsonDocument.Parse requestCanonical
                    let request = requestDocument.RootElement.GetProperty "canonicalRequest"
                    let canonical (node: JsonElement) = CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(node.GetRawText()))
                    if canonical subject<>canonical(request.GetProperty "subject")
                       || assessment.GetProperty("evidenceDigest").GetString()<>request.GetProperty("evidenceDigest").GetString()
                       || assessment.GetProperty("provenance").GetProperty("validationResult").GetString()<>"accepted" then
                        invalidOp "efficiency-ready-request-association-mismatch"
                    use closed = connection.CreateCommand()
                    closed.CommandText <- "SELECT count(*) FROM efficiency_outcome_epochs WHERE outcome_identity=$outcome AND outcome_revision=(SELECT fact_revision FROM native_item_outcomes WHERE identity=$outcome) AND epoch=$epoch AND state='closed';"
                    parameter closed "$outcome" outcome
                    parameter closed "$epoch" (epoch.GetInt64())
                    if Convert.ToInt64(closed.ExecuteScalar())<>1L then invalidOp "efficiency-ready-receiver-epoch-unavailable"
                    use terminal = connection.CreateCommand()
                    terminal.CommandText <- "SELECT count(*) FROM runtime_terminals WHERE invocation_id=$invocation AND item_id=$item;"
                    parameter terminal "$invocation" invocation
                    parameter terminal "$item" (fact.ItemId |> Option.defaultValue "")
                    if Convert.ToInt64(terminal.ExecuteScalar())<>1L then invalidOp "efficiency-ready-analysis-terminal-unavailable"
                    let usageRefs = assessment.GetProperty("provenance").GetProperty "usageRefs"
                    if usageRefs.GetArrayLength()=0 then invalidOp "efficiency-ready-analysis-usage-unavailable"
                    for usageRef in usageRefs.EnumerateArray() do
                        use usage = connection.CreateCommand()
                        usage.CommandText <- "SELECT count(*) FROM runtime_turn_usage u JOIN fact_admissions a ON a.identity=u.identity WHERE u.identity=$id AND u.invocation_id=$invocation AND u.item_id=$item;"
                        parameter usage "$id" (usageRef.GetString())
                        parameter usage "$invocation" invocation
                        parameter usage "$item" (fact.ItemId |> Option.defaultValue "")
                        if Convert.ToInt64(usage.ExecuteScalar())<>1L then invalidOp "efficiency-ready-analysis-usage-unavailable"
                    let reviewId = lifecycle.GetProperty "itemReviewRef"
                    if reviewId.ValueKind<>JsonValueKind.String then invalidOp "efficiency-ready-item-review-unavailable"
                    let reviews = assessment.GetProperty("evidenceRefs").EnumerateArray()
                                  |> Seq.filter (fun reference -> reference.GetProperty("kind").GetString()="process-review" && reference.GetProperty("id").GetString()=reviewId.GetString()) |> Seq.toArray
                    if reviews.Length<>1 then invalidOp "efficiency-ready-item-review-ambiguous"
                    use review = connection.CreateCommand()
                    review.CommandText <- "SELECT count(*) FROM process_reviews r JOIN current_ingest_facts f ON f.identity=r.identity JOIN fact_admissions a ON a.identity=r.identity WHERE r.identity=$id AND f.revision=$revision AND r.item_id=$item AND r.scope='item';"
                    parameter review "$id" (reviewId.GetString())
                    parameter review "$revision" (reviews[0].GetProperty("revision").GetInt64())
                    parameter review "$item" (fact.ItemId |> Option.defaultValue "")
                    if Convert.ToInt64(review.ExecuteScalar())<>1L then invalidOp "efficiency-ready-item-review-unavailable"
            | _ -> invalidOp "efficiency-kind-unsupported"
        | _ -> ()

    let private recordEfficiencyEpochs (connection: SqliteConnection) (admission: TelemetryReceipt.Admission) (allFacts: TelemetryStore.Fact list) (facts: TelemetryStore.Fact list) acceptedAt =
        let executeValues sql values =
            use command = connection.CreateCommand()
            command.CommandText <- sql
            values |> List.iter (fun (name,value) -> parameter command name value)
            command.ExecuteNonQuery() |> ignore
        let scalarValues sql values =
            use command = connection.CreateCommand()
            command.CommandText <- sql
            values |> List.iter (fun (name,value) -> parameter command name value)
            command.ExecuteScalar()
        executeValues "INSERT OR IGNORE INTO efficiency_receiver_order(receipt_key,producer,stream,accepted_at) VALUES($key,$producer,$stream,$accepted);"
            [ "$key", box admission.Envelope.Key; "$producer", box admission.Principal.Scope.Producer; "$stream", box admission.Principal.Scope.Stream; "$accepted", acceptedAt ]
        let receiverSequence = scalarValues "SELECT sequence FROM efficiency_receiver_order WHERE receipt_key=$key;" [ "$key", box admission.Envelope.Key ] |> Convert.ToInt64
        let reference (fact: TelemetryStore.Fact) =
            JsonSerializer.SerializeToNode({| id = fact.Identity; kind = fact.Kind; revision = fact.Revision; contentDigest = "sha256:" + fact.ContentDigest |})
        // Begin/open and root dispatch must occur in the same authenticated batch.
        // A replayed open population is permitted, but a genuinely new root dispatch is required.
        for dispatch in facts do
            match dispatch.Payload, dispatch.ItemId with
            | TelemetryStore.ExpectedDispatch(_, activationId, "root", _, runtime, _, _), Some item ->
                let populations = allFacts |> List.choose (fun fact ->
                    match fact.Payload, fact.ItemId with
                    | TelemetryStore.BudgetPopulation(original, "open", "native-item", source), Some populationItem when populationItem = item ->
                        let key = CanonicalJson.sha256(Encoding.UTF8.GetBytes(item + "\u001f" + original)).Substring(0,32)
                        if fact.Identity = "budget-population-" + key && source = "roadmap-dispatch:" + key then Some(original,fact) else None
                    | _ -> None)
                let activations = allFacts |> List.filter (fun fact ->
                    match fact.Payload, fact.ItemId with
                    | TelemetryStore.OperationalActivation(id, "explicit-future-dispatches", actualRuntime, _, _, _), Some actualItem -> id = activationId && actualRuntime = runtime && actualItem = item
                    | _ -> false)
                match populations, activations with
                | [ declaredOriginal, population ], [ activation ] ->
                    // Resolve only actual supported correction edges to a previously witnessed
                    // stable original. A new alias never creates a fresh sequence counter.
                    use aliases = connection.CreateCommand()
                    aliases.CommandText <- "WITH RECURSIVE aliases(item) AS (SELECT $item UNION SELECT c.prior_item FROM ci_attribution_corrections c JOIN aliases a ON c.effective_item=a.item UNION SELECT c.effective_item FROM ci_attribution_corrections c JOIN aliases a ON c.prior_item=a.item) SELECT DISTINCT e.original_item_id FROM efficiency_outcome_epochs e JOIN aliases a ON a.item=e.effective_item_id LIMIT 2;"
                    parameter aliases "$item" item
                    let knownOriginals =
                        use reader = aliases.ExecuteReader()
                        [ while reader.Read() do yield reader.GetString 0 ]
                    let original =
                        match knownOriginals with
                        | [] -> declaredOriginal
                        | [ original ] -> original
                        | _ -> invalidOp "efficiency-epoch-correction-lineage-conflict"
                    let existing = scalarValues "SELECT count(*) FROM efficiency_outcome_epochs WHERE dispatch_identity=$id;" [ "$id", box dispatch.Identity ] |> Convert.ToInt64
                    if existing = 0L then
                        let openCount = scalarValues "SELECT count(*) FROM efficiency_outcome_epochs WHERE original_item_id=$original AND state='open';" [ "$original", box original ] |> Convert.ToInt64
                        if openCount > 0L then
                            executeValues "INSERT OR IGNORE INTO efficiency_epoch_gaps VALUES($key,$original,'new-root-before-prior-receiver-closure');" [ "$key", box admission.Envelope.Key; "$original", box original ]
                        else
                            let epoch = scalarValues "SELECT COALESCE(max(epoch),0)+1 FROM efficiency_outcome_epochs WHERE original_item_id=$original;" [ "$original", box original ] |> Convert.ToInt64
                            let refs = JsonArray(reference population, reference activation, reference dispatch)
                            executeValues "INSERT INTO efficiency_outcome_epochs(original_item_id,epoch,dispatch_identity,effective_item_id,begin_receipt_key,begin_sequence,state,begin_refs) VALUES($original,$epoch,$dispatch,$item,$key,$sequence,'open',$refs);"
                                [ "$original", box original; "$epoch", box epoch; "$dispatch", box dispatch.Identity; "$item", box item; "$key", box admission.Envelope.Key; "$sequence", box receiverSequence; "$refs", box (refs.ToJsonString()) ]
                | _ -> () // No reconstructed or timestamp-inferred begin authority.
            | _ -> ()
        // Re-evaluate only prospectively witnessed sequences. Every closing member must
        // have a receiver clock at/after this begin; historical current rows cannot close it.
        use openCommand = connection.CreateCommand()
        openCommand.CommandText <- "SELECT original_item_id,epoch,dispatch_identity,effective_item_id,begin_sequence FROM efficiency_outcome_epochs WHERE state='open' ORDER BY original_item_id,epoch;"
        let openRows =
            use reader = openCommand.ExecuteReader()
            [ while reader.Read() do yield reader.GetString 0,reader.GetInt64 1,reader.GetString 2,reader.GetString 3,reader.GetInt64 4 ]
        for original, epoch, dispatch, item, beginSequence in openRows do
            let rootValues = [ "$dispatch", box dispatch; "$item", box item; "$sequence", box beginSequence ]
            use witness = connection.CreateCommand()
            witness.CommandText <- """
SELECT f.identity,f.kind,f.revision,f.content_digest,q.sequence
FROM expected_dispatches d JOIN invocation_lineage l ON l.item_id=d.item_id AND l.dispatch_id=d.dispatch_id
JOIN runtime_admissions a ON a.item_id=l.item_id AND a.invocation_id=l.invocation_id
JOIN runtime_terminals t ON t.item_id=l.item_id AND t.invocation_id=l.invocation_id
JOIN current_ingest_facts f ON f.identity IN (d.identity,l.identity,a.identity,t.identity)
JOIN fact_acceptance_times c ON c.identity=f.identity AND c.fact_revision=f.revision AND c.content_digest=f.content_digest
JOIN efficiency_receiver_order q ON q.receipt_key=c.receipt_key
WHERE d.identity=$dispatch AND d.item_id=$item AND d.relation='root' AND l.relation='root'
AND l.root_invocation_id=l.invocation_id AND q.sequence >= $sequence
AND (SELECT count(*) FROM invocation_lineage other WHERE other.item_id=d.item_id AND other.dispatch_id=d.dispatch_id)=1
ORDER BY f.identity;
"""
            rootValues |> List.iter (fun (name,value) -> parameter witness name value)
            let rootRefs =
                use reader = witness.ExecuteReader()
                [ while reader.Read() do yield reader.GetString 0,reader.GetString 1,reader.GetInt64 2,reader.GetString 3,reader.GetInt64 4 ]
            if rootRefs.Length = 4 then
                // Existing rooted child/follow-up populations must all have actual terminals.
                let unsettledSql = "WITH RECURSIVE population(dispatch_id) AS ( SELECT dispatch_id FROM expected_dispatches WHERE identity=$dispatch AND item_id=$item UNION SELECT d.dispatch_id FROM expected_dispatches d JOIN population p ON p.dispatch_id=d.parent_dispatch_id WHERE d.item_id=$item ) SELECT count(*) FROM expected_dispatches d JOIN population p ON p.dispatch_id=d.dispatch_id WHERE d.item_id=$item AND NOT EXISTS( SELECT 1 FROM invocation_lineage l JOIN runtime_admissions a ON a.item_id=l.item_id AND a.invocation_id=l.invocation_id JOIN runtime_terminals t ON t.item_id=l.item_id AND t.invocation_id=l.invocation_id JOIN expected_dispatches root ON root.identity=$dispatch JOIN invocation_lineage rl ON rl.item_id=root.item_id AND rl.dispatch_id=root.dispatch_id WHERE l.item_id=d.item_id AND l.dispatch_id=d.dispatch_id AND l.root_invocation_id=rl.invocation_id AND (SELECT count(*) FROM invocation_lineage other WHERE other.item_id=l.item_id AND other.dispatch_id=l.dispatch_id)=1 AND NOT EXISTS(SELECT 1 FROM current_ingest_facts f WHERE f.identity IN (d.identity,l.identity,a.identity,t.identity) AND NOT EXISTS( SELECT 1 FROM fact_acceptance_times c JOIN efficiency_receiver_order q ON q.receipt_key=c.receipt_key WHERE c.identity=f.identity AND c.fact_revision=f.revision AND c.content_digest=f.content_digest AND q.sequence>=$sequence)));"
                let unsettled = scalarValues unsettledSql rootValues |> Convert.ToInt64
                use outcomeCommand = connection.CreateCommand()
                outcomeCommand.CommandText <- "SELECT f.identity,f.kind,f.revision,f.content_digest,q.sequence FROM native_item_outcomes o JOIN current_ingest_facts f ON f.identity=o.identity JOIN ingest_facts raw ON raw.identity=o.identity JOIN fact_acceptance_times c ON c.identity=f.identity AND c.fact_revision=f.revision AND c.content_digest=f.content_digest JOIN efficiency_receiver_order q ON q.receipt_key=c.receipt_key WHERE (o.item_id=$item OR raw.item_id=$item) AND q.sequence>$sequence AND NOT EXISTS(SELECT 1 FROM efficiency_outcome_epochs e WHERE e.outcome_identity=o.identity AND e.outcome_revision=f.revision) ORDER BY q.sequence,f.identity LIMIT 2;"
                rootValues |> List.iter (fun (name,value) -> parameter outcomeCommand name value)
                let outcomes =
                    use reader = outcomeCommand.ExecuteReader()
                    [ while reader.Read() do yield reader.GetString 0,reader.GetString 1,reader.GetInt64 2,reader.GetString 3,reader.GetInt64 4 ]
                use nativeInvocations = connection.CreateCommand()
                nativeInvocations.CommandText <- "WITH RECURSIVE population(dispatch_id) AS (SELECT dispatch_id FROM expected_dispatches WHERE identity=$dispatch AND item_id=$item UNION SELECT d.dispatch_id FROM expected_dispatches d JOIN population p ON p.dispatch_id=d.parent_dispatch_id WHERE d.item_id=$item) SELECT l.invocation_id FROM invocation_lineage l JOIN population p ON p.dispatch_id=l.dispatch_id WHERE l.item_id=$item ORDER BY l.invocation_id LIMIT 4097;"
                rootValues |> List.iter (fun (name,value) -> parameter nativeInvocations name value)
                let invocations =
                    use reader = nativeInvocations.ExecuteReader()
                    [ while reader.Read() do yield reader.GetString 0 ]
                let nativeRefs = invocations |> List.map (efficiencyNativeWitnesses connection item)
                let nativeComplete = not invocations.IsEmpty && invocations.Length<=4096 && (nativeRefs |> List.forall (List.isEmpty >> not))
                match outcomes with
                | [ outcomeId, outcomeKind, outcomeRevision, outcomeDigest, outcomeSequence ] when unsettled = 0L && nativeComplete ->
                    let refs = JsonArray()
                    use allWitness = connection.CreateCommand()
                    allWitness.CommandText <- "WITH RECURSIVE population(dispatch_id) AS (SELECT dispatch_id FROM expected_dispatches WHERE identity=$dispatch AND item_id=$item UNION SELECT d.dispatch_id FROM expected_dispatches d JOIN population p ON p.dispatch_id=d.parent_dispatch_id WHERE d.item_id=$item) SELECT DISTINCT f.identity,f.kind,f.revision,f.content_digest,q.sequence FROM expected_dispatches d JOIN population p ON p.dispatch_id=d.dispatch_id JOIN invocation_lineage l ON l.item_id=d.item_id AND l.dispatch_id=d.dispatch_id JOIN runtime_admissions a ON a.invocation_id=l.invocation_id AND a.item_id=l.item_id JOIN runtime_terminals t ON t.invocation_id=l.invocation_id AND t.item_id=l.item_id JOIN current_ingest_facts f ON f.identity IN (d.identity,l.identity,a.identity,t.identity) JOIN fact_acceptance_times c ON c.identity=f.identity AND c.fact_revision=f.revision AND c.content_digest=f.content_digest JOIN efficiency_receiver_order q ON q.receipt_key=c.receipt_key WHERE d.item_id=$item AND q.sequence>=$sequence ORDER BY f.identity LIMIT 4097;"
                    rootValues |> List.iter (fun (name,value) -> parameter allWitness name value)
                    let closingRefs =
                        use reader = allWitness.ExecuteReader()
                        [ while reader.Read() do yield reader.GetString 0,reader.GetString 1,reader.GetInt64 2,reader.GetString 3,reader.GetInt64 4 ]
                    if closingRefs.Length > 4096 then invalidOp "efficiency-epoch-witness-selection-bound"
                    for id,kind,revision,digest,_ in closingRefs do refs.Add(JsonSerializer.SerializeToNode({|id=id;kind=kind;revision=revision;contentDigest="sha256:"+digest|}))
                    for id,kind,revision,digest in nativeRefs |> List.concat |> List.distinct do
                        refs.Add(JsonSerializer.SerializeToNode({|id=id;kind=kind;revision=revision;contentDigest="sha256:"+digest|}))
                    refs.Add(JsonSerializer.SerializeToNode({|id=outcomeId;kind=outcomeKind;revision=outcomeRevision;contentDigest="sha256:"+outcomeDigest|}))
                    executeValues "UPDATE efficiency_outcome_epochs SET state='closed',outcome_identity=$outcome,outcome_revision=$outcomeRevision,close_sequence=$sequence,close_refs=$refs WHERE original_item_id=$original AND epoch=$epoch AND state='open';"
                        [ "$outcome", box outcomeId; "$outcomeRevision",box outcomeRevision; "$sequence", box (max receiverSequence outcomeSequence); "$refs", box (refs.ToJsonString()); "$original", box original; "$epoch", box epoch ]
                | _ -> ()

    let private recordEfficiencyAcceptance (connection: SqliteConnection) admission (allFacts: TelemetryStore.Fact list) (facts: TelemetryStore.Fact list) =
        match admission with
        | None -> () // Historical/anonymous ingestion does not acquire an invented receiver clock.
        | Some (admission: TelemetryReceipt.Admission) ->
            use receipt = connection.CreateCommand()
            receipt.CommandText <- "SELECT terminal_utc FROM transport_receipts WHERE producer=$producer AND batch=$batch AND stream=$stream AND state='applied';"
            parameter receipt "$producer" admission.Principal.Scope.Producer
            parameter receipt "$batch" admission.Envelope.BatchId
            parameter receipt "$stream" admission.Principal.Scope.Stream
            let acceptedAt = receipt.ExecuteScalar()
            if isNull acceptedAt || acceptedAt = box DBNull.Value then
                invalidOp "efficiency-receiver-acceptance-clock-unavailable"
            for fact in facts do
                use command = connection.CreateCommand()
                command.CommandText <- "INSERT INTO fact_acceptance_times VALUES($identity,$revision,$digest,$accepted,$receipt);"
                [ "$identity", box fact.Identity; "$revision", box fact.Revision
                  "$digest", box fact.ContentDigest; "$accepted", acceptedAt
                  "$receipt", box admission.Envelope.Key ]
                |> List.iter (fun (name, value) -> parameter command name value)
                command.ExecuteNonQuery() |> ignore
                match fact.Payload with
                | TelemetryStore.EfficiencyRecord _ ->
                    use history = connection.CreateCommand()
                    history.CommandText <- "INSERT INTO efficiency_record_history VALUES($identity,$revision,$digest,$canonical,$accepted,$receipt);"
                    [ "$identity", box fact.Identity; "$revision", box fact.Revision
                      "$digest", box fact.ContentDigest; "$canonical", box fact.Canonical
                      "$accepted", acceptedAt; "$receipt", box admission.Envelope.Key ]
                    |> List.iter (fun (name, value) -> parameter history name value)
                    history.ExecuteNonQuery() |> ignore
                | _ -> ()

            recordEfficiencyEpochs connection admission allFacts facts acceptedAt

    let private ingestBatchWithReceiptLocked
        root
        beforeCommit
        reevaluateBudget
        finishReceipt
        factAdmission
        (batch: TelemetryStore.Batch)
        =
        if not (File.Exists(Path.Combine(root, databaseFileName))) then
            Error [ "telemetry store is not initialized" ]
        else
            match connect root SqliteOpenMode.ReadWrite with
            | Error errors -> Error errors
            | Ok(connection, engine) ->
                use connection = connection

                try
                    let version = Int32.Parse(scalarText connection "PRAGMA user_version;")

                    if version > currentSchemaVersion then
                        Error
                            [
                                $"store schema version %d{version} is newer than supported version %d{currentSchemaVersion}"
                            ]
                    elif version <> currentSchemaVersion then
                        Error [ "telemetry store schema requires migration; run telemetry store init" ]
                    else
                        beginImmediate connection

                        try
                            match factAdmission with
                            | Some(admission: TelemetryReceipt.Admission) when admission.Principal.Role = TelemetryReceipt.NativeCollector ->
                                let allowed =
                                    set [ "runtime-native-inventory/1"; "runtime-native-inventory-source/1";
                                          "learn-shared-cost/1"; "learn-shared-cost-authority/1";
                                          "learn-native-delivery-source/1"; "learn-installed-origin/1" ]
                                if batch.Facts |> List.exists (fun fact -> not (allowed.Contains fact.Kind)) then
                                    invalidOp "invalid-request"
                            | Some _ when batch.Facts |> List.exists (fun fact -> fact.Kind = "learn-installed-origin/1") ->
                                invalidOp "invalid-request"
                            | None when batch.Facts |> List.exists (fun fact -> fact.Kind = "learn-installed-origin/1") ->
                                invalidOp "invalid-request"
                            | _ -> ()

                            use priorBatch = connection.CreateCommand()
                            priorBatch.CommandText <- "SELECT content_digest FROM ingest_batches WHERE ingest_id=$id;"
                            parameter priorBatch "$id" batch.IngestId
                            let prior = priorBatch.ExecuteScalar()

                            if not (isNull prior) && string prior <> batch.ContentDigest then
                                invalidOp "ingest identity conflicts with different batch content"

                            let mutable accepted = 0L
                            let mutable replayed = 0L
                            let acceptedFacts = ResizeArray<TelemetryStore.Fact>()

                            for fact in batch.Facts do
                                validateEfficiencyFact connection factAdmission batch.SourceIdentity fact
                                use fenced = connection.CreateCommand()
                                fenced.CommandText <- "SELECT count(*) FROM ci_effective_attribution WHERE identity=$identity;"
                                parameter fenced "$identity" fact.Identity
                                if Convert.ToInt64(fenced.ExecuteScalar()) > 0L then
                                    use retained = connection.CreateCommand()
                                    retained.CommandText <- "SELECT content_digest FROM ingest_facts WHERE identity=$identity;"
                                    parameter retained "$identity" fact.Identity
                                    if string (retained.ExecuteScalar()) <> fact.ContentDigest then
                                        invalidOp "ci-attribution-corrected-fact-is-immutable"
                                else
                                    use candidate = connection.CreateCommand()
                                    let values =
                                        match fact.Payload with
                                        | TelemetryStore.NativeItemOutcome(repository, pr, baseRef, baseSha, head, _, _, _, _, _, _, _) ->
                                            candidate.CommandText <- "SELECT count(*) FROM ci_attribution_corrections WHERE repository=$repository AND pr_number=$pr AND base_ref=$baseRef AND base_sha=$baseSha AND head=$head;"
                                            [ "$repository", box repository; "$pr", box pr; "$baseRef", box baseRef; "$baseSha", box baseSha; "$head", box head ]
                                        | TelemetryStore.CiBinding(_, repository, head, pr, _, _, _, _, _, _)
                                        | TelemetryStore.CiPopulationAdmission(_, repository, pr, _, _, head, _) ->
                                            candidate.CommandText <- "SELECT count(*) FROM ci_attribution_corrections WHERE repository=$repository AND pr_number=$pr AND head=$head;"
                                            [ "$repository", box repository; "$pr", box pr; "$head", box head ]
                                        | TelemetryStore.CiRun(repository, _, _, _, _, head, _, _, _, _, _) ->
                                            candidate.CommandText <- "SELECT count(*) FROM ci_attribution_corrections WHERE repository=$repository AND head=$head;"
                                            [ "$repository", box repository; "$head", box head ]
                                        | TelemetryStore.CiPage(collection, _, _, _, _)
                                        | TelemetryStore.CiCoverage(collection, _, _, _, _, _, _, _, _)
                                        | TelemetryStore.CiPopulationCoverage(collection, _, _, _, _, _, _, _, _, _) ->
                                            candidate.CommandText <- "SELECT count(*) FROM ci_effective_attribution e JOIN ci_bindings b ON b.identity=e.identity WHERE b.collection_id=$collection;"
                                            [ "$collection", box collection ]
                                        | TelemetryStore.CiJob(repository, runId, attempt, _, _, _, _, _, _, _)
                                        | TelemetryStore.CiStep(repository, runId, attempt, _, _, _, _, _, _, _, _, _) ->
                                            candidate.CommandText <- "SELECT count(*) FROM ci_effective_attribution e JOIN ci_runs r ON r.identity=e.identity WHERE r.repository=$repository AND r.run_id=$run AND r.attempt=$attempt;"
                                            [ "$repository", box repository; "$run", box runId; "$attempt", box attempt ]
                                        | _ -> []
                                    if not values.IsEmpty then
                                        values |> List.iter (fun (name, value) -> parameter candidate name value)
                                        if Convert.ToInt64(candidate.ExecuteScalar()) > 0L then
                                            invalidOp "ci-attribution-corrected-candidate-reconcile-refused"

                                use existing = connection.CreateCommand()

                                existing.CommandText <-
                                    "SELECT kind,content_digest,revision FROM ingest_facts WHERE identity=$identity;"

                                parameter existing "$kind" fact.Kind
                                parameter existing "$identity" fact.Identity
                                use reader = existing.ExecuteReader()

                                let state =
                                    if reader.Read() then
                                        Some(reader.GetString(0), reader.GetString(1), reader.GetInt64(2))
                                    else
                                        None

                                reader.Close()

                                match state with
                                | Some(oldKind, _, _) when oldKind.StartsWith("efficiency-", StringComparison.Ordinal) ->
                                    if oldKind <> fact.Kind then invalidOp "efficiency-record-kind-is-immutable"
                                    let admission = factAdmission |> Option.defaultWith (fun () -> invalidOp "efficiency-authenticated-receipt-required")
                                    use owner = connection.CreateCommand()
                                    owner.CommandText <- "SELECT count(*) FROM fact_admissions WHERE identity=$identity AND producer=$producer AND stream=$stream;"
                                    parameter owner "$identity" fact.Identity
                                    parameter owner "$producer" admission.Principal.Scope.Producer
                                    parameter owner "$stream" admission.Principal.Scope.Stream
                                    if Convert.ToInt64(owner.ExecuteScalar()) <> 1L then invalidOp "efficiency-record-owner-mismatch"
                                | _ -> ()

                                match state with
                                | Some(_, digest, _) when digest = fact.ContentDigest -> replayed <- replayed + 1L
                                | Some(_, _, revision) when fact.Revision <= revision ->
                                    invalidOp $"native fact identity conflict: %s{fact.Kind}/%s{fact.Identity}"
                                | Some(oldKind, _, _) when oldKind.StartsWith("learn-", StringComparison.Ordinal)
                                    || oldKind = "runtime-native-inventory/1"
                                    || oldKind = "runtime-native-inventory-source/1" ->
                                    invalidOp $"%s{oldKind} is immutable after pre-dispatch persistence"
                                | Some(oldKind, oldDigest, revision) ->
                                    use correction = connection.CreateCommand()

                                    correction.CommandText <-
                                        "INSERT INTO corrections(kind,identity,old_revision,new_revision,old_digest,new_digest) VALUES($kind,$identity,$old,$new,$oldDigest,$newDigest); UPDATE ingest_facts SET kind=$kind,item_id=$item,revision=$new,content_digest=$newDigest,canonical=$canonical WHERE identity=$identity;"

                                    [
                                        "$kind", box fact.Kind
                                        "$identity", fact.Identity
                                        "$old", revision
                                        "$new", fact.Revision
                                        "$oldDigest", oldDigest
                                        "$newDigest", fact.ContentDigest
                                        "$item", fact.ItemId |> Option.map box |> Option.defaultValue DBNull.Value
                                        "$canonical", fact.Canonical
                                    ]
                                    |> List.iter (fun (name, value) -> parameter correction name value)

                                    correction.ExecuteNonQuery() |> ignore

                                    let preservedTable =
                                        if oldKind <> fact.Kind then
                                            None
                                        elif fact.Kind = "ci-population-admission" then
                                            Some "ci_population_admissions"
                                        elif fact.Kind = "ci-binding" then
                                            Some "ci_bindings"
                                        else
                                            None

                                    deleteTyped connection fact.Identity preservedTable
                                    insertTyped connection fact
                                    acceptedFacts.Add fact
                                    accepted <- accepted + 1L
                                | None ->
                                    use insert = connection.CreateCommand()

                                    insert.CommandText <-
                                        "INSERT INTO ingest_facts(kind,identity,item_id,revision,content_digest,canonical) VALUES($kind,$identity,$item,$revision,$digest,$canonical);"

                                    [
                                        "$kind", box fact.Kind
                                        "$identity", fact.Identity
                                        "$item", fact.ItemId |> Option.map box |> Option.defaultValue DBNull.Value
                                        "$revision", fact.Revision
                                        "$digest", fact.ContentDigest
                                        "$canonical", fact.Canonical
                                    ]
                                    |> List.iter (fun (name, value) -> parameter insert name value)

                                    insert.ExecuteNonQuery() |> ignore
                                    if fact.Kind.StartsWith("learn-", StringComparison.Ordinal)
                                       || fact.Kind = "runtime-native-inventory/1"
                                       || fact.Kind = "runtime-native-inventory-source/1" then
                                        use order = connection.CreateCommand()
                                        order.CommandText <- "INSERT INTO learning_fact_order(identity) VALUES($identity);"
                                        parameter order "$identity" fact.Identity
                                        order.ExecuteNonQuery() |> ignore
                                    insertTyped connection fact
                                    match factAdmission with
                                    | Some admission ->
                                        let principal = admission.Principal
                                        use provenance = connection.CreateCommand()
                                        provenance.CommandText <-
                                            "INSERT INTO fact_admissions(identity,producer,stream,authority_role,grant_id,grant_generation,receipt_key,envelope_digest) VALUES($identity,$producer,$stream,$role,$grant,$generation,$key,$digest);"
                                        [ "$identity", box fact.Identity; "$producer", box principal.Scope.Producer
                                          "$stream", box principal.Scope.Stream; "$role", box (roleName principal.Role)
                                          "$grant", principal.GrantId |> Option.map box |> Option.defaultValue DBNull.Value
                                          "$generation", principal.GrantGeneration |> Option.map box |> Option.defaultValue DBNull.Value
                                          "$key", box admission.Envelope.Key; "$digest", box admission.Envelope.Digest ]
                                        |> List.iter (fun (name, value) -> parameter provenance name value)
                                        provenance.ExecuteNonQuery() |> ignore
                                    | None -> ()
                                    acceptedFacts.Add fact
                                    accepted <- accepted + 1L

                            use source = connection.CreateCommand()

                            source.CommandText <-
                                "INSERT INTO source_cursors(source_identity,generation,cursor,batch_digest) VALUES($source,$generation,$cursor,$digest) ON CONFLICT(source_identity,generation) DO UPDATE SET cursor=excluded.cursor,batch_digest=excluded.batch_digest;"

                            [
                                "$source", box batch.SourceIdentity
                                "$generation", batch.Generation
                                "$cursor", batch.Cursor
                                "$digest", batch.ContentDigest
                            ]
                            |> List.iter (fun (name, value) -> parameter source name value)

                            source.ExecuteNonQuery() |> ignore

                            if isNull prior then
                                use insertBatch = connection.CreateCommand()

                                insertBatch.CommandText <-
                                    "INSERT INTO ingest_batches VALUES($id,$digest,$source,$generation,$cursor,$accepted,$replayed);"

                                [
                                    "$id", box batch.IngestId
                                    "$digest", batch.ContentDigest
                                    "$source", batch.SourceIdentity
                                    "$generation", batch.Generation
                                    "$cursor", batch.Cursor
                                    "$accepted", accepted
                                    "$replayed", replayed
                                ]
                                |> List.iter (fun (name, value) -> parameter insertBatch name value)

                                insertBatch.ExecuteNonQuery() |> ignore

                            if reevaluateBudget then
                                budgetReevaluate connection

                            finishReceipt connection
                            recordEfficiencyAcceptance connection factAdmission batch.Facts (List.ofSeq acceptedFacts)
                            beforeCommit ()
                            execute connection "COMMIT;"

                            Ok(
                                JsonSerializer.Serialize
                                    {|
                                        schema = "fsgg.telemetry.ingest-result/1"
                                        ingestId = batch.IngestId
                                        digest = batch.ContentDigest
                                        accepted = accepted
                                        replayed = replayed
                                        cursor = batch.Cursor
                                        nativeEngine = engine
                                    |}
                                + "\n"
                            )
                        with error ->
                            rollback connection
                            raise error
                with
                | :? SqliteException as error -> Error(failBusy error)
                | error -> Error [ error.Message ]

    let private ingestBatchLocked root beforeCommit reevaluateBudget batch =
        ingestBatchWithReceiptLocked root beforeCommit reevaluateBudget ignore None batch

    let publish path assessment bytes =
        match validateRoot path assessment, TelemetryStore.parseBatch bytes with
        | Error errors, _
        | _, Error errors -> Error errors
        | Ok root, Ok batch ->
            try
                if not (File.Exists(Path.Combine(root, databaseFileName))) then
                    Error [ "telemetry store is not initialized" ]
                else
                    match tryWriterLock root with
                    | Error errors -> Error errors
                    | Ok writer ->
                        use writer = writer

                        match isReceiptScoped root with
                        | Error errors -> Error errors
                        | Ok true -> Error [ "scoped-store-requires-receipt-ingestion" ]
                        | Ok false ->
                            let inbox = Path.Combine(root, "inbox")
                            let producer = Path.Combine(inbox, batch.SourceIdentity)
                            Directory.CreateDirectory producer |> ignore

                            if not (OperatingSystem.IsWindows()) then
                                File.SetUnixFileMode(
                                    inbox,
                                    UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
                                )

                                File.SetUnixFileMode(
                                    producer,
                                    UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
                                )

                            let pending =
                                Directory.EnumerateFiles(producer, "*.ready", SearchOption.TopDirectoryOnly)
                                |> Seq.truncate (maxPendingPerProducer + 1)
                                |> Seq.length

                            if pending >= maxPendingPerProducer then
                                Error [ "producer inbox is full" ]
                            else
                                let ready =
                                    Path.Combine(producer, $"%s{batch.IngestId}.%s{batch.ContentDigest}.ready")

                                if File.Exists ready then
                                    let existing = File.ReadAllBytes ready

                                    match TelemetryStore.parseBatch existing with
                                    | Ok current when current.ContentDigest = batch.ContentDigest ->
                                        Ok(
                                            JsonSerializer.Serialize
                                                {|
                                                    schema = "fsgg.telemetry.publish-result/1"
                                                    status = "already-queued"
                                                    producer = batch.SourceIdentity
                                                    ingestId = batch.IngestId
                                                    digest = batch.ContentDigest
                                                |}
                                            + "\n"
                                        )
                                    | _ -> Error [ "ready publication identity collision" ]
                                else
                                    let nonce = Guid.NewGuid().ToString("N")
                                    let temporary = Path.Combine(producer, $".%s{batch.IngestId}.%s{nonce}.tmp")

                                    try
                                        use stream =
                                            new FileStream(
                                                temporary,
                                                FileMode.CreateNew,
                                                FileAccess.Write,
                                                FileShare.None,
                                                4096,
                                                FileOptions.WriteThrough
                                            )

                                        if not (OperatingSystem.IsWindows()) then
                                            File.SetUnixFileMode(
                                                temporary,
                                                UnixFileMode.UserRead ||| UnixFileMode.UserWrite
                                            )

                                        stream.Write bytes
                                        stream.Flush(true)
                                        stream.Close()
                                        File.Move(temporary, ready, false)
                                        fsyncDirectory producer

                                        Ok(
                                            JsonSerializer.Serialize
                                                {|
                                                    schema = "fsgg.telemetry.publish-result/1"
                                                    status = "queued"
                                                    producer = batch.SourceIdentity
                                                    ingestId = batch.IngestId
                                                    digest = batch.ContentDigest
                                                |}
                                            + "\n"
                                        )
                                    finally
                                        if File.Exists temporary then
                                            File.Delete temporary
            with error ->
                Error [ error.Message ]

    let private fairReadyFiles root =
        let inbox = Path.Combine(root, "inbox")

        if not (Directory.Exists inbox) then
            []
        else
            let cursorPath = Path.Combine(root, "drain.cursor")

            let prior =
                if File.Exists cursorPath then
                    File.ReadAllText(cursorPath).Trim()
                else
                    ""

            let directories = Directory.EnumerateDirectories inbox |> Seq.sort |> Seq.toArray

            let start =
                directories
                |> Array.tryFindIndex (fun directory -> String.CompareOrdinal(DirectoryInfo(directory).Name, prior) > 0)
                |> Option.defaultValue 0

            let ordered =
                if start = 0 then
                    directories
                else
                    Array.append directories[start..] directories[.. start - 1]

            let queues =
                ordered
                |> Seq.map (fun directory ->
                    Collections.Generic.Queue<string>(Directory.EnumerateFiles(directory, "*.ready") |> Seq.sort))
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
                            selected.Add(queue.Dequeue())
                            totalBytes <- totalBytes + size
                            progressed <- true

            List.ofSeq selected

    let private pendingCount root =
        let inbox = Path.Combine(root, "inbox")

        [ inbox; Path.Combine(root, "receipt-inbox") ]
        |> List.sumBy (fun directory ->
            if Directory.Exists directory then
                Directory.EnumerateFiles(directory, "*.ready", SearchOption.AllDirectories)
                |> Seq.truncate 1025
                |> Seq.length
            else
                0)

    let private quarantine root (path: string) reasons =
        let producer = DirectoryInfo(Path.GetDirectoryName path).Name
        let targetDirectory = Path.Combine(root, "quarantine", producer)
        Directory.CreateDirectory targetDirectory |> ignore

        if not (OperatingSystem.IsWindows()) then
            File.SetUnixFileMode(
                targetDirectory,
                UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
            )

        let initial = Path.Combine(targetDirectory, Path.GetFileName(path) + ".rejected")

        let target =
            if File.Exists initial then
                initial + "." + Guid.NewGuid().ToString("N")
            else
                initial

        File.Move(path, target, false)
        let bounded = String.concat "; " reasons

        let reason =
            if bounded.Length <= 2048 then
                bounded
            else
                bounded.Substring(0, 2048)

        File.WriteAllText(target + ".reason", reason, UTF8Encoding(false))

        if not (OperatingSystem.IsWindows()) then
            File.SetUnixFileMode(target, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            File.SetUnixFileMode(target + ".reason", UnixFileMode.UserRead ||| UnixFileMode.UserWrite)

        fsyncDirectory targetDirectory
        fsyncDirectory (Path.GetDirectoryName path)

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
                        let bytes =
                            try
                                File.ReadAllBytes ready
                            with error ->
                                failures <- error.Message :: failures
                                Array.empty

                        let parsed = TelemetryStore.parseBatch bytes

                        let nameValid (batch: TelemetryStore.Batch) =
                            Path.GetFileName ready = $"%s{batch.IngestId}.%s{batch.ContentDigest}.ready"

                        match parsed with
                        | Error errors ->
                            quarantine root ready errors
                            quarantined <- quarantined + 1
                        | Ok batch when not (nameValid batch) ->
                            quarantine root ready [ "ready filename does not match batch identity and digest" ]
                            quarantined <- quarantined + 1
                        | Ok batch ->
                            match ingestBatchLocked root hooks.BeforeCommit (not reevaluated) batch with
                            | Error errors when
                                errors |> List.exists (fun error ->
                                    error = "ci-attribution-corrected-fact-is-immutable"
                                    || error = "ci-attribution-corrected-candidate-reconcile-refused") ->
                                // These exact permanent conflicts retain their evidence and
                                // explicit refusal without poisoning later independent batches.
                                quarantine root ready errors
                                quarantined <- quarantined + 1
                                failures <- (String.concat "; " errors) :: failures
                            | Error errors when
                                errors
                                |> List.exists (fun error ->
                                    error.Contains("identity conflict", StringComparison.Ordinal)
                                    || error.Contains("UNIQUE constraint failed: budget_", StringComparison.Ordinal)
                                    || error.Contains(
                                        "UNIQUE constraint failed: operational_event_times",
                                        StringComparison.Ordinal
                                    )
                                    || error.Contains(
                                        "UNIQUE constraint failed: ci_population_",
                                        StringComparison.Ordinal
                                    )
                                    || error.Contains(
                                        "UNIQUE constraint failed: ci_check_runs",
                                        StringComparison.Ordinal
                                    )
                                    || error.Contains(
                                        "UNIQUE constraint failed: process_reviews",
                                        StringComparison.Ordinal
                                    )
                                    || error.Contains(
                                        "UNIQUE constraint failed: activity_spans",
                                        StringComparison.Ordinal
                                    )
                                    || error.Contains(
                                        "UNIQUE constraint failed: activity_usage_attributions",
                                        StringComparison.Ordinal
                                    )
                                    || error.Contains(
                                        "UNIQUE constraint failed: complication_events",
                                        StringComparison.Ordinal
                                    ))
                                ->
                                quarantine root ready errors
                                quarantined <- quarantined + 1
                            | Error errors -> failures <- (String.concat "; " errors) :: failures
                            | Ok result ->
                                reevaluated <- true
                                use doc = JsonDocument.Parse result
                                accepted <- accepted + doc.RootElement.GetProperty("accepted").GetInt32()
                                replayed <- replayed + doc.RootElement.GetProperty("replayed").GetInt32()

                                try
                                    hooks.AfterCommitBeforeDelete()
                                    File.Delete ready
                                    fsyncDirectory (Path.GetDirectoryName ready)
                                with error ->
                                    failures <- ("committed but ready removal failed: " + error.Message) :: failures

                    if not reevaluated then
                        match connect root SqliteOpenMode.ReadWrite with
                        | Error errors -> failures <- String.concat "; " errors :: failures
                        | Ok(connection, _) ->
                            use connection = connection

                            try
                                beginImmediate connection
                                budgetReevaluate connection
                                execute connection "COMMIT;"
                            with error ->
                                rollback connection
                                failures <- error.Message :: failures

                    match List.tryLast selected with
                    | Some last ->
                        let cursorPath = Path.Combine(root, "drain.cursor")
                        let temporary = cursorPath + ".tmp"

                        File.WriteAllText(
                            temporary,
                            DirectoryInfo(Path.GetDirectoryName last).Name,
                            UTF8Encoding(false)
                        )

                        File.Move(temporary, cursorPath, true)
                        fsyncDirectory root
                    | None -> ()

                    if not failures.IsEmpty then
                        Error(List.rev failures)
                    else
                        Ok(
                            JsonSerializer.Serialize
                                {|
                                    schema = "fsgg.telemetry.drain-result/1"
                                    accepted = accepted
                                    replayed = replayed
                                    quarantined = quarantined
                                    remaining = pendingCount root
                                |}
                            + "\n"
                        )

    let drain path assessment =
        drainWithHooks
            path
            assessment
            {
                BeforeCommit = ignore
                AfterCommitBeforeDelete = ignore
            }

    let ingest path assessment bytes =
        match publish path assessment bytes with
        | Error errors -> Error errors
        | Ok queued ->
            match drain path assessment with
            | Ok drained ->
                Ok(
                    JsonSerializer.Serialize
                        {|
                            schema = "fsgg.telemetry.ingest-disposition/1"
                            status = "drained"
                            publish = JsonDocument.Parse(queued).RootElement.Clone()
                            drain = JsonDocument.Parse(drained).RootElement.Clone()
                        |}
                    + "\n"
                )
            | Error [ "writer-busy" ] ->
                Ok(
                    JsonSerializer.Serialize
                        {|
                            schema = "fsgg.telemetry.ingest-disposition/1"
                            status = "queued"
                            reason = "writer-busy"
                            publish = JsonDocument.Parse(queued).RootElement.Clone()
                        |}
                    + "\n"
                )
            | Error errors -> Error errors

    // A scoped store has exactly one immutable workspace association. Legacy roots remain
    // unassigned: enrollment never infers ownership of pre-existing observations.
    let private receiptCommand (connection: SqliteConnection) sql (values: (string * obj) list) =
        let command = connection.CreateCommand()
        command.CommandText <- sql
        values |> List.iter (fun (name, value) -> parameter command name value)
        command

    let private receiptScalar connection sql values =
        use command = receiptCommand connection sql values
        command.ExecuteScalar()

    let private receiptExecute connection sql values =
        use command = receiptCommand connection sql values
        command.ExecuteNonQuery() |> ignore

    let private receiptWorkspace connection =
        string (receiptScalar connection "SELECT value FROM store_metadata WHERE key='receiptWorkspace';" [])

    let private verifyScopedProvenance
        (connection: SqliteConnection)
        (transaction: SqliteTransaction)
        workspace
        receiptKeyComputed
        =
        use workspaceCommand = connection.CreateCommand()
        workspaceCommand.Transaction <- transaction
        workspaceCommand.CommandText <- "SELECT value FROM store_metadata WHERE key='receiptWorkspace';"
        let enrolled = workspaceCommand.ExecuteScalar()

        if isNull enrolled || enrolled = box DBNull.Value || string enrolled <> workspace then
            Error [ "projection-unavailable" ]
        else
            let count sql =
                use command = connection.CreateCommand()
                command.Transaction <- transaction
                command.CommandText <- sql
                Convert.ToInt64(command.ExecuteScalar())

            let expected = count "SELECT count(*) FROM ingest_batches;"
            let mutable covered = 0L
            let mutable cursor = 0L
            let mutable complete = false
            let mutable valid = true

            while valid && not complete do
                use pageCommand = connection.CreateCommand()
                pageCommand.Transaction <- transaction
                // Force the rowid range scan. SQLite otherwise prefers transport_pending(state,producer)
                // and builds a temporary ordering tree again for every page, making this proof quadratic.
                pageCommand.CommandText <-
                    "SELECT rowid,producer,batch,digest FROM transport_receipts NOT INDEXED WHERE rowid>$cursor AND state='applied' ORDER BY rowid LIMIT 256;"

                parameter pageCommand "$cursor" cursor
                use reader = pageCommand.ExecuteReader()
                let page = ResizeArray<int64 * string * string * string>()

                while reader.Read() do
                    page.Add(reader.GetInt64 0, reader.GetString 1, reader.GetString 2, reader.GetString 3)

                reader.Close()

                if page.Count = 0 then
                    complete <- true
                else
                    for rowId, producer, batch, digest in page do
                        cursor <- rowId
                        receiptKeyComputed ()
                        use lookup = connection.CreateCommand()
                        lookup.Transaction <- transaction

                        lookup.CommandText <-
                            "SELECT count(*) FROM ingest_batches WHERE ingest_id=$id AND content_digest=$digest;"

                        parameter lookup "$id" ("receipt-" + TelemetryReceipt.key producer batch)
                        parameter lookup "$digest" digest

                        if Convert.ToInt64(lookup.ExecuteScalar()) = 1L then
                            covered <- covered + 1L
                        else
                            valid <- false

            if valid && covered = expected then
                Ok()
            else
                Error [ "projection-unavailable" ]

    let private receiptAuthorized connection (scope: TelemetryReceipt.Scope) =
        receiptWorkspace connection = scope.Workspace
        && Convert.ToInt64(
            receiptScalar
                connection
                "SELECT count(*) FROM receipt_producers WHERE producer=$p AND stream=$s;"
                [ "$p", box scope.Producer; "$s", box scope.Stream ]
        )
            =
            1L

    let private principalAuthorized connection (principal: TelemetryReceipt.Principal) =
        receiptWorkspace connection = principal.Scope.Workspace
        && Convert.ToInt64(
            receiptScalar connection
                "SELECT count(*) FROM receipt_producers WHERE producer=$p AND stream=$s AND authority_role=$role AND grant_id IS $grant AND grant_generation IS $generation;"
                [ "$p", box principal.Scope.Producer; "$s", box principal.Scope.Stream; "$role", box (roleName principal.Role)
                  "$grant", principal.GrantId |> Option.map box |> Option.defaultValue DBNull.Value
                  "$generation", principal.GrantGeneration |> Option.map box |> Option.defaultValue DBNull.Value ]) = 1L

    let private receiptParameters (envelope: TelemetryReceipt.Envelope) =
        [ "$p", box envelope.Scope.Producer; "$b", box envelope.BatchId ]

    let private admissionParameters (admission: TelemetryReceipt.Admission) =
        let principal = admission.Principal
        receiptParameters admission.Envelope
        @ [ "$s", box principal.Scope.Stream; "$role", box (roleName principal.Role)
            "$grant", principal.GrantId |> Option.map box |> Option.defaultValue DBNull.Value
            "$generation", principal.GrantGeneration |> Option.map box |> Option.defaultValue DBNull.Value
            "$key", box admission.Envelope.Key; "$digest", box admission.Envelope.Digest ]

    let private admissionMatches connection (admission: TelemetryReceipt.Admission) =
        Convert.ToInt64(
            receiptScalar connection
                "SELECT count(*) FROM receipt_admissions WHERE producer=$p AND batch=$b AND stream=$s AND authority_role=$role AND grant_id IS $grant AND grant_generation IS $generation AND receipt_key=$key AND envelope_digest=$digest;"
                (admissionParameters admission)) = 1L

    let private insertAdmission connection (admission: TelemetryReceipt.Admission) =
        receiptExecute connection
            "INSERT INTO receipt_admissions(producer,batch,stream,authority_role,grant_id,grant_generation,receipt_key,envelope_digest) VALUES($p,$b,$s,$role,$grant,$generation,$key,$digest);"
            (admissionParameters admission)

    let private receiptPath root producer batch =
        Path.Combine(root, "receipt-inbox", TelemetryReceipt.key producer batch + ".ready")

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

                        if scalarText connection "PRAGMA user_version;" <> string currentSchemaVersion then
                            Error [ "unsupported-version" ]
                        elif
                            scalarText connection "PRAGMA journal_mode;" <> "wal"
                            || scalarText connection "SELECT digest FROM schema_migrations WHERE version=12;"
                               <> migration12Digest
                        then
                            Error [ "storage-unavailable" ]
                        else
                            action root connection
            with _ ->
                Error [ "storage-unavailable" ]


    let private efficiencyJson (node: JsonNode) =
        use document = JsonDocument.Parse(node.ToJsonString())
        EfficiencyEvidence.encode document.RootElement |> Result.defaultWith invalidOp

    let private efficiencyQueueRow (connection: SqliteConnection) (requestId: string) =
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT canonical,evidence_packet,revision,content_digest,owner_producer,owner_stream FROM efficiency_analysis_requests WHERE request_id=$id;"
        parameter command "$id" requestId
        use reader = command.ExecuteReader()
        if not (reader.Read()) then invalidOp "efficiency-request-unavailable"
        JsonNode.Parse(reader.GetString 0).AsObject(), reader.GetFieldValue<byte array>(1), reader.GetInt64 2,
        reader.GetString 3, reader.GetString 4, reader.GetString 5

    let private efficiencyQueueResult (record: JsonObject) =
        record.ToJsonString() + "\n"

    let private efficiencySaveQueue (connection: SqliteConnection) (record: JsonObject) (now: string) =
        let revision = record["revision"].GetValue<int64>()
        // Hash the receiver-owned lifecycle, excluding only its own digest field.
        let hashed = record.DeepClone().AsObject()
        hashed.Remove "contentDigest" |> ignore
        let digest = "sha256:" + CanonicalJson.sha256(efficiencyJson hashed)
        record["contentDigest"] <- JsonValue.Create digest
        let canonical = record.ToJsonString()
        let id = record["requestId"].GetValue<string>()
        receiptExecute connection
            "UPDATE efficiency_analysis_requests SET state=$state,revision=$revision,content_digest=$digest,canonical=$canonical,claim_id=$claim,claim_generation=$generation,invocation_ref=$invocation,claimed_at=$claimed,updated_at=$now WHERE request_id=$id;"
            [ "$id", box id; "$state", box (record["state"].GetValue<string>())
              "$revision", box revision; "$digest", box digest; "$canonical", box canonical
              "$claim", if isNull record["claimId"] then DBNull.Value else box (record["claimId"].GetValue<string>())
              "$generation", if isNull record["claimGeneration"] then DBNull.Value else box (record["claimGeneration"].GetValue<int64>())
              "$invocation", if isNull record["invocationRef"] then DBNull.Value else box (record["invocationRef"].GetValue<string>())
              "$claimed", if isNull record["claimedAt"] then DBNull.Value else box (record["claimedAt"].GetValue<string>())
              "$now", box now ]
        receiptExecute connection
            "INSERT INTO efficiency_analysis_history(request_id,revision,content_digest,canonical,recorded_at) VALUES($id,$revision,$digest,$canonical,$now);"
            [ "$id", box id; "$revision", box revision; "$digest", box digest; "$canonical", box canonical; "$now", box now ]

    let private efficiencyValidatePacket connection (request: JsonElement) (bytes: byte array) =
        let packet = EfficiencyEvidence.validate bytes |> Result.defaultWith invalidOp
        if (packet.GetProperty("subject").GetRawText() |> JsonNode.Parse |> efficiencyJson)
           <> (request.GetProperty("subject").GetRawText() |> JsonNode.Parse |> efficiencyJson) then
            invalidOp "efficiency-packet-subject-mismatch"
        if "sha256:" + CanonicalJson.sha256 bytes <> request.GetProperty("evidenceDigest").GetString() then
            invalidOp "efficiency-packet-digest-mismatch"
        let refs = request.GetProperty("evidenceRefs").EnumerateArray() |> Seq.toArray
        let records = packet.GetProperty("records").EnumerateArray() |> Seq.toArray
        let referenceKey (reference: JsonElement) =
            reference.GetRawText() |> JsonNode.Parse |> efficiencyJson |> Convert.ToBase64String
        let requestKeys = refs |> Array.map referenceKey |> Set.ofArray
        let packetKeys = records |> Array.map (fun record -> referenceKey (record.GetProperty "canonicalRef")) |> Set.ofArray
        if refs.Length <> records.Length || requestKeys.Count <> refs.Length
           || packetKeys.Count <> records.Length || requestKeys <> packetKeys then
            invalidOp "efficiency-packet-reference-population-mismatch"
        for record in records do
            let reference = record.GetProperty "canonicalRef"
            if not (refs |> Array.exists (fun value -> (value.GetRawText() |> JsonNode.Parse |> efficiencyJson) = (reference.GetRawText() |> JsonNode.Parse |> efficiencyJson))) then
                invalidOp "efficiency-packet-reference-mismatch"
            let actual, item = efficiencyReference connection reference
            if item <> Some(record.GetProperty("itemId").GetString()) then invalidOp "efficiency-packet-effective-subject-mismatch"
            let actualBytes = actual.GetRawText() |> JsonNode.Parse |> efficiencyJson
            let payloadBytes = record.GetProperty("payload").GetRawText() |> JsonNode.Parse |> efficiencyJson
            if actualBytes <> payloadBytes then invalidOp "efficiency-packet-source-content-mismatch"
        packet

    let private efficiencyEnqueue (connection: SqliteConnection) (principal: TelemetryReceipt.Principal) (request: JsonElement) (packet: byte array) (now: string) =
        let subject = request.GetProperty "subject"
        let item = subject.GetProperty("itemId").GetString()
        let outcome = subject.GetProperty("outcomeId").GetString()
        let authority = request.GetProperty "authority"
        if efficiencyAuthority connection principal (authority.GetProperty("sourceIdentity").GetString()) authority <> Some item then
            invalidOp "efficiency-request-authority-subject-mismatch"
        for reference in efficiencyReferences request do efficiencyReference connection reference |> ignore
        efficiencyValidatePacket connection request packet |> ignore
        if Convert.ToInt64(receiptScalar connection "SELECT count(*) FROM native_item_outcomes WHERE identity=$outcome AND item_id=$item;" [ "$outcome", box outcome; "$item", box item ]) <> 1L then
            invalidOp "efficiency-request-outcome-unavailable"
        // Only the prospectively authenticated receiver journal establishes an epoch.
        // Current budget epochs, historical timestamps and corrected aliases cannot do so.
        let epochNode = subject.GetProperty "outcomeEpoch"
        let epoch = if epochNode.ValueKind = JsonValueKind.Null then None else Some(epochNode.GetInt64())
        match epoch with
        | None -> ()
        | Some epoch ->
            use witness = connection.CreateCommand()
            witness.CommandText <- "SELECT begin_refs,close_refs FROM efficiency_outcome_epochs WHERE outcome_identity=$outcome AND outcome_revision=(SELECT fact_revision FROM native_item_outcomes WHERE identity=$outcome) AND epoch=$epoch AND state='closed';"
            parameter witness "$outcome" outcome
            parameter witness "$epoch" epoch
            use reader = witness.ExecuteReader()
            if not (reader.Read()) then invalidOp "efficiency-outcome-epoch-witness-unavailable"
            let required = ResizeArray<JsonElement>()
            for column in [ 0; 1 ] do
                use document = JsonDocument.Parse(reader.GetString column)
                for reference in document.RootElement.EnumerateArray() do
                    if EfficiencyInput.validateReference reference |> Result.isOk then required.Add(reference.Clone())
            let supplied = request.GetProperty("populationWitnessRefs").EnumerateArray() |> Seq.toArray
            let key (reference: JsonElement) = reference.GetRawText() |> JsonNode.Parse |> efficiencyJson |> Convert.ToBase64String
            if (required |> Seq.map key |> Set.ofSeq) <> (supplied |> Seq.map key |> Set.ofSeq) || supplied.Length <> (supplied |> Seq.map key |> Set.ofSeq |> Set.count) then
                invalidOp "efficiency-outcome-epoch-reference-mismatch"
            for reference in supplied do efficiencyReference connection reference |> ignore
        let keyParts = JsonArray()
        for name in [ "itemId"; "outcomeId"; "outcomeEpoch"; "scope" ] do
            keyParts.Add(JsonNode.Parse(subject.GetProperty(name).GetRawText()))
        keyParts.Add(JsonValue.Create(request.GetProperty("evidenceDigest").GetString()))
        keyParts.Add(JsonValue.Create(request.GetProperty("analysisPolicyVersion").GetString()))
        let id = CanonicalJson.sha256(efficiencyJson keyParts)
        if id <> request.GetProperty("requestId").GetString() then invalidOp "efficiency-request-key-mismatch"
        let existing = receiptScalar connection "SELECT count(*) FROM efficiency_analysis_requests WHERE request_id=$id;" [ "$id", box id ] |> Convert.ToInt64
        if existing = 1L then
            let record, retained, _, _, producer, stream = efficiencyQueueRow connection id
            if producer <> principal.Scope.Producer || stream <> principal.Scope.Stream || retained <> packet then
                invalidOp "efficiency-request-replay-conflict"
            record, true
        else
            let record = JsonObject()
            record["schema"] <- JsonValue.Create "fsgg.telemetry.efficiency-analysis-inspect/1"
            record["requestId"] <- JsonValue.Create id
            record["revision"] <- JsonValue.Create 0L
            record["state"] <- JsonValue.Create "pending"
            record["canonicalRequest"] <- JsonNode.Parse(request.GetRawText())
            record["claimId"] <- null
            record["claimGeneration"] <- null
            record["invocationRef"] <- null
            record["claimedAt"] <- null
            record["requestedAt"] <- JsonValue.Create now
            record["updatedAt"] <- JsonValue.Create now
            record["reason"] <- null
            receiptExecute connection
                "INSERT INTO efficiency_analysis_requests(request_id,stable_outcome_identity,outcome_epoch,effective_item_id,scope,evidence_digest,policy_version,state,revision,content_digest,canonical,evidence_packet,owner_producer,owner_stream,requested_at,updated_at) VALUES($id,$outcome,$epoch,$item,$scope,$evidence,$policy,'pending',0,'','',$packet,$producer,$stream,$now,$now);"
                [ "$id", box id; "$outcome", box outcome; "$epoch", epoch |> Option.map box |> Option.defaultValue DBNull.Value; "$item", box item
                  "$scope", box (subject.GetProperty("scope").GetString()); "$evidence", box (request.GetProperty("evidenceDigest").GetString())
                  "$policy", box (request.GetProperty("analysisPolicyVersion").GetString()); "$packet", box packet
                  "$producer", box principal.Scope.Producer; "$stream", box principal.Scope.Stream; "$now", box now ]
            efficiencySaveQueue connection record now
            record, false

    /// Named queue mutations use the enrolled producer, an immediate transaction and exact CAS.
    /// Generic ingestion never executes these transitions or launches a model.
    let efficiencyAnalysis path assessment (principal: TelemetryReceipt.Principal) action (bytes: byte array) (packet: byte array option) =
        if isNull bytes || bytes.Length = 0 || bytes.Length > 16384 then Error [ "efficiency-command-byte-bound" ]
        else
            try
                use document = JsonDocument.Parse bytes
                let input = document.RootElement
                match EfficiencyInput.validateCommand action input with
                | Error reason -> Error [ reason ]
                | Ok () ->
                    receiptLocked path assessment (fun _ connection ->
                        if not (principalAuthorized connection principal) then Error [ "efficiency-authenticated-producer-required" ]
                        else
                            beginImmediate connection
                            try
                                let now = DateTimeOffset.UtcNow.ToString("O")
                                let record =
                                    if action = "enqueue" then
                                        let packet = packet |> Option.defaultWith (fun () -> invalidOp "efficiency-packet-required")
                                        efficiencyEnqueue connection principal input packet now |> fst
                                    else
                                        let cas = input.GetProperty "cas"
                                        let id = cas.GetProperty("requestId").GetString()
                                        let record, _, revision, digest, producer, stream = efficiencyQueueRow connection id
                                        if producer <> principal.Scope.Producer || stream <> principal.Scope.Stream then invalidOp "efficiency-claim-owner-conflict"
                                        let inputDigest = CanonicalJson.sha256(Encoding.UTF8.GetBytes(CanonicalJson.canonicalize bytes |> Result.defaultWith (fun reason -> invalidOp reason)))
                                        use replayLookup = connection.CreateCommand()
                                        replayLookup.CommandText <- "SELECT canonical FROM efficiency_analysis_history WHERE request_id=$id AND json_extract(canonical,'$.lastAction')=$action AND json_extract(canonical,'$.lastInputDigest')=$input ORDER BY revision LIMIT 1;"
                                        parameter replayLookup "$id" id
                                        parameter replayLookup "$action" action
                                        parameter replayLookup "$input" inputDigest
                                        let replay = replayLookup.ExecuteScalar()
                                        if not (isNull replay) && replay <> box DBNull.Value then
                                            JsonNode.Parse(string replay).AsObject()
                                        else
                                            if revision <> cas.GetProperty("expectedRevision").GetInt64() || digest <> cas.GetProperty("expectedContentDigest").GetString() then invalidOp "efficiency-cas-conflict"
                                            let authority = input.GetProperty "authority"
                                            let effective = efficiencyAuthority connection principal (authority.GetProperty("sourceIdentity").GetString()) authority
                                            let subject = record.["canonicalRequest"].["subject"]
                                            let outcome = subject["outcomeId"].GetValue<string>()
                                            let item = receiptScalar connection "SELECT item_id FROM native_item_outcomes WHERE identity=$id;" [ "$id", box outcome ] |> string
                                            if effective <> Some item then invalidOp "efficiency-effective-authority-conflict"
                                            for reference in efficiencyReferences input do efficiencyReference connection reference |> ignore
                                            let state = record["state"].GetValue<string>()
                                            match action with
                                            | "claim" ->
                                                if state <> "pending" then invalidOp "efficiency-request-not-pending"
                                                if input.GetProperty("invocationRef").ValueKind <> JsonValueKind.Null then invalidOp "efficiency-claim-actual-invocation-before-start"
                                                let dispatch = input.GetProperty "dispatchRef"
                                                if dispatch.GetProperty("kind").GetString() <> "expected-dispatch" then invalidOp "efficiency-claim-dispatch-kind"
                                                let _, dispatchItem = efficiencyReference connection dispatch
                                                let dispatchSubject = dispatchItem |> Option.defaultWith (fun () -> invalidOp "efficiency-claim-dispatch-subject")
                                                let subjectJoined = receiptScalar connection "WITH RECURSIVE aliases(item) AS (SELECT $item UNION SELECT c.prior_item FROM ci_attribution_corrections c JOIN aliases a ON c.effective_item=a.item UNION SELECT c.effective_item FROM ci_attribution_corrections c JOIN aliases a ON c.prior_item=a.item) SELECT count(*) FROM aliases WHERE item=$dispatchItem;" [ "$item",box item; "$dispatchItem",box dispatchSubject ] |> Convert.ToInt64
                                                if subjectJoined<>1L then invalidOp "efficiency-claim-dispatch-subject"
                                                let dispatchOwned = receiptScalar connection "SELECT count(*) FROM fact_admissions a JOIN receipt_admissions ra ON ra.receipt_key=a.receipt_key AND ra.envelope_digest=a.envelope_digest AND ra.producer=a.producer AND ra.stream=a.stream JOIN transport_receipts t ON t.producer=ra.producer AND t.batch=ra.batch AND t.state='applied' WHERE a.identity=$dispatch AND a.producer=$producer AND a.stream=$stream;" [ "$dispatch",box (dispatch.GetProperty("id").GetString()); "$producer",box producer; "$stream",box stream ] |> Convert.ToInt64
                                                if dispatchOwned<>1L then invalidOp "efficiency-prospective-dispatch-owner"
                                                let policy = (record.["canonicalRequest"].["analysisPolicyVersion"]).GetValue<string>()
                                                // Count stable outcome history across both scopes and every corrected alias.
                                                // Unknown-epoch reservations remain chargeable when an epoch is later witnessed.
                                                let epochNode = subject["outcomeEpoch"]
                                                let epochKey = if isNull epochNode then "unknown" else epochNode.GetValue<int64>().ToString(Globalization.CultureInfo.InvariantCulture)
                                                // A raw outcome revision is not a witnessed epoch boundary.
                                                // Unknown/initial requests retain all stable-outcome history.
                                                // Only a receiver-closed reopen can delimit prior reservations.
                                                let beginSequence =
                                                    if not (isNull epochNode) && epochNode.GetValue<int64>()>1L then
                                                        receiptScalar connection "SELECT begin_sequence FROM efficiency_outcome_epochs e JOIN native_item_outcomes o ON o.identity=e.outcome_identity AND o.fact_revision=e.outcome_revision WHERE e.outcome_identity=$outcome AND e.epoch=$epoch AND e.state='closed';" [ "$outcome",box outcome; "$epoch",box (epochNode.GetValue<int64>()) ] |> Convert.ToInt64
                                                    else 0L
                                                let budgetValues = [ "$outcome",box outcome; "$policy",box policy; "$epoch",box epochKey; "$begin",box beginSequence ]
                                                let active = receiptScalar connection "SELECT count(*) FROM efficiency_analysis_reservations WHERE stable_outcome_identity=$outcome AND policy_version=$policy AND state IN ('reserved','started');" budgetValues |> Convert.ToInt64
                                                if active<>0L then invalidOp "efficiency-analysis-concurrent-claim"
                                                let used = receiptScalar connection "SELECT count(*) FROM efficiency_analysis_reservations r JOIN efficiency_analysis_requests q ON q.request_id=r.request_id WHERE r.stable_outcome_identity=$outcome AND r.policy_version=$policy AND ($begin=0 OR r.epoch_key=$epoch OR json_type(q.canonical,'$.claimedReceiverOrder') IS NULL OR json_extract(q.canonical,'$.claimedReceiverOrder')>=$begin);" budgetValues |> Convert.ToInt64
                                                if used >= 3L then invalidOp "analysis-budget-exhausted"
                                                let claim = input.GetProperty("claimId").GetString()
                                                let generation = used + 1L
                                                receiptExecute connection
                                                    "INSERT INTO efficiency_analysis_reservations VALUES($outcome,$epoch,$policy,$reservation,$request,$claim,$producer,$stream,$dispatch,$generation,'reserved');"
                                                    [ "$outcome", box outcome; "$epoch",box epochKey; "$policy", box policy; "$reservation", box generation
                                                      "$request", box id; "$claim", box claim; "$producer", box producer; "$stream", box stream
                                                      "$dispatch", box (dispatch.GetProperty("id").GetString()); "$generation", box generation ]
                                                record["claimId"] <- JsonValue.Create claim
                                                record["claimGeneration"] <- JsonValue.Create generation
                                                record["claimedAt"] <- JsonValue.Create now
                                                record["claimedReceiverOrder"] <- JsonValue.Create(Convert.ToInt64(receiptScalar connection "SELECT coalesce(max(sequence),0) FROM efficiency_receiver_order;" []))
                                                record["dispatchRef"] <- JsonNode.Parse(dispatch.GetRawText())
                                                record["modelAlias"] <- JsonValue.Create(input.GetProperty("modelAlias").GetString())
                                                record["limitSupport"] <- JsonNode.Parse(input.GetProperty("limitSupport").GetRawText())
                                                record["state"] <- JsonValue.Create "claimed"
                                            | "attach-invocation" ->
                                                if state <> "claimed" || record["claimId"].GetValue<string>() <> input.GetProperty("claimId").GetString() then invalidOp "efficiency-claim-mismatch"
                                                if not (isNull record["invocationRef"]) then invalidOp "efficiency-invocation-already-attached"
                                                if efficiencyJson record["dispatchRef"] <> (input.GetProperty("dispatchRef").GetRawText() |> JsonNode.Parse |> efficiencyJson) then invalidOp "efficiency-dispatch-mismatch"
                                                let invocation = input.GetProperty("invocationRef").GetString()
                                                let dispatch = (record.["dispatchRef"].["id"]).GetValue<string>()
                                                if Convert.ToInt64(receiptScalar connection
                                                    "SELECT count(*) FROM expected_dispatches d JOIN invocation_lineage l ON l.dispatch_id=d.dispatch_id AND l.item_id=d.item_id JOIN runtime_admissions a ON a.invocation_id=l.invocation_id AND a.item_id=l.item_id WHERE d.identity=$dispatch AND d.item_id=$item AND a.invocation_id=$invocation;"
                                                    [ "$dispatch", box dispatch; "$item", box item; "$invocation", box invocation ]) <> 1L then invalidOp "efficiency-runtime-lineage-unavailable"
                                                use started = connection.CreateCommand()
                                                started.CommandText <- "SELECT f.identity,f.kind,f.revision,f.content_digest,t.accepted_at FROM runtime_starts s JOIN current_ingest_facts f ON f.identity=s.identity JOIN fact_admissions a ON a.identity=s.identity JOIN fact_acceptance_times t ON t.identity=f.identity AND t.fact_revision=f.revision AND t.content_digest=f.content_digest WHERE s.item_id=$item AND s.invocation_id=$invocation AND ((s.phase='process' AND s.process_id>0) OR (s.phase='thread' AND s.thread_id IS NOT NULL)) AND a.producer=$producer AND a.stream=$stream ORDER BY f.identity;"
                                                [ "$item",box item; "$invocation",box invocation; "$producer",box producer; "$stream",box stream ]
                                                |> List.iter (fun (name,value) -> parameter started name value)
                                                let startRows =
                                                    use reader = started.ExecuteReader()
                                                    [ while reader.Read() do yield reader.GetString 0,reader.GetString 1,reader.GetInt64 2,reader.GetString 3,reader.GetString 4 ]
                                                let suppliedRefs = input.GetProperty("lineageRefs").EnumerateArray() |> Seq.toArray
                                                let claimedAt = DateTimeOffset.Parse(record["claimedAt"].GetValue<string>(),Globalization.CultureInfo.InvariantCulture)
                                                let acceptedStart = startRows |> List.exists (fun (id,kind,revision,digest,acceptedAt) ->
                                                    DateTimeOffset.Parse(acceptedAt,Globalization.CultureInfo.InvariantCulture)>=claimedAt
                                                    && (suppliedRefs |> Array.exists (fun reference ->
                                                        reference.GetProperty("id").GetString()=id && reference.GetProperty("kind").GetString()=kind
                                                        && reference.GetProperty("revision").GetInt64()=revision
                                                        && reference.GetProperty("contentDigest").GetString()="sha256:"+digest)))
                                                if not acceptedStart then invalidOp "efficiency-actual-start-witness-unavailable"
                                                record["invocationRef"] <- JsonValue.Create invocation
                                                record["lineageRefs"] <- JsonNode.Parse(input.GetProperty("lineageRefs").GetRawText())
                                                receiptExecute connection "UPDATE efficiency_analysis_reservations SET state='started' WHERE claim_id=$claim;" [ "$claim", box (input.GetProperty("claimId").GetString()) ]
                                            | "settle" ->
                                                if state <> "claimed" || record["claimId"].GetValue<string>() <> input.GetProperty("claimId").GetString() then invalidOp "efficiency-claim-mismatch"
                                                let invocation = input.GetProperty "invocationRef"
                                                if isNull record["invocationRef"] then
                                                    if invocation.ValueKind <> JsonValueKind.Null then invalidOp "efficiency-unattached-invocation"
                                                elif invocation.ValueKind <> JsonValueKind.String || invocation.GetString() <> record["invocationRef"].GetValue<string>() then invalidOp "efficiency-invocation-mismatch"
                                                let nextState = input.GetProperty("state").GetString()
                                                let outcomeState = input.GetProperty("invocationOutcome").GetString()
                                                if nextState = "settled" then
                                                    // An accepted result must join a real attached and settled runtime.
                                                    if isNull record["invocationRef"] || outcomeState <> "completed" then invalidOp "efficiency-result-runtime-unavailable"
                                                    let result = input.GetProperty "resultAssessmentRef"
                                                    if result.ValueKind <> JsonValueKind.String then invalidOp "efficiency-result-assessment-required"
                                                    if Convert.ToInt64(receiptScalar connection "SELECT count(*) FROM efficiency_records WHERE identity=$id AND kind='efficiency-assessment/1' AND item_id=$item;" [ "$id", box (result.GetString()); "$item", box item ]) <> 1L then invalidOp "efficiency-result-assessment-unavailable"
                                                    if Convert.ToInt64(receiptScalar connection "SELECT count(*) FROM runtime_terminals WHERE invocation_id=$invocation AND item_id=$item;" [ "$invocation", box (invocation.GetString()); "$item", box item ]) <> 1L then invalidOp "efficiency-result-terminal-unavailable"
                                                    if input.GetProperty("usageRefs").GetArrayLength() = 0 then invalidOp "efficiency-result-usage-unavailable"
                                                    for usage in input.GetProperty("usageRefs").EnumerateArray() do
                                                        let body, usageItem = efficiencyReference connection usage
                                                        if usage.GetProperty("kind").GetString() <> "runtime-turn-usage" || usageItem <> Some item
                                                           || body.GetProperty("invocationId").GetString() <> invocation.GetString() then
                                                            invalidOp "efficiency-result-usage-invocation-mismatch"
                                                    let resultBody = receiptScalar connection "SELECT canonical FROM efficiency_records WHERE identity=$id;" [ "$id", box (result.GetString()) ] |> string
                                                    use resultDocument = JsonDocument.Parse resultBody
                                                    let accepted = resultDocument.RootElement.GetProperty "assessment"
                                                    let request = record["canonicalRequest"]
                                                    if accepted.GetProperty("lifecycle").GetProperty("idempotencyKey").GetString() <> id
                                                       || accepted.GetProperty("evidenceDigest").GetString() <> request["evidenceDigest"].GetValue<string>()
                                                       || (accepted.GetProperty("subject").GetRawText() |> JsonNode.Parse |> efficiencyJson) <> efficiencyJson request["subject"]
                                                       || accepted.GetProperty("provenance").GetProperty("validationResult").GetString() <> "accepted" then
                                                        invalidOp "efficiency-result-request-association-mismatch"
                                                    let lifecycle = accepted.GetProperty "lifecycle"
                                                    let assessmentState = lifecycle.GetProperty("state").GetString()
                                                    if assessmentState <> "partial" && assessmentState <> "ready" then invalidOp "efficiency-result-assessment-state"
                                                    if assessmentState = "ready" then
                                                        if accepted.GetProperty("subject").GetProperty("scope").GetString() <> "native-item"
                                                           || accepted.GetProperty("subject").GetProperty("outcomeEpoch").ValueKind = JsonValueKind.Null then
                                                            invalidOp "efficiency-ready-native-epoch-required"
                                                        for name in [ "population"; "usage"; "lineage" ] do
                                                            if accepted.GetProperty("coverage").GetProperty(name).GetString() <> "complete" then invalidOp "efficiency-ready-population-incomplete"
                                                    record["generatedReviewRef"] <- null
                                                    let review = lifecycle.GetProperty "itemReviewRef"
                                                    if review.ValueKind = JsonValueKind.String then
                                                        let matches = accepted.GetProperty("evidenceRefs").EnumerateArray()
                                                                      |> Seq.filter (fun reference -> reference.GetProperty("kind").GetString() = "process-review" && reference.GetProperty("id").GetString() = review.GetString())
                                                                      |> Seq.toArray
                                                        if matches.Length <> 1 then invalidOp "efficiency-result-review-revision-ambiguous"
                                                        let reviewRef = matches[0]
                                                        let reviewId = reviewRef.GetProperty("id").GetString()
                                                        let reviewRevision = reviewRef.GetProperty("revision").GetInt64()
                                                        // Receiver-owned receipt order, not caller generatedAt/claimedAt,
                                                        // decides whether this exact review version is new analyst output.
                                                        use lookup = connection.CreateCommand()
                                                        lookup.CommandText <- "SELECT f.kind,f.canonical,t.accepted_at FROM current_ingest_facts f JOIN fact_admissions a ON a.identity=f.identity LEFT JOIN fact_acceptance_times t ON t.identity=f.identity AND t.fact_revision=f.revision AND t.content_digest=f.content_digest WHERE f.identity=$id AND f.revision=$revision AND f.item_id=$item AND a.producer=$producer AND a.stream=$stream;"
                                                        [ "$id", box reviewId; "$revision", box reviewRevision; "$item", box item; "$producer", box producer; "$stream", box stream ] |> List.iter (fun (name,value) -> parameter lookup name value)
                                                        use reviewReader = lookup.ExecuteReader()
                                                        if not (reviewReader.Read()) || reviewReader.GetString 0 <> "process-review" then invalidOp "efficiency-result-review-unavailable"
                                                        use reviewDocument = JsonDocument.Parse(reviewReader.GetString 1)
                                                        if reviewDocument.RootElement.GetProperty("scope").GetString() <> "item" then invalidOp "efficiency-result-review-scope"
                                                        if not (reviewReader.IsDBNull 2) && DateTimeOffset.Parse(reviewReader.GetString 2, Globalization.CultureInfo.InvariantCulture) >= DateTimeOffset.Parse(record["claimedAt"].GetValue<string>(), Globalization.CultureInfo.InvariantCulture) then
                                                            record["generatedReviewRef"] <- JsonNode.Parse(reviewRef.GetRawText())
                                                        // Preclaim or historically unclocked admitted reviews remain substantive.

                                                elif outcomeState = "proven-no-effect" && input.GetProperty("reconciliationRefs").GetArrayLength() = 0 then invalidOp "efficiency-no-effect-witness-required"
                                                record["state"] <- JsonValue.Create nextState
                                                record["resultAssessmentRef"] <- JsonNode.Parse(input.GetProperty("resultAssessmentRef").GetRawText())
                                                record["reason"] <- JsonNode.Parse(input.GetProperty("reason").GetRawText())
                                                record["usageRefs"] <- JsonNode.Parse(input.GetProperty("usageRefs").GetRawText())
                                                let reservationState = if outcomeState = "unknown" then "unknown" elif outcomeState = "proven-no-effect" then "proven-no-effect" elif nextState = "settled" then "settled" else "failed"
                                                receiptExecute connection "UPDATE efficiency_analysis_reservations SET state=$state WHERE claim_id=$claim;" [ "$state", box reservationState; "$claim", box (input.GetProperty("claimId").GetString()) ]
                                            | _ -> invalidOp "efficiency-action-unsupported"
                                            record["lastAction"] <- JsonValue.Create action
                                            record["lastInputDigest"] <- JsonValue.Create inputDigest
                                            record["revision"] <- JsonValue.Create(revision + 1L)
                                            record["updatedAt"] <- JsonValue.Create now
                                            efficiencySaveQueue connection record now
                                            record
                                execute connection "COMMIT;"
                                Ok(efficiencyQueueResult record)
                            with error ->
                                rollback connection
                                Error [ error.Message ])
            with
            | :? JsonException as error -> Error [ error.Message ]
            | :? InvalidOperationException as error -> Error [ error.Message ]

    /// Resolve only an already applied dispatch owned by this association; the claim
    /// transaction rechecks its exact revision/digest and existing CAS/budget guards.
    let efficiencyAnalysisClaimProspective path assessment (principal: TelemetryReceipt.Principal) (dispatchIdentity: string) (itemId: string) (templateBytes: byte array) =
        if isNull templateBytes || templateBytes.Length=0 || templateBytes.Length>16384 then Error [ "efficiency-command-byte-bound" ]
        else
            try
                use template = JsonDocument.Parse templateBytes
                let value = template.RootElement
                if value.ValueKind<>JsonValueKind.Object || (value.EnumerateObject() |> Seq.exists (fun property -> property.Name="dispatchRef")) then
                    Error [ "efficiency-prospective-template-shape" ]
                else
                    let resolved = receiptLocked path assessment (fun _ connection ->
                        if not (principalAuthorized connection principal) then Error [ "efficiency-authenticated-producer-required" ]
                        else
                            use command = connection.CreateCommand()
                            command.CommandText <- "SELECT f.identity,f.kind,f.revision,f.content_digest FROM expected_dispatches d JOIN current_ingest_facts f ON f.identity=d.identity JOIN fact_admissions a ON a.identity=f.identity JOIN receipt_admissions ra ON ra.receipt_key=a.receipt_key AND ra.envelope_digest=a.envelope_digest AND ra.producer=a.producer AND ra.stream=a.stream JOIN transport_receipts t ON t.producer=ra.producer AND t.batch=ra.batch AND t.state='applied' WHERE d.identity=$dispatch AND d.item_id=$item AND a.producer=$producer AND a.stream=$stream;"
                            [ "$dispatch",box dispatchIdentity; "$item",box itemId; "$producer",box principal.Scope.Producer; "$stream",box principal.Scope.Stream ]
                            |> List.iter (fun (name,parameterValue) -> parameter command name parameterValue)
                            use reader = command.ExecuteReader()
                            if not (reader.Read()) then Error [ "efficiency-prospective-dispatch-unavailable" ]
                            else
                                let reference = JsonObject()
                                reference["id"] <- JsonValue.Create(reader.GetString 0)
                                reference["kind"] <- JsonValue.Create(reader.GetString 1)
                                reference["revision"] <- JsonValue.Create(reader.GetInt64 2)
                                reference["contentDigest"] <- JsonValue.Create("sha256:"+reader.GetString 3)
                                if reader.Read() then Error [ "efficiency-prospective-dispatch-ambiguous" ] else Ok reference)
                    match resolved with
                    | Error reasons -> Error reasons
                    | Ok reference ->
                        let input = JsonNode.Parse(templateBytes).AsObject()
                        input["dispatchRef"] <- reference
                        let bytes = Encoding.UTF8.GetBytes(input.ToJsonString())
                        efficiencyAnalysis path assessment principal "claim" bytes None
            with
            | :? JsonException as error -> Error [ error.Message ]
            | :? InvalidOperationException as error -> Error [ error.Message ]

    let efficiencyAnalysisReconcile path assessment (principal: TelemetryReceipt.Principal) (selectedItem: string option) =
        receiptLocked path assessment (fun _ connection ->
            if not (principalAuthorized connection principal) then Error [ "efficiency-authenticated-producer-required" ]
            else
                beginImmediate connection
                try
                    let now = DateTimeOffset.UtcNow.ToString("O")
                    let parameterized sql values =
                        let command = connection.CreateCommand()
                        command.CommandText <- sql
                        values |> List.iter (fun (name, value) -> parameter command name value)
                        command
                    let outcomes =
                        use command =
                            parameterized
                                "SELECT o.identity,o.item_id,f.revision,f.content_digest,f.canonical FROM native_item_outcomes o JOIN current_ingest_facts f ON f.identity=o.identity JOIN fact_admissions a ON a.identity=o.identity WHERE a.producer=$producer AND a.stream=$stream AND ($item IS NULL OR o.item_id=$item) ORDER BY o.item_id,o.identity LIMIT 201;"
                                [ "$producer", box principal.Scope.Producer; "$stream", box principal.Scope.Stream
                                  "$item", selectedItem |> Option.map box |> Option.defaultValue DBNull.Value ]
                        use reader = command.ExecuteReader()
                        [ while reader.Read() do yield reader.GetString 0, reader.GetString 1 ]
                    let ids = ResizeArray<string>()
                    let mutable requested = 0
                    let mutable replayed = 0
                    let mutable unavailable = 0
                    let mutable reason: string = null
                    for outcome, item in outcomes |> List.truncate 200 do
                        try
                            let unresolved =
                                receiptScalar connection
                                    "SELECT count(*) FROM efficiency_analysis_requests WHERE stable_outcome_identity=$outcome AND state IN ('claimed','failed','unavailable');"
                                    [ "$outcome", box outcome ] |> Convert.ToInt64
                            if unresolved > 0L then invalidOp "analysis-pending-unresolved-claim"
                            let authority =
                                use command =
                                    parameterized
                                        "SELECT f.identity,f.kind,f.revision,f.content_digest FROM current_ingest_facts f JOIN fact_admissions a ON a.identity=f.identity JOIN receipt_admissions ra ON ra.receipt_key=a.receipt_key AND ra.envelope_digest=a.envelope_digest AND ra.producer=a.producer AND ra.stream=a.stream JOIN transport_receipts r ON r.producer=ra.producer AND r.batch=ra.batch AND r.state='applied' WHERE f.item_id=$item AND f.kind IN ('process-review','runtime-admission','native-item-outcome') AND a.producer=$producer AND a.stream=$stream ORDER BY CASE f.kind WHEN 'process-review' THEN 0 WHEN 'runtime-admission' THEN 1 ELSE 2 END,f.identity LIMIT 1;"
                                        [ "$item", box item; "$producer", box principal.Scope.Producer; "$stream", box principal.Scope.Stream ]
                                use reader = command.ExecuteReader()
                                if not (reader.Read()) then invalidOp "missing-authority"
                                reader.GetString 0, reader.GetString 1, reader.GetInt64 2, reader.GetString 3
                            let authorityId, authorityKind, authorityRevision, authorityDigest = authority
                            let records = JsonArray()
                            let references = JsonArray()
                            let omissions = ResizeArray<string>()
                            let mutable rawBytes = 0
                            use command =
                                parameterized
                                    "SELECT identity,kind,revision,content_digest,canonical FROM current_ingest_facts WHERE item_id=$item ORDER BY CASE kind WHEN 'complication' THEN 0 WHEN 'process-review' THEN 1 WHEN 'native-item-outcome' THEN 2 ELSE 3 END,identity LIMIT 4097;"
                                    [ "$item", box item ]
                            let sourceRows =
                                use reader = command.ExecuteReader()
                                [ while reader.Read() do yield reader.GetString 0, reader.GetString 1, reader.GetInt64 2, reader.GetString 3, reader.GetString 4 ]
                            for identity, kind, revision, digest, canonical in sourceRows do
                                rawBytes <- rawBytes + Encoding.UTF8.GetByteCount canonical
                                if rawBytes > 4194304 || Encoding.UTF8.GetByteCount canonical > 16384 then invalidOp "efficiency-source-byte-bound"
                                let reference = JsonSerializer.SerializeToNode({| id = identity; kind = kind; revision = revision; contentDigest = "sha256:" + digest |})
                                use referenceDocument = JsonDocument.Parse(reference.ToJsonString())
                                let analysisUsage =
                                    receiptScalar connection
                                        "SELECT count(*) FROM current_ingest_facts f JOIN efficiency_analysis_requests q ON (q.invocation_ref IS NOT NULL AND json_extract(f.canonical,'$.invocationId')=q.invocation_ref) OR json_extract(q.canonical,'$.dispatchRef.id')=f.identity WHERE f.identity=$id;"
                                        [ "$id", box identity ] |> Convert.ToInt64
                                let generatedReview =
                                    receiptScalar connection
                                        "SELECT count(*) FROM efficiency_analysis_requests WHERE state='settled' AND json_extract(canonical,'$.generatedReviewRef.id')=$id AND json_extract(canonical,'$.generatedReviewRef.revision')=$revision;"
                                        [ "$id", box identity; "$revision", box revision ] |> Convert.ToInt64
                                match EfficiencyEvidence.semanticKind kind, EfficiencyInput.validateReference referenceDocument.RootElement with
                                | Some semantic, Ok () when analysisUsage = 0L && generatedReview = 0L ->
                                    if records.Count < 128 then
                                        let row = JsonObject()
                                        row["ref"] <- JsonSerializer.SerializeToNode({| id = identity; kind = semantic; revision = revision |})
                                        row["canonicalRef"] <- reference
                                        row["itemId"] <- JsonValue.Create item
                                        row["payload"] <- JsonNode.Parse canonical
                                        row["priority"] <- JsonValue.Create(if kind = "complication" then "failure" elif kind = "native-item-outcome" then "success" else "other")
                                        row["analysisGenerated"] <- JsonValue.Create false
                                        records.Add row
                                        references.Add(reference.DeepClone())
                                    else omissions.Add "canonical-record-selection-bound"
                                | _ when analysisUsage > 0L || generatedReview > 0L || kind = "efficiency-assessment/1" -> () // Analyst cost remains in canonical metrics.
                                | _ -> omissions.Add("unsupported-substantive-kind:" + kind)
                            if sourceRows.Length > 4096 then omissions.Add "canonical-source-selection-bound"
                            let epoch, epochRefs =
                                use command = parameterized "SELECT e.epoch,e.begin_refs,e.close_refs FROM efficiency_outcome_epochs e JOIN native_item_outcomes o ON o.identity=e.outcome_identity AND o.fact_revision=e.outcome_revision WHERE e.outcome_identity=$outcome AND e.state='closed' ORDER BY e.epoch DESC LIMIT 1;" [ "$outcome", box outcome ]
                                use reader = command.ExecuteReader()
                                if not (reader.Read()) then None, JsonArray()
                                else
                                    let refs = JsonArray()
                                    let seen = System.Collections.Generic.HashSet<string>()
                                    for column in [ 1; 2 ] do
                                        use document = JsonDocument.Parse(reader.GetString column)
                                        for reference in document.RootElement.EnumerateArray() do
                                            if EfficiencyInput.validateReference reference |> Result.isOk then
                                                let canonical = reference.GetRawText() |> JsonNode.Parse |> efficiencyJson
                                                if seen.Add(Convert.ToBase64String canonical) then refs.Add(JsonNode.Parse canonical)
                                    if refs.Count > 32 then invalidOp "efficiency-epoch-population-reference-bound"
                                    Some(reader.GetInt64 0), refs
                            let complete = epoch.IsSome
                            let subject = JsonSerializer.SerializeToNode({| itemId = item; outcomeId = outcome; outcomeEpoch = epoch |> Option.map Nullable |> Option.defaultValue (Nullable<int64>()); scope = (if complete then "native-item" else "provisional-delivery") |})
                            let packetNode = JsonObject()
                            packetNode["schema"] <- JsonValue.Create "fsgg.telemetry.efficiency-evidence-packet/1"
                            packetNode["subject"] <- subject.DeepClone()
                            packetNode["coverage"] <- JsonSerializer.SerializeToNode({| population = (if complete && omissions.Count = 0 then "complete" else "partial"); usage = "unknown"; classification = "unknown"; lineage = (if complete then "complete" else "unknown"); dependency = "unknown" |})
                            packetNode["omissions"] <- JsonSerializer.SerializeToNode(omissions |> Seq.distinct |> Seq.truncate 31 |> Seq.toArray)
                            packetNode["records"] <- records
                            let mutable packet = efficiencyJson packetNode
                            while packet.Length > 24576 && records.Count > 0 do
                                records.RemoveAt(records.Count - 1)
                                references.RemoveAt(references.Count - 1)
                                packetNode.["coverage"].["population"] <- JsonValue.Create "partial"
                                packetNode["omissions"] <- JsonSerializer.SerializeToNode([| "evidence-packet-byte-bound; canonical records omitted" |])
                                packet <- efficiencyJson packetNode
                            if packet.Length > 24576 then invalidOp "efficiency-evidence-packet-byte-bound"
                            let evidenceDigest = "sha256:" + CanonicalJson.sha256 packet
                            let policy = "efficiency-analysis-policy/1"
                            let keyParts = JsonArray()
                            for name in [ "itemId"; "outcomeId"; "outcomeEpoch"; "scope" ] do
                                keyParts.Add(if isNull subject[name] then null else subject[name].DeepClone())
                            keyParts.Add(JsonValue.Create evidenceDigest)
                            keyParts.Add(JsonValue.Create policy)
                            let requestId = CanonicalJson.sha256(efficiencyJson keyParts)
                            let request = JsonObject()
                            request["schema"] <- JsonValue.Create "fsgg.telemetry.efficiency-analysis-request-input/1"
                            request["kind"] <- JsonValue.Create "efficiency-analysis-request/1"
                            request["requestId"] <- JsonValue.Create requestId
                            request["subject"] <- subject
                            request["evidenceDigest"] <- JsonValue.Create evidenceDigest
                            request["analysisPolicyVersion"] <- JsonValue.Create policy
                            request["evidenceRefs"] <- references
                            request["authority"] <-
                                JsonSerializer.SerializeToNode
                                    {| sourceIdentity = principal.Scope.Producer
                                       authorityRole = (if authorityKind = "process-review" then "root-reviewer" elif authorityKind = "runtime-admission" then "runtime-observer" else "ci-observer")
                                       authorityRef = {| id = authorityId; kind = authorityKind; revision = authorityRevision; contentDigest = "sha256:" + authorityDigest |}
                                       rootDispatchRef = (null: string); invocationRef = (null: string) |}
                            request["nativeReviewRef"] <- JsonValue.Create(if authorityKind = "process-review" then authorityId else null)
                            request["populationWitnessRefs"] <- epochRefs
                            request["requestedAt"] <- JsonValue.Create now
                            request["modelAlias"] <- null
                            request["claimant"] <- null
                            request["invocationRef"] <- null
                            if Encoding.UTF8.GetByteCount(request.ToJsonString())>16384 then invalidOp "efficiency-request-byte-bound"
                            use document = JsonDocument.Parse(request.ToJsonString())
                            EfficiencyInput.validateCommand "enqueue" document.RootElement |> Result.defaultWith invalidOp
                            let _, already = efficiencyEnqueue connection principal document.RootElement packet now
                            ids.Add requestId
                            if already then replayed <- replayed + 1 else requested <- requested + 1
                        with :? InvalidOperationException as error ->
                            unavailable <- unavailable + 1
                            reason <- error.Message
                    if outcomes.Length > 200 then unavailable <- unavailable + outcomes.Length - 200
                    execute connection "COMMIT;"
                    Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.efficiency-analysis-reconcile/1"; requested = requested; replayed = replayed; unavailable = unavailable; requestIds = ids.ToArray(); reason = reason |} + "\n")
                with error ->
                    rollback connection
                    Error [ error.Message ])

    let provisionReceiptWorkspace path assessment workspaceId =
        if not (TelemetryReceipt.validId workspaceId) then
            Error [ "invalid-request" ]
        else
            receiptLocked path assessment (fun root connection ->
                let workspace = receiptWorkspace connection

                if workspace = workspaceId then
                    Ok "{\"schema\":\"fsgg.telemetry.workspace-provision/1\",\"status\":\"already-provisioned\"}\n"
                elif workspace <> "" then
                    Error [ "unauthorized-scope" ]
                elif
                    Convert.ToInt64(receiptScalar connection "SELECT count(*) FROM ingest_batches;" []) > 0L
                    || pendingCount root > 0
                then
                    Error [ "legacy-unassigned; select a new prospective store" ]
                else
                    beginImmediate connection

                    try
                        receiptExecute
                            connection
                            "INSERT INTO store_metadata(key,value) VALUES('receiptWorkspace',$w);"
                            [ "$w", box workspaceId ]

                        execute connection "COMMIT;"
                        Ok "{\"schema\":\"fsgg.telemetry.workspace-provision/1\",\"status\":\"provisioned\"}\n"
                    with error ->
                        rollback connection
                        raise error)

    let enrollReceiptPrincipal path assessment (principal: TelemetryReceipt.Principal) =
        let scope = principal.Scope
        if
            [ scope.Workspace; scope.Producer; scope.Stream ]
            |> List.exists (TelemetryReceipt.validId >> not)
            || not (TelemetryReceipt.validPrincipal principal)
        then
            Error [ "invalid-request" ]
        else
            receiptLocked path assessment (fun root connection ->
                let workspace = receiptWorkspace connection

                if workspace <> "" && workspace <> scope.Workspace then
                    Error [ "unauthorized-scope" ]
                elif
                    workspace = ""
                    && (Convert.ToInt64(receiptScalar connection "SELECT count(*) FROM ingest_batches;" []) > 0L
                        || pendingCount root > 0)
                then
                    Error [ "legacy-unassigned; select a new prospective store" ]
                else
                    let existing =
                        Convert.ToInt64(
                            receiptScalar
                                connection
                                "SELECT count(*) FROM receipt_producers WHERE producer=$p;"
                                [ "$p", box scope.Producer ]
                        )

                    let producers =
                        Convert.ToInt64(
                            receiptScalar connection "SELECT count(DISTINCT producer) FROM receipt_producers;" []
                        )

                    let streams =
                        Convert.ToInt64(receiptScalar connection "SELECT count(*) FROM receipt_producers;" [])

                    if
                        (existing = 0L && producers >= 128L)
                        || (not (receiptAuthorized connection scope) && streams >= 1024L)
                    then
                        Error [ "overload" ]
                    else
                        beginImmediate connection

                        try
                            receiptExecute
                                connection
                                "INSERT OR IGNORE INTO store_metadata(key,value) VALUES('receiptWorkspace',$w);"
                                [ "$w", box scope.Workspace ]

                            receiptExecute
                                connection
                                "INSERT OR IGNORE INTO receipt_producers(producer,stream,authority_role,grant_id,grant_generation) VALUES($p,$s,$role,$grant,$generation);"
                                [ "$p", box scope.Producer; "$s", box scope.Stream; "$role", box (roleName principal.Role)
                                  "$grant", principal.GrantId |> Option.map box |> Option.defaultValue DBNull.Value
                                  "$generation", principal.GrantGeneration |> Option.map box |> Option.defaultValue DBNull.Value ]

                            if not (principalAuthorized connection principal) then
                                invalidOp "producer authority conflicts with protected enrollment"

                            execute connection "COMMIT;"
                            Ok "{\"schema\":\"fsgg.telemetry.enrollment/1\",\"status\":\"enrolled\"}\n"
                        with error ->
                            rollback connection
                            raise error)

    let enrollReceiptProducer path assessment scope =
        enrollReceiptPrincipal path assessment (TelemetryReceipt.genericPrincipal scope)

    let private receiptRead (root: string) connection (scope: TelemetryReceipt.Scope) batch now =
        use command =
            receiptCommand
                connection
                "SELECT stream,digest,state,code,terminal_utc FROM transport_receipts WHERE producer=$p AND batch=$b;"
                [ "$p", box scope.Producer; "$b", box batch ]

        use reader = command.ExecuteReader()

        if not (reader.Read()) then
            Error [ "receipt-unavailable" ]
        elif reader.GetString(0) <> scope.Stream then
            Error [ "unauthorized-scope" ]
        else
            let state = reader.GetString(2)

            let recoverable =
                if state <> "durably-received" then
                    true
                else
                    let file = FileInfo(receiptPath root scope.Producer batch)

                    if
                        not file.Exists
                        || not (isNull file.LinkTarget)
                        || file.Length > int64 TelemetryReceipt.MaxAdmissionBytes
                    then
                        false
                    else
                        match TelemetryReceipt.parseAdmission (File.ReadAllBytes file.FullName) with
                        | Ok admission ->
                            admission.Envelope.Scope = scope
                            && admission.Envelope.BatchId = batch
                            && admission.Envelope.Digest = reader.GetString(1)
                            && admissionMatches connection admission
                        | Error _ -> false

            if not recoverable then
                Error [ "storage-unavailable" ]
            else
                let expired =
                    not (reader.IsDBNull 4)
                    && now
                       - DateTimeOffset.Parse(reader.GetString(4), Globalization.CultureInfo.InvariantCulture)
                       >= TimeSpan.FromDays 30.

                let code =
                    if expired || reader.IsDBNull 3 then
                        None
                    else
                        Some(reader.GetString 3)

                Ok(
                    JsonSerializer.Serialize
                        {|
                            schema = "fsgg.telemetry.receipt/1"
                            workspaceId = scope.Workspace
                            producerId = scope.Producer
                            streamId = scope.Stream
                            batchId = batch
                            digest = reader.GetString(1)
                            status = (if expired then "expired" else state)
                            code = code
                        |}
                    + "\n"
                )

    // Recovery scans only bounded, server-owned receipt artifacts. The index can never
    // acknowledge a pending obligation without its recoverable immutable envelope.
    let private recoverReceiptIndex root connection hook =
        let inbox = Path.Combine(root, "receipt-inbox")

        if Directory.Exists inbox then
            for temporary in Directory.EnumerateFiles(inbox, ".*.tmp") do
                File.Delete temporary

            fsyncDirectory inbox

            let files =
                Directory.EnumerateFiles(inbox, "*.ready") |> Seq.truncate 1025 |> Seq.toArray

            if files.Length > 1024 then
                invalidOp "receipt capacity inconsistent"
            // The common path is an already indexed pending obligation. Load that bounded index once
            // instead of reparsing every retained 64 KiB envelope and issuing two queries per file on
            // every one-shot CLI admission. The content digest still verifies every indexed artifact;
            // only an orphan or an interrupted post-commit cleanup needs the full envelope parser.
            let pending =
                Collections.Generic.Dictionary<string, string * string * string * string>()

            use indexed =
                receiptCommand
                    connection
                    "SELECT r.producer,r.batch,r.stream,r.digest,p.producer FROM transport_receipts r LEFT JOIN receipt_producers p ON p.producer=r.producer AND p.stream=r.stream WHERE r.state='durably-received';"
                    []

            use indexedReader = indexed.ExecuteReader()

            while indexedReader.Read() do
                if indexedReader.IsDBNull 4 then
                    invalidOp "invalid receipt binding"

                let producer, batch, stream, digest =
                    indexedReader.GetString(0),
                    indexedReader.GetString(1),
                    indexedReader.GetString(2),
                    indexedReader.GetString(3)

                pending.Add(TelemetryReceipt.key producer batch, (producer, batch, stream, digest))

            indexedReader.Close()
            let workspace = receiptWorkspace connection

            if pending.Count > 0 && String.IsNullOrEmpty workspace then
                invalidOp "invalid receipt binding"

            let seen = Collections.Generic.HashSet<string>(StringComparer.Ordinal)

            for file in files do
                let info = FileInfo file

                if
                    not (isNull info.LinkTarget)
                    || info.Length > int64 TelemetryReceipt.MaxAdmissionBytes
                then
                    invalidOp "invalid receipt artifact"

                let key = Path.GetFileNameWithoutExtension file

                match pending.TryGetValue key with
                | true, (producer, batch, stream, digest) ->
                    if file <> receiptPath root producer batch then
                        invalidOp "invalid receipt binding"

                    let admission =
                        TelemetryReceipt.parseAdmission (File.ReadAllBytes file)
                        |> Result.defaultWith (fun _ -> invalidOp "invalid receipt artifact")

                    if not (seen.Add key) then
                        invalidOp "invalid receipt artifact"

                    if admission.Envelope.Scope.Workspace <> workspace
                       || admission.Envelope.Scope.Producer <> producer
                       || admission.Envelope.Scope.Stream <> stream
                       || admission.Envelope.BatchId <> batch
                       || admission.Envelope.Digest <> digest then
                        invalidOp "invalid receipt binding"
                    if not (admissionMatches connection admission) then
                        // Schema-11 pending artifacts had no receiver-authored provenance. They remain generic.
                        if admission.Principal <> TelemetryReceipt.genericPrincipal admission.Envelope.Scope then
                            invalidOp "invalid receipt authority"
                        insertAdmission connection admission
                | false, _ ->
                    let admission =
                        TelemetryReceipt.parseAdmission (File.ReadAllBytes file)
                        |> Result.defaultWith (fun _ -> invalidOp "invalid receipt artifact")
                    let envelope = admission.Envelope

                    if
                        file <> receiptPath root envelope.Scope.Producer envelope.BatchId
                        || not (principalAuthorized connection admission.Principal)
                    then
                        invalidOp "invalid receipt binding"

                    let parameters = receiptParameters envelope

                    let prior =
                        receiptScalar
                            connection
                            "SELECT digest FROM transport_receipts WHERE producer=$p AND batch=$b;"
                            parameters

                    if not (isNull prior) && string prior <> envelope.Digest then
                        invalidOp "receipt identity conflict"

                    if isNull prior then
                        beginImmediate connection
                        try
                            receiptExecute
                                connection
                                "INSERT INTO transport_receipts(producer,batch,stream,digest,payload_bytes,state) VALUES($p,$b,$s,$d,$n,'durably-received');"
                                (parameters
                                 @ [ "$s", box envelope.Scope.Stream; "$d", box envelope.Digest; "$n", box info.Length ])
                            insertAdmission connection admission
                            execute connection "COMMIT;"
                            hook "index-committed"
                        with error ->
                            rollback connection
                            raise error
                    elif not (admissionMatches connection admission) then
                        invalidOp "receipt authority conflict"

                    let state =
                        string (
                            receiptScalar
                                connection
                                "SELECT state FROM transport_receipts WHERE producer=$p AND batch=$b;"
                                parameters
                        )

                    if state <> "durably-received" then
                        File.Delete file
                        fsyncDirectory inbox

            if seen.Count <> pending.Count then
                invalidOp "missing accepted obligation"
        elif
            Convert.ToInt64(
                receiptScalar connection "SELECT count(*) FROM transport_receipts WHERE state='durably-received';" []
            )
            <> 0L
        then
            invalidOp "missing accepted inbox"

    let submitReceiptPrincipalWithHook path assessment (principal: TelemetryReceipt.Principal) bytes hook =
        let scope = principal.Scope
        match TelemetryReceipt.parse bytes with
        | Error errors -> Error errors
        | Ok envelope ->
            match TelemetryReceipt.authorize scope envelope with
            | Error errors -> Error errors
            | Ok() ->
                receiptLocked path assessment (fun root connection ->
                    if not (principalAuthorized connection principal) then
                        Error [ "unauthorized-scope" ]
                    else
                        recoverReceiptIndex root connection hook
                        let parameters = receiptParameters envelope
                        let admissionCanonical =
                            TelemetryReceipt.encodeAdmission principal envelope
                            |> Result.defaultWith (fun _ -> invalidOp "invalid receipt authority")
                        let admission =
                            TelemetryReceipt.parseAdmission (Encoding.UTF8.GetBytes admissionCanonical)
                            |> Result.defaultWith (fun _ -> invalidOp "invalid receipt authority")

                        let prior =
                            receiptScalar
                                connection
                                "SELECT digest FROM transport_receipts WHERE producer=$p AND batch=$b;"
                                parameters

                        if not (isNull prior) then
                            if string prior <> envelope.Digest then
                                Error [ "identity-conflict" ]
                            elif not (admissionMatches connection admission) then
                                Error [ "identity-conflict" ]
                            else
                                receiptRead root connection scope envelope.BatchId DateTimeOffset.UtcNow
                        else
                            let count sql values =
                                Convert.ToInt64(receiptScalar connection sql values)

                            let size = int64 (Encoding.UTF8.GetByteCount admissionCanonical)
                            let p = [ "$p", box scope.Producer ]

                            if
                                count "SELECT count(*) FROM transport_receipts;" [] >= 1000000L
                                || count "SELECT count(*) FROM transport_receipts WHERE state='durably-received';" []
                                   >= 1024L
                                || count
                                    "SELECT coalesce(sum(payload_bytes),0) FROM transport_receipts WHERE state='durably-received';"
                                    []
                                   + size
                                    >
                                    64L * 1024L * 1024L
                                || count
                                    "SELECT count(*) FROM transport_receipts WHERE producer=$p AND state='durably-received';"
                                    p
                                   >= 128L
                                || count
                                    "SELECT coalesce(sum(payload_bytes),0) FROM transport_receipts WHERE producer=$p AND state='durably-received';"
                                    p
                                   + size
                                    >
                                    8L * 1024L * 1024L
                            then
                                Error [ "overload" ]
                            else
                                let inbox = Path.Combine(root, "receipt-inbox")
                                let createdInbox = not (Directory.Exists inbox)
                                Directory.CreateDirectory inbox |> ignore

                                File.SetUnixFileMode(
                                    inbox,
                                    UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
                                )

                                // The parent sync makes the inbox directory durable after its first creation.
                                // Re-syncing an unchanged parent on every admission adds another storage barrier
                                // to the protected warm path without making the new receipt any more durable.
                                if createdInbox then
                                    fsyncDirectory root
                                    hook "after-inbox-directory-sync"

                                let target = receiptPath root scope.Producer envelope.BatchId
                                let temporary = Path.Combine(inbox, "." + Guid.NewGuid().ToString("N") + ".tmp")

                                try
                                    // Flush(true) below is the single file durability barrier. Opening the
                                    // stream with WriteThrough as well would synchronously persist the write
                                    // and then immediately repeat that work at the explicit crash boundary.
                                    use stream =
                                        new FileStream(
                                            temporary,
                                            FileMode.CreateNew,
                                            FileAccess.Write,
                                            FileShare.None,
                                            4096,
                                            FileOptions.None
                                        )

                                    File.SetUnixFileMode(temporary, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
                                    stream.Write(Encoding.UTF8.GetBytes admissionCanonical)
                                    hook "before-file-sync"
                                    stream.Flush true
                                    hook "after-file-sync"
                                    stream.Close()
                                    File.Move(temporary, target, false)
                                    hook "after-rename"
                                    fsyncDirectory inbox
                                    hook "after-directory-sync"
                                    recoverReceiptIndex root connection hook
                                    receiptRead root connection scope envelope.BatchId DateTimeOffset.UtcNow
                                finally
                                    if File.Exists temporary then
                                        File.Delete temporary)

    let submitReceiptWithHook path assessment scope bytes hook =
        submitReceiptPrincipalWithHook path assessment (TelemetryReceipt.genericPrincipal scope) bytes hook

    let submitReceiptPrincipal path assessment principal bytes =
        submitReceiptPrincipalWithHook path assessment principal bytes ignore

    let submitReceipt path assessment scope bytes =
        submitReceiptWithHook path assessment scope bytes ignore

    let lookupReceipt path assessment (scope: TelemetryReceipt.Scope) batch =
        if not (TelemetryReceipt.validId batch) then
            Error [ "invalid-request" ]
        else
            match validateRoot path assessment with
            | Error errors -> Error errors
            | Ok root ->
                try
                    match connect root SqliteOpenMode.ReadOnly with
                    | Error _ -> Error [ "storage-unavailable" ]
                    | Ok(connection, _) ->
                        use connection = connection

                        if scalarText connection "PRAGMA user_version;" <> string currentSchemaVersion then
                            Error [ "unsupported-version" ]
                        elif
                            scalarText connection "SELECT digest FROM schema_migrations WHERE version=12;"
                            <> migration12Digest
                        then
                            Error [ "storage-unavailable" ]
                        elif not (receiptAuthorized connection scope) then
                            Error [ "unauthorized-scope" ]
                        else
                            receiptRead root connection scope batch DateTimeOffset.UtcNow
                with _ ->
                    Error [ "storage-unavailable" ]

    // Host-wide admission sums this read-only census across explicitly enrolled stores.
    // Tuple fields are lifetime identities, pending batches and pending canonical bytes.
    let receiptCapacity path assessment =
        match validateRoot path assessment with
        | Error errors -> Error errors
        | Ok root ->
            try
                match connect root SqliteOpenMode.ReadOnly with
                | Error _ -> Error [ "storage-unavailable" ]
                | Ok(connection, _) ->
                    use connection = connection

                    if scalarText connection "PRAGMA user_version;" <> string currentSchemaVersion then
                        Error [ "unsupported-version" ]
                    elif
                        scalarText connection "SELECT digest FROM schema_migrations WHERE version=12;"
                        <> migration12Digest
                    then
                        Error [ "storage-unavailable" ]
                    else
                        let count sql =
                            Convert.ToInt64(receiptScalar connection sql [])

                        Ok(
                            count "SELECT count(*) FROM transport_receipts;",
                            count "SELECT count(*) FROM transport_receipts WHERE state='durably-received';",
                            count
                                "SELECT coalesce(sum(payload_bytes),0) FROM transport_receipts WHERE state='durably-received';"
                        )
            with _ ->
                Error [ "storage-unavailable" ]

    let recoverReceiptCapacity path assessment =
        receiptLocked path assessment (fun _ connection ->
            recoverReceiptIndex path connection ignore

            let count sql =
                Convert.ToInt64(receiptScalar connection sql [])

            Ok(
                count "SELECT count(*) FROM transport_receipts;",
                count "SELECT count(*) FROM transport_receipts WHERE state='durably-received';",
                count "SELECT coalesce(sum(payload_bytes),0) FROM transport_receipts WHERE state='durably-received';"
            ))

    let drainReceiptsWithHook path assessment (workspace: string) hook =
        receiptLocked path assessment (fun root connection ->
            if receiptWorkspace connection <> workspace then
                Error [ "unauthorized-scope" ]
            else
                recoverReceiptIndex root connection hook

                let cursor =
                    string (
                        receiptScalar connection "SELECT value FROM store_metadata WHERE key='receiptDrainCursor';" []
                    )

                use command =
                    receiptCommand
                        connection
                        "SELECT producer,batch FROM (SELECT producer,batch,row_number() OVER(PARTITION BY producer ORDER BY batch) AS ordinal FROM transport_receipts WHERE state='durably-received') ORDER BY ordinal,CASE WHEN producer>$cursor THEN 0 ELSE 1 END,producer LIMIT 128;"
                        [ "$cursor", box cursor ]

                use reader = command.ExecuteReader()
                let selected = ResizeArray<string * string>()

                while reader.Read() do
                    selected.Add(reader.GetString 0, reader.GetString 1)

                reader.Close()
                let mutable bytes = 0L
                let mutable applied = 0
                let mutable rejected = 0
                let mutable failure = false

                for producer, batchId in selected do
                    let file = receiptPath root producer batchId
                    let size = FileInfo(file).Length

                    if bytes + size <= maxDrainBytes then
                        bytes <- bytes + size

                        let admission =
                            TelemetryReceipt.parseAdmission (File.ReadAllBytes file)
                            |> Result.defaultWith (fun _ -> invalidOp "invalid receipt artifact")
                        if not (admissionMatches connection admission) then
                            invalidOp "invalid receipt authority"
                        let envelope = admission.Envelope
                        // Only transport identities are adapted. Native fact identities/revisions remain unchanged.
                        let native =
                            { envelope.Batch with
                                IngestId = "receipt-" + envelope.Key
                                SourceIdentity =
                                    TelemetryReceipt.key
                                        producer
                                        (envelope.Scope.Stream + "\n" + envelope.Batch.SourceIdentity)
                                ContentDigest = envelope.Digest
                            }

                        let terminal (db: SqliteConnection) state code =
                            receiptExecute
                                db
                                "UPDATE transport_receipts SET state=$state,code=$code,terminal_utc=$utc WHERE producer=$p AND batch=$b;"
                                ([
                                    "$p", box producer
                                    "$b", box batchId
                                    "$state", box state
                                    "$code", code
                                    "$utc", box (DateTimeOffset.UtcNow.ToString("O"))
                                ])

                        let result =
                            ingestBatchWithReceiptLocked
                                root
                                (fun () -> hook "before-application-commit")
                                true
                                (fun db -> terminal db "applied" DBNull.Value)
                                (Some admission)
                                native

                        match result with
                        | Ok _ -> applied <- applied + 1
                        | Error errors when
                            errors
                            |> List.exists (fun error ->
                                error.Contains("conflict", StringComparison.OrdinalIgnoreCase)
                                || error.Contains("constraint", StringComparison.OrdinalIgnoreCase)
                                || error = "invalid-request"
                                || error.StartsWith("efficiency-", StringComparison.Ordinal))
                            ->
                            terminal connection "rejected" (box "semantic-conflict")
                            rejected <- rejected + 1
                        | Error _ -> failure <- true

                        if not failure then
                            hook "after-application-commit"
                            File.Delete file
                            hook "after-cleanup"
                            fsyncDirectory (Path.GetDirectoryName file)

                            receiptExecute
                                connection
                                "INSERT INTO store_metadata(key,value) VALUES('receiptDrainCursor',$p) ON CONFLICT(key) DO UPDATE SET value=excluded.value;"
                                [ "$p", box producer ]

                if failure then
                    Error [ "storage-unavailable" ]
                else
                    Ok(
                        JsonSerializer.Serialize
                            {|
                                schema = "fsgg.telemetry.receipt-drain/1"
                                applied = applied
                                rejected = rejected
                            |}
                        + "\n"
                    ))

    let drainReceipts path assessment workspace =
        drainReceiptsWithHook path assessment workspace ignore

    let resolveNativeCollectorDispatch path assessment dispatchId nativeAgentId =
        if not (TelemetryReceipt.validId dispatchId) || not (TelemetryReceipt.validId nativeAgentId) then
            Error [ "invalid-request" ]
        else
            match validateRoot path assessment with
            | Error errors -> Error errors
            | Ok root ->
                try
                    match connect root SqliteOpenMode.ReadOnly with
                    | Error _ -> Error [ "storage-unavailable" ]
                    | Ok(connection, _) ->
                        use connection = connection

                        if scalarText connection "PRAGMA user_version;" <> string currentSchemaVersion then
                            Error [ "unsupported-version" ]
                        else
                            use command = connection.CreateCommand()
                            command.CommandText <-
                                """
SELECT d.item_id,l.invocation_id,l.root_invocation_id,a.requested_model,a.requested_effort
FROM expected_dispatches d
JOIN invocation_lineage l ON l.item_id=d.item_id AND l.dispatch_id=d.dispatch_id
JOIN runtime_admissions a ON a.item_id=l.item_id AND a.invocation_id=l.invocation_id
JOIN runtime_terminals t ON t.item_id=l.item_id AND t.invocation_id=l.invocation_id
JOIN runtime_starts s ON s.item_id=l.item_id AND s.invocation_id=l.invocation_id AND s.phase='process'
WHERE d.dispatch_id=$dispatch AND d.relation='child' AND d.runtime='collaboration-spawn-agent'
  AND l.relation=d.relation AND l.runtime=d.runtime AND a.backend='codex-collaboration'
  AND s.thread_id=$native;
"""
                            parameter command "$dispatch" dispatchId
                            parameter command "$native" nativeAgentId
                            use reader = command.ExecuteReader()

                            if not (reader.Read()) then
                                Error [ "native collector dispatch is unavailable" ]
                            else
                                let item = reader.GetString 0
                                let invocation = reader.GetString 1
                                let rootInvocation = reader.GetString 2
                                let model = if reader.IsDBNull 3 then "" else reader.GetString 3
                                let effort = if reader.IsDBNull 4 then "" else reader.GetString 4

                                if reader.Read() || String.IsNullOrWhiteSpace model || String.IsNullOrWhiteSpace effort then
                                    Error [ "native collector dispatch is ambiguous" ]
                                else
                                    reader.Close()
                                    use originals = connection.CreateCommand()
                                    originals.CommandText <-
                                        "SELECT DISTINCT original_item_id FROM budget_population_facts WHERE item_id=$item;"
                                    parameter originals "$item" item
                                    use originalReader = originals.ExecuteReader()

                                    if not (originalReader.Read()) then
                                        Error [ "native collector original item is unavailable" ]
                                    else
                                        let original = originalReader.GetString 0

                                        if originalReader.Read() then
                                            Error [ "native collector original item is ambiguous" ]
                                        else
                                            Ok
                                                {
                                                    ItemId = item
                                                    OriginalItemId = original
                                                    InvocationId = invocation
                                                    RootInvocationId = rootInvocation
                                                    RequestedModel = model
                                                    RequestedEffort = effort
                                                }
                with _ ->
                    Error [ "storage-unavailable" ]

    let resolveInstalledOriginAt now path assessment (query: InstalledOriginQuery) =
        let hash (value: string) =
            not (String.IsNullOrWhiteSpace value)
            && System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9a-f]{64}$")
            && value <> String('0', 64)

        if
            query.Role <> "native-collector"
            || query.GrantGeneration <= 0L
            || not (TelemetryReceipt.validId query.WorkspaceId)
            || not (TelemetryReceipt.validId query.ProducerId)
            || not (TelemetryReceipt.validId query.StreamId)
            || not (TelemetryReceipt.validId query.GrantId)
            || not ([ query.ManagerReceiptSha256; query.CapabilityProfileSha256;
                       query.CapabilityResultSha256; query.NativeCaptureSha256;
                       query.NativeVerificationSha256; query.InstallationSha256 ] |> List.forall hash)
        then
            Error [ "invalid-request" ]
        else
            match validateRoot path assessment with
            | Error errors -> Error errors
            | Ok root ->
                try
                    match connect root SqliteOpenMode.ReadOnly with
                    | Error _ -> Error [ "storage-unavailable" ]
                    | Ok(connection, _) ->
                        use connection = connection
                        if scalarText connection "PRAGMA user_version;" <> string currentSchemaVersion then
                            Error [ "unsupported-version" ]
                        else
                            use command = connection.CreateCommand()
                            command.CommandText <-
                                """
SELECT f.identity,f.revision,f.canonical,f.content_digest,
       a.receipt_key,a.envelope_digest
FROM ingest_facts f
JOIN fact_admissions a ON a.identity=f.identity
JOIN receipt_producers p ON p.producer=a.producer AND p.stream=a.stream
JOIN receipt_admissions ra ON ra.producer=a.producer AND ra.stream=a.stream
  AND ra.receipt_key=a.receipt_key AND ra.envelope_digest=a.envelope_digest
JOIN transport_receipts r ON r.producer=ra.producer AND r.batch=ra.batch AND r.state='applied'
WHERE f.kind='learn-installed-origin/1' AND f.item_id IS NULL
  AND a.producer=$producer AND a.stream=$stream AND a.authority_role='native-collector'
  AND a.grant_id=$grant AND a.grant_generation=$generation
  AND p.authority_role=a.authority_role AND p.grant_id=a.grant_id
  AND p.grant_generation=a.grant_generation
  AND (SELECT value FROM store_metadata WHERE key='receiptWorkspace')=$workspace
  AND json_extract(f.canonical,'$.workspaceId')=$workspace
  AND json_extract(f.canonical,'$.producerId')=$producer
  AND json_extract(f.canonical,'$.streamId')=$stream
  AND json_extract(f.canonical,'$.role')='native-collector'
  AND json_extract(f.canonical,'$.grantId')=$grant
  AND json_extract(f.canonical,'$.grantGeneration')=$generation
  AND json_extract(f.canonical,'$.managerReceiptSha256')=$manager
  AND json_extract(f.canonical,'$.capabilityProfileSha256')=$profile
  AND json_extract(f.canonical,'$.capabilityResultSha256')=$result
  AND json_extract(f.canonical,'$.nativeCaptureSha256')=$capture
  AND json_extract(f.canonical,'$.nativeVerificationSha256')=$verification
  AND json_extract(f.canonical,'$.installationSha256')=$installation
LIMIT 2;
"""
                            [ "$workspace", box query.WorkspaceId; "$producer", box query.ProducerId;
                              "$stream", box query.StreamId; "$grant", box query.GrantId;
                              "$generation", box query.GrantGeneration;
                              "$manager", box query.ManagerReceiptSha256;
                              "$profile", box query.CapabilityProfileSha256;
                              "$result", box query.CapabilityResultSha256;
                              "$capture", box query.NativeCaptureSha256;
                              "$verification", box query.NativeVerificationSha256;
                              "$installation", box query.InstallationSha256 ]
                            |> List.iter (fun (name, value) -> parameter command name value)
                            use reader = command.ExecuteReader()
                            if not (reader.Read()) then
                                Error [ "learning-installed-origin-unavailable" ]
                            else
                                let identity = reader.GetString 0
                                let revision = reader.GetInt64 1
                                let canonical = reader.GetString 2
                                let digest = reader.GetString 3
                                let receiptKey = reader.GetString 4
                                let envelopeDigest = reader.GetString 5
                                if reader.Read() then
                                    Error [ "learning-installed-origin-ambiguous" ]
                                elif CanonicalJson.sha256(Encoding.UTF8.GetBytes canonical) <> digest then
                                    Error [ "learning-installed-origin-corrupt" ]
                                else
                                    use document = JsonDocument.Parse canonical
                                    let root = document.RootElement
                                    let text (name: string) = root.GetProperty(name).GetString()
                                    let number (name: string) = root.GetProperty(name).GetInt64()
                                    if
                                        text "workspaceId" <> query.WorkspaceId
                                        || text "producerId" <> query.ProducerId
                                        || text "streamId" <> query.StreamId
                                        || text "role" <> query.Role
                                        || text "grantId" <> query.GrantId
                                        || number "grantGeneration" <> query.GrantGeneration
                                        || text "managerReceiptSha256" <> query.ManagerReceiptSha256
                                        || text "capabilityProfileSha256" <> query.CapabilityProfileSha256
                                        || text "capabilityResultSha256" <> query.CapabilityResultSha256
                                        || text "nativeCaptureSha256" <> query.NativeCaptureSha256
                                        || text "nativeVerificationSha256" <> query.NativeVerificationSha256
                                        || text "installationSha256" <> query.InstallationSha256
                                    then
                                        Error [ "learning-installed-origin-unavailable" ]
                                    else
                                        let mutable observedAt = DateTimeOffset.MinValue
                                        let mutable expiresAt = DateTimeOffset.MinValue
                                        let observed = text "capabilityObservedAt"
                                        let expires = text "capabilityExpiresAt"
                                        if
                                            not (DateTimeOffset.TryParseExact(observed, "O", Globalization.CultureInfo.InvariantCulture,
                                                                              Globalization.DateTimeStyles.RoundtripKind, &observedAt))
                                            || not (DateTimeOffset.TryParseExact(expires, "O", Globalization.CultureInfo.InvariantCulture,
                                                                                 Globalization.DateTimeStyles.RoundtripKind, &expiresAt))
                                            || observedAt > now
                                            || expiresAt <= observedAt
                                            || expiresAt <= now
                                        then
                                            Error [ "learning-installed-origin-unavailable" ]
                                        else
                                            Ok {
                                                RecordId = identity
                                                Revision = revision
                                                ObservedAt = observed
                                                ExpiresAt = expires
                                                InstallationSha256 = text "installationSha256"
                                                ReceiptKey = receiptKey
                                                EnvelopeDigest = envelopeDigest
                                            }
                with _ -> Error [ "storage-unavailable" ]

    let resolveInstalledOrigin path assessment query =
        resolveInstalledOriginAt DateTimeOffset.UtcNow path assessment query

    let readNativeRoutePopulation path assessment originalItemId =
        if not (TelemetryReceipt.validId originalItemId) then
            Error [ "invalid-request" ]
        else
            match validateRoot path assessment with
            | Error errors -> Error errors
            | Ok root ->
                try
                    match connect root SqliteOpenMode.ReadOnly with
                    | Error _ -> Error [ "storage-unavailable" ]
                    | Ok(connection, _) ->
                        use connection = connection
                        if scalarText connection "PRAGMA user_version;" <> string currentSchemaVersion then
                            Error [ "unsupported-version" ]
                        else
                            use transaction = connection.BeginTransaction()
                            use ambiguity = connection.CreateCommand()
                            ambiguity.Transaction <- transaction
                            ambiguity.CommandText <-
                                """
SELECT CASE WHEN (SELECT count(*) FROM budget_population_facts WHERE original_item_id=$original)=0
            THEN -1 ELSE count(*) END FROM (
  SELECT item_id FROM budget_population_facts
  WHERE item_id IN (SELECT item_id FROM budget_population_facts WHERE original_item_id=$original)
  GROUP BY item_id HAVING count(DISTINCT original_item_id) <> 1
);
"""
                            parameter ambiguity "$original" originalItemId
                            match Convert.ToInt64(ambiguity.ExecuteScalar()) with
                            | -1L -> Error [ "native route original mapping is unavailable" ]
                            | value when value <> 0L ->
                                Error [ "native route original mapping is ambiguous" ]
                            | _ ->
                                use command = connection.CreateCommand()
                                command.Transaction <- transaction
                                command.CommandText <-
                                    """
SELECT d.item_id,d.dispatch_id,d.relation,d.runtime,
       l.invocation_id,l.parent_invocation_id,l.root_invocation_id,l.runtime,
       a.requested_model,a.requested_effort,a.backend,
       CASE WHEN s.invocation_id IS NULL THEN 0 ELSE 1 END,
       CASE WHEN t.invocation_id IS NULL THEN 0 ELSE 1 END
FROM expected_dispatches d
LEFT JOIN invocation_lineage l ON l.item_id=d.item_id AND l.dispatch_id=d.dispatch_id
LEFT JOIN runtime_admissions a ON a.item_id=l.item_id AND a.invocation_id=l.invocation_id
LEFT JOIN runtime_starts s ON s.item_id=l.item_id AND s.invocation_id=l.invocation_id AND s.phase='process'
LEFT JOIN runtime_terminals t ON t.item_id=l.item_id AND t.invocation_id=l.invocation_id
WHERE d.item_id IN (SELECT item_id FROM budget_population_facts WHERE original_item_id=$original)
ORDER BY d.item_id,d.dispatch_id
LIMIT 257;
"""
                                parameter command "$original" originalItemId
                                use reader = command.ExecuteReader()
                                let routes = JsonArray()
                                while reader.Read() do
                                    if routes.Count = 256 then invalidOp "native route population exceeds the bound"
                                    let optional index = if reader.IsDBNull index then null else JsonValue.Create(reader.GetString index) :> JsonNode
                                    let runtime = reader.GetString 3
                                    let relation = reader.GetString 2
                                    let started = reader.GetInt64 11 = 1L
                                    let terminal = reader.GetInt64 12 = 1L
                                    let support =
                                        if runtime = "collaboration-spawn-agent" && relation = "child"
                                           && not (reader.IsDBNull 4) && not (reader.IsDBNull 10)
                                           && reader.GetString(10) = "codex-collaboration" then
                                            if started && terminal then "terminal-child-observed"
                                            elif started then "started-child-observed"
                                            else "child-route-evidence-incomplete"
                                        else "unsupported-runtime-or-role"
                                    let row = JsonObject()
                                    row["itemId"] <- JsonValue.Create(reader.GetString 0)
                                    row["dispatchId"] <- JsonValue.Create(reader.GetString 1)
                                    row["relation"] <- JsonValue.Create relation
                                    row["runtime"] <- JsonValue.Create runtime
                                    row["invocationId"] <- optional 4
                                    row["parentInvocationId"] <- optional 5
                                    row["rootInvocationId"] <- optional 6
                                    row["lineageRuntime"] <- optional 7
                                    row["requestedModel"] <- optional 8
                                    row["requestedEffort"] <- optional 9
                                    row["backend"] <- optional 10
                                    row["started"] <- JsonValue.Create started
                                    row["terminal"] <- JsonValue.Create terminal
                                    row["support"] <- JsonValue.Create support
                                    routes.Add row
                                reader.Close()
                                use facts = connection.CreateCommand()
                                facts.Transaction <- transaction
                                facts.CommandText <-
                                    """
SELECT f.identity,f.item_id,f.kind,f.revision,f.content_digest,
       a.authority_role,a.grant_id,a.grant_generation,a.receipt_key,a.envelope_digest
FROM ingest_facts f
LEFT JOIN fact_admissions a ON a.identity=f.identity
WHERE f.item_id IN (SELECT item_id FROM budget_population_facts WHERE original_item_id=$original)
  AND f.kind IN ('runtime-native-inventory/1','runtime-native-inventory-source/1',
                 'learn-shared-cost/1','learn-shared-cost-allocation/1',
                 'learn-shared-cost-authority/1','learn-native-delivery-source/1')
ORDER BY f.item_id,f.kind,f.identity
LIMIT 257;
"""
                                parameter facts "$original" originalItemId
                                use factReader = facts.ExecuteReader()
                                let selectedFacts = JsonArray()
                                while factReader.Read() do
                                    if selectedFacts.Count = 256 then invalidOp "native route fact population exceeds the bound"
                                    let optional index = if factReader.IsDBNull index then null else JsonValue.Create(factReader.GetString index) :> JsonNode
                                    let row = JsonObject()
                                    row["identity"] <- JsonValue.Create(factReader.GetString 0)
                                    row["itemId"] <- JsonValue.Create(factReader.GetString 1)
                                    row["kind"] <- JsonValue.Create(factReader.GetString 2)
                                    row["revision"] <- JsonValue.Create(factReader.GetInt64 3)
                                    row["contentDigest"] <- JsonValue.Create(factReader.GetString 4)
                                    row["receiptRole"] <- optional 5
                                    row["grantId"] <- optional 6
                                    row["grantGeneration"] <-
                                        if factReader.IsDBNull 7 then null
                                        else JsonValue.Create(factReader.GetInt64 7) :> JsonNode
                                    row["receiptKey"] <- optional 8
                                    row["envelopeDigest"] <- optional 9
                                    selectedFacts.Add row
                                let output = JsonObject()
                                output["schema"] <- JsonValue.Create "fsgg.telemetry.native-route-population/1"
                                output["originalItemId"] <- JsonValue.Create originalItemId
                                output["windowBinding"] <- JsonValue.Create "unknown"
                                output["populationComplete"] <- JsonValue.Create false
                                output["terminalChildEstablishesRoleCoverage"] <- JsonValue.Create false
                                output["allocationReferenceIsAuthority"] <- JsonValue.Create false
                                output["routes"] <- routes
                                output["facts"] <- selectedFacts
                                let canonical = CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(output.ToJsonString())) |> Result.defaultWith invalidOp
                                if Encoding.UTF8.GetByteCount canonical > 1048576 then
                                    Error [ "native route result exceeds the byte bound" ]
                                else
                                    transaction.Rollback()
                                    Ok(canonical + "\n")
                with _ -> Error [ "storage-unavailable" ]

    let resolveNativeDeliveryCandidate path assessment (sourceRef: string) =
        if String.IsNullOrWhiteSpace sourceRef || sourceRef.Length > 1024 || sourceRef |> Seq.exists Char.IsControl then
            Error [ "invalid-request" ]
        else
            match validateRoot path assessment with
            | Error errors -> Error errors
            | Ok root ->
                try
                    match connect root SqliteOpenMode.ReadOnly with
                    | Error _ -> Error [ "storage-unavailable" ]
                    | Ok(connection, _) ->
                        use connection = connection
                        if scalarText connection "PRAGMA user_version;" <> string currentSchemaVersion then
                            Error [ "unsupported-version" ]
                        else
                            use command = connection.CreateCommand()
                            command.CommandText <-
                                """
SELECT n.identity,n.item_id,n.repository,n.pr_number,n.head,n.source_ref,
       f.canonical,f.content_digest,a.authority_role,a.grant_id,a.grant_generation,
       a.receipt_key,a.envelope_digest
FROM native_item_outcomes n
JOIN ingest_facts f ON f.identity=n.identity AND f.kind='native-item-outcome'
LEFT JOIN fact_admissions a ON a.identity=f.identity
WHERE n.source_ref=$source;
"""
                            parameter command "$source" sourceRef
                            use reader = command.ExecuteReader()
                            if not (reader.Read()) then
                                Error [ "native delivery candidate is unavailable" ]
                            else
                                let optionalText index = if reader.IsDBNull index then None else Some(reader.GetString index)
                                let optionalInt index = if reader.IsDBNull index then None else Some(reader.GetInt64 index)
                                let identity = reader.GetString 0
                                let item = reader.GetString 1
                                let repository = reader.GetString 2
                                let pullRequest = reader.GetInt64 3
                                let head = reader.GetString 4
                                let retainedSource = reader.GetString 5
                                let canonicalFact = reader.GetString 6
                                let factDigest = reader.GetString 7
                                let role = optionalText 8
                                let grant = optionalText 9
                                let generation = optionalInt 10
                                let receiptKey = optionalText 11
                                let envelopeDigest = optionalText 12
                                if reader.Read() then
                                    Error [ "native delivery candidate is ambiguous" ]
                                elif (use corrected = connection.CreateCommand()
                                      corrected.CommandText <- "SELECT count(*) FROM ci_effective_attribution WHERE identity=$identity;"
                                      parameter corrected "$identity" identity
                                      Convert.ToInt64(corrected.ExecuteScalar()) > 0L) then
                                    Error [ "corrected-native-delivery-source-unsupported" ]
                                elif CanonicalJson.sha256(Encoding.UTF8.GetBytes canonicalFact) <> factDigest then
                                    Error [ "native delivery candidate fact digest differs" ]
                                else
                                    use factDocument = JsonDocument.Parse canonicalFact
                                    let fact = factDocument.RootElement
                                    let agrees =
                                        fact.ValueKind = JsonValueKind.Object
                                        && fact.GetProperty("identity").GetString() = identity
                                        && fact.GetProperty("kind").GetString() = "native-item-outcome"
                                        && fact.GetProperty("itemId").GetString() = item
                                        && fact.GetProperty("repository").GetString() = repository
                                        && fact.GetProperty("prNumber").GetInt64() = pullRequest
                                        && fact.GetProperty("head").GetString() = head
                                        && fact.GetProperty("sourceRef").GetString() = retainedSource
                                    if not agrees then
                                        Error [ "native delivery candidate differs from immutable fact" ]
                                    else
                                        let binding = JsonObject()
                                        binding["schema"] <- JsonValue.Create "fsgg.telemetry.native-delivery-candidate-binding/1"
                                        binding["canonicalFact"] <- JsonValue.Create canonicalFact
                                        binding["factDigest"] <- JsonValue.Create factDigest
                                        binding["receiptRole"] <- role |> Option.map JsonValue.Create |> Option.defaultValue null
                                        binding["receiptGrantId"] <- grant |> Option.map JsonValue.Create |> Option.defaultValue null
                                        binding["receiptGrantGeneration"] <- generation |> Option.map JsonValue.Create |> Option.defaultValue null
                                        binding["receiptKey"] <- receiptKey |> Option.map JsonValue.Create |> Option.defaultValue null
                                        binding["receiptEnvelopeDigest"] <- envelopeDigest |> Option.map JsonValue.Create |> Option.defaultValue null
                                        let canonicalBinding =
                                            CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(binding.ToJsonString()))
                                            |> Result.defaultWith invalidOp
                                        Ok {
                                            Identity = identity; ItemId = item; Repository = repository
                                            PullRequest = pullRequest; ExpectedHead = head; SourceRef = retainedSource
                                            CanonicalFact = canonicalFact; FactDigest = factDigest
                                            ReceiptRole = role; ReceiptGrantId = grant
                                            ReceiptGrantGeneration = generation; ReceiptKey = receiptKey
                                            ReceiptEnvelopeDigest = envelopeDigest; Binding = canonicalBinding
                                            BindingDigest = CanonicalJson.sha256(Encoding.UTF8.GetBytes canonicalBinding)
                                        }
                with _ -> Error [ "storage-unavailable" ]

    let private fileDigest path =
        use stream = File.OpenRead path
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData stream).ToLowerInvariant()

    let private flushFile path =
        use stream =
            new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.WriteThrough)

        stream.Flush true

    let backupReceiptStore path assessment workspace (outputPath: string) =
        if not (Path.IsPathFullyQualified outputPath) then
            Error [ "backup output path must be absolute" ]
        elif File.Exists outputPath || Directory.Exists outputPath then
            Error [ "backup output already exists" ]
        else
            receiptLocked path assessment (fun root connection ->
                if receiptWorkspace connection <> workspace then
                    Error [ "unauthorized-scope" ]
                else
                    recoverReceiptIndex root connection ignore
                    use projectionTransaction = connection.BeginTransaction()

                    match verifyScopedProvenance connection projectionTransaction workspace ignore with
                    | Error errors ->
                        projectionTransaction.Rollback()
                        Error errors
                    | Ok() ->
                        projectionTransaction.Rollback()
                        let target = Path.GetFullPath outputPath

                        let rootPrefix =
                            root.TrimEnd(Path.DirectorySeparatorChar) + string Path.DirectorySeparatorChar

                        if target.StartsWith(rootPrefix, StringComparison.Ordinal) then
                            Error [ "backup must be outside the store root" ]
                        else
                            let parent = Path.GetDirectoryName target
                            Directory.CreateDirectory parent |> ignore

                            let temporary =
                                Path.Combine(
                                    parent,
                                    "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp"
                                )

                            try
                                Directory.CreateDirectory temporary |> ignore

                                if not (OperatingSystem.IsWindows()) then
                                    File.SetUnixFileMode(
                                        temporary,
                                        UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
                                    )

                                let databaseTarget = Path.Combine(temporary, databaseFileName)

                                use destination =
                                    new SqliteConnection(
                                        $"Data Source={databaseTarget};Mode=ReadWriteCreate;Pooling=False"
                                    )

                                destination.Open()
                                connection.BackupDatabase destination
                                destination.Close()
                                flushFile databaseTarget
                                let files = ResizeArray<string * string * int64>()
                                files.Add(databaseFileName, fileDigest databaseTarget, FileInfo(databaseTarget).Length)
                                let inbox = Path.Combine(root, "receipt-inbox")

                                if Directory.Exists inbox then
                                    let outputInbox = Path.Combine(temporary, "receipt-inbox")
                                    Directory.CreateDirectory outputInbox |> ignore

                                    for source in
                                        Directory.EnumerateFiles(inbox, "*.ready", SearchOption.TopDirectoryOnly)
                                        |> Seq.truncate 1025 do
                                        let info = FileInfo source

                                        if
                                            not (isNull info.LinkTarget)
                                            || info.Length > int64 TelemetryReceipt.MaxEnvelopeBytes
                                        then
                                            invalidOp "invalid receipt artifact"

                                        let relative = Path.Combine("receipt-inbox", info.Name)
                                        let copied = Path.Combine(temporary, relative)
                                        File.Copy(source, copied, false)
                                        flushFile copied

                                        files.Add(
                                            relative.Replace(Path.DirectorySeparatorChar, '/'),
                                            fileDigest copied,
                                            info.Length
                                        )

                                    fsyncDirectory outputInbox

                                if files.Count > 1025 then
                                    invalidOp "receipt capacity inconsistent"

                                let manifest =
                                    JsonSerializer.Serialize
                                        {|
                                            schema = "fsgg.telemetry.host-backup/1"
                                            storeSchemaVersion = currentSchemaVersion
                                            workspaceId = workspace
                                            files =
                                                files
                                                |> Seq.map (fun (name, digest, size) ->
                                                    {|
                                                        path = name
                                                        sha256 = digest
                                                        bytes = size
                                                    |})
                                                |> Seq.toArray
                                        |}

                                let outputManifest = Path.Combine(temporary, "manifest.json")
                                File.WriteAllText(outputManifest, manifest + "\n", UTF8Encoding(false))
                                flushFile outputManifest
                                fsyncDirectory temporary
                                Directory.Move(temporary, target)
                                fsyncDirectory parent

                                Ok(
                                    JsonSerializer.Serialize
                                        {|
                                            schema = "fsgg.telemetry.host-backup-result/1"
                                            workspaceId = workspace
                                            output = target
                                            files = files.Count
                                        |}
                                    + "\n"
                                )
                            finally
                                if Directory.Exists temporary then
                                    Directory.Delete(temporary, true))

    let restoreReceiptStore (inputPath: string) (path: string) assessment workspace =
        try
            if
                not (Path.IsPathFullyQualified inputPath)
                || not (Directory.Exists inputPath)
                || existingAncestors inputPath
                   |> List.exists (fun entry -> not (isNull entry.LinkTarget))
            then
                Error [ "backup input is unavailable" ]
            elif
                not (Path.IsPathFullyQualified path)
                || Directory.Exists path
                || File.Exists path
            then
                Error [ "restore target must be a fresh path" ]
            else
                match validateRoot path assessment with
                | Error errors -> Error errors
                | Ok root ->
                    let manifestPath = Path.Combine(inputPath, "manifest.json")

                    if not (File.Exists manifestPath) || FileInfo(manifestPath).Length > 1024L * 1024L then
                        Error [ "backup-integrity-failed" ]
                    else
                        use document = JsonDocument.Parse(File.ReadAllBytes manifestPath)
                        let top = document.RootElement
                        let topNames = top.EnumerateObject() |> Seq.map _.Name |> Seq.toArray

                        if
                            topNames.Length <> 4
                            || Array.distinct topNames |> Array.length <> 4
                            || Set.ofArray topNames
                               <> set["schema"
                                      "storeSchemaVersion"
                                      "workspaceId"
                                      "files"]
                            || top.GetProperty("schema").GetString() <> "fsgg.telemetry.host-backup/1"
                            || not ((set [ 9; 12; currentSchemaVersion ]).Contains(top.GetProperty("storeSchemaVersion").GetInt32()))
                            || top.GetProperty("workspaceId").GetString() <> workspace
                        then
                            Error [ "backup-incompatible" ]
                        else
                            let files = top.GetProperty("files").EnumerateArray() |> Seq.toArray

                            if files.Length = 0 || files.Length > 1025 then
                                Error [ "backup-integrity-failed" ]
                            else
                                let validated = ResizeArray<string * string * string * int64>()
                                let paths = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
                                let mutable valid = true
                                let mutable totalBytes = 0L

                                for entry in files do
                                    let names = entry.EnumerateObject() |> Seq.map _.Name |> Seq.toArray
                                    let relative = entry.GetProperty("path").GetString()
                                    let expected = entry.GetProperty("sha256").GetString()
                                    let bytes = entry.GetProperty("bytes").GetInt64()

                                    let source =
                                        if isNull relative then
                                            ""
                                        else
                                            Path.GetFullPath(Path.Combine(inputPath, relative))

                                    let prefix =
                                        Path.GetFullPath(inputPath).TrimEnd(Path.DirectorySeparatorChar)
                                        + string Path.DirectorySeparatorChar

                                    let canonicalReceipt =
                                        not (isNull relative)
                                        && relative.StartsWith("receipt-inbox/", StringComparison.Ordinal)
                                        && relative.EndsWith(".ready", StringComparison.Ordinal)
                                        && relative.Length = 14 + 64 + 6
                                        && relative.Substring(14, 64)
                                           |> Seq.forall (fun c ->
                                               Char.IsAsciiHexDigit c && not (Char.IsLetter(c) && Char.IsUpper(c)))

                                    let allowed = relative = databaseFileName || canonicalReceipt

                                    if
                                        names.Length <> 3
                                        || Array.distinct names |> Array.length <> 3
                                        || Set.ofArray names
                                           <> set["path"
                                                  "sha256"
                                                  "bytes"]
                                        || not allowed
                                        || not (paths.Add relative)
                                        || bytes < 0L
                                        || bytes > 4L * 1024L * 1024L * 1024L
                                        || source = ""
                                        || not (source.StartsWith(prefix, StringComparison.Ordinal))
                                        || not (File.Exists source)
                                        || not (isNull (FileInfo(source).LinkTarget))
                                        || FileInfo(source).Length <> bytes
                                        || fileDigest source <> expected
                                    then
                                        valid <- false
                                    else
                                        totalBytes <- totalBytes + bytes
                                        validated.Add(relative, source, expected, bytes)

                                let topEntries =
                                    Directory.EnumerateFileSystemEntries(inputPath, "*", SearchOption.TopDirectoryOnly)
                                    |> Seq.truncate 4
                                    |> Seq.toArray

                                let linkTarget entry =
                                    if Directory.Exists entry then
                                        DirectoryInfo(entry).LinkTarget
                                    else
                                        FileInfo(entry).LinkTarget

                                if
                                    topEntries.Length > 3
                                    || topEntries
                                       |> Array.exists (fun entry ->
                                           not (isNull (linkTarget entry))
                                           || (Path.GetFileName entry <> "manifest.json"
                                               && Path.GetFileName entry <> databaseFileName
                                               && Path.GetFileName entry <> "receipt-inbox"))
                                then
                                    valid <- false

                                let inboxPath = Path.Combine(inputPath, "receipt-inbox")

                                let inboxEntries =
                                    if Directory.Exists inboxPath then
                                        Directory.EnumerateFileSystemEntries(
                                            inboxPath,
                                            "*",
                                            SearchOption.TopDirectoryOnly
                                        )
                                        |> Seq.truncate 1026
                                        |> Seq.toArray
                                    else
                                        [||]

                                if
                                    inboxEntries.Length > 1025
                                    || inboxEntries
                                       |> Array.exists (fun entry ->
                                           Directory.Exists entry || not (isNull (FileInfo(entry).LinkTarget)))
                                then
                                    valid <- false

                                let actualFiles =
                                    Seq.append (topEntries |> Seq.filter File.Exists) inboxEntries
                                    |> Seq.map (fun file ->
                                        Path.GetRelativePath(inputPath, file).Replace(Path.DirectorySeparatorChar, '/'))
                                    |> Set.ofSeq

                                let expectedFiles = Set.add "manifest.json" (paths |> Set.ofSeq)

                                if
                                    not valid
                                    || totalBytes > 4L * 1024L * 1024L * 1024L
                                    || actualFiles <> expectedFiles
                                    || validated
                                       |> Seq.filter (fun (relative, _, _, _) -> relative = databaseFileName)
                                       |> Seq.length
                                       <> 1
                                then
                                    Error [ "backup-integrity-failed" ]
                                else
                                    let parent = Path.GetDirectoryName root
                                    Directory.CreateDirectory parent |> ignore

                                    let temporary =
                                        Path.Combine(
                                            parent,
                                            "."
                                            + Path.GetFileName(root)
                                            + "."
                                            + Guid.NewGuid().ToString("N")
                                            + ".restore"
                                        )

                                    Directory.CreateDirectory temporary |> ignore

                                    if not (OperatingSystem.IsWindows()) then
                                        File.SetUnixFileMode(
                                            temporary,
                                            UnixFileMode.UserRead
                                            ||| UnixFileMode.UserWrite
                                            ||| UnixFileMode.UserExecute
                                        )

                                    try
                                        for relative, source, expected, bytes in validated do
                                            let target = Path.Combine(temporary, relative)
                                            Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
                                            File.Copy(source, target, false)
                                            flushFile target

                                            if FileInfo(target).Length <> bytes || fileDigest target <> expected then
                                                invalidOp "copied backup changed"

                                        if Directory.Exists(Path.Combine(temporary, "receipt-inbox")) then
                                            fsyncDirectory (Path.Combine(temporary, "receipt-inbox"))

                                        fsyncDirectory temporary

                                        let backupSchemaVersion = top.GetProperty("storeSchemaVersion").GetInt32()

                                        let restoredStatus =
                                            if backupSchemaVersion = 9 then
                                                initialize temporary assessment
                                            else
                                                status temporary assessment

                                        match restoredStatus with
                                        | Error errors ->
                                            Directory.Delete(temporary, true)
                                            Error errors
                                        | Ok _ ->
                                            match recoverReceiptCapacity temporary assessment with
                                            | Error errors ->
                                                Directory.Delete(temporary, true)
                                                Error errors
                                            | Ok _ ->
                                                match connect temporary SqliteOpenMode.ReadOnly with
                                                | Error errors ->
                                                    Directory.Delete(temporary, true)
                                                    Error errors
                                                | Ok(restored, _) ->
                                                    use restored = restored
                                                    use transaction = restored.BeginTransaction()

                                                    match
                                                        verifyScopedProvenance restored transaction workspace ignore
                                                    with
                                                    | Error errors ->
                                                        transaction.Rollback()
                                                        Directory.Delete(temporary, true)
                                                        Error errors
                                                    | Ok() ->
                                                        transaction.Rollback()
                                                        Directory.Move(temporary, root)
                                                        fsyncDirectory parent

                                                        Ok(
                                                            JsonSerializer.Serialize
                                                                {|
                                                                    schema = "fsgg.telemetry.host-restore-result/1"
                                                                    workspaceId = workspace
                                                                    root = root
                                                                    storeSchemaVersion = currentSchemaVersion
                                                                |}
                                                            + "\n"
                                                        )
                                    with error ->
                                        if Directory.Exists temporary then
                                            Directory.Delete(temporary, true)

                                        raise error
        with _ ->
            Error [ "backup-integrity-failed" ]

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

        let runtimeSum column =
            runtimeScalar $"SELECT coalesce(sum(%s{column}),0) FROM runtime_turn_usage WHERE item_id=$item;"

        let latestCoverage column fallback =
            use command = connection.CreateCommand()

            command.CommandText <-
                $"SELECT %s{column} FROM coverage_observations WHERE item_id=$item ORDER BY rowid DESC LIMIT 1;"

            parameter command "$item" itemId
            let value = command.ExecuteScalar()

            if isNull value || value = box DBNull.Value then
                fallback
            else
                string value

        let runtimeTurns =
            runtimeScalar "SELECT count(*) FROM runtime_turn_usage WHERE item_id=$item;"

        let usageCount = count "usage_observations" + runtimeTurns

        let reasoning =
            use command = connection.CreateCommand()

            command.CommandText <-
                "SELECT CASE WHEN count(*)=count(reasoning) THEN coalesce(sum(reasoning),0) ELSE NULL END FROM (SELECT reasoning FROM usage_observations WHERE item_id=$item UNION ALL SELECT reasoning FROM runtime_turn_usage WHERE item_id=$item);"

            parameter command "$item" itemId
            let value = command.ExecuteScalar()

            if isNull value || value = box DBNull.Value then
                None
            else
                Some(Convert.ToInt64 value)

        {
            ItemId = itemId
            FactCount =
                runtimeScalar
                                    "SELECT count(*) FROM current_ingest_facts WHERE item_id=$item AND kind NOT IN ('learn-task-snapshot','learn-context-manifest','learn-experiment-assignment','learn-accounting-inventory/1','runtime-native-inventory/1','runtime-native-inventory-source/1','learn-shared-cost/1','learn-shared-cost-allocation/1','learn-shared-cost-authority/1','learn-native-delivery-source/1','learn-installed-origin/1');"
            UsageObservations = usageCount
            DeliveryObservations = count "delivery_observations"
            Input = sum "input_count" + runtimeSum "input_count"
            CachedInput = sum "cached_input" + runtimeSum "cached_input"
            CacheWriteInput = sum "cache_write_input"
            Output = sum "output_count" + runtimeSum "output_count"
            Reasoning = reasoning
            Total = sum "total" + runtimeSum "total"
            Admitted = runtimeScalar "SELECT count(*) FROM runtime_admissions WHERE item_id=$item;"
            Started =
                runtimeScalar
                    "SELECT count(DISTINCT invocation_id) FROM runtime_starts WHERE item_id=$item AND phase='process';"
            Terminal = runtimeScalar "SELECT count(*) FROM runtime_terminals WHERE item_id=$item;"
            RuntimeUsage =
                runtimeScalar "SELECT count(DISTINCT invocation_id) FROM runtime_turn_usage WHERE item_id=$item;"
            MissingAdmission =
                runtimeScalar
                    "SELECT count(*) FROM (SELECT invocation_id FROM runtime_starts WHERE item_id=$item UNION SELECT invocation_id FROM runtime_terminals WHERE item_id=$item UNION SELECT invocation_id FROM runtime_turn_usage WHERE item_id=$item) x WHERE NOT EXISTS (SELECT 1 FROM runtime_admissions a WHERE a.invocation_id=x.invocation_id);"
            MissingStart =
                runtimeScalar
                    "SELECT count(*) FROM runtime_admissions a WHERE item_id=$item AND NOT EXISTS (SELECT 1 FROM runtime_starts s WHERE s.invocation_id=a.invocation_id AND s.phase='process');"
            MissingTerminal =
                runtimeScalar
                    "SELECT count(*) FROM runtime_admissions a WHERE item_id=$item AND NOT EXISTS (SELECT 1 FROM runtime_terminals t WHERE t.invocation_id=a.invocation_id);"
            MissingUsage =
                runtimeScalar
                    "SELECT count(*) FROM runtime_admissions a WHERE item_id=$item AND NOT EXISTS (SELECT 1 FROM runtime_turn_usage u WHERE u.invocation_id=a.invocation_id);"
            RecordValidity = latestCoverage "record_validity" "unknown"
            JoinIntegrity = latestCoverage "join_integrity" "unknown"
            PopulationCoverage = latestCoverage "population_coverage" "unknown"
            Qualification = latestCoverage "qualification" "not-evaluated"
        }

    let summary path assessment itemId =
        match
            validateRoot path assessment
            |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly)
        with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection
            use snapshot = connection.BeginTransaction()
            Ok(TelemetryStore.publicJson (readSummary connection itemId))

    let private readReviews (connection: SqliteConnection) (itemId: string) limit =
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT scope,attempt_id,fact_revision,outcome_synopsis,went_well,problems,avoidable_delay_rework,process_observations,remaining_risks,concrete_improvements,evidence,evidence_coverage,population_coverage,confidence,reviewer_model,reviewer_effort,reviewed_at,duration_seconds FROM process_reviews WHERE item_id=$item ORDER BY CASE scope WHEN 'attempt' THEN 0 ELSE 1 END,coalesce(attempt_id,''),fact_revision DESC LIMIT $limit;"

        parameter command "$item" itemId
        parameter command "$limit" limit
        use reader = command.ExecuteReader()
        let rows = ResizeArray<JsonElement>()

        let optional index =
            if reader.IsDBNull index then
                None
            else
                Some(reader.GetString index)

        let json index =
            use document = JsonDocument.Parse(reader.GetString index) in document.RootElement.Clone()

        while reader.Read() do
            rows.Add(
                JsonSerializer.SerializeToElement
                    {|
                        scope = reader.GetString 0
                        attemptId = optional 1
                        revision = reader.GetInt64 2
                        outcomeSynopsis = reader.GetString 3
                        wentWell = json 4
                        problems = json 5
                        avoidableDelayOrRework = json 6
                        processObservations = json 7
                        remainingRisks = json 8
                        concreteImprovements = json 9
                        evidence = json 10
                        evidenceCoverage = reader.GetString 11
                        populationCoverage = reader.GetString 12
                        confidence = reader.GetString 13
                        reviewerModel = reader.GetString 14
                        reviewerEffort = reader.GetString 15
                        reviewedAt = reader.GetString 16
                        durationSeconds = reader.GetInt64 17
                    |}
            )

        rows.ToArray()

    let reviewSummary path assessment (itemId: string) =
        match
            validateRoot path assessment
            |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly)
        with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection

            try
                let reviews = readReviews connection itemId 129
                let truncated = reviews.Length > 128
                let bounded = if truncated then reviews[..127] else reviews

                Ok(
                    JsonSerializer.Serialize
                        {|
                            schema = "fsgg.telemetry.process-review-summary/1"
                            item = itemId
                            reviews = bounded
                            truncated = truncated
                        |}
                    + "\n"
                )
            with error ->
                Error [ error.Message ]

    let itemDetail path assessment (itemId: string) =
        match
            validateRoot path assessment
            |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly)
        with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection

            try
                let optional (reader: SqliteDataReader) index =
                    if reader.IsDBNull index then
                        None
                    else
                        Some(reader.GetString index)

                let json (reader: SqliteDataReader) index =
                    use document = JsonDocument.Parse(reader.GetString index) in document.RootElement.Clone()

                let activities = ResizeArray<JsonElement>()
                use activity = connection.CreateCommand()

                activity.CommandText <-
                    "SELECT activity_id,invocation_id,attempt_id,category,started_at,ended_at,clock_provenance,evidence,summary,fact_revision FROM activity_spans WHERE item_id=$item ORDER BY started_at,activity_id LIMIT 257;"

                parameter activity "$item" itemId
                use activityReader = activity.ExecuteReader()

                while activityReader.Read() do
                    activities.Add(
                        JsonSerializer.SerializeToElement
                            {|
                                activityId = activityReader.GetString 0
                                invocationId = activityReader.GetString 1
                                attemptId = activityReader.GetString 2
                                category = activityReader.GetString 3
                                startedAt = activityReader.GetString 4
                                endedAt = optional activityReader 5
                                clockProvenance = activityReader.GetString 6
                                evidence = json activityReader 7
                                summary = optional activityReader 8
                                revision = activityReader.GetInt64 9
                            |}
                    )

                activityReader.Close()
                let attributions = ResizeArray<JsonElement>()
                use attribution = connection.CreateCommand()

                attribution.CommandText <-
                    "SELECT usage_identity,activity_id,classification,input_count,cached_input,output_count,reasoning,total,fact_revision FROM activity_usage_attributions WHERE item_id=$item ORDER BY usage_identity LIMIT 257;"

                parameter attribution "$item" itemId
                use attributionReader = attribution.ExecuteReader()

                while attributionReader.Read() do
                    attributions.Add(
                        JsonSerializer.SerializeToElement
                            {|
                                usageIdentity = attributionReader.GetString 0
                                activityId = optional attributionReader 1
                                classification = attributionReader.GetString 2
                                input = attributionReader.GetInt64 3
                                cachedInput = attributionReader.GetInt64 4
                                output = attributionReader.GetInt64 5
                                reasoning =
                                    (if attributionReader.IsDBNull 6 then
                                         None
                                     else
                                         Some(attributionReader.GetInt64 6))
                                total = attributionReader.GetInt64 7
                                revision = attributionReader.GetInt64 8
                            |}
                    )

                attributionReader.Close()
                let complications = ResizeArray<JsonElement>()
                use complication = connection.CreateCommand()

                complication.CommandText <-
                    "SELECT attempt_id,activity_id,trigger,cause,occurred_at,synopsis,evidence,fact_revision FROM complication_events WHERE item_id=$item ORDER BY occurred_at,identity LIMIT 257;"

                parameter complication "$item" itemId
                use complicationReader = complication.ExecuteReader()

                while complicationReader.Read() do
                    complications.Add(
                        JsonSerializer.SerializeToElement
                            {|
                                attemptId = optional complicationReader 0
                                activityId = optional complicationReader 1
                                trigger = complicationReader.GetString 2
                                cause = complicationReader.GetString 3
                                occurredAt = complicationReader.GetString 4
                                synopsis = complicationReader.GetString 5
                                evidence = json complicationReader 6
                                revision = complicationReader.GetInt64 7
                            |}
                    )

                complicationReader.Close()

                let accounting classification =
                    use command = connection.CreateCommand()

                    command.CommandText <-
                        "SELECT coalesce(sum(total),0) FROM activity_usage_attributions WHERE item_id=$item AND classification=$classification;"

                    parameter command "$item" itemId
                    parameter command "$classification" classification
                    Convert.ToInt64(command.ExecuteScalar())

                let missing =
                    use command = connection.CreateCommand()

                    command.CommandText <-
                        "SELECT count(*) FROM runtime_turn_usage u WHERE item_id=$item AND NOT EXISTS(SELECT 1 FROM activity_usage_attributions a WHERE a.item_id=u.item_id AND a.usage_identity=u.identity);"

                    parameter command "$item" itemId
                    Convert.ToInt64(command.ExecuteScalar())

                let nativeTotal =
                    use command = connection.CreateCommand()
                    command.CommandText <- "SELECT coalesce(sum(total),0) FROM runtime_turn_usage WHERE item_id=$item;"
                    parameter command "$item" itemId
                    Convert.ToInt64(command.ExecuteScalar())

                let reviews = readReviews connection itemId 129

                let bounded (values: ResizeArray<JsonElement>) =
                    if values.Count > 256 then
                        (values.ToArray())[..255]
                    else
                        values.ToArray()

                let result =
                    JsonSerializer.Serialize(
                        {|
                            schema = "fsgg.telemetry.item-detail/1"
                            item = itemId
                            activities = bounded activities
                            activityTruncated = activities.Count > 256
                            usageAttributions = bounded attributions
                            attributionTruncated = attributions.Count > 256
                            complications = bounded complications
                            complicationTruncated = complications.Count > 256
                            reviews = (if reviews.Length > 128 then reviews[..127] else reviews)
                            reviewTruncated = reviews.Length > 128
                            accounting =
                                {|
                                    nativeTotal = nativeTotal
                                    direct = accounting "direct"
                                    mixed = accounting "mixed"
                                    unclassified = accounting "unclassified"
                                    missingAttribution = missing
                                    allocation = "native-exact-only"
                                |}
                        |}
                    )
                    + "\n"

                if Encoding.UTF8.GetByteCount result > 1024 * 1024 then
                    Error [ "item detail exceeds 1048576 bytes" ]
                else
                    Ok result
            with error ->
                Error [ error.Message ]

    // Dashboard snapshot /2 is intentionally a read model, not another persistence
    // schema.  Every database value below is selected on this one connection while
    // one explicit transaction is open.  Keeping the table vocabulary here makes
    // the engine the sole owner of SQLite and migration knowledge.
    let private ciSummaryInSnapshot (connection: SqliteConnection) (transaction: SqliteTransaction) (itemId: string) =
        let scalar sql =
            use command = connection.CreateCommand()
            command.Transaction <- transaction
            command.CommandText <- sql
            parameter command "$item" itemId
            Convert.ToInt64(command.ExecuteScalar())

        let intervals sql =
            use command = connection.CreateCommand()
            command.Transaction <- transaction
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
            command.Transaction <- transaction

            command.CommandText <-
                $"SELECT %s{column} FROM ci_coverage WHERE item_id=$item ORDER BY rowid DESC LIMIT 1;"

            parameter command "$item" itemId
            let value = command.ExecuteScalar()

            if isNull value || value = box DBNull.Value then
                "unknown"
            else
                string value

        let population column fallback =
            use command = connection.CreateCommand()
            command.Transaction <- transaction

            command.CommandText <-
                $"SELECT %s{column} FROM ci_population_coverage WHERE item_id=$item ORDER BY fact_revision DESC LIMIT 1;"

            parameter command "$item" itemId
            let value = command.ExecuteScalar()

            if isNull value || value = box DBNull.Value then
                fallback
            else
                string value

        let jobs =
            intervals "SELECT started_at,completed_at FROM ci_jobs WHERE item_id=$item;"

        let queues =
            intervals "SELECT created_at,started_at FROM ci_jobs WHERE item_id=$item;"

        let runner =
            if jobs.IsEmpty then
                None
            else
                jobs
                |> List.sumBy (fun value -> int64 (value.EndUtc - value.StartUtc).TotalSeconds)
                |> Some

        let wall = TelemetryCi.unionSeconds jobs

        let classified classification =
            use command = connection.CreateCommand()
            command.Transaction <- transaction

            command.CommandText <-
                "SELECT started_at,completed_at FROM ci_steps WHERE item_id=$item AND classification=$classification;"

            parameter command "$item" itemId
            parameter command "$classification" classification
            use reader = command.ExecuteReader()
            let values = ResizeArray<TelemetryCi.Interval>()

            while reader.Read() do
                TelemetryCi.interval
                    (if reader.IsDBNull 0 then None else Some(reader.GetString 0))
                    (if reader.IsDBNull 1 then None else Some(reader.GetString 1))
                |> Option.iter values.Add

            values |> Seq.toList |> TelemetryCi.unionSeconds

        let effectiveAssignments =
            use command = connection.CreateCommand()
            command.Transaction <- transaction
            command.CommandText <- "SELECT c.effective_feature,c.effective_attempt,c.correction_id,CASE WHEN EXISTS(SELECT 1 FROM ci_correction_evidence x WHERE x.correction_id=c.correction_id AND x.table_name='ci_bindings') THEN 'retained-binding' ELSE 'operator-evidence-only' END FROM native_item_outcomes n JOIN ci_effective_attribution e ON e.identity=n.identity JOIN ci_attribution_corrections c ON c.correction_id=e.correction_id WHERE n.item_id=$item ORDER BY n.identity;"
            parameter command "$item" itemId
            use reader = command.ExecuteReader()
            [ while reader.Read() do yield {| featureId = reader.GetString 0; attemptId = reader.GetString 1; correctionId = reader.GetString 2; assignmentProvenance = reader.GetString 3 |} ]

        JsonSerializer.Serialize
            {|
                schema = "fsgg.telemetry.ci-summary/1"
                item = itemId
                deliveries = scalar "SELECT count(*) FROM native_item_outcomes WHERE item_id=$item AND code_delivery='delivered';"
                effectiveAssignments = effectiveAssignments
                runs =
                    scalar
                        "SELECT count(*) FROM (SELECT repository,run_id FROM ci_runs WHERE item_id=$item UNION SELECT repository,run_id FROM ci_jobs WHERE item_id=$item);"
                attempts =
                    scalar
                        "SELECT count(*) FROM (SELECT repository,run_id,attempt FROM ci_runs WHERE item_id=$item UNION SELECT repository,run_id,attempt FROM ci_jobs WHERE item_id=$item);"
                jobs = scalar "SELECT count(*) FROM ci_jobs WHERE item_id=$item;"
                steps = scalar "SELECT count(*) FROM ci_steps WHERE item_id=$item;"
                runnerSeconds = runner
                wallSeconds = wall
                queueSeconds = TelemetryCi.unionSeconds queues
                usefulValidationSeconds = classified "useful-validation"
                administrativeSeconds = classified "admin"
                necessarySetupSeconds = classified "necessary-setup"
                mixedSeconds = classified "mixed"
                unclassifiedSeconds = classified "unclassified"
                monetary = "unknown"
                avoidableRerun = "unknown"
                inventoryCoverage = population "actions" (coverage "inventory")
                checkCoverage = population "checks" "unknown"
                attemptCoverage = population "attempts" (coverage "attempts")
                jobPageCoverage = population "jobs" (coverage "job_pages")
                terminalCoverage = population "terminal" (coverage "terminal")
                timestampCoverage = population "timestamps" (coverage "timestamps")
                continuation = population "continuation" "none"
                externalChecks = Int64.Parse(population "external_checks" "0")
                populationGaps = population "gaps" "[]"
                lineageCoverage = coverage "lineage"
                classificationCoverage = coverage "classification"
                criticalPathCoverage = coverage "critical_path"
            |}
        + "\n"

    let private efficiencySourceFingerprint (connection: SqliteConnection) (transaction: SqliteTransaction) =
        use hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256)
        let append (value: string) =
            let bytes = Encoding.UTF8.GetBytes value
            // Length-prefix each field so nulls, separators and Unicode cannot collide.
            hash.AppendData(Encoding.ASCII.GetBytes(string bytes.Length + ":"))
            hash.AppendData bytes
        append "fsgg.telemetry.efficiency-source-fingerprint/1"
        for sql in
            [ "SELECT identity,kind,item_id,revision,content_digest FROM current_ingest_facts ORDER BY identity;"
              "SELECT correction_id,plan_digest,outcome_identity,effective_item,applied_at FROM ci_attribution_corrections ORDER BY correction_id;"
              "SELECT identity,fact_revision,content_digest,accepted_at,receipt_key FROM fact_acceptance_times ORDER BY identity,fact_revision;"
              "SELECT identity,kind,item_id,fact_revision,content_digest,canonical FROM efficiency_records ORDER BY identity;"
              "SELECT identity,resource_identity,resource_revision,resource_digest,effective_item_id,correction_identity,correction_digest,dimension,provider,accounting_scope,unit FROM efficiency_allocation_context ORDER BY identity;"
              "SELECT identity,item_id,invocation_id,event,occurred_at,occurred_clock_provenance,observed_at,observed_clock_provenance,fact_revision FROM operational_event_times ORDER BY identity;"
              "SELECT request_id,revision,content_digest,state,updated_at FROM efficiency_analysis_requests ORDER BY request_id;"
              "SELECT producer,stream,authority_role,grant_id,grant_generation FROM receipt_producers ORDER BY producer,stream;"
              "SELECT identity,producer,stream,authority_role,grant_id,grant_generation,receipt_key,envelope_digest FROM fact_admissions ORDER BY identity;"
              "SELECT sequence,receipt_key,producer,stream,accepted_at FROM efficiency_receiver_order ORDER BY sequence;"
              "SELECT original_item_id,epoch,dispatch_identity,state,outcome_identity,outcome_revision,begin_sequence,close_sequence,begin_refs,close_refs FROM efficiency_outcome_epochs ORDER BY original_item_id,epoch;"
              "SELECT receipt_key,original_item_id,reason FROM efficiency_epoch_gaps ORDER BY receipt_key,original_item_id,reason;"
              "SELECT stable_outcome_identity,epoch_key,policy_version,reservation,claim_id,state FROM efficiency_analysis_reservations ORDER BY stable_outcome_identity,epoch_key,policy_version,reservation;" ] do
            append sql
            use command = connection.CreateCommand()
            command.Transaction <- transaction
            command.CommandText <- sql
            use reader = command.ExecuteReader()
            while reader.Read() do
                append "row"
                for index in 0 .. reader.FieldCount - 1 do
                    if reader.IsDBNull index then append "null"
                    else
                        append "value"
                        append (Convert.ToString(reader.GetValue index, Globalization.CultureInfo.InvariantCulture))
        "sha256:" + (hash.GetHashAndReset() |> Convert.ToHexString |> _.ToLowerInvariant())

    let private efficiencyExportInSnapshot (expectedRevision: string) (maxItems: int) (maxMetrics: int)
        (connection: SqliteConnection) (transaction: SqliteTransaction) (baseSnapshot: JsonObject) revision =
        if revision <> expectedRevision then invalidOp "efficiency-snapshot-revision-mismatch"
        if maxItems <> 200 || maxMetrics <> 1000 then invalidOp "efficiency-export-selection-bound"
        let cutoff = DateTimeOffset.UtcNow.ToString("O")
        let fingerprint = efficiencySourceFingerprint connection transaction
        let query (sql: string) (values: (string * obj) list) =
            use command = connection.CreateCommand()
            command.Transaction <- transaction
            let members = values |> List.tryFind (fun (name,_) -> name="$members")
            command.CommandText <- if members.IsSome then sql.Replace("item_id=$item","item_id IN (SELECT value FROM json_each($members))") else sql
            values |> List.filter (fun (name,_) -> name<>"$members" || command.CommandText.Contains("$members",StringComparison.Ordinal))
                   |> List.iter (fun (name, value) -> parameter command name value)
            use reader = command.ExecuteReader()
            [ while reader.Read() do
                yield [ for index in 0 .. reader.FieldCount - 1 ->
                            if reader.IsDBNull index then None else Some(Convert.ToString(reader.GetValue index, Globalization.CultureInfo.InvariantCulture)) ] ]
        let sourceItems = baseSnapshot["items"].AsArray() |> Seq.map (fun node -> node.GetValue<string>()) |> Seq.toList
        let groups =
            sourceItems |> List.map (fun item ->
                let original =
                    match query "SELECT DISTINCT original_item_id FROM (SELECT original_item_id FROM budget_population_facts WHERE item_id=$item UNION SELECT original_item_id FROM efficiency_outcome_epochs WHERE effective_item_id=$item) ORDER BY original_item_id LIMIT 2;" [ "$item",box item ] with
                    | [ [ Some original ] ] -> original
                    | [] -> item
                    | _ -> invalidOp "efficiency-original-item-lineage-conflict"
                original,item)
            |> List.groupBy fst |> List.map (fun (original,rows) -> original,(rows |> List.map snd |> List.distinct |> List.sort)) |> List.sortBy fst
        let items = groups |> List.map fst
        let outputItems = JsonArray()
        let mutable returned = 0
        let mutable omitted = 0
        for item,members in groups |> List.truncate maxItems do
            // Canonical group scope is witnessed by the actual store population/epoch
            // mapping. Each source identity appears once across its exact member roster.
            let values = [ "$item",box item; "$members",box(JsonSerializer.Serialize members) ]
            let facts =
                query "SELECT identity,kind,revision,content_digest,canonical FROM current_ingest_facts WHERE item_id=$item AND kind IN ('runtime-turn-usage','usage','native-item-outcome') ORDER BY identity LIMIT 4097;" values
            if facts.Length > 4096 then invalidOp "efficiency-export-source-selection-bound"
            let refs = JsonArray()
            let counters = ResizeArray<string * string * string * string * bigint>()
            let mutable delivered = 0I
            for row in facts do
                let identity, kind, factRevision, digest, canonical = row[0].Value, row[1].Value, row[2].Value, row[3].Value, row[4].Value
                if refs.Count < 1000 then
                    let semantic = EfficiencyEvidence.semanticKind kind |> Option.defaultWith (fun () -> invalidOp "efficiency-metric-reference-kind-unavailable")
                    refs.Add(JsonSerializer.SerializeToNode({| id = identity; kind = semantic; revision = Int64.Parse factRevision |}))
                use document = JsonDocument.Parse canonical
                let source = document.RootElement
                if kind = "native-item-outcome" then
                    if source.GetProperty("codeDelivery").GetString() = "delivered" then delivered <- delivered + 1I
                else
                    let provider =
                        let value = source.GetProperty "provider"
                        if value.ValueKind = JsonValueKind.String then value.GetString() else "unknown-provider"
                    let scope = if kind = "runtime-turn-usage" then source.GetProperty("scope").GetString() else "legacy-usage"
                    for property, unit in [ "input", "tokens-input"; "output", "tokens-output"; "total", "tokens-total" ] do
                        counters.Add(provider, scope, property, unit, bigint (source.GetProperty(property).GetInt64()))
            let metrics = JsonArray()
            let original = item
            let mutable groupOpenItems = 1
            let metric (metricName: string) (unit: string) (scope: string) (provider: string) (amount: bigint option) (status: string) (reason: string) (purpose: string) (health: string) =
                let population =
                    {| itemIds = List.toArray members; repository = "unknown"; workType = "observed-population-at-cutoff"
                       acceptanceScope = scope; windowStart = cutoff; windowEnd = cutoff; cutoff = cutoff
                       excludedItems = ([||] : obj array); openItems = groupOpenItems; abandonedItems = 0 |}
                let node =
                    JsonSerializer.SerializeToNode
                        {| schema = "fsgg.telemetry.efficiency-metric/1"
                           metricId = "sha256:" + CanonicalJson.sha256(Encoding.UTF8.GetBytes(item + "\n" + metricName + "\n" + unit + "\n" + provider + "\n" + scope + "\n" + (if isNull purpose then "" else purpose) + "\n" + (if isNull health then "" else health)))
                           metric = metricName; calculationVersion = "efficiency-calculation/1"; unit = unit
                           population = population
                           coverage = {| population = "unknown"; usage = "partial"; classification = "unknown"; lineage = "unknown"; dependency = "unknown" |}
                           policyProfile = (null : string)
                           price = {| kind = "not-applicable"; currency = (null : string); version = (null : string) |}
                           eventTime = (null : string); observedAt = cutoff; projectedAt = cutoff
                           purpose = purpose; healthDimension = health |}
                node["sourceRefs"] <- refs.DeepClone()
                let value = JsonObject()
                value["status"] <- JsonValue.Create status
                value["numerator"] <- amount |> Option.map (fun number -> JsonNode.Parse(number.ToString(Globalization.CultureInfo.InvariantCulture))) |> Option.defaultValue null
                value["denominator"] <- if amount.IsSome then JsonValue.Create(1) else null
                value["unknownAmount"] <- null
                value["reason"] <- JsonValue.Create reason
                node["value"] <- value
                metrics.Add node
            for ((provider, scope, _, unit), resources) in counters |> Seq.groupBy (fun (provider, scope, property, unit, _) -> provider, scope, property, unit) do
                let total = resources |> Seq.sumBy (fun (_, _, _, _, amount) -> amount)
                metric "observed-resource" unit scope provider (Some total) "known" "Observed counters only; unobserved population and usage are not reconstructed." (null: string) (null: string)
            let purposeOf = function
                | "direct-product" -> ProcessEfficiency.DirectProduct
                | "useful-assurance" -> ProcessEfficiency.UsefulAssurance
                | "necessary-coordination" -> ProcessEfficiency.NecessaryCoordination
                | "process-improvement" -> ProcessEfficiency.ProcessImprovement
                | "avoidable-process" -> ProcessEfficiency.AvoidableProcess
                | "unknown" -> ProcessEfficiency.UnknownPurpose
                | _ -> invalidOp "efficiency-purpose-unsupported"
            let purposeName = function
                | ProcessEfficiency.DirectProduct -> "direct-product"
                | ProcessEfficiency.UsefulAssurance -> "useful-assurance"
                | ProcessEfficiency.NecessaryCoordination -> "necessary-coordination"
                | ProcessEfficiency.ProcessImprovement -> "process-improvement"
                | ProcessEfficiency.AvoidableProcess -> "avoidable-process"
                | ProcessEfficiency.UnknownPurpose -> "unknown"
            let exactValue (number: bigint) = JsonNode.Parse(number.ToString(Globalization.CultureInfo.InvariantCulture))
            let ratio name provider scope purpose (fraction: ProcessEfficiency.Fraction option) status reason =
                metric name "ratio" scope provider None status reason purpose (null: string)
                match fraction with
                | None -> ()
                | Some value ->
                    let node = metrics.[metrics.Count - 1].["value"]
                    node["numerator"] <- exactValue value.Numerator
                    node["denominator"] <- exactValue value.Denominator
            // Native resource identities are reduced once per dimension/provider/scope.
            // A stale allocation has no classified shares, while its actual source cost remains.
            let resourceRows = ResizeArray<ProcessEfficiency.Resource>()
            for row in facts do
                let identity, kind, canonical = row[0].Value, row[1].Value, row[4].Value
                if kind = "runtime-turn-usage" || kind = "usage" then
                    use sourceDocument = JsonDocument.Parse canonical
                    let source = sourceDocument.RootElement
                    let providerNode = source.GetProperty "provider"
                    let provider = if providerNode.ValueKind = JsonValueKind.String then providerNode.GetString() else "unknown-provider"
                    let scope = if kind = "runtime-turn-usage" then source.GetProperty("scope").GetString() else "legacy-usage"
                    for property, unit in [ "input", "tokens-input"; "output", "tokens-output"; "total", "tokens-total" ] do
                        let amount = bigint (source.GetProperty(property).GetInt64())
                        let allocation =
                            query
                                "SELECT a.canonical,a.classification_current FROM efficiency_current_allocations a JOIN efficiency_allocation_context c ON c.identity=a.identity WHERE c.resource_identity=$resource AND c.provider=$provider AND c.accounting_scope=$scope AND c.unit=$unit;"
                                [ "$resource", box identity; "$dimension", box property; "$provider", box provider; "$scope", box scope; "$unit", box unit ]
                        let shares =
                            match allocation with
                            | [ [ Some body; Some "1" ] ] ->
                                use document = JsonDocument.Parse body
                                document.RootElement.GetProperty("shares").EnumerateArray()
                                |> Seq.map (fun share ->
                                    let value = share.GetProperty "fraction"
                                    let selected: ProcessEfficiency.Share =
                                        { ItemId = share.GetProperty("itemId").GetString()
                                          Purpose = purposeOf (share.GetProperty("purpose").GetString())
                                          Fraction = { Numerator = bigint (value.GetProperty("numerator").GetInt64()); Denominator = bigint (value.GetProperty("denominator").GetInt64()) } }
                                    selected)
                                |> Seq.toList
                            | _ -> []
                        resourceRows.Add
                            { Key = { Identity = identity; Dimension = property; Provider = provider; AccountingScope = scope }
                              Amount = amount; Shares = shares }
            for ((dimension, provider, accountingScope), resources) in resourceRows |> Seq.groupBy (fun resource -> resource.Key.Dimension, resource.Key.Provider, resource.Key.AccountingScope) do
                let scope = accountingScope + "/tokens-" + dimension
                let allocation = ProcessEfficiency.allocate (Seq.toList resources) |> Result.defaultWith invalidOp
                if allocation.Total = 0I then
                    ratio "work-mix" provider scope "unknown" None "not-applicable" "The observed resource total is zero."
                else
                    // Keep unknown classification and unallocated source exposure in the same
                    // explicit purpose bucket; never convert it to avoidable process work.
                    for purpose in [ ProcessEfficiency.DirectProduct; ProcessEfficiency.UsefulAssurance; ProcessEfficiency.NecessaryCoordination; ProcessEfficiency.ProcessImprovement; ProcessEfficiency.AvoidableProcess; ProcessEfficiency.UnknownPurpose ] do
                        let known = allocation.ByPurpose |> Map.tryFind purpose |> Option.defaultValue { Numerator = 0I; Denominator = 1I }
                        let amount =
                            if purpose <> ProcessEfficiency.UnknownPurpose then known
                            else
                                ProcessEfficiency.fraction
                                    (known.Numerator * allocation.Unallocated.Denominator + allocation.Unallocated.Numerator * known.Denominator)
                                    (known.Denominator * allocation.Unallocated.Denominator)
                                |> Result.defaultWith invalidOp
                        let value = ProcessEfficiency.fraction amount.Numerator (amount.Denominator * allocation.Total) |> Result.defaultWith invalidOp
                        ratio "work-mix" provider scope (purposeName purpose) (Some value) "known" "Exact accepted allocations; unknown and unallocated exposure is preserved."
                    let avoidable = allocation.ByPurpose |> Map.tryFind ProcessEfficiency.AvoidableProcess |> Option.defaultValue { Numerator = 0I; Denominator = 1I }
                    let share = ProcessEfficiency.fraction avoidable.Numerator (avoidable.Denominator * allocation.Total) |> Result.defaultWith invalidOp
                    ratio "avoidable-share" provider scope (null: string) (Some share) (if allocation.Unallocated.Numerator > 0I || (allocation.ByPurpose |> Map.tryFind ProcessEfficiency.UnknownPurpose |> Option.exists (fun amount -> amount.Numerator > 0I)) then "partial" else "known") "Only supported avoidability classifications contribute; this is a lower bound when classification is incomplete."
            let analysisIds = query "SELECT DISTINCT u.identity FROM runtime_turn_usage u JOIN efficiency_analysis_requests q ON q.invocation_ref=u.invocation_id WHERE u.item_id=$item;" values |> List.choose (function [ Some id ] -> Some id | _ -> None) |> Set.ofList
            let analysisCounters = ResizeArray<string*string*string*bigint>()
            for row in facts do
                if row[1]=Some "runtime-turn-usage" && Set.contains row[0].Value analysisIds then
                    use document = JsonDocument.Parse row[4].Value
                    let node = document.RootElement
                    for name,unit in [ "input","tokens-input";"output","tokens-output";"total","tokens-total" ] do
                        analysisCounters.Add(node.GetProperty("provider").GetString(),node.GetProperty("scope").GetString(),unit,bigint(node.GetProperty(name).GetInt64()))
            for ((provider,scope,unit),rows) in analysisCounters |> Seq.groupBy (fun (provider,scope,unit,_) -> provider,scope,unit) do
                metric "analysis-burden" unit scope provider (Some(rows |> Seq.sumBy (fun (_,_,_,amount) -> amount))) "partial" "Exact observed analyst counters are a subset of observed resources; missing turns or unresolved starts are not inferred." (null:string) (null:string)
            metric "delivered-outcomes" "outcomes" "source-delivery" "not-applicable" (Some delivered) "known" "Code delivery is distinct from native feature acceptance." (null: string) (null: string)
            let terminalInvocations = query "SELECT invocation_id FROM runtime_terminals WHERE item_id=$item;" values |> List.choose (function [ Some id ] -> Some id | _ -> None) |> Set.ofList
            let usageInvocations = query "SELECT DISTINCT invocation_id FROM runtime_turn_usage WHERE item_id=$item;" values |> List.choose (function [ Some id ] -> Some id | _ -> None) |> Set.ofList
            let epochDispatches = query "SELECT DISTINCT json_extract(j.value,'$.id') FROM efficiency_outcome_epochs e JOIN json_each(e.close_refs) j JOIN native_item_outcomes o ON o.identity=e.outcome_identity WHERE o.item_id=$item AND e.outcome_revision=o.fact_revision AND e.state='closed' AND json_extract(j.value,'$.kind')='expected-dispatch';" values |> List.choose (function [ Some id ] -> Some id | _ -> None) |> Set.ofList
            let expectedRows = query "SELECT d.dispatch_id,l.invocation_id,d.identity,d.item_id FROM expected_dispatches d LEFT JOIN invocation_lineage l ON l.item_id=d.item_id AND l.dispatch_id=d.dispatch_id WHERE d.item_id=$item ORDER BY d.dispatch_id;" values
                               |> List.filter (function [ _;_;Some id;_ ] -> Set.contains id epochDispatches | _ -> false)
            let expected = expectedRows |> List.map (fun row -> row |> List.take 2)
            let expectedSet = expected |> List.choose (function [ Some _;Some id ] -> Some id | _ -> None) |> Set.ofList
            let closedMembers = query "SELECT DISTINCT o.item_id FROM efficiency_outcome_epochs e JOIN native_item_outcomes o ON o.identity=e.outcome_identity WHERE o.item_id=$item AND e.outcome_revision=o.fact_revision AND e.state='closed';" values |> List.choose (function [ Some memberItem ] -> Some memberItem | _ -> None) |> Set.ofList
            let nativeWitnessComplete = closedMembers=Set.ofList members && not expectedRows.IsEmpty && (expectedRows |> List.forall (function
                | [ _;Some invocation;_;Some memberItem ] -> efficiencyNativeUsageComplete connection memberItem invocation
                | _ -> false))
            let nativeUsage =
                facts |> List.filter (fun row -> row[1]=Some "runtime-turn-usage")
                |> List.sumBy (fun row ->
                    use document = JsonDocument.Parse row[4].Value
                    bigint (document.RootElement.GetProperty("total").GetInt64()))
            let expectedInvocations = expected |> List.choose (function [ Some _; Some id ] -> Some id | _ -> None) |> Set.ofList
            let expectedComplete = expected.Length > 0 && expectedInvocations.Count = expected.Length && (expected |> List.forall (function [ Some _; Some _ ] -> true | _ -> false))
            let population = ProcessEfficiency.population
                                (facts |> List.filter (fun row -> row[1] = Some "native-item-outcome") |> List.map (fun row -> row[0].Value) |> Set.ofList)
                                nativeUsage
                                { ExpectedInvocations = (if expectedComplete then Some expectedInvocations else None)
                                  TerminalInvocations = Set.intersect terminalInvocations expectedSet
                                  UsageInvocations = Set.intersect usageInvocations expectedSet
                                  NativeEligible = delivered > 0I && nativeWitnessComplete && not epochDispatches.IsEmpty }
                             |> Result.defaultWith invalidOp
            groupOpenItems <- if population.NativeCompletions > 0 then 0 else 1
            for node in metrics do node.["population"].["openItems"] <- JsonValue.Create groupOpenItems
            let compatibleNativeCounters =
                facts |> List.filter (fun row -> row[1]=Some "runtime-turn-usage")
                |> List.map (fun row ->
                    use document = JsonDocument.Parse row[4].Value
                    document.RootElement.GetProperty("provider").GetRawText(), document.RootElement.GetProperty("scope").GetString())
                |> List.distinct
            let acceptedCost =
                (if compatibleNativeCounters.Length = 1 then population.WholeItemUsage else None)
                |> Option.bind (fun amount -> ProcessEfficiency.costPerAccepted amount population.NativeCompletions |> Result.defaultWith invalidOp)
            metric "cost-per-accepted" "tokens-per-accepted" "native-item" "mixed-provider-observed-tokens" None
                (if acceptedCost.IsSome then "partial" else "unknown")
                "Observed cohort resource includes unsuccessful and analyst counters. The independently witnessed native denominator does not prove that unobserved resource is zero; this quotient is a lower bound." (null: string) (null: string)
            match acceptedCost with
            | Some value ->
                metrics.[metrics.Count - 1].["value"].["numerator"] <- exactValue value.Numerator
                metrics.[metrics.Count - 1].["value"].["denominator"] <- exactValue value.Denominator
                metrics.[metrics.Count - 1].["coverage"].["population"] <- JsonValue.Create "complete"
                metrics.[metrics.Count - 1].["coverage"].["usage"] <- JsonValue.Create "partial"
                metrics.[metrics.Count - 1].["coverage"].["lineage"] <- JsonValue.Create "complete"
            | None -> ()
            // CI ordinals and provider queue intervals are a separate witnessed cohort.
            // They do not establish native item attempts, provider input equivalence or a DAG.
            let ciFacts =
                query "SELECT identity,kind,revision,canonical FROM current_ingest_facts WHERE item_id=$item AND kind IN ('ci-run','ci-job','ci-coverage','ci-binding') ORDER BY identity LIMIT 4097;" values
            if ciFacts.Length > 4096 then invalidOp "efficiency-ci-source-selection-bound"
            let ciRefs = JsonArray()
            let ciRuns = ResizeArray<string * int * bool>()
            let ciJobs = ResizeArray<string * int * string * string option * string option * string option>()
            let ciCoverage = ResizeArray<string * bool>()
            let ciBindings = ResizeArray<string * string * string * string>()
            let ciRunContexts = ResizeArray<string * int * string * string * string>()
            let ciJobTerminals = ResizeArray<bool>()
            for row in ciFacts do
                use document = JsonDocument.Parse row[3].Value
                let source = document.RootElement
                let kind = row[1].Value
                let text (name: string) = source.GetProperty(name).GetString()
                let optionalText (name: string) =
                    let value = source.GetProperty name
                    if value.ValueKind = JsonValueKind.String then Some(value.GetString()) else None
                if ciRefs.Count < 1000 then
                    // A collection coverage receipt is an operation witness, not another run.
                    let semantic = if kind="ci-coverage" || kind="ci-binding" then "operation" else kind
                    ciRefs.Add(JsonSerializer.SerializeToNode({| id=row[0].Value; kind=semantic; revision=Int64.Parse row[2].Value |}))
                if kind="ci-run" then
                    let key = text "repository" + "\n" + string(source.GetProperty("runId").GetInt64())
                    let ordinal = source.GetProperty("attempt").GetInt32()
                    ciRuns.Add(key,ordinal,text "status"="completed")
                    ciRunContexts.Add(key,ordinal,text "repository",text "head",text "workflow")
                elif kind="ci-job" then
                    ciJobTerminals.Add(text "status"="completed")
                    let key = text "repository" + "\n" + string(source.GetProperty("runId").GetInt64())
                    ciJobs.Add(key,source.GetProperty("attempt").GetInt32(),string(source.GetProperty("jobId").GetInt64()),optionalText "createdAt",optionalText "startedAt",optionalText "completedAt")
                elif kind="ci-binding" then
                    if text "binding"="exact" then ciBindings.Add(text "repository",text "head",text "workflow",text "collectionId")
                else
                    // Conflicting or partial collection witnesses do not prove completeness.
                    let complete = ["inventory";"attempts";"jobPages";"terminal";"timestamps";"lineage"] |> List.forall (fun name -> text name="complete")
                    ciCoverage.Add(text "collectionId",complete)
            let parseInterval (first: string option) (last: string option) =
                match first,last with
                | Some first,Some last ->
                    let mutable startAt = DateTimeOffset.MinValue
                    let mutable endAt = DateTimeOffset.MinValue
                    let representable (timestamp: string) =
                        let fraction = System.Text.RegularExpressions.Regex.Match(timestamp, @"\.(\d+)(?:Z|[+-]\d{2}:\d{2})$")
                        not fraction.Success || fraction.Groups[1].Value.Length<=7
                        || (fraction.Groups[1].Value |> Seq.skip 7 |> Seq.forall ((=) '0'))
                    // DateTimeOffset has 100 ns precision. Never silently round finer source clocks.
                    if representable first && representable last
                       && DateTimeOffset.TryParse(first,Globalization.CultureInfo.InvariantCulture,Globalization.DateTimeStyles.None,&startAt)
                       && DateTimeOffset.TryParse(last,Globalization.CultureInfo.InvariantCulture,Globalization.DateTimeStyles.None,&endAt)
                       && endAt>=startAt then Some(startAt.UtcTicks,endAt.UtcTicks) else None
                | _ -> None
            let jobs = ciJobs |> Seq.distinct |> Seq.toList
            let runs = ciRuns |> Seq.distinct |> Seq.toList
            let jobConflicts = jobs |> List.groupBy (fun (key,attempt,id,_,_,_) -> key,attempt,id) |> List.exists (fun (_,rows) -> rows.Length<>1)
            let runConflicts = runs |> List.groupBy (fun (key,attempt,_) -> key,attempt) |> List.exists (fun (_,rows) -> rows.Length<>1)
            let executionIntervals = jobs |> List.choose (fun (_,_,_,_,first,last) -> parseInterval first last)
            let queueIntervals = jobs |> List.choose (fun (_,_,_,first,last,_) -> parseInterval first last)
            let contexts = ciRunContexts |> Seq.distinct |> Seq.toList
            let contextConflicts = contexts |> List.groupBy (fun (key,attempt,_,_,_) -> key,attempt) |> List.exists (fun (_,rows) -> rows.Length<>1)
            let contextCovered (_,_,repository,head,workflow) =
                ciBindings |> Seq.exists (fun (boundRepository,boundHead,boundWorkflow,collection) ->
                    boundRepository=repository && boundHead=head && (boundWorkflow=workflow || boundWorkflow="*")
                    && (ciCoverage |> Seq.filter (fun (identity,_) -> identity=collection) |> Seq.map snd |> Seq.toList)= [true])
            let fullCi = not contexts.IsEmpty && (contexts |> List.forall contextCovered) && not contextConflicts && not jobConflicts && not runConflicts && ciFacts.Length<=1000
            let ciMetric (name: string) (unit: string) (amount: ProcessEfficiency.Fraction option) (status: string) (reason: string) =
                metric name unit "ci-observed" "github-actions" None status reason (null:string) (null:string)
                let node = metrics[metrics.Count-1]
                node["sourceRefs"] <- ciRefs.DeepClone()
                node.["coverage"].["population"] <- JsonValue.Create(if fullCi then "complete" else "partial")
                match amount with
                | Some (value: ProcessEfficiency.Fraction) ->
                    node.["value"].["numerator"] <- exactValue value.Numerator
                    node.["value"].["denominator"] <- exactValue value.Denominator
                | None -> ()
            let exactRatio numerator denominator = ProcessEfficiency.fraction numerator denominator |> Result.defaultWith invalidOp
            let operationRows = runs |> List.groupBy (fun (key,_,_) -> key)
            let completeOperations =
                operationRows |> List.choose (fun (key,attempts) ->
                    let ordinals = attempts |> List.map (fun (_,ordinal,_) -> ordinal) |> List.sort
                    let complete = fullCi && ordinals=[1..List.max ordinals] && (attempts |> List.forall (fun (_,_,terminal) -> terminal))
                    if complete then Some(key,ordinals) else None)
            let incidence =
                if completeOperations.IsEmpty then None
                else Some(exactRatio (bigint(completeOperations |> List.filter (fun (_,ordinals) -> ordinals.Length>1) |> List.length)) (bigint completeOperations.Length))
            let runKeys = runs |> List.map (fun (key,attempt,_) -> key,attempt) |> Set.ofList
            let jobKeys = jobs |> List.map (fun (key,attempt,_,_,_,_) -> key,attempt) |> Set.ofList
            let allAttemptsKnown = fullCi && not runs.IsEmpty && completeOperations.Length=operationRows.Length && not jobs.IsEmpty && executionIntervals.Length=jobs.Length && runKeys=jobKeys && (ciJobTerminals |> Seq.forall id)
            let observedTicks = executionIntervals |> List.sumBy (fun (first,last) -> bigint(last-first))
            let retryTicks = jobs |> List.sumBy (fun (_,attempt,_,_,first,last) -> if attempt>1 then parseInterval first last |> Option.map (fun (first,last) -> bigint(last-first)) |> Option.defaultValue 0I else 0I)
            let burden = if allAttemptsKnown && observedTicks>0I then Some(exactRatio retryTicks observedTicks) else None
            ciMetric "observed-resource" "runner-seconds" (if executionIntervals.IsEmpty || jobConflicts then None else Some(exactRatio observedTicks (bigint TimeSpan.TicksPerSecond))) (if allAttemptsKnown then "known" elif executionIntervals.IsEmpty || jobConflicts then "unknown" else "partial") "Sum of actual CI job execution intervals, including parallel jobs and later attempts. Missing observations are not zero; provider queue union is a separate metric."
            ciMetric "retry-incidence" "ratio" incidence (if incidence.IsSome then (if allAttemptsKnown then "known" else "partial") else "unknown") "Executed CI attempt ordinals only; incomplete operations are excluded, and same-input versus changed-input equivalence is not inferred from a Git head."
            ciMetric "retry-burden" "ratio" burden (if burden.IsSome then "known" else "unknown") "Exact observed CI job resource in attempts after the first divided by the complete observed CI job resource; missing jobs or attempts preserve an unknown denominator."
            let queueUnion =
                if queueIntervals.IsEmpty || jobConflicts then None
                else
                    let ordered = queueIntervals |> List.sortBy fst
                    let mutable first = fst ordered.Head
                    let mutable last = snd ordered.Head
                    let mutable ticks = 0I
                    for startAt,endAt in ordered.Tail do
                        if startAt<=last then last <- max last endAt
                        else
                            ticks <- ticks + bigint(last-first)
                            first <- startAt
                            last <- endAt
                    Some(exactRatio (ticks+bigint(last-first)) (bigint TimeSpan.TicksPerSecond))
            ciMetric "wait-time" "seconds" queueUnion (if queueUnion.IsSome then (if fullCi && queueIntervals.Length=jobs.Length then "known" else "partial") else "unknown") "Union of actual provider job created-to-started intervals, normalized to UTC with fractional ticks preserved. CI queue wait is not native item wait."
            // Full canonical acceptance and witnessed time are never derived from missing inputs.
            for name, unit in [ "retry-incidence", "ratio"; "retry-burden", "ratio"; "first-pass-delivery", "ratio"; "lead-time", "seconds"; "touch-time", "seconds"; "wait-time", "seconds"; "flow-ratio", "ratio"; "critical-path-delay", "seconds" ] do
                metric name unit "native-item" "unknown-provider" None "unknown" "Required witnessed population, operation or acceptance joins are unavailable." (null: string) (null: string)
            let sourceObservedAt =
                query "SELECT observed_at FROM operational_event_times WHERE item_id=$item AND observed_at IS NOT NULL;" values
                |> List.choose (function [ Some timestamp ] -> Some timestamp | _ -> None)
                |> List.sortByDescending (fun timestamp -> DateTimeOffset.Parse(timestamp, Globalization.CultureInfo.InvariantCulture))
                |> List.tryHead
            let ingestedAt =
                query "SELECT a.accepted_at FROM fact_acceptance_times a JOIN current_ingest_facts f ON f.identity=a.identity AND f.revision=a.fact_revision AND f.content_digest=a.content_digest WHERE f.item_id=$item;" values
                |> List.choose (function [ Some timestamp ] -> Some timestamp | _ -> None)
                |> List.sortByDescending (fun timestamp -> DateTimeOffset.Parse(timestamp, Globalization.CultureInfo.InvariantCulture))
                |> List.tryHead
            let age (dimension: string) (witnessed: string option) =
                metric "data-health" "seconds" "receiver-observed" "not-applicable" None "unknown" "No applicable witnessed clock; another clock is not substituted." (null:string) dimension
                match witnessed with
                | Some timestamp ->
                    let ticks = (DateTimeOffset.Parse(cutoff,Globalization.CultureInfo.InvariantCulture)-DateTimeOffset.Parse(timestamp,Globalization.CultureInfo.InvariantCulture)).Ticks
                    if ticks>=0L then
                        let age = ProcessEfficiency.fraction (bigint ticks) (bigint TimeSpan.TicksPerSecond) |> Result.defaultWith invalidOp
                        let value = metrics.[metrics.Count-1].["value"]
                        value["status"] <- JsonValue.Create "known"
                        value["numerator"] <- exactValue age.Numerator
                        value["denominator"] <- exactValue age.Denominator
                        value["reason"] <- JsonValue.Create "Exact age of the separately witnessed clock at this cutoff."
                | None -> ()
            age "source-age" sourceObservedAt
            age "ingestion-age" ingestedAt
            age "publication-age" None
            let unresolvedLineage = query "SELECT count(*) FROM expected_dispatches d WHERE d.item_id=$item AND (SELECT count(*) FROM invocation_lineage l WHERE l.dispatch_id=d.dispatch_id AND l.item_id=d.item_id)<>1;" values
            let pending = query "SELECT count(*) FROM efficiency_analysis_requests WHERE stable_outcome_identity IN (SELECT identity FROM native_item_outcomes WHERE item_id=$item) AND state IN ('pending','claimed');" values
            let countOf = function [ [ Some amount ] ] -> bigint(Int64.Parse amount) | _ -> invalidOp "efficiency-health-count-unavailable"
            metric "data-health" "records" "receiver-observed" "not-applicable" (Some(countOf unresolvedLineage)) "known" "Unresolved lineage in the current observed dispatch selection; absent expected populations remain unknown." (null:string) "unresolved-lineage"
            metric "data-health" "records" "receiver-observed" "not-applicable" (Some(countOf pending)) "known" "Current pending or claimed canonical requests, including claims whose invocation is not yet witnessed." (null:string) "pending-analysis"
            let producers = query "SELECT count(DISTINCT a.producer) FROM fact_admissions a JOIN current_ingest_facts f ON f.identity=a.identity WHERE f.item_id=$item;" values
            metric "data-health" "records" "receiver-observed" "not-applicable" (Some(countOf producers)) "partial" "Observed authenticated producers only; a complete expected producer roster is not inferred." (null:string) "producer-population"
            let request = query "SELECT state,requested_at,claimed_at FROM efficiency_analysis_requests WHERE stable_outcome_identity IN (SELECT identity FROM native_item_outcomes WHERE item_id=$item) ORDER BY CASE state WHEN 'claimed' THEN 0 WHEN 'pending' THEN 1 WHEN 'failed' THEN 2 WHEN 'unavailable' THEN 3 ELSE 4 END,updated_at DESC,request_id DESC LIMIT 1;" values
            let requestState, pendingSince, lastAttemptAt =
                match request with
                | [ [ Some state; requested; updated ] ] -> Some state, (if state = "pending" then requested else None), (if state = "pending" then None else updated)
                | _ -> None, None, None
            let assessment =
                let assessmentValues = ("$group",box item)::values
                match query "SELECT r.canonical FROM efficiency_records r JOIN efficiency_analysis_requests q ON json_extract(q.canonical,'$.resultAssessmentRef')=r.identity AND q.state='settled' JOIN native_item_outcomes o ON o.identity=q.stable_outcome_identity AND o.item_id=r.item_id WHERE r.item_id=$item AND json_extract(r.canonical,'$.assessment.subject.itemId')=$group AND r.kind='efficiency-assessment/1' AND json_extract(r.canonical,'$.assessment.evidenceDigest')=q.evidence_digest ORDER BY r.fact_revision DESC,r.identity DESC LIMIT 1;" assessmentValues with
                | [ [ Some canonical ] ] ->
                    let root = JsonNode.Parse canonical
                    let value = root["assessment"]
                    if (value.["provenance"].["validationResult"]).GetValue<string>() = "accepted" then value.DeepClone() else null
                | _ -> null
            let assessmentState = if isNull assessment then None else Some((assessment.["lifecycle"].["state"]).GetValue<string>())
            let state = match requestState with Some "pending" -> "pending" | Some "claimed" -> "running" | Some "failed" -> "failed" | Some "unavailable" -> "unavailable" | _ -> assessmentState |> Option.defaultValue "unavailable"
            let available = min 32 (maxMetrics - returned)
            let priority (node: JsonNode) =
                match node["metric"].GetValue<string>() with
                | "data-health" -> 0
                | "observed-resource" | "analysis-burden" | "delivered-outcomes" | "cost-per-accepted" -> 1
                | "work-mix" | "avoidable-share" -> 2
                | _ -> if node.["value"].["status"].GetValue<string>()="unknown" then 3 else 2
            let selected = metrics |> Seq.sortBy (fun node -> priority node,node["metricId"].GetValue<string>()) |> Seq.truncate available |> Seq.toArray
            returned <- returned + selected.Length
            let omittedHere = metrics.Count - selected.Length
            omitted <- omitted + omittedHere
            let exported =
                JsonSerializer.SerializeToNode
                    {| itemId = item; originalItemId = original
                       metricSelection = {| limit = 32; returned = selected.Length; omitted = omittedHere; complete = omittedHere = 0 |}
                       analysisHealth = {| state = state; requestState = requestState; assessmentState = assessmentState
                                           pendingSince = pendingSince; lastAttemptAt = lastAttemptAt
                                           failureCode = if state = "unavailable" then Some "missing-authority" else None |}
                       freshness = {| sourceObservedAt = sourceObservedAt; ingestedAt = ingestedAt |} |}
            exported["metrics"] <- JsonArray(selected |> Array.map _.DeepClone())
            exported["assessment"] <- assessment
            outputItems.Add exported
        let output =
            JsonSerializer.SerializeToNode
                {| schema = "fsgg.telemetry.efficiency-export/1"; snapshotRevision = revision
                   sourceFingerprint = fingerprint; cutoff = cutoff; observedAt = cutoff
                   selection = {| limit = maxItems; returned = outputItems.Count; omitted = max 0 (items.Length - outputItems.Count); complete = items.Length = outputItems.Count |}
                   metricSelection = {| limit = maxMetrics; returned = returned; omitted = omitted; complete = omitted = 0 |} |}
        output["items"] <- outputItems
        let encoded = output.ToJsonString() + "\n"
        if Encoding.UTF8.GetByteCount encoded > 4 * 1024 * 1024 then invalidOp "efficiency-export-byte-bound"
        encoded

    let private dashboardSnapshotBound
        compactCi
        path
        assessment
        (workspaceId: (string * (unit -> unit)) option)
        afterFirstRead
        (itemId: string option)
        (project: (SqliteConnection -> SqliteTransaction -> JsonObject -> string -> string) option)
        =
        match
            validateRoot path assessment
            |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly |> Result.map (fun value -> root, value))
        with
        | Error errors -> Error errors
        | Ok(root, (connection, _)) ->
            use connection = connection

            try
                use transaction = connection.BeginTransaction()
                let version = Int32.Parse(scalarText connection "PRAGMA user_version;")
                let journal = scalarText connection "PRAGMA journal_mode;" |> _.ToLowerInvariant()

                if version <> currentSchemaVersion then
                    Error [ "telemetry store schema requires migration; run telemetry store init" ]
                elif journal <> "wal" then
                    Error [ "telemetry store journal mode must be WAL" ]
                else
                    let projectionAuthorized =
                        match workspaceId with
                        | None -> Ok()
                        | Some(workspace, _) when not (TelemetryReceipt.validId workspace) ->
                            Error [ "unauthorized-scope" ]
                        | Some(workspace, computed) -> verifyScopedProvenance connection transaction workspace computed

                    match projectionAuthorized with
                    | Error errors -> Error errors
                    | Ok() ->
                        let selectedItems =
                            use command = connection.CreateCommand()
                            command.Transaction <- transaction

                            command.CommandText <-
                                match itemId with
                                | None ->
                                    "SELECT item_id FROM (SELECT item_id FROM current_ingest_facts WHERE item_id IS NOT NULL UNION SELECT item_id FROM budget_population_facts UNION SELECT item_id FROM native_item_outcomes) ORDER BY item_id LIMIT 202;"
                                | Some _ ->
                                    "SELECT item_id FROM (SELECT item_id,original_item_id FROM budget_population_facts WHERE item_id=$item OR original_item_id=$item UNION SELECT item_id,item_id AS original_item_id FROM current_ingest_facts WHERE item_id=$item) ORDER BY item_id LIMIT 202;"

                            itemId |> Option.iter (parameter command "$item")
                            use reader = command.ExecuteReader()

                            [|
                                while reader.Read() do
                                    yield reader.GetString 0
                            |]

                        afterFirstRead ()

                        if selectedItems.Length > 200 then
                            Error [ "dashboard snapshot exceeds 200 selected items" ]
                        elif itemId.IsSome && selectedItems.Length = 0 then
                            Error [ "unknown telemetry item" ]
                        else
                            let arrayOfStrings (values: seq<string>) =
                                JsonArray(
                                    values
                                    |> Seq.map (fun value -> JsonValue.Create(value) :> JsonNode)
                                    |> Seq.toArray
                                )

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
                                        let value: JsonNode =
                                            if reader.IsDBNull index then
                                                null
                                            else
                                                match reader.GetValue index with
                                                | :? int64 as value -> JsonValue.Create(value)
                                                | :? int as value -> JsonValue.Create(value)
                                                | :? double as value -> JsonValue.Create(value)
                                                | :? (byte array) as value ->
                                                    JsonValue.Create(Convert.ToBase64String value)
                                                | value -> JsonValue.Create(string value)

                                        row[reader.GetName index] <- value

                                    result.Add row

                                    if result.Count > 10000 then
                                        raise (InvalidOperationException("dashboard snapshot row bound exceeded"))

                                result

                            let whereItem column =
                                match itemId with
                                | None -> ""
                                | Some _ ->
                                    $" WHERE %s{column} IN (SELECT item_id FROM budget_population_facts WHERE item_id=$selected OR original_item_id=$selected UNION SELECT $selected)"

                            let itemFilter = whereItem "item_id"
                            let learningItemFilter = if itemId.IsSome then " AND item_id=$selected" else ""
                            let learningFactItemFilter = if itemId.IsSome then " AND f.item_id=$selected" else ""

                            let table name order =
                                rows ($"SELECT * FROM %s{name}%s{itemFilter} ORDER BY %s{order} LIMIT 10001;")

                            // Establish bounded completeness before writing selection.complete.
                            // LIMIT alone cannot distinguish an exact boundary from truncation.
                            use learningCount = connection.CreateCommand()
                            learningCount.Transaction <- transaction
                            learningCount.CommandText <-
                                $"SELECT count(*) FROM ingest_facts WHERE kind IN ('learn-task-snapshot','learn-context-manifest','learn-experiment-assignment','learn-accounting-inventory/1','runtime-native-inventory/1','runtime-native-inventory-source/1','learn-shared-cost/1','learn-shared-cost-allocation/1','learn-shared-cost-authority/1','learn-native-delivery-source/1','learn-installed-origin/1')%s{learningItemFilter};"
                            itemId |> Option.iter (parameter learningCount "$selected")

                            if not compactCi && Convert.ToInt64(learningCount.ExecuteScalar()) > 10000L then
                                raise (InvalidOperationException("learning observation snapshot row bound exceeded"))

                            let summaries = JsonArray()

                            for selected in selectedItems do
                                use document =
                                    JsonDocument.Parse(TelemetryStore.publicJson (readSummary connection selected))

                                summaries.Add(JsonNode.Parse(document.RootElement.GetRawText()))

                            let content = JsonObject()
                            let selectionMode = if itemId.IsSome then "item" else "all"

                            // Bind retained private snapshots to the store's actual workspace.
                            // An unprovisioned legacy store remains available for ordinary local
                            // inspection, but cannot be mistaken for an experiment input.
                            let snapshotWorkspace =
                                scalarText
                                    connection
                                    "SELECT coalesce((SELECT value FROM store_metadata WHERE key='receiptWorkspace'),'');"

                            content["workspaceId"] <- JsonValue.Create(snapshotWorkspace)
                            content["learningSnapshotSchema"] <-
                                JsonValue.Create("fsgg.telemetry.learn-item-detail/4")

                            content["selection"] <-
                                JsonSerializer.SerializeToNode
                                    {|
                                        mode = selectionMode
                                        requestedItem = itemId
                                        complete = true
                                        maxItems = 200
                                        maxRowsPerRelation = 10000
                                    |}

                            content["store"] <-
                                JsonSerializer.SerializeToNode
                                    {|
                                        schemaVersion = version
                                        journalMode = journal
                                    |}

                            content["items"] <- arrayOfStrings selectedItems
                            content["summaries"] <- summaries

                            let learningRows =
                                if compactCi then
                                    JsonArray()
                                else
                                    rows
                                        ($"SELECT o.sequence AS ingest_order,a.producer AS receipt_producer,a.stream AS receipt_stream,a.authority_role AS receipt_role,a.grant_id AS receipt_grant_id,a.grant_generation AS receipt_grant_generation,a.receipt_key,a.envelope_digest AS receipt_envelope_digest,f.identity,f.kind,f.item_id,f.revision,f.content_digest,f.canonical FROM ingest_facts f LEFT JOIN learning_fact_order o ON o.identity=f.identity LEFT JOIN fact_admissions a ON a.identity=f.identity WHERE f.kind IN ('learn-task-snapshot','learn-context-manifest','learn-experiment-assignment','learn-accounting-inventory/1','runtime-native-inventory/1','runtime-native-inventory-source/1','learn-shared-cost/1','learn-shared-cost-allocation/1','learn-shared-cost-authority/1','learn-native-delivery-source/1','learn-installed-origin/1')%s{learningFactItemFilter} ORDER BY f.item_id,f.kind,f.identity LIMIT 10001;")

                            [
                                "populations",
                                table "budget_population_facts" "item_id,fact_revision DESC,identity DESC"
                                "dirtyItems", table "budget_dirty_items" "item_id"
                                "outcomes",
                                table "native_item_outcomes" "item_id,observed_at DESC,fact_revision DESC,identity DESC"
                                "admissions", table "runtime_admissions" "item_id,invocation_id"
                                "starts", table "runtime_starts" "item_id,invocation_id,phase"
                                "terminals", table "runtime_terminals" "item_id,invocation_id"
                                "expectedDispatches", table "expected_dispatches" "item_id,dispatch_id"
                                "lineage", table "invocation_lineage" "item_id,fact_revision DESC,identity DESC"
                                "times",
                                table
                                    "operational_event_times"
                                    "item_id,invocation_id,event,fact_revision DESC,identity DESC"
                                "usage", table "runtime_turn_usage" "item_id,identity"
                                "runtimeGaps", table "runtime_gaps" "item_id,identity"
                                "ciRuns", table "ci_runs" "item_id,repository,run_id,attempt"
                                "ciJobs", (if compactCi then JsonArray() else table "ci_jobs" "item_id,repository,run_id,attempt,job_id")
                                "ciSteps", (if compactCi then JsonArray() else table "ci_steps" "item_id,repository,run_id,attempt,job_id,number")
                                "ciCoverage", (if compactCi then JsonArray() else table "ci_coverage" "item_id,rowid")
                                "ciPopulationCoverage", (if compactCi then JsonArray() else table "ci_population_coverage" "item_id,fact_revision")
                                "budgetAssessments",
                                table
                                    "budget_assessment_revisions"
                                    "item_id,dimension,provider,accounting_scope,assessment_revision"
                                "budgetMembership", table "budget_epoch_membership" "item_id"
                                "budgetEpochs", rows "SELECT * FROM budget_epochs ORDER BY ordinal LIMIT 10001;"
                                "budgetBreaches",
                                rows "SELECT * FROM budget_breaches ORDER BY epoch_id,item_id LIMIT 10001;"
                                "budgetInterventions",
                                rows "SELECT * FROM budget_interventions ORDER BY epoch_id LIMIT 10001;"
                                "activities", table "activity_spans" "item_id,started_at,activity_id"
                                "activityUsageAttributions",
                                table "activity_usage_attributions" "item_id,usage_identity"
                                "complications", table "complication_events" "item_id,occurred_at,identity"
                                "reviews", table "process_reviews" "item_id,scope,attempt_id,fact_revision"
                                "learningObservations", learningRows
                            ]
                            |> List.iter (fun (name, value) ->
                                if not compactCi || not (Set.contains name (set [ "ciJobs"; "ciSteps"; "ciCoverage"; "ciPopulationCoverage"; "learningObservations" ])) then
                                    content[name] <- value)

                            if compactCi then
                                let ciSummaries = JsonArray()
                                for selected in selectedItems do
                                    ciSummaries.Add(JsonNode.Parse(ciSummaryInSnapshot connection transaction selected))
                                content["ciSummaries"] <- ciSummaries
                                content["ciProjection"] <- JsonValue.Create("fsgg.telemetry.ci-summary/1")
                                content.Remove("learningSnapshotSchema") |> ignore

                            let canonical =
                                CanonicalJson.canonicalize (Encoding.UTF8.GetBytes(content.ToJsonString()))
                                |> Result.defaultWith invalidOp

                            let canonicalBytes = Encoding.UTF8.GetBytes canonical

                            if canonicalBytes.Length > 4 * 1024 * 1024 then
                                raise (InvalidOperationException("dashboard canonical snapshot exceeds 4194304 bytes"))

                            let revision = CanonicalJson.sha256 canonicalBytes
                            let envelope = JsonObject()
                            envelope["schema"] <- JsonValue.Create(if compactCi then "fsgg.telemetry.item-detail/3" else "fsgg.telemetry.item-detail/2")
                            envelope["workspaceId"] <- JsonValue.Create(snapshotWorkspace)
                            envelope["observedAt"] <- JsonValue.Create(DateTimeOffset.UtcNow.ToString("O"))
                            envelope["revision"] <- JsonValue.Create(revision)

                            envelope["canonicalSnapshotGzip"] <-
                                JsonValue.Create(Convert.ToBase64String(gzip canonicalBytes))

                            envelope["operational"] <-
                                match workspaceId with
                                | None ->
                                    JsonSerializer.SerializeToNode
                                        {|
                                            pendingBatches = pendingCount root
                                            consistency = "observed-outside-database-transaction"
                                        |}
                                | Some _ ->
                                    let count state =
                                        use command = connection.CreateCommand()
                                        command.Transaction <- transaction

                                        command.CommandText <-
                                            "SELECT count(*) FROM transport_receipts WHERE state=$state;"

                                        parameter command "$state" state
                                        Convert.ToInt64(command.ExecuteScalar())

                                    JsonSerializer.SerializeToNode
                                        {|
                                            pendingBatches = count "durably-received"
                                            appliedReceipts = count "applied"
                                            rejectedReceipts = count "rejected"
                                            consistency = "database-transaction"
                                        |}

                            let result =
                                match project with
                                | None -> envelope.ToJsonString(JsonSerializerOptions(WriteIndented = false)) + "\n"
                                | Some projection -> projection connection transaction content revision

                            transaction.Rollback()

                            if Encoding.UTF8.GetByteCount result > (if project.IsSome then 4 * 1024 * 1024 else 1024 * 1024) then
                                Error [ "dashboard snapshot exceeds 1048576 bytes" ]
                            else
                                Ok result
            with error ->
                Error [ error.Message ]

    let efficiencyExport path assessment expectedRevision maxItems maxMetrics =
        dashboardSnapshotBound true path assessment None ignore None
            (Some(efficiencyExportInSnapshot expectedRevision maxItems maxMetrics))

    let efficiencyAnalysisInspect path assessment requestId =
        dashboardSnapshotBound true path assessment None ignore None (Some(fun connection transaction baseSnapshot revision ->
            let record,packet,_,_,_,_ = efficiencyQueueRow connection requestId
            record["packetBase64"] <- JsonValue.Create(Convert.ToBase64String packet)
            record["rawDigest"] <- JsonValue.Create("sha256:"+CanonicalJson.sha256 packet)
            let item = (record.["canonicalRequest"].["subject"].["itemId"]).GetValue<string>()
            // Metric and analyst-record selection shares the compact base read transaction.
            use originalCommand = connection.CreateCommand()
            originalCommand.Transaction <- transaction
            originalCommand.CommandText <- "SELECT DISTINCT original_item_id FROM (SELECT original_item_id FROM budget_population_facts WHERE item_id=$item UNION SELECT original_item_id FROM efficiency_outcome_epochs WHERE effective_item_id=$item) LIMIT 2;"
            parameter originalCommand "$item" item
            let originals =
                use reader = originalCommand.ExecuteReader()
                [ while reader.Read() do yield reader.GetString 0 ]
            let original = match originals with [] -> item | [ original ] -> original | _ -> invalidOp "efficiency-original-item-lineage-conflict"
            use exportDocument = JsonDocument.Parse(efficiencyExportInSnapshot revision 200 1000 connection transaction baseSnapshot revision)
            let rows = exportDocument.RootElement.GetProperty("items").EnumerateArray() |> Seq.filter (fun row -> row.GetProperty("itemId").GetString()=original) |> Seq.toArray
            if rows.Length<>1 then invalidOp "efficiency-inspect-metrics-unavailable"
            record["metrics"] <- JsonNode.Parse(rows[0].GetProperty("metrics").GetRawText())
            record["sourceFingerprint"] <- JsonValue.Create(exportDocument.RootElement.GetProperty("sourceFingerprint").GetString())
            let analysisRecords = JsonArray()
            use command = connection.CreateCommand()
            command.Transaction <- transaction
            command.CommandText <- "SELECT f.identity,f.kind,f.revision,f.content_digest,f.canonical FROM current_ingest_facts f JOIN efficiency_analysis_requests q ON q.request_id=$request WHERE f.item_id=$item AND ((q.invocation_ref IS NOT NULL AND json_extract(f.canonical,'$.invocationId')=q.invocation_ref) OR f.identity=json_extract(q.canonical,'$.dispatchRef.id') OR (q.state='settled' AND f.identity=json_extract(q.canonical,'$.generatedReviewRef.id') AND f.revision=json_extract(q.canonical,'$.generatedReviewRef.revision'))) ORDER BY f.identity LIMIT 129;"
            parameter command "$request" requestId
            parameter command "$item" item
            let facts =
                use reader = command.ExecuteReader()
                [ while reader.Read() do yield reader.GetString 0,reader.GetString 1,reader.GetInt64 2,reader.GetString 3,reader.GetString 4 ]
            if facts.Length>128 then invalidOp "efficiency-analysis-record-selection-bound"
            for id,kind,revision,digest,canonical in facts do
                if Encoding.UTF8.GetByteCount canonical>16384 then invalidOp "efficiency-analysis-record-byte-bound"
                match EfficiencyEvidence.semanticKind kind with
                | Some semantic ->
                    let row = JsonObject()
                    row["ref"] <- JsonSerializer.SerializeToNode({|id=id;kind=semantic;revision=revision|})
                    row["canonicalRef"] <- JsonSerializer.SerializeToNode({|id=id;kind=kind;revision=revision;contentDigest="sha256:"+digest|})
                    row["itemId"] <- JsonValue.Create item
                    row["payload"] <- JsonNode.Parse canonical
                    row["priority"] <- JsonValue.Create "other"
                    row["analysisGenerated"] <- JsonValue.Create true
                    analysisRecords.Add row
                | None -> invalidOp "efficiency-analysis-record-kind-unavailable"
            record["analysisRecords"] <- analysisRecords
            efficiencyQueueResult record))

    let dashboardSnapshotWithHooks path assessment (hooks: DashboardSnapshotHooks) itemId =
        dashboardSnapshotBound false path assessment None hooks.AfterFirstRead itemId None

    let dashboardSnapshot path assessment itemId =
        dashboardSnapshotWithHooks path assessment ({ AfterFirstRead = ignore }: DashboardSnapshotHooks) itemId

    let compactDashboardSnapshotWithHooks path assessment (hooks: DashboardSnapshotHooks) itemId =
        dashboardSnapshotBound true path assessment None hooks.AfterFirstRead itemId None

    let compactDashboardSnapshot path assessment itemId =
        compactDashboardSnapshotWithHooks path assessment ({ AfterFirstRead = ignore }: DashboardSnapshotHooks) itemId

    let scopedDashboardSnapshotWithHooks path assessment workspaceId (hooks: ScopedDashboardSnapshotHooks) itemId =
        dashboardSnapshotBound false path assessment (Some(workspaceId, hooks.ReceiptKeyComputed)) hooks.AfterFirstRead itemId None

    let scopedDashboardSnapshot path assessment workspaceId itemId =
        scopedDashboardSnapshotWithHooks
            path
            assessment
            workspaceId
            {
                AfterFirstRead = ignore
                ReceiptKeyComputed = ignore
            }
            itemId

    type private ActivationRow =
        {
            Id: string
            Runtime: string
            ActivatedAt: DateTimeOffset
            Clock: string
            LateAfterSeconds: int64
        }

    type private DispatchRow =
        {
            Id: string
            ActivationId: string
            Relation: string
            ParentId: string option
            Runtime: string
            ExpectedAt: DateTimeOffset
            Clock: string
        }

    type private LineageRow =
        {
            DispatchId: string
            InvocationId: string
            Relation: string
            ParentInvocationId: string option
            RootInvocationId: string
            Runtime: string
        }

    type private TimeRow =
        {
            InvocationId: string
            Event: string
            OccurredAt: DateTimeOffset option
            OccurredClock: string option
            ObservedAt: DateTimeOffset option
            ObservedClock: string option
        }

    type private ReconciliationRow =
        {
            DispatchId: string
            InvocationId: string option
            LineageStatus: string
            LineageCode: string
            TimingStatus: string
            TimingCode: string
        }

    let reconcile path assessment (itemId: string) =
        match
            validateRoot path assessment
            |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly)
        with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection

            try
                let version = Int32.Parse(scalarText connection "PRAGMA user_version;")

                if version <> currentSchemaVersion then
                    Error [ "telemetry store schema requires migration; run telemetry store init" ]
                else
                    let readStringOption (reader: SqliteDataReader) index =
                        if reader.IsDBNull index then
                            None
                        else
                            Some(reader.GetString index)

                    let activations =
                        use command = connection.CreateCommand()

                        command.CommandText <-
                            "SELECT activation_id,runtime,activated_at,clock_provenance,late_after_seconds FROM operational_activations WHERE item_id=$item;"

                        parameter command "$item" itemId
                        use reader = command.ExecuteReader()

                        [
                            while reader.Read() do
                                yield
                                    {
                                        Id = reader.GetString 0
                                        Runtime = reader.GetString 1
                                        ActivatedAt = DateTimeOffset.Parse(reader.GetString 2)
                                        Clock = reader.GetString 3
                                        LateAfterSeconds = reader.GetInt64 4
                                    }
                        ]
                        |> List.map (fun row -> row.Id, row)
                        |> Map.ofList

                    let dispatches =
                        use command = connection.CreateCommand()

                        command.CommandText <-
                            "SELECT dispatch_id,activation_id,relation,parent_dispatch_id,runtime,expected_at,clock_provenance FROM expected_dispatches WHERE item_id=$item ORDER BY dispatch_id;"

                        parameter command "$item" itemId
                        use reader = command.ExecuteReader()

                        [
                            while reader.Read() do
                                yield
                                    {
                                        Id = reader.GetString 0
                                        ActivationId = reader.GetString 1
                                        Relation = reader.GetString 2
                                        ParentId = readStringOption reader 3
                                        Runtime = reader.GetString 4
                                        ExpectedAt = DateTimeOffset.Parse(reader.GetString 5)
                                        Clock = reader.GetString 6
                                    }
                        ]

                    let byDispatch = dispatches |> List.map (fun row -> row.Id, row) |> Map.ofList

                    let lineages =
                        use command = connection.CreateCommand()

                        command.CommandText <-
                            "SELECT dispatch_id,invocation_id,relation,parent_invocation_id,root_invocation_id,runtime FROM invocation_lineage WHERE item_id=$item ORDER BY rowid;"

                        parameter command "$item" itemId
                        use reader = command.ExecuteReader()

                        [
                            while reader.Read() do
                                yield
                                    {
                                        DispatchId = reader.GetString 0
                                        InvocationId = reader.GetString 1
                                        Relation = reader.GetString 2
                                        ParentInvocationId = readStringOption reader 3
                                        RootInvocationId = reader.GetString 4
                                        Runtime = reader.GetString 5
                                    }
                        ]

                    let byLineage = lineages |> List.groupBy _.DispatchId |> Map.ofList
                    let invocationMultiplicity = lineages |> List.countBy _.InvocationId |> Map.ofList

                    let times =
                        use command = connection.CreateCommand()

                        command.CommandText <-
                            "SELECT invocation_id,event,occurred_at,occurred_clock_provenance,observed_at,observed_clock_provenance FROM operational_event_times WHERE item_id=$item ORDER BY rowid;"

                        parameter command "$item" itemId
                        use reader = command.ExecuteReader()

                        [
                            while reader.Read() do
                                yield
                                    {
                                        InvocationId = reader.GetString 0
                                        Event = reader.GetString 1
                                        OccurredAt = readStringOption reader 2 |> Option.map DateTimeOffset.Parse
                                        OccurredClock = readStringOption reader 3
                                        ObservedAt = readStringOption reader 4 |> Option.map DateTimeOffset.Parse
                                        ObservedClock = readStringOption reader 5
                                    }
                        ]
                        |> List.groupBy _.InvocationId
                        |> Map.ofList

                    let cyclic dispatch =
                        let rec loop seen current =
                            if Set.contains current seen then
                                true
                            else
                                match Map.tryFind current byDispatch |> Option.bind _.ParentId with
                                | None -> false
                                | Some parent -> loop (Set.add current seen) parent

                        loop Set.empty dispatch.Id

                    let oneLineage id =
                        Map.tryFind id byLineage
                        |> Option.bind (function
                            | [ value ] -> Some value
                            | _ -> None)

                    let reconcileOne dispatch =
                        let rows = Map.tryFind dispatch.Id byLineage |> Option.defaultValue []
                        let invocation = rows |> List.tryHead |> Option.map _.InvocationId

                        let result lineageStatus lineageCode timingStatus timingCode =
                            {
                                DispatchId = dispatch.Id
                                InvocationId = invocation
                                LineageStatus = lineageStatus
                                LineageCode = lineageCode
                                TimingStatus = timingStatus
                                TimingCode = timingCode
                            }

                        let timing (activation: ActivationRow) (lineage: LineageRow) =
                            let eventRows = Map.tryFind lineage.InvocationId times |> Option.defaultValue []
                            let grouped = eventRows |> List.groupBy _.Event |> Map.ofList
                            let required = [ "admission"; "start"; "terminal" ]

                            if
                                required
                                |> List.exists (fun event ->
                                    Map.tryFind event grouped |> Option.exists (fun rows -> List.length rows > 1))
                            then
                                "invalid", "event-time-conflict"
                            elif required |> List.exists (fun event -> not (Map.containsKey event grouped)) then
                                "missing", "required-event-missing"
                            else
                                let witnesses =
                                    required |> List.map (fun event -> Map.find event grouped |> List.exactlyOne)

                                if
                                    witnesses
                                    |> List.exists (fun row -> row.OccurredAt.IsNone || row.ObservedAt.IsNone)
                                then
                                    "missing", "timestamps-missing"
                                elif
                                    witnesses
                                    |> List.exists (fun row -> row.OccurredClock.IsNone || row.ObservedClock.IsNone)
                                then
                                    "missing", "clock-provenance-missing"
                                elif witnesses |> List.exists (fun row -> row.OccurredClock <> row.ObservedClock) then
                                    "invalid", "clock-domain-mismatch"
                                elif witnesses |> List.choose _.OccurredClock |> Set.ofList |> Set.count <> 1 then
                                    "invalid", "lifecycle-clock-domain-mismatch"
                                else
                                    let complete =
                                        witnesses |> List.map (fun row -> row.OccurredAt.Value, row.ObservedAt.Value)

                                    if complete |> List.exists (fun (occurred, observed) -> observed < occurred) then
                                        "invalid", "event-time-reversed"
                                    elif
                                        complete
                                        |> List.map fst
                                        |> List.pairwise
                                        |> List.exists (fun (earlier, later) -> later < earlier)
                                    then
                                        "invalid", "lifecycle-occurrence-order-invalid"
                                    elif
                                        complete
                                        |> List.map snd
                                        |> List.pairwise
                                        |> List.exists (fun (earlier, later) -> later < earlier)
                                    then
                                        "invalid", "lifecycle-observation-order-invalid"
                                    elif
                                        complete
                                        |> List.exists (fun (occurred, observed) ->
                                            (observed - occurred).TotalSeconds > float activation.LateAfterSeconds)
                                    then
                                        "late", "observation-late"
                                    else
                                        "complete", "required-events-complete"

                        match Map.tryFind dispatch.ActivationId activations with
                        | None -> result "unknown" "activation-missing" "not-evaluated" "lineage-unavailable"
                        | Some activation when dispatch.Runtime <> "codex-exec" || activation.Runtime <> "codex-exec" ->
                            result "unsupported" "runtime-unsupported" "not-evaluated" "runtime-unsupported"
                        | Some activation when dispatch.Clock <> activation.Clock ->
                            result "unknown" "activation-clock-domain-mismatch" "not-evaluated" "dispatch-scope-unknown"
                        | Some activation when dispatch.ExpectedAt < activation.ActivatedAt ->
                            result "out-of-scope" "dispatch-predates-activation" "not-evaluated" "dispatch-out-of-scope"
                        | Some _ when cyclic dispatch ->
                            result "invalid" "cyclic-lineage" "not-evaluated" "lineage-invalid"
                        | Some _ when List.length rows > 1 ->
                            result "invalid" "conflicting-identity" "not-evaluated" "lineage-invalid"
                        | Some _ when rows.IsEmpty ->
                            result "unknown" "invocation-missing" "not-evaluated" "lineage-unavailable"
                        | Some activation ->
                            let lineage = List.head rows

                            let duplicateInvocation =
                                Map.tryFind lineage.InvocationId invocationMultiplicity |> Option.defaultValue 0 > 1

                            if
                                duplicateInvocation
                                || lineage.Runtime <> dispatch.Runtime
                                || lineage.Relation <> dispatch.Relation
                            then
                                result "invalid" "conflicting-identity" "not-evaluated" "lineage-invalid"
                            else
                                let parentProblem =
                                    match dispatch.Relation, dispatch.ParentId, lineage.ParentInvocationId with
                                    | "root", None, None when lineage.RootInvocationId = lineage.InvocationId -> None
                                    | "root", _, _ -> Some "conflicting-identity"
                                    | ("child" | "follow-up"), Some parentDispatch, Some parentInvocation ->
                                        match oneLineage parentDispatch with
                                        | None -> Some "missing-parent"
                                        | Some parent when
                                            parent.InvocationId <> parentInvocation
                                            || parent.RootInvocationId <> lineage.RootInvocationId
                                            ->
                                            Some "conflicting-identity"
                                        | Some _ -> None
                                    | _ -> Some "missing-parent"

                                match parentProblem with
                                | Some code ->
                                    result
                                        (if code = "missing-parent" then "unknown" else "invalid")
                                        code
                                        "not-evaluated"
                                        "lineage-unavailable"
                                | None ->
                                    let timingStatus, timingCode = timing activation lineage
                                    result "matched" "expected-invocation-match" timingStatus timingCode

                    let rows = dispatches |> List.map reconcileOne

                    let lineageCount status =
                        rows |> List.filter (fun row -> row.LineageStatus = status) |> List.length

                    let timingCount status =
                        rows |> List.filter (fun row -> row.TimingStatus = status) |> List.length

                    Ok(
                        JsonSerializer.Serialize
                            {|
                                schema = "fsgg.telemetry.operational-reconciliation/1"
                                item = itemId
                                scope = "explicit-future-dispatches"
                                historicalSessionDiscovery = false
                                supportedRuntimes = [| "codex-exec" |]
                                expected = rows.Length
                                lineageCoverage =
                                    {|
                                        matched = lineageCount "matched"
                                        unknown = lineageCount "unknown"
                                        invalid = lineageCount "invalid"
                                        unsupported = lineageCount "unsupported"
                                        outOfScope = lineageCount "out-of-scope"
                                    |}
                                timingCoverage =
                                    {|
                                        complete = timingCount "complete"
                                        late = timingCount "late"
                                        missing = timingCount "missing"
                                        invalid = timingCount "invalid"
                                        notEvaluated = timingCount "not-evaluated"
                                    |}
                                usageCoverage = "not-evaluated"
                                terminalOutcomeCoverage = "not-evaluated"
                                reconciliations =
                                    rows
                                    |> List.map (fun row ->
                                        {|
                                            dispatchId = row.DispatchId
                                            invocationId = row.InvocationId
                                            lineage =
                                                {|
                                                    status = row.LineageStatus
                                                    code = row.LineageCode
                                                |}
                                            timing =
                                                {|
                                                    status = row.TimingStatus
                                                    code = row.TimingCode
                                                |}
                                        |})
                                    |> List.toArray
                            |}
                        + "\n"
                    )
            with error ->
                Error [ error.Message ]

    // All discovery and apply validation share this exact closure; callers cannot
    // add a target or broaden ownership merely by editing a private plan.
    let private discoverCiCorrection (connection: SqliteConnection) (request: TelemetryCi.CorrectionRequest) : TelemetryCi.CorrectionPlan =
        let query sql values =
            let command = connection.CreateCommand()
            command.CommandText <- sql
            values |> List.iter (fun (name, value) -> parameter command name value)
            command
        let deliveryValues =
            [ "$item", box request.Prior.ItemId; "$repo", box request.Repository; "$pr", box request.PullRequest
              "$baseRef", box request.BaseRef; "$baseSha", box request.BaseSha; "$head", box request.Head; "$merge", box request.MergeCommit ]
        let identities sql values =
            use command = query sql values
            use reader = command.ExecuteReader()
            let found = [ while reader.Read() do yield reader.GetString 0 ]
            if found.Length > 4096 then invalidOp "ci-correction-closure-exceeds-bound"
            found
        let count sql values =
            use command = query sql values
            Convert.ToInt64(command.ExecuteScalar())
        let outcomes =
            identities "SELECT identity FROM native_item_outcomes WHERE item_id=$item AND repository=$repo AND pr_number=$pr AND base_ref=$baseRef AND base_sha=$baseSha AND head=$head AND merge_commit=$merge AND code_delivery='delivered' ORDER BY identity LIMIT 4097;" deliveryValues
        if count "SELECT count(*) FROM native_item_outcomes WHERE repository=$repo AND pr_number=$pr AND base_ref=$baseRef AND base_sha=$baseSha AND head=$head;" deliveryValues <> 1L then
            invalidOp "ci-correction-candidate-ambiguous"
        let outcome =
            match outcomes with
            | [ identity ] -> identity
            | [] -> invalidOp "ci-correction-target-unavailable"
            | _ -> invalidOp "ci-correction-target-ambiguous"
        use predecessor = query "SELECT correction_id FROM ci_effective_attribution WHERE identity=$identity;" [ "$identity", box outcome ]
        let retained = predecessor.ExecuteScalar()
        let active = if isNull retained || retained = box DBNull.Value then None else Some(string retained)
        if active <> request.ExpectedPredecessor then invalidOp "ci-correction-predecessor-conflict"
        match active with
        | Some correction ->
            use prior = query "SELECT plan FROM ci_attribution_corrections WHERE correction_id=$id;" [ "$id", box correction ]
            let plan = Encoding.UTF8.GetBytes(string (prior.ExecuteScalar())) |> TelemetryCi.parseCorrectionPlan
            match plan with
            | Ok priorPlan when priorPlan.Request.Effective = request.Prior -> ()
            | _ -> invalidOp "ci-correction-prior-assignment-conflict"
        | None -> ()
        let admissions =
            identities "SELECT identity FROM ci_population_admissions WHERE item_id=$item AND repository=$repo AND pr_number=$pr AND base_ref=$baseRef AND base_sha=$baseSha AND head=$head ORDER BY identity LIMIT 4097;" deliveryValues
        let bindings =
            identities "SELECT identity FROM ci_bindings WHERE item_id=$item AND repository=$repo AND pr_number=$pr AND head=$head ORDER BY identity LIMIT 4097;" deliveryValues
        let targets = ResizeArray<string * string>()
        targets.Add("native_item_outcomes", outcome)
        match admissions, bindings with
        | [], [] ->
            // No population is invented; feature/attempt provenance remains the
            // explicit operator evidence in the correction request.
            if count "SELECT count(*) FROM ci_runs WHERE item_id=$item AND repository=$repo AND head=$head;" deliveryValues <> 0L then
                invalidOp "ci-correction-unbound-population"
        | [ admission ], [ binding ] ->
            use assignment = query "SELECT collection_id,feature_id,attempt_id,parent_attempt_id,producer_stream FROM ci_bindings WHERE identity=$identity;" [ "$identity", box binding ]
            use reader = assignment.ExecuteReader()
            if not (reader.Read()) then invalidOp "ci-correction-binding-unavailable"
            let collection = reader.GetString 0
            let parent = if reader.IsDBNull 3 then None else Some(reader.GetString 3)
            if reader.GetString 1 <> request.Prior.FeatureId || reader.GetString 2 <> request.Prior.AttemptId
               || parent <> request.Prior.ParentAttemptId || reader.GetString 4 <> request.Prior.ProducerStream then
                invalidOp "ci-correction-prior-assignment-conflict"
            reader.Close()
            let collectionValues = [ "$collection", box collection; "$item", box request.Prior.ItemId; "$repo", box request.Repository; "$head", box request.Head ]
            if count "SELECT count(*) FROM ci_population_admissions WHERE identity=$identity AND collection_id=$collection;" [ "$identity", box admission; "$collection", box collection ] <> 1L then
                invalidOp "ci-correction-collection-mismatch"
            // Runs are shared native identities. Multiple delivery candidates or
            // collections for this head make ownership unknowable and refuse.
            if count "SELECT count(*) FROM ci_bindings WHERE repository=$repo AND head=$head;" collectionValues <> 1L
               || count "SELECT count(*) FROM native_item_outcomes WHERE repository=$repo AND head=$head;" collectionValues <> 1L then
                invalidOp "ci-correction-shared-population"
            targets.Add("ci_bindings", binding)
            targets.Add("ci_population_admissions", admission)
            for table in [ "ci_pages"; "ci_coverage"; "ci_population_coverage" ] do
                for identity in identities ($"SELECT identity FROM %s{table} WHERE collection_id=$collection ORDER BY identity LIMIT 4097;") collectionValues do
                    targets.Add(table, identity)
            for identity in identities "SELECT identity FROM ci_runs WHERE repository=$repo AND head=$head ORDER BY identity LIMIT 4097;" collectionValues do
                targets.Add("ci_runs", identity)
            for table in [ "ci_jobs"; "ci_steps" ] do
                for identity in identities ($"SELECT j.identity FROM %s{table} j JOIN ci_runs r ON r.repository=j.repository AND r.run_id=j.run_id AND r.attempt=j.attempt WHERE r.repository=$repo AND r.head=$head ORDER BY j.identity LIMIT 4097;") collectionValues do
                    targets.Add(table, identity)
            let checks = identities "SELECT identity FROM ci_check_runs WHERE item_id=$item AND repository=$repo ORDER BY identity LIMIT 4097;" collectionValues
            if not checks.IsEmpty &&
               (count "SELECT count(*) FROM ci_bindings WHERE item_id=$item AND repository=$repo;" collectionValues <> 1L
                || count "SELECT count(*) FROM native_item_outcomes WHERE item_id=$item AND repository=$repo;" collectionValues <> 1L) then
                invalidOp "ci-correction-check-ownership-ambiguous"
            for identity in checks do targets.Add("ci_check_runs", identity)
        | _ -> invalidOp "ci-correction-population-ambiguous"
        if targets.Count > 4096 then invalidOp "ci-correction-closure-exceeds-bound"
        let bound : TelemetryCi.CorrectionTarget list =
            [ for table, identity in targets do
                use fact = query ($"SELECT f.revision,f.content_digest,f.canonical,t.item_id FROM ingest_facts f JOIN %s{table} t ON t.identity=f.identity WHERE f.identity=$identity;") [ "$identity", box identity ]
                use reader = fact.ExecuteReader()
                if not (reader.Read()) then invalidOp "ci-correction-evidence-unavailable"
                let revision, digest, canonical, item = reader.GetInt64 0, reader.GetString 1, reader.GetString 2, reader.GetString 3
                if item <> request.Prior.ItemId || CanonicalJson.sha256(Encoding.UTF8.GetBytes canonical) <> digest then
                    invalidOp "ci-correction-evidence-conflict"
                if table = "native_item_outcomes" then
                    use original = JsonDocument.Parse canonical
                    let fact = original.RootElement
                    if fact.GetProperty("repository").GetString() <> request.Repository
                       || fact.GetProperty("prNumber").GetInt64() <> request.PullRequest
                       || fact.GetProperty("baseRef").GetString() <> request.BaseRef
                       || fact.GetProperty("baseSha").GetString() <> request.BaseSha
                       || fact.GetProperty("head").GetString() <> request.Head
                       || fact.GetProperty("mergeCommit").GetString() <> request.MergeCommit
                       || fact.GetProperty("codeDelivery").GetString() <> "delivered" then
                        invalidOp "ci-correction-immutable-delivery-conflict"
                yield ({ Table = table; Identity = identity; Revision = revision; Digest = digest } : TelemetryCi.CorrectionTarget) ]
            |> List.sortBy (fun target -> target.Table, target.Identity)
        { StoreId = scalarText connection "SELECT value FROM store_metadata WHERE key='ciCorrectionStoreId';"
          Request = request; Targets = bound }

    let ciCorrectionPlan path assessment request =
        match validateRoot path assessment |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly) with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection
            try
                use snapshot = connection.BeginTransaction()
                if scalarText connection "PRAGMA user_version;" <> string currentSchemaVersion then
                    Error [ "unsupported-version" ]
                else
                    let plan = discoverCiCorrection connection request
                    Ok(TelemetryCi.correctionPlanJson plan + "\n")
            with error -> Error [ error.Message ]

    let ciCorrectWithHook path assessment planBytes beforeCommit =
        match validateRoot path assessment, TelemetryCi.parseCorrectionPlan planBytes with
        | Error errors, _ | _, Error errors -> Error errors
        | Ok root, Ok plan ->
            match tryWriterLock root with
            | Error errors -> Error errors
            | Ok writer ->
                use writer = writer
                match connect root SqliteOpenMode.ReadWrite with
                | Error errors -> Error errors
                | Ok(connection, _) ->
                    use connection = connection
                    try
                        if scalarText connection "PRAGMA user_version;" <> string currentSchemaVersion then
                            Error [ "unsupported-version" ]
                        else
                            beginImmediate connection
                            try
                                let request = plan.Request
                                let digest = CanonicalJson.sha256 planBytes
                                if scalarText connection "SELECT value FROM store_metadata WHERE key='ciCorrectionStoreId';" <> plan.StoreId then
                                    invalidOp "ci-correction-cross-store-conflict"
                                use replay = connection.CreateCommand()
                                replay.CommandText <- "SELECT plan_digest FROM ci_attribution_corrections WHERE correction_id=$id;"
                                parameter replay "$id" request.CorrectionId
                                let existing = replay.ExecuteScalar()
                                let already = not (isNull existing) && existing <> box DBNull.Value
                                if already && string existing <> digest then invalidOp "ci-correction-identity-content-conflict"
                                if not already then
                                    let actual = discoverCiCorrection connection request
                                    if actual <> plan then invalidOp "ci-correction-stale-plan"
                                    let outcome = plan.Targets |> List.find (fun target -> target.Table = "native_item_outcomes")
                                    use ledger = connection.CreateCommand()
                                    ledger.CommandText <- "INSERT INTO ci_attribution_corrections VALUES($id,$digest,$plan,$outcome,$predecessor,$repo,$pr,$baseRef,$baseSha,$head,$merge,$prior,$effective,$feature,$attempt,$observed,$applied);"
                                    [ "$id", box request.CorrectionId; "$digest", box digest; "$plan", box (Encoding.UTF8.GetString planBytes)
                                      "$outcome", box outcome.Identity; "$predecessor", request.ExpectedPredecessor |> Option.map box |> Option.defaultValue DBNull.Value
                                      "$repo", box request.Repository; "$pr", box request.PullRequest; "$baseRef", box request.BaseRef; "$baseSha", box request.BaseSha
                                      "$head", box request.Head; "$merge", box request.MergeCommit; "$prior", box request.Prior.ItemId
                                      "$effective", box request.Effective.ItemId; "$feature", box request.Effective.FeatureId; "$attempt", box request.Effective.AttemptId
                                      "$observed", box request.ObservedAt; "$applied", box (DateTimeOffset.UtcNow.ToString("O")) ]
                                    |> List.iter (fun (name, value) -> parameter ledger name value)
                                    ledger.ExecuteNonQuery() |> ignore
                                    for target in plan.Targets do
                                        use evidence = connection.CreateCommand()
                                        evidence.CommandText <- "INSERT INTO ci_correction_evidence SELECT $id,identity,$table,revision,content_digest,canonical FROM ingest_facts WHERE identity=$identity; INSERT INTO ci_effective_attribution(identity,correction_id) VALUES($identity,$id) ON CONFLICT(identity) DO UPDATE SET correction_id=excluded.correction_id;"
                                        [ "$id", box request.CorrectionId; "$table", box target.Table; "$identity", box target.Identity ]
                                        |> List.iter (fun (name, value) -> parameter evidence name value)
                                        evidence.ExecuteNonQuery() |> ignore
                                        use effective = connection.CreateCommand()
                                        effective.CommandText <-
                                            if target.Table = "ci_bindings" then
                                                "UPDATE ci_bindings SET item_id=$item,feature_id=$feature,attempt_id=$attempt,parent_attempt_id=$parent,producer_stream=$producer WHERE identity=$identity;"
                                            else $"UPDATE %s{target.Table} SET item_id=$item WHERE identity=$identity;"
                                        [ "$item", box request.Effective.ItemId; "$identity", box target.Identity
                                          "$feature", box request.Effective.FeatureId; "$attempt", box request.Effective.AttemptId
                                          "$parent", request.Effective.ParentAttemptId |> Option.map box |> Option.defaultValue DBNull.Value
                                          "$producer", box request.Effective.ProducerStream ]
                                        |> List.iter (fun (name, value) -> parameter effective name value)
                                        if effective.ExecuteNonQuery() <> 1 then invalidOp "ci-correction-effective-target-conflict"
                                    let items = [ request.Prior.ItemId; request.Effective.ItemId ] |> List.distinct
                                    budgetReevaluateFor connection (Some items) false
                                    beforeCommit()
                                execute connection "COMMIT;"
                                Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.ci-correction-result/1"; correctionId = request.CorrectionId; status = (if already then "already-applied" else "applied"); targets = plan.Targets.Length |} + "\n")
                            with error ->
                                rollback connection
                                Error [ error.Message ]
                    with error -> Error [ error.Message ]

    let ciCorrect path assessment planBytes = ciCorrectWithHook path assessment planBytes ignore

    let ciCorrectionHistory path assessment correctionId =
        if not (TelemetryReceipt.validId correctionId) then Error [ "invalid-request" ]
        else
            match validateRoot path assessment |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly) with
            | Error errors -> Error errors
            | Ok(connection, _) ->
                use connection = connection
                try
                    use snapshot = connection.BeginTransaction()
                    if scalarText connection "PRAGMA user_version;" <> string currentSchemaVersion then Error [ "unsupported-version" ]
                    else
                        use command = connection.CreateCommand()
                        command.CommandText <- "SELECT plan,plan_digest,outcome_identity FROM ci_attribution_corrections WHERE correction_id=$id;"
                        parameter command "$id" correctionId
                        use reader = command.ExecuteReader()
                        if not (reader.Read()) then Error [ "ci-correction-history-unavailable" ]
                        else
                            let plan, digest, outcome = reader.GetString 0, reader.GetString 1, reader.GetString 2
                            reader.Close()
                            use chain = connection.CreateCommand()
                            chain.CommandText <- "SELECT plan,plan_digest,applied_at,correction_id=(SELECT correction_id FROM ci_effective_attribution WHERE identity=$outcome) FROM ci_attribution_corrections WHERE outcome_identity=$outcome ORDER BY rowid LIMIT 65;"
                            parameter chain "$outcome" outcome
                            use rows = chain.ExecuteReader()
                            let history =
                                [ while rows.Read() do
                                    yield {| plan = JsonSerializer.Deserialize<JsonElement>(rows.GetString 0); planSha256 = rows.GetString 1; appliedAt = rows.GetString 2; active = rows.GetInt64 3 = 1L |} ]
                            if history.Length > 64 then Error [ "ci-correction-history-exceeds-bound" ]
                            else
                                rows.Close()
                                use evidence = connection.CreateCommand()
                                evidence.CommandText <- "SELECT identity,table_name,revision,digest,canonical FROM ci_correction_evidence WHERE correction_id=$id ORDER BY table_name,identity LIMIT 4097;"
                                parameter evidence "$id" correctionId
                                use targets = evidence.ExecuteReader()
                                let mutable evidenceBytes = Encoding.UTF8.GetByteCount plan
                                let originals =
                                    [ while targets.Read() do
                                        evidenceBytes <- evidenceBytes + Encoding.UTF8.GetByteCount(targets.GetString 4)
                                        if evidenceBytes > 1048576 then invalidOp "ci-correction-history-exceeds-bound"
                                        yield {| identity = targets.GetString 0; table = targets.GetString 1; revision = targets.GetInt64 2; digest = targets.GetString 3; canonical = JsonSerializer.Deserialize<JsonElement>(targets.GetString 4) |} ]
                                let result = JsonSerializer.Serialize {| schema = "fsgg.telemetry.ci-correction-history/1"; counting = false; requestedPlan = JsonSerializer.Deserialize<JsonElement> plan; planSha256 = digest; chain = history; originalEvidence = originals |} + "\n"
                                if originals.Length > 4096 || Encoding.UTF8.GetByteCount result > 1048576 then Error [ "ci-correction-history-exceeds-bound" ]
                                else Ok result
                with error -> Error [ error.Message ]

    let ciSummary path assessment (itemId: string) =
        match validateRoot path assessment |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly) with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection
            try
                use transaction = connection.BeginTransaction()
                if scalarText connection "PRAGMA user_version;" <> string currentSchemaVersion then
                    invalidOp "unsupported-version"
                Ok(ciSummaryInSnapshot connection transaction itemId)
            with error -> Error [ error.Message ]

    let ciPopulationAdmissionExists
        path
        assessment
        (itemId: string)
        (repository: string)
        (pullRequest: int)
        (baseRef: string)
        (baseSha: string)
        (head: string)
        =
        match
            validateRoot path assessment
            |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly)
        with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection

            try
                if
                    Int32.Parse(scalarText connection "PRAGMA user_version;")
                    <> currentSchemaVersion
                then
                    Error [ "telemetry store schema requires migration; run telemetry store init" ]
                else
                    use command = connection.CreateCommand()

                    command.CommandText <-
                        "SELECT count(*) FROM ci_population_admissions WHERE item_id=$item AND repository=$repo AND pr_number=$pr AND base_ref=$baseRef AND base_sha=$baseSha AND head=$head AND witness='native-pr-head';"

                    parameter command "$item" itemId
                    parameter command "$repo" repository
                    parameter command "$pr" pullRequest
                    parameter command "$baseRef" baseRef
                    parameter command "$baseSha" baseSha
                    parameter command "$head" head
                    Ok(Convert.ToInt64(command.ExecuteScalar()) = 1L)
            with error ->
                Error [ error.Message ]

    let budgetSummary path assessment (itemId: string) =
        match
            validateRoot path assessment
            |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly)
        with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection

            try
                use snapshot = connection.BeginTransaction()
                let version = Int32.Parse(scalarText connection "PRAGMA user_version;")

                if version <> currentSchemaVersion then
                    Error [ "telemetry store schema requires migration; run telemetry store init" ]
                else
                    use command = connection.CreateCommand()

                    command.CommandText <-
                        "SELECT dimension,provider,accounting_scope,verdict,numerator,denominator,severe,reason,epoch_id FROM budget_assessment_revisions a WHERE item_id=$item AND assessment_revision=(SELECT max(assessment_revision) FROM budget_assessment_revisions b WHERE b.item_id=a.item_id AND b.dimension=a.dimension AND b.provider=a.provider AND b.accounting_scope=a.accounting_scope) ORDER BY dimension,provider,accounting_scope;"

                    parameter command "$item" itemId
                    use reader = command.ExecuteReader()
                    let dimensions = ResizeArray<_>()

                    while reader.Read() do
                        dimensions.Add(
                            {|
                                dimension = reader.GetString 0
                                provider = reader.GetString 1
                                accountingScope = reader.GetString 2
                                verdict = reader.GetString 3
                                numerator = if reader.IsDBNull 4 then None else Some(reader.GetInt64 4)
                                denominator = (if reader.IsDBNull 5 then None else Some(reader.GetInt64 5))
                                severe = (reader.GetInt64 6 = 1L)
                                reason = reader.GetString 7
                                epoch = reader.GetString 8
                            |}
                        )

                    reader.Close()
                    use membership = connection.CreateCommand()
                    membership.CommandText <- "SELECT epoch_id FROM budget_epoch_membership WHERE item_id=$item;"
                    parameter membership "$item" itemId
                    let epochValue = membership.ExecuteScalar()

                    let epoch =
                        if isNull epochValue || epochValue = box DBNull.Value then
                            None
                        else
                            Some(string epochValue)

                    Ok(
                        JsonSerializer.Serialize
                            {|
                                schema = "fsgg.telemetry.budget-summary/1"
                                item = itemId
                                epoch = epoch
                                dimensions = dimensions.ToArray()
                            |}
                        + "\n"
                    )
            with error ->
                Error [ error.Message ]

    let budgetStatus path assessment =
        match
            validateRoot path assessment
            |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly)
        with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection

            try
                use snapshot = connection.BeginTransaction()
                let version = Int32.Parse(scalarText connection "PRAGMA user_version;")

                if version <> currentSchemaVersion then
                    Error [ "telemetry store schema requires migration; run telemetry store init" ]
                else
                    let epoch =
                        scalarText connection "SELECT epoch_id FROM budget_epochs WHERE state='open';"

                    let scalar sql =
                        use command = connection.CreateCommand()
                        command.CommandText <- sql
                        Convert.ToInt64(command.ExecuteScalar())

                    let intervention =
                        use command = connection.CreateCommand()
                        command.CommandText <- "SELECT state FROM budget_interventions WHERE epoch_id=$epoch;"
                        parameter command "$epoch" epoch
                        let value = command.ExecuteScalar()

                        if isNull value || value = box DBNull.Value then
                            "none"
                        else
                            string value

                    Ok(
                        JsonSerializer.Serialize
                            {|
                                schema = "fsgg.telemetry.budget-status/1"
                                epoch = epoch
                                distinctBreaches =
                                    scalar
                                        $"SELECT count(DISTINCT item_id) FROM budget_breaches WHERE epoch_id='%s{epoch}';"
                                intervention = intervention
                                dirtyItems = scalar "SELECT count(*) FROM budget_dirty_items;"
                            |}
                        + "\n"
                    )
            with error ->
                Error [ error.Message ]

    let budgetHealth path assessment (itemId: string) =
        match
            validateRoot path assessment
            |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly)
        with
        | Error errors -> Error errors
        | Ok(connection, _) ->
            use connection = connection

            try
                if
                    Int32.Parse(scalarText connection "PRAGMA user_version;")
                    <> currentSchemaVersion
                then
                    Error [ "telemetry store schema requires migration; run telemetry store init" ]
                else
                    let scalar sql =
                        use command = connection.CreateCommand()
                        command.CommandText <- sql
                        parameter command "$item" itemId
                        Convert.ToInt64(command.ExecuteScalar())

                    let text sql fallback =
                        use command = connection.CreateCommand()
                        command.CommandText <- sql
                        parameter command "$item" itemId
                        let value = command.ExecuteScalar()

                        if isNull value || value = box DBNull.Value then
                            fallback
                        else
                            string value

                    let population =
                        text
                            "SELECT state FROM budget_population_facts WHERE item_id=$item ORDER BY CASE WHEN source_ref LIKE 'derived:%' THEN 0 ELSE 1 END,fact_revision DESC,identity DESC LIMIT 1;"
                            "missing"

                    let pending =
                        scalar "SELECT count(*) FROM budget_dirty_items WHERE item_id=$item;" > 0L

                    let outcomes =
                        scalar "SELECT count(*) FROM native_item_outcomes WHERE item_id=$item;"

                    let unknown =
                        scalar
                            "SELECT count(*) FROM budget_assessment_revisions a WHERE item_id=$item AND verdict='unknown' AND assessment_revision=(SELECT max(assessment_revision) FROM budget_assessment_revisions b WHERE b.item_id=a.item_id AND b.dimension=a.dimension AND b.provider=a.provider AND b.accounting_scope=a.accounting_scope);"

                    let status =
                        if pending then "pending"
                        elif outcomes = 0L then "missing-outcome"
                        elif population = "completed" then "complete"
                        else "open"

                    Ok(
                        JsonSerializer.Serialize
                            {|
                                schema = "fsgg.telemetry.budget-health/1"
                                item = itemId
                                status = status
                                population = population
                                unknownDimensions = unknown
                                dirty = pending
                            |}
                        + "\n"
                    )
            with error ->
                Error [ error.Message ]

    let exportPublic path assessment itemId outputPath =
        try
            let content =
                match itemId with
                | Some item -> summary path assessment item
                | None ->
                    match
                        validateRoot path assessment
                        |> Result.bind (fun root -> connect root SqliteOpenMode.ReadOnly)
                    with
                    | Error errors -> Error errors
                    | Ok(connection, _) ->
                        use connection = connection
                        use command = connection.CreateCommand()

                        command.CommandText <-
                            "SELECT DISTINCT item_id FROM current_ingest_facts WHERE item_id IS NOT NULL AND kind NOT IN ('learn-task-snapshot','learn-context-manifest','learn-experiment-assignment','learn-accounting-inventory/1','runtime-native-inventory/1','runtime-native-inventory-source/1','learn-shared-cost/1','learn-shared-cost-allocation/1','learn-shared-cost-authority/1','learn-native-delivery-source/1','learn-installed-origin/1') ORDER BY item_id;"

                        use reader = command.ExecuteReader()
                        let items = ResizeArray<string>()

                        while reader.Read() do
                            items.Add(reader.GetString 0)

                        reader.Close()

                        let summaries =
                            items
                            |> Seq.map (fun item ->
                                use document =
                                    JsonDocument.Parse(TelemetryStore.publicJson (readSummary connection item))

                                document.RootElement.Clone())
                            |> Seq.toArray

                        Ok(
                            JsonSerializer.Serialize
                                {|
                                    schema = "fsgg.telemetry.public-export/1"
                                    items = summaries
                                |}
                            + "\n"
                        )

            match content with
            | Error errors -> Error errors
            | Ok content when Encoding.UTF8.GetByteCount content > 64 * 1024 ->
                Error [ "public export exceeds 65536 bytes" ]
            | Ok content ->
                let target = Path.GetFullPath outputPath

                let rootPrefix =
                    Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar)
                    + string Path.DirectorySeparatorChar

                if target.StartsWith(rootPrefix, StringComparison.Ordinal) then
                    Error [ "public export must be outside the private store root" ]
                elif
                    existingAncestors target
                    |> List.exists (fun entry -> not (isNull entry.LinkTarget))
                then
                    Error [ "public export path has a symlinked ancestor" ]
                else
                    let parent = Path.GetDirectoryName target
                    Directory.CreateDirectory parent |> ignore
                    let temporary = target + ".tmp-" + Guid.NewGuid().ToString("N")
                    File.WriteAllText(temporary, content, UTF8Encoding(false))
                    File.Move(temporary, target, true)
                    fsyncDirectory parent

                    Ok(
                        JsonSerializer.Serialize
                            {|
                                schema = "fsgg.telemetry.export-result/1"
                                output = target
                                bytes = Encoding.UTF8.GetByteCount content
                            |}
                        + "\n"
                    )
        with error ->
            Error [ error.Message ]

    let private option name args =
        args
        |> List.indexed
        |> List.tryPick (fun (index, value) ->
            if value = name then
                args |> List.tryItem (index + 1)
            else
                None)

    let private root args =
        option "--store-root" args
        |> Option.orElseWith (fun () ->
            Environment.GetEnvironmentVariable("FSGG_TELEMETRY_STORE")
            |> Option.ofObj
            |> Option.filter (String.IsNullOrWhiteSpace >> not))

    let private output (result: Result<string, string list>) =
        match result with
        | Ok json ->
            Console.Out.Write json
            0
        | Error errors ->
            errors
            |> List.iter (fun error -> Console.Error.WriteLine("fsgg-coord-engine: telemetry store: " + error))

            1

    let private readBounded path =
        use stream =
            if path = "-" then
                Console.OpenStandardInput()
            else
                File.OpenRead path

        use memory = new MemoryStream()
        let buffer = Array.zeroCreate<byte> 8192
        let mutable doneReading = false

        while not doneReading && memory.Length <= int64 TelemetryStore.MaxBatchBytes do
            let count = stream.Read(buffer, 0, buffer.Length)

            if count = 0 then
                doneReading <- true
            else
                memory.Write(buffer, 0, count)

        memory.ToArray()

    let run action args =
        match root args with
        | None ->
            if action = "status" then
                Console.Out.WriteLine("{\"schema\":\"fsgg.telemetry.store-status/1\",\"status\":\"unconfigured\"}")
                0
            else
                output (Error [ "store root is not configured; use --store-root or FSGG_TELEMETRY_STORE" ])
        | Some path ->
            let assessment = assessProductionRoot path

            match action with
            | "status" -> status path assessment |> output
            | "init" -> initialize path assessment |> output
            | "publish" ->
                match option "--input" args with
                | Some input -> publish path assessment (readBounded input) |> output
                | None -> output (Error [ "--input is required" ])
            | "drain" -> drain path assessment |> output
            | "ingest" ->
                match option "--input" args with
                | Some input -> ingest path assessment (readBounded input) |> output
                | None -> output (Error [ "--input is required" ])
            | "summary" ->
                match option "--item" args with
                | Some item -> summary path assessment item |> output
                | None -> output (Error [ "--item is required" ])
            | "reconcile" ->
                match option "--item" args with
                | Some item -> reconcile path assessment item |> output
                | None -> output (Error [ "--item is required" ])
            | "export" when List.contains "--public" args ->
                match option "--output" args with
                | Some target -> exportPublic path assessment (option "--item" args) target |> output
                | _ -> output (Error [ "--output is required" ])
            | "export" -> output (Error [ "only --public export is supported" ])
            | _ -> output (Error [ "action must be status, init, ingest, summary, reconcile, or export" ])

    let runBudget action args =
        match root args with
        | None ->
            if action = "status" then
                Console.Out.WriteLine("{\"schema\":\"fsgg.telemetry.budget-status/1\",\"status\":\"unconfigured\"}")
                0
            else
                output (Error [ "store root is not configured; use --store-root or FSGG_TELEMETRY_STORE" ])
        | Some path ->
            let assessment = assessProductionRoot path

            match action with
            | "status" -> budgetStatus path assessment |> output
            | "summary" ->
                match option "--item" args with
                | Some item -> budgetSummary path assessment item |> output
                | None -> output (Error [ "--item is required" ])
            | _ -> output (Error [ "action must be status or summary" ])

    let runReview action args =
        match root args with
        | None -> output (Error [ "store root is not configured; use --store-root or FSGG_TELEMETRY_STORE" ])
        | Some path ->
            let assessment = assessProductionRoot path

            match action, option "--item" args with
            | "summary", Some item -> reviewSummary path assessment item |> output
            | "summary", None -> output (Error [ "--item is required" ])
            | _ -> output (Error [ "action must be summary" ])

    let runItemDetail args =
        match root args with
        | None -> output (Error [ "store root is not configured; use --store-root or FSGG_TELEMETRY_STORE" ])
        | Some path ->
            match option "--format-version" args, List.contains "--all" args, option "--item" args with
            | None, false, Some item -> itemDetail path (assessProductionRoot path) item |> output
            | Some "1", false, Some item -> itemDetail path (assessProductionRoot path) item |> output
            | Some "2", false, Some item -> dashboardSnapshot path (assessProductionRoot path) (Some item) |> output
            | Some "2", true, None -> dashboardSnapshot path (assessProductionRoot path) None |> output
            | Some "3", false, Some item -> compactDashboardSnapshot path (assessProductionRoot path) (Some item) |> output
            | Some "3", true, None -> compactDashboardSnapshot path (assessProductionRoot path) None |> output
            | Some version, _, _ when version <> "1" && version <> "2" && version <> "3" ->
                output (Error [ "unsupported item detail format version" ])
            | _, true, Some _ -> output (Error [ "--all and --item are mutually exclusive" ])
            | _ -> output (Error [ "--item is required (or use --format-version 2 --all)" ])
