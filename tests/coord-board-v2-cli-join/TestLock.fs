/// Private writer-lock directories for every board-v2 test that reaches a writer (FS-GG/.github#5126).
/// The engine's run seams take the lock directory as an argument, so a test cannot reach the production lock
/// `$XDG_RUNTIME_DIR/fsgg/board-writer-*.lock` or the legacy `/tmp` locks without naming them.
module TestLock

open System
open System.Collections.Concurrent
open System.IO

let private created = ConcurrentBag<string>()

// Every directory handed out is removed when the test process exits.
do AppDomain.CurrentDomain.ProcessExit.Add(fun _ ->
    for directory in created do
        try Directory.Delete(directory, true) with _ -> ())

/// A fresh private directory under the test process's temporary directory, so parallel tests never contend.
let fresh () =
    let directory = Path.Combine(Path.GetTempPath(), "fsgg-test-writer-lock-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    created.Add directory
    directory
