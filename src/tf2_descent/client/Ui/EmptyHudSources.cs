using SourceSharp.Abi;

namespace TF2.Descent.Client.Ui;

//=============================================================================
// The HUD sources the wire cannot answer yet (plan_descent_logic UI2). Each
// answers "nothing to show", never sample data, and names what the server
// must send for it to go live; the package that sends it replaces the body.
//=============================================================================

/// <summary>
/// The boss bar (<see cref="IDescentBoss"/>), absent. Live when the boss body
/// (HO13) networks its name, life, maximum life and phase to the heroes on
/// its arena.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentBoss)])]
public sealed class BossSource : IDescentBoss
{
    public bool Present => false;

    public string Name => "";

    public int Life => 0;

    public int LifeMax => 0;

    public int PhaseCount => 0;

    public int Phase => 0;
}

