using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's static prop manager (plan_engine_forwarders.md, one library
/// per side): Server.Interop's wrapper over IStaticPropMgrServer and
/// libss_fwd_server's table, which the world build asks through
/// <see cref="IStaticPropPhysics"/>. Acquired through a server engine whose
/// own root serves the interface and its forwarders.
/// </summary>
public sealed unsafe class StaticPropMgrServerTests
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

    private static ModuleEngine ServerOver(FakeShimRoot root)
    {
        var side = TestSides.Create(Side.Server);
        side.Attachment.Initialize();
        Assert.True(side.Attachment.AttachShims(root.Bind(), out string why), why);
        return new ModuleEngine(side);
    }

    [Fact]
    public void CreatingTheRepresentationsCallsTheEnginesObjectWithTheEnvironmentAndTheKeyHandler()
    {
        using var root = new FakeShimRoot();
        var self = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));
        try
        {
            root.Interface(StaticPropMgrServer.InterfaceVersion, self)
                .Forwarders(out StaticPropMgrServerFwd* fwd);
            fwd->CreateVPhysicsRepresentations = &CreateRecords;

            IStaticPropPhysics props = StaticPropMgrServer.Acquire(ServerOver(root), out string why)
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
        using var root = new FakeShimRoot();   // serves no StaticPropMgrServer002

        Assert.Null(StaticPropMgrServer.Acquire(ServerOver(root), out string why));
        Assert.Contains("StaticPropMgrServer002", why);
    }
}
