extern alias client;

using SourceSharp.Abi;

using Xunit;

using CI = client::SourceSharp.Client.Interop;
using ClientSide = client::SourceSharp.Client;
using MP = client::SourceSharp.Client.MaterialProxies;
using Standalone = client::SourceSharp.Client.Standalone;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using Proxies = client::TF2.Client.Proxies;

using TFCond = client::TF2.Shared.TFCond;
using TFClass = client::TF2.Shared.TFClass;
using TFClient = client::TF2.Client;
using TFCondMask = client::TF2.Shared.Conditions.TFCondMask;
using TFPlayer = client::TF2.Shared.Player;
using TFTeam = client::TF2.Shared.TFTeam;

namespace TF2.Tests.Client;

/// <summary>
/// TF2's own proxy, <c>ModelGlowColor</c>: each arm against the stock lines it
/// ports (game/client/tf/c_tf_player.cpp:1985-2121), over the owner chain and
/// the two player resources as the client holds them. The charge-glow arms are
/// deferred (docs/plans/tf2/deferred.tsv) and answer the default.
/// </summary>
public sealed class TFMaterialProxyTests
{
    private const int ViewModelIndex = 10;
    private const int WeaponIndex = 11;
    private const int TeammateWeaponIndex = 12;
    private const int AttachmentIndex = 13;
    private const int BuildingIndex = 14;

    private sealed class InitFake(params MP.ProxyKey[] keys) : MP.IProxyInit
    {
        /// <summary>The vars the material holds, by name; null answers every name slot 0.</summary>
        public Func<string, int>? Vars { get; init; }

        public string MaterialName => "c_syringegun_blue";

        public MP.ProxyKeyValues Keys { get; } = new(keys);

        public int FindVar(string name, bool complain, out bool found)
        {
            if (Vars is null)
            {
                found = true;
                return 0;
            }

            int slot = Vars(name);
            found = slot >= 0;
            return slot;
        }

        public int FindTexture(string name, string group, bool complain, out bool isError)
        {
            isError = false;
            return 100;
        }
    }

    private sealed class BindFake : MP.IProxyBind
    {
        public readonly List<float[]> Vecs = [];
        public readonly List<(int Slot, float X, float Y, float Z)> Vec3s = [];
        public readonly List<(int Slot, float Value)> Floats = [];
        public readonly List<(int Slot, int Value)> Ints = [];
        public readonly List<MP.VMatrix> Mats = [];

        public float CurTime { get; set; }
        public float FrameTime => 0f;
        public int MaxClients => 16;
        public nint Arg { get; set; }

        public CI.ProxyVar NextVar;

        public CI.ProxyVar Var(int slot) => NextVar;

        /// <summary>The frame count <c>Texture</c> answers; -1 keeps stock's NULL answer.</summary>
        public int NextFrames = -1;

        public int Texture(int slot, out string? name)
        {
            name = null;
            return NextFrames;
        }

        public MP.ProxyEntity Entity() => Arg == 0
            ? MP.ProxyEntity.None
            : new MP.ProxyEntity(true, true, (int)Arg, Vector.Zero, 255, 0);

        public Vector ViewOrigin() => default;

        public void WaterLod(out float start, out float end) => start = end = 0;

        public bool WorldBounds(out Vector mins, out Vector maxs)
        {
            mins = maxs = default;
            return false;
        }

        public MP.ParticleBindLight ParticleLight() => default;

        public bool PlayerCustomFile(int player, out uint crc)
        {
            crc = 0;
            return false;
        }

        public int FindTexture(string name, string group, bool complain) => -1;

        public float RandomFloat(float lo, float hi) => lo;

        public float RandomGaussian(float mean, float stdDev) => mean;

        public void NotShipped(string capability, string reason) { }

        public bool TextureFrameIndex(out int frame)
        {
            frame = 0;
            return false;
        }

        public bool EyeLighting(out Vector eye, out Vector forward, out Vector lighting)
        {
            eye = forward = lighting = default;
            return false;
        }

        public void SetFloat(int slot, float value) => Floats.Add((slot, value));

        public void SetInt(int slot, int value) => Ints.Add((slot, value));

        public void SetVec(int slot, ReadOnlySpan<float> values, int count) => Vecs.Add(values[..count].ToArray());

        public void SetVec3(int slot, float x, float y, float z) => Vec3s.Add((slot, x, y, z));

        public void SetVecComponent(int slot, float value, int component) { }

        public void SetMatrix(int slot, in MP.VMatrix matrix) => Mats.Add(matrix);

        public void SetTexture(int slot, int textureSlot) { }
    }

    /// <summary>The player data, predicted or networked by player index.</summary>
    private sealed class PlayerData : TFPlayer.ITFPlayerData
    {
        public readonly Dictionary<int, TFPlayer.TFPlayerNetState> Players = [];

        public TFPlayer.TFPlayerNetState? Predicted(int playerIndex) => Players.GetValueOrDefault(playerIndex);

        public TFPlayer.TFPlayerNetState? Networked(int playerIndex) => Players.GetValueOrDefault(playerIndex);
    }

    /// <summary>The directory: entities by index, the player resource among them.</summary>
    private sealed class Standalones : ClientSide.IClientStandalones
    {
        public readonly Dictionary<int, Standalone.ClientStandaloneEntity> ByIndex = [];

        public IReadOnlyList<Standalone.ClientStandaloneEntity> Live() => [.. ByIndex.Values];

        public Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => ByIndex.GetValueOrDefault(entityIndex);
    }

    private static MP.ProxyKey ResultVar() => new("resultVar", MP.ProxyKeyType.String, "$color2", 0, 0, 0, 0, 0, 0);

    /// <summary>The world: the local player is slot 2 on RED, a teammate is slot 3
    /// on RED, a BLU player slot 4; the viewmodel of 2 is entity 10 and the weapon
    /// of 4 is entity 11; the player resource is entity 30.</summary>
    private static (Proxies.TFProxyPlayers Players, Engine Screen) World(
        PlayerData? data = null, Standalones? st = null, int local = 2,
        Standalone.IRenderPolicyWishes? wishes = null)
    {
        data ??= new PlayerData();
        st ??= new Standalones();
        var screen = new Engine();
        screen.Returns("Hud.LocalPlayerIndex", local);

        var core = new Standalone.ClientPlayerResource(screen);
        core.State.BindLocal();
        core.State.Team[2] = 2;   // RED
        core.State.Team[3] = 2;   // RED
        core.State.Team[4] = 3;   // BLU
        st.ByIndex[30] = core;

        var viewmodel = new Standalone.ClientSsViewModel(screen, screen);
        viewmodel.State.BindLocal();
        viewmodel.State.OwnerIndex = 2;
        st.ByIndex[ViewModelIndex] = viewmodel;

        var weapon = new Standalone.ClientSsWeapon(screen);
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = 4;
        st.ByIndex[WeaponIndex] = weapon;

        var teamWeapon = new Standalone.ClientSsWeapon(screen);
        teamWeapon.State.BindLocal();
        teamWeapon.State.OwnerIndex = 3;
        st.ByIndex[TeammateWeaponIndex] = teamWeapon;

        var maxInvis = new TFClient.TFTeammateMaxInvisConVar();
        maxInvis.Bind(-1, "tf_teammate_max_invis", "0.95");
        return (new Proxies.TFProxyPlayers(data, st, screen, maxInvis,
                                           wishes ?? new Standalone.NoRenderPolicyWishes()), screen);
    }

