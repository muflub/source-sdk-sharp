using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The TF2 HUD and its parts from fakes (the ui-screen loop's sample step):
// every state the in-game cross is ever in, built through the same container
// the game uses. Class names are ui.Data (game copy, case kept); every
// #TF2_Hud_* token lives in game/mod_tf2/resource/tf2_english.txt.

public static class TFHudSamples
{
    public static UiScreen Build(UiSampleServices s, FakeLocalPlayer? player = null, FakeRoundTimer? timer = null,
                                 FakeCapturePoints? points = null, FakeEscort? escort = null,
                                 FakeFlags? flags = null, FakeMedicCharge? medic = null, FakeItemMeter? meter = null,
                                 FakeSpy? spy = null,
                                 FakeCapProgress? progress = null, FakeTextureLibrary? textures = null,
                                 FakeChat? chat = null, FakeTeamStatus? teams = null,
                                 FakeDemomanPipes? pipes = null, FakeTeamGoal? goal = null)
    {
        s.Add<ITFLocalPlayer>(player ?? new FakeLocalPlayer());
        s.Add<ITFRoundTimer>(timer ?? new FakeRoundTimer());   // no clock by default
        s.Add<ITFCapturePoints>(points ?? new FakeCapturePoints());   // no points by default
        s.Add<ITFCapProgress>(progress ?? new FakeCapProgress());   // nobody on a point by default
        s.Add<ITFEscort>(escort ?? new FakeEscort());          // no track by default
        s.Add<ITFFlagStatus>(flags ?? new FakeFlags());        // no flags by default
        s.Add<ITFMedicCharge>(medic ?? new FakeMedicCharge()); // no medic by default
        s.Add<ITFItemMeter>(meter ?? new FakeItemMeter());     // no timed item by default
        s.Add<ITFSpy>(spy ?? new FakeSpy());                   // no spy by default
        s.Add<IChatSource>(chat ?? new FakeChat());            // no chat by default
        s.Add<ITFTeamStatus>(teams ?? new FakeTeamStatus());   // nobody on a team by default
        s.Add<ITFDemomanPipes>(pipes ?? new FakeDemomanPipes());   // no demoman by default
        s.Add<IUiLocalisation>(new SampleLocalise());
        s.Add<ITFTeamGoal>(goal ?? new FakeTeamGoal());        // no goal line by default
        // The art: a fact's recording fake answers its names; without one the
        // preview run's --content seed stands, and a seedless build draws the
        // fallbacks (the container answers a stateless none).
        s.HudArt = textures ?? s.HudArt;
        return s.Create<TFHud>();
    }

    [UiSample("healthy soldier")]
    private static UiScreen Healthy(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Soldier", Health = 200, MaxHealth = 200, MaxBuffedHealth = 300 });

    // The class art's blue arm (teambg_3, hudplayerclass.res): the portrait
    // and the character bg take the local team's images — and the ammo bg
    // its teambg_3, so the soldier carries a rocket launcher.
    [UiSample("healthy blu soldier")]
    private static UiScreen HealthyBlu(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Soldier", Team = Blue,
                              Health = 200, MaxHealth = 200, MaxBuffedHealth = 300,
                              ShowsAmmo = true, Clip = 20, Reserve = 32 });

    // Now sits mid-fade of HudHealthBonusPulse (100.1 % 0.4 = 0.1 of the
    // down ramp): the bonus image is on screen, at half strength.
    [UiSample("overhealed heavy", Now = 100.1)]
    private static UiScreen Overhealed(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Heavy", Health = 375, MaxHealth = 300, MaxBuffedHealth = 450 });

    // Now sits mid-ramp of HudHealthDyingPulse (100.3 % 0.25 = 0.05 of the
    // 0.075 ramp up): the bonus image is on screen at two-thirds strength.
    [UiSample("critical soldier", Now = 100.3)]
    private static UiScreen Critical(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Soldier", Health = 30, MaxHealth = 200, MaxBuffedHealth = 300 });

    // The pair at the max-number rule: "five missing shows /200, four does
    // not" (the tf_hud_playerstatus.cpp:851-858 boundary, tested as a fact).
    [UiSample("soldier missing five")]
    private static UiScreen MissingFive(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Soldier", Health = 195, MaxHealth = 200, MaxBuffedHealth = 300 });

    [UiSample("soldier missing four")]
    private static UiScreen MissingFour(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Soldier", Health = 196, MaxHealth = 200, MaxBuffedHealth = 300 });

    [UiSample("dead soldier")]
    private static UiScreen Dead(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Soldier", Health = 0, MaxHealth = 200, MaxBuffedHealth = 300 });

    // The ammo row's two arms and its silence (tf_hud_ammostatus.cpp:248-319):
    // the clip arm draws clip and pool, the no-clip arm (Clip −1, the
    // nAmmo1 < 0 read at :279-282) draws the pool as the one number, and a
    // weapon whose readout ShouldDraw hides draws no corner at all.
    [UiSample("rocket launcher clip and reserve")]
    private static UiScreen Rockets(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Soldier", Health = 120, MaxHealth = 200, MaxBuffedHealth = 300,
                              ShowsAmmo = true, Clip = 3, Reserve = 24 });

    [UiSample("flamethrower pool total")]
    private static UiScreen Flame(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Pyro", Health = 175, MaxHealth = 175, MaxBuffedHealth = 260,
                              ShowsAmmo = true, Clip = -1, Reserve = 200 });

    [UiSample("medigun hides the ammo")]
    private static UiScreen Medigun(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Medic", Health = 150, MaxHealth = 150, MaxBuffedHealth = 225,
                              Reserve = 199 });   // ShowsAmmo false: the adapter hides the row (:137-144)

    // The low-ammo warning (tf_hud_ammostatus.cpp:312-325): 20 in a full
    // stock of 60 — 20 < round(60 * 0.40) = 24 — grows the red bg about its
    // centre and pulses it. Now 100.1 sits in the pulse's held 255 (0.10
    // of the 0.25 s loop, hudanimations_tf.txt:252-264).
    [UiSample("low on rockets", Now = 100.1)]
    private static UiScreen LowAmmo(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Soldier", Health = 120, MaxHealth = 200, MaxBuffedHealth = 300,
                              ShowsAmmo = true, Clip = 0, Reserve = 20, MaxTotalAmmo = 60 });

    // One round at the threshold: 24 of 60 is exactly the 0.40 line, and
    // the strict < of :321 keeps the swap hidden.
    [UiSample("low ammo boundary")]
    private static UiScreen LowAmmoBoundary(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Soldier", Health = 120, MaxHealth = 200, MaxBuffedHealth = 300,
                              ShowsAmmo = true, Clip = 4, Reserve = 20, MaxTotalAmmo = 60 });

    // The engineer's metal account (CHudAccountPanel): the full 200 beside
    // the shotgun's readout, the stock still's corner; and spent, at the
    // zero the account keeps while the pool is out.
    [UiSample("engineer with metal")]
    private static UiScreen EngineerMetal(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Engineer", Health = 125, MaxHealth = 125, MaxBuffedHealth = 185,
                              ShowsAmmo = true, Clip = 6, Reserve = 32, Metal = 200 });

    [UiSample("engineer out of metal")]
    private static UiScreen EngineerSpent(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Engineer", Health = 125, MaxHealth = 125, MaxBuffedHealth = 185,
                              ShowsAmmo = true, Clip = 6, Reserve = 32, Metal = 0 });

    // The round clock's states (tf_time_panel.cpp:674-814): the countdown,
    // the count-up (:696-699), the setup label over the pre-round (:479-486)
    // and the overtime label at the run-out (:510-571), the m:ss of :720-724.
    // Health 120 puts it on the "/ max" side of the max-number rule — the
    // allocation arm of this row needs more than ten ops a frame.
    [UiSample("round clock counting down")]
    private static UiScreen ClockDown(UiSampleServices s) => Build(s,
        Soldier(120), new FakeRoundTimer { Present = true, ShowTimeRemaining = true, SecondsLeft = 105, TotalSeconds = 600 });

    [UiSample("round clock counting up")]
    private static UiScreen ClockUp(UiSampleServices s) => Build(s,
        Soldier(), new FakeRoundTimer { Present = true, ShowTimeRemaining = false, SecondsLeft = 540, TotalSeconds = 600 });

    [UiSample("round clock in setup")]
    private static UiScreen ClockSetup(UiSampleServices s) => Build(s,
        Soldier(), new FakeRoundTimer { Present = true, InSetup = true, ShowTimeRemaining = true, SecondsLeft = 30, TotalSeconds = 600 });

    [UiSample("round clock in overtime")]
    private static UiScreen ClockOver(UiSampleServices s) => Build(s,
        Soldier(), new FakeRoundTimer { Present = true, Overtime = true, ShowTimeRemaining = true, SecondsLeft = -5, TotalSeconds = 600 });

    // The stalemate band (SetExtraTimePanels :489-508) — its label pulses
    // red the same 0.6 s see-saw as overtime's.
    [UiSample("round clock in sudden death")]
    private static UiScreen ClockSuddenDeath(UiSampleServices s) => Build(s,
        Soldier(), new FakeRoundTimer { Present = true, Stalemate = true, ShowTimeRemaining = true, SecondsLeft = 0, TotalSeconds = 600 });

    // Waiting for players hides the setup band even with a setup timer up
    // (:573-588, "can't be waiting *AND* in setup").
    [UiSample("round clock waiting for players")]
    private static UiScreen ClockWaiting(UiSampleServices s) => Build(s,
        Soldier(), new FakeRoundTimer { Present = true, WaitingForPlayers = true, InSetup = true,
                                        ShowTimeRemaining = true, SecondsLeft = 90, TotalSeconds = 600 });

    // The server-time band (tf_time_panel.cpp:741-808): mp_timelimit left,
    // the convar set and the timer stating a max length.
    [UiSample("round clock server time limit")]
    private static UiScreen ClockServerLimit(UiSampleServices s) => Build(s,
        Soldier(120), new FakeRoundTimer { Present = true, ShowTimeRemaining = true, SecondsLeft = 105,
                                           TotalSeconds = 600, ServerTimeLimit = 1200, ServerTimeLeft = 755,
                                           ShowServerLimit = true });

    // The elapsed fraction past the res's percent_warning 0.75: the wedge
    // tints TimerProgress.Warning (CTFProgressBar::Paint :230-241).
    [UiSample("round clock at the time warning")]
    private static UiScreen ClockWarning(UiSampleServices s) => Build(s,
        Soldier(120), new FakeRoundTimer { Present = true, ShowTimeRemaining = true, SecondsLeft = 55, TotalSeconds = 240 });

    // The red panel: SetTeamBackground picks the red art when the local
    // player is RED (tf_time_panel.cpp:334-342); the other samples sit on
    // the blue arm (team 0 or BLU both read blue, :339-342).
    [UiSample("round clock on the red team")]
    private static UiScreen ClockRed(UiSampleServices s) => Build(s,
        Soldier(120), new FakeRoundTimer { Present = true, ShowTimeRemaining = true, SecondsLeft = 105, TotalSeconds = 600, Team = Red });

    // The control point icons (hud_controlpointicons.cpp): the owner colour,
    // a cap in progress with its cappers, a blocked cap, the map's pyramid
    // layout with a locked point, and a point off the round drawing nothing.
    [UiSample("blu owns the middle point")]
    private static UiScreen OwnedPoint(UiSampleServices s) => Build(s,
        Soldier(120), points: new FakeCapturePoints
        {
            NumPoints = 1,
            Owner = [Blue],
        });

    [UiSample("red caps the middle point")]
    private static UiScreen CappingPoint(UiSampleServices s) => Build(s,
        // Mid-round (elapsed 0.5): the capture is the subject, and the
        // warning clock's red is "round clock counting down"'s.
        Soldier(120), new FakeRoundTimer { Present = true, ShowTimeRemaining = true, SecondsLeft = 300, TotalSeconds = 600 },
        points: new FakeCapturePoints
        {
            NumPoints = 1,
            Owner = [Blue],
            CappingTeams = [Red],
            Progress = [0.4f],
            CapperCounts = [[0, 0, 2]],   // two RED (team 2) cappers on the point
        });

    [UiSample("red caps and nobody pushes")]
    private static UiScreen BlockedPoint(UiSampleServices s) => Build(s,
        Soldier(120), points: new FakeCapturePoints
        {
            NumPoints = 1,
            Owner = [Blue],
            CappingTeams = [Red],
            Progress = [0.6f],
            Blocks = [true],
        });

    // The per-point countdowns (hud_controlpointicons.cpp:579-641,
    // :1843-1876): the timer digits and the timer art on the first point,
    // the last-five unlock countdown on the second, none on the third.
    // Now 100.3: the 7-second timer's tenth is odd, so the flash shows red.
    [UiSample("point timer counts down", Now = 100.3)]
    private static UiScreen PointTimer(UiSampleServices s) => Build(s,
        Soldier(120), points: new FakeCapturePoints
        {
            NumPoints = 3,
            Owner = [Blue, 0, Blue],
            TimerLeft = [7.2f],
            UnlockLeft = [0f, 4.4f],
        });

    [UiSample("pyramid with a locked point")]
    private static UiScreen Pyramid(UiSampleServices s) => Build(s,
        Soldier(120), points: new FakeCapturePoints
        {
            NumPoints = 4,
            CapLayout = "3,1 2",
            Owner = [Blue, Red, Blue, 0],
            Locks = [false, false, false, true],
        });

    [UiSample("pyramid with a point off the round")]
    private static UiScreen PyramidPartial(UiSampleServices s) => Build(s,
        Soldier(120), points: new FakeCapturePoints
        {
            NumPoints = 4,
            CapLayout = "3,1 2",
            Owner = [Blue, Red, Blue, 0],
            OffRound = [false, false, true, false],
        });

    // The cap teardrop (hud_controlpointicons.cpp:1499-1646): the local
    // player stands on the point — their team's cap fills the sweep, and on
    // a blocked point the ladder's text rides the plate instead.
    [UiSample("cap teardrop fills")]
    private static UiScreen TeardropFills(UiSampleServices s) => Build(s,
        Soldier(120), points: new FakeCapturePoints
        {
            NumPoints = 1,
            Owner = [Blue],
            CappingTeams = [Red],
            Progress = [0.4f],
        },
        progress: new FakeCapProgress { Touched = 0, Bars = [true], Progresses = [0.4f] });

    [UiSample("cap teardrop names why")]
    private static UiScreen TeardropText(UiSampleServices s) => Build(s,
        Soldier(120), points: new FakeCapturePoints
        {
            NumPoints = 1,
            Owner = [Blue],
            CappingTeams = [Red],
            Blocks = [true],
        },
        progress: new FakeCapProgress { Touched = 0, Bars = [false], Texts = ["#Team_Capture_Blocked"] });

    private static FakeLocalPlayer Soldier(int health = 200) =>
        new() { ClassName = "Soldier", Health = health, MaxHealth = 200, MaxBuffedHealth = 300 };

    // The escort track (tf_hud_escort.cpp): the cart riding at its progress
    // with its pushers, the recede countdown, rolling backwards, blocked,
    // and the track alarm. Checkpoints at the ends and the quarters; one
    // hill under the first stretch.
    [UiSample("cart rolls down the track")]
    private static UiScreen Rolling(UiSampleServices s) => Build(s,
        Soldier(120),
        escort: new FakeEscort
        {
            Progress = 0.4f,
            Cappers = 2,
            SpeedLevel = 1,
            Distances = [0f, 0.25f, 0.5f, 1f],
            HillSpans = [(0.05f, 0.3f)],
        });

    [UiSample("cart waits with time to recede")]
    private static UiScreen Receding(UiSampleServices s) => Build(s,
        Soldier(120),
        escort: new FakeEscort
        {
            Progress = 0.75f,
            RecedeSeconds = 10f,
            Distances = [0f, 0.25f, 0.5f, 1f],
            HillSpans = [(0.05f, 0.3f)],
        });

    [UiSample("cart rolls backwards")]
    private static UiScreen Backwards(UiSampleServices s) => Build(s,
        Soldier(120),
        escort: new FakeEscort
        {
            Progress = 0.6f,
            SpeedLevel = -1,
            Distances = [0f, 0.25f, 0.5f, 1f],
        });

    [UiSample("cart blocked with nobody pushing")]
    private static UiScreen Blocked(UiSampleServices s) => Build(s,
        Soldier(120),
        escort: new FakeEscort
        {
            Progress = 0.4f,
            Distances = [0f, 0.25f, 0.5f, 1f],
            Blocks = [false, false, true, false],
        });

    [UiSample("cart alarms")]
    private static UiScreen Alarming(UiSampleServices s) => Build(s,
        Soldier(120),
        escort: new FakeEscort
        {
            Progress = 0.4f,
            Cappers = 1,
            SpeedLevel = 1,
            Alarm = true,
            MultipleTrains = true,
            Distances = [0f, 0.25f, 0.5f, 1f],
        });

    // A single-train map's alarm: stock's UpdateAlarmAnimations hides the
    // alert and stops the pulse (:656-691), so the cart paints no danger.
    [UiSample("cart alarms on one train")]
    private static UiScreen AlarmingSingleTrain(UiSampleServices s) => Build(s,
        Soldier(120),
        escort: new FakeEscort
        {
            Progress = 0.4f,
            Cappers = 1,
            SpeedLevel = 1,
            Alarm = true,
            Distances = [0f, 0.25f, 0.5f, 1f],
        });

    // The medic's charge meter (tf_hud_mediccharge.cpp): the meter building
    // with its percent and its patient, the ready pulse on a full one, and
    // the release draining it (the pulse stopped).
    [UiSample("medic builds the ubercharge")]
    private static UiScreen MedicCharging(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Medic", Health = 150, MaxHealth = 150, MaxBuffedHealth = 225 },
        medic: new FakeMedicCharge { Present = true, Charge = 0.55f, HealTarget = "Heavy" });

    // In the pulse's dark half (100.6 / 0.3 odd, the 0.6 s HudMedicCharged loop).
    [UiSample("medic uber ready", Now = 100.6)]
    private static UiScreen MedicReady(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Medic", Health = 150, MaxHealth = 150, MaxBuffedHealth = 225 },
        medic: new FakeMedicCharge { Present = true, Charge = 1f });

    [UiSample("medic releases the uber")]
    private static UiScreen MedicReleasing(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Medic", Health = 150, MaxHealth = 150, MaxBuffedHealth = 225 },
        medic: new FakeMedicCharge { Present = true, Charge = 0.6f, Releasing = true, HealTarget = "Scout" });

    // A timed item half charged: the gold bar to its middle, the item's own
    // word beside it (Bonk!'s label, meter_label).
    [UiSample("bonk half charged")]
    private static UiScreen BonkHalfCharged(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Pyro", Health = 175, MaxHealth = 175, MaxBuffedHealth = 230 },
        meter: new FakeItemMeter { Present = true, Charge = 0.5f, Label = "Bonk!" });

    // The spy's meters: the cloak building, the cloak full, and the disguise
    // status in the imitated team's tone (tf_hud_itemeffectmeter.cpp,
    // tf_hud_disguise_status.cpp).
    [UiSample("spy builds the cloak")]
    private static UiScreen SpyCloaking(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Spy", Health = 125, MaxHealth = 125, MaxBuffedHealth = 185 },
        spy: new FakeSpy { CloakPresent = true, Cloak = 0.5f });

    [UiSample("spy cloak full")]
    private static UiScreen SpyCloaked(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Spy", Health = 125, MaxHealth = 125, MaxBuffedHealth = 185 },
        spy: new FakeSpy { CloakPresent = true, Cloak = 1f });

    [UiSample("spy disguised as a red soldier")]
    private static UiScreen SpyDisguised(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Spy", Health = 125, MaxHealth = 125, MaxBuffedHealth = 185 },
        spy: new FakeSpy { CloakPresent = true, Cloak = 0.3f, Disguised = true,
                           DisguiseClassName = "Soldier", DisguiseTeam = Red });

    // The chat at stock's HudChat box (hudlayout.res:980-992): a team line,
    // a faded one, and a dead one.
    [UiSample("chat over the corner")]
    private static UiScreen ChatLines(UiSampleServices s) => Build(s,
        Soldier(120),
        chat: new FakeChat
        {
            Lines =
            [
                new ChatLine("Soldier", 2, "hold the tunnel, I have the flag", false, 1f),
                new ChatLine("Medic", 2, "coming to you", true, 1f),
                new ChatLine("Sniper", 3, "watch the bridge", false, 0.4f),
                new ChatLine("", 0, "Red picked up the intelligence", false, 1f, true),
            ],
        });

    // The CTF flag row (tf_hud_flagstatus.cpp): both briefcases home with
    // their capture counts and the caps target, one carried, one dropped,
    // and the local player carrying the red one (the CarriedImage swap).
    [UiSample("both flags home")]
    private static UiScreen FlagsHome(UiSampleServices s) => Build(s,
        Soldier(120),
        flags: new FakeFlags
        {
            HasFlag = [false, false, true, true],
            CaptureCounts = [0, 0, 2, 1],
            CapsTarget = 3,
            Bearings = [0f, 0f, 135f, -60f],
        });

    [UiSample("red flag stolen")]
    private static UiScreen FlagStolen(UiSampleServices s) => Build(s,
        Soldier(120),
        flags: new FakeFlags
        {
            HasFlag = [false, false, true, true],
            Statuses = [0, 0, Stolen, Home],
            Bearings = [0f, 0f, 45f, 0f],
        });

    [UiSample("blue flag dropped")]
    private static UiScreen FlagDropped(UiSampleServices s) => Build(s,
        Soldier(120),
        flags: new FakeFlags
        {
            HasFlag = [false, false, true, true],
            Statuses = [0, 0, Home, Dropped],
            Bearings = [0f, 0f, 0f, 210f],
        });

    [UiSample("carrying the red flag")]
    private static UiScreen FlagCarried(UiSampleServices s) => Build(s,
        Soldier(120),
        flags: new FakeFlags
        {
            HasFlag = [false, false, true, true],
            Statuses = [0, 0, Stolen, Home],
            CarriedLocal = [false, false, true, false],
            ZoneBearingValue = 30f,
        });

    // The no-arrow compasses (CTFArrowPanel::OnTick :127-159): in-eye of the
    // red carrier, RED's disc loses its needle while BLU's keeps it.
    [UiSample("in-eye of the red carrier")]
    private static UiScreen InEyeOfCarrier(UiSampleServices s) => Build(s,
        Soldier(120),
        flags: new FakeFlags
        {
            HasFlag = [false, false, true, true],
            Statuses = [0, 0, Stolen, Home],
            Bearings = [0f, 0f, 20f, 0f],
            SpectatedCarriers = [false, false, true, false],
        });

    // The top team-status bar (tf_teamstatus.cpp): the overhealed local
    // soldier innermost of the red chips, a dead team-mate counting its
    // respawn, and the enemy rows silhouetted, barless.
    [UiSample("the team bar both sides")]
    private static UiScreen TeamBar(UiSampleServices s) => Build(s,
        Soldier(120),
        teams: new FakeTeamStatus
        {
            LocalIndex = 2,
            Players =
            [
                new TFTeamStatusPlayer(1, Blue, 1, true, 100, 125, 185, -1),
                new TFTeamStatusPlayer(2, Red, 3, true, 240, 200, 300, -1),
                new TFTeamStatusPlayer(3, Blue, 6, true, 380, 300, 450, -1),
                new TFTeamStatusPlayer(4, Red, 5, true, 130, 150, 300, -1),
                new TFTeamStatusPlayer(5, Red, 6, false, -1, 300, 450, 20),
                new TFTeamStatusPlayer(6, Blue, 8, false, -1, 125, 185, -1),
            ],
        });

    // Seven a side: both rows shrink past the res's max_size to share
    // max_expand at the 6..12 gap ramp (PerformLayout :452, :461).
    [UiSample("the team bar crowded")]
    private static UiScreen TeamBarCrowded(UiSampleServices s) => Build(s,
        Soldier(120),
        teams: new FakeTeamStatus { LocalIndex = 2, Players = Crowd(7) });

    /// <summary>Seven per side, the local soldier the red innermost.</summary>
    private static TFTeamStatusPlayer[] Crowd(int perTeam)
    {
        var rows = new TFTeamStatusPlayer[perTeam * 2];
        byte[] classes = [1, 3, 6, 5, 4, 7, 2];
        for (int i = 0; i < perTeam; i++)
        {
            int blue = 1 + i * 2;
            int red = 2 + i * 2;
            bool dead = i == perTeam - 1;
            rows[i] = new TFTeamStatusPlayer(blue, Blue, classes[i % classes.Length], !dead,
                dead ? -1 : 100 + i * 10, 125, 185, dead ? 12 : -1);
            rows[perTeam + i] = new TFTeamStatusPlayer(red, Red, classes[(i + 2) % classes.Length], true,
                150 + i * 10, 200, 300, -1);
        }

        return rows;
    }

    // The pipes corner's two arms (tf_hud_demomanpipes.cpp:138-144): bombs
    // out counts them by the live icon; the empty tray keeps the corner, the
    // same label dull by the faded one.
    [UiSample("demoman pipes out")]
    private static UiScreen PipesOut(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Demoman", Team = Red, Health = 160, MaxHealth = 160 },
        pipes: new FakeDemomanPipes { Present = true, Count = 3 });

    [UiSample("demoman tray empty")]
    private static UiScreen PipesEmpty(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Demoman", Team = Blue, Health = 160, MaxHealth = 160 },
        pipes: new FakeDemomanPipes { Present = true, Count = 0 });

    // The map-goal hint (tf_hud_teamgoal.cpp): 2fort's setup line on the red
    // panel, and the blue arm of the same sentence.
    [UiSample("the goal hint, red")]
    private static UiScreen GoalRed(UiSampleServices s) => Build(s,
        Soldier(120),
        goal: new FakeTeamGoal
        {
            Visible = true,
            Team = Red,
            Text = "Grab the Intelligence Briefcase from the enemy basement. Return to your basement to win!",
            Image = "hud/hud_icon_defend",
        });

    [UiSample("the goal hint, blu")]
    private static UiScreen GoalBlue(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { ClassName = "Soldier", Team = Blue, Health = 200, MaxHealth = 200, MaxBuffedHealth = 300 },
        goal: new FakeTeamGoal
        {
            Visible = true,
            Team = Blue,
            Text = "Grab the Intelligence Briefcase from the enemy basement. Return to your basement to win!",
            Image = "hud/hud_icon_attack",
        });

    // The raw TF team numbers (tf_shareddefs.h): TF_TEAM_BLUE 3, TF_TEAM_RED 2.
    private const int Blue = 3;
    private const int Red = 2;
    // ETFFlagStatus (tf_shareddefs.h).
    private const int Home = 0;
    private const int Stolen = 1;
    private const int Dropped = 2;
}

