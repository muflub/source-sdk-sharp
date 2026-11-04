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
/// <c>playsoundscape name [radius]</c>: force a soundscape (stock's
/// <c>DEFAULT_SOUND_RADIUS</c> 36 when no radius is given); with no name,
/// report the current one. The name completes as stock's did, from the parsed soundscapes.
/// </summary>
[ConCommand("playsoundscape", Help = "Forces a soundscape to play", Flags = ConVarFlags.Cheat)]
public sealed class PlaySoundscapeCommand(ClientSoundscapes soundscapes)
{
    private IEnumerable<string> Names()
        => Enumerable.Range(0, soundscapes.Library.Count).Select(i => soundscapes.Library.ByIndex(i)!.Name);

    public void Execute([Complete(From = nameof(Names))] string? name = null, float radius = ClientSoundscapes.DefaultSoundRadius)
    {
        if (name is null)
            soundscapes.DevReportSoundscapeName(soundscapes.CurrentSoundscape);
        else
            soundscapes.Force(name, radius);
    }
}

/// <summary><c>stopsoundscape</c>: stop all soundscape processing and fade the loops.</summary>
[ConCommand("stopsoundscape", Help = "Stops all soundscape processing and fades current looping sounds", Flags = ConVarFlags.Cheat)]
public sealed class StopSoundscapeCommand(ClientSoundscapes soundscapes)
{
    public void Execute() => soundscapes.Stop();
}

/// <summary>
/// <c>cl_soundscape_flush</c>: re-read the scripts, keeping the pushed
/// soundscape. It cannot add or remove soundscapes, since the server's index
/// is not flushed with it, only change their parameters.
/// </summary>
[ConCommand("cl_soundscape_flush", Help = "Flushes the client side soundscapes",
            Flags = ConVarFlags.ServerCanExecute | ConVarFlags.Cheat)]
public sealed class SoundscapeFlushCommand(ClientSoundscapes soundscapes, ClientSoundscapeDriver driver)
{
    public void Execute() => soundscapes.Flush(driver.Load(driver.MapName));
}

/// <summary><c>cl_soundscape_printdebuginfo</c>: every soundscape by index, the forced one and the current one.</summary>
[ConCommand("cl_soundscape_printdebuginfo", Help = "print soundscapes")]
public sealed class SoundscapePrintDebugInfoClientCommand(ClientSoundscapes soundscapes, IEngineConsole console)
{
    public void Execute()
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
public sealed class SoundscapeReportClientCommand(ClientSoundscapes soundscapes, IEngineConsole console)
{
    public void Execute()
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
