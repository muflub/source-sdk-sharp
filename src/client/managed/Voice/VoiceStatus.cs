//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Voice;

/// <summary>Stock's voice_modenable (voice_status.cpp): this client's voice on or off, told to the server.</summary>
[ConVar("voice_modenable", "1", Flags = ConVarFlags.Archive | ConVarFlags.ClientCmdCanExecute, Help = "Enable/disable voice in this mod.")]
public sealed class VoiceModEnableConVar : BoolConVar
{
}

/// <summary>Stock's voice_clientdebug (voice_status.cpp): the voice manager's console trace.</summary>
[ConVar("voice_clientdebug", "0")]
public sealed class VoiceClientDebugConVar : BoolConVar
{
}

/// <summary>What the voice manager asks of the engine: who a client index is, and a line to the server.</summary>
public interface IVoiceEngine
{
    /// <summary>The player's guid (player_info_t), or null for an empty slot.</summary>
    string? GuidOf(int client);

    /// <summary>A command to the server (ServerCmd), reliably or not.</summary>
    void ServerCommand(string line, bool reliable);

    /// <summary>voice_modenable on (stock ran "voice_modenable 1").</summary>
    void EnableVoice();
}

/// <summary>The ban list's file: read once, written on each change.</summary>
public interface IVoiceBanFile
{
    byte[]? Read();

    void Write(byte[] file);
}

