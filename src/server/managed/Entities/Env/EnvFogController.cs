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
/// The map's world fog: where it starts, where it ends, what colour it is, and
/// how it changes.
///
/// <para>
/// The 3D-skybox fog has been C#'s since sky_camera was ported; this is the
/// other half, and the two now sit next to each other rather than one on each
/// side of the boundary.
/// </para>
///
/// <para>
/// <b>What this entity does NOT do, and did not do in C++ either.</b> Stock
/// delivers fog to a client through the PLAYER: CBasePlayer::InitFogController
/// puts the master controller's handle in <c>m_Local.m_PlayerFog.m_hCtrl</c>,
/// it networks from there, and C_BasePlayer::GetFogParams is what viewrender
/// asks. Neither half is in this build — <c>player.o</c> is not on the server
/// link line and <c>c_baseplayer.o</c> is not on the client's — so the three
/// writers of that handle and its one reader are all gone, and the fog a
/// C++ CFogController computed reached nobody. It was a sender whose receiver
/// had left AND a receiver whose reader had left, which is the sharper half of
/// the eviction rule stated twice over.
/// </para>
///
/// <para>
/// So this port neither adds nor removes a rendered pixel; what it removes is
/// 86 references from <c>fogcontroller.o</c> and, because the send table goes
/// with it, 181 more from <c>c_env_fog_controller.o</c>. The state is kept and
/// the inputs answered faithfully so that when a fog channel to the managed
/// client is built, it has something correct to read.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvFogController)]
public sealed class EnvFogController : ScriptedEntity
{
    public EnvFogController(IEngineWorldQuery world)
    {
        World = world;
    }

    private readonly IEngineWorldQuery World;

    /// <summary>What a map may set on this entity. See <see cref="EnvFogControllerVars"/>.</summary>
    [ScriptVars] public EnvFogControllerVars Vars = new();

    /// <summary>The spawnflag that makes this the one the map means.</summary>
    private const int MasterFlag = 1;

    /// <summary>Which channels a Lerp input has armed. Stock's m_iChangedVariables.</summary>
    [Flags]
    internal enum FogChannel
    {
        None             = 0,
        ColourPrimary    = 1,
        ColourSecondary  = 2,
        Start            = 4,
        End              = 8,
    }

    //-------------------------------------------------------------------------
    // The live state. Public so the fog channel, when there is one, reads it
    // from here rather than from the map keys — an entity that has been sent a
    // SetColor is not the entity the map described.
    //-------------------------------------------------------------------------

    public bool Enabled { get; private set; }

    /// <summary>
    /// The live fog colours. Parsed ONCE from the map keys in
    /// <see cref="OnSpawn"/> (and moved by the SetColor inputs), which a hot
    /// reload does not run again -- so they are <c>[SaveField]</c>. Uncarried
    /// they would come back black after a <c>cs_sv_reload</c>
    /// (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] public uint ColourPrimary { get; private set; }

    /// <inheritdoc cref="ColourPrimary"/>
    [SaveField] public uint ColourSecondary { get; private set; }
    public float Start { get; private set; }
    public float End { get; private set; }
    public float MaxDensity { get; private set; } = 1f;
    public float FarZ { get; private set; }
    public bool Radial { get; private set; }
    public Vector DirPrimary { get; private set; }

    /// <summary>Whether the map flagged this one as the master.</summary>
    public bool IsMaster => (SpawnFlags & MasterFlag) != 0;

    // The armed-but-not-yet-committed side of a fog transition.
    /// <summary>
    /// Primed from the live colours in <see cref="OnSpawn"/>, which a hot reload
    /// does not run again -- so both colours are <c>[SaveField]</c>: a
    /// transition armed before a <c>cs_sv_reload</c> still commits the colour
    /// it was aiming at (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private uint _colourPrimaryLerpTo;
    [SaveField] private uint _colourSecondaryLerpTo;
    private float _startLerpTo;
    private float _endLerpTo;

    internal FogChannel Armed { get; private set; } = FogChannel.None;

    public override void OnSpawn()
    {
        Enabled         = Vars.FogEnable;
        ColourPrimary   = SkyCamera.ParseColour(Vars.FogColor);
        ColourSecondary = SkyCamera.ParseColour(Vars.FogColor2);
        Start           = Vars.FogStart;
        End             = Vars.FogEnd;
        MaxDensity      = Vars.FogMaxDensity;
        FarZ            = Vars.FarZ;
        Radial          = Vars.FogRadial;
        DirPrimary      = Vars.FogDir;

        // Both LerpTo colours start equal to their live value, so a transition
        // that arms only one channel does not snap the other to black.
        _colourPrimaryLerpTo   = ColourPrimary;
        _colourSecondaryLerpTo = ColourSecondary;

        // Stock does this in Activate rather than Spawn, because it wants the
        // angles after any parenting has settled. Nothing parents a fog
        // controller, and this hierarchy has no parenting to wait for.
        if (Vars.UseAngles)
            DirPrimary = Negate(Angles.Forward(SpawnAngles));
    }

    //-------------------------------------------------------------------------
    // Immediate inputs.
    //-------------------------------------------------------------------------

