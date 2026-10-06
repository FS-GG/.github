"""Pure installed/static Responses artifact producer; never launches or grants authority.

The existing custody owner calls these functions with retained bytes from one fresh
bounded attempt. Synthetic unit receipts establish decoder behavior only. A result
requires the owner's genuine settlement, exact TRX cases, installed evidence, and
independent verifier replies. This module is not a receipt-authentication service.
"""
from __future__ import annotations
import base64
import hashlib
import json
import math
import re
import xml.etree.ElementTree as ET

SCENARIOS = tuple(sorted(("bounded-client-wire-cardinality", "cancellation-retirement",
    "credential-role-separation", "current-installed-closure", "denied-count-no-generation",
    "request-policy-caps", "verifier-mutated-capture-refused", "verifier-stale-snapshot-refused",
    "verifier-valid-capture")))
POLICY = "7a0e6970101b8cc9343c23ebe5d273e183cf4535b4dc09fca4d48a0bee7ae071"
VERIFIER = "599034490c93333875b169c7fc4d3e873e3d911fe5571ad90127ea86b1e8b373"
PROFILE_FIELDS = set("schema sourceVariant provider model effort countEndpoint generationEndpoint inputTokenLimit outputTokenLimit wholeMilliseconds networkMilliseconds requestPolicySha256 instructionsSha256 responseSchemaSha256 responseSchemaName verifierModuleSha256 verifierRuntimeManifestSha256 installedRoots installedFiles".split())
METHODS = {
 "bounded-client-wire-cardinality": ("FS.GG.Telemetry.Tests.DirectResponsesWireTests", "actual sockets wire sends no redirect authentication or ambiguous retry", 6),
 "cancellation-retirement": ("FS.GG.Telemetry.Tests.DirectResponsesWireTests", "cancellation after actual count receipt retires owned wire without generation or retry", 1),
 "denied-count-no-generation": ("FS.GG.Telemetry.Tests.DirectResponsesTests", "denied count never invokes generation", 4),
 "request-policy-caps": ("FS.GG.Coord.Tests.NativeResponsesTests", "count and generation share full schema and all nine documented input fields", 1),
 "credential-role-separation": ("FS.GG.Telemetry.Tests.NativeResponsesStaticQualificationTests", "credential roles are checked without reading secret contents", 1),
 "current-installed-closure": ("FS.GG.Telemetry.Tests.NativeResponsesStaticQualificationTests", "installed static qualification binds the actual loaded product closure", 1),
}
class Refusal(ValueError): pass

def require(ok, reason):
    if not ok: raise Refusal(reason)

def sha(raw): return hashlib.sha256(raw).hexdigest()
def digest(value): return isinstance(value,str) and re.fullmatch(r"[0-9a-f]{64}",value) is not None and value != "0"*64
def integer(value): return type(value) is int and 0 <= value <= 2**63-1

def parse(raw, bound=65536):
    require(type(raw) is bytes and 0 < len(raw) <= bound, "byte-bound")
    def pairs(rows):
        result={}
        for key,value in rows:
            require(key not in result, "duplicate-property")
            result[key]=value
        return result
    try: return json.loads(raw.decode("utf-8",errors="strict"),object_pairs_hook=pairs,parse_constant=lambda _: (_ for _ in ()).throw(Refusal("nonfinite-json")))
    except (UnicodeError,json.JSONDecodeError) as error: raise Refusal("malformed-json") from error

def exact(value, fields):
    require(type(value) is dict and set(value)==set(fields), "closed-shape")
    return value

def encode(value): return json.dumps(value,separators=(",",":"),ensure_ascii=True,allow_nan=False).encode()
def path(value):
    return isinstance(value,str) and value.startswith("/") and value!="/" and all(part not in ("",".","..") for part in value[1:].split("/")) and not any(ord(c)<32 for c in value)
def below(root,value): return value.startswith(root+"/")

