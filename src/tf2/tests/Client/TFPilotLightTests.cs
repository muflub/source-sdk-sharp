extern alias client;

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Econ;
using TF2.Tests.Weapons;

using Xunit;

using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientFlame = client::SourceSharp.Client.Standalone.ClientSsAttachedModel;
using ClientPlayer = client::SourceSharp.Client.Standalone.ClientSsPlayer;
using ClientStandalones = client::SourceSharp.Client.IClientStandalones;
using ClientEntity = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;

namespace TF2.Tests.Client;

/// <summary>
/// The flamethrower's pilot light: the attached model an item's
/// <c>visuals.attached_models</c> names, which stock draws riding the weapon's
/// bones (<c>CEconEntity::UpdateAttachmentModels</c>,
/// econ_entity.cpp:1079-1130) — the blue flame at the igniter tip.
/// </summary>
public sealed class TFPilotLightTests
{
    [Fact]
    public void The_flamethrowers_visuals_name_the_pilot_light()
    {
        var item = EconTestFiles.Trimmed().Items[21];

        // items_game.trimmed.txt:1556-1562, stock's attachedmodel_t with no
        // model_display_flags: MaskAll (econ_item_schema.cpp:2503), drawn in
        // both the world and the viewmodel pass.
        var attached = Assert.Single(item.AttachedModels);
        Assert.Equal("models/weapons/c_models/c_flamethrower/c_flamethrower_pilotlight.mdl", attached.Model);
        Assert.Equal(AttachedModelDisplayFlags.MaskAll, attached.DisplayFlags);
    }

    [Fact]
    public void An_item_without_attached_models_names_none()
    {
        var item = EconTestFiles.Trimmed().Items[13];

        Assert.Empty(item.AttachedModels);
    }

    [Fact]
    public void The_flamethrowers_row_carries_its_pilot_light()
    {
        var row = WeaponFixtures.Types(EconTestFiles.Fixture("items_game.trimmed.txt"))
            .Rows().ToDictionary(r => r.Name)["tf_weapon_flamethrower"];

        var attached = Assert.Single(row.AttachedModels);
        Assert.Equal("models/weapons/c_models/c_flamethrower/c_flamethrower_pilotlight.mdl", attached.Model);
        Assert.Equal(AttachedModelDisplayFlags.MaskAll, attached.DisplayFlags);
    }

    [Fact]
    public void A_row_whose_item_names_none_carries_none()
    {
        var row = WeaponFixtures.Types(EconTestFiles.Fixture("items_game.trimmed.txt"))
            .Rows().ToDictionary(r => r.Name)["tf_weapon_scattergun"];

        Assert.Empty(row.AttachedModels);
    }

    //-------------------------------------------------------------------------
    // The client half: the bearer's copy hangs off the viewmodel host in the
    // viewmodel pass, everyone else's off the weapon in the world.
    //-------------------------------------------------------------------------

    private const int Local = 1;
    private const int WeaponIndex = 0x33;
    private const int HostIndex = 0x44;

    private static readonly EntityHandle Flame = new(0x0001_0000u | 0x45);

    private static (ClientFlame Flame, ClientEngine Engine) Decoded(int owner, int weapon, int host, int flags)
    {
        var engine = new ClientEngine();
        engine.Returns("Hud.LocalPlayerIndex", Local);

        var flame = new ClientFlame(engine, engine) { Handle = Flame };
        flame.State.BindLocal();
        flame.State.OwnerIndex = owner;
        flame.State.WeaponIndex = weapon;
        flame.State.HostIndex = host;
        flame.State.Flags = flags;
        flame.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        return (flame, engine);
    }

    private static bool InThePass(ClientEngine engine, bool enabled)
        => engine.Calls.Any(c => c.Member == "EntityRender.SetViewModel" &&
                                 c.ArgAs<EntityHandle>(0) == Flame &&
                                 c.ArgAs<bool>(1) == enabled);

    private static bool HangingFrom(ClientEngine engine, int source)
        => engine.Calls.Any(c => c.Member == "EntityRender.SetBoneMergeSource" &&
                                 c.ArgAs<EntityHandle>(0) == Flame &&
                                 c.ArgAs<int>(1) == source);

