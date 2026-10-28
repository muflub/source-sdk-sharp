// Players, and what a player carries and sees.

namespace SourceSharp.Abi;

/// <summary>
/// A player: which team, what they are carrying, whether they are alive, and what
/// they are looking through.
///
/// <para>
/// Weapons appear here as well as in <see cref="IEngineWeapons"/>, and the split
/// is by subject rather than by topic: giving a player a gun is something done to
/// the PLAYER, while telling a gun when it may next fire is done to the WEAPON.
/// </para>
/// </summary>
public interface IEnginePlayers
{
    /// <summary>Adds to a player's score. Negative points are refused unless allowed.</summary>
    void AddScore(EntityHandle player, int points, bool allowNegative);

    /// <inheritdoc cref="EngineSide.PlayerPlaceAtSpawn"/>
    void PlaceAtSpawn(EntityHandle player, Vector origin, Vector angles);

    /// <summary>
    /// Takes the remembered weapon back out. False when nothing was remembered,
    /// which is what a player who never had one looks like.
    /// </summary>
    bool DeployLastWeapon(EntityHandle player);

    /// <summary>
    /// Throws the weapon in a player's hands into the world, where somebody else
    /// can pick it up. False when they were empty-handed, which is a real state
    /// rather than a failure.
    /// </summary>
    bool DropActiveWeapon(EntityHandle player, Vector velocity);

    /// <inheritdoc cref="EngineSide.FindPlayerByUserId"/>
    EntityHandle FindByUserId(int userId);

    /// <summary>
    /// The classname of the weapon a player is holding, server side. Empty when
    /// they are empty-handed, which is a real state here — the loadout can be
    /// empty, and a player between weapons has none for a tick.
    /// </summary>
    string GetActiveWeapon(EntityHandle player);

    /// <summary>
    /// The weapon a player is holding, as a handle. Invalid when empty-handed.
    ///
    /// <see cref="GetPlayerActiveWeapon"/> answers the same question with a
    /// classname, which is what a rule about weapons wants. This is for acting
    /// ON the weapon entity — hiding the viewmodel, most of all.
    /// </summary>
    EntityHandle GetActiveWeaponEntity(EntityHandle player);

    /// <summary>
    /// One inventory slot, as an untyped handle. Invalid for an empty slot, a
    /// slot out of 0..47, or no such player.
    ///
    /// The managed half of m_hMyWeapons: the slots become addressable without
    /// a CBaseCombatWeapon anywhere in the question, which is what lets the
    /// inventory's readers survive the weapons leaving that class.
    /// </summary>
    EntityHandle GetWeaponEntity(EntityHandle player, int slot);

    /// <summary>
    /// The player's view model, as an entity handle. Invalid when the player
    /// has none — before spawn, or as an observer.
    /// </summary>
    EntityHandle GetViewModelEntity(EntityHandle player);

    /// <inheritdoc cref="EngineSide.GetPlayerHandle"/>
    EntityHandle GetHandle(int playerIndex);

    /// <inheritdoc cref="EngineSide.GetCommandClient"/>
    EntityHandle GetCommandClient();

    /// <inheritdoc cref="EngineSide.PlayerGetObserverMode"/>
    ObserverMode GetObserverMode(EntityHandle player);

    /// <inheritdoc cref="EngineSide.GetPlayerShootOrigin"/>
    Vector GetShootOrigin(EntityHandle player);

    /// <inheritdoc cref="EngineSide.GetPlayerTeam"/>
    int GetTeam(EntityHandle player);

    /// <summary>
    /// The engine's userid for a player, or 0 for anything that is not one.
    ///
    /// The other half of the player's identity. Entity calls take a handle;
    /// game events name players by userid, and until this existed a rule
    /// reading a death event could not reach either player it described.
    /// </summary>
    int GetUserId(EntityHandle player);

    /// <summary>
    /// Gives a player ammo. Returns how much was actually added, which is less
    /// than asked for near the carry limit.
    /// </summary>
    int GiveAmmo(EntityHandle player, int count, string ammoName);

    /// <summary>
    /// Gives a player an item by entity class name. Returns false if no such
    /// class exists — a typo would otherwise look like a rules bug.
    /// </summary>
    bool GiveItem(EntityHandle player, string className);

    /// <summary>
    /// Puts the weapon in a player's hands away, remembering which it was, and
    /// hides the viewmodel with it. False when they were holding nothing.
    /// </summary>
    bool HolsterActiveWeapon(EntityHandle player);

