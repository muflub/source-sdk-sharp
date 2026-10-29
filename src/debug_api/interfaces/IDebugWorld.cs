namespace Debug.Api.Server.Interfaces;

/// <summary>
/// The server's entities, players and world, as the dev API drives them
/// (group G2). Handles are raw values (<see cref="IDebugWorld.NoEntity"/> is none). Everything
/// here touches the engine: call it on the game thread, except the two
/// properties.
/// </summary>
public interface IDebugWorld
{
    /// <summary>
    /// No entity. Never 0: entry 0 is the world. A constant on the interface
    /// rather than a static class, which this assembly may not hold
    /// (InterfaceAssemblyShapeTests).
    /// </summary>
    const uint NoEntity = 0xFFFFFFFF;

    /// <summary>Every entity of a class; "*" is all of them.</summary>
    IReadOnlyList<uint> FindAllByClassname(string className);

    /// <summary>Every entity carrying a targetname, up to the engine's limit.</summary>
    IReadOnlyList<uint> FindAllByName(string name);

    /// <summary>The first entity carrying a targetname, or <see cref="NoEntity"/>.</summary>
    uint FindByName(string name);

    bool IsValid(uint entity);

    int GetIndex(uint entity);

    string GetClassName(uint entity);

    int GetHealth(uint entity);

    DebugVec3 GetOrigin(uint entity);

    DebugVec3 GetAngles(uint entity);

    /// <summary>
    /// How fast it is going, in units per second: the solver's velocity for a
    /// physics object, the entity's own otherwise.
    /// </summary>
    DebugVec3 GetVelocity(uint entity);

    /// <summary>Its targetname, or "" for none.</summary>
    string GetName(uint entity);

    /// <summary>Its solid type (SOLID_NONE 0, SOLID_BSP 1, SOLID_BBOX 2, SOLID_OBB 3, SOLID_VPHYSICS 6).</summary>
    int GetSolidType(uint entity);

    /// <summary>Its FSOLID_ flags.</summary>
    int GetSolidFlags(uint entity);

    /// <summary>Its physics object: 0 none, 1 static, 2 a shadow, 3 simulated.</summary>
    int GetPhysicsKind(uint entity);

    /// <summary>
    /// Whether its physics object is asleep, as the last wake or sleep event the
    /// solver delivered for it said; null when none was ever delivered (no
    /// physics object, or one that has not changed state since it was made).
    /// </summary>
    bool? IsAsleep(uint entity);

    /// <summary>
    /// The entity index a player stands on, -1 for none; null for an entity
    /// that keeps no ground (anything not a player).
    /// </summary>
    int? GroundIndex(uint entity);

    /// <summary>
    /// Points a player's VIEW, as <c>setang</c> does: the eye angles, with
    /// fixangle so the client adopts them rather than predicting over them.
    /// </summary>
    void SnapEyeAngles(uint player, DebugVec3 pitchYawRoll);

    /// <summary>Removes it, as the Kill input does: at the end of the frame.</summary>
    void Remove(uint entity);

    /// <summary>
    /// The server's tick count: one per simulated server frame, which is what
    /// the dev API's waits count in. Game thread.
    /// </summary>
    int Tick { get; }

    /// <summary>
    /// Delivers an input; whether the entity accepted it. With no activator the
    /// engine's own default applies.
    /// </summary>
    bool FireInput(uint target, string input, string? parameter, uint? activator);

    void Teleport(uint entity, DebugVec3 origin, DebugVec3? angles, bool zeroVelocity);

    /// <summary>The damage type bits a plain hit carries (a bullet).</summary>
    int BulletDamageType { get; }

    /// <summary>
    /// Damages <paramref name="target"/> with no attacker: at a point (pushed
    /// away from it) when <paramref name="at"/> is given, else plainly.
    /// </summary>
    void Damage(uint target, float amount, int damageType, DebugVec3? at);

    /// <summary>
    /// As <see cref="Damage(uint, float, int, DebugVec3?)"/>, credited to
    /// <paramref name="attacker"/> (<see cref="NoEntity"/> for nobody), so a hit
    /// can come FROM somewhere: the player's Damage message carries the
    /// attacker's origin.
    /// </summary>
    void Damage(uint target, float amount, int damageType, DebugVec3? at, uint attacker);

    /// <summary>The player in a slot (1-based), or an invalid handle.</summary>
    uint PlayerHandle(int slot);

    /// <summary>What a player is looking at within <paramref name="range"/>, and where the look ends.</summary>
    uint TraceAim(uint player, float range, out DebugVec3 end);

    /// <summary>Whether this server has a touch directory to answer <see cref="Touching"/> from. Any thread.</summary>
    bool HasTouchDirectory { get; }

    /// <summary>
    /// The entity indices <paramref name="index"/> is linked to in the touch
    /// directory right now, each once per link.
    /// </summary>
    int[] Touching(int index);

    /// <summary>The game clock, in seconds. Any thread.</summary>
    float Now { get; }

    /// <summary>The mod's managed assembly directory. Any thread.</summary>
    string ManagedDirectory { get; }
}
