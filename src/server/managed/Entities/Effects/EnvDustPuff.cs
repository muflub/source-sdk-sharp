using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_dustpuff: a puff of dust, thrown the way the entity points.
///
/// <para>
/// One temp entity per input, with three keys in front of it. The only thing
/// worth saying about it is what is NOT here: the "color" key.
/// </para>
///
/// <para>
/// Stock's env_dustpuff parses a colour and then never sends it — the datadesc
/// has the keyfield, the effect call has no parameter for it, and the field is
/// read by nothing. A mapper who sets it gets grey dust. That is left exactly as
/// it is: the field is declared here so the key is still CLAIMED (an unclaimed
/// key is reported as a mapper's typo, and this one is not a typo), and it is
/// still not sent. Recorded in docs/divergences.md under what stock gets wrong.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvDustpuff)]
public sealed class EnvDustPuff : ScriptedEntity
{
    public EnvDustPuff(IEngineEffects effects)
    {
        Effects = effects;
    }

    /// <summary>the small effects.</summary>
    private readonly IEngineEffects Effects;
    /// <summary>What a map may set on this entity. See <see cref="EnvDustPuffVars"/>.</summary>
    [ScriptVars] public EnvDustPuffVars Vars = new();

    /// <summary>Test-visible: the keys as the entity actually holds them.</summary>
    internal float Scale => Vars.Scale;

    internal float Speed => Vars.Speed;

    /// <summary>Claimed, never sent. See the note on the class.</summary>
    internal string ColourKey => Vars.Color;

    [ScriptedEvent("SpawnDust")]
    private void OnSpawnDust()
    {
        // The CURRENT angles rather than the spawn angles. Stock reads
        // GetAbsAngles, so a dust puff parented to something that turns throws
        // its dust the way the parent is facing now — and EntityGetSpawnAngles
        // answers zero for a point entity anyway, which is the trap
        // env_physimpact fell into and plan_migrate.md records.
        Effects.DustEffect(Origin, Angles.Forward(Entities.GetAngles(Handle)),
                          Vars.Scale, Vars.Speed);
    }
}

/// <summary>
/// The map keys <see cref="EnvDustPuff"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvDustPuffVars
{
    /// <summary>How big.</summary>
    public float Scale { get; set; }

    /// <summary>
    /// How fast it leaves. The map's key is "speed", which is CBaseEntity's own
    /// field in stock — hence the name here rather than something clearer.
    /// </summary>
    public float Speed { get; set; }

    /// <summary>
    /// Claimed and unused, exactly as stock. See the note on the class: this is
    /// here so the key is not reported as unknown, not because anything reads
    /// it.
    /// </summary>
    public string Color { get; set; } = string.Empty;
}
