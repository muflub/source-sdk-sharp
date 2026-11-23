using SourceSharp.Ai;

using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Director;

namespace TF2.Descent.Server.Horde.Nav;

/// <summary>
/// A hull's floor as the level's navigation holds it (<see cref="IHullReach"/>, plan_descent_logic
/// HO9): a robot larger than the game's own body (scale over 1, an extra hull's class,
/// <see cref="HordeHulls.ClassOf"/>) joins the level where its class has a cell under it on the
/// class's main floor (<see cref="HordeNavData.OnMainFloor"/>). A robot of scale 1 or less plans as
/// the heroes' own body, whose floor is the heroes', and is never refused here.
/// </summary>
public sealed class NavHullReach(HordeNavData nav) : IHullReach
{
    public bool Joins(int room, AiVector at, double scale)
    {
        ClearanceClass cls = HordeHulls.ClassOf((float)scale);
        if (cls == ClearanceClass.Stand)
            return true;
        return nav.OnMainFloor(nav.CellAt(at, cls), cls);
    }
}
