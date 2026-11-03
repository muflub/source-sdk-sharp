using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Tests.Player;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Anim;

/// <summary>
/// Stock's <c>CTFViewModel::GetSkin</c> (tf_viewmodel.cpp:362-398) as the
/// chain's skin rule: the item's own skin for the owner's team first, and —
/// when the item names none — the weapon script's
/// <c>HasTeamSkins_Viewmodel</c>, RED 0 / BLU 1.
/// </summary>
public sealed class TFViewmodelTeamSkinTests
{
    private const int Player = PlayerRig.Index;

    private static TFWeaponTypes Types() => WeaponFixtures.Types(EconTestFiles.Fixture("items_game.trimmed.txt"));

    private static TFViewmodelTeamSkinRule Rule(TFWeaponTypes types, int team) => new(
        types,
        new TFWeaponScripts(new RecordingLog(), WeaponFixtures.ShippedWithClasses()),
        new RecordingEngine()
            .Returns("Players.GetHandle", PlayerRig.Player)
            .Returns("Players.GetTeam", team));

    private static WeaponType Row(TFWeaponTypes types, string name) => types.Rows().First(r => r.Name == name);

    /// <summary>
    /// The fixture's base fists item's styles name the per-team VIEWMODEL
    /// skins the same as the player skins (v_skin_red 2 / v_skin_blu 3 over
    /// skin_red 2 / skin_blu 3); this schema splits them apart to show the
    /// viewmodel arm's precedence, and adds the base class's common "skin".
    /// </summary>
    private const string StyledSchema = """
        "items_game"
        {
            "qualities"
            {
            }
            "attributes"
            {
            }
            "items"
            {
                "110"
                {
                    "name"            "TF_WEAPON_FISTS"
                    "item_class"      "tf_weapon_fists"
                    "baseitem"        "1"
                    "visuals"
                    {
                        "styles"
                        {
                            "0"
                            {
                                "skin_red"    "2"
                                "skin_blu"    "3"
                                "v_skin_red"  "4"
                                "v_skin_blu"  "5"
                            }
                        }
                    }
                }
                "111"
                {
                    "name"            "TF_WEAPON_BOTTLE"
                    "item_class"      "tf_weapon_bottle"
                    "baseitem"        "1"
                    "visuals"
                    {
                        "styles"
                        {
                            "0"
                            {
                                "skin"        "7"
                            }
                        }
                    }
                }
            }
        }
        """;

    [Fact]
    public void The_blu_medic_s_medigun_wears_skin_1()
    {
        // The medigun's item names no styles, so stock's second arm decides:
        // the script's HasTeamSkins_Viewmodel, BLU 1. This is the bug the
        // rule exists for — the live BLU medic drew the RED sleeve.
        Assert.Equal(1, Rule(Types(), (int)TFTeam.Blue).Skin(Player, Row(Types(), "tf_weapon_medigun"), null));
    }

    [Fact]
    public void The_red_medic_s_medigun_wears_skin_0()
    {
        Assert.Equal(0, Rule(Types(), (int)TFTeam.Red).Skin(Player, Row(Types(), "tf_weapon_medigun"), null));
    }

    [Fact]
    public void A_team_that_is_neither_leaves_the_answer()
    {
        // Stock's switch falls through and the base class's skin stands.
        Assert.Equal(7, Rule(Types(), 0).Skin(Player, Row(Types(), "tf_weapon_medigun"), 7));
    }

    [Fact]
    public void A_weapon_with_no_team_skins_and_no_item_skin_leaves_the_answer()
    {
        // tf_weapon_pistol_scout's script sets no flag and its base item has
        // no styles block: the rule has no opinion.
        Assert.Equal(7, Rule(Types(), (int)TFTeam.Blue).Skin(Player, Row(Types(), "tf_weapon_pistol_scout"), 7));
    }

    [Fact]
    public void The_item_s_own_skins_win_over_the_team_skins_flag()
    {
        // tf_weapon_fists' script HAS the flag, and its base item (through
        // the weapon_fists prefab) has styles: the item arm answers first
        // (tf_viewmodel.cpp:374-385).
        Assert.Equal(2, Rule(Types(), (int)TFTeam.Red).Skin(Player, Row(Types(), "tf_weapon_fists"), null));
        Assert.Equal(3, Rule(Types(), (int)TFTeam.Blue).Skin(Player, Row(Types(), "tf_weapon_fists"), null));
    }

    [Fact]
    public void The_item_s_viewmodel_skins_win_over_its_player_skins()
    {
        // CEconStyleInfo::GetSkin's arm: the viewmodel skins where set.
        var types = WeaponFixtures.Types(StyledSchema);
        Assert.Equal(4, Rule(types, (int)TFTeam.Red).Skin(Player, Row(types, "tf_weapon_fists"), null));
        Assert.Equal(5, Rule(types, (int)TFTeam.Blue).Skin(Player, Row(types, "tf_weapon_fists"), null));
    }

    [Fact]
    public void The_common_skin_word_stands_in_for_the_per_team_ones()
    {
        // Stock reads the base class's "skin" after the per-team words
        // (tf_item_schema.cpp:1175), so it overrides them: no "skin" word
        // would answer style 0's default 0.
        var types = WeaponFixtures.Types(StyledSchema);
        Assert.Equal(7, Rule(types, (int)TFTeam.Red).Skin(Player, Row(types, "tf_weapon_bottle"), null));
        Assert.Equal(7, Rule(types, (int)TFTeam.Blue).Skin(Player, Row(types, "tf_weapon_bottle"), null));
    }
}
