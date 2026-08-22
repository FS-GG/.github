#!/usr/bin/env python3
"""Contract tests for typed roadmap-health derivation."""
from __future__ import annotations
import importlib.util,json,subprocess,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]; SCRIPT=ROOT/"scripts/report-roadmap-health.py"; FIXTURE=ROOT/"tests/FS.GG.Coord.Core.Tests/fixtures/roadmap-health/roadmap-8813c463.json"
spec=importlib.util.spec_from_file_location("roadmap_health",SCRIPT); assert spec and spec.loader
module=importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
def main():
 source=module.read_fixture(FIXTURE); reading=module.report(source)
 assert [x["id"] for x in reading["measures"]]==list(module.IDS)
 assert [x["verdict"] for x in reading["measures"]]==["violated","retired","violated","violated","violated","violated","met"]
 cli=subprocess.run(["python3",str(SCRIPT),"--fixture",str(FIXTURE),"--format","json"],text=True,capture_output=True,check=True); assert json.loads(cli.stdout)==reading
 three=json.loads(json.dumps(source))
 for p in three["measures"]["issue-flow"]["periods"]: p["opened"],p["closed"]=1,2
 assert module.report(three)["measures"][0]["verdict"]=="met"
 with tempfile.TemporaryDirectory() as t:
  bad=Path(t)/"bad.json"; invalid=json.loads(json.dumps(source)); invalid["measures"]["issue-flow"]["periods"][1]["start"]="2026-08-09T00:00:00Z"; bad.write_text(json.dumps(invalid))
  try: module.read_fixture(bad)
  except ValueError as e: assert "period" in str(e)
  else: raise AssertionError("noncontiguous periods must fail closed")
 print("report-roadmap-health: typed seven-measure derivation and invalid windows hold")
if __name__=="__main__": main()
