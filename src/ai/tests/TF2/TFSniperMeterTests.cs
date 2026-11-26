using SourceSharp.Abi;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>The Sniper's HUD meters' arithmetic: the charge bar in whole points, a holstered jar's bar run on at its recharge.</summary>
public sealed class TFSniperMeterTests
{
    /// <summary>A charge begun 1.01 s ago shows 50 points (stock's 50 a second), in whole points.</summary>
    [Fact]
    public void TheChargeBarShowsWholePoints()
    {
        var rifle = new WeaponNetState();
        rifle.BindLocal();
        rifle.BehaviourMeter = 1f;
        Assert.Equal(50f, TFSniperMeters.ChargeShown(rifle, 2.01f));
    }

    /// <summary>A holstered jar at half its bar ten seconds ago is full (20.1 s a bar); at a quarter one second ago it is not.</summary>
    [Fact]
    public void AHolsteredJarsBarRunsOnAtItsRecharge()
        => Assert.Equal((1f, true), (TFSniperMeters.JarShown(0.5f, 10f, 20.06f), TFSniperMeters.JarShown(0.25f, 10f, 11f) < 1f));

    /// <summary>
    /// The HUD meter chain's members each hold an order of their own (an unordered one is 0): two at one order throw at the
    /// chain's first call, which live stopped every bot's senses (AiSenses.OnPostThink threw: TFMedigunMeters and
    /// TFSniperMeters both declare HookOrder 0). TFBindingTests' chain census reads only interfaces named *Hook, so it
    /// passed over this chain.
    /// </summary>
    [Fact]
    public void TheHudMeterChainsMembersHoldOrdersOfTheirOwn()
    {
        var services = new ServiceContainer();
        IReadOnlyList<Type> registered = SideServices.Register(services, Side.Server,
            [.. typeof(global::TF2.Server.Player.TFServerActorFacts).Assembly.GetTypes(), .. typeof(VisionPass).Assembly.GetTypes(), .. typeof(TFBotGame).Assembly.GetTypes()]);
        static int Order(Type t)
            => typeof(IOrderedHook).IsAssignableFrom(t)
                ? ((IOrderedHook)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t)).HookOrder
                : 0;
        Type[] members = [.. registered.Where(t => t.IsClass && !t.IsAbstract && typeof(ITFHudMeters).IsAssignableFrom(t)).Distinct()];
        Assert.Contains(typeof(TFSniperMeters), members);
        Assert.Equal(members.Length, members.Select(Order).Distinct().Count());
    }
}

/// <summary>
/// The Sniper's HUD meter reader itself (the code review's F2): to a Sniper only, its own scope off its networked
/// conditions, the charge only with its rifle in hand, a holstered jar's bar off its networked item meters, and its kit
/// read again only when the kit's version moves.
/// </summary>
public sealed unsafe class TFSniperMeterReadTests : IDisposable
{
    private const int BotIndex = 2, RifleKind = 60, SmgKind = 61, JarKind = 63;
    private static readonly EntityHandle Bot = new(0x0001_0002u), WeaponEntity = new(0x0001_0040u);

    private readonly CGlobalVarsRaw* _raw = (CGlobalVarsRaw*)System.Runtime.InteropServices.NativeMemory.AllocZeroed((nuint)sizeof(CGlobalVarsRaw));

    public void Dispose() => System.Runtime.InteropServices.NativeMemory.Free(_raw);

    private sealed class Env(nint raw) : IEngineEnvironment
    {
        public nint GetInterface(string name) => 0;

        public Globals Globals => new((CGlobalVarsRaw*)raw);
    }

    private sealed class Kits : global::TF2.Server.Econ.ITFOwnKit
    {
        public List<global::TF2.Server.Econ.TFOwnKitItem> Items { get; } = [];
        public int Version { get; set; } = 1;
        public int Reads { get; private set; }

        public IReadOnlyList<global::TF2.Server.Econ.TFOwnKitItem> KitOf(int entIndex)
        {
            Reads++;
            return Items;
        }

        int global::TF2.Server.Econ.ITFOwnKit.Version(int entIndex) => Version;
    }

    private readonly Kits _kits = new();
    private readonly global::TF2.Shared.Player.TFPlayerNetState _own = new();
    private readonly SourceSharp.Server.SsWeapon _weapon = new();

