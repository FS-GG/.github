open FS.GG.Org.EffectiveSteps

let checker = "scripts/check-alpha.py"
let fixture = "tests/alpha/run.sh"
let fixtureBody = "#!/bin/sh\npython3 scripts/check-alpha.py\n"
let workflow (body: string) (jobExtras: string) (stepExtras: string) =
    "name: fixture\njobs:\n  gate:\n    runs-on: ubuntu-latest\n"
    + jobExtras + "    steps:\n      - name: gate\n" + stepExtras
    + "        run: |\n"
    + (body.Split('\n') |> Array.map (fun line -> "          " + line + "\n") |> String.concat "")

let direct = "python3 scripts/check-alpha.py\nbash tests/alpha/run.sh"
let check label expected yaml body =
    let actual = EffectiveSteps.evaluate yaml body checker fixture
    match expected, actual with
    | true, Ok () -> printfn "ok %s" label
    | false, Error _ -> printfn "ok %s" label
    | _ -> failwithf "%s: expected pass=%b, got %A" label expected actual

check "multiline direct run" true (workflow direct "" "") fixtureBody
check "transitive executable fixture" true (workflow "bash tests/alpha/run.sh" "" "") fixtureBody
check "comment-only workflow mention" false (workflow "echo harmless\n# python3 scripts/check-alpha.py\n# bash tests/alpha/run.sh" "" "") fixtureBody
check "comment-only checker mention" false (workflow "bash tests/alpha/run.sh" "" "") "#!/bin/sh\n# python3 scripts/check-alpha.py\n"
check "dead checker and-list" false (workflow "false && python3 scripts/check-alpha.py\nbash tests/alpha/run.sh" "" "") "#!/bin/sh\necho harmless\n"
check "masked checker fallback" false (workflow "python3 scripts/check-alpha.py || echo ignored\nbash tests/alpha/run.sh" "" "") "#!/bin/sh\necho harmless\n"
check "masked fixture fallback" false (workflow "bash tests/alpha/run.sh || echo ignored" "" "") fixtureBody
check "dead transitive checker" false (workflow "bash tests/alpha/run.sh" "" "") "#!/bin/sh\nfalse && python3 scripts/check-alpha.py\n"
check "masked transitive checker" false (workflow "bash tests/alpha/run.sh" "" "") "#!/bin/sh\npython3 scripts/check-alpha.py || echo ignored\n"
check "continued masked checker" false (workflow "python3 scripts/check-alpha.py \\\n  || echo ignored\nbash tests/alpha/run.sh" "" "") "#!/bin/sh\necho harmless\n"
check "continued masked fixture" false (workflow "bash tests/alpha/run.sh \\\n  || echo ignored" "" "") fixtureBody
check "continued masked transitive checker" false (workflow "bash tests/alpha/run.sh" "" "") "#!/bin/sh\npython3 scripts/check-alpha.py \\\n  || echo ignored\n"
check "duplicate run key" false (workflow direct "" "        run: echo harmless\n") fixtureBody
check "duplicate jobs key" false ((workflow direct "" "") + "jobs:\n  other:\n    steps: []\n") fixtureBody
check "job cannot both call workflow and carry steps" false (workflow direct "    uses: ./.github/workflows/other.yml\n" "") fixtureBody
check "step cannot both use action and run shell" false (workflow direct "" "        uses: actions/checkout@v4\n") fixtureBody
check "disabled job" false (workflow direct "    if: false\n" "") fixtureBody
check "disabled step" false (workflow direct "" "        if: false\n") fixtureBody
check "non-gating step" false (workflow direct "" "        continue-on-error: true\n") fixtureBody
check "quoted literal false continue-on-error" true (workflow direct "" "        continue-on-error: \"${{ false }}\"\n") fixtureBody
check "inert quoted example" false (workflow "echo 'python3 scripts/check-alpha.py'\nbash tests/alpha/run.sh" "" "") "#!/bin/sh\necho harmless\n"
check "malformed YAML" false "name: [bad\n" fixtureBody
check "malformed steps shape" false "jobs:\n  gate:\n    steps: nope\n" fixtureBody
printfn "23 executable-step controls passed"
