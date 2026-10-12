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
/// Passes everybody, and negated, nobody.
///
/// CBaseFilter::PassesFilterImpl answers true and nothing else, and stock
/// registers that class under filter_base — so this is not an abstract base
/// leaking into the map list, it is a real entity whose question is "anyone?".
/// It gets a class of its own because FilterBase here is abstract and a map
/// asking for filter_base needs something spawnable; and negated it is the half
/// a mapper actually reaches for, a named filter that lets nobody through.
/// </summary>
[ScriptedEntity(EntityNames.FilterBase)]
public sealed class FilterAlways : FilterBase
{
    protected override bool Matches(EntityHandle entity) => true;
}
