using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using System.Runtime.InteropServices;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The spawn mispredictions and their adoption: each class join deploys
/// TWICE (the joinclass instant's ForceRespawn, then the round start's
/// respawn), and the second Deploy runs between two client commands — no
/// command carries a weaponselect for it, so MirrorDeploy never runs and
/// the acked ring slot holds deploy-1's clocks while the arrival carries
/// deploy-2's and a flipped parity. Those boots were the class-spawn
/// <c>MISPREDICTION on 'ss_weapon' — NextPrimaryAttack, …</c> lines.
///
/// <para>
/// Stock reports nothing at that moment because the weapon entity is
/// recreated at respawn and the client's <c>InitPredictable</c> seeds every
/// predicted slot from the fresh arrival ("so we don't get an error saying
/// we mispredicted any values which are still at their initial values",
/// <c>c_baseentity.cpp:4578-4583</c>). Our ss pair persists, so
/// <see cref="WeaponNetState.DeployEpoch"/> stands in for the recreation:
/// a stamp Arsenal.Deploy bumps and MirrorDeploy answers. An arrival whose
/// stamp stands away from the acked slot's is adopted, not judged — and
/// only then: the stamp moves on a Deploy alone, so a clock that moved
/// without one still stands judged.
/// </para>
/// </summary>
public class ClientArsenalDeployAdoptTests
{
    private const int LocalPlayer = 1;

    private sealed class Pair : IClientStandalones
    {
        public readonly ClientEntityDirectory Directory = new();

        public IReadOnlyList<ClientStandaloneEntity> Live()
            => throw new InvalidOperationException("the arsenal finds its pair by class");

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => Directory.ByIndex(entityIndex);

        public IEnumerable<ClientStandaloneEntity> OfClass(string? className)
            => Directory.OfClass(className);

        public IEnumerable<ClientStandaloneEntity> OfName(string? name)
            => Directory.OfName(name);
    }

    private sealed class Rig
    {
        public readonly RecordingEngine Engine = Fake();
        public readonly Pair Standalones = new();
        public readonly ClientSsWeapon Weapon;
        public readonly ClientSsViewModel ViewModel;
        public readonly RecordingLog Log = new();
        public readonly ClientArsenal Arsenal;
        public readonly EntityHandle Player = new(0x0001_0000u | LocalPlayer);

        /// <summary>A globals block with the one field the command clock reads: the tick's interval.</summary>
        private static nint TickGlobals()
        {
            nint block = Marshal.AllocHGlobal(Marshal.SizeOf<CGlobalVarsRaw>());
            for (int i = 0; i < Marshal.SizeOf<CGlobalVarsRaw>(); i++)
                Marshal.WriteByte(block, i, 0);
            Marshal.WriteInt32(block,
                (int)Marshal.OffsetOf<CGlobalVarsRaw>(nameof(CGlobalVarsRaw.interval_per_tick)),
                BitConverter.SingleToInt32Bits(0.015f));
            return block;
        }

        public Rig(int pistol, int clip = 18, ILocalAmmoPools? pools = null, ILocalWeaponClips? clips = null,
                   ViewmodelSkin? skin = null, WeaponBehaviours? behaviours = null)
        {
            Engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);
            Engine.Returns("Clock.Now", 5000f);
            Engine.Returns("Environment.Globals", new Globals(TickGlobals()));
            Engine.Returns("EntityRender.LookupSequenceByActivity", 0);

            Weapon = new ClientSsWeapon(Engine) { Handle = new EntityHandle(0x0100_0041u) };
            Weapon.State.BindLocal();
            Weapon.State.OwnerIndex = LocalPlayer;
            Weapon.State.WeaponTypeId = pistol;
            Weapon.State.Clip = clip;
            Weapon.State.Reserve = 30;
            Weapon.State.DeployEpoch = 1;          // the spawn kit's deploy
            Weapon.State.NextPrimaryAttack = 10.5f;
            Weapon.State.NextSecondaryAttack = 10.5f;
            Weapon.State.TimeWeaponIdle = 1000f;   // nothing due: the command idles

