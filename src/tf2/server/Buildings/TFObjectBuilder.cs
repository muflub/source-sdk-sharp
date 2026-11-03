//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Entities.Items;
using TF2.Server.Entities.Rooms;
using TF2.Shared;

namespace TF2.Server.Buildings;

/// <summary>Stock's <c>CanBuildType</c> answer (<c>CanBuildResult</c>, tf_player.h).</summary>
public enum TFCanBuildResult
{
    /// <summary><c>CB_CAN_BUILD</c>.</summary>
    CanBuild,

    /// <summary><c>CB_UNKNOWN_OBJECT</c>: no object table row.</summary>
    UnknownObject,

    /// <summary><c>CB_CANNOT_BUILD</c>: not the engineer's trade, or no such mode.</summary>
    CannotBuild,

    /// <summary><c>CB_LIMIT_REACHED</c>: the builder already has one of this kind.</summary>
    LimitReached,

    /// <summary><c>CB_NEED_RESOURCES</c>: the metal pool is short of the cost.</summary>
    NeedResources,

    /// <summary><c>CB_IN_NOBUILD_REGION</c>: a func_nobuild holds the spot.</summary>
    InNoBuildArea,

    /// <summary><c>CB_IN_RESPAWN_ROOM</c>: a respawn room holds the spot.</summary>
    InRespawnRoom,
}

/// <summary>A build's answer: the result, and the standing object when there is one.</summary>
public sealed record TFBuildOutcome(TFCanBuildResult Result, TFBaseObject? Building = null);

/// <summary>
/// <c>CTFPlayer::CanBuild</c> (tf_player_shared.cpp:11322) and the build
/// route of <c>StartBuildingObjectOfType</c> (tf_player.h:413): the gate a
/// build passes (engineer, mode, one per kind, metal) and the birth itself
/// (spawn, placement, the no-build and respawn-room refusals of
/// <c>CBaseObject::EstimateValidBuildPos</c>, tf_obj.cpp:684, the metal
/// paid, construction started).
/// </summary>
public interface ITFObjectBuilder
{
    /// <summary><c>CTFPlayer::CanBuild</c>, for the facts and the command's message.</summary>
    TFCanBuildResult CanBuild(EntityHandle player, TFObjectType type, int mode);

    /// <summary>
    /// <c>StartBuildingObjectOfType</c>'s whole of a build at a spot —
    /// checks, spawn, placement, pay, construction. A refusal never pays.
    /// </summary>
    TFBuildOutcome Build(EntityHandle player, TFObjectType type, int mode, Vector origin, Vector angles);

    /// <summary>
    /// The carried building put back down (<c>CTFWeaponBuilder</c>'s
    /// carried <c>StartPlacement</c> and <c>PrimaryAttack</c>'s
    /// <c>BS_PLACING</c>, tf_weapon_builder.cpp:1003-1007, :358-423): the
    /// same object re-placed at the spot, no metal and the redeploy build
    /// speed; a refused spot leaves it carried.
    /// </summary>
    TFBuildOutcome Redeploy(TFBaseObject carried, Vector origin, Vector angles);

    /// <summary><c>CTFPlayer::DetonateObjectOfType</c> (tf_player.cpp:7695): every one of the builder's of a kind; how many went.</summary>
    int DestroyAll(EntityHandle player, TFObjectType type);

    /// <summary>
    /// <c>CTFPlayer::DetonateObjectOfType</c> (tf_player.cpp:8187-8232): the
    /// builder's one of a kind and mode — whether it went.
    /// </summary>
    bool DestroyOne(EntityHandle player, TFObjectType type, int mode);
}

