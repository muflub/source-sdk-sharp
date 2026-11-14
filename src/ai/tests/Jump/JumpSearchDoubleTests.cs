using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The Double family's search (lane G7b): a jump up from standing whose second rocket, fired in the air once the
/// launcher is ready, meets a floor the flight passes over. The world is sized by the jump's own reach today
/// (<see cref="JumpEnvelope"/>): a ledge past a single's rise, and a beam under the way at two thirds of it.
/// </summary>
public class JumpSearchDoubleTests
{
    private const float Edge = 1600f, LineY = 1032f, Gap = 160f, BeamLength = 192f, Beyond = 64f;

    private static readonly Lazy<JumpEnvelope> Reach = new(() => Envelope());

    private static float OnGrid(float h) => 16f * MathF.Round(h / 16f);

    /// <summary>The ledge's height: past what one rocket rises, within what two do.</summary>
    private static float LedgeTop => OnGrid(0.94f * Reach.Value.Rise);

    private static float BeamTop => OnGrid(0.66f * Reach.Value.Rise);

    /// <summary>
    /// The takeoff's floor west of <see cref="Edge"/>, a pit, a beam three columns wide on the line <see cref="Gap"/> past
    /// the edge (none when <paramref name="beam"/> is false), and the ledge <see cref="Beyond"/> past the beam's end.
    /// </summary>
    internal static VoxelColumns World(bool beam = true)
    {
        float b0 = Edge + Gap, b1 = b0 + BeamLength, ledge = b1 + Beyond, beamTop = BeamTop, ledgeTop = LedgeTop;
        return VoxelColumns.Of(0f, 0f, 16f, 256, 128, (x, y) =>
        {
            float cx = x * 16f + 8f;
            if (cx < Edge)
                return [new FreeSpan(0f, 8192f)];
            if (cx >= ledge)
                return [new FreeSpan(ledgeTop, 8192f)];
            if (beam && cx >= b0 && cx < b1 && y >= 63 && y < 66)
                return [new FreeSpan(beamTop, 8192f)];
            return [new FreeSpan(-1000f, 8192f)];
        });
    }

    /// <summary>From a column in from the floor's edge to the ledge's landing, as the pass would ask it.</summary>
    internal static JumpAsk Ask(JumpFamilies families = JumpFamilies.Double)
        => new(new Vector(Edge - 8f, LineY, VoxelMoveWorld.DistEpsilon), new Vector(Edge + Gap + BeamLength + Beyond + 40f, LineY, LedgeTop + VoxelMoveWorld.DistEpsilon), 24f, families);

    [Fact]
    public void TheLedgeIsPastOneRocketsRiseAndTheBeamUnderTheWay()
    {
        Assert.True(LedgeTop > Reach.Value.Rise * 0.9f && LedgeTop < (Reach.Value.Rise + Reach.Value.Lift) * 0.9f, $"the ledge at {LedgeTop}, the reach {Reach.Value}");
        Assert.Null(Search(World()).Find(Ask(JumpFamilies.Both)).Found);
    }

    [Fact]
    public void ADoubleAskFindsASecondRocketOffTheBeamThatMakesTheLedge()
    {
        JumpSearch search = Search(World());
        JumpFound found = Assert.NotNull(search.Find(Ask()).Found);
        Assert.Equal(JumpFamily.Double, found.Family);
        JumpSecondShot second = Assert.NotNull(found.Recipe.Second);
        Assert.InRange(second.Over.X, Edge + Gap, Edge + Gap + BeamLength);
        Assert.InRange(second.Over.Z, BeamTop - 0.5f, BeamTop + 0.5f);

        // Flown as found: it makes the ledge, both rockets' blasts felt; its health is the two.
        JumpFlier flier = Flier(World());
        var track = new List<JumpPoint>();
        JumpOutcome flown = flier.Fly(found.Recipe, found.Recipe.Start(), new JumpSearchSettings().MaxCommands, track);
        Assert.True(search.Made(Ask(), flown));
        JumpImpact[] felt = [.. flier.Sim.Impacts.Where(i => i.Damage > 0f)];
        Assert.Equal(2, felt.Length);
        Assert.Equal(felt.Sum(i => i.Damage), found.Health, 3);
    }

    [Fact]
    public void WithNoFloorUnderTheWayThereIsNoDouble()
    {
        JumpSearchResult none = Search(World(beam: false)).Find(Ask());
        Assert.Null(none.Found);
        Assert.True(none.Flights > 0, "the control: the Double grid was flown");
    }

