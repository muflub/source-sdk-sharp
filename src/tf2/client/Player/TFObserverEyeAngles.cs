//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Render;
using SourceSharp.Shared;

using TF2.Shared.Player;

namespace TF2.Client.Player;

/// <summary>
/// <c>C_TFPlayer::EyeAngles</c> for the players this client does not drive
/// (c_tf_player.cpp:7281): the networked <c>m_angEyeAngles</c>, kept as
/// history and read slightly in the past as stock's
/// <c>AddVar( &amp;m_angEyeAngles, &amp;m_iv_angEyeAngles, LATCH_SIMULATION_VAR )</c>
/// (:3874) interpolates it. The observer's in-eye view looks along it.
///
/// <para>
/// Each arrival is filed when the frame first sees the carrier's decode count
/// move (<see cref="ClientTFPlayerData.Decodes"/>), stamped with the client's
/// clock rather than the entity's simulation time; the read is at
/// <c>curtime − the interpolation amount</c>, the same delay the bodies are
/// drawn with (<see cref="BoundedConVars"/>).
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class TFObserverEyeAngles(TFClientCarriers carriers, IGameClock clock, BoundedConVars bounded)
    : IObserverEyeAngles, IClientFrameThink, IOrderedHook
{
    /// <summary>Its place in the client's frame thinks, of TF2's thousand.</summary>
    public const int Order = 1_000_000 - 1_000 + 196;

    public int HookOrder => Order;

    private readonly TFEyeAngleHistory _history = new();

    /// <summary>File every carrier whose decode moved since the last frame.</summary>
    public void ClientThink(float frameTime)
    {
        float now = clock.Now;
        foreach (ClientTFPlayerData carrier in carriers.Live)
            _history.Note(carrier.State.OwnerIndex, carrier.Decodes, carrier.State.EyePitch, carrier.State.EyeYaw,
                          now, bounded.Pushed.Amount);
    }

    /// <inheritdoc/>
    public bool TryEyeAngles(int playerIndex, out QAngle angles)
        => _history.TryRead(playerIndex, clock.Now, bounded.Pushed.Amount, out angles);
}

/// <summary>
/// The eye angles' history per player: an arrival filed when its decode count
/// moves, read <c>amount</c> in the past through stock's angle interpolation
/// (<see cref="InterpolationRing"/>, a quaternion slerp).
/// </summary>
public sealed class TFEyeAngleHistory
{
    private sealed class Track
    {
        public readonly InterpolationRing Ring = new(InterpolationKind.Angle, new InterpolationArms());
        public int Decodes = int.MinValue;
    }

    private readonly Dictionary<int, Track> _byPlayer = [];

    /// <summary>File player <paramref name="player"/>'s wire words if decode <paramref name="decodes"/> is new.</summary>
    public void Note(int player, int decodes, int pitchWire, int yawWire, float now, float amount)
    {
        if (player <= 0)
            return;

        if (!_byPlayer.TryGetValue(player, out Track? track))
            _byPlayer[player] = track = new Track();

        if (track.Decodes == decodes)
            return;

        track.Decodes = decodes;
        track.Ring.NoteChanged(now, amount, now, TFEyeAngleWire.DecodePitch(pitchWire),
                               TFEyeAngleWire.DecodeYaw(yawWire), 0f);
    }

    /// <summary>The eye angles at <c>now − amount</c>, or false with none filed.</summary>
    public bool TryRead(int player, float now, float amount, out QAngle angles)
    {
        angles = default;
        if (!_byPlayer.TryGetValue(player, out Track? track)
            || !track.Ring.Interpolate(now, amount, allowExtrapolation: false, maxExtrapolationAmount: 0f,
                                       out float pitch, out float yaw, out float roll))
            return false;

        angles = new QAngle(pitch, yaw, roll);
        return true;
    }
}
