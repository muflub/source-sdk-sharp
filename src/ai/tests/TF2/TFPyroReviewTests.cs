using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;
using static SourceSharp.Ai.Tests.TF2.TFPyroTests;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Pyro's play after the code review of 2026-11-21: the chase by path, the Über backed away from when it cannot be
/// shoved, the reflect left alone for a teammate's or its own projectile, a reflect counted only when the game sent the
/// projectile back, the view turned only toward what it will blast, the reflect chance's stock ladder, and the back flame
/// counted only when it flames.
/// </summary>
public sealed class TFPyroReviewTests
{
    private static AiVector Eye => Mid + new AiVector(0f, 0f, 68f);

    private static ThingFacts Shot(TFThingType type, AiVector at, AiVector velocity, int team = TFField.Blue, int owner = 0, int id = 900)
        => new(id, ThingKind.Projectile, (int)type, at, velocity, team, owner);

    private static long Counter(TFField f, string name)
        => f.Decider.ClassCounters().Where(c => c.Key == name).Sum(c => c.Value);

    /// <summary>
    /// A Heavy in sight 400 u off and 200 u up (a balcony) is chased by path, not strafed at into the wall under it (review
    /// C1); one 300 u off on the bot's level is closed on straight. Both are the fight's footwork, not another branch's walk.
    /// </summary>
    [Fact]
    public void ATargetOnAnotherLevelIsChasedByPath()
    {
        (MoveKind, string) At(AiVector offset)
        {
            TFField f = Field(Never);
            IntentCommand i = f.StepDrawn(Pyro(Mid), TFField.Ctf(), Drawn(Enemy, Mid + offset, TFField.Blue));
            return (i.Move.Kind, f.Decider.Doing.Verdict);
        }

        Assert.Equal((MoveKind.Follow, "chasing by path"), At(new AiVector(400f, 0f, 200f)));
        Assert.Equal((MoveKind.Strafe, "closing"), At(new AiVector(300f, 0f, 0f)));
    }

    /// <summary>
    /// An Übered Heavy 200 u in front: a Pyro that cannot shove him (its tank under the blast's cost, or a second its skill
    /// draw says no) backs away as every class does; one that can does not (review C2).
    /// </summary>
    [Fact]
    public void AnUberIsBackedAwayFromWhenItCannotBeShoved()
    {
        int BackOffs(Func<DecisionParams, DecisionParams> tune, int reserve)
        {
            TFField f = Field(tune);
            for (int n = 0; n < 5; n++)
                f.StepDrawn(Pyro(Mid, reserve: reserve), TFField.Ctf(), Drawn(Enemy, Mid + new AiVector(200f, 0f, 0f), TFField.Blue, conditions: ActorConditions.Invulnerable));
            return f.Decider.InvulnerableBackOffs;
        }

        Assert.True(BackOffs(Sure, reserve: 10) > 0, "dry tank");
        Assert.True(BackOffs(Never, reserve: 200) > 0, "no skill draw");
        Assert.Equal(0, BackOffs(Sure, reserve: 200));
    }

    /// <summary>A teammate's rocket and the bot's own, in the sphere and heading at it, are not blasted (the reflect is for an enemy's).</summary>
    [Fact]
    public void ATeammatesOrItsOwnProjectileIsLeftAlone()
    {
        string With(ThingFacts shot)
        {
            TFField f = Field(Sure);
            IntentCommand i = f.StepSeeing(Pyro(Mid), TFField.Ctf(), [shot]);
            return Ability(f, in i);
        }

        AiVector at = Eye + new AiVector(100f, 0f, 0f), toward = new(-1100f, 0f, 0f);
        Assert.Equal(TFPyroClass.Airblast, With(Shot(TFThingType.Rocket, at, toward)));
        Assert.Equal("", With(Shot(TFThingType.Rocket, at, toward, team: TFField.Red)));
        Assert.Equal("", With(Shot(TFThingType.Rocket, at, toward, team: 0, owner: 2)));
    }

