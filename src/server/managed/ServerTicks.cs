namespace SourceSharp.Server;

/// <summary>
/// How many server frames this module has run, for <c>cs_status</c> and the
/// dev API's status line.
/// </summary>
public interface IServerTicks
{
    /// <summary>Frames run since the module loaded.</summary>
    int Count { get; }
}

/// <summary>
/// The server module's frame counter, registered in its container.
///
/// <para>
/// It was a static on <c>ServerModule</c> with a static accessor, so that a
/// command class could read it without a reference to the module. A command
/// takes this through its constructor instead. Never reset, as the static
/// never was: its lifetime is the module's load context, which is what the
/// static's was too.
/// </para>
/// </summary>
public sealed class ServerTicks : IServerTicks
{
    /// <inheritdoc/>
    public int Count { get; private set; }

    /// <summary>One more frame. Called by the module's Frame, and nothing else.</summary>
    public void Advance() => Count++;
}
