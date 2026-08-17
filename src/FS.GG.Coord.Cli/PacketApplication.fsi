namespace FS.GG.Coord.Cli

/// `packet validate <packet.json>` (.github#2737) — the finder's own pre-flight over one
/// `fsgg.coord.finding-packet/v1` document.
///
/// This module owns exactly the impure half: reading the named file and printing. Decoding and
/// validation are `FS.GG.Coord.FindingPacket`'s, which is pure and total, so every requirement about
/// unknown fields, sentinel shape and never-throwing is tested without a process boundary.
///
/// **It cannot refuse a post, and that is the design** (`work/2737-finding-packet-schema/`
/// clarifications, DEC-001). `board-analyst/SKILL.md` says a synchronous filing choke-point would
/// wedge chains, and a wedged chain is a worse failure than a duplicate row. This verb sits BEFORE the
/// finder posts, on the finder's own draft; a packet that fails it is still postable as prose.
module PacketApplication =
    /// Run the verb. Green with an `fsgg.coord.packet-result/v1` document on stdout, or red with one
    /// finding per offending field on stderr.
    val run: Options.Options -> int
