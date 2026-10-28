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
/// The volume a player has to be standing in to be driving a
/// <see cref="FuncTrackTrain"/>.
///
/// It does one thing, once: finds the train it names, hands over its own box in
/// the train's frame, and stops thinking. Nothing reads it again — it is
/// SOLID_NONE, undrawn, and after the hand-off the train owns the only copy of
/// the numbers.
///
/// <para>
/// Which is why it does not need to travel with the train, although every mapper
/// parents it to one. The box is stored in the TRAIN's frame, and every test
/// re-derives the world volume from wherever the train is at the time, so the
/// volume follows the train whether or not this entity does. Stock gets the
/// offset for free from being parented; there is no parenting on this hierarchy,
/// so it is measured instead — the box's map position against the train's map
/// position, rotated into the train's facing.
/// </para>
///
/// <para>
/// Stock asserts that the mapper parented it, and the assert is about the
/// arithmetic being meaningful rather than about anything at runtime: an
/// unparented control box in stock reads its world position as an offset and
/// lands somewhere absurd. Said out loud here instead, because the measurement
/// above is right either way and a map that loads is better than a map that
/// stops.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncTraincontrols, ScriptedEntityKind.Brush)]
public sealed class FuncTrainControls : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="FuncTrainControlsVars"/>.</summary>
    [ScriptVars] public FuncTrainControlsVars Vars = new();

    private bool _handedOver;

    /// <summary>Where the mapper drew it, which is what the offset is measured from.</summary>
    internal Vector MapOrigin => SpawnOrigin;

    /// <summary>Its own box, as offsets from its own origin — stock's WorldAlignMins/Maxs.</summary>
    internal (Vector Mins, Vector Maxs) Box => Entities.GetBounds(Handle);

    public override void OnSpawn()
    {
        // A volume, not an obstacle, and nothing to look at.
        SetSolidNone();
        SetVisible(false);

        if (Vars.ParentName.Length == 0)
            Log.Warning($"[SourceSharp] func_traincontrols '{Name}' has no parentname, " +
                           "so the volume it describes is wherever the mapper drew it " +
                           "rather than a place on the train\n");

        // Later than the train's own deferred start, so that by the time this
        // runs the train has already found its track and settled on it. Nothing
        // here depends on that ordering — the box is measured from SPAWN
        // positions, which do not move — but a warning about a missing train is
        // worth being sure about before it is printed.
        SetNextThink(0.3f);
    }

    public override void OnThink()
    {
        if (_handedOver)
            return;

        _handedOver = true;

        var train = FindByName<FuncTrackTrain>(Vars.TrainName);

        if (train is null)
        {
            // Stock walks the entity list until it finds one whose classname is
            // func_tracktrain, and says "No train %s" when it runs out. A typed
            // lookup is the same question asked once.
            Log.Warning($"[SourceSharp] func_traincontrols '{Name}': " +
                           $"no func_tracktrain named '{Vars.TrainName}'\n");
            return;
        }

        train.SetControls(this);
    }
}

/// <summary>
/// The map keys <see cref="FuncTrainControls"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncTrainControlsVars
{
    /// <summary>The name of the train to hand the volume to.</summary>
    [VarName("target")]
    public string TrainName { get; set; } = string.Empty;

    /// <summary>
    /// Declared only to be able to tell a mapper they left it out. Nothing here
    /// can act on it: see the note on the class.
    /// </summary>
    public string ParentName { get; set; } = string.Empty;
}
