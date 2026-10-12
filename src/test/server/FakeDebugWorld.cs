using SourceSharp.Server.Interface;

namespace SourceSharp.Tests;

/// <summary>
/// A world a dev API test can change between ticks: entities with mutable
/// fields, a tick counter the test advances, and removal that takes effect when
/// the test says the frame ended. Every member a route reaches is answered from
/// here; one that is not modelled throws rather than answering a default, so a
/// route that starts asking something new fails loudly.
/// </summary>
internal sealed class FakeDebugWorld : IDebugWorld
{
    internal sealed class Entity
    {
        public uint Handle;
        public int Index;
        public string ClassName = "prop_physics";
        public string Name = "";
        public int Health = 100;
        public DebugVec3 Origin;
        public DebugVec3 Angles;
        public DebugVec3 Velocity;
        public bool? Asleep;
        public int? Ground;
    }

    private readonly List<Entity> _entities = new();
    private readonly List<uint> _removing = new();

    public List<(uint Entity, DebugVec3 Origin, DebugVec3? Angles)> Teleports { get; } = new();

    public int Tick { get; set; }

    public Entity Add(uint handle, int index, string className = "prop_physics", string name = "")
    {
        var e = new Entity { Handle = handle, Index = index, ClassName = className, Name = name };
        _entities.Add(e);
        return e;
    }

    public Entity this[uint handle] => _entities.Single(e => e.Handle == handle);

    /// <summary>Ends the frame: what Remove asked for is gone now, as the engine's batch frees it.</summary>
    /// <summary>When set, a removal is accepted and never happens: an engine that dropped it.</summary>
    public bool IgnoreRemoves { get; set; }

    public void EndFrame()
    {
        if (!IgnoreRemoves)
            _entities.RemoveAll(e => _removing.Contains(e.Handle));
        _removing.Clear();
    }

    private Entity? Find(uint handle) => _entities.FirstOrDefault(e => e.Handle == handle);

    private Entity Must(uint handle) => Find(handle) ?? throw new InvalidOperationException($"no entity {handle}");

    public IReadOnlyList<uint> FindAllByClassname(string className)
        => _entities.Where(e => className == "*" || string.Equals(e.ClassName, className, StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.Handle).ToArray();

    public IReadOnlyList<uint> FindAllByName(string name)
        => _entities.Where(e => Debug.Api.Glob.Matches(name, e.Name)).Select(e => e.Handle).ToArray();

    public uint FindByName(string name)
        => _entities.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))?.Handle
           ?? IDebugWorld.NoEntity;

    public bool IsValid(uint entity) => Find(entity) is not null;
    public int GetIndex(uint entity) => Must(entity).Index;
    public string GetClassName(uint entity) => Must(entity).ClassName;
    public int GetHealth(uint entity) => Must(entity).Health;
    public DebugVec3 GetOrigin(uint entity) => Must(entity).Origin;
    public DebugVec3 GetAngles(uint entity) => Must(entity).Angles;
    public DebugVec3 GetVelocity(uint entity) => Must(entity).Velocity;
    public string GetName(uint entity) => Must(entity).Name;
    public bool? IsAsleep(uint entity) => Must(entity).Asleep;
    public int? GroundIndex(uint entity) => Must(entity).Ground;

    public void Remove(uint entity)
    {
        Must(entity);
        _removing.Add(entity);
    }

    public void Teleport(uint entity, DebugVec3 origin, DebugVec3? angles, bool zeroVelocity)
    {
        var e = Must(entity);
        e.Origin = origin;
        if (angles is { } a)
            e.Angles = a;
        if (zeroVelocity)
            e.Velocity = default;
        Teleports.Add((entity, origin, angles));
    }

    public List<(uint Player, DebugVec3 Angles)> EyeSnaps { get; } = new();

    public void SnapEyeAngles(uint player, DebugVec3 pitchYawRoll)
    {
        Must(player);
        EyeSnaps.Add((player, pitchYawRoll));
    }

    public uint PlayerHandle(int slot)
        => _entities.FirstOrDefault(e => e.ClassName == "player" && e.Index == slot)?.Handle ?? IDebugWorld.NoEntity;

    public float Now => Tick * 0.015f;

    public string ManagedDirectory { get; set; } = "/nonexistent/mod/bin/managed";

    public bool FireInput(uint target, string input, string? parameter, uint? activator)
        => throw new NotSupportedException(nameof(FireInput));

    public int BulletDamageType => 2;

    public void Damage(uint target, float amount, int damageType, DebugVec3? at)
        => throw new NotSupportedException(nameof(Damage));

    public void Damage(uint target, float amount, int damageType, DebugVec3? at, uint attacker)
        => throw new NotSupportedException(nameof(Damage));

    public uint TraceAim(uint player, float range, out DebugVec3 end)
        => throw new NotSupportedException(nameof(TraceAim));

    public bool HasTouchDirectory => false;

    public int[] Touching(int index) => throw new NotSupportedException(nameof(Touching));
}
