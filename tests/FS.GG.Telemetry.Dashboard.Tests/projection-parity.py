#!/usr/bin/env python3
"""Pin only semantics shared with the retained publisher; this is not DTO equality."""
import importlib.util
import pathlib
import unittest

ROOT=pathlib.Path(__file__).resolve().parents[2]
SPEC=importlib.util.spec_from_file_location("retained_dashboard",ROOT/"tools"/"telemetry-dashboard.py")
D=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(D)

class ProjectionParity(unittest.TestCase):
    def test_ci_coverage_uses_population_values_then_legacy_fallback(self):
        item="item"
        snapshot={name:[] for name in ("ciRuns","ciJobs","ciSteps","ciCoverage","ciPopulationCoverage")}
        snapshot["ciCoverage"]=[{"item_id":item,"inventory":"partial","attempts":"unknown","job_pages":"partial","terminal":"unknown","timestamps":"complete","lineage":"unknown","classification":"unknown","critical_path":"unknown"}]
        snapshot["ciPopulationCoverage"]=[{"item_id":item,"actions":"complete","checks":"partial","attempts":"partial","jobs":"complete","terminal":"unknown","timestamps":"complete"}]
        projected=D.snapshot_ci(snapshot,item)
        self.assertEqual({"inventoryCoverage":"complete","checkCoverage":"partial","attemptCoverage":"partial","jobPageCoverage":"complete","terminalCoverage":"unknown","timestampCoverage":"complete"},{key:projected[key] for key in ("inventoryCoverage","checkCoverage","attemptCoverage","jobPageCoverage","terminalCoverage","timestampCoverage")})

    def test_missing_population_coverage_preserves_legacy_and_unknown(self):
        item="item"
        snapshot={name:[] for name in ("ciRuns","ciJobs","ciSteps","ciCoverage","ciPopulationCoverage")}
        snapshot["ciCoverage"]=[{"item_id":item,"inventory":"partial","attempts":"unknown","job_pages":"partial","terminal":"unknown","timestamps":"complete","lineage":"unknown","classification":"unknown","critical_path":"unknown"}]
        projected=D.snapshot_ci(snapshot,item)
        self.assertEqual("partial",projected["inventoryCoverage"])
        self.assertEqual("unknown",projected["checkCoverage"])
        self.assertEqual("partial",projected["jobPageCoverage"])

if __name__=="__main__":unittest.main()
