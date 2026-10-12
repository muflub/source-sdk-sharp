using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// The client's half of the stock user-message hooks: each member hands a
/// parsed payload's values to the client-only shim slot that carries the
/// hook's effect out (the user-message payload model), or, where the effect is
/// managed, to its service: Rumble's mixer is ClientRumble (lane c6-input).
/// Registered by the client module for the handlers in SourceSharp.Shared.
/// </summary>
public sealed class ClientMessageEffects(IEngineHud hud, Func<Vgui.IUiRuntime?>? ui = null, Func<Voice.VoiceStatus?>? voice = null,
                                          Action<Rumble>? rumble = null, Func<Render.ViewEffectsHost?>? viewEffects = null,
                                          IGameClock? clock = null)
    : IClientMessageEffects
{
    /// <summary>stock's __MsgFunc_Shake: the managed view effects (C7).</summary>
    public void Shake(Shake message)
        => viewEffects?.Invoke()?.Shake((ShakeCommand)message.Command, message.Amplitude, message.Frequency, message.Duration);

    /// <summary>stock's __MsgFunc_Fade: the managed view effects, at the client's current time (C7).</summary>
    public void Fade(Fade message)
        => viewEffects?.Invoke()?.Fade(message.Duration, message.HoldTime, (FadeFlags)message.Flags,
                                        (byte)message.Color.R, (byte)message.Color.G, (byte)message.Color.B, (byte)message.Color.A, clock?.Now ?? 0f);

    /// <summary>stock's RumbleEffect: the managed mixer's StartEffect.</summary>
    public void Rumble(Rumble message) => rumble?.Invoke(message);

    /// <summary>A panel the UI has a screen for exists whether or not the stock viewport made one (plan_client_ui U3).</summary>
    public ViewportFacts ViewportQuery(string panel)
    {
        var facts = hud.ViewportQuery(panel);
        return ui?.Invoke() is { } runtime && runtime.Answers(panel) ? facts | ViewportFacts.PanelExists : facts;
    }

    /// <summary>
    /// A panel the UI has a screen for (plan_client_ui U3) is the UI's, with its
    /// data; the rest go to the stock viewport.
    /// </summary>
    public void ViewportShow(string panel, bool show, IReadOnlyList<KeyValuePair<string, string>> data, ViewportActions actions)
    {
        if (ui?.Invoke() is { } host && host.TakePanel(panel, show && (actions & ViewportActions.Show) != 0, data))
            return;
        hud.ViewportShow(panel, show, data, actions);
    }

    /// <summary>VoiceMask: the server's masks, to the managed voice manager (stock CVoiceStatus::HandleVoiceMaskMsg).</summary>
    public void VoiceMasks(VoiceMask message)
        => voice?.Invoke()?.ServerMasks(message.Audible0, message.Banned0, message.Audible1, message.Banned1, message.ModEnable);

    /// <summary>RequestState: the voice manager tells the server everything (stock HandleReqStateMsg).</summary>
    public void VoiceRequestState() => voice?.Invoke()?.ServerRequestedState();
}
