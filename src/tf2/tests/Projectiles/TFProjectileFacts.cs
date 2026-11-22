using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Server.Combat;
using TF2.Server.Econ;
using TF2.Server.Medigun;
using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Movement;
using TF2.Tests.Entities.Items;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>Reads an engine call that a fact requires, failing when it is absent.</summary>
internal static class EngineCallFacts
{
    public static T ArgAs<T>(this EngineCall? call, int index)
        => call is EngineCall c ? c.ArgAs<T>(index)
           : throw new InvalidOperationException("the engine never saw the call");
}

/// <summary>The applier's world: the recording engine, the real scopes and the tray.</summary>
internal sealed class ProjectileRig
{
    public TFStickybombConVars StickyVars { get; }

    public static readonly EntityHandle Player = new(0x0001_0000u | 3);
    public static readonly EntityHandle Victim = new(0x0001_0000u | 5);
    public static readonly EntityHandle ProjectileHandle = new(0x0003_0000u | 9);

    public RecordingEngine Engine { get; } = RecordingEngine.Default();
    public RecordingLog Log { get; } = new();
    public TFDamageScopes Scopes { get; } = new();
    public DamageHitGroups HitGroups { get; } = new();
    public DamageInflictors Inflictors { get; } = new();
    public TFFlareTray FlareTray { get; } = new();
    public FixedClasses Classes { get; } = new();

    /// <summary>The carrier registry the rocket's stun arm asks.</summary>
    public TFPlayerCarriers Carriers { get; } = new();

    /// <summary>The pipebomb tray, over the rig's carriers.</summary>
    public TFStickyTray Tray { get; }

    /// <summary>The temp-entity sink the rocket's effects dispatch through.</summary>
    public RecordingEffectDispatch Temps { get; } = new();

    /// <summary>The players the stun stack's condition asks read.</summary>
    public readonly FakeMapPlayers StunPlayers = new();

    /// <summary>
    /// The player's stun stack, attached: a fake map player under
    /// <paramref name="player"/>'s handle answers the condition words, as the
    /// trigger facts' rig does.
    /// </summary>
    public TFStunStack AttachStun(EntityHandle player)
    {
        StunPlayers.All[player] = new FakeMapPlayer();
        var stack = new TFStunStack(Carriers, StunPlayers, Engine);
        stack.Attach(player);
        return stack;
    }

    /// <summary>The resupply the blast's own-pass heal arm takes health through.</summary>
    public Resupply Health { get; } = new();

    public TFBlastApplier Blast { get; }
    public TFProjectileEffects Effects { get; }

    /// <summary>The one birth the weapons' launches and a caller's own spawn share.</summary>
    public TFProjectileSpawner Spawner { get; }
    public DirectoryView Directory { get; } = new();

    /// <summary>The directory the motion pass resolves its flyers through.</summary>
    public EntityDirectory MotionDirectory { get; }
    public AttributeHooks Hooks { get; } = new();
    public FixedOwners Owners { get; } = new();

    /// <summary>The skin writes, as the registration's <see cref="IServerSkins"/> answers.</summary>
    public RecordingServerSkins Skins { get; } = new();

    /// <summary>
    /// The stream the pipe's launch jitter draws from: at the midpoint (the
    /// default) both draws land on zero; a fact turns the dial to name them.
    /// </summary>
    public DialRandom Random { get; } = new();

    private int _nextDef = 5000;

    /// <summary>A launcher item carrying one float attribute.</summary>
    public IAttributeProvider AttributeItem(string hook, AttributeDescriptionFormat format, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(_nextDef++, "attr " + _nextDef, hook, format,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    /// <summary>The real mover, over the double — a launch asks the engine nothing.</summary>
    public EntityMotion Motion { get; }

    public ProjectileRig(TFPreroundPushFromDamageEnableConVar? preroundPush = null)
    {
        Tray = new(Carriers);

        var motionLog = new RecordingLog();
        var motionDirectory = MotionDirectory = new EntityDirectory(motionLog);
        Motion = new EntityMotion(motionLog, Engine, Engine, DeclaredConVars.Holding<SvGravityConVar>("600"), DeclaredConVars.Fresh<MoversBlockedConVar>(), DeclaredConVars.Fresh<MoversPushedConVar>(), Engine, motionDirectory,
            new PhysicsPushedEntities(new EnginePushWorld(Engine, Engine, Engine, motionDirectory), motionLog));

        Blast = new(Engine, Engine, Engine, Scopes, HitGroups, Inflictors, Classes, Log,
                    Engine, Owners, Hooks, Health, Engine, Engine, Directory, Carriers,
                    preroundPush ?? DeclaredConVars.Fresh<TFPreroundPushFromDamageEnableConVar>());
        // The registered defaults for the convars the sticky's arm and
        // air-burst radius read, as the game would answer; a fact turns them.
        // The blow's force scales are held off.
        StickyVars = new ConVarRegistry(new NamedConVars(new Dictionary<string, string>())).Holder<TFStickybombConVars>();
        StickyVars.ForceFromBullet.Hold("0");
        StickyVars.ForceFromBuckshot.Hold("0");
        StickyVars.ForceFromBlast.Hold("0");
        StickyVars.ForceToMove.Hold("0");
        StickyVars.ForceSleepTime.Hold("0");
        Spawner = new(Engine, Directory, Log);
        Effects = new(Engine, Spawner, Engine, Tray, FlareTray, Log, Owners, Random, Hooks, Temps, Engine, Engine, StickyVars);

        Engine.Returns("Entities.Create", ProjectileHandle);
        Engine.Returns("Players.GetHandle", Player);
        // Off the ground unless a fact stands them on it: the jump a self
        // blast's scales are written for.
        Engine.Returns("Networking.EntityGetState", EntityState.None);
        Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
        // The trail writes ask the owner's team; a fact's Returns turns it.
        Engine.Returns("Players.GetTeam", 2);
        // The deflection takes the deflector's active weapon (the airblast's
        // flamethrower); a fact's Returns turns it.
        Engine.Returns("Players.GetActiveWeapon", "tf_flamethrower");
        Engine.Returns("Players.GetActiveWeaponEntity", EntityHandle.None);
        Engine.Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f));
        Engine.Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f));
        // The firing basis at yaw 0 is mathlib's AngleVectors (Angles.RightUp):
        // right (0, -1, 0), up (0, 0, 1).
        // At rest unless a fact launches: the pipe's skybox trace needs a
        // velocity to trace along, and zero means no trace at all.
        Engine.Returns("Entities.GetVelocity", default(Vector));
        // Not spinning, so the launch's impulse lands undiluted.
        Engine.Returns("Entities.GetAngularVelocity", default(Vector));
        // A clear line of sight, unless a fact says otherwise.
        Engine.Returns("WorldQuery.TraceLine", new TraceResult(true, 1f, default, default, EntityHandle.None));
        Engine.Returns("Entities.IsPlayer", false);
        Engine.Returns("Entities.GetOrigin", default(Vector));
        // Centred where it stands: the own pass reads its owner's centre, as
        // the sweep reads a victim's; a fact's Returns moves it.
        Engine.Returns("Entities.GetWorldSpaceCenter", default(Vector));
        // A point collision box; a fact's Returns decides the victim's bounds.
        Engine.Returns("Entities.GetBounds", (default(Vector), default(Vector)));
        Engine.Returns("Entities.IsValid", true);
        Engine.Returns("Entities.GetIndex", 1);
        // The blast jump's health gate reads its soldier alive; a fact's
        // Returns decides the wound.
        Engine.Returns("Entities.GetHealth", 100);
        // The needle's precached model index, asked when the syringe hands its
        // client effect out (tf_projectile_base.cpp:229); a fact's Returns turns it.
        Engine.Returns("Precache.PrecacheModel", 44);
        Engine.Returns("EntityPhysics.GetSolidFlags", SolidFlags.None);
        // The sphere query always answers its count; a fact's FillsSphere
        // decides what is IN it.
        Engine.Returns("WorldQuery.EntitiesInSphere", 0);
        // The blast's answered damage ask lands by default — the damage the
        // applier passed (>0) is the amount taken, so the push that gates on
        // it runs as it did before the gate existed. A fact that wants a
        // refused hit answers 0 itself.
        Engine.ReturnsEachCall("Damage.TakeDamageAnswered",
            () => Engine.Calls[^1].Args[2] is float d && d > 0f ? 1 : 0);
    }

    /// <summary>
    /// A sticky outside a registry: built by hand, its areas bound to the
    /// double. The clock stands at 10 (the sleep-time gate reads it) and the
    /// toucher has no parent unless a fact names one.
    /// </summary>
    public PostSimulationQueue Queue { get; } = new();

    /// <summary>A stickybomb, pinned through <see cref="Queue"/> unless the fact brings its own.</summary>
    public TFStickybomb Sticky(PostSimulationQueue? postSimulation = null)
    {
        Engine.Returns("Clock.Now", 10f)
              .Returns("Entities.GetParent", EntityHandle.None);
        var sticky = new TFStickybomb(Engine, Blast, Engine, StickyVars, Engine, Engine, Directory, Engine,
                                      postSimulation ?? Queue);
        sticky.RegisteredKind = ScriptedEntityKind.Prop; // TakesDamage, as the registration says
        sticky.BindAreas(TestServices.Areas(Engine));
        return BindStates(sticky);
    }

    /// <summary>A rocket with its areas bound, launchable and touchable.</summary>
    public TFRocket Rocket()
    {
        var rocket = new TFRocket(Engine, Motion, Blast, Owners, Engine, Hooks, Carriers, Temps, Engine,
                                  RocketConds);
        rocket.BindAreas(TestServices.Areas(Engine));
        return BindStates(rocket);
    }

    /// <summary>The condition seam the rocket's radius asks, at its smallest:
    /// the holder is in the blast jump or is not, as <see cref="BlastJumping"/> sets.</summary>
    public sealed class RocketConditions : ITFPlayerConditions
    {
        public bool BlastJumping { get; set; }

        public TFConditions? Of(int playerIndex) => null;
        public TFCondAdd Add(EntityHandle player, TFCond cond, float duration = TFConditions.Permanent, EntityHandle provider = default)
            => TFCondAdd.Refused;
        public bool Remove(EntityHandle player, TFCond cond, bool ignoreDuration = false) => false;
        public TFCondMask RemoveAll(EntityHandle player) => default;
        public bool InCond(EntityHandle player, TFCond cond) => BlastJumping && cond == TFCond.Blastjumping;
    }

    /// <summary>The condition answers the rocket's radius arm reads.</summary>
    public RocketConditions RocketConds { get; } = new();

    /// <summary>A pipebomb with its areas bound.</summary>
    public TFPipebomb Pipebomb()
    {
        var pipe = new TFPipebomb(Engine, Blast, Engine, Directory, Owners, Hooks, Engine, Skins, Engine, Engine,
                                  Engine, Scopes, HitGroups, Carriers, Conditions);
        pipe.BindAreas(TestServices.Areas(Engine));
        return BindStates(pipe);
    }

    /// <summary>A syringe with its areas bound.</summary>
    public TFSyringe Syringe()
    {
        var syringe = new TFSyringe(Engine, Motion, Engine, Scopes, HitGroups);
        syringe.BindAreas(TestServices.Areas(Engine));
        return BindStates(syringe);
    }

    /// <summary>An arrow with its areas bound, launchable by the effects.</summary>
    public TFArrow Arrow()
    {
        var arrow = new TFArrow(Engine, Motion, new TF2.Tests.Combat.ScopedDamage(Scopes), Scopes, HitGroups,
                                Directory, Owners, Hooks, Engine, Temps, Engine);
        arrow.BindAreas(TestServices.Areas(Engine));
        return BindStates(arrow);
    }

    /// <summary>A flare with its areas bound, over <see cref="Conditions"/>.</summary>
    public TFFlare Flare()
    {
        var flare = new TFFlare(Engine, Motion, Engine, Scopes, HitGroups, Conditions, Engine,
                                Blast, Hooks, Owners, FlareTray, Carriers, Engine, Random);
        flare.BindAreas(TestServices.Areas(Engine));
        return BindStates(flare);
    }

    /// <summary>A jar with its areas bound, over <see cref="Wet"/> and the temp sink.</summary>
    public TFJar Jar()
    {
        var jar = new TFJar(Engine, Motion, Engine, Wet, Engine, Temps, Engine, Engine, Owners, Hooks, Directory);
        jar.BindAreas(TestServices.Areas(Engine));
        return BindStates(jar);
    }

    /// <summary>
    /// A healing bolt with its areas bound, over the medigun, notice and
    /// record fakes below: everything <see cref="TFHealingBoltFacts"/> reads.
    /// </summary>
    /// <summary>The heal-stat sink the bolt's booking pays.</summary>
    public FakeStats Stats { get; } = new();

    public TFHealingBolt Bolt()
    {
        var bolt = new TFHealingBolt(Engine, Motion, Engine, Scopes, HitGroups, Engine, Owners, Hooks,
                                     Conditions, Mediguns, Health, Notices, Engine, Engine, Engine, Records, Stats);
        bolt.BindAreas(TestServices.Areas(Engine));
        return BindStates(bolt);
    }

    /// <summary>The wounds the combat layer has stood on record, for the uber's age scale.</summary>
    public TFDamageRecords Records { get; } = new();

    /// <summary>
    /// An energy ring with its areas bound, over the owners and hooks fakes:
    /// what the energy facts build with their own hands.
    /// </summary>
    public TFProjectileEnergyRing Ring()
    {
        var ring = new TFProjectileEnergyRing(Engine, Motion, Engine, Scopes, HitGroups, Owners, Hooks, Engine, Engine,
                                              Temps, Engine, Engine);
        ring.BindAreas(TestServices.Areas(Engine));
        return BindStates(ring);
    }

    /// <summary>The shakes the ball's impact sent, beside the ring's own sink.</summary>
    public BallShakeRecorder Screen { get; } = new();

    /// <summary>An energy ball with its areas bound, over the blast, owners and hooks fakes.</summary>
    public TFProjectileEnergyBall Ball()
    {
        var ball = new TFProjectileEnergyBall(Engine, Motion, Blast, Owners, Hooks, Engine, Temps, Engine, Screen,
                                              Engine);
        ball.BindAreas(TestServices.Areas(Engine));
        return BindStates(ball);
    }

    /// <summary>The shooter's medigun, as the uber charge asks it.</summary>
    public MedigunSpies Mediguns { get; } = new();

    /// <summary>The pickup events, as the heal's two ask the HUD.</summary>
    public NoticeSpies Notices { get; } = new();

    /// <summary>The medigun seam at its smallest: a level and its writes.</summary>
    public sealed class MedigunSpies : ITFMedigunWeapons
    {
        public float Level { get; set; }

        /// <summary>Every <see cref="SetChargeLevel"/>, as (whose, what).</summary>
        public List<(int Owner, float Level)> Writes { get; } = new();

        public void TickMedic(int ownerIndex, float now, float frametime) { }
        public void Holster(int ownerIndex, float now) { }
        public void Unholster(int ownerIndex, float now) { }
        public void RemoveMedigun(int ownerIndex, float now) { }
        public void PreserveCharge(int ownerIndex, float amount) { }
        public float ChargeLevel(int ownerIndex) => Level;
        public void SetChargeLevel(int ownerIndex, float level)
        {
            Level = level;
            Writes.Add((ownerIndex, level));
        }

        public int HealingTargetIndex(int ownerIndex) => -1;
        public bool IsReleasing(int ownerIndex) => false;
        public bool SubtractCharge(int ownerIndex, float amount) => false;
    }

    /// <summary>The notice seam at its smallest: the heal events, recorded.</summary>
    public sealed class NoticeSpies : ITFPickupNotices
    {
        public List<(EntityHandle Patient, EntityHandle Healer, int Amount)> Healed { get; } = new();
        public List<(EntityHandle Player, int Amount)> HealOnHit { get; } = new();
        public List<(EntityHandle Healer, EntityHandle Healed, int Amount)> Crossbow { get; } = new();

        public void ItemPickup(EntityHandle player, string className) { }
        public void ItemPickupEvent(EntityHandle player, string item) { }
        public void PlayerHealed(EntityHandle patient, EntityHandle healer, int amount) => Healed.Add((patient, healer, amount));
        public void PlayerHealOnHit(EntityHandle player, int amount) => HealOnHit.Add((player, amount));
        public void CrossbowHeal(EntityHandle healer, EntityHandle healed, int amount) => Crossbow.Add((healer, healed, amount));
        public void PlayerStealsSandvich(EntityHandle owner, EntityHandle picker) { }
        public void Sound(EntityHandle player, string sound) { }
    }

    /// <summary>
    /// The condition seam the flare's burning-crit reads, at its smallest:
    /// everybody burns or nobody does, as the fact sets <see cref="EveryoneBurns"/>.
    /// </summary>
    public sealed class FlareConditions : ITFPlayerConditions
    {
        public bool EveryoneBurns { get; set; }

        /// <summary>Every cond add, for the facts that read one back.</summary>
        public List<(EntityHandle Player, TFCond Cond, float Duration)> Added { get; } = new();

        public TFConditions? Of(int playerIndex) => null;
        public TFCondAdd Add(EntityHandle player, TFCond cond, float duration = TFConditions.Permanent, EntityHandle provider = default)
        {
            Added.Add((player, cond, duration));
            return TFCondAdd.Refused;
        }
        public bool Remove(EntityHandle player, TFCond cond, bool ignoreDuration = false) => false;
        public TFCondMask RemoveAll(EntityHandle player) => default;
        public bool InCond(EntityHandle player, TFCond cond) => EveryoneBurns && cond == TFCond.Burning;
    }

    /// <summary>The condition answers the flare's crit arm reads.</summary>
    public FlareConditions Conditions { get; } = new();

    /// <summary>
    /// The wet seams the jar's splash writes, recorded: the conditions added
    /// and removed, who burns, and the condition states the dry gates read.
    /// </summary>
    public sealed class JarConditions : ITFPlayerConditions
    {
        public readonly List<(EntityHandle Player, TFCond Cond, float Duration, EntityHandle Provider)> Added = new();
        public readonly List<(EntityHandle Player, TFCond Cond)> Removed = new();
        public readonly HashSet<EntityHandle> Burning = new();
        public readonly Dictionary<int, TFConditions> States = new();

        public TFConditions? Of(int playerIndex) => States.TryGetValue(playerIndex, out var state) ? state : null;

        public TFCondAdd Add(EntityHandle player, TFCond cond, float duration = TFConditions.Permanent, EntityHandle provider = default)
        {
            Added.Add((player, cond, duration, provider));
            return TFCondAdd.Added;
        }

        public bool Remove(EntityHandle player, TFCond cond, bool ignoreDuration = false)
        {
            Removed.Add((player, cond));
            return true;
        }

        public TFCondMask RemoveAll(EntityHandle player) => default;

        public bool InCond(EntityHandle player, TFCond cond) => cond == TFCond.Burning && Burning.Contains(player);
    }

    /// <summary>The wet seams the jar's splash asks.</summary>
    public JarConditions Wet { get; } = new();

    /// <summary>
    /// The registry's bind, for an entity built by hand: its netstates get
    /// managed storage, so the trail writes land somewhere.
    /// </summary>
    private static T BindStates<T>(T entity) where T : ScriptedEntity
    {
        entity.NetStates = NetStateBinding.AttachLocal(entity, entity.Handle, new NetStateFields(new RecordingLog()));
        return entity;
    }

    /// <summary>TF classes as the facts set them.</summary>
    public sealed class FixedClasses : ITFPlayerClasses
    {
        public TFClass Class { get; set; } = TFClass.Soldier;

        public TFClass ClassOf(EntityHandle player) => Class;
    }

    /// <summary>The one launcher item the facts set, for every ask.</summary>
    public sealed class FixedOwners : ITFWeaponOwners
    {
        public IAttributeProvider? Item { get; set; }

        /// <summary>The player's own manager, for the hooks that read it (damage_force_reduction).</summary>
        public IAttributeProvider? Manager { get; set; }

        /// <summary>The crouch answer, for the rocket muzzle's folded offset.</summary>
        public bool Ducking { get; set; }

        /// <summary>The held weapon, for the arms that read it off the victim.</summary>
        public IAttributeProvider? Active { get; set; }

        public TFClass ClassOf(int playerIndex) => TFClass.Undefined;

        /// <summary>The victim's conditions, for the drain telling's visible gate.</summary>
        public TFConditions? Conditions { get; set; }

        public TFConditions? ConditionsOf(int playerIndex) => Conditions;

        public float CritMult(int playerIndex) => 1f;
        public bool WinsCrits(int playerIndex) => false;
        public IAttributeProvider? ItemOf(int playerIndex, string weaponName) => Item;
        public IAttributeProvider? ActiveItemOf(int playerIndex) => Active;
        public IAttributeProvider? ManagerOf(int playerIndex) => Manager;
        public bool OffGround(int playerIndex) => false;
        public bool EyesUnderwater(int playerIndex) => false;
        public bool DuckingOf(int playerIndex) => Ducking;

        /// <summary>The pomson's drains, recorded as (whose, medic fraction, spy fraction).</summary>
        public List<(int Player, float Medic, float Spy)> Drains { get; } = new();

        /// <summary>Whether a meter stood under the last drain, as the real registry answers.</summary>
        public bool MeterStood { get; set; } = true;

        public bool DrainOnHitMeters(int playerIndex, float medicFraction, float spyFraction)
        {
            Drains.Add((playerIndex, medicFraction, spyFraction));
            return MeterStood;
        }
    }

    /// <summary>
    /// The resupply the blast's own-pass heal arm takes health through: every
    /// ask is recorded and answered with its whole amount (the class maxima
    /// stand at zero — no fact reaches for them).
    /// </summary>
    public sealed class Resupply : TF2.Server.Player.ITFPlayerResupply
    {
        /// <summary>Every TakeHealth, as (who, what). The blast facts count and read these.</summary>
        public List<(SourceSharp.Abi.EntityHandle Who, int Amount)> HealthTaken { get; } = new();

        /// <summary>The class ceiling the takes ride against; null answers unbounded.</summary>
        public int? Ceiling { get; set; }

        /// <summary>The <c>ignoreMaxHealth</c> flag of every take, in order.</summary>
        public List<bool> Unbounded { get; } = new();

        public void Regenerate(SourceSharp.Abi.EntityHandle player, bool refillHealthAndAmmo = true) { }
        public int MaxHealth(SourceSharp.Abi.EntityHandle player) => 0;

        public int TakeHealth(SourceSharp.Abi.EntityHandle player, float health, bool ignoreMaxHealth = false)
        {
            Unbounded.Add(ignoreMaxHealth);
            int taken = Ceiling is int ceiling ? Math.Min((int)health, ceiling) : (int)health;
            HealthTaken.Add((player, taken));
            return taken;
        }

        public int MaxAmmo(SourceSharp.Abi.EntityHandle player, TF2.Shared.TFAmmo ammo) => 0;
        public int GiveAmmo(SourceSharp.Abi.EntityHandle player, int count, TF2.Shared.TFAmmo ammo) => count;
    }

    /// <summary>A directory the facts point at one managed entity.</summary>
    public sealed class DirectoryView : IEntityDirectory
    {
        public ScriptedEntity? Instance { get; set; }

        private readonly Dictionary<EntityHandle, ScriptedEntity> _extra = new();

        /// <summary>Points one handle at an entity of its own, over <see cref="Instance"/>.</summary>
        public void Holds(EntityHandle handle, ScriptedEntity entity) => _extra[handle] = entity;

        public ScriptedEntity? Get(EntityHandle handle)
            => _extra.TryGetValue(handle, out var held) ? held : Instance;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => false;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>The last call to a member, or a failure naming the absence.</summary>
    public static EngineCall Must(EngineCall? call, string member)
        => call ?? throw new InvalidOperationException($"the engine never saw {member}");
}

