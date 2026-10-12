extern alias client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE THREE KNOBS THE RELOCATED LEAF-MATH FILES USED TO DECLARE THEMSELVES.
///
/// <para>
/// plan_shim.md Phase 2d and §6. <c>anim_3wayblend</c> and <c>aim_constraint</c>
/// were <c>static ConVar</c>s in <c>public/bone_setup.cpp</c> and
/// <c>mod_load_showstall</c> one in <c>public/studio.cpp</c>. None is a name
/// the engine resolves or transmits, so none may be declared natively; all
/// three are <c>[ConVar]</c> classes now and the native readers resolve them
/// out of the engine's registry through <c>CManagedConVarRef</c>.
/// </para>
///
/// <para>
/// <b>WHAT COULD BE SILENTLY WRONG, and is what each fact below is about.</b>
/// Two of the three carry <c>FCVAR_REPLICATED</c>, which means nothing at all
/// unless BOTH DLLs register the name — a replicated ConVar only one side
/// declares stops tracking the server, with no error. And the native files
/// compile into both DLLs, so a declaration that moved to one side only would
/// leave the other side's <c>CManagedConVarRef</c> permanently unresolved,
/// which reads as the knob being OFF rather than as the knob being missing.
/// </para>
///
/// <para>
/// THE PATHS ARE RESOLVED FROM A MARKER INSIDE THIS WORKTREE AND PRINTED ON
/// FAILURE. A helper that walks UP looking for the file itself escapes a git
/// worktree into the main checkout — every tracked file exists in both trees,
/// and a worktree's <c>.git</c> is a file rather than a directory — so an
/// absence check written that way reads the OTHER tree's copy and answers
/// confidently about a repository nobody is editing. This anchors on
/// <c>src/buildsourcesharp</c>, which exists in the worktree, and then indexes
/// down.
/// </para>
/// </summary>
public class LeafMathConVarTests
{
    // bone_setup.cpp's rows left with the file (plan_physics_bones_native_zero
    // Phase 3): anim_3wayblend and aim_constraint have no native reader now;
    // SourceSharp.Shared.Bones takes anim_3wayblend as an argument.
    private const string Studio = "src/sourcesharp/public/studio.cpp";

    private static string Read(string relative)
    {
        string path = Path.Combine(RepoLayout.SdkTree, relative);

        Assert.True(File.Exists(path), "not on disk: " + path);

        return File.ReadAllText(path);
    }

    private static string Code(string relative)
    {
        string text = Read(relative);

        // Line comments out. Every needle below is code, and the banners these
        // two files now carry NAME all three convars in prose while saying they
        // are declared elsewhere.
        return string.Join("\n",
            text.Split('\n').Select(line =>
            {
                int at = line.IndexOf("//", StringComparison.Ordinal);
                return at >= 0 ? line[..at] : line;
            }));
    }

    private static Dictionary<string, ConVarBase> DeclaredIn(System.Reflection.Assembly assembly)
    {
        var registry = new ConVarRegistry(TestServices.UnattachedConsole());
        registry.RegisterAll(assembly);

        var byName = new Dictionary<string, ConVarBase>();

        foreach (var c in registry.All)
        {
            if (c.GetType().Assembly != assembly)
                continue;

            Assert.True(byName.TryAdd(c.Name, c),
                assembly.GetName().Name + " declares '" + c.Name + "' twice; Source "
                + "refuses a duplicate name from one DLL, so one of them would not exist");
        }

        return byName;
    }

    private static Dictionary<string, ConVarBase> Server()
        => DeclaredIn(typeof(SourceSharp.Server.AnimThreeWayBlendConVar).Assembly);

    private static Dictionary<string, ConVarBase> Client()
        => DeclaredIn(typeof(client::SourceSharp.Client.ClAnimThreeWayBlendConVar).Assembly);

    public static IEnumerable<object[]> TheReplicatedTwo() =>
    [
        ["anim_3wayblend"],
        ["aim_constraint"],
    ];

    //=========================================================================
    // The managed declarations.
    //=========================================================================

    [Theory]
    [MemberData(nameof(TheReplicatedTwo))]
    public void TheServerDeclaresIt(string name)
        => Assert.True(Server().ContainsKey(name),
            "the Server assembly does not declare " + name + ", which "
            + "bone_setup.cpp/studio.cpp used to declare and now only READS. An "
            + "unresolved CManagedConVarRef answers zero, which reads as the "
            + "knob being off rather than as the declaration being gone.");

    [Theory]
    [MemberData(nameof(TheReplicatedTwo))]
    public void TheClientDeclaresIt(string name)
        => Assert.True(Client().ContainsKey(name),
            "the Client assembly does not declare " + name + ". Both files are "
            + "compiled into BOTH game DLLs, so both declared both "
            + "before this move.");

    [Theory]
    [MemberData(nameof(TheReplicatedTwo))]
    public void TheReplicatedFlagSurvivedOnBothSides(string name)
    {
        Assert.True(Server()[name].Flags.HasFlag(ConVarFlags.Replicated),
            name + " was FCVAR_REPLICATED natively and is not Replicated in the "
            + "Server assembly.");

        Assert.True(Client()[name].Flags.HasFlag(ConVarFlags.Replicated),
            name + " is not Replicated on the client side. A replicated ConVar "
            + "only one side declares stops tracking the server, silently.");
    }

