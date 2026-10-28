using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The theme's game fonts (the ui-theme.yaml <c>font.file</c> rows): TTF
/// files on the engine's GAME search paths — a loose file or one in a mounted
/// VPK — read through <see cref="IEngineFiles"/>, the same as the language
/// files. The file goes to the vgui surface by its GAME path (the engine's
/// own filesystem reads it, VPKs included) and the family's metrics come from
/// the same bytes, so the backend measures and calibrates the real face
/// instead of the theme's default. A file on no GAME path is a logged
/// warning and the theme's default family stands. The offline preview has no
/// game — no <see cref="IEngineFiles"/> — and keeps the repo's fonts.
/// </summary>
public static class GameFonts
{
    /// <summary>What was loaded: the GAME paths for the surface, and the metrics over them (null when none loaded).</summary>
    public sealed record Loaded(IReadOnlyList<string> Paths, IUiFontMetrics? Metrics);

    public static Loaded Load(IReadOnlyList<UiThemeFontFile> fonts, IUiFontMetrics fallback, IEngineFiles? files, ILog log)
    {
        if (fonts.Count == 0 || files is null)
            return new Loaded([], null);

        var paths = new List<string>();
        var faces = new List<TtfFace>();
        foreach (var font in fonts)
        {
            string named = font.Family is { } family ? $"\"{family}\" ({font.Path})" : font.Path;
            if (files.ReadGameFileBytes(font.Path) is not { } bytes)
            {
                log.Warning($"[SourceSharp/Ui] font {named} is on no GAME path: its family falls to the theme's default\n");
                continue;
            }
            paths.Add(font.Path);
            if (font.Family is { } known)
                faces.Add(new TtfFace(known, 400, bytes));
            log.Msg($"[SourceSharp/Ui] font {named}: {bytes.Length} bytes from a GAME path\n");
        }
        return new Loaded(paths, faces.Count > 0 ? new Faces(fallback, faces) : null);
    }

    /// <summary>The theme's default metrics, with the game faces over their own families.</summary>
    private sealed class Faces(IUiFontMetrics fallback, IReadOnlyList<TtfFace> faces) : IUiFontMetrics
    {
        public UiFontMetrics Metrics(in UiFontSpec font)
        {
            if (Face(font) is not { } face)
                return fallback.Metrics(font);
            float k = font.PxHeight / (float)face.UnitsPerEm;
            float ascent = face.Ascender * k, descent = -face.Descender * k;
            return new UiFontMetrics(ascent, descent, ascent + descent);
        }

        public float Advance(in UiFontSpec font, char c)
            => Face(font) is not { } face ? fallback.Advance(font, c) : face.AdvanceUnits(c) * font.PxHeight / (float)face.UnitsPerEm;

        private TtfFace? Face(in UiFontSpec font)
        {
            TtfFace? best = null;
            int bestDistance = int.MaxValue;
            foreach (var f in faces)
            {
                if (f.Family != font.Family)
                    continue;
                int d = Math.Abs(f.Weight - font.Weight);
                if (d < bestDistance)
                {
                    best = f;
                    bestDistance = d;
                }
            }
            return best;
        }
    }
}
