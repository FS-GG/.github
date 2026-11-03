-- Read-only bounded metadata projection. No transcript text or tokens are exported.
-- Execute with sqlite3 -readonly -json. Preserve all sections and hashes.
BEGIN;
WITH raw AS (
 SELECT l.item_id,l.invocation_id,l.runtime,d.expected_at,a.requested_model,a.requested_effort,a.producer_stream
 FROM invocation_lineage l JOIN expected_dispatches d ON d.item_id=l.item_id AND d.dispatch_id=l.dispatch_id
 LEFT JOIN runtime_admissions a ON a.item_id=l.item_id AND a.invocation_id=l.invocation_id
 WHERE l.item_id LIKE 'FS-GG/%' AND l.runtime=d.runtime
), eligible AS (SELECT DISTINCT item_id,invocation_id FROM raw)
SELECT 'controls' AS section,json_object('rawAssignments',(SELECT count(*) FROM raw),
 'assignments',(SELECT count(*) FROM eligible),'invocations',(SELECT count(DISTINCT invocation_id) FROM eligible),
 'items',(SELECT count(DISTINCT item_id) FROM eligible),
 'ambiguousInvocations',(SELECT count(*) FROM (SELECT invocation_id FROM eligible GROUP BY invocation_id HAVING count(*)>1)),
 'runtimeMismatches',(SELECT count(*) FROM invocation_lineage l JOIN expected_dispatches d ON d.item_id=l.item_id AND d.dispatch_id=l.dispatch_id WHERE l.item_id LIKE 'FS-GG/%' AND l.runtime<>d.runtime)) AS payload;
WITH eligible AS (
 SELECT l.item_id,l.invocation_id,l.runtime,min(d.expected_at) AS firstExpectedAt,a.requested_model,a.requested_effort,a.producer_stream
 FROM invocation_lineage l JOIN expected_dispatches d ON d.item_id=l.item_id AND d.dispatch_id=l.dispatch_id
 LEFT JOIN runtime_admissions a ON a.item_id=l.item_id AND a.invocation_id=l.invocation_id
 WHERE l.item_id LIKE 'FS-GG/%' AND l.runtime=d.runtime
 GROUP BY l.item_id,l.invocation_id,l.runtime,a.requested_model,a.requested_effort,a.producer_stream
), rows AS (
 SELECT e.*,count(u.identity) AS responses,
 sum(CASE WHEN u.identity IS NOT NULL AND json_extract(u.canonical,'$.model') IS NULL THEN 1 ELSE 0 END) AS nullModelResponses,
 sum(CASE WHEN u.identity IS NOT NULL AND json_extract(u.canonical,'$.effort') IS NULL THEN 1 ELSE 0 END) AS nullEffortResponses,
 json_group_array(DISTINCT json_extract(u.canonical,'$.model')) AS models,
 json_group_array(DISTINCT json_extract(u.canonical,'$.effort')) AS efforts
 FROM eligible e LEFT JOIN current_ingest_facts u ON u.kind='provider-usage-observation/1' AND json_extract(u.canonical,'$.threadId')=e.invocation_id
 GROUP BY e.item_id,e.invocation_id,e.runtime,e.firstExpectedAt,e.requested_model,e.requested_effort,e.producer_stream
)
SELECT 'invocations' AS section,json_group_array(json_object('item',item_id,'invocation',invocation_id,'runtime',runtime,
 'firstExpectedAt',firstExpectedAt,'launchResolvedModel',requested_model,'storedRequestedEffort',requested_effort,
 'producerStream',producer_stream,'responses',responses,'nullModelResponses',nullModelResponses,'nullEffortResponses',nullEffortResponses,
 'models',json(models),'efforts',json(efforts))) AS payload FROM rows;
WITH eligible AS (
 SELECT DISTINCT l.item_id,l.invocation_id FROM invocation_lineage l JOIN expected_dispatches d ON d.item_id=l.item_id AND d.dispatch_id=l.dispatch_id
 WHERE l.item_id LIKE 'FS-GG/%' AND l.runtime=d.runtime
), kinds(kind) AS (VALUES('runtime-start'),('runtime-terminal'),('activity-span'),('runtime-gap'))
SELECT 'boundaries' AS section,json_group_array(json_object('kind',k.kind,'attributedFacts',
 (SELECT count(DISTINCT f.identity) FROM current_ingest_facts f JOIN eligible e ON f.item_id=e.item_id AND json_extract(f.canonical,'$.invocationId')=e.invocation_id WHERE f.kind=k.kind))) AS payload FROM kinds k;
WITH items AS (
 SELECT DISTINCT l.item_id FROM invocation_lineage l JOIN expected_dispatches d ON d.item_id=l.item_id AND d.dispatch_id=l.dispatch_id
 WHERE l.item_id LIKE 'FS-GG/%' AND l.runtime=d.runtime
)
SELECT 'snapshots' AS section,json_object('facts',count(DISTINCT f.identity),'items',count(DISTINCT f.item_id)) AS payload
FROM current_ingest_facts f JOIN items i ON i.item_id=f.item_id WHERE f.kind='learn-task-snapshot';
SELECT 'population' AS section,json_object('passiveResponses',count(*),'nullModelResponses',sum(json_extract(canonical,'$.model') IS NULL),
 'nullEffortResponses',sum(json_extract(canonical,'$.effort') IS NULL),
 'unknownTerminalResponses',sum(json_extract(canonical,'$.terminalCoverage')='unknown'),
 'partialPopulationResponses',sum(json_extract(canonical,'$.populationCoverage')='partial')) AS payload
FROM current_ingest_facts WHERE kind='provider-usage-observation/1';
COMMIT;
