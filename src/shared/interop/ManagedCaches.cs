using System.Runtime.Loader;

namespace SourceSharp.Abi;

/// <summary>
/// What the ABI remembers about a side's types, and how it forgets it.
///
/// Each side's ABI is loaded into that side's ROOT context and kept for the
/// process (plan_side_isolation P5), which is what keeps a contract type the
/// same across the side's reloads. The cost is that anything static there
/// outlives the game context it came from:
/// a cache keyed by a game Type holds that Type, a Type holds its assembly, and
/// an assembly holds the collectible context it was loaded into. One entry is
/// enough to keep a whole side alive forever, and the symptom is not an error —
/// it is a hot reload that silently leaks the old code and keeps running it.
///
/// So a side says so on the way out, and every cache here drops what belonged to
/// it. Selective rather than a clear: only what the going context put there is
/// dropped (ForgettingAnotherSideLeavesThisSidesRoutesAlone holds that).
/// </summary>
public static class ManagedCaches
{
    /// <summary>
    /// Drops everything cached against a type from <paramref name="going"/>,
    /// and, on each of <paramref name="sides"/>, the routes and answers that
    /// came from it. Returns how many entries went, which is worth logging.
    /// Zero is the ordinary answer: a composition's Shutdown detaches what it
    /// bound, and a count above zero is this net catching a Shutdown that did
    /// not get there.
    ///
    /// Call it after the module's Shutdown and before Unload — shutdown code is
    /// still entitled to ask the ABI about its own types.
    /// </summary>
    /// <param name="going">The context being unloaded.</param>
    /// <param name="sides">
    /// The engine sides whose routes to search (plan_abi_hotreload D8): handed
    /// in by whoever composed them, never looked up. A listen server passes
    /// both, since a client registry is the server's business too.
    /// </param>
    internal static int Forget(AssemblyLoadContext going, IReadOnlyList<EngineSide> sides)
    {
        ArgumentNullException.ThrowIfNull(sides);

        // The type caches that lived here (StandaloneNetStates, NetStateBinding,
        // SaveFieldCatalog) are each composition's own since statics to zero,
        // and go with the load that owns them.
        int forgotten = 0;

        // THE THREE ROUTES, ADDED BY LANE `loose-ends`, AND THEY WERE THE
        // LEAK: ScriptedEntityRegistry's constructor binds them to delegates
        // over its own instance, and nothing called their Unbind().
        // plan_abi_hotreload D7 made them each EngineSide's own instances, and
        // the server composition's Shutdown detaches its registry; this stays
        // as the net for a Shutdown that did not get there. The second pin,
        // the player's shadow switch (lane `defect-alc`), is gone with its
        // static: plan_abi_hotreload D7 hands the server's own convar to the
        // run instead.
        foreach (EngineSide side in sides)
            forgotten += ForgetRoutes(side, going);

        return forgotten;
    }

    /// <summary>
    /// One engine side's three routes: dropped whole when the registry bound
    /// into them belongs to the going side, and each delegate or answer that
    /// came from it otherwise. Answers how many went.
    /// </summary>
    internal static int ForgetRoutes(EngineSide side, AssemblyLoadContext going)
        => ForgetDeadRegistry(side, going)
         + side.ShellLess.Forget(going)
         + side.Removal.Forget(going)
         + side.EntityIo.Forget(going);

    /// <summary>
    /// A registry whose entity classes came from the unloading side is dead, and
    /// so are the THREE routes its constructor bound. Answers how many went.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ALL THREE TOGETHER, because one constructor binds all three —
    /// <c>ScriptedEntityRegistry</c>'s. Dropping them one at a time by asking
    /// whether each delegate came from the side is what the first fix did, and
    /// it dropped NONE of them: every one of those delegates is over a method
    /// of <c>EntityDirectory</c> or of the registry itself, both declared in
    /// THIS assembly. The side's own code is two references further down — the
    /// entity <see cref="Type"/>s in the registry and the two hierarchy
    /// delegates on the directory.
    /// </para>
    /// <para>
    /// SAFE ON A LISTEN SERVER: each side has its own routes, and only the
    /// server's registry binds any, so a client unload asks a server registry
    /// whether IT belongs to the client context, gets false, and drops nothing.
    /// </para>
    /// </remarks>
    private static int ForgetDeadRegistry(EngineSide side, AssemblyLoadContext going)
    {
        if (side.ShellLess.Answers is not IRouteBinder registry)
            return 0;

        if (!registry.BelongsTo(going))
            return 0;

        side.ShellLess.Unbind();
        side.Removal.Unbind();
        side.EntityIo.Unbind();
        return 3;
    }

    /// <summary>
    /// Whether a delegate belongs to a context, by its target's type or, for a
    /// static method, by the type declaring it.
    /// </summary>
    /// <remarks>
    /// THE TARGET FIRST. A route is bound to an instance method of the side's
    /// registry, so the target is the object that matters; the declaring type
    /// is the fallback that catches a static method and a lambda compiled into
    /// a display class. Checking only the declaring type would still be right
    /// here, and would stop being right the day a side binds a route to a
    /// delegate over a shared base declared in this assembly.
    /// </remarks>
    internal static bool CameFrom(Delegate? route, AssemblyLoadContext going)
    {
        if (route is null)
            return false;

        if (route.Target is not null && CameFrom(route.Target.GetType(), going))
            return true;

        return route.Method.DeclaringType is not null
            && CameFrom(route.Method.DeclaringType, going);
    }

    /// <summary>
    /// Whether a type came from a context, by way of the assembly declaring it.
    ///
    /// Generic arguments are not chased: nothing here is keyed by a constructed
    /// generic, and a cache that ever is should say so at its own key rather
    /// than have this guess.
    /// </summary>
    internal static bool CameFrom(Type type, AssemblyLoadContext going)
        => AssemblyLoadContext.GetLoadContext(type.Assembly) == going;
}

/// <summary>
/// What bound a side's three routes (ShellLess, Removal, EntityIo) in its
/// constructor: the server's <c>ScriptedEntityRegistry</c>, the one
/// implementer. <see cref="ManagedCaches"/> asks it whether it came from an
/// unloading context and, if so, drops all three together.
///
/// <para>
/// P1 run 3, the integrator's ruling (a): the reload net named the registry's
/// type, which tied the shared caches to the server's entity cluster. It now
/// names only this question.
/// </para>
///
/// <para>
/// PUBLIC because its implementer is server game code, which has no view of
/// this assembly's internals (EngineAreaTests.NothingOutsideTheAbiCanReachTheEngine).
/// It answers one question about a context and reaches nothing.
/// </para>
/// </summary>
public interface IRouteBinder
{
    /// <summary>Whether this binder's own code comes from <paramref name="going"/>.</summary>
    bool BelongsTo(AssemblyLoadContext going);
}
