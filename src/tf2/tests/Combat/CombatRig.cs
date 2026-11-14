using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server.Combat;
using TF2.Server.Medigun;
using TF2.Server.Player;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Tests.Entities.Items;
using TF2.Tests.Weapons;

namespace TF2.Tests.Combat;

internal sealed class CombatClock : IGameClock
{
    public float Now { get; set; } = 100f;
}

/// <summary>A roll that answers a fixed point of the band asked (0 the floor, 1 the ceiling) and counts the asks.</summary>
internal sealed class BandRandom(float at = 0.5f) : IUniformRandom
{
    public List<(float Min, float Max)> Asked { get; } = new();
    public float RandomFloat(float min, float max) { Asked.Add((min, max)); return min + (max - min) * at; }
    public int RandomInt(int min, int max) => min;
}

internal sealed class NoEntities : IEntityDirectory
{
    public ScriptedEntity? Get(EntityHandle h) => null;
    public ScriptedEntity? FindByName(string name) => null;
    public IEnumerable<ScriptedEntity> OfClass(string className) => [];
    public bool IsRegistered(string className) => true;
    public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
}

/// <summary>
/// The directory the damage layer reads: answers only the rig's weapon entity
/// handle, so a fact can put a <see cref="SsWeapon"/> state where the layer's
/// charge arms look (everything else is still nothing).
/// </summary>
internal sealed class RigEntities : IEntityDirectory
{
    public static readonly EntityHandle Weapon = new(0x0001_000Au);

    public SsWeapon? WeaponEntity;

    /// <summary>Wearable entities a fact placed, by handle (the burn's immune arm walks them).</summary>
    public readonly Dictionary<EntityHandle, TFWearableEntity> Wearables = new();

    /// <summary>Other entities a fact placed, by handle — the sentry the layer's note asks for.</summary>
    public readonly Dictionary<EntityHandle, ScriptedEntity> Placed = new();

    public ScriptedEntity? Get(EntityHandle h)
        => h == Weapon ? WeaponEntity
        : Wearables.TryGetValue(h, out var wearable) ? wearable
        : Placed.TryGetValue(h, out var placed) ? placed
        : null;

    public ScriptedEntity? FindByName(string name) => null;
    public IEnumerable<ScriptedEntity> OfClass(string className)
        => Placed.Values.Where(e => e.ClassName == className);
    public bool IsRegistered(string className) => true;
    public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
}

/// <summary>
/// The core's damage op, recording each hit with the TF scope open at the
/// time, and — when the rig hands it one — the hitgroups scope's crit flag,
/// sampled while the damage call is still synchronous inside the push.
/// </summary>
internal sealed class ScopedDamage(TFDamageScopes scopes, DamageHitGroups? groups = null) : IEngineDamage
{
    public List<(EntityHandle Victim, EntityHandle Attacker, float Damage, DamageType Type, TFDamageCustom Custom, TFWeaponId Weapon, bool Critical)> Hits { get; } = new();

    public void Apply(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType)
    {
        scopes.TryCurrent(out var custom, out var weapon, out _);
        Hits.Add((victim, attacker, damage, damageType, custom, weapon, groups?.Critical ?? false));
    }

    public void ApplyAt(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType, Vector force, Vector position)
        => Apply(victim, attacker, damage, damageType);
    public void SetForceServerRagdoll(EntityHandle entity, bool force) { }
    public TakeDamageMode SetTakeDamageMode(EntityHandle entity, TakeDamageMode mode) => mode;
    public void RegisterRules(DamageRuleMasks masks) { }
    public void TakeDamage(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType, Vector? direction = null,
                           Vector forceOrigin = default, bool physicsOnly = false) => Apply(entity, attacker, damage, damageType);
    // Nothing here ever refuses a hit, so the answered ask lands: 1 is enough
    // for a caller that only gates a follow-up (a blast's push) on damage.
    public int TakeDamageAnswered(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType, Vector? direction = null,
                                  Vector forceOrigin = default, bool physicsOnly = false)
    {
        Apply(entity, attacker, damage, damageType);
        return damage > 0 ? 1 : 0;
    }
}

