using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// A mod's language files on the engine's GAME paths (stage 0.9): mods.yaml
/// <c>localisation</c> names them (<c>resource/tf_english.txt</c>, which TF2
/// ships inside a VPK, where no disk path reaches), the UI reads their bytes
/// through IEngineFiles (the FilesReadGameFile slot) and parses them as it
/// parses a file on disk. The chain: the mod's disk file, then these, then the
/// core's strings, then the engine.
/// </summary>
public sealed class UiGameLanguageTests : IDisposable
{
    private const string VpkPath = "resource/tf_english.txt";

    private readonly string _dir = Directory.CreateTempSubdirectory("ss-uigamelang-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string LanguageText(string tokens)
        => "\"lang\"\n{\n\"Language\" \"English\"\n\"Tokens\"\n{\n" + tokens + "\n}\n}\n";

    private static byte[] Utf16(string tokens) => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(LanguageText(tokens))];

    [Fact]
    public void Parse_on_bytes_equals_Load_on_the_same_file()
    {
        // The shipped file (UTF-16 LE with its mark), and the same text in the
        // other encodings Load's mark detection accepts.
        byte[] shipped = File.ReadAllBytes(RepoLayout.Repo("game", "mod_test", "resource", "test_english.txt"));
        string text = Encoding.Unicode.GetString(shipped.AsSpan(2));
        var encodings = new (string Name, byte[] Bytes)[]
        {
            ("utf-16le", shipped),
            ("utf-16be", [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(text)]),
            ("utf-8 marked", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]),
            ("utf-8 bare", Encoding.UTF8.GetBytes(text)),
        };
        foreach (var (name, bytes) in encodings)
        {
            string path = Path.Combine(_dir, name + ".txt");
            File.WriteAllBytes(path, bytes);
            var loaded = UiLanguageFile.Load(path);
            var parsed = UiLanguageFile.Parse(bytes);
            Assert.True(loaded.Count > 10, $"{name}: {loaded.Count} token(s) loaded");   // the stimulus reached the parser
            Assert.Equal(loaded.Keys.Order(StringComparer.Ordinal), parsed.Keys.Order(StringComparer.Ordinal));
            foreach (string key in loaded.Keys)
                Assert.True(loaded.Find("#" + key) == parsed.Find("#" + key), $"{name}: #{key} differs");
        }
    }

    [Fact]
    public void A_token_only_in_a_vpk_file_resolves_through_the_chain()
    {
        var files = new PathFiles(new() { [VpkPath] = Utf16("\"TF_Only\" \"From the VPK\"") });
        var engine = Fake().ReturnsEachCall("Hud.Localise", () => "");
        var unlisted = new GameUiLocaliser(ClientUi.Language(_dir, new RecordingLog(), files, []), engine);
        Assert.Null(unlisted.Find("#TF_Only"));                  // control: not listed, not found
        var localiser = new GameUiLocaliser(ClientUi.Language(_dir, new RecordingLog(), files, [VpkPath]), engine);
        Assert.Equal("From the VPK", localiser.Find("#TF_Only"));
        Assert.Contains(VpkPath, files.Asked);
    }

    /// <summary>
    /// The engine's GameUI strings (#GameUI_*: disconnect reasons, options
    /// labels) were loaded by the stock GameUI module alone, at its
    /// Initialize (docs/gameui-census.md §5); with that module never called,
    /// the chain reads <c>resource/gameui_english.txt</c> off the GAME paths
    /// itself, listed or not.
    /// </summary>
    [Fact]
    public void The_engines_gameui_strings_resolve_through_the_chain_unlisted()
    {
        var files = new PathFiles(new() { [ClientUi.GameUiLanguageFile] = Utf16("\"GameUI_Disconnect\" \"Disconnect\"") });
        var lang = ClientUi.Language(_dir, new RecordingLog(), files, []);
        Assert.Equal("Disconnect", lang.Find("#GameUI_Disconnect"));
        Assert.Equal("resource/gameui_english.txt", ClientUi.GameUiLanguageFile);
    }

    /// <summary>A GameUI file that is no language file (a foreign or broken
    /// one) is said and skipped; the rest of the chain still loads. Found by
    /// the TF2 composition fact, whose engine double answers every read alike.</summary>
    [Fact]
    public void A_gameui_file_that_is_no_language_file_is_skipped_and_said()
    {
        var log = new RecordingLog();
        var files = new PathFiles(new() { [ClientUi.GameUiLanguageFile] = Encoding.UTF8.GetBytes("not a language file") });
        var lang = ClientUi.Language(_dir, log, files, []);
        Assert.Contains($"{ClientUi.GameUiLanguageFile} is no language file", log.Text);
        Assert.Equal("OK", lang.Find("#SS_UI_Ok"));
    }

    /// <summary>IEngineFiles answering by path; every path asked is kept.</summary>
    private sealed class PathFiles(Dictionary<string, byte[]> files) : IEngineFiles
    {
        public List<string> Asked { get; } = [];
        public int GameFileSize(string path) => files.TryGetValue(path, out var b) ? b.Length : -1;
        public string? ReadGameFile(string path) => null;
        public byte[]? ReadGameFileBytes(string path)
        {
            Asked.Add(path);
            return files.GetValueOrDefault(path);
        }
        public byte[]? ReadFileOnPath(string path, string pathId) => null;
    }

    [Fact]
    public void The_mods_disk_file_wins_over_its_game_files_and_they_over_the_core()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "resource"));
        File.WriteAllText(Path.Combine(_dir, ClientUi.LanguageFileName(_dir)), LanguageText("\"Shared\" \"From disk\""), Encoding.Unicode);
        var engine = Fake().Returns("Files.ReadGameFileBytes", Utf16("\"Shared\" \"From the VPK\"\n\"SS_UI_Ok\" \"VPK OK\""));
        var lang = ClientUi.Language(_dir, new RecordingLog(), engine, [VpkPath]);
        Assert.Equal("From disk", lang.Find("#Shared"));
        Assert.Equal("VPK OK", lang.Find("#SS_UI_Ok"));
        Assert.Equal("Cancel", lang.Find("#SS_UI_Cancel"));       // the core fills the rest
    }

    [Fact]
    public void A_listed_file_on_no_game_path_is_said_and_the_rest_still_load()
    {
        var log = new RecordingLog();
        var engine = Fake().Returns("Files.ReadGameFileBytes", null);
        var lang = ClientUi.Language(_dir, log, engine, [VpkPath]);
        Assert.Contains($"{VpkPath} (mods.yaml localisation) is on no GAME path", log.Text);
        Assert.Equal("OK", lang.Find("#SS_UI_Ok"));
    }

    /// <summary>
    /// Both readers land on ONE file per shipped mod. The engine asks
    /// <c>resource/&lt;gamedir&gt;_english.txt</c> by the game directory's name
    /// (observed live: mods/tf2 asks
    /// tf2_english.txt, mods/test test_english.txt, mods/tf2_descent
    /// tf2_descent_english.txt). The managed chain takes the same name from
    /// <see cref="ClientUi.LanguageFileName"/>, so the disk file under that
    /// name is the one file both read -- and it exists for every shipped mod.
    /// </summary>
    [Fact]
    public void Each_shipped_mod_holds_one_language_file_under_the_name_the_engine_asks()
    {
        foreach ((string shippedDir, string gameDirName) in new[]
                 {
                     ("mod_test", "test"), ("mod_tf2", "tf2"), ("mod_tf2_descent", "tf2_descent"),
                 })
        {
            // The engine's asked name for this mod's game directory.
            string asked = ClientUi.LanguageFileName(Path.Combine("bin", "debug", "mods", gameDirName));
            Assert.Equal(Path.Combine("resource", gameDirName + "_english.txt"), asked);

            string[] files = Directory.GetFiles(RepoLayout.Repo("game", shippedDir, "resource"), "*_english.txt");
            Assert.Single(files);                                     // one file, no drifting duplicate
            Assert.Equal(Path.GetFileName(asked), Path.GetFileName(files[0]));
        }
    }
}
