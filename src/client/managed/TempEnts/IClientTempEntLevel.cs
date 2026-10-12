namespace SourceSharp.Client.TempEnts;

/// <summary>
/// An <see cref="IClientTempEnt"/> that has something to forget when the level
/// changes.
///
/// <para>
/// IT EXISTS FOR ONE SENTENCE IN THE BEHAVIOUR LEDGER, and that is worth
/// stating rather than leaving it to look like general machinery. Part 1's
/// client rows each end at *"still open, and here is the stub that says so out
/// loud ONCE PER LEVEL"* — once, because a firefight fires hundreds of temp
/// entities a second and a per-event warning would bury the line that explained
/// it; per LEVEL, because a gap that was said once at load and never again is a
/// gap nobody reading a fresh console will see.
/// </para>
///
/// <para>
/// <see cref="ClientTempEnts"/>'s own "unknown handle" and "nothing plays this"
/// warnings are deliberately once per PROCESS instead: those describe a
/// registration mismatch, which cannot change without a reload, so saying them
/// again at every level change would be noise about a fact that has not moved.
/// A missing CAPABILITY is different — it is missing again, in this map, for
/// whatever fires here.
/// </para>
///
/// <para>
/// OPTIONAL BY DESIGN. Nothing requires a player to implement this and most
/// never will; <see cref="ClientTempEnts.LevelInit"/> tests for it and skips
/// the rest. A required member would put an empty method on forty-three classes
/// to serve two.
/// </para>
/// </summary>
public interface IClientTempEntLevel
{
    /// <summary>
    /// A new level has begun. Anything remembered about the last one — which
    /// warnings have already been said, most of all — is now stale.
    /// </summary>
    void LevelInit();
}