/// <summary>The map's point table as the HUD reads it; unset arrays answer
/// the table's defaults (unassigned owners, everyone in the round).</summary>
public sealed class FakeCapturePoints : ITFCapturePoints
{
    public int NumPoints { get; init; }
    public string CapLayout { get; init; } = "";
    public int[] Owner { get; init; } = [];
    public int[] CappingTeams { get; init; } = [];
    public bool[] Locks { get; init; } = [];
    public bool[] Blocks { get; init; } = [];
    public bool[] OffRound { get; init; } = [];
    public int[][] CapperCounts { get; init; } = [];
    public float[] Progress { get; init; } = [];
    public float[] TimerLeft { get; init; } = [];
    public float[] UnlockLeft { get; init; } = [];

    public bool Visible(int point) => OffRound.Length <= point || !OffRound[point];

    public bool InMiniRound(int point) => !(OffRound.Length > point && OffRound[point]);

    public int OwningTeam(int point) => Owner.Length > point ? Owner[point] : 0;

    public int CappingTeam(int point) => CappingTeams.Length > point ? CappingTeams[point] : 0;

    public float CapProgress(int point) => Progress.Length > point ? Progress[point] : 0f;

    public float TimerSeconds(int point) => TimerLeft.Length > point ? TimerLeft[point] : 0f;

