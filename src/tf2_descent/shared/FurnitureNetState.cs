using SourceSharp.Abi;

// Compiled into both TF2.Descent.Server and TF2.Descent.Client
// (plan_side_isolation §2.4): a furniture entity's networked state and the
// catalog both sides read it by (plan_descent_logic WO3).

namespace TF2.Descent.Shared;

/// <summary>
/// A <c>tf2_descent_furniture</c>'s networked state: what stands there and
/// how it is. Every client sees furniture alike (nothing about it is one
/// hero's), so the whole state rides here: the kind and variant
/// (<see cref="FurnitureCatalog"/>'s numbers, 0 while parked), its flags
/// (<see cref="FurnitureFlags"/>) and how much it has left (a Dispenser's
/// charge, an explosive's health), 0 to 255.
/// </summary>
[StandaloneNetState(ClassName)]
public partial class FurnitureNetState
{
    public const string ClassName = "tf2_descent_furniture";

    /// <summary>The catalog's entry plus one; 0 while the entity is parked.</summary>
    public partial byte Entry { get; set; }

    /// <summary><see cref="FurnitureFlags"/> bits.</summary>
    public partial byte Flags { get; set; }

    /// <summary>What is left of it, 0 to 255: a Dispenser's charge, an explosive's health; 255 for the rest.</summary>
    public partial byte Left { get; set; }
}

/// <summary>What furniture is, by kind (the rules' <c>FurnitureKind</c>, in the same order).</summary>
public enum FurnitureKindId : byte
{
    Shrine,
    Container,
    Dispenser,
    Explosive,
    Trap,
    Breakable,
}

/// <summary>How a piece of furniture is, as bits of <see cref="FurnitureNetState.Flags"/>.</summary>
[Flags]
public enum FurnitureFlags : byte
{
    None = 0,

    /// <summary>A shrine used, a Dispenser with no charge, a trap that will not fire again.</summary>
    Spent = 1,

    /// <summary>A container opened.</summary>
    Opened = 2,

    /// <summary>A container that needs its key.</summary>
    Locked = 4,

    /// <summary>A trap stepped on: it fires when its fuse runs.</summary>
    Tripped = 8,

    /// <summary>A trap that fired and is not armed yet.</summary>
    Rearming = 16,
}

/// <summary>
/// One kind of furniture as both sides know it: its kind, the rules' variant
/// id, the name a hero reads (a localisation token: a shrine's is the rules'
/// hint, never its effect), and the TF2 models it wears, standing and spent
/// or opened. No art of Descent's own: every model is TF2's or HL2's, from
/// the mod's GAME path. A crate's or barrel's spent model is the one its break
/// shatters: the mining crate's own break leaves its body standing (its first
/// stage, <c>miningcrate001_break01</c>), so it breaks as that body, whose
/// pieces are all splinters.
/// </summary>
public sealed record FurnitureEntry(FurnitureKindId Kind, string Variant, string NameToken, string Model, string SpentModel);

/// <summary>
/// Every variant the rules can place (furniture.json), numbered for the wire.
/// A fact holds it to the rules: every shrine effect, container kind,
/// explosive, trap and breakable has its entry, and a shrine's name is the rules' own.
/// </summary>
public static class FurnitureCatalog
{
    /// <summary>Merasmus's book, the shrine standing; the candles left when its power is spent.</summary>
    public const string ShrineModel = "models/props_halloween/bombonomicon.mdl";

    public const string ShrineSpentModel = "models/props_halloween/candle_cluster.mdl";

    /// <summary>The Engineer's Dispenser (skin 0 red, 1 blue: blue when it is spent).</summary>
    public const string DispenserModel = "models/buildables/dispenser.mdl";

    public const string CrateModel = "models/props_2fort/miningcrate001.mdl";

    public const string LockerModel = "models/props_2fort/miningcrate002.mdl";

    public const string CrateOpenedModel = "models/props_2fort/gibs/miningcrate001_break01.mdl";

    public const string SarcophagusModel = "models/props_viaduct_event/coffin_simple_closed.mdl";

    public const string SarcophagusOpenedModel = "models/props_viaduct_event/coffin_simple_base.mdl";

    /// <summary>Badlands' oil drum: TF2 ships no explosive barrel (HL2's oildrum001_explosive is not in its content).</summary>
    public const string BarrelModel = "models/props_badlands/barrel01.mdl";

    /// <summary>The Halloween pumpkin bomb, stock's tf_pumpkin_bomb model.</summary>
    public const string PumpkinBombModel = "models/props_halloween/pumpkin_explode.mdl";

