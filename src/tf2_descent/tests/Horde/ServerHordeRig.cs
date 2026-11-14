using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;
using SourceSharp.Tests;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Horde.Motion;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>The rules as the horde's seam answers them: the facts' one instance.</summary>
internal sealed class RigRules : IHordeRules
{
    public TF2DescentRules Rules => TheRules;
}

/// <summary>Makes the horde's entities through the real registry over a recording engine, and counts each made.</summary>
internal sealed class RigSpawner(ScriptedEntityRegistry registry) : IHordeSpawner
{
    private int _nextEdict = 100;
    private int _nextLogical = 5000;

    public int Controllers { get; private set; }
    public int Bodies { get; private set; }
    public int Weapons { get; private set; }

    /// <summary>Refuse the body made after this many (a fact's engine at its limit); -1 never.</summary>
    public int RefuseBodyAfter { get; set; } = -1;

    public int EdictCount { get; set; } = 25;

    public HordeController? Controller()
    {
        Controllers++;
        return Make<HordeController>(new EntityHandle((uint)((1 << 16) | _nextLogical++)), HordeController.EntityClass);
    }

    public RobotBody? Body()
    {
        if (RefuseBodyAfter >= 0 && Bodies >= RefuseBodyAfter)
            return null;
        Bodies++;
        EdictCount++;
        return Make<RobotBody>(Next(), TF2.Descent.Shared.RobotNetState.ClassName);
    }

    public RobotWeapon? Weapon(RobotBody body)
    {
        Weapons++;
        EdictCount++;
        var w = Make<RobotWeapon>(Next(), TF2.Descent.Shared.RobotWeaponNetState.ClassName);
        w?.Attach(body);
        return w;
    }

    public int Edicts() => EdictCount;

    private EntityHandle Next() => new((uint)((1 << 16) | _nextEdict++));

    private T? Make<T>(EntityHandle h, string cls) where T : ScriptedEntity
    {
        registry.Spawn(h, cls);
        return registry.Get(h) as T;
    }
}

/// <summary>A hero's knockback resistance and downed state for the door shove, by seat, as a fact sets them.</summary>
internal sealed class RigShoveTraits : TF2.Descent.Server.Horde.Doors.IHeroShoveTraits
{
    public Dictionary<int, (double KnockbackResist, bool Exempt)> Seats { get; } = [];

    public (double KnockbackResist, bool Exempt) Of(int seat) => Seats.GetValueOrDefault(seat);
}

/// <summary>The party a fact rolls the roster for.</summary>
internal sealed class FixedParty(int size) : TF2.Descent.Server.Horde.Director.IHordeParty
{
    public int Size { get; set; } = size;

    public int PartySize() => Size;
}

/// <summary>The skill whose hit is in flight, as a fact sets it (none by default).</summary>
internal sealed class RigSkillHits : TF2.Descent.Server.Skills.ISkillHits
{
    public string? Skill { get; set; }

    public string? Current() => Skill;
}

/// <summary>Counts every hit handed on.</summary>
internal sealed class RigHits : IRobotHitSink
{
    public List<RobotHit> Hits { get; } = [];

    public void OnRobotHit(in RobotHit hit) => Hits.Add(hit);
}

/// <summary>The hit chain as the controller is handed it: the members a fact names, in order.</summary>
internal sealed class RigHitHooks(IReadOnlyList<IRobotHitSink> members) : IHooks<IRobotHitSink>
{
    public IReadOnlyList<IRobotHitSink> All => members;

    public void Each(string what, Action<IRobotHitSink> call)
    {
        foreach (var m in members)
            call(m);
    }
}

/// <summary>A world where nothing blocks a line and every box is in every PVS, unless a fact says otherwise.</summary>
internal sealed class RigSightWorld : ISightWorld
{
    public Func<Vector, Vector, bool> Clear { get; set; } = (_, _) => true;
    public bool Pvs { get; set; } = true;
    public int Lines { get; private set; }

    public bool InPvs(EntityHandle hero, Vector mins, Vector maxs) => Pvs;

    public bool LineClear(Vector from, Vector to)
    {
        Lines++;
        return Clear(from, to);
    }
}

/// <summary>The heroes a fact puts down: a seat, a room and a place each.</summary>
internal sealed class RigHeroes : IHordeHeroes
{
    public List<(HeroPlace Place, HeroEye Eye)> Heroes { get; } = [];

    public void Put(HordeLevel level, AiVector at)
    {
        int room = level.RoomAt(at);
        var eye = new Vector(at.X, at.Y, at.Z + 64f);
        Heroes.Add((new HeroPlace(Heroes.Count + 1, room, at), new HeroEye(new EntityHandle((uint)(Heroes.Count + 1)), eye, room)));
    }

    public int Gather(HordeLevel level, Span<HeroPlace> places, Span<HeroEye> eyes)
    {
        int n = Math.Min(Heroes.Count, Math.Min(places.Length, eyes.Length));
        for (int i = 0; i < n; i++)
        {
            places[i] = Heroes[i].Place;
            eyes[i] = Heroes[i].Eye;
        }

        return n;
    }
}

/// <summary>
/// The server's horde over the real entities, registry and rules, with a
/// recording engine under them and fakes for the spawner, the sight world and
/// the heroes: the controller begun on <see cref="Level"/>.
/// </summary>
internal sealed class ServerHordeRig
{
    public const double Tps = 1 / 0.015;

