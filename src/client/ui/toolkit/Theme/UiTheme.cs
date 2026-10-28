using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>How a type role is drawn: family, size in du, weight, flags, case.</summary>
public readonly record struct UiTypeStyle(string Family, float SizeDu, int Weight, UiFontFlags Flags, bool Upper);

/// <summary>A drop shadow under a raised surface, in du and alpha.</summary>
public readonly record struct UiShadow(float OffsetDu, float SpreadDu, byte Alpha);

/// <summary>
/// A TrueType file a theme's families come from, named by GAME path (a loose
/// file or one in a mounted VPK). <see cref="Family"/> is the family the file
/// is — the theme's roles name it; null for a file whose family only the
/// engine knows.
/// </summary>
public sealed record UiThemeFontFile(string? Family, string Path);

/// <summary>
/// What the tokens mean (plan_client_ui §5.5, §5.8.1): one instance per layer,
/// from DI, so a mod swaps the theme and not the screens. Mutable only through
/// <see cref="Set(Tone, UiColor)"/> and friends, which the theme file override
/// uses; screens read it.
/// </summary>
public sealed class UiTheme
{
    private readonly UiColor[] _tones = new UiColor[Enum.GetValues<Tone>().Length];
    private readonly UiTypeStyle[] _type = new UiTypeStyle[Enum.GetValues<Text>().Length];
    private readonly float[] _motion = new float[Enum.GetValues<Motion>().Length];
    private readonly UiShadow[] _shadows = new UiShadow[Enum.GetValues<Elevation>().Length];

    public UiTheme(string name) => Name = name;

    /// <summary>The theme's name, shown in the gallery.</summary>
    public string Name { get; }

    /// <summary>Opacity of HUD surfaces over the world (0..1).</summary>
    public float HudSurfaceOpacity { get; set; } = 1f;

    /// <summary>
    /// The narrow breakpoint (plan_client_ui §5.1): below this design width
    /// (W / Scale, du) a screen uses its narrow arrangement. 1440 du: the HUD's
    /// bottom line (vitals, hints, ammo) and the scoreboard's two teams side by
    /// side both need about 1300 du with margins.
    /// </summary>
    public float NarrowBelowDu { get; set; } = 1440f;

    /// <summary>How far one wheel notch scrolls, du.</summary>
    public float ScrollStepDu { get; set; } = 48f;

    private readonly List<UiThemeFontFile> _fontFiles = [];

    /// <summary>The game font files this theme uses, in file order.</summary>
    public IReadOnlyList<UiThemeFontFile> FontFiles => _fontFiles;

    /// <summary>Names a game font file; the theme file's <c>font.file</c> rows.</summary>
    public UiTheme AddFontFile(UiThemeFontFile file)
    {
        _fontFiles.Add(file);
        return this;
    }

    /// <summary>The minimum click target, du (the lint's rule).</summary>
    public float MinTargetDu { get; set; } = 32f;

    public UiColor Color(Tone tone) => _tones[(int)tone];

    public UiTypeStyle Type(Text role) => _type[(int)role];

    /// <summary>Seconds to ease most of the way to a target.</summary>
    public float Duration(Motion motion) => _motion[(int)motion];

    public UiShadow Shadow(Elevation elevation) => _shadows[(int)elevation];

    public UiTheme Set(Tone tone, UiColor color)
    {
        _tones[(int)tone] = color;
        return this;
    }

    public UiTheme Set(Text role, UiTypeStyle style)
    {
        _type[(int)role] = style;
        return this;
    }

    public UiTheme Set(Motion motion, float seconds)
    {
        _motion[(int)motion] = seconds;
        return this;
    }

    public UiTheme Set(Elevation elevation, UiShadow shadow)
    {
        _shadows[(int)elevation] = shadow;
        return this;
    }

    /// <summary>A paint's colour, opacity applied.</summary>
    public UiColor Resolve(in UiPaint paint)
    {
        var c = paint.IsRaw ? paint.RawColor : Color(paint.Tone);
        if (paint.Opacity >= 1f)
            return c;
        return c with { A = (byte)MathF.Round(c.A * Math.Clamp(paint.Opacity, 0f, 1f)) };
    }
}
