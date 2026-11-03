-- Private metadata only. Completion timestamps do not establish active execution.
BEGIN;
WITH eligible AS (
 SELECT DISTINCT l.item_id,l.invocation_id,l.runtime,a.producer_stream
 FROM invocation_lineage l JOIN expected_dispatches d ON d.item_id=l.item_id AND d.dispatch_id=l.dispatch_id
 LEFT JOIN runtime_admissions a ON a.item_id=l.item_id AND a.invocation_id=l.invocation_id
 WHERE l.item_id LIKE 'FS-GG/%' AND l.runtime=d.runtime
), observed AS (
 SELECT e.*,u.identity,json_extract(u.canonical,'$.occurredAt') AS occurred_at,
 json_extract(u.canonical,'$.model') AS model,json_extract(u.canonical,'$.effort') AS effort
 FROM eligible e LEFT JOIN current_ingest_facts u ON u.kind='provider-usage-observation/1' AND json_extract(u.canonical,'$.threadId')=e.invocation_id
 ORDER BY e.item_id,e.invocation_id,occurred_at,u.identity
)
SELECT item_id AS itemId,invocation_id AS invocationId,runtime,producer_stream AS producerStream,
 count(identity) AS responseCount,count(occurred_at) AS timestampedResponseCount,
 min(occurred_at) AS firstObservedResponseAt,max(occurred_at) AS lastObservedResponseAt,
 json_group_array(occurred_at) FILTER (WHERE identity IS NOT NULL) AS occurredAtJson,
 json_group_array(DISTINCT model) FILTER (WHERE identity IS NOT NULL) AS observedModelsJson,
 json_group_array(DISTINCT effort) FILTER (WHERE identity IS NOT NULL) AS observedEffortsJson
FROM observed GROUP BY item_id,invocation_id,runtime,producer_stream;
COMMIT;
