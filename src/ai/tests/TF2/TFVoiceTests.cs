using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The voice menu, out (plan_bots_teams_tf2_part_g G.2.4, TFDecider.Voice.cs): a TF2 bot calls
/// "Medic!" when hurt, "Sentry ahead!" at a sentry it had not seen, "Spy!" at an enemy drawn as a
/// Spy and "Thanks!" after a Medic's heal, and does not spam: its Teamwork's wait between any
/// two lines, and no line twice inside half a minute.
/// </summary>
public sealed class TFVoiceTests
{
    private static readonly AiVector Mid = new(2048f, 256f, 0f);
    private static readonly ActorId Enemy = new(5, 0x4000_0001), Friend = new(4, 0x4000_0002);
    private const float Decide = DecideSchedule.Period * 0.015f;

    /// <summary>A bot that may speak every two seconds (the facts on Teamwork use the mod's curves instead).</summary>
    private static TFField Talker() => new(tune: p => p with { VoiceEverySeconds = 2f });

    private static ActorFacts Drawn(ActorId id, AiVector at, int team, TFClass playerClass)
        => new(id, at, AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, team, Class: (int)playerClass);

    private static ThingFacts Sentry(int id = 700)
        => new(id, ThingKind.Building, (int)TFThingType.Sentry, Mid + new AiVector(1500f, 0f, 0f), default, TFField.Blue, 5, 1);

    /// <summary>One decide: the bot at mid field on <paramref name="health"/>, with <paramref name="things"/> and <paramref name="sighted"/> in sight.</summary>
    private static IntentCommand Live(TFField f, int health = 200, ThingFacts[]? things = null, params ActorFacts[] sighted)
        => f.StepSeeing(TFField.Self(Mid, health: health), TFField.Ctf(), things ?? [], null, sighted);

    private static int RowOf(string name) => TFAbilities.Table().Rows.Select((r, i) => (r, i)).Single(x => x.r.Name == name).i;

    /// <summary>The line rides the decide's intent as the ability one-shot that names its row: the applier sends the row's voicemenu word once.</summary>
    [Fact]
    public void TheLineIsTheDecidesOneShotNamingItsRow()
    {
        IntentCommand i = Live(Talker(), health: 80);
        Assert.Equal((ActionKind.Ability, RowOf(TFAbilities.VoiceMedic), true), (i.Action.Kind, i.Action.WeaponSlot, i.Action.OneShotSeq != 0));
    }

    /// <summary>
    /// A bot the game holds still (TF2's pre-round freeze, <see cref="SelfFlags.Frozen"/> on its HUD) says
    /// nothing: hurt as the bot that calls for a Medic is, it sends no line while frozen, and calls once it
    /// is let go.
    /// </summary>
    [Fact]
    public void AFrozenBotSaysNothing()
    {
        TFField f = Talker();
        IntentCommand held = default;
        for (int n = 0; n < 4; n++)
            held = f.StepSeeing(TFField.Self(Mid, health: 80, extra: SelfFlags.Frozen), TFField.Ctf(), []);
        Assert.Equal((ActionKind.None, 0), (held.Action.Kind, f.Decider.VoiceLines));
        IntentCommand free = Live(f, health: 80);
        Assert.Equal((ActionKind.Ability, RowOf(TFAbilities.VoiceMedic), 1), (free.Action.Kind, free.Action.WeaponSlot, f.Decider.Said(TFVoiceLine.Medic)));
    }

    /// <summary>A decide carries one action: hurt with two rockets gone and nobody in sight, the reload is sent first and the call a decide later.</summary>
    [Fact]
    public void AReloadComesBeforeALine()
    {
        TFField f = Talker();
        IntentCommand first = f.StepSeeing(TFField.Self(Mid, health: 80, clip: 2), TFField.Ctf(), []);
        Assert.Equal((ActionKind.Reload, 0), (first.Action.Kind, f.Decider.VoiceLines));
        IntentCommand second = f.StepSeeing(TFField.Self(Mid, health: 80, clip: 2), TFField.Ctf(), []);
        Assert.Equal((ActionKind.Ability, 1), (second.Action.Kind, f.Decider.Said(TFVoiceLine.Medic)));
    }

