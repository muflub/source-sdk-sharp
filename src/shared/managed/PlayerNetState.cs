//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What the standalone player networks beyond what every entity on the
/// hierarchy does — and, since 9c5, WHAT THE PLAYER PREDICTS: every field the
/// movement loop reads or writes, so that saving this blob is saving the
/// player and comparing it against the server's word is the whole
/// misprediction check. One declaration is the wire format, the prediction
/// schema and the tolerance table at once, which is the plan's "one list
/// again, not a second one".
///
/// <para>
/// The native halves reach these fields THROUGH THE ASK, never through a
/// copied offset: the sync resolves each name once per publish via
/// NetStateFieldOffset (Step 7's rule), so reshaping this class cannot
/// silently land the loop's state in a neighbour's bytes.
/// </para>
///
/// <para>
/// Tolerances are STOCK'S, quoted not invented: origin's is c_baseentity.cpp's
/// coordTolerance (2/2^COORD_FRACTIONAL_BITS = 0.0625), velocity 0.5 and view
/// offset 0.25 from the same block, base velocity 0.05, max speed 0.5, fall
/// velocity 0.5 and the punch pair 0.125 from c_baseplayer.cpp. A field
/// without the attribute is compared exactly, which is also stock's default
/// (predictioncopy.cpp:736).
/// </para>
///
/// <para>
/// SENT TO EVERY CLIENT for now, not only the owner: the tier tables carry no
/// local-exclusive branch yet (recorded in docs/divergences.md — a Part 18
/// extension owns that).
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.Player)]
public partial class PlayerNetState
{
    // ---- whole words, in declaration order --------------------------------

    /// <summary>What the HUD shows, after the managed damage rules.</summary>
    public partial int Health { get; set; }

    /// <summary>
    /// The suit's armour, 0..100: a managed number (the native player's
    /// m_ArmorValue retired with CBasePlayer), written by the server player's
    /// damage rule and the suit pickups, read by the HUD. The native sync does
    /// not touch it (plan_client_ui U3).
    /// </summary>
    public partial int Armour { get; set; }

    /// <summary>
    /// The observer mode (stock's m_iObserverMode, OBS_MODE_*): 0 in play,
    /// 1 death cam, 2 freeze cam, 3 fixed, 4 in-eye, 5 chase, 6 POI, 7 roaming.
    /// Written by the server's Observers.
    /// </summary>
    public partial int ObserverMode { get; set; }

    /// <summary>
    /// Who is being watched (stock's m_hObserverTarget): the target's raw
    /// entity handle, 0xFFFFFFFF (EntityHandle.None) for nobody -- never 0,
    /// which names the world.
    /// </summary>
    public partial uint ObserverTarget { get; set; }

    /// <summary>The cap this player's commands run under.</summary>
    [Predicted(0.5f)]
    public partial float MaxSpeed { get; set; }

    /// <summary>Speed at the moment ground was hit — the fall-damage input.</summary>
    [Predicted(0.5f)]
    public partial float FallVelocity { get; set; }

    /// <summary>Duck transition countdown (milliseconds, stock's clock).</summary>
    [Predicted]
    public partial float DuckTime { get; set; }

    /// <summary>Duck-jump countdown.</summary>
    [Predicted]
    public partial float DuckJumpTime { get; set; }

    /// <summary>Time to auto-unduck after a crouch-jump.</summary>
    [Predicted]
    public partial float JumpTime { get; set; }

    /// <summary>The ledge height the loop steps over.</summary>
    [Predicted]
    public partial float StepSize { get; set; }

    /// <summary>Countdown while being pushed out of water onto a ledge.</summary>
    [Predicted]
    public partial float WaterJumpTime { get; set; }

    /// <summary>Friction of the surface stood on, as the loop last measured it.</summary>
    [Predicted]
    public partial float SurfaceFriction { get; set; }

    /// <summary>Last command's forward move, for the momentum rules.</summary>
    [Predicted]
    public partial float OldForwardMove { get; set; }

