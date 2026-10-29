using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The weapons' draw effects by weapon: the local viewmodel's ViewModelDrawn
/// reaches its carrier's weapon's effect, the carried weapon's translucent
/// pass reaches its own, every carried weapon thinks once a frame, a weapon
/// that changes kind or goes lets its old effect go, and the carried weapon
/// asks the render policy for the two-pass group its kind answers.
/// </summary>
public class WeaponDrawEffectsTests
{
    private const int Local = 1, Other = 2;

    private sealed class Probe(string weaponName, bool twoPass = false) : IWeaponDrawEffect
    {
        public readonly List<string> Seen = [];
        public readonly List<WeaponDrawContext> Contexts = [];
        public int Answer = 7;

        public string WeaponName => weaponName;
        public bool TwoPass => twoPass;

        public void Think(in WeaponDrawContext context)
        {
            Seen.Add($"think {context.Weapon.Handle.Value:x}");
            Contexts.Add(context);
        }

        public void ViewModelDrawn(in WeaponDrawContext context)
        {
            Seen.Add($"drawn {context.Weapon.Handle.Value:x}");
            Contexts.Add(context);
        }

        public int DrawModel(in WeaponDrawContext context, int flags)
        {
            Seen.Add($"model {context.Weapon.Handle.Value:x} {flags}");
            Contexts.Add(context);
            return Answer;
        }

        public void Forget(EntityHandle weapon) => Seen.Add($"forget {weapon.Value:x}");
    }

    private sealed class Camera : IThirdPersonCamera
    {
        public bool IsThirdPerson { get; set; }
    }

    private sealed class Entities : IClientStandalones
    {
        public readonly List<ClientStandaloneEntity> All = [];
        public IReadOnlyList<ClientStandaloneEntity> Live() => All;
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    private sealed class Wishes : IRenderPolicyWishes
    {
        public readonly List<(uint Entity, bool TwoPass)> Asked = [];
        public void WantTwoPass(EntityHandle entity, bool twoPass) => Asked.Add((entity.Value, twoPass));
    }

    private readonly RecordingEngine _engine = Fake();
    private readonly Entities _live = new();
    private readonly Camera _camera = new();
    private readonly Probe _rpg = new(EntityNames.WeaponRpg);
    private readonly Probe _stick = new(EntityNames.WeaponStunstick, twoPass: true);
    private readonly WeaponDrawEffects _effects;

    private sealed class Predictions : IWeaponDrawStates
    {
        public readonly Dictionary<ClientSsWeapon, WeaponNetState> Held = [];
        public WeaponNetState? PredictedStateOf(ClientSsWeapon weapon) => Held.GetValueOrDefault(weapon);
    }

    private readonly Predictions _predictions = new();

    public WeaponDrawEffectsTests()
    {
        _engine.Returns("Hud.LocalPlayerIndex", Local);
        _effects = new WeaponDrawEffects(_live, _engine, _camera, _engine, WeaponCatalog.Stock(), [_rpg, _stick], _predictions);
    }

    [Fact]
    public void AWeaponsContextCarriesItsPredictionWhereThereIsOne()
    {
        ClientSsWeapon mine = Weapon(0x42, Local, EntityNames.WeaponRpg);
        ClientSsWeapon theirs = Weapon(0x41, Other, EntityNames.WeaponRpg);
        ClientSsViewModel vm = ViewModel(0x43, Local);
        var predicted = new WeaponNetState();
        _predictions.Held[mine] = predicted;

        _effects.Frame(0.01f);
        _effects.ViewModelDrawn(vm);

        var byWeapon = _rpg.Contexts.ToLookup(c => c.Weapon);
        Assert.All(byWeapon[mine], c => Assert.Same(predicted, c.State));
        Assert.All(byWeapon[theirs], c => Assert.Same(theirs.State, c.State));
        Assert.Equal(2, byWeapon[mine].Count());
        Assert.Single(byWeapon[theirs]);
    }

    private ClientSsWeapon Weapon(uint handle, int owner, string name)
    {
        var weapon = new ClientSsWeapon(_engine) { Handle = new EntityHandle(handle) };
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = owner;
        weapon.State.WeaponTypeId = WeaponCatalog.Stock().IndexOf(name);
        _live.All.Add(weapon);
        return weapon;
    }

    private ClientSsViewModel ViewModel(uint handle, int owner)
    {
        var viewModel = new ClientSsViewModel(_engine, _engine) { Handle = new EntityHandle(handle) };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = owner;
        viewModel.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        _live.All.Add(viewModel);
        return viewModel;
    }

    [Fact]
    public void TheLocalViewmodelDrawsItsCarriersWeaponsEffect()
    {
        Weapon(0x41, Other, EntityNames.WeaponRpg);
        Weapon(0x42, Local, EntityNames.WeaponRpg);
        ClientSsViewModel vm = ViewModel(0x43, Local);

        _effects.ViewModelDrawn(vm);

        Assert.Equal(["drawn 42"], _rpg.Seen);
        Assert.Same(vm, _rpg.Contexts[0].ViewModel);
        Assert.True(_rpg.Contexts[0].FirstPerson);
    }

    [Fact]
    public void SomebodyElsesViewmodelDrawsNothing()
    {
        Weapon(0x41, Other, EntityNames.WeaponRpg);
        ClientSsViewModel vm = ViewModel(0x43, Other);

        _effects.ViewModelDrawn(vm);

        Assert.Empty(_rpg.Seen);
    }

