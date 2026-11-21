#!/usr/bin/env bash
# Checks that a Windows lld.exe is LLVM's 20.1.8 and needs nothing beyond
# Windows itself: every DLL it imports must be a system DLL (no MinGW runtime,
# libxml2, zlib, ...). $1: the lld.exe.
# Exit: 0 fine, 1 not, 2 no objdump to read it with.
set -euo pipefail
exe=${1:?lld.exe}
headers=
for tool in objdump llvm-objdump; do
    # Kept, then searched: grep -q stopping early would fail the pipe.
    command -v "$tool" > /dev/null && headers=$("$tool" -p "$exe" 2> /dev/null) || true
    [[ "$headers" == *"DLL Name"* ]] && break
    headers=
done
[ -n "$headers" ] || { echo "check-lld-win.sh needs an objdump that reads PE files (binutils or llvm-objdump)" >&2; exit 2; }
bad=0
for dll in $(sed -n 's/.*DLL Name: *//p' <<< "$headers" | tr -d '\r'); do
    if ! [[ "$(tr '[:upper:]' '[:lower:]' <<< "$dll")" =~ ^(kernel32|advapi32|ntdll|ole32|oleaut32|shell32|user32|version|ws2_32|bcrypt|api-ms-win-.*|ucrtbase)\.dll$ ]]; then
        echo "$exe needs $dll, which isn't part of Windows" >&2
        bad=1
    fi
done
if ! LC_ALL=C grep -aq '20\.1\.8' "$exe"; then
    echo "$exe isn't LLVM 20.1.8" >&2
    bad=1
fi
exit $bad
