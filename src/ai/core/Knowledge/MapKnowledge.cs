using System.Collections.Immutable;

namespace SourceSharp.Ai;

/// <summary>What a weapon's second trigger does, as far as a bot fights with it (§5.4, ruling 2026-11-03).</summary>
public enum AltFireKind : byte
{
    /// <summary>Nothing that hurts anyone: no second trigger, a pistol's or a .357's; a crossbow's scope; an RPG's laser.</summary>
    None = 0,

    /// <summary>A contact grenade on an arc: it goes off on what it first touches, and its blast is the bot's own problem too.</summary>
    Lob,

    /// <summary>A flat ball that hurts what it touches and flies on: aimed as a direct hit.</summary>
    Ball,

    /// <summary>Both barrels: more shot, wider, from the clip.</summary>
    Double,

    /// <summary>A fused grenade, underhand: a lob standing, a roll along the floor ducked.</summary>
    Underhand,
}

/// <summary>A weapon's second trigger, from the classes it runs on (§5.4).</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Speed">How fast it leaves, 0 for shot.</param>
/// <param name="Gravity">What pulls it down, units/s²; 0 flies flat.</param>
/// <param name="BlastRadius">How far its blast reaches, the shooter too; 0 no blast.</param>
/// <param name="Damage">What it does at its best.</param>
/// <param name="AmmoCost">What a press costs.</param>
/// <param name="FromClip">It is paid for from the clip, not a pool of its own.</param>
public readonly record struct AltFireFacts(AltFireKind Kind, float Speed = 0f, float Gravity = 0f, float BlastRadius = 0f, float Damage = 0f, int AmmoCost = 1, bool FromClip = false);

/// <summary>
/// What a weapon kind is, as a player who knows the game knows it (plan_hl2mp_ai
/// §5.0, ruling 2026-11-01): its clip, how fast it fires, how long its reload
/// takes and whether others hear it, whether it reloads shell by shell, how
/// fast its projectile flies (0 for hitscan). The binding reads every number
/// from the classes the weapons run on.
/// </summary>
public readonly record struct WeaponFacts(int Kind, string Name, int ClipSize, float CadenceSeconds, float ReloadSeconds, bool ReloadAudible,
                                          bool ShellByShell, float ProjectileSpeed, AltFireFacts Alt = default)
{
    /// <summary>Whether the weapon has a clip to estimate (a melee weapon, a grenade or the RPG fires from its reserve).</summary>
    public bool HasClip => ClipSize > 0;
}

/// <summary>The game's weapon table as the core reads it: by the binding's weapon kind.</summary>
public sealed class WeaponKnowledge
{
    private readonly WeaponFacts?[] _byKind;

    public WeaponKnowledge(IEnumerable<WeaponFacts> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        All = [.. facts];
        int max = All.Length == 0 ? 0 : All.Max(f => f.Kind) + 1;
        _byKind = new WeaponFacts?[max];
        foreach (WeaponFacts f in All)
        {
            if (f.Kind < 0)
                throw new ArgumentException($"{f.Name} has no kind", nameof(facts));
            _byKind[f.Kind] = f;
        }
    }

    public ImmutableArray<WeaponFacts> All { get; }

    public bool TryGet(int kind, out WeaponFacts facts)
    {
        if ((uint)kind < (uint)_byKind.Length && _byKind[kind] is { } f)
        {
            facts = f;
            return true;
        }

        facts = default;
        return false;
    }
}

/// <summary>What a static item spawn holds.</summary>
public enum ItemKind : byte
{
    Other = 0,
    Weapon,
    Health,
    Armour,
    Ammo,
    Charger,
}

/// <summary>A static item spawn the map places (§5.0): where, what, and how long it takes to come back.</summary>
public readonly record struct ItemSpawn(int Index, string ClassName, ItemKind Kind, AiVector Position, float RespawnSeconds, int WeaponKind = -1);

/// <summary>
/// What every bot knows of a map before it plays (§5.0): the item spawns and
/// the player spawns from the entity lump, and the weapon table. Immutable,
/// shared by every bot of the map. Anything dynamic (an item there now, a
/// player anywhere) comes only through perception.
/// </summary>
public sealed class MapKnowledge(string map, ImmutableArray<ItemSpawn> items, ImmutableArray<AiVector> spawns, WeaponKnowledge weapons)
{
    public string Map { get; } = map;

    public ImmutableArray<ItemSpawn> Items { get; } = items;

    public ImmutableArray<AiVector> Spawns { get; } = spawns;

    public WeaponKnowledge Weapons { get; } = weapons;

    /// <summary>A map with nothing on it (a fact's, or a level without navigation).</summary>
    public static MapKnowledge Empty(WeaponKnowledge weapons) => new("", [], [], weapons);
}
