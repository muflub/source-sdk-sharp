//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Detail;

// detailobjectsystem.cpp's convars, declared here since the file left
// client.so (C7 cluster 10): names, defaults, flags and help as stock
// declared them (DetailConVarOracleTests holds each to the pristine file).
// The engine names cl_detaildist and cl_detailfade only in materialsystem's
// list of convars dxsupport.cfg may set, which it sets by name, as it does
// r_drawropes. Not here: mat_fullbright, the engine's (stock's declaration
// was a child of it), which native reads by reference for each draw; the
// four shape convars (USE_DETAIL_SHAPES is DOD and CS:S only); and
// r_DrawDetailProps, which shell_viewrender_client.cpp keeps with the render
// plan's other native inputs.

[ConVar("cl_detaildist", "1200", Help = "Distance at which detail props are no longer visible")]
public sealed class DetailDistConVar : FloatConVar { }

[ConVar("cl_detailfade", "400", Help = "Distance across which detail props fade in")]
public sealed class DetailFadeConVar : FloatConVar { }

[ConVar("cl_detail_multiplier", "1", Flags = ConVarFlags.Cheat, Help = "extra details to create")]
public sealed class DetailMultiplierConVar : IntConVar { }

[ConVar("cl_fastdetailsprites", "1", Flags = ConVarFlags.Cheat, Help = "whether to use new detail sprite system")]
public sealed class FastDetailSpritesConVar : IntConVar { }

/// <summary>The detail convars as <see cref="DetailPropSystem"/> reads and writes them.</summary>
public sealed class DetailConVars(DetailDistConVar dist, DetailFadeConVar fade, DetailMultiplierConVar multiplier,
                                  FastDetailSpritesConVar fast) : IDetailPropSettings
{
    public float DetailDist
    {
        get => dist.Value;
        set => dist.SetValue(value);
    }

    public float DetailFade
    {
        get => fade.Value;
        set => fade.SetValue(value);
    }

    public int Multiplier => multiplier.Value;

    public int FastSprites => fast.Value;
}
