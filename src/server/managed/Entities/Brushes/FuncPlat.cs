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
/// CFuncPlat and CBasePlatTrain (trains.cpp): a lift. A named one waits at the
/// top for what sends it; an unnamed one waits at the bottom, and its ride
/// trigger (CPlatTrigger) sends it up when a player stands on it and keeps it
/// at the top while they stay. A plat that is not a toggle (SF_PLAT_TOGGLE)
/// comes back down 3 s after reaching the top. Blocked, it hurts what is in
/// the way for 1 and goes back. Its noises are noise1 (moving) and noise2
/// (arrived), Plat.DefaultMoving and Plat.DefaultArrive when the map names
/// none.
///
/// <para>
/// Not here: the moving noise's sound patch and the volume key (both noises
/// play on the script's own channel and volume), and the sound m_sNoise a
/// block stops, which nothing in HL2MP sets.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncPlat, ScriptedEntityKind.Brush)]
public sealed class FuncPlat : ScriptedEntity
{
    public FuncPlat(IEnginePrecache precache, IEngineSound sound, IEngineDamage damage)
    {
        Precache = precache;
        Sound = sound;
        Damage = damage;
    }

    private readonly IEnginePrecache Precache;
    private readonly IEngineSound Sound;
    private readonly IEngineDamage Damage;

    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;

    /// <summary>What a map may set on this entity. See <see cref="FuncPlatVars"/>.</summary>
    [ScriptVars] public FuncPlatVars Vars = new();

    /// <summary>SF_PLAT_TOGGLE: it waits at each end for what sends it, and has no ride trigger.</summary>
    public const int ToggleFlag = 0x0001;

    private bool IsToggle => (SpawnFlags & ToggleFlag) != 0;

    /// <summary>CBaseToggle's TOGGLE_STATE.</summary>
    internal enum ToggleState { AtTop = 0, AtBottom = 1, GoingUp = 2, GoingDown = 3 }

    /// <summary>
    /// The two ends of the climb, measured ONCE, in <see cref="OnSpawn"/>, which
    /// a hot reload does not run again -- so they are <c>[SaveField]</c>.
    /// Uncarried, the rebuilt instance held <c>Vector.Zero</c> for both and
    /// every journey after a <c>cs_sv_reload</c> headed for the world origin
    /// (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private Vector _top;

    /// <inheritdoc cref="_top"/>
    [SaveField] private Vector _bottom;

    /// <summary>m_toggle_state.</summary>
    [SaveField] internal ToggleState State { get; private set; } = ToggleState.AtBottom;

    /// <summary>Whether a Use sends it (SetUse( PlatUse ) for a named plat; cleared by a non-toggle's first use).</summary>
    [SaveField] private bool _useArmed;

    /// <summary>The noises with stock's defaults filled in.</summary>
    [SaveField] private string _moveSound = string.Empty;

    [SaveField] private string _arriveSound = string.Empty;

    private EntityHandle _trigger = EntityHandle.None;

    /// <summary>A non-toggle plat's wait at the top before it comes down (HitTop's SetMoveDoneTime( 3 )).</summary>
    public const float TopWait = 3f;

    /// <summary>How long a player standing on the plat at the top holds it there (CPlatTrigger::Touch).</summary>
    public const float RiderHold = 1f;

    private const string DownContext = "PlatDown";

