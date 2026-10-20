using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Part 0a #3's managed half: which declaration a command-line value may set,
/// and what its declared default is.
///
/// <para>
/// The shell's <c>RegisterConCommandBase</c> does one of stock's three steps —
/// linking the command into the engine's list. The other two ask questions about
/// a <i>declaration</i>, and every declaration lives in
/// <see cref="ConVarRegistry"/>. Dropping them means <c>+sv_cheats 1</c> and
/// every <c>+convar value</c> a server operator passes is silently ignored.
/// </para>
/// </summary>
public class ConVarCommandLineTests
{
    [ConVar("ss_test_cl_plain", "7", Help = "a plain declaration")]
    private sealed class PlainConVar : IntConVar { }

    [ConVar("ss_test_cl_replicated", "3", Flags = ConVarFlags.Replicated)]
    private sealed class ReplicatedConVar : IntConVar { }

    [ConVar("ss_test_cl_unregistered", "1", Flags = ConVarFlags.Unregistered)]
    private sealed class UnregisteredConVar : IntConVar { }

    [ConVar("ss_test_cl_cheat", "0", Flags = ConVarFlags.Cheat)]
    private sealed class CheatConVar : IntConVar { }

    private static ConVarRegistry Registry()
    {
        var registry = ConVarRegistry.For(TestServices.UnattachedConsole());
        registry.RegisterAll(typeof(ConVarCommandLineTests).Assembly);
        return registry;
    }

    [Fact]
    public void ADeclarationTheRegistryMadeIsAnswerable()
    {
        Assert.True(Registry().TryGetCommandLinePolicy("ss_test_cl_plain", out _),
            "the shell can ask about a convar this side declared");
    }

    [Fact]
    public void ANameTheRegistryNeverDeclaredIsNotAnswered()
    {
        Assert.True(!Registry().TryGetCommandLinePolicy("sv_cheats", out _),
            "a native convar is not this registry's to answer for, and it says so rather than guessing");
    }

    /// <summary>
    /// The engine looks a convar up by name and the comparison is
    /// case-insensitive, so this one has to be too — otherwise
    /// <c>+SV_CHEATS 1</c> would take a different path from <c>+sv_cheats 1</c>.
    /// </summary>
    [Fact]
    public void TheLookupIsCaseInsensitiveLikeTheEnginesOwn()
    {
        Assert.True(Registry().TryGetCommandLinePolicy("SS_TEST_CL_PLAIN", out _),
            "the name matches the way the engine matches it");
    }

    [Fact]
    public void APlainDeclarationMayTakeACommandLineValue()
    {
        Registry().TryGetCommandLinePolicy("ss_test_cl_plain", out var policy);

        Assert.True(policy.MayTakeCommandLineValue, "+ss_test_cl_plain 9 is allowed to take");
    }

    [Fact]
    public void ADeclarationCarriesItsDeclaredDefault()
    {
        Registry().TryGetCommandLinePolicy("ss_test_cl_plain", out var policy);

        Assert.True(policy.DefaultValue == "7", "and the registry knows what the class declared");
    }

    /// <summary>
    /// Cheat-flagged convars take command-line values in stock, and that is not
    /// an oversight: <c>+sv_cheats 1</c> is how a cheat-enabled server is
    /// started, and <c>sv_cheats</c> gates the rest at use rather than at
    /// registration.
    /// </summary>
    [Fact]
    public void ACheatDeclarationMayStillTakeACommandLineValue()
    {
        Registry().TryGetCommandLinePolicy("ss_test_cl_cheat", out var policy);

        Assert.True(policy.MayTakeCommandLineValue, "a cheat convar is still settable from the command line");
    }

    /// <summary>
    /// An unregistered convar is never in the engine's list, so a command-line
    /// value applied to it would write something <c>cvar-&gt;FindVar</c> can
    /// never find — a setting that appears to take and does nothing.
    /// </summary>
    [Fact]
    public void AnUnregisteredDeclarationMayNotTakeACommandLineValue()
    {
        Registry().TryGetCommandLinePolicy("ss_test_cl_unregistered", out var policy);

        Assert.True(!policy.MayTakeCommandLineValue,
            "a convar the engine never lists cannot be set by name from the command line");
    }

