using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Screens.Samples;

/// <summary>
/// Sample data for previews, goldens and the lint: the data interfaces
/// answered from fixed values (plan_client_ui §4). Deterministic: the same
/// seed gives the same picture.
/// </summary>
public sealed class FakeScoreboard : IScoreboardSource
{
    private readonly ScoreboardTeam[] _teams;
    private readonly ScoreboardPlayer[] _players;

    public FakeScoreboard(string server, string map, int maxPlayers, ScoreboardTeam[] teams, ScoreboardPlayer[] players)
    {
        ServerName = server;
        MapName = map;
        MaxPlayers = maxPlayers;
        _teams = teams;
        _players = players;
    }

    public string ServerName { get; }
    public string MapName { get; }
    public int MaxPlayers { get; }
    public int TeamCount => _teams.Length;
    public ScoreboardTeam Team(int i) => _teams[i];
    public int PlayerCount => _players.Length;
    public ScoreboardPlayer Player(int i) => _players[i];

    private static IReadOnlyList<string> Names { get; } =
    [
        "Gordon", "xXx_Headcrab_Slayer_From_Ravenholm_xXx", "Alyx", "barney.calhoun", "Dr. Kleiner",
        "Odessa Cubbage", "Vortigaunt", "Father Grigori", "Mossman", "Eli", "Lamarr", "Colonel Cubbage",
        "Uriah", "Magnusson", "Breen", "Overwatch",
    ];

    /// <summary>
    /// Two teams of <paramref name="perTeam"/>: frags and pings from a fixed
    /// sequence, player 0 local, every fifth a bot, every fourth dead.
    /// </summary>
    public static FakeScoreboard Teams(int perTeam, int seed = 1, string server = "SourceSharp Deathmatch | dm_lockdown 24/7")
    {
        var rng = new Random(seed);
        var players = new List<ScoreboardPlayer>();
        for (int t = 0; t < 2; t++)
        {
            for (int k = 0; k < perTeam; k++)
            {
                int i = players.Count;
                bool bot = i % 5 == 4;
                players.Add(new ScoreboardPlayer(
                    Index: i + 1,
                    Name: bot ? $"BOT {Names[i % Names.Count]}" : Names[i % Names.Count],
                    Team: t == 0 ? 2 : 3,
                    Frags: rng.Next(0, 31),
                    Deaths: rng.Next(0, 19),
                    Ping: bot ? 0 : 18 + rng.Next(0, 160),
                    Alive: i % 4 != 3,
                    Bot: bot,
                    Local: i == 0));
            }
        }

        int Score(int team) => players.Where(p => p.Team == team).Sum(p => p.Frags);
        return new FakeScoreboard(server, "dm_lockdown", 16,
            [new ScoreboardTeam(2, "Combine", Score(2)), new ScoreboardTeam(3, "Rebels", Score(3))],
            [.. players]);
    }

    public static FakeScoreboard Empty()
        => new("SourceSharp Deathmatch | dm_lockdown 24/7", "dm_lockdown", 16,
               [new ScoreboardTeam(2, "Combine", 0), new ScoreboardTeam(3, "Rebels", 0)], []);
}

/// <summary>
/// The local player as a sample: one fake behind the six player interfaces,
/// registered once per interface (<see cref="Register"/>).
/// </summary>
public sealed class FakeLocalPlayer : IPlayerLife, IPlayerHealth, IPlayerArmour, IPlayerWeapon, IPlayerAmmo, IPlayerDamage,
                                     IPlayerSuitPower
{
    public float SuitPower { get; init; } = 100f;
    public int SuitDevices { get; init; }
    public bool Alive { get; init; } = true;
    public int Health { get; init; } = 100;
    public int MaxHealth { get; init; } = 100;
    public int Armour { get; init; } = 60;
    public int MaxArmour { get; init; } = 100;
    public bool HasWeapon { get; init; } = true;
    public string WeaponName { get; init; } = "#HL2_SMG1_Menu";
    public int Clip { get; init; } = 32;
    public int Reserve { get; init; } = 180;
    public int Secondary { get; init; } = 2;
    public int SecondaryMax { get; init; } = 3;
    public double LastDamageAt { get; init; } = double.NegativeInfinity;
    public float LastDamageYaw { get; init; } = float.NaN;

    public UiSampleServices Register(UiSampleServices s)
        => s.Add<IPlayerLife>(this).Add<IPlayerHealth>(this).Add<IPlayerArmour>(this)
            .Add<IPlayerWeapon>(this).Add<IPlayerAmmo>(this).Add<IPlayerDamage>(this).Add<IPlayerSuitPower>(this);
}

