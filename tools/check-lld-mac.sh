#!/usr/bin/env bash
# Checks that a macOS lld is LLVM's 20.1.8, works as ld64.lld (-flavor darwin),
# and needs nothing beyond macOS itself: every library it loads must be under
# /usr/lib/ or /System/ (no Homebrew zstd, libxml2 from elsewhere, ...).
# Run on a Mac. $1: the lld.
set -euo pipefail
exe=${1:?lld}
bad=0
for lib in $(otool -L "$exe" | tail -n +2 | awk '{print $1}'); do
    case "$lib" in
        /usr/lib/* | /System/*) ;;
        *) echo "$exe needs $lib, which isn't part of macOS" >&2; bad=1 ;;
    esac
done
if ! "$exe" -flavor darwin --version | grep -q '20\.1\.8'; then
    echo "$exe -flavor darwin isn't LLVM 20.1.8" >&2
    bad=1
fi
exit $bad
