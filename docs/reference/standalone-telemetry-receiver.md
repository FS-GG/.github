---
title: "Standalone telemetry HTTPS receiver"
category: Reference
categoryindex: 4
description: "H1 source contract for the optional authenticated telemetry receiver and client."
---

# Standalone telemetry HTTPS receiver

H1 adds source and synthetic qualification for a Linux x64, .NET 10 receiver. It does not publish,
install, enroll, or activate a service on Main. H2 owns immutable packaging and deployment rehearsal;
H3 owns real container routing and operational acceptance.

The producer configuration is an HTTPS origin plus a local credential reference. The reference is
resolved by the caller and is never sent over HTTP. The host configuration separately names an HTTPS
listener, private certificate/password files, an OS-lock path, explicit workspace/store roots and
private token files bound to workspace/producer/stream scopes. Configuration is closed and bounded;
duplicate fields, symlinks, group/world-readable secrets, relative paths, duplicate stores and a
logical producer bound to several workspaces are refused. Startup never initializes or migrates a
store. It acquires the service lock, checks every enrolled schema and recovers receipt obligations
before becoming ready. Legacy and unenrolled stores are never assigned automatically.

`POST /v1/batches` accepts only `fsgg.telemetry.envelope/1` and returns the R1
`fsgg.telemetry.receipt/1`. `GET /v1/receipts/{batchId}` is authorized by the same current scope.
`GET /private/health` is authenticated. Bearer secrets, envelope bodies and store paths are not logged.
An HTTP 202 without a complete receipt whose workspace, producer, stream, batch and digest match the
submitted envelope is unacknowledged and potentially lossy if no local spool remains.

The host admits at most 16 actor requests without a waiting queue and applies a ten-second HTTP result
deadline while retaining capacity until the actor actually finishes. One fixed admission/drain actor
runs on an Akka 1.5.71 fixed four-thread blocking-I/O dispatcher. Drain schedules its successor only
after completion. Before new identity admission the actor recovers and sums every configured store:
1,024 pending batches, 64 MiB pending canonical bytes and 1,000,000 lifetime identities globally;
R1 also enforces 128 batches/8 MiB per producer. No remoting, cluster, actor-per-request, producer path,
command, package or destination exists.

The client owns a redirect-disabled handler, accepts only HTTPS origins, bounds every response to 4 KiB
and retries at most five times with jittered exponential delay. It reuses identical envelope bytes and
identity after ambiguous failure. Terminal closed errors preserve their stable code. Caller cancellation
remains caller cancellation; timeout and transport ambiguity return an unacknowledged receipt result.

| HTTP status | Stable result |
| --- | --- |
| 200 / 202 | A complete matching receipt; the status alone is never acknowledgement |
| 400 | `invalid-request` or `unsupported-version` |
| 401 | Missing, malformed or revoked bearer credential |
| 403 | `unauthorized-scope` for an authenticated credential |
| 404 | `receipt-unavailable`, including an unknown or expired receipt |
| 408 | The ten-second request-body deadline expired before actor admission |
| 409 | `identity-conflict` |
| 413 | `oversized-batch` |
| 429 | `overload` |
| 503 | `storage-unavailable`, or an indeterminate result after an admitted request deadline |

A read-only lookup that returns `receipt-unavailable` does not prove that the host never accepted the
batch. Retention may have expired, so a producer must interpret only a matching receipt as acknowledgement.

Synthetic qualification uses a generated certificate and Kestrel over real TLS with the shared endpoint
mapping. It covers authenticated durable/application receipts, invalid encoding/version/size, denial,
duplicate submission and actor/server restart. R1 retains the separate eight process-exit persistence
boundaries. Power-loss behavior, immutable package installation, production filesystem qualification,
Main service restart and container-to-Main reachability remain H2/H3 evidence.
