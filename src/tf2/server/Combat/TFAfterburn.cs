using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Medigun;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Server.Combat;

/// <summary>
/// One player's afterburn, as a slice of the per-player composite (Stage 2.7):
/// a <see cref="TFBurn"/> ticked every frame against the player's carrier.
///
/// <para>
/// THE CONDITIONS ARE THE PLAYER'S. <c>TF_COND_BURNING</c> and
/// <c>TF_COND_HEALING_DEBUFF</c> are added and removed through the player's
/// one condition writer (<see cref="ITFPlayerConditions"/>): the burn
/// permanently, as stock's <c>AddCond( TF_COND_BURNING, -1.f, pAttacker )</c>
/// adds it, and the debuff for the weapon's initial afterburn
/// (<c>tf_player_shared.cpp:6650-6653</c>), so the player's per-frame
/// countdown leaves the fire be; they are read from the
/// carrier (<see cref="ITFPlayerData"/>). The burn state that is not a
/// condition (the next tick, the duration, the burn's attacker and weapon) is
/// this slice's own, as it is <c>CTFPlayerShared</c>'s in stock. A burn that
/// something else puts out (the bit gone) is forgotten here.
/// </para>
///
/// <para>
/// A TICK is real damage from the burn's attacker through the core's damage
/// op (<see cref="IEngineDamage.Apply"/>), inside a <see cref="TFDamageScopes"/>
/// scope naming <c>TF_DMG_CUSTOM_BURNING</c> and the burn's weapon, so the
/// damage layer, the kill feed and the rules see it as stock's
/// <c>CTakeDamageInfo( m_hBurnAttacker, ..., TF_DMG_CUSTOM_BURNING )</c>.
/// </para>
/// </summary>
public sealed class TFAfterburn : IPlayerComponent
{
    private readonly ITFPlayerData _players;
    private readonly ITFPlayerConditions _conditions;
    private readonly IEnginePlayers _enginePlayers;
    private readonly IEngineEntities _entities;
    private readonly IEngineDamage _damage;
    private readonly IEntityDirectory _directory;
    private readonly TFDamageScopes _scopes;
    private readonly TFAfterburns _registry;
    private readonly ITFPlayerHealing _healing;
    private readonly ITFWeaponOwners _owners;
    private readonly AttributeHooks _hooks;
    private readonly TFPlayerCarriers _carriers;

    private EntityHandle _player = EntityHandle.None;
    private int _index = -1;
    private float _lastTick = -1f;
    private bool _wasBurning;

    public TFAfterburn(ITFPlayerData players, ITFPlayerConditions conditions, IEnginePlayers enginePlayers,
                       IEngineEntities entities, IEngineDamage damage, IEntityDirectory directory, TFDamageScopes scopes,
                       TFAfterburns registry, ITFPlayerHealing healing, ITFWeaponOwners owners, AttributeHooks hooks,
                       TFPlayerCarriers carriers)
    {
        _players = players;
        _conditions = conditions;
        _enginePlayers = enginePlayers;
        _entities = entities;
        _damage = damage;
        _directory = directory;
        _scopes = scopes;
        _registry = registry;
        _healing = healing;
        _owners = owners;
        _hooks = hooks;
        _carriers = carriers;
    }

    /// <summary>The burn state.</summary>
    public TFBurn Burn { get; } = new();

