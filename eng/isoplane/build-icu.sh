#!/usr/bin/env bash
# Prepare ICU data and WASI native inputs: build-icu.sh ICU_SOURCE WORK_DIR OUTPUT_DIR
set -euo pipefail
if [ "$#" != 3 ]; then
  echo "Usage: $0 ICU_SOURCE WORK_DIR OUTPUT_DIR" >&2
  exit 2
fi
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$HERE/manifest.env"
ICU="$(realpath "$1")"
mkdir -p "$2" "$3"
WORK="$(realpath "$2")"
OUT="$(realpath "$3")"
ICU_BUILD="$WORK/icu-build"
ICU_MONO_NATIVE="$OUT/icu/mono/runtimes/wasi-wasm/native"
DOWNLOADS="$WORK/downloads"
BUILD_HOST=linux
ICU_HOST=Linux
case "$(uname -s)" in MINGW*|MSYS*) BUILD_HOST=windows; ICU_HOST=MinGW ;; esac
mkdir -p "$DOWNLOADS"

fetch() {
  local url="$1" checksum="$2" destination="$3"
  if [ -f "$destination" ] && printf '%s  %s\n' "$checksum" "$destination" | sha256sum --check --status; then
    return
  fi
  curl --fail --location --retry 3 --output "$destination.part" "$url"
  printf '%s  %s\n' "$checksum" "$destination.part" | sha256sum --check
  mv "$destination.part" "$destination"
}

verify_archive() {
  local sha="$1" file="$2"
  printf '%s  %s\n' "$sha" "$file" | sha256sum --check --status || {
    echo "ICU transport does not match its pinned SHA256: $file; rerun ICU preparation to download it again" >&2
    return 1
  }
}

mono_archive="$DOWNLOADS/icu-mono-$ICU_MONO_TRANSPORT_VERSION.nupkg"
fetch "$ICU_MONO_TRANSPORT_URL" "$ICU_MONO_TRANSPORT_SHA256" "$mono_archive"

mkdir -p "$ICU_BUILD"
cd "$ICU_BUILD"
unset ICU_DATA_FILTER_FILE
configure_environment=()
if [ "$BUILD_HOST" = windows ]; then
  configure_environment+=("CXXFLAGS=${CXXFLAGS:--O2} -std=gnu++11")
fi
env "${configure_environment[@]}" "$ICU/icu/icu4c/source/runConfigureICU" "$ICU_HOST" --disable-tests --disable-samples --disable-shared --enable-static --with-data-packaging=archive
make_arguments=()
if [ "$BUILD_HOST" = windows ]; then
  # ICU's MinGW linker rules omit the static-library prefix.
  make_arguments+=(STATIC_PREFIX= STATIC_PREFIX_WHEN_USED=)
fi
make -j"${CMAKE_BUILD_PARALLEL_LEVEL:-$(nproc)}" "${make_arguments[@]}"
[ -f "data/out/$ICU_DATA_FILE" ] || { echo "the ICU build produced no data/out/$ICU_DATA_FILE" >&2; exit 1; }
verify_archive "$ICU_MONO_TRANSPORT_SHA256" "$mono_archive"
rm -rf "$OUT/icu"
mkdir -p "$OUT/icu/mono"
unzip -q "$mono_archive" 'runtimes/wasi-wasm/native/**' -d "$OUT/icu/mono"
cp "data/out/$ICU_DATA_FILE" "$ICU_MONO_NATIVE/lib/icudt.dat"
