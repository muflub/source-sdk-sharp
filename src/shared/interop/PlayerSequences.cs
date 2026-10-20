namespace SourceSharp.Abi;

/// <summary>
/// The slots a player-only sequence is made of (plan_server_native_v2 T4d).
/// <c>ServerPlayerSequenceSlots</c> answers every member with the side's own
/// method of the same name; a fact answers them with a recorder.
/// </summary>
internal interface IPlayerSequenceSlots
{
    bool EntityIsPlayer(EntityHandle h);

    Vector EntityGetEyePosition(EntityHandle h);

    Vector EntityGetEyeAngles(EntityHandle h);

    TraceResult TraceLine(Vector start, Vector end, TraceMask mask, EntityHandle ignore);

    void PlayerSetObserverMode(EntityHandle player, ObserverMode mode);

    void SpawnEntity(EntityHandle entity);
}

/// <summary>
/// PlayerRespawn, GetPlayerShootOrigin and TracePlayerAim, which were native
/// slots that made a fixed sequence of calls the shim already had
/// (shell_entityapi.cpp until plan_server_native_v2 T4d). Each keeps the native
/// body's order and values; native carries the calls out.
/// </summary>
internal static class PlayerSequences
{
    /// <summary>
    /// Spawn IS the respawn on the hierarchy -- the same spawn-point ask, the
    /// same managed OnSpawn resetting the game state -- once the player has
    /// stopped observing. A handle that is not a player is left alone.
    /// </summary>
    public static void Respawn<T>(T slots, EntityHandle player) where T : IPlayerSequenceSlots
    {
        if (!slots.EntityIsPlayer(player))
            return;

        slots.PlayerSetObserverMode(player, ObserverMode.None);
        slots.SpawnEntity(player);
    }

    /// <summary>
    /// A player's eye, which is what the stock weapon code fires from (the
    /// origin would put every shot at the feet); nowhere for anything else.
    /// </summary>
    public static Vector ShootOrigin<T>(T slots, EntityHandle player) where T : IPlayerSequenceSlots
        => slots.EntityIsPlayer(player) ? slots.EntityGetEyePosition(player) : default;

    /// <summary>
    /// From the eye along the view angles (the engine's v_angle, not the body's
    /// pitchless angles) for the range, as a SHOT trace skipping the shooter --
    /// exactly as the stock weapon code traces, so a player cannot shoot
    /// themselves and cover that stops bullets stops this. What it hit, and
    /// where; a handle that is not a player aims at nothing.
    /// </summary>
    public static EntityHandle Aim<T>(T slots, EntityHandle player, float range, out Vector hitPoint)
        where T : IPlayerSequenceSlots
    {
        hitPoint = default;

        if (!slots.EntityIsPlayer(player))
            return EntityHandle.None;

        Vector start = slots.EntityGetEyePosition(player);
        Vector end = start + MoveVec.AngleForward(slots.EntityGetEyeAngles(player)) * range;

        TraceResult tr = slots.TraceLine(start, end, TraceMask.Shot, player);
        hitPoint = tr.End;
        return tr.Entity;
    }
}
