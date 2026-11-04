# Batched enrollment successor: cohort91-root-selected17-5263

This carrier delivers the public commitment of one successor enrollment pair prepared offline by the maintained BoardV2Import constructor (`plan-successor`, `prepare-successor`). It appends 17 row(s) to the 207 rows of the predecessor, which was the protected commitment at `dc9ac5b0780618bb3302a213a127fcace2185be7`, for 224 rows in total. No identity, order or node id of a predecessor row changes.

| Fact | Value |
|---|---|
| Predecessor rows | 207 |
| Appended rows | 17 |
| Total rows | 224 |
| Commitment SHA256 | `a7f9b2ae0eb1300061d59f58ff9fccd6d42a115304d5dfd8e65036bea1a5889f` |
| Public blob SHA256 | `9de1e32a811a3caaefd6502a0046ce976dbf22bf9769ae2a29756eb437819d33` |
| Private binding SHA256 | `492d1fa2c1d77df84b8bd2b680a108cf31e5ecd9cd298f5dce7271d968079753` |
| Constructor source revision | `9c0a1551d29d6d733b997ab9f33d8ea4852a45f1` |
| Constructor SHA256 | `f4a8991b44cc39960c4e504e4da5b7df13fb63f68524accb1db00b15dfd339af` |
| Predecessor prefix byte-identical | yes |
| Payload header and binding fields unchanged | yes |
| Commitment recomputed from the private pair | yes |

The [public manifest](board-v2-import-manifest.json) carries only the two-field commitment document. The private payload, nonce and binding stay outside source.

After protected review and merge, Root must rebind only the successor private runtime binding's population revision to the actual protected delivery (`bind-runtime`), authenticate the delivered public blob against the pair and qualify the exact installed population read. Offline construction and this carrier grant no native write, implementation admission or enrollment authority.
