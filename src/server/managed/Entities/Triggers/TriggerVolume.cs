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
/// What every trigger volume shares: who it responds to, whether it is on, and
/// keeping track of who is inside.
/// </summary>
public abstract class TriggerVolume : ScriptedEntity
{
    public TriggerVolume(IGameClock clock)
    {
        Clock = clock;
    }

    /// <summary>what the server thinks the time is.</summary>
    protected readonly IGameClock Clock;
    public readonly Output OnStartTouch = new();
    public readonly Output OnEndTouchAll = new();
    public readonly Output OnTrigger = new();

    // Named explicitly: a field called OnEndTouch would collide with the
    // OnEndTouch handler, and the map-facing name is not ours to change.
    [OutputName("OnEndTouch")] public readonly Output OnLeft = new();

    /// <summary>What a map may set on this entity. See <see cref="TriggerVolumeVars"/>.</summary>
    [ScriptVars] public TriggerVolumeVars Vars = new();

    // Both carried across a reload, as stock carries them: m_bDisabled is a
    // saved keyfield (triggers.cpp:103) and the re-arm wait is the saved think
    // (SetNextThink( curtime + m_flWait ), :988). Absolute game time, which a
    // cs_sv_reload does not reset.
    [SaveField] private bool _disabled;
    [SaveField] private float _nextFireTime;

    // Who is inside. OnEndTouchAll is not the same as OnEndTouch — a map often
    // wants "the last person left" rather than "someone left", and with several
    // players those differ.
    private readonly HashSet<EntityHandle> _inside = new();

    /// <summary>
    /// <see cref="_inside"/> across a reload. The engine reports crossings, not
    /// occupancy, so nothing re-reports an entrant who was already inside: an
    /// emptied set never sends their leave on (stock keeps its touch list
    /// through a restore). A handle the reload re-checked to nothing is dropped.
    /// </summary>
    [SaveField("inside")]
    private EntityHandle[] InsideSaved
    {
        get => _inside.ToArray();
        set
        {
            _inside.Clear();
            foreach (EntityHandle h in value)
            {
                if (h != EntityHandle.None)
                    _inside.Add(h);
            }
        }
    }

    protected bool Disabled => _disabled;

    /// <summary>Everyone currently in this volume. The engine reports crossings, not occupancy.</summary>
    protected IReadOnlyCollection<EntityHandle> Inside => _inside;

    /// <summary>What happens when something that passes the filters sets it off.</summary>
    protected abstract void Fire(EntityHandle toucher);

    /// <summary>Called each time something inside is still inside. Most triggers do not care.</summary>
    protected virtual void WhileInside(EntityHandle toucher) { }

    public override void OnSpawn()
    {
        _disabled = Vars.StartDisabled;

        // Passable and invisible: a trigger is a volume walked through without
        // being seen.
        SetSolidity(BrushSolidity.Trigger);
        SetVisible(false);

        // InitTrigger: one that starts disabled is no trigger yet.
        if (_disabled)
            SetTriggerFlag(false);

        WarnIfNobodyIsWanted();
    }

    /// <summary>
    /// Says so when a volume has been keyed so that nothing can ever set it off.
    ///
    /// <para>
    /// The kind bits and a class's OWN spawnflags share one word, and several
    /// classes reuse high bits for themselves — trigger_playermovement's two are
    /// 0x80 and 0x800. So a mapper who ticks only that class's own boxes ends up
    /// with a non-zero spawnflags naming neither clients nor everything-else,
    /// and <see cref="Passes"/> answers "nothing" before it looks at anything.
    /// </para>
    ///
    /// <para>
    /// STOCK DOES EXACTLY THE SAME, measured rather than read: in the stock
    /// HL2MP build, five identical volumes over one player in one run, a
    /// trigger_multiple keyed 2176 never fired and the same volume keyed 2177
    /// did. So this is a warning and not a correction — a stock map keyed that
    /// way is equally dead, and quietly making ours work would hide the map's
    /// bug instead of the mapper's.
    /// </para>
    ///
    /// <para>
    /// Worth saying out loud because the failure has no other symptom whatever:
    /// the volume spawns, takes touches, reports no error, and simply never
    /// fires. It has already cost this map one debugging round.
    /// </para>
    /// </summary>
    private void WarnIfNobodyIsWanted()
    {
        if (KindFlags == TriggerSpawnflags.None || KindWanted)
            return;

        Log.Warning($"[SourceSharp] {ClassName} '{Name}': spawnflags {SpawnFlags} names none of "
                       + $"the kinds this volume acts on ({KindFlags}), so nothing can ever "
                       + "touch it\n");
    }

    [ScriptedEvent] private void OnEnable() => SetDisabled(false);
    [ScriptedEvent] private void OnDisable() => SetDisabled(true);
    [ScriptedEvent] private void OnToggle() => SetDisabled(!_disabled);

    /// <summary>
    /// Told when the volume is switched on or off, and only when it actually
    /// changes.
    ///
    /// Most volumes need nothing here: the test at the top of OnTouch is the
    /// whole of being off. One that has handed something to native does, because
    /// no longer noticing arrivals is not the same as stopping — a medium goes
    /// on acting on everything already inside it — and the class that set that
    /// up is the only thing that knows how to take it back.
    /// </summary>
    protected virtual void OnDisabledChanged() { }

    /// <summary>CBaseTrigger::Enable and Disable, for a class that switches itself.</summary>
    protected void SetEnabled(bool enabled) => SetDisabled(!enabled);

