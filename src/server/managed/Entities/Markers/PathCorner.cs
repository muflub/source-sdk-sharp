using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// One stop on a path a mover walks, and the name of the stop after it.
///
/// The oldest of the three node types and the simplest: a place, how long to sit
/// there, and how fast to leave. It owns nothing about the journey — the speed
/// belongs to the leg LEAVING this corner, and who is walking the chain decides
/// what to do with it. A <see cref="KeyframeTrack"/> is the other way round: it
/// owns the duration of its own leg, because a keyframe is a schedule and a
/// corner is only a place.
///
/// The chain is followed forward and never back, which is why there is no
/// back-link here and there is one on a keyframe. Nothing that walks corners
/// reverses: a func_train runs the chain one way until it runs out or loops. The
/// entity that does turn round, func_tracktrain, walks path_track instead, and
/// path_track keeps both links precisely because of that.
///
/// The forward link is resolved on demand rather than built once, and that is
/// not laziness — <c>SetNextPathCorner</c> lets a map rewrite it while a mover is
/// walking the chain, so a link cached at spawn would be a link that stopped
/// telling the truth.
/// </summary>
[ScriptedEntity(EntityNames.PathCorner)]
// Stock's empty subclass, kept as a separate name because HL1 maps ask for it.
// It adds no behaviour there either.
[ScriptedEntity(EntityNames.PathCornerCrash)]
public class PathCorner : ScriptedEntity
{
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>Fired as a mover passes this corner. See <see cref="Pass"/>.</summary>
    public readonly Output OnPass = new();

    /// <summary>What a map may set on this entity. See <see cref="PathCornerVars"/>.</summary>
    [ScriptVars] public PathCornerVars Vars = new();

    private PathCorner? _next;
    private bool _resolved;

    //-------------------------------------------------------------------------
    // Where the corner is, read live.
    //
    // Not captured at spawn, and that is the opposite of what a keyframe does.
    // A keyframe captures because the MOVER is itself a keyframe and travels off
    // its own key, so the key has to remember where the mapper drew it; a corner
    // is never the mover, so there is nothing to remember it against. Stock reads
    // GetLocalOrigin every time for the same reason.
    //
    // It also means a corner that was PUT somewhere after it spawned is where it
    // actually is — which is every corner created from the console, since
    // ent_create places an entity after spawning it.
    //-------------------------------------------------------------------------
    public Vector Position => Origin;

    public float Delay => Vars.Wait;

    public float Speed => Vars.Speed;

    /// <summary>
    /// A mover reaching this corner stops and waits to be started again.
    ///
    /// The same bit a train uses to mean the same thing about itself, which is
    /// how stock can test either and get the answer it wants.
    /// </summary>
    public bool WaitForTrigger => Flags.HasFlag(SpawnFlag.WaitForTrigger);

    /// <summary>
    /// path_corner's own spawnflags, and NOT path_track's.
    ///
    /// <para>
    /// Converted despite being a pair, because the pair is the hazard: both entity
    /// kinds are nodes a mover follows, both have a teleport flag, and they are
    /// numbered differently -- 0x002 here against path_track's 0x0010. A bare int
    /// from one read against the other's constants is silent and wrong.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        WaitForTrigger = 0b0000_0001,
        Teleport = 0b0000_0010,
    }

    /// <summary>A mover reaching this corner jumps to it rather than travelling.</summary>
    public bool Teleport => Flags.HasFlag(SpawnFlag.Teleport);

    /// <summary>
    /// The corner after this one, or null at the end of the chain.
    ///
    /// A chain that names nothing has ended, which is not a mistake; a name that
    /// resolves to nothing is one, and is warned about once because the answer is
    /// then cached.
    /// </summary>
    public PathCorner? Next
    {
        get
        {
            if (_resolved)
                return _next;

            _resolved = true;

            if (Vars.NextName.Length == 0)
                return _next = null;

            _next = FindByName<PathCorner>(Vars.NextName);

            if (_next is null)
                Log.Warning($"[SourceSharp] {ClassName} '{Name}': no path corner named '{Vars.NextName}'\n");

            return _next;
        }
    }

    public override void OnSpawn()
    {
        // Stock asserts here, which stops a debug build on a map that is merely
        // sloppy: an unnamed corner is unreachable, because the only way to a
        // corner is by name. Said out loud instead — the map still loads, and
        // whatever was supposed to walk this chain finds nothing.
        if (Name.Length == 0)
            Log.Warning($"[SourceSharp] {ClassName} at {Position.X} {Position.Y} {Position.Z} " +
                           "has no targetname, so nothing can reach it\n");
    }

    /// <summary>
    /// Joins this corner to the next one, as resolving the name would have.
    ///
    /// Within the assembly only, so a chain can be built with no map to have
    /// named one — which is how the tests reach the walking. One way, because
    /// that is all a corner chain is: see the note on the class.
    /// </summary>
    internal void LinkTo(PathCorner? next)
    {
        _resolved = true;
        _next = next;
    }

    /// <summary>
    /// Points this corner somewhere else, mid-journey.
    ///
    /// The cache goes with it. This input is the whole reason there is one to
    /// invalidate rather than a chain linked once at spawn.
    /// </summary>
    [ScriptedEvent("SetNextPathCorner")]
    private void OnSetNextPathCorner(string value)
    {
        Vars.NextName = value;
        _resolved = false;
        _next = null;
    }

    /// <summary>
    /// Stock's internal input, not in the FGD: a mover announcing that it got
    /// here. Declared because a map that fires it by hand is entitled to.
    /// </summary>
    [ScriptedEvent("InPass")]
    private void OnInPass() => OnPass.Fire();

    /// <summary>
    /// The same announcement from a mover that already holds this corner.
    ///
    /// Directly rather than through the input, because a managed mover has the
    /// corner in hand and going back out through a name lookup would only be a
    /// way of getting it wrong.
    ///
    /// The mover names itself, as stock's CFuncTrain::Wait does with
    /// AcceptInput( "InPass", this, this, ... ) — so whatever the mapper wired
    /// to this corner learns WHICH train got here rather than only that one did.
    /// </summary>
    internal void Pass(EntityHandle mover) => OnPass.Fire(mover);
}

/// <summary>
/// The map keys <see cref="PathCorner"/> declares.
/// </summary>
[ScriptVars]
public partial class PathCornerVars
{
    [VarName("target")]
    public string NextName { get; set; } = string.Empty;

    /// <summary>
    /// Seconds a mover sits here before setting off again. Negative means it
    /// stays until something starts it, which is not the same as zero — zero
    /// means carry straight on.
    /// </summary>
    public float Wait { get; set; }

    /// <summary>
    /// How fast the leg LEAVING this corner runs, or zero to leave the mover's
    /// own speed alone. A CBaseEntity key rather than one path_corner declares,
    /// which is why stock reads it off the corner without the corner mentioning
    /// it anywhere.
    /// </summary>
    public float Speed { get; set; }
}
