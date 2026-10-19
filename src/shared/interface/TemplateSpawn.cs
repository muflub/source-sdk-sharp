//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// One template, and where its copy goes.
///
/// <para>
/// The unit both halves of the template boundary speak in. Coming BACK from a
/// capture it says "this index is the template made from the entity that was
/// standing here"; going DOWN to a spawn it says "put a copy of this index
/// here". The same three things either way, which is why it is one type — the
/// arithmetic in between, <see cref="Transform.Relative"/> and
/// <see cref="Transform.Placed"/>, is exactly the conversion from the first
/// reading to the second.
/// </para>
/// </summary>
/// <param name="Index">The template store's index for this template.</param>
/// <param name="Origin">Where, in world space.</param>
/// <param name="Angles">Facing which way.</param>
public readonly record struct TemplatePlacement(int Index, Vector Origin, QAngle Angles);

/// <summary>
/// One copy of one template, on its way DOWN to the parser.
///
/// <para>
/// <see cref="TemplatePlacement"/>'s counterpart, and it is a second type
/// because the two directions stopped carrying the same three things. Coming
/// back from a capture, a template is an INDEX — the store's name for text the
/// store is holding. Going down to a spawn or a precache it is the TEXT, because
/// the store is managed now (plan_server_all.md Wave 1) and the far side has no
/// index to look anything up in.
/// </para>
///
/// <para>
/// The arithmetic in between is unchanged: <see cref="Transform.Relative"/> at
/// capture and <see cref="Transform.Placed"/> at spawn.
/// </para>
/// </summary>
/// <param name="MapData">
/// The template's block of map text — the STAMPED copy when the group's entity
/// I/O was rewritten and this instance needs its own names, the stored one
/// otherwise. That choice is the store's and is made before this is built.
/// </param>
/// <param name="StringSize">
/// How big the buffer holding that text is. Stock's
/// <c>Templates_GetStringSize</c>, passed on to <c>CEntityMapData</c> so that a
/// parser that rewrites a value knows what room it has.
/// </param>
/// <param name="Origin">Where, in world space.</param>
/// <param name="Angles">Facing which way.</param>
public readonly record struct TemplateSpawn(byte[] MapData, int StringSize,
                                            Vector Origin, QAngle Angles);

/// <summary>
/// One entity of a template instance, ALREADY PARSED — plan_server_novalve.md
/// P5. Native no longer reads map text, so a template copy crosses as its
/// classname and its key/value pairs in block order (classname included, as
/// <c>CEntityMapData</c>'s walk handed them to the entity), with the
/// transform the caller resolved.
/// </summary>
public readonly record struct KeyedSpawn(string ClassName,
                                         IReadOnlyList<KeyValuePair<string, string>> Keys,
                                         Vector Origin, QAngle Angles);
