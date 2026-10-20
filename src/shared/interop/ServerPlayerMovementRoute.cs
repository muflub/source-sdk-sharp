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

    /// <summary>The observer mode, and the move type it implies (CStandalonePlayer::SetObserver's half).</summary>
    void SetObserverMode(EntityHandle player, int mode);

    /// <summary>A push into a player's base velocity. False, said once per entity, when the entity is not a player.</summary>
    bool AddBaseVelocity(EntityHandle entity, Vector velocity);

    /// <summary>UTIL_ViewPunch: every player in reach shoved.</summary>
    void ViewPunch(Vector center, Vector punch, float radius, bool inAir);
}
