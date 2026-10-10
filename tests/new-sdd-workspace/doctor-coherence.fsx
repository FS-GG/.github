// Exercise the actual compiled Creator decision, not a copy of its JSON parser.
open NewSddWorkspace.Program
let coherent = """{"schemaVersion":1,"command":{"name":"doctor"},"outcome":"succeeded","coherent":true,"doctor":{"isCoherent":true}}"""
let drift = """{"schemaVersion":1,"command":{"name":"doctor"},"outcome":"succeededWithWarnings","coherent":false,"doctor":{"isCoherent":false}}"""
if doctorCoherence 0 coherent <> Ok true then failwith "explicit coherent verdict was lost"
if doctorCoherence 0 drift <> Ok false then failwith "exit-zero drift was reported coherent"
if doctorCoherence 0 (coherent.Replace("\"coherent\":true", "\"coherent\":false")) <> Ok true then failwith "generic stage false replaced nested doctor verdict"
if doctorCoherence 0 (coherent.Replace("\"isCoherent\":true", "\"isCoherent\":false")) <> Ok false then failwith "generic stage true hid nested doctor drift"
for malformed in [ ""; "not JSON"; "null"; "[]"; "{}"; "{\"coherent\":true}";
                   coherent.Replace("\"coherent\":true", "\"coherent\":\"true\"");
                   coherent.Replace("\"doctor\"}", "\"upgrade\"}");
                   coherent.Replace("\"schemaVersion\":1", "\"schemaVersion\":2");
                   coherent.Replace("\"succeeded\"", "\"failed\"");
                   coherent.Replace("\"coherent\":true", "\"coherent\":true,\"coherent\":false") ] do
    match doctorCoherence 0 malformed with
    | Error _ -> ()
    | _ -> failwithf "missing, malformed or contradictory doctor verdict accepted: %s" malformed
for code in [ 1; 2; 127 ] do
    match doctorCoherence code coherent with
    | Error _ -> ()
    | _ -> failwithf "failed doctor exit%d claimed coherence" code
