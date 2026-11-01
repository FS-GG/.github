# LEARN finite profile

Source home for the LEARN finite host profile (#4745) and the finite executor that extends it
(`docs/coordination/2026-10-30-learn-finite-executor-pod-design.md`). Until this directory the profile existed
only as installed, hash-pinned bytes (#4745 closure gap 1). Refs FS-GG/.github#4647.

This directory does not install anything. Root admits every install, build and run separately.

## `baseline/`: the admitted bytes

`baseline/BASELINE.json` lists the 20 files of the install journal `home-learn-install-journal/1`: installed path, mode
and sha256 for each, and whether the bytes are imported here. Ten are imported byte-identical: operator r5, finalizer
r4, the retirement-ack writer, builder r4, the four text files of reader image r4, the seccomp profile and the slice
drop-in. Ten are not, each with a recorded reason:

- the static `learn-probe` binary (772,112 bytes). Its C source and build recipe **are** in fdev private custody and are
  imported (below), and the binary is recorded as built from them (`builtFrom`). No rebuild was run here, so rebuild
  reproducibility is not claimed; the admitted artifact `66f8254e…` is identified by sha256 only;
- nine superseded revisions. Three of the four reader image r1 files (`learn-dotnet-shim.sh`, `learn-entrypoint.sh`,
  `learn-slot.sh`) are the same bytes as the reader image r4 files of the same name (`sameBytesAs`, checked); the r1
  `Containerfile` differs.

`baseline/source/` holds the two custody sources of the probe, imported byte-identical: `learn-probe.c` (`2aec965b…`) and
`learn-build-probe.sh` (`ac69adf0…`). They are not in the install journal because they were never installed.

### Custody and the install scripts

The admitted source is preserved in fdev private custody
(`~/.local/state/v3-storage/learn-finite-profile-source-20261023/Containers/learn-qualification/finite-profile/`,
[#4650](https://github.com/FS-GG/.github/issues/4650#issuecomment-6362358351)). `SOURCE-PINS.json` there pins 24 files; all
24 match the files in custody (checked 2026-10-30), and its accepted installed manifest `c0ff55ff…` equals the sha256 of
the installed `install-manifest.json`.

| Install script | What it is | Source |
|---|---|---|
| `8d16d02a…` | C1 r8b installer (`learn-install-r8.py`); the script of the journal add set `operator-r4`, the last install run | in custody, recorded by hash, **not imported** |
| `b9792a07…` | the journal's `installScriptSha256`: the script that created the journal (the first `--install`) | **not in custody** |
| `972f8435…`, `7f4dd900…`, `2437ab0f…` | journal add sets `r4-build`, `operator`, `operator-r3` | **not in custody** |

`learn-install-r8.py` is not published: it embeds private Home source paths (`/home/<operator>/.local/state/...` bundle
directories) and the operator account name in 12 places. It is recorded in `BASELINE.json` with its hash and role; a
redacted import is possible with the recorded-redaction rule if a reviewer needs the text. `SOURCE-PINS.json` is recorded
by hash because it duplicates this manifest for the imported files. The governing spec v11r2 (`ec0a031d…`) is recorded by
hash and already cited by the Atlas record `learn/home-finite-host-profile`. The sources of the first install script and of
the three earlier add-set scripts remain unavailable.

Check it:

```text
python3 deployment/learn-finite-profile/verify_baseline.py                                       # repository
python3 deployment/learn-finite-profile/verify_baseline.py --install-journal <install-manifest.json> --custody-dir <dir>
python3 tests/learn-finite-profile/run.py                                                          # gates
```

The second form needs the private `install-manifest.json`, the installed files and the custody directory. It compares the
manifest with the journal (same file set, sha256, mode), hashes every installed file, checks each imported file against
the installed bytes after any recorded redactions, and hashes every custody source and reference.

## Secrets and identifier scan

Scanned: the 12 imported files, before import and again on every run of the gate. The scan has 16 classes: credential
tokens, bare `Bearer <token>`, private keys, secret assignments, email, IPv4, IPv6, MAC addresses, UUIDs, URLs (public
`https://github.com/FS-GG/` URLs are allowed), host names (dotted names with a service TLD, and `localhost`), `/home/<user>`
paths, SSH keys, `@` mentions, and a person-context detector (a role word such as owner, operator or "runs as" followed by
a name token), plus a list of person names in `BASELINE.json`.

**The scan detects classes. It cannot prove a file is clean:** a name, a private path or a codename has no reliable pattern.
Every imported file therefore also needs a manual read recorded in `scan.manualRead`, keyed by the file's path and bytes
(each record needs a non-empty `file` that names an imported file at those exact bytes, and a non-empty `reader`), and the
gate refuses an imported file without one. **Readers are self-asserted:** a record is a claim by whoever recorded it, not an
authenticated identity or an independent attestation.

`scan.personNames` is published with the manifest, so it may only hold names that are already public. Never add a name to
catch it: the list itself would disclose it. The ten journal files were read in the independent review of #4997; the two probe
sources were read in full by the worker who imported them.

Result (2026-10-30): **0 unexplained findings, 0 redactions.** Six matches are allowlisted in `BASELINE.json`, each with its
reason: `user@30034.service` (a systemd unit name), `0::` twice (the cgroup v2 line prefix read from `/proc/self/cgroup`),
`localhost` (the podman image namespace in `localhost/learn-reader:r4`), and the operator account name in a comment of
`learn-build-probe.sh` ("Runs as eugen"), which is already public in the #4647 issue body. An earlier entropy pass found only
the path `REAL_DOTNET=/usr/share/dotnet/dotnet` and sha256 digests. The files name the machine as "Home (OS hostname Main)" and
use the account path under `/mnt/storage`; both are documented in Atlas (`environment/home-subsystems-migrated-into-fdev`).

Because nothing was redacted, byte-identity to `install-manifest.json` is shown directly on the installed copy. The
redaction mechanism exists for later imports: a redacted file records, per line, the line number, the sha256 of the original
line and the replacement, so the private check can reconstruct the repository file from the installed bytes without the
secret appearing in the record.

## What follows

Later changes, each a separate PR reviewed against these bytes: ack writer r2 (`ack/`), operator r6 (`operator/`), seal and claim tooling (`seal/`),
the pod operator gate layer (`pod-operator/`), the pod image and role entrypoint (`pod-image/`), finalizer r5 (`finalizer/`), and the pod operator's start with its recovery driver.

The install route on fdev, with the files, the journal, the Root-executed steps, the receipts and the rollback, is
[`INSTALL-FDEV.md`](INSTALL-FDEV.md).
