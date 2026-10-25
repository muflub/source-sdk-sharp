using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// <see cref="IUserMessageOutbox"/> over the server's routed IShimUserMessages
/// slot (plan_side_isolation §2.2a): one crossing per message.
/// </summary>
public sealed unsafe class UserMessageOutbox : IUserMessageOutbox
{
    private readonly IUserMessagesArea _messages;

    internal UserMessageOutbox(IUserMessagesArea messages)
    {
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
    }

    /// <summary>The outbox over <paramref name="engine"/>'s user-message area, taken here once.</summary>
    public static UserMessageOutbox For(ModuleEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return new UserMessageOutbox(engine.Attachment.Areas.Require<IUserMessagesArea>());
    }

    /// <inheritdoc/>
    public bool Send(int id, ReadOnlySpan<byte> payload, int player, bool reliable)
    {
        fixed (byte* p = payload)
            return _messages.UserMessageSend(id, p, payload.Length, player, reliable ? 1 : 0) != 0;
    }
}
