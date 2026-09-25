namespace FS.GG.Org.Policy.Tests

open FS.GG.Org.Policy
open Xunit

module RuleATests =
    let private check yaml = RuleA.inspect "sample.yml" yaml
    let private refusal code yaml =
        match (check yaml).Verdict with
        | NoVerdict diagnostic -> Assert.Equal(code, diagnostic.Code)
        | other -> failwithf "expected %s no-verdict, got %A" code other

    let private finding yaml =
        match (check yaml).Verdict with
        | Finding _ -> Assert.True((check yaml).AuditedPair)
        | other -> failwithf "expected finding, got %A" other

    let private agreement audited yaml =
        let actual = check yaml
        Assert.Equal(audited, actual.AuditedPair)
        Assert.Equal(Agreement, actual.Verdict)

    [<Fact>]
    let ``identical allow lists agree as sets`` () =
        agreement true "on: {pull_request: {paths: [a/**, b/**]}, push: {paths: [b/**, a/**]}}\n"

    [<Theory>]
    [<InlineData("on")>]
    [<InlineData("off")>]
    [<InlineData("yes")>]
    [<InlineData("no")>]
    [<InlineData("0b101")>]
    [<InlineData("1:20")>]
    [<InlineData("2026-01-01T00:00:00Z")>]
    let ``implicit YAML 1 1 typed scalars cannot mimic quoted string paths`` word =
        refusal "paths-shape" ("on: {pull_request: {paths: [" + word + "]}, push: {paths: ['" + word + "']}}\n")

    [<Fact>]
    let ``drift is a finding`` () =
        finding "on: {pull_request: {paths: [a/**]}, push: {paths: [b/**]}}\n"

    [<Theory>]
    [<InlineData("pull_request")>]
    [<InlineData("push")>]
    let ``unsigned filtered split is a finding in either direction`` filtered =
        let yaml =
            if filtered = "push" then "on: {pull_request: null, push: {paths: [a/**]}}\n"
            else "on: {pull_request: {paths: [a/**]}, push: null}\n"
        finding yaml

    [<Theory>]
    [<InlineData("pull_request")>]
    [<InlineData("push")>]
    let ``signed filtered split is provisionally allowed`` filtered =
        let yaml =
            if filtered = "push" then "on: {pull_request: null, push: {paths: [a/**]}}\n"
            else "on: {pull_request: {paths: [a/**]}, push: null}\n"
        agreement true ("# paths-coherence: allow-divergence — deliberate split\n" + yaml)

    [<Theory>]
    [<InlineData("[]", "paths-empty")>]
    [<InlineData("null", "paths-shape")>]
    [<InlineData("a/**", "paths-shape")>]
    [<InlineData("['!a/**']", "paths-negated")>]
    [<InlineData("[42]", "paths-shape")>]
    let ``signed split cannot excuse malformed filter`` paths code =
        let yaml = "# paths-coherence: allow-divergence — deliberate split\non: {pull_request: null, push: {paths: " + paths + "}}\n"
        refusal code yaml

    [<Fact>]
    let ``explicit null differs from absent paths`` () =
        refusal "paths-shape" "on: {pull_request: {paths: null}, push: null}\n"

    [<Fact>]
    let ``opposing paths-ignore refuses`` () =
        refusal "paths-ignore" "on: {pull_request: {paths: [a/**]}, push: {paths-ignore: [b/**]}}\n"

    [<Fact>]
    let ``one-sided paths-ignore is outside rule a`` () =
        agreement false "on: {push: {paths-ignore: [a/**]}}\n"

    [<Fact>]
    let ``one-sided filtered workflow is outside rule a`` () =
        agreement false "on: {push: {paths: [a/**]}}\n"

    [<Fact>]
    let ``unsigned comment remains a finding`` () =
        finding "# paths-coherence: allow-divergence\non: {pull_request: {paths: [a/**]}, push: {paths: [b/**]}}\n"

    [<Fact>]
    let ``marker suffix without a separator cannot sign drift`` () =
        finding "# paths-coherence: allow-divergenceevil\non: {pull_request: {paths: [a/**]}, push: {paths: [b/**]}}\n"

    [<Fact>]
    let ``signed comment on equal lists is stale`` () =
        finding "# paths-coherence: allow-divergence: a reason\non: {pull_request: {paths: [a/**]}, push: {paths: [a/**]}}\n"

    [<Fact>]
    let ``a later signed comment wins over explanatory unsigned comment`` () =
        agreement true "# paths-coherence: allow-divergence\n# paths-coherence: allow-divergence — deliberate split\non: {pull_request: null, push: {paths: [a/**]}}\n"

    [<Theory>]
    [<InlineData("|")>]
    [<InlineData(">")>]
    let ``marker inside run scalar is inert`` scalar =
        let yaml = "on: {pull_request: {paths: [a/**]}, push: {paths: [b/**]}}\njobs:\n  x:\n    steps:\n      - run: " + scalar + "\n          # paths-coherence: allow-divergence — shell text\n"
        finding yaml

    [<Fact>]
    let ``marker inside multiline quoted run scalar is inert`` () =
        let yaml = "on: {pull_request: {paths: [a/**]}, push: {paths: [b/**]}}\njobs:\n  x:\n    steps:\n      - run: \"echo hello\n          # paths-coherence: allow-divergence — shell text\"\n"
        finding yaml

    [<Fact>]
    let ``marker inside multiline single quoted run scalar is inert`` () =
        let yaml = "on: {pull_request: {paths: [a/**]}, push: {paths: [b/**]}}\njobs:\n  x:\n    steps:\n      - run: 'echo hello\n          # paths-coherence: allow-divergence — shell text'\n"
        finding yaml

    [<Fact>]
    let ``real comment after multiline quoted run scalar can sign`` () =
        let yaml = "on: {pull_request: {paths: [a/**]}, push: {paths: [b/**]}}\njobs:\n  x:\n    steps:\n      - run: \"echo hello\n          # shell text\"\n# paths-coherence: allow-divergence — real YAML comment\n"
        agreement true yaml

    [<Fact>]
    let ``an indented YAML comment can sign a split`` () =
        agreement true "on:\n  pull_request: null\n  push:\n    # paths-coherence: allow-divergence — intentional\n    paths: [a/**]\n"

    [<Fact>]
    let ``real comment after a run block can sign a split`` () =
        let yaml = "on: {pull_request: null, push: {paths: [a/**]}}\njobs:\n  x:\n    steps:\n      - run: |\n          echo ok\n# paths-coherence: allow-divergence — intentional\n"
        agreement true yaml

    [<Fact>]
    let ``malformed YAML is no verdict`` () =
        refusal "yaml-invalid" "on: [push\n"

    [<Fact>]
    let ``tagged duplicate trigger key cannot yield a rule a verdict`` () =
        refusal "yaml-invalid" "on:\n  pull_request: {paths: [a/**]}\n  push: {paths: [a/**]}\n  !!str push: {paths: [b/**]}\n"

    [<Fact>]
    let ``non-string tagged trigger cannot yield a rule a verdict`` () =
        refusal "on-shape" "on: {pull_request: {paths: [a/**]}, !!int push: {paths: [a/**]}}\n"

    [<Fact>]
    let ``zero pair audit refuses a false green`` () =
        let result = RuleA.audit [ "one.yml", "on: {push: {paths: [a/**]}}\n" ]
        Assert.Equal(0, result.AuditedPairs)
        match result.Verdict with
        | NoVerdict diagnostic -> Assert.Equal("zero-pairs", diagnostic.Code)
        | other -> failwithf "expected zero-pairs refusal, got %A" other

    [<Fact>]
    let ``one clean pair cannot hide a split finding`` () =
        let result = RuleA.audit [
            "clean.yml", "on: {pull_request: {paths: [a/**]}, push: {paths: [a/**]}}\n"
            "split.yml", "on: {pull_request: null, push: {paths: [b/**]}}\n"
        ]
        Assert.Equal(2, result.AuditedPairs)
        match result.Verdict with
        | Finding _ -> ()
        | other -> failwithf "expected split finding, got %A" other

    [<Fact>]
    let ``syntax uncertainty outranks a separate finding`` () =
        let result = RuleA.audit [
            "bad.yml", "on: [push\n"
            "split.yml", "on: {pull_request: null, push: {paths: [b/**]}}\n"
        ]
        Assert.Equal(1, result.AuditedPairs)
        match result.Verdict with
        | NoVerdict diagnostic -> Assert.Equal("yaml-invalid", diagnostic.Code)
        | other -> failwithf "expected no-verdict, got %A" other
