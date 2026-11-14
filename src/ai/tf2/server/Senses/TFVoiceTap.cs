using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.TF2.Senses;

/// <summary>
/// The voice menu, in (plan_bots_teams_tf2_part_g G.2.4): what a bot hears of a player's voice
/// line. The mod speaks a line by emitting the matched scene's sounds from the speaker
/// (<c>TFVoiceMenuCommand</c>), named by the game's sound scripts as the class, a dot and the
/// line (<c>Soldier.Medic02</c>, <c>Heavy.HelpMe01</c>, <c>Engineer.SentryAhead01</c>). This tap
/// reads the server's emitted sounds (<see cref="ISoundEmitHook"/>, which the hearing tap reads
/// too) and tells each bot the sound is sent to, the speaker excepted, as a callout from the
/// speaker at the place the sound came from: "Help!" and "Medic!" are <see cref="CalloutKind.Help"/>
/// (a count of 1 for the Medic's), "Sentry ahead!" is <see cref="CalloutKind.Hazard"/>. A bot's
/// memory keeps a callout only from someone it takes for a teammate, so an enemy's line is
/// dropped there. No game code raises anything for this: the sound a client hears is all it reads.
/// </summary>
[SideService(Side.Server)]
public sealed class TFVoiceTap(AiSenses senses, AiBots bots, IEngineFrame frame) : ISoundEmitHook, IOrderedHook
{
    /// <summary>After the hearing tap (unordered, 0) and the mod's own readers of the chain, in the mods' range, at an order of its own.</summary>
    public int HookOrder => ModStages.First + 905;

    /// <summary>What a voice line's sound is called after its class and the dot: the line, then its take's number.</summary>
    public const string MedicLine = "Medic", HelpLine = "HelpMe", SentryAheadLine = "SentryAhead";

    private readonly List<AiEvent> _events = new();

    /// <summary>Voice lines told to a bot, in all.</summary>
    public long Heard { get; private set; }

    public void OnEmitted(in EmittedSound sound)
    {
        if (bots.Bots.Count == 0 || sound.Owner is < 1 or >= VisionPass.Slots || sound.Recipients.IsEmpty)
            return;
        (CalloutKind kind, int count) = LineOf(sound.SoundName);
        if (kind == CalloutKind.None)
            return;
        ActorId speaker = senses.ActorAt(sound.Owner);
        if (!speaker.IsValid)
            return;
        _events.Clear();
        Tell(speaker, new AiVector(sound.Origin.X, sound.Origin.Y, sound.Origin.Z), kind, count, sound.Recipients,
             [.. bots.Bots.Select(b => b.Id)], new Tick(frame.TickCount()), _events);
        foreach (AiEvent e in _events)
            senses.Push(e);
        Heard += _events.Count;
    }

    /// <summary>
    /// The callout a sound's name is, and its count: the part after the class's dot must begin
    /// with a line this tap knows and go on with its take's number, so a Medic's own lines
    /// (<c>Medic.Go01</c>) and a weapon's (<c>Weapon_Medigun.Healing</c>) are nobody's call.
    /// </summary>
    public static (CalloutKind Kind, int Count) LineOf(string soundName)
    {
        ArgumentNullException.ThrowIfNull(soundName);
        int dot = soundName.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || soundName.IndexOf('.', dot + 1) >= 0 || soundName.Contains('/', StringComparison.Ordinal))
            return (CalloutKind.None, 0);
        ReadOnlySpan<char> line = soundName.AsSpan(dot + 1);
        if (Is(line, MedicLine))
            return (CalloutKind.Help, 1);
        if (Is(line, HelpLine))
            return (CalloutKind.Help, 0);
        return Is(line, SentryAheadLine) ? (CalloutKind.Hazard, 0) : (CalloutKind.None, 0);
    }

    private static bool Is(ReadOnlySpan<char> line, string name)
    {
        if (!line.StartsWith(name, StringComparison.OrdinalIgnoreCase) || line.Length == name.Length)
            return false;
        foreach (char c in line[name.Length..])
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return true;
    }

    /// <summary>The callout for each of <paramref name="bots"/> the sound is sent to (its slot in <paramref name="recipients"/>), the speaker excepted.</summary>
    public static void Tell(ActorId speaker, AiVector at, CalloutKind kind, int count, ReadOnlySpan<int> recipients, ReadOnlySpan<ActorId> bots,
                            Tick tick, List<AiEvent> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var say = new SayIntent(kind, at, count, 0, 0, 0, false);
        foreach (ActorId b in bots)
        {
            if (b != speaker && recipients.Contains(b.Slot))
                into.Add(AiEvent.Callout(b, tick, speaker, in say));
        }
    }
}
