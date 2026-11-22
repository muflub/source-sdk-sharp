using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Horde.Boss;
using TF2.Descent.Shared;
using TF2.Descent.Tests.Horde;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Bosses;

/// <summary>What the boss fight asked of the engine, kept for a fact.</summary>
internal sealed class FakeBossWorld : IBossWorld
{
    public List<(int Player, string Token, string Arg)> Notices { get; } = [];
    public List<(EntityHandle Entity, string Sound)> Sounds { get; } = [];
    public List<(string Name, Vector At)> Particles { get; } = [];
    public List<(int Player, BossBarView Bar)> Bars { get; } = [];
    public List<(EntityHandle Victim, EntityHandle Attacker, float Damage, DamageType Bits)> Hurts { get; } = [];
    public Func<Vector, Vector, bool> Line { get; set; } = (_, _) => true;

    public void Notice(int player, string token, string arg) => Notices.Add((player, token, arg));

    public void Sound(EntityHandle entity, string sound) => Sounds.Add((entity, sound));

    public void Particle(string name, Vector at) => Particles.Add((name, at));

    public EntityHandle Player(int slot) => new((uint)slot);

    public void Bar(int player, BossBar bar) => Bars.Add((player, bar.View()));

    public int Hurt(EntityHandle victim, EntityHandle attacker, float damage, DamageType bits, Vector direction)
    {
        Hurts.Add((victim, attacker, damage, bits));
        return (int)damage;
    }

    public bool Clear(Vector from, Vector to, EntityHandle self, EntityHandle target) => Line(from, to);
}

/// <summary>A map's named entities as the arena's seam sees them: how many of each name the map holds, and every input fired.</summary>
internal sealed class FakeArenaEntities : IArenaEntities
{
    public Dictionary<string, int> Present { get; } = new(StringComparer.Ordinal);
    public List<(string Name, string Input)> Inputs { get; } = [];

    /// <summary>Every contract name of a full arena (phases 2 to 4, stations 1 to 5), one entity each.</summary>
    public FakeArenaEntities Full()
    {
        Present[ArenaStage.ExitName] = 1;
        for (int n = 1; n <= 5; n++)
        {
            Present[ArenaStage.PillarName(n)] = 1;
            Present[ArenaStage.RubbleName(n)] = 1;
        }

        for (int p = 1; p <= 4; p++)
        {
            Present[ArenaStage.PlatformName(p)] = 2;
            Present[ArenaStage.MouthName(p)] = 1;
        }

        return this;
    }

    public IReadOnlyList<ArenaEntity> Fire(string name, string input)
    {
        Inputs.Add((name, input));
        return [.. Enumerable.Range(0, Present.GetValueOrDefault(name)).Select(i => new ArenaEntity(new EntityHandle((uint)(0x0002_0040 + i)), new Vector(100 + i, 200, 300)))];
    }
}

/// <summary>What the horde's paths are told of the arena: every change, in order.</summary>
internal sealed class FakeArenaObstacles : IArenaObstacles
{
    public List<(string Name, bool Enabled)> Changes { get; } = [];

    public void Changed(string name, bool enabled) => Changes.Add((name, enabled));
}

/// <summary>The hands an ability is handed, as a fact sees them: every launch kept, a cap it sets.</summary>
internal sealed class FakeHands : IRobotAbilityHands
{
    public List<(string Projectile, Vector Origin, Vector Velocity, float Damage, bool Crit)> Launches { get; } = [];
    public int Cap { get; set; } = 32;

    public EntityHandle Launch(RobotBody body, string projectile, Vector origin, Vector velocity, float damage, bool crit)
    {
        if (Launches.Count >= Cap)
            return EntityHandle.None;
        Launches.Add((projectile, origin, velocity, damage, crit));
        return new EntityHandle((uint)(0x0001_0800 + Launches.Count));
    }
}

/// <summary>
/// A boss level over the server horde rig: four rooms in a row, room 2 the arena with the boss on
/// its spot, the boss's services over a fake engine (<see cref="FakeBossWorld"/>), ticked by
/// <see cref="Tick"/> as the horde's hands tick them.
/// </summary>
internal sealed class BossRig
{
    public const int ArenaRoom = 2;

