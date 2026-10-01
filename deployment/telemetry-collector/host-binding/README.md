# HOST binding constructor

This nonpackable .NET 10 executable owns the typed public snapshot used by the
V2 HOST private qualification. It validates the clean recipe revision and tree,
the exact Git blobs and SHA-256 values named by the native source-pin file, and
the closed native operation profile. Its output binds those facts to the exact
compiled producer DLL.

Commands reconstruct the snapshot on every call:

- `inspect` emits the bounded public binding;
- `render` emits the public values used to render the private workflow;
- `derive` writes the existing NUL-separated admission digest for a private
  caller;
- `verify` reads one candidate admission from standard input, reconstructs the
  snapshot, and returns only a closed verification result.

The binding is not effect permission. `verify` compares both the reconstructed
binding and admission in constant time immediately before the Python operation
adapter materializes authentication. No command accepts credentials, reads the
private auth capsule, or retains authorization state.

Build and test with locked dependencies:

```console
dotnet restore deployment/telemetry-collector/host-binding/tests/HostBinding.Tests.fsproj --locked-mode
dotnet test deployment/telemetry-collector/host-binding/tests/HostBinding.Tests.fsproj --no-restore
```
