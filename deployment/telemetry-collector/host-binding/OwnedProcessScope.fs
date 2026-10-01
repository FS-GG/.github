namespace Fs.Gg.Telemetry.HostBinding

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open System.Text
open System.Threading
open System.Threading.Tasks

/// Linux process ownership for the dedicated HostBinding CLI.  The scope may
/// own every child because admission proves that the process initially has no
/// children and the assembly has no other child producer.
module internal OwnedProcessScope =
    [<Literal>]
    let private PrSetChildSubreaper = 36
    [<Literal>]
    let private PAll = 0
    [<Literal>]
    let private PPidFd = 3
    [<Literal>]
    let private WNoHang = 1
    [<Literal>]
    let private WExited = 4
    [<Literal>]
    let private WNoWait = 0x01000000
    [<Literal>]
    let private Wall = 0x40000000
    [<Literal>]
    let private EChild = 10
    [<Literal>]
    let private ESrch = 3
    [<Literal>]
    let private SigTerm = 15
    [<Literal>]
    let private SigKill = 9
    [<Literal>]
    let private SigChild = 17
    [<Literal>]
    let private SaNoChildWait = 2

    [<DllImport("libc", SetLastError = true)>]
    extern int private prctl(int option, uint64 argument2, uint64 argument3, uint64 argument4, uint64 argument5)

    [<DllImport("libc", SetLastError = true)>]
    extern int private pidfd_open(int pid, uint32 flags)

    [<DllImport("libc", SetLastError = true)>]
    extern int private pidfd_send_signal(int pidfd, int signal, nativeint info, uint32 flags)

    [<DllImport("libc", SetLastError = true)>]
    extern int private waitid(int idtype, uint32 id, nativeint info, int options)

    [<DllImport("libc", SetLastError = true)>]
    extern int private close(int fd)

    [<DllImport("libc", SetLastError = true)>]
    extern int private sigaction(int signal, nativeint action, nativeint previous)

    type private Identity =
        { Pid: int
          Start: uint64
          PidFd: int
          mutable Reaped: bool }

    let mutable private lease = 0
    let mutable private currentObject: obj option = None

    type internal Scope (deadline: int64, ignoreBaseline: bool) =
        let identities = Dictionary<int, Identity>()
        let ignored = HashSet<int>()
        let mutable unknown = false
        let mutable cancellation = 0
        let mutable activeDirectPid = 0
        let mutable sawDescendant = false
        let supervisorPid = Environment.ProcessId

        let remainingMilliseconds () = deadline - Environment.TickCount64
        let poison () = unknown <- true
        let isCancelled () = Volatile.Read(&cancellation) <> 0
        let cancel () = Interlocked.Exchange(&cancellation, 1) |> ignore

        let readBounded maximum path =
            let info = FileInfo(path)
            if not info.Exists || info.Length < 0L || info.Length > int64 maximum then raise (IOException())
            let value = File.ReadAllText(path, Encoding.ASCII)
            if Encoding.ASCII.GetByteCount(value) > maximum then raise (IOException())
            value

        let readIdentity pid =
            try
                let raw = readBounded 16384 $"/proc/{pid}/stat"
                let closeParen = raw.LastIndexOf(')')
                if closeParen < 2 || closeParen + 2 >= raw.Length then None else
                let fields = raw.Substring(closeParen + 2).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                if fields.Length <= 19 then None else
                let mutable ppid = 0
                let mutable start = 0UL
                if Int32.TryParse(fields[1], &ppid) && UInt64.TryParse(fields[19], &start) then Some(ppid, start) else None
            with _ -> None

        let childPids () =
            try
                let task = DirectoryInfo("/proc/self/task")
                let threads = task.EnumerateDirectories() |> Seq.truncate 257 |> Seq.toArray
                if threads.Length > 256 then poison (); [||] else
                let values = ResizeArray<int>()
                for thread in threads do
                    let raw = readBounded 65536 (Path.Combine(thread.FullName, "children"))
                    for token in raw.Split(' ', StringSplitOptions.RemoveEmptyEntries) do
                        let mutable pid = 0
                        if not (Int32.TryParse(token, &pid)) || pid <= 0 then poison ()
                        elif not (values.Contains(pid)) then values.Add(pid)
                        if values.Count > 256 then poison ()
                if values.Count > 256 then [||] else values.ToArray()
            with _ -> poison (); [||]

        let acquire pid =
            match readIdentity pid with
            | Some(ppid, start) when ppid = supervisorPid ->
                let fd = pidfd_open(pid, 0u)
                if fd < 0 then poison ()
                else
                    match readIdentity pid with
                    | Some(ppid2, start2) when ppid2 = ppid && start2 = start ->
                        identities.Add(pid, { Pid = pid; Start = start; PidFd = fd; Reaped = false })
                        if activeDirectPid <> 0 && pid <> activeDirectPid then sawDescendant <- true
                    | _ -> close(fd) |> ignore; poison ()
            | _ -> poison ()

        let discover () =
            for pid in childPids () do
                if not (ignored.Contains(pid)) && not (identities.ContainsKey(pid)) then acquire pid

        do
            if ignoreBaseline then
                for pid in childPids () do ignored.Add(pid) |> ignore

        let signal signal =
            discover ()
            for identity in identities.Values do
                if not identity.Reaped then
                    let rc = pidfd_send_signal(identity.PidFd, signal, 0n, 0u)
                    if rc <> 0 && Marshal.GetLastPInvokeError() <> ESrch then poison ()

        let reapAdopted directPid =
            let buffer = Marshal.AllocHGlobal(128)
            try
                for identity in identities.Values do
                    if identity.Pid <> directPid && not identity.Reaped then
                        for offset in 0 .. 127 do Marshal.WriteByte(buffer, offset, 0uy)
                        let rc = waitid(PPidFd, uint32 identity.PidFd, buffer, WExited ||| WNoHang)
                        if rc = 0 then
                            // Linux siginfo_t.si_pid is the fourth 32-bit field on the selected x86_64 ABI.
                            if Marshal.ReadInt32(buffer, 16) <> 0 then identity.Reaped <- true
                        elif Marshal.GetLastPInvokeError() = EChild then
                            // Another reaper or unsupported runtime interaction is not success.
                            poison ()
                        else poison ()
            finally Marshal.FreeHGlobal(buffer)

        let kernelHasNoChildren () =
            let buffer = Marshal.AllocHGlobal(128)
            try
                for offset in 0 .. 127 do Marshal.WriteByte(buffer, offset, 0uy)
                let rc = waitid(PAll, 0u, buffer, WExited ||| WNoHang ||| WNoWait ||| Wall)
                rc = -1 && Marshal.GetLastPInvokeError() = EChild
            finally Marshal.FreeHGlobal(buffer)

        let ownedSettled directPid =
            identities.Values
            |> Seq.forall (fun identity -> identity.Pid = directPid || identity.Reaped)

        let settledObservation directPid =
            if ignoreBaseline then ownedSettled directPid else kernelHasNoChildren ()

        let settle (direct: Process) reason =
            let cleanupEnd = min deadline (Environment.TickCount64 + 3250L)
            if reason then cancel ()
            discover ()
            signal SigTerm
            let termEnd = min cleanupEnd (Environment.TickCount64 + 250L)
            while Environment.TickCount64 < termEnd && not (settledObservation direct.Id) do
                discover (); reapAdopted direct.Id; Thread.Sleep(10)
            signal SigKill
            while Environment.TickCount64 < cleanupEnd && not (settledObservation direct.Id) do
                discover (); reapAdopted direct.Id
                try if not direct.HasExited then direct.WaitForExit(10) |> ignore with _ -> poison ()
                Thread.Sleep(10)
            try if not direct.HasExited then direct.WaitForExit(1) |> ignore with _ -> poison ()
            discover (); reapAdopted direct.Id
            let clean = direct.HasExited && settledObservation direct.Id && not unknown
            if not clean then poison ()
            clean

        member _.RequestCancellation() = cancel ()
        member _.RemainingMilliseconds = remainingMilliseconds ()
        member _.IsUnknown = unknown

        member this.Run(executable: string, root: string, arguments: string list, timeoutMilliseconds: int, stdoutMaximum: int, stderrMaximum: int) =
            if isCancelled () || unknown then raise (BindingRefusal "process-scope-refused")
            if timeoutMilliseconds <= 0 || remainingMilliseconds () < int64 timeoutMilliseconds + 3250L then
                raise (BindingRefusal "process-budget-refused")
            if executable <> "/usr/bin/git" && not (executable.StartsWith("/usr/bin/", StringComparison.Ordinal)) then
                raise (BindingRefusal "process-executable-refused")
            let start = ProcessStartInfo("/usr/bin/setsid")
            start.WorkingDirectory <- root
            start.UseShellExecute <- false
            start.RedirectStandardOutput <- true
            start.RedirectStandardError <- true
            start.Environment.Clear()
            start.Environment["PATH"] <- "/usr/bin:/bin"
            start.Environment["LANG"] <- "C.UTF-8"
            start.Environment["LC_ALL"] <- "C.UTF-8"
            start.ArgumentList.Add(executable)
            for argument in arguments do start.ArgumentList.Add(argument)
            use direct = Process.Start(start)
            activeDirectPid <- direct.Id
            sawDescendant <- false
            let capture maximum (stream: Stream) =
                Task.Run(fun () ->
                    use output = new MemoryStream()
                    let buffer = Array.zeroCreate<byte> 1024
                    let mutable doneReading = false
                    while not doneReading do
                        let count = stream.Read(buffer, 0, buffer.Length)
                        if count = 0 then doneReading <- true
                        elif output.Length + int64 count > int64 maximum then
                            cancel ()
                            raise (BindingRefusal "git-output-limit-refused")
                        else output.Write(buffer, 0, count)
                    UTF8Encoding(false, true).GetString(output.ToArray()))
            let stdoutTask = capture stdoutMaximum direct.StandardOutput.BaseStream
            let stderrTask = capture stderrMaximum direct.StandardError.BaseStream
            let operationEnd = min (Environment.TickCount64 + int64 timeoutMilliseconds) (deadline - 3250L)
            let mutable exited = false
            while not exited && not (isCancelled ()) && Environment.TickCount64 < operationEnd do
                exited <- direct.WaitForExit(10)
                discover ()
            let readersEnd = min (Environment.TickCount64 + 100L) operationEnd
            while not (isCancelled ()) && (not stdoutTask.IsCompleted || not stderrTask.IsCompleted) && Environment.TickCount64 < readersEnd do
                Thread.Sleep(5)
            let readersOk = stdoutTask.IsCompletedSuccessfully && stderrTask.IsCompletedSuccessfully
            discover ()
            let clean = settle direct (not exited || not readersOk || isCancelled () || sawDescendant)
            if not clean then raise (BindingRefusal "process-cleanup-unknown")
            for identity in identities.Values do close(identity.PidFd) |> ignore
            identities.Clear()
            if not exited then raise (BindingRefusal "git-timeout-refused")
            if not readersOk || isCancelled () || sawDescendant then raise (BindingRefusal "git-output-refused")
            activeDirectPid <- 0
            direct.ExitCode, stdoutTask.Result, stderrTask.Result

        interface IDisposable with
            member _.Dispose() =
                for identity in identities.Values do close(identity.PidFd) |> ignore
                identities.Clear()
                currentObject <- None
                Interlocked.Exchange(&lease, 0) |> ignore

    let private kernelEmpty () =
        let buffer = Marshal.AllocHGlobal(128)
        try
            let rc = waitid(PAll, 0u, buffer, WExited ||| WNoHang ||| WNoWait ||| Wall)
            rc = -1 && Marshal.GetLastPInvokeError() = EChild
        finally Marshal.FreeHGlobal(buffer)

    let private compatibleSigChild () =
        let buffer = Marshal.AllocHGlobal(152)
        try
            for offset in 0 .. 151 do Marshal.WriteByte(buffer, offset, 0uy)
            if sigaction(SigChild, 0n, buffer) <> 0 then false
            else
                let handler = Marshal.ReadInt64(buffer, 0)
                let flags = Marshal.ReadInt32(buffer, 136)
                handler <> 1L && (flags &&& SaNoChildWait) = 0
        finally Marshal.FreeHGlobal(buffer)

    let private enter requireEntry ignoreBaseline =
        if not (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) || RuntimeInformation.ProcessArchitecture <> Architecture.X64 || Environment.ProcessId = 1 then
            raise (BindingRefusal "process-platform-refused")
        if requireEntry && Assembly.GetEntryAssembly() <> Assembly.GetExecutingAssembly() then raise (BindingRefusal "process-entry-refused")
        if not (File.Exists("/usr/bin/setsid") && File.Exists("/usr/bin/git") && Directory.Exists("/proc/self/task")) then raise (BindingRefusal "process-dependency-refused")
        if not (compatibleSigChild ()) then raise (BindingRefusal "process-sigchild-refused")
        if Interlocked.CompareExchange(&lease, 1, 0) <> 0 then raise (BindingRefusal "process-scope-busy")
        try
            if not ignoreBaseline && not (kernelEmpty ()) then raise (BindingRefusal "process-baseline-refused")
            if prctl(PrSetChildSubreaper, 1UL, 0UL, 0UL, 0UL) <> 0 then raise (BindingRefusal "process-subreaper-refused")
            if not ignoreBaseline && not (kernelEmpty ()) then raise (BindingRefusal "process-baseline-refused")
            let scope = new Scope(Environment.TickCount64 + 80000L, ignoreBaseline)
            currentObject <- Some(scope :> obj)
            scope
        with _ -> Interlocked.Exchange(&lease, 0) |> ignore; reraise ()

    let enterCli () = enter true false
    let enterTest () = enter false true
    let enterStrictTest () = enter false false

    let active () =
        match currentObject with
        | Some value -> value :?> Scope
        | None -> raise (BindingRefusal "process-scope-refused")
