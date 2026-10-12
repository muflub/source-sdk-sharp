using System.Linq;
using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// Containers for tests, so a suite building a service builds it the way the
/// module does.
///
/// <para>
/// Constructing a service with <c>new</c> was fine while services took nothing
/// but convars; now they take the engine, and a test that hand-assembled one
/// would be asserting against a wiring the game does not use. Going through the
/// container is both shorter and more honest.
/// </para>
/// </summary>
public static partial class TestServices
{
    /// <summary>
    /// The size tiers native registers, supplied for a test.
    ///
    /// <para>
    /// A test that reads a tier used to inherit these from whichever suite ran
    /// before it and happened to have supplied them — a cross-suite dependency
    /// nothing declared, and which the old runner's fixed order hid entirely.
    /// The table is an instance now, so a test that needs one makes one and
    /// nothing it does can reach another test.
    /// </para>
    /// </summary>
    public static NetTiers SupplyTiers()
    {
        var tiers = new NetTiers(Fake().Ignores("Networking.NetTierAt"));

        tiers.SupplyForTests(NetShellKind.Standalone, TierSizes
            .Select((bytes, i) => new NetTierInfo(
                i, $"DT_SourceSharpStdEnt{bytes}", bytes,
                bytes / 4, bytes / 4 + StandaloneRealProps, i == 0))
            .ToArray());

        return tiers;
    }

    /// <summary>
    /// The tier sizes, which must be the rows of <c>SourceSharp_Tiers</c> in
    /// generic_nettable.h.
    ///
    /// <para>
    /// This list said <c>{32, 256, 2048}</c> for as long as native did. Native
    /// grew to seven rows and this did not, so the fixture went on describing a
    /// tier table the game no longer had — and a test asking which tier a
    /// 100-byte type lands in would have been told 256, where the game says 128,
    /// and would have passed. Nothing failed, because the one suite using this
    /// supplies the table and reads it back, which is true of any table.
    /// </para>
    /// </summary>
    private static readonly int[] TierSizes = { 32, 64, 128, 256, 512, 1024, 2048 };

    /// <summary>
    /// SOURCESHARP_TIER_PROPS_STANDALONE, which is NetSchema's constant: one
    /// copy on this side, held to standalone_nettable.h by TierParityTests.
    /// Derived here rather than written out per row so a new size cannot arrive
    /// with a prop count that disagrees with it.
    /// </summary>
    private const int StandaloneRealProps = NetSchema.StandaloneSharedProps;

    /// <summary>A tier table with nothing in it — the floor a build with no engine has.</summary>
    public static INetTiers NoTiers => EmptyTiers.Instance;

    /// <summary>
    /// A fresh engine double. Strict, so anything the object under test READS
    /// and the test did not stub is an error naming it.
    ///
    /// One call per argument reads oddly — <c>new Players(Fake(), Fake())</c>
    /// hands over two DIFFERENT doubles — so a test that needs to assert on the
    /// conversation should make one and pass it, and this is for the many that
    /// only need the constructor satisfied.
    /// </summary>
    /// <inheritdoc cref="Mock()"/>
    public static RecordingEngine Fake() => Mock();

    /// <summary>
    /// A container whose engine is a <see cref="RecordingEngine"/> — every one
    /// of the twenty-four areas, plus the clock.
    ///
    /// <para>
    /// The default for tests, and deliberately NOT the real engine. The real
    /// one in a test process answers 0 and empty to everything, silently, so a
    /// test standing on it cannot fail: the entity reads a value nobody chose
    /// and the check passes anyway. The double throws instead, which turns
    /// every unaccounted-for read into a message naming it.
    /// </para>
    /// </summary>
    public static ServiceContainer Services(params object[] extras)
    {
        var services = new ServiceContainer();

        Mock(services, out _);

        foreach (var extra in extras)
            services.AddInstance(extra.GetType(), extra);

        return services;
    }

    /// <summary>
    /// The same, naming engine reads this test does not care about.
    ///
    /// Each name is a dependency the entity under test has and the test does
    /// not exercise — written down rather than defaulted silently, so a reader
    /// can see what was set aside and challenge it.
    /// </summary>
    public static ServiceContainer Ignoring(params string[] members)
    {
        var services = new ServiceContainer();

        Mock(services, out var engine);
        engine.Ignores(members);

        return services;
    }

