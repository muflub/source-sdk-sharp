using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The piercing tail of a round a behaviour marked
/// <see cref="ShotDamage.PenetratePlayers"/>: after the player it struck, the
/// same ray's further players take the shot where they stand, each through the
/// hit's own rules, until a wall or the ray's end stops it — stock's
/// penetrating walk (tf_player_shared.cpp:10303-10330, :10373-10402) as the
/// server's round carries it. The wall arm is the negative control's other
/// half: a round with nobody behind its victim leaves nothing pierced.
/// </summary>
public class BulletPiercingTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);
    private static readonly EntityHandle First = new(0x0001_0002u);
    private static readonly EntityHandle Second = new(0x0001_0003u);

    private const string Gun = "wep_pierce_gun";
    private const string Key = "wep.pierce";
    private const int Attack = (int)InputButtons.Attack;

    private static readonly Vector FirstAt = new(64f, 0f, 64f);
    private static readonly Vector SecondAt = new(128f, 0f, 64f);
    private static readonly Vector WallAt = new(200f, 0f, 64f);

    private static readonly WeaponType GunRow =
        WeaponCatalog.Stock().For("weapon_smg1")!.Value with { Name = Gun, Behaviour = Key };

    private sealed class GunRows : IWeaponTypeSource
    {
        public IEnumerable<WeaponType> Rows() => [GunRow];
    }

    private sealed class Source(IWeaponBehaviour behaviour) : IWeaponBehaviourSource
    {
        public IEnumerable<KeyValuePair<string, IWeaponBehaviour>> Behaviours()
            => [KeyValuePair.Create(Key, behaviour)];
    }

    private sealed class Decides(ShotDamage answer) : IWeaponBehaviour
    {
        public ShotDamage ModifyShot(in WeaponAttackContext attack, ShotDamage shot) => answer;
    }

    private static SurfaceTraceResult Flesh(EntityHandle who, Vector at, float fraction, HitGroup group) =>
        new(true, fraction, at, new Vector(-1f, 0f, 0f), who, 13, 0, group);

    private static readonly SurfaceTraceResult Wall =
        new(true, 0.9f, WallAt, new Vector(-1f, 0f, 0f), EntityHandle.None, 13, 0, HitGroup.Generic);

    private static (List<EntityHandle> Wounded, List<Vector> Impacts, RecordingEngine Engine) Fired(
        ShotDamage shot, params SurfaceTraceResult[] surfaces)
    {
        int asked = 0;
        var engine = Mock().Returns("Entities.IsValid", true).Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", Vector.Zero)
            .Returns("Entities.GetEyeAngles", Vector.Zero)
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            .Returns("WorldQuery.TraceLine",
                     new TraceResult(true, 0.5f, FirstAt, new Vector(-1f, 0f, 0f), EntityHandle.None))
            .ReturnsEachCall("WorldQuery.TraceLineSurface", () => asked < surfaces.Length ? surfaces[asked++] : null)
            .Returns("Clock.Now", 10f);
        engine.Strict = false;   // a shot also animates and sounds; that is not these facts

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");
        registry.KeyValue(First, EntityNames.Player, "targetname", "pierce_first");
        registry.KeyValue(Second, EntityNames.Player, "targetname", "pierce_second");

        var behaviour = new Decides(shot);
        var arsenal = new Arsenal(new RecordingLog(), new WeaponSwitchTests.SpawningEntities(engine, registry),
                                  engine, engine, engine, engine, engine, engine, engine, registry, engine,
                                  new DamageHitGroups(),
                                  weapons: new(() => [new GunRows()], new RecordingLog()),
                                  behaviours: new(() => [new Source(behaviour)], new RecordingLog()));

        Assert.True(arsenal.GiveWeapon(Player, Gun), "the piercing row can be given");

        var wounded = new List<EntityHandle>();
        var impacts = new List<Vector>();
        engine.WhenCalled("Damage.ApplyAt", call => wounded.Add(call.ArgAs<EntityHandle>(0)));
        engine.WhenCalled("Effects.ImpactEffect", call => impacts.Add(call.ArgAs<Vector>(1)));

        engine.Returns("Clock.Now", 30f);
        var cmd = new UserCmd { CommandNumber = 1, Buttons = Attack };
        arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);

        return (wounded, impacts, engine);
    }

    [Fact]
    public void APiercingRoundWoundsThePlayerBehindTheFirst()
    {
        var (wounded, _, engine) = Fired(new ShotDamage(40f, PenetratePlayers: true),
                                         Flesh(First, FirstAt, 0.25f, HitGroup.Chest),
                                         Flesh(Second, SecondAt, 0.5f, HitGroup.Head),
                                         Wall);

        Assert.Equal([First, Second], wounded);
        Assert.Equal(2, engine.Calls.Count(c => c.Member == "Effects.BloodEffect"));
    }

    [Fact]
    public void TheWallTheWayEndsOnTakesItsMark()
    {
        var (_, impacts, _) = Fired(new ShotDamage(40f, PenetratePlayers: true),
                                    Flesh(First, FirstAt, 0.25f, HitGroup.Chest),
                                    Flesh(Second, SecondAt, 0.5f, HitGroup.Head),
                                    Wall);

        Assert.Contains(WallAt, impacts);
    }

    [Fact]
    public void APlainRoundStopsAtItsFirstVictim()
    {
        var (wounded, _, _) = Fired(new ShotDamage(40f),
                                    Flesh(First, FirstAt, 0.25f, HitGroup.Chest),
                                    Flesh(Second, SecondAt, 0.5f, HitGroup.Head),
                                    Wall);

        Assert.Equal([First], wounded);
    }
}
