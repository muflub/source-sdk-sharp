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
/// Which axis a brush turns about, as <c>CBaseToggle::AxisDir</c> reads it.
/// doors.h's SF_DOOR_ROTATE_* values.
///
/// <para>
/// Shared rather than per entity, because AxisDir is shared: func_platrot,
/// func_rotbutton, momentary_rot_button and func_door_rotating all call it and
/// all read these same two bits off their own spawnflags word. Three private
/// copies is how this went wrong once already -- func_rotbutton was reading
/// func_rotating's axis flags, which are 4 and 8, so a mapper who ticked
/// "rotate roll" on a rot button got yaw and no error. One type for one
/// question is what makes that mistake impossible to write.
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
/// CBaseDoor (doors.cpp): a brush that travels between a closed and an open
/// end. A player's +USE (SF_DOOR_PUSE) or touch (SF_DOOR_PTOUCH) opens it,
/// and so do the Open and Toggle inputs. It waits its <c>wait</c> at the top
/// and comes back, unless the wait is negative or SF_DOOR_NO_AUTO_RETURN
/// leaves it open until it is used again. Blocked, it hurts what is in the
/// way (<c>dmg</c>) and goes back the way it came, unless it is
/// <c>forceclosed</c> or never comes back on its own. Its noises are
/// noise1 (moving), noise2 (arrived), startclosesound and closesound, with
/// stock's defaults when the map names none. It opens and shuts the
/// func_areaportals that target it.
///
/// <para>
/// Not here: the "master" key, which needs a multisource to ask (nothing on
/// this build answers IsTriggered); the locked and unlocked sentences, which
/// need the engine's sentence system; loopmovesound, which needs a sound's
/// duration; the moving sounds' CHAN_STATIC (they play on the script's
/// channel); func_water's fluid controller; EFL_USE_PARTITION_WHEN_NOT_SOLID
/// on a passable door; and a door group's realignment of its members'
/// positions (the group still reverses together).
/// </para>
/// </summary>
public abstract class MovingBrush : ScriptedEntity
{
    /// <summary>Stock's Spawn makes it SOLID_VPHYSICS.</summary>
    protected override SolidType BrushSolidType => SolidType.VPhysics;

    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;

    public MovingBrush(IEnginePrecache precache, IEngineSound sound, IEngineDamage damage, IEngineNetworking net,
                       PhysicsEntitySolver solver)
    {
        Precache = precache;
        Sound = sound;
        Damage = damage;
        Net = net;
        Solver = solver;
    }

    /// <summary>asking for what this entity will need before the level runs.</summary>
    protected readonly IEnginePrecache Precache;

    /// <summary>making sounds, and what is heard.</summary>
    protected readonly IEngineSound Sound;

    /// <summary>The blocked door's crush.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>A blocker's movetype.</summary>
    private readonly IEngineNetworking Net;

    /// <summary>The blocked door's physics solver, for a prop it would crush but cannot hurt.</summary>
    private readonly PhysicsEntitySolver Solver;

    public readonly Output OnOpen = new();
    public readonly Output OnClose = new();
    public readonly Output OnFullyOpen = new();
    public readonly Output OnFullyClosed = new();
    public readonly Output OnBlockedClosing = new();
    public readonly Output OnBlockedOpening = new();
    public readonly Output OnUnblockedClosing = new();
    public readonly Output OnUnblockedOpening = new();
    public readonly Output OnLockedUse = new();

    /// <summary>What a map may set on this entity. See <see cref="MovingBrushVars"/>.</summary>
    [ScriptVars] public MovingBrushVars Vars = new();

    /// <summary>doors.h's spawnflags.</summary>
    [Flags]
    protected enum DoorFlag
    {
        None = 0,
        StartOpenObsolete = 1,
        RotateBackwards = 2,
        NonSolidToPlayer = 4,
        Passable = 8,
        OneWay = 16,
        NoAutoReturn = 32,
        RotateRoll = 64,
        RotatePitch = 128,
        UseOpens = 256,
        NoNpcs = 512,
        TouchOpens = 1024,
        Locked = 2048,
        Silent = 4096,
        UseCloses = 8192,
        SilentToNpcs = 16384,
        IgnoreUse = 32768,
        NewUseRules = 65536,
    }

