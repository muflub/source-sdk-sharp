using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Items;
using TF2.Server.Buildings;
using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;

namespace TF2.Descent.Server.Furniture;

//=============================================================================
// What the furniture asks of the rest of the server, each behind a seam of
// its own so its facts hold the real service without an engine: the
// engine's create, the heroes (who is alive where, their life and ammo, a
// refill, a stats recompute) and the world (the clock, a hit, TF2's blast,
// the robots near a point, a line of sight, a sound, an explosion's look).
//=============================================================================

/// <summary>Makes the furniture's entities, and counts the engine's.</summary>
public interface IFurnitureSpawner
{
    /// <summary>A <c>tf2_descent_furniture</c>, or null when the engine refuses one.</summary>
    FurnitureEntity? Make();

    /// <summary>How many edicts the engine holds now.</summary>
    int Edicts();
}

/// <summary>The engine's create, spawn and directory, and its edict count.</summary>
[SideService(Side.Server, As = [typeof(IFurnitureSpawner)])]
public sealed class EngineFurnitureSpawner(IEngineEntities entities, IEntityDirectory directory) : IFurnitureSpawner
{
    public FurnitureEntity? Make()
    {
        var h = entities.Create(Shared.FurnitureNetState.ClassName);
        if (h == EntityHandle.None)
            return null;

        entities.Spawn(h);
        if (directory.Get(h) is FurnitureEntity made)
            return made;

        entities.Remove(h);
        return null;
    }

    public int Edicts() => entities.Count();
}

/// <summary>A living hero: his client slot, the ledger's name for him, his player, his feet and his middle.</summary>
public sealed record FurnitureHero(int Slot, string Key, EntityHandle Player, Vector Origin, Vector Centre);

/// <summary>The heroes as the furniture needs them.</summary>
public interface IFurnitureHeroes
{
    /// <summary>Every living hero with a sheet and a ledger name, by slot.</summary>
    IReadOnlyList<FurnitureHero> Living();

    /// <summary>The living hero <paramref name="player"/> is; null for anything else.</summary>
    FurnitureHero? Of(EntityHandle player);

    /// <summary>The ledger's name for the hero in <paramref name="slot"/>, living or not; null for none.</summary>
    string? KeyOf(int slot);

    /// <summary>The spells the hero in <paramref name="slot"/> knows (his sheet's spell ranks above 0).</summary>
    IReadOnlyCollection<string> SpellsKnown(int slot);

    /// <summary>Life and mana to full (a shrine's refill).</summary>
    void Refill(int slot);

    /// <summary>The hero's stats again: what he carries from a shrine changed.</summary>
    void Recompute(int slot);

    /// <summary>The hero's life and its maximum.</summary>
    (int Life, int Max) Life(EntityHandle player);

    /// <summary>Whole points of life to the hero, up to his maximum; what he took.</summary>
    int Heal(EntityHandle player, int amount);

    /// <summary>The primary and secondary ammo the hero lacks of his class's maximum.</summary>
    int MissingAmmo(EntityHandle player);

    /// <summary>Ammo to the hero, primary first, then secondary, each up to its maximum; what he took.</summary>
    int GiveAmmo(EntityHandle player, int count);
}

/// <summary>The heroes, their keys and TF2's resupply and ammo purse.</summary>
[SideService(Side.Server, As = [typeof(IFurnitureHeroes)])]
public sealed class EngineFurnitureHeroes(Heroes heroes, HeroKeys keys, IEngineEntities entities, IEnginePlayers players,
                                          ITFPlayerResupply resupply, ITFAmmoPurse purse) : IFurnitureHeroes
{
    public IReadOnlyList<FurnitureHero> Living()
    {
        var living = new List<FurnitureHero>();
        foreach (var hero in heroes.All.OrderBy(h => h.PlayerIndex))
            if (Make(hero) is { } h)
                living.Add(h);
        return living;
    }

    public FurnitureHero? Of(EntityHandle player) =>
        player != EntityHandle.None && entities.IsValid(player) && entities.IsPlayer(player) && heroes.Of(entities.GetIndex(player)) is { } hero
            ? Make(hero)
            : null;

    private FurnitureHero? Make(HeroComponent hero)
    {
        if (hero.Sheet is null || hero.Player == EntityHandle.None || !players.IsAlive(hero.Player) || keys.Of(hero.PlayerIndex) is not { } key)
            return null;
        return new FurnitureHero(hero.PlayerIndex, key, hero.Player, entities.GetOrigin(hero.Player), entities.GetWorldSpaceCenter(hero.Player));
    }

    public string? KeyOf(int slot) => keys.Of(slot);

    public IReadOnlyCollection<string> SpellsKnown(int slot) =>
        heroes.Of(slot)?.Sheet is { } sheet ? [.. sheet.SpellRanks.Where(p => p.Value > 0).Select(p => p.Key)] : [];

    public void Refill(int slot) => heroes.Of(slot)?.RefillLifeAndMana();

    public void Recompute(int slot) => heroes.Of(slot)?.Recompute();

    public (int Life, int Max) Life(EntityHandle player) => (entities.GetHealth(player), resupply.MaxHealth(player));

    public int Heal(EntityHandle player, int amount) => amount > 0 ? resupply.TakeHealth(player, amount) : 0;

    public int MissingAmmo(EntityHandle player)
    {
        var pools = purse.Pools(player);
        int missing = 0;
        foreach (var ammo in (ReadOnlySpan<TFAmmo>)[TFAmmo.Primary, TFAmmo.Secondary])
            missing += Math.Max(0, resupply.MaxAmmo(player, ammo) - pools.GetValueOrDefault(TFAmmoNames.Name(ammo)));
        return missing;
    }

    public int GiveAmmo(EntityHandle player, int count)
    {
        int given = 0;
        foreach (var ammo in (ReadOnlySpan<TFAmmo>)[TFAmmo.Primary, TFAmmo.Secondary])
            if (count - given > 0)
                given += resupply.GiveAmmo(player, count - given, ammo);
        return given;
    }
}