    /// <summary>FL_* — ONGROUND, DUCKING and friends. The loop's own bits.</summary>
    [Predicted]
    public partial uint Flags { get; set; }

    /// <summary>
    /// WHAT THEY ARE STANDING ON, as stock's networked ehandle word: the entry
    /// index in the low eleven bits, ten bits of serial above it, and
    /// INVALID_NETWORKED_EHANDLE_VALUE for nothing
    /// (game/shared/ground_handle_wire.h does the arithmetic).
    ///
    /// <para>
    /// Beside <see cref="Flags"/> because it is the fact FL_ONGROUND MIRRORS,
    /// and the mirror is not the original: the movement loop never reads the
    /// bit. It asks MvGetGroundEntity() — gamemovement.cpp:2475 refuses a jump
    /// on a NULL one, FullWalkMove picks WalkMove or AirMove by it, and the
    /// gravity zeroing is the same ask. Predicting the flags word without this
    /// handle hands the replay a contradiction at every ground-state
    /// transition: FL_ONGROUND from the acknowledged command, a handle left
    /// over from a later replayed one, and a jump the server took that the
    /// client refuses. Stock predicts it for this reason, in one line —
    /// c_baseplayer.cpp:405.
    /// </para>
    ///
    /// <para>
    /// No tolerance, and there is no tolerance to give: it is an identity, not
    /// a measurement. Two handles either name the same entity or they do not.
    /// </para>
    /// </summary>
    [Predicted]
    public partial int GroundEntity { get; set; }

    /// <summary>Last command's buttons, for the pressed/released edges.</summary>
    [Predicted]
    public partial int OldButtons { get; set; }

    /// <summary>
    /// The player's command clock. Exact by construction or the replay is off
    /// by a tick.
    ///
    /// <para>
    /// NO TOLERANCE, AND DELIBERATELY NOT GIVEN ONE. It disagrees exactly once
    /// per level load — the first compare of a session reads pred 1 against srv
    /// 99 — because the replay's first basis is the blob the server wrote at
    /// spawn, before it had run a command and therefore before its clock
    /// existed, and the server then re-bases that clock at its first command
    /// batch (RunQueuedCommands, mirroring CBasePlayer::AdjustPlayerTimeBase,
    /// player.cpp:3157). The client adopts the real value from the very next
    /// packet and every later compare is exact. Stock behaves identically and
    /// declares it the same way, with no tolerance (c_baseplayer.cpp:403).
    /// </para>
    ///
    /// <para>
    /// A number here would be permanently in force, and what it would hide is a
    /// replay integrating a different count of ticks than the server ran — the
    /// class of bug this field exists to catch. Recorded in docs/divergences.md
    /// rather than absorbed.
    /// </para>
    /// </summary>
    [Predicted]
    public partial int TickBase { get; set; }

    // ---- the packed word ---------------------------------------------------

    /// <summary>Playing rather than watching.</summary>
    public partial bool Alive { get; set; }

    /// <summary>Fully ducked.</summary>
    [Predicted]
    public partial bool Ducked { get; set; }

    /// <summary>Mid duck transition.</summary>
    [Predicted]
    public partial bool Ducking { get; set; }

    /// <summary>Mid duck-jump.</summary>
    [Predicted]
    public partial bool InDuckJump { get; set; }

    /// <summary>Whether the loop may auto-move (ladders and duck-jumps clear it).</summary>
    [Predicted]
    public partial bool AllowAutoMovement { get; set; }

    /// <summary>How submerged: 0 dry to 3 eyes-under.</summary>
    [Predicted]
    public partial byte WaterLevel { get; set; }

    /// <summary>CONTENTS_* of the water stood in.</summary>
    public partial byte WaterType { get; set; }

    /// <summary>MOVETYPE_* — walk, noclip, ladder. The loop branches on it.</summary>
    [Predicted]
    public partial byte MoveType { get; set; }

