//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// The text a map puts on screen: game_text's, and env_message's.
///
/// Source calls these channels rather than messages, and the distinction is the
/// whole design: a channel holds at most one message and a new one replaces
/// what was there. That is what lets a map write three lines by sending three
/// messages, and what stops a repeatedly-triggered sign stacking up.
///
/// <para>
/// env_message arrives differently and ends up in the same place. It sends a
/// NAME, and the words, the colours, the position and the four times all come
/// back together out of titles.txt through the engine's own parser. Once
/// resolved a titles.txt entry IS a game_text, so it draws through this code —
/// but it lands in a ring of its own, because a game_text names the channel it
/// wants and a title has no channel to name.
/// </para>
///
/// Timing and placement are static and pure so they can be tested without a
/// game -- the drawing itself is two engine calls and has nothing to get wrong.
/// </summary>
public sealed class HudMessages : IHudMessages, IReloadParticipant, IMapTextSource
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    public HudMessages(ILog log, IGameClock clock, IEngineHud screen)
    {
        _log = log;
        Clock = clock;
        Screen = screen;
    }

    /// <summary>what the server thinks the time is.</summary>
    private readonly IGameClock Clock;

    /// <summary>drawing, and the local player it is drawn for.</summary>
    private readonly IEngineHud Screen;


    /// <summary>Channels the engine defines. A message outside these is dropped.</summary>
    public const int Channels = 6;

    /// <summary>
    /// How many titles.txt messages may be on screen at once.
    ///
    /// A separate ring from the six channels, because the two are addressed
    /// differently and sharing would let one steal the other's slot: a game_text
    /// names its channel and replaces whatever was in it, while a title has no
    /// channel at all and simply takes the next free space. Stock keeps one
    /// array of sixteen for both and arrives at the same behaviour by accident.
    /// </summary>
    public const int TitleSlots = 4;

    private struct Channel
    {
        public bool Active;
        public string[] Lines;

        /// <summary>The message as it arrived, for the UI (plan_client_ui U2), which wraps it itself.</summary>
        public string Text;
        public float X, Y;
        public Color32 Color;
        public float FadeIn, FadeOut, Hold;
        public float ArrivedAt;
    }

    /// <summary>A title carries its NAME as well, because another entry may clear it by name.</summary>
    private struct Title
    {
        public Channel Body;
        public string Name;
    }

    private readonly Channel[] _channels = new Channel[Channels];
    private readonly Title[] _titles = new Title[TitleSlots];

    /// <summary>
    /// The titles.txt entry a HudText name reaches: a leading <c>#</c> is how a
    /// map spells "this is a lookup", and both spellings reach the same entry
    /// (stock CHudMessage::MessageAdd, message.cpp). Here since lane M1-B; the
    /// shell's HudTextMessageGet looks up exactly the name it is handed.
    /// </summary>
    public static string TitleName(string name) => name.Length > 0 && name[0] == '#' ? name[1..] : name;

    /// <summary>Listens to the module's HudMsg and HudText events.</summary>
    public void Subscribe(IUserMessageEvent<HudMessageArgs> messages, IUserMessageEvent<string> titles)
    {
        messages.Fired += OnMessage;
        titles.Fired += OnTitle;
    }

    private void OnMessage(HudMessageArgs args) => Show(args, Clock.Now);

    /// <summary>
    /// A titles.txt entry named by env_message, resolved through the engine's own
    /// copy of the file.
    ///
    /// The lookup is the whole of what makes this different from a game_text: the
    /// server sent a name, and everything else comes back with the entry.
    /// </summary>
    private void OnTitle(string name)
    {
        var entry = Screen.TextMessageGet(TitleName(name));

        if (entry is null)
        {
            // Stock returns silently here, and silence is exactly wrong for this
            // one: a map naming a title the mod does not ship looks identical to
            // a map that fired nothing. The name is the only thing worth saying
            // and it is what to search titles.txt for.
            _log.Msg($"[SourceSharp/Hud] no titles.txt entry named '{name}'\n");
            return;
        }

        ShowTitle(name, entry.Value, Clock.Now);
    }

    /// <summary>
    /// A titles.txt message through the localisation tables.
    ///
    /// Stock does this for EVERY title, not only ones that look like a token,
    /// and it is not optional: HL2's own titles.txt is almost entirely
    /// "#HL2_Chapter1_Title" and friends, so a title drawn without the lookup
    /// puts the token itself on screen. Verified live — CHAPTER1_TITLE resolves
    /// to "#HL2_Chapter1_Title" and no further.
    ///
    /// <para>
    /// The trailing newline goes first, because a titles.txt entry ends with one
    /// and a token with a newline stuck to it matches nothing.
    /// </para>
    /// </summary>
    public static string LocaliseTitle(string text, Func<string, string> localise)
    {
        string trimmed = text.TrimEnd('\r', '\n');

        return trimmed.Length == 0 ? text : localise(trimmed);
    }

    /// <summary>
    /// Takes a message, splitting it into lines once rather than every frame --
    /// the paint pass runs sixty times a second and must not allocate.
    /// </summary>
    public void Show(HudMessageArgs args, float now)
    {
        if (args.Channel < 0 || args.Channel >= Channels)
            return;

        // An empty message is how a map clears a channel, and it is worth
        // honouring: otherwise the only way to take a sign down is to wait it out.
        if (string.IsNullOrEmpty(args.Text))
        {
            _channels[args.Channel].Active = false;
            _channels[args.Channel].Lines = [];
            return;
        }

        // Logged as well as drawn.
        //
        // "The text did not appear" has two very different causes -- the server
        // never sent it, or it arrived and was not drawn -- and they are
        // indistinguishable on a blank screen. A console line separates them,
        // and it is the first thing to look for when a map goes quiet.
        _log.Msg($"[SourceSharp/Hud] channel {args.Channel}: {args.Text}\n");

        _channels[args.Channel] = new Channel
        {
            Active    = true,
            Lines     = args.Text.Split('\n'),
            Text      = args.Text,
            X         = args.X,
            Y         = args.Y,
            Color     = args.Color,
            FadeIn    = args.FadeIn,
            FadeOut   = args.FadeOut,
            Hold      = args.HoldTime,
            ArrivedAt = now,
        };
    }

    /// <summary>Puts one resolved titles.txt entry on screen.</summary>
    public void ShowTitle(string name, TextMessage entry, float now)
        => ShowTitle(name,
                     LocaliseTitle(entry.GetMessage(), Screen.Localise),
                     entry.GetClearMessage(),
                     entry.X, entry.Y,
                     new Color32(entry.R1, entry.G1, entry.B1, entry.A1),
                     entry.FadeIn, entry.FadeOut, entry.HoldTime, now);

    /// <summary>
    /// The same, with the entry already unpacked, so the placement and the ring
    /// can be driven without an engine.
    ///
    /// <paramref name="clears"/> is titles.txt's own way of letting one entry
    /// take another down — a "press E to open" replaced by "well done" rather
    /// than the two overlapping. Applied before a slot is picked, so the freed
    /// slot is the one this message can land in.
    /// </summary>
    public void ShowTitle(string name, string text, string clears,
                          float x, float y, Color32 color,
                          float fadeIn, float fadeOut, float hold, float now)
    {
        if (text.Length == 0)
            return;

        _log.Msg($"[SourceSharp/Hud] title '{name}': {text}\n");

        if (clears.Length != 0)
        {
            for (int i = 0; i < TitleSlots; i++)
            {
                if (_titles[i].Body.Active
                    && string.Equals(_titles[i].Name, clears, StringComparison.OrdinalIgnoreCase))
                {
                    _titles[i].Body.Active = false;
                    _titles[i].Body.Lines = [];
                    break;
                }
            }
        }

        int slot = FreeTitleSlot(now);

        _titles[slot] = new Title
        {
            Name = name,
            Body = new Channel
            {
                Active    = true,
                Lines     = text.Split('\n'),
                Text      = text,
                X         = x,
                Y         = y,
                Color     = color,
                FadeIn    = fadeIn,
                FadeOut   = fadeOut,
                Hold      = hold,
                ArrivedAt = now,
            },
        };
    }

    /// <summary>
    /// The first free slot, or the OLDEST when all four are busy.
    ///
    /// Overwriting rather than dropping: a message a map has just sent is the one
    /// the player is meant to read, and refusing it would make a busy sequence go
    /// quiet exactly when it has most to say.
    /// </summary>
    private int FreeTitleSlot(float now)
    {
        int oldest = 0;

        for (int i = 0; i < TitleSlots; i++)
        {
            ref Title title = ref _titles[i];

            if (!title.Body.Active
                || Alpha(now - title.Body.ArrivedAt, title.Body.FadeIn,
                         title.Body.Hold, title.Body.FadeOut) <= 0f)
            {
                return i;
            }

            if (title.Body.ArrivedAt < _titles[oldest].Body.ArrivedAt)
                oldest = i;
        }

        return oldest;
    }

    //-------------------------------------------------------------------------
    // Across a client reload (plan_client_novalve §3.8): the six channels and
    // the four title slots, as they stand. Times are the engine's clock, which
    // the reload does not stop, so a message keeps fading where it was.
    //-------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Save(ReloadWriter writer)
    {
        foreach (var channel in _channels)
            Write(writer, channel);

        foreach (var title in _titles)
        {
            writer.Write(title.Name ?? "");
            Write(writer, title.Body);
        }
    }

    /// <inheritdoc/>
    public void Restore(ReloadReader reader)
    {
        for (int i = 0; i < Channels; i++)
            _channels[i] = Read(reader);

        for (int i = 0; i < TitleSlots; i++)
        {
            string name = reader.ReadString();
            _titles[i] = new Title { Name = name, Body = Read(reader) };
        }
    }

    private static void Write(ReloadWriter writer, in Channel channel)
    {
        writer.Write(channel.Active);
        if (!channel.Active)
            return;

        writer.Write(string.Join('\n', channel.Lines ?? []));
        writer.Write(channel.X);
        writer.Write(channel.Y);
        writer.Write(channel.Color.R);
        writer.Write(channel.Color.G);
        writer.Write(channel.Color.B);
        writer.Write(channel.Color.A);
        writer.Write(channel.FadeIn);
        writer.Write(channel.FadeOut);
        writer.Write(channel.Hold);
        writer.Write(channel.ArrivedAt);
    }

    private static Channel Read(ReloadReader reader)
    {
        if (!reader.ReadBool())
            return new Channel { Lines = [], Text = string.Empty };

        string text = reader.ReadString();
        return new Channel
        {
            Active    = true,
            Text      = text,
            Lines     = text.Split('\n'),
            X         = reader.ReadFloat(),
            Y         = reader.ReadFloat(),
            Color     = new Color32(reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt()),
            FadeIn    = reader.ReadFloat(),
            FadeOut   = reader.ReadFloat(),
            Hold      = reader.ReadFloat(),
            ArrivedAt = reader.ReadFloat(),
        };
    }

    /// <summary>How many channels are currently showing something. Test seam.</summary>
    public int ActiveCount(float now) => CountActive(_channels, now);

    /// <summary>How many titles.txt messages are currently showing. Test seam.</summary>
    public int ActiveTitleCount(float now)
    {
        int n = 0;

        for (int i = 0; i < TitleSlots; i++)
        {
            if (IsShowing(_titles[i].Body, now))
                n++;
        }

        return n;
    }

    private static int CountActive(Channel[] channels, float now)
    {
        int n = 0;

        for (int i = 0; i < channels.Length; i++)
        {
            if (IsShowing(channels[i], now))
                n++;
        }

        return n;
    }

    private static bool IsShowing(in Channel channel, float now)
        => channel.Active
           && Alpha(now - channel.ArrivedAt, channel.FadeIn, channel.Hold, channel.FadeOut) > 0f;

    /// <summary>
    /// How opaque a message is, given how long it has been up.
    ///
    /// Fade in, hold, fade out, gone. A zero fade is not a division by zero but
    /// an instant one, which is what a map that leaves the key at its default
    /// of zero means by it.
    /// </summary>
    public static float Alpha(float elapsed, float fadeIn, float hold, float fadeOut)
    {
        if (elapsed < 0f)
            return 0f;

        if (elapsed < fadeIn)
            return fadeIn <= 0f ? 1f : elapsed / fadeIn;

        float held = elapsed - fadeIn;

        if (held < hold)
            return 1f;

        float fading = held - hold;

        if (fadeOut <= 0f || fading >= fadeOut)
            return 0f;

        return 1f - (fading / fadeOut);
    }

    // ---- the UI's view (plan_client_ui U2): IMapTextSource ----

    private readonly HudTextLine[] _shown = new HudTextLine[TitleSlots + Channels];
    private int _shownCount;

    /// <summary>
    /// What shows now, titles first, then the channels, each at its fade's
    /// alpha; a message whose fade has ended is cleared, as the paint did.
    /// Once a frame, before the UI describes the HUD.
    /// </summary>
    public void Refresh(float now)
    {
        _shownCount = 0;
        for (int i = 0; i < TitleSlots; i++)
            Show(ref _titles[i].Body, now);
        for (int i = 0; i < Channels; i++)
            Show(ref _channels[i], now);
    }

    private void Show(ref Channel channel, float now)
    {
        if (!channel.Active)
            return;

        float alpha = Alpha(now - channel.ArrivedAt, channel.FadeIn, channel.Hold, channel.FadeOut);
        if (alpha <= 0f)
        {
            channel.Active = false;
            channel.Lines = [];
            channel.Text = string.Empty;
            return;
        }

        var c = channel.Color;
        _shown[_shownCount++] = new HudTextLine(channel.Text ?? string.Join('\n', channel.Lines ?? []),
            channel.X, channel.Y,
            new UiColor(Byte(c.R), Byte(c.G), Byte(c.B), Byte(c.A)), alpha);
    }

    private static byte Byte(int v) => (byte)Math.Clamp(v, 0, 255);

    public int MessageCount => _shownCount;

    public HudTextLine Message(int i) => _shown[i];
}
