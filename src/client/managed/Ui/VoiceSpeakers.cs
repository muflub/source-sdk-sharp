//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>Who a client index is, for the voice panel: name, team and life.</summary>
public interface IVoicePlayers
{
    bool TryFind(int client, out string name, out int team, out bool alive);
}

/// <summary>
/// WHO IS SPEAKING (stock CHudVoiceStatus::OnThink and CHudVoiceSelfStatus,
/// hud_voicestatus.cpp, over CVoiceStatus::UpdateSpeakerStatus): the engine's
/// VoiceStatus events as the shell forwards them. A player who starts is added
/// at the end; one who stops is gone (HL2MP's fade_out_time is 0); -1 is the
/// local player (the self icon), -2 the server's acknowledgement (ignored here
/// as stock's panels did). Names, teams and life are read when asked.
/// </summary>
public sealed class VoiceSpeakers(IVoicePlayers players) : IVoiceSource, IVoiceStatusEvents
{
    /// <summary>VOICE_MAX_PLAYERS.</summary>
    public const int MaxPlayers = 64;

    private readonly int[] _order = new int[MaxPlayers];
    private int _count;

    public bool LocalSpeaking { get; private set; }

    public int Count => _count;

    public VoiceSpeaker Speaker(int i)
        => players.TryFind(_order[i], out string name, out int team, out bool alive)
            ? new VoiceSpeaker(name, team, alive)
            : new VoiceSpeaker("", 0, true);

    public void Status(int entIndex, bool talking)
    {
        if (entIndex == -1)
        {
            LocalSpeaking = talking;
            return;
        }
        if (entIndex <= 0 || entIndex > MaxPlayers)
            return;
        int at = Array.IndexOf(_order, entIndex, 0, _count);
        if (talking && at < 0)
            _order[_count++] = entIndex;
        else if (!talking && at >= 0)
        {
            Array.Copy(_order, at + 1, _order, at, _count - at - 1);
            _count--;
        }
    }
}

/// <summary>A client index's name, team and life, over the player resource and the engine's player info.</summary>
public sealed class VoicePlayers(IClientStandalones standalones, IPlayerNames names) : IVoicePlayers
{    private readonly string[] _names = new string[PlayerResourceNetState.Slots];
    private readonly int[] _nameUserIds = new int[PlayerResourceNetState.Slots];

    // A name is asked of the engine once per user in the slot, not each frame.
    public bool TryFind(int client, out string name, out int team, out bool alive)
    {
        (name, team, alive) = ("", 0, true);
        var resource = standalones.OfClass(EntityNames.PlayerManager).OfType<Standalone.ClientPlayerResource>().FirstOrDefault();
        if (resource is null || client <= 0 || client >= PlayerResourceNetState.Slots
            || !resource.State.HasFlag(client, PlayerResourceNetState.FlagConnected))
            return false;
        team = resource.State.Team[client];
        alive = resource.State.HasFlag(client, PlayerResourceNetState.FlagAlive);
        int userId = resource.State.UserId[client];
        if (_names[client] is null || _nameUserIds[client] != userId)
        {
            _names[client] = names.TryGet(client, out var info) ? info.Name : "";
            _nameUserIds[client] = userId;
        }
        name = _names[client];
        return true;
    }
}
