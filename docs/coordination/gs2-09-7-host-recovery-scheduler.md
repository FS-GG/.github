# GS2-09.7 protected recovery scheduler batch contract

This draft adds a source-only scheduler handoff. It does not install a scheduler,
store, credential, workflow, or native provider adapter. Every live identity pin
in the census, worker, finalizer, and release chain remains blank. GS2-09.7 Q5/Q6
remain open; protected workflow #3690 is an unadmitted observation.

The scheduler must first verify the complete protected mint index and sealed
pending census. It then submits **one** durable compare-and-swap batch containing
exactly one recovery job for every pending subject in that seal. The batch binds
the high-water mark, pending and mint digests, scheduler/queue/journal/recovery
identities, and all exact job identities. A missing pending intent for a minted
token prevents any batch append unless a terminal receipt has fresh native
revocation readback. A lost append response leaves the result pending; it must
not cause a second append or launch.

The worker checks a durable batch twice, recomputes the pending subject digest
from its jobs, rejects missing or duplicate jobs, and requires the exact job to
be a member. Its one-use claim carries both schedule and batch IDs. The
installed protected store must linearize the batch commit, withdrawal and
claim in one authority domain: a withdrawn or replaced batch cannot yield a
fresh claim, including when withdrawal occurs after readback. A committed claim
is still subject to the existing shared native-attempt marker and native
revocation readback; neither scheduler nor worker may invoke a candidate.

The fake store proves source refusal for omitted subjects, duplicate jobs,
withdrawal, a pre-pending mint, and a lost append response. It does not prove
durability, authentic mint-index completeness, store isolation, native token
custody, or a live atomic compare-and-swap. Protected owners must supply and
pin a scheduler endpoint and identity, a candidate-inaccessible durable store,
an authentic joint mint/pending seal with complete high-water coverage, and a
native adapter with readback. The store must provide atomic full-batch append,
exact independent batch/job reads, and batch-aware one-use claim semantics.
No production scheduling or GS2-09.7 acceptance follows from this draft.
