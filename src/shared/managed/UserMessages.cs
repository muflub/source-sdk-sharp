//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

// The standard Source user message set, declared in shared code so client and
// server derive the same ordering from the same metadata.
//
// These are protocol, not HL2 game content: client_base and server_base send
// them themselves. Leaving one out is fatal in a non-obvious way — CBasePlayer
// spawn sends "Fade", and an unregistered message aborts the spawn sequence with
// only "UserMessageBegin: Unregistered message 'Fade'" in the console.
//
// Size is the payload byte count; -1 means variable length.
//
// DECLARATION ORDER IN THIS FILE IS THE WIRE PROTOCOL. A user message travels as
// an index into the registered list, so reordering these classes changes what
// every index means. Add new messages at the end.

//
// The six haptics messages, FIRST, because that is where they have always been.
//
// Lifted from RegisterHapticMessages() -- the six names, in that order, with
// those sizes, byte for byte. `public/haptics/haptic_msgs.cpp:28` is the
// original and `HookHapticMessages()` at :113 hooks all six on the client from
// cdll_client_int.cpp:1130, which runs AFTER SourceSharp_ClientEarlyInit() at
// :1075 registers this list.
//
// Neither binary compiles haptic_msgs.cpp's senders or its hooks, so the
// payloads below are stock's layouts, declared so every message is managed
// end to end (user ruling 2026-10-04: all user messages in managed code).
//
// They come first and they cannot be appended instead. A user message travels
// as an INDEX into the registered list; these six occupied 0-5 when C++
// registered them before anything else, so putting them anywhere but the head
// renumbers every message after them and the client misreads all of them.
//
// -1 is a variable-length payload; 0 is an empty one.
//

/// <summary>SPHapWeapEvent's payload: the weapon activity (haptic_msgs.cpp).</summary>
public partial struct SPHapWeapEvent
{
    public partial int Activity { get; set; }
}

[UserMessage("SPHapWeapEvent", Stock = true, Size = 4)]
public sealed class SPHapWeapEventMessage : IUserMessage<SPHapWeapEvent>
{
    /// <summary>The last one that arrived.</summary>
    public SPHapWeapEvent Last { get; private set; }

    public void OnReceived(SPHapWeapEvent message) => Last = message;
}

/// <summary>HapDmg's payload: where the damage came from, how much, and its type.</summary>
public partial struct HapDmg
{
    public partial float Pitch { get; set; }
    public partial float Yaw { get; set; }
    public partial float Damage { get; set; }
    public partial int DamageType { get; set; }
}

[UserMessage("HapDmg", Stock = true)]
public sealed class HapDmgMessage : IUserMessage<HapDmg>
{
    /// <summary>The last one that arrived.</summary>
    public HapDmg Last { get; private set; }

    public void OnReceived(HapDmg message) => Last = message;
}

/// <summary>HapPunch's payload: the punch angle.</summary>
public partial struct HapPunch
{
    public partial float X { get; set; }
    public partial float Y { get; set; }
    public partial float Z { get; set; }
}

[UserMessage("HapPunch", Stock = true)]
public sealed class HapPunchMessage : IUserMessage<HapPunch>
{
    /// <summary>The last one that arrived.</summary>
    public HapPunch Last { get; private set; }

    public void OnReceived(HapPunch message) => Last = message;
}

/// <summary>HapSetDrag's payload: the drag.</summary>
public partial struct HapSetDrag
{
    public partial float Drag { get; set; }
}

[UserMessage("HapSetDrag", Stock = true)]
public sealed class HapSetDragMessage : IUserMessage<HapSetDrag>
{
    /// <summary>The last one that arrived.</summary>
    public HapSetDrag Last { get; private set; }

    public void OnReceived(HapSetDrag message) => Last = message;
}

/// <summary>HapSetConst's payload: a constant force, each axis in a short.</summary>
public partial struct HapSetConst
{
    public partial short X { get; set; }
    public partial short Y { get; set; }
    public partial short Z { get; set; }
}

[UserMessage("HapSetConst", Stock = true)]
public sealed class HapSetConstMessage : IUserMessage<HapSetConst>
{
    /// <summary>The last one that arrived.</summary>
    public HapSetConst Last { get; private set; }

    public void OnReceived(HapSetConst message) => Last = message;
}

/// <summary>HapMeleeContact's payload: nothing; the message is the event.</summary>
public partial struct HapMeleeContact
{
}

[UserMessage("HapMeleeContact", Stock = true, Size = 0)]
public sealed class HapMeleeContactMessage : IUserMessage<HapMeleeContact>
{
    /// <summary>The last one that arrived.</summary>
    public HapMeleeContact Last { get; private set; }

    public void OnReceived(HapMeleeContact message) => Last = message;
}

/// <summary>Geiger's payload: the counter's range.</summary>
public partial struct Geiger
{
    public partial byte Range { get; set; }
}

[UserMessage("Geiger", Stock = true, Size = 1)]
public sealed class GeigerMessage : IUserMessage<Geiger>
{
    /// <summary>The last reading that arrived.</summary>
    public Geiger Last { get; private set; }

    public void OnReceived(Geiger message) => Last = message;
}

/// <summary>Train's payload: the controls' position.</summary>
public partial struct Train
{
    public partial byte Position { get; set; }
}

[UserMessage("Train", Stock = true, Size = 1)]
public sealed class TrainMessage : IUserMessage<Train>
{
    /// <summary>The last position that arrived.</summary>
    public Train Last { get; private set; }

    public void OnReceived(Train message) => Last = message;
}

/// <summary>HudText's payload: the name of a titles.txt entry.</summary>
public partial struct HudText
{
    public partial string Name { get; set; }
}

/// <summary>
/// A line from titles.txt, by NAME, from env_message.
///
/// The whole payload is a name — the words, the colours, the position and the
/// timings all live in titles.txt on the client. That is the entire difference
/// between this and HudMsg, which carries every one of those itself, and it is
/// why this arrives as a lookup rather than as a sentence.
/// </summary>
[UserMessage("HudText", Stock = true)]
public sealed class HudTextMessage : IUserMessage<HudText>, IUserMessageEvent<string>
{
    /// <summary>Raised on the client when a map names a title to show.</summary>
    public event Action<string>? Fired;

    public void OnReceived(HudText message)
    {
        if (!string.IsNullOrEmpty(message.Name))
            Fired?.Invoke(message.Name);
    }
}

/// <summary>SayText's payload: who spoke (0 is the server), the line, and whether it is a player's chat.</summary>
public partial struct SayText
{
    public partial byte Client { get; set; }