/// <summary>
/// The uniform stream as a dial: every draw lands at the fraction
/// <see cref="At"/> of its band, so a fact names the jitter it expects.
/// </summary>
internal sealed class DialRandom : IUniformRandom
{
    /// <summary>Where each draw lands in its band: 0 is min, 1 is max.</summary>
    public float At { get; set; } = 0.5f;

    public float RandomFloat(float min, float max) => min + (max - min) * At;
    public int RandomInt(int min, int max) => min + (int)((max - min) * At);
}

public class TFProjectileDefsFacts
{
    [Fact]
    public void StickySpeedSpansTheCharge()
    {
        Assert.Equal(900f, TFProjectileDefs.StickySpeed(0f));
        Assert.Equal(2400f, TFProjectileDefs.StickySpeed(TFProjectileBehaviour.MaxChargeTime));
        Assert.Equal(1650f, TFProjectileDefs.StickySpeed(TFProjectileBehaviour.MaxChargeTime / 2f));
        Assert.Equal(1650f, TFProjectileDefs.StickySpeed(TFProjectileBehaviour.MaxChargeTime, 8f));
    }

    [Fact]
    public void AYoungStickyCarriesTheDistanceModForItsFirstFiveSeconds()
    {
        // GetDamageType (tf_weapon_grenade_pipebomb.cpp:153-159): age < 5.0.
        DamageType row = TFWeaponDamageTypes.Of(TFWeaponId.Pipebomblauncher);
        Assert.Equal(row | TFDamageBits.UseDistanceMod, TFProjectileDefs.YoungStickyBits(row, 4.9f));
        Assert.Equal(row, TFProjectileDefs.YoungStickyBits(row, 5f));
    }
}

public class TFBlastApplierFacts
{
    private static TFBlast RocketBlast()
        => TFBlast.Of(90f, TFProjectileDefs.RocketRadius, TFWeaponDamageTypes.Of(TFWeaponId.Rocketlauncher));