    /// <summary>An enemy arrow 100 u in front is reflected (the game sends arrows back since the Pyro lane's airblast arm).</summary>
    [Fact]
    public void AnEnemyArrowIsReflected()
    {
        TFField f = Field(Sure);
        IntentCommand i = f.StepSeeing(Pyro(Mid), TFField.Ctf(), [Shot(TFThingType.Arrow, Eye + new AiVector(100f, 0f, 0f), new AiVector(-2400f, 0f, 0f))]);
        Assert.Equal(TFPyroClass.Airblast, Ability(f, in i));
    }

    /// <summary>
    /// A reflect is counted when the game sent the projectile back: the rocket the blast was pressed at shows next as the
    /// bot's own. A press whose rocket stays the enemy's is a press, not a reflect (review C9).
    /// </summary>
    [Fact]
    public void AReflectCountsWhenTheProjectileComesBackTheBots()
    {
        (long Reflects, long Presses) Run(bool sentBack)
        {
            TFField f = Field(Sure);
            f.StepSeeing(Pyro(Mid), TFField.Ctf(), [Shot(TFThingType.Rocket, Eye + new AiVector(100f, 0f, 0f), new AiVector(-1100f, 0f, 0f))]);
            ThingFacts after = sentBack
                ? Shot(TFThingType.Rocket, Eye + new AiVector(200f, 0f, 0f), new AiVector(1100f, 0f, 0f), TFField.Red, f.Bot.Slot)
                : Shot(TFThingType.Rocket, Eye + new AiVector(20f, 0f, 0f), new AiVector(-1100f, 0f, 0f));
            f.StepSeeing(Pyro(Mid), TFField.Ctf(), [after]);
            return (Counter(f, "pyro.reflects"), Counter(f, "pyro.reflect_presses"));
        }

        Assert.Equal((1L, 1L), Run(sentBack: true));
        Assert.Equal((0L, 1L), Run(sentBack: false));
    }

    /// <summary>An enemy rocket closing 400 u off turns the view to it when the Pyro can blast it; with its tank under the cost the aim stays on its target (review C7).</summary>
    [Fact]
    public void TheViewTurnsOnlyToWhatItCanBlast()
    {
        LookKind With(int reserve)
        {
            TFField f = Field(Sure);
            IntentCommand i = f.StepSeeing(Pyro(Mid, reserve: reserve), TFField.Ctf(), [Shot(TFThingType.Rocket, Eye + new AiVector(400f, 0f, 0f), new AiVector(-1100f, 0f, 0f))],
                                           null, Drawn(Enemy, Mid + new AiVector(500f, 0f, 0f), TFField.Blue, TFClass.Soldier));
            return i.Look.Kind;
        }

        Assert.Equal((LookKind.Point, LookKind.Actor), (With(200), With(10)));
    }

    /// <summary>The Backburner's back flames are flames: a Heavy facing away 400 u off (out of the flame's reach) counts none; at 200 u it counts them (review C8).</summary>
    [Fact]
    public void ABackFlameIsCountedOnlyWhenItFlames()
    {
        long At(float distance)
        {
            TFField f = new(seed: SeedOf(TFPyroClass.Backburner), weapons: TFPyroTests.Weapons, tune: Never)
            {
                Hud = Kit(Item(TFLoadoutSlot.Primary, 40, TFPyroClass.FlamethrowerClass, Flamer)),
            };
            var heavy = new ActorFacts(Enemy, Mid + new AiVector(distance, 0f, 0f), default, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue,
                                       (int)TFClass.HeavyWeapons, Facing: new AiAngles(0f, 0f, 0f));
            for (int n = 0; n < 5; n++)
                f.StepDrawn(Pyro(Mid), TFField.Ctf(), heavy);
            return Counter(f, "pyro.back_flames");
        }

        Assert.Equal(0L, At(400f));
        Assert.True(At(200f) > 0);
    }
}
