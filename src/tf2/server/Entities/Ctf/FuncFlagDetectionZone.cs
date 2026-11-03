//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace TF2.Server.Entities.Ctf;

/// <summary>
/// func_flagdetectionzone: a volume that watches carried flags cross it
/// (stock's <c>CFlagDetectionZone</c>, server/tf/func_capture_zone.cpp:415-751).
/// A carrier's entry and exit fire the volume outputs and the flag outputs; the
/// flag itself tells the zones when its carrier picked the flag up or dropped
/// it while inside (<see cref="IFlagDetectionZoneDispatch"/>, named from
/// entity_capture_flag.cpp:1451/:1738-1739/:2118), which re-files the player
/// between the carrier list and the plain-player one.
///
/// <para>
/// The <c>alarm</c> key is stock's <c>m_bShouldAlarm</c>, whose only reader is
/// MvM's bomb watcher; the <c>Test</c> input speaks from the carriers the
/// sweeps leave; the capture arm (<c>FlagCaptured</c>) is the doomsday
/// achievement alone and is deferred (docs/plans/tf2/deferred.tsv).
/// </para>
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class FuncFlagDetectionZone(IGameClock clock, IEngineEntities entities, ICtfPlayers players,
                                          ICtfRules rules, IEngineGameEvents events)
    : TriggerVolume(clock)
{
    public const string MapClass = "func_flagdetectionzone";

    public readonly Output OnStartTouchFlag = new();
    public readonly Output OnEndTouchFlag = new();
    public readonly Output OnDroppedFlag = new();
    public readonly Output OnPickedUpFlag = new();

    [ScriptVars] public FuncFlagDetectionZoneVars ZoneVars = new();

    /// <summary><c>m_bShouldAlarm</c> (IsAlarmZone): MvM's gate, no reader of ours yet.</summary>
    public bool IsAlarmZone => ZoneVars.Alarm;

    public bool IsDisabled => Disabled;

    // m_hTouchingPlayers: every player inside, carrying or not.
    private readonly List<EntityHandle> _players = new();

    // m_hTouchingEntities: the carriers, the list the outputs speak from.
    private readonly List<EntityHandle> _carriers = new();

    /// <summary>The volume takes every kind: stock checks no spawnflag nor filter.</summary>
    protected override TriggerSpawnflags KindFlags => TriggerSpawnflags.None;

    protected override void Fire(EntityHandle toucher) { }

    public override void OnTouch(EntityHandle toucher) => StartTouch(toucher);

    public override void OnEndTouch(EntityHandle toucher) => EndTouch(toucher);

    /// <summary>StartTouch (func_capture_zone.cpp:458-502).</summary>
    public void StartTouch(EntityHandle other)
    {
        // Is the zone enabled?
        if (Disabled)
            return;

        if (players.IsPlayer(other) && !_players.Contains(other))
            _players.Add(other);

        if (!EntityIsFlagCarrier(other))
            return;

        bool added = !_carriers.Contains(other);
        if (added)
            _carriers.Add(other);

        OnStartTouch.Fire(other);

        if (added && _carriers.Count == 1)
        {
            // First entity to touch us that passes our filters
            OnStartTouchFlag.Fire(other);
            OnStartTouchAll.Fire(other);
        }

        // The MvM achievements' listener; no fields, as stock sets none
        // (func_capture_zone.cpp:496-500).
        IntPtr handled = events.EventCreate("flag_carried_in_detection_zone");
        if (handled != IntPtr.Zero)
            events.EventFire(handled);
    }

    /// <summary>EndTouch (func_capture_zone.cpp:504-558).</summary>
    public void EndTouch(EntityHandle other)
    {
        // Is the zone enabled?
        if (Disabled)
            return;

        if (players.IsPlayer(other))
            _players.Remove(other);

        if (!_carriers.Remove(other))
            return;

        OnLeft.Fire(other);

        // If there are no more entities touching this trigger, fire the lost
        // all touches. The sweep is stock's: the gone and the dead do not count.
        if (!SweepCarriers())
        {
            //FIXME: Without this, triggers fire their EndTouch outputs when they are disabled!
            // Didn't find one?
            OnEndTouchFlag.Fire(Handle);
            OnEndTouchAll.Fire(other);
        }
    }

    /// <summary>InputTest (func_capture_zone.cpp:570-599): the same sweep, then speak from what it left.</summary>
    [ScriptedEvent]
    private void OnTest()
    {
        SweepCarriers();

        if (_carriers.Count > 0)
        {
            OnStartTouch.Fire(_carriers[0]);
            OnStartTouchFlag.Fire(Handle);
        }
        else
        {
            OnEndTouchFlag.Fire(Handle);
            OnEndTouchAll.Fire(Handle);
        }
    }

    /// <summary>The sweep EndTouch and Test share (func_capture_zone.cpp:530-548/:572-587).</summary>
    private bool SweepCarriers()
    {
        bool foundOtherTouchee = false;
        for (int i = _carriers.Count - 1; i >= 0; i--)
        {
            EntityHandle held = _carriers[i];

            if (held == EntityHandle.None || !entities.IsValid(held))
                _carriers.RemoveAt(i);
            else if (players.IsPlayer(held) && !players.IsAlive(held))
                _carriers.RemoveAt(i);
            else
                foundOtherTouchee = true;
        }

        return foundOtherTouchee;
    }

    /// <summary>FlagDropped (func_capture_zone.cpp:616-629): the carrier let go inside us.</summary>
    public void FlagDropped(EntityHandle player)
    {
        if (!_carriers.Contains(player))
            return;

        OnDroppedFlag.Fire(player);
        EndTouch(player);

        // Still touching as a non-carrier
        _players.Add(player);
    }

    /// <summary>FlagPickedUp (func_capture_zone.cpp:631-641): a player inside took a flag.</summary>
    public void FlagPickedUp(EntityHandle player)
    {
        if (!_players.Contains(player))
            return;

        OnPickedUpFlag.Fire(player);
        StartTouch(player);
    }

    /// <summary>EntityIsFlagCarrier (func_capture_zone.cpp:643-661): carrying an uncaptured flag.</summary>
    private bool EntityIsFlagCarrier(EntityHandle entity)
        => rules.CarriedBy(entity) is { } flag && !flag.IsCaptured;
}