/// <summary>
/// The damage layer over doubles: two TF players on their carriers — a
/// <see cref="Victim"/> (index 3, red, at the origin) and an
/// <see cref="Attacker"/> (index 4, blue, at <see cref="AttackerAt"/>) — the
/// shipped weapon and class scripts, a fixed roll and the convars at their
/// defaults.
/// </summary>
/// <summary>
/// The player's condition writer (2.3's <see cref="ITFPlayerConditions"/>) as
/// the rig's players have it: one real authoritative <see cref="TFConditions"/>
/// per player, kept on that player's carrier words, so a fact can run the
/// countdown the player's ticker runs every frame.
/// </summary>
internal sealed class RigConditions(TFMovementFixtures.Players players) : ITFPlayerConditions
{
    private readonly Dictionary<int, TFConditions> _sets = [];

    public TFConditions? Of(int playerIndex)
    {
        if (_sets.TryGetValue(playerIndex, out var set))
            return set;
        if (players.Predicted(playerIndex) is not { } state)
            return null;
        return _sets[playerIndex] = new TFConditions(authoritative: true, new TFCarrierCondWords(state));
    }

    public TFCondAdd Add(EntityHandle player, TFCond cond, float duration = TFConditions.Permanent, EntityHandle provider = default)
        => Of(player.EntryIndex)?.Add(cond, duration, provider) ?? TFCondAdd.Refused;

    public bool Remove(EntityHandle player, TFCond cond, bool ignoreDuration = false)
        => Of(player.EntryIndex)?.Remove(cond, ignoreDuration) ?? false;

    public TFCondMask RemoveAll(EntityHandle player) => Of(player.EntryIndex)?.RemoveAll() ?? default;

    public bool InCond(EntityHandle player, TFCond cond) => Of(player.EntryIndex)?.Has(cond) ?? false;
}

/// <summary>
/// The resupply the spun-up gate's max health comes from; every other member
/// waits for a fact that needs it.
/// </summary>
internal sealed class RigResupply : ITFPlayerResupply
{
    public int MaxHealthAnswer;

    /// <summary>The player's health the heals land on; facts set the start.</summary>
    public int HealthAnswer;

    /// <summary>The heal asks, as "<c>take index amount ignoreMaxHealth</c>".</summary>
    public readonly List<string> Takes = [];
    /// <summary>Every TakeHealth, as (who, how much) — the on-hit heals read these.</summary>
    public readonly List<(EntityHandle Who, float Amount)> HealthTaken = [];

    public int MaxHealth(EntityHandle player) => MaxHealthAnswer;

    /// <summary>Adds health up to the answered max, as the real seam caps at its
    /// item-inclusive max health (the class's plus <c>add_maxhealth</c>).</summary>
    public int TakeHealth(EntityHandle player, float health, bool ignoreMaxHealth = false)
    {
        int healed = ignoreMaxHealth
                         ? (int)health
                         : Math.Min((int)health, Math.Max(0, MaxHealthAnswer - HealthAnswer));
        HealthAnswer += healed;
        Takes.Add($"take {player.EntryIndex} {(int)health} {ignoreMaxHealth}");
        HealthTaken.Add((player, health));
        return healed;
    }

    public void Regenerate(EntityHandle player, bool refillHealthAndAmmo = true) => throw new InvalidOperationException();
    public int MaxAmmo(EntityHandle player, TFAmmo ammo) => throw new InvalidOperationException();
    public int GiveAmmo(EntityHandle player, int count, TFAmmo ammo) => throw new InvalidOperationException();
}

