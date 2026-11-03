using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Entities;
using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Server.Voice;
using TF2.Shared;
using TF2.Shared.Rules;
using TF2.Tests.Entities;
using TF2.Tests.Voice;

using Xunit;

namespace TF2.Tests.Payload;

/// <summary>IModeObjectives as a recording fake: the real ObjectiveResource exposes only setters.</summary>
internal sealed class FakeModeObjectives : IModeObjectives
{
    public Dictionary<int, float> PathDistance { get; } = new();
    public Dictionary<int, float> Progress { get; } = new();
    public Dictionary<int, bool> TrackAlarm { get; } = new();
    public Dictionary<int, int> Cappers { get; } = new();
    public Dictionary<int, int> SpeedLevel { get; } = new();
    public Dictionary<int, float> RecedeTime { get; } = new();
    public List<(int Team, float Start, float End, bool Downhill)> Hills { get; } = new();
    public List<int> ResetTeams { get; } = new();
    public bool MultipleTrains { get; private set; }

    public void SetMultipleTrains(bool multiple) => MultipleTrains = multiple;

    public void SetTrainPathDistance(int index, float distance) => PathDistance[index] = distance;

    public void ResetHillData(int team) => ResetTeams.Add(team);

    public void SetHillData(int team, float start, float end, bool downhill) => Hills.Add((team, start, end, downhill));

    public void SetTrackAlarm(int team, bool alarm) => TrackAlarm[team] = alarm;

    public void SetTrainProgress(int team, float progress) => Progress[team] = progress;

    public void SetTrainCappers(int team, int cappers) => Cappers[team] = cappers;

    public void SetTrainSpeedLevel(int team, int level) => SpeedLevel[team] = level;

    public void SetTrainRecedeTime(int team, float time) => RecedeTime[team] = time;

    public void SetFlag(int team, int status, int carrierIndex, float resetTime) { }

    public void SetFlagPresent(int team, bool present) { }

    public void SetFlagOrigin(int team, Vector origin) { }

    public void SetZoneOrigin(int team, Vector origin) { }
}

/// <summary>The sound patch ramps, as a list of what was asked.</summary>
internal sealed class FakeRamps : ISoundPatchRamps
{
    public List<string> Calls { get; } = new();

    public void Destroy(int patch) => Calls.Add($"destroy {patch}");

    public void Play(int patch, float volume, float pitch, float startTime) => Calls.Add($"play {patch}");

    public void ChangePitch(int patch, float target, float deltaTime) => Calls.Add($"pitch {patch}");

    public void ChangeVolume(int patch, float target, float deltaTime) => Calls.Add($"volume {patch}");
}

/// <summary>The resupply seam, recording every heal and ammo grant.</summary>
internal sealed class FakeResupply : ITFPlayerResupply
{
    public List<(EntityHandle Player, float Amount)> Heals { get; } = new();
    public List<(EntityHandle Player, int Count, TFAmmo Ammo)> Grants { get; } = new();

    public int MaxAmmoAnswer = 100;

    public void Regenerate(EntityHandle player, bool refillHealthAndAmmo = true) { }

    public int MaxHealth(EntityHandle player) => 125;

    public int TakeHealth(EntityHandle player, float health, bool ignoreMaxHealth = false)
    {
        Heals.Add((player, health));
        return (int)health;
    }

    public int MaxAmmo(EntityHandle player, TFAmmo ammo) => MaxAmmoAnswer;

    public int GiveAmmo(EntityHandle player, int count, TFAmmo ammo)
    {
        Grants.Add((player, count, ammo));
        return count;
    }
}

/// <summary>
/// A payload track on a registry over a recording engine: the watcher, its
/// train and nodes, the round contract and the objective table as fakes.
/// </summary>
internal sealed class PayloadRig
{
    public readonly ScriptedEntityRegistry Registry = new(TestEngines.Server());
    public readonly RecordingEngine Engine;
    public readonly FakeClock Clock = new();
    public readonly FakeRoundState Round = new();
    public readonly FakeRoundControl Control = new();
    public readonly FakePlayers Players = new();
    public readonly FakeRoundRules RoundRules = new();
    public readonly FakeModeObjectives Mode = new();
    public readonly FakeRamps Ramps = new();
    public readonly FakeResupply Resupply = new();
    public readonly TFPlayerCarriers Carriers = new();
    public readonly FakeConceptRules ConceptRules = new();
    public readonly FakeConceptTeams ConceptTeams = new();
    public readonly TFConceptSpeech Speech;
    public readonly ObjectiveEntities Entities;

