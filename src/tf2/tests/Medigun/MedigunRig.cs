using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Server.Combat;
using TF2.Server.Entities.Ctf;
using TF2.Server.Entities.Rooms;
using TF2.Server.Medigun;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Tests.Weapons;
using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests.Medigun;

/// <summary>
/// The medigun lane's fixture: a <see cref="TFPlayerHealing"/> (and later the
/// weapon service) over a <see cref="RecordingEngine"/> and hand-rolled fakes
/// for the condition, resupply, stats and event seams, with the lane's convars
/// as registered. Health lives in the rig: <c>GetHealth</c> reads it,
/// <c>SetHealth</c> (the drain's direct write) writes it.
/// </summary>
internal sealed class MedigunRig
{
    public const uint HandleBase = 0x0001_0000u;

    public static EntityHandle HandleFor(int index) => new(HandleBase | (uint)index);

    public readonly RecordingEngine Engine = TestServices.Fake();
    public readonly ConVarRegistry ConVars;
    public readonly TFHealingConditions Conditions = new();
    public readonly TFHealingResupply Resupply = new();
    public readonly TFHealingStats Stats = new();
    public readonly TFHealingEvents Events = new();
    public readonly TFHealingCtf Ctf = new();
    public readonly TFHealingClients Clients = new();
    public readonly TFDamageRecords Records = new();
    public readonly TFHealingPlayerData Data = new();
    public readonly TFPlayerHealing Healing;

    /// <summary>The respawn-room seam the charge arm asks: <see cref="MedigunRooms.InRoom">InRoom</see> names who stands in one.</summary>
    public readonly MedigunRooms Rooms = new();
    public readonly AttributeHooks Hooks = new();
    public readonly FakeOwners Owners = new();
    public readonly FakeRoundSetup Setup = new();

    /// <summary>Players this rig says are dead; everyone the rig <see cref="Give"/>s is alive by default.</summary>
    public readonly HashSet<int> Dead = new();
    /// <summary>Player teams by entry index; unlisted players are on team 0 (and so on the medic's team).</summary>
    public readonly Dictionary<int, int> Teams = new();
    /// <summary>Eye positions and world centers by entry index; unlisted players sit at the origin.</summary>
    public readonly Dictionary<int, Vector> Eyes = new();
    public readonly Dictionary<int, Vector> Centers = new();
    /// <summary>The traces the world answers, in order; an exhausted queue answers a miss.</summary>
    public readonly List<TraceResult> TraceQueue = new();

    private readonly Dictionary<int, int> healths = new();

    public MedigunRig()
    {
        ConVars = ConVarRegistry.For(TestEngines.Server().Engine.Require<IEngineConsole>());
        ConVars.RegisterAll(typeof(TFPlayerHealing).Assembly);
        Engine.ReturnsEachCall("Players.GetHandle", () => HandleFor(A<int>(0)));
        Engine.ReturnsEachCall("Players.GetUserId", () => 100 + A<EntityHandle>(0).EntryIndex);
        Engine.ReturnsEachCall("Players.IsAlive", () => !Dead.Contains(A<EntityHandle>(0).EntryIndex));
        Engine.ReturnsEachCall("Players.GetTeam", () => Teams.TryGetValue(A<EntityHandle>(0).EntryIndex, out var t) ? t : 0);
        Engine.ReturnsEachCall("Entities.GetHealth", () => healths[A<EntityHandle>(0).EntryIndex]);
        Engine.ReturnsEachCall("Entities.IsPlayer", () => healths.ContainsKey(A<EntityHandle>(0).EntryIndex));
        Engine.ReturnsEachCall("Entities.GetEyePosition", () => Eyes.TryGetValue(A<EntityHandle>(0).EntryIndex, out var v) ? v : Vector.Zero);
        Engine.Returns("Entities.GetEyeAngles", Vector.Zero);
        Engine.ReturnsEachCall("Entities.GetWorldSpaceCenter", () => Centers.TryGetValue(A<EntityHandle>(0).EntryIndex, out var v) ? v : Vector.Zero);
        Engine.ReturnsEachCall("WorldQuery.TraceLine", () => NextTrace());
        Engine.Returns("Players.GetActiveWeapon", "tf_weapon_medigun");
        Engine.WhenCalled("Entities.SetHealth", c => healths[c.ArgAs<EntityHandle>(0).EntryIndex] = c.ArgAs<int>(1));
        Healing = new TFPlayerHealing(Engine, Engine, Conditions, Resupply,
                                      ConVars.Declared<TFMaxHealthBoostConVar>(), ConVars.Declared<TFBoostDrainTimeConVar>(),
                                      ConVars.Declared<TFInvulnTimeConVar>(), new FakeClock(),
                                      Stats, Records, Events, Ctf, Data, Owners);
    }

