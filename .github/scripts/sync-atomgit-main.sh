#!/usr/bin/env bash
set -euo pipefail

repo=${ATOMGIT_REPOSITORY:?Missing AtomGit owner/repository}
[[ "$repo" =~ ^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$ ]] || { echo "Invalid AtomGit repository" >&2; exit 1; }
mirror="https://atomgit.com/$repo.git"
branch=refs/heads/main
source_sha=$(git rev-parse HEAD)
mirror_sha() {
  git ls-remote --exit-code "$mirror" "$branch" | cut -f1
}

before=$(mirror_sha)
if [[ "$before" != "$source_sha" ]]; then
  if ! git merge-base --is-ancestor "$before" "$source_sha"; then
    echo "AtomGit main ($before) is outside the checked out history ($source_sha). Check both branches before retrying." >&2
    exit 1
  fi
  : "${ATOMGIT_TOKEN:?Set the AtomGit repository access token}"
  git -c http.followRedirects=false -c "credential.username=oauth2" \
    -c credential.helper= \
    -c 'credential.https://atomgit.com.helper=!f() { if test "$1" = get; then printf "password=%s\n" "$ATOMGIT_TOKEN"; fi; }; f' \
    push --no-follow-tags "$mirror" "$source_sha:$branch"
fi

after=$(mirror_sha)
if [[ "$after" != "$source_sha" ]]; then
  echo "Read-back mismatch: expected $source_sha, AtomGit reports $after" >&2
  exit 1
fi
echo "Verified AtomGit $repo main at $after (previously $before)."