    private static (Proxies.TFProxyModelGlowColor Proxy, Engine Screen) Rig(
        PlayerData? data = null, Standalones? st = null, int local = 2)
    {
        var (players, screen) = World(data, st, local);
        return (new Proxies.TFProxyModelGlowColor(players), screen);
    }

    private static TFPlayer.TFPlayerNetState Carrier(params TFCond[] conds)
    {
        var state = new TFPlayer.TFPlayerNetState();
        state.BindLocal();
        TFCondMask mask = default;
        foreach (TFCond cond in conds)
            mask |= TFCondMask.Of(cond);
        TFPlayer.TFPlayerNetStateViews.SetConditions(state, mask);
        return state;
    }

    private static float[] Bound(Proxies.TFProxyModelGlowColor proxy, nint arg)
    {
        Assert.True(proxy.Init(new InitFake(ResultVar())));
        var bind = new BindFake { Arg = arg };
        proxy.Bind(bind);
        var vec = Assert.Single(bind.Vecs);
        return vec;
    }

    [Fact]
    public void A_bind_with_no_entity_answers_white()
    {
        var (proxy, _) = Rig();

        Assert.Equal([1f, 1f, 1f], Bound(proxy, 0));
    }

    [Fact]
    public void A_model_bound_to_nobody_answers_white()
    {
        var (proxy, _) = Rig();

        // An entity index nothing holds (99), and one whose shell answers to no
        // carrier: both fall to stock's default (:2015, :2111).
        Assert.Equal([1f, 1f, 1f], Bound(proxy, 99));
        Assert.Equal([1f, 1f, 1f], Bound(proxy, ViewModelIndex));
    }

    [Fact]
    public void A_crit_boosted_owner_colours_by_their_team()
    {
        var data = new PlayerData();
        data.Players[2] = Carrier(TFCond.Critboosted);
        var (proxy, _) = Rig(data);

        // The viewmodel's owner is the local RED (2): stock's RED arm (:2032-2034).
        Assert.Equal([80f, 8f, 5f], Bound(proxy,ViewModelIndex));

        // A BLU weapon (entity 11, owner 4): stock's else arm (:2036-2038).
        data.Players[4] = Carrier(TFCond.Critboosted);
        Assert.Equal([5f, 20f, 80f], Bound(proxy,WeaponIndex));
    }

    [Fact]
    public void A_disguised_crit_spy_is_white_to_everyone_but_themselves()
    {
        var data = new PlayerData();
        data.Players[3] = Carrier(TFCond.Critboosted, TFCond.Disguised);
        var (proxy, _) = Rig(data);

        // The disguised teammate's model from the local view: white (:2029-2031).
        Assert.Equal([1f, 1f, 1f], Bound(proxy,WeaponIndex));

        // The local player's own disguised view keeps the colour: the "unless it's me" arm.
        var own = new PlayerData();
        own.Players[2] = Carrier(TFCond.Critboosted, TFCond.Disguised);
        var (ownProxy, _) = Rig(own, local: 2);
        Assert.Equal([80f, 8f, 5f], Bound(ownProxy,ViewModelIndex));
    }

    [Fact]
    public void The_soda_popper_hype_colours_purple()
    {
        var data = new PlayerData();
        data.Players[2] = Carrier(TFCond.SodapopperHype);
        var (proxy, _) = Rig(data);

        // stock's :2043-2046, IsHypeBuffed = TF_COND_SODAPOPPER_HYPE.
        Assert.Equal([50f, 2f, 48f], Bound(proxy,ViewModelIndex));
    }

    [Fact]
    public void The_offence_and_energy_buffs_colour_by_team()
    {
        var data = new PlayerData();
        data.Players[2] = Carrier(TFCond.Offensebuff);
        var (proxy, _) = Rig(data);

        Assert.Equal([226f, 150f, 62f], Bound(proxy,ViewModelIndex));

        data.Players[4] = Carrier(TFCond.EnergyBuff);
        Assert.Equal([29f, 202f, 135f], Bound(proxy,WeaponIndex));
    }

    [Fact]
    public void An_unconditioned_owner_answers_white()
    {
        var data = new PlayerData();
        data.Players[2] = Carrier();
        var (proxy, _) = Rig(data);

        // The else arm's default (:2105-2108): no charge state to hold.
        Assert.Equal([1f, 1f, 1f], Bound(proxy,ViewModelIndex));
    }

    [Fact]
    public void The_provider_creates_its_proxy_by_name_and_leaves_the_rest()
    {
        var provider = new Proxies.TFMaterialProxies(new PlayerData(), new Standalones(), new Engine(),
                                                     new TFClient.TFTeammateMaxInvisConVar(),
                                                     new Standalone.NoRenderPolicyWishes(),
                                                     client::SourceSharp.Shared.WeaponCatalog.Stock(),
                                                     new DrawStates());

        Assert.Equal(["ModelGlowColor", "spy_invis", "weapon_invis", "vm_invis", "invis",
            "InvulnLevel", "BurnLevel", "YellowLevel", "HeartbeatScale", "ShieldFalloff", "WheatlyEyeGlow",
            "CommunityWeapon", "BenefactorLevel", "ItemTintColor", "WeaponSkin", "ObjectPower",
            "StatTrakIllum", "StatTrakDigit", "StatTrakIcon", "AnimatedWeaponSheen",
            "TeamTexture", "building_invis", "BuildingRescueLevel", "SniperRifleCharge", "StickybombGlowColor"],
            provider.Names);
        Assert.IsType<Proxies.TFProxyModelGlowColor>(provider.Create("ModelGlowColor"));
        Assert.IsType<Proxies.TFProxySpyInvis>(provider.Create("spy_invis"));
        Assert.IsType<Proxies.TFProxyWeaponInvis>(provider.Create("weapon_invis"));
        Assert.IsType<Proxies.TFProxyVmInvis>(provider.Create("vm_invis"));
        Assert.IsType<Proxies.TFProxyInvisGeneric>(provider.Create("invis"));
        Assert.IsType<Proxies.TFProxyInvulnLevel>(provider.Create("InvulnLevel"));
        Assert.IsType<Proxies.TFProxyBurnLevel>(provider.Create("BurnLevel"));
        Assert.IsType<Proxies.TFProxyYellowLevel>(provider.Create("YellowLevel"));
        Assert.IsType<Proxies.TFProxyHeartbeatScale>(provider.Create("HeartbeatScale"));
        Assert.IsType<Proxies.TFProxyShieldFalloff>(provider.Create("ShieldFalloff"));
        Assert.IsType<Proxies.TFProxyWheatlyEyeGlow>(provider.Create("WheatlyEyeGlow"));
        Assert.Null(provider.Create("Equals"));
    }

    // ---- the cloak proxies --------------------------------------------------

    /// <summary>The render policy a cloak bind wishes to, recorded.</summary>
    private sealed class Wishes : Standalone.IRenderPolicyWishes
    {
        public readonly List<(uint Entity, bool Transparent)> Asked = [];

