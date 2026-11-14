using SourceSharp.Ai.TF2.Jump;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// A search with a hint (lane G6b): a jump a player showed, read as a single family's numbers, is where the search looks
/// first, and never what is flown. On <see cref="JumpRig.LedgeField"/>, the ledge a share of the simulator's rise today.
/// </summary>
public class JumpSearchHintTests
{
    /// <summary>A ledge 0.6 of the jump's rise, on the 16 u grid, and its ask from 480 u before it: past the Forward grid's best (its pitches stop at 60).</summary>
    private static (float Height, JumpAsk Ask) FarLedge()
    {
        float height = 16f * MathF.Round(0.6f * Envelope().Rise / 16f);
        return (height, OntoTheLedge(height, standoff: 480f));
    }

    /// <summary>A person's running jump at a pitch past the Forward grid's (70 against its 30 to 60).</summary>
    private static readonly JumpHint Steep = new(JumpFamily.Forward, 160f, 70f, 180f, 0);

    [Fact]
    public void AHintPastTheGridsFindsAJumpTheGridsAloneDoNot()
    {
        (float height, JumpAsk ask) = FarLedge();
        JumpSearch search = Search(LedgeField(height));
        float keep = search.Settings.Keep;
        JumpSearchResult plain = search.Find(ask);
        Assert.True(plain.Found is not { } p || p.Robustness < keep, $"the control: the grids alone find {plain.Found}");

        JumpSearchResult hinted = search.Find(ask with { Hint = Steep });
        JumpFound found = Assert.NotNull(hinted.Found);
        Assert.True(found.Robustness >= keep, $"robustness {found.Robustness}");
        Assert.Equal(JumpFamily.Forward, found.Family);
        Assert.InRange(found.Recipe.Pitch, Steep.Pitch - 5f, Steep.Pitch + 5f);
        JumpOutcome again = search.Fly(found.Recipe);
        Assert.True(search.Made(ask, again), $"it rests at {again.Rest}");
    }

    [Fact]
    public void AHintThatMissesIsFlownFirstAndTheGridsAnswer()
    {
        float height = 16f * MathF.Round(0.3f * Envelope().Rise / 16f);
        JumpSearch search = Search(LedgeField(height));
        JumpAsk ask = OntoTheLedge(height);
        JumpSearchResult plain = search.Find(ask);
        Assert.NotNull(plain.Found);

        // Looking up: no blast lifts the body, so its nine flights miss and the grids' answer stands.
        JumpSearchResult hinted = search.Find(ask with { Hint = new JumpHint(JumpFamily.Up, 0f, -60f, 180f, 0) });
        Assert.Equal(plain.Found, hinted.Found);
        Assert.Equal(plain.Flights + JumpSearch.HintSide * JumpSearch.HintSide, hinted.Flights);
    }

    [Fact]
    public void AHintOfAFamilyNotAskedIsNotFlown()
    {
        (float height, JumpAsk ask) = FarLedge();
        JumpSearch search = Search(LedgeField(height));
        JumpAsk up = ask with { Families = JumpFamilies.Up };
        JumpSearchResult plain = search.Find(up);
        JumpSearchResult hinted = search.Find(up with { Hint = Steep });
        Assert.Equal((plain.Found, plain.Flights), (hinted.Found, hinted.Flights));
    }
}
