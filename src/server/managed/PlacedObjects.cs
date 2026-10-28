//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>How long a taken item waits to come back, and how often a placed one is checked for having strayed.</summary>
[ConVar("sv_hl2mp_item_respawn_time", "30", Flags = ConVarFlags.Notify)]
public sealed class ItemRespawnTimeConVar : FloatConVar { }

/// <summary>How long a taken weapon waits to come back, and how often a placed one is checked for having strayed.</summary>
[ConVar("sv_hl2mp_weapon_respawn_time", "20", Flags = ConVarFlags.Notify)]
public sealed class WeaponRespawnTimeConVar : FloatConVar { }

/// <summary>Whether an entity's physics object is asleep; null when it has none.</summary>
public interface IPhysicsRest
{
    bool? Asleep(EntityHandle entity);
}

/// <summary>
/// One of the map's items or weapons as HL2MP's rules keep it
/// (CHL2MPRules::AddLevelDesignerPlacedObject): where it came to rest, and
/// when it is next checked for having strayed from there.
/// </summary>
public interface IPlacedObject
{
    EntityHandle Handle { get; }

    /// <summary>A weapon is checked on the weapons' respawn time, an item on the items'.</summary>
    bool IsPlacedWeapon { get; }

    Vector Origin { get; }

    /// <summary>GetOriginalSpawnOrigin: the spot it came to rest on.</summary>
    Vector HomeOrigin { get; }

    /// <summary>GetOriginalSpawnAngles: how it lay there.</summary>
    Vector HomeAngles { get; }

    /// <summary>m_flNextResetCheckTime.</summary>
    float NextResetCheck { get; set; }
}

/// <summary>
/// HL2MP's level-designer-placed objects (hl2mp_gamerules.cpp): the map's
/// items and weapons, each joined once it has come to rest, and
/// ManageObjectRelocation, which puts one back on its spot when it lies
/// asleep more than <see cref="MaxDistanceFromHome"/> away from it. An object
/// is looked at once per its respawn time; the first look comes on the frame
/// after it joins.
/// </summary>
public sealed class PlacedObjects : IFrameLifecycle, IGameLifecycle
{
    /// <summary>WEAPON_MAX_DISTANCE_FROM_SPAWN.</summary>
    public const float MaxDistanceFromHome = 64f;

    /// <summary>What a placed object sounds as it comes back, sent home or respawned.</summary>
    public const string ChargeSound = "AlyxEmp.Charge";

    private readonly IGameClock _clock;
    private readonly IPhysicsRest _rest;
    private readonly IEngineEntities _entities;
    private readonly IEngineEntityPhysics _physics;
    private readonly IEngineSound _sound;
    private readonly IEnginePrecache _precache;
    private readonly ItemRespawnTimeConVar? _itemTime;
    private readonly WeaponRespawnTimeConVar? _weaponTime;
    private readonly List<IPlacedObject> _objects = new();

    public PlacedObjects(IGameClock clock, IPhysicsRest rest, IEngineEntities entities, IEngineEntityPhysics physics,
                         IEngineSound sound, IEnginePrecache precache, ConVarRegistry? convars = null)
    {
        _clock = clock;
        _rest = rest;
        _entities = entities;
        _physics = physics;
        _sound = sound;
        _precache = precache;
        _itemTime = convars?.Get<ItemRespawnTimeConVar>();
        _weaponTime = convars?.Get<WeaponRespawnTimeConVar>();
    }

    int IFrameLifecycle.FrameOrder => (int)FrameStage.PlacedObjects;

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.PlacedObjects;

    /// <summary>sv_hl2mp_item_respawn_time (FlItemRespawnTime).</summary>
    public float ItemRespawnTime => _itemTime?.Value ?? 30f;

    /// <summary>sv_hl2mp_weapon_respawn_time (FlWeaponRespawnTime).</summary>
    public float WeaponRespawnTime => _weaponTime?.Value ?? 20f;

    /// <summary>The objects joined now. For the facts.</summary>
    public int Count => _objects.Count;

    /// <summary>Whether an entity's physics object is asleep; null when it has none.</summary>
    public bool? Asleep(EntityHandle entity) => _rest.Asleep(entity);

    /// <summary>AddLevelDesignerPlacedObject: once, however often it is asked.</summary>
    public void Add(IPlacedObject placed)
    {
        if (!_objects.Contains(placed))
            _objects.Add(placed);
    }

    /// <summary>RemoveLevelDesignerPlacedObject.</summary>
    public void Remove(IPlacedObject placed) => _objects.Remove(placed);

    /// <summary>
    /// HL2MP's charge, which a weapon or item makes when it materializes
    /// from hiding (CItem::Materialize, CWeaponHL2MPBase::Materialize).
    /// </summary>
    public void Charge(EntityHandle entity) => _sound.EntityEmitSound(entity, ChargeSound);

    void IFrameLifecycle.OnThink(float frameTime) => Relocate();

    /// <summary>CHL2MPRules::Precache.</summary>
    void IGameLifecycle.OnLevelInitPreEntity(string mapName) => _precache.PrecacheSound(ChargeSound);

    /// <summary>The rules are made again for each map, and their list with them.</summary>
    void IGameLifecycle.OnLevelShutdown() => _objects.Clear();

    /// <summary>
    /// ManageObjectRelocation: each object whose check is due has its next
    /// one set (GetObjectsOriginalParameters), and goes home when it is
    /// asleep and too far from it. Every object joins with a physics object
    /// (stock's FallThink and Materialize), so one without lies nowhere it
    /// can be put back from.
    /// </summary>
    public void Relocate()
    {
        float now = _clock.Now;
        for (int i = 0; i < _objects.Count; i++)
        {
            IPlacedObject placed = _objects[i];
            if (placed.NextResetCheck > now)
                continue;

            placed.NextResetCheck = now + (placed.IsPlacedWeapon ? WeaponRespawnTime : ItemRespawnTime);

            if ((placed.Origin - placed.HomeOrigin).Length <= MaxDistanceFromHome)
                continue;

            if (_rest.Asleep(placed.Handle) != true)
                continue;

            // Teleport( &origin, &angles, NULL ): the velocity is left as it was.
            _entities.Teleport(placed.Handle, placed.HomeOrigin, placed.HomeAngles, zeroVelocity: false);
            Charge(placed.Handle);
            _physics.Wake(placed.Handle);
        }
    }
}

/// <summary><see cref="IPhysicsRest"/> over the entity's physics object and vphysics' IsAsleep.</summary>
public sealed class EnginePhysicsRest(IEngineEntityPhysics physics, IVPhysicsFrameCalls vphysics) : IPhysicsRest
{
    public bool? Asleep(EntityHandle entity)
    {
        nint o = physics.PhysicsObject(entity);
        return o == 0 ? null : vphysics.IsAsleep(o);
    }
}
