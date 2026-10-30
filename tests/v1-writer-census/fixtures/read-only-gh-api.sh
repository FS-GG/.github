#!/usr/bin/env bash
# Fixture: reads only.
number="$(gh api repos/FS-GG/FS.GG.SDD/pulls/1 --jq .number)" && echo "$number"
(gh api repos/FS-GG/FS.GG.SDD/pulls/1); grep -f patterns file
gh api --paginate repos/FS-GG/FS.GG.SDD/pulls --jq '.[] | .number' | sort -f