/// <summary>
/// The medigun meter as the layer's charge arm uses it: the setter clamps to the
/// meter's bounds as <c>AddCharge</c> does, and records the ask so a fact can
/// read back exactly what the hit put on. The beam arms have no callers here.
/// </summary>
internal sealed class RigMediguns : ITFMedigunWeapons
{
    private readonly Dictionary<int, float> _levels = [];

    /// <summary>The set asks, as "<c>charge index level</c>".</summary>
    public readonly List<string> Sets = [];

    public float ChargeLevel(int ownerIndex) => _levels.TryGetValue(ownerIndex, out var level) ? level : 0f;

    public void SetChargeLevel(int ownerIndex, float level)
    {
        float clamped = Math.Clamp(level, 0f, 1f);      // stock's AddCharge cap (tf_weapon_medigun.cpp:1061)
        _levels[ownerIndex] = clamped;
        Sets.Add($"charge {ownerIndex} {clamped}");
    }

    public void TickMedic(int ownerIndex, float now, float frametime) { }
    public void Holster(int ownerIndex, float now) { }
    public void Unholster(int ownerIndex, float now) { }
    public void RemoveMedigun(int ownerIndex, float now) { }
    public int HealingTargetIndex(int ownerIndex) => -1;
    public bool IsReleasing(int ownerIndex) => false;

    public void PreserveCharge(int ownerIndex, float amount)
        => Sets.Add($"preserve {ownerIndex} {amount}");
}

internal sealed class CombatRig
{
    /// <summary>The healing as the afterburn slice sees it: the extinguish call is recorded.</summary>
    internal sealed class HealingSpy : ITFPlayerHealing
    {
        public readonly List<EntityHandle> Patients = [];

        /// <summary>The item-inclusive buffed max the kill heal's headroom is measured against.</summary>
        public int MaxBuffedAnswer = 125;

        public void Extinguished(EntityHandle patient) => Patients.Add(patient);

        // The slice calls nothing else on the healing.
        public void Heal(EntityHandle patient, EntityHandle healer, float healRate, float now,
                         float overhealBonus = ITFPlayerHealing.StandardOverhealBonus,
                         float decayMult = ITFPlayerHealing.StandardOverhealDecayMult,
                         bool dispenserHeal = false, int scorer = -1)
            => throw new InvalidOperationException();
        public float StopHealing(EntityHandle patient, EntityHandle healer, float now) => throw new InvalidOperationException();
        public int NumHealers(int patientIndex) => throw new InvalidOperationException();

        /// <summary>Who heals whom, by patient index — the connected-damage arm's graph.</summary>
        public readonly Dictionary<int, int[]> Healers = new();

        public IEnumerable<int> HealerIndexes(int patientIndex)
            => Healers.TryGetValue(patientIndex, out var healers) ? healers : [];
        public void RecalculateChargeEffects(EntityHandle player, float now) => throw new InvalidOperationException();
        public void NoteReleasing(int playerIndex, bool releasing, int chargeType = 0) => throw new InvalidOperationException();
        public int MaxBuffedHealth(EntityHandle patient, bool ignoreHealthOverMax = false) => MaxBuffedAnswer;
        public bool CanReceiveMedigunChargeEffect(EntityHandle player) => throw new InvalidOperationException();
        public void RemoveChargeEffect(EntityHandle player) => throw new InvalidOperationException();
        public void TestAndExpireChargeEffect(EntityHandle player, float now) => throw new InvalidOperationException();
        public void TickPatient(EntityHandle patient, float now, float frametime) => throw new InvalidOperationException();
    }

    public static readonly EntityHandle Victim = new(0x0001_0003u);
    public static readonly EntityHandle Attacker = new(0x0001_0004u);
    public static readonly EntityHandle Third = new(0x0001_0005u);

    public RecordingEngine Engine { get; } = TestServices.Fake();
    public TFMovementFixtures.Players Players { get; } = new();
    public CombatClock Clock { get; } = new();

