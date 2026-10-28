//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client;

/// <summary>
/// How far behind the server the standalone hierarchy draws: stock's
/// <c>C_BaseEntity::GetInterpolationAmount</c> for a networked, unpredicted
/// entity in multiplayer — the client's interp amount rounded to whole ticks,
/// plus a tick (two when the server simulates on alternate ticks) so that a
/// value arriving a tick late still has something to blend towards. Nothing on
/// this hierarchy is predicted or client-created, so stock's other arms have no
/// case to answer. (Lane M1-A: <c>StandaloneInterpolationAmount</c> in the
/// shell computed this natively.)
/// </summary>
public static class InterpolationDelay
{
    /// <summary><c>TICKS_TO_TIME( TIME_TO_TICKS( clientInterp ) + ( alternateTicks ? 2 : 1 ) )</c>.</summary>
    public static float Amount(float clientInterp, float tickInterval, bool alternateTicks)
    {
        if (tickInterval <= 0f)
            return 0f;
        int ticks = (int)(0.5f + clientInterp / tickInterval);
        return tickInterval * (ticks + (alternateTicks ? 2 : 1));
    }

    /// <summary>
    /// <c>CBaseEntity::IsSimulatingOnAlternateTicks</c>: only a single-player
    /// game simulates on alternate ticks, and only with sv_alternateticks.
    /// </summary>
    public static bool AlternateTicks(int maxClients, Func<string?> alternateTicksConVar)
        => maxClients == 1 && VariantParse.Atof(alternateTicksConVar() ?? "0") != 0f;
}

/// <summary>
/// The interpolation pass's list (lane M1-A): every standalone block the shell
/// hands over, less the ones whose position is computed rather than received —
/// the local VIEWMODEL, pinned to the eye by the view setup, and the local
/// player while the managed replay drives it (a point read from the networked
/// past must not overwrite where the replay says it is now). The admitted set
/// is handed back to the shell, whose PreRender walk re-transforms exactly
/// those; the delay is <see cref="InterpolationDelay"/>'s.
///
/// <para>
/// The shell decided both natively until then (<c>WantsInterpolation</c> and
/// <c>StandaloneInterpolationAmount</c>).
/// </para>
/// </summary>
public sealed class InterpolatedSet : IInterpolationBlocks
{
    private readonly IInterpolationBlocks _all;
    private readonly Func<uint> _drivenHandle;
    private readonly Func<float> _tickInterval;
    private readonly Func<bool> _alternateTicks;
    private readonly IInterpolationAdmission _admission;

    private readonly List<int> _admitted = [];
    private uint[] _handles = new uint[64];

    /// <param name="all">The shell's list: every block, and the client's interp amount.</param>
    /// <param name="drivenHandle">The entity the managed replay drives, or <c>EntityHandle.None</c>'s raw value.</param>
    /// <param name="tickInterval">The engine's interval per tick.</param>
    /// <param name="alternateTicks">Whether the server simulates on alternate ticks.</param>
    /// <param name="admission">Where the admitted set goes: the shell's PreRender walk.</param>
    public InterpolatedSet(IInterpolationBlocks all, Func<uint> drivenHandle, Func<float> tickInterval,
                           Func<bool> alternateTicks, IInterpolationAdmission admission)
    {
        _all = all;
        _drivenHandle = drivenHandle;
        _tickInterval = tickInterval;
        _alternateTicks = alternateTicks;
        _admission = admission;
    }

    private static readonly int HandleOffset = (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Head))
                                             + (int)Marshal.OffsetOf<EngineBlockHead>(nameof(EngineBlockHead.Handle));
    private static readonly int ViewModelOffset = (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Client))
                                                + (int)Marshal.OffsetOf<EngineBlockClientTail>(nameof(EngineBlockClientTail.ViewModel));

    /// <summary>Whether the pass may write a block: not the viewmodel, not the driven entity.</summary>
    public static bool Admits(ReadOnlySpan<byte> block, uint drivenHandle)
    {
        if (block.Length < EngineBlock.SizeBytes)
            return true;    // the pass skips a short block itself; its index stays
        if (BitConverter.ToInt32(block.Slice(ViewModelOffset, sizeof(int))) != 0)
            return false;
        return BitConverter.ToUInt32(block.Slice(HandleOffset, sizeof(uint))) != drivenHandle;
    }

    public int Begin(out float interpolationAmount)
    {
        int count = _all.Begin(out float clientInterp);
        interpolationAmount = InterpolationDelay.Amount(clientInterp, _tickInterval(), _alternateTicks());

        _admitted.Clear();
        uint driven = _drivenHandle();
        for (int i = 0; i < count; i++)
        {
            if (Admits(_all.Block(i), driven))
                _admitted.Add(i);
        }

        int n = _admitted.Count;
        if (_handles.Length < n)
            _handles = new uint[Math.Max(n, _handles.Length * 2)];
        int handles = 0;
        for (int k = 0; k < n; k++)
        {
            Span<byte> block = _all.Block(_admitted[k]);
            if (block.Length >= EngineBlock.SizeBytes)
                _handles[handles++] = BitConverter.ToUInt32(block.Slice(HandleOffset, sizeof(uint)));
        }
        _admission.Admitted(_handles.AsSpan(0, handles));

        return n;
    }

    public Span<byte> Block(int index) => _all.Block(_admitted[index]);
}
