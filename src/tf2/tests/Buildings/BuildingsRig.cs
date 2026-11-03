using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;
using TF2.Server;
using TF2.Server.Buildings;
using TF2.Server.Combat;
using TF2.Server.Econ;
using TF2.Server.Entities;
using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Server.Medigun;
using TF2.Shared;
using TF2.Shared.Movement;
using TF2.Tests.Entities.Items;
using TF2.Tests.Medigun;
using TF2.Tests.Rules;

namespace TF2.Tests.Buildings;

/// <summary>What the buildings announced, in order.</summary>
internal sealed class FakeAnnouncer : ITFObjectAnnouncer
{
    public List<string> Said { get; } = new();

    public void Built(EntityHandle builder, TFObjectType type, int mode) => Said.Add($"built {type} {mode}");
    public void Destroyed(EntityHandle builder, TFObjectType type, int mode) => Said.Add($"destroyed {type} {mode}");
}

/// <summary>
/// Deterministic draws: always the bottom of the range, unless a fact arms
/// <see cref="Ints"/> — the teleporter's bread roll is drawn first and the
/// queue hands out the fact's answer ahead of the floor. <see cref="IntDraws"/>
/// records every ask, which is how a fact reads the bound the subject rolled
/// against (the bread roll's 2-versus-19, the model draw's class range).
/// </summary>
internal sealed class RigRandom : IUniformRandom
{
    public Queue<int> Ints { get; } = new();
    public List<(int Min, int Max)> IntDraws { get; } = new();

    public float RandomFloat(float min, float max) => min;
    public int RandomInt(int min, int max)
    {
        IntDraws.Add((min, max));
        return Ints.Count > 0 ? Ints.Dequeue() : min;
    }
    public float RandomFloatExp(float min, float max, float exponent) => min;
}

/// <summary>The convar registry's backend: declared defaults held, writes kept — the value a subject's next read answers.</summary>
internal sealed class StoredConVarBackend : IConVarBackend
{
    private readonly List<string> _names = [];
    private readonly List<string> _values = [];

    public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
    {
        _names.Add(name);
        _values.Add(defaultValue);
        return _names.Count - 1;
    }

    public void Set(int id, string value) => _values[id] = value;
    public string Get(int id) => _values[id];
    public string? ValueOf(string name) => _names.Contains(name) ? _values[_names.IndexOf(name)] : null;
}

/// <summary>The effects the buildings dispatched, in order: the name, the record, the filter it rode and the engine-call count at the send.</summary>
internal sealed class BuildingTempEnts(RecordingEngine engine) : TempEntsDouble
{
    public readonly List<(string Name, TeEffectData Data, TeFilter Filter, int EngineCalls)> Dispatched = [];

    public override void DispatchEffect(TeFilter filter, float delay, string effectName, in TeEffectData data)
        => Dispatched.Add((effectName, data, filter, engine.Calls.Count));
}

/// <summary>The screen fades a subject sent, in order; the shakes are nobody's here.</summary>
internal sealed class RecordingScreen : IScreenEffects
{
    public readonly List<(EntityHandle Player, int R, int G, int B, int A, float Duration, float HoldTime, FadeFlags Flags)> Fades = [];

    public void Shake(Vector origin, float amplitude, float frequency, float duration, float radius,
                      ShakeCommand command = ShakeCommand.Start, bool airShake = false) { }

    public void Fade(EntityHandle player, int r, int g, int b, int a, float duration, float holdTime, FadeFlags flags)
        => Fades.Add((player, r, g, b, a, duration, holdTime, flags));
}

