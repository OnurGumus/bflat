#!/usr/bin/env bash
# Checks that an lld needs nothing beyond what every glibc Linux has: its
# NEEDED libraries must come from this list. $1: the lld binary.
set -euo pipefail
command -v readelf >/dev/null || { echo "check-lld.sh needs readelf (binutils)" >&2; exit 2; }
allowed=" libc.so.6 libm.so.6 libpthread.so.0 libdl.so.2 librt.so.1 libgcc_s.so.1 libstdc++.so.6 libz.so.1 ld-linux-x86-64.so.2 ld-linux-aarch64.so.1 "
bad=0
for lib in $(readelf -d "$1" | sed -n 's/.*(NEEDED).*\[\(.*\)\]/\1/p'); do
    if [[ "$allowed" != *" $lib "* ]]; then
        echo "$1 needs $lib, which not every distribution has" >&2
        bad=1
    fi
done
exit $bad
