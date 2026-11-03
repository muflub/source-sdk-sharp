//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

namespace TF2.Server.Combat;

/// <summary>One bleed tick the think asked for.</summary>
/// <param name="Attacker">The bleed's attacker (<c>hBleedingAttacker</c>).</param>
/// <param name="Weapon">The bleed's weapon id (<c>hBleedingWeapon</c>).</param>
/// <param name="WeaponName">The weapon's name, for the kill feed.</param>
/// <param name="Damage">The tick's damage (<c>nBleedDmg</c>).</param>
/// <param name="Custom">The tick's custom kind (<c>nDmgType</c>).</param>
public readonly record struct TFBleedTick(EntityHandle Attacker, TFWeaponId Weapon, string WeaponName,
                                          int Damage, TFDamageCustom Custom);

/// <summary>
/// One player's bleeds: <c>CTFPlayerShared::MakeBleed</c> and
/// <c>StopBleed</c> (<c>shared/tf/tf_player_shared.cpp:6782-6851</c>) and the
/// bleeding arm of <c>ConditionGameRulesThink</c> (:2682-2690, :3035-3066),
/// the server's. The state that is not a condition — the per-bleed entries —
/// is this class's own, as <c>m_PlayerBleeds</c> is stock's; the condition
/// rides the carrier (<c>TF_COND_BLEEDING</c> is added permanent, as stock's
/// <c>AddCond( TF_COND_BLEEDING, -1.f, pPlayer )</c> does, and its countdown
/// leaves it be).
/// </summary>
public sealed class TFBleedState
{
    /// <summary>One <c>bleed_struct_t</c> (<c>tf_player_shared.h:1041-1050</c>).</summary>
    public sealed class Bleed
    {
        public EntityHandle Attacker;
        public TFWeaponId Weapon;
        public string WeaponName = "";

        /// <summary>When the next tick is due (<c>flBleedingTime</c>).</summary>
        public float TickTime;

        /// <summary>When the bleed ends (<c>flBleedingRemoveTime</c>).</summary>
        public float RemoveTime;

        public int Damage = TFCombatDefs.BleedingDamage;
        public bool Permanent;
        public TFDamageCustom Custom = TFDamageCustom.Bleeding;
    }

    private readonly List<Bleed> _bleeds = new();

    /// <summary>The open bleeds, oldest first.</summary>
    public IReadOnlyList<Bleed> Bleeds => _bleeds;

    /// <summary>
    /// <c>MakeBleed</c>: the dead do not bleed; the same attacker and weapon
    /// extend their bleed's time (:6798-6810); a new source is appended and,
    /// when the victim was not bleeding yet, <c>TF_COND_BLEEDING</c> goes on
    /// the carrier. Stock seeds the first tick's clock with the raw duration
    /// (<c>flBleedingTime</c> holds the caller's seconds, :6813-6822), so the
    /// first tick lands once the server clock passes it — on a live server,
    /// at once. Answers the conditions as they are afterwards.
    /// </summary>
    public TFCondMask Make(float now, bool alive, TFCondMask conditions, EntityHandle attacker,
                           TFWeaponId weapon, string weaponName, float bleedingTime,
                           int bleedDamage = TFCombatDefs.BleedingDamage, bool permanent = false,
                           TFDamageCustom custom = TFDamageCustom.Bleeding)
    {
        if (!alive)
            return conditions;

        float expire = now + bleedingTime;
        foreach (var bleed in _bleeds)
        {
            if (bleed.Attacker == attacker && bleed.Weapon == weapon && expire > bleed.RemoveTime)
            {
                bleed.RemoveTime = expire;
                return conditions;
            }
        }

        _bleeds.Add(new Bleed
        {
            Attacker = attacker,
            Weapon = weapon,
            WeaponName = weaponName,
            TickTime = bleedingTime,
            RemoveTime = expire,
            Damage = bleedDamage,
            Permanent = permanent,
            Custom = custom,
        });

        if (!conditions.Has(TFCond.Bleeding))
            conditions.Set(TFCond.Bleeding);
        return conditions;
    }

    /// <summary><c>StopBleed</c>: the named attacker and weapon's bleeds end.</summary>
    public void Stop(EntityHandle attacker, TFWeaponId weapon)
    {
        _bleeds.RemoveAll(bleed => bleed.Attacker == attacker && bleed.Weapon == weapon);
    }

    /// <summary>
    /// The bleeding arm of <c>ConditionGameRulesThink</c> for one frame of
    /// <paramref name="frametime"/>: healers wear the bleeds' ends in twice as
    /// fast (:2682-2690), an expired bleed leaves (:3044-3047), a due tick
    /// deals and reschedules <c>TF_BLEEDING_FREQUENCY</c> on (:3048-3053), and
    /// an empty list takes <c>TF_COND_BLEEDING</c> off (:3062-3065). Answers
    /// whether the last bleed ended; the ticks land in <paramref name="ticks"/>.
    /// </summary>
    public bool Think(float now, float frametime, TFCondMask conditions, List<TFBleedTick> ticks)
    {
        if (!conditions.Has(TFCond.Bleeding))
        {
            // OnRemoveBleeding's server half (:4153-4156): the entries go with the condition.
            _bleeds.Clear();
            return false;
        }

        if (conditions.Has(TFCond.HealthBuff))
        {
            foreach (var bleed in _bleeds)
                bleed.RemoveTime -= 2f * frametime;
        }

        for (int i = _bleeds.Count - 1; i >= 0; i--)
        {
            var bleed = _bleeds[i];
            if (now >= bleed.RemoveTime && !bleed.Permanent)
            {
                _bleeds.RemoveAt(i);
                continue;
            }

            if (now >= bleed.TickTime)
            {
                bleed.TickTime = now + TFCombatDefs.BleedingFrequency;
                ticks.Add(new TFBleedTick(bleed.Attacker, bleed.Weapon, bleed.WeaponName,
                                          bleed.Damage, bleed.Custom));
            }
        }

        return _bleeds.Count == 0;
    }
}

