//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_blood: blood, on demand, at a place a map chooses.
///
/// <para>
/// The effect itself is a temp entity with no server object behind it, so this
/// is env_spark's shape rather than an entity with state — what moved to C# is
/// every one of the choices around it, and there are more of them than the
/// entity's size suggests. Stock decides four separate things from its
/// spawnflags: which of two sprays, which of three positions, whether the
/// direction is fixed or random, and whether to leave a mark. Each is a bit,
/// and reading them as independent decisions is what this class is.
/// </para>
///
/// <para>
/// The one thing NOT decided here is the colour of "no blood". DONT_BLEED is a
/// real value of <see cref="BloodColor"/> and the engine checks for it too; see
/// the note on <c>BloodEffect</c> in sourcesharp.h for why it is checked there
/// rather than here.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvBlood)]
public sealed class EnvBlood : ScriptedEntity
{
    public EnvBlood(IEngineEffects effects, IEngineHost host, BloodDecals blood)
    {
        Blood = blood;
        Effects = effects;
        Host = host;
    }

    /// <summary>the small effects.</summary>
    private readonly IEngineEffects Effects;
    private readonly BloodDecals Blood;

    /// <summary>the process and the match.</summary>
    private readonly IEngineHost Host;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="EnvBloodVars"/>.</summary>
    [ScriptVars] public EnvBloodVars Vars = new();

