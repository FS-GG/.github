# CI throughput: the hosted-runner cap and added capacity

Status: measurement and proposed design for Root's decision. It enables, registers and changes nothing: no runner,
runner group, organization setting, plan, spending limit or workflow. Refs
[#5197](https://github.com/FS-GG/.github/issues/5197), under [#4730](https://github.com/FS-GG/.github/issues/4730);
demand reduction continues in [#5182](https://github.com/FS-GG/.github/issues/5182) and
[#5189](https://github.com/FS-GG/.github/issues/5189).

## 1. Finding

- **The cap is 20 concurrent GitHub-hosted jobs, organization-wide.** Reconstructed from job start and end times
  across all 25 FS-GG repositories, concurrency rose to 20 at 10:15Z on 2026-11-01 and stayed at 18-20 through
  12:17Z, the end of the 15-sample window. It never exceeded 20. The slots are shared across repositories: at one
  peak, `.github` held 18 and Coordination and Game one each.
- **Likely cause: the plan's hosted-runner concurrency limit.** GitHub documents 20 concurrent standard-runner jobs for
  the Free plan, 60 for Team and 500 for Enterprise Cloud. The plateau at exactly 20 matches Free. The plan itself is
  unreadable with this token (section 2.4), so "Free plan" is an inference from the plateau, not a readback.
- **It is not a spending limit.** Public-repository jobs on standard runners are not billed, and private-repository
  jobs ran today (FS.GG.SC2.Client, FS.GG.FourD, FS.GG.Unity.Client). An exhausted spending limit fails private jobs; it
  does not queue public ones.
- **It is not a repository or workflow setting.** No GitHub setting caps hosted jobs per repository, and the queued
  jobs are `queued` on `ubuntu-latest` with no runner assigned. Runs held by a `concurrency:` group would show
  `pending` instead.
- **"8 running" in #5197 is a counting artifact.** A run keeps run-level status `queued` while any of its jobs is
  queued, even when other jobs of the same run are executing. Counting `status=in_progress` runs, or the jobs inside
  them, therefore under-counts. At 12:05Z, 19 jobs were `in_progress`, and 15 of them sat in runs whose status was
  `queued`. Use job records, not run status, to measure concurrency.
- **Demand grew about tenfold.** `.github` `pull_request` runs were 264-552 per day from 25 to 29 October, then 5,252
  on 30 October and 4,942 on 31 October (#5182 window A). Queue wait followed: week-sample p50 17 s; today
  p50 422 s over 09:55-12:05 and 1,342 s in the 12:00-12:17 sample window.

## 2. Measurement

All reads used the Actions REST API with the coordination token. Scripts and raw data are in
`~/.fsgg-scratch/5197/meas/` on fdev (not committed). Times are UTC on 2026-11-01 unless stated.

### 2.1 Concurrency and queue

| Measure | Value | Source |
|---|---|---|
| Max concurrent jobs on a runner, 09:55-12:05 | **20** | job records, all repos (`conc.py`) |
| Time-weighted mean concurrency, 5-min buckets 10:15-12:00 | 18.5-19.8 | same |
| Share of 09:55-12:05 at 19 or 20 concurrent jobs | 77.5 % | same |
| Runs queued, 15 samples 12:00:19-12:17:29 | 145-206 | `sample.sh`, run-level `total_count` |
| Runs `in_progress` (run status), same samples | 3-12 (jobs inside them: 2-8) | same; the artifact in section 1 |
| Jobs on a runner at the same 15 instants, from job records | **19-20** (mean 19.7; 10-s grid max 20) | `window.py` over refetched job records |
| Oldest queued `.github` run at 12:01 | created 11:31 (30 min) | `q_github.json` |
| Runner labels of today's 3,021 jobs | `ubuntu-latest` 2,913, `ubuntu-24.04` 57, `windows-latest` 48, none 3 | job records |
| Self-hosted labels or runner groups other than `GitHub Actions` in use | none | job records |

### 2.2 Queue wait (job `created_at` to `started_at`)

| Window | Jobs | p50 | p90 | Source |
|---|---|---|---|---|
| 25-29 Oct, `.github` PR runs, one sampled run per hour | 70 | 4-17 s per day | 16-91 s per day | `week.py` |
| 25-31 Oct, same sample | 135 | 17 s | 253 s | `week.py` |
| 30 Oct / 31 Oct, same sample | 34 / 31 | 28 s / 216 s | 542 s / 387 s | `week.py` |
| 1 Nov 00:08-09:55, all `.github` PR jobs (#5182 window B) | 2,809 | 96 s | 437 s | #5182 `jobstats.py` |
| 1 Nov 10:00-10:59, all repos | 998 | 287 s | 1,169 s | `conc.py` |
| 1 Nov 11:00-11:59, all repos | 456 | 1,070 s | 1,568 s | `conc.py` |
| 1 Nov 09:55-12:05, all repos | 1,628 | **422 s** | **1,406 s** | `conc.py` |
| 1 Nov 12:00-12:17 (sample window), jobs started, all repos | 188 | 1,342 s | 2,806 s | `window.py` |

The week sample is thin (one run per hour, 83 hours with runs); it bounds the pre-surge level, nothing finer. #5182
window B is complete for its window.

### 2.3 Who holds the 20 slots (10:15-12:00, 2,036 slot-minutes, mean 19.4 busy)

| Consumer | Slot share |
|---|---|
| `.github` (816 jobs) | 45.8 % |
| FS.GG.Coordination (195 jobs) | 39.3 % |
| FS.GG.Game | 9.0 % |
| All private repositories together (SC2.Client, FourD, Unity.Client, Native.Qualification) | **4.1 %** |
| Coordination `optimistic-parallel-validation.yml` (14 PR runs) | 27.5 % |
| `.github` `coord-engine.yml` | 12.7 % |
| Coordination `bootstrap-qualification.yml` | 8.8 % |
| `.github` push events (the 47 queued at 12:01 were all on `main`) | 10.8 % |
| Coordination scheduled runs | 5.6 % |

### 2.4 Organization settings and billing: unreadable

Every settings and billing read returned 403 `Resource not accessible by personal access token`. The token is a
fine-grained PAT for `EHotwagner` without organization permissions:

| Endpoint | Result | Permission GitHub asks for |
|---|---|---|
| `orgs/FS-GG/actions/permissions` (and `/repositories`, `/workflow`, `/self-hosted-runners`, fork-PR approval) | 403 | `organization_administration=read` |
| `orgs/FS-GG/actions/runner-groups`, `orgs/FS-GG/actions/runners` | 403 | `organization_self_hosted_runners=read` |
| `orgs/FS-GG/settings/billing/actions`, `/usage`, `/usage/summary`, `/budgets` | 403 | `organization_administration=read` |
| `orgs/FS-GG/actions/hosted-runners` | 404 | (larger runners not readable or absent) |
| `orgs/FS-GG` | 200, but `plan` and private-repository counts absent | owner-only fields |
| `user/memberships/orgs/FS-GG` | 403 | |

So the plan, spending limit, fork-PR approval policy and runner-group features are **Unknown** here. The owner can
read them at `https://github.com/organizations/FS-GG/settings/billing` and `.../settings/actions`.

## 3. Options

Throughput is in hosted-equivalent job slots; today's demand saturates 20 with a queue of 150-190 runs.

| | (a) Plan change to Team | (b) Self-hosted runner on fdev | (c) Further demand reduction |
|---|---|---|---|
| **Throughput gain** | Cap 20 to 60 (3x) for every repository and event, if the org is on Free | About +2 slots (+10 %) at the fdev budget in 4.4. Usable only by the jobs it may serve: private repositories hold 4.1 % of slot time; `main`-ref work about 16 % (`.github` pushes plus Coordination schedules) | The largest consumers are one workflow each: 27.5 % and 12.7 % of slot time. #5189 removes one failed and one edit-triggered run per push; #5186 already folded 19.8 % of `.github` job starts |
| **Cost** | GitHub list price per member per month (verify the current price; member count is unreadable here). Public-repo minutes stay free; private repos gain included minutes | $0 for public repos. Private-repo self-hosted minutes may carry a GitHub platform charge (verify current pricing). Ongoing operator effort; fdev CPU and memory taken from agent lanes | Engineering time only |
| **Security risk** | None new | High on public repos: fork PRs control `runs-on` in `pull_request` workflows. fdev holds the coordination credentials, and `developer` has passwordless `sudo`. Contained only by the controls in section 4 | None; required contexts must stay intact (#5182 rules) |
| **Reversibility** | Downgrade from the billing page; no repository change | Reversible by the teardown in 4.6; jobs routed to a missing label queue until they time out, so routing must be withdrawn first | `git revert` per PR |
| **Who acts** | Owner account only | Owner (credential, runner group, settings), Home (account), CI owner, independent security reviewer | CI source owners |

## 4. Option (b) specification, if it is ever selected

This is the minimum safe shape. Nothing here is built or registered by this Delivery.

### 4.1 Scope and the fork-PR fence

- **Never reachable by a fork PR on a public repository.** For `pull_request` events the workflow file comes from the
  PR merge ref, so a fork can set any `runs-on` label. A label or an `if:` guard in YAML is therefore no fence. The
  fence must sit in GitHub's runner assignment:
  - **Private-only (b1).** Register repository-level runners on private repositories only, or use a runner group whose
    repository access lists only private repositories with "Allow public repositories" off. Fork workflows on private
    repositories stay disabled (organization setting, owner to confirm).
  - **Trusted workflows on public repositories (b2).** A runner group restricted to selected workflows pinned to
    `@refs/heads/main` (for example `FS-GG/FS.GG.Coordination/.github/workflows/<file>.yml@refs/heads/main`).
    `pull_request` runs use `refs/pull/<n>/merge` and cannot match, so this serves `push` to `main`, `schedule` and
    `workflow_dispatch` only. Same-repository PR work would need a `pull_request_target` workflow on `main` that
    builds only when `head.repo.full_name == github.repository`, with `permissions: contents: read` and no secrets.
    That is a separate, high-risk redesign and is not proposed.
- Custom runner groups and workflow restrictions are documented for paid organization plans. Whether this org can
  create them is Unknown (section 2.4). On a plan without them, only b1 is safe.
- The org's fork-PR approval policy for public repositories is defence in depth, not the fence. A human approval
  click cannot be the only barrier between a fork and fdev.

### 4.2 Job isolation

- **JIT runners only.** A supervisor mints one just-in-time runner configuration per job
  (`POST /orgs/FS-GG/actions/runners/generate-jitconfig`, or the repository equivalent for b1). The runner takes
  exactly one job and is deleted by GitHub. No persistent registration token exists on disk.
- **Each job in a fresh rootless podman container**, removed after the job: `--rm`, `--read-only` root with a tmpfs
  work directory, `--cap-drop=ALL`, `--security-opt=no-new-privileges`, `--userns=auto`, `--pids-limit`, and no
  host mounts, no podman socket and no device passthrough. The image is pinned by digest.
- **A dedicated account, not `developer`.** The supervisor and containers run as a new unprivileged account (for
  example `fsgg-ci-runner`) with no `sudo`, no group memberships and its own user manager, following the
  one-service-account-per-consumer pattern in Atlas `platform/shared-bounded-execution-route`. `developer` has
  passwordless `sudo` on fdev, so a container escape as `developer` would be root.
- **No access to fdev custody.** The account must not read `~developer/.config`, `~developer/.local/share/fs-gg-private`,
  `/srv` or any credential. Today `/home/developer` and `~/.config` are mode 0755 and five files under `~/.config`
  (including shell history) are world-readable; `fs-gg-private` is 0700. Prerequisite: `/home/developer` to 0750
  or 0700, checked by a readback as the runner account before registration. `/srv` stays unmounted in containers.
- **The JIT-minting credential stays outside containers.** It is a dedicated GitHub App or fine-grained token held by
  the runner account (mode 0600) with only `organization_self_hosted_runners: write`, never the coordination token
  and never passed to a job.
- **Network.** Containers get outbound internet only. Block RFC 1918, link-local and the Home host and its services
  with a podman network firewall rule; the guest's LAN is not a job resource.

### 4.3 Secrets and permissions in jobs

Workflows routed to fdev keep `permissions:` minimal and use no repository or organization secrets unless that
workflow's review admits them. `GITHUB_TOKEN` is per job and expires with it.

### 4.4 fdev capacity and the lane probe

- fdev has 12 cores and 31 GiB; MemAvailable varied 20-30 GiB with agents running (Atlas
  `environment/fdev-cgroup-lanes-and-io-delegation`). Agent lanes have priority.
- Containers run in a dedicated slice, for example `fsgg-ci.slice` under the runner account: `CPUQuota=400%`,
  `MemoryMax=10G`, `TasksMax=4096`, a low `CPUWeight` and `IOWeight`. At 2 cores and 5 GiB per job that is two
  concurrent jobs. `io.max` is not yet available in user slices (same Atlas record), so I/O isolation is weight only.
- **Admission.** Before minting each JIT config, the supervisor runs `scripts/fdev-lane-capacity.py` (5-minute load
  below 0.85 x nproc, MemAvailable at least 6 GiB, free disk at least 20 GB). On `hold` (exit 3) it mints nothing;
  running jobs finish. Jobs then wait in GitHub's queue or fall back as routed (4.5). The probe read `admit` at
  load5 4.64 during this measurement.
- The load average is host-wide, so CI load counts toward it and the probe throttles CI before agent lanes
  saturate. The probe also scans every process's argv, so builds inside CI containers appear in `heavy` for Root's
  own launches too.
- The probe derives `nproc` from its own cgroup's CPU limit. Run inside a 400 % slice it would grade the host-wide
  load against 4 CPUs and hold almost always (load5 was 4.64 here). The supervisor must run it outside the CI slice,
  or pass `--nproc` with the host count.

### 4.5 Routing

- Workflows opt in by label (for example `fsgg-fdev`) and only for jobs whose workload fits the 2-core, 5 GiB
  shape and needs no hosted-image tooling.
- Under b2, the selected-workflow list in the runner group is the authority; the label alone grants nothing.
- A label with no online runner queues jobs until GitHub's queue timeout. Teardown and probe holds must therefore not
  strand required contexts: route only non-required or `main`-ref work there, or withdraw the label first.

### 4.6 Teardown

1. Withdraw routing: revert the workflow label changes (or flip the controlling repository variable) and merge.
2. Stop the supervisor and disable its unit; wait for in-flight jobs to finish.
3. Delete remaining runners (`DELETE /orgs/FS-GG/actions/runners/{id}`), then the runner group.
4. Revoke the JIT-minting App or token.
5. As the runner account, remove containers, images and volumes; remove the slice and units; delete the account and
   its home directory. Read back that no `fsgg-ci-runner` process, unit or podman storage remains.
6. Record the teardown readback on the owning Delivery.

### 4.7 Before enabling

An independent security review of the exact supervisor, container command, slice and runner-group configuration, as
#5197's Done when requires. Then a canary: one `workflow_dispatch` job on a private repository, with a readback that
the job saw no host path, no credential and no private address.

## 5. Recommendation

**(a) together with (c). Do not build (b) now.**

1. **(a), owner action.** Confirm the plan on the billing page. If it is Free, move to Team: it triples the cap for
   every repository and event, adds no attack surface, and is reversible from the billing page. Today's demand
   saturates 20 with a 150-190-run backlog; whether it fits under 60 is measured in the Implement step.
2. **(c), CI owners, in parallel.** In order of slot share: Coordination `optimistic-parallel-validation.yml`
   (27.5 %, about 40 slot-minutes per PR run), `.github` `coord-engine.yml` (12.7 %), #5189's guaranteed
   failure-plus-edit run per push, and the remaining #5182 consolidation. These help under any plan.
3. **(b) only on re-entry.** Reconsider if, after (a) and the first (c) items, the p90 job queue wait stays above
   300 s across working hours for a week. Even then prefer b1 (private-only). Private repositories hold 4.1 % of slot
   time, so b1 adds little throughput; b2 on public repositories requires workflow-restricted runner groups and the
   full section 4, and covers only `main`-ref work.

If the owner declines (a), (c) is the only lever that relieves public-repository demand safely. b1 remains possible
but would move only about 4 % of the load.

## 6. Acceptance still open on #5197

- **Decide:** Root records the decision. Option (a) is an owner-account action.
- **Implement and measure:** before/after queue wait for the chosen option, using the job-record method in 2.1 and
  2.2, not run-level status.
- Any self-hosted route: independent security review before enabling (4.7).
