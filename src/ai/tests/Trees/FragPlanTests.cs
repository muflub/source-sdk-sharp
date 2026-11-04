using SourceSharp.Ai.Hl2mp.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Trees;

/// <summary>
/// A frag, as a person throws one (plan_hl2mp_ai §5.4, ruling 2026-11-03: "also bots dont use grenades at
/// all"): the switch, which takes what it takes, the throw only then, and back to the gun.
/// </summary>
public sealed class FragPlanTests
{
    private static readonly AiVector Place = new(600f, 0f, 0f);

    /// <summary>What a person's hands would do with these intents: the weapon in hand and the frags carried, as the HUD would show.</summary>
    private sealed class Hands(Duel d, int grenades, int gun = Duel.Smg1)
    {
        public int Active = gun, Grenades = grenades;
        public readonly List<string> Told = new();
        private int _answered, _switchingTo = -1, _switchLeft, _pinLeft = -1, _decides, _redrawLeft;

        /// <summary>The decide of the last throw, and of the first weapon asked for after it.</summary>
        public int ThrownAt = -1, BackAt = -1;

        public SelfState Self => d.Self(clip: Active == Duel.Frag ? 0 : 30, reserve: Active == Duel.Frag ? Grenades : 90, weapon: Active) with { Grenades = Grenades };

        /// <summary>Lines answered clear or not.</summary>
        public AiEvent[] Answers(AiEvent[] events, bool clear = true)
        {
            DoingRecord doing = d.Decider.Doing;
            if (doing.LineAsk <= _answered)
                return events;
            _answered = doing.LineAsk;
            return [.. events, AiEvent.LineAnswer(Duel.Bot, new Tick(d.Tick + 6), doing.LineAt, doing.LineAsk, clear)];
        }

        /// <summary>A switch lands five decides on (0.45 s), and a pin goes four after the press.</summary>
        public void Saw(in IntentCommand c)
        {
            _decides++;
            if (_redrawLeft > 0)
                _redrawLeft--;
            if (_switchLeft > 0 && --_switchLeft == 0)
                Active = _switchingTo;
            if (_pinLeft > 0 && --_pinLeft == 0)
            {
                Grenades--;
                ThrownAt = _decides;
                _redrawLeft = 8;
                Told.Add("thrown");
            }

            ActionIntent a = c.Action;
            if (a.Kind == ActionKind.SelectWeapon)
            {
                _switchingTo = a.WeaponSlot - Hl2mpDecider.SelectByKind;
                _switchLeft = 5;
                if (ThrownAt >= 0 && BackAt < 0)
                    BackAt = _decides;
                Told.Add("select " + _switchingTo);
            }
            else if (a.Kind is ActionKind.Fire or ActionKind.AltFire)
            {
                Told.Add((a.Kind == ActionKind.Fire ? "fire with " : "alt with ") + Active);
                // The hand is empty until the next is drawn (CWeaponFrag's m_bRedraw).
                if (Active == Duel.Frag && _pinLeft <= 0 && _redrawLeft == 0 && Grenades > 0)
                    _pinLeft = 4;
            }
        }
    }

    private static AiEvent HeldKinds(Duel d) => AiEvent.Holdings(Duel.Bot, new Tick(d.Tick + 6), (1 << Duel.Smg1) | (1 << Duel.Frag));

    private static Duel Bot(string preset = "hard", ulong seed = 3, int aggression = 10)
    {
        SkillProfile p = Duel.Preset(preset);
        return new Duel(p.With(p.IndexOf("aggression"), aggression), Duel.Bot, seed, null, null, null);
    }

    /// <summary>Someone seen, then gone behind something, again and again: how the hands went.</summary>
    private static Hands AroundACorner(Duel d, int grenades = 1, int rounds = 60, bool clear = true)
    {
        var h = new Hands(d, grenades);
        for (int round = 0; round < rounds; round++)
        {
            h.Saw(d.Step(h.Self, [(Duel.Opponent, Place)], [.. d.SeeAt(Duel.Opponent, Place), HeldKinds(d)]));
            for (int i = 0; i < 30; i++)
                h.Saw(d.Step(h.Self, [], h.Answers([], clear)));
        }

        return h;
    }

    [Fact]
    public void ABotWithSomeoneJustBehindACornerThrows()
    {
        Hands h = AroundACorner(Bot());
        Assert.Contains("thrown", h.Told);
    }

    /// <summary>The order is the rule: the weapon asked for, and nothing pressed on the gun or the frag before it is in hand.</summary>
    [Fact]
    public void TheThrowOnlyHappensAfterTheSwitch()
    {
        Hands h = AroundACorner(Bot());
        int selected = h.Told.IndexOf("select " + Duel.Frag);
        int thrown = h.Told.IndexOf("thrown");
        Assert.True(selected >= 0 && thrown > selected, string.Join(", ", h.Told.Take(40)));
        Assert.DoesNotContain(h.Told.Skip(selected).Take(thrown - selected), t => t.EndsWith("with " + Duel.Smg1, StringComparison.Ordinal));
        Assert.Contains(h.Told.Take(thrown), t => t.EndsWith("with " + Duel.Frag, StringComparison.Ordinal));
    }