    /// <summary>The line; longer than 250 is cut, which leaves stock's 256-byte read buffer room.</summary>
    [UserMessageString(MaxLength = 250)]
    public partial string Text { get; set; }

    public partial bool Chat { get; set; }
}

[UserMessage("SayText", Stock = true)]
public sealed class SayTextMessage : IUserMessage<SayText>
{
    /// <summary>The last line that arrived.</summary>
    public SayText Last { get; private set; }

    /// <summary>Each one as it arrives: the client's chat feed prints it (stock CBaseHudChat).</summary>
    public event Action<SayText>? Fired;

    public void OnReceived(SayText message)
    {
        Last = message;
        Fired?.Invoke(message);
    }
}

/// <summary>SayText2's payload: the speaker, whether it is chat, a localisation token and up to four parameters.</summary>
public partial struct SayText2
{
    public partial byte Client { get; set; }
    public partial bool Chat { get; set; }
    public partial string Name { get; set; }
    
    [UserMessageArray(MaxCount = 4)]
    public partial string[] Params { get; set; }
}

[UserMessage("SayText2", Stock = true)]
public sealed class SayText2Message : IUserMessage<SayText2>
{
    /// <summary>The last one that arrived.</summary>
    public SayText2 Last { get; private set; }

    /// <summary>Each one as it arrives: the client's chat feed prints it (stock CBaseHudChat).</summary>
    public event Action<SayText2>? Fired;

    public void OnReceived(SayText2 message)
    {
        Last = message;
        Fired?.Invoke(message);
    }
}

/// <summary>TextMsg's payload: where to print, the text, and up to four parameters for it.</summary>
public partial struct TextMsg
{
    /// <summary>Stock's HUD_PRINT* destination.</summary>
    public partial byte Destination { get; set; }
    public partial string Text { get; set; }

    [UserMessageArray(MaxCount = 4)]
    public partial string[] Params { get; set; }
}

[UserMessage("TextMsg", Stock = true)]
public sealed class TextMsgMessage : IUserMessage<TextMsg>
{
    /// <summary>The last message that arrived.</summary>
    public TextMsg Last { get; private set; }

    /// <summary>Each one as it arrives: the client's TextMessages prints it (stock MsgFunc_TextMsg).</summary>
    public event Action<TextMsg>? Fired;

    public void OnReceived(TextMsg message)
    {
        Last = message;
        Fired?.Invoke(message);
    }
}

/// <summary>
/// One game_text, on one channel: what to say, where, in what colour, and for
/// how long.
/// </summary>
public readonly record struct HudMessageArgs(
    int Channel,
    float X, float Y,
    Color32 Color, Color32 Color2,
    int Effect,
    float FadeIn, float FadeOut, float HoldTime, float FxTime,
    string Text);

/// <summary>
/// HudMsg's payload: UTIL_HudMessage's fields, in its order.
/// </summary>
public partial struct HudMsg
{
    public partial byte Channel { get; set; }
    public partial float X { get; set; }
    public partial float Y { get; set; }
    public partial Color32 Color { get; set; }
    public partial Color32 Color2 { get; set; }
    public partial byte Effect { get; set; }
    public partial float FadeIn { get; set; }
    public partial float FadeOut { get; set; }
    public partial float HoldTime { get; set; }
    public partial float FxTime { get; set; }
    public partial string Message { get; set; }
}

/// <summary>
/// Text a map asked for, via game_text.
///
/// This was a registered message with an empty handler for a long time, which
/// is the worst of both worlds: the server sent it happily and the client threw
/// it away, so a map could not say anything to a player and nothing reported
/// why. A truncated payload is not dispatched (the bridge refuses it), so a hold
/// time read past the end can no longer show text forever.
/// </summary>
[UserMessage("HudMsg", Stock = true)]
public sealed class HudMsgMessage : IUserMessage<HudMsg>, IUserMessageEvent<HudMessageArgs>
{
    /// <summary>Raised on the client when a map sends text.</summary>
    public event Action<HudMessageArgs>? Fired;

    public void OnReceived(HudMsg m)
        => Fired?.Invoke(new HudMessageArgs(m.Channel, m.X, m.Y, m.Color, m.Color2, m.Effect,
                                            m.FadeIn, m.FadeOut, m.HoldTime, m.FxTime, m.Message));
}

/// <summary>ResetHUD's payload: stock's one zero byte.</summary>
public partial struct ResetHud
{
    public partial byte Unused { get; set; }
}

/// <summary>Sent on every respawn; the client rebuilds its HUD from scratch.</summary>
[UserMessage("ResetHUD", Stock = true, Size = 1)]
public sealed class ResetHudMessage : IUserMessage<ResetHud>
{
    /// <summary>The last one that arrived.</summary>
    public ResetHud Last { get; private set; }

    public void OnReceived(ResetHud message) => Last = message;
}

/// <summary>GameTitle's payload: nothing; the message is the request.</summary>
public partial struct GameTitle
{
}

[UserMessage("GameTitle", Stock = true, Size = 0)]
public sealed class GameTitleMessage : IUserMessage<GameTitle>
{
    /// <summary>The last one that arrived.</summary>
    public GameTitle Last { get; private set; }

    public void OnReceived(GameTitle message) => Last = message;
}

/// <summary>ItemPickup's payload: the classname picked up.</summary>
public partial struct ItemPickup
{
    public partial string Item { get; set; }
}

[UserMessage("ItemPickup", Stock = true)]
public sealed class ItemPickupMessage : IUserMessage<ItemPickup>
{
    /// <summary>The last pickup that arrived.</summary>
    public ItemPickup Last { get; private set; }

    public void OnReceived(ItemPickup message) => Last = message;
}

/// <summary>ShowMenu's payload: the valid slots, how long to show it (-1 for ever, as a byte), whether more text follows, and the text.</summary>
public partial struct ShowMenu
{
    public partial ushort ValidSlots { get; set; }
    public partial byte DisplayTime { get; set; }
    public partial bool NeedMore { get; set; }
    public partial string Text { get; set; }
}

[UserMessage("ShowMenu", Stock = true)]
public sealed class ShowMenuMessage : IUserMessage<ShowMenu>
{
    /// <summary>The last one that arrived.</summary>
    public ShowMenu Last { get; private set; }

    public void OnReceived(ShowMenu message) => Last = message;
}

