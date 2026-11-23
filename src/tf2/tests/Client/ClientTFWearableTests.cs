extern alias client;

using SourceSharp.Abi;

using Policy = client::SourceSharp.Client.Standalone.IRenderPolicyWishes;

using TF2.Shared.Player;

using Xunit;

using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientLog = client::SourceSharp.Abi.RecordingLog;
using Wearable = client::TF2.Client.Entities.ClientTFWearable;
using Follow = client::TF2.Client.Entities.TFWearableSkinFollow;
using Carrier = client::TF2.Client.Player.ClientTFPlayerData;
using Carriers = client::TF2.Client.Player.TFClientCarriers;
using ClientDriver = client::TF2.Client.Player.TFClientConditionDriver;
using PlayerResource = client::TF2.Client.Player.ClientTFPlayerResource;
using PlayerResources = client::TF2.Client.Player.TFClientPlayerResources;
using Hook = client::TF2.Client.Player.ITFClientConditionHook;
using ClientWords = client::TF2.Shared.Player.TFPlayerNetStateViews;
using Mask = client::TF2.Shared.Conditions.TFCondMask;
using Cond = client::TF2.Shared.TFCond;
using TFTeam = client::TF2.Shared.TFTeam;

namespace TF2.Tests.Client;

/// <summary>
/// The wearable's three per-viewer decisions, stock's client half of
/// <c>CEconWearable</c> (econ_wearable.cpp:354-460) and
/// <c>CTFWearable::GetSkin</c> (tf_item_wearable.cpp:627-675): the bone merge
/// onto the owner, the skin — the server's, or the disguise's for an enemy
/// eye — and the first-person hide.
/// </summary>
public sealed class ClientTFWearableTests
{
    private const int Local = 1;

    private static readonly EntityHandle Hat = new(0x0004_0021u);

    private static readonly EntityHandle Spy = new(0x0002_0003u);

    private sealed class Wishes : Policy
    {
        public readonly List<(uint Entity, bool OwnerSeen)> Seen = [];

        public void WantTwoPass(EntityHandle entity, bool twoPass) { }

        public void WantTransparent(EntityHandle entity, bool transparent) { }

        public void WantOwnerSeen(EntityHandle entity, bool ownerSeen) => Seen.Add((entity.Value, ownerSeen));

        public int InEyeTarget { get; set; }

        public void WatchInEye(int index) => InEyeTarget = index;
    }

    private static bool MergedOnto(ClientEngine engine, int source)
        => engine.Calls.Any(c => c.Member == "EntityRender.SetBoneMergeSource" &&
                                 c.ArgAs<EntityHandle>(0) == Hat &&
                                 c.ArgAs<int>(1) == source);

    private static int Skins(ClientEngine engine)
        => engine.Calls.Count(c => c.Member == "EntityRender.SetSkin" && c.ArgAs<EntityHandle>(0) == Hat);

    private static int? LastSkin(ClientEngine engine)
        => engine.Calls.LastOrDefault(c => c.Member == "EntityRender.SetSkin" &&
                                          c.ArgAs<EntityHandle>(0) == Hat) is { } call
            ? call.ArgAs<int>(1)
            : null;

    [Fact]
    public void The_decode_merges_the_wearable_onto_its_owners_bones()
    {
        var engine = new ClientEngine();
        var wearable = Decoded(engine, new Wishes(), owner: 7);

        Assert.True(MergedOnto(engine, 7), "EF_BONEMERGE: the hat rides the owner's skeleton");
    }

    [Fact]
    public void Losing_the_owner_lets_the_bones_go()
    {
        var engine = new ClientEngine();
        var wishes = new Wishes();
        var wearable = Decoded(engine, wishes, owner: 7);

        wearable.State.OwnerIndex = 0;
        wearable.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.True(MergedOnto(engine, -1), "no owner, no merge source");
    }

    [Fact]
    public void A_decode_that_decides_the_same_writes_nothing()
    {
        var engine = new ClientEngine();
        var wearable = Decoded(engine, new Wishes(), owner: 7);

        wearable.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.Equal(1, engine.Calls.Count(c => c.Member == "EntityRender.SetBoneMergeSource"));
    }

    [Fact]
    public void The_servers_skin_is_worn_where_no_viewer_moves_it()
    {
        var engine = new ClientEngine();
        var wearable = Decoded(engine, new Wishes(), owner: 7, skin: 2);

        Assert.Equal(2, LastSkin(engine));
    }

