//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SourceSharp.Abi;

/// <summary>
/// The two ABI slots <see cref="StandaloneMirror"/> replaced, still callable —
/// for the divergence gate and for nothing else.
///
/// <para>
/// <b>The slots are KEPT, and this is half of why.</b> A push mirror's failure
/// mode is a write site nobody pushed from: it does not go stale, it diverges
/// and then answers confidently forever. Holding it shut needs an INDEPENDENT
/// oracle, and the slots are one — they read the shell's own members, which are
/// what the engine and the native render path actually use. A gate that
/// compared the mirror against something managed had also written would agree
/// with itself whatever the shell did, which is the shape of check this project
/// has been misled by more than forty times.
/// </para>
///
/// <para>
/// <b>Game code must not call these.</b> Every reader goes through
/// <see cref="IEngineEntityRender"/>, which is what makes the reroute one
/// change in one place. These are here because <c>cs_cl_mirror_check</c> lives
/// in the client assembly, which cannot see <c>Engine</c>, and calling the
/// binding instead would compare the mirror against itself.
/// </para>
/// </summary>
public sealed class StandaloneSlotReference
{
    // ONE side's engine (plan_abi_hotreload D8): the client composition
    // registers one over its own ModuleEngine.
    private readonly EngineSide _side;

    // The client's skeleton, once its composition fills the route (plan
    // physics_bones_native_zero 1.2): the sequence clock is its, not native's.
    private readonly StandaloneSkeletonRoute? _skeletons;

    public StandaloneSlotReference(ModuleEngine engine, StandaloneSkeletonRoute? skeletons = null)
    {
        _side = (engine ?? throw new ArgumentNullException(nameof(engine))).Api;
        _skeletons = skeletons;
    }

    internal StandaloneSlotReference(EngineSide side) => _side = side;

    /// <summary>
    /// The sequence clock the skeleton keeps: the managed skeleton's (the
    /// record the mirror is checked against is the skeleton's, an
    /// implementation of its own); nothing before the route is filled -- the
    /// native slot it read is retired.
    /// </summary>
    public bool SequenceState(EntityHandle entity, out int sequence, out float cycle,
                              out float duration, out bool looping, out int starts)
    {
        if (_skeletons?.Skeletons is IClientStandaloneSkeletons skeletons)
            return skeletons.SequenceState(entity, out sequence, out cycle, out duration, out looping, out starts);

        sequence = 0;
        cycle = 0f;
        duration = 0f;
        looping = false;
        starts = 0;
        return false;
    }

    /// <inheritdoc cref="EngineSide.StandaloneIsDormant"/>
    public bool IsDormant(EntityHandle entity) => _side.StandaloneIsDormant(entity);
}