/// <summary>
/// What the client does with the messages whose stock hooks acted on native
/// client state: the view effects, rumble, the viewport and the voice status.
/// The handlers below decide; this carries out, over client-only shim slots.
/// Implemented in the client assembly (ClientMessageEffects) and handed to the
/// handlers by the client's container, so this shared assembly leans on no
/// client-only slot (SideAuditTests); the server never builds a handler.
/// </summary>
public interface IClientMessageEffects
{
    void Shake(Shake message);
    void Fade(Fade message);
    void Rumble(Rumble message);
    ViewportFacts ViewportQuery(string panel);
    void ViewportShow(string panel, bool show, IReadOnlyList<KeyValuePair<string, string>> data, ViewportActions actions);
    void VoiceMasks(VoiceMask message);
    void VoiceRequestState();
}

/// <summary>Shake's payload: stock's ScreenShake_t, as MsgFunc_Shake reads it.</summary>
public partial struct Shake
{
    /// <summary>A <see cref="ShakeCommand"/>, in a byte.</summary>
    public partial byte Command { get; set; }
    public partial float Amplitude { get; set; }
    public partial float Frequency { get; set; }
    public partial float Duration { get; set; }
}

[UserMessage("Shake", Stock = true, Size = 13)]
public sealed class ShakeMessage(IClientMessageEffects? effects) : IUserMessage<Shake>
{
    /// <summary>For a side that registers the declaration without dispatching it.</summary>
    public ShakeMessage() : this(null) { }

    /// <summary>The last shake that arrived.</summary>
    public Shake Last { get; private set; }

    /// <summary>MsgFunc_Shake: the view effects start, stop or change the shake.</summary>
    public void OnReceived(Shake message)
    {
        Last = message;
        effects?.Shake(message);
    }
}

/// <summary>
/// Fade's payload: stock's ScreenFade_t. The two times are unsigned 7.9 fixed
/// point (ScreenEffects.FixedUnsigned16).
/// </summary>
public partial struct Fade
{
    public partial ushort Duration { get; set; }
    public partial ushort HoldTime { get; set; }

    /// <summary>The <see cref="FadeFlags"/>, in a short.</summary>
    public partial short Flags { get; set; }
    public partial Color32 Color { get; set; }
}

/// <summary>Screen fade. Sent by CBasePlayer::Spawn — omitting it breaks spawning.</summary>
[UserMessage("Fade", Stock = true, Size = 10)]
public sealed class FadeMessage(IClientMessageEffects? effects) : IUserMessage<Fade>
{
    /// <summary>For a side that registers the declaration without dispatching it.</summary>
    public FadeMessage() : this(null) { }

    /// <summary>The last fade that arrived.</summary>
    public Fade Last { get; private set; }

    /// <summary>MsgFunc_Fade: the view effects fade the screen.</summary>
    public void OnReceived(Fade message)
    {
        Last = message;
        effects?.Fade(message);
    }
}

/// <summary>VGUIMenu's payload: the panel, whether to show it, and its data as names and values, pair by pair.</summary>
public partial struct VguiMenu
{
    public const int MaxKeys = 32;
    
    public partial string Name { get; set; }
    public partial bool Show { get; set; }
    
    [UserMessageArray(MaxCount = MaxKeys)]
    public partial string[] Keys { get; set; }
    
    [UserMessageArray(MaxCount = MaxKeys)]
    public partial string[] Values { get; set; }
}

[UserMessage("VGUIMenu", Stock = true)]
public sealed class VguiMenuMessage(IClientMessageEffects? effects, IEngineConsole? console, ILog? log) : IUserMessage<VguiMenu>
{
    /// <summary>For a side that registers the declaration without dispatching it.</summary>
    public VguiMenuMessage() : this(null, null, null) { }

    /// <summary>PANEL_INFO and PANEL_SCOREBOARD (viewport_panel_names.h).</summary>
    public const string InfoPanel = "info";
    public const string ScoresPanel = "scores";

    /// <summary>The last one that arrived.</summary>
    public VguiMenu Last { get; private set; }

    /// <summary>
    /// MsgFunc_VGUIMenu (clientmode_shared.cpp), every decision of it: the
    /// panel must exist; an MOTD URL must be http(s) or about:blank; the
    /// scoreboard takes a screenshot when hud_takesshots says so and always
    /// fires ds_screenshot; the info panel shows only while the client mode
    /// allows it. The viewport carries out what is asked
    /// (<see cref="IClientMessageEffects.ViewportShow"/>).
    /// </summary>
    public void OnReceived(VguiMenu message)
    {
        Last = message;
        if (effects is null)
            return;

        string panel = message.Name ?? string.Empty;
        var facts = effects.ViewportQuery(panel);
        if ((facts & ViewportFacts.PanelExists) == 0)
            return;

        var data = DataOf(message);
        var actions = ViewportActions.None;

        if (data.Count > 0)
        {
            if (string.Equals(panel, InfoPanel, StringComparison.OrdinalIgnoreCase)
                && IntOf(data, "type") == 2 && !IsAllowedMotdUrl(StringOf(data, "msg")))
            {
                log?.Warning($"Blocking MOTD URL '{StringOf(data, "msg")}'; must begin with 'http://' or 'https://' or be about:blank\n");
                return;
            }

            actions |= ViewportActions.SetData;
        }

        if (string.Equals(panel, ScoresPanel, StringComparison.OrdinalIgnoreCase))
        {
            if (IsOn(console?.FindConVarValue("hud_takesshots")))
                actions |= ViewportActions.ScreenShot;
            actions |= ViewportActions.ScreenShotEvent;
        }

        bool show = true;
        if (string.Equals(panel, InfoPanel, StringComparison.OrdinalIgnoreCase))
        {
            if ((facts & ViewportFacts.InfoPanelAllowed) == 0)
                show = false;
            else
                actions |= ViewportActions.InfoPanelDisplayed;
        }

        if (show)
            actions |= ViewportActions.Show;

        effects.ViewportShow(panel, message.Show, data, actions);
    }

