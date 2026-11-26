using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// Round 2's S14 (round2.md §4): carriers and guards that stood still on ctf_2fort, each cause held by a fact. A guard of
/// its own dropped flag is a defender, never an attacker; a defend spot is one the defender walks to from its flag.
/// </summary>
public sealed class TFObjectiveStandTests
{
    /// <summary>
    /// The bot's own flag dropped 200 u from it while a teammate carries the enemy's: an attacker goes with the carrier (the
    /// Escort's 0.6), where before the Guard's 0.7 turned it to stand on its own flag until it went home. Stock's CTF bots
    /// all go on with the enemy's flag.
    /// </summary>
    [Fact]
    public void AnAttackerByItsDroppedFlagGoesOnWithItsCarrier()
    {
        var f = new TFField();
        f.Step(TFField.Self(At), Flags());
        Assert.Equal(TFTeamRole.Attack, f.Decider.Role);
        Assert.Equal(TFDecider.Escort, f.Branch);
    }

    /// <summary>The same place and flags: a defender guards its dropped flag (the positive arm: Guard still wins for the role whose job it is).</summary>
    [Fact]
    public void ADefenderByItsDroppedFlagGuardsIt()
    {
        var f = TFField.Defender();
        IntentCommand i = f.Step(TFField.Self(At), Flags());
        Assert.Equal(TFTeamRole.Defend, f.Decider.Role);
        Assert.Equal(TFDecider.Guard, f.Branch);
        Assert.Equal((MoveKind.Follow, Dropped), (i.Move.Kind, i.Move.Corner0));
    }

    private static readonly AiVector At = new(340f, 256f, 0f), Dropped = new(540f, 256f, 0f), Carrier = new(2400f, 256f, 0f);

    /// <summary>Red's flag dropped at <see cref="Dropped"/>, blue's carried by red's slot 4 at <see cref="Carrier"/>.</summary>
    private static ObjectiveKnowledge Flags() => TFField.Ctf(red: ObjectiveState.Dropped, redAt: Dropped, blue: ObjectiveState.Carried, blueAt: Carrier, blueCarrier: 4);

    /// <summary>
    /// The field with a floor 400 u under its north side past the red flag (y 512 to 768, where the field has none), with no
    /// way to it from the field: ctf_2fort's intel room, whose spot drawn 384 u round blue's flag fell over a sealed floor
    /// under the room. Every defend spot the defender draws is on the field it walks; and some draws land past the field's
    /// edge, where the floor under is the pit (the stimulus: they are moved, not drawn elsewhere).
    /// </summary>
    [Fact]
    public void EveryDefendSpotIsOneTheDefenderWalksToFromItsFlag()
    {
        NavGraph g = FieldOverAPit();
        var f = new TFField(slot: 3, mates: [1, 2], floor: g);
        var spots = new HashSet<(int, int)>();
        int moved = 0;
        for (int i = 0; i < 2400; i++)
        {
            f.Step(TFField.Self(TFField.RedFlag), TFField.Ctf());
            Assert.Equal(TFDecider.Defend, f.Branch);
            Assert.True(f.Decider.Doing.HasGoal);
            AiVector spot = f.Decider.Doing.Goal;
            if (!spots.Add(((int)spot.X, (int)spot.Y)))
                continue;
            CellId under = g.FindCell(spot + new AiVector(0f, 0f, 1f));
            Assert.True(under.IsValid && g.Cell(under).FloorZ == 0f, $"the defend spot {spot} is over {(under.IsValid ? g.Cell(under).FloorZ : float.NaN)}, not the field");
            // A spot moved off the pit is the centre of the field's nearest cell: its north row's.
            if (spot.Y == (TFField.Width - 0.5f) * TFField.CellSize && spot.X % TFField.CellSize == TFField.CellSize / 2f)
                moved++;
        }

        Assert.True(spots.Count >= 20, $"{spots.Count} spots drawn");
        Assert.True(moved > 0, "no spot drawn over the pit was moved to the field's nearest cell");
    }

    /// <summary>
    /// The field's floor 60 u under the flag's place (a flag on a stand, as ctf_2fort's at z -131 over a floor at -192): every
    /// defend spot is on the floor, not at the flag's height, so the follower can arrive at it.
    /// </summary>
    [Fact]
    public void EveryDefendSpotIsOnItsCellsFloor()
    {
        NavGraph g = SunkField(-60f);
        var f = new TFField(slot: 3, mates: [1, 2], floor: g);
        for (int i = 0; i < 600; i++)
        {
            f.Step(TFField.Self(TFField.RedFlag with { Z = -60f }), TFField.Ctf());
            Assert.Equal(TFDecider.Defend, f.Branch);
            AiVector spot = f.Decider.Doing.Goal;
            Assert.True(spot.Z == -60f, $"the defend spot {spot} is not on the floor at z -60");
        }
    }

