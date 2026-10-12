using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui;

/// <summary>
/// The colours that are game data, not design: the rarity ladder (D5) and the
/// character sheet's semantic inks (C.1: Now blue when raised, red when
/// lowered, Base gold at cap). They enter through <see cref="UiPaint.FromGame"/>
/// exactly as a map's game_text colour does — drawn as the server's data says,
/// not a design escape, so the lint's raw rule does not list them (contrast is
/// still checked). One place, so a reviewer sees the whole ladder at once and
/// the theme file can override them per mod the same way.
/// </summary>
public static class DescentPaints
{
    /// <summary>Stock: the muted grey of an item with no affixes.</summary>
    public static UiPaint Stock => UiPaint.FromGame(new UiColor(155, 161, 169, 255));

    /// <summary>Vintage: one or two affixes.</summary>
    public static UiPaint Vintage => UiPaint.FromGame(new UiColor(94, 173, 84, 255));

    /// <summary>Strange: three or four.</summary>
    public static UiPaint Strange => UiPaint.FromGame(new UiColor(90, 148, 214, 255));

    /// <summary>Unusual: five or six and an effect.</summary>
    public static UiPaint Unusual => UiPaint.FromGame(new UiColor(168, 111, 214, 255));

    /// <summary>Australium: the hand-authored uniques, and the currency.</summary>
    public static UiPaint Australium => UiPaint.FromGame(new UiColor(214, 182, 74, 255));

    /// <summary>The ladder, in D5's order.</summary>
    public static UiPaint Of(DescentRarity rarity) => rarity switch
    {
        DescentRarity.Stock => Stock,
        DescentRarity.Vintage => Vintage,
        DescentRarity.Strange => Strange,
        DescentRarity.Unusual => Unusual,
        _ => Australium,
    };

    /// <summary>Sheet: Now above Base (C.1's blue).</summary>
    public static UiPaint Raised => UiPaint.FromGame(new UiColor(110, 165, 235, 255));

    /// <summary>Sheet: Now below Base — a curse or a broken item (C.1's red).</summary>
    public static UiPaint Lowered => UiPaint.FromGame(new UiColor(224, 98, 88, 255));

    /// <summary>Sheet: Base at its class cap, a resistance at its cap (C.1's gold).</summary>
    public static UiPaint Capped => UiPaint.FromGame(new UiColor(232, 197, 108, 255));

    /// <summary>Life orb body: the Diablo red disc, dimmed to sit over the world.</summary>
    public static UiPaint LifeOrb => UiPaint.FromGame(new UiColor(178, 54, 47, 255));

    /// <summary>Mana orb body: the Diablo blue disc.</summary>
    public static UiPaint ManaOrb => UiPaint.FromGame(new UiColor(52, 88, 166, 255));

    /// <summary>Automap: a revealed room's fill and hairline, drawn by the sent outline only.
    /// The fill is a wash, not a slab: the minimap sits over the world, and the
    /// hairline (MapLine) is what actually reads as the room.</summary>
    public static UiPaint MapRoom => UiPaint.FromGame(new UiColor(64, 70, 80, 110));

    public static UiPaint MapRoomCurrent => UiPaint.FromGame(new UiColor(96, 106, 122, 150));

    public static UiPaint MapLine => UiPaint.FromGame(new UiColor(150, 158, 170, 255));
}
