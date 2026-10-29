using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// color_correction_volume: a lookup table applied to the whole screen while
/// the player stands inside a brush.
///
/// <para>
/// The server half is deliberately thin, and how thin is the decision this
/// port had to make. Stock runs the whole entity on the server: a trigger that
/// notices "the local player" entering, a think that ramps one networked
/// weight, and a client that applies the number it is sent. That design only
/// ever meant anything in single-player, because the trigger filter is
/// <c>pEntity == UTIL_GetLocalPlayer()</c> and <c>UTIL_GetLocalPlayer()</c> is
/// NULL whenever <c>maxClients &gt; 1</c> — in this mod the volume has never
/// once fired, and the weight it networks has never left zero.
/// </para>
///
/// <para>
/// The fix is not a weight per client — that is per-player state on a wire
/// that has no per-player rows, and it was rightly refused once already. The
/// fix is noticing which bucket the question lives in: "is the player inside
/// this volume" is a per-viewer fact, the same bucket as its point sibling's
/// distance falloff, and per-viewer facts belong to the client (Part 4b,
/// bucket 3). So the server ingests the map's keys, freezes the brush's box
/// into the net state, answers Enable and Disable, and decides nothing else.
/// Each client runs its own crossing test and its own fade, every player gets
/// the grade exactly when THEY walk in, and nothing is networked per player.
/// </para>
///
/// <para>
/// Not a <see cref="TriggerVolume"/>, although stock derives from
/// CBaseTrigger: this entity no longer takes touches, so inheriting the
/// touch-tracking machinery would be carrying the half of the design the port
/// exists to move. The brush is non-solid, invisible, and always transmitted —
/// the whole screen depends on it, so every client needs it wherever they are.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.ColorCorrectionVolume, ScriptedEntityKind.Brush)]
public sealed class ColorCorrectionVolume : ScriptedEntity
{
    public ColorCorrectionVolume(IEngineEntityRender render)
    {
        Render = render;
    }

    /// <summary>what this entity looks like.</summary>
    private readonly IEngineEntityRender Render;

    [NetState] public ColorCorrectionVolumeNetState State = new();

    /// <summary>What a map may set on this entity. See <see cref="ColorCorrectionVolumeVars"/>.</summary>
    [ScriptVars] public ColorCorrectionVolumeVars Vars = new();

    public override void OnSpawn()
    {
        // Passable and invisible, like every volume; and transmitted to
        // everyone always, like its point sibling, because a fade that started
        // while the volume was in the PVS must keep arriving after the player
        // walks out of it.
        SetSolidity(BrushSolidity.Trigger);
        SetVisible(false);
        Render.SetTransmitAlways(Handle, true);

        State.LookupFile   = Vars.LookupFile;
        State.MaxWeight    = Vars.MaxWeight;
        State.FadeDuration = Vars.FadeDuration;
        State.Enabled      = !Vars.StartDisabled;

        // The brush, frozen into two world-space corners. GetBounds answers
        // offsets from the origin; a color-correction volume has no motion of
        // any kind, so adding the origin once at spawn is the whole transform.
        var (mins, maxs) = Entities.GetBounds(Handle);
        Vector origin = Origin;

        State.BoundsMin = origin + mins;
        State.BoundsMax = origin + maxs;
    }

    [ScriptedEvent("Enable")]
    private void OnEnable() => State.Enabled = true;

    [ScriptedEvent("Disable")]
    private void OnDisable() => State.Enabled = false;
}

/// <summary>
/// The map keys <see cref="ColorCorrectionVolume"/> declares.
/// </summary>
[ScriptVars]
public partial class ColorCorrectionVolumeVars
{
    [VarName("filename")]
    public string LookupFile { get; set; } = string.Empty;

    /// <summary>The weight ceiling. Stock parses this key and then ignores it.</summary>
    public float MaxWeight { get; set; } = 1f;

    /// <summary>
    /// Seconds for the full crossing fade, either direction. Defaults to zero
    /// — instant — which is what a map that omits the key got from stock: an
    /// uninitialised field, a division by zero in the think, and a weight
    /// clamped to full on the next tick. Hammer writes the key on every entity
    /// it authors (the FGD default is 10), so the default only decides
    /// hand-edited maps, and it decides them the way stock did.
    /// </summary>
    public float FadeDuration { get; set; }

    public bool StartDisabled { get; set; }
}
