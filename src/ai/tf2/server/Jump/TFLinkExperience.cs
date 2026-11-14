using System.Buffers.Binary;
using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Experience;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// A blast link's tries in the experience (lane G5e, plan_bots_teams_tf2_part_g G.5 step 7): the <c>link</c> row each of
/// the level's links is written to, and what one jump a bot made of a link adds to it.
///
/// <para>
/// <b>The row.</b> The Soldier's class; the 64 u bins of where the body stands to take it (the recipe's start) and of
/// where the recipe means it to come down; the blast kind; the source <see cref="LinkSource.Solver"/> (the jump was
/// found by the finder, the bots only try it: G.6's player-shown links will be <see cref="LinkSource.Human"/> and earn
/// their place by the same tries); and the recipe's own numbers as bytes (<see cref="RecipeOf"/>), so a link found again
/// with another recipe starts a row of its own, and G.6 has a recipe to fit against. All of it comes from the recipe, not
/// from the graph's cells: a rebuilt navigation keeps its rows.
/// </para>
///
/// <para>
/// <b>A try.</b> A link's jump whose rocket went (<see cref="JumpFlown.Launched"/>): landed near its touch
/// (<see cref="JumpTally.NearUnits"/>) is a success, with its seconds from the program's first command to the touch and
/// the recipe's health; landed elsewhere, fizzled, died or timed out after the launch is a try that failed. Nothing is
/// added for a jump called off before the launch (the bot's view or place, which its own follower already avoids the
/// link for), for a fizzle whose blast was felt within <see cref="TeammateFeltCommands"/> of the trigger (the mod's
/// rocket going off on a teammate in front of it: nothing about the link), nor for a body the game moved
/// (<see cref="JumpEnd.Moved"/>: a round's restart, a teleport). A reset (<see cref="JumpEnd.Reset"/>: a fall put back at
/// the jump's start, lane G7a) is a try that failed.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(IExperienceLinks)])]
public sealed class TFLinkExperience(TFBlastLinks links) : IExperienceLinks
{
    /// <summary>The recipe bytes' first byte: what the rest is (a TF2 rocket jump's numbers, this layout).</summary>
    public const byte RecipeFormat = 1;

    /// <summary>The first byte of a recipe with a second rocket (lane G7b): format 1's numbers, then the second shot's.</summary>
    public const byte SecondRecipeFormat = 2;

    /// <summary>
    /// The first byte of a recipe whose second rocket is fired at a wall beside the flight (lane G7c): format 2's layout. Not 3
    /// or 4, a person's rows (<c>ShownJumpRows</c>).
    /// </summary>
    public const byte WallRecipeFormat = 5;

    /// <summary>A fizzle whose blast was felt this many commands after the trigger or sooner went off on something in front of the muzzle.</summary>
    public const int TeammateFeltCommands = 2;

    private readonly Lock _gate = new();
    private JumpLinkSet? _keyedSet;
    private ExperienceLinkKeys? _keys;

    /// <summary>The published links' keys, made once a set (the game's thread asks).</summary>
    public ExperienceLinkKeys? Keys
    {
        get
        {
            JumpLinkSet? set = links.Set;
            lock (_gate)
            {
                if (!ReferenceEquals(set, _keyedSet))
                    (_keyedSet, _keys) = (set, set is null ? null : KeysOf(set));
                return _keys;
            }
        }
    }

    /// <summary>The keys of <paramref name="set"/>'s links, index for index.</summary>
    public static ExperienceLinkKeys KeysOf(JumpLinkSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var keys = new LinkKey[set.Links.Count];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = KeyOf(set.JumpOf(i).Recipe);
        return new ExperienceLinkKeys(set.Links, keys);
    }

