//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// One decoded user command: <see cref="UserCmd"/> plus the two fields the
/// native run loop reads that the flat command never carried. Mirrors
/// <c>SourceSharpDecodedUserCmd</c> in sourcesharp.h.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DecodedUserCmd
{
    public UserCmd Cmd;
    public int WeaponSubtype;
    public int RandomSeed;
}
