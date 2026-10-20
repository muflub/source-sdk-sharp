extern alias client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SourceSharp.Abi;
using client::SourceSharp.Client;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_abi_hotreload D8 (lane d8e): the model caches and the user message
/// registrations are each side's OWN. They were static classes whose two
/// tables the thread's active side chose between, and a registry that
/// registered with whichever side was active; two game instances in one
/// process shared all of it. Each fact here composes two and shows a call
/// reaching only its own.
/// </summary>
public class SideModelCachesTests
{
    private const string Model = "models/props_junk/wood_crate001a.mdl";

    private const string Crate = "break { \"model\" \"props_junk/Wood_Crate001a_Chunk01\" }";

    private static ModelDescriptors Descriptors(List<string> asked)
        => new(model => { asked.Add(model); return 0; },
               _ => Model, new RecordingLog());

    [Fact]
    public void ADescriptorMissCrossesOnlyItsOwnSidesEngine()
    {
        var askedA = new List<string>();
        var askedB = new List<string>();
        var a = Descriptors(askedA);
        var b = Descriptors(askedB);

        a.ForModel(Model);

        Assert.Equal([Model], askedA);
        Assert.Empty(askedB);
        Assert.Equal(1, a.CachedCount);
        Assert.Equal(0, b.CachedCount);

        // And B's first ask is a miss of its own, not A's cached answer.
        b.ForModel(Model);
        Assert.Equal([Model], askedB);
    }

    [Fact]
    public void ClearingOneSidesDescriptorsLeavesTheOthers()
    {
        var a = Descriptors([]);
        var b = Descriptors([]);
        a.ForModel(Model);
        b.ForModel(Model);
        Assert.Equal(1, b.CachedCount);

        a.Clear();

        Assert.Equal(0, a.CachedCount);
        Assert.Equal(1, b.CachedCount);
    }

    [Fact]
    public void ABreakListIsParsedFromItsOwnSidesEngine()
    {
        var textA = new List<int>();
        var textB = new List<int>();
        var a = new BreakModels(_ => 7, _ => -1, i => { textA.Add(i); return Crate; }, Descriptors([]), new RecordingLog());
        var b = new BreakModels(_ => 9, _ => -1, i => { textB.Add(i); return Crate; }, Descriptors([]), new RecordingLog());

        Assert.Equal(1, a.Count(Model));

        Assert.Equal([7], textA);
        Assert.Empty(textB);
        Assert.Equal(1, a.Parses);
        Assert.Equal(0, b.Parses);

        a.Clear();
        Assert.Equal(1, b.Count(Model));
        Assert.Equal(0, a.Parses);
        Assert.Equal(1, b.Parses);
    }

    [Fact]
    public void AnUnindexedBreakModelIsPrecachedOnItsOwnSide()
    {
        var precachedA = new List<string>();
        var precachedB = new List<string>();
        var a = new BreakModels(_ => -1, m => { precachedA.Add(m); return 3; }, _ => Crate, Descriptors([]), new RecordingLog());
        _ = new BreakModels(_ => -1, m => { precachedB.Add(m); return 3; }, _ => Crate, Descriptors([]), new RecordingLog());

        Assert.Equal(1, a.Count(Model));

        Assert.Equal([Model], precachedA);
        Assert.Empty(precachedB);
    }

    [Fact]
    public void ASideWithNoPrecacheAnswersNoPiecesForAnUnindexedModel()
    {
        // The client's: its precache slot is server-only, so it hands none in
        // and an unindexed model reads as the null slot's -1 did.
        int textReads = 0;
        var breaks = new BreakModels(_ => -1, null, _ => { textReads++; return Crate; }, Descriptors([]), new RecordingLog());

        Assert.Equal(0, breaks.Count(Model));
        Assert.Equal(0, textReads);
        Assert.Equal(0, breaks.Parses);
    }

