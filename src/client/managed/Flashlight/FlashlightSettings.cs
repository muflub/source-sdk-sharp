//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Flashlight;

// flashlighteffect.cpp's ConVars, declared here since the file left client.so
// (C7 cluster 12): names, defaults, flags and help as stock declared them
// (FlashlightConVarOracleTests holds each to the pristine file). Not here:
// mat_slopescaledepthbias_shadowmap and mat_depthbias_shadowmap, which the
// material system and the shader APIs declare (stock's client copies were
// children of theirs), and clientshadowmgr.cpp's r_flashlightdepthtexture and
// r_flashlightdepthres; the four are read by name (FlashlightConVars).

/// <summary>
/// r_newflashlight, with stock's change callback: a card below DX7 is put
/// back on the old flashlight whatever is set (r_newflashlightCallback_f).
/// </summary>
[ConVar("r_newflashlight", "1", Flags = ConVarFlags.Cheat, Help = "")]
public sealed class NewFlashlightConVar : BoolConVar
{
    /// <summary>Raised on every change, as the callback stock registered was.</summary>
    public event Action? Changed;

    protected override void OnChanged(bool oldValue, bool newValue) => Changed?.Invoke();
}

[ConVar("r_swingflashlight", "1", Flags = ConVarFlags.Cheat)]
public sealed class SwingFlashlightConVar : BoolConVar { }

[ConVar("r_flashlightlockposition", "0", Flags = ConVarFlags.Cheat)]
public sealed class FlashlightLockPositionConVar : BoolConVar { }

[ConVar("r_flashlightfov", "45.0", Flags = ConVarFlags.Cheat)]
public sealed class FlashlightFovConVar : FloatConVar { }

/// <summary>Declared by stock and read by nothing (UpdateLightNew offsets only along up).</summary>
[ConVar("r_flashlightoffsetx", "10.0", Flags = ConVarFlags.Cheat)]
public sealed class FlashlightOffsetXConVar : FloatConVar { }

[ConVar("r_flashlightoffsety", "-20.0", Flags = ConVarFlags.Cheat)]
public sealed class FlashlightOffsetYConVar : FloatConVar { }

/// <summary>Declared by stock and read by nothing.</summary>
[ConVar("r_flashlightoffsetz", "24.0", Flags = ConVarFlags.Cheat)]
public sealed class FlashlightOffsetZConVar : FloatConVar { }

[ConVar("r_flashlightnear", "4.0", Flags = ConVarFlags.Cheat)]
public sealed class FlashlightNearConVar : FloatConVar { }

[ConVar("r_flashlightfar", "750.0", Flags = ConVarFlags.Cheat)]
public sealed class FlashlightFarConVar : FloatConVar { }

[ConVar("r_flashlightconstant", "0.0", Flags = ConVarFlags.Cheat)]
public sealed class FlashlightConstantConVar : FloatConVar { }

[ConVar("r_flashlightlinear", "100.0", Flags = ConVarFlags.Cheat)]
public sealed class FlashlightLinearConVar : FloatConVar { }

[ConVar("r_flashlightquadratic", "0.0", Flags = ConVarFlags.Cheat)]
public sealed class FlashlightQuadraticConVar : FloatConVar { }

[ConVar("r_flashlightvisualizetrace", "0", Flags = ConVarFlags.Cheat)]
public sealed class FlashlightVisualizeTraceConVar : BoolConVar { }

[ConVar("r_flashlightambient", "0.0", Flags = ConVarFlags.Cheat)]
public sealed class FlashlightAmbientConVar : FloatConVar { }

[ConVar("r_flashlightshadowatten", "0.35", Flags = ConVarFlags.Cheat)]
public sealed class FlashlightShadowAttenConVar : FloatConVar { }

[ConVar("r_flashlightladderdist", "40.0", Flags = ConVarFlags.Cheat)]
public sealed class FlashlightLadderDistConVar : FloatConVar { }

