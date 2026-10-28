using System.Buffers.Binary;
using System.Text;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;


namespace SourceSharp.Tests.Ui;

/// <summary>
/// The preview drawing as a mod does in game (--mod): the VPKs and
/// directories its gameinfo.txt mounts, its theme, and its fonts read from
/// those files. Every archive here is built in code.
/// </summary>
public sealed class UiPreviewModTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ss-previewmod-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed record VpkFile(string Folder, string Name, string Extension, byte[] Data, int Archive, int Preload = 0);

    /// <summary>
    /// A VPK directory file (and its numbered parts) holding <paramref name="files"/>:
    /// archive 0x7fff is the directory file's own data, any other a <c>_NNN.vpk</c> part.
    /// The first <c>Preload</c> bytes of a file sit in the directory itself.
    /// </summary>
    private string WriteVpk(string baseName, int version, params VpkFile[] files)
    {
        var tree = new MemoryStream();
        var embedded = new MemoryStream();
        var parts = new Dictionary<int, MemoryStream>();
        void Str(string s) { tree.Write(Encoding.Latin1.GetBytes(s)); tree.WriteByte(0); }
        void U16(int v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)v); tree.Write(b); }
        void U32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); tree.Write(b); }

        foreach (var byExt in files.GroupBy(f => f.Extension))
        {
            Str(byExt.Key);
            foreach (var byFolder in byExt.GroupBy(f => f.Folder))
            {
                Str(byFolder.Key);
                foreach (var f in byFolder)
                {
                    Str(f.Name);
                    U32(0);
                    U16(f.Preload);
                    byte[] rest = f.Data[f.Preload..];
                    var target = f.Archive == VpkDirectory.EmbeddedArchiveIndex
                        ? embedded
                        : parts.TryGetValue(f.Archive, out var p) ? p : parts[f.Archive] = new MemoryStream();
                    U16(f.Archive);
                    U32((uint)target.Length);
                    U32((uint)rest.Length);
                    target.Write(rest);
                    U16(0xffff);
                    tree.Write(f.Data, 0, f.Preload);
                }
                Str("");
            }
            Str("");
        }
        Str("");

        var header = new byte[version == 2 ? 28 : 12];
        BinaryPrimitives.WriteUInt32LittleEndian(header, VpkDirectory.Marker);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), version);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), (int)tree.Length);

        string dirPath = Path.Combine(_dir, baseName + "_dir.vpk");
        File.WriteAllBytes(dirPath, [.. header, .. tree.ToArray(), .. embedded.ToArray()]);
        foreach (var (index, stream) in parts)
            File.WriteAllBytes(Path.Combine(_dir, $"{baseName}_{index:D3}.vpk"), stream.ToArray());
        return dirPath;
    }

    private static byte[] Bytes(string s) => Encoding.ASCII.GetBytes(s);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_vpk_serves_its_files_from_the_directory_file_and_its_parts(int version)
    {
        string path = WriteVpk("pak", version,
            new VpkFile("materials/hud", "thing", "vmt", Bytes("in the directory file"), VpkDirectory.EmbeddedArchiveIndex),
            new VpkFile("resource", "font", "ttf", Bytes("in part zero, preloaded in part"), 0, Preload: 7),
            new VpkFile(" ", "readme", "txt", Bytes("at the root"), 1),
            new VpkFile("scripts", "noext", " ", Bytes("no extension"), 0));

        var vpk = VpkDirectory.Open(path);

        Assert.Equal(4, vpk.Count);
        Assert.Equal("in the directory file", Encoding.ASCII.GetString(vpk.Read("materials/hud/thing.vmt")!));
        Assert.Equal("in part zero, preloaded in part", Encoding.ASCII.GetString(vpk.Read("resource/font.ttf")!));
        Assert.Equal("at the root", Encoding.ASCII.GetString(vpk.Read("readme.txt")!));
        Assert.Equal("no extension", Encoding.ASCII.GetString(vpk.Read("scripts/noext")!));
        Assert.Equal("in the directory file", Encoding.ASCII.GetString(vpk.Read(@"MATERIALS\hud\Thing.VMT")!));
        Assert.Null(vpk.Read("materials/hud/absent.vmt"));
    }

    [Fact]
    public void A_file_that_is_not_a_vpk_is_refused_with_the_reason()
    {
        string notVpk = Path.Combine(_dir, "bad_dir.vpk");
        File.WriteAllBytes(notVpk, new byte[16]);
        Assert.Contains("marker", Assert.Throws<InvalidDataException>(() => VpkDirectory.Open(notVpk)).Message);

        string good = WriteVpk("short", 1, new VpkFile("a", "b", "c", Bytes("x"), 0));
        byte[] whole = File.ReadAllBytes(good);
        File.WriteAllBytes(good, whole[..^4]);
        Assert.Contains("runs past the end", Assert.Throws<InvalidDataException>(() => VpkDirectory.Open(good)).Message);
    }

    [Fact]
    public void A_part_shorter_than_the_directory_says_is_refused_naming_the_file()
    {
        string path = WriteVpk("cut", 2, new VpkFile("materials", "art", "vtf", Bytes("twelve bytes"), 0));
        File.WriteAllBytes(Path.Combine(_dir, "cut_000.vpk"), Bytes("twelve"));
        var vpk = VpkDirectory.Open(path);

        Assert.Contains("materials/art.vtf", Assert.Throws<InvalidDataException>(() => vpk.Read("materials/art.vtf")).Message);
    }

    [Fact]
    public void The_mods_gameinfo_mounts_its_relative_game_paths_in_order()
    {
        string mod = Path.Combine(_dir, "mods", "tf2");
        string content = Path.Combine(_dir, "content", "tf");
        Directory.CreateDirectory(Path.Combine(mod, "resource"));
        Directory.CreateDirectory(Path.Combine(content, "resource"));
        File.WriteAllText(Path.Combine(mod, "resource", "both.txt"), "the mod's own");
        File.WriteAllText(Path.Combine(content, "resource", "both.txt"), "the content's");
        File.WriteAllText(Path.Combine(content, "resource", "loose.txt"), "loose in content");
        string vpk = WriteVpk("pak", 2, new VpkFile("resource", "packed", "txt", Bytes("in the vpk"), 0));
        File.Move(vpk, Path.Combine(content, "pak_dir.vpk"));
        File.Move(Path.Combine(_dir, "pak_000.vpk"), Path.Combine(content, "pak_000.vpk"));
        File.WriteAllText(Path.Combine(mod, "gameinfo.txt"), """
            "GameInfo"
            {
                FileSystem
                {
                    SearchPaths
                    {
                        game+mod            |gameinfo_path|custom/*
                        game+game_write     |gameinfo_path|.
                        game                |gameinfo_path|../../content/tf/pak.vpk   // by its bare name
                        game                |gameinfo_path|../../content/tf
                        game                |gameinfo_path|../../content/nowhere
                        game                |all_source_engine_paths|hl2
                        platform            |gameinfo_path|../../content/tf
                    }
                }
            }
            """);

        var files = ContentFiles.FromGameInfo(Path.Combine(mod, "gameinfo.txt"));

        Assert.Equal("the mod's own", files.ReadGameFile("resource/both.txt"));
        Assert.Equal("in the vpk", files.ReadGameFile("resource/packed.txt"));
        Assert.Equal("loose in content", files.ReadGameFile("resource/loose.txt"));
        Assert.Null(files.ReadGameFile("resource/absent.txt"));
        Assert.Equal(4, files.Mounted.Count);
        Assert.Contains(files.Mounted, m => m.StartsWith("missing ") && m.EndsWith("nowhere"));
        Assert.DoesNotContain(files.Mounted, m => m.Contains("hl2") || m.Contains("custom"));
    }

    [Fact]
    public void A_mod_theme_names_its_fonts_and_the_files_answer_them()
    {
        byte[] ttf = new UiFontLibrary().Faces[0].Data;
        Directory.CreateDirectory(Path.Combine(_dir, "resource"));
        File.WriteAllBytes(Path.Combine(_dir, "resource", "ModFace.ttf"), ttf);
        var theme = SourceSharpTheme.Create();
        UiThemeFile.Apply(theme, """
            font.file: "Mod Face" resource/ModFace.ttf
            font.file: "Gone Face" resource/Gone.ttf
            """);

        var faces = PreviewRun.ModFonts(theme, ContentFiles.FromDirectory(_dir));

        Assert.Equal(["Mod Face"], faces.Select(f => f.Family));
        var library = new UiFontLibrary(faces);
        Assert.Equal("Mod Face", library.Face(new UiFontSpec("Mod Face", 20, 500, UiFontFlags.None)).Family);
        Assert.Equal(UiFontLibrary.Sans, library.Face(new UiFontSpec("Gone Face", 20, 500, UiFontFlags.None)).Family);
        Assert.Empty(PreviewRun.ModFonts(theme, null));
    }

    [Fact]
    public void A_mod_run_reads_the_mods_own_theme()
    {
        string mod = Path.Combine(_dir, "mods", "tf2");
        var options = new PreviewOptions(RepoLayout.RepoRoot, _dir, false, true, null, ModDir: mod);

        Assert.Equal(Path.Combine(mod, "resource", "ui-theme.yaml"), options.ThemeFile);
        Assert.EndsWith(Path.Combine("game", "mod_test", "resource", "ui-theme.yaml"),
                        (options with { ModDir = null }).ThemeFile);
    }
}
