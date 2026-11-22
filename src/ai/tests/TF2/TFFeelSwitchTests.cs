using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.TF2;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using TF2.Shared.Movement;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The feel layer's switches (<see cref="TFFeelSwitches"/>, the <c>ai_tf_feel_*</c> convars) on the
/// synthetic field (<see cref="TFField"/>): each part off is what the decider did before it had the
/// part, shown on the scenario of that part's own fact with the part on as the control; and the
/// service reads each convar into its own switch.
/// </summary>
public sealed class TFFeelSwitchTests
{
    private static readonly ActorId A = new(5, 0x4000_0001), B = new(6, 0x4000_0002), Mate = new(4, 0x4000_0003);
    private static readonly AiVector Mid = new(2048f, 256f, 0f);
    private static readonly TFFeelSwitches On = TFFeelSwitches.All;

    private static string Branch(TFField f) => f.Decider.Doing.Branch[(f.Decider.Doing.Branch.IndexOf(": ", StringComparison.Ordinal) + 2)..];

    private static ActorFacts Seen(ActorId id, float east, int team = TFField.Blue)
        => new(id, Mid + new AiVector(east, 0f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, team, (int)TFClass.Soldier);

    // ---------------------------------------------------------------- Carrier (TFTargetTests)

    /// <summary>The target with A in sight 200 u off and B, carrying the bot's flag, 900 u off.</summary>
    private static ActorId TargetOfANearEnemyAndTheCarrier(TFFeelSwitches feel)
    {
        var f = new TFField(feel: feel);
        ObjectiveKnowledge carried = TFField.Ctf(red: ObjectiveState.Carried, redAt: Mid + new AiVector(900f, 0f, 0f), redCarrier: B.Slot);
        f.StepDrawn(TFField.Self(Mid), carried, Seen(A, 200f), Seen(B, 900f));
        return f.Decider.Doing.Target;
    }

    [Fact]
    public void WithCarrierOffTheNearerEnemyIsTheTargetBeforeTheOneCarryingTheBotsFlag()
    {
        Assert.Equal(B, TargetOfANearEnemyAndTheCarrier(On));
        Assert.Equal(A, TargetOfANearEnemyAndTheCarrier(On with { Carrier = false }));
    }

    // ---------------------------------------------------------------- Search (TFSearchTests)

    /// <summary>The branch of a bot with nothing on its HUD, a second after it heard gunfire 800 u off.</summary>
    private static string BranchAfterASound(TFFeelSwitches feel)
    {
        var f = new TFField(feel: feel, tune: p => p with { SearchSeconds = 12f });
        f.StepHearing(TFField.Self(Mid), null, []);
        var heard = AiEvent.SoundHeard(f.Bot, new Tick(f.Tick + DecideSchedule.Period), SoundKind.Gunfire, new AiVector(2848f, 128f, 0f), 1f, ActorId.None, -1);
        f.StepHearing(TFField.Self(Mid), null, [heard]);
        for (int k = 0; k < 12; k++)
            f.StepHearing(TFField.Self(Mid), null, []);
        return Branch(f);
    }

    [Fact]
    public void WithSearchOffABotWithNothingBetterToDoDoesNotGoToTheSoundItHeard()
    {
        Assert.Equal(TFDecider.Search, BranchAfterASound(On));
        Assert.Equal(TFDecider.Roam, BranchAfterASound(On with { Search = false }));
    }

    // ---------------------------------------------------------------- Aim (TFAimTests)

    private const float ErrorDeg = 3f, BodyEast = 300f;

    /// <summary>What a draw of the skill's angle can be off by at the body's distance: 15.7 u.</summary>
    private static readonly float ErrorSize = MathF.Tan(ErrorDeg * (MathF.PI / 180f)) * BodyEast;

    /// <summary>
    /// The aim's error on a body standing still 300 u east, a decide each: across the line (the line
    /// runs along x, so across is y) and up from the body's middle. The shotgun: no flight to lead.
    /// </summary>
    private static (float[] Across, float[] Up) AimErrors(TFFeelSwitches feel)
    {
        var f = new TFField(graph: false, feel: feel, tune: p => p with { AimErrorDeg = ErrorDeg });
        ActorFacts body = Seen(A, BodyEast);
        float middle = body.Origin.Z + TFViewVectors.Params.HullMax.Z / 2f;
        var across = new float[600];
        var up = new float[600];
        for (int i = 0; i < across.Length; i++)
        {
            AiVector at = f.StepDrawn(TFField.Self(Mid, weapon: TFField.Shotgun, clip: 6), TFField.Ctf(), body).Look.Point;
            (across[i], up[i]) = (at.Y - body.Origin.Y, at.Z - middle);
        }

        return (across, up);
    }

    /// <summary>The correlation of each of <paramref name="x"/> with the one after it.</summary>
    private static float Lag1(float[] x)
    {
        float[] a = x[..^1], b = x[1..];
        float ma = a.Average(), mb = b.Average(), ab = 0f, aa = 0f, bb = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            ab += (a[i] - ma) * (b[i] - mb);
            aa += (a[i] - ma) * (a[i] - ma);
            bb += (b[i] - mb) * (b[i] - mb);
        }

        return ab / MathF.Sqrt(aa * bb);
    }

