namespace SourceSharp.Abi;

/// <summary>
/// Two of stock's CBasePlayer members the physics shadow reads, kept per
/// player on the managed side (plan_physics_bones_perf C1/C2, 2026-10-12):
///
/// <list type="bullet">
/// <item><c>m_oldOrigin</c>: where the last command left the player
/// (PostThinkVPhysics) or the last shadow update put them
/// (VPhysicsShadowUpdate). Read by the halfway aim of a player leaning on
/// physics and by the stuck check's way back. Native kept one copy and
/// PlayerShadowRecovery another, and neither updated the other; stock has
/// one (player.cpp:4851, :8338, :8487).</item>
/// <item><c>m_touchedPhysObject</c>: whether the last command's move ran into
/// a moveable physics object (CBasePlayer::Touch, player.cpp:4761), decided
/// from the move's touch list after each command and read until the next.</item>
/// </list>
/// </summary>
public sealed class PlayerMoveMemory
{
    private readonly Dictionary<EntityHandle, (Vector OldOrigin, bool Touched)> _players = new();

    /// <summary>Stock's <c>m_oldOrigin</c>: zero until first written (<c>m_oldOrigin.Init()</c>).</summary>
    public Vector OldOrigin(EntityHandle player)
        => _players.TryGetValue(player, out var p) ? p.OldOrigin : Vector.Zero;

    /// <summary><c>m_oldOrigin = GetAbsOrigin()</c>.</summary>
    public void SetOldOrigin(EntityHandle player, Vector origin)
    {
        _players.TryGetValue(player, out var p);
        _players[player] = (origin, p.Touched);
    }

    /// <summary>Stock's <c>m_touchedPhysObject</c>, as the last command left it.</summary>
    public bool TouchedPhysics(EntityHandle player)
        => _players.TryGetValue(player, out var p) && p.Touched;

    /// <summary>The last command's answer: cleared at its start and set by its touches, in one write.</summary>
    public void SetTouchedPhysics(EntityHandle player, bool touched)
    {
        _players.TryGetValue(player, out var p);
        _players[player] = (p.OldOrigin, touched);
    }

    /// <summary>Every player whose old origin has been written, for the reload's carry.</summary>
    public IEnumerable<(EntityHandle Player, Vector OldOrigin)> OldOrigins()
    {
        foreach (var (player, p) in _players)
            yield return (player, p.OldOrigin);
    }

    /// <summary>Everything forgotten (a reload's restore starts here).</summary>
    public void Clear() => _players.Clear();
}
