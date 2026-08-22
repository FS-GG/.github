#!/usr/bin/env bash
set -euo pipefail

# CI adapter for release-saga.py. Feed credentials remain in the package-owning workflows; this
# adapter makes their remote-before-push, durable journal, and complete-org-set barrier identical.
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tool="$root/scripts/release-saga.py"
command="${1:?command}"; package="${2:?package}"; version="${3:?version}"; source_sha="${4:?source sha}"
release_tag="coherent-set/v$version"
manifest="artifacts/packages/release-manifest.json"
journal="artifacts/packages/journal-$package.json"

github_base() {
  curl -fsSL -u "$GITHUB_ACTOR:$GH_TOKEN" https://nuget.pkg.github.com/FS-GG/index.json \
    | jq -r '.resources[] | select(."@type" == "PackageBaseAddress/3.0.0") | ."@id"' | head -1
}

package_url() {
  local feed="$1" id="$2" lower
  lower="$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')"
  if [ "$feed" = github ]; then printf '%s/%s/%s/%s.%s.nupkg' "$(github_base | sed 's:/*$::')" "$lower" "$version" "$lower" "$version"
  else printf 'https://api.nuget.org/v3-flatcontainer/%s/%s/%s.%s.nupkg' "$lower" "$version" "$lower" "$version"; fi
}

download() {
  local feed="$1" id="$2" target="$3" url code
  url="$(package_url "$feed" "$id")"
  if [ "$feed" = github ]; then
    code="$(curl -sS -L -u "$GITHUB_ACTOR:$GH_TOKEN" -o "$target" -w '%{http_code}' "$url")"
  else
    code="$(curl -sS -L -o "$target" -w '%{http_code}' "$url")"
  fi
  case "$code" in 200) return 0;; 404) return 4;; *) echo "feed download returned HTTP $code: $url" >&2; return 1;; esac
}

upload_journal() {
  local immutable prior remote_journal
  immutable="$(gh release view "$release_tag" --repo "$GITHUB_REPOSITORY" \
    --json isImmutable --jq '.isImmutable')"
  case "$immutable" in
    false)
      gh release upload "$release_tag" --repo "$GITHUB_REPOSITORY" --clobber "$journal"
      ;;
    true)
      # Promotion makes release assets immutable. An exact-source replay must therefore read and
      # validate the already-persisted package journal rather than trying an impossible clobber that
      # aborts the workflow before receiver dashboard delivery. Keep the temporary manifest beside
      # the prepared artifacts so release-saga.py resolves its descriptor-relative package paths
      # against the same bytes as the current run.
      prior="$(mktemp -d "${RUNNER_TEMP:-/tmp}/immutable-release-journal.XXXXXX")"
      remote_journal="$(mktemp "$(dirname "$manifest")/.immutable-journal.XXXXXX.json")"
      if ! gh release download "$release_tag" --repo "$GITHUB_REPOSITORY" \
        --pattern "journal-$package.json" --dir "$prior"; then
        rm -rf "$prior"; rm -f "$remote_journal"; return 1
      fi
      if ! cp "$prior/journal-$package.json" "$remote_journal"; then
        rm -rf "$prior"; rm -f "$remote_journal"; return 1
      fi
      rm -rf "$prior"
      if ! python3 "$tool" assert-identity --manifest "$remote_journal" \
        --release-id "github:$version" --version "$version" --source-sha "$source_sha" \
        --policy-version release-saga/1 \
        || ! python3 "$tool" assert-artifacts --manifest "$remote_journal" \
        || ! jq -e --arg package "$package" '
          .state.feeds.github.packages[$package].state == "verified" and
          .state.feeds.nuget.packages[$package].state == "verified"
        ' "$remote_journal" >/dev/null; then
        rm -f "$remote_journal"; return 1
      fi
      rm -f "$remote_journal"
      echo "immutable release journal read back and verified for $package $version"
      ;;
    *)
      echo "release $release_tag returned unreadable isImmutable=$immutable" >&2
      return 1
      ;;
  esac
}

