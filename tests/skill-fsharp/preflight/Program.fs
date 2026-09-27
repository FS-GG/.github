open System
open System.IO
open System.Text
open FS.GG.Coord.Cli.SkillPreflight

let bytes (value: string) = ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(value))

let fail message = raise (Exception message)

let expectEqual label expected actual =
    if expected <> actual then fail $"{label}: expected {expected}, got {actual}"

let expectRefusal label (expectedFragment: string) result =
    match result with
    | Error refusal when refusal.ExitCode = 2 && refusal.Message.Contains(expectedFragment, StringComparison.Ordinal) -> ()
    | Error refusal -> fail $"{label}: unexpected refusal {refusal.ExitCode}: {refusal.Message}"
    | Ok _ -> fail $"{label}: unexpectedly accepted"

let arguments = Environment.GetCommandLineArgs()
if arguments.Length <> 2 then fail "usage: SkillPreflight.Tests REPOSITORY_ROOT"
let fixture name = File.ReadAllText(Path.Combine(arguments[1], "tests/skill-fsharp/contracts/fixtures", name))

let positiveEstimate = fixture "assess-positive.json"

let expectedPositive =
    """{
  "decision": "pilot-candidate",
  "basis": "estimates, not observed savings",
  "assumptions": "Synthetic public fixture with explicit rates.",
  "outcomes": {
    "low": {
      "net_benefit": 80.0,
      "net_per_run": 1.0,
      "break_even_runs": 20
    },
    "high": {
      "net_benefit": 180.0,
      "net_per_run": 2.0,
      "break_even_runs": 10
    }
  },
  "authority": "advisory; never waives required checks"
}
"""

match assess (bytes positiveEstimate) with
| Ok(PilotCandidate, output) -> expectEqual "positive estimate bytes" expectedPositive (Encoding.UTF8.GetString(output))
| other -> fail $"positive estimate: {other}"

let insufficient = fixture "assess-insufficient.json"
let expectedInsufficient =
    """{
  "decision": "insufficient-data",
  "missing": [
    "setup_cost"
  ],
  "basis": "estimates, not observed savings"
}
"""
match assess (bytes insufficient) with
| Ok(InsufficientData, output) -> expectEqual "insufficient estimate bytes" expectedInsufficient (Encoding.UTF8.GetString(output))
| other -> fail $"insufficient estimate: {other}"

expectRefusal "nonfinite estimate" "finite nonnegative" (assess (bytes (positiveEstimate.Replace("\"setup_cost\": 20", "\"setup_cost\": 1e999"))))
expectRefusal "duplicate estimate field" "Duplicate JSON key" (assess (bytes "{\"horizon_runs\":1,\"horizon_runs\":2}"))
expectRefusal "negative estimate" "finite nonnegative" (assess (bytes (positiveEstimate.Replace("\"setup_cost\": 20", "\"setup_cost\": -1"))))
expectRefusal "fractional horizon" "horizon_runs must be an integer" (assess (bytes (positiveEstimate.Replace("\"horizon_runs\": 100", "\"horizon_runs\": 1.5"))))
expectRefusal "probability range" "between 0 and 1" (assess (bytes (positiveEstimate.Replace("\"detection_probability\": 1", "\"detection_probability\": 2"))))
expectRefusal "reversed range" "Probability bounds are reversed" (assess (bytes (positiveEstimate.Replace("\"defect_probability_high\": 0.75", "\"defect_probability_high\": 0.25"))))
expectRefusal "unknown field" "Unknown fields" (assess (bytes (positiveEstimate.Replace("\n}", ",\n  \"setup_cots\": 1\n}"))))
expectRefusal "empty assumptions" "Provide the horizon" (assess (bytes (positiveEstimate.Replace("Synthetic public fixture with explicit rates.", ""))))

