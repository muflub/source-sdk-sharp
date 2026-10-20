using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's static prop manager: Client.Interop's wrapper over
/// IStaticPropMgrClient and libss_fwd_client's table, which the client's world
/// build asks through <see cref="IStaticPropPhysics"/> (stock's client
/// PhysicsLevelInit made the static props' objects after the world's).
/// </summary>
public sealed unsafe class StaticPropMgrClientTests
{
    private static nint s_Self, s_Environment, s_KeyHandler, s_GameData;
    private static int s_Calls;

    [UnmanagedCallersOnly]
    private static void CreateRecords(void* self, void* environment, void* keyHandler, void* gameData)
    {
        s_Calls++;
        s_Self = (nint)self;
        s_Environment = (nint)environment;
        s_KeyHandler = (nint)keyHandler;
        s_GameData = (nint)gameData;
    }

    private static ModuleEngine ClientOver(FakeShimRoot root)
    {
        var side = EngineSide.Create(Side.Client);
        side.Initialize();
        Assert.True(side.AttachOwnShims(root.Bind(), out string why), why);
        return new ModuleEngine(side);
    }

    [Fact]
    public void CreatingTheRepresentationsCallsTheEnginesObjectWithTheEnvironmentAndTheKeyHandler()
    {
        using var root = new FakeShimRoot();
        var self = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));
        try
        {
            root.Interface(StaticPropMgrClient.InterfaceVersion, self)
                .Forwarders(out StaticPropMgrClientFwd* fwd);
            fwd->CreateVPhysicsRepresentations = &CreateRecords;

            IStaticPropPhysics props = StaticPropMgrClient.Acquire(ClientOver(root), out string why)
                ?? throw new InvalidOperationException(why);
            s_Calls = 0;
            s_GameData = -1;

            props.CreateVPhysicsRepresentations(0x9900, 0x4400);

            Assert.Equal(1, s_Calls);
            Assert.Equal(((nint)self, (nint)0x9900, (nint)0x4400, (nint)0), (s_Self, s_Environment, s_KeyHandler, s_GameData));
        }
        finally
        {
            NativeMemory.Free(self);
        }
    }

    [Fact]
    public void NoManagerWhereTheEngineServesNone()
    {
        using var root = new FakeShimRoot();   // serves no StaticPropMgrClient004

        Assert.Null(StaticPropMgrClient.Acquire(ClientOver(root), out string why));
        Assert.Contains("StaticPropMgrClient004", why);
    }
}
