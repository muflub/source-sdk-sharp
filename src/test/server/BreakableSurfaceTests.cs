using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// func_breakable_surf against stock's CBreakableSurface (func_breakablesurf.cpp),
/// a CBreakable. Glass with no health spawns at one and takes no damage, yet a
/// traced hit (TraceAttack) still counts against its health, says so, breaks
/// the sheet and takes out the pane it struck; later shots take out more,
/// crossing the broken sheet as a trigger. TakeDamage breaks it whole only for
/// a crush, a blast or a slash, and being killed does nothing. Found in a glass
/// A/B on ss_sandbox (2026-10-28): stock's pane went from health 1 to -8 and
/// lost two patches to three shots; ours stayed at 0 and lost one, every shot
/// after the first ignored.
/// </summary>
public class BreakableSurfaceTests
{
    /// <summary>A 192 x 120 pane facing y, as ss_sandbox's: 16 x 10 panes of 12.</summary>
    private static readonly (string, string)[] Pane =
    {
        ("lowerleft", "-416 -704 8"), ("lowerright", "-224 -704 8"),
        ("upperleft", "-416 -704 128"), ("upperright", "-224 -704 128"),
    };

    private sealed class Rig
    {
        public required FuncBreakableSurf Glass;
        public required RecordingEngine Engine;
        public required ScriptedEntityRegistry Registry;
        public int Health;

        public void Shoot(float x, float z, float damage = 8f, DamageType type = DamageType.Bullet)
            => Glass.TraceAttack(EntityHandle.None, damage, type, new Vector(x, -900f, z), new Vector(x, -704f, z));

        public int SolidPanes
        {
            get
            {
                int solid = 0;
                for (int w = 0; w < Glass.State.Wide; w++)
                    for (int h = 0; h < Glass.State.High; h++)
                        if (Glass.State.IsPaneSolid(w, h))
                            solid++;
                return solid;
            }
        }
    }

    private static Rig Spawned(params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncBreakableSurf).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;

        var handle = new EntityHandle(0x0007_0101u);
        foreach (var (key, value) in Pane.Concat(keys))
            registry.KeyValue(handle, EntityNames.FuncBreakableSurf, key, value);
        registry.Spawn(handle, EntityNames.FuncBreakableSurf);

