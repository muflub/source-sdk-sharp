//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Globalization;

using SourceSharp.Abi;

namespace SourceSharp.Client.Soundscapes;

/// <summary>
/// The arguments a stock CCommand would have split <paramref name="args"/>
/// into: whitespace-separated, a double-quoted run kept whole.
/// </summary>
internal static class SoundscapeArgs
{
    public static string[] Split(string args)
    {
        var parts = new System.Collections.Generic.List<string>();
        int i = 0;
        while (i < args.Length)
        {
            while (i < args.Length && char.IsWhiteSpace(args[i]))
                i++;
            if (i >= args.Length)
                break;

            if (args[i] == '"')
            {
                int end = args.IndexOf('"', i + 1);
                if (end < 0)
                    end = args.Length;
                parts.Add(args[(i + 1)..end]);
                i = end + 1;
            }
            else
            {
                int start = i;
                while (i < args.Length && !char.IsWhiteSpace(args[i]))
                    i++;
                parts.Add(args[start..i]);
            }
        }

        return parts.ToArray();
    }
}

/// <summary>
/// <c>playsoundscape name [radius]</c>: force a soundscape (stock's
/// <c>DEFAULT_SOUND_RADIUS</c> 36 when no radius is given); with no name,
/// report the current one. Stock's name completion is not carried
/// (docs/absence-client.md).
/// </summary>
[ConCommand("playsoundscape", Help = "Forces a soundscape to play", Flags = ConVarFlags.Cheat)]
public sealed class PlaySoundscapeCommand(ClientSoundscapes soundscapes) : IConCommand
{
    public void Execute(string args)
    {
        string[] argv = SoundscapeArgs.Split(args);
        if (argv.Length < 1)
        {
            soundscapes.DevReportSoundscapeName(soundscapes.CurrentSoundscape);
            return;
        }

        float radius = argv.Length > 1
            ? (float)Atof(argv[1])
            : ClientSoundscapes.DefaultSoundRadius;
        soundscapes.Force(argv[0], radius);
    }

    // atof: the leading number, 0 for none.
    private static double Atof(string s)
    {
        int end = 0;
        while (end < s.Length && (char.IsAsciiDigit(s[end]) || s[end] is '.' or '-' or '+' or 'e' or 'E'))
            end++;
        for (; end > 0; end--)
        {
            if (double.TryParse(s[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
        }

        return 0;
    }
}

/// <summary><c>stopsoundscape</c>: stop all soundscape processing and fade the loops.</summary>
[ConCommand("stopsoundscape", Help = "Stops all soundscape processing and fades current looping sounds", Flags = ConVarFlags.Cheat)]
public sealed class StopSoundscapeCommand(ClientSoundscapes soundscapes) : IConCommand
{
    public void Execute(string args) => soundscapes.Stop();
}

/// <summary>
/// <c>cl_soundscape_flush</c>: re-read the scripts, keeping the pushed
/// soundscape. It cannot add or remove soundscapes, since the server's index
/// is not flushed with it, only change their parameters.
/// </summary>
[ConCommand("cl_soundscape_flush", Help = "Flushes the client side soundscapes",
            Flags = ConVarFlags.ServerCanExecute | ConVarFlags.Cheat)]
public sealed class SoundscapeFlushCommand(ClientSoundscapes soundscapes, ClientSoundscapeDriver driver) : IConCommand
{
    public void Execute(string args) => soundscapes.Flush(driver.Load(driver.MapName));
}

/// <summary><c>cl_soundscape_printdebuginfo</c>: every soundscape by index, the forced one and the current one.</summary>
[ConCommand("cl_soundscape_printdebuginfo", Help = "print soundscapes")]
public sealed class SoundscapePrintDebugInfoClientCommand(ClientSoundscapes soundscapes, IEngineConsole console) : IConCommand
{
    public void Execute(string args)
    {
        var library = soundscapes.Library;
        var text = new System.Text.StringBuilder("\n------- CLIENT SOUNDSCAPES -------\n");
        for (int i = 0; i < library.Count; i++)
            text.Append($"- {i}: {library.NameByIndex(i)}\n");
        if (soundscapes.ForcedIndex >= 0)
            text.Append($"- PLAYING DEBUG SOUNDSCAPE: {soundscapes.ForcedIndex} [{library.NameByIndex(soundscapes.ForcedIndex)}]\n");
        int current = soundscapes.Params.Index;
        text.Append($"- CURRENT SOUNDSCAPE: {current} [{library.NameByIndex(current)}]\n");
        text.Append("----------------------------------\n\n");
        console.Msg(text.ToString());
    }
}

/// <summary>
/// <c>cs_cl_soundscape_report</c>, plan_entity.md 7e's gate instrument: how
/// many soundscapes are parsed, which one is current, how many of its sounds
/// run, how many pushes arrived, and the forced index. One line, the format
/// tools/7e-particle-sound-gate reads.
/// </summary>
[ConCommand("cs_cl_soundscape_report",
            Help = "plan_entity.md 7e: how many soundscapes are parsed, which one is current, how many of its sounds are running, and whether anything has ever pushed one")]
public sealed class SoundscapeReportClientCommand(ClientSoundscapes soundscapes, IEngineConsole console) : IConCommand
{
    public void Execute(string args)
    {
        int current = soundscapes.CurrentSoundscape;
        string name = soundscapes.Library.NameByIndex(current) ?? "none";
        if (current < 0)
            name = "none";
        console.Msg($"[SourceSharp] soundscape report: parsed {soundscapes.Library.Count} current {current} [{name}] " +
                    $"looping {soundscapes.LoopingCount} random {soundscapes.RandomCount} pushed {soundscapes.Pushes} " +
                    $"forced {soundscapes.ForcedIndex}\n");
    }
}
