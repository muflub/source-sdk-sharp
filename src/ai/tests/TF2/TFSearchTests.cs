using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A TF2 bot goes to look into a sound (plan_bots_teams_tf2_part_g G.2.1) on the synthetic field
/// (<see cref="TFField"/>): a rocket or an enemy's footsteps heard and not explained turn a bot with
/// nothing better to do, and a defender when it is by its flag; not an attacker on its way to the
/// enemy's flag, not with an enemy in sight, not for a teammate's own steps, not into the enemy's
/// respawn room; the sound goes stale by the bot's skill and is settled when reached.
/// </summary>
public sealed class TFSearchTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001), Mate = new(4, 0x4000_0003);
    private static readonly AiVector Mid = new(2048f, 256f, 0f), Noise = new(2848f, 128f, 0f);

    private static string Branch(TFField f) => f.Decider.Doing.Branch[(f.Decider.Doing.Branch.IndexOf(": ", StringComparison.Ordinal) + 2)..];

    private static AiEvent Heard(TFField f, AiVector at, SoundKind kind = SoundKind.Gunfire, ActorId? by = null)
        => AiEvent.SoundHeard(f.Bot, new Tick(f.Tick + DecideSchedule.Period), kind, at, 1f, by ?? ActorId.None, -1);

    private static TFField Bot(int slot = 2, float searchSeconds = 12f) => new(slot: slot, tune: p => p with { SearchSeconds = searchSeconds });

    private static TFField Defender(float searchSeconds = 12f) => TFField.Defender(p => p with { SearchSeconds = searchSeconds });

    /// <summary>With nothing on its HUD the bot only roams: a sound is the better thing to do.</summary>
    [Fact]
    public void ABotWithNothingBetterToDoGoesToTheSoundItHeard()
    {
        TFField f = Bot();
        f.StepHearing(TFField.Self(Mid), null, []);
        Assert.Equal(TFDecider.Roam, Branch(f));

        // Its roam is held a second (the root's commitment); then the sound takes it.
        IntentCommand i = f.StepHearing(TFField.Self(Mid), null, [Heard(f, Noise)]);
        for (int k = 0; k < 12; k++)
            i = f.StepHearing(TFField.Self(Mid), null, []);
        Assert.Equal((TFDecider.Search, "search", "heard something"), (Branch(f), f.Decider.Doing.Leaf, f.Decider.Doing.Verdict));
        Assert.Equal((MoveKind.Follow, Noise), (i.Move.Kind, i.Move.Corner0));
    }

    [Fact]
    public void AnAttackerOnItsWayToTheEnemysFlagIsNotTurnedByASound()
    {
        TFField f = Bot();
        f.StepHearing(TFField.Self(Mid), TFField.Ctf(), [Heard(f, Noise)]);
        Assert.Equal(TFDecider.Fetch, Branch(f));
    }

    [Fact]
    public void ADefenderLooksIntoASoundByItsFlag()
    {
        TFField f = Defender();
        f.StepHearing(TFField.Self(TFField.RedFlag), TFField.Ctf(), [Heard(f, TFField.RedFlag + new AiVector(700f, 0f, 0f))]);
        Assert.Equal(TFTeamRole.Defend, f.Decider.Role);
        Assert.Equal(TFDecider.Search, Branch(f));
    }

    [Fact]
    public void ADefenderStaysAtItsFlagForASoundFarOff()
    {
        TFField f = Defender();
        f.StepHearing(TFField.Self(TFField.RedFlag), TFField.Ctf(), [Heard(f, TFField.RedFlag + new AiVector(2000f, 0f, 0f))]);
        Assert.Equal(TFDecider.Defend, Branch(f));
    }

    /// <summary>By the weights: a fight in sight is worth more than any sound.</summary>
    [Fact]
    public void WithAnEnemyInSightTheFightComesBeforeTheSound()
    {
        TFField f = Bot();
        var seen = new ActorFacts(Enemy, Mid + new AiVector(-600f, 0f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)TFClass.Soldier);
        f.StepHearing(TFField.Self(Mid), null, [Heard(f, Noise)], seen);
        Assert.Equal(TFDecider.Fight, Branch(f));
    }

    [Fact]
    public void TheSoundGoesStaleByTheBotsSearchSeconds()
    {
        TFField f = Bot(searchSeconds: 2f);
        f.StepHearing(TFField.Self(Mid), null, [Heard(f, Noise)]);
        Assert.Equal(TFDecider.Search, Branch(f));
        for (int i = 0; i < (int)(2.5f / (DecideSchedule.Period * 0.015f)); i++)
            f.StepHearing(TFField.Self(Mid), null, []);
        Assert.Equal(TFDecider.Roam, Branch(f));
    }

    [Fact]
    public void ReachedTheSoundIsSettled()
    {
        TFField f = Bot();
        f.StepHearing(TFField.Self(Mid), null, [Heard(f, Noise)]);
        Assert.Equal(1, f.Memory.Board.InvestigationCount);
        f.StepHearing(TFField.Self(Noise + new AiVector(40f, 0f, 0f)), null, []);
        Assert.Equal(0, f.Memory.Board.InvestigationCount);
        for (int i = 0; i < 30; i++)
            f.StepHearing(TFField.Self(Noise + new AiVector(40f, 0f, 0f)), null, []);
        Assert.Equal(TFDecider.Roam, Branch(f));
    }

    /// <summary>An enemy's footsteps are a sound to look into; a teammate's own are recognised for what they are.</summary>
    [Fact]
    public void AnEnemysFootstepsAreLookedIntoAndATeammatesAreNot()
    {
        TFField Knowing(ActorId who, int team)
        {
            TFField f = Bot();
            // Seen once, so the bot knows whose side it is on; then out of sight.
            f.StepHearing(TFField.Self(Mid), null, [], new ActorFacts(who, Mid + new AiVector(0f, 3000f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, team, (int)TFClass.Soldier));
            for (int i = 0; i < 40; i++)
                f.StepHearing(TFField.Self(Mid), null, [AiEvent.SightLost(f.Bot, new Tick(f.Tick + DecideSchedule.Period), who, Mid + new AiVector(0f, 3000f, 0f), AiVector.Zero)]);
            f.StepHearing(TFField.Self(Mid), null, [Heard(f, Noise, SoundKind.Footstep, who)]);
            return f;
        }

        Assert.Equal(0, Knowing(Mate, TFField.Red).Memory.Board.InvestigationCount);
        TFField foe = Knowing(Enemy, TFField.Blue);
        Assert.Equal(1, foe.Memory.Board.InvestigationCount);
    }

    [Fact]
    public void ASoundFromInsideTheEnemysRespawnRoomIsLeft()
    {
        TFField f = Bot();
        // The field's blue respawn room: x 3968 to 4096.
        f.StepHearing(TFField.Self(new AiVector(3400f, 256f, 0f)), null, [Heard(f, new AiVector(4030f, 256f, 0f))]);
        Assert.Equal(1, f.Memory.Board.InvestigationCount);
        Assert.Equal(TFDecider.Roam, Branch(f));
    }
}
