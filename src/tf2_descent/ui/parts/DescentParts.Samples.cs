using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

// The parts' gallery (ui-screen loop step 2): each shared part drawn in the
// states it must look right in, over the busy backdrop like the HUD's life.
// Data names are ui.Data (game copy); every #Descent_* token lives in
// game/mod_tf2_descent/resource/descent_english.txt.

internal static class DescentPartGallery
{
    private static DescentItemView Item(string name, DescentRarity r, int dur = 0, int durMax = 0,
        bool id = true, bool broken = false, bool unusual = false)
        => new(name, r, 24, id, dur, durMax, 3, unusual, broken, false, false);

    [UiGallery("ItemSlot", "stock, empty, locked, damaged, unusual, unidentified")]
    private static UiScreen Slots(UiSampleServices s)
        => new UiPartPreview("ItemSlot", (ref UiFrame ui) =>
        {
            using (ui.Panel().Pad(Space.S, Space.S).Fill(Tone.Surface))
                using (ui.Row(Space.S, Align.Centre))
                {
                    ui.Sub("stock", s.Create<ItemSlot>(), new ItemSlotProps(Item("Force-A-Bonk", DescentRarity.Stock, 18, 40), false, false, false, false));
                    ui.Sub("empty", s.Create<ItemSlot>(), new ItemSlotProps(null, false, false, false, false));
                    ui.Sub("locked", s.Create<ItemSlot>(), new ItemSlotProps(null, true, false, false, false));
                    ui.Sub("vintage", s.Create<ItemSlot>(), new ItemSlotProps(Item("Gunboats", DescentRarity.Vintage, 3, 40), false, false, true, false));
                    ui.Sub("broken", s.Create<ItemSlot>(), new ItemSlotProps(Item("Deadhead", DescentRarity.Strange, 0, 40, true, true), false, false, false, false));
                    ui.Sub("unusual", s.Create<ItemSlot>(), new ItemSlotProps(Item("Axe", DescentRarity.Unusual, unusual: true), false, false, false, true));
                    ui.Sub("unknown", s.Create<ItemSlot>(), new ItemSlotProps(Item("Charged USP", DescentRarity.Australium, id: false), false, true, false, false));
                }
        });

    [UiGallery("ItemSlot", "inspectable: the corner button")]
    private static UiScreen SlotInspectable(UiSampleServices s)
        => new UiPartPreview("ItemSlot", (ref UiFrame ui) =>
        {
            using (ui.Panel().Pad(Space.S, Space.S).Fill(Tone.Surface))
                using (ui.Row(Space.S, Align.Centre))
                {
                    ui.Sub("inspecting", s.Create<ItemSlot>(),
                        new ItemSlotProps(Item("Force-A-Bonk", DescentRarity.Unusual, unusual: true),
                                          false, false, false, false, true, 3, new FakeInspectActions()));
                    ui.Sub("plain", s.Create<ItemSlot>(),
                        new ItemSlotProps(Item("Force-A-Bonk", DescentRarity.Unusual, unusual: true),
                                          false, false, false, false));
                }
        });

    // The inspect modal's two views (the plan's 7: the texture handle is the
    // engine's surface; the preview stamps it, so the picture names it). The
    // hidden state draws nothing and therefore gets no gallery entry.
    [UiGallery("InspectModal", "weapon alone")]
    private static UiScreen InspectWeapon(UiSampleServices s)
        => new UiPanelPreview("InspectModal",
            new InspectModal(new FakeInspect(), new FakeInspectActions()), layer: UiLayerKind.Menu);

    [UiGallery("InspectModal", "mounted on the hero")]
    private static UiScreen InspectMounted(UiSampleServices s)
        => new UiPanelPreview("InspectModal",
            new InspectModal(new FakeInspect { Mounted = true }, new FakeInspectActions()),
            layer: UiLayerKind.Menu);