    [Fact]
    public void AnAskThatDoesNotNameTheDoubleNeverFliesIt()
    {
        // The same ask with the Double grid made another: nothing of a single ask changes, its flights included.
        JumpSearchResult plain = Search(World()).Find(Ask(JumpFamilies.Both));
        JumpSearchResult other = Search(World(), new JumpSearchSettings { Double = new JumpGrid(0f, 45f, 85f, 5f, 0, 4, 1) }).Find(Ask(JumpFamilies.Both));
        Assert.Equal(plain, other);
        Assert.True(Search(World()).Find(Ask()).Flights > 0, "the control: asked by name the Double grid is flown");
    }

    [Fact]
    public void ADoubleIsNotSearchedWhenASingleIsWorthKeeping()
    {
        // A low ledge one rocket makes: asked for every family, the single's answer and its flights, no more.
        VoxelColumns ledge = LedgeField(OnGrid(0.3f * Reach.Value.Rise));
        JumpAsk single = OntoTheLedge(OnGrid(0.3f * Reach.Value.Rise));
        JumpSearchResult both = Search(ledge).Find(single);
        Assert.True(both.Found is { } f && f.Robustness >= new JumpSearchSettings().Keep, "the control: a single is worth keeping");
        Assert.Equal(both, Search(ledge).Find(single with { Families = JumpFamilies.Both | JumpFamilies.Double }));
    }

    [Fact]
    public void ASecondShotsTrialIsDrawnAfterTheFirstShotsSoASinglesTrialsAreTheSame()
    {
        // The first shot's five draws as a single's are, at the Double's own bounds; then the second shot's three.
        var settings = new JumpSearchSettings();
        JumpSearchSettings asDouble = settings with { StartJitter = settings.DoubleStartJitter, ViewJitter = settings.DoubleViewJitter, ShotJitter = settings.DoubleShotJitter };
        Assert.True(asDouble != settings, "the control: a Double's first shot is put off by other bounds");
        uint a = 5, b = 5;
        for (int i = 0; i < 10; i++)
        {
            JumpSearch.Trial one = JumpSearch.Draw(ref a, asDouble), two = JumpSearch.Draw(ref b, settings, second: true);
            Assert.Equal(one, two with { SecondPitch = 0f, SecondYaw = 0f, SecondAlong = 0f });
            Assert.True(two.SecondPitch != 0f && two.SecondAlong != 0f, "the control: the second shot's are drawn");
            b = a;
        }
    }

    [Fact]
    public void ATrialPutsTheSecondShotsViewAndPlaceOff()
    {
        var recipe = new JumpRecipe(Vector.Zero, new Vector(0f, 500f, 0f), 0f, 70f, 180f, 2, true, new JumpSecondShot(new Vector(0f, 200f, 300f), 54, 70, 89f, 180f));
        var trial = new JumpSearch.Trial(0f, 0f, 0f, 0f, 0) { SecondPitch = -2f, SecondYaw = 1.5f, SecondAlong = 10f };
        JumpSecondShot flown = JumpSearch.Perturbed(recipe, trial).Recipe.Second!.Value;
        Assert.Equal((87f, 181.5f), (flown.Pitch, flown.YawOffset));
        // Along the way, north here.
        Assert.Equal(0f, flown.Over.X, 3);
        Assert.Equal((210f, 300f), (flown.Over.Y, flown.Over.Z));
        Assert.Equal((54, 70), (flown.Earliest, flown.Latest));
    }

    [Fact]
    public void ThePassLinksTheFloorToTheLedgeByASecondRocket()
    {
        VoxelColumns columns = World();
        NavGraph graph = GraphOf(columns);
        JumpPlacesResult Pass(JumpEnvelope reach)
        {
            GraphJumpWalk walk = GraphJumpWalk.Of(graph, RunSpeed);
            return new JumpPlaces(graph, columns, Search(columns, walk: walk), walk, reach, JumpPlacesSettings.Of(TFNavProfile.ModNumbers)).Find();
        }

        bool FloorToLedge(JumpLink l) => graph.Cell(new CellId(l.From)).FloorZ < 1f && MathF.Abs(graph.Cell(new CellId(l.To)).FloorZ - LedgeTop) < 1f;
        JumpLink link = Assert.Single(Pass(Reach.Value).Links, FloorToLedge);
        Assert.Equal(JumpFamily.Double, link.Family);
        Assert.NotNull(link.Recipe.Second);

        // The control: told a second rocket lifts nothing, the pass has no way from the floor to the ledge.
        Assert.DoesNotContain(Pass(Reach.Value with { Lift = 0f }).Links, FloorToLedge);
    }
}
