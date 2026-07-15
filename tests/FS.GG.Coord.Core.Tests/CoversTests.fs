module FS.GG.Coord.Core.Tests.CoversTests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types

// `covers` is the verify-paths containment rule, and it must match the bash client's exactly, because the
// shim will run one where the other ran. bash: strip trailing /** /* / from the token, then
// `file == token || file starts-with token/`.

[<Fact>]
let ``a file under a /** token is covered`` () =
    Assert.True(TouchSet.covers (Matchable "src/Audio/**") "src/Audio/Foo.fs")
    Assert.True(TouchSet.covers (Matchable "src/Audio/**") "src/Audio/sub/Bar.fs")

[<Fact>]
let ``the /* and bare-directory forms mean the same subtree`` () =
    Assert.True(TouchSet.covers (Matchable "src/Audio/*") "src/Audio/Foo.fs")
    Assert.True(TouchSet.covers (Matchable "src/Audio") "src/Audio/Foo.fs")

[<Fact>]
let ``an exact-file token covers only that file`` () =
    Assert.True(TouchSet.covers (Matchable "src/Foo.fs") "src/Foo.fs")
    Assert.False(TouchSet.covers (Matchable "src/Foo.fs") "src/Foo.fsx")
    Assert.False(TouchSet.covers (Matchable "src/Foo.fs") "src/Bar.fs")

[<Fact>]
let ``a sibling directory is NOT covered - the prefix must end at a slash`` () =
    // `src/Audio` must not cover `src/AudioEngine/x` — a prefix that is not a path boundary is the classic
    // containment bug.
    Assert.False(TouchSet.covers (Matchable "src/Audio") "src/AudioEngine/Foo.fs")

[<Fact>]
let ``#273 an unmatchable token covers NOTHING`` () =
    Assert.False(TouchSet.covers (Unmatchable "**/Foo.fs") "src/Foo.fs")
    Assert.False(TouchSet.covers (Unmatchable "src/*/Foo.fs") "src/x/Foo.fs")
