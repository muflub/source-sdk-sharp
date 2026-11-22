using SourceSharp.Ai;

namespace TF2.Descent.Ai.Director;

/// <summary>
/// THE SEAM THE SPAWN DIRECTOR ASKS A HULL'S FLOOR THROUGH (plan_descent_logic HO9; plan_tf2_rpg 5d):
/// whether a robot of a scale, standing at a place, is on the floor its hull shares with the rest of
/// the level. A level ships no such thing: the server answers over the level's navigation graph
/// (the class the scale plans by, its cell there, and whether that cell joins the arrival's), and a
/// fact answers its own. A room's doors can fit a hull while the floor beyond them does not
/// (depth 1 seed 1's widedrift rooms split a giant's floor), so the doors alone cannot say.
/// </summary>
public interface IHullReach
{
    /// <summary>
    /// Whether a robot of <paramref name="scale"/> standing at <paramref name="at"/>, in room
    /// <paramref name="room"/>, stands there and can walk from there to the level's arrival (so to
    /// any hero who comes for it).
    /// </summary>
    bool Joins(int room, AiVector at, double scale);
}
