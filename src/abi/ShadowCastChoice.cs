namespace SourceSharp.Abi;

/// <summary>
/// What a managed class asked for about its shadow. Mirrors
/// StandaloneShadowChoice_t in shadow_cast_policy.h.
/// </summary>
public enum ShadowCastChoice
{
    /// <summary>The stock rule, from the model. What everything starts as.</summary>
    Auto = 0,

    /// <summary>
    /// Casts nothing, whatever it is wearing. EF_NOSHADOW's meaning, and what
    /// prop_dynamic's <c>disableshadows</c> keyvalue asks for.
    /// </summary>
    Never = 1,

    /// <summary>
    /// The cheap blob, for something the auto rule would refuse. An entity that
    /// draws ITSELF has no studio header to be judged by and would otherwise
    /// never cast at all.
    /// </summary>
    Simple = 2,
}
