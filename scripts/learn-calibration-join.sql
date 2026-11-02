-- Read-only Claude dispatch attribution. Run with sqlite3 -readonly (JSON functions required).
-- Invocation-lineage identity is not a uniqueness constraint on (item, invocation).
WITH eligible_rows AS (
    SELECT l.item_id, l.invocation_id
    FROM invocation_lineage l
    JOIN expected_dispatches d ON d.item_id = l.item_id AND d.dispatch_id = l.dispatch_id
    WHERE d.runtime = 'claude-code-subagent' AND l.item_id LIKE 'FS-GG/%'
), lineage AS (
    SELECT DISTINCT item_id, invocation_id FROM eligible_rows
), conflicts AS (
    SELECT invocation_id FROM lineage GROUP BY invocation_id HAVING count(*) > 1
), totals AS (
    SELECT l.item_id, count(u.identity) AS responses,
           count(DISTINCT l.invocation_id) AS invocations,
           CASE WHEN count(u.identity) = count(json_extract(u.canonical, '$.total'))
                THEN sum(json_extract(u.canonical, '$.total')) ELSE NULL END AS total
    FROM lineage l
    JOIN current_ingest_facts u ON u.kind = 'provider-usage-observation/1'
        AND json_extract(u.canonical, '$.threadId') = l.invocation_id
    GROUP BY l.item_id
)
SELECT json_object(
    'schema', 'fsgg.learn-calibration-join/1',
    'lineageRows', (SELECT count(*) FROM eligible_rows),
    'distinctAssignments', (SELECT count(*) FROM lineage),
    'ambiguousInvocations', (SELECT count(*) FROM conflicts),
    'attributedIdentities', (SELECT count(*) FROM totals),
    'rows', CASE WHEN (SELECT count(*) FROM conflicts) > 0 THEN NULL
                ELSE json((SELECT json_group_array(json_object('item_id', item_id, 'responses', responses,
                              'invocations', invocations, 'total', total)) FROM totals)) END
);
