using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>A prediction player clip with no players to clip: what a fact about the rest of the move world hands it.</summary>
internal static class PlayerClips
{
    public static PredictionPlayerClip None()
        => new(new PredictionPlayerClipTests.Players(), Fake(), new PredictionPlayerClipTests.Rules(), () => 0);
}

/// <summary>
/// Plan A.7 / F04 on the client: the prediction's world clips the other
/// players' boxes into the engine's (entity-blind) trace, asking the collide
/// rules the server's trace filter asks. One behaviour per fact.
/// </summary>
public class PredictionPlayerClipTests
{
    private const int PlayerMovement = (int)EngineCollisionGroup.PlayerMovement;
    private const uint MaskPlayerSolid = 0x0201400B;
    private const uint MaskSolidBrushOnly = 0x0000400B;
    private const int Combine = 2;
    private const int Rebels = 3;

    private static readonly Vector HullMin = new(-16f, -16f, 0f);
    private static readonly Vector HullMax = new(16f, 16f, 72f);

    internal sealed class Players(params ClientStandaloneEntity[] all) : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live() => all;

        public ClientStandaloneEntity? GetByIndex(int entityIndex)
            => all.FirstOrDefault(e => e.Handle != EntityHandle.None && e.Handle.EntryIndex == entityIndex);
    }

    internal sealed class Rules(params IEntityCollideRule[] all) : IHooks<IEntityCollideRule>
    {
        public IReadOnlyList<IEntityCollideRule> All => all;
        public void Each(string what, Action<IEntityCollideRule> call) { foreach (var r in all) call(r); }
    }

    private sealed class Rig
    {
        public readonly RecordingEngine Engine = Fake().Returns("EntityRender.IsDormant", false);
        public readonly ClientSsPlayer Mover;
        public readonly ClientSsPlayer Other;
        public readonly ClientPlayerResource Resource;
        public readonly MpTeammatesPassConVar Pass = DeclaredConVars.Fresh<MpTeammatesPassConVar>();
        public readonly PredictionPlayerClip Clip;

        public Rig(int otherTeam, bool pass)
        {
            Mover = Player(1, Vector.Zero);
            Other = Player(2, new Vector(64f, 0f, 0f));
            Resource = new ClientPlayerResource(Engine) { Handle = new EntityHandle(0x0003_0040u) };
            Resource.State.BindLocal();
            Resource.State.SetRow(1, 0, 0, 100, 1, Combine, true, true, true);
            Resource.State.SetRow(2, 0, 0, 100, 2, otherTeam, true, true, true);
            if (pass)
                Pass.Hold("1");

            var standalones = new Players(Mover, Other, Resource);
            Clip = new PredictionPlayerClip(standalones, Engine, new Rules(new ClientTeammatesPassRule(Pass, standalones)), () => 2);
            Clip.BeginCommand(new MoveConstants { HullMin = HullMin, HullMax = HullMax, DuckHullMin = HullMin,
                                                  DuckHullMax = new Vector(16f, 16f, 36f) });
        }

        private ClientSsPlayer Player(int index, Vector origin)
        {
            var player = new ClientSsPlayer(Engine) { Handle = new EntityHandle((uint)(index | (index << 16))) };
            player.State.BindLocal();
            player.State.Origin = origin;
            player.State.Alive = true;
            return player;
        }

        /// <summary>The mover's hull swept 100 units along +X through open space.</summary>
        public MoveTrace Walk(uint mask = MaskPlayerSolid, int group = PlayerMovement)
        {
            Vector end = new(100f, 0f, 0f);
            return Clip.Clip(MoveTrace.Clear(end), Mover.Handle, Vector.Zero, end, HullMin, HullMax, mask, group);
        }
    }

    [Fact]
    public void AnEnemyBlocksThePredictedWalk()
    {
        MoveTrace tr = new Rig(Rebels, pass: true).Walk();

        // The boxes touch 32 units on: the fraction is that, less DIST_EPSILON.
        Assert.Equal((32f - PredictionPlayerClip.DistEpsilon) / 100f, tr.Fraction, 5);
        Assert.Equal(new Vector(-1f, 0f, 0f), tr.PlaneNormal);
    }

    [Fact]
    public void TheBlockingPlayerIsNamedAsTheHitAndAsGround()
    {
        var rig = new Rig(Rebels, pass: true);
        MoveTrace tr = rig.Walk();

        Assert.Equal(rig.Other.Handle, tr.HitEntity);
        Assert.True(tr.HasEntity && tr.HasGround);
        Assert.Equal(2, rig.Clip.LastGroundIndex);
    }

    [Fact]
    public void ATeammatePassesUnderThePublishedAnswer()
    {
        MoveTrace tr = new Rig(Combine, pass: true).Walk();

        Assert.Equal(1f, tr.Fraction);
    }

    [Fact]
    public void ADeathmatchPairStillBlocksInThePrediction()
    {
        MoveTrace tr = new Rig(Combine, pass: false).Walk();

        Assert.True(tr.Fraction < 1f);
    }

    /// <summary>A mask that does not ask for entities (a brush-only trace) is not clipped against a player.</summary>
    [Fact]
    public void ABrushOnlyMaskIsNotClippedAgainstAPlayer()
    {
        var rig = new Rig(Rebels, pass: true);
        Assert.True(rig.Walk().Fraction < 1f);

        Assert.Equal(1f, rig.Walk(MaskSolidBrushOnly).Fraction);
    }

    [Fact]
    public void ADeadPlayerDoesNotBlock()
    {
        var rig = new Rig(Rebels, pass: true);
        rig.Other.State.Alive = false;

        Assert.Equal(1f, rig.Walk().Fraction);
    }

    [Fact]
    public void ADormantPlayerDoesNotBlock()
    {
        var rig = new Rig(Rebels, pass: true);
        rig.Engine.Returns("EntityRender.IsDormant", true);

        Assert.Equal(1f, rig.Walk().Fraction);
    }

    /// <summary>A world trace stopped at its start asks no player, as the engine's trace asks no entity then.</summary>
    [Fact]
    public void AWorldStopAtTheStartIsKept()
    {
        var rig = new Rig(Rebels, pass: true);
        var wall = new MoveTrace { Fraction = 0f, EndPos = Vector.Zero, PlaneNormal = new Vector(-1f, 0f, 0f), HitWorld = true };

        MoveTrace tr = rig.Clip.Clip(wall, rig.Mover.Handle, Vector.Zero, new Vector(100f, 0f, 0f), HullMin, HullMax,
                                     MaskPlayerSolid, PlayerMovement);

        Assert.True(tr.HitWorld);
        Assert.Equal(-1, rig.Clip.LastGroundIndex);
    }

    /// <summary>A nearer world hit wins over a farther player.</summary>
    [Fact]
    public void ANearerWorldHitIsKept()
    {
        var rig = new Rig(Rebels, pass: true);
        var wall = new MoveTrace { Fraction = 0.1f, EndPos = new Vector(10f, 0f, 0f), HitWorld = true, HasEntity = true };

        MoveTrace tr = rig.Clip.Clip(wall, rig.Mover.Handle, Vector.Zero, new Vector(100f, 0f, 0f), HullMin, HullMax,
                                     MaskPlayerSolid, PlayerMovement);

        Assert.Equal(0.1f, tr.Fraction);
        Assert.True(tr.HitWorld);
    }

    [Fact]
    public void StartingInsideAPlayerIsStartSolid()
    {
        var rig = new Rig(Rebels, pass: true);
        rig.Other.State.Origin = new Vector(8f, 0f, 0f);

        MoveTrace tr = rig.Walk();

        Assert.True(tr.StartSolid);
        Assert.Equal(0f, tr.Fraction);
    }

    /// <summary>Before a command names the hulls, no player is clipped: the clip is the old entity-blind trace.</summary>
    [Fact]
    public void BeforeACommandNoPlayerIsClipped()
    {
        var engine = Fake().Returns("EntityRender.IsDormant", false);
        var mover = new ClientSsPlayer(engine) { Handle = new EntityHandle(0x0001_0001u) };
        var other = new ClientSsPlayer(engine) { Handle = new EntityHandle(0x0002_0002u) };
        other.State.BindLocal();
        other.State.Origin = new Vector(64f, 0f, 0f);
        other.State.Alive = true;
        var clip = new PredictionPlayerClip(new Players(mover, other), engine, new Rules(), () => 2);
        Vector end = new(100f, 0f, 0f);

        MoveTrace tr = clip.Clip(MoveTrace.Clear(end), mover.Handle, Vector.Zero, end, HullMin, HullMax, MaskPlayerSolid,
                                 PlayerMovement);

        Assert.Equal(1f, tr.Fraction);
    }

    /// <summary>Standing on a player the clip found names that player as the predicted ground, as the server's last ground does.</summary>
    [Fact]
    public void GroundOnAClippedPlayerNamesThatPlayer()
        => Assert.Equal(2, ClientPlayerPrediction.GroundWriteBack(ClientPlayerPrediction.GroundWireNone, true, 2, 0));

    /// <summary>The engine's ground is kept when no player was the last ground (the rule before the clip).</summary>
    [Fact]
    public void GroundTheEngineNamedIsKeptWithoutAPlayer()
    {
        Assert.Equal(0, ClientPlayerPrediction.GroundWriteBack(ClientPlayerPrediction.GroundWireNone, true, -1, 0));
        Assert.Equal(7, ClientPlayerPrediction.GroundWriteBack(7, true, -1, -1));
        Assert.Equal(ClientPlayerPrediction.GroundWireNone, ClientPlayerPrediction.GroundWriteBack(7, false, 2, 0));
    }

    /// <summary>Only movement: the client's rule still lets a bullet's group (another group) hit a teammate.</summary>
    [Fact]
    public void TheClientRuleStillLetsABulletHitATeammate()
    {
        var rig = new Rig(Combine, pass: true);
        var rule = new ClientTeammatesPassRule(rig.Pass, new Players(rig.Mover, rig.Other, rig.Resource));

        Assert.False(rule.ShouldCollide(rig.Mover.Handle, rig.Other.Handle, (CollisionGroup)PlayerMovement, (int)MaskPlayerSolid));
        Assert.True(rule.ShouldCollide(rig.Mover.Handle, rig.Other.Handle, (CollisionGroup)EngineCollisionGroup.None, (int)MaskPlayerSolid));
    }

    private sealed class UnorderedRule : IEntityCollideRule
    {
        public bool ShouldCollide(EntityHandle mover, EntityHandle entity, CollisionGroup group, int contentsMask) => true;
    }

    /// <summary>
    /// The client's core rule sits on the chain beside a mod's that leaves its
    /// order at 0 (TF2's client rule does): the chain resolves, the two not tying.
    /// </summary>
    [Fact]
    public void TheClientsCoreRuleDoesNotTieWithAModsUnorderedRule()
    {
        var rig = new Rig(Combine, pass: true);
        var services = new ServiceContainer();
        services.AddInstance(new ClientTeammatesPassRule(rig.Pass, new Players(rig.Mover, rig.Other, rig.Resource)));
        services.AddInstance(new UnorderedRule());

        var chain = new Hooks<IEntityCollideRule>(services, new RecordingLog());

        Assert.Equal(2, chain.All.Count);
    }

    /// <summary>A clip member that stops the hull at a wall across +X at <see cref="X"/> (a crate the server stops the walk at).</summary>
    private sealed class Wall(float x, EntityHandle entity) : IPredictionTraceClip
    {
        public float X => x;

        public bool Clip(in Vector start, in Vector end, in Vector mins, in Vector maxs, int mask, out MoveTrace hit)
        {
            hit = default;
            float reach = x - maxs.X;
            if (end.X <= reach)
                return false;
            float t = (reach - start.X) / (end.X - start.X);
            hit = new MoveTrace { Fraction = t, EndPos = new Vector(reach, start.Y, start.Z), PlaneNormal = new Vector(-1f, 0f, 0f), HitEntity = entity };
            return true;
        }
    }

    private sealed class Clips(params IPredictionTraceClip[] all) : IHooks<IPredictionTraceClip>
    {
        public IReadOnlyList<IPredictionTraceClip> All => all;
        public void Each(string what, Action<IPredictionTraceClip> call) { foreach (var c in all) call(c); }
    }

    [Fact]
    public void A_clip_member_nearer_than_every_player_stops_the_predicted_walk_and_is_its_ground()
    {
        var engine = Fake().Returns("EntityRender.IsDormant", false);
        var crate = new EntityHandle(0x0002_00EEu);
        var clip = new PredictionPlayerClip(new Players(), engine, new Rules(), () => 0, clips: new Clips(new Wall(40f, crate)));
        clip.BeginCommand(new MoveConstants { HullMin = HullMin, HullMax = HullMax, DuckHullMin = HullMin, DuckHullMax = new Vector(16f, 16f, 36f) });
        Vector end = new(100f, 0f, 0f);

        MoveTrace tr = clip.Clip(MoveTrace.Clear(end), EntityHandle.None, Vector.Zero, end, HullMin, HullMax, MaskPlayerSolid, PlayerMovement);

        Assert.Equal(0.24f, tr.Fraction, 5);
        Assert.Equal(crate, tr.HitEntity);
        Assert.True(tr.HasEntity && tr.HasGround);
        Assert.Equal(-1, clip.LastGroundIndex);
    }

    [Fact]
    public void A_clip_member_that_stops_nothing_leaves_the_world_answer()
    {
        var engine = Fake().Returns("EntityRender.IsDormant", false);
        var clip = new PredictionPlayerClip(new Players(), engine, new Rules(), () => 0, clips: new Clips(new Wall(400f, EntityHandle.None)));
        clip.BeginCommand(new MoveConstants { HullMin = HullMin, HullMax = HullMax, DuckHullMin = HullMin, DuckHullMax = new Vector(16f, 16f, 36f) });
        Vector end = new(100f, 0f, 0f);

        MoveTrace tr = clip.Clip(MoveTrace.Clear(end), EntityHandle.None, Vector.Zero, end, HullMin, HullMax, MaskPlayerSolid, PlayerMovement);

        Assert.Equal(1f, tr.Fraction);
        Assert.False(tr.HasEntity);
    }
}
