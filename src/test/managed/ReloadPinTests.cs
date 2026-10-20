extern alias client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using SourceSharp.Abi;
using SourceSharp.Bootstrap;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// WHY THE SERVER'S LOAD CONTEXT LEAKED ON EVERY HOT RELOAD, AND WHAT NOW STOPS IT.
///
/// <para>
/// <c>Entry.UnloadModule</c> printed <c>"ServerALC did NOT unload"</c> once per
/// reload for the whole of this port. Lane <c>console-warnings</c> removed one
/// real pin (the DebugApi accept thread) and reported honestly that the warning
/// stayed at 1 per reload afterwards, so the thread was not it.
/// </para>
///
/// <para>
/// It was four static routes in the ABI (three per-side instances since
/// plan_abi_hotreload D7, still reachable from the static <c>Engine</c> until D8) — which lives in a context that outlives
/// every side. <c>ScriptedEntityRegistry</c>'s constructor binds all four to
/// itself and to its directory, each of them under a comment saying it is
/// cleared "so nothing static holds an object of a side that is being unloaded",
/// and <c>Unbind()</c> was written on all three route classes for exactly that —
/// AND NOTHING EVER CALLED ANY OF THEM. From the first map load the ABI held the
/// server's registry, its directory and every entity <see cref="Type"/> on that
/// side.
/// </para>
///
/// <para>
/// THE TRAP THESE FACTS EXIST TO PIN. The obvious teardown — "drop the route if
/// the delegate came from the unloading context" — drops NOTHING here, because
/// every one of those delegates is over a method of <c>EntityDirectory</c> or of
/// the registry, both declared in the ABI itself. It was written, it compiled,
/// it forgot two more entries than before, and the ALC still leaked. What
/// belongs to the side is one reference further down: the entity types the side
/// PUT IN the registry. So the rule is the registry's registrations, and
/// <see cref="ManagedCachesForgetDropsAllThreeRoutes"/> is the fact that would go
/// red if anyone replaced it with the obvious one again.
/// </para>
/// </summary>
[Collection(ProcessWide.Name)]
public sealed class ReloadPinTests
{
    // This class's own command queues and shadow run (statics to zero):
    // xUnit builds one instance per fact, so no fact sees another's.
    private readonly PlayerCommandQueue _queue = new(new RecordingLog());

    /// <summary>A pin planted on purpose, for <see cref="TheFinderNamesAPlantedStaticPin"/>.</summary>
    private static object? s_PlantedPin;

    /// <summary>
    /// A collectible context holding its own copy of SourceSharp.Server — a real
    /// second load of a real assembly, which is what makes
    /// <c>AssemblyLoadContext.GetLoadContext</c> answer something other than the
    /// test host's own context.
    /// </summary>
    private static (AssemblyLoadContext Context, Assembly Assembly) LoadSideCopy(string name)
    {
        var context = new AssemblyLoadContext(name, isCollectible: true);
        var path = typeof(PropPhysics).Assembly.Location;
        Assert.False(string.IsNullOrEmpty(path),
            "SourceSharp.Server must be a file on disk for this test to load a second copy of it");
        return (context, context.LoadFromAssemblyPath(path));
    }

