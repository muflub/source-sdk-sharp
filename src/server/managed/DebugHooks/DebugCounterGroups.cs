using SourceSharp.Abi;
using Debug.Api.Server.Interfaces;

namespace SourceSharp.Server;

/// <summary>
/// The counter groups this composition serves its dev API. Each group is a
/// name, the console readout that prints the same numbers, and a read that
/// answers null when its source is absent.
/// </summary>
public sealed class DebugCounterGroups(
    IReadOnlyList<(string Name, string Readout, Func<IReadOnlyList<DebugCounter>?> Read)> groups) : IDebugCounterGroups
{
    public IReadOnlyList<string> Names { get; } = groups.Select(g => g.Name).ToArray();

    public DebugCounterGroup? Read(string name)
    {
        foreach (var (groupName, readout, read) in groups)
        {
            if (!string.Equals(groupName, name, StringComparison.OrdinalIgnoreCase))
                continue;

            var counters = read();
            return new DebugCounterGroup(groupName, readout, counters is not null, counters ?? []);
        }

        return null;
    }

    /// <summary>
    /// The think heap's bookings, counted by the booked entity's classname and
    /// context (<c>classname</c> or <c>classname:context</c>), and their
    /// total: which thinkers a count is made of, so a booking a reload lost can
    /// be named rather than inferred from a total one short.
    /// </summary>
    /// <remarks>
    /// <c>dueWithin.N</c> counts the bookings due in the next N seconds of game
    /// (1, 2, 5, 10, 30): a booking that comes due between two reads runs and
    /// ends, which is the schedule working, so a reader comparing two counts
    /// taken T seconds apart subtracts what was due within T.
    /// </remarks>
    public static IReadOnlyList<DebugCounter> Thinks(IEnumerable<ScheduledThink> bookings, Func<EntityHandle, string> className,
                                                     float now)
    {
        var byKey = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var due1ByKey = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var dueMsByKey = new SortedDictionary<string, List<long>>(StringComparer.Ordinal);
        int[] windows = [1, 2, 5, 10, 30];
        var due = new long[windows.Length];
        long total = 0;
        foreach (var booking in bookings)
        {
            string key = booking.Context is null ? className(booking.Entity) : $"{className(booking.Entity)}:{booking.Context}";
            byKey[key] = byKey.GetValueOrDefault(key) + 1;
            if (!dueMsByKey.TryGetValue(key, out var times))
                dueMsByKey[key] = times = [];
            times.Add(Math.Max(0L, (long)MathF.Round((booking.Time - now) * 1000f)));
            if (booking.Time - now <= windows[0])
                due1ByKey[key] = due1ByKey.GetValueOrDefault(key) + 1;
            total++;
            for (int i = 0; i < windows.Length; i++)
            {
                if (booking.Time - now <= windows[i])
                    due[i]++;
            }
        }

        var counters = new List<DebugCounter> { new("total", total) };
        for (int i = 0; i < windows.Length; i++)
            counters.Add(new DebugCounter($"dueWithin.{windows[i]}", due[i]));
        counters.AddRange(byKey.Select(kv => new DebugCounter("booked." + kv.Key, kv.Value)));
        // A class with a booking due within 1s sits in two reads a second
        // apart, so a reader can call its disappearance lost rather than
        // subtract it as a one-shot that ran in between.
        counters.AddRange(due1ByKey.Select(kv => new DebugCounter("due1." + kv.Key, kv.Value)));
        // And when each booking comes due, so a reader can tell a booking due
        // inside the span between two reads (it may have run and gone) from one
        // due after it (it must still be there): the hot-reload step's
        // think_survival.
        foreach (var (key, times) in dueMsByKey)
        {
            times.Sort();
            for (int i = 0; i < times.Count; i++)
                counters.Add(new DebugCounter($"dueMs.{key}.{i}", times[i]));
        }
        return counters;
    }

    /// <summary>
    /// What <c>cs_sv_playershadow</c> prints, in its order: the switch and the
    /// managed bodies' counters (plan_physics_bones_native_zero 2.5), then the
    /// two native keeps -- live, and outlived their environment. Null when
    /// native answered nothing (no engine, or a side with no such counters).
    /// </summary>
    public static IReadOnlyList<DebugCounter>? PlayerShadow(PlayerShadowBodies bodies, IEngineDiagnostics diagnostics)
    {
        Span<int> native = stackalloc int[2];
        int read = diagnostics.PlayerShadowCounts(native);
        if (read <= 0)
            return null;

        return
        [
            new("enabled", bodies.Enabled ? 1 : 0),
            new("built", bodies.Built),
            new("updates", bodies.Updates),
            new("contacts", bodies.Contacts),
            new("touches", bodies.Touches),
            new("touchOverflows", bodies.TouchOverflows),
            new("snaps", bodies.Snaps),
            new("live", native[0]),
            new("outlived", read > 1 ? native[1] : 0),
        ];
    }

    /// <summary>
    /// What <c>cs_sv_physics_events</c> prints, as numbers, from the same
    /// objects: the drain's totals, every event kind it has seen (by name,
    /// zeroes included, so a consumer can index one unconditionally), and what
    /// the response and the router did with them.
    /// </summary>
    /// <summary>
    /// Managed's IPhysicsCollisionSolver (plan_physics_bones_native_zero 2.1):
    /// what vphysics asked and what it was told, by slot. cs_sv_physics_solver
    /// prints these.
    /// </summary>
    public static IReadOnlyList<DebugCounter> Solver(PhysicsSolver solver)
    {
        var counters = new List<DebugCounter>
        {
            new("collideAsks", solver.CollideAsks),
            new("collideRefusals", solver.CollideRefusals),
            new("collideUnowned", solver.CollideUnowned),
        };

        for (var why = CollisionRefusal.Allowed + 1; why < CollisionRefusal.Count; why++)
            counters.Add(new($"refused.{why}", solver.Refusals(why)));

        counters.AddRange(
        [
            new("disabledPairs", solver.Pairs.Count),
            new("penetrationAsks", solver.PenetrationAsks),
            new("penetrationPaused", solver.PenetrationPaused),
            new("penetrationPastGuard", solver.PenetrationPastGuard),
            new("penetrationPairs", solver.Penetration.Count),
            new("freezeAsks", solver.FreezeAsks),
            new("freezeCrushes", solver.FreezeCrushes),
            new("largeFrictionSystems", solver.LargeFrictionSystems),
            new("budgetAsks", solver.BudgetAsks),
            new("budgetRefusals", solver.BudgetRefusals),
            new("worstChecksDone", solver.WorstChecksDone),
        ]);

        return counters;
    }

    public static IReadOnlyList<DebugCounter> Physics(PhysicsCollisionResponse response, PhysicsEventRouter router)
    {
        var counters = new List<DebugCounter>
        {
            new("events", response.Events),
        };

        foreach (PhysicsEventKind kind in Enum.GetValues<PhysicsEventKind>())
            counters.Add(new($"seen.{kind}", response.SeenOfKind(kind)));

        counters.AddRange(
        [
            new("responded", response.Responded),
            new("updatesDelivered", router.UpdatesDelivered),
            new("screenShakes", response.Shakes),
            new("dustPuffs", response.Dusts),
            new("impactSounds", response.SoundsQueued),
            new("scrapesStarted", router.Friction.Started),
            new("scrapesPlaying", router.Friction.Active),
            new("stressFelt", router.StressFelt),
            new("stressCrushes", router.Impacts.StressCrushes),
            new("shadowMoves", router.Shadows.Moves),
            new("shadowImpulses", router.Shadows.Impulses),
            new("entitySolversCreated", router.Solver.Created),
            new("entitySolversActive", router.Solver.Active),
            new("postedByManaged", router.Posted),
        ]);

        return counters;
    }
}
