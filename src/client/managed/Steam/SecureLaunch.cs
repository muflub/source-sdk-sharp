//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Buffers.Binary;
using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Steam;

/// <summary>
/// Stock's CSecureLaunchSystem and the non-TF UnsafeCmdLineProcessor
/// (game/shared/secure_command_line.cpp) in C# (plan_client_novalve C6, Steam
/// to managed; lane c6-input). Steam hands the client an UNTRUSTED line -- a
/// URL launch's command line, or a friend's rich-presence join -- and only a
/// <c>+connect</c> whose every character is on the safe list runs.
///
/// <para>
/// Stock's <c>ProcessInsecureLaunchParameters</c> has no caller in stock or
/// here, so the launch line is read only when Steam says it changed.
/// </para>
/// </summary>
public sealed class SecureLaunch(ISteamClientApi steam, ISteamLaunchCommands commands, ILog log, Func<int> developer)
{
    /// <summary><c>k_cchMaxRichPresenceValueLength</c>, GameRichPresenceJoinRequested_t's m_rgchConnect.</summary>
    public const int ConnectBytes = 256;

    /// <summary><c>sizeof( GameRichPresenceJoinRequested_t )</c>: the CSteamID, then the connect text.</summary>
    public const int JoinRequestedBytes = 8 + ConnectBytes;

    /// <summary>CSecureLaunchSystem::OnNewUrlLaunchParameters: the launch line, as Steam now has it.</summary>
    public void OnNewUrlLaunchParameters()
    {
        // Stock dereferences SteamApps() unguarded; with no Steam here there is
        // no launch line to read.
        string? line = steam.LaunchCommandLine(out int bytes);
        if (line is null)
            return;

        Process(line, bytes);
    }

    /// <summary>CSecureLaunchSystem::OnGameRichPresenceJoinRequested, over the callback's payload.</summary>
    public void OnGameRichPresenceJoinRequested(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < JoinRequestedBytes)
            return;

        string connect = ConnectOf(payload);
        if (connect.Length == 0)
            return;

        // Stock's loop: each "%20" becomes one space.
        connect = connect.Replace("%20", " ", StringComparison.Ordinal);

        // nBufferSize = V_strlen( szConCommand ) + 1
        Process(connect, Encoding.UTF8.GetByteCount(connect) + 1);
    }

    /// <summary>The friend the join came through (m_steamIDFriend), for the record.</summary>
    public static ulong FriendOf(ReadOnlySpan<byte> payload) => BinaryPrimitives.ReadUInt64LittleEndian(payload);

    /// <summary>m_rgchConnect, up to its terminator or its end.</summary>
    public static string ConnectOf(ReadOnlySpan<byte> payload)
    {
        ReadOnlySpan<byte> text = payload.Slice(8, ConnectBytes);
        int end = text.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? text : text[..end]);
    }

    /// <summary>UnsafeCmdLineProcessor: the non-TF arm.</summary>
    private void Process(string line, int bytes)
    {
        if (bytes <= 1)
        {
            DevMsg("Received empty command line from steam\n");
            return;
        }

        DevMsg($"Received command line request[{bytes}]: '{line}'\n");

        if (line.Length == 0 || line[0] != '+')
            return;

        string command = line[1..];

        // StringAfterPrefix: case-insensitive.
        if (!command.StartsWith("connect ", StringComparison.OrdinalIgnoreCase))
            return;

        if (IsSafe(command.AsSpan("connect ".Length)))
            commands.ConnectFromSteamLaunch(command);
    }

    /// <summary>BHelperCheckSafeUserCmdString: every character on stock's list.</summary>
    private static bool IsSafe(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            bool ok = c is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z'
                      or '_' or '-' or '.' or ':' or '?' or '%' or '/' or '=' or ' '
                      or '[' or ']' or '@' or '"' or '\'' or '#' or '(' or ')' or '!'
                      or '\\' or '$';
            if (!ok)
                return false;
        }
        return true;
    }

    /// <summary>DevMsg: level 1, against the engine's own <c>developer</c>.</summary>
    private void DevMsg(string line)
    {
        if (developer() >= 1)
            log.Msg(line);
    }
}
