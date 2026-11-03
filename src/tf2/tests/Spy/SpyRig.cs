using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Combat;
using TF2.Server.Entities.Ctf;
using TF2.Server.Entities.Rooms;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using TF2.Tests.Combat;
using TF2.Tests.Weapons;

namespace TF2.Tests.Spy;

/// <summary>A clock a fact moves by hand.</summary>
internal sealed class SpyClock : IGameClock
{
    public float Now { get; set; } = 100f;
}

/// <summary>A roll whose class draw a fact chooses (the lastdisguise random).
/// The draw honours the bounds it is given, so a fact can pin the range; keep
/// <see cref="Int"/> off the classes a draw re-rolls (the scout and the spy),
/// or the loop never leaves.</summary>
internal sealed class SpyRandom : IUniformRandom
{
    public int Int { get; set; } = (int)TFClass.Soldier;

    public int RandomInt(int min, int max) => Math.Clamp(Int, min, max);
    public float RandomFloat(float min, float max) => 0.5f;
}

/// <summary>One hit the spy's slice dealt, with the scope and the crit flag open at the time.</summary>
internal sealed record SpyHit(EntityHandle Victim, float Damage, TFDamageCustom Custom, TFWeaponId Weapon, bool Critical, Vector Position);

/// <summary>The respawn-room seam the cloak-consuming disguise asks: which
/// players it says stand in a room, and the centre every ask was made about.</summary>
internal sealed class SpyRooms : ITFRespawnRooms
{
    /// <summary>The entry indexes it answers inside for.</summary>
    public readonly HashSet<int> InRoom = new();

    /// <summary>One entry per ask: the entity asked and the centre it was asked about.</summary>
    public readonly List<(int Target, Vector Origin)> Asks = new();

    public bool PointInRespawnRoom(EntityHandle target, Vector origin, bool touchingSameTeamOnly)
    {
        Asks.Add((target.EntryIndex, origin));
        return InRoom.Contains(target.EntryIndex);
    }

    public bool InRespawnRoom(EntityHandle player, bool sameTeamOnly) => InRoom.Contains(player.EntryIndex);

    public bool PointInRespawnRoom(Vector point) => false;

    public int TouchCount(EntityHandle player) => 0;

    public bool PointsCrossRespawnRoomVisualizer(Vector start, Vector end, int teamToIgnore = 0) => false;
}

/// <summary>The damage op, recording each hit with the TF scope read at the call.</summary>
internal sealed class SpyDamage(TFDamageScopes scopes, DamageHitGroups groups) : IEngineDamage
{
    public List<SpyHit> Hits { get; } = new();

    public void ApplyAt(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType, Vector force, Vector position)
    {
        scopes.TryCurrent(out var custom, out var weapon, out _);
        Hits.Add(new SpyHit(victim, damage, custom, weapon, groups.Critical, position));
    }

    public void Apply(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType)
        => ApplyAt(victim, attacker, damage, damageType, default, default);
    public void SetForceServerRagdoll(EntityHandle entity, bool force) { }
    public TakeDamageMode SetTakeDamageMode(EntityHandle entity, TakeDamageMode mode) => mode;
    public void RegisterRules(DamageRuleMasks masks) { }
    public void TakeDamage(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType, Vector? direction = null,
                           Vector forceOrigin = default, bool physicsOnly = false)
        => ApplyAt(entity, attacker, damage, damageType, default, default);
    // Nothing here ever refuses a hit, so the answered ask lands: 1 is enough
    // for a caller that only gates a follow-up (a blast's push) on damage.
    public int TakeDamageAnswered(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType, Vector? direction = null,
                                  Vector forceOrigin = default, bool physicsOnly = false)
    {
        ApplyAt(entity, attacker, damage, damageType, default, default);
        return damage > 0 ? 1 : 0;
    }
}

/// <summary>
/// One spy on his carrier (entity 3, red, at the origin facing +X) and one
/// enemy (entity 4, blue, 64 units ahead of him): the convars at their
/// defaults, a clock and a roll a fact moves, and the geometry a fact
/// reshapes. The spy slice is attached, so <see cref="Spies"/> routes to it.
/// </summary>
internal sealed class SpyRig
{
    public static readonly EntityHandle Spy = new(0x0001_0003u);
    public static readonly EntityHandle Victim = new(0x0001_0004u);