/// <summary>
/// One player's bleeding, as a slice of the per-player composite: the
/// <see cref="TFBleedState"/> ticked every frame, the way the afterburn slice
/// (<see cref="TFAfterburn"/>) ticks the burn. A TICK is real damage from the
/// bleed's attacker through the core's damage op, inside a
/// <see cref="TFDamageScopes"/> scope naming the bleed's custom kind and
/// weapon, as stock's <c>CTakeDamageInfo( hBleedingAttacker, ..., DMG_SLASH,
/// nDmgType )</c> is (:3052).
/// </summary>
public sealed class TFBleed : IPlayerComponent
{
    /// <summary>The tick's damage type (<c>DMG_SLASH</c>, :3052).</summary>
    public const DamageType TickDamageType = DamageType.Slash;

    private readonly ITFPlayerData _players;
    private readonly ITFPlayerConditions _conditions;
    private readonly IEnginePlayers _enginePlayers;
    private readonly IEngineEntities _entities;
    private readonly IEngineDamage _damage;
    private readonly TFDamageScopes _scopes;
    private readonly TFBleeds _registry;
    private readonly List<TFBleedTick> _ticks = new();

    private EntityHandle _player = EntityHandle.None;
    private int _index = -1;
    private float _lastTick = -1f;

    public TFBleed(ITFPlayerData players, ITFPlayerConditions conditions, IEnginePlayers enginePlayers,
                   IEngineEntities entities, IEngineDamage damage, TFDamageScopes scopes, TFBleeds registry)
    {
        _players = players;
        _conditions = conditions;
        _enginePlayers = enginePlayers;
        _entities = entities;
        _damage = damage;
        _scopes = scopes;
        _registry = registry;
    }

    /// <summary>The bleed state.</summary>
    public TFBleedState State { get; } = new();

    /// <summary>After the afterburn slice's 0, so the stable sort keeps stock's think order.</summary>
    public int Order => 1;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _index = player.EntryIndex;
        _registry.Enter(_index, this);
    }

    /// <summary><c>MakeBleed</c> against the carrier's conditions.</summary>
    public void Make(float now, bool alive, EntityHandle attacker, TFWeaponId weapon, string weaponName,
                     float bleedingTime, int bleedDamage = TFCombatDefs.BleedingDamage,
                     bool permanent = false, TFDamageCustom custom = TFDamageCustom.Bleeding)
    {
        if (_players.Predicted(_index) is not { } state)
            return;

        TFCondMask before = state.Conditions();
        TFCondMask after = State.Make(now, alive, before, attacker, weapon, weaponName,
                                      bleedingTime, bleedDamage, permanent, custom);
        foreach (TFCond cond in TFCondMask.Added(before, after))
            _conditions.Add(_player, cond, TFConditions.Permanent, attacker);
    }

    public void Tick(float now)
    {
        float frametime = _lastTick < 0f ? 0f : now - _lastTick;
        _lastTick = now;

        if (_players.Predicted(_index) is not { } state)
            return;

        TFCondMask conditions = state.Conditions();
        if (!conditions.Has(TFCond.Bleeding))
        {
            State.Think(now, frametime, conditions, _ticks);
            return;
        }

        if (!_enginePlayers.IsAlive(_player))
            return;

        _ticks.Clear();
        if (State.Think(now, frametime, conditions, _ticks))
        {
            _conditions.Remove(_player, TFCond.Bleeding);
            return;
        }

        foreach (var tick in _ticks)
        {
            EntityHandle attacker = tick.Attacker != EntityHandle.None && _entities.IsValid(tick.Attacker)
                ? tick.Attacker : EntityHandle.None;
            using (_scopes.Push(tick.Custom, tick.Weapon, tick.WeaponName))
                _damage.Apply(_player, attacker, tick.Damage, TickDamageType);
        }
    }

    public void Detach()
    {
        if (_index >= 0)
            _registry.Leave(_index, this);
        _index = -1;
        _player = EntityHandle.None;
    }
}

/// <summary>
/// The bleeding slices by player index, and their source in the per-player
/// composite: the damage layer arms a bleed through <see cref="Of"/>.
/// </summary>
[SideService(Side.Server)]
public sealed class TFBleeds(ITFPlayerData players, ITFPlayerConditions conditions, IEnginePlayers enginePlayers,
                             IEngineEntities entities, IEngineDamage damage, TFDamageScopes scopes)
    : IPlayerComponentSource, IOrderedHook
{
    /// <summary>Stage 2.7's band (+700..+799 of TF2's thousand), after the death notices' 704.</summary>
    public const int Order = 1_000_000 - 1_000 + 707;

    private readonly Dictionary<int, TFBleed> _byIndex = new();

    public int HookOrder => Order;

    public IEnumerable<IPlayerComponent> Create(EntityHandle player)
    {
        yield return new TFBleed(players, conditions, enginePlayers, entities, damage, scopes, this);
    }

    /// <summary>The bleeding slice of the player at <paramref name="playerIndex"/>, or null.</summary>
    public TFBleed? Of(int playerIndex) => _byIndex.GetValueOrDefault(playerIndex);

    internal void Enter(int playerIndex, TFBleed bleed) => _byIndex[playerIndex] = bleed;

    internal void Leave(int playerIndex, TFBleed bleed)
    {
        if (_byIndex.TryGetValue(playerIndex, out var held) && ReferenceEquals(held, bleed))
            _byIndex.Remove(playerIndex);
    }
}
