# LEARN finite profile

Source home for the LEARN finite host profile (#4745) and the finite executor that extends it
(`docs/coordination/2026-10-30-learn-finite-executor-pod-design.md`). Until this directory the profile existed
only as installed, hash-pinned bytes (#4745 closure gap 1). Refs FS-GG/.github#4647.

This directory does not install anything. Root admits every install, build and run separately.

## `baseline/`: the admitted bytes

`baseline/BASELINE.json` lists the 20 files of the install journal `home-learn-install-journal/1` (install script
`b9792a07…`): installed path, mode and sha256 for each, and whether the bytes are imported here. Ten are imported
byte-identical: operator r5, finalizer r4, the retirement-ack writer, builder r4, the four text files of reader
image r4, the seccomp profile and the slice drop-in. Ten are not: the static `learn-probe` binary (its C source is
not in custody here) and nine superseded revisions. Each has a recorded reason.

Check it:

```text
python3 deployment/learn-finite-profile/verify_baseline.py                                   # repository
python3 deployment/learn-finite-profile/verify_baseline.py --install-journal <install-manifest.json>   # + private install
python3 tests/learn-finite-profile/run.py                                                      # gates
```

The second form needs the private `install-manifest.json` and the installed files. It compares the manifest with the
journal (same file set, sha256, mode), hashes every installed file, and checks each imported file against the
installed bytes after any recorded redactions.

## Secrets and identifier scan (2026-10-30)

Scanned: the 10 imported files, before import and again on every run of the gate. Classes: credential tokens, private
keys, secret assignments, email addresses, IPv4 addresses, private host names (`*.internal`, `*.lan`, `*.local`,
`*.home`), person names, `/home/<user>` paths and SSH keys.

Result: **0 findings, 0 redactions.** One pattern match is allowlisted in `BASELINE.json` with its reason:
`user@30034.service` in `learn-finalizer-r4.py` is a systemd unit name, not an address. A separate entropy pass over
the same files found three long tokens that are the path `REAL_DOTNET=/usr/share/dotnet/dotnet` and eleven sha256
digests; none is a secret. The files name the machine as "Home (OS hostname Main)" and use the account path under
`/mnt/storage`; both are already documented in Atlas (`environment/home-subsystems-migrated-into-fdev`).

Because nothing was redacted, byte-identity to `install-manifest.json` is shown directly on the installed copy.
The redaction mechanism exists for later imports: a redacted file records, per line, the line number, the sha256 of
the original line and the replacement, so the private check can reconstruct the repository file from the installed
bytes without the secret appearing in the record.

## What follows

Later changes, each a separate PR reviewed against these bytes: ack writer r2, operator r6, seal and claim tooling,
the pod image and role entrypoint, finalizer r5, and the pod operator with its recovery driver.
