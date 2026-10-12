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
/// What fog each viewer is standing in, copied into each player's networked
/// state once a frame — plan_entity.md step 8e-render.
///
/// <para>
/// <b>THE DEFECT THIS CLOSES.</b> World fog did not render on this client, and
/// had not since the flip. Step 7c measured it and made it a printed
/// not-shipped row rather than a working feature, which was right: nothing in
/// <see cref="PlayerNetState"/> carried fog, so
/// <c>SourceSharp_ViewerFogParams</c> returned NULL and
/// <c>CRendering3dView::EnableWorldFog</c> read fog off on every frame.
/// </para>
///
/// <para>
/// <b>AND IT WAS THE WIRE, NOT A REACHABILITY BUG.</b> Worth stating because
/// the 3D skybox — filed beside it as the same gap — turned out to be one:
/// <c>UpdateAllClientData</c> walks <c>UTIL_PlayerByIndex</c>, which cannot
/// answer on this build, so <c>ClientData_Update</c> never ran and
/// <c>SourceSharp_FillSkyboxParams</c> was never reached (lane phase4-exit).
/// The fog has no such path to be unreachable on. In stock it arrives two ways
/// at once — <c>CFogController</c> networks its own <c>m_fog</c> through
/// <c>DT_FogController</c>, and <c>CPlayerLocalData</c> carries a handle to the
/// controller the server assigned this player. <c>fogcontroller.cpp</c> is
/// entirely <c>#ifndef SOURCESHARP</c> here and <c>env_fog_controller</c> is
/// <see cref="EnvFogController"/>, so <c>server.so</c> declares no
/// <c>DT_FogController</c> and there was NO SENDER for either half.
/// </para>
///
/// <para>
/// <b>THE FIRST FOUND, AND A MASTER OVERRIDES IT.</b> Stock's own comment on
/// <c>CFogSystem::LevelInitPostEntity</c> is the rule: <i>"On level load find the
/// master fog controller. If no controller is set as Master, use the first fog
/// controller found."</i>
/// </para>
///
/// <para>
/// THIS LANE GOT IT WRONG FIRST TIME AND THE LIVE GATE IS WHAT CAUGHT IT.
/// Written as "the master and only the master", it read the loop's
/// <c>IsMaster()</c> arm and missed the <c>m_pMasterController == NULL</c> arm
/// above it. Every managed test passed — they were written from the same wrong
/// reading — and then <c>cs_cl_fog</c> reported "a viewer is present and NOTHING
/// has been pushed" on <c>ss_sandbox</c>. Both maps in this build settle it:
/// neither <c>ss_sandbox</c>'s <c>env_fog_controller</c> nor
/// <c>dm_lockdown</c>'s carries a <c>spawnflags</c> key at all, so the strict
/// rule delivered fog to nobody on either — which is a build in which the wire
/// works perfectly and no fog ever renders, i.e. the defect this step closed,
/// reintroduced one layer up.
/// </para>
///
/// <para>
/// ONE DIVERGENCE, NAMED: "first" is stock's ENTITY-INDEX order
/// (<c>FindEntityByClassname</c> walks the engine's list) and here it is the
/// directory's. A map with two unflagged controllers therefore gets one of
/// them rather than a defined one. Flagging a master is the only way either
/// engine makes that choice deterministic, and it is what a map with two
/// should do.
/// </para>
///
/// <para>
/// <b>RECONCILED, NOT NOTIFIED, AND THE CHOICE IS THE POINT.</b>
/// <see cref="EnvFogController"/> changes its numbers from ten places — spawn,
/// seven immediate inputs and the two halves of a transition — and a player can
/// spawn on any frame at all. A design where each of those eleven edges pushes
/// is eleven chances to forget one, in a subsystem whose failure mode is fog
/// that is silently one input stale; and the edge this port has already lost
/// FIVE times is exactly the one nobody remembered to fire. So this compares
/// instead: once a frame it reads the master controller, and writes into any
/// player whose blob does not already say that. Nothing has to be told, a
/// player that arrives mid-level is covered by the same line as one that was
/// there all along, and a write that is missed for any reason is retried on the
/// next frame rather than lost.
/// </para>
///
/// <para>
/// <b>THE COST, stated as a number.</b> One dictionary lookup and one
/// record-struct comparison per player per frame, and NO writes on a still map
/// — <see cref="Polls"/> counts the frames and <see cref="Writes"/> the blob
/// writes, so "it is not costing anything" is measurable rather than claimed.
/// The later performance pass wants this listed; it is a per-frame loop this
/// step added.
/// </para>
/// </summary>
public sealed class WorldFog : IFrameLifecycle, IGameLifecycle
{
    public WorldFog(IEntityDirectory entities)
    {
        _entities = entities;
    }

    private readonly IEntityDirectory _entities;

    /// <inheritdoc/>
    public int FrameOrder => (int)FrameStage.WorldFog;

    /// <inheritdoc/>
    public int LifecycleOrder => (int)LifecycleStage.WorldFog;

