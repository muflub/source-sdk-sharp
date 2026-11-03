using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The crosshair from fakes (the ui-screen loop's sample step). The hidden
// state — a script saying DrawCrosshair 0, or empty hands — draws nothing
// and so has no sample (the samples' runner refuses an empty frame); the
// adapter's facts hold the gate. The custom-file material arm has no
// sample either: the preview's art library binds no material (its Material
// answers 0), so a sample of it could only draw the same fallback the
// drawn sample already shows — TFCrosshairScreenTests holds that arm.

public static class TFCrosshairSamples
{
    public static UiScreen Build(UiSampleServices s, FakeCrosshair? source = null)
    {
        s.Add<ITFCrosshair>(source ?? new FakeCrosshair());
        return s.Create<TFCrosshair>();
    }

    // The plain arm: a weapon whose script draws, at the base scale.
    [UiSample("drawn")]
    private static UiScreen Drawn(UiSampleServices s) => Build(s);
}

/// <summary>The reads as the screen sees them, at the convars' stock
/// defaults (colour 200, the 32-pixel scale, the empty custom file).</summary>
public sealed class FakeCrosshair : ITFCrosshair
{
    public bool Draw { get; set; } = true;

    public float Scale { get; set; } = 1f;

    public string Material { get; set; } = "";

    /// <summary>The script crosshair, the base arm's art — the shipped
    /// heavy's <c>sprites/crosshairs</c> atlas rect at (64,64) 64×64
    /// (<c>tf_weapon_minigun.txt</c>'s TextureData entry). Empty it means
    /// the script names none: the arm draws nothing.</summary>
    public string ScriptFile { get; set; } = "sprites/crosshairs";

    public int ScriptX { get; set; } = 64;

    public int ScriptY { get; set; } = 64;

    public int ScriptWide { get; set; } = 64;

    public int ScriptTall { get; set; } = 64;

    public int Red { get; set; } = 200;

    public int Green { get; set; } = 200;

    public int Blue { get; set; } = 200;
}
