using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui.Screens;

// The map overlay from fakes: the automap screen's dungeons, the overlay up.
// Down, it draws nothing, so that state has no sample.

public static class MapOverlaySamples
{
    /// <summary>The overlay up over <paramref name="map"/>.</summary>
    public static UiScreen Build(UiSampleServices s, FakeBigMap map)
    {
        s.Add<IDescentAutomap>(map);
        s.Add<IDescentMapOverlay>(new FakeMapOverlay { Shown = true });
        return s.Create<MapOverlay>();
    }

    /// <summary>Three storeys explored, the hero on the middle one at the map's west edge.</summary>
    [UiSample("revealed")]
    private static UiScreen Revealed(UiSampleServices s) => Build(s, FakeBigMap.Revealed());

    /// <summary>One storey entered: three rooms round the hero.</summary>
    [UiSample("partial")]
    private static UiScreen Partial(UiSampleServices s) => Build(s, FakeBigMap.Partial());

    /// <summary>The hero in the middle of the crypt, the map round him on every side.</summary>
    [UiSample("hero in the crypt")]
    private static UiScreen Centre(UiSampleServices s)
    {
        var revealed = FakeBigMap.Revealed();
        return Build(s, new FakeBigMap
        {
            Rooms = revealed.Rooms, Doors = revealed.Doors, Markers = revealed.Markers,
            HeroX = 544, HeroY = 560, HeroHeadingDeg = 30,
        });
    }
}

/// <summary>The map overlay's toggle, as the client's state has it.</summary>
public sealed class FakeMapOverlay : IDescentMapOverlay
{
    public bool Shown { get; init; }
}
