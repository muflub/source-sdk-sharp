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
/// Fires once every one of its sources has reported in. Predates logic_branch
/// and survives in older maps.
/// </summary>
[ScriptedEntity(EntityNames.Multisource)]
public sealed class MultiSource : ScriptedEntity
{
    public readonly Output OnTrigger = new();

    // THE globalstate KEY WAS DECLARED AND NEVER READ -- the same silent loss
    // LogicAuto carried, in the other entity that declares this key. Stock gates
    // CMultiSource::IsTriggered on it (logicentities.cpp:1366):
    //
    //     if ( !m_globalstate || GlobalEntity_GetState( m_globalstate ) == GLOBAL_ON )
    //         return 1;
    //     return 0;
    //
    // so a multisource with every source reported in still answers NOT triggered
    // while its global is off. Here it fired regardless.
    private readonly IServerGlobalState _globals;

    public MultiSource(IServerGlobalState globals)
    {
        _globals = globals;
    }

    /// <summary>What a map may set on this entity. See <see cref="MultiSourceVars"/>.</summary>
    [ScriptVars] public MultiSourceVars Vars = new();

    /// <summary>
    /// Stock's <c>!m_globalstate || GlobalEntity_GetState(m_globalstate) ==
    /// GLOBAL_ON</c>. The empty key means "no condition" and NOT "a condition
    /// that is off" -- an unnamed global resolves to index -1, which reads
    /// <see cref="GlobalState.Off"/>.
    /// </summary>
    private bool GlobalStateAllowsFiring
        => string.IsNullOrEmpty(Vars.GlobalState)
           || _globals.GetState(_globals.IndexOf(Vars.GlobalState)) == GlobalState.On;

    private readonly HashSet<uint> _triggered = new();
    private int _expected;
    private bool _fired;

    [ScriptedEvent("Trigger")] private void OnTriggered(InputData input)
    {
        _triggered.Add(input.Activator.Value);

        // Sources are discovered as they report: the map does not list them
        // here, they list this entity as their target.
        _expected = Math.Max(_expected, _triggered.Count);

        // THE GLOBAL GATES THE FIRING AND NOT THE COUNTING, which is stock's
        // shape: IsTriggered checks the global LAST, after the sources are all
        // in, so a source that reports while the global is off is still
        // remembered and the entity fires the moment the global comes on and
        // another source reports. _fired is not set while the gate is shut, so
        // nothing is consumed by a refusal.
        if (!_fired && _triggered.Count >= _expected && GlobalStateAllowsFiring)
        {
            _fired = true;
            OnTrigger.Fire();
        }
    }
}

/// <summary>
/// The map keys <see cref="MultiSource"/> declares.
/// </summary>
[ScriptVars]
public partial class MultiSourceVars
{
    public string GlobalState { get; set; } = string.Empty;
}
