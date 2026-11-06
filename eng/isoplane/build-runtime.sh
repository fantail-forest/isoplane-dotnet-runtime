#!/usr/bin/env bash
# Build runtime packages and their feed: build-runtime.sh runtime|feed [...]
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUNTIME="$(cd "$HERE/../.." && pwd)"
source "$HERE/manifest.env"
WORK="${ISOPLANE_RUNTIME_WORK:?Set the runtime build work directory}"
OUT="${ISOPLANE_RUNTIME_OUTPUT:?Set the runtime output directory}"
WASI_SDK="${WASI_SDK_PATH:?Set the pinned WASI SDK directory}"
ICU_MONO_NATIVE="$OUT/icu/mono/runtimes/wasi-wasm/native"
LOGS="$WORK/logs"
BUILD_HOST=linux
case "$(uname -s)" in MINGW*|MSYS*) BUILD_HOST=windows ;; esac
mkdir -p "$LOGS" "$OUT"
if [ "$(head -n 1 "$WASI_SDK/VERSION" | tr -d '\r')" != "$WASI_SDK_VERSION" ]; then
  echo "The runtime packages require WASI SDK $WASI_SDK_VERSION." >&2
  exit 1
fi

require_ninja() {
  local ninja
  ninja="$(command -v ninja || command -v ninja-build)" || { echo "Ninja is missing from PATH; install a working Ninja before building runtime packs" >&2; return 1; }
  "$ninja" --version >/dev/null 2>&1 || { echo "Ninja cannot run at $ninja; select a working Ninja on PATH" >&2; return 1; }
}

# The toolchain a build directory is configured with. CMake and Configure record
# resolved tool paths, so its real location identifies it as well as its version.
toolchain_identity() {
  realpath "$WASI_SDK"
  cat "$WASI_SDK/VERSION"
}

# Discards build directories configured with another toolchain, which an
# incremental build would otherwise keep compiling and archiving with. The
# first directory holds the record of the toolchain they were configured with.
reset_for_toolchain() {
  local stamp="$1/.isoplane-toolchain" directory
  if [ "$(cat "$stamp" 2>/dev/null)" != "$(toolchain_identity)" ]; then
    for directory in "$@"; do rm -rf "$directory"; done
  fi
  mkdir -p "$1"
  toolchain_identity > "$stamp"
}

runtime_version_file="$WORK/pack-version"
stamp_pack_version() {
  local identity="${ISOPLANE_RUNTIME_INPUTS:?Supply the runtime source identity}"
  # NuGet includes the version in its 200-character installed-path check.
  PACK_VERSION_SUFFIX="$PACK_LABEL-${identity:0:32}.$PACK_ITERATION"
  # Arcade needs a calendar build number for assembly/file versions.
  PACK_BUILD_ID="$(git -C "$RUNTIME" log -1 --format=%cs | tr -d '-').1"
  printf '%s\n' "10.0.$PACK_PATCH-$PACK_VERSION_SUFFIX" > "$1"
  echo "    pack version $(cat "$1")"
}
require_build_inputs() {
  require_ninja
  [ -f "$OUT/openssl/lib/libcrypto.a" ] || { echo "Prepare OpenSSL libraries under $OUT/openssl first." >&2; return 1; }
  [ -f "$ICU_MONO_NATIVE/lib/icudt.dat" ] || { echo "Prepare ICU data under $OUT/icu first." >&2; return 1; }
  [ -x "$WASI_SDK/bin/clang" ] || { echo "Install the pinned WASI SDK at $WASI_SDK first." >&2; return 1; }
}
openssl_cmake_args() {
  local root="$OUT/openssl" cache="$WORK/$(basename "$PWD")-openssl.cmake"
  if [ "$BUILD_HOST" = windows ]; then root="$(cygpath -m "$root")"; fi
  cat > "$cache" <<EOF
set(OPENSSL_ROOT_DIR "$root" CACHE PATH "" FORCE)
set(OPENSSL_INCLUDE_DIR "$root/include" CACHE PATH "" FORCE)
set(OPENSSL_CRYPTO_LIBRARY "$root/lib/libcrypto.a" CACHE FILEPATH "" FORCE)
set(OPENSSL_SSL_LIBRARY "$root/lib/libssl.a" CACHE FILEPATH "" FORCE)
set(OPENSSL_USE_STATIC_LIBS TRUE CACHE BOOL "" FORCE)
EOF
  printf '%s' "-C$cache"
}

build_runtime() {
  if [ "$BUILD_HOST" = windows ]; then
    local arguments=() argument
    while [ "$#" -gt 0 ]; do
      argument="$1"
      shift
      if [ "$argument" = -cmakeargs ]; then
        argument="-cmakeargs:$1"
        shift
      fi
      arguments+=("${argument//$HERE/$(cygpath -m "$HERE")}")
    done
    WASI_SDK_PATH="$(cygpath -m "$WASI_SDK")/" MSYS2_ARG_CONV_EXCL='*' \
      powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$PWD/eng/build.ps1")" "${arguments[@]}"
  else
    WASI_SDK_PATH="$WASI_SDK/" ./build.sh "$@"
  fi
}

