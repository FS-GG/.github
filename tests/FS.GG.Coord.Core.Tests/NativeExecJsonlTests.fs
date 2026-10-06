namespace FS.GG.Coord.Core.Tests

open System
open System.Security.Cryptography
open System.Text
open Xunit
open FS.GG.Coord

/// Authored structural fixtures, never observed provider/custody or collector authority evidence.
module NativeExecJsonlTests =
    let private thread = "{\"type\":\"thread.started\",\"thread_id\":\"21b16dc5-6a09-41c2-a82c-57dfcbb58c31\"}"
    let private start = "{\"type\":\"turn.started\"}"
    let private counters = "{\"input_tokens\":17,\"cached_input_tokens\":5,\"output_tokens\":7}"
    let private complete (usage: string) = "{\"type\":\"turn.completed\",\"usage\":" + usage + "}"
    let private bytes (frames: string list) = Encoding.UTF8.GetBytes(String.concat "\n" frames + "\n")
    let private decode frames = NativeExecJsonl.decode (bytes frames)
    let private unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
    let private item event id kind text =
        $"""{{"type":"{event}","item":{{"id":"{id}","type":"{kind}","text":"{text}"}}}}"""

    [<Fact>]
    let ``one cumulative completion preserves inclusive output and actual local namespace`` () =
        let usage = counters.Replace("}",",\"reasoning_output_tokens\":3,\"cache_write_input_tokens\":2}")
        let raw = bytes [thread;start;item "item.completed" "response" "agent_message" "answer";complete usage]
        let result = NativeExecJsonl.decode raw |> unwrap
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData raw),result.CaptureSha256)
        Assert.Equal(4,result.FrameCount)
        Assert.Equal(1,result.Turn.Key.StartFrameOrdinal)
        Assert.Equal(3,result.Turn.CompletionFrameOrdinal)
        Assert.Equal(0,result.Turn.TurnOrdinal)
        Assert.Equal<Guid option>(None,result.Turn.NativeTurnId)
        Assert.Equal(result.ThreadId,result.Turn.Key.ThreadId)
        Assert.Equal(result.CaptureSha256,result.Turn.Key.CaptureSha256)
        Assert.Equal(24L,result.Turn.Usage.Total) // Reasoning is already included in output seven.
        Assert.Equal<int64 option>(Some 3L,result.Turn.Usage.Reasoning)
        Assert.Equal<int64 option>(Some 2L,result.Turn.Usage.CacheWriteInput)

    [<Fact>]
    let ``missing reasoning and cache write remain unknown breakouts`` () =
        let result = decode [thread;start;complete counters] |> unwrap
        Assert.Equal<int64 option>(None,result.Turn.Usage.Reasoning)
        Assert.Equal<int64 option>(None,result.Turn.Usage.CacheWriteInput)
        Assert.Equal(24L,result.Turn.Usage.Total)

    [<Fact>]
    let ``completed only messages and tracked reasoning updates are genuine distinct patterns`` () =
        let result = decode [thread;start;item "item.started" "reason" "reasoning" "first";item "item.updated" "reason" "reasoning" "second";item "item.completed" "reason" "reasoning" "done";item "item.completed" "answer" "agent_message" "done";complete counters] |> unwrap
        Assert.Equal(6,result.Turn.CompletionFrameOrdinal)
        Assert.Equal(24L,result.Turn.Usage.Total)

    [<Theory>]
    [<InlineData("null")>]
    [<InlineData("\"3\"")>]
    [<InlineData("0.5")>]
    [<InlineData("1e0")>]
    [<InlineData("9223372036854775808")>]
    [<InlineData("-1")>]
    [<InlineData("8")>]
    let ``present malformed or excessive reasoning is refused`` (raw: string) =
        let usage = counters.Replace("}",",\"reasoning_output_tokens\":" + raw + "}")
        Assert.True(decode [thread;start;complete usage] |> Result.isError)

    [<Theory>]
    [<InlineData("command_execution")>]
    [<InlineData("file_change")>]
    [<InlineData("mcp_tool_call")>]
    [<InlineData("web_search")>]
    [<InlineData("collab_tool_call")>]
    [<InlineData("todo_list")>]
    [<InlineData("error")>]
    [<InlineData("new_provider_item")>]
    let ``unaccounted tool or model population cannot become a complete native turn`` (kind: string) =
        Assert.True(decode [thread;start;item "item.completed" "foreign" kind "unknown";complete counters] |> Result.isError)

    [<Fact>]
    let ``duplicate cumulative completion is refused rather than summed`` () =
        Assert.True(decode [thread;start;complete counters;complete counters] |> Result.isError)

    [<Fact>]
    let ``missing reordered resumed and duplicated lifecycles cannot prove population`` () =
        for frames in [[start;complete counters];[thread;complete counters];[thread;start];[thread;thread;start;complete counters];[thread;start;start;complete counters];[thread;start;complete counters;start];[thread;item "item.completed" "early" "agent_message" "answer";start;complete counters]] do
            Assert.True(decode frames |> Result.isError)

    [<Fact>]
    let ``duplicate or dangling text item lifecycle cannot hide unfinished work`` () =
        let message = item "item.completed" "same" "agent_message" "answer"
        for frames in [[thread;start;message;message;complete counters];[thread;start;item "item.started" "open" "reasoning" "unfinished";complete counters];[thread;start;item "item.updated" "absent" "reasoning" "unjoined";complete counters]] do
            Assert.True(decode frames |> Result.isError)

    [<Fact>]
    let ``duplicate properties at frame usage and nested item boundaries refuse`` () =
        for frame in ["{\"type\":\"turn.started\",\"type\":\"turn.started\"}";complete (counters.Replace("}",",\"output_tokens\":7}"));"{\"type\":\"item.completed\",\"item\":{\"id\":\"x\",\"type\":\"agent_message\",\"text\":\"a\",\"text\":\"b\"}}"] do
            Assert.True(decode [thread;start;frame;complete counters] |> Result.isError)

    [<Fact>]
    let ``unknown fields native turn IDs requested metadata and non UUID threads refuse`` () =
        for frame in [start.Replace("}",",\"turn_id\":\"invented\"}");start.Replace("}",",\"model\":\"requested\"}")] do
            Assert.True(decode [thread;frame;complete counters] |> Result.isError)
        for frame in [thread.Replace("21b16dc5-6a09-41c2-a82c-57dfcbb58c31","not-a-native-uuid");thread.Replace("21b16dc5-6a09-41c2-a82c-57dfcbb58c31","00000000-0000-0000-0000-000000000000")] do
            Assert.True(decode [frame;start;complete counters] |> Result.isError)

    [<Fact>]
    let ``every required counter rejects wrong numeric types range and missing fields`` () =
        for field,value in ["input_tokens","17";"cached_input_tokens","5";"output_tokens","7"] do
            for invalid in ["null";"\"7\"";"0.5";"-1";"9223372036854775808"] do
                let malformed = counters.Replace("\"" + field + "\":" + value,"\"" + field + "\":" + invalid)
                Assert.True(decode [thread;start;complete malformed] |> Result.isError)
        Assert.True(decode [thread;start;complete "{\"input_tokens\":17,\"output_tokens\":7}"] |> Result.isError)

    [<Fact>]
    let ``default zero cache overcount and inclusive overflow never prove free usage`` () =
        for usage in [counters.Replace("17","0").Replace("5","0");counters.Replace("7}","0}");counters.Replace("cached_input_tokens\":5","cached_input_tokens\":18");counters.Replace("}",",\"cache_write_input_tokens\":13}");counters.Replace("17","9223372036854775807")] do
            Assert.True(decode [thread;start;complete usage] |> Result.isError)

    [<Fact>]
    let ``raw bytes namespace changes with capture and frames are byte bounded`` () =
        let one = decode [thread;start;complete counters] |> unwrap
        let two = decode [thread;start;item "item.completed" "answer" "agent_message" "done";complete counters] |> unwrap
        Assert.NotEqual<string>(one.Turn.Key.CaptureSha256,two.Turn.Key.CaptureSha256)
        Assert.True(decode [thread;start;item "item.completed" "huge" "agent_message" (String.replicate 65536 "x");complete counters] |> Result.isError)
        Assert.True(NativeExecJsonl.decode (Array.create 262145 10uy) |> Result.isError)
        let many = [thread;start] @ [for index in 0..4093 -> item "item.completed" (string index) "agent_message" ""] @ [complete counters]
        Assert.True(decode many |> Result.isError)

    [<Fact>]
    let ``per frame byte limit is exact and counts multibyte UTF8 rather than characters`` () =
        let empty = item "item.completed" "answer" "agent_message" ""
        let padding = 65536 - Encoding.UTF8.GetByteCount empty
        let exact = item "item.completed" "answer" "agent_message" (String.replicate padding "x")
        Assert.True(decode [thread;start;exact;complete counters] |> Result.isOk)
        let over = item "item.completed" "answer" "agent_message" (String.replicate (padding/2 + 1) "é")
        Assert.True(decode [thread;start;over;complete counters] |> Result.isError)

    [<Fact>]
    let ``empty blank truncated malformed UTF8 and failed frames remain unavailable`` () =
        let raw = bytes [thread;start;complete counters]
        for input in [[||];raw[..raw.Length-2];bytes [thread;"";start;complete counters];[|255uy;10uy|];bytes [thread;start;"{\"type\":\"turn.failed\",\"error\":{\"message\":\"failed\"}}"]] do
            Assert.True(NativeExecJsonl.decode input |> Result.isError)
