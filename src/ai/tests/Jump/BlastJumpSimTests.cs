using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using TF2.Server.Projectiles;
using TF2.Shared.Movement;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The blast-jump simulator on worlds small enough to work out by hand
/// (plan_bots_teams_tf2_part_g G.5 step 3): when the launcher fires, where its rocket ends, and
/// what the blast does to the shooter, each by the mod's own numbers. The differential against
/// flights recorded in the game is BlastJumpDifferentialTests.
/// </summary>
public class BlastJumpSimTests
{
    /// <summary>The bot's rocket jump as its ability row sends it: look down, jump and duck, fire, stay ducked.</summary>
    private static JumpCommand[] RocketJump(float pitch = 70f, int after = 260)
        => [.. Hold(1, InJump | InDuck, pitch), .. Hold(2, InJump | InDuck | InAttack, pitch), .. Hold(after, InDuck, pitch)];

    private static JumpFlight Fly(VoxelColumns columns, JumpStart start, params JumpCommand[][] parts)
        => JumpRig.Sim(columns).Fly(start, parts.SelectMany(p => p).ToArray());

    private static float Length(Vector v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    [Fact]
    public void ARocketJumpThrowsHimFarOverAJump()
    {
        JumpFlight flight = Fly(Room(), Standing(), RocketJump());
        Assert.Equal(1, flight.Rockets);
        Assert.Equal(1, flight.Blasts);
        Assert.True(flight.Landed);
        Assert.Equal(0, flight.Takeoff);
        // The control: the same commands without the trigger are a crouch jump, 72 units.
        JumpFlight hop = Fly(Room(), Standing(), Hold(3, InJump | InDuck, 70f), Hold(260, InDuck, 70f));
        Assert.Equal(0, hop.Rockets);
        Assert.InRange(hop.Apex, 70f, 73f);
        // The blast comes on the way up, so it lifts him over the hop by more than its push alone would from rest: the
        // push's speed upward squared over twice the gravity. No number of the blast's is here but the push it reported.
        float push = Assert.Single(flight.Impacts).Impulse.Z, lift = push * push / (2f * TFViewVectors.Params.Gravity);
        Assert.True(push > 0f && flight.Apex > hop.Apex + lift, $"the rocket jump rose {flight.Apex}, the hop {hop.Apex}, pushed upward at {push}");
    }

    [Fact]
    public void ThePointBlankCrouchJumpBlastPushesAStocksCrouchedSoldier()
    {
        // Ducked in the air, he is pushed as if 55 tall, under the ceiling: TF's hull, not HL2's 32 x 32 x 72, which hit it.
        JumpFlight flight = Fly(Room(), Standing(), RocketJump());
        JumpImpact blast = Assert.Single(flight.Impacts);
        Assert.True(flight.Track[blast.Command].Ducking);
        AssertBlast(ByTheMod(flight, blast, TFRadiusDamage.DamageScaleSelfSoldier, TFRadiusDamage.ForceScaleSelfSoldierJump), blast);
        Assert.True(blast.Impulse.Z > 0.9f * Length(blast.Impulse), $"mostly up: {blast.Impulse}");
    }

    [Fact]
    public void TheRocketEndsOnTheFloorItWasFiredAt()
    {
        JumpImpact blast = Assert.Single(Fly(Room(), Standing(), RocketJump()).Impacts);
        Assert.Equal(VoxelMoveWorld.DistEpsilon, blast.At.Z, 2);
    }

    [Fact]
    public void TheRocketLeavesTheMuzzleRightOfTheEyeSoTheBlastPushesHimLeft()
    {
        // Facing east, right is south (-y): the rocket lands 12 units south of him and he is thrown north.
        JumpFlight flight = Fly(Room(), Standing(), RocketJump(pitch: 89f));
        JumpImpact blast = Assert.Single(flight.Impacts);
        Assert.Equal(256f - TFProjectileDefs.RocketMuzzleRight, blast.At.Y, 0);
        Assert.True(blast.Impulse.Y > 50f, $"north: {blast.Impulse}");
        Assert.True(flight.To.Y > flight.From.Y + 100f, $"from {flight.From} to {flight.To}");
    }

    [Fact]
    public void ARocketFliesItsSpeedATickAndArrivesWhenThatCoversTheWay()
    {
        // Level at a wall 512 east. The muzzle is 23.5 ahead of the eye; the rocket covers 16.5 a tick.
        VoxelColumns walled = Room((x, _) => x >= 48 ? [] : null);
        JumpFlight flight = Fly(walled, Standing(x: 256f), Hold(1, InAttack), Hold(60));
        JumpImpact impact = Assert.Single(flight.Impacts);
        float way = 768f - (256f + TFProjectileDefs.RocketMuzzleForward), perTick = TFProjectileDefs.RocketSpeed * TickSeconds;
        Assert.Equal((int)MathF.Ceiling(way / perTick) - 1, impact.Command);
        Assert.Equal(768f - VoxelMoveWorld.DistEpsilon, impact.At.X, 1);
        // Fired standing: the eye's 68 and the muzzle's 3 under it.
        Assert.Equal(VoxelMoveWorld.DistEpsilon + 68f + TFProjectileDefs.RocketMuzzleUp, impact.At.Z, 2);
    }

    [Fact]
    public void ABlastOutOfReachNeitherHurtsNorPushes()
    {
        VoxelColumns walled = Room((x, _) => x >= 48 ? [] : null);
        JumpFlight flight = Fly(walled, Standing(x: 256f), Hold(1, InAttack), Hold(60));
        JumpImpact impact = Assert.Single(flight.Impacts);
        Assert.Equal((0f, Vector.Zero), (impact.Damage, impact.Impulse));
        Assert.Equal(0, flight.Blasts);
        Assert.Equal(-1, flight.Takeoff);
        Assert.Equal(256f, flight.Track[^1].Origin.X, 3);
    }

    [Fact]
    public void ABlastBehindALowWallSparesHim()
    {
        // A wall 30 high and a column thick stands 28 east of him. He fires over it at the floor behind:
        // the rocket clears it, and the blast's line to his centre does not. Without the wall the same shot
        // reaches him: the control.
        static VoxelColumns Yard(bool wall) => Room((x, _) => wall && x == 13 ? [new FreeSpan(30f, 1024f)] : null);
        JumpCommand[] shot = [.. Hold(1, InAttack, pitch: 35f), .. Hold(40, 0, 35f)];
        JumpStart start = Standing(x: 180f);
        JumpImpact open = Assert.Single(Fly(Yard(wall: false), start, shot).Impacts);
        Assert.True(open.Damage > 0f && open.At.X > 224f, $"the control: the open shot deals {open.Damage} at {open.At}");
        JumpImpact hidden = Assert.Single(Fly(Yard(wall: true), start, shot).Impacts);
        Assert.Equal(open.At, hidden.At);
        Assert.Equal((0f, Vector.Zero), (hidden.Damage, hidden.Impulse));
    }

    /// <summary>
    /// A blast's damage and push worked again from the mod's functions: the shooter where the track has him after the
    /// impact's command, the server's player box (32 across, 72 tall: its centre 36 up), the rocket's own-pass radius,
    /// and the blast's source a unit over where the rocket stopped, as the mod's blast lifts it.
    /// </summary>
    private static (float Damage, Vector Push) ByTheMod(JumpFlight flight, JumpImpact blast, float damageScale, float forceScale)
    {
        // TF's hulls: 48 x 48 x 82 standing, 62 ducked, and a ducked soldier pushed as if 55 tall (tf_player.cpp ApplyPushFromDamage).
        Vector origin = flight.Track[blast.Command].Origin, source = blast.At + new Vector(0f, 0f, 1f);
        bool ducked = flight.Track[blast.Command].Ducking;
        Vector centre = origin + new Vector(0f, 0f, ducked ? 31f : 41f);
        float distance = TFRadiusDamage.VictimDistance(source, false, true, centre, origin, centre);
        float damage = TFRadiusDamage.AtDistance(RocketDamage, TFProjectileDefs.RocketRadiusForRocketJumps, 0.5f, distance) * damageScale;
        var size = new Vector(48f, 48f, ducked ? 55f : 82f);
        return (damage, TFRadiusDamage.Push(TFRadiusDamage.PushDirection(source, centre), size, damage, self: true, forceScale, heavy: false));
    }

    private static void AssertBlast((float Damage, Vector Push) expected, JumpImpact blast)
    {
        Assert.True(Length(expected.Push) < TFRadiusDamage.MaxForce - 50f, $"the control: under the ceiling, so the damage shows: {Length(expected.Push)}");
        Assert.True(expected.Damage > 10f, $"the control: a blast that reaches: {expected.Damage}");
        Assert.Equal(expected.Damage, blast.Damage, 3);
        Assert.Equal(expected.Push.X, blast.Impulse.X, 2);
        Assert.Equal(expected.Push.Y, blast.Impulse.Y, 2);
        Assert.Equal(expected.Push.Z, blast.Impulse.Z, 2);
    }

    [Fact]
    public void AGroundedBlastIsTheWholeDamageAtTheGroundedScale()
    {
        // Fired at the floor 80 ahead, standing: far enough that the push stays under the ceiling.
        JumpFlight flight = Fly(Room(), Standing(), Hold(1, InAttack, 40f), Hold(20, 0, 40f));
        JumpImpact blast = Assert.Single(flight.Impacts);
        Assert.True(flight.Track[blast.Command - 1].OnGround);
        AssertBlast(ByTheMod(flight, blast, damageScale: 1f, TFRadiusDamage.ForceScaleSelfSoldierGrounded), blast);
    }

    [Fact]
    public void AnAirborneBlastIsTheSoldiersShareOfTheDamageAtTheJumpScale()
    {
        // The same shot a command after a jump: off the ground it costs him 0.6 of the damage and throws at twice the scale.
        JumpFlight flight = Fly(Room(), Standing(), Hold(1, InJump, 40f), Hold(1, InAttack, 40f), Hold(20, 0, 40f));
        JumpImpact blast = Assert.Single(flight.Impacts);
        Assert.False(flight.Track[blast.Command].OnGround);
        AssertBlast(ByTheMod(flight, blast, TFRadiusDamage.DamageScaleSelfSoldier, TFRadiusDamage.ForceScaleSelfSoldierJump), blast);
    }

    [Fact]
    public void TheTriggerHeldFiresAgainOnlyWhenTheRefireTimeHasPassed()
    {
        // Level at the far wall, held 1.5 s: fired at 0 and at 0.8 s, and not a third time.
        JumpFlight flight = Fly(Room(), Standing(), Hold(100, InAttack), Hold(60));
        Assert.Equal(2, flight.Rockets);
        Assert.Equal(2, flight.Impacts.Length);
        Assert.Equal((int)MathF.Ceiling(RocketRefire / TickSeconds), flight.Impacts[1].Command - flight.Impacts[0].Command);
    }

    [Fact]
    public void ACrouchTakenInTheFiringCommandIsTheMuzzlesAlready()
    {
        // Crouched for a second, then fires level: the muzzle is the crouched one, 8 over the crouched eye's 45.
        VoxelColumns walled = Room((x, _) => x >= 48 ? [] : null);
        JumpImpact settled = Assert.Single(Fly(walled, Standing(x: 256f), Hold(70, InDuck), Hold(1, InDuck | InAttack), Hold(60, InDuck)).Impacts);
        Assert.Equal(VoxelMoveWorld.DistEpsilon + TFViewVectors.Params.DuckView.Z + TFProjectileDefs.RocketMuzzleUpDucked, settled.At.Z, 2);

        // In the air a duck is taken at once: the command that takes it and fires has the crouched eye and the crouched muzzle
        // (the server publishes the move's duck before the weapon's frame reads it).
        JumpImpact taken = Assert.Single(Fly(walled, Standing(x: 256f), Hold(1, InJump), Hold(1, InDuck | InAttack), Hold(60, InDuck)).Impacts);
        JumpPoint fired = Fly(walled, Standing(x: 256f), Hold(1, InJump), Hold(1, InDuck | InAttack)).Track[1];
        Assert.True(fired.Ducking);
        Assert.Equal(fired.Origin.Z + TFViewVectors.Params.DuckView.Z + TFProjectileDefs.RocketMuzzleUpDucked, taken.At.Z, 2);

        // The control: let go in the firing command, a duck held until then is no longer the muzzle's (his standing eye's 68).
        JumpFlight let = Fly(walled, Standing(x: 256f), Hold(1, InJump), Hold(1, InDuck), Hold(1, InAttack), Hold(60));
        Assert.True(let.Track[1].Ducking && !let.Track[2].Ducking);
        Assert.Equal(let.Track[2].Origin.Z + 68f + TFProjectileDefs.RocketMuzzleUp, Assert.Single(let.Impacts).At.Z, 2);
    }

    [Fact]
    public void HisOwnBlastForgivesTheAirDucksHeHasSpent()
    {
        // Falling from 100 up he fires at the floor under him, and ducks twice while the rocket flies: the mod allows
        // two ducks an airtime, so a third is refused (the control). The blast lands, and after it a duck is taken again.
        JumpStart falling = new(new Vector(200f, 256f, 100f), Vector.Zero, new Vector(89f, 0f, 0f), Ducked: false, OnGround: false);
        JumpFlight flight = Fly(Room(), falling, Hold(1, InAttack, 89f), Hold(1, InDuck, 89f), Hold(1, 0, 89f), Hold(1, InDuck, 89f), Hold(1, 0, 89f),
                                Hold(1, InDuck, 89f), Hold(6, 0, 89f), Hold(1, InDuck, 89f));
        JumpImpact blast = Assert.Single(flight.Impacts);
        Assert.True(blast.Damage > 0f, $"the control: the blast reaches him: {blast}");
        Assert.True(flight.Track[1].Ducking && !flight.Track[2].Ducking && flight.Track[3].Ducking && !flight.Track[4].Ducking,
                    "the control: two ducks taken and let go in the air");
        Assert.False(flight.Track[5].Ducking, "the control: the third duck of the airtime is refused");
        Assert.InRange(blast.Command, 6, 11);
        Assert.False(flight.Track[11].OnGround);
        Assert.True(flight.Track[12].Ducking, "after his own blast he ducks again");
    }

    [Fact]
    public void TheSameStartAndCommandsGiveTheSameFlightBitForBit()
    {
        JumpCommand[] commands = [.. Hold(30, InForward, 70f, 20f, ForwardSpeed), .. RocketJump()];
        JumpFlight first = Fly(Room(), Standing(), commands), again = Fly(Room(), Standing(), commands);
        Assert.Equal(first.Track, again.Track);
        Assert.Equal(first.Impacts, again.Impacts);
        Assert.Equal((first.Takeoff, first.Landing, first.Apex, first.AirSeconds), (again.Takeoff, again.Landing, again.Apex, again.AirSeconds));
        Assert.True(first.Blasts == 1 && first.Landed, "the control: the flight that is compared is a flight");
    }

    /// <summary>A fall from 400 up with a shot at the floor and three ducks: two taken, the airtime's third refused.</summary>
    private static (JumpStart Start, JumpCommand[] Commands) DuckingFall()
        => (new JumpStart(new Vector(200f, 256f, 400f), Vector.Zero, new Vector(89f, 0f, 0f), Ducked: false, OnGround: false),
            [.. Hold(1, InAttack, 89f), .. Hold(1, InDuck, 89f), .. Hold(1, 0, 89f), .. Hold(1, InDuck, 89f), .. Hold(1, 0, 89f), .. Hold(3, InDuck, 89f), .. Hold(4, 0, 89f)]);

    [Fact]
    public void OneSimulatorFliesTwiceWithNothingLeftOverFromTheFirst()
    {
        BlastJumpSim sim = JumpRig.Sim(Room());
        (JumpStart start, JumpCommand[] commands) = DuckingFall();
        // The first run ends in the air with both of the airtime's ducks spent and the launcher just fired.
        JumpFlight first = sim.Fly(start, commands);
        Assert.True(first.Track[1].Ducking && first.Track[3].Ducking && !first.Track[5].Ducking && !first.Landed, "the control: two ducks taken, the third refused");
        JumpFlight second = sim.Fly(start, commands);
        Assert.Equal(first.Track, second.Track);
        Assert.Equal(1, second.Rockets);
    }

    [Fact]
    public void SimulatorsOnOtherThreadsOverOneWorldEachFlyTheirOwnFlight()
    {
        // Two flights, each flown alone first, then by four simulators at once, turn about: a body, a carrier or a
        // launcher shared between two of them would show as a duck taken or refused that the flight alone did not have.
        VoxelColumns room = Room();
        BlastJumpSims sims = JumpRig.Sims();
        JumpLauncher launcher = JumpLauncher.Rocket(RocketDamage, RocketRefire);
        (JumpStart Start, JumpCommand[] Commands)[] flights = [DuckingFall(), (Standing(), RocketJump(after: 40))];
        JumpPoint[][] alone = [.. flights.Select(f => sims.Create(room, launcher).Fly(f.Start, f.Commands).Track)];
        BlastJumpSim[] made = [.. Enumerable.Range(0, 4).Select(_ => sims.Create(room, launcher))];
        int[] wrong = new int[made.Length];
        Thread[] threads = [.. made.Select((sim, i) => new Thread(() =>
        {
            for (int n = 0; n < 400; n++)
            {
                int which = (i + n) % flights.Length;
                if (!sim.Fly(flights[which].Start, flights[which].Commands).Track.AsSpan().SequenceEqual(alone[which]))
                    wrong[i]++;
            }
        }))];
        foreach (Thread t in threads)
            t.Start();
        foreach (Thread t in threads)
            t.Join();
        Assert.Equal(new int[made.Length], wrong);
    }

    [Fact]
    public void SteppedACommandAtATimeItIsTheFlightFlownWhole()
    {
        // What a pilot in the loop uses: the run begun, then a command at a time, read as it goes.
        JumpCommand[] commands = [.. Hold(30, InForward, 70f, 20f, ForwardSpeed), .. RocketJump()];
        BlastJumpSim sim = JumpRig.Sim(Room());
        JumpFlight whole = sim.Fly(Standing(), commands);
        Assert.True(whole.Blasts == 1 && whole.Landed, "the control: the flight that is compared is a flight");
        sim.Begin(Standing());
        Assert.Equal((0, -1, -1, Standing().Origin, 0f), (sim.Commands, sim.Takeoff, sim.Landing, sim.To, sim.AirSeconds));
        var track = new JumpPoint[commands.Length];
        for (int i = 0; i < commands.Length; i++)
        {
            track[i] = sim.Step(commands[i]);
            Assert.Equal(commands[i].View, sim.ViewAngles);
        }

        Assert.Equal(whole.Track, track);
        Assert.Equal((whole.Takeoff, whole.Landing, whole.From, whole.To, whole.Apex, whole.AirSeconds, whole.Rockets),
                     (sim.Takeoff, sim.Landing, sim.From, sim.To, sim.Apex, sim.AirSeconds, sim.Rockets));
        Assert.Equal(whole.BlastDamage, sim.BlastDamage, 3);
        Assert.Equal(commands.Length, sim.Commands);
    }

    [Fact]
    public void AFlightThatStartsInTheAirHasTakenOffAtItsFirstCommand()
    {
        JumpStart falling = new(new Vector(200f, 256f, 300f), new Vector(100f, 0f, -50f), Vector.Zero, Ducked: false, OnGround: false);
        JumpFlight flight = Fly(Room(), falling, Hold(120));
        Assert.Equal(0, flight.Takeoff);
        Assert.True(flight.Landed);
        Assert.Equal(falling.Origin, flight.From);
        Assert.InRange(flight.To.Z, 0f, 2f);
        Assert.True(flight.To.X > 250f);
    }

    [Fact]
    public void ARunWithNoCommandIsWhereItStarted()
    {
        JumpFlight flight = Fly(Room(), Standing());
        Assert.Empty(flight.Track);
        Assert.Equal((Standing().Origin, Standing().Origin, -1, -1), (flight.From, flight.To, flight.Takeoff, flight.Landing));
    }

    [Fact]
    public void TheLaunchersNumbersAreTheModsRocketAndItsScripts()
    {
        JumpLauncher l = JumpLauncher.Rocket(RocketDamage, RocketRefire);
        Assert.Equal((TFProjectileDefs.RocketSpeed, TFProjectileDefs.RocketRadius, TFProjectileDefs.RocketRadiusForRocketJumps), (l.Speed, l.Radius, l.SelfRadius));
        Assert.Equal((90f, 0.8f), (l.Damage, l.RefireSeconds));
        Assert.True((l.Bits & DamageType.Blast) != 0);
        // A script that names a speed flies at it.
        Assert.Equal(1800f, JumpLauncher.Rocket(RocketDamage, RocketRefire, 1800f).Speed);
    }
}
