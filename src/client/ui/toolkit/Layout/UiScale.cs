using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// Pixels per design unit for a screen (plan_client_ui §5.1, user ruling
/// 2026-10-05): min(H / 1080, W / 1280) × ui_scale. 16:9 and wider (and 4:3)
/// scale by height, exactly as before; skinnier screens (5:4, portrait) scale
/// by width, but against 1280 du, not 1920, so they keep readable text
/// (portrait 1080x1920: 0.84) and come out narrow (design width 1280), where
/// the narrow arrangements take over. The one
/// place the rule lives: the core builds ScreenInfo with it, and so do the
/// previews and the tests.
/// </summary>
public static class UiScale
{
    public const float DesignH = 1080f;

    /// <summary>The narrowest design width scaling keeps before it starts to shrink: 1280 du.</summary>
    public const float MinDesignW = 1280f;

    public static float For(int w, int h, float uiScale = 1f)
        => MathF.Min(h / DesignH, w / MinDesignW) * uiScale;

    public static ScreenInfo Screen(int w, int h, float uiScale = 1f, UiInsets safe = default)
        => new(w, h, For(w, h, uiScale), safe);

    /// <summary>The screen's width in design units: what layout breakpoints are chosen by.</summary>
    public static float DesignWidth(in ScreenInfo s) => s.W / s.Scale;

    /// <summary>The screen's height in design units.</summary>
    public static float DesignHeight(in ScreenInfo s) => s.H / s.Scale;
}
