using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

public static class DebugOverlayTextSamples
{
    private sealed record Overlay(DebugOverlayLine[] Lines) : IDebugOverlaySource
    {
        public int Count => Lines.Length;

        public DebugOverlayLine Line(int i) => Lines[i];
    }

    [UiSample("entity text")]
    private static UiScreen Entities(UiSampleServices s) => s.Add<IDebugOverlaySource>(new Overlay(
    [
        new(300, 200, new UiColor(255, 255, 255, 255), "(0) prop_physics_multiplayer"),
        new(300, 213, new UiColor(255, 255, 255, 255), "health 100"),
        new(420, 330, new UiColor(255, 160, 40, 255), "(1) func_door: closed"),
    ])).Create<DebugOverlayText>();
}