    public float UnlockSeconds(int point) => UnlockLeft.Length > point ? UnlockLeft[point] : 0f;

    public bool Blocked(int point) => Blocks.Length > point && Blocks[point];

    public bool Locked(int point) => Locks.Length > point && Locks[point];

    public int Cappers(int point, int team)
        => CapperCounts.Length > point && CapperCounts[point].Length > team ? CapperCounts[point][team] : 0;
}

/// <summary>The cap teardrop's reads as the panel draws them; at rest
/// nobody stands on a point, so the teardrop draws nothing.</summary>
public sealed class FakeCapProgress : ITFCapProgress
{
    public int Touched { get; init; } = -1;
    public bool[] Bars { get; init; } = [];
    public float[] Progresses { get; init; } = [];
    public string[] Texts { get; init; } = [];

    public bool ShowsBar(int point) => Bars.Length > point && Bars[point];

    public float Progress(int point) => Progresses.Length > point ? Progresses[point] : 0f;

    public string Text(int point) => Texts.Length > point ? Texts[point] : "";
}

/// <summary>The escort track as the HUD reads it; at rest there is no
/// track (no point carries a path distance), so the row draws nothing.</summary>
public sealed class FakeEscort : ITFEscort
{
    public float Progress { get; init; }
    public int Cappers { get; init; }
    public int SpeedLevel { get; init; }
    public float RecedeSeconds { get; init; }
    public bool Alarm { get; init; }
    public bool MultipleTrains { get; init; }
    public int Points => Distances.Length;
    public float[] Distances { get; init; } = [];
    public bool[] Blocks { get; init; } = [];
    public int[] Owners { get; init; } = [];
    public (float Start, float End)[] HillSpans { get; init; } = [];
    public bool[] Downhills { get; init; } = [];