        public void WantTwoPass(EntityHandle entity, bool twoPass) { }

        public void WantTransparent(EntityHandle entity, bool transparent) => Asked.Add((entity.Value, transparent));

        public void WantOwnerSeen(EntityHandle entity, bool ownerSeen) { }
    }

    private const int BodyIndex = 2;

    /// <summary>The world with the local spy's own body standing in it (entity 2,
    /// the index the player holds): the body a cloak bind wishes on.</summary>
    private static (Proxies.TFProxyPlayers Players, Engine Screen, Wishes Wishes) WorldWithBody(
        PlayerData data)
    {
        var st = new Standalones();
        var wishes = new Wishes();
        var (players, screen) = World(data, st, wishes: wishes);

        var body = new Standalone.ClientSsPlayer(screen);
        body.State.BindLocal();
        body.Handle = new EntityHandle(0x00010002u);
        st.ByIndex[BodyIndex] = body;
        return (players, screen, wishes);
    }

    private static void CloakBound(MP.MaterialProxy proxy, nint arg, out float? cloak, out (int Slot, float X, float Y, float Z)? tint,
                                   float now = 0f)
    {
        Assert.True(proxy.Init(new InitFake()));
        var bind = new BindFake { Arg = arg, CurTime = now };
        proxy.Bind(bind);
        cloak = Assert.Single(bind.Floats).Value;
        tint = bind.Vec3s.Count switch { 0 => null, 1 => bind.Vec3s[0], _ => throw new InvalidOperationException() };
    }

    /// <summary>A spy mid-fade: cloaked at 100 over the 1.0 s invis time, so
    /// the level is 0.25 at 100.25 (tf_player_shared.cpp:8001-8004).</summary>
    private static TFPlayer.TFPlayerNetState FadingSpy(params TFCond[] conds)
    {
        var state = Carrier(conds);
        state.PlayerClass = (int)TFClass.Spy;
        state.InvisChangeCompleteTime = 101f;
        return state;
    }

    [Fact]
    public void The_spy_cloak_tints_by_the_carriers_team()
    {
        var data = new PlayerData();
        data.Players[2] = Carrier();
        data.Players[4] = Carrier();
        var (players, _) = World(data);
        var red = new Proxies.TFProxySpyInvis(players);

        // The local RED's viewmodel (entity 10, owner 2): stock's RED arm
        // (c_tf_player.cpp:1739-1741); no clock armed, so the level 0.
        CloakBound(red, ViewModelIndex, out float? cloak, out var tint);
        Assert.Equal(0f, cloak);
        Assert.Equal((0, 1.0f, 0.5f, 0.4f), tint!.Value);

        // A BLU weapon (entity 11, owner 4): stock's else arm (:1743-1747).
        var (bluPlayers, _) = World(data);
        var blu = new Proxies.TFProxySpyInvis(bluPlayers);
        CloakBound(blu, WeaponIndex, out _, out tint);
        Assert.Equal((0, 0.4f, 0.5f, 1.0f), tint!.Value);
    }

    [Fact]
    public void The_spy_cloak_writes_the_fading_level()
    {
        var data = new PlayerData();
        data.Players[2] = FadingSpy(TFCond.Stealthed);
        var (players, _) = World(data);
        var red = new Proxies.TFProxySpyInvis(players);

        // The player arm's level (c_tf_player.cpp:1735): a quarter into the
        // fade-in, the local RED's own view — under the teammate cap.
        CloakBound(red, ViewModelIndex, out float? cloak, out _, now: 100.25f);
        Assert.Equal(0.25f, cloak);
    }

    [Fact]
    public void The_teammate_cap_holds_a_cloaked_teammate_at_095_and_spares_an_enemy()
    {
        var data = new PlayerData();
        data.Players[3] = FadingSpy(TFCond.Stealthed);
        data.Players[3].InvisChangeCompleteTime = 101f;   // fully cloaked at the bind's 101
        data.Players[4] = FadingSpy(TFCond.Stealthed);
        data.Players[4].InvisChangeCompleteTime = 101f;
        var (players, _) = World(data);
        var spy = new Proxies.TFProxySpyInvis(players);

        // A RED teammate's weapon from the local RED's view: capped
        // (c_tf_player.cpp:6854-6862).
        CloakBound(spy, TeammateWeaponIndex, out float? teammate, out _, now: 101f);
        Assert.Equal(0.95f, teammate);

        // A BLU spy: an enemy of the local view, uncapped (:6863).
        CloakBound(spy, WeaponIndex, out float? enemy, out _, now: 101f);
        Assert.Equal(1f, enemy);
    }

    [Fact]
    public void The_spy_cloak_leaves_the_tint_when_the_bind_walks_to_nobody()
    {
        var (players, _) = World();
        var spy = new Proxies.TFProxySpyInvis(players);

        // A viewmodel whose owner carries no state: the mimic arm keeps the
        // tint (:1721-1731) and writes only the level.
        CloakBound(spy, 99, out float? cloak, out var tint);
        Assert.Equal(0f, cloak);
        Assert.Null(tint);
    }

    [Fact]
    public void The_spy_cloak_answers_rest_when_bound_to_no_entity()
    {
        var (players, _) = World();
        var spy = new Proxies.TFProxySpyInvis(players);

        // OnBindNotEntity (:1755-1763): cloakfactor 0 and the tint to white.
        CloakBound(spy, 0, out float? cloak, out var tint);
        Assert.Equal(0f, cloak);
        Assert.Equal((0, 1f, 1f, 1f), tint!.Value);
    }

    [Fact]
    public void The_spy_cloak_refuses_a_material_without_the_tint()
    {
        var (players, _) = World();
        var spy = new Proxies.TFProxySpyInvis(players);

        Assert.False(spy.Init(new InitFake { Vars = name => name == "$cloakColorTint" ? -1 : 0 }));
    }

    [Fact]
    public void The_cloak_bind_wishes_the_players_body_transparent_while_it_is_invisible()
    {
        // Stock re-leafs the cloaked body into the translucent group while
        // GetPercentInvisible() > 0 (C_TFPlayer::IsTransparent,
        // c_tf_player.h:208): the bind's level is the same word.
        var data = new PlayerData();
        data.Players[BodyIndex] = FadingSpy(TFCond.Stealthed);
        var (players, _, wishes) = WorldWithBody(data);
        var spy = new Proxies.TFProxySpyInvis(players);

        CloakBound(spy, BodyIndex, out float? cloak, out _, now: 100.25f);

        Assert.Equal(0.25f, cloak);
        Assert.Equal([(0x00010002u, true)], wishes.Asked);
    }

    [Fact]
    public void The_cloak_bind_holds_the_own_cloaked_body_at_the_teammate_cap()
    {
        // The live boot's sustained cloak (thirdperson, +attack2): the body's
        // spy_red material binds with the player resolved and the level
        // reaches the cap, not 1 — the probe logged $cloakfactor 0.95 on
        // models/player/spy/spy_red while the local spy was fully stealthed
        // (GetEffectiveInvisibilityLevel caps the own body as a teammate,
        // c_tf_player.cpp:6854-6862). This is the state the cloak pass draws.
        var data = new PlayerData();
        data.Players[BodyIndex] = FadingSpy(TFCond.Stealthed);
        var (players, _, wishes) = WorldWithBody(data);
        var spy = new Proxies.TFProxySpyInvis(players);

        CloakBound(spy, BodyIndex, out float? cloak, out _, now: 101f);   // fully cloaked

        Assert.Equal(0.95f, cloak);
        Assert.Equal([(0x00010002u, true)], wishes.Asked);
    }

