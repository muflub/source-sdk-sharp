using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The interval rules, and the header they now share them with.
///
/// <para>
/// plan_shared.md Phase 3 took <c>game/shared/interval.cpp</c> out of both
/// projects; its two functions are inline in <c>game/shared/interval.h</c> and
/// the rules they encode are <see cref="Interval"/>. Two things can go wrong
/// with that arrangement and these pin both: the rules can be wrong, and the
/// two copies of them can drift.
/// </para>
/// </summary>
public class IntervalTests
{
    [Fact]
    public void AnIntervalStringWithoutACommaHasZeroRange()
    {
        Interval fixedThree = Interval.Parse("3");

        Assert.Equal(3f, fixedThree.Start);
        Assert.Equal(0f, fixedThree.Range);

        // And a zero range is a FIXED value rather than a broken one: a great
        // many intervals in the game's scripts are written with no comma.
        Assert.Equal(3f, fixedThree.Draw(NeverAsked));
    }

    [Fact]
    public void TheStoredSecondNumberIsARangeAndNotAnEnd()
    {
        // "3,5" means between 3 and 5. Stored as start 3 and range 2 — reading
        // the stored pair as (start, end) would draw between 3 and 8, which is
        // right for a fixed value and wrong for every interval anybody wrote.
        Interval span = Interval.Parse("3,5");

        Assert.Equal(3f, span.Start);
        Assert.Equal(2f, span.Range);
        Assert.Equal(5f, span.End);
    }

    [Fact]
    public void TheRangeIsSubtractedInDoubleAsAtofReturnsIt()
    {
        // ReadInterval: tmp.range = atof( token ) - tmp.start, a double minus a
        // float, stored into a float. "0.5,0.9" is therefore 0.4f
        // (0.400000006), not 0.9f - 0.5f (0.39999998); the client soundscape
        // oracle (soundscape-stock.tsv) records stock asking RandomFloat(0, 0.400000006).
        Interval span = Interval.Parse("0.5,0.9");

        Assert.True(BitConverter.SingleToInt32Bits(span.Range) == BitConverter.SingleToInt32Bits((float)(0.9 - 0.5)),
                    $"range {span.Range:R}, stock's {(float)(0.9 - 0.5):R}");
    }

    [Fact]
    public void LeadingWhitespaceInAFieldIsSkippedAsAtofSkipsIt()
    {
        // The shipped soundscapes write "time" "2, 7"; atof( " 7" ) is 7.
        Interval span = Interval.Parse("2, 7");

        Assert.True(span.Start == 2f && span.Range == 5f, $"start {span.Start} range {span.Range}");
    }

    [Fact]
    public void EmptyFieldsAreSkippedAsStrtokSkipsThem()
    {
        Interval leading = Interval.Parse(",5");
        Interval doubled = Interval.Parse("3,,5");

        Assert.True(leading.Start == 5f && leading.Range == 0f, $",5: start {leading.Start} range {leading.Range}");
        Assert.True(doubled.Start == 3f && doubled.Range == 2f, $"3,,5: start {doubled.Start} range {doubled.Range}");
    }

    [Fact]
    public void AnExponentIsReadAsAtofReadsIt()
    {
        Interval span = Interval.Parse("1e1,2.5E1");

        Assert.True(span.Start == 10f && span.Range == 15f, $"start {span.Start} range {span.Range}");
    }

    [Fact]
    public void AZeroRangeIntervalNeverTouchesTheRandomStream()
    {
        // Not an optimisation. Taking a number out of the engine's RNG stream
        // moves every draw after it, so a fixed interval that consulted the
        // stream would change what an unrelated system rolled next. NeverAsked
        // fails the test if it is called.
        Assert.Equal(7f, new Interval(7f, 0f).Draw(NeverAsked));
    }

    [Fact]
    public void ADrawIsTheStartPlusSomethingWithinTheRange()
    {
        // The bounds are the argument, and they are what native passes:
        // RandomFloat( 0, range ), added to start. A draw asked for (start, end)
        // instead would be right only when start is 0.
        var asked = new List<(float, float)>();

        float v = new Interval(3f, 2f).Draw((lo, hi) => { asked.Add((lo, hi)); return hi; });

        Assert.Equal((0f, 2f), Assert.Single(asked));
        Assert.Equal(5f, v);
    }

    [Fact]
    public void TextWithNoNumberInItIsAFixedZeroRatherThanAFailure()
    {
        // atof's answer, and stock's behaviour: a script with a typo gives a
        // fixed zero and the delay it described simply does not happen. An
        // exception here would be a different behaviour wearing the same name.
        Assert.Equal(default, Interval.Parse("nonsense"));
        Assert.Equal(default, Interval.Parse(""));
        Assert.Equal(default, Interval.Parse(null));
    }

    [Fact]
    public void ALeadingNumberIsReadAndTheRestIsIgnored()
    {
        Assert.Equal(2.5f, Interval.Parse("2.5s").Start);
        Assert.Equal(-1f, Interval.Parse("-1").Start);
    }

    [Fact]
    public void OnlyTheFirstTwoFieldsAreRead()
    {
        // Stock's second strtok stops at the second comma and never asks for a
        // third, so "1,2,3" is 1 to 2 and the 3 is dropped.
        Interval three = Interval.Parse("1,2,3");

        Assert.Equal(1f, three.Start);
        Assert.Equal(1f, three.Range);
    }

    [SharedSourceFact("interval.h")]
    public void TheHeaderStillCarriesTheTwoFunctionsThisMirrors()
    {
        string text = File.ReadAllText(AnimListSources.Find("interval.h")!);

        // The .cpp is out of both builds, so if these are not here they are
        // nowhere and three native translation units stop linking. That is a
        // loud failure; what this catches is the quiet direction, somebody
        // moving them out again and leaving this class as the only copy of a
        // rule native code no longer follows.
        Assert.Contains("inline interval_t ReadInterval", text);
        Assert.Contains("inline float RandomInterval", text);

        // The two clauses this class exists to agree with, asserted against the
        // header's own text rather than against a memory of it.
        Assert.Contains("tmp.range = atof( token ) - tmp.start;", text);
        Assert.Contains("if ( interval.range != 0 )", text);
    }

    private static float NeverAsked(float lo, float hi)
    {
        Assert.Fail($"the random stream was consulted for a fixed interval ({lo}, {hi})");
        return 0f;
    }
}
