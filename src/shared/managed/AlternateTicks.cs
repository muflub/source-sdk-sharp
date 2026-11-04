//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>Whether the game simulates entities on even-numbered ticks only.</summary>
public interface IAlternateTicks
{
    bool Simulating { get; }
}

/// <summary>
/// <c>CBaseEntity::IsSimulatingOnAlternateTicks</c> (baseentity_shared.cpp): only a
/// single-player game simulates on alternate ticks, and only with sv_alternateticks.
/// </summary>
public sealed class AlternateTicks(IEngineEnvironment environment, SvAlternateTicksConVar convar) : IAlternateTicks
{
    public bool Simulating
    {
        get
        {
            Globals globals = environment.Globals;
            return globals.IsAvailable && globals.MaxClients == 1 && convar.Value;
        }
    }
}
