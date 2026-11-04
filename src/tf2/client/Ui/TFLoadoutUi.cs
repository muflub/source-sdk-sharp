using SourceSharp.Abi;
using SourceSharp.Client.Ui;

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Client.Ui;

/// <summary>
/// The loadout screen's game half (D2): the <c>TF2:loadout</c> reply fills
/// what <see cref="ITFLoadoutSource"/> answers — the class's visible slots
/// with the equipped item's name token and inventory image, resolved on the
/// client's schema the way stock's <c>CItemModelPanel</c> reads its item —
/// and <see cref="ITFLoadoutActions"/> sends: the one-byte ask over a
/// SourceSharp net message, the named <c>tf2_equip</c> console line, and
/// the closing click.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFLoadoutSource), typeof(ITFLoadoutActions)])]
public sealed class TFLoadoutUi(IFwdEngineClient console, IEngineNetworking network, ItemSchema schema,
                                ITFMenuNavigation nav, IUiLocalisation localiser)
    : ITFLoadoutSource, ITFLoadoutActions, ITFLoadoutReplyHook
{
    private LoadoutReply _reply;
    private TFLoadoutSlotView[] _slots = [];
    private TFLoadoutOwnedItem[] _owned = [];

    public void OnLoadoutReply(LoadoutReply reply)
    {
        schema.EnsureLoaded();
        _reply = reply;

        var slots = reply.EquippedSlots ?? [];
        _slots = new TFLoadoutSlotView[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            uint defindex = reply.EquippedDefindexes is { } e && i < e.Length ? e[i] : LoadoutReply.NoItem;
            var (name, icon) = SchemaOf(defindex);
            _slots[i] = new TFLoadoutSlotView(slots[i], Label((TFLoadoutSlot)slots[i]), name, icon);
        }

        var defindexes = reply.OwnedDefindexes ?? [];
        var ownedSlots = reply.OwnedSlots ?? [];
        _owned = new TFLoadoutOwnedItem[defindexes.Length];
        for (int i = 0; i < defindexes.Length; i++)
        {
            var (name, icon) = SchemaOf(defindexes[i]);
            _owned[i] = new TFLoadoutOwnedItem((int)defindexes[i],
                ownedSlots is { } s && i < s.Length ? s[i] : 0, name, icon);
        }
    }

    public int Class => _reply.Class;

    public int Slots => _slots.Length;

    public TFLoadoutSlotView Slot(int index) => _slots[index];

    public int OwnedCount => _owned.Length;

    public TFLoadoutOwnedItem Owned(int index) => _owned[index];

    public void Select(int classNumber) =>
        network.SendServerMessage(LoadoutAsk.Key, LoadoutAsk.Write((byte)classNumber));

    public void Equip(int classNumber, int slot, int defindex)
    {
        // The command's slot words are the schema's own (TFItemStrings.ClassSlotIndex);
        // the taunt tabs are not equippable from the screen yet.
        string slotWord = slot switch
        {
            0 => "primary",
            1 => "secondary",
            2 => "melee",
            3 => "utility",
            4 => "building",
            5 => "pda",
            6 => "pda2",
            7 => "head",
            8 => "misc",
            9 => "action",
            10 => "misc2",
            _ => "",
        };
        if (slotWord.Length == 0)
            return;

        console.SubmitConsoleLine($"tf2_equip {TFClassNames.Raw((TFClass)classNumber)} {slotWord} " +
                                  (defindex < 0 ? "none" : defindex.ToString()));
    }

    public void Close() => nav.Close("tf2loadout");

    /// <summary>The item's name token and inventory image, off the client's
    /// schema; the no-item marker and an unknown defindex are empty (the
    /// screen's empty arm).</summary>
    private (string Name, string Icon) SchemaOf(uint defindex)
    {
        if (defindex == LoadoutReply.NoItem || schema.Item((int)defindex) is not { } def)
            return ("", "");

        // The quality prefix and the Australium word stock folds into an
        // item's displayed name (econ_item_description.cpp:748-789 and
        // :822-832): the quality word first, the Australium word next, the
        // name last — one finished string, so every panel that reads the
        // source sees the same one. The name itself rides the mod's token
        // chain (the screen's Find ?? raw arm), resolved before the words
        // are joined; without anything to prefix it stays the token.
        string qualityWord = TFItemStrings.QualityWord(schema.QualityName(def.Quality));
        bool australium = TFItemStrings.HasAttribute(def, "is australium item");
        string itemName = def.ItemName;
        if (qualityWord.Length > 0 || australium)
            itemName = localiser.Find(itemName) ?? itemName;

        return (TFItemStrings.PrefixedName(itemName, qualityWord, australium),
                def.InventoryImage);
    }

    /// <summary>Stock's own display strings (tf_item_schema.cpp:1543-1569):
    /// the three wearables share the misc label.</summary>
    private static string Label(TFLoadoutSlot slot) => slot switch
    {
        TFLoadoutSlot.Primary => "#LoadoutSlot_Primary",
        TFLoadoutSlot.Secondary => "#LoadoutSlot_Secondary",
        TFLoadoutSlot.Melee => "#LoadoutSlot_Melee",
        TFLoadoutSlot.Utility => "#LoadoutSlot_Utility",
        TFLoadoutSlot.Building => "#LoadoutSlot_Building",
        TFLoadoutSlot.Pda => "#LoadoutSlot_pda",
        TFLoadoutSlot.Pda2 => "#LoadoutSlot_pda2",
        TFLoadoutSlot.Action => "#LoadoutSlot_Action",
        _ => "#LoadoutSlot_Misc",
    };
}
