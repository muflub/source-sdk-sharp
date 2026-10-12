//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The name half of stock's <c>CTeamplayRules::ClientSettingsChanged</c>
/// (teamplay_gamerules.cpp; CHL2MPRules derives from it): when a client's
/// userinfo changes and its <c>name</c> convar differs, case-sensitively, from
/// the player's name, and the player had a name (not the first fill), fire
/// <c>player_changename</c> (userid, oldname, newname) and take the new name.
///
/// <para>
/// The player's name is the one ClientPutInServer was handed, kept here per
/// slot; the native player carries out SetPlayerName when
/// <see cref="Changed"/> answers true. Not ported: CHL2MPRules' own half
/// (cl_playermodel changes and their cooldown) and the haptics key.
/// </para>
/// </summary>
public sealed class PlayerNames(IEngineGameEvents events, IConnectedClients? clients = null)
{
    public const string EventName = "player_changename";

    private readonly Dictionary<int, string> _names = new();

    /// <summary>How many player_changename events fired. For the dev API and tests.</summary>
    public int Fired { get; private set; }

    /// <summary>ClientPutInServer: the slot's player is named <paramref name="name"/>.</summary>
    public void PutInServer(int entIndex, string name) => _names[entIndex] = name;

    /// <summary>The player's current name, or empty when none is known.</summary>
    public string NameOf(int entIndex) => _names.TryGetValue(entIndex, out var n) ? n : string.Empty;

    /// <summary>
    /// ClientSettingsChanged. True when the player takes a new name (the
    /// native SetPlayerName).
    /// </summary>
    public bool Changed(int entIndex)
        => Rename(entIndex, clients?.ClientConVar(entIndex, "name") ?? string.Empty);

    /// <summary>
    /// The rule, for a name the client asked for. <see cref="Changed"/> reads it
    /// from the client's <c>name</c> convar; cs_sv_rename hands it in, because
    /// a Steam client's engine puts its persona name back on every change.
    /// </summary>
    public bool Rename(int entIndex, string name)
    {
        string old = NameOf(entIndex);

        // "msg everyone if someone changes their name, and it isn't the first
        // time (changing no name to current name)"; case sensitive.
        if (old.Length == 0 || string.Equals(old, name, StringComparison.Ordinal))
            return false;

        IntPtr e = events.EventCreate(EventName);
        if (e != IntPtr.Zero)
        {
            events.EventSetInt(e, "userid", clients?.UserId(entIndex) ?? 0);
            events.EventSetString(e, "oldname", old);
            events.EventSetString(e, "newname", name);
            if (events.EventFire(e))
                Fired++;
        }

        _names[entIndex] = name;
        return true;
    }
}