    /// <summary>With an enemy Spy in its sights the bot lets the trigger go for the one decide its call takes, and fires again at the next.</summary>
    [Fact]
    public void ALineTakesTheTriggerForOneDecide()
    {
        TFField f = Talker();
        ActorFacts spy = Drawn(Enemy, Mid + new AiVector(700f, 0f, 0f), TFField.Blue, TFClass.Spy);
        IntentCommand first = Live(f, sighted: spy), second = Live(f, sighted: spy);
        Assert.Equal((ActionKind.Ability, RowOf(TFAbilities.VoiceSpy), ActionKind.Fire, 1), (first.Action.Kind, first.Action.WeaponSlot, second.Action.Kind, f.Decider.Shots));
    }

    [Fact]
    public void AHurtBotCallsForAMedic()
    {
        TFField f = Talker();
        Live(f, health: 80);
        Assert.Equal((1, 1), (f.Decider.Said(TFVoiceLine.Medic), f.Decider.VoiceLines));
    }

    [Fact]
    public void ABotOverHalfItsHealthCallsNoMedic()
    {
        TFField f = Talker();
        for (int i = 0; i < 20; i++)
            Live(f, health: 120);
        Assert.Equal(0, f.Decider.VoiceLines);
    }

    [Fact]
    public void ASentryNotSeenBeforeIsCalledOut()
    {
        TFField f = Talker();
        Live(f, things: [Sentry()]);
        Assert.Equal((1, 1), (f.Decider.Said(TFVoiceLine.SentryAhead), f.Decider.VoiceLines));
    }

    /// <summary>The same sentry seen again a minute later is no news; another one is.</summary>
    [Fact]
    public void OnlyANewSentryIsCalledOut()
    {
        TFField f = Talker();
        Live(f, things: [Sentry()]);
        for (int i = 0; i < (int)(60f / Decide); i++)
            Live(f);
        Live(f, things: [Sentry()]);
        Assert.Equal(1, f.Decider.Said(TFVoiceLine.SentryAhead));
        Live(f, things: [Sentry(id: 701)]);
        Assert.Equal(2, f.Decider.Said(TFVoiceLine.SentryAhead));
    }

    [Fact]
    public void AnEnemyDrawnAsASpyIsCalledOut()
    {
        TFField f = Talker();
        Live(f, sighted: Drawn(Enemy, Mid + new AiVector(700f, 0f, 0f), TFField.Blue, TFClass.Spy));
        Assert.Equal((1, 1), (f.Decider.Said(TFVoiceLine.Spy), f.Decider.VoiceLines));
    }

    /// <summary>What the bot's client draws is all it has: an enemy Soldier is no Spy, and a Spy drawn on its own team (a teammate, or a disguise it has no cue against) is not called.</summary>
    [Theory]
    [InlineData(TFField.Blue, TFClass.Soldier)]
    [InlineData(TFField.Red, TFClass.Spy)]
    public void OnlyAnEnemyDrawnAsASpyIsCalledASpy(int team, TFClass drawnAs)
    {
        TFField f = Talker();
        for (int i = 0; i < 10; i++)
            Live(f, sighted: Drawn(Enemy, Mid + new AiVector(1400f, 0f, 0f), team, drawnAs));
        Assert.Equal(0, f.Decider.Said(TFVoiceLine.Spy));
    }

    /// <summary>Healed 6 a decide by a Medic 200 u off: thanks once it adds up to <see cref="TFDecider.ThanksAfterHealth"/>.</summary>
    [Fact]
    public void AMedicsHealIsThanked()
    {
        TFField f = Talker();
        ActorFacts medic = Drawn(Friend, Mid + new AiVector(200f, 0f, 0f), TFField.Red, TFClass.Medic);
        for (int i = 0; i <= TFDecider.ThanksAfterHealth / 6 + 1; i++)
            Live(f, health: 110 + 6 * i, sighted: medic);
        Assert.Equal((1, 1), (f.Decider.Said(TFVoiceLine.Thanks), f.Decider.VoiceLines));
    }

    /// <summary>The same health gained with no Medic by (a dispenser's, a pack's a bit at a time) owes nobody thanks, nor does a pack's all at once with one by.</summary>
    [Theory]
    [InlineData(false, 6)]
    [InlineData(true, 60)]
    public void HealthNotFromAMedicIsNotThanked(bool medicBy, int step)
    {
        TFField f = Talker();
        ActorFacts[] seen = medicBy ? [Drawn(Friend, Mid + new AiVector(200f, 0f, 0f), TFField.Red, TFClass.Medic)] : [];
        for (int i = 0; i < 12; i++)
            Live(f, health: Math.Min(200, 110 + step * i), sighted: seen);
        Assert.Equal(0, f.Decider.Said(TFVoiceLine.Thanks));
    }

