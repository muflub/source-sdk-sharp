using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>A radial menu with fixed choices, for the samples and the facts: what was chosen is kept.</summary>
public sealed class FakeRadialMenu(string title, params UiRadialItem[] items) : IRadialMenuSource, IRadialMenuActions
{
    public List<string> Done { get; } = [];

    public bool Showing { get; set; } = true;

    public string Title => title;

    public int ItemCount => items.Length;

    public UiRadialItem Item(int i) => items[i];

    public void Choose(int i) => Done.Add($"choose {i}");

    public int Hovered { get; private set; } = -2;

    public void Hover(int i) => Hovered = i;

    /// <summary>The button that closes it, for a fact.</summary>
    public int Claims { get; set; } = -1;

    public bool Pressed(int buttonCode)
    {
        if (buttonCode != Claims)
            return false;
        Done.Add("pressed");
        return true;
    }

    public void Back() => Done.Add("back");
}

internal static class RadialMenuSamples
{
    private static UiScreen Build(UiSampleServices s, FakeRadialMenu menu)
    {
        s.Add<IRadialMenuSource>(menu);
        s.Add<IRadialMenuActions>(menu);
        return s.Create<RadialMenu>();
    }

    [UiSample("voice commands")]
    private static UiScreen Voice(UiSampleServices s) => Build(s, new FakeRadialMenu("Voice Menu 1",
        new("MEDIC!", null, true), new("Thanks!", null, true), new("Go! Go! Go!", null, true), new("Move Up!", null, true),
        new("Go Left", null, true), new("Go Right", null, true), new("Yes", null, true), new("No", null, true),
        new("Pass to me!", null, true)));

    [UiSample("call a vote, on cooldown")]
    private static UiScreen Vote(UiSampleServices s) => Build(s, new FakeRadialMenu("Call a vote",
        new("Kick a player", "Wait 2:14", false), new("Change the map", null, true)));

    [UiSample("few choices")]
    private static UiScreen Few(UiSampleServices s) => Build(s, new FakeRadialMenu("Admin",
        new("Map", null, true), new("Game mode", null, true), new("Bots", null, true)));
}
