using SourceSharp.Abi;

using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The last link of the 3D-skybox wire: the LOCAL player's networked sky
/// camera, into the pointer <c>viewrender.cpp</c> asks for — the
/// <c>defect-skybox</c> lane.
///
/// <para>
/// <b>WHY THIS CLASS IS THE PART MOST LIKELY TO BE LOST</b> — the same argument
/// <see cref="ClientWorldFog"/> makes, and it is not rhetorical here: a prop
/// that is not declared does not compile, and a field the server never writes
/// shows up as a zero in a report, but this runs once a frame over state that
/// arrives on its own and calls into native. If it is never called the game
/// runs perfectly and no skybox draws, which is precisely what this tree looked
/// like for every wave the defect was open. That is why <see cref="Pushes"/>
/// and <see cref="Clears"/> exist, and why the gate reads a DRAW COUNT out of
/// native rather than looking at the screen.
/// </para>
///
/// <para>
/// <b>THE LOCAL PLAYER, NOT ANY PLAYER.</b> Every player's blob carries the
/// skybox today, because the tier tables have no local-exclusive branch yet
/// (<c>docs/divergences.md</c>). The skybox a client RENDERS is the viewer's,
/// so this resolves the local index and takes that player's copy; taking
/// whichever player was found first would be right by accident today and wrong
/// the moment a map gives two players different sky cameras.
/// </para>
///
/// <para>
/// <b>EDGE-TRIGGERED.</b> The push is a struct copy across the ABI and the
/// values change when a mapper's entity says so, which for a sky camera is once
/// per map. The comparison is against what was last PUSHED rather than against
/// the blob, because the blob is the server's word and the native copy is what
/// a re-push would change.
/// </para>
/// </summary>
public sealed class ClientSkybox
{
    public ClientSkybox(IClientStandalones standalones, IEngineHud screen,
                        IEngineVisibility visibility)
    {
        _standalones = standalones;
        _screen = screen;
        _visibility = visibility;
    }

    private readonly IClientStandalones _standalones;
    private readonly IEngineHud _screen;
    private readonly IEngineVisibility _visibility;

    /// <summary>What was last handed to native, and whether anything was.</summary>
    private SkyState _pushed;
    private bool _havePushed;

    /// <summary>How many times a skybox has been handed to the renderer.</summary>
    public int Pushes { get; private set; }

    /// <summary>How many times the renderer has been told there is no sky camera.</summary>
    public int Clears { get; private set; }

    /// <summary>How many frames have looked. Zero means this is not being called.</summary>
    public int Passes { get; private set; }

    /// <summary>Whether the last pass found a local player to read a skybox from.</summary>
    public bool SawViewer { get; private set; }

    /// <summary>
    /// One frame. Called from <c>ClientModule.OnStage</c> at
    /// <c>NetUpdatePostDataUpdateEnd</c> — the stage the decode has just
    /// finished on, and the stage <see cref="ClientWorldFog"/> runs at, so the
    /// skybox the renderer draws with this frame is the one that arrived this
    /// frame rather than last frame's.
    /// </summary>
    public void Pass()
    {
        Passes++;

        ClientSsPlayer? viewer = FindLocalPlayer();
        SawViewer = viewer is not null;

        // NO VIEWER IS A CLEAR, NOT A SKIP. Between a level change and the
        // first player update there is no viewer, and the previous map's
        // miniature world would otherwise go on being drawn over the new one --
        // at the previous map's visleaf area, which is the shape of bug that
        // renders a corner of another level into the sky.
        SkyState wanted = viewer is null ? SkyState.None : SkyState.From(viewer.State);

        if (_havePushed && wanted == _pushed)
            return;

        _pushed = wanted;
        _havePushed = true;

        if (!wanted.Valid)
        {
            // CLEARED, WHICH IS NOT AREA 255 — see Engine.ClearViewerSkyboxParams.
            _visibility.ClearViewerSkyboxParams();
            Clears++;
            return;
        }

        var native = new SkyboxParams
        {
            OriginX = wanted.Origin.X,
            OriginY = wanted.Origin.Y,
            OriginZ = wanted.Origin.Z,
            Scale = wanted.Scale,
            Area = wanted.Area,
            FogEnable = wanted.FogEnable ? 1 : 0,
            FogBlend = wanted.FogBlend ? 1 : 0,
            FogRadial = wanted.FogRadial ? 1 : 0,
            FogDirX = wanted.FogDirPrimary.X,
            FogDirY = wanted.FogDirPrimary.Y,
            FogDirZ = wanted.FogDirPrimary.Z,
            FogColorPrimary = wanted.FogColorPrimary,
            FogColorSecondary = wanted.FogColorSecondary,
            FogStart = wanted.FogStart,
            FogEnd = wanted.FogEnd,
            FogMaxDensity = wanted.FogMaxDensity,
        };

        _visibility.SetViewerSkyboxParams(in native);
        Pushes++;
    }