stage_runtime() {
  echo "==> runtime (log: $LOGS/runtime-build.log)"
  require_build_inputs
  stamp_pack_version "$runtime_version_file"
  reset_for_toolchain "$WORK/runtime-artifacts/obj" "$WORK/runtime-artifacts/bin"
  # Official versioning keeps the upstream assembly versions instead of dev versions.
  (
    mkdir -p "$WORK/runtime-artifacts"
    if [ ! -e "$RUNTIME/artifacts" ]; then
      ln -s "$(realpath --relative-to="$RUNTIME" "$WORK/runtime-artifacts")" "$RUNTIME/artifacts"
    fi
    cd "$RUNTIME"
    DOTNET_CLI_USE_MSBUILD_SERVER=0 \
    build_runtime -os wasi -subset mono+libs+packs -c Release -bl \
      /nodeReuse:false /p:UseSharedCompilation=false \
      /p:OfficialBuild=true "/p:OfficialBuildId=$PACK_BUILD_ID" \
      "/p:PatchVersion=$PACK_PATCH" "/p:VersionSuffix=$PACK_VERSION_SUFFIX" \
      /p:WasmNativeStrip=false \
      /p:EnableSourceLink=false \
      "/p:WasiOpenSslLibDir=$OUT/openssl/lib" \
      "/p:_IcuDir=$ICU_MONO_NATIVE" "/p:ICULibDir=$ICU_MONO_NATIVE/lib" \
      -cmakeargs "$(openssl_cmake_args)"
  ) > "$LOGS/runtime-build.log" 2>&1
}

# The version a build stage recorded, or the message naming that stage.
recorded_version() {
  local file="$1" stage="$2"
  [ -f "$file" ] || { echo "the $stage stage records its pack version at $file; run it first" >&2; exit 1; }
  cat "$file"
}
# The pack's identity is its version plus the digest NuGet records for the
# package, the form the SDK checks against the pack a build resolves.
pack_digest() {
  openssl dgst -sha512 -binary "$1" | base64 -w0
}
# Unpacked copies, for projects that import a pack explicitly instead of
# through the installed workload.
unpack() {
  mkdir -p "$2"
  unzip -q "$1" -d "$2"
}
stage_feed() {
  echo "==> feed"
  require_build_inputs
  local feed="$OUT/feed"
  mkdir -p "$feed"
  local version
  version="$(recorded_version "$runtime_version_file" runtime)"
  local shipping="$RUNTIME/artifacts/packages/Release/Shipping"
  local pack="$shipping/Microsoft.NETCore.App.Runtime.Mono.wasi-wasm.$version.nupkg"
  [ -f "$pack" ] || { echo "the runtime stage produced no $pack:" >&2; ls "$shipping" >&2; exit 1; }
  local sdkpack="$shipping/Microsoft.NET.Runtime.WebAssembly.Wasi.Sdk.$version.nupkg"
  [ -f "$sdkpack" ] || { echo "the runtime stage produced no Wasi.Sdk pack at $sdkpack" >&2; exit 1; }
  rm -f "$feed"/*.nupkg
  cp "$pack" "$sdkpack" "$feed/"
  rm -rf "$OUT/packs"
  unpack "$sdkpack" "$OUT/packs/wasi-sdk"
  # The platform runtime build selects its packs by version and checks
  # each resolved pack against the feed's digest.
  cat > "$feed/Isoplane.GuestRuntime.props" <<EOF
<Project>
  <PropertyGroup>
    <IsoplaneGuestRuntimeSourceRevision>$(git -C "$RUNTIME" rev-parse HEAD)</IsoplaneGuestRuntimeSourceRevision>
    <IsoplaneGuestRuntimeBuildInputs>$ISOPLANE_RUNTIME_INPUTS</IsoplaneGuestRuntimeBuildInputs>
    <IsoplaneGuestOpenSslSourceRevision>${ISOPLANE_OPENSSL_REVISION:-$OPENSSL_REVISION}</IsoplaneGuestOpenSslSourceRevision>
    <IsoplaneGuestIcuSourceRevision>${ISOPLANE_ICU_REVISION:-$ICU_REVISION}</IsoplaneGuestIcuSourceRevision>
    <IsoplaneGuestWasiSdkVersion>$WASI_SDK_VERSION</IsoplaneGuestWasiSdkVersion>
    <IsoplaneGuestWasiSdkPackVersion>$version</IsoplaneGuestWasiSdkPackVersion>
    <IsoplaneGuestWasiSdkPackSha512>$(pack_digest "$sdkpack")</IsoplaneGuestWasiSdkPackSha512>
    <IsoplaneGuestRuntimePackVersion>$version</IsoplaneGuestRuntimePackVersion>
    <IsoplaneGuestRuntimePackSha512>$(pack_digest "$pack")</IsoplaneGuestRuntimePackSha512>
    <IsoplaneGuestWasiSdkPackDirectory>\$(MSBuildThisFileDirectory)../packs/wasi-sdk/</IsoplaneGuestWasiSdkPackDirectory>
  </PropertyGroup>
</Project>
EOF
  echo "    $(basename "$pack")"
  echo "    $(basename "$sdkpack")"
}

if [ "$#" = 0 ]; then
  echo "Usage: $0 runtime|feed [...]" >&2
  exit 2
fi
for stage in "$@"; do
  case "$stage" in
    runtime|feed) "stage_$stage" ;;
    *) echo "Unknown runtime stage: $stage" >&2; exit 2 ;;
  esac
done