    /// <inheritdoc cref="EngineSide.PlayerIsAlive"/>
    bool IsAlive(EntityHandle player);

    /// <summary>
    /// Does this player see the world-space box — is it inside the PVS of
    /// the leaf cluster at their eyes? The engine's own read (stock builds
    /// the same cluster/PVS trio in its check-client machinery,
    /// triggers.cpp:1615); what a yes is worth is the caller's rule — stock's
    /// <c>CTRiggerHurt::RadiationThink</c> pings only the geiger of a player
    /// who can see the box.
    ///
    /// <para>
    /// <b>False when there is no engine, no attached shim, or the handle is
    /// not a player.</b> Failing closed means a player who cannot be asked
    /// hears nothing, which is the same outcome as an empty PVS and never a
    /// false ping.
    /// </para>
    /// </summary>
    bool BoxInPvs(EntityHandle player, Vector mins, Vector maxs);

    /// <inheritdoc cref="EngineSide.MakePickup"/>
    void MakePickup(EntityHandle entity, float sizeX, float sizeY, float sizeZ);

    /// <summary>
    /// Destroys everything a player carries, and the suit too if asked.
    ///
    /// Not the same as dropping: <see cref="PlayerDropActiveWeapon"/> hands one
    /// weapon to the world where somebody else can take it, while this leaves
    /// nothing behind.
    /// </summary>
    void RemoveAllItems(EntityHandle player, bool stripSuit);

    /// <inheritdoc cref="EngineSide.PlayerRespawn"/>
    void Respawn(EntityHandle player);

    /// <summary>
    /// Zooms a player's view to a field of view over <paramref name="rate"/>
    /// seconds.
    /// </summary>
    /// <param name="requester">
    /// Who is asking. The engine tracks the owner so two entities zooming the
    /// same player do not fight, and releasing must come from the same one.
    /// </param>
    /// <param name="fov">
    /// Zero RELEASES rather than setting a field of view of zero — it is how a
    /// requester says it has finished and the player returns to their own
    /// setting.
    /// </param>
    void SetFov(EntityHandle player, EntityHandle requester, int fov, float rate = 0f);

    /// <inheritdoc cref="EngineSide.PlayerSetObserverMode"/>
    void SetObserverMode(EntityHandle player, ObserverMode mode);

    /// <inheritdoc cref="EngineSide.PlayerSetTeam"/>
    void SetTeam(EntityHandle player, int team);

    /// <inheritdoc cref="EngineSide.EntitySetPickupEnabled"/>
    void SetPickupEnabled(EntityHandle entity, bool enabled);

    /// <summary>
    /// Renders a player's view from <paramref name="view"/> instead of their own
    /// eyes. An invalid handle gives the view back.
    ///
    /// The entity may be on either hierarchy. What it must be is TRANSMITTED —
    /// the client takes the origin and angles off its own copy, so a view
    /// entity it was never sent leaves the player looking out of their own head
    /// with nothing to say why. See <see cref="EntitySetTransmitAlways"/>.
    /// </summary>
    void SetViewEntity(EntityHandle player, EntityHandle view);

    // ---- plan_server.md Phase 7 / plan-audit C5 ---------------------------
    //
    // Where one client's ears are, asked of ONE HIERARCHY EACH. The pair is
    // here rather than on IEngineSound because what ClientEarPosition wants is
    // a fact about a PLAYER — where this client's listener sits — and the
    // sound system is only who asked.
    //
    // TWO METHODS AND NOT ONE, and that is the point of C5's first crossing
    // rather than an accident of the ABI: the choice between the two
    // hierarchies used to be an `#ifdef SOURCESHARP` block inside
    // ClientEarPosition, and moving it means giving C# both reads separately so
    // the ORDER is C#'s. See ServerClients.EarPositionForClient — the order
    // there is standalone-then-SDK, which is NOT the order of the skybox pair
    // on IEngineVisibility, and the difference is deliberate.
    //
    // BY EDICT INDEX, NOT BY EntityHandle, because the caller is the engine
    // asking about an edict_t and a standalone player has no SDK entity to
    // make a handle out of. That is the same reason the skybox pair takes one.

    /// <inheritdoc cref="EngineSide.ClientsStandalonePlayerEarPosition"/>
    bool TryStandalonePlayerEarPosition(int edictIndex, out Vector ears);

    /// <summary>
    /// Where one client's ears are, read off the SDK hierarchy — the other half
    /// of the pair above.
    /// </summary>
    bool TrySdkPlayerEarPosition(int edictIndex, out Vector ears);
}