    /// <summary>The same, handing back the double so a test can stub and assert.</summary>
    public static ServiceContainer Services(out RecordingEngine engine, params object[] extras)
    {
        var services = new ServiceContainer();

        Mock(services, out engine);

        foreach (var extra in extras)
            services.AddInstance(extra.GetType(), extra);

        return services;
    }

    /// <summary>
    /// Every engine area answered by <paramref name="engine"/>, for binding an
    /// entity a test built by hand (<c>ScriptedEntity.BindAreas</c>): nothing
    /// but a registry binds one, and an unbound entity has no engine at all
    /// (plan_abi_hotreload D8).
    /// </summary>
    public static ServiceContainer Areas(RecordingEngine engine)
    {
        var services = new ServiceContainer();

        foreach (var (contract, _) in EngineServices.Areas(new RecordingLog()))
            services.AddInstance(contract, engine);

        services.AddInstance<IUserMessageSender>(engine);
        services.AddInstance<ILog>(new RecordingLog());
        services.AddInstance(new ConsoleLines());

        return services;
    }

    /// <summary>
    /// Registers one double under every engine area.
    ///
    /// One object for all of them rather than one each, so a test reads the
    /// whole conversation an entity had with the engine in a single ordered log
    /// — which is what "what did it do" actually looks like when an entity
    /// traces the world and then hurts what it found.
    /// </summary>
    private static void Mock(ServiceContainer services, out RecordingEngine engine)
    {
        engine = Mock();

        foreach (var (contract, _) in EngineServices.Areas(new RecordingLog()))
            services.AddInstance(contract, engine);

        services.AddInstance<IGameClock>(engine);

        // The composition's log and its console's lines (statics to zero): a
        // recorder a test can read, as EngineServices.AddTo registers the side's.
        services.AddInstance<ILog>(new RecordingLog());
        services.AddInstance(new ConsoleLines());

        // What sends a user message by key: a side's registry over its engine
        // in the game (plan_side_isolation §2.2a), the recorder here, so a send
        // lands in the same ordered log as everything else.
        services.AddInstance<IUserMessageSender>(engine);

        // The server's screen shake and fade (plan_side_isolation §2.2a): a
        // REAL one over the double, like SoundEnt below, so an env_shake or
        // env_fade a test spawns runs the managed reach rules and its sends
        // land on the recorder.
        AddSideServices(services, engine);
    }

    /// <summary>
    /// The side's own services a mocked composition carries: the server's
    /// (TestServices.Server.cs) where the project sees the server, none where
    /// it does not (plan_side_isolation P6).
    /// </summary>
    static partial void AddSideServices(ServiceContainer services, RecordingEngine engine);

    /// <summary>
    /// A strict double with the UNIVERSAL reads stubbed, and nothing else.
    ///
    /// <para>
    /// These five are read by the base class on the way in or out — every entity that
    /// spawns asks its own spawnflags, and half of them ask their own name —
    /// so making every test declare them would be noise in every test and
    /// information in none. They are stubbed HERE, in one place a reader can
    /// see, rather than answered as a silent zero by a double that shrugs.
    /// </para>
    ///
    /// <para>
    /// Everything else stays strict. A test whose entity traces the world,
    /// reads a player's angles or asks what it is standing on has to say so,
    /// and that is exactly the dependency worth stating out loud.
    /// </para>
    ///
    /// <para>
    /// Override any of them per test by calling <c>Returns</c> again; the last
    /// one wins.
    /// </para>
    /// </summary>
    public static RecordingEngine Mock()
        => new RecordingEngine()
            .Returns("Entities.GetSpawnFlags", 0)
            .Returns("Entities.GetName", string.Empty)
            .Returns("Entities.GetClassName", string.Empty)
            .Returns("Host.ActiveSide", Side.Server)
            // The fifth universal read (plan_abi_hotreload D8): every entity
            // asks whether there is a native side before saving or restoring
            // one (ScriptedEntity.HasNativeSide). A double is not a running
            // engine, so no; a fact that wants the native section says so.
            .Returns("Host.IsInitialized", false);
}