    [Fact]
    public void The_cloak_bind_withdraws_the_bodys_wish_when_the_level_reaches_zero()
    {
        var data = new PlayerData();
        data.Players[BodyIndex] = Carrier();   // visible: no cloak clock armed
        var (players, _, wishes) = WorldWithBody(data);
        var spy = new Proxies.TFProxySpyInvis(players);

        CloakBound(spy, BodyIndex, out float? cloak, out _);

        Assert.Equal(0f, cloak);
        Assert.Equal([(0x00010002u, false)], wishes.Asked);
    }

    [Fact]
    public void A_cloak_bind_on_a_viewmodel_names_no_body()
    {
        // The viewmodel is not the body: its own pass already refreshes the
        // frame buffer, and wishing on it would be wishing on no renderable
        // whose group the policy chooses.
        var data = new PlayerData();
        data.Players[2] = FadingSpy(TFCond.Stealthed);
        var (players, _, wishes) = WorldWithBody(data);
        var spy = new Proxies.TFProxySpyInvis(players);

        CloakBound(spy, ViewModelIndex, out _, out _);

        Assert.Empty(wishes.Asked);
    }

    [Fact]
    public void The_weapon_cloak_writes_its_owners_level()
    {
        var data = new PlayerData();
        data.Players[4] = FadingSpy(TFCond.Stealthed);
        var (players, _) = World(data);
        var weapon = new Proxies.TFProxyWeaponInvis(players);

        // CWeaponInvisProxy reads the level off the move parent's player
        // (tf_weaponbase.cpp:5023-5038): a quarter into the fade.
        CloakBound(weapon, WeaponIndex, out float? cloak, out _, now: 100.25f);
        Assert.Equal(0.25f, cloak);

        // No player behind the model: the default 0 (:5013, :5028).
        CloakBound(weapon, 99, out cloak, out _);
        Assert.Equal(0f, cloak);
    }

    /// <summary>The revolver riding the hands: the attachment (entity 13) whose
    /// move parent is the viewmodel the server named (10), its owner the local spy.</summary>
    private static (Proxies.TFProxyPlayers Players, Engine Screen) WorldWithAttachment(PlayerData data)
    {
        var st = new Standalones();
        var (players, screen) = World(data, st);

        var attachment = new Standalone.ClientSsViewmodelAttachment(screen, screen);
        attachment.State.BindLocal();
        attachment.State.OwnerIndex = 2;
        attachment.State.ViewModelIndex = ViewModelIndex;
        st.ByIndex[AttachmentIndex] = attachment;
        return (players, screen);
    }

    [Fact]
    public void The_weapon_cloak_reaches_the_attachment_through_its_viewmodel()
    {
        var data = new PlayerData();
        data.Players[2] = FadingSpy(TFCond.Stealthed);
        var (players, _) = WorldWithAttachment(data);
        var weapon = new Proxies.TFProxyWeaponInvis(players);

        // The gun drawn in the viewmodel pass is the attachment: stock's walk goes
        // through the non-player move parent's owner entity (tf_weaponbase.cpp:
        // 5019-5024) — a quarter into the fade.
        CloakBound(weapon, AttachmentIndex, out float? cloak, out _, now: 100.25f);
        Assert.Equal(0.25f, cloak);

        // The attachment's own level, before its viewmodel names an owner that
        // exists: stock's 0 (:5013, :5028).
        CloakBound(weapon, 99, out cloak, out _);
        Assert.Equal(0f, cloak);
    }

    [Fact]
    public void The_attachment_whose_viewmodel_has_not_arrived_answers_zero()
    {
        var data = new PlayerData();
        var st = new Standalones();
        var (players, screen) = World(data, st);

        // No move parent yet: stock's 0 (:5012-5014). The viewmodel the server
        // named is not on the client.
        var attachment = new Standalone.ClientSsViewmodelAttachment(screen, screen);
        attachment.State.BindLocal();
        attachment.State.OwnerIndex = 2;
        attachment.State.ViewModelIndex = 77;
        st.ByIndex[AttachmentIndex] = attachment;

        var weapon = new Proxies.TFProxyWeaponInvis(players);
        CloakBound(weapon, AttachmentIndex, out float? cloak, out _);
        Assert.Equal(0f, cloak);
    }

    [Fact]
    public void The_viewmodel_cloak_takes_the_remap_for_the_attachment()
    {
        var data = new PlayerData();
        data.Players[2] = FadingSpy(TFCond.Stealthed);
        var (players, _) = WorldWithAttachment(data);
        var vm = new Proxies.TFProxyVmInvis(players);

        // The attachment's move parent is a viewmodel, so stock's bIsViewModel arm
        // is its (tf_viewmodel.cpp:454-467): the remap into 0.22..0.5, not the
        // plain level.
        CloakBound(vm, AttachmentIndex, out float? cloak, out _, now: 100.25f);
        Assert.Equal(0.22f + 0.25f * 0.28f, cloak!.Value, 4);
    }

    [Fact]
    public void The_viewmodel_cloak_remaps_into_the_022_05_band_and_blinks()
    {
        var data = new PlayerData();
        data.Players[2] = FadingSpy(TFCond.Stealthed);
        var (players, _) = World(data);
        var vm = new Proxies.TFProxyVmInvis(players);

        // Half faded: the remap (tf_viewmodel.cpp:483-486) into 0.22..0.5.
        CloakBound(vm, ViewModelIndex, out float? cloak, out _, now: 100.5f);
        Assert.Equal(0.36f, cloak!.Value, 4);

        // A hundredth below, dropped to 0 (:484-485).
        data.Players[2] = FadingSpy(TFCond.Stealthed);
        data.Players[2].InvisChangeCompleteTime = 100.995f;
        CloakBound(vm, ViewModelIndex, out cloak, out _, now: 100f);
        Assert.Equal(0f, cloak!.Value, 4);

        // A blinking spy's viewmodel: stock's overwrite to 0.3 (:488-492).
        data.Players[2] = FadingSpy(TFCond.Stealthed, TFCond.StealthedBlink);
        CloakBound(vm, ViewModelIndex, out cloak, out _, now: 100.25f);
        Assert.Equal(0.3f, cloak);
    }

    [Fact]
    public void The_generic_cloak_takes_the_vm_arm_for_the_locals_view_and_the_level_for_others()
    {
        var data = new PlayerData();
        data.Players[2] = FadingSpy(TFCond.Stealthed);
        data.Players[4] = FadingSpy(TFCond.Stealthed, TFCond.StealthedBlink);
        var (players, _) = World(data);
        var generic = new Proxies.TFProxyInvisGeneric(players);

        // The local player's own view takes the vm arm (tf_viewmodel.cpp:576-600).
        CloakBound(generic, ViewModelIndex, out float? cloak, out _, now: 100.25f);
        Assert.Equal(0.22f + 0.25f * 0.28f, cloak!.Value, 4);

        // Anyone else takes the effective level (:604) — a blinking enemy spy
        // shows stock's 0.85-scaled level, no remap and no blink overwrite.
        CloakBound(generic, WeaponIndex, out cloak, out _, now: 100.25f);
        Assert.Equal(0.2125f, cloak!.Value, 4);
    }