    /// <summary>The level word; a fact sets <see cref="LevelInfo.Map"/> for the map-named arms.</summary>
    public LevelInfo Level { get; } = new();
    public BandRandom Random { get; }
    public TFDamageRecords Records { get; } = new();
    public TFDamageScopes Scopes { get; } = new();
    public DamageInflictors Inflictors { get; } = new();
    public TFSentryDamages SentryDamages { get; } = new();
    public ScopedDamage Damage { get; }
    public TFAfterburns Afterburns { get; }
    public HealingSpy Healing { get; } = new();
    public TFBleeds Bleeds { get; }
    public TFZoomTimes ZoomTimes { get; } = new();
    public TFPlayerStats Stats { get; } = new();
    public RigEntities Entities { get; private set; } = null!;
    public RigConditions Conditions { get; }
    public ConVarRegistry ConVars { get; }
    public TFDamageLayer Layer { get; }
    public TFDamageRules Rules { get; }
    public RecordingLog Log { get; } = new();

    /// <summary>The per-player parts the burn's immune arm walks the wearables from.</summary>
    public TFPlayerCarriers Carriers { get; } = new();

    /// <summary>The loadout the layer's attribute arms read; empty until a fact sets one.</summary>
    public FakeOwners Owners { get; } = new();

    /// <summary>The resupply the spun-up gate's max health is read from.</summary>
    public RigResupply Resupply { get; } = new();

    /// <summary>The meter the medic's on-hit charge arm lands on.</summary>
    public RigMediguns Mediguns { get; } = new();

    /// <summary>The effects the layer's tempent arms dispatch, the sink the facts read.</summary>
    public RecordingEffectDispatch Temps { get; } = new();

    /// <summary>The players the stun stack's condition asks read.</summary>
    public readonly FakeMapPlayers StunPlayers = new();

    /// <summary>
    /// The player's stun stack, attached: a fake map player under the handle
    /// answers the condition words the stack asks, as the projectile rig's
    /// attach does; the stack then answers the hit's stun.
    /// </summary>
    public TFStunStack AttachStun(EntityHandle player)
    {
        StunPlayers.All[player] = new FakeMapPlayer();
        var stack = new TFStunStack(Carriers, StunPlayers, Clock);
        stack.Attach(player);
        return stack;
    }

    private int _nextDef = 9000;

    /// <summary>Puts one float attribute on the attacker's item, as the loadout handed it out.</summary>
    public IAttributeProvider AttackerItem(string hook, AttributeDescriptionFormat format, float value)
        => ItemOn(CombatRig.Attacker, hook, format, value);

