//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Voice;

/// <summary>VoiceSubtitle's payload (shared/multiplay_gamerules.cpp:1833-1837):
/// the speaker's entity index, then the menu and item whose line was spoken —
/// the client's subtitle panel looks the words up in its own half of
/// voicecommands.txt and its localization.</summary>
public partial struct TFVoiceSubtitle
{
    public partial byte Speaker { get; set; }

    public partial byte Menu { get; set; }

    public partial byte Item { get; set; }
}

/// <summary>What the subtitle's arrival tells the client (stock's chat-area
/// subtitle print off the same three bytes).</summary>
public interface ITFVoiceSubtitleHook
{
    void OnVoiceSubtitle(TFVoiceSubtitle subtitle);
}

/// <summary>The voice subtitle: stock TF2's own "VoiceSubtitle" name
/// (shared/multiplay_gamerules.cpp:1833) — keyed under the module because this
/// build's stock-name set is HL2DM's (UserMessageKey.StockNames).</summary>
[UserMessage("VoiceSubtitle")]
public sealed class TFVoiceSubtitleMessage : IUserMessage<TFVoiceSubtitle>
{
    private readonly IHooks<ITFVoiceSubtitleHook>? _hooks;

    /// <summary>A handler whose subtitles go nowhere: the no-container fallback's (and the server's).</summary>
    public TFVoiceSubtitleMessage()
    {
    }

    /// <summary>The client's container's: the hook chain the subtitle dispatches to.</summary>
    [Inject]
    public TFVoiceSubtitleMessage(IHooks<ITFVoiceSubtitleHook>? hooks) => _hooks = hooks;

    public void OnReceived(TFVoiceSubtitle message) =>
        _hooks?.Each(nameof(ITFVoiceSubtitleHook.OnVoiceSubtitle), hook => hook.OnVoiceSubtitle(message));
}
