using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The scope from fakes (the ui-screen loop's sample step). The hidden state —
// nobody zoomed — draws nothing and so has no sample (the samples' runner
// refuses an empty frame); the adapter's facts hold the gate. The quadrant
// textures are the game's materials, absent here: every sample shows the
// fallback silhouette, the real pixels only in game.

public static class TFScopeSamples
{
    public static UiScreen Build(UiSampleServices s, FakeScope? source = null)
    {
        s.Add<ITFScope>(source ?? new FakeScope());
        return s.Create<TFScope>();
    }

    // Zoomed with the hands off the rifle: the overlay alone.
    [UiSample("zoomed")]
    private static UiScreen Zoomed(UiSampleServices s) => Build(s);

    // Zoomed on the rifle, the charge partway up.
    [UiSample("charging")]
    private static UiScreen Charging(UiSampleServices s) =>
        Build(s, new FakeScope { MeterPresent = true, Charge = 0.62f });
}

/// <summary>The reads as the screen sees them.</summary>
public sealed class FakeScope : ITFScope
{
    public bool Zoomed { get; set; } = true;

    public bool MeterPresent { get; set; }

    public float Charge { get; set; }
}
