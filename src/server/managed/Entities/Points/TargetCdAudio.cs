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
/// target_cdaudio: the point-entity half of the CD player, and the sibling of
/// <see cref="TriggerCdAudio"/>. Fire the input, and a track number is typed at
/// a player's console.
///
/// <para>
/// <b>What it does in this build is nothing, and that is measured rather than
/// assumed.</b> The whole family ends in <c>engine->ClientCommand(client, "cd
/// play N")</c>, and this engine has no <c>cd</c> command: its command table was
/// enumerated on a running game and holds 668 entries with no <c>cd</c>, no
/// <c>cd_*</c> and nothing else CD-shaped. The player went out after Half-Life 1
/// and a stuffed command the client does not recognise is discarded without a
/// word. So the input fires, the output goes over the wire, the client throws it
/// away, and no music changes — before this port and after it.
/// </para>
///
/// <para>
/// Ported anyway because the class is otherwise correct and small, and because a
/// mod that wanted background music would put it behind exactly this name: what
/// the entity DECIDES — which track, and who hears it — is now in C#, and only
/// the delivery is the engine's dead command. Swapping that delivery for
/// something this engine does have is then a one-line change in one place rather
/// than a reason to write a new entity.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.TargetCdaudio)]
public sealed class TargetCdAudio : ScriptedEntity
{
    public TargetCdAudio(IEngineConsole console)
    {
        Console = console;
    }

    /// <summary>the console: convars, and commands run on the server or a client.</summary>
    private readonly IEngineConsole Console;
    /// <summary>What a map may set on this entity. See <see cref="TargetCdAudioVars"/>.</summary>
    [ScriptVars] public TargetCdAudioVars Vars = new();

    /// <summary>What the engine's own player accepted. Stock's bounds, kept.</summary>
    private const int LowestTrack = -1;
    private const int HighestTrack = 30;

    [ScriptedEvent("ChangeCDTrack")]
    private void OnChangeCdTrack(InputData input)
    {
        if (Vars.Track < LowestTrack || Vars.Track > HighestTrack)
        {
            Log.Warning($"[SourceSharp] target_cdaudio '{Name}': track {Vars.Track} out of range\n");
            return;
        }

        // Stock has a singleplayer branch that sends to edict 1 regardless of who
        // fired the input; this build has no singleplayer, and stock's own
        // multiplayer branch already says "send it back to the activator". So
        // only the multiplayer rule is left, which is also what the trigger form
        // was corrected to when it moved.
        if (!Entities.IsPlayer(input.Activator))
        {
            Log.Warning($"[SourceSharp] target_cdaudio '{Name}': nothing to send track {Vars.Track} to; " +
                            "whatever fired this was not a player, and this entity only answers the activator\n");
            return;
        }

        Console.SendClientCommand(input.Activator, Vars.Track == -1 ? "cd pause" : $"cd play {Vars.Track}");
    }
}

/// <summary>
/// The map keys <see cref="TargetCdAudio"/> declares.
/// </summary>
[ScriptVars]
public partial class TargetCdAudioVars
{
    /// <summary>
    /// The track. -1 pauses. Spelled "track" here, where the trigger form reads
    /// stock's <c>m_iHealth</c> and answers to "health" — the two halves of the
    /// same idea never agreed on where a mapper types the number, and both
    /// spellings are kept because both are what Hammer shows.
    /// </summary>
    public int Track { get; set; }
}
