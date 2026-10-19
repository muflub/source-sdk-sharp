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
    /// <inheritdoc cref="EngineSide.AddPlayerScore"/>
    void AddScore(EntityHandle player, int points, bool allowNegative);

    /// <inheritdoc cref="EngineSide.PlayerPlaceAtSpawn"/>
    void PlaceAtSpawn(EntityHandle player, Vector origin, Vector angles);

    /// <inheritdoc cref="EngineSide.PlayerDeployLastWeapon"/>
    bool DeployLastWeapon(EntityHandle player);

    /// <inheritdoc cref="EngineSide.PlayerDropActiveWeapon"/>
    bool DropActiveWeapon(EntityHandle player, Vector velocity);

    /// <inheritdoc cref="EngineSide.FindPlayerByUserId"/>
    EntityHandle FindByUserId(int userId);

    /// <inheritdoc cref="EngineSide.GetPlayerActiveWeapon"/>
    string GetActiveWeapon(EntityHandle player);

    /// <inheritdoc cref="EngineSide.PlayerGetActiveWeaponEntity"/>
    EntityHandle GetActiveWeaponEntity(EntityHandle player);

    /// <inheritdoc cref="EngineSide.PlayerGetWeaponEntity"/>
    EntityHandle GetWeaponEntity(EntityHandle player, int slot);

    /// <inheritdoc cref="EngineSide.PlayerGetViewModelEntity"/>
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

    /// <inheritdoc cref="EngineSide.GetPlayerUserId"/>
    int GetUserId(EntityHandle player);

    /// <inheritdoc cref="EngineSide.GivePlayerAmmo"/>
    int GiveAmmo(EntityHandle player, int count, string ammoName);

    /// <inheritdoc cref="EngineSide.GivePlayerItem"/>
    bool GiveItem(EntityHandle player, string className);

    /// <inheritdoc cref="EngineSide.PlayerHolsterActiveWeapon"/>
    bool HolsterActiveWeapon(EntityHandle player);

    /// <inheritdoc cref="EngineSide.PlayerIsAlive"/>
    bool IsAlive(EntityHandle player);

    /// <inheritdoc cref="EngineSide.PlayerBoxInPvs"/>
    bool BoxInPvs(EntityHandle player, Vector mins, Vector maxs);

    /// <inheritdoc cref="EngineSide.MakePickup"/>
    void MakePickup(EntityHandle entity, float sizeX, float sizeY, float sizeZ);

    /// <inheritdoc cref="EngineSide.PlayerRemoveAllItems"/>
    void RemoveAllItems(EntityHandle player, bool stripSuit);

    /// <inheritdoc cref="EngineSide.PlayerRespawn"/>
    void Respawn(EntityHandle player);

    /// <inheritdoc cref="EngineSide.PlayerSetFov"/>
    void SetFov(EntityHandle player, EntityHandle requester, int fov, float rate = 0f);

    /// <inheritdoc cref="EngineSide.PlayerSetObserverMode"/>
    void SetObserverMode(EntityHandle player, ObserverMode mode);

    /// <inheritdoc cref="EngineSide.PlayerSetTeam"/>
    void SetTeam(EntityHandle player, int team);

    /// <inheritdoc cref="EngineSide.EntitySetPickupEnabled"/>
    void SetPickupEnabled(EntityHandle entity, bool enabled);

    /// <inheritdoc cref="EngineSide.PlayerSetViewEntity"/>
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

    /// <inheritdoc cref="EngineSide.ClientsSdkPlayerEarPosition"/>
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

    /// <inheritdoc cref="EngineSide.PlayerForceButtons"/>
    void ForceButtons(EntityHandle player, InputButtons buttons, bool forced);

    /// <inheritdoc cref="EngineSide.PlayerGetButtons"/>
    InputButtons GetButtons(EntityHandle player);

    /// <inheritdoc cref="EngineSide.RegisterMovementParams"/>
    void RegisterParams(MovementParams parameters);

    /// <inheritdoc cref="EngineSide.PlayerSetAllowAutoMovement"/>
    void SetAllowAutoMovement(EntityHandle player, bool allow);

    /// <inheritdoc cref="EngineSide.PlayerSetButtonsDisabled"/>
    void SetButtonsDisabled(EntityHandle player, InputButtons buttons, bool disabled);

    /// <inheritdoc cref="EngineSide.PlayerSetFrozen"/>
    void SetFrozen(EntityHandle player, bool frozen);

    /// <inheritdoc cref="EngineSide.PlayerSetLaggedMovement"/>
    void SetLaggedMovement(EntityHandle player, float value);

    /// <inheritdoc cref="EngineSide.PlayerSetMovementConstraint"/>
    void SetMovementConstraint(EntityHandle player, Vector centre, float radius, float width, float speedFactor);

    /// <inheritdoc cref="EngineSide.PlayerSetSlowMovement"/>
    void SetSlowMovement(EntityHandle player, bool slow);

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
    /// <inheritdoc cref="EngineSide.WeaponAddViewKick"/>
    void AddViewKick(EntityHandle weapon);

    /// <inheritdoc cref="EngineSide.WeaponAnimDuration"/>
    float AnimDuration(EntityHandle weapon, WeaponAnim anim);

    /// <inheritdoc cref="EngineSide.WeaponFireBullets"/>
    void FireBullets(EntityHandle weapon, int shots, float spreadDegrees);

    /// <inheritdoc cref="EngineSide.WeaponGetState"/>
    bool GetState(EntityHandle weapon, out WeaponState state);

    /// <inheritdoc cref="EngineSide.WeaponIdle"/>
    void Idle(EntityHandle weapon);

    /// <inheritdoc cref="EngineSide.WeaponMuzzleFlash"/>
    void MuzzleFlash(EntityHandle weapon);

    /// <inheritdoc cref="EngineSide.WeaponPlaySound"/>
    void PlaySound(EntityHandle weapon, WeaponSound sound);

    /// <inheritdoc cref="EngineSide.RegisterAmmoType"/>
    void RegisterAmmoType(string name, DamageType damageType, int maxCarry, int tracerType = 0, float damageForce = 0f, float damage = 0f);

    /// <inheritdoc cref="EngineSide.RegisterWeaponEntity"/>
    void RegisterWeaponEntity(string className);

    /// <inheritdoc cref="EngineSide.RegisterWeaponType"/>
    void RegisterWeaponType(string className, float fireRate, float spreadDegrees, int pellets);

    /// <inheritdoc cref="EngineSide.WeaponSendAnim"/>
    void SendAnim(EntityHandle weapon, WeaponAnim anim);

    /// <inheritdoc cref="EngineSide.WeaponSetClip"/>
    void SetClip(EntityHandle weapon, int clip);

    /// <inheritdoc cref="EngineSide.WeaponSetInReload"/>
    void SetInReload(EntityHandle weapon, bool inReload);

    /// <inheritdoc cref="EngineSide.WeaponSetNextPrimaryAttack"/>
    void SetNextPrimaryAttack(EntityHandle weapon, float time);

    /// <inheritdoc cref="EngineSide.WeaponSetNextSecondaryAttack"/>
    void SetNextSecondaryAttack(EntityHandle weapon, float time);

    /// <inheritdoc cref="EngineSide.WeaponTakeAmmo"/>
    int TakeAmmo(EntityHandle weapon, int count);

    /// <inheritdoc cref="EngineSide.WeaponTakeSecondaryAmmo"/>
    int TakeSecondaryAmmo(EntityHandle weapon, int count);
}

