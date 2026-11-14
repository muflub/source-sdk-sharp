using Xunit;

namespace SourceSharp.Ai.Tests.Nav;

/// <summary>
/// The clearance classes past Stand and Crouch (<see cref="ClearanceClass.Hull2"/> to
/// <see cref="ClearanceClass.Hull7"/>): a cell admits a class by that class's own bit and no
/// other, so a body the derivation did not fit somewhere is never taken for a crouched one;
/// what is asked of the first two classes answers as it did, whatever other bits a cell holds.
/// </summary>
public sealed class ClearanceClassTests
{
    private static NavCell Cell(ClearanceClasses classes) => new(0f, 0f, 16f, 16f, 0f, classes, NavCellFlags.None, 1f);

    private static readonly ClearanceClass[] Every =
    [
        ClearanceClass.Stand, ClearanceClass.Crouch, ClearanceClass.Hull2, ClearanceClass.Hull3,
        ClearanceClass.Hull4, ClearanceClass.Hull5, ClearanceClass.Hull6, ClearanceClass.Hull7,
    ];

    /// <summary>Under the old two-way test every class but Stand read as Crouch.</summary>
    [Fact]
    public void ACrouchOnlyCellDoesNotAdmitAnExtraHull() => Assert.False(Cell(ClearanceClasses.Crouch).Admits(ClearanceClass.Hull2));

    [Fact]
    public void ACellAnyoneStandsInAdmitsNoExtraHullItWasNotGiven()
    {
        NavCell cell = Cell(ClearanceClasses.Stand | ClearanceClasses.Crouch);
        Assert.Equal([ClearanceClass.Stand, ClearanceClass.Crouch], Every.Where(cell.Admits));
    }

    [Fact]
    public void EachClassIsAdmittedByItsOwnBitAlone()
    {
        foreach (ClearanceClass c in Every)
            Assert.Equal([c], Every.Where(Cell((ClearanceClasses)(1 << (int)c)).Admits));
    }

    /// <summary>A number that is no class is admitted nowhere (a shift by 32 would read it as Stand).</summary>
    [Fact]
    public void ANumberPastTheLastClassIsAdmittedNowhere()
    {
        NavCell every = Cell((ClearanceClasses)0xFF);
        Assert.Equal((true, false, false, false), (every.Admits(ClearanceClass.Hull7), every.Admits((ClearanceClass)8), every.Admits((ClearanceClass)32), every.Admits((ClearanceClass)255)));
    }

    [Fact]
    public void TheClassesBitsAreTheClassesNumbers()
        => Assert.Equal(((ClearanceClasses)(1 << 2), (ClearanceClasses)(1 << 7), (byte)2, (byte)7),
                        (ClearanceClasses.Hull2, ClearanceClasses.Hull7, (byte)ClearanceClass.Hull2, (byte)ClearanceClass.Hull7));

    [Fact]
    public void ACrouchOnlyCellIsCrouchOnlyWhateverExtraHullsFit()
        => Assert.True(Cell(ClearanceClasses.Crouch | ClearanceClasses.Hull2 | ClearanceClasses.Hull7).CrouchOnly);

    /// <summary>The control: the extra bits do not make a standing cell crouch-only, nor hide that one is.</summary>
    [Fact]
    public void AStandingCellWithExtraHullsIsNotCrouchOnly()
        => Assert.Equal((false, true), (Cell(ClearanceClasses.Stand | ClearanceClasses.Crouch | ClearanceClasses.Hull3).CrouchOnly,
                                        Cell(ClearanceClasses.Crouch).CrouchOnly));

    /// <summary>
    /// A row of three cells, the middle one without the extra hull's bit, and a way round it of
    /// three cells that have it: west, middle, east, then north-west, north, north-east.
    /// </summary>
    private static (NavGraph Graph, int West, int Middle, int East) Row()
    {
        const ClearanceClasses Body = ClearanceClasses.Stand | ClearanceClasses.Crouch, Wide = Body | ClearanceClasses.Hull2;
        var b = new NavGraphBuilder();
        NavCell At(int x, int y, ClearanceClasses classes) => new(x * 32f, y * 32f, x * 32f + 32f, y * 32f + 32f, 0f, classes, NavCellFlags.None, 1f);
        CellId west = b.AddCell(At(0, 0, Wide)), middle = b.AddCell(At(1, 0, Body)), east = b.AddCell(At(2, 0, Wide));
        CellId nw = b.AddCell(At(0, 1, Wide)), n = b.AddCell(At(1, 1, Wide)), ne = b.AddCell(At(2, 1, Wide));
        b.AddBoth(west, middle, NavEdgeType.Walk);
        b.AddBoth(middle, east, NavEdgeType.Walk);
        b.AddBoth(west, nw, NavEdgeType.Walk);
        b.AddBoth(nw, n, NavEdgeType.Walk);
        b.AddBoth(n, ne, NavEdgeType.Walk);
        b.AddBoth(ne, east, NavEdgeType.Walk);
        return (b.Build(), west.Index, middle.Index, east.Index);
    }

    private static int NextTowardsEast(ClearanceClass cls)
    {
        (NavGraph g, int west, _, int east) = Row();
        var field = new FlowField();
        field.Build(g, new CellId(east), cls, new NavObstacleState(0));
        Assert.True(float.IsFinite(field.CostAt(west)), $"no way east for {cls}");
        return field.NextFrom(west).Index;
    }

    [Fact]
    public void AFlowFieldForAnExtraHullGoesRoundACellWithoutItsBit() => Assert.NotEqual(Row().Middle, NextTowardsEast(ClearanceClass.Hull2));

    /// <summary>The control: the body the middle cell admits goes straight through it.</summary>
    [Fact]
    public void AFlowFieldForACrouchedBodyGoesThroughThatCell() => Assert.Equal(Row().Middle, NextTowardsEast(ClearanceClass.Crouch));

    /// <summary>A recording names its nav by the hash (§2.6): a graph classed for more hulls is another graph.</summary>
    [Fact]
    public void AnExtraHullsBitIsPartOfTheGraphsHash()
    {
        NavGraph Of(ClearanceClasses classes)
        {
            var b = new NavGraphBuilder();
            b.AddCell(Cell(classes));
            return b.Build();
        }

        ulong plain = Of(ClearanceClasses.Stand | ClearanceClasses.Crouch).Hash;
        Assert.NotEqual(plain, Of(ClearanceClasses.Stand | ClearanceClasses.Crouch | ClearanceClasses.Hull2).Hash);
        Assert.Equal(plain, Of(ClearanceClasses.Stand | ClearanceClasses.Crouch).Hash);
    }
}
