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
/// Walk in, and the music changes. A Half-Life 1 idea carried into the SDK and
/// never taken out again: it types "cd play 5" at a player's console, and the
/// engine's CD player is what used to be on the other end.
///
/// <para>
/// One shot. Stock unhooks its own touch function and deletes itself the moment
/// it fires, so the volume is gone afterwards and the track cannot be changed
/// back by walking out. That is kept.
/// </para>
///
/// <para>
/// WHICH track is the "health" key, which is not a typo on anyone's part —
/// stock reads m_iHealth, because in 1998 an entity had a handful of general
/// fields and a map reused whichever one was free. A mapper writing this in
/// Hammer types a number into Health, so the key is the one to bind.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.TriggerCdaudio, ScriptedEntityKind.Brush)]
public sealed class TriggerCdAudio : TriggerVolume
{
    public TriggerCdAudio(IGameClock clock, IEngineConsole console)
        : base(clock)
    {
        Console = console;
    }

    /// <summary>the console: convars, and commands run on the server or a client.</summary>
    private readonly IEngineConsole Console;
    /// <summary>What a map may set on this entity. See <see cref="TriggerCdAudioVars"/>.</summary>
    [ScriptVars] public TriggerCdAudioVars TriggerCdAudioVars = new();

    /// <summary>
    /// What the engine's own player accepted, kept as stock's own bounds check
    /// rather than tightened: a map that writes 40 gets told so, exactly as it
    /// was before.
    /// </summary>
    private const int LowestTrack = -1;
    private const int HighestTrack = 30;

    protected override void Fire(EntityHandle toucher) => PlayTrack(toucher);

    /// <summary>
    /// Stock's Use, reached the same way it always was.
    ///
    /// It is not the use key: a trigger volume is invisible and passable, so it
    /// offers no FCAP_IMPULSE_USE and the crosshair never finds it. What reaches
    /// it is the "Use" input, which CBaseEntity declares for every entity and a
    /// map can wire like any other.
    /// </summary>
    [ScriptedEvent("Use")]
    private void OnUsed(InputData input) => PlayTrack(input.Activator);

    private void PlayTrack(EntityHandle listener)
    {
        if (TriggerCdAudioVars.Track < LowestTrack || TriggerCdAudioVars.Track > HighestTrack)
        {
            Log.Warning($"[SourceSharp] trigger_cdaudio '{Name}': track {TriggerCdAudioVars.Track} out of range\n");
            return;
        }

        // Stock finds the listener by taking edict 1 and asserting that there is
        // only one client. In this build that assertion is simply false, and a
        // debug run trips it; what it then does is play the track for whoever
        // happens to be in slot 1, which in a deathmatch game is a stranger on
        // the other side of the map or nobody at all.
        //
        // So the listener is the player who set it off. That is not an invention
        // — target_cdaudio, the same idea as a point entity, already has "in
        // multiplayer, send it back to the activator" written into it, and this
        // class is the one that was never updated to match.
        if (!Entities.IsPlayer(listener))
            return;

        Console.SendClientCommand(listener, TriggerCdAudioVars.Track == -1 ? "cd pause" : $"cd play {TriggerCdAudioVars.Track}");

        // Gone once it has fired, which is stock: SetTouch(NULL) and UTIL_Remove.
        // Removing is the whole of it here — the volume stops reporting because
        // it stops existing, so there is no separate touch function to unhook.
        Remove();
    }
}

/// <summary>
/// The map keys <see cref="TriggerCdAudio"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerCdAudioVars
{
    /// <summary>The track to play. -1 pauses. Stock's m_iHealth, and the map's "health".</summary>
    [VarName("health")]
    public int Track { get; set; }
}
