using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A side's load context must actually die when it is unloaded.
///
/// Hot reload is the whole reason the modules live in collectible contexts, and
/// a context that will not unload fails silently: the old code keeps running,
/// the new code loads beside it, and the only symptom is memory. The bootstrap
/// checks this at runtime and warns; this catches it before the game does.
///
/// The ABI is the dangerous half. It lives in the DEFAULT context and is shared
/// by both sides, so anything it holds about a side's types outlives that side
/// by construction — one entry in one cache keyed by a game Type is enough to
/// pin the whole context forever.
/// </summary>
[Collection(ProcessWide.Name)]
public class UnloadTests
{
    [Fact]
    public void ASidesContextUnloadsAfterTheAbiHasCachedItsTypes()
    {
        var weak = LoadTouchAndUnload();

        if (weak is null)
            return;

        // Collected OUTSIDE the frame that held the context: the local would
        // still be live in there, and the check would report a leak that is only
        // the stack frame — the same false alarm the bootstrap was caught by.
        for (int i = 0; i < 10 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.True(!weak.IsAlive, "a side's context unloads after the ABI has cached its types");
    }

    /// <summary>
    /// Loads the shared assembly into a collectible context of its own, puts its
    /// types through the caches a running side would, and unloads it.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference? LoadTouchAndUnload()
    {
        var side   = new SideContext(collectible: true);
        var shared = side.FromBin("SourceSharp.Server");

        if (shared is null)
        {
            Assert.Fail("a collectible side context can load the shared assembly");
            return null;
        }

        // P1 run 3: the caches live in the side's OWN copy of
        // SourceSharp.Shared.Interop now, loaded into its context beside
        // Shared; only Abi is shared. The premise restated: a side's context
        // unloads after its own engine surface has cached its types, and the
        // default context's copy holds none of them.
        var interop = SideCopy.Interop(side);
        var stateType = shared.GetTypes().FirstOrDefault(
            t => t.GetCustomAttributes(interop.GetType("SourceSharp.Abi.StandaloneNetStateAttribute", true)!, false).Length > 0);

        if (stateType is null)
        {
            Assert.Fail("the shared assembly declares a networked state");
            return null;
        }

        // What a side does at startup and at every spawn: ask its engine surface
        // about its own types. Both answers are cached against the Type that asked.
        var state = SideCopy.Static(interop, "StandaloneNetStates", "Of", stateType)!;
        var bound = SideCopy.Static(interop, "NetStateBinding", "FieldsOn", stateType);
        int fields = ((System.Collections.ICollection)state.GetType().GetProperty("Fields")!.GetValue(state)!).Count;

        Assert.True(fields > 0 && bound is not null && SideCopy.DefaultHoldsNothingFrom(side),
            "the side's own copy answered about its type, and the default copy holds none of the side's types");

        var weak = new WeakReference(side, trackResurrection: true);

        side.Unload();

        return weak;
    }
}

/// <summary>
/// The caches Forget was NOT clearing, found by bisecting the fake-engine
/// harness's section F: the client ALC survived unload, and the leaking side
/// flipped to the server when the client's Initialize was cut short — so the
/// root was an ABI static written by BOTH sides, last writer wins. The writer
/// was StandaloneNetStates.Discover, which fills s_ByTypeId beside the two maps
/// Forget does clear, and ToleranceOf, which fills s_Tolerances on the first
/// misprediction describe. One surviving entry pins the side's whole context.
///
/// UnloadTests.Covers above never saw it because it routes types through Of and
/// FieldsOn only — the caches Forget already knew about.
/// </summary>
[Collection(ProcessWide.Name)]
public class UnloadAfterDiscoverTests
{