/// <summary>The spectated player as a sample.</summary>
public sealed record FakeSpectatorTarget : ISpectatorTargetSource
{
    public bool HasTarget { get; init; } = true;
    public string Name { get; init; } = "Alyx";
    public int Team { get; init; } = 3;
    public int Health { get; init; } = 100;
    public int MaxHealth { get; init; } = 100;
    public string WeaponName { get; init; } = "#HL2_SMG1_Menu";
}

/// <summary>On-screen text as a sample: centre print, map text and hints, registered per interface.</summary>
public sealed class FakeHudText : ICentrePrintSource, IMapTextSource, IKeyHintSource
{
    public string CenterPrint { get; init; } = string.Empty;
    public float CenterPrintAlpha { get; init; } = 1f;
    public HudTextLine[] Messages { get; init; } = [];
    public (string Key, string Text)[] Hints { get; init; } = [];

    public int MessageCount => Messages.Length;
    public HudTextLine Message(int i) => Messages[i];
    public int HintCount => Hints.Length;
    public string Hint(int i) => Hints[i].Text;
    public string HintKey(int i) => Hints[i].Key;

    public UiSampleServices Register(UiSampleServices s)
        => s.Add<ICentrePrintSource>(this).Add<IMapTextSource>(this).Add<IKeyHintSource>(this);
}

/// <summary>Chat as a sample.</summary>
public sealed class FakeChat : IChatSource
{
    public ChatLine[] Lines { get; init; } = [];
    public int Count => Lines.Length;
    public ChatLine Line(int i) => Lines[i];
}

/// <summary>The build stamp as a sample.</summary>
public sealed record FakeBuildStamp : IBuildStampSource
{
    public bool Visible { get; init; } = true;
    public string Line { get; init; } = "n 19:04:12 1a0e · m 20:31:40 4337 · client-ui@8cd02c00";
    public bool Stale { get; init; }
    public string DebugLine { get; init; } = string.Empty;
}

/// <summary>The team menu as a sample.</summary>
public sealed class FakeTeamMenu : ITeamMenuSource
{
    public string MapName { get; init; } = "dm_lockdown";
    public TeamChoice[] Choices { get; init; } = [new(2, "Combine", 5, 61, false), new(3, "Rebels", 6, 58, false)];
    public int CurrentTeam { get; init; }

    public int ChoiceCount => Choices.Length;
    public TeamChoice Choice(int i) => Choices[i];
}

/// <summary>The console as a sample: log, input and completions, registered per interface.</summary>
public sealed class FakeConsole : IConsoleLogSource, IConsoleInputSource, IConsoleCompletionSource
{
    public ConsoleLine[] Lines { get; init; } = [];
    public string Input { get; init; } = string.Empty;
    public int Caret { get; init; } = 0;
    public int SelectionStart { get; init; }
    public int SelectionLength { get; init; }
    public int HistoryCount { get; init; }
    public int HistoryIndex { get; init; } = -1;
    public ConsoleCompletion[] Completions { get; init; } = [];
    public int SelectedCompletion { get; init; } = -1;

    public int LineCount => Lines.Length;
    public ConsoleLine Line(int i) => Lines[i];
    public int CompletionCount => Completions.Length;
    public ConsoleCompletion Completion(int i) => Completions[i];

    public UiSampleServices Register(UiSampleServices s)
        => s.Add<IConsoleLogSource>(this).Add<IConsoleInputSource>(this).Add<IConsoleCompletionSource>(this);

