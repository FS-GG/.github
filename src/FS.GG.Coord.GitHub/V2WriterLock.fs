namespace FS.GG.Coord.GitHub

open System
open System.IO

module V2WriterLock =
    [<Sealed>]
    type Location(paths: string list) =
        member _.Paths = paths

    module private Native =
        [<System.Runtime.InteropServices.DllImport("libc", EntryPoint = "open", SetLastError = true)>]
        extern int openFile(string path, int flags, int mode)
        [<System.Runtime.InteropServices.DllImport("libc", SetLastError = true)>]
        extern int flock(int descriptor, int operation)
        [<System.Runtime.InteropServices.DllImport("libc", SetLastError = true)>]
        extern int close(int descriptor)

    let private lockExclusive = 2
    let private lockNonBlocking = 4
    let private unlock = 8
    // Linux O_RDWR | O_CREAT | O_NOFOLLOW | O_CLOEXEC: a raw descriptor, so the explicit flock below is the only
    // lock (no .NET FileShare emulation hides a missing one) and a symlinked lock file is refused.
    let private openFlags = 0x2 ||| 0x40 ||| 0x20000 ||| 0x80000

    let fileName (projectId: string) = "board-writer-" + projectId + ".lock"
    let migrationLegacyName = "fsgg-v2-migration-PVT_kwDOEYAWY84Bldpa.lock"
    let observationLegacyName (projectId: string) = "fsgg-v2-observation-" + projectId + ".lock"

    let private legacyDirectory = "/tmp"

    let production (projectId: string) (legacyName: string) =
        let runtime = Environment.GetEnvironmentVariable "XDG_RUNTIME_DIR"
        if String.IsNullOrEmpty runtime || not (Path.IsPathRooted runtime) then
            Error "board writer lock needs an absolute XDG_RUNTIME_DIR; refusing to write without the shared lock"
        else
            Ok(Location [ Path.Combine(runtime, "fsgg", fileName projectId); Path.Combine(legacyDirectory, legacyName) ])

    let inDirectory (directory: string) (projectId: string) (legacyName: string) =
        Location [ Path.Combine(directory, fileName projectId); Path.Combine(directory, legacyName) ]

    type private Lease(descriptors: int list) =
        let mutable released = false
        interface IDisposable with
            member _.Dispose() =
                if not released then
                    released <- true
                    for descriptor in List.rev descriptors do
                        try Native.flock(descriptor, unlock) |> ignore
                        finally Native.close descriptor |> ignore

    let private openLocked (path: string) =
        if not (OperatingSystem.IsLinux()) then raise (PlatformNotSupportedException "board writer lock needs Linux flock(2)")
        let parent = Path.GetDirectoryName path
        if not (Directory.Exists parent) then
            Directory.CreateDirectory(parent, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
        let descriptor = Native.openFile(path, openFlags, 0o600)
        if descriptor < 0 then None
        elif Native.flock(descriptor, lockExclusive ||| lockNonBlocking) = 0 then Some descriptor
        else
            Native.close descriptor |> ignore
            None

    let tryAcquire (location: Location) : Result<IDisposable, string> =
        let rec take (taken: int list) = function
            | [] -> Ok(new Lease(List.rev taken) :> IDisposable)
            | path :: rest ->
                match (try openLocked path with _ -> None) with
                | Some descriptor -> take (descriptor :: taken) rest
                | None ->
                    (new Lease(List.rev taken) :> IDisposable).Dispose()
                    Error "lock-held"
        take [] location.Paths

    let acquire (location: Location) =
        match tryAcquire location with
        | Ok lease -> lease
        | Error reason -> raise (IOException("board writer lock unavailable: " + reason))
