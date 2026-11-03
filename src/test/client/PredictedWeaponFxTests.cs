using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Sounds;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The predicting client's weapon effects, against stock's client: the
/// shooter's WeaponSound plays on the client's first prediction, FireBullets
/// traces each pellet from the server's seed and its UTIL_ImpactTrace
/// dispatches "Impact" here (decal, flecks, the surface's sound), a
/// bludgeon's ImpactEffect does the same with DMG_CLUB, and nothing plays
/// with cl_predictweapons 0 or on a replay of a command already predicted.
/// </summary>
public class PredictedWeaponFxTests
{
    private const int LocalPlayer = 1;
    private static readonly EntityHandle Player = new(0x0001_0000u | LocalPlayer);
    private static readonly Vector Eye = new(0f, 0f, 64f);
    private static readonly Vector Wall = new(128f, 0f, 64f);
    private const int Tile = 29;

    internal sealed class Sounds : IClientEntitySounds
    {
        public readonly List<(EntityHandle Entity, string Name)> Emitted = [];

        public void EmitFrom(EntityHandle entity, string soundName, Vector? origin = null)
            => Emitted.Add((entity, soundName));

        public void StopFrom(EntityHandle entity, string soundName) { }
    }

    internal sealed class Dispatcher : IClientEffectDispatch
    {
        public readonly List<(string Name, EffectData Data)> Ran = [];

        public void Dispatch(string name, in EffectData data) => Ran.Add((name, data));
    }

    private readonly Sounds _sounds = new();
    private readonly Dispatcher _dispatch = new();
    private readonly RecordingEngine _world = Fake();
    private readonly PredictWeaponsConVar _predict = new();

    public PredictedWeaponFxTests()
    {
        _predict.NotifyChanged("1", "0");
        _world.Returns("WorldQuery.ClientTraceLine",
                       new ClientTraceResult(true, 0.1f, Wall, new Vector(-1f, 0f, 0f), 0, 0, Tile, 0));
        _world.Returns("WorldQuery.ClientClipRayToEntities",
                       new ClientTraceResult(false, 1f, Wall, Vector.Zero, 0, -1, -1, 0));
        // Dry land: where a round stops is not water.
        _world.Returns("WorldQuery.PointContents", Contents.Empty);
    }

    // The same fake answers the render, the entities and the particle seam the
    // muzzle flash and the brass ask for; the facts that do not start anything
    // never set them.
    private PredictedWeaponFx Fx() => new(_sounds, _dispatch, _world, _world, _world, _world,
                                          AmmoCatalog.Stock(), _predict, _view);

    private sealed class FakeView : SourceSharp.Client.Input.IClientInputEngine
    {
        public SourceSharp.Client.Input.InputAngles Angles = new(10f, 90f, 0f);
        public int Sets;

        public SourceSharp.Client.Input.InputAngles GetViewAngles() => Angles;

        public void SetViewAngles(SourceSharp.Client.Input.InputAngles angles)
        {
            Angles = angles;
            Sets++;
        }

        public void GetScreenSize(out int width, out int height) => (width, height) = (0, 0);
        public bool IsPaused => false;
        public void GetMouseDelta(out int x, out int y, bool ignoreNextMouseDelta) => (x, y) = (0, 0);
        public void ClientCmdUnrestricted(string command) { }
        public void ConsolePrintAt(int position, string text) { }
    }

    private readonly FakeView _view = new();

    /// <summary>weapon_357.cpp's "disorient the player": up to a degree each way, whole degrees, no roll.</summary>
    [Fact]
    public void Disorienting_knocks_the_view_up_to_a_degree_each_way()
    {
        var fx = Fx();
        var seen = new HashSet<float>();
        for (int i = 0; i < 200; i++)
        {
            _view.Angles = new SourceSharp.Client.Input.InputAngles(10f, 90f, 0f);
            fx.DisorientView();
            Assert.InRange(_view.Angles.Pitch, 9f, 11f);
            Assert.InRange(_view.Angles.Yaw, 89f, 91f);
            Assert.Equal(0f, _view.Angles.Roll);
            seen.Add(_view.Angles.Pitch);
        }
        Assert.Equal(200, _view.Sets);
        Assert.Equal(3, seen.Count);
    }

    private static PredictedShot Shot(int seed = 1234) => new(Player, LocalPlayer, Eye, Vector.Zero, seed);

