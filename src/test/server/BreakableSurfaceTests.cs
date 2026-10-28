using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// func_breakable_surf, whose damage rule is the opposite way round from every
/// other breakable here.
///
/// Damage arrives through OnDamagedAt rather than OnDamaged, because glass is
/// the one thing that needs to know WHERE it was hit: a bullet shatters the pane
/// it struck and leaves the rest of the window standing.
///
/// Returning zero from OnDamaged normally means "ignore this". For glass it
/// means "the stock rules apply" — the pane that was struck shatters, and
/// crush, blast or slash take the whole sheet. Stock gives a surface no health
/// at all, so a map that never heard of this mod must come out behaving exactly
/// as it did, and that is what zero buys.
///
/// Anything above zero opts the surface into a health model it does not
/// otherwise have: glass that takes three shots rather than one.
/// </summary>
public class BreakableSurfaceTests
{

    private static FuncBreakableSurf Spawn(uint index, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncBreakableSurf).Assembly);

        var services = Services(out var engine);

        // What the pane is MADE of is not what these three are about. See
        // BreakableSurfaceMaterialTests.
        engine.Ignores("WorldQuery.EntityModelSurfaceProp",
                       "WorldQuery.SurfacePropIndex",
                       "WorldQuery.SurfacePropGameMaterial");

        registry.Services = services;

        var handle = new EntityHandle(index);

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, EntityNames.FuncBreakableSurf, key, value);

        registry.Spawn(handle, EntityNames.FuncBreakableSurf);
        return (FuncBreakableSurf)registry.Get(handle)!;
    }

    [Fact]
    public void NoHealthIsStock()
    {
        // What every stock map writes: no health key at all.
        var glass = Spawn(1);

        Assert.True(glass.OnDamagedAt(EntityHandle.None, 50f, DamageType.Bullet, default, default) == 0, "a surface with no health claims none of the damage");

        // And says nothing, because it did not act. An OnDamaged that fired
        // here would report every bullet that passed through the window.
        Assert.True(glass.Damaged.FireCount == 0, "and does not report having been damaged");
    }

    [Fact]
    public void HealthOptsIn()
    {
        var glass = Spawn(2, ("health", "60"));

        Assert.True(glass.OnDamagedAt(EntityHandle.None, 25f, DamageType.Bullet, default, default) == 25, "a surface with health claims the damage");

        Assert.True(glass.Damaged.FireCount == 1, "and reports it");

        // Once broken it stops claiming, so a second hit in the same tick does
        // not fire OnBreak twice.
        glass.OnKilled(EntityHandle.None);

        Assert.True(glass.OnBreak.FireCount == 1, "breaking fires OnBreak once");

        glass.OnKilled(EntityHandle.None);
        Assert.True(glass.OnBreak.FireCount == 1, "and not again");

        Assert.True(glass.OnDamagedAt(EntityHandle.None, 25f, DamageType.Bullet, default, default) == 0, "a broken surface claims nothing");
    }

    [Fact]
    public void MinimumDamage()
    {
        var glass = Spawn(3, ("health", "60"), ("minhealthdmg", "20"));

        // Under the threshold falls through to stock rather than being reduced,
        // which for glass means the pane still shatters where it was hit.
        Assert.True(glass.OnDamagedAt(EntityHandle.None, 5f, DamageType.Bullet, default, default) == 0, "damage under the minimum is handed back to stock");

        Assert.True(glass.OnDamagedAt(EntityHandle.None, 20f, DamageType.Bullet, default, default) == 20, "damage at the minimum is claimed");
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

        glass.OnDamagedAt(EntityHandle.None, 8f, DamageType.Bullet, at, force);

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
        glass.OnDamagedAt(EntityHandle.None, 8f, DamageType.Bullet, at, new Vector(0f, 400f, 0f));

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
        var glass = Spawn(46, out var engine, modelProp: GlassProp, namedProp: CoarseGlassProp);

        // A blast takes the whole sheet down and has no one point to mark.
        glass.OnDamagedAt(EntityHandle.None, 80f, DamageType.Blast,
                          new Vector(-320f, -704f, 60f), new Vector(0f, 400f, 0f));

        Assert.True(engine.CountOf("Effects.ImpactEffect") == 0,
                    "a blast has no impact point to mark");
    }
}