    [Fact]
    public void The_bearers_own_copy_hides_while_the_camera_is_inside_them()
    {
        var engine = new ClientEngine();
        engine.Returns("Hud.LocalPlayerIndex", Local);
        var wishes = new Wishes();
        Decoded(engine, wishes, screen: engine, owner: 2);

        Assert.Empty(wishes.Seen); // not the bearer: no wish moved

        Decoded(engine, wishes, screen: engine, owner: Local);

        Assert.Equal([(Hat.Value, true)], wishes.Seen);
    }

    [Fact]
    public void The_copy_worn_by_the_player_watched_in_eye_hides_too()
    {
        // C_BasePlayer::InFirstPersonView answers true for the local
        // observer's in-eye target: its hat is not drawn from inside its head.
        var engine = new ClientEngine();
        engine.Returns("Hud.LocalPlayerIndex", Local);
        var wishes = new Wishes();
        var wearable = Decoded(engine, wishes, screen: engine, owner: 2);

        wishes.WatchInEye(2);
        wearable.ReconsiderFirstPerson();

        Assert.Equal([(Hat.Value, true)], wishes.Seen);
    }

    [Fact]
    public void The_removal_clear_the_wish_the_way_the_attached_models_does()
    {
        var engine = new ClientEngine();
        engine.Returns("Hud.LocalPlayerIndex", Local);
        var wishes = new Wishes();
        var wearable = Decoded(engine, wishes, screen: engine, owner: Local);

        wearable.OnRemoved();

        // The decode wished the bearer's own copy seen; the removal clears it.
        Assert.Equal([(Hat.Value, true), (Hat.Value, false)], wishes.Seen);

        // And a fresh entity state starts quiet: the cleared wish is not
        // re-asked until an edge moves it.
        wishes.Seen.Clear();
        wearable.OnRemoved();
        Assert.Empty(wishes.Seen);
    }

    //-------------------------------------------------------------------------
    // The disguise's skin, through the real path: the carrier's words, the
    // scoreboard's teams, the condition driver's edges.
    //-------------------------------------------------------------------------

    [Fact]
    public void An_enemys_eye_sees_a_disguised_spys_wearables_in_the_disguises_skin()
    {
        var rig = Disguised(localTeam: TFTeam.Red, ownerTeam: TFTeam.Blue, disguise: TFTeam.Red, wornSkin: 1);

        rig.Wearable.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.Equal(0, LastSkin(rig.Engine));
    }

    [Fact]
    public void A_teammates_eye_sees_the_worn_skin()
    {
        var rig = Disguised(localTeam: TFTeam.Red, ownerTeam: TFTeam.Red, disguise: TFTeam.Blue, wornSkin: 1);

        rig.Wearable.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.Equal(1, LastSkin(rig.Engine));
    }

    [Fact]
    public void The_disguises_edges_re_skin_the_wearables_the_spy_wears()
    {
        var rig = Disguised(localTeam: TFTeam.Red, ownerTeam: TFTeam.Blue, disguise: TFTeam.Red, wornSkin: 1,
                            disguisedNow: false);
        var (engine, follow, driver) = rig.Wearers;

        // Not disguised yet: the wearable wears the server's skin.
        Assert.Equal(1, LastSkin(engine));

        ClientWords.SetConditions(rig.Carrier.State, Mask.Of(Cond.Disguised));
        driver.Sync(new EntityHandle(0x0002_0003u), default, Mask.Of(Cond.Disguised));

        Assert.Equal(0, LastSkin(engine));

        ClientWords.SetConditions(rig.Carrier.State, default);
        driver.Sync(new EntityHandle(0x0002_0003u), Mask.Of(Cond.Disguised), default);

        Assert.Equal(1, LastSkin(engine));
    }

    [Fact]
    public void Only_the_disguises_edges_reach_the_arm()
    {
        var rig = Disguised(localTeam: TFTeam.Red, ownerTeam: TFTeam.Blue, disguise: TFTeam.Red, wornSkin: 1);
        var (engine, follow, driver) = rig.Wearers;
        int before = Skins(engine);

        driver.Sync(new EntityHandle(0x0002_0003u), default, Mask.Of(Cond.Burning));

        Assert.Equal(before, Skins(engine));
    }

