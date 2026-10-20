using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

//=============================================================================
// THE PER-SIDE HOLDER, AND THE ONE HAZARD IT INHERITED.
//
// plan_native_api.md Phases 3 and 4. Both game DLLs share SourceSharp.Abi in
// the default ALC, so an engine interface held in a static field is held for
// WHICHEVER SIDE BOUND LAST. For IVEngineServer that would be a null on the
// client and nothing worse; for IVModelInfo it is the interesting case, because
// the engine registers VModelInfoServer004 and VModelInfoClient006 for TWO
// INSTANCES OF ONE CLASS. The wrong one is not a crash. It is a plausible
// answer about the other side's world.
//
// WHY THAT MATTERS HERE RATHER THAN IN THE ABSTRACT. Until Phase 4 the same
// hazard lived on the flat table, as an arm of
// tempent_spawn_seam_test.cpp: ModelFrameCount and ModelType had to be wired
// on BOTH sides, because ten of the seventeen tempent spawn helpers open by
// asking a model's frame count and a SERVER answer on the CLIENT is -1 --
// m_flFrameMax goes to -2 and an animated sprite dies on its first frame. And
// ModelType's one reader is BreakModel, where mod_bad is ZERO, so the wrong
// side answers "that model is broken" about a model that is fine.
//
// Those slots are gone. The bug is not gone; it moved, and it can now happen in
// exactly one way -- asking for the wrong version string. This file is where
// that is checked.
//=============================================================================
public class EngineBoundTests
{
    private static Type Bound
        => typeof(EngineServices).Assembly.GetType("SourceSharp.Abi.EngineBound")!;

    // The held interfaces are each engine's own (statics to zero): the set is
    // read off an engine, never off a static.
    private static object Holder(string property)
        => typeof(EngineServices).Assembly.GetType("SourceSharp.Abi.EngineBoundSet")!
               .GetProperty(property, BindingFlags.NonPublic | BindingFlags.Instance)!
               .GetValue(EngineSide.Create(Side.Server).Bound)!;

    private static string VersionOf(string property)
        => (string)Holder(property).GetType()
               .GetField("_version", BindingFlags.NonPublic | BindingFlags.Instance)!
               .GetValue(Holder(property))!;

    [Fact]
    public void ModelInfoIsAskedForByTheSidesOwnName()
    {
        // THE ARM THAT REPLACED FOUR SOURCE-SCAN ARMS, and it checks the
        // acquirer rather than the field it lands in: the field is per side by
        // construction, and what could still be wrong is WHICH NAME each side
        // asks for. Driven through each side's own engine (plan_abi_hotreload
        // D8): the acquire fails in a test process -- there is no native half --
        // but the REASON names the version string it asked for, which is the
        // thing under test.
        string clientWhy = Assert.Throws<InterfaceAcquisitionException>(
            () => EngineBound.ModelInfo(EngineSide.Create(Side.Client))).Message;
        string serverWhy = Assert.Throws<InterfaceAcquisitionException>(
            () => EngineBound.ModelInfo(EngineSide.Create(Side.Server))).Message;

        Assert.Contains("VModelInfoClient006", clientWhy);
        Assert.DoesNotContain("VModelInfoServer004", clientWhy);

        Assert.Contains("VModelInfoServer004", serverWhy);
        Assert.DoesNotContain("VModelInfoClient006", serverWhy);
    }

