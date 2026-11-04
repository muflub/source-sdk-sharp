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
/// The engine's violence ConVars, read the way stock reads them. The four are
/// the engine's own (ConVars.cs says why none is declared here).
/// </summary>
public sealed class ViolenceConVars(ViolenceHBloodConVar humanBlood, ViolenceABloodConVar alienBlood,
                                    ViolenceHGibsConVar humanGibs, ViolenceAGibsConVar alienGibs)
{
    public readonly ViolenceHBloodConVar HumanBlood = humanBlood;
    public readonly ViolenceABloodConVar AlienBlood = alienBlood;
    public readonly ViolenceHGibsConVar HumanGibs = humanGibs;
    public readonly ViolenceAGibsConVar AlienGibs = alienGibs;
}

/// <summary>
/// Stock's violence rules over the convars above.
/// </summary>
public static class Violence
{
    /// <summary>
    /// <c>UTIL_ShouldShowBlood</c> (util_shared.cpp): no blood for
    /// <see cref="BloodColor.None"/>; red blood when <c>violence_hblood</c>
    /// is on, any other colour when <c>violence_ablood</c> is.
    /// </summary>
    public static bool ShouldShowBlood(BloodColor color, ViolenceHBloodConVar human, ViolenceABloodConVar alien)
    {
        if (color == BloodColor.None)
            return false;

        return color == BloodColor.Red ? human.Value : alien.Value;
    }
}
