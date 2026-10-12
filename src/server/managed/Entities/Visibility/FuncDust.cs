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
/// func_dustmotes and func_dustcloud: a volume with motes drifting in it.
///
/// <para>
/// The server entity is a brush and eleven keys, and stock's is no more than
/// that — <c>CFunc_Dust::Spawn</c> binds the model, fixes up the alpha and
/// stops. Everything else is on the client, and the two classes differ by a
/// single bit: <see cref="DustFlags.ScaleMotes"/>, which decides whether a mote
/// keeps its size on screen or in the world.
/// </para>
///
/// <para>
/// So this class is both of them, and the bit is the whole of the distinction.
/// Two classes would have shared everything and disagreed about nothing.
/// </para>
///
/// <para>
/// The alpha fix-up is worth keeping rather than tidying. Keys arrive in
/// whatever order the map wrote them, and parsing a "Color" key stamps a 255
/// over the alpha channel — so stock reads "Alpha" into a separate field and
/// puts it back at spawn, once both have certainly arrived. Doing it at OnSpawn
/// here has the same guarantee for the same reason.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncDustmotes, ScriptedEntityKind.Brush)]
[ScriptedEntity(EntityNames.FuncDustcloud, ScriptedEntityKind.Brush)]
public sealed class FuncDust : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="FuncDustVars"/>.</summary>
    [ScriptVars] public FuncDustVars Vars = new();

    // One table per classname, because a table is keyed by the name the entity
    // is spawned under. The FIELDS are declared once — see IDustNetState — so
    // the two cannot drift.
    [NetState] public DustMotesNetState Motes = new();
    [NetState] public DustCloudNetState Cloud = new();

    /// <summary>Whichever of the two this instance actually is.</summary>
    private IDustNetState State => ClassName == EntityNames.FuncDustmotes
        ? Motes
        : (IDustNetState)Cloud;

    public override void OnSpawn()
    {
        // A volume, not an obstacle: passable, and drawn by nothing. The brush
        // still has to be SOLID_BSP underneath — which is what BrushSolidity
        // leaves it as, adding only FSOLID_NOT_SOLID — because the CLIENT asks
        // the brush "is this point inside you" for every place it considers
        // putting a mote, and a collideable with no shape answers no every time.
        SetSolidity(BrushSolidity.None);
        SetVisible(false);

        var state = State;

        state.Red   = ToByte(Vars.Color.X);
        state.Green = ToByte(Vars.Color.Y);
        state.Blue  = ToByte(Vars.Color.Z);

        // AFTER the colour, and that ordering is stock's bug fix rather than a
        // style: parsing a colour key stamps 255 over the alpha, and the two
        // keys arrive in whatever order the map wrote them.
        state.Alpha = (byte)Math.Clamp(Vars.Alpha, 0, 255);

        state.SpawnRate   = Vars.SpawnRate;
        state.SizeMin     = Vars.SizeMin;
        state.SizeMax     = Vars.SizeMax;
        state.SpeedMax    = Vars.SpeedMax;
        state.LifetimeMin = Vars.LifetimeMin;
        state.LifetimeMax = Vars.LifetimeMax;
        state.DistMax     = Vars.DistanceMax;
        state.FallSpeed   = Vars.FallSpeed;

        DustFlags flags = 0;

        if (!Vars.StartDisabled)
            flags |= DustFlags.On;

        if (Vars.Frozen)
            flags |= DustFlags.Frozen;

        // The one thing that separates the two classnames.
        if (ClassName == EntityNames.FuncDustmotes)
            flags |= DustFlags.ScaleMotes;

        state.DustFlags = (int)flags;
    }

    [ScriptedEvent("TurnOn")]  private void OnTurnedOn()  => SetOn(true);
    [ScriptedEvent("TurnOff")] private void OnTurnedOff() => SetOn(false);

    private void SetOn(bool on)
    {
        var state = State;

        state.DustFlags = on
            ? state.DustFlags | (int)DustFlags.On
            : state.DustFlags & ~(int)DustFlags.On;
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)value, 0, 255);
}

/// <summary>
/// The map keys <see cref="FuncDust"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncDustVars
{
    public Vector Color { get; set; } = new(255f, 255f, 255f);

    public int Alpha { get; set; }

    public int SpawnRate { get; set; }

    public float SizeMin { get; set; }

    public float SizeMax { get; set; }

    public int SpeedMax { get; set; }

    public int LifetimeMin { get; set; }

    public int LifetimeMax { get; set; }

    [VarName("DistMax")]
    public int DistanceMax { get; set; }

    public float FallSpeed { get; set; }

    public bool StartDisabled { get; set; }

    /// <summary>Emit one batch and hold it. A separate key from StartDisabled: a frozen volume IS emitting, once.</summary>
    public bool Frozen { get; set; }
}