    [Fact]
    public void ItSwitchesBackToTheGunItHad()
    {
        Hands h = AroundACorner(Bot());
        int thrown = h.Told.IndexOf("thrown");
        Assert.True(thrown >= 0);
        Assert.Equal("select " + Duel.Smg1, h.Told.Skip(thrown + 1).First(t => t.StartsWith("select", StringComparison.Ordinal)));
        Assert.Equal(Duel.Smg1, h.Active);
        Assert.InRange(h.BackAt - h.ThrownAt, 0, 2);
    }

    [Fact]
    public void NoGrenadesLeftNoneThrown()
    {
        Hands h = AroundACorner(Bot(), grenades: 0);
        Assert.DoesNotContain("select " + Duel.Frag, h.Told);
    }

    /// <summary>A frag at someone standing right there, in a group, is one at its own feet.</summary>
    [Fact]
    public void NoThrowAtItsFeet()
    {
        Duel d = Bot();
        var feet = new AiVector(150f, 0f, 0f);
        var h = new Hands(d, 1);
        for (int i = 0; i < 400; i++)
            h.Saw(d.Step(h.Self, [(Duel.Opponent, feet), (Duel.Other, feet + new AiVector(20f, 0f, 0f))], i == 0 ? [.. d.SeeAt(Duel.Opponent, feet), HeldKinds(d)] : h.Answers([])));
        Assert.DoesNotContain("select " + Duel.Frag, h.Told);
    }

    /// <summary>A rail in front of it, and the game says so: Hard and Expert do not throw at it. A person's mistake, at a low spend, happens.</summary>
    [Theory]
    [InlineData("hard")]
    [InlineData("expert")]
    public void AGoodBotDoesNotThrowIntoWhatIsInFrontOfIt(string preset)
    {
        for (ulong seed = 1; seed <= 8; seed++)
            Assert.DoesNotContain("thrown", AroundACorner(Bot(preset, seed), clear: false).Told);
    }

    [Fact]
    public void AnEasyBotSometimesDoes()
    {
        int thrown = 0;
        for (ulong seed = 1; seed <= 40; seed++)
            thrown += AroundACorner(Bot("easy", seed), clear: false, rounds: 30).Told.Contains("thrown") ? 1 : 0;
        Assert.True(thrown > 0, "no careless throw over 40 bots");
    }

    /// <summary>What a bot's favourite does: Aggression readies the hand, reading how far it sits above the bot's own mean (§10).</summary>
    [Fact]
    public void AggressionThrowsMoreReadily()
    {
        double Throws(int level)
        {
            int all = 0;
            for (ulong seed = 1; seed <= 16; seed++)
                all += AroundACorner(Bot("hard", seed, level), grenades: 40, rounds: 20).Told.Count(t => t == "thrown");
            return all;
        }

        double low = Throws(1), mid = Throws(5), high = Throws(10);
        Assert.True(low < mid && mid < high, $"throws at Aggression 1, 5, 10: {low}, {mid}, {high}");
    }

    /// <summary>The difficulty's say: a bot's taste for frags is Aggression's height, how much of it gets through its level of Cunning.</summary>
    [Fact]
    public void CunningKnowsWhenAFragIsWanted()
    {
        var c = SourceSharp.Ai.Tests.Skills.Curves.Game();
        float Knack(string preset) => c.For(Duel.Preset(preset)).FragKnack;
        Assert.True(Knack("easy") < Knack("normal") && Knack("normal") < Knack("hard") && Knack("hard") < Knack("expert"),
                    $"{Knack("easy")}, {Knack("normal")}, {Knack("hard")}, {Knack("expert")}");
    }

    /// <summary>Cunning places it: the same throw, spread by the level.</summary>
    [Fact]
    public void CunningPlacesItBetter()
    {
        var c = SourceSharp.Ai.Tests.Skills.Curves.Game();
        Assert.True(c.For(Duel.Preset("easy")).FragAimDeg > c.For(Duel.Preset("hard")).FragAimDeg);
        Assert.True(c.For(Duel.Preset("hard")).FragAimDeg > c.For(Duel.Preset("expert")).FragAimDeg);
    }

    [Fact]
    public void AnIdleBotWithNoFragsWalksToOne()
    {
        var kit = new ItemSpawn(0, "weapon_frag", ItemKind.Weapon, new AiVector(500f, 0f, 0f), 20f, Duel.Frag);
        var d = new Duel(Duel.Preset("normal"), kit);
        IntentCommand c = d.Step(d.Self() with { Grenades = 0 }, [], AiEvent.Holdings(Duel.Bot, new Tick(d.Tick + 6), 1 << Duel.Smg1));
        Assert.Equal(MoveKind.Follow, c.Move.Kind);
        Assert.Equal("arm", d.Decider.Doing.Leaf);
    }

    [Fact]
    public void WithOneItLeavesThemBe()
    {
        var kit = new ItemSpawn(0, "weapon_frag", ItemKind.Weapon, new AiVector(500f, 0f, 0f), 20f, Duel.Frag);
        var d = new Duel(Duel.Preset("normal"), kit);
        d.Step(d.Self() with { Grenades = 1 }, [], AiEvent.Holdings(Duel.Bot, new Tick(d.Tick + 6), (1 << Duel.Smg1) | (1 << Duel.Frag)));
        Assert.NotEqual("arm", d.Decider.Doing.Leaf);
    }

    [Fact]
    public void TheSameSeedThrowsAtTheSameDecides()
    {
        Assert.Equal(AroundACorner(Bot(seed: 9)).Told, AroundACorner(Bot(seed: 9)).Told);
    }
}