    [Fact]
    public void VictimInTheOpenTakesTheFalloffedDamage()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        // The victim is not a player, so stock scores it at the trace's end.
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 1f, new Vector(60f, 0f, 0f), default, EntityHandle.None));

        // The rocket launcher's own bits (g_aWeaponDamageTypes) carry
        // DMG_HALF_FALLOFF: the blast keeps half at the radius's edge, so at
        // 60 of 146 units 90 has fallen to 71.5, not the linear 75.8.
        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        var hit = rig.Engine.Last("Damage.TakeDamageAnswered");
        Assert.NotNull(hit);
        Assert.Equal(ProjectileRig.Victim, hit!.ArgAs<EntityHandle>(0));
        Assert.Equal(71.5f, hit.ArgAs<float>(2), 1f);
    }

    [Fact]
    public void AnOwnerThatIsNoPlayerTakesItsOwnSweepAsDealt()
    {
        // With no own pass armed the owner is one of the sweep's victims
        // (tf_gamerules.cpp:5638-5640). The self arms are a player's —
        // CTFPlayer::OnTakeDamage's soldier 0.6 and the player block of
        // ApplyOnDamageAliveModifyRules (:7115) — so an owner that is no
        // player keeps the falloffed 71.5 whatever the class seam answers
        // (the rig's is a soldier, off the ground: 42.9 for a player).
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Player);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 1f, new Vector(60f, 0f, 0f), default, EntityHandle.None));

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        var hit = Assert.Single(rig.Engine.AllOf("Damage.TakeDamageAnswered"));
        Assert.Equal(ProjectileRig.Player, hit.ArgAs<EntityHandle>(0));
        Assert.Equal(71.5f, hit.ArgAs<float>(2), 1f);
    }

    /// <summary>A body that is no player, at 60 out on the push's own level
    /// (the source's 1 less the push's 10 of lift), in a box of its own.</summary>
    private static ProjectileRig BodyAtSixty(Vector mins, Vector maxs)
    {
        var rig = new ProjectileRig();
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, -9f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, -9f));
        rig.Engine.Returns("Entities.GetBounds", (mins, maxs));
        // Not a player, so stock scores it at the trace's end, 60 out.
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 1f, new Vector(60f, 0f, 1f), default, EntityHandle.None));
        return rig;
    }

    [Fact]
    public void ABodyThatRunsTheDamageRulesIsHandedTheBlastsPushWithTheHit()
    {
        // Stock's ApplyPushFromDamage (tf_player.cpp:10365): the push from
        // someone else's blast is -DamageForce(WorldAlignSize, damage, 6)
        // along the line from the blast's centre, 10 low, to the victim's.
        // A body that is no player but runs the rules is handed that push as
        // its hit's force, measured on ITS OWN box (40 x 40 x 60, not a
        // player's 48 x 48 x 82), at the blast's centre:
        // 71.5 * (48*48*82 / 40*40*60) * 6 = 844.3, away from the blast.
        var rig = BodyAtSixty(new Vector(-20f, -20f, 0f), new Vector(20f, 20f, 60f));
        rig.Directory.Holds(ProjectileRig.Victim, new TF2.Tests.Combat.OtherVictim());
        rig.Engine.FillsSphere(ProjectileRig.Victim);

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        var hit = Assert.Single(rig.Engine.AllOf("Damage.ApplyAt"));
        Assert.Equal(ProjectileRig.Victim, hit.ArgAs<EntityHandle>(0));
        Assert.Equal(71.5f, hit.ArgAs<float>(2), 0.1f);
        Vector force = hit.ArgAs<Vector>(4);
        Assert.Equal(844.3f, force.X, 0.5f);
        Assert.Equal(0f, force.Y, 0.001f);
        Assert.Equal(0f, force.Z, 0.001f);
        Assert.Equal(new Vector(0f, 0f, 1f), hit.ArgAs<Vector>(5));
    }

    [Fact]
    public void ABodyThatRunsTheDamageRulesIsHandedItsOwnBlastsSelfPush()
    {
        // The own pass (tf_gamerules.cpp:5661-5714) on an owner that is no
        // player: 90 at 60 of the rocket-jump radius 121, half kept at the
        // edge, is 67.69; the self arm's push (tf_player.cpp:10297) is
        // -DamageForce(size, damage, 9) on its own 60 x 60 x 80 box:
        // 67.69 * (48*48*82 / 60*60*80) * 9 = 399.6.
        var rig = BodyAtSixty(new Vector(-30f, -30f, 0f), new Vector(30f, 30f, 80f));
        rig.Directory.Holds(ProjectileRig.Player, new TF2.Tests.Combat.OtherVictim());
        rig.Engine.FillsSphere();

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false,
                        selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        var hit = Assert.Single(rig.Engine.AllOf("Damage.ApplyAt"));
        Assert.Equal(ProjectileRig.Player, hit.ArgAs<EntityHandle>(0));
        Assert.Equal(67.69f, hit.ArgAs<float>(2), 0.1f);
        Assert.Equal(399.6f, hit.ArgAs<Vector>(4).X, 0.5f);
    }

    [Fact]
    public void NothingNearMeansNoHit()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere();

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void TheCritFlagRidesTheHit(bool critical, bool expected)
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));

        bool? seen = null;
        rig.Engine.WhenCalled("Damage.TakeDamageAnswered", _ => seen = rig.HitGroups.Critical);

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical);

        Assert.Equal(expected, seen);
    }

    [Fact]
    public void TheWeaponRidesTheScope()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));

        (TFWeaponId Weapon, string Name)? seen = null;
        rig.Engine.WhenCalled("Damage.TakeDamageAnswered", call =>
        {
            rig.Scopes.TryCurrent(out _, out var weapon, out var name);
            seen = (weapon, name);
        });

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        Assert.Equal((TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher"), seen);
    }

    [Fact]
    public void TheInflictorRidesItsScope()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));

        EntityHandle? seen = null;
        bool? scoped = null;
        rig.Engine.WhenCalled("Damage.TakeDamageAnswered", call =>
        {
            scoped = rig.Inflictors.TryCurrent(out var inflictor, out _);
            seen = inflictor;
        });

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        // The blast's own inflictor: the rocket the damage layer's projectile
        // arms cast (tf_gamerules.cpp:6159's info.GetInflictor()).
        Assert.True(scoped!.Value);
        Assert.Equal(ProjectileRig.ProjectileHandle, seen);
    }

    [Fact]
    public void ThePlayerIsPushedUpward()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        // The blast goes off 60 below the victim's centre: the push points up.
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(0f, 0f, 60f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(0f, 0f, 60f));
        rig.Engine.Returns("Entities.IsPlayer", true);
        // The standing player's own OBB — for a standing victim the same
        // size the class hull answers, so this fact holds either way;
        // AnEnemyIsPushedOnTheirOwnHull is the one that moves.
        rig.Engine.Returns("Entities.GetBounds",
                           (new Vector(-24f, -24f, 0f),
                            new Vector(24f, 24f, TFViewVectors.Params.HullMax.Z)));

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        var push = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(push);
        // The exact impulse: the push's direction and DamageForce over the
        // delivered damage (someone else's blast at ForceScaleOther),
        // measured on the victim's own standing OBB.
        float delivered = rig.Engine.Last("Damage.TakeDamageAnswered")!.ArgAs<float>(2);
        var hull = new Vector(TFViewVectors.Params.HullMax.X * 2f, TFViewVectors.Params.HullMax.Y * 2f,
                              TFViewVectors.Params.HullMax.Z);
        var expected = TFRadiusDamage.Push(TFRadiusDamage.PushDirection(new Vector(0f, 0f, 1f), new Vector(0f, 0f, 60f)),
                                           hull, delivered, self: false, selfScale: 0f, heavy: false);
        Assert.Equal(expected, push!.ArgAs<Vector>(1));
    }

    [Fact]
    public void AnEnemyIsPushedOnTheirOwnHull()
    {
        // DamageForce measures the victim's WorldAlignSize — the enemy arm
        // (:10365) and the self arm (:10245) both read the victim's own OBB,
        // not the standing class hull the port had fixed on. A crouched
        // victim is pushed as the crouch they are: the ducked height, not 82.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(0f, 0f, 60f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(0f, 0f, 60f));
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetBounds",
                           (new Vector(-24f, -24f, 0f),
                            new Vector(24f, 24f, TFViewVectors.Params.DuckHullMax.Z)));

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        var push = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(push);
        float delivered = rig.Engine.Last("Damage.TakeDamageAnswered")!.ArgAs<float>(2);
        var crouched = new Vector(TFViewVectors.Params.DuckHullMax.X * 2f,
                                  TFViewVectors.Params.DuckHullMax.Y * 2f,
                                  TFViewVectors.Params.DuckHullMax.Z);
        var expected = TFRadiusDamage.Push(
            TFRadiusDamage.PushDirection(new Vector(0f, 0f, 1f), new Vector(0f, 0f, 60f)),
            crouched, delivered, self: false, selfScale: 0f, heavy: false);
        Assert.Equal(expected, push!.ArgAs<Vector>(1));
    }

    [Fact]
    public void TheCrouchedSelfPassIsMeasuredAtTheStockFiftyFive()
    {
        // The self arm folds a crouched hull to 55 high (:10248-10253): the
        // ducked 62 would jump lower, the stock 55 jumps higher. The bounds
        // are the victim's own, so the fold reads the crouch the engine
        // answers and folds only its height.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(); // nobody in the sweep but the attacker's own pass
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(100f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(100f, 0f, 0f));
        rig.Engine.Returns("Entities.GetBounds",
                           (new Vector(-24f, -24f, 0f),
                            new Vector(24f, 24f, TFViewVectors.Params.DuckHullMax.Z)));

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false,
                        selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        var push = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(push);
        float delivered = rig.Engine.Last("Damage.TakeDamageAnswered")!.ArgAs<float>(2);
        var folded = new Vector(TFViewVectors.Params.DuckHullMax.X * 2f,
                                TFViewVectors.Params.DuckHullMax.Y * 2f, 55f);
        var expected = TFRadiusDamage.Push(
            TFRadiusDamage.PushDirection(new Vector(0f, 0f, 1f), new Vector(100f, 0f, 0f)),
            folded, delivered, self: true,
            TFRadiusDamage.SelfPushScale(soldier: true, blast: true, onGround: false, pyroFlareJump: false),
            heavy: false);
        Assert.Equal(expected, push!.ArgAs<Vector>(1));
    }

    [Fact]
    public void TheSelfPassHitsTheAttackerAtItsOwnRadius()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(); // nobody else in range
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(100f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(100f, 0f, 0f));

        // The stock launcher's own pass: 90 at the rocket-jump radius (121).
        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false,
                        selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        var hit = rig.Engine.Last("Damage.TakeDamageAnswered");
        Assert.NotNull(hit);
        Assert.Equal(ProjectileRig.Player, hit!.ArgAs<EntityHandle>(0));

        var push = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(push);
    }

    //=========================================================================
    // THE ROCKET JUMP. Stock measured live (ctf_2fort, soldier at 89 down, a
    // rocket at the feet in the jump): 83.03 out of RadiusDamage, 49.82 at the
    // push, force (-12.4, -87.6, 490.3). tf_player.cpp:8921 and :9155-9158.
    //=========================================================================

    private static (ProjectileRig Rig, float Damage, Vector Impulse) SelfBlast(
        EntityState state, TFClass cls = TFClass.Soldier, byte waterLevel = 0)
    {
        var rig = new ProjectileRig();
        rig.Classes.Class = cls;
        rig.Engine.Returns("Networking.EntityGetState", state);
        rig.Engine.FillsSphere();
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(0f, 0f, 41f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f));
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-24f, -24f, 0f), new Vector(24f, 24f, 82f)));

        var player = new SsPlayer(rig.Engine, rig.Engine, TestServices.Impacts(rig.Engine), TestServices.Fluid(), TestServices.InfiniteAux(), TestServices.PrintDamage())
            { Handle = ProjectileRig.Player };
        player.State.BindLocal();
        player.State.WaterLevel = waterLevel;
        rig.Directory.Holds(ProjectileRig.Player, player);

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false,
                        selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        return (rig,
                rig.Engine.Last("Damage.TakeDamageAnswered")!.ArgAs<float>(2),
                rig.Engine.Last("Entities.ApplyVelocityImpulse")!.ArgAs<Vector>(1));
    }

    [Fact]
    public void ASoldierInTheAirTakesSixTenthsAndIsThrownAtTheSixTenths()
    {
        (_, float grounded, _) = SelfBlast(EntityState.OnGround);
        (_, float damage, Vector impulse) = SelfBlast(EntityState.None);

        Assert.Equal(grounded * 0.6f, damage, 0.01f);
        Assert.Equal(damage * 10f, impulse.Z, 0.5f);
    }

    [Fact]
    public void ASoldierOnTheFloorTakesItAllAndIsThrownAtHalfTheScale()
    {
        (_, float damage, Vector impulse) = SelfBlast(EntityState.OnGround);

        Assert.Equal(90f, damage, 0.5f);
        Assert.Equal(damage * 5f, impulse.Z, 0.5f);
    }

    /// <summary>The grounded push in whole numbers: 90 damage at the soldier's
    /// grounded scale of 5 (:10320-10335), exactly 450, and that sits below
    /// TFRadiusDamage.MaxForce's cap of 1000 (:10336), so the cap never trims
    /// a soldier's own jump launch.</summary>
    [Fact]
    public void TheGroundedSoldierPushIsFourHundredFiftyUnderTheCap()
    {
        (_, float damage, Vector impulse) = SelfBlast(EntityState.OnGround);

        Assert.Equal(90f, damage, 0.5f);
        Assert.Equal(450f, impulse.Z, 3f);
        Assert.True(impulse.Z < TFRadiusDamage.MaxForce,
                    $"grounded push {impulse.Z} must sit below the cap {TFRadiusDamage.MaxForce}");
    }

    /// <summary>The scale's FL_INWATER test; the push's own arm reads only the ground (:10265).</summary>
    [Fact]
    public void ASoldierInTheWaterTakesItAllAndIsThrownAtTheJumpsScale()
    {
        (_, float damage, Vector impulse) = SelfBlast(EntityState.None, waterLevel: 1);

        Assert.Equal(90f, damage, 0.5f);
        Assert.Equal(damage * 10f, impulse.Z, 0.5f);
    }

    [Fact]
    public void AnotherClassInTheAirTakesItAll()
    {
        (_, float damage, _) = SelfBlast(EntityState.None, TFClass.Demoman);

        Assert.Equal(90f, damage, 0.5f);
    }

    [Fact]
    public void TheSelfPassAddsOnlyTheImpulseAndLeavesTheGroundToMovement()
    {
        // Stock's ApplyPushFromDamage adds the impulse and nothing else
        // (:10470); the rise leaves the floor through PlayerMove's own test
        // (gamemovement.cpp:4612, held by the movement facts in
        // src/test/server/MovementTests.cs), so both passes hand over the
        // impulse with the ground untouched.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(); // nobody else in range
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(100f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(100f, 0f, 0f));

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false,
                        selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        var push = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(push);
        Assert.False(push!.ArgAs<bool>(2));
    }

    [Fact]
    public void TheAttackerIsSkippedInTheMainSweepWhenTheOwnPassArms()
    {
        // Stock (:5638-5640): with a rocket-jump radius set the attacker is
        // left out of the sweep and hit after it, so the blast hurts them
        // exactly once — their own pass — never twice.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Player); // only the attacker is near
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false,
                        selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        Assert.Equal(1, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
    }

    [Fact]
    public void TheSelfPassReachesTheAttackersOwnBounds()
    {
        // :5708-5710 retests the attacker against the own pass's radius at
        // CalcNearestPoint — the nearest point of the attacker's collision
        // box, not its centre. At an origin 130 out with a 24-half-wide hull
        // the nearest point is 106, inside the rocket-jump 121: 90 halved at
        // the clamped distance is 45, for a soldier standing on the floor.
        var rig = new ProjectileRig();
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.FillsSphere();
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(130f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(130f, 0f, 0f));
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-24f, -24f, 0f), new Vector(24f, 24f, 82f)));

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false,
                        selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        var hit = rig.Engine.Last("Damage.TakeDamageAnswered");
        Assert.NotNull(hit);
        Assert.Equal(ProjectileRig.Player, hit!.ArgAs<EntityHandle>(0));
        Assert.Equal(45f, hit.ArgAs<float>(2), 1f);
    }

    [Fact]
    public void TheMainSweepScoresTheVictimsOwnBounds()
    {
        // :5642-5646 retests each sphere result at CalcNearestPoint — the
        // nearest point of the victim's collision box, not its centre. A
        // centre at 160 of the 146 radius is out by the centre but its
        // 24-half-wide box reaches in to 136: the hit lands, at the clamped
        // 45.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(160f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(160f, 0f, 0f));
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-24f, -24f, 0f), new Vector(24f, 24f, 82f)));
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 1f, new Vector(160f, 0f, 0f), default, EntityHandle.None));

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        var hit = rig.Engine.Last("Damage.TakeDamageAnswered");
        Assert.NotNull(hit);
        Assert.Equal(ProjectileRig.Victim, hit!.ArgAs<EntityHandle>(0));
        Assert.Equal(45f, hit.ArgAs<float>(2), 1f);
    }

    [Fact]
    public void ABlastWithNoRadiusStillRunsTheOwnPass()
    {
        // A zero-radius main sweep is skipped, not the whole Apply: a script
        // that names no radius still jumps its shooter.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere();
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(100f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(100f, 0f, 0f));

        var noRadius = TFBlast.Of(90f, 0f, TFWeaponDamageTypes.Of(TFWeaponId.Rocketlauncher));
        rig.Blast.Apply(new Vector(0f, 0f, 0f), noRadius, ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false,
                        selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        Assert.Equal(1, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
    }

    [Fact]
    public void ASolidNonvictimBetweenSparesTheVictim()
    {
        // :5755-5788: a blocked trace to anyone but the victim themselves
        // spares them — a prop in the way counts, not just world.
        var rig = new ProjectileRig();
        var blocker = new EntityHandle(0x0003_0000u | 7);
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(false, 0.5f, new Vector(30f, 0f, 0f), default, blocker));

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
    }

    [Fact]
    public void APlayerInTheWayDoesNotBlock()
    {
        // Our seam's trace passes one ignore only; stock's filter ignores
        // players too, so a player between blast and victim does not spare
        // them. This holds the approximation in place.
        var rig = new ProjectileRig();
        var otherPlayer = new EntityHandle(0x0001_0000u | 6);
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(false, 0.5f, new Vector(30f, 0f, 0f), default, otherPlayer));

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        Assert.Equal(1, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
    }

    [Fact]
    public void AProjectileInTheWayDoesNotBlock()
    {
        // ApplyToEntity's filter chain (tf_gamerules.cpp:5765-5769): the
        // occlusion trace ignores players, projectiles and the attacker's
        // friendly combat items, so only another solid blocks. A rocket in
        // the way does not spare the victim.
        var rig = new ProjectileRig();
        var flying = new EntityHandle(0x0003_0000u | 8);
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetClassName", "tf_projectile_rocket");
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(false, 0.5f, new Vector(30f, 0f, 0f), default, flying));

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        Assert.Equal(1, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
    }

    [Fact]
    public void AVictimTheRadiusDoesNotReachTakesNothing()
    {
        // The sphere query is a box test, so stock retests each result
        // against the radius (CalcNearestPoint, :5642-5646): a box whose
        // nearest point is 176 out of the 146 radius takes nothing, however
        // the query answered.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(200f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(200f, 0f, 0f));
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-24f, -24f, 0f), new Vector(24f, 24f, 82f)));

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
    }

    [Fact]
    public void ABlastHandsANonPlayerVictimItsDirectionAwayFromTheCentre()
    {
        // CTFRadiusDamageInfo::ApplyToEntity (tf_gamerules.cpp:5846-5860):
        // the hit carries dir — the normalized line away from the blast's
        // lifted centre to the victim — and the centre itself as the force
        // position. With no force on the info the engine manufactures one
        // (CalculateExplosiveDamageForce, takedamageinfo.cpp:301-327): the
        // answered damage times 75 kg x 4 in/s, clamped at 30000, fudged
        // 0.85-1.15 through the engine's own random stream. What managed
        // hands over is the direction and that centre: the victim at
        // (60, 0, 0) of a blast at (0, 0, 0) takes
        // (60, 0, -1) normalized, the centre lifted by the same 1 stock
        // lifts vecSrc (:5635).
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 1f, new Vector(60f, 0f, 0f), default, EntityHandle.None));

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        var hit = rig.Engine.Last("Damage.TakeDamageAnswered");
        Assert.NotNull(hit);

        // A missing direction reads as the zeroed default, so the away-from-
        // the-centre components below are also the non-null proof.
        Vector direction = hit!.ArgAs<Vector>(4);
        Assert.Equal(0.9999f, direction.X, 4);   // away from the centre, along +x
        Assert.Equal(0f, direction.Y, 4);
        Assert.Equal(-0.0167f, direction.Z, 4);  // the lifted centre tips it down

        Assert.Equal(new Vector(0f, 0f, 1f), hit.ArgAs<Vector>(5)); // vecSrc itself
    }

    [Fact]
    public void AnEnemyRocketBlastBesideAStuckStickyKnocksItLoose()
    {
        // OnTakeDamage of the stuck bomb (tf_weapon_grenade_pipebomb.cpp:
        // 1046-1069): an enemy blow carrying DMG_BLAST scales the hit's
        // force by tf_grenade_forcefrom_blast and past
        // tf_pipebomb_force_to_move re-animates the prop. The force reaches
        // the bomb through the answered ask's direction, so an enemy
        // rocket's blast beside the sticky has to hand one over — and the
        // bomb, given the force the ask carried, detaches.
        var rig = new ProjectileRig();
        var enemy = new EntityHandle(0x0001_0000u | 6);
        rig.StickyVars.ForceFromBlast.Hold("0.15");
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == enemy ? 3 : 2);
        rig.Engine.Returns("Clock.Now", 10f);
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 1f, new Vector(60f, 0f, 0f), default, EntityHandle.None));

        var sticky = rig.Sticky();
        sticky.OnSpawn();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        rig.Engine.Returns("Entities.GetIndex", 0); // the toucher is the world
        sticky.OnTouch(new EntityHandle(0x0001_0000u)); // the world: it sticks
        rig.Queue.Run(); // the pin waits for the step's end
        Assert.Equal(1, rig.Engine.CountOf("EntityPhysics.EnableMotion"));

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        var hit = rig.Engine.Last("Damage.TakeDamageAnswered");
        Assert.NotNull(hit);
        var direction = hit!.ArgAs<Vector>(4);
        Assert.True(direction.LengthSquared > 0f, $"the handed force is not zero ({direction})");

        int moved = sticky.OnDamagedAt(enemy, hit.ArgAs<float>(2), hit.ArgAs<DamageType>(3),
                                       hit.ArgAs<Vector>(5), direction);
        Assert.Equal(1, moved);
        Assert.Equal(2, rig.Engine.CountOf("EntityPhysics.EnableMotion")); // re-animated
        Assert.True(rig.Engine.Last("EntityPhysics.EnableMotion")!.ArgAs<bool>(1));
    }

    [Fact]
    public void ThePlayersPushNumbersSurviveTheHandedForce()
    {
        // The player's push is stock's ApplyPushFromDamage inside
        // OnTakeDamage_Alive (tf_player.cpp:10232): the managed Push owns
        // it, and the force the answered ask now carries (stock's
        // :5846-5860, what moves props and stickies) must not double it —
        // the impulse keeps the numbers it had before the ask carried a
        // direction at all. Half-falloff damage 71.5; the other's push is
        // DamageForce at scale 6 along the lift-10 line: (60, 0, -9)
        // normalized times 429, minus-signs folded in.
        var rig = new ProjectileRig();
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-24f, -24f, 0f), new Vector(24f, 24f, 82f)));
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 1f, new Vector(60f, 0f, 0f), default, EntityHandle.None));

        rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                        ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                        TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

        Assert.Equal(1, rig.Engine.CountOf("Damage.TakeDamageAnswered")); // the hit
        Assert.Equal(1, rig.Engine.CountOf("Entities.ApplyVelocityImpulse")); // pushed once

        var impulse = EngineCallFacts.ArgAs<Vector>(rig.Engine.Last("Entities.ApplyVelocityImpulse"), 1);
        Assert.Equal(424.3f, impulse.X, 1);
        Assert.Equal(0f, impulse.Y, 1);
        Assert.Equal(63.6f, impulse.Z, 1);
    }
}

public class TFGrenadePhysicsFacts
{
    [Fact]
    public void TheGrenadeIsBornAtStocksInitGrenadePhysics()
    {
        // InitGrenade's SetGravity( 0.4f ) / SetFriction( 0.2f )
        // (tf_weaponbase_grenadeproj.cpp:245-246) — not the dead
        // TF_WEAPON_PIPEBOMB_GRAVITY/FRICTION defines (0.5/0.8), which the
        // spawn path never reads. InitGrenade's SetElasticity( 0.45f ) has
        // one reader — VPhysicsUpdate's bounce off an enemy (:784), not
        // ported (docs/plans/tf2/deferred.tsv).
        Assert.Equal(0.4f, TFProjectileDefs.PipebombGravity);
        Assert.Equal(0.2f, TFProjectileDefs.PipebombFriction);
    }

    [Fact]
    public void ThePipebombCarriesTheFrictionToItsProp()
    {
        var rig = new ProjectileRig();
        var pipe = rig.Pipebomb();

        pipe.OnSpawn();

        var friction = rig.Engine.Last("EntityPhysics.SetFriction");
        Assert.NotNull(friction);
        Assert.Equal(0.2f, friction!.ArgAs<float>(1), 3);
    }

    [Fact]
    public void TheStickyCarriesTheFrictionToItsProp()
    {
        var rig = new ProjectileRig();
        var sticky = rig.Sticky();

        sticky.OnSpawn();

        var friction = rig.Engine.Last("EntityPhysics.SetFriction");
        Assert.NotNull(friction);
        Assert.Equal(0.2f, friction!.ArgAs<float>(1), 3);
    }

    [Fact]
    public void TheStickySticksOnItsFirstWorldContact()
    {
        // VPhysicsCollision (tf_weapon_grenade_pipebomb.cpp:928): a world
        // contact sticks it, armed or not — the ninth-sticky rule needs no
        // arming to have happened first.
        var rig = new ProjectileRig();
        var sticky = rig.Sticky();
        sticky.OnSpawn();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        rig.Engine.Returns("Entities.GetIndex", 0);

        sticky.OnTouch(new EntityHandle(0x0001_0000u)); // the world, entry 0
        rig.Queue.Run(); // the pin waits for the step's end

        var motion = rig.Engine.Last("EntityPhysics.EnableMotion");
        Assert.NotNull(motion);
        Assert.False(motion!.ArgAs<bool>(1));
    }

    [Fact]
    public void AStuckStickyStillArmsAndDetonatesOnPulse()
    {
        // PreArmThink schedules the arm think at spawn
        // (tf_weapon_grenade_pipebomb.cpp:1142-1145) and ArmThink detonates
        // on m_bDetonateOnPulse without asking the stick (:1175-1178):
        // VPhysicsCollision's stick (:928) touches no arming.
        var rig = new ProjectileRig();
        var sticky = rig.Sticky();
        sticky.OnSpawn();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        rig.Engine.Returns("Entities.GetIndex", 0);
        sticky.OnTouch(new EntityHandle(0x0001_0000u)); // the world: it sticks
        sticky.ArmDetonateOnPulse();

        sticky.OnThink(); // the arm think

        Assert.True(sticky.Armed);
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove")); // the pulse went off
    }

    [Fact]
    public void AStickySticksToADynamicProp()
    {
        // VPhysicsCollision (tf_weapon_grenade_pipebomb.cpp:916-928): a
        // dynamic_cast<CDynamicProp*> contact sticks it like the world does;
        // only saw-parented props and training props with the
        // grenade-collision spawnflag are cleared of the arm (:918-925).
        var rig = new ProjectileRig();
        var sticky = rig.Sticky();
        sticky.OnSpawn();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        rig.Engine.Returns("Entities.GetIndex", 42);
        rig.Engine.Returns("Entities.GetClassName", "prop_dynamic");

        sticky.OnTouch(new EntityHandle(0x0002_0000u | 42)); // a prop_dynamic
        rig.Queue.Run(); // the pin waits for the step's end

        var motion = rig.Engine.Last("EntityPhysics.EnableMotion");
        Assert.NotNull(motion);
        Assert.False(motion!.ArgAs<bool>(1));
    }

