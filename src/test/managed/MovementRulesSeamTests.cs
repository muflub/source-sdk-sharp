extern alias client;

using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using client::SourceSharp.Client.Interop;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Phase 0.3: the movement rules seam. The three decisions a game instance's
/// movement obeys are a resolved value, not three statics — the resolver
/// counts the side's <see cref="IMovementRules"/> registrations (zero = stock,
/// one = that mod, two = a startup error naming both), and BOTH sides build
/// their answer the same way, because movement is predicted: server and client
/// must answer identically or the player rubber-bands.
///
/// <para>
/// The mirrored facts (one per side's binding, same expectations) are the
/// side-symmetry proof: the server's <c>PlayerCommandsBinding</c> and the
/// client's <c>PredictionClientBinding</c> each hand their loop a pack built
/// by the module's resolver, so a fake hook answered the same way on both
/// halves is the seam's whole promise.
/// </para>
/// </summary>
public class MovementRulesSeamTests
{
    /// <summary>A fake hook a mod would register, stamping everything it sees.</summary>
    /// <summary>A second fake, so the two-hook facts hold two CONTRACTS, the
    /// way two mods would: the container keys by registration type, and the
    /// real scan registers every marked class under its own.</summary>
    private sealed class OtherFakeRules : IMovementRules
    {
        public float JumpScale(in JumpRequest request) => 1f;
        public float FrictionScale(int i, float f, float s, bool d, int m) => f;
        public float MaxSpeedScale(int i, float ms, bool d, int m) => ms;
        public MovementParams? Constants => null;
    }

    private sealed class FakeRules : IMovementRules
    {
        public readonly List<string> Seen = [];

        /// <summary>The constants to hand over, or null for stock.</summary>
        public MovementParams? Constants { get; init; }

        /// <summary>What each scale returns; null = identity.</summary>
        public float? JumpTo { get; init; }
        public float? FrictionTo { get; init; }
        public float? MaxSpeedTo { get; init; }

        public float JumpScale(in JumpRequest request)
        {
            Seen.Add($"jump:{request.PlayerIndex}");
            return JumpTo ?? 1f;
        }

        public float FrictionScale(int playerIndex, float friction, float speed,
                                   bool ducking, int groundMaterial)
        {
            Seen.Add($"friction:{playerIndex},{friction},{speed},{ducking},{groundMaterial}");
            return FrictionTo ?? friction;
        }

        public float MaxSpeedScale(int playerIndex, float maxSpeed, bool ducking, int groundMaterial)
        {
            Seen.Add($"maxspeed:{playerIndex},{maxSpeed},{ducking},{groundMaterial}");
            return MaxSpeedTo ?? maxSpeed;
        }
    }

    private static ServiceContainer Container()
    {
        var c = new ServiceContainer();
        c.AddInstance<ILog>(new RecordingLog());
        return c;
    }

    private static MovementRulesResolver Resolve(params IMovementRules[] hooks)
    {
        var c = Container();
        // Like SideServices.Register with a marked [SideService] hook class:
        // the CONTRACT is the concrete type — two mods mean two registrations,
        // not two writes to one contract key.
        foreach (var h in hooks)
            c.AddFactory(h.GetType(), _ => h);
        try
        {
            return c.Create<MovementRulesResolver>();
        }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
        {
            // The container invokes the constructor by reflection; the game
            // thread sees the same wrapper at startup. What a fact asserts is
            // the resolver's own refusal.
            throw tie.InnerException;
        }
    }

    //=========================================================================
    // The resolver's arms.
    //=========================================================================

    [Fact]
    public void No_hook_resolves_the_identity_and_the_stock_constants()
    {
        var r = Resolve();

        Assert.True(r.IsStock, "with nothing registered, the core owns movement");
        // The stimulus must be non-zero: identity answers must MEANINGFULLY
        // return their input, and the constants must be the stock table.
        Assert.Equal(1f, r.Rules.Jump(default));
        Assert.Equal(3.5f, r.Rules.Friction(1, 3.5f, 100f, false, 0));
        Assert.Equal(275f, r.Rules.MaxSpeed(1, 275f, false, 0));
        Assert.Equal(Movement.Current.JumpHeight, r.Constants.JumpHeight);
        Assert.Equal(Movement.Current.MaxSpeed, r.Constants.MaxSpeed);
    }