/// <summary>
/// Drawing on the screen, and reading the local player it is drawn for.
///
/// <para>
/// CLIENT ONLY, and the only area of which that is true in this strict a sense:
/// the draw calls are valid only inside the HUD paint pass, and the local-player
/// reads have no meaning on a server that has many.
/// </para>
/// </summary>
/// <summary>
/// How much room a string takes in the HUD's font.
///
/// A zero width is "there is no font yet" rather than "an empty string", which
/// is a real state during start-up — and a caller that laid text out against it
/// would pile every right-aligned line on the margin.
/// </summary>
public readonly record struct TextSize(int Width, int Height)
{
    /// <summary>Whether the engine could answer at all.</summary>
    public bool IsKnown => Width > 0;
}

public interface IEngineHud
{
    /// <inheritdoc cref="EngineSide.DrawWorldText"/>
    void DrawWorldText(Vector origin, int line, string text, float duration, int r = 255, int g = 255, int b = 255, int a = 255);

    /// <inheritdoc cref="EngineSide.DrawEntityOverlay"/>
    bool DrawEntityOverlay(EntityHandle entity, int line, string? text, float duration, int r = 255, int g = 255, int b = 255, int a = 255);

    /// <inheritdoc cref="EngineSide.OverlayReadout"/>
    (uint Drawn, uint Clipped, uint NoViewer, uint NoOverlay, bool ViewerSet) OverlayReadout();

    /// <inheritdoc cref="EngineSide.OverlayResetCounts"/>
    void OverlayResetCounts();

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
