# fdev fresh telemetry producer enrollment: preparation recipe

Prepared for [#4638](https://github.com/FS-GG/.github/issues/4638) under Root's 2026-10-30 ruling: the legacy
Main 8443 listener is obsolete, and fdev's producer gets a fresh enrollment against the live `fsgg-store`
receiver (Host 0.8.0, store schema 14) on `:8447`. This page is preparation only. It performs, authorizes and
records no enrollment, key operation, restart or send. Root admits each effect separately. Observations were made
read-only on 2026-10-30 and must be re-read before use.

## Sanctioned commands

There is no `provision` command. Provisioning is `init`, and enrollment is `enroll-producer`; both are offline
and explicit (the Host README states that `serve` never provisions or enrolls).

| Step | Command surface | Source |
| --- | --- | --- |
| Create a store and its receipt workspace | `fsgg-telemetry-host init --root ROOT --workspace W` | `src/FS.GG.Telemetry.Host/Program.fs:1301` |
| Enroll one producer principal | `fsgg-telemetry-host enroll-producer --config host.json --reference R --secret-file F --workspace W --producer P --stream S [--revoked]` | `Program.fs:1307-1370` |
| Validate before serving | `fsgg-telemetry-host preflight --config host.json` (takes the service lock) | `Program.fs:1450` |
| Read-only state | `fsgg-telemetry-host status --config host.json` (no lock) | `Program.fs:1454` |

Custody facts that follow from the source:

- `enroll-producer` only enrolls an entry that `host.json` already declares, with the same reference, secret file,
  workspace, producer, stream and revoked flag; otherwise it refuses with "enrollment is not declared by config"
  (`Program.fs:1337-1370`). The receiver operator therefore edits `host.json` first. The credential entry and store
  rules are validated at `src/FS.GG.Telemetry.Host/HostRuntime.fs:222-290`: the workspace must have a store entry,
  a producer cannot span workspaces, references are unique, and the secret file must be a private regular file.
- `init` and `enroll-producer` take the exclusive service lock (`Program.fs:332-337`), and `serve` holds it
  (`Program.fs:1780-1781`). Enrollment therefore needs the receiver stopped. `serve` reads credentials once at start
  (`Program.fs:1784`), so a restart is needed before a new credential authenticates.
- The receiver holds the secret file; the producer holds the same token in its own private directory. The
  producer wrapper requires 32 to 8192 printable characters, mode 0400 and caller ownership
  (`/opt/fdev/bin/fdev-telemetry`, `read_private` and `secret_value`).
- The producer resolves its credential only from the environment variable
  `FSGG_TELEMETRY_CREDENTIAL_<REFERENCE>`; configuration never carries it
  (`src/FS.GG.Coord.Cli/WorkspaceTelemetryApplication.fs:606-613`). The wrapper `exec` sets only `_MAIN`, so the
  association's credential reference is `main`.
- A destination change must use a producer identity that has never appeared in the client's association history
  (`docs/reference/local-telemetry-store.md:130-131`). Use a new producer id, not `fsharp-dev-main`.
- [ADR-0094](../adr/0094-separate-v3-and-utel-principals-and-custody.md) names no Host producer role. Its binding
  principles apply by Root's ruling: a distinct principal and key, no reuse of an existing key, explicit enrollment,
  and Programme accountable for custody.

## Observed state (2026-10-30, read-only)

| Fact | Observation |
| --- | --- |
| Receiver | Host 0.8.0 `serve --config /srv/telemetry/config/home-20261003/host.json`, rootless container under account `fsgg-store-dev` (uid 30033), listening on `0.0.0.0:8447`. |
| Name and route | `fsgg-store.lan` resolves to `127.0.0.1` (`/etc/hosts`, written by `fdev-config`). The legacy `telemetry.main.internal:8443` does not answer. |
| TLS | TLSv1.3. Leaf `CN=fsgg-store.lan`, issuer `CN=FS-GG Home Telemetry 20261003 CA`, valid to 2027-10-01. The server sends the leaf only, so the client needs the CA. |
| Authentication | An unauthenticated `GET /private/health` returns 401 over a verified TLS session; no bearer was sent. |
| Producer client | `~/.config/fs-gg/telemetry-main` holds the legacy client (endpoint `:8443`, old self-signed `FS-GG-Telemetry-CA`, a secret for the dead listener). `~/.config/fs-gg/telemetry.json` does not exist, so `workspace status` returns `unconfigured`. |
| Drain timer | `fdev-telemetry-drain.timer` runs every five minutes and prints "waiting for released durable-receipt CLI". |

## Blockers

1. **The drain message is misleading.** Coordination CLI 0.113.0 is installed and already contains the
   durable-receipt census (`WorkspaceTelemetryApplication.fs:1540`, PR 3567). The drain script waits because
   `workspace status` returns `{"status":"unconfigured"}` without `pendingDurablyReceived`
   (`WorkspaceTelemetryApplication.fs:1475`). No CLI release is missing; the missing piece is an activated
   association, which needs the fresh credential first.
2. **Receiver custody.** `/home/fsgg-store-dev` is `0700 fsgg-store-dev`, and the Host `host.json`, secret files
   and store live under that account's container. `developer` has passwordless sudo in fdev, but that is not
   admission to cross receiver custody. The edit, stop, enroll, start sequence belongs to the receiver custodian
   (Home is the recorded application custodian) or to an explicit Root grant.
3. **Enrollment needs a receiver stop and restart.** See the lock rules above. During the window the receiver is
   unavailable to every producer. Root should confirm no producer is mid-submission.
4. **The client does not trust the live CA.** The engine uses the system trust store
   (`src/FS.GG.Telemetry.Client` sets no CA of its own), and `/etc/ca-certificates/trust-source/anchors` is empty.
   The CA copy in `telemetry-main` is the retired Main CA. The public live CA exists in the earlier canonical
   client directory (SHA-256 `CB:20:40:91:E8:5C:82:09:FA:FE:89:EB:4E:AD:1E:AE:29:F3:4E:E5:D4:45:BA:3D:62:9A:21:29:D9:3F:79:C8`).
   Trust needs a privileged `update-ca-trust` or `SSL_CERT_FILE`.
5. **Wrapper and drain script live outside this repository.** `/opt/fdev/bin/fdev-telemetry` and
   `fdev-telemetry-drain.sh` come from Home's SystemAdmin image source and are regenerated on upgrade. Their
   `install` subcommand also rewrites `/etc/hosts` and requires an RFC1918 `hostAddress`; this recipe avoids
   `install`. No `.github` source change is needed. If Root wants the drain script to take a directory argument or
   print the real reason, that is a SystemAdmin request.
6. **Scope decision (Root).** Option A reuses the live workspace `utel-home-20261003` with a new producer. It needs
   no new store but adds identities to a workspace whose accepted counts (49 current identities, 48 canonical
   joins) were pinned by earlier acceptances. Option B adds a new workspace and store (`init`), which keeps those
   counts untouched but needs a `Stores` entry and browser-principal access for later assessment. Recommendation: B.

## Admission recipe

Placeholders: `W` workspace, `P` producer (new, for example `fdev-vm-main`), `S` stream (`coordination`),
`R` receiver-side reference (a label only, for example `fdev-vm-main-v1`), `D` the new producer directory.

Receiver side, as the receiver custodian, all with the secret never printed or placed in arguments:

1. Record the pre-state: hash `host.json`, run `status --config` (no lock). Keep an unmodified copy of `host.json`
   as the rollback.
2. Generate a CSPRNG token (at least 32 printable characters) straight into a new `0600` secret file under the
   receiver's credentials directory, owned by the container user. Do not reuse any existing secret file.
3. Add the `Credentials` entry for `R`, `W`, `P`, `S` (role `generic`, `Revoked` false, plus grant fields if the
   schema is `host-config/2`), and for Option B a `Stores` entry. Leave every other entry byte-identical.
4. Stop `fsgg-store.service`.
5. Option B only: `init --root <store root> --workspace W`.
6. `enroll-producer --config host.json --reference R --secret-file <file> --workspace W --producer P --stream S`.
7. `preflight --config host.json` must accept. Start `fsgg-store.service`. Read `status --config` and confirm the
   other stores and receipts are unchanged.

Producer side, as `developer`:

8. Retire, do not delete, the legacy client: rename `~/.config/fs-gg/telemetry-main` to
   `telemetry-main.retired-20261030`. Create a new `telemetry-main` (`0700`). The drain script and wrapper default
   to that name, so no script change is needed.
9. Install the token as `producer-main.secret` (`0400`, delivered by a root-owned pipe, never echoed), the public
   live CA as `telemetry-ca.crt`, and `client.json` with schema `fsgg.fdev.telemetry-client/1`, endpoint
   `https://fsgg-store.lan:8447`, `hostAddress` `10.0.2.15` (validated only; not used by `exec`), route
   `private-lan`, and `W`, `P`, `S`.
10. Trust the public CA: `sudo install -m 0644` into `/etc/ca-certificates/trust-source/anchors/` and
    `sudo update-ca-trust` (root-admitted), or a per-process `SSL_CERT_FILE` bundle for a single check.
11. `fdev-telemetry health` is the first authenticated request; it must return the Host ready body.
12. Activate with a new, empty spool (never the shared legacy spool root, which holds the old queue and outcomes):

    ```console
    fdev-telemetry exec fsgg-coord-engine telemetry workspace activate-remote \
      --workspace W --producer P --stream S --repository FS-GG/.github \
      --endpoint https://fsgg-store.lan:8447/ --credential-reference main \
      --spool-root "$HOME/.local/share/fs-gg-private/telemetry-spool-fdev-vm-20261030"
    ```

    `activate-remote` is local: it checks only that the credential variable exists and creates the spool and
    `~/.config/fs-gg/telemetry.json` (`WorkspaceTelemetryApplication.fs:1404-1420`).
13. `fsgg-coord-engine telemetry workspace status --repository FS-GG/.github` must now include
    `pendingDurablyReceived` and `pendingUnacknowledged`; the next timer tick then runs `workspace drain` on the
    empty spool.

Rollback: set the entry `Revoked` and run `enroll-producer ... --revoked` after the same stop (`Program.fs:1333`),
or restore the saved `host.json` and restart; restore the retired client directory name; remove the trust anchor.
The legacy queue and its recorded gap are never touched.
