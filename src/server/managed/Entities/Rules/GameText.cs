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
/// game_text: the only way this mod has of saying a sentence to a player.
///
/// It matters more here than the class suggests. A mod with no NPCs and no
/// custom content has nothing else that talks — <c>point_message</c>, the
/// obvious alternative, calls <c>UTIL_GetLocalPlayer</c>, which is NULL in
/// multiplayer and asserts under <c>developer 1</c>. So every description in
/// <c>ss_sandbox</c> is one of these, and the map is the heaviest user of this
/// class in the tree.
///
/// <para>
/// The client half was already managed and has been since the HUD moved:
/// <c>HudMessages</c> holds the channels, the fades and the placement. What was
/// missing was any way for managed SERVER code to send a user message at all —
/// the mod could receive every message it declared and send exactly one. So the
/// entity that arrives last is the one that needed the general path built, and
/// with it both ends of the wire are C# and the native middle is the engine's
/// own transport.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.GameText)]
public sealed class GameText : ScriptedEntity
{
    public GameText(IUserMessageSender messages)
    {
        Messages = messages;
    }

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IUserMessageSender Messages;
    /// <summary>What a map may set on this entity. See <see cref="GameTextVars"/>.</summary>
    [ScriptVars] public GameTextVars Vars = new();

    private const int AllPlayersFlag = 1;

    [ScriptedEvent("Display")]
    private void OnDisplay(InputData input) => Display(input.Activator);

    [ScriptedEvent("Use")]
    private void OnUsed(InputData input) => Display(input.Activator);

    private void Display(EntityHandle activator)
    {
        int player = PlayerFor(activator);

        // Nobody to tell. Guarded HERE rather than passed down, because the send
        // call reads zero as EVERYBODY -- so a "no recipient" sentinel that
        // reached it would put a message meant for one player on every screen in
        // the server. The two are one integer apart and the failure is silent.
        if (player == NoRecipient)
            return;

        var (r1, g1, b1, a1) = ParseColor(Vars.Color, 100, 100, 100, 255);
        var (r2, g2, b2, a2) = ParseColor(Vars.Color2, 240, 110, 0, 255);

        // UTIL_HudMessage's fields; the payload's declaration is their order.
        var message = new HudMsg
        {
            Channel  = (byte)(Vars.Channel & 0xFF),
            X        = Vars.X,
            Y        = Vars.Y,
            Color    = new Color32(r1, g1, b1, a1),
            Color2   = new Color32(r2, g2, b2, a2),
            Effect   = (byte)Vars.Effect,
            FadeIn   = Vars.FadeIn,
            FadeOut  = Vars.FadeOut,
            HoldTime = Vars.Hold,
            FxTime   = Vars.FxTime,
            Message  = Vars.Message,
        };

        // Reliable: this is a one-off a map fired, and dropping it loses the
        // event entirely rather than being superseded by the next one. Not
        // sent rather than sent short when it does not fit.
        if (!Messages.Send(message, player, reliable: true))
            Log.Warning($"[SourceSharp] game_text '{Name}': message does not fit a user message; not sent\n");
    }

    /// <summary>Not zero: zero is everybody. See <see cref="Display"/>.</summary>
    internal const int NoRecipient = -1;

    /// <summary>
    /// Which player index gets it, where zero is everybody.
    ///
    /// Stock has a third case for singleplayer — no activator means the local
    /// player — which cannot arise here: this build has no singleplayer, and
    /// <c>UTIL_GetLocalPlayer</c> is exactly the call that makes point_message
    /// assert in multiplayer. A map that fires this from a timer with the flag
    /// unset therefore reaches nobody, which is stock's behaviour on a
    /// multiplayer server too.
    /// </summary>
    private int PlayerFor(EntityHandle activator)
    {
        if ((SpawnFlags & AllPlayersFlag) != 0)
            return 0;

        return Entities.IsPlayer(activator) ? Entities.GetIndex(activator) : NoRecipient;
    }

    /// <summary>
    /// Four numbers in a string, with each component falling back on its own.
    ///
    /// Per-component rather than all-or-nothing because a map that wrote three
    /// numbers meant three: taking the whole key as malformed would throw away
    /// the colour it did specify, and the alpha is the one most often left off.
    /// </summary>
    internal static (byte R, byte G, byte B, byte A) ParseColor(
        string text, byte r, byte g, byte b, byte a)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Span<byte> fallback = stackalloc byte[4] { r, g, b, a };

        for (int i = 0; i < 4 && i < parts.Length; i++)
        {
            if (int.TryParse(parts[i], System.Globalization.NumberStyles.Integer,
                             System.Globalization.CultureInfo.InvariantCulture, out int v))
            {
                fallback[i] = (byte)Math.Clamp(v, 0, 255);
            }
        }

        return (fallback[0], fallback[1], fallback[2], fallback[3]);
    }
}

/// <summary>
/// The map keys <see cref="GameText"/> declares.
/// </summary>
[ScriptVars]
public partial class GameTextVars
{
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Which of the six slots to occupy. A channel holds ONE message and a new
    /// one replaces what was there, which is what lets a map write three lines
    /// by sending three messages.
    /// </summary>
    public int Channel { get; set; }

    /// <summary>
    /// Position, 0..1 across the screen. Negative centres on that axis, which is
    /// the convention the client half already implements.
    /// </summary>
    public float X { get; set; } = -1f;

    public float Y { get; set; } = -1f;

    public int Effect { get; set; }

    public float FadeIn { get; set; } = 1.5f;

    public float FadeOut { get; set; } = 0.5f;

    [VarName("holdtime")]
    public float Hold { get; set; } = 1.2f;

    /// <summary>How long the scan/flicker effect takes. Only read for effect 1 and 2.</summary>
    public float FxTime { get; set; } = 0.25f;

    /// <summary>
    /// Colours as the map writes them: four numbers in a string.
    ///
    /// A string rather than a typed field because a Vector holds three
    /// components and a colour here has four — the alpha is the half a mapper
    /// most often changes, and dropping it would silently make every message
    /// opaque.
    /// </summary>
    public string Color { get; set; } = "100 100 100 255";

    public string Color2 { get; set; } = "240 110 0 255";
}
