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
    private static int GetIndex(void* self, byte* name)
        => Marshal.PtrToStringUTF8((nint)name) == "glass" ? 5 : -1;

    private readonly FakeShimRoot _root = new();
    private readonly nint* _self = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));
    private readonly EngineWorldQuery _world;

    public SurfacePropFoldTests()
    {
        s_Data = (byte*)NativeMemory.AllocZeroed(VPhysicsSurfaceData.SizeInBytes);
        *(ushort*)(s_Data + 72) = (ushort)'Y';   // CHAR_TEX_GLASS
        s_Asked = -1;

        _root.Interface(PhysicsSurfaceProps.InterfaceVersion, _self).Forwarders(out PhysicsSurfacePropsFwd* fwd);
        fwd->GetSurfaceData = &GetData;
        fwd->GetSurfaceIndex = &GetIndex;

        var side = new EngineSide(Side.Server);
        side.Initialize();
        Assert.True(side.AttachShims(_root.Bind(), out string why), why);
        _world = new EngineWorldQuery(side);
    }

    public void Dispose()
    {
        _root.Dispose();
        NativeMemory.Free(_self);
        NativeMemory.Free(s_Data);
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
}