    [Fact]
    public void AWeaponWithNoEffectDrawsNothing()
    {
        Weapon(0x42, Local, EntityNames.WeaponPistol);
        ClientSsViewModel vm = ViewModel(0x43, Local);

        _effects.ViewModelDrawn(vm);

        Assert.Empty(_rpg.Seen);
        Assert.Empty(_stick.Seen);
    }

    [Fact]
    public void TheCarriedWeaponsTranslucentPassIsItsEffectsAnswer()
    {
        ClientSsWeapon weapon = Weapon(0x41, Other, EntityNames.WeaponStunstick);

        Assert.Equal(7, _effects.DrawWorldModel(weapon, 9));
        Assert.Equal(["model 41 9"], _stick.Seen);
        Assert.False(_stick.Contexts[0].FirstPerson);
    }

    [Fact]
    public void AWeaponWithNoEffectLetsItsModelDraw()
        => Assert.Equal(-1, _effects.DrawWorldModel(Weapon(0x41, Other, EntityNames.WeaponPistol), 9));

    [Fact]
    public void TheLocalWeaponIsFirstPersonUntilTheCameraIsBehindThePlayer()
    {
        Weapon(0x42, Local, EntityNames.WeaponStunstick);
        ViewModel(0x43, Local);

        _effects.Frame(1f / 60f);
        _camera.IsThirdPerson = true;
        _effects.Frame(1f / 60f);

        Assert.Equal([true, false], _stick.Contexts.Select(c => c.FirstPerson));
        Assert.NotNull(_stick.Contexts[0].ViewModel);
        Assert.Null(_stick.Contexts[1].ViewModel);
    }

    [Fact]
    public void TheFramesTimeReachesTheThinkAndTheDraws()
    {
        // gpGlobals->frametime: 0 while paused, when stock's thinks strike no beams.
        ClientSsWeapon weapon = Weapon(0x41, Other, EntityNames.WeaponStunstick);

        _effects.Frame(0.025f);
        _effects.DrawWorldModel(weapon, 9);

        Assert.Equal([0.025f, 0.025f], _stick.Contexts.Select(c => c.FrameTime));
    }

    [Fact]
    public void EveryCarriedWeaponThinksOnceAFrame()
    {
        Weapon(0x41, Other, EntityNames.WeaponStunstick);
        Weapon(0x42, Local, EntityNames.WeaponStunstick);
        Weapon(0x44, 0, EntityNames.WeaponStunstick);

        _effects.Frame(1f / 60f);

        Assert.Equal(["think 41", "think 42"], _stick.Seen);
    }

    [Fact]
    public void AWeaponThatChangesKindLetsItsOldEffectGo()
    {
        ClientSsWeapon weapon = Weapon(0x42, Local, EntityNames.WeaponRpg);
        _effects.Frame(1f / 60f);

        weapon.State.WeaponTypeId = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponStunstick);
        _effects.Frame(1f / 60f);

        Assert.Equal(["think 42", "forget 42"], _rpg.Seen);
        Assert.Equal(["think 42"], _stick.Seen);
    }

    [Fact]
    public void AWeaponThatGoesLetsItsEffectGo()
    {
        ClientSsWeapon weapon = Weapon(0x42, Local, EntityNames.WeaponRpg);
        _effects.Frame(1f / 60f);

        _live.All.Remove(weapon);
        _effects.Frame(1f / 60f);

        Assert.Equal(["think 42", "forget 42"], _rpg.Seen);
    }

    [Fact]
    public void TheTwoPassAnswerIsTheKindsEffects()
    {
        Assert.True(_effects.WantsTwoPass(WeaponCatalog.Stock().IndexOf(EntityNames.WeaponStunstick)));
        Assert.False(_effects.WantsTwoPass(WeaponCatalog.Stock().IndexOf(EntityNames.WeaponRpg)));
        Assert.False(_effects.WantsTwoPass(WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol)));
    }

    //-------------------------------------------------------------------------
    // The entities' halves.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheCarriedWeaponAsksForTheGroupItsKindAnswersOncePerChange()
    {
        var wishes = new Wishes();
        var weapon = new ClientSsWeapon(_engine, _effects, wishes) { Handle = new EntityHandle(0x41) };
        weapon.State.BindLocal();
        weapon.State.WeaponTypeId = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponStunstick);

        weapon.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        weapon.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
        weapon.State.WeaponTypeId = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponRpg);
        weapon.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.Equal([(0x41u, true), (0x41u, false)], wishes.Asked);
    }

    [Fact]
    public void ARemovedWeaponWithdrawsItsWish()
    {
        var wishes = new Wishes();
        var weapon = new ClientSsWeapon(_engine, _effects, wishes) { Handle = new EntityHandle(0x41) };

        weapon.OnRemoved();

        Assert.Equal([(0x41u, false)], wishes.Asked);
    }

    [Fact]
    public void TheCarriedWeaponsExtrasAreItsEffects()
    {
        var weapon = new ClientSsWeapon(_engine, _effects, new Wishes()) { Handle = new EntityHandle(0x41) };
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = Other;
        weapon.State.WeaponTypeId = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponStunstick);

        Assert.Equal(7, weapon.OnDrawExtras(9));
    }

    [Fact]
    public void TheViewmodelsExtrasAreItsWeaponsViewModelDrawnAndLetTheModelStand()
    {
        Weapon(0x42, Local, EntityNames.WeaponRpg);
        var vm = new ClientSsViewModel(_engine, _engine, _effects) { Handle = new EntityHandle(0x43) };
        vm.State.BindLocal();
        vm.State.OwnerIndex = Local;
        vm.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.Equal(-1, vm.OnDrawExtras(1));
        Assert.Equal(["drawn 42"], _rpg.Seen);
    }
}
