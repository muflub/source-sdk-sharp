namespace SourceSharp.Abi;

/// <summary>
/// Lifts one live entity's networked blob off the game thread.
///
/// <para>
/// Kept apart from <see cref="NetSchema"/> on purpose: everything there is a
/// pure function of metadata and bytes and is tested without a game, and this is
/// the one part that can only be right in a running one. Mixing the two would
/// have meant the decode could not be tested at all.
/// </para>
///
/// <para>
/// The walk is over both hierarchies, because half this mod's classes are not
/// CBaseEntity and an iteration that stopped at the SDK's list would answer for
/// some classes and not others.
/// </para>
///
/// <para>
/// READING NO LONGER COSTS THE ENTITY ANYTHING, and it used to. Fetching a
/// standalone entity's blob called <c>NetworkStateChanged</c> native-side — a
/// read marked the edict dirty, deliberately, because C# wrote through a bare
/// pointer and there was no setter to hang the notification off. So polling this
/// endpoint kept its entity in every delta compare for as long as somebody
/// watched it, and <c>SV_PackEntity</c> skips the encode entirely for an entity
/// whose edict is unchanged, which is exactly what that gave up. Step 5 of the
/// payload plan removed it: a bound state announces its own writes through the
/// control block and native drains them once a frame, so this route is now a
/// fetch and an observer does not change what it observes.
/// </para>
///
/// <para>
/// An instance over ONE side's entity and networking areas (plan_abi_hotreload
/// D8): it was a static class over the static <c>Engine</c>, which walked
/// whichever side was active. The server's composition builds one.
/// </para>
/// </summary>
public sealed class NetPayloadReader
{
    private readonly IEngineEntities _entities;
    private readonly IEngineNetworking _networking;

    public NetPayloadReader(IEngineEntities entities, IEngineNetworking networking)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(networking);
        _entities = entities;
        _networking = networking;
    }

    /// <summary>
    /// Every entity, whichever hierarchy it is on. "*" is what the engine's own
    /// matcher treats as everything, on both sides of the walk.
    /// </summary>
    private const string Everything = "*";

    /// <summary>
    /// One entity's blob by index, copied.
    ///
    /// Must run on the game thread. Returns
    /// <see cref="NetEntityBlob.Missing"/> for an index nothing answers to,
    /// which is a real answer — an index is reused the moment an entity is
    /// freed, so "nothing there" and "something else there" are both worth
    /// telling apart from a blob of zeroes.
    /// </summary>
    public NetEntityBlob Read(int index)
    {
        if (index < 0)
            return NetEntityBlob.Missing(index);

        foreach (var entity in _entities.FindAllByClassname(Everything))
        {
            if (_entities.GetIndex(entity) != index)
                continue;

            // ToArray rather than the span itself: this is borrowed native
            // memory whose lifetime is the entity's, and the reply is written
            // after the frame it was taken on.
            byte[] blob = _networking.GetEntityBlob(entity).ToArray();

            // Asked here rather than by the caller, because it can only be asked
            // on the game thread and only while the handle still resolves — the
            // same two constraints the blob copy above exists for.
            var agreement = _networking.CheckPayloadAgreement(entity);

            return new NetEntityBlob(true, index, entity.Value, _entities.GetClassName(entity), blob, agreement);
        }

        return NetEntityBlob.Missing(index);
    }
}
