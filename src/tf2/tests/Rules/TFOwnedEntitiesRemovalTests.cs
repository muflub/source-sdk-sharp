using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Server.Buildings;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Server.Rules;
using TF2.Shared;

using TF2.Tests.Buildings;
using TF2.Tests.Entities.Items;
using TF2.Tests.Player;
using TF2.Tests.Projectiles;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// Stock's <c>RemoveAllOwnedEntitiesFromWorld</c> (tf_player.cpp:13404-13431)
/// over its stock callers' arms: a team change and a departure take the
/// owner's whole world with the buildings exploding, the class-change
/// respawn takes it quietly — and only when the desired class differs from
/// the worn one. The world: an engineer's standing sentry through the
/// buildings rig, a soldier's rocket and stickies through the projectile rig.
/// </summary>
public sealed class TFOwnedEntitiesRemovalTests
{
    private const int Red = (int)TFTeam.Red;
    private const int Blue = (int)TFTeam.Blue;

    private static readonly EntityHandle Rocket = new(0x0003_00A0u);
    private static readonly EntityHandle EnemyRocket = new(0x0003_00A1u);
    private static readonly EntityHandle Sticky = new(0x0003_00A2u);

    /// <summary>The rule over the rig's seams; the buildings go through its
    /// real directory, the stickies through the tray.</summary>
    private static TFOwnedEntitiesRemoval Over(BuildingsRig rig) =>
        new(rig.Engine, rig.Registry, rig.Buildings(), new TFStickyTray(rig.Carriers), rig.Carriers);

    private sealed class Flyers(params ScriptedEntity[] owned) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h) => null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) =>
            className == TFProjectileEntityNames.Rocket ? owned : [];
        public bool IsRegistered(string className) => false;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>An arsenal with nothing standing, so the rocket fact presses
    /// only the flyers.</summary>
    private sealed class EmptyBuildings : ITFBuildings
    {
        public IEnumerable<TFBaseObject> OwnedBy(EntityHandle? builder, TFObjectType? type, int? mode) => [];
    }

    private static void ADesk(BuildingsRig rig)
    {
        rig.StubWorld();
        rig.Purse.Pockets[TFAmmoNames.Name(TFAmmo.Metal)] = 500;
    }

    [Fact]
    public void TheEngineersSentryIsDestroyedWhenHeChangesTeam()
    {
        var rig = new BuildingsRig();
        ADesk(rig);
        EntityHandle engineer = rig.AddBuilder(TFTeam.Red);
        Assert.NotNull(rig.Builder().Build(engineer, TFObjectType.Sentrygun, 0,
                                           new Vector(100f, 0f, 0f), Vector.Zero).Building);
        Assert.NotEmpty(rig.Buildings().OwnedBy(engineer, TFObjectType.Sentrygun, null));

        Over(rig).TeamChanged(engineer, Red, Blue);

        // The explode arm kills through the object's own blast: the standing
        // walk finds nothing of the engineer's left.
        Assert.Empty(rig.Buildings().OwnedBy(engineer, TFObjectType.Sentrygun, null));
    }

    /// <summary>Both arms of the respawn guard: the class change's respawn
    /// takes the buildings (the quiet <see cref="TFBaseObject.DestroyObject"/>),
    /// the same-class respawn keeps them.</summary>
    [Theory]
    [InlineData(TFClass.Soldier)]
    [InlineData(TFClass.Scout)]
    public void TheClassChangeRespawnTakesTheBuildingsAndASameClassOneKeepsThem(TFClass desired)
    {
        var rig = new BuildingsRig();
        ADesk(rig);
        rig.Players.All[PlayerRig.Player] = new FakeMapPlayer { Class = TFClass.Engineer };
        Assert.NotNull(rig.Builder().Build(PlayerRig.Player, TFObjectType.Sentrygun, 0,
                                           new Vector(100f, 0f, 0f), Vector.Zero).Building);

        var player = new PlayerRig();
        var cls = new TFPlayerClass(player.Carriers, player.Items);
        player.Extra.Add(cls);
        player.Join();
        player.Carriers.SetClass(PlayerRig.Index, TFClass.Scout);
        cls.Desired = desired;

        var rule = new TFOwnedEntitiesRemoval(rig.Engine, rig.Registry, rig.Buildings(),
                                              new TFStickyTray(player.Carriers), player.Carriers);
        rule.OnSpawning(PlayerRig.Player, 0f);

        bool changedClass = desired != TFClass.Scout;
        Assert.Equal(changedClass, !rig.Buildings().OwnedBy(PlayerRig.Player, TFObjectType.Sentrygun, null)
                                        .Any());
    }

    [Fact]
    public void ASoldiersRocketInTheAirIsRemovedWhenHeChangesTeam()
    {
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rocket.Handle = Rocket;
        rocket.ArmedBy(90f, TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false, ownerIndex: 3);
        var alien = rig.Rocket();
        alien.Handle = EnemyRocket;
        alien.ArmedBy(90f, TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false, ownerIndex: 9);
        var sticky = rig.Sticky();
        sticky.Handle = Sticky;
        rig.Tray.Register(ownerIndex: 3, sticky);
        Assert.Equal(1, rig.Tray.Count(3));

        var rule = new TFOwnedEntitiesRemoval(rig.Engine, new Flyers(rocket, alien),
                                              new EmptyBuildings(), rig.Tray, rig.Carriers);
        rule.TeamChanged(ProjectileRig.Player, Red, Blue);

        // The owner's rocket and his launcher's stickies go — the rocket
        // through the engine's removal route, the sticky fizzled whole beside
        // it (stock's grenade-owner arm, quiet and no blast); the other
        // owner's rocket stays in the air.
        var removed = rig.Engine.AllOf("Entities.Remove").Select(c => c.ArgAs<EntityHandle>(0)).ToList();
        Assert.Equal([Rocket, Sticky], removed);
        Assert.Equal(0, rig.Tray.Count(3));
    }

    [Fact]
    public void ALeavingPlayersBuildingsGoWithThemAndHisTeammatesStay()
    {
        var rig = new BuildingsRig();
        ADesk(rig);
        EntityHandle engineer = rig.AddBuilder(TFTeam.Red);
        EntityHandle mate = rig.AddBuilder();
        rig.Builder().Build(engineer, TFObjectType.Sentrygun, 0, new Vector(100f, 0f, 0f), Vector.Zero);
        rig.Builder().Build(mate, TFObjectType.Sentrygun, 0, new Vector(400f, 0f, 0f), Vector.Zero);

        Over(rig).OnClientDisconnected(engineer.EntryIndex);

        Assert.Empty(rig.Buildings().OwnedBy(engineer, TFObjectType.Sentrygun, null));
        Assert.NotEmpty(rig.Buildings().OwnedBy(mate, TFObjectType.Sentrygun, null));
    }

    /// <summary>The rule's one order, shared by the chains it joins, standing
    /// before the spawn that collapses the desired class.</summary>
    [Fact]
    public void TheRuleOrdersOnceAndBeforeTheSpawnCollapse()
    {
        var rig = new BuildingsRig();
        var rule = Over(rig);

        Assert.Equal(TeamRoundOrders.OwnedEntitiesRemoval, rule.HookOrder);
        Assert.Equal(TeamRoundOrders.Band + 13, rule.HookOrder);
        Assert.Equal(rule.HookOrder, rule.ClientLifecycleOrder);
        Assert.True(rule.HookOrder < TFPlayerOrders.Spawn,
                    "the class-change guard must read the desired class before the spawn collapses it");
    }
}
