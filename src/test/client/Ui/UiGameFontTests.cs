using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The theme's game fonts: a mod names TTF files by GAME path (a loose file or
/// one in a mounted VPK) and the family each is, the host loads them through
/// the engine's filesystem, and a miss is a logged warning with the theme's
/// default family standing.
/// </summary>
public class UiGameFontTests
{
    private const string Plex = "fonts/IBMPlexSans-Regular.ttf";

    private static byte[] TtfBytes()
    {
        using var stream = typeof(UiFontLibrary).Assembly.GetManifestResourceStream(Plex)
            ?? throw new InvalidOperationException($"{Plex} is not embedded in the toolkit");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private sealed class Files : IEngineFiles
    {
        public readonly Dictionary<string, byte[]> Bytes = new(StringComparer.Ordinal);

        public int GameFileSize(string path) => Bytes.TryGetValue(path, out var b) ? b.Length : -1;
        public string? ReadGameFile(string path) => null;
        public byte[]? ReadGameFileBytes(string path) => Bytes.TryGetValue(path, out var b) ? b : null;
        public byte[]? ReadFileOnPath(string path, string pathId) => ReadGameFileBytes(path);
    }

    [Fact]
    public void A_theme_file_names_a_game_font_by_family_and_path()
    {
        var t = SourceSharpTheme.Create();
        UiThemeFile.Apply(t, """
            font.file: "TF2 Build" resource/TF2Build.ttf
            font.file: resource/tf2.ttf
            """);
        var f = Assert.Single(t.FontFiles, x => x.Family is not null);
        Assert.Equal("TF2 Build", f.Family);
        Assert.Equal("resource/TF2Build.ttf", f.Path);
        Assert.Null(Assert.Single(t.FontFiles, x => x.Family is null).Family);
    }

    [Fact]
    public void A_font_file_row_without_a_path_is_an_error_naming_the_line()
    {
        var e = Assert.Throws<InvalidDataException>(() =>
            UiThemeFile.Apply(SourceSharpTheme.Create(), "\nfont.file: \"TF2 Build\""));
        Assert.Contains("line 2", e.Message);
    }

    [Fact]
    public void GameFonts_load_a_named_game_font_from_a_game_path()
    {
        var files = new Files { Bytes = { ["resource/TF2Build.ttf"] = TtfBytes() } };
        var log = new RecordingLog();
        var baseMetrics = new UiFontLibrary();

        var theme = SourceSharpTheme.Create().AddFontFile(new UiThemeFontFile("TF2 Build", "resource/TF2Build.ttf"));
        var loaded = GameFonts.Load(theme.FontFiles, baseMetrics, files, log);

        Assert.Equal(["resource/TF2Build.ttf"], loaded.Paths);
        Assert.Contains("TF2Build.ttf", log.Text);
        Assert.DoesNotContain(log.Lines, l => l.Warning);

        // The loaded face measures the family the theme named, the base's own
        // families still measure as they did.
        var spec = new UiFontSpec("TF2 Build", 20, 500, UiFontFlags.Antialias);
        var face = new TtfFace("TF2 Build", 400, TtfBytes());
        float k = 20f / face.UnitsPerEm;
        Assert.Equal(face.Ascender * k - face.Descender * k, loaded.Metrics!.Metrics(spec).LineHeight, 0.5f);
        Assert.Equal(face.AdvanceUnits('A') * k, loaded.Metrics!.Advance(spec, 'A'), 0.01f);
        var plex = new UiFontSpec(UiFontLibrary.Sans, 20, 400, UiFontFlags.Antialias);
        Assert.Equal(baseMetrics.Metrics(plex), loaded.Metrics!.Metrics(plex));
    }

    [Fact]
    public void A_font_file_on_no_game_path_is_a_logged_miss()
    {
        var log = new RecordingLog();

        var theme = SourceSharpTheme.Create().AddFontFile(new UiThemeFontFile("TF2 Build", "resource/TF2Build.ttf"));
        var loaded = GameFonts.Load(theme.FontFiles, new UiFontLibrary(), new Files(), log);

        Assert.Empty(loaded.Paths);
        Assert.Null(loaded.Metrics);
        Assert.Contains(log.Lines, l => l.Warning && l.Text.Contains("TF2Build.ttf"));
    }

    [Fact]
    public void GameFonts_without_an_engine_load_nothing()
    {
        var log = new RecordingLog();

        var theme = SourceSharpTheme.Create().AddFontFile(new UiThemeFontFile("TF2 Build", "resource/TF2Build.ttf"));
        var loaded = GameFonts.Load(theme.FontFiles, new UiFontLibrary(), null, log);

        Assert.Empty(loaded.Paths);
        Assert.Null(loaded.Metrics);
        Assert.Empty(log.Lines);
    }
}