/// <summary>
/// The world's one builder: the object table row checked, the builder's
/// standing objects counted through <see cref="ITFBuildings"/>, the spot
/// refused by <see cref="ITFNoBuildAreas"/> and
/// <see cref="ITFRespawnRooms"/> (stock's <c>EstimateValidBuildPos</c>), and
/// the metal paid through <see cref="ITFAmmoPurse"/> so a refusal never pays.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFObjectBuilder)])]
public sealed class TFObjectBuilder(ITFMapPlayers players, ITFBuildings buildings, ITFNoBuildAreas noBuild,
                                     ITFRespawnRooms rooms, ITFAmmoPurse purse, ITFObjectSpawner spawner, ILog log)
    : ITFObjectBuilder
{
    /// <summary>The metal an engineer pays with: the class script's <c>AmmoMax</c> key.</summary>
    private static string MetalName => TFAmmoNames.Name(TFAmmo.Metal);

    public TFCanBuildResult CanBuild(EntityHandle player, TFObjectType type, int mode)
    {
        if (TFObjectInfo.Of(type) is not { } info)
            return TFCanBuildResult.UnknownObject;

        // Stock's trade check and its mode check both answer CB_CANNOT_BUILD.
        if (players.ClassOf(player) != TFClass.Engineer || mode < 0 || mode >= info.Modes)
            return TFCanBuildResult.CannotBuild;

        // GetNumObjects(type, mode) (tf_player.h:402): this builder's of this
        // (type, mode), the dead not counted.
        int count = 0;
        foreach (TFBaseObject owned in buildings.OwnedBy(player, type, mode))
            count++;

        if (count >= info.MaxObjects)
            return TFCanBuildResult.LimitReached;

        if (purse.Pools(player).GetValueOrDefault(MetalName) < info.Cost)
            return TFCanBuildResult.NeedResources;

        return TFCanBuildResult.CanBuild;
    }

    /// <summary>
    /// The birth: spawn, <c>StartPlacement</c>, the placement
    /// refusals, the pay, <c>StartBuilding</c>. The refusals of
    /// <c>CBaseObject::EstimateValidBuildPos</c> run on both the floor point
    /// and the object's own centre — the mid check is what catches a tall
    /// object reaching into a no-build or a room from outside it.
    /// </summary>
    public TFBuildOutcome Build(EntityHandle player, TFObjectType type, int mode, Vector origin, Vector angles)
    {
        TFCanBuildResult can = CanBuild(player, type, mode);
        if (can != TFCanBuildResult.CanBuild)
            return new TFBuildOutcome(can);

        TFBaseObject? building = spawner.Spawn(type, origin, angles);
        if (building is null)
        {
            log.Warning($"[TF2/Build] the world refused to create {TFObjectClassNames.Raw(type)}\n");
            return new TFBuildOutcome(TFCanBuildResult.CannotBuild);
        }

        building.StartPlacement(player);
        building.SetObjectMode(mode);

        Vector center = spawner.CenterOf(building);
        if (SpotRefused(origin, type, building.Team) is { } floorReason)
            return Refused(building, floorReason);
        if (SpotRefused(center, type, building.Team) is { } centerReason)
            return Refused(building, centerReason);

        // StartedBuildingObject's pay, after the spot is known good: an
        // unfunded build has cost nothing. (Stock's pay sits in
        // StartedBuildingObject; the order — pay only once placed — is kept.)
        int cost = TFObjectInfo.Of(type)!.Cost;
        if (purse.TakeAmmo(player, cost, MetalName) < cost)
            return Refused(building, TFCanBuildResult.NeedResources);

        building.StartBuilding();
        log.Msg($"[TF2/Build] {TFObjectClassNames.Raw(type)} mode {mode} placed at "
              + $"({origin.X}, {origin.Y}, {origin.Z}) for {player}\n");
        return new TFBuildOutcome(TFCanBuildResult.CanBuild, building);
    }

    /// <summary>
    /// The redeploy: the spot refused or not, then the same entity down at
    /// the new spot — no pay (the metal went in at the first build) and the
    /// redeploy speed of <c>CTFPlayer::GetObjectBuildSpeedMultiplier</c>
    /// (tf_player.cpp:8237-8260: +2 for the sentry, +3 the others, on the
    /// 1.0 base). A refusal leaves the building in the arms.
    /// </summary>
    public TFBuildOutcome Redeploy(TFBaseObject carried, Vector origin, Vector angles)
    {
        // EstimateValidBuildPos's two refusals, at the new spot and at the
        // spot the object's own centre will land on (the mid check is the
        // tall object's). The offset survives the move; the object itself
        // does not move until the spot is known good.
        Vector offset = spawner.CenterOf(carried) - spawner.OriginOf(carried);
        if (SpotRefused(origin, carried.Type, carried.Team) is { } floorReason)
            return RefusedCarried(carried, floorReason);
        if (SpotRefused(origin + offset, carried.Type, carried.Team) is { } centerReason)
            return RefusedCarried(carried, centerReason);

        float rate = carried.Type switch
        {
            TFObjectType.Sentrygun => 3f,
            _ => 4f,
        };

        carried.StartRedeploy(origin, angles, rate);
        log.Msg($"[TF2/Build] {TFObjectClassNames.Raw(carried.Type)} redeployed at "
              + $"({origin.X}, {origin.Y}, {origin.Z}) for {carried.Builder}\n");
        return new TFBuildOutcome(TFCanBuildResult.CanBuild, carried);
    }

    /// <summary>A refused redeploy: nothing moves, the arms keep hold.</summary>
    private TFBuildOutcome RefusedCarried(TFBaseObject carried, TFCanBuildResult reason)
    {
        log.Msg($"[TF2/Build] {TFObjectClassNames.Raw(carried.Type)} redeploy refused: {reason}\n");
        return new TFBuildOutcome(reason);
    }

    public int DestroyAll(EntityHandle player, TFObjectType type)
    {
        int destroyed = 0;
        foreach (TFBaseObject owned in buildings.OwnedBy(player, type))
        {
            owned.DestroyObject();
            destroyed++;
        }

        return destroyed;
    }

    /// <summary>
    /// <c>CTFPlayer::DetonateObjectOfType</c> (tf_player.cpp:8187-8232): the
    /// builder's first of the kind and mode. A sapped building cannot be
    /// detonated by its own hand — stock's
    /// <c>!bIgnoreSapperState &amp;&amp; HasSapper()</c> refusal (:8193); the
    /// plasma-disabled arm is MvM's, which the port does not carry.
    /// </summary>
    public bool DestroyOne(EntityHandle player, TFObjectType type, int mode)
    {
        TFBaseObject? target = buildings.OwnedBy(player, type, mode).FirstOrDefault();
        if (target is null || target.HasSapper)
            return false;

        target.DestroyObject();
        return true;
    }

    /// <summary><c>EstimateValidBuildPos</c>'s two refusals at one point, stock's order.</summary>
    private TFCanBuildResult? SpotRefused(Vector point, TFObjectType type, int team)
    {
        if (noBuild.PointInNoBuild(point, type, team))
            return TFCanBuildResult.InNoBuildArea;

        if (rooms.PointInRespawnRoom(point))
            return TFCanBuildResult.InRespawnRoom;

        return null;
    }

    private TFBuildOutcome Refused(TFBaseObject building, TFCanBuildResult reason)
    {
        log.Msg($"[TF2/Build] {TFObjectClassNames.Raw(building.Type)} mode {building.Mode} refused: {reason}\n");
        spawner.Remove(building);
        return new TFBuildOutcome(reason);
    }
}

