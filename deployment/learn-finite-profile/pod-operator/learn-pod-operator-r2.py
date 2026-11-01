#!/usr/bin/python3 -I
"""LEARN finite executor: POD OPERATOR r2 (design: docs/coordination/2026-10-30-learn-finite-executor-pod-design.md sections 2-8, 10 and 14.1;
Refs FS-GG/.github#4647, FS-GG/.github#5183). r2 = the gate layer r1 (d34f812d) plus the START: pins, the loop-volume and container prechecks,
the finalizer and pod units, the containers, the recovery driver and the per-run credential generator. Runs ONLY as uid 30034 (fsgg-learn-qual):
    learn-pod-operator-r2.py --precheck-only|--run --id <op-id> --argv-json '["learn-pod-plan/1","<plan sha256>"]' --source-identity <64 hex>
    learn-pod-operator-r2.py --drive --id <op-id> --source-identity <64 hex> [--input synthetic]   (internal: the main process of learn-pod-<id>.service)
#5184 adds the SYNTHETIC input kind (the pod harness, the install receipt op): the argv-json names it (synthetic_plan_argv: the plan argv plus
"input:synthetic", bound by the admission), the claim and the sealed snapshot are read from synthetic-claims/ and synthetic-inputs/ with the seal tool's
synthetic record types, and the finalizer and the driver are started with --input synthetic. The RESULT records inputProvenance.

Gates (the first refusal wins; nothing is started or written before the lock except the RESULT on stdout). 1-10 are r1's, unchanged in effect:
  1 identity    euid 30034; exact arguments; argv-json names THIS file's plan by its sha256; the app and pg image Ids and the finalizer sha256 are pinned
                here (the image Ids are admitted only after the build and the pull, so they are empty in this revision: refuse).
  2 pins        each image present locally by Id; the finalizer hashes to its pin through a pinned descriptor chain; the seccomp profile hashes to its name.
  3 slice       live user-30034.slice limits exactly 6 GiB and 1280 pids.     4 lock     the SHARED operator lock (operator r6's), held to exit.
  5 exclusion   any learn-op-*/learn-pod-* unit active or transitioning, a failed learn-fin-*, a leftover learn-pod-*/learn-pre-* pod, an orphan container.
  6 custody     an existing custody, evidence, checkpoint, drive or credential directory for the id (or <id>-pre), or a unit named for the id.
  7 acks        operator r6's retirement gate, verbatim.          8 evidence   operator r6's evidence-size refusal, verbatim.
  9 admission   learn_seal_claim.py's reader, verbatim: argv hash, source identity, this file's sha256, the finalizer pin and the image Ids.
 10 headroom    slice memory headroom >= 4.5 GiB, pids >= 1000, MemAvailable >= 4 GiB, custody disk >= 1 GiB.
 11 plan        (checked with 1, as it is static) the plan is complete (no HELD step) and keeps its invariants (validate_plan): at most 8
                checkpoints, all three role slots running at every checkpoint, no container removed or killed before a checkpoint holds its
                state, never all three roles done before the end, exactly one generation with /in, pg running and every role done at the end.
 12 volume      the loop volume <volume_root>/<id>.mnt: ONE ext4 mount from a loop device with rw,nosuid,nodev,noexec; between 900 MiB and 1 GiB;
                fresh (at most 16 inodes and 64 MiB in use); root-owned top directory closed to group and other writers holding exactly pgdata
                (428678:428678 0700) and recv (428679:428679 0700) and at most lost+found, on the same filesystem.
 13 containers  the PRECHECK pod learn-pre-<id>: every container started with the run's own options (cgroup mode aside) and a probe command, then
                asserted: start succeeded (EPERM and ENOSYS named), uid/gid, five capability sets 0, NoNewPrivs 1, tmpfs owners, only `lo` and no
                route, the socket probe (AF_VSOCK errno from the profile), `setsid -w true` in the role image (the step runner needs it), the host uid of the payload (pg 428678, apps 428679, never 0 or 30034),
                CapAdd empty, read-only root, memory and pids limits, the seccomp path, and OCI seccomp rule-set equality (operator r6's
                expected_set/effective_set, verbatim). The precheck pod is removed and its removal read back.
--precheck-only exits 0 when 1-13 hold (the RESULT shows inputMounted false and no container with /in), so Root may take the claim from it.
--run adds: census again; claim (names this op and source identity; manifest sha matches); nonce (runNonceSha256 before anything starts);
            begin_input_access (verbatim r1): the input-read marker, then gates.inputMounted, then the START: the finalizer unit FIRST, then the pod
            unit (BindsTo/After the finalizer), whose main process is this file in --drive mode. The RESULT lists the containers the driver will create.

--drive (the RECOVERY DRIVER and the section 5 sequence). Authority: it runs only as uid 30034 inside learn-pod-<id>.service, only while
learn-fin-<id>.service is active, only if the admission for <id> names THIS file's sha256, the input-read marker for <id> exists and the live
claim names <id>. It acts only on this op's pod and containers, through a closed set of actions (PLAN_ACTIONS). Readback custody: before it
stops-and-removes or kills a role container, the finalizer must have archived that container's /out/<role> through the checkpoint handshake
(req-<n> -> archive -> ack-<n>), and the driver reads the ack and the extracted copy in custody back before it acts. Every action goes to the
hash-chained ~/.local/state/learn-drive/<id>/drive.jsonl and to the journal (FSGG <nonce> DRIVE ...). On any failure it aborts: it never removes
evidence, replaces only a role container that no longer runs, fills an empty role slot with a hold container and TERMs each role that is not yet
done (a done role keeps its /out for the archive; a second TERM would lose it), so the finalizer triggers, archives and retires. An abort before the
replay leaves feed `signalled`, which finalizer r5 never counts as a success; after the replay it can, so the drive log and the pod unit's
ExecMainStatus are part of Root's readback (the success rule is #5175's). The driver then waits for the finalizer and leaves only on a definite
state (pod gone, or the finalizer unit inactive or failed), bounded by the finalizer's RuntimeMaxSec; every exception lands in that same tail.

One-shot commands (the Host's section 5 init/verify, the receiver's init --root and enroll-producer, the producer's activate/drain): no podman exec.
Each role container runs the role entrypoint with ONE command, the step runner below (STEP_RUNNER, pinned here): it runs an ordered step list,
writes each step's stdout, stderr and exit code under /out/<role>/steps/<k>-<name>/ (checked writes), runs each step in its own session and process
group (setsid) and stops that whole group on TERM, stops at the first failure, and may end by exec'ing the role's service. /out/<role> is what finalizer r5 archives, so one-shot evidence reaches custody by the finalizer's own route, unchanged.

Credentials: generated by the driver in the per-run tmpfs <cred_root>/<id>/ (CSPRNG via `secrets`, O_EXCL 0600, never printed or passed in argv;
an ephemeral CA and server PFX through openssl with the password read from a file; private keys removed after use); one directory per generation
holding only the files that generation names, handed to the container uid with `podman unshare chown` and read back (owner, group, mode) inside the
user namespace, bind-mounted read-only at /cred (a generation without credentials mounts none); removed by the driver at the end and by the pod
unit's ExecStopPost. No podman secret is created (podman's file driver keeps secrets on disk, not in tmpfs).

Verbatim blocks are checked byte for byte against learn_seal_claim.py, operator r6 and pod operator r1 by tests/learn-pod-operator. Cooperative:
like the claim and the admission, it detects mistakes and does not defend against Root.
"""
import fcntl, hashlib, json, os, re, secrets, stat, subprocess, sys, time
from datetime import datetime, timedelta, timezone

SCHEMA = 'home-learn-pod-operator/1'
REVISION = 'r2'
MAX_TTL_S = 600
OPID = re.compile(r'^[a-z0-9][a-z0-9-]{2,58}$')
HEX64 = re.compile(r'^[0-9a-f]{64}$')
POD_MEMORY_HEADROOM, POD_PIDS_HEADROOM, POD_MEMAVAILABLE = int(4.5 * (1 << 30)), 1000, 4 << 30
SLICE_MEMORY_MAX, SLICE_PIDS_MAX = 6 << 30, 1280
TRANSITIONING = ('active', 'activating', 'deactivating', 'reloading', 'refreshing', 'failed')
PLAN_SCHEMA = 'learn-pod-plan/1'
ROLES = ('host', 'recv', 'feed')                         # the slots finalizer r5 archives; pg has no /out
SLOTS = ('pg',) + ROLES
MAX_CHECKPOINTS = 8                                      # finalizer r5's cap
HOST_UID = {'pg': 428678, 'app': 428679}                 # container 999 and 1000 through the account's subuid range (design section 3)
PG_PORT, HOST_PORT, RUNNER_PORT, RECV_PORT = 5432, 5108, 5109, 5443
DECLARED_PORTS = {PG_PORT, HOST_PORT, RUNNER_PORT, RECV_PORT}
REQUIRED_PORTS = {PG_PORT, HOST_PORT, RECV_PORT}
VOLUME_CAP, VOLUME_MIN, VOLUME_FRESH_BYTES, VOLUME_FRESH_INODES = 1 << 30, 900 << 20, 64 << 20, 16
WS, PRODUCER, STREAM = 'utel-persistent-20261018', 'codex-persistent-20261018', 'native-persistent-20261018'   # the one drainable identity (design 2.3)
REPLAY_SUBTREE = 'passive-prospective-0106-20261018/spool'
FIN_RUNTIME_S = 1980                                     # finalizer r5's unit RuntimeMaxSec
DRIVE_BUDGET_S = 1500                                    # the plan must end before finalizer r5's soft deadline (1800 s after the pod unit is active)
LIMITS = {'pg': ('1024m', '200', None), 'host': ('1024m', '300', '0x30000000'), 'recv': ('768m', '150', '0x24000000'), 'feed': ('256m', '50', '0xC000000')}


class Held(tuple):
    """A plan value that is not yet decided; a plan holding one cannot run."""
    def __new__(cls, why):
        return tuple.__new__(cls, ('HELD', why))


DOTNET = '/usr/share/dotnet/dotnet'
TH = [DOTNET, '/opt/fsgg/telemetry-host/FS.GG.Telemetry.Host.dll']
HOSTX = '/opt/fsgg/orchestration/host/fsgg-coord-orchestration-host'
HOLD = ['/bin/sh', '-c', 'while :; do sleep 3600; done']
RECV_SERVE = TH + ['serve', '--config', '/cred/host-config.json']
WAIT_PG = ('wait-pg', ['wait-tcp', str(PG_PORT), '120'])
# #5184: the plan values resolved from the pinned sources (Host: FS.GG.Coordination 5d525ad2; producer: FS.GG.Coord.Cli 0.113.0 = FS-GG/.github 79db622f, tag
# coherent-set/v0.113.0). Citations and the reasoning are in the README ("The eight plan values"). Every identity below is a fixed per-plan constant
# (fixture provenance); none is a secret. The backup identity is the one value init creates at random: it is chained from init's own stdout.
HOST_SOURCE_REVISION = '5d525ad2423e733a38ce75fb1f9cac6cf76f214e'
HOST_SHA256 = 'b3d6a6e664cf55ad25a110daa3bf1578a6f6210755e22857f5bddff139877c47'          # the matched pair's Host (pod-image context, INSTALL-FDEV)
STORE_ID = 'learn-pod-qualification-store'
FIXTURE_IDS = {'permit': 'c5d6f4a8-3e1b-4f0a-9b2c-7d8e9f0a1b2c', 'principal': 'learn-pod-pilot', 'repoNode': 'R_learn_pod', 'repoDb': '8201',
               'issueNode': 'I_learn_pod', 'issueDb': '9201'}
INIT_STDOUT = '/out/host/steps/02-init/stdout'          # the step runner's own path for host-init's second step (wait-pg is first); the tests run the runner to prove it
# verify-installed-adoption needs a private per-run request (fsgg.orchestration.installed-adoption-request/1: exact field set, a 10-minute window, the
# backup identity init just created). Built inside the container from init's archived stdout, which must be exactly the host-init/1 object; it fails closed.
# What init prints (Host 5d525ad2, src/FS.GG.Coordination.Orchestration.Host/Program.fs:52-58): System.Text.Json of an F# ANONYMOUS record, whose fields
# serialize in alphabetical order, compact: {"backupIdentity":"<uuid>","schema":"fsgg.orchestration.host-init/1"}. The check below does not depend on the
# key order: the whole line must be that object with exactly those two keys in either order, so it holds exactly one UUID: the identity.
VERIFY_REQUEST_SH = r'''set -eu
umask 077
j=$(cat "$1")
[ "$(printf '%s\n' "$j" | wc -l)" -eq 1 ] || { echo 'init stdout is not exactly one line' >&2; exit 65; }
u='"backupIdentity":"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"'
k='"schema":"fsgg\.orchestration\.host-init/1"'
printf '%s\n' "$j" | grep -Eqx "\\{($u,$k|$k,$u)\\}" || { echo 'init stdout is not exactly one host-init/1 object' >&2; exit 65; }
b=$(printf '%s\n' "$j" | grep -Eo '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}')
o=$(cat /proc/sys/kernel/random/uuid)
printf '%s\n' "$o" | grep -Eqx '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}' || exit 65
t=$(date -u +%s)
i=$(date -u -d "@$t" +%Y-%m-%dT%H:%M:%SZ)
e=$(date -u -d "@$((t + 540))" +%Y-%m-%dT%H:%M:%SZ)
printf '%s' "$b" > /work/backup-identity
printf '{"schema":"fsgg.orchestration.installed-adoption-request/1","operationId":"%s","expectedSourceRevision":"%s","expectedExecutableSha256":"%s","storeId":"%s","expectedBackupIdentity":"%s","expectedSchemaVersion":2,"expectedGenerationFence":0,"issuedAt":"%s","expiresAt":"%s","ordinaryCapacity":1,"recoveryCapacityPerProject":1,"projectA":{"projectId":"f73401b4-b65d-4688-a960-a5de59b8f830","repositoryNodeId":"R_project_a","repositoryDatabaseId":8101,"issueNodeId":"I_subject_a","issueDatabaseId":9101},"projectB":{"projectId":"1e2807c3-8669-4bf3-baf2-c184532b3ea4","repositoryNodeId":"R_project_b","repositoryDatabaseId":8102,"issueNodeId":"I_subject_b","issueDatabaseId":9102}}' "$o" "$2" "$3" "$4" "$b" "$i" "$e" > /work/verify-request.json
'''
# serve without GitHub settings: the only mode Host 5d525ad2 runs with no egress. Runner roots, the fixture executor binding and the telemetry options are
# accepted only WITH GitHub settings (local-executor-requires-github-main-configuration), so this serve admits no main work (see HOST_NO_EGRESS).
SERVE_ARGS = ['--connection-file', '/cred/pg.connection', '--token-file', '/cred/operator.token', '--runner-token-file', '/cred/runner.token',
              '--prefix', f'http://127.0.0.1:{HOST_PORT}/', '--store-id', STORE_ID, '--minimum-generation-fence', '0', '--permit-id', FIXTURE_IDS['permit'],
              '--pilot-principal', FIXTURE_IDS['principal'], '--repository-node-id', FIXTURE_IDS['repoNode'], '--repository-database-id', FIXTURE_IDS['repoDb'],
              '--issue-node-id', FIXTURE_IDS['issueNode'], '--issue-database-id', FIXTURE_IDS['issueDb']]
