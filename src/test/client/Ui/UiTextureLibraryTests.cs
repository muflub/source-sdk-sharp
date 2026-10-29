using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The in-game HUD art library, over a fake engine filesystem and a
/// recording surface. The .vmts and .vtfs are built in code (no game content
/// is committed); the bytes the facts expect come from the same rules the
/// VTF reader's facts use.
/// </summary>
public sealed class UiTextureLibraryTests
{
    private const string Vmt = "\"UnlitGeneric\"\n{\n\"$basetexture\" \"hud/test_thing\"\n}\n";

    private readonly Files _files = new();
    private readonly CapturingSurface _surface = new();
    private readonly RecordingLog _log = new();

    private UiTextureLibrary Library() => new(_files, _surface, _log);

    private static byte[] SmallVtf() => VtfFile.Build(new VtfFile(minor: 3, wide: 2, tall: 2, format: 12, mips: 1,
        image:
        [
            [0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xA0, 0xB0, 0xC0, 0xD0, 0xE0, 0xF0, 0x00],
        ], resources: true));

    private void ServeMaterial(string name, string baseTexture) => _files.Text[$"materials/{name}.vmt"] =
        "\"UnlitGeneric\"\n{\n\"$basetexture\" \"" + baseTexture + "\"\n}\n";

    [Fact]
    public void A_material_loads_its_texture_once()
    {
        _files.Text["materials/hud/test_thing.vmt"] = Vmt;
        _files.Bytes["materials/hud/test_thing.vtf"] = SmallVtf();
        var library = Library();

        int handle = library.Find("hud/test_thing");
        Assert.NotEqual(0, handle);
        Assert.Equal(1, _surface.Created);
        Assert.Single(_surface.Uploaded);
        Assert.Equal(handle, _surface.Uploaded[0].Handle);
        Assert.Equal((2, 2), (_surface.Uploaded[0].Wide, _surface.Uploaded[0].Tall));
        // BGRA8888 swizzles to RGBA: the first texel.
        Assert.Equal([0x30, 0x20, 0x10, 0x40], _surface.Uploaded[0].Rgba[0..4]);

        // The screen asks every frame: the answers are cached, the surface sees one texture.
        Assert.Equal(handle, library.Find("hud/test_thing"));
        Assert.Equal(handle, library.Find("hud/test_thing"));
        Assert.Equal(1, _surface.Created);
        Assert.Single(_surface.Uploaded);
    }

    [Fact]
    public void About_names_what_loaded()
    {
        _files.Text["materials/hud/test_thing.vmt"] = Vmt;
        _files.Bytes["materials/hud/test_thing.vtf"] = SmallVtf();
        var library = Library();

        library.Find("hud/test_thing");
        var info = library.About("hud/test_thing");
        Assert.Equal(2, info!.Value.Wide);
        Assert.Equal(2, info.Value.Tall);
        Assert.Equal("Bgra8888", info.Value.Format);
        Assert.Null(library.About("hud/other"));
    }

    [Fact]
    public void Two_materials_on_one_image_upload_it_once()
    {
        ServeMaterial("hud/one", "hud/shared");
        ServeMaterial("hud/two", "hud/shared");
        _files.Bytes["materials/hud/shared.vtf"] = SmallVtf();
        var library = Library();

        int first = library.Find("hud/one");
        int second = library.Find("hud/two");
        Assert.Equal(first, second);
        Assert.NotEqual(0, first);
        Assert.Equal(1, _surface.Created);
    }

    [Fact]
    public void A_missing_material_is_zero_said_once()
    {
        var library = Library();

        Assert.Equal(0, library.Find("hud/absent"));
        Assert.Equal(0, library.Find("hud/absent"));
        Assert.Single(Warned(_log));
        Assert.Contains("hud/absent", Warned(_log)[0]);
    }

    [Fact]
    public void A_material_without_a_base_texture_is_zero_said_once()
    {
        _files.Text["materials/hud/bare.vmt"] = "\"UnlitGeneric\"\n{\n\"$noclip\" 1\n}\n";
        var library = Library();

        Assert.Equal(0, library.Find("hud/bare"));
        Assert.Single(Warned(_log));
        Assert.Contains("$baseTexture", Warned(_log)[0]);
    }

    [Fact]
    public void A_refused_vtf_is_zero_said_once_with_the_readers_reason()
    {
        ServeMaterial("hud/bad", "hud/bad");
        _files.Bytes["materials/hud/bad.vtf"] = [0x00, 0x01, 0x02];
        var library = Library();

        Assert.Equal(0, library.Find("hud/bad"));
        Assert.Single(Warned(_log));
        Assert.Contains("bad.vtf", Warned(_log)[0]);
        Assert.Contains("short", Warned(_log)[0]);
    }