/// <summary>
/// The buildables on a registry over a recording engine, with the players,
/// the announcer and the clock as fakes. Buildings are runtime entities, so
/// creation is a plain registry spawn; where the world is asked (the sentry's
/// sphere and traces, the dispenser's radius) the answers come from the
/// engine fake's returns. The build command's world — the purse, the no-build
/// and respawn refusals, the spawner — is grafted on in
/// <c>BuildingsRig.Builder.cs</c>.
/// </summary>
internal sealed partial class BuildingsRig
{
    public readonly ScriptedEntityRegistry Registry = new(TestEngines.Server());
    public readonly RecordingEngine Engine;
    public readonly FakeMapPlayers Players = new();
    public readonly FakeAnnouncer Announcer = new();
    public readonly FakeClock Clock = new();
    public readonly TFPlayerCarriers Carriers = new();
    public readonly ServiceContainer Services;
    public readonly Projectiles.ProjectileRig.FixedClasses Classes = new();
    public readonly RigRandom Random = new();

    /// <summary>The gib the teleporter last threw, so a bread fact can name it.</summary>
    public EntityHandle LastGib;

    private uint _next = 0x0040_0000u;

    public BuildingsRig()
    {
        Registry.RegisterAll(typeof(TFPowerup).Assembly);
        Registry.RegisterAll(typeof(BuildingsRig).Assembly);
        // The teleporter throws its bread as a gib through the Gibs seam,
        // so the registry must know the class the arrival's spawn asks for.
        Registry.RegisterAll(typeof(SsGib).Assembly);

        Services = TestServices.Ignoring("Host.RandomFloat", "Networking.EntityGetState", "Networking.EntityGetState.moveType",
                                         "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles", "Entities.IsValid",
                                         "EntityRender.LookupSequence", "EntityRender.LookupSequenceByActivity",
                                         "EntityRender.GetModelName", "EntityRender.LookupPoseParameter",
                                         "EntityRender.SetPoseParameter", "Precache.PrecacheModel");
        Engine = (RecordingEngine)Services.Resolve<IEngineEntities>()!;
        Engine.Returns("Entities.GetSpawnFlags", 0)
              .Returns("Entities.IsPlayer", true)
              .Returns("Entities.GetAngles", Vector.Zero)
              .Returns("Entities.GetOrigin", Vector.Zero)
              // The telefrag sweep of a trip nobody staged answers empty;
              // a fact opts entities in with FillsSphere. The blow's answer —
              // what the victim's own TakeDamage returned — is read by
              // nothing here, and facts that care answer it themselves.
              .Returns("WorldQuery.EntitiesInSphere", 0)
              .Returns("Damage.TakeDamageAnswered", 0)
              // The hook facts key a manager by the index the subject reads
              // back; the answer nobody armed is the world entry.
              .Returns("Entities.GetIndex", 0)
              .Returns("Files.GameFileSize", -1);
        // player_healed names the healer and the patient through the engine's
        // player table (TFPlayerHealing.TickHealedPerSecond); the fake's
        // players have no user ids to give.
        Engine.Returns("Players.GetHandle", EntityHandle.None)
              .Returns("Players.GetUserId", 0);
        // The show's records ride the system index: a stable per-name number,
        // so a fact reads the name back off the record's HitBox.
        Engine.ReturnsEachCall("Precache.ParticleSystemIndex", () =>
        {
            string name = (Engine.Last("Precache.ParticleSystemIndex") ?? default).ArgAs<string>(0);
            if (!ParticleIndices.TryGetValue(name, out int index))
                ParticleIndices[name] = index = 100 + ParticleIndices.Count;
            return index;
        });
        // Validity is the registry's: FindByHandle asks it, and a handle the
        // registry holds is valid (EntityHandle.None never is).
        Engine.ReturnsEachCall("Entities.IsValid", () =>
        {
            EntityHandle who = (Engine.Last("Entities.IsValid") ?? default).ArgAs<EntityHandle>(0);
            return Registry.Get(who) is not null;
        });
        // The shell's health, per handle: what the engine's writes leave and
        // what its reads answer — the store the game's wounds land in
        // (shell_standalone_base.cpp:1207).
        Engine.WhenCalled("Entities.SetHealth",
                          c => Healths[c.ArgAs<EntityHandle>(0)] = c.ArgAs<int>(1));
        Engine.ReturnsEachCall("Entities.GetHealth", () =>
        {
            EntityHandle who = (Engine.Last("Entities.GetHealth") ?? default).ArgAs<EntityHandle>(0);
            return Healths.GetValueOrDefault(who);
        });

        Services.AddInstance<IEntityDirectory>(Registry);
        Services.AddInstance<ITFMapPlayers>(Players);
        Services.AddInstance<ITFObjectAnnouncer>(Announcer);
        Services.AddInstance<IGameClock>(Clock);
        // The show's halves: the entity sound the pair speaks and the effects
        // it dispatches, both read back off the records.
        Services.AddInstance<IEngineSound>(Engine);
        Services.AddInstance<ITempEnts>(Effects);
        Services.AddInstance<IScreenEffects>(Screen);
        Services.AddInstance<ITFBuildings>(new TFBuildings(Registry));
        Services.AddInstance<IUniformRandom>(Random);
        Services.AddInstance(new TFDamageScopes());
        Services.AddInstance(new DamageInflictors());
        Services.AddInstance(new TF2.Server.Combat.TFDamageRecords());
        Services.AddInstance(new TF2.Server.Combat.TFSentryDamages());
        // The carrier word the carried building writes (the player_data the
        // movement's speed arm reads); a fact attaches it per player.
        Services.AddInstance(Carriers);

        // The bread the arrival rolls for is thrown as a gib through the
        // Gibs seam, which goes through this double: Create hands out a
        // handle and the Spawn binds the managed instance behind it — the
        // route the rig's own Spawn takes — and the solver takes the loaf
        // (the debris group is the gib's own to ask). The model precaches
        // answer the ignored default (index 0, no type), so Shoot does not
        // refuse the loaf for a model it cannot see.
        Engine.Returns("EntityPhysics.InitPhysics", true)
              .Returns("EntityPhysics.GetMass", 10f);
        Engine.ReturnsEachCall("Entities.Create", () =>
        {
            EntityHandle made = (Engine.Last("Entities.Create") ?? default).ArgAs<string>(0) == EntityNames.Gib
                ? NewHandle()
                : EntityHandle.None;
            LastGib = made;
            return made;
        });
        Engine.WhenCalled("Entities.Spawn", c =>
        {
            EntityHandle h = c.ArgAs<EntityHandle>(0);
            if (h != EntityHandle.None && h == LastGib)
                Registry.Spawn(h, EntityNames.Gib);
        });

        // The blast applier the level 3 pod's rockets and the other flyers
        // reach, over the same double, with the projectile facts' fakes for
        // the classes, owners and resupply it reads.
        var hitGroups = new DamageHitGroups();
        var classes = Classes;
        var resupply = new Projectiles.ProjectileRig.Resupply();
        Services.AddInstance(hitGroups);
        Services.AddInstance<ITFPlayerClasses>(classes);
        Services.AddInstance<ITFPlayerResupply>(resupply);
        Services.AddInstance(new TF2.Shared.Econ.AttributeHooks());
        // The carrier fake the blast and the teleporter's hooks read; a fact
        // arms Managers by the player index the subject reads back.
        Services.AddInstance<ITFWeaponOwners>(Owners);
        Services.AddInstance(new TFBlastApplier(Engine, Engine, Engine, Services.Resolve<TFDamageScopes>()!,
                                                hitGroups, Services.Resolve<DamageInflictors>()!, classes, Log, Engine,
                                                Owners, new TF2.Shared.Econ.AttributeHooks(),
                                                resupply, Engine, Engine, Registry, Carriers));

        // The typed convars the subjects read, held by a backend of the rig's
        // own (the engine's console only records): the declared defaults, and
        // every write a fact makes. The healing stack's registration shares it.
        Convars = new ConVarRegistry(new StoredConVarBackend());
        Convars.RegisterAll(typeof(TFPlayerHealing).Assembly);
        Services.AddInstance(Convars.Get<TFTeleporterFovStartConVar>()!);
        Services.AddInstance(Convars.Get<TFTeleporterFovTimeConVar>()!);
        Services.AddInstance(Convars.Get<TFSentryGunDamageConVar>()!);
        Services.AddInstance(Convars.Get<ObjSapperAmountConVar>()!);

        RegisterHealing();
        Registry.Services = Services;
        // The buildables themselves take the purse now (the wrench's metal),
        // so the seams are up from the spawn, not first command.
        EnsureBuilderSeams();
    }

