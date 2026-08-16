#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Build the cross-platform console tool.  Usage:  ./build.sh /path/to/FoxBrowser.exe
set -euo pipefail

FOXBROWSER="${1:?usage: ./build.sh /path/to/FoxBrowser.exe [rid]}"
RID="${2:-linux-x64}"
ROOT="$(cd "$(dirname "$0")" && pwd)"

echo "Unpacking reference assemblies from $FOXBROWSER"
python3 "$ROOT/tools/extract-refs.py" "$FOXBROWSER" "$ROOT/refs"

echo "Building foxanimrip-cli ($RID)"
dotnet publish "$ROOT/src/FoxAnimRip.Headless/FoxAnimRip.Headless.csproj" \
    -c Release -r "$RID" --self-contained false \
    -p:FoxBrowserRefDir="$ROOT/refs" -o "$ROOT/dist"

echo
echo "Done. Binaries in $ROOT/dist"
echo "Note: the Windows GUI (src/FoxAnimRip) needs -p:EnableWindowsTargeting=true"
echo "      when built from Linux, and only runs on Windows."
