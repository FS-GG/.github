#!/usr/bin/env bash
# Selftest for scripts/lib/gate.py — the shared gate harness (.github#1159, decisions in #1158).
#
# The harness ships with no gate consumers yet (they migrate onto it in #1160 and the follow-ups), so
# this fixture is what keeps it honest until then. It is offline and credential-free: selftest.py
# imports the harness the way a real gate does — scripts/ on the path, `from lib.gate import ...` — and
# asserts the ExitCode contract, the couldn't-look path (GateError/Unreachable/crash all become
# no-verdicts, never the FINDING code), and the on:->True normaliser. It needs PyYAML, because one leg
# drives load_yaml.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
export PYTHONDONTWRITEBYTECODE=1

python3 "$HERE/selftest.py"