HOST_SERVE = ['/bin/sh', '-c', 'b=$(cat /work/backup-identity) && [ -n "$b" ] || exit 70; exec "$@" --backup-identity "$b"', 'serve', HOSTX, 'serve'] + SERVE_ARGS
# Root ruling (#5184, issuecomment-6559191767): the finite pod plan carries NO Host assignment step. Host 5d525ad2 admits and resumes main work only with
# GitHub settings, and the pod has no egress beyond the model API (decision 1, #5068), so prepare-main-admission and resume are not run in the pod: main-work
# assignment and resume stay host-side. feed-admin therefore only waits for the Host and holds its slot (no credentials).
# host-restart's serve needs the backup identity host-init created, across containers (/work is per container; a second init refuses a schema-2 store).
# The plan names it by BID_REF; the DRIVER substitutes it at the start, from host-init's init stdout that a checkpoint put in custody (validate_plan
# requires such a checkpoint before any start that uses it). It is a store identity, not a secret.
BID_REF = '%host-init-backup-identity%'
INIT_STDOUT_REL = INIT_STDOUT[len('/out/host/'):]
UUID = re.compile(r'[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}')
INIT_SCHEMA = 'fsgg.orchestration.host-init/1'
# the producer association (activate-remote) and its binding digest: sha256 over the int32-LE-length-prefixed UTF-8 of [workspace, producer, stream,
# 'remote', endpoint AbsoluteUri, credential reference, spool root] + repositories (WorkspaceTelemetryApplication.associationDigest). The tests recompute it.
SPOOL_ROOT = '/work/spool/' + 'passive-prospective-0106-20261018/spool'
TELEMETRY_CONFIG, PRODUCER_REPOSITORY = '/work/telemetry.json', 'FS-GG/.github'
PRODUCER_CLI = [DOTNET, '/opt/fsgg/producer/fsgg-coord-engine.dll']
BINDING_DIGEST = '92e0e70d86bdfc4611d229e07ce9c54092ad55bb2dd21fd1eb3a53d6b9a810bf'

# Each generation is ONE container in one slot. creds: the files of the per-run credential set the slot's /cred holds. A generation without a
# service ends when its steps end (role writes the terminal status and .done); with one, the runner execs it after the steps.
GENERATIONS = {
    'pg': {'slot': 'pg', 'creds': ['pg-password']},
    'recv-init': {'slot': 'recv', 'creds': ['server.pfx', 'server.password', 'collector.secret', 'host-config.json', 'browser-key-hash.json', 'ca.pem'], 'volume': True,
                  'steps': [('init', TH + ['init', '--root', '/recv/store', '--workspace', WS]),
                            ('enroll', TH + ['enroll-producer', '--config', '/cred/host-config.json', '--reference', 'collector', '--secret-file', '/cred/collector.secret',
                                             '--workspace', WS, '--producer', PRODUCER, '--stream', STREAM])],
                  'service': RECV_SERVE, 'ready': ['--ready-tcp', str(RECV_PORT), '--ready-timeout', '600']},
    'recv-restart': {'slot': 'recv', 'creds': ['server.pfx', 'server.password', 'collector.secret', 'host-config.json', 'browser-key-hash.json', 'ca.pem'], 'volume': True,
                     'steps': [], 'service': RECV_SERVE, 'ready': ['--ready-tcp', str(RECV_PORT), '--ready-timeout', '600']},
    'host-init': {'slot': 'host', 'creds': ['pg.connection', 'operator.token', 'runner.token', 'ca.pem'],
                  'steps': [WAIT_PG, ('init', [HOSTX, 'init', '--connection-file', '/cred/pg.connection']),
                            ('request', ['/bin/sh', '-c', VERIFY_REQUEST_SH, 'verify-request', INIT_STDOUT, HOST_SOURCE_REVISION, HOST_SHA256, STORE_ID]),
                            ('verify', [HOSTX, 'verify-installed-adoption', '--connection-file', '/cred/pg.connection', '--request-file', '/work/verify-request.json'])],
                  'service': HOST_SERVE, 'ready': ['--ready-tcp', str(HOST_PORT), '--ready-timeout', '600']},
    # #5184: no verify after a restart: verify-installed-adoption refuses any store that is not empty (dedicated-store-not-empty), and init's verify
    # and serve's startup pause have written rows by then. WAIT_PG and the readiness probe are what a restart can check.
    'host-restart': {'slot': 'host', 'creds': ['pg.connection', 'operator.token', 'runner.token', 'ca.pem'],
                     'steps': [WAIT_PG], 'service': [HOSTX, 'serve'] + SERVE_ARGS + ['--backup-identity', BID_REF],
                     'ready': ['--ready-tcp', str(HOST_PORT), '--ready-timeout', '600']},
    'feed-admin': {'slot': 'feed', 'creds': [],
                   'steps': [('wait-host', ['wait-tcp', str(HOST_PORT), '300'])],
                   'service': HOLD, 'ready': ['--ready-file', '/out/feed/steps/complete', '--ready-timeout', '600']},
    'feed-replay': {'slot': 'feed', 'creds': ['ca.pem', 'collector.secret', 'spool.sha256'], 'input': True,
                    'steps': [('copy', ['/bin/cp', '-R', '--preserve=mode,timestamps', '/in/.', '/work/spool']),
                              ('verify', ['/usr/bin/sha256sum', '--check', '--strict', '--quiet', '/cred/spool.sha256']),
                              # activate-remote needs the spool root 0700 under a private parent and drain needs *.ready at 0600; the seal makes them 0755/0644
                              ('private', ['/bin/chmod', '-R', 'u=rwX,go=', '/work/spool']),
                              ('credential', ['env-file', 'FSGG_TELEMETRY_CREDENTIAL_COLLECTOR', '/cred/collector.secret']),
                              ('activate', PRODUCER_CLI + ['telemetry', 'workspace', 'activate-remote', '--config', TELEMETRY_CONFIG, '--workspace', WS,
                                                           '--producer', PRODUCER, '--stream', STREAM, '--repository', PRODUCER_REPOSITORY,
                                                           '--endpoint', f'https://localhost:{RECV_PORT}', '--credential-reference', 'collector',
                                                           '--spool-root', SPOOL_ROOT]),
                              ('drain', PRODUCER_CLI + ['telemetry', 'workspace', 'drain', '--config', TELEMETRY_CONFIG, '--repository', PRODUCER_REPOSITORY,
                                                        '--binding-digest', BINDING_DIGEST])],
                    'service': None},
    'hold': {'slot': '*', 'creds': [], 'steps': [], 'service': HOLD, 'ready': []},
}

# The section 5 sequence and the section 6 recovery cases, in order. Actions are a closed set (PLAN_ACTIONS); validate_plan checks the invariants.
PLAN = {
    'schema': PLAN_SCHEMA,
    'generations': GENERATIONS,
    'actions': [
        ['start', 'pg'], ['wait-listen', 'pg', PG_PORT],
        ['start', 'recv-init'], ['start', 'host-init'], ['wait-ready', 'recv'], ['wait-ready', 'host'],
        ['start', 'feed-admin'], ['wait-ready', 'feed'], ['assert-listeners'], ['assert-cgroups'],
        ['checkpoint', 'admitted'],
        # 6 clean restart: TERM host, INT pg (fast shutdown), TERM recv; custody holds their terminal state before any is removed; start pg, recv, host again
        ['stop', 'host'], ['stop', 'pg'], ['stop', 'recv'], ['checkpoint', 'clean-restart'],
        ['remove', 'host'], ['remove', 'recv'], ['restart', 'pg'], ['wait-listen', 'pg', PG_PORT],
        ['start', 'recv-restart'], ['start', 'host-restart'], ['wait-ready', 'recv'], ['wait-ready', 'host'], ['assert-listeners'],
        # 6 SIGKILL of host (and its Runner child) during the invocation: restart reconciliation, not orphan detection (no shared PID namespace)
        ['checkpoint', 'pre-kill-host'], ['kill', 'host'], ['reconcile', 'host'], ['remove', 'host'], ['start', 'host-restart'], ['wait-ready', 'host'],
        # 6 SIGKILL of pg during the invocation; host must refuse and reconcile after pg restarts (pg has no /out, nothing to archive first)
        ['kill', 'pg'], ['reconcile', 'pg'], ['restart', 'pg'], ['wait-listen', 'pg', PG_PORT], ['wait-ready', 'host'],
        # 6 receiver down while host emits; the outbox must drain once after the restart
        ['stop', 'recv'], ['sleep', 30], ['checkpoint', 'recv-down'], ['remove', 'recv'], ['start', 'recv-restart'], ['wait-ready', 'recv'], ['assert-listeners'],
        # 5.6 the replay: the only container with /in
        ['checkpoint', 'pre-replay'], ['remove', 'feed'], ['start', 'feed-replay'], ['wait-done', 'feed'],
        # the end: every role done, so finalizer r5 triggers, archives, stops pg with SIGINT and retires
        ['stop', 'host'], ['stop', 'recv'],
    ],
}
PLAN_ACTIONS = ('start', 'wait-listen', 'wait-ready', 'wait-done', 'assert-listeners', 'assert-cgroups', 'checkpoint', 'stop', 'kill', 'reconcile', 'remove',
                'restart', 'sleep')

# The ONE command of every role container: an ordered step list, then optionally the service (exec). Arguments are passed as positional parameters
# (<role> then, per step, <name> <argc> <argv...>); eval only ever sees "${N}" references built from integers, never a value. wait-tcp and env-file
# are runner built-ins: a LISTEN socket on a port of the pod's network namespace, and an FSGG_TELEMETRY_CREDENTIAL_* variable read from a file.
STEP_RUNNER = r'''set -u
r=$1; shift
case $r in host|recv|feed) ;; *) exit 64 ;; esac
d=/out/$r/steps
mkdir "$d" || exit 70
k=0; o=; sp=
x='printf "%s\n" "$$" > "$0/pgid" || exit 70; exec "$@"'
stop_step() {
  [ -n "$sp" ] || return 0
  g=; [ -r "$o/pgid" ] && g=$(cat "$o/pgid")
  if [ -n "$g" ]; then kill -TERM -"$g" 2> /dev/null; else kill -TERM "$sp" 2> /dev/null; fi
  i=0
  while [ "$i" -lt 75 ] && { { [ -n "$g" ] && kill -0 -"$g" 2> /dev/null; } || kill -0 "$sp" 2> /dev/null; }; do sleep 0.2; i=$((i + 1)); done
  [ -z "$g" ] || kill -KILL -"$g" 2> /dev/null
  return 0
}
trap 'stop_step; exit 143' TERM INT
while [ "$#" -gt 0 ]; do
  [ "$#" -ge 2 ] || exit 64
  name=$1; c=$2; shift 2
  case $name in ''|*[!a-z0-9-]*) exit 64 ;; esac
  case $c in ''|*[!0-9]*) exit 64 ;; esac
  [ "$c" -ge 1 ] && [ "$c" -le "$#" ] || exit 64
  refs=; i=1
  while [ "$i" -le "$c" ]; do refs="$refs \"\${$i}\""; i=$((i + 1)); done
  if [ "$name" = service ]; then
    [ "$c" -eq "$#" ] || exit 64
    printf '' > "$d/complete" || exit 70
    trap - TERM INT
    eval "exec $refs < /dev/null"
  fi
  k=$((k + 1)); o=$d/$(printf '%02d' "$k")-$name
  mkdir "$o" || exit 70
  case $1 in
    wait-tcp)
      [ "$c" -eq 3 ] || exit 64
      case $2 in ''|*[!0-9]*) exit 64 ;; esac
      case $3 in ''|*[!0-9]*) exit 64 ;; esac
      p=$(printf '%04X' "$2"); n=0; rc=1
      while [ "$n" -lt "$3" ]; do
        if grep -Eq ":$p [0-9A-F]+:0000 0A " /proc/net/tcp /proc/net/tcp6 2> /dev/null; then rc=0; break; fi
        sleep 1; n=$((n + 1))
      done ;;
    env-file)
      [ "$c" -eq 3 ] || exit 64
      case $2 in FSGG_TELEMETRY_CREDENTIAL_*) ;; *) exit 64 ;; esac
      case $2 in *[!A-Z0-9_]*) exit 64 ;; esac
      v=$(cat "$3") && export "$2=$v"; rc=$?; v= ;;
    *)
      eval "setsid -w /bin/sh -c \"\$x\" \"\$o\" $refs" < /dev/null > "$o/stdout" 2> "$o/stderr" &
      sp=$!
      wait "$sp"; rc=$?; sp= ;;
  esac
  printf '%s\n' "$rc" > "$o/rc" || exit 70
  shift "$c"
  [ "$rc" -eq 0 ] || exit "$rc"
done
printf '' > "$d/complete" || exit 70
'''

# The precheck probe (no input, no credentials, no volume): what the container sees, printed for the operator to read from the container log.
PROBE_SH = r'''k=$1; e=$2; shift 2
exec 2>&1
echo "ID $(id -u) $(id -g)"
while read -r a b _; do case $a in Cap*:|NoNewPrivs:) echo "STATUS ${a%:} $b" ;; esac; done < /proc/self/status
for p in "$@"; do echo "OWN $(stat -c %u "$p") $p"; done
while read -r l; do case $l in *:*) echo "IF ${l%%:*}" ;; esac; done < /proc/net/dev
n=0; while read -r i _; do [ "$i" = Iface ] || n=$((n + 1)); done < /proc/net/route; echo "ROUTE4 $n"
n=0; while read -r _ _ _ _ _ _ _ _ _ i; do [ "$i" = lo ] || n=$((n + 1)); done < /proc/net/ipv6_route; echo "ROUTE6 $n"
if [ "$k" = app ]; then /opt/fsgg/pod/probe "$e"; if setsid -w true < /dev/null > /dev/null 2>&1; then echo "SETSID ok"; else echo "SETSID absent"; fi; fi
echo END
if [ "$k" = pg ]; then exec sleep 120; fi
'''
class Refuse(Exception):
    pass


def refuse(why):
    raise Refuse(why)




PROD = {
    'uid': 30034, 'owner_uid': 0, 'chain_start': '/',
    'home': '/mnt/storage/containers/learn-qualification/account',
    'view': '/usr/local/libexec/fsgg-learn',
    'slice': '/sys/fs/cgroup/user.slice/user-30034.slice',
    'meminfo': '/proc/meminfo',
    'fin': '/usr/local/libexec/fsgg-learn/learn-finalizer-r5.py',
    'finalizer_sha': 'c1de5c03598e6f1e315de7f5d55ce394f35b0880991c28467410420a042556d7',    # finalizer r5 with the #5184 synthetic-input declaration
    'app_image': None,              # learn-pod-app:r1 config Id: admitted after the build (INSTALL-FDEV step 7); None refuses
    'pg_image': None,               # postgres:18.6 local config Id: admitted after the pull by index digest PG_INDEX_DIGEST; None refuses
    'self': '/usr/local/libexec/fsgg-learn/learn-pod-operator-r2.py',
    'python': '/usr/bin/python3',
    'seccomp': '/etc/fsgg/learn/seccomp-8bd52f8d.json', 'seccomp_sha': '8bd52f8d249b79693751ceba153038d6b4fb7b507e6ab16133a0cb207ca1e835',
    'volume_root': '/mnt/storage/containers/learn-qualification/learn-pod',      # finalizer r5's VOLUME_ROOT
    'cred_root': '/run/user/30034/learn-cred',                                   # finalizer r5's credential tmpfs root
    'proc': '/proc', 'cgroot': '/sys/fs/cgroup', 'mountinfo': '/proc/self/mountinfo', 'self_cgroup': '/proc/self/cgroup',
    'unshare': ['podman', 'unshare'], 'openssl': '/usr/bin/openssl', 'host_uid': dict(HOST_UID),
    'lock_wait_s': 120,
    'plan': PLAN,
}
PG_INDEX_DIGEST = 'sha256:4ef4dbc939d61acea57712655ddb4b4ab27419c913f94cca0cd57cb3ea3c2280'      # design 2.1; recorded, the config Id is the pin


# #5184: the input kinds. 'real' is the original reading; 'synthetic' is the seal tool's synthetic mode (learn_seal_claim.py --mode synthetic), selected
# ONLY by the admitted argv-json (synthetic_plan_argv) and, for the driver and the finalizer, by an explicit `--input synthetic`. Same directories, record
# types and provenance fields as the seal tool and finalizer r5's INPUT_KINDS.
INPUT_KINDS = {'real': {'claims': 'claims', 'claim': 'learn-input-claim/1', 'inputs': 'inputs', 'sealed': 'learn-input-snapshot/1'},
               'synthetic': {'claims': 'synthetic-claims', 'claim': 'learn-input-claim-synthetic/1', 'inputs': 'synthetic-inputs', 'sealed': 'learn-input-snapshot-synthetic/1'}}