    [Fact]
    public void TheBearersFlameJoinsTheViewmodelPassAndMergesOntoTheHost()
    {
        var (_, engine) = Decoded(Local, WeaponIndex, HostIndex, (int)AttachedModelDisplayFlags.MaskAll);

        Assert.True(InThePass(engine, enabled: true), "the bearer's own copy draws at the bearer's eyes");
        Assert.True(HangingFrom(engine, HostIndex), "and hangs off the host the server named");
    }

    [Fact]
    public void AnotherBearersFlameStaysInTheWorldOnTheWeaponsBones()
    {
        var (_, engine) = Decoded(2, WeaponIndex, HostIndex, (int)AttachedModelDisplayFlags.MaskAll);

        Assert.True(!engine.Calls.Any(c => c.Member == "EntityRender.SetViewModel" &&
                                          c.ArgAs<EntityHandle>(0) == Flame),
            "nobody else's copy is in the pass: a flame at every eye is worse than none");
        Assert.True(HangingFrom(engine, WeaponIndex), "it rides the weapon's bones, where the world sees it");
    }

    [Fact]
    public void AFlameTheViewmodelPassNeverGetsStaysInTheWorldForItsBearerToo()
    {
        var (_, engine) = Decoded(Local, WeaponIndex, HostIndex, (int)AttachedModelDisplayFlags.WorldModel);

        Assert.True(!engine.Calls.Any(c => c.Member == "EntityRender.SetViewModel" &&
                                          c.ArgAs<EntityHandle>(0) == Flame &&
                                          c.ArgAs<bool>(1)),
            "no viewmodel flag, no pass");
        Assert.True(HangingFrom(engine, WeaponIndex), "the world bones carry it still");
    }

    [Fact]
    public void TheDeadBearersFlameLeavesThePassForTheWorldBonesAndBack()
    {
        var engine = new ClientEngine();
        engine.Returns("Hud.LocalPlayerIndex", Local);

        var directory = new BearerDirectory();

        var bearer = new ClientPlayer(engine, screen: engine, entities: directory) { Handle = new EntityHandle(0x0001_0000u | Local) };
        bearer.BindIndex(Local);
        bearer.State.BindLocal();
        bearer.State.Alive = true;
        directory.Entities.Add(bearer);

        var flame = new ClientFlame(engine, engine, directory) { Handle = Flame };
        flame.State.BindLocal();
        flame.State.OwnerIndex = Local;
        flame.State.WeaponIndex = WeaponIndex;
        flame.State.HostIndex = HostIndex;
        flame.State.Flags = (int)AttachedModelDisplayFlags.MaskAll;
        directory.Entities.Add(flame);
        flame.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.True(HangingFrom(engine, HostIndex), "the rig: on the host while the bearer lives");

        // The death decode is the PLAYER's: the (alive, mode) edge re-runs the
        // flame's decision, exactly as the viewmodel attachment's.
        bearer.State.Alive = false;
        bearer.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.True(InThePass(engine, enabled: false), "the death cam has no eyes to hang a flame at");
        Assert.True(HangingFrom(engine, WeaponIndex), "and the world bones carry it for the death cam instead");

        bearer.State.Alive = true;
        bearer.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.True(engine.Calls.Count(c => c.Member == "EntityRender.SetViewModel" &&
                                           c.ArgAs<EntityHandle>(0) == Flame &&
                                           c.ArgAs<bool>(1)) == 2,
            "back in on the respawn: out once, in once");
        Assert.True(HangingFrom(engine, HostIndex), "and back on the host");
    }

    /// <summary>A directory holding the bearer: GetByIndex answers by bound index.</summary>
    private sealed class BearerDirectory : ClientStandalones
    {
        public readonly List<ClientEntity> Entities = [];
        public IReadOnlyList<ClientEntity> Live() => Entities;
        public ClientEntity? GetByIndex(int entityIndex)
            => Entities.FirstOrDefault(e => e.Index == entityIndex);
    }
}