    // The vendor's 3D portrait (plan 7: the surface is the engine's, the
    // panel declares it): the stall with the portrait injected mid-sentence
    // (the texture stamp names the surface, the right column's plate layers
    // over it). The plain stall is what every vendor sample already draws,
    // so the gallery carries the declared-surface state alone.
    [UiSample("portrait-identify")]
    private static UiScreen Portrait(UiSampleServices s)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(new MemoryWindowStore());
        s.Add<IDescentVendor>(new FakeWareVendor
        {
            Kind = DescentVendorKind.Identify,
            VendorNameToken = "#Descent_Vendor_Spy",
            StanceToken = "#Descent_Vendor_Stance_Identify",
            PlayerAustralium = 900,
            ServiceFee = 45,
            BuyWares = [],
            SellWares = [],
        });
        s.Add<IDescentItemGridSource>(new FakeWareGrid
        {
            Capacity = 8,
            Items =
            [
                new DescentItemView("Unidentified Revolver", DescentRarity.Vintage, 17,
                                    false, 6, 10, 0, false, false, false, false),
            ],
        });
        s.Add<IDescentVendorActions>(new FakeWareActions());
        var hover = new FakeWareHover();
        s.Add<IDescentVendorHover>(hover);
        s.Add<IDescentHintTip>(hover);
        s.Add<IDescentItemHover>(new FakeVendorItemHover());
        s.Add<IDescentVendorPortrait>(new FakeGalleryPortrait());
        return s.Create<Vendor>();
    }

    [UiGallery("OrbPair", "healthy")]
    private static UiScreen OrbsFull(UiSampleServices s)
    {
        s.Add<IDescentVitals>(new FakeVitals());
        return new UiPanelPreview("OrbPair", s.Create<OrbPair>());
    }

    [UiGallery("OrbPair", "low life, mid mana")]
    private static UiScreen OrbsLow(UiSampleServices s)
        => new UiPanelPreview("OrbPair", new OrbPair(new FakeVitals { Life = 18, LifeMax = 140, Mana = 44, ManaMax = 120 }));

    [UiGallery("BeltStrip", "two potions, one on cooldown")]
    private static UiScreen Belt(UiSampleServices s)
        => new UiPanelPreview("BeltStrip", new BeltStrip(new FakeBelt { SlotReady = false, Names = ["Healing Buff", "Vitality", "", ""], Counts = [4, 1, 0, 0] }));

    [UiGallery("PartyPill", "healthy, low, downed, hardcore, reviving")]
    private static UiScreen Pills(UiSampleServices s)
        => new UiPartPreview("PartyPill", (ref UiFrame ui) =>
        {
            var pill = new PartyPill();
            // Judged on the opaque Surface the HUD and the downed overlay
            // actually put them on: a Scrim fill hides the chip's contrast
            // from the lint (the downed lane found HC in Danger measured
            // 4.4:1 over an opaque Surface).
            using (ui.Panel().Fill(Tone.Surface).Pad(Space.S, Space.S))
            using (ui.Column(Space.S, Align.Start))
            {
                ui.Sub("a", pill, new PartyPillProps(new DescentPartyMember("Bugha", "#Descent_Class_Soldier", 18, 140, 140, false, true, false, 0, false), true, false));
                ui.Sub("b", pill, new PartyPillProps(new DescentPartyMember("Sentry", "#Descent_Class_Engineer", 16, 22, 130, false, false, true, 0, false), true, false));
                ui.Sub("c", pill, new PartyPillProps(new DescentPartyMember("Dr. Grordison", "#Descent_Class_Medic", 17, 0, 118, true, false, false, 1234, false), true, false));
                ui.Sub("d", pill, new PartyPillProps(new DescentPartyMember("Merasmus", "#Descent_Class_Spy", 17, 88, 110, true, false, false, 96, false), true, true));
            }
        });

    [UiGallery("XpBar", "mid, points waiting")]
    private static UiScreen Xp(UiSampleServices s)
        => new UiPanelPreview("XpBar", new XpBar(new FakeXp { AttributePoints = 2, SkillPoints = 1 }));

    [UiGallery("RankPips", "filled, empty, item-bonus")]
    private static UiScreen Pips(UiSampleServices s)
        => new UiPartPreview("RankPips", (ref UiFrame ui) =>
        {
            var pips = new RankPips();
            using (ui.Panel().Fill(Tone.Surface).Pad(Space.S, Space.S))
            using (ui.Column(Space.S, Align.Start))
            {
                ui.Sub("full", pips, new RankPipsProps(3, 3, 0));
                ui.Sub("some", pips, new RankPipsProps(1, 3, 0));
                ui.Sub("bonus", pips, new RankPipsProps(1, 3, 1));
            }
        });

    [UiGallery("NumberPlate", "stat, resistance")]
    private static UiScreen Plates(UiSampleServices s)
        => new UiPartPreview("NumberPlate", (ref UiFrame ui) =>
        {
            var plate = new NumberPlate();
            using (ui.Panel().Fill(Tone.Surface).Pad(Space.S, Space.S))
            using (ui.Column(Space.S, Align.Start))
            {
                ui.Sub("a", plate, new NumberPlateProps("45", DescentPaints.Raised, Text.Number, 96));
                ui.Sub("b", plate, new NumberPlateProps("50 %", (UiPaint)Tone.Text, Text.Number, 96));
                ui.Sub("c", plate, new NumberPlateProps("72 / 75", DescentPaints.Capped, Text.Number, 120));
            }
        });

    [UiGallery("AutomapCanvas", "revealed rooms")]
    private static UiScreen Map(UiSampleServices s)
        => new UiPanelPreview<AutomapProps>("AutomapCanvas", new AutomapCanvas(new FakeAutomap()), new AutomapProps(480, 480));

    [UiGallery("AutomapCanvas", "one storey")]
    private static UiScreen MapFlat(UiSampleServices s)
        => new UiPanelPreview<AutomapProps>("AutomapCanvas", new AutomapCanvas(new FakeAutomapFlat()), new AutomapProps(240, 240));
    [UiGallery("RarityChip", "the ladder")]
    private static UiScreen Ladder(UiSampleServices s)
        => new UiPartPreview("RarityChip", (ref UiFrame ui) =>
        {
            var chip = new RarityChip();
            using (ui.Row(Space.M, Align.Centre))
                for (DescentRarity r = 0; r <= DescentRarity.Australium; r++)
                    ui.Sub($"chip{(int)r}", chip, r);
        });

    [UiGallery("ItemGrid", "items, one row of overflow")]
    private static UiScreen Grid(UiSampleServices s)
        => new UiPanelPreview<ItemGridProps>("ItemGrid", new ItemGrid(),
            new ItemGridProps(new FakeGrid(), 6, null, null));

    [UiGallery("ItemTooltip", "hovered, unidentified")]
    private static UiScreen TipHidden(UiSampleServices s)
        => new UiPanelPreview("ItemTooltip", new ItemTooltip(new FakeHover { Unid = true }),
            layer: UiLayerKind.Menu);

    [UiGallery("ItemTooltip", "identified, durability, compare")]
    private static UiScreen TipFull(UiSampleServices s)
        => new UiPanelPreview("ItemTooltip", new ItemTooltip(new FakeHover()), layer: UiLayerKind.Menu);

    [UiGallery("AttributeRow", "base, raised, cursed, capped, plus")]
    private static UiScreen AttrRows(UiSampleServices s)
        => new UiPartPreview("AttributeRow", (ref UiFrame ui) =>
        {
            var attr = new AttributeRow();
            using (ui.Panel().Pad(Space.S, Space.S).Fill(Tone.Surface))
            using (ui.Column(Space.S, Align.Start))
            {
                ui.Sub("a0", attr, new AttributeRowProps(0, new DescentAttribute("#Descent_Attr_Brawn", 12, 12, 62, 0), false, null));
                ui.Sub("a1", attr, new AttributeRowProps(1, new DescentAttribute("#Descent_Attr_Smarts", 10, 14, 62, 0), false, null));
                ui.Sub("a2", attr, new AttributeRowProps(2, new DescentAttribute("#Descent_Attr_Aim", 11, 8, 62, 0), false, null));
                ui.Sub("a3", attr, new AttributeRowProps(3, new DescentAttribute("#Descent_Attr_Grit", 62, 66, 62, 0), true, null));
            }
        });

    [UiGallery("ResistRow", "mid, at cap")]
    private static UiScreen ResistRows(UiSampleServices s)
        => new UiPartPreview("ResistRow", (ref UiFrame ui) =>
        {
            var resist = new ResistRow();
            using (ui.Panel().Pad(Space.S, Space.S).Fill(Tone.Surface))
            using (ui.Column(Space.S, Align.Start))
            {
                ui.Sub("r0", resist, new ResistRowProps(DescentDamageTypes.Fire, 30, 75));
                ui.Sub("r1", resist, new ResistRowProps(DescentDamageTypes.Blast, 21, 75));
                ui.Sub("r2", resist, new ResistRowProps(DescentDamageTypes.Bullet, 44, 75));
                ui.Sub("r3", resist, new ResistRowProps(DescentDamageTypes.Shock, 75, 75));
            }
        });

    [UiGallery("NumberField", "fee rows")]
    private static UiScreen FeeFields(UiSampleServices s)
        => new UiPartPreview("NumberField", (ref UiFrame ui) =>
        {
            var field = new NumberField();
            using (ui.Panel().Pad(Space.S, Space.S).Fill(Tone.Surface))
            using (ui.Column(Space.S, Align.Start))
            {
                ui.Sub("f0", field, new NumberFieldProps("#Descent_Field_RepairFee", "120", (UiPaint)Tone.Text));
                ui.Sub("f1", field, new NumberFieldProps("#Descent_Field_RepairAll", "840", (UiPaint)Tone.Warning));
            }
        });

    [UiGallery("SkillCard", "buyable, pips, locked by tier, locked by prereq")]
    private static UiScreen SkillCards(UiSampleServices s)
        => new UiPartPreview("SkillCard", (ref UiFrame ui) =>
        {
            var skill = new SkillCard();
            using (ui.Row(Space.M, Align.Start))
            {
                ui.Sub("s0", skill, new SkillCardProps(
                    new DescentSkill(1, "#Descent_Skill_RocketJump", DescentSkillKind.Action,
                        2, 5, 1, 1, 1, 18, true, true, "12 / 10 s", "", "", true), null, null));
                ui.Sub("s1", skill, new SkillCardProps(
                    new DescentSkill(2, "#Descent_Skill_FamilyBullet", DescentSkillKind.UpgradeFamily,
                        1, 3, 0, 2, 8, 18, true, false, "", "", "#Descent_Skill_RocketJump", true), null, null));
                ui.Sub("s2", skill, new SkillCardProps(
                    new DescentSkill(3, "#Descent_Skill_Barrage", DescentSkillKind.Capstone,
                        0, 1, 0, 4, 24, 18, false, false, "", "", "", false), null, null));
                ui.Sub("s3", skill, new SkillCardProps(
                    new DescentSkill(4, "#Descent_Skill_ReloadLoop", DescentSkillKind.UpgradeTriggered,
                        0, 4, 0, 2, 1, 18, true, false, "", "", "", false), null, null));
            }
        });

    [UiGallery("MetalPockets", "three stacks")]
    private static UiScreen Metals(UiSampleServices s)
        => new UiPanelPreview<MetalPocketsProps>("MetalPockets", new MetalPockets(),
            new MetalPocketsProps(8, 3, 1));

    [UiGallery("CoinLabel", "amount, short")]
    private static UiScreen Coins(UiSampleServices s)
        => new UiPartPreview("CoinLabel", (ref UiFrame ui) =>
        {
            var coin = new CoinLabel();
            using (ui.Panel().Pad(Space.S, Space.S).Fill(Tone.Surface))
            using (ui.Column(Space.S, Align.Start))
            {
                ui.Sub("c0", coin, new CoinLabelProps(1043, false));
                ui.Sub("c1", coin, new CoinLabelProps(52, true));
            }
        });

    // The four hover screens draw the tip's ordinary states (title, one or
    // two copy lines, a good pinned value). The gallery takes the two the
    // screens never show: a cost pinned at a line (the red rungs) and a
    // value that reads as plain text, on a tip with no subtitle.
    [UiGallery("HintTip", "cost line, plain value")]
    private static UiScreen Hint(UiSampleServices s)
        => new UiPanelPreview("HintTip", new HintTip(new GalleryTip()), UiAnchor.Centre, UiLayerKind.Menu);

    /// <summary>A fixed tip: the part reads its subject through
    /// <see cref="IDescentHintTip"/>, so the gallery gives it one.</summary>
    private sealed class GalleryTip : IDescentHintTip
    {
        public bool Active => true;
        public string TitleToken => "#Descent_Attr_Grit";
        public string TitleData => string.Empty;
        public string SubtitleData => string.Empty;
        public int LineCount => 2;
        public string LineToken(int i) => i == 0 ? "#Descent_Attr_Grit_Detail" : string.Empty;
        public string LineData(int i) => i == 0 ? string.Empty : "Wearing it costs speed; the belt buckle keeps it honest.";
        public string LineGood(int i) => i == 0 ? "-3" : null!;
        public bool LineGoodNegative(int i) => true;
        public string ValueToken(int i) => string.Empty;
        public string ValueData(int i) => "cap 20";
        public bool ValueGood(int i) => false;
    }
}

