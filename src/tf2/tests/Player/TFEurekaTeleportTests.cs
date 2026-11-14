using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server;
using TF2.Server.Buildings;
using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Server.Voice;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Tests.Buildings;
using TF2.Tests.Entities;
using TF2.Tests.Rules;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// <see cref="TFEurekaTeleport"/>: the <c>eureka_teleport</c> command's gates
/// and the landing when the taunt's <c>TF_COND_TAUNTING</c> ends
/// (tf_player.cpp:7705-7739, 1560-1621).
/// </summary>
public sealed class TFEurekaTeleportTests
{
    private static readonly EntityHandle Spot = new(0x0050_0001u);

    private sealed class FakeEurekaTeams : ITeams
    {
        public EntityHandle Spawn = Spot;

        public int Team(EntityHandle player) => (int)TFTeam.Red;
        public IReadOnlyList<EntityHandle> Players(int team) => [];
        public int Count(int team) => 0;
        public JoinTeamResult Join(EntityHandle player, int requested) => default;
        public EntityHandle SelectSpawn(EntityHandle player) => Spawn;
    }

    private sealed class FakeEurekaBuildings : ITFBuildings
    {
        private readonly TFBaseObject[] _owned;

        public FakeEurekaBuildings(params TFBaseObject[] owned) => _owned = owned;

        public IEnumerable<TFBaseObject> OwnedBy(EntityHandle? builder, TFObjectType? type, int? mode)
            => _owned;
    }

    private static IAttributeProvider AttribItem(string hook, float value, string quality)
    {
        var container = new AttributeContainer(EurekaItem(quality), AttributeProviderType.Weapon);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(7001, "eureka test", hook, AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    /// <summary>The held wrench's item, as the schema parses it: the quality
    /// and names the criteria words read (the defindex is the shipped one).</summary>
    private static string ItemSchema(string quality) => $$"""
        "items_game"
        {
            "qualities"
            {
                "normal"
                {
                    "value"	"0"
                }
                "unique"
                {
                    "value"	"6"
                }
            }
            "attributes"
            {
            }
            "items"
            {
                "589"
                {
                    "name"	"The Eureka Effect"
                    "item_class"	"tf_weapon_wrench"
                    "item_name"	"#TF_Unique_Wrench_Eureka"
                    "item_type_name"	"#TF_Weapon_Wrench"
                    "item_quality"	"{{quality}}"
                    "item_slot"	"melee"
                    "used_by_classes"
                    {
                        "engineer"	"1"
                    }
                }
            }
        }
        """;

    private static ItemDefinition EurekaItem(string quality) => SchemaParser.Parse(ItemSchema(quality)).Items[589];

    /// <summary>The engineer, the command's every word, and the watch; empty of buildings until a fact hands one over.</summary>
    private sealed class Harness
    {
        public readonly PlayerRig Rig = new();
        public readonly FakeEurekaTeams Teams = new();
        public readonly FakeConceptRules Rules = new();
        public readonly Dictionary<EntityHandle, Vector> Origins = [];
        public readonly Dictionary<EntityHandle, Vector> Angles = [];
        public readonly TFEurekaTeleport Eureka;
        public readonly TFEurekaTauntWatch Watch;
        public readonly TFPlayerConditions Conditions;

        public Harness(TFClass cls, bool teleportAttrib, TFBaseObject[] buildings, string itemQuality = "unique")
        {
            PlayerRig rig = Rig;
            rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
            rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
            rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_wrench");
            rig.Engine.ReturnsEachCall("Entities.GetOrigin", () =>
            {
                EntityHandle who = (rig.Engine.Last("Entities.GetOrigin") ?? default).ArgAs<EntityHandle>(0);
                return Origins.TryGetValue(who, out var origin) ? origin : default;
            });
            rig.Engine.ReturnsEachCall("Entities.GetAngles", () =>
            {
                EntityHandle who = (rig.Engine.Last("Entities.GetAngles") ?? default).ArgAs<EntityHandle>(0);
                return Angles.TryGetValue(who, out var angle) ? angle : default;
            });

            Conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(), rig.Engine);
            var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, Conditions, rig.Loadout, rig.Engine,
                                                rig.Items, new AttributeHooks(),
                                                scripts: new TFWeaponScripts(rig.Log, WeaponFixtures.ShippedWithClasses()),
                                                catalog: rig.Catalog,
                                                arsenal: new Arsenal(rig.Log, rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                                                     rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                                                     new ScriptedEntityRegistry(TestEngines.Server()), rig.Engine,
                                                                     new DamageHitGroups()),
                                                hpMin: DeclaredConVars.Holding<TFMaxHealthDrainHpMinConVar>(null),
                                                deployCost: DeclaredConVars.Holding<TFMaxHealthDrainDeployCostConVar>(null),
                                                clock: rig.Engine, equipHooks: new ListHooks<ITFWeaponEquipHook>());
            var players = new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, resupply, Conditions, rig.Carriers, rig.Items, new AttributeHooks());
            var owners = new FakeOwners();
            if (teleportAttrib)
            {
                owners.Held[PlayerRig.Index] = "tf_weapon_wrench";
                owners.Items[PlayerRig.Index] = AttribItem("alt_fire_teleport_to_spawn", 1f, itemQuality);
            }

            var voice = new TFConceptSpeech(Rules, rig.Carriers, Teams, rig.Engine,
                                            ObjectiveRig.ConceptSceneImage(), rig.Engine, new FakeRandom(),
                                            new LoadoutDeps.IdleRound(), owners, rig.Log);
            var command = new TFTauntCommand(players, rig.Engine, rig.Engine, rig.Engine, rig.Carriers, voice,
                                             owners, new AttributeHooks(), ObjectiveRig.ConceptSceneImage());
            Eureka = new TFEurekaTeleport(rig.Carriers, rig.Engine, rig.Engine, Teams, rig.Engine,
                                          new FakeEurekaBuildings(buildings), command,
                                          new ListHooks<ISpawnPlacement>(), owners, new AttributeHooks(),
                                          new TF2.Tests.Medigun.TFHealingCtf());
            Watch = new TFEurekaTauntWatch(Eureka, Conditions);
            Watch.Attach(PlayerRig.Player);
            rig.As(cls);
        }