/// <summary>The world as the furniture acts on it.</summary>
public interface IFurnitureWorld
{
    /// <summary>The server's clock, in seconds.</summary>
    double Now { get; }

    /// <summary>One hit through the damage rules (a hero's resistances apply; a robot body's rules take it).</summary>
    void Hurt(EntityHandle victim, EntityHandle attacker, float damage, DamageType bits, Vector at);

    /// <summary>
    /// TF2's blast (its falloff, its pushes) from <paramref name="inflictor"/>, credited to
    /// <paramref name="attacker"/>, hurting what is not a player: heroes are the furniture's own
    /// pass (<see cref="FurnitureBlastRule"/> refuses them in this one). <paramref name="ignored"/>
    /// is skipped.
    /// </summary>
    void Blast(Vector origin, float damage, float radius, EntityHandle attacker, EntityHandle inflictor, EntityHandle ignored);

    /// <summary>The robot bodies (entities that run the damage rules and are no player, no furniture) within <paramref name="radius"/>.</summary>
    IReadOnlyList<EntityHandle> RobotsNear(Vector origin, float radius);

    /// <summary>Whether a line from <paramref name="from"/> to <paramref name="to"/> crosses no world brush.</summary>
    bool Clear(Vector from, Vector to);

    /// <summary>A script sound from <paramref name="source"/>, through the server's emitter (the horde's ear hears it).</summary>
    void Sound(EntityHandle source, string name);

    /// <summary>An explosion's look at <paramref name="origin"/>, sized to <paramref name="radius"/>.</summary>
    void Explosion(Vector origin, float radius);
}

/// <summary>The engine's clock, damage, TF2's blast applier, the world's queries, sounds and effects.</summary>
[SideService(Side.Server, As = [typeof(IFurnitureWorld)])]
public sealed class EngineFurnitureWorld(IHordeClock clock, IEngineDamage damage, TFBlastApplier blasts, TFDamageScopes scopes,
                                         FurnitureBlastScope scope, IEngineEntities entities, IEngineWorldQuery world,
                                         IEntityDirectory directory, IEngineSound sound, IEngineEffects effects, IEnginePrecache precache)
    : IFurnitureWorld
{
    /// <summary>The weapon name the furniture's hits carry (kill feeds, logs).</summary>
    public const string WeaponName = "tf2_descent_furniture";

    /// <summary>The explosion sprite, as TF2's taunt grenade draws one.</summary>
    public const string ExplosionSprite = "sprites/zerogxplode.vmt";

    private readonly EntityHandle[] _near = new EntityHandle[256];

    public double Now => clock.TicksPerSecond > 0 ? clock.Tick / clock.TicksPerSecond : 0;

    public void Hurt(EntityHandle victim, EntityHandle attacker, float amount, DamageType bits, Vector at)
    {
        using var named = scopes.Push(TFDamageCustom.None, TFWeaponId.None, WeaponName);
        damage.ApplyAt(victim, attacker, amount, bits, default, at);
    }

    public void Blast(Vector origin, float amount, float radius, EntityHandle attacker, EntityHandle inflictor, EntityHandle ignored)
    {
        scope.Enter();
        try
        {
            blasts.Apply(origin, TFBlast.Of(amount, radius, DamageType.Blast), attacker, inflictor, ignored,
                         TFWeaponId.None, WeaponName, critical: false);
        }
        finally
        {
            scope.Leave();
        }
    }

    public IReadOnlyList<EntityHandle> RobotsNear(Vector origin, float radius)
    {
        int count = world.EntitiesInSphere(origin, radius, _near);
        var found = new List<EntityHandle>(count);
        for (int i = 0; i < count; i++)
        {
            EntityHandle e = _near[i];
            if (!entities.IsPlayer(e) && directory.Get(e) is RobotBody { Parked: false } body && body.EngineHealth > 0)
                found.Add(e);
        }

        return found;
    }

    public bool Clear(Vector from, Vector to) => !world.TraceLine(from, to, TraceMask.Brushes).Hit;

    public void Sound(EntityHandle source, string name) => sound.EntityEmitSound(source, name);

    public void Explosion(Vector origin, float radius) =>
        effects.ExplosionEffect(origin, precache.PrecacheModel(ExplosionSprite), radius * 0.03f, 25, 0, (int)radius, (int)radius);
}

/// <summary>
/// HAZARDS FOR THE ROBOTS (plan_tf2_rpg B.3: <c>HazardArmed / HazardGone</c>, barrels and
/// traps, so a brain knows what it can use and what to stand clear of): told when a room's
/// explosives and traps stand (its room woke, or the level began with it awake), when one is
/// gone (an explosive went off, a trap is spent) and when a room's go to sleep. The horde's
/// senses join this chain when its brains read hazards (HO5); nothing in Descent does yet.
/// </summary>
public interface IFurnitureHazardHook
{
    /// <summary>A hazard stands at <paramref name="at"/>: an explosive, or an armed trap.</summary>
    void HazardArmed(int furniture, Shared.FurnitureKindId kind, string variant, Vector at, float radius);

    /// <summary>The hazard is gone: it went off for good, or its room slept.</summary>
    void HazardGone(int furniture);
}

/// <summary>The hazard chain (<see cref="IFurnitureHazardHook"/>), which only a registrar can name.</summary>
[SideService(Side.Server)]
public sealed class FurnitureRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<IFurnitureHazardHook>();
}
