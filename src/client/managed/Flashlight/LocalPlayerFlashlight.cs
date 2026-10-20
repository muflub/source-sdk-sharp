//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client.Flashlight;

/// <summary>The local player as the flashlight driver reads it.</summary>
public readonly record struct FlashlightViewer(int Index, bool DimLight, Vector Eye, QAngle EyeAngles, bool OnLadder);

/// <summary>Where the driver finds the local player each frame.</summary>
public interface IFlashlightViewerSource
{
    /// <summary>The local player, or null when there is none.</summary>
    FlashlightViewer? Viewer();
}

/// <summary>
/// The local player's flashlight, once a client frame at FRAME_RENDER_START:
/// stock's C_BasePlayer::UpdateFlashlight (c_baseplayer.cpp), which
/// C_BasePlayer::Simulate runs for the local player. With EF_DIMLIGHT on, an
/// effect is made and turned on if there is none, then lit from the eye along
/// the eye's vectors (FLASHLIGHT_DISTANCE); with it off, the effect is deleted.
/// </summary>
public sealed class LocalPlayerFlashlight(IFlashlightViewerSource viewers, IFlashlightWorld world,
                                          IFlashlightSettings settings) : IReloadParticipant
{
    /// <summary>The section's first word: what says the next is a flashlight's shadow handle.</summary>
    public const int Magic = 0x464C5331; // "FLS1"

    // The shadow handle a previous build's effect held, which the managed
    // shadow manager carried across the reload too: the first effect adopts
    // it, or (the light went off meanwhile) it is destroyed.
    private int _carried = FlashlightEffect.InvalidHandle;

    /// <summary>FLASHLIGHT_DISTANCE (c_baseplayer.cpp).</summary>
    public const int FlashlightDistance = 1000;

    /// <summary>The local player's effect, or null while the flashlight is off.</summary>
    public FlashlightEffect? Effect { get; private set; }

    /// <summary>Effects made since load, for the dev API and the live check.</summary>
    public int Made { get; private set; }

    /// <summary>Frames the flashlight was lit.</summary>
    public long LitFrames { get; private set; }

    public void Pass()
    {
        if (viewers.Viewer() is { DimLight: true } viewer)
        {
            if (Effect is null)
            {
                Effect = new FlashlightEffect(viewer.Index, world, settings);
                Made++;
                Effect.TurnOn();
                if (_carried != FlashlightEffect.InvalidHandle)
                {
                    Effect.AdoptHandle(_carried);
                    _carried = FlashlightEffect.InvalidHandle;
                }
            }

            MoveVec.AngleVectors(viewer.EyeAngles.AsVector(), out Vector forward, out Vector right, out Vector up);
            Effect.UpdateLight(viewer.Eye, forward, right, up, FlashlightDistance, viewer.OnLadder);
            LitFrames++;
        }
        else if (Effect is { } effect)
        {
            effect.Release();
            Effect = null;
        }
        else if (_carried != FlashlightEffect.InvalidHandle)
        {
            world.Destroy(_carried);
            _carried = FlashlightEffect.InvalidHandle;
        }
    }

    /// <summary>The effect's shadow handle, for the reloaded build (docs/todo.md's "hot reload forgets the shadow handle").</summary>
    public void Save(ReloadWriter writer)
    {
        writer.Write(Magic);
        writer.Write(Effect?.Handle ?? _carried);
    }

    public void Restore(ReloadReader reader)
    {
        if (reader.ReadInt() != Magic)
            throw new ReloadFormatException("not a flashlight's shadow handle");
        _carried = reader.ReadInt();
    }

    /// <summary>
    /// The level is going: the shadow manager destroys every shadow at its own
    /// level shutdown, so the effect is forgotten rather than released.
    /// </summary>
    public void LevelShutdown()
    {
        Effect?.ForgetHandle();
        Effect = null;
        _carried = FlashlightEffect.InvalidHandle;
    }

    public string Report()
    {
        if (Effect is not { } e)
            return FormattableString.Invariant($"[SourceSharp/Flashlight] off (made {Made}, lit {LitFrames} frames)");
        string state = e.LastState is { } s
            ? FormattableString.Invariant(
                $" origin {s.OriginX:F1} {s.OriginY:F1} {s.OriginZ:F1} fov {s.HorizontalFovDegrees:F1} near {s.NearZ:F2} far {s.FarZ:F1} linear {s.LinearAtten:F1} shadows {s.EnableShadows}")
            : "";
        return FormattableString.Invariant(
            $"[SourceSharp/Flashlight] on: player {e.EntIndex} handle {e.Handle} texture {e.TextureName} distmod {e.DistMod:F3}{state} (made {Made}, lit {LitFrames} frames)");
    }
}

/// <summary>
/// The local player through the client's standalone entities: its EF_* word
/// out of the engine block, the eye and angles the prediction published
/// (<see cref="ViewReadout"/>, stock's EyePosition and pl.v_angle), and the
/// networked movetype for stock's ladder test.
/// </summary>
public sealed class StandaloneFlashlightViewers(IClientStandalones standalones, IEngineHud hud,
                                                IStandaloneBlocks blocks, ViewReadout view)
    : IFlashlightViewerSource
{
    private static readonly int EffectsOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Effects));

    /// <summary>MOVETYPE_LADDER (const.h).</summary>
    private const byte MoveTypeLadder = 9;

    public FlashlightViewer? Viewer()
    {
        int index = hud.LocalPlayerIndex;
        if (index <= 0 || standalones.GetByIndex(index) is not ClientSsPlayer player)
            return null;

        Span<byte> block = blocks.Block(player.Handle);
        int effects = block.Length < EngineBlock.SizeBytes ? 0 : MemoryMarshal.Read<int>(block.Slice(EffectsOffset));

        PlayerNetState state = player.State;
        Vector eye = view.Known ? view.Eye : state.Origin + state.ViewOffset;
        return new FlashlightViewer(index, (effects & (int)EntityEffect.DimLight) != 0, eye,
                                    new QAngle(view.Pitch, view.Yaw, 0f), state.MoveType == MoveTypeLadder);
    }
}

/// <summary><c>cs_cl_flashlight</c>: the local player's flashlight, as the driver last left it.</summary>
[ConCommand("cs_cl_flashlight", Help = "Print the local player's flashlight: on or off, its shadow handle and the last state handed to the shadow manager")]
public sealed class FlashlightReportCommand(ILog log, LocalPlayerFlashlight flashlight) : IConCommand
{
    public void Execute(string args) => log.Msg(flashlight.Report() + "\n");
}