    [Fact]
    public void The_cloak_proxies_refuse_a_material_without_the_cloak_factor()
    {
        var (players, _) = World();
        var spy = new Proxies.TFProxySpyInvis(players);
        Assert.False(spy.Init(new InitFake { Vars = name => name == "$cloakfactor" ? -1 : 0 }));

        var (weaponPlayers, _) = World();
        var weapon = new Proxies.TFProxyWeaponInvis(weaponPlayers);
        Assert.False(weapon.Init(new InitFake { Vars = name => name == "$cloakfactor" ? -1 : 0 }));
    }

    // ---- the condition proxies ----------------------------------------------

    /// <summary>ResultProxy's single float answer for a bind.</summary>
    private static float BoundLevel(MP.MaterialProxy proxy, nint arg, float now = 0f)
    {
        Assert.True(proxy.Init(new InitFake(ResultVar())));
        var bind = new BindFake { Arg = arg, CurTime = now };
        proxy.Bind(bind);
        return Assert.Single(bind.Floats).Value;
    }

    /// <summary>ResultProxy's single SetVec3 answer for a bind.</summary>
    private static (int Slot, float X, float Y, float Z) Tint(MP.MaterialProxy proxy, nint arg)
    {
        Assert.True(proxy.Init(new InitFake(ResultVar())));
        var bind = new BindFake { Arg = arg };
        proxy.Bind(bind);
        return Assert.Single(bind.Vec3s);
    }

    [Fact]
    public void The_invuln_level_falls_only_while_the_wearing_off_runs()
    {
        var data = new PlayerData();
        data.Players[2] = Carrier(TFCond.Invulnerable, TFCond.InvulnerableWearingoff);
        var (players, _) = World(data);
        var proxy = new Proxies.TFProxyInvulnLevel(players);

        // Stock's wearing-off arm (c_tf_player.cpp:1805-1808): the level falls to 0.
        Assert.Equal(0f, BoundLevel(proxy, ViewModelIndex));

        // Invulnerable with the wearing-off already run, or nobody behind the
        // model: the default 1.0 (:1778-1789, :1812).
        data.Players[2] = Carrier(TFCond.Invulnerable);
        Assert.Equal(1f, BoundLevel(proxy, ViewModelIndex));
        Assert.Equal(1f, BoundLevel(proxy, 0));
    }

    [Fact]
    public void The_burn_level_ramps_to_its_peak_and_fades_over_the_flames_life()
    {
        var data = new PlayerData();
        data.Players[2] = Carrier(TFCond.Burning);
        data.Players[2].BurnEffectStartTime = 100f;
        var (players, _) = World(data);
        var burn = new Proxies.TFProxyBurnLevel(players);

        // Ignited at 100: 0 at the start, half way up 0.15 s in, 1 at the peak
        // (c_tf_player.cpp:1872-1876, :1884).
        Assert.Equal(0f, BoundLevel(burn, ViewModelIndex, now: 100f));
        Assert.Equal(0.5f, BoundLevel(burn, ViewModelIndex, now: 100.15f), 4);
        Assert.Equal(1f, BoundLevel(burn, ViewModelIndex, now: 100.3f), 4);

        // Then down: 0 again once TF_BURNING_FLAME_LIFE (10 s) is out
        // (:1877-1881).
        Assert.Equal(0f, BoundLevel(burn, ViewModelIndex, now: 110f), 4);
    }

    [Fact]
    public void The_burn_level_needs_the_burning_condition_and_a_start()
    {
        var data = new PlayerData();
        var (players, _) = World(data);
        var burn = new Proxies.TFProxyBurnLevel(players);

        // A stale start with the condition gone, a burning player without a
        // clock, and no player behind the model: stock's default 0
        // (:1846, :1852, :1868).
        data.Players[2] = Carrier();
        data.Players[2].BurnEffectStartTime = 100f;
        Assert.Equal(0f, BoundLevel(burn, ViewModelIndex));

        data.Players[2] = Carrier(TFCond.Burning);
        Assert.Equal(0f, BoundLevel(burn, ViewModelIndex));
        Assert.Equal(0f, BoundLevel(burn, 99));
    }

    [Fact]
    public void The_burn_level_ramps_from_the_corpses_own_clock()
    {
        var st = new Standalones();
        var (players, screen) = World(st: st);
        var body = new Standalone.ClientSsRagdoll(screen, screen, screen, st,
                                                  new ClientSide.Ragdolls.NoClientRagdolls());
        body.BindEngine(screen, screen);
        body.State.BindLocal();
        body.State.Burning = true;
        screen.Returns("EntityRender.LookupSequenceByActivity", -1);
        screen.Returns("Effects.ParticleSystemStart", true);
        screen.Returns("Clock.Now", 100f);
        body.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        st.ByIndex[16] = body;
        var burn = new Proxies.TFProxyBurnLevel(players);

        // A burning corpse ramps on ITS OWN clock (c_tf_player.cpp:1857-1865)
        // — the corpse walks to nobody in the owner walk, and the clock is the
        // one the ragdoll stamped on arrival (:928-932). 1 at the peak, half
        // down mid-fade, out once TF_BURNING_FLAME_LIFE (10 s) is spent
        // (:1869-1885).
        Assert.Equal(1f, BoundLevel(burn, 16, now: 100.3f), 4);
        Assert.Equal(0.5f, BoundLevel(burn, 16, now: 105.15f), 4);
        Assert.Equal(0f, BoundLevel(burn, 16, now: 110f), 4);

        // A corpse that arrived cold carries no clock: the default 0 (:1868).
        var cold = new Standalone.ClientSsRagdoll(screen, screen, screen, st,
                                                  new ClientSide.Ragdolls.NoClientRagdolls());
        cold.BindEngine(screen, screen);
        cold.State.BindLocal();
        cold.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        st.ByIndex[17] = cold;
        Assert.Equal(0f, BoundLevel(burn, 17));
    }

    [Fact]
    public void The_jarate_tint_colours_by_the_team_the_view_sees()
    {
        var data = new PlayerData();
        data.Players[2] = Carrier(TFCond.Urine);
        data.Players[4] = Carrier(TFCond.Urine);
        var (players, _) = World(data);
        var proxy = new Proxies.TFProxyYellowLevel(players);

        // A RED carrier in the local RED's view: stock's RED arm
        // (c_tf_player.cpp:1918-1925).
        Assert.Equal((0, 6f, 9f, 2f), Tint(proxy, ViewModelIndex));

        // A BLU carrier: stock's else arm (:1954-1961).
        Assert.Equal((0, 7f, 5f, 1f), Tint(proxy, WeaponIndex));

        // No urine on the carrier, or nobody behind the model: [1 1 1] (:1911).
        data.Players[2] = Carrier();
        Assert.Equal((0, 1f, 1f, 1f), Tint(proxy, ViewModelIndex));
        Assert.Equal((0, 1f, 1f, 1f), Tint(proxy, 0));
    }

