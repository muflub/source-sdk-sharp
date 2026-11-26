using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Shared;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Sniper's round 2 leftovers (round2.md §5 "Sniper", the round 1 code review's C7, C9 and C10): the attacker of a hit,
/// not the bot's target, says whether the hit came from range; a far target is scoped only standing still; the SMG in hand
/// is filled in a lull before the rifle comes back.
/// </summary>
public sealed class TFSniperRound2Tests
{
    private static readonly ActorId Soldier = new(5, 0x4000_0001), Unseen = new(9, 0x4000_0001);
    private static readonly AiVector Start = new(1500f, 256f, 0f);

    private static TFField Field() => new(seed: TFSniperPlayTests.SeedOf(TFSniperClass.Marksman), weapons: TFSniperPlayTests.Weapons);

    private static long Counter(TFField f, string name) => f.Decider.ClassCounters().Single(kv => kv.Key == name).Value;

    private static bool IsZoomPress(TFField f, in IntentCommand i)
        => i.Action.Kind == ActionKind.Ability && f.Memory.World.Map.Abilities.TryGet(AbilityCall.Row(i.Action.WeaponSlot), out AbilityRow row) && row.Name == TFSniperClass.Zoom;

    /// <summary>Walks the bot to its spot with nobody in sight, standing it where it walks; true once it holds the line.</summary>
    private static AiVector ToTheSpot(TFField f)
    {
        AiVector at = Start;
        for (int n = 0; n < 12; n++)
        {
            f.Hud = TFSniperPlayTests.Kit(zoomed: false);
            f.StepHearing(TFSniperPlayTests.Me(at), TFField.Ctf(), []);
            if (f.Decider.Doing.Verdict == "holding a sight line")
                return at;
            if (f.Decider.Doing.HasGoal)
                at = f.Decider.Doing.Goal;
        }

        throw new InvalidOperationException("never held a spot");
    }

    /// <summary>
    /// Held at its spot and scoped on a Soldier it sees, the Sniper is hit by someone it does not see (a far Sniper): it leaves the
    /// line, though its own target is in sight (the code review's C7: the rule tested the target, not the attacker).
    /// </summary>
    [Fact]
    public void HitByAnUnseenShooterWhileItsTargetIsInSightItLeaves()
    {
        TFField f = Field();
        AiVector at = ToTheSpot(f);
        var soldier = TFSniperPlayTests.Drawn(Soldier, at + new AiVector(1400f, 0f, 0f), TFClass.Soldier);
        for (int n = 0; n < 4; n++)
        {
            f.Hud = TFSniperPlayTests.Kit(zoomed: true, charge: 0f);
            f.StepHearing(TFSniperPlayTests.Me(at), TFField.Ctf(), [], soldier);
        }

        long before = Counter(f, "sniper.relocations");
        f.Hud = TFSniperPlayTests.Kit(zoomed: true, charge: 0f);
        f.StepHearing(TFSniperPlayTests.Me(at), TFField.Ctf(),
                      [AiEvent.Damaged(f.Bot, new Tick(f.Tick + DecideSchedule.Period), Unseen, 50f, new AiVector(-1f, 0f, 0f), 0)], soldier);
        Assert.Equal(before + 1, Counter(f, "sniper.relocations"));
    }

    /// <summary>Held at its spot and hit by the Soldier it sees and is shooting at, it holds the line: a hit from in front is no reason to move.</summary>
    [Fact]
    public void HitByTheSoldierItSeesItHoldsTheLine()
    {
        TFField f = Field();
        AiVector at = ToTheSpot(f);
        var soldier = TFSniperPlayTests.Drawn(Soldier, at + new AiVector(1400f, 0f, 0f), TFClass.Soldier);
        for (int n = 0; n < 4; n++)
        {
            f.Hud = TFSniperPlayTests.Kit(zoomed: true, charge: 0f);
            f.StepHearing(TFSniperPlayTests.Me(at), TFField.Ctf(), [], soldier);
        }

        long before = Counter(f, "sniper.relocations");
        f.Hud = TFSniperPlayTests.Kit(zoomed: true, charge: 0f);
        f.StepHearing(TFSniperPlayTests.Me(at), TFField.Ctf(),
                      [AiEvent.Damaged(f.Bot, new Tick(f.Tick + DecideSchedule.Period), Soldier, 50f, new AiVector(1f, 0f, 0f), 0)], soldier);
        Assert.Equal(before, Counter(f, "sniper.relocations"));
    }

