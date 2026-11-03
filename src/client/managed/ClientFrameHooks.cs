namespace SourceSharp.Client;

/// <summary>
/// One per-frame client think of game logic (stock's entity
/// <c>ClientThink</c>): a service that watches the clock joins this chain by
/// registration and the client module steps every member once a frame, after
/// its own per-frame work (<c>ClientModule.Frame</c>). The hooks resolve
/// lazily, so the empty chain when no mod joins costs nothing.
/// </summary>
public interface IClientFrameThink
{
    /// <summary>Stock's entity <c>ClientThink</c>, once per client frame.</summary>
    /// <param name="frameTime">Seconds this frame took.</param>
    void ClientThink(float frameTime);
}