        /// <summary>The press through the taunt's window and gone: the tick the cond is gone on.</summary>
        public void Expire()
        {
            Conditions.Of(PlayerRig.Index)!.Tick(TFTauntCommand.BaseTauntSceneSeconds + TFTauntCommand.TauntTailSeconds,
                                                 healers: 0, expiresFast: default);
            Watch.Tick(0f);
        }

        public void Press(string argument = "") => Eureka.Handle(PlayerRig.Player, TFEurekaTeleport.Command,
                                                                 argument.Length > 0 ? [argument] : []);
    }

    private static Harness NoExit(TFClass cls = TFClass.Engineer, bool teleportAttrib = true, string itemQuality = "unique")
        => new(cls, teleportAttrib, [], itemQuality);

    private static Harness WithExit(params TFBaseObject[] buildings) => new(TFClass.Engineer, true, buildings);

    [Fact]
    public void ThePressTauntsTheEurekaConcept()
    {
        Harness eureka = NoExit();

        eureka.Press();

        Assert.Equal(TFConceptSpeech.TauntEurekaEffect, eureka.Rules.Asked.Single().Concept);
        Assert.True(eureka.Rig.State.InCond(TFCond.Taunting));
    }

    [Fact]
    public void TheAskCarriesTheHeldItemsWords()
    {
        // The active weapon's item words (tf_player.cpp:19334-19339): the
        // quality-bearing Eureka names itself and its type on every ask — the
        // rule's WeaponIsEurekaEffect criterion reads item_name.
        Harness eureka = NoExit();

        eureka.Press();

        Assert.Contains(("item_name", "The Eureka Effect"), eureka.Rules.Words);
        Assert.Contains(("item_type_name", "#TF_Weapon_Wrench"), eureka.Rules.Words);
    }

    [Fact]
    public void APlainQualityItemNamesNothing()
    {
        // The gate is the quality (pItem->GetItemQuality() != AE_NORMAL): the
        // stock wrench, quality normal, rides no item words.
        Harness eureka = NoExit(teleportAttrib: true, itemQuality: "normal");

        eureka.Press();

        Assert.DoesNotContain(("item_name", "The Eureka Effect"), eureka.Rules.Words);
        Assert.DoesNotContain(("item_type_name", "#TF_Weapon_Wrench"), eureka.Rules.Words);
    }

    [Fact]
    public void AWeaponWithoutTheTeleportIsConsumedWithNothingBehindIt()
    {
        // The attrib ask on the held weapon alone (tf_player.cpp:7719-7722):
        // a wrench without the hook reads zero, and the command falls through
        // consumed, stock's return true.
        Harness eureka = NoExit(teleportAttrib: false);

        eureka.Press();

        Assert.Empty(eureka.Rules.Asked);
        Assert.False(eureka.Rig.State.InCond(TFCond.Taunting));
    }

    [Fact]
    public void ANonEngineerIsRefused()
    {
        Harness eureka = NoExit(TFClass.Soldier);

        eureka.Press();

        Assert.False(eureka.Rig.State.InCond(TFCond.Taunting));
    }

    [Fact]
    public void TheLandingIsTheSpawnSpotLiftedOneUnit()
    {
        Harness eureka = NoExit();
        eureka.Origins[Spot] = new Vector(5f, 6f, 50f);

        eureka.Press();
        eureka.Expire();

        EngineCall placed = Assert.Single(eureka.Rig.Engine.AllOf("Players.PlaceAtSpawn"));
        Assert.Equal(PlayerRig.Player, placed.ArgAs<EntityHandle>(0));
        Vector at = placed.ArgAs<Vector>(1);
        Assert.Equal(5f, at.X);
        Assert.Equal(6f, at.Y);
        Assert.Equal(51f, at.Z, 2);   // the ground drop's answer, then the one-unit lift
        EngineCall send = Assert.Single(eureka.Rig.Engine.AllOf("Sound.EntityEmitSound"),
                                        sent => sent.ArgAs<string>(1) == TFTeleporter.SendSound);
        Assert.Equal(TFTeleporter.SendSound, send.ArgAs<string>(1));
        Assert.Equal(1, eureka.Rig.Engine.CountOf("Players.BreakInterpolation"));
    }

    [Fact]
    public void TheLandingLandsOnce()
    {
        Harness eureka = NoExit();
        eureka.Press();
        eureka.Expire();
        int placed = eureka.Rig.Engine.CountOf("Players.PlaceAtSpawn");

        eureka.Watch.Tick(0f);
        eureka.Watch.Tick(1f);

        Assert.Equal(placed, eureka.Rig.Engine.CountOf("Players.PlaceAtSpawn"));
    }

    [Fact]
    public void TheExitsDeckIsTheArrival()
    {
        var (_, _, exit, _) = TeleporterHarness.Pair();
        Harness eureka = WithExit(exit);
        eureka.Origins[exit.Handle] = new Vector(10f, 20f, 30f);
        eureka.Angles[exit.Handle] = new Vector(0f, 90f, 0f);

        eureka.Press("1");
        eureka.Expire();

        EngineCall trip = Assert.Single(eureka.Rig.Engine.AllOf("Entities.Teleport"));
        Assert.Equal(PlayerRig.Player, trip.ArgAs<EntityHandle>(0));
        Vector at = trip.ArgAs<Vector>(1);
        Assert.Equal(10f, at.X);
        Assert.Equal(20f, at.Y);
        Assert.Equal(TFTeleporter.ArrivalHeight + 30f, at.Z, 2);   // the deck: maxs.z 12 plus the step
        Assert.True(trip.ArgAs<bool>(3));
        Assert.Equal(0, eureka.Rig.Engine.CountOf("Players.PlaceAtSpawn"));
    }

    [Fact]
    public void AnExitStillBuildingSendsYouHomeInstead()
    {
        var unbuilt = new BuildingsRig();
        var shell = unbuilt.Spawn<TFTeleporter>(TFTeleporter.MapClass);
        Assert.Equal(TFTeleporterState.Building, shell.State);

        Harness eureka = WithExit(shell);

        eureka.Press("1");
        eureka.Expire();

        Assert.Equal(1, eureka.Rig.Engine.CountOf("Players.PlaceAtSpawn"));
        Assert.Equal(0, eureka.Rig.Engine.CountOf("Entities.Teleport"));
    }

    [Fact]
    public void TheHomeTargetIgnoresABuiltExit()
    {
        var (_, _, exit, _) = TeleporterHarness.Pair();
        Harness eureka = WithExit(exit);

        eureka.Press();
        eureka.Expire();

        Assert.Equal(1, eureka.Rig.Engine.CountOf("Players.PlaceAtSpawn"));
        Assert.Equal(0, eureka.Rig.Engine.CountOf("Entities.Teleport"));
    }

    [Fact]
    public void ThePendingDiesWithThePlayer()
    {
        Harness eureka = NoExit();

        eureka.Press();
        eureka.Watch.Detach();
        eureka.Watch.Attach(PlayerRig.Player);
        eureka.Expire();

        Assert.Equal(0, eureka.Rig.Engine.CountOf("Players.PlaceAtSpawn"));
    }
}
