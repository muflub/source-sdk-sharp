using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Server.Mode;
using TF2.Server.Player;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// Life to a hero's TF health, in whole points (HE3's sustain): a fraction
/// owed is kept until it makes a point, never above the player's TF maximum
/// (no overheal), and only while the hero is alive. A hero already at the
/// maximum banks nothing.
/// </summary>
public sealed class HeroLife(IEngineEntities entities, IEnginePlayers players, ITFPlayerResupply resupply)
{
    private readonly Dictionary<int, double> _owed = new();

    /// <summary>Adds <paramref name="amount"/> life to <paramref name="player"/>; answers the whole points healed.</summary>
    public int Heal(EntityHandle player, int playerIndex, double amount)
    {
        if (!(amount > 0) || !players.IsAlive(player))
            return 0;

        int max = resupply.MaxHealth(player);
        int health = entities.GetHealth(player);
        if (health >= max)
        {
            _owed.Remove(playerIndex);
            return 0;
        }

        double owed = _owed.GetValueOrDefault(playerIndex) + amount;
        int whole = (int)Math.Min(Math.Floor(owed), max - health);
        _owed[playerIndex] = owed - Math.Floor(owed);
        if (whole <= 0)
            return 0;

        entities.SetHealth(player, health + whole);
        return whole;
    }

    /// <summary>Forgets what <paramref name="playerIndex"/> was owed.</summary>
    public void Forget(int playerIndex) => _owed.Remove(playerIndex);
}

/// <summary>The life a hero's hits and regeneration owe, one keeper for the side.</summary>
[SideService(Side.Server)]
public sealed class HeroLifeKeeper(IEngineEntities entities, IEnginePlayers players, ITFPlayerResupply resupply)
{
    /// <summary>What a hero's hits return (leech).</summary>
    public HeroLife Leech { get; } = new(entities, players, resupply);

    /// <summary>What a hero regenerates over time.</summary>
    public HeroLife Regen { get; } = new(entities, players, resupply);
}

/// <summary>
/// Life and mana from damage dealt (HE3): each damaging hit a hero lands on
/// another player, or on an entity that runs the damage rules, returns
/// <see cref="HeroStats.LifeFromHit"/> as TF health and
/// <see cref="HeroStats.ManaFromHit"/> as mana, from the damage the victim
/// actually TOOK (after its resistances; nothing for a hit it took nothing
/// of) and no more than the life it had (no overkill), each under the rules'
/// cap. A hero's own blast on himself returns nothing.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroLeech(IDescentModeState mode, Heroes heroes, IEngineEntities entities, HeroLifeKeeper life,
                              HeroDebugDamageConVar debug, ILog log)
    : IPlayerHurtHook, IEntityHurtHook, IOrderedHook
{
    /// <summary>In TF2 Descent's stats block.</summary>
    public const int Order = ModStages.First + 300;

    public int HookOrder => Order;

    public void OnPlayerHurt(in PlayerHurt hurt) => Leech(hurt.Player, hurt.Attacker, hurt.Taken);

    public void OnEntityHurt(in EntityHurt hurt) => Leech(hurt.Victim, hurt.Attacker, hurt.Taken);

    private void Leech(EntityHandle victim, EntityHandle attacker, int taken)
    {
        if (!mode.Active || taken <= 0 || attacker == EntityHandle.None || attacker == victim || !entities.IsPlayer(attacker))
            return;

        int index = entities.GetIndex(attacker);
        if (heroes.Of(index) is not { Stats: { } stats } hero)
            return;

        // The hooks run before the victim's health takes the hit (the shell
        // subtracts the answer after), so this is the life it had.
        double lifeBefore = entities.GetHealth(victim);
        double mana = stats.ManaFromHit(taken, lifeBefore);
        if (mana > 0)
            hero.SetMana(hero.Mana + mana);

        int healed = life.Leech.Heal(attacker, index, stats.LifeFromHit(taken, lifeBefore));
        if (debug.Value)
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"[TF2Descent/Leech] #{index} hit #{entities.GetIndex(victim)} for {taken} of {lifeBefore}: +{healed} life, +{mana:0.##} mana\n"));
    }
}

/// <summary>
/// Life regenerated over time (HE3): each tick, a hero's
/// <see cref="HeroStats.LifeRegenPerSecond"/> times the tick's length as TF
/// health, never above the TF maximum. TF's own regeneration (the Medic's,
/// an item's) is TF's and untouched.
/// </summary>
public sealed class HeroRegen(Heroes heroes, HeroLifeKeeper life) : IPlayerComponent
{
    /// <summary>After the hero's own component (<see cref="HeroComponent.ComponentOrder"/>).</summary>
    public const int ComponentOrder = ModStages.First + 210;

    private EntityHandle _player = EntityHandle.None;
    private int _index = -1;
    private float? _last;

    public int Order => ComponentOrder;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _index = player.EntryIndex;
        _last = null;
    }

    public void Tick(float now)
    {
        float dt = _last is { } last && now > last ? now - last : 0f;
        _last = now;
        if (_index >= 0 && dt > 0f && heroes.Of(_index)?.Stats is { LifeRegenPerSecond: > 0 } stats)
            life.Regen.Heal(_player, _index, stats.LifeRegenPerSecond * dt);
    }

    public void Detach()
    {
        if (_index >= 0)
        {
            life.Regen.Forget(_index);
            life.Leech.Forget(_index);
        }

        _index = -1;
        _player = EntityHandle.None;
    }
}

/// <summary>A <see cref="HeroRegen"/> for each hero (the Descent mode is on and the player is no bot).</summary>
[SideService(Side.Server)]
public sealed class HeroRegens(IDescentModeState mode, IBotPlayers bots, Heroes heroes, HeroLifeKeeper life)
    : IPlayerComponentSource, IOrderedHook
{
    /// <summary>After TF2's band, in TF2 Descent's stats block.</summary>
    public const int Order = ModStages.First + 300;

    public int HookOrder => Order;

    public IEnumerable<IPlayerComponent> Create(EntityHandle player)
    {
        if (!mode.Active || bots.IsBot(player))
            yield break;

        yield return new HeroRegen(heroes, life);
    }
}
