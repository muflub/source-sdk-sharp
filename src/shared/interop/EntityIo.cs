//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.Loader;

namespace SourceSharp.Abi;

/// <summary>
/// ONE CONNECTION A MAPPER DREW — <c>CEventAction</c>, in managed code
/// (<c>plan_entity.md</c> step 2a, §3.5: "outputs are fields holding connection
/// lists parsed from the map").
///
/// <para>
/// A map writes a connection as an ordinary keyvalue whose KEY is the output's
/// name and whose VALUE is this: <c>target,input,parameter,delay,times</c>. The
/// native shell used to parse it into a <c>CEventAction</c> list hanging off
/// <c>COutputEvent m_Outputs[]</c> — native memory a hot reload could not
/// reach, which is exactly what <see cref="Output.Restore"/>'s remarks used to
/// record as a limitation. It is a managed record now, so the connection, its
/// remaining firings and any event already in flight all survive a reload.
/// </para>
///
/// <para>
/// <b>THE DELIMITER IS ESC BEFORE IT IS A COMMA.</b>
/// <c>VMF_IOPARAM_STRING_DELIMITER</c> is <c>0x1b</c>
/// (<c>public/fgdlib/entitydefs.h:19</c>) so that an I/O parameter may itself
/// contain commas; stock only falls back to <c>','</c> when the string contains
/// no ESC at all. Choosing one unconditionally is the difference between
/// <c>game_text</c> receiving "Hello, world" and receiving "Hello".
/// </para>
/// </summary>
/// <param name="Target">
/// The <c>targetname</c> the connection aims at. May be a procedural name
/// (<c>!activator</c> and friends — see <see cref="EntityIoTarget"/>), a
/// trailing-wildcard prefix, or, failing both, a classname:
/// <c>CEventQueue::ServiceEvents</c> falls back to a classname search when a
/// name search found nothing, and so does <see cref="EntityIoQueue"/>.
/// </param>
/// <param name="TargetInput">
/// The input to fire. <c>"Use"</c> when the map left it blank, which is stock's
/// default in <c>CEventAction::CEventAction</c> and not an invention here.
/// </param>
/// <param name="Parameter">
/// The parameter override, or <c>null</c> when the map wrote none. Null and
/// empty are NOT the same: an override present but empty replaces the fired
/// value with an empty string, and a missing one passes the fired value
/// through. Stock draws that line with <c>NULL_STRING</c>.
/// </param>
/// <param name="Delay">Seconds to hold the event before delivering it.</param>
/// <param name="TimesToFire">
/// How many firings are left, or <see cref="FireAlways"/>. Stock decrements
/// this on the action itself and unlinks the action at zero, which is why this
/// record is replaced rather than mutated: see
/// <see cref="EntityConnections.Fire"/>.
/// </param>
/// <param name="IdStamp">
/// A unique number per connection, stock's <c>m_iIDStamp</c>. It travels with
/// the event as its output id and is what the SDK's I/O debugging identifies a
/// firing by.
/// </param>
public readonly record struct EventAction(
    string Target,
    string TargetInput,
    string? Parameter,
    float Delay,
    int TimesToFire,
    int IdStamp)
{
    /// <summary><c>EVENT_FIRE_ALWAYS</c> — <c>game/server/entityoutput.h:21</c>.</summary>
    public const int FireAlways = -1;

    /// <summary><c>VMF_IOPARAM_STRING_DELIMITER</c> — <c>fgdlib/entitydefs.h:19</c>.</summary>
    public const char VmfDelimiter = '\x1b';

    /// <summary>The delimiter stock would choose for one action string.</summary>
    /// <remarks>
    /// <c>CEventAction::CEventAction</c>: ESC unless the string contains none,
    /// in which case a comma. Exposed so a test can pin the choice rather than
    /// infer it from a parse.
    /// </remarks>
    public static char DelimiterFor(string actionData)
        => actionData.IndexOf(VmfDelimiter) >= 0 ? VmfDelimiter : ',';

    /// <summary>
    /// Parses one map connection string.
    ///
    /// <para>
    /// Deliberately total: every arm of stock's parse has a default and none of
    /// them refuses. A malformed connection in a map becomes a connection that
    /// aims at nothing, exactly as it does today, rather than an exception
    /// during a map load.
    /// </para>
    /// </summary>
    /// <param name="actionData">The connection, as the map wrote it.</param>
    /// <param name="stamps">
    /// Where its id stamp comes from: the side's <see cref="EventIdStamps"/>,
    /// which outlives a managed reload (P1 run 3, lifetime ruling b).
    /// </param>
    public static EventAction Parse(string? actionData, EventIdStamps stamps)
    {
        ArgumentNullException.ThrowIfNull(stamps);

        if (string.IsNullOrEmpty(actionData))
            return new EventAction(string.Empty, "Use", null, 0f, FireAlways, stamps.Next());

        char delimiter = DelimiterFor(actionData);
        string[] token = actionData.Split(delimiter);

        string At(int i) => i < token.Length ? token[i] : string.Empty;

        string target = At(0);
        string input = At(1).Length > 0 ? At(1) : "Use";
        string? parameter = At(2).Length > 0 ? At(2) : null;
        float delay = ParseFloat(At(3));

        // ZERO MEANS ALWAYS, and that is stock's line rather than a tidy-up of
        // it: `if (m_nTimesToFire == 0) m_nTimesToFire = EVENT_FIRE_ALWAYS`. A
        // map that writes 0 means "no limit", not "never fire".
        int times = At(4).Length > 0 ? ParseInt(At(4)) : FireAlways;
        if (times == 0)
            times = FireAlways;

        return new EventAction(target, input, parameter, delay, times, stamps.Next());
    }

    /// <summary>
    /// <c>atof</c>, for a connection's DELAY and for the number an input
    /// carries.
    ///
    /// <para>
    /// FORWARDED TO <see cref="VariantParse.Atof"/>, AND THAT IS A BUG FIX
    /// RATHER THAN A TIDY-UP. This was a hand-rolled digit scanner, and the
    /// input path was the native <c>variant_t::Convert</c> — two parsers for one
    /// question, disagreeing on exactly the strings a mapper can write.
    /// <c>"1e3"</c> was 1000 down the native queue and 1 down the managed one,
    /// for the same map value, and which one an entity got depended on which
    /// queue happened to carry the event. <c>EventActionParsesAsStockDoesTests</c>
    /// was red on seven cases before this line.
    /// </para>
    ///
    /// <para>
    /// Stock is plainly <c>m_flDelay = atof(szToken)</c>
    /// (<c>CEventAction::CEventAction</c>, src/game/server/cbase.cpp), so the
    /// exact <c>atof</c> is right here for its own reason as well as for
    /// agreement.
    /// </para>
    /// </summary>
    internal static float ParseFloat(string text) => VariantParse.Atof(text);

    /// <summary>
    /// <c>atoi</c>, for a connection's TIMES-TO-FIRE. Stock is
    /// <c>m_nTimesToFire = atoi(szToken)</c> in the same constructor, and its
    /// overflow matters: a count too big for an integer WRAPS rather than
    /// reading as zero, and zero here means "always" — so refusing the number
    /// turned a limited connection into an unlimited one.
    /// </summary>
    internal static int ParseInt(string text) => VariantParse.Atoi(text);

    /// <summary>Whether this connection has firings left.</summary>
    public bool CanFire => TimesToFire != 0;

    /// <summary>
    /// This connection after one firing: unchanged when it fires always,
    /// otherwise one fewer.
    /// </summary>
    public EventAction Fired()
        => TimesToFire == FireAlways ? this : this with { TimesToFire = TimesToFire - 1 };
}

