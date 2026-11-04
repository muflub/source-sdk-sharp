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
