using SourceSharp.Abi;

namespace TF2.Descent.Shared;

/// <summary>
/// What every client knows of a hero's corpse (plan_descent_logic WO5; death
/// plan M4, Q3: everyone sees bodies, only the owner loots one): whose it is
/// and how many items it holds, on the one <c>tf2_descent_corpse</c> entity a
/// hero, which stands where he died. The owner's client reads where his
/// corpse is from the entity's origin (the automap's corpse marker).
/// </summary>
[StandaloneNetState(ClassName)]
public partial class CorpseNetState
{
    /// <summary>The entity's classname (plan section 3's <c>tf2_descent_*</c>).</summary>
    public const string ClassName = "tf2_descent_corpse";

    /// <summary>The owner's player index (1..64); 0 for a corpse whose owner left the level.</summary>
    [NetField(Bits = 7)] public partial byte OwnerIndex { get; set; }

    /// <summary>The items on it, as the ledger counts them; -1 when the ledger cannot say (hosted).</summary>
    public partial short Items { get; set; }

    /// <summary>A hardcore hero's corpse: lootable by nobody (D7).</summary>
    public partial bool Hardcore { get; set; }
}
