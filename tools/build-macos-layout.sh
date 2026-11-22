#!/usr/bin/env bash
# Makes bflat's osx-arm64 layout. Run on an Apple Silicon Mac with the .NET 10
# SDK and Xcode's command-line tools (NativeAOT links with them).
#
# bflat can't build itself for macOS (it only compiles for macOS, -c), so:
# - bflat is published with Microsoft's NativeAOT for osx-arm64;
# - the JITs and libjitinterface are Microsoft's, from
#   runtime.osx-arm64.microsoft.dotnet.ilcompiler (GaJitVersion), which hosts
#   every JIT the Linux layouts carry;
# - lld is LLVM's macOS arm64 build, from this repository's lld release
#   (LldVersion; .github/workflows/lld.yml), or from LLVM's own release if
#   that doesn't have it;
# - lib/, ref/ and the notices, which don't depend on the host, are the
#   linux-glibc-arm64 layout's (BuildLayouts, on Linux).
#
# Usage:  tools/build-macos-layout.sh <linux-glibc-arm64 layout> <out dir>
# Env:    LLD_TGZ   a local lld-osx-arm64.tar.gz instead of the release's
#         GITHUB_TOKEN   for the bflattened package feed, as BuildLayouts needs
set -euo pipefail
cd "$(dirname "$0")/.."
linux=$(cd "${1:?linux-glibc-arm64 layout}" && pwd)
out=${2:?out dir}
[ "$(uname -s)-$(uname -m)" = Darwin-arm64 ] || { echo "Error: run this on an Apple Silicon Mac." >&2; exit 1; }
[ -f "$linux/bflat" ] && [ -d "$linux/lib" ] && [ -d "$linux/ref" ] || { echo "Error: $linux isn't a bflat Linux layout." >&2; exit 1; }

prop() { sed -n "s|.*<$1>\(.*\)</$1>.*|\1|p" src/bflat/bflat.csproj | head -1; }
jit_version=$(prop GaJitVersion)
lld_version=$(prop LldVersion)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

echo "[publish] bflat for osx-arm64 (NativeAOT)"
if [ -n "${GITHUB_TOKEN:-}" ] && ! dotnet nuget list source --configfile src/bflat/nuget.config 2>/dev/null | grep -q bflattened; then
    (cd src/bflat && dotnet nuget add source --username USERNAME --password "$GITHUB_TOKEN" --store-password-in-clear-text \
        --name github "https://nuget.pkg.github.com/bflattened/index.json")
fi
# CompilerHost only satisfies the project's per-host downloads, which this
# layout doesn't use.
dotnet publish src/bflat/bflat.csproj -c Release -r osx-arm64 -p:PublishAot=true \
    -p:CompilerHost=linux-glibc-arm64 -o "$work/publish" > "$work/publish.log" 2>&1 || { tail -30 "$work/publish.log"; exit 1; }

rm -rf "$out"
mkdir -p "$out/bin"
cp -R "$linux/lib" "$linux/ref" "$out/"
for f in THIRD-PARTY-NOTICES.TXT WindowsAPIs.txt; do [ -f "$linux/$f" ] && cp "$linux/$f" "$out/"; done
cp "$work/publish/bflat" "$out/"

echo "[jit] runtime.osx-arm64.microsoft.dotnet.ilcompiler $jit_version"
curl -sfL -o "$work/ilc.nupkg" \
    "https://api.nuget.org/v3-flatcontainer/runtime.osx-arm64.microsoft.dotnet.ilcompiler/$jit_version/runtime.osx-arm64.microsoft.dotnet.ilcompiler.$jit_version.nupkg"
(cd "$work" && unzip -q ilc.nupkg 'tools/libclrjit_*.dylib' 'tools/libjitinterface_arm64.dylib')
cp "$work"/tools/*.dylib "$out/"

echo "[lld] $lld_version"
if [ -n "${LLD_TGZ:-}" ]; then
    tar -xzf "$LLD_TGZ" -C "$out/bin" lld
elif curl -sfL -o "$work/lld.tgz" "https://github.com/OnurGumus/bflat/releases/download/lld-$lld_version/lld-osx-arm64.tar.gz"; then
    tar -xzf "$work/lld.tgz" -C "$out/bin" lld
else
    # Not in the release (yet): the same file, from LLVM's own macOS release.
    curl -sfL "https://github.com/llvm/llvm-project/releases/download/llvmorg-$lld_version/LLVM-$lld_version-macOS-ARM64.tar.xz" \
        | tar -xJ --strip-components=2 -C "$out/bin" '*/bin/lld'
fi
tools/check-lld-mac.sh "$out/bin/lld"

# NativeAOT and lld output are signed ad hoc by their linkers; Microsoft's
# dylibs by Microsoft. Nothing here may need more than macOS itself.
for f in "$out/bflat" "$out/bin/lld" "$out"/*.dylib; do
    codesign -v "$f"
    otool -L "$f" | tail -n +2 | awk '{print $1}' | while read -r lib; do
        case "$lib" in /usr/lib/* | /System/* | @rpath/* | @executable_path/* | @loader_path/*) ;;
            *) echo "Error: $f needs $lib, which isn't part of macOS." >&2; exit 1 ;; esac
    done
done
echo "$(find "$out" -type f | wc -l | tr -d ' ') files in $out"