    private static WeaponType Row(string name) => WeaponCatalog.Stock().For(name)!.Value;

    [Fact]
    public void The_shot_sound_plays_from_the_shooter()
    {
        Fx().Sound(Shot(), "Weapon_Pistol.Single");

        Assert.Equal((Player, "Weapon_Pistol.Single"), Assert.Single(_sounds.Emitted));
    }

    [Fact]
    public void Cl_predictweapons_0_plays_nothing_and_fires_nothing()
    {
        _predict.NotifyChanged("0", "1");
        var fx = Fx();

        fx.Sound(Shot(), "Weapon_Pistol.Single");
        fx.FireBullets(Shot(), Row(EntityNames.WeaponPistol), 1, 1f);
        fx.MeleeSwing(Shot(), Row(EntityNames.WeaponCrowbar));

        Assert.Empty(_sounds.Emitted);
        Assert.Empty(_dispatch.Ran);
        Assert.False(_world.Called("WorldQuery.ClientTraceLine"));
    }

    [Fact]
    public void Each_pellet_that_lands_dispatches_the_impact_with_the_traced_surface()
    {
        Fx().FireBullets(Shot(), Row(EntityNames.WeaponPistol), 3, 2f);

        Assert.Equal(3, _dispatch.Ran.Count);
        var (name, data) = _dispatch.Ran[0];
        Assert.Equal("Impact", name);
        Assert.Equal(Wall, data.Origin);
        Assert.Equal(Tile, data.SurfaceProp);
        Assert.Equal((int)AmmoCatalog.Stock().For(Row(EntityNames.WeaponPistol).AmmoName)!.Value.DamageType,
                     data.DamageType);
        Assert.True(data.Start.X < data.Origin.X, "the impact starts on the near side of the face it marks");
    }

    /// <summary>
    /// HandleShotImpactingWater: a round that ends under water splashes where
    /// it went in, the ammo's size, and marks nothing below. Found against
    /// stock (2026-10-25): this mod's rounds marked a pool's floor and never
    /// splashed.
    /// </summary>
    private List<(string Name, EffectData Data)> FiredInto(Contents liquid)
    {
        var surface = new Vector(48f, 0f, 40f);
        _world.Returns("WorldQuery.PointContents", liquid)
              .ReturnsEachCall("WorldQuery.ClientTraceLine",
                               () => _world.Last("WorldQuery.ClientTraceLine")!.Value.ArgAs<TraceMask>(2) == TraceMask.BrushesAndWater
                                   ? new ClientTraceResult(true, 0.05f, surface, new Vector(0f, 0f, 1f), 0, 0, 0, 0)
                                   : new ClientTraceResult(true, 0.1f, Wall, new Vector(-1f, 0f, 0f), 0, 0, Tile, 0));

        Fx().FireBullets(Shot(), Row(EntityNames.WeaponPistol), 1, 0f);
        return _dispatch.Ran;
    }

    [Fact]
    public void A_round_into_water_splashes_where_it_went_in_and_marks_nothing_below()
    {
        var (name, data) = Assert.Single(FiredInto(Contents.Water));

        Assert.Equal("gunshotsplash", name);
        Assert.Equal((new Vector(48f, 0f, 40f), 0), (data.Origin, data.Flags));
        Assert.InRange(data.Scale, 4f, 8f);
    }

    [Fact]
    public void A_round_into_slime_splashes_as_slime()
        => Assert.Equal(1, Assert.Single(FiredInto(Contents.Slime)).Data.Flags);

    [Fact]
    public void The_pellets_trace_from_the_eye_past_the_shooter_along_the_servers_spread()
    {
        const int seed = 0x5A5A;
        Fx().FireBullets(Shot(seed), Row(EntityNames.WeaponPistol), 2, 3f);

        var traces = _world.Calls.Where(c => c.Member == "WorldQuery.ClientTraceLine").ToList();
        Assert.Equal(2, traces.Count);

        var aim = Angles.Vectors(Vector.Zero);
        var stream = new SeededUniformStream();
        for (int i = 0; i < 2; i++)
        {
            Vector dir = ShotSpread.Apply(aim, ShotSpread.SpreadOf(3f), ShotSpread.FirstSeed(seed) + i, stream).Normalized;
            Assert.Equal(Eye, traces[i].ArgAs<Vector>(0));
            Assert.Equal(Eye + dir * PredictedWeaponFx.MaxTrace, traces[i].ArgAs<Vector>(1));
            Assert.Equal(TraceMask.Shot, traces[i].ArgAs<TraceMask>(2));
            Assert.Equal((TraceFilter.Simple, LocalPlayer), (traces[i].ArgAs<TraceFilter>(3), traces[i].ArgAs<int>(4)));
        }

        Assert.NotEqual(traces[0].ArgAs<Vector>(1), traces[1].ArgAs<Vector>(1));
    }