/// <summary>The gallery's portrait source: the stall is talking, the engine
/// has sent the render (handle 41; the preview stamps it).</summary>
file sealed class FakeGalleryPortrait : IDescentVendorPortrait
{
    public bool Active => true;
    public int Texture => 41;
}

/// <summary>A fixed inspect subject for the modal's gallery: a Strange
/// shotgun with both engine surfaces sent. <see cref="Mounted"/> flips the
/// showing view, as the view-model's answer to the tab does.</summary>
file sealed class FakeInspect : IDescentInspect
{
    public bool Mounted { get; init; }
    public bool Showing => true;
    public DescentItemView? Item => new("The Back Scatter", DescentRarity.Strange, 21,
                                        true, 40, 82, 3, false, false, false, true);
    public bool ShowsMounted => Mounted;
    public bool HasWeaponSurface => true;
    public int WeaponTexture => 21;
    public bool HasMountedSurface => true;
    public int MountedTexture => 22;
    public string CharacterName => "Sentry";
}

/// <summary>The modal gallery's sink: the taps land here; the faked
/// view-model does not move, so the preview shows one view at a time.</summary>
file sealed class FakeInspectActions : IDescentInspectActions
{
    public int LastCell { get; private set; } = -1;
    public int Closes { get; private set; }
    public bool? LastMounted { get; private set; }
    public void OpenInspect(int slotOrCell) => LastCell = slotOrCell;
    public void Close() => Closes++;
    public void ShowMounted(bool mounted) => LastMounted = mounted;
}