    // ---- the wide fields ---------------------------------------------------

    /// <summary>Where they are. Stock's coordTolerance, quoted above.</summary>
    [Predicted(0.0625f)]
    public partial Vector Origin { get; set; }

    /// <summary>How fast they move.</summary>
    [Predicted(0.5f)]
    public partial Vector Velocity { get; set; }

    /// <summary>Velocity the world imposed — pushes, explosions.</summary>
    [Predicted(0.05f)]
    public partial Vector BaseVelocity { get; set; }

    /// <summary>Eyes above feet; ducking moves it.</summary>
    [Predicted(0.25f)]
    public partial Vector ViewOffset { get; set; }

    /// <summary>View kick in flight.</summary>
    [Predicted(0.125f)]
    public partial QAngle PunchAngle { get; set; }

    /// <summary>View kick decay rate.</summary>
    [Predicted(0.125f)]
    public partial QAngle PunchAngleVel { get; set; }

    /// <summary>The ladder's facing, while on one.</summary>
    [Predicted]
    public partial Vector LadderNormal { get; set; }

    /// <summary>The push a water-jump imparted.</summary>
    [Predicted]
    public partial Vector WaterJumpVel { get; set; }

    // ---- the useable ladder (HL2's m_HL2Local.m_hLadder and LadderMove_t) --
    //
    // Stock networks these in the player's HL2 local data and predicts them
    // (c_hl2_playerlocaldata.cpp, c_basehlplayer.cpp): the movement loop's
    // CHL2GameMovement reads and writes every one, so the replay has to start
    // from the server's word for each. The tolerances are stock's: the two
    // times 0.02, the two positions not compared (FTYPEDESC_NOERRORCHECK)
    // but still the basis.

    /// <summary>
    /// The func_useableladder the player is on, by entity index; -1 for none
    /// (MoveState.NoLadder). An identity, so compared exactly.
    /// </summary>
    [Predicted]
    public partial int Ladder { get; set; }

    /// <summary><c>m_bForceLadderMove</c>: being slid on or off a ladder.</summary>
    [Predicted]
    public partial bool LadderForceMove { get; set; }

    /// <summary><c>m_bForceMount</c>: that slide ends on the ladder.</summary>
    [Predicted]
    public partial bool LadderForceMount { get; set; }

    /// <summary><c>m_flStartTime</c>: when the slide began.</summary>
    [Predicted(0.02f)]
    public partial float LadderStartTime { get; set; }

    /// <summary><c>m_flArrivalTime</c>: when it ends.</summary>
    [Predicted(0.02f)]
    public partial float LadderArrivalTime { get; set; }

    /// <summary><c>m_vecGoalPosition</c>: where the slide ends.</summary>
    public partial Vector LadderGoalPosition { get; set; }

    /// <summary><c>m_vecStartPosition</c>: where it began.</summary>
    public partial Vector LadderStartPosition { get; set; }

    /// <summary>
    /// <c>m_hForceLadder</c>: the ladder a mounting slide ends on, by entity
    /// index. Stock does not send it and its client keeps its own; it is sent
    /// here so a replay that starts mid-slide ends on the same ladder.
    /// </summary>
    [Predicted]
    public partial int LadderForceLadder { get; set; }

    // ---- HL2MP's suit (m_HL2Local's power words and m_bNewSprinting) -------
    //
    // Stock networks these in the player's HL2 local data and predicts them,
    // exactly (c_hl2_playerlocaldata.cpp): the movement loop's HL2 player
    // (GameMovement.Hl2Player) picks the sprint from the power and drains and
    // recharges it every command, on both sides.

    /// <summary><c>m_flSuitPower</c>: 0 to 100; the AUX POWER bar.</summary>
    [Predicted]
    public partial float SuitPower { get; set; }

    /// <summary><c>m_flSuitPowerLoad</c>: the drain of the devices on.</summary>
    [Predicted]
    public partial float SuitPowerLoad { get; set; }

