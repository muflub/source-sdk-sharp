//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Render;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Client.Weapons;

/// <summary>
/// The viewmodels' min mode, stock's <c>CalcViewModelView</c> tail
/// (tf_viewmodel.cpp:195-221): <c>tf_use_min_viewmodels</c>, or a nonempty
/// <c>tf_viewmodels_offset_override</c>, holds the weapon's offset x y z along
/// the eye's forward, right and up — the override when it stands, else the
/// active weapon's <c>min_viewmodel_offset</c> attribute
/// (<c>CTFWeaponBase::GetViewmodelOffset</c>, tf_weaponbase.cpp:6937).
///
/// <para>
/// Stock scales the term by <c>Gain(1 - s_inspectInterp, 0.5)</c>, the inspect
/// crossfade's tail; this port has no inspect state, so the interp is always
/// 0 and the factor 1 — the offset stands whole, as it does once stock's
/// inspect has ended.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(IViewModelOffset)])]
public sealed class TFViewmodelOffset(TFUseMinViewmodelsConVar min, TFViewmodelsOffsetOverrideConVar overrideConVar,
                                      IEngineHud screen, ITFWeaponOwners owners)
    : IViewModelOffset
{
    /// <summary>The item attribute the minimized offset reads (tf_weaponbase.cpp:6941).</summary>
    public const string MinViewmodelOffset = "min_viewmodel_offset";

    private readonly AttributeHooks _hooks = new();

    public bool Offset(Span<float> offset)
    {
        string? forced = overrideConVar.Value;
        bool minMode = min.Value || !string.IsNullOrEmpty(forced);
        if (!minMode)
            return false;

        Parse(!string.IsNullOrEmpty(forced)
                  ? forced
                  : _hooks.String(null, MinViewmodelOffset, owners.ActiveItemOf(screen.LocalPlayerIndex)),
              offset);
        return true;
    }

    /// <summary>Stock's <c>UTIL_StringToVector</c> over three floats: what parses stands, the rest zero.</summary>
    private static void Parse(string? text, Span<float> offset)
    {
        offset[0] = offset[1] = offset[2] = 0f;
        if (string.IsNullOrWhiteSpace(text))
            return;

        int end = 0;
        foreach (string part in text!.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (float.TryParse(part, out float value))
                offset[end] = value;
            if (++end == 3)
                return;
        }
    }
}