/// <summary>The map keys <see cref="FuncFlagDetectionZone"/> declares
/// (func_capture_zone.cpp:415-431's keyfield; StartDisabled is the base's).</summary>
[ScriptVars]
public partial class FuncFlagDetectionZoneVars
{
    /// <summary><c>m_bShouldAlarm</c>: MvM's bomb watcher gates on it; our MvM half is deferred.</summary>
    [VarName("alarm")] public bool Alarm { get; set; }
}

/// <summary>
/// The flag's calls into the zones: stock's
/// <c>HandleFlagPickedUpInDetectionZone</c> and
/// <c>HandleFlagDroppedInDetectionZone</c> (func_capture_zone.cpp:711-736)
/// over the autolist, which here is the side's entity directory. Every
/// enabled zone hears the carrier's pick-up and drop; capture's own arm is
/// the doomsday achievement (deferred), and the capture path fires the drop
/// half, as stock's order does (entity_capture_flag.cpp:1738-1739).
/// </summary>
public interface IFlagDetectionZoneDispatch
{
    void PickedUp(EntityHandle player);

    void Dropped(EntityHandle player);
}

/// <summary>The game's <see cref="IFlagDetectionZoneDispatch"/> over the side's entity directory.</summary>
[SideService(Side.Server, As = [typeof(IFlagDetectionZoneDispatch)])]
public sealed class FlagDetectionZoneDispatch(IEntityDirectory directory) : IFlagDetectionZoneDispatch
{
    public void PickedUp(EntityHandle player)
    {
        foreach (FuncFlagDetectionZone zone in Zones())
            if (!zone.IsDisabled)
                zone.FlagPickedUp(player);
    }

    public void Dropped(EntityHandle player)
    {
        foreach (FuncFlagDetectionZone zone in Zones())
            if (!zone.IsDisabled)
                zone.FlagDropped(player);
    }

    private IEnumerable<FuncFlagDetectionZone> Zones()
        => directory.OfClass(FuncFlagDetectionZone.MapClass).OfType<FuncFlagDetectionZone>();
}
