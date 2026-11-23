using SourceSharp.Abi;

// Compiled into both TF2.Descent.Server and TF2.Descent.Client (plan_side_isolation §2.4): a
// town stall's entity, a vendor NPC or a piece of furniture a hero uses (plan_descent_logic WO8).

namespace TF2.Descent.Shared;

/// <summary>
/// A <c>tf2_descent_vendor</c>'s networked state: which stall it is and the idle sequence its
/// model plays. Everyone is sent it: a vendor is no secret.
/// </summary>
[StandaloneNetState(ClassName)]
public partial class VendorNetState
{
    public const string ClassName = "tf2_descent_vendor";

    /// <summary>The stall's index in the town's stall table, + 1; 0 for none.</summary>
    public partial byte Stall { get; set; }

    /// <summary>The sequence it plays, looked up on the server; -1 for none (a prop that stands still).</summary>
    public partial short Sequence { get; set; }

    /// <summary>
    /// Whether <see cref="Sequence"/> plays once and holds its last frame (the resupply locker's
    /// doors opening, then closing) rather than looping as an idle does.
    /// </summary>
    public partial bool Once { get; set; }
}
