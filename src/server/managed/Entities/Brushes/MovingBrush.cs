using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Which axis a brush turns about, as <c>CBaseToggle::AxisDir</c> reads it.
/// doors.h's SF_DOOR_ROTATE_* values.
///
/// <para>
/// Shared rather than per entity, because AxisDir is shared: func_platrot,
/// func_rotbutton and momentary_rot_button all call it and all read these same
/// two bits off their own spawnflags word. Three private copies is how this
/// went wrong once already -- func_rotbutton was reading func_rotating's axis
/// flags, which are 4 and 8, so a mapper who ticked "rotate roll" on a rot
/// button got yaw and no error. One type for one question is what makes that
/// mistake impossible to write.
/// </para>
///
/// <para>
/// Roll is checked before pitch, in AxisDir's own order.
/// </para>
/// </summary>
[Flags]
internal enum RotationAxisFlag
{
    /// <summary>
    /// Neither bit set, which doors.h already has a name for: SF_DOOR_ROTATE_YAW,
    /// and it is 0 there too. Yaw is not a bit you set, it is what a brush does
    /// when the mapper sets neither of the others.
    ///
    /// <para>
    /// It follows that this member must never be TESTED for. <c>&amp; None</c>
    /// is always zero and <c>HasFlag(None)</c> is always true, so either reads
    /// as a yaw check and is not one. Ask for yaw by ruling the other two out,
    /// which is what AxisDir does.
    /// </para>
    /// </summary>
    None = 0b0000_0000,

    /// <inheritdoc cref="None"/>
    Yaw = None,

    Roll = 0b0100_0000,
    Pitch = 0b1000_0000,
}

/// <summary>
/// The moving map brushes, in C#.
///
/// A door, a lift and a platform are the same thing seen three ways: a brush
/// that is told to be somewhere else, says so when it arrives, and has to decide
/// what to do when a player is standing where it wants to go.
///
/// The stepping itself is native — it runs every tick and has to land exactly on
/// the destination, and a door that stops a fraction short never closes. What
/// the movement MEANS is here: which position counts as open, what to do when
/// blocked, whether to close again on its own.
/// </summary>
public abstract class MovingBrush : ScriptedEntity
{
    /// <summary>Stock's Spawn makes it SOLID_VPHYSICS.</summary>
    protected override SolidType BrushSolidType => SolidType.VPhysics;

    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;

    public MovingBrush(IEnginePrecache precache, IEngineSound sound)
    {
        Precache = precache;
        Sound = sound;
    }

    /// <summary>asking for what this entity will need before the level runs.</summary>
    protected readonly IEnginePrecache Precache;

    /// <summary>making sounds, and what is heard.</summary>
    protected readonly IEngineSound Sound;
    public readonly Output OnOpen = new();
    public readonly Output OnClose = new();
    public readonly Output OnFullyOpen = new();
    public readonly Output OnFullyClosed = new();
    public readonly Output OnBlockedClosing = new();
    public readonly Output OnBlockedOpening = new();

    /// <summary>What a map may set on this entity. See <see cref="MovingBrushVars"/>.</summary>
    [ScriptVars] public MovingBrushVars Vars = new();

    protected enum DoorState { Closed, Opening, Open, Closing }

    /// <summary>
    /// Which end of its travel the brush stood at when the level was saved.
    /// Carried because it changes at runtime, not only at spawn: a door left
    /// open by an input has to come back open, since an uncarried
    /// <c>Closed</c> makes a later <c>Close()</c> return early and the door
    /// never comes down after a <c>cs_sv_reload</c>. Owned by the live gate's
    /// hot-reload door check.
    /// </summary>
    [SaveField] protected DoorState State { get; private set; } = DoorState.Closed;

    private bool _locked;

    /// <summary>
    /// Where the brush sits when shut. Its spawn position, by definition.
    /// Computed ONCE, in <see cref="OnSpawn"/>, which a hot reload does not run
    /// again -- so it is <c>[SaveField]</c>. Uncarried, the rebuilt instance held
    /// <c>Vector.Zero</c> and every close after a <c>cs_sv_reload</c> headed for
    /// the world origin (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] protected Vector ClosedPosition { get; private set; }

    /// <summary>
    /// Where it sits when open. Worked out ONCE, at spawn, from the map's keys,
    /// which a hot reload does not read again -- carried for the same reason as
    /// <see cref="ClosedPosition"/> (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] protected Vector OpenPosition { get; private set; }

    /// <summary>Computes the open position from the map's keys. Called once, at spawn.</summary>
    protected abstract Vector ComputeOpenPosition();

    /// <summary>Starts the brush moving toward a position.</summary>
    protected abstract void Travel(Vector destination);

    public override void OnSpawn()
    {
        base.OnSpawn();

        // Solid and visible: a door is a wall until it is not. func_water's
        // CreateVPhysics adds FSOLID_VOLUME_CONTENTS, which is what makes the
        // engine's point contents report the moving brush as water.
        if (string.Equals(ClassName, EntityNames.FuncWater, StringComparison.OrdinalIgnoreCase))
            SetSolidity(BrushSolidType, SolidFlags.VolumeContents);
        else
            SetSolidity(BrushSolidity.Solid);
        // CreateVPhysics: a shadow that follows it, which props meet.
        InitShadowPhysics();
        SetVisible(true);

        _locked = Vars.StartDisabled;

        ClosedPosition = SpawnOrigin;
        OpenPosition = ComputeOpenPosition();


        if (Vars.Speed <= 0f)
            Vars.Speed = 100f;

        // Precached HERE because here is inside the level-load window: spawn
        // runs while the map is still being built, and a sound precached later
        // is a warning and silence. It cannot be done at registration either —
        // these names come from the map, which does not exist yet then.
        if (Vars.MoveSound.Length > 0)
            Precache.PrecacheSound(Vars.MoveSound);

        if (Vars.ArriveSound.Length > 0)
            Precache.PrecacheSound(Vars.ArriveSound);
    }

