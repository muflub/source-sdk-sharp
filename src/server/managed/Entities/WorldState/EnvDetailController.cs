using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_detail_controller: how close the map's detail props give up.
///
/// <para>
/// The smallest entity in this family after <c>water_lod_control</c> — two
/// distances, no inputs, no behaviour, stock's included. A mapper who scattered
/// grass over everything says "and stop drawing it sooner than the player's
/// settings would", which is a per-map fact and therefore an entity rather
/// than a convar.
/// </para>
///
/// <para>
/// Always transmitted for the family's shared reason: a setting every client
/// needs, sitting wherever the mapper happened to drop it.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvDetailController, ScriptedEntityKind.Effect)]
public sealed class EnvDetailController : ScriptedEntity
{
    public EnvDetailController(IEngineEntityRender render)
    {
        Render = render;
    }

    /// <summary>what this entity looks like.</summary>
    private readonly IEngineEntityRender Render;

    [NetState] public DetailControllerNetState State = new();

    /// <summary>What a map may set on this entity. See <see cref="EnvDetailControllerVars"/>.</summary>
    [ScriptVars] public EnvDetailControllerVars Vars = new();

    public override void OnSpawn()
    {
        Render.SetTransmitAlways(Handle, true);

        State.FadeStartDist = Vars.FadeStart;
        State.FadeEndDist   = Vars.FadeEnd;
    }
}

/// <summary>
/// The map keys <see cref="EnvDetailController"/> declares — Hammer's fade
/// pair, under the names the stock FGD gives them.
///
/// <para>
/// The defaults are the client convars' own (cl_detailfade 400, cl_detaildist
/// 1200), which makes a KEYLESS controller a no-op. Stock defaults both to
/// zero, and since the client clamps DOWN to the controller's values, a stock
/// map that places one and sets nothing hides every detail prop it has — an
/// accident wearing an entity's name, and not one worth keeping.
/// </para>
/// </summary>
[ScriptVars]
public partial class EnvDetailControllerVars
{
    [VarName("fademindist")]
    public float FadeStart { get; set; } = 400f;

    [VarName("fademaxdist")]
    public float FadeEnd { get; set; } = 1200f;
}
