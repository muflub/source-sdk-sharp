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
/// The instruction line at the bottom of the screen, from env_hudhint.
///
/// <para>
/// What separates a hint from any other text is that it is about the CONTROLS:
/// hint strings are written as "Press %+use% to open", and only the client knows
/// which key that is. So the server sends the sentence with the tokens still in
/// it and the substitution happens here — which is also why this could not have
/// been folded into the message channels. Stock draws these in
/// <c>CHudHintKeyDisplay</c>, 790 lines of VGUI panel with a per-token label
/// layout; what a mod decides about a hint is which one is up and what it says,
/// and that is all of what is here.
/// </para>
///
/// <para>
/// One hint at a time, and the newest wins. An empty hint takes it down, which
/// is how env_hudhint's Hide input reaches this side — there is no separate
/// message for hiding, and there does not need to be.
/// </para>
/// </summary>
public sealed class HudHints : IHudHints, IKeyHintSource
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    public HudHints(ILog log, IEngineHud screen)
    {
        _log = log;
        Screen = screen;
    }

    /// <summary>drawing, and the local player it is drawn for.</summary>
    private readonly IEngineHud Screen;



    /// <summary>What stock draws for a command nothing is bound to.</summary>
    public const string Unbound = "< NOT BOUND >";

    private string[] _lines = [];

    // The UI's view (plan_client_ui U2): each line's key apart from its text,
    // so the key draws as a keycap.
    private string[] _keys = [];
    private string[] _texts = [];

    /// <summary>Listens to the module's KeyHintText event.</summary>
    public void Subscribe(IUserMessageEvent<IReadOnlyList<string>> hints) => hints.Fired += OnHints;

    private void OnHints(IReadOnlyList<string> hints)
    {
        // The message carries a list and env_hudhint always sends one, so the
        // hints are joined rather than a slot being kept per hint: what a player
        // sees is a block of instruction, and the sender decides how many lines
        // it takes.
        var resolved = new List<string>(hints.Count);

        foreach (string hint in hints)
        {
            string text = Resolve(hint, Screen.Localise, Screen.LookupBinding);

            if (text.Length != 0)
                resolved.AddRange(text.Split('\n'));
        }

        _lines = resolved.ToArray();

        var keys = new List<string>(hints.Count);
        var texts = new List<string>(hints.Count);
        foreach (string hint in hints)
        {
            string localised = hint.Length != 0 && hint[0] == '#' ? Screen.Localise(hint) : hint;
            foreach (string line in localised.Split('\n'))
            {
                string text = SplitKey(line, Screen.LookupBinding, out string key);
                if (text.Length == 0 && key.Length == 0)
                    continue;
                keys.Add(key);
                texts.Add(text);
            }
        }
        _keys = keys.ToArray();
        _texts = texts.ToArray();

        // Said aloud for the same reason the message channels are: a hint that
        // does not appear has two causes that look identical on a blank screen.
        _log.Msg(_lines.Length == 0
            ? "[SourceSharp/Hud] hint cleared\n"
            : $"[SourceSharp/Hud] hint: {string.Join(" / ", _lines)}\n");
    }

    /// <summary>
    /// Turns a hint as the map wrote it into the sentence a player reads.
    ///
    /// Two substitutions, in that order. A leading '#' means the whole string is
    /// a localisation token and stands for a sentence in another file; then any
    /// %command% inside the result is the key that command is bound to. Doing
    /// the lookup FIRST is what lets a translated string carry its own bindings,
    /// which is how every stock hint is written.
    ///
    /// <para>
    /// Both lookups are passed in rather than called directly so the rule can be
    /// driven in a test: what is interesting here is the parsing, and the engine
    /// has nothing to say about it.
    /// </para>
    /// </summary>
    public static string Resolve(string hint, Func<string, string> localise,
                                 Func<string, string> binding)
    {
        if (hint.Length == 0)
            return string.Empty;

        string text = hint[0] == '#' ? localise(hint) : hint;

        int open = text.IndexOf('%');

        if (open < 0)
            return text;

        var built = new System.Text.StringBuilder(text.Length + 16);
        int at = 0;

        while (at < text.Length)
        {
            open = text.IndexOf('%', at);

            if (open < 0)
            {
                built.Append(text, at, text.Length - at);
                break;
            }

            built.Append(text, at, open - at);

            int close = text.IndexOf('%', open + 1);

            // An unpaired % is a literal per cent sign rather than the start of
            // something. Stock walks off the end of the string here; a map with
            // "50% health" in a hint is the case that finds it.
            if (close < 0)
            {
                built.Append(text, open, text.Length - open);
                break;
            }

            string command = text[(open + 1)..close];

            // "%%" is an escaped per cent, and the empty command in between is
            // how it reads to a parser scanning for pairs.
            if (command.Length == 0)
            {
                built.Append('%');
            }
            else
            {
                string key = binding(command);
                built.Append(key.Length != 0 ? key : Unbound);
            }

            at = close + 1;
        }

        return built.ToString();
    }

    /// <summary>
    /// A hint line's first <c>%command%</c> as the key bound to it (or
    /// <see cref="Unbound"/>), and the rest of the line with any further
    /// commands resolved in place, trimmed. A line with no command has no key.
    /// </summary>
    public static string SplitKey(string line, Func<string, string> binding, out string key)
    {
        key = string.Empty;
        int open = line.IndexOf('%');
        int close = open < 0 ? -1 : line.IndexOf('%', open + 1);
        if (close <= open + 1)
            return Resolve(line, static t => t, binding).Trim();

        string bound = binding(line[(open + 1)..close]);
        key = bound.Length != 0 ? bound : Unbound;
        string rest = string.Concat(line.AsSpan(0, open), line.AsSpan(close + 1));
        return Resolve(rest, static t => t, binding).Trim();
    }

    public int HintCount => _texts.Length;

    public string Hint(int i) => _texts[i];

    public string HintKey(int i) => _keys[i];

    /// <summary>Test seam: the lines currently on screen.</summary>
    public IReadOnlyList<string> Lines => _lines;
}