    public bool Present
    {
        get
        {
            foreach (float d in Distances)
                if (d > 0f)
                    return true;
            return false;
        }
    }

    public bool PointOnTrack(int point) => point < Points;

    public float PathDistance(int point) => point < Points ? Distances[point] : 0f;

    public bool Blocked(int point) => Blocks.Length > point && Blocks[point];

    public int OwningTeam(int point) => Owners.Length > point ? Owners[point] : 0;

    public int NumHills => HillSpans.Length;

    public bool Hill(int hill, out float start, out float end)
    {
        if (hill < 0 || hill >= HillSpans.Length)
        {
            (start, end) = (0f, 0f);
            return false;
        }

        (start, end) = HillSpans[hill];
        return true;
    }

    public bool HillDownhill(int hill) => Downhills.Length > hill && Downhills[hill];
}

/// <summary>The teams' flags as the HUD reads them; at rest no team
/// fields a flag, so the row draws nothing.</summary>
public sealed class FakeFlags : ITFFlagStatus
{
    public bool[] HasFlag { get; init; } = new bool[4];
    public int[] Statuses { get; init; } = new int[4];
    public float[] Resets { get; init; } = new float[4];
    public int[] CaptureCounts { get; init; } = new int[4];
    public int CapsTarget { get; init; }
    public float[] Bearings { get; init; } = new float[4];
    public bool[] CarriedLocal { get; init; } = new bool[4];
    public bool[] SpectatedCarriers { get; init; } = new bool[4];
    public float ZoneBearingValue { get; init; }

