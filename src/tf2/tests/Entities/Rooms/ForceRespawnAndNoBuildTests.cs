using SourceSharp.Abi;
using TF2.Server.Buildings;
using TF2.Server.Entities.Rooms;
using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Tests.Buildings;
using TF2.Tests.Entities.Items;

using Xunit;

namespace TF2.Tests.Entities.Rooms;

/// <summary>The machine's players as a fact sets them, recording each respawn and move.</summary>
internal sealed class FakeRoster(FakeMapPlayers players) : ITeamRoundRoster
{
    public List<EntityHandle> Respawned { get; } = new();

    /// <summary>How many removals had happened when the first respawn came — to see the wipe's order.</summary>
    public Func<int>? RemoveCount { get; set; }
    public int RemoveCountAtFirstRespawn { get; private set; } = -1;

    public IReadOnlyList<EntityHandle> Players(TFTeam team)
        => players.All.Where(p => p.Value.Team == (int)team).Select(p => p.Key).ToList();

    public TFTeam TeamOf(EntityHandle player) => (TFTeam)players.All[player].Team;
    public bool IsAlive(EntityHandle player) => players.All[player].Alive;
    public float DeathTime(EntityHandle player) => -1f;
    public bool IsReadyToPlay(EntityHandle player) => true;

    public void ForceRespawn(EntityHandle player)
    {
        if (RemoveCountAtFirstRespawn < 0 && RemoveCount is { } count)
            RemoveCountAtFirstRespawn = count();
        Respawned.Add(player);
        players.All[player].Alive = true;
    }

    public bool ChangeTeam(EntityHandle player, TFTeam team)
    {
        players.All[player].Team = (int)team;
        return true;
    }

    public string Name(EntityHandle player) => string.Empty;
    public int EntIndex(EntityHandle player) => (int)player.EntryIndex;
}

/// <summary>The buildings directory, as the wipe asks it: the standing ones, and how often it was asked.</summary>
internal sealed class FakeBuildings : ITFBuildings
{
    public List<TFBaseObject> Standing { get; } = new();
    public int Queries { get; private set; }

    public IEnumerable<TFBaseObject> OwnedBy(EntityHandle? builder = null, TFObjectType? type = null, int? mode = null)
    {
        Queries++;
        return Standing;
    }
}

/// <summary>
/// game_forcerespawn (<c>CTFForceRespawn::ForceRespawn</c>, entity_forcerespawn.cpp)
/// and func_nobuild (<c>PointInNoBuild</c>, <c>PreventsBuildOf</c>,
/// func_no_build.cpp), stage 2.4.
/// </summary>
public sealed class ForceRespawnAndNoBuildTests
{
    private static (MapItemRig Rig, FakeRoster Roster, EntityForceRespawn Entity, TFPlayerCarriers Carriers,
                     FakeBuildings Buildings) ForceRespawn()
    {
        var rig = new MapItemRig();
        var roster = new FakeRoster(rig.Players);
        var carriers = new TFPlayerCarriers();
        var buildings = new FakeBuildings();
        rig.Services.AddInstance<ITeamRoundRoster>(roster);
        rig.Services.AddInstance(carriers);
        rig.Services.AddInstance<ITFBuildings>(buildings);
        rig.Engine.Returns("Entities.FindAllByClassname", Array.Empty<EntityHandle>());
        roster.RemoveCount = () => rig.Engine.CountOf("Entities.Remove");
        return (rig, roster, rig.Spawn<EntityForceRespawn>(EntityForceRespawn.MapClass), carriers, buildings);
    }

    /// <summary>The player has a class part, as every live TF player does.</summary>
    private static TFPlayerClass With(TFPlayerCarriers carriers, EntityHandle player)
    {
        var cls = new TFPlayerClass(carriers, new TFPlayerItems());
        cls.Attach(player);
        return cls;
    }

    /// <summary>Each named entity class answers its own handles to FindAllByClassname.</summary>
    private static void AnswersClassesWith(MapItemRig rig, Dictionary<string, EntityHandle[]> byClass)
        => rig.Engine.ReturnsEachCall("Entities.FindAllByClassname",
                                       () => byClass.GetValueOrDefault((string)rig.Engine.Calls[^1].Arg(0)!,
                                                                       Array.Empty<EntityHandle>()));