    public override void OnSpawn()
    {
        // CFuncPlat::Setup. Angles on a platform are not a facing: zeroed
        // before measuring, which the drop below depends on.
        SetAngles(Vector.Zero);
        SetSolidity(BrushSolidity.Solid);
        SetVisible(true);

        _top = SpawnOrigin;

        // Without a height it drops just far enough to be flush, leaving eight
        // units proud: an unnamed platform reads as a step rather than a hole.
        float travel = Vars.Height != 0f ? Vars.Height : Entities.GetBoundsSize(Handle).Z - 8f;
        _bottom = new Vector(_top.X, _top.Y, _top.Z - travel);

        if (Vars.Speed == 0f)
            Vars.Speed = 150f;

        // CBasePlatTrain::Precache.
        _moveSound = Vars.MoveSound.Length > 0 ? Vars.MoveSound : "Plat.DefaultMoving";
        _arriveSound = Vars.ArriveSound.Length > 0 ? Vars.ArriveSound : "Plat.DefaultArrive";
        Precache.PrecacheSound(_moveSound);
        Precache.PrecacheSound(_arriveSound);

        // CFuncPlat::Precache: the ride trigger, where the map drew the plat.
        if (!IsToggle)
            SpawnRideTrigger();

        // A named platform waits at the top for whatever calls it; an unnamed
        // one has nothing to call it, so it waits at the bottom where a player
        // can step on.
        if (Name.Length > 0)
        {
            Origin = _top;
            State = ToggleState.AtTop;
            _useArmed = true;
        }
        else
        {
            Origin = _bottom;
            State = ToggleState.AtBottom;
        }

        // CreateVPhysics: a shadow that follows it, which props meet.
        InitShadowPhysics();
    }

    /// <summary>
    /// PlatSpawnInsideTrigger and CPlatTrigger::SpawnInsideTrigger: a box over
    /// the plat's travel at its map position, made as stock writes it (the
    /// mins moved in 25 and the maxs out 25, an arm under 50 wide pinched to
    /// its middle). A box rather than stock's SOLID_BSP with no model, which
    /// this build's touch walk would pass over.
    /// </summary>
    private void SpawnRideTrigger()
    {
        _trigger = Entities.Create(EntityNames.PlatTrigger);
        if (!Entities.IsValid(_trigger))
            return;

        var (mins, maxs) = Entities.GetBounds(Handle);
        Vector size = maxs - mins;
        Vector tmin = mins + new Vector(25f, 25f, 0f);
        Vector tmax = maxs + new Vector(25f, 25f, 8f);
        tmin = new Vector(tmin.X, tmin.Y, tmax.Z - (_top.Z - _bottom.Z + 8f));

        if (size.X <= 50f)
        {
            float mid = (mins.X + maxs.X) / 2f;
            (tmin, tmax) = (new Vector(mid, tmin.Y, tmin.Z), new Vector(mid + 1f, tmax.Y, tmax.Z));
        }

        if (size.Y <= 50f)
        {
            float mid = (mins.Y + maxs.Y) / 2f;
            (tmin, tmax) = (new Vector(tmin.X, mid, tmin.Z), new Vector(tmax.X, mid + 1f, tmax.Z));
        }

        Entities.SetOrigin(_trigger, Origin);
        Entities.Spawn(_trigger);
        if (FindByHandle<PlatTrigger>(_trigger) is { } trigger)
            trigger.Platform = this;
        Entities.SetSolidity(_trigger, SolidType.BoundingBox, SolidFlags.Trigger);
        Entities.SetBounds(_trigger, tmin, tmax);
    }

    public override void OnRemoved()
    {
        StopMovingSound();

        if (Entities.IsValid(_trigger))
            Entities.Remove(_trigger);
    }

    /// <summary>CPlatTrigger::Touch: a live player sends it up from the bottom, and at the top holds it a second longer.</summary>
    internal void RiderTouched(EntityHandle player)
    {
        if (!Entities.IsPlayer(player) || Entities.GetHealth(player) <= 0)
            return;

        if (State == ToggleState.AtBottom)
            GoUp();
        else if (State == ToggleState.AtTop)
            SetContextThink(DownContext, RiderHold);
    }

    /// <summary>InputToggle: a toggle plat goes to its other end; any other stops hearing Use and comes down from the top.</summary>
    [ScriptedEvent("Toggle")]
    private void OnToggle()
    {
        if (IsToggle)
        {
            if (State == ToggleState.AtTop)
                GoDown();
            else if (State == ToggleState.AtBottom)
                GoUp();
        }
        else
        {
            _useArmed = false;
            if (State == ToggleState.AtTop)
                GoDown();
        }
    }

