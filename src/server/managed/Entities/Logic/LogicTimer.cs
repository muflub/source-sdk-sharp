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
/// Fires an output at a fixed or random interval.
///
/// Timing is the engine's: this asks for the next think and is called back,
/// rather than tracking time itself, so a paused or slowed server behaves
/// correctly without this having to know about it.
/// </summary>
[ScriptedEntity(EntityNames.LogicTimer)]
public sealed class LogicTimer : ScriptedEntity
{
    /// <summary>
    /// The side's seed source, from which this timer's own stream is seeded:
    /// explicitly and differently per instance.
    /// </summary>
    public LogicTimer(IRandomSeeds seeds)
    {
        _random = new Random(seeds.Next());
    }

    /// <summary>
    /// logic_timer's own spawnflag.
    ///
    /// <para>
    /// It used to live in a class called LogicSpawnflags alongside four constants
    /// belonging to three other entities, three of which were also 1. Grouping by
    /// size is not grouping by domain: nothing about a timer's up-down mode relates
    /// to a relay's remove-on-fire, and putting them in one class implied it did.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        UpDown = 0b0000_0001,
    }

    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    public readonly Output OnTimer = new();
    public readonly Output OnTimerHigh = new();
    public readonly Output OnTimerLow = new();

    /// <summary>What a map may set on this entity. See <see cref="LogicTimerVars"/>.</summary>
    [ScriptVars] public LogicTimerVars Vars = new();

    private const float MinInterval = 0.01f;

    private bool _disabled;

    // Up/down timers alternate outputs, so which comes next is state.
    private bool _upDownState;

    // What was last asked for. The engine schedules an absolute time and does
    // not report what is left, so shifting a pending fire needs this. Seeded
    // in <see cref="OnSpawn"/> (via OnResetTimer), which a hot reload does not
    // run again -- so it is <c>[SaveField]</c>. Uncarried, the rebuilt instance
    // read zero and the next fire arrived immediately instead of on the
    // shifted schedule the map had waited for (OnSpawnFieldReloadTests).
    [SaveField] private float _pending;

    // This timer's own stream, seeded from the side's IRandomSeeds, for the
    // same reason as logic_case's.
    private readonly Random _random;

    public override void OnSpawn()
    {
        _disabled = Vars.StartDisabled;

        // A zero interval would think every frame forever. The floor is the
        // engine's own, so a map wanting "as fast as possible" still works.
        if (!Vars.UseRandomTime && Vars.RefireTime < MinInterval)
            Vars.RefireTime = MinInterval;

        OnResetTimer();
    }

    public override void OnThink()
    {
        if (_disabled)
            return;

        if (Flags.HasFlag(SpawnFlag.UpDown))
        {
            if (_upDownState)
                OnTimerHigh.Fire();
            else
                OnTimerLow.Fire();

            _upDownState = !_upDownState;
        }
        else
        {
            OnTimer.Fire();
        }

        OnResetTimer();
    }

    [ScriptedEvent] private void OnEnable()
    {
        _disabled = false;
        OnResetTimer();
    }

    [ScriptedEvent] private void OnDisable()
    {
        _disabled = true;
        StopThinking();
    }

    [ScriptedEvent] private void OnToggle()
    {
        if (_disabled) OnEnable(); else OnDisable();
    }

    /// <summary>Fires now and restarts — not a fire on top of the pending one.</summary>
    [ScriptedEvent] private void OnFireTimer() => OnThink();

    [ScriptedEvent] private void OnResetTimer()
    {
        if (_disabled)
            return;

        if (Vars.UseRandomTime)
            Vars.RefireTime = Vars.LowerRandomBound + (float)_random.NextDouble() * (Vars.UpperRandomBound - Vars.LowerRandomBound);

        _pending = Math.Max(Vars.RefireTime, MinInterval);
        SetNextThink(_pending);
    }

    [ScriptedEvent] private void OnRefireTime(float seconds) => Vars.RefireTime = Math.Max(seconds, MinInterval);

    [ScriptedEvent] private void OnAddToTimer(float seconds) => AdjustPendingBy(seconds);

    [ScriptedEvent] private void OnSubtractFromTimer(float seconds) => AdjustPendingBy(-seconds);

    private void AdjustPendingBy(float seconds)
    {
        _pending = Math.Max(_pending + seconds, MinInterval);
        SetNextThink(_pending);
    }
}

/// <summary>
/// The map keys <see cref="LogicTimer"/> declares.
/// </summary>
[ScriptVars]
public partial class LogicTimerVars
{
    public float RefireTime { get; set; }

    public bool UseRandomTime { get; set; }

    public float LowerRandomBound { get; set; }

    public float UpperRandomBound { get; set; }

    public bool StartDisabled { get; set; }
}