    /// <summary>A registry over a side's routes, with a load context's classes in it.</summary>
    /// <summary>
    /// The side copy's OWN registry over <paramref name="side"/>'s routes, with
    /// the copy's classes registered. Since P1 run 3 the registry lives in
    /// SourceSharp.Server beside its classes, so a reloaded side brings its own
    /// registry type: the one a test builds in the default context could not
    /// take the copy's classes (their ScriptedEntity is the copy's). The
    /// routes are the engine side's, shared (Abi), as before.
    /// </summary>
    private static IRouteBinder RegistryOver(EngineSide side, Assembly classes)
    {
        Type type = classes.GetType("SourceSharp.Abi.ScriptedEntityRegistry", throwOnError: true)!;
        Assert.NotSame(typeof(ScriptedEntityRegistry), type);

        object registry = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { side.ShellLess, side.EntityIo, side.Removal, TestEngines.Server() }, null)!;
        int registered = (int)type.GetMethod("RegisterAll", new[] { typeof(Assembly) })!.Invoke(registry, new object[] { classes })!;
        Assert.True(registered > 0, "the second copy of SourceSharp.Server must contribute entity classes");
        return (IRouteBinder)registry;
    }

    /// <summary>The side copy's own registry with its classes, over a fresh side's routes.</summary>
    private static IRouteBinder SideRegistry(Assembly classes) => RegistryOver(TestSides.Create(Side.Server), classes);

    //-------------------------------------------------------------------------
    // Which side a registry belongs to.
    //-------------------------------------------------------------------------

    [Fact]
    public void ARegistryBelongsToTheContextItsClassesCameFrom()
    {
        var (context, assembly) = LoadSideCopy("belongs-yes");
        try
        {
            var registry = SideRegistry(assembly);
            Assert.True(registry is not ScriptedEntityRegistry,
                "the registry is the side copy's own, not the default context's");

            Assert.True(registry.BelongsTo(context));
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void ARegistryDoesNotBelongToAContextThatPutNothingInIt()
    {
        var (context, assembly) = LoadSideCopy("belongs-no");
        var (other, _) = LoadSideCopy("belongs-no-other");
        try
        {
            var registry = SideRegistry(assembly);

            Assert.False(registry.BelongsTo(other));
        }
        finally
        {
            context.Unload();
            other.Unload();
        }
    }

    [Fact]
    public void AnEmptyRegistryBelongsToNoContextAtAll()
    {
        var (context, _) = LoadSideCopy("belongs-empty");
        try
        {
            Assert.False(Invoke_BelongsTo(new ScriptedEntityRegistry(TestEngines.Server()), context));
        }
        finally
        {
            context.Unload();
        }
    }

    //-------------------------------------------------------------------------
    // What Forget does with it. THE REGRESSION FACTS.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The production path: <see cref="ManagedCaches.Forget(AssemblyLoadContext, IReadOnlyList{EngineSide})"/>
    /// walks the sides it is handed (plan_abi_hotreload D8) and drops a dead
    /// registry's routes on the one that held it.
    /// </summary>
    [Fact]
    public void ManagedCachesForgetDropsAllThreeRoutes()
    {
        var (context, assembly) = LoadSideCopy("forget-drops");
        var side = TestSides.Create(Side.Server);
        try
        {
            var registry = RegistryOver(side, assembly);

            Assert.Same(registry, side.ShellLess.Answers);
            Assert.NotNull(side.EntityIo.Input);
            Assert.NotNull(side.EntityIo.ServiceQueue);
            Assert.NotNull(side.Removal.Mark);

            ManagedCaches.Forget(context, [TestSides.Create(Side.Client), side]);

            Assert.Null(side.ShellLess.Answers);
            Assert.Null(side.EntityIo.Input);
            Assert.Null(side.EntityIo.ServiceQueue);
            Assert.Null(side.Removal.Mark);
            Assert.Null(side.Removal.Free);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void ManagedCachesForgetSearchesOnlyTheSidesItIsHanded()
    {
        // plan_abi_hotreload D8: two game instances' server sides, both
        // holding a registry over the going context. Forget is handed one of
        // them, and only that one's routes go: the sides are its caller's,
        // never a process-wide pair it looks up.
        var (context, assembly) = LoadSideCopy("forget-handed");
        try
        {
            var handed = TestSides.Create(Side.Server);
            var other = TestSides.Create(Side.Server);
            RegistryOver(handed, assembly);
            var kept = RegistryOver(other, assembly);

            Assert.True(ManagedCaches.Forget(context, [handed]) >= 3, "the handed side's three routes went");

            Assert.Null(handed.ShellLess.Answers);
            Assert.Same(kept, other.ShellLess.Answers);
            Assert.NotNull(other.EntityIo.Input);
            Assert.NotNull(other.Removal.Mark);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void ManagedCachesForgetCountsTheThreeItDropped()
    {
        var (context, assembly) = LoadSideCopy("forget-counts");
        try
        {
            var side = TestSides.Create(Side.Server);
            RegistryOver(side, assembly);

            Assert.Equal(3, ManagedCaches.ForgetRoutes(side, context));
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void ForgettingAnotherSideLeavesThisSidesRoutesAlone()
    {
        // A LISTEN SERVER IS ONE PROCESS. The client unloading must not take
        // the server's routes with it, which is why the forget is selective
        // rather than a clear.
        var (context, assembly) = LoadSideCopy("forget-other-mine");
        var (stranger, _) = LoadSideCopy("forget-other-theirs");
        try
        {
            var side = TestSides.Create(Side.Server);
            var registry = RegistryOver(side, assembly);

            Assert.Equal(0, ManagedCaches.ForgetRoutes(side, stranger));

            Assert.Same(registry, side.ShellLess.Answers);
            Assert.NotNull(side.EntityIo.Input);
            Assert.NotNull(side.Removal.Mark);
        }
        finally
        {
            context.Unload();
            stranger.Unload();
        }
    }

    /// <summary>
    /// What the server composition's Shutdown calls: a registry takes itself out
    /// of its side's routes, and leaves a route a LATER registry bound alone.
    /// </summary>
    /// <summary>
    /// P1 run 3, lifetime ruling (b), per side state since statics to zero: a
    /// reloaded side's directory draws connection id stamps from the same
    /// counter as the one before it, because restored connections keep their
    /// stamps across a reload; the counter is its <see cref="SideState"/>'s,
    /// which the Bootstrap keeps across every load, so the other side's draws
    /// (and another game instance's) never move it.
    /// </summary>
    [Fact]
    public void TheConnectionIdStampIsOneCounterPerSideStateAcrossItsReloads()
    {
        var server = new SideState(Side.Server);
        var client = new SideState(Side.Client);
        var handle = new EntityHandle(0x0001_0005u);

        static int Next(SideState state, EntityHandle handle)
            => new EntityDirectory(new RecordingLog(), state.Stamps).Connect(handle, "OnTrigger", "a,Use,,0,-1").IdStamp;

        int before = Next(server, handle);

        // The reload: a new directory (the new load's registry builds one) over
        // the same state the Bootstrap kept.
        int after = Next(server, handle);
        int clientFirst = Next(client, handle), clientSecond = Next(client, handle);
        int afterClient = Next(server, handle);

        Assert.True(after == before + 1, $"one counter across the server's reload: {before}, then {after}");
        Assert.True(clientFirst == 1 && clientSecond == 2, $"the client's own counter: {clientFirst}, {clientSecond}");
        Assert.True(afterClient == after + 1, $"the client's draws leave the server's alone: {after}, then {afterClient}");
    }

    [Fact]
    public void TheRegistryIsTheOneRouteBinder()
    {
        // ManagedCaches drops a side's three routes when the IRouteBinder bound
        // into them came from the going context. It asked for
        // ScriptedEntityRegistry by type until P1 run 3; the interface is the
        // same question only while the registry is its one implementer.
        // Each side compiles its own IRouteBinder (plan_shim_areas_split phase
        // 3), so a type is asked against both, and both sides' interops are read.
        var binders = new[]
            {
                typeof(ILog).Assembly, typeof(ManagedCaches).Assembly, typeof(client::SourceSharp.Abi.ManagedCaches).Assembly,
                typeof(ScriptedEntityRegistry).Assembly,
                typeof(ServerModule).Assembly,
                typeof(client::SourceSharp.Client.ClientModule).Assembly,
            }
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsInterface && (typeof(IRouteBinder).IsAssignableFrom(t)
                                           || typeof(client::SourceSharp.Abi.IRouteBinder).IsAssignableFrom(t)))
            .ToList();

        Assert.Equal(new[] { typeof(ScriptedEntityRegistry) }, binders);
    }

    [Fact]
    public void DetachDropsOnlyTheRoutesStillBoundToThisRegistry()
    {
        var side = TestSides.Create(Side.Server);
        var first = new ScriptedEntityRegistry(side.ShellLess, side.EntityIo, side.Removal, TestEngines.Server());

        Assert.Equal(3, first.Detach());
        Assert.Null(side.ShellLess.Answers);
        Assert.Null(side.EntityIo.Input);
        Assert.Null(side.Removal.Mark);

        _ = new ScriptedEntityRegistry(side.ShellLess, side.EntityIo, side.Removal, TestEngines.Server());
        var second = side.ShellLess.Answers;

        Assert.Equal(0, first.Detach());
        Assert.Same(second, side.ShellLess.Answers);
        Assert.NotNull(side.EntityIo.Input);
        Assert.NotNull(side.Removal.Mark);
    }

    //-------------------------------------------------------------------------
    // The finder that named the pin.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheFinderNamesAPlantedStaticPin()
    {
        var (context, assembly) = LoadSideCopy("finder-plant");
        try
        {
            s_PlantedPin = assembly.GetTypes().First(t => t.Name == nameof(PropPhysics));

            var found = ManagedPins.Find(context);

            Assert.Contains(found, line => line.Contains(nameof(s_PlantedPin), StringComparison.Ordinal));
        }
        finally
        {
            s_PlantedPin = null;
            context.Unload();
        }
    }

    [Fact]
    public void TheFinderIsSilentWhenThePlantedPinIsGone()
    {
        var (context, _) = LoadSideCopy("finder-quiet");
        try
        {
            s_PlantedPin = null;

            Assert.DoesNotContain(ManagedPins.Find(context),
                line => line.Contains(nameof(s_PlantedPin), StringComparison.Ordinal));
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void TheFinderCallsATypeFromTheContextAPin()
    {
        var (context, assembly) = LoadSideCopy("belongs-type");
        try
        {
            var type = assembly.GetTypes().First(t => t.Name == nameof(PropPhysics));
            Assert.NotNull(Invoke_Belongs(type, context));
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void TheFinderDoesNotCallThisAssemblysOwnTypeAPin()
    {
        var (context, _) = LoadSideCopy("belongs-type-no");
        try
        {
            Assert.Null(Invoke_Belongs(typeof(ReloadPinTests), context));
        }
        finally
        {
            context.Unload();
        }
    }

    //-------------------------------------------------------------------------
    // THE SECOND PIN, found by the finder rather than guessed at — lane
    // `defect-alc` — was PlayerShadow.Switch, a static the server's
    // PlayerShadowConVar published itself into. plan_abi_hotreload D7 deleted
    // that static (the composition hands its own convar to the run), and
    // ManagedCaches' ForgetDeadShadowSwitch went with it; the facts that the
    // switch holds no static state are RouteInjectionTests'.
    //
    // The finder's half stays. Its last production subject,
    // PlayerShadow.AdvanceSwitch, went with the Advance that B7 replaced, so
    // the shape is held here: a static holding a plain object of a side's type.
    //-------------------------------------------------------------------------

    /// <summary>A static the finder walks: SourceSharp.Tests is one of ours.</summary>
    private static class PlainObjectPin
    {
        public static object? Held;
    }

    /// <summary>
    /// The finder names a static that holds a plain object of the going side's
    /// type — the fact that keeps <c>ManagedPins</c> honest about a static
    /// holding an object rather than a <see cref="Type"/> or a
    /// <see cref="Delegate"/>.
    /// </summary>
    [Fact]
    public void TheFinderNamesAPlainObjectPin()
    {
        // The specimen static is the test's own (statics to zero left the game
        // assemblies no slot to plant one in): the finder walks every
        // SourceSharp* assembly's statics, this one's included.
        var (context, assembly) = LoadSideCopy("finder-plain-object");
        try
        {
            var type = assembly.GetType("SourceSharp.Server.ServerModule", throwOnError: true)!;
            Assert.NotSame(typeof(ServerModule), type);
            PlainObjectPin.Held = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);

            var pins = ManagedPins.Find(context, limit: 50);

            Assert.Contains(pins, pin =>
                pin.Contains("PlainObjectPin", StringComparison.Ordinal)
                && pin.Contains("Held", StringComparison.Ordinal)
                && pin.Contains("ServerModule", StringComparison.Ordinal));
        }
        finally
        {
            PlainObjectPin.Held = null;
            context.Unload();
        }
    }

    //-------------------------------------------------------------------------
    // Both subjects are internal to SourceSharp.Abi, which does not grant this
    // assembly access. Reflection here rather than an InternalsVisibleTo the
    // shipping ABI carries only for a test.
    //-------------------------------------------------------------------------

    private static bool Invoke_BelongsTo(ScriptedEntityRegistry registry, AssemblyLoadContext context)
    {
        var method = typeof(ScriptedEntityRegistry).GetMethod(
            "BelongsTo", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method!.Invoke(registry, new object[] { context })!;
    }

    private static string? Invoke_Belongs(object value, AssemblyLoadContext context)
    {
        var method = typeof(ManagedPins).GetMethod(
            "Belongs", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (string?)method!.Invoke(null, new object[] { value, context });
    }
}
