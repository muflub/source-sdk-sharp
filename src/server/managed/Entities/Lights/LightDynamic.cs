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
/// light_dynamic: the one light in Source that is not baked.
///
/// <para>
/// Every other light in the family is consumed by vrad before the map ships.
/// This one is applied by the renderer every frame, which is what lets it move,
/// change colour, be switched, and follow something. The engine keeps a small
/// pool of them and the client's half of this entity asks for one each frame.
/// </para>
///
/// <para>
/// What is decided here: whether it is lit, how far it reaches, how bright, what
/// colour, whether it is a cone, and — the only part with any motion in it —
/// which way it points, re-derived every tenth of a second from whatever the
/// map told it to aim at.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.LightDynamic, ScriptedEntityKind.Effect)]
public sealed class LightDynamic : ScriptedEntity
{
    public LightDynamic(IEngineEntityRender render)
    {
        Render = render;
    }

    /// <summary>what this entity looks like.</summary>
    private readonly IEngineEntityRender Render;
    /// <summary>
    /// The engine clamps the colour exponent to a signed byte's usable range and
    /// stock warns rather than silently accepting a number that will wrap.
    /// </summary>
    private const int MinExponent = -127;
    private const int MaxExponent = 127;

    /// <summary>The same declaration the client half reads.</summary>
    [NetState] public DynamicLightNetState State = new();

    /// <summary>What a map may set on this entity. See <see cref="LightDynamicVars"/>.</summary>
    [ScriptVars] public LightDynamicVars Vars = new();

    /// <summary>
    /// The flags the map asked for, kept so <c>TurnOn</c> can put them back.
    ///
    /// <para>
    /// Switching off is not a flag of its own in stock: it sets both "no world"
    /// and "no model", which leaves a light that exists and illuminates
    /// nothing. Keeping the original separately is what makes that reversible,
    /// and it is why the spawnflags are read here rather than off the engine
    /// each time.
    /// </para>
    /// </summary>
    private DynamicLightFlags _mapFlags;

    private bool _on = true;

    /// <summary>Whether the light is currently lit. For tests and debug-api.</summary>
    public bool IsOn => _on;

    /// <summary>
    /// The colour, which arrives as vrad's "_light" key rather than as a render
    /// colour. Four components: RGB and a brightness this entity ignores,
    /// because its brightness is the exponent instead.
    /// </summary>
    public override bool OnKeyValue(string key, string value)
    {
        if (!key.Equals("_light", StringComparison.OrdinalIgnoreCase))
            return false;

        uint packed = SkyCamera.ParseColour(value);

        // WRITTEN WHOLE, which is what it always was. ParseColour packs R into
        // the low byte and Color32 stores it there too, so this is the same
        // three bytes reaching the same three bytes -- what has gone is the
        // unpack into three fields and back, and with it any way for a caller
        // to set two channels and leave the third holding the last map's.
        //
        // OPAQUE, AND THE FOURTH BYTE OF `packed` IS DROPPED ON PURPOSE. It is
        // vrad's brightness and this entity's brightness is the exponent, which
        // is why it was already being dropped when this unpacked three bytes by
        // hand. Passing it through as an alpha would network a number the client
        // must not read.
        State.Colour = new Color32((int)(packed & 0xFF),
                                   (int)((packed >> 8) & 0xFF),
                                   (int)((packed >> 16) & 0xFF));

        return true;
    }

    public override void OnSpawn()
    {
        // The spawnflags word is the engine's, so the cast is at the boundary
        // where it arrives rather than at each of the places that read it. The
        // mask is the field's declared width, which is four bits.
        _mapFlags = (DynamicLightFlags)(SpawnFlags & 0xF);

        State.Flags      = _mapFlags;
        State.Radius     = Vars.Distance;
        State.Exponent   = ClampExponent(Vars.Brightness);
        State.InnerAngle = Vars.InnerCone;
        State.OuterAngle = Vars.Cone;
        State.SpotRadius = Vars.SpotRadius;
        State.Style      = (byte)Math.Clamp(Vars.Style, 0, 63);
        State.Origin     = Origin;
        State.Direction  = Angles.Forward(Aim());

        // The client must be told about a light wherever the player is: a lamp
        // round the corner still lights the wall you can see. Stock adds
        // EFL_FORCE_CHECK_TRANSMIT for the same reason.
        Render.SetTransmitAlways(Handle, true);

        // Only think if there is something to follow. A static light is set up
        // once and never touched again, and there are a great many of them.
        if (Vars.Target.Length > 0)
            SetNextThink(0.1f);
    }

