using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Tests.ModUi;

/// <summary>
/// The fixture's one screen: a HUD element over a core data interface, with a
/// sub-panel of its own, so placing it proves the container filled both the
/// interface (from the side's services) and the part (built from this
/// assembly).
/// </summary>
public sealed class ModProbe(ICentrePrintSource text, ModProbeLine line) : UiScreen
{
    public override string Name => "modprobe";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    public override int Order => 20;

    /// <summary>The source the container handed in, for the facts.</summary>
    public ICentrePrintSource Source => text;

    /// <summary>The part the container built, for the facts.</summary>
    public ModProbeLine Line => line;

    public override void Describe(ref UiFrame ui)
    {
        using (ui.Panel(Space.M, Space.XS, Elevation.Raised).At(UiAnchor.Top).Inset(Space.XL))
            ui.Sub("line", line);
    }
}

/// <summary>The probe's one part: the centre print, as data.</summary>
public sealed class ModProbeLine(ICentrePrintSource text) : UiPanel
{
    public override void Describe(ref UiFrame ui) => ui.Data(text.CenterPrint, Text.Body);
}

/// <summary>A centre print for the sample.</summary>
public sealed class FakeProbeText : ICentrePrintSource
{
    public string CenterPrint { get; init; } = "";

    public float CenterPrintAlpha { get; init; } = 1f;
}

internal static class ModProbeSamples
{
    [UiSample("a line")]
    private static UiScreen ALine(UiSampleServices s)
        => s.Add<ICentrePrintSource>(new FakeProbeText { CenterPrint = "mod screen" }).Create<ModProbe>();
}
