namespace SourceSharp.Ai.Server.Chat;

/// <summary>
/// What the dev API reports of the bots' chat (<c>/ai/chat</c>, the chat part
/// of <c>/ai/counters</c>, plan_hl2mp_ai §3.6): the game's chat answers it, so
/// the routes name no game's chat.
/// </summary>
public interface IBotChatReport
{
    ChatCounters Counters { get; }

    /// <summary><c>/ai/chat</c>: the counters and each bot's last lines.</summary>
    string Json();
}
