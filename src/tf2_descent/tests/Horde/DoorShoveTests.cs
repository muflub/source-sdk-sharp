using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Motion;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The door shove on the server (plan_descent_logic L9, 3.9, HO11): a robot walking through
/// door 0 of a row (x = 512, normal +x, 64 wide) meets a hero standing in it. "A hero in a
/// doorway is pushed out along the door's normal; a hero beside it is not": nor one on open
/// floor, nor a downed one, nor one no robot is held up by; one shove a door a cooldown, as
/// strong as the content and the robot's scale say, less the hero's resistance.
/// </summary>
public sealed class DoorShoveTests
{
    private const string Impulse = "Entities.ApplyVelocityImpulse";

    private sealed class Kit
    {
        public Kit(AiVector hero, bool route = true, double scale = 1.0)
        {
            Rig = new ServerHordeRig(Row(3, markers: 0));
            Rig.World.Put(new Vector(hero.X - 24, hero.Y - 24, hero.Z), new Vector(hero.X + 24, hero.Y + 24, hero.Z + 82), entityIndex: 1, isPlayer: true);
            Rig.Heroes.Put(Rig.Level, hero);
            (_, Body) = Rig.Lent("soldier", scale);
            if (route)
                Rig.Motion.Route(Body.Index, [At(464, 256), At(560, 256), At(800, 256)]);
        }

        public ServerHordeRig Rig { get; }
        public TF2.Descent.Server.Horde.RobotBody Body { get; }
        public int HeroBlocks { get; private set; }

        /// <summary>Runs ticks; answers each tick a shove was given on.</summary>
        public List<long> Run(int ticks)
        {
            var shoveTicks = new List<long>();
            for (int t = 0; t < ticks; t++)
            {
                int before = Rig.Engine.AllOf(Impulse).Count();
                Rig.Tick();
                if (Rig.Motion.Blocked(Body.Index) is { Kind: BlockerKind.Hero } r && r.Last == Rig.Now) HeroBlocks++;
                if (Rig.Engine.AllOf(Impulse).Count() > before) shoveTicks.Add(Rig.Now);
            }
            return shoveTicks;
        }

        public (EntityHandle Hero, Vector Push, bool ClearGround) Last
        {
            get
            {
                var c = Rig.Engine.Last(Impulse)!.Value;
                return (c.ArgAs<EntityHandle>(0), c.ArgAs<Vector>(1), c.ArgAs<bool>(2));
            }
        }
    }

    [Fact]
    public void A_hero_in_the_doorway_is_shoved_through_it_along_the_normal_away_from_the_robot()
    {
        var kit = new Kit(At(512, 256));

        var shoves = kit.Run(200);

        Assert.NotEmpty(shoves);
        var s = TheRules.Content.Horde.Shove;
        var (hero, push, clear) = kit.Last;
        Assert.Equal(new EntityHandle(1), hero);
        Assert.Equal((float)s.Speed, push.X, 2);   // +x: the robot comes from room 0
        Assert.Equal(0, push.Y);
        Assert.Equal((float)s.Lift, push.Z, 2);
        Assert.True(clear);
        Assert.Equal(1, kit.Rig.Shove.Doors[0].LastHero);
        Assert.Equal(kit.Body.Index, kit.Rig.Shove.Doors[0].LastBody);
    }

    [Fact]
    public void A_hero_beside_the_door_is_not_shoved()
    {
        // Two units outside the opening's half-width, in the robot's way on its own side.
        var kit = new Kit(At(500, 256 + 34));

        var shoves = kit.Run(200);

        Assert.True(kit.HeroBlocks > 20, $"blocked by the hero {kit.HeroBlocks} ticks");
        Assert.Empty(shoves);
    }

    [Fact]
    public void A_hero_on_open_floor_is_not_shoved()
    {
        var kit = new Kit(At(400, 256));

        var shoves = kit.Run(200);

        Assert.True(kit.HeroBlocks > 20, $"blocked by the hero {kit.HeroBlocks} ticks");
        Assert.Empty(shoves);
    }

    [Fact]
    public void No_robot_held_up_no_shove()
    {
        var kit = new Kit(At(512, 256), route: false);

        Assert.Empty(kit.Run(200));
        Assert.Equal(0, kit.HeroBlocks);
    }

    [Fact]
    public void A_downed_hero_is_not_shoved()
    {
        var kit = new Kit(At(512, 256));
        kit.Rig.Traits.Seats[1] = (0, true);

        var shoves = kit.Run(200);

        Assert.True(kit.HeroBlocks > 20);
        Assert.Empty(shoves);
    }

    [Fact]
    public void One_shove_a_door_each_cooldown()
    {
        var kit = new Kit(At(512, 256));   // a hero who never moves: a fake world's box

        var shoves = kit.Run(400);

        long cooldown = kit.Rig.Shove.Rules!.CooldownTicks;
        Assert.True(shoves.Count >= 4, $"{shoves.Count} shoves");
        Assert.All(shoves.Zip(shoves.Skip(1)), p => Assert.Equal(cooldown, p.Second - p.First));
        Assert.Equal(shoves.Count, kit.Rig.Shove.Doors[0].Shoves);
    }

