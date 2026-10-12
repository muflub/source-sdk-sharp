//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Render;

namespace SourceSharp.Client.MaterialProxies;

/// <summary>
/// What the post-processing proxies bind: the motion blur values and the
/// engine_post material's, as the post program left them this frame
/// (<see cref="PostProcessHost"/>).
/// </summary>
public interface IPostProxyValues
{
    MotionBlurValues MotionBlur { get; }

    EnginePostValues EnginePost { get; }
}

/// <summary>
/// viewpostprocess.cpp's CMotionBlurMaterialProxy ("MotionBlur"): the motion
/// blur pass's four values into $MotionBlurInternal. A material without that
/// var refuses the proxy.
/// </summary>
public sealed class MotionBlurProxy(IPostProxyValues values) : EntityMaterialProxy
{
    private int _param = -1;

    public override bool Init(IProxyInit init)
    {
        _param = init.FindVar("$MotionBlurInternal", false, out bool found);
        return found;
    }

    protected override void BindEntity(IProxyBind bind, ProxyEntity entity)
    {
        if (_param < 0)
            return;
        MotionBlurValues v = values.MotionBlur;
        bind.SetVec(_param, [v.X, v.Y, v.Falling, v.Roll], 4);
    }
}

/// <summary>
/// viewpostprocess.cpp's CEnginePostMaterialProxy ("engine_post"): the
/// software AA values, the bloom UV transform and the bloom switch the post
/// program set up for dev/engine_post.
/// </summary>
public sealed class EnginePostProxy(IPostProxyValues values) : EntityMaterialProxy
{
    private int _aaValues = -1, _aaValues2 = -1, _bloomUvTransform = -1, _bloomEnable = -1;

    public override bool Init(IProxyInit init)
    {
        _aaValues = init.FindVar("$AAInternal1", false, out _);
        _aaValues2 = init.FindVar("$AAInternal3", false, out _);
        _bloomUvTransform = init.FindVar("$AAInternal2", false, out _);
        _bloomEnable = init.FindVar("$bloomEnable", false, out _);
        // Stock also finds the colour-correction vars and never sets them.
        init.FindVar("$colCorrectEnable", false, out _);
        init.FindVar("$colCorrect_NumLookups", false, out _);
        init.FindVar("$colCorrect_DefaultWeight", false, out _);
        init.FindVar("$colCorrect_LookupWeights", false, out _);
        return true;
    }

    protected override void BindEntity(IProxyBind bind, ProxyEntity entity)
    {
        EnginePostValues v = values.EnginePost;
        if (_aaValues >= 0)
            bind.SetVec(_aaValues, [v.Aa0, v.Aa1, v.Aa2, v.Aa3], 4);
        if (_aaValues2 >= 0)
            bind.SetVec(_aaValues2, [v.Aa2_0, v.Aa2_1, v.Aa2_2, v.Aa2_3], 4);
        if (_bloomUvTransform >= 0)
            bind.SetVec(_bloomUvTransform, [v.Uv0, v.Uv1, v.Uv2, v.Uv3], 4);
        if (_bloomEnable >= 0)
            bind.SetInt(_bloomEnable, v.BloomEnable);
    }
}
