using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Econ;
using TF2.Server.Entities.Items;
using TF2.Server.Medigun;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// One player's <c>tf_player_data</c> carrier, as a component of the core's
/// per-player composite: made when the player is attached, removed when they
/// leave or the level ends. The carrier's owner is the player's entity index,
/// which is what the client matches its local carrier by. It enters
/// <see cref="TFPlayerCarriers"/> under that index for as long as it lives.
/// </summary>
public sealed class TFPlayerCarrier : IPlayerComponent
{
    private readonly IEngineEntities _entities;
    private readonly IEntityDirectory _directory;
    private readonly TFPlayerCarriers _carriers;
    private readonly ILog _log;

    private EntityHandle _carrier = EntityHandle.None;
    private int _index = -1;

    public TFPlayerCarrier(IEngineEntities entities, IEntityDirectory directory, TFPlayerCarriers carriers, ILog log)
    {
        _entities = entities;
        _directory = directory;
        _carriers = carriers;
        _log = log;
    }

    /// <summary>First in TF2's block: the other TF components read the carrier.</summary>
    public int Order => 0;

    /// <summary>The carrier entity, or <see cref="EntityHandle.None"/> before the attach and after the detach.</summary>
    public EntityHandle Carrier => _carrier;

    /// <summary>The carrier's state, or null when it has none.</summary>
    public TFPlayerNetState? State { get; private set; }

    /// <summary>
    /// The player's one condition set, its active words bound to
    /// <see cref="State"/>'s five (<see cref="TFCarrierCondWords"/>): what the
    /// weapons, the movement and <see cref="ITFPlayerConditions"/> all read
    /// and write. Null when the player has no carrier.
    /// </summary>
    public TFConditions? Conditions { get; private set; }

    public void Attach(EntityHandle player)
    {
        _carrier = _entities.Create(TFEntityNames.PlayerData);
        if (_carrier == EntityHandle.None)
        {
            _log.Warning($"[TF2/Player] no {TFEntityNames.PlayerData} could be made for {player}\n");
            return;
        }

        _entities.SetOrigin(_carrier, _entities.GetOrigin(player));
        _entities.Spawn(_carrier);

        if (_directory.Get(_carrier) is not TFPlayerDataEntity entity)
        {
            _log.Warning($"[TF2/Player] {_carrier} is not a {TFEntityNames.PlayerData}; the player has no TF state\n");
            return;
        }

        State = entity.State;
        Conditions = new TFConditions(authoritative: true, new TFCarrierCondWords(entity.State));
        _index = _entities.GetIndex(player);
        State.OwnerIndex = _index;
        _carriers.Enter(_index, this);
    }

    /// <summary>
    /// Does the carrier's entity still stand: both the directory's removal
    /// view and the binding's own. A round cleanup that removes the carrier
    /// under a staying player (the mode switch's <c>sv_mode null</c> before
    /// TF2 named it its own) fails both, and the registry must not answer
    /// through either half that says dead.
    /// </summary>
    internal bool Alive => State is { IsLive: true } && _directory.Get(_carrier) is TFPlayerDataEntity;

    /// <summary>
    /// The entity died under the attach (<see cref="Alive"/> said no). Say so
    /// once, leave the registry, and forget the binding: the dead state is
    /// never handed out again, and a dead entity has nothing to remove. The
    /// player keeps their composite; a class read answers no-carrier until a
    /// re-attach makes a new carrier.
    /// </summary>
    internal void Drop()
    {
        _log.Warning($"[TF2/Player] {TFEntityNames.PlayerData} {_carrier} died under player {_index}; the carrier is dropped\n");
        if (_index >= 0)
            _carriers.Leave(_index, this);
        _index = -1;
        State = null;
        Conditions = null;
        _carrier = EntityHandle.None;
    }

    public void Tick(float now) { }

    public void Detach()
    {
        if (_index >= 0)
            _carriers.Leave(_index, this);
        _index = -1;
        State = null;
        Conditions = null;
        if (_carrier != EntityHandle.None && _entities.IsValid(_carrier))
            _entities.Remove(_carrier);
        _carrier = EntityHandle.None;
    }
}

/// <summary>
/// TF2's per-player components (Stage 2.3's <c>TFPlayerComponents</c>), in
/// their order: the carrier (0), the class (10), the condition countdown (20),
/// the blast jump (25), the stun stack (30) and the respawn override (40).
/// Each is its own class; nothing here decides anything. The countdown takes
/// the medigun lane's healing (2.9) to report the frame's healer count; a
/// container without the lane passes none and the count is 2.3's none.
/// </summary>
[SideService(Side.Server)]
public sealed class TFPlayerComponents(IEngineEntities entities, IEntityDirectory directory,
                                       TFPlayerCarriers carriers, ILog log,
                                       TFPlayerItems items, TFPlayerConditions conditions,
                                       ITFMapPlayers players, IGameClock clock,
                                       IEngineNetworking networking, TF2.Shared.Econ.ItemSchema schema,
                                       IWeaponGiver arsenal,
                                       IEnginePlayers enginePlayers, IEngineGameEvents events,
                                       ITFPlayerHealing? healing = null)
    : IPlayerComponentSource, IOrderedHook
{
    public int HookOrder => TFPlayerOrders.Components;

    public IEnumerable<IPlayerComponent> Create(EntityHandle player)
    {
        yield return new TFPlayerCarrier(entities, directory, carriers, log);
        yield return new TFPlayerClass(carriers, items);
        yield return new TFPlayerConditionTicker(carriers, conditions, networking, healing);
        yield return new TFBlastJumpState(carriers, conditions, networking, directory, enginePlayers, events);
        yield return new TFStunStack(carriers, players, clock);
        yield return new TFRespawnOverride(carriers, clock);
        yield return new TFPlayerAttributes(carriers, items, schema, clock);
        yield return new TFAmmoPoolMirror(entities, carriers, arsenal);
        yield return new TFWeaponClipMirror(entities, carriers, arsenal);
    }
}