case "$command" in
  init)
    python3 "$tool" assert-identity --manifest "$manifest" --release-id "github:$version" \
      --version "$version" --source-sha "$source_sha" --policy-version release-saga/1
    python3 "$tool" assert-artifacts --manifest "$manifest"
    prior="$(mktemp -d "${RUNNER_TEMP:-/tmp}/release-journal.XXXXXX")"
    if gh release download "$release_tag" --repo "$GITHUB_REPOSITORY" \
      --pattern "journal-$package.json" --dir "$prior" >/dev/null 2>&1; then
      cp "$prior/journal-$package.json" "$journal"
      python3 "$tool" assert-identity --manifest "$journal" --release-id "github:$version" \
        --version "$version" --source-sha "$source_sha" --policy-version release-saga/1
      python3 "$tool" assert-artifacts --manifest "$journal"
    else
      cp "$manifest" "$journal"
    fi
    ;;
  github)
    echo github > "${RUNNER_TEMP:-/tmp}/release-saga-stage"
    mkdir -p artifacts/observed/github
    own="artifacts/observed/github/$package.$version.nupkg"
    if download github "$package" "$own"; then
      python3 "$tool" record-observed --manifest "$journal" --feed github \
        --observed "$package=$own" --detail "pre-push observation; existing immutable version"
    else
      rc=$?
      [ "$rc" -eq 4 ] || exit "$rc"
      dotnet nuget push "artifacts/packages/$package.$version.nupkg" \
        --source https://nuget.pkg.github.com/FS-GG/index.json --api-key "$GH_TOKEN"
      for _ in $(seq 1 24); do
        if download github "$package" "$own"; then break; else rc=$?; [ "$rc" -eq 4 ] || exit "$rc"; sleep 5; fi
      done
      test -s "$own"
      python3 "$tool" record-observed --manifest "$journal" --feed github \
        --observed "$package=$own" --detail "post-push external observation"
    fi
    upload_journal

    # Complete-set barrier: no package-owning workflow may request a NuGet token until every exact
    # package is externally served by GitHub Packages.
    for _ in $(seq 1 60); do
      ready=true; observations=()
      for id in FS.GG.Coord.Cli FS.GG.Kit FS.GG.Drivers; do
        target="artifacts/observed/github/$id.$version.nupkg"
        if download github "$id" "$target"; then :; else rc=$?; [ "$rc" -eq 4 ] || exit "$rc"; ready=false; break; fi
        observations+=(--observed "$id=$target")
      done
      if [ "$ready" = true ]; then
        python3 "$tool" record-observed --manifest "$journal" --feed github \
          "${observations[@]}" --detail "complete coherent-set org barrier"
        upload_journal
        exit 0
      fi
      sleep 10
    done
    echo "complete GitHub Packages set did not become observable" >&2; exit 1
    ;;
  nuget-probe)
    echo nuget > "${RUNNER_TEMP:-/tmp}/release-saga-stage"
    mkdir -p artifacts/observed/nuget
    target="artifacts/observed/nuget/$package.$version.nupkg"
    if download nuget "$package" "$target"; then
      python3 "$tool" record-observed --manifest "$journal" --feed nuget \
        --observed "$package=$target" --detail "pre-push observation; existing immutable version"
      upload_journal
      echo "push=false" >> "$GITHUB_OUTPUT"
    else
      rc=$?; [ "$rc" -eq 4 ] || exit "$rc"
      # A preceding attempt may have received success from nuget.org's push endpoint while the
      # immutable version is still being validated/indexed. Its durable failure journal proves the
      # write was attempted; never issue a blind duplicate push. Resume by observing until the exact
      # payload appears, or fail durably again without changing external state.
      if jq -e --arg package "$package" \
        '.state.recovery.lastFailure.feed == "nuget" and .state.recovery.lastFailure.package == $package' \
        "$journal" >/dev/null; then
        observed=false
        for _ in $(seq 1 60); do
          if download nuget "$package" "$target"; then observed=true; break
          else rc=$?; [ "$rc" -eq 4 ] || exit "$rc"; sleep 10; fi
        done
        [ "$observed" = true ] || { echo "nuget.org accepted the prior push but has not served it yet" >&2; exit 1; }
        python3 "$tool" record-observed --manifest "$journal" --feed nuget \
          --observed "$package=$target" --detail "resumed external observation after accepted push"
        upload_journal
        echo "push=false" >> "$GITHUB_OUTPUT"
      else
        echo "push=true" >> "$GITHUB_OUTPUT"
      fi
    fi
    ;;
  nuget-record)
    target="artifacts/observed/nuget/$package.$version.nupkg"
    observed=false
    for _ in $(seq 1 24); do
      if download nuget "$package" "$target"; then observed=true; break
      else rc=$?; [ "$rc" -eq 4 ] || exit "$rc"; sleep 5; fi
    done
    [ "$observed" = true ] || { echo "nuget.org accepted the push but has not served it yet" >&2; exit 1; }
    python3 "$tool" record-observed --manifest "$journal" --feed nuget \
      --observed "$package=$target" --detail "post-push external observation"
    upload_journal
    ;;
  failure)
    feed="$(cat "${RUNNER_TEMP:-/tmp}/release-saga-stage" 2>/dev/null || printf github)"
    if [ -f "$journal" ]; then
      python3 "$tool" record-failure --manifest "$journal" --feed "$feed" --package "$package" \
        --detail "workflow $GITHUB_SERVER_URL/$GITHUB_REPOSITORY/actions/runs/$GITHUB_RUN_ID failed"
      upload_journal
    fi
    ;;
  *) echo "unknown command: $command" >&2; exit 2;;
esac
