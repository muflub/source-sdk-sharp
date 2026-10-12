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
/// Passes a trigger along. The most-placed logic entity there is: a map wires
/// many things to one relay so a single event can drive all of them, and can
/// then disable the relay to switch the whole group off at once.
/// </summary>
[ScriptedEntity(EntityNames.LogicRelay)]
public sealed class LogicRelay : ScriptedEntity
{
    /// <summary>
    /// logic_relay's own spawnflags. The only one of the four entities that had a
    /// PAIR of them, and the pair is unrelated to the lone bits the other three
    /// read -- which is why LogicSpawnflags collected five constants that were not
    /// one domain and this collects two that are.
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        RemoveOnFire = 0b0000_0001,
        AllowFastRetrigger = 0b0000_0010,
    }

    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    public readonly Output OnTrigger = new();

    // Named explicitly: a field called OnSpawn would collide with the OnSpawn
    // handler every entity has, and the map-facing name is not ours to change.
    public readonly Output OnRelaySpawn = new("OnSpawn");

    /// <summary>What a map may set on this entity. See <see cref="LogicRelayVars"/>.</summary>
    [ScriptVars] public LogicRelayVars Vars = new();

    private bool _disabled;

    // Set after firing so a relay cannot re-enter while its own outputs are
    // still going out. Without it a map that loops a relay back to itself
    // recurses until the stack gives up.
    private bool _waitingForRefire;

    public override void OnSpawn() => _disabled = Vars.StartDisabled;

    [ScriptedEvent] private void OnEnable() => _disabled = false;
    [ScriptedEvent] private void OnDisable() => _disabled = true;
    [ScriptedEvent] private void OnToggle() => _disabled = !_disabled;
    [ScriptedEvent] private void OnEnableRefire() => _waitingForRefire = false;
    [ScriptedEvent] private void OnCancelPending() => _waitingForRefire = false;

    [ScriptedEvent("Trigger")] private void OnTriggered()
    {
        if (_disabled || _waitingForRefire)
            return;

        OnTrigger.Fire();

        if (Flags.HasFlag(SpawnFlag.RemoveOnFire))
        {
            Remove();
            return;
        }

        // Unless the map explicitly allows it, a relay refuses to fire again
        // until its outputs have gone out. The engine's own version schedules
        // the re-enable past the longest output delay; a think does the same
        // thing without needing the event queue.
        if (!Flags.HasFlag(SpawnFlag.AllowFastRetrigger))
        {
            _waitingForRefire = true;
            SetNextThink(0.001f);
        }
    }

    public override void OnThink() => _waitingForRefire = false;
}

/// <summary>
/// The map keys <see cref="LogicRelay"/> declares.
/// </summary>
[ScriptVars]
public partial class LogicRelayVars
{
    public bool StartDisabled { get; set; }
}
