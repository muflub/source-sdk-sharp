namespace SourceSharp.Ai;

/// <summary>
/// One bot's mind. A step is a pure function of the bot's seed, the decide
/// tick, that tick's <see cref="PerceivedView"/> and the events since the
/// last step (plan_hl2mp_ai §2.6): nothing else reaches it, so the intent for
/// a (bot, tick) is the same whichever thread runs it and whenever.
///
/// <para>A brain with a <see cref="Decider"/> (a game's trees, Phase 5)
/// answers its decide; one without answers <see cref="IntentCommand.Hold"/>.</para>
/// </summary>
public sealed class Brain
{
    private readonly AiRandom _random;
    private int _seq;

    public Brain(ActorId bot, ulong seed, BotMemory? memory = null, DecisionParams? decisions = null, IDecider? decider = null)
    {
        Decider = decider;
        Memory = memory;
        Decisions = decisions;
        if (!bot.IsValid)
            throw new ArgumentException("a brain needs a valid actor", nameof(bot));
        Bot = bot;
        Seed = seed;
        _random = new AiRandom(seed);
    }

    public ActorId Bot { get; }

    public ulong Seed { get; }

    /// <summary>What the bot remembers (§5.1), or null for a brain with no map to remember (a fact's).</summary>
    public BotMemory? Memory { get; }

    /// <summary>What the bot's skills make of its decisions (§10), or null for a brain with no curves (a fact's).</summary>
    public DecisionParams? Decisions { get; }

    /// <summary>The game's decision layer, or null for a brain that holds (a fact's, or one with no curves).</summary>
    public IDecider? Decider { get; }

    /// <summary>The last tick this brain stepped at, or null before its first step.</summary>
    public Tick? LastStep { get; private set; }

    /// <summary>Runs one decide step.</summary>
    public IntentCommand Step(Tick tick, PerceivedView view, ReadOnlySpan<AiEvent> events)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Bot != Bot)
            throw new ArgumentException($"the view is {view.Bot}'s, this brain is {Bot}'s", nameof(view));
        if (view.Tick != tick)
            throw new ArgumentException($"the view is for {view.Tick}, the step for {tick}", nameof(view));
        if (LastStep is { } last && tick <= last)
            throw new ArgumentOutOfRangeException(nameof(tick), tick.Value, $"a brain steps forward; it last stepped at {last}");

        LastStep = tick;
        Memory?.Step(tick, view, events);
        _ = _random;
        return Decider is { } d ? d.Decide(tick, view, ++_seq) : IntentCommand.Hold(Bot, tick, ++_seq);
    }
}
