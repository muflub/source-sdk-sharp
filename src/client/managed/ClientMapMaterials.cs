using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// The client's half of plan_map_load P1: reads the two maps' paks and asks
/// <see cref="MapMaterialReload"/> what the spawn must reload. The paths come
/// from native already resolved to files on disk (IFileSystem's own
/// RelativePathToFullPath), so this reads them directly: the client has no
/// read slot on the engine filesystem, and a map is a loose file. A map that
/// cannot be read is the full reload, never a guess.
/// </summary>
public sealed class ClientMapMaterials(ILog log)
{
    private readonly MapMaterialReload _policy = new();

    /// <summary>The materials to reload, newline-joined and possibly empty, or null for the full reload.</summary>
    public string? ReloadSet(string nextBsp, string previousBsp)
    {
        var next = Index(nextBsp);
        var previous = previousBsp.Length == 0 ? null : Index(previousBsp);
        var plan = _policy.Decide(next, previous, previousExpected: previousBsp.Length != 0);
        if (plan.Full)
        {
            log.Msg($"[SourceSharp] map materials: full reload for {Path.GetFileName(nextBsp)}\n");
            return null;
        }
        log.Msg($"[SourceSharp] map materials: {plan.Materials.Count} to reload for {Path.GetFileName(nextBsp)}\n");
        return string.Join('\n', plan.Materials);
    }

    private MapMaterialReload.PakIndex? Index(string bsp)
    {
        try
        {
            return _policy.Index(Path.GetFileNameWithoutExtension(bsp).ToLowerInvariant(), File.ReadAllBytes(bsp));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
