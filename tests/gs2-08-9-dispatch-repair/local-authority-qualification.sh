#!/usr/bin/env bash
set -euo pipefail

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
git init --bare "$work/authority.git" >/dev/null
mkdir -p "$work/authority.git/hooks"
cat > "$work/authority.git/hooks/pre-receive" <<'HOOK'
#!/usr/bin/env bash
set -euo pipefail
while read -r old new ref; do
  [ "$new" != "0000000000000000000000000000000000000000" ] || exit 1
  if [ "$old" != "0000000000000000000000000000000000000000" ]; then
    git merge-base --is-ancestor "$old" "$new" || exit 1
  fi
done
HOOK
chmod +x "$work/authority.git/hooks/pre-receive"
git clone "$work/authority.git" "$work/work" >/dev/null 2>&1
cd "$work/work"
git config user.name qualification
git config user.email qualification@example.invalid
echo first > journal
git add journal
git commit -m first >/dev/null
first="$(git rev-parse HEAD)"
git push origin "$first:refs/heads/authority" >/dev/null
echo second >> journal
git commit -am second >/dev/null
second="$(git rev-parse HEAD)"
git push --force-with-lease=refs/heads/authority:"$first" origin "$second:refs/heads/authority" >/dev/null
if git push --force-with-lease=refs/heads/authority:"$first" origin "$first:refs/heads/authority" >/dev/null 2>&1; then
  echo "stale lease unexpectedly replaced the authority ref" >&2
  exit 1
fi
if git push origin :refs/heads/authority >/dev/null 2>&1; then
  echo "authority ref deletion unexpectedly succeeded" >&2
  exit 1
fi
test "$(git --git-dir="$work/authority.git" rev-parse refs/heads/authority)" = "$second"
echo "local authority qualification: CAS and immutable-ref refusals passed"
