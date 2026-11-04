using System.Text;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Server.Entities.Items;
using TF2.Server.Medigun;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;

using TF2.Tests.Entities.Items;
using TF2.Tests.Medigun;
using TF2.Tests.Projectiles;
using TF2.Tests.Weapons;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace TF2.Tests.Player;

/// <summary>
/// What a TF2 death drops: the ammo pack, and never a weapon-classname entity.
///
/// <para>
/// The live bug: a death logged
/// <c>[SourceSharp] REFUSED: no entity factory answers to 'tf_weapon_rocketlauncher'</c>
/// — the core's HL2MP answer (<see cref="IPlayerDirectory.DropWeaponOnDeath"/>)
/// asked the native gate for the dead weapon's classname as an entity, and TF2
/// has no such entity: <c>CTFPlayer::Event_Killed</c> drops a
/// <see cref="TFAmmoPack"/> instead (tf_player.cpp:13103). The heaviest-weapon
/// scan that dresses it (tf_player.cpp:13029-13068), the ammo snapshot it
/// stores (tf_ammo_pack.cpp:136-145) and the one-oldest cleanup that keeps an
/// owner to three (tf_player.cpp:12975-13010) are the behaviour the hook facts
/// pin. The entity facts hold <c>PackTouch</c> (tf_ammo_pack.cpp:271-446):
/// the owner's flight gate, the picker's own-half restock, the lunchbox's
/// heal, the empty pack's nothing, and the gave-nothing-keeps-the-pack quirk.
/// </para>
/// </summary>
public sealed class TFDeathDropsTests
{
    private const string Rocket = "tf_weapon_rocketlauncher";
    private const string Fists = "tf_weapon_fists";
    private const string Shotgun = "tf_weapon_shotgun_soldier";
    private const string Scattergun = "tf_weapon_scattergun";
    private const string PistolScout = "tf_weapon_pistol_scout";
    private const string Bat = "tf_weapon_bat";
    private const string Medigun = "tf_weapon_medigun";

    private static readonly Vector DiedAt = new(10f, 20f, 30f);

    /// <summary>
    /// The lane's world: one strict recording engine and a registry for the
    /// Arsenal's carried pair (the answer set the core's proven arsenal rig
    /// uses), the map's TF players and the notices as fakes, the shipped
    /// scripts as the drop scan reads them, and a lazy directory that hands
    /// the hook a fresh, own-engined <see cref="TFAmmoPack"/> per create — so
    /// the cleanup fact can tell WHICH pack the removal named.
    /// </summary>
    private sealed class Rig
    {
        public readonly RecordingLog Log = new();
        public readonly RecordingEngine Engine = Mock().Ignores("Entities.IsValid")
            .Returns("Entities.GetIndex", 3)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            .Returns("Entities.GetEyeAngles", default(Vector)) // the Arsenal's deploy aim
            .Returns("EntityRender.LookupSequence", -1)         // the Arsenal's first Deploy
            .Returns("EntityRender.LookupSequenceByActivity", -1)
            .Returns("Clock.Now", 10f);
        public readonly ScriptedEntityRegistry Registry;
        public readonly SpawningEntities World;
        public readonly FakeMapPlayers Map = new();
        public readonly FakeNotices Notices = new();
        public readonly TFHealingStats Stats = new();
        public readonly ConVarRegistry ConVars;
        public readonly FakeSeeds Seeds = new();
        public readonly PackDirectory Packs = new();
        public readonly FakeMedigun Medigun = new();
        public readonly TFWeaponScripts Scripts;
        public readonly Arsenal Arsenal;
        public readonly Players Roster;
        public readonly (EntityHandle Handle, FakeMapPlayer Player) Dead;

        public Rig(string? schemaText = null)
        {
            Engine.Returns("Entities.IsValid", true)        // the pack's create gate says yes
                  .Returns("Entities.GetAngles", default(Vector))
                  // The drop's floor trace (Arsenal.DropPoint): a floor under
                  // the feet, so the dropped pickup is placed, not left in the air.
                  .Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 1f, new Vector(0f, 0f, 64f), Vector.Zero, EntityHandle.None))
                  .Returns("Entities.GetVelocity", new Vector(10f, 0f, 0f));

            Registry = new ScriptedEntityRegistry(TestEngines.Server());
            Registry.RegisterAll(typeof(SsWeapon).Assembly);
            Registry.Services = Services(out _);

            Dead = Map.Add();
            Registry.KeyValue(Dead.Handle, EntityNames.Player, "targetname", "fixture_player");

            World = new SpawningEntities(Engine, Registry);

            ConVars = ConVarRegistry.For(UnattachedConsole());
            ConVars.RegisterAll(typeof(TFAmmoPack).Assembly);

            var files = WeaponFixtures.ShippedWithClasses();
            // The scan's DontDrop arm only bites if the non-droppable row is
            // the heaviest it sees (:13049-13050 must beat the weight max):
            // fists ship at weight 1, under the rocket's 3, so this rig's
            // fists row is re-sealed at 4 — a synthetic condition for the
            // scan facts; the fixture's shipped text holds its own facts.
            files.Bytes[TFWeaponScripts.ScriptName(Fists) + ".ctx"] = WeaponFixtures.Seal(
                Encoding.UTF8.GetBytes(Regex.Replace(WeaponFixtures.Text(Fists),
                                                      "\"weight\"\\s+\"1\"", "\"weight\" \"4\"")));
            // The dress gate (tf_player.cpp:13084) reads the row's worldmodel,
            // which only the item schema carries: the trimmed shipped one, or
            // the fact's stripped variant.
            files.Bytes[ItemSchema.FilePath] =
                Encoding.UTF8.GetBytes(schemaText ?? EconTestFiles.Fixture("items_game.trimmed.txt"));
            Scripts = new TFWeaponScripts(Log, files);
            var types = new TFWeaponTypes(Scripts, new TFClassTable(Log, files), new ItemSchema(files, Log));
            Arsenal = new Arsenal(Log, World, Engine, Engine, Engine, Engine, Engine, Engine, Engine,
                                  Registry, Engine, new DamageHitGroups(),
                                  weapons: new WeaponCatalog(() => [types], Log),
                                  ammo: new AmmoCatalog(() => [new TFAmmoTypes()], Log));