    [Fact]
    public void AForceMovedStickyDoesNotRestickUntilItsSleepTimePasses()
    {
        // OnTakeDamage (tf_weapon_grenade_pipebomb.cpp:1046-1069): a blow
        // whose scaled force passes tf_pipebomb_force_to_move re-animates the
        // prop and sets m_flMinSleepTime = curtime + tf_grenade_force_sleeptime;
        // the stick itself waits on curtime > m_flMinSleepTime (:928), so the
        // next contact does not re-stick until the sleep time has passed.
        var rig = new ProjectileRig();
        var attacker = new EntityHandle(0x0001_0000u | 6);
        rig.Engine.Returns("Entities.GetIndex", 0);
        rig.Engine.Returns("Clock.Now", 10f);
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == attacker ? 3 : 2);
        rig.StickyVars.ForceFromBullet.Hold("2.0");
        rig.StickyVars.ForceFromBuckshot.Hold("0.75");
        rig.StickyVars.ForceFromBlast.Hold("0.15");
        rig.StickyVars.ForceSleepTime.Hold("1.0");
        rig.StickyVars.ForceToMove.Hold("1500.0");
        var sticky = rig.Sticky();
        sticky.OnSpawn();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);

        sticky.OnTouch(new EntityHandle(0x0001_0000u)); // the world: it sticks
        rig.Queue.Run(); // the pin waits for the step's end
        Assert.Equal(1, rig.Engine.CountOf("EntityPhysics.EnableMotion"));

        var moved = sticky.OnDamagedAt(attacker, 10f, DamageType.Bullet, default,
                                       new Vector(2000f, 0f, 0f)); // past 1500
        Assert.Equal(1, moved);
        Assert.Equal(2, rig.Engine.CountOf("EntityPhysics.EnableMotion")); // re-animated

        rig.Engine.Returns("Clock.Now", 10.5f);
        sticky.OnTouch(new EntityHandle(0x0001_0000u)); // the sleep time still runs
        Assert.Equal(2, rig.Engine.CountOf("EntityPhysics.EnableMotion"));

        rig.Engine.Returns("Clock.Now", 11.5f);
        sticky.OnTouch(new EntityHandle(0x0001_0000u)); // past it: re-sticks
        rig.Queue.Run(); // the re-pin waits for the step's end too
        var restuck = rig.Engine.Last("EntityPhysics.EnableMotion");
        Assert.NotNull(restuck);
        Assert.False(restuck!.ArgAs<bool>(1));
    }

    [Fact]
    public void AStickyTouchingASawParentedPropDoesNotStick()
    {
        // The sawmill exemption (:918-921): a prop parented to
        // sawmovelinear01/02 is cleared of the dynamic-prop arm, however its
        // classname reads.
        var rig = new ProjectileRig();
        rig.Engine.Returns("Entities.GetIndex", 42);
        rig.Engine.Returns("Entities.GetClassName", "prop_dynamic");
        rig.Engine.Returns("Entities.GetParent", new EntityHandle(0x0001_0000u | 7));
        rig.Engine.Returns("Entities.GetName", "sawmovelinear01");
        var sticky = rig.Sticky();
        sticky.OnSpawn();

        sticky.OnTouch(new EntityHandle(0x0002_0000u | 42));

        Assert.Equal(0, rig.Engine.CountOf("EntityPhysics.EnableMotion"));
    }

    [Fact]
    public void ATrainingPropWithoutTheGrenadeCollisionSpawnflagDoesNotStick()
    {
        // PropDynamic_CollidesWithGrenades (tf_gamerules.cpp:19308-19320): a
        // training_prop_dynamic takes a sticky only carrying
        // SF_TF_DYNAMICPROP_GRENADE_COLLISION (512).
        var rig = new ProjectileRig();
        rig.Engine.Returns("Entities.GetIndex", 42);
        rig.Engine.Returns("Entities.GetClassName", "training_prop_dynamic");
        rig.Engine.Returns("Entities.GetSpawnFlags", 0);
        var sticky = rig.Sticky();
        sticky.OnSpawn();

        sticky.OnTouch(new EntityHandle(0x0002_0000u | 42));

        Assert.Equal(0, rig.Engine.CountOf("EntityPhysics.EnableMotion"));
    }

    [Fact]
    public void ATrainingPropWithTheGrenadeCollisionSpawnflagSticks()
    {
        var rig = new ProjectileRig();
        rig.Engine.Returns("Entities.GetIndex", 42);
        rig.Engine.Returns("Entities.GetClassName", "training_prop_dynamic");
        rig.Engine.Returns("Entities.GetSpawnFlags", 512);
        var sticky = rig.Sticky();
        sticky.OnSpawn();

        sticky.OnTouch(new EntityHandle(0x0002_0000u | 42));
        rig.Queue.Run(); // the pin waits for the step's end

        var motion = rig.Engine.Last("EntityPhysics.EnableMotion");
        Assert.NotNull(motion);
        Assert.False(motion!.ArgAs<bool>(1));
    }

    [Fact]
    public void AnArmedSlowStickyStillSticksByTheRestCheck()
    {
        var rig = new ProjectileRig();
        rig.Engine.Returns("Entities.GetVelocity", default(Vector));
        var sticky = rig.Sticky();
        sticky.OnSpawn();

        sticky.OnThink(); // arms
        sticky.OnThink(); // the rest check: still not moving
        rig.Queue.Run(); // the pin waits for the step's end

        var motion = rig.Engine.Last("EntityPhysics.EnableMotion");
        Assert.NotNull(motion);
        Assert.False(motion!.ArgAs<bool>(1));
    }

    /// <summary>
    /// A solver collision record with a sideless, static other side — the
    /// shape the router delivers a world contact in
    /// (<see cref="PhysicsCollisionImpacts"/> names the world's side no entity):
    /// a PostCollision whose flags say collision, the sticky's handle on
    /// <paramref name="mySide"/>.
    /// </summary>
    private static PhysicsEvent WorldCollision(uint stickyHandle, int mySide)
        => new()
        {
            KindRaw = (uint)PhysicsEventKind.PostCollision,
            Flags = PhysicsEvent.FlagCollision,
            Entity0 = mySide == 0 ? stickyHandle : PhysicsEvent.NoEntity,
            Entity1 = mySide == 0 ? PhysicsEvent.NoEntity : stickyHandle,
            Static0 = mySide == 0 ? 0 : 1,
            Static1 = mySide == 0 ? 1 : 0,
        };

    [Fact]
    public void AWorldCollisionEventSticksTheSticky()
    {
        // VPhysicsCollision (tf_weapon_grenade_pipebomb.cpp:928) is where the
        // stick is decided, and the router delivers it to OnVPhysicsCollision —
        // not to OnTouch, which the engine's touch dispatch raises for a
        // trigger's pair and never for a vphysics contact. The floor of the
        // live bug: every world arrival went unheard and the bomb rolled.
        var rig = new ProjectileRig();
        var sticky = rig.Sticky();
        sticky.OnSpawn();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);

        var ev = WorldCollision(sticky.Handle.Value, mySide: 1);
        sticky.OnVPhysicsCollision(in ev, 1);
        rig.Queue.Run(); // the pin waits for the step's end

        var motion = rig.Engine.Last("EntityPhysics.EnableMotion");
        Assert.NotNull(motion);
        Assert.False(motion!.ArgAs<bool>(1));
    }

    [Fact]
    public void TheCollisionStickWaitsForTheStepToEnd()
    {
        // g_PostSimulationQueue.QueueCall( EnableMotion, false ) (:932): the
        // record arrives inside the solver's callback, where stock never lets
        // an entity act, so the pinning is queued for the step's end and the
        // engine hears nothing until the queue runs.
        var rig = new ProjectileRig();
        var afterStep = new PostSimulationQueue();
        var sticky = rig.Sticky(afterStep);
        sticky.OnSpawn();

        var ev = WorldCollision(sticky.Handle.Value, mySide: 0);
        sticky.OnVPhysicsCollision(in ev, 0);
        Assert.Equal(0, rig.Engine.CountOf("EntityPhysics.EnableMotion")); // still inside the step

        afterStep.Run();

        var motion = rig.Engine.Last("EntityPhysics.EnableMotion");
        Assert.NotNull(motion);
        Assert.False(motion!.ArgAs<bool>(1));
    }

    [Fact]
    public void AStaticSideWithNoNameIsTheWorldAndSticks()
    {
        // The world contact as the live floor delivers it: no entity on the
        // other side and the static bit set — the world's own object's record,
        // which stock's PhysCreateWorld made the world's.
        var rig = new ProjectileRig();
        var sticky = rig.Sticky();
        sticky.OnSpawn();

        var ev = WorldCollision(sticky.Handle.Value, mySide: 0);
        sticky.OnVPhysicsCollision(in ev, 0);
        rig.Queue.Run(); // the pin waits for the step's end

        var motion = rig.Engine.Last("EntityPhysics.EnableMotion");
        Assert.NotNull(motion);
    }

    [Fact]
    public void ACollisionWithABrushThatIsNeitherWorldNorPropDoesNotStick()
    {
        // The window's bounce, live: a func_brush is a named entity that is
        // neither the world nor a dynamic prop, so the contact only bounces
        // (:928's gate) and the bomb flies on.
        var rig = new ProjectileRig();
        var sticky = rig.Sticky();
        sticky.OnSpawn();
        rig.Engine.Returns("Entities.GetIndex", 7);
        rig.Engine.Returns("Entities.GetClassName", "func_brush");

        var ev = new PhysicsEvent
        {
            KindRaw = (uint)PhysicsEventKind.PostCollision,
            Flags = PhysicsEvent.FlagCollision,
            Entity0 = sticky.Handle.Value,
            Entity1 = 0x0002_0000u | 7,
        };
        sticky.OnVPhysicsCollision(in ev, 0);

        Assert.Equal(0, rig.Engine.CountOf("EntityPhysics.EnableMotion"));
    }

    [Fact]
    public void ASidelessMovingOtherIsNoContactToStick()
    {
        // Stock's !pHitEntity (:854-857): a sideless, moving object has no
        // owner to name (PhysicsCollisionImpacts), and the record is no world.
        var rig = new ProjectileRig();
        var sticky = rig.Sticky();
        sticky.OnSpawn();

        var ev = new PhysicsEvent
        {
            KindRaw = (uint)PhysicsEventKind.PostCollision,
            Flags = PhysicsEvent.FlagCollision,
            Entity0 = sticky.Handle.Value,
            Entity1 = PhysicsEvent.NoEntity,
        };
        sticky.OnVPhysicsCollision(in ev, 0);

        Assert.Equal(0, rig.Engine.CountOf("EntityPhysics.EnableMotion"));
    }
}

public class TFStickyTrayFacts
{
    private static TFStickybomb Sticky(ProjectileRig rig)
    {
        var sticky = rig.Sticky();
        sticky.OnThink(); // arms it, the pre-arm think's first tick
        return sticky;
    }

    /// <summary>
    /// The blast's hits on the rig's victim. The thrower stands in the rig's
    /// one place as well, so stock's own pass (<c>RadiusDamage</c>'s second
    /// sweep, tf_gamerules.cpp:5661-5714) hits him beside them.
    /// </summary>
    private static EngineCall[] HitsOnVictim(ProjectileRig rig)
        => rig.Engine.AllOf("Damage.TakeDamageAnswered")
              .Where(c => c.ArgAs<EntityHandle>(0) == ProjectileRig.Victim).ToArray();

    [Fact]
    public void TheNinthStickyDetonatesTheOldestEvenUnarmed()
    {
        // Stock's ninth arrival sets the oldest's timer to "explode NOW"
        // (tf_weapon_pipebomblauncher.cpp:388) — no arming gate, so it goes
        // off however young it is. The stickies here are never armed and
        // never touched, so the blast goes at the air-burst scale
        // (GetDamageRadius, tf_weapon_grenade_pipebomb.cpp:1417-1434); the
        // blast's 100 full damage names the oldest (a 90 would land 68.24).
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));

        var oldest = rig.Sticky();
        oldest.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 100f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        var tray = rig.Tray;
        tray.Register(3, oldest);
        for (int i = 0; i < 7; i++)
        {
            var sticky = rig.Sticky();
            sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                            TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
            tray.Register(3, sticky);
        }
        Assert.Equal(8, tray.Count(3));

        tray.Register(3, rig.Sticky());

        Assert.Equal(8, tray.Count(3));
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
        // At the never-touched air-burst scale the radius is 146 * 0.85 =
        // 124.1, so 100 - 50 * 60/124.1 (a 90 sticky would land 68.24).
        Assert.Equal(75.83f, Assert.Single(HitsOnVictim(rig)).ArgAs<float>(2), 2f);
    }

    [Fact]
    public void AYoungStickyStaysInTheTrayAndIsNotMarked()
    {
        // DetonateRemotePipebombs (tf_weapon_pipebomblauncher.cpp:549-559):
        // a sticky fired within its live time — not yet armed — is skipped:
        // it stays in m_Pipebombs and is marked SetDetonateOnPulse only
        // when GetLiveTime() <= 0.5, which the default 0.8 never is. The
        // next pulse just arms it; a later detonation takes it.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        var sticky = rig.Sticky();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        rig.Tray.Register(3, sticky);

        rig.Tray.DetonateAll(3);

        Assert.Equal(1, rig.Tray.Count(3));
        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamageAnswered"));

        sticky.OnThink(); // the arm think: arms, detonates nothing

        Assert.True(sticky.Armed);
        Assert.Equal(0, rig.Engine.CountOf("Entities.Remove"));

        rig.Tray.DetonateAll(3); // the later detonation

        Assert.Equal(0, rig.Tray.Count(3));
        Assert.Single(HitsOnVictim(rig));
    }

    [Fact]
    public void DetonateClearsTheOwnersTray()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere();

        rig.Tray.Register(3, Sticky(rig));
        rig.Tray.Register(3, Sticky(rig));
        rig.Tray.Register(4, Sticky(rig));

        rig.Tray.DetonateAll(3);

        Assert.Equal(0, rig.Tray.Count(3));
        Assert.Equal(1, rig.Tray.Count(4));
        // Both of player 3's stickies left the world; player 4's stays.
        Assert.Equal(2, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void ARemovedStickyLeavesTheTrayAndNeverGoes()
    {
        // CTFGrenadePipebombProjectile::UpdateOnRemove
        // (tf_weapon_grenade_pipebomb.cpp:175-186): a removed bomb tells its
        // launcher, and the launcher's DeathNotice FindAndRemoves it
        // (tf_weapon_pipebomblauncher.cpp:508-517) — a sticky the engine has
        // taken never sits in the list to go off later. Today the tray is
        // pruned only by the ninth arrival and DetonateAll.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));

        var gone = rig.Sticky();
        var kept = rig.Sticky();
        foreach (var sticky in new[] { gone, kept })
            sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                            TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        gone.OnThink(); // arm both, as a second-old sticky is by the time of a
        kept.OnThink(); // detonation — arming is not what the tray prunes on
        rig.Tray.Register(3, gone);
        rig.Tray.Register(3, kept);

        gone.OnRemoved(); // the removal notice the engine brings for a taken entity

        Assert.Equal(1, rig.Tray.Count(3));

        rig.Tray.DetonateAll(3);

        Assert.Equal(0, rig.Tray.Count(3));
        // The taken sticky did not blast retroactively: one blast, one removal.
        Assert.Single(HitsOnVictim(rig));
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void ADyingOwnerFizzlesTheirStickiesWithoutABlast()
    {
        // CTFPipebombLauncher::UpdateOnRemove (tf_weapon_pipebomblauncher.cpp:479-486):
        // the removed launcher detonates its lot with bFizzle — every entry,
        // young included (the young-sticky skip is the detonate arm's only,
        // :549-551), each Fizzle'd (tf_weapon_grenade_pipebomb.cpp:691-694)
        // and Detonate'd through the m_bFizzle branch — sparks and Destroy,
        // never BaseClass::Detonate (:606-617). Stock reads it off the
        // launcher entity, which the death's weapon removal takes (a class
        // change kills first); this port has no launcher entity, so the tray
        // hears the death itself.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));

        // Blast-capable stickies: armed, launched at the stock damage, so
        // "no blast ask" is the fizzle talking, not an unarmed bomb.
        var armed = rig.Sticky();
        armed.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                       TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        armed.OnThink();
        var young = rig.Sticky();
        young.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                       TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        var other = rig.Sticky();
        other.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                       TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        other.OnThink();

        rig.Tray.Register(3, armed);
        rig.Tray.Register(3, young);  // never armed, still fizzled
        rig.Tray.Register(4, other);  // another owner, untouched

        rig.Tray.OnPlayerDied(new PlayerDeath(ProjectileRig.Player, 3,
                                               EntityHandle.None, default, EntityHandle.None));

        Assert.Equal(0, rig.Tray.Count(3));
        Assert.Equal(1, rig.Tray.Count(4));
        // The fizzle never explodes: no blast ask, just the two removals.
        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
        Assert.Equal(2, rig.Engine.CountOf("Entities.Remove"));
    }

    // ------- the carrier's pipebomb count (HudDemomanPipes's m_iPipebombCount) -------

    /// <summary>
    /// A real player_data carrier under index 3, attached as the composite
    /// does: the attach's make answers a fixed handle the rig's directory
    /// holds a <see cref="TFPlayerDataEntity"/> under.
    /// </summary>
    private static TFStickyTray ACarrieredTray(ProjectileRig rig)
    {
        var carrierHandle = new EntityHandle(0x0004_0020u);
        rig.Engine.Returns("Entities.Create", carrierHandle)
                  .Returns("Entities.GetIndex", 3);
        var data = new TFPlayerDataEntity(rig.Engine);
        data.State.BindLocal();     // the registry's bind, as Build's rig does
        rig.Directory.Holds(carrierHandle, data);
        new TFPlayerCarrier(rig.Engine, rig.Directory, rig.Carriers, rig.Log)
            .Attach(ProjectileRig.Player);
        return new TFStickyTray(rig.Carriers);
    }

    [Fact]
    public void TheTrayPublishesItsCountToTheOwnersCarrier()
    {
        // CTFPipebombLauncher::IncrementPipebombCount's netword
        // m_iPipebombCount (tf_weapon_pipebomblauncher.cpp:46) is what
        // GetNumActivePipebombs hands the HUD (tf_player_shared.cpp:13065);
        // this port's count rides the carrier, written by every tray change.
        var rig = new ProjectileRig();
        TFStickyTray tray = ACarrieredTray(rig);
        Assert.Equal(0, rig.Carriers.Get(3)?.PipebombCount);

        var sticky = rig.Sticky();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        tray.Register(3, sticky);
        Assert.Equal(1, rig.Carriers.Get(3)?.PipebombCount);

        tray.Unregister(3, sticky);
        Assert.Equal(0, rig.Carriers.Get(3)?.PipebombCount);
    }

    [Fact]
    public void TheFizzleEmptiesTheCarriersCountToo()
    {
        var rig = new ProjectileRig();
        TFStickyTray tray = ACarrieredTray(rig);

        for (int i = 0; i < 2; i++)
        {
            var sticky = rig.Sticky();
            sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                            TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
            tray.Register(3, sticky);
        }
        Assert.Equal(2, rig.Carriers.Get(3)?.PipebombCount);

        tray.FizzleAll(3);
        Assert.Equal(0, rig.Carriers.Get(3)?.PipebombCount);
    }
}

/// <summary>
/// The winbomb guard on a sticky's air burst:
/// <c>CTFGrenadePipebombProjectile::GetDamageRadius</c>
/// (<c>tf_weapon_grenade_pipebomb.cpp:1417-1434</c>) scales a
/// remote-detonate bomb that has never touched to
/// <c>tf_sticky_airdet_radius</c>, ramping to full across
/// <c>tf_sticky_radius_ramp_time</c> after the arm time. The blast's sphere
/// query carries the radius, so the query is the readout.
/// </summary>
public class TFStickyDetonateFacts
{
    /// <summary>A thrown, not-yet-touched sticky, the blast arm minus the detonation.</summary>
    private static TFStickybomb Airborne(ProjectileRig rig)
    {
        var sticky = rig.Sticky();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        return sticky;
    }

    /// <summary>The radius of the blast the sticky just threw (the sweep's sphere query).</summary>
    private static float BlastRadius(ProjectileRig rig)
    {
        var sweep = rig.Engine.Last("WorldQuery.EntitiesInSphere");
        Assert.NotNull(sweep);
        return sweep!.ArgAs<float>(1);
    }

    [Fact]
    public void AStickyDetonatedYoungInTheAirBlastsAtTheAirRadiusScale()
    {
        // m_bTouched == false and an age under the arm time (0.8): the remap
        // sits at its low end, tf_sticky_airdet_radius (tf_gamerules.cpp:860),
        // so 146 * 0.85 = 124.1 of radius goes into the blast.
        var rig = new ProjectileRig();
        var sticky = Airborne(rig);

        sticky.Detonate(); // the clock stands at the 10 its throw was stamped at

        Assert.Equal(124.1f, BlastRadius(rig), 2);
    }

    [Fact]
    public void AStickyAirDetonationRampsLinearlyWithItsAge()
    {
        // Age 1.8: two seconds past the arm time, half of the 2 s
        // tf_sticky_radius_ramp_time — the scale is halfway from 0.85 to 1,
        // and 146 * 0.925 is the radius.
        var rig = new ProjectileRig();
        var sticky = Airborne(rig);
        rig.Engine.Returns("Clock.Now", 11.8f);

        sticky.Detonate();

        Assert.Equal(135.05f, BlastRadius(rig), 2);
    }

    [Fact]
    public void AStickyAirDetonationReachesItsFullRadiusAtTheEndOfTheRamp()
    {
        var rig = new ProjectileRig();
        var sticky = Airborne(rig);
        rig.Engine.Returns("Clock.Now", 12.8f); // arm 0.8 + ramp 2.0

        sticky.Detonate();

        Assert.Equal(146f, BlastRadius(rig), 2);
    }

    [Fact]
    public void AStuckStickyDetonatesAtItsFullRadiusEvenWhileYoung()
    {
        // The m_bTouched arm (GetDamageRadius :1426): a bomb that has touched
        // the world is no longer an air burst, at any age.
        var rig = new ProjectileRig();
        rig.Engine.Returns("Entities.GetIndex", 0); // the toucher is the world
        var sticky = Airborne(rig);
        sticky.OnTouch(new EntityHandle(0x0001_0000u));

        sticky.Detonate();

        Assert.Equal(146f, BlastRadius(rig), 2);
    }

    [Fact]
    public void TheAirRadiusReadsItsConvars()
    {
        // Both names read live: ramp_time 4.0 and airdet 0.5 put an age-2.8
        // bomb at the half of a half — scale 0.75, radius 146 * 0.75.
        var rig = new ProjectileRig();
        rig.StickyVars.RadiusRampTime.Hold("4.0");
        rig.StickyVars.AirDetRadius.Hold("0.5");
        var sticky = Airborne(rig);
        rig.Engine.Returns("Clock.Now", 12.8f);

        sticky.Detonate();

        Assert.Equal(109.5f, BlastRadius(rig), 2);
    }

    [Fact]
    public void TheAirRadiusRampStartsAtTheLiveTime()
    {
        // The ramp's start is the live time, as stock's flArmTime is (:1428):
        // live 1.8 at the same age-2.8 clock holds the bomb at the ramp's
        // half — 0.925, radius 135.05 — where the declared 0.8 would end it.
        var rig = new ProjectileRig();
        rig.StickyVars.LiveTime.Hold("1.8");
        var sticky = Airborne(rig);
        rig.Engine.Returns("Clock.Now", 12.8f);

        sticky.Detonate();

        Assert.Equal(135.05f, BlastRadius(rig), 2);
    }
}

/// <summary>
/// The pulse's server half: the flag stock's client would time from a
/// creation time the engine does not network is written at the arm think —
/// <c>StickyArmTime</c>, stock's default live time — where
/// <c>Simulate</c>'s <c>m_bPulsed</c> gate sits
/// (<c>tf_weapon_grenade_pipebomb.cpp:345-373</c>).
/// </summary>
public class TFStickyPulseFacts
{
    [Fact]
    public void TheArmThinkIsBookedAtTheLiveTime()
    {
        // PreArmThink books the arm tf_grenadelauncher_livetime out
        // (tf_weapon_grenade_pipebomb.cpp:1144), not the constant.
        var rig = new ProjectileRig();
        rig.StickyVars.LiveTime.Hold("1.5");
        var sticky = rig.Sticky();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        var thinks = new ThinkScheduler();
        sticky.BindThinks(thinks);

        sticky.OnSpawn();

        Assert.Equal(1.5f, Assert.Single(thinks.Bookings()).Time, 4);
    }

    [Fact]
    public void TheArmThinkWritesThePulseToTheWire()
    {
        var rig = new ProjectileRig();
        var sticky = rig.Sticky();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);

        Assert.False(sticky.State.Pulsed);

        sticky.OnThink(); // the arm think, 0.8 s after the throw

        Assert.True(sticky.State.Pulsed);
        Assert.False(sticky.Fizzled); // the pulse is not the detonation
    }

    [Fact]
    public void ThePulseWritesEvenWhenTheBombIsSetToDetonateOnIt()
    {
        // Stock's Simulate creates the pulse and THEN honours
        // m_bDetonateOnPulse (:365-370) — the wire flag lands either way.
        var rig = new ProjectileRig();
        var sticky = rig.Sticky();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        sticky.ArmDetonateOnPulse();
        rig.Engine.FillsSphere();

        sticky.OnThink();

        Assert.True(sticky.State.Pulsed);
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }
}

