using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Huntsman's draw through the applier (the review's H2): from the press that starts the draw to the decider's own release,
/// the first trigger is down on every tick, though a second enemy steps into sight, a voice line falls due (the bot drops under
/// half its health) and a teammate crosses the line; letting go is the shot, so nothing but the release may let it go.
/// </summary>
public sealed class TFSniperBowApplierTests
{
    private const int Bow = 64;
    private static readonly ActorId Heavy = new(5, 0x4000_0001), Soldier = new(6, 0x4000_0001), Mate = new(4, 0x4000_0001);
    private static readonly EntityHandle Player = new(0x0001_0002u);
    private static readonly AiVector At = new(1024f, 256f, 0f);

    private static readonly WeaponKnowledge Weapons = new([
        .. TFSniperPlayTests.Weapons.All,
        new WeaponFacts(Bow, TFSniperKit.BowClass, 0, 2f, 0f, false, false, 2600f, ProjectileGravity: 80f, ChargeSeconds: 1f),
    ]);

    private static TFKitItem Item(TFLoadoutSlot slot, int defindex, string itemClass, int kind) => new(slot, defindex, itemClass, kind, []);

    private static TFHud Kit(float draw)
        => new([Item(TFLoadoutSlot.Primary, 56, TFSniperKit.BowClass, Bow), Item(TFLoadoutSlot.Secondary, 58, TFSniperKit.JarClass, TFSniperPlayTests.JarKind),
                Item(TFLoadoutSlot.Melee, 3, "tf_weapon_club", TFSniperPlayTests.Kukri)],
               [new TFMeter(TFSniperMeters.Zoomed, 0f), new TFMeter(TFSniperMeters.Charge, 0f), new TFMeter(TFSniperMeters.Jar, 0f), new TFMeter(TFSniperMeters.Draw, draw)], []);

    private sealed class Runner : IPlayerCommandRunner
    {
        public UserCmd Last;

        public void Queue(EntityHandle player, in UserCmd cmd) => Last = cmd;

        public int RunQueued(EntityHandle player) => 1;
    }

    private sealed class Self : IBotSelf
    {
        public BotSelfFacts Facts;

        public BotSelfFacts Read(EntityHandle player) => Facts;

        public int WeaponSelectFor(EntityHandle player, int slot) => 0;
    }

    private static ActorFacts Drawn(ActorId id, AiVector at, TFClass cls, int team)
        => new(id, at, AiVector.Zero, TFSniperPlayTests.Rifle, ActorActivity.None, 1f, team, (int)cls);