    protected DoorFlag Flags => SpawnFlagsAs<DoorFlag>();

    /// <summary>CBaseDoor::ObjectCaps: usable, and found in the radius, when use opens it.</summary>
    public override UseCaps ObjectCaps
        => base.ObjectCaps | (Flags.HasFlag(DoorFlag.UseOpens) ? UseCaps.ImpulseUse | UseCaps.UseInRadius : UseCaps.None);

    /// <summary>SF_DOOR_NONSOLID_TO_PLAYER's FL_UNBLOCKABLE_BY_PLAYER.</summary>
    public override bool UnblockableByPlayer => Flags.HasFlag(DoorFlag.NonSolidToPlayer);

    /// <summary>CBaseToggle's TOGGLE_STATE.</summary>
    protected enum ToggleState { AtTop = 0, AtBottom = 1, GoingUp = 2, GoingDown = 3 }

    /// <summary>
    /// m_toggle_state. Carried across a reload because it changes at runtime:
    /// a door left open by an input has to come back open, or a later Close
    /// returns early and the door never comes down after a cs_sv_reload.
    /// Owned by the live gate's hot-reload door check.
    /// </summary>
    [SaveField] protected ToggleState State { get; set; } = ToggleState.AtBottom;

    [SaveField] private bool _locked;

    /// <summary>Whether DoorTouch is the touch: off once a touch has opened it, on again at the bottom.</summary>
    [SaveField] private bool _touchArmed = true;

    /// <summary>m_hActivator: who last opened it.</summary>
    private EntityHandle _activator = EntityHandle.None;

    /// <summary>locksound_t::flwaitSound.</summary>
    private float _lockSoundWait;

    /// <summary>m_isChaining: a chained door does not chain back.</summary>
    private bool _chaining;

    // The noises with stock's defaults filled in, as its sound keyfields hold
    // them after Precache; the spawn does not run again after a reload.
    [SaveField] private string _moveSound = string.Empty;
    [SaveField] private string _arriveSound = string.Empty;
    [SaveField] private string _lockedSound = string.Empty;
    [SaveField] private string _unlockedSound = string.Empty;

    /// <summary>DOOR_SOUNDWAIT: the lock sounds at most once a second.</summary>
    private const float LockSoundWait = 1f;

    /// <summary>How long a blocked door's solver keeps a prop it cannot hurt as debris.</summary>
    private const float BlockedSolverSeconds = 4f;

    private const string ReturnContext = "DoorReturn", AreaPortalContext = "DoorAreaPortals";

    /// <summary>IsRotatingDoor.</summary>
    protected abstract bool IsRotatingDoor { get; }

    /// <summary>The noises a door makes when the map names none (CBaseDoor::Precache).</summary>
    protected abstract (string Move, string Arrive, string Locked) DefaultSounds { get; }

    /// <summary>
    /// Where the travel goes and the end it starts at: a sliding door's
    /// positions, a rotating door's angles, and the spawnpos and start-open
    /// keys' starting end. Run in the spawn, before the physics shadow is made.
    /// </summary>
    protected abstract void SpawnTravel();

    /// <summary>The travel to the open end. <paramref name="sign"/> is -1 when a yaw door opens away from its activator.</summary>
    protected abstract void TravelUp(float sign);

    /// <summary>The travel back to the closed end.</summary>
    protected abstract void TravelDown();

    /// <summary>SetToggleState: straight to an end.</summary>
    protected abstract void PlaceAt(ToggleState end);

    /// <summary>CreateVPhysics: whether the door makes its shadow. CRotDoor makes none for a door that is not solid.</summary>
    protected virtual bool MakesShadow => true;

    /// <summary>The axis a rotating door turns about, for which way it opens; zero for a sliding door.</summary>
    protected virtual QAngle MoveAngles => QAngle.Zero;

