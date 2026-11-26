using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Movement;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The sticky kind of the blast-jump simulator (round 2's S3), on the rig's room, against hand calculations: the sticky
/// leaves the pipes' muzzle at a tap's 900 u/s along the aim and 200 up the shooter's up, falls at the world's 800 u/s² (the
/// port's sticky is a physics prop, whose solver does not read the entity's 0.4) by the projectiles' step (the speed, then
/// the move: <c>MoveMath.AdvanceFlight</c>), sticks where it meets a floor or a wall, and goes off on the second trigger only
/// once the game's arm think has run (55 commands on); its blast takes the Demoman's own share (0.75) and
/// the plain self push (<c>DAMAGE_FORCE_SCALE_SELF</c> 9), at <c>TF_GRENADE_JUMP_RADIUS</c> 146. The script's numbers are
/// the shipped tf_weapon_pipebomblauncher's: Damage 120, DamageRadius 146, TimeFireDelay 0.6.
/// </summary>
public class BlastJumpStickyTests
{
    private const float StickyDamage = 120f, StickyRadius = 146f, StickyRefire = 0.6f;
    private const int InAttack2 = GameMovement.InAttack2;

    private static readonly JumpLauncher Launcher = JumpLauncher.Sticky(StickyDamage, StickyRadius, StickyRefire);

    private static JumpFlight Fly(JumpStart start, params JumpCommand[][] parts)
        => Sims().Create(Room(), Launcher).Fly(start, parts.SelectMany(p => p).ToArray());

    [Fact]
    public void TheStickyLauncherIsStocks()
    {
        Assert.Equal((JumpProjectile.Sticky, 900f, 200f, 1f, 0.8f, 146f), (Launcher.Kind, Launcher.Speed, Launcher.LaunchUp, Launcher.GravityScale, Launcher.ArmSeconds, Launcher.SelfRadius));
        Assert.Equal((JumpProjectile.Rocket, 0f, 0f), (JumpLauncher.Rocket(RocketDamage, RocketRefire).Kind, JumpLauncher.Rocket(RocketDamage, RocketRefire).GravityScale, JumpLauncher.Rocket(RocketDamage, RocketRefire).LaunchUp));
    }

    /// <summary>
    /// Fired 30° down from standing, east: the sticky's height after n ticks is z0 + n·dt·vz − 320·dt²·n(n+1)/2 and its way
    /// east n·dt·vx, so it meets the floor in the tick that height first goes under it, where the step crosses the floor.
    /// Detonated once armed, its blast is where it stuck.
    /// </summary>
    [Fact]
    public void AStickyFliesItsArcAndSticksWhereItMeetsTheFloor()
    {
        const float Pitch = 30f, Dt = TickSeconds;
        JumpStart start = Standing(x: 200f);
        Vector forward = Angles.Forward(new Vector(Pitch, 0f, 0f));
        (Vector right, Vector up) = Angles.RightUp(forward);
        // The eye 68 over the feet, standing (the rocket's fact), and the pipes' muzzle (16 on, 8 right, 6 down).
        Vector origin = start.Origin + new Vector(0f, 0f, 68f) + (forward * 16f) + (right * 8f) + (up * -6f);
        Vector v = (forward * 900f) + (up * 200f);
        float floor = 0f, g = 800f;
        float Z(int n) => origin.Z + (n * Dt * v.Z) - (g * Dt * Dt * n * (n + 1) / 2f);
        int land = 1;
        while (Z(land) > floor)
            land++;
        float share = (Z(land - 1) - floor) / (Z(land - 1) - Z(land));
        float x = origin.X + ((land - 1 + share) * Dt * v.X);

        JumpFlight flight = Fly(start, Hold(1, InAttack, Pitch), Hold(70, 0, Pitch), Hold(1, InAttack2, Pitch), Hold(30, 0, Pitch));
        Assert.Equal(1, flight.Rockets);
        JumpImpact blast = Assert.Single(flight.Impacts);
        Assert.Equal(71, blast.Command);
        Assert.Equal(x, blast.At.X, 0.25);
        Assert.Equal(origin.Y, blast.At.Y, 1);
        Assert.InRange(blast.At.Z, floor, floor + 0.5f);
        Assert.True(land > 3 && land < 40, $"lands after {land} ticks");
    }

    /// <summary>
    /// The second trigger held from the next command on: nothing goes off until the game's arm think has run. It is booked
    /// 0.8 s after the spawn, so it runs on the 54th tick of 0.015 (53 is 0.795 s, under stock's compare), and may run after
    /// that tick's command: the first command that finds the sticky armed is the 55th.
    /// </summary>
    [Fact]
    public void AStickyGoesOffOnlyOnceArmed()
    {
        JumpFlight flight = Fly(Standing(), Hold(1, InAttack, 89f), Hold(100, InAttack2, 89f));
        JumpImpact blast = Assert.Single(flight.Impacts);
        int armed = (int)MathF.Ceiling((0.8f / TickSeconds) - 1e-3f) + 1;
        Assert.Equal(55, armed);
        Assert.Equal(armed, blast.Command);
        // The control: never detonated, it never goes off, and it stays out.
        JumpFlight held = Fly(Standing(), Hold(1, InAttack, 89f), Hold(100, 0, 89f));
        Assert.Empty(held.Impacts);
    }

    /// <summary>
    /// The sticky jump the Demoman's program flies (look down, tap, wait, jump and crouch, detonate): the blast as the mod's
    /// functions work it by hand (the Demoman's 0.75, half falloff over the jump radius, the plain self push of a 55-tall
    /// crouched body), and it throws him far over the crouch jump the same commands make without the sticky.
    /// </summary>
    [Fact]
    public void AStickyJumpPushesHimAsTheModWorksIt()
    {
        JumpCommand[] jump = [.. Hold(1, InAttack, 89f), .. Hold(54, 0, 89f), .. Hold(1, InJump | InDuck, 89f), .. Hold(1, InJump | InDuck | InAttack2, 89f), .. Hold(200, InDuck, 89f)];
        JumpFlight flight = Fly(Standing(), jump);
        JumpImpact blast = Assert.Single(flight.Impacts);
        Assert.Equal(56, blast.Command);

        Vector origin = flight.Track[blast.Command].Origin, source = blast.At + new Vector(0f, 0f, 1f);
        bool ducked = flight.Track[blast.Command].Ducking;
        Vector centre = origin + new Vector(0f, 0f, ducked ? 31f : 41f);
        float distance = MathF.Min((source - centre).Length, (source - origin).Length);
        float damage = TFRadiusDamage.AtDistance(StickyDamage, StickyRadius, 0.5f, distance) * 0.75f;
        Vector size = new(48f, 48f, ducked ? 55f : 82f);
        Vector push = TFRadiusDamage.Push(TFRadiusDamage.PushDirection(source, centre), size, damage, self: true, 9f, heavy: false);
        Assert.True(damage > 30f, $"the control: a blast at his feet: {damage}");
        Assert.Equal(damage, blast.Damage, 3);
        Assert.Equal(push.X, blast.Impulse.X, 2);
        Assert.Equal(push.Y, blast.Impulse.Y, 2);
        Assert.Equal(push.Z, blast.Impulse.Z, 2);

        JumpFlight hop = Fly(Standing(), [.. Hold(56, 0, 89f), .. Hold(2, InJump | InDuck, 89f), .. Hold(200, InDuck, 89f)]);
        Assert.InRange(hop.Apex, 70f, 73f);
        Assert.True(flight.Apex > hop.Apex + 150f, $"the sticky jump rose {flight.Apex}, the hop {hop.Apex}");
    }

    /// <summary>
    /// A worked call for the Demoman's program (round 2's S3: the row that flies it is the Demoman lane's): a launcher from
    /// the stock numbers, the commands of a jump, and what the flight says to plan with.
    /// </summary>
    [Fact]
    public void AWorkedCallForTheDemomansProgram()
    {
        BlastJumpSim sim = Sims().Create(Room(), JumpLauncher.Sticky(StickyDamage, StickyRadius, StickyRefire));
        int wait = (int)MathF.Ceiling(sim.Mover.TickSeconds > 0f ? 0.8f / sim.Mover.TickSeconds : 0f);
        JumpCommand[] program = [.. Hold(1, InAttack, 89f), .. Hold(wait, 0, 89f), .. Hold(1, InJump | InDuck | InAttack2, 89f), .. Hold(200, InDuck, 89f)];
        JumpFlight flight = sim.Fly(Standing(), program);
        Assert.True(flight.Landed && flight.Blasts == 1 && flight.BlastDamage > 0f, $"{flight.Blasts} blasts, {flight.BlastDamage} damage, landed {flight.Landed}");
        Assert.Equal(0, sim.StickiesOut);
    }

    /// <summary>
    /// A sticky sticks to a wall as to a floor: fired level at a wall 168 u ahead, it meets the wall well over the floor and
    /// stays there, so its blast, detonated a second later, is at the wall where it struck (unstuck, it would have slid down
    /// the wall to the floor in that second).
    /// </summary>
    [Fact]
    public void AStickySticksToAWall()
    {
        BlastJumpSim sim = Sims().Create(Room((x, _) => x >= 48 ? [] : null), Launcher);
        JumpFlight flight = sim.Fly(Standing(x: 600f), [.. Hold(1, InAttack), .. Hold(70), .. Hold(1, InAttack2), .. Hold(10)]);
        JumpImpact blast = Assert.Single(flight.Impacts);
        Assert.Equal(768f - VoxelMoveWorld.DistEpsilon, blast.At.X, 1);
        Assert.True(blast.At.Z > 60f, $"stuck at {blast.At}");
    }
}
