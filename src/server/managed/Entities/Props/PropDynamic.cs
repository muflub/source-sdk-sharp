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

    public override void OnSpawn()
    {
        if (DynamicPropSpawn.Refusal(ClassName, Vars.Model, Precache, Render, PropDataTable) is { } refusal)
        {
            Log.Msg(DynamicPropSpawn.Line(ClassName, SpawnOrigin, refusal));
            Remove();
            return;
        }

        SetModel(Vars.Model);

        // Solid but never simulated. Chosen here because the shell reads it when
        // it creates the physics object, moments later.
        MakePhysicsStatic();

        _visible = !Vars.StartDisabled;
        Apply();

        if (Vars.DefaultAnim.Length > 0)
            Play(Vars.DefaultAnim);
    }

    [ScriptedEvent] private void OnTurnOn() { _visible = true; Apply(); }
    [ScriptedEvent] private void OnTurnOff() { _visible = false; Apply(); }
    [ScriptedEvent] private void OnEnable() { _visible = true; Apply(); }
    [ScriptedEvent] private void OnDisable() { _visible = false; Apply(); }

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

    private void Apply()
    {
        SetVisible(_visible);

        // Hidden scenery should not be collided with either — a player walking
        // into an invisible wall is the classic symptom of forgetting this.
        SetSolidity(_visible ? BrushSolidity.Solid : BrushSolidity.None);
    }
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
}
