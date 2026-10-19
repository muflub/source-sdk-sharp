//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// The FCVAR_* bits, all of them. Mirrors public/tier1/iconvar.h.
///
/// All of them, and not the handful a mod was expected to want, because the same
/// enum now says two things: what a C# declaration ASKS for, and what a convar
/// the engine registered turned out to CARRY. A subset was fine while it only
/// ever did the first — but a listing that renders an unnamed bit as a decimal
/// number spliced between two names has told the reader nothing, and a
/// declaration that cannot ask for FCVAR_HIDDEN when every other kind of convar
/// can is a limit with no reason behind it.
///
/// Unsigned because the top bit is one of them. As a signed enum
/// ExecDespiteDefault is negative, and a flag word carrying it compares as less
/// than zero in code that had no idea it was doing arithmetic.
/// </summary>
[Flags]
public enum ConVarFlags : uint
{
    None                  = 0,

    /// <summary>Not added to the registry at all. Rarely what a mod wants.</summary>
    Unregistered          = 1u << 0,
    /// <summary>Compiled out of a released build.</summary>
    DevelopmentOnly       = 1u << 1,
    /// <summary>Declared by the game DLL. The engine adds this to ours already.</summary>
    GameDll               = 1u << 2,
    /// <summary>Declared by the client DLL. Likewise added for us.</summary>
    ClientDll             = 1u << 3,
    /// <summary>Absent from find and autocomplete, but still there.</summary>
    Hidden                = 1u << 4,

    /// <summary>A server value never sent to clients — a password, say.</summary>
    Protected             = 1u << 5,
    /// <summary>Cannot be changed by a client connected to a multiplayer server.</summary>
    SpOnly                = 1u << 6,
    /// <summary>Saved to config.cfg.</summary>
    Archive               = 1u << 7,
    /// <summary>Players are told when it changes.</summary>
    Notify                = 1u << 8,
    /// <summary>Client sends its value to the server.</summary>
    UserInfo              = 1u << 9,
    /// <summary>The value may not contain unprintable characters.</summary>
    PrintableOnly         = 1u << 10,
    /// <summary>Changes are kept out of the server log.</summary>
    Unlogged              = 1u << 11,
    /// <summary>Never printed as a string.</summary>
    NeverAsString         = 1u << 12,
    /// <summary>Replicated from server to clients. Both sides must declare it.</summary>
    Replicated            = 1u << 13,
    /// <summary>Blocked unless sv_cheats is on.</summary>
    Cheat                 = 1u << 14,
    /// <summary>Not archived, but exposed to players and allowed in competitive play.</summary>
    InternalUse           = 1u << 15,
    /// <summary>Recorded when a demo starts.</summary>
    Demo                  = 1u << 16,
    /// <summary>Kept out of demo files.</summary>
    DontRecord            = 1u << 17,
    /// <summary>Changeable in strict competitive settings mode.</summary>
    AllowedInCompetitive  = 1u << 18,
    /// <summary>A change forces a material reload.</summary>
    ReloadMaterials       = 1u << 20,
    /// <summary>A change forces a texture reload.</summary>
    ReloadTextures        = 1u << 21,
    /// <summary>Cannot be changed by a client that is connected to a server.</summary>
    NotConnected          = 1u << 22,
    /// <summary>Read from the material system thread.</summary>
    MaterialSystemThread  = 1u << 23,
    /// <summary>Written to config.cfg on the Xbox.</summary>
    ArchiveXbox           = 1u << 24,
    /// <summary>Safe to read from other threads.</summary>
    AccessibleFromThreads = 1u << 25,
    /// <summary>The server may run this on clients.</summary>
    ServerCanExecute      = 1u << 28,
    /// <summary>The server may not query this value.</summary>
    ServerCannotQuery     = 1u << 29,
    /// <summary>IVEngineClient::ClientCmd may run this.</summary>
    ClientCmdCanExecute   = 1u << 30,
    /// <summary>Runs even under -default, which otherwise ignores it.</summary>
    ExecDespiteDefault    = 1u << 31,
}