    private void SetDisabled(bool disabled)
    {
        if (disabled == _disabled)
            return;

        _disabled = disabled;
        SetTriggerFlag(!disabled);
        OnDisabledChanged();
    }

    /// <summary>
    /// Whether being on is FSOLID_TRIGGER, as CBaseTrigger's Enable and
    /// Disable make it. trigger_brush (FL_DONTTOUCH) and the vphysics volumes
    /// (their object's collisions) switch some other way.
    /// </summary>
    protected virtual bool OnIsTriggerFlag => true;

    /// <summary>
    /// CBaseTrigger::Enable and Disable: FSOLID_TRIGGER with being on. Off,
    /// the volume leaves the frame's overlap walk, so whoever was inside is
    /// told they left (stock fires OnEndTouch for a disabled trigger, by its
    /// own FIXME); on again, whoever is inside arrives, as stock's
    /// PhysicsTouchTriggers finds them.
    /// </summary>
    private void SetTriggerFlag(bool on)
    {
        if (!OnIsTriggerFlag)
            return;

        SolidFlags flags = Entities.GetSolidFlags(Handle);
        SolidFlags wanted = on ? flags | SolidFlags.Trigger : flags & ~SolidFlags.Trigger;
        if (wanted != flags)
            SetSolidity(Entities.GetSolidType(Handle), wanted);
    }

    /// <summary>
    /// Whether this volume responds to a given toucher.
    ///
    /// Two questions in order: the spawnflags say which KINDS of thing a trigger
    /// notices, and a named filter — if the map wired one — decides among them.
    /// A filter the map named but did not create is ignored rather than treated
    /// as rejecting everyone, because one typo should not silently disable a
    /// trigger.
    /// </summary>
    protected virtual bool Passes(EntityHandle toucher)
    {
        if (!KindWanted)
            return false;

        return PassesFilter(toucher);
    }

    /// <summary>
    /// Which spawnflag bits this volume reads as "a kind I act on" — or zero for
    /// a class that decides by something other than spawnflags.
    ///
    /// Overridable because a class whose whole subject is one kind asks a
    /// different question: a wind acts on physics objects and never on a player,
    /// so the bit that means anything to it is the props one and the clients bit
    /// is noise. Named here rather than left as a literal in each override so
    /// that <see cref="WarnIfNobodyIsWanted"/> and <see cref="Passes"/> cannot
    /// disagree about what the volume actually reads — they did, and the warning
    /// went off at a wind that was working perfectly.
    /// </summary>
    protected virtual TriggerSpawnflags KindFlags
        => TriggerSpawnflags.Clients | TriggerSpawnflags.EverythingElse;

    /// <summary>The map's spawnflags read as the kinds they name.</summary>
    protected TriggerSpawnflags Kinds => SpawnFlagsAs<TriggerSpawnflags>();

    /// <summary>
    /// Whether the spawnflags name a kind this volume acts on. Zero means the
    /// map said nothing, which is read as "anybody" — see docs/divergences.md,
    /// where stock reads it as "nobody".
    /// </summary>
    /// <summary>
    /// ANY of the kinds this volume acts on, not all of them: a map that ticks
    /// only "clients" on a volume that acts on clients and everything else must
    /// still pass. Written as an intersection rather than as HasFlag for exactly
    /// that reason -- HasFlag(KindFlags) would demand every bit the volume reads.
    /// </summary>
    protected bool KindWanted => Kinds == TriggerSpawnflags.None || (Kinds & KindFlags) != 0;

    /// <summary>
    /// The second half of <see cref="Passes"/> on its own: whether the filter the
    /// map named accepts this toucher.
    ///
    /// Separate because a volume that decides for itself which KINDS of thing it
    /// notices — a wind cares about physics objects and nothing else — still owes
    /// the mapper the filter they wired, and answering the kind question is no
    /// reason to start ignoring it.
    /// </summary>
    protected bool PassesFilter(EntityHandle toucher)
    {
        var filter = FindByName<FilterBase>(Vars.FilterName);
        return filter is null || filter.Passes(toucher);
    }

    public override void OnTouch(EntityHandle toucher)
    {
        if (_disabled || !Passes(toucher))
            return;

        // The engine reports entering once, but a player can leave and come
        // back, so this is a membership test rather than a first-time flag.
        if (!_inside.Add(toucher))
            return;

        OnStartTouch.Fire();

        if (Clock.Now < _nextFireTime)
            return;

        _nextFireTime = Clock.Now + Vars.Wait;
        Fire(toucher);
    }

    public override void OnEndTouch(EntityHandle toucher)
    {
        if (!_inside.Remove(toucher))
            return;

        OnLeft.Fire();

        if (_inside.Count == 0)
            OnEndTouchAll.Fire();
    }

    /// <summary>
    /// Drives WhileInside for triggers that act continuously, and stops as soon
    /// as the volume empties so an unoccupied one costs nothing.
    /// </summary>
    protected void PumpWhileOccupied(float interval)
    {
        if (_disabled)
            return;

        foreach (var toucher in _inside)
        {
            if (Entities.IsValid(toucher))
                WhileInside(toucher);
        }

        if (_inside.Count > 0)
            SetNextThink(interval);
    }
}

/// <summary>
/// The map keys <see cref="TriggerVolume"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerVolumeVars
{
    public bool StartDisabled { get; set; }

    /// <summary>Seconds before the same volume fires again. The map's "wait" key.</summary>
    public float Wait { get; set; } = 0.2f;

    /// <summary>Name of a filter entity that decides who this responds to.</summary>
    public string FilterName { get; set; } = string.Empty;
}