/// <summary>
/// The flashlight's settings: its own ConVars, and the four it reads by name
/// through the convar watch (the material system's two depth biases,
/// clientshadowmgr.cpp's depth texture and resolution). Holds stock's
/// r_newflashlight callback.
/// </summary>
public sealed class FlashlightConVars : IFlashlightSettings
{
    /// <summary>The ConVars the flashlight reads but does not declare.</summary>
    public static string[] EngineNames() =>
    [
        "mat_slopescaledepthbias_shadowmap", "mat_depthbias_shadowmap", "r_flashlightdepthtexture",
        "r_flashlightdepthres",
    ];

    private readonly NewFlashlightConVar _newFlashlight;
    private readonly SwingFlashlightConVar _swing;
    private readonly FlashlightLockPositionConVar _lock;
    private readonly FlashlightFovConVar _fov;
    private readonly FlashlightOffsetYConVar _offsetY;
    private readonly FlashlightNearConVar _near;
    private readonly FlashlightFarConVar _far;
    private readonly FlashlightConstantConVar _constant;
    private readonly FlashlightLinearConVar _linear;
    private readonly FlashlightQuadraticConVar _quadratic;
    private readonly FlashlightVisualizeTraceConVar _visualize;
    private readonly FlashlightAmbientConVar _ambient;
    private readonly FlashlightShadowAttenConVar _shadowAtten;
    private readonly FlashlightLadderDistConVar _ladderDist;
    private readonly WatchedConVarValues _engine;
    private readonly Func<int?> _dxLevel;

    public FlashlightConVars(ConVarRegistry convars, WatchedConVarValues engine, Func<int?> dxLevel)
    {
        _newFlashlight = Need<NewFlashlightConVar>(convars);
        _swing = Need<SwingFlashlightConVar>(convars);
        _lock = Need<FlashlightLockPositionConVar>(convars);
        _fov = Need<FlashlightFovConVar>(convars);
        _offsetY = Need<FlashlightOffsetYConVar>(convars);
        _near = Need<FlashlightNearConVar>(convars);
        _far = Need<FlashlightFarConVar>(convars);
        _constant = Need<FlashlightConstantConVar>(convars);
        _linear = Need<FlashlightLinearConVar>(convars);
        _quadratic = Need<FlashlightQuadraticConVar>(convars);
        _visualize = Need<FlashlightVisualizeTraceConVar>(convars);
        _ambient = Need<FlashlightAmbientConVar>(convars);
        _shadowAtten = Need<FlashlightShadowAttenConVar>(convars);
        _ladderDist = Need<FlashlightLadderDistConVar>(convars);
        _engine = engine;
        _dxLevel = dxLevel;

        _newFlashlight.Changed += OnNewFlashlightChanged;
    }

    private static T Need<T>(ConVarRegistry convars) where T : ConVarBase
        => convars.Get<T>() ?? throw new InvalidOperationException(typeof(T).Name + " is not registered");

    /// <summary>Watches the four the flashlight reads by name; the module routes their changes here.</summary>
    public void Start() => _engine.Watch(EngineNames());

    public bool OnChanged(int id, string newValue) => _engine.OnChanged(id, newValue);

    /// <summary>r_newflashlightCallback_f.</summary>
    private void OnNewFlashlightChanged()
    {
        if (_dxLevel() is int dx && dx < 70)
            DisableNewFlashlight();
    }

    public bool NewFlashlight => _newFlashlight.Value;

    public void DisableNewFlashlight() => _newFlashlight.SetValue(false);

    public bool SwingFlashlight => _swing.Value;
    public bool LockPosition => _lock.Value;
    public float Fov => _fov.Value;
    public float OffsetY => _offsetY.Value;
    public float Near => _near.Value;
    public float Far => _far.Value;
    public float ConstantAtten => _constant.Value;
    public float LinearAtten => _linear.Value;
    public float QuadraticAtten => _quadratic.Value;
    public bool VisualizeTrace => _visualize.Value;
    public float Ambient => _ambient.Value;
    public float ShadowAtten => _shadowAtten.Value;
    public float LadderDist => _ladderDist.Value;
    public float SlopeScaleDepthBias => _engine.Float("mat_slopescaledepthbias_shadowmap");
    public float DepthBias => _engine.Float("mat_depthbias_shadowmap");
    public bool DepthTexture => _engine.Bool("r_flashlightdepthtexture");
    public int DepthResolution => _engine.Int("r_flashlightdepthres");
}