    /// <summary>What the master controller last said. <c>Valid</c> false when there is none.</summary>
    public FogState Current { get; private set; }

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
    /// Reads the master controller and reconciles every live player against it.
    /// Public so a test can drive it without a clock.
    /// </summary>
    public void Update()
    {
        Polls++;
        Current = Read();

        int seen = 0;

        foreach (var entity in _entities.OfClass(EntityNames.SsPlayer))
        {
            if (entity is not SsPlayer player)
                continue;

            seen++;

            // THE COMPARISON IS AGAINST THE BLOB, not against a remembered
            // publish. A player that spawned this frame has a zeroed blob and is
            // written; a player whose blob already says this is skipped; and a
            // write lost to anything at all is retried next frame. A remembered
            // publish would have to be invalidated by hand on every one of those.
            if (FogState.From(player.State) == Current)
                continue;

            Current.WriteTo(player.State);
            Writes++;
        }

        LastSeen = seen;
    }

    /// <summary>
    /// The master controller's numbers, or <see cref="FogState.None"/>.
    ///
    /// <para>
    /// <c>OfClass</c> and not the engine's list: <c>env_fog_controller</c> is a
    /// managed class, so <c>gEntList.FindEntityByClassname</c> — which is what
    /// stock's <c>CFogSystem</c> walks — cannot see one. Every managed entity
    /// appears in the engine's list as one of a handful of shell classes.
    /// </para>
    /// </summary>
    public FogState Read()
    {
        EnvFogController? chosen = null;

        foreach (var entity in _entities.OfClass(EntityNames.EnvFogController))
        {
            if (entity is not EnvFogController fog)
                continue;

            // STOCK'S TWO ARMS, IN STOCK'S ORDER: the first one found is taken,
            // and any later one that declares itself master replaces it. The
            // arm that is easy to miss is the FIRST one -- see the class comment
            // for what missing it cost.
            if (chosen is null || fog.IsMaster)
                chosen = fog;
        }

        if (chosen is null)
            return FogState.None;

        // BUILT HERE RATHER THAN BY A FACTORY ON FogState, because FogState
        // lives in SourceSharp.Shared -- the client reads the same struct out of
        // the same PlayerNetState -- and EnvFogController is the server's. One
        // declaration of the values, two sides, and no reference from Shared to
        // Server.
        return new FogState(
            Valid: true,
            Enable: chosen.Enabled,
            Blend: chosen.Vars.FogBlend,
            Radial: chosen.Radial,
            DirPrimary: chosen.DirPrimary,
            ColorPrimary: chosen.ColourPrimary,
            ColorSecondary: chosen.ColourSecondary,
            Start: chosen.Start,
            End: chosen.End,
            FarZ: chosen.FarZ,
            MaxDensity: chosen.MaxDensity);
    }

    //-------------------------------------------------------------------------
    // The level.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Forgotten between levels, so the counters describe one map.
    ///
    /// <para>
    /// <see cref="Current"/> is re-read from scratch every poll rather than
    /// carried, so nothing survives a level change that could make the next
    /// map's first poll skip a write — which is the shape of bug a remembered
    /// publish would have.
    /// </para>
    /// </summary>
    void IGameLifecycle.OnLevelShutdown()
    {
        Current = FogState.None;
        Polls = 0;
        Writes = 0;
        LastSeen = 0;
    }
}

/// <summary>
/// What the server's world fog is doing, as counts.
///
/// <para>
/// The server half of the gate. <c>cs_cl_fog</c> says whether the fog reached
/// the renderer; this says whether there was any to reach it, and the two
/// together tell a reader WHICH link is broken. A map with no master
/// <c>env_fog_controller</c> and a wire that has stopped working produce the
/// same screenshot and the same <c>cs_cl_fog</c> reading; only this tells them
/// apart.
/// </para>
/// </summary>
[ConCommand("cs_sv_fog",
            Help = "What the server's world fog service is doing: polls, blob writes, "
                 + "players seen, and what the master env_fog_controller says.")]
public sealed class WorldFogReportCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    public WorldFogReportCommand(ILog log, WorldFog fog)
    {
        _log = log;
        _fog = fog;
    }

    private readonly WorldFog _fog;

    public void Execute(string args)
    {
        FogState now = _fog.Read();

        _log.Msg($"[SourceSharp] fog: {_fog.Polls} poll(s), {_fog.Writes} blob write(s), "
              + $"{_fog.LastSeen} player(s) on the last poll\n");

        _log.Msg($"[SourceSharp] fog: master controller {(now.Valid ? "present" : "NONE")}\n");

        if (now.Valid)
        {
            _log.Msg($"[SourceSharp] fog: enable {(now.Enable ? 1 : 0)} blend {(now.Blend ? 1 : 0)} "
                  + $"radial {(now.Radial ? 1 : 0)} start {now.Start:F1} end {now.End:F1} "
                  + $"farz {now.FarZ:F1} maxdensity {now.MaxDensity:F3}\n");
            _log.Msg($"[SourceSharp] fog: primary 0x{now.ColorPrimary:x8} secondary 0x{now.ColorSecondary:x8}\n");
        }

        // WHICH FAILURE, said rather than left to be inferred from two zeroes.
        if (_fog.Polls == 0)
        {
            _log.Msg("[SourceSharp] fog: NOTHING IS POLLING — WorldFog is not registered with the "
                  + "frame driver, so no player will ever be given a fog\n");
        }
        else if (!now.Valid)
        {
            _log.Msg("[SourceSharp] fog: this map declares no MASTER env_fog_controller. Stock "
                  + "behaves the same way — CFogSystem keeps only the one whose IsMaster() is "
                  + "true, and a map that flags none gets no fog\n");
        }
        else if (_fog.LastSeen == 0)
        {
            _log.Msg("[SourceSharp] fog: there is fog and no players to give it to\n");
        }
    }
}