    /// <summary>The MOTD whitelist: http://, https:// (case-sensitive, as Q_strncmp) or about:blank (any case).</summary>
    public static bool IsAllowedMotdUrl(string url)
        => url.StartsWith("http://", StringComparison.Ordinal)
           || url.StartsWith("https://", StringComparison.Ordinal)
           || string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The pairs as KeyValues would hold them: a name set twice keeps its
    /// last value, in its first place; a name with no value is dropped.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> DataOf(VguiMenu message)
    {
        var keys = message.Keys ?? [];
        var values = message.Values ?? [];
        var data = new List<KeyValuePair<string, string>>(Math.Min(keys.Length, values.Length));

        for (int i = 0; i < keys.Length && i < values.Length; i++)
        {
            int at = data.FindIndex(d => string.Equals(d.Key, keys[i], StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
                data[at] = new(data[at].Key, values[i]);
            else
                data.Add(new(keys[i], values[i]));
        }

        return data;
    }

    private static string StringOf(IReadOnlyList<KeyValuePair<string, string>> data, string key)
    {
        foreach (var (name, value) in data)
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                return value;
        return string.Empty;
    }

    private static int IntOf(IReadOnlyList<KeyValuePair<string, string>> data, string key)
        => int.TryParse(StringOf(data, key), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : 0;

    private static bool IsOn(string? value)
        => value is not null && (int.TryParse(value, out int n) ? n != 0
                                 : float.TryParse(value, System.Globalization.NumberStyles.Float,
                                                  System.Globalization.CultureInfo.InvariantCulture, out float f) && f != 0f);
}

/// <summary>Rumble's payload: the waveform, its data and its flags.</summary>
public partial struct Rumble
{
    public partial byte Waveform { get; set; }
    public partial byte Data { get; set; }
    public partial byte Flags { get; set; }
}

[UserMessage("Rumble", Stock = true, Size = 3)]
public sealed class RumbleMessage(IClientMessageEffects? effects) : IUserMessage<Rumble>
{
    /// <summary>For a side that registers the declaration without dispatching it.</summary>
    public RumbleMessage() : this(null) { }

    /// <summary>The last one that arrived.</summary>
    public Rumble Last { get; private set; }

    /// <summary>MsgFunc_Rumble: RumbleEffect.</summary>
    public void OnReceived(Rumble message)
    {
        Last = message;
        effects?.Rumble(message);
    }
}

/// <summary>Battery's payload: the suit's charge.</summary>
public partial struct Battery
{
    public partial short Value { get; set; }
}

[UserMessage("Battery", Stock = true, Size = 2)]
public sealed class BatteryMessage : IUserMessage<Battery>
{
    /// <summary>The last charge that arrived.</summary>
    public Battery Last { get; private set; }

    public void OnReceived(Battery message) => Last = message;
}

/// <summary>What the server sent with a Damage message.</summary>
public readonly record struct DamageArgs(int Armour, int Health, int DamageBits, Vector Origin = default);

/// <summary>
/// Damage's payload: armour taken, health taken, the damage bits, and where it
/// came from. Stock wrote the origin as a bit-packed coordinate; the payload
/// carries it as a <see cref="Vector"/>, which is what the declared 18 bytes
/// always were.
/// </summary>
public partial struct Damage
{
    public partial byte Armour { get; set; }
    public partial byte Health { get; set; }
    public partial int DamageBits { get; set; }
    public partial Vector Origin { get; set; }
}

[UserMessage("Damage", Stock = true, Size = 18)]
public sealed class DamageMessage : IUserMessage<Damage>, IUserMessageEvent<DamageArgs>
{
    /// <summary>Raised on the client when the local player takes damage.</summary>
    public event Action<DamageArgs>? Fired;

    public void OnReceived(Damage message)
        => Fired?.Invoke(new DamageArgs(message.Armour, message.Health, message.DamageBits, message.Origin));
}

/// <summary>VoiceMask's payload: per dword of players (VOICE_MAX_PLAYERS_DW == 2) the audible mask and the server's ban mask, then whether the mod's voice is on.</summary>
public partial struct VoiceMask
{
    public partial uint Audible0 { get; set; }
    public partial uint Banned0 { get; set; }
    public partial uint Audible1 { get; set; }
    public partial uint Banned1 { get; set; }
    public partial bool ModEnable { get; set; }
}

[UserMessage("VoiceMask", Stock = true, Size = 2 * 4 * 2 + 1)]
public sealed class VoiceMaskMessage(IClientMessageEffects? effects) : IUserMessage<VoiceMask>
{
    /// <summary>For a side that registers the declaration without dispatching it.</summary>
    public VoiceMaskMessage() : this(null) { }

    /// <summary>The last one that arrived.</summary>
    public VoiceMask Last { get; private set; }

    /// <summary>MsgFunc_VoiceMask: the voice status takes the server's masks.</summary>
    public void OnReceived(VoiceMask message)
    {
        Last = message;
        effects?.VoiceMasks(message);
    }
}

/// <summary>RequestState's payload: nothing; the message is the request.</summary>
public partial struct RequestState
{
}

[UserMessage("RequestState", Stock = true, Size = 0)]
public sealed class RequestStateMessage(IClientMessageEffects? effects) : IUserMessage<RequestState>
{
    /// <summary>For a side that registers the declaration without dispatching it.</summary>
    public RequestStateMessage() : this(null) { }

    /// <summary>How many requests arrived.</summary>
    public int Requests { get; private set; }

    /// <summary>MsgFunc_RequestState: the voice status sends its state again.</summary>
    public void OnReceived(RequestState message)
    {
        Requests++;
        effects?.VoiceRequestState();
    }
}

/// <summary>CloseCaption's payload: the caption token, its duration in tenths of a second (at most 255), and its flags (warn if missing, from the player, the speaker's gender).</summary>
public partial struct CloseCaption
{
    public partial string Token { get; set; }
    public partial short Duration { get; set; }
    public partial byte Flags { get; set; }
}

[UserMessage("CloseCaption", Stock = true)]
public sealed class CloseCaptionMessage : IUserMessage<CloseCaption>
{
    /// <summary>The last one that arrived.</summary>
    public CloseCaption Last { get; private set; }

    public void OnReceived(CloseCaption message) => Last = message;
}

/// <summary>HintText's payload: the hint.</summary>
public partial struct HintText
{
    public partial string Text { get; set; }
}

[UserMessage("HintText", Stock = true)]
public sealed class HintTextMessage : IUserMessage<HintText>
{
    /// <summary>The last one that arrived.</summary>
    public HintText Last { get; private set; }

    public void OnReceived(HintText message) => Last = message;
}

/// <summary>
/// KeyHintText's payload: a list of hints, of which env_hudhint always sends
/// one. An empty hint is how a map takes the last one down.
/// </summary>
public partial struct KeyHintText
{
    /// <summary>How many hints one message may carry.</summary>
    public const int MaxHints = 8;

    [UserMessageArray(MaxCount = MaxHints)]
    public partial string[] Hints { get; set; }
}

/// <summary>
/// The instruction line at the bottom of the screen, from env_hudhint. The
/// hints are delivered exactly as the server wrote them, an empty one
/// included; a payload counting more than <see cref="KeyHintText.MaxHints"/>
/// is refused whole rather than read part-way.
/// </summary>
[UserMessage("KeyHintText", Stock = true)]
public sealed class KeyHintTextMessage : IUserMessage<KeyHintText>, IUserMessageEvent<IReadOnlyList<string>>
{
    /// <summary>Raised on the client with the hints exactly as the server wrote them.</summary>
    public event Action<IReadOnlyList<string>>? Fired;

    public void OnReceived(KeyHintText message) => Fired?.Invoke(message.Hints ?? []);
}

/// <summary>SquadMemberDied's payload: nothing; the message is the event.</summary>
public partial struct SquadMemberDied
{
}

[UserMessage("SquadMemberDied", Stock = true, Size = 0)]
public sealed class SquadMemberDiedMessage : IUserMessage<SquadMemberDied>
{
    /// <summary>The last one that arrived.</summary>
    public SquadMemberDied Last { get; private set; }

    public void OnReceived(SquadMemberDied message) => Last = message;
}

/// <summary>AmmoDenied's payload: the ammo type refused.</summary>
public partial struct AmmoDenied
{
    public partial short AmmoType { get; set; }
}

[UserMessage("AmmoDenied", Stock = true, Size = 2)]
public sealed class AmmoDeniedMessage : IUserMessage<AmmoDenied>
{
    /// <summary>The last refusal that arrived.</summary>
    public AmmoDenied Last { get; private set; }

    public void OnReceived(AmmoDenied message) => Last = message;
}

/// <summary>CreditsMsg's payload: which credits to roll (env_credits: 1 the outro, 2 the intro, 3 the logo).</summary>
public partial struct CreditsMsg
{
    public partial byte Kind { get; set; }
}

[UserMessage("CreditsMsg", Stock = true, Size = 1)]
public sealed class CreditsMsgMessage : IUserMessage<CreditsMsg>
{
    /// <summary>The last one that arrived.</summary>
    public CreditsMsg Last { get; private set; }

    public void OnReceived(CreditsMsg message) => Last = message;
}

/// <summary>LogoTimeMsg's payload: how long the logo shows.</summary>
public partial struct LogoTimeMsg
{
    public partial float Length { get; set; }
}

[UserMessage("LogoTimeMsg", Stock = true, Size = 4)]
public sealed class LogoTimeMsgMessage : IUserMessage<LogoTimeMsg>
{
    /// <summary>The last one that arrived.</summary>
    public LogoTimeMsg Last { get; private set; }

    public void OnReceived(LogoTimeMsg message) => Last = message;
}

/// <summary>AchievementEvent's payload: the achievement's id.</summary>
public partial struct AchievementEvent
{
    public partial short Achievement { get; set; }
}

[UserMessage("AchievementEvent", Stock = true)]
public sealed class AchievementEventMessage : IUserMessage<AchievementEvent>
{
    /// <summary>The last one that arrived.</summary>
    public AchievementEvent Last { get; private set; }

    public void OnReceived(AchievementEvent message) => Last = message;
}

/// <summary>
/// The jalopy's radar sweep: the payload. See <see cref="UpdateJalopyRadarMessage"/>.
/// </summary>
public partial struct JalopyRadar
{
    /// <summary>Contacts the sweep found.</summary>
    public partial byte Contacts { get; set; }

    /// <summary>Whether the sweep should run at the fast rate — dog is near.</summary>
    public partial bool FastUpdate { get; set; }
}

/// <summary>
/// The jalopy's radar sweep — a message whose payload managed code is free to
/// choose.
///
/// <para>
/// Neither <c>vehicle_jeep_episodic.cpp</c> (which sends it) nor
/// <c>c_vehicle_jeep_episodic.cpp</c> (which reads it) is compiled, so no
/// native code on either side has an opinion about the layout. Stock's payload
/// was a single zero byte, an "end marker" for contacts the message never
/// carried; the payload is <see cref="JalopyRadar"/>'s two members, and the
/// two-byte schema hash the old packer put in front of them is gone with that
/// packer (one wire convention for every message).
/// </para>
/// </summary>
[UserMessage("UpdateJalopyRadar", Stock = true)]
public sealed class UpdateJalopyRadarMessage : IUserMessage<JalopyRadar>, IUserMessageEvent<JalopyRadar>
{
    /// <summary>Raised on the client when a sweep arrives and reads whole.</summary>
    public event Action<JalopyRadar>? Fired;

    public void OnReceived(JalopyRadar message) => Fired?.Invoke(message);
}

//
// SourceSharp's own messages start here. Appended AFTER the standard set so
// adding one never shifts a standard message's index.
//

/// <summary>
/// The join handshake's payload: the entity layouts' fingerprint and count,
/// the engine block's fingerprint, and the three protocol-list hashes. Six
/// fixed members, 24 bytes, which is the declared Size the engine checks.
/// </summary>
public partial struct LayoutCheck
{
    public partial uint Fingerprint { get; set; }
    public partial int LayoutCount { get; set; }
    public partial uint BlockFingerprint { get; set; }
    public partial uint StringTablesHash { get; set; }
    public partial uint TempEntsHash { get; set; }
    public partial uint NetStatesHash { get; set; }
}

/// <summary>
/// Carries the server's build fingerprints to a joining client.
///
/// Client and server generate their datatables independently from the same
/// metadata, so a divergence would otherwise surface only as the two sides
/// quietly disagreeing about what replicated bytes mean. The client compares
/// these against its own and says so loudly if they differ.
///
/// <para>
/// TWO HASHES, THREE LONGS. The first covers the entity layouts; the second
/// covers the ENGINE BLOCK — the struct declared once in C# and once in C++
/// that the shell getters and the network decoder read at a fixed offset
/// (plan_entity.md §3.2). The second was added while the block was still
/// empty, on purpose: a slot that arrives with its first real payload is a
/// slot whose refusal path has never run, and the block is precisely the thing
/// whose drift nobody would otherwise see.
/// </para>
///
/// <para>
/// The payload is <see cref="LayoutCheck"/>, written by
/// <c>ServerModule.SendLayoutCheck</c> and read by the generated code: one
/// declaration, so the two sides cannot hand-disagree on the layout. A
/// truncated handshake is refused by the bridge and leaves
/// <see cref="Matched"/> false.
/// </para>
/// </summary>
[UserMessage("Layout", Size = 24)]
public sealed class LayoutCheckMessage : IUserMessage<LayoutCheck>
{
    /// <summary>
    /// A handler with no registry to verify against: every check reports a
    /// mismatch. What the registry's no-container fallback builds.
    /// </summary>
    public LayoutCheckMessage()
    {
    }

    /// <summary>
    /// A handler that verifies against <paramref name="registry"/> — the
    /// client module's, handed over by its container.
    /// </summary>
    public LayoutCheckMessage(EntityLayoutRegistry registry)
        : this(registry, null)
    {
    }

    /// <summary>
    /// A handler that also compares the server's protocol lists against
    /// <paramref name="protocol"/>, this side's own (plan_side_isolation §2.1).
    /// </summary>
    public LayoutCheckMessage(EntityLayoutRegistry registry, ProtocolManifest? protocol)
        : this(registry, protocol, null)
    {
    }

    /// <summary>
    /// The client's container's: also the composition's log, which a
    /// protocol-list mismatch is said through.
    /// </summary>
    [Inject]
    public LayoutCheckMessage(EntityLayoutRegistry registry, ProtocolManifest? protocol, ILog? log)
    {
        Registry = registry;
        Protocol = protocol;
        _log = log;
    }

    private readonly ILog? _log;

    /// <summary>This side's protocol-list hashes, or null when none was handed over (every check then fails).</summary>
    public ProtocolManifest? Protocol { get; }

    /// <summary>
    /// The registry the check runs against. It was a static the client module
    /// set; it is this handler's own now, so two modules cannot verify against
    /// each other's layouts.
    /// </summary>
    public EntityLayoutRegistry? Registry { get; }

    public uint Fingerprint      { get; private set; }
    public int  LayoutCount      { get; private set; }

    /// <summary>The server's engine-block layout hash. See the class remarks.</summary>
    public uint BlockFingerprint { get; private set; }

    /// <summary>The server's hashes of its string table, temp-entity and networked-state keys.</summary>
    public uint StringTablesHash { get; private set; }
    public uint TempEntsHash     { get; private set; }
    public uint NetStatesHash    { get; private set; }

    public bool Matched          { get; private set; }

    public void OnReceived(LayoutCheck message)
    {
        Fingerprint      = message.Fingerprint;
        LayoutCount      = message.LayoutCount;
        BlockFingerprint = message.BlockFingerprint;
        StringTablesHash = message.StringTablesHash;
        TempEntsHash     = message.TempEntsHash;
        NetStatesHash    = message.NetStatesHash;

        bool layouts = Registry?.Verify("Client", Fingerprint, LayoutCount, BlockFingerprint) ?? false;
        bool lists   = VerifyProtocol();

        Matched = layouts && lists;
    }

    /// <summary>
    /// Loud rather than fatal, like the layout comparison: a list the two
    /// sides disagree on is a declaration one side compiled and the other did
    /// not, and naming the list is what makes it findable.
    /// </summary>
    private bool VerifyProtocol()
    {
        if (Protocol is null)
        {
            _log?.Warning("[SourceSharp/Client] no protocol manifest to check the server's lists against\n");
            return false;
        }

        var differ = Protocol.Differences(StringTablesHash, TempEntsHash, NetStatesHash);
        if (differ.Count == 0)
            return true;

        _log?.Warning($"[SourceSharp/Client] PROTOCOL LIST MISMATCH: the server declares different " +
                    $"{string.Join(", ", differ)} than this client. A declaration is compiled into " +
                    $"one side only; both sides must build it from the same shared source.\n");
        return false;
    }
}

/// <summary>SendAudio's payload: the radio sound to play.</summary>
public partial struct SendAudio
{
    public partial string Sound { get; set; }
}

/// <summary>
/// Plays a radio command. Nothing in this mod sends it, but base client code
/// hooks it unconditionally from CHud's message table — and hooking a message
/// that was never registered trips an assert in usermessages.cpp, which a
/// release build compiles away and a debug build stops dead on.
/// </summary>
[UserMessage("SendAudio", Stock = true)]
public sealed class SendAudioMessage : IUserMessage<SendAudio>
{
    /// <summary>The last one that arrived.</summary>
    public SendAudio Last { get; private set; }

    public void OnReceived(SendAudio message) => Last = message;
}

/// <summary>SavedConvar's payload: the replicated convar's name.</summary>
public partial struct SavedConvar
{
    public partial string Name { get; set; }
}

/// <summary>
/// Saved-convar notification, registered by the stock RegisterScriptMessages().
/// This mod does not compile the file that calls it, but something on the client
/// still hooks the name, so without it the hook fails and a debug build stops
/// on the assert.
/// </summary>
[UserMessage("SavedConvar", Stock = true)]
public sealed class SavedConvarMessage : IUserMessage<SavedConvar>
{
    /// <summary>The last one that arrived.</summary>
    public SavedConvar Last { get; private set; }

    public void OnReceived(SavedConvar message) => Last = message;
}

/// <summary>
/// The debug channel invitation's payload: the dev API's bytes, carried whole
/// and unread by the game (plan_devapi_interfaces §5.5).
/// </summary>
public partial struct DebugChannelInvite
{
    /// <summary>The most an invitation may take: the dev API's own limit.</summary>
    public const int MaxBytes = 64;

    [UserMessageArray(MaxCount = MaxBytes)]
    public partial byte[] Invitation { get; set; }
}

/// <summary>
/// The server's INVITATION to open a debug channel back to it.
///
/// <para>
/// <b>AN INVITATION, NOT AN INSTRUCTION, AND NOT AN ADDRESS.</b> It carries a
/// protocol number, a port and a single-use nonce, and it is refused outright if
/// it carries anything else — see <see cref="DebugChannelInvitation.TryRead"/>,
/// which enforces that as a length rule rather than as a convention. The client
/// dials the host it decided on for itself, from its own command line; a server
/// never gets to say where a client's command-and-control socket points.
/// </para>
///
/// <para>
/// <b>THE CLIENT'S OWN OPT-IN IS CHECKED FIRST, BEFORE THE PAYLOAD IS READ AT
/// ALL.</b> <see cref="Accepted"/> is installed by <c>ClientModule</c> only when
/// this process was started with <c>-debugui</c>. Without it this handler returns
/// on its first line: nothing parses, nothing resolves, nothing dials, and there
/// is no reachable code path from an arriving message to a socket. The
/// declaration still has to exist on both sides, because declaration order in
/// this file IS the wire protocol and a message only one side declares
/// renumbers every message after it on the other.
/// </para>
///
/// <para>
/// Last on purpose, like the two above it.
/// </para>
/// </summary>
[UserMessage(MessageName)]
public sealed class DebugChannelInviteMessage : IUserMessage<DebugChannelInvite>
{
    /// <summary>
    /// The message's name on the wire. The game's own: the dev API never needs
    /// it, because the game carries the invitation's bytes without reading them
    /// (plan_devapi_interfaces §5.5).
    /// </summary>
    public const string MessageName = "DebugChannel";

    /// <summary>What receives an invitation's bytes, whole and unread.</summary>
    public delegate void InvitationHandler(ReadOnlySpan<byte> invitation);

    /// <summary>
    /// Where an accepted invitation goes.
    ///
    /// <para>
    /// NULL IS THE DEFAULT AND NULL MEANS OFF. Installed by the client module
    /// only under <c>-debugui</c>, and cleared again at level shutdown so a
    /// channel cannot outlive the level that invited it.
    /// </para>
    ///
    /// <para>
    /// An INSTANCE property of the handler the client module's container
    /// holds, where it used to be a static every handler in the process read.
    /// </para>
    /// </summary>
    public InvitationHandler? Accepted { get; set; }

    private long _payloadsRead;

    /// <summary>
    /// How many invitation payloads this side has actually looked at.
    ///
    /// <para>
    /// HERE SO THE NEGATIVE IS OBSERVABLE. "A client without <c>-debugui</c> never
    /// dials" is weak — a handler that parsed the offer, resolved a host and
    /// then decided not to connect would satisfy it. What is true instead is
    /// that the payload is never read, and this counter is how a test holds that
    /// rather than taking it on trust. It only ever goes up, so a reader
    /// compares a delta and no test has to reset anything.
    /// </para>
    /// </summary>
    public long PayloadsRead => Interlocked.Read(ref _payloadsRead);

    /// <summary>
    /// The dispatch, written here rather than taken from
    /// <see cref="IUserMessage{TPayload}"/>'s default, for ONE reason: the
    /// client's opt-in is checked BEFORE the payload is parsed. The default
    /// would read the invitation first and decline it after.
    /// </summary>
    bool IUserMessage.OnReceived(UserMessageReader reader)
    {
        // GATE ONE, and it is first for a reason: a client that did not ask for
        // a debug channel must not so much as parse a server's offer of one.
        // Declining is not a refusal of the payload, so it answers true.
        if (Accepted is null)
            return true;

        Interlocked.Increment(ref _payloadsRead);

        var message = DebugChannelInvite.Read(ref reader);
        if (reader.Overran)
            return false;

        OnReceived(message);
        return true;
    }

    public void OnReceived(DebugChannelInvite message)
    {
        // Opaque bytes, handed over whole: the client's dev API reads them and
        // refuses a malformed one itself. The game does not know the format.
        Accepted?.Invoke(message.Invitation ?? []);
    }
}

/// <summary>
/// The soundscape push's payload: the soundscape, the entity setting it, which
/// of the eight positioned sounds carry a position, and those positions, one
/// per set bit in bit order. Only the named positions travel.
/// </summary>
public partial struct SoundscapePush
{
    public partial int SoundscapeIndex { get; set; }
    public partial int EntIndex { get; set; }
    public partial byte LocalBits { get; set; }

    [UserMessageArray(MaxCount = SoundscapeMessage.LocalSoundCount)]
    public partial Vector[] Positions { get; set; }
}

/// <summary>
/// <b>WHICH SOUNDSCAPE ONE PLAYER IS HEARING</b> — plan_entity.md step
/// 8e-sound, closing the second of the two declared refusals step 7e measured.
///
/// <para>
/// <b>THE GAP THIS FILLS.</b> Both ends of the soundscape mechanism were
/// present and correct and there was no wire between them.
/// <c>Soundscapes.cs</c> on the server picks the right soundscape — range,
/// line of sight, nearest, trigger volume — and
/// <c>C_SoundscapeSystem::UpdateAudioParams</c> on the client starts its loops,
/// its random waves and its DSP. Between them, <c>Soundscape_Update</c> had NO
/// CALLER and the server's <c>PlayerSetSoundscape</c> slot had an EMPTY BODY,
/// because <c>audioparams_t</c> used to be networked through
/// <c>CBasePlayer</c>'s local-data table and that class retired at step 13c.
/// Measured on <c>dm_lockdown</c>: 255 soundscapes parsed, a player standing
/// inside one the server had chosen, and the client's current soundscape
/// <c>-1 [none]</c>.
/// </para>
///
/// <para>
/// <b>A USER MESSAGE RATHER THAN NETWORKED PLAYER STATE, which is what stock
/// did.</b> Stock put <c>audioparams_t</c> on <c>CBasePlayer</c>'s local data
/// and let the snapshot carry it. That costs a struct of eight
/// <c>Vector</c>s plus three ints on EVERY player snapshot for a value that
/// changes a few times a map, and it would edit <c>PlayerNetState</c>, the
/// managed entity base's. A user message is sent when the answer changes and
/// costs nothing the rest of the time. The index-drift objection does not
/// apply: both sides register this list from the same <c>Shared.dll</c>, in
/// metadata-token order, so the wire index is the same number on both ends by
/// construction.
/// </para>
///
/// <para>
/// <b>DECLARED LAST IN THIS FILE ON PURPOSE.</b> The registration order IS the
/// protocol, so a message inserted anywhere but the end would shift the wire
/// index of every message after it. Appending gives it the next index and moves
/// nothing.
/// </para>
///
/// <para>
/// <b>The three numbers are not interchangeable and the middle one is the one
/// that surprises.</b> <c>SoundscapeIndex</c> is the position in the parsed
/// <c>scripts/soundscapes_*.txt</c> list, which is the only name both sides
/// share for a soundscape. <c>EntIndex</c> is what stock sent as "the entity
/// setting the soundscape", and <c>UpdateAudioParams</c> requires it to be
/// GREATER THAN ZERO before it will start anything — an entIndex of zero with
/// an index of -1 is precisely how stock says "you have left, hear nothing".
/// The server sends the soundscape's own one-based identity token there, which
/// is what <c>Soundscapes.cs</c> has always passed to the slot. <c>LocalBits</c>
/// says which of the eight positioned sounds carry a position, and only those
/// positions are on the wire.
/// </para>
/// </summary>
[UserMessage("Soundscape")]
public sealed class SoundscapeMessage : IUserMessage<SoundscapePush>
{
    /// <summary>
    /// The engine's <c>NUM_AUDIO_LOCAL_SOUNDS</c>, which is also the width of
    /// the bitfield. Eight, so the bits fit in one byte.
    /// </summary>
    public const int LocalSoundCount = 8;

    /// <summary>Position in the parsed soundscape list, or -1 for none.</summary>
    public int SoundscapeIndex { get; private set; } = -1;

    /// <summary>The soundscape's identity token. Zero means "left, hear nothing".</summary>
    public int EntIndex { get; private set; }

    /// <summary>Which of the eight positioned sounds have a position.</summary>
    public int LocalBits { get; private set; }

    /// <summary>The positions, indexed by bit. Unset bits hold the origin.</summary>
    public Vector[] LocalPositions { get; } = new Vector[LocalSoundCount];

    /// <summary>How many of these this handler has applied. For tests and for the gate.</summary>
    public long Applied => Interlocked.Read(ref _applied);

    private long _applied;

    /// <summary>A handler whose pushes go nowhere: the no-container fallback's, and the server's.</summary>
    public SoundscapeMessage()
    {
    }

    /// <summary>
    /// A handler whose decoded pushes go to <paramref name="lands"/>. The
    /// client's container builds it with the sink the client module registers
    /// (the client's <c>ClientSoundscapes.Push</c>); the sink is a registration
    /// of the CLIENT assembly because the soundscape system is the client's,
    /// and this shared assembly must not lean on it (SideAuditTests).
    /// </summary>
    public SoundscapeMessage(SoundscapeSink lands)
        : this(lands, null)
    {
    }

    /// <summary>The client's container's: the sink, and the composition's log a dropped push is said through.</summary>
    [Inject]
    public SoundscapeMessage(SoundscapeSink lands, ILog? log)
    {
        Lands = lands;
        _log = log;
    }

    private readonly ILog? _log;

    /// <summary>Where a decoded push goes. See <see cref="Lands"/>.</summary>
    public delegate void SoundscapeSink(int soundscapeIndex, int entIndex,
                                        int localBits, Vector[] localPositions);

    /// <summary>
    /// <b>THE ONE PLACE A DECODED PUSH GOES</b>, fixed when the handler is built.
    ///
    /// <para>
    /// This was a static delegate the client module installed, because a
    /// <c>[UserMessage]</c> handler used to be built by
    /// <c>Activator.CreateInstance</c> with no constructor arguments and so
    /// could not be handed an engine area. The client's container builds it
    /// now (<c>UserMessageRegistry.AddHandlersTo</c>), and the client module's
    /// sink arrives through the constructor above. A test hands it a delegate
    /// and reads the decoded numbers without a running engine.
    /// </para>
    ///
    /// <para>
    /// Null on the SERVER, and that is correct rather than incomplete: a
    /// soundscape is something a client hears, the server never receives a user
    /// message, and the registry does not even construct handlers on that side.
    /// </para>
    /// </summary>
    public SoundscapeSink? Lands { get; }

    /// <summary>
    /// The payload for one push: <paramref name="localPositions"/> is indexed
    /// by bit, and only the positions of set bits go in, in bit order. A set
    /// bit with no position given carries the origin.
    /// </summary>
    public static SoundscapePush PayloadOf(int soundscapeIndex, int entIndex, int localBits,
                                           ReadOnlySpan<Vector> localPositions)
    {
        var positions = new List<Vector>(LocalSoundCount);
        for (int i = 0; i < LocalSoundCount; i++)
        {
            if ((localBits & (1 << i)) != 0)
                positions.Add(i < localPositions.Length ? localPositions[i] : default);
        }

        return new SoundscapePush
        {
            SoundscapeIndex = soundscapeIndex,
            EntIndex        = entIndex,
            LocalBits       = (byte)(localBits & 0xFF),
            Positions       = positions.ToArray(),
        };
    }

    public void OnReceived(SoundscapePush message)
    {
        // POSITIONS ARE INDEXED BY BIT, NOT PACKED, on this side: a script's
        // position2 is the third slot whether or not the first two are set.
        // The wire carries one per set bit; a count that disagrees with the
        // bits is a payload this build did not write, and is dropped.
        var positions = message.Positions ?? [];
        if (positions.Length != System.Numerics.BitOperations.PopCount(message.LocalBits))
        {
            _log?.Warning($"[SourceSharp] soundscape push carries {positions.Length} position(s) for " +
                        $"bits 0x{message.LocalBits:X2}; dropped\n");
            return;
        }

        SoundscapeIndex = message.SoundscapeIndex;
        EntIndex        = message.EntIndex;
        LocalBits       = message.LocalBits;

        int next = 0;
        for (int i = 0; i < LocalSoundCount; i++)
            LocalPositions[i] = (LocalBits & (1 << i)) != 0 ? positions[next++] : default;

        Interlocked.Increment(ref _applied);

        // The sink is the client's soundscape system: since sound cluster 17
        // (lane c2-sims) the managed ClientSoundscapes.Push, stock's
        // Soundscape_Update -- the function step 7e kept rather than deleting
        // as unused, precisely so that closing this would be a CALLER and not a
        // rewrite.
        //
        // The counter above is incremented whether or not a sink is installed,
        // so "the message arrived and nothing was listening" is a readable
        // state rather than an invisible one.
        Lands?.Invoke(SoundscapeIndex, EntIndex, LocalBits, LocalPositions);
    }
}

/// <summary>
/// LAST IN THIS FILE ON PURPOSE: a declaration's position is its wire index,
/// so a new message is appended after every existing one.
///
/// The server asks a client to reconnect (plan_host_sdk §2.3, the user's ruling
/// of 2026-09-30): an explicit message whose only effect is the reconnect, so
/// the server cannot use it to run anything else on the client (managed code on
/// the client may run console commands since 2026-10-05; the server still sends
/// it none: a message names a purpose, never a command line). A hosting service
/// sends it after re-routing the player; the client's reconnect lands them on
/// their new server. No payload.
/// </summary>
/// <summary>Reconnect's payload: nothing; the message is the request.</summary>
public partial struct Reconnect
{
}

[UserMessage(Name, Size = 0)]
public sealed class ReconnectMessage(IClientReconnect? reconnect) : IUserMessage<Reconnect>
{
    public const string Name = "Reconnect";

    /// <summary>For a side that registers the declaration without dispatching it.</summary>
    public ReconnectMessage() : this(null) { }

    public void OnReceived(Reconnect message) => reconnect?.Reconnect();
}
