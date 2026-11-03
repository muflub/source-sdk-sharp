extern alias client;

using System.Text;

using SourceSharp.Tests;

using Xunit;

using Abi = client::SourceSharp.Abi;
using ClientFilesShim = client::SourceSharp.Abi.IEngineFiles;
using ClientLog = client::SourceSharp.Abi.RecordingLog;
using ClientUi = client::SourceSharp.Client.Ui;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using Nav = client::TF2.Client.Ui.ITFMenuNavigation;
using Ui = client::TF2.Client.Ui.TFLoadoutUi;
using ClientSchema = client::TF2.Shared.Econ.ItemSchema;
using Reply = client::TF2.Shared.Econ.LoadoutReply;
using Hook = client::TF2.Shared.Econ.ITFLoadoutReplyHook;
using Ask = client::TF2.Shared.Econ.LoadoutAsk;

// The contracts ride the interface assembly, which is global (not the
// client alias): the aliases exist for the shared source compiled per side.
using TF2.Client.Ui;

namespace TF2.Tests.Client;

/// <summary>
/// The loadout screen's game half (D2): the <see cref="Hook"/> the
/// <c>TF2:loadout</c> reply arrives on fills what
/// <c>client::TF2.Client.Ui.ITFLoadoutSource</c> answers — the class's
/// visible slots with the equipped item's name token and inventory image,
/// resolved on the client's schema — and what
/// <c>client::TF2.Client.Ui.ITFLoadoutActions</c> sends: the one-byte ask
/// over a SourceSharp net message, the named <c>tf2_equip</c> console
/// line, and the closing click.
/// </summary>
public sealed class TFLoadoutUiTests
{
    [Fact]
    public void BeforeAReplyTheSourceHasNothing()
    {
        var rig = new Rig();

        Assert.Equal(0, rig.Source.Class);
        Assert.Equal(0, rig.Source.Slots);
        Assert.Equal(0, rig.Source.OwnedCount);
    }

    [Fact]
    public void TheSourceShowsTheServerReply()
    {
        var rig = new Rig();
        var reply = new Reply
        {
            Class = 3,
            EquippedSlots = [0, 1, 2, 7, 8, 10, 9],
            EquippedDefindexes = [18, 10, 6, Reply.NoItem, Reply.NoItem, Reply.NoItem, Reply.NoItem],
            OwnedDefindexes = [441],
            OwnedSlots = [0],
        };

        ((Hook)rig.Ui).OnLoadoutReply(reply);

        Assert.Equal(3, rig.Source.Class);
        Assert.Equal(7, rig.Source.Slots);
        Assert.Equal(0, rig.Source.Slot(0).Slot);
        Assert.Equal("#LoadoutSlot_Primary", rig.Source.Slot(0).Label);
        Assert.Equal("#TF_Weapon_RocketLauncher", rig.Source.Slot(0).ItemName);
        Assert.Equal("backpack/weapons/w_models/w_rocketlauncher", rig.Source.Slot(0).Icon);
        // A slot with nothing (no base item, nothing equipped) is empty.
        Assert.Equal("", rig.Source.Slot(3).ItemName);
        Assert.Equal("#LoadoutSlot_Misc", rig.Source.Slot(3).Label);
        // The owned items, with their slot for the screen's filter.
        Assert.Equal(1, rig.Source.OwnedCount);
        Assert.Equal((441, 0, "#TF_CowMangler"), (rig.Source.Owned(0).Defindex, rig.Source.Owned(0).Slot, rig.Source.Owned(0).Name));
    }

    [Fact]
    public void SelectAsksTheServerForTheClass()
    {
        var rig = new Rig();

        ((ITFLoadoutActions)rig.Ui).Select(3);

        var sent = Assert.Single(rig.Engine.Calls, c => c.Member == "Networking.SendServerMessage");
        Assert.Equal(Ask.Key, sent.Arg(0));
        Assert.Equal(new byte[] { 3 }, (byte[])sent.Arg(1)!);
    }

    [Fact]
    public void AnEquipSendsTheNamedCommandLine()
    {
        var rig = new Rig();

        ((ITFLoadoutActions)rig.Ui).Equip(3, 0, 441);
        ((ITFLoadoutActions)rig.Ui).Equip(3, 0, -1);

        Assert.Equal(["tf2_equip soldier primary 441", "tf2_equip soldier primary none"], rig.Sent.Lines);
    }