    [Fact]
    public void Force_respawn_respawns_every_player_with_a_class_on_a_playing_team()
    {
        var (rig, roster, entity, carriers, _) = ForceRespawn();
        var (red, _) = rig.Players.Add(TFTeam.Red);
        var (blue, _) = rig.Players.Add(TFTeam.Blue);
        var (classless, _) = rig.Players.Add(TFTeam.Blue, TFClass.Undefined);
        var (spectator, _) = rig.Players.Add(TFTeam.Spectator);
        foreach (var player in new[] { red, blue, classless, spectator })
            With(carriers, player);

        rig.Input(entity, "ForceRespawn");

        Assert.Equal([red, blue], roster.Respawned);
        Assert.DoesNotContain(classless, roster.Respawned);
        Assert.DoesNotContain(spectator, roster.Respawned);
        Assert.Equal(1, entity.OnForceRespawn.FireCount);
    }

    [Fact]
    public void Force_team_respawn_respawns_only_that_teams_dead()
    {
        var (rig, roster, entity, carriers, _) = ForceRespawn();
        var (deadBlue, db) = rig.Players.Add(TFTeam.Blue);
        var (liveBlue, _) = rig.Players.Add(TFTeam.Blue);
        var (deadRed, dr) = rig.Players.Add(TFTeam.Red);
        foreach (var player in new[] { deadBlue, liveBlue, deadRed })
            With(carriers, player);
        db.Alive = false;
        dr.Alive = false;

        rig.Registry.Input(entity.Handle, "ForceTeamRespawn", "3", 3f, EntityHandle.None);

        Assert.Equal([deadBlue], roster.Respawned);
        Assert.DoesNotContain(liveBlue, roster.Respawned);
    }

    [Fact]
    public void Switch_teams_swaps_the_playing_teams_then_respawns_them()
    {
        var (rig, roster, entity, carriers, _) = ForceRespawn();
        var (red, r) = rig.Players.Add(TFTeam.Red);
        var (blue, b) = rig.Players.Add(TFTeam.Blue);
        foreach (var player in new[] { red, blue })
            With(carriers, player);

        rig.Input(entity, "ForceRespawnSwitchTeams");

        Assert.Equal((int)TFTeam.Blue, r.Team);
        Assert.Equal((int)TFTeam.Red, b.Team);
        Assert.Equal(2, roster.Respawned.Count);
    }

    [Fact]
    public void The_dropped_ammo_packs_are_removed()
    {
        var (rig, _, entity, _, _) = ForceRespawn();
        var pack = new EntityHandle(0x0060_0001u);
        AnswersClassesWith(rig, new() { [EntityForceRespawn.DroppedAmmoClass] = [pack] });

        entity.ForceRespawn(false);

        var removed = rig.Engine.AllOf("Entities.Remove").Select(c => (EntityHandle)c.Arg(0)!).ToList();
        Assert.Equal([pack], removed);
        Assert.Equal(EntityForceRespawn.DroppedAmmoClass, rig.Engine.Last("Entities.FindAllByClassname")!.Value.Arg(0));
    }

    [Fact]
    public void The_ammo_packs_go_even_on_a_teams_only_respawn()
    {
        // Stock: the pack removal sits outside the bRemoveEverything guard
        // (entity_forcerespawn.cpp:108-113).
        var (rig, roster, entity, carriers, buildings) = ForceRespawn();
        var (deadBlue, db) = rig.Players.Add(TFTeam.Blue);
        With(carriers, deadBlue);
        db.Alive = false;
        var pack = new EntityHandle(0x0060_0002u);
        AnswersClassesWith(rig, new()
        {
            [EntityForceRespawn.DroppedAmmoClass] = [pack],
            [EntityForceRespawn.ProjectileClasses[0]] = [new EntityHandle(0x0020_0001u)],
        });

        rig.Registry.Input(entity.Handle, "ForceTeamRespawn", "3", 3f, EntityHandle.None);

        Assert.Equal([deadBlue], roster.Respawned);
        var removed = rig.Engine.AllOf("Entities.Remove").Select(c => (EntityHandle)c.Arg(0)!).ToList();
        Assert.Equal([pack], removed); // no projectile went, though one was there to take
        Assert.Equal([EntityForceRespawn.DroppedAmmoClass],
                     rig.Engine.AllOf("Entities.FindAllByClassname").Select(c => (string)c.Arg(0)!));
        Assert.Equal(0, buildings.Queries);
    }

