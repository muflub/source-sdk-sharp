using SourceSharp.Abi;

using TF2.Shared;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The selection icons a weapon script carries: the <c>TextureData</c>
/// block's <c>weapon</c> (inactive, the red team's art) and
/// <c>weapon_s</c> (active, blue) entries, the rect stock's
/// <c>LoadHudTextures</c> reads (hud.cpp:118-141) and
/// <c>WeaponsResource::LoadWeaponSprites</c> hangs off
/// <c>iconInactive</c>/<c>iconActive</c> (weapons_resource.cpp:85-100) —
/// the same two <c>CHudTexture</c>s <c>GetSpriteInactive</c>/
/// <c>GetSpriteActive</c> hand the selection's draw
/// (tf_hud_weaponselection.cpp:803-820).
/// </summary>
public sealed class TFWeaponScriptIconTests
{
    private const string Medigun = """
        WeaponData
        {
            "printname"  "#TF_Weapon_Medigun"
            "bucket"     "1"
            "bucket_position" "0"

            TextureData
            {
                "weapon"
                {
                    "file"   "sprites/bucket_medigun_red"
                    "x"      "0"
                    "y"      "0"
                    "width"  "200"
                    "height" "128"
                }
                "weapon_s"
                {
                    "file"   "sprites/bucket_medigun_blue"
                    "x"      "0"
                    "y"      "0"
                    "width"  "200"
                    "height" "128"
                }
                "ammo"
                {
                    "file"   "sprites/a_icons1"
                    "x"      "55"
                    "y"      "60"
                    "width"  "73"
                    "height" "15"
                }
            }
        }
        """;

    [Fact]
    public void The_selection_icons_are_the_weapon_entries()
    {
        // As the database hands it over: the document's WeaponData block.
        var script = TFWeaponScriptParser.Parse(KeyValuesText.Parse(Medigun).Children[0], "tf_weapon_medigun");

        Assert.Equal(new TFHudIcon("sprites/bucket_medigun_red", 0, 0, 200, 128), script.IconInactive);
        Assert.Equal(new TFHudIcon("sprites/bucket_medigun_blue", 0, 0, 200, 128), script.IconActive);
    }

    [Fact]
    public void A_script_without_the_block_carries_no_icons()
    {
        var script = TFWeaponScriptParser.Parse(
            KeyValuesText.Parse("WeaponData\n{\n\t\"printname\"\t\"x\"\n}").Children[0], "tf_weapon_bat");

        Assert.Null(script.IconInactive);
        Assert.Null(script.IconActive);
    }
}
