//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// C_PhysPropClientside::HitSurface's blood splat, over the engine.
///
/// <para>
/// WHETHER: PROPINTER_WORLD_BLOODSPLAT, which ParsePropData sets only for a
/// model whose $keyvalues hold a <c>prop_data</c> section, and then from its
/// <c>world_interactions</c> block's <c>"onworldimpact" "bloodsplat"</c>
/// (props_shared.cpp's sPropdataInteractionSections). A <c>base</c> is not
/// looked up: this client loads no propdata.txt, and a base only adds values,
/// never interactions. Asked once per model.
/// </para>
///
/// <para>
/// THE SPLAT: UTIL_BloodDecalTrace( &amp;tr, BLOOD_COLOR_RED ) -- nothing
/// under <c>violence_hblood 0</c> -- then UTIL_DecalTrace's "Blood" on the
/// trace's entity, whose DecalTrace is a TE decal with no hitbox, which the
/// client's TE_Decal lays through AddDecal from the trace's start to its end.
/// </para>
/// </summary>
public sealed class ClientPropHitSurfaces(Func<string, string?> modelKeyValues, IClientDecals decals,
                                         IEngineWorldQuery world, IEngineEffects effects, Func<bool> showRedBlood)
    : IClientPropHitSurfaces
{
    private readonly Dictionary<string, bool> _splats = new(StringComparer.OrdinalIgnoreCase);

    public bool BloodSplats(string model)
    {
        if (_splats.TryGetValue(model, out bool known))
            return known;

        bool splats = Parse(modelKeyValues(model));
        _splats[model] = splats;
        return splats;
    }

    /// <summary>ParsePropData's gate and the interaction it reads, over the model's $keyvalues text.</summary>
    public static bool Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        KeyValuesText document;
        try
        {
            document = KeyValuesText.Parse(text);
        }
        catch (KeyValuesTextException)
        {
            return false;
        }

        // LoadFromBuffer reads the first root; FindKey searches its children.
        if (document.Children.Count == 0)
            return false;

        KeyValuesText root = document.Children[0];
        if (root.FindPath("prop_data") is null)
            return false;

        string? onImpact = root.FindPath("world_interactions")?.GetString("onworldimpact", null);
        return !string.IsNullOrEmpty(onImpact) && string.Equals(onImpact, "bloodsplat", StringComparison.OrdinalIgnoreCase);
    }

    public void BloodDecal(EntityHandle other, Vector start, Vector end)
    {
        if (!showRedBlood())
            return;

        int index = decals.IndexForName("Blood");
        if (index < 0)
            return;

        ClientEntityDecal.AddDecal(world, effects, other.EntryIndex, rayStart: start, rayEnd: end,
                                   decalCenter: end, decalIndex: index);
    }
}