    public bool Present => HasFlag[3] || HasFlag[2];

    public bool Has(int team) => HasFlag[team];

    public int Status(int team) => Statuses[team];

    public float ResetSeconds(int team) => Resets[team];

    public int Captures(int team) => CaptureCounts[team];

    public int CapsPerRound => CapsTarget;

    public float Bearing(int team) => Bearings[team];

    public bool CarriedByLocal(int team) => CarriedLocal[team];

    public bool SpectatingCarrier(int team) => SpectatedCarriers[team];

    public float ZoneBearing => ZoneBearingValue;
}

public sealed class FakeLocalPlayer : ITFLocalPlayer
{
    public string ClassName { get; init; } = "";
    public int Health { get; init; }
    public int MaxHealth { get; init; }
    public int MaxBuffedHealth { get; init; }
    public int Overheal => System.Math.Max(0, Health - MaxHealth);

    /// <summary>RED (the raw TF team number 2) unless the sample says
    /// otherwise: the stock .res arms draw the red art by default
    /// (teambg_2 before teambg_3).</summary>
    public int Team { get; init; } = 2;

    /// <summary>The weapon reads, at rest for a hero with empty hands:
    /// no readout, no clip, an empty pool.</summary>
    public bool ShowsAmmo { get; init; }
    public int Clip { get; init; } = -1;
    public int Reserve { get; init; }
    public int MaxTotalAmmo { get; init; }

