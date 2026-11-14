using System.Buffers.Binary;
using System.Collections.Immutable;

namespace SourceSharp.Ai;

/// <summary>
/// How a jump a player showed was made (plan_bots_teams_tf2_part_g G.6, lane G6a), as a <c>link</c> row's kind numbers it.
/// <see cref="Blast"/> is <see cref="NavLinkKind.Blast"/>'s number, so a learned blast jump's rows sit beside the
/// finder's; the others start at 16, past any kind the navigation may name later.
/// </summary>
public enum ShownJumpKind : byte
{
    /// <summary>The player's own rocket lifted it (TF2's Soldier).</summary>
    Blast = 0,

    /// <summary>The player's own sticky lifted it (TF2's Demoman).</summary>
    Sticky = 16,

    /// <summary>A second jump in the air (TF2's Scout).</summary>
    Double = 17,

    /// <summary>A jump with the body ducked in the air.</summary>
    Crouch = 18,

    /// <summary>A jump, never ducked.</summary>
    Plain = 19,
}

/// <summary>What else was seen of a jump a player showed.</summary>
[Flags]
public enum ShownJumpFlags : byte
{
    None = 0,
    DuckedAtTakeoff = 1,
    DuckedAtLanding = 2,
    JumpPressed = 4,
    OwnBlast = 8,
    OwnSticky = 16,
    AirJump = 32,
    ShotSeen = 64,
}

/// <summary>A blast the game says moved a player in a flight: its own rocket, its own sticky, someone else's.</summary>
[Flags]
public enum JumpBlast : byte
{
    None = 0,
    Own = 1,
    OwnSticky = 2,
    Other = 4,
}

/// <summary>
/// A player's body as one command finds it (the state after the command before): where its feet are, how it moves,
/// whether it stands on the ground, is ducked, is in water, walks (not on a ladder, not flying free) and stands on
/// another player. Read on the game's thread.
/// </summary>
public readonly record struct ShownJumpBody(AiVector Origin, AiVector Velocity, bool OnGround, bool Ducked, bool Wet, bool Walking, bool OnPlayer = false);

/// <summary>
/// One command as a jump's trace keeps it: the buttons that matter (<see cref="Jump"/> and the rest), the view and the
/// move along and across it.
/// </summary>
public readonly record struct ShownJumpCommand(byte Buttons, float Pitch, float Yaw, float Forward, float Side)
{
    public const byte Jump = 1, Duck = 2, Attack = 4, Attack2 = 8, MoveForward = 16, MoveBack = 32, MoveRight = 64, MoveLeft = 128;

    /// <summary>Whether <paramref name="button"/> is held.</summary>
    public bool Holds(byte button) => (Buttons & button) != 0;
}