    /// <summary>Hurt for a minute and a half: "Medic!" at most once a <see cref="TFDecider.SameLineSeconds"/>, though it may speak every two seconds.</summary>
    [Fact]
    public void TheSameLineIsNotSaidAgainInsideHalfAMinute()
    {
        TFField f = Talker();
        const float Seconds = 90f;
        for (int i = 0; i < (int)(Seconds / Decide); i++)
            Live(f, health: 60);
        Assert.InRange(f.Decider.Said(TFVoiceLine.Medic), 2, (int)(Seconds / TFDecider.SameLineSeconds) + 1);
    }

    /// <summary>Hurt, with a new sentry and a Spy in sight at once: one line a decide, and the next not before its wait.</summary>
    [Fact]
    public void TwoLinesAreAtLeastTheBotsWaitApart()
    {
        var f = new TFField(tune: p => p with { VoiceEverySeconds = 3f });
        ActorFacts spy = Drawn(Enemy, Mid + new AiVector(1500f, 0f, 0f), TFField.Blue, TFClass.Spy);
        Live(f, health: 60, things: [Sentry()], sighted: spy);
        Assert.Equal(1, f.Decider.VoiceLines);
        int decides = 1;
        while (f.Decider.VoiceLines < 2 && decides < 200)
        {
            Live(f, health: 60, things: [Sentry()], sighted: spy);
            decides++;
        }

        Assert.InRange((decides - 1) * Decide, 3f, 3f + 2 * Decide);
    }

    /// <summary>The most pressing first: a Spy before a sentry before the bot's own health.</summary>
    [Fact]
    public void ASpyIsCalledBeforeASentryAndASentryBeforeAMedic()
    {
        TFField f = Talker();
        ActorFacts spy = Drawn(Enemy, Mid + new AiVector(1500f, 0f, 0f), TFField.Blue, TFClass.Spy);
        var order = new List<TFVoiceLine>();
        for (int i = 0; i < 60 && order.Count < 3; i++)
        {
            int before = f.Decider.VoiceLines;
            Live(f, health: 60, things: [Sentry()], sighted: spy);
            if (f.Decider.VoiceLines > before)
                order.Add(new[] { TFVoiceLine.Spy, TFVoiceLine.SentryAhead, TFVoiceLine.Medic }.First(l => f.Decider.Said(l) == 1 && !order.Contains(l)));
        }

        Assert.Equal([TFVoiceLine.Spy, TFVoiceLine.SentryAhead, TFVoiceLine.Medic], order);
    }

    /// <summary>On the mod's curves, hurt and shown a new sentry every ten seconds for two minutes: a bot glued to its team says several times what a lone wolf says.</summary>
    [Fact]
    public void ALoneWolfSaysLittleAndATeamPlayerMore()
    {
        int Lines(int teamwork)
        {
            var f = new TFField(profile: TFSkillCurvesTests.Profile("teamwork", teamwork), curves: TFSkillCurvesTests.ShippedCurves());
            int perTen = (int)(10f / Decide);
            for (int i = 0; i < 12 * perTen; i++)
                Live(f, health: 60, things: i % perTen == 0 ? [Sentry(id: 700 + i / perTen)] : []);
            return f.Decider.VoiceLines;
        }

        (int lone, int glued) = (Lines(0), Lines(10));
        Assert.True(lone >= 1 && glued >= lone * 2, $"{lone} lines in two minutes at Teamwork 0, {glued} at 10");
    }

    /// <summary>A line whose moment came while the bot could not speak is dropped: a sentry first seen during its wait is not called ten seconds later.</summary>
    [Fact]
    public void ALineNotSaidInItsMomentIsDropped()
    {
        var f = new TFField(tune: p => p with { VoiceEverySeconds = 10f });
        Live(f, health: 60);
        Assert.Equal(1, f.Decider.Said(TFVoiceLine.Medic));
        Live(f, things: [Sentry()]);
        for (int i = 0; i < (int)(12f / Decide); i++)
            Live(f);
        Assert.Equal(0, f.Decider.Said(TFVoiceLine.SentryAhead));
    }
}