        var rig = new Rig { Glass = (FuncBreakableSurf)registry.Get(handle)!, Engine = engine, Registry = registry };
        rig.Health = engine.Last("Entities.SetHealth")!.Value.ArgAs<int>(1);
        engine.ReturnsEachCall("Entities.GetHealth", () => rig.Health);
        return rig;
    }

    private static int LastHealthSet(RecordingEngine engine) => engine.Last("Entities.SetHealth")!.Value.ArgAs<int>(1);

    /// <summary>CBreakable::Spawn: "this allows people to shoot at the glass".</summary>
    [Fact]
    public void Glass_with_no_health_spawns_at_one_and_takes_no_damage()
    {
        var rig = Spawned();

        Assert.Equal(1, rig.Health);
        Assert.Equal(TakeDamageMode.No, rig.Engine.Last("Damage.SetTakeDamageMode")!.Value.ArgAs<TakeDamageMode>(1));
    }

    [Fact]
    public void A_shot_counts_against_its_health_and_breaks_the_sheet()
    {
        var rig = Spawned();

        rig.Shoot(-320f, 60f);

        Assert.Contains(rig.Engine.AllOf("Entities.SetHealth"), c => c.ArgAs<int>(1) == -7);
        Assert.Equal("-7", rig.Glass.OnHealthChanged.LastValue);
        Assert.Equal(1, rig.Glass.OnBreak.FireCount);
        Assert.True(rig.Glass.State.Broken);
        Assert.True(rig.SolidPanes < 160);
    }

    /// <summary>Every shot takes out panes, not just the one that broke the sheet.</summary>
    [Fact]
    public void A_later_shot_takes_out_more_panes()
    {
        var rig = Spawned();
        rig.Shoot(-400f, 20f);
        int after = rig.SolidPanes;

        rig.Shoot(-240f, 110f);

        Assert.True(rig.SolidPanes < after, $"{after} solid before the second shot, {rig.SolidPanes} after");
        Assert.Equal(1, rig.Glass.OnBreak.FireCount);
    }

    /// <summary>TraceAttackToTriggers: once broken it is a trigger a shot crosses within its panes.</summary>
    [Fact]
    public void A_broken_sheet_is_crossed_by_a_shot_through_its_panes()
    {
        var rig = Spawned();
        Assert.False(rig.Glass.ShotThrough(new Vector(-320f, -900f, 60f), new Vector(-320f, -500f, 60f), out _),
                     "a whole sheet is solid, not a trigger");

        rig.Shoot(-320f, 60f);

        Assert.True(rig.Glass.ShotThrough(new Vector(-300f, -900f, 40f), new Vector(-300f, -500f, 40f), out Vector hit));
        // Die moves the corners a unit towards where the blow came from (stock's m_vNormal step).
        Assert.InRange(hit.Y, -705f, -703f);
        Assert.False(rig.Glass.ShotThrough(new Vector(-100f, -900f, 40f), new Vector(-100f, -500f, 40f), out _),
                     "beside the pane");
        Assert.False(rig.Glass.ShotThrough(new Vector(-300f, -900f, 40f), new Vector(-300f, -800f, 40f), out _),
                     "short of it");
    }

    [Theory]
    [InlineData(DamageType.Bullet, false)]
    [InlineData(DamageType.Club, false)]
    [InlineData(DamageType.Crush, true)]
    [InlineData(DamageType.Blast, true)]
    [InlineData(DamageType.Slash, true)]
    public void TakeDamage_breaks_it_whole_only_for_a_crush_a_blast_or_a_slash(DamageType type, bool breaks)
    {
        var rig = Spawned(("health", "50"));

        Assert.Equal(0, rig.Glass.OnDamagedAt(EntityHandle.None, 30f, type, default, new Vector(0f, 100f, 0f)));

        Assert.Equal(breaks, rig.Glass.State.Broken);
    }

    /// <summary>Event_Killed does nothing: a sheet is not removed for running out of health.</summary>
    [Fact]
    public void Being_killed_does_nothing()
    {
        var rig = Spawned(("health", "50"));

        rig.Glass.OnKilled(EntityHandle.None);

        Assert.Equal(0, rig.Glass.OnBreak.FireCount);
        Assert.Equal(0, rig.Engine.CountOf("Entities.Remove"));
    }

    /// <summary>InputShatter "x y radius": the sheet breaks and the panes within the radius of the point go.</summary>
    [Fact]
    public void Shatter_takes_out_the_panes_within_its_radius()
    {
        var rig = Spawned();

        rig.Registry.Input(rig.Glass.Handle, "Shatter", "0.5 0.5 24", 0f, EntityHandle.None);

        Assert.Equal(1, rig.Glass.OnBreak.FireCount);
        Assert.False(rig.Glass.State.IsPaneSolid(8, 5), "the pane at the middle");
        Assert.False(rig.Glass.State.IsPaneSolid(9, 5), "and its neighbour, 19 units off");
        Assert.True(rig.Glass.State.IsPaneSolid(9, 6), "but not the diagonal one, 25 units off");
        Assert.True(rig.Glass.State.IsPaneSolid(0, 0), "but not a corner");
        Assert.InRange(rig.SolidPanes, 120, 155);
    }

    /// <summary>SurfaceTouch: what passes through a broken sheet takes out the panes its box covers.</summary>
    [Fact]
    public void Walking_through_a_broken_sheet_takes_out_the_panes_in_the_way()
    {
        var rig = Spawned();
        var player = new EntityHandle(0x0001_0001u);
        rig.Engine.ReturnsEachCall("Entities.GetOrigin",
                                   () => rig.Engine.Calls.Last().ArgAs<EntityHandle>(0) == player ? new Vector(-320f, -704f, 40f) : Vector.Zero)
                  .Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)));
        rig.Shoot(-410f, 14f);
        Assert.True(rig.Glass.TouchEveryTick, "a broken sheet is touched every frame");
        Assert.True(rig.Glass.State.IsPaneSolid(9, 5));

        rig.Glass.OnTouch(player);

        Assert.False(rig.Glass.State.IsPaneSolid(9, 5), "a pane inside the box");
        Assert.True(rig.Glass.State.IsPaneSolid(9, 9), "but not one above it");
    }

    /// <summary>A prop of <paramref name="mass"/> kg striking the sheet (side 0, 10 kg) at <paramref name="speed"/>.</summary>
    private static PhysicsEvent Struck(FuncBreakableSurf glass, float mass, float speed) => new()
    {
        KindRaw = (uint)PhysicsEventKind.PostCollision,
        Entity0 = glass.Handle.Value, Entity1 = 0x0002_0001u,
        Mass0 = 10f, Mass1 = mass,
        Flags = PhysicsEvent.FlagMoveable0 | PhysicsEvent.FlagMoveable1,
        PreVelocity1X = speed,
        PointX = -320f, PointY = -704f, PointZ = 60f, NormalY = 1f,
    };

    /// <summary>VPhysicsCollision: over 10 the sheet is lightened to 2 and the damage queued for after the step.</summary>
    [Theory]
    [InlineData(50f, 400f)]
    [InlineData(2f, 150f)]
    public void A_heavy_impact_is_queued_as_damage(float mass, float speed)
    {
        var rig = Spawned();
        var impacts = rig.Registry.Services!.Require<PhysicsDamageQueue>();

        rig.Glass.OnVPhysicsCollision(Struck(rig.Glass, mass, speed), 0);

        Assert.Equal(1, impacts.Count);
        Assert.Contains(rig.Engine.AllOf("EntityPhysics.SetMass"), c => c.ArgAs<float>(1) == 2f);
    }

    /// <summary>A knock of 10 or less only marks it, and only with SF_BREAKABLESURF_CRACK_DECALS.</summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void A_light_knock_leaves_a_mark_where_the_map_asks(int spawnFlags, bool marked)
    {
        var rig = Spawned();
        var impacts = rig.Registry.Services!.Require<PhysicsDamageQueue>();
        rig.Engine.Returns("Entities.GetSpawnFlags", spawnFlags)
                  .Returns("WorldQuery.TraceLineSurface",
                           new SurfaceTraceResult(true, 0.5f, new Vector(-320f, -704f, 60f), new Vector(0f, -1f, 0f), rig.Glass.Handle, 28, 0));

        rig.Glass.OnVPhysicsCollision(Struck(rig.Glass, 2f, 80f), 0);

        Assert.Equal(0, impacts.Count);
        Assert.Equal(marked, rig.Engine.Called("Effects.ImpactEffect"));
    }

    [Fact]
    public void A_broken_sheet_takes_no_impact()
    {
        var rig = Spawned();
        var impacts = rig.Registry.Services!.Require<PhysicsDamageQueue>();
        rig.Shoot(-320f, 60f);

        rig.Glass.OnVPhysicsCollision(Struck(rig.Glass, 50f, 400f), 0);

        Assert.Equal(0, impacts.Count);
    }

    /// <summary>A traced blast blows out the whole of a glass window.</summary>
    [Fact]
    public void A_traced_blast_blows_out_the_whole_window()
    {
        var rig = Spawned();

        rig.Shoot(-320f, 60f, 50f, DamageType.Blast);

        Assert.Equal(0, rig.SolidPanes);
    }
}

