namespace SourceSharp.Abi;

/// <summary>
/// The engine calls game code makes on a server player's movement state
/// (plan_server_native_v2 T4g), answered by SourceSharp.Server.Interop's
/// ServerPlayerMovement from the managed command loop's records. Declared
/// here, beside <see cref="IServerTraceRoute"/>, because the side's own half
/// holds one: its EngineSide asks this before it would ask native, and native
/// keeps only the words its own readers still read.
/// </summary>
public interface IServerPlayerMovementRoute
{
    /// <summary>
    /// A player's move type and whether it stands on something, from its
    /// record. False when <paramref name="entity"/> is not a player.
    /// </summary>
    bool PlayerState(EntityHandle entity, out MoveType moveType, out bool onGround);

    /// <summary>A player's move type set; the one it had.</summary>
    MoveType SetMoveType(EntityHandle player, MoveType moveType);

    /// <summary>m_Local.m_bSlowMovement.</summary>
    void SetSlowMovement(EntityHandle player, bool slow);

    /// <summary><c>SuitPower_SetCharge</c>: the record's suit power.</summary>
    void SetSuitPower(EntityHandle player, float power);

    /// <summary>The record's old buttons take IN_USE (PlayerUse's debounce).</summary>
    void SwallowUse(EntityHandle player);

    /// <summary>The record's PlayUseDenySound set.</summary>
    void QueueUseDenySound(EntityHandle player);

    /// <summary>The record's PlayUseDenySound, cleared.</summary>
    bool TakeUseDenySound(EntityHandle player);

    /// <summary>The observer mode, and the move type it implies (CStandalonePlayer::SetObserver's half).</summary>
    void SetObserverMode(EntityHandle player, int mode);

    /// <summary>A push into a player's base velocity. False, said once per entity, when the entity is not a player.</summary>
    bool AddBaseVelocity(EntityHandle entity, Vector velocity);

    /// <summary>
    /// Stock's <c>ApplyAbsVelocityImpulse</c> on a player, whose move type is
    /// never VPHYSICS: the impulse is added to the velocity its record holds,
    /// and <paramref name="clearGround"/> lets go of the ground as
    /// <c>SetGroundEntity( NULL )</c> does. False when <paramref name="entity"/>
    /// is not a player.
    /// </summary>
    bool ApplyVelocityImpulse(EntityHandle entity, Vector impulse, bool clearGround);

    /// <summary>
    /// Stock's <c>GetAbsVelocity</c> of a player: its record's velocity, the value
    /// the last command left and every write since. False for anything without a
    /// record, which is not a player or has not spawned.
    /// </summary>
    bool Velocity(EntityHandle entity, out Vector velocity);

    /// <summary>
    /// Stock's <c>SetAbsVelocity</c> on a player: the record's velocity, which its
    /// next command moves by. A plain write: no clamp, and the ground stays held.
    /// False when <paramref name="entity"/> is not a player.
    /// </summary>
    bool SetVelocity(EntityHandle entity, Vector velocity);

    /// <summary>
    /// <c>CBasePlayer::GetSmoothedVelocity</c>: the record's velocity as PostThink
    /// averages it, which a collision has not yet taken away. False as <see cref="Velocity"/>.
    /// </summary>
    bool SmoothedVelocity(EntityHandle entity, out Vector velocity);

    /// <summary>
    /// A player's view punch and its velocity, on the record the next command decays:
    /// what a shot's kick lands on. False when <paramref name="entity"/> is not a player.
    /// </summary>
    bool SetPunch(EntityHandle entity, Vector punch, Vector velocity);

    /// <summary>UTIL_ViewPunch: every player in reach shoved.</summary>
    void ViewPunch(Vector center, Vector punch, float radius, bool inAir);

    /// <summary>
    /// CBasePlayer::SetFOV (baseplayer_shared.cpp:1946): the player's field of
    /// view set by <paramref name="requester"/>, 0 releasing the zoom, ramped
    /// over <paramref name="rate"/> seconds. A requester while another's zoom
    /// is held is refused (the owner rule); a null requester is stock's
    /// assertion. A positive <paramref name="start"/> is stock's
    /// <c>iZoomStart</c>, the foot of the new ramp; 0 takes the view's
    /// <c>GetFOV()</c> as stock's default arm does.
    /// </summary>
    void SetFov(EntityHandle player, EntityHandle requester, int fov, float rate, int start = 0);

    /// <summary>
    /// The player's own field of view (<c>CBasePlayer::SetDefaultFOV</c>,
    /// player.cpp:8741): the word an unzoomed view answers, written plain —
    /// no ramp, no owner. Stock's <c>ClientSettingsChanged</c> feeds it the
    /// clamped <c>fov_desired</c> (tf_gamerules.cpp:10275-10279).
    /// </summary>
    void SetDefaultFov(EntityHandle player, int fov);
}
