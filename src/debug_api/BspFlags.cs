using System.Collections.Immutable;
using System.Text;

namespace Debug.Api;

/// <summary>
/// The bit fields a map is described with, spelled out.
///
/// Every one of these is an int in the file and a word in every conversation
/// anybody has about a map: nobody says "contents 0x10001", they say "solid and
/// playerclip". A debugger that showed the number would be making its reader do
/// a lookup that the reader is entitled to expect from it -- and the lookup is
/// against a header they may not have open.
///
/// The names are the engine's own, from public/bspflags.h, minus the prefix that
/// says which field they belong to. A row already sits under a column headed
/// "Contents"; repeating CONTENTS_ eleven times across it is noise.
/// </summary>
internal static class BspFlags
{
    /// <summary>
    /// What a brush or a leaf is made of.
    ///
    /// Ordered by bit so that the same set of flags always reads the same way
    /// round. Sorting by name instead would put "solid" in a different place
    /// depending on what it was combined with, and a column that reorders itself
    /// is a column you cannot scan.
    /// </summary>
    private static readonly ImmutableArray<(int Bit, string Name)> Contents =
    [
        (0x1, "solid"),
        (0x2, "window"),
        (0x4, "aux"),
        (0x8, "grate"),
        (0x10, "slime"),
        (0x20, "water"),
        (0x40, "blocklos"),
        (0x80, "opaque"),
        (0x100, "testfogvolume"),
        (0x800, "team1"),
        (0x1000, "team2"),
        (0x2000, "ignorenodrawopaque"),
        (0x4000, "moveable"),
        (0x8000, "areaportal"),
        (0x10000, "playerclip"),
        (0x20000, "monsterclip"),
        (0x40000, "current0"),
        (0x80000, "current90"),
        (0x100000, "current180"),
        (0x200000, "current270"),
        (0x400000, "currentup"),
        (0x800000, "currentdown"),
        (0x1000000, "origin"),
        (0x2000000, "monster"),
        (0x4000000, "debris"),
        (0x8000000, "detail"),
        (0x10000000, "translucent"),
        (0x20000000, "ladder"),
        (0x40000000, "hitbox"),
    ];

    /// <summary>
    /// What a surface does.
    ///
    /// Held in a short by the engine, so nothing above bit 15 is real here.
    /// These are the flags worth reading a map for: nodraw, hint and skip are all
    /// invisible in game and all three change what the compiler did.
    /// </summary>
    private static readonly ImmutableArray<(int Bit, string Name)> Surface =
    [
        (0x0001, "light"),
        (0x0002, "sky2d"),
        (0x0004, "sky"),
        (0x0008, "warp"),
        (0x0010, "trans"),
        (0x0020, "noportal"),
        (0x0040, "trigger"),
        (0x0080, "nodraw"),
        (0x0100, "hint"),
        (0x0200, "skip"),
        (0x0400, "nolight"),
        (0x0800, "bumplight"),
        (0x1000, "noshadows"),
        (0x2000, "nodecals"),
        (0x4000, "nochop"),
        (0x8000, "hitbox"),
    ];

    /// <summary>What a level was compiled with. One bit each, and both worth knowing.</summary>
    private static readonly ImmutableArray<(int Bit, string Name)> Level =
    [
        (0x1, "static prop lighting baked (LDR)"),
        (0x2, "static prop lighting baked (HDR)"),
    ];

    public static string DescribeContents(int flags) => Describe(flags, Contents);

    public static string DescribeSurface(int flags) => Describe(flags, Surface);

    public static string DescribeLevel(int flags) => Describe(flags, Level);

    /// <summary>
    /// The set bits, named and joined.
    ///
    /// A bit with no name is reported as its own hexadecimal value rather than
    /// dropped. The alternative is a row that looks like it has fewer flags than
    /// it has, and being quietly incomplete is the one thing a view of a file
    /// must never be.
    /// </summary>
    private static string Describe(int flags, ImmutableArray<(int Bit, string Name)> names)
    {
        if (flags == 0)
            return "";

        var text = new StringBuilder();
        int left = flags;

        foreach (var (bit, name) in names)
        {
            if ((flags & bit) == 0)
                continue;

            if (text.Length > 0)
                text.Append(' ');

            text.Append(name);
            left &= ~bit;
        }

        if (left != 0)
        {
            if (text.Length > 0)
                text.Append(' ');

            text.Append("0x").Append(left.ToString("x"));
        }

        return text.ToString();
    }
}