    public BossRig(string boss = "sir_nukesalot", HordeArenaLayout? layout = null)
    {
        var spot = new PackMarker(Centre(ArenaRoom), 180f);
        int depth = TheRules.Bosses.Of(boss)!.Depth;
        Level = Row(4, markers: 3, depth: depth, boss: new HordeBoss(boss, ArenaRoom, spot, layout));
        Horde = new ServerHordeRig(Level);
        Arena = new ArenaStage(ArenaEntities, ArenaObstacles, World, new RigRules(), Horde.Log);
        Adds = new BossAdds(new RigRules(), World, Horde.Log);
        Encounter = new BossEncounter(new RigRules(), Horde.Heroes, World, Adds, Horde.Motion, Arena, Horde.Log);
        Abilities = new BossAbilities(new RigRules(), World, Adds, Horde.Heroes, Encounter, Arena, Horde.Log);
        Exit = new BossExit(new RigHordeCurrent(Horde), new RigRules(), World, Horde.Log);
        Row = Enumerable.Range(0, Horde.Roster.Count).Single(Horde.Roster.IsBoss);
    }

    public HordeLevel Level { get; }
    public ServerHordeRig Horde { get; }
    public FakeBossWorld World { get; } = new();
    public FakeHands Hands { get; } = new();

    /// <summary>The named arena entities the map holds, and every input fired at them.</summary>
    public FakeArenaEntities ArenaEntities { get; } = new();

    /// <summary>What the horde's paths were told of the arena.</summary>
    public FakeArenaObstacles ArenaObstacles { get; } = new();

    public ArenaStage Arena { get; }
    public BossAdds Adds { get; }
    public BossEncounter Encounter { get; }
    public BossAbilities Abilities { get; }
    public BossExit Exit { get; }
    public int Row { get; }
    public RobotRoster Roster => Horde.Roster;
    public HordeController Controller => Horde.Controller;
    public long Now => Horde.Now;

    /// <summary>The boss lent its body where it stands (its room woken).</summary>
    public RobotBody Lend()
    {
        if (Roster.State(Row) == RobotState.Asleep)
            Roster.WakeRoom(Roster.Room(Row));
        if (!Controller.LendByHand(Row, Now, out string why))
            throw new InvalidOperationException(why);
        return Horde.Bodies.Body(Roster.Body(Row));
    }

    /// <summary>A hero at <paramref name="at"/>: his seat is the next slot.</summary>
    public void Hero(AiVector at) => Horde.Heroes.Put(Level, at);

    /// <summary>Moves hero <paramref name="seat"/> to <paramref name="at"/>.</summary>
    public void MoveHero(int seat, AiVector at)
    {
        var (_, eye) = Horde.Heroes.Heroes[seat - 1];
        int room = Level.RoomAt(at);
        Horde.Heroes.Heroes[seat - 1] = (new HeroPlace(seat, room, at), eye with { Room = room, Eye = new Vector(at.X, at.Y, at.Z + 64f) });
    }

    /// <summary><paramref name="ticks"/> server ticks of the boss's tickers, the encounter's first, the clock moved on before each.</summary>
    public void Tick(int ticks = 1)
    {
        for (int t = 0; t < ticks; t++)
        {
            Horde.Now++;
            Encounter.Tick(Controller, Now);
            Abilities.Tick(Controller, Now);
        }
    }

    /// <summary>A second of ticks.</summary>
    public static int Seconds(double s) => (int)Math.Ceiling(s * ServerHordeRig.Tps);

    public bool Ask(RobotBody body, int ability, int arg) =>
        Abilities.Act(Controller, body, body.Robot.Index, new ActionIntent(ActionKind.Ability, FireStyle.Tap, ability, arg), Hands, Now);

    /// <summary>The boss's life set to <paramref name="share"/> of its most.</summary>
    public void LifeTo(double share)
    {
        double most = Roster.Stats(Row).Life, life = Roster.Life(Row), target = most * share;
        if (target < life)
            Roster.Hurt(Row, life - target);
        else
            Roster.Heal(Row, target - life);
    }
}