    /// <summary><c>m_bitsActiveDevices</c>.</summary>
    [Predicted]
    public partial int SuitDevices { get; set; }

    /// <summary><c>m_flTimeAllSuitDevicesOff</c>: when the last device went off.</summary>
    [Predicted]
    public partial float SuitDevicesOffTime { get; set; }

    /// <summary><c>m_bNewSprinting</c>.</summary>
    [Predicted]
    public partial bool NewSprinting { get; set; }

    // ---- the world fog this viewer is standing in (step 8e-render) ---------
    //
    // WHY THE PLAYER CARRIES IT. In stock, fog reaches a client two ways at
    // once: CFogController networks its own m_fog through DT_FogController, and
    // CPlayerLocalData carries a handle to the controller the server assigned
    // THIS player. Here fogcontroller.cpp is entirely `#ifndef SOURCESHARP` and
    // env_fog_controller is EnvFogController.cs, so server.so declares no
    // DT_FogController and there was NO SENDER for either half -- world fog
    // rendered off on every frame since the flip, and step 7c made that a
    // printed ledger row rather than a working feature. The player is the
    // carrier because the fog a viewer sees is per-viewer state in stock too:
    // SetFogController is an input on a PLAYER, and a trigger_fog changes one
    // player's fog and nobody else's.
    //
    // NOT [Predicted], and none of them ever should be. Prediction compares the
    // client's replayed state against the server's word; the movement loop
    // neither reads nor writes any of these, so a tolerance on them would put
    // fog on the misprediction ring for a value that can only ever agree.
    //
    // THE VALUES ARE RESOLVED, NOT RAW. EnvFogController.OnThink runs the fog
    // transition on the server and publishes the result, so the lerp targets
    // stock networks (colorPrimaryLerpTo, startLerpTo, endLerpTo, lerptime,
    // duration) are deliberately absent -- shipping them as well would be two
    // implementations of one transition with no way to tell which one a
    // screenshot came from.
    //
    // THE COST, stated because it is real: ten more words in every player's
    // blob, and the tier tables have no local-exclusive branch yet, so they go
    // to every client rather than only to the viewer they describe (the same
    // divergence this class's header already records). Today they hold the same
    // numbers for every player, because EnvFogController has one master and no
    // per-player assignment; per-player fog is what this shape leaves room for
    // and is not implemented.

    /// <summary>Whether this viewer has a fog controller at all.</summary>
    public partial bool FogValid { get; set; }

    /// <summary>Whether that controller's fog is switched on.</summary>
    public partial bool FogEnable { get; set; }

    /// <summary>Blended fog: two colours, chosen by view direction.</summary>
    public partial bool FogBlend { get; set; }

    /// <summary>Radial rather than planar distance.</summary>
    public partial bool FogRadial { get; set; }

    /// <summary>Where the secondary colour comes from, for a blended fog.</summary>
    public partial Vector FogDirPrimary { get; set; }

    /// <summary>Packed RGBA in the engine's color32 byte order.</summary>
    public partial uint FogColorPrimary { get; set; }

    /// <summary>The second colour of a blended fog, packed the same way.</summary>
    public partial uint FogColorSecondary { get; set; }

    /// <summary>Where the fog begins.</summary>
    public partial float FogStart { get; set; }

    /// <summary>Where it reaches full density.</summary>
    public partial float FogEnd { get; set; }

    /// <summary>How far the far plane is pushed. Zero or less is "the mapper set none".</summary>
    public partial float FogFarZ { get; set; }

    /// <summary>The densest the fog gets.</summary>
    public partial float FogMaxDensity { get; set; }