    /// <summary>
    /// Where the light points.
    ///
    /// <para>
    /// The map's angles with the pitch key folded in, which is stock's
    /// arrangement and is unusual enough to be worth stating: `pitch` overrides
    /// the pitch component of `angles` and is NEGATED doing it, so a mapper who
    /// writes pitch 90 gets a light aimed at the floor.
    /// </para>
    /// </summary>
    private Vector Aim()
    {
        Vector angles = Entities.GetAngles(Handle);

        return Vars.Pitch != 0f ? new Vector(-Vars.Pitch, angles.Y, angles.Z) : angles;
    }

    /// <summary>
    /// Follows its target.
    ///
    /// <para>
    /// Re-resolved by NAME every tenth of a second rather than latched once,
    /// which is stock — and it is what lets a light track something that is
    /// destroyed and replaced. The cost is a name lookup at 10Hz per light,
    /// which is why the think only runs for a light that has a target at all.
    /// </para>
    /// </summary>
    public override void OnThink()
    {
        SetNextThink(0.1f);

        var target = Entities.FindByName(Vars.Target);

        if (!Entities.IsValid(target))
            return;

        Vector origin = Origin;
        Vector toTarget = Entities.GetOrigin(target) - origin;

        if (toTarget.Length < 0.001f)
            return;

        Vector aim = Angles.FromVector(toTarget);

        // Both the entity and the networked direction, because the two answer
        // different questions: the entity's angles are what a mapper and the
        // dev API read back, and the networked vector is what the client aims
        // the cone along.
        Entities.SetAngles(Handle, aim);
        State.Direction = Angles.Forward(aim);
        State.Origin    = origin;
    }

    [ScriptedEvent("TurnOn")]
    private void OnTurnOn()
    {
        _on = true;
        State.Flags = _mapFlags;
    }

    /// <summary>
    /// Off is "illuminates neither the world nor models", which is stock. The
    /// light still exists and still costs a slot; what it does not do is light
    /// anything.
    /// </summary>
    [ScriptedEvent("TurnOff")]
    private void OnTurnOff()
    {
        _on = false;
        // Both bits: illuminating neither is what "off" means here. No cast --
        // the NetField is the enum itself since plan Step 11.
        State.Flags = DynamicLightFlags.NoWorldIllumination
                    | DynamicLightFlags.NoModelIllumination;
    }

    [ScriptedEvent("Toggle")]
    private void OnToggle()
    {
        if (_on) OnTurnOff(); else OnTurnOn();
    }

    /// <summary>
    /// Clamps the brightness exponent, saying so.
    ///
    /// <para>
    /// Public and static because it is the one piece of arithmetic here and it
    /// has a boundary a map can cross: the exponent travels as a signed byte,
    /// and a mapper's 200 would arrive as something dark rather than as
    /// something very bright.
    /// </para>
    /// </summary>
    public static int ClampExponent(int exponent) => Math.Clamp(exponent, MinExponent, MaxExponent);
}

/// <summary>
/// The map keys <see cref="LightDynamic"/> declares.
/// </summary>
[ScriptVars]
public partial class LightDynamicVars
{
    public float Distance { get; set; }

    public int Brightness { get; set; }

    [VarName("_inner_cone")]
    public float InnerCone { get; set; }

    [VarName("_cone")]
    public float Cone { get; set; }

    [VarName("spotlight_radius")]
    public float SpotRadius { get; set; }

    public int Style { get; set; }

    /// <summary>What to aim at, by targetname. Re-resolved on every think.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// The mapper's pitch, which for this entity is NOT decorative — unlike the
    /// baked lights, this one's angles are what the cone points along. Stock
    /// negates it, and the sign matters: a positive pitch key aims a light
    /// downward.
    /// </summary>
    public float Pitch { get; set; }
}
