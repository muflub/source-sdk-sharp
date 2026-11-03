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
using TF2.Shared.Movement;

namespace TF2.Server.Buildings;

/// <summary>
/// The arms the builder comes out on: the core's <see cref="Arsenal"/> switch
/// and its grant, as a seam the buildings can be tested over (the
/// <see cref="ITFAmmoPurse"/> shape).
/// </summary>
public interface ITFBuilderArms
{
    /// <summary><c>Arsenal.Switch</c>.</summary>
    SwitchOutcome Switch(EntityHandle player, string className);

    /// <summary><c>Arsenal.GiveWeapon</c>.</summary>
    bool GiveWeapon(EntityHandle player, string className);

    /// <summary><c>Arsenal.SwitchLast</c>, the place's SwitchOwnersWeaponToLast (tf_weapon_builder.cpp:421).</summary>
    SwitchOutcome SwitchLast(EntityHandle player);
}

/// <summary>The arms the core's <see cref="Arsenal"/> answers.</summary>
[SideService(Side.Server, As = [typeof(ITFBuilderArms)])]
public sealed class TFBuilderArms(Arsenal arsenal) : ITFBuilderArms
{
    public SwitchOutcome Switch(EntityHandle player, string className) => arsenal.Switch(player, className);

    public bool GiveWeapon(EntityHandle player, string className) => arsenal.GiveWeapon(player, className);

    public SwitchOutcome SwitchLast(EntityHandle player) => arsenal.SwitchLast(player);
}

