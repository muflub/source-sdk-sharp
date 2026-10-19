using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real slots.</summary>
internal sealed unsafe class ObserverCameraSlots(EngineSide api) : IObserverCameraSlots
{
    public CameraTrace TraceHull(Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask, TraceFilter filter, int passIndex)
    {
        if (!api.ClientAttached)
            return new CameraTrace(false, 1f, end);
        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* lo = stackalloc float[3] { mins.X, mins.Y, mins.Z };
        float* hi = stackalloc float[3] { maxs.X, maxs.Y, maxs.Z };
        float* endPos = stackalloc float[3];
        float fraction = 1f;
        int hit = api.ClientShims.ClientTraceHull(s, e, lo, hi, (int)mask, (int)filter, passIndex, &fraction, endPos);
        return new CameraTrace(hit != 0, fraction, new Vector(endPos[0], endPos[1], endPos[2]));
    }

    public EntityRenderPose? RenderPose(int entityIndex)
    {
        if (!api.ClientAttached)
            return null;
        float* o = stackalloc float[3];
        float* a = stackalloc float[3];
        int answer = api.ClientShims.ClientEntityRenderPose(entityIndex, o, a);
        if (answer < 0)
            return null;
        return new EntityRenderPose(new Vector(o[0], o[1], o[2]), new QAngle(a[0], a[1], a[2]), answer != 0);
    }

    public void SetView(bool active, Vector origin, QAngle angles, bool setViewAngles)
    {
        if (!api.ClientAttached)
            return;
        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        float* a = stackalloc float[3] { angles.Pitch, angles.Yaw, angles.Roll };
        api.ClientShims.ViewOverrideSet(active ? 1 : 0, o, a, setViewAngles ? 1 : 0);
    }
}
