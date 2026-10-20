using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// func_clip_vphysics, the invisible brush that stops physics objects and
/// nothing else.
///
/// <para>
/// The whole entity is three engine calls in one order, and the ORDER is the
/// thing that can be wrong without anything complaining.
/// <c>CStandaloneEntity::InitPhysics</c> — shell_standalone_base.cpp — ends
/// with <c>SetSolidity( SOLID_VPHYSICS, m_nSolidFlags &amp; ~FSOLID_NOT_SOLID )</c>,
/// clearing not-solid on its way out, because everything else that asks the
/// shell for a physics object wants a real wall. So a clip brush that asked for
/// its object AFTER going not-solid is a solid invisible wall — and dm_lockdown
/// has three of them standing in the open, where that would read as the map
/// being broken rather than as this class being wrong.
/// </para>
///
/// <para>
/// Nothing in the class body says "clip". The clipping falls out of
/// <c>PhysicsOwner_t::GetSolidFlags</c> (game/shared/physicsowner.h) answering
/// 0 for an entity outside the SDK, so the solver's own not-solid rule never
/// applies to one. That is a documented neutral rather than an accident, but it
/// is a fact about somebody else's file, so it is asserted from the native side
/// by sourcesharp/test/clip_vphysics_port_test.cpp. This suite asserts the half
/// that is C#'s.
/// </para>
/// </summary>
public class FuncClipVphysicsTests
{
    private static (RecordingEngine Engine, FuncClipVphysics Brush) Spawn()
    {
        var services = Services(out var engine);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncClipVphysics).Assembly);
        registry.Services = services;

        var handle = new EntityHandle(7);
        registry.Spawn(handle, EntityNames.FuncClipVphysics);

        return (engine, (FuncClipVphysics)registry.Get(handle)!);
    }

    /// <summary>
    /// The map-facing name, which is the entire reason this class exists: it is
    /// the last stock C++ classname either shipping map reaches.
    /// </summary>
    [Fact]
    public void ItServesTheClassnameTheMapWrites()
    {
        Assert.True(EntityNames.FuncClipVphysics == "func_clip_vphysics",
            "the classname is spelled the way dm_lockdown spells it");

        var (_, brush) = Spawn();

        Assert.True(brush is not null, "the registry builds one for that classname");
    }

    /// <summary>
    /// The object is asked for BEFORE the brush stops being solid to traces.
    ///
    /// Asserted as an order over the recorded call log rather than as two
    /// separate "was called" checks: both calls happen in either arrangement,
    /// and only the order tells a clip brush from an invisible wall.
    /// </summary>
    [Fact]
    public void ThePhysicsObjectIsMadeBeforeTheBrushGoesNotSolid()
    {
        var (engine, _) = Spawn();

        int madeStatic = IndexOf(engine, "EntityPhysics.SetPhysicsStatic");
        int wentPassable = IndexOf(engine, "EntityPhysics.SetNotSolid");

        Assert.True(madeStatic >= 0,
            "the brush asks the shell for a static physics object — without one it is "
            + "not in the solver at all and stops nothing. Recorded calls: "
            + Log(engine));

        Assert.True(wentPassable >= 0,
            "the brush asks to stop being solid to traces — without that it is an "
            + "invisible wall a player cannot walk through. Recorded calls: " + Log(engine));

        Assert.True(madeStatic < wentPassable,
            "the physics object is made FIRST. CStandaloneEntity::InitPhysics ends with "
            + "SetSolidity( SOLID_VPHYSICS, m_nSolidFlags & ~FSOLID_NOT_SOLID ), so asking "
            + "for the object after going not-solid CLEARS not-solid again and leaves an "
            + "invisible wall. Recorded calls: " + Log(engine));
    }

    /// <summary>
    /// ...and both are asked for with the value that means what the class name
    /// says, on this entity's own handle.
    ///
    /// Without this the order check above would pass just as happily for
    /// <c>SetPhysicsStatic(false)</c> followed by <c>SetNotSolid(false)</c>,
    /// which is a solid simulated crate rather than a clip brush.
    /// </summary>
    /// <summary>
    /// The brush marks its own object to force collision with moveable physics
    /// objects (physics stock parity A5). Stock's CFuncVPhysicsClip answers
    /// ForceVPhysicsCollide with its no-filter rule, a moveable
    /// MOVETYPE_VPHYSICS entity; once the solver sees the brush's real
    /// FSOLID_NOT_SOLID (A6), the force bit is what keeps crates stopping at it
    /// while a player walks through. The bit goes on the object, so after it is
    /// made.
    /// </summary>
    [Fact]
    public void TheObjectIsMarkedToForceCollisionWithMoveablePhysicsObjects()
    {
        var (engine, brush) = Spawn();

        int made = IndexOf(engine, "EntityPhysics.SetPhysicsStatic");
        int marked = IndexOf(engine, "EntityPhysics.AddGameFlags");

        Assert.True(marked > made && made >= 0,
            "the force-collide bit is set on the brush's object after it is made. Recorded calls: " + Log(engine));

        var call = engine.Calls[marked];
        Assert.True(Equals(call.Args[0], brush.Handle) && Equals(call.Args[1], 0x1000u),
            "on the brush's own handle, with kPhysFlagForceCollideMoveable (0x1000): " + call);
    }

    [Fact]
    public void BothAreAskedForWithTheValueThatMeansClipBrush()
    {
        var (engine, _) = Spawn();

        var madeStatic = engine.Last("EntityPhysics.SetPhysicsStatic");
        var wentPassable = engine.Last("EntityPhysics.SetNotSolid");

        Assert.True(madeStatic is not null && wentPassable is not null,
            "both calls were made at all. Recorded calls: " + Log(engine));

        Assert.True(madeStatic!.Value.ArgAs<bool>(1),
            "the object is asked for as STATIC. A dynamic one is a brush the solver "
            + "pushes about, which is a crate rather than a clip");

        Assert.True(wentPassable!.Value.ArgAs<bool>(1),
            "and the brush is asked to go NOT-solid. Solid is an invisible wall");

        Assert.True(madeStatic.Value.ArgAs<EntityHandle>(0).Value == 7
                 && wentPassable.Value.ArgAs<EntityHandle>(0).Value == 7,
            "both are asked about this entity, not about a default handle");
    }

    /// <summary>Where a member appears in the ordered log, or -1.</summary>
    private static int IndexOf(RecordingEngine engine, string member)
    {
        for (int i = 0; i < engine.Calls.Count; i++)
        {
            if (engine.Calls[i].Member == member)
                return i;
        }

        return -1;
    }

    private static string Log(RecordingEngine engine)
        => engine.Calls.Count == 0
            ? "(none)"
            : string.Join(" | ", engine.Calls.Select(c => c.ToString()));
}
