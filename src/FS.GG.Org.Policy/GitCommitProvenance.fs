namespace FS.GG.Org.Policy

open System
open System.Globalization
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions

/// Pure, provisional binding of a supplied Git root tree to one exact commit. The read-only
/// reader port has no installed implementation here; its repository identity and completeness
/// must be authenticated by the provider before this result can carry policy authority.
module GitCommitProvenance =
    type ExactCommitPin =
        { RepositoryNodeId: string
          RepositoryFullName: string
          CommitId: string }

    type CommitObservation =
        { RepositoryNodeId: string
          RepositoryFullName: string
          CommitId: string
          RawCommit: byte[] }

    type IReadOnlyCommitReader =
        abstract ReadExact: ExactCommitPin -> Result<CommitObservation, unit>

    type ProvisionalRoot = {
        RepositoryNodeId: string
        CommitId: string
        TreeId: string
    }

    let private error path message =
        Error { Code = "git-commit-provenance"; Path = path; Message = message }

    let private canonicalSha1 (value: string) =
        not (isNull value) && Regex.IsMatch(value, @"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant)

    let private commitId (bytes: byte[]) =
        let prefix = Encoding.ASCII.GetBytes("commit " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\000")
        let digest: byte[] = SHA1.HashData(Array.append prefix bytes)
        Convert.ToHexString(digest).ToLowerInvariant()

    let private rootedTree (bytes: byte[]) =
        let raw = Encoding.Latin1.GetString(bytes)
        let separator = raw.IndexOf("\n\n", StringComparison.Ordinal)
        if separator < 0 then
            error "<commit>" "commit header has no message separator"
        else
            let headers = raw.Substring(0, separator).Split('\n')
            let tree = Regex.Match(headers.[0], "^tree ([0-9a-f]{40})$", RegexOptions.CultureInvariant)
            if not tree.Success then
                error "<commit>" "commit tree header is malformed"
            elif headers |> Array.skip 1 |> Array.exists (fun line -> line.StartsWith("tree ", StringComparison.Ordinal)) then
                error "<commit>" "duplicate commit tree header"
            elif headers |> Array.filter (fun line -> line.StartsWith("author ", StringComparison.Ordinal)) |> Array.length <> 1
                 || headers |> Array.filter (fun line -> line.StartsWith("committer ", StringComparison.Ordinal)) |> Array.length <> 1 then
                error "<commit>" "commit author or committer header is absent or duplicated"
            else
                Ok tree.Groups.[1].Value

    /// Validate exact pin and reader claims, then verify raw SHA-1 commit bytes and their tree.
    /// A fake reader can still fabricate repository membership; provider authentication and an
    /// accepted pin are external prerequisites. No branch/ref alias is accepted here.
    let inspectProvisionalRoot
        (pin: ExactCommitPin)
        (expectedTreeId: string)
        (reader: IReadOnlyCommitReader)
        : Result<ProvisionalRoot, SyntaxDiagnostic> =
        if isNull (box pin)
           || String.IsNullOrWhiteSpace pin.RepositoryNodeId
           || isNull pin.RepositoryFullName
           || not (Regex.IsMatch(pin.RepositoryFullName, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)) then
            error "<pin>" "exact repository identity is absent or malformed"
        elif not (canonicalSha1 pin.CommitId) then
            error "<pin>" "exact commit ID must be 40 lowercase SHA-1 hexadecimal characters"
        elif not (canonicalSha1 expectedTreeId) then
            error "<root-tree>" "root tree ID must be 40 lowercase SHA-1 hexadecimal characters"
        elif isNull (box reader) then
            error "<commit>" "read-only commit observation is unavailable"
        else
            let observed =
                try reader.ReadExact pin
                with _ -> Error ()
            match observed with
            | Error () -> error "<commit>" "read-only commit observation is unavailable"
            | Ok value when isNull (box value) -> error "<commit>" "read-only commit observation is absent"
            | Ok value ->
                if not (String.Equals(value.RepositoryNodeId, pin.RepositoryNodeId, StringComparison.Ordinal))
                   || not (String.Equals(value.RepositoryFullName, pin.RepositoryFullName, StringComparison.Ordinal)) then
                    error "<commit>" "observed repository identity does not match exact pin"
                elif not (String.Equals(value.CommitId, pin.CommitId, StringComparison.Ordinal)) then
                    error "<commit>" "observed commit ID does not match exact pin"
                elif isNull value.RawCommit || Array.isEmpty value.RawCommit then
                    error "<commit>" "raw commit bytes are absent"
                else
                    let snapshot = Array.copy value.RawCommit
                    if commitId snapshot <> pin.CommitId then
                        error "<commit>" "raw commit hash does not match exact commit ID"
                    else
                        match rootedTree snapshot with
                        | Error diagnostic -> Error diagnostic
                        | Ok treeId when treeId <> expectedTreeId ->
                            error "<root-tree>" "commit tree ID does not match supplied root tree ID"
                        | Ok treeId ->
                            Ok { RepositoryNodeId = pin.RepositoryNodeId
                                 CommitId = pin.CommitId
                                 TreeId = treeId }