    [Fact]
    public void OnlyTheServersEngineSideHandsItsBreaksAPrecache()
    {
        static object? PrecacheOf(BreakModels b)
            => typeof(BreakModels).GetField("_precache", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(b);

        Assert.NotNull(PrecacheOf(TestSides.Create(Side.Server).Breaks));
        Assert.Null(PrecacheOf(TestSides.Create(Side.Client).Breaks));
    }

    [Fact]
    public void EachEngineOwnsItsModelCachesAndItsBreakListsReadItsDescriptors()
    {
        var first = TestSides.Create(Side.Server);
        var second = TestSides.Create(Side.Server);

        Assert.NotSame(first.Models, second.Models);
        Assert.NotSame(first.Breaks, second.Breaks);
        Assert.Same(first.Models, Held<ModelDescriptors>(first.Breaks));
        Assert.Same(second.Models, Held<ModelDescriptors>(second.Breaks));
    }

    [Fact]
    public void TwoServerCompositionsRegisterAndClearTheirOwnEnginesCaches()
    {
        var engineA = new ModuleEngine(TestSides.Create(Side.Server));
        var engineB = new ModuleEngine(TestSides.Create(Side.Server));
        var a = new ServerModule(engineA);
        var b = new ServerModule(engineB);
        a.ComposeEngineAreas();
        b.ComposeEngineAreas();

        try
        {
            Assert.Same(engineA.Models, a.Services.Require<ModelDescriptors>());
            Assert.Same(engineB.Models, b.Services.Require<ModelDescriptors>());
            Assert.Same(engineA.Breaks, a.Services.Require<BreakModels>());
            Assert.Same(engineB.Breaks, b.Services.Require<BreakModels>());
            Assert.NotSame(engineA.Models, engineB.Models);

            // The level-shutdown handler each root builds clears its own.
            var frame = b.NewFrameBinding();
            Assert.Same(engineB.Models, Held<ModelDescriptors>(frame));
            Assert.Same(engineB.Breaks, Held<BreakModels>(frame));
        }
        finally
        {
            a.Shutdown();
            b.Shutdown();
        }
    }

    [Fact]
    public void TheServersLevelShutdownClearsTheCachesItIsHandedAndNoOthers()
    {
        var mine = Descriptors([]);
        var theirs = Descriptors([]);
        var breaks = new BreakModels(_ => 7, _ => -1, _ => Crate, mine, new RecordingLog());
        mine.ForModel(Model);
        theirs.ForModel(Model);
        breaks.Count(Model);
        Assert.Equal(1, breaks.Parses);

        new SourceSharp.Server.Interop.FrameBinding(new Crossings(), new RecordingLog(), new QuietModule(), new LivenessMirror(), mine, breaks).LevelShutdown();

        Assert.Equal(0, mine.CachedCount);
        Assert.Equal(0, breaks.Parses);
        Assert.Equal(1, theirs.CachedCount);
    }

    [Fact]
    public void TwoSendersSendThroughTheirOwnEngine()
    {
        var engineA = new RecordingUserMessageEngine();
        var engineB = new RecordingUserMessageEngine();
        var a = new UserMessageRegistry(new RecordingLog(), receives: false);
        var b = new UserMessageRegistry(new RecordingLog(), receives: false);
        int registered = a.RegisterAll(typeof(SourceSharp.Shared.FadeMessage).Assembly);
        b.RegisterAll(typeof(SourceSharp.Shared.FadeMessage).Assembly);
        _ = new UserMessageSender(b, engineB, new RecordingLog());

        new UserMessageSender(a, engineA, new RecordingLog()).Send("Fade", new byte[] { 1 }, player: 1);

        Assert.True(registered > 0, "the shared assembly declares user messages (the positive arm)");
        Assert.Single(engineA.Sent);
        Assert.Empty(engineB.Sent);
    }

    // The one field of type T an object holds (a primary constructor's
    // captured parameter included).
    private static T Held<T>(object holder)
        => (T)holder.GetType()
               .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
               .Single(f => f.FieldType == typeof(T))
               .GetValue(holder)!;

    private sealed class QuietModule : ISideModule
    {
        public void Initialize(Side side) { }
        public void Frame(float frameTime) { }
        public void LevelInit(string mapName) { }
        public void LevelShutdown() { }
        public void ConCommandDispatch(int commandId, string args) { }
        public void Shutdown() { }
        public void DispatchGameEvent(string eventName, IntPtr handle) { }
    }
}
