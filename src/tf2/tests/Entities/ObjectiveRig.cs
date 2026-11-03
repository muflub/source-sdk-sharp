using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;
using TF2.Server.Entities;
using TF2.Server.Entities.Ctf;
using TF2.Server.Player;
using TF2.Server.Voice;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;
using TF2.Tests.Ctf;
using TF2.Tests.Voice;
using ModeSlot = TF2.Tests.Rules.ModeSlot;

namespace TF2.Tests.Entities;

/// <summary>The convars, as a record a fact sets.</summary>
internal sealed class FakeSettings : IObjectiveSettings
{
    public int CapStyle { get; set; } = 1;
    public int BlockStyle { get; set; } = 1;
    public float CapDeteriorateTime { get; set; } = 90f;
    public int SimulateMultipleCappers { get; set; } = 1;
    public bool CapLinear { get; set; } = true;
}

/// <summary>A clock a fact moves.</summary>
internal sealed class FakeClock : IGameClock
{
    public float Now { get; set; }
}

/// <summary>The contract's state half, as a fact sets it.</summary>
internal sealed class FakeRoundState : ITeamRoundState
{
    public RoundState State { get; set; } = RoundState.RoundRunning;
    public bool InWaitingForPlayers { get; set; }
    public bool InSetup { get; set; }
    public float MapResetTime { get; set; }
    public float StalemateStartTime { get; set; } = -1f;
    public TFTeam WinningTeam { get; set; } = TFTeam.Unassigned;
}

/// <summary>The contract's control half, recording each end of a round.</summary>
internal sealed class FakeRoundControl : ITeamRoundControl
{
    public List<(TFTeam Team, WinReason Reason, bool ForceMapReset, bool SwitchTeams)> Wins { get; } = new();
    public List<StalemateReason> Stalemates { get; } = new();

    /// <summary>What <c>SetStalemateOnTimelimit</c> was told; null before any input.</summary>
    public bool? StalemateOnTimelimit { get; set; }

    public void SetWinningTeam(TFTeam team, WinReason reason, bool forceMapReset = true, bool switchTeams = false,
                               bool dontAddScore = false, bool final = false)
        => Wins.Add((team, reason, forceMapReset, switchTeams));

    public void SetStalemate(StalemateReason reason, bool forceMapReset = true, bool switchTeams = false)
        => Stalemates.Add(reason);

    public void SetStalemateOnTimelimit(bool stalemate) => StalemateOnTimelimit = stalemate;

    public bool SwitchTeams { get; private set; }

    public void SetSwitchTeams(bool switchTeams) => SwitchTeams = switchTeams;

    public bool ShouldSwitchTeams() => SwitchTeams;

    public bool ScrambleTeams { get; private set; }

    public void SetScrambleTeams(bool scrambleTeams) => ScrambleTeams = scrambleTeams;

    public bool ShouldScrambleTeams() => ScrambleTeams;

    public (bool Team, bool Player)? ResetScores { get; private set; }

    public void ShouldResetScores(bool teamScores, bool playerScores) => ResetScores = (teamScores, playerScores);

    public bool? ResetRoundsPlayed { get; private set; }

    public void ShouldResetRoundsPlayed(bool roundsPlayed) => ResetRoundsPlayed = roundsPlayed;
}

/// <summary>Players by handle: team and alive.</summary>
internal sealed class FakePlayers : ICapturePlayers
{
    public Dictionary<EntityHandle, (int Team, bool Alive)> All { get; } = new();

    public EntityHandle Add(int index, TFTeam team, bool alive = true)
    {
        var handle = new EntityHandle((uint)index | (1u << 16));
        All[handle] = ((int)team, alive);
        return handle;
    }

    public bool IsPlayer(EntityHandle entity) => All.ContainsKey(entity);
    public bool IsAlive(EntityHandle player) => All.TryGetValue(player, out var p) && p.Alive;
    public int Team(EntityHandle player) => All.TryGetValue(player, out var p) ? p.Team : 0;
}

/// <summary>The stack's game events, as a list of names.</summary>
internal sealed class FakeEvents : IObjectiveEvents
{
    public List<string> Fired { get; } = new();