/// <summary>A backpack for the grid gallery: rarity ladder, an unidentified, a broken
/// weapon, and two cells past capacity (the 2d withdraw-only tail).</summary>
file sealed class FakeGrid : IDescentItemGridSource
{
    private readonly DescentItemView?[] _items =
    [
        new DescentItemView("Force-A-Nature", DescentRarity.Vintage, 21, true, 65, 65, 3, false, false, false, false),
        new DescentItemView("The Back Scatter", DescentRarity.Stock, 14, true, 40, 82, 2, false, false, false, false),
        new DescentItemView("The Equalizer", DescentRarity.Stock, 24, true, 90, 90, 1, false, false, false, false),
        new DescentItemView("The Disciplinary Action", DescentRarity.Stock, 9, true, 0, 100, 1, false, true, false, false),
        new DescentItemView("Strange Resistance Band", DescentRarity.Strange, 15, true, 25, 25, 2, false, false, false, false),
        new DescentItemView("Unusual Taunt Hat", DescentRarity.Unusual, 7, true, 11, 11, 1, true, false, false, false),
        new DescentItemView("Australium Butterfly Knife", DescentRarity.Australium, 22, true, 30, 30, 4, false, false, false, false),
        new DescentItemView("The Crossup Contract", DescentRarity.Stock, 5, false, 10, 10, 0, false, false, false, false),
        new DescentItemView("The Sandman", DescentRarity.Vintage, 11, true, 33, 55, 3, false, false, true, false),
        new DescentItemView("DeMatterializer", DescentRarity.Stock, 18, true, 55, 55, 2, false, false, false, true),
        new DescentItemView("Aged Bonk Hat", DescentRarity.Vintage, 4, true, 8, 8, 1, false, false, false, false),
        null,
        new DescentItemView("The Catted Pilot", DescentRarity.Strange, 19, true, 45, 45, 3, false, false, false, false),
    ];

    public int CellCount => _items.Length;
    public int Capacity => 11;
    public bool Overflow => true;
    public DescentItemView? Cell(int i) => _items[i];
}

