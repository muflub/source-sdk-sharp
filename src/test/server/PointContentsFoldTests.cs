using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What is at a point, through IEngineTrace's forwarders on each side
/// (2026-10-19), where the PointContents shim slot made the same
/// <c>GetPointContents( vec, NULL )</c> call. Each side asks for its own
/// object by its own name: the server's EngineTraceServer003, the client's
/// EngineTraceClient003.
/// </summary>
public sealed unsafe class PointContentsFoldTests : System.IDisposable
{
    private static float s_X, s_Y, s_Z;
    private static nint s_Entity = -1;

    [UnmanagedCallersOnly]
    private static int Contents(void* self, void* point, void* entity)
    {
        var v = (float*)point;
        (s_X, s_Y, s_Z) = (v[0], v[1], v[2]);
        s_Entity = (nint)entity;
        return 0x20;   // CONTENTS_WATER
    }

    private readonly FakeShimRoot _root = new();
    private readonly nint* _self = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));

    public void Dispose()
    {
        _root.Dispose();
        NativeMemory.Free(_self);
    }

    private IEngineWorldQuery World(Side side, string version)
    {
        _root.Interface(version, _self).Forwarders(out EngineTraceFwd* fwd);
        fwd->GetPointContents = &Contents;
        var api = new EngineSide(side);
        api.Initialize();
        string why;
        bool ok = api.AttachOwnShims(_root.Bind(), out why);
        Assert.True(ok, why);
        return SideBindings.Of<IEngineWorldQuery>(api);
    }

    [Fact]
    public void TheServerAsksItsOwnTraceForThePoint()
    {
        Contents c = World(Side.Server, EngineTrace.ServerInterfaceVersion).PointContents(new Vector(1, 2, 3));

        Assert.Equal((Contents)0x20, c);
        Assert.Equal((1f, 2f, 3f, (nint)0), (s_X, s_Y, s_Z, s_Entity));
    }

    [Fact]
    public void TheClientAsksItsOwnTraceForThePoint()
    {
        Contents c = World(Side.Client, EngineTrace.ClientInterfaceVersion).PointContents(new Vector(-4, 5, 6));

        Assert.Equal((Contents)0x20, c);
        Assert.Equal((-4f, 5f, 6f), (s_X, s_Y, s_Z));
    }
}