    /// <summary>The same, on whoever the fact names: the sentry-mark arm reads
    /// the builder's held item, not the constant attacker's.</summary>
    public IAttributeProvider ItemOn(EntityHandle who, string hook, AttributeDescriptionFormat format, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(_nextDef++, "attr " + _nextDef, hook, format,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        Owners.Items[who.EntryIndex] = container;
        return container;
    }

    public Vector AttackerAt { get; set; } = new(512f, 0f, 0f);

    /// <summary>The victim's eye angles, pitch yaw roll — level along +X unless a fact turns.</summary>
    public Vector VictimEye { get; set; } = new(0f, 0f, 0f);

    public string AttackerWeapon { get; set; } = "tf_weapon_pistol";

    /// <summary>The victim's active weapon row (<c>GetActiveTFWeapon</c>), for the burn's immune arm.</summary>
    public string VictimWeapon { get; set; } = "tf_weapon_pistol";

    public int AttackerTeam { get; set; } = (int)TFTeam.Blue;

    public TFPlayerNetState VictimState { get; }
    public TFPlayerNetState AttackerState { get; }

    public CombatRig(float roll = 0.5f)
    {
        Random = new BandRandom(roll);
        Damage = new ScopedDamage(Scopes);
        Conditions = new RigConditions(Players);
        VictimState = Players.Add(Victim.EntryIndex, TFClass.Scout);
        AttackerState = Players.Add(Attacker.EntryIndex, TFClass.Sniper);

        Engine.Returns("Entities.IsValid", true)
              .ReturnsEachCall("Entities.IsPlayer", () => Arg() is EntityHandle h && (h == Victim || h == Attacker || h == Third))
              .ReturnsEachCall("Entities.GetWorldSpaceCenter", () => Arg() is EntityHandle h && h == Attacker ? AttackerAt : default(Vector))
              .ReturnsEachCall("Entities.GetOrigin", () => Arg() is EntityHandle h && h == Attacker ? AttackerAt : default(Vector))
              .ReturnsEachCall("Entities.GetEyeAngles", () => Arg() is EntityHandle h && h == Victim ? VictimEye : default(Vector))
              .ReturnsEachCall("Entities.GetIndex", () => Arg() is EntityHandle h ? h.EntryIndex : -1)
              .ReturnsEachCall("Players.GetActiveWeapon", () => Arg() is EntityHandle h && h == Victim ? VictimWeapon : AttackerWeapon)
              .ReturnsEachCall("Players.GetTeam", () => Arg() is EntityHandle h && h == Victim ? (int)TFTeam.Red : AttackerTeam)
              .ReturnsEachCall("Players.GetUserId", () => Arg() is EntityHandle h ? 100 + h.EntryIndex : 0)
              .Returns("Players.IsAlive", true);

        ConVars = ConVarRegistry.For(TestEngines.Server().Engine.Require<IEngineConsole>());
        ConVars.RegisterAll(typeof(TFDamageLayer).Assembly);

        var files = WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses());
        Entities = new RigEntities();
        Engine.Returns("Players.GetActiveWeaponEntity", RigEntities.Weapon);
        Afterburns = new TFAfterburns(Players, Conditions, Engine, Engine, Damage, Entities, Scopes, Healing,
                                      Owners, new TF2.Shared.Econ.AttributeHooks(), Carriers);
        Bleeds = new TFBleeds(Players, Conditions, Engine, Engine, Damage, Scopes);
        Layer = new TFDamageLayer(Players, Engine, Engine, Entities, new TFWeaponScripts(Log, files), new TFClassTable(Log, files),
                                  Owners, Resupply, new TF2.Shared.Econ.AttributeHooks(), Carriers,
                                  Random, Clock, Records, Scopes, Afterburns, Bleeds,
                                  ConVars.Declared<TFDamageRangeConVar>(), ConVars.Declared<TFDamageDisableSpreadConVar>(),
                                  ConVars.Declared<TFWeaponCriticalsDistanceFalloffConVar>(),
                                  ConVars.Declared<TFWeaponMinicritsDistanceFalloffConVar>(),
                                  ConVars.Declared<TFFallDamageDisableSpreadConVar>(), ConVars.Declared<TFDebugDamageConVar>(), ZoomTimes, Log,
                                  Stats, Inflictors, SentryDamages, ConVars.Declared<TFStealthDamageReductionConVar>(),
                                  Conditions, Engine, Engine, Mediguns, Damage, Healing, Temps, Engine, Engine);
        Rules = new TFDamageRules(Players, Engine, Scopes);
    }

    private object? Arg() => Engine.Calls[^1].Args.Length > 0 ? Engine.Calls[^1].Args[0] : null;

    /// <summary>A hit from the attacker on the victim, through the layer.</summary>
    public float Hit(float damage, DamageType bits, HitGroup group = HitGroup.Generic, bool critical = false,
                     EntityHandle? attacker = null, EntityHandle? victim = null)
        => Layer.Modify(new DamageContext(victim ?? Victim, attacker ?? Attacker, damage, (int)bits, group, critical), damage);

    public void Set<T>(string value) where T : ConVarBase => ConVars.Declared<T>().Bind(-1, ConVars.Declared<T>().Name, value);
}