/// <summary>A hovered item for the tooltip gallery: affix rows, a curse, durability,
/// an unmet requirement, and the equipped-item compare with one rising and one
/// falling delta. <see cref="Unid"/> swaps to the D22 unidentified shape.</summary>
file sealed class FakeHover : IDescentItemHover
{
    public bool Unid { get; init; }

    public bool Hovering => true;
    public DescentItemView Item => Unid
        ? new DescentItemView("Strange Bonk! Helmet", DescentRarity.Strange, 12, false, 20, 20, 0, false, false, false, false)
        : new DescentItemView("The Back Scatter", DescentRarity.Vintage, 21, true, 40, 82, 3, false, false, false, true);
    public int AffixLineCount => Unid ? 0 : 3;
    public DescentAffixLine AffixLine(int i) => Unid ? default : i switch
    {
        0 => new DescentAffixLine("Critical Hit Chance", 12),
        1 => new DescentAffixLine("Reload Speed", 8),
        _ => new DescentAffixLine("Spread", -4),
    };
    public string Family => "shotgun";
    public string UnusualEffect => "";
    public string RequirementToken => "#Descent_Req_Brawn";
    public int RequirementValue => 14;
    public bool RequirementMet => false;
    public bool CompareHas => !Unid;
    public DescentItemView CompareItem =>
        new DescentItemView("The Screamer", DescentRarity.Stock, 19, true, 55, 55, 2, false, false, false, false);
    public int CompareAffixCount => Unid ? 0 : 3;
    public DescentAffixLine CompareAffix(int i) => Unid ? default : i switch
    {
        0 => new DescentAffixLine("Critical Hit Chance", 5),
        1 => new DescentAffixLine("Reload Speed", 11),
        _ => new DescentAffixLine("Spread", -4),
    };
}

