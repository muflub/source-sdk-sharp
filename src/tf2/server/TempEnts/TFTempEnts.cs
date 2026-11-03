//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Server;

/// <summary>The TF2 temp entities the damage path sends.</summary>
public interface ITFTempEnts
{
    /// <inheritdoc cref="TFTempEnts.Blood"/>
    void Blood(TeFilter filter, float delay, Vector origin, Vector normal, int entIndex);
}

/// <summary>
/// TF2's temp entity sends, over the wire exactly as
/// <see cref="SourceSharp.Server.TempEnts"/> is: a declaration resolved by its
/// class name, a blob built from it, and <c>TempEntPlayback</c>.
///
/// <para>
/// IT LIVES IN TF2.SERVER AND NOT THE CORE because its declaration does:
/// <c>TeTFBlood</c> compiles into this side's game assembly, which the core's
/// <c>ITempEnts</c> cannot name. The registry and the engine slot are the
/// composition's, injected — the server module registers every side assembly's
/// declarations, TF2's among them, so the handle the core's registry answers
/// and the client's agree by the same key-ordered scan.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFTempEnts)])]
public sealed class TFTempEnts(TempEntRegistry registry, IEngineEffects engine, ILog log) : ITFTempEnts
{
    // Said once, as the core's writer does: a refused registration is a
    // console line naming why, not a per-hit warning in a firefight.
    private readonly HashSet<string> _complained = new(StringComparer.Ordinal);

    /// <summary>
    /// <c>TE_TFBlood</c> (<c>te_tfblood.cpp:55</c>): the hit's origin, its
    /// normal, and the edict that bled.
    /// </summary>
    public void Blood(TeFilter filter, float delay, Vector origin, Vector normal, int entIndex)
    {
        TempEntType? type = registry.Find(ClassNameOf<TeTFBlood>());

        if (type is not { Registered: true } declared)
        {
            if (_complained.Add(ClassNameOf<TeTFBlood>()))
                log.Warning(
                    $"[SourceSharp] temp entity '{ClassNameOf<TeTFBlood>()}' is not registered on " +
                    "this side; every blood send does nothing. The registration line in the " +
                    "console says why it was refused.\n");

            return;
        }

        var state = new TeTFBlood
        {
            Origin   = origin,
            Normal   = normal,
            EntIndex = entIndex,
        };

        engine.TempEntPlayback(declared.Handle, state.ToBlob(declared), filter, delay);
    }

    /// <summary>The class name the declaration carries, not a restated literal.</summary>
    private static string ClassNameOf<T>() where T : TempEntState, new()
        => TempEntityAttribute.ClassNameOf(typeof(T));
}
