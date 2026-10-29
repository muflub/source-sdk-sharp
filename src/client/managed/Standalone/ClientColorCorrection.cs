//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// Every colour-correction lookup this client is holding.
///
/// <para>
/// The same shape as <c>DynamicLights</c> and for the same reason: a correction
/// has to be re-weighted every frame because the thing its weight depends on —
/// where the player is standing — moves on its own, and it does not have to be
/// VISIBLE to matter. A correction round a corner still tints the wall you can
/// see, so <c>OnDraw</c>, which only runs for what the leaf system thinks is in
/// view, is the wrong hook.
/// </para>
/// </summary>
public sealed class ColorCorrections : IColorCorrections
{
    public ColorCorrections(IEngineHud screen, IEngineVisibility visibility)
    {
        Screen = screen;
        Visibility = visibility;
    }

    /// <summary>drawing, and the local player it is drawn for.</summary>
    private readonly IEngineHud Screen;

    /// <summary>the lookups' weights, which the frame resets before it re-weighs them.</summary>
    private readonly IEngineVisibility Visibility;


    private readonly List<IWeightedCorrection> _live = new();

    /// <inheritdoc/>
    public void Add(IWeightedCorrection correction)
    {
        if (!_live.Contains(correction))
            _live.Add(correction);
    }

    /// <inheritdoc/>
    public void Remove(IWeightedCorrection correction) => _live.Remove(correction);

    /// <summary>How many are being weighted. For tests and debug-api.</summary>
    public int LiveCount => _live.Count;

    /// <summary>
    /// Resets every lookup's weight, then re-weights every live correction.
    /// Called once a frame by the client module, at FRAME_RENDER_START: stock's
    /// <c>OnRenderStart</c> calls <c>ResetColorCorrectionWeights</c> and then
    /// <c>SimulateEntities</c>, where each <c>C_ColorCorrection</c> thinks, so
    /// the weights the view draws with are set after the reset and before the
    /// render (lane M1-A: the reset was native, at OnRenderStart, and ran after
    /// this pass, which ran from the frame's update after the render).
    ///
    /// <para>
    /// Backwards, so a correction that releases itself mid-pass cannot make the
    /// walk skip its neighbour — the mistake <c>StandaloneEntities_RunFrame</c>
    /// made natively and which stayed latent until two went at once.
    /// </para>
    /// </summary>
    public void Frame()
    {
        Visibility.ColorCorrectionResetWeights();

        if (_live.Count == 0)
            return;

        Vector? player = Screen.LocalPlayerOrigin;

        if (player is null)
            return;

        for (int i = _live.Count - 1; i >= 0; i--)
            _live[i].Reweigh(player.Value);
    }

    /// <summary>
    /// Forgets everything without releasing it: the lookups belong to the
    /// level's colour-correction manager and go with the level.
    /// </summary>
    public void Reset() => _live.Clear();
}

/// <summary>
/// The client half of <c>color_correction</c>.
///
/// <para>
/// It owns half of one number. The server sends how far through its fade the
/// correction is; this multiplies that by how close the player is standing,
/// between <c>minfalloff</c> and <c>maxfalloff</c>. Two weights meaning
/// different things, and only this one depends on something that changes
/// without anybody deciding it.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.ColorCorrection)]
public sealed class ClientColorCorrection : ClientStandaloneEntity, IWeightedCorrection
{
    /// <summary>what can be seen, and how it is lit.</summary>
    private readonly IEngineVisibility Visibility;


    private readonly IColorCorrections _corrections;

    /// <inheritdoc cref="ClientDynamicLight(IDynamicLights)"/>
    public ClientColorCorrection(IColorCorrections corrections, IEngineEntityRender render,
                                 IEngineVisibility visibility)
        : base(render)
    {
        _corrections = corrections;
        Visibility = visibility;
    }

    public override string ClassName => EntityNames.ColorCorrection;

    [NetState] public ColorCorrectionNetState State = new();

    /// <summary>The manager's lookup, or 0 if it never loaded.</summary>
    private int _lookup;

    /// <summary>
    /// Loads the lookup on the first update that carries a filename.
    ///
    /// <para>
    /// Not on creation: this side has no state at all until an update arrives,
    /// and the filename is part of that state rather than something known from
    /// the classname.
    /// </para>
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (_lookup != 0 || State.LookupFile.Length == 0)
            return;

        _lookup = Visibility.ColorCorrectionAdd(State.LookupFile);

        if (_lookup == 0)
        {
            Log.Warning(
                $"[SourceSharp/Client] color_correction: lookup '{State.LookupFile}' would not load\n");
            return;
        }

        _corrections.Add(this);
    }

    /// <summary>
    /// Not optional. The manager holds the lookup until it is told otherwise,
    /// so a correction whose entity has gone would tint the map for the rest of
    /// the level.
    /// </summary>
    public override void OnRemoved()
    {
        _corrections.Remove(this);

        if (_lookup != 0)
        {
            Visibility.ColorCorrectionRemove(_lookup);
            _lookup = 0;
        }
    }

    /// <summary>
    /// This frame's weight: the server's fade, scaled by distance.
    ///
    /// <para>
    /// The falloff runs the way round that reads backwards and is right — the
    /// computed fraction is how far OUT of range the player is, so it is
    /// subtracted from one. Both ends unset (stock's -1, or equal) means no
    /// falloff at all and the correction applies at full strength everywhere,
    /// which is what a map wants from a global grade.
    /// </para>
    /// </summary>
    public void Reweigh(Vector playerOrigin)
    {
        if (_lookup == 0)
            return;

        if (!State.Enabled && State.CurrentWeight == 0f)
        {
            Visibility.ColorCorrectionSetWeight(_lookup, 0f);
            return;
        }

        float outOfRange = 0f;

        if (State.MinFalloff != -1f && State.MaxFalloff != -1f && State.MinFalloff != State.MaxFalloff)
        {
            // Through the shell rather than a networked copy: the standalone
            // hierarchy already sends origin as a shared prop, so a second one
            // in this class's own state could only ever disagree with it.
            Vector delta = playerOrigin - Entities.GetOrigin(Handle);
            float distance = MathF.Sqrt((delta.X * delta.X) + (delta.Y * delta.Y) + (delta.Z * delta.Z));

            outOfRange = Math.Clamp(
                (distance - State.MinFalloff) / (State.MaxFalloff - State.MinFalloff), 0f, 1f);
        }

        Visibility.ColorCorrectionSetWeight(_lookup, State.CurrentWeight * (1f - outOfRange));
    }
}
