using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Entities;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Entities;

/// <summary>
/// <see cref="SniperDot"/>: the dot rides the owner's aim — each think traces
/// from the eye along it to <c>MAX_TRACE_LENGTH</c>
/// (<c>UpdateSniperDot</c>, <c>tf_weapon_sniperrifle.cpp:964-999</c>), sits
/// where the trace stopped, names what stopped it when it is neither the
/// world nor nothing, and dies with its owner (<c>CSniperDot::Update</c>'s
/// guard, :1471-1476).
/// </summary>
public sealed class SniperDotTests
{
    private static readonly EntityHandle Dot = new(0x0004_0021u);
    private static readonly EntityHandle Owner = new(0x0001_0003u);
    private static readonly EntityHandle Hit = new(0x0002_0007u);

    /// <summary>The world: entry 0, named on purpose (EntityHandle's rule).</summary>
    private static readonly EntityHandle World = new(0u);

    private static readonly Vector Eye = new(64f, 128f, 200f);
    private static readonly Vector TraceEnd = new(100f, 128f, 200f);

    private static (SniperDot Dot, RecordingEngine Engine) Rig()
    {
        var engine = TestServices.Fake();
        var dot = new SniperDot(engine, engine);
        dot.Handle = Dot;
        dot.BindAreas(TestServices.Areas(engine));
        dot.NetStates = NetStateBinding.AttachLocal(dot, dot.Handle, new NetStateFields(new RecordingLog()));
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.GetIndex", 0)
              .Returns("Players.GetTeam", (int)TFTeam.Unassigned)
              .Returns("Players.IsAlive", true)
              .Returns("Entities.GetEyePosition", Eye)
              .Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f))
              .Returns("WorldQuery.TraceLine",
                       new TraceResult(true, 0.25f, TraceEnd, new Vector(0f, 1f, 0f), Hit));
        return (dot, engine);
    }

    [Fact]
    public void Being_dressed_arms_the_first_think()
    {
        var (dot, _) = Rig();

        dot.Begin(EntityHandle.None, 0f);

        Assert.Equal(0f, dot.NextThinkDelay());
    }

    [Fact]
    public void Being_dressed_publishes_the_owner_team_and_charge_start_to_the_wire_state()
    {
        var (dot, engine) = Rig();
        engine.Returns("Entities.GetIndex", 7)
              .Returns("Players.GetTeam", (int)TFTeam.Blue);

        dot.Begin(Owner, 12.5f);

        Assert.Equal(7, dot.State.OwnerIndex);
        Assert.Equal((byte)TFTeam.Blue, dot.State.Team);
        Assert.Equal(12.5f, dot.State.ChargeStartTime);
    }

    [Fact]
    public void A_dressing_without_owner_publishes_nobody()
    {
        var (dot, _) = Rig();

        dot.Begin(EntityHandle.None, 0f);

        Assert.Equal(0, dot.State.OwnerIndex);
    }

    [Fact]
    public void Being_dressed_names_the_owner_to_the_engine()
    {
        var (dot, engine) = Rig();

        dot.Begin(Owner, 0f);

        var set = engine.Calls.Single(c => c.Member == "Entities.SetOwner");
        Assert.Equal(Dot, set.ArgAs<EntityHandle>(0));
        Assert.Equal(Owner, set.ArgAs<EntityHandle>(1));
    }

    [Fact]
    public void A_think_moves_the_dot_to_where_the_owners_aim_stopped()
    {
        var (dot, engine) = Rig();
        dot.Owner = Owner;

        dot.OnThink();

        var trace = engine.Calls.Single(c => c.Member == "WorldQuery.TraceLine");
        Assert.Equal(Eye, trace.ArgAs<Vector>(0));
        // Level eye angles (forward +X) out to MAX_TRACE_LENGTH: the diagonal of the world cube.
        Assert.Equal(Eye + new Vector(56756f, 0f, 0f), trace.ArgAs<Vector>(1));
        Assert.Equal(TraceMask.ShotNoWindows, trace.ArgAs<TraceMask>(2));
        Assert.Equal(Owner, trace.ArgAs<EntityHandle>(3));

        var set = engine.Calls.Single(c => c.Member == "Entities.SetOrigin");
        Assert.Equal(Dot, set.ArgAs<EntityHandle>(0));
        Assert.Equal(TraceEnd, set.ArgAs<Vector>(1));

        Assert.Equal(0f, dot.NextThinkDelay());
    }

    [Fact]
    public void The_target_is_what_the_trace_stopped_on_and_the_normal_with_it()
    {
        var (dot, _) = Rig();
        dot.Owner = Owner;

        dot.OnThink();

        Assert.Equal(Hit, dot.Target);
        Assert.Equal(new Vector(0f, 1f, 0f), dot.Normal);
    }

    [Fact]
    public void A_hit_on_the_world_names_no_target()
    {
        var (dot, engine) = Rig();
        dot.Owner = Owner;
        engine.Returns("WorldQuery.TraceLine",
                       new TraceResult(true, 1f, TraceEnd, new Vector(0f, 1f, 0f), World));

        dot.OnThink();

        Assert.Equal(EntityHandle.None, dot.Target);
    }

    [Fact]
    public void A_miss_names_no_target()
    {
        var (dot, engine) = Rig();
        dot.Owner = Owner;
        engine.Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, TraceEnd, default, EntityHandle.None));

        dot.OnThink();

        Assert.Equal(EntityHandle.None, dot.Target);
    }

    [Fact]
    public void A_dead_owner_takes_the_dot_with_them()
    {
        var (dot, engine) = Rig();
        dot.Owner = Owner;
        engine.Returns("Players.IsAlive", false);

        dot.OnThink();

        Assert.Equal(Dot, engine.Calls.Single(c => c.Member == "Entities.Remove").ArgAs<EntityHandle>(0));
        Assert.Equal(-1f, dot.NextThinkDelay());
    }

    [Fact]
    public void A_gone_owner_takes_the_dot_with_them()
    {
        var (dot, engine) = Rig();
        dot.Owner = Owner;
        engine.Returns("Entities.IsValid", false);

        dot.OnThink();

        Assert.Equal(Dot, engine.Calls.Single(c => c.Member == "Entities.Remove").ArgAs<EntityHandle>(0));
    }
}
