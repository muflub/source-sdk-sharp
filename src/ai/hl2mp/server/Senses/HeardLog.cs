using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>One sound a bot was told of: when, what kind, where it believes it was (the place carries the hearing's error), how loud, whose.</summary>
public readonly record struct LoggedSound(Tick Tick, SoundKind Kind, AiVector Place, float Loudness, int Source);

/// <summary>
/// The last sounds each bot heard, for the web UI's 3D view (plan_hl2mp_ai
/// §7.1). The blackboard holds one last-heard place per actor and the
/// unexplained sounds worth going to, so a history needs its own record: this
/// ring, filled where the tap hands a brain the event. No brain reads it: a
/// brain has the event, and <c>PerceptionCensusTests</c> holds who calls this.
/// </summary>
[SideService(Side.Server)]
public sealed class HeardLog
{
    /// <summary>The sounds kept a bot: three others' steps and a burst of fire, for several seconds.</summary>
    public const int Capacity = 48;

    private sealed class Ring(ActorId bot)
    {
        public ActorId Bot { get; } = bot;
        public LoggedSound[] Sounds { get; } = new LoggedSound[Capacity];
        public long Written { get; set; }
    }

    private readonly Ring?[] _rings = new Ring?[VisionPass.Slots];

    /// <summary>Records a <see cref="AiEventKind.SoundHeard"/>; another bot in the slot starts its own history.</summary>
    public void Add(in AiEvent heard)
    {
        if (heard.Kind != AiEventKind.SoundHeard || (uint)heard.Bot.Slot >= (uint)_rings.Length)
            return;
        Ring? ring = _rings[heard.Bot.Slot];
        if (ring is null || ring.Bot != heard.Bot)
            _rings[heard.Bot.Slot] = ring = new Ring(heard.Bot);
        ring.Sounds[ring.Written++ % Capacity] = new LoggedSound(heard.Tick, (SoundKind)heard.Code, heard.Position, heard.Amount, heard.Other.IsValid ? heard.Other.Slot : -1);
    }

    /// <summary>
    /// What <paramref name="bot"/> heard in the last <paramref name="ticks"/>
    /// before <paramref name="now"/>, oldest first. A sound stamped after now
    /// is the last level's (a new level counts its ticks from the start again).
    /// </summary>
    public LoggedSound[] Recent(ActorId bot, Tick now, int ticks)
    {
        if ((uint)bot.Slot >= (uint)_rings.Length || _rings[bot.Slot] is not { } ring || ring.Bot != bot)
            return [];
        var recent = new List<LoggedSound>();
        for (long i = Math.Max(0, ring.Written - Capacity); i < ring.Written; i++)
        {
            LoggedSound s = ring.Sounds[i % Capacity];
            int age = now.Value - s.Tick.Value;
            if (age >= 0 && age <= ticks)
                recent.Add(s);
        }

        return [.. recent];
    }
}