    [Fact]
    public void The_jarate_tint_of_a_disguised_spy_shows_the_disguise_team()
    {
        var data = new PlayerData();
        var spy = Carrier(TFCond.Urine, TFCond.Disguised);
        spy.DisguiseTeam = (int)TFTeam.Red;   // a BLU spy disguised RED
        data.Players[4] = spy;
        var (players, _) = World(data);
        var proxy = new Proxies.TFProxyYellowLevel(players);

        // The local RED's view follows the disguise team (:1946-1953).
        Assert.Equal((0, 6f, 9f, 2f), Tint(proxy, WeaponIndex));
    }

    [Fact]
    public void The_heartbeat_scale_pulses_between_the_stock_bounds()
    {
        var proxy = new Proxies.TFProxyHeartbeatScale();
        Assert.True(proxy.Init(new InitFake(ResultVar())));

        // The stronger of two beats a quarter cycle apart, scaled into
        // 1.0*0.6 .. 0.8*0.6 (c_tf_player.cpp:2193-2207).
        var trough = new BindFake { Arg = 0, CurTime = 0.5f };
        proxy.Bind(trough);
        Assert.Equal(0.6f, Assert.Single(trough.Floats).Value);

        var peak = new BindFake { Arg = 0, CurTime = 0.25f };
        proxy.Bind(peak);
        Assert.Equal(0.8f * 0.6f, Assert.Single(peak.Floats).Value);
    }

    [Fact]
    public void The_shield_falloff_answers_the_rest_vector()
    {
        var proxy = new Proxies.TFProxyShieldFalloff();
        Assert.True(proxy.Init(new InitFake(ResultVar())));

        // GetLastResistTime is on no carrier (docs/plans/tf2/deferred.tsv):
        // stock's rest value (c_tf_player.cpp:2415-2419).
        var bind = new BindFake { Arg = 0 };
        proxy.Bind(bind);
        Assert.Equal([1f, 1f, 1f, 1f], Assert.Single(bind.Vecs));
    }

    [Fact]
    public void The_sapper_eyes_hold_the_fresh_static()
    {
        var proxy = new Proxies.TFProxyWheatlyEyeGlow();

        // IsWheatleyTalking and the eyelids pose parameter are on no carrier
        // (docs/plans/tf2/deferred.tsv); the instance holds stock's fresh
        // s_flCurr 0.2 (c_tf_player.cpp:2443-2453).
        Assert.Equal(0.2f, BoundLevel(proxy, 0));
        Assert.Equal(0.2f, BoundLevel(proxy, ViewModelIndex));
    }

    // ---- the item proxies ---------------------------------------------------

    /// <summary>A bind through any proxy, for the arms that record nothing.</summary>
    private static BindFake Bound(MP.MaterialProxy proxy, nint arg)
    {
        Assert.True(proxy.Init(new InitFake(ResultVar())));
        var bind = new BindFake { Arg = arg };
        proxy.Bind(bind);
        return bind;
    }

    [Fact]
    public void The_community_weapon_answers_zero_until_item_quality_lands()
    {
        var proxy = new Proxies.TFProxyCommunityWeapon();

        // The econ item view reaches no client (docs/plans/tf2/deferred.tsv):
        // stock's no-item arm SetIntValue(0) (c_tf_player.cpp:2153).
        Assert.Equal((0, 0), Assert.Single(Bound(proxy, 0).Ints));
        Assert.Equal((0, 0), Assert.Single(Bound(proxy, ViewModelIndex).Ints));
    }

    [Fact]
    public void The_benefactor_heart_keeps_its_scale_until_the_kill_eater_lands()
    {
        var proxy = new Proxies.TFProxyBenefactorLevel();

        // The kill-eater attribute is on no client: stock returns without
        // writing (c_tf_player.cpp:2248-2249).
        var bind = Bound(proxy, ViewModelIndex);
        Assert.Empty(bind.Floats);
        Assert.Empty(bind.Vecs);
    }

    [Fact]
    public void The_item_tint_answers_the_default_paint()
    {
        var proxy = new Proxies.TFProxyItemTintColor();

        // No modified RGB without the item view: stock's Vector(0,0,0)
        // (econ_wearable.cpp:471, :540).
        Assert.Equal((0, 0f, 0f, 0f), Tint(proxy, 0));
        Assert.Equal((0, 0f, 0f, 0f), Tint(proxy, ViewModelIndex));
    }

    [Fact]
    public void The_weapon_skin_keeps_the_base_texture_until_a_skin_lands()
    {
        var proxy = new Proxies.TFProxyWeaponSkin();

        // A material without $basetexture refuses (:3454-3457).
        Assert.False(proxy.Init(new InitFake { Vars = name => name == "$basetexture" ? -1 : 0 }));
        Assert.True(proxy.Init(new InitFake()));

        // No skin to composite: stock's no-item exit (c_tf_player.cpp:3484-3486).
        var bind = Bound(proxy, ViewModelIndex);
        Assert.Empty(bind.Floats);
        Assert.Empty(bind.Ints);
    }

    [Fact]
    public void The_object_power_writes_the_scale_for_a_building()
    {
        var st = new Standalones();
        st.ByIndex[BuildingIndex] = new TFClient.Buildings.ClientTFSentryGun(new Engine());
        var (players, _) = World(st: st);

        var proxy = new Proxies.TFProxyObjectPower(players);
        Assert.True(proxy.Init(new InitFake(ResultVar())));

        // The factor is the "scale" keyvalue, default 1 (c_baseobject.cpp:1161,
        // :1176) — the power itself rides no carrier in stock either.
        var bind = new BindFake { Arg = BuildingIndex };
        proxy.Bind(bind);
        Assert.Equal((0, 1f), Assert.Single(bind.Floats));

        var scaled = new Proxies.TFProxyObjectPower(players);
        Assert.True(scaled.Init(new InitFake(ResultVar(),
            new MP.ProxyKey("scale", MP.ProxyKeyType.Float, "", 2.5f, 2, 0, 0, 0, 0))));
        var scaledBind = new BindFake { Arg = BuildingIndex };
        scaled.Bind(scaledBind);
        Assert.Equal((0, 2.5f), Assert.Single(scaledBind.Floats));
    }

    [Fact]
    public void The_object_power_leaves_the_result_when_the_bind_is_no_building()
    {
        var (players, _) = World();
        var proxy = new Proxies.TFProxyObjectPower(players);
        Assert.True(proxy.Init(new InitFake(ResultVar())));

        // Stock's cast-fail return (c_baseobject.cpp:1172-1174): a player's
        // model, and a bind with no entity, write nothing.
        var bind = new BindFake { Arg = ViewModelIndex };
        proxy.Bind(bind);
        Assert.Empty(bind.Floats);

        var none = new BindFake { Arg = 0 };
        proxy.Bind(none);
        Assert.Empty(none.Floats);
    }

    // ---- the StatTrak proxies ----------------------------------------------

    [Fact]
    public void The_stat_illum_leaves_the_result_until_a_module_mirrors()
    {
        var proxy = new Proxies.TFProxyStatTrakIllum();

        // The module's move-parent viewmodel reaches no client
        // (docs/plans/tf2/deferred.tsv): stock's fall-through
        // (c_tf_player.cpp:3092, :3106-3108).
        var bind = Bound(proxy, ViewModelIndex);
        Assert.Empty(bind.Floats);
    }

