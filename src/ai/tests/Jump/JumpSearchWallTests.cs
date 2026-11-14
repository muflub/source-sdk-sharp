using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The Wall family's search (lane G7c): a jump up from standing whose second rocket, fired in the air once the launcher is
/// ready, meets a wall beside the flight. The world is <see cref="JumpSearchDoubleTests"/>' with no beam: the takeoff's
/// floor, a pit, the ledge past one rocket's rise, and a wall along the way on one side of the pit.
/// </summary>
public class JumpSearchWallTests
{
    private const float Edge = 1600f, LineY = 1032f, Ledge = Edge + 160f + 192f + 64f;

    /// <summary>How far the wall's face is from the line, to its south (the second view's side is the search's to find).</summary>
    internal const float Aside = 40f;

    /// <summary>The Double world with no beam, and a wall from the pit's floor to the sky south of the line over the pit (none when <paramref name="wall"/> is false; north of it when <paramref name="north"/>).</summary>
    internal static VoxelColumns World(bool wall = true, bool north = false)
    {
        VoxelColumns plain = JumpSearchDoubleTests.World(beam: false);
        return VoxelColumns.Of(0f, 0f, 16f, 256, 128, (x, y) =>
        {
            float cx = x * 16f + 8f, cy = y * 16f + 8f;
            bool inWall = north ? cy > LineY + Aside : cy < LineY - Aside;
            if (wall && cx >= Edge && cx < Ledge && inWall)
                return [];
            return plain.Spans(x, y).ToArray();
        });
    }

    internal static JumpAsk Ask(JumpFamilies families = JumpFamilies.Wall) => JumpSearchDoubleTests.Ask(families);

    [Fact]
    public void AWallAskFindsASecondRocketAtTheWallThatMakesTheLedge()
    {
        JumpSearch search = Search(World());
        JumpFound found = Assert.NotNull(search.Find(Ask()).Found);
        Assert.Equal(JumpFamily.Wall, found.Family);
        Assert.True(found.Robustness >= search.Settings.Keep, $"robustness {found.Robustness}");
        JumpSecondShot second = Assert.NotNull(found.Recipe.Second);
        Assert.True(second.Wall);
        Assert.True(JumpPilot.WrapDegrees(second.YawOffset) < 0f, $"the second shot looks to the wall's side, south of the way east: yaw offset {second.YawOffset}");

        // Flown as found: it makes the ledge, both rockets' blasts felt, the second's at the wall's face; its health is the two.
        JumpFlier flier = Flier(World());
        JumpOutcome flown = flier.Fly(found.Recipe, found.Recipe.Start(), search.Settings.MaxCommands);
        Assert.True(search.Made(Ask(), flown));
        JumpImpact[] felt = [.. flier.Sim.Impacts.Where(i => i.Damage > 0f)];
        Assert.Equal(2, felt.Length);
        Assert.InRange(felt[1].At.Y, LineY - Aside - 1f, LineY - Aside + 1f);
        Assert.Equal(felt.Sum(i => i.Damage), found.Health, 3);
    }

    [Fact]
    public void AWallOnTheOtherSideIsFoundOnThatSide()
    {
        JumpFound found = Assert.NotNull(Search(World(north: true)).Find(Ask()).Found);
        Assert.Equal(JumpFamily.Wall, found.Family);
        Assert.True(JumpPilot.WrapDegrees(Assert.NotNull(found.Recipe.Second).YawOffset) > 0f, $"yaw offset {found.Recipe.Second?.YawOffset}");
    }

    [Fact]
    public void WithNoWallThereIsNoWallJump()
    {
        Assert.NotNull(Search(World()).Find(Ask()).Found);
        Assert.Null(Search(World(wall: false)).Find(Ask(JumpFamilies.Both | JumpFamilies.Double | JumpFamilies.Wall)).Found);
    }

    [Fact]
    public void WithTheFamilySwitchedOffNothingElseMakesIt()
    {
        Assert.Equal(JumpFamily.Wall, Assert.NotNull(Search(World()).Find(Ask(JumpFamilies.Both | JumpFamilies.Double | JumpFamilies.Wall)).Found).Family);
        Assert.Null(Search(World()).Find(Ask(JumpFamilies.Both | JumpFamilies.Double)).Found);
    }

    [Fact]
    public void AFloorUnderTheWayIsTriedFirstAndAWallNotSearchedAfterIt()
    {
        // The beam's world: the Double family answers, and the Wall family spends nothing on top of it.
        JumpSearchResult both = Search(JumpSearchDoubleTests.World()).Find(JumpSearchDoubleTests.Ask(JumpFamilies.Double | JumpFamilies.Wall));
        JumpSearchResult alone = Search(JumpSearchDoubleTests.World()).Find(JumpSearchDoubleTests.Ask(JumpFamilies.Double));
        Assert.Equal(JumpFamily.Double, Assert.NotNull(both.Found).Family);
        Assert.Equal(alone.Flights, both.Flights);
        Assert.Equal(alone.Found, both.Found);
    }

    [Fact]
    public void ARefinedWallHitIsSteppedAlongItsOwnSecondView()
    {
        // One first shot (85 degrees, delay 0) and two second views (58 degrees down, straight across: north, then south): no
        // grid flight makes the ledge; the refinement round the nearest miss (along the south view, at the wall) does at 80
        // degrees, and the half-pitch step from that answer, flown along the south view again, makes it quicker at 75. Flown
        // along the grid's first view (north, no wall) the step meets nothing and the refined 80 stays the answer.
        var settings = new JumpSearchSettings
        {
            WallFirst = new JumpGrid(0f, 85f, 85f, 10f, 0, 0, 2), WallSecondPitchFrom = 58f, WallSecondPitches = 1, WallSecondYawFrom = 90f, WallSecondYaws = 1,
        };
        JumpFound found = Assert.NotNull(Search(World(), settings).Find(Ask()).Found);
        Assert.Equal(JumpFamily.Wall, found.Family);
        Assert.True(JumpPilot.WrapDegrees(Assert.NotNull(found.Recipe.Second).YawOffset) < 0f, $"yaw offset {found.Recipe.Second?.YawOffset}");
        Assert.Equal(75f, found.Recipe.Pitch);
        Assert.True(found.Robustness >= settings.Keep, $"robustness {found.Robustness}");
    }

    [Fact]
    public void ItSpendsNoMoreThanItsFlights()
    {
        var settings = new JumpSearchSettings { WallMaxFlights = 90 };
        JumpSearchResult result = Search(World(), settings).Find(Ask());
        Assert.InRange(result.Flights, 1, settings.WallMaxFlights);
        Assert.InRange(Search(World()).Find(Ask()).Flights, settings.WallMaxFlights + 1, new JumpSearchSettings().WallMaxFlights);
    }
}
