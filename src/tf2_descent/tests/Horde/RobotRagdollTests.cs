using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;
using TF2.Descent.Server.Horde;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// A robot that dies falls as a ragdoll (<see cref="RobotRagdolls"/>): one
/// <c>hl2mp_ragdoll</c> through the core's <see cref="Corpses.Leave"/>, wearing
/// the robot's model and skin, where it stood, thrown by the killing hit; the
/// pooled body hidden at the death and parked at the next decision as before;
/// the cap retiring the oldest; the Tank and the sentry buster leaving none.
/// </summary>
public sealed class RobotRagdollTests
{
    /// <summary>The skins the server was asked to draw entities in.</summary>
    private sealed class Skins : IServerSkins
    {
        public List<(EntityHandle Entity, int Skin)> Set { get; } = [];

        public void SetSkin(EntityHandle entity, int skin) => Set.Add((entity, skin));

        public void SetBody(EntityHandle entity, int body) { }
    }

    private sealed class Setup
    {
        public Setup(int limit)
        {
            Rig.Registry.RegisterAll(typeof(SsRagdoll).Assembly);
            Rig.Engine.ReturnsEachCall("Entities.Create", () =>
            {
                var h = new EntityHandle((1u << 16) | _next++);
                Rig.Registry.Spawn(h, EntityNames.Hl2mpRagdoll);
                Made.Add(h);
                return h;
            });
            Rig.Engine.Returns("Entities.IsValid", true);
            Corpses = new Corpses(Rig.Log, Rig.Engine, Rig.Engine, Rig.Registry, skins: Skins) { Limit = limit };
            Ragdolls = new RobotRagdolls(new RigRules(), new RigHordeCurrent(Rig), Rig.Motion, Corpses);
            Rig.HitMembers.Add(Ragdolls);
        }

        private uint _next = 1500;   // an edict slot: the directory holds edicts below 2048

        public ServerHordeRig Rig { get; } = new();
        public Skins Skins { get; } = new();
        public List<EntityHandle> Made { get; } = [];
        public Corpses Corpses { get; }
        public RobotRagdolls Ragdolls { get; }

        public SsRagdoll Body(int i) => Rig.Registry.Get(Made[i]) as SsRagdoll ?? throw new Xunit.Sdk.XunitException(string.Join(" | ", Rig.Log.Lines.Select(l => l.Text)));
    }

    private static readonly Vector Force = new(0f, 4000f, 1000f);

    private static int Kill(RobotBody body, DamageType type = DamageType.Bullet, Vector force = default) =>
        body.OnDamagedAt(new EntityHandle(1u), 1_000_000f, type, default, force, HitGroup.Chest);

    [Fact]
    public void A_robots_death_leaves_one_ragdoll_of_its_own_model_and_skin_where_it_stood()
    {
        var s = new Setup(12);
        var (_, body) = s.Rig.Lent("soldier", 1.1);

        Kill(body);

        var made = Assert.Single(s.Made);
        Assert.NotEqual(body.Handle, made);
        Assert.Equal(EntityNames.Hl2mpRagdoll, s.Rig.Engine.Last("Entities.Create")!.Value.ArgAs<string>(0));
        Assert.Equal(RobotLooks.SoldierModel, s.Rig.LastFor<string>("EntityRender.SetModel", made, 1));
        Assert.Equal(HordeVectors.ToEngine(body.Position), s.Rig.LastFor<Vector>("Entities.SetOrigin", made, 1));
        Assert.Equal(new Vector(0f, body.Yaw, 0f), s.Rig.LastFor<Vector>("Entities.SetAngles", made, 1));
        Assert.Equal(new[] { (made, RobotLooks.BluSkin) }, s.Skins.Set);
        Assert.Equal(1, s.Ragdolls.Left);
    }

    [Fact]
    public void The_ragdoll_collapses_from_the_sequence_the_robot_was_playing()
    {
        var s = new Setup(12);
        var (_, body) = s.Rig.Lent("scout");
        Assert.True(body.State.Sequence > 0);

        Kill(body);

        Assert.Equal(body.State.Sequence, s.Body(0).State.Sequence);
        Assert.Equal((short)-1, s.Body(0).State.PlayerIndex);
    }

