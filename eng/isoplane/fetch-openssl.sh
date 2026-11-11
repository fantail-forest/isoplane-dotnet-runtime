#!/usr/bin/env bash
# Download the WASI OpenSSL libraries the OpenSSL fork built for the pinned
# revision: fetch-openssl.sh OUTPUT_DIR
# Uses the fork's release for that revision when one exists, and otherwise the
# artifact of the fork's build of it, which needs a signed-in GitHub CLI.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ "$#" != 1 ]; then
  echo "Usage: $0 OUTPUT_DIR" >&2
  exit 2
fi
source "$HERE/manifest.env"
fork=fantail-forest/isoplane-openssl
revision=$OPENSSL_REVISION
release="https://github.com/$fork/releases/download/isoplane-wasi-$revision"

# Prints "built|building|failed RUN_ID RUN_URL" for the fork's build of the
# revision, or "missing". A pull request run builds a merge commit, so it is
# not a build of the revision.
fork_build() {
  gh api "repos/$fork/actions/runs?head_sha=$revision&per_page=100" --jq '
    [.workflow_runs[] | select(.path == ".github/workflows/isoplane.yml" and .event != "pull_request")]
    | (map(select(.conclusion == "success")) | first) as $built
    | (map(select(.status != "completed")) | first) as $building
    | if $built then "built \($built.id) \($built.html_url)"
      elif $building then "building \($building.id) \($building.html_url)"
      elif length > 0 then "failed \(.[0].id) \(.[0].html_url)"
      else "missing" end'
}

download_release() {
  echo "Downloading the OpenSSL release for $revision"
  curl --fail --location --retry 3 "$release/openssl-wasi.tar.gz" -o openssl-wasi.tar.gz
  curl --fail --location --retry 3 "$release/SHA256SUMS" -o SHA256SUMS
}

download_build() {
  local build state run url
  build=$(fork_build)
  read -r state run url <<<"$build"
  if [ "$state" = building ]; then
    echo "Waiting for the fork to finish building $revision: $url"
    gh run watch "$run" --repo "$fork" --interval 60 >/dev/null
    build=$(fork_build)
    read -r state run url <<<"$build"
  fi
  case "$state" in
    built) ;;
    building)
      echo "The fork is still building $revision: $url" >&2
      exit 1 ;;
    failed)
      echo "The fork's build of $revision did not succeed: $url" >&2
      exit 1 ;;
    missing)
      echo "$fork has no release and no build for $revision. Push the revision to an isoplane/** branch of the fork." >&2
      exit 1 ;;
    *)
      echo "Unexpected answer looking up the fork's build of $revision: $build" >&2
      exit 1 ;;
  esac
  echo "Downloading the OpenSSL libraries built by $url"
  if ! gh run download "$run" --repo "$fork" --name openssl-wasi --dir "$download"; then
    echo "The artifact of $url could not be downloaded. The fork keeps artifacts for 90 days; push a branch at $revision to rebuild an expired one." >&2
    exit 1
  fi
}

mkdir -p "$1"
output=$(realpath "$1")
download=$(mktemp -d)
trap 'rm -rf "$download"' EXIT
cd "$download"

status=$(curl --silent --head --location --retry 3 --output /dev/null --write-out '%{http_code}' "$release/SHA256SUMS")
case "$status" in
  200) download_release ;;
  404) download_build ;;
  *)
    echo "Checking for a release at $release returned HTTP $status." >&2
    exit 1 ;;
esac
sha256sum --check SHA256SUMS
tar -xzf openssl-wasi.tar.gz -C "$output"
jq -e --arg revision "$revision" --arg sdk "$WASI_SDK_VERSION" \
  '.source_revision == $revision and (.wasi_sdk | split("\n")[0]) == $sdk' "$output/source.json" >/dev/null
