//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared;

/// <summary>
/// The counter that says "play that again".
///
/// <para>
/// <c>ViewModelNetState.AnimationParity</c> is stock's <c>m_nAnimationParity</c>
/// and exists for stock's reason: sending the fire sequence to a viewmodel that
/// is already playing the fire sequence is not a change, so without a counter
/// the second shot of a pair is invisible. Bumping it is what restarts the
/// animation.
/// </para>
///
/// <para>
/// <b>It is three bits wide and it WRAPS</b>, and that is what makes this a
/// class rather than an <c>&amp; 7</c> written wherever it is needed. A wrapping
/// counter cannot be compared with <c>&lt;</c>: parity 0 arriving after parity 7
/// is the next shot, and parity 7 arriving after parity 0 is a stale packet.
/// Told apart wrongly in either direction the viewmodel restarts its animation
/// when it should not, or fails to when it should.
/// </para>
///
/// <para>
/// This was not hypothetical. The animation-event dispatcher fires a muzzle
/// flash when the animation restarts, and the first live burst of the SMG lit
/// 77 flashes in one second from a weapon that fires 13 times a second — and
/// 43, then 7, then 45 on identical bursts after it. The cause is upstream of
/// the flash: the local viewmodel is predicted, the prediction mispredicts
/// <c>Sequence</c> and <c>AnimationParity</c> (the log has been saying so),
/// and the server's older parity arriving after the replay had already
/// presented the newer one read as a second restart. The count was unstable
/// because the amount of disagreement is.
/// </para>
///
/// <para>
/// SIDE-NESS: bucket 5, a shared declaration. Both halves bump this counter and
/// they have to agree about its width, which is exactly the argument for the
/// bucket. Nothing here decides anything; it is arithmetic on a ring.
/// </para>
/// </summary>
public static class AnimationParity
{
    /// <summary>
    /// How many values the counter has. Three bits, as the networked field is.
    /// </summary>
    public const int Modulus = 8;

    /// <summary>The mask that wraps it.</summary>
    public const int Mask = Modulus - 1;

    /// <summary>
    /// The next value: one more, wrapped.
    /// </summary>
    public static int Next(int parity) => (parity + 1) & Mask;

    /// <summary>
    /// Whether an arriving parity is AHEAD of the one already on screen, and so
    /// a restart rather than a stale packet.
    ///
    /// <para>
    /// The standard comparison for a wrapping sequence number: the near half of
    /// the ring is ahead, the far half is behind. With eight values that puts
    /// the boundary at four — a gap of one to four is a burst that got ahead of
    /// the last look, and five to seven is a value from before it.
    /// </para>
    ///
    /// <para>
    /// The same parity is neither, and answers false: it is not a restart, and
    /// a caller that treated "not ahead" as "act on it" would restart the
    /// animation on every repeat of the value it is already showing.
    /// </para>
    /// </summary>
    public static bool IsAhead(int shown, int arriving)
    {
        int gap = (arriving - shown) & Mask;

        return gap != 0 && gap <= Modulus / 2;
    }
}