    /// <summary>A believable session: a map load, warnings, an error, commands and their answers.</summary>
    public static ConsoleLine[] BusyLog()
    {
        var m = ConsoleSeverity.Message;
        var lines = new List<ConsoleLine>
        {
            new("Source Engine 2013 Multiplayer, SourceSharp client-ui@8cd02c00", m),
            new("Network: IP 192.168.1.20, mode MP, dedicated No, ports 27015 SV / 27005 CL", m),
            new("] map dm_lockdown", ConsoleSeverity.Echo),
            new("Map: dm_lockdown", m),
            new("Commentary: Could not find commentary data file 'maps/dm_lockdown_commentary.txt'.", ConsoleSeverity.Developer),
            new("[SourceSharp] managed runtime 10.0.0 up in 412 ms; 61 entity classes registered", m),
            new("Missing map material: maps/dm_lockdown/concrete/concretewall071a_c17", ConsoleSeverity.Warning),
            new("Precache: 473 entities, 61 classes, 1,904 models", ConsoleSeverity.Developer),
            new("SOLID_VPHYSICS static prop with no vphysics model! (models/props_c17/fence01a.mdl)", ConsoleSeverity.Warning),
            new("Connected to 127.0.0.1:27015", m),
            new("Gordon connected.", m),
            new("] sv_cheats 1", ConsoleSeverity.Echo),
            new("] cl_interp_ratio 2", ConsoleSeverity.Echo),
            new("\"cl_interp_ratio\" = \"2\" ( def. \"2\" ) min. 1.000000 max. 5.000000", m),
            new("] give weapon_ar3", ConsoleSeverity.Echo),
            new("Unknown entity class 'weapon_ar3': give failed", ConsoleSeverity.Error),
            new("] give weapon_ar2", ConsoleSeverity.Echo),
            new("Alyx killed Overwatch with ar2", m),
            new("[SourceSharp/Hud] first paint: 1920x1080, local health 100", ConsoleSeverity.Developer),
            new("Lag compensation: 12 records rewound for Overwatch", ConsoleSeverity.Developer),
            new("ConVarRef sv_skyname doesn't point to an existing ConVar", ConsoleSeverity.Warning),
            new("] status", ConsoleSeverity.Echo),
            new("hostname: SourceSharp Deathmatch | dm_lockdown 24/7", m),
            new("version : 9a05cdf2/24 9093 secure", m),
            new("map     : dm_lockdown at: -2739 x, 2563 y, 192 z", m),
            new("players : 12 humans, 4 bots (16 max)", m),
            new("# userid name                uniqueid            connected ping loss state", m),
            new("#      2 \"Gordon\"            [U:1:1234567]       12:41       18    0 active", m),
            new("#      3 \"Alyx\"              [U:1:7654321]       11:02       34    0 active", m),
            new("Error: CUtlRBTree overflow in the soundscape cache (512 entries)", ConsoleSeverity.Error),
            new("] mat_setvideomode 2560 1080 1", ConsoleSeverity.Echo),
            new("Video mode changed: 2560x1080 windowed", m),
        };
        return [.. lines];
    }
}

/// <summary>The server browser as a sample: list, query, filters and details, registered per interface.</summary>
public sealed class FakeServerBrowser : IServerListSource, IServerQuerySource, IServerFilterSource, IServerDetailsSource
{
    public ServerEntry[] Servers { get; init; } = [];
    public int Version { get; init; } = 1;
    public ServerTab Tab { get; init; }
    public bool Refreshing { get; init; }
    public float RefreshProgress { get; init; }
    public string MapContains { get; init; } = string.Empty;
    public bool NotFull { get; init; }
    public bool NotEmpty { get; init; }
    public bool NoPassword { get; init; }
    public int MaxPing { get; init; }
    public int Selected { get; init; } = -1;
    public ServerPlayer[] Players { get; init; } = [];

    public int ServerCount => Servers.Length;
    public ServerEntry Server(int i) => Servers[i];
    public int PlayerCount => Players.Length;
    public ServerPlayer Player(int i) => Players[i];

