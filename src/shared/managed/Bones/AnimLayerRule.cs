//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// The managed twin of src/shared/native/anim_layer_rule.h (plan
// physics_bones_native_zero step 1.0b): which layers play, and in what order.
//
// Two clauses, each silently wrong on any model playing one layer or none:
//
//   * ORDER IS NOT A HINT. AccumulatePose blends a layer onto what is already
//     in the pose, so A then B differs from B then A wherever they share a
//     bone. Stock's CBaseAnimatingOverlay sorts by the order field; so does
//     Sort, stably (an equal order never overtakes).
//   * AN INACTIVE LAYER IS NOT A ZERO-WEIGHT LAYER. A negative sequence is a
//     slot nobody filled; weight zero is a fade that reached its end. Both are
//     skipped, and so is a sequence outside the model's table.
//=============================================================================//

using System;

namespace SourceSharp.Shared.Bones;

/// <summary><c>AnimLayer_t</c>: one layer as the caller chose it (no clock).</summary>
public struct AnimLayer
{
    /// <summary><c>nSequence</c>: negative for a slot nobody has filled.</summary>
    public int Sequence;

    /// <summary><c>flWeight</c>: how much to blend in, 0..1.</summary>
    public float Weight;

    /// <summary><c>nOrder</c>: lower goes on first.</summary>
    public int Order;

    /// <summary><c>Reset()</c>: an unfilled slot.</summary>
    public void Reset()
    {
        Sequence = -1;
        Weight = 0.0f;
        Order = 0;
    }
}

/// <summary>The rules of anim_layer_rule.h.</summary>
public static class AnimLayerRule
{
    /// <summary><c>SOURCESHARP_MAX_ANIM_LAYERS</c>.</summary>
    public const int MaxLayers = 8;

    /// <summary><c>AnimLayer_IsActive</c>: filled, inside the model's table, and weighted.</summary>
    public static bool IsActive(in AnimLayer layer, int sequenceCount)
    {
        if (layer.Sequence < 0 || layer.Sequence >= sequenceCount)
        {
            return false;
        }

        return layer.Weight > 0.0f;
    }

    /// <summary><c>AnimLayer_Weight</c>: the weight clamped to 0..1.</summary>
    public static float Weight(in AnimLayer layer)
    {
        if (layer.Weight < 0.0f)
        {
            return 0.0f;
        }

        return layer.Weight > 1.0f ? 1.0f : layer.Weight;
    }

    /// <summary>
    /// <c>AnimLayer_Sort</c>: the active slots' indices into
    /// <paramref name="order"/>, ascending by order and stable. Answers how
    /// many. <paramref name="order"/> must hold <paramref name="layers"/>' length.
    /// </summary>
    public static int Sort(ReadOnlySpan<AnimLayer> layers, int sequenceCount, Span<int> order)
    {
        int active = 0;

        for (int i = 0; i < layers.Length; i++)
        {
            if (!IsActive(layers[i], sequenceCount))
            {
                continue;
            }

            int j = active++;

            // Strictly greater, so an equal order does not overtake: that is
            // what makes this stable.
            while (j > 0 && layers[order[j - 1]].Order > layers[i].Order)
            {
                order[j] = order[j - 1];
                j--;
            }

            order[j] = i;
        }

        return active;
    }
}
