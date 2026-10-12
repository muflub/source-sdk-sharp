using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client-side effect list's carry-out over IShimFxClient
/// (shell_effects_list_client.cpp). Each member is one slot; a missing shim
/// draws nothing and answers as an empty view would.
/// </summary>
internal sealed unsafe class ClientFxRenderSlots(EngineSide api) : IFxRender
{
    private const int NameBytes = 256;

    public int MaterialFind(string name)
    {
        if (!api.ClientAttached)
            return 0;
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = utf8)
            return api.ClientShims.FxMaterialFind(p);
    }

    public void MaterialAddRef(int material)
    {
        if (api.ClientAttached)
            api.ClientShims.FxMaterialAddRef(material);
    }

    public void MaterialRelease(int material)
    {
        if (api.ClientAttached)
            api.ClientShims.FxMaterialRelease(material);
    }

    public string MaterialName(int material)
    {
        if (!api.ClientAttached)
            return string.Empty;
        byte* buffer = stackalloc byte[NameBytes];
        buffer[0] = 0;
        return api.ClientShims.FxMaterialName(material, buffer, NameBytes) < 0 ? string.Empty : new string((sbyte*)buffer);
    }

    public void DrawQuads(int material, ReadOnlySpan<FxVertex> vertices, bool byteColor)
    {
        if (!api.ClientAttached || vertices.IsEmpty)
            return;
        fixed (FxVertex* p = vertices)
            api.ClientShims.FxDrawQuads(material, vertices.Length, (float*)p, byteColor ? 1 : 0);
    }

    public FxView View()
    {
        if (!api.ClientAttached)
            return default;
        float* v = stackalloc float[7];
        api.ClientShims.FxView(v);
        return new FxView(new Vector(v[0], v[1], v[2]), new Vector(v[3], v[4], v[5]), (int)v[6]);
    }

    public Vector MainViewOrigin()
    {
        if (!api.ClientAttached)
            return default;
        float* v = stackalloc float[3];
        api.ClientShims.FxMainViewOrigin(v);
        return new Vector(v[0], v[1], v[2]);
    }

    public bool EmitTracerSound(string soundName, Vector start, Vector direction)
    {
        if (!api.ClientAttached)
            return false;
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(soundName + "\0");
        float* s = stackalloc float[] { start.X, start.Y, start.Z };
        float* d = stackalloc float[] { direction.X, direction.Y, direction.Z };
        fixed (byte* p = utf8)
            return api.ClientShims.FxEmitTracerSound(p, s, d) != 0;
    }

    public int LocalPlayer() => api.ClientAttached ? api.ClientShims.FxLocalPlayer() : 0;

    public QAngle ViewAngles()
    {
        if (!api.ClientAttached)
            return default;
        float* v = stackalloc float[3];
        api.ClientShims.FxViewAngles(v);
        return new QAngle(v[0], v[1], v[2]);
    }

    public int EntityAttachment(int entIndex, int attachment, out Vector origin, out string model)
    {
        origin = default;
        model = string.Empty;
        if (!api.ClientAttached)
            return -1;
        float* v = stackalloc float[3];
        byte* name = stackalloc byte[NameBytes];
        name[0] = 0;
        int found = api.ClientShims.FxEntityAttachment(entIndex, attachment, v, name, NameBytes);
        if (found >= 0)
            origin = new Vector(v[0], v[1], v[2]);
        model = new string((sbyte*)name);
        return found;
    }

    public bool EntityRenderAngles(int entIndex, out QAngle angles)
    {
        angles = default;
        if (!api.ClientAttached)
            return false;
        float* v = stackalloc float[3];
        if (api.ClientShims.FxEntityRenderAngles(entIndex, v) == 0)
            return false;
        angles = new QAngle(v[0], v[1], v[2]);
        return true;
    }

    public void EjectBrass(Vector origin, QAngle angles, QAngle gunAngles, int type)
    {
        if (!api.ClientAttached)
            return;
        float* o = stackalloc float[] { origin.X, origin.Y, origin.Z };
        float* a = stackalloc float[] { angles.Pitch, angles.Yaw, angles.Roll };
        float* g = stackalloc float[] { gunAngles.Pitch, gunAngles.Yaw, gunAngles.Roll };
        api.ClientShims.FxEjectBrass(o, a, g, type);
    }

    public void DispatchParticleEffect(int systemIndex, Vector start, Vector end, QAngle angles, int entIndex)
    {
        if (!api.ClientAttached)
            return;
        float* s = stackalloc float[] { start.X, start.Y, start.Z };
        float* e = stackalloc float[] { end.X, end.Y, end.Z };
        float* a = stackalloc float[] { angles.Pitch, angles.Yaw, angles.Roll };
        api.ClientShims.FxDispatchParticleEffect(systemIndex, s, e, a, entIndex);
    }
}
