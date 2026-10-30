#!/usr/bin/env bash
# Fixture: implicit POST. A field flag with no method flag sends a POST.
gh api repos/FS-GG/FS.GG.SDD/issues -f title=t -f body=b