    /// <summary>The metal account, at the spawn locker's full 200 for a
    /// fresh engineer unless the sample says less.</summary>
    public int Metal { get; init; }
}

/// <summary>The round timer as the election left it; at rest there is no
/// timer up, so the clock draws nothing.</summary>
public sealed class FakeRoundTimer : ITFRoundTimer
{
    public bool Present { get; init; }
    public bool InSetup { get; init; }
    public bool Overtime { get; init; }
    public bool Stalemate { get; init; }
    public bool WaitingForPlayers { get; init; }
    public bool IsPaused { get; init; }
    public bool ShowTimeRemaining { get; init; }
    public int SecondsLeft { get; init; }
    public int TotalSeconds { get; init; }
    public int Team { get; init; }
    public bool StopWatch { get; init; }
    public int ServerTimeLeft { get; init; }
    public int ServerTimeLimit { get; init; }
    public bool ShowServerLimit { get; init; }
}

/// <summary>The HUD art fake: records every name a screen asks and
/// answers a distinct handle for the names given (0 — the fallback arm —
/// for the rest). <see cref="About"/> answers null: the panels read no
/// size off the art, they draw it into the res's own rectangle.</summary>
public sealed class FakeTextureLibrary(params string[] answered) : IUiTextureLibrary
{
    /// <summary>Every name Find was called with, in order — the fact's
    /// instrument: the screen asked for stock's names.</summary>
    /// <summary>Recording keeps the fact's instrument (Asked) growing with
    /// every frame a screen renders; the sample screens render hundreds of
    /// frames through one fake, so they ask with <c>Record = false</c>.</summary>
    public bool Record { get; init; } = true;