    public void StartTouch(EntityHandle player, int area) => Fired.Add($"starttouch {area}");
    public void EndTouch(EntityHandle player, int area) => Fired.Add($"endtouch {area}");
    public void PointStartCapture(int cp, string cpName, int team, int capTeam, float capTime, IReadOnlyList<int> cappers)
        => Fired.Add($"startcapture {cp} {capTeam} {cappers.Count}");
    public void PointCaptured(int cp, string cpName, int team, IReadOnlyList<int> cappers)
        => Fired.Add($"captured {cp} {team} {string.Join(",", cappers)}");
    public void CaptureBroken(int cp, string cpName, float timeRemaining) => Fired.Add($"broken {cp}");
    public void CaptureBlocked(int cp, string cpName, int blocker, int victim) => Fired.Add($"blocked {cp} {blocker}");
    public void PointLocked(int cp, string cpName, int team) => Fired.Add($"locked {cp}");
    public void PointUnlocked(int cp, string cpName, int team) => Fired.Add($"unlocked {cp}");
    public void CapperKilled(int blocker, int victim) => Fired.Add($"capperkilled {blocker} {victim}");
    public void RoundSelected(string round) => Fired.Add($"round {round}");
}

/// <summary>The rules beyond the contract, with overtime and the escort answer a fact sets, and what they were told.</summary>
internal sealed class FakeRoundRules : IObjectiveRoundRules
{
    private readonly TF2.Server.Rules.RoundHistory _history = new();

    public bool InOvertime { get; set; }
    public bool PointsMayAlwaysBeBlocked { get; set; }
    public List<(TFTeam Team, float Seconds)> WaveTimes { get; } = new();
    public List<(TFTeam Team, int Points)> Scores { get; } = new();

    public void AddTeamRespawnWaveTime(TFTeam team, float seconds) => WaveTimes.Add((team, seconds));
    public void AddTeamScore(TFTeam team, int points) => Scores.Add((team, points));
    public void SetLastCapPointChanged(int index) { }
    public string RoundToPlayNext { get => _history.RoundToPlayNext; set => _history.RoundToPlayNext = value; }
    public string LastPlayedRound => _history.LastPlayedRound;
    public string FirstRoundPlayed { get => _history.FirstRoundPlayed; set => _history.FirstRoundPlayed = value; }
    public void AddPlayedRound(string round) => _history.AddPlayedRound(round);
    public bool IsPreviouslyPlayedRound(string round) => _history.IsPreviouslyPlayedRound(round);
    public void SetRoundOverlayDetails() { }
    public void PlaySpecialCapSounds(TFTeam cappingTeam, TeamControlPoint point) { }
}

internal sealed class FakeRandom : IUniformRandom
{
    public int Next { get; set; }
    public float RandomFloat(float min, float max) => min;
    public int RandomInt(int min, int max) => Math.Clamp(min + Next, min, max);
}

internal sealed class FakeMaterials : IHudMaterialIndex
{
    public int IndexOf(string material) => material.Length;
}

/// <summary>The round machine as the timer host, with the answers a fact sets.</summary>
internal sealed class FakeRoundTimerHost : IRoundTimerHost
{
    public RoundState State { get; set; } = RoundState.RoundRunning;
    public bool InWaitingForPlayers { get; set; }
    public bool InSetup { get; set; }
    public float MapResetTime { get; set; }
    public TFTeam WinningTeam { get; set; } = TFTeam.Unassigned;
    public bool InOvertime { get; set; }
    public bool MayExpire { get; set; } = true;
    public bool SetupSet { get; set; }
    public bool OvertimeSet { get; set; }
    public float StalemateStartTime => -1f;

    public void SetSetup(bool setup) => SetupSet = setup;
    public void SetOvertime(bool overtime) => OvertimeSet = overtime;
    public bool TimerMayExpire() => MayExpire;
}

/// <summary>The waves' state, as a list of what the wave inputs were told.</summary>
internal sealed class FakeTeamRespawnWaves : ITeamRespawnWaves
{
    public List<(TFTeam Team, float Seconds)> Set { get; } = new();
    public List<(TFTeam Team, float Seconds)> Added { get; } = new();

    public float NextRespawnWave(TFTeam team) => 0f;
    public float NextRespawnWave(TFTeam team, EntityHandle player) => 0f;
    public float RespawnWaveMaxLength(TFTeam team, bool scaleWithNumPlayers = true) => 0f;
    public void SetTeamRespawnWaveTime(TFTeam team, float seconds) => Set.Add((team, seconds));
    public void AddTeamRespawnWaveTime(TFTeam team, float seconds) => Added.Add((team, seconds));
}