/// <summary>
/// How a player moves, and what may stop them: speed scales, buttons, and the
/// constraints a map can put on them.
///
/// <para>
/// Separate from <see cref="IEnginePlayers"/> because it is the one part of a
/// player that is asked about every tick, by prediction on one side and by rules
/// on the other.
/// </para>
/// </summary>
public interface IEnginePlayerMovement
{
    /// <inheritdoc cref="EngineSide.PlayerClearMovementConstraint"/>
    void ClearMovementConstraint(EntityHandle player);

    /// <summary>
    /// Holds buttons down for a player whether or not they are pressing them.
    /// The mirror of <see cref="PlayerSetButtonsDisabled"/>: only the named bits
    /// move either way, so a caller gives back exactly what it took.
    /// </summary>
    void ForceButtons(EntityHandle player, InputButtons buttons, bool forced);

    /// <summary>
    /// The buttons a player is holding right now.
    ///
    /// The read behind <see cref="PlayerSetButtonsDisabled"/>. Wanted by
    /// anything waiting for the player to press ANY key, which no subscription
    /// to a particular one can express.
    /// </summary>
    InputButtons GetButtons(EntityHandle player);

    /// <inheritdoc cref="EngineSide.RegisterMovementParams"/>
    void RegisterParams(MovementParams parameters);

    /// <summary>
    /// Whether the engine may move a player who did not steer — the step up onto
    /// a stair, the slide along a wall, the ladder mount.
    ///
    /// An engine call rather than a Movement rule because it is predicted on
    /// both sides and networked, exactly as the lagged movement value is.
    /// </summary>
    void SetAllowAutoMovement(EntityHandle player, bool allow);

    /// <summary>
    /// Strikes buttons out of a player's commands before anything reads them, or
    /// stops doing so.
    ///
    /// Only the named bits are touched, so a caller clears exactly what it
    /// claimed and leaves whatever else is held down alone.
    /// </summary>
    void SetButtonsDisabled(EntityHandle player, InputButtons buttons, bool disabled);

    /// <summary>
    /// Whether a player may act at all. Stock's EnableControl, inverted to read
    /// as what it does rather than what it permits.
    ///
    /// Frozen is not safe: a frozen player is still shot at, and still dies.
    /// Anything that freezes somebody for a cutscene wants
    /// <see cref="EntitySetTakeDamageMode"/> as well.
    /// </summary>
    void SetFrozen(EntityHandle player, bool frozen);

    /// <summary>
    /// How fast one player's world runs: 1 is normal, 0.5 is half speed.
    ///
    /// Deliberately here rather than in <c>Movement</c>, which is where a speed
    /// rule otherwise belongs. Movement is predicted, so a per-player factor the
    /// client cannot see makes the two sides disagree about where the player is
    /// on every unacknowledged command — and the symptom is rubber-banding. The
    /// engine networks this value, so both halves scale by the same number.
    /// </summary>
    void SetLaggedMovement(EntityHandle player, float value);

    /// <summary>
    /// Keeps a player within <paramref name="radius"/> of a point, by slowing
    /// them near the edge rather than by a wall.
    ///
    /// <paramref name="width"/> is the band over which the slowing comes on and
    /// <paramref name="speedFactor"/> is what the speed is multiplied by at the
    /// edge. A radius of 0 releases the player, which is how the engine itself
    /// spells "not constrained".
    /// </summary>
    void SetMovementConstraint(EntityHandle player, Vector centre, float radius, float width, float speedFactor);

    /// <inheritdoc cref="EngineSide.PlayerSetSlowMovement"/>
    void SetSlowMovement(EntityHandle player, bool slow);

    /// <summary>
    /// <c>SuitPower_SetCharge</c>: the player's suit power, 0 to 100 (a spawn
    /// sets it full). The movement loop drains and recharges it from there.
    /// </summary>
    void SetSuitPower(EntityHandle player, float power);

    /// <summary>
    /// PlayerUse's debounce (<c>m_Local.m_nOldButtons |= IN_USE</c>): the
    /// +USE that used something reads as held to this command's move, so a
    /// ladder in reach is not mounted by it as well.
    /// </summary>
    void SwallowUse(EntityHandle player);

    /// <summary>
    /// CHL2_Player's <c>m_bPlayUseDenySound = true</c>: a +USE found nothing,
    /// so the deny sound is due after the move, unless the move's ladder took
    /// the press.
    /// </summary>
    void QueueUseDenySound(EntityHandle player);