    /// <summary>Every name Find was called with, in order — the fact's
    /// instrument: the screen asked for stock's names.</summary>
    public readonly List<string> Asked = [];

    private int _handles;

    public int Find(string name)
    {
        if (Record)
            Asked.Add(name);
        return Array.IndexOf(answered, name) >= 0 ? ++_handles : 0;
    }

    public int Material(string name) => 0;

    public UiTextureInfo? About(string name) => null;

    public UiTextureIcon? Icon(string name)
    {
        if (Record)
            Asked.Add(name);
        return null;
    }
}

/// <summary>The medic's medigun as the meter reads it; at rest no medic
/// holds one, so the meter draws nothing.</summary>
public sealed class FakeMedicCharge : ITFMedicCharge
{
    public bool Present { get; init; }
    public float Charge { get; init; }
    public bool Releasing { get; init; }
    public string HealTarget { get; init; } = "";
}

/// <summary>The timed item's charge bar as the carrier states it; at rest no
/// item carries one, so the corner draws nothing.</summary>
public sealed class FakeItemMeter : ITFItemMeter
{
    public bool Present { get; init; }
    public float Charge { get; init; }
    public string Label { get; init; } = "";
}

/// <summary>The demoman's sticky count as the tray publishes it; at rest no
/// demoman stands, so the corner draws nothing.</summary>
public sealed class FakeDemomanPipes : ITFDemomanPipes
{
    public bool Present { get; init; }
    public int Count { get; init; }
}

