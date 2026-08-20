// SPDX-License-Identifier: MIT
namespace FoxAnimRip;

/// <summary>
/// Folds several single-clip FBX files into one file with several takes.
///
/// Writing one file per clip means Blender pays the whole FBX import cost --
/// parse, build an armature, build 139 bones, throw it all away -- once per
/// clip. Four thousand times that is most of an afternoon. FBX has supported
/// multiple AnimationStacks forever and Blender's importer creates one action
/// per stack, so packing 50 clips into a file cuts the overhead by 50x.
///
/// The merge is structural. Every animation object from the incoming file gets
/// a fresh id so nothing collides, its connections are rewritten to point at the
/// *base* file's bone models (matched by name, since both files describe the
/// same skeleton), and the stack is renamed to the clip. Nothing is re-encoded:
/// the key arrays move across as the bytes FoxBrowser wrote.
/// </summary>
public static class FbxTakes
{
    private static readonly string[] AnimationClasses =
    {
        "AnimationStack", "AnimationLayer", "AnimationCurveNode", "AnimationCurve",
    };

    /// <summary>Build one document from several single-clip exports.</summary>
    public static byte[] Pack(IReadOnlyList<(string Take, byte[] Fbx)> clips)
    {
        if (clips.Count == 0) throw new ArgumentException("nothing to pack");

        var doc = FbxDoc.Parse(clips[0].Fbx);
        RenameStacks(doc, clips[0].Take);

        for (var i = 1; i < clips.Count; i++)
            MergeInto(doc, FbxDoc.Parse(clips[i].Fbx), clips[i].Take);

        UpdateDefinitions(doc);
        return doc.Serialize();
    }

    private static void RenameStacks(FbxDoc doc, string take)
    {
        var objects = doc.Root("Objects");
        if (objects is null) return;
        foreach (var node in objects.Children)
        {
            if (node.NameText != "AnimationStack") continue;
            node.SetStringAt(1, take + "\0AnimStack");
        }
    }

    private static void MergeInto(FbxDoc target, FbxDoc source, string take)
    {
        var targetObjects = target.Root("Objects");
        var sourceObjects = source.Root("Objects");
        var targetConnections = target.Root("Connections");
        var sourceConnections = source.Root("Connections");
        if (targetObjects is null || sourceObjects is null
            || targetConnections is null || sourceConnections is null)
            return;

        // Bone/mesh models are identical in both files; match them by name so
        // the incoming curve nodes can be pointed at the ones we are keeping.
        var targetModels = new Dictionary<string, long>(StringComparer.Ordinal);
        long nextId = 1;
        foreach (var node in targetObjects.Children)
        {
            var id = node.Int64At(0);
            if (id is null) continue;
            if (id.Value >= nextId) nextId = id.Value + 1;
            if (node.NameText == "Model")
            {
                var name = node.ObjectName();
                if (name is not null) targetModels[name] = id.Value;
            }
        }

        var remap = new Dictionary<long, long>();
        var carried = new List<FbxNode>();

        foreach (var node in sourceObjects.Children)
        {
            if (!AnimationClasses.Contains(node.NameText)) continue;
            var id = node.Int64At(0);
            if (id is null) continue;

            var clone = node.Clone();
            var newId = nextId++;
            remap[id.Value] = newId;
            clone.SetInt64At(0, newId);
            if (clone.NameText == "AnimationStack")
                clone.SetStringAt(1, take + "\0AnimStack");
            carried.Add(clone);
        }

        if (carried.Count == 0) return;

        // Models in the source that we are *not* carrying: map them onto the
        // equivalent object already in the target.
        foreach (var node in sourceObjects.Children)
        {
            if (node.NameText != "Model") continue;
            var id = node.Int64At(0);
            var name = node.ObjectName();
            if (id is null || name is null) continue;
            if (targetModels.TryGetValue(name, out var existing)) remap[id.Value] = existing;
        }

        targetObjects.Children.AddRange(carried);

        foreach (var connection in sourceConnections.Children)
        {
            if (connection.NameText != "C" || connection.Props.Count < 3) continue;
            var child = connection.Int64At(1);
            var parent = connection.Int64At(2);
            if (child is null || parent is null) continue;

            // Only keep connections that involve something we carried over,
            // and only when both ends resolve in the merged document.
            var childCarried = carried.Any(c => c.Int64At(0) == remap.GetValueOrDefault(child.Value));
            var parentCarried = carried.Any(c => c.Int64At(0) == remap.GetValueOrDefault(parent.Value));
            if (!childCarried && !parentCarried) continue;
            if (!remap.TryGetValue(child.Value, out var newChild)) continue;
            if (!remap.TryGetValue(parent.Value, out var newParent)) continue;

            var clone = connection.Clone();
            clone.SetInt64At(1, newChild);
            clone.SetInt64At(2, newParent);
            targetConnections.Children.Add(clone);
        }
    }

    /// <summary>Keep the Definitions counts honest; some readers trust them.</summary>
    private static void UpdateDefinitions(FbxDoc doc)
    {
        var definitions = doc.Root("Definitions");
        var objects = doc.Root("Objects");
        if (definitions is null || objects is null) return;

        var counts = objects.Children
            .GroupBy(c => c.NameText)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var total = 0;
        foreach (var entry in definitions.Children)
        {
            if (entry.NameText != "ObjectType") continue;
            var type = entry.StringAt(0);
            if (type is null || !counts.TryGetValue(type, out var count)) continue;
            var countNode = entry.Child("Count");
            if (countNode is null || countNode.Props.Count == 0) continue;
            var buf = new byte[5];
            buf[0] = (byte)'I';
            BitConverter.GetBytes(count).CopyTo(buf, 1);
            countNode.Props[0] = buf;
            total += count;
        }

        var totalNode = definitions.Child("Count");
        if (totalNode is { Props.Count: > 0 } && total > 0)
        {
            var buf = new byte[5];
            buf[0] = (byte)'I';
            BitConverter.GetBytes(objects.Children.Count).CopyTo(buf, 1);
            totalNode.Props[0] = buf;
        }
    }
}
