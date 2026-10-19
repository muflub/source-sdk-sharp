using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>An entity's drawn pose, and whether it has a model (IShimViewClient's ClientEntityRenderPose).</summary>
public readonly record struct EntityRenderPose(Vector Origin, QAngle Angles, bool HasModel);

/// <summary>One camera hull trace's answer.</summary>
public readonly record struct CameraTrace(bool Hit, float Fraction, Vector End);

/// <summary>
/// What the observer's camera asks native and tells it (plan_client_novalve C7
/// item 5): a hull trace with a stock filter, an entity's drawn pose by index,
/// and the view the client mode's OverrideView draws this frame.
/// </summary>
public interface IObserverCameraSlots
{
    CameraTrace TraceHull(Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask, TraceFilter filter, int passIndex);

    EntityRenderPose? RenderPose(int entityIndex);

    /// <summary>The view to draw instead of the player's eye; inactive hands it back.</summary>
    void SetView(bool active, Vector origin, QAngle angles, bool setViewAngles);
}
