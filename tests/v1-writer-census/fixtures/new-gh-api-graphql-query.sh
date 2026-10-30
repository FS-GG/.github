#!/usr/bin/env bash
# Fixture: a GraphQL query document is sent as a POST field.
gh api graphql -f query='{viewer{login}}'
