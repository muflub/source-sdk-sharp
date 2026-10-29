using System;

namespace SourceSharp.Abi;

/// <summary>
/// The skin a server entity is drawn with, sent to every client. Not named
/// <c>IEngine*</c>: it is not an engine area, it is the server's own write
/// into an entity's engine block.
/// </summary>
public interface IServerSkins
{
    /// <summary>
    /// Which skin of its model <paramref name="entity"/> is drawn with. Sent as
    /// the m_nSkin prop (standalone_nettable.h); the client's GetSkin answers it.
    /// </summary>
    void SetSkin(EntityHandle entity, int skin);

    /// <summary>
    /// Which bodygroup combination <paramref name="entity"/> is drawn with
    /// (CBaseAnimating's m_nBody). Sent as the m_nBody prop; the client's
    /// GetBody answers it.
    /// </summary>
    void SetBody(EntityHandle entity, int body);
}

/// <summary>
/// <see cref="IServerSkins"/> over the server's engine blocks: the skin goes
/// into the block's Client.Skin (<see cref="NetworkedSkin"/>) and that one
/// offset is marked changed, so the delta carries the one prop. Stock sent
/// CBaseAnimating's m_nSkin the same way; before this a skin chosen on the
/// server (env_beverage's can, a gib) reached no client (2026-10-19).
/// </summary>
public sealed class ServerSkins : IServerSkins
{
    private readonly EngineSide _side;
    private readonly INetworkServerArea _network;

    internal ServerSkins(EngineSide side, INetworkServerArea network)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _network = network ?? throw new ArgumentNullException(nameof(network));
    }

    /// <summary>Over <paramref name="engine"/>'s side and its network area, taken here once.</summary>
    public static ServerSkins For(ModuleEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return new ServerSkins(engine.Api, engine.Attachment.Areas.Require<INetworkServerArea>());
    }

    /// <inheritdoc/>
    public void SetSkin(EntityHandle entity, int skin)
    {
        if (NetworkedSkin.Write(_side.GetEntityBlock(_network, entity), skin))
            _side.MarkEntityBlockChanged(_network, entity, NetworkedSkin.Offset);
    }

    /// <inheritdoc/>
    public void SetBody(EntityHandle entity, int body)
    {
        if (NetworkedBody.Write(_side.GetEntityBlock(_network, entity), body))
            _side.MarkEntityBlockChanged(_network, entity, NetworkedBody.Offset);
    }
}
