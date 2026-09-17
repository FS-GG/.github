#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
python tests/gs2-08-9-dispatch-repair/run.py
