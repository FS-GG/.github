namespace FS.GG.Coord.GitHub

module V2Custody =
    open System
    open System.IO

    // EEXIST. .NET reports a lost CreateNew race as an IOException carrying the errno as its HResult.
    let private alreadyExists = 17

    let createExclusive (path: string) : Result<FileStream, V2Enrollment.Refusal> =
        let options = FileStreamOptions(Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.WriteThrough)
        if not (OperatingSystem.IsWindows()) then options.UnixCreateMode <- UnixFileMode.UserRead ||| UnixFileMode.UserWrite
        try Ok(new FileStream(path, options))
        with
        | :? IOException as error when error.HResult = alreadyExists || File.Exists path || Directory.Exists path -> Error V2Enrollment.OutputExists
        | _ -> Error V2Enrollment.CustodyUnavailable