    public TFSniperMeterReadTests()
    {
        _raw->maxClients = 8;
        _raw->interval_per_tick = 0.015f;
        _raw->curtime = 100f;
        _own.BindLocal();
        _weapon.State.BindLocal();
        _kits.Items.Add(new(global::TF2.Shared.TFLoadoutSlot.Primary, 14, "tf_weapon_sniperrifle", "tf_weapon_sniperrifle", RifleKind, []));
        _kits.Items.Add(new(global::TF2.Shared.TFLoadoutSlot.Secondary, 58, "tf_weapon_jar", "tf_weapon_jar", JarKind, []));
        _weapon.State.WeaponTypeId = RifleKind;
    }

    private TFSniperMeters Reader()
        => new(_kits,
               Senses.FakeEngine.Of<IEnginePlayers>((m, a) => m.Name == "GetActiveWeaponEntity" ? WeaponEntity : throw Senses.FakeEngine.Unasked(m)),
               Senses.FakeEngine.Of<IEntityDirectory>((m, a) => m.Name == "Get" ? ((EntityHandle)a[0]! == WeaponEntity ? _weapon : null) : throw Senses.FakeEngine.Unasked(m)),
               Senses.FakeEngine.Of<global::TF2.Shared.Player.ITFPlayerData>((m, a) => m.Name == "Networked" ? ((int)a[0]! == BotIndex ? _own : null) : throw Senses.FakeEngine.Unasked(m)),
               new Env((nint)_raw));

    private List<TFMeter> Read(TFSniperMeters reader, global::TF2.Shared.TFClass cls = global::TF2.Shared.TFClass.Sniper)
    {
        var into = new List<TFMeter>();
        reader.Read(Bot, cls, into);
        return into;
    }

    private static float Of(List<TFMeter> m, string name) => m.Where(x => x.Name == name).Select(x => x.Value).DefaultIfEmpty(-1f).Single();

    /// <summary>A Soldier's HUD shows none of the Sniper's meters; a Sniper's shows the scope and the charge.</summary>
    [Fact]
    public void OnlyASnipersHudShowsTheSnipersMeters()
    {
        TFSniperMeters reader = Reader();
        Assert.Empty(Read(reader, global::TF2.Shared.TFClass.Soldier));
        Assert.Contains(Read(reader), m => m.Name == TFSniperMeters.Zoomed);
    }

    /// <summary>The scope is the bot's own networked TF_COND_ZOOMED (bit 1 of the first condition word).</summary>
    [Fact]
    public void TheScopeIsTheBotsOwnZoomCondition()
    {
        TFSniperMeters reader = Reader();
        float before = Of(Read(reader), TFSniperMeters.Zoomed);
        _own.PlayerCond = 1 << (int)global::TF2.Shared.TFCond.Zoomed;
        Assert.Equal((0f, 1f), (before, Of(Read(reader), TFSniperMeters.Zoomed)));
    }

    /// <summary>The charge shows with the rifle in hand (a charge begun 2 s ago: 100), and 0 with the SMG in hand, whatever its own meter.</summary>
    [Fact]
    public void TheChargeShowsOnlyWithTheRifleInHand()
    {
        TFSniperMeters reader = Reader();
        _weapon.State.BehaviourMeter = 98f;
        float rifle = Of(Read(reader), TFSniperMeters.Charge);
        _weapon.State.WeaponTypeId = SmgKind;
        Assert.Equal((100f, 0f), (rifle, Of(Read(reader), TFSniperMeters.Charge)));
    }

    /// <summary>A holstered jar's bar is its networked item meter run on: half full 10.05 s ago is full now; a quarter 1 s ago is not.</summary>
    [Fact]
    public void AHolsteredJarIsReadOffTheNetworkedItemMeters()
    {
        TFSniperMeters reader = Reader();
        _own.CarriedRows[3] = JarKind + 1;
        _own.ItemMeters[3] = 0.5f;
        _own.ItemMeterTimes[3] = 89.95f;
        float full = Of(Read(reader), TFSniperMeters.Jar);
        _own.ItemMeters[3] = 0.25f;
        _own.ItemMeterTimes[3] = 99f;
        float part = Of(Read(reader), TFSniperMeters.Jar);
        Assert.Equal(1f, full);
        Assert.InRange(part, 0.25f, 0.4f);
    }

    /// <summary>The kit is read once while its version holds, and again when it moves (the review's C11: KitOf allocates and sorts).</summary>
    [Fact]
    public void TheKitIsReadAgainOnlyWhenItsVersionMoves()
    {
        TFSniperMeters reader = Reader();
        for (int i = 0; i < 5; i++)
            Read(reader);
        int once = _kits.Reads;
        _kits.Version++;
        Read(reader);
        Assert.Equal((1, 2), (once, _kits.Reads));
    }
}
