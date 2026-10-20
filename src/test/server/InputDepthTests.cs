using SourceSharp.Abi;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// An entity whose input fires the same input straight back at itself.
///
/// Contrived here; not contrived in the game. The shells route the use key
/// through the input system, and CBaseEntity declares an input called "Use"
/// whose handler calls Use() — so before this guard existed, pressing the use
/// key on anything WITHOUT a managed Use handler looped between the two until
/// the stack ran out.
/// </summary>
[ScriptedEntity("test_recursive_input")]
public sealed class RecursiveInputEntity : ScriptedEntity
{
    public static ScriptedEntityRegistry? Registry;

    public int Entered;

    [ScriptedEvent("Loop")]
    private void OnLoop()
    {
        Entered++;
        Registry?.Input(Handle, "Loop", "", 0f, EntityHandle.None);
    }
}

/// <summary>An entity that reads its own registry's and a watched registry's input depth.</summary>
[ScriptedEntity("test_depth_probe")]
public sealed class DepthProbeEntity : ScriptedEntity
{
    public ScriptedEntityRegistry? Watched;
    public int Own = -1;
    public int Other = -1;

    public override bool OnInput(string name, string value, float number, EntityHandle activator)
    {
        Own = Owner?.InputDepth ?? -1;
        Other = Watched?.InputDepth ?? -1;
        return true;
    }
}

/// <summary>
/// The guard on input nesting.
///
/// It exists because of how badly the alternative fails. A cycle overflows the
/// managed stack, and CoreCLR answers a stack overflow by printing
/// "Stack overflow." and aborting the process WITHOUT running any handler —
/// no exception, no trace, no clue which entity was responsible. An exception
/// at a fixed depth turns that into a named entity and a named input.
/// </summary>
public class InputDepthTests
{
    /// <summary>
    /// A registry with one self-firing entity in it, and the entity.
    ///
    /// <para>
    /// <c>RecursiveInputEntity.Registry</c> is process-wide, so it is set here
    /// and each fact that clears it puts it back. The bulk fact this replaces
    /// set it once, cleared it at the end for its last assertion, and left it
    /// null — which was harmless only because nothing after it looked.
    /// </para>
    /// </summary>
    private static (ScriptedEntityRegistry Registry, EntityHandle Handle, RecursiveInputEntity Entity) Looping()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(RecursiveInputEntity).Assembly);
        registry.Services = Services();
        RecursiveInputEntity.Registry = registry;

        var handle = new EntityHandle(1);
        registry.Spawn(handle, "test_recursive_input");

        return (registry, handle, (RecursiveInputEntity)registry.Get(handle)!);
    }

    /// <summary>
    /// Fires the loop and returns the innermost message, or "" if nothing threw.
    ///
    /// Reflection wraps whatever the handler threw, which is exactly what the
    /// diagnostics in Entry walk — so this walks it the same way.
    /// </summary>
    private static string Loop(ScriptedEntityRegistry registry, EntityHandle handle)
    {
        try
        {
            registry.Input(handle, "Loop", "", 0f, EntityHandle.None);
            return "";
        }
        catch (Exception e)
        {
            string message = "";

            for (var at = e; at is not null; at = at.InnerException)
                message = at.Message;

            return message;
        }
    }

    [Fact]
    public void ASelfFiringInputIsStoppedRatherThanOverflowing()
    {
        var (registry, handle, _) = Looping();

        Assert.True(Loop(registry, handle).Length > 0, "a self-firing input is stopped rather than overflowing");
    }

    [Fact]
    public void AndTheEntityThatDidItIsNamed()
    {
        var (registry, handle, _) = Looping();

        Assert.True(Loop(registry, handle).Contains("test_recursive_input", StringComparison.Ordinal), "and the entity that did it is named");
    }

    [Fact]
    public void AndSoIsTheInput()
    {
        var (registry, handle, _) = Looping();

        Assert.True(Loop(registry, handle).Contains("'Loop'", StringComparison.Ordinal), "and so is the input");
    }

    [Fact]
    public void ItStopsAtABoundedDepth()
    {
        // It has to stop somewhere short of the stack.
        var (registry, handle, entity) = Looping();
        _ = Loop(registry, handle);

        Assert.True(entity.Entered is > 8 and < 128, "it stops at a bounded depth");
    }

    [Fact]
    public void AndTheDepthIsUnwoundAfterwards()
    {
        // Having stopped, the guard must leave the counter where it found it —
        // one that leaked its own depth would refuse every later input in the
        // map.
        var (registry, handle, _) = Looping();
        _ = Loop(registry, handle);

        Assert.True(registry.InputDepth == 0, "and the depth is unwound afterwards");
    }

    [Fact]
    public void ALaterInputStillRuns()
    {
        // The counter having unwound is only useful if the next input works.
        var (registry, handle, entity) = Looping();
        _ = Loop(registry, handle);

        entity.Entered = 0;
        RecursiveInputEntity.Registry = null;

        try
        {
            registry.Input(handle, "Loop", "", 0f, EntityHandle.None);

            Assert.True(entity.Entered == 1, "a later input still runs");
        }
        finally
        {
            RecursiveInputEntity.Registry = registry;
        }
    }

    /// <summary>
    /// The depth is the registry's own (statics to zero: it was a thread-static
    /// every registry on the game thread shared): an input running in one
    /// game instance's registry leaves another's at 0.
    /// </summary>
    [Fact]
    public void AnInputRaisesOnlyItsOwnRegistrysDepth()
    {
        var mine = new ScriptedEntityRegistry(TestEngines.Server());
        mine.RegisterAll(typeof(DepthProbeEntity).Assembly);
        mine.Services = Services();
        var other = new ScriptedEntityRegistry(TestEngines.Server());

        var handle = new EntityHandle(2);
        mine.Spawn(handle, "test_depth_probe");
        var probe = (DepthProbeEntity)mine.Get(handle)!;
        probe.Watched = other;

        Assert.True(mine.Input(handle, "Probe", "", 0f, EntityHandle.None));

        Assert.Equal(1, probe.Own);    // the stimulus reached the registry it ran in
        Assert.Equal(0, probe.Other);
    }
}
