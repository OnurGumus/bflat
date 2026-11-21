#!/usr/bin/env bash
# Builds lld from LLVM source (see LldVersion in src/bflat/bflat.csproj),
# terminfo, libstdc++ linked in. Run in ubuntu:22.04 (glibc 2.35, so the
# result runs on 22.04 and newer). $1: output directory.
set -euo pipefail
out=$1
apt-get update -qq >/dev/null
DEBIAN_FRONTEND=noninteractive apt-get install -y -qq curl xz-utils cmake ninja-build g++ python3 binutils >/dev/null
v=20.1.8
mkdir -p /src && cd /src
curl -sL "https://github.com/llvm/llvm-project/releases/download/llvmorg-$v/llvm-project-$v.src.tar.xz" \
    | tar -xJ --strip-components=1 "llvm-project-$v.src/llvm" "llvm-project-$v.src/lld" \
        "llvm-project-$v.src/cmake" "llvm-project-$v.src/libunwind" "llvm-project-$v.src/third-party"
cmake -S llvm -B build -G Ninja -DCMAKE_BUILD_TYPE=Release -DLLVM_ENABLE_PROJECTS=lld \
    -DLLVM_TARGETS_TO_BUILD="X86;AArch64" -DLLVM_ENABLE_ZLIB=OFF -DLLVM_ENABLE_ZSTD=OFF \
    -DLLVM_ENABLE_LIBXML2=OFF -DLLVM_ENABLE_TERMINFO=OFF -DLLVM_ENABLE_LIBEDIT=OFF \
    -DLLVM_STATIC_LINK_CXX_STDLIB=ON -DLLVM_INCLUDE_TESTS=OFF -DLLVM_INCLUDE_BENCHMARKS=OFF \
    -DLLVM_INCLUDE_EXAMPLES=OFF -DLLVM_ENABLE_BINDINGS=OFF > /dev/null
ninja -C build lld > /dev/null
strip -o "$out/lld" build/bin/lld
readelf -d "$out/lld" | grep NEEDED
ls -la "$out/lld"