    /// <summary>The third player's handle (<see cref="AddPlayer"/>'s index 5).</summary>
    public static readonly EntityHandle Other = new(0x0001_0005u);

    /// <summary>The spy's carried weapon entity.</summary>
    public static readonly EntityHandle WeaponEntity = new(0x0003_0001u);

    private readonly TFMovementFixtures.Players _states = new();

    public RecordingEngine Engine { get; } = TestServices.Fake();
    public SpyClock Clock { get; } = new();
    public SpyRandom Random { get; } = new();
    public DamageHitGroups HitGroups { get; } = new();
    public TFDamageScopes Scopes { get; } = new();
    public SpyDamage Damage { get; }
    public ArmedConditions Conditions { get; }
    public ConVarRegistry ConVars { get; }
    public RecordingLog Log { get; } = new();
    public ItemSchema Schema { get; } = EconTestFiles.TrimmedSchema();
    public TFClassTable Classes { get; }
    public WeaponCatalog Catalog { get; }

    /// <summary>The entity world the disguise's dress reads: the spy's carried weapon in it.</summary>
    public RigArmory Armory { get; }

    /// <summary>The disguise's dress, the service the spy's think tails.</summary>
    public TFDisguiseWeapons DisguiseWeapons { get; }

    public TFSpy Spies { get; }

    /// <summary>The attached spy slice.</summary>
    public TFSpyComponent Slice { get; }

    /// <summary>The spy's carrier words.</summary>
    public TFPlayerNetState State => _states.States[Spy.EntryIndex];

    /// <summary>The carrier states, as the class-skill service reads them.</summary>
    public ITFPlayerData States => _states;

    /// <summary>Where the victim stands; the swing's line ends there too.</summary>
    public Vector VictimCenter { get; set; } = new(64f, 0f, 64f);

    /// <summary>The eye angles (yaw 0 faces +X). The victim faces away by default.</summary>
    public Vector VictimAngles { get; set; } = default;

    /// <summary>Which team the traced victim answers as.</summary>
    public int VictimTeam { get; set; } = (int)TFTeam.Blue;

    /// <summary>Whether the swing's line names the victim.</summary>
    public bool TraceHits { get; set; } = true;

    /// <summary>Whether the spy answers alive (CanDisguise's gate).</summary>
    public bool SpyAlive { get; set; } = true;

    /// <summary>Whether the traced victim answers alive.</summary>
    public bool VictimAlive { get; set; } = true;

    /// <summary>Teams the rig's extra players (beyond the spy and the victim) answer as, by index.</summary>
    private readonly Dictionary<int, int> _extraTeams = new();

    /// <summary>A third player on <paramref name="index"/>, of <paramref name="playerClass"/>, answering <paramref name="team"/>.</summary>
    public TFPlayerNetState AddPlayer(int index, TFClass playerClass, int team)
    {
        _extraTeams[index] = team;
        return _states.Add(index, playerClass);
    }

    private int TeamOf(EntityHandle player)
        => player == Victim ? VictimTeam
         : player == Spy ? (int)TFTeam.Red
         : _extraTeams.TryGetValue(player.EntryIndex, out int team) ? team
         : (int)TFTeam.Red;

    /// <summary>Where the swing's line stops — a point on the victim, not
    /// necessarily his centre.</summary>
    public Vector TraceEnd { get; set; } = new(60f, 2f, 63f);

    /// <summary>The team-win holder the cloak's CanGoInvisible reads.</summary>
    public TFRoundWinners Winners { get; } = new();

    /// <summary>The respawn-room seam the cloak's disguise drain asks.</summary>
    public SpyRooms Rooms { get; } = new();

    /// <summary>The attribute hooks the cloak's disguise drain asks.</summary>
    public AttributeHooks Hooks { get; } = new();

    /// <summary>The item owners whose per-player managers the hook reads.</summary>
    public FakeOwners Owners { get; } = new();

    /// <summary>The engine's active-weapon answer, for the dress's restore.</summary>
    public string ActiveWeapon { set => Engine.Returns("Players.GetActiveWeapon", value); }

    /// <summary>The carried weapon's deploy, as <c>Arsenal.Deploy</c> is: the stamp, its own dress aside —
    /// the dress under test is the disguise's.</summary>
    public void Deploy() => Armory.Deploy();

