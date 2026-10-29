//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Sounds;
using SourceSharp.Client.Standalone;

namespace SourceSharp.Client;

/// <summary>
/// The client's half of <c>CBasePlayer::SetPlayerUnderwater</c>
/// (baseplayer_shared.cpp:2043), from the local player's
/// <c>UpdateUnderwaterState</c> in its PreThink: "Player.AmbientUnderWater"
/// from the player while its eyes are under the water, stopped when they come
/// up. Asked once a frame, whatever the player's life state, as stock's
/// PreThink asked it before its dying test.
/// </summary>
public sealed class UnderwaterAmbience(IClientStandalones standalones, IEngineHud screen, IClientEntitySounds sounds)
{
    /// <summary>The ambience's script.</summary>
    public const string Ambient = "Player.AmbientUnderWater";

    /// <summary>WL_Eyes.</summary>
    private const int WaterLevelEyes = 3;

    private bool _underwater;
    private EntityHandle _player;

    /// <summary>Whether the ambience is playing, for the facts.</summary>
    public bool Underwater => _underwater;

    public void Frame()
    {
        bool under = false;
        EntityHandle player = default;

        if (standalones.GetByIndex(screen.LocalPlayerIndex) is ClientSsPlayer local)
        {
            under = local.State.WaterLevel == WaterLevelEyes;
            player = local.Handle;
        }

        if (under == _underwater)
            return;

        if (under)
            sounds.EmitFrom(player, Ambient);
        else
            sounds.StopFrom(_player, Ambient);

        _underwater = under;
        _player = player;
    }
}