/// <summary>
/// The click-to-place half of build, as the server answers the builder's
/// shell (<see cref="ITFBuilderPlacement"/>): the blueprint born at the
/// deploy, walked to the crosshair every command, placed by the trigger,
/// turned by the alt-fire, and taken back down when the builder leaves the
/// hands.
///
/// <para>
/// <b>The geometry</b> is stock's <c>CBaseObject::CalculatePlacementPos</c>
/// (baseobject_shared.cpp:509-634) and <c>IsPlacementPosValid</c>
/// (:674-738) with <c>EstimateValidBuildPos</c> (tf_obj.cpp:684-718): the
/// build spot an eye-yaw step ahead of the player's centre, a hull walk down
/// to the ground, four corners verified against the slope, then the fit, the
/// sight line and the other-buildings window. The refusals the seams name
/// (no-build, respawn room) run at the spot and the spot the object's own
/// centre lands on, and the far-edge ray asks the visualizers whether the
/// build would reach across one.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFBuilderPlacement)])]
public sealed class TFBuilderPlacement(IEnginePlayers enginePlayers, IEngineEntities entities,
                                       IEngineWorldQuery world, ITFNoBuildAreas noBuild,
                                       ITFRespawnRooms rooms, ITFBuildings buildings,
                                       ITFObjectBuilder objects, ITFAmmoPurse purse,
                                       ITFObjectSpawner spawner, ITFSapperState sapper,
                                       ITFBuilderArms? arms = null, ILog? log = null)
    : ITFBuilderPlacement
{
    /// <summary><c>TF_OBJ_GROUND_CLEARANCE</c> (baseobject_shared.h:34): how far a corner verifies down.</summary>
    public const float GroundClearance = 32f;

    /// <summary>The slope a corner refuses (baseobject_shared.cpp:661): normal·up under 0.65.</summary>
    public const float SlopeNormalZ = 0.65f;

    /// <summary>The small safety buffer of the build distance (baseobject_shared.cpp:542).</summary>
    public const float BuildBuffer = 4f;

    /// <summary>The ±4 the build box wears over the hull (tf_obj.cpp:896-897).</summary>
    public const float BuildBoxPadding = 4f;

    /// <summary>The hull walk's iterations (baseobject_shared.cpp:567).</summary>
    public const int SweepIterations = 8;

    /// <summary>The other-buildings window across and up (baseobject_shared.cpp:713-716): 24.</summary>
    public const float OtherBuildingBox = 24f;

    /// <summary>The other-buildings window down (baseobject_shared.cpp:714): flBoxDepth, 32.</summary>
    public const float OtherBuildingDepth = 32f;

    /// <summary>The lead over the build distance on the far-edge ray (tf_obj.cpp:713): +8.</summary>
    public const float VisualizerLead = 8f;

    /// <summary>The builder's row (the fixture script's name).</summary>
    public const string BuilderWeapon = "tf_weapon_builder";

    /// <summary>The metal an engineer pays with: the class script's <c>AmmoMax</c> key.</summary>
    private static string MetalName => TFAmmoNames.Name(TFAmmo.Metal);

    private sealed class Placing
    {
        public TFBaseObject? Blueprint;
        public int Mode;
        public bool Carried;
    }

    private sealed class Order
    {
        public TFObjectType Type;
        public int Mode;
    }

    /// <summary>The blueprint each placing player walks, keyed by the player's handle.</summary>
    private readonly Dictionary<EntityHandle, Placing> _placing = new();

    /// <summary>The orders the console named, consumed by the next deploy.</summary>
    private readonly Dictionary<EntityHandle, Order> _pending = new();

    public bool BeginOrder(EntityHandle player, TFObjectType type, int mode)
    {
        if (objects.CanBuild(player, type, mode) != TFCanBuildResult.CanBuild)
            return false;

        _pending[player] = new Order { Type = type, Mode = mode };
        return true;
    }

    /// <summary>
    /// <c>CTFWeaponBuilder::StartPlacement</c> (tf_weapon_builder.cpp:995-1036):
    /// the previous placement stops, then the carried branch resumes the
    /// lifted building (:1003-1007) or a fresh blueprint is born — the object
    /// created, its mode set, spawned and placed.
    /// </summary>
    public void StartPlacement(int ownerIndex)
    {
        EntityHandle player = enginePlayers.GetHandle(ownerIndex);
        if (player == EntityHandle.None)
            return;

        StopPlacement(ownerIndex);

        if (_pending.Remove(player, out Order? order))
        {
            TFBaseObject? blueprint = spawner.Spawn(order.Type, entities.GetOrigin(player), Vector.Zero);
            if (blueprint is null)
            {
                log?.Warning($"[TF2/Build] the world refused to create the {TFObjectClassNames.Raw(order.Type)} blueprint\n");
                return;
            }

            blueprint.SetObjectMode(order.Mode);
            blueprint.StartPlacement(player);
            _placing[player] = new Placing { Blueprint = blueprint, Mode = order.Mode };
            log?.Msg($"[TF2/Build] {TFObjectClassNames.Raw(order.Type)} mode {order.Mode} blueprint out for {player}\n");
            return;
        }

        if (buildings.OwnedBy(player).FirstOrDefault(b => b.IsCarried && !b.IsDead) is { } carried)
        {
            carried.ResumePlacement(player);
            _placing[player] = new Placing { Blueprint = carried, Carried = true };
            log?.Msg($"[TF2/Build] {TFObjectClassNames.Raw(carried.Type)} back out of the arms for {player}\n");
        }
    }

    /// <summary>
    /// The sapper's placement ghost (<c>CTFWeaponBuilder::StartPlacement</c>'s
    /// attachment arm, tf_weapon_builder.cpp:995-1036, over
    /// <c>CBaseObject::Spawn</c> at the owner): the sapper's blueprint in its
    /// placement body, born now and walked to the sappable the same way the
    /// builder's is — the ghost's first pose is this command's walk.
    /// </summary>
    public void StartAttachmentPlacement(int ownerIndex)
    {
        EntityHandle player = enginePlayers.GetHandle(ownerIndex);
        if (player == EntityHandle.None)
            return;

        StopPlacement(ownerIndex);

        TFBaseObject? ghost = spawner.Spawn(TFObjectType.AttachmentSapper, entities.GetOrigin(player), Vector.Zero);
        if (ghost is null)
        {
            log?.Warning($"[TF2/Build] the world refused to create the {TFObjectClassNames.Raw(TFObjectType.AttachmentSapper)} ghost\n");
            return;
        }

        ghost.StartPlacement(player);
        _placing[player] = new Placing { Blueprint = ghost };
        WalkAttachmentPlacement(ownerIndex, ghost);
        log?.Msg($"[TF2/Build] {TFObjectClassNames.Raw(TFObjectType.AttachmentSapper)} ghost out for {player}\n");
    }

    /// <summary>
    /// <c>UpdatePlacementState</c> (tf_weapon_builder.cpp:535-560): the
    /// blueprint is walked to where the crosshair looks, one ask per command.
    /// A dead or gone blueprint clears the state.
    /// </summary>
    public void UpdatePlacement(int ownerIndex)
    {
        EntityHandle player = enginePlayers.GetHandle(ownerIndex);
        if (player == EntityHandle.None || !_placing.TryGetValue(player, out Placing? placing))
            return;

        if (placing.Blueprint is not { } blueprint || blueprint.IsDead)
        {
            _placing.Remove(player);
            return;
        }

        // The attachment ghost walks the hostile find, not the ground spot:
        // UpdatePlacement branches on the attachment objects
        // (tf_obj.cpp:1150-1156 → UpdateAttachmentPlacement).
        if (blueprint.Type == TFObjectType.AttachmentSapper)
            WalkAttachmentPlacement(ownerIndex, blueprint);
        else
            WalkPlacement(player, blueprint);
    }

    /// <summary>
    /// The attachment walk (<c>CBaseObject::UpdateAttachmentPlacement</c> →
    /// <c>FindSnapToBuildPos</c>'s hostile arm, tf_obj.cpp:663-679,
    /// :1169-1280): a sappable under the crosshair and the sapper's find
    /// takes the ghost — teleported onto the building's point and shown;
    /// none, the ghost is hidden where it stands
    /// (<c>EF_NODRAW</c>, :1263-1277) and unmoved.
    /// </summary>
    private void WalkAttachmentPlacement(int ownerIndex, TFBaseObject ghost)
    {
        if (!sapper.FindSappable(ownerIndex, out EntityHandle building, out Vector point))
        {
            ghost.SetPlacementVisible(false);
            return;
        }

        entities.Teleport(ghost.Handle, point, entities.GetAngles(ghost.Handle), zeroVelocity: true);
        ghost.SetPlacementVisible(true);
    }

    /// <summary>
    /// <c>CTFWeaponBuilder::PrimaryAttack</c>'s <c>BS_PLACING</c> arm
    /// (tf_weapon_builder.cpp:358-423): the spot re-walked, and valid — the
    /// buildable stands there. Carried, the redeploy answers; fresh, the
    /// metal is paid (<c>StartBuilding</c>'s <c>StartedBuildingObject</c>,
    /// tf_obj.cpp:1329-1336), construction starts and the builder switches
    /// away (<c>SwitchOwnersWeaponToLast</c>, :489-530). An invalid spot, a
    /// refused redeploy or unpaid metal leaves the blueprint placing.
    /// </summary>
    public bool Place(int ownerIndex)
    {
        EntityHandle player = enginePlayers.GetHandle(ownerIndex);
        if (player == EntityHandle.None || !_placing.TryGetValue(player, out Placing? placing))
            return false;

        if (placing.Blueprint is not { } blueprint || blueprint.IsDead)
        {
            _placing.Remove(player);
            return false;
        }

        (Vector origin, bool valid, string refusal) = WalkPlacement(player, blueprint);
        if (!valid)

        {
            // Stock denies the place with a VO; the console line is what this
            // build has where the voice would be.
            log?.Msg($"[TF2/Build] {TFObjectClassNames.Raw(blueprint.Type)} mode {placing.Mode} spot refused at ({origin.X}, {origin.Y}, {origin.Z}): {refusal}\n");
            return false;
        }

        Vector angles = new(0f, BuildYaw(player, blueprint), 0f);

        if (placing.Carried)
        {
            TFBuildOutcome outcome = objects.Redeploy(blueprint, origin, angles);
            if (outcome.Result != TFCanBuildResult.CanBuild)
                return false;
        }
        else
        {
            // StartedBuildingObject's pay: a short purse aborts (tf_obj.cpp:1330-1336).
            int cost = TFObjectInfo.Of(blueprint.Type)!.Cost;
            if (purse.TakeAmmo(player, cost, MetalName) < cost)
            {
                log?.Msg($"[TF2/Build] {TFObjectClassNames.Raw(blueprint.Type)} mode {placing.Mode} unpaid; the blueprint comes down\n");
                blueprint.DestroyObject();
                _placing.Remove(player);
                return false;
            }

            blueprint.StartBuilding();
            log?.Msg($"[TF2/Build] {TFObjectClassNames.Raw(blueprint.Type)} mode {placing.Mode} placed at "
                     + $"({origin.X}, {origin.Y}, {origin.Z}) for {player}\n");
        }

        _placing.Remove(player);
        arms?.SwitchLast(player);
        return true;
    }

    /// <summary><c>RotateBuildAngles</c> (tf_obj.cpp:3416-3421), on the blueprint.</summary>
    public void Rotate(int ownerIndex)
    {
        if (enginePlayers.GetHandle(ownerIndex) is { } player && player != EntityHandle.None
            && _placing.TryGetValue(player, out Placing? placing))
            placing.Blueprint?.RotateBuildAngles();
    }

    /// <summary>
    /// <c>CTFWeaponBuilder::StopPlacement</c> (tf_weapon_builder.cpp:1041-1055):
    /// the carried building goes back into the arms; a fresh blueprint is
    /// taken out of the world (<c>CBaseObject::StopPlacement</c> → removal,
    /// tf_obj.cpp:908-911).
    /// </summary>
    public void StopPlacement(int ownerIndex)
    {
        EntityHandle player = enginePlayers.GetHandle(ownerIndex);
        if (player == EntityHandle.None || !_placing.Remove(player, out Placing? placing))
            return;

        if (placing.Blueprint is not { } blueprint || blueprint.IsDead)
            return;

        if (blueprint.IsCarried)
            blueprint.MakeCarriedObject(player);
        else
            blueprint.DestroyObject();
    }

    /// <summary>
    /// The build order's arming (<c>StartBuildingObjectOfType</c>'s
    /// <c>Weapon_Switch</c>, tf_player_shared.cpp:13234-13258): the builder
    /// into the hands — given first when the loadout has none. Answers
    /// whether a builder is out; a re-order while one is already out
    /// restarts the placement on the new type, stock's next deploy's
    /// <c>StartPlacement</c> over the new <c>m_iObjectType</c>.
    /// </summary>
    public bool ArmBuilder(int ownerIndex)
    {
        EntityHandle player = enginePlayers.GetHandle(ownerIndex);
        if (player == EntityHandle.None || arms is null)
            return false;

        switch (arms.Switch(player, BuilderWeapon))
        {
            case SwitchOutcome.Switched:
                return true;
            case SwitchOutcome.AlreadyHeld:
                if (_pending.ContainsKey(player))
                    StartPlacement(ownerIndex);
                return true;
            case SwitchOutcome.NotOwned:
                // The engineer's loadout carries no builder until given: grant
                // one and switch to it, the arms the pickup's builder rides.
                if (!arms.GiveWeapon(player, BuilderWeapon))
                    return false;
                return arms.Switch(player, BuilderWeapon) is SwitchOutcome.Switched or SwitchOutcome.AlreadyHeld;
            default:
                return false;
        }
    }

    /// <summary>
    /// One walk of the placement: the spot computed and the blueprint
    /// teleported to it — or to the error origin, when the spot refused —
    /// <c>UpdatePlacement</c>'s Teleport (tf_obj.cpp:1159-1161). The walked
    /// origin and whether it is valid come back.
    /// </summary>
    private (Vector Origin, bool Valid, string Refusal) WalkPlacement(EntityHandle player, TFBaseObject blueprint)
    {
        PlacementSpot spot = CalculatePlacementPos(player, blueprint);
        string refusal = "";
        bool valid = spot.Valid && IsValidPlacement(player, blueprint, spot, out refusal);
        entities.Teleport(blueprint.Handle, valid ? spot.Origin : spot.ErrorOrigin,
                          new Vector(0f, spot.BuildYaw, 0f), zeroVelocity: true);
        return (valid ? spot.Origin : spot.ErrorOrigin, valid, spot.Valid ? refusal : spot.Refusal);
    }

    /// <summary><c>IsPlacementPosValid</c> (baseobject_shared.cpp:674-738) at an already-walked spot.</summary>
    private bool IsValidPlacement(EntityHandle player, TFBaseObject blueprint, PlacementSpot spot, out string refusal)
    {
        refusal = "";
        Vector origin = spot.Origin;
        TFObjectType type = blueprint.Type;
        int team = blueprint.Team;

        // EstimateValidBuildPos (tf_obj.cpp:684-718): the no-build and
        // respawn-room refusals at the spot and at the spot the object's own
        // centre lands on. (The trigger-hurt arm has no seam here — the
        // divergence ledger's.)
        (Vector mins, Vector maxs) = BuildBox(type);
        Vector halfDims = (maxs - mins) * 0.5f;
        Vector centerOfMass = origin + new Vector(0f, 0f, halfDims.Z);

        if (noBuild.PointInNoBuild(origin, type, team)
            || noBuild.PointInNoBuild(centerOfMass, type, team))
        {
            refusal = "the spot is in a no-build area";
            return false;
        }

        if (rooms.PointInRespawnRoom(origin) || rooms.PointInRespawnRoom(centerOfMass))
        {
            refusal = "the spot is in a respawn room";
            return false;
        }

        // The far edge of the build box, led past the build distance, and the
        // player's centre to it (:713-715): a spot whose build would reach
        // across a respawn-room visualizer refuses. Stock clips MASK_ALL at
        // the visualizer's box — <see cref="ITFRespawnRooms"/>'s divergence.
        Vector farEdge = origin + spot.Forward * (spot.BuildDistance + VisualizerLead);
        if (rooms.PointsCrossRespawnRoomVisualizer(spot.PlayerCenter, farEdge))
        {
            refusal = "a respawn-room visualizer stands behind the spot";
            return false;
        }

        // The whole object must fit (UTIL_TraceEntity, :698-701): the build
        // box swept in place, MASK_SOLID, against everything.
        if (world.TraceBox(origin, origin, mins, maxs, TraceMask.Solid, blueprint.Handle).Fraction < 1f)
        {
            refusal = "the object does not fit";
            return false;
        }

        // The eye must reach the final position (the 2×2 hull of :704-708).
        Vector eye = entities.GetEyePosition(player);
        Vector to = origin + new Vector(0f, 0f, maxs.Z * 0.5f);
        if (world.TraceBox(eye, to, new Vector(-2f, -2f, -2f), new Vector(2f, 2f, 2f),
                           TraceMask.Brushes, player).Fraction < 1f)
        {
            refusal = "the eye does not reach the spot";
            return false;
        }

        // Not on top of another building (:710-735): the 24-across window
        // around the spot, the placing ones (the blueprints) skipped.
        foreach (TFBaseObject owned in buildings.OwnedBy(blueprint.Builder))
        {
            if (owned == blueprint || owned.IsDead || owned.IsPlacing)
                continue;

            Vector where = spawner.OriginOf(owned);
            if (MathF.Abs(where.X - origin.X) <= OtherBuildingBox
                && MathF.Abs(where.Y - origin.Y) <= OtherBuildingBox
                && where.Z >= origin.Z - OtherBuildingDepth
                && where.Z <= origin.Z + OtherBuildingBox)
            {
                refusal = "another building stands on the spot";
                return false;
            }
        }

        return true;
    }

    /// <summary>The build box over the hull, ±4 on X and Y (tf_obj.cpp:895-899).</summary>
    private static (Vector Mins, Vector Maxs) BuildBox(TFObjectType type)
    {
        (Vector mins, Vector maxs) = TFObjectModels.Hull(type);
        return (mins - new Vector(BuildBoxPadding, BuildBoxPadding, 0f),
                maxs + new Vector(BuildBoxPadding, BuildBoxPadding, 0f));
    }

    private readonly record struct PlacementSpot(bool Valid, string Refusal, Vector Origin, Vector ErrorOrigin,
                                                 float BuildYaw, Vector PlayerCenter, Vector Forward, float BuildDistance);

    /// <summary>
    /// <c>CBaseObject::CalculatePlacementPos</c> (baseobject_shared.cpp:509-634):
    /// the build spot an eye-yaw step ahead of the player's centre, sized to
    /// the object's box and the player's, walked down to the ground in eight
    /// sweeps, its corners verified against the slope.
    /// </summary>
    private PlacementSpot CalculatePlacementPos(EntityHandle player, TFBaseObject blueprint)
    {
        float eyeYaw = entities.GetEyeAngles(player).Y;
        float buildYaw = eyeYaw + 90f * blueprint.DesiredBuildRotations;

        // The forward of the eye yaw alone (no pitch, no rotation):
        // AngleVectors(vecAngles, &m_vecBuildForward), :529.
        float yawRad = eyeYaw * (MathF.PI / 180f);
        Vector forward = new(MathF.Cos(yawRad), MathF.Sin(yawRad), 0f);

        (Vector mins, Vector maxs) = BuildBox(blueprint.Type);
        Vector dims = maxs - mins;
        Vector halfDims = dims * 0.5f;
        Vector halfXY = new(halfDims.X, halfDims.Y, 0f);
        Vector negHalfXY = new(-halfDims.X, -halfDims.Y, 0f);

        Vector playerMins = TFViewVectors.Params.HullMin;
        Vector playerMaxs = TFViewVectors.Params.HullMax;
        float objectRadius = MathF.Max(MathF.Abs(mins.X), MathF.Abs(maxs.X));
        objectRadius = MathF.Max(objectRadius, MathF.Max(MathF.Abs(mins.Y), MathF.Abs(maxs.Y)));
        float playerRadiusX = MathF.Max(MathF.Abs(playerMins.X), MathF.Abs(playerMaxs.X));
        float playerRadiusY = MathF.Max(MathF.Abs(playerMins.Y), MathF.Abs(playerMaxs.Y));
        float buildDistance = MathF.Sqrt(objectRadius * objectRadius + objectRadius * objectRadius)
                            + MathF.Sqrt(playerRadiusX * playerRadiusX + playerRadiusY * playerRadiusY)
                            + BuildBuffer;

        Vector playerCenter = entities.GetWorldSpaceCenter(player);
        Vector buildOrigin = playerCenter + forward * buildDistance;
        Vector errorOrigin = buildOrigin - dims * 0.5f - mins;

        // The hull walk (:552-606): the box's top starts above the player's
        // head and sweeps down in eight steps; no ground, or no room for the
        // box, refuses the spot. (Stock's `!tr.startsolid` arm rides the
        // engine's start-solid answer, which the ABI's surface trace does not
        // carry — the divergence ledger's.)
        float halfPlayerZ = (playerMaxs.Z - playerMins.Z) * 0.5f;
        float topZ = playerCenter.Z + halfPlayerZ + dims.Z;
        float bottomLimit = playerCenter.Z - halfPlayerZ - dims.Z;
        float topZInc = (bottomLimit - topZ) / (SweepIterations - 1);
        float bottomZ = 0f;
        bool fits = false;

        for (int i = 0; i < SweepIterations; i++)
        {
            SurfaceTraceResult tr = world.TraceBox(new Vector(buildOrigin.X, buildOrigin.Y, topZ),
                                                  new Vector(buildOrigin.X, buildOrigin.Y, bottomLimit),
                                                  negHalfXY, halfXY, TraceMask.Brushes, blueprint.Handle);
            if (tr.Fraction >= 1f)
                return new PlacementSpot(false, "the sweep finds no ground", buildOrigin, errorOrigin, buildYaw,
                                         playerCenter, forward, buildDistance);

            bottomZ = tr.End.Z;
            if (topZ - bottomZ > dims.Z)
            {
                fits = true;
                break;
            }

            topZ += topZInc;
        }

        if (!fits)
            return new PlacementSpot(false, "the sweep finds no room for the box", buildOrigin, errorOrigin, buildYaw,
                                     playerCenter, forward, buildDistance);

        // The corners verify against the slope (:615-624): each started 2
        // above the ground and traced down the clearance, a hit whose normal
        // stands up enough — and a hit at all, stock's `fraction < 1` —
        // or the spot refuses.
        Vector bottomCenter = new(buildOrigin.X, buildOrigin.Y, bottomZ);
        if (!VerifyCorner(bottomCenter, -halfDims.X, -halfDims.Y, blueprint.Handle)
            || !VerifyCorner(bottomCenter, halfDims.X, halfDims.Y, blueprint.Handle)
            || !VerifyCorner(bottomCenter, halfDims.X, -halfDims.Y, blueprint.Handle)
            || !VerifyCorner(bottomCenter, -halfDims.X, halfDims.Y, blueprint.Handle))
            return new PlacementSpot(false, "a corner of the spot slopes or hangs", buildOrigin, errorOrigin, buildYaw,
                                     playerCenter, forward, buildDistance);

        // The box re-offset to where its bottom stands on the ground
        // (:626-631).
        Vector origin = new(buildOrigin.X - halfDims.X - mins.X,
                            buildOrigin.Y - halfDims.Y - mins.Y,
                            bottomZ - mins.Z);
        return new PlacementSpot(true, "", origin, errorOrigin, buildYaw, playerCenter, forward, buildDistance);
    }

    /// <summary><c>CBaseObject::VerifyCorner</c> (baseobject_shared.cpp:639-668).</summary>
    private bool VerifyCorner(Vector bottomCenter, float xOffset, float yOffset, EntityHandle ignore)
    {
        Vector start = new(bottomCenter.X + xOffset, bottomCenter.Y + yOffset, bottomCenter.Z + 2f);
        SurfaceTraceResult tr = world.TraceLineSurface(start, start - new Vector(0f, 0f, GroundClearance),
                                                       TraceMask.Brushes, ignore);
        if (tr.Hit && tr.Normal.Z < SlopeNormalZ)
            return false;

        return tr.Hit;
    }

    /// <summary>The eye yaw the build angles turn from, plus the rotations (stock's :518, :526).</summary>
    private float BuildYaw(EntityHandle player, TFBaseObject blueprint)
        => entities.GetEyeAngles(player).Y + 90f * blueprint.DesiredBuildRotations;
}
