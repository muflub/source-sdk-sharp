using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui.Screens;

// The crate stall from fakes (the ui-screen loop's sample step): the states a
// crate is ever in. Item and base names are ui.Data (game copy, case kept);
// every #Descent_* token lives in game/mod_tf2_descent/resource/descent_english.txt.

public static class CrateSamples
{
    /// <summary>The stall from fakes; windows placed from <paramref name="store"/> (empty: the default size).</summary>
    public static UiScreen Build(UiSampleServices s, FakeCrate crate, MemoryWindowStore? store = null,
                                 FakeCrateActions? actions = null)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(store ?? new MemoryWindowStore());
        s.Add<IDescentCrate>(crate);
        if (actions is not null)
            s.Add<IDescentCrateActions>(actions);
        return s.Create<Crate>();
    }

    /// <summary>The five published rungs of the plan's crate: 50 / 30 / 15 / 5 / 0.01 percent.</summary>
    public static FakeCrate Published() => new()
    {
        BaseName = "Force-A-Nature",
        PriceAustralium = 12,
        Affordable = true,
        Rarities = [DescentRarity.Stock, DescentRarity.Vintage, DescentRarity.Strange,
                    DescentRarity.Unusual, DescentRarity.Australium],
        Weights = [50_000, 30_000, 15_000, 4_990, 10],
    };

    [UiSample("odds shown, affordable")]
    private static UiScreen Preview(UiSampleServices s) => Build(s, Published(), null, new FakeCrateActions());

    [UiSample("not enough Australium")]
    private static UiScreen ShortFunds(UiSampleServices s)
        => Build(s, Published() with { PriceAustralium = 40, Affordable = false });

    [UiSample("just opened, a Strange result")]
    private static UiScreen JustOpened(UiSampleServices s)
        => Build(s, Published() with
        {
            Opening = true,
            OpenedItem = new DescentItemView("Shotgun", DescentRarity.Strange, 27, true, 4, 5, 3, false, false, false, false),
        });

    [UiSample("resized to its minimum")]
    private static UiScreen Minimum(UiSampleServices s)
        => Build(s, Published(), new MemoryWindowStore().With("crate", new UiWindowPlacement(200, 120, 10, 10)));
}

/// <summary>A crate the samples choose: the published odds table and whatever the roll landed.</summary>
public sealed record FakeCrate : IDescentCrate
{
    public string BaseName { get; init; } = "Force-A-Nature";
    public long PriceAustralium { get; init; }
    public bool Affordable { get; init; }
    public DescentRarity[] Rarities { get; init; } = [];
    public long[] Weights { get; init; } = [];
    public bool Opening { get; init; }
    public DescentItemView? OpenedItem { get; init; }

    public int OddsRowCount => Rarities.Length;

    public DescentRarity OddsRarity(int i) => Rarities[i];

    public long OddsWeight(int i) => Weights[i];
}

/// <summary>Records that <c>Open</c> fired (the preview cannot click; the wiring test drives it).</summary>
public sealed class FakeCrateActions : IDescentCrateActions
{
    public int Opened { get; private set; }

    public void Open() => Opened++;
}
