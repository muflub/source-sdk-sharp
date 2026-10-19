using System.Reflection.Emit;
using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The generic per-player composite (tf2 stage 0.7 part B, rulings D9/D10).
/// The claims the plan named: two sources from two FAKE ASSEMBLIES attach in
/// the sorted order (assembly name, then <see cref="IPlayerComponent.Order"/>
/// — and NOT in the chains' HookOrder, which the plant contradicts on purpose),
/// tick in that order, detach on disconnect in REVERSE with a count
/// differential, survive a level shutdown, and <c>Get&lt;T&gt;</c> finds a
/// component across sources by interface. The no-owner arm — no source
/// registered at all — must cost the game nothing.
///
/// <para>
/// The fake assemblies are emitted (the <c>StringTableDeclarationTests</c>
/// precedent): the ordering key IS the registering assembly, so a test that
/// faked it with two types in this assembly would be a fact that cannot see
/// the sort. Each emitted type is an empty subclass of
/// <see cref="TracedComponent"/>, whose constructor the IL feeds a label and
/// an <see cref="IPlayerComponent.Order"/> — the behaviour lives in readable
/// C#, the assembly switch in four lines of IL.
/// </para>
/// </summary>
public class PlayerCompositesTests
{
    public sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    /// <summary>The shared log every component records into, in dispatch order.</summary>
    public static readonly List<string> Sink = [];

    public abstract class TracedComponent : IPlayerComponent
    {
        private readonly string _label;
        public List<string> Events { get; } = [];
        public int Order { get; }
        public int Ticks { get; private set; }
        public bool ThrowsOnTick { get; init; }
        public EntityHandle AttachedTo { get; private set; }

        protected TracedComponent(string label, int order)
        {
            _label = label;
            Order = order;
        }

        public void Attach(EntityHandle player)
        {
            AttachedTo = player;
            Events.Add("attach");
            Sink.Add($"{_label}:attach");
        }

        public void Tick(float now)
        {
            if (ThrowsOnTick)
                throw new InvalidOperationException($"{_label} tick is broken");
            Ticks++;
            Events.Add("tick");
            Sink.Add($"{_label}:tick");
        }

        public void Detach()
        {
            Events.Add("detach");
            Sink.Add($"{_label}:detach");
        }
    }

    /// <summary>A slice another mod may ask for BY INTERFACE, never by type.</summary>
    public interface ISharedBadge : IPlayerComponent
    {
        string Value { get; }
    }

    public class BadgedComponent(string label, int order, string value) : TracedComponent(label, order), ISharedBadge
    {
        public string Value { get; } = value;
        public BadgedComponent(string label, int order) : this(label, order, "tf2-class") { }
    }