/// <summary>
/// The base answer (everyone may cap, value 1), save for the players a fact
/// names as blockers: they may not cap but may block, as an ubered player does.
/// </summary>
internal sealed class FakeCapturePlayerRules : ICapturePlayerRules
{
    public readonly HashSet<EntityHandle> Blockers = new();

    public bool PlayerMayCapturePoint(EntityHandle player, int pointIndex) => !Blockers.Contains(player);
    public bool PlayerMayBlockPoint(EntityHandle player, int pointIndex) => Blockers.Contains(player);
    public int CaptureValueForPlayer(EntityHandle player) => 1;
}

/// <summary>
/// A map's objective entities on a registry over a recording engine, with
/// the round contract, the convars and the players as fakes: what every
/// control-point fact stands on.
/// </summary>
internal sealed class ObjectiveRig
{
    public readonly ScriptedEntityRegistry Registry = new(TestEngines.Server());
    public readonly RecordingEngine Engine;
    public readonly FakeSettings Settings = new();
    public readonly FakeClock Clock = new();
    public readonly FakeRoundState Round = new();
    public readonly FakeRoundControl Control = new();
    public readonly FakePlayers Players = new();
    public readonly FakeEvents Events = new();
    public readonly FakeCapturePlayerRules PlayerRules = new();
    public readonly TFPlayerObjectiveTimes ObjectiveTimes = new();
    public readonly TF2.Server.Player.TFPlayerStats Stats = new();
    public readonly TF2.Tests.Medigun.TFHealingEvents GameEvents = new();
    public readonly ObjectiveResource Objectives = new(new TF2.Tests.Client.ServerWarnScripts());
    public readonly FakeRoundRules RoundRules = new();
    public readonly ControlPointRules Rules;
    public readonly ObjectiveEntities Entities;
    public readonly FakeRoundTimerHost TimerHost = new();
    public readonly FakeTeamRespawnWaves Waves = new();
    public readonly KothRoundState Koth;
    public readonly TeamResource TeamTable = new();
    public readonly TFTeamScores Scores;
    public readonly CtfRules Ctf;
    public readonly FakeCarrierEffects Effects = new();
    public readonly TFPlayerCarriers Carriers = new();
    public readonly FakeConceptRules ConceptRules = new();
    public readonly FakeConceptTeams ConceptTeams = new();
    public readonly FakeRandom Random = new();
    public readonly TFConceptSpeech Speech;
    public readonly ServiceContainer Services;

    private uint _next = 0x0040_0000u;
    private readonly List<ScriptedEntity> _spawned = new();