    /// <summary>A medigun over this rig's healing, as the composition builds it: the weapon
    /// reports its release to the healing, which holds the answer the recalc reads.</summary>
    public TFMedigunWeapon NewWeapon(ILagCompensation? lag = null)
        => new(Engine, Engine, Engine, Healing, Conditions, Resupply, ConVars.Declared<TFInvulnTimeConVar>(),
               ConVars.Declared<WeaponMedigunChargeRateConVar>(), Rooms, Hooks, Owners, Stats, Events, Clients, Data, Setup,
               ConVars.Declared<TFMedigunLagCompConVar>(), lag);

    public void QueueHit(EntityHandle hit) => TraceQueue.Add(new TraceResult(true, 1f, default, default, hit));
    public void QueueMiss() => TraceQueue.Add(new TraceResult(false, 1f, default, default, default));
    public void QueueBlocked(Vector end, EntityHandle blocker) => TraceQueue.Add(new TraceResult(true, 0.5f, end, default, blocker));

    private TraceResult NextTrace()
    {
        if (TraceQueue.Count == 0)
            return new TraceResult(false, 1f, default, default, default);
        var t = TraceQueue[0];
        TraceQueue.RemoveAt(0);
        return t;
    }

    /// <summary>The player exists with this health and class max.</summary>
    public void Give(int index, int health, int max)
    {
        healths[index] = health;
        Resupply.Maxes[index] = max;
    }

    /// <summary>The health as the engine fake holds it (TakeHealth does not move it; the drain does).</summary>
    public int Health(int index) => healths[index];

    /// <summary>Stock's GetLastDamageReceivedTime half: a hit this long ago (none by default).</summary>
    public void NoteDamage(int victim, float time)
        => Records.Note(victim, new TFHitRecord(HandleFor(9), 10f, (DamageType)0, (TFCritType)0,
                                                (TFDamageCustom)0, (TFWeaponId)0, "tf_weapon_rocketlauncher", time), damager: true);

    public void Set<T>(string value) where T : ConVarBase => ConVars.Declared<T>().Bind(-1, ConVars.Declared<T>().Name, value);

    /// <summary>The last call recorded to the engine — the fake's argument reader, the CombatRig pattern.</summary>
    private object?[] Args() => Engine.Calls[^1].Args;
    private T A<T>(int i) => Args()[i] is T v ? v : throw new Xunit.Sdk.XunitException($"engine call {Engine.Calls[^1].Member} arg {i} is not {typeof(T).Name}");
}

/// <summary>The player-data seam: carrier states the facts hand out, by index.</summary>
internal sealed class TFHealingPlayerData : ITFPlayerData
{
    public readonly Dictionary<int, TFPlayerNetState> States = new();

    public TFPlayerNetState? Predicted(int playerIndex) => States.TryGetValue(playerIndex, out var s) ? s : null;
    public TFPlayerNetState? Networked(int playerIndex) => States.TryGetValue(playerIndex, out var s) ? s : null;
}

/// <summary>The respawn-room seam for the charge arm's two asks: which party it
/// says stands in a room, and the centre every ask was made about.</summary>
internal sealed class MedigunRooms : ITFRespawnRooms
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

/// <summary>The condition seam as the facts want it: a set with a logged trail.</summary>
internal sealed class TFHealingConditions : ITFPlayerConditions
{
    public readonly List<string> Calls = new();
    private readonly HashSet<(int Player, TFCond Cond)> active = new();

    public TFCondAdd Add(EntityHandle player, TFCond cond, float duration = TFConditions.Permanent, EntityHandle provider = default)
    {
        Calls.Add($"add {player.EntryIndex} {cond} d{duration} p{provider.EntryIndex}");
        active.Add((player.EntryIndex, cond));
        return TFCondAdd.Added;
    }

    public bool Remove(EntityHandle player, TFCond cond, bool ignoreDuration = false)
    {
        Calls.Add($"remove {player.EntryIndex} {cond}");
        return active.Remove((player.EntryIndex, cond));
    }

    public TFCondMask RemoveAll(EntityHandle player)
    {
        active.RemoveWhere(a => a.Player == player.EntryIndex);
        return default;
    }

    public bool InCond(EntityHandle player, TFCond cond) => active.Contains((player.EntryIndex, cond));

    public TFConditions? Of(int playerIndex) => null;

    public bool Holds(int playerIndex, TFCond cond) => active.Contains((playerIndex, cond));
}

/// <summary>The resupply seam: class maxes from the rig, TakeHealth logged and answered.</summary>
internal sealed class TFHealingResupply : ITFPlayerResupply
{
    public readonly Dictionary<int, int> Maxes = new();
    public readonly List<string> Takes = new();

    public void Regenerate(EntityHandle player, bool refillHealthAndAmmo = true)
        => throw new NotSupportedException();

    public int MaxHealth(EntityHandle player) => Maxes.TryGetValue(player.EntryIndex, out var m) ? m : 0;