    [Fact]
    public void The_wipe_clears_projectiles_buildings_and_ammo_packs_before_the_first_respawn()
    {
        var (rig, roster, entity, carriers, fakeBuildings) = ForceRespawn();
        var (red, _) = rig.Players.Add(TFTeam.Red);
        With(carriers, red);

        var rocket = new EntityHandle(0x0020_0001u);
        var pipe = new EntityHandle(0x0020_0002u);
        var sticky = new EntityHandle(0x0020_0003u);
        var syringe = new EntityHandle(0x0020_0004u);
        var pack = new EntityHandle(0x0060_0003u);
        AnswersClassesWith(rig, new()
        {
            [EntityForceRespawn.ProjectileClasses[0]] = [rocket],
            [EntityForceRespawn.ProjectileClasses[1]] = [pipe],
            [EntityForceRespawn.ProjectileClasses[2]] = [sticky],
            [EntityForceRespawn.ProjectileClasses[3]] = [syringe],
            [EntityForceRespawn.DroppedAmmoClass] = [pack],
        });

        // A standing building from the buildings rig, destroyed by the wipe through the real DestroyObject.
        var buildingsRig = new BuildingsRig();
        var builder = buildingsRig.AddBuilder();
        var building = buildingsRig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(builder);
        fakeBuildings.Standing.Add(building);

        entity.ForceRespawn(false);

        var removed = rig.Engine.AllOf("Entities.Remove").Select(c => (EntityHandle)c.Arg(0)!).ToList();
        Assert.Equal([rocket, pipe, sticky, syringe, pack], removed);
        Assert.Equal(1, fakeBuildings.Queries);
        Assert.True(building.IsDead);
        // The four projectiles (and the building's fall) before anyone respawned;
        // the pack, which stock removes after the walk, has not gone yet.
        Assert.Equal(4, roster.RemoveCountAtFirstRespawn);
    }

    [Fact]
    public void Off_team_and_classless_players_are_granted_the_instant_spawn_instead()
    {
        var (rig, roster, entity, carriers, _) = ForceRespawn();
        var (red, _) = rig.Players.Add(TFTeam.Red);
        var (spectator, _) = rig.Players.Add(TFTeam.Spectator);
        var (unassigned, _) = rig.Players.Add(TFTeam.Unassigned);
        var (classless, _) = rig.Players.Add(TFTeam.Blue, TFClass.Undefined);
        var spectatorClass = With(carriers, spectator);
        var unassignedClass = With(carriers, unassigned);
        var classlessClass = With(carriers, classless);
        var redClass = With(carriers, red);

        rig.Input(entity, "ForceRespawn");

        Assert.Equal([red], roster.Respawned);
        Assert.True(spectatorClass.AllowInstantSpawn);
        Assert.True(unassignedClass.AllowInstantSpawn);
        Assert.True(classlessClass.AllowInstantSpawn);
        Assert.False(redClass.AllowInstantSpawn); // the respawned player gets no grant
    }

    private static (MapItemRig Rig, TFNoBuildAreas Areas, FuncNoBuild Area) NoBuild(params (string, string)[] keys)
    {
        var rig = new MapItemRig();
        rig.Services.AddInstance<ITFBuildings>(new FakeBuildings());
        rig.Engine.Returns("Entities.GetOrigin", new SourceSharp.Abi.Vector(0f, 0f, 0f))
                  .Returns("Entities.GetBounds", (new SourceSharp.Abi.Vector(-100f, -100f, 0f), new SourceSharp.Abi.Vector(100f, 100f, 100f)));
        var area = rig.Spawn<FuncNoBuild>(FuncNoBuild.MapClass, keys);
        return (rig, new TFNoBuildAreas(rig.Registry), area);
    }

    private static readonly SourceSharp.Abi.Vector Inside = new(10f, 10f, 10f);
    private static readonly SourceSharp.Abi.Vector Outside = new(500f, 10f, 10f);