    /// <summary>
    /// Stock re-applies the declared default to a REPLICATED convar after
    /// linking, so the server's value wins. The bug it fixes: a shared convar
    /// whose value came from inside an <c>#if defined(_DEBUG)</c> block, so a
    /// debug game DLL against a release client took the client's number.
    /// </summary>
    [Fact]
    public void AReplicatedDeclarationHasADefaultToRestore()
    {
        Registry().TryGetCommandLinePolicy("ss_test_cl_replicated", out var policy);

        Assert.True(policy.ReplicatedDefault == "3",
            "with no command-line value, a replicated convar goes back to the server's declared default");
    }

    /// <summary>
    /// And an unreplicated one has none, which is the other half of stock's rule
    /// rather than a simplification of it: restoring the default on every convar
    /// would clobber whatever a config had already set.
    /// </summary>
    [Fact]
    public void AnUnreplicatedDeclarationHasNothingToRestore()
    {
        Registry().TryGetCommandLinePolicy("ss_test_cl_plain", out var policy);

        Assert.True(policy.ReplicatedDefault is null,
            "an unreplicated convar keeps whatever a config gave it");
    }

    [Fact]
    public void ADeclarationSaysWhetherItIsReplicated()
    {
        Registry().TryGetCommandLinePolicy("ss_test_cl_replicated", out var policy);

        Assert.True(policy.IsReplicated, "the flag the shell would otherwise have to re-read off the ConVar");
    }

    [Fact]
    public void AnEmptyNameIsNotADeclaration()
    {
        Assert.True(!Registry().TryGetCommandLinePolicy("", out _),
            "an empty name answers no rather than matching whatever happens to be first");
    }

    /// <summary>
    /// The policy comes from the ATTRIBUTE, not from the engine's reply, so it is
    /// there whatever the engine said.
    ///
    /// <para>
    /// <b>What this does NOT pin, said plainly.</b> The registry records a
    /// declaration BEFORE calling <c>Engine.RegisterConVar</c>, and that ordering
    /// is load-bearing: a <c>ConVar</c> constructed after <c>ConVar_Register</c>
    /// has run does not wait in a list — <c>ConCommandBase::Init</c> hands it
    /// straight to the registered accessor, which is the shell's
    /// <c>RegisterConCommandBase</c>, which asks this registry. Recorded after
    /// the call, every managed declaration would take the unknown-name path.
    /// <b>No managed test can see that</b>: nothing native is loaded in a test
    /// process, so <c>Engine.RegisterConVar</c> never calls back and the ordering
    /// is unobservable from here. It is argued in ConVar.cs and recorded in
    /// docs/gamedll-ledger.md as an untested claim, and Phase 5 — which wires the
    /// shell to ask — is where it becomes checkable.
    /// </para>
    /// </summary>
    [Fact]
    public void ADeclarationIsAnswerableEvenWhenTheEngineRefusedTheConVar()
    {
        var registry = Registry();

        // Nothing native is loaded in a test process, so every registration is
        // refused: IsRegistered is false for all of them. The policy must be
        // answerable anyway, because it is recorded from the ATTRIBUTE and not
        // from the engine's reply.
        var declared = registry.Get<PlainConVar>();

        Assert.True(declared is not null && !declared.IsRegistered
                    && registry.TryGetCommandLinePolicy("ss_test_cl_plain", out _),
            "the policy comes from the declaration, so it survives an engine that refused the convar");
    }

    [Fact]
    public void EveryDeclarationIsListed()
    {
        var registry = Registry();
        var names = registry.CommandLinePolicies.Select(p => p.Name).ToArray();

        Assert.True(names.Contains("ss_test_cl_plain")
                 && names.Contains("ss_test_cl_replicated")
                 && names.Contains("ss_test_cl_unregistered")
                 && names.Contains("ss_test_cl_cheat"),
            "a tool can list what this side declared and what each one allows");
    }
}
