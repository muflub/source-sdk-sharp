using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Modes;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;
using TF2.Tests.Rules;

namespace TF2.Tests.Modes;

/// <summary>A map whose classnames a fact lists.</summary>
internal sealed class FixtureMap(string mapName, params string[] classNames) : IMapClassnames
{
    public bool Contains(string className) => classNames.Contains(className, StringComparer.OrdinalIgnoreCase);

    public string MapName => mapName;
}

internal sealed class RecordingFlags : ITFGameTypeFlags
{
    public readonly List<TFGameType> Set = new();

    public void SetGameType(TFGameType type) => Set.Add(type);
}

/// <summary>A mode hook that records what it hears and answers what it is told to.</summary>
internal sealed class RecordingModeHook(int order = 1) : ITFModeHook, IOrderedHook
{
    public readonly List<string> Heard = new();
    public bool AllowsWins { get; set; } = true;
    public float WaveAdd { get; set; }
    public int WinChecks { get; private set; }

    public int HookOrder => order;

    public void OnRoundStart(TFModeBase mode) => Heard.Add($"round_start {mode.Id}");

    public void OnObjectiveEvent(TFModeBase mode, TFObjectiveEvent objective)
        => Heard.Add($"objective {mode.Id} {objective.Kind} {objective.Team} {objective.Index}");

    public bool ModifyWinCheck(TFModeBase mode, TFTeam team, WinReason reason)
    {
        WinChecks++;
        return AllowsWins;
    }

    public float ModifyRespawnWave(TFModeBase mode, TFTeam team, float seconds) => seconds + WaveAdd;
}

/// <summary>
/// The round machine of <see cref="RoundRig"/> with a real TF2 mode active:
/// the mode is built over the machine as the container builds it, and the
/// machine's active-mode slot answers it.
/// </summary>
internal sealed class TFModeRig
{
    public readonly RoundRig Round = new();
    public readonly RecordingFlags Flags = new();
    public readonly RecordingModeHook Hook = new();
    public readonly TFModeServices Services;
    public readonly TF2.Tests.Ctf.FakeCtfSettings CtfSettings = new();
    public readonly TF2.Server.Entities.Ctf.CtfRules CtfRules;
    public readonly CtfMode Ctf;
    public readonly PayloadMode Payload;
    public readonly CPMode Cp;
    public readonly KothMode Koth;
    public readonly TF2.Tests.Payload.FakeModeObjectives Objectives = new();
    public readonly RecordingEngine Engine = RecordingEngine.Default();

    public TFModeRig()
    {
        Services = new TFModeServices(Round.Flow, Round.Flow, Round.Roster, Round.Scores, Flags, Round.Log,
                                      new ListHooks<ITFModeHook>(Hook));
        CtfRules = new TF2.Server.Entities.Ctf.CtfRules(Round.Flow, Round.Flow, Round.Scores, CtfSettings,
                                                        new TF2.Tests.Ctf.FakeCtfPlayers(), new TF2.Tests.Ctf.EmptyDirectory(),
                                                        Round.Slot);
        Ctf = new CtfMode(Services, CtfRules, CtfSettings);
        Engine.Returns("Entities.FindAllByClassname", Array.Empty<EntityHandle>());
        Payload = new PayloadMode(Services, Objectives, Engine);
        Cp = new CPMode(Services);
        Koth = new KothMode(Services);
        Round.Slot.Current = Ctf;
    }
}

/// <summary>
/// The core's mode proxy over the core's own rules, built the way the server
/// module builds it, with TF2's modes registered as the container registers
/// them and TF2's selector in the selector chain.
/// </summary>
internal sealed class ProxyRig
{
    public readonly RecordingEngine Host = RecordingEngine.Default();
    public readonly ServiceContainer Services = new();
    public readonly GameModes Modes;
    public readonly GameRules Rules;
    public readonly TFModeRig Tf = new();

    public ProxyRig(IMapClassnames map, bool registerTFModes = true)
    {
        Host.Strict = false;
        var convars = ConVarRegistry.For(TestEngines.Server().Engine.Require<IEngineConsole>());
        convars.RegisterAll(typeof(GameRules).Assembly);
        var log = new SourceSharp.Abi.RecordingLog();

        Services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>(new TFGameModeSelector(map)));
        if (registerTFModes)
        {
            Services.AddInstance(typeof(CtfMode), Tf.Ctf);
            Services.AddInstance(typeof(PayloadMode), Tf.Payload);
            Services.AddInstance(typeof(CPMode), Tf.Cp);
            Services.AddInstance(typeof(KothMode), Tf.Koth);
        }

        Modes = new GameModes(log, Host, Services, convars.Declare<SvModeSwitchWarmupConVar>(), convars.Declare<MpWaitingForPlayersTimeConVar>());
        var board = new Scoreboard(Host, new PlayerDisconnectEvent(), log);
        Rules = new GameRules(log, convars.Holder<GameRulesConVars>(), Host, Host, Host, board, new PlayerDeathEvent(), modes: Modes);
        Modes.Attach(Rules);
        Services.AddInstance(Modes);
    }

    /// <summary>The level's pick, as a level init runs it.</summary>
    public string? Pick(string mapName = "test_map")
    {
        ((IGameLifecycle)Modes).OnLevelInit(mapName);
        return Modes.ActiveId;
    }
}