    [Fact]
    public void CloseClosesTheScreen()
    {
        var rig = new Rig();

        ((ITFLoadoutActions)rig.Ui).Close();

        Assert.Equal(["tf2loadout"], rig.Nav.Closed);
    }

    // The quality prefix over the displayed item name, over a small inline
    // schema: the schema qualities table's word in front of the item's own
    // name token, the Australium word after it when the item carries the
    // attribute (the stock arms in econ_item_description.cpp:748-789,
    // :822-832). The name part rides the mod's token chain, so the token
    // file below answers the token the fixture items carry.

    private const string QualityItems =
        "\"3\" { \"name\" \"RL\" \"item_class\" \"tf_weapon_rocketlauncher\" \"item_name\" \"#TF_Weapon_RocketLauncher\" } " +
        "\"4\" { \"name\" \"RLStrange\" \"item_class\" \"tf_weapon_rocketlauncher\" \"item_name\" \"#TF_Weapon_RocketLauncher\" \"item_quality\" \"strange\" } " +
        "\"5\" { \"name\" \"RLUnique\" \"item_class\" \"tf_weapon_rocketlauncher\" \"item_name\" \"#TF_Weapon_RocketLauncher\" \"item_quality\" \"unique\" } " +
        "\"6\" { \"name\" \"RLAustralium\" \"item_class\" \"tf_weapon_rocketlauncher\" \"item_name\" \"#TF_Weapon_RocketLauncher\" \"item_quality\" \"strange\" \"attributes\" { \"is australium item\" { \"value\" \"1\" } } }";

    /// <summary>The source over the inline schema, with a name token file.</summary>
    private static Ui SourceWith(string items, params (string Token, string Text)[] tokens)
    {
        var schema = new ClientSchema(new FilesShim(
            "\"items_game\" { \"qualities\" { \"normal\" { \"value\" \"0\" } \"unique\" { \"value\" \"6\" } \"strange\" { \"value\" \"11\" } } " +
            "\"prefabs\" { } \"attributes\" { \"1\" { \"name\" \"is australium item\" } } \"items\" { " + items + " } }"), new ClientLog());

        return new Ui(new EngineClient(), new Engine(), schema, new Recorder(),
                      new TokenFile(tokens.ToDictionary(t => t.Token, t => t.Text)));
    }

    private static Reply StrangeReply(uint defindex) => new()
    {
        Class = 3,
        EquippedSlots = [0],
        EquippedDefindexes = [defindex],
        OwnedDefindexes = [defindex],
        OwnedSlots = [0],
    };

    [Fact]
    public void AStrangeItemsNameIsPrefixedStrange()
    {
        var ui = SourceWith(QualityItems, ("#TF_Weapon_RocketLauncher", "Rocket Launcher"));
        ((Hook)ui).OnLoadoutReply(StrangeReply(4));

        Assert.Equal("Strange Rocket Launcher", ui.Slot(0).ItemName);
    }

    [Fact]
    public void AUniqueItemsNameHasNoPrefix()
    {
        // Unique's stock arm is the article-or-empty one (econ_item_description.cpp:761-778):
        // the name stands as it is.
        var ui = SourceWith(QualityItems, ("#TF_Weapon_RocketLauncher", "Rocket Launcher"));
        ((Hook)ui).OnLoadoutReply(StrangeReply(5));

        Assert.Equal("#TF_Weapon_RocketLauncher", ui.Slot(0).ItemName);
    }

    [Fact]
    public void AnAustraliumItemsNameIsPrefixedAustralium()
    {
        // The stock order: the quality word first, Australium after it
        // (econ_item_description.cpp:748-789, :822-832).
        var ui = SourceWith(QualityItems, ("#TF_Weapon_RocketLauncher", "Rocket Launcher"));
        ((Hook)ui).OnLoadoutReply(StrangeReply(6));

        Assert.Equal("Strange Australium Rocket Launcher", ui.Slot(0).ItemName);
    }

