// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Text;

namespace FoxAnimRip;

/// <summary>
/// Corrects the animation class tokens in a FoxBrowser-written FBX.
///
/// FoxBrowser tags its animation objects <c>AnimationStack</c> /
/// <c>AnimationLayer</c>. The FBX convention -- what the Autodesk SDK, Maya and
/// Blender all expect -- is <c>AnimStack</c> / <c>AnimLayer</c>; the long
/// spelling belongs only in the Definitions table. Blender 4.x asserts on it in
/// <c>import_fbx.blen_read_animations</c> and aborts the import part-way
/// through, so every clip this tool writes gets fixed on the way out.
///
/// Node headers store absolute end offsets, so shortening a string by five
/// bytes moves every offset after it. This re-serialises the whole tree with
/// recomputed offsets while copying property payloads -- including the
/// compressed vertex arrays -- byte for byte.
/// </summary>
internal static class FbxFix
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0");

    private static readonly (string Bad, string Good)[] Fixes =
    {
        ("\0AnimationStack", "\0AnimStack"),
        ("\0AnimationLayer", "\0AnimLayer"),
    };

    private sealed class Node
    {
        public byte[] Name = Array.Empty<byte>();
        public int NumProps;
        public List<byte[]> Props = new();
        public List<Node> Children = new();
        public bool Terminated;
    }

    /// <summary>Returns the corrected bytes, and how many tokens were rewritten.</summary>
    public static byte[] Apply(byte[] fbx, out int fixesApplied)
    {
        fixesApplied = 0;
        if (fbx.Length < 27 || !fbx.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            return fbx;

        var version = BitConverter.ToUInt32(fbx, 23);
        var pos = 27;
        var roots = new List<Node>();
        while (true)
        {
            var node = ReadNode(fbx, ref pos, version);
            if (node is null) break;
            roots.Add(node);
        }
        var tailStart = pos;

        var applied = 0;
        foreach (var root in roots) applied += FixNode(root);
        fixesApplied = applied;
        if (applied == 0) return fbx;

        using var ms = new MemoryStream(fbx.Length);
        ms.Write(Magic);
        ms.WriteByte(0x1a); ms.WriteByte(0x00);
        ms.Write(BitConverter.GetBytes(version));
        foreach (var root in roots) WriteNode(ms, root, version);
        ms.Write(new byte[version >= 7500 ? 25 : 13]);
        ms.Write(fbx, tailStart, fbx.Length - tailStart);   // footer carries no offsets
        return ms.ToArray();
    }

    private static int FixNode(Node node)
    {
        var n = 0;
        for (var i = 0; i < node.Props.Count; i++)
        {
            var prop = node.Props[i];
            if (prop.Length < 5 || prop[0] != (byte)'S') continue;
            var length = BitConverter.ToInt32(prop, 1);
            var value = Encoding.UTF8.GetString(prop, 5, length);
            foreach (var (bad, good) in Fixes)
            {
                if (!value.EndsWith(bad, StringComparison.Ordinal)) continue;
                var replaced = value[..^bad.Length] + good;
                var bytes = Encoding.UTF8.GetBytes(replaced);
                var buf = new byte[5 + bytes.Length];
                buf[0] = (byte)'S';
                BitConverter.GetBytes(bytes.Length).CopyTo(buf, 1);
                bytes.CopyTo(buf, 5);
                node.Props[i] = buf;
                n++;
                break;
            }
        }
        foreach (var child in node.Children) n += FixNode(child);
        return n;
    }

    private static Node ReadNode(byte[] d, ref int pos, uint version)
    {
        var header = version >= 7500 ? 25 : 13;
        long end, numProps, propLen;
        if (version >= 7500)
        {
            end = BitConverter.ToInt64(d, pos);
            numProps = BitConverter.ToInt64(d, pos + 8);
            propLen = BitConverter.ToInt64(d, pos + 16);
        }
        else
        {
            end = BitConverter.ToUInt32(d, pos);
            numProps = BitConverter.ToUInt32(d, pos + 4);
            propLen = BitConverter.ToUInt32(d, pos + 8);
        }
        int nameLen = d[pos + header - 1];
        if (end == 0) { pos += header; return null; }

        var node = new Node { NumProps = (int)numProps };
        var nameStart = pos + header;
        node.Name = d.AsSpan(nameStart, nameLen).ToArray();

        var p = nameStart + nameLen;
        var propsEnd = p + (int)propLen;
        for (var i = 0; i < numProps && p < propsEnd; i++)
        {
            var begin = p;
            var kind = d[p++];
            switch (kind)
            {
                case (byte)'Y': p += 2; break;
                case (byte)'C': p += 1; break;
                case (byte)'I': p += 4; break;
                case (byte)'F': p += 4; break;
                case (byte)'D': p += 8; break;
                case (byte)'L': p += 8; break;
                case (byte)'S':
                case (byte)'R':
                    p += 4 + BitConverter.ToInt32(d, p);
                    break;
                case (byte)'f': case (byte)'d': case (byte)'l':
                case (byte)'i': case (byte)'b': case (byte)'c':
                    p += 12 + BitConverter.ToInt32(d, p + 8);
                    break;
                default:
                    throw new InvalidDataException($"unknown FBX property type 0x{kind:x2}");
            }
            node.Props.Add(d.AsSpan(begin, p - begin).ToArray());
        }

        var cursor = propsEnd;
        while (cursor < end)
        {
            var child = ReadNode(d, ref cursor, version);
            if (child is null) { node.Terminated = true; break; }
            node.Children.Add(child);
        }
        pos = (int)end;
        return node;
    }

    private static void WriteNode(MemoryStream ms, Node node, uint version)
    {
        var header = version >= 7500 ? 25 : 13;
        var start = (int)ms.Position;
        ms.Write(new byte[header]);
        ms.Write(node.Name);
        var propsStart = ms.Position;
        foreach (var prop in node.Props) ms.Write(prop);
        var propLen = ms.Position - propsStart;
        foreach (var child in node.Children) WriteNode(ms, child, version);
        if (node.Terminated) ms.Write(new byte[header]);
        var end = ms.Position;

        ms.Position = start;
        if (version >= 7500)
        {
            ms.Write(BitConverter.GetBytes(end));
            ms.Write(BitConverter.GetBytes((long)node.NumProps));
            ms.Write(BitConverter.GetBytes(propLen));
        }
        else
        {
            ms.Write(BitConverter.GetBytes((uint)end));
            ms.Write(BitConverter.GetBytes((uint)node.NumProps));
            ms.Write(BitConverter.GetBytes((uint)propLen));
        }
        ms.WriteByte((byte)node.Name.Length);
        ms.Position = end;
    }
}