    [Fact]
    public void The_stat_digit_answers_zero_until_the_score_lands()
    {
        var proxy = new Proxies.TFProxyStatTrakDigit();

        // The kill-eater score is on no client: stock's no-score arm
        // (c_tf_player.cpp:3240-3245).
        Assert.Equal((0, 0f), Assert.Single(Bound(proxy, 0).Floats));
        Assert.Equal((0, 0f), Assert.Single(Bound(proxy, ViewModelIndex).Floats));
    }

    [Fact]
    public void The_stat_icon_answers_the_identity_transform()
    {
        var proxy = new Proxies.TFProxyStatTrakIcon();

        // Stock's mat.Identity() with the offset convars at 0
        // (c_tf_player.cpp:3326-3361).
        var mat = Assert.Single(Bound(proxy, ViewModelIndex).Mats);
        Assert.Equal(1f, mat[0, 0]);
        Assert.Equal(1f, mat[1, 1]);
        Assert.Equal(1f, mat[2, 2]);
        Assert.Equal(1f, mat[3, 3]);
        Assert.Equal(0f, mat[0, 3]);
        Assert.Equal(0f, mat[1, 3]);
        Assert.Equal(0f, mat[3, 0]);
    }

    // ---- the weapon sheen ---------------------------------------------------

    [Fact]
    public void The_weapon_sheen_refuses_a_material_without_its_nine_vars()
    {
        var proxy = new Proxies.TFProxyAnimatedWeaponSheen();

        // Stock's refusals in order (c_tf_player.cpp:2619-2655), one missing
        // var at a time.
        Assert.False(proxy.Init(new InitFake { Vars = name => name == "$sheenmaptint" ? -1 : 0 }));
        Assert.False(proxy.Init(new InitFake { Vars = name => name == "$sheenindex" ? -1 : 0 }));
        Assert.False(proxy.Init(new InitFake { Vars = name => name == "$sheenmapmaskscaleX" ? -1 : 0 }));
        Assert.False(proxy.Init(new InitFake { Vars = name => name == "$sheenmapmaskoffsetY" ? -1 : 0 }));
        Assert.False(proxy.Init(new InitFake { Vars = name => name == "$sheenmapmaskdirection" ? -1 : 0 }));
        Assert.False(proxy.Init(new InitFake { Vars = name => name == "$sheenmap" ? -1 : 0 }));
        Assert.False(proxy.Init(new InitFake { Vars = name => name == "$sheenmapmask" ? -1 : 0 }));
    }

    [Fact]
    public void The_weapon_sheen_runs_the_no_proxy_arm_until_the_killstreak_lands()
    {
        var proxy = new Proxies.TFProxyAnimatedWeaponSheen();
        Assert.True(proxy.Init(new InitFake()));

        // The animated texture var not a texture yet: stock's early return
        // (c_tf_player.cpp:2662-2664) — nothing is written.
        var early = new BindFake { Arg = ViewModelIndex };
        proxy.Bind(early);
        Assert.Empty(early.Vec3s);
        Assert.Empty(early.Ints);

        // A texture with frames, no killstreak attribute: RunNoProxy — the
        // black tint, frame 0 and sheen index 0 (:2831-2835, :3026-3031).
        var bound = new BindFake
        {
            Arg = ViewModelIndex,
            NextVar = new CI.ProxyVar { Type = 3 },
            NextFrames = 10,
        };
        proxy.Bind(bound);
        Assert.Equal((0, 0f, 0f, 0f), Assert.Single(bound.Vec3s));
        Assert.Equal([(0, 0), (0, 0)], bound.Ints);
    }

    // ---- the team, building and charge proxies ------------------------------

    [Fact]
    public void The_team_texture_frames_by_the_bound_players_team()
    {
        var data = new PlayerData();
        data.Players[2] = Carrier();
        data.Players[4] = Carrier();
        var (players, _) = World(data);
        var proxy = new Proxies.TFProxyTeamTexture(players);
        Assert.True(proxy.Init(new InitFake()));

        // The BLU weapon (entity 11, owner 4): team 3 - 2 = frame 1
        // (teammaterialproxy.cpp:72-76).
        var blu = new BindFake { Arg = WeaponIndex };
        proxy.Bind(blu);
        Assert.Equal((0, 1), Assert.Single(blu.Ints));

        // The RED viewmodel (entity 10, owner 2): frame 0.
        var (redPlayers, _) = World(data);
        var red = new Proxies.TFProxyTeamTexture(redPlayers);
        Assert.True(red.Init(new InitFake()));
        var redBind = new BindFake { Arg = ViewModelIndex };
        red.Bind(redBind);
        Assert.Equal((0, 0), Assert.Single(redBind.Ints));

        // Nobody behind the model: nothing is written (:69-70).
        var (plainPlayers, _) = World(data);
        var plain = new Proxies.TFProxyTeamTexture(plainPlayers);
        Assert.True(plain.Init(new InitFake()));
        var plainBind = new BindFake { Arg = 99 };
        plain.Bind(plainBind);
        Assert.Empty(plainBind.Ints);
    }

    [Fact]
    public void The_team_texture_answers_true_without_the_frame_var()
    {
        var (players, _) = World();
        var proxy = new Proxies.TFProxyTeamTexture(players);

        // Stock's Init never refuses (teammaterialproxy.cpp:53-62); without
        // the var no bind writes.
        Assert.True(proxy.Init(new InitFake { Vars = name => name == "$frame" ? -1 : 0 }));
        var bind = new BindFake { Arg = WeaponIndex };
        proxy.Bind(bind);
        Assert.Empty(bind.Ints);
    }

    [Fact]
    public void The_building_cloak_writes_zero_for_a_building_and_nothing_otherwise()
    {
        var st = new Standalones();
        st.ByIndex[BuildingIndex] = new TFClient.Buildings.ClientTFDispenser(new Engine());
        var (players, _) = World(st: st);
        var proxy = new Proxies.TFProxyBuildingInvis(players);
        Assert.True(proxy.Init(new InitFake()));

        // The owner-less arm 0 (c_baseobject.cpp:1237-1239); GetInvisibilityLevel
        // (:1242) is a dead field in stock's drop — no writer, no recv prop
        // (c_baseobject.cpp:1089-1111) — so the owner arm answers 0 too.
        var building = new BindFake { Arg = BuildingIndex };
        proxy.Bind(building);
        Assert.Equal((0, 0f), Assert.Single(building.Floats));

        // Stock's IsBaseObject-fail return (:1228-1229): a player's model and
        // a bind with no entity write nothing.
        var player = new BindFake { Arg = ViewModelIndex };
        proxy.Bind(player);
        Assert.Empty(player.Floats);

        var none = new BindFake { Arg = 0 };
        proxy.Bind(none);
        Assert.Empty(none.Floats);
    }

    [Fact]
    public void The_rescue_level_leaves_the_result_until_the_ammo_lands()
    {
        var proxy = new Proxies.TFProxyBuildingRescueLevel();

        // The attribute, the active weapon and TF_AMMO_METAL reach no client
        // (docs/plans/tf2/deferred.tsv): stock's early returns
        // (c_tf_player.cpp:2324-2335).
        var bind = Bound(proxy, ViewModelIndex);
        Assert.Empty(bind.Floats);
        Assert.Empty(bind.Mats);
    }

