using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// water_lod_control: the two distances at which water stops being expensive.
///
/// <para>
/// The smallest entity in this family — two floats, read by the view renderer
/// every frame. There is no behaviour and stock has none either; what a map
/// gets out of it is the ability to say "this water is cheap sooner", and to
/// move the line at runtime through two inputs.
/// </para>
///
/// <para>
/// Always transmitted for the same reason <c>shadow_control</c> is: a setting
/// every client needs, sitting wherever the mapper happened to drop it.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.WaterLodControl, ScriptedEntityKind.Effect)]
public sealed class WaterLodControl : ScriptedEntity
{
    public WaterLodControl(IEngineEntityRender render)
    {
        Render = render;
    }

    /// <summary>what this entity looks like.</summary>
    private readonly IEngineEntityRender Render;
    [NetState] public WaterLodControlNetState State = new();

    /// <summary>What a map may set on this entity. See <see cref="WaterLodControlVars"/>.</summary>
    [ScriptVars] public WaterLodControlVars Vars = new();

    public override void OnSpawn()
    {
        Render.SetTransmitAlways(Handle, true);

        State.CheapStart = Vars.Start;
        State.CheapEnd   = Vars.End;
    }

    [ScriptedEvent("SetCheapWaterStartDistance")]
    private void OnSetStart(float value) => State.CheapStart = value;

    [ScriptedEvent("SetCheapWaterEndDistance")]
    private void OnSetEnd(float value) => State.CheapEnd = value;
}

/// <summary>
/// The map keys <see cref="WaterLodControl"/> declares.
/// </summary>
[ScriptVars]
public partial class WaterLodControlVars
{
    [VarName("cheapwaterstartdistance")]
    public float Start { get; set; } = 1000f;

    [VarName("cheapwaterenddistance")]
    public float End { get; set; } = 2000f;
}
