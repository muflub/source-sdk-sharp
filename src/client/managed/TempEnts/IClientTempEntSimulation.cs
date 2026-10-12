using SourceSharp.Abi;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// One pooled temp entity's physics step — <c>C_LocalTempEntity::Frame</c>.
///
/// <para>
/// A SEAM RATHER THAN A METHOD ON THE POOL, and the split is the same one
/// <see cref="TempEntSpawns"/> is: the pool is slots, eviction and expiry, and
/// this is what a slot's contents DO for a frame. They are asked about by
/// different tests — the pool's are about the list surviving a re-entrant
/// spawn, and these are about a gib bouncing — and a pool test that had to set
/// up a velocity and a bounce factor to check its free list would be testing
/// two things at once.
/// </para>
///
/// <para>
/// It also keeps the pool's own tests honest. Before this, the pool's tests
/// drove <c>TempEntPoolSimulate</c> through the engine double, so "the loop
/// stepped this slot on frame 17" was observable. With the physics in C# the
/// engine double no longer sees it, and without a seam those assertions would
/// have had to be rewritten as assertions about a velocity — which is a pool
/// test that has quietly become a physics test.
/// </para>
/// </summary>
public interface IClientTempEntSimulation
{
    /// <summary>
    /// One tempent's frame. False means the tempent asked to die, which is
    /// stock's own return value: an animating sprite that ran off its last
    /// frame and is not set to loop.
    /// </summary>
    /// <param name="life">
    /// Read and WRITTEN. <c>Frame</c> mutates <c>flags</c> and <c>die</c> — a
    /// gib that lands clears <c>FTENT_ROTATE</c>, a <c>FTENT_COLLIDEKILL</c>
    /// one sets <c>die = curtime</c> — and the caller is what puts them back
    /// across the boundary, because it has already read the record and would
    /// otherwise read it twice.
    /// </param>
    /// <param name="frameNumber">
    /// Stock's <c>gTempEntFrame</c>, 0..31. Only <c>FTENT_FLICKER</c> reads it.
    /// </param>
    /// <param name="frameCount">
    /// A monotonic count of the frames the POOL has run, standing in for
    /// <c>gpGlobals-&gt;framecount</c>. Only the fast-collision rate limit reads
    /// it, and only differences of it ever matter.
    /// </param>
    bool Step(int slot, float frameTime, int frameNumber, int frameCount, ref TempEntLife life);

    /// <summary>
    /// This slot's tempent is gone. Anything remembered about it — the
    /// transform C# owns, and <c>Frame</c>'s three private working values — must
    /// go with it, because the slot is about to be handed to something else.
    /// </summary>
    void Forget(int slot);

    /// <summary>
    /// A new level. Whatever has already been said out loud is now stale; see
    /// <see cref="IClientTempEntLevel"/> for why once per level rather than
    /// once per process.
    /// </summary>
    void LevelInit();
}
