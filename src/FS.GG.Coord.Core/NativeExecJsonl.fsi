namespace FS.GG.Coord

open System

/// Structural replay of bounded native stdout; success confers no collector or custody authority.
module NativeExecJsonl =
    /// A capture-scoped identity, distinct from a provider-native turn identifier.
    type LocalTurnKey =
        { CaptureSha256: string
          ThreadId: Guid
          StartFrameOrdinal: int }
    /// Inclusive provider counters from the sole cumulative completion, never summed frames.
    type Usage =
        { Input: int64
          CachedInput: int64
          CacheWriteInput: int64 option
          Output: int64
          Reasoning: int64 option
          Total: int64 }
    /// The single structurally complete turn in a newly started thread.
    type Turn =
        { Key: LocalTurnKey
          TurnOrdinal: int
          CompletionFrameOrdinal: int
          NativeTurnId: Guid option
          Usage: Usage }
    /// Parsed bytes only; terminal, claim, population authority and isolation remain external gates.
    type Capture =
        { CaptureSha256: string
          CaptureBytes: int
          FrameCount: int
          ThreadId: Guid
          Turn: Turn }
    /// Replay exact UTF-8 JSONL within fixed byte/frame bounds, refusing ambiguous populations.
    val decode: stdout: byte array -> Result<Capture, string list>
