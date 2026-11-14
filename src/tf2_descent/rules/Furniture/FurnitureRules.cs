using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Furniture;

/// <summary>
/// The dungeon furniture's rules (plan_tf2_rpg 2l, R24; plan_descent_logic RU11), one per
/// kind, over furniture.json: placing it at load, shrines, containers, the Dispenser,
/// explosives and traps.
/// </summary>
public sealed class FurnitureRules
{
    public FurnitureRules(DescentContent content)
    {
        Placement = new FurnishingRules(content);
        Shrines = new ShrineRules(content);
        Containers = new ContainerRules(content);
        Dispensers = new DispenserRules(content);
        Explosives = new ExplosiveRules(content);
        Traps = new TrapRules(content);
    }

    public FurnishingRules Placement { get; }
    public ShrineRules Shrines { get; }
    public ContainerRules Containers { get; }
    public DispenserRules Dispensers { get; }
    public ExplosiveRules Explosives { get; }
    public TrapRules Traps { get; }
}
