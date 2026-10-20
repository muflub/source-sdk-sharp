
namespace SourceSharp.Abi;

/// <summary>
/// VPhysics' surface property table, <c>IPhysicsSurfaceProps</c> — the three
/// members the server's manifest load needs. plan_server_novalve.md P2.4: the
/// manifest is read in C# (SurfacePropertyManifest), and each file it names is
/// handed to VPhysics' own parser exactly as physics_shared.cpp's
/// PhysParseSurfaceData did.
/// </summary>
public interface IFwdPhysicsSurfaceProps
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

    /// <summary>
    /// <c>GetSurfaceData( n )->game.material</c>, or 0 for an index the
    /// physics module knows nothing of.
    /// </summary>
    int GameMaterial(int surfaceProps);

    /// <summary>
    /// One surfaceprop as the movement loop and the friction sounds read it:
    /// <c>GetSurfaceData( n )</c>'s game and audio fields, and the friction and
    /// density <c>GetPhysicsProperties</c> answers. <c>default</c> (not valid)
    /// for an index the physics module knows nothing of.
    /// </summary>
    MoveSurface Surface(int surfaceProps);

    /// <summary>
    /// <c>sounds.scrapeSmooth</c> or <c>sounds.scrapeRough</c>'s name, or null
    /// when the surface has none.
    /// </summary>
    string? ScrapeSound(int surfaceProps, bool smooth);
}

public sealed unsafe class PhysicsSurfaceProps : IFwdPhysicsSurfaceProps, IVPhysicsSurfaceSounds
{
    private const int MaxSoundName = 128;

    public const string InterfaceVersion = "VPhysicsSurfaceProps001";

    private readonly void* _self;
    private readonly PhysicsSurfacePropsFwd* _fwd;

    private PhysicsSurfaceProps(void* self, PhysicsSurfacePropsFwd* table)
    {
        _self = self;
        _fwd = table;
    }

    /// <summary>
    /// From the physics module's factory, through <paramref name="engine"/>'s
    /// own side (plan_abi_hotreload D8); null, with the reason, when it does
    /// not answer.
    /// </summary>
    public static IFwdPhysicsSurfaceProps? Acquire(ModuleEngine engine, out string why)
        => Acquire((engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces, out why);

    /// <summary>The same, through one side's own factory: what EngineBound holds per side.</summary>
    internal static IFwdPhysicsSurfaceProps? Acquire(NativeInterfaceFactory factory, out string why)
        => Forwarders.TryAcquire<PhysicsSurfacePropsFwd>(
               factory, SourceSharpFactoryType.Physics, InterfaceVersion,
               out void* self, out PhysicsSurfacePropsFwd* table, out why)
            ? new PhysicsSurfaceProps(self, table)
            : null;

    public int ParseSurfaceData(string fileName, string text)
    {
        byte[] name = Vtable.Utf8(fileName);
        byte[] body = Vtable.Utf8(text);

        fixed (byte* pName = name)
        fixed (byte* pBody = body)
            return _fwd->ParseSurfaceData(_self, pName, pBody);
    }

    public int SurfacePropCount() => _fwd->SurfacePropCount(_self);

    public string? PropName(int index) => Vtable.TextOf(_fwd->GetPropName(_self, index), 128);

    public SurfaceImpactSounds? Sounds(int surfaceProps)
    {
        var data = (VPhysicsSurfaceData*)_fwd->GetSurfaceData(_self, surfaceProps);
        if (data == null)
            return null;

        return new SurfaceImpactSounds(Name(data->ImpactHard), Name(data->ImpactSoft), Name(data->BreakSound),
                                       data->HardnessFactor, data->HardThreshold, data->HardVelocityThreshold);
    }

    // A string index: 0 is "no sound" (stock tests the index, not the text).
    private string? Name(ushort index)
        => index == 0 ? null : Vtable.TextOf(_fwd->GetString(_self, index), MaxSoundName);

    public int SurfaceIndex(string name)
    {
        byte[] bytes = Vtable.Utf8(name);

        fixed (byte* p = bytes)
            return _fwd->GetSurfaceIndex(_self, p);
    }

    public int GameMaterial(int surfaceProps)
    {
        var data = (VPhysicsSurfaceData*)_fwd->GetSurfaceData(_self, surfaceProps);
        return data == null ? 0 : data->GameMaterial;
    }

    public MoveSurface Surface(int surfaceProps)
    {
        var data = (VPhysicsSurfaceData*)_fwd->GetSurfaceData(_self, surfaceProps);
        if (data == null)
            return default;

        float density = 0f, friction = 0f;
        _fwd->GetPhysicsProperties(_self, surfaceProps, &density, null, &friction, null);

        return new MoveSurface
        {
            Valid = true,
            Friction = friction,
            Material = data->GameMaterial,
            Climbable = data->Climbable,
            MaxSpeedFactor = data->MaxSpeedFactor,
            JumpFactor = data->JumpFactor,
            Density = density,
            RoughnessFactor = data->RoughnessFactor,
            RoughThreshold = data->RoughThreshold,
        };
    }

    public string? ScrapeSound(int surfaceProps, bool smooth)
    {
        var data = (VPhysicsSurfaceData*)_fwd->GetSurfaceData(_self, surfaceProps);
        if (data == null)
            return null;

        return Name(smooth ? data->ScrapeSmooth : data->ScrapeRough);
    }
}
