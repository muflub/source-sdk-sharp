//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_hudhint: the line of instruction at the bottom of the screen, and the
/// only one of the three text entities that can be taken back down again.
///
/// <para>
/// What separates a hint from a message is that it is about the CONTROLS. Hint
/// strings are written with %+use%-style tokens in them, and only the client can
/// say what key that is — so the substitution belongs on the far side and this
/// half sends the string as the map wrote it. Both inputs go through the same
/// user message; hiding is showing an empty one, which is stock and is why there
/// is no separate hide message to declare.
/// </para>
///
/// <para>
/// The client half is part of the port. Stock's reader is
/// <c>CHudHintKeyDisplay</c> in <c>hud_hintdisplay.cpp</c>, which this mod took
/// out of the client build when the HUD moved to C#, so the message has had no
/// reader here at all. It has one now, in <c>HudHints</c>.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvHudhint)]
public sealed class EnvHudHint : ScriptedEntity
{
    public EnvHudHint(IUserMessageSender messages)
    {
        Messages = messages;
    }

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IUserMessageSender Messages;
    /// <summary>Everyone sees it, rather than only whoever set it off.</summary>
    private const int FlagAllPlayers = 1;

    /// <summary>What a map may set on this entity. See <see cref="EnvHudHintVars"/>.</summary>
    [ScriptVars] public EnvHudHintVars Vars = new();

    [ScriptedEvent("ShowHudHint")]
    private void OnShowHudHint(InputData input) => Send(input.Activator, Vars.Message);

    /// <summary>
    /// Taking the hint down is showing an empty one. Stock does exactly this and
    /// it is worth keeping rather than inventing a second message: the client
    /// then has one rule — whatever arrived last is what is on screen.
    /// </summary>
    [ScriptedEvent("HideHudHint")]
    private void OnHideHudHint(InputData input) => Send(input.Activator, "");

    private void Send(EntityHandle activator, string text)
    {
        int player = PlayerFor(activator);

        if (player == NoRecipient)
            return;

        // KeyHintText carries a LIST of hints, and stock's entity always sends
        // exactly one. Kept rather than flattened, because the message is the
        // engine's and other senders use the list.
        if (!Messages.Send(new KeyHintText { Hints = [text] }, player, reliable: true))
            Log.Warning($"[SourceSharp] env_hudhint '{Name}': hint does not fit a user message; not sent\n");
    }

    /// <summary>Not zero: zero is everybody.</summary>
    private const int NoRecipient = -1;

    /// <summary>
    /// Who sees it, where zero is everybody.
    ///
    /// Stock falls back to <c>UTIL_GetLocalPlayer</c> when the activator is not
    /// a player, which is NULL on a multiplayer server — so stock reaches nobody
    /// in that case too. The difference is that this says so.
    /// </summary>
    private int PlayerFor(EntityHandle activator)
    {
        if ((SpawnFlags & FlagAllPlayers) != 0)
            return 0;

        if (Entities.IsPlayer(activator))
            return Entities.GetIndex(activator);

        Log.Warning($"[SourceSharp] env_hudhint '{Name}': nothing to show the hint to; " +
                        "whatever fired this was not a player and the All Players flag is not set\n");

        return NoRecipient;
    }
}

/// <summary>
/// The map keys <see cref="EnvHudHint"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvHudHintVars
{
    /// <summary>
    /// The hint. Plain text, a "#Token" for the localisation tables, or either
    /// with %binding% tokens in it — all three are resolved on the client.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
