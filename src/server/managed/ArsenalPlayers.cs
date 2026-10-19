using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="IEnginePlayers"/> with the weapon calls answered by
/// <see cref="Arsenal"/> for hierarchy players (Phase 10).
///
/// <para>
/// A DECORATOR rather than edits at every call site, because the call sites
/// are the game's rules — the loadout, the ammo crates, the death drop — and
/// none of them should care which hierarchy a player is on. The old native
/// arms resolve CBasePlayer and answer nothing for a standalone; this answers
/// from the managed inventory first and lets everything else fall through
/// unchanged.
/// </para>
/// </summary>
public sealed class ArsenalPlayers : IEnginePlayers
{
    private readonly IEnginePlayers _inner;
    private readonly Arsenal _arsenal;

    // IEngineEntities was a constructor argument until the D19 re-census: the
    // ONLY thing this decorator used it for was asking the shell for a
    // classname once per touch. The question is now the registry's, so the
    // engine area is not taken at all rather than taken and left unread.
    public ArsenalPlayers(IEnginePlayers inner, Arsenal arsenal)
    {
        _inner = inner;
        _arsenal = arsenal;
    }

    /// <summary>
    /// Whether a handle is a hierarchy player.
    ///
    /// <para>
    /// <b>A DICTIONARY LOOKUP, NOT A CROSSING</b> — see
    /// <see cref="Arsenal.IsStandalonePlayer"/>, which this defers to rather
    /// than repeating, so the two decorator halves cannot drift apart on what
    /// "a hierarchy player" is. This is the site the D19 re-census measured at
    /// <b>6.000 <c>EntityGetClassName</c> crossings per server frame</b>: every
    /// <c>PickupItem.OnTouch</c> of an un-taken ammo item runs
    /// <c>TryGive</c> -> <see cref="GiveAmmo"/> -> here, and the engine reports
    /// the touch every tick a player stands inside the item's trigger.
    /// </para>
    /// </summary>
    private bool IsStandalone(EntityHandle player)
        => _arsenal.IsStandalonePlayer(player);

    //-------------------------------------------------------------------------
    // The weapon calls the Arsenal owns for hierarchy players.
    //-------------------------------------------------------------------------

    public bool GiveItem(EntityHandle player, string className)
        => IsStandalone(player) && _arsenal.Weapons.Contains(className)
            ? _arsenal.GiveWeapon(player, className)
            : _inner.GiveItem(player, className);

    public int GiveAmmo(EntityHandle player, int count, string ammoName)
        => IsStandalone(player)
            ? _arsenal.GiveAmmo(player, count, ammoName)
            : _inner.GiveAmmo(player, count, ammoName);

    public bool DropActiveWeapon(EntityHandle player, Vector velocity)
    {
        if (!IsStandalone(player))
            return _inner.DropActiveWeapon(player, velocity);

        _arsenal.DropActive(player, velocity);
        return true;
    }

    public void RemoveAllItems(EntityHandle player, bool stripSuit)
    {
        if (IsStandalone(player))
            _arsenal.Strip(player);
        else
            _inner.RemoveAllItems(player, stripSuit);
    }

    public string GetActiveWeapon(EntityHandle player)
    {
        if (!IsStandalone(player))
            return _inner.GetActiveWeapon(player);

        foreach (var (className, _, active) in _arsenal.Holdings(player))
        {
            if (active)
                return className;
        }

        return "";
    }

    //-------------------------------------------------------------------------
    // Everything else is the engine's, untouched.
    //-------------------------------------------------------------------------

    public void AddScore(EntityHandle player, int points, bool allowNegative) => _inner.AddScore(player, points, allowNegative);
    public void PlaceAtSpawn(EntityHandle player, Vector origin, Vector angles) => _inner.PlaceAtSpawn(player, origin, angles);

    public bool DeployLastWeapon(EntityHandle player) => _inner.DeployLastWeapon(player);
    public EntityHandle FindByUserId(int userId) => _inner.FindByUserId(userId);
    public EntityHandle GetActiveWeaponEntity(EntityHandle player) => _inner.GetActiveWeaponEntity(player);
    public EntityHandle GetWeaponEntity(EntityHandle player, int slot) => _inner.GetWeaponEntity(player, slot);
    public EntityHandle GetViewModelEntity(EntityHandle player) => _inner.GetViewModelEntity(player);
    public EntityHandle GetHandle(int playerIndex) => _inner.GetHandle(playerIndex);
    public EntityHandle GetCommandClient() => _inner.GetCommandClient();
    public ObserverMode GetObserverMode(EntityHandle player) => _inner.GetObserverMode(player);
    public Vector GetShootOrigin(EntityHandle player) => _inner.GetShootOrigin(player);
    public int GetTeam(EntityHandle player) => _inner.GetTeam(player);
    public int GetUserId(EntityHandle player) => _inner.GetUserId(player);
    public bool HolsterActiveWeapon(EntityHandle player) => _inner.HolsterActiveWeapon(player);
    public bool IsAlive(EntityHandle player) => _inner.IsAlive(player);
    public bool BoxInPvs(EntityHandle player, Vector mins, Vector maxs) => _inner.BoxInPvs(player, mins, maxs);
    public void MakePickup(EntityHandle entity, float sizeX, float sizeY, float sizeZ) => _inner.MakePickup(entity, sizeX, sizeY, sizeZ);
    public void Respawn(EntityHandle player) => _inner.Respawn(player);
    public void SetFov(EntityHandle player, EntityHandle requester, int fov, float rate = 0f) => _inner.SetFov(player, requester, fov, rate);
    public void SetObserverMode(EntityHandle player, ObserverMode mode) => _inner.SetObserverMode(player, mode);
    public void SetTeam(EntityHandle player, int team) => _inner.SetTeam(player, team);
    public void SetPickupEnabled(EntityHandle entity, bool enabled) => _inner.SetPickupEnabled(entity, enabled);
    public void SetViewEntity(EntityHandle player, EntityHandle view) => _inner.SetViewEntity(player, view);

    // plan_server.md Phase 7 / plan-audit C5. NOT decorated, deliberately: the
    // Arsenal answers WEAPON questions for a hierarchy player, and where a
    // player's ears are is not one. The two reads already ask one hierarchy
    // each and the choice between them is ServerClients.EarPositionForClient's,
    // so a decorator that intercepted either would be taking that decision back
    // into a place nobody would look for it.
    public bool TryStandalonePlayerEarPosition(int edictIndex, out Vector ears)
        => _inner.TryStandalonePlayerEarPosition(edictIndex, out ears);

    public bool TrySdkPlayerEarPosition(int edictIndex, out Vector ears)
        => _inner.TrySdkPlayerEarPosition(edictIndex, out ears);
}