    [Fact]
    public void A_no_build_area_refuses_every_building_inside_it()
    {
        var (_, areas, _) = NoBuild();

        Assert.True(areas.PointInNoBuild(Inside, TFObjectType.Sentrygun, (int)TFTeam.Red));
        Assert.True(areas.PointInNoBuild(Inside, TFObjectType.Teleporter, (int)TFTeam.Blue));
        Assert.False(areas.PointInNoBuild(Outside, TFObjectType.Sentrygun, (int)TFTeam.Red));
    }

    [Fact]
    public void An_allowed_kind_may_be_built()
    {
        var (_, areas, _) = NoBuild(("AllowSentry", "1"));

        Assert.False(areas.PointInNoBuild(Inside, TFObjectType.Sentrygun, (int)TFTeam.Red));
        Assert.True(areas.PointInNoBuild(Inside, TFObjectType.Dispenser, (int)TFTeam.Red));
    }

    [Fact]
    public void A_team_area_denies_only_its_team()
    {
        var (_, areas, _) = NoBuild(("TeamNum", "3"));

        Assert.True(areas.PointInNoBuild(Inside, TFObjectType.Sentrygun, (int)TFTeam.Blue));
        Assert.False(areas.PointInNoBuild(Inside, TFObjectType.Sentrygun, (int)TFTeam.Red));
    }

    [Fact]
    public void An_inactive_area_refuses_nothing_until_set_active()
    {
        var (rig, areas, area) = NoBuild(("StartDisabled", "1"));

        Assert.False(areas.PointInNoBuild(Inside, TFObjectType.Sentrygun, (int)TFTeam.Red));

        rig.Input(area, "SetActive");
        Assert.True(areas.PointInNoBuild(Inside, TFObjectType.Sentrygun, (int)TFTeam.Red));

        rig.Input(area, "ToggleActive");
        Assert.False(area.Active);
    }

    /// <summary>
    /// A <c>DestroyBuildings</c> area over the brush at the origin (its box
    /// <c>NoBuild</c stands), with the standing buildings' spots by handle:
    /// where its build origin and its centre of mass are. Everything else —
    /// the brush's own reads — answers the origin 0 the box is around.
    /// </summary>
    private static (MapItemRig Rig, FuncNoBuild Area, FakeBuildings Standing, BuildingsRig Buildings,
                     Dictionary<EntityHandle, (SourceSharp.Abi.Vector At, SourceSharp.Abi.Vector Centre)> Spots)
        DestroyArea(params (string Key, string Value)[] keys)
    {
        var rig = new MapItemRig();
        var standing = new FakeBuildings();
        rig.Services.AddInstance<ITFBuildings>(standing);
        var buildings = new BuildingsRig();
        var spots = new Dictionary<EntityHandle, (SourceSharp.Abi.Vector, SourceSharp.Abi.Vector)>();

        rig.Engine.Returns("Entities.GetBounds", (new SourceSharp.Abi.Vector(-100f, -100f, 0f), new SourceSharp.Abi.Vector(100f, 100f, 100f)))
                  .Returns("Entities.IsPlayer", true)
                  .Returns("GameEvents.EventCreate", new IntPtr(7))
                  .Returns("GameEvents.EventFire", true);
        rig.Engine.ReturnsEachCall("Entities.GetOrigin", () =>
            rig.Engine.Last("Entities.GetOrigin") is { } call
                && spots.TryGetValue(call.ArgAs<EntityHandle>(0), out (SourceSharp.Abi.Vector At, SourceSharp.Abi.Vector _) spot)
                ? spot.Item1 : SourceSharp.Abi.Vector.Zero);
        rig.Engine.ReturnsEachCall("Entities.GetWorldSpaceCenter", () =>
            rig.Engine.Last("Entities.GetWorldSpaceCenter") is { } call
                && spots.TryGetValue(call.ArgAs<EntityHandle>(0), out (SourceSharp.Abi.Vector _, SourceSharp.Abi.Vector Centre) spot)
                ? spot.Item2 : SourceSharp.Abi.Vector.Zero);
        rig.Engine.ReturnsEachCall("Players.GetUserId", () =>
            100 + (rig.Engine.Last("Players.GetUserId") ?? default).ArgAs<EntityHandle>(0).EntryIndex);

        var area = rig.Spawn<FuncNoBuild>(FuncNoBuild.MapClass, keys);
        return (rig, area, standing, buildings, spots);
    }

