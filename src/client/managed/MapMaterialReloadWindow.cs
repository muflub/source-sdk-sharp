using System.Text;

namespace SourceSharp.Client;

/// <summary>
/// plan_map_load P1: the state machine between the three engine calls a listen
/// server's spawn makes, and what each <c>IMaterialSystem::ReloadMaterials</c>
/// comes to.
///
/// <para>
/// The shipped engine's <c>SV_SpawnServer</c> mounts the new map's pak
/// (<c>IFileSystem::AddSearchPath( "maps/&lt;map&gt;.bsp", "GAME",
/// PATH_ADD_TO_HEAD )</c>), opens map access (<c>BeginMapAccess</c>) and then
/// calls <c>materials-&gt;ReloadMaterials( NULL )</c>: every resident material
/// re-parsed, 0.56-0.94 s a load. Inside that window the spawn's full reload
/// becomes the targeted set <see cref="ClientMapMaterials"/> answers for the
/// two maps; anything it cannot answer is the stock full reload.
/// </para>
///
/// <para>
/// ARMED ONLY INSIDE A MAP LOAD. A pak mounted at the head of GAME and map
/// access open is the spawn's window; <c>EndMapAccess</c> closes it.
/// <c>mat_reloadallmaterials</c>, which reaches the same slot from the material
/// system's own command, runs between frames and never inside that window, so
/// it keeps its full reload. A substring reload is always passed through.
/// </para>
///
/// <para>
/// It was native (<c>CMapMaterialReload</c>, map_material_reload.h); the hooks
/// in shell_map_material_reload.cpp forward the three calls here and carry out
/// the plan.
/// </para>
/// </summary>
public sealed class MapMaterialReloadWindow
{
    /// <summary>The longest pak path the window holds, in bytes (native's MAX_PATH-sized buffer).</summary>
    public const int PathMax = 260;

    private readonly Func<string, string, string?> _reloadSet;

    private string _mounted = string.Empty;
    private string _previous = string.Empty;
    private bool _armed;

    /// <param name="reloadSet">
    /// The materials to reload when the first pak is mounted after the second
    /// (files on disk; empty: no previous map), newline-joined and possibly
    /// empty, or null for the full reload: <see cref="ClientMapMaterials.ReloadSet"/>.
    /// </param>
    public MapMaterialReloadWindow(Func<string, string, string?> reloadSet)
    {
        _reloadSet = reloadSet;
    }

    /// <summary>Whether the next full reload is the spawn's.</summary>
    public bool IsArmed => _armed;

    /// <summary>The pak the last planned spawn mounted; empty before the first.</summary>
    public string Previous => _previous;

    /// <summary>
    /// <c>IFileSystem::AddSearchPath</c>, after the engine's own. Only a map
    /// pak mounted at the head of GAME is the spawn's.
    /// </summary>
    public void OnAddSearchPath(string? path, string? pathId, bool addToHead)
    {
        if (path is null || pathId is null || !addToHead || !string.Equals(pathId, "GAME", StringComparison.Ordinal))
            return;

        int bytes = Encoding.UTF8.GetByteCount(path);
        if (bytes < 4 || bytes >= PathMax || !path.EndsWith(".bsp", StringComparison.Ordinal))
            return;

        _mounted = path;
    }

    /// <summary><c>IFileSystem::BeginMapAccess</c>: a mounted pak opens the window.</summary>
    public void OnBeginMapAccess() => _armed = _mounted.Length != 0;

    /// <summary><c>IFileSystem::EndMapAccess</c>: the window closes and the pak is forgotten.</summary>
    public void OnEndMapAccess()
    {
        _armed = false;
        _mounted = string.Empty;
    }

    /// <summary>
    /// <c>IMaterialSystem::ReloadMaterials( <paramref name="subString"/> )</c>:
    /// null to pass the call through unchanged (a substring, or a full reload
    /// outside the window, or the spawn's when no targeted set could be
    /// answered), else the newline-joined names to reload one by one, possibly
    /// empty. The window is spent either way, and this pak is the next spawn's
    /// previous map.
    /// </summary>
    public string? Plan(string? subString)
    {
        if (subString is not null || !_armed)
            return null;

        _armed = false;

        string? names = _reloadSet(_mounted, _previous);
        _previous = _mounted;
        return names;
    }
}