    /// <summary>
    /// Starts the travel loop, if the map named one.
    ///
    /// Stopped explicitly on arrival, because noise1 is a LOOP: a door that
    /// started one and never stopped it would grind for the rest of the map.
    /// </summary>
    private void StartMoveSound()
    {
        if (Vars.MoveSound.Length > 0)
            Sound.EntityEmitSound(Handle, Vars.MoveSound);
    }

    private void StopMoveSound()
    {
        if (Vars.MoveSound.Length > 0)
            Sound.EntityStopSound(Handle, Vars.MoveSound);
    }

    /// <summary>The one-shot when it gets where it was going. Ends by itself.</summary>
    private void PlayArriveSound()
    {
        if (Vars.ArriveSound.Length > 0)
            Sound.EntityEmitSound(Handle, Vars.ArriveSound);
    }

    // Named explicitly. The handler cannot be called OnOpen — that is the
    // output's name — and the "On" strip would otherwise register these as
    // "OpenInput" and "CloseInput", which no map fires.
    [ScriptedEvent("Open")] private void OnOpenInput() => Open();
    [ScriptedEvent("Close")] private void OnCloseInput() => Close();
    [ScriptedEvent] private void OnLock() => _locked = true;
    [ScriptedEvent] private void OnUnlock() => _locked = false;

    [ScriptedEvent("Toggle")] private void OnToggled()
    {
        if (State is DoorState.Open or DoorState.Opening)
            Close();
        else
            Open();
    }

    /// <summary>Also the input a player's use key sends.</summary>
    [ScriptedEvent] private void OnUse() => OnToggled();

    protected void Open()
    {
        if (_locked || State is DoorState.Open or DoorState.Opening)
            return;

        State = DoorState.Opening;
        OnOpen.Fire();
        StartMoveSound();
        Travel(OpenPosition);
    }

    protected void Close()
    {
        if (_locked || State is DoorState.Closed or DoorState.Closing)
            return;

        State = DoorState.Closing;
        OnClose.Fire();
        StartMoveSound();
        Travel(ClosedPosition);
    }

    public override void OnMoveDone()
    {
        // Both, whichever direction it was going: the loop has to stop or it
        // runs forever, and the arrival sound is what the player hears as the
        // door settling.
        StopMoveSound();
        PlayArriveSound();

        if (State == DoorState.Opening)
        {
            State = DoorState.Open;
            OnFullyOpen.Fire();

            // A negative wait is how a mapper says "stay open", and it is the
            // difference between a door and a one-way passage.
            if (Vars.WaitBeforeClose >= 0f)
                SetNextThink(Vars.WaitBeforeClose);

            return;
        }

        if (State == DoorState.Closing)
        {
            State = DoorState.Closed;
            OnFullyClosed.Fire();
        }
    }

    /// <summary>The auto-close timer.</summary>
    public override void OnThink()
    {
        if (State == DoorState.Open)
            Close();
    }

    //-------------------------------------------------------------------------
    // Something is in the way.
    //
    // Reversing rather than crushing: a door that kills whoever stands in it is
    // a choice some games make, and not one a deathmatch mod should make by
    // accident. The map still hears about it either way. A door with a
    // negative wait does not reverse, as stock's does not (2026-10-12).
    //-------------------------------------------------------------------------
    public override void OnBlocked(EntityHandle other)
    {
        // Stock's CBaseDoor::Blocked (doors.cpp:1222-1234): "if a door has a
        // negative wait, it would never come back if blocked", so only a door
        // that closes by itself reverses; one that stays open keeps pushing.
        // The map hears about the block either way.
        bool reverses = Vars.WaitBeforeClose >= 0f;

        if (State == DoorState.Closing)
        {
            OnBlockedClosing.Fire();
            if (!reverses)
                return;
            State = DoorState.Closed;
            Open();
            return;
        }

        if (State == DoorState.Opening)
        {
            OnBlockedOpening.Fire();
            if (!reverses)
                return;
            State = DoorState.Open;
            Close();
        }
    }
}

/// <summary>
/// The map keys <see cref="MovingBrush"/> declares.
/// </summary>
[ScriptVars]
public partial class MovingBrushVars
{
    public float Speed { get; set; } = 100f;

    /// <summary>Seconds open before closing again. Negative means it stays open.</summary>
    [VarName("wait")]
    public float WaitBeforeClose { get; set; } = 4f;

    public bool StartDisabled { get; set; }

    //-------------------------------------------------------------------------
    // The noises a door makes, named by the map.
    //
    // These are Hammer's own key names, which is why they read as nothing at
    // all: noise1 is the sound while it MOVES and noise2 the sound when it
    // ARRIVES. dm_lockdown names both on one of its doors — "Doors.Move18" and
    // "Doors.FullOpen16" — and nothing was reading either, so that door opened
    // in silence.
    //
    // Empty is the ordinary case rather than a mistake: most doors in most maps
    // name no sound, and a door that invented one would be worse than a quiet
    // one.
    //-------------------------------------------------------------------------
    [VarName("noise1")]
    public string MoveSound { get; set; } = string.Empty;

    [VarName("noise2")]
    public string ArriveSound { get; set; } = string.Empty;
}