    public SpyRig(TFClass playerClass = TFClass.Spy, ICtfRules? ctf = null, ILagCompensation? lag = null,
                  bool withWeapon = true)
    {
        Damage = new SpyDamage(Scopes, HitGroups);
        _states.Add(Spy.EntryIndex, playerClass);
        _states.Add(Victim.EntryIndex, TFClass.HeavyWeapons);
        Conditions = new ArmedConditions(_states);

        Engine.Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
              .ReturnsEachCall("Entities.GetEyeAngles", () => Arg() is EntityHandle h && h == Victim ? VictimAngles : default(Vector))
              .ReturnsEachCall("Entities.GetWorldSpaceCenter", () => Arg() is EntityHandle h && h == Victim ? VictimCenter : new Vector(0f, 0f, 64f))
              .ReturnsEachCall("Entities.IsPlayer", () => Arg() is EntityHandle h && h == Victim)
              .ReturnsEachCall("Players.IsAlive", () => Arg() is EntityHandle h && h == Victim ? VictimAlive : SpyAlive)
              .ReturnsEachCall("Players.GetTeam", () => Arg() is EntityHandle h ? TeamOf(h) : (int)TFTeam.Red)
              .ReturnsEachCall("Players.GetHandle", () => Arg() is int i && i == Victim.EntryIndex ? Victim
                                                     : Arg() is int j && j == Spy.EntryIndex ? Spy
                                                     : Arg() is int k && k == Other.EntryIndex ? Other
                                                     : EntityHandle.None)
              .Returns("Entities.GetHealth", 100)
              .ReturnsEachCall("WorldQuery.TraceLine", () => new TraceResult(TraceHits, 1f, TraceEnd, default, Victim));

        ConVars = ConVarRegistry.For(TestServices.UnattachedConsole());
        ConVars.RegisterAll(typeof(TFSpy).Assembly);

        // The drain's tick: the rig's seconds, one per Tick the facts call.
        nint globals = Marshal.AllocHGlobal(Marshal.SizeOf<CGlobalVarsRaw>());
        Marshal.WriteInt32(globals, (int)Marshal.OffsetOf<CGlobalVarsRaw>(nameof(CGlobalVarsRaw.interval_per_tick)),
                           BitConverter.SingleToInt32Bits(1f));
        Engine.Returns("Environment.Globals", new Globals(globals));

        Classes = new TFClassTable(new RecordingLog(), WeaponFixtures.ShippedWithClasses());
        Catalog = new WeaponCatalog(() => [new TFWeaponTypes(
            new TFWeaponScripts(Log, WeaponFixtures.ShippedWithClasses()), Classes, Schema)], Log);

        Armory = new RigArmory();
        if (withWeapon)
            Armory.Give(WeaponEntity, Spy.EntryIndex);
        DisguiseWeapons = new TFDisguiseWeapons(Engine, Armory, Engine, Schema, Classes, Catalog);

        Spies = new TFSpy(_states, Conditions, Engine, Engine, Engine, Damage, HitGroups, Scopes, Random, Classes,
                          Clock, Engine,
                          ConVars.Get<TFSpyCloakConsumeRateConVar>()!, ConVars.Get<TFSpyCloakRegenRateConVar>()!,
                          ConVars.Get<TFSpyInvisTimeConVar>()!, ConVars.Get<TFSpyInvisUnstealthTimeConVar>()!,
                          ConVars.Get<TFSpyCloakNoAttackTimeConVar>()!,
                          Winners, Rooms, Hooks, Owners, Engine, DisguiseWeapons, ctf, lag);
        Conditions.Spies = Spies;   // the rig's chain: removals run the disguise arms
        var component = new TFSpyComponent(_states, Conditions, Engine, Engine, Engine, Damage, HitGroups, Scopes, Random,
                                           Classes, Spies,
                                           Winners, Rooms, Hooks, Owners, Engine, DisguiseWeapons, ctf, lag);
        component.Attach(Spy);
        Slice = component;
    }

    private object? Arg() => Engine.Calls[^1].Args.Length > 0 ? Engine.Calls[^1].Args[0] : null;
}

