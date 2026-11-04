using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Server;
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
                                    w.Projectile.Length > 0 ? w.LaunchSpeed : 0f, AltOf(in w)));
        }

        return new WeaponKnowledge(all);
    }

    /// <summary>
    /// What a weapon's second trigger is (§5.4, ruling 2026-11-03), from its row and the classes it
    /// launches. Those with none that hurts: the crossbow's zooms (CheckZoomToggle), the RPG's and the
    /// pistol's do nothing (the laser comes on by itself), a .357, crowbar and stunstick have none, and
    /// the gravity gun's pulls: its primary is the punt.
    /// </summary>
    public static AltFireFacts AltOf(in WeaponType w)
    {
        if (w.Behaviour == Hl2mpWeaponBehaviours.Frag)
        {
            var frag = new GrenadeVars();
            return new AltFireFacts(AltFireKind.Underhand, FragLobSpeed, FragGravity, frag.Radius, frag.Magnitude);
        }

        if (w.Secondary is not { } alt)
            return default;
        if (alt.Projectile == EntityNames.GrenadeAr2)
        {
            var vars = new GrenadeAr2Vars();
            return new AltFireFacts(AltFireKind.Lob, alt.LaunchSpeed, GrenadeAr2.GravityUnits, vars.Radius, vars.Magnitude, alt.AmmoCost);
        }

        if (alt.Projectile == EntityNames.PropCombineBall)
            return new AltFireFacts(AltFireKind.Ball, alt.LaunchSpeed, 0f, 0f, new EnergyBallVars().Damage, alt.AmmoCost);
        if (alt.FromClip && alt.Pellets > 0)
            return new AltFireFacts(AltFireKind.Double, 0f, 0f, 0f, 0f, alt.AmmoCost, true);
        return default;
    }

    /// <summary>The underhand's forward speed (Arsenal.ThrowGrenade's Lob), and HL2MP's sv_gravity.</summary>
    public const float FragLobSpeed = 350f, FragGravity = 600f;

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

/// <summary>
/// Bot chat's read of what every bot knows before it plays (§5.0, §3.6): the session's map
/// knowledge, the nav's layout, and which weapons are the good ones: rank 6 or better on
/// <see cref="Trees.Hl2mpWeaponRanges"/>, what bots themselves rate a weapon by (the .357, the
/// crossbow, the RPG, the shotgun and the AR2 of ruling 2026-11-03). Nothing dynamic.
/// </summary>
[SideService(Side.Server, As = [typeof(Chat.IChatMapFacts)])]
public sealed class SessionMapFacts(Senses.Hl2mpAiSession ai, IHl2mpNavigation nav) : Chat.IChatMapFacts
{
    /// <summary>The rank from which a weapon is one a bot keeps vague about.</summary>
    public const int GoodRank = 6;

    private readonly HashSet<string> _good = new(StringComparer.Ordinal);

    public MapKnowledge? Map => ai.Session.World?.Map;

    public NavGraph? Graph => nav.Current?.Nav.Graph;

    public IReadOnlySet<string> GoodWeapons
    {
        get
        {
            _good.Clear();
            if (Map is { } map)
                _good.UnionWith(GoodOf(map.Weapons.All.Select(w => w.Name)));
            return _good;
        }
    }

    /// <summary>The good ones of <paramref name="weaponClasses"/>.</summary>
    public static IEnumerable<string> GoodOf(IEnumerable<string> weaponClasses)
        => weaponClasses.Where(w => Trees.Hl2mpWeaponRanges.For(w).Rank >= GoodRank);
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
