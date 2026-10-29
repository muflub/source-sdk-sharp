using SourceSharp.Abi;

using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The last link of the world-fog wire: the LOCAL player's networked fog, into
/// the pointer <c>viewrender.cpp</c> asks for — plan_entity.md step 8e-render.
///
/// <para>
/// <b>WHY THIS CLASS IS THE PART MOST LIKELY TO BE LOST.</b> Everything else on
/// this wire fails loudly. A prop that is not declared does not compile; a field
/// the server never writes shows up as a zero in a report. This runs once a
/// frame, reads state that arrives on its own, and calls into native — so if it
/// is never called, the game runs perfectly and the fog is off, which is exactly
/// what the tree looked like for the whole time the defect was open. That is why
/// <see cref="Pushes"/> and <see cref="Clears"/> exist and why the gate reads
/// them rather than looking at the screen.
/// </para>
///
/// <para>
/// <b>THE LOCAL PLAYER, NOT ANY PLAYER.</b> Every player's blob carries fog
/// today, because the tier tables have no local-exclusive branch yet
/// (<c>docs/divergences.md</c>). The fog a client RENDERS is the viewer's, so
/// this resolves the local index and takes that player's copy; taking whichever
/// player was found first would be right by accident today and wrong the moment
/// per-player fog exists.
/// </para>
///
/// <para>
/// <b>EDGE-TRIGGERED.</b> The push is a struct copy across the ABI and the
/// values change when a mapper's entity says so, which is close to never. The
/// comparison is against what was last PUSHED rather than against the blob,
/// because the blob is the server's word and the native copy is what a
/// re-push would change.
/// </para>
/// </summary>
public sealed class ClientWorldFog
{
    public ClientWorldFog(IClientStandalones standalones, IEngineHud screen,
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
    private FogState _pushed;
    private bool _havePushed;

    /// <summary>How many times a fog has been handed to the renderer.</summary>
    public int Pushes { get; private set; }

    /// <summary>How many times the renderer has been told there is no fog.</summary>
    public int Clears { get; private set; }

    /// <summary>How many frames have looked. Zero means this is not being called.</summary>
    public int Passes { get; private set; }

    /// <summary>Whether the last pass found a local player to read fog from.</summary>
    public bool SawViewer { get; private set; }

    /// <summary>
    /// One frame. Called from <c>ClientModule.OnStage</c> at
    /// <c>NetUpdatePostDataUpdateEnd</c> — the stage the decode has just
    /// finished on, and the same stage the render policy pass runs at, so the
    /// fog the renderer draws with this frame is the fog that arrived this
    /// frame rather than last frame's.
    /// </summary>
    public void Pass()
    {
        Passes++;

        ClientSsPlayer? viewer = FindLocalPlayer();
        SawViewer = viewer is not null;

        // NO VIEWER IS A CLEAR, NOT A SKIP. Between a level change and the
        // first player update there is no viewer, and the fog held from the
        // previous map would otherwise go on being rendered over the new one.
        FogState wanted = viewer is null ? FogState.None : FogState.From(viewer.State);

        if (_havePushed && wanted == _pushed)
            return;

        _pushed = wanted;
        _havePushed = true;

        if (!wanted.Valid)
        {
            // CLEARED, WHICH IS NOT "FOG OFF" — see Engine.ClearViewerFogParams.
            _visibility.ClearViewerFogParams();
            Clears++;
            return;
        }

        var native = new FogParams
        {
            Enable = wanted.Enable ? 1 : 0,
            Blend = wanted.Blend ? 1 : 0,
            Radial = wanted.Radial ? 1 : 0,
            DirPrimaryX = wanted.DirPrimary.X,
            DirPrimaryY = wanted.DirPrimary.Y,
            DirPrimaryZ = wanted.DirPrimary.Z,
            ColorPrimary = wanted.ColorPrimary,
            ColorSecondary = wanted.ColorSecondary,
            Start = wanted.Start,
            End = wanted.End,
            FarZ = wanted.FarZ,
            MaxDensity = wanted.MaxDensity,
        };

        _visibility.SetViewerFogParams(in native);
        Pushes++;
    }

    /// <summary>
    /// Forgotten at level init, so the first pass of a new map pushes even when
    /// the new map's fog happens to equal the old map's.
    /// </summary>
    public void LevelInit()
    {
        _pushed = FogState.None;
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

        // Through the directory's classname index — see
        // ClientPlayerPrediction.FindLocalPlayer, which is the same lookup and
        // carries the reasoning. plan_entity.md §3.5.
        foreach (var entity in _standalones.OfClass(EntityNames.Player))
        {
            if (entity is ClientSsPlayer player &&
                (int)(player.Handle.Value & HandleEntryMask) == local)
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>
    /// The entry index inside a handle. <c>ClientPlayerPrediction</c>'s constant
    /// and the same one, because the two ask the same question of the same
    /// handle.
    /// </summary>
    private const uint HandleEntryMask = (1u << 11) - 1u;
}

/// <summary>
/// Whether the world fog is reaching this client, as counts.
///
/// <para>
/// THE COMMAND EXISTS BECAUSE THE FEATURE IS INVISIBLE WHEN IT FAILS. A fog
/// that never arrives renders as no fog, and a map with no fog controller
/// renders as no fog: the two are the same screenshot. What tells them apart is
/// whether anything was ever pushed, and that is a number.
/// </para>
/// </summary>
[ConCommand("cs_cl_fog",
            Help = "Whether the viewer's world fog is reaching this client: passes, "
                 + "pushes, clears, and whether a viewer was found.")]
public sealed class ClientWorldFogReportCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly ClientWorldFog? _fog;

    /// <summary>
    /// The module's world fog, from its container; null when the container has
    /// none, which is reported rather than dereferenced.
    /// </summary>
    public ClientWorldFogReportCommand(ILog log, ClientWorldFog? fog = null)
    {
        _log = log;
        _fog = fog;
    }

    public void Execute(string args)
    {
        ClientWorldFog? fog = _fog;

        if (fog is null)
        {
            _log.Msg("[SourceSharp/Client] fog: the client module has not built its services yet\n");
            return;
        }

        _log.Msg($"[SourceSharp/Client] fog: {fog.Passes} pass(es), {fog.Pushes} push(es), "
              + $"{fog.Clears} clear(s), viewer {(fog.SawViewer ? "found" : "NOT found")}\n");

        // SAID OUT LOUD, because two counts do not tell a reader which failure
        // they are looking at and there are three distinct ones.
        if (fog.Passes == 0)
        {
            _log.Msg("[SourceSharp/Client] fog: NOTHING IS CALLING THE PASS — ClientModule.OnStage "
                  + "is not reaching _worldFog.Pass(), so no fog can ever reach the renderer\n");
        }
        else if (!fog.SawViewer)
        {
            _log.Msg("[SourceSharp/Client] fog: no local player — nothing to read a viewer's fog from\n");
        }
        else if (fog.Pushes == 0)
        {
            _log.Msg("[SourceSharp/Client] fog: a viewer is present and NOTHING has been pushed — "
                  + "the player's blob carries FogValid false, so either the map has no master "
                  + "env_fog_controller or the server's WorldFog is not writing it\n");
        }
    }
}
