using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using TF2.Shared.Buildings;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A building's sapper as the bot's team sees it (round 2's S7, TFDecider.Sapped.cs): read sapped in sight, never unseen, and
/// a builder's own through walls by its HUD tray; and the general play's answer, going to stand by a sapped friendly building.
/// </summary>
public sealed class TFSappedTests
{
    private const int Mate = 5, SentryId = 700;
    private static readonly AiVector RedSide = new(600f, 256f, 0f);
    private static readonly int Sapped = (int)TFBuildingFlags.Sapped;

    private static ThingFacts Sentry(AiVector at, int team, int owner, int flags)
        => new(SentryId, ThingKind.Building, (int)TFThingType.Sentry, at, default, team, owner, 1, -1, flags);

    private static SelfState Soldier(AiVector at) => TFField.Self(at);

    /// <summary>A friendly sentry in sight with a sapper drawn on it reads sapped; the same sentry with none does not.</summary>
    [Fact]
    public void ASappedBuildingTheBotSeesReadsSapped()
    {
        bool Reads(int flags)
        {
            var f = new TFField();
            f.StepSeeing(Soldier(RedSide), TFField.Ctf(), [Sentry(RedSide + new AiVector(300f, 0f, 0f), TFField.Red, Mate, flags)]);
            return f.Decider.SeesSapped(SentryId);
        }

        Assert.Equal((true, false), (Reads(Sapped), Reads(0)));
    }

    /// <summary>An enemy sentry seen sapped reads sapped while in sight, and not on the next decide, when it is out of sight.</summary>
    [Fact]
    public void AnUnseenEnemyBuildingDoesNotReadSapped()
    {
        var f = new TFField();
        f.StepSeeing(Soldier(RedSide), TFField.Ctf(), [Sentry(RedSide + new AiVector(300f, 0f, 0f), TFField.Blue, Mate, Sapped)]);
        bool inSight = f.Decider.SeesSapped(SentryId);
        f.Step(Soldier(RedSide), TFField.Ctf());
        Assert.Equal((true, false), (inSight, f.Decider.SeesSapped(SentryId)));
    }

    /// <summary>
    /// The bot's own sentry, seen once unsapped and then out of sight, reads sapped through walls while its HUD tray flags its
    /// sentry sapped, and not while the tray shows it clear. A teammate's sentry out of sight does not, whatever the bot's own
    /// tray says: stock shows a builder his own buildings sapped, and nobody else's.
    /// </summary>
    [Fact]
    public void ItsOwnBuildingReadsSappedThroughWalls()
    {
        bool Reads(int owner, float tray)
        {
            var f = new TFField();
            f.StepSeeing(Soldier(RedSide), TFField.Ctf(), [Sentry(RedSide + new AiVector(300f, 0f, 0f), TFField.Red, owner, 0)]);
            f.Hud = new TFHud([], [new TFMeter(TFBuildingTrayMeters.SentrySapped, tray)], []);
            f.Step(Soldier(RedSide), TFField.Ctf());
            return f.Decider.SeesSapped(SentryId);
        }

        int own = new TFField().Bot.Slot;
        Assert.Equal((true, false, false), (Reads(own, 1f), Reads(own, 0f), Reads(Mate, 1f)));
    }

    /// <summary>The tray's meters are the bot's own rows: one per building standing, 1 for a sapped one, none for an empty row.</summary>
    [Fact]
    public void TheTrayShowsEachOwnBuildingStandingAndWhetherItIsSapped()
    {
        const byte present = TFBuildingResourceNetState.Present, sapped = TFBuildingResourceNetState.Sapped;
        var into = new List<TFMeter>();
        TFBuildingTrayMeters.Rows([present | sapped, 0, present, 0], into);
        Assert.Equal([new TFMeter(TFBuildingTrayMeters.SentrySapped, 1f), new TFMeter(TFBuildingTrayMeters.EntranceSapped, 0f)], into);
    }

    /// <summary>The flags the view draws: a sapper, a teleporter end ready to send, and a building under construction, each its own bit.</summary>
    [Fact]
    public void TheViewDrawsASapperAndAReadyTeleporterAsTheirOwnBits()
        => Assert.Equal(((int)TFBuildingFlags.Sapped, (int)TFBuildingFlags.Ready, (int)TFBuildingFlags.Building, 0),
                        (TFKnowledge.BuildingFlags(sapped: true, ready: false), TFKnowledge.BuildingFlags(sapped: false, ready: true),
                         TFKnowledge.BuildingFlags(false, false, building: true), TFKnowledge.BuildingFlags(false, false)));

    /// <summary>
    /// The general play's answer: a Soldier on its own side with a friendly sentry 400 u off in sight, sapped, goes to it on the
    /// class branch. Unsapped it does not, nor sapped 1,400 u off (past <see cref="TFDecider.SappedAnswerReach"/>).
    /// </summary>
    [Fact]
    public void ANearbyTeammateGoesToASappedFriendlySentryItSees()
    {
        (string Branch, string Verdict, AiVector Goal) Answer(float distance, int flags)
        {
            var f = new TFField();
            AiVector at = RedSide + new AiVector(distance, 0f, 0f);
            for (int i = 0; i < 4; i++)
                f.StepSeeing(Soldier(RedSide), TFField.Ctf(), [Sentry(at, TFField.Red, Mate, flags)]);
            return (f.Branch, f.Decider.Doing.Verdict, f.Decider.Doing.Goal);
        }

        Assert.Equal((TFDecider.ClassBranch, TFDecider.SappedGoingVerdict, RedSide + new AiVector(400f, 0f, 0f)), Answer(400f, Sapped));
        Assert.NotEqual(TFDecider.ClassBranch, Answer(400f, 0).Branch);
        Assert.NotEqual(TFDecider.ClassBranch, Answer(1400f, Sapped).Branch);
    }

    /// <summary>A class whose play answers no sapper (<c>AnswersSapped</c> 0) does not go, by the same sight.</summary>
    [Fact]
    public void AClassThatDoesNotAnswerStaysOnItsWay()
    {
        var f = new TFField(plays: (d, c) => c == TFClass.Soldier ? new DeafPlay(d) : null);
        for (int i = 0; i < 4; i++)
            f.StepSeeing(Soldier(RedSide), TFField.Ctf(), [Sentry(RedSide + new AiVector(400f, 0f, 0f), TFField.Red, Mate, Sapped)]);
        Assert.NotEqual(TFDecider.ClassBranch, f.Branch);
    }

    private sealed class DeafPlay(TFDecider d) : TFDecider.ClassPlay(d, TFClass.Soldier)
    {
        public override float AnswersSapped => 0f;
    }
}