    /// <summary>A building placed by <paramref name="builder"/>, standing in the area's walk.</summary>
    private static TestBuilding Placed(BuildingsRig buildings, FakeBuildings standing, EntityHandle builder)
    {
        var building = buildings.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(builder);
        standing.Standing.Add(building);
        return building;
    }

    private static List<int> EventFields(MapItemRig rig, string field)
        => rig.Engine.Calls.Where(c => c.Member == "GameEvents.EventSetInt" && (string)c.Arg(1)! == field)
                           .Select(c => (int)c.Arg(2)!)
                           .ToList();

    [Fact]
    public void An_inside_building_is_removed_with_the_object_removed_event_and_detonated()
    {
        var (rig, area, standing, buildings, spots) = DestroyArea(("DestroyBuildings", "1"));
        var builder = buildings.AddBuilder();
        var byOrigin = Placed(buildings, standing, builder);
        var byCentre = Placed(buildings, standing, builder);
        spots[byOrigin.Handle] = (new SourceSharp.Abi.Vector(10f, 10f, 10f), new SourceSharp.Abi.Vector(10f, 10f, 10f));
        // The origin outside, the centre in: either standing spot is removal.
        spots[byCentre.Handle] = (new SourceSharp.Abi.Vector(500f, 10f, 10f), new SourceSharp.Abi.Vector(10f, 10f, 10f));

        area.SetActive(true);

        Assert.True(byOrigin.IsDead);
        Assert.True(byCentre.IsDead);
        Assert.Equal(2, rig.Engine.CountOf("GameEvents.EventFire"));
        Assert.Equal([100 + builder.EntryIndex, 100 + builder.EntryIndex], EventFields(rig, "userid"));
        Assert.All(EventFields(rig, "objecttype"), type => Assert.Equal((int)TFObjectType.Sentrygun, type));
        Assert.Equal(new[] { byOrigin.Handle.EntryIndex, byCentre.Handle.EntryIndex }, EventFields(rig, "index"));
    }

    [Fact]
    public void Another_teams_building_survives_a_team_area()
    {
        var (rig, area, standing, buildings, spots) = DestroyArea(("DestroyBuildings", "1"), ("TeamNum", "2"));
        var blueBuilder = buildings.AddBuilder(TFTeam.Blue);
        var blue = Placed(buildings, standing, blueBuilder);
        spots[blue.Handle] = (new SourceSharp.Abi.Vector(10f, 10f, 10f), new SourceSharp.Abi.Vector(10f, 10f, 10f));

        area.SetActive(true);

        Assert.False(blue.IsDead);
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventFire"));
    }

    [Fact]
    public void A_map_placed_building_survives_the_sweep()
    {
        var (rig, area, standing, buildings, spots) = DestroyArea(("DestroyBuildings", "1"));
        var mapPlaced = buildings.Spawn<TestBuilding>(TestBuilding.MapClass); // no placement: no builder, as stock's map-placed object
        standing.Standing.Add(mapPlaced);
        spots[mapPlaced.Handle] = (new SourceSharp.Abi.Vector(10f, 10f, 10f), new SourceSharp.Abi.Vector(10f, 10f, 10f));

        area.SetActive(true);

        Assert.False(mapPlaced.IsDead);
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventFire"));
    }

    [Fact]
    public void Without_the_key_activation_removes_nothing()
    {
        var (rig, area, standing, buildings, spots) = DestroyArea();
        var builder = buildings.AddBuilder();
        var sentry = Placed(buildings, standing, builder);
        spots[sentry.Handle] = (new SourceSharp.Abi.Vector(10f, 10f, 10f), new SourceSharp.Abi.Vector(10f, 10f, 10f));

        area.SetActive(true);

        Assert.False(sentry.IsDead);
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventCreate"));
    }

    [Fact]
    public void Deactivation_removes_nothing()
    {
        var (rig, area, standing, buildings, spots) = DestroyArea(("DestroyBuildings", "1"));
        var builder = buildings.AddBuilder();
        var sentry = Placed(buildings, standing, builder);
        spots[sentry.Handle] = (new SourceSharp.Abi.Vector(10f, 10f, 10f), new SourceSharp.Abi.Vector(10f, 10f, 10f));

        area.SetActive(false);

        Assert.False(sentry.IsDead);
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventCreate"));
    }
}