    /// <summary>The health each player has been handed, summed — the string log beside it is the older lens.</summary>
    public readonly Dictionary<int, float> Taken = new();

    /// <summary>Takes exactly what was asked; the refusing arms (no-heal debuff) are deferred rows.</summary>
    public int TakeHealth(EntityHandle player, float health, bool ignoreMaxHealth = false)
    {
        Takes.Add($"take {player.EntryIndex} {health} {ignoreMaxHealth}");
        Taken[player.EntryIndex] = Taken.GetValueOrDefault(player.EntryIndex) + health;
        return (int)health;
    }

    public int MaxAmmo(EntityHandle player, TFAmmo ammo) => 0;

    public int GiveAmmo(EntityHandle player, int count, TFAmmo ammo) => 0;
}

/// <summary>The stats seam: the heals it was credited, as one list.</summary>
internal sealed class TFHealingStats : ITFPlayerStats
{
    public readonly List<string> Heals = new();

    public void Increment(EntityHandle player, TFStat stat, int amount = 1) => Heals.Add($"{stat} {player.EntryIndex} {amount}");

    public int Round(int playerIndex, TFStat stat) => 0;

    public int Accumulated(int playerIndex, TFStat stat) => 0;

    public int RoundScore(int playerIndex) => 0;

    public int Score(int playerIndex) => 0;

    // The medigun facts never arm the player-score reset; this half of the
    // interface is not their subject.
    public void ResetPlayer(EntityHandle player) { }
}

/// <summary>The game-events seam: every fired event's keys as one line.</summary>
internal sealed class TFHealingEvents : IEngineGameEvents
{
    public readonly List<string> Fired = new();
    private readonly Dictionary<IntPtr, SortedDictionary<string, string>> values = new();
    private IntPtr next = new(1);

    public IntPtr EventCreate(string name)
    {
        IntPtr h = next;
        next = new IntPtr(next.ToInt64() + 1);
        values[h] = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["event"] = name };
        return h;
    }

    public bool EventFire(IntPtr handle, bool dontBroadcast = false)
    {
        Fired.Add(string.Join(" ", values[handle].Select(kv => $"{kv.Key}={kv.Value}")));
        return true;
    }

    public void EventSetInt(IntPtr handle, string key, int value) => values[handle][key] = value.ToString();
    public void EventSetBool(IntPtr handle, string key, bool value) => values[handle][key] = value.ToString();
    public void EventSetString(IntPtr handle, string key, string value) => values[handle][key] = value;
    public bool EventGetBool(IntPtr handle, string key) => false;
    public float EventGetFloat(IntPtr handle, string key) => 0f;
    public int EventGetInt(IntPtr handle, string key) => 0;
    public string EventGetString(IntPtr handle, string key) => string.Empty;
    public void ListenForGameEvent(string name) { }
}

/// <summary>The connected-client seam: only the per-player convar answer matters here.</summary>
internal sealed class TFHealingClients : IConnectedClients
{
    public readonly Dictionary<(int Ent, string Name), string> ConVars = new();

    public ulong SteamId64(int entIndex) => 0;
    public string? Address(int entIndex) => null;
    public float? AvgLatency(int entIndex) => null;
    public string? ClientConVar(int entIndex, string name) => ConVars.TryGetValue((entIndex, name), out var v) ? v : null;
    public int UserId(int entIndex) => 100 + entIndex;
    public void Kick(int entIndex, string reason) => throw new NotSupportedException();
    public void ClientCommand(int entIndex, string command) => throw new NotSupportedException();
    public void Reconnect(int entIndex) => throw new NotSupportedException();
}

/// <summary>The CTF rules seam for the flag-carrier refusal arm; the flag-list
/// members answer nothing — this fact's subject is <c>CarriedBy</c>.</summary>
internal sealed class TFHealingCtf : ICtfRules
{
    /// <summary>The players this fake says carry a flag; their CarriedBy answers an uninitialized item.</summary>
    public readonly HashSet<int> Carriers = new();

    public ItemTeamFlag? CarriedBy(EntityHandle player)
        => Carriers.Contains(player.EntryIndex)
            ? (ItemTeamFlag)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ItemTeamFlag))
            : null;

    public bool OvertimeAllowedForCtf => true;
    public void SetOvertimeAllowedForCtf(bool allowed) { }
    public bool FlagsMayBeCapped => true;
    public bool InWaitingForPlayers => false;
    public bool InStalemate => false;
    public bool CanFlagBeCaptured(EntityHandle player) => false;
    public IEnumerable<ItemTeamFlag> Flags => [];
    public bool CheckCapsPerRound() => false;
    public bool PointInRespawnFlagZone(Vector point) => false;
    public void Report(string kind, TFTeam team, int index) { }
    public int FlagCaptures(TFTeam team) => 0;
    public void RewardTeam(TFTeam team, bool asCapture, int score) { }
    public void SetFlagCaptures(TFTeam team, int captures) { }
}