    [Fact]
    public void TheTargetIdReadsThePrefixedName()
    {
        // The kill-feed/target-ID string is the one string the loadout source
        // builds: both the slot row and the owned row of the same item carry it.
        var ui = SourceWith(QualityItems, ("#TF_Weapon_RocketLauncher", "Rocket Launcher"));
        ((Hook)ui).OnLoadoutReply(StrangeReply(4));

        Assert.Equal("Strange Rocket Launcher", ui.Slot(0).ItemName);
        Assert.Equal(ui.Slot(0).ItemName, ui.Owned(0).Name);
    }

    // ---- the rig ----

    private sealed class Rig
    {
        public readonly Engine Engine = new();
        public readonly EngineClient Sent = new();
        public readonly Recorder Nav = new();
        public readonly Ui Ui;

        public Rig()
        {
            Engine.Returns("Networking.SendServerMessage", true);
            Ui = new Ui(Sent, Engine, Schema(), Nav, new TokenFile([]));
        }

        public ITFLoadoutSource Source => Ui;
    }

    /// <summary>The trimmed schema, through the client's own file seam.</summary>
    private static ClientSchema Schema()
    {
        string text = EconTestFiles.Fixture("items_game.trimmed.txt");
        return new ClientSchema(new FilesShim(text), new ClientLog());
    }

    private sealed class FilesShim(string schemaText) : ClientFilesShim
    {
        private readonly byte[] _schema = Encoding.UTF8.GetBytes(schemaText);

        public int GameFileSize(string path) => path == client::TF2.Shared.Econ.ItemSchema.FilePath ? _schema.Length : -1;
        public string? ReadGameFile(string path) => ReadGameFileBytes(path) is { } b ? Encoding.UTF8.GetString(b) : null;
        public byte[]? ReadGameFileBytes(string path) => path == client::TF2.Shared.Econ.ItemSchema.FilePath ? _schema : null;
        public byte[]? ReadFileOnPath(string path, string pathId) => pathId == "GAME" ? ReadGameFileBytes(path) : null;
    }

    /// <summary>A mod token file for the name chain: the shipped tokens and their text.</summary>
    private sealed class TokenFile(Dictionary<string, string> tokens) : ClientUi.IUiLocalisation
    {
        public string? Find(string token) => tokens.TryGetValue(token, out var text) ? text : null;
    }

    private sealed class Recorder : Nav
    {
        public readonly List<string> Opened = [], OpenedPanels = [], Closed = [];

        public bool Has(string screen) => true;

        public void Open(string screen) => Opened.Add(screen);

        public void OpenPanel(string panel) => OpenedPanels.Add(panel);

        public void Close(string screen) => Closed.Add(screen);
    }

    private sealed class EngineClient : Abi.IFwdEngineClient
    {
        public readonly List<string> Lines = [];

        public void SubmitConsoleLine(string line) => Lines.Add(line);

        public void SpectatorCommand(Abi.SpectatorKeyCommand command) => Lines.Add(command.ToString()!);
        public void SpectatorServerCommand(string line) => Lines.Add(line);
        public bool IsInGame() => true;
        public void VoiceServerCommand(string line, bool reliable) { }
        public int SentenceIndexFromName(string name) => -1;
        public int LocalPlayerSlot() => 0;
        public string? LevelName() => null;
        public string? GameDirectory() => null;
        public uint ProtocolVersion() => 0;
        public bool CopyLocalFile(string source, string destination) => false;
        public bool TryGetPlayerInfo(int entIndex, out Abi.ClientPlayerInfo info) { info = default; return false; }
        public void JoinTeam(int team) { }
        public void ConnectFromSteamLaunch(string connectLine) { }
        public void Reconnect() { }
        public void RequestManagedReload() { }
        public void ConsolePrintAt(int position, string text) { }
        public void ConsolePrintAt(int position, float timeToLive, float red, float green, float blue, bool fixedWidthFont, string text) { }
        public bool TryLightForPoint(in global::SourceSharp.Abi.Vector position, bool clamp, out global::SourceSharp.Abi.Vector light) { light = default; return false; }
        public bool TryLightForPointFast(in global::SourceSharp.Abi.Vector position, bool clamp, out global::SourceSharp.Abi.Vector light) { light = default; return false; }
    }
}
