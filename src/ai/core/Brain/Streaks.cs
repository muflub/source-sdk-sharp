namespace SourceSharp.Ai;

/// <summary>
/// A run of playing above or below itself (plan_hl2mp_ai §5.5, ruling 2026-11-02: "some times bad
/// bots become great for a very short stint"). Mostly earned, by a kill or better two close
/// together, and now and then from nothing; a death ends a hot one, and two close together can
/// start a cold one. Its own stream, every draw every time, so one bot's luck moves nothing else.
/// A bot a streak would not change (one at the cap) has none to show. Reads only what the bot's
/// memory holds of its own kills and deaths; per bot.
/// </summary>
public sealed class Streaks(AiRandom random)
{
    /// <summary>The chance a kill starts one, two kills close together, and how close.</summary>
    public const float KillChance = 0.25f, DoubleChance = 0.6f, DoubleSeconds = 8f;

    /// <summary>How often one starts from nothing, a person's good minute, and how often that is looked at.</summary>
    public const float RandomPerMinute = 0.1f, CheckSeconds = 10f;

    /// <summary>A hot streak's length, and a rest after any streak before the next.</summary>
    public const float HotMinSeconds = 8f, HotMaxSeconds = 20f, RestSeconds = 45f;

    /// <summary>The chance two deaths close together start a cold spell, how close, and its length.</summary>
    public const float ColdChance = 0.5f, ColdDeathSeconds = 20f, ColdMinSeconds = 6f, ColdMaxSeconds = 12f;

    /// <summary>A cold spell is a good bot's: under this much worse, a weak bot has none to show.</summary>
    public const float ColdWorthShowing = 1.1f;

    private readonly AiRandom _random = random;
    private int _askSeen;
    private Tick _until, _next, _check, _killSeen, _deathSeen;

    /// <summary>The streak on now: 1 hot, -1 cold, 0 none.</summary>
    public int Kind { get; private set; }

    /// <summary>Its name for the record: "hot", "cold", or "" with none.</summary>
    public string Name => Kind > 0 ? "hot" : Kind < 0 ? "cold" : "";

    /// <summary>How many streaks have begun: one more than at the last step says one began in it.</summary>
    public int Begun { get; private set; }

    /// <summary>What the streak makes of the bot now: only read with one on.</summary>
    public StreakParams Now(in DecisionParams p) => Kind > 0 ? p.Hot : p.Cold;

    /// <summary>The aim's settled error now: the streak's, or the bot's own.</summary>
    public float AimErrorDeg(in DecisionParams p) => Kind == 0 ? p.AimErrorDeg : Now(in p).AimErrorDeg;

    /// <summary>How well a shot is led now: the streak's, or the bot's own.</summary>
    public float LeadQuality(in DecisionParams p) => Kind == 0 ? p.LeadQuality : Now(in p).LeadQuality;

    /// <summary>How long a change of stance goes unanswered now (<see cref="AimHeightHold.ReAimSeconds"/>), by the streak's recognition and reaction or the bot's own.</summary>
    public float ReAimSeconds(in DecisionParams p)
        => Kind == 0 ? AimHeightHold.ReAimSeconds(p.RecognizeSeconds, p.ReactionMs) : AimHeightHold.ReAimSeconds(Now(in p).RecognizeSeconds, Now(in p).ReactionMs);

    /// <summary>
    /// What the doing record carries of the streak (<see cref="DoingRecord.Streak"/> and the three
    /// after it), which the motion layer and the senses read: its name, and the aim error, the
    /// reaction and the recognition it gives; nothing with none on.
    /// </summary>
    public (string Name, float AimErrorDeg, float ReactionMs, float RecognizeSeconds) Record(in DecisionParams p)
        => (Name, Kind == 0 ? 0f : Now(in p).AimErrorDeg, Kind == 0 ? 0f : Now(in p).ReactionMs, Kind == 0 ? 0f : Now(in p).RecognizeSeconds);

    /// <summary>One decide, alive or dead: what the bot's own kills and deaths since the last, the time and a console's ask make of its streak.</summary>
    public void Step(Tick now, float tickSeconds, BotMemory memory, in DecisionParams p, bool alive)
    {
        ArgumentNullException.ThrowIfNull(memory);
        if (!memory.StreaksOn)
        {
            Kind = 0;
            return;
        }

        if (Kind != 0 && now >= _until)
            End(now, tickSeconds);

        (Tick lastDeath, Tick deathBefore) = memory.OwnDeaths;
        if (lastDeath != _deathSeen)
        {
            _deathSeen = lastDeath;
            float u = _random.NextFloat();
            if (Kind > 0)
                End(now, tickSeconds);
            if (u < ColdChance && deathBefore.Value != 0 && (lastDeath - deathBefore) * tickSeconds <= ColdDeathSeconds && Rested(now) && CanRunCold(in p))
                Begin(-1, now, tickSeconds);
        }

        (Tick lastKill, Tick killBefore) = memory.OwnKills;
        if (lastKill != _killSeen)
        {
            _killSeen = lastKill;
            float u = _random.NextFloat();
            bool twice = killBefore.Value != 0 && (lastKill - killBefore) * tickSeconds <= DoubleSeconds;
            if (u < (twice ? DoubleChance : KillChance) && Rested(now) && CanRunHot(in p))
                Begin(1, now, tickSeconds);
        }

        // The first look a period in: no bot opens its game on a run.
        if (_check.Value == 0)
            _check = now + (int)(CheckSeconds / tickSeconds);
        if (now >= _check)
        {
            _check = now + (int)(CheckSeconds / tickSeconds);
            float u = _random.NextFloat();
            if (u < RandomPerMinute * CheckSeconds / 60f && Rested(now) && CanRunHot(in p) && alive)
                Begin(1, now, tickSeconds);
        }

        (int asked, int code) = memory.StreakAsked;
        if (asked != _askSeen)
        {
            _askSeen = asked;
            Begin(code == AiEvent.StreakHot ? 1 : -1, now, tickSeconds);
        }
    }

    private bool Rested(Tick now) => Kind == 0 && now >= _next;

    private static bool CanRunHot(in DecisionParams p) => p.Hot.AimErrorDeg != p.AimErrorDeg;

    private static bool CanRunCold(in DecisionParams p) => p.Cold.AimErrorDeg > p.AimErrorDeg * ColdWorthShowing;

    private void Begin(int kind, Tick now, float tickSeconds)
    {
        float least = kind > 0 ? HotMinSeconds : ColdMinSeconds, most = kind > 0 ? HotMaxSeconds : ColdMaxSeconds;
        Kind = kind;
        _until = now + (int)((least + (most - least) * _random.NextFloat()) / tickSeconds);
        Begun++;
    }

    private void End(Tick now, float tickSeconds)
    {
        Kind = 0;
        _next = now + (int)(RestSeconds / tickSeconds);
    }
}
