using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using TF2.Server.Entities.Rooms;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;
using TF2.Tests.Entities.Items;
using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests.Entities.Rooms;

/// <summary>
/// The respawn-room visualizer's solidity (tf2 F07): the per-entity trace arm
/// (<see cref="TFRespawnRoomCollide"/>, stock's
/// <c>CFuncRespawnRoomVisualizer::ShouldCollide</c>, func_respawnroom.cpp:494-523)
/// asked through the server's real trace filter, with the mask the movement
/// arm carries (<c>TFMovementRules.PlayerSolidMask</c>: the OTHER team's bit
/// over the stock mask) — an enemy is blocked, a teammate passes, the win
/// state and an unassigned wall block nobody, and a non-movement trace (a
/// bullet's) passes the brush.
/// </summary>
public sealed class RespawnRoomVisualizerTests
{
    private const int PlayerMovement = (int)EngineCollisionGroup.PlayerMovement;

    private sealed class Candidates : ITraceCandidates
    {
        public readonly Dictionary<EntityHandle, TraceCandidate> Blocks = [];

        public bool TryRead(EntityHandle entity, out TraceCandidate candidate)
            => Blocks.TryGetValue(entity, out candidate);

        public bool IsWorld(EntityHandle entity) => false;

        public bool IsBrushModel(int modelIndex) => true;
    }

    private static readonly TraceCandidate Brush = new((int)CollisionGroup.None, SolidType.Bsp, 1, EntityHandle.None);

    private static TraceFilterRules Filter(TFRespawnRoomCollide rule, EntityHandle wall)
    {
        var blocks = new Candidates();
        blocks.Blocks[wall] = Brush;
        return TraceFilterRules.Over(blocks, CollisionRules.BuildMatrix(),
                                     new ListHooks<IEntityCollideRule>(rule));
    }

    private static (TFRespawnRoomCollide Rule, MapItemRig Map, FuncRespawnRoomVisualizer Wall) Rig(TFTeam team)
    {
        var map = new MapItemRig();
        var wall = map.Spawn<FuncRespawnRoomVisualizer>(FuncRespawnRoomVisualizer.MapClass,
                                                        ("TeamNum", ((int)team).ToString()));
        return (new TFRespawnRoomCollide(map.Registry, map.Round), map, wall);
    }

    private static bool HitBy(TraceFilterRules filter, EntityHandle wall, uint mask, int group = PlayerMovement)
    {
        TraceFilterQuery query = filter.Resolve(default, group);
        return filter.ShouldHitEntity(query, wall.Value, (int)mask);
    }

    /// <summary>The mover's mask, the movement arm's word: the OTHER team's bit over the stock mask.</summary>
    private static uint MovementMask(TFTeam mover)
        => mover switch
        {
            TFTeam.Red => GameMovement.MaskPlayerSolid | TFContents.BlueTeam,
            TFTeam.Blue => GameMovement.MaskPlayerSolid | TFContents.RedTeam,
            _ => GameMovement.MaskPlayerSolid,
        };

    /// <summary>
    /// An enemy is blocked: a red player's movement mask (the blue bit) asked
    /// about a blue room's wall through the filter with the rule chained —
    /// the rule admits it, the mask carrying the wall's own team's bit.
    /// </summary>
    [Fact]
    public void AnEnemyIsBlockedByTheVisualizer()
    {
        var (rule, _, wall) = Rig(TFTeam.Blue);
        var filter = Filter(rule, wall.Handle);

        Assert.True(HitBy(filter, wall.Handle, MovementMask(TFTeam.Red)));
    }

    /// <summary>
    /// A teammate passes: the blue player's mask (the red bit) asked about the
    /// same blue wall — no blue bit in it, the rule refuses the hit and the
    /// teammate walks through his own room's door.
    /// </summary>
    [Fact]
    public void ATeammatePassesTheVisualizer()
    {
        var (rule, _, wall) = Rig(TFTeam.Blue);
        var filter = Filter(rule, wall.Handle);

        Assert.False(HitBy(filter, wall.Handle, MovementMask(TFTeam.Blue)));
    }

    /// <summary>
    /// The win state opens the rooms (stock's first arm): in
    /// <see cref="RoundState.TeamWin"/> the wall blocks nobody, the mask
    /// notwithstanding.
    /// </summary>
    [Fact]
    public void NothingIsBlockedDuringTeamWin()
    {
        var (rule, map, wall) = Rig(TFTeam.Blue);
        map.Round.State = RoundState.TeamWin;
        var filter = Filter(rule, wall.Handle);

        Assert.False(HitBy(filter, wall.Handle, MovementMask(TFTeam.Red)));
    }

