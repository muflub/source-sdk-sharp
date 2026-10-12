//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server.Entities.Soundscape;

/// <summary>
/// Stock's <c>sv_soundscape_printdebuginfo</c>, registry half — plan_server_novalve.md
/// P2.4. It printed the native registry (CSoundscapeSystem, then
/// shell_soundscape.cpp's CSoundscapeRegistry); the registry is the shared C#
/// library now, so this prints that, in INDEX order (the native dict printed in
/// its own tree order; the lines are the same set). The entity half is
/// <c>cs_sv_soundscapes</c>'s question and is answered there.
/// </summary>
[ConCommand("sv_soundscape_printdebuginfo", Help = "print soundscapes",
            Flags = ConVarFlags.DevelopmentOnly)]
public sealed class SoundscapePrintDebugInfoCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly ISoundscapes _soundscapes;
    private readonly IEnginePlayers _players;
    private readonly IEngineHost _host;

    public SoundscapePrintDebugInfoCommand(ILog log, ISoundscapes soundscapes, IEnginePlayers players, IEngineHost host)
    {

        _log = log;
        _soundscapes = soundscapes;
        _players = players;
        _host = host;
    }

    public void Execute(string args)
    {
        if (!ServerAdmin.MayRun(_players, _host))
            return;

        _log.Msg(Format(_soundscapes.Library?.All.Select(s => (s.Index, s.Name))
                       ?? Enumerable.Empty<(int, string)>()));
    }

    /// <summary>Stock's text: a header, "- index: name" per soundscape, a rule.</summary>
    public static string Format(IEnumerable<(int Index, string Name)> soundscapes)
    {
        var text = new System.Text.StringBuilder("\n------- SERVER SOUNDSCAPES -------\n");
        foreach (var (index, name) in soundscapes)
            text.Append("- ").Append(index).Append(": ").Append(name).Append('\n');
        text.Append("----------------------------------\n\n");
        return text.ToString();
    }
}