let reference name = File.ReadAllText(Path.Combine(arguments[1], ".agents/skills/pipeline-preflight/references", name))
match assess (bytes (reference "one-off.json")) with
| Ok(NotCostJustified, output) when Encoding.UTF8.GetString(output).Contains("-718.505", StringComparison.Ordinal) -> ()
| other -> fail $"one-off economics parity: {other}"
match assess (bytes (reference "recurring.json")) with
| Ok(PilotCandidate, output) when Encoding.UTF8.GetString(output).Contains("80.0", StringComparison.Ordinal) -> ()
| other -> fail $"recurring economics parity: {other}"
match assess (bytes ((reference "recurring.json").Replace("\"defect_probability_low\": 0.02", "\"defect_probability_low\": 0"))) with
| Ok(Uncertain, _) -> ()
| other -> fail $"uncertain economics parity: {other}"

let escapedAssumption = positiveEstimate.Replace("Synthetic public fixture with explicit rates.", "é<&")
match assess (bytes escapedAssumption) with
| Ok(_, output) when Encoding.UTF8.GetString(output).Contains("\\u00e9<&", StringComparison.Ordinal) -> ()
| other -> fail $"Python JSON string spelling: {other}"

let passedWorkflow = fixture "graph-passed.yml"
let expectedPassed =
    """{
  "decision": "passed",
  "scope": "dependency-order-only",
  "jobs": 4,
  "requirements": [
    "report:shard_a,shard_b,build"
  ]
}
"""
match graph (bytes passedWorkflow) [ "report:shard_a,shard_b,build" ] with
| Ok(Passed, output) -> expectEqual "passed graph bytes" expectedPassed (Encoding.UTF8.GetString(output))
| other -> fail $"passed graph: {other}"

let actualWorkflow = File.ReadAllText(Path.Combine(arguments[1], ".github/workflows/coord-engine.yml"))
match graph (bytes actualWorkflow) [ "engine:change-completeness" ] with
| Ok(Passed, _) -> ()
| other -> fail $"actual workflow parser qualification: {other}"

let blockedWorkflow = fixture "graph-blocked.yml"
let expectedBlocked =
    """{
  "decision": "blocked",
  "scope": "dependency-order-only",
  "target": "report",
  "missing_ancestors": [
    "shard_b"
  ]
}
"""
match graph (bytes blockedWorkflow) [ "report:shard_a,shard_b" ] with
| Ok(Blocked [ "shard_b" ], output) -> expectEqual "blocked graph bytes" expectedBlocked (Encoding.UTF8.GetString(output))
| other -> fail $"blocked graph: {other}"

let refusals =
    [ "duplicate YAML key", fixture "graph-duplicate.yml", "Duplicate key"
      "malformed YAML", "jobs: [", "While parsing"
      "unknown dependency", "jobs: { build: {}, report: { needs: missing } }", "unknown dependencies"
      "cycle", "jobs: { build: { needs: report }, report: { needs: build } }", "Dependency cycle"
      "expression", "jobs: { build: {}, report: { needs: '${{ inputs.needs }}' } }", "expressions are unsupported"
      "unsupported needs mapping", "jobs: { build: {}, report: { needs: { job: build } } }", "expressions are unsupported"
      "duplicate dependency", "jobs: { build: {}, report: { needs: [build, build] } }", "duplicate dependency"
      "unknown requirement", "jobs: { build: {} }", "Unknown or malformed requirement" ]

for label, yaml, fragment in refusals do
    let requirements = if label = "unknown requirement" then [ "report:build" ] else [ "report:build" ]
    expectRefusal label fragment (graph (bytes yaml) requirements)

let boundedJobs =
    [ 0..256 ] |> List.map (fun number -> $"job_{number}: {{}}") |> String.concat ", " |> fun jobs -> $"jobs: {{ {jobs} }}"
expectRefusal "job bound" "More than 256 jobs" (graph (bytes boundedJobs) [ "job_1:job_0" ])

let oversize = Array.create (2 * 1024 * 1024 + 1) (byte 'x')
expectRefusal "input bound" "Input exceeds 2 MiB" (graph (ReadOnlyMemory<byte>(oversize)) [ "report:build" ])

let invalidUtf8 = ReadOnlyMemory<byte>([| 0xffuy |])
expectRefusal "invalid UTF-8" "Unable to translate bytes" (graph invalidUtf8 [ "report:build" ])

printfn "SKILL-FS-01.4 preflight: PASS"
