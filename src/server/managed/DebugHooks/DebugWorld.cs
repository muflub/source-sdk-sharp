using SourceSharp.Abi;
using SourceSharp.Server.Interface;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="IDebugWorld"/> over the server composition's engine areas
/// (group G2): the same calls the dev API made on them, one for one.
/// </summary>
public sealed class DebugWorld(
    IEngineEntities entities, IEngineDamage damage, IEnginePlayers players,
    IEngineWorldQuery world, IGameClock clock, IEngineHost host,
    Func<IEnumerable<(EntityHandle A, EntityHandle B)>>? touchingPairs = null,
    Func<EntityHandle, bool?>? asleep = null, Func<EntityHandle, int?>? groundIndex = null,
    Func<int>? tick = null, Func<IEnginePlayerMovement>? movement = null) : IDebugWorld
{
    private readonly IEngineEntities _entities = entities ?? throw new ArgumentNullException(nameof(entities));
    private readonly IEngineDamage _damage = damage ?? throw new ArgumentNullException(nameof(damage));
    private readonly IEnginePlayers _players = players ?? throw new ArgumentNullException(nameof(players));
    private readonly IEngineWorldQuery _world = world ?? throw new ArgumentNullException(nameof(world));
    private readonly IGameClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IEngineHost _host = host ?? throw new ArgumentNullException(nameof(host));

    /// <summary>
    /// Over the areas a side's container holds, and the touch links of its
    /// entity directory (null: a server with no touch directory).
    /// </summary>
    public static DebugWorld From(IServiceLocator services,
                                Func<IEnumerable<(EntityHandle A, EntityHandle B)>>? touchingPairs = null,
                                Func<EntityHandle, bool?>? asleep = null,
                                Func<EntityHandle, int?>? groundIndex = null,
                                Func<int>? tick = null)
        => new(services.Require<IEngineEntities>(), services.Require<IEngineDamage>(),
               services.Require<IEnginePlayers>(), services.Require<IEngineWorldQuery>(),
               services.Require<IGameClock>(), services.Require<IEngineHost>(), touchingPairs,
               asleep, groundIndex, tick,
               // Resolved when first asked: a test container that never points
               // a view need not register it.
               () => services.Require<IEnginePlayerMovement>());

    private static EntityHandle H(uint value) => new(value);

    private static DebugVec3 V(Vector v) => new(v.X, v.Y, v.Z);

    private static Vector V(DebugVec3 v) => new(v.X, v.Y, v.Z);

    public IReadOnlyList<uint> FindAllByClassname(string className)
        => _entities.FindAllByClassname(className).Select(h => h.Value).ToArray();

    public IReadOnlyList<uint> FindAllByName(string name)
        => _entities.FindAllByName(name).Select(h => h.Value).ToArray();

    public uint FindByName(string name) => _entities.FindByName(name).Value;

    public bool IsValid(uint entity) => _entities.IsValid(H(entity));

    public int GetIndex(uint entity) => _entities.GetIndex(H(entity));

    public string GetClassName(uint entity) => _entities.GetClassName(H(entity));

    public int GetHealth(uint entity) => _entities.GetHealth(H(entity));

    public DebugVec3 GetOrigin(uint entity) => V(_entities.GetOrigin(H(entity)));

    public DebugVec3 GetAngles(uint entity) => V(_entities.GetAngles(H(entity)));

    public DebugVec3 GetVelocity(uint entity) => V(_entities.GetVelocity(H(entity)));

    public string GetName(uint entity) => _entities.GetName(H(entity));

    public bool? IsAsleep(uint entity) => asleep?.Invoke(H(entity));

    public int? GroundIndex(uint entity) => groundIndex?.Invoke(H(entity));

    public void Remove(uint entity) => _entities.Remove(H(entity));

    public void SnapEyeAngles(uint player, DebugVec3 pitchYawRoll)
        => (movement ?? throw new InvalidOperationException("no player movement area was given"))()
            .SnapEyeAngles(H(player), V(pitchYawRoll));

    public int Tick => tick?.Invoke() ?? 0;

    public bool FireInput(uint target, string input, string? parameter, uint? activator)
        => activator is { } who
            ? _entities.FireInput(H(target), input, parameter, H(who))
            : _entities.FireInput(H(target), input, parameter);

    public void Teleport(uint entity, DebugVec3 origin, DebugVec3? angles, bool zeroVelocity)
        => _entities.Teleport(H(entity), V(origin), angles is { } a ? V(a) : null, zeroVelocity);

    public int BulletDamageType => (int)DamageType.Bullet;

    public void Damage(uint target, float amount, int damageType, DebugVec3? at)
        => Damage(target, amount, damageType, at, IDebugWorld.NoEntity);

    public void Damage(uint target, float amount, int damageType, DebugVec3? at, uint attacker)
    {
        var entity = H(target);
        var from = H(attacker);
        var type = (DamageType)damageType;

        if (at is { } p)
        {
            Vector point = V(p);
            Vector direction = (point - _entities.GetOrigin(entity)).Normalized;
            _damage.TakeDamage(entity, from, amount, type, direction, point);
        }
        else
        {
            _damage.Apply(entity, from, amount, type);
        }
    }

    public uint PlayerHandle(int slot) => _players.GetHandle(slot).Value;

    public uint TraceAim(uint player, float range, out DebugVec3 end)
    {
        var hit = _world.TracePlayerAim(H(player), range, out var point);
        end = V(point);
        return hit.Value;
    }

    public bool HasTouchDirectory => touchingPairs is not null;

    public int[] Touching(int index)
        => (touchingPairs?.Invoke() ?? [])
            .Select(p => (A: _entities.GetIndex(p.A), B: _entities.GetIndex(p.B)))
            .Where(p => p.A == index || p.B == index)
            .Select(p => p.A == index ? p.B : p.A)
            .ToArray();

    public float Now => _clock.Now;

    public string ManagedDirectory => _host.ManagedDirectory;
}