    [Fact]
    public void One_hook_own_the_three_scales()
    {
        var hook = new FakeRules { JumpTo = 0.5f, FrictionTo = 1f, MaxSpeedTo = 160f };
        var r = Resolve(hook);

        Assert.False(r.IsStock);
        Assert.Equal(0.5f, r.Rules.Jump(default));
        Assert.Equal(1f, r.Rules.Friction(1, 4f, 100f, false, 0));
        Assert.Equal(160f, r.Rules.MaxSpeed(1, 320f, false, 0));

        // The hook is asked, not merely consulted: its log proves the call.
        Assert.Contains("jump:0", hook.Seen);
        Assert.Contains("maxspeed:1,320,False,0", hook.Seen);
    }

    [Fact]
    public void Two_hooks_are_a_startup_error_naming_both_types()
    {
        Assert.Throws<ServiceResolutionException>(() =>
            Resolve(new FakeRules(), new OtherFakeRules()));
    }

    [Fact]
    public void The_two_hook_message_names_both_type_names()
    {
        var e = Assert.Throws<ServiceResolutionException>(() =>
            Resolve(new FakeRules(), new OtherFakeRules()));

        // Both arms of the assertion: the message must name the two owners,
        // not just any text. The fakes are nested here, so FullName ends with
        // the declaring type's name.
        Assert.Contains(nameof(FakeRules), e.Message, StringComparison.Ordinal);
        Assert.Contains("registered by 2 mods", e.Message, StringComparison.Ordinal);
        Assert.Contains("ONE owner", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hooks_constants_replace_the_stock_table()
    {
        var mod = new MovementParams { JumpHeight = 40f, Gravity = 800f, MaxSpeed = 400f };
        var r = Resolve(new FakeRules { Constants = mod });

        Assert.Equal(40f, r.Constants.JumpHeight);
        Assert.Equal(800f, r.Constants.Gravity);
        Assert.Equal(400f, r.Constants.MaxSpeed);
    }

    //=========================================================================
    // Register hands the resolved constants to the engine (both sides call it
    // with their resolver's answer).
    //=========================================================================

    [Fact]
    public void Register_hands_the_resolved_constants_to_the_engine()
    {
        var engine = new RecordingEngine();
        var mod = new MovementParams { JumpHeight = 33f, MaxSpeed = 380f };
        var r = Resolve(new FakeRules { Constants = mod });

        Movement.Register("Server", r.Constants, engine, new RecordingLog());

        // RecordingEngine shows the mod's hull and numbers, not 32x32x72 and
        // jump 21: the registered parameters must be the hook's.
        var call = engine.Calls.Single(c => c.Member == "PlayerMovement.RegisterParams");
        var p = call.ArgAs<MovementParams>(0);
        Assert.Equal(33f, p.JumpHeight);
        Assert.Equal(380f, p.MaxSpeed);
        Assert.NotEqual(Movement.Current.JumpHeight, p.JumpHeight);
    }

    [Fact]
    public void Register_without_a_hook_registers_the_stock_parameters()
    {
        var engine = new RecordingEngine();
        var r = Resolve();

        Movement.Register("Server", r.Constants, engine, new RecordingLog());

        var call = engine.Calls.Single(c => c.Member == "PlayerMovement.RegisterParams");
        var p = call.ArgAs<MovementParams>(0);
        Assert.Equal(Movement.Current.JumpHeight, p.JumpHeight);
        Assert.Equal(Movement.Current.ViewZ, p.ViewZ);
        Assert.True(p.HullMaxZ == 72f, "the test mod's hull is still 32x32x72");
    }

    //=========================================================================
    // The mirrored bindings: the same fake hook, seen identically by the
    // server's command binding and the client's prediction binding.
    //=========================================================================

    /// <summary>
    /// A module whose three movement getters answer from ITS side's resolver,
    /// exactly as <c>ServerModule</c> and <c>ClientModule</c> do (the routing
    /// text is pinned by <see cref="Both_modules_answer_the_three_ABI_getters_from_the_resolver"/>).
    /// Every other member is not this fact's and throws.
    /// </summary>
    public class ScaleRoute : DispatchProxy
    {
        public MovementRulesResolver? Resolver;

        protected override object? Invoke(MethodInfo? method, object?[]? a)
        {
            var rules = Resolver!.Rules;
            return method!.Name switch
            {
                "GetJumpScale" => rules.Jump((JumpRequest)a![0]!),
                "GetFrictionScale" => rules.Friction((int)a![0]!, (float)a[1]!, (float)a[2]!, (bool)a[3]!, (int)a[4]!),
                "GetMaxSpeedScale" => rules.MaxSpeed((int)a![0]!, (float)a[1]!, (bool)a[2]!, (int)a[3]!),
                _ => throw new NotSupportedException(method.Name + " is not a movement getter"),
            };
        }
    }

    private static T Module<T>(MovementRulesResolver resolver) where T : class
    {
        var module = DispatchProxy.Create<T, ScaleRoute>();
        ((ScaleRoute)(object)module).Resolver = resolver;
        return module;
    }

    /// <summary>
    /// The pack a binding built in its constructor and hands its loop on
    /// every run. Reflection, because the pack is the binding's own; a binding
    /// that stops keeping one fails here rather than passing vacuously. Of the
    /// binding's side's type: each side compiles its own copy of the shared
    /// interop (plan_shim_areas_split phase 3).
    /// </summary>
    private static TRules PackOf<TRules>(object binding)
    {
        var field = binding.GetType().GetField("_rules", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(field is not null, $"{binding.GetType().Name} keeps its loop's pack in _rules");
        return (TRules)field!.GetValue(binding)!;
    }

    /// <summary>
    /// Each side as the game builds it: its own container, its own instance
    /// of the mod's hook, its own resolver, its module answering the ABI
    /// getters from that resolver — and the REAL binding over that module.
    /// The server's <c>PlayerCommandsBinding</c> and the client's
    /// <c>PredictionClientBinding</c> must hand their loops packs that answer
    /// identically for the same inputs, and the two hook instances must have
    /// been asked the same questions: that is the promise prediction lives on.
    /// </summary>
    [Fact]
    public void The_server_and_client_bindings_answer_one_hook_identically()
    {
        MovementRulesResolver Resolved(FakeRules hook)
        {
            var c = Container();
            c.AddFactory(hook.GetType(), _ => hook);
            return c.Create<MovementRulesResolver>();
        }

        var serverHook = new FakeRules { JumpTo = 0.25f, FrictionTo = 2f, MaxSpeedTo = 128f };
        var clientHook = new FakeRules { JumpTo = 0.25f, FrictionTo = 2f, MaxSpeedTo = 128f };

        var serverBinding = PlayerCommandsBinding.For(
            new Crossings(), new RecordingLog(), null!, Module<IServerModule>(Resolved(serverHook)),
            new PlayerCommandQueue(new RecordingLog()), () => false);
        var clientBinding = PredictionClientBinding.For(
            new Crossings(), null!, Module<client::SourceSharp.Abi.IClientModule>(Resolved(clientHook)),
            client::SourceSharp.Abi.TestSides.Movement(new client::SourceSharp.Abi.ModuleEngine(client::SourceSharp.Abi.TestSides.Create(Side.Client))));

        var server = PackOf<MovementRules>(serverBinding);
        var client = PackOf<client::SourceSharp.Abi.MovementRules>(clientBinding);

        var request = new JumpRequest { PlayerIndex = 3, HorizontalSpeed = 200f };
        float serverJump = server.Jump(request), clientJump = client.Jump(request);
        Assert.Equal(serverJump, clientJump);
        Assert.True(serverJump == 0.25f, "the stimulus is non-trivial: 0.25 is the hook's, not the identity's");

        float serverFriction = server.Friction(3, 4f, 220f, true, 7), clientFriction = client.Friction(3, 4f, 220f, true, 7);
        Assert.Equal(serverFriction, clientFriction);
        Assert.True(serverFriction == 2f, "friction came from the hook");

        float serverSpeed = server.MaxSpeed(3, 320f, false, 7), clientSpeed = client.MaxSpeed(3, 320f, false, 7);
        Assert.Equal(serverSpeed, clientSpeed);
        Assert.True(serverSpeed == 128f, "max speed came from the hook");

        // Both halves reached their own hook, with the arguments the loop hands.
        Assert.Equal(["jump:3", "friction:3,4,220,True,7", "maxspeed:3,320,False,7"], serverHook.Seen);
        Assert.Equal(serverHook.Seen, clientHook.Seen);
    }

    //=========================================================================
    // Source parity, in the style of MoveTypePredictionParityTests: the
    // bindings and both modules call the resolved pack — and no stray scale
    // static remains to call instead.
    //=========================================================================

    // The farm's old layout, read through RepoLayout like every other
    // source-reading fact (SourceFileNamesExistTests indexes these names).
    private const string ServerModuleSource =
        "src/sourcesharp/managed/SourceSharp.Server/ServerModule.cs";
    private const string ClientModuleSource =
        "src/sourcesharp/managed/SourceSharp.Client/ClientModule.cs";
    private const string ServerBindingSource =
        "src/sourcesharp/managed/SourceSharp.Server.Interop/PlayerCommandsBinding.cs";
    private const string ClientBindingSource =
        "src/sourcesharp/managed/SourceSharp.Client.Interop/PredictionClientBinding.cs";
    private const string ManagedMovementSource =
        "src/sourcesharp/managed/SourceSharp.Shared/Movement.cs";

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(RepoLayout.SdkTree, relative));

    [Fact]
    public void No_public_scale_static_survives_in_shared_movement()
    {
        // The plan's grep-fact: the three statics are gone, so nothing can
        // bypass the resolver. Anchor on the whole construct, not a substring.
        var code = Read(ManagedMovementSource);
        Assert.True(Regex.Matches(code, @"public static float \w*Scale\(").Count == 0,
            "no scale static may remain in Movement.cs");
        Assert.True(Regex.Matches(code, @"public static float JumpScale").Count == 0, "no jump scale static may return");
        // The plumbing it replaced is still reachable: Register and the jump
        // arithmetic, so the fact above is about the three and not about the
        // file having been emptied.
        Assert.Contains("public static void Register(string side, MovementParams constants", code,
            StringComparison.Ordinal);
        Assert.Contains("public static float JumpVelocity", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_modules_answer_the_three_ABI_getters_from_the_resolver()
    {
        foreach (var file in new[] { ServerModuleSource, ClientModuleSource })
        {
            var code = Read(file);
            Assert.Contains("_movementRules.Rules.Jump(request)", code, StringComparison.Ordinal);
            Assert.Contains("_movementRules.Rules.Friction(", code, StringComparison.Ordinal);
            Assert.Contains("_movementRules.Rules.MaxSpeed(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("Movement.JumpScale", code, StringComparison.Ordinal);
            Assert.DoesNotContain("Movement.FrictionScale", code, StringComparison.Ordinal);
            Assert.DoesNotContain("Movement.MaxSpeedScale", code, StringComparison.Ordinal);
            Assert.Contains("MovementRulesResolver", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Both_bindings_build_their_pack_over_the_module_getters()
    {
        // The bindings themselves still close over the ABI getters (the plan's
        // shape); what changed is what the getters answer.
        foreach (var file in new[] { ServerBindingSource, ClientBindingSource })
        {
            var code = Read(file);
            Assert.Contains("game.GetJumpScale(in r)", code, StringComparison.Ordinal);
            Assert.Contains("game.GetFrictionScale(i, f, sp, d, m)", code, StringComparison.Ordinal);
            Assert.Contains("game.GetMaxSpeedScale(i, ms, d, m)", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_client_replays_under_the_same_resolved_rules_it_advertises()
    {
        // The replay's loop takes its decisions from the runner, and the
        // client module freezes them from the same resolver the ABI getters
        // answer — one resolver, both client movement paths.
        var runner = Read("src/sourcesharp/managed/SourceSharp.Shared.Interop/MovementRunner.cs");
        Assert.Contains("loop.Rules = ReplayRules;", runner, StringComparison.Ordinal);

        var client = Read(ClientModuleSource);
        Assert.Contains("_movement.ReplayRules = _movementRules.Rules;", client, StringComparison.Ordinal);
    }
}