    public override void OnSpawn()
    {
        base.OnSpawn();

        // CBaseDoor::Precache: the noises the map named, or stock's.
        var defaults = DefaultSounds;
        _moveSound = Valid(Vars.MoveSound, defaults.Move);
        _arriveSound = Valid(Vars.ArriveSound, defaults.Arrive);
        _lockedSound = Valid(Vars.LockedSound, defaults.Locked);
        _unlockedSound = Valid(Vars.UnlockedSound, "DoorSound.Null");
        if (_lockedSound.Length < 4)
            _lockedSound = "buttons/button2.wav";

        foreach (string sound in new[] { _moveSound, _arriveSound, Vars.StartCloseSound, Vars.CloseSound, _lockedSound, _unlockedSound })
        {
            if (sound.Length > 0)
                Precache.PrecacheSound(sound);
        }

        // Solid and visible: a door is a wall until it is not. func_water's
        // CreateVPhysics adds FSOLID_VOLUME_CONTENTS, which is what makes the
        // engine's point contents report the moving brush as water. A passable
        // door is FSOLID_NOT_SOLID.
        bool passable = Flags.HasFlag(DoorFlag.Passable) && !IsWater;
        SolidFlags flags = (IsWater ? SolidFlags.VolumeContents : SolidFlags.None) | (passable ? SolidFlags.NotSolid : SolidFlags.None);
        if (flags == SolidFlags.None)
            SetSolidity(BrushSolidity.Solid);
        else
            SetSolidity(BrushSolidType, flags);

        if (!IsWater)
        {
            if (Flags.HasFlag(DoorFlag.NonSolidToPlayer))
                SetCollisionGroup(CollisionGroup.PassableDoor);

            // ignoredebris and the passable-to-player group both set the group; stock keeps the latter.
            if (Vars.IgnoreDebris)
            {
                if (Flags.HasFlag(DoorFlag.NonSolidToPlayer))
                    Log.Warning($"Door {Name} with conflicting collision settings, removing ignoredebris\n");
                else
                    SetCollisionGroup(CollisionGroup.Interactive);
            }
        }

        _locked = Flags.HasFlag(DoorFlag.Locked);
        _touchArmed = true;

        if (Vars.Speed == 0f)
            Vars.Speed = 100f;

        SpawnTravel();

        if (Vars.SpawnPosition == 1 && Flags.HasFlag(DoorFlag.StartOpenObsolete))
            Log.Warning($"Door {Name} using obsolete 'Start Open' spawnflag with 'Spawn Position' set to 'Open'. Reverting to old behavior.\n");

        if (MakesShadow)
            InitShadowPhysics();
        SetVisible(true);
    }

    private bool IsWater => string.Equals(ClassName, EntityNames.FuncWater, StringComparison.OrdinalIgnoreCase);

    /// <summary>SF_DOOR_SILENT, which func_water's CreateVPhysics sets ("water is silent for now").</summary>
    private bool Silent => Flags.HasFlag(DoorFlag.Silent) || IsWater;

    /// <summary>UTIL_ValidateSoundName: the map's name, or the default when it gave none.</summary>
    private static string Valid(string name, string fallback) => name.Length > 0 ? name : fallback;

    /// <summary>CBaseDoor::Activate: the area portals it targets told which end it is at.</summary>
    public override void OnServerActivate()
    {
        if (State == ToggleState.AtTop)
            UpdateAreaPortals(true);
        else if (State == ToggleState.AtBottom)
            UpdateAreaPortals(false);
    }

    // Named explicitly. The handler cannot be called OnOpen -- that is the
    // output's name -- and the "On" strip would otherwise register these as
    // "OpenInput" and "CloseInput", which no map fires.
    [ScriptedEvent("Open")]
    private void OnOpenInput()
    {
        if (State is ToggleState.AtTop or ToggleState.GoingUp || _locked)
            return;

        PlayLockSounds(locked: false);
        GoUp();
    }

    [ScriptedEvent("Close")]
    private void OnCloseInput()
    {
        if (State != ToggleState.AtBottom)
            GoDown();
    }

    [ScriptedEvent("Toggle")]
    private void OnToggled()
    {
        if (_locked)
            return;

        if (State == ToggleState.AtBottom)
            GoUp();
        else if (State == ToggleState.AtTop)
            GoDown();
    }

    [ScriptedEvent] private void OnLock() => _locked = true;
    [ScriptedEvent] private void OnUnlock() => _locked = false;

