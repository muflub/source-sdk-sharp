//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Interop;
using SourceSharp.Client.Render.Shadows;

namespace SourceSharp.Client.MaterialProxies;

// clientshadowmgr.cpp's two material proxies ("Shadow", "ShadowModel"), the
// render-to-texture shadow decals' (decals/rendershadow.vmt and
// decals/rendermodelshadow.vmt). Q1 of docs/plans/c7_shadow_manager.md
// (2026-10-19, "move to c#"): their OnBind is managed and goes through the
// proxy host like the other proxies; native keeps only their registration by
// name. Not registered yet (S1): the stock proxies still bind.

/// <summary>
/// The material system's bind argument for a shadow decal: the client
/// shadow handle, <c>( unsigned short )(intp)pProxyData &amp; 0xffff</c>.
/// </summary>
internal static class ShadowProxyArg
{
    public static int Handle(nint arg) => (int)(unchecked((ulong)arg) & 0xffff);

    /// <summary>The atlas render target's name (its slot comes from FindTexture, as any proxy's texture does).</summary>
    public const string AtlasName = "_rt_Shadows";

    /// <summary>TEXTURE_GROUP_RENDER_TARGET.</summary>
    public const string RenderTargetGroup = "RenderTargets";

    /// <summary>
    /// The atlas's texture slot (-1 for NULL while there is no atlas): asked of
    /// the material system once per atlas the manager makes, so a proxy holds
    /// one reference to each.
    /// </summary>
    public static int Atlas(IProxyBind bind, IShadowProxySource shadows, ref int page, ref int slot)
    {
        int current = shadows.AtlasPage;
        if (current == 0)
            return -1;
        if (current != page)
        {
            page = current;
            slot = bind.FindTexture(AtlasName, RenderTargetGroup, false);
        }
        return slot;
    }
}

/// <summary>
/// CShadowProxy ("Shadow"): the atlas into <c>$basetexture</c>, NULL while
/// render-to-texture is off.
/// </summary>
public sealed class ShadowProxy(IShadowProxySource shadows) : MaterialProxy
{
    private int _baseTexture = -1;
    private int _atlasPage;
    private int _atlas = -1;

    public override bool Init(IProxyInit init)
    {
        _baseTexture = init.FindVar("$basetexture", false, out bool found);
        return found;
    }

    public override void Bind(IProxyBind bind)
    {
        bind.SetTexture(_baseTexture, Atlas(bind));
    }

    private int Atlas(IProxyBind bind) => ShadowProxyArg.Atlas(bind, shadows, ref _atlasPage, ref _atlas);
}

/// <summary>
/// CShadowModelProxy ("ShadowModel"): the atlas, and the engine's ShadowInfo_t
/// for the shadow being drawn (its world-to-texture matrix, its fragment's
/// origin and size, its falloff) into the model shadow's seven vars. Init
/// refuses at the first var the material lacks.
/// </summary>
public sealed class ShadowModelProxy(IShadowProxySource shadows) : MaterialProxy
{
    private readonly string[] _varNames =
    [
        "$basetexture", "$basetextureoffset", "$basetexturescale", "$basetexturetransform", "$falloffoffset", "$falloffdistance",
        "$falloffamount",
    ];

    private readonly int[] _vars = [-1, -1, -1, -1, -1, -1, -1];
    private int _atlasPage;
    private int _atlas = -1;

    public override bool Init(IProxyInit init)
    {
        for (int i = 0; i < _varNames.Length; i++)
        {
            _vars[i] = init.FindVar(_varNames[i], false, out bool found);
            if (!found)
                return false;
        }
        return true;
    }

    public override void Bind(IProxyBind bind)
    {
        int handle = ShadowProxyArg.Handle(bind.Arg);
        bind.SetTexture(_vars[0], Atlas(bind));

        // A handle that names no shadow: stock reads past its list; nothing is set.
        if (!shadows.TryGetShadowInfo(handle, out ShadowEngineInfo info))
            return;

        var matrix = new VMatrix();
        for (int i = 0; i < 16; i++)
            matrix[i] = info.WorldToShadow[i];
        bind.SetMatrix(_vars[3], matrix);
        bind.SetVec(_vars[1], [info.TexOriginX, info.TexOriginY], 2);
        bind.SetVec(_vars[2], [info.TexSizeX, info.TexSizeY], 2);
        bind.SetFloat(_vars[4], info.FalloffOffset);
        bind.SetFloat(_vars[5], info.MaxDist);
        bind.SetFloat(_vars[6], info.FalloffAmount);
    }

    private int Atlas(IProxyBind bind) => ShadowProxyArg.Atlas(bind, shadows, ref _atlasPage, ref _atlas);
}