    [Fact]
    public void TheTriggerIsDownFromTheDrawToTheRelease()
    {
        TFField f = new(seed: TFSniperPlayTests.SeedOf(TFSniperClass.Huntsman), weapons: Weapons);
        float eyeUp = SourceSharp.Ai.TF2.Knowledge.TFKnowledge.AimHeight(AimPoint.Head, TFClass.Sniper).Standing;
        var runner = new Runner();
        var self = new Self { Facts = new(true, false, At, At + new AiVector(0f, 0f, eyeUp), default) };
        var applier = new BotCommandApplier(runner, self);
        applier.Add(f.Bot, Player, new MotionParams(200f, 487.5f, 16.2f, 174f), 42);
        ActorFacts heavy = Drawn(Heavy, At + new AiVector(900f, 0f, 0f), TFClass.HeavyWeapons, TFField.Blue);
        int pressedAt = -1, releasedAt = -1, tick = 0, health = 125;
        var events = new List<string>();
        bool mateCrossed = false, voiceDue = false, secondSeen = false;
        for (int n = 0; n < 80 && releasedAt < 0; n++)
        {
            float draw = pressedAt < 0 ? 0f : (tick - pressedAt) * 0.015f;
            var sighted = new List<ActorFacts> { heavy };
            if (pressedAt >= 0)
            {
                // A second enemy in sight, a little nearer; a teammate on the line from 0.3 s to 1.4 s, past the full charge; the bot hurt under half.
                sighted.Add(Drawn(Soldier, At + new AiVector(700f, 300f, 0f), TFClass.Soldier, TFField.Blue));
                secondSeen = true;
                if (draw is > 0.3f and < 1.4f)
                {
                    sighted.Add(Drawn(Mate, At + new AiVector(300f, 0f, 0f), TFClass.Soldier, TFField.Red));
                    mateCrossed = true;
                }

                if (draw > 0.5f && health == 125)
                    (health, voiceDue) = (50, true);
            }

            f.Hud = Kit(draw);
            SelfState me = TFField.Self(At, health: health, clip: 1, reserve: 12, weapon: Bow, playerClass: TFClass.Sniper) with { Eye = self.Facts.View };
            IntentCommand intent = f.StepDrawn(me, TFField.Ctf(), [.. sighted]);
            applier.Submit(intent);
            for (int k = 0; k < DecideSchedule.Period && releasedAt < 0; k++, tick++)
            {
                applier.ApplyTick(new Tick(f.Tick + k), 0.015f);
                self.Facts = self.Facts with { View = new AiAngles(runner.Last.Pitch, runner.Last.Yaw, 0f) };
                bool down = (runner.Last.Buttons & (int)InputButtons.Attack) != 0;
                if (down && pressedAt < 0)
                    pressedAt = tick;
                else if (!down && pressedAt >= 0)
                    releasedAt = tick;
            }
        }

        Assert.True(pressedAt >= 0, "never drew");
        Assert.True(releasedAt > 0, "never let go");
        float held = (releasedAt - pressedAt) * 0.015f;
        // Let go only by the play: at a full draw (0.95 of the 1 s charge), past the teammate on the line.
        Assert.True(mateCrossed && voiceDue && secondSeen, $"mate {mateCrossed} voice {voiceDue} second {secondSeen}");
        Assert.True(held >= 1.4f, $"let go {held:F2} s after the draw, before the teammate left the line");
        Assert.True(f.Decider.ClassCounters().Single(kv => kv.Key == "sniper.bow_releases").Value >= 1, "the play's own release");
    }
    /// <summary>
    /// The drawn arrow's target falls out of sight 0.6 s into the draw and the bot turns to the next (a new stimulus: the eye
    /// reacts and flicks): the trigger stays down through the reaction, and the arrow goes at the play's release, not at the turn.
    /// </summary>
    [Fact]
    public void TheDrawIsKeptThroughTheEyesTurnToTheNextTarget()
    {
        TFField f = new(seed: TFSniperPlayTests.SeedOf(TFSniperClass.Huntsman), weapons: Weapons);
        float eyeUp = SourceSharp.Ai.TF2.Knowledge.TFKnowledge.AimHeight(AimPoint.Head, TFClass.Sniper).Standing;
        var runner = new Runner();
        var self = new Self { Facts = new(true, false, At, At + new AiVector(0f, 0f, eyeUp), default) };
        var applier = new BotCommandApplier(runner, self);
        applier.Add(f.Bot, Player, new MotionParams(200f, 487.5f, 16.2f, 174f), 42);
        ActorFacts heavy = Drawn(Heavy, At + new AiVector(900f, 0f, 0f), TFClass.HeavyWeapons, TFField.Blue);
        ActorFacts soldier = Drawn(Soldier, At + new AiVector(600f, 600f, 0f), TFClass.Soldier, TFField.Blue);
        int pressedAt = -1, releasedAt = -1, tick = 0;
        for (int n = 0; n < 80 && releasedAt < 0; n++)
        {
            float draw = pressedAt < 0 ? 0f : (tick - pressedAt) * 0.015f;
            ActorFacts[] sighted = draw < 0.6f ? [heavy] : [soldier];
            f.Hud = Kit(draw);
            SelfState me = TFField.Self(At, health: 125, clip: 1, reserve: 12, weapon: Bow, playerClass: TFClass.Sniper) with { Eye = self.Facts.View };
            applier.Submit(f.StepDrawn(me, TFField.Ctf(), sighted));
            for (int k = 0; k < DecideSchedule.Period && releasedAt < 0; k++, tick++)
            {
                applier.ApplyTick(new Tick(f.Tick + k), 0.015f);
                self.Facts = self.Facts with { View = new AiAngles(runner.Last.Pitch, runner.Last.Yaw, 0f) };
                bool down = (runner.Last.Buttons & (int)InputButtons.Attack) != 0;
                if (down && pressedAt < 0)
                    pressedAt = tick;
                else if (!down && pressedAt >= 0)
                    releasedAt = tick;
            }
        }

        Assert.True(pressedAt >= 0 && releasedAt > 0, $"pressed {pressedAt} released {releasedAt}");
        // The turn to the Soldier is about 45 degrees: the reaction and the flick take well past the full charge.
        Assert.True((releasedAt - pressedAt) * 0.015f > 0.95f, $"let go {(releasedAt - pressedAt) * 0.015f:F2} s after the draw");
        Assert.True(f.Decider.ClassCounters().Single(kv => kv.Key == "sniper.bow_releases").Value >= 1, "the play's own release");
    }
}
