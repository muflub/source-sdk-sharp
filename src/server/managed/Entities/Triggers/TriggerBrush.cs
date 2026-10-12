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
/// The old trigger volume, kept working for the maps that still contain one.
///
/// It predates the filter entities and the spawnflag scheme every other volume
/// shares: rather than naming a filter it carries a bitfield of things to
/// IGNORE, and rather than one "something entered" it reports being touched and
/// being used as separate things. C++ marks it obsolete and Hammer has not
/// offered it for years, so nothing new will ever be one.
///
/// Not a CBaseTrigger in C++ — it is a CBaseEntity that makes itself a trigger
/// by hand — which matters less to a port than it looks. What it took from its
/// base was the shape of the volume, and the shape comes from the map either way.
/// </summary>
[ScriptedEntity(EntityNames.TriggerBrush, ScriptedEntityKind.Brush)]
public sealed class TriggerBrush : TriggerVolume
{
    public TriggerBrush(IGameClock clock)
        : base(clock)
    {
    }

    public readonly Output OnUse = new();


    /// <summary>
    /// The filter, which IS its own type since plan Step 12. Kept as a name of
    /// its own because "Ignoring" is what the five tests below are asking about,
    /// and the var set's key is called InputFilter.
    /// </summary>
    private IgnoreFlag Ignoring => TriggerBrushVars.InputFilter;

    /// <summary>What a map may set on this entity. See <see cref="TriggerBrushVars"/>.</summary>
    [ScriptVars] public TriggerBrushVars TriggerBrushVars = new();

    // The ignore list is only meaningful if the crates arrive in the first place.
    public override bool TouchedByAnything => true;

    /// <summary>
    /// The ignore list rather than the spawnflags, because this entity is older
    /// than the spawnflags: a map that has one wrote its filtering into
    /// InputFilter and left the flags at nothing.
    /// </summary>
    /// <summary>Nothing: this one's filtering is the ignore list, not the flags.</summary>
    protected override TriggerSpawnflags KindFlags => TriggerSpawnflags.None;

    protected override bool Passes(EntityHandle toucher)
    {
        if (Ignoring.HasFlag(IgnoreFlag.Touch))
            return false;

        return Wanted(toucher) && PassesFilter(toucher);
    }

    /// <summary>
    /// Whether the ignore list lets this one through. Asked of a toucher and of
    /// whoever pressed the use key, since one bitfield covers both.
    /// </summary>
    private bool Wanted(EntityHandle other)
    {
        if (TriggerBrushVars.InputFilter == IgnoreFlag.None)
            return true;

        if (Ignoring.HasFlag(IgnoreFlag.Players) && Entities.IsPlayer(other))
            return false;

        // By class name: C++ asks for the FL_NPC flag, which is not something the
        // ABI can answer, and in a mod with no NPCs in it the two agree anyway.
        if (Ignoring.HasFlag(IgnoreFlag.NPCs) && Entities.GetClassName(other).StartsWith("npc_", StringComparison.Ordinal))
            return false;

        if (Ignoring.HasFlag(IgnoreFlag.Pushables) && Entities.GetClassName(other) == EntityNames.FuncPushable)
            return false;

        return true;
    }

    // Nothing: everything this volume says about being entered, the base has
    // already said. Its own output is for being used, not for being walked into.
    protected override void Fire(EntityHandle toucher)
    {
    }

    /// <summary>
    /// Being used — the use key, or an ent_fire. Filtered by the same list as a
    /// touch and by a bit of its own, because a volume may be walked through in
    /// silence and still answer to being pressed.
    /// </summary>
    [ScriptedEvent("Use")]
    private void OnUsed(InputData input)
    {
        if (Ignoring.HasFlag(IgnoreFlag.Use) || !Wanted(input.Activator))
            return;

        OnUse.Fire();
    }
}

/// <summary>
/// trigger_brush's ignore list -- and NOT its spawnflags, which is the whole
/// reason it needs a name of its own.
///
/// <para>
/// This entity is older than the shared trigger filter and reads a keyvalue
/// called InputFilter rather than the spawnflags word every other trigger uses.
/// The numbers overlap with <see cref="TriggerSpawnflags"/>'s -- 1, 2 and 4 are
/// clients, NPCs and pushables there and the ignoring of each here -- so the
/// type is what says which word a bit was read from, and it inverts the sense
/// into the bargain.
/// </para>
///
/// <para>
/// The C++ enum also has 0x20, "ignore all", which nothing tests. A map that
/// sets it gets no filtering at all -- here as there -- so it is not named.
/// </para>
/// </summary>
[Flags]
public enum IgnoreFlag
{
    /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
    None = 0b0000_0000,

    Players = 0b0000_0001,
    NPCs = 0b0000_0010,
    Pushables = 0b0000_0100,
    Touch = 0b0000_1000,
    Use = 0b0001_0000,
}

/// <summary>
/// The map keys <see cref="TriggerBrush"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerBrushVars
{
    /// <summary>
    /// What NOT to respond to. The sense is inverted: a set bit means "not
    /// this". The enum itself since plan Step 12 -- which is why
    /// <see cref="IgnoreFlag"/> had to leave TriggerBrush: a private nested type
    /// cannot be the type of a public property on another class, and the var set
    /// is its own class by construction.
    /// </summary>
    public IgnoreFlag InputFilter { get; set; }

    /// <summary>
    /// Read and ignored. In C++ it gates a call up to CBaseEntity's StartTouch,
    /// EndTouch and Use, each of which forwards the event to m_pParent — so in
    /// stock the key genuinely suppresses something, for a brush that has a
    /// parent. There is no parenting on the parallel hierarchy for it to gate.
    /// Declared because old maps set it and an unclaimed key is a puzzle.
    /// </summary>
    public int DontMessageParent { get; set; }
}