    /// <summary>
    /// A far enemy in sight while the bot walks (hurt, to a pack): no scope press on any decide that moves; with full health
    /// the same enemy is fought standing still, and scoped (the code review's C9: the scope came up on the walk, a crawl at 80 u/s).
    /// </summary>
    [Fact]
    public void AFarTargetIsScopedOnlyStandingStill()
    {
        (int walking, int walkingPresses, _) = Run(health: 30);
        (_, _, int holdingPresses) = Run(health: 125);
        Assert.True(walking > 0 && holdingPresses > 0, $"walking {walking} holding presses {holdingPresses}");
        Assert.Equal(0, walkingPresses);
    }

    private static (int Walking, int WalkingPresses, int HoldingPresses) Run(int health)
    {
        TFField f = Field();
        int walkingWithTarget = 0, walkingPresses = 0, holdingPresses = 0;
        AiVector at = Start;
        for (int n = 0; n < 40; n++)
        {
            var enemy = TFSniperPlayTests.Drawn(Soldier, at + new AiVector(1600f, 0f, 0f), TFClass.Soldier);
            f.Hud = TFSniperPlayTests.Kit(zoomed: false);
            SelfState me = TFField.Self(at, health: health, clip: -1, reserve: 25, weapon: TFSniperPlayTests.Rifle, playerClass: TFClass.Sniper);
            IntentCommand i = f.StepHearing(me, TFField.Ctf(), [], enemy);
            bool press = IsZoomPress(f, i);
            if (i.Move.Kind == MoveKind.Hold)
                holdingPresses += press ? 1 : 0;
            else
                (walkingWithTarget, walkingPresses) = (walkingWithTarget + 1, walkingPresses + (press ? 1 : 0));
        }

        return (walkingWithTarget, walkingPresses, holdingPresses);
    }

    /// <summary>In a lull with the SMG in hand and its clip part spent, the Sniper reloads it before taking the rifle back (the code review's C10).</summary>
    [Fact]
    public void InALullThePartSpentSmgIsReloadedFirst()
    {
        TFField f = Field();
        SelfState me = TFField.Self(Start, health: 125, clip: 10, reserve: 50, weapon: TFSniperPlayTests.Smg, playerClass: TFClass.Sniper);
        var seen = new List<ActionKind>();
        for (int n = 0; n < 6; n++)
        {
            f.Hud = TFSniperPlayTests.Kit(zoomed: false);
            seen.Add(f.StepHearing(me, TFField.Ctf(), []).Action.Kind);
        }

        Assert.Contains(ActionKind.Reload, seen);
        Assert.DoesNotContain(ActionKind.SelectWeapon, seen);
    }