    [Fact]
    public void EachEngineAcquiresThroughItsOwnFactoryAndHoldsItsOwnObject()
    {
        // plan_abi_hotreload D8, statics to zero: the holder belongs to ONE
        // engine and answers through that engine's own factory, never the
        // thread's active side or a process-wide slot. Two engines of the SAME
        // side (two game instances) each acquire and hold their own.
        var first = EngineSide.Create(Side.Server);
        var second = EngineSide.Create(Side.Server);
        var client = EngineSide.Create(Side.Client);
        var asked = new List<(NativeInterfaceFactory Factory, Side Side)>();

        BoundInterface<object> Holder() => new("Test001", (NativeInterfaceFactory factory, Side side, out string why) =>
        {
            asked.Add((factory, side));
            why = string.Empty;
            return new object();
        });

        BoundInterface<object> firstHolder = Holder(), secondHolder = Holder(), clientHolder = Holder();

        object firstAnswer = firstHolder.For(first);
        object secondAnswer = secondHolder.For(second);
        object clientAnswer = clientHolder.For(client);

        Assert.Equal(3, asked.Count);
        Assert.Same(first.Interfaces, asked[0].Factory);
        Assert.Same(second.Interfaces, asked[1].Factory);
        Assert.Same(client.Interfaces, asked[2].Factory);
        Assert.Equal(Side.Client, asked[2].Side);
        Assert.NotSame(firstAnswer, secondAnswer);

        // Held: asked again, each answers what it acquired and nothing is
        // acquired twice.
        Assert.Same(firstAnswer, firstHolder.For(first));
        Assert.Same(clientAnswer, clientHolder.For(client));
        Assert.Equal(3, asked.Count);
    }

    [Fact]
    public void ASideNobodyBoundAnswersIVEngineServerByNameNotWithAZero()
    {
        // IVEngineServer's wrapper is Server.Interop's (one forwarder library
        // per side), so the shared slot holds no acquirer until the server's
        // entry installs one; the client's never does.
        string why = Assert.Throws<InterfaceAcquisitionException>(
            () => EngineBound.Server(EngineSide.Create(Side.Client))).Message;

        Assert.Contains("VEngineServer023", why);
        Assert.Contains("binds no IVEngineServer", why);
    }

    [Fact]
    public void TheServerBindingAsksIVEngineServerThroughTheSidesOwnFactory()
    {
        var side = EngineSide.Create(Side.Server);
        EngineServerBinding.Bind(side);

        // No native half in a test process: the acquire fails, but in the
        // factory, asking for the server's version string -- not in the
        // unbound slot.
        string why = Assert.Throws<InterfaceAcquisitionException>(() => EngineBound.Server(side)).Message;

        Assert.Contains("VEngineServer023", why);
        Assert.DoesNotContain("binds no IVEngineServer", why);
    }

    [Fact]
    public void UseServerInstallsTheAcquirerTheSideAsksThrough()
    {
        var side = EngineSide.Create(Side.Server);
        var asked = new List<(NativeInterfaceFactory Factory, Side Side)>();

        side.Bound.UseServer((NativeInterfaceFactory factory, Side s, out string why) =>
        {
            asked.Add((factory, s));
            why = "the installed acquirer answered";
            return null;
        });

        string message = Assert.Throws<InterfaceAcquisitionException>(() => EngineBound.Server(side)).Message;

        Assert.Contains("the installed acquirer answered", message);
        Assert.Equal(new[] { (side.Interfaces, Side.Server) }, asked);
    }

    [Fact]
    public void EveryEngineHoldsItsOwnBoundSet()
    {
        // The set's whole shape: one per engine. A future edit that hoisted it
        // into a static would make every engine answer for whichever bound last.
        var one = EngineSide.Create(Side.Server);
        var two = EngineSide.Create(Side.Server);

        Assert.NotSame(one.Bound, two.Bound);
        Assert.NotSame(one.Bound.ModelInfo, two.Bound.ModelInfo);
        Assert.Same(one.Bound, one.Bound);
    }

    [Fact]
    public void TheHolderIsInternalSoNoInterfacePointerReachesGameCode()
        // Game code asks an IEngine* area for a capability; it never asks for an
        // interface. Publishing this would be a service locator over the exact
        // pointers the impl classes exist to keep hidden.
        => Assert.False(Bound.IsPublic);

    [Fact]
    public void EveryHeldInterfaceNamesTheVersionStringItsImplDeclares()
    {
        // The holder repeats a version string that the impl class already owns.
        // A second spelling is a second thing to get wrong, so the two are held
        // against each other rather than trusted to agree.
        Assert.Equal(EngineServer.InterfaceVersion, VersionOf("Server"));
        Assert.Equal(UniformRandomStream.InterfaceVersion, VersionOf("Random"));
        Assert.Equal(EngineBaseFileSystem.InterfaceVersion, VersionOf("Files"));
        Assert.Equal(EngineModelInfo.ServerInterfaceVersion, VersionOf("ModelInfo"));
    }
}