    [ScriptedEvent("SetSpeed")] private void OnSetSpeed(float speed) => Vars.Speed = speed;

    /// <summary>InputSetToggleState: straight to the top (0) or the bottom (anything else).</summary>
    [ScriptedEvent("SetToggleState")]
    private void OnSetToggleState(float state) => PlaceAt((int)state == (int)ToggleState.AtTop ? ToggleState.AtTop : ToggleState.AtBottom);

    /// <summary>CBaseDoor::Use, which a player's +USE and the Use input reach.</summary>
    [ScriptedEvent("Use")]
    private void OnUsed(InputData input) => Use(input.Activator);

    private void Use(EntityHandle activator)
    {
        _activator = activator;

        if (Vars.ChainsToDoor.Length > 0)
            Chain(door => door.Use(_activator));

        // A player's use opens only a door that says so.
        if (activator != EntityHandle.None && Entities.IsPlayer(activator) && !Flags.HasFlag(DoorFlag.UseOpens))
        {
            PlayLockSounds(locked: true);
            return;
        }

        bool noAutoReturn = Flags.HasFlag(DoorFlag.NoAutoReturn);
        bool allowUse = Flags.HasFlag(DoorFlag.NewUseRules)
            ? State is ToggleState.AtBottom or ToggleState.GoingDown
              || (noAutoReturn && State is ToggleState.AtTop or ToggleState.GoingUp)
            : State == ToggleState.AtBottom || (noAutoReturn && State == ToggleState.AtTop);

        if (!allowUse)
            return;

        if (_locked)
        {
            OnLockedUse.Fire(activator);
            PlayLockSounds(locked: true);
        }
        else
        {
            DoorActivate();
        }
    }

    /// <summary>CBaseDoor::DoorTouch: a player's touch opens a door with SF_DOOR_PTOUCH.</summary>
    public override void OnTouch(EntityHandle toucher)
    {
        if (!_touchArmed)
            return;

        if (Vars.ChainsToDoor.Length > 0)
            Chain(door => door.OnTouch(toucher));

        if (!Entities.IsPlayer(toucher) || !Flags.HasFlag(DoorFlag.TouchOpens))
            return;

        if (_locked)
        {
            OnLockedUse.Fire(toucher);
            PlayLockSounds(locked: true);
            return;
        }

        _activator = toucher;
        if (DoorActivate())
            _touchArmed = false;
    }

    /// <summary>ChainUse and ChainTouch: every other door named by chainstodoor, which does not chain back.</summary>
    private void Chain(Action<MovingBrush> act)
    {
        if (_chaining)
            return;

        foreach (EntityHandle h in Entities.FindAllByName(Vars.ChainsToDoor))
        {
            if (h == Handle || FindByHandle<MovingBrush>(h) is not { } door)
                continue;

            door._chaining = true;
            act(door);
            door._chaining = false;
        }
    }

    /// <summary>CBaseDoor::DoorActivate: a door left open goes back down; otherwise the unlocked sound and up.</summary>
    private bool DoorActivate()
    {
        if (Flags.HasFlag(DoorFlag.NoAutoReturn) && State == ToggleState.AtTop)
        {
            GoDown();
        }
        else
        {
            PlayLockSounds(locked: false);
            if (State is not (ToggleState.AtTop or ToggleState.GoingUp))
                GoUp();
        }

        return true;
    }

    /// <summary>CBaseDoor::DoorGoUp.</summary>
    protected void GoUp()
    {
        UpdateAreaPortals(true);

        if (!Silent && State is not (ToggleState.GoingUp or ToggleState.GoingDown))
            StartMovingSound();

        State = ToggleState.GoingUp;
        StopThinking(ReturnContext);

        TravelUp(IsRotatingDoor ? OpeningSign() : 1f);
        OnOpen.Fire();
    }

    /// <summary>
    /// A yaw door that is not one-way swings away from whoever opened it: -1
    /// when the activator stands on the side the door would swing into.
    /// </summary>
    private float OpeningSign()
    {
        if (_activator == EntityHandle.None || Flags.HasFlag(DoorFlag.OneWay) || MoveAngles.Yaw == 0f)
            return 1f;

        Vector at = Entities.GetOrigin(_activator);
        Vector nearest = PlayerUseFinder.NearestPoint(Entities, Handle, at);
        Vector toNearest = nearest - at;
        Vector toOrigin = Entities.GetOrigin(Handle) - at;

        // The cross product's z of the two, flattened.
        float crossZ = toOrigin.X * toNearest.Y - toOrigin.Y * toNearest.X;
        return crossZ > 0f ? -1f : 1f;
    }