/// <summary>A single-storey reveal: the level's whole known world at once.</summary>
file sealed class FakeAutomapFlat : IDescentAutomap
{
    private readonly (float X, float Y, float W, float H)[] Rooms =
    [
        (64, 448, 192, 192),
        (352, 352, 320, 192),
        (768, 320, 192, 128),
        (704, 640, 256, 192),
    ];

    public int RoomCount => Rooms.Length;
    public float RoomX(int r) => Rooms[r].X;
    public float RoomY(int r) => Rooms[r].Y;
    public float RoomW(int r) => Rooms[r].W;
    public float RoomH(int r) => Rooms[r].H;
    public int RoomStorey(int r) => 1;
    public bool RoomCurrentStorey(int r) => true;

    public int DoorStubCount => 3;
    public float DoorStubX(int i) => i switch { 0 => 256, 1 => 672, _ => 832 };
    public float DoorStubY(int i) => i switch { 0 => 512, 1 => 416, _ => 672 };
    public float DoorStubW(int i) => 4;
    public float DoorStubH(int i) => i == 2 ? 96 : 32;

    // Marker centres are ordinary map points; the canvas wraps each glyph and
    // stub in a zero-size box so the lint never compares them against the room
    // borders they sit on (see AutomapCanvas).
    private readonly DescentMarker[] Markers =
    [
        new(DescentMapMarker.Stairs, 3, 900, 700, 1),
        new(DescentMapMarker.Corpse, 1, 420, 420, 1),
        new(DescentMapMarker.Trap, 1, 560, 480, 1),
        new(DescentMapMarker.Portal, 0, 120, 536, 1),
    ];

    public int MarkerCount => Markers.Length;
    public DescentMarker Marker(int i) => Markers[i];
    public int StoreyCount => 1;
    public int HeroStorey => 1;
    public float HeroX => 300;
    public float HeroY => 448;
    public float HeroHeadingDeg => 0;
    public int FocusStorey => 1;
}