    public int Order => 0;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _index = player.EntryIndex;
        _registry.Enter(_index, this);
    }

    /// <summary>
    /// <c>Burn</c>: sets the player alight from <paramref name="attacker"/>'s
    /// <paramref name="weapon"/>, on the carrier's conditions.
    /// </summary>
    public void Ignite(float now, EntityHandle attacker, TFWeaponId weapon, string weaponName, float burningTime = -1f)
    {
        if (_players.Predicted(_index) is not { } state)
            return;

        TFCondMask before = state.Conditions();
        TFCondMask after = Burn.Ignite(now, _enginePlayers.IsAlive(_player), state.Class(), before,
                                       attacker, weapon, weaponName, burningTime, _hooks,
                                       // The immune arm's walks, stock's three in its order
                                       // (tf_player_shared.cpp:6672-6720).
                                       ImmunitySources(state),
                                       attacker != EntityHandle.None ? _owners.ItemOf(attacker.EntryIndex, weaponName) : null);
        foreach (TFCond cond in TFCondMask.Added(before, after))
            _conditions.Add(_player, cond,
                // The debuff's time is the weapon's initial afterburn, taken
                // before the rate-on-hit lands (tf_player_shared.cpp:6650-6653);
                // a 0 means stock's next condition think takes it off.
                cond == TFCond.HealingDebuff ? TFBurn.InitialAfterburnDuration(weapon) : TFConditions.Permanent,
                attacker);
        foreach (TFCond cond in TFCondMask.Removed(before, after))
            _conditions.Remove(_player, cond);

        // OnAddBurning's clock (tf_player_shared.cpp:7306): the burn ramp starts
        // when TF_COND_BURNING arrives, so a re-ignite while already burning
        // keeps the earlier start (OnRemoveBurning never zeroes it).
        if (!before.Has(TFCond.Burning) && after.Has(TFCond.Burning))
            state.BurnEffectStartTime = now;

        _wasBurning = after.Has(TFCond.Burning);
    }

    /// <summary>
    /// The immune arm's sources, stock's walks in its order
    /// (<c>tf_player_shared.cpp:6672-6720</c>): the active weapon's own — a
    /// holstered weapon's attribute is not consulted — then, for a sniper,
    /// every worn item. The demo shield's arm waits for shield items
    /// (<c>docs/plans/tf2/deferred.tsv</c>).
    /// </summary>
    private List<IAttributeProvider> ImmunitySources(TFPlayerNetState state)
    {
        var sources = new List<IAttributeProvider>();
        if (_owners.ItemOf(_index, _enginePlayers.GetActiveWeapon(_player)) is { } active)
            sources.Add(active);

        if (state.Class() == TFClass.Sniper && _carriers.Part<TFWearables>(_index) is { } worn)
            foreach (EntityHandle wornItem in worn.Wearables)
                if (_directory.Get(wornItem) is TFWearableEntity { Item: { } item })
                    sources.Add(item);

        return sources;
    }

    public void Tick(float now)
    {
        float frametime = _lastTick < 0f ? 0f : now - _lastTick;
        _lastTick = now;

        if (_players.Predicted(_index) is not { } state)
            return;

        TFCondMask conditions = state.Conditions();
        if (!conditions.Has(TFCond.Burning))
        {
            if (_wasBurning)
                Burn.Extinguished();
            _wasBurning = false;
            return;
        }

        _wasBurning = true;
        if (!_enginePlayers.IsAlive(_player))
            return;

        var think = Burn.Think(now, frametime, state.Class(), conditions, WaterLevel(), _hooks,
                               // The burn weapon's item, the tick arm's walk
                               // (tf_player_shared.cpp:2898); the attacker gone, none.
                               Burn.Attacker != EntityHandle.None
                                   ? _owners.ItemOf(Burn.Attacker.EntryIndex, Burn.WeaponName)
                                   : null);
        if (think.Extinguish)
        {
            _conditions.Remove(_player, TFCond.Burning);
            _conditions.Remove(_player, TFCond.HealingDebuff);
            // The burning think's HEALTH_BUFF arm (tf_player_shared.cpp:2793): a
            // healer keeping the patient up hears the fire went out, once per healer.
            _healing.Extinguished(_player);
            Burn.Extinguished();
            _wasBurning = false;
            return;
        }

        if (think.TickDamage > 0f)
        {
            EntityHandle attacker = Burn.Attacker != EntityHandle.None && _entities.IsValid(Burn.Attacker)
                ? Burn.Attacker : EntityHandle.None;
            using (_scopes.Push(think.Custom, Burn.Weapon, Burn.WeaponName))
                _damage.Apply(_player, attacker, think.TickDamage, TFBurn.TickDamageType);
        }
    }

    public void Detach()
    {
        if (_index >= 0)
            _registry.Leave(_index, this);
        _index = -1;
        _player = EntityHandle.None;
    }

    /// <summary><c>GetWaterLevel</c>: the player's movement state's, 0 for a player with none.</summary>
    private int WaterLevel() => _directory.Get(_player) is SsPlayer player ? player.State.WaterLevel : 0;
}

/// <summary>
/// The afterburn slices by player index, and their source in the per-player
/// composite (<see cref="IPlayerComponentSource"/>): the damage layer ignites
/// a victim through <see cref="Of"/>.
/// </summary>
[SideService(Side.Server)]
public sealed class TFAfterburns(ITFPlayerData players, ITFPlayerConditions conditions, IEnginePlayers enginePlayers,
                                 IEngineEntities entities, IEngineDamage damage, IEntityDirectory directory,
                                 TFDamageScopes scopes, ITFPlayerHealing healing, ITFWeaponOwners owners,
                                 AttributeHooks hooks, TFPlayerCarriers carriers)
    : IPlayerComponentSource, IOrderedHook
{
    /// <summary>Stage 2.7's band (+700..+799 of TF2's thousand).</summary>
    public const int Order = 1_000_000 - 1_000 + 700;

    private readonly Dictionary<int, TFAfterburn> _byIndex = new();

    public int HookOrder => Order;

    public IEnumerable<IPlayerComponent> Create(EntityHandle player)
    {
        yield return new TFAfterburn(players, conditions, enginePlayers, entities, damage, directory, scopes,
                                     this, healing, owners, hooks, carriers);
    }

    /// <summary>The afterburn of the player at <paramref name="playerIndex"/>, or null.</summary>
    public TFAfterburn? Of(int playerIndex) => _byIndex.GetValueOrDefault(playerIndex);

    internal void Enter(int playerIndex, TFAfterburn afterburn) => _byIndex[playerIndex] = afterburn;

    internal void Leave(int playerIndex, TFAfterburn afterburn)
    {
        if (_byIndex.TryGetValue(playerIndex, out var held) && ReferenceEquals(held, afterburn))
            _byIndex.Remove(playerIndex);
    }
}