    [Fact]
    public void TheDiagnosticIsTheEnginesAndDeclaredByNeitherSide()
    {
        // mod_load_showstall is the engine's own name (no game-DLL flag in the
        // live table), so a managed declaration would alias it
        // (convar_ownership.h). studio.cpp reads the engine's by name.
        Assert.False(Server().ContainsKey("mod_load_showstall"));
        Assert.False(Client().ContainsKey("mod_load_showstall"));
    }

    [Theory]
    [MemberData(nameof(TheReplicatedTwo))]
    public void TheTwoSidesAgreeOnTheDefaultAndTheFlags(string name)
    {
        ConVarBase s = Server()[name];
        ConVarBase c = Client()[name];

        Assert.Equal(s.DefaultValue, c.DefaultValue);
        Assert.Equal(s.Flags, c.Flags);
    }

    [Fact]
    public void TheDefaultsAreTheOnesTheNativeDeclarationsCarried()
    {
        Assert.Equal("1", Server()["anim_3wayblend"].DefaultValue);
        Assert.Equal("1", Server()["aim_constraint"].DefaultValue);
    }

    //=========================================================================
    // The native side: it READS them and no longer DECLARES them.
    //=========================================================================

    [Theory]
    [InlineData(Studio, "mod_load_showstall")]
    public void TheNativeFileHoldsAManagedRefAndNotADeclaration(string file, string name)
    {
        string code = Code(file);

        Assert.Contains("CManagedConVarRef " + name + "( \"" + name + "\" )", code,
            StringComparison.Ordinal);

        Assert.DoesNotContain("ConVar " + name + "( \"" + name + "\"", code,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Studio)]
    public void TheNativeFileDeclaresNoConVarAtAll(string file)
    {
        string code = Code(file);

        // `static ConVar ` is the declaration form; CManagedConVarRef and
        // ConVarRef are reads and are not this.
        Assert.DoesNotContain("static ConVar ", code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Studio, "mod_load_showstall")]
    public void TheReadersAreStillThere(string file, string name)
    {
        // The point of the move is that the RULE is unchanged and only the
        // DECLARATION left. A file that had lost its reads would pass every
        // absence assertion above.
        Assert.Contains(name + ".Get", Code(file), StringComparison.Ordinal);
    }

    //=========================================================================
    // The path resolution itself, because an absence check that read the wrong
    // tree would pass every one of the assertions above.
    //=========================================================================

    [Fact]
    public void TheRootIsThisWorktreeAndNotWhateverIsAboveIt()
    {
        string root = RepoLayout.SdkTree;

        // The test binary lives under this repository's root. If the walk had
        // gone past the worktree into a checkout above it, the binary would not
        // be below the answer. The tree the facts read is the compat farm,
        // which is under that root unless $SOURCESHARP_SDK_TREE moved it.
        Assert.StartsWith(RepoLayout.RepoRoot, AppContext.BaseDirectory, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "src", "buildsourcesharp")),
            "the marker is not under the root this resolved to: " + root);
    }

    /// <summary>
    /// THE ESCAPE, DEMONSTRATED ON A REAL FILE RATHER THAN DESCRIBED.
    ///
    /// <para>
    /// <c>src/public/stringregistry.cpp</c> was deleted by this branch two
    /// commits ago and still exists in the main checkout this worktree hangs
    /// off. A helper that walks UP from the test binary looking for the file
    /// ITSELF therefore finds it — in a tree nobody is editing — and an
    /// absence check written that way answers "still there". It does not
    /// error; it answers confidently about the wrong repository.
    /// </para>
    ///
    /// <para>
    /// This fact asserts the two halves that matter: the anchored resolution
    /// says the file is absent, and a walking resolution is shown to be
    /// capable of disagreeing. If the walk stops at this worktree too — which
    /// it will when this branch is merged and the main checkout no longer has
    /// the file — the second half is skipped rather than inverted, because the
    /// claim being made is about the HELPER, not about the main checkout.
    /// </para>
    /// </summary>
    [Fact]
    public void AnAnchoredAbsenceCheckDoesNotEscapeIntoTheCheckoutAbove()
    {
        const string deleted = "src/public/stringregistry.cpp";

        string anchored = Path.Combine(RepoLayout.SdkTree, deleted);

        Assert.False(File.Exists(anchored),
            "this branch deleted " + deleted + " and the anchored path still "
            + "resolves to a file: " + anchored);

        // The walking form, written out so the difference is visible.
        string? walked = null;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, deleted);

            if (File.Exists(candidate))
            {
                walked = candidate;
                break;
            }
        }

        if (walked is not null)
        {
            Assert.False(walked.StartsWith(RepoLayout.SdkTree, StringComparison.Ordinal),
                "a file the anchored check says is absent was found INSIDE this "
                + "worktree by the walking form, which means RepoLayout.SdkTree is wrong: "
                + walked);
        }
    }

    [Fact]
    public void AMissingFileFailsAndSaysWhichPathItLookedAt()
    {
        // The check that cannot fail, made able to fail: Read() must refuse a
        // path that is not there rather than finding one somewhere else. The
        // failure message carries the RESOLVED path, which is the only thing
        // that tells a reader which tree was searched.
        var ex = Assert.ThrowsAny<Xunit.Sdk.XunitException>(
            () => Read("src/sourcesharp/public/this_file_does_not_exist.cpp"));

        Assert.Contains("this_file_does_not_exist.cpp", ex.Message, StringComparison.Ordinal);
        Assert.Contains(RepoLayout.SdkTree, ex.Message, StringComparison.Ordinal);
    }
}
