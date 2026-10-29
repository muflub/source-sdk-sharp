using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// What 3D skybox each viewer is shown, copied into each player's networked
/// state once a frame — the <c>defect-skybox</c> lane.
///
/// <para>
/// <b>THE DEFECT THIS CLOSES, AND IT IS THE SECOND HALF OF ONE.</b> The 3D
/// skybox has not rendered since the flip and was the LAST entry in
/// <c>ContractBaseline.OpenDefects</c>. Lane <c>phase4-exit</c> found and fixed
/// the first half — <c>UpdateAllClientData</c> walked
/// <c>UTIL_PlayerByIndex</c>, which cannot answer on this build, so
/// <c>ClientData_Update</c> never ran and <c>SourceSharp_FillSkyboxParams</c>,
/// its only caller, was never reached. That fix landed and works: the managed
/// <c>sky_camera</c> is heard, and <c>StandaloneClientData_Update</c> writes
/// <c>MvLocal().m_skybox3d</c> every frame.
/// </para>
///
/// <para>
/// <b>AND IT STILL WENT NOWHERE.</b> <c>CStandalonePlayer</c> does not network
/// <c>CPlayerLocalData</c> — it networks <see cref="PlayerNetState"/>, and that
/// carried no skybox. Measured live on <c>ss_sandbox</c> before this class
/// existed: 12,270 consecutive frames in which the engine said a 3D skybox WAS
/// visible from the eye and <c>SourceSharp_ViewerSkyboxParams</c> answered NULL
/// every single time. So the remaining fault is the WIRE, which is what
/// <c>ContractBaseline</c>'s entry said it was, and it needed the reachability
/// fix first for the server to have anything worth sending.
/// </para>
///
/// <para>
/// <b>RECONCILED, NOT NOTIFIED.</b> Deliberately <see cref="WorldFog"/>'s shape
/// and for <see cref="WorldFog"/>'s stated reason: a player can spawn on any
/// frame, so a design where the entity pushes at spawn is one edge to forget,
/// in a subsystem whose failure mode is silent. This compares instead — once a
/// frame it reads the sky camera and writes into any player whose blob does not
/// already say that. A player that arrives mid-level is covered by the same
/// line as one that was there all along, and a write missed for any reason is
/// retried on the next frame.
/// </para>
///
/// <para>
/// <b>THE LAST CAMERA TO SPAWN WINS,</b> which is <see cref="SkyCamera"/>'s own
/// rule restated rather than a second opinion: that class pushes to
/// <c>SourceSharp_SetSkyboxParams</c> at every spawn and the last push is what
/// the native side keeps. Stock takes the HEAD of its <c>CSkyCamera</c> list
/// and says "UNDONE: Support multiple sky cameras?" beside it; one map has one
/// camera, and the two arbitrary choices differ only on maps that are already
/// undefined.
/// </para>
///
/// <para>
/// <b>THE COST, stated as a number.</b> One directory walk over
/// <c>sky_camera</c> (a map has one) and one record-struct comparison per
/// player per frame, and NO writes on a still map — <see cref="Polls"/> counts
/// the frames and <see cref="Writes"/> the blob writes, so "it is not costing
/// anything" is measurable rather than claimed. Plus thirteen fields in every
/// player's blob, which <see cref="PlayerNetState"/> records at the fields
/// themselves.
/// </para>
/// </summary>
public sealed class WorldSkybox : IFrameLifecycle, IGameLifecycle
{
    public WorldSkybox(IEntityDirectory entities)
    {
        _entities = entities;
    }

    private readonly IEntityDirectory _entities;

    /// <inheritdoc/>
    public int FrameOrder => (int)FrameStage.WorldSkybox;

    /// <inheritdoc/>
    public int LifecycleOrder => (int)LifecycleStage.WorldSkybox;

    /// <summary>What the sky camera last said. <c>Valid</c> false when there is none.</summary>
    public SkyState Current { get; private set; }

    /// <summary>How many frames have looked. For the report and for the gate.</summary>
    public int Polls { get; private set; }

    /// <summary>How many player blobs have been written. Zero on a still map after the first.</summary>
    public int Writes { get; private set; }

    /// <summary>How many players the last poll reconciled, written or not.</summary>
    public int LastSeen { get; private set; }

    //-------------------------------------------------------------------------
    // The frame.
    //-------------------------------------------------------------------------

    /// <inheritdoc/>
    void IFrameLifecycle.OnPreClientUpdate(float frameTime) => Update();

    /// <summary>
    /// Reads the sky camera and reconciles every live player against it.
    /// Public so a test can drive it without a clock.
    /// </summary>
    public void Update()
    {
        Polls++;
        Current = Read();

        int seen = 0;

        foreach (var entity in _entities.OfClass(EntityNames.Player))
        {
            if (entity is not SsPlayer player)
                continue;

            seen++;

            // THE COMPARISON IS AGAINST THE BLOB, not against a remembered
            // publish -- WorldFog.Update's reasoning, and the same three
            // properties fall out of it: a player that spawned this frame has a
            // zeroed blob and is written, a player whose blob already says this
            // is skipped, and a write lost to anything is retried next frame.
            if (SkyState.From(player.State) == Current)
                continue;

            Current.WriteTo(player.State);
            Writes++;
        }

        LastSeen = seen;
    }