def property_bytes(raw, name):
    """Retain the exact managed serializer's property bytes, never emulate its encoder."""
    value=parse(raw); require(type(value) is dict and name in value,"property-absent")
    text=raw.decode(); decoder=json.JSONDecoder(); index=0
    def skip(i):
        while i<len(text) and text[i].isspace(): i+=1
        return i
    index=skip(index); require(text[index]=="{","malformed-json"); index+=1
    while True:
        index=skip(index); key,index=decoder.raw_decode(text,index); index=skip(index)
        require(text[index]==":","malformed-json"); index=skip(index+1); start=index
        _,index=decoder.raw_decode(text,index)
        if key==name: return text[start:index].encode("utf-8")
        index=skip(index); require(text[index]==",","property-absent"); index+=1

def validate_profile(raw):
    profile=exact(parse(raw),PROFILE_FIELDS)
    fixed={"schema":"fsgg.telemetry.responses-capability-profile/1","sourceVariant":"openai-responses/1","provider":"openai","model":"gpt-6.1-sol","effort":"medium","countEndpoint":"https://api.openai.com/v1/responses/input_tokens","generationEndpoint":"https://api.openai.com/v1/responses","requestPolicySha256":POLICY,"verifierModuleSha256":VERIFIER}
    require(all(profile[k]==v for k,v in fixed.items()),"fixed-policy")
    for key,value in (("inputTokenLimit",8000),("outputTokenLimit",1500),("wholeMilliseconds",60000),("networkMilliseconds",55000)):
        require(type(profile[key]) is int and profile[key]==value,"fixed-policy")
    for key in ("instructionsSha256","responseSchemaSha256","verifierModuleSha256","verifierRuntimeManifestSha256"):
        require(digest(profile[key]),"profile-digest")
    require(isinstance(profile["responseSchemaName"],str) and re.fullmatch(r"[A-Za-z0-9_-]{1,64}",profile["responseSchemaName"]) is not None,"schema-name")
    roots=profile["installedRoots"]; files=profile["installedFiles"]
    require(type(roots) is list and 1<=len(roots)<=4 and all(path(root) for root in roots) and len(set(roots))==len(roots),"roots")
    require(not any(below(a,b) for a in roots for b in roots if a!=b),"root-overlap")
    require(type(files) is list and 1<=len(files)<=512,"file-bound")
    roles={}; previous=""; total=0
    expected={"host":"FS.GG.Telemetry.Host.dll","client":"FS.GG.Telemetry.Client.dll","core":"FS.GG.Coord.Core.dll","store":"FS.GG.Telemetry.Store.dll"}
    for row in files:
        exact(row,("path","bytes","sha256","components"))
        require(path(row["path"]) and row["path"]>previous and any(below(root,row["path"]) for root in roots),"file-path-order")
        previous=row["path"]; require(integer(row["bytes"]) and row["bytes"]>0 and digest(row["sha256"]),"file-row")
        total+=row["bytes"]; require(total<=200*1024*1024,"file-byte-bound")
        require(type(row["components"]) is list and len(row["components"])<=4,"components")
        for role in row["components"]:
            require(role in expected and role not in roles and row["path"].split("/")[-1]==expected[role],"component-role")
            roles[role]=row["path"]
    require(set(roles)==set(expected),"component-roster")
    return profile