// Immutable actual SDD 2.1 stdout, bound to authentic package source commit518517f6.
// Healthy raw SHA256 7f28b67718db339217d31fd675d254a755559a630e55dd5d4ce2e0a3837400d8; drift raw SHA256 6e051f4743fbc36eecf2811c4e8d071814cd2f7eb185a6f8fdba927a2f7efc7e.
// Generic stage coherent is not doctor.isCoherent (ReportAssembly + HandlersDoctor).
let genuineHealthyDoctor = """{
  "schemaVersion": 1,
  "reportVersion": "2.6.0",
  "toolVersion": "2.1.0",
  "command": {
    "name": "doctor",
    "stage": "doctor"
  },
  "context": {
    "projectRoot": ".",
    "workId": null
  },
  "invocation": {
    "outputFormat": "json",
    "dryRun": false
  },
  "outcome": "noChange",
  "coherent": false,
  "changedArtifacts": [],
  "specification": null,
  "clarification": null,
  "checklist": null,
  "plan": null,
  "tasks": null,
  "analysis": null,
  "evidence": null,
  "verification": null,
  "ship": null,
  "agentGuidance": null,
  "refresh": null,
  "scaffold": null,
  "doctor": {
    "hasProvenance": true,
    "providerName": "rendering",
    "installedCliVersion": "2.1.0",
    "requiredMinimumCliVersion": "2.1.0",
    "requiredMinimumCliVersionSource": "providerDescriptor",
    "cliAxis": "atOrAbove",
    "cliBehindBy": null,
    "expectedArtifactCount": 44,
    "missingArtifactPaths": [],
    "skillDriftPaths": [],
    "previewSteps": [
      {
        "stepId": "cliSelfUpdate",
        "kind": "cliSelfUpdate",
        "diffPreview": "no CLI version target",
        "outcome": "noTarget",
        "targetPaths": []
      },
      {
        "stepId": "templateRePin",
        "kind": "templateRePin",
        "diffPreview": "no re-pin target",
        "outcome": "noTarget",
        "targetPaths": []
      },
      {
        "stepId": "artifactReSeed",
        "kind": "artifactReSeed",
        "diffPreview": "no missing artifacts",
        "outcome": "noTarget",
        "targetPaths": []
      }
    ],
    "isCoherent": true
  },
  "upgrade": null,
  "lint": null,
  "surface": null,
  "dependencySurface": null,
  "generatedViews": [],
  "diagnostics": [],
  "governanceCompatibility": [
    {
      "path": ".fsgg/capabilities.yml",
      "relationship": "optionalGovernanceCapabilities",
      "requiredBySdd": false,
      "state": "notEvaluated",
      "diagnosticIds": []
    },
    {
      "path": ".fsgg/policy.yml",
      "relationship": "optionalGovernancePolicy",
      "requiredBySdd": false,
      "state": "notEvaluated",
      "diagnosticIds": []
    },
    {
      "path": ".fsgg/tooling.yml",
      "relationship": "optionalGovernanceTooling",
      "requiredBySdd": false,
      "state": "notEvaluated",
      "diagnosticIds": []
    }
  ],
  "nextAction": {
    "actionId": "doctor.coherent",
    "command": null,
    "workId": null,
    "reason": "Scaffold is coherent \u2014 nothing to reconcile.",
    "requiredArtifacts": [],
    "blockingDiagnosticIds": []
  },
  "help": null,
  "lifecycleStatus": {
    "workId": null,
    "isLifecycleStage": false,
    "currentOrdinal": 0,
    "totalStages": 10,
    "outcome": "noChange",
    "nextCommand": "charter",
    "stages": [
      {
        "command": "charter",
        "ordinal": 1,
        "state": "next"
      },
      {
        "command": "specify",
        "ordinal": 2,
        "state": "pending"
      },
      {
        "command": "clarify",
        "ordinal": 3,
        "state": "pending"
      },
      {
        "command": "checklist",
        "ordinal": 4,
        "state": "pending"
      },
      {
        "command": "plan",
        "ordinal": 5,
        "state": "pending"
      },
      {
        "command": "tasks",
        "ordinal": 6,
        "state": "pending"
      },
      {
        "command": "analyze",
        "ordinal": 7,
        "state": "pending"
      },
      {
        "command": "evidence",
        "ordinal": 8,
        "state": "pending"
      },
      {
        "command": "verify",
        "ordinal": 9,
        "state": "pending"
      },
      {
        "command": "ship",
        "ordinal": 10,
        "state": "pending"
      }
    ]
  }
}
"""
let genuineDriftDoctor = """{
  "schemaVersion": 1,
  "reportVersion": "2.6.0",
  "toolVersion": "2.1.0",
  "command": {
    "name": "doctor",
    "stage": "doctor"
  },
  "context": {
    "projectRoot": ".",
    "workId": null
  },
  "invocation": {
    "outputFormat": "json",
    "dryRun": false
  },
  "outcome": "succeededWithWarnings",
  "coherent": false,
  "changedArtifacts": [],
  "specification": null,
  "clarification": null,
  "checklist": null,
  "plan": null,
  "tasks": null,
  "analysis": null,
  "evidence": null,
  "verification": null,
  "ship": null,
  "agentGuidance": null,
  "refresh": null,
  "scaffold": null,
  "doctor": {
    "hasProvenance": true,
    "providerName": "rendering",
    "installedCliVersion": "2.1.0",
    "requiredMinimumCliVersion": "2.1.0",
    "requiredMinimumCliVersionSource": "providerDescriptor",
    "cliAxis": "atOrAbove",
    "cliBehindBy": null,
    "expectedArtifactCount": 44,
    "missingArtifactPaths": [],
    "skillDriftPaths": [
      ".agents/skills/padd-item/SKILL.md",
      ".agents/skills/work-board/SKILL.md",
      ".agents/skills/work-board/references/workspace-scope.md",
      ".claude/skills/padd-item/SKILL.md",
      ".claude/skills/work-board/SKILL.md",
      ".claude/skills/work-board/references/workspace-scope.md"
    ],
    "previewSteps": [
      {
        "stepId": "cliSelfUpdate",
        "kind": "cliSelfUpdate",
        "diffPreview": "no CLI version target",
        "outcome": "noTarget",
        "targetPaths": []
      },
      {
        "stepId": "templateRePin",
        "kind": "templateRePin",
        "diffPreview": "no re-pin target",
        "outcome": "noTarget",
        "targetPaths": []
      },
      {
        "stepId": "artifactReSeed",
        "kind": "artifactReSeed",
        "diffPreview": "no missing artifacts",
        "outcome": "noTarget",
        "targetPaths": []
      }
    ],
    "isCoherent": false
  },
  "upgrade": null,
  "lint": null,
  "surface": null,
  "dependencySurface": null,
  "generatedViews": [],
  "diagnostics": [
    {
      "id": "doctor.driftDetected",
      "severity": "warning",
      "artifact": null,
      "location": null,
      "message": "The scaffold has drifted from its coherent set (CLI behind the declared minimum and/or seeded artifacts missing).",
      "correction": "Run \u0060fsgg-sdd upgrade\u0060 to reconcile each step interactively, or \u0060fsgg-sdd upgrade --yes\u0060 non-interactively.",
      "relatedIds": []
    }
  ],
  "governanceCompatibility": [
    {
      "path": ".fsgg/capabilities.yml",
      "relationship": "optionalGovernanceCapabilities",
      "requiredBySdd": false,
      "state": "notEvaluated",
      "diagnosticIds": []
    },
    {
      "path": ".fsgg/policy.yml",
      "relationship": "optionalGovernancePolicy",
      "requiredBySdd": false,
      "state": "notEvaluated",
      "diagnosticIds": []
    },
    {
      "path": ".fsgg/tooling.yml",
      "relationship": "optionalGovernanceTooling",
      "requiredBySdd": false,
      "state": "notEvaluated",
      "diagnosticIds": []
    }
  ],
  "nextAction": {
    "actionId": "doctor.next.upgrade",
    "command": "upgrade",
    "workId": null,
    "reason": "Drift detected; run \u0060fsgg-sdd upgrade\u0060 to reconcile each step interactively (or \u0060fsgg-sdd upgrade --yes\u0060 non-interactively).",
    "requiredArtifacts": [],
    "blockingDiagnosticIds": []
  },
  "help": null,
  "lifecycleStatus": {
    "workId": null,
    "isLifecycleStage": false,
    "currentOrdinal": 0,
    "totalStages": 10,
    "outcome": "succeededWithWarnings",
    "nextCommand": "charter",
    "stages": [
      {
        "command": "charter",
        "ordinal": 1,
        "state": "next"
      },
      {
        "command": "specify",
        "ordinal": 2,
        "state": "pending"
      },
      {
        "command": "clarify",
        "ordinal": 3,
        "state": "pending"
      },
      {
        "command": "checklist",
        "ordinal": 4,
        "state": "pending"
      },
      {
        "command": "plan",
        "ordinal": 5,
        "state": "pending"
      },
      {
        "command": "tasks",
        "ordinal": 6,
        "state": "pending"
      },
      {
        "command": "analyze",
        "ordinal": 7,
        "state": "pending"
      },
      {
        "command": "evidence",
        "ordinal": 8,
        "state": "pending"
      },
      {
        "command": "verify",
        "ordinal": 9,
        "state": "pending"
      },
      {
        "command": "ship",
        "ordinal": 10,
        "state": "pending"
      }
    ]
  }
}
"""
if doctorCoherence 0 genuineHealthyDoctor <> Ok true then failwith "genuine SDD21 noChange/topfalse/nestedtrue was rejected"
if doctorCoherence 0 genuineDriftDoctor <> Ok false then failwith "genuine SDD21 driftfalse was accepted coherent"
printfn "Doctor JSON coherence controls passed: 20 actual judgments: genuine SDD21 healthy/drift, independent generic stage signal, malformed/absent and failed command."
