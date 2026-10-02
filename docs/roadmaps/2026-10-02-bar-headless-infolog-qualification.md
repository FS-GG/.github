# BAR headless engine and infolog qualification plan

Status: selected engine update and proposed verifier implementation, 2026-10-02. Owners: the FSBar/HighBar
source lanes and the existing BAR product integrator. This follows
[BARC-01.5f](https://github.com/FS-GG/FSBarV2/blob/main/docs/roadmaps/barc-01-useful-play.md)
and its RP1–RP5 continuation; it creates no additional acceptance ledger.

The selected next proof budget is **10 MiB of raw infolog**, replacing 4 MiB after
coherent source delivery and installation. Qualify a current stock headless engine
before choosing an atlas workaround. Reassess the budget against that engine's
actual log volume. Model complete-record handling in F# and Quint, retaining
unfinished bytes and refusing ambiguous evidence.

This is BAR product work. Full V2 acceptance at its selected execution profile
remains complete. Coordination V2 board migration retains its selected priority;
BAR qualification can progress independently.

## Findings and limits

The [selected stock profile](https://github.com/FS-GG/FSBarV2/blob/4955ae7e7b7ce7ff2f5d5aaa6231fc84662585b7/docs/roadmaps/barc-01-useful-play.md)
uses unmodified Recoil **2025.06.19**, source
`2639eedac7d1fd67d793ec93ebd27f014f336a14`, with an ABI-matched HighBar plugin.
The owner selected an update on 2026-10-02 to upstream's current stable
[2026.07.04 release](https://github.com/beyond-all-reason/RecoilEngine/releases/tag/2026.07.04),
published 2026-08-04, source `de69361239d8c8b1012dba3f5aa3122954ea4da3`.
Engine installation and compatibility results are recorded below as they become
available; useful-play acceptance requires separate native qualification.

The official amd64 Linux archive was downloaded and installed in a separate
candidate directory. Its SHA-256 matches GitHub's asset digest
`9824c2c38124e4b90a9b5f7c4e7200c3ea6503c0bd4bff0e0ff46212ec29dcab`.
The extracted `spring-headless` hashes to
`651d6dca67ad99fde1a593d57eede0dfee167e200558988f154ad2bac1cfce21`
and actually reports `spring-headless version 2026.07.04 (Headless)`.
The existing runtime directory and failed-attempt evidence were preserved.
Plugin source adoption and native compatibility remain separate checks.

Upstream [2025.06.20 already added a headless guard](https://github.com/beyond-all-reason/RecoilEngine/blob/2025.06.20/rts/Rendering/Textures/TextureRenderAtlas.cpp#L383)
in `CTextureRenderAtlas::CreateAtlasTexture()`, skipping OpenGL work and marking
the atlas rendered. The guard remains in
[2026.07.04 source](https://github.com/beyond-all-reason/RecoilEngine/blob/de69361239d8c8b1012dba3f5aa3122954ea4da3/rts/Rendering/Textures/TextureRenderAtlas.cpp#L389).
The exact function matches the observed retry messages. This is strong source
evidence for an existing upstream repair; it does not prove the new engine resolves
the local incident or the separate Lua rendering errors.

The retained 2026-10-02 native attempt refused before browser admission with
`infolog-record-settlement-exhausted`; cleanup settled and useful-play qualification
remained 0/6. Its log contained 3,666,566 bytes, 33,531 complete records and a
77-byte unfinished tail. Atlas retries accounted for 32,019 complete records
(95.49%). The 4 MiB byte cap had not been exceeded. These are sanitized incident
measurements, not a successful gameplay result or an expected rate for the successor.

Two verifier assumptions also need reassessment: sampled EOF must end at a newline,
and total file size must remain unchanged across policy evaluation. The stock
producer uses buffered writes; neither condition follows from a complete earlier
record. Trimming the tail alone leaves the second assumption unresolved. See the
[protected policy](https://github.com/FS-GG/FSBarV2/blob/4955ae7e7b7ce7ff2f5d5aaa6231fc84662585b7/tests/Broker.NativeProof/RuntimeEvidence/DataRootPolicy.fs)
and [actual adapter](https://github.com/FS-GG/FSBarV2/blob/4955ae7e7b7ce7ff2f5d5aaa6231fc84662585b7/tests/Broker.NativeProof/RuntimeEvidence/fixtures/complete-record-helper/growing_log.py).

## Bounded delivery sequence

| Stage | Work | Completion evidence |
|---|---|---|
| Stock engine candidate | Select official 2026.07.04 at its immutable source and artifact hashes; verify headless assets and HighBar ABI compatibility. Preserve the old profile. | Actual loaded binary identity, plugin compatibility and isolated startup comparison; no source-only fix claim. |
| Budget amendment | Deliver coherent 10 MiB raw / 16 MiB encoded request guards and a full-size exact-read allowance. | F# and adapter boundary checks pass, then protected artifact readback proves the installed guards. |
| RP1/RP2 record contract | Retain RP1 buffered-writer reproduction; qualify the complete-record semantics in the canonical F# implementation and Quint model. | Real buffered-file transcripts and formal/concrete joins demonstrate truthful pending, refusal and acceptance. |
| RP3 capacity | Measure the successor across browser admission, normalization and release, including buffered bytes and cleanup. | Finite byte, record, I/O and time margins at every boundary; no unexplained logging flood. |
| RP4 successor | Deliver one coherent protected source/build/helper/plugin/engine closure and prepare fresh private placement. | Independent custody, configuration, source, artifact and actual loaded-process joins. |
| RP5 native operation | Run one changed, bounded Count1 attempt after the preceding evidence is accepted. | Genuine typed outcome and cleanup readback; all six original useful-play journeys remain separate gates. |

Engine artifact/ABI investigation and the budget source candidate can progress in
parallel using separate repositories and touch sets. Record-model work can use RP1
public controls concurrently; final capacity qualification depends on the selected
engine and qualified verifier. The product integrator owns runtime placement and
native execution, preventing competing launches.

### Qualify the stock engine first

Bind the official release, platform, source, downloaded artifact hash, dependency
closure and actual reported/loaded version. Rebuild the plugin only where the
new stock ABI requires it. Preserve command and queue semantics, the pinned game,
map and observation component; any required compatibility change gets its own
explicit source and artifact join. Retain failed-attempt evidence unchanged.

Compare startup log volume, atlas message counts, root records, unfinished tails
and Lua errors using the actual headless binary. If the upstream guard removes the
atlas flood, use the reduced workload for RP3. If errors remain, trace their exact
source and supported configuration before choosing another remedy. Consecutive
message suppression does not solve alternating atlas messages; guessed log-section
or flush settings do not constitute a verified repair. A custom engine fork is not
selected by this plan.

### Make the 10 MiB amendment coherent

Set raw guards in the F# policy, codec and adapter to 10 MiB. Set encoded request
and stdin guards to 16 MiB to accommodate Base64 plus bounded metadata. Permit
160 reads of at most 64 KiB for a full-size exact read; the old 128-call allowance
cannot read an admitted 10 MiB file. Preserve the 65,536-record limit, 32 shared
probes, three evaluations and five-second deadline unless separate evidence
justifies a later decision. Unrelated process-map, normalization, guest and
knowledge-store limits retain their own purposes.

A local source candidate has seven passing Python controls, including full-size
read and encoded-envelope checks. Its F# tests and protected delivery are pending;
this does not change any existing installed artifact. Verify exact byte limit and
limit + 1, exact record limit and limit + 1, short reads and oversized envelopes
through the actual F# entrypoint before RP4.

### Preserve complete-record evidence

Extend the existing `GrowingLogEvidence.qnt` and typed F# reducer/codec; do not
create a shadow state machine. Bind the full raw sample, complete-record prefix
and unfinished tail with lengths, hashes and exact recombination. F# independently
derives UTF-8, record boundaries and relevant root semantics. Keep writer/process
identity, inode, prefix, truncation, rewrite and closure checks.

Define the consumption boundary and the effect of append before and after it.
Newly completed contradictory roots revoke authority. A partial record that could
be relevant remains pending or unknown, with no new authority; treating a tail as
irrelevant requires an explicit source-grounded grammar. Retain its raw bytes.
Future writes remain possible after a successful observation; the contract must
state exactly which completed evidence it consumed and when freshness is checked.

Exercise actual buffered ordinary-file writers and the real compiled policy.
Include partial UTF-8, relevant partial roots, delayed contradictions, growth during
evaluation, rewrite, truncation/regrowth, writer replacement and unknown cleanup.
No byte deletion, fabricated transcript or unlimited retry makes evidence acceptable.

### Revisit capacity from measured operation

Measure both byte and record margins: the unchanged record limit may bind before
10 MiB. Include browser timing, normalization, release, queued buffer flushes,
cleanup tail, encoded copies and policy deadline. Revisit when either cap becomes
a constraint, documenting the measured cause and the product integrator's selected
response. A continuing flood calls for a producer repair or an explicitly modeled
bounded streaming design; raising caps repeatedly is not the default response.

### Record results without overstating completion

Source checks, protected merge, built artifact, private installation and native
acceptance are separate results. RP5 has no automatic same-input retry. Preserve
refusal/unknown/deadline/capacity outcomes and truthful cleanup; browser admission
alone is not Count1 acceptance. Main is not a deployment prerequisite.

Fold the accepted findings and later measurements into the BAR project knowledge
chapters during owning-lane delivery. Follow the existing
[knowledge backup plan](bar-knowledge-backup-retention.md): compact project knowledge,
current backup plus at most three older versions, excluding the large source index.
This document itself neither imports private records nor changes runtime settings.
