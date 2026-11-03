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
    public void A_refract_material_is_bound_whole_not_decoded()
    {
        // The scope quadrants are Refract shaders: their pixels are the
        // refracted frame buffer, not a picture to upload. The surface binds
        // the .vmt whole (stock's DrawSetTextureFile, tf_hud_scope.cpp:317-320)
        // and the library passes its handle through, asking the surface once.
        var library = Library();

        Assert.Equal(0x77, library.Material("HUD/scope_sniper_ul"));
        Assert.Equal(0x77, library.Material("HUD/scope_sniper_ul"));
        Assert.Single(_surface.Materials);
        Assert.Equal("HUD/scope_sniper_ul", _surface.Materials[0]);
    }

    [Fact]
    public void A_material_the_surface_refuses_is_zero_said_once()
    {
        var library = Library();

        Assert.Equal(0, library.Material("HUD/scope_missing"));
        Assert.Equal(0, library.Material("HUD/scope_missing"));
        Assert.Single(Warned(_log), w => w.Contains("scope_missing"));
    }

    [Fact]
    public void A_material_name_binds_the_material_not_an_upload()
    {
        // The HUD art draws through the material's own shader (integrator
        // ruling 2026-10-31, stock's DrawSetTextureFile): Find binds the
        // .vmt by name, it does not decode the .vtf to RGBA and upload it.
        _files.Text["materials/hud/test_thing.vmt"] = Vmt;
        _files.Bytes["materials/hud/test_thing.vtf"] = SmallVtf();
        var library = Library();

        int handle = library.Find("hud/test_thing");

        Assert.Equal(0x77, handle);
        Assert.Single(_surface.Materials);
        Assert.Equal("hud/test_thing", _surface.Materials[0]);
        Assert.Empty(_surface.Uploaded);

        // Asked again every frame: still one bind.
        Assert.Equal(handle, library.Find("hud/test_thing"));
        Assert.Single(_surface.Materials);
    }

    [Fact]
    public void About_names_what_loaded()
    {
        // The size comes from the $baseTexture VTF header, not a decode.
        _files.Text["materials/hud/test_thing.vmt"] = Vmt;
        _files.Bytes["materials/hud/test_thing.vtf"] = SmallVtf();
        var library = Library();

        library.Find("hud/test_thing");
        var info = library.About("hud/test_thing");
        Assert.Equal(2, info!.Value.Wide);
        Assert.Equal(2, info.Value.Tall);
        Assert.Equal("Bgra8888", info.Value.Format);
        Assert.Equal(0, _surface.Created);
    }

    [Fact]
    public void A_name_the_surface_bound_is_never_asked_again()
    {
        ServeMaterial("hud/one", "hud/shared");
        ServeMaterial("hud/two", "hud/shared");
        var library = Library();

        _ = library.Find("hud/one");
        _ = library.Find("hud/two");
        Assert.Equal(2, _surface.Materials.Count);
        _ = library.Find("hud/one");
        _ = library.Find("hud/two");
        Assert.Equal(2, _surface.Materials.Count);
    }

    [Fact]
    public void A_material_the_surface_refuses_by_find_is_zero_said_once()
    {
        var library = Library();

        Assert.Equal(0, library.Find("hud/missing"));
        Assert.Equal(0, library.Find("hud/missing"));
        Assert.Single(Warned(_log));
        Assert.Contains("hud/missing", Warned(_log)[0]);
        Assert.Contains("bound nothing", Warned(_log)[0]);
    }

    [Fact]
    public void A_vmt_without_a_base_texture_still_binds_the_size_unknown()
    {
        _files.Text["materials/hud/bare.vmt"] = "\"UnlitGeneric\"\n{\n\"$noclip\" 1\n}\n";
        var library = Library();

        Assert.NotEqual(0, library.Find("hud/bare"));
        var info = library.About("hud/bare");
        Assert.NotNull(info);
        Assert.Equal((0, 0, ""), (info!.Value.Wide, info.Value.Tall, info.Value.Format));
    }

    [Fact]
    public void A_shader_without_a_decodable_base_texture_still_binds()
    {
        // A Refract shader's pixels are the refracted frame buffer — its
        // $refracttinttexture is a tint to multiply by, not a picture to
        // upload (the rejected scope overlay drew it opaque). Find binds the
        // shader whole, as Material always did.
        _files.Text["materials/hud/scope.vmt"] =
            "\"Refract\"\n{\n\"$refracttinttexture\" \"hud/scope_tint\"\n}\n";
        var library = Library();

        Assert.Equal(0x77, library.Find("hud/scope"));
        Assert.Empty(_surface.Uploaded);
    }

    [Fact]
    public void An_unreadable_vtf_leaves_the_size_zero_not_the_art()
    {
        ServeMaterial("hud/bad", "hud/bad");
        _files.Bytes["materials/hud/bad.vtf"] = [0x00, 0x01, 0x02];
        var library = Library();

        Assert.NotEqual(0, library.Find("hud/bad"));
        var info = library.About("hud/bad");
        Assert.NotNull(info);
        Assert.Equal((0, 0, ""), (info!.Value.Wide, info.Value.Tall, info.Value.Format));
    }

    [Fact]
    public void A_client_with_no_engine_file_reads_still_binds()
    {
        var library = new UiTextureLibrary(null, _surface, _log);

        Assert.Equal(0x77, library.Find("hud/anything"));
        var info = library.About("hud/anything");
        Assert.Equal((0, 0), (info!.Value.Wide, info.Value.Tall));
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
        public List<string> Materials { get; } = [];
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

        public void TexturedQuad(int texture, ReadOnlySpan<float> xyuv, uint rgba, bool additive)
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

        public int MaterialTexture(string name)
        {
            Materials.Add(name);
            return name.Contains("missing") ? 0 : 0x77;
        }

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
    public void It_asks_for_the_current_library_each_time_and_holds_none()
    {
        var log = new RecordingLog();
        var files = new UiTextureLibraryTests.Files();
        files.Text["materials/hud/test_thing.vmt"] = "\"UnlitGeneric\"\n{\n\"$basetexture\" \"hud/test_thing\"\n}\n";
        files.Bytes["materials/hud/test_thing.vtf"] = SmallVtf();
        var first = new UiTextureLibrary(new UiTextureLibraryTests.Files(), new UiTextureLibraryTests.CapturingSurface(), log);
        var source = new CurrentTextures(first);
        var command = new UiTextureCommand(source, log);

        // A UI-only reload: the source now answers the new UI's library.
        source.Library = new UiTextureLibrary(files, new UiTextureLibraryTests.CapturingSurface(), log);
        command.Execute("hud/test_thing");

        Assert.Contains("handle 1", log.Text);
    }

    [Fact]
    public void It_says_so_when_no_ui_is_loaded()
    {
        var log = new RecordingLog();
        var command = new UiTextureCommand(new CurrentTextures(null), log);

        command.Execute("hud/test_thing");

        Assert.Contains("no UI is loaded", log.Text);
    }

    [Fact]
    public void It_reports_what_the_library_loaded()
    {
        var files = new UiTextureLibraryTests.Files();
        files.Text["materials/hud/test_thing.vmt"] = "\"UnlitGeneric\"\n{\n\"$basetexture\" \"hud/test_thing\"\n}\n";
        files.Bytes["materials/hud/test_thing.vtf"] = SmallVtf();
        var surface = new UiTextureLibraryTests.CapturingSurface();
        var log = new RecordingLog();
        var command = new UiTextureCommand(new CurrentTextures(new UiTextureLibrary(files, surface, log)), log);

        command.Execute("hud/test_thing");

        string text = log.Text;
        Assert.Contains("handle 119", text);
        Assert.Contains("2x2", text);
        Assert.Contains("Bgra8888", text);
    }

    [Fact]
    public void It_says_no_texture_for_a_missing_material()
    {
        var log = new RecordingLog();
        var command = new UiTextureCommand(new CurrentTextures(new UiTextureLibrary(new UiTextureLibraryTests.Files(), new UiTextureLibraryTests.CapturingSurface(), log)), log);

        command.Execute("hud/missing");

        Assert.Contains("no texture (0)", log.Text);
        Assert.Single(UiTextureLibraryTests.Warned(log));   // the library's one warning, above the report
    }

    [Fact]
    public void It_shows_usage_without_a_name()
    {
        var log = new RecordingLog();
        var command = new UiTextureCommand(new CurrentTextures(new UiTextureLibrary(new UiTextureLibraryTests.Files(), new UiTextureLibraryTests.CapturingSurface(), log)), log);

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

/// <summary>The loader's answer to cs_ui_texture, settable as a UI reload would change it.</summary>
internal sealed class CurrentTextures(IUiTextureLibrary? library) : IUiTextureSource
{
    public IUiTextureLibrary? Library { get; set; } = library;

    public IUiTextureLibrary? Textures => Library;
}
#endif
