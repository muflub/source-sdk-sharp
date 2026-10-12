//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// The client's effect list, <c>CEffectsList</c> (clientsideeffects.cpp): at most
/// 256 effects, drawn once a frame from the last to the first, each removed the
/// frame it stops being active by moving the last effect into its place (so the
/// order changes exactly as stock's does). Replayed against stock's recorded
/// list (ClientEffectListTests).
/// </summary>
public sealed class ClientEffectList : IReloadParticipant
{
    /// <summary><c>MAX_EFFECTS</c>.</summary>
    public const int MaxEffects = 256;

    private readonly IFxRender _render;
    private readonly IEngineConsole _console;
    private readonly ClientSideEffect?[] _effects = new ClientSideEffect?[MaxEffects];
    private int _count;

    public ClientEffectList(IFxRender render, IEngineConsole console)
    {
        _render = render;
        _console = console;
    }

    /// <summary>How many effects are on the list.</summary>
    public int Count => _count;

    /// <summary>The effect at a slot, for the replay and the effects route.</summary>
    public ClientSideEffect? this[int index] => index >= 0 && index < _count ? _effects[index] : null;

    /// <summary>Effects drawn since load, for the effects route.</summary>
    public long Draws { get; private set; }

    /// <summary>Effects refused because the list was full.</summary>
    public int Refused { get; private set; }

    /// <summary>
    /// Called before and after each effect's draw (the effect, then whether it is
    /// still active): the replay's view of which effect drew, and the effects
    /// route's. Null in play.
    /// </summary>
    internal Action<ClientSideEffect>? BeforeDraw { get; set; }

    internal Action<ClientSideEffect, bool>? AfterDraw { get; set; }

    /// <summary>
    /// <c>tracer_extra</c>, as a restored discreet line reads it (the factory
    /// gives a new one the same source).
    /// </summary>
    public Func<bool> TracerExtra { get; set; } = () => true;

    /// <summary>
    /// A managed reload (plan_client_novalve §3.8) carries every live effect:
    /// the same effects, in the same slots, mid-life, each still holding the
    /// material reference it took (the ids are native's and outlive the swap).
    /// </summary>
    public void Save(ReloadWriter writer)
    {
        writer.Write(_count);
        for (int i = 0; i < _count; i++)
        {
            ClientSideEffect effect = _effects[i]!;
            writer.Write(effect.Kind);
            effect.Save(writer);
        }
    }

    /// <inheritdoc/>
    public void Restore(ReloadReader reader)
    {
        int count = reader.ReadInt();
        for (int i = 0; i < count; i++)
        {
            string kind = reader.ReadString();
            ClientSideEffect effect = kind switch
            {
                "Line" => FxLine.Restore(reader),
                "Quad" => FxQuad.Restore(reader),
                "StaticLine" => FxStaticLine.Restore(reader),
                "DiscreetLine" => FxDiscreetLine.Restore(reader, () => TracerExtra()),
                _ => throw new ReloadFormatException($"an effect kind this build does not know: {kind}"),
            };
            if (_count < MaxEffects)
                _effects[_count++] = effect;
        }
    }

    /// <summary>
    /// <c>AddEffect</c>: onto the end of the list, or refused with stock's
    /// developer warning when it is full. A refused effect keeps its material
    /// reference, as stock's leaked effect did.
    /// </summary>
    public void Add(ClientSideEffect? effect)
    {
        if (effect is null)
            return;

        if (_count >= MaxEffects)
        {
            Refused++;
            if (int.TryParse(_console.FindConVarValue("developer"), out int level) && level >= 1)
                _console.Warning($"No room for effect {effect.Name}\n");
            return;
        }

        _effects[_count++] = effect;
    }

    /// <summary>
    /// <c>DrawEffects</c>: every effect, last to first, drawn and then removed if
    /// it has finished.
    /// </summary>
    public void DrawEffects(double frametime)
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            ClientSideEffect? effect = _effects[i];
            if (effect is null)
                continue;

            BeforeDraw?.Invoke(effect);
            effect.Draw(frametime, _render);
            Draws++;

            bool active = effect.IsActive;
            AfterDraw?.Invoke(effect, active);

            if (!active)
                RemoveAt(i);
        }
    }

    /// <summary><c>Flush</c>: removes every effect, last to first.</summary>
    public void Flush()
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            if (_effects[i] is null)
                continue;
            RemoveAt(i);
        }
    }

    /// <summary><c>RemoveEffect( int )</c>: the last effect fills the hole; the removed one is destroyed.</summary>
    private void RemoveAt(int index)
    {
        if (index >= _count || index < 0)
            return;

        ClientSideEffect effect = _effects[index]!;
        _count--;

        if (_count > 0 && index != _count)
            _effects[index] = _effects[_count];
        _effects[_count] = null;

        effect.Destroy(_render);
    }
}