    /// <summary>The row a link flown by <paramref name="recipe"/> is written to.</summary>
    public static LinkKey KeyOf(in JumpRecipe recipe)
    {
        JumpStart start = recipe.Start();
        return new LinkKey((int)TFClass.Soldier, WorldBin.Of(new AiVector(start.Origin.X, start.Origin.Y, start.Origin.Z)),
                           WorldBin.Of(new AiVector(recipe.Landing.X, recipe.Landing.Y, recipe.Landing.Z)), (int)NavLinkKind.Blast, LinkSource.Solver, RecipeOf(recipe));
    }

    /// <summary>
    /// A recipe's numbers as the row keeps them: <see cref="RecipeFormat"/>, then the takeoff's and the landing's x, y and z,
    /// the run-up, the pitch and the yaw offset as little-endian floats, the shot's delay as a little-endian int and the
    /// duck as a byte. A recipe with a second rocket (lane G7b) is <see cref="SecondRecipeFormat"/>, the same numbers, then
    /// its place's x, y and z, its earliest and latest commands as ints, its pitch and yaw offset: a second rocket's row is
    /// never a one-rocket recipe's. One whose second rocket is fired at a wall (lane G7c) is <see cref="WallRecipeFormat"/>,
    /// the same numbers. The same recipe is the same bytes, read from the links' cache or found again.
    /// </summary>
    public static LinkRecipe RecipeOf(in JumpRecipe recipe)
    {
        var bytes = new byte[recipe.Second is null ? One : One + Second];
        bytes[0] = recipe.Second is not { } shot ? RecipeFormat : shot.Wall ? WallRecipeFormat : SecondRecipeFormat;
        Span<byte> at = bytes.AsSpan(1);
        foreach (float f in (ReadOnlySpan<float>)[recipe.Takeoff.X, recipe.Takeoff.Y, recipe.Takeoff.Z, recipe.Landing.X, recipe.Landing.Y, recipe.Landing.Z,
                                                   recipe.RunUp, recipe.Pitch, recipe.YawOffset])
        {
            BinaryPrimitives.WriteSingleLittleEndian(at, f);
            at = at[sizeof(float)..];
        }

        BinaryPrimitives.WriteInt32LittleEndian(at, recipe.ShotDelay);
        at[sizeof(int)] = recipe.Ducked ? (byte)1 : (byte)0;
        if (recipe.Second is { } second)
        {
            at = at[(sizeof(int) + 1)..];
            foreach (float f in (ReadOnlySpan<float>)[second.Over.X, second.Over.Y, second.Over.Z])
            {
                BinaryPrimitives.WriteSingleLittleEndian(at, f);
                at = at[sizeof(float)..];
            }

            BinaryPrimitives.WriteInt32LittleEndian(at, second.Earliest);
            BinaryPrimitives.WriteInt32LittleEndian(at[sizeof(int)..], second.Latest);
            BinaryPrimitives.WriteSingleLittleEndian(at[(2 * sizeof(int))..], second.Pitch);
            BinaryPrimitives.WriteSingleLittleEndian(at[(2 * sizeof(int) + sizeof(float))..], second.YawOffset);
        }

        return new LinkRecipe(ImmutableArray.Create(bytes));
    }

    /// <summary>A one-rocket recipe's bytes (<see cref="RecipeFormat"/>), and what a second rocket adds to them.</summary>
    private const int One = 1 + 9 * sizeof(float) + sizeof(int) + 1, Second = 5 * sizeof(float) + 2 * sizeof(int);

    /// <summary>The row a practice of <paramref name="recipe"/> is written to (lane G8, <c>tools/ai-jump-practice</c>): <see cref="KeyOf"/>'s, of <see cref="LinkSource.Practice"/>.</summary>
    public static LinkKey PracticeKeyOf(in JumpRecipe recipe) => KeyOf(recipe) with { Source = LinkSource.Practice };

