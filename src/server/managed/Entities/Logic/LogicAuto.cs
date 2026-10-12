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
/// Fires once, shortly after the map loads. How a map sets itself up: initial
/// state, opening doors, starting timers.
/// </summary>
[ScriptedEntity(EntityNames.LogicAuto)]
public sealed class LogicAuto : ScriptedEntity
{
    // THE globalstate KEY WAS DECLARED AND NEVER READ, and that is the sixth
    // instance of this project's recorded silent-loss shape (STATE.md, 5e).
    // LogicAutoVars.GlobalState has existed since this entity was ported and
    // nothing consulted it, so a map that gates its whole setup on a global --
    // stock's logicauto.cpp:87 --
    //
    //     if (!m_globalstate || GlobalEntity_GetState(m_globalstate) == GLOBAL_ON)
    //
    // fired unconditionally here. It cost nothing on dm_lockdown, whose
    // logic_auto sets no globalstate, and it cost the whole gate on any map that
    // does. The state it names had no managed owner until GlobalStateTable.
    private readonly IServerGlobalState _globals;

    public LogicAuto(IServerGlobalState globals)
    {
        _globals = globals;
    }

    /// <summary>
    /// Stock's <c>!m_globalstate || GlobalEntity_GetState(m_globalstate) ==
    /// GLOBAL_ON</c>, both clauses.
    ///
    /// <para>
    /// The empty key means "no condition", which is not the same as a condition
    /// that is off: an unnamed global resolves to index -1 and
    /// <see cref="IServerGlobalState.GetState"/> answers <c>Off</c> for that, so
    /// dropping the first clause would silence every logic_auto on every map.
    /// </para>
    /// </summary>
    private bool GlobalStateAllowsFiring
        => string.IsNullOrEmpty(Vars.GlobalState)
           || _globals.GetState(_globals.IndexOf(Vars.GlobalState)) == GlobalState.On;

    /// <summary>
    /// logic_auto's own spawnflag. Value 1, as three of the five constants in the
    /// old LogicSpawnflags were -- which is precisely why they could not share a
    /// type: three members cannot all be 1 in one enum without aliasing, and
    /// aliasing would say they mean the same thing.
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        FireOnce = 0b0000_0001,
    }

    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    public readonly Output OnMapSpawn = new();
    public readonly Output OnNewGame = new();
    public readonly Output OnLoadGame = new();
    public readonly Output OnMapTransition = new();
    public readonly Output OnBackgroundMap = new();
    public readonly Output OnMultiNewMap = new();
    public readonly Output OnMultiNewRound = new();

    /// <summary>What a map may set on this entity. See <see cref="LogicAutoVars"/>.</summary>
    [ScriptVars] public LogicAutoVars Vars = new();

    // Deferred rather than fired from spawn: the map's other entities do not all
    // exist yet when this one spawns, so firing immediately would send outputs
    // to targets that cannot be found.
    public override void OnSpawn() => SetNextThink(0.2f);

    public override void OnThink()
    {
        // THE REMOVAL IS INSIDE STOCK'S GATE, not after it, and the difference
        // is not cosmetic. logicauto.cpp:87-125 wraps the outputs AND the
        // SF_AUTO_FIREONCE removal in the one `if`, so a logic_auto whose global
        // is off SURVIVES: it fires nothing, it is not removed, and because
        // nothing reschedules the think it simply sits there for the rest of the
        // map. An Enable-style input could still reach it. Removing it here
        // would make the block permanent and would be invisible against a stock
        // build on any map where the global is on.
        if (!GlobalStateAllowsFiring)
            return;

        OnMapSpawn.Fire();

        // Always a new map here. The load types the singleplayer version
        // distinguishes — transition, save game, background — have no meaning
        // in a multiplayer mod, and the round-restart case belongs to game
        // rules that do not restart rounds this way.
        OnMultiNewMap.Fire();

        if (Flags.HasFlag(SpawnFlag.FireOnce))
            Remove();
    }
}

/// <summary>
/// The map keys <see cref="LogicAuto"/> declares.
/// </summary>
[ScriptVars]
public partial class LogicAutoVars
{
    public string GlobalState { get; set; } = string.Empty;
}
