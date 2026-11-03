using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>An options page's console lines, run as the stock options ran theirs (ClientCmd_Unrestricted).</summary>
public sealed class EngineCommandLine(IFwdEngineClient engine) : IUiCommandLine
{
    public void Submit(string line) => engine.SubmitConsoleLine(line);
}

/// <summary>The microphone's volume and boost through the engine's voice tweak controls (ivoicetweak.h's MicrophoneVolume and MicBoost).</summary>
public sealed class EngineMicrophone(IVoiceTweakControls controls) : IMicrophoneSettings
{
    private const int MicrophoneVolume = 0, MicBoost = 2;

    public bool Available => controls.HasVoiceTweak;

    public float Volume
    {
        get => controls.VoiceControl(MicrophoneVolume);
        set => controls.SetVoiceControl(MicrophoneVolume, value);
    }

    public bool Boost
    {
        get => controls.VoiceControl(MicBoost) != 0f;
        set => controls.SetVoiceControl(MicBoost, value ? 1f : 0f);
    }
}

/// <summary>The video options' lists, from the engine's GameUI funcs and the material system's config.</summary>
public sealed class EngineVideoModes(IFwdGameUIFuncs funcs, IFwdMaterialSystem materials) : IVideoModeSource
{
    public IReadOnlyList<UiVideoMode> Modes
    {
        get
        {
            var modes = funcs.VideoModes();
            var list = new UiVideoMode[modes.Length];
            for (int i = 0; i < modes.Length; i++)
                list[i] = new UiVideoMode(modes[i].Width, modes[i].Height);
            return list;
        }
    }

    public UiVideoMode Desktop
    {
        get
        {
            var (w, h) = funcs.DesktopResolution();
            return new UiVideoMode(w, h);
        }
    }

    public UiVideoMode Current
    {
        get
        {
            var config = materials.CurrentVideoConfig();
            return new UiVideoMode(config.Width, config.Height);
        }
    }

    public bool Windowed => materials.CurrentVideoConfig().Windowed;

    public bool SupportsMsaa(int samples) => materials.SupportsMsaa(samples);

    public bool SupportsCsaa(int samples, int quality) => materials.SupportsCsaa(samples, quality);
}

/// <summary>The game's files by wildcard over the GAME search path.</summary>
public sealed class EngineFileList(IFwdFileFinder finder) : IUiFileList
{
    public IReadOnlyList<string> Find(string wildcard) => finder.Find(wildcard, "GAME");

    public IReadOnlyList<string> Directories(string wildcard) => finder.Find(wildcard, "GAME", directories: true);
}

/// <summary>
/// The create-server map list (stock's LoadMaps): maps/*.bsp on the GAME
/// paths, by name, less the gameinfo's hidden_maps; sorted, as the paths
/// answer in no useful order. The game's name is gameinfo's "game".
/// </summary>
public sealed class EngineMapList(IFwdFileFinder finder, string? gameInfoText) : IMapListSource
{
    public string GameName => Info()?.GetString("game", null) is { Length: > 0 } name ? name : "Source";

    public IReadOnlyList<string> Maps()
    {
        var hidden = Info()?.Find("hidden_maps");
        var maps = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in finder.Find("maps/*.bsp", "GAME"))
        {
            if (!file.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase))
                continue;
            string name = file[..^4];
            if (hidden?.GetInt(name, 0) is not (null or 0))
                continue;
            maps.Add(name);
        }
        return [.. maps];
    }

    private KeyValuesText? Info()
    {
        if (gameInfoText is null)
            return null;
        try
        {
            return KeyValuesText.Parse(gameInfoText).Find("GameInfo");
        }
        catch (KeyValuesTextException)
        {
            return null;
        }
    }
}

/// <summary>
/// Starts the server as stock's create-server OK did: drop any game, a LAN
/// server with the chosen size, password and name, the progress dialog, the
/// map. Stock also reverted the replicated and cheat convars first, which
/// the engine's console offers no command for: not done.
/// </summary>
public sealed class CreateServerActions(IUiCommandLine commands) : ICreateServerActions
{
    public void Start(string map, int maxPlayers, string hostName, string password)
    {
        commands.Submit("disconnect");
        commands.Submit("wait");
        commands.Submit("wait");
        commands.Submit("sv_lan 1");
        commands.Submit("setmaster enable");
        commands.Submit(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"maxplayers {maxPlayers}"));
        commands.Submit("sv_password \"" + password.Replace("\"", "", StringComparison.Ordinal) + "\"");
        commands.Submit("hostname \"" + hostName.Replace("\"", "", StringComparison.Ordinal) + "\"");
        commands.Submit("progress_enable");
        commands.Submit("map " + map);
    }
}

/// <summary>
/// The player list over the players' (filtered) names and this client's
/// voice mutes (stock's CPlayerListDialog over GetPlayerInfo and the game
/// client exports' mute calls): every client slot with a player, in slot order.
/// </summary>
public sealed class PlayerListUi(IPlayerNames names, IFwdEngineClient engine, IEngineEnvironment environment,
                                 IVoiceBlockEvents mutes)
    : IPlayerListSource
{
    // MAX_PLAYERS, when the globals are not up yet (stock walked GetMaxClients()).
    private const int SlotCeiling = 33;

    private readonly List<(int Slot, PlayerListEntry Entry)> _players = [];

    public int Count => _players.Count;

    public PlayerListEntry Player(int i) => _players[i].Entry;

    public void Refresh()
    {
        _players.Clear();
        var globals = environment.Globals;
        int max = globals.IsAvailable && globals.MaxClients > 0 ? globals.MaxClients : SlotCeiling - 1;
        int local = engine.LocalPlayerSlot();
        for (int slot = 1; slot <= max; slot++)
            if (names.TryGet(slot, out var info))
                _players.Add((slot, new PlayerListEntry(info.Name, mutes.IsBlocked(slot), info.Fake, slot == local)));
    }

    public void ToggleMute(int i)
    {
        if ((uint)i >= (uint)_players.Count)
            return;
        var (slot, entry) = _players[i];
        if (entry.Bot || entry.Local)
            return;
        mutes.ToggleBlocked(slot);
        _players[i] = (slot, entry with { Muted = mutes.IsBlocked(slot) });
    }
}