def build_profile(instructions_sha256, schema_sha256, schema_name, verifier_module_sha256,
                  verifier_runtime_manifest_sha256, installed_roots, managed_files_raw):
    """Compose fixed profile fields around the exact managed inventory declaration.

    The caller binds physical bytes independently. This does not install anything.
    """
    files=parse(managed_files_raw)
    value={"schema":"fsgg.telemetry.responses-capability-profile/1","sourceVariant":"openai-responses/1",
        "provider":"openai","model":"gpt-6.1-sol","effort":"medium",
        "countEndpoint":"https://api.openai.com/v1/responses/input_tokens",
        "generationEndpoint":"https://api.openai.com/v1/responses","inputTokenLimit":8000,
        "outputTokenLimit":1500,"wholeMilliseconds":60000,"networkMilliseconds":55000,
        "requestPolicySha256":POLICY,"instructionsSha256":instructions_sha256,
        "responseSchemaSha256":schema_sha256,"responseSchemaName":schema_name,
        "verifierModuleSha256":verifier_module_sha256,
        "verifierRuntimeManifestSha256":verifier_runtime_manifest_sha256,
        "installedRoots":installed_roots}
    # Keep the managed serializer fragment verbatim; default JSON encoders differ
    # for HTML-sensitive and nonASCII paths even when parsed values agree.
    raw=encode(value)[:-1]+b',"installedFiles":'+managed_files_raw+b'}'
    validate_profile(raw)
    require(parse(property_bytes(raw,"installedFiles"))==files,"managed-files-codec")
    return raw

def profile_artifact(raw, managed_files_raw):
    """Validate a managed-produced profile and its exact serializer receipt, return clones."""
    validate_profile(raw)
    require(type(managed_files_raw) is bytes and property_bytes(raw,"installedFiles")==managed_files_raw,"managed-files-codec")
    return bytes(raw),sha(managed_files_raw)

def trx_cases(raw):
    require(type(raw) is bytes and 0<len(raw)<=8*1024*1024 and b"<!DOCTYPE" not in raw and b"<!ENTITY" not in raw,"trx-bound")
    try: root=ET.fromstring(raw)
    except ET.ParseError as error: raise Refusal("malformed-trx") from error
    ns="{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
    require(root.tag==ns+"TestRun","trx-schema")
    definitions={}; executions=set(); identities=set(); displays=set(); result=[]
    for row in root.findall("./"+ns+"TestDefinitions/"+ns+"UnitTest"):
        method=row.find(ns+"TestMethod"); execution=row.find(ns+"Execution")
        require(method is not None and execution is not None and row.get("id") not in definitions,"trx-definition")
        definitions[row.get("id")]=(method.get("className"),method.get("name"),execution.get("id"))
    for row in root.findall("./"+ns+"Results/"+ns+"UnitTestResult"):
        identity=row.get("testId"); execution=row.get("executionId"); display=row.get("testName")
        require(identity in definitions and identity not in identities and execution not in executions and display not in displays,"trx-duplicate-or-detached")
        require(definitions[identity][2]==execution and row.get("outcome")=="Passed","trx-not-passed")
        identities.add(identity); executions.add(execution); displays.add(display)
        result.append((*definitions[identity][:2],display))
    counters=root.find("./"+ns+"ResultSummary/"+ns+"Counters")
    require(counters is not None and len(result)>0 and len(result)<=512,"trx-count")
    require(all(int(counters.get(k,"-1"))==len(result) for k in ("total","executed","passed")) and int(counters.get("failed","-1"))==0,"trx-count")
    require(set(definitions)==identities,"trx-unexecuted")
    return result

def metadata_evidence(raw, profile_raw, attempt_id, scenario):
    value=exact(parse(raw), ("schema","attemptId","scenario","profileSha256","installedFilesSha256","loadedComponents","managedInstalledFilesBase64","checks"))
    require(value["schema"]=="fsgg.telemetry.responses-static-metadata-evidence/1" and value["attemptId"]==attempt_id and value["scenario"]==scenario and value["profileSha256"]==sha(profile_raw),"metadata-binding")
    try: files=base64.b64decode(value["managedInstalledFilesBase64"],validate=True)
    except (ValueError,TypeError) as error: raise Refusal("metadata-codec") from error
    _,files_sha=profile_artifact(profile_raw,files)
    require(value["installedFilesSha256"]==files_sha,"metadata-codec")
    profile=validate_profile(profile_raw)
    roles={role:row["path"] for row in profile["installedFiles"] for role in row["components"]}
    require(value["loadedComponents"]==roles,"loaded-components")
    required=("physicalInventoryExact","immutableFilesVerified","assemblyNamesVerified","loadedLocationsVerified") if scenario=="current-installed-closure" else ("distinctReferences","providerNotIngestionFile","positiveRoles","aliasRefused")
    require(type(value["checks"]) is dict and set(value["checks"])==set(required) and all(value["checks"][k] is True for k in required),"metadata-checks")

