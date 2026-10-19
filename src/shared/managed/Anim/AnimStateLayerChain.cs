//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;
using System.Linq;

using SourceSharp.Abi;

namespace SourceSharp.Shared.Anim;

/// <summary>
/// The <see cref="IAnimStateLayer"/> chain: ordered, tie-refused, and asked
/// first-answer-wins — the composition ruling D10 for animation.
///
/// <para>
/// Same invariant as <c>Hooks&lt;T&gt;</c> and the frame drivers, for the same
/// reason: <see cref="IAnimStateLayer.LayerOrder"/> decides which mod's rule
/// answers a gait question, and an order decided by dictionary mood is a pose
/// that flaps when somebody moves a registration line. <see
/// cref="StageUniqueness"/> refuses the tie at chain build, naming both
/// sides, rather than letting a stable sort keep a registration order nobody
/// chose.
/// </para>
///
/// <para>
/// The chain is built once per side from what the container knows — the
/// core's HL2 layer last (<see cref="Hl2PostureLayer.LayerOrderDefault"/>),
/// every mod's own layers before it — and then only iterated. It never
/// allocates during a drive: the consultation loops are over the kept
/// array (the zero-allocation fact in
/// <c>AnimStateUpdateAllocationTests</c> is what makes that a checked claim
/// rather than a hope).
/// </para>
/// </summary>
public sealed class AnimStateLayerChain
{
    private readonly IAnimStateLayer[] _layers;

    /// <summary>
    /// The layers, lowest <see cref="IAnimStateLayer.LayerOrder"/> first.
    /// </summary>
    public AnimStateLayerChain(IEnumerable<IAnimStateLayer> layers)
    {
        var found = layers.ToArray();

        // Ties are impossible, not resolved: two layers at the same order
        // is two answers to "which rule decides the gait" that nobody
        // arbitrated. Same argument, same helper, as the frame drivers.
        StageUniqueness.Require(found, l => l.LayerOrder,
                                nameof(IAnimStateLayer.LayerOrder),
                                nameof(IAnimStateLayer));

        _layers = found.OrderBy(l => l.LayerOrder).ToArray();
    }

    /// <summary>What was found, in the order they will be asked.</summary>
    public IReadOnlyList<IAnimStateLayer> Layers => _layers;

    /// <summary>
    /// Ask for the main sequence, in order; the first true wins and the
    /// rest are not asked. False from every layer means nobody overrode and
    /// the caller runs its own (stock's) answer.
    /// </summary>
    public bool TryMainSequence(AnimStateContext context, AnimMainSequence main)
    {
        for (int i = 0; i < _layers.Length; i++)
        {
            if (_layers[i].TryOverrideMainSequence(context, main))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Ask about one active gesture, in order; the first true wins and the
    /// rest are not asked. The layer mutates <paramref name="gesture"/> (or
    /// marks it <see cref="AnimGestureDecision.Kill"/>) and says true.
    /// </summary>
    public bool TryGesture(AnimStateContext context, AnimGesture gesture)
    {
        for (int i = 0; i < _layers.Length; i++)
        {
            if (_layers[i].TryOverrideGesture(context, gesture))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Offer an animation event, in order; the first true consumes it. When
    /// nobody answers, the caller runs stock's own switch — the no-owner arm
    /// of every seam in Stage 0.
    /// </summary>
    public bool TryAnimEvent(AnimStateContext context, PlayerAnimEvent animEvent, int data)
    {
        for (int i = 0; i < _layers.Length; i++)
        {
            if (_layers[i].TryHandleAnimEvent(context, animEvent, data))
                return true;
        }

        return false;
    }
}
