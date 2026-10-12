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
/// The screen effects a server sends to players: a shake to everyone in range,
/// a fade to one player or to all. Managed end to end (plan_side_isolation
/// §2.2a): which players a shake reaches and how strongly, the fixed-point
/// fade, and both payloads are decided here and sent as user messages.
/// </summary>
public interface IScreenEffects
{
    /// <summary>
    /// Shakes the screen of every player within <paramref name="radius"/> of
    /// <paramref name="origin"/> (0: every player), falling off linearly to
    /// nothing at the edge. A start reaches only players on the ground unless
    /// <paramref name="airShake"/>.
    /// </summary>
    void Shake(Vector origin, float amplitude, float frequency, float duration, float radius,
               ShakeCommand command = ShakeCommand.Start, bool airShake = false);

    /// <summary>
    /// Fades <paramref name="player"/>'s screen, or every player's with
    /// <see cref="FadeFlags.Purge"/> added when the handle names no player.
    /// </summary>
    void Fade(EntityHandle player, int r, int g, int b, int a, float duration, float holdTime, FadeFlags flags);
}

/// <summary>
/// Ports UTIL_ScreenShake, UTIL_ScreenFadeAll / UTIL_ScreenFadeSlot and their
/// transmit halves (stock util.cpp), with its reach rules
/// (ComputeShakeAmplitude, the on-ground gate, TransmitShakeEvent's gate). The
/// only sender of Shake and Fade: the native walks
/// (shell_vieweffects.cpp) are gone.
/// </summary>
public sealed class ScreenEffects(
    IEnginePlayers players,
    IEngineEntities entities,
    IEngineNetworking networking,
    IUserMessageSender messages) : IScreenEffects
{
    /// <summary>Stock's MAX_SHAKE_AMPLITUDE: a shake is clamped from above only.</summary>
    public const float MaxShakeAmplitude = 16.0f;

    /// <summary>SCREENFADE_FRACBITS: fade times travel as unsigned 7.9 fixed point.</summary>
    public const int FadeFractionBits = 9;

    /// <inheritdoc/>
    public void Shake(Vector origin, float amplitude, float frequency, float duration, float radius,
                      ShakeCommand command = ShakeCommand.Start, bool airShake = false)
    {
        if (amplitude > MaxShakeAmplitude)
            amplitude = MaxShakeAmplitude;

        foreach (int slot in Slots())
        {
            // Asked of the injected engine: EntityHandle.IsValid reads the
            // static one.
            var player = players.GetHandle(slot);
            if (!entities.IsValid(player))
                continue;

            bool onGround = (networking.EntityGetState(player, out _) & EntityState.OnGround) != 0;
            if (!ShakeReachesPlayer(onGround, airShake, command == ShakeCommand.Start))
                continue;

            float local = ShakeAmplitude((origin - entities.GetWorldSpaceCenter(player)).Length,
                                         amplitude, radius);

            // Outside the radius every command is ignored, a stop included.
            if (local < 0.0f)
                continue;

            if (!ShakeTransmits(local, command == ShakeCommand.Stop, out float transmit))
                continue;

            messages.Send(ShakeOf(command, transmit, frequency, duration), slot, reliable: true);
        }
    }

    /// <inheritdoc/>
    public void Fade(EntityHandle player, int r, int g, int b, int a, float duration, float holdTime, FadeFlags flags)
    {
        if (entities.IsValid(player) && entities.IsPlayer(player))
        {
            int slot = entities.GetIndex(player);
            if (slot < 1 || slot > EngineLimits.MaxPlayerSlots)
                return;

            messages.Send(FadeOf(duration, holdTime, flags, r, g, b, a), slot, reliable: true);
            return;
        }

        // No player named: everyone, and anything already fading is purged.
        var all = FadeOf(duration, holdTime, flags | FadeFlags.Purge, r, g, b, a);

        foreach (int slot in Slots())
        {
            if (entities.IsValid(players.GetHandle(slot)))
                messages.Send(all, slot, reliable: true);
        }
    }

    /// <summary>
    /// Every player slot. Stock walked 1..maxClients; a slot past the live
    /// count answers an invalid handle, so walking the engine's ceiling and
    /// skipping the empty ones reaches the same players (PointMessage does the same).
    /// </summary>
    private static IEnumerable<int> Slots()
    {
        for (int slot = 1; slot <= EngineLimits.MaxPlayerSlots; slot++)
            yield return slot;
    }

    //-------------------------------------------------------------------------
    // The reach rules (stock util.cpp's; view_effect_reach.h held them natively
    // until lane HC, 2026-10-04), and the payloads.
    //-------------------------------------------------------------------------

    /// <summary>Full strength with no radius; -1 past the edge; linear falloff inside it.</summary>
    public static float ShakeAmplitude(float distance, float amplitude, float radius)
    {
        if (radius <= 0.0f)
            return amplitude;
        if (distance > radius)
            return -1.0f;
        return amplitude * (1.0f - (distance / radius));
    }

    /// <summary>Only a starting shake asks whether the player is on the ground.</summary>
    public static bool ShakeReachesPlayer(bool onGround, bool airShake, bool startingShake)
        => onGround || airShake || !startingShake;

    /// <summary>A stop always goes, carrying zero; anything else only with some strength left.</summary>
    public static bool ShakeTransmits(float localAmplitude, bool stop, out float transmit)
    {
        if (stop)
        {
            transmit = 0.0f;
            return true;
        }

        transmit = localAmplitude;
        return localAmplitude > 0.0f;
    }

    /// <summary>FixedUnsigned16: truncated toward zero, clamped to 0..65535.</summary>
    public static ushort FixedUnsigned16(float value, int scale)
    {
        int output = (int)(value * scale);
        return (ushort)Math.Clamp(output, 0, 0xFFFF);
    }

    /// <summary>The client's MsgFunc_Shake reads: byte command, float amplitude, frequency, duration.</summary>
    public static Shake ShakeOf(ShakeCommand command, float amplitude, float frequency, float duration)
        => new() { Command = (byte)command, Amplitude = amplitude, Frequency = frequency, Duration = duration };

    /// <summary>
    /// The client's MsgFunc_Fade reads: short duration, short hold, short
    /// flags, byte r, g, b, a. Times are 7.9 fixed point; colours truncate.
    /// </summary>
    public static Fade FadeOf(float duration, float holdTime, FadeFlags flags, int r, int g, int b, int a)
        => new()
        {
            Duration = FixedUnsigned16(duration, 1 << FadeFractionBits),
            HoldTime = FixedUnsigned16(holdTime, 1 << FadeFractionBits),
            Flags    = (short)flags,
            Color    = new Color32((byte)r, (byte)g, (byte)b, (byte)a),
        };
}