def verifier_evidence(raw, profile_raw, attempt_id, scenario):
    value=exact(parse(raw,2*1024*1024),("schema","attemptId","scenario","profileSha256","verifierModuleSha256","captureBase64","snapshotBase64","resultBase64"))
    profile=validate_profile(profile_raw)
    require(value["schema"]=="fsgg.telemetry.responses-static-verifier-evidence/1" and value["attemptId"]==attempt_id and value["scenario"]==scenario and value["profileSha256"]==sha(profile_raw) and value["verifierModuleSha256"]==profile["verifierModuleSha256"],"verifier-binding")
    try: capture,snapshot,result=(base64.b64decode(value[k],validate=True) for k in ("captureBase64","snapshotBase64","resultBase64"))
    except (ValueError,TypeError) as error: raise Refusal("verifier-bytes") from error
    c=parse(capture,1100000); s=parse(snapshot,400000); r=parse(result,65536)
    require(c.get("invocationId")=="synthetic-invocation" and c.get("originalItemId")=="synthetic-item" and s.get("installedProfileSha256")==sha(profile_raw),"synthetic-fixture-binding")
    require(r.get("schema")=="fsgg.telemetry.responses-verification/1" and r.get("captureSha256")==sha(capture) and r.get("snapshotSha256")==sha(snapshot),"verifier-result-binding")
    if scenario=="verifier-valid-capture": require(r.get("profileSha256")==sha(profile_raw) and r.get("accepted") is True and r.get("observationVerified") is True and r.get("errors")==[],"verifier-positive")
    else:
        code="request-policy-mismatch" if scenario=="verifier-mutated-capture-refused" else "binding-mismatch"
        require(r.get("accepted") is False and r.get("observationVerified") is False and code in r.get("errors",[]),"verifier-negative")

