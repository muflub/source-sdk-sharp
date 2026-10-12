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
/// A base class with a map name: "trigger". It does nothing, and that is the
/// whole of it.
///
/// CBaseTrigger is registered under its own bare name in triggers.cpp, so a map
/// may contain one — but CBaseTrigger::Spawn never calls InitTrigger, which is
/// what gives a volume its brush model, its trigger flag and its invisibility.
/// A placed one therefore has no shape, is never touched, and never fires any of
/// the six outputs its datadesc declares. Hammer does not offer it and nothing
/// in the SDK creates one by name; it is the base of every real trigger, exposed
/// by accident.
///
/// So this class exists to hold the name and to be as inert as the C++ one. The
/// brush shell's default is a solid, visible wall, which is what a map author
/// would get instead — a volume drawn around a doorway suddenly blocking it.
/// </summary>
[ScriptedEntity(EntityNames.Trigger, ScriptedEntityKind.Brush)]
public sealed class Trigger : ScriptedEntity
{
    public override void OnSpawn()
    {
        SetSolidity(BrushSolidity.None);
        SetVisible(false);
    }
}
