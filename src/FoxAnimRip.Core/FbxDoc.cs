// SPDX-License-Identifier: MIT
using System.Text;

namespace FoxAnimRip;

/// <summary>A node in a binary FBX tree, with its properties kept as raw bytes.</summary>
public sealed class FbxNode
{
    public byte[] Name = Array.Empty<byte>();
    public int NumProps;
    public List<byte[]> Props = new();
    public List<FbxNode> Children = new();
    public bool Terminated;

    public string NameText
    {
        get => Encoding.UTF8.GetString(Name);
        set => Name = Encoding.UTF8.GetBytes(value);
    }

    public FbxNode Child(string name) =>
        Children.FirstOrDefault(c => c.NameText == name);

    /// <summary>Property <paramref name="index"/> as an int64, if it is one.</summary>
    public long? Int64At(int index)
    {
        if (index >= Props.Count) return null;
        var prop = Props[index];
        return prop.Length == 9 && prop[0] == (byte)'L'
            ? BitConverter.ToInt64(prop, 1)
            : null;
    }

    public void SetInt64At(int index, long value)
    {
        if (index >= Props.Count) return;
        var buf = new byte[9];
        buf[0] = (byte)'L';
        BitConverter.GetBytes(value).CopyTo(buf, 1);
        Props[index] = buf;
    }

    /// <summary>Property <paramref name="index"/> as a string, if it is one.</summary>
    public string StringAt(int index)
    {
        if (index >= Props.Count) return null;
        var prop = Props[index];
        if (prop.Length < 5 || prop[0] != (byte)'S') return null;
        var length = BitConverter.ToInt32(prop, 1);
        return Encoding.UTF8.GetString(prop, 5, Math.Min(length, prop.Length - 5));
    }

    public void SetStringAt(int index, string value)
    {
        if (index >= Props.Count) return;
        var bytes = Encoding.UTF8.GetBytes(value);
        var buf = new byte[5 + bytes.Length];
        buf[0] = (byte)'S';
        BitConverter.GetBytes(bytes.Length).CopyTo(buf, 1);
        bytes.CopyTo(buf, 5);
        Props[index] = buf;
    }

    /// <summary>FBX object names are ``"name\0Class"``; this is the name half.</summary>
    public string ObjectName()
    {
        var raw = StringAt(1);
        if (raw is null) return null;
        var cut = raw.IndexOf("\0", StringComparison.Ordinal);
        return cut >= 0 ? raw[..cut] : raw;
    }

    public FbxNode Clone() => new()
    {
        Name = (byte[])Name.Clone(),
        NumProps = NumProps,
        Props = Props.Select(p => (byte[])p.Clone()).ToList(),
        Children = Children.Select(c => c.Clone()).ToList(),
        Terminated = Terminated,
    };
}

/// <summary>
/// A minimal read/write model for binary FBX.
///
/// Only enough to move nodes between documents: property payloads -- including
/// the compressed vertex and key arrays -- are copied byte for byte and never
/// re-encoded. Node headers store *absolute* end offsets, so serialising always
/// recomputes them from scratch.
/// </summary>
public sealed class FbxDoc
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0");

    public uint Version { get; private set; } = 7400;
    public List<FbxNode> Roots { get; private set; } = new();
    /// <summary>Everything after the last root node: the footer, copied verbatim.</summary>
    public byte[] Tail { get; private set; } = Array.Empty<byte>();

    public static bool LooksLikeFbx(byte[] data) =>
        data.Length > 27 && data.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    public static FbxDoc Parse(byte[] data)
    {
        if (!LooksLikeFbx(data)) throw new InvalidDataException("not a binary FBX");

        var doc = new FbxDoc { Version = BitConverter.ToUInt32(data, 23) };
        var pos = 27;
        while (true)
        {
            var node = ReadNode(data, ref pos, doc.Version);
            if (node is null) break;
            doc.Roots.Add(node);
        }
        doc.Tail = data.AsSpan(pos).ToArray();
        return doc;
    }

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        ms.Write(Magic);
        ms.WriteByte(0x1a);
        ms.WriteByte(0x00);
        ms.Write(BitConverter.GetBytes(Version));
        foreach (var root in Roots) WriteNode(ms, root, Version);
        ms.Write(new byte[Version >= 7500 ? 25 : 13]);
        ms.Write(Tail);
        return ms.ToArray();
    }

    public FbxNode Root(string name) => Roots.FirstOrDefault(r => r.NameText == name);

    private static FbxNode ReadNode(byte[] d, ref int pos, uint version)
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

        var node = new FbxNode { NumProps = (int)numProps };
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

    private static void WriteNode(MemoryStream ms, FbxNode node, uint version)
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