    /// <summary>The typed convars, registered once over this engine's console; the subjects' knobs read through it.</summary>
    public ConVarRegistry Convars { get; private set; } = null!;

    /// <summary>The heal accumulation's resupply half, the facts read what was taken.</summary>
    public TFHealingResupply HealingResupply { get; private set; } = null!;
    /// <summary>The heal accumulation's condition half, the facts read what was added and removed.</summary>
    public TFHealingConditions HealingConditions { get; private set; } = null!;
    /// <summary>The shared's accumulation over this rig, as production composes it.</summary>
    public TFPlayerHealing Healing { get; private set; } = null!;

    /// <summary>
    /// The shared's accumulation over this rig's engine, registered for the
    /// dispenser's heal arm (the medigun rig's fakes for the seams).
    /// </summary>
    private void RegisterHealing()
    {
        HealingResupply = new TFHealingResupply();
        HealingConditions = new TFHealingConditions();
        Healing = new TFPlayerHealing(Engine, Engine, HealingConditions, HealingResupply,
                                      Convars.Get<TFMaxHealthBoostConVar>()!, Convars.Get<TFBoostDrainTimeConVar>()!,
                                      Convars.Get<TFInvulnTimeConVar>()!, Clock);
        Services.AddInstance(HealingConditions);
        // The condition arm of the send (the teleporter's speed boost) joins
        // the same accumulator the dispenser's heal arm feeds.
        Services.AddInstance<ITFPlayerConditions>(HealingConditions);
        Services.AddInstance<ITFPlayerHealing>(Healing);
    }