            Roster = new Players(new RecordingLog(), Engine, Engine, Engine, NoButtons);
            Packs.Build = BuildPack;
            Packs.BuildDropped = BuildDropped;
            Packs.ClassOf = World.CreatedClassOf;
        }

        public TFDeathDrops Hook() => new(Log, Arsenal, Scripts, Map, Packs, Roster, World, Engine, Medigun);

        public PlayerDeath Death => new(Dead.Handle, 3, EntityHandle.None, DiedAt, EntityHandle.None);

        /// <summary>A pack built by hand on its own recording engine, already spawned.</summary>
        public (TFAmmoPack Pack, RecordingEngine Engine) BuildPack(float clock)
        {
            var engine = Mock();
            engine.Returns("Clock.Now", clock)
                  .Returns("Entities.IsValid", true)
                  .Returns("Entities.IsPlayer", true)
                  .Returns("Entities.GetEyeAngles", default(Vector))
                  .Returns("Entities.GetVelocity", default(Vector))
                  .Returns("Entities.GetAngularVelocity", default(Vector))
                  .Returns("EntityPhysics.InitPhysics", false);
            var pack = new TFAmmoPack(Map, Notices, ConVars.Declared<TFWeaponRagdollVelocityMinConVar>(),
                                      ConVars.Declared<TFWeaponRagdollVelocityMaxConVar>(),
                                      ConVars.Declared<TFWeaponRagdollMaxSpeedConVar>(), Seeds, engine, Stats);
            pack.BindEngine(engine);
            pack.BindAreas(TestServices.Areas(engine));
            pack.RegisteredKind = ScriptedEntityKind.Prop;
            // The clock its Spawn stamps m_flCreationTime with: an owner
            // registry whose container's IGameClock is a stub answering THIS
            // pack's clock — the game's own registry owner gives an entity
            // its Now (ScriptedEntity.GameTime => Owner?.Now).
            pack.Owner = OwnerFor(clock);
            pack.OnSpawn();
            return (pack, engine);
        }

        /// <summary>A dropped weapon built by hand on its own recording engine, already spawned.</summary>
        public (TFDroppedWeapon Dropped, RecordingEngine Engine) BuildDropped(float clock)
        {
            var engine = Mock();
            engine.Returns("Clock.Now", clock)
                  .Returns("Entities.IsValid", true)
                  .Returns("Entities.GetEyeAngles", default(Vector))
                  .Returns("Entities.GetVelocity", default(Vector))
                  .Returns("Entities.GetAngularVelocity", default(Vector))
                  .Returns("EntityPhysics.InitPhysics", false);
            var dropped = new TFDroppedWeapon(ConVars.Declared<TFDroppedWeaponLifetimeConVar>(), Seeds, engine);
            dropped.BindEngine(engine);
            dropped.BindAreas(TestServices.Areas(engine));
            dropped.RegisteredKind = ScriptedEntityKind.Prop;
            dropped.Owner = OwnerFor(clock);
            dropped.OnSpawn();
            return (dropped, engine);
        }

        /// <summary>A registry whose container answers <c>Now</c> as the pack's clock.</summary>
        private static ScriptedEntityRegistry OwnerFor(float clock)
        {
            var services = TestServices.Services(out var clockEngine);
            clockEngine.Returns("Clock.Now", clock);
            return new ScriptedEntityRegistry(TestEngines.Server()) { Services = services };
        }

        public void Give(EntityHandle player, params string[] weapons)
        {
            foreach (var weapon in weapons)
                Arsenal.GiveWeapon(player, weapon);
        }

        public void GiveAmmo(EntityHandle player, int primary, int secondary, int metal)
        {
            Arsenal.GiveAmmo(player, primary, TFAmmoNames.Name(TFAmmo.Primary));
            Arsenal.GiveAmmo(player, secondary, TFAmmoNames.Name(TFAmmo.Secondary));
            Arsenal.GiveAmmo(player, metal, TFAmmoNames.Name(TFAmmo.Metal));
        }

        public bool Logged(string needle) => Log.Lines.Any(l => l.Text.Contains(needle));
    }

    /// <summary>
    /// The hook's view of the managed entities: a fresh own-engined pack or
    /// dropped weapon per handle the hook creates (the game's registry spawn
    /// event makes it and this is that event's stand-in — the created
    /// handle's class names which), plus the fact's pre-seeded ones. The
    /// per-entity engine is the name: hand-built entities share handle None,
    /// so only the engine can say which one the removal took.
    /// </summary>
    private sealed class PackDirectory : IEntityDirectory
    {
        public readonly List<TFAmmoPack> Packs = new();
        public readonly Dictionary<TFAmmoPack, RecordingEngine> Engines = new();
        public readonly List<TFDroppedWeapon> Dropped = new();
        public readonly Dictionary<TFDroppedWeapon, RecordingEngine> DroppedEngines = new();
        public Func<float, (TFAmmoPack Pack, RecordingEngine Engine)>? Build;
        public Func<float, (TFDroppedWeapon Dropped, RecordingEngine Engine)>? BuildDropped;

        /// <summary>The class a created handle carries — the SpawningEntities double's book.</summary>
        public Func<EntityHandle, string?>? ClassOf;

        /// <summary>The clock the hook's own entities spawn at — newest, so cleanup never names them.</summary>
        public float NextClock = 500f;

        private readonly Dictionary<uint, ScriptedEntity> _known = new();
        private uint _nextSeed = 0x0900;

        public TFAmmoPack Seed(EntityHandle owner, float clock)
        {
            var (pack, engine) = Build!.Invoke(clock);
            pack.Init(owner, (int)TFWeaponId.Rocketlauncher, TFClass.Soldier, 5, 5, 5);
            Packs.Add(pack);
            Engines[pack] = engine;
            _known[_nextSeed++] = pack;
            return pack;
        }

        /// <summary>A dropped weapon the dedupe and cap will see, hand-inited.</summary>
        public TFDroppedWeapon SeedDropped(EntityHandle owner, string weapon, float clock)
        {
            var (dropped, engine) = BuildDropped!.Invoke(clock);
            dropped.Init(owner, weapon, 4, 12);
            Dropped.Add(dropped);
            DroppedEngines[dropped] = engine;
            _known[_nextSeed++] = dropped;
            return dropped;
        }

        public ScriptedEntity? Get(EntityHandle handle)
        {
            if (_known.TryGetValue(handle.Value, out var known))
                return known;

            if (ClassOf?.Invoke(handle) == TFDroppedWeapon.Class)
            {
                var (dropped, droppedEngine) = BuildDropped!.Invoke(NextClock);
                Dropped.Add(dropped);
                DroppedEngines[dropped] = droppedEngine;
                _known[handle.Value] = dropped;
                return dropped;
            }

            var (pack, engine) = Build!.Invoke(NextClock);
            _known[handle.Value] = pack;
            Packs.Add(pack);
            Engines[pack] = engine;
            return pack;
        }

        public bool Removed(TFAmmoPack pack) => Engines[pack].Calls.Any(c => c.Member == "Entities.Remove");
        public bool RemovedDropped(TFDroppedWeapon dropped) =>
            DroppedEngines[dropped].Calls.Any(c => c.Member == "Entities.Remove");

        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => className switch
        {
            TFAmmoPack.Class => Packs,
            TFDroppedWeapon.Class => Dropped,
            _ => [],
        };
        public bool IsRegistered(string className) => className is TFAmmoPack.Class or TFDroppedWeapon.Class;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>
    /// The throw's stream (<see cref="IUniformRandom"/> — the engine's
    /// <c>random</c>, as the stock throw draws from it): scripted fractions,
    /// so a fact's bounds never depend on a lucky draw. <c>InitAmmoPack</c>'s
    /// four draws come in order — the up jitter, the right jitter, the
    /// ragdoll-band speed, the yaw spin — and the plan cycles, so a fact that
    /// builds two packs never runs out.
    /// </summary>
    private sealed class FakeSeeds : IUniformRandom
    {
        private readonly float[] _fractions = [0.9f, 0.3f, 0.5f, 0.5f];
        private int _at;

        public float RandomFloat(float min, float max) => min + (max - min) * _fractions[_at++ % _fractions.Length];
        public int RandomInt(int min, int max) => min + (int)((max - min) * _fractions[_at++ % _fractions.Length]);
    }

    /// <summary>The medigun seam, as the death hook reads it: the meter, nothing else.</summary>
    private sealed class FakeMedigun : ITFMedigunWeapons
    {
        public float Charge;

        public float ChargeLevel(int ownerIndex) => Charge;
        public void SetChargeLevel(int ownerIndex, float level) { }
        public void TickMedic(int ownerIndex, float now, float frametime) { }
        public void Holster(int ownerIndex, float now) { }
        public void Unholster(int ownerIndex, float now) { }
        public void RemoveMedigun(int ownerIndex, float now) { }
        public int HealingTargetIndex(int ownerIndex) => -1;
        public bool IsReleasing(int ownerIndex) => false;
    }

    //------------------------------------------------------------------
    // The hook: the pack a death leaves, and the weapon entity it must not make.
    //------------------------------------------------------------------

    [Fact]
    public void The_level_init_switches_the_weapons_drop_off()
    {
        var rig = new Rig();
        Assert.True(rig.Roster.DropWeaponOnDeath); // the core's HL2MP answer, until TF2 says otherwise

        ((IGameLifecycle)rig.Hook()).OnLevelInit("tc_2fort");

        Assert.False(rig.Roster.DropWeaponOnDeath);
    }

    [Fact]
    public void Without_the_switch_a_death_still_drops_the_weapon()
    {
        // The positive control for the switch above: the core's answer is live
        // until TF2's level init turns it off — it asks the engine to drop the
        // active weapon, the very call whose arsenal decorator made the
        // REFUSED line the live game logged.
        var engine = Mock().Ignores("Players.DropActiveWeapon")
                           .Returns("Entities.IsPlayer", true)
                           .Returns("Entities.GetEyeAngles", default(Vector)); // the throw's aim, before the drop call
        var players = new Players(new RecordingLog(), engine, engine, engine, NoButtons);
        Assert.True(players.DropWeaponOnDeath);

        players.OnKilled(new EntityHandle(0x0001_0001u), new EntityHandle(0x0001_0002u), 10f);

        Assert.Equal(1, engine.CountOf("Players.DropActiveWeapon"));
    }

    [Fact]
    public void A_death_makes_the_ammo_pack_and_never_a_weapon_entity()
    {
        var rig = new Rig();
        rig.Give(rig.Dead.Handle, Rocket);
        rig.GiveAmmo(rig.Dead.Handle, 30, 12, 200);

        // The whole death chain, not just the hook: PlayerDeaths makes the
        // body, updates the record, and then calls the hook — and with the
        // switch off, the record's own weapon drop must not fire.
        var engineC = Mock();
        engineC.Returns("EntityRender.GetModelName", "models/humans/group03/male_01.mdl")
               .Returns("Entities.GetOrigin", DiedAt)
               .Returns("Entities.GetAngles", default(Vector))
               .Returns("Entities.GetEyeAngles", default(Vector))
               .Returns("Entities.GetVelocity", default(Vector))
               .Returns("Entities.Create", new EntityHandle(99u))
               .Returns("Entities.IsValid", true)
               .Returns("Entities.GetIndex", 3)
               .Returns("Entities.IsPlayer", true)
               .Returns("Players.GetUserId", 0)
               .Returns("Players.GetActiveWeapon", string.Empty)
               .Returns("GameEvents.EventCreate", IntPtr.Zero)
               .Returns("Clock.Now", 10f);
        engineC.Ignores("Players.DropActiveWeapon");
        var body = new SsRagdoll(engineC) { Handle = new EntityHandle(99u) };
        body.BindEngine(engineC);
        body.State.BindLocal();
        var corpses = new Corpses(new RecordingLog(), engineC, engineC, new Bodies(body), engineC);
        var playersC = new Players(new RecordingLog(), engineC, engineC, engineC, NoButtons);
        var hook = new TFDeathDrops(rig.Log, rig.Arsenal, rig.Scripts, rig.Map, rig.Packs, playersC, rig.World, rig.Engine, rig.Medigun);
        ((IGameLifecycle)hook).OnLevelInit("tc_2fort");
        var chain = new PlayerDeaths(new RecordingLog(), engineC, corpses, playersC,
                                     engineC, new ListHooks<IPlayerDeathHook>(hook),
                                     new DeathNotice(engineC, engineC, engineC, new DamageInflictors()),
                                     engineC);

        chain.OnKilled(rig.Dead.Handle, new EntityHandle(0x0001_0002u));

        Assert.Equal(0, engineC.CountOf("Players.DropActiveWeapon"));
        Assert.Contains(TFAmmoPack.Class, rig.World.CreatedClasses);
        Assert.DoesNotContain(rig.World.CreatedClasses, c => c.StartsWith("tf_weapon_", StringComparison.Ordinal));
        var pack = Assert.Single(rig.Packs.Packs);
        Assert.Equal(rig.Dead.Handle, pack.PackOwner);
    }

    [Fact]
    public void The_scout_who_dies_with_the_core_drop_live_never_names_a_tf_weapon_to_the_engine_and_the_respawn_arms_him()
    {
        // The same death again WITHOUT the switch — the live state the team
        // change leaves when the drop is asked of the core — but with the
        // drop's own create arm in view, which the switch hides. The live
        // report: a scout carrying the intelligence changed team (the
        // change-team kill lands on this same PlayerDeaths.OnKilled through
        // ServerModule.OnPlayerKilled) and his respawn logged
        // "[SourceSharp] could not create 'tf_weapon_scattergun' — no such
        // entity class": the core's drop walked the arsenal decorator into
        // asking the engine for an ENTITY named after the weapon ROW — right
        // for a weapon_smg1, which is a CBaseEntity class, wrong for every TF
        // weapon, whose row name is an item class with no entity (stock's
        // death drop is the ammo pack, which TFDeathDrops owns). The rows
        // answer it themselves now (TFWeaponTypes.Row: RemovedOnDrop), so the
        // engine is never asked, whatever else toggles the drop; the respawn's
        // kit — the production loop GameRules.OnPlayerSpawned runs per weapon,
        // the decorated GiveItem — must still put the scattergun in his hands.
        var rig = new Rig();

        // The kit, given the way the rules give it: through the decorator,
        // primary first as the loadout lists it (the first given is drawn).
        var decor = new ArsenalPlayers(rig.Engine, rig.Arsenal);
        foreach (var weapon in new[] { Scattergun, PistolScout, Bat })
            Assert.True(decor.GiveItem(rig.Dead.Handle, weapon),
                        $"the arsenal should own {weapon}, not fall through to the engine");
        rig.GiveAmmo(rig.Dead.Handle, 32, 36, 100);

        var engineC = Mock();
        engineC.Returns("EntityRender.GetModelName", "models/humans/group03/male_01.mdl")
               .Returns("Entities.GetOrigin", DiedAt)
               .Returns("Entities.GetAngles", default(Vector))
               .Returns("Entities.GetEyeAngles", default(Vector))
               .Returns("Entities.GetVelocity", default(Vector))
               .Returns("Entities.Create", new EntityHandle(99u))
               .Returns("Entities.IsValid", true)
               .Returns("Entities.GetIndex", 3)
               .Returns("Entities.IsPlayer", true)
               .Returns("Players.GetUserId", 0)
               .Returns("Players.GetActiveWeapon", string.Empty)
               .Returns("GameEvents.EventCreate", IntPtr.Zero)
               .Returns("Clock.Now", 10f);
        var chainPlayers = new Players(new RecordingLog(), engineC, decor, engineC, NoButtons);
        Assert.True(chainPlayers.DropWeaponOnDeath); // the state the report observed: the drop is live
        var hook = new TFDeathDrops(rig.Log, rig.Arsenal, rig.Scripts, rig.Map, rig.Packs, chainPlayers, rig.World, rig.Engine, rig.Medigun);
        var body = new SsRagdoll(engineC) { Handle = new EntityHandle(99u) };
        body.BindEngine(engineC);
        body.State.BindLocal();
        var corpses = new Corpses(new RecordingLog(), engineC, engineC, new Bodies(body), engineC);
        var chain = new PlayerDeaths(new RecordingLog(), engineC, corpses, chainPlayers,
                                     engineC, new ListHooks<IPlayerDeathHook>(hook),
                                     new DeathNotice(engineC, engineC, engineC, new DamageInflictors()),
                                     decor);

        chain.OnKilled(rig.Dead.Handle, new EntityHandle(0x0001_0002u));

        // The chain ran (the pack says so), the death's strip went through the
        // decorator (Strip ran: the arsenal answers a hierarchy player), and
        // no create-by-name ever named a TF weapon's ROW at the engine.
        Assert.Contains(TFAmmoPack.Class, rig.World.CreatedClasses);
        Assert.DoesNotContain(rig.World.CreatedClasses, c => c.StartsWith("tf_weapon_", StringComparison.Ordinal));

        // The respawn, as GameRules.OnPlayerSpawned arms it: the loadout
        // through the decorated give, and the decorated read says he holds
        // the scattergun.
        foreach (var weapon in new[] { Scattergun, PistolScout, Bat })
            Assert.True(decor.GiveItem(rig.Dead.Handle, weapon));
        Assert.Equal(Scattergun, decor.GetActiveWeapon(rig.Dead.Handle));
    }

    [Fact]
    public void A_player_carrying_nothing_drops_nothing()
    {
        // Stock's bail on !pWeapon (tf_player.cpp:13067): no weapon found,
        // no pack at all.
        var rig = new Rig();
        rig.Hook().OnPlayerDied(rig.Death);

        Assert.Empty(rig.Packs.Packs);
        Assert.DoesNotContain(TFAmmoPack.Class, rig.World.CreatedClasses);
    }

    [Fact]
    public void The_pack_stores_the_dead_player_s_ammo_snapshot()
    {
        var rig = new Rig();
        rig.Give(rig.Dead.Handle, Rocket);
        rig.GiveAmmo(rig.Dead.Handle, 30, 12, 200);

        rig.Hook().OnPlayerDied(rig.Death);

        var pack = Assert.Single(rig.Packs.Packs);
        Assert.Equal(30, pack.AmmoPrimary);
        Assert.Equal(12, pack.AmmoSecondary);
        Assert.Equal(100, pack.AmmoMetal); // Clamp(count, 5, 100), tf_ammo_pack.cpp:139
        Assert.Equal(0, pack.AmmoGrenades1);
    }

    [Fact]
    public void The_pack_stores_at_least_five_of_each()
    {
        // Max(5, count) and the metal clamp's floor (tf_ammo_pack.cpp:137-139):
        // an empty reserve still fills the pack with the stock minimum.
        var rig = new Rig();
        rig.Give(rig.Dead.Handle, Rocket);
        rig.GiveAmmo(rig.Dead.Handle, 0, 0, 0);

        rig.Hook().OnPlayerDied(rig.Death);

        var pack = Assert.Single(rig.Packs.Packs);
        Assert.Equal(5, pack.AmmoPrimary);
        Assert.Equal(5, pack.AmmoSecondary);
        Assert.Equal(5, pack.AmmoMetal);
    }

    [Fact]
    public void The_active_weapon_dresses_the_pack_though_a_heavier_one_waits()
    {
        // Stock asks the active weapon first (tf_player.cpp:13034, :13061-13064):
        // the shotgun in hand dresses the pack even though the rocket (3)
        // outweighs it (2).
        var rig = new Rig();
        rig.Give(rig.Dead.Handle, Shotgun, Rocket);

        rig.Hook().OnPlayerDied(rig.Death);

        Assert.Single(rig.Packs.Packs);
        Assert.True(rig.Logged(Shotgun), $"the pack should be dressed as the active {Shotgun}: {rig.Log.Text}");
    }

    [Fact]
    public void An_active_weapon_that_does_not_drop_sends_the_scan_to_the_heaviest()
    {
        // The fists carry m_bDontDrop (tf_player.cpp:13036): the scan runs and
        // the rocket (weight 3) wins the dress.
        var rig = new Rig();
        rig.Give(rig.Dead.Handle, Fists, Rocket);

        rig.Hook().OnPlayerDied(rig.Death);

        Assert.Single(rig.Packs.Packs);
        Assert.True(rig.Logged(Rocket), $"the pack should be dressed as the heaviest {Rocket}: {rig.Log.Text}");
    }

    [Fact]
    public void The_scan_takes_the_heaviest_whatever_order_the_inventory_walks_in()
    {
        // The weight scan is a max, not a first-found (tf_player.cpp:13040-13058,
        // iWeight from -1 and a strict >): a later lighter row may not take
        // the dress, and a later heavier row must.
        var first = new Rig();
        first.Give(first.Dead.Handle, Fists, Shotgun, Rocket);
        first.Hook().OnPlayerDied(first.Death);
        Assert.True(first.Logged(Rocket), $"heavier-later must win: {first.Log.Text}");

        var second = new Rig();
        second.Give(second.Dead.Handle, Fists, Rocket, Shotgun);
        second.Hook().OnPlayerDied(second.Death);
        Assert.True(second.Logged(Rocket), $"lighter-later must not win: {second.Log.Text}");
    }

    [Fact]
    public void A_blue_death_dresses_its_pack_in_blue()
    {
        // The dress's skin (DropAmmoPack :13077-13082 over CTFWeaponBase::GetSkin's
        // team fallback, tf_weaponbase.cpp:4924-4938): the dying team's colour —
        // TF_TEAM_BLUE is skin 1 — reaches the pack's m_nSkin and its render.
        var rig = new Rig();
        var (blue, _) = rig.Map.Add(TFTeam.Blue, TFClass.Soldier);
        rig.Registry.KeyValue(blue, EntityNames.Player, "targetname", "blue_fixture");
        rig.Give(blue, Rocket);

        rig.Hook().OnPlayerDied(new PlayerDeath(blue, 3, EntityHandle.None, DiedAt, EntityHandle.None));

        var pack = Assert.Single(rig.Packs.Packs);
        Assert.Equal(1, pack.Skin);
        Assert.Equal(1, rig.Packs.Engines[pack].Last("EntityRender.SetSkin").ArgAs<int>(1));
    }

    //------------------------------------------------------------------
    // The weapon itself: the create beside the pack (tf_player.cpp:13096-13100).
    //------------------------------------------------------------------

    [Fact]
    public void A_death_leaves_the_weapon_itself_beside_the_pack()
    {
        // The create pair (tf_player.cpp:13096-13107): CTFDroppedWeapon::Create
        // FIRST, the pack after it — the weapon wearing the dress's worldmodel
        // (:13076, :13099) at the death spot, carrying the clip it died with
        // and the primary reserve its owner held (InitDroppedWeapon :566-569).
        var rig = new Rig();
        rig.Give(rig.Dead.Handle, Rocket);
        rig.GiveAmmo(rig.Dead.Handle, 30, 12, 200);

        rig.Hook().OnPlayerDied(rig.Death);

        var order = rig.World.CreatedClasses.ToList();
        Assert.True(order.IndexOf(TFDroppedWeapon.Class) < order.IndexOf(TFAmmoPack.Class),
                    "the weapon spawns before the pack (:13096 before :13103)");
        var dropped = Assert.Single(rig.Packs.Dropped);
        Assert.Equal(rig.Dead.Handle, dropped.LastOwner);
        Assert.Equal(Rocket, dropped.Weapon);
        Assert.Equal(rig.Arsenal.Weapons.For(Rocket)!.Value.WorldModel,
                     rig.Engine.Last("EntityRender.SetModel").ArgAs<string>(1));
        Assert.Equal(rig.Arsenal.Holdings(rig.Dead.Handle).First(h => h.ClassName == Rocket).Clip, dropped.Clip);
        Assert.Equal(30, dropped.Ammo);
        var origins = rig.Engine.Calls.Where(c => c.Member == "Entities.SetOrigin")
                                      .Select(c => c.ArgAs<Vector>(1)).ToList();
        Assert.Equal(new[] { DiedAt, DiedAt }, origins.TakeLast(2)); // the creates'; earlier ones are the rig's
    }

    [Fact]
    public void A_blue_death_dresses_its_dropped_weapon_in_blue_too()
    {
        // The one skin read serves both creates (:13077-13082 → :13099): the
        // dying team's colour reaches the dropped weapon's render too.
        var rig = new Rig();
        var (blue, _) = rig.Map.Add(TFTeam.Blue, TFClass.Soldier);
        rig.Registry.KeyValue(blue, EntityNames.Player, "targetname", "blue_fixture");
        rig.Give(blue, Rocket);

        rig.Hook().OnPlayerDied(new PlayerDeath(blue, 3, EntityHandle.None, DiedAt, EntityHandle.None));

        var dropped = Assert.Single(rig.Packs.Dropped);
        Assert.Equal(1, dropped.Skin);
        // The skin rides the entity's own render, as the pack's does its own.
        Assert.Equal(1, rig.Packs.DroppedEngines[dropped].Last("EntityRender.SetSkin").ArgAs<int>(1));
    }

    [Fact]
    public void The_dropped_medigun_carries_the_medic_s_charge()
    {
        // InitDroppedWeapon's medigun arm (:588-601), the meter read at the
        // death — before the RemoveMedigun the same death later fires.
        var rig = new Rig();
        rig.Medigun.Charge = 0.42f;
        rig.Give(rig.Dead.Handle, Medigun);

        rig.Hook().OnPlayerDied(rig.Death);

        Assert.Equal(0.42f, Assert.Single(rig.Packs.Dropped).ChargeLevel, 3);
    }

    [Fact]
    public void A_suicide_drops_the_medigun_drained()
    {
        // bIsSuicide (tf_player.cpp:13094, a killer on the dead's own team)
        // zeroes the meter the drop carries (:588-591).
        var rig = new Rig();
        rig.Medigun.Charge = 0.42f;
        rig.Give(rig.Dead.Handle, Medigun);

        rig.Hook().OnPlayerDied(rig.Death with { Attacker = rig.Dead.Handle });

        Assert.Equal(0f, Assert.Single(rig.Packs.Dropped).ChargeLevel, 3);
    }

    [Fact]
    public void A_second_lie_of_the_same_weapon_takes_the_first_away()
    {
        // CTFDroppedWeapon::Create's dedupe (:495-503): one weapon of this
        // owner's already lying there and the new one replaces it — another
        // weapon of the same owner, or a stranger's lie, stands.
        var rig = new Rig();
        rig.Give(rig.Dead.Handle, Rocket);
        var first = rig.Packs.SeedDropped(rig.Dead.Handle, Rocket, clock: 1f);
        var otherWeapon = rig.Packs.SeedDropped(rig.Dead.Handle, Bat, clock: 2f);

        rig.Hook().OnPlayerDied(rig.Death);

        Assert.True(rig.Packs.RemovedDropped(first));
        Assert.False(rig.Packs.RemovedDropped(otherWeapon));
    }

    [Fact]
    public void The_floor_past_two_and_thirty_takes_the_earliest_lie()
    {
        // The cap (:506-517): past MAX_DROPPED_WEAPON_COUNT lying, the
        // listing's earliest go — the deduped excepted (:512), the fresh
        // create after the sweep.
        var rig = new Rig();
        rig.Give(rig.Dead.Handle, Rocket);
        var seeded = new List<TFDroppedWeapon>();
        for (int at = 0; at <= TFDroppedWeapon.MaxDroppedCount; at++) // one past the floor
            seeded.Add(rig.Packs.SeedDropped(new EntityHandle((uint)(0x2000 + at)), Bat, clock: 1f + at));

        rig.Hook().OnPlayerDied(rig.Death);

        Assert.True(rig.Packs.RemovedDropped(seeded[0]));
        Assert.False(rig.Packs.RemovedDropped(seeded[1]));
        Assert.False(rig.Packs.RemovedDropped(rig.Packs.Dropped[^1])); // the fresh one stands
    }

    [Fact]
    public void A_dress_without_a_worldmodel_ends_the_whole_drop()
    {
        // The one return (tf_player.cpp:13084-13085): a dress with no
        // worldmodel ends the drop — the pack never spawns either.
        string stripped = Regex.Replace(EconTestFiles.Fixture("items_game.trimmed.txt"),
            "\\n[ \\t]*\"model_player\"[ \\t]+\"models/weapons/c_models/c_rocketlauncher/c_rocketlauncher.mdl\"",
            string.Empty);
        var rig = new Rig(stripped);
        rig.Give(rig.Dead.Handle, Rocket);

        rig.Hook().OnPlayerDied(rig.Death);

        Assert.Empty(rig.Packs.Packs);
        Assert.Empty(rig.Packs.Dropped);
        Assert.DoesNotContain(TFDroppedWeapon.Class, rig.World.CreatedClasses);
        Assert.DoesNotContain(TFAmmoPack.Class, rig.World.CreatedClasses);
    }

    [Fact]
    public void A_fourth_pack_takes_the_oldest_of_the_owner_s_three_away()
    {
        // AmmoPackCleanUp (tf_player.cpp:12975-13010): more than three of the
        // same owner and the ONE oldest goes — not the second, not a
        // stranger's, and not the fresh one.
        var rig = new Rig();
        rig.Give(rig.Dead.Handle, Rocket);
        var oldest = rig.Packs.Seed(rig.Dead.Handle, clock: 1f);
        var second = rig.Packs.Seed(rig.Dead.Handle, clock: 2f);
        var third = rig.Packs.Seed(rig.Dead.Handle, clock: 3f);
        var (other, _) = rig.Map.Add();
        var stranger = rig.Packs.Seed(other, clock: 0.5f);

        rig.Hook().OnPlayerDied(rig.Death);

        Assert.Equal(5, rig.Packs.Packs.Count); // four of the owner (3 seeded + the drop) and a stranger's
        Assert.True(rig.Packs.Removed(oldest), "the owner's oldest pack should be gone");
        Assert.False(rig.Packs.Removed(second));
        Assert.False(rig.Packs.Removed(third));
        Assert.False(rig.Packs.Removed(stranger));
        var fresh = rig.Packs.Packs[^1];
        Assert.False(rig.Packs.Removed(fresh)); // the one this death made stands
    }

    //------------------------------------------------------------------
    // The entity: Spawn, InitAmmoPack, FlyThink and PackTouch.
    //------------------------------------------------------------------

    [Fact]
    public void The_pack_wears_the_medium_model_and_is_scheduled_to_think_and_die()
    {
        var rig = new Rig();
        var (pack, engine) = rig.BuildPack(10f);

        Assert.Equal(TFAmmoPack.Model, engine.Last("EntityRender.SetModel").ArgAs<string>(1)); // :46, :81
        // The Debris group (tf_ammo_pack.cpp:184) is born with the solver
        // object: it lands at spawn, where the Prop kind hands that object over.
        Assert.Equal(CollisionGroup.Debris, engine.Last("EntityPhysics.SetCollisionGroup").ArgAs<CollisionGroup>(1));
        Assert.Equal(0.75f, pack.NextThinkDelay(), 3);            // FlyThink (:49-50)
        Assert.Equal(30f, pack.NextThinkDelay("DieContext"), 3);  // :71
        Assert.Equal(10f, pack.CreationTime);                      // :54
        Assert.False(pack.AllowOwnerPickup);                       // :60 — no pickup until FlyThink
        Assert.False(pack.NoPickup);
        Assert.False(pack.HealthInstead);
        Assert.False(pack.EmptyPack);
        Assert.Equal(0.5f, pack.AmmoRatio);                        // :57
    }

    [Fact]
    public void The_init_throws_it_off_the_corpse_within_the_ragdoll_band()
    {
        var rig = new Rig();
        var (pack, engine) = rig.BuildPack(10f);

        pack.Init(rig.Dead.Handle, (int)TFWeaponId.Rocketlauncher, TFClass.Soldier,
                  30, 12, 200, skin: 7);

        Assert.Equal(30, pack.AmmoPrimary);
        Assert.Equal(12, pack.AmmoSecondary);
        Assert.Equal(100, pack.AmmoMetal);
        Assert.Equal(25f, engine.Last("EntityPhysics.SetMass").ArgAs<float>(1), 3); // :174
        var thrown = engine.Last("Entities.SetVelocity").ArgAs<Vector>(1);
        // The unit impulse scaled at the planned 0.5 draw to the middle of the
        // ragdoll band [100,150], riding on the pack's own (still zero) velocity (:153-172).
        Assert.Equal(125f, thrown.Length, 3);
        var spin = engine.Last("Entities.SetAngularVelocity").ArgAs<Vector>(1);
        Assert.Equal(50f, spin.Y, 3); // :175 — 0.5 of the [0,100] yaw band
        Assert.Equal(900, engine.Last("Entities.SetHealth").ArgAs<int>(1)); // :186
        Assert.Equal(7, pack.Skin);                                          // :181
        Assert.Equal(7, engine.Last("EntityRender.SetSkin").ArgAs<int>(1));

        // The cap (tf_weapon_ragdoll_maxspeed, :164-169): a carry big enough
        // to blow past maxspeed must leave the throw AT maxspeed — without
        // the cap this carry lands at 450+ and goes red.
        var (fast, fastEngine) = rig.BuildPack(10f);
        fastEngine.Returns("Entities.GetVelocity", new Vector(0f, 0f, 450f));
        fast.Init(rig.Dead.Handle, (int)TFWeaponId.Rocketlauncher, TFClass.Soldier, 5, 5, 5);
        Assert.True(fastEngine.Last("Entities.SetVelocity").ArgAs<Vector>(1).Length <= 300.01f,
                    "the throw must respect tf_weapon_ragdoll_maxspeed");

        // The band IS the convars (tf_ammo_pack.cpp:112-114, read live at :161):
        // move the band and the throw moves with it — a hardcoded speed would
        // sit at 125 and go red here.
        rig.ConVars.Declared<TFWeaponRagdollVelocityMinConVar>().Bind(-1, "tf_weapon_ragdoll_velocity_min", "200");
        rig.ConVars.Declared<TFWeaponRagdollVelocityMaxConVar>().Bind(-1, "tf_weapon_ragdoll_velocity_max", "210");
        var (moved, movedEngine) = rig.BuildPack(10f);
        moved.Init(rig.Dead.Handle, (int)TFWeaponId.Rocketlauncher, TFClass.Soldier, 5, 5, 5);
        Assert.Equal(205f, movedEngine.Last("Entities.SetVelocity").ArgAs<Vector>(1).Length, 3);
    }

    [Fact]
    public void The_owner_cannot_take_its_own_pack_until_it_lands()
    {
        var rig = new Rig();
        var owner = rig.Dead;
        owner.Player.MaxAmmo[TFAmmo.Primary] = 80;
        owner.Player.MaxAmmo[TFAmmo.Secondary] = 40;
        var (pack, _) = rig.BuildPack(10f);
        pack.Init(owner.Handle, (int)TFWeaponId.Rocketlauncher, TFClass.Soldier,
                  5, 5, 5);

        pack.OnTouch(owner.Handle); // still flying (:293)
        Assert.Empty(owner.Player.Ammo);
        Assert.Empty(rig.Notices.Said);

        pack.OnThink(); // FlyThink (:265-269): it has landed
        Assert.True(pack.AllowOwnerPickup);

        pack.OnTouch(owner.Handle);
        Assert.Equal(40, owner.Player.Ammo[TFAmmo.Primary]);   // ceil(80 * 0.5)
        Assert.Equal(20, owner.Player.Ammo[TFAmmo.Secondary]); // ceil(40 * 0.5)
    }

    [Fact]
    public void A_dead_player_takes_nothing()
    {
        var rig = new Rig();
        var (corpse, player) = rig.Map.Add();
        player.Alive = false; // :285
        var (pack, engine) = rig.BuildPack(10f);
        pack.Init(rig.Dead.Handle, (int)TFWeaponId.Rocketlauncher, TFClass.Soldier,
                  5, 5, 5);

        pack.OnTouch(corpse);

        Assert.Empty(rig.Notices.Said);
        Assert.DoesNotContain("Entities.Remove", engine.Calls.Select(c => c.Member));
    }

    [Fact]
    public void A_prop_rolling_over_the_pack_takes_nothing()
    {
        var rig = new Rig();
        var (pack, engine) = rig.BuildPack(10f);
        engine.Returns("Entities.IsPlayer", false); // :282
        pack.Init(rig.Dead.Handle, (int)TFWeaponId.Rocketlauncher, TFClass.Soldier,
                  5, 5, 5);

        pack.OnTouch(new EntityHandle(0x0700u)); // a prop: not a player at all

        Assert.Empty(rig.Notices.Said);
        Assert.DoesNotContain("Entities.Remove", engine.Calls.Select(c => c.Member));
    }

    [Fact]
    public void The_pack_restocks_the_picker_to_their_own_half()
    {
        // PackTouch's recalc (:347-351): ceil of the PICKER's maximum times
        // the ratio, stored as the pack's own and then walked out of the loop
        // (:355-363) — and the metal snapshot rides along.
        var rig = new Rig();
        var (toucher, player) = rig.Map.Add();
        player.MaxAmmo[TFAmmo.Primary] = 80;
        player.MaxAmmo[TFAmmo.Secondary] = 40;
        player.MaxAmmo[TFAmmo.Metal] = 200;
        var (pack, engine) = rig.BuildPack(10f);
        pack.Init(rig.Dead.Handle, (int)TFWeaponId.Rocketlauncher, TFClass.Soldier,
                  30, 12, 200, default, default);

        pack.OnTouch(toucher);

        Assert.Equal(40, pack.AmmoPrimary);   // ceil(80 * 0.5), the member store — the snapshot 30 is gone
        Assert.Equal(20, pack.AmmoSecondary); // ceil(40 * 0.5) — the snapshot 12 is gone
        Assert.Equal(40, player.Ammo[TFAmmo.Primary]);
        Assert.Equal(20, player.Ammo[TFAmmo.Secondary]);
        Assert.Equal(100, player.Ammo[TFAmmo.Metal]); // the snapshot the init clamped to 100
        Assert.Contains("item_pickup tf_ammo_pack", rig.Notices.Said);
        Assert.Contains("Entities.Remove", engine.Calls.Select(c => c.Member));
    }

    [Fact]
    public void A_taken_pack_counts_the_ammokit_stat()
    {
        // :434 — Event_PlayerAmmokitPickup, in the same taken>0 block as the
        // event and the removal: nothing given counts nothing.
        var rig = new Rig();
        var (toucher, player) = rig.Map.Add();
        player.MaxAmmo[TFAmmo.Primary] = 80;
        player.MaxAmmo[TFAmmo.Secondary] = 40;
        player.MaxAmmo[TFAmmo.Metal] = 200;
        var (pack, _) = rig.BuildPack(10f);
        pack.Init(rig.Dead.Handle, (int)TFWeaponId.Rocketlauncher, TFClass.Soldier,
                  30, 12, 200);

        pack.OnTouch(toucher);

        Assert.Equal([$"Ammokits {toucher.EntryIndex} 1"], rig.Stats.Heals);
    }

    [Fact]
    public void A_pack_that_gives_nothing_counts_no_ammokit_stat()
    {
        var rig = new Rig();
        var (toucher, _) = rig.Map.Add(); // every MaxAmmo default-zero: nothing fits
        var (pack, _) = rig.BuildPack(10f);
        pack.Init(rig.Dead.Handle, (int)TFWeaponId.Rocketlauncher, TFClass.Soldier,
                  5, 5, 5);

        pack.OnTouch(toucher);

        Assert.Empty(rig.Stats.Heals);
    }

    [Fact]
    public void The_engineer_also_comes_out_of_the_touch_with_grenades()
    {
        // The extra arm (:393-397): the engineer takes their own half of the
        // grenade maximum on top of the loop — the snapshot alone would leave
        // this pool at zero.
        var rig = new Rig();
        var (toucher, player) = rig.Map.Add(TFTeam.Red, TFClass.Engineer);
        player.MaxAmmo[TFAmmo.Primary] = 80;
        player.MaxAmmo[TFAmmo.Grenades1] = 32;
        var (pack, _) = rig.BuildPack(10f);
        pack.Init(rig.Dead.Handle, (int)TFWeaponId.Rocketlauncher, TFClass.Soldier,
                  5, 5, 5);

        pack.OnTouch(toucher);

        Assert.Equal(16, player.Ammo[TFAmmo.Grenades1]); // ceil(32 * 0.5)
    }

    [Fact]
    public void A_pack_that_could_give_nothing_survives_the_touch()
    {
        // The :432 quirk, kept: iAmmoTaken is the sum ACTUALLY given — a
        // picker with every pool full takes nothing, the event stays unfired
        // and the pack stands on the ground.
        var rig = new Rig();
        var (toucher, _) = rig.Map.Add(); // every MaxAmmo default-zero: nothing fits
        var (pack, engine) = rig.BuildPack(10f);
        pack.Init(rig.Dead.Handle, (int)TFWeaponId.Rocketlauncher, TFClass.Soldier,
                  5, 5, 10, default, default);

        pack.OnTouch(toucher);

        Assert.DoesNotContain("Entities.Remove", engine.Calls.Select(c => c.Member));
        Assert.DoesNotContain("item_pickup tf_ammo_pack", rig.Notices.Said);
    }

    [Fact]
    public void A_spy_takes_the_pack_that_gave_the_soldier_nothing_for_its_cloak()
    {
        // The cloak arm (:366-369): AddToSpyCloakMeter(100 * m_flAmmoRatio) —
        // a moved meter is an iAmmoTaken++, so the pack the previous fact
        // left standing goes with the spy, event and all.
        var rig = new Rig();
        var (toucher, player) = rig.Map.Add(TFTeam.Red, TFClass.Spy); // every MaxAmmo default-zero: no ammo fits
        var (pack, engine) = rig.BuildPack(10f);
        pack.Init(rig.Dead.Handle, (int)TFWeaponId.Rocketlauncher, TFClass.Soldier,
                  5, 5, 10, default, default);

        pack.OnTouch(toucher);

        Assert.Equal(50f, player.CloakMeter); // 100 * the pack's 0.5 ratio
        Assert.Contains("Entities.Remove", engine.Calls.Select(c => c.Member));
        Assert.Contains("item_pickup tf_ammo_pack", rig.Notices.Said);
    }

    [Fact]
    public void An_empty_pack_empties_itself_and_the_picker_gets_nothing()
    {
        // The empty arm (:299-311): the pack's own primary becomes the token
        // one and the pack goes — the picker receives nothing at all.
        var rig = new Rig();
        var (toucher, player) = rig.Map.Add();
        player.MaxAmmo[TFAmmo.Primary] = 80;
        var (pack, engine) = rig.BuildPack(10f);
        pack.Init(rig.Dead.Handle, (int)TFWeaponId.Rocketlauncher, TFClass.Soldier,
                  5, 5, 5, empty: true);
        Assert.True(pack.EmptyPack);

        pack.OnTouch(toucher);

        Assert.Equal(1, pack.AmmoPrimary); // GiveAmmo(1, TF_AMMO_PRIMARY), the member store
        Assert.Empty(player.Ammo);
        Assert.Empty(rig.Notices.Said);
        Assert.Contains("Entities.Remove", engine.Calls.Select(c => c.Member));
    }

    [Theory]
    [InlineData(TFClass.Scout, 75)] // the scout movie (:321)
    [InlineData(TFClass.Soldier, 50)]
    [InlineData(TFClass.HeavyWeapons, 50)]
    public void The_lunchbox_pack_heals_instead_of_giving(TFClass toucherClass, int heal)
    {
        var rig = new Rig();
        var (owner, _) = rig.Map.Add(TFTeam.Red, TFClass.HeavyWeapons); // the lunchbox is the heavy's (:145)
        var (toucher, player) = rig.Map.Add(TFTeam.Red, toucherClass);
        var (pack, engine) = rig.BuildPack(10f);
        pack.Init(owner, (int)TFWeaponId.Lunchbox, TFClass.HeavyWeapons,
                  5, 5, 5);
        Assert.True(pack.HealthInstead);

        pack.OnThink(); // :317 — even the heal waits for the landing
        pack.OnTouch(toucher);

        Assert.Equal(heal, player.Health);
        Assert.Contains($"player_healonhit {heal}", rig.Notices.Said);
        Assert.Contains(rig.Notices.Said, s => s.StartsWith("player_stealsandvich", StringComparison.Ordinal));
        Assert.Empty(player.Ammo); // no ammo arm
        Assert.Contains("Entities.Remove", engine.Calls.Select(c => c.Member));
    }

    [Fact]
    public void A_health_pack_still_hangs_in_the_air_its_first_moment()
    {
        // :317: the lunchbox hides itself from EVERYONE until FlyThink.
        var rig = new Rig();
        var (owner, _) = rig.Map.Add(TFTeam.Red, TFClass.HeavyWeapons);
        var (toucher, player) = rig.Map.Add();
        var (pack, engine) = rig.BuildPack(10f);
        pack.Init(owner, (int)TFWeaponId.Lunchbox, TFClass.HeavyWeapons,
                  5, 5, 5);

        pack.OnTouch(toucher);

        Assert.Equal(0, player.Health);
        Assert.Empty(rig.Notices.Said);
        Assert.DoesNotContain("Entities.Remove", engine.Calls.Select(c => c.Member));
    }

    [Fact]
    public void Thirty_seconds_out_the_pack_removes_itself()
    {
        var rig = new Rig();
        var (pack, engine) = rig.BuildPack(10f);

        pack.OnThink("DieContext"); // SUB_Remove (:71)
        Assert.Contains("Entities.Remove", engine.Calls.Select(c => c.Member));

        var (other, otherEngine) = rig.BuildPack(10f);
        other.OnThink("SomethingElse"); // a context the schedule does not know leaves it alone
        Assert.DoesNotContain("Entities.Remove", otherEngine.Calls.Select(c => c.Member));
    }

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Bodies(params ScriptedEntity[] bodies) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle handle) => bodies.FirstOrDefault(b => b.Handle == handle);
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => className == EntityNames.Hl2mpRagdoll;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>
    /// The Arsenal's entity double: hands out handles and routes the carried
    /// pair's spawn to the registry (the rest forwards to the recording
    /// engine). A copy of core's WeaponSwitchTests' SpawningEntities, which is
    /// internal over there.
    /// </summary>
    private sealed class SpawningEntities : IEngineEntities
    {
        public IInterpolationBlocks InterpolationBlocks => NoInterpolationBlocks.Instance;
        public IRenderPolicyBlocks RenderPolicyBlocks => NoRenderPolicyBlocks.Instance;
        public IClientReplayEngine ClientReplay => NoClientReplayEngine.Instance;

        private readonly IEngineEntities _engine;
        private readonly ScriptedEntityRegistry _registry;
        private readonly Dictionary<uint, string> _created = new();
        private uint _next = 0x0500;

        public IReadOnlyCollection<string> CreatedClasses => _created.Values;

        public SpawningEntities(RecordingEngine engine, ScriptedEntityRegistry registry)
        {
            _engine = engine;
            _registry = registry;
        }

        public int Count() => _engine.Count();
        public void PurgeMapRefs() => _engine.PurgeMapRefs();
        public void SetClassName(EntityHandle h, string className) => _engine.SetClassName(h, className);
        public void UseTriggerBounds(EntityHandle entity, bool enable, float bloat, bool uniform = false) => _engine.UseTriggerBounds(entity, enable, bloat, uniform);
        public void CleanupDeleteList() => _engine.CleanupDeleteList();
        public int InvalidateMdlCacheAll() => _engine.InvalidateMdlCacheAll();
        public int UpdateVisibilityAll() => _engine.UpdateVisibilityAll();

        public EntityHandle Create(string className)
        {
            var handle = new EntityHandle(_next++);
            _created[handle.Value] = className;
            return handle;
        }

        public EntityHandle CreateFromMap(string className, byte[] packedKeys) => Create(className);
        public EntityHandle CreateFromMapAt(string className, byte[] packedKeys, int edictIndex) => Create(className);

        /// <summary>The class a created handle carries, for the directory's dispatch.</summary>
        public string? CreatedClassOf(EntityHandle handle) =>
            _created.TryGetValue(handle.Value, out string? className) ? className : null;

        public void Spawn(EntityHandle entity)
        {
            // The carried pair spawns for real, through the registry as the
            // game's spawn event does; the ammo pack's managed half is this
            // fixture's directory's to hand over, so its spawn just records.
            if (_created.TryGetValue(entity.Value, out string? className)
                && className is EntityNames.SsWeapon or EntityNames.PredictedViewModel)
                _registry.Spawn(entity, className);
            else
                _engine.Spawn(entity);
        }

        public void AddBaseVelocity(EntityHandle entity, Vector velocity) => _engine.AddBaseVelocity(entity, velocity);
        public void PositionChanged(EntityHandle entity, Vector previousOrigin) => _engine.PositionChanged(entity, previousOrigin);
        public void ApplyVelocityImpulse(EntityHandle h, Vector impulse, bool clearGround) => _engine.ApplyVelocityImpulse(h, impulse, clearGround);
        public IEnumerable<EntityHandle> FindAllByClassname(string className) => _engine.FindAllByClassname(className);
        public List<EntityHandle> FindAllByName(string name, int limit = 512) => _engine.FindAllByName(name, limit);
        public EntityHandle FindByName(string name) => _engine.FindByName(name);
        public bool FireInput(EntityHandle target, string input, string? value = null, EntityHandle activator = default) => _engine.FireInput(target, input, value, activator);
        public Vector GetAngles(EntityHandle h) => _engine.GetAngles(h);
        public Vector GetAngularVelocity(EntityHandle entity) => _engine.GetAngularVelocity(entity);
        public int GetArmor(EntityHandle entity) => _engine.GetArmor(entity);
        public (Vector Mins, Vector Maxs) GetBounds(EntityHandle entity) => _engine.GetBounds(entity);
        public Vector GetBoundsSize(EntityHandle entity) => _engine.GetBoundsSize(entity);
        public string GetClassName(EntityHandle h) => _engine.GetClassName(h);
        public Vector GetEyeAngles(EntityHandle h) => _engine.GetEyeAngles(h);
        public Vector GetEyePosition(EntityHandle h) => _engine.GetEyePosition(h);
        public int GetHealth(EntityHandle h) => _engine.GetHealth(h);
        public int GetIndex(EntityHandle h) => _engine.GetIndex(h);
        public string GetName(EntityHandle entity) => _engine.GetName(entity);
        public Vector GetOrigin(EntityHandle h) => _engine.GetOrigin(h);
        public Vector GetSpawnAngles(EntityHandle entity) => _engine.GetSpawnAngles(entity);
        public int GetSpawnFlags(EntityHandle entity) => _engine.GetSpawnFlags(entity);
        public Vector GetSpawnOrigin(EntityHandle entity) => _engine.GetSpawnOrigin(entity);
        public Vector GetVelocity(EntityHandle h) => _engine.GetVelocity(h);
        public Vector GetSmoothedVelocity(EntityHandle h) => default;
        public Vector GetWorldSpaceCenter(EntityHandle h) => _engine.GetWorldSpaceCenter(h);
        public bool IsPlayer(EntityHandle h) => _engine.IsPlayer(h);
        public bool IsValid(EntityHandle h) => _engine.IsValid(h);
        public void Remove(EntityHandle entity) => _engine.Remove(entity);
        public void SetAngles(EntityHandle entity, Vector angles) => _engine.SetAngles(entity, angles);
        public void SetAngularVelocity(EntityHandle entity, Vector degreesPerSecond) => _engine.SetAngularVelocity(entity, degreesPerSecond);
        public void SetArmor(EntityHandle entity, int armor) => _engine.SetArmor(entity, armor);
        public void SetHealth(EntityHandle h, int health) => _engine.SetHealth(h, health);
        public bool SetKeyValue(EntityHandle entity, string key, string value) => _engine.SetKeyValue(entity, key, value);
        public void SetOrigin(EntityHandle h, Vector origin) => _engine.SetOrigin(h, origin);
        public void SetOwner(EntityHandle h, EntityHandle owner) => _engine.SetOwner(h, owner);
        public void SetVelocity(EntityHandle h, Vector velocity) => _engine.SetVelocity(h, velocity);
        public void Teleport(EntityHandle h, Vector origin, Vector? angles, bool zeroVelocity) => _engine.Teleport(h, origin, angles, zeroVelocity);
        public bool SetParent(EntityHandle child, EntityHandle parent, int parentAttachment = 0) => _engine.SetParent(child, parent, parentAttachment);
        public EntityHandle GetParent(EntityHandle child) => _engine.GetParent(child);
        public int GetParentAttachment(EntityHandle child) => _engine.GetParentAttachment(child);
        public void SetLocalTransform(EntityHandle child, Vector? origin, Vector? angles) => _engine.SetLocalTransform(child, origin, angles);
        public void GetLocalTransform(EntityHandle child, out Vector origin, out Vector angles) => _engine.GetLocalTransform(child, out origin, out angles);
        public ParentProbe ParentProbe(EntityHandle entity) => _engine.ParentProbe(entity);
        public bool GetAttachmentFrame(EntityHandle entity, int attachment, out Vector origin, out QAngle angles) => _engine.GetAttachmentFrame(entity, attachment, out origin, out angles);
        public int LookupAttachmentAny(EntityHandle entity, string attachmentName) => _engine.LookupAttachmentAny(entity, attachmentName);
        public EntityHandle FindBaseByName(string name) => _engine.FindBaseByName(name);
        public Span<byte> GetEngineBlock(EntityHandle entity) => _engine.GetEngineBlock(entity);
        public void DestroyPhysics(EntityHandle entity) => _engine.DestroyPhysics(entity);
        public float GetGravity(EntityHandle entity) => _engine.GetGravity(entity);
        public void SetGravity(EntityHandle entity, float gravity) => _engine.SetGravity(entity, gravity);
        public SolidFlags GetSolidFlags(EntityHandle entity) => _engine.GetSolidFlags(entity);
        public SolidType GetSolidType(EntityHandle entity) => _engine.GetSolidType(entity);
        public void SetSolidity(EntityHandle entity, SolidType solidType, SolidFlags solidFlags) => _engine.SetSolidity(entity, solidType, solidFlags);
        public void SetBounds(EntityHandle entity, Vector mins, Vector maxs) => _engine.SetBounds(entity, mins, maxs);
        public void SetSpawnFlags(EntityHandle entity, int spawnFlags) => _engine.SetSpawnFlags(entity, spawnFlags);
    }
}
