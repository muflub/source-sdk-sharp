using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// An attacker chooses its way into the enemy's flag room by what worked (plan_bots_teams_tf2_part_g G.4's "outcomes", lane
/// G4b), on a field of two lanes: the floor of <see cref="TFField"/> (64 by 8 cells of 64 u) with the cells of rows 3 and 4
/// taken out from x 20 to 49, so between the bases a north lane (rows 5 to 7) and a south lane (rows 0 to 2) lead to blue's
/// flag room, whose doors are where the lanes are 1,200 u from the flag.
/// </summary>
public sealed class TFRouteFamilyTests
{
    private static readonly ExperienceKey Key = new("tf_field", "0123456789abcdef", "ctf");

    private static NavGraph TwoLanes()
    {
        var b = new NavGraphBuilder();
        var ids = new CellId[TFField.Length, TFField.Width];
        static bool Wall(int x, int y) => x is >= 20 and < 50 && y is 3 or 4;
        for (int x = 0; x < TFField.Length; x++)
        {
            for (int y = 0; y < TFField.Width; y++)
            {
                ids[x, y] = Wall(x, y) ? CellId.None
                    : b.AddCell(new NavCell(x * TFField.CellSize, y * TFField.CellSize, (x + 1) * TFField.CellSize, (y + 1) * TFField.CellSize, 0f,
                                            ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
            }
        }

        for (int x = 0; x < TFField.Length; x++)
        {
            for (int y = 0; y < TFField.Width; y++)
            {
                if (!ids[x, y].IsValid)
                    continue;
                if (x + 1 < TFField.Length && ids[x + 1, y].IsValid)
                    b.AddBoth(ids[x, y], ids[x + 1, y], NavEdgeType.Walk);
                if (y + 1 < TFField.Width && ids[x, y + 1].IsValid)
                    b.AddBoth(ids[x, y], ids[x, y + 1], NavEdgeType.Walk);
            }
        }

        return b.Build();
    }

    private static readonly NavGraph Lanes = TwoLanes();

    private static RouteFamilies Doors() => RouteFamilies.Around(Lanes, Lanes.FindCell(TFField.BlueFlag + new AiVector(0f, 0f, 1f)).Index);

    private static RouteDoor North => Doors().Doors.Single(d => d.At.Y > 4 * TFField.CellSize);

    private static RouteDoor South => Doors().Doors.Single(d => d.At.Y < 3 * TFField.CellSize);

    /// <summary>A snapshot of the store: none (off), on with no rows, or on with red's tries of the north door and the south door.</summary>
    private static MapExperience Store(bool on = true, (double Tries, double Score)? north = null, (double Tries, double Score)? south = null, int role = TFField.Red)
    {
        if (!on)
            return MapExperience.Empty;
        var rows = new ExperienceDelta();
        if (north is { } n)
            rows.Add(new OutcomeKey(new ExperienceContext(0, role, 3), TFDecider.RouteFamilyKind, North.Bin.Packed), new OutcomeStat(n.Tries, n.Score));
        if (south is { } s)
            rows.Add(new OutcomeKey(new ExperienceContext(0, role, 3), TFDecider.RouteFamilyKind, South.Bin.Packed), new OutcomeStat(s.Tries, s.Score));
        // A row of another table, so an on store with no outcome rows still has a key and a row.
        rows.Add(new CellKey(new ExperienceContext(0, TFField.Red, 3), new WorldBin(0, 0, 0), ExperienceSource.Bot), new CellStat(1d, 0d, 0d, 0d, 0d));
        return new MapExperience(Key, 1, rows);
    }

    private static TFField Attacker(MapExperience experience, ulong seed = 7, float trust = 1f)
    {
        var f = new TFField(slot: 2, seed: seed, floor: Lanes, tune: p => p with { FamilyTrust = trust });
        f.Memory.World.ExperienceSlot.Current = experience;
        return f;
    }

    /// <summary>
    /// The attacker's run from red's side: each decide it moves 120 u along the way to its goal, as the follower would
    /// (straight toward a door, then toward the flag) for <paramref name="decides"/> decides.
    /// </summary>
    private static List<IntentCommand> Run(TFField f, int decides = 30, AiVector? from = null)
    {
        var intents = new List<IntentCommand>();
        AiVector at = from ?? new AiVector(1600f, 256f, 0f);
        for (int d = 0; d < decides; d++)
        {
            IntentCommand i = f.Step(TFField.Self(at), TFField.Ctf());
            intents.Add(i);
            if (i.Move.Kind == MoveKind.Follow)
            {
                AiVector to = i.Move.Corner0 - at;
                at += to.Length <= 120f ? to : to * (120f / to.Length);
            }
        }

        return intents;
    }

    [Fact]
    public void TheFieldHasTwoWaysIntoBluesFlagRoom()
    {
        Assert.Equal(2, Doors().Doors.Count);
        Assert.NotEqual(North.Bin, South.Bin);
    }

    [Fact]
    public void WithOutcomesTheAttackerGoesThroughTheDoorThatWorked()
    {
        int north = Enumerable.Range(1, 30).Count(s =>
        {
            TFField f = Attacker(Store(north: (5d, 5d), south: (5d, 0d)), (ulong)s);
            return Run(f, decides: 1)[0].Move.Corner0 == North.At;
        });
        Assert.InRange(north, 27, 30);
        int south = Enumerable.Range(1, 30).Count(s => Run(Attacker(Store(north: (5d, 0d), south: (5d, 5d)), (ulong)s), decides: 1)[0].Move.Corner0 == South.At);
        Assert.InRange(south, 27, 30);
    }

    [Fact]
    public void AfterTheDoorItGoesForTheFlag()
    {
        TFField f = Attacker(Store(north: (5d, 5d), south: (5d, 0d)));
        List<IntentCommand> intents = Run(f, decides: 40);
        Assert.Equal(North.At, intents[0].Move.Corner0);
        Assert.Equal(TFField.BlueFlag, intents[^1].Move.Corner0);
        Assert.Equal(1, f.Decider.FamilyDraws.Sum());
    }

    [Fact]
    public void TheSameSeedAndStoreDrawTheSameDoor()
    {
        MapExperience store = Store(north: (3d, 1d), south: (3d, 1.5d));
        Assert.Equal(Run(Attacker(store, 4)), Run(Attacker(store, 4)));
        // Over seeds both doors are drawn: it is a draw, not a rule.
        int north = Enumerable.Range(1, 40).Count(s => Run(Attacker(store, (ulong)s), decides: 1)[0].Move.Corner0 == North.At);
        Assert.InRange(north, 5, 35);
    }

    [Fact]
    public void WithNoOutcomeRowsEveryIntentIsAsBefore()
    {
        List<IntentCommand> off = Run(Attacker(Store(on: false)));
        Assert.Equal(off, Run(Attacker(Store())));
        TFField blue = Attacker(Store(north: (5d, 5d), south: (5d, 0d), role: TFField.Blue));
        Assert.Equal(off, Run(blue));
        Assert.Equal(0, blue.Decider.FamilyDraws.Sum());
        // A trust of 0 draws nothing either.
        Assert.Equal(off, Run(Attacker(Store(north: (5d, 5d), south: (5d, 0d)), trust: 0f)));
        // The control: its own side's rows do.
        Assert.NotEqual(off, Run(Attacker(Store(north: (5d, 5d), south: (5d, 0d)))));
        Assert.All(off, i => Assert.Equal(TFField.BlueFlag, i.Move.Corner0));
    }

    [Fact]
    public void WithTheStoreOffNoRunIsKept()
    {
        TFField f = Attacker(Store(on: false));
        Run(f);
        Assert.Equal(0, f.Decider.FamilyRuns);
        Assert.Null(f.Decider.Families);
    }

    /// <summary>A run that came through the south door and died past it is one try of the south door scored a quarter.</summary>
    [Fact]
    public void ADeathPastTheDoorIsATryOfThatDoorScoredAQuarter()
    {
        TFField f = Attacker(Store());
        Run(f, decides: 6, from: South.At + new AiVector(-300f, 0f, 0f));
        Assert.True(f.Decider.FamilyRuns >= 1);
        f.Step(TFField.Self(South.At + new AiVector(200f, 0f, 0f)) with { Flags = SelfFlags.Grounded, Health = 0 }, TFField.Ctf());
        FamilyTry t = Assert.Single(f.Decider.FamilyTries);
        Assert.Equal((South.Bin.Packed, TFDecider.DoorScore, false, TFField.Red), (t.Choice, t.Score, t.Drawn, t.Team));
    }

    [Fact]
    public void APickupIsATryScoredOne()
    {
        TFField f = Attacker(Store());
        Run(f, decides: 6, from: North.At + new AiVector(-300f, 0f, 0f));
        f.Step(TFField.Self(TFField.BlueFlag), TFField.Ctf(blue: ObjectiveState.Carried, blueAt: TFField.BlueFlag, blueCarrier: 2));
        FamilyTry t = Assert.Single(f.Decider.FamilyTries);
        Assert.Equal((North.Bin.Packed, TFDecider.PickedUpScore), (t.Choice, t.Score));
    }

    [Fact]
    public void ADeathBeforeAnyDoorOnAnUndrawnRunIsNoTry()
    {
        TFField f = Attacker(Store());
        Run(f, decides: 2);
        f.Step(TFField.Self(new AiVector(1800f, 256f, 0f)) with { Flags = SelfFlags.Grounded, Health = 0 }, TFField.Ctf());
        Assert.Empty(f.Decider.FamilyTries);
    }
}