    public UiSampleServices Register(UiSampleServices s)
        => s.Add<IServerListSource>(this).Add<IServerQuerySource>(this).Add<IServerFilterSource>(this).Add<IServerDetailsSource>(this);

    private static IReadOnlyList<string> Maps { get; } =
        ["dm_lockdown", "dm_overwatch", "dm_resistance", "dm_steamlab", "dm_underpass", "dm_runoff", "dm_powerhouse",
         "ss_sandbox", "dm_crossfire", "dm_killbox_kbh", "dm_aerial", "dm_frenzy", "ctf_bluewhale", "jb_lego_jail"];

    private static IReadOnlyList<string> Names { get; } =
        ["SourceSharp Deathmatch", "Ravenholm Refugees", "City 17 | Fast DL", "[EU] Combine Overwatch", "Lambda Lockers",
         "Black Mesa East 24/7", "Nova Prospekt Night", "Resistance HQ", "Whiteforest Gaming", "Kleiner's Lab | Newbies"];

    /// <summary><paramref name="count"/> servers from a fixed sequence: names, maps, players, pings, flags.</summary>
    public static ServerEntry[] Many(int count, int seed = 3)
    {
        var rng = new Random(seed);
        var all = new ServerEntry[count];
        for (int i = 0; i < count; i++)
        {
            int max = rng.Next(4) switch { 0 => 8, 1 => 16, 2 => 24, _ => 32 };
            int players = rng.Next(5) == 0 ? 0 : rng.Next(max + 1);
            all[i] = new ServerEntry(
                Name: $"{Names[i % Names.Count]} #{i + 1}",
                Game: rng.Next(6) == 0 ? "SourceSharp" : "Half-Life 2: Deathmatch",
                Map: Maps[rng.Next(Maps.Count)],
                Players: players,
                MaxPlayers: max,
                Bots: rng.Next(4) == 0 ? rng.Next(1, 5) : 0,
                Ping: 8 + rng.Next(rng.Next(3) == 0 ? 380 : 140),
                Password: rng.Next(9) == 0,
                Secure: rng.Next(5) != 0);
        }
        return all;
    }

    public static ServerPlayer[] SomePlayers() =>
    [
        new("Gordon", 31, 42), new("Alyx", 27, 40), new("barney.calhoun", 19, 12), new("Dr. Kleiner", 14, 55),
        new("Odessa Cubbage", 11, 8), new("Vortigaunt", 9, 33), new("xXx_Headcrab_Slayer_From_Ravenholm_xXx", 4, 3),
    ];
}

/// <summary>The kill feed as a sample: its lines, oldest first.</summary>
public sealed record FakeKillFeed : IKillFeedSource
{
    public KillFeedLine[] Lines { get; init; } = [];

    public int LineCount => Lines.Length;

    public KillFeedLine Line(int i) => Lines[i];

    /// <summary>A kill as the sample data spells it: names, teams, weapon (empty for the skull).</summary>
    public static KillFeedLine Kill(string killer, int killerTeam, string victim, int victimTeam, string weapon,
                                    bool local = false, bool headshot = false)
        => new(killer, killerTeam, victim, victimTeam, weapon.Replace('_', ' ').ToUpperInvariant(), Art(weapon), false, headshot, local);

    // The sample data's icon for a weapon, as the client's KillIcons maps it (kept in step by UiKillFeedIconTests).
    private static string Art(string weapon) => weapon switch
    {
        "crossbow_bolt" => "crossbow",
        "rpg_missile" => "rpg",
        "grenade_frag" => "frag",
        "smg1_grenade" => "explosion",
        "world" or "" => "skull",
        _ => weapon,
    };

    public static KillFeedLine Suicide(string victim, int team, string weapon = "", bool local = false)
        => new("", 0, victim, team, weapon, "skull", true, false, local);
}

/// <summary>Who is speaking, as a sample.</summary>
public sealed record FakeVoice : IVoiceSource
{
    public bool LocalSpeaking { get; init; }

    public VoiceSpeaker[] Speakers { get; init; } = [];

    public int Count => Speakers.Length;

    public VoiceSpeaker Speaker(int i) => Speakers[i];
}