    [Fact]
    public void A_round_into_nothing_or_the_sky_marks_nothing()
    {
        _world.Returns("WorldQuery.ClientTraceLine",
                       new ClientTraceResult(false, 1f, Wall, Vector.Zero, 0, -1, 0, 0));
        Fx().FireBullets(Shot(), Row(EntityNames.WeaponPistol), 1, 1f);

        _world.Returns("WorldQuery.ClientTraceLine",
                       new ClientTraceResult(true, 0.5f, Wall, Vector.Zero, 0, 0, Tile, PredictedWeaponFx.SurfSky));
        Fx().FireBullets(Shot(), Row(EntityNames.WeaponPistol), 1, 1f);

        Assert.Equal(2, _world.Calls.Count(c => c.Member == "WorldQuery.ClientTraceLine"));
        Assert.Empty(_dispatch.Ran);
    }

    [Fact]
    public void A_swing_that_lands_marks_it_as_a_club()
    {
        Fx().MeleeSwing(Shot(), Row(EntityNames.WeaponCrowbar));

        var (name, data) = Assert.Single(_dispatch.Ran);
        Assert.Equal(("Impact", (int)DamageType.Club, Wall), (name, data.DamageType, data.Origin));
        var trace = _world.Calls.Single(c => c.Member == "WorldQuery.ClientTraceLine");
        Assert.Equal(Eye + new Vector(Row(EntityNames.WeaponCrowbar).MeleeRange, 0f, 0f), trace.ArgAs<Vector>(1));
    }

    [Fact]
    public void A_round_an_entity_stops_before_the_wall_marks_the_entity()
    {
        var crate = new Vector(64f, 0f, 64f);
        const int wood = 12;
        _world.Returns("WorldQuery.ClientClipRayToEntities",
                       new ClientTraceResult(true, 0.5f, crate, new Vector(-1f, 0f, 0f), 0, 7, wood, 0));

        Fx().FireBullets(Shot(), Row(EntityNames.WeaponPistol), 1, 0f);

        var clip = _world.Calls.Single(c => c.Member == "WorldQuery.ClientClipRayToEntities");
        Assert.Equal(Eye, clip.ArgAs<Vector>(0));
        Assert.Equal(Wall, clip.ArgAs<Vector>(1));
        Assert.Equal(TraceMask.Shot, clip.ArgAs<TraceMask>(2));
        int[] named = clip.ArgAs<int[]>(3);
        Assert.Contains(7, named);
        Assert.DoesNotContain(LocalPlayer, named);
        Assert.Equal(EngineLimits.MaxEdicts - 2, named.Length);

        var (_, data) = Assert.Single(_dispatch.Ran);
        Assert.Equal((crate, 7, wood), (data.Origin, data.EntIndex, data.SurfaceProp));
    }

    [Fact]
    public void A_round_no_entity_stops_marks_the_world()
    {
        _world.Returns("WorldQuery.ClientClipRayToEntities",
                       new ClientTraceResult(false, 1f, Wall, Vector.Zero, 0, -1, -1, 0));

        Fx().FireBullets(Shot(), Row(EntityNames.WeaponPistol), 1, 0f);

        Assert.True(_world.Called("WorldQuery.ClientClipRayToEntities"), "the entities were asked (the positive arm)");
        var (_, data) = Assert.Single(_dispatch.Ran);
        Assert.Equal((Wall, 0, Tile), (data.Origin, data.EntIndex, data.SurfaceProp));
    }

    //-------------------------------------------------------------------------
    // The gun's flash and brass: stock's weapon-side predicted arms, handed
    // to this client's effect registry.
    //-------------------------------------------------------------------------

    private static readonly EntityHandle ViewModel = new(0x0100_0042u);