    public ObjectiveRig()
    {
        Registry.RegisterAll(typeof(TriggerAreaCapture).Assembly);
        Entities = new ObjectiveEntities(Registry);
        Koth = new KothRoundState(Registry, TimerHost);
        Rules = new ControlPointRules(Round, Objectives, Settings, Entities, Koth);
        Scores = new TFTeamScores(TeamTable);
        Ctf = new CtfRules(Round, Control, Scores, new FakeCtfSettings(), new FakeCtfPlayers(), Registry, new ModeSlot());

        Services = TestServices.Ignoring("Host.RandomFloat", "Networking.EntityGetState", "Networking.EntityGetState.moveType",
                                             "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles", "Entities.IsValid",
                                             "EntityRender.LookupSequence", "Precache.PrecacheModel");
        Engine = (RecordingEngine)Services.Resolve<IEngineEntities>()!;
        Engine.Returns("Entities.GetSpawnFlags", 0)
              // A trigger's Enable/Disable reads its solid flags (CBaseTrigger's FSOLID_TRIGGER).
              .Returns("Entities.GetSolidFlags", SolidFlags.Trigger)
              .Returns("Entities.GetSolidType", SolidType.Bsp)
              // A door's activation looks for the area portals it opens (none here).
              .Returns("Entities.FindAllByClassname", Array.Empty<EntityHandle>())
              .Returns("Entities.GetOrigin", new Vector(100f, 200f, 300f))
              .Returns("EntityRender.LookupPoseParameter", -1)
              .Returns("EntityRender.SetPoseParameter", 0f)
              .Returns("WorldQuery.TraceHull", new HullResult(false, false, EntityHandle.None))
              // The speech path's reads (TFConceptSpeech): no one is alive and
              // no index names a player, so the concept facts stage their own.
              .Returns("Players.IsAlive", false)
              .Returns("Players.GetHandle", EntityHandle.None);

        Services.AddInstance<IEntityDirectory>(Registry);
        Services.AddInstance(Registry.Directory);
        Services.AddInstance<IGameClock>(Clock);
        Services.AddInstance<IObjectiveSettings>(Settings);
        Services.AddInstance<IObjectiveResource>(Objectives);
        Services.AddInstance<IObjectiveRoundRules>(RoundRules);
        Services.AddInstance<IControlPointRules>(Rules);
        Services.AddInstance<ITeamRoundState>(Round);
        Services.AddInstance<ITeamRoundControl>(Control);
        Services.AddInstance<ICapturePlayerRules>(PlayerRules);
        Services.AddInstance<ITFPlayerObjectiveTimes>(ObjectiveTimes);
        Services.AddInstance<ITFPlayerStats>(Stats);
        Services.AddInstance<IEngineGameEvents>(GameEvents);
        Services.AddInstance<ICapturePlayers>(Players);
        Services.AddInstance<IObjectiveEntities>(Entities);
        Services.AddInstance<IObjectiveEvents>(Events);
        Services.AddInstance<IHudMaterialIndex>(new FakeMaterials());
        Services.AddInstance<IUniformRandom>(Random);
        Services.AddInstance(new TF2.Server.Rules.TFWinPanelCappers());
        Services.AddInstance<IRoundTimerHost>(TimerHost);
        Services.AddInstance<IRoundTimers>(new RoundTimers(Registry));
        Services.AddInstance<IKothRoundState>(Koth);
        Services.AddInstance<ITeamRespawnWaves>(Waves);
        Services.AddInstance<ICtfRules>(Ctf);
        Services.AddInstance<IFlagCarrierEffects>(Effects);
        Services.AddInstance<TF2.Server.Entities.ITeamResource>(TeamTable);
        // The koth clocks are real TeamRoundTimers (ArmKothTimerSpawns): since
        // the overtime nag the timer's constructor asks for the convars and a
        // seed source, and the registry builds nothing without them. The nag
        // convar is left unregistered — Get answers null, so it stays off.
        Services.AddInstance(new ConVarRegistry(new RigConVarBackend()));
        Services.AddInstance<IRandomSeeds>(new RandomSeeds(7));
        Services.AddInstance<IResponseRules>(ConceptRules);
        Services.AddInstance<ITeams>(ConceptTeams);
        Speech = new TFConceptSpeech(ConceptRules, Carriers, ConceptTeams,
            Services.Resolve<IEnginePlayers>()!, ConceptSceneImage(), Services.Resolve<IEngineSound>()!, Random);
        Services.AddInstance(Speech);
        Registry.Services = Services;
    }

    public EntityHandle NewHandle() => new(_next++);

    public T Spawn<T>(string className, params (string Key, string Value)[] keys) where T : ScriptedEntity
    {
        var handle = NewHandle();
        foreach (var (key, value) in keys)
            Registry.KeyValue(handle, className, key, value);
        Registry.Spawn(handle, className);
        var entity = (T)Registry.Get(handle)!;
        _spawned.Add(entity);
        return entity;
    }

    /// <summary>A point, owned by default by <paramref name="owner"/>.</summary>
    public TeamControlPoint Point(string name, int index, TFTeam owner = TFTeam.Unassigned, int group = 0)
        => Spawn<TeamControlPoint>(TeamControlPoint.MapClass, ("targetname", name), ("point_index", index.ToString()),
                                   ("point_default_owner", ((int)owner).ToString()), ("point_group", group.ToString()),
                                   ("point_printname", name), ("team_icon_2", "sprites/obj_icons/icon_obj_red"),
                                   ("team_icon_3", "sprites/obj_icons/icon_obj_blu"), ("team_icon_0", "sprites/obj_icons/icon_obj_neutral"));

