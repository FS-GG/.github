#!/usr/bin/env bash
# Selftest for scripts/lib/gate.py — the shared gate harness (.github#1159, decisions in #1158).
#
# Consumers migrate onto the harness in bounded follow-ups, so this fixture keeps the shared contract
# honest throughout that migration. It is offline and credential-free: selftest.py
# imports the harness the way a real gate does — scripts/ on the path, `from lib.gate import ...` — and
# asserts the ExitCode contract, the couldn't-look path (GateError/Unreachable/crash all become
# no-verdicts, never the FINDING code), and the on:->True normaliser. It needs PyYAML, because one leg
# drives load_yaml.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
export PYTHONDONTWRITEBYTECODE=1

python3 "$HERE/selftest.py"
