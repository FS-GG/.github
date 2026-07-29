#!/usr/bin/env bash
# The failure legs of scripts/dashboard-tick.py — see fixture.py's header for what each one proves.
#
# Hermetic: a fake GitHub API on localhost, no credential, no network. The subject is the real
# script and the real registry/repos.yml, driven as subprocesses.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."
exec python3 tests/dashboard-tick/fixture.py
