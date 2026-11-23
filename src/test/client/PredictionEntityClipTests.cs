using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The client's prediction clips the solid props and brush entities its
/// engine trace cannot see, deciding as the server's trace filter does
/// (cpbob: standing on a control point's base bobbed the view, the client
/// falling through the prop every command while the server stood on it).
/// One behaviour per fact.
/// </summary>
public class PredictionEntityClipTests
{
    private const int PlayerMovement = (int)EngineCollisionGroup.PlayerMovement;
    private const uint MaskPlayerSolid = 0x0201400B;
    private const uint MaskSolidBrushOnly = 0x0000400B;

    private static readonly Vector HullMin = new(-24f, -24f, 0f);
    private static readonly Vector HullMax = new(24f, 24f, 82f);

    // cp_granary's middle point: the player's feet on the base's top, 7.15 units
    // over the floor (server z -376.82, client's prediction -383.97).
    private static readonly Vector Feet = new(-1536f, 0f, -376.81787f);
    private const int PropIndex = 161;
    private static readonly EntityHandle PropHandle = new(0x0005_00A1u);
    private const float FloorFraction = 1f;

    private static ClientSolidEntity CapBase(int index = PropIndex, int group = (int)EngineCollisionGroup.None,
                                             SolidType solid = SolidType.VPhysics, int model = (int)ModelType.Studio,
                                             Vector? at = null)
    {
        Vector c = at ?? new Vector(-1536f, 0f, -384f);
        return new ClientSolidEntity(index, index == PropIndex ? PropHandle : new EntityHandle((uint)(index | (7 << 16))),
                                     solid, SolidFlags.None, group, model,
                                     c + new Vector(-90f, -90f, 0f), c + new Vector(90f, 90f, 7.2f));
    }

    /// <summary>What native's clip answers for the prop: the hull's ground trace stops on its top at once.</summary>
    private static MoveTrace OnTop(Vector start) => new()
    {
        Fraction = 0f,
        EndPos = start,
        PlaneNormal = new Vector(0f, 0f, 1f),
        PlaneDist = start.Z,
        Contents = 1,
        HasEntity = true,
        HasGround = true,
    };

    private sealed class Rig
    {
        public readonly RecordingEngine Engine = Fake();
        public readonly PredictionEntityClip Entities;
        public readonly PredictionPlayerClip Clip;
        public readonly EntityHandle Mover = new(0x0001_0001u);

        public Rig(params ClientSolidEntity[] solids) : this(new PredictionPlayerClipTests.Rules(), solids) { }

        public Rig(PredictionPlayerClipTests.Rules rules, params ClientSolidEntity[] solids)
        {
            Engine.Returns("WorldQuery.ClientSolidEntities", solids);
            Entities = new PredictionEntityClip(Engine, rules, () => 2);
            Clip = new PredictionPlayerClip(new PredictionPlayerClipTests.Players(), Engine, rules, () => 2, Entities);
            Clip.BeginCommand(new MoveConstants { HullMin = HullMin, HullMax = HullMax, DuckHullMin = HullMin,
                                                  DuckHullMax = new Vector(24f, 24f, 62f) });
        }

        public Rig Answering(int hit, MoveTrace trace)
        {
            Engine.Returns("WorldQuery.ClientClipHullToEntities", ((int, MoveTrace)?)(hit, trace));
            return this;
        }

        /// <summary>CategorizePosition's ground trace: two units down from the feet, the world's floor 7.15 below.</summary>
        public MoveTrace Ground(uint mask = MaskPlayerSolid, int group = PlayerMovement, MoveTrace? world = null)
        {
            Vector end = Feet - new Vector(0f, 0f, 2f);
            return Clip.Clip(world ?? MoveTrace.Clear(end), Mover, Feet, end, HullMin, HullMax, mask, group);
        }

        public int[] Asked()
            => Engine.Calls.Where(c => c.Member == "WorldQuery.ClientClipHullToEntities")
                     .Select(c => (int[])c.Args[5]!).SelectMany(a => a).ToArray();

        public int ClipCalls() => Engine.Calls.Count(c => c.Member == "WorldQuery.ClientClipHullToEntities");

        public int Listings() => Engine.Calls.Count(c => c.Member == "WorldQuery.ClientSolidEntities");
    }

    [Fact]
    public void APlayerOnACapPointBaseStandsOnIt()
    {
        var rig = new Rig(CapBase()).Answering(PropIndex, OnTop(Feet));

        MoveTrace tr = rig.Ground();

        Assert.True(tr.Fraction == 0f && tr.HasGround && tr.PlaneNormal.Z == 1f,
                    $"the ground trace stops on the prop's top (fraction {tr.Fraction}, ground {tr.HasGround})");
    }

    [Fact]
    public void ThePropIsTheGroundTheWriteBackNames()
    {
        var rig = new Rig(CapBase()).Answering(PropIndex, OnTop(Feet));

        MoveTrace tr = rig.Ground();

        Assert.Equal(PropIndex, rig.Clip.LastGroundIndex);
        Assert.Equal(PropHandle, tr.HitEntity);
    }