SYNTHETIC_TAG = 'input:synthetic'


def input_kind(env):
    k = env.get('input', 'real')
    if k not in INPUT_KINDS:
        refuse('unknown input kind')
    return k


def paths(env):
    st = f"{env['home']}/.local/state"
    k = INPUT_KINDS[input_kind(env)]
    return {'lock': f'{st}/learn-operator.lock', 'custody': f'{st}/learn-custody', 'evidence': f'{st}/learn-evidence', 'markers': f'{st}/learn-input',
            'checkpoints': f'{st}/learn-checkpoint', 'drive': f'{st}/learn-drive',
            'ack_dir': f"{env['view']}/retirement-acks", 'claims': f"{env['view']}/{k['claims']}", 'inputs': f"{env['view']}/{k['inputs']}"}


def image_ids(env):
    return [i for i in (env.get('app_image'), env.get('pg_image')) if i]

# ============================================================ VERBATIM from deployment/learn-finite-profile/seal/learn_seal_claim.py
DIR_FLAGS = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC


ADMIT_FIELDS = ('opId', 'argvJsonSha256', 'sourceIdentity', 'operatorSha256', 'finalizerSha256', 'imageIds')


def _check_dir_fd(fd, owner_uid, label):
    st = os.fstat(fd)
    if not stat.S_ISDIR(st.st_mode) or st.st_uid != owner_uid or (stat.S_IMODE(st.st_mode) & 0o022):
        os.close(fd)
        refuse(f'write path not root-safe at {label}')
    return st


def open_dir_chain(path, start, owner_uid=0):
    """Open `path` as a directory descriptor by walking from `start` with O_DIRECTORY|O_NOFOLLOW and openat. Every directory
    opened (start included) is fstat-checked on its descriptor: owner, directory, not group/other-writable. A symlink or a
    missing component refuses. Returns the final descriptor; intermediate descriptors are closed."""
    if start != '/' and not (path == start or path.startswith(start + '/')):
        refuse(f'write path not root-safe at {path}: outside the chain start {start}')
    try:
        fd = os.open(start, DIR_FLAGS)
    except OSError as e:
        refuse(f'write path not root-safe at {start}: {e.strerror}')
    _check_dir_fd(fd, owner_uid, start)
    cur = start
    for comp in [p for p in path[len(start):].split('/') if p]:
        cur = cur.rstrip('/') + '/' + comp
        try:
            nfd = os.open(comp, DIR_FLAGS, dir_fd=fd)
        except OSError as e:
            os.close(fd)
            refuse(f'write path not root-safe at {cur}: {e.strerror}')
        os.close(fd)
        fd = nfd
        _check_dir_fd(fd, owner_uid, cur)
    return fd


def check_admission(path_root, op_id, argv_sha, ident, operator_sha, finalizer_sha, image_ids, chain_start, owner_uid=0, now=None):
    """Read-only reader side; returns [] when the admission matches, else the reasons. The pod operator is a separate
    single-file script run with -I, so it cannot import this module: it must carry this logic verbatim. The test vectors in
    tests/learn-seal-claim are the conformance set for that copy, and this CLI subcommand needs no root (it only reads)."""
    try:
        dfd = open_dir_chain(f'{path_root}/admissions', chain_start, owner_uid)
    except Refuse as e:
        return [str(e).replace('write path', 'admission path')] if 'not root-safe' in str(e) else ['admission is absent or unreadable']
    except OSError:
        return ['admission is absent or unreadable']
    try:
        try:
            fd = os.open(f'{op_id}.admit', os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=dfd)
        except OSError:
            return ['admission is absent or unreadable']
        with os.fdopen(fd, 'rb') as f:
            st = os.fstat(f.fileno())
            if not stat.S_ISREG(st.st_mode) or st.st_uid != owner_uid or (stat.S_IMODE(st.st_mode) & 0o022) or st.st_size > 65536:
                return ['admission path not root-safe: not a root-owned regular file closed to group and other writers']
            try:
                a = json.loads(f.read())
            except ValueError:
                return ['admission is absent or unreadable']
    finally:
        os.close(dfd)
    why = []
    want = {'opId': op_id, 'argvJsonSha256': argv_sha, 'sourceIdentity': ident, 'operatorSha256': operator_sha,
            'finalizerSha256': finalizer_sha, 'imageIds': sorted(image_ids)}
    for k in ADMIT_FIELDS:
        if a.get(k) != want[k]:
            why.append(f'{k} differs')
    now = now or datetime.now(timezone.utc)
    try:
        issued, expires = datetime.fromisoformat(a['issuedUtc']), datetime.fromisoformat(a['expiresUtc'])
    except (KeyError, ValueError, TypeError):
        return why + ['the admission has no valid issue or expiry time']
    if issued.tzinfo is None or expires.tzinfo is None:
        why.append('the admission times carry no time zone')
    else:
        if expires <= issued:
            why.append('the admission expires before it was issued')
        if (expires - issued).total_seconds() > MAX_TTL_S:
            why.append(f'the admission lifetime exceeds {MAX_TTL_S} seconds')
        if issued > now:
            why.append('the admission is dated in the future')
        if now > expires:
            why.append('the admission has expired')
    return why


# ============================================================ VERBATIM from deployment/learn-finite-profile/operator/learn-operator-r6.py
EVID_MAX_BYTES, EVID_MAX_ENTRIES = 32 << 30, 1 << 20


def read_record_nofollow(path, cap=16 << 20):
    """r5: bytes of a record file opened O_NOFOLLOW (regular, <= cap), else None"""
    try: fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
    except OSError: return None
    try:
        st = os.fstat(fd)
        if not stat.S_ISREG(st.st_mode) or st.st_size > cap: return None
        b = b''
        while len(b) <= cap:
            c = os.read(fd, 1 << 20)
            if not c: break
            b += c
        return b if len(b) <= cap else None
    finally: os.close(fd)


def evidence_usage(root):
    """r5 (3): allocated bytes (st_blocks * 512) of every file under the evidence root, no symlinks followed; stops early once
    over EVID_MAX_BYTES or EVID_MAX_ENTRIES entries (then 'bounded': True)"""
    total, n, stack = 0, 0, [root]
    while stack:
        d = stack.pop()
        try: it = list(os.scandir(d))
        except OSError: return {'bytes': total, 'entries': n, 'error': 'unreadable ' + d}
        for e in it:
            n += 1
            try: st = e.stat(follow_symlinks=False)
            except OSError: continue
            total += st.st_blocks * 512
            if stat.S_ISDIR(st.st_mode): stack.append(e.path)
            if total > EVID_MAX_BYTES or n > EVID_MAX_ENTRIES: return {'bytes': total, 'entries': n, 'bounded': True}
    return {'bytes': total, 'entries': n, 'bounded': False}


def read_ack_pinned(path, start='/', owner_uid=0):
    """r6: bytes of an acknowledgement reached through pinned descriptors, or None. `path` is walked from `start` ('/' in production) with
    O_DIRECTORY|O_NOFOLLOW openat; every directory is fstat-checked on its descriptor (directory, owner, not group/other-writable); the file
    is opened O_NOFOLLOW|O_NONBLOCK (a FIFO is refused, never waited on) and fstat-checked (regular, owner, not group/other-writable, <= 4096 B).
    Nothing is decided from a path lstat."""
    flags = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC
    if start != '/' and not path.startswith(start + '/'): return None
    parent, name = os.path.split(path)
    try: fd = os.open(start, flags)
    except OSError: return None
    try:
        def safe(f):
            st = os.fstat(f)
            return stat.S_ISDIR(st.st_mode) and st.st_uid == owner_uid and not (stat.S_IMODE(st.st_mode) & 0o022)
        if not safe(fd): return None
        for comp in [c for c in parent[len(start):].split('/') if c]:
            nfd = os.open(comp, flags, dir_fd=fd); os.close(fd); fd = nfd
            if not safe(fd): return None
        ffd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC, dir_fd=fd)      # O_NONBLOCK: a FIFO must be refused, not waited on
        try:
            st = os.fstat(ffd)
            if not stat.S_ISREG(st.st_mode) or st.st_uid != owner_uid or (stat.S_IMODE(st.st_mode) & 0o022) or st.st_size > 4096: return None
            return os.read(ffd, 4097)
        finally: os.close(ffd)
    except OSError: return None
    finally: os.close(fd)


def retirement_gate(evid_root, ack_dir=None, chain_start='/', owner_uid=0):
    """r5 (§7): every retirement-failure (or unreadable) record must have a matching root-owned acknowledgement. r6: the acknowledgement is
    read from the persistent ACK_DIR through read_ack_pinned (ack_dir, chain_start and owner_uid are parameters only so the gate can be tested)"""
    ack_dir = ack_dir or ACK_DIR
    failures, acked, unacked = [], [], []
    try: entries = sorted(e.name for e in os.scandir(evid_root) if e.is_dir(follow_symlinks=False))
    except OSError as e: return {'error': f'{type(e).__name__}'}, ['<evidence root unreadable>']
    for name in entries:
        rp = f'{evid_root}/{name}/record.json'
        if not os.path.lexists(rp): continue
        b = read_record_nofollow(rp)
        try: sch = json.loads(b).get('schema') if b is not None else None
        except ValueError: sch = None
        if b is not None and sch != 'learn-op-retirement-failure/1' and sch is not None: continue
        failures.append(name); want = hashlib.sha256(b).hexdigest() if b is not None else None
        ap = f'{ack_dir}/{name}.retirement-ack'; ok = False
        if want:
            a = read_ack_pinned(ap, chain_start, owner_uid)
            ok = a is not None and a.decode('ascii', 'replace').strip() == want
        (acked if ok else unacked).append(name)
    return {'failures': failures, 'acked': acked, 'unacked': unacked, 'ackDir': ack_dir}, unacked


def parse_unit_rows(out):
    """r6: rows of `systemctl --user list-units` with at least four columns"""
    rows = [l.replace('●', ' ').split() for l in out.splitlines()]
    return [r_ for r_ in rows if len(r_) >= 4]


def used_ids(opid, custody_root, evid_root):
    """r6: existing custody or evidence directories for the op id or its precheck id; any of them refuses the run"""
    return [p for p in (f'{custody_root}/{opid}', f'{custody_root}/{opid}-pre', f'{evid_root}/{opid}', f'{evid_root}/{opid}-pre') if os.path.lexists(p)]


def kver(s):
    return tuple(int(x) for x in re.findall(r'\d+', s)[:2]) if s else (0, 0)


def norm_rule(action, errno_ret, name, args):
    na = sorted(json.dumps({'index': a.get('index'), 'op': a.get('op'), 'value': a.get('value'), 'valueTwo': a.get('valueTwo') or 0}, sort_keys=True) for a in (args or []))
    return (action, errno_ret, name, tuple(na))


def expected_set(src, kernel):
    exp, excluded = set(), []
    for rule in src.get('syscalls', []):
        inc, exc = rule.get('includes') or {}, rule.get('excludes') or {}
        ok = True
        if inc.get('caps'): ok = False                                      # empty cap set: an includes.caps rule never applies
        if inc.get('arches') and 'amd64' not in inc['arches'] and 'x86_64' not in inc['arches'] and 'x32' not in inc['arches']: ok = False
        if inc.get('minKernel') and kver(kernel) < kver(inc['minKernel']): ok = False
        if exc.get('caps'): pass                                            # empty cap set: an excludes.caps rule is never excluded
        if exc.get('arches') and ('amd64' in exc['arches'] or 'x86_64' in exc['arches']): ok = False
        if exc.get('minKernel') and kver(kernel) >= kver(exc['minKernel']): ok = False
        er = rule.get('errnoRet', src.get('defaultErrnoRet'))
        names = rule.get('names') or ([rule['name']] if rule.get('name') else [])
        for n in names:
            t = norm_rule(rule.get('action'), er if rule.get('action') in ('SCMP_ACT_ERRNO', 'SCMP_ACT_TRACE') else None, n, rule.get('args'))
            (exp.add(t) if ok else excluded.append({'name': n, 'action': rule.get('action'), 'includes': inc, 'excludes': exc}))
    return exp, excluded


def effective_set(oci, default_errno):
    eff = set()
    for rule in oci.get('syscalls', []):
        er = rule.get('errnoRet', default_errno)
        for n in rule.get('names') or []:
            eff.add(norm_rule(rule.get('action'), er if rule.get('action') in ('SCMP_ACT_ERRNO', 'SCMP_ACT_TRACE') else None, n, rule.get('args')))
    return eff


def dump_cmd(nonce):
    # C4 (M1): values read with the shell's read builtin, markers printed with the shell's own echo (the $(sed ...) for the
    # cgroup path forks, harmlessly); no exec; no podman output; nonce inline
    p = f'FSGG {nonce}'
    return ('-/bin/sh -c \'C=/sys/fs/cgroup$$(sed -n "s/^0:://p" /proc/self/cgroup); C=$${C%/.control}; '
            'E=""; while read k v; do E="$$E $$k=$$v"; done < "$$C/memory.events"; '
            f'echo "{p} MEMEV$$E"; read MP < "$$C/memory.peak"; read PP < "$$C/pids.peak"; '
            f'echo "{p} PEAK mem=$$MP pids=$$PP"; echo "{p} STOPPOST SR=$$SERVICE_RESULT EC=$$EXIT_CODE ES=$$EXIT_STATUS"\'')


# ============================================================ VERBATIM from deployment/learn-finite-profile/pod-operator/learn-pod-operator-r1.py
HELD_LOCKS = []
ACK_DIR = '/usr/local/libexec/fsgg-learn/retirement-acks'      # the default retirement_gate falls back to; the gate is always called with the env's directory


def sha_bytes(b):
    return hashlib.sha256(b).hexdigest()


def read_pinned(path, start, owner_uid, cap):
    """Bytes of a regular file reached through pinned descriptors: every directory from `start` is opened O_DIRECTORY|O_NOFOLLOW and fstat-checked
    (open_dir_chain), the file is opened O_NOFOLLOW|O_NONBLOCK and fstat-checked (regular, owner, not group/other-writable, at most `cap` bytes).
    Refuses with a reason; never returns None."""
    parent, name = os.path.split(path)
    dfd = open_dir_chain(parent, start, owner_uid)
    try:
        try:
            fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC, dir_fd=dfd)
        except FileNotFoundError:
            refuse(f'absent: {name}')
        except OSError:
            refuse(f'not a root-owned regular file: {name}')
        with os.fdopen(fd, 'rb') as f:
            st = os.fstat(f.fileno())
            if not stat.S_ISREG(st.st_mode) or st.st_uid != owner_uid or (stat.S_IMODE(st.st_mode) & 0o022) or st.st_size > cap:
                refuse(f'not a root-owned regular file closed to group and other writers, within {cap} bytes: {name}')
            return f.read(cap + 1)
    finally:
        os.close(dfd)


