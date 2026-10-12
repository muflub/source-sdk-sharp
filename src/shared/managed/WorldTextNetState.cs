using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a <c>point_worldtext</c> tells the client.
///
/// <para>
/// The whole entity, and that is the unusual part. Most rows in report.md's
/// presentation group network a handful of numbers and keep the interesting
/// work on one side or the other; this one is eight values that a mapper typed
/// and nine hundred lines of mesh building that reads them. Stock's send table
/// is these eight fields exactly, in this order.
/// </para>
///
/// <para>
/// The TEXT travels as a string rather than as an index, and there is no choice
/// about it: an arbitrary message a mapper wrote is not in any precache table,
/// so there is no number for both sides to already agree on. Same reason
/// <see cref="ColorCorrectionNetState.LookupFile"/> is a string.
/// </para>
///
/// <para>
/// THE WIDTH IS 128 AND NOT STOCK'S 260, and that is the one decision in this
/// file. Stock declares <c>CNetworkString( m_szText, MAX_PATH )</c>, and
/// <c>MAX_PATH</c> is an allowance rather than a measurement — the same
/// allowance <c>color_correction</c> carried until Step 13 measured what it
/// cost. Every entity on the parallel hierarchy is allocated ONE blob whatever
/// its class, sized to the greediest declaration, so 260 here makes the class
/// 280 bytes, puts the shared blob 24 past the 256-byte ceiling, and moves all
/// 336 entities up a tier — to carry a message nobody writes. 128 is
/// <c>func_smokevolume</c>'s width for its material name and leaves this class
/// at 148 bytes, under that class's 164 — so the tier does not move at all.
/// </para>
///
/// <para>
/// It was written at 260 first and <c>NetTableTests.TierPressureIsPerClassNow</c>
/// stopped it, when the next tier above 256 was 2048 and 518 props. The 512 and
/// 1024 tiers have since made the overshoot cost a doubling rather than an
/// eightfold jump, and Part 18 made it cost THIS CLASS rather than every entity
/// in the game — each class is sized to its own tier now. It is still a mistake
/// worth not making: 24 bytes nothing writes would still double this class's
/// blob and prop count for nothing.
/// </para>
///
/// <para>
/// A message that does not fit is REFUSED and names itself — see
/// <c>NetFieldRange.ThrowString</c> — rather than being truncated, because half
/// a sign is a sign that says something else. If a map ever wants more, the
/// number to change is this class's own declaration and the change is visible in
/// <c>NetTableTests.TierPressureIsPerClassNow</c>, which is where it should be
/// argued.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.PointWorldtext)]
public partial class WorldTextNetState
{
    /// <summary>The message. Newlines break lines, which is the only formatting there is.</summary>
    [NetString(128)] public partial string Text { get; set; }

    /// <summary>Height of a line in world units, before the font's own metrics scale it.</summary>
    public partial float TextSize { get; set; }

    /// <summary>Extra world units between glyphs. Zero is the font's own advance.</summary>
    public partial float SpacingX { get; set; }

    /// <summary>Extra world units between lines.</summary>
    public partial float SpacingY { get; set; }

    /// <summary>
    /// Packed RGBA with red in the LOW byte, which is <c>color32</c>'s own
    /// layout in memory and therefore what stock's single 32-bit send prop
    /// carries. Packed rather than four fields so that the number crossing the
    /// ABI reads identically on both sides.
    /// </summary>
    public partial uint Color { get; set; }

    /// <summary>
    /// 0 draws along the entity's own angles, 1 faces the viewer, 2 faces the
    /// viewer but stays upright. Stock sends it in three bits.
    /// </summary>
    [NetField(Bits = 3)] public partial uint Orientation { get; set; }

    /// <summary>Index into the client's font table. The client clamps it; nothing here does.</summary>
    [NetField(Bits = 16)] public partial uint Font { get; set; }

    /// <summary>Cycles each glyph's hue instead of using <see cref="Color"/>'s RGB.</summary>
    [NetField(Bits = 1)] public partial bool Rainbow { get; set; }
}
