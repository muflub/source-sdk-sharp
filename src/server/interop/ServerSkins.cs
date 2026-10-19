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

    public ServerSkins(ModuleEngine engine)
        => _side = (engine ?? throw new ArgumentNullException(nameof(engine))).Api;

    /// <inheritdoc/>
    public void SetSkin(EntityHandle entity, int skin)
    {
        if (NetworkedSkin.Write(_side.GetEntityBlock(entity), skin))
            _side.MarkEntityBlockChanged(entity, NetworkedSkin.Offset);
    }
}
