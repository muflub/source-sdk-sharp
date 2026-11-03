using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The loadout screen from fakes (the ui-screen loop's sample step): the
// states stock's CClassLoadoutPanel is ever in, built through the same
// container the game uses. The #LoadoutSlot_*, #TF_Class_Name_* and
// #NoItemsToEquip tokens live in game/mod_tf2/resource/tf2_english.txt;
// the item names ride the sample localiser (in game, the schema's
// item_name tokens go through the engine's tf_english.txt).

public static class TFLoadoutScreenSamples
{
    private static UiScreen Build(UiSampleServices s, FakeTFLoadoutSource? source = null,
                                  FakeTFLoadoutActions? actions = null)
    {
        s.Add<ITFLoadoutSource>(source ?? new FakeTFLoadoutSource());
        s.Add<ITFLoadoutActions>(actions ?? new FakeTFLoadoutActions());
        s.Add<IUiLocalisation>(new LoadoutLocalise());
        return s.Create<TFLoadoutScreen>();
    }

    // A scout's loadout on the reply that named it: stock's seven panels in
    // position order — the three weapons in column 0, the cosmetics and the
    // action slot in column 1 — the second slot with nothing in it, and the
    // class strip with the class the reply names armed.
    [UiSample("scout loadout")]
    private static UiScreen ScoutLoadout(UiSampleServices s) => Build(s, new FakeTFLoadoutSource
    {
        Class = 1,
        Slots =
        [
            new(0, "#LoadoutSlot_Primary", "#TF_Weapon_Scattergun", "backpack/weapons/c_models/c_scattergun"),
            new(1, "#LoadoutSlot_Secondary", "", ""),
            new(2, "#LoadoutSlot_Melee", "#TF_Weapon_Bat", "backpack/weapons/c_models/c_bat"),
            new(7, "#LoadoutSlot_Misc", "#TF_Weapon_Cap", "backpack/player/items/scout/scout_cap"),
            new(8, "#LoadoutSlot_Misc", "#TF_Weapon_Dogfighter", ""),
            new(10, "#LoadoutSlot_Misc", "", ""),
            new(9, "#LoadoutSlot_Action", "#TF_Weapon_LunchBox", ""),
        ],
        Owned =
        [
            new(200, 0, "#TF_Weapon_Scattergun", "backpack/weapons/c_models/c_scattergun"),
            new(45, 0, "#TF_Weapon_ForceANature", "backpack/weapons/c_models/c_force_a_nature"),
            new(220, 0, "#TF_Weapon_Shortstop", "backpack/weapons/c_models/c_shortstop"),
            new(23, 1, "#TF_Weapon_Pistol", "backpack/weapons/c_models/c_pistol"),
            new(0, 2, "#TF_Weapon_Bat", "backpack/weapons/c_models/c_bat"),
            new(999, 7, "#TF_Weapon_Cap", "backpack/player/items/scout/scout_cap"),
        ],
    });

    // Nothing equipped anywhere and nothing owned: every panel in the
    // no-item arm and the selection view with its no-items label.
    [UiSample("nothing equipped")]
    private static UiScreen NothingEquipped(UiSampleServices s) => Build(s, new FakeTFLoadoutSource
    {
        Class = 3,
        Slots =
        [
            new(0, "#LoadoutSlot_Primary", "", ""),
            new(1, "#LoadoutSlot_Secondary", "", ""),
            new(2, "#LoadoutSlot_Melee", "", ""),
        ],
    });

    // Stock's item selection over the canvas: the primary slot's three
    // owned weapons in the 94x70 grid, the slot's label west and the
    // cancel under the second dotted line.
    [UiSample("picking a primary")]
    private static UiScreen PickingPrimary(UiSampleServices s)
    {
        var screen = Build(s, new FakeTFLoadoutSource
        {
            Class = 1,
            Slots = [new(0, "#LoadoutSlot_Primary", "#TF_Weapon_Scattergun", "backpack/weapons/c_models/c_scattergun")],
            Owned =
            [
                new(200, 0, "#TF_Weapon_Scattergun", "backpack/weapons/c_models/c_scattergun"),
                new(45, 0, "#TF_Weapon_ForceANature", "backpack/weapons/c_models/c_force_a_nature"),
                new(220, 0, "#TF_Weapon_Shortstop", "backpack/weapons/c_models/c_shortstop"),
                new(23, 1, "#TF_Weapon_Pistol", "backpack/weapons/c_models/c_pistol"),
            ],
        });
        ((TFLoadoutScreen)screen).Picking = 0;
        return screen;
    }

    // The engineer's eight: the PDA pair and the toolbox take the arms the
    // default row leaves at position 4, and the strip arms the engineer.
    [UiSample("engineer loadout")]
    private static UiScreen EngineerLoadout(UiSampleServices s) => Build(s, new FakeTFLoadoutSource
    {
        Class = 9,
        Slots =
        [
            new(0, "#LoadoutSlot_Primary", "#TF_Weapon_Shotgun", "backpack/weapons/c_models/c_shotgun"),
            new(1, "#LoadoutSlot_Secondary", "#TF_Weapon_Pistol", "backpack/weapons/c_models/c_pistol"),
            new(2, "#LoadoutSlot_Melee", "#TF_Weapon_Wrench", "backpack/weapons/c_models/c_wrench"),
            new(5, "#LoadoutSlot_pda", "#TF_Weapon-builder", "backpack/weapons/c_models/c_builder"),
            new(6, "#LoadoutSlot_pda2", "#TF_Weapon_DestructionPDA", "backpack/weapons/c_models/c_pda_engineer"),
        ],
        Owned =
        [
            new(298, 5, "#TF_Weapon-builder", "backpack/weapons/c_models/c_builder"),
            new(28, 6, "#TF_Weapon_DestructionPDA", "backpack/weapons/c_models/c_pda_engineer"),
        ],
    });
}

/// <summary>The reply as the screen reads it: the class, the visible slots
/// in panel order and the owned items — the record structs the contract
/// names, listed straight.</summary>
public sealed class FakeTFLoadoutSource : ITFLoadoutSource
{
    public int Class { get; init; }

    public TFLoadoutSlotView[] Slots { get; init; } = [];

    public TFLoadoutOwnedItem[] Owned { get; init; } = [];

    int ITFLoadoutSource.Slots => Slots.Length;

    TFLoadoutSlotView ITFLoadoutSource.Slot(int index) => Slots[index];

    int ITFLoadoutSource.OwnedCount => Owned.Length;

    TFLoadoutOwnedItem ITFLoadoutSource.Owned(int index) => Owned[index];
}

/// <summary>What the clicks did — the screen fact's recorder, read after
/// the click's frame.</summary>
public sealed class FakeTFLoadoutActions : ITFLoadoutActions
{
    public List<int> Selects { get; } = [];
    public List<(int Class, int Slot, int Defindex)> Equips { get; } = [];
    public int Closes { get; private set; }

    public void Select(int classNumber) => Selects.Add(classNumber);

    public void Equip(int classNumber, int slot, int defindex) => Equips.Add((classNumber, slot, defindex));

    public void Close() => Closes++;
}

/// <summary>The tokens the screen resolves itself (the item names) plus
/// the ones the samples' fakes carry.</summary>
public sealed class LoadoutLocalise : IUiLocalisation
{
    public string? Find(string token) => token switch
    {
        "#TF_Weapon_Scattergun" => "Scattergun",
        "#TF_Weapon_ForceANature" => "Force-A-Nature",
        "#TF_Weapon_Shortstop" => "Shortstop",
        "#TF_Weapon_Pistol" => "Pistol",
        "#TF_Weapon_Bat" => "Bat",
        "#TF_Weapon_Cap" => "Bonk! Helmet",
        "#TF_Weapon_Dogfighter" => "Dogfighter",
        "#TF_Weapon_LunchBox" => "Lunch Box",
        "#TF_Weapon_Shotgun" => "Shotgun",
        "#TF_Weapon_Wrench" => "Wrench",
        "#TF_Weapon-builder" => "Builder",
        "#TF_Weapon_DestructionPDA" => "Destruction PDA",
        _ => null,
    };
}