    /// <summary>The wearable's first decode: merged, skinned, wished. The
    /// holders are required since the class takes them outright: with no
    /// scripted viewer the engine answers no local player, and the wish
    /// stays quiet the way the old null-screen skip left it.</summary>
    private static Wearable Decoded(ClientEngine engine, Wishes wishes, int owner, int skin = 0,
                                    client::SourceSharp.Abi.IEngineHud? screen = null)
    {
        screen ??= engine.Returns("Hud.LocalPlayerIndex", 0);
        var wearable = new Wearable(engine, wishes, screen, new Carriers(), new PlayerResources()) { Handle = Hat };
        wearable.State.BindLocal();
        wearable.State.OwnerIndex = owner;
        wearable.State.Skin = skin;
        wearable.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        return wearable;
    }

    private sealed record DisguiseRig(ClientEngine Engine, Wearable Wearable, Carrier Carrier,
                                      (ClientEngine, Follow, ClientDriver) Wearers);

    /// <summary>A red viewer's client: the spy's carrier disguised
    /// <paramref name="disguise"/>, the scoreboard's teams, the wearable whose
    /// skin follows, and the driver whose edges the follower wears. The spy
    /// wears the disguise already unless <paramref name="disguisedNow"/>).</summary>
    private static DisguiseRig Disguised(TFTeam localTeam, TFTeam ownerTeam, TFTeam disguise, int wornSkin,
                                         bool disguisedNow = true)
    {
        var engine = new ClientEngine();
        engine.Returns("Hud.LocalPlayerIndex", Local);

        var wishes = new Wishes();
        var carriers = new Carriers();
        var resources = new PlayerResources();
        var resource = new PlayerResource(engine, resources) { Handle = new EntityHandle(0x0005_0001u) };
        resource.State.BindLocal();
        resource.OnSpawn();
        resource.State.Team[Local] = (byte)localTeam;
        resource.State.Team[3] = (byte)ownerTeam;

        var log = new ClientLog();
        var classes = new client::TF2.Shared.TFClassTable(log, new ClientFiles(TFClassTableTests.Files.Shipped()));
        var standalones = new HatDirectory();
        var wearable = new Wearable(engine, wishes, engine, carriers, resources) { Handle = Hat };
        wearable.State.BindLocal();
        wearable.State.OwnerIndex = 3;
        wearable.State.Skin = wornSkin;
        standalones.Entities.Add(wearable);

        var carrier = new Carrier(engine, carriers, standalones, resources, engine, classes,
                                  new ClientDriver(new NoHooks()), log);
        carrier.State.BindLocal();
        carrier.State.OwnerIndex = 3;
        carrier.State.DisguiseTeam = (int)disguise;
        if (disguisedNow)
            ClientWords.SetConditions(carrier.State, Mask.Of(Cond.Disguised));
        carriers.Track(carrier);

        var follow = new Follow(standalones);
        var driver = new ClientDriver(new HookList([follow]));

        wearable.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        return new DisguiseRig(engine, wearable, carrier, (engine, follow, driver));
    }

    /// <summary>The directory holding the hat, for the follower's walk.</summary>
    private sealed class HatDirectory : client::SourceSharp.Client.IClientStandalones
    {
        public readonly List<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Entities = [];

        public IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live() => Entities;

        public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
            => Entities.FirstOrDefault(e => e.Index == entityIndex);
    }

    /// <summary>No hooks: the carrier's own sync arm is silent here.</summary>
    private sealed class NoHooks : client::SourceSharp.Abi.IHooks<Hook>
    {
        public IReadOnlyList<Hook> All => [];

        public void Each(string what, Action<Hook> call) { }
    }

    /// <summary>One hook, the follower: the driver's chain under test.</summary>
    private sealed class HookList(IReadOnlyList<Hook> hooks) : client::SourceSharp.Abi.IHooks<Hook>
    {
        public IReadOnlyList<Hook> All => hooks;

        public void Each(string what, Action<Hook> call)
        {
            foreach (var hook in hooks)
                call(hook);
        }
    }

    /// <summary>The shipped class scripts, as the client's file seam sees them.</summary>
    private sealed class ClientFiles(TFClassTableTests.Files files) : client::SourceSharp.Abi.IEngineFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);

        public string? ReadGameFile(string path) => files.ReadGameFile(path);

        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);

        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
    }
}
