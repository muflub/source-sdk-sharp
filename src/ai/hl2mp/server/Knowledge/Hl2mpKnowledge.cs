using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Knowledge;

/// <summary>
/// The game's weapon knowledge (plan_hl2mp_ai §5.0, ruling 2026-11-01), from
/// the classes the weapons run on: the weapon table's clip and refire
/// interval, the projectile's speed, the reload animation's length on the
/// weapon's own view model, whether others hear the reload, and whether it
/// reloads shell by shell. No second copy of a number.
/// </summary>
public static class WeaponKnowledgeBuilder
{
    /// <summary>
    /// The behaviours whose reload plays its sound on the server, so others
    /// hear it (§4.5): the shotgun's shell, the SMG1's, the RPG's and the
    /// pistol's <c>attack.Arms.Sound(attack.Type.ReloadSound)</c>
    /// (Hl2mpWeaponBehaviours.cs:190, 454, 971, 1046); every other weapon's
    /// reload sound is its owner's client's alone (WeaponLogic.cs:766-768).
    /// <c>WeaponKnowledgeTests</c> holds this list to the behaviours' IL.
    /// </summary>
    public static bool AudibleReload(string behaviour)
        => behaviour is Hl2mpWeaponBehaviours.Shotgun or Hl2mpWeaponBehaviours.Smg1 or Hl2mpWeaponBehaviours.Rpg or Hl2mpWeaponBehaviours.Pistol;

    /// <summary>The activity a view model's reload is tagged with.</summary>
    public const string ReloadActivity = "ACT_VM_RELOAD";

    public static WeaponKnowledge Build(WeaponCatalog weapons, Func<WeaponType, float> reloadSeconds)
    {
        var all = new List<WeaponFacts>();
        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponType w = weapons.ByIndex(i)!.Value;
            all.Add(new WeaponFacts(i, w.Name, w.ClipSize, w.Interval, reloadSeconds(w),
                                    AudibleReload(w.Behaviour) && w.ReloadSound.Length > 0,
                                    w.Behaviour == Hl2mpWeaponBehaviours.Shotgun,
                                    w.Projectile.Length > 0 ? w.LaunchSpeed : 0f));
        }

        return new WeaponKnowledge(all);
    }

    /// <summary>The reload animation's length on the weapon's view model, or 0 when it has none.</summary>
    public static float ReloadSeconds(ModelDescriptors models, in WeaponType w)
    {
        if (w.ViewModel.Length == 0)
            return 0f;
        ModelDescriptor vm = models.ForModel(w.ViewModel);
        int seq = vm.LookupSequenceByActivity(ReloadActivity);
        return seq < 0 ? 0f : vm.SequenceDuration(seq);
    }
}

/// <summary>What the map knows before play (§5.0), from the entity lump the navigation read.</summary>
public static class MapKnowledgeBuilder
{
    public static ItemKind KindOf(string className) => className switch
    {
        _ when className.StartsWith("weapon_", StringComparison.Ordinal) => ItemKind.Weapon,
        "item_healthkit" or "item_healthvial" => ItemKind.Health,
        "item_battery" => ItemKind.Armour,
        "item_suitcharger" or "item_healthcharger" => ItemKind.Charger,
        _ when className.StartsWith("item_ammo_", StringComparison.Ordinal) || className.StartsWith("item_box_", StringComparison.Ordinal)
            || className == "item_rpg_round" => ItemKind.Ammo,
        _ => ItemKind.Other,
    };

    /// <summary>
    /// Every weapon and item the lump places, in lump order, with the game's
    /// respawn rule for it (a weapon <paramref name="weaponRespawn"/>, an item
    /// <paramref name="itemRespawn"/>, a charger none), and the deathmatch spawns.
    /// </summary>
    public static MapKnowledge Build(string map, MapEntities lump, WeaponCatalog weapons, WeaponKnowledge weaponFacts,
                                     float weaponRespawn, float itemRespawn)
    {
        var items = ImmutableArray.CreateBuilder<ItemSpawn>();
        foreach (MapEntity e in lump.All)
        {
            if (!e.ClassName.StartsWith("weapon_", StringComparison.Ordinal) && !e.ClassName.StartsWith("item_", StringComparison.Ordinal))
                continue;
            ItemKind kind = KindOf(e.ClassName);
            float respawn = kind switch { ItemKind.Weapon => weaponRespawn, ItemKind.Charger => 0f, _ => itemRespawn };
            items.Add(new ItemSpawn(items.Count, e.ClassName, kind, e.Origin, respawn, kind == ItemKind.Weapon ? weapons.IndexOf(e.ClassName) : -1));
        }

        return new MapKnowledge(map, items.ToImmutable(), [.. lump.Spawns.Select(s => s.Origin)], weaponFacts);
    }
}
