# LEARN-01 native source capture contract

This source slice captures one bounded native Codex thread tree and independently verifies a telemetry projection against retained source bytes. Its sole positive outcome is `native-census-and-usage-reconciled-at-capture`.

## Fixed source and retained evidence

The collector reads Codex App Server through fixed `thread/read`, `thread/list`, and `thread/turns/list` requests. Descendant discovery queries both active and archived `subAgent` and `subAgentThreadSpawn` kinds. Every descendant must have the exact `parentThreadId` and `source.subAgent.thread_spawn.parent_thread_id` expected from traversal. The captured parent chain starts at the requested root. Every page must terminate inside the declared page and row bounds; repeated cursors, unsupported shapes, duplicate identities, and nonterminal or empty turn rosters refuse.

The private capture retains the exact JSON request and matching JSON response bytes for every call, together with SHA-256 digests. A second complete census is taken after rollout scanning. Any topology, provider/profile, rollout path, terminal status, turn identity, sequence, or page result change refuses the capture. Capture timestamps and caller-supplied support assertions are not evidence and are absent from the contract.

For each discovered thread, the collector opens the App Server supplied rollout path relative to the configured Codex `sessions` directory. It walks every component with descriptor-relative `O_NOFOLLOW`, reads a fixed regular-file descriptor within the byte bound, and requires unchanged device, inode, size, and nanosecond modification time. The private artifact records that identity, the full-file digest and scan bound, and exact bytes, offsets, lengths, and digests only for selected `token_usage_record` lines. It does not retain conversation records.

A later record for the same response ID replaces the earlier counters. The final retained `turn_token_usage` must equal the sum of the final response counters for that turn. Missing terminal-turn usage, foreign thread or turn IDs, malformed or inconsistent counters, partial records, source mutation, inaccessible files, symlinks, and size-limit failures refuse.

## Independent verification

Verification reparses the retained request and response bytes, replays the fixed request sequence, reconstructs both censuses, recomputes the source projection and token totals, and checks the capture digest. It then compares a closed telemetry snapshot containing thread identity, parent chain, provider, exact ordered turn IDs, and per-turn usage. The result explicitly lists missing, foreign, reordered, or mismatched descendants, turns, and usage. Any discrepancy is `incomplete` and has no positive outcome.

The standalone verification CLI accepts capture location, Codex home, root thread identity, and a telemetry snapshot. It accepts no support flag, expected roster, caller digest, provider capability assertion, assignment, or qualification override. Injectable transports are private test seams.

The telemetry host also exposes a narrower protected collection command. Its request contains only a durable dispatch selector, the native parent thread selector, and the native agent selector. The host resolves the child invocation, root invocation, original item, requested model, and requested effort from durable admitted facts. It loads the exact executable path, Codex home, evidence root, provider profile, and restricted collector principal from a private adjacent installation file; none can be supplied on the command line. The executable runs with a cleared environment containing only the protected homes and a fixed locale. The reader then reconstructs the App Server roster and rollout counters and the host submits the resulting inventory and source binding only through that principal.

Before receipt submission, the host atomically retains the canonical envelope in the protected evidence root. A retry reuses those exact bytes and the receipt store preserves their original role and grant. A durable model/effort mismatch, missing or ambiguous child lineage, foreign native agent selector, unsafe installation path, incomplete census, or changed provider profile refuses before new facts are admitted. The command never accepts caller inventory, totals, rollout path, source binding, executable, source root, or credential.

## Deliberate limits

This contract does not establish policy assignment, treatment fidelity, follow-up ownership, shared-cost allocation, provider capability, delivery acceptance, whole-item totals, or comparative token qualification. The protected command proves only that a host-owned reader reconstructed a source candidate for one already admitted child; the retained candidate still has `unknown` source verification and snapshot origin. It performs no public publication and makes no claim that the protected installation has been deployed. Adapter adoption, installed credential/configuration, trusted capture custody, and trusted snapshot acquisition remain separate work.
