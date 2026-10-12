//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Sound envelopes — <c>CSoundControllerImp</c>, in C#.
///
/// <para>
/// plan_gamedll_after.md Phase A3, second pair. It was row 13 of
/// <c>kServerSystems[]</c> and <c>game/shared/soundenvelope.cpp</c> was in
/// <c>server_csharp.vpc</c>; neither is true now. What answers
/// <c>CSoundEnvelopeController::GetController()</c> on the server is a shim
/// (<c>shell_soundenvelope.cpp</c>) forwarding here, so the six compiled call
/// sites — five in <c>physics.cpp</c>'s friction sounds and one in
/// <c>gameinterface.cpp</c> — are unedited.
/// </para>
///
/// <para>
/// <b>An ENVELOPE is a value approaching a target at a rate.</b> That is the
/// whole of what moved: volume and pitch each ramp toward a target over a
/// number of seconds, and a queue schedules further ramps ahead of time. None
/// of it touches the engine. What did NOT move is everything that does: a
/// recipient filter, an entity handle, a sound-script lookup and an
/// <c>EmitSound_t</c> all stayed in the shim, and a patch crosses as an
/// <c>int</c> handle so that a pointer to one is never seen here.
/// </para>
///
/// <para>
/// <b>The CLIENT still has its own native copy, and must.</b>
/// <c>soundenvelope.cpp</c> compiles into <c>client.so</c> as well, and its
/// class was already split down the middle by <c>#ifdef CLIENT_DLL</c>. The
/// file left the SERVER's project rather than the split being carved deeper,
/// which is T1's lesson applied rather than risked.
/// </para>
///
/// <para>
/// <b>Every rule below is stock's, including the odd ones.</b> The rate floor
/// of 0.1 that makes a very small ramp take longer than asked; the volume clamp
/// at 1.0 on the target and not on the current value; the "force update" flag
/// that makes ONE more emit happen after a ramp has arrived; and
/// <c>SND_CHANGE_VOL</c> being set on every emit whatever changed, because the
/// envelope is a MULTIPLIER on the script's volume and an emit without it would
/// use the script's number raw.
/// </para>
/// </summary>
public sealed class SoundController : SoundEnvelopeController, IServerSoundController, IFrameLifecycle, IGameLifecycle
{
    public SoundController(IEngineSound patches, IGameClock clock, IEngineHost host, IEngineConsole console)
        : base(patches, clock, host, console)
    {
    }

    // ---- placement (the controller itself is SourceSharp.Shared.SoundEnvelopeController, which the client shares) ----

    /// <summary>
    /// Stock's row overrode <c>PreClientUpdate</c> for its per-frame work
    /// (docs/gamedll-ledger.md's measured stage matrix), which is the LAST
    /// point of the server frame — after the entities have thought and before
    /// the engine builds each client's update.
    /// </summary>
    int IFrameLifecycle.FrameOrder => (int)FrameStage.SoundController;

    /// <summary>And <c>LevelShutdownPreEntity</c> for its teardown.</summary>
    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.SoundController;

    void IFrameLifecycle.OnPreClientUpdate(float frameTime) => SystemUpdate();

    void IGameLifecycle.OnLevelShutdownPreEntity() => SystemReset();

}
