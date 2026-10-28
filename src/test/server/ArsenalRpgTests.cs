using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The RPG through the real arsenal and server arms (CWeaponRPG,
/// shared/hl2mp/weapon_rpg.cpp): the missile made at stock's muzzle and
/// facing the aim, the switch refused while it flies, the reload once it has
/// gone, and the laser dot the carrier owns, moved to the end of their aim and
/// removed when the RPG is put away.
/// </summary>
public class ArsenalRpgTests
{
    private static EntityHandle Player => new(0x0001_0001);

    private const int Attack = (int)InputButtons.Attack;

    private static readonly Vector Wall = new(500f, 0f, 64f);

    /// <param name="EntityEngine">What the spawned entities themselves (the missile, the dot) talk to.</param>
    private sealed record Rig(Arsenal Arsenal, RecordingEngine Engine, ScriptedEntityRegistry Registry,
                              WeaponSwitchTests.SpawningEntities World, RecordingEngine EntityEngine);

    private static Rig Armed()
    {
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f))
            .Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f))
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            .Returns("WorldQuery.TraceLine",
                     new TraceResult(true, 0.01f, Wall, new Vector(-1f, 0f, 0f), EntityHandle.None))
            .Returns("Clock.Now", 10f);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out var entityEngine);
        entityEngine.Strict = false;
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");

        var world = new WeaponSwitchTests.SpawningEntities(engine, registry);
        var arsenal = new Arsenal(new RecordingLog(), world, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups());

        Assert.True(arsenal.GiveWeapon(Player, EntityNames.WeaponPistol), "the fixture's player has a pistol");
        Assert.True(arsenal.GiveWeapon(Player, EntityNames.WeaponRpg), "and an RPG");
        Assert.Equal(SwitchOutcome.Switched, arsenal.Switch(Player, EntityNames.WeaponRpg));

        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;
        return new Rig(arsenal, engine, registry, world, entityEngine);
    }

    private static void Command(Rig rig, int buttons, float at)
    {
        rig.Engine.Returns("Clock.Now", at);
        var cmd = new UserCmd { Buttons = buttons };
        rig.Arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);
    }

    private static Rocket? TheMissile(Rig rig) => rig.Registry.OfClass(EntityNames.RpgMissile).OfType<Rocket>().SingleOrDefault();

    private static LaserDot? TheDot(Rig rig) => rig.Registry.OfClass(EntityNames.EnvLaserdot).OfType<LaserDot>().SingleOrDefault();

    private static WeaponNetState Held(Rig rig)
        => ((SsWeapon)rig.Registry.Get(rig.Arsenal.CarriedPair(Player).Weapon)!).State;

    [Fact]
    public void AnRpgArrivesWithItsDefaultClipOfThreeRounds()
    {
        var rig = Armed();

        Assert.Equal(3, rig.Arsenal.Pools(Player)["RPG_Round"]);
    }

    [Fact]
    public void TheMissileLeavesStocksMuzzleFacingTheAim()
    {
        var rig = Armed();

        Command(rig, Attack, 30f);

        var missile = TheMissile(rig);
        Assert.NotNull(missile);
        var placed = rig.Engine.Calls.Last(c => c.Member == "Entities.SetOrigin" && c.ArgAs<EntityHandle>(0) == missile!.Handle);
        // Shoot position (0,0,64) + forward*12 + right*6 + up*-3, facing +X.
        Assert.Equal(new Vector(12f, -6f, 61f), placed.ArgAs<Vector>(1));
        var owner = rig.Engine.Calls.Last(c => c.Member == "Entities.SetOwner" && c.ArgAs<EntityHandle>(0) == missile!.Handle);
        Assert.Equal(Player, owner.ArgAs<EntityHandle>(1));
        Assert.True(missile!.LauncherWaits);
    }

    [Fact]
    public void TheRpgCannotBePutAwayWhileItsMissileFlies()
    {
        var rig = Armed();
        Command(rig, Attack, 30f);

        Assert.Equal(SwitchOutcome.CannotHolster, rig.Arsenal.Switch(Player, EntityNames.WeaponPistol));
    }

    [Fact]
    public void OnceItsMissileHasGoneTheRpgReloadsAndMayBePutAway()
    {
        var rig = Armed();
        Command(rig, Attack, 30f);
        TheMissile(rig)!.OnTouch(EntityHandle.None);

        Command(rig, 0, 30.1f);

        Assert.False(RpgState.Has(Held(rig).Special, RpgState.MissileOut));
        Assert.Equal(RpgActivity.Reload, RpgState.Activity(Held(rig).Special));
        Assert.Equal(SwitchOutcome.Switched, rig.Arsenal.Switch(Player, EntityNames.WeaponPistol));
    }

    [Fact]
    public void TheLaserDotIsTheCarriersAndSitsWhereTheirAimEnds()
    {
        var rig = Armed();

        Command(rig, 0, 30f);

        var dot = TheDot(rig);
        Assert.NotNull(dot);
        Assert.Equal(Player, dot!.OwnerEntity);
        Assert.True(dot.IsOn);
        var placed = rig.EntityEngine.Calls.Last(c => c.Member == "Entities.SetOrigin" && c.ArgAs<EntityHandle>(0) == dot.Handle);
        Assert.Equal(Wall, placed.ArgAs<Vector>(1));
    }

    [Fact]
    public void PuttingTheRpgAwayRemovesItsDot()
    {
        var rig = Armed();
        Command(rig, 0, 30f);
        var dot = TheDot(rig)!;

        Assert.Equal(SwitchOutcome.Switched, rig.Arsenal.Switch(Player, EntityNames.WeaponPistol));

        Assert.Contains(rig.Engine.Calls, c => c.Member == "Entities.Remove" && c.ArgAs<EntityHandle>(0) == dot.Handle);
        Assert.False(dot.IsOn);
    }

    /// <summary>
    /// UpdateLaserPosition traces MASK_SHOT less CONTENTS_WINDOW: the dot lands
    /// on what is behind glass. Found against stock (docs/divergences.md, the
    /// RPG laser trace row, 2026-10-25): this mod's stopped on the pane.
    /// </summary>
    [Fact]
    public void TheLaserLooksThroughGlass()
    {
        var rig = Armed();

        Command(rig, 0, 30f);

        Assert.Contains(rig.Engine.Calls, c => c.Member == "WorldQuery.TraceLine"
                                            && c.ArgAs<TraceMask>(2) == TraceMask.ShotNoWindows);
    }

    /// <summary>
    /// The dot's target is what it sits on when that can be hurt (stock's
    /// m_takedamage): a prop can, an info_target cannot. This mod asked the
    /// entity's health, which a prop with none reads as unhurtable.
    /// </summary>
    [Theory]
    [InlineData(EntityNames.PropPhysics, true)]
    [InlineData(EntityNames.InfoTarget, false)]
    public void TheDotsTargetIsWhatItSitsOnWhenThatCanBeHurt(string className, bool target)
    {
        var rig = Armed();
        var thing = new EntityHandle(0x0002_0031u);
        rig.Registry.Spawn(thing, className);
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 0.01f, Wall, new Vector(-1f, 0f, 0f), thing))
                  .Returns("Entities.GetHealth", 0);

        Command(rig, 0, 30f);

        Assert.Equal(target ? thing : EntityHandle.None, TheDot(rig)!.TargetEntity);
    }
}