    [Fact]
    public void The_muzzle_flash_starts_the_script_particle_at_the_bearers_muzzle()
    {
        // CreateMuzzleFlashEffects' particle branch, DispatchMuzzleFlash's
        // PATTACH_POINT_FOLLOW: the named system on the bearer, its first
        // control point chained to the bearer's "muzzle" attachment.
        _world.Returns("EntityRender.LookupAttachment", 3).Returns("Effects.ParticleSystemStart", true)
              .Returns("Effects.ParticleSystemFollowEntity", true);

        Fx().MuzzleFlash(ViewModel, "muzzle_scattergun");

        var start = _world.Calls.Single(c => c.Member == "Effects.ParticleSystemStart");
        Assert.Equal((ViewModel, "muzzle_scattergun"), (start.ArgAs<EntityHandle>(0), start.ArgAs<string>(1)));
        var follow = _world.Calls.Single(c => c.Member == "Effects.ParticleSystemFollowEntity");
        Assert.Equal((ViewModel, 0, ViewModel, 3),
                     (follow.ArgAs<EntityHandle>(0), follow.ArgAs<int>(1),
                      follow.ArgAs<EntityHandle>(2), follow.ArgAs<int>(3)));
    }

    [Fact]
    public void A_flash_the_particle_seam_refuses_follows_nothing()
    {
        _world.Returns("EntityRender.LookupAttachment", 3).Returns("Effects.ParticleSystemStart", false);

        Fx().MuzzleFlash(ViewModel, "muzzle_scattergun");

        Assert.DoesNotContain(_world.Calls, c => c.Member == "Effects.ParticleSystemFollowEntity");
    }

    [Fact]
    public void A_script_with_no_flash_particle_or_a_bearer_with_no_muzzle_starts_nothing()
    {
        _world.Returns("EntityRender.LookupAttachment", 3).Returns("Effects.ParticleSystemStart", false);
        Fx().MuzzleFlash(ViewModel, "");

        _world.Returns("EntityRender.LookupAttachment", 0);
        Fx().MuzzleFlash(ViewModel, "muzzle_scattergun");

        Assert.DoesNotContain(_world.Calls, c => c.Member == "Effects.ParticleSystemStart");
        // The empty name asks nothing of the model at all.
        Assert.Equal(1, _world.Calls.Count(c => c.Member == "EntityRender.LookupAttachment"));
    }

    [Fact]
    public void The_brass_dispatches_TF_EjectBrass_carrying_the_item_and_the_gun()
    {
        // OnFireEvent's brass: the "eject_brass" attachment's place, and the
        // effect's data carries the item's defindex and the weapon's id.
        var origin = new Vector(1f, 2f, 64f);
        var angles = new Vector(2f, 180f, 0f);
        _world.Returns("EntityRender.LookupAttachment", 5)
              .Returns("EntityRender.GetAttachment", true)
              .Returns("EntityRender.GetAttachment.origin", origin)
              .Returns("EntityRender.GetAttachment.angles", angles);

        Fx().ShellEject(ViewModel, 149, 3);

        var (name, data) = Assert.Single(_dispatch.Ran);
        Assert.Equal(("TF_EjectBrass", 149, 3), (name, data.DamageType, data.HitBox));
        Assert.Equal((origin, angles), (data.Origin, data.Angles));
        var lookup = _world.Calls.Single(c => c.Member == "EntityRender.LookupAttachment");
        Assert.Equal("eject_brass", lookup.ArgAs<string>(1));
    }

    [Fact]
    public void Hands_with_no_ejector_eject_nothing()
    {
        _world.Returns("EntityRender.LookupAttachment", 0)
              .Returns("EntityRender.GetAttachment", true);

        Fx().ShellEject(ViewModel, 149, 3);

        Assert.Empty(_dispatch.Ran);
    }

    [Fact]
    public void Flash_and_brass_are_quiet_when_prediction_is_off()
    {
        _predict.NotifyChanged("0", "1");
        _world.Returns("Entities.GetIndex", 66)
              .Returns("EntityRender.LookupAttachment", 3)
              .Returns("EntityRender.GetAttachment", true);

        Fx().MuzzleFlash(ViewModel, "muzzle_flash");
        Fx().ShellEject(ViewModel, 149, 3);

        Assert.Empty(_dispatch.Ran);
        Assert.False(_world.Called("EntityRender.LookupAttachment"),
                     "the arms asked nothing of the hands either");
    }