    /// <param name="brains">The controller's AI session (HO4); none given, one that does nothing (<see cref="IdleBrains"/>).</param>
    public ServerHordeRig(HordeLevel? level = null, bool begin = true, Func<ServerHordeRig, TF2.Descent.Server.Horde.Session.IHordeBrainHost>? brains = null)
    {
        Level = level ?? Row(4, markers: 3);
        Engine = RecordingEngine.Default();
        Engine.ReturnsEachCall("Entities.GetIndex", () => (int)(Engine.Last("Entities.GetIndex") ?? default).ArgAs<EntityHandle>(0).EntryIndex);
        Engine.Returns("Entities.SetParent", true);
        Engine.Returns("EntityRender.LookupSequence", 7);

        Registry = new ScriptedEntityRegistry(TestEngines.Server());
        Registry.RegisterAll(typeof(RobotBody).Assembly);

        var services = new ServiceContainer();
        foreach (var (contract, _) in EngineServices.Areas(new RecordingLog()))
            services.AddInstance(contract, Engine);
        services.AddInstance<IUserMessageSender>(Engine);
        services.AddInstance<ILog>(Log);
        services.AddInstance(new ConsoleLines());
        Spawner = new RigSpawner(Registry);
        services.AddInstance<IHordeRules>(new RigRules());
        services.AddInstance<IHordeSpawner>(Spawner);
        services.AddInstance(Parking);
        services.AddInstance<IRobotHitSink>(Hits);
        HitMembers.Add(Hits);
        services.AddInstance<IHooks<IRobotHitSink>>(new RigHitHooks(HitMembers));
        Motion = new RobotMotion(World);
        services.AddInstance<IHordeActors>(Actors);
        services.AddInstance(Motion);
        services.AddInstance<ISightWorld>(SightWorld);
        services.AddInstance<IHordeHeroes>(Heroes);
        services.AddInstance(brains?.Invoke(this) ?? new IdleBrains());
        services.AddInstance<TF2.Descent.Server.Skills.ISkillHits>(SkillHits);
        Shove = new TF2.Descent.Server.Horde.Doors.DoorShove(Motion, Heroes, Engine, Traits, new RigRules());
        services.AddInstance(Shove);
        Registry.Services = services;

        if (begin)
        {
            Controller = Spawner.Controller() ?? throw new InvalidOperationException("no controller");
            if (!Controller.Begin(Level, Tps, 1, Now))
                throw new InvalidOperationException("the controller did not begin");
        }
    }

    public HordeLevel Level { get; }
    public RecordingEngine Engine { get; }
    public ScriptedEntityRegistry Registry { get; }
    public RigSpawner Spawner { get; }
    public RecordingLog Log { get; } = new();
    public HordeParking Parking { get; } = new();
    public RigHits Hits { get; } = new();

    /// <summary>The skill hit in flight the bodies read (<see cref="RigSkillHits.Skill"/>).</summary>
    public RigSkillHits SkillHits { get; } = new();

    /// <summary>The hit chain's members, in order: <see cref="Hits"/> first; a fact may add more.</summary>
    public List<IRobotHitSink> HitMembers { get; } = [];
    public RigSightWorld SightWorld { get; } = new();
    public RigHeroes Heroes { get; } = new();

    /// <summary>The world the bodies' motion sweeps: a floor a unit under the level's (a body put down at z 0 settles onto it), and the boxes a fact puts down.</summary>
    public BoxMotionWorld World { get; } = new() { FloorZ = -1f };

    public RigActors Actors { get; } = new();

    public RobotMotion Motion { get; }

    /// <summary>The door shove the controller runs after the motion (HO11).</summary>
    public TF2.Descent.Server.Horde.Doors.DoorShove Shove { get; }

    /// <summary>What the shove reads of each hero: no resistance and none downed, unless a fact says.</summary>
    public RigShoveTraits Traits { get; } = new();

    public HordeController Controller { get; } = null!;
    public long Now { get; set; } = 1000;

    public RobotRoster Roster => Controller.Roster!;
    public RobotBodies Bodies => Controller.Bodies!;

    /// <summary>Runs <paramref name="ticks"/> server ticks of the controller.</summary>
    public void Tick(int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
            Controller.Tick(++Now);
    }

    /// <summary>A robot of <paramref name="template"/> at scale <paramref name="scale"/> with <paramref name="affixes"/> in room 0, lent a body by hand.</summary>
    public (RobotId Id, RobotBody Body) Lent(string template, double scale = 1.0, params string[] affixes)
    {
        var at = Centre(0);
        var id = Controller.SpawnByHand(template, scale, at, 90f, 42, Now, out string why, affixes);
        if (!id.IsValid || Roster.Body(id.Index) == 0)
            throw new InvalidOperationException(why);
        return (id, Bodies.Body(Roster.Body(id.Index)));
    }

    /// <summary>The last value the engine was told for one call on one entity, by its argument at <paramref name="arg"/>.</summary>
    public T LastFor<T>(string member, EntityHandle entity, int arg) =>
        Engine.AllOf(member).Where(c => c.ArgAs<EntityHandle>(0) == entity).Select(c => c.ArgAs<T>(arg)).LastOrDefault()!;
}