/// <summary>
/// THE CLIENT'S VOICE MANAGER (stock CVoiceStatus, voice_status.cpp): who is
/// talking (the engine's VoiceStatus: a client index, -1 this client, -2 the
/// server's acknowledgement), the server's masks (VoiceMask: who this client
/// hears, whom the server has banned for it, and its idea of voice_modenable),
/// the players this client muted (VoiceBanList), and what it tells the server:
/// once a second, or when the server asks (RequestState), "VModEnable n" when
/// voice_modenable is not what the server last heard, and "vban m0 m1" (each a
/// 32-player bit mask, hex) when its mutes differ from the server's or it was
/// asked. Talking turns voice_modenable on. A mute toggles (stock's
/// SetPlayerBlockedState ignores its argument) and is written to voice_ban.dt
/// at once (stock wrote it at exit).
/// </summary>
public sealed class VoiceStatus(IVoiceEngine engine, IGameClock clock, ILog log, IVoiceBanFile file,
                                VoiceModEnableConVar? modEnable = null, VoiceClientDebugConVar? debug = null)
    : IVoiceStatusEvents, IVoiceBlockEvents
{
    /// <summary>VOICE_MAX_PLAYERS.</summary>
    public const int MaxPlayers = 64;

    /// <summary>VOICE_MAX_PLAYERS_DW.</summary>
    public const int MaskWords = 2;

    /// <summary>MAX_PLAYERS: the vban masks cover these slots.</summary>
    public const int BanSlots = 33;

    private readonly bool[] _talking = new bool[MaxPlayers];
    private readonly bool[] _enabled = new bool[MaxPlayers];
    private readonly uint[] _audible = new uint[MaskWords];
    private readonly uint[] _serverBanned = new uint[MaskWords];
    private readonly VoiceBanList _bans = Load(file);
    private int _serverModEnable = -1;
    private float _lastUpdate;

    /// <summary>
    /// Whether a level is running (stock's g_bLevelInitialized). Set, the
    /// once-a-second clock starts again: the client clock restarts with each
    /// level, and stock's update, keyed on the old level's last time, went
    /// silent until the new clock passed it.
    /// </summary>
    public bool LevelInitialized
    {
        get => _levelInitialized;
        set
        {
            if (value && !_levelInitialized)
                _lastUpdate = float.NegativeInfinity;
            _levelInitialized = value;
        }
    }

    private bool _levelInitialized;

    public bool LocalTalking { get; private set; }

    public bool ServerAcked { get; private set; }

    private bool Debug => debug is { Value: true };

    private static VoiceBanList Load(IVoiceBanFile file)
    {
        var bans = new VoiceBanList();
        if (file.Read() is { } bytes)
            bans.Load(bytes);
        return bans;
    }

    public void Status(int entIndex, bool talking)
    {
        if (Debug)
            log.Msg($"CVoiceStatus::UpdateSpeakerStatus: ent {entIndex} talking = {(talking ? 1 : 0)}\n");
        if (entIndex == -1)
        {
            LocalTalking = talking;
            if (talking)
                engine.EnableVoice();
        }
        else if (entIndex == -2)
        {
            ServerAcked = talking;
        }
        else if (entIndex > 0 && entIndex <= MaxPlayers)
        {
            _talking[entIndex - 1] = talking;
            if (talking)
                _enabled[entIndex - 1] = true;
        }
    }

    public bool IsSpeaking(int client) => client > 0 && client <= MaxPlayers && _talking[client - 1];

    public bool IsAudible(int client) => client > 0 && client <= MaxPlayers && (_audible[(client - 1) / 32] & (1u << ((client - 1) % 32))) != 0;

    /// <summary>The engine's frame: the server is told the state once a second.</summary>
    public void Frame()
    {
        if (clock.Now - _lastUpdate > 1f)
            UpdateServerState(force: false);
    }

    /// <summary>VoiceMask: the server's masks and its idea of voice_modenable.</summary>
    public void ServerMasks(uint audible0, uint banned0, uint audible1, uint banned1, bool modEnable)
    {
        (_audible[0], _serverBanned[0], _audible[1], _serverBanned[1]) = (audible0, banned0, audible1, banned1);
        _serverModEnable = modEnable ? 1 : 0;
        if (Debug)
            for (int dw = 0; dw < MaskWords; dw++)
                log.Msg($"CVoiceStatus::HandleVoiceMaskMsg\n    - m_AudiblePlayers[{dw}] = {_audible[dw]}\n    - m_ServerBannedPlayers[{dw}] = {_serverBanned[dw]}\n");
    }

    /// <summary>RequestState: the whole state, now.</summary>
    public void ServerRequestedState() => UpdateServerState(force: true);

    public bool IsBlocked(int client) => engine.GuidOf(client) is { } guid && _bans.IsBanned(VoiceBanList.IdOf(guid));

    /// <summary>The player's mute flips (stock's SetPlayerBlockedState), and the server hears of it.</summary>
    public void ToggleBlocked(int client)
    {
        if (engine.GuidOf(client) is not { } guid)
            return;
        byte[] id = VoiceBanList.IdOf(guid);
        _bans.SetBanned(id, !_bans.IsBanned(id));
        file.Write(_bans.Save());
        UpdateServerState(force: false);
    }

    private void UpdateServerState(bool force)
    {
        if (!LevelInitialized)
        {
            if (Debug)
                log.Msg("CVoiceStatus::UpdateServerState: g_bLevelInitialized\n");
            return;
        }

        int enabled = modEnable is null || modEnable.Value ? 1 : 0;
        if (force || _serverModEnable != enabled)
        {
            _serverModEnable = enabled;
            string mod = $"VModEnable {enabled}";
            engine.ServerCommand(mod, reliable: true);
            if (Debug)
                log.Msg($"CVoiceStatus::UpdateServerState: Sending '{mod}'\n");
        }

        var line = new StringBuilder("vban");
        bool changed = false;
        for (int dw = 0; dw < MaskWords; dw++)
        {
            uint banMask = 0, serverMask = 0;
            for (int i = 0; i < 32; i++)
            {
                int slot = dw * 32 + i;
                if (slot >= BanSlots)
                    break;
                if (engine.GuidOf(slot + 1) is not { } guid)
                    continue;
                if (_bans.IsBanned(VoiceBanList.IdOf(guid)))
                    banMask |= 1u << i;
                if ((_serverBanned[dw] & (1u << i)) != 0)
                    serverMask |= 1u << i;
            }
            changed |= serverMask != banMask;
            line.Append(' ').Append(banMask.ToString("x", CultureInfo.InvariantCulture));
        }
        if (changed || force)
        {
            engine.ServerCommand(line.ToString(), reliable: false);
            if (Debug)
                log.Msg($"CVoiceStatus::UpdateServerState: Sending '{line}'\n");
        }
        else if (Debug)
        {
            log.Msg("CVoiceStatus::UpdateServerState: no change\n");
        }
        _lastUpdate = clock.Now;
    }
}