    /// <summary>The control: with the SMG full, the lull takes the rifle back.</summary>
    [Fact]
    public void InALullAFullSmgGivesWayToTheRifle()
    {
        TFField f = Field();
        SelfState me = TFField.Self(Start, health: 125, clip: 25, reserve: 50, weapon: TFSniperPlayTests.Smg, playerClass: TFClass.Sniper);
        var seen = new List<ActionKind>();
        for (int n = 0; n < 6; n++)
        {
            f.Hud = TFSniperPlayTests.Kit(zoomed: false);
            seen.Add(f.StepHearing(me, TFField.Ctf(), []).Action.Kind);
        }

        Assert.DoesNotContain(ActionKind.Reload, seen);
        Assert.Contains(ActionKind.SelectWeapon, seen);
    }
    /// <summary>Scoped and charged on a Soldier 1,400 u off: with the view far off its point, no shot; with the view kept on the decide's own point, a shot.</summary>
    [Fact]
    public void AScopedShotWaitsForTheCrosshairOnItsPoint()
    {
        int Shots(bool onAim)
        {
            TFField f = Field();
            AiVector at = ToTheSpot(f);
            var soldier = TFSniperPlayTests.Drawn(Soldier, at + new AiVector(1400f, 0f, 0f), TFClass.Soldier);
            AiVector eye = at + new AiVector(0f, 0f, SourceSharp.Ai.TF2.Knowledge.TFKnowledge.AimHeight(AimPoint.Head, TFClass.Sniper).Standing);
            AiAngles view = BotCommandApplier.AnglesTo(eye, at + new AiVector(0f, 3000f, 0f));
            int shots = 0;
            for (int n = 0; n < 12; n++)
            {
                f.Hud = TFSniperPlayTests.Kit(zoomed: true, charge: 150f);
                IntentCommand i = f.StepHearing(TFSniperPlayTests.Me(at) with { Eye = view }, TFField.Ctf(), [], soldier);
                shots += i.Action.Kind == ActionKind.Fire ? 1 : 0;
                if (onAim && i.Look.Kind == LookKind.Actor)
                    view = BotCommandApplier.AnglesTo(eye, i.Look.Point);
            }

            return shots;
        }

        Assert.Equal((0, true), (Shots(onAim: false), Shots(onAim: true) > 0));
    }
    /// <summary>
    /// Scoped on a Soldier's head, he crouches: the decide he is drawn ducked the aim is still at his standing head (the eye has
    /// not yet acted on it), and within a second it is at his ducked head (the code review's H4: the head followed the next decide).
    /// </summary>
    [Fact]
    public void ACrouchPeekIsReAimedAfterAReaction()
    {
        TFField f = new(seed: TFSniperPlayTests.SeedOf(TFSniperClass.Marksman), weapons: TFSniperPlayTests.Weapons, tune: p => p with { AimErrorDeg = 0f });
        AiVector at = ToTheSpot(f);
        AiVector there = at + new AiVector(1400f, 0f, 0f);
        ActorFacts Soldier(bool ducked) => new(TFSniperRound2Tests.Soldier, there, AiVector.Zero, TFSniperPlayTests.Rifle,
                                               ducked ? ActorActivity.Crouched : ActorActivity.None, 1f, TFField.Blue, (int)TFClass.Soldier);
        float Z(bool ducked)
        {
            f.Hud = TFSniperPlayTests.Kit(zoomed: true, charge: 0f);
            return f.StepHearing(TFSniperPlayTests.Me(at), TFField.Ctf(), [], Soldier(ducked)).Look.Point.Z;
        }

        for (int n = 0; n < 6; n++)
            Z(false);
        float standing = Z(false), first = Z(true), later = 0f;
        for (int n = 0; n < 12; n++)
            later = Z(true);
        Assert.True(MathF.Abs(first - standing) < 2f && standing - later > 15f, $"standing {standing} first ducked {first} a second later {later}");
    }
    /// <summary>
    /// A rifle shot (the reserve falls) whose damage shows the next decide is a hit; one whose damage shows a second later (another
    /// gun's, a burn) is not.
    /// </summary>
    [Fact]
    public void ARifleHitIsDamageShortlyAfterTheShot()
    {
        long Hits(int lateDecides)
        {
            TFField f = Field();
            AiVector at = ToTheSpot(f);
            var soldier = TFSniperPlayTests.Drawn(Soldier, at + new AiVector(1400f, 0f, 0f), TFClass.Soldier);
            int reserve = 25;
            for (int n = 0; n < 24; n++)
            {
                if (n == 4)
                    reserve = 24;
                AiEvent[] events = n == 4 + lateDecides ? [AiEvent.DealtDamage(f.Bot, new Tick(f.Tick + DecideSchedule.Period), Soldier, 50f, 150)] : [];
                f.Hud = TFSniperPlayTests.Kit(zoomed: true, charge: 150f);
                f.StepHearing(TFSniperPlayTests.Me(at) with { Reserve = reserve }, TFField.Ctf(), events, soldier);
            }

            return Counter(f, "sniper.rifle_hits");
        }

        Assert.Equal((1L, 0L), (Hits(lateDecides: 1), Hits(lateDecides: 12)));
    }
    /// <summary>
    /// Where a jar thrown along <paramref name="look"/> from <paramref name="eye"/> comes down to <paramref name="floorZ"/>: the
    /// game's launch (forward at 1,000 u/s and 200 u/s along the view's up, TFProjectileEffects' Lob) under the pull it flies by:
    /// the constant the game's TFJar takes its GravityScale from (TFProjectileDefs.PipebombGravity), whatever its value.
    /// </summary>
    private static AiVector Lands(AiVector eye, AiVector look, float floorZ)
    {
        AiVector forward = (look - eye).Normalised();
        var worldUp = new AiVector(0f, 0f, 1f);
        float along = forward.X * worldUp.X + forward.Y * worldUp.Y + forward.Z * worldUp.Z;
        AiVector up = (worldUp - forward * along).Normalised();
        AiVector v = forward * 1000f + up * 200f, p = eye;
        float g = global::TF2.Server.Projectiles.TFProjectileDefs.PipebombGravity * SourceSharp.Ai.TF2.Knowledge.TFKnowledge.Gravity;
        for (int i = 0; i < 2000 && (v.Z > 0f || p.Z > floorZ); i++)
        {
            p += v * 0.005f;
            v = new AiVector(v.X, v.Y, v.Z - g * 0.005f);
        }

        return p;
    }