    [Fact]
    public void AStoppedGroundTraceAsksForThePropAlone()
    {
        var rig = new Rig(CapBase(), CapBase(index: 300, at: new Vector(4000f, 0f, 0f))).Answering(PropIndex, OnTop(Feet));

        rig.Ground();

        Assert.Equal([PropIndex], rig.Asked());
    }

    [Fact]
    public void ADebrisPropIsPassedAsTheServerPassesIt()
    {
        var rig = new Rig(CapBase(group: (int)EngineCollisionGroup.Debris)).Answering(PropIndex, OnTop(Feet));

        MoveTrace tr = rig.Ground();

        Assert.True(rig.ClipCalls() == 0 && tr.Fraction == FloorFraction,
                    "a debris can is solid on both sides and still walked through by a player's movement");
    }

    [Fact]
    public void ABoxSolidEntityIsLeftOut()
    {
        var rig = new Rig(CapBase(solid: SolidType.BoundingBox)).Answering(PropIndex, OnTop(Feet));

        rig.Ground();

        Assert.Equal(0, rig.ClipCalls());
    }

    [Fact]
    public void APlayerIndexIsThePlayerClipsNotThisOnes()
    {
        var rig = new Rig(CapBase(index: 2)).Answering(2, OnTop(Feet));

        rig.Ground();

        Assert.Equal(0, rig.ClipCalls());
    }

    [Fact]
    public void ACollideRuleThatRefusesThePropIsObeyed()
    {
        var rig = new Rig(new PredictionPlayerClipTests.Rules(new Refuse(PropHandle)), CapBase())
            .Answering(PropIndex, OnTop(Feet));

        rig.Ground();

        Assert.Equal(0, rig.ClipCalls());
    }

    [Fact]
    public void AStudioPropIsPassedByAMaskThatAsksForNoEntities()
    {
        var rig = new Rig(CapBase()).Answering(PropIndex, OnTop(Feet));

        rig.Ground(mask: MaskSolidBrushOnly);

        Assert.Equal(0, rig.ClipCalls());
    }

    [Fact]
    public void ABrushEntityStopsAMaskThatAsksForNoEntities()
    {
        var rig = new Rig(CapBase(solid: SolidType.Bsp, model: (int)ModelType.Brush)).Answering(PropIndex, OnTop(Feet));

        rig.Ground(mask: MaskSolidBrushOnly);

        Assert.Equal([PropIndex], rig.Asked());
    }

    [Fact]
    public void AWorldStopSoonerThanThePropKeepsTheWorld()
    {
        Vector end = Feet - new Vector(0f, 0f, 2f);
        MoveTrace world = MoveTrace.Clear(end);
        world.Fraction = 0.25f;
        MoveTrace prop = OnTop(Feet);
        prop.Fraction = 0.5f;
        var rig = new Rig(CapBase()).Answering(PropIndex, prop);

        MoveTrace tr = rig.Ground(world: world);

        Assert.True(tr.Fraction == 0.25f && rig.Clip.LastGroundIndex == -1, "the world's nearer stop stands");
    }

    [Fact]
    public void AWorldTraceBlockedAtItsStartAsksNoEntity()
    {
        Vector end = Feet - new Vector(0f, 0f, 2f);
        MoveTrace world = MoveTrace.Clear(end);
        world.Fraction = 0f;
        world.StartSolid = true;
        var rig = new Rig(CapBase()).Answering(PropIndex, OnTop(Feet));

        rig.Ground(world: world);

        Assert.Equal(0, rig.ClipCalls());
    }

    [Fact]
    public void ThePartitionIsReadOncePerCommand()
    {
        var rig = new Rig(CapBase()).Answering(PropIndex, OnTop(Feet));

        rig.Ground();
        rig.Ground();

        Assert.Equal(1, rig.Listings());
    }

    [Fact]
    public void AnotherCommandReadsThePartitionAgain()
    {
        var rig = new Rig(CapBase()).Answering(PropIndex, OnTop(Feet));

        rig.Ground();
        rig.Clip.BeginCommand(new MoveConstants { HullMin = HullMin, HullMax = HullMax });
        rig.Ground();

        Assert.Equal(2, rig.Listings());
    }

    [Fact]
    public void APartitionLargerThanTheBufferIsReadWhole()
    {
        // 300 far-off props before the cap base, more than the first buffer holds.
        ClientSolidEntity[] many = Enumerable.Range(0, 300)
            .Select(i => CapBase(index: 400 + i, at: new Vector(8000f + i * 200f, 0f, 0f)))
            .Append(CapBase()).ToArray();
        var rig = new Rig(many).Answering(PropIndex, OnTop(Feet));

        rig.Ground();

        Assert.Equal([PropIndex], rig.Asked());
    }

    [Fact]
    public void AGroupOutsideTheSharedTableCollides()
    {
        Assert.True(PredictionEntityClip.GroupsCollide(PlayerMovement, CollisionRules.GroupCount + 2));
    }

    private sealed class Refuse(EntityHandle refused) : IEntityCollideRule
    {
        public bool ShouldCollide(EntityHandle mover, EntityHandle entity, CollisionGroup group, int contentsMask)
            => entity != refused;
    }
}