    [Fact]
    public void DiscoveredAndDescribedTypesDoNotPinTheContext()
    {
        var (weak, typeId, defaultHeldNothing) = LoadDiscoverDescribeAndUnload();

        if (weak is null)
            return;

        for (int i = 0; i < 10 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.True(!weak.IsAlive, "a side's context unloads after Discover filled the type-id map and a describe filled the tolerance cache");

        // And no map the default context keeps was written with the side's
        // types: the side's discovery and describe stayed in its own copy
        // (restated in P1 run 3; it read "a forgotten type id describes as
        // unknown" when the one map was Abi's and both sides wrote it).
        Assert.True(defaultHeldNothing && typeId != 0,
                    "the default context's maps held none of the side's types while it was loaded");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference?, uint, bool) LoadDiscoverDescribeAndUnload()
    {
        var side   = new SideContext(collectible: true);
        var shared = side.FromBin("SourceSharp.Server");

        if (shared is null)
        {
            Assert.Fail("a collectible side context can load the shared assembly");
            return (null, 0, false);
        }

        // What both modules do at startup: Discover, which fills the classname
        // map, the TYPE-ID map and the by-type cache from this context's types,
        // in the side's OWN copy of the engine surface since P1 run 3.
        var interop = SideCopy.Interop(side);
        var states = ((System.Collections.IEnumerable)SideCopy.Static(interop, "StandaloneNetStates", "Discover", shared, new RecordingLog())!)
                     .Cast<object>()
                     .Select(s => (State: s, Type: (Type)s.GetType().GetProperty("Type")!.GetValue(s)!,
                                   ClassName: (string)s.GetType().GetProperty("ClassName")!.GetValue(s)!,
                                   Fields: ((System.Collections.IEnumerable)s.GetType().GetProperty("Fields")!.GetValue(s)!).Cast<object>().ToList()))
                     .Where(s => s.Type.Assembly == shared)
                     .ToArray();

        Assert.True(states.Length > 0, "the side context's copy of the shared assembly declares networked states");

        // Proved, not assumed: the entries just written hold THIS context's
        // types — a copy resolved from the default context would make the leak
        // check below pass whatever Forget does.
        Assert.True(AssemblyLoadContext.GetLoadContext(states[0].Type.Assembly) == side,
                    "the discovered states are the side context's own");

        // What the prediction meter does on the first misprediction: describe a
        // byte of a declared field, which caches the type's tolerances.
        uint typeId = 0;
        bool described = false;
        object catalog = SideCopy.Catalog(interop, shared);

        foreach (var state in states)
        {
            foreach (var field in state.Fields)
            {
                uint id = NetSchema.TypeIdOf(state.ClassName);
                int offset = (int)field.GetType().GetProperty("Offset")!.GetValue(field)!;

                if (SideCopy.DescribeOffset(catalog, id, offset) == 1)
                {
                    typeId = id;
                    described = true;
                    break;
                }
            }

            if (described)
                break;
        }

        Assert.True(described, "a declared field's first byte describes as that field");
        bool defaultHeldNothing = SideCopy.DefaultHoldsNothingFrom(side);

        var weak = new WeakReference(side, trackResurrection: true);

        side.Unload();
        return (weak, typeId, defaultHeldNothing);
    }
}

/// <summary>
/// A side context's own copies, reached by reflection (P1 run 3): the engine
/// surface is SourceSharp.Shared.Interop, loaded per side, so a side's types
/// are cached in its own copy, and the types this test binds statically are
/// the default context's.
/// </summary>
internal static class SideCopy
{
    public static Assembly Interop(AssemblyLoadContext side)
    {
        var interop = side.LoadFromAssemblyName(new AssemblyName("SourceSharp.Shared.Interop"));
        Assert.True(AssemblyLoadContext.GetLoadContext(interop) == side, "the side has its own copy of the engine surface");
        return interop;
    }

    public static object? Static(Assembly assembly, string type, string method, params object[] args)
    {
        var t = assembly.GetType("SourceSharp.Abi." + type, throwOnError: true)!;
        var m = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                 .Single(x => x.Name == method && x.GetParameters().Length == args.Length
                              && x.GetParameters().Zip(args).All(p => p.First.ParameterType.IsInstanceOfType(p.Second)));
        return m.Invoke(null, args);
    }

    /// <summary>A side's own <c>NetStateCatalog</c>, over its own copy of the declarations (statics to zero).</summary>
    public static object Catalog(Assembly interop, Assembly declarations)
        => Activator.CreateInstance(interop.GetType("SourceSharp.Abi.NetStateCatalog", true)!, declarations, new RecordingLog())!;

    public static int DescribeOffset(object catalog, uint typeId, int offset)
    {
        var m = catalog.GetType().GetMethod("DescribeOffset")!;
        var args = new object?[m.GetParameters().Length];
        args[0] = typeId;
        args[1] = offset;
        return (int)m.Invoke(catalog, args)!;
    }

    /// <summary>
    /// Whether the default context's net-state types could hold a Type from
    /// <paramref name="side"/>: since statics to zero they have no static field
    /// that holds an object at all, so the answer is structural. The side
    /// argument keeps the call sites' shape; the scan's reach is checked on a
    /// type that does have one.
    /// </summary>
    public static bool DefaultHoldsNothingFrom(AssemblyLoadContext side)
    {
        _ = side;
        static bool NoObjectStatics(Type t)
            => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .All(f => f.IsLiteral || f.FieldType.IsValueType);

        Assert.False(NoObjectStatics(typeof(StaticStateWaivers)), "the scan reads a type's object-holding statics");
        return NoObjectStatics(typeof(StandaloneNetStates)) && NoObjectStatics(typeof(NetStateCatalog))
            && NoObjectStatics(typeof(NetStateBinding)) && NoObjectStatics(typeof(NetStateFields))
            && NoObjectStatics(typeof(SaveFieldCatalog));
    }
}
