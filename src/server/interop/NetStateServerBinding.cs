namespace SourceSharp.Abi;

/// <summary>
/// The SERVER's arm of binding a net state: acquiring the entity's blob, a
/// server-only slot (EntityBindBlob), and pointing the state at it.
///
/// <para>
/// P1 run 3, the integrator's ruling (a): the bind was a body of the shared
/// <see cref="NetState"/>, so the shared assembly named a server-only slot
/// that the client never reaches (it binds local storage,
/// <see cref="NetStateBinding.AttachLocal"/>). The acquisition lives here, on
/// the server's side; <see cref="NetState.BindTo"/> keeps the checks and the
/// fields.
/// </para>
/// </summary>
public static class NetStateServerBinding
{
    /// <summary>
    /// Points this state at an entity's blob. Called by the registry when the
    /// entity is created, which is before anything can set a property on it.
    ///
    /// <para>
    /// Throws <see cref="BlobTooShortException"/> when the entity's blob cannot
    /// hold this state's fields — including the case where the entity has no
    /// blob at all, which is the same fact with a length of zero. That check was
    /// once per write and is now once per bind, and it had to move: a per-write
    /// check could refuse the write, whereas a cached pointer into a short blob
    /// is an overrun. Refusing to bind is the only shape that cannot be wrong
    /// later.
    /// </para>
    ///
    /// <para>
    /// WITH NO ENGINE AT ALL the state takes local storage instead, and that is
    /// not the quiet fallback this part exists to delete. The two are different
    /// facts: "this entity offers no blob" is a server bug and throws, while
    /// "there is no game" is the test harness, which spawns entities and drives
    /// their rules with no native side under them. A state that threw there
    /// would make every entity test a networking test, and the thing it would be
    /// reporting is the absence of an engine — which the caller already knows.
    /// </para>
    /// </summary>
    /// <param name="state">The state to bind.</param>
    /// <param name="engine">The composition's engine the entity lives on.</param>
    /// <param name="handle">The entity.</param>
    public static void Bind(this NetState state, ModuleEngine engine, EntityHandle handle)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(engine);
        state.Bind(engine.Api, engine.Attachment.Areas.Require<INetworkServerArea>(), handle);
    }

    /// <inheritdoc cref="Bind(NetState, ModuleEngine, EntityHandle)"/>
    /// <param name="state">The state to bind.</param>
    /// <param name="side">The side the entity lives on: its registry's own.</param>
    /// <param name="network">That side's network area, whose slot binds the blob.</param>
    /// <param name="handle">The entity.</param>
    internal static void Bind(this NetState state, EngineSide side, INetworkServerArea network, EntityHandle handle)
    {
        if (!side.Attachment.IsInitialized)
        {
            state.BindLocal();
            return;
        }

        state.BindTo(side.BindEntityBlob(network, handle), side, handle);
    }

    /// <summary>
    /// Creates any missing state objects and points each at the entity's blob
    /// on <paramref name="side"/>, so a property set writes into it. The side is
    /// the registry's own (plan_abi_hotreload D8: the bind asked the static
    /// Engine).
    ///
    /// <para>
    /// BOTH ARMS GIVE STORAGE; they differ only in whose it is. The server binds
    /// to the entity's own blob, which is the memory the SendProps read, so a
    /// property set IS the update. The client has no writable blob of the
    /// entity's — what it gets is a borrowed one per update — so
    /// <see cref="AttachLocal"/> gives it a pinned buffer of its own and the
    /// update is memcpy'd into it.
    /// </para>
    ///
    /// <para>
    /// What neither gives is nothing. A state with no storage at all throws on
    /// every access now (Part 9), because there is nowhere for a write to go and
    /// nothing for a read to return — and these are the calls that make sure no
    /// state reaches an entity class in that condition.
    /// </para>
    ///
    /// <para>
    /// Each returns the live states, so a caller that has to decode into them
    /// every update does not have to reflect again to find them.
    /// </para>
    /// </summary>
    internal static NetState[] AttachBound(object instance, EntityHandle handle, EngineSide side,
                                           INetworkServerArea network, NetStateFields known)
    {
        var fields = known.FieldsOn(instance.GetType());
        if (fields.Length == 0)
            return Array.Empty<NetState>();

        var states = new NetState[fields.Length];

        for (int i = 0; i < fields.Length; i++)
        {
            if (NetStateBinding.StateIn(instance, fields[i], known.Log) is not { } state)
                continue;

            state.Bind(side, network, handle);
            states[i] = state;
        }

        return states;
    }
}