    /// <summary>
    /// A trace in any group but player-movement passes the wall (stock's
    /// closing arm): a bullet's path is not the wall's business, the brush
    /// being there to stop walkers.
    /// </summary>
    [Fact]
    public void BulletsPassTheVisualizer()
    {
        var (rule, _, wall) = Rig(TFTeam.Blue);
        var filter = Filter(rule, wall.Handle);

        Assert.False(HitBy(filter, wall.Handle, GameMovement.MaskPlayerSolid, (int)EngineCollisionGroup.Player));
    }

    /// <summary>
    /// A wall with no team yet blocks nobody (stock's TEAM_UNASSIGNED arm):
    /// the room's RoundActivate has not named it.
    /// </summary>
    [Fact]
    public void AnUnassignedVisualizerBlocksNobody()
    {
        var (rule, _, wall) = Rig(TFTeam.Unassigned);
        var filter = Filter(rule, wall.Handle);

        Assert.False(HitBy(filter, wall.Handle, MovementMask(TFTeam.Red)));
    }

    /// <summary>
    /// An entity that is not a visualizer is left as it was: the rule stands
    /// aside and the chain's other arms and the matrix answer, stock's
    /// hierarchy carrying no arm for them here.
    /// </summary>
    [Fact]
    public void TheRuleLeavesEveryOtherEntityToTheRestOfTheChain()
    {
        var (rule, map, _) = Rig(TFTeam.Blue);
        var room = map.Spawn<FuncRespawnRoom>(FuncRespawnRoom.MapClass, ("TeamNum", "3"));

        Assert.True(rule.ShouldCollide(EntityHandle.None, room.Handle, (CollisionGroup)PlayerMovement, (int)MovementMask(TFTeam.Red)));
    }

    /// <summary>
    /// The wall is a brush the traces meet (stock's SetActive(true) clearing
    /// FSOLID_TRIGGER and FSOLID_NOT_SOLID, func_respawnroom.cpp:527-535 with
    /// its "we're a trigger, but we want to be solid" note): the spawn leaves
    /// a SetBrushSolidity of Solid behind, or the rule above answers a filter
    /// that never reaches the wall.
    /// </summary>
    [Fact]
    public void TheBrushSpawnsSolidToTraces()
    {
        var (_, map, _) = Rig(TFTeam.Blue);

        var solidity = map.Engine.AllOf("EntityPhysics.SetBrushSolidity").Select(c => c.Arg(1)).ToList();
        Assert.NotEmpty(solidity);
        Assert.Contains(BrushSolidity.Solid, solidity);
    }

    /// <summary>
    /// Taken off solid (stock's SetActive(false) adding both flags back), the
    /// wall is a trigger again: <c>InputSetSolid 0</c>, the map's own switch.
    /// </summary>
    [Fact]
    public void AVisualizerTakenOffSolidIsATrigger()
    {
        var (_, map, wall) = Rig(TFTeam.Blue);
        map.Input(wall, "SetSolid", "0");

        var solidity = map.Engine.AllOf("EntityPhysics.SetBrushSolidity").Select(c => c.Arg(1)).ToList();
        Assert.Contains(BrushSolidity.Trigger, solidity);
    }

    /// <summary>
    /// Stock's <c>UpdateTransmitState</c> (<c>FL_EDICT_ALWAYS</c>, its FULLCHECK
    /// arm commented out, func_respawnroom.cpp:464-469): the brush reaches every
    /// eye on the wire and is drawn wherever an eye looks — the hiding is the
    /// client's own answer at its draw, so the server writes transmit-always and
    /// visible at the spawn, and again at every on/off switch.
    /// </summary>
    [Fact]
    public void TheBrushIsSentAndDrawnForEveryEye()
    {
        var (_, map, wall) = Rig(TFTeam.Blue);

        Assert.Contains(true, map.Engine.AllOf("EntityRender.SetTransmitAlways").Select(c => c.Arg(1)).ToList());
        Assert.Contains(true, map.Engine.AllOf("EntityRender.SetVisible").Select(c => c.Arg(1)).ToList());

        map.Input(wall, "SetSolid", "0");
        Assert.Contains(true, map.Engine.AllOf("EntityRender.SetVisible").Select(c => c.Arg(1)).ToList());
    }

    /// <summary>
    /// The team rides the wire at the spawn: it is what the client's draw
    /// compares the local player against.
    /// </summary>
    [Fact]
    public void TheVisualizerPutsItsTeamOnTheWire()
    {
        var (_, _, wall) = Rig(TFTeam.Blue);

        Assert.Equal((byte)(int)TFTeam.Blue, wall.State.Team);
    }
}
