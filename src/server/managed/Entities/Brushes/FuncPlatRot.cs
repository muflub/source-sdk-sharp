//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>Which of two places a lift is, or which of them it is on its way to.</summary>
public enum PlatformState
{
    AtTop,
    AtBottom,
    GoingUp,
    GoingDown,
}

/// <summary>
/// A lift that turns on the way, and the base of the track changers.
///
/// A sibling of <see cref="FuncPlat"/> rather than a subclass, which is the
/// opposite of stock's arrangement and is worth the duplication: the rotation is
/// not a decoration on top of the lift, it is the thing the timing is built
/// around. The turn takes exactly as long as the climb, so that a platform
/// arrives level rather than still swinging, and a platform told not to move at
/// all — which is what a track changer bolted to the floor is — takes its
/// duration from the turn instead. Neither of those is expressible as "a
/// func_plat that also spins".
///
/// <para>
/// Driven by a TIMED think and not by arrival, for the reason on
/// <see cref="TrainBrush"/> and one more of its own: this moves and turns at the
/// same time, and the shell reports an arrival once per axis-set, so a combined
/// journey reports twice. The think asks where the platform actually IS, which
/// also covers the case where something stood in the way and the stepper stopped
/// dead against it.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncPlatrot, ScriptedEntityKind.Brush)]
public class FuncPlatRot : ScriptedEntity
{
    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;

    public FuncPlatRot(IGameClock clock, IEngineDamage damage,
                       IEnginePrecache precache, IEngineSound sound)
    {
        Clock = clock;
        Damage = damage;
        Precache = precache;
        Sound = sound;
    }

    /// <summary>what the server thinks the time is.</summary>
    protected readonly IGameClock Clock;

    /// <summary>hurting things.</summary>
    protected readonly IEngineDamage Damage;

    /// <summary>asking for what this entity will need before the level runs.</summary>
    protected readonly IEnginePrecache Precache;

    /// <summary>making sounds, and what is heard.</summary>
    protected readonly IEngineSound Sound;

    /// <summary>
    /// The shared rotation-axis bits off this entity's spawnflags word.
    /// Separate from any flags of its own: see <see cref="RotationAxisFlag"/>.
    /// </summary>
    private RotationAxisFlag Axis => SpawnFlagsAs<RotationAxisFlag>();

    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="FuncPlatRotVars"/>.</summary>
    [ScriptVars] public FuncPlatRotVars Vars = new();

    /// <summary>
    /// The two ends of the climb, measured ONCE, by the <see cref="Setup"/> the
    /// spawn runs, which a hot reload does not run again -- so they are
    /// <c>[SaveField]</c>. Uncarried, the rebuilt instance held
    /// <c>Vector.Zero</c> for both and every journey after a
    /// <c>cs_sv_reload</c> headed for the world origin
    /// (OnSpawnFieldReloadTests). The track changers' losses are these same
    /// declarations, inherited.
    /// </summary>
    [SaveField] private Vector _top;

    /// <inheritdoc cref="_top"/>
    [SaveField] private Vector _bottom;

    /// <summary>
    /// The two facings, and both are absolute rather than relative to the map's:
    /// stock zeroes a platform's angles before it measures anything, so a
    /// platform's bottom facing is always level however the mapper drew it.
    /// </summary>
    protected Vector StartAngles;
    protected Vector EndAngles;

    /// <summary>
    /// Which end it stood at, or which end it is on its way to. Set by the spawn
    /// and by the inputs, and never recomputed after it -- so it is
    /// <c>[SaveField]</c>. Uncarried, the rebuilt instance read the default and
    /// a platform standing at the top answered <c>Use</c> with the wrong move
    /// after a <c>cs_sv_reload</c> (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private PlatformState _state = PlatformState.AtTop;

    /// <summary>Set while the platform is only turning, which is a track changer bolted down.</summary>
    protected bool RotateOnly;

