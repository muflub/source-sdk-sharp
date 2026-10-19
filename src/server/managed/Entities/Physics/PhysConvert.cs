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
/// phys_convert: the map turning scenery into furniture, on cue.
///
/// The interesting thing about it is that it does not act on itself or on
/// something it holds. It names a set of OTHER entities, and when told to,
/// replaces each of them with a simulated copy — so the entity a map wired to
/// afterwards is not the entity the mapper drew.
///
/// It converts everything sharing the target name, which matters: sharing a
/// targetname is how a map treats six panes of glass as one thing, and
/// converting only the first would leave five hanging in the air.
/// </summary>
[ScriptedEntity(EntityNames.PhysConvert)]
public sealed class PhysConvert : ScriptedEntity
{
    public PhysConvert(IPhysicsConversion conversion)
    {
        Conversion = conversion;
    }

    /// <summary>Stock's conversion of one target (physobj.cpp), managed.</summary>
    private readonly IPhysicsConversion Conversion;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="PhysConvertVars"/>.</summary>
    [ScriptVars] public PhysConvertVars Vars = new();

    /// <summary>
    /// phys_convert's own spawnflags -- what state the entity it makes starts in.
    ///
    /// <para>
    /// The same two numbers phys_magnet's first two bits use, and NOT the same
    /// meanings: 2 is debris here and motion-disabled there. Keeping them in
    /// separate types is the whole reason these are enums.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        Asleep = 0b0000_0001,
        Debris = 0b0000_0010,
    }

    public readonly Output OnConvert = new("OnConvert");

    [ScriptedEvent("ConvertTarget")]
    private void OnConvertTarget()
    {
        // Fired before the work rather than after, which is stock's order and
        // the useful one: the converted entities replace the originals, so
        // anything the output triggers is looking at a world mid-change either
        // way, and firing first means it fires even if nothing converts.
        OnConvert.Fire();

        EntityHandle swap = Vars.SwapModel.Length > 0
            ? Entities.FindByName(Vars.SwapModel)
            : EntityHandle.None;

        // With a swap model, ONE. The replacement borrows a model the original
        // never had, and doing that to a whole group would give them all the
        // same one — stock caps it here for the same reason.
        // (A limit is not the cap: a name-lookup limit bounds the shelled half
        // only, so the first match is taken by stopping, as the old
        // RemoveRange( 1, ... ) did.)
        using NameMatches targets = NameLookup.Named(Vars.Target);

        foreach (EntityHandle target in targets)
        {
            // Stock's loop body. A target that is already physics says so
            // itself (stock's Msg), and one that could not be made says
            // nothing more, as stock.
            Conversion.Convert(target, swap, Vars.MassOverride,
                               asleep: Flags.HasFlag(SpawnFlag.Asleep),
                               debris: Flags.HasFlag(SpawnFlag.Debris));

            if (Entities.IsValid(swap))
                break;
        }

        if (targets.Count == 0)
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': nothing named '{Vars.Target}'\n");
    }
}

/// <summary>
/// The map keys <see cref="PhysConvert"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysConvertVars
{
    /// <summary>
    /// An entity whose MODEL to borrow — not a model name, and not the thing
    /// being converted. Stock's indirection, and how a map turns a pristine wall
    /// into an already-cracked one in the same motion.
    /// </summary>
    /// <summary>
    /// What to convert. The plain "target" key, which CBaseEntity also claims —
    /// harmless, because both sides see every keyvalue.
    /// </summary>
    public string Target { get; set; } = string.Empty;

    public string SwapModel { get; set; } = string.Empty;

    /// <summary>Mass for the result. Zero keeps whatever the model implies.</summary>
    public float MassOverride { get; set; }
}