    /// <summary>An area over a point that both game teams may capture, one capper required.</summary>
    public TriggerAreaCapture Area(string point, float capTime, int required = 1)
        => Spawn<TriggerAreaCapture>(TriggerAreaCapture.MapClass, ("area_cap_point", point), ("area_time_to_cap", capTime.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                                     ("team_cancap_2", "1"), ("team_cancap_3", "1"),
                                     ("team_numcap_2", required.ToString()), ("team_numcap_3", required.ToString()));

    public TeamControlPointMaster Master(params (string Key, string Value)[] keys)
        => Spawn<TeamControlPointMaster>(TeamControlPointMaster.MapClass, keys);

    /// <summary>The koth logic entity; its RoundSpawn/RoundActivate walk with the rest.</summary>
    public KothLogic SpawnKothLogic(params (string Key, string Value)[] keys)
        => Spawn<KothLogic>(TF2.Server.Entities.KothLogic.MapClass, keys);

    /// <summary>
    /// Arms the engine's timer creations so the koth logic's RoundSpawn
    /// attaches real <see cref="TeamRoundTimer"/>s to the registry as they
    /// happen (stock's <c>CBaseEntity::Create</c>, answered by the managed
    /// registry).
    /// </summary>
    public void ArmKothTimerSpawns()
    {
        var created = new Queue<EntityHandle>();
        Engine.ReturnsEachCall("Entities.Create", () =>
        {
            var handle = NewHandle();
            created.Enqueue(handle);
            return handle;
        });
        Engine.WhenCalled("Entities.Spawn", call =>
            Registry.Spawn((EntityHandle)call.Args[0]!, RoundTimers.ClassName));
    }

    /// <summary>Every live entity's Activate, then the round's RoundSpawn and RoundActivate, as a level starts a round.</summary>
    public void StartRound()
    {
        foreach (var entity in _spawned)
            entity.OnServerActivate();

        foreach (var entity in _spawned)
            Registry.Input(entity.Handle, "RoundSpawn", string.Empty, 0f, EntityHandle.None);

        foreach (var entity in _spawned)
            Registry.Input(entity.Handle, "RoundActivate", string.Empty, 0f, EntityHandle.None);
    }

    /// <summary>Moves the clock and runs one think of the area.</summary>
    public void ThinkAt(TriggerAreaCapture area, float now)
    {
        Clock.Now = now;
        area.CaptureThink();
    }

    /// <summary>A one-scene image, so a staged Scene response has sounds to name.</summary>
    internal static TFSceneImage ConceptSceneImage()
        => new(TFSceneImageTests.Files(("scenes/scenes.image",
            TFSceneImageTests.Image(("scenes/concept.vcd", 1000, ["vo/concept.ogg"])))));
}

/// <summary>The convar table's backend: declared defaults held, writes kept.</summary>
internal sealed class RigConVarBackend : IConVarBackend
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

/// <summary>
/// The concept search behind the rig's <see cref="TFConceptSpeech"/>: every
/// ask recorded as the concept and playerclass words it carried, and the
/// staged answer returned.
/// </summary>
internal sealed class FakeConceptRules : IResponseRules
{
    public readonly List<(string Concept, string PlayerClass)> Asked = new();

    /// <summary>The weapon and roll words the ask carried, in ask order.</summary>
    public readonly List<(string Key, string Value)> Words = new();

    public bool Match = true;

    public ResponseKind Kind = ResponseKind.Speak;

    public string Name = "vo/concept.ogg";

    public bool FindBestResponse(ResponseCriteriaSet criteria, out MatchedResponse response, IResponseFilter? filter = null)
    {
        Asked.Add((criteria.GetValue(criteria.Find("concept")), criteria.GetValue(criteria.Find("playerclass"))));
        foreach (string key in new[] { "playerweapon", "minigunfiretime", "randomnum", "objtype" })
            Words.Add((key, criteria.GetValue(criteria.Find(key))));

        response = Match ? new MatchedResponse { Kind = Kind, Name = Name, Rule = "staged" } : new();
        return Match;
    }

    public IEnumerable<(ResponseKind Kind, string Value)> Responses => [];
}

/// <summary>Teams by handle, as a fact sets them; the speech service's roster.</summary>
internal sealed class FakeConceptTeams : ITeams
{
    private readonly Dictionary<int, List<EntityHandle>> _members = [];

    private readonly Dictionary<EntityHandle, int> _of = [];

    public void SetTeam(int team, params EntityHandle[] players)
    {
        _members[team] = [.. players];
        foreach (var player in players)
            _of[player] = team;
    }

    public int Team(EntityHandle player) => _of.GetValueOrDefault(player);

    public IReadOnlyList<EntityHandle> Players(int team) => _members.GetValueOrDefault(team) ?? [];

    public int Count(int team) => Players(team).Count;

    public JoinTeamResult Join(EntityHandle player, int requested) => throw new NotSupportedException();

    public EntityHandle SelectSpawn(EntityHandle player) => EntityHandle.None;
}
