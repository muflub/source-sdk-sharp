//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of <c>color_correction_volume</c> — and, in this build, the
/// half that owns the weight.
///
/// <para>
/// Stock computed the weight on the server by watching "the local player"
/// cross the brush, which is a per-viewer question asked on the machine that
/// has no viewer: <c>UTIL_GetLocalPlayer()</c> is NULL in multiplayer and the
/// networked weight never left zero. The crossing test consumes exactly one
/// fact — where MY player is standing this frame — so it sits in the same
/// bucket as <see cref="ClientColorCorrection"/>'s distance falloff and moves
/// to the same place. The server sends the volume's box, the lookup, the
/// ceiling and the fade time; this class decides, per viewer, how far in the
/// fade is.
/// </para>
///
/// <para>
/// The fade is a constant-rate ramp: the full journey between zero and
/// <c>MaxWeight</c> takes <c>FadeDuration</c> seconds, and a crossing mid-fade
/// reverses from the current weight at the same rate — so stepping out one
/// second into a ten-second fade takes one second to undo, which is stock's
/// intent stated without stock's arithmetic (whose interrupted fades take
/// <c>(1-w)²·D</c> rather than <c>(1-w)·D</c>, an artefact nobody would ask
/// for). Two smaller corrections while porting, both in docs/divergences.md:
/// the box is the brush's axis-aligned bounds against the player's origin
/// rather than a hull-against-planes trigger test, and <c>maxweight</c> is
/// honoured where stock parses it and fades to a flat 1.0 anyway.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.ColorCorrectionVolume)]
public sealed class ClientColorCorrectionVolume : ClientStandaloneEntity, IWeightedCorrection
{
    /// <summary>what can be seen, and how it is lit.</summary>
    private readonly IEngineVisibility Visibility;

    /// <summary>what the client thinks the time is. Frame deltas only.</summary>
    private readonly IGameClock Clock;

    private readonly IColorCorrections _corrections;

    public ClientColorCorrectionVolume(IColorCorrections corrections, IEngineEntityRender render,
                                       IEngineVisibility visibility, IGameClock clock)
        : base(render)
    {
        _corrections = corrections;
        Visibility = visibility;
        Clock = clock;
    }

    public override string ClassName => EntityNames.ColorCorrectionVolume;

    [NetState] public ColorCorrectionVolumeNetState State = new();

    /// <summary>The manager's lookup, or 0 if it never loaded.</summary>
    private int _lookup;

    /// <summary>How far in the fade is, between 0 and <c>State.MaxWeight</c>. This viewer's alone.</summary>
    private float _weight;

    /// <summary>When the last reweigh ran, for the frame delta.</summary>
    private float _lastTime;

    /// <summary>
    /// Loads the lookup on the first update that carries a filename — the same
    /// dance as <see cref="ClientColorCorrection.OnDataChanged"/>, and for the
    /// same reason: this side has no state at all until an update arrives.
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (_lookup != 0 || State.LookupFile.Length == 0)
            return;

        _lookup = Visibility.ColorCorrectionAdd(State.LookupFile);

        if (_lookup == 0)
        {
            Log.Warning(
                $"[SourceSharp/Client] color_correction_volume: lookup '{State.LookupFile}' would not load\n");
            return;
        }

        _lastTime = Clock.Now;
        _corrections.Add(this);
    }

    /// <summary>
    /// Not optional, exactly as on the point entity: the manager holds the
    /// lookup until told otherwise, and a volume whose entity has gone would
    /// tint the map for the rest of the level.
    /// </summary>
    public override void OnRemoved()
    {
        _corrections.Remove(this);

        if (_lookup != 0)
        {
            Visibility.ColorCorrectionRemove(_lookup);
            _lookup = 0;
        }
    }

    /// <summary>
    /// One frame of the crossing fade. Called by <see cref="ColorCorrections"/>
    /// with the local player's origin — the one per-viewer input the whole
    /// entity turns on.
    /// </summary>
    public void Reweigh(Vector playerOrigin)
    {
        if (_lookup == 0)
            return;

        float now = Clock.Now;
        float dt  = Math.Max(now - _lastTime, 0f);
        _lastTime = now;

        // Off is off NOW — stock's think writes a hard zero every tick while
        // disabled, so Disable has never been a fade.
        float target = State.Enabled && Contains(playerOrigin) ? State.MaxWeight : 0f;

        if (State.FadeDuration > 0f && State.MaxWeight > 0f)
        {
            float step = State.MaxWeight * (dt / State.FadeDuration);
            _weight = _weight < target
                ? Math.Min(_weight + step, target)
                : Math.Max(_weight - step, target);
        }
        else
        {
            _weight = target;
        }

        Visibility.ColorCorrectionSetWeight(_lookup, _weight);
    }

    /// <summary>
    /// Whether the local player is inside the volume: their origin against the
    /// brush's world-space box. The origin is the feet, so this reads as "am I
    /// standing in it", which is what a mapper drew the brush to mean.
    /// </summary>
    private bool Contains(Vector point)
        => point.X >= State.BoundsMin.X && point.X <= State.BoundsMax.X
        && point.Y >= State.BoundsMin.Y && point.Y <= State.BoundsMax.Y
        && point.Z >= State.BoundsMin.Z && point.Z <= State.BoundsMax.Z;
}
