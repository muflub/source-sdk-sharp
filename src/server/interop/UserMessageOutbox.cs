using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// <see cref="IUserMessageOutbox"/> over the server's routed IShimUserMessages
/// slot (plan_side_isolation §2.2a): one crossing per message.
/// </summary>
public sealed unsafe class UserMessageOutbox : IUserMessageOutbox
{
    private readonly EngineSide _side;
    private readonly UserMessagesArea _messages;

    public UserMessageOutbox(ModuleEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _side = engine.Api;
        _messages = _side.Areas.Require<UserMessagesArea>();
    }

    /// <inheritdoc/>
    public bool Send(int id, ReadOnlySpan<byte> payload, int player, bool reliable)
    {
        if (!_side.IsInitialized || !_side.ShimsAttached)
            return false;

        fixed (byte* p = payload)
            return _messages.UserMessageSend(id, p, payload.Length, player, reliable ? 1 : 0) != 0;
    }
}