    [ScriptedEvent] private void OnSetStartDist(float value) => Start = value;
    [ScriptedEvent] private void OnSetEndDist(float value) => End = value;
    [ScriptedEvent] private void OnSetMaxDensity(float value) => MaxDensity = value;
    [ScriptedEvent] private void OnTurnOn() => Enabled = true;
    [ScriptedEvent] private void OnTurnOff() => Enabled = false;
    [ScriptedEvent] private void OnSetFarZ(float value) => FarZ = value;
    [ScriptedEvent] private void OnSetRadial(float value) => Radial = value != 0f;

    [ScriptedEvent] private void OnSetColor(string value)
        => ColourPrimary = SkyCamera.ParseColour(value);

    [ScriptedEvent] private void OnSetColorSecondary(string value)
        => ColourSecondary = SkyCamera.ParseColour(value);

    /// <summary>
    /// "P Y R" from a map, turned into the fog direction.
    ///
    /// Stock also calls SetAbsAngles here, which moves the entity as well as
    /// the fog. A point entity's angles are read by nothing else on this
    /// hierarchy, so the direction is all that is kept.
    /// </summary>
    [ScriptedEvent] private void OnSetAngles(string value)
    {
        DirPrimary = Negate(Angles.Forward(ParseVector(value)));
    }

    //-------------------------------------------------------------------------
    // The transition.
    //
    // Stock's name for it is a lerp, and the SERVER does not interpolate
    // anything: it arms channels, waits `duration`, and snaps each armed one to
    // its target. The interpolation is the CLIENT's, from `lerptime` and
    // `duration`, which is why the server's half looks like it is missing the
    // arithmetic. It is not missing it; it never had it.
    //-------------------------------------------------------------------------

    [ScriptedEvent] private void OnSetColorLerpTo(string value)
    {
        Armed |= FogChannel.ColourPrimary;
        _colourPrimaryLerpTo = SkyCamera.ParseColour(value);
    }

    [ScriptedEvent] private void OnSetColorSecondaryLerpTo(string value)
    {
        Armed |= FogChannel.ColourSecondary;
        _colourSecondaryLerpTo = SkyCamera.ParseColour(value);
    }

    [ScriptedEvent] private void OnSetStartDistLerpTo(float value)
    {
        Armed |= FogChannel.Start;
        _startLerpTo = value;
    }

    [ScriptedEvent] private void OnSetEndDistLerpTo(float value)
    {
        Armed |= FogChannel.End;
        _endLerpTo = value;
    }

    [ScriptedEvent] private void OnStartFogTransition() => SetNextThink(Vars.FogLerpTime);

    public override void OnThink()
    {
        CommitTransition();
        StopThinking();
    }

    /// <summary>
    /// Commits every armed channel and disarms. Separated from
    /// <see cref="OnThink"/> so it can be driven without a clock.
    /// </summary>
    /// <summary>
    /// Away from the facing rather than along it. Stock writes
    /// `dirPrimary *= -1.0f` after AngleVectors, and the sign is not cosmetic:
    /// fogdir names the direction the SECOND colour is seen in, which is the
    /// direction light comes FROM, i.e. the opposite of where the entity looks.
    /// </summary>
    private static Vector Negate(Vector v) => new(-v.X, -v.Y, -v.Z);

    internal void CommitTransition()
    {
        if ((Armed & FogChannel.ColourPrimary) != 0)
            ColourPrimary = _colourPrimaryLerpTo;

        if ((Armed & FogChannel.ColourSecondary) != 0)
            ColourSecondary = _colourSecondaryLerpTo;

        if ((Armed & FogChannel.Start) != 0)
            Start = _startLerpTo;

        if ((Armed & FogChannel.End) != 0)
            End = _endLerpTo;

        Armed = FogChannel.None;
    }
}

/// <summary>
/// The map keys <see cref="EnvFogController"/> declares. The names are stock's
/// keyfields and belong to maps rather than to this file.
/// </summary>
[ScriptVars]
public partial class EnvFogControllerVars
{
    /// <summary>"fogenable" — whether the fog is on at all to begin with.</summary>
    public bool FogEnable { get; set; }

    /// <summary>"fogblend" — whether the two colours blend by view direction.</summary>
    public bool FogBlend { get; set; }

    /// <summary>"fogradial" — distance measured from the eye rather than along the view.</summary>
    public bool FogRadial { get; set; }

    /// <summary>
    /// "use_angles" — take the fog direction from this entity's angles.
    ///
    /// Spelled out because key matching is case-insensitive but not
    /// underscore-insensitive: without this the key would be "useangles" and no
    /// map in existence writes that.
    /// </summary>
    [VarName("use_angles")] public bool UseAngles { get; set; }

    /// <summary>"fogcolor", "R G B".</summary>
    public string FogColor { get; set; } = string.Empty;

    /// <summary>"fogcolor2", "R G B". The colour looking INTO fogdir.</summary>
    public string FogColor2 { get; set; } = string.Empty;

    /// <summary>"fogdir" — which way the second colour faces.</summary>
    public Vector FogDir { get; set; }

    /// <summary>"fogstart" — how far away the fog begins.</summary>
    public float FogStart { get; set; }

    /// <summary>"fogend" — how far away it is fully opaque.</summary>
    public float FogEnd { get; set; }

    /// <summary>"fogmaxdensity" — how opaque "fully opaque" is. One is stock's default.</summary>
    public float FogMaxDensity { get; set; } = 1f;

    /// <summary>"farz" — the far clip plane, which a map usually ties to fogend.</summary>
    public float FarZ { get; set; }

    /// <summary>"foglerptime" — how long a transition takes.</summary>
    public float FogLerpTime { get; set; }
}