def take_lock(path, wait_s, deps):
    """The SHARED operator lock, with operator r6's semantics: O_RDWR|O_CREAT|O_NOFOLLOW|O_CLOEXEC 0600, an exclusive non-blocking flock tried every
    0.5 s for up to `wait_s`, then 'operator lock busy'. The descriptor is returned and kept open to exit, so the lock covers the whole run."""
    lfd = os.open(path, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
    t0 = deps['monotonic']()
    while True:
        try:
            fcntl.flock(lfd, fcntl.LOCK_EX | fcntl.LOCK_NB)
            return lfd, round(deps['monotonic']() - t0, 1)
        except BlockingIOError:
            if deps['monotonic']() - t0 > wait_s:
                os.close(lfd)
                refuse('operator lock busy')
            deps['sleep'](0.5)


def exclusion_conflicts(rows):
    """SYMMETRIC exclusion for a pod op: any learn-op-* reader op (precheck ops included) or learn-pod-* pod unit that is active or
    transitioning, and any failed or transitioning learn-fin-* unit. Returns the conflicting unit names."""
    out = []
    for r_ in rows:
        name, state = r_[0], r_[2]
        if name.startswith(('learn-op-', 'learn-pod-')) and state in TRANSITIONING:
            out.append(name)
        elif name.startswith('learn-fin-') and state in ('failed', 'activating', 'deactivating', 'reloading', 'refreshing'):
            out.append(name)
    return sorted(out)


def headroom(slice_dir, meminfo, custody_root, statvfs=os.statvfs):
    def rd(p):
        try:
            return open(p).read().strip()
        except OSError:
            return None
    pm, pc, mm, mc = (rd(f'{slice_dir}/{x}') for x in ('pids.max', 'pids.current', 'memory.max', 'memory.current'))
    mav = next((int(l.split()[1]) * 1024 for l in open(meminfo) if l.startswith('MemAvailable:')), 0)
    st = statvfs(custody_root)
    free = st.f_bavail * st.f_frsize
    ok = {'pids': pm == 'max' or bool(pm and pc and int(pm) - int(pc) >= POD_PIDS_HEADROOM),
          'memory': mm == 'max' or bool(mm and mc and int(mm) - int(mc) >= POD_MEMORY_HEADROOM),
          'memAvailable': mav >= POD_MEMAVAILABLE, 'custodyDisk': free >= 1 << 30}
    return ok, {'pidsMax': pm, 'pidsCurrent': pc, 'memMax': mm, 'memCurrent': mc, 'memAvailable': mav, 'custodyFree': free}


def verify_claim(env, ident, opid):
    """The live claim must name this op and this source identity, and its sealed manifest on disk must have the sha256 the claim records and
    the same source identity. Only metadata is read: the sealed COPY of the input is not opened (hashing it would be an input read)."""
    p, k = paths(env), INPUT_KINDS[input_kind(env)]           # #5184: the claim directory and record type follow the admitted input kind
    raw = read_pinned(f"{p['claims']}/{ident}.claim", env['chain_start'], env['owner_uid'], 65536)
    claim = json.loads(raw)
    if claim.get('schema') != k['claim']:
        refuse('claim has another schema')
    if k['claim'] != 'learn-input-claim/1' and (claim.get('provenance') != 'synthetic' or claim.get('realSpoolProvenance') is not False):
        refuse('the synthetic claim does not say synthetic provenance')
    if claim.get('opId') != opid:
        refuse('the live claim names another op')
    if claim.get('sourceIdentity') != ident:
        refuse('the claim has another source identity')
    sid = claim.get('snapshotId')
    if not isinstance(sid, str) or not OPID.match(sid):
        refuse('the claim has no valid snapshot id')
    smraw = read_pinned(f"{p['inputs']}/{sid}.manifest.json", env['chain_start'], env['owner_uid'], 1 << 20)
    if sha_bytes(smraw) != claim.get('sealedManifestSha256'):
        refuse('the sealed manifest does not have the sha256 the claim records')
    sm = json.loads(smraw)
    if sm.get('sourceIdentity') != ident or sm.get('snapshotId') != sid:
        refuse('the sealed manifest names another source or snapshot')
    if k['sealed'] != 'learn-input-snapshot/1' and (sm.get('schema') != k['sealed'] or sm.get('provenance') != 'synthetic' or sm.get('realSpoolProvenance') is not False):
        refuse('the synthetic sealed manifest is not a synthetic seal')
    return claim, sha_bytes(raw), sid


def fsync_dir(path):
    fd = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def create_marker(marker_root, opid, claim_sha, now):
    """The input-read marker is owned by THIS operator: <marker_root>/<op-id>/input-read, directory 0700, file O_EXCL. It is created before the
    first read or mount of the input and never removed. A second attempt for the same op id refuses."""
    os.makedirs(marker_root, 0o700, exist_ok=True)
    d = f'{marker_root}/{opid}'
    try:
        os.mkdir(d, 0o700)
    except FileExistsError:
        refuse('input-read directory already exists for this op id')
    p = f'{d}/input-read'
    fd = os.open(p, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
    with os.fdopen(fd, 'w') as f:
        f.write(json.dumps({'opId': opid, 'claimSha256': claim_sha, 'utc': now().isoformat()}, sort_keys=True) + '\n')
        f.flush()
        os.fsync(f.fileno())
    fsync_dir(d)                                         # the file's directory entry, then the marker directory's entry in its parent:
    fsync_dir(marker_root)                               # the marker must survive a crash before the input is touched
    return p


def begin_input_access(env, R, opid, ident, mount, now):
    """Ordering contract: (1) the claim is verified, and a refusal here creates nothing; (2) the input-read marker is created (O_EXCL);
    (3) gates.inputMounted is set to true; (4) only then is the input mounted. A run that dies during the mount therefore cannot show
    inputMounted false, and the marker exists before the first read or mount."""
    claim, claim_sha, sid = verify_claim(env, ident, opid)
    R['gates']['claim'] = {'generation': claim.get('generation'), 'snapshotId': sid, 'claimSha256': claim_sha}
    R['gates']['inputReadMarker'] = create_marker(paths(env)['markers'], opid, claim_sha, now)
    R['gates']['inputMounted'] = True
    mount(claim, sid)



# ============================================================ r2: the plan
STEP_NAME = re.compile(r'^[a-z0-9][a-z0-9-]{0,30}$')
RUNNER_BUILTINS = ('wait-tcp', 'env-file')
CRED_FILES = ('pg-password', 'pg.connection', 'operator.token', 'runner.token', 'ca.pem', 'server.pfx', 'server.password', 'collector.secret', 'host-config.json',
              'browser-key-hash.json', 'spool.sha256')
PG_COMMAND = ['postgres', '-c', 'listen_addresses=127.0.0.1', '-c', 'shared_buffers=128MB', '-c', 'max_connections=20', '-c', 'io_method=worker', '-c', 'fsync=on',
              '-c', 'max_wal_size=256MB', '-c', 'temp_file_limit=128MB']


def is_held(v):
    return isinstance(v, (list, tuple)) and len(v) == 2 and v[0] == 'HELD'


def plan_digest(plan):
    return sha_bytes(json.dumps(plan, sort_keys=True, separators=(',', ':')).encode())


def plan_argv(plan):
    """The --argv-json a pod op is admitted with: it names the plan by digest, so the admission binds the run to this plan."""
    return json.dumps([PLAN_SCHEMA, plan_digest(plan)], separators=(',', ':'))


def synthetic_plan_argv(plan):
    """#5184: the --argv-json of a SYNTHETIC-input op (the pod harness, the install receipt op). The third element is the explicit declaration; the
    admission's argvJsonSha256 binds it, so Root admits a synthetic op as synthetic and no real admission can start one (or the reverse)."""
    return json.dumps([PLAN_SCHEMA, plan_digest(plan), SYNTHETIC_TAG], separators=(',', ':'))


def argv_input_kind(argv_raw, plan):
    """The input kind an --argv-json names, or None when it names neither form of this plan."""
    return {plan_argv(plan): 'real', synthetic_plan_argv(plan): 'synthetic'}.get(argv_raw)


def strs(v):
    return isinstance(v, list) and len(v) > 0 and all(isinstance(x, str) and x and '\0' not in x for x in v)


def validate_plan(plan):
    """Returns (errors, held). errors: the plan is malformed or breaks an invariant the finalizer depends on; held: values not yet decided. A run needs both
    empty. The simulation tracks each slot: occupied, running, done (no / may / yes) and dirty (state not yet in custody)."""
    errs, held = [], []
    if not isinstance(plan, dict) or plan.get('schema') != PLAN_SCHEMA or not isinstance(plan.get('generations'), dict) or not isinstance(plan.get('actions'), list):
        return ['the plan has another schema or shape'], held
    gens = plan['generations']
    for gname, g in sorted(gens.items()):
        slot = g.get('slot')
        if not STEP_NAME.match(gname):
            errs.append(f'generation {gname}: not a valid name (it names the credential directory)')
        if slot not in SLOTS + ('*',):
            errs.append(f'generation {gname}: unknown slot')
            continue
        if not isinstance(g.get('creds', []), list) or not set(g.get('creds', [])) <= set(CRED_FILES):
            errs.append(f'generation {gname}: unknown credential file')
        if slot == 'pg':
            continue
        for st in g.get('steps', []):
            if not (isinstance(st, (list, tuple)) and len(st) == 2 and isinstance(st[0], str) and STEP_NAME.match(st[0]) and st[0] != 'service'):
                errs.append(f'generation {gname}: a malformed step')
            elif is_held(st[1]):
                held.append(f'{gname}.{st[0]}: {st[1][1]}')
            elif not strs(st[1]):
                errs.append(f'generation {gname}.{st[0]}: argv is not a non-empty list of strings')
            elif not (st[1][0].startswith('/') or st[1][0] in RUNNER_BUILTINS):
                errs.append(f'generation {gname}.{st[0]}: argv[0] is neither an absolute path nor a runner built-in')
        svc = g.get('service')
        if is_held(svc):
            held.append(f'{gname}.service: {svc[1]}')
        elif svc is not None and not strs(svc):
            errs.append(f'generation {gname}: the service is not a list of strings')
        elif svc is not None and not svc[0].startswith('/'):
            errs.append(f'generation {gname}: the service argv[0] is not an absolute path')
        if g.get('ready') and not strs(g['ready']):
            errs.append(f'generation {gname}: bad readiness options')
        if g.get('input') and slot != 'feed':
            errs.append(f'generation {gname}: only the feed slot mounts /in')
        if any(strs(st[1]) and BID_REF in st[1] for st in g.get('steps', []) if isinstance(st, (list, tuple)) and len(st) == 2) \
                or BID_REF in (g.get('ready') or []) or (strs(svc) and BID_REF in svc and slot != 'host'):
            errs.append(f'generation {gname}: the backup-identity reference is only for a host service')
    S = {s: None for s in SLOTS}
    ck, replay, acts, bid = 0, 0, plan['actions'], False

    def need(cond, why):
        if not cond:
            errs.append(f'action {i + 1} {a}: {why}')
        return cond
    for i, a in enumerate(acts):
        if not (isinstance(a, list) and a and a[0] in PLAN_ACTIONS):
            errs.append(f'action {i + 1}: not one of {",".join(PLAN_ACTIONS)}')
            continue
        v, arg = a[0], (a[1] if len(a) > 1 else None)
        if v == 'start':
            g = gens.get(arg)
            if g is not None and strs(g.get('service')) and BID_REF in g['service']:
                need(bid, 'no checkpoint with host-init in the host slot holds the backup identity this start needs')
            if need(g is not None and g.get('slot') in SLOTS, 'unknown generation') and need(S[g['slot']] is None, 'the slot is occupied'):
                S[g['slot']] = {'gen': arg, 'running': True, 'done': 'may' if (g['slot'] != 'pg' and g.get('service') is None) else 'no', 'dirty': g['slot'] != 'pg'}
                replay += bool(g.get('input'))
        elif v in ('wait-ready', 'wait-done', 'stop', 'kill', 'reconcile', 'remove', 'restart', 'wait-listen'):
            if not need(arg in SLOTS and S.get(arg) is not None, 'the slot is empty'):
                continue
            s = S[arg]
            if v == 'wait-ready':
                need(arg != 'pg' and s['running'] and bool(gens[s['gen']].get('ready')), 'no readiness probe here')
            elif v == 'wait-listen':
                need(arg == 'pg' and s['running'] and len(a) == 3 and a[2] in DECLARED_PORTS, 'wait-listen is for a running pg on a declared port')
            elif v == 'wait-done':
                if need(arg != 'pg' and s['running'] and gens[s['gen']].get('service') is None, 'only a generation without a service ends by itself'):
                    s['done'] = 'yes'
            elif v == 'stop':
                if need(s['running'] and s['done'] != 'yes', 'nothing running to stop'):
                    if arg == 'pg':
                        s['running'] = False
                    else:
                        s['done'], s['dirty'] = 'yes', True
            elif v == 'kill':
                if need(s['running'], 'nothing running to kill') and need(arg == 'pg' or not s['dirty'], 'no checkpoint holds this container\'s state'):
                    s['running'], s['killed'] = False, True
            elif v == 'reconcile':
                need(not s['running'] and s.get('killed'), 'reconcile follows a kill')
            elif v == 'remove':
                if need(arg != 'pg', 'pg is never removed (the finalizer stops it)') and need(not s['dirty'], 'no checkpoint holds this container\'s state'):
                    S[arg] = None
            elif v == 'restart':
                if need(arg == 'pg' and not s['running'], 'only a stopped pg restarts in place'):
                    s['running'], s['killed'] = True, False
        elif v == 'checkpoint':
            if need(isinstance(arg, str) and re.fullmatch(r'[a-z0-9][a-z0-9-]{0,39}', arg) is not None, 'bad label') and \
               need(all(S[r_] is not None and S[r_]['running'] for r_ in ROLES), 'finalizer r5 needs all three role containers running at a checkpoint'):
                ck += 1
                bid = bid or S['host']['gen'] == 'host-init'
                for r_ in ROLES:
                    S[r_]['dirty'] = False
        elif v == 'sleep':
            need(isinstance(arg, int) and not isinstance(arg, bool) and 1 <= arg <= 300, 'sleep 1 to 300 s')
        if i < len(acts) - 1 and all(S[r_] is not None and S[r_]['done'] in ('may', 'yes') for r_ in ROLES):
            errs.append(f'action {i + 1} {a}: all three roles could be done before the end, so the finalizer would trigger early')
    if ck > MAX_CHECKPOINTS:
        errs.append(f'{ck} checkpoints; finalizer r5 serves at most {MAX_CHECKPOINTS}')
    if replay != 1:
        errs.append(f'{replay} generations mount /in; the plan must replay exactly once')
    if not (all(S[r_] is not None and S[r_]['done'] == 'yes' for r_ in ROLES) and S['pg'] is not None and S['pg']['running']):
        errs.append('at the end every role must be done (the finalizer trigger) and pg running (the finalizer stops it with SIGINT)')
    return errs, held


def encode_steps(g):
    out = []
    for name, argv in g.get('steps', []):
        out += [name, str(len(argv))] + list(argv)
    if g.get('service'):
        out += ['service', str(len(g['service']))] + list(g['service'])
    return out


# ============================================================ r2: container and unit argv (the precheck uses the same builder)
def common_opts(env, split):
    return (['--cgroups=split'] if split else []) + ['--pull=never', '--cap-drop=all', '--security-opt=no-new-privileges', '--security-opt', f"seccomp={env['seccomp']}",
                                                      '--read-only', '--read-only-tmpfs=false', '--log-driver=k8s-file', '--log-opt', 'max-size=16m',
                                                      '--tmpfs', '/tmp:rw,noexec,nosuid,nodev,size=64m,U', '--tmpfs', '/dev/shm:rw,noexec,nosuid,nodev,size=64m,U']


def ctr_argv(env, opid, gname, slot, sid=None, pre=False, command=None):
    """`podman run -d` for one container. pre=True is the precheck pod: no credentials, no volume, no input, default cgroups (the operator is not in
    the pod unit), and `command` instead of the generation's steps. Everything else is the run's own option set."""
    g = env['plan']['generations'][gname]
    pod = f'learn-pre-{opid}' if pre else f'learn-pod-{opid}'
    mem, pids, heap = LIMITS[slot]
    a = ['podman', 'run', '-d', '--pod', pod, '--name', f'{pod}-{slot}'] + common_opts(env, not pre) + ['--memory', mem, '--memory-swap', mem, '--pids-limit', pids]
    cred, vol = f"{env['cred_root']}/{opid}/{gname}", f"{env['volume_root']}/{opid}.mnt"
    if slot == 'pg':
        a += ['--user', '999:999', '--tmpfs', '/var/run/postgresql:rw,noexec,nosuid,nodev,size=1m,mode=0700,U']
        if not pre:
            a += ['-v', f'{vol}/pgdata:/pgdata:rw,nosuid,nodev,noexec', '-v', f'{cred}:/cred:ro,nosuid,nodev,noexec', '--env', 'PGDATA=/pgdata/data',
                  '--env', 'POSTGRES_PASSWORD_FILE=/cred/pg-password', '--env', 'POSTGRES_USER=learn', '--env', 'POSTGRES_DB=learn']
        return a + [env['pg_image']] + list(command or PG_COMMAND)
    a += ['--user', '1000:1000', '--tmpfs', f'/out/{slot}:rw,noexec,nosuid,nodev,size=64m,mode=0700,U', '--tmpfs', '/work:rw,exec,nosuid,nodev,size=512m,mode=0700,U',
          '--env', f'DOTNET_GCHeapHardLimit={heap}']
    if not pre:
        if g.get('creds'):                                   # a generation mounts only its own credential directory; one without creds mounts none
            a += ['-v', f'{cred}:/cred:ro,nosuid,nodev,noexec']
        if 'ca.pem' in g.get('creds', []):
            a += ['--env', 'SSL_CERT_FILE=/cred/ca.pem']
        if g.get('volume'):
            a += ['-v', f'{vol}/recv:/recv:rw,nosuid,nodev,noexec']
        if g.get('input'):
            a += ['-v', f"{env['view']}/{INPUT_KINDS[input_kind(env)]['inputs']}/{sid}:/in:ro,nosuid,nodev,noexec"]
    a += [env['app_image'], slot]
    if command is not None:
        return a + ['--'] + list(command)
    return a + list(g.get('ready') or []) + ['--', '/bin/sh', '-c', STEP_RUNNER, 'step-runner', slot] + encode_steps(g)


def mounts_of(argv):
    out = []
    for i, x in enumerate(argv[:-1]):
        if x == '-v':
            parts = argv[i + 1].split(':')
            out.append({'type': 'bind', 'source': parts[0], 'destination': parts[1], 'readOnly': 'ro' in (parts[2].split(',') if len(parts) > 2 else [])})
        elif x == '--tmpfs':
            out.append({'type': 'tmpfs', 'destination': argv[i + 1].split(':')[0]})
    return out


def planned_containers(env, opid, sid):
    """Every container the driver will create, with its mounts; the feed replay is the only one with /in. This is the RESULT's `containers` list."""
    out = []
    for a in env['plan']['actions']:
        if a[0] == 'start':
            g = env['plan']['generations'][a[1]]
            argv = ctr_argv(env, opid, a[1], g['slot'], sid)
            out.append({'name': f"learn-pod-{opid}-{g['slot']}", 'generation': a[1], 'mode': 'planned',
                        'image': env['pg_image'] if g['slot'] == 'pg' else env['app_image'], 'mounts': mounts_of(argv)})
    return out


def fin_unit_argv(env, opid, ident, nonce):
    """Finalizer r5 FIRST (design section 8). No pod removal here: a finalizer that loses the custody race exits 70 and must not touch the winner's pod."""
    return ['systemd-run', '--user', '--unit', f'learn-fin-{opid}', '-p', 'RuntimeMaxSec=1980', '-p', 'MemoryMax=512M', '-p', 'TasksMax=64',
            '-p', f'Environment=FSGG_NONCE={nonce}', '-p', f"Environment=FSGG_PG_IMAGE={env['pg_image']}",
            env['python'], '-I', env['fin'], '--id', opid, '--source-identity', ident] + (['--input', 'synthetic'] if input_kind(env) == 'synthetic' else [])


def pod_unit_argv(env, opid, ident, nonce):
    """The pod unit: BindsTo/After the finalizer, the design section 3 limits, operator r6's bounded ExecStop and MEMEV dump (verbatim dump_cmd), the pod and
    the credential tmpfs removed on stop. Its main process is this file in --drive mode."""
    pod, cred = f'learn-pod-{opid}', f"{env['cred_root']}/{opid}"
    return ['systemd-run', '--user', '--unit', pod, '-p', f'BindsTo=learn-fin-{opid}.service', '-p', f'After=learn-fin-{opid}.service',
            '-p', 'KillMode=control-group', '-p', 'RuntimeMaxSec=1920', '-p', 'TimeoutStopSec=10', '-p', 'Delegate=yes', '-p', 'CPUQuota=150%',
            '-p', 'MemoryMax=4G', '-p', 'MemorySwapMax=0', '-p', 'TasksMax=900', '-p', f'Environment=FSGG_NONCE={nonce}',
            '-p', f'ExecStop=-/usr/bin/timeout 5 /usr/bin/podman pod unpause {pod}', '-p', f'ExecStop=-/usr/bin/timeout 9 /usr/bin/podman pod stop -t 3 {pod}',
            '-p', f'ExecStopPost={dump_cmd(nonce)}', '-p', f'ExecStopPost=-/usr/bin/podman pod rm -f --time 0 {pod}',
            '-p', f'ExecStopPost=-/usr/bin/podman unshare rm -rf --one-file-system {cred}',
            '--', env['python'], '-I', env['self'], '--drive', '--id', opid, '--source-identity', ident] + (['--input', 'synthetic'] if input_kind(env) == 'synthetic' else [])


# ============================================================ r2: the loop volume (carry-forward item 3)
def mountinfo_rows(text):
    rows = []
    for line in text.splitlines():
        f = line.split(' ')
        if '-' not in f[6:]:
            continue
        sep = f.index('-', 6)
        if len(f) < sep + 4:
            continue
        unesc = lambda s: re.sub(r'\\([0-7]{3})', lambda m: chr(int(m.group(1), 8)), s)
        rows.append({'point': unesc(f[4]), 'options': f[5].split(','), 'fsType': f[sep + 1], 'source': unesc(f[sep + 2]), 'superOptions': f[sep + 3].split(',')})
    return rows


def volume_precheck(env, deps, opid):
    mp = f"{env['volume_root']}/{opid}.mnt"
    V = {'path': mp}
    try:
        rows = [r_ for r_ in mountinfo_rows(deps['read'](env['mountinfo'])) if r_['point'] == mp]
    except OSError:
        refuse('loop volume: the mount table is unreadable')
    V['mounts'] = len(rows)
    if len(rows) != 1:
        refuse(f'loop volume: {mp} is not mounted exactly once ({len(rows)} mounts)')
    m = rows[0]
    V.update(fsType=m['fsType'], source=m['source'], options=sorted(m['options']))
    missing = sorted({'rw', 'nosuid', 'nodev', 'noexec'} - set(m['options']))
    if m['fsType'] != 'ext4' or not re.fullmatch(r'/dev/loop[0-9]+', m['source']) or missing:
        refuse(f"loop volume: not an rw,nosuid,nodev,noexec ext4 loop mount (type {m['fsType']}, source {m['source']}, missing {','.join(missing) or 'none'})")
    v = deps['statvfs'](mp)
    total, used, inodes = v.f_blocks * v.f_frsize, (v.f_blocks - v.f_bfree) * v.f_frsize, v.f_files - v.f_ffree
    V.update(totalBytes=total, usedBytes=used, inodesUsed=inodes)
    if not VOLUME_MIN <= total <= VOLUME_CAP:
        refuse(f'loop volume: {total} bytes, not a 1 GiB filesystem')
    if used > VOLUME_FRESH_BYTES or inodes > VOLUME_FRESH_INODES:
        refuse(f'loop volume: not fresh ({used} bytes and {inodes} inodes in use)')
    st = os.lstat(mp)
    if not stat.S_ISDIR(st.st_mode) or st.st_uid != env['owner_uid'] or stat.S_IMODE(st.st_mode) & 0o022:
        refuse('loop volume: the top directory is not a root-owned directory closed to group and other writers')
    names = set(os.listdir(mp))
    V['entries'] = sorted(names)
    if not {'pgdata', 'recv'} <= names <= {'pgdata', 'recv', 'lost+found'}:
        refuse('loop volume: the top directory must hold exactly pgdata and recv (and lost+found)')
    for name, who in (('pgdata', 'pg'), ('recv', 'app')):
        s, uid = os.lstat(f'{mp}/{name}'), env['host_uid'][who]
        if not (stat.S_ISDIR(s.st_mode) and s.st_uid == uid and s.st_gid == uid and stat.S_IMODE(s.st_mode) == 0o700 and s.st_dev == st.st_dev):
            refuse(f'loop volume: {name} is not a {uid}:{uid} 0700 directory on the volume itself')
    return V


# ============================================================ r2: the container precheck
def start_errno(err):
    e = err or ''
    return 'EPERM (Operation not permitted)' if 'Operation not permitted' in e else 'ENOSYS (Function not implemented)' if 'Function not implemented' in e else 'start failed'


def parse_probe(text):
    p = {'id': None, 'status': {}, 'own': {}, 'ifs': [], 'route4': None, 'route6': None, 'probe': [], 'setsid': None, 'end': False}
    for line in text.splitlines():
        w = line.split()
        if not w:
            continue
        if w[0] == 'ID' and len(w) == 3:
            p['id'] = [w[1], w[2]]
        elif w[0] == 'STATUS' and len(w) == 3:
            p['status'][w[1]] = w[2]
        elif w[0] == 'OWN' and len(w) == 3:
            p['own'][w[2]] = w[1]
        elif w[0] == 'IF' and len(w) == 2:
            p['ifs'].append(w[1])
        elif w[0] in ('ROUTE4', 'ROUTE6') and len(w) == 2:
            p[w[0].lower()] = w[1]
        elif w[0] == 'PROBE':
            p['probe'].append(line.strip())
        elif w[0] == 'SETSID' and len(w) == 2:
            p['setsid'] = w[1]
        elif line.strip() == 'END':
            p['end'] = True
    return p


def vsock_errno(src, kernel):
    exp, _ = expected_set(src, kernel)
    vs = sorted({t[1] for t in exp if t[2] == 'socket' and t[0] == 'SCMP_ACT_ERRNO'
                 and any(json.loads(x) == {'index': 0, 'op': 'SCMP_CMP_EQ', 'value': 40, 'valueTwo': 0} for x in t[3])}, key=str)
    if len(vs) != 1 or not isinstance(vs[0], int):
        refuse(f'cannot determine the expected AF_VSOCK errnoRet from the profile: {vs}')
    return vs[0]


def proc_uids(deps, proc, pid):
    try:
        return next(l.split()[1:] for l in deps['read'](f'{proc}/{pid}/status').splitlines() if l.startswith('Uid:'))
    except (OSError, StopIteration):
        return []


def probe_command(slot, errno_v):
    if slot == 'pg':
        return ['/bin/sh', '-c', PROBE_SH, 'probe', 'pg', '0', '/tmp', '/dev/shm', '/var/run/postgresql']
    return ['/bin/sh', '-c', PROBE_SH, 'probe', 'app', str(errno_v), f'/out/{slot}', '/work', '/tmp', '/dev/shm']


def assess(env, deps, name, slot, log, exp, src):
    run = deps['run']
    p = parse_probe(log)
    kind = 'pg' if slot == 'pg' else 'app'
    want = ['999', '999'] if kind == 'pg' else ['1000', '1000']
    rc, ins, _ = run(['podman', 'inspect', name], 15)
    try:
        j = json.loads(ins)[0]
    except (ValueError, IndexError, TypeError):
        j = {}
    hc, state = j.get('HostConfig') or {}, j.get('State') or {}
    uids = proc_uids(deps, env['proc'], state.get('Pid'))
    try:
        oci = json.loads(deps['read'](j.get('OCIConfigPath') or '/nonexistent'))
    except (OSError, ValueError):
        oci = {}
    sc = (oci.get('linux') or {}).get('seccomp') or {}
    eff = effective_set(sc, sc.get('defaultErrnoRet'))
    mem, pids, _ = LIMITS[slot]
    paths_ = ['/tmp', '/dev/shm', '/var/run/postgresql'] if kind == 'pg' else [f'/out/{slot}', '/work', '/tmp', '/dev/shm']
    checks = {
        'probeComplete': p['end'],
        'uidGid': p['id'] == want,
        'capsZero': all(p['status'].get(k) == '0' * 16 for k in ('CapInh', 'CapPrm', 'CapEff', 'CapBnd', 'CapAmb')),
        'noNewPrivs': p['status'].get('NoNewPrivs') == '1',
        'tmpfsOwners': sorted(p['own']) == sorted(paths_) and all(u == want[0] for u in p['own'].values()),
        'loopbackOnly': p['ifs'] == ['lo'] and p['route4'] == '0' and p['route6'] == '0',
        'socketProbe': kind == 'pg' or p['probe'] == ['PROBE ok'],
        'setsidWait': kind == 'pg' or p['setsid'] == 'ok',          # the step runner needs util-linux `setsid -w` in the role image
        'running': state.get('Status') == 'running',
        'hostUid': len(uids) == 4 and all(u.isdigit() and int(u) == env['host_uid'][kind] and int(u) not in (0, env['uid']) for u in uids),
        'image': j.get('Image') == (env['pg_image'] if kind == 'pg' else env['app_image']),
        'capAddEmpty': not hc.get('CapAdd'),
        'readOnlyRoot': bool(hc.get('ReadonlyRootfs')),
        'memory': hc.get('Memory') == int(mem[:-1]) << 20,
        'pids': hc.get('PidsLimit') == int(pids),
        'seccompPath': f"seccomp={env['seccomp']}" in (hc.get('SecurityOpt') or []),
        'seccompDefault': sc.get('defaultAction') == src.get('defaultAction') and 'defaultErrnoRet' in sc and sc.get('defaultErrnoRet') == src.get('defaultErrnoRet'),
        'seccompEqual': bool(exp) and eff == exp,
    }
    return {'checks': checks, 'hostUids': uids, 'probe': {k: p[k] for k in ('id', 'ifs', 'route4', 'route6', 'probe', 'setsid')},
            'seccomp': {'expected': len(exp), 'effective': len(eff), 'missing': sorted(map(str, exp - eff))[:20], 'extra': sorted(map(str, eff - exp))[:20]}}


def pod_precheck(env, deps, R, opid):
    """Design sections 3 and 7: the precheck pod learn-pre-<id>, its containers built by ctr_argv with the probe command, every assertion recorded, the pod
    removed and the removal read back. Returns nothing; refuses with the first failure (after the cleanup)."""
    run, pod = deps['run'], f'learn-pre-{opid}'
    P = R['gates']['podPrecheck'] = {'pod': pod, 'containers': {}}
    first = None
    try:
        src = json.loads(read_pinned(env['seccomp'], env['chain_start'], env['owner_uid'], 1 << 20))
        kernel = deps['kernel']()
        exp, _ = expected_set(src, kernel)
        errno_v = P['vsockErrnoExpected'] = vsock_errno(src, kernel)
        rc, _, err = run(['podman', 'pod', 'create', '--name', pod, '--network=none', '--share=net', '--infra-name', f'{pod}-infra'], 60)
        if rc != 0:
            refuse('precheck pod create failed: ' + start_errno(err))
        for slot in SLOTS:
            argv = ctr_argv(env, opid, 'pg' if slot == 'pg' else 'hold', slot, pre=True, command=probe_command(slot, errno_v))
            R['containers'].append({'name': f'{pod}-{slot}', 'mode': 'precheck', 'mounts': mounts_of(argv)})
            rc, _, err = run(argv, 60)
            if rc != 0:
                P['containers'][slot] = {'start': start_errno(err)}
                refuse(f'precheck container {slot} did not start: {start_errno(err)}')
        logs = {}
        for slot in SLOTS:
            t0 = deps['monotonic']()
            while True:
                rc, out, _ = run(['podman', 'logs', f'{pod}-{slot}'], 15)
                if rc == 0 and re.search(r'^END$', out, re.M):
                    logs[slot] = out
                    break
                if deps['monotonic']() - t0 > 60:
                    refuse(f'precheck container {slot} printed no complete probe')
                deps['sleep'](0.5)
        for slot in SLOTS:
            P['containers'][slot] = assess(env, deps, f'{pod}-{slot}', slot, logs[slot], exp, src)
        bad = sorted(f'{s}.{k}' for s, a in P['containers'].items() for k, v in a['checks'].items() if not v)
        if bad:
            refuse('pod precheck failed: ' + ','.join(bad))
    except Refuse as e:
        first = e
    finally:
        rc, _, _ = run(['podman', 'pod', 'rm', '-f', '-t', '0', pod], 60)
        r1, pods, _ = run(['podman', 'pod', 'ps', '-a', '--filter', f'name=^{pod}$', '--format', '{{.Name}}'], 30)
        r2, ctrs, _ = run(['podman', 'ps', '-a', '--filter', f'name=^{pod}-', '--format', '{{.Names}}'], 30)
        P['retired'] = rc == 0 and r1 == 0 and r2 == 0 and not pods.strip() and not ctrs.strip()
    if first is not None:
        raise first
    if not P['retired']:
        refuse('the precheck pod was not retired')


# ============================================================ r2: the credential generator (design 2.3, the qualify_host_state.py pattern)
def write_private(path, data):
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
    with os.fdopen(fd, 'wb') as f:
        f.write(data)
        f.flush()
        os.fsync(f.fileno())


def read_private(path, cap=1 << 20):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
    with os.fdopen(fd, 'rb') as f:
        st = os.fstat(f.fileno())
        if not stat.S_ISREG(st.st_mode) or st.st_size > cap:
            refuse(f'credentials: {os.path.basename(path)} is not a regular file within {cap} bytes')
        return f.read(cap + 1)


def filesystem_of(env, deps, path):
    best = None
    for r_ in mountinfo_rows(deps['read'](env['mountinfo'])):
        p = r_['point']
        if path == p or path.startswith(p.rstrip('/') + '/'):
            if best is None or len(p) > len(best[0]):
                best = (p, r_['fsType'])
    return best


def host_config():
    """fsgg.telemetry.host-config/2 for the in-pod receiver: the qualify_host_state.py template with the one native-collector grant the replay needs."""
    return {'Schema': 'fsgg.telemetry.host-config/2', 'ListenUrl': f'https://localhost:{RECV_PORT}', 'CertificatePath': '/cred/server.pfx',
            'CertificatePasswordFile': '/cred/server.password', 'ServiceLockPath': '/recv/service.lock', 'Stores': [{'WorkspaceId': WS, 'Root': '/recv/store'}],
            'Credentials': [{'Reference': 'collector', 'SecretFile': '/cred/collector.secret', 'WorkspaceId': WS, 'ProducerId': PRODUCER, 'StreamId': STREAM,
                             'Role': 'native-collector', 'GrantId': 'grant-collector', 'GrantGeneration': 1, 'Revoked': False}],
            'BrowserPrincipals': [{'PrincipalId': 'learn-pod-browser-unused', 'KeyHashFile': '/cred/browser-key-hash.json', 'WorkspaceIds': [WS], 'Revoked': False}],
            'BrowserSession': {'IdleSeconds': 300, 'AbsoluteSeconds': 3600, 'MaximumSessions': 16, 'LoginAttemptsPerMinute': 8, 'LoginAdmission': 2,
                               'QueryAdmission': 2, 'QueryTimeoutSeconds': 10}}


SPOOL_PATH = re.compile(r'^[A-Za-z0-9._+-]+(/[A-Za-z0-9._+-]+)*$')


def spool_checklist(files):
    lines = []
    for f in files:
        p = f.get('path') if isinstance(f, dict) else None
        if not isinstance(p, str) or not SPOOL_PATH.match(p) or '..' in p.split('/') or not HEX64.match(str(f.get('sha256'))) or f.get('type') != 'file':
            refuse('credentials: the sealed manifest holds a path or digest the replay check cannot carry')
        lines.append(f"{f['sha256']}  /work/spool/{p}\n")
    return ''.join(lines).encode()


def gen_credentials(env, deps, opid, sealed_files):
    """Creates <cred_root>/<id>/<slot>/ with exactly the files each slot's generations name, 0600, on a tmpfs, owned by the container's uid. Secrets come from
    the CSPRNG (`secrets`), are written O_EXCL and are never printed, logged or passed in argv: openssl reads the PFX password from a file. The CA and server
    keys exist only in <id>/work and are removed before the containers start. Returns a record with names, sizes and public certificate digests only."""
    run, root = deps['run'], env['cred_root']
    d, work = f'{root}/{opid}', f'{root}/{opid}/work'
    rec = {'dir': d, 'files': {}}
    fs = rec['filesystem'] = filesystem_of(env, deps, root)
    if not fs or fs[1] != 'tmpfs':
        refuse('credentials: the credential root is not on a tmpfs')
    os.makedirs(root, 0o700, exist_ok=True)
    st = os.lstat(root)
    if not stat.S_ISDIR(st.st_mode) or st.st_uid != deps['geteuid']() or stat.S_IMODE(st.st_mode) != 0o700:
        refuse('credentials: the credential root is not a 0700 directory of this account')
    try:
        os.mkdir(d, 0o700)
    except FileExistsError:
        refuse('credentials: the credential directory for this op id already exists')
    os.mkdir(work, 0o700)
    tok = deps['token']
    val = {'pg-password': tok(32), 'operator.token': tok(48), 'runner.token': tok(48), 'collector.secret': tok(48), 'server.password': tok(32)}
    val = {k: v.encode() for k, v in val.items()}
    val['pg.connection'] = (f'Host=127.0.0.1;Port={PG_PORT};Database=learn;Username=learn;Password={val["pg-password"].decode()};SSL Mode=Disable;Pooling=false').encode()
    val['browser-key-hash.json'] = json.dumps({'schema': 'fsgg.telemetry.browser-key/1', 'algorithm': 'sha256', 'keyHash': sha_bytes(tok(32).encode())},
                                              separators=(',', ':')).encode()
    val['host-config.json'] = json.dumps(host_config(), separators=(',', ':')).encode()
    val['spool.sha256'] = spool_checklist(sealed_files)
    write_private(f'{work}/server.password', val['server.password'])
    write_private(f'{work}/server.ext', b'subjectAltName=DNS:localhost\nbasicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature\nextendedKeyUsage=serverAuth\n')
    o, w = env['openssl'], lambda n: f'{work}/{n}'
    for step, argv in (
            ('ca', [o, 'req', '-x509', '-newkey', 'ec', '-pkeyopt', 'ec_paramgen_curve:P-256', '-nodes', '-keyout', w('ca.key'), '-out', w('ca.pem'), '-days', '1',
                    '-subj', f'/CN=learn-pod-{opid}-ca', '-addext', 'basicConstraints=critical,CA:TRUE', '-addext', 'keyUsage=critical,keyCertSign,cRLSign']),
            ('csr', [o, 'req', '-new', '-newkey', 'ec', '-pkeyopt', 'ec_paramgen_curve:P-256', '-nodes', '-keyout', w('server.key'), '-out', w('server.csr'), '-subj', '/CN=localhost']),
            ('sign', [o, 'x509', '-req', '-in', w('server.csr'), '-CA', w('ca.pem'), '-CAkey', w('ca.key'), '-set_serial', '0x' + secrets.token_hex(16), '-days', '1',
                      '-sha256', '-extfile', w('server.ext'), '-out', w('server.pem')]),
            ('pfx', [o, 'pkcs12', '-export', '-in', w('server.pem'), '-inkey', w('server.key'), '-out', w('server.pfx'), '-passout', 'file:' + w('server.password'),
                     '-name', 'localhost']),
            ('verify', [o, 'verify', '-CAfile', w('ca.pem'), w('server.pem')])):
        rc, _, _ = run(argv, 60)
        rec.setdefault('openssl', {})[step] = rc
        if rc != 0:
            refuse(f'credentials: openssl {step} failed')
    val['ca.pem'], val['server.pfx'] = read_private(w('ca.pem')), read_private(w('server.pfx'))
    rec['caCertSha256'], rec['serverCertSha256'] = sha_bytes(val['ca.pem']), sha_bytes(read_private(w('server.pem')))
    for n in sorted(os.listdir(work)):
        os.unlink(w(n))
    os.rmdir(work)
    gens = {gn: g for gn, g in sorted(env['plan']['generations'].items()) if g.get('slot') in SLOTS and g.get('creds')}
    for gn, g in gens.items():
        gd = f'{d}/{gn}'
        os.mkdir(gd, 0o700)
        for n in sorted(g['creds']):
            write_private(f'{gd}/{n}', val[n])
            rec['files'][f'{gn}/{n}'] = len(val[n])
        cu = '999' if g['slot'] == 'pg' else '1000'
        rc, _, _ = run(env['unshare'] + ['chown', '-R', f'{cu}:{cu}', gd], 30)
        # After the chown the directory belongs to a subordinate uid, so the account cannot lstat inside it: read owner, group and mode back INSIDE the
        # user namespace, where the container's own ids are what a container sees.
        names = [gd] + [f'{gd}/{n}' for n in sorted(g['creds'])]
        rc2, out, _ = run(env['unshare'] + ['stat', '-c', '%u %g %a %F', '--'] + names, 30)
        want = [f'{cu} {cu} 700 directory'] + [f'{cu} {cu} 600 regular file'] * len(g['creds'])
        got = out.strip().splitlines() if rc2 == 0 else []
        if rc != 0 or got != want:
            refuse(f'credentials: {gn} is not handed to container uid {cu} (owner, group and mode read in the user namespace: {got[:3]})')
    rec['holders'] = {gn: sorted(g['creds']) for gn, g in gens.items()}
    return rec


def remove_credentials(env, deps, opid):
    d = f"{env['cred_root']}/{opid}"
    if os.path.lexists(d):
        deps['run'](env['unshare'] + ['rm', '-rf', '--one-file-system', d], 60)
    return not os.path.lexists(d)


# ============================================================ r2: the recovery driver (--drive)
def write_request(ckdir, n, label):
    """Checkpoint request req-<n> in finalizer r5's format: {"n": n, "label": slug}, at most 512 bytes, 0600, renamed into place so the finalizer never
    reads a partial request. Returns its bytes."""
    body = json.dumps({'n': n, 'label': label}, sort_keys=True, separators=(',', ':')).encode()
    tmp = f'{ckdir}/tmp-req-{n}'
    write_private(tmp, body)
    os.rename(tmp, f'{ckdir}/req-{n}')
    return body


def ack_problems(raw, n, body):
    """Reasons finalizer r5's ack-<n> does not let the driver act: it must name this request's sha256, be ok, have every role accepted and a chain head."""
    try:
        ack = json.loads(raw)
    except ValueError:
        return ['not JSON']
    if not isinstance(ack, dict):
        return ['not an object']
    want = {'n': n, 'requestSha256': sha_bytes(body), 'ok': True, 'roles': {r_: 'accepted' for r_ in ROLES}}
    why = [f'{k} is {json.dumps(ack.get(k), sort_keys=True)[:120]}' for k, v in want.items() if ack.get(k) != v]
    if not HEX64.match(str(ack.get('chainHead'))):
        why.append('no chain head')
    return why


def readback_extract(d, role, g):
    """Readback custody: finalizer r5's extracted copy of a role's /out at a checkpoint exists, holds a learn-pod-status/1 status of this role and, once the
    status says the steps ran (ready, or terminal ok or signalled), the steps/complete evidence. Returns the phase and a digest of the file manifest."""
    try:
        st = json.loads(read_private(f'{d}/.status', 4096).rstrip(b' \n'))
    except (OSError, ValueError, Refuse):
        raise Abort(f'custody readback: no readable status for {role} in checkpoints/{os.path.basename(os.path.dirname(d))}')
    if not isinstance(st, dict) or st.get('schema') != 'learn-pod-status/1' or st.get('role') != role:
        raise Abort(f'custody readback: another status for {role}')
    if (g.get('steps') or g.get('service')) and st.get('phase') in ('ready', 'terminal') and st.get('reason') in (None, 'ok', 'signalled') \
            and not os.path.isfile(f'{d}/steps/complete'):
        raise Abort(f'custody readback: no step evidence of {role}')
    files = []
    for root, _, fs in os.walk(d):
        for f in sorted(fs):
            p = os.path.join(root, f)
            files.append([os.path.relpath(p, d), sha_bytes(read_private(p, 64 << 20))])
    return {'phase': st.get('phase'), 'files': len(files), 'manifestSha256': sha_bytes(json.dumps(sorted(files)).encode())}


class Abort(Exception):
    pass


def backup_identity_from(extract_host):
    """host-init's backup identity from its init stdout as the finalizer extracted it into custody (Host 5d525ad2 Program.fs:52-58 prints one compact JSON
    object, keys in alphabetical order). Parsed as JSON, not matched as bytes: exactly one object, one line, exactly the keys backupIdentity and schema
    (no duplicate), the host-init/1 schema and a lower-case UUID. Anything else aborts."""
    try:
        raw = read_private(f'{extract_host}/{INIT_STDOUT_REL}', 4096)
    except (OSError, Refuse):
        raise Abort('custody readback: no init stdout of host-init')

    def pairs(kv):
        if len({k for k, _ in kv}) != len(kv):
            raise ValueError('duplicate key')
        return dict(kv)
    try:
        text = raw.decode('utf-8')
        if text.endswith('\n'):
            text = text[:-1]
        if '\n' in text:
            raise ValueError('more than one line')
        j = json.loads(text, object_pairs_hook=pairs)
    except (UnicodeDecodeError, ValueError):
        j = None
    if not (isinstance(j, dict) and set(j) == {'backupIdentity', 'schema'} and j['schema'] == INIT_SCHEMA and isinstance(j['backupIdentity'], str)
            and UUID.fullmatch(j['backupIdentity'])):
        raise Abort('custody readback: host-init\'s init stdout is not exactly one host-init/1 object')
    return j['backupIdentity']


class Driver:
    """Runs PLAN against this op's pod. Every container is created by ctr_argv; every destructive act on a role container needs custody first (the
    checkpoint handshake with finalizer r5, read back); every action is logged with a hash chain."""

    def __init__(self, env, deps, opid, ident, nonce, sid):
        self.env, self.deps, self.opid, self.ident, self.nonce, self.sid = env, deps, opid, ident, nonce, sid
        self.run, self.pod, self.gens = deps['run'], f'learn-pod-{opid}', env['plan']['generations']
        p = paths(env)
        self.ckdir, self.cust, self.logdir = f"{p['checkpoints']}/{opid}", f"{p['custody']}/{opid}", f"{p['drive']}/{opid}"
        self.slots, self.ck, self.head, self.lines, self.t0 = {s: None for s in SLOTS}, 0, '0' * 64, 0, deps['monotonic']()
        self.unit_cg, self.bid = None, None

    # ---- log, clock, reads
    def log(self, event, **kv):
        entry = {'utc': self.deps['now']().isoformat(), 'event': event, **kv, 'prev': self.head}
        b = json.dumps(entry, sort_keys=True, separators=(',', ':'))
        self.head, self.lines = sha_bytes(b.encode()), self.lines + 1
        with open(f'{self.logdir}/drive.jsonl', 'a') as f:
            f.write(b + '\n')
            f.flush()
            os.fsync(f.fileno())
        flat = ' '.join(f'{k}={v}' for k, v in kv.items() if isinstance(v, (str, int)) and not isinstance(v, bool))
        print(f'FSGG {self.nonce} DRIVE {event} {flat}'.rstrip(), flush=True)

    def left(self):
        return DRIVE_BUDGET_S - (self.deps['monotonic']() - self.t0)

    def until(self, pred, timeout, what):
        t = self.deps['monotonic']()
        while True:
            v = pred()
            if v:
                return v
            if self.deps['monotonic']() - t > min(timeout, max(self.left(), 0)):
                raise Abort(f'timed out: {what}')
            self.deps['sleep'](1)

    def inspect(self, name, fmt):
        rc, out, _ = self.run(['podman', 'inspect', '--format', fmt, name], 15)
        return out.strip() if rc == 0 else None

    def cat(self, path, cap=1 << 16):
        rc, out, _ = self.run(self.env['unshare'] + ['cat', path], 15, cap)
        return out if rc == 0 else None

    def status(self, slot):
        s = self.slots[slot]
        raw = self.cat(f"{self.env['proc']}/{s['pid']}/root/out/{slot}/.status", 8192) if s else None
        try:
            j = json.loads(raw.rstrip(' \n')) if raw else None
        except ValueError:
            return None
        return j if isinstance(j, dict) and j.get('schema') == 'learn-pod-status/1' and j.get('role') == slot else None

    def done(self, slot):
        s = self.slots[slot]
        p = f"{self.env['proc']}/{s['pid']}/root/out/{slot}/.done"
        return self.run(self.env['unshare'] + ['test', '-f', p], 10)[0] == 0 and self.run(self.env['unshare'] + ['test', '-L', p], 10)[0] != 0

    def listening(self):
        pid = next((s['pid'] for s in self.slots.values() if s and s['running']), None)
        ports = set()
        for t in ('tcp', 'tcp6'):
            for line in ((self.cat(f"{self.env['proc']}/{pid}/net/{t}") or '').splitlines()[1:] if pid else []):
                w = line.split()
                if len(w) > 3 and w[3] == '0A' and ':' in w[1]:
                    ports.add(int(w[1].rsplit(':', 1)[1], 16))
        return ports

    def cgroup_of(self, pid):
        try:
            return next(l.split('::', 1)[1].strip() for l in self.deps['read'](f"{self.env['proc']}/{pid}/cgroup").splitlines() if l.startswith('0::'))
        except (OSError, StopIteration):
            return None

    # ---- actions
    def a_start(self, gname):
        g = self.gens[gname]
        slot = g['slot']
        if self.slots[slot] is not None:
            raise Abort(f'start {gname}: the {slot} slot is occupied')
        argv = ctr_argv(self.env, self.opid, gname, slot, self.sid)
        if BID_REF in argv:                                     # host-restart's serve: host-init's backup identity, read back from custody
            if not self.bid:
                raise Abort(f'start {gname}: no backup identity of host-init in custody')
            argv = [self.bid if x == BID_REF else x for x in argv]
        rc, _, err = self.run(argv, 120)
        if rc != 0:
            raise Abort(f'start {gname}: {start_errno(err)}')
        self.adopt(slot, gname)

    def adopt(self, slot, gname):
        name = f'{self.pod}-{slot}'
        cid, pid = self.inspect(name, '{{.Id}}'), self.inspect(name, '{{.State.Pid}}')
        if not cid or not pid or pid == '0':
            raise Abort(f'{name} is not running after its start')
        kind = 'pg' if slot == 'pg' else 'app'
        uids = proc_uids(self.deps, self.env['proc'], pid)
        self.slots[slot] = {'gen': gname, 'name': name, 'id': cid, 'pid': pid, 'cgroup': self.cgroup_of(pid), 'dirty': slot != 'pg', 'running': True, 'killed': False}
        ok = len(uids) == 4 and all(u.isdigit() and int(u) == self.env['host_uid'][kind] for u in uids)
        self.log('start', slot=slot, generation=gname, container=cid, hostUids=uids, cgroup=self.slots[slot]['cgroup'])
        if not ok:
            raise Abort(f'{name}: payload host uids {uids} are not {self.env["host_uid"][kind]}')

    def a_wait_ready(self, slot):
        def ready():
            st = self.status(slot)
            if st and st.get('phase') == 'terminal':
                raise Abort(f'{slot} ended before it was ready ({st.get("reason")})')
            return st is not None and st.get('phase') == 'ready'
        self.until(ready, 600, f'{slot} ready')
        self.log('ready', slot=slot)

    def a_wait_listen(self, slot, port):
        self.until(lambda: port in self.listening(), 300, f'{slot} listening on {port}')
        self.log('listening', slot=slot, port=port)

    def a_wait_done(self, slot):
        self.until(lambda: self.done(slot), 900, f'{slot} done')
        st = self.status(slot) or {}
        self.log('done', slot=slot, reason=str(st.get('reason')), exit=st.get('exit') if isinstance(st.get('exit'), int) else -1)
        if st.get('reason') != 'ok':
            raise Abort(f'{slot} ended {st.get("reason")} (exit {st.get("exit")}), not ok')

    def a_assert_listeners(self):
        ports = self.listening()
        self.log('listeners', ports=sorted(ports))
        if not (REQUIRED_PORTS <= ports <= DECLARED_PORTS):
            raise Abort(f'listeners {sorted(ports)} are not the declared {sorted(DECLARED_PORTS)} (required {sorted(REQUIRED_PORTS)})')

    def a_assert_cgroups(self):
        unit = self.unit_cg
        rows = {s: v['cgroup'] for s, v in self.slots.items() if v and v['running']}
        ipid = self.inspect(f'{self.pod}-infra', '{{.State.Pid}}')
        rows['infra'] = self.cgroup_of(ipid) if ipid and ipid != '0' else None
        inside = {k: bool(v and unit and v.startswith(unit + '/')) for k, v in rows.items()}
        self.log('cgroups', unit=unit, inside=inside)
        if not all(inside.values()):
            raise Abort('a pod process is outside the pod unit cgroup: ' + ','.join(k for k, v in inside.items() if not v))

    def a_checkpoint(self, label):
        for r_ in ROLES:
            if not (self.slots[r_] and self.slots[r_]['running']):
                raise Abort(f'checkpoint {label}: the {r_} slot is not running')
        n = self.ck + 1
        if n > MAX_CHECKPOINTS:
            raise Abort('checkpoint budget exhausted')
        body = write_request(self.ckdir, n, label)
        self.log('checkpoint-request', n=n, label=label, requestSha256=sha_bytes(body))
        ackp = f'{self.ckdir}/ack-{n}'

        def parsed():
            # the ack may be seen before it is fully written: retry until it parses as JSON, within the same bound
            try:
                raw_ = read_private(ackp, 65536)
                json.loads(raw_)
                return raw_
            except (OSError, ValueError, Refuse):
                return None
        raw = self.until(parsed, 600, f'ack-{n}')
        why = ack_problems(raw, n, body)
        if why:
            raise Abort(f'ack-{n} does not acknowledge this request with every role accepted: {why}')
        custody = {r_: readback_extract(f'{self.cust}/checkpoints/{n}/extract-{r_}', r_, self.gens[self.slots[r_]['gen']]) for r_ in ROLES}
        if self.bid is None and self.slots['host']['gen'] == 'host-init':
            self.bid = backup_identity_from(f'{self.cust}/checkpoints/{n}/extract-host')
            self.log('backup-identity', n=n, backupIdentity=self.bid)
        self.ck = n
        for r_ in ROLES:
            self.slots[r_]['dirty'] = False
        self.log('checkpoint-ack', n=n, label=label, ackSha256=sha_bytes(raw), chainHead=json.loads(raw)['chainHead'], custody=custody)

    def a_stop(self, slot):
        s = self.slots[slot]
        sig = 'INT' if slot == 'pg' else 'TERM'                     # pg: fast shutdown (carry-forward item 2)
        rc = self.run(['podman', 'kill', '--signal', sig, s['name']], 15)[0]
        if slot == 'pg':
            self.until(lambda: self.inspect(s['name'], '{{.State.Status}}') in ('exited', 'stopped'), 60, 'pg stopped')
            s['running'] = False
            self.log('stop', slot=slot, signal=sig, rc=rc, exitCode=self.inspect(s['name'], '{{.State.ExitCode}}') or '')
            return
        self.until(lambda: self.done(slot), 60, f'{slot} done after TERM')
        s['dirty'] = True
        st = self.status(slot) or {}
        self.log('stop', slot=slot, signal=sig, rc=rc, reason=str(st.get('reason')))

    def a_kill(self, slot):
        s = self.slots[slot]
        if slot != 'pg' and s['dirty']:
            raise Abort(f'kill {slot}: custody does not hold its current state')
        rc = self.run(['podman', 'kill', '--signal', 'KILL', s['name']], 15)[0]
        self.until(lambda: self.inspect(s['name'], '{{.State.Status}}') in ('exited', 'stopped'), 60, f'{slot} exited after KILL')
        s['running'], s['killed'] = False, True
        self.log('kill', slot=slot, rc=rc)

    def a_reconcile(self, slot):
        """Restart reconciliation's precondition (carry-forward item 5): the killed container's own cgroup holds no process, so nothing of it (the Runner child
        included) survives into the restart. There is no shared PID namespace, so this is not orphan detection across containers."""
        s = self.slots[slot]
        cg = f"{self.env['cgroot']}{s['cgroup']}" if s.get('cgroup') else None
        try:
            procs = self.deps['read'](f'{cg}/cgroup.procs').split() if cg else None
        except FileNotFoundError:
            procs = []
        except OSError:
            procs = None
        state = self.inspect(s['name'], '{{.State.Status}}')
        self.log('reconcile', slot=slot, state=state or '', survivors=len(procs) if procs is not None else -1)
        if procs is None or procs or state not in ('exited', 'stopped'):
            raise Abort(f'reconcile {slot}: the killed container still has processes or is not exited')

    def a_remove(self, slot):
        s = self.slots[slot]
        if s['dirty']:
            raise Abort(f'remove {slot}: custody does not hold its state')
        rc = self.run(['podman', 'rm', '-f', '-t', '3', s['name']], 30)[0]
        gone = self.run(['podman', 'container', 'exists', s['name']], 15)[0] == 1
        self.log('remove', slot=slot, rc=rc, gone=gone)
        if not gone:
            raise Abort(f'remove {slot}: the container still exists')
        self.slots[slot] = None

    def a_restart(self, slot):
        s = self.slots[slot]
        rc = self.run(['podman', 'start', s['name']], 60)[0]
        if rc != 0:
            raise Abort(f'restart {slot} failed')
        self.adopt(slot, s['gen'])

    def a_sleep(self, n):
        t = self.deps['monotonic']()
        while self.deps['monotonic']() - t < n:
            if self.left() <= 0:
                raise Abort('the drive budget ran out')
            self.deps['sleep'](1)
        self.log('slept', seconds=n)

    def execute(self):
        for i, a in enumerate(self.env['plan']['actions']):
            if self.left() <= 0:
                raise Abort('the drive budget ran out')
            fn = getattr(self, 'a_' + a[0].replace('-', '_'))
            fn(*a[1:])

    def abort(self, why):
        """Fail closed so that the finalizer triggers and archives: never remove evidence; replace only a role container that is no longer running (its
        tmpfs is already gone) and fill an empty slot with a hold container; then TERM each role that is NOT yet done. A role that is done is blocking with its
        /out intact, and a second TERM would make it exit and lose that /out before the finalizer archives it (the role contract). Before the replay, feed
        ends `signalled` and finalizer r5 cannot record a success; after the replay it can, so Root's readback reads the drive log and the pod unit's
        ExecMainStatus (the success rule itself is #5175's)."""
        self.log('abort', reason=str(why)[:300])
        for r_ in ROLES:
            try:
                s = self.slots[r_]
                if s is not None and not s['running']:
                    self.run(['podman', 'rm', '-f', '-t', '0', s['name']], 30)
                    self.slots[r_] = s = None
                if s is None:
                    rc = self.run(ctr_argv(self.env, self.opid, 'hold', r_, self.sid), 120)[0]
                    if rc == 0:
                        self.adopt(r_, 'hold')
            except Exception as e:
                self.log('abort-fill-failed', slot=r_, error=type(e).__name__)
        termed = []
        for r_ in ROLES:
            try:
                s = self.slots[r_]
                if s is not None and s['running'] and not self.done(r_):
                    self.run(['podman', 'kill', '--signal', 'TERM', s['name']], 15)
                    termed.append(r_)
            except Exception as e:
                self.log('abort-term-failed', slot=r_, error=type(e).__name__)
        self.log('abort-stopped', termed=','.join(termed))


def drive_authority(env, deps, opid, ident):
    """Who may drive: this file, as uid 30034, inside learn-pod-<id>.service, while learn-fin-<id>.service is active, under an admission naming THIS file's
    sha256 for <id> and <ident>, after the --run created the input-read marker, with the live claim naming <id>. Returns (self sha, claim, sid, sealed files)."""
    p = paths(env)
    self_sha = sha_bytes(open(os.path.abspath(__file__), 'rb').read())
    if deps['geteuid']() != env['uid']:
        refuse(f"refuse: run as fsgg-learn-qual (uid {env['uid']})")
    if not re.fullmatch(r'[0-9a-f]{32}', deps['environ'].get('FSGG_NONCE', '')):
        refuse('drive: FSGG_NONCE missing')
    try:
        cg = next(l.split('::', 1)[1].strip() for l in deps['read'](env['self_cgroup']).splitlines() if l.startswith('0::'))
    except (OSError, StopIteration):
        cg = ''
    if not cg.endswith(f'/learn-pod-{opid}.service'):
        refuse('drive: not running inside learn-pod-<id>.service')
    rc, out, _ = deps['run'](['systemctl', '--user', 'is-active', f'learn-fin-{opid}.service'], 10)
    if out.strip() != 'active':
        refuse('drive: the finalizer unit is not active')
    try:
        adm = json.loads(read_pinned(f"{env['view']}/admissions/{opid}.admit", env['chain_start'], env['owner_uid'], 65536))
    except ValueError:
        refuse('drive: the admission is not JSON')
    if adm.get('opId') != opid or adm.get('sourceIdentity') != ident or adm.get('operatorSha256') != self_sha:
        refuse('drive: the admission does not name this op, this source and this file')
    want_argv = synthetic_plan_argv(env['plan']) if input_kind(env) == 'synthetic' else plan_argv(env['plan'])
    if adm.get('argvJsonSha256') != sha_bytes(want_argv.encode()):
        refuse('drive: the admission was not given for this plan and input kind')
    if not os.path.isfile(f"{p['markers']}/{opid}/input-read"):
        refuse('drive: no input-read marker; only a --run starts a driver')
    claim, _, sid = verify_claim(env, ident, opid)
    sm = json.loads(read_pinned(f"{p['inputs']}/{sid}.manifest.json", env['chain_start'], env['owner_uid'], 1 << 20))
    return self_sha, cg, sid, sm.get('files') or []


def wait_for_finalizer(deps, drv, opid, R):
    """The finalizer archives, stops pg and removes the pod. The driver leaves only on a DEFINITE state: `podman pod exists` exits 1 (gone), or the
    finalizer unit reads exactly `inactive` or `failed`. Anything else (a timeout, another exit code, an empty answer, an exception) is unknown and the
    driver keeps waiting, bounded by the finalizer's RuntimeMaxSec from the driver's start; leaving early would let the pod unit stop and remove the pod
    in the middle of the finalizer's archive."""
    while deps['monotonic']() - drv.t0 < FIN_RUNTIME_S:
        try:
            if deps['run'](['podman', 'pod', 'exists', drv.pod], 15)[0] == 1:
                R['podGone'] = True
                return
            if deps['run'](['systemctl', '--user', 'is-active', f'learn-fin-{opid}.service'], 10)[1].strip() in ('inactive', 'failed'):
                R['finalizerEnded'] = True
                return
        except Exception:
            pass
        deps['sleep'](2)
    R['finalWaitTimedOut'] = True


def drive_main(opid, ident, env, deps):
    p = paths(env)
    R = {'schema': 'home-learn-pod-drive/1', 'operatorRevision': REVISION, 'opId': opid, 'errors': []}
    code, drv = 5, None
    try:
        R['operatorSha256'], unit_cg, sid, files = drive_authority(env, deps, opid, ident)
        errs, held = validate_plan(env['plan'])
        if errs or held:
            refuse('drive: the plan cannot run: ' + '; '.join(errs + held))
        t = deps['monotonic']()
        while not (os.path.isdir(f"{p['custody']}/{opid}") and os.path.isdir(f"{p['checkpoints']}/{opid}")):
            if deps['monotonic']() - t > 30:
                refuse('drive: the finalizer created no custody or checkpoint directory')
            deps['sleep'](1)
        os.makedirs(p['drive'], 0o700, exist_ok=True)
        try:
            os.mkdir(f"{p['drive']}/{opid}", 0o700)
        except FileExistsError:
            refuse('drive: a driver already ran for this op id')
        drv = Driver(env, deps, opid, ident, deps['environ']['FSGG_NONCE'], sid)
        drv.unit_cg = unit_cg
        drv.log('authority', operatorSha256=R['operatorSha256'], unit=unit_cg, snapshot=sid)
        R['credentials'] = gen_credentials(env, deps, opid, files)
        drv.log('credentials', files=len(R['credentials']['files']), caCertSha256=R['credentials']['caCertSha256'])
        rc, _, err = deps['run'](['podman', 'pod', 'create', '--name', drv.pod, '--network=none', '--share=net', '--infra-name', f'{drv.pod}-infra'], 60)
        if rc != 0:
            raise Abort('pod create failed: ' + start_errno(err))
        drv.log('pod-created', pod=drv.pod)
        drv.execute()
        R['verdict'], code = 'complete', 0
    except BaseException as e:                              # every failure after the authority check ends in the same fail-closed tail
        R['errors'].append(str(e) if isinstance(e, (Abort, Refuse)) else f'exception: {type(e).__name__}: {str(e)[:200]}')
        if drv is not None:
            R['verdict'], code = 'aborted', 3
            try:
                drv.abort(e)
            except BaseException as e2:
                R['errors'].append(f'abort: {type(e2).__name__}: {str(e2)[:200]}')
    if drv is not None:
        R['podGone'] = R['finalizerEnded'] = False
        try:
            wait_for_finalizer(deps, drv, opid, R)
        except BaseException as e:
            R['errors'].append(f'final wait: {type(e).__name__}: {str(e)[:200]}')
        try:
            R['credentialsRemoved'] = remove_credentials(env, deps, opid)
        except BaseException as e:
            R['credentialsRemoved'] = False
            R['errors'].append(f'credential removal: {type(e).__name__}')
        try:
            drv.log('end', verdict=R.get('verdict', 'refused'), credentialsRemoved=int(R['credentialsRemoved']))
        except BaseException as e:
            R['errors'].append(f'log: {type(e).__name__}')
        R['checkpoints'], R['chain'] = drv.ck, {'head': drv.head, 'lines': drv.lines}
        if not R['credentialsRemoved'] and code == 0:
            code = 3
    R['exitCode'] = code
    b = json.dumps(R, indent=1, sort_keys=True)
    print('RESULT-BEGIN')
    print(b)
    print('RESULT-END')
    print(json.dumps({'sha256': sha_bytes(b.encode()), 'exit': code}))
    return code


# ============================================================ r2: arguments, gates, start, main
USAGE = ('usage: --precheck-only|--run --id <op-id> --argv-json <JSON list> --source-identity <64 hex> | --drive --id <op-id> --source-identity <64 hex>'
         ' [--input synthetic]')


def parse_args(argv):
    mode = argv[0] if argv else ''
    opts, i = {}, 1
    while i < len(argv):
        if not argv[i].startswith('--') or i + 1 >= len(argv) or argv[i] in opts:
            refuse(USAGE)
        opts[argv[i]] = argv[i + 1]
        i += 2
    want = {'--id', '--source-identity'} if mode == '--drive' else {'--id', '--argv-json', '--source-identity'}
    if mode == '--drive' and '--input' in opts:                  # #5184: only the driver takes the kind as an argument (the others take it from argv-json)
        if opts['--input'] != 'synthetic' or argv[-2:] != ['--input', 'synthetic']:
            refuse(USAGE)
        want = want | {'--input'}
    if mode not in ('--precheck-only', '--run', '--drive') or set(opts) != want:
        refuse(USAGE)
    if not OPID.match(opts['--id']):
        refuse('bad op id')
    if not HEX64.match(opts['--source-identity']):
        refuse('source identity must be 64 lowercase hex')
    if mode == '--drive':
        return mode, opts['--id'], None, 'synthetic' if '--input' in opts else 'real', opts['--source-identity']
    try:
        argv_list = json.loads(opts['--argv-json'])
    except ValueError:
        refuse('argv-json is not JSON')
    if not isinstance(argv_list, list) or not argv_list or not all(isinstance(x, str) for x in argv_list):
        refuse('argv-json must be a non-empty JSON list of strings')
    return mode, opts['--id'], opts['--argv-json'], argv_list, opts['--source-identity']


def default_deps():
    def run(argv, timeout=60, cap=1 << 22):
        try:
            p = subprocess.run(argv, capture_output=True, timeout=timeout, cwd='/')
            return p.returncode, p.stdout[:cap].decode('utf-8', 'replace'), p.stderr[-2000:].decode('utf-8', 'replace')
        except subprocess.TimeoutExpired:
            return None, '', 'timeout'

    def read(path):
        with open(path) as f:
            return f.read(1 << 22)
    return {'run': run, 'geteuid': os.geteuid, 'now': lambda: datetime.now(timezone.utc), 'sleep': time.sleep, 'monotonic': time.monotonic,
            'nonce': lambda: secrets.token_hex(16), 'token': secrets.token_urlsafe, 'read': read, 'statvfs': os.statvfs, 'kernel': lambda: os.uname().release,
            'environ': os.environ}


def census(deps):
    """Units, pods (learn-pod-* and the precheck's learn-pre-*) and learn-op-* orphans. A census that fails refuses."""
    run = deps['run']
    rc, out, _ = run(['systemctl', '--user', 'list-units', '--all', '--no-legend', '--plain', 'learn-op-*', 'learn-fin-*', 'learn-pod-*'], 30)
    if rc != 0:
        refuse('the unit census failed')
    rows = parse_unit_rows(out)
    rc2, pods, _ = run(['podman', 'pod', 'ps', '-a', '--format', '{{.Name}}'], 30)
    if rc2 != 0:
        refuse('the pod census failed')
    leftover = sorted(n for n in pods.split() if n.startswith(('learn-pod-', 'learn-pre-')))
    rc3, names, _ = run(['podman', 'ps', '-a', '--filter', 'name=^learn-op-', '--format', '{{.Names}}'], 30)
    if rc3 != 0:
        refuse('the container census failed')
    active_ops = {r_[0][:-len('.service')] for r_ in rows if r_[0].startswith('learn-op-') and r_[2] == 'active'}
    orphans = sorted(n for n in names.split() if n not in active_ops)
    return rows, exclusion_conflicts(rows), leftover, orphans


def gates(env, deps, R, mode, opid, argv_raw, ident):
    p = paths(env)
    me = os.path.abspath(__file__)
    self_sha = sha_bytes(open(me, 'rb').read())
    if deps['geteuid']() != env['uid']:
        refuse(f"refuse: run as fsgg-learn-qual (uid {env['uid']})")
    os.umask(0o077)
    if argv_input_kind(argv_raw, env['plan']) is None:                       # main() took the input kind from this same argv
        refuse('argv-json must name this operator\'s plan: ' + plan_argv(env['plan']) + ' (or, for a synthetic-input op, ' + synthetic_plan_argv(env['plan']) + ')')
    # 11 plan (static, so it is checked with the identity)
    errs, held = validate_plan(env['plan'])
    R['gates']['plan'] = {'errors': errs, 'held': held, 'checkpoints': sum(1 for a in env['plan']['actions'] if a[0] == 'checkpoint')}
    if errs:
        refuse('the plan is invalid: ' + '; '.join(errs))
    if held:
        refuse('the plan has held steps: ' + '; '.join(held))
    if not env['app_image'] or not env['pg_image']:
        refuse('no image ids are pinned in this revision (they are admitted after the build and the pull)')
    if not env['finalizer_sha']:
        refuse('the finalizer pin is empty')
    # 2 pins
    for iid in image_ids(env):
        rc, out, _ = deps['run'](['podman', 'image', 'inspect', '--format', '{{.Id}}', iid], 30)
        if rc != 0 or out.strip() != iid:
            refuse(f'image {iid[:12]} is not present locally by Id')
    if sha_bytes(read_pinned(env['fin'], env['chain_start'], env['owner_uid'], 1 << 20)) != env['finalizer_sha']:
        refuse('the finalizer does not hash to its pin')
    if sha_bytes(read_pinned(env['seccomp'], env['chain_start'], env['owner_uid'], 1 << 20)) != env['seccomp_sha']:
        refuse('the seccomp profile does not hash to its pin')
    R['gates']['pins'] = {'imageIds': image_ids(env), 'appImage': env['app_image'], 'pgImage': env['pg_image'], 'pgIndexDigest': PG_INDEX_DIGEST,
                          'finalizerSha256': env['finalizer_sha'], 'operatorSha256': self_sha, 'planSha256': plan_digest(env['plan'])}
    # 3 slice
    def rd(x):
        try:
            return open(f"{env['slice']}/{x}").read().strip()
        except OSError:
            return None
    lim = {'memoryMax': rd('memory.max'), 'pidsMax': rd('pids.max')}
    R['gates']['sliceLimits'] = {**lim, 'expected': {'memoryMax': str(SLICE_MEMORY_MAX), 'pidsMax': str(SLICE_PIDS_MAX)}}
    if lim != {'memoryMax': str(SLICE_MEMORY_MAX), 'pidsMax': str(SLICE_PIDS_MAX)}:
        refuse(f'slice limits absent: {lim}')
    # 4 lock
    os.makedirs(os.path.dirname(p['lock']), 0o700, exist_ok=True)
    lfd, waited = take_lock(p['lock'], env['lock_wait_s'], deps)
    R['gates']['operatorLockWaitSeconds'] = waited
    HELD_LOCKS.append(lfd)                                   # held open to process exit; tests close it
    # 5 exclusion
    rows, conflicts, leftover, orphans = census(deps)
    R['gates']['exclusion'] = {'conflicts': conflicts, 'leftoverPods': leftover, 'orphanContainers': orphans}
    if conflicts:
        refuse('exclusion refused: an op or pod is active or transitioning: ' + ','.join(conflicts))
    if leftover:
        refuse('exclusion refused: a pod exists: ' + ','.join(leftover))
    if orphans:
        refuse('exclusion refused: orphan learn-op-* containers: ' + ','.join(orphans))
    # 6 custody
    used = used_ids(opid, p['custody'], p['evidence']) + [d for d in (f"{p['checkpoints']}/{opid}", f"{p['drive']}/{opid}", f"{p['markers']}/{opid}",
                                                                      f"{env['cred_root']}/{opid}") if os.path.lexists(d)]
    if used or any(r_[0].startswith((f'learn-op-{opid}', f'learn-fin-{opid}', f'learn-pod-{opid}')) for r_ in rows):
        refuse('op id already used: ' + ','.join(used))
    # 7 acks
    R['gates']['retirementAcks'], unacked = retirement_gate(p['evidence'], p['ack_dir'], env['chain_start'], env['owner_uid'])
    if unacked:
        refuse('retirement-failure-unacknowledged: ' + ','.join(unacked))
    # 8 evidence size
    ev = evidence_usage(p['evidence'])
    ev['maxBytes'] = EVID_MAX_BYTES
    R['gates']['evidenceUsage'] = ev
    if ev.get('error') or ev['bytes'] > EVID_MAX_BYTES or ev['entries'] > EVID_MAX_ENTRIES:
        refuse(f"evidence size refused: {ev['bytes']} B in {ev['entries']} entries (max {EVID_MAX_BYTES} B)")
    # 9 admission (verbatim reader)
    why = check_admission(env['view'], opid, sha_bytes(argv_raw.encode()), ident, self_sha, env['finalizer_sha'], image_ids(env), env['chain_start'],
                          env['owner_uid'], deps['now']())
    R['gates']['admission'] = {'reasons': why}
    if why:
        refuse('admission refused: ' + '; '.join(why))
    # 10 headroom
    hg, vals = headroom(env['slice'], env['meminfo'], p['custody'])
    R['gates']['headroom'] = {**hg, 'values': vals}
    if not all(hg.values()):
        refuse('headroom refused: ' + ','.join(k for k, v in hg.items() if not v))
    # 12 the loop volume
    R['gates']['volume'] = volume_precheck(env, deps, opid)
    return self_sha


def start_units(env, deps, R, opid, ident, nonce, sid):
    """The START (called by begin_input_access after the marker and inputMounted): finalizer unit first; only if it started, the pod unit."""
    R['containers'] += planned_containers(env, opid, sid)
    a = deps['run'](fin_unit_argv(env, opid, ident, nonce), 30)
    R['start'] = {'finalizerStart': a[0], 'podStart': None}
    if a[0] != 0:
        refuse('the finalizer unit did not start; the pod unit was not started')
    b = deps['run'](pod_unit_argv(env, opid, ident, nonce), 30)
    R['start']['podStart'] = b[0]
    if b[0] != 0:
        refuse('the pod unit did not start (the finalizer retires the partial start)')
    deps['sleep'](2)
    R['units'] = {u: deps['run'](['systemctl', '--user', 'is-active', u], 10)[1].strip() for u in (f'learn-fin-{opid}.service', f'learn-pod-{opid}.service')}


def main(argv, env=None, deps=None):
    env = dict(PROD, **(env or {}))
    deps = dict(default_deps(), **(deps or {}))
    if argv[:1] == ['--drive']:
        try:
            _, opid, _, kind, ident = parse_args(argv)
            env['input'] = kind
        except Refuse as e:
            print(json.dumps({'schema': 'home-learn-pod-drive/1', 'errors': [str(e)], 'exitCode': 5}))
            return 5
        os.umask(0o077)
        return drive_main(opid, ident, env, deps)
    R = {'schema': SCHEMA, 'operatorRevision': REVISION, 'utc': deps['now']().isoformat(), 'gates': {'inputMounted': False}, 'containers': [], 'errors': []}
    code = 0
    try:
        mode, opid, argv_raw, argv_list, ident = parse_args(argv)
        if mode == '--drive':
            refuse(USAGE)
        R.update(mode=mode, opId=opid, argv=argv_list, sourceIdentity=ident)
        env['input'] = argv_input_kind(argv_raw, env['plan']) or 'real'          # #5184: the kind is what the admitted argv names; gates() refuses any other
        R['inputProvenance'] = env['input']
        R['operatorSha256'] = gates(env, deps, R, mode, opid, argv_raw, ident)
        pod_precheck(env, deps, R, opid)                                         # 13: precheck pod, both modes (as operator r6 runs its precheck op)
        if mode == '--run':
            _, conflicts, leftover, _ = census(deps)
            R['gates']['startCensus'] = {'conflicts': conflicts, 'leftoverPods': leftover}
            if conflicts or leftover:
                refuse('exclusion refused at the start: ' + ','.join(conflicts + leftover))
            claim, claim_sha, sid = verify_claim(env, ident, opid)
            R['gates']['claim'] = {'generation': claim.get('generation'), 'snapshotId': sid, 'claimSha256': claim_sha}
            # the replay's check list is built from the sealed manifest by the driver; refuse a path it cannot carry HERE, before the marker consumes
            # the claim (metadata only: the sealed copy of the input is not opened)
            sm = json.loads(read_pinned(f"{paths(env)['inputs']}/{sid}.manifest.json", env['chain_start'], env['owner_uid'], 1 << 20))
            R['gates']['replayCheckList'] = {'files': len(spool_checklist(sm.get('files') or []).splitlines())}
            nonce = deps['nonce']()
            R['runNonceSha256'] = sha_bytes(nonce.encode())                     # recorded BEFORE anything can start
            begin_input_access(env, R, opid, ident, lambda c, s: start_units(env, deps, R, opid, ident, nonce, s), deps['now'])
    except Refuse as e:
        R['errors'].append(str(e))
        code = 5
    except Exception as e:
        R['errors'].append(f'exception: {type(e).__name__}: {str(e)[:200]}')
        code = 5
    R['exitCode'] = code
    b = json.dumps(R, indent=1, sort_keys=True)
    print('RESULT-BEGIN')
    print(b)
    print('RESULT-END')
    print(json.dumps({'sha256': sha_bytes(b.encode()), 'exit': code}))
    return code


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