    /// <summary>CBaseDoor::DoorHitTop.</summary>
    private void HitTop()
    {
        if (!Silent)
        {
            StopMovingSound();
            Emit(_arriveSound);
        }

        State = ToggleState.AtTop;

        // A door that stays open is touched again; any other comes back after its wait, if it has one.
        if (Flags.HasFlag(DoorFlag.NoAutoReturn))
            _touchArmed = true;
        else if (Vars.WaitBeforeClose >= 0f)
            SetContextThink(ReturnContext, Vars.WaitBeforeClose);

        if (Flags.HasFlag(DoorFlag.StartOpenObsolete))
            OnFullyClosed.Fire();
        else
            OnFullyOpen.Fire();
    }

    /// <summary>CBaseDoor::DoorGoDown.</summary>
    protected void GoDown()
    {
        if (!Silent && State is not (ToggleState.GoingUp or ToggleState.GoingDown))
            StartMovingSound();

        State = ToggleState.GoingDown;
        StopThinking(ReturnContext);

        TravelDown();
        OnClose.Fire();
    }

    /// <summary>CBaseDoor::DoorHitBottom.</summary>
    private void HitBottom()
    {
        if (!Silent)
        {
            StopMovingSound();
            Emit(Vars.CloseSound.Length > 0 ? Vars.CloseSound : _arriveSound);
        }

        State = ToggleState.AtBottom;
        _touchArmed = true;

        if (Flags.HasFlag(DoorFlag.StartOpenObsolete))
            OnFullyOpen.Fire(_activator);
        else
            OnFullyClosed.Fire(_activator);

        SetContextThink(AreaPortalContext, 0.5f);
    }

    public override void OnMoveDone()
    {
        if (State == ToggleState.GoingUp)
            HitTop();
        else if (State == ToggleState.GoingDown)
            HitBottom();
    }

    public override void OnThink(string context)
    {
        if (context == ReturnContext)
        {
            if (State == ToggleState.AtTop)
                GoDown();
        }
        else if (context == AreaPortalContext)
        {
            UpdateAreaPortals(false);
        }
    }

    /// <summary>
    /// UpdateAreaPortals: every func_areaportal whose target is this door's
    /// name opened or shut with it. A rotating door that starts open has it
    /// the other way about.
    /// </summary>
    private void UpdateAreaPortals(bool open)
    {
        StopThinking(AreaPortalContext);

        if (IsRotatingDoor && Flags.HasFlag(DoorFlag.StartOpenObsolete))
            open = !open;

        if (Name.Length == 0)
            return;

        foreach (EntityHandle h in Entities.FindAllByClassname(EntityNames.FuncAreaportal))
        {
            if (FindByHandle<FuncAreaPortal>(h) is { } portal && string.Equals(portal.Vars.Target, Name, StringComparison.OrdinalIgnoreCase))
                portal.Use(open);
        }
    }

    /// <summary>MovingSoundThink's noise: startclosesound on the way down, when the map names one.</summary>
    private string MovingSoundFor(bool closing) => Vars.StartCloseSound.Length > 0 && closing ? Vars.StartCloseSound : _moveSound;

    // Called before the state changes: from the bottom it is opening, from the top closing.
    private void StartMovingSound()
        => Emit(MovingSoundFor(closing: State is not (ToggleState.GoingDown or ToggleState.AtBottom)));

    // Called at the end of the travel, before the state changes.
    private void StopMovingSound()
    {
        string sound = MovingSoundFor(closing: State is not (ToggleState.GoingUp or ToggleState.AtTop));
        if (sound.Length > 0)
            Sound.EntityStopSound(Handle, sound);
    }

    private void Emit(string sound)
    {
        if (sound.Length > 0)
            Sound.EntityEmitSound(Handle, sound);
    }

