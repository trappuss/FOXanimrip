// SPDX-License-Identifier: MIT
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;

namespace FoxAnimRip;

/// <summary>
/// Reads the .NET single-file bundle appended to FoxBrowser.exe.
///
/// FoxBrowser ships as one self-contained executable: a normal PE image with a
/// bundle of every managed assembly glued on the end, each entry optionally
/// Deflate-compressed. This tool never redistributes those assemblies -- it
/// unpacks them from the copy of FoxBrowser.exe the user already has, into a
/// cache folder, and loads them from there. That keeps the decoder, the rig
/// solve and the FBX writer byte-for-byte identical to what the GUI produces,
/// and it means the tool tracks whatever FoxBrowser version is installed.
/// </summary>
public static class Bundle
{
    // Marker Microsoft embeds just after the bundle header offset.
    private static readonly byte[] Signature =
    {
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
        0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32
    };

    /// <summary>Assemblies that belong to the host runtime, not the app.</summary>
    private static bool IsRuntimeAssembly(string name) =>
        name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
        || name.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase)
        || name.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase)
        || name.Equals("WindowsBase.dll", StringComparison.OrdinalIgnoreCase);

    public static string Extract(string exePath, bool force, Action<string> log)
    {
        var info = new FileInfo(exePath);
        if (!info.Exists)
            throw new FileNotFoundException($"FoxBrowser.exe not found at '{exePath}'");

        // Cache key covers size + mtime so a FoxBrowser update re-extracts.
        var key = $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}";
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(info.FullName.ToLowerInvariant() + key)))[..12];
        var dir = Path.Combine(Paths.Assemblies, hash);
        var stamp = Path.Combine(dir, ".complete");

        if (File.Exists(stamp) && !force)
            return dir;

        if (Directory.Exists(dir) && force)
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        var data = File.ReadAllBytes(exePath);
        var sig = IndexOf(data, Signature);
        if (sig < 0)
            throw new InvalidDataException(
                "That file is not a .NET single-file bundle. Point --fb at FoxBrowser.exe itself.");

        var pos = (int)BitConverter.ToInt64(data, sig - 8);
        var major = BitConverter.ToUInt32(data, pos); pos += 4;
        BitConverter.ToUInt32(data, pos); pos += 4;               // minor
        var count = BitConverter.ToInt32(data, pos); pos += 4;
        ReadString(data, ref pos);                                 // bundle id
        if (major >= 2)
        {
            pos += 8 * 4;                                          // deps/runtimeconfig spans
            pos += 8;                                              // flags
        }

        var written = 0;
        for (var i = 0; i < count; i++)
        {
            var offset = BitConverter.ToInt64(data, pos); pos += 8;
            var size = BitConverter.ToInt64(data, pos); pos += 8;
            long compressed = 0;
            if (major >= 6) { compressed = BitConverter.ToInt64(data, pos); pos += 8; }
            pos += 1;                                              // entry type
            var path = ReadString(data, ref pos);

            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            if (IsRuntimeAssembly(path)) continue;

            var raw = new ReadOnlySpan<byte>(data, (int)offset, (int)(compressed != 0 ? compressed : size));
            byte[] bytes;
            if (compressed != 0)
            {
                using var src = new MemoryStream(raw.ToArray());
                using var inflate = new System.IO.Compression.DeflateStream(
                    src, System.IO.Compression.CompressionMode.Decompress);
                using var dst = new MemoryStream((int)size);
                inflate.CopyTo(dst);
                bytes = dst.ToArray();
            }
            else
            {
                bytes = raw.ToArray();
            }

            File.WriteAllBytes(Path.Combine(dir, Path.GetFileName(path)), bytes);
            written++;
        }

        File.WriteAllText(stamp, key);
        log($"unpacked {written} assemblies from {Path.GetFileName(exePath)}");
        return dir;
    }

    /// <summary>Resolve FoxBrowser's assemblies out of the extraction folder.</summary>
    public static void Hook(string dir)
    {
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var candidate = Path.Combine(dir, name.Name + ".dll");
            return File.Exists(candidate)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate)
                : null;
        };
    }

    private static string ReadString(byte[] data, ref int pos)
    {
        var length = 0;
        var shift = 0;
        while (true)
        {
            var b = data[pos++];
            length |= (b & 0x7f) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        var value = Encoding.UTF8.GetString(data, pos, length);
        pos += length;
        return value;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { ok = false; break; }
            if (ok) return i;
        }
        return -1;
    }
}
