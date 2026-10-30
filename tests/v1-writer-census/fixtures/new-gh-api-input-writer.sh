#!/usr/bin/env bash
# Fixture: request body from stdin.
gh api repos/FS-GG/FS.GG.SDD/issues --input - < payload.json