def assemble(profile_raw, managed_files_raw, selection_raw, terminal_raw, receipts):
    """Finalizer inside existing owner's original deadline, after all child settlement.

    No signature/authenticity follows from supplied JSON. The root-selected custody
    owner must retain these exact bytes and current physical closure observations.
    """
    _,files_sha=profile_artifact(profile_raw,managed_files_raw)
    profile=validate_profile(profile_raw)
    binding=exact(parse(selection_raw),("schema","attemptId","profileSha256","installedFilesSha256","sourceHead","consumerSha256","scenarioKinds","originalWholeMilliseconds"))
    require(binding["schema"]=="fsgg.telemetry.responses-static-selection/1" and isinstance(binding["attemptId"],str) and re.fullmatch(r"[A-Za-z0-9_-]{1,128}",binding["attemptId"]) is not None,"selection")
    require(binding["profileSha256"]==sha(profile_raw) and binding["installedFilesSha256"]==files_sha and re.fullmatch(r"[0-9a-f]{40}",binding["sourceHead"]) and digest(binding["consumerSha256"]),"selection-binding")
    kinds={name:("trx+metadata" if name in ("credential-role-separation","current-installed-closure") else "trx" if name in METHODS else "verifier") for name in SCENARIOS}
    require(binding["scenarioKinds"]==kinds,"selected-roster")
    whole=binding["originalWholeMilliseconds"]
    require(integer(whole) and 0<whole<=60000,"original-whole")
    terminal=parse(terminal_raw,16*1024*1024)
    # These fields come from the existing custody owner, not an injected pass flag.
    required_terminal=set("qualified ownedCustodyClean resourceFailed storageFailed actualRuntimeEvidenceFailed cleanupFailure stopCause attemptId sourceHead originalWholeMilliseconds operationSpecificCaptureProduced elapsedSeconds installedFilesBeforeSha256 installedFilesAfterSha256".split())
    require(type(terminal) is dict and required_terminal <= set(terminal),"terminal-incomplete")
    require(terminal["installedFilesBeforeSha256"]==files_sha and terminal["installedFilesAfterSha256"]==files_sha,"physical-closure-changed")
    require(terminal.get("qualified") is True and terminal.get("ownedCustodyClean") is True and terminal.get("resourceFailed") is False and terminal.get("storageFailed") is False and terminal.get("actualRuntimeEvidenceFailed") is False and terminal.get("cleanupFailure") is None and terminal.get("stopCause") is None,"custody-not-settled")
    require(terminal.get("attemptId")==binding["attemptId"] and terminal.get("sourceHead")==binding["sourceHead"] and terminal.get("originalWholeMilliseconds")==whole and terminal.get("operationSpecificCaptureProduced") is False,"terminal-binding")
    elapsed=terminal.get("elapsedSeconds")
    require(type(elapsed) in (int,float) and math.isfinite(elapsed) and 0<=elapsed*1000<whole,"original-deadline")
    require(type(receipts) is dict and set(receipts)==set(SCENARIOS),"receipt-roster")
    rows=[]
    for scenario in SCENARIOS:
        receipt=exact(parse(receipts[scenario],12*1024*1024),("schema","attemptId","scenario","profileSha256","consumerSha256","artifactBase64","metadataBase64"))
        require(receipt["schema"]=="fsgg.telemetry.responses-static-scenario-receipt/1" and receipt["attemptId"]==binding["attemptId"] and receipt["scenario"]==scenario and receipt["profileSha256"]==sha(profile_raw) and receipt["consumerSha256"]==binding["consumerSha256"],"receipt-binding")
        try: artifact=base64.b64decode(receipt["artifactBase64"],validate=True)
        except (ValueError,TypeError) as error: raise Refusal("receipt-bytes") from error
        if scenario in METHODS:
            cls,method,count=METHODS[scenario]; cases=trx_cases(artifact)
            require(len([row for row in cases if row[:2]==(cls,method)])==count,"selected-case-roster")
            if scenario=="request-policy-caps":
                require(len([row for row in cases if row[:2]==(cls,"count boundary and exact retained byte joins are accepted purely")])==1,"selected-cap-boundary")
            if kinds[scenario]=="trx+metadata":
                try: metadata=base64.b64decode(receipt["metadataBase64"],validate=True)
                except (ValueError,TypeError) as error: raise Refusal("metadata-bytes") from error
                metadata_evidence(metadata,profile_raw,binding["attemptId"],scenario)
            else: require(receipt["metadataBase64"] is None,"unexpected-metadata")
        else:
            require(receipt["metadataBase64"] is None,"unexpected-metadata")
            verifier_evidence(artifact,profile_raw,binding["attemptId"],scenario)
        rows.append({"name":scenario,"outcome":"pass","evidenceSha256":sha(receipts[scenario])})
    result={"schema":"fsgg.telemetry.responses-static-qualification/1","sourceVariant":"openai-responses/1","profileSha256":sha(profile_raw),"verifierRuntimeManifestSha256":profile["verifierRuntimeManifestSha256"],"verifierModuleSha256":profile["verifierModuleSha256"],"installedFilesSha256":files_sha,"scenarioResults":rows,"ownedCustodyClean":True,"resourceFailed":False,"elapsedMilliseconds":math.ceil(elapsed*1000),"originalWholeMilliseconds":whole,"operationSpecificCaptureProduced":False}
    require(result["elapsedMilliseconds"]<whole,"rounded-original-deadline")
    raw=encode(result); require(len(raw)<=65536,"result-byte-bound"); return raw
