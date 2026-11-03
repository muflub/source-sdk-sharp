using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server.Entities;
using TF2.Server.Entities.Ctf;
using TF2.Server.Entities.Rooms;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Tests.Entities;
using ModeSlot = TF2.Tests.Rules.ModeSlot;

namespace TF2.Tests.Ctf;

/// <summary>The flag convars, as a record a fact sets.</summary>
internal sealed class FakeCtfSettings : ICtfSettings
{
    public bool ReturnOnTouch { get; set; }
    public float ReturnTimeCreditFactor { get; set; } = 1f;
    public int CapsPerRound { get; set; } = 3;
}

/// <summary>Players by handle: team, alive, class, conditions, centre and flag attachment.</summary>
internal sealed class FakeCtfPlayers : ICtfPlayers
{
    public sealed class Player
    {
        public TFTeam Team;
        public bool Alive = true;
        public bool IsTF = true;
        public TFClass Class = TFClass.Scout;
        public TFCondMask Conditions;
        public Vector Center;
        public int Attachment;
        public bool AllowedToPickUp = true;
        public float PercentInvisible;
        public Vector Velocity;
        public float MaxSpeed = 400f;
    }

    public Dictionary<EntityHandle, Player> All { get; } = new();

    public EntityHandle Add(int index, TFTeam team, TFClass playerClass = TFClass.Scout)
    {
        var handle = new EntityHandle((uint)index | (1u << 16));
        All[handle] = new Player { Team = team, Class = playerClass, Center = new Vector(index * 10f, 0f, 40f) };
        return handle;
    }

    public Player this[EntityHandle player] => All[player];

    public bool IsPlayer(EntityHandle entity) => All.ContainsKey(entity);
    public bool IsAlive(EntityHandle player) => All.TryGetValue(player, out var p) && p.Alive;
    public TFTeam Team(EntityHandle player) => All.TryGetValue(player, out var p) ? p.Team : TFTeam.Unassigned;
    public int Index(EntityHandle player) => player.EntryIndex;
    public int UserId(EntityHandle player) => player.EntryIndex + 100;
    public Vector WorldSpaceCenter(EntityHandle player) => All[player].Center;
    public bool IsTFPlayer(EntityHandle player) => All.TryGetValue(player, out var p) && p.IsTF;
    public TFClass Class(EntityHandle player) => All.TryGetValue(player, out var p) ? p.Class : TFClass.Undefined;
    public TFCondMask Conditions(EntityHandle player) => All.TryGetValue(player, out var p) ? p.Conditions : default;
    public bool AllowedToPickUpFlag(EntityHandle player) => !All.TryGetValue(player, out var p) || p.AllowedToPickUp;
    public float PercentInvisible(EntityHandle player) => All.TryGetValue(player, out var p) ? p.PercentInvisible : 0f;
    public int FlagAttachment(EntityHandle player) => All.TryGetValue(player, out var p) ? p.Attachment : 0;
    public Vector Velocity(EntityHandle player) => All.TryGetValue(player, out var p) ? p.Velocity : default;
    public float MaxSpeed(EntityHandle player) => All.TryGetValue(player, out var p) ? p.MaxSpeed : 0f;
}

/// <summary>The CTF events, as a list of lines.</summary>
internal sealed class FakeCtfEvents : ICtfEvents
{
    public List<string> Fired { get; } = new();

    public void FlagEvent(TFFlagEvent type, int player, TFTeam team, int priority, bool? home = null, int carrier = -1)
        => Fired.Add($"flag {type} player={player} team={team} priority={priority}"
                     + (home is { } h ? $" home={(h ? 1 : 0)}" : "") + (carrier != -1 ? $" carrier={carrier}" : ""));

    public void FlagCaptured(TFTeam cappingTeam, int cappingTeamScore, int capperUserId)
        => Fired.Add($"captured team={cappingTeam} score={cappingTeamScore} capper={capperUserId}");

    public void FlagStatusUpdate(int userId, int entIndex) => Fired.Add($"status userid={userId}");
}

/// <summary>The carrier effects, as a list of lines.</summary>
internal sealed class FakeCarrierEffects : IFlagCarrierEffects
{
    public List<string> Done { get; } = new();

    public void RemoveDisguise(EntityHandle player) => Done.Add($"undisguise {player.EntryIndex}");

    public void SetCaptureBonusTime(float seconds) => Done.Add($"bonus_time {seconds}");

    public void CaptureBonus(TFTeam team) => Done.Add($"bonus {team}");
}

/// <summary>The flag's announcer, as a list of lines.</summary>
internal sealed class FakeAnnouncer : ITFAnnouncer
{
    public List<string> Lines { get; } = new();

    public void Stolen(EntityHandle flag, TFTeam pickerTeam, EntityHandle picker)
        => Lines.Add($"stolen flag={flag.EntryIndex} pickerTeam={pickerTeam} picker={picker.EntryIndex}");

