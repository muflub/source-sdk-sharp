
namespace SourceSharp.Abi;

/// <summary>
/// VPhysics' surface property table, <c>IPhysicsSurfaceProps</c> — the three
/// members the server's manifest load needs. plan_server_novalve.md P2.4: the
/// manifest is read in C# (SurfacePropertyManifest), and each file it names is
/// handed to VPhysics' own parser exactly as physics_shared.cpp's
/// PhysParseSurfaceData did.
/// </summary>
public interface IVtablePhysicsSurfaceProps
{
    /// <summary><c>ParseSurfaceData( pFileName, pTextfile )</c>: VPhysics parses one file's text.</summary>
    int ParseSurfaceData(string fileName, string text);

    /// <summary>How many surface properties VPhysics holds.</summary>
    int SurfacePropCount();

    /// <summary>The name at an index, or null.</summary>
    string? PropName(int index);

    /// <summary>
    /// <c>GetSurfaceIndex( pszName )</c>: a surfaceprop name to its index --
    /// what stock's hitbox trace asks of a bone's surfaceprop (bone_setup.cpp
    /// TraceToStudio). VPhysics answers its own default for a name it lacks.
    /// </summary>
    int SurfaceIndex(string name);
}

public sealed unsafe class PhysicsSurfaceProps : IVtablePhysicsSurfaceProps, IVPhysicsSurfaceSounds
{
    private const int MaxSoundName = 128;

    public const string InterfaceVersion = "VPhysicsSurfaceProps001";

    private readonly void* _self;
    private readonly PhysicsSurfacePropsVtbl* _vtbl;

    private PhysicsSurfaceProps(void* self, void** table)
    {
        _self = self;
        _vtbl = (PhysicsSurfacePropsVtbl*)table;
    }

    /// <summary>
    /// From the physics module's factory, through <paramref name="engine"/>'s
    /// own side (plan_abi_hotreload D8); null, with the reason, when it does
    /// not answer.
    /// </summary>
    public static IVtablePhysicsSurfaceProps? Acquire(ModuleEngine engine, out string why)
        => Vtable.TryAcquire<PhysicsSurfacePropsVtbl>(
               (engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces,
               SourceSharpFactoryType.Physics, InterfaceVersion, out void* self, out void** table, out why)
            ? new PhysicsSurfaceProps(self, table)
            : null;

    public int ParseSurfaceData(string fileName, string text)
    {
        byte[] name = Vtable.Utf8(fileName);
        byte[] body = Vtable.Utf8(text);

        fixed (byte* pName = name)
        fixed (byte* pBody = body)
            return _vtbl->ParseSurfaceData(_self, pName, pBody);
    }

    public int SurfacePropCount() => _vtbl->SurfacePropCount(_self);

    public string? PropName(int index) => Vtable.TextOf(_vtbl->GetPropName(_self, index), 128);

    public SurfaceImpactSounds? Sounds(int surfaceProps)
    {
        var data = (VPhysicsSurfaceData*)_vtbl->GetSurfaceData(_self, surfaceProps);
        if (data == null)
            return null;

        return new SurfaceImpactSounds(Name(data->ImpactHard), Name(data->ImpactSoft), Name(data->BreakSound),
                                       data->HardnessFactor, data->HardThreshold, data->HardVelocityThreshold);
    }

    // A string index: 0 is "no sound" (stock tests the index, not the text).
    private string? Name(ushort index)
        => index == 0 ? null : Vtable.TextOf(_vtbl->GetString(_self, index), MaxSoundName);

    public int SurfaceIndex(string name)
    {
        byte[] bytes = Vtable.Utf8(name);

        fixed (byte* p = bytes)
            return _vtbl->GetSurfaceIndex(_self, p);
    }
}