    /// <summary><see cref="TFField.Floor"/>'s cells and walks with their floor at <paramref name="z"/>.</summary>
    private static NavGraph SunkField(float z)
    {
        var b = new NavGraphBuilder();
        var ids = new CellId[TFField.Length, TFField.Width];
        for (int x = 0; x < TFField.Length; x++)
        {
            for (int y = 0; y < TFField.Width; y++)
            {
                ids[x, y] = b.AddCell(new NavCell(x * TFField.CellSize, y * TFField.CellSize, (x + 1) * TFField.CellSize, (y + 1) * TFField.CellSize, z,
                                                  ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
                if (x > 0)
                    b.AddBoth(ids[x - 1, y], ids[x, y], NavEdgeType.Walk);
                if (y > 0)
                    b.AddBoth(ids[x, y - 1], ids[x, y], NavEdgeType.Walk);
            }
        }

        return b.Build();
    }

    /// <summary>
    /// A hurt bot midway between two packs 1,024 u either side, stepping back and forth across the middle (as on ctf_2fort's
    /// stairs, where each step it took toward one pack made the other the nearer by the straight line): it keeps the pack its
    /// visit began with, where before it turned at every step.
    /// </summary>
    [Fact]
    public void AHurtBotBetweenTwoPacksKeepsTheOneItWentFor()
    {
        AiVector west = new(1024f, 64f, 0f), east = new(3072f, 64f, 0f);
        var f = new TFField(packs: [new ItemSpawn(0, "item_healthkit_medium", ItemKind.HealthMedium, west, 10f),
                                    new ItemSpawn(1, "item_healthkit_medium", ItemKind.HealthMedium, east, 10f)]);
        var goals = new HashSet<AiVector>();
        for (int i = 0; i < 20; i++)
        {
            f.Step(TFField.Self(new AiVector(i % 2 == 0 ? 2049f : 2047f, 64f, 0f), health: 60), TFField.Ctf());
            Assert.Equal(TFDecider.Survive, f.Branch);
            goals.Add(f.Decider.Doing.Goal);
        }

        Assert.Equal([east], goals);
    }

    /// <summary>The latch holds a place only while it is one: the east pack seen taken mid-visit, the bot turns for the west one.</summary>
    [Fact]
    public void AVisitsPackSeenTakenIsLetGo()
    {
        AiVector west = new(1024f, 64f, 0f), east = new(3072f, 64f, 0f);
        var f = new TFField(packs: [new ItemSpawn(0, "item_healthkit_medium", ItemKind.HealthMedium, west, 10f),
                                    new ItemSpawn(1, "item_healthkit_medium", ItemKind.HealthMedium, east, 10f)]);
        f.Step(TFField.Self(new AiVector(2049f, 64f, 0f), health: 60), TFField.Ctf());
        Assert.Equal(east, f.Decider.Doing.Goal);
        f.Memory.Apply(AiEvent.ItemSeen(f.Bot, new Tick(f.Tick), 1, false));
        f.Step(TFField.Self(new AiVector(2049f, 64f, 0f), health: 60), TFField.Ctf());
        Assert.Equal((TFDecider.Survive, west), (f.Branch, f.Decider.Doing.Goal));
    }

    /// <summary>
    /// The latch is the visit's only: a bot that went for the east pack, was healed (other branches ran), and is hurt again
    /// beside the west one goes for the west one.
    /// </summary>
    [Fact]
    public void AVisitThatEndedHoldsNoPlace()
    {
        AiVector west = new(1024f, 64f, 0f), east = new(3072f, 64f, 0f);
        var f = new TFField(packs: [new ItemSpawn(0, "item_healthkit_medium", ItemKind.HealthMedium, west, 10f),
                                    new ItemSpawn(1, "item_healthkit_medium", ItemKind.HealthMedium, east, 10f)]);
        f.Step(TFField.Self(new AiVector(2049f, 64f, 0f), health: 60), TFField.Ctf());
        Assert.Equal((TFDecider.Survive, east), (f.Branch, f.Decider.Doing.Goal));
        for (int i = 0; i < 5; i++)
            f.Step(TFField.Self(new AiVector(1500f, 64f, 0f)), TFField.Ctf());
        Assert.NotEqual(TFDecider.Survive, f.Branch);
        // Hurt again: once the Fetch it is on gives way to Survive (its commitment), the pack is the west one.
        for (int i = 0; i < 40 && f.Branch != TFDecider.Survive; i++)
            f.Step(TFField.Self(new AiVector(1500f, 64f, 0f), health: 60), TFField.Ctf());
        Assert.Equal((TFDecider.Survive, west), (f.Branch, f.Decider.Doing.Goal));
    }

    /// <summary><see cref="TFField.Floor"/>, and 4 x 12 cells of 64 u at z -400 north of it (y 512 to 768), joined to each other only.</summary>
    private static NavGraph FieldOverAPit()
    {
        var b = new NavGraphBuilder();
        var ids = new CellId[TFField.Length, TFField.Width];
        const ClearanceClasses Both = ClearanceClasses.Stand | ClearanceClasses.Crouch;
        for (int x = 0; x < TFField.Length; x++)
        {
            for (int y = 0; y < TFField.Width; y++)
                ids[x, y] = b.AddCell(new NavCell(x * TFField.CellSize, y * TFField.CellSize, (x + 1) * TFField.CellSize, (y + 1) * TFField.CellSize, 0f, Both, NavCellFlags.None, 1f));
        }

        for (int x = 0; x < TFField.Length; x++)
        {
            for (int y = 0; y < TFField.Width; y++)
            {
                if (x + 1 < TFField.Length)
                    b.AddBoth(ids[x, y], ids[x + 1, y], NavEdgeType.Walk);
                if (y + 1 < TFField.Width)
                    b.AddBoth(ids[x, y], ids[x, y + 1], NavEdgeType.Walk);
            }
        }

        var pit = new CellId[12, 4];
        for (int x = 0; x < 12; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                float wy = (TFField.Width + y) * TFField.CellSize;
                pit[x, y] = b.AddCell(new NavCell(x * TFField.CellSize, wy, (x + 1) * TFField.CellSize, wy + TFField.CellSize, -400f, Both, NavCellFlags.None, 1f));
                if (x > 0)
                    b.AddBoth(pit[x - 1, y], pit[x, y], NavEdgeType.Walk);
                if (y > 0)
                    b.AddBoth(pit[x, y - 1], pit[x, y], NavEdgeType.Walk);
            }
        }

        return b.Build();
    }
}