/// <summary>
/// THE PROCEDURAL NAMES, resolved in the directory
/// (<c>plan_entity.md</c> §3.5: "<c>!activator</c>, <c>!caller</c>,
/// <c>!self</c>, <c>!player</c> resolve here").
///
/// <para>
/// The managed half of <c>CGlobalEntityList::FindEntityProcedural</c>
/// (<c>entitylist.cpp:891</c>). A '!' name is not looked up: it is answered
/// from the event that carries it, which is why it can never match more than
/// one entity and why stock refuses to continue a procedural search past its
/// first answer.
/// </para>
/// </summary>
public enum EntityIoTarget
{
    /// <summary>An ordinary name, or a classname. Not procedural.</summary>
    ByName = 0,

    /// <summary>Whoever caused the chain this event belongs to.</summary>
    Activator,

    /// <summary>The entity whose output fired this event.</summary>
    Caller,

    /// <summary>The entity doing the searching, which for an event is its caller.</summary>
    Self,

    /// <summary>
    /// Player one. <c>!pvsplayer</c> answers the same in this tree — its two
    /// PVS spellings need an edict and a <c>CBaseEntity::Instance</c> that a
    /// managed entity has never had, and <c>entitylist.cpp:920</c> already
    /// records that the fall-through arm is what runs.
    /// </summary>
    Player,