/// <summary>
/// The ammo the metal economy runs on: the core's <see cref="Arsenal"/> pools
/// and its one subtraction, as a seam the buildings can be tested over
/// (the <see cref="TFArsenalWeapons"/> shape).
/// </summary>
public interface ITFAmmoPurse
{
    /// <summary><c>Arsenal.TakeAmmo</c>: take up to the count; what came away.</summary>
    int TakeAmmo(EntityHandle player, int count, string ammoName);

    /// <summary><c>Arsenal.Pools</c>: the player's ammo pools by name.</summary>
    IReadOnlyDictionary<string, int> Pools(EntityHandle player);
}

/// <summary>The purse the core's <see cref="Arsenal"/> answers.</summary>
[SideService(Side.Server, As = [typeof(ITFAmmoPurse)])]
public sealed class TFAmmoPurse(Arsenal arsenal) : ITFAmmoPurse
{
    public int TakeAmmo(EntityHandle player, int count, string ammoName) => arsenal.TakeAmmo(player, count, ammoName);

    public IReadOnlyDictionary<string, int> Pools(EntityHandle player) => arsenal.Pools(player);
}

/// <summary>
/// The building's birth and burial, where the world does them: created at the
/// spot, and removed again when a placement is refused. (The managed half of
/// creation is the scripted registry's; the real spawner asks the engine,
/// which binds the two.)
/// </summary>
public interface ITFObjectSpawner
{
    /// <summary>The object of this type standing at the spot, or null when the world refuses.</summary>
    TFBaseObject? Spawn(TFObjectType type, Vector origin, Vector angles);

    /// <summary>The object's centre — the mid point <c>EstimateValidBuildPos</c> re-checks.</summary>
    Vector CenterOf(TFBaseObject building);

    /// <summary>The object's origin, as the world holds it.</summary>
    Vector OriginOf(TFBaseObject building);

    /// <summary>Take the object out of the world (a refused placement).</summary>
    void Remove(TFBaseObject building);
}

/// <summary>The spawner the engine answers: create, place, spawn; and remove.</summary>
[SideService(Side.Server, As = [typeof(ITFObjectSpawner)])]
public sealed class TFObjectSpawner(IEngineEntities entities, IEntityDirectory directory) : ITFObjectSpawner
{
    public TFBaseObject? Spawn(TFObjectType type, Vector origin, Vector angles)
    {
        EntityHandle handle = entities.Create(TFObjectClassNames.Raw(type));
        if (handle == EntityHandle.None)
            return null;

        entities.SetOrigin(handle, origin);
        entities.SetAngles(handle, angles);
        entities.Spawn(handle);
        return directory.Get(handle) as TFBaseObject;
    }

    public Vector CenterOf(TFBaseObject building) => entities.GetWorldSpaceCenter(building.Handle);

    public Vector OriginOf(TFBaseObject building) => entities.GetOrigin(building.Handle);

    public void Remove(TFBaseObject building) => entities.Remove(building.Handle);
}
