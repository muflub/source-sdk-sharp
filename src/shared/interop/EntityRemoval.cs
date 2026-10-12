using System.Runtime.Loader;

namespace SourceSharp.Abi;

/// <summary>
/// What one call to <see cref="EntityDirectory.Remove"/> did —
/// <c>plan_entity.md</c> step 2c.
///
/// <para>
/// <b>THREE ANSWERS AND NOT A BOOL, because the two things a bool would run
/// together are the two things this step is measured on.</b> "I marked it" and
/// "it was already marked" are both success and they are not the same event: a
/// double remove has to be idempotent (the second call must not queue a second
/// free) and it must not be mistaken for a removal that found nothing. A bool
/// forces a caller to pick which of those it means, and every caller picks a
/// different one.
/// </para>
///
/// <para>
/// <b><see cref="NotMine"/> IS NOT AN ERROR.</b> The directory answers it for a
/// handle it has never bound — a stock <c>CBaseEntity</c>, the world, a stale
/// handle whose entry has been rebound. <see cref="EngineSide.RemoveEntity"/> reads
/// it as "cross to native and let <c>UTIL_Remove</c> have it", which is what
/// keeps the SDK hierarchy working while it still exists.
/// </para>
/// </summary>
public enum RemovalMark
{
    /// <summary>
    /// The directory does not hold this handle. Nothing was marked and nothing
    /// will be freed; the caller owns what happens next.
    /// </summary>
    NotMine = 0,

    /// <summary>Marked by this call. It will be freed at the end of the frame.</summary>
    Marked = 1,

    /// <summary>
    /// Already marked, by an earlier call in this frame. Nothing changed —
    /// <b>this is what "a double remove is idempotent" means</b>, and it is a
    /// distinct answer so a test can tell it from both of the others.
    /// </summary>
    AlreadyMarked = 2,
}

/// <summary>
/// WHERE A REMOVAL GOES — the route from the engine surface to the side's own
/// <see cref="EntityDirectory"/>, <c>plan_entity.md</c> step 2c.
///
/// <para>
/// <b>Why this exists at all.</b> <see cref="EngineSide.RemoveEntity"/> reaches
/// an <see cref="EngineSide"/>, and the directory is an INSTANCE, one per side
/// module (<c>plan.md</c> §1a). Before step 2c the call went straight out
/// through the <c>EntityRemove</c> ABI slot into <c>CStandaloneEntity::Remove</c>,
/// which appended to a native list. §3.5 puts the mark in the directory, so the
/// engine side needs a way to reach one.
/// </para>
///
/// <para>
/// <b>AN INSTANCE, ONE PER ENGINE SIDE</b> (plan_abi_hotreload D7), for
/// <see cref="ShellLessRoutes"/>' reasons: it was a static class, so two game
/// instances in one process would have marked removals in one directory, and a
/// listen server's CLIENT removal had to be kept out of the server's directory
/// by a side check on every call. Each side's route is its own now, and only
/// the server's registry binds one.
/// </para>
///
/// <para>
/// <b>An unbound route answers false, and false means "cross".</b> That
/// direction is the safe one: a missed route costs a crossing and the native
/// deferred list still frees the entity. The other direction would silently
/// drop removals.
/// </para>
/// </summary>
public sealed class EntityRemovalRoutes
{
    /// <summary>Marks one entity in the side's directory. Null when no registry has bound one.</summary>
    public Func<EntityHandle, RemovalMark>? Mark { get; private set; }

    /// <summary>
    /// Frees what is marked, and answers how many. Null when no registry has
    /// bound one.
    ///
    /// <para>
    /// <b>The frame does not come through here</b> — it calls
    /// <c>EntityDirectory.FreeRemoved</c> directly, at the one position where
    /// the order around the thinks and the parenting sweep is load-bearing.
    /// This is the route for the OTHER drains, which are stock's own
    /// <c>CleanupDeleteList</c> points.
    /// </para>
    /// </summary>
    public Func<int>? Free { get; private set; }

    /// <summary>Binds both routes. The registry's constructor is the one caller.</summary>
    public void Bind(Func<EntityHandle, RemovalMark> mark, Func<int> free)
    {
        Mark = mark;
        Free = free;
    }

    /// <summary>Drops the routes.</summary>
    public void Unbind()
    {
        Mark = null;
        Free = null;
    }

    /// <summary>
    /// Drops the routes if they belong to a side that is unloading, and answers
    /// how many went. <inheritdoc cref="ShellLessRoutes.Forget" path="/remarks/para[1]"/>
    /// </summary>
    internal int Forget(AssemblyLoadContext going)
    {
        int dropped = 0;

        if (ManagedCaches.CameFrom(Mark, going))
        {
            Mark = null;
            dropped++;
        }

        if (ManagedCaches.CameFrom(Free, going))
        {
            Free = null;
            dropped++;
        }

        return dropped;
    }

    /// <summary>
    /// Frees whatever is marked, now. Answers how many; 0 with no route bound,
    /// which is the same answer as "nothing was waiting" and is correct for
    /// both.
    /// </summary>
    public int FreeNow() => Free is not null ? Free() : 0;

    /// <summary>
    /// Asks the bound directory to mark <paramref name="entity"/>, and answers
    /// whether the directory CLAIMED it.
    ///
    /// <para>
    /// <b>Claimed is not the same as newly marked</b>, and reading it as such is
    /// the bug this method exists to make impossible: a second remove in the same
    /// frame answers <see cref="RemovalMark.AlreadyMarked"/>, which is still
    /// claimed — the entity is going, and falling through to native
    /// <c>UTIL_Remove</c> for it would remove it twice through two different
    /// mechanisms.
    /// </para>
    /// </summary>
    public bool Remove(EntityHandle entity)
        => Mark is not null && Mark(entity) != RemovalMark.NotMine;
}
