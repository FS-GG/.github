# LEARN-01 native source capture contract

This source slice captures one bounded native Codex thread tree and independently verifies a telemetry projection against retained source bytes. Its sole positive outcome is `native-census-and-usage-reconciled-at-capture`.

## Fixed source and retained evidence

The collector reads Codex App Server through fixed `thread/read`, `thread/list`, and `thread/turns/list` requests. Descendant discovery queries both active and archived `subAgent` and `subAgentThreadSpawn` kinds. Every descendant must have the exact `parentThreadId` and `source.subAgent.thread_spawn.parent_thread_id` expected from traversal. The captured parent chain starts at the requested root. Every page must terminate inside the declared page and row bounds; repeated cursors, unsupported shapes, duplicate identities, and nonterminal or empty turn rosters refuse.

The private capture retains the exact JSON request and matching JSON response bytes for every call, together with SHA-256 digests. A second complete census is taken after rollout scanning. Any topology, provider/profile, rollout path, terminal status, turn identity, sequence, or page result change refuses the capture. Capture timestamps and caller-supplied support assertions are not evidence and are absent from the contract.

For each discovered thread, the collector opens the App Server supplied rollout path relative to the configured Codex `sessions` directory. It walks every component with descriptor-relative `O_NOFOLLOW`, reads a fixed regular-file descriptor within the byte bound, and requires unchanged device, inode, size, and nanosecond modification time. The private artifact records that identity, the full-file digest and scan bound, and exact bytes, offsets, lengths, and digests only for selected `token_usage_record` lines. It does not retain conversation records.

A later record for the same response ID replaces the earlier counters. The final retained `turn_token_usage` must equal the sum of the final response counters for that turn. Missing terminal-turn usage, foreign thread or turn IDs, malformed or inconsistent counters, partial records, source mutation, inaccessible files, symlinks, and size-limit failures refuse.

## Independent verification

Verification reparses the retained request and response bytes, replays the fixed request sequence, reconstructs both censuses, recomputes the source projection and token totals, and checks the capture digest. It then compares a closed telemetry snapshot containing thread identity, parent chain, provider, exact ordered turn IDs, and per-turn usage. The result explicitly lists missing, foreign, reordered, or mismatched descendants, turns, and usage. Any discrepancy is `incomplete` and has no positive outcome.

The public CLI accepts capture location, Codex home, root thread identity, and a telemetry snapshot. It accepts no support flag, expected roster, caller digest, provider capability assertion, assignment, or qualification override. Injectable transports are private test seams.

## Deliberate limits

This contract does not establish policy assignment, treatment fidelity, original-item membership, expected dispatch population, follow-up ownership, shared-cost allocation, provider capability, delivery acceptance, whole-item totals, or comparative token qualification. It performs no public publication and makes no claim about live installed collection. Package A/B integration, telemetry writer role binding, and adoption remain separate work.