    [ScriptedEvent("GoUp")]
    private void OnGoUp()
    {
        if (State == ToggleState.AtBottom)
            GoUp();
    }

    [ScriptedEvent("GoDown")]
    private void OnGoDown()
    {
        if (State == ToggleState.AtTop)
            GoDown();
    }

    /// <summary>CFuncPlat::PlatUse, for a named plat: a toggle goes to its other end; any other comes down once.</summary>
    [ScriptedEvent("Use")]
    private void OnUsed()
    {
        if (!_useArmed)
            return;

        if (IsToggle)
        {
            if (State == ToggleState.AtTop)
                GoDown();
            else if (State == ToggleState.AtBottom)
                GoUp();
        }
        else
        {
            _useArmed = false;
            if (State == ToggleState.AtTop)
                GoDown();
        }
    }

    private void GoUp()
    {
        PlayMovingSound();
        State = ToggleState.GoingUp;
        StopThinking(DownContext);
        MoveTo(_top, Vars.Speed);
    }

    private void GoDown()
    {
        PlayMovingSound();
        State = ToggleState.GoingDown;
        StopThinking(DownContext);
        MoveTo(_bottom, Vars.Speed);
    }

    public override void OnMoveDone()
    {
        if (State == ToggleState.GoingUp)
            HitTop();
        else if (State == ToggleState.GoingDown)
            HitBottom();
    }

    private void HitTop()
    {
        StopMovingSound();
        Sound.EntityEmitSound(Handle, _arriveSound);
        State = ToggleState.AtTop;

        if (!IsToggle)
            SetContextThink(DownContext, TopWait);
    }

    private void HitBottom()
    {
        StopMovingSound();
        Sound.EntityEmitSound(Handle, _arriveSound);
        State = ToggleState.AtBottom;
    }

    public override void OnThink(string context)
    {
        if (context == DownContext && State == ToggleState.AtTop)
            GoDown();
    }

    private readonly MovingNoise _movingNoise = new();

    // CBasePlatTrain::PlayMovingSound stops the last before starting again.
    private void PlayMovingSound() => _movingNoise.Play(Sound, Handle, _moveSound, Vars.Volume);

    private void StopMovingSound() => _movingNoise.Stop(Sound);

    /// <summary>CFuncPlat::Blocked: 1 of crush to what is in the way, and back the way it came.</summary>
    public override void OnBlocked(EntityHandle other)
    {
        Damage.Apply(other, Handle, 1f, DamageType.Crush);

        if (State == ToggleState.GoingUp)
            GoDown();
        else if (State == ToggleState.GoingDown)
            GoUp();
    }
}

/// <summary>
/// CPlatTrigger (trains.cpp): a func_plat's ride trigger. Every frame a player
/// is inside it, the platform hears so.
/// </summary>
[ScriptedEntity(EntityNames.PlatTrigger, ScriptedEntityKind.Brush)]
public sealed class PlatTrigger : ScriptedEntity
{
    /// <summary>The platform it rides with; set by the platform that made it.</summary>
    internal FuncPlat? Platform { get; set; }

    /// <summary>CPlatTrigger's Touch is every frame, not only the arrival.</summary>
    public override bool TouchEveryTick => true;

    public override void OnTouch(EntityHandle toucher) => Platform?.RiderTouched(toucher);
}

/// <summary>
/// The map keys <see cref="FuncPlat"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncPlatVars
{
    /// <summary>How far it travels. Zero means it works its own depth out from the brush.</summary>
    public float Height { get; set; }

    public float Speed { get; set; } = 150f;

    /// <summary>The sound while it moves.</summary>
    [VarName("noise1")]
    public string MoveSound { get; set; } = string.Empty;

    /// <summary>The sound when it arrives at either end.</summary>
    [VarName("noise2")]
    public string ArriveSound { get; set; } = string.Empty;

    /// <summary>m_volume: the moving noise's volume, 0.85 when unset.</summary>
    public float Volume { get; set; }
}