    //-------------------------------------------------------------------------
    // Through the replay: the first prediction only.
    //-------------------------------------------------------------------------

    internal sealed class RecordingFx : IPredictedWeaponFx
    {
        public readonly List<string> Played = [];

        public void Sound(in PredictedShot shot, string soundScript) => Played.Add("sound " + soundScript);

        public Vector LastAim;

        public void FireBullets(in PredictedShot shot, in WeaponType type, int pellets, float spreadDegrees)
        {
            LastAim = shot.ViewAngles;
            Played.Add($"bullets {pellets} from {shot.Eye} seed {shot.CommandRandomSeed}");
        }

        public void MeleeSwing(in PredictedShot shot, in WeaponType type) => Played.Add("swing");
    }

    private sealed class Pair : IClientStandalones
    {
        public readonly ClientEntityDirectory Directory = new();

        public IReadOnlyList<ClientStandaloneEntity> Live() => throw new InvalidOperationException();

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => Directory.ByIndex(entityIndex);

        public IEnumerable<ClientStandaloneEntity> OfClass(string? className) => Directory.OfClass(className);

        public IEnumerable<ClientStandaloneEntity> OfName(string? name) => Directory.OfName(name);
    }

    private static (ClientArsenal Arsenal, RecordingFx Fx) ArmedReplay(string weaponName = EntityNames.WeaponPistol,
                                                                      RecordingEngine? world = null,
                                                                      Action<ClientEntityDirectory>? populate = null)
    {
        var engine = world ?? Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);
        engine.Returns("EntityRender.LookupSequenceByActivity", 0);

        var weapon = new ClientSsWeapon(engine) { Handle = new EntityHandle(0x0100_0041u) };
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = LocalPlayer;
        weapon.State.WeaponTypeId = WeaponCatalog.Stock().IndexOf(weaponName);
        weapon.State.Clip = 12;

        var viewModel = new ClientSsViewModel(engine, engine) { Handle = new EntityHandle(0x0100_0042u) };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = LocalPlayer;

        var pair = new Pair();
        pair.Directory.Bind(weapon.Handle, -1, weapon);
        pair.Directory.Bind(viewModel.Handle, -1, viewModel);
        populate?.Invoke(pair.Directory);

        var fx = new RecordingFx();
        var arsenal = new ClientArsenal(new RecordingLog(), pair, engine, engine, engine,
                                        NetStateCatalog.Of(ClientGame, new RecordingLog()), world: engine, fx: fx);

