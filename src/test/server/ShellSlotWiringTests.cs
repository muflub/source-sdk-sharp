using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <b>The shell game DLL's managed answers come from the module.</b>
///
/// <para>
/// Until plan_engineapi_vtables E17b this class also held the shell's
/// required-slot list against the bootstrap's assignments. That list emptied
/// at E17 (every slot it named is a managed-built area method now, and
/// ManagedAreas_Acquire refuses a load without one), and E17b deleted it with
/// its validator. What follows is the history of why such a check existed.
/// </para>
///
/// <para>
/// <b>The failure this catches has a shape rather than a symptom.</b> Between
/// Phase 2 and Phase 3 all thirteen were declared on both sides and assigned by
/// nobody: the validator would have refused, <c>LoadManagedRuntime</c> would have
/// returned null, <c>DLLInit</c> would have returned false — and none of that was
/// visible, because the shell's <c>EXPOSE_</c> is commented out until Phase 4 and
/// the engine never picks it. The next slot added to that macro has exactly the
/// same window, and this closes it.
/// </para>
///
/// <para>
/// <c>AbiParityTests</c> makes the mirror-image check for the ENGINE table, and
/// says explicitly that the managed half is "assigned from C#, where the compiler
/// already refuses a struct field that does not exist" — true, and it is about
/// the field EXISTING. Nothing checked that anything was put in it.
/// </para>
/// </summary>
public class ShellSlotWiringTests
{
    [Fact]
    public void EveryPartSixThunkAsksTheModuleRatherThanAnsweringWithALiteral()
    {
        // THE FACT THAT WOULD CATCH PART 6 NOT HAVING HAPPENED. Each of these
        // five slots exists so that a mod owns the answer; a thunk that returns
        // a constant compiles, links, wires, validates, and answers -- and is
        // indistinguishable at every other gate from one that reached C#.
        //
        // The subject each resolves is checked too, and that is not pedantry:
        // IServerModule exposes Lifetime, Level, Frames and Listing precisely so
        // a thunk can reach ONE of them, which is what makes Part 13's split of
        // ServerGameDll a move rather than a rewrite. A thunk that reached the
        // module directly would still work and would quietly undo that.
        // The runtime's slot bodies left Entry.cs for the managed-built binding
        // (plan_engineapi_vtables D5), where they read the module as `game`.
        string path = RepoLayout.Repo("src", "server", "interop", "ServerRuntimeBinding.cs");
        Assert.True(File.Exists(path), $"the runtime binding at {path}");

        string code = Regex.Replace(File.ReadAllText(path), @"//[^\n]*", string.Empty);
        code = Regex.Replace(code, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        (string Thunk, string Subject)[] expected =
        {
            ("ServerTickInterval", "Lifetime.TickInterval"),
            ("ServerShouldHideServer", "Listing.ShouldHideServer"),
            ("ServerBrowserMapOverride", "Listing.BrowserMapOverride"),
            ("ServerBrowserGameData", "Listing.BrowserGameData"),
            ("ServerIsManualMapChangeOkay", "Listing.IsManualMapChangeOkay"),
        };

        foreach ((string thunk, string subject) in expected)
        {
            Assert.True(code.Contains($"game.{subject}", StringComparison.Ordinal),
                $"{thunk}'s slot must answer from game.{subject}, not from a literal");
        }
    }

    /// <summary>
    /// plan_gamedll.md Part 10's three, and the subject they resolve.
    ///
    /// <para>
    /// A SEPARATE FACT rather than three rows on the Part 6 one, because that
    /// fact is named after Part 6 and a fact whose name stops describing what it
    /// checks is how a suite becomes unreadable. It also asks a different
    /// question: two of these three are believed unreachable, so what matters is
    /// not that a literal was avoided but that the slot lands on <c>Hosting</c>
    /// — the fifth subject — and not on the module directly, which would work
    /// and would quietly undo the split Part 13 means to make.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryPartTenThunkResolvesTheHostingSubject()
    {
        // The runtime's slot bodies left Entry.cs for the managed-built binding
        // (plan_engineapi_vtables D5), where they read the module as `game`.
        string path = RepoLayout.Repo("src", "server", "interop", "ServerRuntimeBinding.cs");
        Assert.True(File.Exists(path), $"the runtime binding at {path}");

        string code = Regex.Replace(File.ReadAllText(path), @"//[^\n]*", string.Empty);
        code = Regex.Replace(code, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        (string Thunk, string Subject)[] expected =
        {
            ("ServerQueryCvarValueFinished", "Hosting.QueryCvarValueFinished"),
            ("ServerHibernation", "Hosting.SetHibernating"),
            ("ServerStatusLines", "Hosting.StatusLines"),
        };

        foreach ((string thunk, string subject) in expected)
        {
            Assert.True(code.Contains($"game.{subject}", StringComparison.Ordinal),
                $"{thunk}'s slot must reach game.{subject} — the fifth subject, not the module");
        }
    }

    /// <summary>
    /// plan_gamedll_after.md Phase A2 — Part 11a2's three gaps, and the subject
    /// they resolve.
    ///
    /// <para>
    /// A FACT OF ITS OWN rather than three rows on the Part 10 one, for the
    /// reason that fact's own comment gives: a fact named after Part 10 that
    /// checks Part 11a2 is how a suite stops being readable. The question is
    /// also different again — these three were EMPTY BODIES, so what matters
    /// first is that the thunk reaches C# at all, and second that it lands on
    /// <c>Hosting</c> rather than on the module.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryPartElevenA2ThunkResolvesTheHostingSubject()
    {
        // The runtime's slot bodies left Entry.cs for the managed-built binding
        // (plan_engineapi_vtables D5), where they read the module as `game`.
        string path = RepoLayout.Repo("src", "server", "interop", "ServerRuntimeBinding.cs");
        Assert.True(File.Exists(path), $"the runtime binding at {path}");

        string code = Regex.Replace(File.ReadAllText(path), @"//[^\n]*", string.Empty);
        code = Regex.Replace(code, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        (string Thunk, string Subject)[] expected =
        {
            ("ServerInvalidateMdlCache", "Hosting.InvalidateMdlCache"),
            ("ServerSteamGameServerActivated", "Hosting.SteamGameServerActivated"),
            ("ServerSteamGameServerShutdown", "Hosting.SteamGameServerShutdown"),
        };

        foreach ((string thunk, string subject) in expected)
        {
            Assert.True(code.Contains($"game.{subject}", StringComparison.Ordinal),
                $"{thunk}'s slot must reach game.{subject} — the fifth subject, not the module");
        }
    }

    /// <summary>
    /// A cvar the client does not have comes back as NULL, not as "".
    ///
    /// <para>
    /// <b>Found by a mutation that went green.</b> The rule is tested on
    /// <c>ServerGameDll</c> already — but the place the null can be lost is the
    /// bootstrap THUNK, and a thunk is <c>[UnmanagedCallersOnly]</c>, so no
    /// managed test can call it. Dropping the guard in <c>Entry.cs</c> passed
    /// every fact in the suite. <c>Engine.FromUtf8</c> collapses null to
    /// <c>string.Empty</c>, which is right for the cvar NAME and wrong for its
    /// value: "the client has no such cvar" is not "the client has an empty
    /// one", and every reader that tests the string would read a missing
    /// setting as a blank one.
    /// </para>
    ///
    /// <para>
    /// Source-read because there is no other instrument, and stated as what it
    /// is.
    /// </para>
    /// </summary>
    [Fact]
    public void TheCvarQueryThunkKeepsANullValueDistinctFromAnEmptyOne()
    {
        // The runtime's slot bodies left Entry.cs for the managed-built binding
        // (plan_engineapi_vtables D5), where they read the module as `game`.
        string path = RepoLayout.Repo("src", "server", "interop", "ServerRuntimeBinding.cs");
        Assert.True(File.Exists(path), $"the runtime binding at {path}");

        string code = Regex.Replace(File.ReadAllText(path), @"//[^\n]*", string.Empty);
        code = Regex.Replace(code, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        Assert.True(code.Contains("pCvarValue == null ? null :", StringComparison.Ordinal),
            "ServerQueryCvarValueFinished must guard the VALUE against null before "
            + "marshalling it — Engine.FromUtf8 answers string.Empty for null, which "
            + "turns \"the client has no such cvar\" into \"the client has an empty one\"");
    }

    /// <summary>
    /// <c>Status</c>'s printer never crosses, and this is the managed half of
    /// the native fact that says so.
    ///
    /// <para>
    /// Stock hands the game rules a <c>void (*print)( const char *fmt, ... )</c>.
    /// A C variadic function pointer is not a calling convention this boundary
    /// can carry, so the direction inverts: managed returns lines. The slot's
    /// SHAPE is the whole guarantee — a <c>byte*</c> buffer and a written count,
    /// exactly like the three browser strings — and if it ever grew a function
    /// pointer parameter this is what would say so.
    /// </para>
    /// </summary>
    [Fact]
    public void TheStatusSlotCarriesABufferRatherThanAPrinter()
    {
        // The slot is the managed-built runtime's OnStatusLines since
        // plan_engineapi_vtables E1 (the struct's ServerStatusLines went at
        // E-last), read from its generated table (Server.Interop's, written by
        // tools/managed-areas: plan_forwarders_everywhere.md phase 4).
        string code = Regex.Replace(File.ReadAllText(RepoLayout.Repo("src", "server", "interop", "ServerManagedTables.g.cs")),
                                    @"//[^\n]*", string.Empty);

        Assert.True(code.Contains("delegate* unmanaged<void*, byte*, int, int> OnStatusLines",
                                  StringComparison.Ordinal),
            "ServerStatusLines crosses as (buffer, size) -> written count, the same "
            + "convention the three browser strings use — managed returns LINES and "
            + "the shell prints them, because the printer stock passes is variadic");
    }

    [Fact]
    public void TheDefaultTickIntervalHasOneDefinition()
    {
        // Two assemblies need the same fallback -- ServerGameDll when -tickrate
        // did not move it, and the bootstrap when there is no server module at
        // all -- and SourceSharp.Bootstrap references only SourceSharp.Abi, so
        // the obvious thing to write is a second `0.015f`. Two copies of a
        // constant is two things that can drift, and a tick interval that
        // differs by assembly is a server whose durations mean different things
        // depending on which path answered.
        Assert.Equal(0.015f, IServerLifetime.DefaultTickInterval);
        Assert.Equal(IServerLifetime.DefaultTickInterval, ServerGameDll.DefaultTickInterval);

        // COMMENT-STRIPPED, and this line is here because the first version was
        // not and could not fail: the doc comment above the alias says "An ALIAS
        // for IServerLifetime.DefaultTickInterval", so a raw search found the
        // PROSE and passed against a body that had gone back to a literal 0.015f.
        // Caught by mutating exactly that, which is what the mutation pass is
        // for; recorded here so the stripping does not get tidied away again.
        string? path = RepoLayout.FindSdkFile(Path.Combine("src", "sourcesharp", "managed",
                                                 "SourceSharp.Server", "ServerGameDll.cs"));
        Assert.NotNull(path);

        string body = Regex.Replace(File.ReadAllText(path!), @"//[^\n]*", string.Empty);
        body = Regex.Replace(body, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        Assert.Contains("IServerLifetime.DefaultTickInterval", body, StringComparison.Ordinal);
    }
}
