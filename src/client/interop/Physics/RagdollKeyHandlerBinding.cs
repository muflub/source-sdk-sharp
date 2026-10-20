//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The ragdolls' one key-handler table. A registry holds one live table per
/// contract, so the two handlers stock passes -- per solid,
/// physics_shared.cpp's <c>CSolidSetDefaults</c> (<c>g_SolidSetup</c>); per
/// custom block, ragdoll_shared.cpp's <c>CRagdollCollisionRules</c> and
/// <c>CRagdollAnimatedFriction</c> -- are one table answering for whichever
/// parse is in flight:
/// <list type="bullet">
///   <item>a SOLID (<see cref="SolidInFlight"/>): defaults are
///     <c>g_PhysDefaultObjectParams</c> written into the <c>solid_t</c>'s
///     params, and the <c>contents</c> key is kept (the ragdoll does not read it,
///     and stock's does not either);</item>
///   <item>a CUSTOM block: every key goes to <see cref="Current"/>, and
///     defaults are nothing, as both of stock's are.</item>
/// </list>
/// </summary>
public sealed unsafe class RagdollKeyHandlerBinding(Crossings crossings, SolidKeyDefaults solids) : IVPhysicsKeyHandlerSink, ISolidKeyTable
{
    /// <summary>bspflags.h's <c>CONTENTS_SOLID</c>.</summary>
    public const uint ContentsSolid = SolidKeyDefaults.ContentsSolid;

    // A .phy key or value is a token of a few dozen bytes; the bound only
    // stops a missing terminator from walking off.
    private const int MaxToken = 1024;

    /// <summary>The sink of the ParseCustom in flight, or null between parses.</summary>
    public IRagdollKeySink? Current { get; set; }

    /// <summary>Whether the parse in flight is a ParseSolid.</summary>
    public bool SolidInFlight { get; set; }

    /// <summary>The solid half: stock's <c>CSolidSetDefaults</c>, shared with the world's physics build.</summary>
    public SolidKeyDefaults Solids { get; } = solids;

    /// <summary>The table vphysics is handed, once <see cref="RagdollVPhysics.InstallHandlers"/> has built it.</summary>
    public nint Table { get; set; }

    /// <summary>The last solid's <c>contents</c> (<c>CSolidSetDefaults::GetContentsMask</c>).</summary>
    public uint ContentsMask => Solids.ContentsMask;

    public void ParseKeyValue(void* pData, byte* pKey, byte* pValue)
    {
        if (SolidInFlight)
        {
            crossings.Count(CrossingOp.ClientRagdollSolidKeyValue);
            Solids.ParseKeyValue(pKey, pValue);
            return;
        }

        crossings.Count(CrossingOp.ClientRagdollKeyValue);
        Current?.ParseKeyValue(Vtable.TextOf(pKey, MaxToken) ?? string.Empty, Vtable.TextOf(pValue, MaxToken) ?? string.Empty);
    }

    public void SetDefaults(void* pData)
    {
        if (!SolidInFlight)
        {
            crossings.Count(CrossingOp.ClientRagdollKeyDefaults);
            return;
        }

        crossings.Count(CrossingOp.ClientRagdollSolidDefaults);
        Solids.SetDefaults(pData);
    }

    /// <summary>The table vphysics is handed, built over this sink.</summary>
    public static ManagedVtable Create(ManagedVtables vtables, IVPhysicsKeyHandlerSink target)
        => IVPhysicsKeyHandlerSink__ManagedVtable.Create(vtables, target);
}
