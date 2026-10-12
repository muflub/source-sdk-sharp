//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// A model's auto-emitting particles, from its $keyvalues text —
/// plan_server_novalve.md P2.4, out of Precache_ModelComponents
/// (precache_model.cpp): the text's first root's "Particles" section, and each
/// entry's "name" (empty when it has none, which stock precached as well).
/// </summary>
public static class ModelParticles
{
    public static List<string> Names(string? modelKeyValueText)
    {
        var names = new List<string>();

        if (string.IsNullOrEmpty(modelKeyValueText))
            return names;

        KeyValuesText document;
        try
        {
            document = KeyValuesText.Parse(modelKeyValueText);
        }
        catch (KeyValuesTextException)
        {
            return names;
        }

        if (document.Children.Count == 0)
            return names;

        KeyValuesText? particles = document.Children[0].FindPath("Particles");
        if (particles is null)
            return names;

        foreach (var effect in particles.Children)
            names.Add(effect.GetString("name", "") ?? "");

        return names;
    }
}
