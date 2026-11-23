using SourceSharp.Abi;

using TF2.Descent.Shared;

namespace TF2.Descent.Server.Town;

/// <summary>
/// One of the town's stalls (plan_tf2_rpg D15, Phase 9; plan_descent_logic WO8): the vendor NPC
/// or the furniture a hero uses, where it stands, how it looks, and the window its use opens.
/// <list type="bullet">
/// <item><paramref name="Marker"/>: the <c>info_target</c> it stands at (the town's <c>shop_&lt;name&gt;</c>),
/// <paramref name="Offset"/> from it (a stall the town has no marker for stands beside another's).</item>
/// <item><paramref name="Vendor"/>: the rules' vendor (vendors.json) whose stock it sells; null for none.</item>
/// <item><paramref name="Model"/>, <paramref name="Sequence"/>: a TF2 model and its idle (no art of Descent's own);
/// "" for a prop that stands still.</item>
/// <item><paramref name="NameToken"/>: the window's title, a token of the mod's language file.</item>
/// </list>
/// </summary>
public sealed record TownStall(
    string Id, string Marker, Vector Offset, string? Vendor, TownScreen Screen, TownStance Stance,
    string Model, string Sequence, string NameToken, string StanceToken, Vector Mins, Vector Maxs);

/// <summary>
/// The town's stalls, in their table order (a stall's index is its place here). The town
/// (src/mapgen/TF2DescentTown.cs, the map session's) carries a <c>shop_*</c> marker for each
/// place a stall stands: six buildings, and since 79be3d68af the Engineer's armour shop
/// (<c>shop_engineer</c>) and his bench (<c>shop_bench</c>), 176 either side of the smithy's.
/// </summary>
public static class TownStalls
{
    /// <summary>A TF2 player's hull, which a vendor in a player's model takes.</summary>
    public static Vector PlayerMins => new(-24f, -24f, 0f);

    public static Vector PlayerMaxs => new(24f, 24f, 82f);

    /// <summary>The marker's prefix: <c>shop_&lt;name&gt;</c>.</summary>
    public const string MarkerPrefix = "shop_";

    public const string HeavyModel = "models/player/heavy.mdl";
    public const string SpyModel = "models/player/spy.mdl";
    public const string MedicModel = "models/player/medic.mdl";
    public const string ScoutModel = "models/player/scout.mdl";
    public const string EngineerModel = "models/player/engineer.mdl";
    public const string LockerModel = "models/props_gameplay/resupply_locker.mdl";
    public const string BenchModel = "models/props_spytech/work_table001.mdl";

    /// <summary>The idle sequences (the player models' own: robots stand in the same).</summary>
    public const string StandMelee = "stand_MELEE", StandPrimary = "stand_PRIMARY";

    public static IReadOnlyList<TownStall> All =>
    [
        new("saxton", MarkerPrefix + "general_store", Vector.Zero, "saxton", TownScreen.Vendor, TownStance.Weapons,
            HeavyModel, StandMelee, "#Descent_Vendor_Hale", "#Descent_Vendor_Stance_Weapons", PlayerMins, PlayerMaxs),
        new("spy", MarkerPrefix + "assay_office", Vector.Zero, "spy", TownScreen.Vendor, TownStance.Identify,
            SpyModel, StandMelee, "#Descent_Vendor_Spy", "#Descent_Vendor_Stance_Identify", PlayerMins, PlayerMaxs),
        new("medic", MarkerPrefix + "saloon", Vector.Zero, "medic", TownScreen.Vendor, TownStance.Healing,
            MedicModel, StandMelee, "#Descent_Vendor_Medic", "#Descent_Vendor_Stance_Healing", PlayerMins, PlayerMaxs),
        new("scout", MarkerPrefix + "bunkhouse_1", Vector.Zero, "scout", TownScreen.Crate, TownStance.Gambling,
            ScoutModel, StandMelee, "#Descent_Vendor_Scout", "#Descent_Vendor_Stance_Gambling", PlayerMins, PlayerMaxs),
        new("stash", MarkerPrefix + "bunkhouse_2", Vector.Zero, null, TownScreen.Stash, TownStance.Weapons,
            LockerModel, "", "#Descent_Stash_Title", "", new(-24f, -24f, 0f), new(24f, 24f, 96f)),
        new("engineer_repair", MarkerPrefix + "smithy", Vector.Zero, null, TownScreen.Vendor, TownStance.Repair,
            EngineerModel, StandMelee, "#Descent_Vendor_Engineer", "#Descent_Vendor_Stance_Repair", PlayerMins, PlayerMaxs),
        new("engineer", MarkerPrefix + "engineer", Vector.Zero, "engineer", TownScreen.Vendor, TownStance.Armour,
            EngineerModel, StandMelee, "#Descent_Vendor_Engineer", "#Descent_Vendor_Stance_Armour", PlayerMins, PlayerMaxs),
        new("bench", MarkerPrefix + "bench", Vector.Zero, null, TownScreen.Craft, TownStance.Repair,
            BenchModel, "", "#Descent_Craft_Title", "", new(-32f, -32f, 0f), new(32f, 32f, 40f)),
    ];

    /// <summary>The stall a name says (its id), or null.</summary>
    public static TownStall? Find(string id) => All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>A stall's index in <see cref="All"/>; -1 for none.</summary>
    public static int IndexOf(string id)
    {
        var all = All;
        for (int i = 0; i < all.Count; i++)
            if (all[i].Id == id)
                return i;
        return -1;
    }
}