    /// <summary>The TF damage scope the rig's subjects read (the sapper's gate among them).</summary>
    public TFDamageScopes Scopes => Services.Resolve<TFDamageScopes>()!;

    /// <summary>The inflictor scopes, as the subjects open them.</summary>
    public DamageInflictors Inflictors => Services.Resolve<DamageInflictors>()!;

    /// <summary>
    /// The carrier fake the blast and the attribute hooks read. A hook fact
    /// arms <see cref="Weapons.FakeOwners.Managers"/> by the player index it
    /// answers on GetIndex and hands the attributes with <see cref="AttributeItem"/>.
    /// </summary>
    public Weapons.FakeOwners Owners { get; } = new();

    /// <summary>The temp entities, resolved after the engine it orders against.</summary>
    public BuildingTempEnts Effects => _effects ??= new(Engine);

    private BuildingTempEnts? _effects;

    /// <summary>The screen effects, resolved the same way: the fades a subject sent.</summary>
    public RecordingScreen Screen => _screen ??= new();

    private RecordingScreen? _screen;

    /// <summary>Where each asked particle name landed, the stable per-name index a record carries.</summary>
    public Dictionary<string, int> ParticleIndices { get; } = new();

    private int _nextAttrDef = 5000;

    /// <summary>A manager carrying one float attribute, the projectile facts' shape.</summary>
    public TF2.Shared.Econ.IAttributeProvider AttributeItem(string hook, TF2.Shared.Econ.AttributeDescriptionFormat format,
                                                             float value)
    {
        var container = new TF2.Shared.Econ.AttributeContainer(null, TF2.Shared.Econ.AttributeProviderType.Generic);
        container.Set(TF2.Shared.Econ.AttributeValue.OfFloat(
            new TF2.Shared.Econ.AttributeDefinition(_nextAttrDef++, "attr " + _nextAttrDef, hook, format,
                                                    TF2.Shared.Econ.AttributeEffectType.Neutral, false, false,
                                                    TF2.Shared.Econ.AttributeValueType.Default,
                                                    TF2.Shared.Econ.MergedKeys.Block("x")), value));
        return container;
    }

