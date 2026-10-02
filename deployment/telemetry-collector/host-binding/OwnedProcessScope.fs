namespace Fs.Gg.Telemetry.HostBinding

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open System.Runtime.ExceptionServices
open System.Text
open System.Threading
open System.Threading.Tasks

/// Linux process ownership for the dedicated HostBinding CLI.  The scope may
/// own every child because admission proves that the process initially has no
/// children and the assembly has no other child producer.
module internal OwnedProcessScope =
    type internal DiagnosticObservation = NotObserved | ObservedFalse | ObservedTrue
    type internal DiagnosticReaderState = ReaderNotObserved | ReaderComplete | ReaderFaulted | ReaderIncomplete
    type internal DiagnosticRetirement = RetirementNotObserved | RetirementClean | RetirementUnknown
    type internal DiagnosticFailureSite =
        | NoFailure | ChildTaskLimit | ChildToken | ChildEnumeration | IdentityRead | IdentityParent
        | PidFdOpen | IdentityRecheck | RetainedCapacity | Signal | ReapNoChild | ReapError
        | DirectWait | FinalDirectExit | FinalSettlement | PriorUnknown | StdoutReader | StderrReader
    type internal DiagnosticExceptionClass = NoException | Io | Format | Unauthorized | InvalidOperation | Other
    type internal DiagnosticReadOrigin = ReadNotObserved | ExistenceGuard | MetadataLengthGuard | ContentRead | PostReadByteGuard | StatParse
    type internal DiagnosticManagedException = ManagedNotObserved | FileNotFound | DirectoryNotFound | ManagedUnauthorized | GenericIo | ManagedFormat | ManagedOther | ManagedNone
    type internal DiagnosticReadGuard = GuardNotObserved | ExistsFalse | LengthNegative | LengthOver | ByteLengthOver | GuardNone
    type internal DiagnosticAcquisitionPass = PassNotObserved | InitialPass | RecheckPass
    type internal DiagnosticCandidateRelation = RelationNotObserved | ActiveDirect | OtherCandidate
    type internal DiagnosticSnapshot =
        { RunOrdinal: int; ExecutionRole: string; FirstFailureSite: DiagnosticFailureSite
          Errno: int option; ExceptionClass: DiagnosticExceptionClass; DirectExit: DiagnosticObservation
          Settlement: DiagnosticObservation; UnknownBeforeFinal: DiagnosticObservation
          StdoutReader: DiagnosticReaderState; StderrReader: DiagnosticReaderState
          FirstRetirement: DiagnosticRetirement; FinalRetirement: DiagnosticRetirement
          Deadline: DiagnosticObservation; ReadOrigin: DiagnosticReadOrigin; ManagedException: DiagnosticManagedException
          ReadGuard: DiagnosticReadGuard; AcquisitionPass: DiagnosticAcquisitionPass; CandidateRelation: DiagnosticCandidateRelation
          FirstFailureRunOrdinal: int; ScopeFirstRetirementRunOrdinal: int; RunFirstRetirement: DiagnosticRetirement
          RunFinalRetirement: DiagnosticRetirement; RetirementRunOrdinal: int }
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
    let mutable private capabilityProbeOverride: (unit -> bool) option = None

    type internal Scope (deadline: int64, ignoreBaseline: bool) =
        let identities = Dictionary<int, Identity>()
        let ignored = HashSet<int>()
        let rejected = HashSet<int>()
        let mutable capacityExhausted = false
        let mutable unknown = false
        let mutable cancellation = 0
        let mutable activeDirectPid = 0
        let mutable sawDescendant = false
        let mutable runOrdinal = 0
        let mutable firstFailure = NoFailure
        let mutable firstErrno: int option = None
        let mutable firstException = NoException
        let mutable directExitObservation = NotObserved
        let mutable settlementObservationValue = NotObserved
        let mutable unknownBeforeFinal = NotObserved
        let mutable stdoutReaderState = ReaderNotObserved
        let mutable stderrReaderState = ReaderNotObserved
        let mutable firstRetirement = RetirementNotObserved
        let mutable finalRetirement = RetirementNotObserved
        let mutable deadlineObservation = NotObserved
        let mutable readOrigin = ReadNotObserved
        let mutable managedException = ManagedNotObserved
        let mutable readGuard = GuardNotObserved
        let mutable acquisitionPass = PassNotObserved
        let mutable candidateRelation = RelationNotObserved
        let mutable firstFailureRunOrdinal = 0
        let mutable scopeFirstRetirementRunOrdinal = 0
        let mutable runFirstRetirement = RetirementNotObserved
        let mutable runFinalRetirement = RetirementNotObserved
        let mutable retirementRunOrdinal = 0
        let supervisorPid = Environment.ProcessId

        let remainingMilliseconds () = deadline - Environment.TickCount64
        let exceptionClass (error: exn) =
            match error with
            | :? FormatException -> Format | :? UnauthorizedAccessException -> Unauthorized | :? IOException -> Io
            | :? InvalidOperationException -> InvalidOperation | _ -> Other
        let managedClass (error: exn) =
            match error with
            | :? FileNotFoundException -> FileNotFound | :? DirectoryNotFoundException -> DirectoryNotFound
            | :? UnauthorizedAccessException -> ManagedUnauthorized | :? IOException -> GenericIo
            | :? FormatException -> ManagedFormat | _ -> ManagedOther
        let note site errno category =
            if firstFailure = NoFailure then firstFailure <- site; firstErrno <- errno; firstException <- category; firstFailureRunOrdinal <- runOrdinal
        let poisonAt site errno category = note site errno category; unknown <- true
        let isCancelled () = Volatile.Read(&cancellation) <> 0
        let cancel () = Interlocked.Exchange(&cancellation, 1) |> ignore

        let noteIdentityRead origin guard pass relation category managed =
            note IdentityRead None category
            if readOrigin = ReadNotObserved then
                readOrigin <- origin; readGuard <- guard; acquisitionPass <- pass; candidateRelation <- relation; managedException <- managed

        let readBounded maximum path context =
            let observe origin guard category managed = context |> Option.iter(fun (pass, relation) -> noteIdentityRead origin guard pass relation category managed)
            let info = FileInfo(path)
            if not info.Exists then observe ExistenceGuard ExistsFalse Io ManagedNone; raise (IOException())
            if info.Length < 0L then observe MetadataLengthGuard LengthNegative Io ManagedNone; raise (IOException())
            if info.Length > int64 maximum then observe MetadataLengthGuard LengthOver Io ManagedNone; raise (IOException())
            let value =
                try File.ReadAllText(path, Encoding.ASCII)
                with error -> observe ContentRead GuardNone (exceptionClass error) (managedClass error); reraise()
            if Encoding.ASCII.GetByteCount(value) > maximum then observe PostReadByteGuard ByteLengthOver Io ManagedNone; raise (IOException())
            value

        let readIdentity pass pid =
            let relation = if activeDirectPid <> 0 && pid = activeDirectPid then ActiveDirect else OtherCandidate
            try
                let raw = readBounded 16384 $"/proc/{pid}/stat" (Some(pass,relation))
                let closeParen = raw.LastIndexOf(')')
                if closeParen < 2 || closeParen + 2 >= raw.Length then noteIdentityRead StatParse GuardNone pass relation Format ManagedFormat; None else
                let fields = raw.Substring(closeParen + 2).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                if fields.Length <= 19 then noteIdentityRead StatParse GuardNone pass relation Format ManagedFormat; None else
                let mutable ppid = 0
                let mutable start = 0UL
                if Int32.TryParse(fields[1], &ppid) && UInt64.TryParse(fields[19], &start) then Some(ppid, start)
                else noteIdentityRead StatParse GuardNone pass relation Format ManagedFormat; None
            with error -> note IdentityRead None (exceptionClass error); None

        let childPids () =
            try
                let task = DirectoryInfo("/proc/self/task")
                let threads = task.EnumerateDirectories() |> Seq.truncate 257 |> Seq.toArray
                if threads.Length > 256 then poisonAt ChildTaskLimit None NoException; [||] else
                let values = ResizeArray<int>()
                for thread in threads do
                    let raw = readBounded 65536 (Path.Combine(thread.FullName, "children")) None
                    for token in raw.Split(' ', StringSplitOptions.RemoveEmptyEntries) do
                        let mutable pid = 0
                        if not (Int32.TryParse(token, &pid)) || pid <= 0 then poisonAt ChildToken None Format
                        elif not (values.Contains(pid)) then values.Add(pid)
                        if values.Count > 256 then poisonAt RetainedCapacity None NoException
                if values.Count > 256 then [||] else values.ToArray()
            with error -> poisonAt ChildEnumeration None (exceptionClass error); [||]

        let acquire pid =
            let reject () =
                poisonAt IdentityRead None NoException
                if identities.Count + rejected.Count < 256 then rejected.Add(pid) |> ignore
                else capacityExhausted <- true
            if capacityExhausted || identities.Count + rejected.Count >= 256 then
                capacityExhausted <- true
                poisonAt RetainedCapacity None NoException
            else
                match readIdentity InitialPass pid with
                | Some(ppid, start) when ppid = supervisorPid ->
                    let fd = pidfd_open(pid, 0u)
                    if fd < 0 then let errno = Marshal.GetLastPInvokeError() in note PidFdOpen (Some errno) NoException; reject ()
                    else
                        match readIdentity RecheckPass pid with
                        | Some(ppid2, start2) when ppid2 = ppid && start2 = start ->
                            identities.Add(pid, { Pid = pid; Start = start; PidFd = fd; Reaped = false })
                            if activeDirectPid <> 0 && pid <> activeDirectPid then sawDescendant <- true
                        | _ -> note IdentityRecheck None NoException; close(fd) |> ignore; reject ()
                | Some _ -> note IdentityParent None NoException; reject ()
                | None -> reject ()

        let discover () =
            for pid in childPids () do
                if not (ignored.Contains(pid)) && not (rejected.Contains(pid)) && not (identities.ContainsKey(pid)) then acquire pid

        do
            if ignoreBaseline then
                for pid in childPids () do ignored.Add(pid) |> ignore

        let signal signal =
            discover ()
            for identity in identities.Values do
                if not identity.Reaped then
                    let rc = pidfd_send_signal(identity.PidFd, signal, 0n, 0u)
                    if rc <> 0 then
                        let errno = Marshal.GetLastPInvokeError()
                        if errno <> ESrch then poisonAt Signal (Some errno) NoException

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
                        else
                            let errno = Marshal.GetLastPInvokeError()
                            if errno = EChild then
                            // Another reaper or unsupported runtime interaction is not success.
                                poisonAt ReapNoChild (Some errno) NoException
                            else poisonAt ReapError (Some errno) NoException
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
            while Environment.TickCount64 < termEnd && (not direct.HasExited || not (settledObservation direct.Id)) do
                discover (); reapAdopted direct.Id
                try if not direct.HasExited then direct.WaitForExit(10) |> ignore with error -> poisonAt DirectWait None (exceptionClass error)
                Thread.Sleep(10)
            signal SigKill
            while Environment.TickCount64 < cleanupEnd && (not direct.HasExited || not (settledObservation direct.Id)) do
                // KILL every newly adopted identity during the same forced-stage
                // budget. Later generations receive no new TERM grace.
                signal SigKill; reapAdopted direct.Id
                try if not direct.HasExited then direct.WaitForExit(10) |> ignore with error -> poisonAt DirectWait None (exceptionClass error)
                // Reaping the direct child can adopt its session-separated child
                // synchronously; discover and KILL that generation before the
                // loop is allowed to observe settlement.
                signal SigKill; reapAdopted direct.Id
                Thread.Sleep(10)
            try if not direct.HasExited then direct.WaitForExit(1) |> ignore with error -> poisonAt DirectWait None (exceptionClass error)
            if Environment.TickCount64 < cleanupEnd then signal SigKill
            discover (); reapAdopted direct.Id
            let directExited = direct.HasExited
            directExitObservation <- if directExited then ObservedTrue else ObservedFalse
            let settled =
                if directExited then
                    let value = settledObservation direct.Id
                    settlementObservationValue <- if value then ObservedTrue else ObservedFalse
                    value
                else false
            unknownBeforeFinal <- if unknown then ObservedTrue else ObservedFalse
            deadlineObservation <- if remainingMilliseconds () <= 0L then ObservedTrue else ObservedFalse
            let clean = directExited && settled && not unknown
            if not clean then
                if not directExited then poisonAt FinalDirectExit None NoException
                elif not settled then poisonAt FinalSettlement None NoException
                else poisonAt PriorUnknown None NoException
            clean

        member _.RequestCancellation() = cancel ()
        member _.RemainingMilliseconds = remainingMilliseconds ()
        member _.IsUnknown = unknown
        member _.DiagnosticSnapshot =
            { RunOrdinal=runOrdinal;ExecutionRole=(if ignoreBaseline then "test" else "cli");FirstFailureSite=firstFailure
              Errno=firstErrno;ExceptionClass=firstException;DirectExit=directExitObservation;Settlement=settlementObservationValue
              UnknownBeforeFinal=unknownBeforeFinal;StdoutReader=stdoutReaderState;StderrReader=stderrReaderState
              FirstRetirement=firstRetirement;FinalRetirement=finalRetirement;Deadline=deadlineObservation
              ReadOrigin=readOrigin;ManagedException=managedException;ReadGuard=readGuard;AcquisitionPass=acquisitionPass;CandidateRelation=candidateRelation
              FirstFailureRunOrdinal=firstFailureRunOrdinal;ScopeFirstRetirementRunOrdinal=scopeFirstRetirementRunOrdinal
              RunFirstRetirement=runFirstRetirement;RunFinalRetirement=runFinalRetirement;RetirementRunOrdinal=retirementRunOrdinal }
        member internal _.ObserveCandidateForTest(pid: int) =
            if not (rejected.Contains(pid)) && not (identities.ContainsKey(pid)) then acquire pid
        member internal _.ObserveIdentityContextForTest(pid: int, recheck: bool, direct: bool) =
            let prior=activeDirectPid
            if direct then activeDirectPid<-pid
            try readIdentity (if recheck then RecheckPass else InitialPass) pid |> ignore
            finally activeDirectPid<-prior
        member internal _.ClassifyManagedExceptionForTest(error: exn) = managedClass error
        member internal _.RejectedCandidateCount = rejected.Count
        member internal _.RetainedCapacityExhausted = capacityExhausted

        member private this.RunCore(executable: string, root: string, arguments: string list, timeoutMilliseconds: int, stdoutMaximum: int, stderrMaximum: int, postSpawnHook: (unit -> unit) option) =
            runOrdinal <- min 64 (runOrdinal + 1)
            runFirstRetirement <- RetirementNotObserved; runFinalRetirement <- RetirementNotObserved; retirementRunOrdinal <- runOrdinal
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
            let mutable settlementComplete = false
            let mutable stdoutTask: Task<string> option = None
            let mutable stderrTask: Task<string> option = None
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
            let finishReaders () =
                try direct.StandardOutput.BaseStream.Close() with _ -> ()
                try direct.StandardError.BaseStream.Close() with _ -> ()
                let tasks =
                    [ stdoutTask |> Option.map (fun value -> value :> Task)
                      stderrTask |> Option.map (fun value -> value :> Task) ]
                    |> List.choose id |> List.toArray
                let doneReading = if tasks.Length = 0 then true else try Task.WaitAll(tasks, 100) with _ -> false
                let state (value: Task<string> option) = match value with None -> ReaderNotObserved | Some task when task.IsCompletedSuccessfully -> ReaderComplete | Some task when task.IsFaulted -> ReaderFaulted | Some _ -> ReaderIncomplete
                stdoutReaderState <- state stdoutTask; stderrReaderState <- state stderrTask
                if stdoutReaderState = ReaderFaulted then note StdoutReader None Other
                elif stderrReaderState = ReaderFaulted then note StderrReader None Other
                doneReading
            let retire reason =
                let clean = settle direct reason
                let readersDone = finishReaders ()
                settlementComplete <- clean && readersDone
                let result = if settlementComplete then RetirementClean else RetirementUnknown
                if firstRetirement = RetirementNotObserved then firstRetirement <- result; scopeFirstRetirementRunOrdinal <- runOrdinal
                finalRetirement <- result
                if runFirstRetirement = RetirementNotObserved then runFirstRetirement <- result
                runFinalRetirement <- result
                if settlementComplete then
                    for identity in identities.Values do close(identity.PidFd) |> ignore
                    identities.Clear()
                settlementComplete
            try
                postSpawnHook |> Option.iter (fun hook -> hook ())
                stdoutTask <- Some(capture stdoutMaximum direct.StandardOutput.BaseStream)
                stderrTask <- Some(capture stderrMaximum direct.StandardError.BaseStream)
                let operationEnd = min (Environment.TickCount64 + int64 timeoutMilliseconds) (deadline - 3250L)
                let mutable exited = false
                while not exited && not (isCancelled ()) && Environment.TickCount64 < operationEnd do
                    exited <- direct.WaitForExit(10)
                    discover ()
                let stdout = stdoutTask.Value
                let stderr = stderrTask.Value
                let readersEnd = min (Environment.TickCount64 + 100L) operationEnd
                while not (isCancelled ()) && (not stdout.IsCompleted || not stderr.IsCompleted) && Environment.TickCount64 < readersEnd do
                    Thread.Sleep(5)
                let readersOk = stdout.IsCompletedSuccessfully && stderr.IsCompletedSuccessfully
                discover ()
                if not (retire (not exited || not readersOk || isCancelled () || sawDescendant)) then
                    raise (BindingRefusal "process-cleanup-unknown")
                if not exited then raise (BindingRefusal "git-timeout-refused")
                if not readersOk || isCancelled () || sawDescendant then raise (BindingRefusal "git-output-refused")
                activeDirectPid <- 0
                direct.ExitCode, stdout.Result, stderr.Result
            with error ->
                if settlementComplete then
                    ExceptionDispatchInfo.Capture(error).Throw()
                    Unchecked.defaultof<_>
                elif retire true then
                    ExceptionDispatchInfo.Capture(error).Throw()
                    Unchecked.defaultof<_>
                else raise (BindingRefusal "process-cleanup-unknown")

        member this.Run(executable: string, root: string, arguments: string list, timeoutMilliseconds: int, stdoutMaximum: int, stderrMaximum: int) =
            this.RunCore(executable, root, arguments, timeoutMilliseconds, stdoutMaximum, stderrMaximum, None)

        member internal this.RunWithPostSpawnHook(executable: string, root: string, arguments: string list, timeoutMilliseconds: int, stdoutMaximum: int, stderrMaximum: int, hook: unit -> unit) =
            this.RunCore(executable, root, arguments, timeoutMilliseconds, stdoutMaximum, stderrMaximum, Some hook)

        interface IDisposable with
            member _.Dispose() =
                for identity in identities.Values do close(identity.PidFd) |> ignore
                identities.Clear()
                rejected.Clear()
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

    let private probePidFdCapability () =
        match capabilityProbeOverride with
        | Some probe -> probe ()
        | None ->
            let mutable fd = -1
            let buffer = Marshal.AllocHGlobal(128)
            try
                try
                    for offset in 0 .. 127 do Marshal.WriteByte(buffer, offset, 0uy)
                    fd <- pidfd_open(Environment.ProcessId, 0u)
                    if fd < 0 then false
                    elif pidfd_send_signal(fd, 0, 0n, 0u) <> 0 then false
                    else
                        let rc = waitid(PPidFd, uint32 fd, buffer, WExited ||| WNoHang ||| WNoWait)
                        // Self is deliberately not waitable. ECHILD proves the selected
                        // P_PIDFD/options ABI was understood without consuming status.
                        (rc = -1 && Marshal.GetLastPInvokeError() = EChild)
                        || (rc = 0 && Marshal.ReadInt32(buffer, 16) = 0)
                with
                | :? EntryPointNotFoundException
                | :? DllNotFoundException -> false
            finally
                if fd >= 0 then close(fd) |> ignore
                Marshal.FreeHGlobal(buffer)

    let private enter requireEntry ignoreBaseline =
        if not (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) || RuntimeInformation.ProcessArchitecture <> Architecture.X64 || Environment.ProcessId = 1 then
            raise (BindingRefusal "process-platform-refused")
        if requireEntry && Assembly.GetEntryAssembly() <> Assembly.GetExecutingAssembly() then raise (BindingRefusal "process-entry-refused")
        if not (File.Exists("/usr/bin/setsid") && File.Exists("/usr/bin/git") && Directory.Exists("/proc/self/task")) then raise (BindingRefusal "process-dependency-refused")
        if not (compatibleSigChild ()) then raise (BindingRefusal "process-sigchild-refused")
        if not (probePidFdCapability ()) then raise (BindingRefusal "process-pidfd-refused")
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
    let probePidFdCapabilityForTest () = probePidFdCapability ()
    let setCapabilityProbeForTest value = capabilityProbeOverride <- value

    let active () =
        match currentObject with
        | Some value -> value :?> Scope
        | None -> raise (BindingRefusal "process-scope-refused")
