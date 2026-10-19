using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client-side effect list's carry-out over IShimFxClient
/// (shell_effects_list_client.cpp). Each member is one slot; a missing shim
/// draws nothing and answers as an empty view would.
/// </summary>
internal sealed unsafe class ClientFxRenderSlots(FxClientArea? fxClient) : IFxRender
{
    // The shim areas this calls, taken by constructor (plan_shim_areas_split
    // phase 5). Null on a client that never attached (an unattached engine
    // double): a guarded call answers its default and any other throws, as
    // they did through the side's attach guard.
    private FxClientArea FxClient => fxClient ?? throw ShimAreaGuard.NotAttached(nameof(FxClient));

    private const int NameBytes = 256;

    public int MaterialFind(string name)
    {
        if (fxClient is null)
            return 0;
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = utf8)
            return FxClient.FxMaterialFind(p);
    }

    public void MaterialAddRef(int material)
    {
        if (fxClient is not null)
            FxClient.FxMaterialAddRef(material);
    }

    public void MaterialRelease(int material)
    {
        if (fxClient is not null)
            FxClient.FxMaterialRelease(material);
    }

    public string MaterialName(int material)
    {
        if (fxClient is null)
            return string.Empty;
        byte* buffer = stackalloc byte[NameBytes];
        buffer[0] = 0;
        return FxClient.FxMaterialName(material, buffer, NameBytes) < 0 ? string.Empty : new string((sbyte*)buffer);
    }

    public void DrawQuads(int material, ReadOnlySpan<FxVertex> vertices, bool byteColor)
    {
        if (fxClient is null || vertices.IsEmpty)
            return;
        fixed (FxVertex* p = vertices)
            FxClient.FxDrawQuads(material, vertices.Length, (float*)p, byteColor ? 1 : 0);
    }

    public FxView View()
    {
        if (fxClient is null)
            return default;
        float* v = stackalloc float[7];
        FxClient.FxView(v);
        return new FxView(new Vector(v[0], v[1], v[2]), new Vector(v[3], v[4], v[5]), (int)v[6]);
    }

    public Vector MainViewOrigin()
    {
        if (fxClient is null)
            return default;
        float* v = stackalloc float[3];
        FxClient.FxMainViewOrigin(v);
        return new Vector(v[0], v[1], v[2]);
    }

    public bool EmitTracerSound(string soundName, Vector start, Vector direction)
    {
        if (fxClient is null)
            return false;
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(soundName + "\0");
        float* s = stackalloc float[] { start.X, start.Y, start.Z };
        float* d = stackalloc float[] { direction.X, direction.Y, direction.Z };
        fixed (byte* p = utf8)
            return FxClient.FxEmitTracerSound(p, s, d) != 0;
    }

    public int LocalPlayer() => (fxClient is not null) ? FxClient.FxLocalPlayer() : 0;

    public QAngle ViewAngles()
    {
        if (fxClient is null)
            return default;
        float* v = stackalloc float[3];
        FxClient.FxViewAngles(v);
        return new QAngle(v[0], v[1], v[2]);
    }

    public int EntityAttachment(int entIndex, int attachment, out Vector origin, out string model)
    {
        origin = default;
        model = string.Empty;
        if (fxClient is null)
            return -1;
        float* v = stackalloc float[3];
        byte* name = stackalloc byte[NameBytes];
        name[0] = 0;
        int found = FxClient.FxEntityAttachment(entIndex, attachment, v, name, NameBytes);
        if (found >= 0)
            origin = new Vector(v[0], v[1], v[2]);
        model = new string((sbyte*)name);
        return found;
    }

    public bool EntityRenderAngles(int entIndex, out QAngle angles)
    {
        angles = default;
        if (fxClient is null)
            return false;
        float* v = stackalloc float[3];
        if (FxClient.FxEntityRenderAngles(entIndex, v) == 0)
            return false;
        angles = new QAngle(v[0], v[1], v[2]);
        return true;
    }

    public void EjectBrass(Vector origin, QAngle angles, QAngle gunAngles, int type)
    {
        if (fxClient is null)
            return;
        float* o = stackalloc float[] { origin.X, origin.Y, origin.Z };
        float* a = stackalloc float[] { angles.Pitch, angles.Yaw, angles.Roll };
        float* g = stackalloc float[] { gunAngles.Pitch, gunAngles.Yaw, gunAngles.Roll };
        FxClient.FxEjectBrass(o, a, g, type);
    }

    public void DispatchParticleEffect(int systemIndex, Vector start, Vector end, QAngle angles, int entIndex)
    {
        if (fxClient is null)
            return;
        float* s = stackalloc float[] { start.X, start.Y, start.Z };
        float* e = stackalloc float[] { end.X, end.Y, end.Z };
        float* a = stackalloc float[] { angles.Pitch, angles.Yaw, angles.Roll };
        FxClient.FxDispatchParticleEffect(systemIndex, s, e, a, entIndex);
    }
}
