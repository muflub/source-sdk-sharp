using System.Buffers.Binary;

using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The HUD icon table (stock's hud_textures.txt / mod_textures.txt): an
/// entry names a texture file and the pixel rectangle in it
/// (CHud::LoadHudTextures, hud.cpp:58-141), the first file to name an icon
/// keeps it (AddSearchableHudIconToList, hud.cpp:772-787), and the library
/// resolves a name to a handle and the normalized UV that rectangle makes.
/// </summary>
public class HudIconTableTests
{
    // The real files' shape (tf2_misc_dir.vpk scripts/mod_textures.txt): the
    // block names TextureFileRefs/TextureData/font are written bare, the
    // root key wraps both blocks, and a file ref is a block of its own.
    private const string Table = """
        "sprites/640_hud"
        {
            TextureFileRefs
            {
                "dfile"    { "prefix"    "d_" }
                "dnegfile" { "prefix"    "dneg_" }
            }

            TextureData
            {
                "hud_test_icon"
                {
                    "file"    "vgui/test_art"
                    "x"    "4"
                    "y"    "2"
                    "width"    "2"
                    "height"    "1"
                }

                "hud_test_d"
                {
                    "dfile"    "test_d"
                    "dnegfile" "test_dneg"
                    "x"    "0"
                    "y"    "0"
                    "width"    "4"
                    "height"    "4"
                }

                "hud_test_font"
                {
                    font    "ResourceHudFontSmaller"
                }
            }
        }
        """;

    private const string OverridingTable = """
        "sprites/640_hud"
        {
            "TextureData"
            {
                "hud_test_icon"    { "file" "vgui/other_art" "x" "0" "y" "0" "width" "8" "height" "8" }
            }
        }
        """;

    [Fact]
    public void An_Entry_Names_Its_File_And_Pixel_Rect()
    {
        var entry = HudIconTable.Parse(Table).Entry("hud_test_icon");
        Assert.Equal(new HudIconEntry("vgui/test_art", 4, 2, 2, 1), entry);
    }

    [Fact]
    public void A_File_Ref_Key_Prefixes_The_Icons_Name()
    {
        // TextureFileRefs maps a file key to the prefix its icon's NAME takes
        // (hud.cpp:111-129: szShortName is the prefix then the entry's name,
        // szTextureFile the key's value as written): "hud_test_d" with
        // "dfile" "test_d" is the icon d_hud_test_d on the file test_d — the
        // way TF2's "skull_tf" { "dfile" "HUD/d_images" } is d_skull_tf.
        var table = HudIconTable.Parse(Table);
        Assert.Equal(new HudIconEntry("test_d", 0, 0, 4, 4), table.Entry("d_hud_test_d"));
        Assert.Null(table.Entry("hud_test_d"));   // no "file" key: no bare name
    }

    [Fact]
    public void Each_File_Ref_Key_Of_An_Entry_Is_An_Icon_Of_Its_Own()
    {
        // The loop runs every ref (hud.cpp:113-129): the dneg_ art of the same
        // rectangle is its own icon on its own file.
        Assert.Equal(new HudIconEntry("test_dneg", 0, 0, 4, 4), HudIconTable.Parse(Table).Entry("dneg_hud_test_d"));
    }

    [Fact]
    public void A_Font_Entry_Is_No_Bitmap()
    {
        // The font arm continues past the rect (hud.cpp:96-110): a font icon
        // is no texture a UV rectangle makes.
        Assert.Null(HudIconTable.Parse(Table).Entry("hud_test_font"));
    }

    [Fact]
    public void A_Name_No_Entry_Holds_Is_Null()
        => Assert.Null(HudIconTable.Parse(Table).Entry("hud_test_nope"));

    [Fact]
    public void The_First_File_To_Name_An_Icon_Wins()
    {
        // hud_textures is loaded before mod_textures (hud.cpp:464-465) and
        // AddSearchableHudIconToList answers the icon already in the list
        // (hud.cpp:772-787): the earlier table's entry survives.
        var both = HudIconTable.Chain(HudIconTable.Parse(Table), HudIconTable.Parse(OverridingTable));
        Assert.Equal(new HudIconEntry("vgui/test_art", 4, 2, 2, 1), both.Entry("hud_test_icon"));
    }

    [Fact]
    public void The_Library_Answers_An_Icon_As_Its_Handle_And_Uv()
    {
        // 8x8 texture, rect (4,2)-(6,3): the UV rectangle is its normalized
        // quarter/eighth. The handle is the file's own, not a fresh one.
        var textures = new PreviewTextures();
        var library = new PreviewUiTextureLibrary(new StubFiles(), textures);
        var icon = library.Icon("hud_test_icon");

        Assert.True(icon.HasValue, "the icon did not resolve");
        var file = library.About("vgui/test_art");
        Assert.True(file.HasValue, "the icon's file did not load");
        Assert.Equal(file.Value.Handle, icon.Value.Handle);
        Assert.Equal(0.5f, icon.Value.Uv.X, 5);
        Assert.Equal(0.25f, icon.Value.Uv.Y, 5);
        Assert.Equal(0.25f, icon.Value.Uv.W, 5);
        Assert.Equal(0.125f, icon.Value.Uv.H, 5);
        // The table's own pixels: what stock's EffectiveWidth/Height scale
        // (hud_basedeathnotice.cpp:179-189 keeps the icon's aspect).
        Assert.Equal(2, icon.Value.Wide);
        Assert.Equal(1, icon.Value.Tall);
    }

    [Fact]
    public void A_Missing_Icon_Is_Null()
    {
        var library = new PreviewUiTextureLibrary(new StubFiles(), new PreviewTextures());
        Assert.False(library.Icon("hud_test_nope").HasValue);
        Assert.False(new PreviewUiTextureLibrary(null, new PreviewTextures()).Icon("hud_test_icon").HasValue);
    }

    /// <summary>A game-files double serving the icon table and one small
    /// RGBA8888 VTF under the material path the entry names.</summary>
    private sealed class StubFiles : IEngineFiles
    {
        public int GameFileSize(string path) => ReadGameFileBytes(path) is { } bytes ? bytes.Length : -1;

        public string? ReadGameFile(string path)
            => path == "materials/vgui/test_art.vmt"
                   ? "\"UnlitGeneric\"\n{\n    \"$basetexture\" \"vgui/test_art\"\n}"
                   : null;

        public byte[]? ReadGameFileBytes(string path) => path switch
        {
            "scripts/mod_textures.txt" => System.Text.Encoding.UTF8.GetBytes(Table),
            "materials/vgui/test_art.vtf" => Vtf(8, 8),
            _ => null,
        };

        public byte[]? ReadFileOnPath(string path, string pathId) => ReadGameFileBytes(path);
    }

    /// <summary>A minimal 7.2 RGBA8888 VTF: 80-byte header, one mip, the pixels after it.</summary>
    private static byte[] Vtf(int wide, int tall)
    {
        var vtf = new byte[80 + wide * tall * 4];
        "VTF\0"u8.CopyTo(vtf);
        BinaryPrimitives.WriteInt32LittleEndian(vtf.AsSpan(4), 7);
        BinaryPrimitives.WriteInt32LittleEndian(vtf.AsSpan(8), 2);
        BinaryPrimitives.WriteInt32LittleEndian(vtf.AsSpan(12), 80);
        BinaryPrimitives.WriteUInt16LittleEndian(vtf.AsSpan(16), (ushort)wide);
        BinaryPrimitives.WriteUInt16LittleEndian(vtf.AsSpan(18), (ushort)tall);
        BinaryPrimitives.WriteInt32LittleEndian(vtf.AsSpan(20), 0);          // flags
        BinaryPrimitives.WriteUInt16LittleEndian(vtf.AsSpan(24), 1);         // frames
        BinaryPrimitives.WriteInt32LittleEndian(vtf.AsSpan(52), 0);          // RGBA8888
        vtf[56] = 1;                                                         // mips
        BinaryPrimitives.WriteInt32LittleEndian(vtf.AsSpan(57), -1);         // no low-res image
        vtf[63] = 1;                                                         // depth
        for (int i = 0; i < wide * tall; i++)
            vtf[80 + i * 4] = (byte)i;
        return vtf;
    }
}