    /// <summary>Viaduct's stone pressure plate: a dart, a flame jet and a tesla coil are set under one.</summary>
    public const string PlateModel = "models/props_viaduct_event/stone_button_trap.mdl";

    /// <summary>A stickybomb lying on the floor: the charge.</summary>
    public const string ChargeModel = "models/weapons/w_models/w_stickybomb.mdl";

    /// <summary>Farm's wooden barrel: it breaks into staves (its prop_data, wooden.large, and its gibs).</summary>
    public const string WoodenBarrelModel = "models/props_farm/wooden_barrel.mdl";

    /// <summary>How many entries the catalog holds: wire numbers 1 to this.</summary>
    public const int Count = 19;

    private static (FurnitureKindId Kind, string Variant, string Name, string Model, string Spent)? Row(int number) => number switch
    {
        1 => (FurnitureKindId.Shrine, "fury", "#TF2Descent_Shrine_Fury", ShrineModel, ShrineSpentModel),
        2 => (FurnitureKindId.Shrine, "plenty", "#TF2Descent_Shrine_Plenty", ShrineModel, ShrineSpentModel),
        3 => (FurnitureKindId.Shrine, "sight", "#TF2Descent_Shrine_Sight", ShrineModel, ShrineSpentModel),
        4 => (FurnitureKindId.Shrine, "lore", "#TF2Descent_Shrine_Lore", ShrineModel, ShrineSpentModel),
        5 => (FurnitureKindId.Shrine, "bargain", "#TF2Descent_Shrine_Bargain", ShrineModel, ShrineSpentModel),
        6 => (FurnitureKindId.Shrine, "frailty", "#TF2Descent_Shrine_Frailty", ShrineModel, ShrineSpentModel),
        7 => (FurnitureKindId.Shrine, "drought", "#TF2Descent_Shrine_Drought", ShrineModel, ShrineSpentModel),
        8 => (FurnitureKindId.Container, "supply_crate", "#Descent_Furniture_SupplyCrate", CrateModel, CrateOpenedModel),
        9 => (FurnitureKindId.Container, "locker", "#Descent_Furniture_Locker", LockerModel, CrateOpenedModel),
        10 => (FurnitureKindId.Container, "sarcophagus", "#Descent_Furniture_Sarcophagus", SarcophagusModel, SarcophagusOpenedModel),
        11 => (FurnitureKindId.Dispenser, "dispenser", "#Descent_Furniture_Dispenser", DispenserModel, DispenserModel),
        12 => (FurnitureKindId.Explosive, "barrel", "#Descent_Furniture_Barrel", BarrelModel, BarrelModel),
        13 => (FurnitureKindId.Explosive, "pumpkin_bomb", "#Descent_Furniture_PumpkinBomb", PumpkinBombModel, PumpkinBombModel),
        14 => (FurnitureKindId.Trap, "dart", "#Descent_Furniture_Dart", PlateModel, PlateModel),
        15 => (FurnitureKindId.Trap, "flame_jet", "#Descent_Furniture_FlameJet", PlateModel, PlateModel),
        16 => (FurnitureKindId.Trap, "charge", "#Descent_Furniture_Charge", ChargeModel, ChargeModel),
        17 => (FurnitureKindId.Trap, "tesla_coil", "#Descent_Furniture_TeslaCoil", PlateModel, PlateModel),
        18 => (FurnitureKindId.Breakable, "crate", "#Descent_Furniture_Crate", CrateModel, CrateOpenedModel),
        19 => (FurnitureKindId.Breakable, "barrel", "#Descent_Furniture_WoodenBarrel", WoodenBarrelModel, WoodenBarrelModel),
        _ => null,
    };

    /// <summary>Every entry, in wire order.</summary>
    public static IEnumerable<FurnitureEntry> All()
    {
        for (int n = 1; n <= Count; n++)
            yield return Of((byte)n)!;
    }

    /// <summary>The entry's number on the wire (index plus one); 0 when the catalog has no such variant.</summary>
    public static byte Number(FurnitureKindId kind, string variant)
    {
        for (int n = 1; n <= Count; n++)
            if (Row(n) is { } r && r.Kind == kind && r.Variant == variant)
                return (byte)n;
        return 0;
    }

    /// <summary>The entry a wire number names; null for 0 or a number past the catalog.</summary>
    public static FurnitureEntry? Of(byte number) => Row(number) is { } r ? new FurnitureEntry(r.Kind, r.Variant, r.Name, r.Model, r.Spent) : null;
}