/// <summary>
/// What a pane of glass says it is MADE of, and what carries that answer.
///
/// The struck face cannot be asked. vbsp will not compile a breakable surface
/// unless exactly one of its faces is drawn, so five faces out of six carry
/// nodraw's surfaceprop, which is 0, which is concrete — measured on
/// ss_sandbox, the same pane reads GLASS/GLASSWINDOWBREAK070A from the front
/// and TOOLS/TOOLSNODRAW from four units behind it. So the ENTITY answers, and
/// what it answers has to reach the impact.
/// </summary>
public class BreakableSurfaceMaterialTests
{
    /// <summary>What ss_sandbox's pane actually resolves to, live.</summary>
    private const int GlassProp = 28;

    /// <summary>A different number, so an assertion cannot pass by coincidence.</summary>
    private const int CoarseGlassProp = 14;

    private static FuncBreakableSurf Spawn(uint index, out RecordingEngine engine,
                                           int modelProp, int namedProp,
                                           params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncBreakableSurf).Assembly);

        var services = Services(out engine);
        engine.Strict = false;

        engine.Returns("WorldQuery.EntityModelSurfaceProp", modelProp);
        engine.Returns("WorldQuery.SurfacePropIndex", namedProp);

        // Only ever read for the spawn line that names what it resolved. The
        // letter comes from the double, so nothing here may ASSERT on it — the
        // index is the answer under test.
        engine.Returns("WorldQuery.SurfacePropGameMaterial", SurfaceMaterial.Glass);

        registry.Services = services;

        var handle = new EntityHandle(index);

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, EntityNames.FuncBreakableSurf, key, value);

        registry.Spawn(handle, EntityNames.FuncBreakableSurf);
        return (FuncBreakableSurf)registry.Get(handle)!;
    }

    /// <summary>CBreakableSurface::Spawn's SetCollisionGroup( COLLISION_GROUP_BREAKABLE_GLASS ), which this mod left at none.</summary>
    [Fact]
    public void ASheetOfGlassIsInGlasssOwnGroup()
    {
        var glass = Spawn(43, out var engine, modelProp: GlassProp, namedProp: CoarseGlassProp);

        Assert.Contains(engine.Calls, c => c.Member == "EntityPhysics.SetCollisionGroup"
                                        && c.ArgAs<EntityHandle>(0) == glass.Handle
                                        && c.ArgAs<CollisionGroup>(1) == CollisionGroup.BreakableGlass);
    }

    [Fact]
    public void TheModelIsWhatThePaneIsMadeOf()
    {
        var glass = Spawn(41, out var engine, modelProp: GlassProp, namedProp: CoarseGlassProp);

        Assert.True(glass.SurfaceProp == GlassProp,
                    "a pane is made of what its own model is textured with");

        Assert.True(engine.CountOf("WorldQuery.SurfacePropIndex") == 0,
                    "and the coarse map key is not consulted when the model answered");
    }

    [Fact]
    public void TheMapKeySpeaksWhenTheModelCannot()
    {
        // -1 is what a side with no materials loaded answers, and what a brush
        // drawing more than one material answers. Neither is a failure.
        var glass = Spawn(42, out var engine, modelProp: -1, namedProp: CoarseGlassProp);

        Assert.True(glass.SurfaceProp == CoarseGlassProp,
                    "a pane whose model cannot answer falls back to the kind of surface the map named");

        Assert.True((string?)engine.Last("WorldQuery.SurfacePropIndex")!.Value.Args[0] == "glass",
                    "and surfacetype 0 is glass");
    }

    [Fact]
    public void TileIsTheOtherKind()
    {
        var tile = Spawn(43, out var engine, modelProp: -1, namedProp: CoarseGlassProp,
                         ("surfacetype", "1"));

        Assert.True((string?)engine.Last("WorldQuery.SurfacePropIndex")!.Value.Args[0] == "tile",
                    "surfacetype 1 is tile, which is the only other kind stock has");
    }

    [Fact]
    public void AShotCarriesThePanesOwnSurfaceProp()
    {
        var glass = Spawn(44, out var engine, modelProp: GlassProp, namedProp: CoarseGlassProp);

        var at = new Vector(-320f, -704f, 60f);

        // Shot from BEHIND: the nodraw side, the one whose own surfaceprop is 0.
        var force = new Vector(0f, 400f, 0f);

        glass.TraceAttack(EntityHandle.None, 8f, DamageType.Bullet, at - force, at);

        var impact = engine.Last("Effects.ImpactEffect");

        Assert.True(impact is not null,
                    "a bullet leaves an impact\n" + engine.Describe());

        Assert.True((int)impact!.Value.Args[2]! == GlassProp,
                    "and the impact carries the PANE's surfaceprop, not the nodraw face's");

        Assert.True((DamageType)impact.Value.Args[3]! == DamageType.Bullet,
                    "with the damage that made it");
    }

    [Fact]
    public void TheImpactStartsOnTheSideTheShotCameFrom()
    {
        var glass = Spawn(45, out var engine, modelProp: GlassProp, namedProp: CoarseGlassProp);

        var at = new Vector(-320f, -704f, 60f);

        // Travelling in +y, so the shot came from lower y and the client has to
        // trace back that way to find the face it struck.
        glass.TraceAttack(EntityHandle.None, 8f, DamageType.Bullet, at - new Vector(0f, 400f, 0f), at);

        var impact = engine.Last("Effects.ImpactEffect")!.Value;

        var start = (Vector)impact.Args[0]!;
        var end   = (Vector)impact.Args[1]!;

        Assert.True(end.Y == at.Y, "the impact is at the point the shot landed");

        Assert.True(start.Y < at.Y,
                    $"and starts back along the shot, on the side it came from — got {start}");
    }

    [Fact]
    public void OnlyABulletLeavesAMark()
    {
        var glass = Spawn(46, out var engine, modelProp: GlassProp, namedProp: CoarseGlassProp,
                          ("lowerleft", "-416 -704 8"), ("lowerright", "-224 -704 8"),
                          ("upperleft", "-416 -704 128"), ("upperright", "-224 -704 128"));

        // A blast takes the whole sheet down and has no one point to mark.
        glass.TraceAttack(EntityHandle.None, 80f, DamageType.Blast,
                          new Vector(-320f, -1104f, 60f), new Vector(-320f, -704f, 60f));

        Assert.True(engine.CountOf("Effects.ImpactEffect") == 0,
                    "a blast has no impact point to mark");
    }
}
