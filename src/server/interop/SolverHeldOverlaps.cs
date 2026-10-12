using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The pairs vphysics holds open: a physics trigger and what is inside it,
/// from IPhysicsCollisionEvent's ObjectEnterTrigger until ObjectLeaveTrigger
/// (plan_physics_managed B2.2). The managed half of native's g_SolverHeld.
///
/// <para>
/// The solver announces entry and exit once each and says nothing between,
/// while the engine renews an overlap every tick; so a held pair is REPORTED
/// EVERY FRAME, merged into the overlap report the touch diff reads, until
/// the solver says it ended. Dropped only at level shutdown, as native's list
/// is: vphysics reports the leave when an object goes.
/// </para>
/// </summary>
public sealed class SolverHeldOverlaps
{
    private readonly List<OverlapPair> _held = new();

    public int Count => _held.Count;

    public void Add(EntityHandle a, EntityHandle b)
    {
        var pair = new OverlapPair(a, b);
        if (!_held.Contains(pair))
            _held.Add(pair);
    }

    public void Remove(EntityHandle a, EntityHandle b) => _held.Remove(new OverlapPair(a, b));

    public void Clear() => _held.Clear();

    /// <summary>
    /// The engine's report (<paramref name="found"/> pairs in
    /// <paramref name="into"/>) with every held pair it lacks appended. Answers
    /// the total; past the span's length it is the size to grow to, as the
    /// native report answers.
    /// </summary>
    public int Merge(Span<OverlapPair> into, int found)
    {
        if (found < 0 || found > into.Length)
            return found < 0 ? found : found + _held.Count;

        int total = found;
        foreach (OverlapPair pair in _held)
        {
            if (into[..found].Contains(pair))
                continue;

            if (total < into.Length)
                into[total] = pair;
            total++;
        }

        return total;
    }
}