    /// <summary>The replay: the local player's rifle predicted, nobody else's.</summary>
    private sealed class DrawStates : ClientSide.WeaponEffects.IWeaponDrawStates
    {
        public readonly Dictionary<Standalone.ClientSsWeapon, client::SourceSharp.Shared.WeaponNetState?> Held = [];

        public client::SourceSharp.Shared.WeaponNetState? PredictedStateOf(Standalone.ClientSsWeapon weapon)
            => Held.GetValueOrDefault(weapon);
    }

    /// <summary>Two TF2 rows for the wire table, the way the mod's source appends them.</summary>
    private sealed class TestTypes : client::SourceSharp.Shared.IWeaponTypeSource
    {
        public IEnumerable<client::SourceSharp.Shared.WeaponType> Rows()
        {
            yield return new("tf_weapon_sniperrifle", 0.5f);
            yield return new("tf_weapon_medigun", 0.5f);
        }
    }

    private static client::SourceSharp.Shared.WeaponCatalog TestCatalog() =>
        new(() => [new TestTypes()]);

    /// <summary>The charge proxy over a world whose local player (2) carries a
    /// rifle at index 15 — a fresh <see cref="DrawStates"/> beside it. BLU's
    /// weapon at 11 becomes a rifle when <paramref name="bluRifle"/> asks for
    /// the spectator arm; <paramref name="rifle"/> false leaves the local
    /// player empty-handed.</summary>
    private static (Proxies.TFProxySniperRifleCharge Proxy, Engine Screen, DrawStates States,
        Standalones Standalones) ChargeRig(Standalones? st = null, bool bluRifle = false, bool rifle = true)
    {
        st ??= new Standalones();
        int rifleId = TestCatalog().IndexOf("tf_weapon_sniperrifle");

        var (players, screen) = World(st: st);

        if (rifle)
        {
            var held = new Standalone.ClientSsWeapon(new Engine());
            held.State.BindLocal();
            held.State.OwnerIndex = 2;
            held.State.WeaponTypeId = rifleId;
            st.ByIndex[15] = held;
        }

        if (bluRifle)
        {
            var blu = new Standalone.ClientSsWeapon(new Engine());
            blu.State.BindLocal();
            blu.State.OwnerIndex = 4;
            blu.State.WeaponTypeId = rifleId;
            st.ByIndex[WeaponIndex] = blu;
        }

        var states = new DrawStates();
        var proxy = new Proxies.TFProxySniperRifleCharge(players, TestCatalog(), states);
        Assert.True(proxy.Init(new InitFake(ResultVar())));
        return (proxy, screen, states, st);
    }

    [Fact]
    public void The_sniper_charge_transform_tracks_the_carriers_charge()
    {
        var (proxy, _, states, st) = ChargeRig();

        // A one-second-old charge start read at now 10: charge 50 of the 150
        // max (TFSniperRifleBehaviour.Charge), GetHUDDamagePerc 1/3, so the y
        // offset is (1-1/3)*0.8+0.6 (tf_weapon_sniperrifle.cpp:1311, :1339).
        // The transform is the centre-anchored scale then the y translation
        // (:1343-1363): [0,0] 1, [1,1] 0.25, [0,3] 0, [1,3] 0.375+offset.
        var rifle = (Standalone.ClientSsWeapon)st.ByIndex[15];
        rifle.State.BehaviourMeter = 9f;
        var bind = new BindFake { Arg = ViewModelIndex, CurTime = 10f };
        proxy.Bind(bind);
        var mat = Assert.Single(bind.Mats);
        Assert.Equal(1f, mat[0, 0], 4);
        Assert.Equal(0.25f, mat[1, 1], 4);
        Assert.Equal(0f, mat[0, 3], 4);
        Assert.Equal(0.375f + (1f - 50f / 150f) * 0.8f + 0.6f, mat[1, 3], 4);

        // The local player's own replay wins while it is fresh: a prediction
        // begun a second later holds its own charge, not the decode's.
        var fresh = new client::SourceSharp.Shared.WeaponNetState();
        fresh.BindLocal();
        states.Held[rifle] = fresh;
        rifle.State.BehaviourMeter = 9.5f;
        fresh.BehaviourMeter = 9.5f;
        var replayed = new BindFake { Arg = ViewModelIndex, CurTime = 10f };
        proxy.Bind(replayed);
        Assert.Equal(0.375f + (1f - 25f / 150f) * 0.8f + 0.6f, Assert.Single(replayed.Mats)[1, 3], 4);
    }

    [Fact]
    public void The_sniper_charge_reads_the_spectators_in_eye_target()
    {
        var st = new Standalones();
        var viewer = new Standalone.ClientSsPlayer(new Engine());
        viewer.State.BindLocal();
        viewer.State.ObserverMode = client::SourceSharp.Abi.GameMovement.ObsModeInEye;
        viewer.State.ObserverTarget = 4; // the raw handle's entry: BLU at index 4
        st.ByIndex[2] = viewer;

        var (proxy, _, _, _) = ChargeRig(st, bluRifle: true);

        // Stock swaps the read to UTIL_PlayerByIndex(GetSpectatorTarget())
        // (:1329-1332): BLU's own rifle, here uncharged, offset 1.4.
        var bind = new BindFake { Arg = ViewModelIndex, CurTime = 10f };
        proxy.Bind(bind);
        var mat = Assert.Single(bind.Mats);
        Assert.Equal(0.375f + (1f - 0f / 150f) * 0.8f + 0.6f, mat[1, 3], 4);
    }

    [Fact]
    public void The_sniper_charge_writes_nothing_without_a_carried_rifle()
    {
        // Empty hands: stock's pPlayer-with-no-weapon arm (:1334-1365) writes
        // nothing.
        var (proxy, _, _, _) = ChargeRig(rifle: false);
        var unarmed = new BindFake { Arg = ViewModelIndex, CurTime = 10f };
        proxy.Bind(unarmed);
        Assert.Empty(unarmed.Mats);

        // A carried weapon that is not a rifle: the same arm.
        var st = new Standalones();
        var (medicProxy, _, _, _) = ChargeRig(st, rifle: false);
        var gun = new Standalone.ClientSsWeapon(new Engine());
        gun.State.BindLocal();
        gun.State.OwnerIndex = 2;
        gun.State.WeaponTypeId = TestCatalog().IndexOf("tf_weapon_medigun");
        st.ByIndex[15] = gun;
        var medic = new BindFake { Arg = ViewModelIndex, CurTime = 10f };
        medicProxy.Bind(medic);
        Assert.Empty(medic.Mats);
    }

    [Fact]
    public void The_stickybomb_glow_answers_white_until_highlighting_lands()
    {
        var proxy = new Proxies.TFProxyStickybombGlowColor();

        // No pipebomb mirror: the not-a-pipebomb fall-through
        // (tf_weapon_grenade_pipebomb.cpp:1298, :1342).
        Assert.Equal((0, 1f, 1f, 1f), Tint(proxy, 0));
        Assert.Equal((0, 1f, 1f, 1f), Tint(proxy, WeaponIndex));
    }
}
