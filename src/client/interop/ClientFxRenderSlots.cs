using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client-side effect list's carry-out over IShimFxClient
/// (shell_effects_list_client.cpp). Each member is one slot; a missing shim
/// draws nothing and answers as an empty view would.
/// </summary>
internal sealed unsafe class ClientFxRenderSlots(IFxClientArea fxClient) : IFxRender
{
    private const int NameBytes = 256;

    public int MaterialFind(string name)
    {
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = utf8)
            return fxClient.FxMaterialFind(p);
    }

    public void MaterialAddRef(int material)
    {
        fxClient.FxMaterialAddRef(material);
    }

    public void MaterialRelease(int material)
    {
        fxClient.FxMaterialRelease(material);
    }

    public string MaterialName(int material)
    {
        byte* buffer = stackalloc byte[NameBytes];
        buffer[0] = 0;
        return fxClient.FxMaterialName(material, buffer, NameBytes) < 0 ? string.Empty : new string((sbyte*)buffer);
    }

    public void DrawQuads(int material, ReadOnlySpan<FxVertex> vertices, bool byteColor)
    {
        if (vertices.IsEmpty)
            return;
        fixed (FxVertex* p = vertices)
            fxClient.FxDrawQuads(material, vertices.Length, (float*)p, byteColor ? 1 : 0);
    }

    public FxView View()
    {
        float* v = stackalloc float[7];
        fxClient.FxView(v);
        return new FxView(new Vector(v[0], v[1], v[2]), new Vector(v[3], v[4], v[5]), (int)v[6]);
    }

    public Vector MainViewOrigin()
    {
        float* v = stackalloc float[3];
        fxClient.FxMainViewOrigin(v);
        return new Vector(v[0], v[1], v[2]);
    }


    public int LocalPlayer() => fxClient.FxLocalPlayer();

    public QAngle ViewAngles()
    {
        float* v = stackalloc float[3];
        fxClient.FxViewAngles(v);
        return new QAngle(v[0], v[1], v[2]);
    }

    public int EntityAttachment(int entIndex, int attachment, out Vector origin, out string model)
    {
        origin = default;
        model = string.Empty;
        float* v = stackalloc float[3];
        byte* name = stackalloc byte[NameBytes];
        name[0] = 0;
        int found = fxClient.FxEntityAttachment(entIndex, attachment, v, name, NameBytes);
        if (found >= 0)
            origin = new Vector(v[0], v[1], v[2]);
        model = new string((sbyte*)name);
        return found;
    }

    public bool EntityRenderAngles(int entIndex, out QAngle angles)
    {
        angles = default;
        float* v = stackalloc float[3];
        if (fxClient.FxEntityRenderAngles(entIndex, v) == 0)
            return false;
        angles = new QAngle(v[0], v[1], v[2]);
        return true;
    }

    public void EjectBrass(Vector origin, QAngle angles, QAngle gunAngles, int type)
    {
        float* o = stackalloc float[] { origin.X, origin.Y, origin.Z };
        float* a = stackalloc float[] { angles.Pitch, angles.Yaw, angles.Roll };
        float* g = stackalloc float[] { gunAngles.Pitch, gunAngles.Yaw, gunAngles.Roll };
        fxClient.FxEjectBrass(o, a, g, type);
    }

    public void DispatchParticleEffect(int systemIndex, Vector start, Vector end, QAngle angles, int entIndex)
    {
        float* s = stackalloc float[] { start.X, start.Y, start.Z };
        float* e = stackalloc float[] { end.X, end.Y, end.Z };
        float* a = stackalloc float[] { angles.Pitch, angles.Yaw, angles.Roll };
        fxClient.FxDispatchParticleEffect(systemIndex, s, e, a, entIndex);
    }
}
