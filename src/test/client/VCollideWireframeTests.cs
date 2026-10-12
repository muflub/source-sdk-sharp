using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Client;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>vcollide_wireframe</c> — the one ConVar Phase 1c could not move, and what
/// moving it needed.
///
/// <para>
/// <b>WHY IT WAS STUCK.</b> <c>plan_shim.md</c> §6's rule is that no ConVar's
/// default or semantics is declared natively, and every other tenant of
/// <c>shell_entitystatics.cpp</c> went. This one did not, for one reason: the
/// toggle DRAWS NOTHING BY ITSELF. Its whole effect is its change callback,
/// which forces every renderable to re-evaluate whether the client leaf system
/// should be showing it — and a managed <c>[ConVar]</c> carries a managed
/// <c>OnChanged</c> with no way to say that. There was no ABI slot for
/// "re-evaluate every client shell's visibility". Lane 1e-abi appended one.
/// </para>
///
/// <para>
/// <b>WHAT THESE FACTS ARE FOR.</b> A managed declaration whose
/// <c>OnChanged</c> reaches nothing is strictly WORSE than leaving the ConVar
/// native: the console variable exists, the console reports the new value, and
/// nothing re-evaluates. That is the silent wrong answer, not a missing
/// feature, so the sweep is asserted rather than assumed — and the unwired case
/// is asserted too, because the seam is a nullable delegate and an unwired one
/// is exactly what a module that forgot to install it would leave behind.
/// </para>
/// </summary>
public class VCollideWireframeTests
{
    /// <summary>The engine double and the convar, wired the way the module wires it.</summary>
    private static (RecordingEngine Engine, VCollideWireframeConVar ConVar) Rig(int swept = 3)
    {
        var engine = TestServices.Fake();
        engine.Returns("Entities.UpdateVisibilityAll", swept);

        var convar = new VCollideWireframeConVar();
        convar.Sweep = ((IEngineEntities)engine).UpdateVisibilityAll;

        return (engine, convar);
    }

    /// <summary>
    /// The engine's own route: its change callback hands the registry a raw
    /// string and the typed layer re-parses and calls <c>OnChanged</c>. Driven
    /// that way here rather than through <c>SetValue</c>, which goes OUT to an
    /// engine there is none of.
    /// </summary>
    private static void Flip(VCollideWireframeConVar convar, string to)
        => convar.NotifyChanged(to, to == "1" ? "0" : "1");

    /// <summary>
    /// The DECLARATION, read off the attribute rather than off an instance:
    /// <c>Name</c>, <c>Flags</c> and <c>DefaultValue</c> on a
    /// <see cref="ConVarBase"/> are filled in at registration, so an unbound
    /// instance answers empty for all three and a fact asserting on one would
    /// be asserting on the test's own construction.
    /// </summary>
    private static ConVarAttribute Declaration()
    {
        var attribute = typeof(VCollideWireframeConVar).GetCustomAttribute<ConVarAttribute>();

        Assert.True(attribute is not null,
            "VCollideWireframeConVar carries no [ConVar], so it is discovered by "
            + "nothing and the console variable does not exist");

        return attribute!;
    }

    [Fact]
    public void TheToggleIsDeclaredInManagedCode()
        => Assert.Equal("vcollide_wireframe", Declaration().Name);

    /// <summary>
    /// Stock's flag, unchanged by the move: FCVAR_CHEAT is the whole of what
    /// stops a player turning this on in a normal game.
    /// </summary>
    [Fact]
    public void ItKeepsStocksCheatFlag()
        => Assert.True(Declaration().Flags.HasFlag(ConVarFlags.Cheat),
            "vcollide_wireframe lost FCVAR_CHEAT in the move to managed code");

    [Fact]
    public void ItKeepsStocksDefaultOfOff()
        => Assert.Equal("0", Declaration().DefaultValue);

    /// <summary>
    /// THE POINT OF THE WHOLE SLOT. A change sweeps; without this the managed
    /// declaration is a console variable that reports a new value and changes
    /// nothing at all.
    /// </summary>
    [Fact]
    public void AChangeSweepsEveryClientShell()
    {
        var (engine, convar) = Rig();

        Flip(convar, "1");

        Assert.True(engine.Called("Entities.UpdateVisibilityAll"),
            "turning vcollide_wireframe on did not ask anything to re-evaluate "
            + "visibility, so the toggle reports a new value and draws the same "
            + "thing");
    }

    /// <summary>
    /// And turning it OFF sweeps too. Stock's callback did not test the new
    /// value either — the renderables have to re-evaluate in both directions or
    /// the wireframe stays on screen until something else moves.
    /// </summary>
    [Fact]
    public void TurningItOffSweepsAsWell()
    {
        var (engine, convar) = Rig();

        Flip(convar, "1");
        engine.Clear();
        Flip(convar, "0");

        Assert.True(engine.Called("Entities.UpdateVisibilityAll"),
            "turning vcollide_wireframe off did not sweep, so the wireframe "
            + "stays drawn until something else makes a renderable re-evaluate");
    }