/// <summary>The spy's meters as the carrier states them; at rest no spy
/// presents and no disguise is on.</summary>
public sealed class FakeSpy : ITFSpy
{
    public bool CloakPresent { get; init; }
    public float Cloak { get; init; }
    public bool Disguised { get; init; }
    public string DisguiseClassName { get; init; } = "";
    public int DisguiseTeam { get; init; }
}

/// <summary>The map's goal line as the panel reads it; at rest no goal
/// stands, so the hint draws nothing.</summary>
public sealed class FakeTeamGoal : ITFTeamGoal
{
    public bool Visible { get; init; }
    public string Text { get; init; } = "";
    public string Image { get; init; } = "";
    public int Team { get; init; }
}

/// <summary>The chat feed; empty unless the sample says lines.</summary>
public sealed class FakeChat : IChatSource
{
    public List<ChatLine> Lines { get; init; } = [];

    public int Count => Lines.Count;

    public ChatLine Line(int i) => Lines[i];
}

/// <summary>The team-status rows; empty unless the sample lists players,
/// and then the local player is on a game team as the rows say.</summary>
public sealed class FakeTeamStatus : ITFTeamStatus
{
    public TFTeamStatusPlayer[] Players { get; init; } = [];

    /// <summary>The local player's entity index.</summary>
    public int LocalIndex { get; init; }

    public bool Present
    {
        get
        {
            foreach (var row in Players)
                if (row.EntityIndex == LocalIndex && row.Team >= 2)
                    return true;
            return false;
        }
    }

    public int Count => Players.Length;

    public TFTeamStatusPlayer Player(int index) => Players[index];

    public int LocalPlayerIndex => LocalIndex;
}