    /// <summary>
    /// A Jarate Sniper throws at two Soldiers together 400 u and 800 u off on the flat: the jar, flying as the game flies it, comes down
    /// inside its 200 u splash of the pair (the r2s4 review: aimed under the world's whole pull, it landed 196 u and 994 u long).
    /// </summary>
    [Fact]
    public void AJarComesDownInsideItsSplash()
    {
        float Miss(float distance)
        {
            TFField f = new(seed: TFSniperPlayTests.SeedOf(TFSniperClass.Jarate), weapons: TFSniperPlayTests.Weapons);
            AiVector at = new(1024f, 256f, 0f);
            ActorFacts a = TFSniperPlayTests.Drawn(Soldier, at + new AiVector(distance, -40f, 0f), TFClass.Soldier);
            ActorFacts b = TFSniperPlayTests.Drawn(new ActorId(6, 0x4000_0001), at + new AiVector(distance, 40f, 0f), TFClass.Soldier);
            for (int n = 0; n < 12; n++)
            {
                f.Hud = TFSniperPlayTests.Kit(jar: true);
                int weapon = n == 0 ? TFSniperPlayTests.Rifle : TFSniperPlayTests.JarKind;
                IntentCommand i = f.StepDrawn(TFSniperPlayTests.Me(at, weapon), TFField.Ctf(), a, b);
                if (f.Decider.Doing.Verdict == "jarate" && i.Look.Kind == LookKind.Point)
                {
                    AiVector eye = at + new AiVector(0f, 0f, SourceSharp.Ai.TF2.Knowledge.TFKnowledge.AimHeight(AimPoint.Head, TFClass.Sniper).Standing);
                    AiVector down = Lands(eye, i.Look.Point, 0f);
                    return new AiVector(down.X - (at.X + distance), down.Y - at.Y, 0f).Length;
                }
            }

            throw new InvalidOperationException("never threw");
        }

        float near = Miss(400f), far = Miss(800f);
        Assert.True(near < 200f && far < 200f, $"lands {near:F0} u off at 400 u, {far:F0} u off at 800 u");
    }

    /// <summary>The kill counts the round flow's reset reaches depend on nothing: a dependency there closed a cycle through the weapons, and the server did not compose.</summary>
    [Fact]
    public void TheKillCountsDependOnNothing()
        => Assert.All(typeof(TFSniperKillCounts).GetConstructors(), c => Assert.Empty(c.GetParameters()));
    /// <summary>The kill counts, a level participant, hold a lifecycle stage of their own: at the unspecified 0 they collided with another participant and the server refused to load.</summary>
    [Fact]
    public void TheKillCountsHoldAStageOfTheirOwn()
        => Assert.NotEqual((int)SourceSharp.Abi.LifecycleStage.Unspecified, ((SourceSharp.Abi.IGameLifecycle)new TFSniperKillCounts()).LifecycleOrder);
}