/// <summary>
/// One jump a player showed (plan G.6, lane G6a): how it was made, where the feet left the ground and came down, the body
/// at the takeoff, at the launch, after its own blast and at the arrival, the commands in the air, the health it cost,
/// and the trace of the player's commands from before the takeoff through the landing with the places in it of the
/// takeoff, the jump, the shot and the blast. Nothing in it says who: the indexes are into the trace.
///
/// <para>Kept in a <c>link</c> row's recipe as <see cref="Format"/> (<see cref="Recipe"/>, <see cref="TryRead"/>),
/// little-endian: the format, the kind, the flags, a reserved byte; the takeoff, the landing and the four velocities as
/// three floats each; the air commands and the whole health cost as two unsigned shorts; the takeoff, jump, shot and
/// blast indexes as shorts (-1 for none); the trace's count as an unsigned short; then each command as 7 bytes: the
/// buttons, the pitch and the yaw in hundredths of a degree as shorts (the yaw wrapped into [-180, 180)), the forward and
/// side moves over 4 as signed bytes.</para>
/// </summary>
public sealed record ShownJumpRecipe(ShownJumpKind Kind, ShownJumpFlags Flags, AiVector Takeoff, AiVector Landing, AiVector GroundVelocity,
                                     AiVector LaunchVelocity, AiVector BlastVelocity, AiVector ArrivalVelocity, int AirCommands, int HealthCost,
                                     int TakeoffIndex, int JumpIndex, int ShotIndex, int BlastIndex, ImmutableArray<ShownJumpCommand> Trace)
{
    /// <summary>A recipe's first byte for a jump a player showed: its numbers and commands (the exemplar row).</summary>
    public const byte Format = 3;

    /// <summary>
    /// A recipe's first byte for the counts of a jump players showed (the count row): nothing follows. 4, since a solver's
    /// recipes are 1 and, with a second rocket, 2 (TF2's <c>TFLinkExperience</c>): no two kinds of row share a first byte.
    /// </summary>
    public const byte CountFormat = 4;

    /// <summary>The bytes before the trace.</summary>
    public const int HeaderBytes = 90;

    /// <summary>The bytes of one command of the trace.</summary>
    public const int CommandBytes = 7;

    /// <summary>The count row's recipe: <see cref="CountFormat"/> alone, the same for every sighting.</summary>
    public static LinkRecipe CountRecipe => new([CountFormat]);

    /// <summary>This jump as an exemplar row's recipe.</summary>
    public LinkRecipe Recipe()
    {
        var bytes = new byte[HeaderBytes + Trace.Length * CommandBytes];
        bytes[0] = Format;
        bytes[1] = (byte)Kind;
        bytes[2] = (byte)Flags;
        Span<byte> at = bytes.AsSpan(4);
        foreach (AiVector v in (ReadOnlySpan<AiVector>)[Takeoff, Landing, GroundVelocity, LaunchVelocity, BlastVelocity, ArrivalVelocity])
        {
            BinaryPrimitives.WriteSingleLittleEndian(at, v.X);
            BinaryPrimitives.WriteSingleLittleEndian(at[4..], v.Y);
            BinaryPrimitives.WriteSingleLittleEndian(at[8..], v.Z);
            at = at[12..];
        }

        BinaryPrimitives.WriteUInt16LittleEndian(at, (ushort)Math.Clamp(AirCommands, 0, ushort.MaxValue));
        BinaryPrimitives.WriteUInt16LittleEndian(at[2..], (ushort)Math.Clamp(HealthCost, 0, ushort.MaxValue));
        BinaryPrimitives.WriteInt16LittleEndian(at[4..], Index(TakeoffIndex));
        BinaryPrimitives.WriteInt16LittleEndian(at[6..], Index(JumpIndex));
        BinaryPrimitives.WriteInt16LittleEndian(at[8..], Index(ShotIndex));
        BinaryPrimitives.WriteInt16LittleEndian(at[10..], Index(BlastIndex));
        BinaryPrimitives.WriteUInt16LittleEndian(at[12..], (ushort)Trace.Length);
        at = at[14..];
        foreach (ShownJumpCommand c in Trace)
        {
            at[0] = c.Buttons;
            BinaryPrimitives.WriteInt16LittleEndian(at[1..], Hundredths(c.Pitch));
            BinaryPrimitives.WriteInt16LittleEndian(at[3..], Hundredths(Wrap(c.Yaw)));
            at[5] = (byte)Quarter(c.Forward);
            at[6] = (byte)Quarter(c.Side);
            at = at[CommandBytes..];
        }

        return new LinkRecipe(ImmutableArray.Create(bytes));
    }

    /// <summary>
    /// The jump <paramref name="recipe"/> holds, as <see cref="Recipe"/> wrote it: false for any other recipe (another
    /// format, a count row's, a solver's, bytes cut short or running on).
    /// </summary>
    public static bool TryRead(LinkRecipe recipe, out ShownJumpRecipe? jump)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        jump = null;
        ReadOnlySpan<byte> bytes = recipe.Bytes.AsSpan();
        if (bytes.Length < HeaderBytes || bytes[0] != Format)
            return false;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[88..]);
        if (bytes.Length != HeaderBytes + count * CommandBytes)
            return false;
        Span<AiVector> v = stackalloc AiVector[6];
        ReadOnlySpan<byte> at = bytes[4..];
        for (int i = 0; i < v.Length; i++, at = at[12..])
            v[i] = new AiVector(BinaryPrimitives.ReadSingleLittleEndian(at), BinaryPrimitives.ReadSingleLittleEndian(at[4..]), BinaryPrimitives.ReadSingleLittleEndian(at[8..]));
        var trace = ImmutableArray.CreateBuilder<ShownJumpCommand>(count);
        for (ReadOnlySpan<byte> c = bytes[HeaderBytes..]; c.Length > 0; c = c[CommandBytes..])
        {
            trace.Add(new ShownJumpCommand(c[0], BinaryPrimitives.ReadInt16LittleEndian(c[1..]) / 100f, BinaryPrimitives.ReadInt16LittleEndian(c[3..]) / 100f,
                                      (sbyte)c[5] * 4f, (sbyte)c[6] * 4f));
        }

        jump = new ShownJumpRecipe((ShownJumpKind)bytes[1], (ShownJumpFlags)bytes[2], v[0], v[1], v[2], v[3], v[4], v[5],
                                   BinaryPrimitives.ReadUInt16LittleEndian(at), BinaryPrimitives.ReadUInt16LittleEndian(at[2..]),
                                   BinaryPrimitives.ReadInt16LittleEndian(at[4..]), BinaryPrimitives.ReadInt16LittleEndian(at[6..]),
                                   BinaryPrimitives.ReadInt16LittleEndian(at[8..]), BinaryPrimitives.ReadInt16LittleEndian(at[10..]), trace.MoveToImmutable());
        return true;
    }

    /// <summary>The bin the feet left the ground in.</summary>
    public WorldBin From => WorldBin.Of(Takeoff);

    /// <summary>The bin the feet came down in.</summary>
    public WorldBin To => WorldBin.Of(Landing);

    /// <summary>The count row's key of this jump for a player of <paramref name="cls"/>: the same for every sighting of it.</summary>
    public LinkKey CountKey(int cls) => new(cls, From, To, (int)Kind, LinkSource.Human, CountRecipe);

    /// <summary>The exemplar row's key of this jump for a player of <paramref name="cls"/>.</summary>
    public LinkKey ExemplarKey(int cls) => new(cls, From, To, (int)Kind, LinkSource.Human, Recipe());

    /// <summary>The shot's view (the trace's command at <see cref="ShotIndex"/>), or null with no shot seen.</summary>
    public ShownJumpCommand? ShotCommand => (uint)ShotIndex < (uint)Trace.Length ? Trace[ShotIndex] : null;

    /// <summary>Commands from the jump's to the shot's: negative when the shot came first; null without both.</summary>
    public int? ShotDelay => JumpIndex >= 0 && ShotIndex >= 0 ? ShotIndex - JumpIndex : null;

    private static short Index(int index) => (short)Math.Clamp(index, -1, short.MaxValue);

    private static short Hundredths(float degrees) => (short)Math.Clamp(MathF.Round(degrees * 100f), short.MinValue, short.MaxValue);

    private static sbyte Quarter(float move) => (sbyte)Math.Clamp(MathF.Round(move / 4f), sbyte.MinValue, sbyte.MaxValue);

    /// <summary><paramref name="degrees"/> into [-180, 180).</summary>
    public static float Wrap(float degrees)
    {
        float w = (degrees + 180f) % 360f;
        if (w < 0f)
            w += 360f;
        return w - 180f;
    }

    public bool Equals(ShownJumpRecipe? other)
        => other is not null && Kind == other.Kind && Flags == other.Flags && Takeoff == other.Takeoff && Landing == other.Landing
           && GroundVelocity == other.GroundVelocity && LaunchVelocity == other.LaunchVelocity && BlastVelocity == other.BlastVelocity
           && ArrivalVelocity == other.ArrivalVelocity && AirCommands == other.AirCommands && HealthCost == other.HealthCost
           && TakeoffIndex == other.TakeoffIndex && JumpIndex == other.JumpIndex && ShotIndex == other.ShotIndex && BlastIndex == other.BlastIndex
           && Trace.AsSpan().SequenceEqual(other.Trace.AsSpan());

    public override int GetHashCode() => HashCode.Combine(Kind, Flags, Takeoff, Landing, AirCommands, HealthCost, Trace.Length);
}