    [Fact]
    public void WithAimOffOneDecidesErrorTellsNothingOfTheNexts()
    {
        Assert.True(Lag1(AimErrors(On).Across) > 0.9f, "the control: the wandering error drifts");
        float[] x = AimErrors(On with { Aim = false }).Across;
        Assert.True(x.Max() - x.Min() > 5f, $"the control: the aim errs, over {x.Max() - x.Min():F1} u");
        Assert.InRange(Lag1(x), -0.2f, 0.2f);
    }

    /// <summary>The draw it was: anywhere within the skill's angle across, half that up, and no further (a wandering error has no edge).</summary>
    [Fact]
    public void WithAimOffTheErrorIsADrawWithinTheSkillsAngleAcrossAndHalfItUp()
    {
        (float[] across, float[] up) = AimErrors(On with { Aim = false });
        Assert.InRange(across.Min(), -ErrorSize - 0.01f, -0.9f * ErrorSize);
        Assert.InRange(across.Max(), 0.9f * ErrorSize, ErrorSize + 0.01f);
        Assert.InRange(up.Min(), -ErrorSize / 2f - 0.01f, -0.45f * ErrorSize);
        Assert.InRange(up.Max(), 0.45f * ErrorSize, ErrorSize / 2f + 0.01f);
    }

    // ---------------------------------------------------------------- Retreat (TFRetreatTests)

    /// <summary>The branch of a timid bot (it leaves a fight at 60 %) by the west pack at <paramref name="health"/> of 200, an enemy in sight.</summary>
    private static string TimidUnderFireAt(TFFeelSwitches feel, int health)
    {
        var here = new AiVector(700f, 64f, 0f);
        var f = new TFField(feel: feel, tune: p => p with { RetreatHealthPct = 0.6f, GiveUpHealth = 0f });
        f.Step(TFField.Self(here, health: health), TFField.Ctf(), (A, here + new AiVector(600f, 0f, 0f), AiVector.Zero, TFField.Blue));
        return Branch(f);
    }

    [Fact]
    public void WithRetreatOffATimidBotUnderFireGoesForHealthUnderFortyPercentAndNotAtFiftyFive()
    {
        Assert.Equal(TFDecider.Survive, TimidUnderFireAt(On, 110));
        TFFeelSwitches off = On with { Retreat = false };
        Assert.NotEqual(TFDecider.Survive, TimidUnderFireAt(off, 110));
        Assert.Equal(TFDecider.Survive, TimidUnderFireAt(off, 70));
    }

    // ---------------------------------------------------------------- FriendGate (TFFriendGateTests)

    /// <summary>The decide with an enemy 600 u east and a teammate on the line to it, 250 u east.</summary>
    private static (ActionKind Action, int Held, int Shots) WithATeammateOnTheLine(TFFeelSwitches feel)
    {
        var f = new TFField(feel: feel, tune: p => p with { AimErrorDeg = 0f });
        IntentCommand i = f.StepDrawn(TFField.Self(Mid), TFField.Ctf(), Seen(A, 600f), Seen(Mate, 250f, team: TFField.Red));
        Assert.Equal(A, f.Decider.Doing.Target);
        return (i.Action.Kind, f.Decider.HeldFireFriend, f.Decider.Shots);
    }

    [Fact]
    public void WithFriendGateOffTheBotFiresWithATeammateOnTheLineToItsTarget()
    {
        Assert.Equal((ActionKind.None, 1, 0), WithATeammateOnTheLine(On));
        Assert.Equal((ActionKind.Fire, 0, 1), WithATeammateOnTheLine(On with { FriendGate = false }));
    }

    // ---------------------------------------------------------------- Glance (TFGlanceTests)