        var basis = new UserCmd { CommandNumber = 10 };
        arsenal.OnPlayerCommand(Player, in basis, 2, false);
        return (arsenal, fx);
    }

    //-------------------------------------------------------------------------
    // The predicted swing's aim and the SLAM's probe: the world's trace, and
    // the entities the client's traces cannot see (ClientClipRayToEntities,
    // ClientTraceHullEntities), as the server's traces stop.
    //-------------------------------------------------------------------------

    private static readonly ClientTraceResult LineMiss = new(false, 1f, Vector.Zero, Vector.Zero, 0, -1, -1, 0);

    /// <summary>A crowbar's replay, aimed along +x from <see cref="Eye"/> (or <paramref name="eye"/>).</summary>
    private ClientArsenal AimedSwing(Vector? eye = null, Action<ClientEntityDirectory>? populate = null)
    {
        _world.Returns("WorldQuery.ClientTraceLine", LineMiss);
        _world.Returns("WorldQuery.ClientClipRayToEntities", LineMiss);
        var (arsenal, _) = ArmedReplay(EntityNames.WeaponCrowbar, _world, populate);
        var aim = new UserCmd { CommandNumber = 11 };
        arsenal.OnPlayerCommand(Player, in aim, 1, true, 10f, eye ?? Eye);
        _world.Clear();
        return arsenal;
    }

    /// <summary>GetAutoaimVector on the predicting client, as the server's round: eyes plus punch.</summary>
    [Fact]
    public void A_predicted_round_goes_where_the_punched_view_points()
    {
        var (arsenal, fx) = ArmedReplay(EntityNames.WeaponSmg1);
        var working = new PlayerNetState();
        working.BindLocal();
        working.PunchAngle = new QAngle(-3f, 1f, 0f);
        arsenal.WorkingPlayer = working;

        var shot = new UserCmd { CommandNumber = 11, Pitch = 10f, Yaw = 90f, Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(Player, in shot, 1, true, 10f, Eye);

        Assert.Contains(fx.Played, p => p.StartsWith("bullets", StringComparison.Ordinal));
        Assert.Equal(new Vector(7f, 91f, 0f), fx.LastAim);
    }

    private void HullStops(int entityIndex, Vector origin)
        => _world.Returns("WorldQuery.ClientTraceHullEntities",
                          new ClientBoxTrace(true, 0.5f, Eye + new Vector(30f, 0f, 0f), entityIndex, origin));

    [Fact]
    public void A_swing_whose_ray_an_entity_stops_connects_without_a_hull()
    {
        var arsenal = AimedSwing();
        _world.Returns("WorldQuery.ClientClipRayToEntities",
                       new ClientTraceResult(true, 0.5f, new Vector(37f, 0f, 64f), Vector.Zero, 0, 2, 0, 0));

        Assert.True(arsenal.MeleeConnects(75f));

        var clip = _world.Calls.Single(c => c.Member == "WorldQuery.ClientClipRayToEntities");
        Assert.Equal((Eye, Eye + new Vector(75f, 0f, 0f), TraceMask.ShotHull),
                     (clip.ArgAs<Vector>(0), clip.ArgAs<Vector>(1), clip.ArgAs<TraceMask>(2)));
        Assert.DoesNotContain(LocalPlayer, clip.ArgAs<int[]>(3));
        Assert.False(_world.Called("WorldQuery.ClientTraceHullEntities"), "the ray's stop needs no hull");
    }

    [Fact]
    public void A_swing_whose_ray_meets_nothing_sweeps_the_bludgeon_hull_backed_off_by_its_radius()
    {
        var arsenal = AimedSwing();
        _world.Returns("WorldQuery.ClientTraceHullEntities", new ClientBoxTrace(false, 1f, Vector.Zero, -1, Vector.Zero));

        Assert.False(arsenal.MeleeConnects(75f));

        var hull = _world.Calls.Single(c => c.Member == "WorldQuery.ClientTraceHullEntities");
        Assert.Equal(Eye, hull.ArgAs<Vector>(0));
        Assert.Equal(Eye + new Vector(75f - 1.732f * 16f, 0f, 0f), hull.ArgAs<Vector>(1));
        Assert.Equal((new Vector(-16f, -16f, -16f), new Vector(16f, 16f, 16f)), (hull.ArgAs<Vector>(2), hull.ArgAs<Vector>(3)));
        Assert.Equal((TraceMask.ShotHull, LocalPlayer), (hull.ArgAs<TraceMask>(4), hull.ArgAs<int>(5)));
        Assert.DoesNotContain(LocalPlayer, hull.ArgAs<int[]>(6));
    }

    [Fact]
    public void A_hull_stop_on_an_entity_ahead_connects()
    {
        var arsenal = AimedSwing();
        HullStops(2, new Vector(60f, 10f, 64f));

        Assert.True(arsenal.MeleeConnects(75f));
    }

    [Fact]
    public void A_hull_stop_on_an_entity_off_to_the_side_misses()
    {
        var arsenal = AimedSwing();
        // 60 units aside, 10 ahead: far outside the 0.70721 facing test.
        HullStops(2, new Vector(10f, 60f, 64f));

        Assert.False(arsenal.MeleeConnects(75f));
    }

    [Fact]
    public void A_hull_stop_on_the_world_faces_the_maps_origin()
    {
        // The world's origin is the map's: ahead of an eye 200 units behind it...
        var behind = AimedSwing(new Vector(-200f, 0f, 64f));
        HullStops(0, new Vector(999f, 999f, 999f));
        Assert.True(behind.MeleeConnects(75f));

        // ...and below one standing over it, whatever the trace answered.
        var over = AimedSwing();
        HullStops(0, new Vector(60f, 0f, 64f));
        Assert.False(over.MeleeConnects(75f));
    }

    [Fact]
    public void The_slam_probe_sees_a_player_the_world_trace_cannot()
    {
        const int other = 2;
        var arsenal = AimedSwing(populate: d =>
        {
            var them = new ClientSsPlayer(_world) { Handle = new EntityHandle(0x0001_0000u | other) };
            d.Bind(them.Handle, other, them);
        });
        _world.Returns("WorldQuery.ClientClipRayToEntities",
                       new ClientTraceResult(true, 0.3f, new Vector(12f, 0f, 64f), Vector.Zero, 0, other, 0, 0));

        Assert.Equal(WallProbe.Character, arsenal.ProbeWall(42f));

        var clip = _world.Calls.Single(c => c.Member == "WorldQuery.ClientClipRayToEntities");
        Assert.Equal(TraceMask.Solid, clip.ArgAs<TraceMask>(2));
    }

    [Fact]
    public void The_local_guns_draw_state_is_the_replays_once_it_has_run_on_the_latest_decode()
    {
        ClientSsWeapon? mine = null;
        var (arsenal, _) = ArmedReplay(populate: d => mine = d.OfClass(EntityNames.SsWeapon).OfType<ClientSsWeapon>().Single());

        Assert.Same(arsenal.WorkingWeapon, arsenal.PredictedStateOf(mine!));

        // A decode the replay has not yet reset to: the copy is behind it.
        mine!.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
        Assert.Null(arsenal.PredictedStateOf(mine!));
    }

    [Fact]
    public void Somebody_elses_gun_has_no_predicted_draw_state()
    {
        var other = new ClientSsWeapon(Fake()) { Handle = new EntityHandle(0x0100_0051u) };
        other.State.BindLocal();
        other.State.OwnerIndex = LocalPlayer + 1;
        var (arsenal, _) = ArmedReplay(populate: d => d.Bind(other.Handle, -1, other));

        Assert.Null(arsenal.PredictedStateOf(other));
    }

    [Fact]
    public void The_first_prediction_of_a_shot_plays_it_with_the_commands_eye_and_seed()
    {
        var (arsenal, fx) = ArmedReplay();

        var cmd = new UserCmd { CommandNumber = 11, Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(Player, in cmd, 1, true, 10f, Eye);

        Assert.Equal(["sound Weapon_Pistol.Single", $"bullets 1 from {Eye} seed {UserCmdCodec.RandomSeedFor(11)}"],
                     fx.Played);
    }

    [Fact]
    public void A_replay_of_a_command_already_predicted_plays_nothing()
    {
        var (arsenal, fx) = ArmedReplay();

        var cmd = new UserCmd { CommandNumber = 11, Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(Player, in cmd, 1, false, 10f, Eye);

        Assert.Empty(fx.Played);
    }

    [Fact]
    public void A_replay_told_no_eye_fires_no_shot()
    {
        var (arsenal, fx) = ArmedReplay();

        var cmd = new UserCmd { CommandNumber = 11, Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(Player, in cmd, 1, true, 10f);

        Assert.Equal(["sound Weapon_Pistol.Single"], fx.Played);
    }

    /// <summary>
    /// The predicted swing's probe looks from the eye the prediction ran the
    /// command to, as the server's run of it does (Weapon_ShootPosition), not
    /// from the last decoded origin. Found against stock (docs/divergences.md,
    /// the SLAM's wall probe, 2026-10-25).
    /// </summary>
    [Fact]
    public void A_swings_probe_looks_from_the_commands_own_eye()
    {
        var engine = Fake();
        engine.Returns("WorldQuery.ClientTraceLine",
                       new ClientTraceResult(false, 1f, Vector.Zero, Vector.Zero, 0, -1, -1, 0));
        engine.Returns("WorldQuery.ClientClipRayToEntities",
                       new ClientTraceResult(false, 1f, Vector.Zero, Vector.Zero, 0, -1, -1, 0));
        engine.Returns("WorldQuery.ClientTraceHullEntities", new ClientBoxTrace(false, 1f, Vector.Zero, -1, Vector.Zero));
        var (arsenal, _) = ArmedReplay(EntityNames.WeaponCrowbar, engine);

        var cmd = new UserCmd { CommandNumber = 11, Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(Player, in cmd, 1, false, 10f, Eye);

        EngineCall? probe = engine.Last("WorldQuery.ClientTraceLine");
        Assert.True(probe is not null, "the swing made no probe");
        Assert.Equal(Eye, probe!.Value.ArgAs<Vector>(0));
        EngineCall? hull = engine.Last("WorldQuery.ClientTraceHullEntities");
        Assert.True(hull is not null, "the swing swept no hull");
        Assert.Equal(Eye, hull!.Value.ArgAs<Vector>(0));
    }
}
