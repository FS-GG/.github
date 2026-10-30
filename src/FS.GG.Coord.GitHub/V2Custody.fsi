namespace FS.GG.Coord.GitHub

/// Exclusive creation of private custody files for the enrollment tool. No payload, path or exception text
/// is ever carried in a result.
module V2Custody =

    /// Create `path` exclusively (`CreateNew`, owner read/write only, write-through, no sharing).
    /// An entry that already exists when the create itself runs - including one that appeared after any
    /// earlier absence check - is `OutputExists`. Every other failure is `CustodyUnavailable`.
    /// The caller owns and must dispose the returned stream.
    val createExclusive: path: string -> Result<System.IO.FileStream, V2Enrollment.Refusal>
