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
/// A named global state a map can set and other map logic can read —
/// stock's <c>CEnvGlobal</c> (<c>game/server/logicentities.cpp:972</c>).
///
/// <para>
/// <b>It used to write into a table nobody read.</b> This entity kept its own
/// <c>private static Dictionary&lt;string,int&gt; s_globals</c>, and nothing in
/// the tree ever looked at it — while <c>logic_auto</c> and <c>multisource</c>,
/// which stock gates on the SAME table, read nothing at all. So a map could set
/// a global and no map logic could see it, in either direction. That is
/// plan_entity.md's Phase 4 exit finding for this lane and it is fixed by there
/// being ONE table: <see cref="IServerGlobalState"/>, which is
/// <c>game/server/globalstate.cpp</c>'s <c>CGlobalState</c> ported.
/// </para>
///
/// <para>
/// <b>The private dictionary also disagreed with stock in five ways</b>, each of
/// which is now stock's answer and pinned by a fact:
/// </para>
///
/// <list type="bullet">
/// <item>it was case-INSENSITIVE where stock's <c>CUtlSymbolTable</c> is not;</item>
/// <item><c>Remove</c> DELETED the entry, where stock sets <c>GLOBAL_DEAD</c> —
///       the opposite meaning, since a deleted name reads as OFF and a DEAD one
///       is a permanent refusal that later maps honour;</item>
/// <item>the <c>counter</c> key was declared and never used at all, and stock
///       applies it at spawn;</item>
/// <item><c>Toggle</c> flipped a DEAD global to ON, where stock returns without
///       touching it;</item>
/// <item>there was no <c>GetCounter</c> input and no <c>Counter</c> output.</item>
/// </list>
///
/// <para>
/// One deliberate divergence remains and it is stock's own bug rather than a
/// port decision: stock's <c>Spawn</c> tests <c>if ( !m_globalstate )</c>, which
/// is a <c>string_t</c> null test rather than an empty-string test, so an
/// <c>env_global</c> with <c>globalstate ""</c> survives there and is removed
/// here. Both agree for the case a mapper can actually author.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvGlobal)]
public sealed class EnvGlobal : ScriptedEntity
{
    /// <summary>
    /// <c>SF_GLOBAL_SET</c> — "set global state to initial state on spawn"
    /// (<c>logicentities.cpp:970</c>). Without it an <c>env_global</c> declares
    /// NOTHING at spawn and only its inputs touch the table.
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        SetOnSpawn = 0b0000_0001,
    }

    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    private readonly IServerGlobalState _globals;

    public EnvGlobal(IServerGlobalState globals)
    {
        _globals = globals;
    }

    /// <summary>What a map may set on this entity. See <see cref="EnvGlobalVars"/>.</summary>
    [ScriptVars] public EnvGlobalVars Vars = new();

    /// <summary>Stock's <c>m_outCounter</c>, spelled <c>Counter</c> in the FGD.</summary>
    public readonly Output Counter = new();

    /// <summary>The global this entity names, or -1 while it names none.</summary>
    private int Index => _globals.IndexOf(Vars.GlobalName);

    /// <summary>
    /// Declares the global if it is not declared and answers its index — stock's
    /// <c>if ( !GlobalEntity_IsInTable(...) ) GlobalEntity_Add(...)</c>, which
    /// every input of this entity opens with.
    ///
    /// <para>
    /// The map name is left null so the table stamps the map that is loaded;
    /// stock passes <c>gpGlobals-&gt;mapname</c> and a managed entity has no
    /// equivalent to reach for.
    /// </para>
    /// </summary>
    private int Declare(GlobalState ifNew)
    {
        int i = Index;
        return i >= 0 ? i : _globals.Add(Vars.GlobalName, null, ifNew);
    }

    public override void OnSpawn()
    {
        // Stock's first two lines: an env_global that names no global has
        // nothing to be, and removes itself.
        if (Vars.GlobalName.Length == 0)
        {
            Remove();
            return;
        }

        // THE SPAWNFLAG GATES THE DECLARATION. Without SF_GLOBAL_SET stock adds
        // nothing at spawn, so the global stays whatever a PREVIOUS map left it
        // — which is the entire point of a cross-level table, and what the old
        // unconditional Publish() overwrote on every load.
        if (!Flags.HasFlag(SpawnFlag.SetOnSpawn))
            return;

        int i = _globals.Add(Vars.GlobalName, null, (GlobalState)Vars.State);

        // Stock applies the counter key only when it is non-zero, and applies it
        // AFTER the add — so a global that was already in the table from an
        // earlier map keeps its state and takes this map's counter.
        if (Vars.CounterValue != 0)
            _globals.SetCounter(i, Vars.CounterValue);
    }

    [ScriptedEvent] private void OnTurnOn() => _globals.SetState(Declare(GlobalState.On), GlobalState.On);

    [ScriptedEvent] private void OnTurnOff() => _globals.SetState(Declare(GlobalState.Off), GlobalState.Off);

    /// <summary>
    /// Stock's <c>InputRemove</c>: the global is marked <see cref="GlobalState.Dead"/>,
    /// NOT deleted. Dead is a permanent refusal that later maps honour —
    /// <c>util.cpp:1874</c> deletes any entity whose <c>globalname</c> reads
    /// dead — and a deleted entry would read as merely OFF.
    /// </summary>
    [ScriptedEvent] private void OnRemove() => _globals.SetState(Declare(GlobalState.Dead), GlobalState.Dead);

    [ScriptedEvent]
    private void OnToggle()
    {
        // Stock's InputToggle reads the state BEFORE declaring anything, and
        // returns without a write for anything that is not exactly ON or OFF.
        // A DEAD global stays dead: it is a decision a previous map made and a
        // toggle is not entitled to reverse it.
        GlobalState was = _globals.GetState(Index);

        GlobalState now = was switch
        {
            GlobalState.On => GlobalState.Off,
            GlobalState.Off => GlobalState.On,
            _ => was,
        };

        if (now == was)
            return;

        _globals.SetState(Declare(now), now);
    }

    [ScriptedEvent]
    private void OnSetCounter(float value)
        => _globals.SetCounter(Declare(GlobalState.On), (int)value);

    [ScriptedEvent]
    private void OnAddToCounter(float value)
        => _globals.AddToCounter(Declare(GlobalState.On), (int)value);

    /// <summary>
    /// Stock's <c>InputGetCounter</c>, which had no managed half at all: it
    /// declares the global if need be and fires the counter out.
    /// </summary>
    [ScriptedEvent]
    private void OnGetCounter()
        => Counter.Fire(_globals.GetCounter(Declare(GlobalState.On)));
}

/// <summary>
/// The map keys <see cref="EnvGlobal"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvGlobalVars
{
    [VarName("globalstate")]
    public string GlobalName { get; set; } = string.Empty;

    /// <summary>
    /// Stock's <c>m_initialstate</c>, a <c>GLOBALESTATE</c> carried as an
    /// integer. The numbers are the wire — see <see cref="GlobalState"/>.
    /// </summary>
    [VarName("initialstate")]
    public int State { get; set; }

    /// <summary>
    /// Stock's <c>m_counter</c>, applied at spawn when non-zero. It was declared
    /// here and read by nothing before plan_entity.md's Phase 4 exit.
    /// </summary>
    [VarName("counter")]
    public int CounterValue { get; set; }
}
