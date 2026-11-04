using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Server.Entities.Items;
using TF2.Server.Entities.Rooms;
using TF2.Server.Medigun;
using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Rules;

using TF2.Tests.Entities;
using TF2.Tests.Entities.Items;
using TF2.Tests.Projectiles;
using TF2.Tests.Weapons;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace TF2.Tests.Player;

/// <summary>
/// The use key's pickup of a dropped weapon (<see cref="TFDroppedWeaponPickup"/>):
/// the reach, the gates, the swap's throw and carries, the room's mercy and
/// the verb's hold. The stock the facts pin is CTFPlayer's
/// TryToPickupDroppedWeapon and CanPickupDroppedWeapon
/// (tf_player.cpp:22789-22808, tf_player_shared.cpp:14742-14794) and the
/// action-slot verbs (tf_gamerules.cpp:15324-1548).
/// </summary>
public sealed class TFDroppedWeaponPickupTests
{
    private const string Rocket = "tf_weapon_rocketlauncher";
    private const string Shotgun = "tf_weapon_shotgun_soldier";
    private const string Medigun = "tf_weapon_medigun";
    private const string Nonesuch = "tf_weapon_nonesuch";

    private static readonly Vector Eye = new(0f, 0f, 64f);

    /// <summary>
    /// The pickup's world: the proven arsenal rig's engine answer set, a
    /// soldier, and a directory whose unknown dropped-weapon handles become
    /// fresh, own-engined lies (the swap's thrown copy is the one the swap
    /// creates) — with the registry behind it for the hands' ss_weapon.
    /// </summary>
    private sealed class Rig
    {
        public readonly RecordingLog Log = new();
        public readonly RecordingEngine Engine = Mock().Ignores("Entities.IsValid")
            .Returns("Entities.GetIndex", 3)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", Eye)
            .Returns("Entities.GetEyePosition", Eye)
            .Returns("Entities.GetEyeAngles", default(Vector)) // the aim and the throw's push
            .Returns("Entities.GetVelocity", new Vector(10f, 0f, 0f))
            .Returns("Players.IsAlive", true)
            .Returns("EntityRender.LookupSequence", -1) // the Arsenal's first Deploy
            .Returns("EntityRender.LookupSequenceByActivity", -1)
            .Returns("Clock.Now", 100f);
        public readonly ScriptedEntityRegistry Registry;
        public readonly SpawningEntities World;
        public readonly FakeMapPlayers Map = new();
        public readonly ConVarRegistry ConVars;
        public readonly FakeSeeds Seeds = new();
        public readonly PickDirectory Lies;
        public readonly FakeMedigun Meter = new();
        public readonly TFWeaponScripts Scripts;
        public readonly ItemSchema Schema;
        public readonly TFClassTable Classes;
        public readonly Arsenal Arsenal;
        public readonly (EntityHandle Handle, FakeMapPlayer Player) Me;

        public Rig(TFClass playerClass = TFClass.Soldier)
        {
            Engine.Returns("Entities.IsValid", true)
                  .Returns("Entities.GetAngles", default(Vector))
                  .Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 1f, new Vector(150f, 0f, 64f), Vector.Zero, EntityHandle.None));

            Registry = new ScriptedEntityRegistry(TestEngines.Server());
            Registry.RegisterAll(typeof(SsWeapon).Assembly);
            Registry.Services = Services(out _);

            Me = Map.Add(TFTeam.Red, playerClass);
            Registry.KeyValue(Me.Handle, EntityNames.Player, "targetname", "fixture_player");

            World = new SpawningEntities(Engine, Registry);

            ConVars = ConVarRegistry.For(UnattachedConsole());
            ConVars.RegisterAll(typeof(TFDroppedWeapon).Assembly);

            var files = WeaponFixtures.ShippedWithClasses();
            files.Bytes[ItemSchema.FilePath] =
                Encoding.UTF8.GetBytes(EconTestFiles.Fixture("items_game.trimmed.txt"));
            Scripts = new TFWeaponScripts(Log, files);
            Schema = new ItemSchema(files, Log);
            Classes = new TFClassTable(Log, files);
            var types = new TFWeaponTypes(Scripts, Classes, Schema);
            Arsenal = new Arsenal(Log, World, Engine, Engine, Engine, Engine, Engine, Engine, Engine,
                                  Registry, Engine, new DamageHitGroups(),
                                  weapons: new WeaponCatalog(() => [types], Log),
                                  ammo: new AmmoCatalog(() => [new TFAmmoTypes()], Log));

