//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Frozen;
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// Which side an animation event belongs to.
/// </summary>
[Flags]
public enum AnimationEventSide
{
    /// <summary>Nobody has said. Not the same as neither side wanting it.</summary>
    Unknown = 0,

    /// <summary><c>AE_TYPE_SERVER</c>.</summary>
    Server = 1 << 0,

    /// <summary><c>AE_TYPE_CLIENT</c>.</summary>
    Client = 1 << 1,

    /// <summary><c>AE_TYPE_WEAPON</c>: routed to the weapon rather than to its carrier.</summary>
    Weapon = 1 << 2,
}

/// <summary>
/// WHICH SIDE each named animation event is for, transcribed from
/// <c>eventlist.cpp</c>.
///
/// <para>
/// This table exists because the obvious way to answer the question does not
/// work here, and the reason is worth writing down. Stock decides client-ness
/// from <c>pevent-&gt;type &amp; AE_TYPE_CLIENT</c> — but that bit is not in the
/// .mdl. What studiomdl writes is <c>AE_TYPE_NEWEVENTSYSTEM</c> and a NAME;
/// the side flags are OR-ed in later by
/// <c>SetEventIndexForSequence</c>, which looks the name up in this very list
/// and <b>writes the answer back into the loaded model</b>. This shell's
/// <c>sequence_events.h</c> deliberately does not do that — "a report that
/// mutated the model it was reporting on would be a worse thing than the
/// assumption it replaces" — so a raw table read hands managed code a type
/// with no side bits in it at all.
/// </para>
///
/// <para>
/// A dispatcher that tested the raw bit would therefore fire NOTHING, on every
/// model, forever, and every log line would agree that it was working. So the
/// lookup is done here, by name, where it can be read and checked.
/// </para>
///
/// <para>
/// <b>It is checked.</b> <c>AnimationEventCatalogTests</c> reads
/// <c>src/game/shared/eventlist.cpp</c> and asserts this table agrees with it
/// name for name and flag for flag, in both directions. A hand-maintained
/// mirror of something native owns is exactly the shape that gave this tree
/// its stale tier fixture, which was true whatever was in it because the only
/// suite using it supplied the table and read the same table back.
/// </para>
///
/// <para>
/// SIDE-NESS: bucket 5, a shared declaration. Which side an event is FOR is a
/// fact of the event list, and both halves compile against the same one.
/// </para>
/// </summary>
public static class AnimationEventCatalog
{
    /// <summary>
    /// The lowest id an OLD-SYSTEM event must carry to be the client's.
    ///
    /// <para>
    /// <c>EVENT_CLIENT</c> from <c>npcevent.h</c>. An old-system event has no
    /// name to look up, so its number is the whole of what it says about
    /// itself, and stock's client dispatcher uses exactly this test. It is why
    /// the grenade's throw — a bare 3005 — is the SERVER's event and does not
    /// fire here, while the RPG's 5001 does.
    /// </para>
    /// </summary>
    public const int FirstClientEventId = 5000;

    private static readonly FrozenDictionary<string, AnimationEventSide> Sides =
        BuildSides().ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, AnimationEventSide> BuildSides()
    {
        // DERIVED, not transcribed. The fifty names and the AE_TYPE_* mask each
        // is registered under live once, in SourceSharp.Abi's EventCatalog,
        // because that is the table the ABI carries to the native registry
        // (plan_shared.md Phase 1). This used to be a second hand-written copy
        // of the same fifty facts; two tables of side flags with nothing but a
        // test holding them together is the shape this port keeps unpicking, so
        // there is one now and this is a VIEW of it.
        //
        // Case-insensitive as before, and as stock's CStringRegistry is.
        var map = new Dictionary<string, AnimationEventSide>(
            EventCatalog.Count, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < EventCatalog.Count; i++)
        {
            map[EventCatalog.Ordered[i]] = FromNativeMask(EventCatalog.NativeTypes[i]);
        }

        return map;
    }

    /// <summary>
    /// The native <c>AE_TYPE_*</c> bits, as the managed flags enum.
    ///
    /// <para>
    /// The two numberings are unrelated on purpose: <c>AE_TYPE_CLIENT</c> is
    /// <c>1 &lt;&lt; 4</c> in <c>eventlist.h</c>, with four flags below it that
    /// nothing in either table is registered under. This is the one place the
    /// translation happens.
    /// </para>
    /// </summary>
    private static AnimationEventSide FromNativeMask(int mask)
    {
        const int AeTypeServer = 1 << 0;
        const int AeTypeWeapon = 1 << 3;
        const int AeTypeClient = 1 << 4;

        AnimationEventSide side = AnimationEventSide.Unknown;

        if ((mask & AeTypeServer) != 0) side |= AnimationEventSide.Server;
        if ((mask & AeTypeClient) != 0) side |= AnimationEventSide.Client;
        if ((mask & AeTypeWeapon) != 0) side |= AnimationEventSide.Weapon;

        return side;
    }

    /// <summary>Every name this catalog knows, for the parity check.</summary>
    public static IReadOnlyDictionary<string, AnimationEventSide> All => Sides;

    /// <summary>
    /// Which side a NAMED event is for, or <see cref="AnimationEventSide.Unknown"/>
    /// for a name nobody has registered.
    ///
    /// <para>
    /// Unknown is a real answer, not a failure. A model may carry a private
    /// event a mod invented — stock registers those at load with
    /// <c>EventList_RegisterPrivateEvent</c> and no side flags at all — and
    /// treating an unrecognised name as the client's would have this shell
    /// firing effects for events it has never heard of.
    /// </para>
    /// </summary>
    public static AnimationEventSide SideOf(string name)
        => name.Length > 0 && Sides.TryGetValue(name, out var side) ? side : AnimationEventSide.Unknown;

    /// <summary>
    /// Whether the CLIENT should act on one event of a model's table.
    ///
    /// <para>
    /// Two rules, because there are two kinds of event and they say which side
    /// they are for in entirely different ways. A NEW-system event says it by
    /// name, through <see cref="SideOf"/>. An OLD-system event has no name and
    /// says it by number: at or above <see cref="FirstClientEventId"/> it is
    /// the client's, below it the server's. Both kinds are live in the models
    /// this mod runs against — <c>v_smg1</c>'s fire carries a named
    /// <c>AE_MUZZLEFLASH</c> AND a bare 6001 — so a dispatcher that understood
    /// only one would be silently half-deaf.
    /// </para>
    /// </summary>
    public static bool IsClientEvent(in ModelAnimationEvent carried)
        => carried.IsNamed
            ? (SideOf(carried.Name) & AnimationEventSide.Client) != 0
            : carried.EventId >= FirstClientEventId;
}