/// <summary>
/// The sticky-destroying launcher's sweep:
/// <c>CTFGrenadePipebombProjectile::DetonateStickies</c>
/// (<c>tf_weapon_grenade_pipebomb.cpp:625-674</c>) behind
/// <c>CTFPipebombLauncher::CanDestroyStickies</c>'s
/// <c>stickies_detonate_stickies</c> (<c>tf_weapon_pipebomblauncher.h:80</c>,
/// run per bomb at :561-565).
/// </summary>
public class TFStickyDestroyFacts
{
    private static readonly EntityHandle EnemyBomb = new(0x0001_0000u | 7);

    private static TFWeaponScript Script(string name)
    {
        var files = WeaponFixtures.ShippedWithClasses();
        return new TFWeaponScripts(new RecordingLog(), files).Get(name)
            ?? throw new InvalidOperationException($"no script {name}");
    }

    private static WeaponAttackContext Attack(int owner) => new(default!, null!, null!, 1f, true,
        null!, new WeaponCommand(1, 5, owner));

    /// <summary>The detonator: thrown, armed, its own radius the sweep's.</summary>
    private static TFStickybomb Detonator(ProjectileRig rig)
    {
        var sticky = rig.Sticky();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        sticky.OnThink();
        rig.Tray.Register(3, sticky);
        return sticky;
    }

    /// <summary>The enemy bomb: thrown, armed, on the other team, in the directory.</summary>
    private static TFStickybomb Enemy(ProjectileRig rig)
    {
        var sticky = rig.Sticky();
        sticky.ThrownBy(ProjectileRig.Victim, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        sticky.OnThink();
        rig.Tray.Register(5, sticky);
        rig.Directory.Holds(EnemyBomb, sticky);
        return sticky;
    }

    /// <summary>The sweep's world: the enemy bomb in the sphere, both at 60, teams split.</summary>
    private static void Sees(ProjectileRig rig)
    {
        rig.Engine.FillsSphere(EnemyBomb);
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            // As the player seam answers: a bomb is no player and has no team.
            rig.Engine.Calls[^1].Args[0] is EntityHandle h
                ? h == ProjectileRig.Victim ? 3 : h == ProjectileRig.Player ? 2 : -1
                : -1);
    }

    [Fact]
    public void TheSweepLeavesItsOwnTeamsBombsToBlast()
    {
        // The Quickiebomb and the Scottish Resistance: the sweep found the bomb
        // that was sweeping, took it for an enemy's, and it fizzled without a blast.
        var rig = new ProjectileRig();
        Sees(rig);
        var own = Detonator(rig);
        var mine = new EntityHandle(0x0003_0000u | 11);
        rig.Directory.Holds(mine, own);
        rig.Engine.FillsSphere(mine);

        own.DetonateStickies();

        Assert.False(own.Fizzled);
    }

    [Fact]
    public void TheDestroyingLauncherTakesAnEnemyBombWithinSight()
    {
        // DetonateStickies (:625-674): the sweeping bomb's damage radius is
        // the sphere, the enemy bomb fizzles and detonates with its own
        // blast, and both leave the world. The detonation ask here is the
        // detonator's own blast — the enemy's sphere carries only itself,
        // which its ignore skips (:655-660, :824).
        var rig = new ProjectileRig();
        Sees(rig);
        var enemy = Enemy(rig);
        Detonator(rig);
        rig.Owners.Item = rig.AttributeItem("stickies_detonate_stickies",
                                            AttributeDescriptionFormat.Additive, 1f);

        rig.Effects.Detonate(Attack(3), Script("tf_weapon_pipebomblauncher"));

        Assert.True(enemy.Fizzled);
        Assert.Equal(0, rig.Tray.Count(3));
        // The engine's removal notice is what prunes the enemy's tray
        // (UpdateOnRemove -> DeathNotice); it arrives after the Detonate.
        enemy.OnRemoved();
        Assert.Equal(0, rig.Tray.Count(5));
        Assert.Equal(2, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void APlainLauncherSparesTheEnemyBomb()
    {
        // :7449's counterpart for stickies — no stickies_detonate_stickies,
        // no sweep at all: the enemy keeps its entry and its m_bFizzle.
        var rig = new ProjectileRig();
        Sees(rig);
        var enemy = Enemy(rig);
        Detonator(rig);

        rig.Effects.Detonate(Attack(3), Script("tf_weapon_pipebomblauncher"));

        Assert.False(enemy.Fizzled);
        Assert.Equal(0, rig.Tray.Count(3));
        Assert.Equal(1, rig.Tray.Count(5));
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void AnEnemyBombOutOfSightSurvives()
    {
        // :655-657 — the brush-only trace between the two bombs decides; a
        // blocked line skips the bomb unfizzed.
        var rig = new ProjectileRig();
        Sees(rig);
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 0.5f, new Vector(30f, 0f, 0f), default, EntityHandle.None));
        var enemy = Enemy(rig);
        Detonator(rig);
        rig.Owners.Item = rig.AttributeItem("stickies_detonate_stickies",
                                            AttributeDescriptionFormat.Additive, 1f);

        rig.Effects.Detonate(Attack(3), Script("tf_weapon_pipebomblauncher"));

        Assert.False(enemy.Fizzled);
        Assert.Equal(1, rig.Tray.Count(5));
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void AFizzledEnemyBombIsNotTaken()
    {
        // :652-653 — a bomb that already fizzled (its owner's death, say)
        // stays fizzled and is skipped: no second Detonate, no un-fizzle.
        var rig = new ProjectileRig();
        Sees(rig);
        var enemy = Enemy(rig);
        enemy.Fizzle();
        Detonator(rig);
        rig.Owners.Item = rig.AttributeItem("stickies_detonate_stickies",
                                            AttributeDescriptionFormat.Additive, 1f);

        rig.Effects.Detonate(Attack(3), Script("tf_weapon_pipebomblauncher"));

        Assert.Equal(1, rig.Tray.Count(5));
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void ASameTeamBombIsNotTaken()
    {
        // :642-643 — the team ask is on the bomb itself, against the
        // sweeping thrower's; a teammate's bomb is skipped before the cast.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(EnemyBomb);
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        var enemy = Enemy(rig);
        Detonator(rig);
        rig.Owners.Item = rig.AttributeItem("stickies_detonate_stickies",
                                            AttributeDescriptionFormat.Additive, 1f);

        rig.Effects.Detonate(Attack(3), Script("tf_weapon_pipebomblauncher"));

        Assert.Equal(1, rig.Tray.Count(5));
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }
}

public class TFProjectileEffectsFacts
{
    [Fact]
    public void TheScriptSpeedWinsOverStocksWhenItNamesOne()
    {
        Assert.Equal(1100f, TFProjectileDefs.Speed(0f, TFProjectileDefs.RocketSpeed));
        Assert.Equal(1200f, TFProjectileDefs.Speed(0f, TFProjectileDefs.PipeSpeed));
        Assert.Equal(800f, TFProjectileDefs.Speed(800f, TFProjectileDefs.RocketSpeed));
    }

    private static TFWeaponScript Script(string name)
    {
        var files = WeaponFixtures.ShippedWithClasses();
        return new TFWeaponScripts(new RecordingLog(), files).Get(name)
            ?? throw new InvalidOperationException($"no script {name}");
    }

    private static WeaponAttackContext Attack(int owner) => new(default!, null!, null!, 1f, true,
        null!, new WeaponCommand(1, 5, owner));

    [Fact]
    public void TheRocketLauncherCreatesARocket()
    {
        var rig = new ProjectileRig();
        rig.Directory.Instance = null; // the mapping is what the fact holds

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_rocketlauncher"), default, 0f);

        var create = rig.Engine.Last("Entities.Create");
        Assert.NotNull(create);
        Assert.Equal(TFProjectileEntityNames.Rocket, create!.ArgAs<string>(0));

        // Born at the rocket launcher's own muzzle (FireRocket's vecOffset
        // from the eye) and brought to life before the arming asks for its
        // managed half.
        var placed = rig.Engine.Last("Entities.SetOrigin");
        Assert.NotNull(placed);
        var muzzle = placed!.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.RocketMuzzleForward, muzzle.X, 0.5f);
        Assert.Equal(-TFProjectileDefs.RocketMuzzleRight, muzzle.Y, 0.5f); // right is -Y at yaw 0
        Assert.Equal(64f + TFProjectileDefs.RocketMuzzleUp, muzzle.Z, 0.5f);
        var spawned = rig.Engine.Last("Entities.Spawn");
        Assert.NotNull(spawned);
        Assert.Equal(ProjectileRig.ProjectileHandle, spawned!.ArgAs<EntityHandle>(0));
    }

    /// <summary>
    /// A rocket carries the shot the hooks answered, not the script's number
    /// (<c>SetDamage( GetProjectileDamage() )</c>, tf_weaponbase_gun.cpp:505-793, whose
    /// first arm is <c>mult_dmg</c>, :909-910): every projectile gun with a damage
    /// attribute fired at script damage until this was seen on a real enemy.
    /// </summary>
    [Fact]
    public void A_rocket_carries_the_shot_the_hooks_answered()
    {
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rig.Directory.Instance = rocket;

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_rocketlauncher"), new ShotDamage(112.5f), 0f);

        Assert.Equal(112.5f, rocket.Damage, 0.01f);
    }

    /// <summary>
    /// A rocket sweeps for the hull, not the hitboxes (tf_weaponbase_rocket.cpp:354-369). With
    /// the shot mask it flew through a spy standing square in the crosshair: two clients, 0
    /// damage from a launcher that did 105 to a heavy on the same spot.
    /// </summary>
    [Fact]
    public void A_rockets_sweep_finds_a_players_hull()
    {
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rocket.Handle = ProjectileRig.ProjectileHandle;
        rig.Directory.Instance = rocket;
        rig.Engine.Returns("Entities.GetParent", EntityHandle.None)
                  .Returns("Entities.GetOrigin", Vector.Zero)
                  .Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None))
                  .Ignores("Entities.SetOrigin");

        rig.MotionDirectory.Bind(ProjectileRig.ProjectileHandle, rocket);
        rocket.FiredBy(ProjectileRig.Player, new Vector(1100f, 0f, 0f));
        int moved = rig.Motion.RunFrame(0.015f);

        Assert.Equal(1, moved);
        Assert.Equal(TraceMask.Solid, rig.Engine.Last("WorldQuery.TraceLine")!.ArgAs<TraceMask>(2));
    }

    /// <summary>
    /// Stock's teammate-pass window (<c>CTFBaseRocket::PhysicsSolidMaskForEntity</c>,
    /// tf_weaponbase_rocket.cpp:354-369; <c>ResetCollideWithTeammates</c>,
    /// baseprojectile.cpp:212-230): while the rocket is younger than
    /// <see cref="TFProjectileDefs.CollideWithTeammatesDelay"/> its sweep carries only the
    /// other team's contents bit, so a teammate standing in front does not stop it — the
    /// rocket-jump over the heavy who walked in front of the soldier. At 0.2 s the shot
    /// flies through the teammate and is spent on what stands behind him, not on his body.
    /// </summary>
    [Fact]
    public void A_rocket_passes_its_own_teammate_for_the_first_quarter_second()
    {
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rocket.Handle = ProjectileRig.ProjectileHandle;
        rig.Directory.Instance = rocket;
        var teammate = new EntityHandle(0x0001_0000u | 4);

        rig.Engine.Returns("Entities.GetParent", EntityHandle.None)
                  .Returns("Entities.GetOrigin", Vector.Zero)
                  .Returns("Entities.IsPlayer", true)
                  .Returns("Players.GetTeam", 2)
                  .ReturnsEachCall("WorldQuery.TraceLine", () =>
                      rig.Engine.Calls.Count(c => c.Member == "WorldQuery.TraceLine") <= 2
                          ? new TraceResult(true, 0.5f, new Vector(0f, 7.5f, 0f), new Vector(0f, -1f, 0f), teammate)
                          : new TraceResult(true, 0.8f, new Vector(0f, 12f, 0f), new Vector(0f, 0f, 1f), EntityHandle.None))
                  .Ignores("Entities.SetOrigin", "Entities.SetVelocity");

        rig.MotionDirectory.Bind(ProjectileRig.ProjectileHandle, rocket);
        rocket.FiredBy(ProjectileRig.Player, new Vector(0f, 1000f, 0f));
        rig.Motion.RunFrame(0.015f);   // stops on the teammate; the pass is armed
        rig.Motion.RunFrame(0.015f);   // flown through him — the brush behind stops the shot

        Assert.Equal(EntityHandle.None, rocket.DirectVictim);

        // The floor beyond the teammate, not the step's destination: the world kept its say.
        var origin = rig.Engine.Last("Entities.SetOrigin")!.ArgAs<Vector>(1);
        Assert.Equal(12f, origin.Y, 0.01f);

        // The first sweep carries the full solid mask; the re-trace that replaced
        // it is brush-only, and the hull's sweep is recorded beside the line's.
        var traces = rig.Engine.AllOf("WorldQuery.TraceLine")
                          .Concat(rig.Engine.AllOf("WorldQuery.TraceBox")).ToArray();
        Assert.Equal(TraceMask.Solid, traces[0].ArgAs<TraceMask>(2));
        Assert.Contains(traces, t => t.ArgAs<TraceMask>(2) == TraceMask.Brushes);
    }