    [Fact]
    public void OneSweepPerChangeAndNotOnePerRead()
    {
        var (engine, convar) = Rig();

        Flip(convar, "1");
        _ = convar.Value;
        _ = convar.Value;

        Assert.Equal(1, engine.CountOf("Entities.UpdateVisibilityAll"));
    }

    /// <summary>
    /// The count the slot answers is kept, and that is not decoration: a sweep
    /// changes what the leaf system does NEXT frame, so nothing about the call
    /// itself is observable and a sweep of nothing would otherwise be
    /// indistinguishable from a sweep that never ran.
    /// </summary>
    [Fact]
    public void ItRecordsHowManyShellsTheSweepReached()
    {
        var (_, convar) = Rig(swept: 7);

        Flip(convar, "1");

        Assert.Equal(7, convar.LastSweep);
    }

    /// <summary>
    /// UNWIRED IS A NO-OP AND NOT A CRASH. The seam is a nullable delegate
    /// because a <c>[ConVar]</c> class is constructed with no arguments — the
    /// registry registers the convars before the services exist — so there is a
    /// window, and a change inside it must do nothing rather than throw into
    /// the engine's change callback.
    /// </summary>
    [Fact]
    public void AChangeBeforeTheSweepIsInstalledDoesNothing()
    {
        var convar = new VCollideWireframeConVar();

        Flip(convar, "1");

        Assert.Equal(0, convar.LastSweep);
        Assert.True(convar.Value, "and the value still took");
    }

    //-------------------------------------------------------------------------
    // THE SOURCES -- the half a double cannot see.
    //
    // Every fact above passes against an ABI slot that is declared and never
    // assigned, because the double answers whatever the double answers. An
    // unwired slot is a null function pointer Engine.cs skips in silence: the
    // convar would flip, OnChanged would run, the sweep delegate would be
    // called, and zero shells would re-evaluate.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheGamesystemWiresTheVisibilitySweepSlot()
    {
        // plan_client_vtables CC-last: wired means the client's shim area
        // forwards the slot to its body (the fill that assigned it is gone).
        string forwards = Source("src", "sourcesharp", "game", "shared", "shell_shim_areas_client.inc");

        Assert.Contains("CShimViewClient::EntitiesUpdateVisibilityAll", forwards);
        Assert.Contains("::SourceSharp_ClientEntitiesUpdateVisibilityAll(", forwards);
    }

    /// <summary>
    /// And the native body it is wired to still walks the registry. A slot
    /// assigned to a body that swept nothing would pass everything above.
    /// </summary>
    [Fact]
    public void TheNativeSweepWalksTheStandaloneRegistry()
    {
        string body = Source("src", "sourcesharp", "game", "client", "shell_entitystatics.cpp");

        Assert.Contains("int32_t SourceSharp_ClientEntitiesUpdateVisibilityAll( void )", body);
        Assert.Contains("SourceSharp_StandaloneClientCount()", body);
        Assert.Contains("UpdateVisibility()", body);
    }

    /// <summary>
    /// NOTHING DECLARES IT NATIVELY ANY MORE. A second registration of one name
    /// is neither a compile error nor a link error: the console sets one object
    /// and the reader is bound to the other, and as
    /// <c>util_shared_library.cpp</c>'s note on <c>developer</c> records,
    /// "neither the console nor the game says which one it read".
    /// </summary>
    [Fact]
    public void TheShimNoLongerDeclaresItNatively()
    {
        string shim = Source("src", "sourcesharp", "game", "client", "shell_entitystatics.cpp");

        Assert.DoesNotContain("ConVar vcollide_wireframe(", shim);
    }

    /// <summary>
    /// The native reader resolves it BY NAME. An <c>extern</c> binds to a
    /// DEFINITION and a managed declaration is not one, so under SOURCESHARP an
    /// extern would not link at all — and a <c>ConVarRef</c> built once at DLL
    /// load would cache the miss and answer 0 for the life of the process.
    ///
    /// <para>
    /// viewrender.cpp read it through a CManagedConVarRef until it left
    /// client.so at C7 step 3; the reader is the render plan's executor now,
    /// which reads its whole convar table by name at each frame (a ConVarRef
    /// made at the read) into the plan's inputs, where ViewDrawProgram hands it
    /// to both static-prop batches.
    /// </para>
    /// </summary>
    [Fact]
    public void TheNativeReaderResolvesItByNameAtTheRead()
    {
        string text = Source("src", "sourcesharp", "game", "client", "shell_view_executor_client.cpp");
        int table = text.IndexOf("const char *const kRenderCvars[] =", StringComparison.Ordinal);
        Assert.True(table > 0, "the executor has no render convar table");
        int end = text.IndexOf("};", table, StringComparison.Ordinal);
        Assert.Contains("\"vcollide_wireframe\"", text[table..end]);
        Assert.Contains("ConVarRef var( kRenderCvars[i], true );", text);
    }

    private static string Source(params string[] parts)
    {
        return File.ReadAllText(RepoLayout.Sdk(parts));
    }
}