    /// <summary>A '!' name this build does not know. Resolves to nothing, loudly.</summary>
    Unknown,
}

/// <summary>Reading a target name.</summary>
public static class EntityIoTargets
{
    /// <summary>
    /// Which kind of target a name is. Case-insensitive, as stock's
    /// <c>FStrEq</c> is not — <c>FStrEq</c> is <c>Q_stricmp</c>-based, so
    /// <c>!Activator</c> works in a map today.
    /// </summary>
    public static EntityIoTarget Classify(string? name)
    {
        if (string.IsNullOrEmpty(name) || name[0] != '!')
            return EntityIoTarget.ByName;

        var what = name.AsSpan(1);

        if (what.Equals("player", StringComparison.OrdinalIgnoreCase)
            || what.Equals("pvsplayer", StringComparison.OrdinalIgnoreCase))
        {
            return EntityIoTarget.Player;
        }

        if (what.Equals("activator", StringComparison.OrdinalIgnoreCase))
            return EntityIoTarget.Activator;

        if (what.Equals("caller", StringComparison.OrdinalIgnoreCase))
            return EntityIoTarget.Caller;

        if (what.Equals("self", StringComparison.OrdinalIgnoreCase))
            return EntityIoTarget.Self;

        return EntityIoTarget.Unknown;
    }
}

/// <summary>
/// ONE EVENT WAITING TO BE DELIVERED — <c>EventQueuePrioritizedEvent_t</c>
/// (<c>eventqueue.h:20</c>), in managed code.
/// </summary>
/// <param name="FireTime">Absolute <c>curtime</c> the event comes due at.</param>
/// <param name="Target">
/// The name, procedural name or classname to deliver to. Stock also allows a
/// direct entity POINTER target; this carries the handle instead, in
/// <paramref name="DirectTarget"/>, because a managed queue has no pointers and
/// because a handle re-checks its serial where a pointer cannot.
/// </param>
/// <param name="DirectTarget">
/// A single entity to deliver to, ignoring <paramref name="Target"/>. Stock's
/// <c>m_pEntTarget</c>.
/// </param>
/// <param name="TargetInput">The input to fire.</param>
/// <param name="Value">The parameter, or null for none.</param>
/// <param name="Activator">Whoever caused the chain. May be nothing.</param>
/// <param name="Caller">The entity whose output posted this. May be nothing.</param>
/// <param name="OutputId">The connection's id stamp, or 0.</param>
/// <param name="Sequence">
/// A posting number, monotonic across the queue. It is the tie-break between
/// two events due at the same instant, and it is what makes equal-time
/// ordering a FACT rather than whatever a sort happens to do — stock gets the
/// same guarantee from a linked list it inserts into after every event whose
/// time is less than or EQUAL to the new one.
/// </param>
public readonly record struct EntityIoEvent(
    float FireTime,
    string Target,
    EntityHandle DirectTarget,
    string TargetInput,
    string? Value,
    EntityHandle Activator,
    EntityHandle Caller,
    int OutputId,
    long Sequence);

/// <summary>
/// ONE PENDING EVENT, IN A FORM THAT OUTLIVES THE ASSEMBLY THAT POSTED IT.
///
/// <para>
/// The reload record's half of the queue — see <see cref="ReloadStore"/> for
/// why nothing here may be an object of the side being unloaded. Strings and
/// numbers only, and the delay is stored as SECONDS REMAINING rather than as an
/// absolute fire time: the reload does not stop the clock, so an absolute time
/// saved before the unload would come back already past.
/// </para>
///
/// <para>
/// <b>THIS IS WHAT MAKES A DELAYED OUTPUT SURVIVE A RELOAD</b>, which
/// <c>plan_entity.md</c> step 2a names as this step's gate. Before it the queue
/// was <c>g_EventQueue</c>, native memory holding <c>EHANDLE</c>s, and a
/// managed reload could neither read it nor put it back — a
/// <c>logic_relay</c> with a two-second delay that was reloaded at one second
/// fired anyway, from code that no longer existed.
/// </para>
/// </summary>
/// <param name="Delay">Seconds still to wait when the record was taken.</param>
public readonly record struct SavedIoEvent(
    float Delay,
    string Target,
    uint DirectTarget,
    string TargetInput,
    string? Value,
    uint Activator,
    uint Caller,
    int OutputId);