    /// <summary>
    /// After the delay the mask carries both teams' bits
    /// (<c>tf_weaponbase_rocket.cpp:363-366</c>, answered by <c>CTFPlayer::ShouldCollide</c>,
    /// tf_player.cpp:10159-10178): at 0.3 s a teammate stops the rocket and it is spent on him.
    /// </summary>
    [Fact]
    public void A_rocket_hits_a_teammate_once_the_delay_has_passed()
    {
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rocket.Handle = ProjectileRig.ProjectileHandle;
        rig.Directory.Instance = rocket;
        var teammate = new EntityHandle(0x0001_0000u | 4);

        rig.Engine.Returns("Entities.GetParent", EntityHandle.None)
                  .Returns("Entities.GetOrigin", Vector.Zero)
                  .Returns("Players.GetTeam", 2)
                  .ReturnsEachCall("WorldQuery.TraceLine", () =>
                      rig.Engine.Calls.Count(c => c.Member == "WorldQuery.TraceLine") <= 2
                          ? new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None)
                          : new TraceResult(true, 0.5f, new Vector(0f, 7.5f, 0f), new Vector(0f, -1f, 0f), teammate))
                  .Ignores("Entities.SetOrigin", "Entities.SetVelocity");

        rig.MotionDirectory.Bind(ProjectileRig.ProjectileHandle, rocket);
        rocket.FiredBy(ProjectileRig.Player, new Vector(0f, 1000f, 0f));
        rig.Motion.RunFrame(0.1f);     // two fly-over frames put the age past the delay
        rig.Motion.RunFrame(0.1f);
        rig.Motion.RunFrame(0.1f);     // third frame: the teammate stops it

        Assert.Equal(teammate, rocket.DirectVictim);

        var origin = rig.Engine.Last("Entities.SetOrigin")!.ArgAs<Vector>(1);
        Assert.Equal(7.5f, origin.Y, 0.01f);

