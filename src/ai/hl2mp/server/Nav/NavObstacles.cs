using SourceSharp.Abi;

namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>What the binding sees of an obstacle's entity this tick.</summary>
public readonly record struct ObstacleSighting(bool Found, bool Alive, float Moved, float Turned);

/// <summary>
/// When a dynamic obstacle is in the way (plan_hl2mp_ai §6.2), from its
/// entity's state as any player could see it: a door blocks while it is
/// where and as it spawned (shut); a breakable or a toggled brush while it
/// stands; a physics or dynamic prop while it is still at its place; a mover
/// never (it travels, and a path through it waits). An obstacle whose entity
/// is not found (broken, removed, or never named) blocks nothing.
/// </summary>
public static class ObstacleRule
{
    /// <summary>How far a prop may be nudged and still hold its place, in units.</summary>
    public const float Nudge = 16f;

    /// <summary>How far a door may move or turn and still count as shut.</summary>
    public const float Shut = 1f;

    public static bool Blocking(NavObstacleKind kind, ObstacleSighting s)
    {
        if (!s.Found || !s.Alive)
            return false;
        return kind switch
        {
            NavObstacleKind.Door => s.Moved < Shut && s.Turned < Shut,
            NavObstacleKind.Breakable or NavObstacleKind.Toggle => true,
            NavObstacleKind.Physics or NavObstacleKind.Prop => s.Moved < Nudge,
            _ => false,
        };
    }
}

/// <summary>
/// Keeps a graph's <see cref="NavObstacleState"/> current from the entities
/// (plan_hl2mp_ai §6.2): each obstacle is matched to its entity once, at
/// load (by name, or, unnamed, the entity of its class nearest the place
/// the file gives it), its spawn place remembered, and its state re-read
/// through <see cref="IObstacleEyes"/>.
/// </summary>
public sealed class NavObstacles(DerivedNav nav, IObstacleEyes eyes)
{
    private readonly NavObstacleState _state = new(nav.Graph.ObstacleCount);
    private readonly EntityHandle[] _entity = new EntityHandle[nav.Obstacles.Length];
    private readonly AiVector[] _spawnOrigin = new AiVector[nav.Obstacles.Length];
    private readonly AiVector[] _spawnAngles = new AiVector[nav.Obstacles.Length];

    public NavObstacleState State => _state;

    /// <summary>How many obstacles were matched to an entity.</summary>
    public int Matched { get; private set; }

    /// <summary>How many obstacles block now.</summary>
    public int Blocking { get; private set; }

    public void Resolve()
    {
        Matched = 0;
        for (int o = 0; o < nav.Obstacles.Length; o++)
        {
            NavObstacleInfo info = nav.Obstacles[o];
            _entity[o] = eyes.Find(info.Name, info.ClassName, (info.Mins + info.Maxs) * 0.5f);
            if (_entity[o] == EntityHandle.None)
                continue;
            Matched++;
            _spawnOrigin[o] = eyes.Origin(_entity[o]);
            _spawnAngles[o] = eyes.Angles(_entity[o]);
        }

        Update();
    }

    public void Update()
    {
        int blocking = 0;
        for (int o = 0; o < nav.Obstacles.Length; o++)
        {
            EntityHandle e = _entity[o];
            ObstacleSighting s = e == EntityHandle.None ? default
                : !eyes.Alive(e) ? new ObstacleSighting(true, false, 0f, 0f)
                : new ObstacleSighting(true, true, eyes.Origin(e).DistanceTo(_spawnOrigin[o]), eyes.Angles(e).DistanceTo(_spawnAngles[o]));
            bool b = ObstacleRule.Blocking(nav.Obstacles[o].Kind, s);
            _state.Set(o, b);
            if (b)
                blocking++;
        }

        Blocking = blocking;
    }
}

/// <summary>The binding's reads of obstacle entities: found by name or class and place, alive, where and how turned.</summary>
public interface IObstacleEyes
{
    /// <summary>The entity called <paramref name="name"/>, or of <paramref name="className"/> nearest <paramref name="near"/> within 64 units; None for none.</summary>
    EntityHandle Find(string name, string className, AiVector near);

    bool Alive(EntityHandle entity);

    AiVector Origin(EntityHandle entity);

    AiVector Angles(EntityHandle entity);
}

/// <summary><see cref="IObstacleEyes"/> over the engine's entity reads.</summary>
public sealed class EngineObstacleEyes(IEngineEntities entities) : IObstacleEyes
{
    public const float MatchRadius = 64f;

    public EntityHandle Find(string name, string className, AiVector near)
    {
        if (!name.Contains('@', StringComparison.Ordinal))
        {
            EntityHandle named = entities.FindByName(name);
            if (named != EntityHandle.None)
                return named;
        }

        EntityHandle best = EntityHandle.None;
        float bestD = MatchRadius;
        foreach (EntityHandle h in entities.FindAllByClassname(className))
        {
            Vector c = entities.GetWorldSpaceCenter(h);
            float d = new AiVector(c.X, c.Y, c.Z).DistanceTo(near);
            if (d <= bestD)
            {
                bestD = d;
                best = h;
            }
        }

        return best;
    }

    public bool Alive(EntityHandle entity) => entities.IsValid(entity);

    public AiVector Origin(EntityHandle entity)
    {
        Vector v = entities.GetOrigin(entity);
        return new AiVector(v.X, v.Y, v.Z);
    }

    public AiVector Angles(EntityHandle entity)
    {
        Vector v = entities.GetAngles(entity);
        return new AiVector(v.X, v.Y, v.Z);
    }
}
