using SourceSharp.Abi;

namespace TF2.Descent.Server.Items;

/// <summary>
/// What a new hero of the in-memory ledger starts with, so there is something to equip and
/// drink (HE0): the class's three stock weapon bases in the backpack and three potions on the
/// belt, each a Stock item rolled through the rules with a forced base. This is content and
/// belongs in the rules' tables (a per-class kit beside classes.json, which the host would
/// then also give a new character); it is kept here, in one class, until the rules own it.
/// </summary>
[SideService(Side.Server)]
public sealed class StartingKit
{
    /// <summary>The source the kit is rolled under: a craft's (identified, never cursed or promoted).</summary>
    public const string Source = "craft:starting_kit";

    /// <summary>The backpack's bases by class: the class's stock primary, secondary and melee.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Weapons { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["soldier"] = ["rocket_launcher", "shotgun", "shovel"],
        ["scout"] = ["scattergun", "pistol", "bat"],
        ["pyro"] = ["flamethrower", "flare_gun", "fire_axe"],
        ["demoman"] = ["grenade_launcher", "stickybomb_launcher", "bottle"],
        ["heavy"] = ["minigun", "fists"],
        ["engineer"] = ["pistol", "wrench"],
        ["medic"] = ["syringe_gun", "medigun", "bonesaw"],
        ["sniper"] = ["sniper_rifle", "smg", "kukri"],
        ["spy"] = ["revolver", "knife"],
    };

    /// <summary>The belt's potions, by belt slot.</summary>
    public IReadOnlyList<string> Potions { get; } = ["health_small", "health_small", "mana_small"];

    /// <summary>The weapon bases for a class; none for a class the kit does not name.</summary>
    public IReadOnlyList<string> WeaponsOf(string classId) => Weapons.TryGetValue(classId, out var w) ? w : [];
}