    /// <summary>
    /// The recipe <paramref name="bytes"/> hold (lane G8): <see cref="RecipeOf"/> read back, formats 1, 2 and 5. False for any
    /// other format (a person's rows, 3 and 4), a length that is not the format's, a duck byte that is neither 0 nor 1, or a
    /// number that is not finite.
    /// </summary>
    public static bool TryRecipe(LinkRecipe bytes, out JumpRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        recipe = default;
        ReadOnlySpan<byte> b = bytes.Bytes.AsSpan();
        if (b.Length == 0 || b[0] is not (RecipeFormat or SecondRecipeFormat or WallRecipeFormat))
            return false;
        bool two = b[0] != RecipeFormat;
        if (b.Length != (two ? One + Second : One))
            return false;
        Span<float> f = stackalloc float[9];
        ReadOnlySpan<byte> at = b[1..];
        for (int i = 0; i < f.Length; i++)
        {
            f[i] = BinaryPrimitives.ReadSingleLittleEndian(at);
            at = at[sizeof(float)..];
        }

        int delay = BinaryPrimitives.ReadInt32LittleEndian(at);
        byte duck = at[sizeof(int)];
        if (duck > 1)
            return false;
        JumpSecondShot? second = null;
        if (two)
        {
            at = at[(sizeof(int) + 1)..];
            var over = new Vector(BinaryPrimitives.ReadSingleLittleEndian(at), BinaryPrimitives.ReadSingleLittleEndian(at[4..]), BinaryPrimitives.ReadSingleLittleEndian(at[8..]));
            at = at[(3 * sizeof(float))..];
            int earliest = BinaryPrimitives.ReadInt32LittleEndian(at), latest = BinaryPrimitives.ReadInt32LittleEndian(at[sizeof(int)..]);
            float pitch = BinaryPrimitives.ReadSingleLittleEndian(at[(2 * sizeof(int))..]), yaw = BinaryPrimitives.ReadSingleLittleEndian(at[(2 * sizeof(int) + sizeof(float))..]);
            if (!float.IsFinite(over.X) || !float.IsFinite(over.Y) || !float.IsFinite(over.Z) || !float.IsFinite(pitch) || !float.IsFinite(yaw))
                return false;
            second = new JumpSecondShot(over, earliest, latest, pitch, yaw) { Wall = b[0] == WallRecipeFormat };
        }

        foreach (float v in f)
        {
            if (!float.IsFinite(v))
                return false;
        }

        recipe = new JumpRecipe(new Vector(f[0], f[1], f[2]), new Vector(f[3], f[4], f[5]), f[6], f[7], f[8], delay, duck == 1, second);
        return true;
    }

    /// <summary>The family a recipe's numbers say (lane G8): a second rocket at a wall or under the way, else a run-up or none.</summary>
    public static JumpFamily FamilyOf(in JumpRecipe recipe)
        => recipe.Second is { } s ? s.Wall ? JumpFamily.Wall : JumpFamily.Double : recipe.RunUp > 0f ? JumpFamily.Forward : JumpFamily.Up;

    /// <summary>
    /// What a jump a bot made adds to its link's row, or null: a jump that was no link's (a stretch's plan), or one that
    /// says nothing of its link (see the class's remarks).
    /// </summary>
    public static LinkStat? StatOf(JumpFlown flown)
    {
        ArgumentNullException.ThrowIfNull(flown);
        if (NavLinks.LinkOf(flown.Planned.Id) < 0 || !flown.Launched || flown.End == JumpEnd.Moved)
            return null;
        if (flown.End == JumpEnd.Fizzled && flown.FeltAfterShot is >= 0 and <= TeammateFeltCommands)
            return null;
        if (flown.End == JumpEnd.Landed && JumpTally.Flat(flown.Touch, flown.Planned.Touch) <= JumpTally.NearUnits)
            return new LinkStat(1d, 1d, (flown.ApproachCommands + flown.AirCommands) * flown.TickSeconds, flown.Planned.Health);
        return new LinkStat(1d, 0d, 0d, 0d);
    }
}
