namespace SourceSharp.Abi;

/// <summary>
/// The server's answers to <see cref="IPlayerSequenceSlots"/> (plan_server_native_v2
/// T4d), over the areas the slots read.
///
/// <para>
/// A value made per call rather than a member of <see cref="EngineSide"/>:
/// the side keeps no areas of its own (plan_shim_areas_split), so the binding
/// that runs a sequence hands over the areas it was given. Each member is the
/// side's own method of the same name, which is what the bindings' own
/// members call too, so a sequence and a direct call cannot disagree.
/// </para>
/// </summary>
internal readonly struct ServerPlayerSequenceSlots(EngineSide side,
                                                   IEntitiesServerArea entitiesServerArea,
                                                   ITransformArea transformArea,
                                                   IEntityLifecycleArea entityLifecycleArea,
                                                   IPlayersArea playersArea) : IPlayerSequenceSlots
{
    public bool EntityIsPlayer(EntityHandle h) => side.EntityIsPlayer(entitiesServerArea, h);

    public Vector EntityGetEyePosition(EntityHandle h) => side.EntityGetEyePosition(h);

    public Vector EntityGetEyeAngles(EntityHandle h) => side.EntityGetEyeAngles(transformArea, h);

    public TraceResult TraceLine(Vector start, Vector end, TraceMask mask, EntityHandle ignore)
        => side.TraceLine(start, end, mask, ignore);

    public void PlayerSetObserverMode(EntityHandle player, ObserverMode mode)
        => side.PlayerSetObserverMode(playersArea, player, mode);

    public void SpawnEntity(EntityHandle entity) => side.SpawnEntity(entityLifecycleArea, entity);
}
