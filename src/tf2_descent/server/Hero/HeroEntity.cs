using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Shared;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// <c>tf2_descent_hero</c>: one per hero, carrying <see cref="HeroNetState"/>
/// to every client (plan_descent_logic HE2, section 2: one Descent state
/// entity a hero). Created and removed by <see cref="HeroComponent"/>, never
/// placed by a map. Effect kind (no model, no collision) and ALWAYS
/// TRANSMITTED, as TF2's <c>tf_player_data</c> is: party frames need every
/// hero's state wherever the entity stands.
/// </summary>
[ScriptedEntity(HeroNetState.ClassName, ScriptedEntityKind.Effect)]
public sealed class HeroEntity(IEngineEntityRender render) : ScriptedEntity
{
    /// <summary>The hero's networked state.</summary>
    [NetState] public HeroNetState State = new();

    public override void OnSpawn() => render.SetTransmitAlways(Handle, true);
}
