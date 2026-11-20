#!/usr/bin/env bash
set -euo pipefail

source_repo=${GITHUB_REPOSITORY:?Missing GitHub owner/repository}
mirror_repo=${ATOMGIT_REPOSITORY:-$source_repo}
for repo in "$source_repo" "$mirror_repo"; do
  if [[ ! "$repo" =~ ^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$ || "$repo" =~ (^|/)[.]{1,2}(/|$) ]]; then
    echo 'Invalid repository name; expected owner/repository.' >&2
    exit 1
  fi
done

main_sha() {
  local row sha
  row=$(GIT_TERMINAL_PROMPT=0 git -c credential.helper= -c http.followRedirects=false \
    ls-remote --exit-code "$1" refs/heads/main) || return 1
  sha=${row%%$'\t'*}
  [[ "$sha" =~ ^[a-f0-9]{40}$ && "$row" == "$sha"$'\t'refs/heads/main ]] || return 1
  printf '%s\n' "$sha"
}

source="https://github.com/$source_repo.git"
mirror="https://atomgit.com/$mirror_repo.git"
for attempt in 1 2 3; do
  # Read GitHub on both sides of the mirror lookup, because main can move mid-check.
  before=$(main_sha "$source") && target=$(main_sha "$mirror") && after=$(main_sha "$source") || {
    echo 'Could not read a valid public main reference; source equality is unknown.' >&2
    exit 1
  }
  printf 'Observation %d: GitHub %s -> %s; AtomGit %s\n' "$attempt" "$before" "$after" "$target"
  if [[ "$before" == "$after" && "$after" == "$target" ]]; then
    echo 'Verified identical public main commits (including README and the source tree).'
    exit 0
  fi
  if [[ "$attempt" != 3 ]]; then sleep 2; fi
done
echo 'Public main commits did not settle on the same SHA. Check the AtomGit Pull mirror status and rerun this verification.' >&2
exit 1