            ViewModel = new ClientSsViewModel(Engine, Engine) { Handle = new EntityHandle(0x0100_0042u) };
            ViewModel.State.BindLocal();
            ViewModel.State.OwnerIndex = LocalPlayer;
            ViewModel.State.AnimationParity = 1;   // deploy-1's flip

            Standalones.Directory.Bind(Weapon.Handle, -1, Weapon);
            Standalones.Directory.Bind(ViewModel.Handle, -1, ViewModel);

            Arsenal = new ClientArsenal(Log, Standalones, Engine, Engine, Engine,
                                        NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog()),
                                        behaviours: behaviours,
                                        localAmmoPools: pools,
                                        localWeaponClips: clips,
                                        viewmodelSkin: skin,
                                        environment: Engine);
        }

        /// <summary>The server's word arriving: decode, then a fresh OnDataChanged.</summary>
        public void Arrive(WeaponNetState weapon, ViewModelNetState viewModel)
        {
            Weapon.State.Decode(weapon.Snapshot());
            Weapon.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
            ViewModel.State.Decode(viewModel.Snapshot());
            ViewModel.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
        }
    }

    /// <summary>A skin chain of one rule, every weapon the same answer.</summary>
    private sealed class Fixed(int skin) : IViewmodelSkinRule
    {
        public int? Skin(int playerIndex, WeaponType type, int? chosen) => skin;
    }

    /// <summary>The same rule answering no opinion: what stands, stands.</summary>
    private sealed class Passing : IViewmodelSkinRule
    {
        public int? Skin(int playerIndex, WeaponType type, int? chosen) => chosen;
    }

    private sealed class OneRule(params IViewmodelSkinRule[] rules) : IHooks<IViewmodelSkinRule>
    {
        public IReadOnlyList<IViewmodelSkinRule> All => rules;

        public void Each(string what, Action<IViewmodelSkinRule> call)
        {
            foreach (var rule in rules)
                call(rule);
        }
    }

    private static WeaponNetState ServerWeapon(Rig rig, int epoch, float deploy)
    {
        var w = new WeaponNetState();
        w.BindLocal();
        w.OwnerIndex = LocalPlayer;
        w.WeaponTypeId = rig.Weapon.State.WeaponTypeId;
        w.Clip = rig.Weapon.State.Clip;
        w.Reserve = rig.Weapon.State.Reserve;
        w.DeployEpoch = epoch;
        w.NextPrimaryAttack = deploy;
        w.NextSecondaryAttack = deploy;
        w.TimeWeaponIdle = deploy + 2f;
        return w;
    }

    private static ViewModelNetState ServerViewModel(Rig rig, int parity)
    {
        var vm = new ViewModelNetState();
        vm.BindLocal();
        vm.OwnerIndex = LocalPlayer;
        vm.AnimationParity = parity;
        return vm;
    }

    [Fact]
    public void AnUnmirroredServerRedeployIsAdoptedNotMisjudged()
    {
        int pistol = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol);
        var rig = new Rig(pistol);

        // The spawn kit's deploy is the state the client joined with; one
        // live command then puts deploy-1's words into the ring at command 11.
        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 11 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, true, commandTime: 20f);

        // The round start's respawn: the server Deploys again between two
        // client commands — no weaponselect carried it, so the client never
        // mirrored it. The clocks move on their deploy time, and the stamp
        // and the viewmodel's parity move with them.
        rig.Arrive(ServerWeapon(rig, epoch: 2, deploy: 20.05f), ServerViewModel(rig, parity: 2));

        var ack = new UserCmd { CommandNumber = 11 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in ack, 2, false);

        var (saves, holds, tolerated, mispredicts, noBasis, rebuilt) = rig.Arsenal.Counts;
        Assert.Equal(1, saves);
        Assert.Equal(1, rebuilt);
        Assert.True(mispredicts == 0 && holds == 0 && tolerated == 0,
            $"the redeploy is neither judged nor misjudged: holds {holds}, " +
            $"tolerated {tolerated}, mispredicts {mispredicts}");
        Assert.Equal(2, noBasis);   // command 10's ack had nothing saved either
        Assert.DoesNotContain("MISPREDICTION", rig.Log.Text);

        // Adopted, not merely forgiven: the working copies now carry the
        // server's redeployed clocks and stamp, which the replay re-predicts
        // from — stock's fresh-arrival seeding, the same adoption.
        Assert.Equal(20.05f, rig.Arsenal.WorkingWeapon.NextPrimaryAttack, 4);
        Assert.Equal(2, rig.Arsenal.WorkingWeapon.DeployEpoch);
        Assert.Equal(2, rig.Arsenal.WorkingViewModel.AnimationParity);
    }

    [Fact]
    public void ClocksThatMovedWithoutADeployStampStillStandJudged()
    {
        int pistol = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol);
        var rig = new Rig(pistol);

        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 11 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, true, commandTime: 20f);

        // A server write with no Deploy behind it — the stamp stands at 1
        // while the clocks move: the meter's whole reason to exist. It must
        // still go red, stamp-adoption or not.
        rig.Arrive(ServerWeapon(rig, epoch: 1, deploy: 21.0f), ServerViewModel(rig, parity: 1));

        var ack = new UserCmd { CommandNumber = 11 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in ack, 2, false);

        var (saves, holds, tolerated, mispredicts, _, rebuilt) = rig.Arsenal.Counts;
        Assert.Equal(1, saves);
        Assert.Equal(0, rebuilt);
        Assert.Equal(1, mispredicts);   // the weapon ring; the viewmodel holds
        Assert.Equal(1, holds);
        Assert.Contains("MISPREDICTION on 'ss_weapon'", rig.Log.Text);
    }

    [Fact]
    public void AMirroredSwitchCarriesTheStampAndStandsFullyJudged()
    {
        var catalog = WeaponCatalog.Stock();
        int pistol = catalog.IndexOf(EntityNames.WeaponPistol);
        int crowbar = catalog.IndexOf(EntityNames.WeaponCrowbar);

        var rig = new Rig(pistol);

        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        // The switch the client predicts on its own command: MirrorDeploy
        // re-dresses the working copies as Arsenal.Deploy dressed the
        // entities — the stamp with the clocks, so the two sides land on the
        // same one and the ack stands fully judged.
        var cmd = new UserCmd { CommandNumber = 11, WeaponSelect = crowbar + 1 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, true, commandTime: 10f);

        Assert.Equal(2, rig.Arsenal.WorkingWeapon.DeployEpoch);   // decoded 1 + the mirrored Deploy

        // The server's run of the same command wrote the same words: deliver
        // exactly what the prediction computed, and the compare must JUDGE
        // (hold), not adopt — a mirrored switch is nothing to reseed.
        var weapon = new WeaponNetState();
        weapon.BindLocal();
        weapon.Decode(rig.Arsenal.WorkingWeapon.Snapshot());
        var viewModel = new ViewModelNetState();
        viewModel.BindLocal();
        viewModel.Decode(rig.Arsenal.WorkingViewModel.Snapshot());
        rig.Arrive(weapon, viewModel);

        var ack = new UserCmd { CommandNumber = 11 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in ack, 2, false);

        var (saves, holds, tolerated, mispredicts, _, rebuilt) = rig.Arsenal.Counts;
        Assert.Equal(1, saves);
        Assert.Equal(0, rebuilt);
        Assert.True(holds == 2 && mispredicts == 0 && tolerated == 0,
            $"both rings judged and held: holds {holds}, tolerated {tolerated}, " +
            $"mispredicts {mispredicts}");
    }

    [Fact]
    public void AMirroredSwitchAWeaponCannotDeployWritesNothing()
    {
        // The mirror's own CanDeploy ask (stock's Weapon_CanSwitchTo through
        // Weapon_Switch, basecombatcharacter_shared.cpp:88): a behaviour that
        // refuses the deploy holds the switch — nothing is written, the held
        // weapon and its deploy stamp stand as the command found them, the
        // server's word to correct them by.
        int pistol = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol);
        int ar2 = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponAr2);

        var rig = new Rig(pistol, behaviours: new WeaponBehaviours(
            () => new IWeaponBehaviourSource[] { new RefusingAr2() }));

        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 11, WeaponSelect = ar2 + 1 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, true, commandTime: 10f);

        Assert.Equal(pistol, rig.Arsenal.WorkingWeapon.WeaponTypeId);
        Assert.Equal(1, rig.Arsenal.WorkingWeapon.DeployEpoch);
    }

    [Fact]
    public void AHolsteredItemHoldsTheMirroredSwitchWriteUntilTheWindowPasses()
    {
        // Stock's order on the mirrored press (tf_player_shared.cpp:11798-11826):
        // the held item's holster_anim_time arms the window FIRST and the
        // branch returns before anything is asked or written (:11815); the
        // press the window expired on clears the window (:11825) and walks on
        // with the switch it was waiting for. The mirror matches, so its rings
        // carry the server's bytes on the same presses.
        var catalog = WeaponCatalog.Stock();
        int pistol = catalog.IndexOf(EntityNames.WeaponPistol);
        int crowbar = catalog.IndexOf(EntityNames.WeaponCrowbar);

        var rig = new Rig(pistol, behaviours: new WeaponBehaviours(
            () => new IWeaponBehaviourSource[] { new Holstered() }));

        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        // The first press: the window arms, nothing is written yet — the held
        // weapon and its stamp stand on, as on the server's Deferred.
        var cmd = new UserCmd { CommandNumber = 11, WeaponSelect = crowbar + 1 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, true, commandTime: 10f);

        Assert.Equal(pistol, rig.Arsenal.WorkingWeapon.WeaponTypeId);
        Assert.Equal(1, rig.Arsenal.WorkingWeapon.DeployEpoch);

        // Past the window: it clears, the press walks on, and the mirror
        // writes the switch — the stamp with the clocks, on the next one.
        var next = new UserCmd { CommandNumber = 12, WeaponSelect = crowbar + 1 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in next, 1, true, commandTime: 10.6f);

        Assert.Equal(crowbar, rig.Arsenal.WorkingWeapon.WeaponTypeId);
        Assert.Equal(2, rig.Arsenal.WorkingWeapon.DeployEpoch);
    }

    [Fact]
    public void AHolsteredItemRunsTheWindowBeforeTheRefusedCandidateAnswers()
    {
        // The refusal the melted knife's CanDeploy is (:523 through
        // Weapon_CanSwitchTo) is asked BEHIND the window: the press inside the
        // window writes nothing with the refusal not even consulted, and the
        // press past it stands the held weapon too, the window cleared first.
        var catalog = WeaponCatalog.Stock();
        int pistol = catalog.IndexOf(EntityNames.WeaponPistol);
        int ar2 = catalog.IndexOf(EntityNames.WeaponAr2);

        var rig = new Rig(pistol, behaviours: new WeaponBehaviours(
            () => new IWeaponBehaviourSource[] { new Holstered(), new RefusingAr2() }));

        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 11, WeaponSelect = ar2 + 1 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, true, commandTime: 10f);

        Assert.Equal(pistol, rig.Arsenal.WorkingWeapon.WeaponTypeId);
        Assert.Equal(1, rig.Arsenal.WorkingWeapon.DeployEpoch);

        var next = new UserCmd { CommandNumber = 12, WeaponSelect = ar2 + 1 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in next, 1, true, commandTime: 10.6f);

        Assert.Equal(pistol, rig.Arsenal.WorkingWeapon.WeaponTypeId);
        Assert.Equal(1, rig.Arsenal.WorkingWeapon.DeployEpoch);
    }

    /// <summary>The pistol's behaviour, carrying half a second of holster anim on its held item.</summary>
    private sealed class HolsteredPistolBehaviour : IWeaponBehaviour
    {
        public float HolsterSeconds(in WeaponType type, int ownerIndex) => 0.5f;
    }

    private sealed class Holstered : IWeaponBehaviourSource
    {
        public IEnumerable<KeyValuePair<string, IWeaponBehaviour>> Behaviours()
        {
            yield return KeyValuePair.Create<string, IWeaponBehaviour>(Hl2mpWeaponBehaviours.Pistol, new HolsteredPistolBehaviour());
        }
    }

    /// <summary>The AR2's behaviour, refusing every deploy: the stand-in refusal.</summary>
    private sealed class RefusingAr2Behaviour : IWeaponBehaviour
    {
        public bool CanDeploy(in WeaponAttackContext attack) => false;
    }

    private sealed class RefusingAr2 : IWeaponBehaviourSource
    {
        public const string Key = Hl2mpWeaponBehaviours.Ar2;

        public IEnumerable<KeyValuePair<string, IWeaponBehaviour>> Behaviours()
        {
            yield return KeyValuePair.Create<string, IWeaponBehaviour>(Key, new RefusingAr2Behaviour());
        }
    }

    [Fact]
    public void AMirroredSwitchDressesTheSkinTheChainNames()
    {
        // The server's deploy wrote the chain's skin into the networked state
        // (Arsenal.Deploy); the mirror's replay of the same command writes
        // nothing, so the first compare after a skin-changing switch
        // mispredicted the byte for a round trip. The mirror asks the same
        // chain, for the local player.
        int pistol = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol);
        int crowbar = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponCrowbar);

        var rig = new Rig(pistol, skin: new ViewmodelSkin(new OneRule(new Fixed(3))));

        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 11, WeaponSelect = crowbar + 1 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, true, commandTime: 10f);

        Assert.Equal(3, rig.Arsenal.WorkingViewModel.Skin);
    }

    [Fact]
    public void AMirroredSwitchWithoutAnOpinionLeavesTheSkinStanding()
    {
        // The chain's null is no write: whatever an earlier state carried
        // keeps standing, as on the server (the deploy's own comment).
        int pistol = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol);
        int crowbar = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponCrowbar);

        var rig = new Rig(pistol, skin: new ViewmodelSkin(new OneRule(new Passing())));
        rig.ViewModel.State.Skin = 9;

        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 11, WeaponSelect = crowbar + 1 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, true, commandTime: 10f);

        Assert.Equal(9, rig.Arsenal.WorkingViewModel.Skin);
    }

    [Fact]
    public void AMirroredSwitchToAnUnheldWeaponCarriesTheFreshGive()
    {
        var catalog = WeaponCatalog.Stock();
        int pistol = catalog.IndexOf(EntityNames.WeaponPistol);
        int ar2 = catalog.IndexOf(EntityNames.WeaponAr2);

        var rig = new Rig(pistol);

        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        // The first switch to a weapon no command has carried: the mirror's
        // pool memory has no AR2 row (only the active weapon's pools ride
        // it), and the deploy must answer the balance the server's GiveWeapon
        // already created at the spawn kit — the default clip's overflow past
        // the clip (60 - 30, the AR2 row's numbers), clamped to the carry —
        // never a plain zero.
        var cmd = new UserCmd { CommandNumber = 11, WeaponSelect = ar2 + 1 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, true, commandTime: 10f);

        Assert.Equal(30, rig.Arsenal.WorkingWeapon.Reserve);
    }

    [Fact]
    public void TheMirroredSwitchIsTheWeaponTheHandsAskAbout()
    {
        var catalog = WeaponCatalog.Stock();
        int pistol = catalog.IndexOf(EntityNames.WeaponPistol);
        int ar2 = catalog.IndexOf(EntityNames.WeaponAr2);

        var rig = new Rig(pistol);

        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 11, WeaponSelect = ar2 + 1 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, true, commandTime: 10f);

        // The draw's sequence resolves through the activity rules, which ask
        // the active weapon's role: mid-switch that is the weapon the mirror
        // has just dressed — the same point of its own deploy where the
        // server's arms read the weapon it had just dressed — not the entity's
        // last decode, which still names the outgoing one until the next
        // phase 2.
        Assert.True(rig.Arsenal.TryGetActive(out string name, out _, out _));
        Assert.Equal(EntityNames.WeaponAr2, name);
    }

    [Fact]
    public void ThePoolTheSidePublishesIsTheReserveTheNextSwitchPredicts()
    {
        var catalog = WeaponCatalog.Stock();
        int pistol = catalog.IndexOf(EntityNames.WeaponPistol);
        int ar2 = catalog.IndexOf(EntityNames.WeaponAr2);

        // The pool changed on the server while the AR2 sat unheld, and the
        // side publishes its word: 7 — a number neither the mirror's memory
        // (none: this AR2 was never held) nor the spawn give's arithmetic (the
        // default clip's overflow, 30) answers. The switch carries it.
        var rig = new Rig(pistol, pools: new Published(7));

        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 11, WeaponSelect = ar2 + 1 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, true, commandTime: 10f);

        Assert.Equal(7, rig.Arsenal.WorkingWeapon.Reserve);
    }

    [Fact]
    public void ARowThePublisherLeavesUnansweredKeepsTheFreshGive()
    {
        var catalog = WeaponCatalog.Stock();
        int pistol = catalog.IndexOf(EntityNames.WeaponPistol);
        int ar2 = catalog.IndexOf(EntityNames.WeaponAr2);

        // A publisher that names no row (a side before its carrier arrives):
        // the mirror's own answer stands, the spawn give's arithmetic.
        var rig = new Rig(pistol, pools: new Published(7, row: "SMG1"));

        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 11, WeaponSelect = ar2 + 1 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, true, commandTime: 10f);

        Assert.Equal(30, rig.Arsenal.WorkingWeapon.Reserve);
    }

    /// <summary>A side that publishes one ammo row's pool, the carrier's word.</summary>
    private sealed class Published(int count, string row = "AR2") : ILocalAmmoPools
    {
        public bool PoolOf(string ammoName, out int found)
        {
            found = 0;
            if (ammoName != row)
                return false;

            found = count;
            return true;
        }
    }

    [Fact]
    public void TheClipTheSidePublishesIsTheClipTheNextSwitchPredicts()
    {
        var catalog = WeaponCatalog.Stock();
        int pistol = catalog.IndexOf(EntityNames.WeaponPistol);
        int ar2 = catalog.IndexOf(EntityNames.WeaponAr2);

        // The clip changed on the server while the AR2 sat holstered, and the
        // side publishes its word: 5 — a number neither the mirror's memory
        // (none: this AR2 was never held) nor the fresh clip's arithmetic (30)
        // answers. The switch carries it, as stock's m_iClip1 arrival would.
        var rig = new Rig(pistol, clips: new PublishedClip(5, row: ar2));

        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 11, WeaponSelect = ar2 + 1 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, true, commandTime: 10f);

        Assert.Equal(5, rig.Arsenal.WorkingWeapon.Clip);
    }

    [Fact]
    public void AClipRowThePublisherLeavesUnansweredKeepsTheFreshGive()
    {
        var catalog = WeaponCatalog.Stock();
        int pistol = catalog.IndexOf(EntityNames.WeaponPistol);
        int ar2 = catalog.IndexOf(EntityNames.WeaponAr2);

        // A publisher that names no row (a side before its carrier arrives):
        // the mirror's own answer stands, the fresh clip.
        var rig = new Rig(pistol, clips: new PublishedClip(5, row: pistol));

        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 11, WeaponSelect = ar2 + 1 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, true, commandTime: 10f);

        Assert.Equal(30, rig.Arsenal.WorkingWeapon.Clip);
    }

    /// <summary>
    /// The first prediction's timers ride the TICK-BASE clock, not the frame's
    /// and not the engine tick the command carries (spy/pyro/sniper's
    /// ItemMeterTime miss of the plain-life boots): stock's prediction sets
    /// the command's curtime from the predicted player's tick base times the
    /// tick's interval (<c>CPrediction::RunCommand</c>, prediction.cpp:894),
    /// and the server runs the same command on its own tick base times the
    /// same interval — the clock the movement already takes as CurTime
    /// (<c>_working.TickBase * tick</c>, ClientPlayerPrediction.cs:573). A
    /// command whose creation tick (<see cref="UserCmd.TickCount"/>) differs
    /// from the tick base must NOT move the stamp: the pair below holds that
    /// directly. Observable here through the idle settle (WeaponLogic.Step,
    /// buttons==0): the machine arms <see cref="WeaponNetState.TimeWeaponIdle"/>
    /// at the command's now plus the repeat; the frame's clock (the rig's
    /// stubbed 5000) lands the settle a whole clock-correction away, the miss
    /// itself. With no working base behind the ask (every other rig here) the
    /// frame's clock stands, as it did before the command carried the clock.
    /// </summary>
    [Fact]
    public void TheFirstPredictionSetsTheIdleSettleOnTheCommandsTickClock()
    {
        int pistol = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol);
        var rig = new Rig(pistol);
        rig.Weapon.State.TimeWeaponIdle = 0f;           // due at once: every idle command re-sends idle

        // The working player carries the tick base the replay runs on — the
        // same object the movement's replay holds (ClientModule's hand-over).
        // The rig's engine carries the tick's interval (Rig.TickGlobals); the
        // first-prediction crossing reads it through the injected environment.
        var working = new PlayerNetState();
        working.BindLocal();
        working.TickBase = 2010;
        rig.Arsenal.WorkingPlayer = working;

        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        // The crossing the game's first prediction takes (ClientModule's
        // OnStandalonePlayerCommand): the 4-argument form, the command its
        // own clock on. The command's creation tick (2000) deliberately
        // differs from the tick base (2010): the stamp must follow the base.
        var cmd = new UserCmd { CommandNumber = 11, TickCount = 2000 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, firstTime: true);

        // The command's now is the tick BASE's ticks of the interval; the
        // settle the machine arms on it is the repeat's 5 on top
        // (WeaponLogic.IdleRepeat). The creation tick (2000 -> 35) is the
        // wrong clock: it answers 35, the base answers 35.15+.
        Assert.Equal(2010 * 0.015f + 5f, rig.Arsenal.WorkingWeapon.TimeWeaponIdle, 4);
    }

    /// <summary>The selection command and entity sounds the hud press travels through.</summary>
    private sealed class Press : IWeaponSelectionCommand, SourceSharp.Client.Sounds.IClientEntitySounds
    {
        public readonly List<string> Lines = [];

        public void SendSelection(string line) => Lines.Add(line);
        public void Escape() { }
        public void EmitFrom(EntityHandle entity, string soundName, Vector? origin = null) { }
        public void StopFrom(EntityHandle entity, string soundName) { }
    }

    /// <summary>
    /// The HUD's slot press (the cs_key the plain-life boots send) is answered
    /// INSIDE the next replayed command, on that command's tick clock: the
    /// server reads the press's <c>ss_use</c> within the command's own run and
    /// stamps the deploy's meters there (<c>ItemMeterTime=40.32</c>, the
    /// pyro/heavy miss), while the press itself lands between commands, where
    /// the frame clock is behind the server's write by the ack batch (the
    /// <c>40.275</c> those boots answered with). So the press holds its pick
    /// for the replay, whose tick base — 2010 times the interval — carries the
    /// stamp. The rig's frame clock (stubbed 5000) is the wrong answer; no
    /// prediction at all leaves the stamp at zero.
    /// </summary>
    [Fact]
    public void AHudSlotPressDeploysInsideTheNextReplayedCommand()
    {
        var catalog = WeaponCatalog.Stock();
        int pistol = catalog.IndexOf(EntityNames.WeaponPistol);
        int crowbar = catalog.IndexOf(EntityNames.WeaponCrowbar);

        var rig = new Rig(pistol);

        var working = new PlayerNetState();
        working.BindLocal();
        working.TickBase = 2010;
        rig.Arsenal.WorkingPlayer = working;

        var basis = new UserCmd { CommandNumber = 10 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in basis, 2, false);

        // The real press path: the hud's Select — the console string out to
        // the server, the pick held for the replay by this side's arsenal.
        var hud = new LocalWeaponSelection(rig.Engine, rig.Standalones, rig.Arsenal, catalog,
                                           new Press(), new Press());
        hud.Select(crowbar);

        var cmd = new UserCmd { CommandNumber = 11 };
        rig.Arsenal.OnPlayerCommand(rig.Player, in cmd, 1, firstTime: true);

        Assert.Equal(crowbar, rig.Arsenal.WorkingWeapon.WeaponTypeId);
        Assert.Equal(2010 * 0.015f, rig.Arsenal.WorkingWeapon.LastDeployTime, 4);
    }

    /// <summary>A side that publishes one weapon row's carried clip, the carrier's word.</summary>
    private sealed class PublishedClip(int clip, int row = -1) : ILocalWeaponClips
    {
        public bool ClipOf(int typeIndex, out int found)
        {
            found = 0;
            if (typeIndex != row)
                return false;

            found = clip;
            return true;
        }
    }
}
