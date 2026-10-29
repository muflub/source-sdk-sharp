using System.Globalization;

using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// The theme override file (plan_client_ui §5.5, decision 4):
/// <c>resource/ui-theme.yaml</c> in the mod, read on load and on change, so a
/// colour or a size changes without a build. Flat <c>key: value</c> lines, a
/// strict subset of YAML:
/// <code>
/// tone.accent: "#ffb52e"          # #rrggbb or #rrggbbaa
/// type.number.size: 48            # du
/// type.title.weight: 700
/// type.label.upper: false
/// font.file: "TF2 Build" resource/TF2Build.ttf   # a TTF on a GAME path
/// motion.quick: 0.1               # seconds
/// hud.surface-opacity: 0.85
/// scroll.step: 48                # du a wheel notch scrolls
/// </code>
/// An unknown key or a bad value is an error naming its line: a typo in a theme
/// must not silently do nothing.
/// </summary>
public static class UiThemeFile
{
    public static void Apply(UiTheme theme, string text)
    {
        int lineNo = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            string line = raw;
            int hash = IndexOfComment(line);
            if (hash >= 0) line = line[..hash];
            line = line.Trim();
            if (line.Length == 0)
                continue;

            int colon = line.IndexOf(':');
            if (colon < 0)
                throw Error(lineNo, raw, "expected key: value");
            string key = line[..colon].Trim().ToLowerInvariant();
            string value = line[(colon + 1)..].Trim().Trim('"', '\'');

            try
            {
                ApplyOne(theme, key, value, line[(colon + 1)..].Trim());
            }
            catch (FormatException e)
            {
                throw Error(lineNo, raw, e.Message);
            }
        }
    }

    private static int IndexOfComment(string line)
    {
        // A # inside quotes is a colour, not a comment.
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] is '"' or '\'') quoted = !quoted;
            else if (line[i] == '#' && !quoted && (i == 0 || char.IsWhiteSpace(line[i - 1]))) return i;
        }
        return -1;
    }

    private static InvalidDataException Error(int line, string text, string why)
        => new($"ui-theme.yaml line {line}: {why}: {text.Trim()}");

    private static void ApplyOne(UiTheme theme, string setting, string value, string quoted)
    {
        string key = setting;
        var parts = key.Split('.');
        switch (parts[0])
        {
            case "tone" when parts.Length == 2:
                theme.Set(Parse<Tone>(parts[1]), Colour(value));
                return;
            case "type" when parts.Length == 3:
            {
                var role = Parse<Text>(parts[1]);
                var t = theme.Type(role);
                t = parts[2] switch
                {
                    "size" => t with { SizeDu = Number(value) },
                    "weight" => t with { Weight = (int)Number(value) },
                    "family" => t with { Family = value },
                    "upper" => t with { Upper = Bool(value) },
                    "shadow" => t with { Flags = Bool(value) ? t.Flags | UiFontFlags.DropShadow : t.Flags & ~UiFontFlags.DropShadow },
                    _ => throw new FormatException($"unknown type property '{parts[2]}' (size, weight, family, upper, shadow)"),
                };
                theme.Set(role, t);
                return;
            }
            case "font" when parts.Length == 2 && parts[1] == "file":
                // The row's own quotes (a family and a path on one line); the
                // plain value has had its outer quotes stripped.
                theme.AddFontFile(FontFile(quoted));
                return;
            case "motion" when parts.Length == 2:
                theme.Set(Parse<Motion>(parts[1]), Number(value));
                return;
            case "hud" when key == "hud.surface-opacity":
                theme.HudSurfaceOpacity = Number(value);
                return;
            case "scroll" when key == "scroll.step":
                theme.ScrollStepDu = Number(value);
                return;
        }
        throw new FormatException($"unknown key '{key}' (tone.*, type.<role>.*, font.file, motion.*, hud.surface-opacity, scroll.step)");
    }

    /// <summary>
    /// A font file row: an optional quoted family, then the GAME path.
    /// <c>font.file: "TF2 Build" resource/TF2Build.ttf</c>, or a bare path
    /// whose family only the engine knows.
    /// </summary>
    private static UiThemeFontFile FontFile(string value)
    {
        if (value.StartsWith('"'))
        {
            int end = value.IndexOf('"', 1);
            if (end < 0 || value[(end + 1)..].Trim().Length == 0)
                throw new FormatException($"'{value}' is not \"family\" game/path.ttf");
            return new UiThemeFontFile(value[1..end], value[(end + 1)..].Trim());
        }
        if (value.Length == 0)
            throw new FormatException("expected \"family\" game/path.ttf or game/path.ttf");
        return new UiThemeFontFile(null, value);
    }

    private static T Parse<T>(string name) where T : struct, Enum
        => Enum.TryParse<T>(name.Replace("-", ""), ignoreCase: true, out var v)
            ? v
            : throw new FormatException($"no {typeof(T).Name} named '{name}' ({string.Join(", ", Enum.GetNames<T>())})");

    private static float Number(string v)
        => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : throw new FormatException($"'{v}' is not a number");

    private static bool Bool(string v)
        => v switch { "true" or "yes" => true, "false" or "no" => false, _ => throw new FormatException($"'{v}' is not true/false") };

    private static UiColor Colour(string v)
    {
        if (v.Length is not (7 or 9) || v[0] != '#'
            || !uint.TryParse(v.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint x))
            throw new FormatException($"'{v}' is not #rrggbb or #rrggbbaa");
        if (v.Length == 7)
            return new UiColor((byte)(x >> 16), (byte)(x >> 8), (byte)x, 255);
        return new UiColor((byte)(x >> 24), (byte)(x >> 16), (byte)(x >> 8), (byte)x);
    }
}
