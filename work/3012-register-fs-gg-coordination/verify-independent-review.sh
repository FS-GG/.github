#!/usr/bin/env bash
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
out="${1:-$root/work/3012-register-fs-gg-coordination/test-results/independent-review.junit.xml}"
comment_id=5426876370
reviewed_head=b3b07078b14c8dda05616fe6cdb71697149c83d9
review_digest=4614ec49f2aceb212290d69ea1b6c8a030447c009c24fb8b47849d912ee612d5
tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT

cd "$root"
gh api "repos/FS-GG/.github/issues/comments/$comment_id" > "$tmp_dir/comment.json"
jq -e '.created_at == .updated_at and (.author_association == "OWNER" or .author_association == "MEMBER" or .author_association == "COLLABORATOR")' "$tmp_dir/comment.json" >/dev/null
jq -r '.body' "$tmp_dir/comment.json" | tail -n +2 > "$tmp_dir/decision.json"
jq -e --arg head "$reviewed_head" --arg digest "$review_digest" '
  .schema == "fsgg.coord.review-decision/v2" and
  .kind == "confirmation" and
  .verdict == "pass" and
  .revision == 2 and
  .headSha == $head and
  .digest == $digest and
  .critic == "plover-d91f" and
  .subject == "FS-GG/.github#3012/pr/3013"
' "$tmp_dir/decision.json" >/dev/null

scripts/project-field-options verify-snapshot \
  --snapshot work/3012-register-fs-gg-coordination/project-migration-before.json >/dev/null
scripts/project-field-options verify-snapshot \
  --snapshot work/3012-register-fs-gg-coordination/project-migration-after.json >/dev/null
test "$(jq '[.items[] | select(.repoScope != null)] | length' work/3012-register-fs-gg-coordination/project-migration-before.json)" = 86
test "$(jq '[.items[] | select(.repoScope == null)] | length' work/3012-register-fs-gg-coordination/project-migration-before.json)" = 235
diff -u \
  <(jq -S '.items' work/3012-register-fs-gg-coordination/project-migration-before.json) \
  <(jq -S '.items' work/3012-register-fs-gg-coordination/project-migration-after.json) >/dev/null

test "$(sha256sum docs/2026-08-26-fs-gg-coordination-admin-settings-report.md | cut -d' ' -f1)" = \
  6bbb303da20bab8b3be50dd1a170fc90c9f1931e425f70516393dbc177baf032
python3 work/2953-gh-modernization-m0-invariants/validate_q0.py \
  work/2953-gh-modernization-m0-invariants/q0-evidence.json --self-test >/dev/null

mkdir -p "$(dirname "$out")"
cat > "$out" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<testsuite name="GS2-01.2 independent review receipt" tests="6" failures="0" errors="0" skipped="0">
  <testcase classname="review" name="structured successor decision is live, unedited, and authorized" />
  <testcase classname="review" name="successor decision passes the exact repaired head" />
  <testcase classname="project-migration" name="before and after snapshots are complete and integrity sealed" />
  <testcase classname="project-migration" name="321 item states preserve 86 assigned and 235 unassigned values" />
  <testcase classname="project-migration" name="before and after item-state arrays are identical" />
  <testcase classname="q0" name="frozen administrator report and complete Q0 mutation self-test remain green" />
</testsuite>
XML

printf 'GS2-01.2-REVIEW-RECEIPT-GREEN: 6/6\n'