    public EntityHandle NewHandle() => new(_next++);

    /// <summary>Where each entity's centre is, the per-handle answer to GetWorldSpaceCenter.</summary>
    public Dictionary<EntityHandle, Vector> Centers { get; } = new();

    /// <summary>Where each entity stands, the per-handle answer to GetOrigin — a fact opts entities in.</summary>
    public Dictionary<EntityHandle, Vector> Origins { get; } = new();

    /// <summary>The shell's health by handle, as the engine's own writes left it.</summary>
    public Dictionary<EntityHandle, int> Healths { get; } = new();

    /// <summary>Endings a wall stands before: a trace to one of these hits, and hits nothing.</summary>
    public HashSet<Vector> BlockedEnds { get; } = new();

    /// <summary>Each entity's collision box, the per-handle answer to GetBounds — a fact opts entities in.</summary>
    public Dictionary<EntityHandle, (Vector Mins, Vector Maxs)> Bounds { get; } = new();

    /// <summary>Who a sphere sweep finds — and the count it answers, which the strict double demands.</summary>
    public void FillsSphere(params EntityHandle[] found)
    {
        Engine.FillsSphere(found);
        Engine.Returns("WorldQuery.EntitiesInSphere", found.Length);
    }

    /// <summary>
    /// Wire the per-entity answers — centres by handle, traces by ending —
    /// through factories that read the call just recorded, the pusher facts'
    /// pattern. Call it before the subject thinks.
    /// </summary>
    public void StubWorld()
    {
        Engine.ReturnsEachCall("Entities.GetWorldSpaceCenter", () =>
        {
            EntityHandle who = (Engine.Last("Entities.GetWorldSpaceCenter") ?? default).ArgAs<EntityHandle>(0);
            return Centers.GetValueOrDefault(who);
        });
        Engine.ReturnsEachCall("Entities.GetOrigin", () =>
        {
            EntityHandle who = (Engine.Last("Entities.GetOrigin") ?? default).ArgAs<EntityHandle>(0);
            return Origins.GetValueOrDefault(who);
        });
        Engine.ReturnsEachCall("WorldQuery.TraceLine", () =>
        {
            Vector end = (Engine.Last("WorldQuery.TraceLine") ?? default).ArgAs<Vector>(1);
            return BlockedEnds.Contains(end)
                ? new TraceResult(true, 1f, end, Vector.Zero, EntityHandle.None)
                : default;
        });
        // The unknown entity is a standing player — the hull the projectile
        // rig answers, and the shape a box retest reads.
        Engine.ReturnsEachCall("Entities.GetBounds", () =>
        {
            EntityHandle who = (Engine.Last("Entities.GetBounds") ?? default).ArgAs<EntityHandle>(0);
            return Bounds.TryGetValue(who, out (Vector Mins, Vector Maxs) hull)
                ? hull
                : (new Vector(-24f, -24f, 0f), new Vector(24f, 24f, TFViewVectors.Params.HullMax.Z));
        });
    }

    /// <summary>A builder on the team asked, ready to place.</summary>
    public EntityHandle AddBuilder(TFTeam team = TFTeam.Red, TFClass playerClass = TFClass.Engineer)
        => Players.Add(team, playerClass).Handle;

    public T Spawn<T>(string className) where T : ScriptedEntity
    {
        var handle = NewHandle();
        Registry.Spawn(handle, className);
        return (T)Registry.Get(handle)!;
    }

    /// <summary>
    /// One engine-side blow, as the native shell takes it: the refusals of
    /// <c>OnDamaged</c> first, then the shell's subtract and its death call at
    /// zero. Returns what the object accepted.
    /// </summary>
    public static int Hit(TFBaseObject building, EntityHandle attacker, float damage, DamageType type)
    {
        int taken = building.OnDamaged(attacker, damage, type);
        if (taken <= 0)
            return 0;

        building.Health = Math.Max(0, building.Health - taken);
        if (building.Health == 0)
            building.OnKilled(attacker);

        return taken;
    }
}