    /// <summary>
    /// env_blood's own spawnflags, stock's SF_BLOOD_*.
    ///
    /// <para>
    /// One domain, and it divides in a way the enum makes visible: the top three
    /// are which PARTICLE effects to throw and are folded into a
    /// <see cref="BloodSpray"/> by <see cref="SprayFor"/>, while the low four
    /// decide direction, form and whether a decal lands. The mapping between the
    /// two sets of bits is not a shift -- Cloud is 16 here and 1 there -- so they
    /// have to be two types with a function between them.
    /// </para>
    /// </summary>
    [Flags]
    internal enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        Random = 0b0000_0001,
        Stream = 0b0000_0010,
        Player = 0b0000_0100,
        Decal = 0b0000_1000,
        Cloud = 0b0001_0000,
        Drops = 0b0010_0000,
        Gore = 0b0100_0000,
    }

    /// <summary>
    /// How far from a player's eye the blood may appear, in each axis.
    ///
    /// Stock's ±10, and the jitter is the point: a spray pinned exactly to the
    /// eye reads as a texture on the camera rather than as an injury.
    /// </summary>
    private const float PlayerSpread = 10f;

    /// <summary>The direction, once the angles have been turned into one.</summary>
    private Vector _sprayDir = new(1f, 0f, 0f);

    /// <summary>
    /// Red unless the map says otherwise.
    ///
    /// <para>
    /// The "color" key is not a colour: it is an index, and stock's switch
    /// recognises exactly one value of it — 1, meaning yellow — and silently
    /// leaves everything else red, INCLUDING values that name real blood
    /// colours in the same enum. That is stock's behaviour and it is kept,
    /// because a map that wrote 2 has been getting red since 2004.
    /// </para>
    /// </summary>
    private BloodColor _color = BloodColor.Red;

    /// <summary>Which blood, once the key has been read. Test-visible.</summary>
    internal BloodColor Colour => _color;

    public override bool OnKeyValue(string key, string value)
    {
        if (!key.Equals("color", StringComparison.OrdinalIgnoreCase))
            return false;

        _color = ParseInt(value) == 1 ? BloodColor.Yellow : BloodColor.Red;
        return true;
    }

    public override void OnSpawn()
        => _sprayDir = Angles.Forward(Vars.SprayAngles);

    /// <summary>
    /// Which way this spray goes.
    ///
    /// <para>
    /// The random case is NOT a uniform direction on the sphere, and the
    /// difference matters: the bias is upward, which is what makes a random
    /// spray read as something spurting rather than as noise.
    /// </para>
    /// </summary>
    private Vector Direction()
        => Flags.HasFlag(SpawnFlag.Random) ? RandomBloodVector() : _sprayDir;

    /// <summary>
    /// Stock's UTIL_RandomBloodVector, in managed code rather than behind
    /// another slot: it is three random numbers and no engine state, and the
    /// shape of the bias is the interesting part.
    ///
    /// <para>
    /// X and Y run the full ±1, Z runs 0..1 — so every random spray has an
    /// upward component and none goes straight down.
    /// </para>
    /// <para>
    /// The randomness comes from the ENGINE's stream rather than a managed one,
    /// for the reason env_spark gives: demo playback replays server randomness
    /// and has to come out the same.
    /// </para>
    /// </summary>
    private Vector RandomBloodVector()
        => new(Host.RandomFloat(-1f, 1f),
               Host.RandomFloat(-1f, 1f),
               Host.RandomFloat(0f, 1f));

    /// <summary>
    /// Where the blood comes from.
    ///
    /// <para>
    /// Two answers, and the choice between them is stock's: with the player
    /// flag set it is near whoever fired the input; otherwise it is where the
    /// entity stands.
    /// </para>
    /// <para>
    /// The fallback differs from stock and does so knowingly. Stock calls
    /// UTIL_GetLocalPlayer when the activator is not a player, which in a
    /// multiplayer game is the first connected client and is documented by
    /// Valve as not meaning anything there. This falls back to the entity's own
    /// origin, which is at least a place the mapper chose. Recorded in
    /// docs/divergences.md.
    /// </para>
    /// </summary>
    private Vector Position(EntityHandle activator)
    {
        if (!Flags.HasFlag(SpawnFlag.Player))
            return Origin;

        if (!Entities.IsValid(activator) || !Entities.IsPlayer(activator))
            return Origin;

        return Entities.GetEyePosition(activator)
             + new Vector(Host.RandomFloat(-PlayerSpread, PlayerSpread),
                          Host.RandomFloat(-PlayerSpread, PlayerSpread),
                          Host.RandomFloat(-PlayerSpread, PlayerSpread));
    }

    /// <summary>
    /// Which of the newer effects the spawnflags asked for, as one value.
    ///
    /// Static and taking the flags, so the mapping can be checked without an
    /// engine — the bit values are Source's on one side and this mod's enum on
    /// the other, and a transposition between them is exactly the kind of thing
    /// that would never show up in play.
    /// </summary>
    internal static BloodSpray SprayFor(SpawnFlag spawnFlags)
    {
        var spray = BloodSpray.None;

        if (spawnFlags.HasFlag(SpawnFlag.Cloud)) spray |= BloodSpray.Cloud;
        if (spawnFlags.HasFlag(SpawnFlag.Drops)) spray |= BloodSpray.Drops;
        if (spawnFlags.HasFlag(SpawnFlag.Gore)) spray |= BloodSpray.Gore;

        return spray;
    }

    [ScriptedEvent("EmitBlood")]
    private void OnEmitBlood(InputData input) => Emit(input.Activator);

    private void Emit(EntityHandle activator)
    {
        Vector direction = Direction();

        Effects.BloodEffect(Position(activator), direction, _color, Vars.Amount,
                           stream: Flags.HasFlag(SpawnFlag.Stream),
                           sprayFlags: BloodSpray.None);

        if (Flags.HasFlag(SpawnFlag.Decal))
        {
            // Twice the amount, as a DISTANCE. Stock's, and it is one number
            // doing two jobs: a bigger spray marks a wall further away.
            //
            // Both ends are recomputed rather than reusing the pair above,
            // because stock calls Direction() and BloodPosition() again here —
            // so a RANDOM spray and its decal genuinely do not agree about
            // which way the blood went, and that is what every map with one has
            // always looked like.
            Vector start = Position(activator);
            Blood.Trace(start, start + Direction() * (Vars.Amount * 2f), _color);
        }

        // The newer effects come from the entity's OWN origin in stock, not
        // from the position the player flag chose — so a gore burst attached to
        // a player still happens where the entity is. Kept, and named here
        // because it reads as a bug in a map rather than in the engine.
        BloodSpray spray = SprayFor((SpawnFlag)SpawnFlags);

        if (spray != BloodSpray.None)
            Effects.BloodEffect(Origin, direction, _color, Vars.Amount, stream: false, spray);
    }
}

/// <summary>
/// The map keys <see cref="EnvBlood"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvBloodVars
{
    /// <summary>
    /// Which way it sprays, written as ANGLES and used as a vector.
    ///
    /// <para>
    /// Stock stores the key in a Vector field and converts it in place at
    /// Spawn, so the same three floats mean pitch/yaw/roll before spawn and a
    /// direction after. Kept as two fields here rather than one that changes
    /// meaning: the map's key is angles, what the effect wants is a direction,
    /// and a field that is sometimes one and sometimes the other is what makes
    /// the stock version read wrongly on a second look.
    /// </para>
    /// </summary>
    [VarName("spraydir")]
    public Vector SprayAngles { get; set; }

    /// <summary>How much. Also how far the decal trace reaches — see <see cref="Emit"/>.</summary>
    public float Amount { get; set; }
}