            Lies = new PickDirectory(this) { BuildDropped = BuildDropped };
        }

        public EntityHandle Player => Me.Handle;

        /// <summary>The pickup over this rig, with the seams a fact names.</summary>
        public TFDroppedWeaponPickup Pickup(ITFRespawnRooms? rooms = null, ITeamRoundState? rounds = null)
            => new(Arsenal, Map, World, Engine, Engine, Lies, Engine, Meter, Scripts, Schema,
                   Classes, rooms, rounds);

        /// <summary>A lie on the floor before the eye, its handle the one the ray ends on.</summary>
        public (TFDroppedWeapon Dropped, EntityHandle Handle) Drop(string weapon, float clock = 40f)
            => Lies.Seed(weapon, clock);

        /// <summary>Where the eye's ray ends: the lie the pickup sees.</summary>
        public void AimAt(EntityHandle handle)
            => Engine.Returns("WorldQuery.TraceLine",
                              new TraceResult(true, 1f, new Vector(150f, 0f, 64f), Vector.Zero, handle));

        public void Give(params string[] weapons)
        {
            foreach (var weapon in weapons)
                Arsenal.GiveWeapon(Player, weapon);

            // The deploy's draw finished long ago (Arsenal's
            // now + DeployTime against this rig's frozen clock): the use key
            // is not held off by the swing (:22794).
            if (Arsenal.CarriedPair(Player).Weapon is { } held
                && Registry.Get(held) is SsWeapon hands)
                hands.State.NextPrimaryAttack = 0f;
        }

        public IReadOnlyList<(string ClassName, int Clip, bool Active)> Holdings => Arsenal.Holdings(Player);

        /// <summary>A dropped weapon built by hand on its own recording engine, already spawned.</summary>
        private (TFDroppedWeapon Dropped, RecordingEngine Engine) BuildDropped(float clock)
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

        /// <summary>A registry whose container answers <c>Now</c> as the lie's clock.</summary>
        private static ScriptedEntityRegistry OwnerFor(float clock)
        {
            var services = TestServices.Services(out var clockEngine);
            clockEngine.Returns("Clock.Now", clock);
            return new ScriptedEntityRegistry(TestEngines.Server()) { Services = services };
        }
    }

    /// <summary>
    /// The pickup's view of the managed entities: the fact's pre-seeded lies,
    /// a fresh own-engined lie per handle the swap creates (the game's
    /// registry spawn event makes it and this is that event's stand-in), and
    /// the registry behind it for every real entity — the hands' ss_weapon
    /// above all.
    /// </summary>
    private sealed class PickDirectory : IEntityDirectory
    {
        public readonly List<TFDroppedWeapon> Dropped = new();
        public readonly Dictionary<TFDroppedWeapon, RecordingEngine> Engines = new();
        public Func<float, (TFDroppedWeapon Dropped, RecordingEngine Engine)>? BuildDropped;

        private readonly Rig Rig;
        private readonly Dictionary<uint, ScriptedEntity> _known = new();
        private uint _nextSeed = 0x0900;

        public PickDirectory(Rig rig) => Rig = rig;

        public (TFDroppedWeapon Dropped, EntityHandle Handle) Seed(string weapon, float clock = 40f,
                                                                   int ammo = 12, float charge = 0f)
        {
            var (dropped, engine) = BuildDropped!.Invoke(clock);
            var handle = new EntityHandle(_nextSeed);
            dropped.Init(EntityHandle.None, weapon, 4, ammo, chargeLevel: charge);
            Dropped.Add(dropped);
            Engines[dropped] = engine;
            _known[_nextSeed++] = dropped;
            return (dropped, handle);
        }

        public ScriptedEntity? Get(EntityHandle handle)
        {
            if (_known.TryGetValue(handle.Value, out var known))
                return known;

            // An unknown handle the class names as a lie is one the swap
            // created this frame: build it, as the game's spawn event would.
            if (Rig.World.CreatedClassOf(handle) == TFDroppedWeapon.Class)
            {
                var (dropped, engine) = BuildDropped!.Invoke(500f);
                Dropped.Add(dropped);
                Engines[dropped] = engine;
                _known[handle.Value] = dropped;
                return dropped;
            }

            return Rig.Registry.Get(handle);
        }

        public bool Removed(TFDroppedWeapon dropped) =>
            Engines[dropped].Calls.Any(c => c.Member == "Entities.Remove");

        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => className switch
        {
            TFDroppedWeapon.Class => Dropped,
            _ => [],
        };
        public bool IsRegistered(string className) => className == TFDroppedWeapon.Class;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>
    /// The throw's stream (<see cref="IUniformRandom"/>): scripted fractions
    /// on a cycle, so no fact depends on a lucky draw.
    /// </summary>
    private sealed class FakeSeeds : IUniformRandom
    {
        private readonly float[] _fractions = [0.9f, 0.3f, 0.5f, 0.5f];
        private int _at;

        public float RandomFloat(float min, float max) => min + (max - min) * _fractions[_at++ % _fractions.Length];
        public int RandomInt(int min, int max) => min + (int)((max - min) * _fractions[_at++ % _fractions.Length]);
    }

    /// <summary>The medigun seam, recording the meter's writes.</summary>
    private sealed class FakeMedigun : ITFMedigunWeapons
    {
        public float Charge = 0.25f;
        public readonly List<float> SetTo = [];

        public float ChargeLevel(int ownerIndex) => Charge;
        public void SetChargeLevel(int ownerIndex, float level) => SetTo.Add(level);
        public void TickMedic(int ownerIndex, float now, float frametime) { }
        public void Holster(int ownerIndex, float now) { }
        public void Unholster(int ownerIndex, float now) { }
        public void RemoveMedigun(int ownerIndex, float now) { }
        public int HealingTargetIndex(int ownerIndex) => -1;
        public bool IsReleasing(int ownerIndex) => false;
    }

    //------------------------------------------------------------------
    // The swap: the take, the throw, the carries.
    //------------------------------------------------------------------

    [Fact]
    public void A_reached_lie_is_taken_and_the_hands_go_to_the_floor()
    {
        var rig = new Rig();
        rig.Give(Rocket);
        var heldClip = rig.Holdings.First(h => h.ClassName == Rocket).Clip;
        var (dropped, handle) = rig.Drop(Rocket);
        rig.AimAt(handle);

        Assert.True(rig.Pickup().TryPickup(rig.Player));

        // The hands hold the lie's weapon at the lie's clip, one row only.
        var row = Assert.Single(rig.Holdings);
        Assert.Equal(Rocket, row.ClassName);
        Assert.Equal(dropped.Clip, row.Clip);

        // The held weapon lies on the floor as a new lie of its own, at the
        // clip the hands held, and the taken lie is gone.
        var thrown = Assert.Single(rig.Lies.Dropped, d => d != dropped && d.Weapon == Rocket);
        Assert.Equal(heldClip, thrown.Clip);
        Assert.Equal(rig.Player, thrown.LastOwner);
        Assert.True(rig.Lies.Removed(dropped));
    }

    [Fact]
    public void The_reserve_is_set_not_added_to()
    {
        var rig = new Rig();
        rig.Give(Rocket);
        rig.Arsenal.GiveAmmo(rig.Player, 30, TFAmmoNames.Name(TFAmmo.Primary));
        var (_, handle) = rig.Drop(Rocket, 0);
        rig.AimAt(handle);

        Assert.True(rig.Pickup().TryPickup(rig.Player));

        // The pool holds the lie's 12, not 30 + 12 (:626-30, the set).
        Assert.Equal(12, rig.Arsenal.Pools(rig.Player).GetValueOrDefault(TFAmmoNames.Name(TFAmmo.Primary)));
    }

    [Fact]
    public void The_mediguns_charge_crosses_both_ways()
    {
        var rig = new Rig(TFClass.Medic);
        rig.Give(Medigun);
        rig.Meter.Charge = 0.25f; // the hands' meter, the throw's snapshot
        var (seeded, handle) = rig.Lies.Seed(Medigun, charge: 0.6f);
        rig.AimAt(handle);

        Assert.True(rig.Pickup().TryPickup(rig.Player));

        // The taken meter reads the lie's (:636-40) and the thrown meter the hands'.
        Assert.Contains(0.6f, rig.Meter.SetTo);
        var thrown = Assert.Single(rig.Lies.Dropped, d => d != seeded);
        Assert.Equal(0.25f, thrown.ChargeLevel);
    }

    //------------------------------------------------------------------
    // The gates.
    //------------------------------------------------------------------

    [Fact]
    public void A_lie_off_the_ray_is_no_pickup()
    {
        var rig = new Rig();
        rig.Give(Rocket);
        var (dropped, _) = rig.Drop(Rocket);
        rig.AimAt(EntityHandle.None); // the ray ends on the world, not the lie

        Assert.False(rig.Pickup().TryPickup(rig.Player));
        Assert.False(rig.Lies.Removed(dropped));
        Assert.DoesNotContain(rig.Lies.Dropped, d => d != dropped); // no thrown copy either
    }

    [Fact]
    public void A_cloaked_player_picks_up_nothing()
    {
        var rig = new Rig();
        rig.Me.Player.Conditions.Add(TFCond.Stealthed);
        rig.Give(Rocket);
        var (dropped, handle) = rig.Drop(Rocket);
        rig.AimAt(handle);

        // CanAttack's stealth arm (:12245) holds the attempt, not the
        // wear-check CanPickupDroppedWeapon — the cloak is no stranger to it.
        Assert.True(rig.Pickup().CanPickup(rig.Player, dropped));

        var pickup = rig.Pickup();
        Assert.False(pickup.TryPickup(rig.Player));
        Assert.False(rig.Lies.Removed(dropped));

        rig.Me.Player.Conditions.Remove(TFCond.Stealthed);
        Assert.True(pickup.TryPickup(rig.Player));
    }

    [Fact]
    public void A_disguised_or_cloaked_spies_hands_are_tied()
    {
        var rig = new Rig(TFClass.Spy);
        rig.Me.Player.Conditions.Add(TFCond.Disguised);
        rig.Give(Rocket); // the spy's slot-one stand-in for the fact
        var (dropped, handle) = rig.Drop(Rocket);
        rig.AimAt(handle);

        Assert.False(rig.Pickup().CanPickup(rig.Player, dropped));

        rig.Me.Player.Conditions.Remove(TFCond.Disguised);
        rig.Me.Player.Conditions.Add(TFCond.Stealthed);
        Assert.False(rig.Pickup().CanPickup(rig.Player, dropped));
    }

    [Fact]
    public void A_dead_player_picks_up_nothing()
    {
        var rig = new Rig();
        rig.Give(Rocket);
        rig.Me.Player.Alive = false;
        var (dropped, handle) = rig.Drop(Rocket);
        rig.AimAt(handle);

        Assert.False(rig.Pickup().TryPickup(rig.Player));
        Assert.False(rig.Lies.Removed(dropped));
    }

    [Fact]
    public void Empty_hands_pick_up_nothing()
    {
        var rig = new Rig(); // no weapon granted: the hands are empty
        var (dropped, handle) = rig.Drop(Rocket);
        rig.AimAt(handle);

        Assert.False(rig.Pickup().TryPickup(rig.Player));
    }

    [Fact]
    public void A_name_no_item_answers_is_no_pickup()
    {
        var rig = new Rig();
        rig.Give(Rocket);
        var (dropped, handle) = rig.Drop(Nonesuch);
        rig.AimAt(handle);

        Assert.False(rig.Pickup().CanPickup(rig.Player, dropped));
        Assert.False(rig.Pickup().TryPickup(rig.Player));
    }

    [Fact]
    public void A_shot_on_its_way_holds_the_use()
    {
        var rig = new Rig();
        rig.Give(Rocket);
        var (_, handle) = rig.Drop(Rocket);
        rig.AimAt(handle);

        // The hands' next trigger time in the future (:22794).
        var hands = (SsWeapon)rig.Registry.Get(rig.Arsenal.CarriedPair(rig.Player).Weapon)!;
        hands.State.NextPrimaryAttack = 999f;
        Assert.False(rig.Pickup().TryPickup(rig.Player));

        hands.State.NextPrimaryAttack = 50f; // the clock reads 100
        Assert.True(rig.Pickup().TryPickup(rig.Player));
    }

    [Fact]
    public void A_loser_of_a_won_round_waits()
    {
        var rig = new Rig();
        var rounds = new FakeRoundState { State = RoundState.TeamWin, WinningTeam = TFTeam.Blue };
        rig.Give(Rocket);
        var (dropped, handle) = rig.Drop(Rocket);
        rig.AimAt(handle);

        // Red lost the round: the wait (:12271, IsLoser).
        Assert.False(rig.Pickup(rounds: rounds).TryPickup(rig.Player));

        rounds.WinningTeam = TFTeam.Red;
        Assert.True(rig.Pickup(rounds: rounds).TryPickup(rig.Player));
    }

    [Fact]
    public void The_respawn_room_keeps_the_floor_clear()
    {
        var rig = new Rig();
        var rooms = new Ctf.FakeRespawnRooms();
        rooms.Inside.Add(rig.Player);
        rig.Give(Rocket);
        var before = rig.Lies.Dropped.Count;
        var (_, handle) = rig.Drop(Rocket);
        rig.AimAt(handle);

        Assert.True(rig.Pickup(rooms: rooms).TryPickup(rig.Player));

        // The take happened and no second lie was thrown (:22737-47).
        Assert.Equal(before + 1, rig.Lies.Dropped.Count);
        Assert.Equal(Rocket, Assert.Single(rig.Holdings).ClassName);
    }

    //------------------------------------------------------------------
    // The verb and the hold.
    //------------------------------------------------------------------

    [Fact]
    public void The_verbs_press_release_and_claim_their_name()
    {
        var presses = new List<EntityHandle>();
        var releases = new List<EntityHandle>();
        var pickup = new VerbSpy(presses, releases);
        var verb = new TFUseActionSlotCommand(pickup);
        var player = new EntityHandle(0x21u);

        Assert.True(verb.Handle(player, TFUseActionSlotCommand.Verb, ReadOnlySpan<string>.Empty));
        Assert.Equal([player], presses);
        Assert.Equal([player], releases); // the tap is both, :15324-31

        Assert.True(verb.Handle(player, "+" + TFUseActionSlotCommand.Verb, ReadOnlySpan<string>.Empty));
        Assert.Equal([player, player], presses);
        Assert.Equal([player], releases); // the hold's downstroke only, :15332-41

        Assert.True(verb.Handle(player, "-" + TFUseActionSlotCommand.Verb, ReadOnlySpan<string>.Empty));
        Assert.Equal([player, player], releases);

        Assert.False(verb.Handle(player, "some_other_verb", ReadOnlySpan<string>.Empty));
    }

    [Fact]
    public void A_held_key_picks_up_once_a_hold()
    {
        var rig = new Rig();
        rig.Give(Rocket);
        var (_, handle) = rig.Drop(Rocket);
        rig.AimAt(handle);
        var pickup = rig.Pickup();

        pickup.Press(rig.Player);

        // The swap's fresh deploy drew (:749, now + DeployTime) — let it
        // finish, so the hold's latch stays the only gate on the next press.
        if (rig.Arsenal.CarriedPair(rig.Player).Weapon is { } held
            && rig.Registry.Get(held) is SsWeapon redeployed)
            redeployed.State.NextPrimaryAttack = 0f;

        pickup.Press(rig.Player); // still held: no second ask (:15336)

        int asks() => rig.Engine.Calls.Count(c => c.Member == "WorldQuery.TraceLine");
        Assert.Equal(1, asks());

        pickup.Release(rig.Player);
        pickup.Press(rig.Player); // a new hold asks again
        Assert.Equal(2, asks());
    }

    /// <summary>The pickup seam, as the verb's record.</summary>
    private sealed class VerbSpy(List<EntityHandle> presses, List<EntityHandle> releases)
        : ITFDroppedWeaponPickup
    {
        public bool CanPickup(EntityHandle player, TFDroppedWeapon weapon) => true;
        public bool TryPickup(EntityHandle player) => true;
        public void Press(EntityHandle player) => presses.Add(player);
        public void Release(EntityHandle player) => releases.Add(player);
    }

    /// <summary>
    /// The Arsenal's entity double: hands out handles and routes the carried
    /// pair's spawn to the registry (the rest forwards to the recording
    /// engine). A copy of the death drops' SpawningEntities, which is private
    /// over there.
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
            // game's spawn event does; the lie's managed half is this
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
