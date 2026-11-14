//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared.Bones;

namespace SourceSharp.Server;

/// <summary>
/// Stock's bodygroup pair over an entity's own model: <see cref="FindBodygroupByName"/>
/// answers a group's index from the model's hdr (animation.cpp:927), and
/// <see cref="SetBodygroup"/> replaces one group's digit inside the entity's
/// body (animation.cpp:863, baseanimating.cpp:2294) and sends the result as
/// <c>m_nBody</c> through <see cref="IServerSkins.SetBody"/>. A group outside
/// the hdr's groups, or a value past the group's model count, leaves the body
/// as it was, stock's early-return arms. The body itself is the entity's own
/// state (stock's <c>m_nBody</c> member): the caller holds it and passes it in,
/// so successive groups compose and a saved entity keeps its body.
/// </summary>
public sealed class ServerBodygroups(IServerSkins skins, IBoneModels models, IEnginePrecache precache,
                                     IEngineEntityRender render)
{
    private readonly IServerSkins _skins = skins;
    private readonly IBoneModels _models = models;
    private readonly IEnginePrecache _precache = precache;
    private readonly IEngineEntityRender _render = render;

    /// <summary><c>FindBodygroupByName</c> (animation.cpp:927): the group of this
    /// entity's model named <paramref name="name"/>, or the stock -1, which a
    /// model with no hdr answers too (animation.cpp:929).</summary>
    public int FindBodygroupByName(EntityHandle entity, string name)
    {
        StudioHdr? hdr = OpenHdr(entity);
        return hdr is null ? -1 : hdr.FindBodygroupByName(name);
    }

    /// <summary><c>CBaseAnimating::SetBodygroup</c> (baseanimating.cpp:2294-2302):
    /// <paramref name="body"/> with group <paramref name="group"/> set to
    /// <paramref name="value"/> over the model's own groups, written to the
    /// entity's <c>m_nBody</c> and answered so the caller keeps it.</summary>
    public int SetBodygroup(EntityHandle entity, int body, int group, int value)
    {
        if (OpenHdr(entity) is { } hdr)
            body = hdr.SetBodygroup(body, group, value);

        _skins.SetBody(entity, body);
        return body;
    }

    private StudioHdr? OpenHdr(EntityHandle entity)
        => _models.OpenStudioHdr(_precache.PrecacheModel(_render.GetModelName(entity)));
}