    /// <summary>An empty subclass in a dynamic assembly of the given name.</summary>
    private static Type Emitted(string assemblyName, string typeName, string label, int order, Type? baseType = null)
    {
        var module = AssemblyBuilder
            .DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("M");

        var base_ = baseType ?? typeof(TracedComponent);
        var baseCtor = base_.GetConstructor(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance,
            null, [typeof(string), typeof(int)], null)!;

        var tb = module.DefineType(typeName, TypeAttributes.Public, base_);
        var ctor = tb.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, label);
        il.Emit(OpCodes.Ldc_I4, order);
        il.Emit(OpCodes.Call, baseCtor);
        il.Emit(OpCodes.Ret);
        return tb.CreateType()!;
    }

    private sealed class Source(Func<EntityHandle, IEnumerable<IPlayerComponent>> create) : IPlayerComponentSource
    {
        public int Calls { get; private set; }
        public IEnumerable<IPlayerComponent> Create(EntityHandle player)
        {
            Calls++;
            return create(player);
        }
    }

    private sealed class Rig
    {
        public readonly RecordingLog Log = new();
        public readonly RecordingEngine Engine = TestServices.Fake();
        public readonly ServiceContainer Services = TestServices.Services();
        public readonly PlayerComposites Composites;

        public Rig(params IPlayerComponentSource[] sources)
        {
            Sink.Clear();
            Engine.Returns("Host.ActiveSide", Side.Server);
            Engine.Returns("Players.GetHandle", Handle);
            Composites = new PlayerComposites(Log, new ListHooks<IPlayerComponentSource>(sources), Engine, Engine);
        }

        public static readonly EntityHandle Handle = new(0x0001_0002u);

        public void Active(int entIndex = 1) => ((IClientLifecycle)Composites).OnClientActive(entIndex);
        public void Left(int entIndex = 1) => ((IClientLifecycle)Composites).OnClientDisconnected(entIndex);
        public void Frame() => ((IFrameLifecycle)Composites).OnThink(0.1f);
        public void Shutdown() => ((IGameLifecycle)Composites).OnLevelShutdown();
    }

    //---------------------------------------------------------------------
    // The order: assembly name first, Order inside the assembly, HookOrder
    // deliberately contradicting both (the plant only bites if the sort is
    // real — HookOrder is the order the chain hands the components over).
    //---------------------------------------------------------------------

    [Fact]
    public void SourcesFromTwoAssembliesAttachInAssemblyOrderNotHookOrder()
    {
        // Z-registered first in the chain (HookOrder via list position), A's
        // components second; assembly order must WIN: every A first, then Z.
        var z1 = (TracedComponent)Activator.CreateInstance(Emitted("ZFakeMod", "Z1", "Z1", 0))!;
        var a1 = (TracedComponent)Activator.CreateInstance(Emitted("AFakeMod", "A1", "A1", 5))!;
        var a2 = (TracedComponent)Activator.CreateInstance(Emitted("AFakeMod", "A2", "A2", 1))!;

        var srcZ = new Source(_ => [z1]);
        var srcA = new Source(_ => [a1, a2]);
        var rig = new Rig(srcZ, srcA);   // Z first: the chain's own order

        rig.Active();

        Assert.True(Sink.SequenceEqual(new[] { "A2:attach", "A1:attach", "Z1:attach" }),
            "assembly name (ordinal) first, then Order inside the assembly — not the chain's order");

        Sink.Clear();
        rig.Frame();
        Assert.True(Sink.SequenceEqual(new[] { "A2:tick", "A1:tick", "Z1:tick" }),
            "the tick runs the same fixed sequence, every frame");
        Sink.Clear();
        rig.Frame();
        Assert.True(a1.Ticks == 2, "two frames, every component asked both times");
    }

    [Fact]
    public void DetachRunsReverseAndExactlyOnce()
    {
        var z1 = (TracedComponent)Activator.CreateInstance(Emitted("ZFakeMod", "Z1", "Z1", 0))!;
        var a1 = (TracedComponent)Activator.CreateInstance(Emitted("AFakeMod", "A1", "A1", 0))!;
        var rig = new Rig(new Source(_ => [z1]), new Source(_ => [a1]));

        rig.Active();
        Assert.True(Sink.SequenceEqual(new[] { "A1:attach", "Z1:attach" }), "the plan's attach order");

        Sink.Clear();
        rig.Left();
        Assert.True(Sink.SequenceEqual(new[] { "Z1:detach", "A1:detach" }),
            "reverse, so a component still reads its neighbours while tearing down");

        Sink.Clear();
        rig.Left();
        Assert.Empty(Sink);   // the count differential: one detach per attach, never a second

        // BEFORE != AFTER is the whole pair: attach asked each once, leave
        // detached each once, a repeated leave asks nothing.
        Assert.Equal(1, z1.Events.Count(e => e == "attach"));
        Assert.Equal(1, z1.Events.Count(e => e == "detach"));
    }

    [Fact]
    public void ALevelChangeEndsTheComponentsEvenThoughNobodyLeft()
    {
        // The engine does NOT call OnClientDisconnected for a level change —
        // the client stays. The composite's own level-shutdown half is what
        // keeps per-player state from crossing the reload, and this fact is
        // the difference between the two routes existing separately.
        var a1 = (TracedComponent)Activator.CreateInstance(Emitted("AFakeMod", "A1", "A1", 0))!;
        var rig = new Rig(new Source(_ => [a1]));

        rig.Active();
        Sink.Clear();
        rig.Shutdown();
        Assert.Equal(["A1:detach"], Sink);

        // And the room is empty: the same index attaching again builds fresh
        // components, which is what a re-load of the same map must mean.
        var a1again = (TracedComponent)Activator.CreateInstance(Emitted("AFakeMod", "A1", "A1", 0))!;
        rig = new Rig(new Source(_ => [a1again]));
        rig.Active();
        Assert.Equal(["A1:attach"], Sink);
    }

    [Fact]
    public void APlayerAttachesOnceEvenWhenTheEngineSaysActiveTwice()
    {
        var a1 = (TracedComponent)Activator.CreateInstance(Emitted("AFakeMod", "A1", "A1", 0))!;
        var src = new Source(_ => [a1]);
        var rig = new Rig(src);

        rig.Active();
        rig.Active();   // a doubled event (a load-activate followed by the real one)

        Assert.True(src.Calls == 1, "one build per player, not one per event");
        Assert.Equal(1, a1.Events.Count(e => e == "attach"));
    }

    //---------------------------------------------------------------------
    // Get<T> across sources — the composite is the one object that saw both.
    //---------------------------------------------------------------------

    [Fact]
    public void GetFindsAComponentAcrossSourcesByInterface()
    {
        // Descent's component reading TF2's class component: the badge lives
        // in the Z mod's assembly and implements an interface NEITHER the rig
        // type nor the query references by concrete type.
        var badge = (BadgedComponent)Activator.CreateInstance(
            Emitted("TF2FakeMod", "ClassComp", "badge", 0, typeof(BadgedComponent)))!;
        var other = (TracedComponent)Activator.CreateInstance(Emitted("DescentFakeMod", "Abl", "abl", 0))!;

        var rig = new Rig(new Source(_ => [badge]), new Source(_ => [other]));
        rig.Active();

        Assert.True(badge.AttachedTo == Rig.Handle, "the handle the engine's own GetHandle answers");
        Assert.Same(badge, rig.Composites.Get<ISharedBadge>(Rig.Handle));
        Assert.Null(rig.Composites.Get<ISharedBadge>(new EntityHandle(0x0009_0009u)));   // no such player
        Assert.Null(rig.Composites.Get<ITracingWish>(Rig.Handle));                        // no such slice
    }

    private interface ITracingWish : IPlayerComponent { }

    //---------------------------------------------------------------------
    // The no-owner arm and the chain's failures.
    //---------------------------------------------------------------------

    [Fact]
    public void WithNoSourcesTheCompositeCostsTheGameNothing()
    {
        // test mod / HL2MP today: nothing registers a source. The composite
        // still attaches (the room exists), ticks silently, detaches silently.
        var rig = new Rig();

        rig.Active();
        Assert.Empty(Composes(rig));   // attached, with nothing in the room

        rig.Frame();
        rig.Frame();
        rig.Left();
        Assert.Empty(rig.Log.Lines);   // a game nobody extended says nothing about it
    }

    private static IEnumerable<IPlayerComponent> Composes(Rig rig) => rig.Composites.Of(1) ?? [];

    [Fact]
    public void AThrowingSourceIsNamedAndTheOtherModsComponentsStillArrive()
    {
        var good = (TracedComponent)Activator.CreateInstance(Emitted("AFakeMod", "A1", "A1", 0))!;

        var rig = new Rig(
            new Source(_ => throw new InvalidOperationException("mod broken")),
            new Source(_ => [good]));

        rig.Active();
        Assert.Contains(rig.Log.Lines, l => l.Warning && l.Text.Contains("mod broken"));
        Assert.Equal(1, good.Events.Count(e => e == "attach"));
        Assert.Single(rig.Composites.Of(1)!);   // the OTHER mod's slice arrived beside the broken source
    }

    [Fact]
    public void ATickingThrowIsNamedOnceSkippedAndStillDetached()
    {
        var bad = new BadgedComponent("bad", 0) { ThrowsOnTick = true };
        var good = (TracedComponent)Activator.CreateInstance(Emitted("AFakeMod", "A1", "A1", 1))!;
        var rig = new Rig(new Source(_ => [bad, good]));

        rig.Active();
        rig.Frame();
        rig.Frame();
        rig.Frame();

        // (the first frame attempted it and threw: no Tick completed)
        Assert.Equal(0, bad.Ticks);
        Assert.Equal(3, good.Ticks);   // its neighbour ticked every frame regardless
        // named once, not sixty times a second
        Assert.Equal(1, rig.Log.Lines.Count(l => l.Warning && l.Text.Contains("bad tick is broken")));

        rig.Left();
        Assert.Equal(1, bad.Events.Count(e => e == "detach"));   // teardown is owed even to a skipped slice
    }



    //---------------------------------------------------------------------
    // The composition facts the plan asks for: participant positions, and the
    // D10 rule over the new files (sealed is allowed only beside a hook).
    //---------------------------------------------------------------------

    [Fact]
    public void TheCompositeIsAParticipantOfAllThreeDrivers()
    {
        // All three at once is the shape the class needs: attach/detach come
        // from the client lifecycle, the tick from the frame, the
        // cross-reload clear from the level lifecycle.
        Assert.True(typeof(PlayerComposites).IsAssignableTo(typeof(IClientLifecycle)));
        Assert.True(typeof(PlayerComposites).IsAssignableTo(typeof(IFrameLifecycle)));
        Assert.True(typeof(PlayerComposites).IsAssignableTo(typeof(IGameLifecycle)));
    }

    [Fact]
    public void EveryDispatchingClassBehindTheCompositeIsSealedBesideAHook()
    {
        // The plan's structure fact, over the REAL types of the server
        // assembly: every class that holds a component source or a component
        // is found by reflection, and a sealed one must reach them through a
        // seam (the IHooks chain or the container) — so a new sealed dispatcher
        // with its sources hard-wired fails here without anyone listing it.
        Assert.True(typeof(IPlayerComponentSource).IsInterface);
        Assert.True(typeof(IPlayerComponent).IsInterface);

        var dispatchers = SealedSeams.Dispatchers(typeof(PlayerComposites).Assembly,
                                                  [typeof(IPlayerComponentSource), typeof(IPlayerComponent)]);
        Assert.Contains(typeof(PlayerComposites), dispatchers);     // the stimulus: the walk finds the composite

        var unseamed = SealedSeams.SealedWithoutSeam(dispatchers, []);
        Assert.True(unseamed.Count == 0,
            "sealed classes dispatch to player components with no hook: " +
            string.Join(", ", unseamed.Select(t => t.FullName)));
    }
}
