using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Flashlight;

/// <summary>
/// <see cref="IFlashlightWorld"/> over the flashlight's slots and the engine's
/// dynamic-light pool: each call carried straight through.
/// </summary>
public sealed class FlashlightWorld(IFlashlightSlots slots, IEngineVisibility visibility, Render.Shadows.IFlashlightShadows shadows)
    : IFlashlightWorld
{
    public int? DXSupportLevel() => slots.DXSupportLevel();

    public bool SupportsBorderColor() => slots.SupportsBorderColor();

    public int Texture(string name) => slots.Texture(name);

    // The projected texture is the managed shadow manager's (C7 cluster 12 S2): in-process.
    public int Create(in FlashlightState state) => shadows.CreateFlashlight(in state);

    public void Update(int handle, in FlashlightState state) => shadows.UpdateFlashlightState(handle, in state);

    public void Project(int handle) => shadows.ProjectFlashlight(handle);

    public void Destroy(int handle) => shadows.DestroyFlashlight(handle);

    public FlashlightTrace TraceHull(Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask)
        => slots.TraceHull(start, end, mins, maxs, mask);

    public FlashlightTrace TraceLine(Vector start, Vector end, TraceMask mask) => slots.TraceLine(start, end, mask);

    public void BoxOverlay(Vector origin, Vector mins, Vector maxs, QAngle orientation, int r, int g, int b, int a,
                           float duration)
        => slots.BoxOverlay(origin, mins, maxs, orientation, r, g, b, a, duration);

    public void LineOverlay(Vector origin, Vector dest, int r, int g, int b, bool noDepthTest, float duration)
        => slots.LineOverlay(origin, dest, r, g, b, noDepthTest, duration);

    public void DynamicLightUpdate(in DynamicLight light) => visibility.DynamicLightUpdate(in light);

    public void DynamicLightRelease(int key) => visibility.DynamicLightRelease(key);
}

/// <summary>
/// ConVars read by name through the convar watch (IShimConVarsClient): each
/// value is read once when watched and kept current by the change callback
/// the watch forwards, so a per-frame read is a dictionary lookup rather than
/// a crossing. ConVar's own conversions: GetFloat is (float)atof, GetInt the
/// float truncated, GetBool GetInt != 0.
/// </summary>
public sealed class WatchedConVarValues
{
    private readonly IClientConVarSlots _slots;
    private readonly Func<string, string?> _read;
    private readonly Dictionary<int, string> _byId = new();
    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

    public WatchedConVarValues(IClientConVarSlots slots, Func<string, string?> read)
    {
        _slots = slots;
        _read = read;
    }

    /// <summary>How many of the names the watch answered.</summary>
    public int Watched => _byId.Count;

    /// <summary>Watches each name and reads its value now.</summary>
    public void Watch(IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            int id = _slots.Watch(name);
            if (id >= 0)
                _byId[id] = name;
            _values[name] = _read(name);
        }
    }

    /// <summary>A watched convar changed; false for an id this set does not hold.</summary>
    public bool OnChanged(int id, string newValue)
    {
        if (!_byId.TryGetValue(id, out string? name))
            return false;
        _values[name] = newValue;
        return true;
    }

    public string? Raw(string name) => _values.GetValueOrDefault(name);

    public float Float(string name) => VariantParse.Atof(Raw(name));

    public int Int(string name) => (int)Float(name);

    public bool Bool(string name) => Int(name) != 0;
}
