//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// What <see cref="TempEntSpawn.BreakHelper"/> asks for.
///
/// <para>
/// Stock's <c>CBreakableHelper</c> takes a bool — <c>Insert( pTemp, isSlave )</c>
/// — and the third state is not calling it at all. Three named values rather
/// than a nullable bool, because "not in a break group" is the answer for
/// sixteen of the seventeen helpers and deserves a name.
/// </para>
/// </summary>
public static class BreakGroup
{
    /// <summary>Not a break-model gib. Everything except <c>BreakModel</c>.</summary>
    public const int None = 0;

    /// <summary>
    /// The first gib of a batch. Its origin is the lighting origin every slave
    /// in the batch borrows, and when it dies the next one inherits the job.
    /// </summary>
    public const int Master = 1;

    /// <summary>
    /// One of the rest. Stock decides this from <c>BREAK_SLAVE</c> in the
    /// temp entity's own flags, so a server that sends two <c>TE_BreakModel</c>
    /// for one crate can have the second batch light off the first.
    /// </summary>
    public const int Slave = 2;
}

/// <summary>
/// <c>modelinfo-&gt;GetModelType</c>'s answer. Source's <c>modtype_t</c>
/// (model_types.h), with its own numbering — the native slot passes the value
/// through rather than translating it, so these must stay in step with that
/// enum and not merely with each other.
/// </summary>
public enum ModelType
{
    /// <summary>Nothing — also what an index that resolves to no model answers.</summary>
    Bad = 0,

    /// <summary>A brush model: <c>*12</c> and the like.</summary>
    Brush = 1,

    /// <summary>A <c>.vmt</c> sprite. <c>BreakModel</c> randomises the FRAME of one.</summary>
    Sprite = 2,

    /// <summary>An <c>.mdl</c>. <c>BreakModel</c> randomises the BODY of one.</summary>
    Studio = 3,
}
