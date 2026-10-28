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
/// A model that is solid and can animate, but never moves on its own. The
/// scenery of a map: machinery, signage, doors that are only decoration.
///
/// The difference from prop_physics is not what it looks like but what the
/// engine does with it — this is collided with and never simulated, which is
/// what makes a map full of them affordable.
/// </summary>
[ScriptedEntity(EntityNames.PropDynamic, ScriptedEntityKind.Prop)]
// The same entity under the names the editor uses for it.
[ScriptedEntity(EntityNames.DynamicProp, ScriptedEntityKind.Prop)]
[ScriptedEntity(EntityNames.PropDynamicOverride, ScriptedEntityKind.Prop)]
public sealed class PropDynamic : ScriptedEntity
{
    public PropDynamic(IEnginePrecache precache, IEngineEntityRender render, IServerPropData propData)
    {
        Precache = precache;
        Render = render;
        PropDataTable = propData;
    }

    private readonly IEnginePrecache Precache;
    private readonly IEngineEntityRender Render;
    private readonly IServerPropData PropDataTable;

    public readonly Output OnAnimationBegun = new();
    public readonly Output OnAnimationDone = new();

    /// <summary>What a map may set on this entity. See <see cref="PropDynamicVars"/>.</summary>
    [ScriptVars] public PropDynamicVars Vars = new();

    private bool _visible = true;

    /// <summary>SF_DYNAMICPROP_NO_VPHYSICS.</summary>
    private const int NoVPhysicsFlag = 128;

    /// <summary>SF_DYNAMICPROP_DISABLE_COLLISION.</summary>
    private const int DisableCollisionFlag = 256;

    /// <summary>CBaseProp's MOVETYPE_PUSH (stock's ss_sandbox census measures 7).</summary>
    public override bool MovesAsPusher => true;

    /// <summary>CreateVPhysics makes a static object or none; never a simulated one.</summary>
    public override bool SpawnsPhysics => false;

    public override void OnSpawn()
    {
        // Condense classname's to one, except for "prop_dynamic_override" (CDynamicProp::Spawn).
        if (string.Equals(ClassName, EntityNames.DynamicProp, StringComparison.OrdinalIgnoreCase))
            SetClassName(EntityNames.PropDynamic);

        if (DynamicPropSpawn.Refusal(ClassName, Vars.Model, Precache, Render, PropDataTable) is { } refusal)
        {
            Log.Msg(DynamicPropSpawn.Line(ClassName, SpawnOrigin, refusal));
            Remove();
            return;
        }

        SetModel(Vars.Model);

        // The map's solid key (SOLID_NONE when it writes none). A prop_dynamic
        // left without one is an oriented box nothing collides with, "so the
        // bounding box surrounds the prop as it rotates"; the override and the
        // classes derived from it keep SOLID_NONE (CDynamicProp::Spawn).
        SolidType solid = (SolidType)Vars.Solid;
        SolidFlags flags = SolidFlags.None;
        if (solid == SolidType.None && ClassName is EntityNames.PropDynamic)
        {
            solid = SolidType.OrientedBox;
            flags = SolidFlags.NotSolid;
        }

        // CreateVPhysics: a static object (VPhysicsInitStatic) unless it is
        // SOLID_NONE, or not solid and asked for none. The object keeps the
        // solid type: a brush's SetSolidity would make it SOLID_BSP, which a
        // studio model is not traced as. The collision spawnflag comes after.
        bool noObject = (flags & SolidFlags.NotSolid) != 0 && (SpawnFlags & NoVPhysicsFlag) != 0;

        if ((SpawnFlags & DisableCollisionFlag) != 0)
            flags |= SolidFlags.NotSolid;

        SetSolidity(solid, flags);

        if (solid != SolidType.None && !noObject)
            InitStaticPhysics();

        // Now condense all classnames to one: the override too, once the
        // spawn has read it (CDynamicProp::Spawn after BaseClass::Spawn).
        if (string.Equals(ClassName, EntityNames.PropDynamicOverride, StringComparison.OrdinalIgnoreCase))
            SetClassName(EntityNames.PropDynamic);

        _visible = !Vars.StartDisabled;
        Apply();

        if (Vars.DefaultAnim.Length > 0)
            Play(Vars.DefaultAnim);
    }

    [ScriptedEvent] private void OnTurnOn() { _visible = true; Apply(); }
    [ScriptedEvent] private void OnTurnOff() { _visible = false; Apply(); }
    [ScriptedEvent] private void OnEnable() { _visible = true; Apply(); }
    [ScriptedEvent] private void OnDisable() { _visible = false; Apply(); }

    [ScriptedEvent] private void OnEnableCollision() => SetNotSolid(false);
    [ScriptedEvent] private void OnDisableCollision() => SetNotSolid(true);

    [ScriptedEvent] private void OnSetAnimation(string animation) => Play(animation);

    [ScriptedEvent] private void OnSetDefaultAnimation(string animation) => Vars.DefaultAnim = animation;

    //-------------------------------------------------------------------------
    // A named animation the model does not have is worth saying out loud. The
    // symptom otherwise is scenery frozen in its first frame, which looks like
    // the entity failing rather than the name being wrong.
    //-------------------------------------------------------------------------
    private void Play(string animation)
    {
        if (animation.Length == 0)
            return;

        if (PlaySequence(animation))
        {
            OnAnimationBegun.Fire();
            return;
        }

        Log.Warning($"[SourceSharp] {ClassName} '{Name}': model has no animation '{animation}'\n");
    }

    /// <summary>
    /// TurnOn and TurnOff: EF_NODRAW alone. Stock's hidden prop_dynamic is
    /// still solid; DisableCollision is the input that lets things through.
    /// </summary>
    private void Apply() => SetVisible(_visible);
}

/// <summary>
/// The map keys <see cref="PropDynamic"/> declares.
/// </summary>
[ScriptVars]
public partial class PropDynamicVars
{
    public string Model { get; set; } = string.Empty;

    /// <summary>Animation to play on spawn and return to when another finishes.</summary>
    public string DefaultAnim { get; set; } = string.Empty;

    public bool StartDisabled { get; set; }

    /// <summary>The map's solid type: 0 none (the default), 2 a box, 6 the model's collision.</summary>
    public int Solid { get; set; }
}