    /// <summary>
    /// func_platrot's own spawnflag, and only its own. The two axis bits this
    /// class also reads are 64 and 128, they are shared with func_rotbutton and
    /// momentary_rot_button through <c>CBaseToggle::AxisDir</c>, and they live
    /// in <see cref="RotationAxisFlag"/> rather than being copied here.
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        /// <summary>Stays where it is put rather than dropping back by itself.</summary>
        PlatToggle = 0b0000_0001,
    }

    /// <summary>
    /// The three seconds a non-toggle platform waits at the top before it drops
    /// again by itself. Stock's, and hard-coded there too.
    /// </summary>
    private const float ReturnDelay = 3f;

    public PlatformState State => _state;

    public Vector TopPosition => _top;

    public Vector BottomPosition => _bottom;

    /// <summary>
    /// Whether it stays where it is put rather than dropping back by itself.
    ///
    /// Virtual because a track changer is always one, whatever the map's flags
    /// say — a track that swung a train into place and then wandered back down
    /// would be worse than useless. Stock overrides the same method for the same
    /// reason, and its own comment says the override is there to keep the two
    /// classes' spawnflags from colliding.
    /// </summary>
    protected virtual bool IsTogglePlat => Flags.HasFlag(SpawnFlag.PlatToggle);

    public override void OnSpawn()
    {
        SetSolidity(BrushSolidity.Solid);
        SetVisible(true);

        Setup();
        SetupRotation();

        // A named platform is one a button sends for, so it starts at the top and
        // is brought down. An unnamed one has nothing that could ever call it, so
        // it starts at the bottom and is a step.
        if (Name.Length > 0)
        {
            Origin = _top;
            _state = PlatformState.AtTop;
            SetAngles(EndAngles);
        }
        else
        {
            Origin = _bottom;
            _state = PlatformState.AtBottom;
            SetAngles(StartAngles);
        }

        PrecacheSounds();
    }

    /// <summary>
    /// Here rather than at registration because these names come from a map that
    /// does not exist then, and separate from OnSpawn because a subclass that
    /// places itself differently still needs its sounds.
    /// </summary>
    protected void PrecacheSounds()
    {
        if (Vars.MoveSound.Length > 0)
            Precache.PrecacheSound(Vars.MoveSound);

        if (Vars.ArriveSound.Length > 0)
            Precache.PrecacheSound(Vars.ArriveSound);
    }

    /// <summary>
    /// Puts the platform in a state without moving it, for a subclass that starts
    /// somewhere the spawnflags chose and for a test with no engine to travel in.
    /// </summary>
    protected void SetPlatformState(PlatformState state) => _state = state;

    /// <summary>
    /// Where the two ends are. Stock's Setup, which is also where a track changer
    /// gets its geometry from.
    /// </summary>
    protected void Setup()
    {
        // Angles on a platform are not a facing, and everything below measures
        // against the brush as though it were level. Stock zeroes them here for
        // exactly that reason and leaves a comment saying so where it matters.
        SetAngles(Vector.Zero);

        if (Vars.Speed <= 0f)
            Vars.Speed = 150f;

        _top = SpawnOrigin;

        // Without an explicit height it drops just far enough to be flush,
        // leaving eight units proud — which is what makes an unnamed platform
        // read as a step rather than a hole.
        float travel = Vars.Height != 0f ? Vars.Height : Entities.GetBoundsSize(Handle).Z - 8f;

        _bottom = new Vector(_top.X, _top.Y, _top.Z - travel);
    }

    /// <summary>
    /// The two facings. Stock's SetupRotation.
    ///
    /// A rotation of zero is not "turn by nothing" but "do not turn", and the
    /// difference shows: the platform's facing is then left at level in both
    /// places rather than being driven to a target every journey.
    /// </summary>
    protected void SetupRotation()
    {
        if (Vars.Rotation == 0f)
        {
            StartAngles = Vector.Zero;
            EndAngles = Vector.Zero;
            return;
        }

        StartAngles = Vector.Zero;
        EndAngles = RotationAxis() * Vars.Rotation;
    }

    /// <summary>Stock's AxisDir: yaw unless a spawnflag names roll or pitch.</summary>
    private Vector RotationAxis()
    {
        if (Axis.HasFlag(RotationAxisFlag.Roll))
            return new Vector(0f, 0f, 1f);

        if (Axis.HasFlag(RotationAxisFlag.Pitch))
            return new Vector(1f, 0f, 0f);

        return new Vector(0f, 1f, 0f);
    }

    /// <summary>Where the platform is, so a subclass can move a rider with it.</summary>
    protected Vector Position => Origin;

    //-------------------------------------------------------------------------
    // Being sent for.
    //
    // The only thing a map can say to a platform is "use", which is why there is
    // no Open or Close here: stock's PlatUse toggles a toggle platform and sends
    // a one-shot one down, once. Toggle and GoUp/GoDown are declared as well
    // because CFuncPlat's datadesc declares them and a map is entitled to them.
    //-------------------------------------------------------------------------
    [ScriptedEvent("Use")] private void OnUse() => PlatUse();

    [ScriptedEvent("Toggle")]
    private void OnToggleInput()
    {
        if (_state == PlatformState.AtTop)
            GoDown();
        else if (_state == PlatformState.AtBottom)
            GoUp();
    }

    [ScriptedEvent("GoUp")]
    private void OnGoUpInput()
    {
        if (_state == PlatformState.AtBottom)
            GoUp();
    }

    [ScriptedEvent("GoDown")]
    private void OnGoDownInput()
    {
        if (_state == PlatformState.AtTop)
            GoDown();
    }

    protected virtual void PlatUse() => OnToggleInput();

    //-------------------------------------------------------------------------
    // The journey.
    //-------------------------------------------------------------------------
    protected virtual void GoDown() => Depart(false);

    protected virtual void GoUp() => Depart(true);

    /// <summary>
    /// Sets off, and arms the arrival.
    ///
    /// The turn is handed the journey's own duration, so the two finish together.
    /// A platform told not to move takes the duration from the turn instead, at
    /// the same speed read as degrees rather than units — which is stock's
    /// AngularMove, and the only place in this class where the speed key changes
    /// units.
    /// </summary>
    private void Depart(bool up)
    {
        _state = up ? PlatformState.GoingUp : PlatformState.GoingDown;

        Vector wantedAngles = up ? EndAngles : StartAngles;
        float turn = LargestTurn(wantedAngles);

        float seconds;

        if (RotateOnly)
        {
            seconds = turn > 0f ? turn / Vars.Speed : 0.1f;
        }
        else
        {
            Vector destination = up ? _top : _bottom;
            float distance = (destination - Origin).Length;

            seconds = distance > 0f ? distance / Vars.Speed : 0.1f;

            MoveTo(destination, Vars.Speed);
        }

        if (turn > 0f)
            RotateTo(wantedAngles, turn / seconds);

        PlayMovingSound();

        // Remembered as well as armed, because a platform that only TURNS has no
        // position to check itself against and the clock is the only answer it
        // has — and a subclass that wants to be asked every frame would otherwise
        // arrive the moment it set off.
        _arriveAt = Clock.Now + seconds;

        SetNextThink(TicksEveryFrame ? 0f : seconds);
    }

    private float _arriveAt;

    /// <summary>
    /// Whether the journey needs looking at every frame rather than once at the
    /// end. True here since the lifts learned to carry — base velocity is
    /// consumed per command, so a rider fed slower than every tick is kicked
    /// rather than carried. The track changer's override narrows it back to
    /// its train-dragging phase, which is the one journey it makes with
    /// something aboard.
    /// </summary>
    protected virtual bool TicksEveryFrame => true;

    /// <summary>Called on every think while the platform is on its way.</summary>
    protected virtual void WhileTravelling() { }

    public override void OnThink()
    {
        if (_state == PlatformState.GoingUp || _state == PlatformState.GoingDown)
        {
            WhileTravelling();

            // A platform that is not there yet was stopped, not mistimed:
            // something is standing in the way and the stepper has halted against
            // it. Ask again at the time it would arrive if it set off now. A
            // platform that only turns is asked of the clock instead, having no
            // position that changes.
            Vector destination = _state == PlatformState.GoingUp ? _top : _bottom;

            // THE CARRY IS NOT HERE ANY MORE -- plan_entity.md step 2g. Riders
            // are carried by the integrator, for every mover, from the step it
            // just committed (SourceSharp.Server/EntityMotion.cs). The turn is
            // still NOT imparted — a rider on the belts-station platrot rises
            // level while the platform yaws under their feet, which stays in
            // docs/divergences.md.

            float remaining = (destination - Origin).Length;

            bool arrived = RotateOnly ? Clock.Now >= _arriveAt
                                      : remaining <= 0.1f;

            if (!arrived)
            {
                SetNextThink(TicksEveryFrame ? 0f
                                             : (destination - Origin).Length / Vars.Speed);
                return;
            }

            if (_state == PlatformState.GoingUp)
                HitTop();
            else
                HitBottom();

            return;
        }

        // Arrived at the top a while ago, and not a toggle platform, so it is
        // time to go back down. See HitTop.
        if (_state == PlatformState.AtTop)
            GoDown();
    }

    protected virtual void HitTop()
    {
        StopMovingSound();
        PlayArriveSound();

        _state = PlatformState.AtTop;
        SetAngles(EndAngles);

        // A one-shot platform drops back by itself after a pause, which is the
        // whole of what makes it a lift rather than a switch.
        if (!IsTogglePlat)
            SetNextThink(ReturnDelay);
    }

    protected virtual void HitBottom()
    {
        StopMovingSound();
        PlayArriveSound();

        _state = PlatformState.AtBottom;
        SetAngles(StartAngles);
    }

    /// <summary>
    /// Something in the way, so it goes back where it came from.
    ///
    /// A lift reverses rather than crushing, for the reason a door does: killing
    /// whoever is standing in one is a choice, and not one to make by accident.
    /// The single point of damage is stock's, and it is there to tell the blocker
    /// something happened rather than to hurt them.
    /// </summary>
    public override void OnBlocked(EntityHandle other)
    {
        Damage.TakeDamage(other, Handle, 1f, DamageType.Crush);

        StopMovingSound();

        if (_state == PlatformState.GoingUp)
            GoDown();
        else if (_state == PlatformState.GoingDown)
            GoUp();
    }

    //-------------------------------------------------------------------------
    // The noises, under Hammer's own opaque key names, as on a platform train:
    // noise1 while it moves and noise2 when it arrives.
    //-------------------------------------------------------------------------
    protected void PlayMovingSound()
    {
        if (Vars.MoveSound.Length > 0)
            Sound.EntityEmitSound(Handle, Vars.MoveSound);
    }

    protected void StopMovingSound()
    {
        if (Vars.MoveSound.Length > 0)
            Sound.EntityStopSound(Handle, Vars.MoveSound);
    }

    protected void PlayArriveSound()
    {
        if (Vars.ArriveSound.Length > 0)
            Sound.EntityEmitSound(Handle, Vars.ArriveSound);
    }

    /// <summary>
    /// The biggest single-axis turn between here and there, which is how long the
    /// turn takes: the shell steps every axis at the same rate and finishes when
    /// the last of them arrives.
    /// </summary>
    protected float LargestTurn(Vector wanted)
    {
        Vector current = Entities.GetAngles(Handle);

        return MathF.Max(MathF.Abs(AngleDelta(wanted.X, current.X)),
               MathF.Max(MathF.Abs(AngleDelta(wanted.Y, current.Y)),
                         MathF.Abs(AngleDelta(wanted.Z, current.Z))));
    }

    /// <summary>The shortest way round from one angle to another, signed.</summary>
    protected static float AngleDelta(float to, float from)
    {
        float delta = (to - from) % 360f;

        if (delta > 180f)
            delta -= 360f;
        else if (delta < -180f)
            delta += 360f;

        return delta;
    }

    /// <summary>Within the assembly so a test can walk a journey with no engine to time it.</summary>
    internal void FinishJourney()
    {
        if (_state == PlatformState.GoingUp)
            HitTop();
        else if (_state == PlatformState.GoingDown)
            HitBottom();
    }
}

/// <summary>
/// The map keys <see cref="FuncPlatRot"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncPlatRotVars
{
    public float Speed { get; set; } = 150f;

    /// <summary>How far it drops. Zero means it works its own depth out from the brush.</summary>
    public float Height { get; set; }

    /// <summary>
    /// How far it turns between bottom and top, in degrees about one axis.
    ///
    /// Stock reads this into the X of a whole angle and then multiplies it by the
    /// axis it picked, which is why one number describes a turn about any of the
    /// three: the key says how far and the spawnflags say about what.
    /// </summary>
    public float Rotation { get; set; }

    [VarName("noise1")]
    public string MoveSound { get; set; } = string.Empty;

    [VarName("noise2")]
    public string ArriveSound { get; set; } = string.Empty;

    /// <summary>
    /// Declared so a map's value survives being read back, and used by nothing:
    /// the managed sound call plays at the entity's own attenuation and takes no
    /// volume. See docs/divergences.md.
    /// </summary>
    public float Volume { get; set; } = 0.85f;
}