/// <summary>
/// The server's TF player state, by player: every live carrier by its
/// player's index (each <see cref="TFPlayerCarrier"/> enters on attach and
/// leaves on detach; <see cref="PlayerComposites.Get{T}(int)"/> finds the same
/// component), the <see cref="ITFPlayerData"/> the shared rules read and
/// write, and the <see cref="ITFPlayerClasses"/> the loadout asks.
///
/// <para>
/// NOTHING INJECTED, on purpose: the movement rules take this at the core's
/// movement resolution, which runs before the composite is registered (a
/// first build that took <see cref="PlayerComposites"/> here failed the
/// server's load live: "no service is registered for PlayerComposites").
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFPlayerData), typeof(ITFPlayerClasses)])]
public sealed class TFPlayerCarriers : ITFPlayerData, ITFPlayerClasses
{
    private readonly Dictionary<int, TFPlayerCarrier> _byIndex = new();

    // The player's other TF components, by index and type: each enters on its
    // attach and leaves on its detach, as the carrier does.
    private readonly Dictionary<(int Index, Type Type), object> _parts = new();

    /// <summary>How many players hold a carrier.</summary>
    public int Count => _byIndex.Count;

    /// <summary>A component of <paramref name="playerIndex"/>'s enters, under its own type.</summary>
    internal void EnterPart<T>(int playerIndex, T part) where T : class => _parts[(playerIndex, typeof(T))] = part;

    /// <summary>The component leaves, when it is the one held.</summary>
    internal void LeavePart<T>(int playerIndex, T part) where T : class
    {
        if (_parts.TryGetValue((playerIndex, typeof(T)), out var held) && ReferenceEquals(held, part))
            _parts.Remove((playerIndex, typeof(T)));
    }

    /// <summary>The player's component of type <typeparamref name="T"/>, or null.</summary>
    public T? Part<T>(int playerIndex) where T : class
        => _parts.GetValueOrDefault((playerIndex, typeof(T))) as T;

    /// <summary>The player's condition set (the carrier's), or null when the player has no carrier.</summary>
    public TFConditions? ConditionsOf(int playerIndex) => LiveCarrier(playerIndex)?.Conditions;

    internal void Enter(int playerIndex, TFPlayerCarrier carrier) => _byIndex[playerIndex] = carrier;

    internal void Leave(int playerIndex, TFPlayerCarrier carrier)
    {
        if (_byIndex.TryGetValue(playerIndex, out var held) && ReferenceEquals(held, carrier))
            _byIndex.Remove(playerIndex);
    }

    /// <summary>
    /// The player's carrier, dropped first when its entity died under the
    /// attach: a held binding is not a live entity, and a dead carrier is no
    /// carrier at all (reading its state throws <c>StaleEntityException</c> —
    /// the movement rule that asks every command would lose every run).
    /// </summary>
    private TFPlayerCarrier? LiveCarrier(int playerIndex)
    {
        if (!_byIndex.TryGetValue(playerIndex, out var carrier))
            return null;
        if (!carrier.Alive)
        {
            carrier.Drop();
            return null;
        }
        return carrier;
    }

    /// <summary>The player's carrier state, or null when the player has none.</summary>
    public TFPlayerNetState? Get(int playerIndex) => LiveCarrier(playerIndex)?.State;

    /// <summary>The player's carrier entity, or <see cref="EntityHandle.None"/>.</summary>
    public EntityHandle CarrierOf(int playerIndex)
        => LiveCarrier(playerIndex)?.Carrier ?? EntityHandle.None;

    /// <inheritdoc/>
    public TFPlayerNetState? Predicted(int playerIndex) => Get(playerIndex);

    /// <inheritdoc/>
    public TFPlayerNetState? Networked(int playerIndex) => Get(playerIndex);

    /// <inheritdoc/>
    public TFClass ClassOf(EntityHandle player) => Get(player.EntryIndex)?.Class() ?? TFClass.Undefined;

    /// <summary>
    /// Sets the player's class (<c>CTFPlayerClassShared::Init</c>'s
    /// <c>m_iClass</c>) and clears what belongs to the class before
    /// (<c>m_iAirDash</c>). False when the player has no carrier.
    /// </summary>
    public bool SetClass(int playerIndex, TFClass playerClass)
    {
        if (Get(playerIndex) is not { } state)
            return false;

        state.SetClass(playerClass);
        state.AirDash = 0;
        return true;
    }
}
