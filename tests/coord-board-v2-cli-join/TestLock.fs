/// Private writer-lock directories for every board-v2 test that reaches a writer (FS-GG/.github#5126).
/// The engine's run seams take the lock directory as an argument, so a test cannot reach the production lock
/// `$XDG_RUNTIME_DIR/fsgg/board-writer-*.lock` or the legacy `/tmp` locks without naming them.
module TestLock

open System
open System.IO

/// A fresh private directory under the test process's temporary directory, so parallel tests never contend.
let fresh () =
    let directory = Path.Combine(Path.GetTempPath(), "fsgg-test-writer-lock-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    directory
