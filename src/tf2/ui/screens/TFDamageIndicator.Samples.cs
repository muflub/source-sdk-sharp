using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using System.Numerics;

namespace TF2.Client.Ui;

// The damage indicator from fakes (the ui-screen loop's sample step). The
// preview's clock reads 100, so the fakes set At just behind it.

public static class TFDamageIndicatorSamples
{
    public static UiScreen Build(UiSampleServices s, FakeDamageHistory? hits = null, FakeCombatText? dealt = null)
    {
        s.Add<ITFDamageHistory>(hits ?? new FakeDamageHistory());
        s.Add<ITFCombatText>(dealt ?? new FakeCombatText());
        return s.Create<TFDamageIndicator>();
    }

    // The plain arm: one hit from the left, fresh (its radius mid-travel).
    [UiSample("hit-from-the-left")]
    private static UiScreen Left(UiSampleServices s) => Build(s, new FakeDamageHistory
    {
        Hits = [new(30, -90f, 99.75f, 1.3f)],
    });

    // The spread: three hits around the circle — front, right, behind — the
    // front one fading, plus a heavy hit from the right.
    [UiSample("three-hits-and-a-heavy")]
    private static UiScreen Spread(UiSampleServices s) => Build(s, new FakeDamageHistory
    {
        Hits =
        [
            new(25, 0f, 99.9f, 1.25f),
            new(45, 90f, 99.8f, 1.45f),
            new(15, 180f, 99.7f, 1.15f),
            new(100, 45f, 99.5f, 2f),
        ],
    }, new FakeCombatText
    {
        Numbers = [new(25, false, 99.5f), new(45, true, 99.2f)],
    });

    // The fade: one hit at the fade-out percentage of its life.
    [UiSample("fading-hit")]
    private static UiScreen Fading(UiSampleServices s) => Build(s, new FakeDamageHistory
    {
        Hits = [new(30, -45f, 99f, 1.3f)],
    }, new FakeCombatText
    {
        Numbers = [new(30, false, 98.5f)],    // three quarters of the lifetime: well up the drift
    });
}

/// <summary>The hits as the screen reads them; the record's walk lives in
/// the adapter's facts, so the samples fix one frame.</summary>
public sealed class FakeDamageHistory : ITFDamageHistory
{
    public TFDamageTaken[] Hits { get; set; } = [];

    public int Count => Hits.Length;

    public TFDamageTaken Damage(int i) => Hits[i];
}

/// <summary>The dealt numbers as the screen reads them (the account panel's
/// items, the preview's clock reading 100). The projection is a stand-in
/// ladder of spots across the middle, the numbers' order down it.</summary>
public sealed class FakeCombatText : ITFCombatText
{
    public TFCombatNumber[] Numbers { get; set; } = [];

    public int Count => Numbers.Length;

    public TFCombatNumber Number(int i) => Numbers[i];

    public bool Screen(int i, float lifetimePercent, out Vector2 hud)
    {
        hud = new(560f + i * 40f, 300f + i * 36f);
        return true;
    }
}