    [Fact]
    public void A_bullet_throws_the_ragdoll_with_its_force_on_the_root()
    {
        var s = new Setup(12);
        var (_, body) = s.Rig.Lent("scout");

        Kill(body, DamageType.Bullet, Force);

        Assert.Equal(Force, s.Body(0).State.Force);
        Assert.Equal(RobotRagdolls.ForceBone, s.Body(0).State.ForceBone);
    }

    [Fact]
    public void A_blast_throws_the_ragdoll_harder_as_stock_does()
    {
        var s = new Setup(12);
        var (_, body) = s.Rig.Lent("scout");

        Kill(body, DamageType.Blast, Force);

        Assert.Equal(new Vector(0f, 10000f, 2000f), s.Body(0).State.Force);
    }

    [Fact]
    public void The_pooled_body_is_hidden_at_the_death_and_parked_at_the_next_decision()
    {
        var s = new Setup(12);
        var (id, body) = s.Rig.Lent("soldier");
        int free = s.Rig.Controller.Pool!.FreePoolBodies;

        Kill(body);

        Assert.False(s.Rig.LastFor<bool>("EntityRender.SetVisible", body.Handle, 1));
        Assert.False(body.Weapon!.Shown);
        Assert.Equal(SolidType.None, s.Rig.LastFor<SolidType>("Entities.SetSolidity", body.Handle, 1));

        s.Rig.Tick((int)s.Rig.Controller.DecideInterval + 1);

        Assert.True(body.Parked);
        Assert.Equal(0, s.Rig.Roster.Body(id.Index));
        Assert.Equal(free + 1, s.Rig.Controller.Pool.FreePoolBodies);
        Assert.Single(s.Made);
    }

    [Fact]
    public void The_cap_retires_the_oldest_ragdoll()
    {
        var s = new Setup(2);
        for (int k = 0; k < 3; k++)
            Kill(s.Rig.Lent("scout").Body);

        Assert.Equal(3, s.Made.Count);
        var removed = Assert.Single(s.Rig.Engine.AllOf("Entities.Remove"));
        Assert.Equal(s.Made[0], removed.ArgAs<EntityHandle>(0));
        Assert.Equal(2, s.Corpses.Count);
    }

    [Fact]
    public void A_ragdoll_is_removed_on_the_server_after_its_lifetime()
    {
        var s = new Setup(12);
        Kill(s.Rig.Lent("heavy").Body);

        Assert.Equal(RobotRagdolls.Lifetime, s.Rig.Registry.Directory.Think.NextThinkTime(s.Made[0], null), 3);
    }

    [Theory]
    [InlineData("tank")]
    [InlineData("sentry_buster")]
    public void A_robot_whose_death_is_not_a_body_leaves_no_ragdoll(string template)
    {
        var s = new Setup(12);
        Kill(s.Rig.Lent(template).Body);

        Assert.Empty(s.Made);
        Assert.Equal(1, s.Ragdolls.Exempt);
    }

    [Fact]
    public void A_giant_leaves_its_giant_model_at_the_models_own_size()
    {
        var s = new Setup(12);
        var (_, body) = s.Rig.Lent("giant_soldier", 1.75);

        Kill(body);

        var made = Assert.Single(s.Made);
        Assert.Equal(RobotLooks.GiantSoldierModel, s.Rig.LastFor<string>("EntityRender.SetModel", made, 1));
        Assert.DoesNotContain(s.Rig.Engine.AllOf("EntityRender.SetModelScale"), c => c.ArgAs<EntityHandle>(0) == made);
    }

    [Fact]
    public void A_hit_that_does_not_kill_leaves_nothing()
    {
        var s = new Setup(12);
        var (_, body) = s.Rig.Lent("heavy");

        body.OnDamagedAt(new EntityHandle(1u), 1f, DamageType.Bullet, default, Force, HitGroup.Chest);

        Assert.Empty(s.Made);
        Assert.True(s.Rig.LastFor<bool>("EntityRender.SetVisible", body.Handle, 1));
    }
}