    /// <summary>The glances taken, and the decides spent looking aside, over three minutes of a run to the enemy's flag with nobody in sight.</summary>
    private static (int Taken, int Glancing) GlancesOnTheWay(TFFeelSwitches feel)
    {
        var f = new TFField(graph: false, feel: feel, tune: p => p with { GlanceRate = 0.4f });
        int glancing = 0;
        for (int i = 0; i < 2000; i++)
            glancing += f.Step(TFField.Self(Mid), TFField.Ctf()).Look.Kind == LookKind.Glance ? 1 : 0;
        return (f.Decider.Glances, glancing);
    }

    [Fact]
    public void WithGlanceOffTheBotOnItsWayNeverGlances()
    {
        (int taken, int glancing) = GlancesOnTheWay(On);
        Assert.True(taken > 30 && glancing > taken, $"the control: {taken} glances over {glancing} decides");
        Assert.Equal((0, 0), GlancesOnTheWay(On with { Glance = false }));
    }

    // ---------------------------------------------------------------- the convars

    private static T ConVar<T>(string? off) where T : ConVarBase
    {
        T convar = DeclaredConVars.Fresh<T>();
        if (convar.Name == off)
            convar.Hold("0");
        return convar;
    }

    /// <summary>The service over nine convars of its own, each at its default but the one named <paramref name="off"/>, held at 0.</summary>
    internal static TFFeelSwitchConVars Switches(string? off = null)
        => new(ConVar<TFFeelCarrierConVar>(off), ConVar<TFFeelSearchConVar>(off), ConVar<TFFeelAimConVar>(off), ConVar<TFFeelRetreatConVar>(off),
               ConVar<TFFeelFriendGateConVar>(off), ConVar<TFFeelGlanceConVar>(off), ConVar<TFFeelPrefireConVar>(off), ConVar<TFFeelJumpHealthConVar>(off),
               ConVar<TFFeelFightConVar>(off));

    [Fact]
    public void AsDeclaredEveryPartIsOn()
        => Assert.Equal(new TFFeelSwitches(true, true, true, true, true, true, true, true, true), Switches().Read());

    [Theory]
    [InlineData("ai_tf_feel_carrier", false, true, true, true, true, true, true, true, true)]
    [InlineData("ai_tf_feel_search", true, false, true, true, true, true, true, true, true)]
    [InlineData("ai_tf_feel_aim", true, true, false, true, true, true, true, true, true)]
    [InlineData("ai_tf_feel_retreat", true, true, true, false, true, true, true, true, true)]
    [InlineData("ai_tf_feel_friendgate", true, true, true, true, false, true, true, true, true)]
    [InlineData("ai_tf_feel_glance", true, true, true, true, true, false, true, true, true)]
    [InlineData("ai_tf_feel_prefire", true, true, true, true, true, true, false, true, true)]
    [InlineData("ai_tf_feel_jumphealth", true, true, true, true, true, true, true, false, true)]
    [InlineData("ai_tf_feel_fight", true, true, true, true, true, true, true, true, false)]
    public void AConVarAtZeroSwitchesOffItsOwnPartAndNoOther(string convar, bool carrier, bool search, bool aim, bool retreat, bool friendGate, bool glance, bool prefire,
                                                             bool jumpHealth, bool fight)
        => Assert.Equal(new TFFeelSwitches(Carrier: carrier, Search: search, Aim: aim, Retreat: retreat, FriendGate: friendGate, Glance: glance, Prefire: prefire,
                                           JumpHealth: jumpHealth, Fight: fight),
                        Switches(off: convar).Read());

    /// <summary>A bot added with a switch off plays differently from the server's others: each is a cheat, as <c>ai_sync</c> is.</summary>
    [Theory]
    [InlineData(typeof(TFFeelCarrierConVar))]
    [InlineData(typeof(TFFeelSearchConVar))]
    [InlineData(typeof(TFFeelAimConVar))]
    [InlineData(typeof(TFFeelRetreatConVar))]
    [InlineData(typeof(TFFeelFriendGateConVar))]
    [InlineData(typeof(TFFeelGlanceConVar))]
    [InlineData(typeof(TFFeelPrefireConVar))]
    [InlineData(typeof(TFFeelJumpHealthConVar))]
    [InlineData(typeof(TFFeelFightConVar))]
    public void EachSwitchIsACheat(Type convar)
        => Assert.Equal(ConVarFlags.Cheat, convar.GetCustomAttribute<ConVarAttribute>()!.Flags & ConVarFlags.Cheat);
}