    /// <summary>PlayLockSounds for a door: the locked or unlocked sound, at most once a second; nothing with SF_DOOR_SILENT.</summary>
    private void PlayLockSounds(bool locked)
    {
        if (Silent)
            return;

        string sound = locked ? _lockedSound : _unlockedSound;
        if (sound.Length == 0 || GameTime <= _lockSoundWait)
            return;

        Sound.EntityEmitSound(Handle, sound);
        _lockSoundWait = GameTime + LockSoundWait;
    }

    /// <summary>CBaseDoor::StartBlocked.</summary>
    public override void OnStartBlocked(EntityHandle other)
    {
        if (State == ToggleState.GoingDown)
            OnBlockedClosing.Fire(other);
        else
            OnBlockedOpening.Fire(other);
    }

    /// <summary>CBaseDoor::EndBlocked.</summary>
    public override void OnEndBlocked()
    {
        if (State == ToggleState.GoingDown)
            OnUnblockedClosing.Fire();
        else
            OnUnblockedOpening.Fire();
    }

    /// <summary>
    /// CBaseDoor::Blocked, every frame something is in the way: the crush, or
    /// for a prop it cannot hurt that it will never back away from a solver
    /// that lets it through; then, unless forced closed, back the way it came
    /// if it comes back on its own, and every door of its name with it.
    /// </summary>
    public override void OnBlocked(EntityHandle other)
    {
        if (Vars.BlockDamage != 0f)
        {
            Net.EntityGetState(other, out MoveType moveType);
            bool hurtable = FindByHandle<ScriptedEntity>(other)?.TakesDamage ?? true;

            if ((Vars.ForceClosed || Vars.WaitBeforeClose < 0f) && moveType == MoveType.VPhysics && !hurtable)
                Solver.Create(Handle, other, BlockedSolverSeconds, GameTime);
            else
                Damage.Apply(other, Handle, Vars.BlockDamage, DamageType.Crush);
        }

        if (Vars.ForceClosed)
            return;

        if (Vars.WaitBeforeClose >= 0f)
            Reverse();

        if (Name.Length == 0)
            return;

        foreach (EntityHandle h in Entities.FindAllByName(Name))
        {
            if (h != Handle && FindByHandle<MovingBrush>(h) is { } door && door.Vars.WaitBeforeClose >= 0f)
                door.Reverse();
        }
    }

    private void Reverse()
    {
        if (State == ToggleState.GoingDown)
            GoUp();
        else
            GoDown();
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

    /// <summary>The sound while it moves (open, and closed without startclosesound).</summary>
    [VarName("noise1")]
    public string MoveSound { get; set; } = string.Empty;

    /// <summary>The sound when it arrives (open, and closed without closesound).</summary>
    [VarName("noise2")]
    public string ArriveSound { get; set; } = string.Empty;

    /// <summary>The sound while it closes.</summary>
    [VarName("startclosesound")]
    public string StartCloseSound { get; set; } = string.Empty;

    /// <summary>The sound when it has closed.</summary>
    [VarName("closesound")]
    public string CloseSound { get; set; } = string.Empty;

    [VarName("locked_sound")]
    public string LockedSound { get; set; } = string.Empty;

    [VarName("unlocked_sound")]
    public string UnlockedSound { get; set; } = string.Empty;

    /// <summary>The door whose Use and Touch this one passes on.</summary>
    [VarName("chainstodoor")]
    public string ChainsToDoor { get; set; } = string.Empty;

    /// <summary>The crush a blocked door deals each frame.</summary>
    [VarName("dmg")]
    public float BlockDamage { get; set; }

    /// <summary>FuncDoorSpawnPos_t: 0 closed, 1 open.</summary>
    [VarName("spawnpos")]
    public int SpawnPosition { get; set; }

    /// <summary>Always closes, blocked or not.</summary>
    [VarName("forceclosed")]
    public bool ForceClosed { get; set; }

    /// <summary>COLLISION_GROUP_INTERACTIVE: debris passes through it.</summary>
    [VarName("ignoredebris")]
    public bool IgnoreDebris { get; set; }

    /// <summary>Which way a sliding door slides: the movedir key, written as angles.</summary>
    public QAngle MoveDir { get; set; }
}