        // Past the delay the sweep itself stops the shot: the stop trace carries the
        // full solid mask and no brush-only re-trace replaces it.
        var traces = rig.Engine.AllOf("WorldQuery.TraceLine").ToArray();
        Assert.Equal(TraceMask.Solid, traces[0].ArgAs<TraceMask>(2));
    }

    /// <summary>
    /// The other team's bit is in the mask from the first frame
    /// (<c>tf_weaponbase_rocket.cpp:358-361</c>): at 0.1 s an enemy stops the rocket —
    /// the window passes teammates, not everybody.
    /// </summary>
    [Fact]
    public void A_rocket_hits_an_enemy_inside_the_pass_delay()
    {
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rocket.Handle = ProjectileRig.ProjectileHandle;
        rig.Directory.Instance = rocket;
        var enemy = ProjectileRig.Victim;
        var answers = new Queue<int>(new[] { 2, 3 });

        rig.Engine.Returns("Entities.GetParent", EntityHandle.None)
                  .Returns("Entities.GetOrigin", Vector.Zero)
                  .Returns("Entities.IsPlayer", true)
                  .ReturnsEachCall("Players.GetTeam", () => answers.Dequeue())
                  .Returns("WorldQuery.TraceLine",
                      new TraceResult(true, 0.5f, new Vector(0f, 7.5f, 0f), new Vector(0f, -1f, 0f), enemy))
                  .Ignores("Entities.SetOrigin", "Entities.SetVelocity");

        rig.MotionDirectory.Bind(ProjectileRig.ProjectileHandle, rocket);
        rocket.FiredBy(ProjectileRig.Player, new Vector(0f, 1000f, 0f));
        rig.Motion.RunFrame(0.015f);   // age 0.015 — inside the window

        Assert.Equal(enemy, rocket.DirectVictim);

        // The enemy stops the shot inside the window: the sweep itself hits him,
        // on the full solid mask — the window passes teammates, not everybody.
        var traces = rig.Engine.AllOf("WorldQuery.TraceLine").ToArray();
        Assert.Equal(TraceMask.Solid, traces[0].ArgAs<TraceMask>(2));
    }

    /// <summary>
    /// The pass is a mask change, not a cleared step
    /// (<c>PhysicsCheckSweep</c>'s shape): when the teammate stands on the floor the
    /// rocket is to reach, flying through him must still stop on that floor — not carry
    /// on to the step's destination past it. Seventeen rockets were lost under the
    /// cleared shape on ctf_2fort, 4v4 bots.
    /// </summary>
    [Fact]
    public void A_passed_teammate_on_the_floor_still_stops_the_rocket_below_it()
    {
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rocket.Handle = ProjectileRig.ProjectileHandle;
        rig.Directory.Instance = rocket;
        var teammate = new EntityHandle(0x0001_0000u | 4);

        rig.Engine.Returns("Entities.GetParent", EntityHandle.None)
                  .Returns("Entities.GetOrigin", Vector.Zero)
                  .Returns("Entities.IsPlayer", true)
                  .Returns("Players.GetTeam", 2)
                  .ReturnsEachCall("WorldQuery.TraceLine", () =>
                      rig.Engine.Calls.Count(c => c.Member == "WorldQuery.TraceLine") <= 2
                          ? new TraceResult(true, 0.5f, new Vector(0f, 7.5f, 0f), new Vector(0f, -1f, 0f), teammate)
                          : new TraceResult(true, 0.8f, new Vector(0f, 12f, 0f), new Vector(0f, 0f, 1f), EntityHandle.None))
                  .Ignores("Entities.SetOrigin", "Entities.SetVelocity");

        rig.MotionDirectory.Bind(ProjectileRig.ProjectileHandle, rocket);
        rocket.FiredBy(ProjectileRig.Player, new Vector(0f, 1000f, 0f));
        rig.Motion.RunFrame(0.015f);
        rig.Motion.RunFrame(0.015f);

        // The teammate standing on the floor did not let the shot through it: the
        // re-trace beside him is brush-only, and the floor answered it at twelve.
        var origin = rig.Engine.Last("Entities.SetOrigin")!.ArgAs<Vector>(1);
        Assert.Equal(12f, origin.Y, 0.01f);

        var traces = rig.Engine.AllOf("WorldQuery.TraceLine")
                          .Concat(rig.Engine.AllOf("WorldQuery.TraceBox")).ToArray();
        Assert.Contains(traces, t => t.ArgAs<TraceMask>(2) == TraceMask.Brushes);
    }

    /// <summary>
    /// The window leaves out the TEAMMATE, not every player (stock's mask keeps the
    /// other team's bit, <c>tf_weaponbase_rocket.cpp:358-361</c>): a rocket that has
    /// flown past a teammate still stops on the enemy behind him. Review of F242: a
    /// pass that stayed on for the rest of the flight flew through that enemy too.
    /// </summary>
    [Fact]
    public void A_rocket_that_flew_past_a_teammate_still_hits_the_enemy_behind_him()
    {
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rocket.Handle = ProjectileRig.ProjectileHandle;
        rig.Directory.Instance = rocket;
        var teammate = new EntityHandle(0x0001_0000u | 4);
        var enemy = ProjectileRig.Victim;

        rig.Engine.Returns("Entities.GetParent", EntityHandle.None)
                  .Returns("Entities.GetOrigin", Vector.Zero)
                  .Returns("Entities.IsPlayer", true)
                  .ReturnsEachCall("Players.GetTeam", () => rig.Engine.Calls[^1].ArgAs<EntityHandle>(0) == enemy ? 3 : 2)
                  .ReturnsEachCall("WorldQuery.TraceLine", () =>
                      rig.Engine.Calls.Count(c => c.Member == "WorldQuery.TraceLine") switch
                      {
                          1 or 2 => new TraceResult(true, 0.5f, new Vector(0f, 7.5f, 0f), new Vector(0f, -1f, 0f), teammate),
                          4 => new TraceResult(true, 0.5f, new Vector(0f, 30f, 0f), new Vector(0f, -1f, 0f), enemy),
                          _ => new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None),
                      })
                  .Ignores("Entities.SetOrigin", "Entities.SetVelocity");

        rig.MotionDirectory.Bind(ProjectileRig.ProjectileHandle, rocket);
        rocket.FiredBy(ProjectileRig.Player, new Vector(0f, 1000f, 0f));
        rig.Motion.RunFrame(0.015f);   // strikes the teammate: flown past
        rig.Motion.RunFrame(0.015f);   // through him, against the brushes alone
        rig.Motion.RunFrame(0.015f);   // the enemy behind him stops it

        Assert.Equal(enemy, rocket.DirectVictim);
    }

    /// <summary>
    /// The pass belongs to the body struck inside the window, and ends with it: a rocket
    /// that flew past one teammate early is stopped by the next teammate it meets after
    /// <see cref="TFProjectileDefs.CollideWithTeammatesDelay"/>.
    /// </summary>
    [Fact]
    public void A_rocket_that_flew_past_a_teammate_early_is_stopped_by_a_teammate_after_the_delay()
    {
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rocket.Handle = ProjectileRig.ProjectileHandle;
        rig.Directory.Instance = rocket;
        var first = new EntityHandle(0x0001_0000u | 4);
        var second = new EntityHandle(0x0001_0000u | 6);

        rig.Engine.Returns("Entities.GetParent", EntityHandle.None)
                  .Returns("Entities.GetOrigin", Vector.Zero)
                  .Returns("Entities.IsPlayer", true)
                  .Returns("Players.GetTeam", 2)
                  .ReturnsEachCall("WorldQuery.TraceLine", () =>
                      rig.Engine.Calls.Count(c => c.Member == "WorldQuery.TraceLine") switch
                      {
                          1 or 2 => new TraceResult(true, 0.5f, new Vector(0f, 7.5f, 0f), new Vector(0f, -1f, 0f), first),
                          6 => new TraceResult(true, 0.5f, new Vector(0f, 300f, 0f), new Vector(0f, -1f, 0f), second),
                          _ => new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None),
                      })
                  .Ignores("Entities.SetOrigin", "Entities.SetVelocity");

        rig.MotionDirectory.Bind(ProjectileRig.ProjectileHandle, rocket);
        rocket.FiredBy(ProjectileRig.Player, new Vector(0f, 1000f, 0f));
        rig.Motion.RunFrame(0.015f);   // strikes the first teammate inside the window
        rig.Motion.RunFrame(0.015f);   // through him (the re-trace is the third trace)
        rig.Motion.RunFrame(0.1f);     // open air
        rig.Motion.RunFrame(0.1f);     // open air: the age is past the delay
        rig.Motion.RunFrame(0.1f);     // the second teammate stops it

        Assert.Equal(second, rocket.DirectVictim);
    }

    [Fact]
    public void A_pipe_carries_the_shot_the_hooks_answered()
    {
        var rig = new ProjectileRig();
        var pipe = rig.Pipebomb();
        rig.Directory.Instance = pipe;

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_grenadelauncher"), new ShotDamage(72f), 0f);

        Assert.Equal(72f, pipe.FullDamage, 0.01f);
    }

    [Fact]
    public void TheRocketLauncherArmsItsRocketAtTheLaunchSpeed()
    {
        var rig = new ProjectileRig();
        var script = Script("tf_weapon_rocketlauncher");
        var rocket = rig.Rocket();
        rig.Directory.Instance = rocket;

        rig.Effects.Projectile(Attack(3), script, default, 0f);

        var velocity = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(velocity);
        Assert.Equal(TFProjectileDefs.Speed(script.Primary.ProjectileSpeed, TFProjectileDefs.RocketSpeed),
                     velocity!.ArgAs<Vector>(1).X, 0.5f);
    }

    [Fact]
    public void ProjectileSpeedScalesTheLaunch()
    {
        // mult_projectile_speed on the launcher's item rides the launch
        // speed before the specialist's remap (tf_weaponbase_rocket.cpp:274-275):
        // the 0.5 halves the stock 1100 to 550.
        var rig = new ProjectileRig();
        var script = Script("tf_weapon_rocketlauncher");
        rig.Directory.Instance = rig.Rocket();
        rig.Owners.Item = rig.AttributeItem("mult_projectile_speed", AttributeDescriptionFormat.Percentage, 0.5f);

        rig.Effects.Projectile(Attack(3), script, default, 0f);

        var velocity = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(velocity);
        Assert.Equal(550f, velocity!.ArgAs<Vector>(1).X, 0.5f);
    }

    /// <summary>GetProjectileFireSetup's centre-fire arm (tf_weaponbase.cpp:5755-5760):
    /// the launcher's item answering exactly 1 on centerfire_projectile drops the
    /// lateral offset of the shot position, so the rocket is born on the sight line.</summary>
    [Fact]
    public void A_centerfiring_launcher_fires_the_rocket_from_the_centre()
    {
        var rig = new ProjectileRig();
        rig.Directory.Instance = rig.Rocket();
        rig.Owners.Item = rig.AttributeItem("centerfire_projectile", AttributeDescriptionFormat.Additive, 1f);

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_rocketlauncher"), default, 0f);

        var muzzle = rig.Engine.Last("Entities.SetOrigin")!.ArgAs<Vector>(1);
        Assert.Equal(0f, muzzle.Y, 0.5f);   // right is -Y at yaw 0; the offset is gone
    }

    /// <summary>Stock compares the answer with == exactly (tf_weaponbase.cpp:5757):
    /// a two keeps the lateral offset.</summary>
    [Fact]
    public void An_answer_of_two_keeps_the_lateral_muzzle_offset()
    {
        var rig = new ProjectileRig();
        rig.Directory.Instance = rig.Rocket();
        rig.Owners.Item = rig.AttributeItem("centerfire_projectile", AttributeDescriptionFormat.Additive, 2f);

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_rocketlauncher"), default, 0f);

        var muzzle = rig.Engine.Last("Entities.SetOrigin")!.ArgAs<Vector>(1);
        Assert.Equal(-TFProjectileDefs.RocketMuzzleRight, muzzle.Y, 0.5f);
    }

    [Fact]
    public void TheLochNLoadsPipeLeavesAtItsSpedUpLaunch()
    {
        // GetProjectileSpeed (tf_weapon_grenadelauncher.cpp:476-478): the
        // stock 1200 under the launcher's mult_projectile_speed — the
        // Loch-n-Load's 1.25 — before FirePipeBomb's range hook.
        var rig = new ProjectileRig();
        rig.Owners.Item = rig.AttributeItem("mult_projectile_speed",
                                            AttributeDescriptionFormat.Percentage, 1.25f);
        rig.Directory.Instance = rig.Pipebomb();

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_grenadelauncher"), default, 0f);

        var velocity = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(velocity);
        Assert.Equal(1500f, velocity!.ArgAs<Vector>(1).X, 0.5f);
    }

    [Fact]
    public void TheLochNLoadsLauncherStampsThePipeWallShatter()
    {
        // FireProjectileInternal (tf_weapon_grenadelauncher.cpp:272-275): the
        // launcher's set_detonate_mode AIR pokes m_bWallShatter; the plain
        // launcher's pipe carries none.
        var rig = new ProjectileRig();
        var loch = rig.Pipebomb();
        rig.Directory.Instance = loch;
        rig.Owners.Item = rig.AttributeItem("set_detonate_mode", AttributeDescriptionFormat.Additive, 2);

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_grenadelauncher"), default, 0f);

        Assert.True(loch.WallShatter);

        var plainRig = new ProjectileRig();
        var pipe = plainRig.Pipebomb();
        plainRig.Directory.Instance = pipe;

        plainRig.Effects.Projectile(Attack(3), Script("tf_weapon_grenadelauncher"), default, 0f);

        Assert.False(pipe.WallShatter);
    }

    /// <summary>The launcher's script firing the cannonball, as the shipped <c>tf_weapon_cannonball</c> does (<c>projectile_cannonball</c>).</summary>
    private static TFWeaponScript CannonballScript(TFWeaponScript script) => new()
    {
        Base = script.Base,
        Primary = script.Primary with { Projectile = TFProjectile.Cannonball },
    };

    [Fact]
    public void TheLooseCannonsShotIsTheCannonballPipe()
    {
        // Create's translation (tf_weapon_grenade_pipebomb.cpp:437-458): the
        // cannonball projectile type rides the pipe entity with
        // TF_GL_MODE_CANNONBALL stamped, the shell re-dressed in Spawn
        // (:513-516).
        var rig = new ProjectileRig();
        var ball = rig.Pipebomb();
        rig.Directory.Instance = ball;

        rig.Effects.Projectile(Attack(3), CannonballScript(Script("tf_weapon_grenadelauncher")), default, 0f);

        Assert.Equal(TFProjectileDefs.GrenadeModeCannonball, ball.PipebombMode);
        var shell = rig.Engine.Last("EntityRender.SetModel");
        Assert.NotNull(shell);
        Assert.Equal(TFProjectileDefs.CannonballModel, shell!.ArgAs<string>(1));
    }

    [Fact]
    public void TheLooseCannonsChargedShotFusesAtTheWindowLeft()
    {
        // FireProjectileInternal's charged fuse
        // (tf_weapon_grenadelauncher.cpp:277-287) over the launch: the window
        // the item's grenade_launcher_mortar_mode names, less the seconds
        // held — half a second of a 2 s window books 1.5, where the plain
        // pipe's launch leaves the spawn's 2.
        var rig = new ProjectileRig();
        var ball = rig.Pipebomb();
        rig.Directory.Instance = ball;
        var thinks = new ThinkScheduler();
        ball.BindThinks(thinks);
        rig.Owners.Item = rig.AttributeItem(TFProjectileBehaviour.MortarModeAttribute,
                                            AttributeDescriptionFormat.Additive, 2f);

        rig.Effects.Projectile(Attack(3), CannonballScript(Script("tf_weapon_grenadelauncher")), default, 0.5f);

        Assert.Equal(1.5f, Assert.Single(thinks.Bookings()).Time, 3);
    }

    [Fact]
    public void TheRocketSpecialistsLevelSpeedsTheLaunch()
    {
        // 1100 * RemapValClamped(2, 1, 4, 1.15, 1.6) = 1100 * 1.30
        // (tf_weaponbase_rocket.cpp:293-297).
        var rig = SpecialistRocket(2, out var script);

        rig.Effects.Projectile(Attack(3), script, default, 0f);

        var velocity = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(velocity);
        Assert.Equal(1430f, velocity!.ArgAs<Vector>(1).X, 0.5f);
    }

    [Fact]
    public void TheRocketSpecialistsLaunchSpeedCapsAt3000()
    {
        // A 4000 u/s script at level 4: 4000 * 1.6 = 6400, capped
        // (tf_weaponbase_rocket.cpp:296).
        var rig = new ProjectileRig();
        var script = Script("tf_weapon_rocketlauncher");
        var rocket = rig.Rocket();
        rig.Directory.Instance = rocket;
        rig.Owners.Item = rig.AttributeItem("rocket_specialist", AttributeDescriptionFormat.Additive, 4);
        var fast = new TFWeaponScript { Base = script.Base, Primary = script.Primary with { ProjectileSpeed = 4000f } };

        rig.Effects.Projectile(Attack(3), fast, default, 0f);

        var velocity = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(velocity);
        Assert.Equal(TFProjectileEffects.SpecialistSpeedCap, velocity!.ArgAs<Vector>(1).X, 0.5f);
    }

    /// <summary>A launchable rocket whose owner's item carries rocket_specialist at <paramref name="level"/>.</summary>
    private static ProjectileRig SpecialistRocket(int level, out TFWeaponScript script)
    {
        var rig = new ProjectileRig();
        script = Script("tf_weapon_rocketlauncher");
        rig.Directory.Instance = rig.Rocket();
        rig.Owners.Item = rig.AttributeItem("rocket_specialist", AttributeDescriptionFormat.Additive, level);
        return rig;
    }

    [Fact]
    public void TheRocketLauncherFoldsItsMuzzleHeightWhenTheShooterCrouches()
    {
        // FireRocket's FL_DUCKING arm (tf_weaponbase_gun.cpp:521-525): the
        // offset's z becomes 8, so the rocket clears a crouched holder.
        var rig = new ProjectileRig();
        rig.Directory.Instance = null;
        rig.Owners.Ducking = true;

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_rocketlauncher"), default, 0f);

        var placed = rig.Engine.Last("Entities.SetOrigin");
        Assert.NotNull(placed);
        var muzzle = placed!.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.RocketMuzzleForward, muzzle.X, 0.5f);
        Assert.Equal(64f + TFProjectileDefs.RocketMuzzleUpDucked, muzzle.Z, 0.5f);
    }

    [Fact]
    public void TheGrenadeLauncherCreatesAPipe()
    {
        var rig = new ProjectileRig();
        rig.Directory.Instance = null;

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_grenadelauncher"), default, 0f);

        var create = rig.Engine.Last("Entities.Create");
        Assert.NotNull(create);
        Assert.Equal(TFProjectileEntityNames.Pipebomb, create!.ArgAs<string>(0));
    }

    [Fact]
    public void TheGrenadeLauncherArmsItsPipeAtTheLaunchSpeed()
    {
        var rig = new ProjectileRig();
        var script = Script("tf_weapon_grenadelauncher");
        var pipe = rig.Pipebomb();
        rig.Directory.Instance = pipe;

        rig.Effects.Projectile(Attack(3), script, default, 0f);

        var velocity = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(velocity);
        Assert.Equal(TFProjectileDefs.Speed(script.Primary.ProjectileSpeed, TFProjectileDefs.PipeSpeed),
                     velocity!.ArgAs<Vector>(1).X, 0.5f);
    }

    [Fact]
    public void ThePipeIsBornAtThePipesMuzzleAndLobedUpAtTwoHundred()
    {
        // FirePipeBomb: vecOffset (16, 8, -6) from the shoot position, and
        // vecVelocity's up * 200 (tf_weaponbase_gun.cpp:660, 687). At the
        // stream's midpoint both jitter draws are zero, so the launch is pure.
        var rig = new ProjectileRig();
        var pipe = rig.Pipebomb();
        rig.Directory.Instance = pipe;

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_grenadelauncher"), default, 0f);

        var muzzle = rig.Engine.Last("Entities.SetOrigin")!.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.PipeMuzzleForward, muzzle.X, 0.5f);
        Assert.Equal(-TFProjectileDefs.PipeMuzzleRight, muzzle.Y, 0.5f); // right is -Y at yaw 0
        Assert.Equal(64f + TFProjectileDefs.PipeMuzzleUp, muzzle.Z, 0.5f);

        var velocity = rig.Engine.Last("Entities.SetVelocity")!.ArgAs<Vector>(1);
        Assert.Equal(0f, velocity.Y, 0.5f);
        Assert.Equal(TFProjectileDefs.PipeLaunchUp, velocity.Z, 0.5f);
    }

    [Fact]
    public void ThePipesLaunchJittersRightAndUp()
    {
        // The two rand(-10, 10) draws (tf_weaponbase_gun.cpp:687-688): with
        // the stream dialled to its low end both land on -10.
        var rig = new ProjectileRig();
        rig.Random.At = 0f;
        var pipe = rig.Pipebomb();
        rig.Directory.Instance = pipe;

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_grenadelauncher"), default, 0f);

        var velocity = rig.Engine.Last("Entities.SetVelocity")!.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.PipeLaunchJitter, velocity.Y, 0.5f); // -10 along right, which is -Y
        Assert.Equal(TFProjectileDefs.PipeLaunchUp - TFProjectileDefs.PipeLaunchJitter, velocity.Z, 0.5f);
    }

    [Fact]
    public void TheSyringeJittersItsAnglesByOneAndAHalfDegrees()
    {
        // FireNail's spread (tf_weaponbase_gun.cpp:611-623): flSpread is 1.5
        // (plus GetProjectileSpread, which no SDK weapon overrides), drawn
        // onto the pitch and the yaw. With the stream at its high end both
        // draws land on +1.5, so the syringe leaves at that cone.
        var rig = new ProjectileRig();
        rig.Random.At = 1f;
        var script = Script("tf_weapon_syringegun_medic");
        var syringe = rig.Syringe();
        rig.Directory.Instance = syringe;

        rig.Effects.Projectile(Attack(3), script, default, 0f);

        var velocity = rig.Engine.Last("Entities.SetVelocity")!.ArgAs<Vector>(1);
        var thrown = Angles.Forward(new Vector(1.5f, 1.5f, 0f))
                   * TFProjectileDefs.Speed(script.Primary.ProjectileSpeed, TFProjectileDefs.SyringeSpeed);
        Assert.Equal(thrown.X, velocity.X, 0.5f);
        Assert.Equal(thrown.Y, velocity.Y, 0.5f);
        Assert.Equal(thrown.Z, velocity.Z, 0.5f);
    }

    [Fact]
    public void ThePipeConesWithItsSpreadAttribute()
    {
        // FirePipeBomb's projectile_spread_angle (tf_weaponbase_gun.cpp:654-661):
        // a RandomAngle over ±the attribute added to the eye angles, its roll
        // zeroed — the three draws with the launch jitter's two to follow.
        // Dialled high: pitch and yaw +10, and the pipe leaves along that
        // cone (the Lob's own jitter draws then land on +10 too).
        var rig = new ProjectileRig();
        rig.Random.At = 1f;
        rig.Owners.Item = rig.AttributeItem("projectile_spread_angle",
                                            AttributeDescriptionFormat.Additive, 10f);
        var script = Script("tf_weapon_grenadelauncher");
        var pipe = rig.Pipebomb();
        rig.Directory.Instance = pipe;

        rig.Effects.Projectile(Attack(3), script, default, 0f);

        var velocity = rig.Engine.Last("Entities.SetVelocity")!.ArgAs<Vector>(1);
        var aim = Angles.Forward(new Vector(10f, 10f, 0f));
        var (right, up) = Angles.RightUp(aim);
        // The cone, then the Lob along the coned basis: its up at 200 and both
        // jitter draws, +10 at this dial.
        var thrown = aim * TFProjectileDefs.Speed(script.Primary.ProjectileSpeed, TFProjectileDefs.PipeSpeed)
                   + up * (TFProjectileDefs.PipeLaunchUp + TFProjectileDefs.PipeLaunchJitter)
                   + right * TFProjectileDefs.PipeLaunchJitter;
        Assert.Equal(thrown.X, velocity.X, 0.5f);
        Assert.Equal(thrown.Y, velocity.Y, 0.5f);
    }

    [Fact]
    public void ThePipeSpeedReadsMultProjectileRange()
    {
        // FirePipeBomb's mult_projectile_range (tf_weaponbase_gun.cpp:685-687):
        // the launch speed through the float hook, the Lob's up and jitter
        // riding the base, not the hook.
        var rig = new ProjectileRig();
        rig.Owners.Item = rig.AttributeItem("mult_projectile_range",
                                            AttributeDescriptionFormat.Percentage, 1.5f);
        var script = Script("tf_weapon_grenadelauncher");
        var pipe = rig.Pipebomb();
        rig.Directory.Instance = pipe;

        rig.Effects.Projectile(Attack(3), script, default, 0f);

        var velocity = rig.Engine.Last("Entities.SetVelocity")!.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.Speed(script.Primary.ProjectileSpeed, TFProjectileDefs.PipeSpeed) * 1.5f,
                     velocity.X, 0.5f);
    }

    [Fact]
    public void ThePipeSpinsLikeStocksLaunchImpulse()
    {
        // Create's ApplyLocalAngularVelocityImpulse of AngularImpulse(600,
        // rand(-1200, 1200), 0): the impulse swizzles into an angular velocity
        // (vector.h:2198), so dialled to its low end the bomb pitches at
        // -1200°/s and rolls at 600°/s.
        var rig = new ProjectileRig();
        rig.Random.At = 0f;
        var pipe = rig.Pipebomb();
        rig.Directory.Instance = pipe;

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_grenadelauncher"), default, 0f);

        Assert.Equal(1, rig.Engine.CountOf("Entities.SetAngularVelocity"));
        var angular = rig.Engine.Last("Entities.SetAngularVelocity")!.ArgAs<Vector>(1);
        Assert.Equal(-TFProjectileDefs.PipeSpinPitchMax, angular.X, 0.5f);
        Assert.Equal(0f, angular.Y, 0.5f);
        Assert.Equal(TFProjectileDefs.PipeSpinRoll, angular.Z, 0.5f);
    }

    [Fact]
    public void TheSyringeGunCreatesASyringe()
    {
        var rig = new ProjectileRig();
        rig.Directory.Instance = null;

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_syringegun_medic"), default, 0f);

        var create = rig.Engine.Last("Entities.Create");
        Assert.NotNull(create);
        Assert.Equal(TFProjectileEntityNames.Syringe, create!.ArgAs<string>(0));
    }

    [Fact]
    public void TheSyringeGunArmsItsSyringeAtTheLaunchSpeed()
    {
        var rig = new ProjectileRig();
        var script = Script("tf_weapon_syringegun_medic");
        var syringe = rig.Syringe();
        rig.Directory.Instance = syringe;

        rig.Effects.Projectile(Attack(3), script, default, 0f);

        var velocity = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(velocity);
        Assert.Equal(TFProjectileDefs.Speed(script.Primary.ProjectileSpeed, TFProjectileDefs.SyringeSpeed),
                     velocity!.ArgAs<Vector>(1).X, 0.5f);
    }

    [Fact]
    public void TheSyringeGunCreatesItsSyringeAtItsOwnMuzzle()
    {
        // FireNail's vecOffset (16, 6, -8) (tf_weaponbase_gun.cpp:620), the
        // third launcher with its own muzzle.
        var rig = new ProjectileRig();
        rig.Directory.Instance = null;

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_syringegun_medic"), default, 0f);

        var muzzle = rig.Engine.Last("Entities.SetOrigin")!.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.SyringeMuzzleForward, muzzle.X, 0.5f);
        Assert.Equal(-TFProjectileDefs.SyringeMuzzleRight, muzzle.Y, 0.5f); // right is -Y at yaw 0
        Assert.Equal(64f + TFProjectileDefs.SyringeMuzzleUp, muzzle.Z, 0.5f);
    }

    [Fact]
    public void TheSyringeDispatchesItsNeedleToTheClientAsStockDoes()
    {
        // tf_projectile_base.cpp's GAME_DLL arm (:172-235): the server entity
        // hides itself (EF_NODRAW at :172-173) and the needle goes out as the
        // named client effect — Flags the lifetime (:220), Start the launch
        // velocity, Material the born model (:229), EntIndex the shooter
        // (:230). Before the dispatch existed ours carried only the server
        // entity: on the world touch it removed itself at once and nothing
        // stayed on the wall; stock's stuck needle is the client tempent.
        var rig = new ProjectileRig();
        // Born's managed half must answer, or the launch returns before the
        // dispatch arm — unlike the facts that stop at Create/SetOrigin/SetVelocity.
        rig.Directory.Instance = rig.Syringe();
        Vector spawnPoint = new(1_888f, 1_368f, 260f); // the live pose's setpos
        rig.Engine.Returns("Entities.GetOrigin", spawnPoint); // Born's placement, read back
        rig.Engine.Returns("Entities.GetIndex", 9);
        var script = Script("tf_weapon_syringegun_medic");

        rig.Effects.Projectile(Attack(3), script, default, 0f);

        var sent = Assert.Single(rig.Temps.Effects); // red before the fix: nothing dispatched
        Assert.Equal("ClientProjectile_Syringe", sent.Name);
        Assert.Equal(0f, sent.Delay);
        Assert.Equal(TeFilterKind.Pvs, sent.Filter.Kind);
        Assert.Equal(spawnPoint, sent.Filter.Origin);
        Assert.True(sent.Filter.IgnorePredictionCull,
            "the shooter survives the prediction cull: stock shows the owner its own needle through the predicted "
          + "local dispatch (tf_projectile_base.cpp:231-233, the client arm), and this client has only the networked "
          + "one — a filter the cull empties leaves the shooter with no needle at all (EffectsServer.cpp:58-76, "
          + "whose IgnorePredictionCull arm keeps the host)");
        Assert.Equal(spawnPoint, sent.Data.Origin);
        // Eyes at yaw 0: the launch rides +X at the syringe speed (the same
        // ask the arming fact above answers for Entities.SetVelocity).
        Assert.Equal(TFProjectileDefs.Speed(script.Primary.ProjectileSpeed, TFProjectileDefs.SyringeSpeed),
                     sent.Data.Start.X, 0.5f);
        Assert.Equal(6, sent.Data.Flags);
        Assert.Equal(44, sent.Data.Material);
        Assert.Equal(9, sent.Data.EntIndex);
        Assert.Equal(0, sent.Data.DamageType); // non-crit shot: stock's bit stays off
        var hidden = rig.Engine.Last("EntityRender.SetVisible"); // the hide arm
        Assert.NotNull(hidden);
        Assert.False(hidden!.ArgAs<bool>(1));
    }

    [Fact]
    public void TheStickyLauncherCreatesAStickyInTheTray()
    {
        var rig = new ProjectileRig();
        var sticky = rig.Sticky();
        rig.Directory.Instance = sticky;

        // Fully charged: the bomb leaves at TF_PIPEBOMB_MAX_CHARGE_VEL.
        rig.Effects.Projectile(Attack(3), Script("tf_weapon_pipebomblauncher"), default,
                               TFProjectileBehaviour.MaxChargeTime);

        var create = rig.Engine.Last("Entities.Create");
        Assert.NotNull(create);
        Assert.Equal(TFProjectileEntityNames.Stickybomb, create!.ArgAs<string>(0));
        Assert.Equal(1, rig.Tray.Count(3));

        var velocity = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(velocity);
        Assert.Equal(TFProjectileDefs.StickyMaxChargeSpeed, velocity!.ArgAs<Vector>(1).X, 0.5f);
    }

    [Fact]
    public void TheQuickiebombsChargeRateStretchesTheSpeedRemap()
    {
        // GetProjectileSpeed (tf_weapon_pipebomblauncher.cpp:353-362) remaps
        // over GetChargeMaxTime, the window the launcher's
        // stickybomb_charge_rate hook bends: at twice the window a half
        // charge reads the mid-remap 1650, not the stock window's top 2400.
        var rig = new ProjectileRig();
        rig.Owners.Item = rig.AttributeItem(TFProjectileBehaviour.ChargeRateAttribute,
                                            AttributeDescriptionFormat.Percentage, 2f);
        rig.Directory.Instance = rig.Sticky();

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_pipebomblauncher"), default, 4f);

        var velocity = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(velocity);
        Assert.Equal(1650f, velocity!.ArgAs<Vector>(1).X, 0.5f);
    }

    [Fact]
    public void TheStickybombJumpersOverrideFiresThePracticeBomb()
    {
        // FireProjectile's override (tf_weaponbase_gun.cpp:260-268): the
        // item's 14 is TF_PROJECTILE_PIPEBOMB_PRACTICE, so the launch rides
        // the sticky arm with the practice mode stamped — Create's
        // translation (:437-458), the shell and the damage-type arms read it.
        var rig = new ProjectileRig();
        rig.Owners.Item = rig.AttributeItem(TFProjectileEffects.OverrideProjectileAttribute,
                                            AttributeDescriptionFormat.Additive, 14f);
        var sticky = rig.Sticky();
        rig.Directory.Instance = sticky;

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_pipebomblauncher"), default, 0f);

        Assert.Equal(TFProjectileDefs.GrenadeModeRemoteDetonatePractice, sticky.PipebombMode);
        var create = rig.Engine.Last("Entities.Create");
        Assert.NotNull(create);
        Assert.Equal(TFProjectileEntityNames.Stickybomb, create!.ArgAs<string>(0));

        // Spawn's practice arm (tf_weapon_grenade_pipebomb.cpp:499-502): the
        // stamp re-dresses the bomb w_stickybomb2.
        var shell = rig.Engine.Last("EntityRender.SetModel");
        Assert.NotNull(shell);
        Assert.Equal(TFProjectileDefs.StickyPracticeModel, shell!.ArgAs<string>(1));
    }

    [Fact]
    public void TheScottishResistancesBombIsTheDefensiveShell()
    {
        // LaunchGrenade's DOT arm (tf_weapon_pipebomblauncher.cpp:305-309):
        // set_detonate_mode 1 — the Scottish Resistance's — dresses the bomb
        // w_stickybomb_d at the launch, beside the aimed detonate the
        // secondary fire already runs.
        var rig = new ProjectileRig();
        rig.Owners.Item = rig.AttributeItem("set_detonate_mode", AttributeDescriptionFormat.Additive, 1);
        var sticky = rig.Sticky();
        rig.Directory.Instance = sticky;

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_pipebomblauncher"), default, 0f);

        var shell = rig.Engine.Last("EntityRender.SetModel");
        Assert.NotNull(shell);
        Assert.Equal(TFProjectileDefs.StickyDefensiveModel, shell!.ArgAs<string>(1));
    }

    [Fact]
    public void APlainStickyLauncherStampsTheRemoteMode()
    {
        // The untranslated birth: without the override the sticky is stock's
        // remote-detonate bomb, the young-pipe damage type's bearer.
        var rig = new ProjectileRig();
        var sticky = rig.Sticky();
        rig.Directory.Instance = sticky;

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_pipebomblauncher"), default, 0f);

        Assert.Equal(TFProjectileDefs.GrenadeModeRemoteDetonate, sticky.PipebombMode);
    }

    [Fact]
    public void TheStickyLauncherSharesThePipesLobAndMuzzle()
    {
        // The sticky rides FirePipeBomb too: the pipes' vecOffset and the
        // same up * 200 beneath its charged forward speed.
        var rig = new ProjectileRig();
        var sticky = rig.Sticky();
        rig.Directory.Instance = sticky;

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_pipebomblauncher"), default,
                               TFProjectileBehaviour.MaxChargeTime);

        var muzzle = rig.Engine.Last("Entities.SetOrigin")!.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.PipeMuzzleForward, muzzle.X, 0.5f);
        Assert.Equal(-TFProjectileDefs.PipeMuzzleRight, muzzle.Y, 0.5f); // right is -Y at yaw 0
        Assert.Equal(64f + TFProjectileDefs.PipeMuzzleUp, muzzle.Z, 0.5f);

        var velocity = rig.Engine.Last("Entities.SetVelocity")!.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.StickyMaxChargeSpeed, velocity.X, 0.5f);
        Assert.Equal(TFProjectileDefs.PipeLaunchUp, velocity.Z, 0.5f);
    }

    [Fact]
    public void TheStickyLauncherSpinsItsStickyToo()
    {
        // The remote arm shares Create's spin: dialled to its top end the
        // pitch draw lands on +1200.
        var rig = new ProjectileRig();
        rig.Random.At = 1f;
        var sticky = rig.Sticky();
        rig.Directory.Instance = sticky;

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_pipebomblauncher"), default,
                               TFProjectileBehaviour.MaxChargeTime);

        var angular = rig.Engine.Last("Entities.SetAngularVelocity")!.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.PipeSpinPitchMax, angular.X, 0.5f);
        Assert.Equal(TFProjectileDefs.PipeSpinRoll, angular.Z, 0.5f);
    }

    [Fact]
    public void DetonateClearsTheCommandingOwnersTray()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere();

        var sticky = rig.Sticky();
        sticky.OnThink();
        rig.Tray.Register(3, sticky);

        rig.Effects.Detonate(Attack(3), Script("tf_weapon_pipebomblauncher"));

        Assert.Equal(0, rig.Tray.Count(3));
        // The commanding owner's sticky left the world.
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void AddMaxPipebombsRaisesTheLiveCap()
    {
        // FireProjectile keeps TF_WEAPON_PIPEBOMB_COUNT — the stock 8
        // (tf_shareddefs.h:391) — through add_max_pipebombs
        // (tf_weapon_pipebomblauncher.cpp:381-392): one added, the ninth
        // sticky stays out instead of popping the oldest. Without the hook
        // the cap clamps at 8 and this reads Failed: 1.
        var rig = new ProjectileRig();
        rig.Owners.Item = rig.AttributeItem("add_max_pipebombs", AttributeDescriptionFormat.Additive, 1f);
        rig.Directory.Instance = rig.Sticky();

        for (var i = 0; i < 9; i++)
            rig.Effects.Projectile(Attack(3), Script("tf_weapon_pipebomblauncher"), default, 0f);

        Assert.Equal(9, rig.Tray.Count(3));
    }

    [Fact]
    public void StickyArmTimeDelaysDetonation()
    {
        // GetLiveTime (tf_weapon_grenade_pipebomb.cpp:1186-1190): the hook's
        // bonus lifts the arm past the bare convar, and the arm think books
        // at the total (:1144) — the armed gate waits the full window. The
        // launch stamps it; without the stamp the booking is the bare 1.0.
        var rig = new ProjectileRig();
        rig.StickyVars.LiveTime.Hold("1.0");
        rig.Owners.Item = rig.AttributeItem("sticky_arm_time", AttributeDescriptionFormat.Additive, 0.4f);
        var sticky = rig.Sticky();
        rig.Directory.Instance = sticky;
        var thinks = new ThinkScheduler();
        sticky.BindThinks(thinks);

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_pipebomblauncher"), default, 0f);
        sticky.OnSpawn(); // the arm booking, at the stamped total (:1144)

        Assert.Equal(1.4f, Assert.Single(thinks.Bookings()).Time, 4); // 1.0 + 0.4
    }

    /// <summary>The aimed sticky: straight ahead of the eyes at yaw 0.</summary>
    private static readonly EntityHandle AimedBomb = new(0x0001_0000u | 11);

    /// <summary>The other sticky, off to the right of the cone.</summary>
    private static readonly EntityHandle SideBomb = new(0x0001_0000u | 12);

    /// <summary>Two armed stickies, ahead and to the right, as the world holds them.</summary>
    private static void ArmedPair(ProjectileRig rig)
    {
        rig.Engine.ReturnsEachCall("Entities.GetWorldSpaceCenter", () =>
        {
            if (rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == SideBomb)
                return new Vector(200f, 200f, 64f);
            return new Vector(200f, 0f, 64f);
        });
        rig.Engine.ReturnsEachCall("Entities.GetOrigin", () =>
        {
            object? ask = rig.Engine.Calls[^1].Args[0];
            if (ask is EntityHandle h && h == AimedBomb)
                return new Vector(200f, 0f, 64f);
            if (ask is EntityHandle s && s == SideBomb)
                return new Vector(200f, 200f, 64f);
            return default;
        });

        foreach (var (handle, armed) in new[] { (AimedBomb, true), (SideBomb, true) })
        {
            var sticky = rig.Sticky();
            sticky.Handle = handle;
            sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                            TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
            if (armed)
                sticky.OnThink(); // the arm think, as the Detonate gate reads
            rig.Tray.Register(3, sticky);
        }
    }

    [Fact]
    public void DetonateModeOneFiresOnlyTheAimedStickies()
    {
        // TF_DETONATE_MODE_DOT (tf_weapon_pipebomblauncher.h:24):
        // ModifyPipebombsInView (tf_weapon_pipebomblauncher.cpp:592-605)
        // keeps only bombs in view — the centre dotting over 0.975 with the
        // eyes' forward, or within the bomb's damage radius of the player.
        // At yaw 0 the ahead bomb dots at 1; the right one misses both arms.
        // With the mode unimplemented the whole tray goes and this is Failed: 1.
        var rig = new ProjectileRig();
        rig.Owners.Item = rig.AttributeItem("set_detonate_mode", AttributeDescriptionFormat.Additive, 1f);
        ArmedPair(rig);

        rig.Effects.Detonate(Attack(3), Script("tf_weapon_pipebomblauncher"));

        Assert.Equal(1, rig.Tray.Count(3)); // the aimed one went, the side one stayed
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void DetonateModeZeroFiresThemAll()
    {
        // TF_DETONATE_MODE_STANDARD 0, stock's unset default
        // (tf_weapon_pipebomblauncher.h:23): the whole-tray walk of
        // DetonateRemotePipebombs (:530-571) — every armed sticky, aimed or
        // not. The aim cone must not gate it.
        var rig = new ProjectileRig();
        rig.Owners.Item = rig.AttributeItem("set_detonate_mode", AttributeDescriptionFormat.Additive, 0f);
        ArmedPair(rig);

        rig.Effects.Detonate(Attack(3), Script("tf_weapon_pipebomblauncher"));

        Assert.Equal(0, rig.Tray.Count(3));
        Assert.Equal(2, rig.Engine.CountOf("Entities.Remove"));
    }
}