    [Fact]
    public void A_client_with_no_engine_file_reads_is_zero_said_once()
    {
        var library = new UiTextureLibrary(null, _surface, _log);

        Assert.Equal(0, library.Find("hud/anything"));
        Assert.Single(Warned(_log));
    }

    internal static System.Collections.Generic.IReadOnlyList<string> Warned(RecordingLog log)
        => [.. log.Lines.Where(l => l.Warning).Select(l => l.Text)];


    internal sealed class Files : IEngineFiles
    {
        public readonly Dictionary<string, byte[]> Bytes = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Text files, what <see cref="ReadGameFile"/> answers.</summary>
        public readonly Dictionary<string, string> Text = new(StringComparer.OrdinalIgnoreCase);

        public int GameFileSize(string path) => Bytes.TryGetValue(path, out var b) ? b.Length : -1;

        public string? ReadGameFile(string path) => Text.TryGetValue(path, out var t) ? t : null;

        public byte[]? ReadGameFileBytes(string path) => Bytes.TryGetValue(path, out var b) ? b : null;

        public byte[]? ReadFileOnPath(string path, string pathId)
            => string.Equals(pathId, "GAME", StringComparison.Ordinal) ? ReadGameFileBytes(path) : null;
    }

    internal sealed class CapturingSurface : IUiSurface
    {
        public int Created;
        public List<(int Handle, int Wide, int Tall, byte[] Rgba)> Uploaded { get; } = [];

        public void FilledRect(int x0, int y0, int x1, int y1, uint rgba)
        {
        }

        public void FilledRectFade(int x0, int y0, int x1, int y1, uint rgba, int alpha0, int alpha1, bool horizontal)
        {
        }

        public void OutlinedRect(int x0, int y0, int x1, int y1, uint rgba)
        {
        }

        public void Line(int x0, int y0, int x1, int y1, uint rgba)
        {
        }

        public void TexturedRect(int texture, int x0, int y0, int x1, int y1, float s0, float t0, float s1, float t1, uint rgba)
        {
        }

        public void Polygon(int texture, ReadOnlySpan<float> xyuv, uint rgba)
        {
        }

        public void Text(int font, int x, int y, ReadOnlySpan<char> text, uint rgba)
        {
        }

        public void MeasureText(int font, ReadOnlySpan<char> text, out int width, out int height) => (width, height) = (0, 0);

        public void SetClip(bool enabled, int x0, int y0, int x1, int y1)
        {
        }

        public int TextureCreate() => ++Created;

        public void TextureUpload(int texture, ReadOnlySpan<byte> rgba, int wide, int tall)
            => Uploaded.Add((texture, wide, tall, rgba.ToArray()));

        public bool FontAddFile(string path) => true;

        public int FontCreate(string family, int tall, int weight, int vguiFlags) => 1;

        public void FontMetrics(int font, out int tall, out int ascent) => (tall, ascent) = (16, 12);

        public int FontAdvances(int font, int first, Span<int> advances)
        {
            advances.Fill(8);
            return advances.Length;
        }

        public void PlaySound(string path)
        {
        }

        public void SetMenuInput(bool mouse, bool keyboard)
        {
        }

        public void SetCursor(int shape)
        {
        }
    }
}