    public void Captured(EntityHandle flag, TFTeam capperTeam, EntityHandle capper)
        => Lines.Add($"captured flag={flag.EntryIndex} capperTeam={capperTeam} capper={capper.EntryIndex}");

    public void Dropped(EntityHandle flag, TFTeam dropperTeam, EntityHandle dropper)
        => Lines.Add($"dropped flag={flag.EntryIndex} dropperTeam={dropperTeam} dropper={dropper.EntryIndex}");

    public void Returned(EntityHandle flag, TFTeam flagTeam)
        => Lines.Add($"returned flag={flag.EntryIndex} flagTeam={flagTeam}");

    public void TouchingEnemyCap(EntityHandle player) => Lines.Add($"touchcap player={player.EntryIndex}");

    public void AttackDefendStolen(EntityHandle flag, TFTeam pickerTeam, EntityHandle picker)
        => Lines.Add($"ad stolen flag={flag.EntryIndex} pickerTeam={pickerTeam} picker={picker.EntryIndex}");

    public void AttackDefendCaptured(EntityHandle flag, TFTeam capperTeam, EntityHandle capper)
        => Lines.Add($"ad captured flag={flag.EntryIndex} capperTeam={capperTeam} capper={capper.EntryIndex}");

    public void AttackDefendDropped(EntityHandle flag, TFTeam dropperTeam, EntityHandle dropper)
        => Lines.Add($"ad dropped flag={flag.EntryIndex} dropperTeam={dropperTeam} dropper={dropper.EntryIndex}");

    public void AttackDefendReturned(EntityHandle flag, TFTeam flagTeam)
        => Lines.Add($"ad returned flag={flag.EntryIndex} flagTeam={flagTeam}");
}

/// <summary>A respawn room that holds a point a fact names.</summary>
internal sealed class FakeRespawnRooms : ITFRespawnRooms
{
    public HashSet<EntityHandle> Inside { get; } = new();

    public bool InRespawnRoom(EntityHandle player, bool sameTeamOnly) => Inside.Contains(player);

    public bool PointInRespawnRoom(Vector point) => false;

    public bool PointInRespawnRoom(EntityHandle target, Vector origin, bool touchingSameTeamOnly) => Inside.Contains(target);

    public bool PointsCrossRespawnRoomVisualizer(Vector start, Vector end, int teamToIgnore = 0) => false;

    public int TouchCount(EntityHandle player) => Inside.Contains(player) ? 1 : 0;
}

/// <summary>A directory with nothing in it: a map with no flags.</summary>
internal sealed class EmptyDirectory : IEntityDirectory
{
    public ScriptedEntity? Get(EntityHandle handle) => null;
    public ScriptedEntity? FindByName(string name) => null;
    public IEnumerable<ScriptedEntity> OfClass(string className) => [];
    public bool IsRegistered(string className) => false;
    public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
}

/// <summary>
/// A CTF map on a registry over a recording engine: the flags, the zones,
/// the real <see cref="CtfRules"/> over the round contract's fakes and a real
/// team table, and the players as fakes. What every CTF fact stands on.
/// </summary>
internal sealed class CtfRig
{
    public static readonly Vector RedHome = new(-488f, 3348f, -131f);
    public static readonly Vector BlueHome = new(489f, -3348f, -131f);

    /// <summary>Where every drop trace lands, unless a fact says otherwise.</summary>
    public static readonly Vector Ground = new(7f, 8f, 9f);

    public readonly ScriptedEntityRegistry Registry = new(TestEngines.Server());
    public readonly RecordingEngine Engine;
    public readonly FakeClock Clock = new() { Now = 10f };
    public readonly FakeRoundState Round = new();
    public readonly FakeRoundControl Control = new();
    public readonly FakeCtfSettings Settings = new();
    public readonly FakeCtfPlayers Players = new();
    public readonly FakeCtfEvents Events = new();
    public readonly FakeCarrierEffects Effects = new();
    public readonly FakeAnnouncer Announcer = new();
    public readonly TF2.Tests.Rules.FakeAnnouncer RoundAnnouncer = new();
    public readonly FakeRespawnRooms RespawnRooms = new();
    public readonly ObjectiveResource Objectives = new();
    public readonly TF2.Shared.Resources.ObjectiveResourceNetState ObjectiveState = new();
    public readonly TeamResource TeamTable = new();
    public readonly TFTeamScores Scores;
    public readonly ModeSlot Slot = new();
    public readonly CtfRules Rules;

    private uint _next = 0x0040_0000u;
    private readonly Dictionary<EntityHandle, Vector> _origins = new();