public class TFBowTiredAimFacts
{
    private static TFWeaponScript Script(string name)
    {
        var files = WeaponFixtures.ShippedWithClasses();
        return new TFWeaponScripts(new RecordingLog(), files).Get(name)
            ?? throw new InvalidOperationException($"no script {name}");
    }

    /// <summary>A launch's arrow velocity: the rig's dial names both draws.</summary>
    private static Vector Loose(bool tired, float at)
    {
        var rig = new ProjectileRig { };
        rig.Random.At = at;
        rig.Directory.Instance = rig.Arrow();
        var w = new WeaponNetState();
        w.BindLocal();
        if (tired)
            w.BehaviourMode |= TFBowBehaviour.ModeTired;

        rig.Effects.Projectile(new WeaponAttackContext(default!, w, null!, 1f, true, null!,
                                                       new WeaponCommand(1, 5, 3)),
                               Script("tf_weapon_compound_bow"), default, 1f);

        return rig.Engine.Last("Entities.SetVelocity").ArgAs<Vector>(1);
    }

    [Fact]
    public void ATiredDrawLoosesOffTheAim()
    {
        // GetProjectileFireSetup's tail (:490-503): the tired launch re-draws
        // the aim ±6° in pitch and yaw before the basis — the arrow leaves
        // along the jittered forward, not the eye's.
        var plain = Loose(tired: false, at: 0.5f);
        var wild = Loose(tired: true, at: 1f);

        // At the dial's ends the plain arm is the eye's forward times the
        // full-charge 2600; the tired one is off it in both axes.
        Assert.Equal(0f, plain.Y, 3);
        Assert.Equal(0f, plain.Z, 3);
        Assert.True(wild.Y != 0f || wild.Z != 0f);
        Assert.NotEqual(plain.X, wild.X, 3);
        Assert.NotEqual(plain.Z, wild.Z, 3);
    }
}

/// <summary>The ball's charged-shot shake, recorded whole for the facts.</summary>
internal sealed class BallShakeRecorder : IScreenEffects
{
    public readonly List<(Vector Origin, float Amplitude, float Frequency, float Duration, float Radius)> Shakes = [];

    public void Shake(Vector origin, float amplitude, float frequency, float duration, float radius,
                      ShakeCommand command = ShakeCommand.Start, bool airShake = false)
        => Shakes.Add((origin, amplitude, frequency, duration, radius));

    public void Fade(EntityHandle player, int r, int g, int b, int a, float duration, float holdTime, FadeFlags flags) { }
}

/// <summary>
/// <see cref="TFProjectileEnergyBall"/>: stock's <c>Explode</c>
/// (<c>tf_projectile_energy_ball.cpp:259-337</c>) as the blast it is — the
/// touched victim scored first at distance zero, the charged shot's shake
/// (:289-292), the oriented explosion particle (:281-286, :342-352), the hit's
/// bits and plasma custom (:365-390) — the shake only for the charged ball.
/// </summary>
public sealed class TFProjectileEnergyBallFacts
{
    private static TFProjectileEnergyBall Arm(ProjectileRig rig, bool charged, bool critical = false)
    {
        var ball = rig.Ball();
        // The blast's push arm and the explosion's index ask these every
        // impact; a fact's own Returns after the arm turns the answer.
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(0f, 0f, 64f));
        rig.Engine.Returns("Precache.ParticleSystemIndex", 7);
        // The victim on the other team: a touch inside the teammate window
        // (TFProjectileDefs.OwnTeamWithinPass) flies past a teammate.
        rig.Engine.ReturnsEachCall("Players.GetTeam",
                                   () => rig.Engine.Calls[^1].ArgAs<EntityHandle>(0) == ProjectileRig.Victim ? 3 : 2);
        ball.ArmedBy(100f, TFWeaponId.Raygun, "tf_weapon_raygun", critical, charged, 1);
        return ball;
    }

    [Fact]
    public void APlainBallBlastsTheTouchedVictimForItsFullShot()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        var ball = Arm(rig, charged: false);

        ball.OnTouch(ProjectileRig.Victim);

        // The blast's direct victim (:306-325): scored at distance zero, so
        // the shot lands whole — the old shape had no splash arm at all.
        var hit = rig.Engine.Last("Damage.TakeDamageAnswered");
        Assert.Equal(100f, hit!.ArgAs<float>(2), 3);
    }

    [Fact]
    public void APlainShotCarriesTheBlastBitsWithoutIgnite()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        var ball = Arm(rig, charged: false);

        ball.OnTouch(ProjectileRig.Victim);

        // GetDamageType (:365-382): blast, half falloff and the distance mod
        // always; ignite is the charged ball's only extra bit.
        var hit = rig.Engine.Last("Damage.TakeDamageAnswered");
        Assert.Equal(DamageType.Blast | TFDamageBits.HalfFalloff | TFDamageBits.UseDistanceMod,
                     hit!.ArgAs<DamageType>(3));
    }

    [Fact]
    public void ACriticalPlainShotAddsTheCritBit()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        var ball = Arm(rig, charged: false, critical: true);

        ball.OnTouch(ProjectileRig.Victim);

        // The plain crit (:375): the crit bit rides the shot's own crit.
        var hit = rig.Engine.Last("Damage.TakeDamageAnswered");
        Assert.True(hit!.ArgAs<DamageType>(3).HasFlag(TFDamageBits.Critical),
            "a plain crit ball carries DMG_CRITICAL");
    }

    [Fact]
    public void AChargedShotIgnitesInsteadOfCarryingTheCritBit()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        var ball = Arm(rig, charged: true, critical: true);

        ball.OnTouch(ProjectileRig.Victim);

        // The charged bits (:368-373): ignite on, the crit bit never — the
        // charge's mini-crit comes through the ladder instead.
        var bits = rig.Engine.Last("Damage.TakeDamageAnswered")!.ArgAs<DamageType>(3);
        Assert.True(bits.HasFlag(TFDamageBits.Ignite), "the charged ball ignites");
        Assert.False(bits.HasFlag(TFDamageBits.Critical),
            "the charged shot answers with no crit bit even when the shot was crit");
    }

    [Fact]
    public void TheChargedBallShakesTheScreenOnceAtItsCentre()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        var ball = Arm(rig, charged: true);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(4f, -8f, 32f));

        ball.OnTouch(ProjectileRig.Victim);

        // UTIL_ScreenShake (:289-292): the charged ball's alone, with stock's
        // five numbers at the ball's world-space centre.
        Assert.Single(rig.Screen.Shakes);
        var shake = rig.Screen.Shakes[0];
        Assert.Equal(new Vector(4f, -8f, 32f), shake.Origin);
        Assert.Equal((25f, 150f, 1f, 750f), (shake.Amplitude, shake.Frequency, shake.Duration, shake.Radius));
    }

    [Fact]
    public void APlainBallSendsNoShake()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        var ball = Arm(rig, charged: false);

        ball.OnTouch(ProjectileRig.Victim);

        Assert.Empty(rig.Screen.Shakes);
    }

    [Fact]
    public void ThePlainBallNamesTheNormalCoreAndDispatches()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Precache.ParticleSystemIndex", 7);
        var ball = Arm(rig, charged: false);

        ball.OnTouch(ProjectileRig.Victim);

        // The oriented explosion (:281-286, :342-352): always dispatched, the
        // core named by the charge, a red owner leaving the suffix off.
        Assert.Equal("drg_cow_explosioncore_normal",
                     rig.Engine.Last("Precache.ParticleSystemIndex")!.ArgAs<string>(0));
        Assert.Contains(rig.Temps.Effects, e => e.Name == "ParticleEffect");
    }

    [Fact]
    public void TheChargedCoreIsNamedForTheCharge()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Precache.ParticleSystemIndex", 7);
        var ball = Arm(rig, charged: true);

        ball.OnTouch(ProjectileRig.Victim);

        Assert.Equal("drg_cow_explosioncore_charged",
                     rig.Engine.Last("Precache.ParticleSystemIndex")!.ArgAs<string>(0));
    }

    [Fact]
    public void ABlueOwnersParticleTakesTheBlueSuffix()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Precache.ParticleSystemIndex", 7);
        var ball = Arm(rig, charged: false);
        // A BLU owner (TF_TEAM_BLUE) and a RED victim.
        rig.Engine.ReturnsEachCall("Players.GetTeam",
                                   () => rig.Engine.Calls[^1].ArgAs<EntityHandle>(0) == ProjectileRig.Victim ? 2 : 3);

        ball.OnTouch(ProjectileRig.Victim);

        Assert.Equal("drg_cow_explosioncore_normal_blue",
                     rig.Engine.Last("Precache.ParticleSystemIndex")!.ArgAs<string>(0));
    }

    [Fact]
    public void ThePlainHitRidesThePlasmaScope()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        TFDamageCustom seen = TFDamageCustom.None;
        rig.Engine.WhenCalled("Damage.TakeDamageAnswered",
                              call =>
                              {
                                  if (rig.Scopes.TryCurrent(out var custom, out var weapon, out var name))
                                      seen = custom;
                              });
        var ball = Arm(rig, charged: false);

        ball.OnTouch(ProjectileRig.Victim);

        // GetDamageCustom (:387-390): the plain ball's hit carries
        // TF_DMG_CUSTOM_PLASMA beside it, so the building's plasma arm reads it.
        Assert.Equal(TFDamageCustom.Plasma, seen);
    }

    [Fact]
    public void TheChargedHitRidesTheChargedPlasmaScope()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        TFDamageCustom seen = TFDamageCustom.None;
        rig.Engine.WhenCalled("Damage.TakeDamageAnswered",
                              call =>
                              {
                                  if (rig.Scopes.TryCurrent(out var custom, out var weapon, out var name))
                                      seen = custom;
                              });
        var ball = Arm(rig, charged: true);

        ball.OnTouch(ProjectileRig.Victim);

        Assert.Equal(TFDamageCustom.PlasmaCharged, seen);
    }

    [Fact]
    public void TheRingHitRidesThePlasmaScopeToo()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        TFDamageCustom seen = TFDamageCustom.None;
        rig.Engine.WhenCalled("Damage.TakeDamage",
                              call =>
                              {
                                  if (rig.Scopes.TryCurrent(out var custom, out var weapon, out var name))
                                      seen = custom;
                              });
        var ring = rig.Ring();
        ring.ArmedBy(60f, TFWeaponId.DrgPomson, "tf_weapon_drg_pomson", false, 1);

        ring.OnTouch(ProjectileRig.Victim);

        // Stock's ring answers TF_DMG_CUSTOM_PLASMA for every hit
        // (tf_projectile_energy_ring.cpp:267), so the building reads its
        // plasma arm off a pomson or bison hit as well.
        Assert.Equal(TFDamageCustom.Plasma, seen);
    }
}