/// <summary>
/// The preview's offline library, over a temp content directory laid out like
/// a game dir (materials/...). No directory or a missing name answers 0.
/// </summary>
public sealed class PreviewUiTextureLibraryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ss-previewtex-").FullName;
    private readonly PreviewTextures _store = new();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_content_directory_serves_its_art()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "materials", "hud"));
        File.WriteAllText(Path.Combine(_dir, "materials", "hud", "thing.vmt"),
                          "\"UnlitGeneric\"\n{\n\"$basetexture\" \"hud/thing\"\n}\n");
        File.WriteAllBytes(Path.Combine(_dir, "materials", "hud", "thing.vtf"), SmallVtf());
        var library = new PreviewUiTextureLibrary(ContentFiles.FromDirectory(_dir), _store);

        int handle = library.Find("hud/thing");
        Assert.NotEqual(0, handle);
        var art = _store.Find(handle);
        Assert.NotNull(art);
        Assert.Equal((2, 2), (art!.Value.Wide, art.Value.Tall));
        Assert.Equal("Bgra8888", library.About("hud/thing")!.Value.Format);

        // No directory configured, or a name it cannot load: 0, the panel's fallback.
        Assert.Equal(0, new PreviewUiTextureLibrary(null, _store).Find("hud/thing"));
        Assert.Equal(0, library.Find("hud/absent"));
    }

    [Fact]
    public void A_runs_samples_share_one_store_and_library()
    {
        // Made lazily, two parallel samples could each make a store; a
        // sample's backend then looked its art up in the other one's.
        var run = new PreviewRun(new PreviewOptions(RepoLayout.RepoRoot, _dir, false, true, null, Content: _dir));
        var stores = new System.Collections.Concurrent.ConcurrentBag<object?>();
        var libraries = new System.Collections.Concurrent.ConcurrentBag<object?>();

        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
        {
            stores.Add(run.HudStore());
            libraries.Add(run.HudLibrary());
        });

        Assert.NotNull(run.HudStore());
        Assert.Single(stores.Distinct());
        Assert.Single(libraries.Distinct());
    }

    [Fact]
    public void One_library_answers_the_previews_parallel_samples()
    {
        // The preview draws its samples in parallel over one library and one
        // store; the first screen that asked for art from several threads
        // corrupted their dictionaries (an InvalidOperationException inside
        // a sample's describe, surfacing as an unclosed container).
        Directory.CreateDirectory(Path.Combine(_dir, "materials", "hud"));
        const int materials = 64;
        for (int m = 0; m < materials; m++)
        {
            File.WriteAllText(Path.Combine(_dir, "materials", "hud", $"art{m}.vmt"),
                              $"\"UnlitGeneric\"\n{{\n\"$basetexture\" \"hud/art{m}\"\n}}\n");
            File.WriteAllBytes(Path.Combine(_dir, "materials", "hud", $"art{m}.vtf"), SmallVtf());
        }
        var library = new PreviewUiTextureLibrary(ContentFiles.FromDirectory(_dir), _store);
        var seen = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentBag<int>>();

        Parallel.For(0, 20_000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, k =>
        {
            string name = $"hud/art{k % materials}";
            int handle = library.Find(name);
            seen.GetOrAdd(name, _ => []).Add(handle);
            Assert.NotNull(_store.Find(handle));
        });

        Assert.Equal(materials, seen.Count);
        Assert.All(seen, kv => Assert.Single(kv.Value.Distinct()));
        Assert.Equal(materials, seen.Values.Select(v => v.First()).Distinct().Count());
    }

    private static byte[] SmallVtf() => VtfFile.Build(new VtfFile(minor: 3, wide: 2, tall: 2, format: 12, mips: 1,
        image:
        [
            [0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xA0, 0xB0, 0xC0, 0xD0, 0xE0, 0xF0, 0x00],
        ], resources: true));
}

#if DEBUG
// cs_ui_texture is a debug-build command, like every diagnostic command.
/// <summary>The cs_ui_texture report, over the same fakes.</summary>
public sealed class UiTextureCommandTests
{
    [Fact]
    public void It_reports_what_the_library_loaded()
    {
        var files = new UiTextureLibraryTests.Files();
        files.Text["materials/hud/test_thing.vmt"] = "\"UnlitGeneric\"\n{\n\"$basetexture\" \"hud/test_thing\"\n}\n";
        files.Bytes["materials/hud/test_thing.vtf"] = SmallVtf();
        var surface = new UiTextureLibraryTests.CapturingSurface();
        var log = new RecordingLog();
        var command = new UiTextureCommand(new UiTextureLibrary(files, surface, log), log);

        command.Execute("hud/test_thing");

        string text = log.Text;
        Assert.Contains("handle 1", text);
        Assert.Contains("2x2", text);
        Assert.Contains("Bgra8888", text);
    }

    [Fact]
    public void It_says_no_texture_for_a_missing_material()
    {
        var log = new RecordingLog();
        var command = new UiTextureCommand(new UiTextureLibrary(new UiTextureLibraryTests.Files(), new UiTextureLibraryTests.CapturingSurface(), log), log);

        command.Execute("hud/absent");

        Assert.Contains("no texture (0)", log.Text);
        Assert.Single(UiTextureLibraryTests.Warned(log));   // the library's one warning, above the report
    }

    [Fact]
    public void It_shows_usage_without_a_name()
    {
        var log = new RecordingLog();
        var command = new UiTextureCommand(new UiTextureLibrary(new UiTextureLibraryTests.Files(), new UiTextureLibraryTests.CapturingSurface(), log), log);

        command.Execute("");

        Assert.Contains("usage", log.Text);
        Assert.Empty(UiTextureLibraryTests.Warned(log));
    }

    private static byte[] SmallVtf() => VtfFile.Build(new VtfFile(minor: 3, wide: 2, tall: 2, format: 12, mips: 1,
        image:
        [
            [0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xA0, 0xB0, 0xC0, 0xD0, 0xE0, 0xF0, 0x00],
        ], resources: true));
}
#endif