    /// <param name="optionalSeams">False leaves the two optional seams (respawn rooms, carrier effects) unregistered, as the game has them today.</param>
    public CtfRig(bool optionalSeams = true)
    {
        Registry.RegisterAll(typeof(ItemTeamFlag).Assembly);
        ObjectiveState.BindLocal();
        Objectives.Attach(ObjectiveState);
        Scores = new TFTeamScores(TeamTable);
        Rules = new CtfRules(Round, Control, Scores, Settings, Players, Registry, Slot);

        var services = TestServices.Ignoring("Host.RandomFloat", "Networking.EntityGetState", "Networking.EntityGetState.moveType",
                                             "Entities.GetSpawnOrigin", "Entities.IsValid", "Precache.PrecacheModel",
                                             // The carried effects (trail, return icon) create their entities on
                                             // pick-up/drop; tests that assert on them stub Create themselves.
                                             "Entities.Create", "Entities.SetKeyValue");
        Engine = (RecordingEngine)services.Resolve<IEngineEntities>()!;
        Engine.Returns("Entities.GetSpawnFlags", 0)
              // A trigger's Enable/Disable reads its solid flags (CBaseTrigger's FSOLID_TRIGGER).
              .Returns("Entities.GetSolidFlags", SolidFlags.Trigger)
              .Returns("Entities.GetSolidType", SolidType.Bsp)
              .Returns("Entities.GetSpawnAngles", new Vector(0f, 120f, 0f))
              .Returns("Entities.GetAngles", new Vector(0f, 120f, 0f))
              .Returns("EntityRender.LookupSequence", 1)
              .Returns("Entities.SetParent", true)
              .Returns("Entities.GetBounds", (new Vector(-50f, -50f, -50f), new Vector(50f, 50f, 50f)))
              .Returns("WorldQuery.EntityTraceHull", new EntityHullTrace(Ground, false, false));
        Engine.ReturnsEachCall("Entities.GetIndex", () => Engine.Calls[^1].Args[0] is EntityHandle h ? h.EntryIndex : 0);
        Engine.ReturnsEachCall("Entities.GetOrigin",
                               () => Engine.Calls[^1].Args[0] is EntityHandle h && _origins.TryGetValue(h, out var o) ? o : default);
        Engine.WhenCalled("Entities.SetOrigin", call => _origins[(EntityHandle)call.Args[0]!] = (Vector)call.Args[1]!);

        services.AddInstance<IEntityDirectory>(Registry);
        services.AddInstance<IGameClock>(Clock);
        services.AddInstance<ICtfRules>(Rules);
        services.AddInstance<ICtfPlayers>(Players);
        services.AddInstance<ICtfSettings>(Settings);
        services.AddInstance<ICtfEvents>(Events);
        services.AddInstance<ITFAnnouncer>(Announcer);
        services.AddInstance<TF2.Server.Rules.IRoundAnnouncer>(RoundAnnouncer);
        services.AddInstance<IFlagDetectionZoneDispatch>(new FlagDetectionZoneDispatch(Registry));
        services.AddInstance(new TF2.Server.Rules.TFWinPanelCappers());
        services.AddInstance<IModeObjectives>(Objectives);
        if (optionalSeams)
        {
            services.AddInstance<IFlagCarrierEffects>(Effects);
            services.AddInstance<ITFRespawnRooms>(RespawnRooms);
        }
        Registry.Services = services;
    }

    public EntityHandle NewHandle() => new(_next++);

    public T Spawn<T>(string className, Vector origin, params (string Key, string Value)[] keys) where T : ScriptedEntity
    {
        var handle = NewHandle();
        _origins[handle] = origin;
        foreach (var (key, value) in keys)
            Registry.KeyValue(handle, className, key, value);
        Registry.Spawn(handle, className);
        return (T)Registry.Get(handle)!;
    }

    /// <summary>A CTF flag of <paramref name="team"/> at its home, keyed as ctf_2fort keys its two.</summary>
    public ItemTeamFlag Flag(TFTeam team, params (string Key, string Value)[] extra)
    {
        var keys = new List<(string, string)>
        {
            ("targetname", team == TFTeam.Red ? "flag_red" : "flag_blue"), ("TeamNum", ((int)team).ToString()),
            ("GameType", "0"), ("ReturnTime", "60"), ("StartDisabled", "0"),
        };
        keys.AddRange(extra);
        return Spawn<ItemTeamFlag>(ItemTeamFlag.MapClass, team == TFTeam.Red ? RedHome : BlueHome, keys.ToArray());
    }

    /// <summary>A capture zone of <paramref name="team"/>.</summary>
    public FuncCaptureZone Zone(TFTeam team)
        => Spawn<FuncCaptureZone>(FuncCaptureZone.MapClass, default, ("TeamNum", ((int)team).ToString()), ("capturepoint", "1"));

    /// <summary>Moves the clock and runs one think of the flag.</summary>
    public void ThinkAt(ItemTeamFlag flag, float now)
    {
        Clock.Now = now;
        flag.Think();
    }

    /// <summary>The flag's current origin, as the engine was last told it.</summary>
    public Vector OriginOf(ScriptedEntity entity) => _origins[entity.Handle];

    /// <summary>The parent the flag was last given, or None for a clear.</summary>
    public EntityHandle LastParent(ScriptedEntity entity)
        => Engine.AllOf("Entities.SetParent").Where(c => (EntityHandle)c.Args[0]! == entity.Handle)
                 .Select(c => (EntityHandle)c.Args[1]!).LastOrDefault();
}