    /// <summary>
    /// CHL2_Player::ItemPostFrame's half: whether the deny sound is still due,
    /// cleared by asking.
    /// </summary>
    bool TakeUseDenySound(EntityHandle player);

    /// <inheritdoc cref="EngineSide.PlayerSetMoveType"/>
    MoveType SetMoveType(EntityHandle player, MoveType moveType);

    /// <inheritdoc cref="EngineSide.PlayerSnapEyeAngles"/>
    void SnapEyeAngles(EntityHandle player, Vector pitchYawRoll);
}

/// <summary>
/// A weapon's own state: its clip, its animations, when it may next fire, and the
/// bullets it puts into the world.
///
/// <para>
/// The state machine that DECIDES when a weapon fires is managed and is not here.
/// What is here is what genuinely is the engine — assembling a bullet, tracing it,
/// and playing what that looks like.
/// </para>
/// </summary>
public interface IEngineWeapons
{

    /// <inheritdoc cref="EngineSide.RegisterAmmoType"/>
    void RegisterAmmoType(string name, DamageType damageType, int maxCarry, int tracerType = 0, float damageForce = 0f, float damage = 0f);

}

public interface IEngineHud
{
    /// <summary>
    /// A line of text drawn in the world at a position, through the engine's
    /// debug-overlay channel.
    ///
    /// <para>
    /// <paramref name="line"/> offsets it downward, so several calls at one
    /// position stack rather than overprint. <paramref name="duration"/> is a
    /// minimum in seconds — whatever is drawing has to keep drawing.
    /// </para>
    /// </summary>
    void DrawWorldText(Vector origin, int line, string text, float duration, int r = 255, int g = 255, int b = 255, int a = 255);

    /// <inheritdoc cref="EngineSide.DrawEntityOverlay"/>
    bool DrawEntityOverlay(EntityHandle entity, int line, string? text, float duration, int r = 255, int g = 255, int b = 255, int a = 255);

    /// <inheritdoc cref="EngineSide.OverlayReadout"/>
    (uint Drawn, uint Clipped, uint NoViewer, uint NoOverlay, bool ViewerSet) OverlayReadout();

    /// <inheritdoc cref="EngineSide.AddLineOverlay"/>
    void AddLineOverlay(Vector start, Vector end, int r, int g, int b,
                        bool noDepthTest = true, float duration = 0f);

    /// <inheritdoc cref="EngineSide.LocalPlayerHealth"/>
    int LocalPlayerHealth { get; }

    /// <inheritdoc cref="EngineSide.LocalPlayerOrigin"/>
    Vector? LocalPlayerOrigin { get; }

    /// <inheritdoc cref="EngineSide.HudLocalise"/>
    string Localise(string token);

    /// <inheritdoc cref="EngineSide.HudLookupBinding"/>
    string LookupBinding(string binding);

    /// <inheritdoc cref="EngineSide.GetLocalPlayerIndex"/>
    int LocalPlayerIndex { get; }

    /// <inheritdoc cref="EngineSide.HudTextMessageGet"/>
    TextMessage? TextMessageGet(string name);

    /// <inheritdoc cref="EngineSide.ViewPunch"/>
    void ViewPunch(Vector origin, Vector punch, float radius, bool inAir = false);

    /// <inheritdoc cref="EngineSide.ClientViewportQuery"/>
    ViewportFacts ViewportQuery(string panel);

    /// <inheritdoc cref="EngineSide.ClientViewportShow"/>
    void ViewportShow(string panel, bool show, IReadOnlyList<KeyValuePair<string, string>> data, ViewportActions actions);
}

/// <summary>What the client's native state says about a viewport panel (ClientViewportQuery).</summary>
[Flags]
public enum ViewportFacts
{
    None = 0,

    /// <summary>The viewport has a panel by that name.</summary>
    PanelExists = 1,

    /// <summary>The client mode allows the info (MOTD) panel just now.</summary>
    InfoPanelAllowed = 2,
}

/// <summary>What C#'s VGUIMenu handler asks the client's viewport to do (ClientViewportShow), in stock's order.</summary>
[Flags]
public enum ViewportActions
{
    None = 0,

    /// <summary>Hand the panel its data.</summary>
    SetData = 1,

    /// <summary>Take a screenshot in a second (hud_takesshots at the scoreboard).</summary>
    ScreenShot = 2,

    /// <summary>Fire ds_screenshot client side.</summary>
    ScreenShotEvent = 4,

    /// <summary>Tell the client mode the info panel was shown.</summary>
    InfoPanelDisplayed = 8,

    /// <summary>Show or hide the panel.</summary>
    Show = 16,
}