    // ---- the 3D skybox this viewer is shown (step defect-skybox) -----------
    //
    // WHY THE PLAYER CARRIES IT, and here the answer is simply STOCK'S: the
    // destination is CPlayerLocalData::m_skybox3d, a CNetworkVarEmbedded on the
    // player's own local-data table (playerlocaldata.h:81), and
    // c_baseplayer.cpp:185-198 receives it there. The 3D skybox is per-viewer
    // state in stock and it is per-viewer state here.
    //
    // THIS IS THE SECOND HALF OF A TWO-PART FAULT AND ONLY THE FIRST WAS FIXED.
    // Lane phase4-exit found the reachability half: UpdateAllClientData walked
    // UTIL_PlayerByIndex, which cannot answer on this build, so
    // ClientData_Update never ran and SourceSharp_FillSkyboxParams -- its only
    // caller -- was never reached. That is fixed and
    // StandaloneClientData_Update now writes MvLocal().m_skybox3d every frame.
    // What it writes still goes nowhere: CStandalonePlayer does not network
    // CPlayerLocalData, it networks THIS BLOB, and the blob had no skybox in
    // it. The server has been filling a struct nothing sends since that fix.
    //
    // NOT [Predicted], for the same reason the fog fields are not: the movement
    // loop neither reads nor writes any of them, so a tolerance here would put
    // the skybox on the misprediction ring for a value that can only agree.
    //
    // THE COST, stated because it is real: thirteen more fields in every
    // player's blob (three vectors, two ints, two uints, three floats and three
    // bools), and the tier tables still have no local-exclusive branch, so they
    // go to every client rather than only to the viewer they describe -- the
    // divergence this class's header already records, paid a second time. They
    // hold the same numbers for every player today, because a map has one sky
    // camera; per-player skyboxes are what the shape leaves room for and are
    // not implemented.

    /// <summary>Whether a sky camera has been heard from at all. See <see cref="SkyState"/>.</summary>
    public partial bool SkyValid { get; set; }

    /// <summary>Where in the world the miniature sits. The client subtracts this from the eye.</summary>
    public partial Vector SkyOrigin { get; set; }

    /// <summary>How many times smaller the miniature is than the real thing.</summary>
    public partial int SkyScale { get; set; }

    /// <summary>The visleaf area the camera is in. 255 is the engine's "nowhere".</summary>
    public partial int SkyArea { get; set; }

    /// <summary>Whether the skybox has fog of its own — a separate volume from the world's.</summary>
    public partial bool SkyFogEnable { get; set; }

    /// <summary>Blended skybox fog: two colours, chosen by view direction.</summary>
    public partial bool SkyFogBlend { get; set; }

    /// <summary>Radial rather than planar distance, in the skybox.</summary>
    public partial bool SkyFogRadial { get; set; }

    /// <summary>Where the secondary skybox colour comes from, for a blended fog.</summary>
    public partial Vector SkyFogDirPrimary { get; set; }

    /// <summary>Packed RGBA in the engine's color32 byte order.</summary>
    public partial uint SkyFogColorPrimary { get; set; }

    /// <summary>The second colour of a blended skybox fog, packed the same way.</summary>
    public partial uint SkyFogColorSecondary { get; set; }

    /// <summary>Where the skybox's fog begins.</summary>
    public partial float SkyFogStart { get; set; }

    /// <summary>Where it reaches full density.</summary>
    public partial float SkyFogEnd { get; set; }

    /// <summary>The densest the skybox's fog gets.</summary>
    public partial float SkyFogMaxDensity { get; set; }

    // ---- the weapon-selection HUD (stock's m_hMyWeapons and m_iAmmo) --------
    //
    // Stock's client walks the weapons the player carries and asks each
    // CanBeSelected. Only the active weapon is an entity here, so the server
    // says both answers as bits, one per WeaponTypes index.

    /// <summary>The weapons carried: bit <c>1 &lt;&lt; WeaponTypeId</c>.</summary>
    public partial uint CarriedWeapons { get; set; }

    /// <summary>
    /// The carried weapons that cannot be selected: stock's CanBeSelected is
    /// false, which is HasAmmo's answer (no clip, no pool, no select-on-empty).
    /// </summary>
    public partial uint EmptyWeapons { get; set; }
}
