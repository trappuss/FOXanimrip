// SPDX-License-Identifier: MIT
using FoxBrowser.Models;

namespace FoxAnimRip;

/// <summary>
/// A form-variation file, read into rows you can put in a spreadsheet.
///
/// The parser is FoxBrowser's; this only reads its results out. Two things it
/// has to work around: the parser takes a file path rather than bytes, so an
/// archive entry goes through a temporary file, and the format identifies
/// everything by 32- or 64-bit hash, so names come out only as far as the game's
/// own dictionaries reach.
///
/// The distinction worth understanding when reading the output: a
/// <c>textureSwap</c> row means the option genuinely points at a different
/// texture file, while a <c>materialParameter</c> row means it only changes a
/// shader value. Something like a skin tone can be built either way, and this is
/// where you find out which.
/// </summary>
public sealed class Fv2Reader
{
    public sealed record Entry(string Kind, string Line);

    /// <summary>The variation's external file list, as the raw 64-bit codes the
    /// format stores. Texture swaps and attachments refer to these by index.</summary>
    public ulong[] ExternalFiles => _file.externalFileEntries ?? Array.Empty<ulong>();

    private readonly MgsvModBldr.Tools.Fv2.Fv2 _file;

    private Fv2Reader(MgsvModBldr.Tools.Fv2.Fv2 file) => _file = file;

    public static Fv2Reader Read(byte[] bytes)
    {
        if (bytes is null || bytes.Length < 16) return null;
        var temp = Path.Combine(Path.GetTempPath(),
                                "foxanimrip-fv2-" + Guid.NewGuid().ToString("N") + ".fv2");
        try
        {
            File.WriteAllBytes(temp, bytes);
            var file = new MgsvModBldr.Tools.Fv2.Fv2();
            file.Read(temp);
            return new Fv2Reader(file);
        }
        catch { return null; }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    public IEnumerable<Entry> Describe(CatalogEntry entry, ref int unresolved)
    {
        var rows = new List<Entry>();
        var name = entry.Stem;
        var where = $"{entry.ArchiveName}\t{entry.Path}";
        var missing = 0;

        string Named(ulong hash)
        {
            if (hash == 0) return "";
            var full = HashNames.ResolveFull(hash) ?? HashNames.ResolveLeaf(hash);
            if (full is not null) return full;
            var label = StrCodeNames.Label(hash, null);
            if (label is not null) return label;
            missing++;
            return $"{hash:x16}";
        }

        string Named32(uint hash) => Named(hash);

        void Add(string kind, string detail, string value) =>
            rows.Add(new Entry(kind, string.Join('\t', name, kind, detail, value, where)));

        // Which parts of the mesh this variation turns off and on. This is how a
        // single model file serves many appearances.
        foreach (var group in _file.hideMeshGroupEntries ?? Array.Empty<uint>())
            Add("hideMeshGroup", Named32(group), "");
        foreach (var group in _file.showMeshGroupEntries ?? Array.Empty<uint>())
            Add("showMeshGroup", Named32(group), "");

        // A different texture on a named material slot: a real file swap.
        foreach (var swap in _file.textureSwapEntries
                             ?? Array.Empty<MgsvModBldr.Tools.Fv2.Fv2.TextureSwapEntry>())
            Add("textureSwap", Named32(swap.materialInstanceStrCode32),
                $"{Named32(swap.textureTypeStrCode32)} -> texture #{swap.textureIndex}");

        // A shader value rather than a texture. Colour variation is often this.
        var parameterList = _file.materialParameterEntries;
        for (var i = 0; parameterList is not null && i < parameterList.Length; i++)
        {
            var v = parameterList[i];
            if (v is null) continue;
            // The Fv2 assembly carries its own lower-case Vector4, not the one
            // from System.Numerics.
            Add("materialParameter", $"#{i}",
                $"{v.x:0.###} {v.y:0.###} {v.z:0.###} {v.w:0.###}");
        }

        // Extra models bolted on: hair, headgear, equipment.
        foreach (var variable in _file.variableDataEntries
                                 ?? Array.Empty<MgsvModBldr.Tools.Fv2.Fv2.VariableDataEntry>())
        {
            foreach (var sub in variable.variableDataSubEntries
                                ?? Array.Empty<MgsvModBldr.Tools.Fv2.Fv2.VariableDataSubEntry>())
            {
                foreach (var attach in sub.boneModelAttachEntries
                                       ?? Array.Empty<MgsvModBldr.Tools.Fv2.Fv2.BoneModelAttachEntry>())
                    Add("attachModel", $"bone, file #{attach.fmdlIndex}",
                        attach.simIndex >= 0 ? $"sim #{attach.simIndex}" : "");
                foreach (var attach in sub.cnpModelAttachEntries
                                       ?? Array.Empty<MgsvModBldr.Tools.Fv2.Fv2.CnpModelAttachEntry>())
                    Add("attachModel", $"{Named32(attach.cnpStrCode32)}, file #{attach.fmdlIndex}",
                        attach.simIndex >= 0 ? $"sim #{attach.simIndex}" : "");
                foreach (var swap in sub.textureSwapEntries
                                     ?? Array.Empty<MgsvModBldr.Tools.Fv2.Fv2.TextureSwapEntry>())
                    Add("textureSwap", Named32(swap.materialInstanceStrCode32),
                        $"{Named32(swap.textureTypeStrCode32)} -> texture #{swap.textureIndex}");
            }
        }

        // The files those indexes point into, so a swap can be traced to a file.
        var external = _file.externalFileEntries;
        for (var i = 0; external is not null && i < external.Length; i++)
            Add("file", $"#{i}", Named(external[i]));

        unresolved += missing;
        return rows;
    }
}
