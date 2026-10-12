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
/// Blends between two colours across a value range.
/// </summary>
[ScriptedEntity(EntityNames.MathColorblend)]
public sealed class MathColorBlend : ScriptedEntity
{
    public readonly Output OutColor = new();

    /// <summary>What a map may set on this entity. See <see cref="MathColorBlendVars"/>.</summary>
    [ScriptVars] public MathColorBlendVars Vars = new();

    [ScriptedEvent] private void OnEnable() => Vars.Disabled = false;
    [ScriptedEvent] private void OnDisable() => Vars.Disabled = true;

    [ScriptedEvent] private void OnInValue(float value)
    {
        if (Vars.Disabled || Vars.InMin == Vars.InMax)
            return;

        float t = Math.Clamp((value - Vars.InMin) / (Vars.InMax - Vars.InMin), 0.0f, 1.0f);
        OutColor.Fire(Blend(Vars.ColorMin, Vars.ColorMax, t));
    }

    /// <summary>
    /// Component-wise blend, written out as the three numbers an output carries.
    ///
    /// The endpoints were strings until the keys became <see cref="Color32"/>,
    /// which meant re-parsing both of them on every value this entity is sent.
    /// The output is still text because an output is.
    /// </summary>
    internal static string Blend(Color32 from, Color32 to, float t)
        => $"{Lerp(from.R, to.R, t)} {Lerp(from.G, to.G, t)} {Lerp(from.B, to.B, t)}";

    private static int Lerp(int x, int y, float f) => (int)(x + (y - x) * f);
}

/// <summary>
/// The map keys <see cref="MathColorBlend"/> declares.
/// </summary>
[ScriptVars]
public partial class MathColorBlendVars
{
    public float InMin { get; set; }

    public float InMax { get; set; } = 1.0f;

    public Color32 ColorMin { get; set; } = new(0, 0, 0);

    public Color32 ColorMax { get; set; } = new(255, 255, 255);

    [VarName("StartDisabled")]
    public bool Disabled { get; set; }
}