    /// <summary>
    /// The sky camera's numbers, or <see cref="SkyState.None"/>.
    ///
    /// <para>
    /// <c>OfClass</c> and not the engine's list, for <see cref="WorldFog"/>'s
    /// reason: <c>sky_camera</c> is a managed class, so the linked list stock's
    /// <c>GetCurrentSkyCamera()</c> walks is empty by construction and every
    /// managed entity appears in the engine's list as one of a handful of shell
    /// classes.
    /// </para>
    ///
    /// <para>
    /// Reads <see cref="SkyCamera.Params"/>, the block that entity already
    /// resolved at spawn — its area, its fog direction and its two parsed
    /// colours — rather than re-deriving any of it. One resolution of the map's
    /// keys, two destinations.
    /// </para>
    /// </summary>
    public SkyState Read()
    {
        SkyCamera? chosen = null;

        foreach (var entity in _entities.OfClass(EntityNames.SkyCamera))
        {
            if (entity is SkyCamera sky)
                chosen = sky;
        }

        if (chosen is null)
            return SkyState.None;

        SkyboxParams p = chosen.Params;

        return new SkyState(
            Valid: true,
            Origin: new Vector(p.OriginX, p.OriginY, p.OriginZ),
            Scale: p.Scale,
            Area: p.Area,
            FogEnable: p.FogEnable != 0,
            FogBlend: p.FogBlend != 0,
            FogRadial: p.FogRadial != 0,
            FogDirPrimary: new Vector(p.FogDirX, p.FogDirY, p.FogDirZ),
            FogColorPrimary: p.FogColorPrimary,
            FogColorSecondary: p.FogColorSecondary,
            FogStart: p.FogStart,
            FogEnd: p.FogEnd,
            FogMaxDensity: p.FogMaxDensity);
    }

    //-------------------------------------------------------------------------
    // The level.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Forgotten between levels, so the counters describe one map and nothing
    /// survives a level change that could make the next map's first poll skip a
    /// write.
    /// </summary>
    void IGameLifecycle.OnLevelShutdown()
    {
        Current = SkyState.None;
        Polls = 0;
        Writes = 0;
        LastSeen = 0;
    }
}

/// <summary>
/// What the server's 3D skybox service is doing, as counts.
///
/// <para>
/// The server half of the gate, and <c>cs_sv_fog</c>'s twin. <c>cs_cl_skybox</c>
/// says whether a skybox reached the renderer; this says whether there was one
/// to reach it. A map with no <c>sky_camera</c> and a wire that has stopped
/// working produce the same screenshot and the same client reading; only this
/// tells them apart.
/// </para>
/// </summary>
[ConCommand("cs_sv_skybox",
            Help = "What the server's 3D skybox service is doing: polls, blob writes, "
                 + "players seen, and what the map's sky_camera says.")]
public sealed class WorldSkyboxReportCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    public WorldSkyboxReportCommand(ILog log, WorldSkybox skybox)
    {
        _log = log;
        _skybox = skybox;
    }

    private readonly WorldSkybox _skybox;

    public void Execute(string args)
    {
        SkyState now = _skybox.Read();

        _log.Msg($"[SourceSharp] skybox: {_skybox.Polls} poll(s), {_skybox.Writes} blob write(s), "
              + $"{_skybox.LastSeen} player(s) on the last poll\n");

        _log.Msg($"[SourceSharp] skybox: sky camera {(now.Valid ? "present" : "NONE")}\n");

        if (now.Valid)
        {
            _log.Msg($"[SourceSharp] skybox: origin {now.Origin.X:F0} {now.Origin.Y:F0} "
                  + $"{now.Origin.Z:F0} scale {now.Scale} area {now.Area}\n");
            _log.Msg($"[SourceSharp] skybox: fog enable {(now.FogEnable ? 1 : 0)} "
                  + $"blend {(now.FogBlend ? 1 : 0)} radial {(now.FogRadial ? 1 : 0)} "
                  + $"start {now.FogStart:F1} end {now.FogEnd:F1} maxdensity {now.FogMaxDensity:F3}\n");
        }

        // WHICH FAILURE, said rather than left to be inferred from zeroes.
        if (_skybox.Polls == 0)
        {
            _log.Msg("[SourceSharp] skybox: NOTHING IS POLLING — WorldSkybox is not registered "
                  + "with the frame driver, so no player will ever be given a skybox\n");
        }
        else if (!now.Valid)
        {
            _log.Msg("[SourceSharp] skybox: this map declares no sky_camera, so there is no 3D "
                  + "skybox to send. That is an ordinary map and not a fault\n");
        }
        else if (now.Area == 255)
        {
            _log.Msg("[SourceSharp] skybox: the sky camera resolved area 255, the engine's "
                  + "\"nowhere\" — it is outside the sealed world, or the map was compiled "
                  + "without vis. PreRender3dSkyboxWorld refuses that on its own line\n");
        }
        else if (_skybox.LastSeen == 0)
        {
            _log.Msg("[SourceSharp] skybox: there is a sky camera and no players to give it to\n");
        }
    }
}
