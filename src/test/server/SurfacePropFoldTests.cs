using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A surfaceprop's index and game material through IPhysicsSurfaceProps'
/// forwarders (2026-10-19), where the SurfacePropIndex and
/// SurfacePropGameMaterial slots made the same calls: the material is
/// <c>game.material</c> at offset 72 of surfacedata_t, and the slots' guards
/// (an empty name is -1, a negative index is material 0) are the binding's.
/// The movement loop's surface and the friction sounds' scrape names followed
/// the same day, where the MoveSurface and SurfaceScrapeSound slots read the
/// same fields.
/// </summary>
public sealed unsafe class SurfacePropFoldTests : System.IDisposable
{
    private static byte* s_Data;
    private static int s_Asked = -1;

    [UnmanagedCallersOnly]
    private static void* GetData(void* self, int index)
    {
        s_Asked = index;
        return index == 5 ? s_Data : null;
    }

    [UnmanagedCallersOnly]
    private static byte* GetString(void* self, ushort index)
        => index == 11 ? s_Smooth : index == 12 ? s_Rough : null;

    [UnmanagedCallersOnly]
    private static void GetPhysics(void* self, int index, void* density, void* thickness, void* friction, void* elasticity)
    {
        *(float*)density = 2700f;
        *(float*)friction = 0.4f;
    }

    private static byte* s_Smooth, s_Rough;

    [UnmanagedCallersOnly]
    private static int GetIndex(void* self, byte* name)
        => Marshal.PtrToStringUTF8((nint)name) == "glass" ? 5 : -1;

    private readonly FakeShimRoot _root = new();
    private readonly nint* _self = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));
    private readonly IEngineWorldQuery _world;
    private readonly IEngineSound _sound;
    private readonly PhysicsSurfaces _surfaces;

    public SurfacePropFoldTests()
    {
        s_Data = (byte*)NativeMemory.AllocZeroed(VPhysicsSurfaceData.SizeInBytes);
        *(ushort*)(s_Data + 72) = (ushort)'Y';   // CHAR_TEX_GLASS
        *(float*)(s_Data + 28) = 0.5f;           // audio.roughnessFactor
        *(float*)(s_Data + 32) = 0.25f;          // audio.roughThreshold
        *(ushort*)(s_Data + 52) = 11;            // sounds.scrapeSmooth
        *(ushort*)(s_Data + 54) = 12;            // sounds.scrapeRough
        *(float*)(s_Data + 64) = 0.9f;           // game.maxSpeedFactor
        *(float*)(s_Data + 68) = 1.5f;           // game.jumpFactor
        s_Data[74] = 1;                          // game.climbable
        s_Smooth = (byte*)Marshal.StringToCoTaskMemUTF8("Glass.ScrapeSmooth");
        s_Rough = (byte*)Marshal.StringToCoTaskMemUTF8("Glass.ScrapeRough");
        s_Asked = -1;

        _root.Interface(PhysicsSurfaceProps.InterfaceVersion, _self).Forwarders(out PhysicsSurfacePropsFwd* fwd);
        fwd->GetSurfaceData = &GetData;
        fwd->GetSurfaceIndex = &GetIndex;
        fwd->GetString = &GetString;
        fwd->GetPhysicsProperties = &GetPhysics;

        var side = TestSides.Create(Side.Server);
        side.Initialize();
        Assert.True(side.AttachOwnShims(_root.Bind(), out string why), why);
        _world = SideBindings.Of<IEngineWorldQuery>(side);
        _sound = SideBindings.Of<IEngineSound>(side);
        _surfaces = new PhysicsSurfaces(EngineBound.SurfaceProps(side));
    }

    public void Dispose()
    {
        _root.Dispose();
        NativeMemory.Free(_self);
        NativeMemory.Free(s_Data);
        Marshal.FreeCoTaskMem((nint)s_Smooth);
        Marshal.FreeCoTaskMem((nint)s_Rough);
    }

    [Fact]
    public void TheMaterialIsGameMaterialAtOffset72()
        => Assert.Equal((SurfaceMaterial)'Y', _world.SurfacePropGameMaterial(5));

    [Fact]
    public void AnIndexTheModuleDoesNotKnowIsMaterialZero()
        => Assert.Equal((SurfaceMaterial)0, _world.SurfacePropGameMaterial(9));

    [Fact]
    public void ANegativeIndexAsksNothing()
    {
        Assert.Equal((SurfaceMaterial)0, _world.SurfacePropGameMaterial(-1));
        Assert.Equal(-1, s_Asked);
    }

    [Fact]
    public void ANameResolvesThroughTheModuleAndAnEmptyOneIsMinusOne()
    {
        Assert.Equal(5, _world.SurfacePropIndex("glass"));
        Assert.Equal(-1, _world.SurfacePropIndex(""));
    }

    [Fact]
    public void TheScrapeSoundIsTheSmoothOrRoughNameAsAsked()
    {
        Assert.Equal("Glass.ScrapeSmooth", _sound.SurfaceScrapeSound(5, smooth: true));
        Assert.Equal("Glass.ScrapeRough", _sound.SurfaceScrapeSound(5, smooth: false));
    }

    [Fact]
    public void ASurfaceWithNoScrapeSoundHasNone()
    {
        *(ushort*)(s_Data + 54) = 0;
        Assert.Null(_sound.SurfaceScrapeSound(5, smooth: false));
        Assert.Null(_sound.SurfaceScrapeSound(9, smooth: true));
    }

    [Fact]
    public void TheMoveSurfaceIsEveryFieldTheMovementLoopReads()
    {
        MoveSurface s = _surfaces.Of(5);

        Assert.True(s.Valid);
        Assert.Equal(('Y', 1, 0.9f, 1.5f), ((char)s.Material, s.Climbable, s.MaxSpeedFactor, s.JumpFactor));
        Assert.Equal((0.4f, 2700f), (s.Friction, s.Density));
        Assert.Equal((0.5f, 0.25f), (s.RoughnessFactor, s.RoughThreshold));
    }

    [Fact]
    public void AnUnknownSurfaceIsNotValid()
        => Assert.False(_surfaces.Of(9).Valid);
}
