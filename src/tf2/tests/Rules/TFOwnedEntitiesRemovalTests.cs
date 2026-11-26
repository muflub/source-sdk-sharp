using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server.Buildings;
using TF2.Server.Combat;
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

    private static readonly EntityHandle Pipe = new(0x0003_00B0u);
    private static readonly EntityHandle AlienPipe = new(0x0003_00B1u);
    private static readonly EntityHandle Arrow = new(0x0003_00B2u);
    private static readonly EntityHandle AlienArrow = new(0x0003_00B3u);
    private static readonly EntityHandle Flare = new(0x0003_00B4u);
    private static readonly EntityHandle AlienFlare = new(0x0003_00B5u);
    private static readonly EntityHandle Jar = new(0x0003_00B6u);
    private static readonly EntityHandle AlienJar = new(0x0003_00B7u);
    private static readonly EntityHandle Cleaver = new(0x0003_00B8u);
    private static readonly EntityHandle AlienCleaver = new(0x0003_00B9u);

    /// <summary>The second owner: the entry-index bits a fact names the
    /// alien's flyers by.</summary>
    private static readonly EntityHandle Alien = new(0x0001_0000u | 9);

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

    /// <summary>The flyers of every name the walk covers, found by the one
    /// name each wears — the class the directory answers with.</summary>
    private sealed class NamedFlyers(params ScriptedEntity[] flyers) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h) => null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) =>
            flyers.Where(f => f.ClassName == className);
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

    /// <summary>The whole flyer band on the team-change arm: the owner's
    /// pipe, arrow, flare, jar and cleaver go in the walk's own order, the
    /// other owner's same-named flyers stay in the air.</summary>
    [Fact]
    public void TheFlyingPipeArrowFlareJarAndCleaverGoWithTheirOwnerAndTheAliensStay()
    {
        var rig = new ProjectileRig();
        rig.Engine.Returns("Clock.Now", 10f);

        var pipe = rig.Pipebomb();
        pipe.Handle = Pipe;
        pipe.ClassName = TFProjectileEntityNames.Pipebomb;
        pipe.ThrownBy(ProjectileRig.Player, new Vector(1200f, 0f, 0f), 90f, 146f,
                      TFWeaponId.Grenadelauncher, "tf_weapon_grenadelauncher", critical: false);
        var alienPipe = rig.Pipebomb();
        alienPipe.Handle = AlienPipe;
        alienPipe.ClassName = TFProjectileEntityNames.Pipebomb;
        alienPipe.ThrownBy(Alien, new Vector(1200f, 0f, 0f), 90f, 146f,
                           TFWeaponId.Grenadelauncher, "tf_weapon_grenadelauncher", critical: false);

        var arrow = rig.Arrow();
        arrow.Handle = Arrow;
        arrow.ClassName = TFProjectileEntityNames.Arrow;
        arrow.ArmedBy(TFWeaponId.CompoundBow, "tf_weapon_compound_bow", critical: false, lit: true,
                      TFProjectileDefs.ArrowDamage(10f, 1f), TFProjectileDefs.ArrowGravity(1f));
        arrow.FiredBy(ProjectileRig.Player, new Vector(500f, 0f, 0f));
        var alienArrow = rig.Arrow();
        alienArrow.Handle = AlienArrow;
        alienArrow.ClassName = TFProjectileEntityNames.Arrow;
        alienArrow.ArmedBy(TFWeaponId.CompoundBow, "tf_weapon_compound_bow", critical: false, lit: true,
                           TFProjectileDefs.ArrowDamage(10f, 1f), TFProjectileDefs.ArrowGravity(1f));
        alienArrow.FiredBy(Alien, new Vector(500f, 0f, 0f));

        var flare = rig.Flare();
        flare.Handle = Flare;
        flare.ClassName = TFProjectileEntityNames.Flare;
        flare.ArmedBy(30f, TFWeaponId.Flaregun, "tf_weapon_flaregun", critical: false,
                      TFProjectileDefs.FlareGravity, ownerIndex: 3,
                      flareMode: TFProjectileBehaviour.FlareNormal);
        var alienFlare = rig.Flare();
        alienFlare.Handle = AlienFlare;
        alienFlare.ClassName = TFProjectileEntityNames.Flare;
        alienFlare.ArmedBy(30f, TFWeaponId.Flaregun, "tf_weapon_flaregun", critical: false,
                           TFProjectileDefs.FlareGravity, ownerIndex: 9,
                           flareMode: TFProjectileBehaviour.FlareNormal);

        var jar = rig.Jar();
        jar.Handle = Jar;
        jar.ClassName = TFProjectileEntityNames.Jar;
        jar.ArmedBy(critical: false, ownerIndex: 3);
        jar.FiredBy(ProjectileRig.Player, new Vector(TFProjectileDefs.JarSpeed, 0f, 0f));
        var alienJar = rig.Jar();
        alienJar.Handle = AlienJar;
        alienJar.ClassName = TFProjectileEntityNames.Jar;
        alienJar.ArmedBy(critical: false, ownerIndex: 9);
        alienJar.FiredBy(Alien, new Vector(TFProjectileDefs.JarSpeed, 0f, 0f));

        var players = new TFMovementFixtures.Players();
        var bleeds = new TFBleeds(players, rig.Conditions, rig.Engine, rig.Engine, rig.Engine, rig.Scopes);
        var cleaver = MakeCleaver(rig, bleeds, Cleaver);
        cleaver.FiredBy(ProjectileRig.Player, new Vector(600f, 0f, 0f));
        var alienCleaver = MakeCleaver(rig, bleeds, AlienCleaver);
        alienCleaver.FiredBy(Alien, new Vector(600f, 0f, 0f));

        var rule = new TFOwnedEntitiesRemoval(rig.Engine,
                                              new NamedFlyers(pipe, alienPipe, arrow, alienArrow, flare, alienFlare,
                                                              jar, alienJar, cleaver, alienCleaver),
                                              new EmptyBuildings(), rig.Tray, rig.Carriers);
        rule.TeamChanged(ProjectileRig.Player, Red, Blue);

        // The walk's own order: pipe, arrow, flare, jar, cleaver — only the
        // owner's. The alien's same-named flyers stay.
        var removed = rig.Engine.AllOf("Entities.Remove").Select(c => c.ArgAs<EntityHandle>(0)).ToList();
        Assert.Equal([Pipe, Arrow, Flare, Jar, Cleaver], removed);
    }

    /// <summary>A cleaver has no rig factory: built the way the cleaver's
    /// own facts build it, areas, net states and thinks bound.</summary>
    private static TFCleaver MakeCleaver(ProjectileRig rig, TFBleeds bleeds, EntityHandle handle)
    {
        var cleaver = new TFCleaver(rig.Engine, rig.Engine, rig.Engine, rig.HitGroups, rig.Scopes,
                                    bleeds, rig.Engine, rig.Conditions, rig.Engine, rig.Engine, rig.Engine,
                                    new SpyGiver());
        cleaver.Handle = handle;
        cleaver.BindAreas(TestServices.Areas(rig.Engine));
        cleaver.NetStates = NetStateBinding.AttachLocal(cleaver, cleaver.Handle,
                                                        new NetStateFields(new RecordingLog()));
        cleaver.BindThinks(new ThinkScheduler());
        cleaver.ClassName = TFProjectileEntityNames.Cleaver;
        return cleaver;
    }

    /// <summary>The giver behind the cleaver; the walk never reaches the
    /// recharge arms, so the giver only stands.</summary>
    private sealed class SpyGiver : IWeaponGiver
    {
        public bool GiveWeapon(EntityHandle player, string className) => true;

        public int GiveAmmo(EntityHandle player, int count, string ammoName) => count;

        public void AdvanceCarriedMeter(EntityHandle player, string className, float seconds) { }
    }

    /// <summary>A pipe deflected off a heavy's shotgun belongs to the
    /// deflector: the thrower's team change walks past it, the deflector's
    /// takes it.</summary>
    [Fact]
    public void ADeflectedPipeGoesWithTheDeflectorAndNotWithItsThrower()
    {
        var rig = new ProjectileRig();
        var pipe = rig.Pipebomb();
        pipe.Handle = Pipe;
        pipe.ClassName = TFProjectileEntityNames.Pipebomb;
        pipe.ThrownBy(ProjectileRig.Player, new Vector(1200f, 0f, 0f), 90f, 146f,
                      TFWeaponId.Grenadelauncher, "tf_weapon_grenadelauncher", critical: false);
        pipe.DeflectedBy(Alien, deflectorCritBoosted: false, launcherName: "tf_weapon_shotgun_heavy");

        var rule = new TFOwnedEntitiesRemoval(rig.Engine, new NamedFlyers(pipe),
                                              new EmptyBuildings(), rig.Tray, rig.Carriers);

        rule.TeamChanged(ProjectileRig.Player, Red, Blue);
        Assert.Empty(rig.Engine.AllOf("Entities.Remove"));

        rule.TeamChanged(Alien, Red, Blue);
        var removed = rig.Engine.AllOf("Entities.Remove").Select(c => c.ArgAs<EntityHandle>(0)).ToList();
        Assert.Equal([Pipe], removed);
    }

    /// <summary>The same answer on the departure arm: the original thrower's
    /// leaving keeps the sent-back pipe, the deflector's takes it.</summary>
    [Fact]
    public void ASentBackPipeStaysWhenItsFirstThrowerLeavesAndGoesWhenTheDeflectorDoes()
    {
        var rig = new ProjectileRig();
        var pipe = rig.Pipebomb();
        pipe.Handle = Pipe;
        pipe.ClassName = TFProjectileEntityNames.Pipebomb;
        pipe.ThrownBy(ProjectileRig.Player, new Vector(1200f, 0f, 0f), 90f, 146f,
                      TFWeaponId.Grenadelauncher, "tf_weapon_grenadelauncher", critical: false);
        pipe.DeflectedBy(Alien, deflectorCritBoosted: false, launcherName: "tf_weapon_shotgun_heavy");

        var rule = new TFOwnedEntitiesRemoval(rig.Engine, new NamedFlyers(pipe),
                                              new EmptyBuildings(), rig.Tray, rig.Carriers);

        rule.OnClientDisconnected(ProjectileRig.Player.EntryIndex);
        Assert.Empty(rig.Engine.AllOf("Entities.Remove"));

        rule.OnClientDisconnected(Alien.EntryIndex);
        var removed = rig.Engine.AllOf("Entities.Remove").Select(c => c.ArgAs<EntityHandle>(0)).ToList();
        Assert.Equal([Pipe], removed);
    }

    /// <summary>Stock's flame arm on the team change: the attacker's manager
    /// loses its points, every other manager keeps them.</summary>
    [Fact]
    public void TheFlameManagersPointsClearForItsAttackerAndNotForAnother()
    {
        var rig = new ManagerRig();
        rig.Manager.SetAttacker(ManagerRig.Attacker);
        Assert.True(rig.Manager.AddPoint(default, new Vector(1f, 0f, 0f), default));
        rig.Manager.ClassName = TFFlameManagerDefs.ClassName;

        var alienManager = new TFFlameManager(rig.Engine, rig.Damage, rig.Engine, rig.Engine, rig.Conditions,
                                              rig.Scopes, rig.HitGroups, rig.BoxSize, rig.Owners, rig.Directory);
        alienManager.BindAreas(TestServices.Areas(rig.Engine));
        alienManager.SetAttacker(Alien);
        Assert.True(alienManager.AddPoint(default, new Vector(1f, 0f, 0f), default));
        alienManager.ClassName = TFFlameManagerDefs.ClassName;

        var rule = new TFOwnedEntitiesRemoval(rig.Engine, new NamedFlyers(rig.Manager, alienManager),
                                              new EmptyBuildings(), new TFStickyTray(rig.Carriers), rig.Carriers);
        rule.TeamChanged(ProjectileRig.Player, Red, Blue);

        Assert.Equal(0, rig.Manager.PointCount);
        Assert.Equal(1, alienManager.PointCount);
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