    [Fact]
    public void The_shoves_strength_is_the_contents_times_the_robots_scale_less_the_heros_resistance()
    {
        var kit = new Kit(At(512, 256), scale: 0.8);
        kit.Rig.Traits.Seats[1] = (0.25, false);

        Assert.NotEmpty(kit.Run(200));
        Assert.Equal((float)(TheRules.Content.Horde.Shove.Speed * 0.8 * 0.75), kit.Last.Push.X, 2);
    }

    /// <summary>A robot standing still at <paramref name="robot"/> (no route: a fighter at its range) and a hero at <paramref name="hero"/>.</summary>
    private static (ServerHordeRig Rig, List<long> Shoves) Standing(AiVector robot, AiVector hero, int ticks = 200)
    {
        var rig = new ServerHordeRig(Row(3, markers: 0));
        rig.World.Put(new Vector(hero.X - 24, hero.Y - 24, hero.Z), new Vector(hero.X + 24, hero.Y + 24, hero.Z + 82), entityIndex: 1, isPlayer: true);
        rig.Heroes.Put(rig.Level, hero);
        var id = rig.Controller.SpawnByHand("soldier", 1.0, robot, 0, 42, rig.Now, out string why);
        Assert.True(id.IsValid, why);
        var shoves = new List<long>();
        for (int t = 0; t < ticks; t++)
        {
            int before = rig.Engine.AllOf(Impulse).Count();
            rig.Tick();
            if (rig.Engine.AllOf(Impulse).Count() > before) shoves.Add(rig.Now);
        }
        return (rig, shoves);
    }

    [Fact]
    public void A_robot_fighting_the_hero_who_holds_the_doorway_at_arms_length_shoves_him()
    {
        var (rig, shoves) = Standing(At(430, 256), At(512, 256));

        Assert.NotEmpty(shoves);
        var push = rig.Engine.Last(Impulse)!.Value.ArgAs<Vector>(1);
        Assert.True(push.X > 0 && push.Y == 0, $"pushed {push}");
    }

    [Fact]
    public void A_robot_out_of_its_reach_of_the_doorway_hero_does_not_shove()
    {
        // 24 + 24 + 48 = 96 is a scale 1 soldier's contact; it stands 112 off.
        var (_, shoves) = Standing(At(400, 256), At(512, 256));

        Assert.Empty(shoves);
    }

    [Fact]
    public void A_robot_at_arms_length_of_a_hero_beside_the_door_does_not_shove()
    {
        var (_, shoves) = Standing(At(430, 256 + 60), At(500, 256 + 60));

        Assert.Empty(shoves);
    }

    [Fact]
    public void The_shove_command_prints_a_doors_held_robot_hero_inside_and_last_shove()
    {
        var rig = new ServerHordeRig(begin: false);
        var clock = new FixedClock();
        var service = new TF2.Descent.Server.Horde.HordeService(new ActiveMode(), new OneLevel(Row(3, markers: 0)), rig.Spawner, clock, rig.Parking,
            new TF2.Descent.Server.Horde.HordeCensus(rig.Spawner, rig.Parking, rig.Log), new TF2.Descent.Server.Horde.HordeTickTimer(), rig.Log,
            new TF2.Descent.Server.Horde.Director.MarkerClearances(rig.World, new RigRules()), new FixedParty(1));
        service.OnLevelReady("tf2_descent-3-s7");
        rig.World.Put(new Vector(488, 232, 0), new Vector(536, 280, 82), entityIndex: 1, isPlayer: true);
        rig.Heroes.Put(service.Current!.Level!, At(512, 256));
        var c = service.Current!;
        var id = c.SpawnByHand("soldier", 1.0, Centre(0), 90, 42, clock.Tick, out _);
        rig.Motion.Route(c.Roster!.Body(id.Index), [At(464, 256), At(560, 256), At(800, 256)]);
        for (int t = 0; t < 120; t++) c.Tick(++clock.Tick);
        var command = new TF2.Descent.Server.Horde.Doors.ShoveCommand(service, rig.Shove, rig.Heroes, new RigRules(), clock, rig.Log);

        command.Execute();

        string text = rig.Log.Text;
        Assert.Contains("[TF2Descent/Shove]   door 0 rooms 0-1 at (512, 256, 0) normal (1, 0) width 64: robots held up 1", text);
        Assert.Contains("heroes inside [1], last shove tick ", text);
        Assert.Contains("impulse (450, 0, 160)", text);
        Assert.Contains("[TF2Descent/Shove]   1 doors with no robot held up, no hero inside and no shove", text);
    }

    [Fact]
    public void A_drilled_robot_waits_its_dwell_before_the_first_shove()
    {
        var kit = new Kit(At(512, 256));
        var shoves = kit.Run(200);
        int discipline = kit.Rig.Roster.Temperament(kit.Body.Robot.Index).Levels[TheRules.Temperaments.Index("discipline")];
        long dwell = kit.Rig.Shove.Rules!.DwellTicks(discipline);
        var blocked = kit.Rig.Motion.Blocked(kit.Body.Index);

        Assert.NotEmpty(shoves);
        Assert.True(dwell > 0, $"discipline {discipline}: this fact wants a drilled robot");
        Assert.Equal(dwell, shoves[0] - blocked.Since + 1);   // held up its dwell (BlockedReport.For counts the first tick), then at once
    }
}