/// <summary>
/// The rig's condition writer as production drives it: the real
/// <see cref="RigConditions"/> set on the carrier, with the
/// <see cref="ITFConditionHook"/> arms fired around it — the removal arms
/// (<c>OnRemoveDisguising</c>/<c>OnRemoveDisguised</c>) run on every removal
/// and, through <see cref="RemoveAll"/>, on every condition death takes off
/// (stock's <c>RemoveAllCond</c> removes condition by condition,
/// tf_player_shared.cpp:1545-1556, running <c>OnConditionRemoved</c> with each,
/// :1182). The shared <see cref="TFConditionArms"/>
/// ride as well, so an arm's cascade (the burning set a resupply ends) lands.
/// </summary>
internal sealed class ArmedConditions(TFMovementFixtures.Players players) : ITFPlayerConditions
{
    private readonly RigConditions inner = new(players);
    private readonly TFConditionArms sharedArms = new();

    /// <summary>The spy service whose disguise arms the chain carries; set once the rig has built it.</summary>
    public TFSpy? Spies { get; set; }

    public TFConditions? Of(int playerIndex) => inner.Of(playerIndex);

    public TFCondAdd Add(EntityHandle player, TFCond cond, float duration = TFConditions.Permanent, EntityHandle provider = default)
    {
        var added = inner.Add(player, cond, duration, provider);
        if (added is TFCondAdd.Added or TFCondAdd.Refreshed)
            Added(player, cond);
        return added;
    }

    public bool Remove(EntityHandle player, TFCond cond, bool ignoreDuration = false)
    {
        if (inner.Of(player.EntryIndex) is not { } conditions || !conditions.Remove(cond, ignoreDuration))
            return false;
        Ended(player, conditions, cond);
        return true;
    }

    public TFCondMask RemoveAll(EntityHandle player)
    {
        if (inner.Of(player.EntryIndex) is not { } conditions)
            return default;
        var removed = conditions.RemoveAll();
        foreach (TFCond cond in removed)
            Removed(player, cond);
        return removed;
    }

    public bool InCond(EntityHandle player, TFCond cond) => inner.InCond(player, cond);

    // The real chain's shape (TFPlayerConditions.Ended): the shared arms first,
    // then the hook, then the conditions the arms themselves ended.
    private void Added(EntityHandle player, TFCond cond)
    {
        if (inner.Of(player.EntryIndex) is not { } conditions)
            return;
        var ended = sharedArms.OnAdded(conditions, cond);
        Spies?.OnAdded(player, cond);
        Ended(player, conditions, ended);
    }

    private void Ended(EntityHandle player, TFConditions conditions, TFCond cond)
    {
        var ended = sharedArms.OnRemoved(conditions, cond);
        Spies?.OnRemoved(player, cond);
        Ended(player, conditions, ended);
    }

    private void Ended(EntityHandle player, TFConditions conditions, in TFCondMask conds)
    {
        foreach (TFCond cond in conds)
            Ended(player, conditions, cond);
    }

    private void Removed(EntityHandle player, TFCond cond)
        => Spies?.OnRemoved(player, cond);
}

/// <summary>
/// The entity directory the dress reads: the one <c>ss_weapon</c> the core
/// gives each player, found by owner as production's walk is. The handle is
/// set through the friend grant, as the compositor's registration does.
/// </summary>
internal sealed class RigArmory : IEntityDirectory
{
    private readonly Dictionary<EntityHandle, SsWeapon> _byHandle = new();

    /// <summary>The spy's carried weapon, when the rig gave one.</summary>
    public SsWeapon? Held { get; private set; }

    public SsWeapon Give(EntityHandle handle, int ownerIndex)
    {
        var weapon = new SsWeapon();
        weapon.Handle = handle;
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = ownerIndex;
        _byHandle[handle] = weapon;
        Held = weapon;
        return weapon;
    }

    /// <summary>A deploy's stamp (<c>Arsenal.Deploy</c>'s epoch write; the
    /// arsenal's own dress of the carried weapon it accompanies, not this
    /// dress's business).</summary>
    public void Deploy()
    {
        if (Held is { } held)
            held.State.DeployEpoch++;
    }

    ScriptedEntity? IEntityDirectory.Get(EntityHandle h) => _byHandle.GetValueOrDefault(h);

    public ScriptedEntity? FindByName(string name) => null;

    public IEnumerable<ScriptedEntity> OfClass(string className)
        => className == EntityNames.SsWeapon ? _byHandle.Values : [];

    public bool IsRegistered(string className) => className == EntityNames.SsWeapon;

    public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
}
