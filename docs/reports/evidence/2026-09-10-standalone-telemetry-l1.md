# Standalone telemetry L1 source and package evidence

This evidence qualifies the L1 source and candidate package behavior on Linux x64. It does not claim that
`0.88.0` is published, that Main is installed, or that the local overlay filesystem is durable. L2 owns the
exact public release and complete standalone dashboard proof.

| L1 requirement | Evidence |
| --- | --- |
| Unconfigured operation creates nothing | `WorkspaceTelemetryApplicationTests.unconfigured status is read only`; installed package fixture check 11 |
| One explicit workspace/repository/destination | Closed loader, packaged `workspace binding`, duplicate-property/repository/producer tests, XDG selection test |
| Private path and concurrent mutation safety | Owner/mode/symlink checks, ancestor-symlink test, Linux `open(O_NOFOLLOW)` plus `flock` second-process exclusion, flushed atomic config/spool writes |
| Local placement refusal and recovery | Package fixture ran the production assessor on `overlayfs`, refused it, and observed no config/store residue; R1 receipt crash suite covers indexed inbox recovery used by cutover |
| Remote HTTPS capture and replay | Installed package fixture uses a private test CA and passes applied ACK, lost-response lookup, outage/restart drain, mismatch, auth and bounded-response cases |
| Interrupted producer recovery | Installed submit is held after the fake receiver records acceptance, then receives `SIGKILL`; a fresh installed CLI drain preserves the exact digest and settles through lookup |
| Prospective cutover | A→B→A producer reuse refusal, old pending refusal, retired history retention, and opaque scope/destination binding fences for runtime, roadmap and CI multi-batch work |
| Normal workflow integration | Runtime launcher, roadmap adapter and routine delivery CI use the bound workspace transport; focused .NET and Python tests preserve native exit/delivery authority on telemetry failure |
| Product/package isolation | Source-free generated F# workspace and local tool install need no Main, daemon, Host, Akka, Python or Node product dependency; uninstall preserves private outcomes |
| Bounded operation | 128-file/8 MiB spool admission, 16-file/30-second drain, 72 KiB envelope and 4 KiB response bounds, specific terminal diagnostics |

The final local run used .NET SDK `10.0.401`. The candidate package SHA-256 was
`92b07604ecf2349f183038fb9431ef9ff6fdde213b4943fcc457d3943cdd882a`: 31,089,190 compressed bytes and
126,524,785 installed bytes, increases of 131,113 and 618,559 bytes over the isolated public `0.87.0`
baseline. All 28 package checks passed. The remote receiver stores synthetic JSON and validates client
transport behavior; it is not a durable receiver/application qualification. CI reruns the same fixture and
records its actual filesystem/profile rather than treating this container's overlay refusal as durable proof.
