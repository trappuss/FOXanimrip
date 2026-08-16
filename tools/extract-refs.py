#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""
Unpack FoxBrowser's assemblies so the project can be compiled against them.

foxanimrip deliberately ships none of FoxBrowser's code. At run time it reads
the assemblies out of the user's own FoxBrowser.exe; at build time the compiler
still needs to see the API, so run this once against your copy:

    python tools/extract-refs.py "C:/Tools/FoxBrowser/FoxBrowser.exe" refs

then build with -p:FoxBrowserRefDir=<abs path to refs>.

FoxBrowser.exe is a .NET single-file bundle: a normal PE image with every
managed assembly appended, each entry optionally Deflate-compressed. This walks
that bundle and writes out the app assemblies (skipping the .NET runtime's own,
which the SDK already has).
"""

from __future__ import annotations

import os
import struct
import sys
import zlib

# Marker Microsoft writes just after the bundle header offset.
SIGNATURE = bytes([
    0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
    0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
])

RUNTIME_PREFIXES = ("System.", "Microsoft.")
RUNTIME_NAMES = ("netstandard.dll", "mscorlib.dll", "WindowsBase.dll")

#: What the build actually references. Pass --all to write everything.
WANTED = (
    "FoxBrowser.Core.dll",
    "FoxBrowser.Native.dll",
    "MgsvModBldr.Tools.Browse.dll",
    "Avalonia.Base.dll",
)


def read_string(data: bytes, pos: int) -> tuple[str, int]:
    """7-bit-encoded length prefix, then UTF-8."""
    length = 0
    shift = 0
    while True:
        byte = data[pos]
        pos += 1
        length |= (byte & 0x7F) << shift
        if not byte & 0x80:
            break
        shift += 7
    return data[pos:pos + length].decode("utf-8"), pos + length


def entries(data: bytes):
    sig = data.find(SIGNATURE)
    if sig < 0:
        raise SystemExit("that file is not a .NET single-file bundle "
                         "- point this at FoxBrowser.exe itself")

    pos = struct.unpack_from("<q", data, sig - 8)[0]
    major = struct.unpack_from("<I", data, pos)[0]
    count = struct.unpack_from("<i", data, pos + 8)[0]
    pos += 12
    _bundle_id, pos = read_string(data, pos)
    if major >= 2:
        pos += 8 * 4          # deps.json / runtimeconfig.json spans
        pos += 8              # flags

    for _ in range(count):
        offset, size = struct.unpack_from("<qq", data, pos)
        pos += 16
        compressed = 0
        if major >= 6:
            compressed = struct.unpack_from("<q", data, pos)[0]
            pos += 8
        pos += 1              # entry type
        path, pos = read_string(data, pos)
        yield path, offset, size, compressed


def is_runtime(name: str) -> bool:
    return name.startswith(RUNTIME_PREFIXES) or name in RUNTIME_NAMES


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__)
        return 64

    exe = argv[1]
    out = argv[2] if len(argv) > 2 else "refs"
    take_all = "--all" in argv

    with open(exe, "rb") as handle:
        data = handle.read()

    os.makedirs(out, exist_ok=True)
    written = 0
    for path, offset, size, compressed in entries(data):
        name = os.path.basename(path)
        if not name.lower().endswith(".dll"):
            continue
        if not take_all and name not in WANTED:
            continue
        if take_all and is_runtime(name):
            continue

        raw = data[offset:offset + (compressed or size)]
        if compressed:
            raw = zlib.decompress(raw, -15)
        if len(raw) != size:
            print(f"  ! {name}: expected {size} bytes, got {len(raw)}")
            continue
        with open(os.path.join(out, name), "wb") as handle:
            handle.write(raw)
        written += 1
        print(f"  {name}  ({size:,} bytes)")

    print(f"\n{written} assembly(ies) written to {os.path.abspath(out)}")
    if not take_all:
        missing = [n for n in WANTED if not os.path.exists(os.path.join(out, n))]
        if missing:
            print("! missing: " + ", ".join(missing))
            print("  Try --all, or check that this is a recent FoxBrowser build.")
            return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