    /// <summary>
    /// Forgotten at level init, so the first pass of a new map pushes even when
    /// the new map's sky camera happens to equal the old map's.
    /// </summary>
    public void LevelInit()
    {
        _pushed = SkyState.None;
        _havePushed = false;
        Pushes = 0;
        Clears = 0;
        Passes = 0;
        SawViewer = false;
    }

    /// <summary>
    /// The viewer, re-resolved per frame rather than held, for the reason
    /// <c>ClientPlayerPrediction.FindLocalPlayer</c> gives: the entity can
    /// arrive, die or be replaced between any two frames, and a stale reference
    /// is the shape of bug that survives a whole session.
    /// </summary>
    private ClientSsPlayer? FindLocalPlayer()
    {
        int local = _screen.LocalPlayerIndex;

        if (local <= 0)
            return null;

        foreach (var entity in _standalones.OfClass(EntityNames.SsPlayer))
        {
            if (entity is ClientSsPlayer player &&
                (int)(player.Handle.Value & HandleEntryMask) == local)
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>The entry index inside a handle. <c>ClientWorldFog</c>'s constant.</summary>
    private const uint HandleEntryMask = (1u << 11) - 1u;
}

/// <summary>
/// Whether the 3D skybox is reaching this client, as counts.
///
/// <para>
/// THE COMMAND EXISTS BECAUSE THE FEATURE IS INVISIBLE WHEN IT FAILS. A skybox
/// that never arrives renders as no skybox, a map with no sky camera renders as
/// no skybox, and a map with no sky FACE over the player renders as no skybox:
/// three different states, one screenshot. This says whether anything was
/// pushed; <c>cs_cl_skybox_report</c> (native) says whether the renderer then
/// drew one; <c>cs_sv_skybox</c> says whether there was one to send.
/// </para>
/// </summary>
[ConCommand("cs_cl_skybox",
            Help = "Whether the viewer's 3D skybox is reaching this client: passes, "
                 + "pushes, clears, and whether a viewer was found.")]
public sealed class ClientSkyboxReportCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly ClientSkybox? _sky;

    /// <summary>
    /// The module's skybox, from its container; null when the container has
    /// none, which is reported rather than dereferenced.
    /// </summary>
    public ClientSkyboxReportCommand(ILog log, ClientSkybox? sky = null)
    {
        _log = log;
        _sky = sky;
    }

    public void Execute(string args)
    {
        ClientSkybox? sky = _sky;

        if (sky is null)
        {
            _log.Msg("[SourceSharp/Client] skybox: the client module has not built its services yet\n");
            return;
        }

        _log.Msg($"[SourceSharp/Client] skybox: {sky.Passes} pass(es), {sky.Pushes} push(es), "
              + $"{sky.Clears} clear(s), viewer {(sky.SawViewer ? "found" : "NOT found")}\n");

        // SAID OUT LOUD, because counts do not tell a reader which failure they
        // are looking at and there are three distinct ones.
        if (sky.Passes == 0)
        {
            _log.Msg("[SourceSharp/Client] skybox: NOTHING IS CALLING THE PASS — ClientModule.OnStage "
                  + "is not reaching _skybox.Pass(), so no skybox can ever reach the renderer\n");
        }
        else if (!sky.SawViewer)
        {
            _log.Msg("[SourceSharp/Client] skybox: no local player — nothing to read a viewer's "
                  + "skybox from\n");
        }
        else if (sky.Pushes == 0)
        {
            _log.Msg("[SourceSharp/Client] skybox: a viewer is present and NOTHING has been pushed — "
                  + "the player's blob carries SkyValid false, so either the map has no sky_camera "
                  + "or the server's WorldSkybox is not writing it\n");
        }
    }
}