    /// <summary>What the engine answers for GetOrigin — moved per fact to stand for the train's place.</summary>
    public Vector EngineOrigin = new(100f, 200f, 300f);

    /// <summary>What the engine answers for GetParent — the train while an area's parent is asked.</summary>
    public EntityHandle ParentOf = EntityHandle.None;

    private uint _next = 0x0042_0000u;
    private readonly List<ScriptedEntity> _spawned = new();

    public PayloadRig()
    {
        Registry.RegisterAll(typeof(TeamTrainWatcher).Assembly);
        // env_spark joins it: the watcher's HandleSparks drives the core's spark
        Registry.RegisterAll(typeof(EnvSpark).Assembly);
        Entities = new ObjectiveEntities(Registry);

        var services = TestServices.Ignoring("Host.RandomFloat", "Networking.EntityGetState", "Networking.EntityGetState.moveType",
                                             "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles", "Entities.IsValid",
                                             "Entities.GetAngles", "Entities.GetBounds", "Entities.GetBoundsSize", "Clock.Now",
                                             "EntityRender.LookupSequence", "Precache.PrecacheModel");
        Engine = (RecordingEngine)services.Resolve<IEngineEntities>()!;
        Engine.Returns("Entities.IsValid", true)
              .Returns("Entities.GetSpawnFlags", 0)
              .Returns("Entities.IsPlayer", true)
              // payload_pushed is undeclared (no .res in the whole SDK declares it —
              // stock's CreateEvent answers null too), so the create answers zero
              // and the publish block stays quiet unless a fact declares an event
              .Returns("GameEvents.EventCreate", IntPtr.Zero)
              .Returns("Sound.SoundPatchCreate", 7)
              .Returns("EntityRender.LookupPoseParameter", -1)
              .Returns("EntityRender.SetPoseParameter", 0f)
              .Returns("WorldQuery.EntitiesInSphere", 0)
              // The speech path's reads (TFConceptSpeech): no one is alive and
              // no index names a player, as on the objective rig.
              .Returns("Players.IsAlive", false)
              .Returns("Players.GetHandle", EntityHandle.None);
        Engine.ReturnsEachCall("Entities.GetOrigin", () => EngineOrigin);
        Engine.ReturnsEachCall("Entities.GetParent", () => ParentOf);

        services.AddInstance<IEntityDirectory>(Registry);
        services.AddInstance<IGameClock>(Clock);
        services.AddInstance<IModeObjectives>(Mode);
        services.AddInstance<IObjectiveRoundRules>(RoundRules);
        services.AddInstance<ITeamRoundState>(Round);
        services.AddInstance<ITeamRoundControl>(Control);
        services.AddInstance<ICapturePlayers>(Players);
        services.AddInstance<IObjectiveEntities>(Entities);
        services.AddInstance<ISoundPatchRamps>(Ramps);
        services.AddInstance<ITFPlayerResupply>(Resupply);

        // the points and master read the same table the watcher writes
        var objectives = new ObjectiveResource();
        var settings = new FakeSettings();
        services.AddInstance<IObjectiveResource>(objectives);
        services.AddInstance<IObjectiveSettings>(settings);

        // no tf_logic_koth on the payload map, so the koth state says "not koth"
        // and its win and capture gates never speak.
        var timerHost = new FakeRoundTimerHost();
        var koth = new KothRoundState(Registry, timerHost);
        services.AddInstance<IRoundTimerHost>(timerHost);
        services.AddInstance<IKothRoundState>(koth);
        services.AddInstance<IControlPointRules>(new ControlPointRules(Round, objectives, settings, Entities, koth));
        services.AddInstance<ICapturePlayerRules>(new BaseCapturePlayerRules());
        services.AddInstance<IObjectiveEvents>(new FakeEvents());
        services.AddInstance<IHudMaterialIndex>(new FakeMaterials());
        services.AddInstance<IUniformRandom>(new FakeRandom());
        services.AddInstance(new TF2.Server.Rules.TFWinPanelCappers());

        // the points' capture speech, the same staging the objective rig gives
        services.AddInstance<IResponseRules>(ConceptRules);
        services.AddInstance<ITeams>(ConceptTeams);
        Speech = new TFConceptSpeech(ConceptRules, Carriers, ConceptTeams,
            services.Resolve<IEnginePlayers>()!, ObjectiveRig.ConceptSceneImage(), services.Resolve<IEngineSound>()!);
        services.AddInstance(Speech);

        var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
        convars.RegisterAll(typeof(TeamTrainWatcher).Assembly);
        services.AddInstance(convars);

        Registry.Services = services;
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

    /// <summary>A path_track at a position, with a name.</summary>
    public PathTrack Node(string name, Vector position, params (string Key, string Value)[] keys)
    {
        var all = new List<(string Key, string Value)> { ("targetname", name) };
        all.AddRange(keys);
        var node = Spawn<PathTrack>(EntityNames.PathTrack, all.ToArray());
        node.PlaceAt(position);
        return node;
    }

    /// <summary>A func_tracktrain named for the watcher, with the startspeed its MaxSpeed reads.</summary>
    public FuncTrackTrain Train(string name, float maxSpeed = 400f)
        => Spawn<FuncTrackTrain>(EntityNames.FuncTracktrain, ("targetname", name), ("speed", "100"),
                                 ("startspeed", maxSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>The watcher over a train and track, driving the train's movement.</summary>
    public TeamTrainWatcher Watcher(string train, string start, string goal, params (string Key, string Value)[] keys)
    {
        var all = new List<(string Key, string Value)>
        {
            ("targetname", "watcher"),
            ("train", train),
            ("start_node", start),
            ("goal_node", goal),
            ("handle_train_movement", "1"),
        };
        all.AddRange(keys);
        return Spawn<TeamTrainWatcher>(TeamTrainWatcher.MapClass, all.ToArray());
    }

    /// <summary>An area over a point that both game teams may capture, one capper required.</summary>
    public TriggerAreaCapture Area(string point, float capTime = 10f, int required = 1)
        => Spawn<TriggerAreaCapture>(TriggerAreaCapture.MapClass, ("area_cap_point", point),
                                     ("area_time_to_cap", capTime.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                                     ("team_cancap_2", "1"), ("team_cancap_3", "1"),
                                     ("team_numcap_2", required.ToString()), ("team_numcap_3", required.ToString()));

    public TeamControlPoint Point(string name, int index)
        => Spawn<TeamControlPoint>(TeamControlPoint.MapClass, ("targetname", name), ("point_index", index.ToString()),
                                   ("point_printname", name), ("team_icon_2", "sprites/obj_icons/icon_obj_red"),
                                   ("team_icon_3", "sprites/obj_icons/icon_obj_blu"), ("team_icon_0", "sprites/obj_icons/icon_obj_neutral"));

    public TeamControlPointMaster Master(params (string Key, string Value)[] keys)
        => Spawn<TeamControlPointMaster>(TeamControlPointMaster.MapClass, keys);

    /// <summary>Activates everything spawned, as a round start does.</summary>
    public void StartRound()
    {
        foreach (var entity in _spawned)
            entity.OnServerActivate();

        foreach (var entity in _spawned)
            Registry.Input(entity.Handle, "RoundSpawn", string.Empty, 0f, EntityHandle.None);

        foreach (var entity in _spawned)
            Registry.Input(entity.Handle, "RoundActivate", string.Empty, 0f, EntityHandle.None);
    }

    /// <summary>Runs one watcher think at the given time.</summary>
    public void ThinkAt(TeamTrainWatcher watcher, float now)
    {
        Clock.Now = now;
        watcher.WatcherThink();
    }

    /// <summary>Runs one dispenser think at the given time.</summary>
    public void ThinkAt(CartDispenser dispenser, float now)
    {
        Clock.Now = now;
        dispenser.DispenseThink();
    }

    /// <summary>Runs one capture-area think at the given time.</summary>
    public void ThinkAt(TriggerAreaCapture area, float now)
    {
        Clock.Now = now;
        area.CaptureThink();
    }
}
